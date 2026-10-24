using System.IO.Compression;
using System.Text;
using Unciv.Logic;
using Unciv.Logic.Map;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using RulesetClass = Unciv.Models.Ruleset.Ruleset;

namespace Unciv.Core.Tests.Logic.Map;

// Ports OdMapExportTests.kt and OdMapImportTests.kt (T042). Files are in memory; PNGs come from OdMapPng instead of libGDX Pixmap.
[Collection("RulesetCache")]
public sealed class OdMapTests : IDisposable
{
    private sealed class MemoryFile : IOdMapFile
    {
        public byte[] Bytes { get; private set; } = [];
        public Stream OpenRead() => new MemoryStream(Bytes);
        public Stream OpenWrite() => new CaptureStream(this);

        private sealed class CaptureStream(MemoryFile owner) : MemoryStream
        {
            protected override void Dispose(bool disposing)
            {
                if (disposing) owner.Bytes = ToArray();
                base.Dispose(disposing);
            }
        }
    }

    private readonly RulesetClass ruleset;

    public OdMapTests()
    {
        RulesetCache.Clear();
        var jsons = JsonsPath();
        RulesetCache.LoadRulesets(new DirectoryRulesetFileSource(jsons, Path.Combine(jsons, "no mods here")), noMods: true);
        ruleset = RulesetCache.Get(BaseRuleset.Civ_V_GnK.FullName())!;
    }

    public void Dispose() => RulesetCache.Clear();

    private static string JsonsPath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "android", "assets", "jsons");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException("android/assets/jsons not found above " + AppContext.BaseDirectory);
    }

    private static MemoryFile WriteOdmap(bool withTerrain)
    {
        const int w = 64, h = 32;
        var pixels = new int[w * h];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                pixels[y * w + x] = ((x < w / 2 ? 1 : 0) << 8) | 0xFF; // id 1 is (r,g,b) = (0,0,1); id 0 is unpainted
        var terrain = withTerrain ? ",\"terrain\":\"desert\"" : "";
        var table = $"{{\"1\":{{\"id\":1,\"iso_a3\":\"ZZZ\",\"name\":\"Testland\"{terrain}}}}}";
        return Zip(("provinces.png", OdMapPng.Encode(w, h, pixels)), ("provinces.json", Encoding.UTF8.GetBytes(table)));
    }

    private static MemoryFile Zip(params (string Name, byte[] Bytes)[] entries)
    {
        var file = new MemoryFile();
        using (var stream = file.OpenWrite())
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            foreach (var (name, bytes) in entries)
            {
                using var s = zip.CreateEntry(name).Open();
                s.Write(bytes, 0, bytes.Length);
            }
        return file;
    }

    // ---- OdMapImportTests.kt

    [Fact]
    public void AnOdmapBecomesATileMapUncivCanUse()
    {
        var map = OdMapImport.Read(WriteOdmap(false), ruleset, 20, 10);
        Assert.Equal(200, map.Values.Count);
        foreach (var tile in map.Values)
            Assert.True(ruleset.Terrains.ContainsKey(tile.BaseTerrain), $"{tile.BaseTerrain} is not in the ruleset");
    }

    [Fact]
    public void EveryHexIsPaintedAndTheMapSurvivesASecondSetTransients()
    {
        var map = OdMapImport.Read(WriteOdmap(true), ruleset, 20, 10);
        var unpainted = map.Values.Where(it => it.BaseTerrain is not ("Desert" or "Ocean")).ToList();
        Assert.Empty(unpainted);
        map.SetTransients(ruleset, true); // the call GameStarter makes
    }

    [Fact]
    public void UnpaintedRasterBecomesOcean()
    {
        var map = OdMapImport.Read(WriteOdmap(false), ruleset, 20, 10);
        var ocean = map.Values.Count(it => it.BaseTerrain == "Ocean");
        Assert.InRange(ocean, 80, 120);
    }

    [Fact]
    public void AStatedTerrainBeatsTheLatitudeGuess()
    {
        var map = OdMapImport.Read(WriteOdmap(true), ruleset, 20, 10);
        var land = map.Values.Where(it => it.BaseTerrain != "Ocean").ToList();
        Assert.NotEmpty(land);
        Assert.All(land, it => Assert.Equal("Desert", it.BaseTerrain));
    }

    // ---- Added: branches the Kotlin tests do not reach

    [Fact]
    public void AnArchiveWithoutProvincesPngIsRefused()
    {
        var e = Assert.Throws<UncivShowableException>(() => OdMapImport.Read(Zip(("other.txt", [1])), ruleset, 20, 10));
        Assert.Contains("no provinces.png", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ACorruptPngIsRefused() =>
        Assert.Throws<UncivShowableException>(() => OdMapImport.Read(Zip(("provinces.png", [1, 2, 3])), ruleset, 20, 10));

    [Fact]
    public void SeaFlagAndLatitudeFallbackAreHonoured()
    {
        var pixels = new int[8 * 8];
        Array.Fill(pixels, (1 << 8) | 0xFF);
        var seaFile = Zip(("provinces.png", OdMapPng.Encode(8, 8, pixels)), ("provinces.json", Encoding.UTF8.GetBytes("[{\"id\":1,\"is_sea\":true}]")));
        Assert.All(OdMapImport.Read(seaFile, ruleset, 4, 4).Values, it => Assert.Equal("Ocean", it.BaseTerrain));

        // no table: climate from latitude, Jungle at the equator
        var plain = Zip(("provinces.png", OdMapPng.Encode(8, 8, pixels)));
        var map = OdMapImport.Read(plain, ruleset, 4, 4);
        Assert.Contains(map.Values, it => it.BaseTerrain == "Snow");
        Assert.All(map.Values, it => Assert.True(ruleset.Terrains.ContainsKey(it.BaseTerrain)));
    }

    [Fact]
    public void LenientProvinceTableValuesFollowLibGdxDefaults()
    {
        var pixels = new int[8 * 8];
        Array.Fill(pixels, (1 << 8) | 0xFF);
        // id null -> key name 1; terrain object -> null; is_sea "yes" -> false
        var table = "{\"1\":{\"id\":null,\"terrain\":{},\"is_sea\":\"yes\"}}";
        var map = OdMapImport.Read(Zip(("provinces.png", OdMapPng.Encode(8, 8, pixels)), ("provinces.json", Encoding.UTF8.GetBytes(table))), ruleset, 4, 4);
        Assert.DoesNotContain(map.Values, it => it.BaseTerrain == "Ocean");
        var sea = "{\"1\":{\"id\":1,\"is_sea\":0.5}}";
        var seaMap = OdMapImport.Read(Zip(("provinces.png", OdMapPng.Encode(8, 8, pixels)), ("provinces.json", Encoding.UTF8.GetBytes(sea))), ruleset, 4, 4);
        Assert.All(seaMap.Values, it => Assert.Equal("Ocean", it.BaseTerrain));
    }

    // ---- OdMapExportTests.kt

    private TileMap SampleMap(int columns = 20, int rows = 10)
    {
        var map = new TileMap(columns, rows, ruleset, true);
        map.MapParameters.Shape = MapShape.Rectangular;
        map.MapParameters.MapSize = new MapSize(columns, rows);
        string[] terrains = ["Grassland", "Plains", "Desert", "Tundra", "Snow"];
        var index = 0;
        foreach (var tile in map.Values)
        {
            tile.BaseTerrain = index % 3 == 0 ? "Ocean" : terrains[index % terrains.Length];
            tile.SetTerrainFeatures([]);
            index++;
        }
        map.SetTransients(ruleset, true);
        return map;
    }

    private static string ReadEntry(MemoryFile file, string name)
    {
        using var zip = new ZipArchive(file.OpenRead(), ZipArchiveMode.Read);
        using var s = zip.GetEntry(name)!.Open();
        return new StreamReader(s, Encoding.UTF8).ReadToEnd();
    }

    [Fact]
    public void TheArchiveHoldsWhatAnOdmapMustHold()
    {
        var file = new MemoryFile();
        OdMapExport.Write(SampleMap(), file, "Test map");
        using var zip = new ZipArchive(file.OpenRead(), ZipArchiveMode.Read);
        foreach (var member in new[] { "provinces.png", "land_sea.png", "provinces.json", "countries.json", "metadata.json" })
            Assert.NotNull(zip.GetEntry(member));
        Assert.Contains("\"name\":\"Test map\"", ReadEntry(file, "metadata.json"), StringComparison.Ordinal);
    }

    [Fact]
    public void AMapSurvivesTheRoundTripHexForHex()
    {
        var original = SampleMap();
        var file = new MemoryFile();
        OdMapExport.Write(original, file, "Test map");
        var back = OdMapImport.Read(file, ruleset, 20, 10);

        Assert.Equal(original.Values.Count, back.Values.Count);
        var changed = new List<string>();
        var grasslandBecamePlains = 0;
        foreach (var tile in original.Values)
        {
            var other = back[tile.Position];
            if (other.BaseTerrain == tile.BaseTerrain) continue;
            if (tile.BaseTerrain == "Grassland" && other.BaseTerrain == "Plains")
            {
                grasslandBecamePlains++;
                continue;
            }
            changed.Add($"{tile.Position}: {tile.BaseTerrain} -> {other.BaseTerrain}");
        }
        Assert.Empty(changed);
        Assert.Equal(original.Values.Count(it => it.BaseTerrain == "Grassland"), grasslandBecamePlains);
    }

    [Fact]
    public void EveryTerrainTheTwoSidesShareExistsInTheRuleset()
    {
        foreach (var terrain in OdTerrain.Entries)
        {
            Assert.True(ruleset.Terrains.ContainsKey(terrain.BaseTerrain), $"{terrain.Token} maps to {terrain.BaseTerrain}, which the ruleset has not got");
            if (terrain.Feature != null)
                Assert.True(ruleset.Terrains.ContainsKey(terrain.Feature), $"{terrain.Token} implies the feature {terrain.Feature}");
            Assert.Same(terrain.Token == "inland_sea" ? OdTerrain.InlandSea : terrain, OdTerrain.ForToken(terrain.Token));
        }
        Assert.Equal(13, OdTerrain.Entries.Count);
        Assert.Null(OdTerrain.ForToken("not_a_terrain"));
        Assert.Same(OdTerrain.Desert, OdTerrain.ForToken("DESERT"));
    }

    [Fact]
    public void ForTilePrefersBaseAndFeatureOverBaseAlone()
    {
        var map = SampleMap();
        var tile = map.Values.First(it => !it.IsWater);
        tile.BaseTerrain = "Plains";
        tile.SetTerrainFeatures([]);
        Assert.Same(OdTerrain.Plains, OdTerrain.ForTile(tile));
        tile.SetTerrainFeatures(["Hill"]);
        Assert.Same(OdTerrain.Hills, OdTerrain.ForTile(tile));
        tile.BaseTerrain = "Grassland";
        tile.SetTerrainFeatures([]);
        Assert.Same(OdTerrain.Plains, OdTerrain.ForTile(tile)); // Grassland has no token
        tile.BaseTerrain = "Lakes";
        Assert.Same(OdTerrain.Lakes, OdTerrain.ForTile(tile));
    }

    [Fact]
    public void WaterBecomesUnpaintedRaster()
    {
        var original = SampleMap();
        var file = new MemoryFile();
        OdMapExport.Write(original, file, "Test map");
        var provinces = ReadEntry(file, "provinces.json");
        var count = System.Text.RegularExpressions.Regex.Matches(provinces, "\"id\":\\d+").Count;
        Assert.Equal(original.Values.Count(it => !it.IsWater), count);
    }

    [Fact]
    public void AnAllWaterMapIsRefusedRatherThanWrittenEmpty()
    {
        var map = SampleMap();
        foreach (var tile in map.Values) tile.BaseTerrain = "Ocean";
        map.SetTransients(ruleset, true);
        var e = Assert.Throws<UncivShowableException>(() => OdMapExport.Write(map, new MemoryFile(), "All sea"));
        Assert.Contains("all water", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportedJsonAndPngAreWellFormed()
    {
        var file = new MemoryFile();
        OdMapExport.Write(SampleMap(), file, "A \"quoted\" \\ name");
        foreach (var name in new[] { "provinces.json", "countries.json", "metadata.json" })
            System.Text.Json.JsonDocument.Parse(ReadEntry(file, name)).Dispose();
        Assert.Contains("\"iso_a3\":\"AAA\"", ReadEntry(file, "countries.json"), StringComparison.Ordinal);
        Assert.Contains("\"color\":\"#000001\"", ReadEntry(file, "provinces.json"), StringComparison.Ordinal);
        Assert.Contains("A \\\"quoted\\\" \\\\ name", ReadEntry(file, "metadata.json"), StringComparison.Ordinal);

        using var zip = new ZipArchive(file.OpenRead(), ZipArchiveMode.Read);
        using var ls = new MemoryStream();
        zip.GetEntry("land_sea.png")!.Open().CopyTo(ls);
        var (w, h, rgb) = OdMapPng.Decode(ls.ToArray());
        Assert.Equal((20 * OdMapExport.DefaultCellSize, 10 * OdMapExport.DefaultCellSize), (w, h));
        Assert.All(rgb.Distinct(), v => Assert.True(v is 0xC8C8C8 or 0x282828));
    }

    [Fact]
    public void PngCodecRoundTripsAndRejectsInterlace()
    {
        int[] pixels = [0x112233FF, 0x445566FF, 0x778899FF, unchecked((int)0xAABBCCFF)];
        var (w, h, rgb) = OdMapPng.Decode(OdMapPng.Encode(2, 2, pixels));
        Assert.Equal((2, 2), (w, h));
        Assert.Equal([0x112233, 0x445566, 0x778899, 0xAABBCC], rgb);
        var png = OdMapPng.Encode(2, 2, pixels);
        png[8 + 8 + 12] = 1; // IHDR interlace byte
        Assert.Throws<InvalidDataException>(() => OdMapPng.Decode(png));
    }
}
