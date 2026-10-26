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
        var human = game.Civilizations.FirstOrDefault(it => it.IsHuman());
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

    public Task<TurnOutcome> AutoPlayTurnAsync() => Task.FromResult(flow.AutoPlayTurn());

    public Task<string?> ExportSaveAsync() => Task.FromResult<string?>(UncivFiles.GameInfoToString(session.Game, forceZip: true));

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
