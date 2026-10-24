using Unciv.Logic.Map;
using Unciv.Logic.Map.MapGenerator;
using Unciv.Logic.Map.MapGenerator.Mapregions;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tile;
using Unciv.Utils;

namespace Unciv.Core.Tests.Logic.Map.MapGenerator.Mapregions;

// T076: StartNormalizer and MinorCivPlacer. No Kotlin unit tests exist for them (MapGenerationTests.kt drives the whole pipeline, T041).
public class MinorCivPlacerTests
{
    private sealed class FakeCiv(string id, List<string>? bias = null) : IMapRegionsCivilization
    {
        public string CivName => id;
        public string CivId => id;
        public List<string> GetStartBias(Ruleset ruleset) => bias ?? [];
        public GameRandom StateBasedRandom(string caller) => new(id.GetHashCode() ^ caller.Length);
    }

    private static Ruleset LandRuleset()
    {
        var ruleset = new Ruleset();
        ruleset.Terrains.Put("Grassland", new Terrain
        {
            Name = "Grassland",
            Type = TerrainType.Land,
            Food = 2,
            Uniques = ["[2] to Fertility for Map Generation", "Considered [Food] when determining start locations", "Considered [Desirable] when determining start locations"],
        });
        return ruleset;
    }

    private static (TileMap map, MapRegions regions, TileDataMap data) Setup(int regionCount)
    {
        var ruleset = LandRuleset();
        var map = new TileMap(12, ruleset, false) { GameInfo = new Unciv.Core.Tests.Models.Ruleset.Unique.FakeGameInfo() };
        map.MapParameters.Shape = MapShape.Hexagonal;
        map.MapParameters.MapSize = new MapSize(12);
        var randomness = new MapGenerationRandomness();
        randomness.SeedRNG(1);
        new MapLandmassGenerator(map, ruleset, randomness).GenerateLand();
        map.AssignContinents(TileMap.AssignContinentsMode.Assign);
        var mapRegions = new MapRegions(ruleset);
        mapRegions.GenerateRegions(map, regionCount);
        var data = new TileDataMap(map.Values.Count);
        foreach (var tile in map.Values)
            data[tile] = new MapGenTileData(tile, mapRegions.Regions.First(it => it.Tiles.Contains(tile)), ruleset);
        return (map, mapRegions, data);
    }

    [Fact]
    public void FilterTilesByStartBiasKeepsMatchingAndDropsAvoided()
    {
        var (map, _, _) = Setup(2);
        var tiles = map.Values.ToList();
        Assert.Equal(tiles.Count, MinorCivPlacer.FilterTilesByStartBias(tiles, []).Count);
        Assert.Equal(tiles.Count, MinorCivPlacer.FilterTilesByStartBias(tiles, ["Grassland"]).Count);
        Assert.Empty(MinorCivPlacer.FilterTilesByStartBias(tiles, ["Coast"]));
        Assert.Empty(MinorCivPlacer.FilterTilesByStartBias(tiles, ["Avoid [Grassland]"]));
    }

    [Fact]
    public void PrefersCoastalStartReadsStartBias()
    {
        var ruleset = LandRuleset();
        Assert.True(MinorCivPlacer.PrefersCoastalStart(new FakeCiv("a", ["Coast"]), ruleset));
        Assert.False(MinorCivPlacer.PrefersCoastalStart(new FakeCiv("b"), ruleset));
    }

    [Fact]
    public void PlaceMinorCivsAssignsAndPlacesEveryCivOnDistinctTiles()
    {
        var (map, regions, data) = Setup(2);
        foreach (var region in regions.Regions) region.Luxury = "Dyes";
        var civs = Enumerable.Range(0, 3).Select(i => (IMapRegionsCivilization)new FakeCiv("cs" + i)).ToList();
        MinorCivPlacer.PlaceMinorCivs(regions.Regions.ToList(), map, civs, data, map.Ruleset!);
        var locations = map.StartingLocations.Where(it => civs.Any(c => c.CivId == it.Nation)).ToList();
        Assert.Equal(3, locations.Count);
        Assert.Equal(3, locations.Select(it => it.Position).Distinct().Count());
        Assert.All(locations, it => Assert.True(data[map[it.Position]]!.Impacts.ContainsKey(MapRegions.ImpactType.MinorCiv)));
    }

    [Fact]
    public void PlaceMinorCivsWithNoCivsDoesNothing()
    {
        var (map, regions, data) = Setup(2);
        MinorCivPlacer.PlaceMinorCivs(regions.Regions.ToList(), map, [], data, map.Ruleset!);
        Assert.Empty(map.StartingLocations);
    }

    [Fact]
    public void NormalizeStartForMinorCivWithoutBonusResourcesLeavesPlainStartAlone()
    {
        var (map, _, data) = Setup(1);
        var start = map.Values.First(it => it.Neighbors.Count == 6 && it.AerialDistanceTo(map.Values.First()) > 3);
        StartNormalizer.NormalizeStart(start, map, data, map.Ruleset!, isMinorCiv: true);
        Assert.All(start.Neighbors, it => Assert.Equal("Grassland", it.BaseTerrain));
    }
}
