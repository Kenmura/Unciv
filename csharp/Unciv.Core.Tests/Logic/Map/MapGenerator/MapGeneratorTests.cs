using System.Globalization;
using System.Text;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tile;
using Unciv.UI.Screens.MapEditorScreen;
using GeneratorClass = Unciv.Logic.Map.MapGenerator.MapGenerator;
using RulesetClass = Unciv.Models.Ruleset.Ruleset;

namespace Unciv.Core.Tests.Logic.Map.MapGenerator;

// T041: MapGenerator.kt, MapGenerationTests.kt (terrain counts, auto size, all type x shape x wrap) with the Civ V Vanilla ruleset (what RulesetCache.getVanillaRuleset() returns).
// Golden/MapGenerator.golden.tsv: the maps Kotlin produces for the same seeds (map editor mode, no GameInfo), dumped by a throw-away
// JUnit test (tests/src/.../GoldenDumpTmp.kt, not committed) that ran MapGenerator(ruleset).generateMap(params) via
// `./gradlew :tests:test --tests '*GoldenDumpTmp*'` on the vanilla ruleset, Tiny size, one line per tile in tileList order:
// x,y|baseTerrain|features joined by '+'|resource|resourceAmount|naturalWonder|improvement|riverBitsBL,BR,B|continent.
// The full pipeline with civilizations is not golden: Kotlin's StartNormalizer/MinorCivPlacer draw from unseeded Random, and two Kotlin
// runs of the same seed differ.
[Collection("RulesetCache")]
public sealed class MapGeneratorTests : IDisposable
{
    private readonly RulesetClass ruleset;

    public MapGeneratorTests()
    {
        RulesetCache.Clear();
        var jsons = JsonsPath();
        RulesetCache.LoadRulesets(new DirectoryRulesetFileSource(jsons, Path.Combine(jsons, "no mods here")), noMods: true);
        ruleset = RulesetCache.GetVanillaRuleset();
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

    private static string Dump(TileMap map)
    {
        var sb = new StringBuilder();
        foreach (var t in map.Values)
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"{t.Position.X},{t.Position.Y}|{t.BaseTerrain}|{string.Join('+', t.TerrainFeatures)}|{t.Resource ?? ""}|{t.ResourceAmount}|{t.NaturalWonder ?? ""}|{t.Improvement ?? ""}|{(t.HasBottomLeftRiver ? 1 : 0)}{(t.HasBottomRightRiver ? 1 : 0)}{(t.HasBottomRiver ? 1 : 0)}|{t.GetContinent()}\n");
        }
        return sb.ToString();
    }

    private static Dictionary<string, string> LoadGolden()
    {
        var result = new Dictionary<string, string>();
        string? name = null;
        var sb = new StringBuilder();
        foreach (var line in File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Logic", "Map", "MapGenerator", "Golden", "MapGenerator.golden.tsv")))
        {
            if (line.StartsWith('#'))
            {
                if (name != null) result[name] = sb.ToString();
                name = line[1..];
                sb.Clear();
            }
            else sb.Append(line).Append('\n');
        }
        if (name != null) result[name] = sb.ToString();
        return result;
    }

    private TileMap Generate(string type, string shape, bool wrap, long seed, MapSize? size = null, string mirroring = MirroringType.None)
    {
        var p = new MapParameters { Type = type, Shape = shape, WorldWrap = wrap, Seed = seed, MapSize = size ?? MapSize.Tiny, Mirroring = mirroring };
        return new GeneratorClass(ruleset).GenerateMap(p);
    }

    [Theory]
    [InlineData("edit_Pangaea_Hexagonal_false_1", MapType.Pangaea, MapShape.Hexagonal, false, 1L)]
    [InlineData("edit_Pangaea_Hexagonal_false_7", MapType.Pangaea, MapShape.Hexagonal, false, 7L)]
    [InlineData("edit_Perlin_Rectangular_true_1", MapType.Perlin, MapShape.Rectangular, true, 1L)]
    [InlineData("edit_Perlin_Rectangular_true_7", MapType.Perlin, MapShape.Rectangular, true, 7L)]
    [InlineData("edit_Perlin_Flat_Earth_Hexagonal_false_1", MapType.Perlin, MapShape.FlatEarth, false, 1L)]
    [InlineData("edit_Perlin_Flat_Earth_Hexagonal_false_7", MapType.Perlin, MapShape.FlatEarth, false, 7L)]
    [InlineData("edit_Boreal_Hexagonal_true_1", MapType.Boreal, MapShape.Hexagonal, true, 1L)]
    [InlineData("edit_Boreal_Hexagonal_true_7", MapType.Boreal, MapShape.Hexagonal, true, 7L)]
    [InlineData("edit_Archipelago_Hexagonal_false_1", MapType.Archipelago, MapShape.Hexagonal, false, 1L)]
    [InlineData("edit_Archipelago_Hexagonal_false_7", MapType.Archipelago, MapShape.Hexagonal, false, 7L)]
    public void EditorMapsMatchKotlinGolden(string caseName, string type, string shape, bool wrap, long seed)
    {
        Assert.Equal(LoadGolden()[caseName], Dump(Generate(type, shape, wrap, seed)));
    }

    [Theory]
    [InlineData("edit_mirror_fourway_3", MirroringType.FourWay)]
    [InlineData("edit_mirror_leftright_3", MirroringType.LeftRight)]
    public void MirroredEditorMapsMatchKotlinGolden(string caseName, string mirroring)
    {
        Assert.Equal(LoadGolden()[caseName], Dump(Generate(MapType.Perlin, MapShape.Hexagonal, false, 3, mirroring: mirroring)));
    }

    [Fact]
    public void EmptyMapIsAllOceanAndMatchesKotlinGolden()
    {
        var map = Generate(MapType.Empty, MapShape.Hexagonal, false, 1);
        Assert.All(map.Values, it => Assert.Equal(Constants.Ocean, it.BaseTerrain));
        Assert.Equal(LoadGolden()["edit_empty"], Dump(map));
    }

    [Fact]
    public void TestAllMapGenerations()
    {
        foreach (var mapType in MapType.AllValues)
            foreach (var mapShape in MapShape.AllValues)
                foreach (var wrap in new[] { true, false })
                    Assert.NotEmpty(Generate(mapType, mapShape, wrap, 1).Values);
    }

    private static void AssertApproximately(int? value, int target, double ratio = 2.0)
    {
        var min = (int)(target / ratio) - 1;
        var max = (int)(target * ratio) + 1;
        if (min == 0) Assert.True(value == null || (value >= 0 && value <= max), $"expected: 0 to {max} but was: {value}");
        else Assert.True(value != null && value >= min && value <= max, $"expected: {min} to {max} but was: {value}");
    }

    private (Dictionary<string, int> Terrains, Dictionary<string, int> Features) CountOver(string shape, MapSize? size)
    {
        var terrainCounts = new Dictionary<string, int>();
        var featureCounts = new Dictionary<string, int>();
        for (long seed = 1; seed <= 20; seed++)
        {
            var p = new MapParameters { Type = MapType.Perlin, Shape = shape, WorldWrap = true, Seed = seed };
            if (size != null) p.MapSize = size;
            var map = new GeneratorClass(ruleset).GenerateMap(p);
            foreach (var tile in map.Values)
            {
                terrainCounts[tile.BaseTerrain] = terrainCounts.GetValueOrDefault(tile.BaseTerrain) + 1;
                foreach (var feature in tile.TerrainFeatures)
                    featureCounts[feature] = featureCounts.GetValueOrDefault(feature) + 1;
            }
        }
        return (terrainCounts, featureCounts);
    }

    private static int? C(Dictionary<string, int> d, string key) => d.TryGetValue(key, out var v) ? v : null;

    [Fact]
    public void TerrainTileCountsReasonableForHexMaps()
    {
        var (t, f) = CountOver(MapShape.Hexagonal, MapSize.Small);
        AssertApproximately(C(t, "Ocean"), 4079); AssertApproximately(C(t, "Coast"), 2443);
        AssertApproximately(C(t, "Grassland"), 2140); AssertApproximately(C(t, "Plains"), 2263);
        AssertApproximately(C(t, "Tundra"), 1156); AssertApproximately(C(t, "Desert"), 1038);
        AssertApproximately(C(t, "Lakes"), 105); AssertApproximately(C(t, "Mountain"), 212);
        AssertApproximately(C(t, "Snow"), 664); AssertApproximately(C(f, "Ice"), 40);
        AssertApproximately(C(f, "Hill"), 1804); AssertApproximately(C(f, "Forest"), 1001);
        AssertApproximately(C(f, "Marsh"), 64); AssertApproximately(C(f, "Flood plains"), 47);
        AssertApproximately(C(f, "Oasis"), 34); AssertApproximately(C(f, "Atoll"), 135);
        AssertApproximately(C(f, "Jungle"), 393);
    }

    [Fact]
    public void TerrainTileCountsReasonableForRectangleMaps()
    {
        var (t, f) = CountOver(MapShape.Rectangular, null);
        AssertApproximately(C(t, "Ocean"), 7826); AssertApproximately(C(t, "Coast"), 4547);
        AssertApproximately(C(t, "Grassland"), 3019); AssertApproximately(C(t, "Plains"), 3168);
        AssertApproximately(C(t, "Tundra"), 2345); AssertApproximately(C(t, "Desert"), 1490);
        AssertApproximately(C(t, "Lakes"), 118); AssertApproximately(C(t, "Mountain"), 344);
        AssertApproximately(C(t, "Snow"), 2663); AssertApproximately(C(f, "Ice"), 347);
        AssertApproximately(C(f, "Hill"), 3115); AssertApproximately(C(f, "Forest"), 1466);
        AssertApproximately(C(f, "Marsh"), 100); AssertApproximately(C(f, "Flood plains"), 78);
        AssertApproximately(C(f, "Oasis"), 64); AssertApproximately(C(f, "Atoll"), 248);
        AssertApproximately(C(f, "Jungle"), 511);
    }

    [Fact]
    public void TerrainTileCountsReasonableForFlatEarthMaps()
    {
        var (t, f) = CountOver(MapShape.FlatEarth, null);
        AssertApproximately(C(t, "Ocean"), 8340); AssertApproximately(C(t, "Coast"), 4847);
        AssertApproximately(C(t, "Grassland"), 2771); AssertApproximately(C(t, "Plains"), 2720);
        AssertApproximately(C(t, "Tundra"), 1629); AssertApproximately(C(t, "Desert"), 1350);
        AssertApproximately(C(t, "Lakes"), 58); AssertApproximately(C(t, "Mountain"), 906, 4.0);
        AssertApproximately(C(t, "Snow"), 2025); AssertApproximately(C(f, "Ice"), 1083);
        AssertApproximately(C(f, "Hill"), 2392); AssertApproximately(C(f, "Forest"), 1285);
        AssertApproximately(C(f, "Marsh"), 87); AssertApproximately(C(f, "Flood plains"), 84);
        AssertApproximately(C(f, "Oasis"), 48); AssertApproximately(C(f, "Atoll"), 239);
        AssertApproximately(C(f, "Jungle"), 526);
    }

    private FakeGameInfoState NewGame(int majors, int minors)
    {
        var info = new FakeGameInfoState { RulesetData = ruleset };
        foreach (var nation in ruleset.Nations.Values.Where(it => it.IsMajorCiv).Take(majors)
                     .Concat(ruleset.Nations.Values.Where(it => it.IsCityState).Take(minors)))
        {
            var civ = new Civilization(nation) { GameInfo = info };
            civ.State = new Unciv.Models.Ruleset.Unique.GameContext(civInfo: civ);
            info.Civs.Add(civ);
        }
        return info;
    }

    [Fact]
    public void FullPipelineWithCivilizationsPlacesStartsAndResourcesWithoutThrowing()
    {
        var info = NewGame(3, 3);
        var p = new MapParameters { Type = MapType.Perlin, Shape = MapShape.Hexagonal, Seed = 1, MapSize = MapSize.Small };
        var map = new GeneratorClass(ruleset).GenerateMap(p, new GameParameters(), info);
        info.Map = map;
        Assert.Same(info, map.GameInfo);
        Assert.Equal(6, map.StartingLocations.Count);
        Assert.Contains(map.Values, it => it.Resource != null);
        Assert.Contains(map.Values, it => it.NaturalWonder != null);
        Assert.Contains(map.Values, it => it.IsLand);
    }

    [Fact]
    public void FullPipelineIsDeterministic()
    {
        string Run()
        {
            var info = NewGame(3, 3);
            var p = new MapParameters { Type = MapType.Perlin, Shape = MapShape.Hexagonal, Seed = 5, MapSize = MapSize.Small };
            var map = new GeneratorClass(ruleset).GenerateMap(p, new GameParameters(), info);
            return Dump(map) + string.Join(';', map.StartingLocations.Select(it => $"{it.Position.X},{it.Position.Y},{it.Nation}"));
        }
        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void AutoMapSizeKeepsStartsAboutThirteenTilesApart()
    {
        var info = NewGame(4, 0);
        var p = new MapParameters { MapSize = new MapSize(MapSize.AutoName) };
        var results = new List<double>();
        for (var i = 0; i < 10; i++)
        {
            p.Seed += 1;
            var map = new GeneratorClass(ruleset).GenerateMap(p, new GameParameters(), info);
            var coords = map.StartingLocations.Select(it => it.Position).ToList();
            results.Add(coords.Select(pos => coords.Where(it => it != pos).Min(other => HexMath.GetDistance(pos.ToVector2(), other.ToVector2()))).Average());
        }
        var average = (int)(results.Sum() / results.Count);
        Assert.InRange(average, 13 - 1.25, 13 + 1.25);
    }

    [Fact]
    public void SeedZeroNeedsAnExplicitClockAndCancellationSkipsSteps()
    {
        var p = new MapParameters { Type = MapType.Perlin, MapSize = MapSize.Tiny };
        Assert.Throws<InvalidOperationException>(() => new GeneratorClass(ruleset).GenerateMap(p));
        var map = new GeneratorClass(ruleset, currentTimeMillis: () => 12345L).GenerateMap(p);
        Assert.Equal(12345L, map.MapParameters.Seed);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var cancelled = new GeneratorClass(ruleset, cts.Token).GenerateMap(new MapParameters { Seed = 2, MapSize = MapSize.Tiny });
        Assert.All(cancelled.Values, it => Assert.Equal("Grassland", it.BaseTerrain)); // no generation step ran
    }

    [Fact]
    public void SingleStepsRunAndAllIsRejected()
    {
        var map = Generate(MapType.Perlin, MapShape.Hexagonal, false, 4);
        var generator = new GeneratorClass(ruleset);
        Assert.Throws<ArgumentException>(() => generator.GenerateSingleStep(map, MapGeneratorSteps.All));
        foreach (var step in Enum.GetValues<MapGeneratorSteps>().Where(it => it != MapGeneratorSteps.All))
            generator.GenerateSingleStep(map, step);
        Assert.NotEmpty(map.Values);
    }

    [Fact]
    public void SetConnectedByRiverWithConvertTerrainsAppliesChangesTerrain()
    {
        var rs = new RulesetClass();
        rs.Terrains.Put("Plains", new Terrain { Name = "Plains", Type = TerrainType.Land, Uniques = ["Becomes [Grassland] when adjacent to [River]"] });
        rs.Terrains.Put("Grassland", new Terrain { Name = "Grassland", Type = TerrainType.Land });
        var map = new TileMap(2, rs);
        foreach (var tile in map.Values) { tile.BaseTerrain = "Plains"; tile.SetTerrainTransients(); }
        var a = map[HexCoord.Zero];
        var b = a.Neighbors[0];
        Assert.True(a.SetConnectedByRiver(b, true, convertTerrains: false));
        Assert.Equal("Plains", a.BaseTerrain);
        Assert.True(a.SetConnectedByRiver(b, false));
        Assert.True(a.SetConnectedByRiver(b, true, convertTerrains: true));
        Assert.Equal("Grassland", a.BaseTerrain);
        Assert.Equal("Grassland", b.BaseTerrain);
        Assert.False(a.SetConnectedByRiver(b, true, convertTerrains: true));
    }

    [Fact]
    public void TerrainOccursRangeMakesLowerLimitsInclusive()
    {
        var terrain = new Terrain { Name = "T", Type = TerrainType.Land, Uniques = ["Occurs at temperature between [-1] and [0.5] and humidity between [0] and [1]"] };
        var range = new GeneratorClass.TerrainOccursRange(terrain, ((Unciv.Models.Ruleset.Unique.IHasUniques)terrain).GetMatchingUniques(Unciv.Models.Ruleset.Unique.UniqueType.TileGenerationConditions).First());
        Assert.True(range.TempFrom < -1f);
        Assert.Equal(-1.0000001f, range.TempFrom);
        Assert.True(range.HumidFrom < 0f);
        Assert.Equal(0.5f, range.TempTo);
        Assert.True(range.IsConstrained);
    }
}
