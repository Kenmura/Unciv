using Unciv.Json;
using Unciv.Logic.Map;
using Unciv.Models.Metadata;

namespace Unciv.Core.Tests.Models.Metadata;

public class GameSetupInfoTests
{
    [Fact]
    public void DefaultConstructor_InitializesParametersWithDefaults()
    {
        var setup = new GameSetupInfo();

        Assert.NotNull(setup.GameParameters);
        Assert.NotNull(setup.MapParameters);
        Assert.Null(setup.MapFile);

        Assert.Equal("Prince", setup.GameParameters.Difficulty);
        Assert.Equal(MapType.Pangaea, setup.MapParameters.Type);
        Assert.Equal(MapShape.Hexagonal, setup.MapParameters.Shape);
    }

    [Fact]
    public void ParameterizedConstructor_AssignsProvidedParameters()
    {
        var gameParams = new GameParameters { Difficulty = "King" };
        var mapParams = new MapParameters { Type = MapType.Archipelago };

        var setup = new GameSetupInfo(gameParams, mapParams);

        Assert.Same(gameParams, setup.GameParameters);
        Assert.Same(mapParams, setup.MapParameters);
        Assert.Equal("King", setup.GameParameters.Difficulty);
        Assert.Equal(MapType.Archipelago, setup.MapParameters.Type);
    }

    [Fact]
    public void CopyConstructor_PerformsDeepClone()
    {
        var original = new GameSetupInfo();
        original.GameParameters.Difficulty = "Emperor";
        original.MapParameters.Name = "TestMap";
        original.MapFile = "path/to/map.json";

        var cloned = new GameSetupInfo(original);

        Assert.NotSame(original.GameParameters, cloned.GameParameters);
        Assert.NotSame(original.MapParameters, cloned.MapParameters);
        Assert.Equal("Emperor", cloned.GameParameters.Difficulty);
        Assert.Equal("TestMap", cloned.MapParameters.Name);
        Assert.Equal("path/to/map.json", cloned.MapFile);

        // Mutating original does not mutate clone
        original.GameParameters.Difficulty = "Deity";
        original.MapParameters.Name = "ModifiedMap";

        Assert.Equal("Emperor", cloned.GameParameters.Difficulty);
        Assert.Equal("TestMap", cloned.MapParameters.Name);
    }

    [Fact]
    public void CloneMethod_PerformsDeepClone()
    {
        var original = new GameSetupInfo();
        original.GameParameters.Difficulty = "Immortal";
        original.MapParameters.Shape = MapShape.FlatEarth;

        var cloned = original.Clone();

        Assert.NotSame(original.GameParameters, cloned.GameParameters);
        Assert.NotSame(original.MapParameters, cloned.MapParameters);
        Assert.Equal("Immortal", cloned.GameParameters.Difficulty);
        Assert.Equal(MapShape.FlatEarth, cloned.MapParameters.Shape);
    }

    [Fact]
    public void CreateDefault_AppliesQuickStartDefaults()
    {
        var setup = GameSetupInfo.CreateDefault();

        Assert.Equal(MapShape.Rectangular, setup.MapParameters.Shape);
        Assert.True(setup.MapParameters.WorldWrap);
        Assert.True(setup.GameParameters.EspionageEnabled);
        Assert.Equal("Prince", setup.GameParameters.Difficulty);
    }

    [Fact]
    public void CreateDefault_WithDifficultyOverride_OverridesDifficulty()
    {
        var setup = GameSetupInfo.CreateDefault("Chieftain");

        Assert.Equal("Chieftain", setup.GameParameters.Difficulty);
        Assert.Equal(MapShape.Rectangular, setup.MapParameters.Shape);
        Assert.True(setup.MapParameters.WorldWrap);
        Assert.True(setup.GameParameters.EspionageEnabled);
    }

    [Fact]
    public void JsonSerialization_RoundTripsCorrectly()
    {
        var original = new GameSetupInfo();
        original.GameParameters.Difficulty = "Warlord";
        original.MapParameters.Type = MapType.ContinentAndIslands;
        original.MapFile = "some/transient/file.json";

        string json = UncivJson.Serialize(original);

        Assert.Contains("\"gameParameters\":", json);
        Assert.Contains("\"mapParameters\":", json);
        Assert.DoesNotContain("mapFile", json);
        Assert.DoesNotContain("MapFile", json);

        var deserialized = UncivJson.Deserialize<GameSetupInfo>(json);

        Assert.NotNull(deserialized);
        Assert.Equal("Warlord", deserialized.GameParameters.Difficulty);
        Assert.Equal(original.MapParameters.Type, deserialized.MapParameters.Type);
        Assert.Null(deserialized.MapFile);
    }

    [Fact]
    public void JsonDeserialization_FromKotlinStyleJson()
    {
        string json = """
        {
            "gameParameters": {
                "difficulty": "Settler",
                "speed": "Standard"
            },
            "mapParameters": {
                "shape": "Rectangular",
                "worldWrap": true
            }
        }
        """;

        var setup = UncivJson.Deserialize<GameSetupInfo>(json);

        Assert.NotNull(setup);
        Assert.Equal("Settler", setup.GameParameters.Difficulty);
        Assert.Equal("Standard", setup.GameParameters.Speed);
        Assert.Equal(MapShape.Rectangular, setup.MapParameters.Shape);
        Assert.True(setup.MapParameters.WorldWrap);
    }
}
