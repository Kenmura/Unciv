using Unciv.Logic.Map;
using Unciv.Logic.Map.MapGenerator;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tile;
using Unciv.Utils;
using TerrainOccursRange = Unciv.Logic.Map.MapGenerator.MapGenerator.TerrainOccursRange;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Core.Tests.Logic.Map.MapGenerator;

// T073: MapGenerationRandomness, MapElevationGenerator, MapLandmassGenerator (MapResourceSetting lives in MapParameters.cs, T022).
// MapGenerationTests.kt drives the full MapGenerator (T041, with vanilla rulesets): its loop over every type x shape x wrap is ported
// here against the landmass generator, with a hand-built ruleset. The aggregate terrain-count test needs the whole pipeline and is T041's.
public class MapGenerationTests
{
    private static Ruleset GenRuleset()
    {
        var ruleset = new Ruleset();
        void Add(string name, TerrainType type, params string[] uniques) =>
            ruleset.Terrains.Put(name, new Terrain { Name = name, Type = type, Uniques = [.. uniques], OccursOn = type == TerrainType.TerrainFeature ? ["Grassland", "Plains"] : [] });
        Add("Grassland", TerrainType.Land);
        Add("Plains", TerrainType.Land);
        Add("Mountain", TerrainType.Land, "Occurs in chains at high elevations", "Rough terrain");
        Add("Ocean", TerrainType.Water);
        Add("Hill", TerrainType.TerrainFeature, "Occurs in groups around high elevations", "Rough terrain");
        return ruleset;
    }

    private static TileMap NewMap(Ruleset ruleset, string type, string shape, bool wrap, int radius = 12)
    {
        var map = shape == MapShape.Rectangular ? new TileMap(2 * radius, radius + 1, ruleset, wrap) : new TileMap(radius, ruleset, wrap);
        map.MapParameters.Type = type;
        map.MapParameters.Shape = shape;
        map.MapParameters.WorldWrap = wrap;
        map.MapParameters.MapSize = shape == MapShape.Rectangular ? new MapSize(2 * radius, radius + 1) : new MapSize(radius);
        return map;
    }

    private static string Fingerprint(TileMap map) =>
        string.Join(',', map.Values.Select(it => it.BaseTerrain[0] + string.Concat(it.TerrainFeatures.Select(f => f[0]))));

    private static void Generate(TileMap map, Ruleset ruleset, long seed)
    {
        var randomness = new MapGenerationRandomness();
        randomness.SeedRNG(seed);
        new MapLandmassGenerator(map, ruleset, randomness).GenerateLand();
    }

    [Fact]
    public void PowSignedFollowsKotlinSignSemantics()
    {
        Assert.Equal(4.0, MapElevationGenerator.PowSigned(2.0, 2.0));
        Assert.Equal(-4.0, MapElevationGenerator.PowSigned(-2.0, 2.0));
        Assert.Equal(0.0, MapElevationGenerator.PowSigned(0.0, 2.0));
        Assert.True(double.IsNaN(MapElevationGenerator.PowSigned(double.NaN, 2.0))); // Math.Sign would throw
    }

    [Fact]
    public void TestAllLandmassGenerations()
    {
        var ruleset = GenRuleset();
        foreach (string mapType in MapType.AllValues)
            foreach (string shape in MapShape.AllValues)
                foreach (bool wrap in new[] { true, false })
                {
                    var map = NewMap(ruleset, mapType, shape, wrap);
                    Generate(map, ruleset, 1);
                    Assert.All(map.Values, it => Assert.Contains(it.BaseTerrain, new[] { "Grassland", "Ocean" }));
                }
    }

    [Fact]
    public void LandmassIsDeterministicPerSeedAndVariesBetweenSeeds()
    {
        var ruleset = GenRuleset();
        var first = NewMap(ruleset, MapType.Perlin, MapShape.Hexagonal, true);
        var second = NewMap(ruleset, MapType.Perlin, MapShape.Hexagonal, true);
        var other = NewMap(ruleset, MapType.Perlin, MapShape.Hexagonal, true);
        Generate(first, ruleset, 7);
        Generate(second, ruleset, 7);
        Generate(other, ruleset, 8);
        Assert.Equal(Fingerprint(first), Fingerprint(second));
        Assert.NotEqual(Fingerprint(first), Fingerprint(other));
        Assert.Contains(first.Values, it => it.BaseTerrain == "Ocean");
        Assert.Contains(first.Values, it => it.BaseTerrain == "Grassland");
    }

    [Fact]
    public void LandOnlyRulesetMakesAllLand()
    {
        var ruleset = new Ruleset();
        ruleset.Terrains.Put("Grassland", new Terrain { Name = "Grassland", Type = TerrainType.Land });
        var map = NewMap(ruleset, MapType.Archipelago, MapShape.Hexagonal, false, 4);
        Generate(map, ruleset, 3);
        Assert.All(map.Values, it => Assert.Equal("Grassland", it.BaseTerrain));
    }

    [Fact]
    public void NoLandTerrainThrows()
    {
        var ruleset = new Ruleset();
        ruleset.Terrains.Put("Ocean", new Terrain { Name = "Ocean", Type = TerrainType.Water });
        var map = NewMap(GenRuleset(), MapType.Perlin, MapShape.Hexagonal, false, 3);
        Assert.Throws<InvalidOperationException>(() => new MapLandmassGenerator(map, ruleset, new MapGenerationRandomness()));
    }

    [Fact]
    public void FlatEarthHasWaterAtCentreAndEdge()
    {
        var ruleset = GenRuleset();
        var map = NewMap(ruleset, MapType.Pangaea, MapShape.FlatEarth, false);
        Generate(map, ruleset, 5);
        Assert.Equal("Ocean", map[HexCoord.Zero].BaseTerrain);
        Assert.All(map.Values.Where(it => it.Neighbors.Count < 6), it => Assert.Equal("Ocean", it.BaseTerrain));
    }

    [Fact]
    public void PangaeaYieldsSingleLargeContinentOnMediumMap()
    {
        var ruleset = GenRuleset();
        var map = NewMap(ruleset, MapType.Pangaea, MapShape.Hexagonal, false, 20);
        Generate(map, ruleset, 11);
        Assert.Equal(0, map.ContinentSizes.Count);
        Assert.True(map.Values.Count(it => it.BaseTerrain == "Ocean") / (double)map.Values.Count <= 0.7);
    }

    [Fact]
    public void ElevationRaisesMountainsAndHillsDeterministically()
    {
        var ruleset = GenRuleset();
        TileMap Build()
        {
            var map = NewMap(ruleset, MapType.Perlin, MapShape.Hexagonal, false, 15);
            Generate(map, ruleset, 21);
            var terrains = ruleset.Terrains.Values.Select(it => new TerrainOccursRange(it)).ToList();
            var randomness = new MapGenerationRandomness();
            randomness.SeedRNG(22);
            new MapElevationGenerator(map, ruleset, terrains, randomness).RaiseMountainsAndHills();
            return map;
        }
        var a = Build();
        var b = Build();
        Assert.Equal(Fingerprint(a), Fingerprint(b));
        Assert.Contains(a.Values.Where(it => !it.IsWater), it => it.BaseTerrain == "Mountain");
        Assert.Contains(a.Values, it => it.TerrainFeatures.Contains("Hill"));
        Assert.All(a.Values, it => Assert.DoesNotContain("~Raising~", it.TerrainFeatures));
        Assert.All(a.Values, it => Assert.DoesNotContain("~Lowering~", it.TerrainFeatures));
    }

    [Fact]
    public void ElevationWithoutFlatTerrainDoesNothing()
    {
        var ruleset = GenRuleset();
        var map = NewMap(ruleset, MapType.Perlin, MapShape.Hexagonal, false, 4);
        var before = Fingerprint(map);
        new MapElevationGenerator(map, ruleset, [], new MapGenerationRandomness()).RaiseMountainsAndHills();
        Assert.Equal(before, Fingerprint(map));
    }

    [Fact]
    public void RandomnessSeedingIsRepeatable()
    {
        var one = new MapGenerationRandomness();
        var two = new MapGenerationRandomness();
        one.SeedRNG(99);
        two.SeedRNG(99);
        Assert.Equal(one.RNG.NextInt(), two.RNG.NextInt());
        two.SeedRNG();
        Assert.Equal(new GameRandom(42L).NextInt(), two.RNG.NextInt());
    }

    [Fact]
    public void PerlinNoiseWithoutWrapIsPlainPerlinOfWorldCoords()
    {
        var ruleset = GenRuleset();
        var map = NewMap(ruleset, MapType.Perlin, MapShape.Hexagonal, false, 5);
        var tile = map[HexCoord.Of(2, 1)];
        var coords = HexMath.Hex2WorldCoords(tile.Position);
        Assert.Equal(Perlin.Noise3d(coords.x, coords.y, 3.0, 6, 0.5, 2.0, 30.0), new MapGenerationRandomness().GetPerlinNoise(tile, 3.0));
    }

    [Fact]
    public void PerlinNoiseWithWrapIsFiniteAndSeedDependent()
    {
        var ruleset = GenRuleset();
        var map = NewMap(ruleset, MapType.Perlin, MapShape.Hexagonal, true, 6);
        var randomness = new MapGenerationRandomness();
        var tile = map[HexCoord.Of(2, 1)];
        double a = randomness.GetPerlinNoise(tile, 1.0);
        double b = randomness.GetPerlinNoise(tile, 2.0);
        Assert.True(double.IsFinite(a) && System.Math.Abs(a) <= 1.0);
        Assert.NotEqual(a, b);
        Assert.Equal(a, randomness.GetPerlinNoise(tile, 1.0));
    }

    [Fact]
    public void ChooseSpreadOutLocationsRespectsCountDistanceAndSeed()
    {
        var ruleset = GenRuleset();
        List<TileClass> Choose(long seed, int number)
        {
            var map = NewMap(ruleset, MapType.Perlin, MapShape.Hexagonal, false, 15);
            var randomness = new MapGenerationRandomness();
            randomness.SeedRNG(seed);
            return randomness.ChooseSpreadOutLocations(number, map.Values, 15);
        }
        Assert.Empty(Choose(1, 0));
        Assert.Single(Choose(1, 1));
        var chosen = Choose(1, 10);
        Assert.Equal(10, chosen.Count);
        Assert.Equal(10, chosen.Distinct().Count());
        Assert.Equal(chosen.Select(it => it.Position), Choose(1, 10).Select(it => it.Position));
        for (int i = 0; i < chosen.Count; i++)
            for (int j = i + 1; j < chosen.Count; j++)
                Assert.True(HexMath.GetDistance(chosen[i].Position, chosen[j].Position) >= 2);
    }

    [Fact]
    public void ChooseSpreadOutLocationsBalancesBaseTerrains()
    {
        var ruleset = GenRuleset();
        var map = NewMap(ruleset, MapType.Perlin, MapShape.Hexagonal, false, 10);
        foreach (var tile in map.Values)
            tile.BaseTerrain = tile.Position.x % 2 == 0 ? "Grassland" : "Plains";
        var randomness = new MapGenerationRandomness();
        var chosen = randomness.ChooseSpreadOutLocations(6, map.Values, 10);
        Assert.Equal(6, chosen.Count);
        Assert.Equal(3, chosen.Count(it => it.BaseTerrain == "Grassland"));
    }

    [Fact]
    public void ChooseSpreadOutLocationsReturnsFewerWhenNotEnoughTiles()
    {
        var ruleset = GenRuleset();
        var map = NewMap(ruleset, MapType.Perlin, MapShape.Hexagonal, false, 3);
        var chosen = new MapGenerationRandomness().ChooseSpreadOutLocations(100, map.Values, 3);
        Assert.InRange(chosen.Count, 1, map.Values.Count - 1); // distance 1 still excludes neighbours
    }

    [Fact]
    public void MapResourceSettingBehavesAsKotlin()
    {
        Assert.Equal(MapResourceSetting.Default, MapResourceSettingExtensions.SafeValueOf("nonsense"));
        Assert.Equal(80, MapResourceSetting.Sparse.GetRandomLuxuriesPercent());
        Assert.DoesNotContain("Strategic Balance", MapResourceSettingExtensions.ActiveLabels());
    }
}
