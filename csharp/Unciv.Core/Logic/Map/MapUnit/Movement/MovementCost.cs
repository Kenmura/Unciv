using System.Globalization;
using Unciv.Logic.Map.Tiles;
using Unciv.Models.Ruleset.Unique;
using Unciv.Utils;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Logic.Map.MapUnits;

// Ported from core/src/com/unciv/logic/map/mapunit/movement/MovementCost.kt (T067). Stripped: the purity annotations, the timeThis profiling.
// Tile.GetOwner() is typed by its seam (T053 rule); the owner is always a Civilization, hence the casts.
// All costs are Float and every operation keeps the Kotlin evaluation order.

public static class MovementCost
{
    public static float GetMovementCostBetweenAdjacentTilesEscort(
        MapUnit unit,
        Tile from,
        Tile to,
        bool considerZoneOfControl = true,
        bool includeEscortUnit = true)
    {
        var movementCost = includeEscortUnit && unit.IsEscorting()
            ? Math.Max(GetMovementCostBetweenAdjacentTiles(unit, from, to, considerZoneOfControl),
                GetMovementCostBetweenAdjacentTiles(unit.GetOtherEscortUnit().NotNull(), from, to, considerZoneOfControl))
            : GetMovementCostBetweenAdjacentTiles(unit, from, to, considerZoneOfControl);
        if (movementCost < 0) throw new InvalidOperationException("Got a negative movement cost?!");
        return movementCost;
    }

    // This function is called ALL THE TIME and should be as time-optimal as possible!
    /// <summary>Does not include escort unit.</summary>
    /// <returns>The cost of movement for the unit between two tiles</returns>
    public static float GetMovementCostBetweenAdjacentTiles(
        MapUnit unit,
        Tile from,
        Tile to,
        bool considerZoneOfControl = true)
    {
        var civ = unit.Civ;

        if (unit.Cache.CannotMove) return 100f;

        if (from.IsLand != to.IsLand && unit.BaseUnit.IsLandUnit && !unit.Cache.CanMoveOnWater)
        {
            return from.IsWater && to.IsLand ? unit.Cache.CostToDisembark ?? 100f
                : unit.Cache.CostToEmbark ?? 100f;
        }

        // If the movement is affected by a Zone of Control, all movement points are expended
        if (considerZoneOfControl && IsMovementAffectedByZoneOfControl(unit, from, to))
            return 100f;

        // land units will still spend all movement points to embark even with this unique
        if (unit.Cache.AllTilesCosts1)
            return 1f;

        var toOwner = (CivilizationObject?)to.GetOwner();

        var extraCost = toOwner != null
            && toOwner.HasActiveEnemyMovementPenalty
            && civ.IsAtWarWith(toOwner)
            ? GetEnemyMovementPenalty(toOwner, unit) : 0f;

        if (from.GetUnpillagedRoad() == RoadStatus.Railroad && to.GetUnpillagedRoad() == RoadStatus.Railroad)
            return RoadStatus.Railroad.GetMovement() + extraCost;

        // Each of these two function calls `hasUnique(UniqueType.CityStateTerritoryAlwaysFriendly)`
        // when entering territory of a city state
        var areConnectedByRoad = from.HasConnection(civ) && to.HasConnection(civ);

        // You might think "wait doesn't isAdjacentToRiver() call isConnectedByRiver() anyway, why have those checks?"
        // The answer is that the isAdjacentToRiver values are CACHED per tile, but the isConnectedByRiver are not - this is an efficiency optimization
        var areConnectedByRiver =
            from.IsAdjacentToRiver() && to.IsAdjacentToRiver() && from.IsConnectedByRiver(to);

        if (areConnectedByRoad && (!areConnectedByRiver || civ.Tech.RoadsConnectAcrossRivers))
            return unit.Civ.Tech.MovementSpeedOnRoads + extraCost;

        if (unit.Cache.IgnoresTerrainCost) return 1f + extraCost;
        if (areConnectedByRiver) return 100f; // Rivers take the entire turn to cross

        // Cities reduce terrain cost to 1
        var terrainCost = to.IsCityCenter() ? 1f
            : (float)to.LastTerrain.MovementCost;

        if (unit.Cache.NoTerrainMovementUniques)
            return terrainCost + extraCost;

        var gameContext = new GameContext(unit.Civ, unit: unit, tile: to);

        if (to.TerrainFeatures.Any(it => HasDoubleMovement(unit, it, MapUnitCache.DoubleMovementTerrainTarget.Feature, gameContext)))
            return terrainCost * 0.5f + extraCost;

        if (unit.Cache.RoughTerrainPenalty && to.IsRoughTerrain())
            return 100f; // units that have to spend all movement in rough terrain, have to spend all movement in rough terrain
        // Placement of this 'if' based on testing, see #4232

        if (civ.Nation.IgnoreHillMovementCost && to.IsHill())
            return 1f + extraCost; // usually hills take 2 movements, so here it is 1

        if (unit.Cache.NoBaseTerrainOrHillDoubleMovementUniques)
            return terrainCost + extraCost;

        if (HasDoubleMovement(unit, to.BaseTerrain, MapUnitCache.DoubleMovementTerrainTarget.Base, gameContext))
            return terrainCost * 0.5f + extraCost;
        var hillTerrain = to.GetHillTerrain();
        if (hillTerrain != null && HasDoubleMovement(unit, hillTerrain.Name, MapUnitCache.DoubleMovementTerrainTarget.Hill, gameContext))
            return terrainCost * 0.5f + extraCost;

        if (unit.Cache.NoFilteredDoubleMovementUniques)
            return terrainCost + extraCost;
        if (unit.Cache.DoubleMovementInTerrain.Any(it =>
                HasDoubleMovement(it.Value, MapUnitCache.DoubleMovementTerrainTarget.Filter, gameContext)
                && to.MatchesFilter(it.Key)))
            return terrainCost * 0.5f + extraCost;

        return terrainCost + extraCost; // no road or other movement cost reduction
    }

    private static bool HasDoubleMovement(
        MapUnitCache.DoubleMovement doubleMovement,
        MapUnitCache.DoubleMovementTerrainTarget target,
        GameContext gameContext)
    {
        if (doubleMovement.TerrainTarget != target) return false;
        if (doubleMovement.Unique.Modifiers.Count != 0
            && !doubleMovement.Unique.ConditionalsApply(gameContext)) return false;

        return true;
    }

    private static bool HasDoubleMovement(
        MapUnit unit,
        string terrainName,
        MapUnitCache.DoubleMovementTerrainTarget target,
        GameContext gameContext)
    {
        if (!unit.Cache.DoubleMovementInTerrain.TryGetValue(terrainName, out var doubleMovement)) return false;
        return HasDoubleMovement(doubleMovement, target, gameContext);
    }

    private static float GetEnemyMovementPenalty(CivilizationObject civInfo, MapUnit enemyUnit)
    {
        if (civInfo.EnemyMovementPenaltyUniques != null && civInfo.EnemyMovementPenaltyUniques.Any())
        {
            var sum = 0;
            foreach (var it in civInfo.EnemyMovementPenaltyUniques)
            {
                if (it.Type.NotNull() == UniqueType.EnemyUnitsSpendExtraMovement
                    && enemyUnit.MatchesFilter(it.Params[0]))
                    sum = unchecked(sum + int.Parse(it.Params[1], NumberStyles.Integer, CultureInfo.InvariantCulture));
            }
            return sum;
        }
        return 0f; // should not reach this point
    }

    /// <summary>Returns whether the movement between the adjacent tiles <paramref name="from"/> and <paramref name="to"/> is affected by Zone of Control</summary>
    private static bool IsMovementAffectedByZoneOfControl(MapUnit unit, Tile from, Tile to)
    {
        // Sources:
        // - https://civilization.fandom.com/wiki/Zone_of_control_(Civ5)
        // - https://forums.civfanatics.com/resources/understanding-the-zone-of-control-vanilla.25582/
        //
        // Enemy military units exert a Zone of Control over the tiles surrounding them. Moving from
        // one tile in the ZoC of an enemy unit to another tile in the same unit's ZoC expends all
        // movement points. Land units only exert a ZoC against land units. Sea units exert a ZoC
        // against both land and sea units. Cities exert a ZoC as well, and it also affects both
        // land and sea units. Embarked land units do not exert a ZoC. Finally, units that can move
        // after attacking are not affected by zone of control if the movement is caused by killing
        // a unit. This last case is handled in the movement-after-attacking code instead of here.

        // We only need to check the two shared neighbors of [from] and [to]: the way of getting
        // these two tiles can perhaps be optimized. Using a hex-math-based "commonAdjacentTiles"
        // function is surprisingly less efficient than the current neighbor-intersection approach.
        // See #4085 for more details.
        if (!AnyTilesExertingZoneOfControl(unit, from, to))
            return false;

        // Even though this is a very fast check, we perform it last. This is because very few units
        // ignore zone of control, so the previous check has a much higher chance of yielding an
        // early "false". If this function is going to return "true", the order doesn't matter
        // anyway.
        if (unit.Cache.IgnoresZoneOfControl)
            return false;
        return true;
    }

    private static bool AnyTilesExertingZoneOfControl(MapUnit unit, Tile from, Tile to)
    {
        foreach (var neighbor in from.Neighbors)
        {
            if (neighbor.IsCityCenter())
            {
                if (neighbor.AerialDistanceTo(to) == 1
                    && unit.Civ.IsAtWarWith((CivilizationObject)neighbor.GetOwner().NotNull()))
                    return true;
            }
            else if (neighbor.MilitaryUnit != null)
            {
                if (neighbor.AerialDistanceTo(to) == 1
                    && (neighbor.MilitaryUnit.Type.IsWaterUnit() || (unit.Type.IsLandUnit() && !neighbor.MilitaryUnit.IsEmbarked()))
                    && unit.Civ.IsAtWarWith(neighbor.MilitaryUnit.Civ))
                    return true;
            }
        }
        return false;
    }
}
