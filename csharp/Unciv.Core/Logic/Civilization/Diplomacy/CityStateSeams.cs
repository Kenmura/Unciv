using Unciv.Logic.Battle;
using CityObject = Unciv.Logic.City.City;

namespace Unciv.Logic.Civilization.Diplomacy;

// T068: the two calls CityStateFunctions makes into classes ported later. Each is the single place that call is made; the owning ticket
// replaces the body (owner: T094; T081 implemented the other one).
internal static class CityStateSeams
{
    /// <summary>Kotlin <c>NextTurnAutomation.getForeignCityNearCapital(capital, receivingCiv)?.city</c>.</summary>
    // TODO(T094): NextTurnAutomation.getForeignCityNearCapital
    internal static CityObject? GetForeignCityNearCapital(CityObject? capital, Civilization receivingCiv) =>
        throw new NotImplementedException($"NextTurnAutomation.getForeignCityNearCapital({capital}, {receivingCiv}) needs T094");

    /// <summary>Kotlin <c>CityCombatant(city).getDefendingStrength()</c> (CityCombatant is ported, T081; kept as a method so CityStateFunctions stays within its coupling limit).</summary>
    internal static int GetCityDefendingStrength(CityObject city) => new CityCombatant(city).GetDefendingStrength();
}
