using Unciv.Logic.Civilization;
using Unciv.Logic.Map.MapUnit.Actions;
using Unciv.Logic.Map.Tiles;
using Unciv.Models;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using Unciv.Utils;
using Unciv.View;
using CityClass = Unciv.Logic.City.City;
using CivilizationClass = Unciv.Logic.Civilization.Civilization;
using MapUnitClass = Unciv.Logic.Map.MapUnits.MapUnit;
using NotificationCategory = Unciv.Logic.Civilization.Notification.NotificationCategory;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Logic.Automation.Unit;

// Ported from core/src/com/unciv/logic/automation/unit/WorkerAutomation.kt (T092). Purity annotations, timeThis profiling and debug() logging dropped (T027 policy).
// - Absorbs WorkerAutomationSeams (T158): HasWorkableSeaResource and IsNotBonusResourceOrWorkable are the Kotlin companion members.
// - Kotlin Sequence/Map operations keep their semantics: filter/groupBy/sortedBy are lazy-or-eager but order-preserving and stable (LINQ Where/GroupBy/OrderBy),
//   maxByOrNull is the FIRST maximum (MaxByOrNull below), ruleset.tileImprovements is the insertion-ordered LinkedHashMap.
// - Kotlin HashMap<Tile,...> / Counter<City> are only looked up (never iterated): Dictionary / Counter.
// - `settings.stopAutomatedWorkersRemoveVegetation` / `automatedWorkersReplaceImprovements` are GameClientState properties;
//   `worldScreen?.autoPlay?.isAutoPlayingAndFullAutoPlayAI() == false` is "AutoPlay != null && !IsFullAutoPlayAI".
// - `synchronized(workersPerCity)` dropped: the engine is single-threaded.

/// <summary>
/// Contains the logic for worker automation.
/// This is instantiated from <c>Civilization.GetWorkerAutomation</c> and cached there.
/// </summary>
/// <param name="civInfo">The Civilization - data common to all automated workers is cached once per Civ</param>
/// <param name="cachedForTurn">The turn number this was created for - a recreation of the instance is forced on different turn numbers</param>
/// <param name="cloningSource">The previous instance, whose road automation settings are reused</param>
public partial class WorkerAutomation(CivilizationClass civInfo, int cachedForTurn, WorkerAutomation? cloningSource = null)
{
    public CivilizationClass CivInfo { get; } = civInfo;
    public int CachedForTurn { get; } = cachedForTurn;

    ///////////////////////////////////////// Cached data /////////////////////////////////////////

    public RoadToAutomation RoadToAutomation { get; } = new(civInfo);

    internal RoadBetweenCitiesAutomation RoadBetweenCitiesAutomation { get; } =
        new(civInfo, cachedForTurn, cloningSource?.RoadBetweenCitiesAutomation);

    ///////////////////////////////////////// Helpers /////////////////////////////////////////

    ///////////////////////////////////////// Methods /////////////////////////////////////////

    /// <summary>Automate one Worker - decide what to do and where, move, start or continue work.</summary>
    public void AutomateWorkerAction(MapUnitView unitView, HashSet<TileView> dangerousTiles)
    {
        var unit = unitView.Unwrap();
        var currentTile = unit.GetTile();
        var currentTileIsCreatesOneImprovementMarker = currentTile.IsMarkedForCreatesOneImprovement();
        // Must be called before any getPriority checks to guarantee the local road cache is processed
        var citiesToConnect = RoadBetweenCitiesAutomation.GetNearbyCitiesToConnect(unit);
        // Shortcut, we are working a suitable tile, and we're better off minimizing worker-turns by finishing everything on this tile
        if (!currentTileIsCreatesOneImprovementMarker
            && currentTile.ImprovementInProgress != null && !dangerousTiles.Contains(unitView.GetTile())
            && GetFullPriority(unit.GetTile(), unitView) >= 2)
        {
            return;
        }
        var tileToWork = FindTileToWork(unitView, dangerousTiles);

        if (tileToWork != currentTile && tileToWork != null)
        {
            HeadTowardsTileToWork(unitView, tileToWork);
            return;
        }

        if (!currentTileIsCreatesOneImprovementMarker && currentTile.ImprovementInProgress != null) return; // we're working!

        if (tileToWork == currentTile && TileHasWorkToDo(currentTile, unitView))
        {
            StartWorkOnCurrentTile(unitView);
            return;
        }

        // Support Alpha Frontier-Style Workers that _also_ have the "May create improvements on water resources" unique
        if (unit.Cache.HasUniqueToCreateWaterImprovements && AutomateWorkBoats(unitView)) return;

        if (TryHeadTowardsUndevelopedCity(unitView, currentTile)) return;

        // Nothing to do, try again to connect cities
        if (RoadBetweenCitiesAutomation.TryConnectingCities(unit, citiesToConnect)) return;

        unit.Civ.AddNotification($"{unit.ShortDisplayName()} has no work to do.", new MapUnitAction(unit.GetTile().Position, unit.Id),
            NotificationCategory.Units, unit.Name, "OtherIcons/Sleep");

        // Idle CS units should wander so they don't obstruct players so much
        if (unit.Civ.IsCityState)
            UnitAutomation.Wander(unitView, stayInTerritory: true, tilesToAvoid: dangerousTiles);
    }

    private static CityClass ClosestCity(MapUnitClass unit) =>
        unit.CurrentTile.OwningCity
        ?? unit.Civ.Cities.MinBy(it => unit.GetTile().AerialDistanceTo(it.GetCenterTile())).NotNull();

    private void FillWorkersPerCity()
    {
        if (!workersPerCity.IsEmpty()) return;
        foreach (var unit in CivInfo.Units.GetCivUnits().Where(it => it.Cache.HasUniqueToBuildImprovements))
            workersPerCity.Add(ClosestCity(unit), 1);
    }

    private bool TryHeadTowardsUndevelopedCity(MapUnitView unitView, TileClass currentTile)
    {
        var unit = unitView.Unwrap();
        if (unit.Civ.Cities.Count == 0) return false;

        FillWorkersPerCity();
        // Note, however, that the closest city to a tile isn't necessarily the owning city
        // Only move to cities with fewer workers nearby.
        var closestUndevelopedCity = unit.Civ.Cities
            .OrderBy(it => it.GetCenterTile().AerialDistanceTo(currentTile))
            .FirstOrDefault(it =>
                it != unit.CurrentTile.OwningCity
                && workersPerCity[it] < workersPerCity[ClosestCity(unit)]
                && it.GetTiles().Any(tile => tile.IsLand
                    && !tile.GetUnits().Any(u => u.Cache.HasUniqueToBuildImprovements)
                    && (tile.IsPillaged() || TileHasWorkToDo(tile, unitView)))
                && unit.Movement.CanReach(it.GetCenterTile())); // goto closest undeveloped city

        if (closestUndevelopedCity != null)
        {
            workersPerCity.Add(ClosestCity(unit), -1);
            workersPerCity.Add(closestUndevelopedCity, +1);
            var reachedTile = unit.Movement.HeadTowards(closestUndevelopedCity.GetCenterTile());
            if (reachedTile != currentTile) unit.DoAction(); // since we've moved, maybe we can do something here - automate
            return true;
        }
        return false;
    }

    private void StartWorkOnCurrentTile(MapUnitView unitView)
    {
        var unit = unitView.Unwrap();
        var currentTile = unit.CurrentTile;
        var tileRanking = tileRankings[currentTile];
        if (tileRanking.RepairImprovment == true)
        {
            UnitActionsFromUniques.GetRepairAction(unit)?.Action?.Invoke();
            return;
        }

        if (tileRanking.BestImprovement != null)
        {
            currentTile.StartWorkingOnImprovement(tileRanking.BestImprovement, CivInfo, unit);
            return;
        }
        throw new InvalidOperationException("We didn't find anything to improve on this tile even though there was supposed to be something to improve!");
    }

    private void HeadTowardsTileToWork(MapUnitView unitView, TileClass tileToWork)
    {
        var unit = unitView.Unwrap();
        var currentTile = unit.GetTile();
        var reachedTile = unit.Movement.HeadTowards(tileToWork);

        if (reachedTile.Neighbors.Contains(tileToWork)
            && unit.Movement.CanMoveTo(tileToWork, allowSwap: true)
            && !unit.Movement.CanMoveTo(tileToWork, allowSwap: false)
            && unit.Movement.CanUnitSwapTo(tileToWork))
        {
            // There must be a unit on the target tile! Let's swap with it.
            unit.Movement.SwapMoveToTile(tileToWork);
        }

        if (reachedTile != currentTile) // otherwise, we get a situation where the worker is automated, so it tries to move but doesn't, then tries to automate, then move, etc, forever. Stack overflow exception!
            unit.DoAction();

        // If we have reached a fort tile that is in progress and shouldn't be there, cancel it.
        // Kotlin note: Replace this code entirely and change [chooseImprovement] to not continue building the improvement by default
        if (reachedTile == tileToWork && reachedTile.ImprovementInProgress == Constants.Fort)
            reachedTile.StopWorkingOnImprovement();

        if (!unit.HasMovement() || reachedTile != tileToWork) return;

        // If there's move still left, and this is even a tile we want, perform action
        // Unit may stop due to Enemy Unit within walking range during doAction() call

        // tileRankings is updated in getBasePriority, which is only called if isAutomationWorkableTile is true
        // Meaning, there are tiles we can't/shouldn't work, and they won't even be in tileRankings
        if (TileHasWorkToDo(unit.CurrentTile, unitView))
            StartWorkOnCurrentTile(unitView);
    }

    /// <summary>Looks for a worthwhile tile to improve</summary>
    /// <returns>Null if no tile to work was found</returns>
    private TileClass? FindTileToWork(MapUnitView unitView, IReadOnlySet<TileView> tilesToAvoid)
    {
        var unit = unitView.Unwrap();
        var currentTile = unit.GetTile();

        if (IsAutomationWorkableTile(currentTile, tilesToAvoid, currentTile, unitView)
            && GetBasePriority(currentTile, unitView) >= 3
            && (currentTile.IsPillaged() || currentTile.HasFalloutEquivalent() || TileHasWorkToDo(currentTile, unitView)))
        {
            return currentTile;
        }

        var workableTilesCenterFirst = currentTile.GetTilesInDistance(3)
            .Where(it =>
                (it.GetOwner() == null || it.GetOwner() == unit.Civ || it.GetOwner().NotNull().IsCityState)
                && IsAutomationWorkableTile(it, tilesToAvoid, currentTile, unitView)
                && GetTileSelectionPriority(it, unitView) >= 0);

        var workableTilesPrioritized = workableTilesCenterFirst.GroupBy(it => GetTileSelectionPriority(it, unitView))
            .OrderByDescending(it => it.Key);

        // Search through each group by priority
        // If we can find an eligible best tile in the group lets return that
        // under the assumption that best tile is better than tiles in all lower groups
        foreach (var tilePriorityGroup in workableTilesPrioritized)
        {
            TileClass? bestTile = null;
            foreach (var tileInGroup in tilePriorityGroup.OrderBy(it => unit.GetTile().AerialDistanceTo(it)))
            {
                // These are the expensive calculations (tileCanBeImproved, canReach), so we only apply these filters after everything else it done.
                if (!TileHasWorkToDo(tileInGroup, unitView)) continue;
                if (unit.GetTile() == tileInGroup) return unit.GetTile();
                if (!unit.Movement.CanReach(tileInGroup)) continue;
                if (bestTile == null || GetFullPriority(tileInGroup, unitView) > GetFullPriority(bestTile, unitView))
                    bestTile = tileInGroup;
            }
            if (bestTile != null)
                return bestTile;
        }
        return null;
    }

    /// <summary>
    /// Road tiles may be outside our borders, so their normal tile priority can be lower than nearby
    /// improvements even though <c>GetImprovementRanking</c> gives the planned road a positive value.
    /// Use the road plan priority when ordering tile groups so those strategic tiles get considered.
    /// </summary>
    private float GetTileSelectionPriority(TileClass tile, MapUnitView unitView)
    {
        var unit = unitView.Unwrap();
        var basePriority = GetBasePriority(tile, unitView);
        if (!RoadBetweenCitiesAutomation.TilesOfRoadsMap.TryGetValue(tile, out var roadPlan)) return basePriority;
        var bestRoad = RoadBetweenCitiesAutomation.BestRoadAvailable;
        var roadImprovement = bestRoad.Improvement(ruleSet);
        if (roadImprovement == null) return basePriority;

        if (tile.GetUnpillagedRoad() >= bestRoad || !unit.CanBuildImprovement(roadImprovement, tile))
            return basePriority;

        return Math.Max(basePriority, roadPlan.Priority);
    }

    private bool IsAutomationWorkableTile(TileClass tile, IReadOnlySet<TileView> tilesToAvoid, TileClass currentTile, MapUnitView unitView)
    {
        var unit = unitView.Unwrap();
        if (tilesToAvoid.Contains(unitView.GetTile().GetTileMap().GetTile(tile))) return false;
        if (tile.IsMarkedForCreatesOneImprovement()) return false;
        if (!(tile == currentTile
              || (unit.IsCivilian() && (tile.CivilianUnit == null || !tile.CivilianUnit.Cache.HasUniqueToBuildImprovements))
              || (unit.IsMilitary() && (tile.MilitaryUnit == null || !tile.MilitaryUnit.Cache.HasUniqueToBuildImprovements))))
        {
            return false;
        }
        if (tile.OwningCity != null && tile.GetOwner() != CivInfo) return false;
        if (tile.IsCityCenter()) return false;
        if (RoadBetweenCitiesAutomation.TilesOfRoadsMap.ContainsKey(tile)) return true;
        // Don't try to improve tiles we can't benefit from at all
        if (!CivInfo.CanSeeResource(tile.TileResource)
            && !tile.GetTilesInDistance(CivInfo.GameInfo.Ruleset.ModOptions.Constants.CityWorkRange)
                .Any(it => it.IsCityCenter() && it.GetCity()?.Civ == CivInfo))
        {
            return false;
        }
        if ((tile.TileImprovement as IHasUniques)?.HasUnique(UniqueType.AutomatedUnitsWillNotReplace) == true && !tile.IsPillaged()) return false;
        return true;
    }

    /// <summary>
    /// Try improving a Water Resource. todo: No logic to avoid capture by enemies yet!
    /// </summary>
    /// <returns>Whether any progress was made (improved a tile or at least moved towards an opportunity)</returns>
    public bool AutomateWorkBoats(MapUnitView unitView)
    {
        var unit = unitView.Unwrap();
        var closestReachableResource = unit.Civ.Cities
            .SelectMany(city => city.GetTiles())
            .Where(it =>
                HasWorkableSeaResource(it, unit.Civ)
                && (unit.CurrentTile == it || unit.Movement.CanMoveTo(it)))
            .OrderBy(it => it.AerialDistanceTo(unit.CurrentTile))
            .FirstOrDefault(it => unit.Movement.CanReach(it) && IsNotBonusResourceOrWorkable(it, unit.Civ));
        if (closestReachableResource == null) return false;

        unit.Movement.HeadTowards(closestReachableResource);
        if (unit.CurrentTile != closestReachableResource) return true; // moving counts as progress

        return UnitActions.InvokeUnitAction(unit, UnitActionType.CreateImprovement);
    }

    // Static methods so they can be reused in ConstructionAutomation

    /// <summary>
    /// Checks whether <paramref name="tile"/> is water and has a resource <paramref name="civInfo"/> can improve.
    /// Does check whether a matching improvement can currently be built (e.g. Oil before Refrigeration).
    /// Can return <c>true</c> if there is an improvement that does not match the resource (for future modding abilities).
    /// Does not check tile ownership - caller <see cref="AutomateWorkBoats"/> already did, other callers need to ensure this explicitly.
    /// </summary>
    public static bool HasWorkableSeaResource(TileClass tile, CivilizationClass civInfo)
    {
        var resource = tile.TileResource;
        if (!tile.IsWater) return false;
        if (resource == null || !civInfo.CanSeeResource(resource)) return false; // Kotlin: canSeeResource(null) is false, so resource below is non-null
        if (tile.Improvement != null && resource.IsImprovedBy(tile.Improvement)) return false;
        return resource.GetImprovements().Any(it =>
        {
            var improvement = civInfo.GameInfo.Ruleset.TileImprovements[it];
            return tile.ImprovementFunctions.CanBuildImprovement(improvement, civInfo.State);
        });
    }

    /// <summary>
    /// Test whether improving the resource on <paramref name="tile"/> benefits <paramref name="civInfo"/> (yields or strategic or luxury).
    /// Only tests resource type and city range, not any improvement requirements. Throws on tiles without a resource, like the Kotlin <c>!!</c>.
    /// </summary>
    public static bool IsNotBonusResourceOrWorkable(TileClass tile, CivilizationClass civInfo) =>
        tile.TileResource.NotNull().ResourceType != ResourceType.Bonus // Improve Oil even if no City reaps the yields
        || civInfo.Cities.Any(it => it.TilesInRange.Contains(tile)); // Improve Fish only if any of our Cities reaps the yields
}
