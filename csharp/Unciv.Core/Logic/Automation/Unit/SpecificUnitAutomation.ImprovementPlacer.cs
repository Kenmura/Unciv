using Unciv.Logic.Map.MapUnit.Actions;
using Unciv.Models;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.StatTypes;
using Unciv.View;
using MapUnitClass = Unciv.Logic.Map.MapUnits.MapUnit;

namespace Unciv.Logic.Automation.Unit;

// Ported from core/src/com/unciv/logic/automation/unit/SpecificUnitAutomation.kt (T091): automateImprovementPlacer (a partial of SpecificUnitAutomation.cs).
public static partial class SpecificUnitAutomation
{
    /// <returns>whether there was any progress in placing the improvement. A return value of <c>false</c>
    /// can be interpreted as: the unit doesn't know where to place the improvement or is stuck.</returns>
    public static bool AutomateImprovementPlacer(MapUnitClass unit)
    {
        var improvementBuildingUnique = unit.GetMatchingUniques(UniqueType.ConstructImprovementInstantly).FirstOrDefault();
        if (improvementBuildingUnique == null) return false;

        var improvementName = improvementBuildingUnique.Params[0];
        var improvement = unit.Civ.GameInfo.Ruleset.TileImprovements.GetValueOrDefault(improvementName);
        if (improvement == null) return false;
        // Kotlin maxByOrNull on the Stats entries: the first maximum, Culture when the improvement has no stats
        var relatedStat = improvement.MaxBy(it => it.Value)?.Key ?? Stat.Culture;

        var citiesByStatBoost = unit.Civ.Cities
            .OrderByDescending(it => it.CityStats.StatPercentBonusTree.TotalStats[relatedStat])
            .ToList();

        var civView = new GameView((GameInfo)unit.Civ.GameInfo, unit.Civ).CivView;
        var averageTerrainStatsValue = unit.Civ.GameInfo.Ruleset.Terrains.Values
            .Where(it => it.Type == TerrainType.Land)
            .Select(it => (double)Automation.RankStatsValue(it, civView))
            .DefaultIfEmpty(double.NaN) // Kotlin Sequence<Float>.average() is a Double, NaN when empty
            .Average();

        foreach (var city in citiesByStatBoost)
        {
            var applicableTiles = city.GetWorkableTiles().Where(it =>
                it.IsLand && (it.TileResource?.IsImprovedBy(improvementName) != false) && !it.IsCityCenter()
                && (unit.CurrentTile == it || unit.Movement.CanMoveTo(it))
                // okay if we replace regular improvements by great improvements, but not the other way around
                && (it.TileImprovement == null || !((IHasUniques)it.TileImprovement).HasUnique(UniqueType.GreatImprovement))
                && it.ImprovementFunctions.CanBuildImprovement(improvement, unit.Cache.State)
                && (!((IHasUniques)improvement).HasUnique(UniqueType.GreatImprovement) || Automation.RankStatsValue(it.GetBaseTerrain().CloneStats(), civView) > averageTerrainStatsValue))
                .ToList();

            if (applicableTiles.Count == 0) continue;

            var pathToCity = unit.Movement.GetShortestPath(city.GetCenterTile());

            if (pathToCity.Count == 0) continue;
            if (pathToCity.Count > 2 && unit.GetTile().GetCity() != city)
            {
                // Radius 5 is quite arbitrary. Few units have such a high movement radius although
                // streets might modify it. Also there might be invisible units, so this is just an
                // approximation for relative safety and simplicity.
                var enemyUnitsNearby = unit.GetTile().GetTilesInDistance(5).Any(tileNearby =>
                    tileNearby.GetUnits().Any(unitOnTileNearby =>
                        unitOnTileNearby.IsMilitary() && unitOnTileNearby.Civ.IsAtWarWith(unit.Civ)));
                // Don't move until you're accompanied by a military unit if there are enemies nearby.
                if (unit.GetTile().MilitaryUnit == null && enemyUnitsNearby) return true;
                unit.Movement.HeadTowards(city.GetCenterTile());
                return true;
            }

            // if we got here, we're pretty close, start looking!
            var chosenTile = applicableTiles
                .OrderByDescending(it => Automation.RankTile(it, civView))
                .FirstOrDefault(it => unit.Movement.CanReach(it));
            if (chosenTile == null) continue; // to another city

            var unitTileBeforeMovement = unit.CurrentTile;
            unit.Movement.HeadTowards(chosenTile);
            if (unit.CurrentTile == chosenTile)
            {
                if (unit.CurrentTile.IsPillaged())
                    UnitActions.InvokeUnitAction(unit, UnitActionType.Repair);
                else UnitActions.InvokeUnitAction(unit, UnitActionType.CreateImprovement);
                return true;
            }
            return unitTileBeforeMovement != unit.CurrentTile;
        }
        // No city needs this improvement.
        return false;
    }
}
