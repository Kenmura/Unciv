using Unciv.Assets;
using Unciv.Client.Net.Session;
using Unciv.Commands;
using Unciv.Commands.Events;
using Unciv.Commands.Turn;
using Unciv.Models.Ruleset;
using Unciv.Net;
using Unciv.Net.Remote;
using Unciv.Net.Visibility;
using Unciv.Queries;
using Unciv.Server;

namespace Unciv.GodotClient.Play;

/// <summary>
/// T244: the network implementation of <see cref="IGameSession"/> over a joined <see cref="ClientSession"/> (T180). The mirror is the session's snapshot-plus-deltas
/// mirror: nothing is rebuilt per command (the single-player LocalHost rebuilds a full snapshot), a <see cref="DeltaApplied"/> replaces <see cref="Snapshot"/> and its events reach
/// <see cref="EventsReceived"/> numbered by command sequence. The session's own <see cref="Desynchronized"/> (sequence gap, bad diff, reconnect) is passed on as
/// <see cref="Desynchronized"/>; the <see cref="ClientSession"/> resyncs by itself and the new snapshot arrives through <see cref="SnapshotChanged"/>.
/// <para>Ownership: the caller owns the <see cref="ClientSession"/> (join, lobby, leave, dispose). This class takes over reading <see cref="ClientSession.Events"/> once the game
/// started (the channel has a single reader, so the lobby UI must stop reading it first) and needs the first snapshot to exist.</para>
/// <para>Events are raised on the <see cref="SynchronizationContext"/> current at construction (the Godot main thread); with none they are raised on the pump thread.
/// With none, a handler that throws faults the pump, and every pending and later call then faults with that exception (under a context the exception goes to the context).</para>
/// <para>End turn over the network: the host plays the other civs on its own schedule, so <see cref="EndTurnAsync"/> submits <see cref="EndTurn"/> and completes when the
/// mirror shows a later turn (or the viewer's civ is defeated). <see cref="TurnOutcome.Events"/> holds the events of the End turn command itself; everything that follows
/// (other civs, the next turn's start) arrives through <see cref="EventsReceived"/>, and <c>AiTurnsRun</c> is 0 because the host does not report it. Cancelling stops waiting
/// (a refusal "cancelled", also while the submit itself is waiting); `progress` is unused; the host keeps the turn ended, and calling again waits for the next turn without submitting End turn a second time.
/// <see cref="ExportSaveAsync"/> is null: the host saves.</para>
/// </summary>
public sealed class NetGameSession : IGameSession, ITurnRoster, IGameSocial, IAsyncDisposable
{
    /// <summary>How long a submit waits for the delta that carries its events after the host accepted it; past it the events are reported empty (the snapshot still catches up).</summary>
    private static readonly TimeSpan EventWait = TimeSpan.FromSeconds(30);

    /// <summary>Batches kept for submits that wait for theirs; older ones can no longer be asked for.</summary>
    private const int KeptBatches = 256;

    private readonly ClientSession session;
    private readonly SynchronizationContext? context;
    private readonly CancellationTokenSource stop = new();
    private readonly Task pump;
    private readonly Lock gate = new();
    private readonly SortedDictionary<long, IReadOnlyList<GameEvent>> batches = [];
    private TaskCompletionSource changed = NewSignal();
    private Exception? failure;
    private volatile ClientSnapshot snapshot;
    private int endingTurn;
    private int replacedCount; // full snapshots applied so far; a resync waits for it to advance

    private readonly InProcessHost? hostedBy;
    private IReadOnlyList<ChatLine> chat = [];

    /// <param name="session">the joined session; the caller owns it.</param>
    /// <param name="ruleset">the ruleset of the game.</param>
    /// <param name="hostedBy">C045: the host this process runs when the viewer is the host player (it enables kick and rebind); null for a joined friend.</param>
    public NetGameSession(ClientSession session, Ruleset ruleset, InProcessHost? hostedBy = null)
    {
        this.hostedBy = hostedBy;
        this.session = session;
        Ruleset = ruleset;
        snapshot = session.Game.Snapshot ?? throw new InvalidOperationException("The game has not started: there is no snapshot yet");
        ViewerCivId = snapshot.You;
        if (session.State == ClientSessionState.Reconnecting) Connection = ClientSessionState.Reconnecting;
        context = SynchronizationContext.Current;
        pump = PumpAsync();
    }

    public string ViewerCivId { get; }

    public Ruleset Ruleset { get; }

    public ClientSnapshot Snapshot => snapshot;

    public event Action<ClientSnapshot>? SnapshotChanged;

    public event Action<EventBatch>? EventsReceived;

    public event Action<string>? Desynchronized;

    /// <summary>C036: the humans of the game from the host's latest lobby view (slots of kind Human with a nation), in slot order; the host re-sends it when somebody connects or drops.</summary>
    public IReadOnlyList<TurnRosterEntry> Roster => RosterOf(session.Lobby);

    public event Action? RosterChanged;

    /// <summary>C006: where the connection is (<see cref="ClientSessionState.Reconnecting"/> while it is down), whether the host announced it is closing, and why the session ended for good (null while it lives).
    /// Read these from the main thread; <see cref="ConnectionChanged"/> follows every change there.</summary>
    public ClientSessionState Connection { get; private set; } = ClientSessionState.InGame;

    public bool HostClosing { get; private set; }

    public string? EndedReason { get; private set; }

    public event Action? ConnectionChanged;

    public IReadOnlyList<ChatLine> Chat
    {
        get { lock (gate) return chat; }
    }

    public event Action<ChatLine>? ChatReceived;

    public event Action? PlayersChanged;

    public IReadOnlyList<PlayerRow> Players => SocialModel.Players(session.Lobby, ViewerCivId, IsHost);

    public bool IsHost => hostedBy != null;

    public async Task SendChatAsync(string text)
    {
        if (SocialModel.Clean(text) is { } clean) await session.SendChatAsync(clean).ConfigureAwait(false);
    }

    public Task<string?> KickAsync(string civId) =>
        hostedBy?.KickAsync(civId) ?? Task.FromResult<string?>("not-allowed");

    public Task<string?> RebindAsync(string civId, string newPlayerKey) =>
        hostedBy?.RebindAsync(civId, newPlayerKey) ?? Task.FromResult<string?>("not-allowed");

    private static List<TurnRosterEntry> RosterOf(LobbyState? lobby) => lobby == null ? [] :
    [
        .. lobby.Slots
            .Where(it => it.Kind == "Human" && !string.IsNullOrEmpty(it.Nation))
            .Select(it => new TurnRosterEntry(it.Nation!, string.IsNullOrEmpty(it.PlayerName) ? it.Nation! : it.PlayerName, it.Connected ?? false))
    ];

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    // ------------------------------------------------------------ the pump

    private async Task PumpAsync()
    {
        try
        {
            await foreach (var clientEvent in session.Events.ReadAllAsync(stop.Token).ConfigureAwait(false))
            {
                switch (clientEvent)
                {
                    case SnapshotReplaced replaced:
                        Apply(replaced.Snapshot, null);
                        break;
                    case DeltaApplied delta:
                        Apply(delta.Snapshot, new EventBatch(delta.Seq, DecodeEvents(delta)));
                        break;
                    case LobbyUpdated:
                        Raise(() =>
                        {
                            RosterChanged?.Invoke();
                            PlayersChanged?.Invoke();
                        });
                        break;
                    case LobbyChatReceived said:
                        var line = new ChatLine(string.IsNullOrEmpty(said.Sender) ? "?" : said.Sender, said.Text);
                        lock (gate) chat = SocialModel.Append(chat, line);
                        Raise(() => ChatReceived?.Invoke(line));
                        break;
                    case StateChanged moved:
                        Raise(() =>
                        {
                            Connection = moved.State;
                            if (moved.State == ClientSessionState.InGame) HostClosing = false;
                            ConnectionChanged?.Invoke();
                        });
                        break;
                    case HostClosed:
                        Raise(() =>
                        {
                            HostClosing = true;
                            ConnectionChanged?.Invoke();
                        });
                        break;
                    case Unciv.Client.Net.Session.Desynchronized lost:
                        Raise(() => Desynchronized?.Invoke(lost.Reason));
                        break;
                    case SessionEnded ended:
                        Raise(() =>
                        {
                            EndedReason = ended.Reason;
                            ConnectionChanged?.Invoke();
                        });
                        Fail(new InvalidOperationException(ended.Reason));
                        return;
                }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            Fail(new ObjectDisposedException(nameof(NetGameSession)));
        }
        catch (Exception e)
        {
            Fail(e);
            throw;
        }
    }

    /// <summary>Events the client cannot decode (a host newer than this client) are dropped: the mirror itself comes from the diff, so it stays right.</summary>
    private static List<GameEvent> DecodeEvents(DeltaApplied delta)
    {
        var events = new List<GameEvent>(delta.Events.Count);
        foreach (var element in delta.Events)
        {
            GameEventEnvelope envelope;
            try
            {
                envelope = System.Text.Json.JsonSerializer.Deserialize<GameEventEnvelope>(element, Unciv.Json.UncivJson.DefaultOptions)
                    ?? throw new System.Text.Json.JsonException("null event");
            }
            catch (System.Text.Json.JsonException)
            {
                continue;
            }
            if (GameEventRegistry.TryFromEnvelope(envelope, out var decoded, out _) && decoded != null) events.Add(decoded);
        }
        return events;
    }

    private void Apply(ClientSnapshot fresh, EventBatch? batch)
    {
        lock (gate)
        {
            snapshot = fresh;
            if (batch == null) replacedCount++;
            if (batch != null)
            {
                batches[batch.Seq] = batch.Events;
                while (batches.Count > KeptBatches) batches.Remove(batches.Keys.First());
            }
        }
        Signal();
        Raise(() => SnapshotChanged?.Invoke(fresh));
        if (batch != null) Raise(() => EventsReceived?.Invoke(batch));
    }

    private void Raise(Action handlers)
    {
        if (context == null) handlers();
        else context.Post(static state => ((Action)state!)(), handlers);
    }

    private void Signal()
    {
        TaskCompletionSource old;
        lock (gate)
        {
            old = changed;
            changed = NewSignal();
        }
        old.TrySetResult();
    }

    private void Fail(Exception error)
    {
        TaskCompletionSource old;
        lock (gate)
        {
            failure ??= error;
            old = changed;
        }
        old.TrySetException(failure);
    }

    private Task NextChange()
    {
        lock (gate) return failure != null ? Task.FromException(failure) : changed.Task;
    }

    /// <summary>Completes once <paramref name="done"/> holds for the mirror; faults when the session ended or the pump failed.</summary>
    private async Task WaitUntilAsync(Func<bool> done, CancellationToken cancellation)
    {
        while (true)
        {
            var next = NextChange(); // taken before the check, so a change between check and wait is not lost
            if (done()) return;
            await next.WaitAsync(cancellation).ConfigureAwait(false);
        }
    }

    // ------------------------------------------------------------ IGameSession

    public async Task ResyncAsync()
    {
        int before;
        lock (gate) before = replacedCount;
        await session.ResyncAsync().ConfigureAwait(false);
        // the mirror is replaced before the pump has applied the event, and the new snapshot may have the same seq: wait for the application itself
        await WaitUntilAsync(() => { lock (gate) return replacedCount > before; }, CancellationToken.None).ConfigureAwait(false);
    }

    public async Task<TResult?> QueryAsync<TResult>(IQuery<TResult> query) where TResult : class
    {
        var answer = await session.QueryAsync(query).ConfigureAwait(false);
        if (answer.Ok) return answer.Value;
        if (answer.Code is QueryWireCodes.SessionClosed or ClientCodes.QueryTimeout or QueryWireCodes.MalformedResult)
            throw new InvalidOperationException(answer.Code);
        return null;
    }

    public Task<SubmitResult> SubmitAsync(GameCommand command) => SubmitAsync(command, CancellationToken.None);

    private async Task<SubmitResult> SubmitAsync(GameCommand command, CancellationToken cancellation)
    {
        var outcome = await session.SubmitAsync(command, cancellation).ConfigureAwait(false);
        if (outcome.Code is ClientCodes.SessionClosed or ClientCodes.ConnectionLost or ClientCodes.ConnectFailed)
            throw new InvalidOperationException(outcome.Code);
        if (!outcome.Accepted || outcome.Seq is not { } seq) return new SubmitResult(false, outcome.Code ?? "refused", []);
        return new SubmitResult(true, "", await EventsOfAsync(seq).ConfigureAwait(false));
    }

    /// <summary>The events the host sent for command <paramref name="seq"/>: the delta may land before or after the command's answer, so wait until the mirror has reached it.</summary>
    private async Task<IReadOnlyList<GameEvent>> EventsOfAsync(long seq)
    {
        using var limit = new CancellationTokenSource(EventWait);
        try
        {
            await WaitUntilAsync(() => snapshot.Seq >= seq, limit.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (limit.IsCancellationRequested)
        {
            return [];
        }
        lock (gate) return batches.GetValueOrDefault(seq) ?? [];
    }

    public Task<TurnOutcome> EndTurnAsync(IProgress<TurnProgress>? progress = null, CancellationToken cancellation = default) =>
        PlayTurnAsync(static () => new EndTurn(), cancellation);

    public Task<TurnOutcome> AutoPlayTurnAsync(IProgress<TurnProgress>? progress = null, CancellationToken cancellation = default) =>
        PlayTurnAsync(static () => new AutoPlayTurn(), cancellation);

    private async Task<TurnOutcome> PlayTurnAsync(Func<GameCommand> command, CancellationToken cancellation)
    {
        if (Interlocked.Exchange(ref endingTurn, 1) == 1) return new TurnOutcome(false, "The turn is already being processed", 0, false, []);
        try
        {
            var startTurn = snapshot.Turn;
            IReadOnlyList<GameEvent> events = [];
            try
            {
                if (!snapshot.TurnStatus.Ended.Contains(ViewerCivId)) // else a cancelled End turn is being continued
                {
                    var submitted = await SubmitAsync(command(), cancellation).ConfigureAwait(false);
                    if (!submitted.Accepted) return new TurnOutcome(false, submitted.Message, 0, false, []);
                    events = submitted.Events;
                }
                await WaitUntilAsync(() => snapshot.Turn > startTurn || IsDefeated(snapshot), cancellation).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                return new TurnOutcome(false, Unciv.Net.Visibility.ViewerSessionFlow.CancelledMessage, 0, false, events);
            }
            return new TurnOutcome(true, "", 0, !IsDefeated(snapshot), events);
        }
        finally
        {
            Volatile.Write(ref endingTurn, 0);
        }
    }

    private bool IsDefeated(ClientSnapshot view) => view.Civs.Any(it => it.CivId == ViewerCivId && it.IsDefeated);

    public Task<string?> ExportSaveAsync() => Task.FromResult<string?>(null);

    /// <summary>Stops pumping events; the <see cref="ClientSession"/> stays open (the caller owns it). Later calls that wait for the host fault.</summary>
    public async ValueTask DisposeAsync()
    {
        await stop.CancelAsync().ConfigureAwait(false);
        try
        {
            await pump.ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // already reported to every waiter by Fail
        }
        stop.Dispose();
    }
}
