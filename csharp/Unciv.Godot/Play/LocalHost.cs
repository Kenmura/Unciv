using Unciv.Commands;
using Unciv.Commands.Turn;
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
    private readonly ViewerSessionFlow flow;

    public LocalHost(GameSession session, string humanCivId, Func<long> clock)
    {
        this.session = session;
        ViewerCivId = humanCivId;
        flow = new ViewerSessionFlow(session, humanCivId, clock, Publish);
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

    public Task<SubmitResult> SubmitAsync(GameCommand command) => Task.FromResult(flow.Submit(command));

    public Task<TurnOutcome> EndTurnAsync() => Task.FromResult(flow.EndTurn());

    private ClientSnapshot BuildSnapshot()
    {
        var game = session.Game;
        // Stopgap owned by C036: the multiplayer TurnStatus (who has ended, deadline); single player only ever shows this "Playing" placeholder.
        return SnapshotBuilder.Build(game, game.GetCivilization(ViewerCivId), session.LastSeq, new TurnStatusDto { Ended = [], Phase = "Playing" });
    }

    // TODO(T180): a full snapshot is rebuilt after every accepted command; the network session applies ViewDeltas to its mirror instead.
    private void Publish(long seq, IReadOnlyList<GameEvent> visible)
    {
        Snapshot = BuildSnapshot();
        SnapshotChanged?.Invoke(Snapshot);
        EventsReceived?.Invoke(new EventBatch(seq, visible));
    }
}
