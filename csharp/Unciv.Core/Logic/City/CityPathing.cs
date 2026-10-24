using CivilizationObject = Unciv.Logic.Civilization.Civilization;
using PathingMapObject = Unciv.Logic.Map.PathingMap.PathingMap;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Logic.City;

// Ported from the A* path functions of core/src/com/unciv/logic/city/City.kt (T093): getLandAttackPath, getAmphibiousAttackPath, getRoadPath,
// getRoadPathToAny, their three transient PathingMap caches and clearCaches. A partial of City so City.cs keeps its size.
// - ConcurrentHashMap<Civilization, PathingMap> is a Dictionary keyed by civ identity (only get/put/clear, never iterated).
// - Kotlin String comparison (`id < destination.id`) is ordinal UTF-16.

public partial class City
{
    private readonly Dictionary<CivilizationObject, PathingMapObject> landAttackPathing = [];
    private readonly Dictionary<CivilizationObject, PathingMapObject> amphibiousAttackPathing = [];
    private PathingMapObject? potentialRoadPathing;

    private PathingMapObject PotentialRoadPathing => potentialRoadPathing ??= PathingMapObject.CreateRoadPathingMap(Civ, GetCenterTile());

    public List<TileClass>? GetLandAttackPath(City destination, int maxTurns = PathingMapObject.MaxValidTurns)
    {
        if (!landAttackPathing.TryGetValue(destination.Civ, out var pathingCache))
            landAttackPathing[destination.Civ] = pathingCache = PathingMapObject.CreateLandAttackPathingMap(Civ, GetCenterTile(), destination.Civ);
        return pathingCache.GetShortestPath(destination.GetCenterTile(), maxTurns);
    }

    public List<TileClass>? GetAmphibiousAttackPath(City destination, int maxTurns = PathingMapObject.MaxValidTurns)
    {
        if (!amphibiousAttackPathing.TryGetValue(destination.Civ, out var pathingCache))
            amphibiousAttackPathing[destination.Civ] = pathingCache = PathingMapObject.CreateAmphibiousAttackPathingMap(Civ, GetCenterTile(), destination.Civ);
        return pathingCache.GetShortestPath(destination.GetCenterTile(), maxTurns);
    }

    public List<TileClass>? GetRoadPath(City destination, int maxTurns = PathingMapObject.MaxValidTurns) =>
        string.CompareOrdinal(Id, destination.Id) < 0
            ? PotentialRoadPathing.GetShortestPath(destination.GetCenterTile(), maxTurns)
            : destination.GetRoadPath(this, maxTurns);

    public List<TileClass>? GetRoadPathToAny(IReadOnlySet<TileClass> destinations, int maxBfsReachPadding)
    {
        // Kotlin note: replace with multi-target AStar
        var maxTurns = maxBfsReachPadding + destinations.Min(it => it.AerialDistanceTo(GetCenterTile()));
        var otherCity = PotentialRoadPathing.BfsUntilMatchingTile((tile, _) => destinations.Contains(tile), maxTurns);
        if (otherCity == null) return null;
        return PotentialRoadPathing.GetShortestPath(otherCity); // this just reads from cached results
    }

    public void ClearCaches()
    {
        landAttackPathing.Clear();
        amphibiousAttackPathing.Clear();
        potentialRoadPathing?.Clear();
    }
}
