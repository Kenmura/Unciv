using Unciv.Logic.Map.MapUnit.Actions;
using Unciv.Logic.Map.Tiles;
using Unciv.Models;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.StatTypes;
using Unciv.Utils;
using Unciv.View;
using CityClass = Unciv.Logic.City.City;
using CivilizationClass = Unciv.Logic.Civilization.Civilization;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Logic.Automation.Unit;

// Ported from core/src/com/unciv/logic/automation/unit/WorkerAutomation.kt (T092), part 2: the tile and improvement priorities (split for the coupling ratchet).
public partial class WorkerAutomation
{
    private readonly Ruleset ruleSet = civInfo.GameInfo.Ruleset;

    /// <summary>
    /// Each object has two stages, this first one is checking the basic priority without any improvements.
    /// If tilePriority is -1 then it must be a dangerous tile.
    /// The improvementPriority and bestImprovement are by default not set.
    /// Once improvementPriority is set we have already checked for the best improvement, repairImprovement.
    /// </summary>
    private sealed class TileImprovementRank(float tilePriority)
    {
        public float TilePriority { get; } = tilePriority;
        public float? ImprovementPriority { get; set; }
        public TileImprovement? BestImprovement { get; set; }
        public bool? RepairImprovment { get; set; }
    }

    private readonly Dictionary<TileClass, TileImprovementRank> tileRankings = [];

    private readonly Counter<CityClass> workersPerCity = new();

    private static readonly IEnumerable<TileImprovement> NoIgnoredImprovements = [];

    /// <summary>Kotlin <c>Float.compareTo(other) &lt; 0</c> (total order: -0.0 &lt; 0.0, NaN above everything).</summary>
    private static bool KotlinFloatLess(float a, float b)
    {
        if (float.IsNaN(a)) return false;
        if (float.IsNaN(b)) return true;
        if (a < b) return true;
        if (a > b) return false;
        return a == 0f && float.IsNegative(a) && !float.IsNegative(b);
    }

    /// <summary>Kotlin <c>maxByOrNull</c>: the first element with the maximum selector value; null when empty.</summary>
    private static T? MaxByOrNull<T>(IEnumerable<T> source, Func<T, float> selector) where T : class
    {
        T? best = null;
        var bestValue = 0f;
        foreach (var item in source)
        {
            var value = selector(item);
            if (best == null || KotlinFloatLess(bestValue, value))
            {
                best = item;
                bestValue = value;
            }
        }
        return best;
    }

    private GameView GameViewOf(CivilizationClass civ) => new((GameInfo)civ.GameInfo, civ);

    /// <summary>
    /// Calculate a priority for the tile without accounting for the improvement it'self.
    /// This is a cheap guess on how helpful it might be to do work on this tile
    /// </summary>
    public float GetBasePriority(TileClass tile, MapUnitView unitView)
    {
        var unit = unitView.Unwrap();
        var unitSpecificPriority = 2 - Math.Clamp(tile.AerialDistanceTo(unit.GetTile()) / 2.0f, 0f, 2f) * 3;
        if (tileRankings.TryGetValue(tile, out var cachedRank))
            return cachedRank.TilePriority + unitSpecificPriority;

        var priority = 0f;
        if (tile.GetOwner() == CivInfo)
        {
            priority += 1f; // the fact it's inside our borders means we can do something
            if (tile.ProvidesYield()) priority += 2;
            if (tile.HasFalloutEquivalent()) priority += 1;
            if (tile.TerrainFeatures.Count != 0)
            {
                if (((IHasUniques)tile.LastTerrain).HasUnique(UniqueType.ProductionBonusWhenRemoved)) priority += 0.5f;
                else if (((IHasUniques)tile.LastTerrain).HasUnique(UniqueType.Vegetation)) priority -= 0.3f;
            } // Removing our forests is good for tempo, jungles not as much
            if (tile.TerrainHasUnique(UniqueType.FreshWater)) priority += 1;
            // we want our farms up when unlocking Civil Service
        }

        if (CivInfo.CanSeeResource(tile.TileResource))
        {
            priority += 1;
            if (tile.TileResource?.ResourceType == ResourceType.Luxury) priority += 5;
            //luxuries are more important than other types of resources
        }

        tileRankings[tile] = new TileImprovementRank(priority);
        return priority + unitSpecificPriority;
    }

    /// <summary>Calculates the priority building the improvement on the tile</summary>
    private float GetImprovementPriority(TileClass tile, MapUnitView unitView)
    {
        var unit = unitView.Unwrap();
        GetBasePriority(tile, unitView);
        var rank = tileRankings[tile];
        if (rank.ImprovementPriority == null)
        {
            // All values of rank have to be initialized
            rank.ImprovementPriority = -100f;
            rank.BestImprovement = null;
            rank.RepairImprovment = false;

            var bestImprovement = ChooseImprovement(unitView, tile);
            if (bestImprovement != null)
            {
                rank.BestImprovement = bestImprovement;
                // Increased priority if the improvement has been worked on longer
                var timeSpentPriority = tile.ImprovementInProgress == bestImprovement.Name
                    ? bestImprovement.GetTurnsToBuild(unit.Civ, unit) - tile.TurnsToImprovement
                    : 0;

                rank.ImprovementPriority = GetImprovementRanking(tile, unitView, rank.BestImprovement) + timeSpentPriority;
            }

            if (tile.TileImprovement != null && tile.IsPillaged() && tile.OwningCity != null)
            {
                // Value repairing higher when it is quicker and is in progress
                var repairBonusPriority = tile.GetImprovementToRepair().NotNull().GetTurnsToBuild(unit.Civ, unit) - UnitActionsFromUniques.GetRepairTurns(unit);
                if (tile.ImprovementInProgress == Constants.Repair) repairBonusPriority += UnitActionsFromUniques.GetRepairTurns(unit) - tile.TurnsToImprovement;

                var repairPriority = repairBonusPriority + Automation.RankStatsValue(
                    tile.Stats.GetStatDiffForImprovement(tile.TileImprovement, unit.Civ, tile.OwningCity), GameViewOf(unit.Civ).CivView);
                if (repairPriority > rank.ImprovementPriority)
                {
                    rank.ImprovementPriority = repairPriority;
                    rank.BestImprovement = null;
                    rank.RepairImprovment = true;
                }
            }
        }
        // A better tile than this unit can build might have been stored in the cache
        if (rank.RepairImprovment != true && (rank.BestImprovement == null ||
                !unit.CanBuildImprovement(rank.BestImprovement, tile))) return -100f;
        return rank.ImprovementPriority.Value;
    }

    /// <summary>Calculates the full priority of the tile</summary>
    private float GetFullPriority(TileClass tile, MapUnitView unitView) =>
        GetBasePriority(tile, unitView) + GetImprovementPriority(tile, unitView);

    private bool TileHasWorkToDo(TileClass tile, MapUnitView unitView)
    {
        if (GetImprovementPriority(tile, unitView) <= 0) return false;
        if (!(tileRankings[tile].BestImprovement != null || tileRankings[tile].RepairImprovment == true))
            throw new InvalidOperationException("There was an improvementPriority > 0 and nothing to do");
        return true;
    }

    /// <summary>Determine the improvement appropriate to a given tile and worker. Returns null if none is worth it</summary>
    private TileImprovement? ChooseImprovement(MapUnitView unitView, TileClass tile, IEnumerable<TileImprovement>? ignoreImprovements = null)
    {
        ignoreImprovements ??= NoIgnoredImprovements;
        var unit = unitView.Unwrap();
        if (tile.IsMarkedForCreatesOneImprovement()) return null;
        // You can keep working on half-built improvements, even if they're unique to another civ
        if (tile.ImprovementInProgress != null) return ruleSet.TileImprovements.GetValueOrDefault(tile.ImprovementInProgress);

        var clientState = CivInfo.GameInfo.ClientState;
        var gameContext = new GameContext(civInfo: unit.Civ, unit: unit, tile: tile);
        var currentImprovement = tile.TileImprovement;
        // A LinkedHashMap filter: insertion order is kept
        var potentialTileImprovements = new LinkedHashMap<string, TileImprovement>();
        foreach (var (key, value) in ruleSet.TileImprovements)
        {
            if (!ignoreImprovements.Contains(value)
                && (value.UniqueTo == null || unit.Civ.MatchesFilter(value.UniqueTo, gameContext))
                && unit.CanBuildImprovement(value, tile)
                && tile.ImprovementFunctions.CanBuildImprovement(value, gameContext)
                // Properly exclude removing forest and jungle tiles from potentialTileImprovements.
                && !(clientState.StopAutomatedWorkersRemoveVegetation
                     && tile.TerrainHasUnique(UniqueType.Vegetation)
                     && value.Name.StartsWith(Constants.Remove, StringComparison.Ordinal)
                     && CivInfo.IsHuman())) // Make sure to only apply this to player automated workers.
            {
                potentialTileImprovements[key] = value;
            }
        }
        if (potentialTileImprovements.Count == 0) return null;
        var ignoreWithPotential = ignoreImprovements.Concat(potentialTileImprovements.Values).ToList();

        var currentTileStats = tile.Stats.GetTileStats(tile.GetCity(), CivInfo);
        var bestBuildableImprovement = MaxByOrNull(
            potentialTileImprovements.Values
                .Select(it => new RankedImprovement(it, GetImprovementRanking(tile, unitView, it, currentTileStats, ignoreWithPotential)))
                .Where(it => it.Rank > 0f),
            it => it.Rank)?.Improvement;

        if (currentImprovement != null && CivInfo.IsHuman() && !clientState.AutomatedWorkersReplaceImprovements
            && clientState.AutoPlay != null && !clientState.IsFullAutoPlayAI)
        {
            // Note that we might still want to build roads or remove fallout, so we can't exit the function immedietly
            bestBuildableImprovement = null;
        }

        var lastTerrain = tile.LastTerrain;

        bool IsRemovable(Terrain terrain) => potentialTileImprovements.ContainsKey(Constants.Remove + terrain.Name);

        var resource = tile.TileResource;
        TileImprovement? improvementForResource;
        if (!CivInfo.CanSeeResource(resource))
        {
            improvementForResource = null;
        }
        else if (tile.TerrainFeatures.Count != 0
                 && lastTerrain.Unbuildable
                 && IsRemovable(lastTerrain)
                 && !tile.ProvidesResources(CivInfo)
                 && !IsResourceImprovementAllowedOnFeature(tile, potentialTileImprovements))
        {
            improvementForResource = potentialTileImprovements.GetValueOrDefault(Constants.Remove + lastTerrain.Name);
        }
        else
        {
            improvementForResource = MaxByOrNull(
                resource.NotNull().GetImprovements()
                    .Select(it => potentialTileImprovements.GetValueOrDefault(it))
                    .OfType<TileImprovement>()
                    .Where(it => potentialTileImprovements.ContainsKey(it.Name) || it == currentImprovement),
                it => GetImprovementRanking(tile, unitView, it, null, ignoreWithPotential));
        }

        // After gathering all the data, we conduct the hierarchy in one place
        if (bestBuildableImprovement != null && bestBuildableImprovement.IsRoad())
            return bestBuildableImprovement;

        // For bonus resources we just want the highest-yield improvement, not necessarily the resource-yielding improvement
        if (improvementForResource != null && resource.NotNull().ResourceType != ResourceType.Bonus)
            return improvementForResource == currentImprovement ? null : improvementForResource;

        // If this is a resource that HAS an improvement that we can see, but this unit can't build it, don't waste your time
        if (resource != null && CivInfo.CanSeeResource(resource)
            && resource.ResourceType != ResourceType.Bonus
            && resource.GetImprovements().Count != 0)
        {
            return null;
        }

        if (bestBuildableImprovement == null) return null;

        if (tile.Improvement != null
            && GetImprovementRanking(tile, unitView, tile.TileImprovement.NotNull(), null, ignoreWithPotential)
            > GetImprovementRanking(tile, unitView, bestBuildableImprovement, null, ignoreWithPotential))
        {
            return null; // What we have is better, even if it's pillaged we should repair it
        }

        if (IsRemovable(lastTerrain)
            && (Automation.RankStatsValue(lastTerrain, GameViewOf(CivInfo).CivView) < 0 || ((IHasUniques)lastTerrain).HasUnique(UniqueType.NullifyYields)))
        {
            return potentialTileImprovements.GetValueOrDefault(Constants.Remove + lastTerrain.Name);
        }

        return bestBuildableImprovement;
    }

    private sealed record RankedImprovement(TileImprovement Improvement, float Rank);

    private float GetImprovementRanking(
        TileClass tile,
        MapUnitView unitView,
        TileImprovement improvement,
        Stats? currentTileStats = null,
        IEnumerable<TileImprovement>? ignoreImprovements = null)
    {
        var unit = unitView.Unwrap();
        ignoreImprovements ??= NoIgnoredImprovements;

        // Add the value of roads if we want to build it here
        if (improvement.IsRoad() && RoadBetweenCitiesAutomation.BestRoadAvailable.Improvement(ruleSet) == improvement
            && RoadBetweenCitiesAutomation.TilesOfRoadsMap.TryGetValue(tile, out var roadPlan))
        {
            // We still want good farm/mine building to be able to outrank a low-value road (e.g. an
            // upgrade-only Road -> Railroad on an already-connected route), but connecting a genuinely
            // disconnected city (see #15417) shouldn't need a near-maximum roadPlan.priority (previously -9)
            // to ever get built - that left roads languishing for dozens of turns behind minor tile upgrades.
            return roadPlan.Priority - WorkerAutomationConst.ConnectRoadPriorityOffset;
        }

        // If this tile is not in our territory or neighboring it, it has no value
        if (tile.GetOwner() != unit.Civ &&
            // Check if it is not an unowned neighboring tile that can be in city range
            (!((IHasUniques)improvement).HasUnique(UniqueType.CanBuildOutsideBorders) ||
             !tile.Neighbors.Any(it =>
                 it.GetOwner() == unit.Civ && it.OwningCity != null
                 && tile.AerialDistanceTo(it.OwningCity.GetCenterTile()) <= CivInfo.ModConstants.CityWorkRange)))
        {
            return 0f;
        }

        var stats = tile.Stats.GetStatDiffForImprovement(improvement, CivInfo, tile.GetCity(), currentTileStats);

        ((IHasUniques)improvement).ForEachMatchingUnique(UniqueType.ImprovementStatsForAdjacencies, GameContext.EmptyState, unique =>
        {
            if (unique.Params[1] == improvement.Name) // for Moai-like improvements, add the yields of future self-adjacencies
            {
                stats.Add(unique.Stats * tile.Neighbors.Count(it =>
                    it.TileImprovement != improvement && it.ImprovementFunctions.CanBuildImprovement(improvement, CivInfo.State)));
            }
        });

        var tileResource = tile.TileResource;
        var isResourceImprovedByNewImprovement = CivInfo.CanSeeResource(tileResource) && tileResource.NotNull().IsImprovedBy(improvement.Name);

        if (improvement.Name.StartsWith(Constants.Remove, StringComparison.Ordinal))
        {
            // We need to look beyond what we are doing right now and at the final improvement that will be on this tile
            var removalObject = improvement.Name.Replace(Constants.Remove, "", StringComparison.Ordinal);
            var removedFeature = tile.TerrainFeatures.FirstOrDefault(it => it == removalObject);
            var removalImprovement = removalObject == tile.Improvement ? removalObject : null;

            if (removedFeature != null || removalImprovement != null)
            {
                var newTile = tile.Clone(addUnits: false);
                newTile.SetTerrainTransients();
                if (removedFeature != null)
                    newTile.RemoveTerrainFeature(removedFeature);
                if (removalImprovement != null)
                    newTile.RemoveImprovement();
                var wantedFinalImprovement = ChooseImprovement(unitView, newTile, ignoreImprovements);
                if (wantedFinalImprovement != null)
                {
                    var statDiff = newTile.Stats.GetStatDiffForImprovement(wantedFinalImprovement, CivInfo, newTile.GetCity());
                    stats.Add(statDiff);
                    // Take into account that the resource might be improved by the *final* improvement
                    isResourceImprovedByNewImprovement = newTile.TileResource?.IsImprovedBy(wantedFinalImprovement.Name) == true;
                    if (tile.TerrainFeatures.Count != 0 && ((IHasUniques)tile.LastTerrain).HasUnique(UniqueType.ProductionBonusWhenRemoved))
                        stats.Add(Stat.Production, 0.5f); // We're gaining tempo by chopping the forest, adding an imaginary yield per turn is a way to correct for this
                }
            }
        }

        // If the tile is a neighboring tile it has a lower value
        // Kotlin quirk kept: `stats.div(3f)` is the non-mutating operator, so its result is discarded and this lower value of neighbouring tiles never applies.

        var value = Automation.RankStatsValue(stats, GameViewOf(unit.Civ).CivView);
        // Calculate the bonus from gaining the resources, this isn't included in the stats above
        var resource = tile.TileResource;
        var currentImprovement = tile.TileImprovement;
        if (resource != null)
        {
            // A better resource ranking system might be required, we don't want the improvement
            // ranking for resources to be too high
            if (resource.ResourceType != ResourceType.Bonus)
            {
                // Always build the resource-specific improvement for luxuries and strategics,
                // as it's nontrivial to tell when we won't need them
                if (currentImprovement != null && resource.IsImprovedBy(currentImprovement))
                    value -= 2f;
                if (isResourceImprovedByNewImprovement)
                    value += 2f;
            }
            else
            {
                // For bonus resources, we want to build just enough resource-specific improvements
                // to unlock stone works and stables, otherwise farms are better
                if (currentImprovement != null && tile.TileResource.NotNull().IsImprovedBy(currentImprovement))
                    value -= 0.3f; // enough to offset the 0.2f food vs production value difference
                if (isResourceImprovedByNewImprovement && !tile.GetTilesInDistance(4).Any(it => it.TileImprovement == improvement))
                    value += 0.3f;
            }
        }
        if (currentImprovement != null && IsImprovementProbablyAFort(currentImprovement))
        {
            // Replace/build improvements on other tiles before this one
            // the old fort building logic was found to be detrimental (PR #14309), but let's keep the forts we got around for a bit
            value /= 2;
        }
        return value;
    }

    /// <summary>
    /// Checks whether the improvement matching the tile resource requires any terrain feature to be removed first.
    /// Assumes the caller ensured that terrainFeature and resource are both present!
    /// </summary>
    private static bool IsResourceImprovementAllowedOnFeature(TileClass tile, LinkedHashMap<string, TileImprovement> potentialTileImprovements) =>
        tile.TileResource.NotNull().GetImprovements().Any(resourceImprovementName =>
        {
            if (!potentialTileImprovements.ContainsKey(resourceImprovementName)) return false;
            var resourceImprovement = potentialTileImprovements[resourceImprovementName];
            return tile.TerrainFeatureObjects.Any(resourceImprovement.IsAllowedOnFeature);
        });

    public static bool IsImprovementProbablyAFort(TileImprovement improvement) => ((IHasUniques)improvement).HasUnique(UniqueType.DefensiveBonus);
}
