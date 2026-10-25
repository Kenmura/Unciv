using Unciv.Logic.Map;
using Unciv.Logic.Map.Tiles;
using Unciv.Utils;
using CityClass = Unciv.Logic.City.City;
using CivilizationClass = Unciv.Logic.Civilization.Civilization;
using MapUnitClass = Unciv.Logic.Map.MapUnits.MapUnit;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Logic.Automation.Unit;

// Ported from core/src/com/unciv/logic/automation/unit/RoadBetweenCitiesAutomation.kt (T092). Purity annotations and the debug logging dropped (T027 policy).
// - Kotlin HashMaps keyed by Tile/City (identity) are only ever looked up, never iterated: Dictionary.
// - Kotlin MutableList.sortBy is stable: re-sorted with OrderBy; minByOrNull/minOf are first-extremum / plain minimum.
// - `settings.useAStarPathfinding` is "GameEngineOptions.RoadPathing is set" (A* is PathingMap, see AStarPathing); `settings.autoBuildingRoads` is PlayerSettings.AutoBuildingRoads (T183).

/// <summary>What <c>RoadBetweenCitiesAutomation</c> calls on <c>City</c> when <c>settings.useAStarPathfinding</c> is on.</summary>
public interface ICityRoadPathing
{
    /// <summary><c>from.getRoadPath(destination)</c></summary>
    List<TileClass>? GetRoadPath(CityClass from, CityClass destination);

    /// <summary><c>city.getRoadPathToAny(destinations, maxBfsReachPadding)</c></summary>
    List<TileClass>? GetRoadPathToAny(CityClass city, IReadOnlySet<TileClass> destinations, int maxBfsReachPadding);
}

internal static class WorkerAutomationConst
{
    /// <summary>BFS max size is determined by the aerial distance of two cities to connect, padded with this.</summary>
    internal const int MaxBfsReachPadding = 2;

    /// <summary>
    /// Subtracted from a <see cref="RoadBetweenCitiesAutomation.RoadPlan.Priority"/> to decide whether an automated
    /// worker will treat building that road as *the* best thing to do on a tile (see WorkerAutomation.GetImprovementRanking).
    /// </summary>
    internal const float ConnectRoadPriorityOffset = 4f;
}

/// <summary>Responsible for the "connect cities" automation as part of worker automation.</summary>
public class RoadBetweenCitiesAutomation
{
    private readonly CivilizationClass civInfo;

    /// <summary>Caches BFS by city locations (cities needing connecting), whether successful or not.</summary>
    private readonly Dictionary<HexCoord, BFS> bfsCache = [];

    /// <summary>Caches road type to build for connecting cities, unless option is off or ruleset removed all roads.</summary>
    internal RoadStatus BestRoadAvailable { get; }

    /// <summary>Civ-wide city center tile list of _connected_ Cities, unsorted.</summary>
    private readonly KotlinLazy<List<TileClass>> tilesOfConnectedCities;

    /// <summary>Cache of cities and their road plans to connect to the surrounding cities each turn. Call <see cref="GetRoadsToBuildFromCity"/> instead of using this.</summary>
    private readonly Dictionary<CityClass, List<RoadPlan>> roadsToBuildByCitiesCache = [];

    /// <summary>All cached tiles in each <see cref="RoadPlan"/> list in <see cref="roadsToBuildByCitiesCache"/>.</summary>
    internal Dictionary<TileClass, RoadPlan> TilesOfRoadsMap { get; } = [];

    /// <param name="civInfo">Civilization which does road automation</param>
    /// <param name="cachedForTurn">The turn number the instance was created for, road plans are recalculated each turn</param>
    /// <param name="cloningSource">Kotlin passes the previous instance so bestRoadAvailable need not be recomputed</param>
    public RoadBetweenCitiesAutomation(CivilizationClass civInfo, int cachedForTurn, RoadBetweenCitiesAutomation? cloningSource = null)
    {
        _ = cachedForTurn; // only used by the dropped debug logging
        this.civInfo = civInfo;
        var clientState = civInfo.GameInfo.ClientState;
        var settings = civInfo.GameInfo.GetPlayerSettings(civInfo.CivId);
        // Player can choose not to auto-build roads & railroads. (`autoPlay?.isAutoPlayingAndFullAutoPlayAI() == false`: no autoPlay is not false)
        BestRoadAvailable = cloningSource?.BestRoadAvailable
            ?? (civInfo.IsHuman() && !settings.AutoBuildingRoads && settings.HumanConquestPrompt && !clientState.IsFullAutoPlayAI
                ? RoadStatus.None
                : civInfo.Tech.GetBestRoadAvailable());
        tilesOfConnectedCities = new KotlinLazy<List<TileClass>>(() => civInfo.Cities
            .Where(it => it.IsCapital() || it.CityStats.IsConnectedToCapital(BestRoadAvailable))
            .Select(it => it.GetCenterTile())
            .ToList());
    }

    /// <summary>
    /// Road plan (path) between two cities taking into account best available existing roads and population counts.
    /// Kotlin's <c>inner class</c>: <c>numberOfRoadsToBuild</c> reads the enclosing <c>bestRoadAvailable</c>.
    /// </summary>
    public sealed class RoadPlan(List<TileClass> tiles, float priority, CityClass fromCity, CityClass toCity, RoadStatus bestRoadAvailable)
    {
        /// <summary>Tiles included in the road plan, including city centers</summary>
        public List<TileClass> Tiles { get; } = tiles;

        /// <summary>Road plan priority, used to prioritize tiles for road construction in <see cref="TryConnectingCities"/></summary>
        public float Priority { get; } = priority;

        /// <summary>Starting city</summary>
        public CityClass FromCity { get; } = fromCity;

        /// <summary>Destination city</summary>
        public CityClass ToCity { get; } = toCity;

        // Kotlin note: This number is not consistent across turns because pathing depends on worker position
        private int? numberOfRoadsToBuild;

        public int NumberOfRoadsToBuild => numberOfRoadsToBuild ??= Tiles.Count(it => it.GetUnpillagedRoad() != bestRoadAvailable);
    }

    private static int IndexOfCity(IReadOnlyList<CityClass> cities, CityClass city)
    {
        for (var i = 0; i < cities.Count; i++)
            if (cities[i].Equals(city)) return i;
        return -1;
    }

    /// <returns>Rank 0, 1 or 2 of how important it is to build best available road to capital</returns>
    private float RankRoadCapitalPriority(RoadStatus roadStatus) => roadStatus switch
    {
        RoadStatus.None => BestRoadAvailable != RoadStatus.None ? 2f : 0f,
        RoadStatus.Road => BestRoadAvailable != RoadStatus.Road ? 1f : 0f,
        _ => 0f,
    };

    /// <summary>
    /// Tries to return a list of road plans to connect <paramref name="city"/> to the surrounding cities.
    /// If there are no surrounding cities to connect to and the city is still unconnected to the capital it will try and build a special road to the capital.
    /// </summary>
    /// <returns>Every <see cref="RoadPlan"/> that we want to try and connect associated with <paramref name="city"/>.</returns>
    private List<RoadPlan> GetRoadsToBuildFromCity(CityClass city)
    {
        if (roadsToBuildByCitiesCache.TryGetValue(city, out var cached))
            return cached;

        var roadToCapitalStatus = city.CityStats.GetRoadTypeOfConnectionToCapital();
        var roadPathing = civInfo.GameInfo.EngineOptions.RoadPathing;

        var basePriority = RankRoadCapitalPriority(roadToCapitalStatus);
        var roadPlans = new List<RoadPlan>();
        foreach (var closeCity in city.GetNeighboringCities(8).Where(it => it.Civ == civInfo))
        {
            // Try to find if the other city has planned to build a road to this city
            if (roadsToBuildByCitiesCache.TryGetValue(closeCity, out var closeCityPlans))
            {
                // There should only ever be one or zero possible connections from their city to this city
                var roadToBuild = closeCityPlans.FirstOrDefault(it => it.FromCity == city || it.ToCity == city);
                if (roadToBuild != null)
                {
                    // We already did the hard work, there can't be any other possible roads to this city
                    roadPlans.Add(roadToBuild);
                    continue;
                }
            }

            if (roadToCapitalStatus == BestRoadAvailable)
            {
                // Avoid invoking [MapPathing.getRoadPath]
                continue;
            }

            // Try to build a plan for the road to the city
            // Kotlin note: May return inconsistent paths across turns due to worker position, this makes it impossible to plan an exact road resulting in excessive roads built
            List<TileClass>? roadPath;
            if (roadPathing != null)
                roadPath = roadPathing.GetRoadPath(city, closeCity);
            else if (IndexOfCity(civInfo.Cities, city) < IndexOfCity(civInfo.Cities, closeCity))
                roadPath = MapPathing.GetRoadPath(civInfo, city.GetCenterTile(), closeCity.GetCenterTile());
            else
                roadPath = MapPathing.GetRoadPath(civInfo, closeCity.GetCenterTile(), city.GetCenterTile());
            if (roadPath == null) continue;

            var worstRoadStatus = GetWorstRoadTypeInPath(roadPath);
            if (worstRoadStatus == BestRoadAvailable) continue;

            // Make sure that we are taking in to account the other cities needs
            var roadPriority = Math.Max(basePriority, RankRoadCapitalPriority(closeCity.CityStats.GetRoadTypeOfConnectionToCapital()));
            if (worstRoadStatus == RoadStatus.None)
            {
                // A completely unroaded connection is a strategic task, not just another tile improvement.
                // Give it enough extra weight to compete with ordinary farms/mines while preserving the
                // normal offset for routes that already have some road infrastructure.
                roadPriority += 4;
            }
            else if (worstRoadStatus == RoadStatus.Road && BestRoadAvailable == RoadStatus.Railroad)
            {
                roadPriority += 1;
            }
            if (closeCity.CityStats.GetRoadTypeOfConnectionToCapital() > roadToCapitalStatus)
                roadPriority += 1;

            var newRoadPlan = new RoadPlan(roadPath, roadPriority + (city.Population.Population + closeCity.Population.Population) / 4f, city, closeCity, BestRoadAvailable);
            roadPlans.Add(newRoadPlan);
        }

        var bestPlan = ChooseBestPlan(roadPlans);
        if (bestPlan != null)
        {
            roadPlans.Clear();
            roadPlans.Add(bestPlan);
        }
        // If and only if we have no roads to build to close-by cities then we check for a road to build to the capital
        // The condition !city.isConnectedToCapital() is to avoid BFS for cities connected to capital with roads when railroads are unlocked
        else if (roadPlans.Count == 0 && roadToCapitalStatus < BestRoadAvailable && !city.IsConnectedToCapital())
        {
            var roadToCapital = GetRoadToConnectCityToCapital(city);

            if (roadToCapital != null)
            {
                var worstRoadStatus = GetWorstRoadTypeInPath(roadToCapital.Value.Path);
                var roadPriority = basePriority;
                roadPriority += worstRoadStatus == RoadStatus.None ? 2f : 1f;

                bestPlan = new RoadPlan(roadToCapital.Value.Path, roadPriority + city.Population.Population / 2f, city, roadToCapital.Value.City, BestRoadAvailable);
                roadPlans.Add(bestPlan);
            }
        }

        if (bestPlan != null)
        {
            foreach (var tile in bestPlan.Tiles)
                if (!TilesOfRoadsMap.TryGetValue(tile, out var existing) || existing.Priority < bestPlan.Priority)
                    TilesOfRoadsMap[tile] = bestPlan;
        }

        roadsToBuildByCitiesCache[city] = roadPlans;
        return roadPlans;
    }

    /// <summary>
    /// The best road plan is that which reuses existing roads to upgrade them to railroads if researched,
    /// to avoid duplication of roads to newly settled or conquered cities that didn't exist during old road construction.
    /// Followed by choosing a plan with shortest distance. Sorts <paramref name="planList"/> in place (stable).
    /// </summary>
    /// <returns>The shortest road plan and drop other plans</returns>
    private RoadPlan? ChooseBestPlan(List<RoadPlan> planList)
    {
        if (planList.Count < 2)
            return planList.FirstOrDefault();

        var existingRoadPlans = new List<RoadPlan>();
        static RoadPlan? ShortestPlan(List<RoadPlan> roadPlans) => roadPlans.MinBy(it => it.NumberOfRoadsToBuild);
        // If there are multiple shortest distance road plans make minByOrNull priotirize ones with closer cities
        // helps to avoid road crossovers (but not completely) due to roadPreferredMovementCost function
        var sorted = planList.OrderBy(it => it.FromCity.GetCenterTile().AerialDistanceTo(it.ToCity.GetCenterTile())).ToList();
        planList.Clear();
        planList.AddRange(sorted);

        if (BestRoadAvailable != RoadStatus.Railroad)
            return ShortestPlan(planList);

        foreach (var roadPlan in planList)
            if (roadPlan.Tiles.All(it => it.RoadStatus > RoadStatus.None || it.RoadIsPillaged))
                existingRoadPlans.Add(roadPlan);

        return existingRoadPlans.Count == 0 ? ShortestPlan(planList) : ShortestPlan(existingRoadPlans);
    }

    /// <returns>Lowest road level (aka. road type) in road path</returns>
    private static RoadStatus GetWorstRoadTypeInPath(List<TileClass> path)
    {
        var worstRoadTile = RoadStatus.Railroad;
        foreach (var tile in path)
        {
            if (tile.GetUnpillagedRoad() < worstRoadTile)
            {
                worstRoadTile = tile.GetUnpillagedRoad();
                if (worstRoadTile == RoadStatus.None)
                    return RoadStatus.None;
            }
        }
        return worstRoadTile;
    }

    /// <summary>
    /// Returns a road that can connect this city to the capital.
    /// This is a very expensive function that doesn't necessarily produce the same roads as in <see cref="GetRoadsToBuildFromCity"/>.
    /// So it should only be used if it is the only road that a city wants to build.
    /// </summary>
    /// <returns>The tiles that resemble the road to build and the city that the road will connect to</returns>
    private (CityClass City, List<TileClass> Path)? GetRoadToConnectCityToCapital(CityClass city)
    {
        var connectedCityTiles = tilesOfConnectedCities.Value;
        if (connectedCityTiles.Count == 0) return null; // In mods with no capital city indicator, there are no connected cities

        bool IsCandidateTilePredicate(TileClass it) => it.IsLand && MapPathing.IsValidRoadPathTile(city.Civ, it);
        var toConnectTile = city.GetCenterTile();
        var cityTilesToSeek = new HashSet<TileClass>(connectedCityTiles);
        var roadPathing = civInfo.GameInfo.EngineOptions.RoadPathing;
        if (roadPathing != null)
        {
            var pathToCity = roadPathing.GetRoadPathToAny(city, cityTilesToSeek, WorkerAutomationConst.MaxBfsReachPadding);
            if (pathToCity == null) return null;
            var cityTile = pathToCity[^1];
            return (cityTile.GetCity().NotNull(), pathToCity);
        }

        if (!bfsCache.TryGetValue(toConnectTile.Position, out var bfs))
        {
            bfs = new BFS(toConnectTile, IsCandidateTilePredicate)
            {
                MaxSize = HexMath.GetNumberOfTilesInHexagon(
                    WorkerAutomationConst.MaxBfsReachPadding + connectedCityTiles.Min(it => it.AerialDistanceTo(toConnectTile)))
            };
            bfsCache[toConnectTile.Position] = bfs;
        }

        var nextTile = bfs.NextStep();
        while (nextTile != null)
        {
            if (cityTilesToSeek.Contains(nextTile))
            {
                // We have a winner!
                var pathToCity = bfs.GetPathTo(nextTile);
                return (nextTile.GetCity().NotNull(), pathToCity.ToList());
            }
            nextTile = bfs.NextStep();
        }
        return null;
    }

    /// <summary>
    /// Most importantly builds the cache so that WorkerAutomation.ChooseImprovement knows later what tiles a road should be built on.
    /// </summary>
    /// <param name="unit">Civilian unit which may want to connect cities</param>
    /// <returns>A list of all cities the <paramref name="unit"/> will try to connect if in its vicinity</returns>
    internal List<CityClass> GetNearbyCitiesToConnect(MapUnitClass unit)
    {
        if (BestRoadAvailable == RoadStatus.None) return [];
        var candidateCities = civInfo.Cities.Where(it =>
            // Cities that are too far away make the canReach() calculations devastatingly long
            it.GetCenterTile().AerialDistanceTo(unit.GetTile()) < 20 && GetRoadsToBuildFromCity(it).Count != 0).ToList();
        return candidateCities; // empty means do nothing
    }

    /// <summary>
    /// Looks for work to connect cities. Used to search for far away roads to build.
    /// </summary>
    /// <param name="unit">Civilian unit which will try to connect cities</param>
    /// <param name="candidateCities">The cities from <see cref="GetNearbyCitiesToConnect"/></param>
    /// <returns>Whether we actually did anything</returns>
    internal bool TryConnectingCities(MapUnitClass unit, List<CityClass> candidateCities)
    {
        if (BestRoadAvailable == RoadStatus.None) return false;

        if (candidateCities.Count == 0) return false; // do nothing.
        var unitStartingTile = unit.GetTile();

        // Search through ALL candidate cities for the closest tile to build a road on
        foreach (var toConnectCity in candidateCities.OrderBy(it => it.GetCenterTile().AerialDistanceTo(unitStartingTile)))
        {
            var tilesByPriority = GetRoadsToBuildFromCity(toConnectCity)
                .SelectMany(roadPlan => roadPlan.Tiles.Select(tile => (Tile: tile, roadPlan.Priority)));
            var tilesSorted = tilesByPriority
                .Where(it => !it.Tile.IsMarkedForCreatesOneImprovement() && it.Tile.GetUnpillagedRoad() < BestRoadAvailable)
                .OrderBy(it => it.Tile.AerialDistanceTo(unitStartingTile) - it.Priority / 10f);
            var bestTile = tilesSorted.FirstOrDefault(it =>
                unitStartingTile == it.Tile || (unit.Movement.CanMoveTo(it.Tile) && unit.Movement.CanReach(it.Tile))).Tile;
            if (bestTile == null) continue; // Apparently we can't reach any of these tiles at all

            if (bestTile != unitStartingTile && unit.HasMovement())
                unit.Movement.HeadTowards(bestTile);
            if (unit.HasMovement() && bestTile == unit.GetTile() && bestTile.ImprovementInProgress != BestRoadAvailable.ToString())
            {
                var improvement = BestRoadAvailable.Improvement(civInfo.GameInfo.Ruleset).NotNull();
                bestTile.StartWorkingOnImprovement(improvement, civInfo, unit);
            }
            return true;
        }

        return false;
    }
}
