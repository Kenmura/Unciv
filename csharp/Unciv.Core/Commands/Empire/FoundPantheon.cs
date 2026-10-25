using Unciv.Models.Ruleset;

namespace Unciv.Commands.Empire;

/// <summary>
/// Found or expand a pantheon. Kotlin: NextTurnAction FoundPantheon / ExpandPantheon (<c>canFoundPantheon</c> / <c>canExpandPantheon</c>), then
/// <c>PantheonPickerScreen</c>: an unowned, allowed Pantheon belief, applied with <c>chooseBeliefs(listOf(belief), usingFreeBeliefs())</c>.
/// </summary>
[CommandType("found-pantheon")]
public sealed class FoundPantheon : GameCommand
{
    public string Belief { get; init; } = "";

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.RequireActive().TryGet(out var actor, out var failure)) return failure;
        if (!actor.ReligionManager.CanFoundOrExpandPantheon())
            return CommandValidation.Fail(CommandErrors.NotAllowed, "A pantheon cannot be founded now");
        if (!actor.GameInfo.Ruleset.Beliefs.TryGetValue(Belief, out var belief) || belief.Type != BeliefType.Pantheon)
            return CommandValidation.Fail(CommandErrors.InvalidTarget, $"\"{Belief}\" is not a pantheon belief");
        return actor.ReligionManager.GetReligionWithBelief(belief) == null && ReligionCommandChecks.BeliefIsAllowed(belief, actor)
            ? CommandValidation.Ok
            : CommandValidation.Fail(CommandErrors.NotAllowed, $"{Belief} is not available");
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        var manager = context.Actor.ReligionManager;
        manager.ChooseBeliefs([context.Game.Ruleset.Beliefs[Belief]], manager.UsingFreeBeliefs());
        return [];
    }
}
