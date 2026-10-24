using Unciv.Logic.Map.Tiles;

namespace Unciv.Logic.Map;

// Ported from core/src/com/unciv/logic/map/AStar.kt (T040). Stripped: the purity annotations.
// Deviations:
// - java.util.PriorityQueue is reproduced by a private binary heap with the same siftUp/siftDown, so ties between equal priorities
//   are broken exactly as on the JVM; Float.compareTo (total order: -0f < 0f, NaN greatest) is reproduced by JavaFloatCompare.
// - tilesReached/cumulativeTileCost/costCache are Dictionaries (Kotlin HashMap, identity hashing on Tile); the Pair key is a ValueTuple.

public readonly record struct TilePriority(Tile Tile, float Priority);

/// <summary>
/// AStar is an implementation of the A* search algorithm, commonly used for finding the shortest path
/// in a weighted graph.
/// </summary>
public class AStar
{
    private readonly Func<Tile, bool> predicate;
    private readonly Func<Tile, Tile, float> cost;
    private readonly Func<Tile, Tile, float> heuristic;

    /// <param name="startingPoint">The initial tile where the search begins.</param>
    /// <param name="predicate">Determines if a tile should be considered for further exploration.</param>
    /// <param name="cost">The cost of moving from 'fromTile' to 'toTile'.</param>
    /// <param name="heuristic">Estimates the cost from a given tile to the goal; must be admissible for the shortest path.
    /// Set to a function returning 0 for Dijkstra's algorithm.</param>
    public AStar(Tile startingPoint, Func<Tile, bool> predicate, Func<Tile, Tile, float> cost, Func<Tile, Tile, float> heuristic)
    {
        StartingPoint = startingPoint;
        this.predicate = predicate;
        this.cost = cost;
        this.heuristic = heuristic;
        tilesToCheck.Add(new TilePriority(startingPoint, 0f));
        tilesReached[startingPoint] = startingPoint;
        cumulativeTileCost[startingPoint] = 0f;
    }

    public Tile StartingPoint { get; }

    /// <summary>Maximum number of tiles to search</summary>
    public int MaxSize { get; set; } = int.MaxValue;

    /// <summary>Cache for storing the costs</summary>
    private readonly Dictionary<(Tile, Tile), float> costCache = [];

    private float GetCost(Tile from, Tile to)
    {
        if (costCache.TryGetValue((from, to), out var cached)) return cached;
        var computed = cost(from, to);
        costCache[(from, to)] = computed;
        return computed;
    }

    /// <summary>Frontier priority queue for managing the tiles to be checked.</summary>
    private readonly JavaPriorityQueue tilesToCheck = new();

    /// <summary>Each tile reached during the search points to its parent tile.</summary>
    private readonly Dictionary<Tile, Tile> tilesReached = [];

    /// <summary>The cumulative cost to reach each tile.</summary>
    private readonly Dictionary<Tile, float> cumulativeTileCost = [];

    /// <summary>Continues the search process until there are no more tiles left to check.</summary>
    public void StepToEnd()
    {
        while (!HasEnded())
            NextStep();
    }

    /// <summary>Continues the search until <paramref name="destination"/> is reached or there are no more tiles left to check.</summary>
    /// <returns>This AStar instance, allowing for method chaining.</returns>
    public AStar StepUntilDestination(Tile destination)
    {
        while (!tilesReached.ContainsKey(destination) && !HasEnded())
            NextStep();
        return this;
    }

    /// <summary>
    /// Processes one step in the A* algorithm, expanding the search from the current tile to its neighbors.
    /// If the maximum size is reached or no more tiles are available, this method will do nothing.
    /// </summary>
    public void NextStep()
    {
        if (tilesReached.Count >= MaxSize) { tilesToCheck.Clear(); return; }
        var polled = tilesToCheck.Poll();
        if (polled == null) return;
        var currentTile = polled.Value.Tile;
        foreach (var neighbor in currentTile.Neighbors)
        {
            float newCost = cumulativeTileCost[currentTile] + GetCost(currentTile, neighbor);
            if (predicate(neighbor) &&
                (!cumulativeTileCost.ContainsKey(neighbor)
                    || newCost < cumulativeTileCost.GetValueOrDefault(neighbor, float.MaxValue)))
            {
                cumulativeTileCost[neighbor] = newCost;
                float priority = newCost + heuristic(currentTile, neighbor);
                tilesToCheck.Add(new TilePriority(neighbor, priority));
                tilesReached[neighbor] = currentTile;
            }
        }
    }

    /// <summary>
    /// The path from the given <paramref name="destination"/> tile back to the starting point.
    /// If the destination has not been reached, the sequence will be empty.
    /// </summary>
    public IEnumerable<Tile> GetPathTo(Tile destination)
    {
        var currentNode = destination;
        while (true)
        {
            if (!tilesReached.TryGetValue(currentNode, out var parent)) break; // destination is not in our path
            yield return currentNode;
            if (currentNode == StartingPoint) break;
            currentNode = parent;
        }
    }

    /// <returns>True if there are no more tiles to be checked in the search.</returns>
    public bool HasEnded() => tilesToCheck.IsEmpty;

    /// <returns>True if the <paramref name="tile"/> has been reached during the search.</returns>
    public bool HasReachedTile(Tile tile) => tilesReached.ContainsKey(tile);

    /// <returns>The tiles that have been reached so far (a live read-only view; Kotlin returned the mutable key set).</returns>
    public IReadOnlyCollection<Tile> GetReachedTiles() => tilesReached.Keys;

    /// <returns>The count of tiles reached.</returns>
    public int Size() => tilesReached.Count;

    /// <summary><c>Float.compareTo</c> on the JVM: a total order where -0f &lt; 0f and NaN is greater than everything (incl. +Infinity).</summary>
    internal static int JavaFloatCompare(float a, float b)
    {
        if (a < b) return -1;
        if (a > b) return 1;
        // JVM floatToIntBits canonicalizes every NaN to one value
        int bitsA = float.IsNaN(a) ? 0x7fc00000 : BitConverter.SingleToInt32Bits(a);
        int bitsB = float.IsNaN(b) ? 0x7fc00000 : BitConverter.SingleToInt32Bits(b);
        return bitsA == bitsB ? 0 : bitsA < bitsB ? -1 : 1;
    }

    /// <summary>The binary heap of java.util.PriorityQueue (siftUp/siftDown/poll), ordered by <see cref="TilePriority.Priority"/> only.</summary>
    internal sealed class JavaPriorityQueue
    {
        private readonly List<TilePriority> queue = new(27);

        public bool IsEmpty => queue.Count == 0;

        public void Clear() => queue.Clear();

        public void Add(TilePriority e)
        {
            int k = queue.Count;
            queue.Add(e);
            while (k > 0)
            {
                int parent = (k - 1) >> 1;
                var p = queue[parent];
                if (JavaFloatCompare(e.Priority, p.Priority) >= 0) break;
                queue[k] = p;
                k = parent;
            }
            queue[k] = e;
        }

        public TilePriority? Poll()
        {
            if (queue.Count == 0) return null;
            var result = queue[0];
            int n = queue.Count - 1;
            var x = queue[n];
            queue.RemoveAt(n);
            if (n > 0) SiftDown(x, n);
            return result;
        }

        private void SiftDown(TilePriority x, int n)
        {
            int k = 0;
            int half = n >> 1;
            while (k < half)
            {
                int child = (k << 1) + 1;
                var c = queue[child];
                int right = child + 1;
                if (right < n && JavaFloatCompare(c.Priority, queue[right].Priority) > 0)
                    c = queue[child = right];
                if (JavaFloatCompare(x.Priority, c.Priority) <= 0) break;
                queue[k] = c;
                k = child;
            }
            queue[k] = x;
        }
    }
}
