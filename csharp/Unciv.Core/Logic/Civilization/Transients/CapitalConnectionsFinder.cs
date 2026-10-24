using Unciv.Logic.Map;
using Unciv.Logic.Map.Tiles;
using Unciv.Models.Ruleset.Unique;

namespace Unciv.Logic.Civilization.Transients;

// Ported from core/src/com/unciv/logic/civilization/transients/CapitalConnectionsFinder.kt (T058).
//
// Deviations:
// - City is the ICityState seam until T060. Cities are keyed by reference (Kotlin City has no equals/hashCode).
// - The result map is insertion-ordered (Kotlin: a java.util.HashMap with identity-hashed City keys, i.e. an arbitrary order that reaches
//   the order of the "connected/disconnected" notifications). Insertion order is the deterministic choice.
// - EnumSet<CapitalConnectionMedium> is a HashSet (only membership and any() are used).
// - The timeThis profiling wrappers and the purity annotations are dropped.

/// <summary>The travel mediums a city can be reached by from the capital.</summary>
public enum CapitalConnectionMedium
{
    Unknown, // future use only
    Start,
    Road,
    Railroad,
    Harbor,
    HarborFromRoad,
    HarborFromRailroad,
}

public static class CapitalConnectionMediumExtensions
{
    /// <summary>Kotlin <c>roadType</c> (default <see cref="RoadStatus.None"/>).</summary>
    public static RoadStatus RoadType(this CapitalConnectionMedium medium) => medium switch
    {
        CapitalConnectionMedium.Road => RoadStatus.Road,
        CapitalConnectionMedium.Railroad => RoadStatus.Railroad,
        CapitalConnectionMedium.HarborFromRoad => RoadStatus.Road,
        CapitalConnectionMedium.HarborFromRailroad => RoadStatus.Railroad,
        _ => RoadStatus.None,
    };

    /// <summary>Kotlin <c>checkRailroad</c> (default <c>false</c>).</summary>
    public static bool CheckRailroad(this CapitalConnectionMedium medium) => medium is
        CapitalConnectionMedium.Start or CapitalConnectionMedium.Railroad or CapitalConnectionMedium.HarborFromRailroad;
}

public class CapitalConnectionsFinder
{
    private readonly Civilization civInfo;
    private List<ICityState> citiesToCheck;
    private readonly OrderedDictionary<ICityState, HashSet<CapitalConnectionMedium>> citiesReachedToMediums = [];
    private List<ICityState> newCitiesToCheck = null!; // Kotlin lateinit

    /// <summary>All cities we want to know about, including our own</summary>
    private readonly List<ICityState> openBordersCivCities;

    private readonly bool roadIsResearched;
    private readonly bool railroadIsResearched;

    public CapitalConnectionsFinder(Civilization civInfo)
    {
        this.civInfo = civInfo;
        var capital = civInfo.GetCapital();
        citiesToCheck = capital == null ? [] : [capital];
        foreach (var city in citiesToCheck) citiesReachedToMediums[city] = [CapitalConnectionMedium.Start];

        openBordersCivCities = civInfo.GameInfo.GetCities().Cast<ICityState>().Where(it => CanEnterBordersOf(it.Civ)).ToList();

        var ruleset = civInfo.GameInfo.Ruleset;
        roadIsResearched = ruleset.TileImprovements.TryGetValue(nameof(RoadStatus.Road), out var road)
            && (road.TechRequired == null || civInfo.Tech.IsResearched(road.TechRequired));
        railroadIsResearched = ruleset.TileImprovements.TryGetValue(nameof(RoadStatus.Railroad), out var railroad)
            && (railroad.TechRequired == null || civInfo.Tech.IsResearched(railroad.TechRequired));
    }

    public OrderedDictionary<ICityState, HashSet<CapitalConnectionMedium>> Find()
    {
        // We map which cities we've reached, to the mediums they've been reached by -
        // this is so we know that if we've seen which cities can be connected by port A, and one
        // of those is city B, then we don't need to check the cities that B can connect to by port,
        // since we'll get the same cities we got from A, since they're connected to the same sea.
        var maxCities = openBordersCivCities.Count; // Won't change during this loop
        while (citiesToCheck.Count != 0 && citiesReachedToMediums.Count < maxCities)
        {
            newCitiesToCheck = [];
            foreach (var cityToConnectFrom in citiesToCheck)
            {
                if (ContainsHarbor(cityToConnectFrom))
                    CheckHarbor(cityToConnectFrom);
                if (railroadIsResearched)
                {
                    var mediumsReached = citiesReachedToMediums[cityToConnectFrom];
                    if (mediumsReached.Any(it => it.CheckRailroad()))
                        CheckRailroad(cityToConnectFrom); // This is only relevant for city connection if there is an unbreaking line from the capital
                }
                if (roadIsResearched)
                    CheckRoad(cityToConnectFrom);
            }
            citiesToCheck = newCitiesToCheck;
        }
        return citiesReachedToMediums;
    }

    private void CheckRoad(ICityState cityToConnectFrom)
    {
        Check(
            cityToConnectFrom,
            transportType: CapitalConnectionMedium.Road,
            overridingTransportType: CapitalConnectionMedium.Railroad,
            tileFilter: tile => tile.HasConnection(civInfo));
    }

    private void CheckRailroad(ICityState cityToConnectFrom)
    {
        Check(
            cityToConnectFrom,
            transportType: CapitalConnectionMedium.Railroad,
            overridingTransportType: null,
            tileFilter: tile => tile.GetUnpillagedRoad() == RoadStatus.Railroad);
    }

    private void CheckHarbor(ICityState cityToConnectFrom)
    {
        Check(
            cityToConnectFrom,
            transportType: WasPreviouslyReached(cityToConnectFrom, CapitalConnectionMedium.Railroad, null)
                ? CapitalConnectionMedium.HarborFromRailroad : CapitalConnectionMedium.HarborFromRoad,
            overridingTransportType: CapitalConnectionMedium.HarborFromRailroad,
            tileFilter: tile => tile.IsWater,
            cityFilter: city => ReferenceEquals(city.Civ, civInfo) && ContainsHarbor(city) && !city.IsBlockaded()); // use only own harbors
    }

    private static bool ContainsHarbor(ICityState city) =>
        city.ContainsBuildingUnique(UniqueType.ConnectTradeRoutes);

    private void Check(ICityState cityToConnectFrom,
        CapitalConnectionMedium transportType,
        CapitalConnectionMedium? overridingTransportType,
        Func<Tile, bool> tileFilter,
        Func<ICityState, bool>? cityFilter = null)
    {
        cityFilter ??= _ => true;
        // This is the time-saving mechanism we discussed earlier - If I arrived at this city via a certain BFS,
        // then obviously I already have all the cities that can be reached via that BFS so I don't need to run it again.
        if (WasPreviouslyReached(cityToConnectFrom, transportType, overridingTransportType))
            return;

        var bfs = new BFS(cityToConnectFrom.GetCenterTile(), it =>
        {
            var owner = (Civilization?)it.GetOwner();
            return (it.IsCityCenter() || tileFilter(it)) && (owner == null || CanEnterBordersOf(owner));
        });
        bfs.StepToEnd();
        var reachedCities = openBordersCivCities.Where(it =>
            bfs.HasReachedTile(it.GetCenterTile()) && cityFilter(it)).ToList();
        foreach (var reachedCity in reachedCities)
        {
            AddCityIfFirstEncountered(reachedCity);
            if (ReferenceEquals(reachedCity, cityToConnectFrom)) continue;
            if (!WasPreviouslyReached(reachedCity, transportType, overridingTransportType))
                citiesReachedToMediums[reachedCity].Add(transportType);
        }
    }

    private void AddCityIfFirstEncountered(ICityState reachedCity)
    {
        if (!citiesReachedToMediums.ContainsKey(reachedCity))
        {
            newCitiesToCheck.Add(reachedCity);
            citiesReachedToMediums[reachedCity] = [];
        }
    }

    private bool WasPreviouslyReached(ICityState city, CapitalConnectionMedium transportType, CapitalConnectionMedium? overridingTransportType)
    {
        var mediums = citiesReachedToMediums[city];
        return mediums.Contains(transportType) || (overridingTransportType != null && mediums.Contains(overridingTransportType.Value));
    }

    private bool CanEnterBordersOf(Civilization otherCiv)
    {
        if (ReferenceEquals(otherCiv, civInfo)) return true; // own borders are always open
        if (otherCiv.IsBarbarian || civInfo.IsBarbarian) return false; // barbarians blocks the routes
        var diplomacyManager = civInfo.GetDiplomacyManager(otherCiv);
        if (diplomacyManager == null) return false; // not encountered yet
        if (otherCiv.IsCityState && !diplomacyManager.IsAtWarStatus) return true;
        return diplomacyManager.HasOpenBorders;
    }
}
