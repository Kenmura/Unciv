using System.Globalization;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map.MapGenerator.Mapregions;
using Unciv.Logic.Map.Tiles;
using Unciv.Models;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using Unciv.UI.Screens.MapEditorScreen;
using Unciv.Utils;

namespace Unciv.Logic.Map.MapGenerator;

// core/src/com/unciv/logic/map/mapgenerator/MapGenerator.kt
// Map generator, used by new game, map editor and main menu background.
// Deviations:
// - The coroutine scope becomes a CancellationToken (early abort when cancelled).
// - UncivGame.VERSION is Version.Current. A seed of 0 means "use the current time" in Kotlin; wall-clock time is banned in the engine,
//   so it is an explicit input: the optional currentTimeMillis delegate, and without one a seed of 0 is an error.
// - The gameInfo parameter is the IGameInfoState seam of GameInfo (T046 retypes it).
// - Kotlin keeps the water tiles of spawnLakesAndCoasts in a HashSet<Tile> and takes first(): Tile has identity hashing there, so the order
//   was arbitrary. Here it is the map's tile order. The per-step debug timing output (consoleTimings = false) is stripped.
// - convertTerrains on the river step iterates the resulting tile set, a Kotlin mutableSetOf (LinkedHashSet, insertion ordered): LinkedHashSet<T> here.
// - Kotlin's unused gameParameters argument of resolveAutoMapSize is dropped.
public class MapGenerator
{
    private readonly CancellationToken cancellationToken;
    private readonly Func<long>? currentTimeMillis;
    private readonly MapGenerationRandomness randomness = new();
    private readonly List<TerrainOccursRange> terrainConditions;
    private readonly List<TerrainOccursRange> baseTerrainPicker;
    private readonly List<TerrainOccursRange> terrainFeaturePicker;

    public Ruleset Ruleset { get; }

    public MapGenerator(Ruleset ruleset, CancellationToken cancellationToken = default, Func<long>? currentTimeMillis = null)
    {
        Ruleset = ruleset;
        this.cancellationToken = cancellationToken;
        this.currentTimeMillis = currentTimeMillis;
        terrainConditions = ruleset.Terrains.Values
            .Where(it => !((IHasUniques)it).HasUnique(UniqueType.NoNaturalGeneration))
            .SelectMany(GetGenerationConditions)
            .OrderBy(it => it.IsConstrained) // stable, like Kotlin sortedBy
            .ToList();
        baseTerrainPicker = terrainConditions
            .Where(it => it.Terrain.Type == TerrainType.Land || it.Terrain.Type == TerrainType.Water).ToList();
        terrainFeaturePicker = terrainConditions
            .Where(it => it.Terrain.Type == TerrainType.TerrainFeature).ToList();
    }

    /// <returns>Radius in range 0.0 (center of map) to 1.0 (edge of map).</returns>
    internal static double GetTileRadius(Tile tile, TileMap tileMap)
    {
        // Numbers betwee 0.0-1.0
        double latitudeRatio = Math.Abs(tile.Latitude) / (double)tileMap.MaxLatitude;
        double longitudeRatio = Math.Abs(tile.Longitude) / (double)tileMap.MaxLongitude;
        return Math.Sqrt(Math.Pow(latitudeRatio, 2) + Math.Pow(longitudeRatio, 2));
    }

    /// <summary>Associates <see cref="Terrain"/> with a range of temperatures and a range of humidities (both open to closed)</summary>
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

        /// <summary>builds a <see cref="TerrainOccursRange"/> for <paramref name="terrain"/> from a <paramref name="unique"/> (type <see cref="UniqueType.TileGenerationConditions"/>)</summary>
        public TerrainOccursRange(Terrain terrain, Unique unique)
            : this(terrain,
                ToFloatMakeInclusive(unique.Params[0], -1f), ParseFloat(unique.Params[1]),
                ToFloatMakeInclusive(unique.Params[2], 0f), ParseFloat(unique.Params[3]),
                isConstrained: true)
        {
        }

        /// <summary>builds a <see cref="TerrainOccursRange"/> for <paramref name="terrain"/> without a TileGenerationConditions unique</summary>
        public TerrainOccursRange(Terrain terrain)
            : this(terrain, -float.MaxValue, float.MaxValue, -float.MaxValue, float.MaxValue, isConstrained: false)
        {
        }

        private static float ParseFloat(string text) => float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);

        /// <summary>A toFloat that also nudges the value slightly down if it matches <paramref name="limit"/> to make the resulting range inclusive on the lower end</summary>
        private static float ToFloatMakeInclusive(string text, float limit)
        {
            var result = ParseFloat(text);
            if (result != limit) return result;
            var magnitude = MathF.Abs(result);
            var ulp = MathF.BitIncrement(magnitude) - magnitude; // Float.ulp
            return result - ulp;
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

    private static IEnumerable<TerrainOccursRange> GetGenerationConditions(Terrain terrain)
    {
        var conditions = ((IHasUniques)terrain).GetMatchingUniques(UniqueType.TileGenerationConditions)
            .Select(unique => new TerrainOccursRange(terrain, unique)).ToList();
        return conditions.Count == 0 ? [new TerrainOccursRange(terrain)] : conditions;
    }

    private long ResolveSeed(long seed)
    {
        if (seed != 0L) return seed;
        if (currentTimeMillis == null)
            throw new InvalidOperationException("MapParameters.Seed is 0: the engine has no wall clock, seed explicitly or pass currentTimeMillis");
        return currentTimeMillis();
    }

    public TileMap GenerateMap(MapParameters mapParameters, GameParameters? gameParameters = null, IGameInfoState? gameInfo = null)
    {
        gameParameters ??= new GameParameters();
        var mapSize = mapParameters.MapSize.Name != MapSize.AutoName ? mapParameters.MapSize : ResolveAutoMapSize(mapParameters, gameInfo);
        var mapType = mapParameters.Type;

        mapParameters.Seed = ResolveSeed(mapParameters.Seed);

        randomness.SeedRNG(mapParameters.Seed);

        var map = mapParameters.Shape == MapShape.Rectangular
            ? new TileMap(mapSize.Width, mapSize.Height, Ruleset, mapParameters.WorldWrap)
            : new TileMap(mapSize.Radius, Ruleset, mapParameters.WorldWrap);

        mapParameters.CreatedWithVersion = Unciv.Logic.Version.Current.ToSerializeString();
        map.MapParameters = mapParameters;

        if (mapType == MapType.Empty)
        {
            foreach (var tile in map.Values)
            {
                tile.BaseTerrain = Constants.Ocean;
                tile.SetTerrainTransients();
            }

            return map;
        }

        RunAndMeasure(() => new MapLandmassGenerator(map, Ruleset, randomness).GenerateLand());
        RunAndMeasure(() => ApplyHumidityAndTemperature(map));
        RunAndMeasure(() => new MapElevationGenerator(map, Ruleset, terrainConditions, randomness).RaiseMountainsAndHills());
        RunAndMeasure(() => SpawnLakesAndCoasts(map));
        RunAndMeasure(() => SpawnVegetation(map));
        RunAndMeasure(() => SpawnRareFeatures(map));
        RunAndMeasure(() => SpawnIce(map));
        RunAndMeasure(() => map.AssignContinents(TileMap.AssignContinentsMode.Assign));
        RunAndMeasure(() => new RiverGenerator(map, randomness, Ruleset).SpawnRivers());
        ConvertTerrains(map.Values);

        // Region based map generation - not used when generating maps in map editor
        var civilizations = gameInfo?.Civilizations;
        var isMapEditor = civilizations == null || civilizations.Count == 0;
        if (!isMapEditor)
        {
            map.GameInfo = gameInfo!;
            var regions = new MapRegions(Ruleset);
            var majorCivs = civilizations!.Where(it => Ruleset.Nations[it.CivName].IsMajorCiv).ToList();
            RunAndMeasure(() => regions.GenerateRegions(map, majorCivs.Count));
            RunAndMeasure(() => regions.AssignRegions(map, majorCivs.Cast<IMapRegionsCivilization>().ToList(), gameParameters));
            // Natural wonders need to go before most resources since there is a minimum distance
            RunAndMeasure(() => new NaturalWonderGenerator(Ruleset, randomness).SpawnNaturalWonders(map));
            RunAndMeasure(() => regions.PlaceResourcesAndMinorCivs(map,
                civilizations!.Where(it => Ruleset.Nations[it.CivName].IsCityState).Cast<IMapRegionsCivilization>().ToList()));
        }
        else
        {
            RunAndMeasure(() => new NaturalWonderGenerator(Ruleset, randomness).SpawnNaturalWonders(map));
            // Fallback spread resources function - used when generating maps in map editor
            RunAndMeasure(() => SpreadResources(map));
        }
        RunAndMeasure(() => SpreadAncientRuins(map));

        if (isMapEditor)
            Mirror(map);

        // Map generation may generate incompatible terrain/feature combinations
        foreach (var tile in map.Values)
            TileNormalizer.NormalizeToRuleset(tile, Ruleset);

        return map;
    }

    private static MapSize ResolveAutoMapSize(MapParameters mapParameters, IGameInfoState? gameInfo)
    {
        if (gameInfo == null) return mapParameters.MapSize;

        var numberOfMajorCivs = gameInfo.Civilizations.Count(it => it.IsMajorCiv());
        var numberOfMinorCivs = gameInfo.Civilizations.Count(it => it.IsCityState);
        // This is mostly just vibes, tries to make the average distance between a civ and its closest neighbor as close to 13 tiles as possible
        var majorCivContribution = unchecked(384 * numberOfMajorCivs - 134);
        var targetNumberOfTiles = unchecked(majorCivContribution + numberOfMinorCivs * 60);

        // Calculates mapsize from tile number, simple algebra reversing area formulas
        const double aspectRatio = 1.55; // This is around the default aspect ratios
        mapParameters.MapSize.Radius = KotlinMath.RoundToInt(Math.Sqrt(1.0 / 3 * targetNumberOfTiles - 1.0 / 12) + 1.0 / 2);
        mapParameters.MapSize.Height = KotlinMath.RoundToInt(Math.Sqrt(targetNumberOfTiles / aspectRatio));
        mapParameters.MapSize.Width = KotlinMath.RoundToInt(Math.Sqrt(targetNumberOfTiles / aspectRatio) * aspectRatio);

        return mapParameters.MapSize;
    }

    private static HexCoord FlipTopBottom(HexCoord vector) => HexCoord.Of(-vector.Y, -vector.X);
    private static HexCoord FlipLeftRight(HexCoord vector) => HexCoord.Of(vector.Y, vector.X);

    private static void Mirror(TileMap map)
    {
        var mirroringType = map.MapParameters.Mirroring;

        Tile? GetMirrorTile(Tile tile)
        {
            HexCoord mirrorTileVector;
            if (mirroringType == MirroringType.TopBottom)
            {
                if (tile.GetRow() <= 0) return null;
                mirrorTileVector = FlipTopBottom(tile.Position);
            }
            else if (mirroringType == MirroringType.LeftRight)
            {
                if (tile.GetColumn() <= 0) return null;
                mirrorTileVector = FlipLeftRight(tile.Position);
            }
            else if (mirroringType == MirroringType.AroundCenterTile)
            {
                if (tile.GetRow() <= 0) return null;
                mirrorTileVector = FlipLeftRight(FlipTopBottom(tile.Position));
            }
            else if (mirroringType == MirroringType.FourWay)
            {
                if (tile.GetRow() < 0 && tile.GetColumn() < 0) return null;
                if (tile.GetRow() < 0 && tile.GetColumn() >= 0) mirrorTileVector = FlipLeftRight(tile.Position);
                else if (tile.GetRow() >= 0 && tile.GetColumn() < 0) mirrorTileVector = FlipTopBottom(tile.Position);
                else mirrorTileVector = FlipLeftRight(FlipTopBottom(tile.Position));
            }
            else return null;
            return map.GetIfTileExistsOrNull(mirrorTileVector.X, mirrorTileVector.Y);
        }

        void CopyTile(Tile tile)
        {
            var mirrorTile = GetMirrorTile(tile);
            if (mirrorTile == null) return;

            tile.SetBaseTerrain(mirrorTile.GetBaseTerrain());
            tile.NaturalWonder = mirrorTile.NaturalWonder;
            tile.SetTerrainFeatures(mirrorTile.TerrainFeatures);
            tile.TileResource = mirrorTile.TileResource;
            tile.ResourceAmount = mirrorTile.ResourceAmount;
            tile.SetImprovementBasic(mirrorTile.TileImprovement);

            foreach (var neighbor in tile.Neighbors)
            {
                var neighborMirror = GetMirrorTile(neighbor);
                if (neighborMirror == null) continue;
                if (!mirrorTile.Neighbors.Contains(neighborMirror)) continue; // we landed on the edge here
                tile.SetConnectedByRiver(neighbor, mirrorTile.IsConnectedByRiver(neighborMirror));
            }
        }

        if (map.MapParameters.Mirroring == MirroringType.None) return;
        foreach (var tile in map.Values)
            CopyTile(tile);
    }

    public void GenerateSingleStep(TileMap map, MapGeneratorSteps step)
    {
        map.MapParameters.Seed = ResolveSeed(map.MapParameters.Seed);

        randomness.SeedRNG(map.MapParameters.Seed);

        RunAndMeasure(() =>
        {
            switch (step)
            {
                case MapGeneratorSteps.None: break;
                case MapGeneratorSteps.All:
                    throw new ArgumentException("MapGeneratorSteps.All cannot be used in generateSingleStep");
                case MapGeneratorSteps.Landmass: new MapLandmassGenerator(map, Ruleset, randomness).GenerateLand(); break;
                case MapGeneratorSteps.HumidityAndTemperature: ApplyHumidityAndTemperature(map); break;
                case MapGeneratorSteps.Elevation: new MapElevationGenerator(map, Ruleset, terrainConditions, randomness).RaiseMountainsAndHills(); break;
                case MapGeneratorSteps.LakesAndCoast: SpawnLakesAndCoasts(map); break;
                case MapGeneratorSteps.Vegetation: SpawnVegetation(map); break;
                case MapGeneratorSteps.RareFeatures: SpawnRareFeatures(map); break;
                case MapGeneratorSteps.Ice: SpawnIce(map); break;
                case MapGeneratorSteps.Continents: map.AssignContinents(TileMap.AssignContinentsMode.Reassign); break;
                case MapGeneratorSteps.NaturalWonders: new NaturalWonderGenerator(Ruleset, randomness).SpawnNaturalWonders(map); break;
                case MapGeneratorSteps.Rivers:
                    var resultingTiles = new LinkedHashSet<Tile>();
                    new RiverGenerator(map, randomness, Ruleset).SpawnRivers(resultingTiles);
                    ConvertTerrains(resultingTiles);
                    break;
                case MapGeneratorSteps.Resources: SpreadResources(map); break;
                case MapGeneratorSteps.AncientRuins: SpreadAncientRuins(map); break;
                default: throw new ArgumentOutOfRangeException(nameof(step), step, null);
            }
        });
    }

    private void RunAndMeasure(Action action)
    {
        if (cancellationToken.IsCancellationRequested) return;
        action();
    }

    public void ConvertTerrains(IEnumerable<Tile> tiles) => Helpers.ConvertTerrains(Ruleset, tiles);

    public static class Helpers
    {
        public static void ConvertTerrains(Ruleset ruleset, IEnumerable<Tile> tiles)
        {
            foreach (var tile in tiles)
            {
                var conversionUnique = ((IHasUniques)tile.GetBaseTerrain())
                    .GetMatchingUniques(UniqueType.ChangesTerrain, new GameContext(tile: tile))
                    .FirstOrDefault(it => tile.IsAdjacentTo(it.Params[1]));
                if (conversionUnique == null) continue;
                if (!ruleset.Terrains.TryGetValue(conversionUnique.Params[0], out var terrain)) continue;

                if (terrain.Type != TerrainType.TerrainFeature)
                    tile.BaseTerrain = terrain.Name;
                else if (!terrain.OccursOn.Contains(tile.LastTerrain.Name)) continue;
                else
                    tile.AddTerrainFeature(terrain.Name);
                tile.SetTerrainTransients();
            }
        }
    }

    private void SpreadCoast(TileMap map, List<TerrainOccursRange> coasts)
    {
        for (int i = 1; i <= map.MapParameters.MaxCoastExtension; i++)
        {
            var toCoast = new List<Tile>();
            foreach (var tile in map.Values.Where(it => it.IsOcean).ToList())
            {
                var tilesInDistance = tile.GetTilesInDistance(1);
                foreach (var neighborTile in tilesInDistance)
                {
                    if (neighborTile.IsLand)
                    {
                        toCoast.Add(tile);
                        break;
                    }
                    if (neighborTile.GetBaseTerrain().IsCoast)
                    {
                        var randbool = randomness.RNG.NextBoolean();
                        if (randbool)
                            toCoast.Add(tile);
                        break;
                    }
                }
            }
            foreach (var tile in toCoast)
            {
                var matching = coasts.Where(it => it.MatchesHumidityAndTemp(tile)).ToList();
                var coast = (matching.Count == 0 ? coasts : matching).Random(randomness.RNG);
                tile.BaseTerrain = coast.Name;
                tile.SetTransients();
            }
        }
    }

    private void SpawnLakesAndCoasts(TileMap map)
    {
        var lakeTerrains = terrainConditions.Where(it => it.IsFreshwater && !it.RareFeature && it.Terrain.Type == TerrainType.Water).ToList();
        if (lakeTerrains.Count > 0)
        {
            //define lakes
            var waterTilesInOrder = map.Values.Where(it => it.IsWater).ToList();
            var waterTiles = new HashSet<Tile>(waterTilesInOrder);

            var tilesInArea = new HashSet<Tile>();
            var tilesToCheck = new Queue<Tile>();

            var maxLakeSize = Ruleset.ModOptions.Constants.MaxLakeSize;

            foreach (var initialWaterTile in waterTilesInOrder)
            {
                if (!waterTiles.Remove(initialWaterTile)) continue;
                tilesInArea.Add(initialWaterTile);
                tilesToCheck.Enqueue(initialWaterTile);

                // Floodfill to cluster water tiles
                while (tilesToCheck.Count > 0)
                {
                    var tileWeAreChecking = tilesToCheck.Dequeue();
                    foreach (var vector in tileWeAreChecking.Neighbors)
                    {
                        if (tilesInArea.Contains(vector)) continue;
                        if (!waterTiles.Contains(vector)) continue;
                        tilesInArea.Add(vector);
                        tilesToCheck.Enqueue(vector);
                        waterTiles.Remove(vector);
                    }
                }

                var matching = lakeTerrains.Where(it => it.MatchesHumidityAndTemp(initialWaterTile)).ToList();
                var lakeTerrain = (matching.Count == 0 ? lakeTerrains : matching).Random(randomness.RNG);
                if (tilesInArea.Count <= maxLakeSize)
                {
                    foreach (var tile in tilesInArea)
                    {
                        tile.BaseTerrain = lakeTerrain.Name;
                        tile.SetTransients();
                    }
                }
                tilesInArea.Clear();
            }
        }

        //Coasts
        var coasts = terrainConditions.Where(it => it.IsCoast && !it.RareFeature).ToList();
        if (coasts.Count > 0)
            SpreadCoast(map, coasts);
    }

    private void SpreadAncientRuins(TileMap map)
    {
        var ruinsEquivalents = Ruleset.TileImprovements.Where(it => it.Value.IsAncientRuinsEquivalent()).ToList();
        if (map.MapParameters.NoRuins || ruinsEquivalents.Count == 0) return;

        static bool IsPlaceable(TileImprovement improvement, Tile tile) =>
            tile.ImprovementFunctions.CanImprovementBeBuiltHere(improvement, gameContext: GameContext.IgnoreConditionalsState);

        var suitableTiles = map.Values.Where(it => it.IsLand && !it.IsImpassible()
            && ruinsEquivalents.Any(improvement => IsPlaceable(improvement.Value, it))).ToList();
        var locations = randomness.ChooseSpreadOutLocations(
            KotlinMath.RoundToInt(suitableTiles.Count * Ruleset.ModOptions.Constants.AncientRuinCountMultiplier),
            suitableTiles,
            map.MapParameters.MapSize.Radius);
        foreach (var tile in locations)
        {
            var rng = new GameContext(tile: tile).StateBasedRandom("MapGenerator.spreadAncientRuins");
            var ruins = ruinsEquivalents.Where(it => IsPlaceable(it.Value, tile)).Select(it => it.Value).ToList().Random(rng);
            tile.SetImprovementBasic(ruins);
        }
    }

    private void SpreadResources(TileMap tileMap)
    {
        var mapRadius = tileMap.MapParameters.MapSize.Radius;
        foreach (var tile in tileMap.Values)
            tile.TileResource = null;

        var snowTerrains = terrainConditions.Where(it => it.MaybeSnow()).ToList();
        SpreadStrategicResources(tileMap, mapRadius, snowTerrains);
        SpreadResources(tileMap, mapRadius, ResourceType.Luxury, snowTerrains);
        SpreadResources(tileMap, mapRadius, ResourceType.Bonus, snowTerrains);
    }

    // Here, we need each specific resource to be spread over the map - it matters less if specific resources are near each other
    private void SpreadStrategicResources(TileMap tileMap, int mapRadius, List<TerrainOccursRange> snowTerrains)
    {
        var strategicResources = Ruleset.TileResources.Values.Where(it => it.ResourceType == ResourceType.Strategic).ToList();
        // passable land tiles (no mountains, no wonders) without resources yet
        // can't be next to NW
        var candidateTiles = tileMap.Values.Where(it => it.Resource == null && !it.IsImpassible()
            && !it.Neighbors.Any(neighbor => neighbor.IsNaturalWonder())).ToList();
        var totalNumberOfResources = candidateTiles.Count(it => it.IsLand) * tileMap.MapParameters.ResourceRichness;
        var resourcesPerType = KotlinMath.FloatToInt(totalNumberOfResources / strategicResources.Count);
        foreach (var resource in strategicResources)
        {
            // remove the tiles where previous resources have been placed
            var suitableTiles = candidateTiles
                .Where(it => !(snowTerrains.Any(terrain => it.BaseTerrain == terrain.Name) && it.IsHill()))
                .Where(it => it.Resource == null && resource.GeneratesNaturallyOn(it))
                .ToList();

            var locations = randomness.ChooseSpreadOutLocations(resourcesPerType, suitableTiles, mapRadius);

            foreach (var location in locations) location.SetTileResource(resource, rng: randomness.RNG);
        }
    }

    /// <summary>
    /// Spreads resources of type <paramref name="resourceType"/> picking locations at a minimum distance from each other,
    /// which is determined from <paramref name="mapRadius"/> and then tuned down until the desired number fits.
    /// <see cref="MapParameters.ResourceRichness"/> used to control how many resources to spawn.
    /// </summary>
    private void SpreadResources(TileMap tileMap, int mapRadius, ResourceType resourceType, List<TerrainOccursRange> snowTerrains)
    {
        var resourcesOfType = Ruleset.TileResources.Values.Where(it => it.ResourceType == resourceType).ToList();

        var suitableTiles = tileMap.Values
            .Where(it => !(snowTerrains.Any(terrain => it.BaseTerrain == terrain.Name) && it.IsHill()))
            .Where(it => it.Resource == null
                && !it.Neighbors.Any(neighbor => neighbor.IsNaturalWonder())
                && resourcesOfType.Any(r => r.GeneratesNaturallyOn(it)))
            .ToList();
        var numberOfResources = tileMap.Values.Count(it => it.IsLand && !it.IsImpassible()) *
            tileMap.MapParameters.ResourceRichness;
        var locations = randomness.ChooseSpreadOutLocations(KotlinMath.FloatToInt(numberOfResources), suitableTiles, mapRadius);

        var resourceToNumber = new Counter<string>();

        foreach (var tile in locations)
        {
            var possibleResources = resourcesOfType.Where(it => it.GeneratesNaturallyOn(tile)).ToList();
            if (possibleResources.Count == 0) continue;
            var resourceWithLeastAssignments = possibleResources[0]; // minByOrNull: the first minimum wins
            foreach (var candidate in possibleResources)
                if (resourceToNumber[candidate.Name] < resourceToNumber[resourceWithLeastAssignments.Name])
                    resourceWithLeastAssignments = candidate;
            resourceToNumber.Add(resourceWithLeastAssignments.Name, 1);
            tile.SetTileResource(resourceWithLeastAssignments, rng: randomness.RNG);
        }
    }

    /// <summary>Kotlin <c>Double.sign</c> (NaN stays NaN, unlike Math.Sign)</summary>
    private static double Sign(double value) => value > 0 ? 1.0 : value < 0 ? -1.0 : value;

    /// <summary>
    /// <see cref="MapParameters.TilesPerBiomeArea"/> to set biomes size
    /// <see cref="MapParameters.TemperatureIntensity"/> to favor very high and very low temperatures
    /// <see cref="MapParameters.TemperatureShift"/> to shift temperature towards cold (negative) or hot (positive)
    /// </summary>
    private void ApplyHumidityAndTemperature(TileMap tileMap)
    {
        double humiditySeed = randomness.RNG.NextInt();
        double temperatureSeed = randomness.RNG.NextInt();

        tileMap.SetTransients(Ruleset);

        double scale = tileMap.MapParameters.TilesPerBiomeArea;
        var temperatureintensity = tileMap.MapParameters.TemperatureIntensity;
        var temperatureShift = tileMap.MapParameters.TemperatureShift;
        var humidityShift = temperatureShift > 0 ? -temperatureShift / 2 : 0f;

        // List is OK here as it's only sequentially scanned
        var landTerrains = baseTerrainPicker.Where(it => it.Terrain.Type == TerrainType.Land && !it.Terrain.Impassable && !it.IsRough && !it.RareFeature).ToList();
        var coastTerrains = baseTerrainPicker.Where(it => it.Terrain.IsCoast && !it.RareFeature).ToList();
        var oceanTerrains = baseTerrainPicker.Where(it => it.Terrain.IsOcean && !it.RareFeature).ToList();
        var noTerrainUniques = !landTerrains.Any(it => it.IsConstrained);
        var elevationTerrains = baseTerrainPicker.Where(it => it.OccursInChains).Select(it => it.Name).ToHashSet();

        // Temperature at the provided tile before adding noise etc..
        // May be outside of range -1.0 to 1.0, so make sure to coerce it at some point.
        double GetExpectedTemperature(Tile tile)
        {
            // Latitude in range -1.0 (south pole equivalent) to +1.0 (north pole equivalent).
            var normalizedLatitude =
                tileMap.MapParameters.Shape == MapShape.FlatEarth ? 2 * GetTileRadius(tile, tileMap) - 1
                : tile.Latitude / (double)tileMap.MaxLatitude;
            // Flat earth temperature should be -1.0 at latitudes ±0.9, and -1.111 at latitudes ±1.0.
            // Instead of adjusting the temperature later, we can adjust the latitude here.
            // This way, custom map types don't have to worry as much about flat earth logic.
            var adjustedLatitude =
                tileMap.MapParameters.Shape == MapShape.FlatEarth ? normalizedLatitude * 10.0 / 9.0
                : normalizedLatitude;
            // This part translates from latitude to temperature.
            // Starting temperature is -0.4 across most (southern ~75%) of the map, but declines to -1.0 in the north so ice can spawn
            if (tileMap.MapParameters.Type == MapType.Boreal)
                return -0.4 - 0.6 * Math.Pow(Math.E, 5 * adjustedLatitude - 5);
            // Cold poles, warm equator. Most map types use this function.
            return 1.0 - 2.0 * Math.Abs(adjustedLatitude);
        }

        foreach (var tile in tileMap.Values)
        {
            if (elevationTerrains.Contains(tile.BaseTerrain))
                continue;

            var humidityRandom = randomness.GetPerlinNoise(tile, humiditySeed, scale: scale, nOctaves: 1);
            var humidity = Math.Clamp((humidityRandom + 1.0) / 2.0 + humidityShift, 0.0, 1.0);
            tile.Humidity = humidity;

            var expectedTemperature = GetExpectedTemperature(tile);

            var randomTemperature = randomness.GetPerlinNoise(tile, temperatureSeed, scale: scale, nOctaves: 1);
            var temperature = (5.0 * expectedTemperature + randomTemperature) / 6.0;
            temperature = Math.Pow(Math.Abs(temperature), 1.0 - temperatureintensity) * Sign(temperature);
            temperature = Math.Clamp(temperature + temperatureShift, -1.0, 1.0);
            tile.Temperature = temperature;

            // Old, static map generation rules - necessary for existing base ruleset mods to continue to function
            if (noTerrainUniques)
            {
                string autoTerrain;
                if (temperature < -0.4) autoTerrain = humidity < 0.5 ? Constants.Snow : Constants.Tundra;
                else if (temperature < 0.8) autoTerrain = humidity < 0.5 ? Constants.Plains : Constants.Grassland;
                else if (temperature <= 1.0) autoTerrain = humidity < 0.7 ? Constants.Desert : Constants.Plains;
                else continue; // Invalid temperature
                if (Ruleset.Terrains.ContainsKey(autoTerrain))
                {
                    tile.BaseTerrain = autoTerrain;
                    tile.SetTerrainTransients();
                }
                continue;
            }

            var terrains =
                tile.IsLand ? landTerrains
                : tile.GetBaseTerrain().IsCoast ? coastTerrains
                : oceanTerrains;
            var matches = terrains.Where(it => it.MatchesTempAndTerrain(tile)).ToList();
            var matchingTerrain = (matches.Count == 0 ? terrains : matches).RandomOrNull(randomness.RNG);

            if (matchingTerrain != null)
            {
                tile.BaseTerrain = matchingTerrain.Name;
                tile.SetTerrainTransients();
            }
        }
    }

    /// <summary><see cref="MapParameters.VegetationRichness"/> is the threshold for vegetation spawn</summary>
    private void SpawnVegetation(TileMap tileMap)
    {
        double vegetationSeed = randomness.RNG.NextInt();
        var vegetationTerrains = terrainFeaturePicker.Where(it => it.HasVegitation && !it.RareFeature).ToList();
        if (vegetationTerrains.Count == 0)
            vegetationTerrains = terrainFeaturePicker.Where(it => Constants.Vegetation.Contains(it.Name)).ToList();
        var candidateTerrains = vegetationTerrains.SelectMany(it => it.Terrain.OccursOn).ToHashSet();
        // some map types are more forested than others
        double vegetationRichness = tileMap.MapParameters.VegetationRichness + (tileMap.MapParameters.Type == MapType.Boreal ? 0.10 : 0.0);
        // Checking it.baseTerrain in candidateTerrains to make sure forest does not spawn on desert hill
        foreach (var tile in tileMap.Values.Where(it => candidateTerrains.Contains(it.BaseTerrain)
            && candidateTerrains.Contains(it.LastTerrain.Name)))
        {
            var vegetation = (randomness.GetPerlinNoise(tile, vegetationSeed, scale: 3.0, nOctaves: 1) + 1.0) / 2.0;

            if (vegetation <= vegetationRichness)
            {
                var possibleVegetation = vegetationTerrains.Where(it => it.MatchesTempAndTerrain(tile)
                    && NaturalWonderGenerator.FitsTerrainUniques(it.Terrain, tile)).ToList();
                if (possibleVegetation.Count == 0) continue;
                var randomVegetation = possibleVegetation.Random(randomness.RNG);
                tile.HasVegetation = true;
                tile.AddTerrainFeature(randomVegetation.Name);
            }
        }
    }

    /// <summary><see cref="MapParameters.RareFeaturesRichness"/> is the probability of spawning a rare feature</summary>
    private void SpawnRareFeatures(TileMap tileMap)
    {
        var rareFeatures = terrainFeaturePicker.Where(it => it.RareFeature).ToList();
        foreach (var tile in tileMap.Values.Where(it => it.TerrainFeatures.Count == 0))
        {
            if (randomness.RNG.NextDouble() <= tileMap.MapParameters.RareFeaturesRichness)
            {
                var hillFeature = tile.GetHillTerrain();
                var possibleFeatures = rareFeatures.Where(it => it.MatchesTempAndTerrain(tile)
                    && (hillFeature == null || it.Terrain.OccursOn.Contains(hillFeature.Name))
                    && NaturalWonderGenerator.FitsTerrainUniques(it.Terrain, tile)).ToList();
                if (possibleFeatures.Count > 0)
                    tile.AddTerrainFeature(possibleFeatures.Random(randomness.RNG).Name);
            }
        }
    }

    /// <summary><see cref="MapParameters.TemperatureIntensity"/> as in <see cref="ApplyHumidityAndTemperature"/></summary>
    private void SpawnIce(TileMap tileMap)
    {
        var oceanTerrains = baseTerrainPicker.Where(it => it.Terrain.IsOcean && it.TempFrom <= -1).ToList();
        if (oceanTerrains.Count == 0)
            oceanTerrains = terrainFeaturePicker.Where(it => it.Terrain.IsOcean).ToList();
        var iceTerrains = terrainFeaturePicker.Where(it => it.Terrain.IsIce).ToList();

        if (iceTerrains.Count == 0) return;

        if (tileMap.MapParameters.Shape == MapShape.FlatEarth)
            SpawnFlatEarthIceWalls(tileMap, iceTerrains, oceanTerrains);

        tileMap.SetTransients(Ruleset);
        double temperatureSeed = randomness.RNG.NextInt();
        foreach (var tile in tileMap.Values)
        {
            if (!oceanTerrains.Any(it => it.Name == tile.BaseTerrain) || tile.TerrainFeatures.Count != 0)
                continue;

            var randomTemperature = randomness.GetPerlinNoise(tile, temperatureSeed, scale: tileMap.MapParameters.TilesPerBiomeArea, nOctaves: 1);
            var latitudeTemperature = 1.0 - 2.0 * Math.Abs(tile.Latitude) / tileMap.MaxLatitude;
            var iceTemperature = (latitudeTemperature + randomTemperature) / 2.0;
            iceTemperature = Math.Pow(Math.Abs(iceTemperature), 1.0 - tileMap.MapParameters.TemperatureIntensity) * Sign(iceTemperature);
            iceTemperature = Math.Clamp(iceTemperature + tileMap.MapParameters.TemperatureShift, -1.0, 1.0);
            // This is quite different from the normal tile temperature. upstream wants to unify the two

            var iceTerrain = iceTerrains
                .Where(it => it.MatchesTempAndTerrain(tile, iceTemperature)
                    && NaturalWonderGenerator.FitsTerrainUniques(it.Terrain, tile))
                .Select(it => it.Terrain.Name).ToList()
                .RandomOrNull(randomness.RNG);
            if (iceTerrain != null)
                tile.AddTerrainFeature(iceTerrain);
        }
    }

    private void SpawnFlatEarthIceWalls(TileMap tileMap, List<TerrainOccursRange> iceTerrains, List<TerrainOccursRange> oceanTerrains)
    {
        var snowTerrains = baseTerrainPicker.Where(it => it.MaybeSnow()).ToList();
        var mountainTerrains = baseTerrainPicker.Where(it => it.Terrain.Impassable && it.OccursInChains && !it.RareFeature).ToList();
        var allArcticTerrains = iceTerrains.Concat(snowTerrains).Concat(mountainTerrains).ToList();

        // Skip the tile loop if nothing can be done in it
        if (allArcticTerrains.Count == 0) return;

        // Flat Earth needs a 1 tile wide perimeter of ice/mountain/snow and a 2 radius cluster of ice in the center.
        foreach (var tile in tileMap.Values)
        {
            var isCenterTile = tile.Latitude == 0 && tile.Longitude == 0;
            var isEdgeTile = tile.Neighbors.Count < 6;

            // Make center tiles ice or snow or mountain depending on availability
            if (isCenterTile)
                SpawnFlatEarthCenterIceWall(tile, iceTerrains, mountainTerrains, oceanTerrains);

            // Make edge tiles randomly ice or snow or mountain if available
            if (isEdgeTile)
                SpawnFlatEarthEdgeIceWall(tile, allArcticTerrains, iceTerrains);
        }
    }

    private void SpawnBestIce(
        Tile tile,
        List<TerrainOccursRange> iceTerrains,
        List<TerrainOccursRange> mountainTerrains,
        List<TerrainOccursRange> oceanTerrains)
    {
        var ice = iceTerrains.Where(it => it.MatchesTempAndTerrain(tile, -1.0)).ToList().RandomOrNull(randomness.RNG)
            ?? iceTerrains.RandomOrNull(randomness.RNG);
        if (ice != null)
        {
            if (!ice.MatchesTempAndTerrain(tile, -1.0))
            {
                var fallbackBases = oceanTerrains.Where(it => ice.Terrain.OccursOn.Contains(it.Name)).ToList();
                if (fallbackBases.Count == 0)
                    fallbackBases = oceanTerrains.Where(it => it.IsOcean).ToList();
                var fallbackBase = fallbackBases.RandomOrNull(randomness.RNG);
                if (fallbackBase != null)
                    tile.BaseTerrain = fallbackBase.Name;
            }
            tile.RemoveTerrainFeatures();
            tile.AddTerrainFeature(ice.Terrain.Name);
            tile.SetTerrainTransients();
        }
        else
        {
            var mountain =
                mountainTerrains.Where(it => it.MatchesHumidity(tile, -1.0)).ToList().RandomOrNull(randomness.RNG)
                ?? mountainTerrains.Random(randomness.RNG);
            tile.BaseTerrain = mountain.Terrain.Name;
            tile.RemoveTerrainFeatures();
        }
        tile.SetTerrainTransients();
    }

    private void SpawnFlatEarthCenterIceWall(
        Tile tile,
        List<TerrainOccursRange> iceTerrains,
        List<TerrainOccursRange> mountainTerrains,
        List<TerrainOccursRange> oceanTerrains)
    {
        // Spawn ice on center tile
        SpawnBestIce(tile, iceTerrains, mountainTerrains, oceanTerrains);

        // Spawn circle of ice around center tile
        foreach (var neighbor in tile.Neighbors)
        {
            SpawnBestIce(neighbor, iceTerrains, mountainTerrains, oceanTerrains);

            // Spawn partial circle of ice around circle of ice
            foreach (var neighbor2 in neighbor.Neighbors)
            {
                // Do nothing most of the time at random.
                if (randomness.RNG.NextDouble() > 0.75)
                    SpawnBestIce(neighbor2, iceTerrains, mountainTerrains, oceanTerrains);
            }
        }
    }

    private void SpawnRandomIce(Tile tile, TerrainOccursRange arcticTerrain, List<TerrainOccursRange> iceTerrains)
    {
        tile.RemoveTerrainFeatures();
        if (arcticTerrain.Terrain.Type == TerrainType.Land && arcticTerrain.Terrain.Impassable)
        {
            tile.BaseTerrain = arcticTerrain.Terrain.Name;
        }
        else if (arcticTerrain.Terrain.Type == TerrainType.Land)
        {
            // legacy code forced ice on snow even though that's impossible
            tile.BaseTerrain = arcticTerrain.Terrain.Name;
            tile.AddTerrainFeature(iceTerrains.Random(randomness.RNG).Terrain.Name);
        }
        else if (arcticTerrain.Terrain.Type == TerrainType.TerrainFeature && arcticTerrain.MatchesTempAndTerrain(tile))
        {
            tile.AddTerrainFeature(arcticTerrain.Terrain.Name);
        }
        else
        {
            // legacy code forced ice on ocean without checking if possible
            tile.BaseTerrain = Constants.Ocean;
            tile.AddTerrainFeature(arcticTerrain.Terrain.Name);
        }
        tile.SetTerrainTransients();
    }

    private void SpawnFlatEarthEdgeIceWall(Tile tile, List<TerrainOccursRange> arcticTerrains, List<TerrainOccursRange> iceTerrains)
    {
        var arcticTerrain = arcticTerrains.Where(it => it.MatchesTempAndTerrain(tile, -1.0)).ToList().RandomOrNull(randomness.RNG)
            ?? arcticTerrains.Where(it => it.MatchesHumidity(tile, -1.0)).ToList().RandomOrNull(randomness.RNG)
            ?? arcticTerrains.Random(randomness.RNG);
        SpawnRandomIce(tile, arcticTerrain, iceTerrains);

        // Spawn partial circle of arctic tiles next to the edge
        foreach (var neighbor in tile.Neighbors)
        {
            var neighborIsEdgeTile = neighbor.Neighbors.Count < 6;
            // Do not redo edge tile. It is already done.
            // Do nothing most of the time at random.
            if (!neighborIsEdgeTile && randomness.RNG.NextDouble() >= 0.75)
                SpawnRandomIce(tile, arcticTerrain, iceTerrains);
        }
    }
}
