using System.Threading.Channels;
using Unciv.Commands;
using Unciv.Commands.Events;
using Unciv.Logic;
using Unciv.Net;
using Unciv.Net.Persistence;
using Unciv.Net.Remote;
using Unciv.Net.Turns;
using Unciv.Net.Visibility;
using Unciv.Queries;
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
    public bool IsOpen => Transport.IsOpen;
}

/// <summary>
/// Single-threaded authoritative actor hosting one game (SERVER.md 1, 5, 6).
/// Owns the GameSession single writer, TurnTimer, RemoteQueryHost, and CommandGate.
/// Distributes ViewDelta with SnapshotDiff and ViewHash after every accepted command
/// and server command to all connected human/spectator sessions.
/// </summary>
public sealed class GameHost : IAsyncDisposable
{
    private readonly GameSession session;
    private readonly IGameStore? store;
    private readonly TurnTimer turnTimer;
    private readonly RemoteQueryHost remoteQueries;
    private readonly CommandGate gate;
    private readonly IClock clock;
    private readonly Dictionary<string, HostSession> sessions = new(StringComparer.Ordinal);
    private readonly Channel<Func<Task>> mailbox = Channel.CreateUnbounded<Func<Task>>();
    private readonly CancellationTokenSource cts = new();
    private Task? actorTask;
    private bool closed;

    public GameHost(
        GameSession session,
        IGameStore? store = null,
        TurnTimer? turnTimer = null,
        RemoteQueryHost? remoteQueries = null,
        CommandGate? gate = null,
        IClock? clock = null,
        bool startActor = true)
    {
        this.session = session;
        this.store = store;
        this.clock = clock ?? SystemClock.Instance;
        this.turnTimer = turnTimer ?? new TurnTimer(session, TurnPolicy.Default, this.clock, autoResolve: false);
        this.turnTimer.AutoResolve = false;
        this.turnTimer.ServerCommandSubmitter = ApplyServerCommand;
        this.remoteQueries = remoteQueries ?? CreateDefaultRemoteQueryHost();
        this.gate = gate ?? new CommandGate(store, null, this.clock);

        if (startActor)
        {
            Start();
        }
    }

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
    public async Task AttachSessionAsync(string sessionId, string boundCivId, ITransport transport, string? playerKeyId = null)
    {
        var completion = new TaskCompletionSource<bool>();
        await mailbox.Writer.WriteAsync(async () =>
        {
            try
            {
                var civ = session.Game.GetCivilizationOrNull(boundCivId);
                var turnStatusDto = CreateTurnStatusDto();
                ClientSnapshot? initialSnapshot = null;
                if (civ != null)
                {
                    initialSnapshot = SnapshotBuilder.Build(session.Game, civ, session.LastSeq, turnStatusDto);
                }

                var hostSession = new HostSession
                {
                    SessionId = sessionId,
                    BoundCivId = boundCivId,
                    PlayerKeyId = playerKeyId,
                    Transport = transport,
                    PreviousSnapshot = initialSnapshot
                };

                // A reconnect replaces the civ's older session (its late detach must not mark the new one disconnected).
                foreach (var stale in sessions.Values.Where(x => string.Equals(x.BoundCivId, boundCivId, StringComparison.Ordinal)).ToList())
                {
                    await DetachSessionAsync(stale.SessionId);
                }

                sessions[sessionId] = hostSession;
                turnTimer.Connect(boundCivId, playerKeyId);

                if (initialSnapshot != null)
                {
                    var snapshotMsg = SnapshotBuilder.ToMessage(initialSnapshot);
                    await transport.SendAsync(ProtocolCodec.Encode(snapshotMsg));
                }

                _ = RunSessionReaderAsync(hostSession, cts.Token);
                completion.SetResult(true);
            }
            catch (Exception ex)
            {
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
                    await SendFullSnapshotAsync(hostSession);
                    break;
                case Ping:
                    await SafeSendAsync(hostSession, new Pong());
                    break;
                case Leave:
                    await DetachSessionAsync(sessionId);
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
            BroadcastDelta(marks, result);
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
            ApplyServerCommand(cmd);
            steps++;
        }
        return steps;
    }

    /// <summary>Advances turn timer and triggers turn resolution if deadlines or disconnects expired.</summary>
    public void Tick()
    {
        turnTimer.Tick();
        if (session.Game.TurnState is { Phase: SimultaneousPhase.Resolving })
        {
            PumpResolving();
            turnTimer.Tick();
        }
    }

    /// <summary>Closes all sessions, writes clean marker if store is configured, and stops actor.</summary>
    public async Task CloseCleanlyAsync()
    {
        if (closed) return;
        closed = true;

        await EnqueueAsync(async () =>
        {
            // Finish a half-resolved round first so the final snapshot is taken at a safe point (SERVER.md 5.3).
            if (session.Game.TurnState is { Phase: SimultaneousPhase.Resolving })
            {
                PumpResolving();
            }
            foreach (var s in sessions.Values.ToList())
            {
                await SafeSendAsync(s, new HostClosing());
                await s.Transport.CloseAsync();
                remoteQueries.EndSession(s.SessionId);
                gate.EndSession(s.SessionId);
                turnTimer.Disconnect(s.BoundCivId);
            }
            sessions.Clear();
            store?.CloseCleanly(session.Game);
        });

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

        store?.Append(filterResult.Envelope!, command.ClientCmdId);
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
        await SendFullSnapshotAsync(hostSession);
    }

    private static RemoteQueryHost CreateDefaultRemoteQueryHost()
    {
        var registry = new QueryRegistry(Unciv.Json.UncivJson.DefaultOptions);
        registry.RegisterFromAssembly(typeof(QueryRegistry).Assembly);
        return new RemoteQueryHost(registry, RemoteQueryPolicy.Default);
    }

    private async Task SendFullSnapshotAsync(HostSession hostSession)
    {
        var civ = session.Game.GetCivilizationOrNull(hostSession.BoundCivId);
        if (civ == null) return;

        var turnStatusDto = CreateTurnStatusDto();
        var snapshot = SnapshotBuilder.Build(session.Game, civ, session.LastSeq, turnStatusDto);
        hostSession.PreviousSnapshot = snapshot;

        var msg = SnapshotBuilder.ToMessage(snapshot);
        await SafeSendAsync(hostSession, msg);
    }

    private async Task DetachSessionAsync(string sessionId)
    {
        if (sessions.Remove(sessionId, out var hostSession))
        {
            turnTimer.Disconnect(hostSession.BoundCivId);
            remoteQueries.EndSession(sessionId);
            gate.EndSession(sessionId);
            try
            {
                await hostSession.Transport.CloseAsync();
            }
            catch { }
        }
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

                var message = ProtocolCodec.Decode(frame);
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
