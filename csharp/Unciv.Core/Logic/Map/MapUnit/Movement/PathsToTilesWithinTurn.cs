using System.Collections;
using Unciv.Logic.Map.Tiles;
using Unciv.Utils;

namespace Unciv.Logic.Map.MapUnits;

// Ported from the end of core/src/com/unciv/logic/map/mapunit/movement/UnitMovement.kt (T067).
// Kotlin extends LinkedHashMap; ours is sealed, so this wraps one (insertion-ordered, replacing a key keeps its position).

/// <summary>Should contain current unit location even when it has no movement</summary>
public class PathsToTilesWithinTurn : IReadOnlyDictionary<Tile, UnitMovement.ParentTileAndTotalMovement>
{
    private readonly LinkedHashMap<Tile, UnitMovement.ParentTileAndTotalMovement> map = new();

    public int Count => map.Count;
    public IEnumerable<Tile> Keys => map.Keys;
    public IEnumerable<UnitMovement.ParentTileAndTotalMovement> Values => map.Values;

    public UnitMovement.ParentTileAndTotalMovement this[Tile key]
    {
        get => map[key];
        set => map.Put(key, value);
    }

    public bool ContainsKey(Tile key) => map.ContainsKey(key);
    public bool TryGetValue(Tile key, out UnitMovement.ParentTileAndTotalMovement value) => map.TryGetValue(key, out value);
    public IEnumerator<KeyValuePair<Tile, UnitMovement.ParentTileAndTotalMovement>> GetEnumerator() => map.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public List<Tile> GetPathToTile(Tile tile)
    {
        if (!ContainsKey(tile))
        {
            Log.Debug("PathsToTilesWithinTurn#getPathToTile does not contain {0}", tile);
            throw new InvalidOperationException($"Can't reach {tile}");
        }
        var reversePathList = new List<Tile>();
        var currentTile = tile;
        while (this[currentTile].ParentTile != currentTile)
        {
            reversePathList.Add(currentTile);
            currentTile = this[currentTile].ParentTile;
        }
        reversePathList.Reverse();
        return reversePathList;
    }
}
