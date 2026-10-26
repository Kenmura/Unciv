using Unciv.Commands.Events;

namespace Unciv.Commands.Turn;

/// <summary>
/// Simultaneous turns (T202): the actor takes back its <see cref="EndTurn"/> while the round is still being played, so it can act again. Valid only in
/// the Playing phase (<c>turn-resolving</c> once the round is being resolved) and only for a human that ended (<c>not-allowed</c> otherwise, and in
/// Sequential games, which have no end mark). Kotlin has no counterpart: Unciv ends the turn at once.
/// </summary>
[CommandType("undo-end-turn")]
public sealed class UndoEndTurn : GameCommand
{
    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.RequireActive(allowDefeated: true, allowEnded: true).TryGet(out var actor, out var failure)) return failure;
        if (!context.IsSimultaneous())
            return CommandValidation.Fail(CommandErrors.NotAllowed, "Only simultaneous turns can be taken back");
        return context.Game.HasEndedTurn(actor)
            ? CommandValidation.Ok
            : CommandValidation.Fail(CommandErrors.NotAllowed, $"{actor.CivId} has not ended the turn");
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        context.Game.UndoEndHumanTurn(context.Actor);
        return [new PlayerUndidEndTurn(context.ActorId, context.Game.Turns)];
    }
}
