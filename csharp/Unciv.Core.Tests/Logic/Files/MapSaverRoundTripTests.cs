using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Unciv.Json;
using Unciv.Logic.Files;
using Unciv.Logic.Map;
using Unciv.Logic.Map.Tiles;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;

namespace Unciv.Core.Tests.Logic.Files;

// T107: the TileMap/Tile/TileMap.Preview JSON wiring. Logic/Map/Golden/MapSaver.golden.json was produced by the real Kotlin
// MapSaver.mapToSavedString (saveZipped = false) on a 3x2 TileMap built from Civ_V_GnK with a throwaway GdxTestRunner test
// (TileMap(3, 2, ruleset, false), then fields set as in GoldenParsesToTheExpectedFields, setTransients, mapToSavedString).
public class MapSaverRoundTripTests
{
    private static string GoldenText() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Logic", "Map", "Golden", "MapSaver.golden.json"));

    private static Ruleset GenRuleset()
    {
        var ruleset = new Ruleset();
        foreach (var (name, type) in new[] { ("Grassland", TerrainType.Land), ("Plains", TerrainType.Land), ("Ocean", TerrainType.Water) })
            ruleset.Terrains.Put(name, new Terrain { Name = name, Type = type });
        return ruleset;
    }

    private static TileMap ParseGolden() => MapSaver.MapFromSavedString(GoldenText());

    [Fact]
    public void GoldenParsesToTheExpectedFields()
    {
        var map = ParseGolden();
        Assert.Equal("Golden", map.MapParameters.Name);
        Assert.Equal(MapShape.Rectangular, map.MapParameters.Shape);
        Assert.Equal(3, map.MapParameters.MapSize.Width);
        Assert.Equal(2, map.MapParameters.MapSize.Height);
        Assert.True(map.MapParameters.NoRuins);
        Assert.Equal(["Foo"], map.MapParameters.Mods);
        Assert.Equal(1234567890123L, map.MapParameters.Seed);
        Assert.Equal("A golden map", map.Description);
        Assert.Equal(6, map.TileList.Count);

        var tiles = map.TileList;
        Assert.Equal(new HexCoord(0, -1), tiles[0].Position);
        Assert.Equal("Ocean", tiles[0].BaseTerrain);
        Assert.Equal(["Forest"], tiles[1].TerrainFeatures);
        Assert.Equal("Horses", tiles[1].Resource);
        Assert.Equal(2, tiles[1].ResourceAmount);
        Assert.Equal(["Hill", "Forest"], tiles[2].TerrainFeatures);
        Assert.True(tiles[2].HasBottomRiver && tiles[2].HasBottomLeftRiver && tiles[2].HasBottomRightRiver);
        Assert.Equal(RoadStatus.Railroad, tiles[2].RoadStatus);
        Assert.True(tiles[2].RoadIsPillaged);
        Assert.Equal("Farm", tiles[3].Improvement);
        Assert.True(tiles[3].ImprovementIsPillaged);
        Assert.Equal([("Road", 3), ("Mine", 0)], tiles[3].ImprovementQueue.Select(it => (it.Improvement, it.TurnsToImprovement)));
        Assert.Equal("Mount Fuji", tiles[4].NaturalWonder);
        Assert.Equal(RoadStatus.Road, tiles[4].RoadStatus);
        Assert.Equal([(3, "Rome", true), (10, (string?)null, false)],
            tiles[4].History.Select(it => (it.Key, it.Value.OwningCivName, it.Value.CityCenterTypeValue == TileHistory.CityCenterType.Capital)));

        Assert.Equal([new TileMap.StartingLocation(new HexCoord(1, 0), "Rome", TileMap.StartingLocation.UsageType.Human),
            new TileMap.StartingLocation(new HexCoord(-1, -1), "Greece"),
            new TileMap.StartingLocation(default, "Egypt", TileMap.StartingLocation.UsageType.Normal)], map.StartingLocations);
    }

    [Fact]
    public void GoldenReserializesToTheKotlinTileListByteForByte()
    {
        var golden = JsonNode.Parse(GoldenText())!;
        var again = JsonNode.Parse(UncivJson.Serialize(ParseGolden()))!;
        // Same field names, same default omission, same order. (mapParameters is MapParameters' own wiring, not compared here.)
        Assert.Equal(golden["tileList"]!.ToJsonString(), again["tileList"]!.ToJsonString());
        Assert.Equal(golden["startingLocations"]!.ToJsonString(), again["startingLocations"]!.ToJsonString());
        Assert.Equal(golden["description"]!.ToJsonString(), again["description"]!.ToJsonString());
    }

    [Fact]
    public void MapToSavedStringRoundTripsZippedAndPlain()
    {
        var ruleset = GenRuleset();
        var map = new TileMap(2, ruleset, false);
        map.MapParameters.Name = "Rt";
        map.Description = "d";
        map.TileList[1].BaseTerrain = "Ocean";
        map.TileList[2].SetTerrainTransients();
        map.StartingLocations.Add(new TileMap.StartingLocation(map.TileList[0].Position, "Rome", TileMap.StartingLocation.UsageType.Human));

        var zipped = MapSaver.MapToSavedString(map);
        var plain = FileConversions.Unzip(zipped);
        Assert.StartsWith("{", plain);
        var fromZipped = MapSaver.MapFromSavedString(zipped);
        var fromPlain = MapSaver.MapFromSavedString("  " + plain + "\n");
        Assert.Equal(plain, UncivJson.Serialize(fromZipped));
        Assert.Equal(plain, UncivJson.Serialize(fromPlain));
        Assert.Equal(map.TileList.Count, fromZipped.TileList.Count);
        Assert.Equal(map.StartingLocations, fromZipped.StartingLocations);
        Assert.Equal("Ocean", fromZipped.TileList[1].BaseTerrain);
        Assert.All(fromZipped.TileList, it => Assert.True(it.Position.Equals(map.TileList[fromZipped.TileList.IndexOf(it)].Position)));
        // The loaded map is usable after setTransients
        fromZipped.SetTransients(ruleset);
        Assert.Equal(map.TileList.Count, fromZipped.Values.Count(it => fromZipped[it.Position] == it));
    }

    [Fact]
    public void PreviewReadsParametersAndLocationsAndSkipsTheTiles()
    {
        var preview = UncivJson.Deserialize<TileMap.Preview>(GoldenText())!;
        Assert.Equal("Golden", preview.MapParameters.Name);
        Assert.Equal(["Rome", "Greece"], preview.GetDeclaredNations());
        Assert.Equal(["Rome"], preview.GetNationsForHumanPlayer());
        Assert.Equal(1234567890123L, preview.MapParameters.Seed);
    }

    [Fact]
    public void PreviewOfEmptyObjectHasDefaults()
    {
        var preview = UncivJson.Deserialize<TileMap.Preview>("{}")!;
        Assert.Equal("", preview.MapParameters.Name);
        Assert.Empty(preview.GetDeclaredNations());
    }

    [Fact]
    public void ListsMayUseTheGdxClassWrapper()
    {
        var tile = UncivJson.Deserialize<Tile>(
            "{\"baseTerrain\":\"Plains\",\"terrainFeatures\":{\"class\":\"java.util.Collections$SingletonList\",\"items\":[\"Forest\"]},\"exploredBy\":{\"class\":\"x\",\"items\":[\"B\",\"A\"]}}")!;
        Assert.Equal(["Forest"], tile.TerrainFeatures);
        // exploredBy is private; it is written back ordinally sorted
        Assert.Contains("\"exploredBy\":[\"A\",\"B\"]", UncivJson.Serialize(tile));
    }

    [Fact]
    public void EmptyAndUnknownAndNullFieldsFallBackToDefaults()
    {
        Assert.Equal("{}", UncivJson.Serialize(UncivJson.Deserialize<Tile>("{\"bogus\":{\"a\":[1,2]},\"resource\":null,\"improvementQueue\":null,\"terrainFeatures\":[]}")));
        var map = UncivJson.Deserialize<TileMap>("{\"extra\":1}")!;
        Assert.Empty(map.TileList);
        Assert.Empty(map.StartingLocations);
        Assert.Equal("", map.Description);
        Assert.Equal("{\"mapParameters\":" + UncivJson.Serialize(new MapParameters()) + "}", UncivJson.Serialize(map));
    }

    [Fact]
    public void IntegerFieldsAcceptFloatsLikeGdx()
    {
        var tile = UncivJson.Deserialize<Tile>("{\"resourceAmount\":3.0,\"continent\":\"2\",\"position\":{\"x\":2.0}}")!;
        Assert.Equal(3, tile.ResourceAmount);
        Assert.Equal(new HexCoord(2, 0), tile.Position);
        Assert.Contains("\"continent\":2", UncivJson.Serialize(tile));
    }

    [Theory]
    [InlineData("{\"roadStatus\":\"Highway\"}")]
    [InlineData("{\"roadStatus\":7}")]
    [InlineData("[]")]
    public void InvalidTileJsonThrows(string json) =>
        Assert.ThrowsAny<Exception>(() => UncivJson.Deserialize<Tile>(json));

    [Fact]
    public void InvalidStartingLocationUsageThrows() =>
        Assert.ThrowsAny<JsonException>(() => UncivJson.Deserialize<TileMap>("{\"startingLocations\":[{\"usage\":\"Boss\"}]}"));

    [Fact]
    public void UnitsAreNotWiredYet()
    {
        Assert.Throws<NotSupportedException>(() => UncivJson.Deserialize<Tile>("{\"militaryUnit\":{\"name\":\"Warrior\"}}"));
        Assert.Throws<NotSupportedException>(() => UncivJson.Deserialize<Tile>("{\"airUnits\":[{}]}"));
        // Absent, null and empty unit fields are what a unit-less map has
        Assert.Equal("{}", UncivJson.Serialize(UncivJson.Deserialize<Tile>("{\"militaryUnit\":null,\"civilianUnit\":null,\"airUnits\":[]}")));
        var tile = new Tile { MilitaryUnit = DispatchProxy.Create<IGameContextUnit, UnitProxy>() };
        Assert.Throws<NotSupportedException>(() => UncivJson.Serialize(tile));
    }

    public class UnitProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => null;
    }

    [Fact]
    public void HistoryAndImprovementQueueRoundTrip()
    {
        var json = "{\"improvementQueue\":[{\"improvement\":\"Road\",\"turnsToImprovement\":2},{}],\"history\":{\"1\":[null,\"N\"],\"5\":[\"Rome\",\"C\"]}}";
        var tile = UncivJson.Deserialize<Tile>(json)!;
        Assert.Equal(2, tile.ImprovementQueue.Count);
        Assert.Equal("", tile.ImprovementQueue[1].Improvement);
        // An all-default queue entry is written as {} and a "None" history entry is kept
        Assert.Equal("{\"improvementQueue\":[{\"improvement\":\"Road\",\"turnsToImprovement\":2},{}],\"history\":{\"1\":[null,\"N\"],\"5\":[\"Rome\",\"C\"]}}",
            UncivJson.Serialize(tile));
    }
}
