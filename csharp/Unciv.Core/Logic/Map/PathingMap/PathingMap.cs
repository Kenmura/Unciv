using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Logic.Map.MapUnits;
using Unciv.Logic.Map.Tiles;
using CivilizationClass = Unciv.Logic.Civilization.Civilization;
using MapUnitClass = Unciv.Logic.Map.MapUnits.MapUnit;

namespace Unciv.Logic.Map.PathingMap;

// Ported from core/src/com/unciv/logic/map/pathingmap/PathingMap.kt (T093). Stripped: the purity annotations, the timeThis profiling (T027 policy),
// and the VERBOSE_PATHFINDING_LOGS debug logging (the flag constants stay: PathingMapTest asserts the flag is off).
// - AtomicReference / compareAndSet / synchronized are plain fields: the engine is single-threaded, so the data-race retry in fetchCache cannot happen.
// - Kotlin fun interfaces (TilePredicate, EndTurnDamageLookup, TileMovementCost, TileRoadCost, EndSearchPredicate) are delegates.
// - Kotlin `(Tile) -> RelationshipLevel` is Func.
// - The Kotlin default `getShortestPath(destination, maxTurns = MAX_VALID_TURNS)` is an optional parameter; IUnitPathingMap sees the one-argument form.

public delegate bool TilePredicate(Tile it);
public delegate int EndTurnDamageLookup(Tile it);
public delegate FixedPointMovement TileMovementCost(Tile from, Tile to);
public delegate FixedPointMovement TileRoadCost(Tile it);
public delegate bool EndSearchPredicate(Tile tile, RouteNode node);

/// <summary>
/// <c>PathingMap</c> is a class that coordinates the pathing caches and calculations.
///
/// For the most part, all this class really does is manage the <see cref="PathingMapCache"/> automatically.
/// When pathing is requested, it builds an A* pathfinder, which does the actual pathfinding, and
/// then the <c>PathingMap</c> merges the results back into its <see cref="PathingMapCache"/>.
///
/// To calculate with different moveThroughPredicate, cost, or other conditions would invalidate the
/// cached "Node"s, so require a separate <c>PathingMap</c>.
///
/// The A* algorithm is used here because each tile is calculated exactly once, and the
/// resulting "Node" data can be reused for all subsequent pathfinding, even when pathfinding to
/// different targets. This introduces a *very* minor bias in later paths toward earlier paths, but
/// only among routes of otherwise equal priority.
/// </summary>
public sealed class PathingMap(
    TileMap tileMap,
    object debugId,
    string debugMapType,
    Func<PathingMapCacheKey> getCurrentCacheKey,
    TilePredicate passThroughPredicate,
    TilePredicate moveToPredicate,
    EndTurnDamageLookup endTurnDamage,
    TileMovementCost cost,
    TileRoadCost tileRoadCost,
    Func<Tile, RelationshipLevel> relationshipLevel) : IUnitPathingMap
{
    public const int MaxValidTurns = RouteNode.MaxValidTurns;

    internal static readonly HexCoord AlwaysLog = new(0xFFFF, 0xFFFE);
    public static readonly HexCoord NeverLog = new(0xFFFF, 0xFFFF);
    /// <summary>You can temporarily set this to a tile position, e.g. a unit's, or to <see cref="AlwaysLog"/>, to enable verbose logging in Kotlin; the logging is not ported.</summary>
    public static readonly HexCoord VerbosePathfindingLogs = NeverLog;

    private PathingMapCache? cache;

    public void Clear() => cache = null;

    private PathingMapCache FetchCache()
    {
        var latestKey = getCurrentCacheKey();
        var oldCache = cache;
        if (oldCache?.IsCacheValid(latestKey) == true)
            return oldCache; // if the cache is still valid, keep using it
        // if the cache is invalid, it is dumped by the assignment below
        var newCache = new PathingMapCache(latestKey, tileMap); // otherwise, make a new cache
        var movementUsedThisTurn = (latestKey.FullMove - latestKey.MoveRemaining).CoerceIn(FixedPointMovement.FpmZero, RouteNode.MaxMoveThisTurn);
        var tile = tileMap[latestKey.StartingPoint];
        var root = RouteNode.RootNode(tile, movementUsedThisTurn);
        newCache.RouteNodes[tile.ZeroBasedIndex] = root.Bits;
        newCache.NodesNeedingNeighbors.Set(tile.ZeroBasedIndex);
        cache = newCache;
        return newCache;
    }

    List<Tile>? IUnitPathingMap.GetShortestPath(Tile destination) => GetShortestPath(destination);

    /// <summary>
    /// Constructs a list representing the path from the starting point to the given destination tile (the start itself is not included).
    /// If the destination is the starting point, the list contains just it; if there is no movement left, it is empty;
    /// <c>null</c> when the destination is not reachable within <paramref name="maxTurns"/>.
    ///
    /// WARNING: If the path being analyzed exceeds maxTurns, then this returns a path of length
    /// maxTurns towards the destination, but the path will not actually reach the destination.
    /// </summary>
    public List<Tile>? GetShortestPath(Tile destination, int maxTurns = MaxValidTurns)
    {
        var fetched = FetchCache();
        if (destination.Position == fetched.Key.StartingPoint)
            return [destination];
        if (fetched.Key.MoveRemaining <= 0)
            return []; // emulating no-movement-pathing bug
        // if we don't already know the shortest path, and might yet find it, search
        var targetNode = new RouteNode(fetched.RouteNodes[destination.ZeroBasedIndex]);
        if (!targetNode.Initialized && !fetched.NodesNeedingNeighbors.IsEmpty)
            AStarStepUntilDestination(fetched, destination, maxTurns);
        var bestTarget = new RouteNode(fetched.RouteNodes[destination.ZeroBasedIndex]);
        // if the target is not reachable within maxTurns, return nothing
        if (!bestTarget.Initialized || bestTarget.IsNoPathingNode)
            return null;
        return bestTarget.DamagingTiles == 0 ? PathAsListNoDamage(bestTarget, fetched) : PathAsListWithDamage(destination, fetched);
    }

    private List<Tile> PathAsListNoDamage(RouteNode targetNode, PathingMapCache fetched)
    {
        // Now routeNodes has the shortest route, so we extract it into a list and return
        var currentNode = targetNode;
        var result = new List<Tile> { currentNode.GetTile(tileMap) };
        var turns = currentNode.Turns;
        while (true)
        {
            var parentTile = currentNode.ParentTile(tileMap);
            var parentNode = new RouteNode(fetched.RouteNodes[parentTile.ZeroBasedIndex]);
            if (parentTile.Position == fetched.Key.StartingPoint) break;
            if (parentNode.Turns < turns && parentNode.EndTurnWithoutMoreDamage)
            {
                result.Add(parentTile);
                turns = parentNode.Turns;
            }
            currentNode = parentNode;
        }
        result.Reverse();
        return result;
    }

    /// <summary>
    /// Reconstructs the turn-by-turn waypoint list for the already-found route ending at <paramref name="targetTile"/>,
    /// for the case where the route DOES take damage somewhere.
    ///
    /// The cached RouteNode chain's own turns/moveUsedThisTurn bookkeeping can be slightly ahead of
    /// its own physical parentTile chain -- e.g. a damage-free retroactive pause anchors moveUsedThisTurn
    /// and turns to an *earlier* tile than the immediate physical predecessor, so comparing those
    /// fields between physically-adjacent nodes doesn't reliably reproduce per-hop cost or turn
    /// boundaries. So instead we rewalk the (already known, physically correct) tile chain from
    /// scratch, recomputing per-hop cost/damage directly -- cheap, since it's only O(path length),
    /// not O(search).
    /// </summary>
    private List<Tile> PathAsListWithDamage(Tile targetTile, PathingMapCache fetched)
    {
        // get the whole list of tiles in the selected path, and reverse it
        var pathTileIndices = new List<int>(16);
        var walkTile = targetTile;
        while (walkTile.Position != fetched.Key.StartingPoint)
        {
            pathTileIndices.Add(walkTile.ZeroBasedIndex);
            var node = new RouteNode(fetched.RouteNodes[walkTile.ZeroBasedIndex]);
            walkTile = node.ParentTile(tileMap);
        }
        pathTileIndices.Reverse();

        // now recalculate the optimal stop positions
        var startTile = tileMap[fetched.Key.StartingPoint];
        var fpmFullMovement = fetched.Key.FullMove;
        var result = new List<Tile>();
        void AddWaypoint(Tile tile)
        {
            if (tile != startTile && (result.Count == 0 || result[^1] != tile)) result.Add(tile);
        }
        var moveThisTurn = FixedPointMovement.FpmZero;
        var previousTile = startTile;
        var previousNode = new RouteNode(fetched.RouteNodes[startTile.ZeroBasedIndex]);
        var lastFullSafeTile = startTile;
        var moveSinceFullSafe = FixedPointMovement.FpmZero;
        var lastStoppableTile = startTile;
        var moveSinceStoppable = FixedPointMovement.FpmZero;
        foreach (var tileIndex in pathTileIndices)
        {
            var nextTile = tileMap.TileList[tileIndex];
            var nextNode = new RouteNode(fetched.RouteNodes[nextTile.ZeroBasedIndex]);
            var moveCost = !nextNode.EndTurnWithoutMoreDamage ? cost(previousTile, nextTile)
                : nextNode.Turns == previousNode.Turns ? nextNode.MoveUsedThisTurn - previousNode.MoveUsedThisTurn
                : nextNode.MoveUsedThisTurn;

            if (previousNode.EndTurnWithoutMoreDamage && !nextNode.EndTurnWithoutMoreDamage)
            {
                lastFullSafeTile = previousTile;
                moveSinceFullSafe = FixedPointMovement.FpmZero;
            }
            if (previousNode.CanStopOn && !nextNode.CanStopOn)
            {
                lastStoppableTile = previousTile;
                moveSinceStoppable = FixedPointMovement.FpmZero;
            }
            moveSinceFullSafe += moveCost;
            moveSinceStoppable += moveCost;

            moveThisTurn += moveCost;
            if (moveThisTurn > fpmFullMovement) // choose best place to end the turn
            {
                Tile endTurnTile;
                if (moveSinceFullSafe < moveThisTurn) // end turn on last tile that was non-damaging
                {
                    endTurnTile = lastFullSafeTile;
                    moveThisTurn = moveSinceFullSafe;
                }
                else if (moveSinceStoppable < moveThisTurn) // end turn on last stoppable tile
                {
                    endTurnTile = lastStoppableTile;
                    moveThisTurn = moveSinceStoppable;
                }
                else // end turn on previous tile
                {
                    endTurnTile = previousTile;
                    moveThisTurn = moveCost;
                }
                AddWaypoint(endTurnTile);
                lastFullSafeTile = endTurnTile;
                lastStoppableTile = endTurnTile;
                moveSinceFullSafe = moveThisTurn;
                moveSinceStoppable = moveThisTurn;
            }
            previousTile = nextTile;
            previousNode = nextNode;
        }
        // The destination is always the final waypoint, whether or not it happened to also be a
        // forced turn-boundary above.
        AddWaypoint(targetTile);
        return result;
    }

    /// <summary>
    /// Gets the tiles the unit could move to with remaining movement this turn.
    /// Does not consider if tiles can actually be entered, use canMoveTo for that.
    /// If a tile can be reached within the turn, but it cannot be passed through, the total distance to it is set to unitMovement
    /// </summary>
    public PathsToTilesWithinTurn GetMovementToTilesAtPosition()
    {
        var fetched = FetchCache();
        var tilesSameTurn = fetched.TilesSameTurn;
        if (tilesSameTurn.Count > 0)
            return tilesSameTurn;
        // if we've already calculated the results, return that
        // otherwise, find all tiles we can reach this turn, plus 1 tile extra, to make sure we
        // include enemies we would otherwise reach this turn.
        if (!fetched.NodesNeedingNeighbors.IsEmpty)
            AStarStepUntilDestination(fetched, null, 1);
        GetTilesSameTurn(fetched);
        return tilesSameTurn;
    }

    /// <summary>
    /// Finds the closest tile that matches a predicate.
    ///
    /// If there are multiple matches with the same movement cost, then this uses
    /// tile-owner-relationship as a tiebreaker, followed by zeroBasedIndex (roughly, distance from
    /// center of map).
    ///
    /// The results of the method, and the per-tile-predicate-results are NOT cached, and so this has
    /// to check all tiles starting from the origin every call, even if the tiles' nodes have already
    /// been cached from prior pathfinding. However, this uses/generates the same cached nodes as the
    /// other pathfinding (movement cost, relationship level, shortest route, damage numbers, etc),
    /// and so if the nodes were already cached, then this is fast, and if the nodes were not already
    /// cached, then this makes subsequent pathfinding fast.
    ///
    /// IMPORTANT NOTE: This does NOT consider obstacles (hills/mountains) in the attack range.
    /// Callers have to check for obstacles along the path themselves.
    /// </summary>
    public Tile? BfsUntilMatchingTile(EndSearchPredicate endSearchPredicate, int timeLimitTurns = MaxValidTurns) =>
        BfsStepUntilDestination(FetchCache(), endSearchPredicate, timeLimitTurns);

    /// <summary>
    /// Finds all tiles in attack range that match a predicate.
    ///
    /// This does not cache the results of the predicate matching, and so has to check all tiles
    /// starting from the origin, even if the tiles' nodes have already been cached from prior
    /// pathfinding. However, this uses/generates the same cached nodes as the other pathfinding
    /// (movement cost, relationship level, shortest route, damage numbers, etc), so if any other
    /// pathing also occurs before or after, then these checks are effectively free.
    ///
    /// If you do not care about movement cost, then the BFS class is probably faster.
    ///
    /// IMPORTANT NOTE: This does NOT consider sight obstacles (hills/mountains) in the attack range.
    ///
    /// IMPORTANT NOTE: The results of this method are not cached. The *nodes* are cached, but the list of tiles isn't.
    /// </summary>
    public List<Tile> BfsAllMatchingTiles(int timeLimitTurns, EndSearchPredicate tilePredicate)
    {
        var results = new List<Tile>();
        bool SearchPredicate(Tile tile, RouteNode node)
        {
            if (tilePredicate(tile, node)) results.Add(tile);
            return false;
        }
        BfsStepUntilDestination(FetchCache(), SearchPredicate, timeLimitTurns);
        return results;
    }

    public List<Tile> BfsAllMatchingTilesThisTurn(EndSearchPredicate tilePredicate) => BfsAllMatchingTiles(1, tilePredicate);

    private void GetTilesSameTurn(PathingMapCache fetched)
    {
        var tilesSameTurn = fetched.TilesSameTurn;
        // accumulate all the results
        if (tilesSameTurn.Count > 0) return; // if we've already calculated the results, return that
        var initialMovement = fetched.Key.FullMove - fetched.Key.MoveRemaining;
        // otherwise, add the tiles in turn 0, (or would be if unoccupied, and we can swap/attack in)
        void AddIfSameTurn(int index)
        {
            var node = new RouteNode(fetched.RouteNodes[index]);
            if (!node.Initialized || node.Turns != 0) return;
            var tile = node.GetTile(tileMap);
            tilesSameTurn[tile] = new UnitMovement.ParentTileAndTotalMovement(tile, node.ParentTile(tileMap), (node.MoveUsedThisTurn - initialMovement).ToFloat());
        }
        fetched.AddedNeighborNodes.ForEachSetBit(AddIfSameTurn);
        fetched.NodesNeedingNeighbors.ForEachSetBit(AddIfSameTurn);
    }

    /// <summary>The cached node of <paramref name="tile"/>; uninitialized when there is no cache or the tile has not been reached. For tests.</summary>
    public RouteNode GetCachedNode(Tile tile) => new(cache?.RouteNodes[tile.ZeroBasedIndex] ?? 0L);

    /// <summary>Use A* to calculate the route to the destination.</summary>
    private void AStarStepUntilDestination(PathingMapCache fetched, Tile? destination, int timeLimitTurns)
    {
        var finder = new AStarPathfinder(
            destination,
            passThroughPredicate,
            moveToPredicate,
            endTurnDamage,
            cost,
            tileRoadCost,
            relationshipLevel,
            (_, _) => false,
            fetched.ForkForPathfinding(),
            Math.Min(timeLimitTurns, MaxValidTurns),
            tileMap);
        finder.StepUntilDestination();

        // now merge the pathfinder's tilesChecked and tilesToCheck back into the shared PathingData
        fetched.MergePathfindingFork(finder.Cache);
    }

    /// <summary>Use Breadth-First Search algorithm to find a target tile.</summary>
    private Tile? BfsStepUntilDestination(PathingMapCache fetched, EndSearchPredicate endSearchPredicate, int timeLimitTurns)
    {
        var finder = new AStarPathfinder(
            null,
            passThroughPredicate,
            moveToPredicate,
            endTurnDamage,
            cost,
            tileRoadCost,
            relationshipLevel,
            endSearchPredicate,
            fetched.ForkForPathfinding(),
            Math.Min(timeLimitTurns, MaxValidTurns),
            tileMap);
        var result = finder.StepUntilDestination();

        // now merge the pathfinder's tilesChecked and tilesToCheck back into the shared PathingData
        fetched.MergePathfindingFork(finder.Cache);

        return result;
    }

    public override string ToString() => $"PathingMap[debugMapType={debugMapType} debugId={debugId} key={cache?.Key}]";

    public string ToDebugString(Tile? destination = null) => FetchCache().ToDebugString(tileMap, destination);

    public static PathingMap CreateUnitPathingMap(MapUnitClass unit, bool considerZoneOfControl = true, bool includeEscortUnit = true)
    {
        var name = !considerZoneOfControl ? "createUnitPathingMapNoZoc"
            : !includeEscortUnit ? "createUnitPathingMapNoEscort"
            : "createUnitPathingMap";
        // These two precalculated because for some reason they're ridiculously slow
        var selfFullMove = unit.GetMaxMovement();
        var otherUntilFullMove = includeEscortUnit ? unit.GetOtherEscortUnit()?.GetMaxMovement() ?? MaxValidTurns : MaxValidTurns;
        PathingMapCacheKey GetCurrentCacheKey()
        {
            var escort = includeEscortUnit && unit.IsEscorting() ? unit.GetOtherEscortUnit() : null;
            return new PathingMapCacheKey(
                unit.CurrentTile.Position,
                FixedPointMovement.FpmFromMovement(unit.CurrentMovement).CoerceAtMost(
                    escort != null ? FixedPointMovement.FpmFromMovement(escort.CurrentMovement) : RouteNode.MaxMoveThisTurn),
                FixedPointMovement.FpmFromMovement(Math.Min(selfFullMove, escort != null ? otherUntilFullMove : MaxValidTurns)));
        }
        return new PathingMap(
            unit.CurrentTile.TileMap,
            unit,
            name,
            GetCurrentCacheKey,
            it => unit.Movement.CannotPassThroughReason(it, includeEscortUnit) == null,
            it => unit.Movement.CanMoveTo(it, assumeCanPassThrough: true, allowSwap: false, includeOtherEscortUnit: includeEscortUnit),
            it => unit.GetDamageFromTerrain(it),
            (from, to) => FixedPointMovement.FpmFromMovement(
                MovementCost.GetMovementCostBetweenAdjacentTilesEscort(unit, from, to, considerZoneOfControl, includeEscortUnit)),
            it => FixedPointMovement.FpmFromMovement(it.GetConnectionStatus(unit.Civ).GetMovement()),
            tile => RelationshipOfOwner(tile, unit.Civ));
    }

    public static PathingMap CreateLandAttackPathingMap(CivilizationClass civ, Tile startingPoint, CivilizationClass targetCiv) =>
        new(
            civ.GameInfo.TileMap,
            civ,
            "createLandAttackPathingMap",
            () => CivPathExistCacheKey(startingPoint.Position),
            it => IsLandTileCanAttackThrough(civ, it, targetCiv),
            _ => true,
            _ => 0,
            (from, to) => FixedPointMovement.FpmFromMovement(MapPathing.RoadPreferredMovementCost(civ, from, to)),
            it => FixedPointMovement.FpmFromMovement(it.GetConnectionStatus(civ).GetMovement()),
            tile => RelationshipOfOwner(tile, civ));

    public static PathingMap CreateAmphibiousAttackPathingMap(CivilizationClass civ, Tile startingPoint, CivilizationClass targetCiv) =>
        new(
            civ.GameInfo.TileMap,
            civ,
            "createAmphibiousAttackPathingMap",
            () => CivPathExistCacheKey(startingPoint.Position),
            it => IsTileCanAttackThrough(civ, it, targetCiv),
            _ => true,
            _ => 0,
            (from, to) => FixedPointMovement.FpmFromMovement(MapPathing.RoadPreferredMovementCost(civ, from, to)),
            it => FixedPointMovement.FpmFromMovement(it.GetConnectionStatus(civ).GetMovement()),
            tile => RelationshipOfOwner(tile, civ));

    public static PathingMap CreateRoadPathingMap(CivilizationClass civ, Tile startingPoint) =>
        // Use a max movement speed equal to improved (non-rail) road speed, to ensure that the
        // path "ends its turn" on every non-rail tile along the route. Right now, the costs
        // are weird, so this is a full movement of 0.5.  If we used the correct move costs
        // here, then this would be 1/3f.
        new(
            civ.GameInfo.TileMap,
            civ,
            "createRoadPathingMap",
            () => new PathingMapCacheKey(startingPoint.Position, FixedPointMovement.FpmPointFive, FixedPointMovement.FpmPointFive),
            it => MapPathing.IsValidRoadPathTile(civ, it),
            _ => true,
            _ => 0,
            (_, to) => to.HasRoadConnection(civ, false) || to.HasRailroadConnection(false) ? FixedPointMovement.FpmPointFive : FixedPointMovement.FpmOne,
            _ => FixedPointMovement.FpmOne,
            tile => RelationshipOfOwner(tile, civ));

    /// <summary><c>tile.getOwner()?.getDiplomacyManager(civ)?.relationshipIgnoreAfraid() ?: RelationshipLevel.Favorable</c></summary>
    private static RelationshipLevel RelationshipOfOwner(Tile tile, CivilizationClass civ) =>
        ((CivilizationClass?)tile.GetOwner())?.GetDiplomacyManager(civ)?.RelationshipIgnoreAfraid() ?? RelationshipLevel.Favorable;

    private static PathingMapCacheKey CivPathExistCacheKey(HexCoord startingPoint) =>
        new(startingPoint, RouteNode.MaxMoveThisTurn, RouteNode.MaxMoveThisTurn);

    private static bool IsTileCanAttackThrough(CivilizationClass civInfo, Tile tile, CivilizationClass targetCiv)
    {
        var owner = (CivilizationClass?)tile.GetOwner();
        return !tile.IsImpassible()
            && (owner == targetCiv || owner == null || civInfo.DiplomacyFunctions.CanPassThroughTiles(owner));
    }

    private static bool IsLandTileCanAttackThrough(CivilizationClass civInfo, Tile tile, CivilizationClass targetCiv) =>
        tile.IsLand && IsTileCanAttackThrough(civInfo, tile, targetCiv);
}
