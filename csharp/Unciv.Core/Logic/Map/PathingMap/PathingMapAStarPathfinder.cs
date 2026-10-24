using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Logic.Map.Tiles;
using Unciv.Utils;

namespace Unciv.Logic.Map.PathingMap;

// Ported from core/src/com/unciv/logic/map/pathingmap/PathingMapAStarPathfinder.kt (T093). Stripped: the purity annotations, the
// VERBOSE_PATHFINDING_LOGS debug logging (and so its commented-out calls), cacheToDebugString / queueToDebugString (debugger helpers).
// - libGDX IntIntMap is a Dictionary<int, int>: it is only ever looked up, never iterated.
// - The Kotlin class is named AStarPathfinder (the file is PathingMapAStarPathfinder.kt).
// - The Kotlin local `cost` shadows the `cost` property inside calculateNeighborNode; the property is `movementCost` here.

internal sealed class AStarPathfinder
{
    // Setting this higher than the fastest speed (railroads at 0.1f) will cause the pathfinding
    // to execute significantly faster, but it may miss optimal paths that use railroads way off
    // to the side. Additionally, it will cause subsequent pathfinding to bias *very* strongly
    // towards the earlier pathfinding, potentially causing it to miss even obvious railroads
    // for later paths.  If we eliminate the caching, then it would be safe to set this higher.
    internal const float FastestRoadCost = 0.1f;

    private readonly Tile? destination;
    private readonly TilePredicate passThroughPredicate;
    private readonly TilePredicate moveToPredicate;
    private readonly EndTurnDamageLookup endTurnDamage;
    private readonly TileMovementCost movementCost;
    private readonly TileRoadCost tileRoadCost;
    private readonly Func<Tile, RelationshipLevel> relationshipLevel;
    private readonly EndSearchPredicate endSearchPredicate;
    internal PathingMapCache Cache { get; }
    private readonly int timeLimitTurns;
    private readonly TileMap tileMap;

    private readonly long[] routeNodes; // Actually Array<RouteNode>
    private readonly Dictionary<int, int> tilesInTodo;
    private readonly FixedPointMovement fpmFullMovement;
    private FixedPointMovement damageFreeAnchorCost = FixedPointMovement.FpmZero;

    /// <summary>
    /// Frontier priority queue for managing the tiles to be checked.
    /// Tiles are ordered based on their priority, determined by the cumulative cost so far and the heuristic estimate to the goal.
    /// </summary>
    private readonly LongPriorityQueue todo;

    static AStarPathfinder()
    {
        if (FastestRoadCost != RoadStatus.Railroad.GetMovementImproved()) throw new ArgumentException("Failed requirement.");
    }

    internal AStarPathfinder(
        Tile? destination,
        TilePredicate passThroughPredicate,
        TilePredicate moveToPredicate,
        EndTurnDamageLookup endTurnDamage,
        TileMovementCost movementCost,
        TileRoadCost tileRoadCost,
        Func<Tile, RelationshipLevel> relationshipLevel,
        EndSearchPredicate endSearchPredicate,
        PathingMapCache cache,
        int timeLimitTurns,
        TileMap tileMap)
    {
        this.destination = destination;
        this.passThroughPredicate = passThroughPredicate;
        this.moveToPredicate = moveToPredicate;
        this.endTurnDamage = endTurnDamage;
        this.movementCost = movementCost;
        this.tileRoadCost = tileRoadCost;
        this.relationshipLevel = relationshipLevel;
        this.endSearchPredicate = endSearchPredicate;
        Cache = cache;
        this.timeLimitTurns = timeLimitTurns;
        this.tileMap = tileMap;

        routeNodes = cache.RouteNodes;
        var initialBufferSize = tileMap.TileMatrix.Count + tileMap.TileMatrix[0].Count;
        tilesInTodo = new Dictionary<int, int>(initialBufferSize);
        fpmFullMovement = cache.Key.FullMove;
        todo = new LongPriorityQueue(initialBufferSize);

        if (timeLimitTurns <= 0) throw new ArgumentException("Failed requirement.");
        if (timeLimitTurns >= RouteNode.MaxTurns) throw new ArgumentException("Failed requirement.");
        // Add all the initial tiles to check to the priority queue
        cache.NodesNeedingNeighbors.ForEachSetBit(it =>
        {
            var node = new RouteNode(routeNodes[it]);
            if (node.Initialized && MoveFromLessThanTimeLimit(node))
            {
                todo.Add(new PrioritizedNode(node, CalculateUnderestimatedMovement(node)).Bits);
                tilesInTodo[it] = node.DamagingTiles;
            }
        });
    }

    private bool MoveFromLessThanTimeLimit(RouteNode node) =>
        node.Turns < timeLimitTurns - 1 || (node.Turns == timeLimitTurns - 1 && node.MoveUsedThisTurn < fpmFullMovement);

    // Heuristics for not-yet-calculated tiles here based on distance to target
    private FixedPointMovement CalculateUnderestimatedMovement(RouteNode node)
    {
        var tile = node.GetTile(tileMap);
        var movementSoFar = fpmFullMovement * node.Turns + node.MoveUsedThisTurn.CoerceAtMost(fpmFullMovement);
        var minRemainingTiles = destination != null ? tile.AerialDistanceTo(destination) : 1;
        var minRemainingCost = tileRoadCost(tile) + (minRemainingTiles - 1) * FastestRoadCost;
        var underestimatedTotal = movementSoFar + minRemainingCost;
        return underestimatedTotal;
    }

    private bool NeighborNeedsQueueing(RouteNode currentNode, Tile neighborTile)
    {
        var alreadyCalculatedNode = new RouteNode(routeNodes[neighborTile.ZeroBasedIndex]);
        if (Cache.AddedNeighborNodes.Get(neighborTile.ZeroBasedIndex) && alreadyCalculatedNode.DamagingTiles <= currentNode.DamagingTiles)
            return false; // Note this only checks if THIS fork calculated it

        var todoWithDamage = tilesInTodo.TryGetValue(neighborTile.ZeroBasedIndex, out var queuedDamage) ? queuedDamage : int.MaxValue;
        if (todoWithDamage <= currentNode.DamagingTiles)
            return false; // another tile already queued a route to that neighbor. skip it.
        return true;
    }

    private bool NeighborNeedsCalculating(RouteNode currentNode, Tile neighborTile)
    {
        var alreadyCalculatedNode = new RouteNode(routeNodes[neighborTile.ZeroBasedIndex]);
        // If another fork already calculated the best route, then we can queue it and move on
        if (alreadyCalculatedNode.Initialized && alreadyCalculatedNode.DamagingTiles <= currentNode.DamagingTiles)
        {
            if (MoveFromLessThanTimeLimit(alreadyCalculatedNode))
                todo.Add(new PrioritizedNode(alreadyCalculatedNode, CalculateUnderestimatedMovement(alreadyCalculatedNode)).Bits);
            tilesInTodo[neighborTile.ZeroBasedIndex] = alreadyCalculatedNode.DamagingTiles;
            Cache.NodesNeedingNeighbors.Set(neighborTile.ZeroBasedIndex);
            return false;
        }
        if (!passThroughPredicate(neighborTile)) // can't pass through.
        {
            var noPathingNode = RouteNode.NoPathingNode(neighborTile, currentNode.Turns);
            routeNodes[neighborTile.ZeroBasedIndex] = noPathingNode.Bits;
            Cache.AddedNeighborNodes.Set(neighborTile.ZeroBasedIndex);
            Cache.NodesNeedingNeighbors.Clear(neighborTile.ZeroBasedIndex);
            return false;
        }
        return true;
    }

    /// <summary>
    /// Find the previous tile where stopping does not incur damage.
    ///
    /// Tiles that incur damage are assumed to be rare, so we save bits in the node by not storing where the previous
    /// non-damaging parent is. So in the rare cases we'd end up stopping on a damaging tile, we transitively check parents
    /// to find the previous tile where stopping does not incur damage.
    /// </summary>
    private RouteNode FindDamageFreeAnchor(RouteNode startNode, FixedPointMovement firstHopCost)
    {
        var ancestor = startNode;
        var totalCost = firstHopCost;
        while (true)
        {
            if (totalCost >= fpmFullMovement)
                return new RouteNode(); // this ancestor is already too far; anything earlier is even farther
            if (ancestor.CanStopOn && ancestor.EndTurnWithoutMoreDamage)
            {
                damageFreeAnchorCost = totalCost;
                return ancestor;
            }
            var ancestorTile = ancestor.GetTile(tileMap);
            var parentTile = ancestor.ParentTile(tileMap);
            if (parentTile == ancestorTile)
                return new RouteNode(); // reached the search's root with nothing usable
            ancestor = new RouteNode(routeNodes[parentTile.ZeroBasedIndex]);
            totalCost = (totalCost + movementCost(ancestor.GetTile(tileMap), ancestorTile)).CoerceAtMost(RouteNode.MaxMoveThisTurn);
        }
    }

    // This can use more than the remaining movement, but that's correct behavior.
    // https://yairm210.medium.com/multi-turn-pathfinding-7136bd0bdaf0
    private RouteNode CalculateNeighborNode(RouteNode currentNode, Tile neighborTile)
    {
        var currentTile = currentNode.GetTile(tileMap);
        var damagingTiles = currentNode.DamagingTiles;
        var cost = movementCost(currentTile, neighborTile).CoerceAtMost(fpmFullMovement);
        var canMoveTo = currentNode.Turns > 0 || moveToPredicate(neighborTile);
        var endTurnThereDamage = Math.Min(endTurnDamage(neighborTile), 1);
        var neighborDamaging = endTurnThereDamage != 0;
        var trueSafe = canMoveTo && !neighborDamaging;
        var moveSinceStoppable = (currentNode.MoveSinceStoppable + cost).CoerceAtMost(RouteNode.MaxMoveThisTurn);
        var newMoveSinceStoppable = canMoveTo ? FixedPointMovement.FpmZero : moveSinceStoppable;
        var midTurn = currentNode.MoveUsedThisTurn < fpmFullMovement;
        var usedSoFar = midTurn ? currentNode.MoveUsedThisTurn : FixedPointMovement.FpmZero;
        var newUsedMovement = (usedSoFar + cost).CoerceAtMost(fpmFullMovement);
        var thisTurnPassThroughOrSafeEndTurn = newUsedMovement < fpmFullMovement || trueSafe;
        var relationship = relationshipLevel(neighborTile);

        RouteNode NewNode(FixedPointMovement moveSince, FixedPointMovement usedMovement, int turnDelta, int newDamagingTiles) => new(
            neighborTile, relationship, moveSince, usedMovement,
            currentNode.Turns + turnDelta, currentTile, newDamagingTiles, neighborDamaging);

        if ((midTurn || currentNode.CanStopOn) && thisTurnPassThroughOrSafeEndTurn)
        {
            // if we can move to the next tile, and then either end our turn safely or move away, then we do so.
            var turnsDelta = midTurn ? 0 : 1;
            return NewNode(newMoveSinceStoppable, newUsedMovement, turnsDelta, damagingTiles);
        }
        if (!canMoveTo && moveSinceStoppable < fpmFullMovement)
        {
            // If we could have moved here if we'd paused before entering unstoppable tiles
            // (usually allied units), pretend we paused before entering the mountains.
            // Kotlin note: eliminate this endTurnDamage call.
            var retreatDamagingTiles = currentNode.CanStopOn
                ? Math.Min(damagingTiles + Math.Min(endTurnDamage(currentTile), 1), RouteNode.MaxDamagingTiles)
                : damagingTiles;
            var turnsDelta = currentNode.CanStopOn ? 1 : 0;
            return NewNode(moveSinceStoppable, moveSinceStoppable, turnsDelta, retreatDamagingTiles);
        }
        if (!canMoveTo)
        {
            // Even pausing as early as possible wasn't enough: it's simply not reachable this way,
            // and hopefully another tile finds a route to it later.
            return new RouteNode(); // uninitialized
        }

        // canMoveTo is true, so we CAN stop here -- but it's damaging, and we've run out of budget
        // to push past it. Before accepting the damage, look backward for a nearer damage-free
        // anchor to retroactively pause at instead, taking no damage at all.
        var damageFreeAnchor = FindDamageFreeAnchor(currentNode, cost);
        if (damageFreeAnchor.Initialized)
        {
            return new RouteNode(
                neighborTile, relationship,
                FixedPointMovement.FpmZero, damageFreeAnchorCost,
                damageFreeAnchor.Turns + 1, currentTile, damageFreeAnchor.DamagingTiles, neighborDamaging);
        }
        // Ending our turn here takes damage. We'll add the neighbor tile, but the damage
        // means its neighbors will be calculated at a super low priority. In the meantime, another
        // tile might find a route here that doesn't require taking damage, which is the ONLY
        // scenario where a tile can get recalculated.
        var newDamageTiles = Math.Min(damagingTiles + endTurnThereDamage, RouteNode.MaxDamagingTiles);
        return NewNode(FixedPointMovement.FpmZero, cost, 1, newDamageTiles);
    }

    private Tile? ConsiderNeighbor(RouteNode currentNode, Tile neighborTile)
    {
        var alreadyCalculatedNode = new RouteNode(routeNodes[neighborTile.ZeroBasedIndex]);
        if (!NeighborNeedsQueueing(currentNode, neighborTile)) return null;
        RouteNode neighborNode;
        if (!NeighborNeedsCalculating(currentNode, neighborTile))
        {
            neighborNode = new RouteNode(routeNodes[neighborTile.ZeroBasedIndex]);
        }
        else
        {
            var newNode = CalculateNeighborNode(currentNode, neighborTile);
            if (!newNode.Initialized) return null;
            routeNodes[neighborTile.ZeroBasedIndex] = newNode.Bits;
            if (MoveFromLessThanTimeLimit(newNode))
                todo.Add(new PrioritizedNode(newNode, CalculateUnderestimatedMovement(newNode)).Bits);
            tilesInTodo[neighborTile.ZeroBasedIndex] = newNode.DamagingTiles;
            Cache.NodesNeedingNeighbors.Set(neighborTile.ZeroBasedIndex);
            neighborNode = newNode;
        }
        if (!alreadyCalculatedNode.Initialized && endSearchPredicate(neighborTile, neighborNode))
            return neighborTile;
        return null;
    }

    internal Tile? StepUntilDestination()
    {
        var startTile = tileMap[Cache.Key.StartingPoint];
        if (endSearchPredicate(startTile, new RouteNode(routeNodes[startTile.ZeroBasedIndex]))) return startTile;
        while (todo.IsNotEmpty())
        {
            var currentPrioritizedNode = new PrioritizedNode(todo.Poll());
            var currentNode = new RouteNode(routeNodes[currentPrioritizedNode.TileIdx]);
            var currentTile = currentNode.GetTile(tileMap);
            foreach (var neighborTile in currentTile.Neighbors) // calculate each neighbor
            {
                var foundTargetTile = ConsiderNeighbor(currentNode, neighborTile);
                if (foundTargetTile != null) return foundTargetTile;
            }
            // mark this tile as having its neighbors added
            tilesInTodo.Remove(currentTile.ZeroBasedIndex);
            Cache.NodesNeedingNeighbors.Clear(currentTile.ZeroBasedIndex);
            Cache.AddedNeighborNodes.Set(currentTile.ZeroBasedIndex);
            // if we reached the destination, (or if another fork did), then we stop
            if (destination != null && new RouteNode(routeNodes[destination.ZeroBasedIndex]).Initialized)
                return destination;
        }
        return null;
    }

    public override string ToString() => nameof(AStarPathfinder);
}
