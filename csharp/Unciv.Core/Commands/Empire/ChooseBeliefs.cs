using Unciv.Logic.Civilization.Managers;

namespace Unciv.Commands.Empire;

/// <summary>
/// Choose the beliefs of an enhancement (Kotlin NextTurnAction EnhanceReligion, slots <c>getBeliefsToChooseAtEnhancing</c>) or free beliefs
/// (ReformReligion, slots <c>freeBeliefsAsEnums</c>), via <c>ReligiousBeliefsPickerScreen</c> without the icon part. Beliefs are given in slot
/// order. Applied with <c>chooseBeliefs(beliefs, usingFreeBeliefs())</c>.
/// </summary>
[CommandType("choose-beliefs")]
public sealed class ChooseBeliefs : GameCommand
{
    public List<string> Beliefs { get; init; } = [];

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.RequireActive().TryGet(out var actor, out var failure)) return failure;
        var manager = actor.ReligionManager;
        if (manager.Religion == null || manager.ReligionState == ReligionState.FoundingReligion)
            return CommandValidation.Fail(CommandErrors.NotAllowed, "There is no religion to choose beliefs for");
        var slots = manager.ReligionState == ReligionState.EnhancingReligion ? manager.GetBeliefsToChooseAtEnhancing()
            : manager.HasFreeBeliefs() ? manager.FreeBeliefsAsEnums()
            : null;
        if (slots == null)
            return CommandValidation.Fail(CommandErrors.NotAllowed, "There are no beliefs to choose");
        return ReligionCommandChecks.TryBeliefs(actor, Beliefs, slots, out _, out failure) ? CommandValidation.Ok : failure;
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        var manager = context.Actor.ReligionManager;
        var ruleset = context.Game.Ruleset;
        manager.ChooseBeliefs(Beliefs.Select(it => ruleset.Beliefs[it]).ToList(), manager.UsingFreeBeliefs());
        return [];
    }
}
