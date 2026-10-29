using Unciv.Core.Tests.Models.Ruleset.Unique;
using Unciv.Logic.Map;
using Unciv.Logic.Map.MapGenerator;
using Unciv.Logic.Map.MapGenerator.Mapregions;
using Unciv.Logic.Map.MapGenerator.ResourcePlacement;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tech;
using Unciv.Models.Ruleset.Tile;

namespace Unciv.Core.Tests.Logic.Map.MapGenerator.ResourcePlacement;

// T077: MapRegionResources, LuxuryResourcePlacementLogic, StrategicBonusResourcePlacementLogic. The Kotlin fixture (ResourceDiversityTest) drives the whole
// MapGenerator pipeline (T041), so these tests exercise the placement logic directly on a small all-land map with fallback (weighting-less) rules.
public class ResourcePlacementTests
{
    private const int LuxuryCount = 9;

    private static Ruleset MakeRuleset()
    {
        var ruleset = new Ruleset();
        ruleset.Terrains.Put("Grassland", new Terrain
        {
            Name = "Grassland",
            Type = TerrainType.Land,
            Uniques = ["[2] to Fertility for Map Generation", "Considered [Food] when determining start locations", "Considered [Desirable] when determining start locations"],
        });
        ruleset.Eras.Put("Ancient", new Era { Name = "Ancient" });
        for (var i = 1; i <= LuxuryCount; i++)
            ruleset.TileResources.Put("Lux" + i, new TileResource { Name = "Lux" + i, ResourceType = ResourceType.Luxury, TerrainsCanBeFoundOn = ["Grassland"] });
        ruleset.TileResources.Put("Iron", new TileResource { Name = "Iron", ResourceType = ResourceType.Strategic, TerrainsCanBeFoundOn = ["Grassland"] });
        ruleset.TileResources.Put("Horses", new TileResource { Name = "Horses", ResourceType = ResourceType.Strategic, TerrainsCanBeFoundOn = ["Grassland"] });
        ruleset.TileResources.Put("Wheat", new TileResource { Name = "Wheat", ResourceType = ResourceType.Bonus, TerrainsCanBeFoundOn = ["Grassland"] });
        return ruleset;
    }

    private sealed record Scenario(TileMap Map, List<Region> Regions, TileDataMap TileData, Ruleset Ruleset);

    private static Scenario MakeScenario(int regionCount = 3, int radius = 12)
    {
        var ruleset = MakeRuleset();
        var map = new TileMap(radius, ruleset, false);
        map.MapParameters.Shape = MapShape.Hexagonal;
        map.MapParameters.MapSize = new MapSize(radius);
        map.GameInfo = new FakeGameInfo();
        var randomness = new MapGenerationRandomness();
        randomness.SeedRNG(1);
        new MapLandmassGenerator(map, ruleset, randomness).GenerateLand();
        map.AssignContinents(TileMap.AssignContinentsMode.Assign);

        var mapRegions = new MapRegions(ruleset);
        mapRegions.GenerateRegions(map, regionCount);
        var tileData = new TileDataMap(map.Values.Count);
        foreach (var tile in map.Values)
            tileData[tile] = new MapGenTileData(tile, mapRegions.Regions.First(it => it.Tiles.Contains(tile)), ruleset);
        foreach (var region in mapRegions.Regions)
        {
            region.CountTerrains();
            RegionStartFinder.FindStart(region, tileData);
        }
        return new Scenario(map, mapRegions.Regions.ToList(), tileData, ruleset);
    }

    private static int Count(Scenario scenario, string resource) => scenario.Map.Values.Count(it => it.Resource == resource);

    [Fact]
    public void TryAddingResourceToTilesHonoursRatioAndAmount()
    {
        var scenario = MakeScenario();
        var tiles = scenario.Map.Values.Take(10).ToList();
        var iron = scenario.Ruleset.TileResources["Iron"];

        var placed = MapRegionResources.TryAddingResourceToTiles(scenario.TileData, iron, 100, tiles, ratio: 0.5f);

        Assert.Equal(5, placed);
        Assert.Equal(new[] { 0, 2, 4, 6, 8 }, tiles.Select((tile, index) => (tile, index)).Where(it => it.tile.Resource == "Iron").Select(it => it.index));
        Assert.Equal(0, MapRegionResources.TryAddingResourceToTiles(scenario.TileData, iron, 0, tiles));
        Assert.Equal(2, MapRegionResources.TryAddingResourceToTiles(scenario.TileData, iron, 2, scenario.Map.Values.Skip(10).ToList()));
    }

    [Fact]
    public void TryAddingResourceToTilesRespectsImpactsAndPlacesImpact()
    {
        var scenario = MakeScenario();
        var tiles = scenario.Map.Values.Take(3).ToList();
        var wheat = scenario.Ruleset.TileResources["Wheat"];
        scenario.TileData[tiles[0]]!.Impacts[MapRegions.ImpactType.Bonus] = 5;

        var placed = MapRegionResources.TryAddingResourceToTiles(scenario.TileData, wheat, 1, tiles, respectImpacts: true, baseImpact: 1, randomImpact: 0);

        Assert.Equal(1, placed);
        Assert.Null(tiles[0].Resource);
        Assert.Equal("Wheat", tiles[1].Resource);
        Assert.Equal(99, scenario.TileData[tiles[1]]!.Impacts[MapRegions.ImpactType.Bonus]);
    }

    [Fact]
    public void PlaceResourcesInTilesPlacesOneEveryFrequencyTilesAndFallsBackToImpactedTiles()
    {
        var scenario = MakeScenario();
        var wheat = scenario.Ruleset.TileResources["Wheat"];
        var tiles = scenario.Map.Values.Take(20).ToList();

        var result = MapRegionResources.PlaceResourcesInTiles(scenario.TileData, 10, tiles, [wheat]);
        Assert.Equal(3, result[wheat]); // 20 / 10 + 1
        Assert.Equal(3, tiles.Count(it => it.Resource == "Wheat"));
        Assert.Empty(MapRegionResources.PlaceResourcesInTiles(scenario.TileData, 0, tiles, [wheat]));
        Assert.Empty(MapRegionResources.PlaceResourcesInTiles(scenario.TileData, 10, [], [wheat]));

        // Every remaining free tile carries a Bonus impact now only if it is near a placed one; mark all as impacted and the fallback pass must still place.
        var fresh = scenario.Map.Values.Skip(100).Take(20).ToList();
        foreach (var tile in fresh) scenario.TileData[tile]!.Impacts[MapRegions.ImpactType.Bonus] = 7;
        scenario.TileData[fresh[5]]!.Impacts[MapRegions.ImpactType.Bonus] = 1;
        var fallback = MapRegionResources.PlaceResourcesInTiles(scenario.TileData, 100, fresh, [wheat]);
        Assert.Equal(1, fallback[wheat]);
        Assert.Equal("Wheat", fresh[5].Resource);
    }

    [Fact]
    public void GeneratesNaturallyOnChecksTerrain()
    {
        var scenario = MakeScenario();
        var tile = scenario.Map.Values[0];
        Assert.True(scenario.Ruleset.TileResources["Iron"].GeneratesNaturallyOn(tile));
        var elsewhere = new TileResource { Name = "Fish", ResourceType = ResourceType.Bonus, TerrainsCanBeFoundOn = ["Ocean"] };
        Assert.False(elsewhere.GeneratesNaturallyOn(tile));
        var banned = new TileResource { Name = "Banned", TerrainsCanBeFoundOn = ["Grassland"], Uniques = ["Doesn't generate naturally"] };
        Assert.False(banned.GeneratesNaturallyOn(tile));
        var smaller = new TileResource { Name = "Small", TerrainsCanBeFoundOn = ["Grassland"], Uniques = ["Must not be on [1] largest landmasses"] };
        var larger = new TileResource { Name = "Large", TerrainsCanBeFoundOn = ["Grassland"], Uniques = ["Must be on [1] largest landmasses"] };
        Assert.NotEqual(smaller.GeneratesNaturallyOn(tile), larger.GeneratesNaturallyOn(tile)); // the tile is on the largest landmass or it is not, never both
        var cold = new TileResource { Name = "Cold", TerrainsCanBeFoundOn = ["Grassland"], Uniques = ["Occurs at temperature between [0.0] and [0.2] and humidity between [0.0] and [1.0]"] };
        tile.Temperature = 0.1;
        tile.Humidity = 0.5;
        Assert.True(cold.GeneratesNaturallyOn(tile));
        tile.Temperature = 0.9;
        Assert.False(cold.GeneratesNaturallyOn(tile));
        tile.Temperature = null;
        tile.Humidity = null;
    }

    [Fact]
    public void AssignLuxuriesGivesRegionsDistinctLuxuriesAndSplitsTheRest()
    {
        var scenario = MakeScenario();
        var (cityStateLuxuries, randomLuxuries) = LuxuryResourcePlacementLogic.AssignLuxuries(scenario.Regions, scenario.TileData, scenario.Ruleset);

        var regionLuxuries = scenario.Regions.Select(it => it.Luxury).ToList();
        Assert.All(regionLuxuries, Assert.NotNull);
        Assert.Equal(3, regionLuxuries.Distinct().Count()); // at most one region per luxury below 11 regions
        Assert.Equal(4, cityStateLuxuries.Count);
        Assert.Empty(cityStateLuxuries.Intersect(regionLuxuries!));
        Assert.Empty(randomLuxuries.Intersect(regionLuxuries!));
        Assert.Empty(randomLuxuries.Intersect(cityStateLuxuries));
        Assert.Equal(LuxuryCount, regionLuxuries.Count + cityStateLuxuries.Count + randomLuxuries.Count);
    }

    [Fact]
    public void AssignLuxuriesIsDeterministic()
    {
        var a = MakeScenario();
        var b = MakeScenario();
        var resultA = LuxuryResourcePlacementLogic.AssignLuxuries(a.Regions, a.TileData, a.Ruleset);
        var resultB = LuxuryResourcePlacementLogic.AssignLuxuries(b.Regions, b.TileData, b.Ruleset);
        Assert.Equal(resultA.CityStateLuxuries, resultB.CityStateLuxuries);
        Assert.Equal(resultA.RandomLuxuries, resultB.RandomLuxuries);
        Assert.Equal(a.Regions.Select(it => it.Luxury), b.Regions.Select(it => it.Luxury));
    }

    [Fact]
    public void PlaceLuxuriesPutsRegionLuxuryNextToEachStart()
    {
        var scenario = MakeScenario();
        var (cityStateLuxuries, randomLuxuries) = LuxuryResourcePlacementLogic.AssignLuxuries(scenario.Regions, scenario.TileData, scenario.Ruleset);

        LuxuryResourcePlacementLogic.PlaceLuxuries(scenario.Regions, scenario.Map, scenario.TileData, scenario.Ruleset, cityStateLuxuries, randomLuxuries);

        foreach (var region in scenario.Regions)
        {
            var near = scenario.Map[region.StartPosition!.Value].GetTilesInDistanceRange(new Unciv.Utils.IntRange(1, 3));
            Assert.Contains(near, it => it.Resource == region.Luxury);
        }
        Assert.True(scenario.Map.Values.Count(it => it.TileResource?.ResourceType == ResourceType.Luxury) >= 6);
    }

    [Fact]
    public void PlaceStrategicAndBonusesPlacesEveryStrategicAtLeastOncePerCivAndBonuses()
    {
        var scenario = MakeScenario();

        StrategicBonusResourcePlacementLogic.PlaceStrategicAndBonuses(scenario.Map, scenario.Regions, scenario.TileData);

        Assert.True(Count(scenario, "Iron") >= 3);
        Assert.True(Count(scenario, "Horses") >= 3);
        Assert.True(Count(scenario, "Wheat") >= 3);
        Assert.All(scenario.Map.Values.Where(it => it.Resource is "Iron" or "Horses"), it => Assert.True(it.ResourceAmount > 0));
    }

    [Fact]
    public void PlaceStrategicAndBonusesIsDeterministic()
    {
        var a = MakeScenario();
        var b = MakeScenario();
        StrategicBonusResourcePlacementLogic.PlaceStrategicAndBonuses(a.Map, a.Regions, a.TileData);
        StrategicBonusResourcePlacementLogic.PlaceStrategicAndBonuses(b.Map, b.Regions, b.TileData);
        Assert.Equal(a.Map.Values.Select(it => it.Resource), b.Map.Values.Select(it => it.Resource));
    }

    [Fact]
    public void GeneratesNaturallyOnHonoursTemperatureAndHumidityRangesInclusively()
    {
        var scenario = MakeScenario();
        var tile = scenario.Map.Values[0];
        var resource = new TileResource
        {
            Name = "Ranged",
            TerrainsCanBeFoundOn = ["Grassland"],
            Uniques = ["Occurs at temperature between [-0.5] and [0.5] and humidity between [0.2] and [0.8]"],
        };
        tile.Temperature = null;
        tile.Humidity = null;
        Assert.True(resource.GeneratesNaturallyOn(tile)); // only checked during map generation
        tile.Temperature = 0.5;
        tile.Humidity = 0.2;
        Assert.True(resource.GeneratesNaturallyOn(tile));
        tile.Humidity = 0.1;
        Assert.False(resource.GeneratesNaturallyOn(tile));
        tile.Humidity = 0.5;
        tile.Temperature = 0.6;
        Assert.False(resource.GeneratesNaturallyOn(tile));
    }

    [Fact]
    public void GeneratesNaturallyOnRejectsNaNTemperatureAndHumidity()
    {
        // Kotlin `x !in a..b` is true for NaN, so the resource does not generate
        var scenario = MakeScenario();
        var tile = scenario.Map.Values[0];
        var resource = new TileResource
        {
            Name = "Ranged",
            TerrainsCanBeFoundOn = ["Grassland"],
            Uniques = ["Occurs at temperature between [-0.5] and [0.5] and humidity between [0.2] and [0.8]"],
        };
        tile.Temperature = double.NaN;
        tile.Humidity = 0.5;
        Assert.False(resource.GeneratesNaturallyOn(tile));
        tile.Temperature = 0.0;
        tile.Humidity = double.NaN;
        Assert.False(resource.GeneratesNaturallyOn(tile));
    }

    [Fact]
    public void PlaceMajorDepositsUsesFallbackWeightingsAndTerrainFrequency()
    {
        var scenario = MakeScenario();
        var terrain = scenario.Ruleset.Terrains["Grassland"];
        var tiles = scenario.Map.Values.Take(60).ToList();

        var withoutFallback = MapRegionResources.PlaceMajorDeposits(scenario.TileData, scenario.Ruleset, tiles, terrain, false, 2, 2);
        Assert.Empty(withoutFallback); // no strategic resource carries a matching rule
        var placed = MapRegionResources.PlaceMajorDeposits(scenario.TileData, scenario.Ruleset, tiles, terrain, true, 2, 2);
        Assert.Equal(60 / 25 + 1, placed.Values.Sum()); // default frequency 25
    }

    [Fact]
    public void ShuffledSequenceReshufflesOnEveryEnumeration()
    {
        var scenario = MakeScenario();
        var sequence = scenario.Map.Values.ShuffledSequence(new Unciv.Utils.GameRandom(7));
        Assert.NotEqual(sequence.ToList(), sequence.ToList());
    }
}
