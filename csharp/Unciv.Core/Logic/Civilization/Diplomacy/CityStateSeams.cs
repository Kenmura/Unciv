using Unciv.Logic.Battle;
using CityObject = Unciv.Logic.City.City;

namespace Unciv.Logic.Civilization.Diplomacy;

// T068: the two calls CityStateFunctions makes into classes ported later. Each is the single place that call is made (T094 and T081 implemented them). Remove in the T104 sweep (T104 is the open owner).
internal static class CityStateSeams
{
    /// <summary>Kotlin <c>NextTurnAutomation.getForeignCityNearCapital(capital, receivingCiv)?.city</c>.</summary>
    internal static CityObject? GetForeignCityNearCapital(CityObject? capital, Civilization receivingCiv) =>
        Automation.Civilization.NextTurnAutomation.GetForeignCityNearCapital(capital, receivingCiv)?.City;

    /// <summary>Kotlin <c>CityCombatant(city).getDefendingStrength()</c> (CityCombatant is ported, T081; kept as a method so CityStateFunctions stays within its coupling limit).</summary>
    internal static int GetCityDefendingStrength(CityObject city) => new CityCombatant(city).GetDefendingStrength();
}
