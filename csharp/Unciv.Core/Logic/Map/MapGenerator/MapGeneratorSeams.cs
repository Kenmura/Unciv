using Unciv.Logic.Map.Tiles;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;

namespace Unciv.Logic.Map.MapGenerator;

// T073 seam for MapGenerator.kt (owned by T041). The Kotlin MapGenerator class is not ported yet, so the members of it that
// MapElevationGenerator / MapLandmassGenerator call live here. T041 moves them into MapGenerator (nested class / companion) and deletes this file.

/// <summary>
/// Kotlin <c>MapGenerator.TerrainOccursRange</c>: associates <see cref="Terrain"/> with a range of temperatures and a range of
/// humidities (both open to closed). Deferred to T041: the <c>(Terrain, Unique)</c> constructor and <c>Terrain.getGenerationConditions</c>.
/// </summary>
internal class TerrainOccursRange(
    Terrain terrain,
    float tempFrom, float tempTo,
    float humidFrom, float humidTo,
    bool isConstrained)
{
    public Terrain Terrain { get; } = terrain;
    public float TempFrom { get; } = tempFrom;
    public float TempTo { get; } = tempTo;
    public float HumidFrom { get; } = humidFrom;
    public float HumidTo { get; } = humidTo;
    public bool IsConstrained { get; } = isConstrained;

    public string Name => Terrain.Name;
    public bool OccursInChains { get; } = ((IHasUniques)terrain).HasUnique(UniqueType.OccursInChains);
    public bool OccursInGroups { get; } = ((IHasUniques)terrain).HasUnique(UniqueType.OccursInGroups);
    public bool HasVegitation { get; } = ((IHasUniques)terrain).HasUnique(UniqueType.Vegetation);
    public bool IsRough => Terrain.IsRough;
    public bool IsFreshwater => Terrain.IsFreshwater;
    public bool IsCoast => Terrain.IsCoast;
    public bool IsOcean => Terrain.IsOcean;
    public bool RareFeature { get; } = ((IHasUniques)terrain).HasUnique(UniqueType.RareFeature);

    /// <summary>builds a <see cref="TerrainOccursRange"/> for <paramref name="terrain"/> without a TileGenerationConditions unique</summary>
    public TerrainOccursRange(Terrain terrain)
        : this(terrain, -float.MaxValue, float.MaxValue, -float.MaxValue, float.MaxValue, isConstrained: false)
    {
    }

    private bool MatchesBaseTerrain(Tile tile)
    {
        if (Terrain.Type == TerrainType.Water)
            return tile.GetBaseTerrain().Type == TerrainType.Water
                && tile.GetBaseTerrain().IsFreshwater == IsFreshwater
                && tile.GetBaseTerrain().IsCoast == IsCoast;
        if (Terrain.Type == TerrainType.Land)
            return tile.GetBaseTerrain().Type == TerrainType.Land
                && ((IHasUniques)tile.GetBaseTerrain()).HasUnique(UniqueType.OccursInChains) == OccursInChains;
        return Terrain.OccursOn.Contains(tile.LastTerrain.Name);
    }

    /// <summary>
    /// Checks if both temperature and humidity satisfy their ranges (&gt;From, &lt;=To)
    /// Note the lowest allowed limit has been made inclusive (temp -1 nudged down by 1 float ulp, humidity at 0)
    /// </summary>
    // Yes this does implicit conversions Float/Double
    public bool MatchesHumidityAndTemp(Tile tile) => MatchesHumidity(tile, tile.Temperature ?? 0.0);

    public bool MatchesHumidity(Tile tile, double overrideTileTemp) =>
        TempFrom < overrideTileTemp && overrideTileTemp <= TempTo
        && HumidFrom < (tile.Humidity ?? 0.0) && (tile.Humidity ?? 0.0) <= HumidTo;

    public bool MatchesTempAndTerrain(Tile tile) => MatchesBaseTerrain(tile) && MatchesHumidityAndTemp(tile);

    public bool MatchesTempAndTerrain(Tile tile, double overrideTileTemp) => MatchesBaseTerrain(tile) && MatchesHumidity(tile, overrideTileTemp);

    public bool MaybeSnow() => Terrain.Type == TerrainType.Land
        && TempFrom <= -1 && TempTo <= -.5
        && HumidFrom >= -.1 && HumidTo >= 1
        && !RareFeature;

    public override string ToString() => Name;
}

internal static class MapGeneratorSeams
{
    /// <summary>Kotlin <c>MapGenerator.getTileRadius</c> (companion).</summary>
    /// <returns>Radius in range 0.0 (center of map) to 1.0 (edge of map).</returns>
    internal static double GetTileRadius(Tile tile, TileMap tileMap)
    {
        // Numbers betwee 0.0-1.0
        double latitudeRatio = Math.Abs(tile.Latitude) / (double)tileMap.MaxLatitude;
        double longitudeRatio = Math.Abs(tile.Longitude) / (double)tileMap.MaxLongitude;
        return Math.Sqrt(Math.Pow(latitudeRatio, 2) + Math.Pow(longitudeRatio, 2));
    }
}
