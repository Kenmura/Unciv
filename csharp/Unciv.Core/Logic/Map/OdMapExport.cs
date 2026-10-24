using System.Globalization;
using System.IO.Compression;
using System.Text;
using Unciv.Utils;

namespace Unciv.Logic.Map;

/// <summary>
/// Writes a <see cref="TileMap"/> out as an Open Doctrines <c>.odmap</c> (Kotlin <c>OdMapExport</c>). The inverse of <see cref="OdMapImport"/>:
/// each grid cell is painted as a solid rectangle and the importer samples the cell centre, so a map round-trips.
/// Archive: <c>provinces.png</c> (one province id per LAND hex in the pixel colour), <c>land_sea.png</c> (land 200, sea 40),
/// <c>provinces.json</c> (with the additive <c>terrain</c> key), <c>countries.json</c>, <c>metadata.json</c>.
/// Deviation: the PNG bytes come from <see cref="OdMapPng"/> rather than libGDX, so the files differ byte-wise but decode to the same pixels.
/// </summary>
public static class OdMapExport
{
    /// <summary>Raster pixels per hex. Enough that coastlines are not staircases.</summary>
    public const int DefaultCellSize = 16;

    public static void Write(TileMap map, IOdMapFile file, string name = "Unciv map", int cellSize = DefaultCellSize)
    {
        var tiles = map.Values.ToList();
        if (tiles.Count == 0) throw new ArgumentException("a TileMap with no tiles");

        // The grid is walked FORWARDS and never by inverting a hex coordinate (HexMath.GetColumn/GetRow are not exact inverses).
        var columns = map.MapParameters.MapSize.Width;
        var rows = map.MapParameters.MapSize.Height;
        var colMin = -columns / 2;
        var rowMin = -rows / 2;

        var byCell = new Dictionary<long, Tiles.Tile>(tiles.Count * 2);
        for (var ri = 0; ri < rows; ri++)
            for (var ci = 0; ci < columns; ci++)
            {
                var position = HexMath.GetTileCoordsFromColumnRow(ci + colMin, ri + rowMin);
                if (!map.Contains(position)) continue;
                byCell[CellKey(ci + colMin, ri + rowMin)] = map[position];
            }
        if (byCell.Count != tiles.Count)
            throw new UncivShowableException(
                $"That map's declared size [{columns}x{rows}] does not match its "
                + $"[{tiles.Count}] tiles - [{byCell.Count}] were found on the grid");

        map.AssignContinents(TileMap.AssignContinentsMode.Ensure);

        // One province per land hex, numbered from 1 -- id 0 means unpainted.
        var provinceOf = new Dictionary<long, int>();
        var continents = new LinkedHashSet<int>();
        var nextId = 1;
        for (var ri = 0; ri < rows; ri++)
            for (var ci = 0; ci < columns; ci++)
            {
                if (!byCell.TryGetValue(CellKey(ci + colMin, ri + rowMin), out var tile)) continue;
                if (tile.IsWater) continue;
                provinceOf[CellKey(ci + colMin, ri + rowMin)] = nextId++;
                continents.Add(tile.GetContinent());
            }
        if (provinceOf.Count == 0)
            throw new UncivShowableException("That map is all water - there is nothing to export");

        var countryOf = new Dictionary<int, int>();
        var countryIndex = 0;
        foreach (var continent in continents) countryOf[continent] = ++countryIndex; // associate: later duplicates cannot occur in a set

        var provincePng = Paint(columns, rows, cellSize, (ci, ri) =>
        {
            var id = provinceOf.GetValueOrDefault(CellKey(ci + colMin, ri + rowMin), 0);
            return (id << 8) | 0xFF; // RGBA8888; id in the top three bytes
        });
        var landSeaPng = Paint(columns, rows, cellSize, (ci, ri) =>
        {
            var land = provinceOf.ContainsKey(CellKey(ci + colMin, ri + rowMin));
            var v = land ? 200 : 40;
            return (v << 24) | (v << 16) | (v << 8) | 0xFF;
        });

        // In grid order, so the table reads the way the raster is laid out.
        var provinceEntries = new List<string>();
        for (var ri = 0; ri < rows; ri++)
            for (var ci = 0; ci < columns; ci++)
            {
                var key = CellKey(ci + colMin, ri + rowMin);
                if (!provinceOf.TryGetValue(key, out var id)) continue;
                var tile = byCell[key];
                var country = countryOf.GetValueOrDefault(tile.GetContinent(), 1);
                provinceEntries.Add(
                    $"\"{Inv(id)}\":{{\"id\":{Inv(id)},\"color\":\"#{id.ToString("X6", CultureInfo.InvariantCulture)}\",\"country_id\":{Inv(country)},"
                    + $"\"iso_a3\":\"{IsoFor(country)}\",\"name\":\"Province {Inv(id)}\","
                    + $"\"terrain\":\"{OdTerrain.ForTile(tile).Token}\"}}");
            }
        var provincesJson = "{" + string.Join(",", provinceEntries) + "}";

        var countriesJson = "{" + string.Join(",", countryOf.Values.Order().Select(country =>
            $"\"{Inv(country)}\":{{\"id\":{Inv(country)},\"color\":\"{CountryColour(country)}\","
            + $"\"iso_a3\":\"{IsoFor(country)}\",\"name\":\"Continent {Inv(country)}\",\"treasury\":100.0}}")) + "}";

        var metadata = $"{{\"name\":{Quote(name)},"
            + "\"description\":\"Exported from Unciv\",\"author\":\"Unciv\","
            + "\"map_date\":\"January 2000 AD\",\"license\":\"\",\"has_scripts\":false}";

        using var stream = file.OpenWrite();
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        PutEntry(zip, "provinces.png", provincePng);
        PutEntry(zip, "land_sea.png", landSeaPng);
        PutEntry(zip, "provinces.json", Encoding.UTF8.GetBytes(provincesJson));
        PutEntry(zip, "countries.json", Encoding.UTF8.GetBytes(countriesJson));
        PutEntry(zip, "metadata.json", Encoding.UTF8.GetBytes(metadata));
    }

    private static string Inv(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static void PutEntry(ZipArchive zip, string name, byte[] bytes)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.SmallestSize);
        using var s = entry.Open();
        s.Write(bytes, 0, bytes.Length);
    }

    /// <summary>Grid cell as one key, so no nested maps and no allocation per lookup.</summary>
    private static long CellKey(int column, int row) => ((long)column << 32) | ((long)row & 0xFFFFFFFFL);

    /// <summary>Each cell painted as a solid rectangle, which the importer re-samples at its centre.</summary>
    private static byte[] Paint(int columns, int rows, int cellSize, Func<int, int, int> rgbaOf)
    {
        var width = columns * cellSize;
        var height = rows * cellSize;
        var pixels = new int[width * height];
        for (var ri = 0; ri < rows; ri++)
            for (var ci = 0; ci < columns; ci++)
            {
                var rgba = rgbaOf(ci, ri);
                for (var y = ri * cellSize; y < (ri + 1) * cellSize; y++)
                    Array.Fill(pixels, rgba, y * width + ci * cellSize, cellSize);
            }
        return OdMapPng.Encode(width, height, pixels);
    }

    /// <summary>AAA, AAB, ... - a placeholder code, since a continent has no country.</summary>
    private static string IsoFor(int country)
    {
        var n = country - 1;
        return new string([(char)('A' + (n / 676) % 26), (char)('A' + (n / 26) % 26), (char)('A' + n % 26)]);
    }

    /// <summary>Spread around the hue circle by the golden angle, so neighbours differ.</summary>
    private static string CountryColour(int country)
    {
        var h = country * 137.508 % 360.0 / 60.0;
        const double s = 0.55;
        const double v = 0.75;
        var i = KotlinMath.DoubleToInt(h);
        var f = h - i;
        var p = v * (1 - s);
        var q = v * (1 - s * f);
        var t = v * (1 - s * (1 - f));
        var (r, g, b) = (i % 6) switch
        {
            0 => (v, t, p),
            1 => (q, v, p),
            2 => (p, v, t),
            3 => (p, q, v),
            4 => (t, p, v),
            _ => (v, p, q),
        };
        return string.Create(CultureInfo.InvariantCulture,
            $"#{KotlinMath.DoubleToInt(r * 255):X2}{KotlinMath.DoubleToInt(g * 255):X2}{KotlinMath.DoubleToInt(b * 255):X2}");
    }

    private static string Quote(string s) =>
        "\"" + s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}
