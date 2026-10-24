using Unciv.Logic.Automation.Civilization;
using Unciv.Logic.Automation.Unit;
using Unciv.Logic.Map.MapUnits;
using Unciv.Logic.Map.Tiles;
using CityObject = Unciv.Logic.City.City;
using CivilizationClass = Unciv.Logic.Civilization.Civilization;
using MapUnitClass = Unciv.Logic.Map.MapUnits.MapUnit;

namespace Unciv.Logic.Map.PathingMap;

// T093: Kotlin reads `UncivGame.Current.settings.useAStarPathfinding` at the call sites of PathingMap. The headless engine has no settings object:
// the call sites go through the small interfaces below (GameClientState.UnitPathing / AttackPathing / RoadPathing, null = setting off), and
// this class is the PathingMap-backed implementation of all of them (GameClientState.UseAStarPathfinding installs it).

/// <summary>The A* implementation of the pathing seams: <see cref="PathingMap"/> for units, <c>City.getLandAttackPath</c> and friends for cities.</summary>
public sealed class AStarPathing : IUnitPathingMapFactory, ICityAttackPathing, ICityRoadPathing
{
    public static readonly AStarPathing Instance = new();

    private AStarPathing() { }

    public IUnitPathingMap CreateUnitPathingMap(MapUnitClass unit, bool considerZoneOfControl = true, bool includeEscortUnit = true) =>
        PathingMap.CreateUnitPathingMap(unit, considerZoneOfControl, includeEscortUnit);

    public IUnitPathingMap CreateRoadPathingMap(CivilizationClass civ, Tile startTile) => PathingMap.CreateRoadPathingMap(civ, startTile);

    public List<Tile>? GetLandAttackPath(CityObject from, CityObject destination, int maxTurns) => from.GetLandAttackPath(destination, maxTurns);

    public List<Tile>? GetAmphibiousAttackPath(CityObject from, CityObject destination, int maxTurns) => from.GetAmphibiousAttackPath(destination, maxTurns);

    public List<Tile>? GetRoadPath(CityObject from, CityObject destination) => from.GetRoadPath(destination);

    public List<Tile>? GetRoadPathToAny(CityObject city, IReadOnlySet<Tile> destinations, int maxBfsReachPadding) =>
        city.GetRoadPathToAny(destinations, maxBfsReachPadding);
}
