using Unciv.Logic.Civilization;
using Unciv.Utils;

namespace Unciv.Commands.Diplomacy;

/// <summary>
/// Make a demand of a major civ ("Please don't spread your religion to us."). Kotlin: <c>MajorCivDiplomacyTable.getDemandsTable</c> lists the
/// <c>Demand.entries</c> whose <c>show(viewingCiv)</c> holds, disabled while the same demand is pending with the other civ or our own manager
/// already carries its <c>agreedToDemand</c> flag; the button adds the demand's popup alert to the other civ. An AI answers in its own turn
/// (<c>respondToDemandAlert</c>), a human with <see cref="RespondToDemand"/>.
/// </summary>
[CommandType("make-demand")]
public sealed class MakeDemand : GameCommand
{
    public string CivId { get; init; } = "";

    /// <summary>The demand's name: DontSpyOnUs, DoNotSpreadReligion, DoNotSettleNearUs (DoNotAttackUs is never offered to a human).</summary>
    public string Demand { get; init; } = "";

    internal static Logic.Civilization.Diplomacy.Demand? Find(string name) => Logic.Civilization.Diplomacy.Demand.Values.FirstOrDefault(it => it.Name == name);

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.TryOtherCiv(CivId, CivKind.Major, out var actor, out var other, out var failure)) return failure;
        var demand = Find(Demand);
        if (demand == null || !demand.Show(actor))
            return CommandValidation.Fail(CommandErrors.InvalidTarget, $"\"{Demand}\" is not a demand you can make");
        if (DiplomacyCommandChecks.FindAlert(other, demand.DemandAlert, actor.CivId) != null
            || actor.GetDiplomacyManager(other).NotNull().HasFlag(demand.AgreedToDemand))
            return CommandValidation.Fail(CommandErrors.NotAllowed, $"{CivId} has already been asked or agreed");
        return CommandValidation.Ok;
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        context.ValidatedOther(CivId).AddPopupAlert(new PopupAlert(Find(Demand).NotNull().DemandAlert, context.ActorId));
        return [];
    }
}
