namespace Unciv.Commands.Turn;

/// <summary>The player closed the policy picker without choosing. Kotlin: <c>CivView.tryDismissPolicyPicker</c> clears <c>PolicyManager.shouldOpenPolicyPicker</c> (saved state).</summary>
[CommandType("dismiss-policy-picker")]
public sealed class DismissPolicyPicker : GameCommand
{
    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.RequireActive().TryGet(out var actor, out var failure)) return failure;
        return actor.Policies.ShouldOpenPolicyPicker
            ? CommandValidation.Ok
            : CommandValidation.Fail(CommandErrors.StaleReference, "The policy picker is not pending");
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        context.Actor.Policies.ShouldOpenPolicyPicker = false;
        return [];
    }
}
