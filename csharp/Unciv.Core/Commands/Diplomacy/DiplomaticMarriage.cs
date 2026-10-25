using Unciv.Logic.Civilization;
using Unciv.Models.Ruleset.Unique;

namespace Unciv.Commands.Diplomacy;

/// <summary>
/// Buy a city-state by marrying into its ruling family. Kotlin: <c>CityStateDiplomacyTable.getDiplomaticMarriageButton</c>, offered with the
/// <c>CityStateCanBeBoughtForGold</c> unique and enabled when <c>canBeMarriedBy(viewingCiv)</c>, then <c>diplomaticMarriage</c>; each acquired city
/// then queues a <c>DiplomaticMarriage</c> popup alert for us (annex or keep as puppet).
/// </summary>
[CommandType("diplomatic-marriage")]
public sealed class DiplomaticMarriage : GameCommand
{
    public string CivId { get; init; } = "";

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.TryOtherCiv(CivId, CivKind.CityState, out var actor, out var other, out var failure)) return failure;
        if (!actor.HasUnique(UniqueType.CityStateCanBeBoughtForGold))
            return CommandValidation.Fail(CommandErrors.NotAllowed, "You cannot buy city-states");
        return DiplomacyCommandChecks.CityState(other).CanBeMarriedBy(actor)
            ? CommandValidation.Ok
            : CommandValidation.Fail(CommandErrors.NotAllowed, $"You cannot marry into {CivId} now");
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        var actor = context.Actor;
        var other = context.ValidatedOther(CivId);
        var newCities = other.Cities.ToList();
        DiplomacyCommandChecks.CityState(other).DiplomaticMarriage(actor);
        foreach (var city in newCities)
            actor.AddPopupAlert(new PopupAlert(AlertType.DiplomaticMarriage, city.Id));
        return [];
    }
}
