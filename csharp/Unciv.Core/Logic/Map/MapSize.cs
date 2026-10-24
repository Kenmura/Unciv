using System.Text.Json.Serialization;
using Unciv.Json;

namespace Unciv.Logic.Map;

/// <summary>
/// Encapsulates the "map size" concept, without also choosing a shape.
/// Ported from <c>core/src/com/unciv/logic/map/MapSize.kt</c>.
/// </summary>
public class MapSize : IIsPartOfGameInfoSerialization
{
    public const string Custom = "Custom";
    public const string AutoName = "Auto";

    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("radius")]
    public int Radius { get; set; }

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }

    /// <summary>Needed for JSON parsing.</summary>
    [JsonConstructor]
    public MapSize() : this("", 0, 0, 0)
    {
    }

    private MapSize(string name, int radius, int width, int height)
    {
        Name = name;
        Radius = radius;
        Width = width;
        Height = height;
    }

    public MapSize(Predefined size)
        : this(size.ToString(), size.GetRadius(), size.GetWidth(), size.GetHeight())
    {
    }

    public MapSize(string name)
        : this(name == AutoName ? Auto : new MapSize(SafeValueOfPredefined(name)))
    {
    }

    public MapSize(int radius)
        : this(Custom, radius, 0, 0)
    {
        SetNewRadius(radius);
    }

    public MapSize(int width, int height)
        : this(Custom, HexMath.GetEquivalentHexagonalRadius(width, height), width, height)
    {
    }

    public MapSize(MapSize other)
        : this(other.Name, other.Radius, other.Width, other.Height)
    {
    }

    /// <summary>Predefined Map Sizes; their name can appear in JSON only as copy in <see cref="MapSize"/>.</summary>
    public enum Predefined
    {
        Tiny,
        Small,
        Medium,
        Large,
        Huge,
    }

    public static Predefined SafeValueOfPredefined(string name) => name switch
    {
        nameof(Predefined.Tiny) => Predefined.Tiny,
        nameof(Predefined.Small) => Predefined.Small,
        nameof(Predefined.Medium) => Predefined.Medium,
        nameof(Predefined.Large) => Predefined.Large,
        nameof(Predefined.Huge) => Predefined.Huge,
        _ => Predefined.Tiny,
    };

    public static MapSize Auto => new(
        AutoName,
        Predefined.Medium.GetRadius(),
        Predefined.Medium.GetWidth(),
        Predefined.Medium.GetHeight());

    public static MapSize Tiny => new(Predefined.Tiny);
    public static MapSize Small => new(Predefined.Small);
    public static MapSize Medium => new(Predefined.Medium);
    public static MapSize Large => new(Predefined.Large);
    public static MapSize Huge => new(Predefined.Huge);

    public static IReadOnlyList<string> Names() =>
        ["Tiny", "Small", "Medium", "Large", "Huge", AutoName];

    public MapSize Clone() => new(Name, Radius, Width, Height);

    public Predefined GetPredefinedOrNextSmaller()
    {
        if (Name != Custom && Name != AutoName)
        {
            return SafeValueOfPredefined(Name);
        }

        Predefined[] all = [Predefined.Huge, Predefined.Large, Predefined.Medium, Predefined.Small, Predefined.Tiny];
        foreach (Predefined predef in all)
        {
            if (Radius >= predef.GetRadius())
            {
                return predef;
            }
        }

        return Predefined.Tiny;
    }

    /// <summary>
    /// Check custom dimensions, fix if too extreme.
    /// </summary>
    /// <param name="worldWrap">Whether world wrap is on.</param>
    /// <returns><c>null</c> if size was acceptable, otherwise untranslated reason message.</returns>
    public string? FixUndesiredSizes(bool worldWrap)
    {
        if (Name != Custom)
        {
            return null;
        }

        if (worldWrap && Width % 2 != 0)
        {
            Width--;
        }

        string? message;
        if (worldWrap && Width < 32)
        {
            message = "World wrap requires a minimum width of 32 tiles";
        }
        else if (Width < 3 || Height < 3 || Radius < 2)
        {
            message = "The provided map dimensions were too small";
        }
        else if (Radius > 500)
        {
            message = "The provided map dimensions were too big";
        }
        else if (Height * 16 < Width || Width * 16 < Height)
        {
            message = "The provided map dimensions had an unacceptable aspect ratio";
        }
        else
        {
            return null;
        }

        int fixedRadius;
        if (Radius < 2)
        {
            fixedRadius = 2;
        }
        else if (Radius > 500)
        {
            fixedRadius = 500;
        }
        else if (worldWrap && Radius < 15)
        {
            fixedRadius = 15;
        }
        else
        {
            fixedRadius = Radius;
        }

        SetNewRadius(fixedRadius);
        return message;
    }

    private void SetNewRadius(int radius)
    {
        Radius = radius;
        HexCoord size = HexMath.GetEquivalentRectangularSize(radius);
        Width = size.x;
        Height = size.y;
    }

    public override string ToString() => Name == Custom ? $"{Width}x{Height}" : Name;
}

public static class MapSizePredefinedExtensions
{
    public static int GetRadius(this MapSize.Predefined size) => size switch
    {
        MapSize.Predefined.Tiny => 10,
        MapSize.Predefined.Small => 15,
        MapSize.Predefined.Medium => 20,
        MapSize.Predefined.Large => 30,
        MapSize.Predefined.Huge => 40,
        _ => throw new ArgumentOutOfRangeException(nameof(size), size, null),
    };

    public static int GetWidth(this MapSize.Predefined size) => size switch
    {
        MapSize.Predefined.Tiny => 23,
        MapSize.Predefined.Small => 33,
        MapSize.Predefined.Medium => 44,
        MapSize.Predefined.Large => 66,
        MapSize.Predefined.Huge => 87,
        _ => throw new ArgumentOutOfRangeException(nameof(size), size, null),
    };

    public static int GetHeight(this MapSize.Predefined size) => size switch
    {
        MapSize.Predefined.Tiny => 15,
        MapSize.Predefined.Small => 21,
        MapSize.Predefined.Medium => 29,
        MapSize.Predefined.Large => 43,
        MapSize.Predefined.Huge => 57,
        _ => throw new ArgumentOutOfRangeException(nameof(size), size, null),
    };

    public static float GetTechCostMultiplier(this MapSize.Predefined size) => size switch
    {
        MapSize.Predefined.Tiny => 1f,
        MapSize.Predefined.Small => 1f,
        MapSize.Predefined.Medium => 1.1f,
        MapSize.Predefined.Large => 1.2f,
        MapSize.Predefined.Huge => 1.3f,
        _ => throw new ArgumentOutOfRangeException(nameof(size), size, null),
    };

    public static float GetTechCostPerCityModifier(this MapSize.Predefined size) => size switch
    {
        MapSize.Predefined.Tiny => 0.05f,
        MapSize.Predefined.Small => 0.05f,
        MapSize.Predefined.Medium => 0.05f,
        MapSize.Predefined.Large => 0.0375f,
        MapSize.Predefined.Huge => 0.025f,
        _ => throw new ArgumentOutOfRangeException(nameof(size), size, null),
    };

    public static float GetPolicyCostPerCityModifier(this MapSize.Predefined size) => size switch
    {
        MapSize.Predefined.Tiny => 0.1f,
        MapSize.Predefined.Small => 0.1f,
        MapSize.Predefined.Medium => 0.1f,
        MapSize.Predefined.Large => 0.075f,
        MapSize.Predefined.Huge => 0.05f,
        _ => throw new ArgumentOutOfRangeException(nameof(size), size, null),
    };
}
