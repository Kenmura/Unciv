using CityObject = Unciv.Logic.City.City;
using CivilizationClass = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Logic.Automation.Civilization;

// T090: the members of Kotlin `NextTurnAutomation` (owner: T094) that the unit and gold automation call. They are small and pure, so they are
// ported faithfully here (and testable); T094 moves them into NextTurnAutomation and deletes this file.
internal static class NextTurnAutomationSeams
{
    /// <summary>Kotlin <c>NextTurnAutomation.CityDistance</c>.</summary>
    internal sealed record CityDistance(CityObject City, int AerialDistance);

    /// <summary>Kotlin <c>NextTurnAutomation.getForeignCityNearCapital(capital, foreignCiv)</c>. TODO(T094)</summary>
    internal static CityDistance? GetForeignCityNearCapital(CityObject? capital, CivilizationClass foreignCiv)
    {
        if (capital == null || foreignCiv.Cities.Count == 0)
            return null;

        CityDistance? minDistance = null;
        foreach (var foreignCity in foreignCiv.Cities)
        {
            var currentDistance = foreignCity.GetCenterTile().AerialDistanceTo(capital.GetCenterTile());
            if (minDistance == null || currentDistance < minDistance.AerialDistance)
                minDistance = new CityDistance(foreignCity, currentDistance);
        }
        return minDistance;
    }
}
