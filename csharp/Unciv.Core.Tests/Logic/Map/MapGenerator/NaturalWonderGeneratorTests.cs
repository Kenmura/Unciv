using Unciv.Logic.Map;
using Unciv.Logic.Map.MapGenerator;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Core.Tests.Logic.Map.MapGenerator;

// T074: NaturalWonderGeneratorTests.kt (TestGame is T072; the map and Rock of Gibraltar are built by hand) plus RiverGenerator coverage.
public class NaturalWonderGeneratorTests
{
    private readonly Ruleset ruleset;
    private readonly TileMap map;
    private readonly Terrain wonder;
    private readonly TileClass location;

    public NaturalWonderGeneratorTests()
    {
        ruleset = new Ruleset();
        void Add(string name, TerrainType type, string[]? uniques = null, Action<Terrain>? init = null)
        {
            var terrain = new Terrain { Name = name, Type = type, Uniques = [.. uniques ?? []] };
            init?.Invoke(terrain);
            ruleset.Terrains.Put(name, terrain);
        }
        Add("Grassland", TerrainType.Land);
        Add("Mountain", TerrainType.Land, ["Rough terrain"], it => it.Impassable = true);
        Add("Coast", TerrainType.Water, ["Coastal Water"]);
        Add("Ocean", TerrainType.Water);
        Add("Lakes", TerrainType.Water, ["Fresh water"]);
        Add("Forest", TerrainType.TerrainFeature, null, it => it.OccursOn = ["Grassland"]);
        Add("Rock of Gibraltar", TerrainType.NaturalWonder,
            ["Must be adjacent to [1] to [5] [Coast] tiles", "Must be adjacent to [1] [Mountain] tiles",
                "Neighboring tiles will convert to [Coast] <in tiles without [Mountain]>"],
            it => { it.TurnsInto = "Mountain"; it.Impassable = true; it.OccursOn = ["Grassland"]; });
        ruleset.TileResources.Put("Deer", new TileResource { Name = "Deer", ResourceType = ResourceType.Bonus });
        ruleset.TileImprovements.Put("Camp", new TileImprovement { Name = "Camp" });
        map = new TileMap(3, ruleset, false);
        foreach (var tile in map.Values) SetTerrain(tile, "Grassland");
        wonder = ruleset.Terrains["Rock of Gibraltar"];
        location = Tile(0, 0);
    }

    private TileClass Tile(int x, int y) => map[x, y];

    private static void SetTerrain(TileClass tile, string baseTerrain)
    {
        tile.BaseTerrain = baseTerrain;
        tile.SetTerrainTransients();
    }

    [Fact]
    public void RockOfGibraltarConvertsConnectedLakesOutsideItsNeighborRingToCoast()
    {
        var convertedNeighbor = Tile(1, 0);
        var lakeOutside = Tile(2, 0);
        var end = Tile(3, 0);
        foreach (var tile in new[] { convertedNeighbor, lakeOutside, end }) SetTerrain(tile, "Lakes");

        NaturalWonderGenerator.PlaceNaturalWonder(wonder, location);

        foreach (var tile in new[] { convertedNeighbor, lakeOutside, end })
        {
            Assert.Equal("Coast", tile.BaseTerrain);
            Assert.DoesNotContain(tile.Neighbors, it => ((IHasUniques)it.GetBaseTerrain()).HasUnique(UniqueType.FreshWater));
        }
    }

    [Fact]
    public void RockOfGibraltarPreservesFiltersAndExistingConversionClearingBehavior()
    {
        var land = Tile(1, 0);
        var existingCoast = Tile(0, 1);
        var ocean = Tile(-1, 0);
        var mountain = Tile(0, -1);
        var unrelatedLake = Tile(-3, -3);
        SetTerrain(existingCoast, "Coast");
        SetTerrain(ocean, "Ocean");
        SetTerrain(mountain, "Mountain");
        SetTerrain(unrelatedLake, "Lakes");
        land.SetTerrainFeatures(["Forest"]);
        land.TileResource = ruleset.TileResources["Deer"];
        land.SetImprovementBasic("Camp");

        NaturalWonderGenerator.PlaceNaturalWonder(wonder, location);

        Assert.Equal("Coast", land.BaseTerrain);
        Assert.True(land.IsWater);
        Assert.Empty(land.TerrainFeatures);
        Assert.Null(land.TileResource);
        Assert.Null(land.Improvement);
        Assert.Equal("Coast", existingCoast.BaseTerrain);
        Assert.Equal("Coast", ocean.BaseTerrain);
        Assert.Equal("Mountain", mountain.BaseTerrain);
        Assert.Equal("Lakes", unrelatedLake.BaseTerrain);
    }

    [Fact]
    public void RockOfGibraltarRemovesEveryRiverEdgeFromAllTilesConvertedToCoast()
    {
        var convertedNeighbor = Tile(1, 0);
        var lakeOutside = Tile(2, 0);
        SetTerrain(convertedNeighbor, "Lakes");
        SetTerrain(lakeOutside, "Lakes");
        foreach (var tile in new[] { convertedNeighbor, lakeOutside })
            foreach (var neighbor in tile.Neighbors)
                tile.SetConnectedByRiver(neighbor, true);

        NaturalWonderGenerator.PlaceNaturalWonder(wonder, location);

        foreach (var tile in new[] { convertedNeighbor, lakeOutside })
        {
            Assert.Equal("Coast", tile.BaseTerrain);
            Assert.DoesNotContain(tile.Neighbors, it => tile.IsConnectedByRiver(it));
        }
    }

    [Fact]
    public void FitsTerrainUniquesChecksNeighborCounts()
    {
        // location (0,0) has no Coast and no Mountain neighbors -> Rock of Gibraltar does not fit
        Assert.False(NaturalWonderGenerator.FitsTerrainUniques(wonder, location));
        SetTerrain(Tile(1, 0), "Coast");
        SetTerrain(Tile(0, 1), "Mountain");
        Assert.True(NaturalWonderGenerator.FitsTerrainUniques(wonder, location));
    }

    [Fact]
    public void SpawnNaturalWondersIsDeterministicAndHonoursNoNaturalWonders()
    {
        SetTerrain(Tile(1, 0), "Coast");
        SetTerrain(Tile(0, 1), "Mountain");
        map.MapParameters.NoNaturalWonders = true;
        new NaturalWonderGenerator(ruleset, new MapGenerationRandomness()).SpawnNaturalWonders(map);
        Assert.All(map.Values, it => Assert.Null(it.NaturalWonder));

        map.MapParameters.NoNaturalWonders = false;
        map.MapParameters.MapSize = new MapSize(10);
        var randomness = new MapGenerationRandomness();
        randomness.SeedRNG(7);
        new NaturalWonderGenerator(ruleset, randomness).SpawnNaturalWonders(map);
        var placed = map.Values.Where(it => it.NaturalWonder != null).ToList();
        Assert.Single(placed);
        Assert.Equal("Mountain", placed[0].BaseTerrain);
    }
}

public class RiverGeneratorTests
{
    private static (TileMap map, Ruleset ruleset) LandMapWithSea()
    {
        var ruleset = new Ruleset();
        ruleset.Terrains.Put("Grassland", new Terrain { Name = "Grassland", Type = TerrainType.Land });
        ruleset.Terrains.Put("Ocean", new Terrain { Name = "Ocean", Type = TerrainType.Water });
        var map = new TileMap(6, ruleset, false);
        map.MapParameters.MapSize = new MapSize(6);
        foreach (var tile in map.Values)
        {
            tile.BaseTerrain = tile.AerialDistanceTo(map[0, 0]) >= 6 ? "Ocean" : "Grassland";
            tile.SetTerrainTransients();
        }
        return (map, ruleset);
    }

    private static string RiverFingerprint(TileMap map) =>
        string.Join(',', map.Values.Select(it => (it.HasBottomRiver ? 1 : 0) + (it.HasBottomLeftRiver ? 2 : 0) + (it.HasBottomRightRiver ? 4 : 0)));

    [Fact]
    public void SpawnRiversIsDeterministicPerSeedAndPaintsRivers()
    {
        var results = new List<string>();
        for (int i = 0; i < 2; i++)
        {
            var (map, ruleset) = LandMapWithSea();
            var randomness = new MapGenerationRandomness();
            randomness.SeedRNG(3);
            var affected = new HashSet<TileClass>();
            new RiverGenerator(map, randomness, ruleset).SpawnRivers(affected);
            results.Add(RiverFingerprint(map));
            Assert.NotEmpty(affected);
        }
        Assert.Equal(results[0], results[1]);
        Assert.Contains(results[0].Split(',').Select(int.Parse), it => it != 0);
    }

    [Fact]
    public void SpawnRiversDoesNothingWithoutWater()
    {
        var (map, ruleset) = LandMapWithSea();
        foreach (var tile in map.Values) { tile.BaseTerrain = "Grassland"; tile.SetTerrainTransients(); }
        new RiverGenerator(map, new MapGenerationRandomness(), ruleset).SpawnRivers();
        Assert.All(map.Values, it => Assert.False(it.HasBottomRiver || it.HasBottomLeftRiver || it.HasBottomRightRiver));
    }

    [Fact]
    public void ContinueRiverOnPlacesAnEdgeAndRefusesWater()
    {
        var (map, _) = LandMapWithSea();
        var tile = map[0, 0];
        Assert.True(RiverGenerator.ContinueRiverOn(tile));
        Assert.Contains(tile.Neighbors, it => tile.IsConnectedByRiver(it));
        var water = map.Values.First(it => it.IsWater);
        Assert.False(RiverGenerator.ContinueRiverOn(water));
    }

    [Fact]
    public void RiverCoordinateAdjacentPositionsAlternateSides()
    {
        var (map, _) = LandMapWithSea();
        var coordinate = new RiverGenerator.RiverCoordinate(map, new HexCoord(0, 0), RiverGenerator.RiverCoordinate.BottomRightOrLeft.BottomLeft);
        Assert.Equal(3, coordinate.GetAdjacentPositions().Count());
        Assert.Equal(3, coordinate.GetAdjacentTiles().Count());
        Assert.Equal(0, coordinate.NumberOfConnectedRivers());
    }
}
