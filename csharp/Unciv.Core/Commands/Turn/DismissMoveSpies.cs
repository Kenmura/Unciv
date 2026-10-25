namespace Unciv.Commands.Turn;

/// <summary>The player closed the move-spies screen without moving. Kotlin: <c>CivView.tryDismissMoveSpies</c> sets <c>EspionageManager.dismissedShouldMoveSpies</c> (saved state).</summary>
[CommandType("dismiss-move-spies")]
public sealed class DismissMoveSpies : GameCommand
{
    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.RequireActive().TryGet(out var actor, out var failure)) return failure;
        return actor.EspionageManager.ShouldShowMoveSpies()
            ? CommandValidation.Ok
            : CommandValidation.Fail(CommandErrors.StaleReference, "Moving spies is not pending");
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        context.Actor.EspionageManager.DismissedShouldMoveSpies = true;
        return [];
    }
}
