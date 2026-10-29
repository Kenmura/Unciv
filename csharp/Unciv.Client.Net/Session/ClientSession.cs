using System.Text.Json;
using System.Threading.Channels;
using Unciv.Commands;
using Unciv.Json;
using Unciv.Logic;
using Unciv.Net;
using Unciv.Net.Identity;
using Unciv.Net.Remote;
using Unciv.Queries;
using CommandResult = Unciv.Net.CommandResult;

namespace Unciv.Client.Net.Session;

/// <summary>Everything a <see cref="ClientSession"/> needs to know about who is joining where. Plain data and callbacks: no Godot, no storage.</summary>
public sealed class ClientSessionOptions
{
    public required ClientIdentity Identity { get; init; }
    public required string DisplayName { get; init; }
    public required string RulesetHash { get; init; }
    public string ClientVersion { get; init; } = "unciv-client";
    public List<string> ModList { get; init; } = [];

    public string? Password { get; init; }
    public string? InviteToken { get; init; }

    /// <summary>Fingerprint carried by an invite code: verified before anything is sent, no first-use window.</summary>
    public string? ExpectedFingerprint { get; init; }

    /// <summary>The key into <see cref="KnownHosts"/> (<c>address:port</c> as the user typed it).</summary>
    public string HostAddress { get; init; } = "";

    public KnownHosts? KnownHosts { get; init; }

    /// <summary>Called after a new pin was added so the UI layer can write <c>known-hosts.json</c>.</summary>
    public Action<KnownHosts>? PersistKnownHosts { get; init; }

    /// <summary>The TOFU prompt: (host address, fingerprint) to "trust this host?". Without it an unknown host is refused.</summary>
    public Func<string, string, Task<bool>>? TrustPrompt { get; init; }

    /// <summary>Accept a host with neither an invite fingerprint nor known hosts (the in-process loopback host, tests). Off by default.</summary>
    public bool AllowUnpinnedHost { get; init; }

    /// <summary>The game id kept from the last session; with it a reconnect sends <c>Resume</c>, without it a <c>ResyncRequest</c> (the host answers both with a snapshot or the missed deltas).</summary>
    public string? GameId { get; init; }

    public int MaxReconnectAttempts { get; init; } = 5;

    /// <summary>Waits before reconnect attempt n (1-based). The default backs off 1, 2, 4, ... 30 seconds; tests pass an immediate one.</summary>
    public Func<int, CancellationToken, Task> ReconnectDelay { get; init; } =
        (attempt, token) => Task.Delay(TimeSpan.FromSeconds(Math.Min(30, 1 << Math.Min(attempt - 1, 5))), token);

    public QueryRegistry? Queries { get; init; }
    public ISecureRandom? Random { get; init; }
}

/// <summary>The result of <see cref="ClientSession.JoinAsync"/>: joined, or the refusal code.</summary>
public sealed record JoinResult(bool Joined, string? Code);

/// <summary>
/// The client side of the host-a-lobby protocol (SERVER.md 2.3, 6, 7): join (invite fingerprint or TOFU, password, key proof), lobby, mirror of the
/// viewer's state moved by snapshots and deltas, commands with retry by <c>ClientCmdId</c>, queries, reconnect with the same key and resume.
/// No Godot types and no sockets: it talks to an <see cref="ITransport"/> from a factory, so tests drive it over <see cref="InMemoryTransport"/>.
/// All incoming messages are handled in order by one background loop; <see cref="Events"/> is the ordered stream the UI consumes.
/// </summary>
public sealed class ClientSession : IAsyncDisposable
{
    private const int MaxPendingCommands = 64;

    private readonly Func<CancellationToken, Task<ITransport>> connect;
    private readonly ClientSessionOptions options;
    private readonly Channel<ClientEvent> events = Channel.CreateUnbounded<ClientEvent>();
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim sendLock = new(1, 1);
    private readonly Lock gate = new();
    private readonly Dictionary<string, PendingCommand> pending = [];
    private readonly RemoteQueryClient queries;
    private readonly string idPrefix;
    private readonly TaskCompletionSource<JoinResult> joined = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CommandRegistry<GameInfo>? commandRegistry;

    private volatile ITransport? transport;
    private volatile ClientSessionState state = ClientSessionState.Disconnected;
    private long nextCommand = 1;
    private Task? loop;
    private bool stopped;

    // Receive-loop state (touched by the loop only, except where noted).
    private bool everJoined;
    private bool connectionJoined;
    private bool awaitingSnapshot;
    private bool gameStarted;
    private string? gameId;
    private TaskCompletionSource? resyncWaiter;

    private sealed class PendingCommand(Command message, long ordinal)
    {
        public Command Message { get; } = message;
        public long Ordinal { get; } = ordinal;
        public TaskCompletionSource<CommandOutcome> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public ClientSession(Func<CancellationToken, Task<ITransport>> connect, ClientSessionOptions options)
    {
        this.connect = connect;
        this.options = options;
        gameId = options.GameId;
        var random = options.Random ?? SystemSecureRandom.Instance;
        Span<byte> bytes = stackalloc byte[6];
        random.Fill(bytes);
        idPrefix = Convert.ToHexString(bytes).ToLowerInvariant();

        var registry = options.Queries;
        if (registry == null)
        {
            registry = new QueryRegistry(UncivJson.DefaultOptions);
            registry.RegisterFromAssembly(typeof(QueryRegistry).Assembly);
        }
        queries = new RemoteQueryClient(registry, UncivJson.DefaultOptions, SendAsync);
    }

    public ClientSessionState State => state;

    /// <summary>The mirror of the viewer's state; null until the first snapshot.</summary>
    public ClientGameState Game { get; } = new();

    /// <summary>Latest lobby view (null before the first <c>LobbyState</c>).</summary>
    public LobbyState? Lobby { get; private set; }

    /// <summary>Latest turn status message, if any.</summary>
    public TurnStatus? TurnStatus { get; private set; }

    /// <summary>Commands submitted and not yet answered (they are resent after a reconnect).</summary>
    public int PendingCommandCount
    {
        get { lock (gate) return pending.Count; }
    }

    /// <summary>The ordered stream of what happened. Read it with <c>ReadAllAsync</c> or <c>TryRead</c> from the UI pump.</summary>
    public ChannelReader<ClientEvent> Events => events.Reader;

    /// <summary>
    /// Connects, runs the join handshake and completes when the host admitted this key (or refused, or the connection failed).
    /// The session then keeps running in the background (and reconnects when the connection drops) until <see cref="LeaveAsync"/>.
    /// </summary>
    public Task<JoinResult> JoinAsync()
    {
        lock (gate)
        {
            if (loop != null) throw new InvalidOperationException("Session already started");
            loop = RunAsync();
        }
        return joined.Task;
    }

    // ------------------------------------------------------------ commands and queries

    /// <summary>
    /// Submits a command and completes with the host's answer. The command keeps its <c>ClientCmdId</c> through reconnects (the host answers a repeated id
    /// with the first result instead of applying it twice), so a command submitted while the connection is down is sent after the resume.
    /// </summary>
    public async Task<CommandOutcome> SubmitAsync(string commandType, JsonElement payload, CancellationToken cancellationToken = default)
    {
        PendingCommand entry;
        lock (gate)
        {
            if (stopped) return new CommandOutcome("", false, ClientCodes.SessionClosed, null);
            if (pending.Count >= MaxPendingCommands) return new CommandOutcome("", false, ClientCodes.TooManyPending, null);
            var ordinal = nextCommand++;
            var id = $"{idPrefix}-{ordinal}";
            entry = new PendingCommand(new Command { ClientCmdId = id, CommandType = commandType, Payload = payload }, ordinal);
            pending[id] = entry;
        }

        if (state is ClientSessionState.InGame or ClientSessionState.Syncing)
        {
            try { await SendAsync(entry.Message, cancellationToken).ConfigureAwait(false); }
            catch (Exception e) when (IsConnectionGone(e)) { /* connection just dropped: resent after the resume */ }
        }

        using var registration = cancellationToken.Register(() =>
        {
            lock (gate) pending.Remove(entry.Message.ClientCmdId);
            entry.Completion.TrySetCanceled(cancellationToken);
        });
        return await entry.Completion.Task.ConfigureAwait(false);
    }

    /// <summary>Submits an engine command (serialized with the command registry under its registered type).</summary>
    public Task<CommandOutcome> SubmitAsync(GameCommand command, CancellationToken cancellationToken = default)
    {
        commandRegistry ??= GameCommandRegistry.Create();
        CommandEnvelope envelope;
        try { envelope = commandRegistry.ToEnvelope(0, "", command); }
        catch (InvalidOperationException) { return Task.FromResult(new CommandOutcome("", false, ClientCodes.MalformedCommand, null)); }
        return SubmitAsync(envelope.Type, envelope.Payload, cancellationToken);
    }

    /// <summary>A read-only question to the host as this session's civ. Fails with <see cref="ClientCodes.QueryTimeout"/> after <paramref name="timeout"/> (default 10 s).</summary>
    public async Task<RemoteQueryResult<TResult>> QueryAsync<TResult>(IQuery<TResult> query, TimeSpan? timeout = null, CancellationToken cancellationToken = default) where TResult : notnull
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout ?? TimeSpan.FromSeconds(10));
        try
        {
            return await queries.QueryAsync(query, deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return RemoteQueryResult<TResult>.Fail(ClientCodes.QueryTimeout);
        }
    }

    /// <summary>Asks the host for a full snapshot and completes when it replaced the mirror (or the connection was lost first, which faults the task).</summary>
    public async Task ResyncAsync(CancellationToken cancellationToken = default)
    {
        TaskCompletionSource waiter;
        lock (gate)
        {
            waiter = resyncWaiter ??= new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        await RequestResyncAsync("requested").ConfigureAwait(false);
        await waiter.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    // ------------------------------------------------------------ lobby actions

    public ValueTask SetLobbySlotAsync(int slot, string? nation, string? color, bool ready) =>
        SendAsync(new LobbySet { Slot = slot, Nation = nation, Color = color, Ready = ready });

    public ValueTask SendChatAsync(string text) => SendAsync(new LobbyChat { Text = text });

    public ValueTask PingAsync() => SendAsync(new Ping());

    /// <summary>Tells the host this player leaves and ends the session (no reconnect).</summary>
    public async Task LeaveAsync()
    {
        try { await SendAsync(new Leave()).ConfigureAwait(false); }
        catch (Exception e) when (IsConnectionGone(e)) { }
        await StopAsync("left").ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync("disposed").ConfigureAwait(false);
        lifetime.Dispose();
    }

    // ------------------------------------------------------------ connection loop

    private async Task RunAsync()
    {
        var token = lifetime.Token;
        var attempt = 0;
        while (!stopped)
        {
            ITransport? current = null;
            connectionJoined = false;
            try { current = await connect(token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception e) when (e is IOException or InvalidOperationException or System.Net.Sockets.SocketException or System.Security.Authentication.AuthenticationException or TimeoutException or OperationCanceledException)
            {
                // fall through to the retry logic below
            }

            if (current != null) await RunConnectionAsync(current, token).ConfigureAwait(false);
            if (connectionJoined) attempt = 0;
            if (stopped) break;

            if (!everJoined)
            {
                await EndAsync(current == null ? ClientCodes.ConnectFailed : ClientCodes.ConnectionLost, refused: false).ConfigureAwait(false);
                return;
            }
            // The mirror may have missed deltas since the drop: IGameSession.Desynchronized must fire before the reconnect/resume (T180 note, T244).
            Emit(new Desynchronized("reconnect"));
            if (++attempt > options.MaxReconnectAttempts)
            {
                await EndAsync("reconnect-failed", refused: false).ConfigureAwait(false);
                return;
            }
            SetState(ClientSessionState.Reconnecting);
            FailWaiters();
            try { await options.ReconnectDelay(attempt, token).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task RunConnectionAsync(ITransport current, CancellationToken token)
    {
        transport = current;
        connectionJoined = false;
        awaitingSnapshot = false;
        try
        {
            if (!everJoined) SetState(ClientSessionState.Joining);
            await SendAsync(new Hello
            {
                Protocol = ProtocolVersion.Current,
                ClientVersion = options.ClientVersion,
                RulesetHash = options.RulesetHash,
                ModList = options.ModList
            }, token).ConfigureAwait(false);

            while (!stopped)
            {
                var frame = await current.ReceiveAsync(token).ConfigureAwait(false);
                if (frame == null) break;
                if (!await HandleAsync(ProtocolCodec.Decode(frame), token).ConfigureAwait(false)) break;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is IOException or InvalidOperationException)
        {
            // a dead transport ends this connection; the loop decides about reconnecting
        }
        finally
        {
            transport = null;
            try { await current.CloseAsync().ConfigureAwait(false); } catch (Exception e) when (e is IOException or ObjectDisposedException) { }
            queries.FailAll();
        }
    }

    /// <summary>Handles one message; false ends this connection.</summary>
    private async Task<bool> HandleAsync(Message message, CancellationToken token)
    {
        switch (message)
        {
            case Welcome welcome:
                return await HandleWelcomeAsync(welcome, token).ConfigureAwait(false);
            case JoinOk:
                await HandleJoinOkAsync(token).ConfigureAwait(false);
                return true;
            case JoinRefused refused:
                await EndAsync(refused.Code, refused: true).ConfigureAwait(false);
                return false;
            case LobbyState lobby:
                Lobby = lobby;
                Emit(new LobbyUpdated(lobby));
                return true;
            case LobbyChat chat:
                Emit(new LobbyChatReceived(chat.Sender, chat.Text));
                return true;
            case GameStarting:
                gameStarted = true;
                awaitingSnapshot = true;
                SetState(ClientSessionState.Syncing);
                Emit(new GameStarted());
                return true;
            case Snapshot snapshot:
                HandleSnapshot(snapshot);
                return true;
            case ViewDelta delta:
                await HandleDeltaAsync(delta).ConfigureAwait(false);
                return true;
            case TurnStatus status:
                TurnStatus = status;
                Emit(new TurnStatusChanged(status));
                return true;
            case CommandResult result:
                HandleCommandResult(result);
                return true;
            case QueryResult result:
                queries.Complete(result);
                return true;
            case HostClosing:
                Emit(new HostClosed());
                return true;
            case Error error:
                Emit(new ProtocolError(error.Code));
                return true;
            case Pong:
                return true;
            default:
                Emit(new ProtocolError(ErrorCodes.InvalidMessage));
                return true;
        }
    }

    private async Task<bool> HandleWelcomeAsync(Welcome welcome, CancellationToken token)
    {
        if (!ProtocolVersion.IsCompatible(welcome.Protocol))
        {
            await EndAsync(ClientCodes.ProtocolMismatch, refused: true).ConfigureAwait(false);
            return false;
        }
        var refusal = await CheckHostKeyAsync(welcome.HostKeyFingerprint).ConfigureAwait(false);
        if (refusal != null)
        {
            await EndAsync(refusal, refused: true).ConfigureAwait(false);
            return false;
        }

        await SendAsync(new Join
        {
            Password = options.Password,
            PlayerKey = options.Identity.PlayerKey,
            DisplayName = options.DisplayName,
            InviteToken = options.InviteToken
        }, token).ConfigureAwait(false);
        await SendAsync(new Auth { Proof = options.Identity.Sign(welcome.HostKeyFingerprint, welcome.SessionId, welcome.Challenge) }, token).ConfigureAwait(false);
        return true;
    }

    /// <summary>The refusal code when the host's key must not be trusted, else null. A changed key is a hard stop, never a prompt.</summary>
    private async Task<string?> CheckHostKeyAsync(string presented)
    {
        var known = options.KnownHosts;
        var address = options.HostAddress;
        var pinnedTrust = known?.Check(address, presented) ?? HostTrust.Unknown;
        if (pinnedTrust == HostTrust.Mismatch) return ClientCodes.HostKeyChanged;

        if (!string.IsNullOrEmpty(options.ExpectedFingerprint))
        {
            if (!HostKey.Matches(options.ExpectedFingerprint, presented)) return ClientCodes.HostKeyMismatch;
        }
        else if (pinnedTrust != HostTrust.Trusted)
        {
            if (known != null)
            {
                if (options.TrustPrompt == null || !await options.TrustPrompt(address, presented).ConfigureAwait(false)) return ClientCodes.HostUntrusted;
            }
            else if (!options.AllowUnpinnedHost)
            {
                return ClientCodes.HostUntrusted;
            }
        }

        if (known != null && pinnedTrust == HostTrust.Unknown && known.Pin(address, presented)) options.PersistKnownHosts?.Invoke(known);
        return null;
    }

    private async Task HandleJoinOkAsync(CancellationToken token)
    {
        connectionJoined = true;
        var firstJoin = !everJoined;
        everJoined = true;
        joined.TrySetResult(new JoinResult(true, null));

        var snapshot = Game.Snapshot;
        if (snapshot == null && !gameStarted)
        {
            SetState(ClientSessionState.Lobby);
            return;
        }

        SetState(ClientSessionState.Syncing);
        if (snapshot == null)
        {
            awaitingSnapshot = true;
            if (!firstJoin) await SendAsync(new ResyncRequest(), token).ConfigureAwait(false);
        }
        else if (gameId != null)
        {
            await SendAsync(new Resume { GameId = gameId, LastSeq = snapshot.Seq, LastViewHash = snapshot.ViewHash ?? "" }, token).ConfigureAwait(false);
        }
        else
        {
            awaitingSnapshot = true;
            await SendAsync(new ResyncRequest(), token).ConfigureAwait(false);
        }
        await ResendPendingAsync(token).ConfigureAwait(false);
    }

    private async Task ResendPendingAsync(CancellationToken token)
    {
        List<PendingCommand> all;
        // Submission order, not ClientCmdId text order: "10" sorts before "2" as a string, and the host must apply a player's commands in the order they were issued.
        lock (gate) all = [.. pending.Values.OrderBy(it => it.Ordinal)];
        foreach (var entry in all) await SendAsync(entry.Message, token).ConfigureAwait(false);
    }

    // ------------------------------------------------------------ mirror

    private void HandleSnapshot(Snapshot message)
    {
        switch (Game.Replace(message))
        {
            case SnapshotOutcome.Accepted:
                awaitingSnapshot = false;
                queries.FailAll(); // an answer computed against the old state must not be applied to the new one
                SetState(ClientSessionState.InGame);
                Emit(new SnapshotReplaced(Game.Snapshot!));
                lock (gate)
                {
                    resyncWaiter?.TrySetResult();
                    resyncWaiter = null;
                }
                break;
            case SnapshotOutcome.Malformed:
                awaitingSnapshot = false;
                Emit(new Desynchronized("malformed-snapshot"));
                break;
            default:
                awaitingSnapshot = false;
                Emit(new Desynchronized("snapshot-hash-mismatch"));
                break;
        }
    }

    private async Task HandleDeltaAsync(ViewDelta delta)
    {
        if (awaitingSnapshot) return; // a snapshot is on its way and will supersede this
        var outcome = Game.ApplyDelta(delta);
        switch (outcome)
        {
            case DeltaOutcome.Applied:
                Emit(new DeltaApplied(delta.Seq, delta.Events, Game.Snapshot!));
                break;
            case DeltaOutcome.Duplicate:
                break;
            default:
                await RequestResyncAsync(DescribeOutcome(outcome)).ConfigureAwait(false);
                break;
        }
    }

    private static string DescribeOutcome(DeltaOutcome outcome) => outcome switch
    {
        DeltaOutcome.Gap => "sequence-gap",
        DeltaOutcome.NoSnapshot => "delta-before-snapshot",
        DeltaOutcome.NoDiff => "delta-without-diff",
        DeltaOutcome.BadDiff => "malformed-diff",
        _ => "view-hash-mismatch"
    };

    private async Task RequestResyncAsync(string reason)
    {
        if (awaitingSnapshot) return;
        awaitingSnapshot = true;
        queries.FailAll();
        Emit(new Desynchronized(reason));
        try { await SendAsync(new ResyncRequest()).ConfigureAwait(false); }
        catch (Exception e) when (IsConnectionGone(e)) { /* connection lost: the reconnect resyncs */ }
    }

    private void HandleCommandResult(CommandResult result)
    {
        PendingCommand? entry;
        lock (gate)
        {
            if (!pending.Remove(result.ClientCmdId, out entry)) return; // duplicate answer (retry, resume) or cancelled command
        }
        var outcome = new CommandOutcome(result.ClientCmdId, result.Accepted, result.Code, result.Seq);
        entry.Completion.TrySetResult(outcome);
        Emit(new CommandCompleted(outcome));
    }

    // ------------------------------------------------------------ plumbing

    /// <summary>A send that failed because the connection died (closed transport, broken pipe, disposed stream): not a command failure, the reconnect resends.</summary>
    private static bool IsConnectionGone(Exception e) => e is InvalidOperationException or IOException or ObjectDisposedException;

    private async ValueTask SendAsync(Message message, CancellationToken cancellationToken = default)
    {
        var target = transport ?? throw new InvalidOperationException("Not connected");
        var frame = ProtocolCodec.Encode(message);
        await sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await target.SendAsync(frame, cancellationToken).ConfigureAwait(false); }
        finally { sendLock.Release(); }
    }

    private void SetState(ClientSessionState next)
    {
        if (state == next) return;
        state = next;
        Emit(new StateChanged(next));
    }

    private void Emit(ClientEvent clientEvent) => events.Writer.TryWrite(clientEvent);

    private void FailWaiters()
    {
        queries.FailAll();
        lock (gate)
        {
            resyncWaiter?.TrySetException(new InvalidOperationException(ClientCodes.ConnectionLost));
            resyncWaiter = null;
        }
    }

    /// <summary>The session is over for good: answers everything still waiting and reports why.</summary>
    private async Task EndAsync(string reason, bool refused)
    {
        List<PendingCommand> all;
        lock (gate)
        {
            if (stopped) return;
            stopped = true;
            all = [.. pending.Values];
            pending.Clear();
        }
        joined.TrySetResult(new JoinResult(false, reason));
        foreach (var entry in all) entry.Completion.TrySetResult(new CommandOutcome(entry.Message.ClientCmdId, false, ClientCodes.SessionClosed, null));
        FailWaiters();
        if (refused) Emit(new JoinRefusedEvent(reason));
        SetState(ClientSessionState.Disconnected);
        Emit(new SessionEnded(reason));
        events.Writer.TryComplete();
        await lifetime.CancelAsync().ConfigureAwait(false);
    }

    private async Task StopAsync(string reason)
    {
        await EndAsync(reason, refused: false).ConfigureAwait(false);
        var current = transport;
        if (current != null) await current.CloseAsync().ConfigureAwait(false);
        var running = loop;
        if (running != null) await running.ConfigureAwait(false);
    }
}
