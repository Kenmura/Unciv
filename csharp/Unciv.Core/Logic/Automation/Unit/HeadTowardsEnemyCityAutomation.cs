using Unciv.Logic.Automation.Civilization;
using Unciv.Logic.Battle;
using Unciv.Logic.Map.MapUnits;
using Unciv.Utils;
using Unciv.View;
using CityObject = Unciv.Logic.City.City;
using MapUnitClass = Unciv.Logic.Map.MapUnits.MapUnit;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Logic.Automation.Unit;

// Ported from core/src/com/unciv/logic/automation/unit/HeadTowardsEnemyCityAutomation.kt (T090). Purity annotations and timeThis dropped (T027 policy).
// - Kotlin Sequences stay lazy (IEnumerable); sortedBy/sortedWith are stable (OrderBy/ThenBy).
// - NextTurnAutomation.getForeignCityNearCapital is NextTurnAutomationSeams.GetForeignCityNearCapital (TODO(T094)).
public static class HeadTowardsEnemyCityAutomation
{
    /// <returns>whether the unit has taken this action</returns>
    public static bool TryHeadTowardsEnemyCity(MapUnitView unitView)
    {
        var unit = unitView.Unwrap();
        if (unit.Civ.Cities.Count == 0) return false;

        // only focus on *attacking* 1 enemy at a time otherwise you'll lose on both fronts
        var closestReachableEnemyCity = GetEnemyCitiesByPriority(unitView)
            .FirstOrDefault(it => unit.Movement.CanReach(it.GetCenterTile()));
        if (closestReachableEnemyCity == null) return false; // No enemy city reachable

        return HeadTowardsEnemyCity(
            unitView,
            closestReachableEnemyCity.GetCenterTile(),
            // This should be cached after the `canReach` call above.
            unit.Movement.GetShortestPath(closestReachableEnemyCity.GetCenterTile())
        );
    }

    public static IEnumerable<CityObject> GetEnemyCitiesByPriority(MapUnitView unitView)
    {
        var unit = unitView.Unwrap();
        var enemies = unit.Civ.GetKnownCivs()
            .Where(it => unit.Civ.IsAtWarWith(it) && it.Cities.Count != 0);

        var closestEnemyCity = enemies
            .Select(it => NextTurnAutomationSeams.GetForeignCityNearCapital(unit.Civ.GetCapital(), it))
            .OfType<NextTurnAutomationSeams.CityDistance>()
            .MinBy(it => it.AerialDistance)?.City;
        if (closestEnemyCity == null) return []; // no attackable cities found

        // Our main attack target is the closest city, but we're fine with deviating from that a bit
        var enemyCitiesByPriority = closestEnemyCity.Civ.Cities
            .Select(it => (City: it, Distance: it.GetCenterTile().AerialDistanceTo(closestEnemyCity.GetCenterTile())))
            .Where(it => !(it.Distance > 10)) // anything 10 tiles away from the target is irrelevant
            .OrderBy(it => it.Distance).Select(it => it.City); // sort the list by closeness to target - least is best!

        if (unit.BaseUnit.IsRanged()) // ranged units don't harm capturable cities, waste of a turn
            enemyCitiesByPriority = enemyCitiesByPriority.Where(it => it.Health != 1);

        return enemyCitiesByPriority;
    }

    private const int MaxDistanceFromCityToConsiderForLandingArea = 5;
    private const int MinDistanceFromCityToConsiderForLandingArea = 3;

    /// <returns>whether the unit has taken this action</returns>
    /// <exception cref="NullReferenceException">when <paramref name="closestReachableEnemyCity"/> does not contain a City (Kotlin NullPointerException)</exception>
    public static bool HeadTowardsEnemyCity(MapUnitView unitView, TileClass closestReachableEnemyCity, List<TileClass> shortestPath)
    {
        var unit = unitView.Unwrap();
        var unitDistanceToTiles = unit.Movement.GetDistanceToTiles();

        var unitRange = unit.GetRange();
        var cityBombardRange = ((CityObject)closestReachableEnemyCity.GetCity().NotNull()).GetBombardRange();
        if (unitRange > cityBombardRange) // long-ranged unit, should never be in a bombardable position
            return HeadTowardsEnemyCityLongRange(closestReachableEnemyCity, unitDistanceToTiles, unitRange, unit);

        var nextTileInPath = shortestPath[0];

        // None of the stuff below is relevant if we're still quite far away from the city, so we
        // short-circuit here for performance reasons.
        if (unit.CurrentTile.AerialDistanceTo(closestReachableEnemyCity) > MaxDistanceFromCityToConsiderForLandingArea
            // Even in the worst case of only being able to move 1 tile per turn, we would still
            // not overshoot.
            && shortestPath.Count > MinDistanceFromCityToConsiderForLandingArea)
        {
            unit.Movement.MoveToTile(nextTileInPath);
            return true;
        }

        var ourUnitsAroundEnemyCity = closestReachableEnemyCity.GetTilesInDistance(6)
            .SelectMany(it => it.GetUnits())
            .Where(it => it.IsMilitary() && it.Civ == unit.Civ);

        var city = (CityObject)closestReachableEnemyCity.GetCity().NotNull();

        if (CannotTakeCitySoon(ourUnitsAroundEnemyCity, city))
            return HeadToLandingGrounds(closestReachableEnemyCity, unit);

        unit.Movement.MoveToTile(nextTileInPath); // go for it!

        return true;
    }

    /// <summary>Cannot take within 5 turns</summary>
    private static bool CannotTakeCitySoon(IEnumerable<MapUnitClass> ourUnitsAroundEnemyCity, CityObject city)
    {
        var cityCombatant = new CityCombatant(city);
        var expectedDamagePerTurn = KotlinMath.SumOf(ourUnitsAroundEnemyCity,
            it => BattleDamage.CalculateDamageToDefender(new MapUnitCombatant(it), cityCombatant));

        const int cityHealingPerTurn = 20;
        return expectedDamagePerTurn < city.Health // Cannot take immediately
            && (expectedDamagePerTurn <= cityHealingPerTurn // No lasting damage
                || city.Health / (expectedDamagePerTurn - cityHealingPerTurn) > 5); // Can damage, but will take more than 5 turns
    }

    private static bool HeadToLandingGrounds(TileClass closestReachableEnemyCity, MapUnitClass unit)
    {
        // don't head straight to the city, try to head to landing grounds -
        // this is against tha AI's brilliant plan of having everyone embarked and attacking via sea when unnecessary.
        var tileToHeadTo = closestReachableEnemyCity.GetTilesInDistanceRange(new IntRange(MinDistanceFromCityToConsiderForLandingArea, MaxDistanceFromCityToConsiderForLandingArea))
            .Where(it => it.IsLand && unit.GetDamageFromTerrain(it) <= 0) // Don't head for hurty terrain
            .OrderBy(it => it.AerialDistanceTo(unit.CurrentTile))
            .FirstOrDefault(it => (unit.Movement.CanMoveTo(it) || it == unit.CurrentTile) && unit.Movement.CanReach(it));

        if (tileToHeadTo != null) // no need to worry, keep going as the movement alg. says
            unit.Movement.HeadTowards(tileToHeadTo);
        return true;
    }

    private static bool HeadTowardsEnemyCityLongRange(
        TileClass closestReachableEnemyCity,
        PathsToTilesWithinTurn unitDistanceToTiles,
        int unitRange,
        MapUnitClass unit)
    {
        // If we're already in fire range but have no sight, hold position
        // If there IS sight, automateUnitMoves would order attacking instead of heading to city
        if (closestReachableEnemyCity.GetTilesInDistance(unitRange).Contains(unit.GetTile()))
            return true;

        var tilesInBombardRange = closestReachableEnemyCity.GetTilesInDistance(2).ToHashSet();
        var candidateTiles = unitDistanceToTiles.Where(it =>
            it.Key.AerialDistanceTo(closestReachableEnemyCity) >= unitRange
            && !tilesInBombardRange.Contains(it.Key)
            && unit.GetDamageFromTerrain(it.Key) <= 0 // Don't set up on a mountain
                                                      // Avoid mountains in path because unitDistanceToTiles parameter doesn't exclude them due to getMovementToTilesAtPosition
            && unit.Movement.CanMoveTo(it.Key));

        // Sort by closest distance to target city, then by the least amount of moves needed to get into fire range
        var tileToMoveTo = candidateTiles
            .OrderBy(it => it.Key.AerialDistanceTo(closestReachableEnemyCity))
            .ThenBy(it => it.Value.TotalMovement)
            .Select(it => (KeyValuePair<TileClass, UnitMovement.ParentTileAndTotalMovement>?)it)
            .FirstOrDefault();

        if (tileToMoveTo != null)
        {
            // move into position far away enough so that city bombard or enemy units don't hurt
            unit.Movement.HeadTowards(tileToMoveTo.Value.Key);
            return true;
        }

        // return false if no tile to move to
        return false;
    }
}
