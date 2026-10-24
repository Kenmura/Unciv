using Unciv.Logic.Map.Tiles;
using Unciv.Mathematics;
using Unciv.Models.Ruleset.Unique;
using Unciv.Utils;

namespace Unciv.Logic.Map.MapGenerator.Mapregions;

// core/src/com/unciv/logic/map/mapgenerator/mapregions/Region.kt
// Deviations: libGDX Rectangle is a mutable class, ours a readonly struct, so Rect is a settable property and the Kotlin in-place
// field writes (rect.x = ...) are `Rect = Rect with { X = ... }`. The Kotlin HashSet<Tile> uses identity hashes (iteration order not
// reproducible even on the JVM), so tiles is an insertion-ordered LinkedHashSet.
public class Region(TileMap tileMap, Rectangle rect, int continentID = -1) : IGameContextRegion
{
    public TileMap TileMap { get; } = tileMap;
    public Rectangle Rect { get; set; } = rect;
    public int ContinentID { get; } = continentID;
    public LinkedHashSet<Tile> Tiles { get; } = new();
    public JavaHashMap<string, int> TerrainCounts { get; } = new();
    public int TotalFertility { get; set; }
    public string Type { get; set; } = "Hybrid"; // being an undefined or indeterminate type
    public string? Luxury { get; set; }
    public HexCoord? StartPosition { get; set; }
    public List<IMapRegionsCivilization> AssignedMinorCivs { get; } = [];

    public bool AffectedByWorldWrap { get; set; }

    int IGameContextRegion.ContinentId => ContinentID;

    /// <summary>libGDX <c>Rectangle.hashCode()</c>: 31-based over the raw float bits of height, width, x, y.</summary>
    int IGameContextRegion.RectHashCode
    {
        get
        {
            unchecked
            {
                var result = 1;
                result = (31 * result) + BitConverter.SingleToInt32Bits(Rect.Height);
                result = (31 * result) + BitConverter.SingleToInt32Bits(Rect.Width);
                result = (31 * result) + BitConverter.SingleToInt32Bits(Rect.X);
                result = (31 * result) + BitConverter.SingleToInt32Bits(Rect.Y);
                return result;
            }
        }
    }

    /// <summary>Recalculates tiles and fertility</summary>
    public void UpdateTiles(bool trim = true)
    {
        TotalFertility = 0;
        var minColumn = 99999f;
        var maxColumn = -99999f;
        var minRow = 99999f;
        var maxRow = -99999f;

        var columnHasTile = new HashSet<int>();

        Tiles.Clear();
        foreach (var tile in TileMap.GetTilesInRectangle(Rect).Where(it => ContinentID == -1 || it.GetContinent() == ContinentID).ToList())
        {
            var fertility = tile.GetTileFertility(ContinentID != -1);
            Tiles.Add(tile);
            unchecked { TotalFertility += fertility; }

            if (AffectedByWorldWrap)
                columnHasTile.Add(tile.GetColumn());

            if (trim)
            {
                var row = (float)tile.GetRow();
                var column = (float)tile.GetColumn();
                minColumn = Math.Min(minColumn, column);
                maxColumn = Math.Max(maxColumn, column);
                minRow = Math.Min(minRow, row);
                maxRow = Math.Max(maxRow, row);
            }
        }

        if (trim)
        {
            if (AffectedByWorldWrap) // Need to be more thorough with origin longitude
                Rect = Rect with { X = columnHasTile.Where(it => !columnHasTile.Contains(it - 1)).Max() };
            else
                Rect = Rect with { X = minColumn }; // ez way for non-wrapping regions
            Rect = Rect with { Y = minRow, Height = maxRow - minRow + 1 };
            if (AffectedByWorldWrap && minColumn < Rect.X)
            { // Thorough way
                Rect = Rect with { Width = columnHasTile.Count };
            }
            else
            {
                Rect = Rect with { Width = maxColumn - minColumn + 1 }; // ez way
                AffectedByWorldWrap = false; // also we're not wrapping anymore
            }
        }
    }

    /// <summary>Counts the terrains in the Region for type and start determination</summary>
    public void CountTerrains()
    {
        // Count terrains in the region
        TerrainCounts.Clear();
        foreach (var tile in Tiles)
        {
            var terrainsToCount = tile.TerrainHasUnique(UniqueType.IgnoreBaseTerrainForRegion)
                ? tile.TerrainFeatureObjects.Select(it => it.Name)
                : tile.AllTerrains.Select(it => it.Name);
            foreach (var terrain in terrainsToCount)
                TerrainCounts[terrain] = (TerrainCounts.TryGetValue(terrain, out var count) ? count : 0) + 1;
            if (tile.IsAdjacentToCoast())
                TerrainCounts["Coastal"] = (TerrainCounts.TryGetValue("Coastal", out var coastal) ? coastal : 0) + 1;
        }
    }

    /// <summary>Returns number terrains with <paramref name="name"/></summary>
    public int GetTerrainAmount(string name) => TerrainCounts.TryGetValue(name, out var count) ? count : 0;

    public override string ToString() =>
        $"Region({Type}, {Tiles.Count} tiles, {string.Join(", ", TerrainCounts.Select(it => $"{it.Value} {it.Key}"))})";
}
