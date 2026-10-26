using Unciv.Commands;
using Unciv.Commands.Turn;
using Unciv.Logic;

namespace Unciv.Net.Visibility;

/// <summary>What one submitted command did, as the client needs it: whether the host took it and, if not, why, plus the events the viewer may see.</summary>
public sealed record SubmitResult(bool Accepted, string Message, IReadOnlyList<GameEvent> Events);

/// <summary>What one End turn did. <see cref="AiTurnsRun"/> and <see cref="NextTurnBegun"/> are informational: a remote session that cannot tell reports 0 / false.</summary>
public sealed record TurnOutcome(bool Accepted, string Message, int AiTurnsRun, bool NextTurnBegun, IReadOnlyList<GameEvent> Events);

/// <summary>C040: where an End turn is: <see cref="Played"/> of <see cref="Total"/> automatic civs are done, <see cref="CivId"/> is the one about to play (the total is an estimate when the round changes the set of civs).</summary>
public sealed record TurnProgress(int Played, int Total, string CivId);

/// <summary>
/// C037: the single-player command pipeline between a <see cref="GameSession"/> and one viewing human. Every command and every server command the turn
/// flow needs (<see cref="RunAiTurn"/> per auto civ, then <see cref="BeginTurn"/>) goes through <see cref="GameSession"/>, and every event leaving it
/// (<see cref="SubmitResult.Events"/>, <see cref="TurnOutcome.Events"/> and the <see cref="Published"/> callback) passes <see cref="ViewerEventFilter"/>.
/// It lives in Core so a headless test can guard that wiring; the Godot LocalHost only adapts it to IGameSession.
/// </summary>
public sealed class ViewerSessionFlow
{
    private readonly GameSession session;
    private readonly string viewerCivId;
    private readonly Func<long> clock;
    private readonly ViewerEventFilter eventFilter;

    /// <param name="session">The engine session every command goes through.</param>
    /// <param name="viewerCivId">The one human whose commands and view this is.</param>
    /// <param name="clock">Supplies the Now of turn commands (wall time in the game, a counter in scenarios).</param>
    /// <param name="published">Called once per accepted command, after the engine applied it, with its sequence number and the viewer-visible events.</param>
    public ViewerSessionFlow(GameSession session, string viewerCivId, Func<long> clock, Action<long, IReadOnlyList<GameEvent>> published)
    {
        this.session = session;
        this.viewerCivId = viewerCivId;
        this.clock = clock;
        Published = published;
        eventFilter = new ViewerEventFilter(session.Game, viewerCivId);
    }

    private Action<long, IReadOnlyList<GameEvent>> Published { get; }

    private SubmitResult Publish(Unciv.Commands.CommandResult result, ViewDeltaBuilder.ViewMark before)
    {
        if (!result.Accepted) return new SubmitResult(false, result.Validation.Message, []);
        var visible = eventFilter.Filter(before, result.Events);
        Published(result.Seq, visible);
        return new SubmitResult(true, result.Validation.Message, visible);
    }

    public SubmitResult Submit(GameCommand command)
    {
        // C044: a timed command the caller did not stamp (Resign from the menu) gets this session's clock, as the authority would; an explicit Now is kept.
        if (command is ITimedCommand { Now: 0 } timed) timed.Now = clock();
        var before = eventFilter.Mark();
        return Publish(session.Submit(viewerCivId, command), before);
    }

    private SubmitResult ServerCommand(GameCommand command)
    {
        var before = eventFilter.Mark();
        return Publish(session.SubmitAsServer(command), before);
    }

    /// <summary>
    /// Ends the human's turn and plays everything up to the human's next turn (or the end of the game).
    /// C040: <paramref name="progress"/> is told before each automatic civ plays; <paramref name="cancellation"/> is honoured between civs (never inside one), so the game is always at a command boundary.
    /// A cancelled call is a refusal ("cancelled") that keeps what was already played; calling again continues with the civ that was next, without ending the human's turn a second time.
    /// </summary>
    public TurnOutcome EndTurn(IProgress<TurnProgress>? progress = null, CancellationToken cancellation = default) =>
        FinishTurn(() => new EndTurn { Now = clock() }, progress, cancellation);

    /// <summary>
    /// C047: lets the AI play the human's turn (the engine's <see cref="AutoPlayTurn"/>, which needs a running <see cref="StartAutoPlay"/>) and then plays everything up to
    /// the human's next turn exactly as <see cref="EndTurn"/> does (progress and cancellation included). Refused (not accepted) when no auto-play is running, which is how a client sees that it has finished.
    /// </summary>
    public TurnOutcome AutoPlayTurn(IProgress<TurnProgress>? progress = null, CancellationToken cancellation = default) =>
        FinishTurn(() => new AutoPlayTurn { Now = clock() }, progress, cancellation);

    private TurnOutcome FinishTurn(Func<GameCommand> endCommand, IProgress<TurnProgress>? progress, CancellationToken cancellation)
    {
        var events = new List<GameEvent>();
        if (session.Game.TurnPhase is not TurnPhase.AutoPlayers) // else a cancelled turn is being continued
        {
            var ended = Submit(endCommand());
            if (!ended.Accepted) return new TurnOutcome(false, ended.Message, 0, false, events);
            events.AddRange(ended.Events);
        }

        int aiTurns = 0;
        int total = session.Game.PendingAutoPlayerCount();
        while (session.Game.TurnPhase is TurnPhase.AutoPlayers(var next))
        {
            if (cancellation.IsCancellationRequested) return new TurnOutcome(false, CancelledMessage, aiTurns, false, events);
            progress?.Report(new TurnProgress(aiTurns, total, next.CivId));
            var ran = ServerCommand(new RunAiTurn { CivId = next.CivId });
            if (!ran.Accepted) return new TurnOutcome(false, ran.Message, aiTurns, false, events);
            events.AddRange(ran.Events);
            aiTurns++;
        }

        bool begun = false;
        if (session.Game.TurnPhase is TurnPhase.AwaitingInput(var human) && human.CivId == viewerCivId && !session.Game.IsTurnStarted(human))
        {
            var began = ServerCommand(new BeginTurn { CivId = viewerCivId, Now = clock() });
            if (!began.Accepted) return new TurnOutcome(false, began.Message, aiTurns, false, events);
            events.AddRange(began.Events);
            begun = true;
        }
        return new TurnOutcome(true, "", aiTurns, begun, events);
    }

    /// <summary>The <see cref="TurnOutcome.Message"/> of an End turn stopped by its cancellation token.</summary>
    public const string CancelledMessage = "Turn processing was cancelled";
}
