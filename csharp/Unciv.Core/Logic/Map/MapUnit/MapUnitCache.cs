using Unciv.Logic.Civilization.Transients;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.Logic.Map.MapUnits;

// Ported from core/src/com/unciv/logic/map/mapunit/MapUnitCache.kt (T065).
// Single use in MapUnit and transient there, so it is not serialized.

/// <summary>Cached results of the unit's uniques, so movement and filter code does not re-query them.</summary>
public class MapUnitCache(MapUnit mapUnit)
{
    // These are for performance improvements to getMovementCostBetweenAdjacentTiles,
    // a major component of getDistanceToTilesWithinTurn,
    // which in turn is a component of getShortestPath and canReach
    public bool IgnoresTerrainCost { get; private set; }

    public bool IgnoresZoneOfControl { get; private set; }

    public bool AllTilesCosts1 { get; private set; }

    public bool CanMoveOnWater { get; private set; }

    public bool CanPassThroughImpassableTiles { get; private set; }

    public bool RoughTerrainPenalty { get; private set; }

    /// <summary><c>true</c> if movement 0 _or_ has CannotMove unique</summary>
    public bool CannotMove { get; private set; }

    /// <summary>
    /// If set causes an early exit in getMovementCostBetweenAdjacentTiles
    /// - means no double movement uniques, roughTerrainPenalty or ignoreHillMovementCost
    /// </summary>
    public bool NoTerrainMovementUniques { get; private set; }

    /// <summary>If set causes a second early exit in getMovementCostBetweenAdjacentTiles</summary>
    public bool NoBaseTerrainOrHillDoubleMovementUniques { get; private set; }

    /// <summary>If set skips tile.matchesFilter tests for double movement in getMovementCostBetweenAdjacentTiles</summary>
    public bool NoFilteredDoubleMovementUniques { get; private set; }

    /// <summary>Used for getMovementCostBetweenAdjacentTiles only, based on order of testing</summary>
    public enum DoubleMovementTerrainTarget { Feature, Base, Hill, Filter }

    public class DoubleMovement(DoubleMovementTerrainTarget terrainTarget, UniqueObject unique)
    {
        public DoubleMovementTerrainTarget TerrainTarget { get; } = terrainTarget;
        public UniqueObject Unique { get; } = unique;
    }

    /// <summary>Mod-friendly cache of double-movement terrains</summary>
    // Kotlin HashMap; only looked up by key and scanned with none{}, so insertion order cannot reach game state.
    public Dictionary<string, DoubleMovement> DoubleMovementInTerrain { get; } = [];

    public bool CanEnterIceTiles { get; set; }
    public bool CannotEmbark { get; set; }
    public bool CannotEnterOceanTiles { get; set; }
    public bool CanEnterForeignTerrain { get; set; }
    public bool CanEnterCityStates { get; set; }
    public float? CostToDisembark { get; set; }
    public float? CostToEmbark { get; set; }

    /// <summary>A hashmap where the key represents the tileFilter, and the value is how far away the tile could be</summary>
    public Dictionary<string, int> ParadropDestinationTileFilters { get; } = [];

    public bool HasUniqueToBuildImprovements { get; set; } // not canBuildImprovements to avoid confusion
    public bool HasUniqueToCreateWaterImprovements { get; set; }

    public bool HasStrengthBonusInRadiusUnique { get; set; }
    public bool HasCitadelPlacementUnique { get; set; }

    public GameContext State { get; set; } = GameContext.EmptyState;

    public void UpdateUniques()
    {
        State = GameContext.ForUnit(mapUnit);
        AllTilesCosts1 = mapUnit.HasUnique(UniqueType.AllTilesCost1Move);
        CanPassThroughImpassableTiles = mapUnit.HasUnique(UniqueType.CanPassImpassable);
        IgnoresTerrainCost = mapUnit.HasUnique(UniqueType.IgnoresTerrainCost);
        IgnoresZoneOfControl = mapUnit.HasUnique(UniqueType.IgnoresZOC);
        RoughTerrainPenalty = mapUnit.HasUnique(UniqueType.RoughTerrainPenalty);
        CannotMove = mapUnit.HasUnique(UniqueType.CannotMove) || mapUnit.BaseUnit.Movement == 0;
        CanMoveOnWater = mapUnit.HasUnique(UniqueType.CanMoveOnWater);

        DoubleMovementInTerrain.Clear();
        mapUnit.ForEachMatchingUnique(UniqueType.DoubleMovementOnTerrain, GameContext.IgnoreConditionalsState, true, unique =>
        {
            var param = unique.Params[0];
            mapUnit.Civ.GameInfo.Ruleset.Terrains.TryGetValue(param, out var terrain);
            DoubleMovementInTerrain[param] = new DoubleMovement(
                terrain == null ? DoubleMovementTerrainTarget.Filter
                : terrain.IsHill ? DoubleMovementTerrainTarget.Hill
                : terrain.Type == TerrainType.TerrainFeature ? DoubleMovementTerrainTarget.Feature
                : terrain.Type.IsBaseTerrain() ? DoubleMovementTerrainTarget.Base
                : DoubleMovementTerrainTarget.Filter,
                unique);
        });
        // Init shortcut flags
        NoTerrainMovementUniques = DoubleMovementInTerrain.Count == 0 &&
            !RoughTerrainPenalty && !mapUnit.Civ.Nation.IgnoreHillMovementCost;
        NoBaseTerrainOrHillDoubleMovementUniques = DoubleMovementInTerrain
            .All(it => it.Value.TerrainTarget == DoubleMovementTerrainTarget.Feature);
        NoFilteredDoubleMovementUniques = DoubleMovementInTerrain
            .All(it => it.Value.TerrainTarget != DoubleMovementTerrainTarget.Filter);
        CostToDisembark = MinOfOrNull(mapUnit.GetMatchingUniques(UniqueType.ReducedDisembarkCost, checkCivInfoUniques: true));
        CostToEmbark = MinOfOrNull(mapUnit.GetMatchingUniques(UniqueType.ReducedEmbarkCost, checkCivInfoUniques: true));

        //todo: consider parameterizing [terrainFilter] in some of the following:
        CanEnterIceTiles = mapUnit.HasUnique(UniqueType.CanEnterIceTiles);
        CannotEmbark = mapUnit.HasUnique(UniqueType.CannotEmbark);
        CannotEnterOceanTiles = mapUnit.HasUnique(UniqueType.CannotEnterOcean, State);

        HasUniqueToBuildImprovements = mapUnit.HasUnique(UniqueType.BuildImprovements);
        HasUniqueToCreateWaterImprovements = mapUnit.HasUnique(UniqueType.CreateWaterImprovements);

        CanEnterForeignTerrain = mapUnit.HasUnique(UniqueType.CanEnterForeignTiles)
            || mapUnit.HasUnique(UniqueType.CanEnterForeignTilesButLosesReligiousStrength);

        CanEnterCityStates = mapUnit.HasUnique(UniqueType.CanTradeWithCityStateForGoldAndInfluence);

        HasStrengthBonusInRadiusUnique = mapUnit.HasUnique(UniqueType.StrengthBonusInRadius, GameContext.IgnoreConditionalsState);
        HasCitadelPlacementUnique = mapUnit.GetMatchingUniques(UniqueType.ConstructImprovementInstantly)
            .Select(it => mapUnit.Civ.GameInfo.Ruleset.TileImprovements.TryGetValue(it.Params[0], out var improvement) ? improvement : null)
            .Where(it => it != null)
            .Any(it => ((IHasUniques)it!).HasUnique(UniqueType.OneTimeTakeOverTilesInRadius));
    }

    // Kotlin minOfOrNull { it.params[0].toFloat() } over a Sequence: null when empty. Params are validated numbers.
    private static float? MinOfOrNull(IEnumerable<UniqueObject> uniques)
    {
        float? min = null;
        foreach (var unique in uniques)
        {
            var value = unique.Params[0].ToFloat();
            if (min == null || value < min) min = value;
        }
        return min;
    }

    public static readonly IReadOnlySet<UniqueType> UnitMovementUniques = new HashSet<UniqueType>
    {
        UniqueType.AllTilesCost1Move,
        UniqueType.CanPassImpassable,
        UniqueType.IgnoresTerrainCost,
        UniqueType.IgnoresZOC,
        UniqueType.RoughTerrainPenalty,
        UniqueType.CannotMove,
        UniqueType.CanMoveOnWater,
        UniqueType.DoubleMovementOnTerrain,
        UniqueType.ReducedDisembarkCost,
        UniqueType.ReducedEmbarkCost,
        UniqueType.CanEnterIceTiles,
        UniqueType.CanEnterForeignTiles,
        UniqueType.CanEnterForeignTilesButLosesReligiousStrength,
        // Special - applied in Nation and not here, wshould be moved to mapunitcache as well
        UniqueType.ForestsAndJunglesAreRoads,
        UniqueType.IgnoreHillMovementCost,
        // Movement algorithm avoids damage on route, meaning terrain damage requires caching
        UniqueType.DamagesContainingUnits,
        UniqueType.LandUnitEmbarkation,
        UniqueType.LandUnitsCrossTerrainAfterUnitGained,
        UniqueType.EnemyUnitsSpendExtraMovement,
    };
}
