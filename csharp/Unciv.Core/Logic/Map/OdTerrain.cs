using Unciv.Logic.Map.Tiles;

namespace Unciv.Logic.Map;

/// <summary>
/// The thirteen terrain tokens an Open Doctrines map can carry, and what each one is here (Kotlin enum <c>OdTerrain</c>).
/// One list, read both ways by <see cref="OdMapImport"/> (<see cref="ForToken"/>) and <see cref="OdMapExport"/> (<see cref="ForTile"/>).
/// Not a bijection: <c>inland_sea</c> and <c>lakes</c> both arrive as Lakes (Lakes leaves as <c>lakes</c>, declared first),
/// and Grassland has no token, so it leaves as <c>plains</c>.
/// </summary>
public sealed class OdTerrain
{
    /// <summary>What the Open Doctrines side calls it.</summary>
    public string Token { get; }
    /// <summary>The Unciv base terrain.</summary>
    public string BaseTerrain { get; }
    /// <summary>The Unciv terrain feature the token implies, where it implies one.</summary>
    public string? Feature { get; }

    private OdTerrain(string token, string baseTerrain, string? feature = null)
    {
        Token = token;
        BaseTerrain = baseTerrain;
        Feature = feature;
    }

    public static readonly OdTerrain Ocean = new("ocean", "Ocean");
    public static readonly OdTerrain CoastalSea = new("coastal_sea", "Coast");
    public static readonly OdTerrain Lakes = new("lakes", "Lakes");
    public static readonly OdTerrain InlandSea = new("inland_sea", "Lakes");
    public static readonly OdTerrain Mountain = new("mountain", "Mountain");
    public static readonly OdTerrain Hills = new("hills", "Plains", "Hill");
    public static readonly OdTerrain Desert = new("desert", "Desert");
    public static readonly OdTerrain Plains = new("plains", "Plains");
    public static readonly OdTerrain Forest = new("forest", "Grassland", "Forest");
    public static readonly OdTerrain Jungle = new("jungle", "Plains", "Jungle");
    public static readonly OdTerrain Swamp = new("swamp", "Grassland", "Marsh");
    public static readonly OdTerrain Tundra = new("tundra", "Tundra");
    public static readonly OdTerrain Frozen = new("frozen", "Snow");

    /// <summary>Kotlin <c>entries</c>, in declaration order.</summary>
    public static IReadOnlyList<OdTerrain> Entries { get; } =
        [Ocean, CoastalSea, Lakes, InlandSea, Mountain, Hills, Desert, Plains, Forest, Jungle, Swamp, Tundra, Frozen];

    /// <summary><c>null</c> when the token is not one of the thirteen.</summary>
    public static OdTerrain? ForToken(string token)
    {
        var wanted = token.ToLowerInvariant();
        foreach (var terrain in Entries)
            if (terrain.Token == wanted) return terrain;
        return null;
    }

    /// <summary>
    /// What to write for a tile. A match on base AND feature wins over a match on base alone,
    /// so a bare Plains must not come back as hills.
    /// </summary>
    public static OdTerrain ForTile(Tile tile)
    {
        foreach (var terrain in Entries)
            if (terrain.BaseTerrain == tile.BaseTerrain && terrain.Feature != null
                && tile.TerrainFeatures.Contains(terrain.Feature)) return terrain;
        foreach (var terrain in Entries)
            if (terrain.BaseTerrain == tile.BaseTerrain && terrain.Feature == null) return terrain;
        return Plains; // Grassland, which has no token of its own
    }
}
