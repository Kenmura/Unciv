using Unciv.Logic;
using Unciv.Utils;

namespace Unciv.Commands.Turn;

/// <summary>
/// Play the actor's turn with the AI and end it: the job of <c>NextTurnButton.update</c> while <c>shouldContinueAutoPlaying</c>
/// (<c>TurnManager.automateTurn()</c>, <c>worldScreen.nextTurn()</c>, <c>endTurnMultiturnAutoPlay()</c>). Pending decisions do not block it, the
/// automation resolves them. The auto-play stops when the game was won or the civ defeated (<c>WorldScreen.update</c>).
/// </summary>
[CommandType("auto-play-turn")]
public sealed class AutoPlayTurn : GameCommand, ITimedCommand
{
    public long Now { get; set; }

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.RequireActive().TryGet(out var actor, out var failure)) return failure;
        // TODO(T202): ending the turn of an auto-played civ in a simultaneous round (mark it ended instead of advancing the cycle).
        if (context.Game.TurnState is { Mode: TurnMode.Simultaneous })
            return CommandValidation.Fail(CommandErrors.NotAllowed, "Auto-play turns are not supported in simultaneous games yet");
        if (context.Game.ClientState.AutoPlay is not AutoPlayState { } state || state.CivId != actor.CivId || !state.ShouldContinueAutoPlaying())
            return CommandValidation.Fail(CommandErrors.NotAllowed, "Auto-play is not running for " + actor.CivId);
        failure = context.CanEndTurnOf(actor);
        return failure.IsValid ? context.CheckClock(Now) : failure;
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        var game = context.Game;
        var actor = context.Actor;
        var state = (AutoPlayState)game.ClientState.AutoPlay.NotNull();
        state.AutoPlayTurnInProgress = true;
        new Logic.Civilization.Managers.TurnManager(actor).AutomateTurn();
        state.AutoPlayTurnInProgress = false;
        game.EndPlayerTurn(Now, actor);
        state.EndTurnMultiturnAutoPlay();
        if (actor.IsDefeated() || (game.VictoryData != null && !game.OneMoreTurnMode))
            state.StopAutoPlay();
        return [];
    }
}
