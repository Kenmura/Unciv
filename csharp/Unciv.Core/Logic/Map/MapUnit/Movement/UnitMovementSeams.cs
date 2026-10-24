using Unciv.Logic.Map.Tiles;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Logic.Map.MapUnits;

// T067 (UnitMovement): the A* pathfinding classes (PathingMap, T093) are not ported yet. UnitMovement reaches them only through the members
// below. Kotlin reads <c>UncivGame.Current.settings.useAStarPathfinding</c> (default false) in four places: a headless engine has no
// client settings, so the flag is "UnitMovement.PathingMaps is set". T093 implements the interfaces, retypes them to PathingMap and
// sets the property where the setting is read.

/// <summary>What UnitMovement calls on <c>PathingMap</c> (<c>aStarPathing</c>, <c>aStarPathingWithoutZoneControl</c>, <c>aStarPathingWithoutEscort</c>, <c>roadPathing</c>). TODO(T093)</summary>
public interface IUnitPathingMap
{
    /// <summary><c>pathingMap.getMovementToTilesAtPosition()</c></summary>
    PathsToTilesWithinTurn GetMovementToTilesAtPosition();
    /// <summary><c>pathingMap.getShortestPath(destination)</c>; <c>null</c> when there is none.</summary>
    List<Tile>? GetShortestPath(Tile destination);
    /// <summary><c>pathingMap.clear()</c></summary>
    void Clear();
}

/// <summary>What UnitMovement calls on the <c>PathingMap</c> companion. TODO(T093)</summary>
public interface IUnitPathingMapFactory
{
    /// <summary><c>PathingMap.createUnitPathingMap(unit, considerZoneOfControl, includeEscortUnit)</c></summary>
    IUnitPathingMap CreateUnitPathingMap(MapUnit unit, bool considerZoneOfControl = true, bool includeEscortUnit = true);
    /// <summary><c>PathingMap.createRoadPathingMap(civ, startTile)</c></summary>
    IUnitPathingMap CreateRoadPathingMap(CivilizationObject civ, Tile startTile);
}
