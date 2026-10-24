using Unciv.Logic.Map;
using Unciv.Logic.Map.MapGenerator;
using Unciv.Logic.Map.MapGenerator.Mapregions;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tile;

namespace Unciv.Core.Tests.Logic.Map.MapGenerator.Mapregions;

// T075: Region, MapGenTileData, MapRegions (region division), RegionStartFinder. There are no Kotlin tests for these classes (MapGenerationTests.kt
// drives the whole pipeline, T041). assignRegions needs StartNormalizer (T076), so it is not exercised here.
public class MapRegionsTests
{
    private static Ruleset LandRuleset()
    {
        var ruleset = new Ruleset();
        ruleset.Terrains.Put("Grassland", new Terrain
        {
            Name = "Grassland",
            Type = TerrainType.Land,
            Uniques = ["[2] to Fertility for Map Generation", "Considered [Food] when determining start locations", "Considered [Desirable] when determining start locations"],
        });
        return ruleset;
    }

    private static TileMap LandMap(Ruleset ruleset, int radius = 12)
    {
        var map = new TileMap(radius, ruleset, false);
        map.MapParameters.Shape = MapShape.Hexagonal;
        map.MapParameters.MapSize = new MapSize(radius);
        var randomness = new MapGenerationRandomness();
        randomness.SeedRNG(1);
        new MapLandmassGenerator(map, ruleset, randomness).GenerateLand();
        map.AssignContinents(TileMap.AssignContinentsMode.Assign);
        return map;
    }

    [Fact]
    public void TileFertilitySumsUniques()
    {
        var map = LandMap(LandRuleset());
        Assert.All(map.Values, it => Assert.Equal(2, it.GetTileFertility(true)));
    }

    [Fact]
    public void GenerateRegionsDividesContinentIntoRequestedRegions()
    {
        var map = LandMap(LandRuleset());
        var mapRegions = new MapRegions(map.Ruleset!);
        mapRegions.GenerateRegions(map, 4);
        Assert.Equal(4, mapRegions.Regions.Count);
        // Kotlin quirk kept: HexMath.getRow truncates towards zero, so regions may share a row of tiles at negative rows
        Assert.All(map.Values, tile => Assert.Contains(mapRegions.Regions, region => region.Tiles.Contains(tile)));
        var fertilities = mapRegions.Regions.Select(it => it.TotalFertility).ToList();
        Assert.True(fertilities.Max() - fertilities.Min() <= fertilities.Sum() / 4);
    }

    [Fact]
    public void GenerateRegionsIsDeterministicAndIgnoresNonPositiveCounts()
    {
        var map = LandMap(LandRuleset());
        var a = new MapRegions(map.Ruleset!);
        var b = new MapRegions(map.Ruleset!);
        a.GenerateRegions(map, 0);
        Assert.Empty(a.Regions);
        a.GenerateRegions(map, 3);
        b.GenerateRegions(map, 3);
        Assert.Equal(a.Regions.Select(it => it.Rect), b.Regions.Select(it => it.Rect));
    }

    [Fact]
    public void RegionStartFinderPicksLandStartAndPenalisesNeighbours()
    {
        var map = LandMap(LandRuleset());
        var mapRegions = new MapRegions(map.Ruleset!);
        mapRegions.GenerateRegions(map, 2);
        var tileData = new TileDataMap(map.Values.Count);
        foreach (var tile in map.Values) tileData[tile] = new MapGenTileData(tile, mapRegions.Regions.First(it => it.Tiles.Contains(tile)), map.Ruleset!);
        foreach (var region in mapRegions.Regions) RegionStartFinder.FindStart(region, tileData);
        Assert.All(mapRegions.Regions, it => Assert.NotNull(it.StartPosition));
        var start = map[mapRegions.Regions[0].StartPosition!.Value];
        Assert.True(tileData[start]!.CloseStartPenalty >= 99);
    }

    [Fact]
    public void MapGenTileDataQualitiesAndClosePenalty()
    {
        var map = LandMap(LandRuleset());
        var data = new MapGenTileData(map.Values[0], null, map.Ruleset!);
        Assert.True(data.IsFood);
        Assert.True(data.IsGood);
        Assert.False(data.IsJunk);
        data.AddCloseStartPenalty(50);
        Assert.Equal(50, data.CloseStartPenalty);
        data.AddCloseStartPenalty(60);
        Assert.Equal(72, data.CloseStartPenalty);
        data.AddCloseStartPenalty(97);
        Assert.Equal(97, data.CloseStartPenalty);
    }

    [Fact]
    public void PlaceImpactFollowsKotlinFormula()
    {
        var map = LandMap(LandRuleset());
        var data = new TileDataMap(map.Values.Count);
        foreach (var tile in map.Values) data[tile] = new MapGenTileData(tile, null, map.Ruleset!);
        var center = map[HexCoord.Of(0, 0)];
        data.PlaceImpact(MapRegions.ImpactType.Luxury, center, 2);
        Assert.Equal(99, data[center]!.Impacts[MapRegions.ImpactType.Luxury]);
        Assert.Equal(2, data[center.Neighbors[0]]!.Impacts[MapRegions.ImpactType.Luxury]);
        data.PlaceImpact(MapRegions.ImpactType.Luxury, center.Neighbors[0], 1);
        Assert.Equal(50, data[center]!.Impacts[MapRegions.ImpactType.Luxury]);
    }

    [Fact]
    public void RegionPriorityAndRulesHelpers()
    {
        Assert.Equal(99999, MapRegionFunctions.GetRegionPriority(null));
        var terrain = new Terrain { Name = "Tundra", Type = TerrainType.Land, Uniques = ["A Region is formed with at least [30]% [Tundra] tiles, with priority [3]"] };
        Assert.Equal(3, MapRegionFunctions.GetRegionPriority(terrain));
        Assert.Null(MapRegionFunctions.GetRegionPriority(new Terrain { Name = "X", Type = TerrainType.Land }));
        Assert.Equal("RULE <in [Featureless] [Tundra] tiles>", MapRegionFunctions.GetTerrainRule(terrain, new Ruleset()).Text);
    }

    [Fact]
    public void NegativeBiasKeyIgnoresAvoidedTerrainLikeKotlin()
    {
        var map = LandMap(LandRuleset());
        var region = new Region(map, new Unciv.Mathematics.Rectangle(0, 0, 1, 1));
        region.TerrainCounts["Desert"] = 5;
        region.TerrainCounts["Plains"] = 3;
        Assert.Equal(-3, MapRegions.NegativeBiasKey(region, ["Plains"]));
        Assert.Equal(0, MapRegions.NegativeBiasKey(region, ["Tundra"]));
    }
}
