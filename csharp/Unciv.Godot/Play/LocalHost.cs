using Unciv.Commands;
using Unciv.Commands.Turn;
using Unciv.GodotClient.Setup;
using Unciv.Logic;
using Unciv.Logic.Files;
using Unciv.Models.Ruleset;
using Unciv.Net.Visibility;
using Unciv.Queries;
using Unciv.View;

namespace Unciv.GodotClient.Play;

/// <summary>The result of <see cref="LocalHost.Start"/>: the session, or the reasons there is none.</summary>
public sealed record StartOutcome(LocalHost? Host, IReadOnlyList<string> Errors);

/// <summary>
/// The single-player implementation of <see cref="IGameSession"/> and the only client class that holds the engine's <see cref="GameSession"/> (C037). It is the single-player authority around a <see cref="GameSession"/> (C010): the one human's commands go in as that civ, and after End turn this
/// class issues the server commands the engine's turn flow needs (<see cref="RunAiTurn"/> per auto civ, then <see cref="BeginTurn"/> for the human),
/// exactly the sequence a hosted game's turn controller will issue (T202/T204). Everything goes through <see cref="GameSession"/>, so the command log
/// stays complete and replays headlessly. It reads no clock: the caller supplies one (wall time in the game, a counter in scenarios).
/// C029 hotseat: a second human is another viewer of the same game. When End turn reaches that human's turn the outcome names it (<see cref="TurnOutcome.HandOverTo"/>) and the flow stops;
/// <see cref="TakeOverAsync"/> then retires this session (every later call is refused) and returns a fresh one for the other human, so the previous player's view and identity cannot be used again.
/// </summary>
public sealed class LocalHost : IGameSession
{
    private readonly GameSession session;
    private readonly ViewerSessionFlow flow;
    private readonly Func<long> clock;
    private volatile bool retired;

    // C040: the engine is not thread safe and End turn runs on a worker, so every engine access (commands, queries, saving, snapshots) takes this gate.
    // Waiting for it is async, so the UI thread is never blocked by a running turn; an uncontended wait completes synchronously.
    private readonly SemaphoreSlim gate = new(1, 1);
    private volatile bool endingTurn;
    private List<(long Seq, IReadOnlyList<GameEvent> Events)>? collected; // only touched while the gate is held

    public LocalHost(GameSession session, string humanCivId, Func<long> clock)
    {
        this.session = session;
        this.clock = clock;
        ViewerCivId = humanCivId;
        flow = new ViewerSessionFlow(session, humanCivId, clock, Publish);
        Snapshot = BuildSnapshot();
    }

    /// <summary>
    /// C016: a session around a saved game text (the human is the first human civ, so a save of a single-player game resumes as that player).
    /// Throws <see cref="InvalidDataException"/> with a player-readable message when the text is not a loadable save with a human in it.
    /// </summary>
    public static LocalHost FromSave(string saveText, Func<long> clock)
    {
        GameInfo game;
        try
        {
            game = UncivFiles.GameInfoFromString(saveText);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // any parse failure of a file the player picked is a damaged save, whatever the serializer threw
            throw new InvalidDataException("The save is damaged or from an incompatible version: " + e.Message, e);
        }
        // C029: a hotseat save resumes as the human whose turn it is (else as the first human)
        var human = (game.TurnPhase is TurnPhase.AwaitingInput(var waiting) && waiting.IsHuman() ? waiting : null) ?? game.Civilizations.FirstOrDefault(it => it.IsHuman());
        if (human == null) throw new InvalidDataException("The save has no human player");
        return new LocalHost(new GameSession(game), human.CivName, clock);
    }

    /// <summary>
    /// C015: starts a new game from the setup form through <see cref="NewGameLauncher"/> (<c>NewGame.Start</c>: validation, then GameStarter) and wraps it for the civ the form's human slot ended up with
    /// (a random nation is drawn by the seeded GameStarter). Invalid setups come back as <see cref="StartOutcome.Errors"/>, never as exceptions.
    /// </summary>
    public static StartOutcome Start(NewGameForm form, Func<long> clock)
    {
        var launched = NewGameLauncher.Launch(form, clock());
        return launched.Game == null
            ? new StartOutcome(null, launched.Errors)
            : new StartOutcome(new LocalHost(new GameSession(launched.Game), launched.HumanCiv, clock), []);
    }

    /// <summary>The engine session, for the composition root and the harness (hashing a loaded game); client screens use only <see cref="IGameSession"/>.</summary>
    public GameSession Session => session;

    public string ViewerCivId { get; }

    public Ruleset Ruleset => session.Game.Ruleset;

    public ClientSnapshot Snapshot { get; private set; }

    public event Action<ClientSnapshot>? SnapshotChanged;

    public event Action<EventBatch>? EventsReceived;

    // A local session shares the engine's memory, so it can never miss a batch or lose a connection: it never raises this.
    // TODO(T180): the network ClientSession raises it on a sequence gap or reconnect and then resyncs from a snapshot.
    public event Action<string>? Desynchronized
    {
        add { }
        remove { }
    }

    /// <summary>True while an End turn is being processed on the worker (commands and queries wait for it).</summary>
    public bool IsProcessingTurn => endingTurn;

    /// <summary>C029: true once <see cref="TakeOverAsync"/> handed the game to another viewer; this session then refuses everything.</summary>
    public bool IsRetired => retired;

    /// <summary>C029: it is this viewer's turn but the turn has not begun (a hotseat player who has not taken over yet, or a loaded hotseat game); <see cref="TakeOverAsync"/> begins it.</summary>
    public bool TurnNeedsBegin => flow.ViewerTurnPending;

    /// <summary>The <see cref="SubmitResult.Message"/> / <see cref="TurnOutcome.Message"/> of a call on a session that was handed over.</summary>
    public const string RetiredMessage = "This player's session was handed over";

    /// <summary>
    /// C029: hotseat. Gives <paramref name="civId"/>, the human whose turn is next (or this session's own viewer when its turn has not begun, as after loading a hotseat game), the game: its turn is begun,
    /// and the returned session views the game as that civ. For another civ this session is retired first, so a screen that still holds it cannot read or command the game as the previous player.
    /// Throws <see cref="InvalidOperationException"/> when it is nobody's turn to take over, the civ is not the human whose turn is next, or a turn is being processed.
    /// </summary>
    public async Task<LocalHost> TakeOverAsync(string civId)
    {
        if (retired) throw new InvalidOperationException(RetiredMessage);
        if (endingTurn) throw new InvalidOperationException("The turn is still being processed");
        var next = civId == ViewerCivId ? this : NextViewer(civId);
        if (!next.TurnNeedsBegin) throw new InvalidOperationException("It is not " + civId + "'s turn to begin");
        var began = await next.SubmitCommandAsync(next.flow.BeginPendingTurn);
        if (!began.Accepted) throw new InvalidOperationException(began.Message);
        if (next != this) retired = true;
        return next;
    }

    private LocalHost NextViewer(string civId)
    {
        if (flow.HandOverTarget != civId) throw new InvalidOperationException(civId + " is not the human whose turn is next");
        return new LocalHost(session, civId, clock);
    }

    public async Task ResyncAsync()
    {
        if (retired) return;
        await gate.WaitAsync();
        try
        {
            Snapshot = BuildSnapshot();
        }
        finally
        {
            gate.Release();
        }
        SnapshotChanged?.Invoke(Snapshot);
    }

    public async Task<TResult?> QueryAsync<TResult>(IQuery<TResult> query) where TResult : class
    {
        if (retired) return null;
        await gate.WaitAsync();
        try
        {
            var game = session.Game;
            return query.Run(new GameView(game, game.GetCivilization(ViewerCivId)));
        }
        catch (QueryException)
        {
            return null;
        }
        finally
        {
            gate.Release();
        }
    }

    public Task<SubmitResult> SubmitAsync(GameCommand command) => SubmitCommandAsync(() => flow.Submit(command));

    private async Task<SubmitResult> SubmitCommandAsync(Func<SubmitResult> run)
    {
        if (retired) return new SubmitResult(false, RetiredMessage, []);
        await gate.WaitAsync();
        var batches = new List<(long Seq, IReadOnlyList<GameEvent> Events)>();
        ClientSnapshot? fresh = null;
        try
        {
            collected = batches;
            return run();
        }
        finally
        {
            collected = null;
            try
            {
                if (batches.Count > 0) fresh = BuildSnapshot();
            }
            finally
            {
                gate.Release(); // review C040: a throwing snapshot must never leave the gate held (every later call would wait forever)
            }
            Raise(fresh, batches); // after the gate is released: a handler may call back into the session
        }
    }

    /// <summary>
    /// C040: the whole End turn (the human's end, every automatic civ, the human's next begin) runs on a worker thread holding the engine gate; the caller's thread only awaits.
    /// Batches published meanwhile are kept and raised, with the final snapshot, on the caller's context once the turn is over (also when it was cancelled or failed
    /// part way, because the commands that did run changed the game). A second End turn while one runs is refused. The worker releases the gate itself, so nothing
    /// the turn needs depends on the caller's thread being free.
    /// </summary>
    public Task<TurnOutcome> EndTurnAsync(IProgress<TurnProgress>? progress = null, CancellationToken cancellation = default) =>
        PumpTurnAsync(() => flow.EndTurn(progress, cancellation));

    /// <summary>C047: the AI plays the viewer's turn; the automatic civs after it are processed off the UI thread exactly like <see cref="EndTurnAsync"/>.</summary>
    public Task<TurnOutcome> AutoPlayTurnAsync(IProgress<TurnProgress>? progress = null, CancellationToken cancellation = default) =>
        PumpTurnAsync(() => flow.AutoPlayTurn(progress, cancellation));

    private async Task<TurnOutcome> PumpTurnAsync(Func<TurnOutcome> run)
    {
        if (retired) return new TurnOutcome(false, RetiredMessage, 0, false, []);
        if (endingTurn) return new TurnOutcome(false, "The turn is already being processed", 0, false, []);
        endingTurn = true;
        var batches = new List<(long Seq, IReadOnlyList<GameEvent> Events)>();
        ClientSnapshot? fresh = null;
        try
        {
            await gate.WaitAsync(CancellationToken.None);
            return await Task.Run(() =>
            {
                collected = batches;
                try
                {
                    return run();
                }
                finally
                {
                    collected = null;
                    try
                    {
                        fresh = BuildSnapshot();
                    }
                    finally
                    {
                        gate.Release(); // review C040: released even when the snapshot throws
                    }
                }
            }, CancellationToken.None);
        }
        finally
        {
            endingTurn = false; // review C040: cleared only here, on the caller's context, so a second End turn cannot slip in between the worker finishing and this turn's results being raised
            Raise(fresh, batches);
        }
    }

    private void Raise(ClientSnapshot? fresh, List<(long Seq, IReadOnlyList<GameEvent> Events)> batches)
    {
        if (fresh == null) return;
        // review C040: a command that waited on the gate while the turn ran can finish (and raise a newer snapshot) before the turn's continuation runs; never go backwards
        if (fresh.Seq >= Snapshot.Seq) Snapshot = fresh;
        SnapshotChanged?.Invoke(Snapshot);
        foreach (var (seq, events) in batches) EventsReceived?.Invoke(new EventBatch(seq, events));
    }

    /// <summary>
    /// C029 (review): a sequential game between one human's End turn and the next human's turn begin cannot be saved: the save does not record whose turn comes next, so a loaded copy would resume as
    /// the human who just ended the turn, with that turn running again. The export is refused (null) in that window; <see cref="TakeOverAsync"/> ends it.
    /// </summary>
    public bool CanExportSave => !retired && !flow.ViewerTurnPending && flow.HandOverTarget == null;

    public async Task<string?> ExportSaveAsync()
    {
        await gate.WaitAsync();
        try
        {
            if (!CanExportSave) return null;
            return UncivFiles.GameInfoToString(session.Game, forceZip: true);
        }
        finally
        {
            gate.Release();
        }
    }

    private ClientSnapshot BuildSnapshot()
    {
        var game = session.Game;
        // Stopgap owned by C036: the multiplayer TurnStatus (who has ended, deadline); single player only ever shows this "Playing" placeholder.
        return SnapshotBuilder.Build(game, game.GetCivilization(ViewerCivId), session.LastSeq, new TurnStatusDto { Ended = [], Phase = "Playing" });
    }

    // TODO(T180): a full snapshot is rebuilt after every accepted command; the network session applies ViewDeltas to its mirror instead.
    // Every command runs inside SubmitAsync or EndTurnAsync under the gate, which collect what is published; nothing is raised from inside the engine call.
    private void Publish(long seq, IReadOnlyList<GameEvent> visible) =>
        (collected ?? throw new InvalidOperationException("A command ran outside the session gate")).Add((seq, visible));
}
