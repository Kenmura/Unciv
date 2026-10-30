using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using Unciv.Json;
using Unciv.Models.Metadata;
using Unciv.Utils;

namespace Unciv.Logic.Map;

public static class MapShape
{
    public const string Rectangular = "Rectangular";
    public const string Hexagonal = "Hexagonal";
    public const string FlatEarth = "Flat Earth Hexagonal";

    public static IReadOnlyList<string> AllValues { get; } =
        [Rectangular, Hexagonal, FlatEarth];
}

public static class MapGeneratedMainType
{
    public const string Generated = "Generated";
    public const string RandomGenerated = "Random Generated";
    public const string Custom = "Custom";
    public const string Scenario = "Scenario";
}

public static class MapType
{
    public const string Pangaea = "Pangaea";
    public const string SmallContinents = "Small Continents";
    public const string Perlin = "Perlin";
    public const string Fractal = "Fractal";
    public const string ContinentAndIslands = "Continent and Islands";
    public const string Archipelago = "Archipelago";
    public const string TwoContinents = "Two Continents";
    public const string ThreeContinents = "Three Continents";
    public const string InnerSea = "Inner Sea";
    public const string Lakes = "Lakes";
    public const string FourCorners = "Four Corners";
    public const string Boreal = "Boreal";
    public const string Spiral = "Spiral";

    public static IReadOnlyList<string> AllValues { get; } =
    [
        Pangaea,
        SmallContinents,
        Perlin,
        Fractal,
        ContinentAndIslands,
        Archipelago,
        TwoContinents,
        ThreeContinents,
        InnerSea,
        Lakes,
        FourCorners,
        Spiral,
        Boreal,
    ];

    public const string Empty = "Empty";
}

public static class MirroringType
{
    public const string None = "None";
    public const string LeftRight = "Left-right";
    public const string TopBottom = "Top-bottom";
    public const string FourWay = "Four-way";
    public const string AroundCenterTile = "Around center tile";
}

/// <summary>
/// Ported from <c>core/src/com/unciv/logic/map/mapgenerator/MapResourceSetting.kt</c>.
/// </summary>
public enum MapResourceSetting
{
    Sparse,
    Default,
    Abundant,
    StrategicBalance,
    LegendaryStart,
}

public static class MapResourceSettingExtensions
{
    public static string GetLabel(this MapResourceSetting setting) => setting switch
    {
        MapResourceSetting.Sparse => "Sparse",
        MapResourceSetting.Default => "Default",
        MapResourceSetting.Abundant => "Abundant",
        MapResourceSetting.StrategicBalance => "Strategic Balance",
        MapResourceSetting.LegendaryStart => "Legendary Start",
        _ => throw new ArgumentOutOfRangeException(nameof(setting), setting, null),
    };

    public static int GetRandomLuxuriesPercent(this MapResourceSetting setting) => setting switch
    {
        MapResourceSetting.Sparse => 80,
        MapResourceSetting.Abundant => 133,
        _ => 100,
    };

    public static int GetRegionalLuxuriesDelta(this MapResourceSetting setting) => setting switch
    {
        MapResourceSetting.Sparse => -1,
        MapResourceSetting.Abundant => 1,
        _ => 0,
    };

    public static float GetSpecialLuxuriesTargetFactor(this MapResourceSetting setting) => setting switch
    {
        MapResourceSetting.Sparse => 0.5f,
        MapResourceSetting.Abundant => 0.9f,
        _ => 0.75f,
    };

    public static float GetBonusFrequencyMultiplier(this MapResourceSetting setting) => setting switch
    {
        MapResourceSetting.Sparse => 1.5f,
        MapResourceSetting.Abundant => 0.6667f,
        _ => 1f,
    };

    public static IReadOnlyList<string> ActiveLabels() =>
        ["Sparse", "Default", "Abundant"];

    public static MapResourceSetting SafeValueOf(string label) => label switch
    {
        "Sparse" => MapResourceSetting.Sparse,
        "Default" => MapResourceSetting.Default,
        "Abundant" => MapResourceSetting.Abundant,
        "Strategic Balance" => MapResourceSetting.StrategicBalance,
        "Legendary Start" => MapResourceSetting.LegendaryStart,
        _ => MapResourceSetting.Default,
    };
}

/// <summary>
/// Ported from <c>core/src/com/unciv/logic/map/MapParameters.kt</c>.
/// </summary>
public class MapParameters : IIsPartOfGameInfoSerialization
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = MapType.Pangaea;

    // DO NOT CHANGE DEFAULTS since that changes all existing games to new default!
    [JsonPropertyName("shape")]
    public string Shape { get; set; } = MapShape.Hexagonal;

    [JsonPropertyName("mapSize")]
    public MapSize MapSize { get; set; } = MapSize.Medium;

    [JsonPropertyName("mapResources")]
    public string MapResources { get; set; } = MapResourceSetting.Default.GetLabel();

    [JsonPropertyName("mirroring")]
    public string Mirroring { get; set; } = MirroringType.None;

    [JsonPropertyName("noRuins")]
    public bool NoRuins { get; set; }

    [JsonPropertyName("noNaturalWonders")]
    public bool NoNaturalWonders { get; set; }

    // DO NOT CHANGE DEFAULTS since that changes all existing games to new default!
    [JsonPropertyName("worldWrap")]
    public bool WorldWrap { get; set; }

    [JsonPropertyName("strategicBalance")]
    public bool StrategicBalance { get; set; }

    [JsonPropertyName("legendaryStart")]
    public bool LegendaryStart { get; set; }

    /// <summary>Used mainly for the map editor, so you can continue editing a map under the same ruleset you started with.</summary>
    [JsonPropertyName("mods")]
    public LinkedHashSet<string> Mods { get; set; } = [];

    [JsonPropertyName("baseRuleset")]
    public string BaseRuleset { get; set; } = Models.Metadata.BaseRuleset.Civ_V_BNW.FullName();

    /// <summary>Unciv Version of creation for support cases.</summary>
    [JsonPropertyName("createdWithVersion")]
    public string CreatedWithVersion { get; set; } = "";

    /// <summary>
    /// RNG seed for map generation. Note: in Kotlin this defaults to <c>System.currentTimeMillis()</c>;
    /// in C# wall-clock time is banned for determinism, so it defaults to <c>0L</c> and must be seeded explicitly
    /// or via <see cref="Reseed(long)"/> / <see cref="Reseed(GameRandom)"/>.
    /// </summary>
    [JsonPropertyName("seed")]
    public long Seed { get; set; }

    [JsonPropertyName("tilesPerBiomeArea")]
    public int TilesPerBiomeArea { get; set; } = 6;

    [JsonPropertyName("maxCoastExtension")]
    public int MaxCoastExtension { get; set; } = 3;

    [JsonPropertyName("elevationExponent")]
    public float ElevationExponent { get; set; } = 0.7f;

    [JsonPropertyName("temperatureintensity")]
    public float TemperatureIntensity { get; set; } = 0.6f;

    [JsonPropertyName("vegetationRichness")]
    public float VegetationRichness { get; set; } = 0.4f;

    [JsonPropertyName("rareFeaturesRichness")]
    public float RareFeaturesRichness { get; set; } = 0.05f;

    [JsonPropertyName("resourceRichness")]
    public float ResourceRichness { get; set; } = 0.1f;

    [JsonPropertyName("waterThreshold")]
    public float WaterThreshold { get; set; } = 0.0f;

    /// <summary>Shifts temperature (after random, latitude and temperatureintensity).</summary>
    [JsonPropertyName("temperatureShift")]
    public float TemperatureShift { get; set; } = 0f;

    public MapParameters Clone() => new()
    {
        Name = Name,
        Type = Type,
        Shape = Shape,
        MapSize = MapSize.Clone(),
        MapResources = MapResources,
        NoRuins = NoRuins,
        NoNaturalWonders = NoNaturalWonders,
        WorldWrap = WorldWrap,
        StrategicBalance = StrategicBalance,
        LegendaryStart = LegendaryStart,
        Mods = new LinkedHashSet<string>(Mods),
        BaseRuleset = BaseRuleset,
        Seed = Seed,
        TilesPerBiomeArea = TilesPerBiomeArea,
        MaxCoastExtension = MaxCoastExtension,
        ElevationExponent = ElevationExponent,
        TemperatureIntensity = TemperatureIntensity,
        TemperatureShift = TemperatureShift,
        VegetationRichness = VegetationRichness,
        RareFeaturesRichness = RareFeaturesRichness,
        ResourceRichness = ResourceRichness,
        WaterThreshold = WaterThreshold,
        Mirroring = Mirroring,
        CreatedWithVersion = CreatedWithVersion,
    };

    public void Reseed(long newSeed) => Seed = newSeed;

    public void Reseed(GameRandom rng) => Seed = rng.NextLong();

    public void ResetAdvancedSettings(long newSeed = 0L)
    {
        Reseed(newSeed);
        ResetAdvancedNumericSettings();
    }

    public void ResetAdvancedSettings(GameRandom rng)
    {
        Reseed(rng);
        ResetAdvancedNumericSettings();
    }

    private void ResetAdvancedNumericSettings()
    {
        TilesPerBiomeArea = 6;
        MaxCoastExtension = 3;
        ElevationExponent = 0.7f;
        TemperatureIntensity = 0.6f;
        TemperatureShift = 0.0f;
        VegetationRichness = 0.4f;
        RareFeaturesRichness = 0.05f;
        ResourceRichness = 0.1f;
        WaterThreshold = 0f;
    }

    public MapResourceSetting GetMapResources() =>
        MapResourceSettingExtensions.SafeValueOf(MapResources);

    public bool GetStrategicBalance() =>
        StrategicBalance || MapResources == MapResourceSetting.StrategicBalance.GetLabel();

    public bool GetLegendaryStart() =>
        LegendaryStart || MapResources == MapResourceSetting.LegendaryStart.GetLabel();

    public int GetArea()
    {
        if (Shape == MapShape.Hexagonal || Shape == MapShape.FlatEarth)
        {
            return HexMath.GetNumberOfTilesInHexagon(MapSize.Radius);
        }

        if (WorldWrap && MapSize.Width % 2 != 0)
        {
            return (MapSize.Width - 1) * MapSize.Height;
        }

        return MapSize.Width * MapSize.Height;
    }

    private string DisplayMapDimensions()
    {
        string dims = Shape == MapShape.Hexagonal || Shape == MapShape.FlatEarth
            ? $"R{MapSize.Radius}"
            : $"{MapSize.Width}x{MapSize.Height}";
        return WorldWrap ? dims + "w" : dims;
    }

    private static string NiceToString(float value, int maxPrecision)
    {
        decimal dec = decimal.Parse(((double)value).ToString("R", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        decimal rounded = Math.Round(dec, maxPrecision, MidpointRounding.AwayFromZero);
        string result = rounded.ToString($"F{maxPrecision}", CultureInfo.InvariantCulture)
            .TrimEnd('0')
            .TrimEnd('.');
        if (rounded == 0m && float.IsNegative(value))
        {
            return "-" + result;
        }

        return result;
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        if (Name.Length > 0)
        {
            sb.Append('"').Append(Name).Append("\" ");
        }

        sb.Append('(');
        if (MapSize.Name != MapSize.Custom)
        {
            sb.Append('{').Append(MapSize.Name).Append("} ");
        }

        if (WorldWrap)
        {
            sb.Append("{World Wrap} ");
        }

        sb.Append('{').Append(Shape).Append('}');
        sb.Append(' ').Append(DisplayMapDimensions()).Append(')');

        if (MapResources != MapResourceSetting.Default.GetLabel())
        {
            sb.Append(" {Resource Setting}: {").Append(MapResources).Append('}');
        }

        if (StrategicBalance)
        {
            sb.Append(" {Strategic Balance}");
        }

        if (LegendaryStart)
        {
            sb.Append(" {Legendary Start}");
        }

        if (Name.Length == 0)
        {
            return sb.ToString();
        }

        sb.Append('\n');
        if (Type != MapGeneratedMainType.Custom && Type != MapType.Empty)
        {
            sb.Append("{Map Generation Type}: {").Append(Type).Append("}, ");
        }

        sb.Append("{RNG Seed} ").Append(Seed.ToString(CultureInfo.InvariantCulture));
        sb.Append(", {Map Elevation}=").Append(NiceToString(ElevationExponent, 2));
        sb.Append(", {Temperature intensity}=").Append(NiceToString(TemperatureIntensity, 2));
        sb.Append(", {Resource richness}=").Append(NiceToString(ResourceRichness, 3));
        sb.Append(", {Vegetation richness}=").Append(NiceToString(VegetationRichness, 2));
        sb.Append(", {Rare features richness}=").Append(NiceToString(RareFeaturesRichness, 3));
        sb.Append(", {Max Coast extension}=").Append(MaxCoastExtension.ToString(CultureInfo.InvariantCulture));
        sb.Append(", {Biome size}=").Append(TilesPerBiomeArea.ToString(CultureInfo.InvariantCulture));
        sb.Append(", {Water level}=").Append(NiceToString(WaterThreshold, 2));
        return sb.ToString();
    }

    public int NumberOfTiles() =>
        Shape == MapShape.Hexagonal || Shape == MapShape.FlatEarth
            ? 1 + 3 * MapSize.Radius * (MapSize.Radius - 1)
            : MapSize.Width * MapSize.Height;
}
