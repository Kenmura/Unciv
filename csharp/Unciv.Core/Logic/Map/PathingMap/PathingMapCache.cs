using System.Globalization;
using System.Text;
using Unciv.Logic.Map.MapUnits;
using Unciv.Logic.Map.Tiles;

namespace Unciv.Logic.Map.PathingMap;

// Ported from core/src/com/unciv/logic/map/pathingmap/PathingMapCache.kt (T093). Stripped: the purity annotations.
// - java.util.BitSet is PathingBitSet; the `synchronized` blocks are dropped (the engine is single-threaded and deterministic).
// - Not ported: PathingMapCache.clear(), which has no caller in Kotlin (PathingMap.clear() just drops the cache).

/// <summary>The key of a <see cref="PathingMapCache"/>. If the key no longer matches, the cache is invalid.</summary>
public sealed record PathingMapCacheKey(HexCoord StartingPoint, FixedPointMovement MoveRemaining, FixedPointMovement FullMove)
{
    public override string ToString() => $"{StartingPoint}/{MoveRemaining}/{FullMove}";
}

/// <summary>
/// <c>NodesNeedingNeighbors</c>: frontier list of the tiles to be checked. In exceptional cases, a node already calculated may be left here, and recalculated again later.
/// <c>AddedNeighborNodes</c>: which tiles have already been checked, so each tile is processed only once.
/// <c>RouteNodes</c>: for each tile reached during the search, its <see cref="RouteNode"/> bits (including the parent tile), used to reconstruct the path.
/// </summary>
internal sealed class PathingMapCache
{
    internal PathingMapCacheKey Key { get; }
    internal PathingBitSet NodesNeedingNeighbors { get; }
    internal PathingBitSet AddedNeighborNodes { get; }
    internal long[] RouteNodes { get; }  // Actually Array<RouteNode>
    internal PathsToTilesWithinTurn TilesSameTurn { get; } = new();

    private PathingMapCache(PathingMapCacheKey key, PathingBitSet nodesNeedingNeighbors, PathingBitSet addedNeighborNodes, long[] routeNodes)
    {
        Key = key;
        NodesNeedingNeighbors = nodesNeedingNeighbors;
        AddedNeighborNodes = addedNeighborNodes;
        RouteNodes = routeNodes;
    }

    /// <summary>Creates an empty <see cref="PathingMapCache"/>, using <paramref name="tileMap"/> only for allocating the arrays to the proper size</summary>
    public PathingMapCache(PathingMapCacheKey key, TileMap tileMap)
        : this(key, new PathingBitSet(tileMap.TileList.Count), new PathingBitSet(tileMap.TileList.Count), new long[tileMap.TileList.Count])
    {
    }

    public bool IsCacheValid(PathingMapCacheKey latestKey) => Key == latestKey;

    /// <summary>
    /// Returns a mutable fork for pathfinding. This uses the same <see cref="RouteNodes"/>, but copies of the frontier bitsets.
    /// It does not set <see cref="TilesSameTurn"/>.
    /// </summary>
    public PathingMapCache ForkForPathfinding() => new(Key, NodesNeedingNeighbors.Clone(), AddedNeighborNodes.Clone(), RouteNodes);

    /// <summary>
    /// Merges a fork created with <see cref="ForkForPathfinding"/>, modifying <see cref="AddedNeighborNodes"/> and <see cref="NodesNeedingNeighbors"/>.
    /// Also mutates <c>update.NodesNeedingNeighbors</c>, meaning the fork should be discarded.
    /// </summary>
    public void MergePathfindingFork(PathingMapCache update)
    {
        if (!(Key == update.Key && ReferenceEquals(RouteNodes, update.RouteNodes))) throw new ArgumentException("Failed requirement.");
        // For each tile who had its neighbors queued by the fork, move them to the right data structure.
        AddedNeighborNodes.Or(update.AddedNeighborNodes);
        NodesNeedingNeighbors.AndNot(update.AddedNeighborNodes);

        // For tiles that were queued but not yet calculated, add them to nodesNeedingNeighbors.
        // When a tile incurs taking damage, a later tile can replace its data with a less
        // damaging route. In edge cases, this can cause a tile to be queued a second time.
        // Since tiles can be queued multiple times, they can be both queued (at higher damage)
        // and already calculated (at lower damage). We have to skip over tiles already calculated.
        update.NodesNeedingNeighbors.AndNot(update.AddedNeighborNodes);
        NodesNeedingNeighbors.Or(update.NodesNeedingNeighbors);
    }

    public string ToDebugString(TileMap tileMap, Tile? destination = null)
    {
        var routeTiles = tileMap.TileList.Where((_, idx) => new RouteNode(RouteNodes[idx]).Initialized).ToList();
        if (routeTiles.Count == 0) return "{}";
        // first determine which rows and columns need printing
        var height = -tileMap.BottomY * 2;
        var width = tileMap.TileMatrix.Count;
        var xs = new PathingBitSet(width);
        var ys = new PathingBitSet(height);
        foreach (var tile in routeTiles)
        {
            xs.Set(tile.Position.x - tileMap.LeftX);
            ys.Set(height - tile.Position.y + tileMap.BottomY); // invert the ys to easily iterate from positive to negative
        }
        var stringBuilder = new StringBuilder("\n");
        // format the column headers
        stringBuilder.Append("       ");
        xs.ForEachSetBit(xIndex =>
        {
            var x = xIndex + tileMap.LeftX;
            stringBuilder.Append(' ').Append(FormatSigned(x)).Append("  ");
        });
        stringBuilder.Append('\n');
        // for each row
        ys.ForEachSetBit(yIndex =>
        {
            // print row header
            var y = -yIndex + height + tileMap.BottomY;
            stringBuilder.Append("  ").Append(FormatSigned(y)).Append(' ');
            // display each cell for the row
            xs.ForEachSetBit(xIndex =>
            {
                var x = xIndex + tileMap.LeftX;
                AppendDebugNode(stringBuilder, tileMap.GetOrNull(x, y), destination);
            });
            stringBuilder.Append('\n');
        });
        return stringBuilder.ToString();
    }

    /// <summary>Java <c>%+-4d</c>: explicit sign, left-justified in 4 columns.</summary>
    private static string FormatSigned(int value) =>
        ((value >= 0 ? "+" : "-") + Math.Abs((long)value).ToString(CultureInfo.InvariantCulture)).PadRight(4);

    private void AppendDebugNode(StringBuilder builder, Tile? tile, Tile? destination)
    {
        if (tile == null) // out of map bounds
        {
            builder.Append("       ");
            return;
        }
        var node = new RouteNode(RouteNodes[tile.ZeroBasedIndex]);
        if (!node.Initialized) // pathing has not explored this tile yet
        {
            builder.Append("  /    ");
            return;
        }
        var tag = node.Turns == 0 && node.MoveUsedThisTurn == FixedPointMovement.FpmZero ? 'S'
            : tile == destination ? 'D'
            : !node.CanStopOn ? '*'
            : ' ';
        if (node.Turns == int.MaxValue) // cannot move to this tile
            builder.Append(" -/---").Append(tag);
        else // we've found a minimum path to this tile
            builder.Append(node.Turns.ToString(CultureInfo.InvariantCulture).PadLeft(2))
                .Append('/')
                .Append(((double)node.MoveUsedThisTurn.ToFloat()).ToString("F1", CultureInfo.InvariantCulture))
                .Append(tag);
    }

    public override string ToString() =>
        $"PathingMapCache[key={Key} nodesNeedingNeighbors={NodesNeedingNeighbors.Cardinality()} addedNeighborNodes={AddedNeighborNodes.Cardinality()} routeNodes={RouteNodes.Length} tilesSameTurn={TilesSameTurn.Count}]";
}
