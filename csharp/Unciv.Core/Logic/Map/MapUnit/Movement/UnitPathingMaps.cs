using Unciv.Logic.Map.Tiles;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Logic.Map.MapUnits;

// UnitMovement reaches the A* pathfinding (PathingMap, T093) only through the interfaces below. Kotlin reads
// `UncivGame.Current.settings.useAStarPathfinding` (default false) in four places: a headless engine has no client settings, so the
// flag is "GameClientState.UnitPathing is set" (PathingMap implements IUnitPathingMap, AStarPathing is the factory).

/// <summary>What UnitMovement calls on <c>PathingMap</c> (<c>aStarPathing</c>, <c>aStarPathingWithoutZoneControl</c>, <c>aStarPathingWithoutEscort</c>, <c>roadPathing</c>).</summary>
public interface IUnitPathingMap
{
    /// <summary><c>pathingMap.getMovementToTilesAtPosition()</c></summary>
    PathsToTilesWithinTurn GetMovementToTilesAtPosition();
    /// <summary><c>pathingMap.getShortestPath(destination)</c>; <c>null</c> when there is none.</summary>
    List<Tile>? GetShortestPath(Tile destination);
    /// <summary><c>pathingMap.clear()</c></summary>
    void Clear();
}

/// <summary>What UnitMovement calls on the <c>PathingMap</c> companion.</summary>
public interface IUnitPathingMapFactory
{
    /// <summary><c>PathingMap.createUnitPathingMap(unit, considerZoneOfControl, includeEscortUnit)</c></summary>
    IUnitPathingMap CreateUnitPathingMap(MapUnit unit, bool considerZoneOfControl = true, bool includeEscortUnit = true);
    /// <summary><c>PathingMap.createRoadPathingMap(civ, startTile)</c></summary>
    IUnitPathingMap CreateRoadPathingMap(CivilizationObject civ, Tile startTile);
}
