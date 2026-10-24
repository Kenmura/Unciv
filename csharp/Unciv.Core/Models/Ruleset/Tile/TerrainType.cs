namespace Unciv.Models.Ruleset.Tile;

// core/src/com/unciv/models/ruleset/tile/TerrainType.kt
public enum TerrainType
{
    Land,
    Water,
    TerrainFeature,
    NaturalWonder,
}

public static class TerrainTypeExtensions
{
    /// <summary>Kotlin <c>TerrainType.isBaseTerrain</c></summary>
    public static bool IsBaseTerrain(this TerrainType type) => type is TerrainType.Land or TerrainType.Water;
}
