using Unciv.Commands;
using Unciv.Commands.Turn;
using Unciv.Logic;
using Unciv.Models.Ruleset;
using Unciv.Net.Visibility;
using Unciv.Queries;
using Unciv.View;

namespace Unciv.GodotClient.Play;

/// <summary>
/// The single-player implementation of <see cref="IGameSession"/> and the only client class that holds the engine's <see cref="GameSession"/> (C037). It is the single-player authority around a <see cref="GameSession"/> (C010): the one human's commands go in as that civ, and after End turn this
/// class issues the server commands the engine's turn flow needs (<see cref="RunAiTurn"/> per auto civ, then <see cref="BeginTurn"/> for the human),
/// exactly the sequence a hosted game's turn controller will issue (T202/T204). Everything goes through <see cref="GameSession"/>, so the command log
/// stays complete and replays headlessly. It reads no clock: the caller supplies one (wall time in the game, a counter in scenarios).
/// A second human in the game is not handled here (hotseat is C029): the flow stops when it is that civ's turn.
/// </summary>
public sealed class LocalHost : IGameSession
{
    private readonly GameSession session;
    private readonly Func<long> clock;
    private readonly ViewerEventFilter eventFilter;

    public LocalHost(GameSession session, string humanCivId, Func<long> clock)
    {
        this.session = session;
        this.clock = clock;
        ViewerCivId = humanCivId;
        eventFilter = new ViewerEventFilter(session.Game, humanCivId);
        Snapshot = BuildSnapshot();
    }

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

    public Task ResyncAsync()
    {
        Snapshot = BuildSnapshot();
        SnapshotChanged?.Invoke(Snapshot);
        return Task.CompletedTask;
    }

    public Task<TResult?> QueryAsync<TResult>(IQuery<TResult> query) where TResult : class
    {
        try
        {
            var game = session.Game;
            return Task.FromResult<TResult?>(query.Run(new GameView(game, game.GetCivilization(ViewerCivId))));
        }
        catch (QueryException)
        {
            return Task.FromResult<TResult?>(null);
        }
    }

    public Task<SubmitResult> SubmitAsync(GameCommand command) => Task.FromResult(Submit(command));

    public Task<TurnOutcome> EndTurnAsync() => Task.FromResult(EndTurn());

    private ClientSnapshot BuildSnapshot()
    {
        var game = session.Game;
        // Stopgap owned by C036: the multiplayer TurnStatus (who has ended, deadline); single player only ever shows this "Playing" placeholder.
        return SnapshotBuilder.Build(game, game.GetCivilization(ViewerCivId), session.LastSeq, new TurnStatusDto { Ended = [], Phase = "Playing" });
    }

    // TODO(T180): a full snapshot is rebuilt after every accepted command; the network session applies ViewDeltas to its mirror instead.
    private IReadOnlyList<GameEvent> Publish(CommandResult result, ViewDeltaBuilder.ViewMark before)
    {
        var visible = eventFilter.Filter(before, result.Events);
        Snapshot = BuildSnapshot();
        SnapshotChanged?.Invoke(Snapshot);
        EventsReceived?.Invoke(new EventBatch(result.Seq, visible));
        return visible;
    }

    private SubmitResult Submit(GameCommand command)
    {
        var before = eventFilter.Mark();
        var result = session.Submit(ViewerCivId, command);
        var visible = result.Accepted ? Publish(result, before) : [];
        return new SubmitResult(result.Accepted, result.Validation.Message, visible);
    }

    /// <summary>Ends the human's turn and plays everything up to the human's next turn (or the end of the game).</summary>
    private TurnOutcome EndTurn()
    {
        var events = new List<GameEvent>();
        var ended = Submit(new EndTurn { Now = clock() });
        if (!ended.Accepted) return new TurnOutcome(false, ended.Message, 0, false, events);
        events.AddRange(ended.Events);

        int aiTurns = 0;
        while (session.Game.TurnPhase is TurnPhase.AutoPlayers(var next))
        {
            var ran = ServerCommand(new RunAiTurn { CivId = next.CivId });
            if (!ran.Accepted) return new TurnOutcome(false, ran.Message, aiTurns, false, events);
            events.AddRange(ran.Events);
            aiTurns++;
        }

        bool begun = false;
        if (session.Game.TurnPhase is TurnPhase.AwaitingInput(var human) && human.CivId == ViewerCivId && !session.Game.IsTurnStarted(human))
        {
            var began = ServerCommand(new BeginTurn { CivId = ViewerCivId, Now = clock() });
            if (!began.Accepted) return new TurnOutcome(false, began.Message, aiTurns, false, events);
            events.AddRange(began.Events);
            begun = true;
        }
        return new TurnOutcome(true, "", aiTurns, begun, events);
    }

    private SubmitResult ServerCommand(GameCommand command)
    {
        var before = eventFilter.Mark();
        var result = session.SubmitAsServer(command);
        var visible = result.Accepted ? Publish(result, before) : [];
        return new SubmitResult(result.Accepted, result.Validation.Message, visible);
    }
}
