namespace Unciv.Models.Ruleset.Tile;

// core/src/com/unciv/models/ruleset/tile/ResourceType.kt
// Not ported yet: getColor() (Color.valueOf needs the engine colour type, T050), use the hex string from GetColorString.
public enum ResourceType
{
    Luxury,
    Strategic,
    Bonus,
}

public static class ResourceTypeExtensions
{
    /// <summary>Kotlin <c>ResourceType.color</c>: the hex colour string.</summary>
    public static string GetColorString(this ResourceType type) => type switch
    {
        ResourceType.Luxury => "#ffd800",
        ResourceType.Strategic => "#c14d00",
        ResourceType.Bonus => "#a8c3c9",
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };
}
