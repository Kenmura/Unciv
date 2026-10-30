using System.Threading.Channels;
using Unciv.Commands;
using Unciv.Commands.Events;
using Unciv.Commands.Log;
using Unciv.Commands.Turn;
using Unciv.Logic;
using Unciv.Net;
using Unciv.Net.Identity;
using Unciv.Net.Persistence;
using Unciv.Net.Remote;
using Unciv.Net.Turns;
using Unciv.Net.Visibility;
using Unciv.Queries;
using Unciv.Server.Transport;
using EngineCommandResult = Unciv.Commands.CommandResult;
using WireCommandResult = Unciv.Net.CommandResult;

namespace Unciv.Server;

/// <summary>Represents an active client session connected to the game host.</summary>
public sealed class HostSession
{
    public required string SessionId { get; init; }
    public required string BoundCivId { get; init; }
    public string? PlayerKeyId { get; init; }
    public required ITransport Transport { get; init; }
    public ClientSnapshot? PreviousSnapshot { get; set; }

    /// <summary>T273: the client's Hello advertised host migration, so a clean quit may send it <see cref="HostHandoff"/>.</summary>
    public bool HostMigration { get; init; }
    public bool IsOpen => Transport.IsOpen;

    private long lastInboundMillis;
    /// <summary>Clock time of the last frame received from this peer (any message counts as liveness); read by the idle-timeout check.</summary>
    public long LastInboundMillis
    {
        get => Interlocked.Read(ref lastInboundMillis);
        set => Interlocked.Exchange(ref lastInboundMillis, value);
    }

    /// <summary>Clock time of the last full snapshot sent in answer to a <c>ResyncRequest</c> (long.MinValue: none yet).</summary>
    public long LastResyncMillis { get; set; } = long.MinValue;

    /// <summary>A ResyncRequest arrived inside the rate-limit window: one snapshot is owed once the window ends (requests coalesce).</summary>
    public bool ResyncPending { get; set; }

    /// <summary>C045: when this session's recent chat lines were accepted (host clock), for the rate limit.</summary>
    public Queue<long> ChatTimes { get; } = new();

    /// <summary>C090: when this session's recent map pings were accepted (host clock), for the rate limit.</summary>
    public Queue<long> PingTimes { get; } = new();
}

/// <summary>Per-session protection limits of the host. Times are in <see cref="IClock"/> milliseconds.</summary>
public sealed record HostLimits
{
    public static HostLimits Default { get; } = new();

    /// <summary>A session that sent nothing for this long is dropped (clients ping every 20 s, so a silent peer is dead or half-open).</summary>
    public long IdleTimeoutMillis { get; init; } = 90_000;

    /// <summary>At most one resync snapshot per session in this window; extra requests coalesce into one later snapshot.</summary>
    public long ResyncMinIntervalMillis { get; init; } = 2_000;

    /// <summary>Outbound frames queued per session before a slow reader is disconnected.</summary>
    public int MaxQueuedFrames { get; init; } = 256;

    /// <summary>C045: longest chat line broadcast (UTF-16 units); longer text is cut.</summary>
    public int MaxChatChars { get; init; } = 500;

    /// <summary>C045: chat lines one session may send per <see cref="ChatWindowMillis"/>; the rest are dropped silently.</summary>
    public int MaxChatPerWindow { get; init; } = 5;

    public long ChatWindowMillis { get; init; } = 10_000;

    /// <summary>C090: map pings one session may send per <see cref="PingWindowMillis"/>; the rest are dropped silently.</summary>
    public int MaxPingPerWindow { get; init; } = 4;

    /// <summary>C090: longest sender name on a relayed ping.</summary>
    public int MaxPingNameChars { get; init; } = 24;

    public long PingWindowMillis { get; init; } = 5_000;

    /// <summary>Outbound bytes (approximate) queued per session before a slow reader is disconnected.</summary>
    public long MaxQueuedBytes { get; init; } = 64L * 1024 * 1024;
}

/// <summary>
/// Single-threaded authoritative actor hosting one game (SERVER.md 1, 5, 6).
/// Owns the GameSession single writer, TurnTimer, RemoteQueryHost, and CommandGate.
/// Distributes ViewDelta with SnapshotDiff and ViewHash after every accepted command
/// and server command to all connected human/spectator sessions.
/// </summary>
public sealed partial class GameHost : IAsyncDisposable
{
    private readonly GameSession session;
    private readonly IGameStore? store;
    private readonly TurnTimer turnTimer;
    private readonly RemoteQueryHost remoteQueries;
    private readonly CommandGate gate;
    private readonly IClock clock;
    private readonly HostLimits limits;
    private readonly Dictionary<string, HostSession> sessions = new(StringComparer.Ordinal);
    private readonly Channel<Func<Task>> mailbox = Channel.CreateUnbounded<Func<Task>>();
    private readonly CancellationTokenSource cts = new();
    private Task? actorTask;
    private readonly bool announceRoster;
    private bool closed;

    public GameHost(
        GameSession session,
        IGameStore? store = null,
        TurnTimer? turnTimer = null,
        RemoteQueryHost? remoteQueries = null,
        CommandGate? gate = null,
        IClock? clock = null,
        bool startActor = true,
        HostLimits? limits = null,
        bool announceRoster = false,
        TurnPolicy? turnPolicy = null,
        Unciv.Net.Standby.StandbyCoordinator? standby = null)
    {
        this.announceRoster = announceRoster;
        this.limits = limits ?? HostLimits.Default;
        this.session = session;
        this.store = store;
        this.clock = clock ?? SystemClock.Instance;
        this.turnTimer = turnTimer ?? new TurnTimer(session, turnPolicy ?? TurnPolicy.Default, this.clock, autoResolve: false);
        this.turnTimer.AutoResolve = false;
        this.turnTimer.ServerCommandSubmitter = ApplyServerCommand;
        this.remoteQueries = remoteQueries ?? CreateDefaultRemoteQueryHost();
        this.gate = gate ?? new CommandGate(store, null, this.clock);
        Standby = InitStandby(standby);
        InitReplication();

        if (startActor)
        {
            Start();
        }
    }

    /// <summary>T270: where the host reports something an operator must know (the dedicated server logs it). Called on the actor.</summary>
    public Action<string>? Warn { get; set; }

    private string? lastResolveRefusal;

    public GameSession Session => session;

    public GameInfo Game => session.Game;

    public TurnTimer TurnTimer => turnTimer;

    public RemoteQueryHost RemoteQueries => remoteQueries;

    public CommandGate Gate => gate;

    public IGameStore? Store => store;

    public IClock Clock => clock;

    public IReadOnlyDictionary<string, HostSession> Sessions => sessions;

    /// <summary>Starts the single-threaded actor loop processing the inbound mailbox.</summary>
    public void Start()
    {
        if (actorTask != null) return;
        actorTask = RunActorLoopAsync(cts.Token);
    }

    /// <summary>
    /// Attaches an authenticated session bound to <paramref name="boundCivId"/>, seeds its initial snapshot,
    /// sends the Snapshot message, and starts reading from the transport.
    /// </summary>
    public async Task AttachSessionAsync(string sessionId, string boundCivId, ITransport transport, string? playerKeyId = null, bool hostMigration = false)
    {
        var completion = new TaskCompletionSource<bool>();
        await mailbox.Writer.WriteAsync(async () =>
        {
            HostSession? hostSession = null;
            try
            {
                var civ = session.Game.GetCivilizationOrNull(boundCivId);
                hostSession = new HostSession
                {
                    SessionId = sessionId,
                    BoundCivId = boundCivId,
                    PlayerKeyId = playerKeyId,
                    HostMigration = hostMigration,
                    Transport = new BoundedSendTransport(transport, limits.MaxQueuedFrames, limits.MaxQueuedBytes),
                    LastInboundMillis = clock.NowMillis
                };

                // A reconnect replaces the civ's older session (its late detach must not mark the new one disconnected).
                foreach (var stale in sessions.Values.Where(x => string.Equals(x.BoundCivId, boundCivId, StringComparison.Ordinal)).ToList())
                {
                    await DetachSessionAsync(stale.SessionId);
                }

                sessions[sessionId] = hostSession;
                var seqBefore = session.LastSeq;
                turnTimer.Connect(boundCivId, playerKeyId);
                var changedState = session.LastSeq != seqBefore;
                if (changedState)
                {
                    // Connecting can apply commands (undoing a drop's forced end, handing an AI civ to the joiner). They happened outside the broadcast path,
                    // so they are logged now and every other session gets a fresh snapshot instead of a delta with a gap.
                    store?.Sync(session);
                    foreach (var other in sessions.Values.Where(x => x != hostSession).ToList())
                        await SendFullSnapshotAsync(other);
                }

                if (civ != null)
                {
                    var initialSnapshot = SnapshotBuilder.Build(session.Game, civ, session.LastSeq, CreateTurnStatusDto());
                    hostSession.PreviousSnapshot = initialSnapshot;
                    await hostSession.Transport.SendAsync(ProtocolCodec.Encode(SnapshotBuilder.ToMessage(initialSnapshot)));
                }

                _ = RunSessionReaderAsync(hostSession, cts.Token);
                await SendStandbyHelloAsync(hostSession);
                await AnnounceRosterAsync();
                completion.SetResult(true);
            }
            catch (Exception ex)
            {
                // A seat that failed half-way (its snapshot could not be sent) must not stay registered and counted as connected.
                if (hostSession != null && sessions.TryGetValue(sessionId, out var registered) && ReferenceEquals(registered, hostSession))
                {
                    try { await DetachSessionAsync(sessionId); }
                    catch (Exception) { /* the seating failure is the news */ }
                }
                completion.SetException(ex);
            }
        });

        await completion.Task;
    }

    /// <summary>Enqueues an action on the actor mailbox and awaits its execution.</summary>
    public Task EnqueueAsync(Func<Task> action)
    {
        var tcs = new TaskCompletionSource();
        var written = mailbox.Writer.TryWrite(async () =>
        {
            try
            {
                await action();
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        if (!written)
        {
            tcs.SetException(new InvalidOperationException("Host mailbox is closed"));
        }
        return tcs.Task;
    }

    /// <summary>Dispatches a message from a connected session on the actor.</summary>
    public Task HandleInboundAsync(string sessionId, Message message) =>
        EnqueueAsync(async () =>
        {
            if (!sessions.TryGetValue(sessionId, out var hostSession) || !hostSession.IsOpen)
                return;

            switch (message)
            {
                case Command cmd:
                    await ProcessCommandAsync(hostSession, cmd);
                    break;
                case Query query:
                    await ProcessQueryAsync(hostSession, query);
                    break;
                case Resume resume:
                    await ProcessResumeAsync(hostSession, resume);
                    break;
                case ResyncRequest:
                    if (ResyncAllowed(hostSession))
                        await SendResyncSnapshotAsync(hostSession);
                    else
                        hostSession.ResyncPending = true;
                    break;
                case Ping:
                    await SafeSendAsync(hostSession, new Pong());
                    break;
                case LobbyChat chat:
                    await ProcessChatAsync(hostSession, chat);
                    break;
                case MapPing ping:
                    await ProcessMapPingAsync(hostSession, ping);
                    break;
                case Leave:
                    await DetachSessionAsync(sessionId, explicitLeave: true);
                    break;
                case StandbyAccept accept:
                    ProcessStandbyAccept(hostSession, accept);
                    break;
                case StandbyDecline:
                    await ProcessStandbyDeclineAsync(hostSession);
                    break;
                case StandbySubscribe subscribe:
                    await ProcessStandbySubscribeAsync(hostSession, subscribe);
                    break;
                case StandbyAck ack:
                    await ProcessStandbyAckAsync(hostSession, ack);
                    break;
                case Error err:
                    await SafeSendAsync(hostSession, err);
                    break;
            }
        });

    /// <summary>Applies a server command, syncs the store, and broadcasts ViewDelta to all recipients.</summary>
    public EngineCommandResult ApplyServerCommand(GameCommand command)
    {
        var marks = CaptureViewMarks();
        var result = session.SubmitAsServer(command);
        if (result.Accepted)
        {
            store?.Sync(session);
            turnTimer.RefreshDeadline();
            BroadcastDelta(marks, result);
            if (command is SetController) _ = AnnounceRosterAsync(); // T267: a takeover at the boundary changes a slot's kind
        }
        return result;
    }

    /// <summary>Pumps any pending resolution steps until the simultaneous round is Playing again.</summary>
    public int PumpResolving()
    {
        int steps = 0;
        while (session.Game.NextResolveStep() is { } step)
        {
            var cmd = ResolvePump.StepCommand(step);
            // A refused step would come back as the same step forever and freeze the actor at full speed: stop; the round stays Resolving and the next tick tries again.
            var applied = ApplyServerCommand(cmd);
            if (!applied.Accepted)
            {
                // T270: said once per distinct refusal, not once per tick, so a stuck round is visible in the log without flooding it.
                var refusal = $"{applied.Validation.Code}: {applied.Validation.Message}";
                if (refusal != lastResolveRefusal) Warn?.Invoke($"the round cannot resolve: a server step was refused ({refusal}); trying again every tick");
                lastResolveRefusal = refusal;
                break;
            }
            lastResolveRefusal = null;
            steps++;
        }
        if (steps > 0 && store != null)
        {
            // SERVER.md 5.1: a checkpoint after every round's BeginTurn batch lets recovery and audits verify the replay, and the snapshot cadence rides on it.
            store.AppendCheckpoint(session.LastSeq, session.Game.Turns, ReplayVerifier.FullHash(session.Game));
            store.MaybeSnapshot(session.Game);
        }
        return steps;
    }

    /// <summary>Advances turn timer and triggers turn resolution if deadlines or disconnects expired.</summary>
    public void Tick()
    {
        DropIdleSessions();
        SendDueResyncs();
        if (standbyStream != null) PostStandbyStep(); // T272: lag and resync decisions that wait on time, not on a store write
        turnTimer.Tick();
        if (session.Game.TurnState is { Phase: SimultaneousPhase.Resolving })
        {
            PumpResolving();
            turnTimer.Tick();
        }
    }

    /// <summary>Ticks the turn timer on the host actor every <paramref name="interval"/> until <paramref name="token"/> is cancelled or the host stops (shared by the in-process host and the dedicated server). <paramref name="afterTick"/> runs on the actor after each tick.</summary>
    public async Task RunTickLoopAsync(TimeSpan interval, CancellationToken token, Action? afterTick = null)
    {
        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                await EnqueueAsync(() =>
                {
                    Tick();
                    afterTick?.Invoke(); // on the actor, so it may read the game
                    return Task.CompletedTask;
                }).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (InvalidOperationException) { } // mailbox closed by a concurrent stop
    }

    /// <summary>Closes all sessions, writes clean marker if store is configured, and stops actor.</summary>
    public async Task CloseCleanlyAsync()
    {
        if (closed) return;
        closed = true;

        await EnqueueAsync(() => CloseSessionsAndStoreAsync(null)).ConfigureAwait(false);
        await StopActorAsync().ConfigureAwait(false);
    }

    /// <summary>The body of a clean close, on the actor. <paramref name="handoff"/> (T273) is sent instead of <see cref="HostClosing"/> to the sessions that advertised host migration.</summary>
    private async Task CloseSessionsAndStoreAsync(HostHandoff? handoff)
    {
        // Finish a half-resolved round first so the final snapshot is taken at a safe point (SERVER.md 5.3).
        if (session.Game.TurnState is { Phase: SimultaneousPhase.Resolving })
        {
            PumpResolving();
        }
        StopStandbyStream();
        foreach (var s in sessions.Values.ToList())
        {
            await SafeSendAsync(s, handoff != null && s.HostMigration ? handoff : new HostClosing());
            await s.Transport.CloseAsync();
            remoteQueries.EndSession(s.SessionId);
            gate.EndSession(s.SessionId);
            turnTimer.Disconnect(s.BoundCivId);
        }
        sessions.Clear();
        store?.CloseCleanly(session.Game);
    }

    private async Task StopActorAsync()
    {
        cts.Cancel();
        mailbox.Writer.TryComplete();
        if (actorTask != null)
        {
            try
            {
                await actorTask;
            }
            catch (OperationCanceledException) { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await CloseCleanlyAsync();
        cts.Dispose();
        store?.Dispose();
    }

    private async Task ProcessCommandAsync(HostSession hostSession, Command command)
    {
        var filterResult = gate.Filter(hostSession.SessionId, hostSession.BoundCivId, command, session.Game);

        if (filterResult.Kind == CommandGateResultKind.DedupHit)
        {
            await SafeSendAsync(hostSession, new WireCommandResult
            {
                ClientCmdId = command.ClientCmdId,
                Accepted = filterResult.Accepted,
                Code = filterResult.Code,
                Seq = filterResult.Seq
            });
            return;
        }

        if (filterResult.Kind == CommandGateResultKind.Rejected)
        {
            await SafeSendAsync(hostSession, new WireCommandResult
            {
                ClientCmdId = command.ClientCmdId,
                Accepted = false,
                Code = filterResult.Code,
                Seq = null
            });
            return;
        }

        var marks = CaptureViewMarks();
        var result = session.Submit(filterResult.Envelope!);

        if (!result.Accepted)
        {
            var sanitized = CommandGate.SanitizeValidation(result.Validation);
            gate.RecordOutcome(hostSession.BoundCivId, command.ClientCmdId, 0, false, sanitized.Code);
            await SafeSendAsync(hostSession, new WireCommandResult
            {
                ClientCmdId = command.ClientCmdId,
                Accepted = false,
                Code = sanitized.Code,
                Seq = null
            });
            return;
        }

        LogAccepted(result.Seq, command.ClientCmdId);
        gate.RecordOutcome(hostSession.BoundCivId, command.ClientCmdId, result.Seq, true, null);

        await SafeSendAsync(hostSession, new WireCommandResult
        {
            ClientCmdId = command.ClientCmdId,
            Accepted = true,
            Code = null,
            Seq = result.Seq
        });

        BroadcastDelta(marks, result);

        if (session.Game.TurnState is { Phase: SimultaneousPhase.Resolving })
        {
            PumpResolving();
            turnTimer.Tick();
        }
    }

    /// <summary>
    /// Appends the command the session just accepted under the seq it was given (the gate's envelope still carries the placeholder seq), after any command the session applied
    /// outside the host's own paths (a reconnect's UndoEndTurn or SetController) so the log stays consecutive.
    /// </summary>
    private void LogAccepted(long seq, string clientCmdId)
    {
        if (store == null) return;
        var log = session.Log;
        var first = log.Count;
        while (first > 0 && log[first - 1].Seq > store.LastSeq) first--; // only the unlogged tail: the log grows for the whole game
        CommandEnvelope? accepted = null;
        for (var i = first; i < log.Count; i++)
        {
            if (log[i].Seq < seq) store.Append(log[i]);
            else if (log[i].Seq == seq) accepted = log[i];
        }
        store.Append(accepted ?? throw new InvalidOperationException($"Accepted command {seq} is not in the session log"), clientCmdId);
    }

    private async Task ProcessQueryAsync(HostSession hostSession, Query query)
    {
        bool resolving = session.Game.TurnState is { Phase: SimultaneousPhase.Resolving };
        long viewSeq = session.LastSeq;
        var context = new QueryContext(session.Game);

        var queryResult = remoteQueries.Handle(
            hostSession.SessionId,
            hostSession.BoundCivId,
            query,
            context,
            viewSeq,
            resolving,
            clock.NowMillis);

        await SafeSendAsync(hostSession, queryResult);
    }

    private async Task ProcessResumeAsync(HostSession hostSession, Resume resume)
    {
        if (!string.Equals(resume.GameId, session.Game.GameId, StringComparison.Ordinal))
        {
            await SafeSendAsync(hostSession, new Error { Code = ErrorCodes.InvalidMessage, Detail = "Mismatched game ID" });
            return;
        }
        // T273: a client ahead of this host (the host changed and some acknowledged commands were never replicated) gets its mirror replaced and is told how much was lost.
        await SendFullSnapshotAsync(hostSession, resume.LastSeq > session.LastSeq ? resume.LastSeq - session.LastSeq : null);
    }

    private static RemoteQueryHost CreateDefaultRemoteQueryHost()
    {
        var registry = new QueryRegistry(Unciv.Json.UncivJson.DefaultOptions);
        registry.RegisterFromAssembly(typeof(QueryRegistry).Assembly);
        return new RemoteQueryHost(registry, RemoteQueryPolicy.Default);
    }

    private async Task SendResyncSnapshotAsync(HostSession hostSession)
    {
        hostSession.LastResyncMillis = clock.NowMillis;
        hostSession.ResyncPending = false;
        await SendFullSnapshotAsync(hostSession);
    }

    private bool ResyncAllowed(HostSession s) =>
        s.LastResyncMillis == long.MinValue || clock.NowMillis - s.LastResyncMillis >= limits.ResyncMinIntervalMillis;

    private void DropIdleSessions()
    {
        var now = clock.NowMillis;
        foreach (var s in sessions.Values.ToList())
        {
            if (now - s.LastInboundMillis > limits.IdleTimeoutMillis || !s.IsOpen)
                _ = DetachSessionAsync(s.SessionId);
        }
    }

    private void SendDueResyncs()
    {
        foreach (var s in sessions.Values.ToList())
        {
            if (s.ResyncPending && ResyncAllowed(s))
                _ = SendResyncSnapshotAsync(s);
        }
    }

    private async Task SendFullSnapshotAsync(HostSession hostSession, long? lostCommands = null)
    {
        var civ = session.Game.GetCivilizationOrNull(hostSession.BoundCivId);
        if (civ == null) return;

        var turnStatusDto = CreateTurnStatusDto();
        var snapshot = SnapshotBuilder.Build(session.Game, civ, session.LastSeq, turnStatusDto);
        hostSession.PreviousSnapshot = snapshot;

        var msg = SnapshotBuilder.ToMessage(snapshot);
        if (lostCommands is > 0) msg = msg with { RolledBack = true, LostCommands = lostCommands };
        await SafeSendAsync(hostSession, msg);
    }

    private async Task DetachSessionAsync(string sessionId, bool explicitLeave = false)
    {
        if (!sessions.Remove(sessionId, out var hostSession)) return;
        if (standbyStream is { } streaming && ReferenceEquals(streaming.Target, hostSession)) StopStandbyStream(); // the successor reconnects and subscribes again, resuming from what it holds
        if (explicitLeave) turnTimer.Depart(hostSession.BoundCivId); // T267: the AI takes over at the next boundary, not now
        else turnTimer.Disconnect(hostSession.BoundCivId);
        remoteQueries.EndSession(sessionId);
        gate.EndSession(sessionId);
        // The roster is built before the first await (still on the actor even when a caller does not wait for this method), then sent while the transport closes.
        var announced = AnnounceRosterAsync();
        try
        {
            await hostSession.Transport.CloseAsync();
        }
        catch { }
        await announced;
    }

    // ------------------------------------------------------------ C045: chat, roster and host administration

    private string DisplayNameOf(string civId)
    {
        var name = store?.ReadMeta().Slots.GetValueOrDefault(civId)?.DisplayName;
        return string.IsNullOrWhiteSpace(name) ? civId : name;
    }

    /// <summary>
    /// A chat line from a bound player, sent to every open session (the sender included) with the sender's bound display name: the name on the wire is never taken from the frame.
    /// Blank lines are dropped, long ones cut to <see cref="HostLimits.MaxChatChars"/>, and a session over <see cref="HostLimits.MaxChatPerWindow"/> lines per window is ignored.
    /// </summary>
    private async Task ProcessChatAsync(HostSession from, LobbyChat chat)
    {
        var text = chat.Text.Trim();
        if (text.Length == 0) return;
        long now = clock.NowMillis;
        while (from.ChatTimes.Count > 0 && now - from.ChatTimes.Peek() >= limits.ChatWindowMillis) from.ChatTimes.Dequeue();
        if (from.ChatTimes.Count >= limits.MaxChatPerWindow) return;
        from.ChatTimes.Enqueue(now);
        if (text.Length > limits.MaxChatChars) text = text[..limits.MaxChatChars];
        var line = new LobbyChat { Text = text, Sender = DisplayNameOf(from.BoundCivId) };
        foreach (var target in sessions.Values.ToList()) await SafeSendAsync(target, line);
    }

    /// <summary>
    /// C090: a map ping from a bound player, relayed as a <see cref="MapPingNotice"/> (with the sender's bound display name) to the sender and to the open sessions of civs allied with the
    /// sender (a defensive pact). Dropped silently: an unknown kind, a tile off the map or one the sender has not explored, and anything over <see cref="HostLimits.MaxPingPerWindow"/> per window.
    /// A recipient who has not explored the tile gets nothing, so a ping never shows an ally a place they do not know. Not an engine command: nothing is logged, hashed or changed.
    /// </summary>
    private async Task ProcessMapPingAsync(HostSession from, MapPing ping)
    {
        if (!MapPingKinds.All.Contains(ping.Kind)) return;
        var tile = session.Game.TileMap.GetOrNull(ping.X, ping.Y);
        var sender = session.Game.GetCivilizationOrNull(from.BoundCivId);
        if (tile == null || sender == null || !tile.IsExplored(sender)) return;
        long now = clock.NowMillis;
        while (from.PingTimes.Count > 0 && now - from.PingTimes.Peek() >= limits.PingWindowMillis) from.PingTimes.Dequeue();
        if (from.PingTimes.Count >= limits.MaxPingPerWindow) return;
        from.PingTimes.Enqueue(now);
        var notice = new MapPingNotice { X = ping.X, Y = ping.Y, Kind = ping.Kind, Sender = PingSenderName(from.BoundCivId) };
        foreach (var target in sessions.Values.ToList())
        {
            if (target != from && !CanReceivePing(sender, tile, target)) continue;
            await SafeSendAsync(target, notice);
        }
    }

    /// <summary>The bound display name as a ping label: control characters removed, at most <see cref="HostLimits.MaxPingNameChars"/> characters (a joiner chooses their own name, and a ping draws it over the map).</summary>
    private string PingSenderName(string civId)
    {
        var clean = new string(DisplayNameOf(civId).Where(it => !char.IsControl(it)).ToArray()).Trim();
        if (clean.Length > limits.MaxPingNameChars) clean = clean[..limits.MaxPingNameChars].TrimEnd();
        return clean.Length == 0 ? civId : clean;
    }

    private bool CanReceivePing(Unciv.Logic.Civilization.Civilization sender, Unciv.Logic.Map.Tiles.Tile tile, HostSession target)
    {
        if (!target.IsOpen || string.Equals(target.BoundCivId, sender.CivId, StringComparison.Ordinal)) return false;
        var civ = session.Game.GetCivilizationOrNull(target.BoundCivId);
        return civ != null && sender.GetDiplomacyManager(civ)?.DiplomaticStatus == Unciv.Logic.Civilization.Diplomacy.DiplomaticStatus.DefensivePact && tile.IsExplored(civ);
    }

    /// <summary>The humans of the game and the players whose seat is bound, as a <see cref="LobbyState"/> (what the turn bar and the player list read).</summary>
    private LobbyState BuildRoster()
    {
        var meta = store?.ReadMeta();
        var slots = new List<Unciv.Net.LobbySlot>();
        foreach (var civ in session.Game.Civilizations.Where(it => it.IsMajorCiv() && !it.IsSpectator()))
        {
            var binding = meta?.Slots.GetValueOrDefault(civ.CivId);
            if (!civ.IsHuman() && binding == null) continue;
            slots.Add(new Unciv.Net.LobbySlot
            {
                Slot = slots.Count,
                PlayerName = binding == null || binding.DisplayName.Length == 0 ? null : binding.DisplayName,
                Nation = civ.CivId,
                Ready = false,
                Kind = civ.IsHuman() ? "Human" : "AI",
                PlayerKeyId = binding is { PlayerKey.Length: > 0 } ? binding.PlayerKey : null,
                Connected = sessions.Values.Any(it => it.IsOpen && string.Equals(it.BoundCivId, civ.CivId, StringComparison.Ordinal)),
            });
        }
        return new LobbyState
        {
            Settings = meta?.LobbySettings ?? System.Text.Json.JsonDocument.Parse("{}").RootElement.Clone(),
            Slots = slots,
            HostName = "",
        };
    }

    /// <summary>Sends the current roster to every open session when the host was created with <c>announceRoster</c>; otherwise nothing is sent.</summary>
    public async Task AnnounceRosterAsync()
    {
        if (Standby.KeyId != null) await AnnounceSuccessorAsync(); // T281: the roster rides with the backup-host announcement
        if (!announceRoster || sessions.Count == 0) return;
        var roster = BuildRoster();
        var targets = sessions.Values.ToList();
        foreach (var target in targets) await SafeSendAsync(target, roster);
    }

    /// <summary>
    /// Host administration (called by the host player's process, never reachable from a client frame): the player of <paramref name="civId"/> is disconnected with <c>kicked</c>, the seat
    /// binding is cleared so nobody can come back with the old key, and the AI takes the civ over at the next turn boundary (T267: <see cref="SetController"/> then, never mid-turn). Null on success, otherwise a refusal code:
    /// <c>unknown-civ</c>, <c>not-bound</c>, <c>no-store</c> or the command's own (<c>turn-resolving</c> while the round resolves).
    /// </summary>
    public Task<string?> KickCivAsync(string civId) => AdminAsync(civId, null);

    /// <summary>
    /// Host administration: the seat of <paramref name="civId"/> now belongs to <paramref name="newPlayerKey"/> (a player who lost their key or changed device). The old player is
    /// disconnected, the civ plays as AI until the new key connects, and that connection takes it back (the same path as a reconnect after an AI takeover). Refusals: those of
    /// <see cref="KickCivAsync"/> plus <c>bad-key</c>, <c>key-in-use</c> and <c>unchanged</c>.
    /// </summary>
    public Task<string?> RebindCivAsync(string civId, string newPlayerKey) => AdminAsync(civId, newPlayerKey);

    private Task<string?> AdminAsync(string civId, string? newKey)
    {
        var result = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        EnqueueAsync(async () => result.SetResult(await AdminCoreAsync(civId, newKey))).ContinueWith(t =>
        {
            if (t.IsFaulted) result.TrySetException(t.Exception!.InnerExceptions);
        }, TaskScheduler.Default);
        return result.Task;
    }

    private async Task<string?> AdminCoreAsync(string civId, string? newKey)
    {
        var civ = session.Game.GetCivilizationOrNull(civId);
        if (civ == null || !civ.IsMajorCiv() || civ.IsSpectator()) return "unknown-civ";
        if (store == null) return "no-store";
        var meta = store.ReadMeta();
        if (!meta.Slots.TryGetValue(civId, out var binding)) return "not-bound";
        if (newKey != null)
        {
            if (!PlayerKeys.IsValid(newKey)) return "bad-key";
            if (string.Equals(binding.PlayerKey, newKey, StringComparison.Ordinal)) return "unchanged";
            if (meta.Slots.Any(it => string.Equals(it.Value.PlayerKey, newKey, StringComparison.Ordinal))) return "key-in-use";
        }

        // First the checks: refused (resolving, last human) means nothing else changes.
        // A kick hands nothing over now (T267: the AI takes the civ at the next turn boundary); a rebind hands it over at once, and the new key takes it straight back.
        if (civ.IsHuman())
        {
            if (newKey == null)
            {
                if (session.Game.TurnState is { Phase: SimultaneousPhase.Resolving }) return CommandErrors.TurnResolving;
                if (!turnTimer.HasOtherHumans(civ))
                    return CommandErrors.NotAllowed;
            }
            else
            {
                var handed = ApplyServerCommand(new SetController { CivId = civId, Controller = Unciv.Logic.Civilization.PlayerType.AI });
                if (!handed.Accepted) return handed.Validation.Code;
            }
        }

        turnTimer.ForgetPlayer(civId); // no pending reclaim, takeover or remembered key of the old player may act on the seat
        if (newKey == null && civ.IsHuman()) turnTimer.Depart(civId);
        var slots = new SortedDictionary<string, SlotBinding>(meta.Slots, StringComparer.Ordinal)
        {
            [civId] = new SlotBinding { PlayerKey = newKey ?? "", DisplayName = newKey == null ? binding.DisplayName : "" },
        };
        store.WriteMeta(meta with { Slots = slots });
        if (Standby.IsDesignated(binding.PlayerKey)) await RevokeBackupCoreAsync(); // the seat changed hands: the old player is no longer a backup host

        foreach (var old in sessions.Values.Where(it => string.Equals(it.BoundCivId, civId, StringComparison.Ordinal)).ToList())
        {
            await SafeSendAsync(old, new JoinRefused { Code = JoinRefusedCodes.Kicked });
            await DetachSessionAsync(old.SessionId);
        }
        await AnnounceRosterAsync();
        return null;
    }

    private Dictionary<string, ViewDeltaBuilder.ViewMark> CaptureViewMarks()
    {
        var marks = new Dictionary<string, ViewDeltaBuilder.ViewMark>(StringComparer.Ordinal);
        foreach (var s in sessions.Values)
        {
            if (!s.IsOpen) continue;
            var civ = session.Game.GetCivilizationOrNull(s.BoundCivId);
            if (civ != null)
            {
                marks[s.SessionId] = ViewDeltaBuilder.Capture(session.Game, civ);
            }
        }
        return marks;
    }

    private void BroadcastDelta(Dictionary<string, ViewDeltaBuilder.ViewMark> marks, Unciv.Commands.CommandResult result)
    {
        var turnStatusDto = CreateTurnStatusDto();
        foreach (var s in sessions.Values)
        {
            if (!s.IsOpen || s.PreviousSnapshot == null) continue;
            if (!marks.TryGetValue(s.SessionId, out var mark)) continue;
            var civ = session.Game.GetCivilizationOrNull(s.BoundCivId);
            if (civ == null) continue;

            var (delta, after) = ViewDeltaBuilder.BuildWithDiff(
                session.Game,
                civ,
                s.PreviousSnapshot,
                mark,
                result.Events.OfType<EngineEvent>(),
                result.Seq,
                turnStatusDto);

            s.PreviousSnapshot = after;
            _ = SafeSendAsync(s, delta);
        }
    }

    private TurnStatusDto CreateTurnStatusDto()
    {
        var game = session.Game;
        return new TurnStatusDto
        {
            Ended = game.TurnState?.Ended.ToList() ?? [],
            Deadline = turnTimer.TurnDeadline,
            Phase = (game.TurnState?.Phase ?? SimultaneousPhase.Playing).ToString()
        };
    }

    private static async Task SafeSendAsync(HostSession hostSession, Message message)
    {
        if (!hostSession.IsOpen) return;
        try
        {
            string frame = ProtocolCodec.Encode(message);
            await hostSession.Transport.SendAsync(frame);
        }
        catch (InvalidOperationException)
        {
            // Transport closed
        }
    }

    private async Task RunActorLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (await mailbox.Reader.WaitToReadAsync(cancellationToken))
            {
                while (mailbox.Reader.TryRead(out var action))
                {
                    await action();
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task RunSessionReaderAsync(HostSession hostSession, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && hostSession.IsOpen)
            {
                string? frame = await hostSession.Transport.ReceiveAsync(cancellationToken);
                if (frame == null) break;

                hostSession.LastInboundMillis = clock.NowMillis;
                var message = ProtocolCodec.Decode(frame, ProtocolVersion.MaxFrameBytes);
                if (message is Ping)
                {
                    // Answered here, not on the actor: a long actor job (an AI turn, a big resolve) must not make a live client look dead.
                    await SafeSendAsync(hostSession, new Pong());
                    continue;
                }
                await HandleInboundAsync(hostSession.SessionId, message);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { }
        finally
        {
            await EnqueueAsync(() => DetachSessionAsync(hostSession.SessionId));
        }
    }
}
