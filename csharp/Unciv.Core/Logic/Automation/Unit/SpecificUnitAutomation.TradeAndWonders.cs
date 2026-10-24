using Unciv.Logic.Map.MapUnit.Actions;
using Unciv.Models;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Unique;
using Unciv.Utils;
using CityObject = Unciv.Logic.City.City;
using MapUnitClass = Unciv.Logic.Map.MapUnits.MapUnit;

namespace Unciv.Logic.Automation.Unit;

// Ported from core/src/com/unciv/logic/automation/unit/SpecificUnitAutomation.kt (T091): conductTradeMission, speedupWonderConstruction and its helper
// (a partial of SpecificUnitAutomation.cs).
public static partial class SpecificUnitAutomation
{
    /// <returns>whether there was any progress in conducting the trade mission. A return value of
    /// <c>false</c> can be interpreted as: the unit doesn't know where to go or there are no city states.</returns>
    public static bool ConductTradeMission(MapUnitClass unit)
    {
        var conductedTradeMission = UnitActions.InvokeUnitAction(unit, UnitActionType.ConductTradeMission);
        if (conductedTradeMission) return true;

        var relevantTiles = unit.Civ.GameInfo.Civilizations
            .Where(it =>
                it != unit.Civ
                && !unit.Civ.IsAtWarWith(it)
                && it.IsCityState
                && it.Cities.Count != 0)
            .SelectMany(it => it.Cities[0].GetTiles())
            .Where(it => unit.Civ.HasExplored(it))
            .OrderBy(it => unit.CurrentTile.AerialDistanceTo(it));

        var closestCityStateTile = relevantTiles.FirstOrDefault(tile =>
        {
            if (!unit.Movement.CanPassThrough(tile)) return false;
            var path = unit.Movement.GetShortestPath(tile);
            // 0 is unreachable, 10 is too far away
            return path.Count is >= 1 and <= 10;
        });
        if (closestCityStateTile == null) return false;

        var unitTileBeforeMovement = unit.CurrentTile;
        unit.Movement.HeadTowards(closestCityStateTile);

        return unitTileBeforeMovement != unit.CurrentTile;
    }

    /// <summary>
    /// If there's a city nearby that can construct a wonder, walk there an get it built. Typically I
    /// like to build all wonders in the same city to have the bonuses accumulate (and it typically ends
    /// up being my capital), but that would need too much logic (e.g. how far away is the capital,
    /// is the wonder likely still available by the time I'm there, is this particular wonder even
    /// buildable in the capital, etc.)
    /// </summary>
    /// <returns>whether there was any progress in speeding up a wonder construction. A return value
    /// of <c>false</c> can be interpreted as: the unit doesn't know where to go or is stuck.</returns>
    public static bool SpeedupWonderConstruction(MapUnitClass unit)
    {
        var nearbyCityWithAvailableWonders = unit.Civ.Cities.Where(city =>
                // Don't speed up construction in small cities. There's a risk the great
                // engineer can't get it done entirely and then it takes forever for the small
                // city to finish the rest.
                city.Population.Population >= 3
                && (unit.Movement.CanMoveTo(city.GetCenterTile()) || unit.CurrentTile == city.GetCenterTile())
                && GetWonderThatWouldBenefitFromBeingSpedUp(city) != null)
            .Select(city =>
            {
                var path = unit.Movement.GetShortestPath(city.GetCenterTile());
                return (City: city, Size: path.Count != 0 && path.Count <= 5 ? path.Count : 0); // 0: no path or too far
            })
            .Where(it => it.Size > 0)
            .OrderBy(it => it.Size) // stable: the first minimum, like minByOrNull
            .Select(it => it.City)
            .FirstOrDefault();
        if (nearbyCityWithAvailableWonders == null) return false;

        if (unit.CurrentTile == nearbyCityWithAvailableWonders.GetCenterTile())
        {
            var wonderToHurry = GetWonderThatWouldBenefitFromBeingSpedUp(nearbyCityWithAvailableWonders).NotNull();
            nearbyCityWithAvailableWonders.CityConstructions.EditQueue(queue => queue.Insert(0, wonderToHurry.Name));
            return UnitActions.InvokeUnitAction(unit, UnitActionType.HurryBuilding)
                || UnitActions.InvokeUnitAction(unit, UnitActionType.HurryWonder);
        }

        // Walk towards the city.
        var tileBeforeMoving = unit.GetTile();
        unit.Movement.HeadTowards(nearbyCityWithAvailableWonders.GetCenterTile());
        return tileBeforeMoving != unit.CurrentTile;
    }

    private static Building? GetWonderThatWouldBenefitFromBeingSpedUp(CityObject city) =>
        city.CityConstructions.GetBuildableBuildings()
            .Where(building =>
                building.IsWonder && !((IHasUniques)building).HasUnique(UniqueType.CannotBeHurried)
                && city.CityConstructions.TurnsToConstruction(building.Name) >= 5)
            .OrderBy(building => -city.CityConstructions.GetRemainingWork(building.Name))
            .FirstOrDefault();
}
