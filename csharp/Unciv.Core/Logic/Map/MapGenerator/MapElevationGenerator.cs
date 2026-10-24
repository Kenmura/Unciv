using Unciv.Logic.Map.Tiles;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using Unciv.Utils;

namespace Unciv.Logic.Map.MapGenerator;

// core/src/com/unciv/logic/map/mapgenerator/MapElevationGenerator.kt
// Deviation: the Kotlin private Tile extension functions (isMountainTerrain etc.) are private methods taking the tile.
internal class MapElevationGenerator(
    TileMap tileMap,
    Ruleset ruleset,
    IReadOnlyList<TerrainOccursRange> terrains,
    MapGenerationRandomness randomness)
{
    private const string Rising = "~Raising~";
    private const string Lowering = "~Lowering~";

    private readonly List<TerrainOccursRange> flats = terrains
        .Where(it => !it.Terrain.Impassable && it.Terrain.Type == TerrainType.Land && !it.RareFeature && !it.IsRough).ToList();
    private readonly List<TerrainOccursRange> hills = terrains
        .Where(it => (it.Terrain.Type == TerrainType.Land || it.Terrain.Type == TerrainType.TerrainFeature) && it.OccursInGroups && !it.RareFeature).ToList();
    private readonly List<TerrainOccursRange> mountains = terrains
        .Where(it => (it.Terrain.Type == TerrainType.Land || it.Terrain.Type == TerrainType.TerrainFeature) && it.OccursInChains && !it.RareFeature).ToList();

    private static double PowSigned(double value, double exponent) => Math.Pow(Math.Abs(value), exponent) * Math.Sign(value);

    /// <summary><see cref="MapParameters.ElevationExponent"/> favors high elevation</summary>
    public void RaiseMountainsAndHills()
    {
        if (flats.Count == 0)
        {
            Log.Debug("Ruleset seems to contain no flat terrain - can't generate heightmap");
            return;
        }

        double elevationSeed = randomness.RNG.NextInt();

        // some map types are more mountainous than others
        double mapSpecificElevationOffset = tileMap.MapParameters.Type == MapType.Boreal ? +0.03 : 0.0;
        double exponent = 1.0 - (double)tileMap.MapParameters.ElevationExponent - mapSpecificElevationOffset;

        tileMap.SetTransients(ruleset);

        foreach (var tile in tileMap.Values)
        {
            if (tile.IsWater) continue;
            double elevation = PowSigned(randomness.GetPerlinNoise(tile, elevationSeed, scale: 2.0), exponent);
            if (elevation > 0.7 && mountains.Count != 0)
                ApplyTerrain(tile, mountains);
            else if (elevation > 0.5 && hills.Count != 0)
                ApplyTerrain(tile, hills);
            else
                ApplyTerrain(tile, flats);
        }

        CellularMountainRanges();
        CellularHills();

        foreach (var tile in tileMap.Values)
            tile.SetTerrainTransients();
    }

    private void CellularMountainRanges()
    {
        if (mountains.Count == 0) return;
        Log.Debug("Mountain-like generation");

        int targetMountains = tileMap.Values.Count(IsMountainTerrain) * 2;
        var impassableTerrains = ruleset.Terrains.Values.Where(it => it.Impassable).Select(it => it.Name).ToHashSet();

        for (int i = 1; i <= 5; i++)
        {
            int totalMountains = tileMap.Values.Count(IsMountainTerrain) * 2;

            foreach (var tile in tileMap.Values)
            {
                if (tile.IsWater) continue;
                bool isMountain = IsMountainTerrain(tile);
                int adjacentMountains = tile.Neighbors.Count(IsMountainTerrain);
                int adjacentImpassible = tile.Neighbors.Count(it => impassableTerrains.Contains(it.BaseTerrain));

                if (adjacentMountains == 0)
                {
                    if (isMountain && randomness.RNG.NextInt(4) == 0)
                        tile.AddTerrainFeature(Lowering);
                }
                else if (adjacentMountains == 1)
                {
                    if (!isMountain && randomness.RNG.NextInt(10) == 0)
                        tile.AddTerrainFeature(Rising);
                }
                else if (adjacentImpassible == 3)
                {
                    if (isMountain && randomness.RNG.NextInt(2) == 0)
                        tile.AddTerrainFeature(Lowering);
                }
                else if (isMountain && adjacentImpassible > 3)
                {
                    tile.AddTerrainFeature(Lowering);
                }
            }

            foreach (var tile in tileMap.Values)
            {
                if (tile.IsWater) continue;
                if (tile.TerrainFeatures.Contains(Rising))
                {
                    tile.RemoveTerrainFeature(Rising);
                    if (totalMountains >= targetMountains) continue;
                    totalMountains++;
                    ApplyTerrain(tile, mountains);
                }
                if (tile.TerrainFeatures.Contains(Lowering))
                {
                    tile.RemoveTerrainFeature(Lowering);
                    if (totalMountains * 2 <= targetMountains) continue;
                    totalMountains--;
                    ApplyTerrain(tile, flats);
                }
            }
        }
    }

    private void CellularHills()
    {
        if (hills.Count == 0) return;
        Log.Debug("Hill-like generation");

        int targetHills = tileMap.Values.Count(IsHillTerrain);

        for (int i = 1; i <= 5; i++)
        {
            int totalHills = tileMap.Values.Count(IsHillTerrain);

            foreach (var tile in tileMap.Values)
            {
                bool isMountain = HasTerrain(tile, mountains);
                if (tile.IsWater || isMountain) continue;
                bool isHill = IsHillTerrain(tile);
                int adjacentMountains = tile.Neighbors.Count(IsMountainTerrain);
                int adjacentHills = tile.Neighbors.Count(IsHillTerrain);

                if (adjacentHills <= 1 && adjacentMountains == 0 && isHill && randomness.RNG.NextInt(2) == 0)
                    tile.AddTerrainFeature(Lowering);
                else if (adjacentHills > 3 && adjacentMountains == 0 && isHill && randomness.RNG.NextInt(2) == 0)
                    tile.AddTerrainFeature(Lowering);
                else if (adjacentHills + adjacentMountains is >= 2 and <= 3 && !isHill && randomness.RNG.NextInt(2) == 0)
                    tile.AddTerrainFeature(Rising);
            }

            foreach (var tile in tileMap.Values)
            {
                if (tile.TerrainFeatures.Contains(Rising))
                {
                    tile.RemoveTerrainFeature(Rising);
                    if (totalHills > targetHills && i != 1) continue;
                    totalHills++;
                    ApplyTerrain(tile, hills);
                }
                if (tile.TerrainFeatures.Contains(Lowering))
                {
                    tile.RemoveTerrainFeature(Lowering);
                    if (totalHills >= targetHills * 0.9f || i == 1)
                    {
                        totalHills--;
                        ApplyTerrain(tile, flats);
                    }
                }
            }
        }
    }

    private bool IsMountainTerrain(Tile tile) => HasUniqueOnTerrainOrFeature(tile, UniqueType.OccursInChains);

    private bool IsHillTerrain(Tile tile) => HasUniqueOnTerrainOrFeature(tile, UniqueType.OccursInGroups);

    private bool HasUniqueOnTerrainOrFeature(Tile tile, UniqueType uniqueType)
    {
        if (!ruleset.Terrains.TryGetValue(tile.BaseTerrain, out var terrainObj)) return false;
        if (((IHasUniques)terrainObj).HasUnique(uniqueType)) return true;
        foreach (string feature in tile.TerrainFeatures)
        {
            if (!ruleset.Terrains.TryGetValue(feature, out var featureObj)) continue;
            if (((IHasUniques)featureObj).HasUnique(uniqueType)) return true;
        }
        return false;
    }

    private void DropElevatedFeatures(Tile tile)
    {
        foreach (string feature in tile.TerrainFeatures)
        {
            if (!ruleset.Terrains.TryGetValue(feature, out var featureObj)) continue;
            if (((IHasUniques)featureObj).HasUnique(UniqueType.OccursInChains)
                || ((IHasUniques)featureObj).HasUnique(UniqueType.OccursInGroups))
            {
                tile.RemoveTerrainFeature(feature);
                return;
            }
        }
    }

    private static bool HasTerrain(Tile tile, List<TerrainOccursRange> terrains)
    {
        foreach (var terrain in terrains)
        {
            if (terrain.Terrain.Type == TerrainType.Land)
            {
                if (tile.BaseTerrain == terrain.Name)
                    return true;
            }
            else // must be TerrainType.TerrainFeature
            {
                if (tile.TerrainFeatures.Contains(terrain.Name))
                    return true;
            }
        }
        return false;
    }

    private void ApplyTerrain(Tile tile, List<TerrainOccursRange> terrainsToChooseFrom)
    {
        var matching = terrainsToChooseFrom.Where(it => it.MatchesTempAndTerrain(tile)).ToList();
        var desiredTerrain = (matching.Count != 0 ? matching : terrainsToChooseFrom).Random(randomness.RNG);
        DropElevatedFeatures(tile);
        if (desiredTerrain.Terrain.Type == TerrainType.Land)
        {
            tile.BaseTerrain = desiredTerrain.Name;
        }
        else // must be TerrainType.TerrainFeature
        {
            var baseTerrains = flats.Where(it => desiredTerrain.Terrain.OccursOn.Contains(it.Name) && it.MatchesHumidityAndTemp(tile)).ToList();
            if (baseTerrains.Count == 0)
                baseTerrains = flats.Where(it => desiredTerrain.Terrain.OccursOn.Contains(it.Name)).ToList();
            if (baseTerrains.Count == 0)
            {
                Log.Debug("Ruleset seems to contain no flat terrain that %s can apply to. Failing feature.", desiredTerrain.Name);
                return;
            }
            var baseTerrain = baseTerrains.Random(randomness.RNG);
            tile.BaseTerrain = baseTerrain.Name;
            tile.AddTerrainFeature(desiredTerrain.Name);
        }
    }
}
