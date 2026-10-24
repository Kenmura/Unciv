using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Unciv.Json;
using Unciv.Models.Ruleset;
using Unciv.Utils;

namespace Unciv.Logic.Map;

/// <summary>
/// Reads an Open Doctrines <c>.odmap</c> into a <see cref="TileMap"/> (Kotlin <c>OdMapImport</c>). A zip whose <c>provinces.png</c> carries a province id
/// per pixel colour and whose <c>provinces.json</c> may name a <c>terrain</c> per province. The raster is RESAMPLED: each hex takes the province under its
/// centre. Deviation: PNG decoding is <see cref="OdMapPng"/> (no interlaced files) instead of libGDX <c>Pixmap</c>.
/// </summary>
public static class OdMapImport
{
    /// <summary>Large enough that continents survive the resample, small enough to play.</summary>
    public const int DefaultColumns = 80;
    public const int DefaultRows = 50;

    private sealed record Province(string? Terrain, bool IsSea);

    public static TileMap Read(IOdMapFile file, Ruleset ruleset, int columns = DefaultColumns, int rows = DefaultRows)
    {
        var provinces = new Dictionary<int, Province>();
        int[] raster;
        int width, height;

        using (var stream = file.OpenRead())
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
        {
            var provinceEntry = zip.GetEntry("provinces.png")
                ?? throw new UncivShowableException("That is not an Open Doctrines map - it has no provinces.png");
            byte[] pngBytes;
            using (var s = provinceEntry.Open())
            using (var buffer = new MemoryStream())
            {
                s.CopyTo(buffer);
                pngBytes = buffer.ToArray();
            }
            try
            {
                (width, height, raster) = OdMapPng.Decode(pngBytes);
            }
            catch (Exception e) when (e is InvalidDataException or EndOfStreamException)
            {
                throw new UncivShowableException("That map's provinces.png could not be read", e);
            }

            var tableEntry = zip.GetEntry("provinces.json");
            if (tableEntry != null)
            {
                string text;
                using (var s = tableEntry.Open())
                using (var reader = new StreamReader(s, new UTF8Encoding(false)))
                    text = reader.ReadToEnd();
                ReadProvinceTable(text, provinces);
            }
        }

        if (width <= 0 || height <= 0)
            throw new UncivShowableException("That map's provinces.png is empty");

        return Build(raster, width, height, provinces, ruleset, columns, rows);
    }

    /// <summary>The <c>JsonReader().parse(text).child</c> walk: every member (or element) of the top-level value.</summary>
    private static void ReadProvinceTable(string text, Dictionary<int, Province> provinces)
    {
        using var doc = JsonDocument.Parse(LenientJson.QuoteBareKeys(text), new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
        });
        var root = doc.RootElement;
        IEnumerable<(string? Name, JsonElement Value)> children = root.ValueKind switch
        {
            JsonValueKind.Object => root.EnumerateObject().Select(it => ((string?)it.Name, it.Value)),
            JsonValueKind.Array => root.EnumerateArray().Select(it => ((string?)null, it)),
            _ => [],
        };
        foreach (var (name, child) in children)
        {
            var nameAsInt = name != null && int.TryParse(name, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : 0;
            var id = child.ValueKind == JsonValueKind.Object && child.TryGetProperty("id", out var idValue) && IsPlainValue(idValue) ? AsInt(idValue) : nameAsInt;
            if (id == 0) continue;
            provinces[id] = new Province(GetString(child, "terrain"), GetBoolean(child, "is_sea"));
        }
    }

    // libGDX getXxx(name, default) returns the default for a missing, null, object or array member
    private static bool IsPlainValue(JsonElement value) => value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Object or JsonValueKind.Array);

    // libGDX JsonValue.asInt
    private static int AsInt(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number => value.TryGetInt64(out var l) ? unchecked((int)l) : KotlinMath.DoubleToInt(value.GetDouble()),
        JsonValueKind.String => int.Parse(value.GetString()!, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture),
        JsonValueKind.True => 1,
        JsonValueKind.False => 0,
        _ => throw new JsonException("Value cannot be converted to int"),
    };

    // getString(name, null)
    private static string? GetString(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out var value) || !IsPlainValue(value)) return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
    }

    // getBoolean(name, false); asBoolean: string is equalsIgnoreCase("true"), number is != 0
    private static bool GetBoolean(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out var value) || !IsPlainValue(value)) return false;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.String => string.Equals(value.GetString(), "true", StringComparison.OrdinalIgnoreCase),
            JsonValueKind.Number => value.GetDouble() != 0.0,
            _ => false,
        };
    }

    private static TileMap Build(int[] raster, int rasterWidth, int rasterHeight, Dictionary<int, Province> provinces, Ruleset ruleset, int columns, int rows)
    {
        // worldWrap = true: an Open Doctrines map is a globe. The constructor keeps the grid centred on the origin.
        var map = new TileMap(columns, rows, ruleset, true);
        map.MapParameters.Type = MapType.Empty;
        map.MapParameters.Shape = MapShape.Rectangular;
        map.MapParameters.MapSize = new MapSize(columns, rows);

        var colMin = -columns / 2;
        var rowMin = -rows / 2;
        for (var ri = 0; ri < rows; ri++)
            for (var ci = 0; ci < columns; ci++)
            {
                var px = Math.Clamp(KotlinMath.DoubleToInt((ci + 0.5) * rasterWidth / columns), 0, rasterWidth - 1);
                var py = Math.Clamp(KotlinMath.DoubleToInt((ri + 0.5) * rasterHeight / rows), 0, rasterHeight - 1);
                var id = raster[py * rasterWidth + px];
                provinces.TryGetValue(id, out var province);

                // Null when the province names no terrain, or names one that is not among the thirteen the two sides share.
                var stated = province?.Terrain == null ? null : OdTerrain.ForToken(province.Terrain);

                string baseTerrain;
                string? feature = null;
                if (id == 0 || province?.IsSea == true)
                    baseTerrain = "Ocean";
                else if (stated != null)
                {
                    baseTerrain = stated.BaseTerrain;
                    feature = stated.Feature;
                }
                else
                {
                    var latitude = 90.0 - 180.0 * (py + 0.5) / rasterHeight;
                    baseTerrain = ClimateFor(latitude);
                    if (Math.Abs(latitude) < 12.0) feature = "Jungle";
                }

                var position = HexMath.GetTileCoordsFromColumnRow(ci + colMin, ri + rowMin);
                if (!map.Contains(position)) continue;
                map[position].BaseTerrain = baseTerrain;
                map[position].SetTerrainFeatures(feature == null ? [] : [feature]);
            }

        map.SetTransients(ruleset, true);
        return map;
    }

    private static string ClimateFor(double latitude)
    {
        var a = Math.Abs(latitude);
        return a switch
        {
            >= 75.0 => "Snow",
            >= 60.0 => "Tundra",
            >= 35.0 => "Plains",
            >= 30.0 => "Desert", // the horse latitudes, roughly
            >= 23.0 => "Plains",
            _ => "Grassland", // the tropics; Jungle is added as a feature
        };
    }
}
