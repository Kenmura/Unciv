using CityObject = Unciv.Logic.City.City;

namespace Unciv.Logic.Civilization.Diplomacy;

// T068: the two calls CityStateFunctions makes into classes ported later. Each is the single place that call is made; the owning ticket
// replaces the body (owners: T094, T081).
internal static class CityStateSeams
{
    /// <summary>Kotlin <c>NextTurnAutomation.getForeignCityNearCapital(capital, receivingCiv)?.city</c>.</summary>
    // TODO(T094): NextTurnAutomation.getForeignCityNearCapital
    internal static CityObject? GetForeignCityNearCapital(CityObject? capital, Civilization receivingCiv) =>
        throw new NotImplementedException($"NextTurnAutomation.getForeignCityNearCapital({capital}, {receivingCiv}) needs T094");

    /// <summary>Kotlin <c>CityCombatant(city).getDefendingStrength()</c>.</summary>
    // TODO(T081): CityCombatant.getDefendingStrength
    internal static int GetCityDefendingStrength(CityObject city) =>
        throw new NotImplementedException($"CityCombatant({city}).getDefendingStrength() needs T081");
}
