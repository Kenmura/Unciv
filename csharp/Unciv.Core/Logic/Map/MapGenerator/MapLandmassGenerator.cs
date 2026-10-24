using Unciv.Logic.Map.Tiles;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using Unciv.Utils;

namespace Unciv.Logic.Map.MapGenerator;

// core/src/com/unciv/logic/map/mapgenerator/MapLandmassGenerator.kt
// Float/Double mix mirrors Kotlin: float locals stay float, Float.pow(Float) goes through KotlinMath.Pow, Float hypot/atan2/sqrt are
// computed in double and rounded to float. (Math.Sqrt(x*x+y*y) stands in for Math.hypot: the float rounding hides the difference.)
public class MapLandmassGenerator
{
    private readonly TileMap tileMap;
    private readonly MapGenerationRandomness randomness;

    private readonly string landTerrainName;
    private readonly string waterTerrainName;
    private readonly bool landOnlyMod;
    private double waterThreshold;

    public MapLandmassGenerator(TileMap tileMap, Ruleset ruleset, MapGenerationRandomness randomness)
    {
        this.tileMap = tileMap;
        this.randomness = randomness;
        landTerrainName = GetInitializationTerrain(ruleset, TerrainType.Land);
        try
        {
            waterTerrainName = GetInitializationTerrain(ruleset, TerrainType.Water);
        }
        catch (InvalidOperationException)
        {
            waterTerrainName = landTerrainName;
        }
        landOnlyMod = waterTerrainName == landTerrainName;
    }

    // this is called from TileMap constructors as well
    internal static string GetInitializationTerrain(Ruleset ruleset, TerrainType type) =>
        ruleset.Terrains.Values.FirstOrDefault(it => it.Type == type && !((IHasUniques)it).HasUnique(UniqueType.NoNaturalGeneration))?.Name
            ?? throw new InvalidOperationException($"Cannot create map - no {type} terrains found!");

    public void GenerateLand()
    {
        // This is to accommodate land-only mods
        if (landOnlyMod)
        {
            foreach (var tile in tileMap.Values)
                tile.BaseTerrain = landTerrainName;
            return;
        }

        waterThreshold = tileMap.MapParameters.WaterThreshold;

        switch (tileMap.MapParameters.Type)
        {
            case MapType.Pangaea: CreatePangaea(); break;
            case MapType.InnerSea: CreateInnerSea(); break;
            case MapType.ContinentAndIslands: CreateContinentAndIslands(); break;
            case MapType.TwoContinents: CreateTwoContinents(); break;
            case MapType.ThreeContinents: CreateThreeContinents(); break;
            case MapType.FourCorners: CreateFourCorners(); break;
            case MapType.Archipelago: CreateArchipelago(); break;
            case MapType.Perlin: CreatePerlin(); break;
            case MapType.Fractal: CreateFractal(); break;
            case MapType.Lakes: CreateLakes(); break;
            case MapType.Boreal: CreateBoreal(); break;
            case MapType.SmallContinents: CreateSmallContinents(); break;
            case MapType.Spiral: CreateSpiral(); break;
            default: throw new InvalidOperationException($"No landmass generator for map type {tileMap.MapParameters.Type}");
        }

        if (tileMap.MapParameters.Shape == MapShape.FlatEarth)
            GenerateFlatEarthExtraWater();
    }

    private void GenerateFlatEarthExtraWater()
    {
        foreach (var tile in tileMap.Values)
        {
            bool isCenterTile = tile.Latitude == 0 && tile.Longitude == 0;
            bool isEdgeTile = tile.Neighbors.Count < 6;

            if (!isCenterTile && !isEdgeTile) continue;

            /*
            Flat Earth needs a 3 tile wide water perimeter and a 4 tile radius water center.
            This helps map generators to not place important things there which would be destroyed
            when the ice walls are placed there.
            */
            tile.BaseTerrain = waterTerrainName;
            foreach (var neighbor in tile.Neighbors)
            {
                neighbor.BaseTerrain = waterTerrainName;
                foreach (var neighbor2 in neighbor.Neighbors)
                {
                    neighbor2.BaseTerrain = waterTerrainName;
                    if (!isCenterTile) continue;
                    foreach (var neighbor3 in neighbor2.Neighbors)
                        neighbor3.BaseTerrain = waterTerrainName;
                }
            }
        }
    }

    private void SpawnLandOrWater(Tile tile, double elevation)
    {
        tile.BaseTerrain = elevation < waterThreshold ? waterTerrainName : landTerrainName;
    }

    /// <summary>
    /// Repeat <paramref name="function"/> until <paramref name="predicate"/> is <c>true</c>, lowering <see cref="waterThreshold"/> on each retry, preventing an endless loop.
    /// The predicate receives the proportion of water on the map, the default (null) accepts &lt;= 70%.
    /// </summary>
    private void RetryLoweringWaterLevel(Action function, Func<float, bool>? predicate = null)
    {
        predicate ??= waterPercent => waterPercent <= 0.7f;
        int retries = 0;
        while (++retries <= 30) // 28 is enough to go from +1 to -1 with only the retries acceleration below
        {
            function();
            float waterPercent = (float)tileMap.Values.Count(it => it.BaseTerrain == waterTerrainName) / tileMap.Values.Count;
            if (waterThreshold < -1f || predicate(waterPercent)) break;

            // lower water table to reduce water percentage, with empiric base step and acceleration
            // (tweaked to acceptable performance on huge maps - but feel free to improve)
            waterThreshold -= 0.02 *
                Math.Max(waterPercent / 0.7f, 1f) *
                KotlinMath.Pow(retries, 0.5f);
        }
    }

    #region Type-specific generators

    private void CreatePerlin()
    {
        double elevationSeed = randomness.RNG.NextInt();
        foreach (var tile in tileMap.Values)
        {
            double elevation = randomness.GetPerlinNoise(tile, elevationSeed);
            SpawnLandOrWater(tile, elevation);
        }
    }

    private void CreateFractal()
    {
        RetryLoweringWaterLevel(() =>
        {
            double elevationSeed = randomness.RNG.NextInt();
            foreach (var tile in tileMap.Values)
            {
                int maxdim = Math.Max(tileMap.MaxLatitude, tileMap.MaxLongitude);
                double ratio = maxdim / 32.0; // change scale depending on map size so that average number of continents stay the same
                if (tileMap.MapParameters.Shape == MapShape.Hexagonal || tileMap.MapParameters.Shape == MapShape.FlatEarth)
                    ratio *= 0.5; // In hexagonal type map for some reason it tends to make a single continent like pangaea if we don't diminish the scale

                double elevation = randomness.GetPerlinNoise(tile, elevationSeed, persistence: 0.8, lacunarity: 1.5, scale: ratio * 30.0);

                elevation += GetOceanEdgesTransform(tile);

                SpawnLandOrWater(tile, elevation);
            }
        });
    }

    /// <summary>
    /// A large region that is all tundra and well forested.
    /// Implemented based on screenshot from Civ V: https://i.imgur.com/L63zfv6.jpeg
    /// </summary>
    private void CreateBoreal()
    {
        // There are two layers of noise
        // The fine noise layer affects lake formation and has no impact on overall elevation
        // The broad layer impacts the coastline and can cause small oceans to spawn inland
        double lakesSeed = randomness.RNG.NextInt();
        double broadNoiseSeed = randomness.RNG.NextInt(); // wavy coast in the north

        // Lake frequency is not affected by map size, important for rivers
        const double lakesScale = 5.0;
        // The broad noise layer scales sublinearly with map size (relatively less noisy on smaller maps)
        double broadNoiseScale = 3.0 * Math.Sqrt(tileMap.MapParameters.MapSize.Radius);

        foreach (var tile in tileMap.Values)
        {
            // In range -1.0 to +1.0
            double latitude =
                tileMap.MapParameters.Shape == MapShape.FlatEarth
                    // North is at the edges instead of at the top
                    ? 2 * MapGeneratorSeams.GetTileRadius(tile, tileMap) - 1
                    : (double)tile.Latitude / tileMap.MaxLatitude;
            // Elevation declines faster and faster towards the north
            const double southElevation = 0.15;
            const double northElevation = -0.4;
            const int b = 4;
            double elevation = southElevation + (northElevation - southElevation) * Math.Pow(Math.E, b * latitude - b);

            // The threshold ensures small lakes and rivers spawn irrespective of water level (map parameter)
            const double lakeThreshold = 0.43;
            bool shouldSpawnLake = GetRidgedPerlinNoise(tile, lakesSeed, scale: lakesScale) > lakeThreshold;
            if (shouldSpawnLake)
                elevation -= 0.5;

            const double broadLayerImpact = 0.55;
            elevation += broadLayerImpact * randomness.GetPerlinNoise(tile, broadNoiseSeed, scale: broadNoiseScale);
            SpawnLandOrWater(tile, elevation);
        }
    }

    private void CreateSpiral()
    {
        double elevationSeed = randomness.RNG.NextInt();
        double radius = tileMap.MapParameters.MapSize.Radius;
        int flipX = randomness.RNG.NextBoolean() ? -1 : 1;
        int flipY = randomness.RNG.NextBoolean() ? -1 : 1;
        int coordinateDivisor = tileMap.Width; // prevent stretching of spiral

        // config
        double spinFactor = 4.5 * Math.Sqrt(radius); // how quickly the spiral spins
        double noiseScale = 0.5 * Math.Sqrt(radius); // lower means more grainy noise

        foreach (var tile in tileMap.Values)
        {
            var coordinate = HexMath.Hex2WorldCoords(tile.Position);
            float x = coordinate.x / coordinateDivisor * flipX;
            float y = coordinate.y / coordinateDivisor * flipY;

            // spiral function with output range -1 to +1
            float angle = (float)Math.Atan2(y, x);
            float hypot = (float)Math.Sqrt((double)x * x + (double)y * y);
            double elevation = Math.Sin(angle - spinFactor * hypot);

            // config
            elevation *= 0.25; // adjust range
            elevation += 0.15; // water level
            elevation += 0.18 * randomness.GetPerlinNoise(tile, elevationSeed, scale: noiseScale);

            SpawnLandOrWater(tile, elevation);
        }
    }

    private void CreateLakes()
    {
        double elevationSeed = randomness.RNG.NextInt();
        foreach (var tile in tileMap.Values)
        {
            double elevation = 0.3 - GetRidgedPerlinNoise(tile, elevationSeed, persistence: 0.7, lacunarity: 1.5);

            SpawnLandOrWater(tile, elevation);
        }
    }

    private void CreateSmallContinents()
    {
        double elevationSeed = randomness.RNG.NextInt();
        waterThreshold += 0.25;
        RetryLoweringWaterLevel(() =>
        {
            foreach (var tile in tileMap.Values)
            {
                double elevation = GetRidgedPerlinNoise(tile, elevationSeed, scale: 22.0);
                elevation += GetOceanEdgesTransform(tile);
                SpawnLandOrWater(tile, elevation);
            }
        });
    }

    private void CreateArchipelago()
    {
        double elevationSeed = randomness.RNG.NextInt();
        waterThreshold += 0.25;
        foreach (var tile in tileMap.Values)
        {
            double elevation = GetRidgedPerlinNoise(tile, elevationSeed);
            SpawnLandOrWater(tile, elevation);
        }
    }

    private void CreatePangaea()
    {
        int largeContinentThreshold = Math.Max(
            Math.Min(25, tileMap.Values.Count / 4), // Or really tiny maps will exhaust retries
            KotlinMath.FloatToInt(KotlinMath.Pow(tileMap.Values.Count, 0.333f))); // kicks in on really large maps: 130k tiles -> 50
        RetryLoweringWaterLevel(
            () =>
            {
                double elevationSeed = randomness.RNG.NextInt();
                foreach (var tile in tileMap.Values)
                {
                    double elevation = randomness.GetPerlinNoise(tile, elevationSeed);
                    elevation = elevation * (3 / 4f) + GetEllipticContinent(tile) / 4;
                    SpawnLandOrWater(tile, elevation);
                    tile.SetTerrainTransients(); // necessary for assignContinents
                }
                tileMap.AssignContinents(TileMap.AssignContinentsMode.Reassign); // to support largeContinents above
            },
            waterPercent =>
            {
                int largeContinents = tileMap.ContinentSizes.Values.Count(it => it > largeContinentThreshold);
                return largeContinents == 1 && waterPercent <= 0.7f;
            });
        tileMap.AssignContinents(TileMap.AssignContinentsMode.Clear);
    }

    private void CreateInnerSea()
    {
        double elevationSeed = randomness.RNG.NextInt();
        foreach (var tile in tileMap.Values)
        {
            double elevation = randomness.GetPerlinNoise(tile, elevationSeed);
            elevation -= GetEllipticContinent(tile, 0.6) * 0.3;
            SpawnLandOrWater(tile, elevation);
        }
    }

    private bool IsLatitudeForTwoLandAreas()
    {
        var shape = tileMap.MapParameters.Shape;
        var size = tileMap.MapParameters.MapSize;
        if (shape == MapShape.Hexagonal || shape == MapShape.FlatEarth) return randomness.RNG.NextDouble() > 0.5f;
        if (size.Height > size.Width) return true;
        if (size.Width > size.Height) return false;
        return randomness.RNG.NextDouble() > 0.5f;
    }

    private void CreateContinentAndIslands()
    {
        bool isNorth = randomness.RNG.NextDouble() < 0.5;
        bool isLatitude = IsLatitudeForTwoLandAreas();

        double elevationSeed = randomness.RNG.NextInt();
        foreach (var tile in tileMap.Values)
        {
            double elevation = randomness.GetPerlinNoise(tile, elevationSeed);
            elevation = (elevation + GetContinentAndIslandsTransform(tile, isNorth, isLatitude)) / 2.0;
            SpawnLandOrWater(tile, elevation);
        }
    }

    private void CreateTwoContinents()
    {
        bool isLatitude = IsLatitudeForTwoLandAreas();

        double elevationSeed = randomness.RNG.NextInt();
        foreach (var tile in tileMap.Values)
        {
            double elevation = randomness.GetPerlinNoise(tile, elevationSeed);
            elevation = (elevation + GetTwoContinentsTransform(tile, isLatitude)) / 2.0;
            SpawnLandOrWater(tile, elevation);
        }
    }

    private void CreateThreeContinents()
    {
        bool isNorth = randomness.RNG.NextDouble() < 0.5;
        // On flat earth maps we can randomly do East or West instead of North or South
        bool isEastWest = tileMap.MapParameters.Shape == MapShape.FlatEarth && randomness.RNG.NextDouble() > 0.5;

        double elevationSeed = randomness.RNG.NextInt();
        foreach (var tile in tileMap.Values)
        {
            double elevation = randomness.GetPerlinNoise(tile, elevationSeed);
            elevation = (elevation + GetThreeContinentsTransform(tile, isNorth, isEastWest)) / 2.0;
            SpawnLandOrWater(tile, elevation);
        }
    }

    private void CreateFourCorners()
    {
        double elevationSeed = randomness.RNG.NextInt();
        foreach (var tile in tileMap.Values)
        {
            double elevation = randomness.GetPerlinNoise(tile, elevationSeed);
            elevation = elevation / 2 + GetFourCornersTransform(tile) / 2;
            SpawnLandOrWater(tile, elevation);
        }
    }

    #endregion
    #region Shaping helpers

    /// <summary>
    /// Create an elevation map that favors a central elliptic continent spanning over 85% - 95% of
    /// the map size.
    /// </summary>
    private double GetEllipticContinent(Tile tile, double percentOfMap = 0.85)
    {
        double randomScale = randomness.RNG.NextDouble();
        double ratio = percentOfMap + 0.1 * randomness.RNG.NextDouble();

        double a = ratio * tileMap.MaxLongitude;
        double b = ratio * tileMap.MaxLatitude;
        float x = tile.Longitude;
        float y = tile.Latitude;

        double distanceFactor = x * x / (a * a) + y * y / (b * b) + KotlinMath.Pow(x + y, 2) / Math.Pow(a + b, 2);

        return Math.Min(0.3, 1.0 - (5.0 * distanceFactor * distanceFactor + randomScale) / 3.0);
    }

    private double GetContinentAndIslandsTransform(Tile tile, bool isNorth, bool isLatitude)
    {
        // The idea here is to create a water area separating the two land areas.
        // So what we do it create a line of water in the middle - where latitude or longitude is close to 0.
        double randomScale = randomness.RNG.NextDouble();
        var (longitudeFactor, latitudeFactor) = GetLonLatFactors(tile);

        // We then pick one side to become islands instead of a continent
        if (isLatitude)
        {
            if ((isNorth && tile.Latitude < 0) || (!isNorth && tile.Latitude > 0))
                latitudeFactor = 0.2f;
        }
        else
        {
            // In longitude mode North represents West
            if ((isNorth && tile.Longitude < 0) || (!isNorth && tile.Longitude > 0))
                longitudeFactor = 0.2f;
        }

        float factor = isLatitude ? latitudeFactor : longitudeFactor;

        // If this is a world wrap, we want it to be separated on both sides -
        // so we make the actual strip of water thinner, but we put it both in the middle of the map and on the edges of the map
        if (tileMap.MapParameters.WorldWrap)
            factor = Math.Min(factor, GetReverseLongitudeFactor(tile.Longitude)) * 1.1f;

        // there's nothing magical about this, it's just what we got from playing around with a lot of different options -
        //   the numbers can be changed if you find that something else creates better looking continents
        return Math.Min(0.2, -1.0 + (5.0 * KotlinMath.Pow(factor, 0.6f) + randomScale) / 3.0);
    }

    private double GetTwoContinentsTransform(Tile tile, bool isLatitude)
    {
        // The idea here is to create a water area separating the two land areas.
        // So what we do it create a line of water in the middle - where latitude or longitude is close to 0.
        double randomScale = randomness.RNG.NextDouble();
        var (longitudeFactor, latitudeFactor) = GetLonLatFactors(tile);

        float factor = isLatitude ? latitudeFactor : longitudeFactor;

        // If this is a world wrap, we want it to be separated on both sides -
        // so we make the actual strip of water thinner, but we put it both in the middle of the map and on the edges of the map
        if (tileMap.MapParameters.WorldWrap)
            factor = Math.Min(longitudeFactor, GetReverseLongitudeFactor(tile.Longitude)) * 1.5f;

        // there's nothing magical about this, it's just what we got from playing around with a lot of different options -
        //   the numbers can be changed if you find that something else creates better looking continents
        return Math.Min(0.2, -1.0 + (5.0 * KotlinMath.Pow(factor, 0.6f) + randomScale) / 3.0);
    }

    private double GetThreeContinentsTransform(Tile tile, bool isNorth, bool isEastWest)
    {
        // The idea here is to create a water area separating the three land areas.
        // So what we do it create a line of water in the middle - where latitude or longitude is close to 0.
        double randomScale = randomness.RNG.NextDouble();
        var (longitudeFactor, latitudeFactor) = GetLonLatFactors(tile);

        // 3rd continent should use only half the map width, or if flat earth, only a third
        float sizeReductionFactor = tileMap.MapParameters.Shape == MapShape.FlatEarth ? 3f : 2f;

        // We then pick one side to be merged into one centered continent instead of two cornered.
        if (isEastWest)
        {
            // In EastWest mode North represents West
            if ((isNorth && tile.Longitude < 0) || (!isNorth && tile.Longitude > 0))
                latitudeFactor = Math.Max(0f, tileMap.MaxLatitude - Math.Abs(tile.Latitude * sizeReductionFactor)) / tileMap.MaxLatitude;
        }
        else
        {
            if ((isNorth && tile.Latitude < 0) || (!isNorth && tile.Latitude > 0))
                longitudeFactor = Math.Max(0f, GetReverseLongitudeFactor(KotlinMath.FloatToInt(Math.Abs(tile.Longitude * sizeReductionFactor))));
        }

        float factor = Math.Min(longitudeFactor, latitudeFactor);

        // If this is a world wrap, we want it to be separated on both sides -
        // so we make the actual strip of water thinner, but we put it both in the middle of the map and on the edges of the map
        if (tileMap.MapParameters.WorldWrap)
            factor = Math.Min(factor, GetReverseLongitudeFactor(tile.Longitude)) * 1.5f;

        // there's nothing magical about this, it's just what we got from playing around with a lot of different options -
        //   the numbers can be changed if you find that something else creates better looking continents
        return Math.Min(0.2, -1.0 + (5.0 * KotlinMath.Pow(factor, 0.5f) + randomScale) / 3.0);
    }

    private double GetFourCornersTransform(Tile tile)
    {
        // The idea here is to create a water area separating the four land areas.
        // So what we do it create a line of water in the middle - where latitude or longitude is close to 0.
        double randomScale = randomness.RNG.NextDouble();
        var (longitudeFactor, latitudeFactor) = GetLonLatFactors(tile);

        float factor = (float)Math.Sqrt(longitudeFactor * latitudeFactor) / 1.5f;

        // If this is a world wrap, we want it to be separated on both sides -
        // so we make the actual strip of water thinner, but we put it both in the middle of the map and on the edges of the map
        if (tileMap.MapParameters.WorldWrap)
            factor = Math.Min(factor, GetReverseLongitudeFactor(tile.Longitude)) * 1.5f;

        float shouldBeWater = 1 - factor;

        // there's nothing magical about this, it's just what we got from playing around with a lot of different options -
        //   the numbers can be changed if you find that something else creates better looking continents
        return 1.0 - (5.0 * shouldBeWater * shouldBeWater + randomScale) / 3.0;
    }

    private double GetOceanEdgesTransform(Tile tile)
    {
        // The idea is to reduce elevation at the border of the map, so that we have mostly ocean there.
        int maxX = tileMap.MaxLongitude;
        int maxY = tileMap.MaxLatitude;
        int x = tile.Longitude;
        int y = tile.Latitude;

        double elevationOffset = 0.0;

        double xdistanceratio = (double)Math.Abs(x) / maxX;
        double ydistanceratio = (double)Math.Abs(y) / maxY;

        if (tileMap.MapParameters.Shape == MapShape.Hexagonal || tileMap.MapParameters.Shape == MapShape.FlatEarth)
        {
            const double startdropoffratio = 0.8; // distance from center at which we start decreasing elevation linearly

            // On wrapping maps, we must NOT reduce elevation at the East/West edges, otherwise
            // land will never span across the map seam.
            // We ignore the horizontal (X) ratio and only apply the ocean drop-off to the
            // North/South (Y) poles.
            double distancefromcenter = tileMap.MapParameters.WorldWrap && tileMap.MapParameters.Shape == MapShape.Hexagonal
                ? ydistanceratio
                : Math.Sqrt(xdistanceratio * xdistanceratio + ydistanceratio * ydistanceratio);

            double distanceoffset = 0.0;
            if (distancefromcenter > startdropoffratio)
            {
                double dropoffdistance = distancefromcenter - startdropoffratio;
                const double normalizationDivisor = 1.0 - startdropoffratio; // for normalizing to [0;1] range
                distanceoffset = dropoffdistance / normalizationDivisor;
            }
            elevationOffset -= distanceoffset * 0.35;
        }
        else
        {
            double xoffset = 0.0;

            const double xstartdropoffratio = 0.8;
            // Only force oceans on the left/right edges of rectangular maps if the world is NOT wrapping.
            if (!tileMap.MapParameters.WorldWrap && xdistanceratio > xstartdropoffratio)
            {
                double xdropoffdistance = xdistanceratio - xstartdropoffratio;
                const double xnormalizationdivisor = 1.0 - xstartdropoffratio; // for normalizing to [0;1] range
                xoffset = xdropoffdistance / xnormalizationdivisor;
            }

            double yoffset = 0.0;

            const double ystartdropoffratio = 0.76; // we want to have enough space at the north and south for circumnavigation
            if (ydistanceratio > ystartdropoffratio)
            {
                double ydropoffdistance = ydistanceratio - ystartdropoffratio;
                const double ynormalizationdivisor = 1.0 - ystartdropoffratio; // for normalizing to [0;1] range
                yoffset = ydropoffdistance / ynormalizationdivisor;
            }
            // these factors were just found by trial and error to be adequate for having enough space while not reducing land too much.
            elevationOffset -= xoffset * 0.33;
            elevationOffset -= yoffset * 0.35;
        }

        return Math.Max(elevationOffset, -0.35);
    }

    /// <summary>Generates ridged perlin noise. As for parameters see <see cref="MapGenerationRandomness.GetPerlinNoise"/></summary>
    private double GetRidgedPerlinNoise(Tile tile, double seed,
        int nOctaves = 10,
        double persistence = 0.5,
        double lacunarity = 2.0,
        double scale = 10.0)
    {
        return randomness.GetNoise(tile, seed, nOctaves, persistence, lacunarity, scale, Perlin.RidgedNoise3d);
    }

    /// <summary>Returns lon at lat "percentile from center" - numbers between 0.0-0.1</summary>
    private (float longitudeFactor, float latitudeFactor) GetLonLatFactors(Tile tile)
    {
        float longitudeFactor = Math.Abs(tile.Longitude) / (float)tileMap.MaxLongitude;
        float latitudeFactor = Math.Abs(tile.Latitude) / (float)tileMap.MaxLatitude;
        return (longitudeFactor, latitudeFactor);
    }

    /// <summary>For world wrap maps, we want the "1-N" factor of the tile</summary>
    private float GetReverseLongitudeFactor(int longitude) =>
        1 - Math.Abs(longitude) / (float)tileMap.MaxLongitude;

    #endregion
}
