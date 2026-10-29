using Unciv.Commands.Diplomacy;
using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Utils;

namespace Unciv.Commands.Units;

/// <summary>
/// A Merchant of Venice (unique <c>Can buy a City-State</c>) standing next to a city of a city-state buys the city-state for gold and is spent;
/// its cities become puppets of the buyer, who then answers the usual puppet-or-annex alert (bnw-spec 4.10). Not in Kotlin.
/// Rules and refusals: <see cref="CityStateBuyout.WhyCannotBeBoughtBy"/>.
/// </summary>
[CommandType("buy-city-state")]
public sealed class BuyCityState : GameCommand
{
    public int UnitId { get; init; }

    public string CivId { get; init; } = "";

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.RequireActive().TryGet(out var actor, out var failure)) return failure;
        if (!context.OwnUnit(actor, UnitId).TryGet(out var unit, out failure)) return failure;
        if (!context.TryOtherCiv(CivId, CivKind.CityState, out _, out var other, out failure)) return failure;
        return CityStateBuyout.WhyCannotBeBoughtBy(other, unit) is { } refusal
            ? CommandValidation.Fail(CommandErrors.NotAllowed, refusal)
            : CommandValidation.Ok;
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        var actor = context.Actor;
        var unit = context.OwnUnit(actor, UnitId).Value.NotNull();
        var other = context.ValidatedOther(CivId);
        var newCities = other.Cities.ToList();
        CityStateBuyout.BuyCityState(other, unit);
        foreach (var city in newCities)
            actor.AddPopupAlert(new PopupAlert(AlertType.DiplomaticMarriage, city.Id));
        return [];
    }
}
