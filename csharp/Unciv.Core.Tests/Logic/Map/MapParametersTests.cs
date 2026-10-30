using Unciv.Json;
using Unciv.Logic.Map;
using Unciv.Logic.Map.Tiles;
using Unciv.Utils;

namespace Unciv.Core.Tests.Logic.Map;

public class MapParametersTests
{
    [Fact]
    public void MapSize_Predefined_MatchesKotlinGoldenValues()
    {
        AssertPredefined(MapSize.Predefined.Tiny, 10, 23, 15, 1f, 0.05f, 0.1f);
        AssertPredefined(MapSize.Predefined.Small, 15, 33, 21, 1f, 0.05f, 0.1f);
        AssertPredefined(MapSize.Predefined.Medium, 20, 44, 29, 1.1f, 0.05f, 0.1f);
        AssertPredefined(MapSize.Predefined.Large, 30, 66, 43, 1.2f, 0.0375f, 0.075f);
        AssertPredefined(MapSize.Predefined.Huge, 40, 87, 57, 1.3f, 0.025f, 0.05f);

        Assert.Equal(MapSize.Predefined.Tiny, MapSize.SafeValueOfPredefined("Unknown"));
        Assert.Equal(MapSize.Predefined.Tiny, MapSize.SafeValueOfPredefined("2"));
        Assert.Equal(MapSize.Predefined.Large, MapSize.SafeValueOfPredefined("Large"));

        MapSize auto = MapSize.Auto;
        Assert.Equal("Auto", auto.Name);
        Assert.Equal(20, auto.Radius);
        Assert.Equal(44, auto.Width);
        Assert.Equal(29, auto.Height);
        Assert.Equal(MapSize.Predefined.Medium, auto.GetPredefinedOrNextSmaller());

        Assert.Equal(["Tiny", "Small", "Medium", "Large", "Huge", "Auto"], MapSize.Names());
    }

    private static void AssertPredefined(
        MapSize.Predefined predefined,
        int radius,
        int width,
        int height,
        float techMult,
        float techPerCity,
        float policyPerCity)
    {
        var ms = new MapSize(predefined);
        Assert.Equal(predefined.ToString(), ms.Name);
        Assert.Equal(radius, ms.Radius);
        Assert.Equal(width, ms.Width);
        Assert.Equal(height, ms.Height);
        Assert.Equal(predefined.ToString(), ms.ToString());
        Assert.Equal(predefined, ms.GetPredefinedOrNextSmaller());
        Assert.Equal(techMult, predefined.GetTechCostMultiplier());
        Assert.Equal(techPerCity, predefined.GetTechCostPerCityModifier());
        Assert.Equal(policyPerCity, predefined.GetPolicyCostPerCityModifier());
    }

    [Fact]
    public void MapSize_CustomRadius_MatchesKotlinGoldenValues()
    {
        (int Radius, int Width, int Height, MapSize.Predefined Predef)[] cases =
        [
            (1, 3, 2, MapSize.Predefined.Tiny),
            (2, 5, 3, MapSize.Predefined.Tiny),
            (10, 23, 15, MapSize.Predefined.Tiny),
            (14, 31, 20, MapSize.Predefined.Tiny),
            (15, 33, 21, MapSize.Predefined.Small),
            (25, 55, 36, MapSize.Predefined.Medium),
            (35, 76, 49, MapSize.Predefined.Large),
            (50, 108, 70, MapSize.Predefined.Huge),
            (501, 1077, 700, MapSize.Predefined.Huge),
        ];

        foreach (var c in cases)
        {
            var ms = new MapSize(c.Radius);
            Assert.Equal(MapSize.Custom, ms.Name);
            Assert.Equal(c.Radius, ms.Radius);
            Assert.Equal(c.Width, ms.Width);
            Assert.Equal(c.Height, ms.Height);
            Assert.Equal($"{c.Width}x{c.Height}", ms.ToString());
            Assert.Equal(c.Predef, ms.GetPredefinedOrNextSmaller());
        }
    }

    [Fact]
    public void MapSize_CustomWidthHeightAndFixUndesiredSizes_MatchesKotlinGoldenValues()
    {
        // Verified against JVM com.unciv.logic.map.MapSize
        var ms10x10 = new MapSize(10, 10);
        Assert.Equal(5, ms10x10.Radius);
        Assert.Equal("10x10", ms10x10.ToString());
        Assert.Equal(MapSize.Predefined.Tiny, ms10x10.GetPredefinedOrNextSmaller());

        MapSize c1 = ms10x10.Clone();
        Assert.Null(c1.FixUndesiredSizes(worldWrap: false));
        Assert.Equal((5, 10, 10), (c1.Radius, c1.Width, c1.Height));

        MapSize c2 = ms10x10.Clone();
        Assert.Equal("World wrap requires a minimum width of 32 tiles", c2.FixUndesiredSizes(worldWrap: true));
        Assert.Equal((15, 33, 21), (c2.Radius, c2.Width, c2.Height));

        // 33x21 with worldWrap=true silently decrements odd width to 32 (>= 32, so valid)
        var ms33x21 = new MapSize(33, 21);
        Assert.Equal(15, ms33x21.Radius);
        Assert.Equal(MapSize.Predefined.Small, ms33x21.GetPredefinedOrNextSmaller());
        Assert.Null(ms33x21.FixUndesiredSizes(worldWrap: true));
        Assert.Equal((15, 32, 21), (ms33x21.Radius, ms33x21.Width, ms33x21.Height));

        // 100x5 aspect ratio > 16:1
        var ms100x5 = new MapSize(100, 5);
        Assert.Equal(12, ms100x5.Radius);
        MapSize aspectNoWrap = ms100x5.Clone();
        Assert.Equal(
            "The provided map dimensions had an unacceptable aspect ratio",
            aspectNoWrap.FixUndesiredSizes(worldWrap: false));
        Assert.Equal((12, 27, 18), (aspectNoWrap.Radius, aspectNoWrap.Width, aspectNoWrap.Height));

        MapSize aspectWrap = ms100x5.Clone();
        Assert.Equal(
            "The provided map dimensions had an unacceptable aspect ratio",
            aspectWrap.FixUndesiredSizes(worldWrap: true));
        Assert.Equal((15, 33, 21), (aspectWrap.Radius, aspectWrap.Width, aspectWrap.Height));

        // Too small (radius < 2) and too big (radius > 500)
        var tooSmall = new MapSize(1);
        Assert.Equal("The provided map dimensions were too small", tooSmall.FixUndesiredSizes(worldWrap: false));
        Assert.Equal((2, 5, 3), (tooSmall.Radius, tooSmall.Width, tooSmall.Height));

        var tooBig = new MapSize(501);
        Assert.Equal("The provided map dimensions were too big", tooBig.FixUndesiredSizes(worldWrap: false));
        Assert.Equal(500, tooBig.Radius);

        // Predefined size is untouched even if worldWrap=true on Tiny (width 23)
        MapSize tiny = MapSize.Tiny;
        Assert.Null(tiny.FixUndesiredSizes(worldWrap: true));
        Assert.Equal(23, tiny.Width);
    }

    [Fact]
    public void MapParameters_ToStringAreaAndJson_MatchesKotlinGoldenValues()
    {
        var mp = new MapParameters { Seed = 123456789L };
        Assert.Equal("({Medium} {Hexagonal} R20)", mp.ToString());
        Assert.Equal(1261, mp.GetArea());
        Assert.Equal(1141, mp.NumberOfTiles());

        // Deserialize golden JSON from Kotlin UncivJsonKt.json().toJson(mp)
        const string kotlinDefaultJson = """{"mapSize":{"name":"Medium","radius":20,"width":44,"height":29},"seed":123456789}""";
        MapParameters? fromKotlinDefault = UncivJson.Deserialize<MapParameters>(kotlinDefaultJson);
        Assert.NotNull(fromKotlinDefault);
        Assert.Equal("({Medium} {Hexagonal} R20)", fromKotlinDefault.ToString());
        Assert.Equal(123456789L, fromKotlinDefault.Seed);
        Assert.Equal("Civ V - Brave New World", fromKotlinDefault.BaseRuleset);
        Assert.Equal(0.6f, fromKotlinDefault.TemperatureIntensity);

        // Named rectangular world-wrap configuration matching JVM golden output
        const string kotlinNamedJson =
            """{"name":"TestMap","shape":"Rectangular","mapSize":{"name":"Custom","radius":14,"width":33,"height":20},"mapResources":"Abundant","worldWrap":true,"strategicBalance":true,"legendaryStart":true,"mods":["ModA","ModB"],"seed":123456789}""";
        MapParameters? fromKotlinNamed = UncivJson.Deserialize<MapParameters>(kotlinNamedJson);
        Assert.NotNull(fromKotlinNamed);

        const string expectedToString =
            "\"TestMap\" ({World Wrap} {Rectangular} 33x20w) {Resource Setting}: {Abundant} {Strategic Balance} {Legendary Start}\n" +
            "{Map Generation Type}: {Pangaea}, {RNG Seed} 123456789, {Map Elevation}=0.7, {Temperature intensity}=0.6, " +
            "{Resource richness}=0.1, {Vegetation richness}=0.4, {Rare features richness}=0.05, " +
            "{Max Coast extension}=3, {Biome size}=6, {Water level}=0";
        Assert.Equal(expectedToString, fromKotlinNamed.ToString());
        Assert.Equal(640, fromKotlinNamed.GetArea());
        Assert.Equal(660, fromKotlinNamed.NumberOfTiles());
        Assert.Equal(["ModA", "ModB"], fromKotlinNamed.Mods.ToArray());
        Assert.Equal(MapResourceSetting.Abundant, fromKotlinNamed.GetMapResources());
        Assert.True(fromKotlinNamed.GetStrategicBalance());
        Assert.True(fromKotlinNamed.GetLegendaryStart());

        // Round-trip through UncivJson preserves all fields including lowercase "temperatureintensity"
        fromKotlinNamed.TemperatureIntensity = 0.75f;
        string serialized = UncivJson.Serialize(fromKotlinNamed);
        Assert.Contains("\"temperatureintensity\":0.75", serialized, StringComparison.Ordinal);
        MapParameters? roundTripped = UncivJson.Deserialize<MapParameters>(serialized);
        Assert.NotNull(roundTripped);
        Assert.Equal(0.75f, roundTripped.TemperatureIntensity);
        Assert.Equal(fromKotlinNamed.ToString(), roundTripped.ToString());

        // Exact decimal tie rounding and negative-zero preservation in NiceToString (matching JVM String.format)
        var tieParams = new MapParameters
        {
            Name = "TieMap",
            Seed = 42L,
            ElevationExponent = 0.125f, // 2 digits -> 0.13
            TemperatureIntensity = -2.625f, // 2 digits -> -2.63
            ResourceRichness = 0.3125f, // 3 digits -> 0.313
            VegetationRichness = -14.625f, // 2 digits -> -14.63
            RareFeaturesRichness = 0.0005f, // 3 digits -> 0.001
            WaterThreshold = -0.001f, // 2 digits -> -0
        };
        const string expectedTieToString =
            "\"TieMap\" ({Medium} {Hexagonal} R20)\n" +
            "{Map Generation Type}: {Pangaea}, {RNG Seed} 42, {Map Elevation}=0.13, {Temperature intensity}=-2.63, " +
            "{Resource richness}=0.313, {Vegetation richness}=-14.63, {Rare features richness}=0.001, " +
            "{Max Coast extension}=3, {Biome size}=6, {Water level}=-0";
        Assert.Equal(expectedTieToString, tieParams.ToString());
    }

    [Fact]
    public void MapParameters_CloneReseedAndLegacyResourceFlags()
    {
        var mp = new MapParameters
        {
            Name = "Original",
            MapResources = "Strategic Balance",
        };
        Assert.True(mp.GetStrategicBalance());
        Assert.False(mp.GetLegendaryStart());

        mp.MapResources = "Legendary Start";
        Assert.False(mp.GetStrategicBalance());
        Assert.True(mp.GetLegendaryStart());

        Assert.Equal(["Sparse", "Default", "Abundant"], MapResourceSettingExtensions.ActiveLabels());

        var rng = new GameRandom(42);
        mp.ElevationExponent = 0.9f;
        mp.ResetAdvancedSettings(rng);
        Assert.Equal(0.7f, mp.ElevationExponent);
        Assert.NotEqual(0L, mp.Seed);

        MapParameters clone = mp.Clone();
        clone.MapSize.Width = 999;
        clone.Mods.Add("ExtraMod");
        Assert.Equal(44, mp.MapSize.Width);
        Assert.True(mp.Mods.IsEmpty);

        // LinkedHashSet preserves insertion order when removing and re-adding
        mp.Mods.Add("First");
        mp.Mods.Add("Second");
        mp.Mods.Add("Third");
        mp.Mods.Remove("First");
        mp.Mods.Add("Fourth");
        mp.Mods.Add("First");
        Assert.Equal(["Second", "Third", "Fourth", "First"], mp.Mods.ToArray());
        Assert.Equal(["Second", "Third", "Fourth", "First"], mp.Clone().Mods.ToArray());
    }

    [Fact]
    public void NeighborDirection_And_RoadStatus_MatchKotlinDefinitions()
    {
        Assert.Equal(0, (int)NeighborDirection.TopRight);
        Assert.Equal(1, (int)NeighborDirection.BottomRight);
        Assert.Equal(2, (int)NeighborDirection.Bottom);
        Assert.Equal(3, (int)NeighborDirection.BottomLeft);
        Assert.Equal(4, (int)NeighborDirection.TopLeft);
        Assert.Equal(5, (int)NeighborDirection.Top);

        Assert.Equal(NeighborDirection.TopRight, NeighborDirectionExtensions.ByClockPosition[2]);
        Assert.Equal(NeighborDirection.Bottom, NeighborDirectionExtensions.ByClockPosition[6]);
        Assert.Equal(NeighborDirection.Top, NeighborDirectionExtensions.ByClockPosition[12]);
        Assert.Equal(10, NeighborDirection.TopLeft.GetClockPosition());

        Assert.True(RoadStatus.None < RoadStatus.Road && RoadStatus.Road < RoadStatus.Railroad);
        Assert.Equal(0, RoadStatus.None.GetUpkeep());
        Assert.Equal(1, RoadStatus.Road.GetUpkeep());
        Assert.Equal(2, RoadStatus.Railroad.GetUpkeep());
        Assert.Equal(1f, RoadStatus.None.GetMovement());
        Assert.Equal(0.5f, RoadStatus.Road.GetMovement());
        Assert.Equal(1f / 3f, RoadStatus.Road.GetMovementImproved());
        Assert.Equal(0.1f, RoadStatus.Railroad.GetMovement());
        Assert.Null(RoadStatus.None.GetRemoveAction());
        Assert.Equal("Remove Road", RoadStatus.Road.GetRemoveAction());
        Assert.Equal("Remove Railroad", RoadStatus.Railroad.GetRemoveAction());

        Assert.Equal("\"Railroad\"", UncivJson.Serialize(RoadStatus.Railroad));
        Assert.Equal(RoadStatus.Railroad, UncivJson.Deserialize<RoadStatus>("\"Railroad\""));
    }
}
