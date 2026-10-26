using Unciv.Utils;

namespace Unciv.Commands.Diplomacy;

/// <summary>
/// Answer a demand another civ made (the human side of <see cref="MakeDemand"/>, and of the AI's own <c>DoNotAttackUs</c> ultimatum). Kotlin:
/// <c>AlertPopup.addDemand</c>, not shown (the alert is only dropped, as here) when the demanding civ is defeated; accepting runs
/// <c>agreeToDemand</c>; refusing runs <c>refuseDemand</c> and, for <c>DoNotAttackUs</c>, declares war once more on top of the declaration
/// <c>refuseDemand</c> already makes (a quirk of the Kotlin popup that is kept).
/// </summary>
[CommandType("respond-demand")]
public sealed class RespondToDemand : GameCommand
{
    /// <summary>The civ that made the demand.</summary>
    public string CivId { get; init; } = "";

    public string Demand { get; init; } = "";

    public bool Accept { get; init; }

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.RequireActive(allowEnded: true).TryGet(out var actor, out var failure)) return failure;
        var demand = MakeDemand.Find(Demand);
        if (demand == null)
            return CommandValidation.Fail(CommandErrors.InvalidTarget, $"\"{Demand}\" is not a demand");
        return DiplomacyCommandChecks.FindAlert(actor, demand.DemandAlert, CivId) == null
            ? DiplomacyCommandChecks.NoSuchAlert("demand from " + CivId)
            : CommandValidation.Ok;
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        var actor = context.Actor;
        var demand = MakeDemand.Find(Demand).NotNull();
        DiplomacyCommandChecks.RemoveAlert(actor, DiplomacyCommandChecks.FindAlert(actor, demand.DemandAlert, CivId).NotNull());
        var demander = context.ValidatedOther(CivId);
        if (demander.IsDefeated()) return [];
        var diplomacy = actor.GetDiplomacyManager(demander).NotNull();
        if (Accept) diplomacy.AgreeToDemand(demand);
        else
        {
            diplomacy.RefuseDemand(demand);
            if (demand == Logic.Civilization.Diplomacy.Demand.DoNotAttackUs) diplomacy.DeclareWar();
        }
        return [];
    }
}
