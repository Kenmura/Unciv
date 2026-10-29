namespace Unciv.Commands.Empire;

/// <summary>
/// B014 (not in Kotlin, bnw-spec 4.6.5): switch to the ideology the people prefer. Allowed while the public opinion makes the civ unhappy
/// (<c>SocialPolicyPopup.lua</c>: the button is enabled when opinion unhappiness is above 0) and no anarchy runs. The tenets are lost, some come
/// back as free tenets, and anarchy starts (<see cref="Unciv.Logic.Civilization.Managers.PolicyManager.SwitchIdeology"/>).
/// </summary>
[CommandType("switch-ideology")]
public sealed class SwitchIdeology : GameCommand
{
    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.RequireActive().TryGet(out var actor, out var failure)) return failure;
        return actor.Policies.CanSwitchIdeology()
            ? CommandValidation.Ok
            : CommandValidation.Fail(CommandErrors.NotAllowed, "The civ cannot switch ideology now");
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        context.Actor.Policies.SwitchIdeology();
        return [];
    }
}
