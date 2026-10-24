using Unciv.Logic.Map.Tiles;
using Unciv.Utils;

namespace Unciv.Logic.Map.MapGenerator;

// core/src/com/unciv/logic/map/mapgenerator/MapGenerationRandomness.kt
// Deviation: Kotlin's `inline fun getNoise(..., noiseFunction)` takes a lambda; here it is a Func delegate.
public class MapGenerationRandomness
{
    public GameRandom RNG { get; set; } = new(42L);

    public void SeedRNG(long seed = 42) => RNG = new GameRandom(seed);

    /// <summary>
    /// Generates a perlin noise channel combining multiple octaves
    /// Default settings generate mostly within [-0.55, 0.55], but clustered around 0.0
    /// About 28% are &lt; -0.1 and 28% are &gt; 0.1
    /// </summary>
    /// <param name="tile">Source for x / x coordinates.</param>
    /// <param name="seed">Misnomer: actually the z value the Perlin cloud is 'cut' on.</param>
    /// <param name="nOctaves">is the number of octaves.</param>
    /// <param name="persistence">is the scaling factor of octave amplitudes.</param>
    /// <param name="lacunarity">is the scaling factor of octave frequencies.</param>
    /// <param name="scale">is the distance the noise is observed from.</param>
    public double GetPerlinNoise(
        Tile tile,
        double seed,
        int nOctaves = 6,
        double persistence = 0.5,
        double lacunarity = 2.0,
        double scale = 30.0)
    {
        return GetNoise(tile, seed, nOctaves, persistence, lacunarity, scale, Perlin.Noise3d);
    }

    /// <summary>Generates a noise value using the specified noise function, accounting for world wrap.</summary>
    /// <param name="tile">Source for coordinates.</param>
    /// <param name="seed">Z-axis value (often used as a variation seed).</param>
    /// <param name="nOctaves">is the number of octaves.</param>
    /// <param name="persistence">is the scaling factor of octave amplitudes.</param>
    /// <param name="lacunarity">is the scaling factor of octave frequencies.</param>
    /// <param name="scale">is the distance the noise is observed from.</param>
    /// <param name="noiseFunction">The underlying noise function to use (e.g., Perlin.Noise3d or Perlin.RidgedNoise3d).</param>
    public double GetNoise(
        Tile tile,
        double seed,
        int nOctaves,
        double persistence,
        double lacunarity,
        double scale,
        Func<double, double, double, int, double, double, double, double> noiseFunction)
    {
        var worldCoords = HexMath.Hex2WorldCoords(tile.Position);
        var parameters = tile.TileMap.MapParameters;

        // Non-wrapping maps use standard 2D noise which naturally has a seam at the edges.
        if (!parameters.WorldWrap)
            return noiseFunction(worldCoords.x, worldCoords.y, seed, nOctaves, persistence, lacunarity, scale);

        // We wrap the 2D map plane into a 3D cylinder. By calculating noise on the surface of this
        // cylinder, the left edge (0 deg) and right edge (360 deg) of the map occupy the exact same
        // coordinates in 3D noise space, making the transition mathematically perfect.
        double radius;
        if (parameters.Shape == MapShape.Rectangular)
        {
            int width = parameters.MapSize.Width;
            int adjustedWidth = width % 2 != 0 ? width - 1 : width;
            radius = adjustedWidth / 2.0;
        }
        else radius = parameters.MapSize.Radius;

        // Map horizontal world coordinates to a circle. In Unciv HexMath, a full wrap is 3.0 * radius.
        double wrapPeriod = 3.0 * radius;
        double angle = worldCoords.x / wrapPeriod * 2 * Math.PI;
        double cylinderRadius = wrapPeriod / (2 * Math.PI);

        // Unciv's Perlin noise algorithm uses a 256-element permutation array, meaning the core
        // noise pattern mathematically repeats every 256 integer units.
        // Because the input coordinates are divided by `scale` inside the noise function, the actual
        // spatial period before the terrain pattern repeats is exactly `256.0 * scale`.
        double maxOffset = 256.0 * scale;

        // Generate three independent, uncorrelated spatial offsets from the single map seed
        // (SplitMix64 constants used as a one-off multiplicative hash; 'ushr 32' keeps the top 32 bits).
        long seedLong = KotlinMath.DoubleToLong(seed);
        double offsetX = Offset(seedLong, unchecked((long)0x9E3779B97F4A7C15UL), maxOffset);
        double offsetY = Offset(seedLong, unchecked((long)0xBF58476D1CE4E5B9UL), maxOffset);
        double offsetZ = Offset(seedLong, unchecked((long)0x94D049BB133111EBUL), maxOffset);

        // Map the 2D coordinates onto a 3D cylinder to achieve seamless X-axis world wrapping.
        double nx = cylinderRadius * Math.Cos(angle) + offsetX;
        double ny = cylinderRadius * Math.Sin(angle) + offsetY;
        double nz = worldCoords.y + offsetZ;

        return noiseFunction(nx, ny, nz, nOctaves, persistence, lacunarity, scale);
    }

    private static double Offset(long seedLong, long constant, double maxOffset)
    {
        long product = unchecked(seedLong * constant);
        return (long)((ulong)product >> 32) / 4294967296.0 * maxOffset;
    }

    public List<Tile> ChooseSpreadOutLocations(int number, IReadOnlyList<Tile> suitableTiles, int mapRadius)
    {
        if (number <= 0) return [];

        // Determine sensible initial distance from number of desired placements and mapRadius
        // empiric formula comes very close to eliminating retries for distance.
        // The `if` means if we need to fill 60% or more of the available tiles, no sense starting with minimum distance 2.
        float sparsityFactor = KotlinMath.Pow(HexMath.GetHexagonalRadiusForArea(suitableTiles.Count) / mapRadius, 0.333f);
        int initialDistance = number == 1 || number * 5 >= suitableTiles.Count * 3
            ? 1
            : Math.Max(1, KotlinMath.RoundToInt(mapRadius * 0.666f / KotlinMath.Pow(HexMath.GetHexagonalRadiusForArea(number), 0.9f) * sparsityFactor));

        // If possible, we want to equalize the base terrains upon which
        //  the resources are found, so we save how many have been
        //  found for each base terrain and try to get one from the lowest
        var baseTerrainsToChosenTiles = new JavaHashMap<string, int>();
        // Once we have a preference to choose from a specific base terrain, we want quick lookup of the available candidates
        var suitableTilesGrouped = new LinkedHashMap<string, LinkedHashSet<Tile>>();
        // Prefill both with all existing base terrains as keys, and group suitableTiles into base terrain buckets
        foreach (var tile in suitableTiles)
        {
            string terrain = tile.BaseTerrain;
            if (!baseTerrainsToChosenTiles.ContainsKey(terrain))
                baseTerrainsToChosenTiles.Put(terrain, 0);
            if (!suitableTilesGrouped.TryGetValue(terrain, out var bucket))
            {
                bucket = [];
                suitableTilesGrouped.Put(terrain, bucket);
            }
            bucket.Add(tile);
        }

        static LinkedHashMap<string, LinkedHashSet<Tile>> DeepClone(LinkedHashMap<string, LinkedHashSet<Tile>> source)
        {
            var result = new LinkedHashMap<string, LinkedHashSet<Tile>>();
            foreach (var (key, value) in source)
                result.Put(key, new LinkedHashSet<Tile>(value));
            return result;
        }

        for (int distanceBetweenResources = initialDistance; distanceBetweenResources >= 1; distanceBetweenResources--)
        {
            var availableTiles = DeepClone(suitableTilesGrouped);
            var chosenTiles = new List<Tile>(number);

            foreach (string terrain in baseTerrainsToChosenTiles.Keys.ToList())
                baseTerrainsToChosenTiles.Put(terrain, 0);

            for (int i = 1; i <= number; i++)
            {
                var orderedKeys = baseTerrainsToChosenTiles
                    .OrderBy(it => it.Value) // stable, like Kotlin sortedBy
                    .Select(it => it.Key)
                    .ToList();
                string? firstKeyWithTilesLeft = orderedKeys.FirstOrDefault(it => availableTiles[it].Count != 0);
                if (firstKeyWithTilesLeft == null) break;
                var chosenTile = availableTiles[firstKeyWithTilesLeft].Random(RNG);
                var closeTiles = chosenTile.GetTilesInDistance(distanceBetweenResources).ToList();
                foreach (var availableSet in availableTiles.Values)
                    foreach (var close in closeTiles)
                        availableSet.Remove(close);
                chosenTiles.Add(chosenTile);
                baseTerrainsToChosenTiles.Put(firstKeyWithTilesLeft, baseTerrainsToChosenTiles[firstKeyWithTilesLeft] + 1);
            }
            if (chosenTiles.Count == number || distanceBetweenResources == 1)
            {
                // Either we got them all, or we're not going to get anything better
                if (distanceBetweenResources < initialDistance)
                    Log.Debug("chooseSpreadOutLocations: distance %d < initial %d", distanceBetweenResources, initialDistance);
                return chosenTiles;
            }
        }
        // unreachable due to last loop iteration always returning and initialDistance >= 1
        throw new InvalidOperationException("Unreachable code reached!");
    }
}
