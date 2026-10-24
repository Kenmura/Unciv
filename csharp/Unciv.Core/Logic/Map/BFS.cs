using Unciv.Logic.Map.Tiles;

namespace Unciv.Logic.Map;

// Ported from core/src/com/unciv/logic/map/BFS.kt (T040). Stripped: the purity annotations.
// Deviation: tilesReached is a Dictionary (Kotlin HashMap, identity hashing on Tile); the order of GetReachedTiles was never defined.

/// <summary>
/// Defines intermediate steps of a breadth-first search, for use in either get shortest path or get connected tiles.
/// </summary>
public class BFS
{
    private readonly Func<Tile, bool> predicate;

    /// <param name="startingPoint">Starting <see cref="Tile"/> from which to start the search</param>
    /// <param name="predicate">A condition for subsequent neighboring tiles to be considered in search</param>
    public BFS(Tile startingPoint, Func<Tile, bool> predicate)
    {
        StartingPoint = startingPoint;
        this.predicate = predicate;
        tilesToCheck.Enqueue(startingPoint);
        tilesReached[startingPoint] = startingPoint;
    }

    public Tile StartingPoint { get; }

    /// <summary>Maximum number of tiles to search</summary>
    public int MaxSize { get; set; } = int.MaxValue;

    /// <summary>remaining tiles to check</summary>
    private readonly Queue<Tile> tilesToCheck = new(37); // needs resize at distance 4

    /// <summary>each tile reached points to its parent tile, where we got to it from</summary>
    private readonly Dictionary<Tile, Tile> tilesReached = [];

    /// <summary>Process fully until there's nowhere left to check</summary>
    public void StepToEnd()
    {
        while (!HasEnded())
            NextStep();
    }

    /// <summary>Process until either <paramref name="destination"/> is reached or there's nowhere left to check</summary>
    /// <returns><c>this</c> instance for chaining</returns>
    public BFS StepUntilDestination(Tile destination)
    {
        while (!tilesReached.ContainsKey(destination) && !HasEnded())
            NextStep();
        return this;
    }

    /// <summary>
    /// Process one tile-to-search, fetching all neighbors not yet touched
    /// and adding those that fulfill the predicate to the reached set
    /// and to the yet-to-be-processed set.
    /// Will do nothing when <see cref="HasEnded"/> returns <c>true</c>.
    /// </summary>
    /// <returns>The Tile that was checked, or <c>null</c> if there was nothing to do</returns>
    public Tile? NextStep()
    {
        if (tilesReached.Count >= MaxSize) { tilesToCheck.Clear(); return null; }
        if (!tilesToCheck.TryDequeue(out var current)) return null;
        foreach (var neighbor in current.Neighbors)
        {
            if (!tilesReached.ContainsKey(neighbor) && predicate(neighbor))
            {
                tilesReached[neighbor] = current;
                tilesToCheck.Enqueue(neighbor);
            }
        }
        return current;
    }

    /// <returns>a Sequence from the <paramref name="destination"/> back to the <see cref="StartingPoint"/>, including both, or empty if destination has not been reached</returns>
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

    /// <returns>true if there are no more tiles to check</returns>
    public bool HasEnded() => tilesToCheck.Count == 0;

    /// <returns>true if the <paramref name="tile"/> has been reached</returns>
    public bool HasReachedTile(Tile tile) => tilesReached.ContainsKey(tile);

    /// <returns>all tiles reached so far (a live read-only view; Kotlin returned the mutable key set, callers only read it)</returns>
    public IReadOnlyCollection<Tile> GetReachedTiles() => tilesReached.Keys;

    /// <returns>number of tiles reached so far</returns>
    public int Size() => tilesReached.Count;
}
