using System.Collections;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Logic.Map.Tiles;
using Unciv.Logic.Trade.Routes;
using Unciv.Models;
using Unciv.Models.Ruleset.Unique;
using Unciv.Utils;
using CityObject = Unciv.Logic.City.City;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Logic.Map.MapUnits;

// Ported from core/src/com/unciv/logic/map/mapunit/movement/UnitMovement.kt (T067). Stripped: the purity annotations, the timeThis profiling.
// - The A* branches (`UncivGame.Current.settings.useAStarPathfinding`) go through PathingMaps (UnitPathingMaps.cs): null = setting off.
// - Tile.GetOwner()/GetCity() are typed by their seams (T053 rule); the owner is always a Civilization / City, hence the casts.
// - Kotlin BitSet is a BitArray over the tile list; ArrayList<Boolean?> is a List<bool?> (see GetOrPut).
// - The cost caches keep Kotlin's Float values; sorting keeps Kotlin's stable order.

/// <summary>
/// Handles all movement-related logic for a <see cref="MapUnit"/>.
/// getMovementToTilesAtPosition does a breadth-first search outwards from a given position, calculating the movement cost to each tile
/// within the units remaining movement. getShortestPath is the multi-turn pathing logic (reasonably good, not optimal paths).
/// teleportToClosestMoveableTile iterates over nearby tiles and uses getPathBetweenTiles (BFS) to see if the unit can reach them.
/// </summary>
// The members MapUnit calls are virtual only so that MapUnit tests can stand in for the movement (Unciv.Core.Tests FakeUnitMovement).
public class UnitMovement
{
    public MapUnit Unit { get; }

    private readonly PathfindingCache pathfindingCache;
    private readonly KotlinLazy<IUnitPathingMap> aStarPathing;
    private readonly KotlinLazy<IUnitPathingMap> aStarPathingWithoutZoneControl;
    private readonly KotlinLazy<IUnitPathingMap> aStarPathingWithoutEscort;
    private readonly KotlinLazy<IUnitPathingMap> roadPathing;

    private IUnitPathingMapFactory? pathingMapsOverride;

    /// <summary>The A* pathfinding: this unit's override, else <c>GameEngineOptions.UnitPathing</c>. <c>null</c> is Kotlin's <c>useAStarPathfinding = false</c> (the default).</summary>
    internal IUnitPathingMapFactory? PathingMaps
    {
        get => pathingMapsOverride ?? Unit.Civ.GameInfo.EngineOptions.UnitPathing;
        set => pathingMapsOverride = value;
    }

    public UnitMovement(MapUnit unit)
    {
        Unit = unit;
        pathfindingCache = new PathfindingCache(unit);
        aStarPathing = new KotlinLazy<IUnitPathingMap>(() => PathingMaps.NotNull().CreateUnitPathingMap(unit));
        aStarPathingWithoutZoneControl = new KotlinLazy<IUnitPathingMap>(() => PathingMaps.NotNull().CreateUnitPathingMap(unit, considerZoneOfControl: false));
        aStarPathingWithoutEscort = new KotlinLazy<IUnitPathingMap>(() => PathingMaps.NotNull().CreateUnitPathingMap(unit, includeEscortUnit: false));
        roadPathing = new KotlinLazy<IUnitPathingMap>(() => PathingMaps.NotNull().CreateRoadPathingMap(unit.Civ, unit.CurrentTile));
    }

    public class ParentTileAndTotalMovement(Tile tile, Tile parentTile, float totalMovement)
    {
        public Tile Tile { get; } = tile;
        public Tile ParentTile { get; } = parentTile;
        public float TotalMovement { get; } = totalMovement;
    }

    public bool IsUnknownTileWeShouldAssumeToBePassable(Tile tile) => !Unit.Civ.HasExplored(tile);

    private IUnitPathingMap SelectPathingMap(bool considerZoneOfControl, bool includeOtherEscortUnit)
    {
        if (!considerZoneOfControl && !includeOtherEscortUnit) throw new ArgumentException("Failed requirement.");
        return !considerZoneOfControl ? aStarPathingWithoutZoneControl.Value
            : includeOtherEscortUnit || !Unit.IsEscorting() ? aStarPathing.Value
            : aStarPathingWithoutEscort.Value;
    }

    /// <summary>Kotlin <c>ArrayList&lt;T?&gt;.getOrPut(index) {}</c>: caches that replace hashmaps contain nulls where the answer is not known yet.</summary>
    private static bool GetOrPut(List<bool?> list, int index, Func<bool> getValue)
    {
        if (index >= 0 && index < list.Count && list[index] is { } currentValue) return currentValue;

        var value = getValue();

        while (list.Count <= index) list.Add(null); // Fill with nulls until we reach the index
        list[index] = value;
        return value;
    }

    /// <summary>
    /// Gets the tiles the unit could move to at <paramref name="position"/> with <paramref name="unitMovement"/>.
    /// Does not consider if tiles can actually be entered, use canMoveTo for that.
    /// If a tile can be reached within the turn, but it cannot be passed through, the total distance to it is set to unitMovement
    /// </summary>
    public PathsToTilesWithinTurn GetMovementToTilesAtPosition(
        HexCoord position,
        float unitMovement,
        bool considerZoneOfControl = true,
        BitArray? tilesToIgnoreBitset = null,
        List<bool?>? canPassThroughCache = null,
        Dictionary<int, float>? movementCostCache = null,
        bool includeOtherEscortUnit = true)
    {
        canPassThroughCache ??= [];
        movementCostCache ??= [];
        if (PathingMaps != null)
            return SelectPathingMap(considerZoneOfControl, includeOtherEscortUnit).GetMovementToTilesAtPosition();
        var distanceToTiles = new PathsToTilesWithinTurn();

        var currentUnitTile = Unit.CurrentTile;
        // This is for performance, because this is called all the time
        var unitTile = position == currentUnitTile.Position ? currentUnitTile : currentUnitTile.TileMap[position];
        distanceToTiles[unitTile] = new ParentTileAndTotalMovement(unitTile, unitTile, 0f);

        // If I can't move my only option is to stay...
        if (unitMovement == 0f || Unit.Cache.CannotMove) return distanceToTiles;
        // If our escort can't move, ditto
        if (includeOtherEscortUnit && Unit.IsEscorting()
            && Unit.GetOtherEscortUnit()?.CurrentMovement == 0f) return distanceToTiles;

        // Loop-invariant across the whole search: escort state and the usable movement cap never change
        // during one call, but were recomputed on every edge relaxation below.
        var usableMovement = includeOtherEscortUnit && Unit.IsEscorting()
            ? Math.Min(unitMovement, Unit.GetOtherEscortUnit().NotNull().CurrentMovement)
            : unitMovement;

        IReadOnlyList<Tile> tilesToCheck = [unitTile];

        while (tilesToCheck.Count != 0)
        {
            var updatedTiles = new List<Tile>();
            foreach (var tileToCheck in tilesToCheck)
            {
                // Loop-invariant across this tile's neighbors: was looked up twice per neighbor below.
                var tileToCheckMovement = distanceToTiles[tileToCheck].TotalMovement;
                foreach (var neighbor in tileToCheck.Neighbors)
                {
                    // ignore this tile
                    if (tilesToIgnoreBitset != null && GetBit(tilesToIgnoreBitset, neighbor.ZeroBasedIndex)) continue; // ignore this tile
                    float totalDistanceToTile;
                    if (!neighbor.IsExplored(Unit.Civ))
                        totalDistanceToTile = tileToCheckMovement + 1f; // If we don't know then we just guess it to be 1.
                    else if (!GetOrPut(canPassThroughCache, neighbor.ZeroBasedIndex, () => CanPassThrough(neighbor)))
                        totalDistanceToTile = unitMovement; // Can't go here.
                    // The reason that we don't just "return" is so that when calculating how to reach an enemy,
                    // You need to assume his tile is reachable, otherwise all movement algorithms on reaching enemy
                    // cities and units goes kaput.
                    else
                    {
                        var key = HexMath.TilesAndNeighborUniqueIndex(tileToCheck, neighbor);
                        if (!movementCostCache.TryGetValue(key, out var movementCost))
                        {
                            movementCost = MovementCost.GetMovementCostBetweenAdjacentTilesEscort(Unit, tileToCheck, neighbor, considerZoneOfControl, includeOtherEscortUnit);
                            movementCostCache[key] = movementCost;
                        }
                        totalDistanceToTile = tileToCheckMovement + movementCost;
                    }

                    var hasBestPath = distanceToTiles.TryGetValue(neighbor, out var currentBestPath);
                    if (!hasBestPath || currentBestPath.NotNull().TotalMovement > totalDistanceToTile) // this is the new best path
                    {
                        if (totalDistanceToTile < usableMovement - Constants.MinimumMovementEpsilon) // We can still keep moving from here!
                            updatedTiles.Add(neighbor);
                        else
                            totalDistanceToTile = usableMovement;
                        // In Civ V, you can always travel between adjacent tiles, even if you don't technically
                        // have enough movement points - it simply depletes what you have

                        distanceToTiles[neighbor] = new ParentTileAndTotalMovement(neighbor, tileToCheck, totalDistanceToTile);
                    }
                }
            }

            tilesToCheck = updatedTiles;
        }

        return distanceToTiles;
    }

    /// <summary>Kotlin <c>BitSet.get</c>: bits beyond the size are false.</summary>
    private static bool GetBit(BitArray bits, int index) => index >= 0 && index < bits.Length && bits[index];

    /// <summary>
    /// Does not consider if the <paramref name="destination"/> tile can actually be entered, use <see cref="CanMoveTo"/> for that.
    /// Returns an empty list if there's no way to get to the destination.
    /// </summary>
    public List<Tile> GetShortestPath(Tile destination, bool avoidDamagingTerrain = false)
    {
        if (Unit.Cache.CannotMove) return [];
        if (PathingMaps != null)
            return aStarPathing.Value.GetShortestPath(destination) ?? [];

        // First try and find a path without damaging terrain
        if (!avoidDamagingTerrain && Unit.Civ.PassThroughImpassableUnlocked && Unit.BaseUnit.IsLandUnit)
        {
            var damageFreePath = GetShortestPath(destination, true);
            if (damageFreePath.Count != 0) return damageFreePath;
        }

        if (!destination.Neighbors.Any(it => IsUnknownTileWeShouldAssumeToBePassable(it) || CanPassThrough(it)))
        {
            // edge case where this all of the tiles around the destination are
            // explored and known the unit can't pass through any of thoes tiles so we know a priori that no path exists
            pathfindingCache.SetShortestPathCache(destination, []);
            return [];
        }
        var cachedPath = pathfindingCache.GetShortestPathCache(destination);
        if (cachedPath.Count != 0)
            return cachedPath;

        var currentTile = Unit.GetTile();
        if (currentTile.Position == destination.Position)
        {
            // edge case that's needed, so that workers will know that they can reach their own tile. *sigh*
            pathfindingCache.SetShortestPathCache(destination, [currentTile]);
            return [currentTile];
        }

        IReadOnlyList<Tile> tilesToCheck = [currentTile];
        var movementTreeParents = new Dictionary<Tile, Tile?> // contains a map of "you can get from X to Y in that turn"
        {
            [currentTile] = null,
        };

        var distance = 1;
        var unitMaxMovement = (float)Unit.GetMaxMovement();
        var newTilesToCheck = new List<Tile>();
        var visitedTilesBitset = new BitArray(currentTile.TileMap.TileList.Count);
        visitedTilesBitset[currentTile.ZeroBasedIndex] = true;
        var civilization = Unit.Civ;

        var passThroughCacheNew = new List<bool?>(currentTile.TileMap.TileList.Count);
        var movementCostCache = new Dictionary<int, float>();
        var canMoveToCache = new Dictionary<Tile, bool>();

        while (true)
        {
            newTilesToCheck.Clear();

            // When comparing booleans, we get false first, so we need to negate the isLand / isCityState checks
            // By order of preference: 1. Land tiles 2. Aerial distance 3. Not city states
            var tilesByPreference = (Unit.Type.IsLandUnit()
                    ? tilesToCheck.OrderBy(it => !it.IsLand).ThenBy(it => it.AerialDistanceTo(destination)).ThenBy(IsUnfriendlyCityState)
                    : tilesToCheck.OrderBy(it => it.AerialDistanceTo(destination)).ThenBy(IsUnfriendlyCityState))
                .ToList();

            foreach (var tileToCheck in tilesByPreference)
            {
                var distanceToTilesThisTurn = distance == 1
                    ? GetDistanceToTiles(true, passThroughCacheNew, movementCostCache) // check cache
                    : GetMovementToTilesAtPosition(
                        tileToCheck.Position,
                        unitMaxMovement,
                        false,
                        visitedTilesBitset,
                        passThroughCacheNew,
                        movementCostCache);
                foreach (var reachableTile in distanceToTilesThisTurn.Keys)
                {
                    // Avoid damaging terrain on first pass
                    if (avoidDamagingTerrain && Unit.GetDamageFromTerrain(reachableTile) > 0)
                        continue;
                    // Avoid Enemy Territory if Civilian and Automated. For multi-turn pathing
                    if (Unit.IsCivilian() && Unit.IsAutomated() && reachableTile.IsEnemyTerritory(civilization))
                        continue;
                    if (reachableTile == destination)
                    {
                        var path = BuildPathFromParents(destination, tileToCheck, currentTile, movementTreeParents);
                        pathfindingCache.SetShortestPathCache(destination, path);

                        return path;
                    }

                    if (movementTreeParents.ContainsKey(reachableTile)) continue; // We cannot be faster than anything existing...
                    if (!IsUnknownTileWeShouldAssumeToBePassable(reachableTile))
                    {
                        if (!canMoveToCache.TryGetValue(reachableTile, out var canMoveToReachable))
                        {
                            canMoveToReachable = CanMoveTo(reachableTile);
                            canMoveToCache[reachableTile] = canMoveToReachable;
                        }
                        // This is a tile that we can't actually enter - either an intermediary tile containing our unit, or an enemy unit/city
                        if (!canMoveToReachable) continue;
                    }
                    movementTreeParents[reachableTile] = tileToCheck;
                    newTilesToCheck.Add(reachableTile);
                }
            }

            if (newTilesToCheck.Count == 0)
            {
                // there is NO PATH (eg blocked by enemy units)
                pathfindingCache.SetShortestPathCache(destination, []);
                return [];
            }

            // add newTilesToCheck to visitedTiles so we do not path over these tiles in a later iteration
            foreach (var tile in newTilesToCheck) visitedTilesBitset[tile.ZeroBasedIndex] = true;
            // no need to check tiles that are surrounded by reachable tiles, only need to check the edgemost tiles.
            // Because anything we can reach from intermediate tiles, can be more easily reached by the edgemost tiles,
            // since we'll have to pass through an edgemost tile in order to reach the destination anyway
            tilesToCheck = newTilesToCheck.Where(tile => !tile.Neighbors.All(it => visitedTilesBitset[it.ZeroBasedIndex])).ToList();

            distance++;
        }
    }

    private static List<Tile> BuildPathFromParents(Tile destination, Tile tileToCheck, Tile currentTile, Dictionary<Tile, Tile?> movementTreeParents)
    {
        var path = new List<Tile> { destination };
        // Traverse the tree upwards to get the list of tiles leading to the destination
        var intermediateTile = tileToCheck;
        while (intermediateTile != currentTile)
        {
            path.Add(intermediateTile);
            intermediateTile = movementTreeParents[intermediateTile].NotNull();
        }
        path.Reverse(); // and reverse in order to get the list in chronological order
        return path;
    }

    private bool IsUnfriendlyCityState(Tile tile)
    {
        var owner = (CivilizationObject?)tile.GetOwner();
        return owner != null && owner.IsCityState
            && owner.GetDiplomacyManager(Unit.Civ)?.IsRelationshipLevelLT(RelationshipLevel.Friend) == true;
    }

    public class UnreachableDestinationException(string msg) : Exception(msg);

    public Tile GetTileToMoveToThisTurn(Tile finalDestination)
    {
        var currentTile = Unit.GetTile();
        if (currentTile == finalDestination) return currentTile;

        // If we can fly, head there directly
        if ((Unit.BaseUnit.IsAirUnit() || Unit.IsPreparingParadrop()) && CanMoveTo(finalDestination)) return finalDestination;

        var distanceToTiles = GetDistanceToTiles();

        // If the tile is far away, we need to build a path how to get there, and then take the first step
        if (!distanceToTiles.ContainsKey(finalDestination))
        {
            var shortestDestination = GetShortestPath(finalDestination).FirstOrDefault()
                ?? throw new UnreachableDestinationException($"{Unit} {Unit.CurrentTile} cannot reach {finalDestination}");
            if (!distanceToTiles.ContainsKey(shortestDestination))
                return distanceToTiles.Keys.MinBy(it => it.AerialDistanceTo(finalDestination)).NotNull();
            return shortestDestination;
        }

        // we should be able to get there this turn
        if (CanMoveTo(finalDestination))
            return finalDestination;

        // Someone is blocking to the path to the final tile...
        var destinationNeighbors = finalDestination.Neighbors;
        if (destinationNeighbors.Contains(currentTile))
            return currentTile; // We're right nearby anyway, no need to move
        return destinationNeighbors
            .Where(it => distanceToTiles.ContainsKey(it) && CanMoveTo(it))
            .MinBy(it => distanceToTiles[it].TotalMovement) // we can get a little closer
            ?? currentTile; // We can't get closer...
    }

    /// <returns>The tile that we reached this turn</returns>
    public virtual Tile HeadTowards(Tile destination)
    {
        var destinationTileThisTurn = GetTileToMoveToThisTurn(destination);
        MoveToTile(destinationTileThisTurn);
        return Unit.CurrentTile;
    }

    /// <summary>
    /// This is performance-heavy - use as last resort, only after checking everything else!
    /// Also note that REACHABLE tiles are not necessarily tiles that the unit CAN ENTER
    /// </summary>
    /// <seealso cref="CanReachInCurrentTurn"/>
    public virtual bool CanReach(Tile destination) => CanReachCommon(destination, it => GetShortestPath(it).Count != 0);

    /// <summary>Cached and thus not as performance-heavy as <see cref="CanReach"/></summary>
    public bool CanReachInCurrentTurn(Tile destination) => CanReachCommon(destination, it => GetDistanceToTiles().ContainsKey(it));

    private bool CanReachCommon(Tile destination, Func<Tile, bool> specificFunction)
    {
        if (Unit.Cache.CannotMove)
            return destination == Unit.GetTile();
        if (Unit.BaseUnit.IsAirUnit())
            return Unit.CurrentTile.AerialDistanceTo(destination) <= Unit.GetAirUnitMovementRange();
        if (Unit.IsPreparingParadrop())
            return CanParadropOn(destination, Unit.CurrentTile.AerialDistanceTo(destination));
        return specificFunction(destination);
    }

    /// <param name="includeOtherEscortUnit">
    /// determines whether or not this method will also check its the other escort unit if it has one.
    /// Leave it as default unless you know what <see cref="GetReachableTilesInCurrentTurn"/> does.
    /// </param>
    public IEnumerable<Tile> GetReachableTilesInCurrentTurn(bool includeOtherEscortUnit = true)
    {
        if (Unit.Cache.CannotMove)
            return [Unit.GetTile()];
        if (Unit.BaseUnit.IsAirUnit())
            return Unit.GetTile().GetTilesInDistanceRange(new IntRange(1, Unit.GetAirUnitMovementRange()));
        if (Unit.IsPreparingParadrop())
        {
            return Unit.GetTile().GetTilesInDistance(Unit.Cache.ParadropDestinationTileFilters.Max(it => it.Value))
                .Where(it => Unit.Movement.CanParadropOn(it, it.AerialDistanceTo(Unit.GetTile())));
        }
        if (includeOtherEscortUnit && Unit.IsEscorting())
        {
            var otherUnitTiles = Unit.GetOtherEscortUnit().NotNull().Movement.GetReachableTilesInCurrentTurn(false).ToHashSet();
            return Unit.Movement.GetDistanceToTiles().Keys.Where(it => otherUnitTiles.Contains(it)).ToList();
        }
        return Unit.Movement.GetDistanceToTiles().Keys;
    }

    /// <summary>Returns whether we can perform a swap move to the specified tile</summary>
    public bool CanUnitSwapTo(Tile destination) => CanReachInCurrentTurn(destination) && CanUnitSwapToReachableTile(destination);

    /// <summary>Returns the tiles to which we can perform a swap move</summary>
    public IEnumerable<Tile> GetUnitSwappableTiles() => GetReachableTilesInCurrentTurn().Where(it => CanUnitSwapToReachableTile(it));

    /// <summary>Returns whether we can perform a unit swap move to the specified tile, given that it is reachable in the current turn</summary>
    private bool CanUnitSwapToReachableTile(Tile reachableTile, bool checkEscorted = true)
    {
        // Air units cannot swap
        if (Unit.BaseUnit.IsAirUnit()) return false;
        // We can't swap with ourself
        if (reachableTile == Unit.GetTile()) return false;
        if (Unit.Cache.CannotMove) return false;
        if (!Unit.HasMovement()) return false; // A* incorrectly reports occupied tiles as reachable when movement==0

        // Check whether the tile contains a unit of the same type as us that we own and that can also reach our tile in its current turn.
        // When looking for escort formation swaps, however, the 'other' unit should be taken disregarding this unit's type.
        // (So when we have a escorting Warrior+Worker pair, and want to move that pair to a tile with just a Worker or juse a Warrior,
        // we can get the Swap button no matter which of the pair we selected first)
        var escortedUnit = checkEscorted && Unit.IsEscorting() ? Unit.GetOtherEscortUnit() : null; // Only isEscorting has the actual flag check
        MapUnit? GetMatchInReachableTile(MapUnit mapUnit) =>
            mapUnit.IsCivilian() ? reachableTile.CivilianUnit : reachableTile.MilitaryUnit;
        var otherUnit = GetMatchInReachableTile(Unit)
            ?? (escortedUnit != null ? GetMatchInReachableTile(escortedUnit) : null);
        if (otherUnit == null) return false;
        var ourPosition = Unit.GetTile();
        if (otherUnit.Owner != Unit.Owner
            || otherUnit.Cache.CannotMove // redundant but faster, line below would cover it too
            || !otherUnit.HasMovement() // A* incorrectly reports occupied tiles as reachable when movement==0
            || !otherUnit.Movement.CanReachInCurrentTurn(ourPosition)) return false;

        if (!CanMoveTo(reachableTile, allowSwap: true)) return false;
        if (!otherUnit.Movement.CanMoveTo(ourPosition, allowSwap: true)) return false;

        if (escortedUnit == null) return true; // All clear!

        // Check whether the escorted unit can move OR swap too
        if (escortedUnit.Movement.CanMoveTo(reachableTile, allowSwap: false, includeOtherEscortUnit: false)) return true;
        return escortedUnit.Movement.CanUnitSwapToReachableTile(reachableTile, false);
    }

    /// <summary>
    /// Displace a unit - choose a viable tile close by if possible and 'teleport' the unit there.
    /// This will not use movement points or check for a possible route.
    /// It is used e.g. if an enemy city expands its borders, or trades or diplomacy change a unit's
    /// allowed position. Does not teleport transported units on their own, these are teleported when
    /// the transporting unit is moved.
    /// Does not capture enemy civilians; this is not an attack.
    /// CAN DESTROY THE UNIT.
    /// </summary>
    public virtual void TeleportToClosestMoveableTile()
    {
        Unit.StopEscorting();
        if (Unit.IsTransported) return; // handled when carrying unit is teleported
        Tile? allowedTile = null;
        var distance = 0;
        // When we didn't limit the allowed distance the game would sometimes spend a whole minute looking for a suitable tile.

        if (CanPassThrough(Unit.GetTile())
            && !IsCityCenterCannotEnter(Unit.GetTile()))
            return; // This unit can stay here - e.g. it has "May enter foreign tiles without open borders"

        // Forced displacement is not an attack. [canMoveTo] treats an enemy civilian as enterable so
        // ordinary movement can capture them, but accepting that tile here would capture them as a
        // side effect of borders, trades, or diplomacy, and can leave the victim's old owner
        // automating an instance that now belongs to Barbarians. Another civ's unit can only be
        // passed through, so a destination must be empty or already hold a unit of ours.
        bool IsDisplacementDestination(Tile tile)
        {
            if (!CanMoveTo(tile)) return false;
            var currentCivInTile = tile.GetFirstUnit()?.Civ;
            return currentCivInTile == null || currentCivInTile == Unit.Civ;
        }

        while (allowedTile == null && distance < 5)
        {
            distance++;
            allowedTile = Unit.GetTile().GetTilesAtDistance(distance)
                // can the unit be placed safely there? Is tile either unowned or friendly?
                .Where(it => IsDisplacementDestination(it) && ((CivilizationObject?)it.GetOwner())?.IsAtWarWith(Unit.Civ) != true)
                // out of those where it can be placed, can it reach them in any meaningful way?
                .FirstOrDefault(it => GetPathBetweenTiles(Unit.CurrentTile, it).Contains(it));
        }

        // No tile within 4 spaces? move him to a city.
        var origin = Unit.GetTile();
        if (allowedTile == null)
        {
            allowedTile = Unit.Civ.Cities.SelectMany(it => it.GetTiles())
                .OrderBy(it => it.AerialDistanceTo(origin)).FirstOrDefault(IsDisplacementDestination);
        }

        if (allowedTile != null)
        {
            Unit.RemoveFromTile(); // we "teleport" them away
            Unit.PutInTile(allowedTile);
            GameEventHooks.UnitMoved(Unit, origin, allowedTile);
            // Cancel sleep or fortification if forcibly displaced - for now, leave movement / auto / explore orders
            if (Unit.IsSleeping() || Unit.IsFortified() || Unit.IsGuarding())
                Unit.Action = null;
            Unit.MostRecentMoveType = UnitMovementMemoryType.UnitTeleported;

            TeleportTransportedUnitsTo(origin, allowedTile);
        }
        // it's possible that there is no close tile, and all the guy's cities are full.
        // Nothing we can do.
        else Unit.Destroy();
    }

    /// <summary>
    /// Moves the units <see cref="Unit"/> is carrying from <paramref name="origin"/> to <paramref name="destination"/>, keeping them transported.
    /// Deliberately not <see cref="MapUnit.CanTransport"/>: that rejects a unit once the carrier is at capacity,
    /// which is true of every payload already aboard a full carrier. These are not new passengers.
    /// </summary>
    public void TeleportTransportedUnitsTo(Tile origin, Tile destination)
    {
        var payloadUnits = origin.GetUnits()
            .Where(it => it.IsTransported && it.Owner == Unit.Owner && Unit.IsTransportTypeOf(it))
            .ToList();
        foreach (var payload in payloadUnits)
        {
            payload.RemoveFromTile();
            payload.PutInTile(destination);
            GameEventHooks.UnitMoved(payload, origin, destination);
            payload.IsTransported = true; // restore the flag to not leave the payload in the city
            payload.MostRecentMoveType = UnitMovementMemoryType.UnitTeleported;
        }
    }

    public virtual void MoveToTile(Tile destination, bool considerZoneOfControl = true)
    {
        if (destination == Unit.GetTile() || Unit.IsDestroyed) return; // already here (or dead)!
        // Reset closestEnemy chache
        var escortUnit = Unit.IsEscorting() ? Unit.GetOtherEscortUnit().NotNull() : null;

        if (Unit.BaseUnit.IsAirUnit()) // air units move differently from all other units
        {
            var airOrigin = Unit.GetTile();
            MoveAirUnitToTile(destination);
            GameEventHooks.UnitMoved(Unit, airOrigin, Unit.GetTile());
            return;
        }

        if (Unit.IsPreparingParadrop()) // paradropping units move differently
        {
            var dropOrigin = Unit.GetTile();
            ParadropToTile(destination);
            GameEventHooks.UnitMoved(Unit, dropOrigin, Unit.GetTile());
            return;
        }

        var distanceToTiles = GetDistanceToTiles(considerZoneOfControl);
        var pathToDestination = distanceToTiles.GetPathToTile(destination);
        var movableTiles = pathToDestination.TakeWhile(it => CanPassThrough(it)).ToList();
        var lastReachableTile = movableTiles.LastOrDefault(it => CanMoveTo(it));
        if (lastReachableTile == null) return; // no tiles can pass though/can move to
        Unit.MostRecentMoveType = UnitMovementMemoryType.UnitMoved;
        var pathToLastReachableTile = distanceToTiles.GetPathToTile(lastReachableTile);

        if (Unit.IsFortified() || Unit.IsGuarding() || Unit.IsSetUpForSiege() || Unit.IsSleeping())
            Unit.Action = null; // un-fortify/un-setup/un-sleep after moving

        // If this unit is a carrier, keep record of its air payload whereabouts.
        var origin = Unit.GetTile();
        var needToFindNewRoute = false;
        // Cache this in case something goes wrong

        var lastReachedEnterableTile = Unit.GetTile();
        var previousTile = Unit.GetTile();
        List<Tile>? enteredTiles = Unit.Civ.GameInfo.EventSink != null ? [origin] : null; // T186: the path for the UnitMoved event
        var passingMovementSpent = 0f; // Movement points spent since last tile we could end our turn on

        foreach (var tile in pathToLastReachableTile)
        {
            if (!Unit.Movement.CanPassThrough(tile))
            {
                // AAAH something happened making our previous path invalid
                // Maybe we spawned a unit using ancient ruins, or our old route went through
                // fog of war, and we found an obstacle halfway?
                // Anyway: PANIC!! We stop this route, and after leaving the game in a valid state,
                // we try again.
                needToFindNewRoute = true;
                break; // If you ever remove this break, remove the `assumeCanPassThrough` param below
            }

            // This fixes a bug where tiles in the fog of war would always only cost 1 mp
            if (!Unit.Civ.GameInfo.GameParameters.GodMode)
                passingMovementSpent += MovementCost.GetMovementCostBetweenAdjacentTiles(Unit, previousTile, tile);

            // In case something goes wrong, cache the last tile we were able to end on
            // We can assume we can pass through this tile, as we would have broken earlier
            if (Unit.Movement.CanMoveTo(tile, assumeCanPassThrough: true))
            {
                lastReachedEnterableTile = tile;
                Unit.UseMovementPoints(passingMovementSpent);
                Unit.RemoveFromTile();
                Unit.PutInTile(tile); // Required for ruins,
                enteredTiles?.Add(tile);
                TradeRouteTurns.TryPlunder(Unit, tile); // B020: BNW trade routes; a no-op without routes

                if (escortUnit != null)
                {
                    escortUnit.Movement.MoveToTile(tile);
                    Unit.StartEscorting(); // Need to re-apply this
                }

                passingMovementSpent = 0f;
            }

            previousTile = tile;

            // We can't continue, stop here.
            if (Unit.IsDestroyed || Unit.CurrentMovement - passingMovementSpent < Constants.MinimumMovementEpsilon)
                break;
        }

        var finalTileReached = lastReachedEnterableTile;
        if (enteredTiles != null) GameEventHooks.UnitMoved(Unit, enteredTiles);

        // Silly floats which are almost zero
        if (Unit.CurrentMovement < Constants.MinimumMovementEpsilon)
            Unit.CurrentMovement = 0f;

        BringAlongPayloads(origin, pathToLastReachableTile, finalTileReached);

        // Unit maintenance changed
        if (Unit.CanGarrison()
            && (origin.IsCityCenter() || finalTileReached.IsCityCenter())
            && Unit.Civ.HasUnique(UniqueType.UnitsInCitiesNoMaintenance)
        ) Unit.Civ.UpdateStatsForNextTurn();

        // Under rare cases (see #8044), we can be headed to a tile and *the entire path* is blocked by other units, so we can't "enter" that tile.
        // If, in such conditions, the *destination tile* is unenterable, needToFindNewRoute will trigger, so we need to catch this situation to avoid infinite loop
        if (needToFindNewRoute && Unit.CurrentTile != origin)
            MoveToTile(destination, considerZoneOfControl);

        if (Unit.CurrentTile != origin)
        {
            ClearPathfindingCache();
            Unit.GetOtherEscortUnit()?.Movement.ClearPathfindingCache();
        }
        Unit.UpdateUniques();
    }

    private void BringAlongPayloads(Tile origin, List<Tile> pathToLastReachableTile, Tile finalTileReached)
    {
        // The .toList() here is because we have a sequence that's running on the units in the tile,
        // then if we move one of the units we'll get a ConcurrentModificationException, se we save them all to a list
        var payloadUnits = origin.GetUnits().Where(it => it.IsTransported && Unit.CanTransport(it)).ToList();
        // bring along the payloads
        foreach (var payload in payloadUnits)
        {
            payload.RemoveFromTile();
            foreach (var tile in pathToLastReachableTile)
            {
                payload.MoveThroughTile(tile);
                if (tile == finalTileReached) break; // this is the final tile the transport reached
            }
            payload.PutInTile(finalTileReached);
            GameEventHooks.UnitMoved(payload, origin, finalTileReached);
            payload.IsTransported = true; // restore the flag to not leave the payload in the city
            payload.MostRecentMoveType = UnitMovementMemoryType.UnitMoved;
        }
    }

    private void MoveAirUnitToTile(Tile destination)
    {
        if (Unit.Action != UnitActionType.Automate.Value()) Unit.Action = null;
        Unit.RemoveFromTile();
        Unit.IsTransported = false; // it has left the carrier by own means
        Unit.PutInTile(destination);
        Unit.CurrentMovement = 0f;
        Unit.MostRecentMoveType = UnitMovementMemoryType.UnitTeleported;
        ClearPathfindingCache();
    }

    private void ParadropToTile(Tile destination)
    {
        var paradropOrigin = Unit.GetTile();
        Unit.Action = null;
        Unit.RemoveFromTile();
        Unit.PutInTile(destination);
        Unit.MostRecentMoveType = UnitMovementMemoryType.UnitTeleported;

        TeleportTransportedUnitsTo(paradropOrigin, destination);

        Unit.UseMovementPoints(1f);
        Unit.AttacksThisTurn += 1;
        // Check if unit maintenance changed
        // Is also done for other units, but because we skip everything else, we have to manually check it
        // The reason we skip everything, is that otherwise `getPathToTile()` throws an exception
        // As we can not reach our destination in a single turn
        if (Unit.CanGarrison()
            && (Unit.GetTile().IsCityCenter() || destination.IsCityCenter())
            && Unit.Civ.HasUnique(UniqueType.UnitsInCitiesNoMaintenance)
        ) Unit.Civ.UpdateStatsForNextTurn();
        ClearPathfindingCache();
    }

    /// <summary>
    /// Swaps this unit with the unit on the given tile
    /// Precondition: this unit can swap-move to the given tile, as determined by canUnitSwapTo
    /// </summary>
    public void SwapMoveToTile(Tile destination, bool keepEscorting = false)
    {
        if (keepEscorting && Unit.IsEscorting())
        {
            SwapMoveEscortPair(destination);
            return;
        }

        Unit.StopEscorting();
        var otherUnit = Unit.IsCivilian() ? destination.CivilianUnit : destination.MilitaryUnit;
        if (otherUnit == null) return; // The precondition guarantees that there is an eligible same-type unit at the destination
        otherUnit.StopEscorting();
        var ourOldPosition = Unit.GetTile();
        var theirOldPosition = otherUnit.GetTile();

        var ourPayload = ourOldPosition.GetUnits().Where(it => it.IsTransported && Unit.IsTransportTypeOf(it)).ToList();
        var theirPayload = theirOldPosition.GetUnits().Where(it => it.IsTransported && otherUnit.IsTransportTypeOf(it)).ToList();

        // Swap the units
        // Step 1: Release the destination tile
        otherUnit.RemoveFromTile();
        foreach (var payload in theirPayload)
            payload.RemoveFromTile();
        // Step 2: Perform the movement
        Unit.Movement.MoveToTile(destination);
        // Step 3: Release the newly taken tile
        Unit.RemoveFromTile();
        foreach (var payload in ourPayload)
            payload.RemoveFromTile();
        // Step 4: Restore the initial position after step 1
        otherUnit.PutInTile(theirOldPosition);
        foreach (var payload in theirPayload)
        {
            payload.PutInTile(theirOldPosition);
            payload.IsTransported = true; // restore the flag to not leave the payload in the city
        }
        // Step 5: Perform the another movement
        otherUnit.Movement.MoveToTile(ourOldPosition);
        // Step 6: Restore the position in the new tile after step 3
        Unit.PutInTile(theirOldPosition);
        foreach (var payload in ourPayload)
        {
            payload.PutInTile(theirOldPosition);
            payload.IsTransported = true; // restore the flag to not leave the payload in the city
        }
        // Step 6: Update states
        otherUnit.MostRecentMoveType = UnitMovementMemoryType.UnitMoved;
        Unit.MostRecentMoveType = UnitMovementMemoryType.UnitMoved;
        ClearPathfindingCache();
        Unit.GetOtherEscortUnit()?.Movement.ClearPathfindingCache();
    }

    private void SwapMoveEscortPair(Tile destination)
    {
        var escortedUnit = Unit.GetOtherEscortUnit();
        if (escortedUnit == null) return;
        var destinationWasEscortingUnit = destination.MilitaryUnit is { } militaryUnit && militaryUnit.IsEscorting() ? militaryUnit : null;
        Unit.StopEscorting(); // If a moveToTile comes first - it would try to keep the escort and fail

        bool NeedsSwapTo(MapUnit mapUnit) => (mapUnit.IsCivilian() ? destination.CivilianUnit : destination.MilitaryUnit) != null;
        void SwapOrMoveToDestination(MapUnit mapUnit)
        {
            if (NeedsSwapTo(mapUnit)) mapUnit.Movement.SwapMoveToTile(destination);
            else mapUnit.Movement.MoveToTile(destination);
        }

        SwapOrMoveToDestination(Unit);
        SwapOrMoveToDestination(escortedUnit);
        Unit.StartEscorting();
        destinationWasEscortingUnit?.StartEscorting();
    }

    private bool IsCityCenterCannotEnter(Tile tile) => tile.IsCityCenter()
        && tile.GetOwner() != Unit.Civ
        && !((CityObject)tile.GetCity().NotNull()).HasJustBeenConquered;

    /// <summary>
    /// Designates whether we can enter the tile - without attacking
    /// DOES NOT designate whether we can reach that tile in the current turn
    /// </summary>
    /// <param name="tile">The tile to enter.</param>
    /// <param name="assumeCanPassThrough">Skip the pass-through check (the caller has done it).</param>
    /// <param name="allowSwap">Treat own units of the same kind as movable away (swap moves).</param>
    /// <param name="includeOtherEscortUnit">
    /// determines whether or not this method will also check if the other escort unit <see cref="CanMoveTo"/> if it has one.
    /// Leave it as default unless you know what <see cref="CanMoveTo"/> does.
    /// </param>
    public virtual bool CanMoveTo(Tile tile, bool assumeCanPassThrough = false, bool allowSwap = false, bool includeOtherEscortUnit = true) =>
        GetCannotMoveToReason(tile, assumeCanPassThrough, allowSwap, includeOtherEscortUnit) == null;

    public enum CannotMoveToReason
    {
        TerrainImpassable,
        BoatCannotGoOnLand,
        CannotEmbark,
        CannotEnterOcean,
        CannotEnterForeignLand,
        CannotEnterCityCenter,
        EscortCannotMove,
        TileIsNotEmpty,
        NoAirUnitTransport,
    }

    public virtual CannotMoveToReason? GetCannotMoveToReason(Tile tile, bool assumeCanPassThrough = false, bool allowSwap = false, bool includeOtherEscortUnit = true)
    {
        if (Unit.BaseUnit.IsAirUnit())
            return GetAirUnitCannotMoveToReason(tile, Unit);

        var canPassThroughReason = assumeCanPassThrough ? null : CannotPassThroughReason(tile);
        if (canPassThroughReason != null) return canPassThroughReason;

        // even if they'll let us pass through, we can't enter their city - unless we just captured it
        if (IsCityCenterCannotEnter(tile))
            return CannotMoveToReason.CannotEnterCityCenter;

        if (includeOtherEscortUnit && Unit.IsEscorting()
            && !Unit.GetOtherEscortUnit().NotNull().Movement.CanMoveTo(tile, assumeCanPassThrough, allowSwap, includeOtherEscortUnit: false))
            return CannotMoveToReason.EscortCannotMove;

        bool tileIsEmpty;
        if (Unit.IsCivilian())
        {
            tileIsEmpty = (tile.CivilianUnit == null || (allowSwap && tile.CivilianUnit.Owner == Unit.Owner))
                && (tile.MilitaryUnit == null || tile.MilitaryUnit.Owner == Unit.Owner);
        }
        else
        {
            // can skip checking for airUnit since not a city
            tileIsEmpty = (tile.MilitaryUnit == null || (allowSwap && tile.MilitaryUnit.Owner == Unit.Owner))
                && (tile.CivilianUnit == null || tile.CivilianUnit.Owner == Unit.Owner || Unit.Civ.IsAtWarWith(tile.CivilianUnit.Civ));
        }

        if (!tileIsEmpty) return CannotMoveToReason.TileIsNotEmpty;

        return null;
    }

    private static CannotMoveToReason? GetAirUnitCannotMoveToReason(Tile tile, MapUnit unit)
    {
        // landing in the city
        if (tile.IsCityCenter())
        {
            var city = (CityObject)tile.GetCity().NotNull();
            if (tile.AirUnits.Count(it => !it.IsTransported) < city.GetMaxAirUnits() && city.Civ == unit.Civ)
                return null; // if city is free - no problem, get in
        } // let's check whether it enters city on carrier now...

        if (tile.MilitaryUnit != null)
        {
            var unitAtDestination = tile.MilitaryUnit;
            if (unitAtDestination.CanTransport(unit)) return null;
        }
        return CannotMoveToReason.NoAirUnitTransport;
    }

    // Can a paratrooper land at this tile?
    private bool CanParadropOn(Tile destination, int distance)
    {
        if (Unit.Cache.CannotMove) return false;

        // Can only move to tiles within range that are visible and not impassible
        // Based on some testing done in the base game
        if (destination.IsImpassible() || !Unit.Civ.ViewableTiles.Contains(destination)) return false;

        // The destination is valid if any of the `tileFilters` match, and is within range
        foreach (var (tileFilter, distanceAllowed) in Unit.Cache.ParadropDestinationTileFilters)
        {
            if (distance <= distanceAllowed && destination.MatchesFilter(tileFilter, Unit.Civ)) return true;
        }

        return false;
    }

    /// <returns>
    /// whether this unit can pass through <paramref name="tile"/>.
    /// Note that sometimes, a tile can be passed through but not entered. Use <see cref="CanMoveTo"/> to determine whether a unit can enter a tile.
    /// This is the most called function in the entire game, so multiple callees of this function have been optimized,
    /// because optimization on this function results in massive benefits!
    /// </returns>
    /// <param name="tile">The tile to test.</param>
    /// <param name="includeOtherEscortUnit">
    /// determines whether or not this method will also check if the other escort unit <see cref="CanPassThrough"/> if it has one.
    /// Leave it as default unless you know what <see cref="CanPassThrough"/> does.
    /// </param>
    public virtual bool CanPassThrough(Tile tile, bool includeOtherEscortUnit = true) => CannotPassThroughReason(tile, includeOtherEscortUnit) == null;

    public CannotMoveToReason? CannotPassThroughReason(Tile tile, bool includeOtherEscortUnit = true)
    {
        if (tile.IsImpassible())
        {
            // special exception - ice tiles are technically impassible, but some units can move through them anyway
            // helicopters can pass through impassable tiles like mountains
            if (!Unit.Cache.CanPassThroughImpassableTiles && !(Unit.Cache.CanEnterIceTiles && tile.TerrainFeatureObjects.Any(it => it.IsIce))
                // carthage-like uniques sometimes allow passage through impassible tiles
                && !(Unit.Civ.PassThroughImpassableUnlocked && Unit.Civ.PassableImpassables.Contains(tile.LastTerrain.Name)))
                return CannotMoveToReason.TerrainImpassable;
        }
        if (tile.IsLand
            && Unit.BaseUnit.IsWaterUnit
            && !tile.IsCityCenter())
            return CannotMoveToReason.BoatCannotGoOnLand;

        var unitSpecificAllowOcean = new KotlinLazy<bool>(() =>
            Unit.Civ.Tech.SpecificUnitsCanEnterOcean &&
            Unit.Civ.GetMatchingUniques(UniqueType.UnitsMayEnterOcean)
                .Any(it => Unit.MatchesFilter(it.Params[0])));
        if (tile.IsWater && Unit.BaseUnit.IsLandUnit && !Unit.Cache.CanMoveOnWater)
        {
            if (!Unit.Civ.Tech.UnitsCanEmbark) return CannotMoveToReason.CannotEmbark;
            if (Unit.Cache.CannotEmbark) return CannotMoveToReason.CannotEmbark;
            if (tile.IsOcean && !Unit.Civ.Tech.EmbarkedUnitsCanEnterOcean && !unitSpecificAllowOcean.Value)
                return CannotMoveToReason.CannotEnterOcean;
        }
        if (tile.IsOcean && !Unit.Civ.Tech.AllUnitsCanEnterOcean) // Apparently all Polynesian naval units can enter oceans
        {
            if (!unitSpecificAllowOcean.Value && Unit.Cache.CannotEnterOceanTiles) return CannotMoveToReason.CannotEnterOcean;
        }

        if (Unit.Cache.CanEnterCityStates && ((CivilizationObject?)tile.GetOwner())?.IsCityState == true)
            return null;
        if (!Unit.Cache.CanEnterForeignTerrain && !tile.CanCivPassThrough(Unit.Civ)) return CannotMoveToReason.CannotEnterForeignLand;

        // The first unit is:
        //   1. Either military unit
        //   2. or unprotected civilian
        //   3. or unprotected air unit while no civilians on tile
        var firstUnit = tile.GetFirstUnit();
        // Moving to non-empty tile
        if (firstUnit != null && Unit.Civ != firstUnit.Civ)
        {
            // Allow movement through unguarded, at-war Civilian Unit. Capture on the way
            // But not for Embarked Units capturing on Water
            if (!(Unit.BaseUnit.IsLandUnit && tile.IsWater && !Unit.Cache.CanMoveOnWater)
                && firstUnit.IsCivilian() && Unit.Civ.IsAtWarWith(firstUnit.Civ))
                return null;
            // Cannot enter hostile tile with any unit in there
            if (Unit.Civ.IsAtWarWith(firstUnit.Civ))
                return CannotMoveToReason.TileIsNotEmpty;
        }
        if (includeOtherEscortUnit && Unit.IsEscorting())
        {
            var escortReason = Unit.GetOtherEscortUnit().NotNull().Movement.CannotPassThroughReason(tile, false);
            if (escortReason != null) return escortReason;
        }
        return null;
    }

    /// <param name="considerZoneOfControl">Whether zone of control makes movement cost all movement points.</param>
    /// <param name="passThroughCacheNew">Cache of <see cref="CanPassThrough"/> per tile index (shared across calls of one path search).</param>
    /// <param name="movementCostCache">Cache of the movement cost per tile pair (shared across calls of one path search).</param>
    /// <param name="includeOtherEscortUnit">
    /// determines whether or not this method will also check if the other escort units <see cref="GetDistanceToTiles"/> if it has one.
    /// Leave it as default unless you know what <see cref="GetDistanceToTiles"/> does.
    /// </param>
    public virtual PathsToTilesWithinTurn GetDistanceToTiles(
        bool considerZoneOfControl = true,
        List<bool?>? passThroughCacheNew = null,
        Dictionary<int, float>? movementCostCache = null,
        bool includeOtherEscortUnit = true)
    {
        if (PathingMaps != null)
            return SelectPathingMap(considerZoneOfControl, includeOtherEscortUnit).GetMovementToTilesAtPosition();
        return GetMovementToTilesAtPosition(
            Unit.CurrentTile.Position,
            Unit.CurrentMovement,
            considerZoneOfControl,
            null,
            passThroughCacheNew ?? new List<bool?>(Unit.GetTile().TileMap.TileList.Count),
            movementCostCache ?? [],
            includeOtherEscortUnit);
    }

    /// <summary>Get a road path for the "Connect road" unit action. A valid path must include the current tile.</summary>
    public List<Tile>? GetRoadPath(Tile destinationTile)
    {
        if (PathingMaps == null) return MapPathing.GetRoadPath(Unit.Civ, Unit.GetTile(), destinationTile);
        var path = roadPathing.Value.GetShortestPath(destinationTile);
        return path == null ? null : [Unit.CurrentTile, .. path];
    }

    /// <returns>
    /// the set of <see cref="Tile"/> between <paramref name="from"/> and <paramref name="to"/> tiles.
    /// It takes into account the terrain and units possibilities of entering the terrain,
    /// however ignores the diplomatic aspects of such movement like crossing closed borders.
    /// </returns>
    private IReadOnlyCollection<Tile> GetPathBetweenTiles(Tile from, Tile to)
    {
        var tmp = Unit.Cache.CanEnterForeignTerrain;
        Unit.Cache.CanEnterForeignTerrain = true; // the trick to ignore tiles owners
        var bfs = new BFS(from, it => CanPassThrough(it));
        bfs.StepUntilDestination(to);
        Unit.Cache.CanEnterForeignTerrain = tmp;
        return bfs.GetReachedTiles();
    }

    public virtual void ClearPathfindingCache()
    {
        pathfindingCache.Clear();
        if (PathingMaps == null) return; // Kotlin builds and clears the (unused) maps here; they have no other effect
        aStarPathing.Value.Clear();
        aStarPathingWithoutZoneControl.Value.Clear();
        aStarPathingWithoutEscort.Value.Clear();
        roadPathing.Value.Clear();
    }
}

/// <summary>
/// Cache for the results of <see cref="UnitMovement.GetDistanceToTiles"/> accounting for zone of control.
/// It is called in numerous places for AI pathfinding so being able to skip redundant calculations helps out over a long game
/// (especially with high level AI or a big map). Same thing with <see cref="UnitMovement.GetShortestPath"/> which is called in
/// <see cref="UnitMovement.CanReach"/> and in <see cref="UnitMovement.HeadTowards"/>. Often, the AI will
/// see if it can reach a tile using canReach then if it can, it will headTowards it. We can cache
/// the result since otherwise this is a redundant calculation that will find the same path.
/// </summary>
public class PathfindingCache(MapUnit unit)
{
    private List<Tile> shortestPathCache = [];
    private Tile? destination;
    private float movement = -1f;
    private Tile? currentTile;

    /// <summary>
    /// Check if the caches are valid (only checking if the unit has moved or consumed movement points;
    /// the isPlayerCivilization check is performed in the functions because we want isValid() == false to have a specific behavior)
    /// </summary>
    private bool IsValid() => movement == unit.CurrentMovement && unit.GetTile() == currentTile;

    public List<Tile> GetShortestPathCache(Tile destination)
    {
        if (unit.Civ.IsHuman()) return [];
        if (IsValid() && this.destination == destination)
            return shortestPathCache;
        return [];
    }

    public void SetShortestPathCache(Tile destination, List<Tile> newShortestPath)
    {
        if (unit.Civ.IsHuman()) return;
        if (IsValid())
        {
            shortestPathCache = newShortestPath;
            this.destination = destination;
        }
    }

    public void Clear()
    {
        movement = unit.CurrentMovement;
        currentTile = unit.GetTile();
        destination = null;
        shortestPathCache = [];
    }
}
