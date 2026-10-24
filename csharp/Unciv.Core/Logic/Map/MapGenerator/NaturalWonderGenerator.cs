using Unciv.Logic.Map.Tiles;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using Unciv.Utils;

namespace Unciv.Logic.Map.MapGenerator;

// core/src/com/unciv/logic/map/mapgenerator/NaturalWonderGenerator.kt
public class NaturalWonderGenerator(Ruleset ruleset, MapGenerationRandomness randomness)
{
    private readonly HashSet<Tile> blockedTiles = [];

    public Ruleset Ruleset { get; } = ruleset;
    public MapGenerationRandomness Randomness { get; } = randomness;

    /*
    https://gaming.stackexchange.com/questions/95095/do-natural-wonders-spawn-more-closely-to-city-states/96479
    https://www.reddit.com/r/civ/comments/1jae5j/information_on_the_occurrence_of_natural_wonders/
    Above all, look in assignstartingplots.lua! The wonders are always attempted to be placed in order of
    which has the least amount of candidate tiles. There is a minimum distance between wonders equal
    to the map height / 5.
    */
    public void SpawnNaturalWonders(TileMap tileMap)
    {
        if (tileMap.MapParameters.NoNaturalWonders)
            return;
        int mapRadius = tileMap.MapParameters.MapSize.Radius;
        // number of Natural Wonders scales linearly with mapRadius
        var constants = Ruleset.ModOptions.Constants;
        int numberToSpawn = KotlinMath.RoundToInt(mapRadius * constants.NaturalWonderCountMultiplier + constants.NaturalWonderCountAddedConstant);

        var chosenWonders = new List<Terrain>();
        var wonderCandidateTiles = new Dictionary<Terrain, IReadOnlyList<Tile>>();
        var allNaturalWonders = Ruleset.Terrains.Values
            .Where(it => it.Type == TerrainType.NaturalWonder).ToList();
        var spawned = new List<Terrain>();

        while (allNaturalWonders.Count != 0 && chosenWonders.Count < numberToSpawn)
        {
            float totalWeight = (float)allNaturalWonders.Sum(it => it.Weight);
            double random = Randomness.RNG.NextDouble();
            float sum = 0f;
            foreach (var wonder in allNaturalWonders)
            {
                sum += wonder.Weight / totalWeight;
                if (random <= sum)
                {
                    chosenWonders.Add(wonder);
                    allNaturalWonders.Remove(wonder);
                    break;
                }
            }
        }

        var tilesTooCloseToSpawnLocations = tileMap.StartingLocationsByNation.Values
            .SelectMany(it => it).SelectMany(it => it.GetTilesInDistance(5)).ToHashSet();

        // First attempt to spawn the chosen wonders in order of least candidate tiles
        foreach (var it in chosenWonders)
            wonderCandidateTiles[it] = GetCandidateTilesForWonder(tileMap, it, tilesTooCloseToSpawnLocations);
        chosenWonders = chosenWonders.OrderBy(it => wonderCandidateTiles[it].Count).ToList(); // stable, like sortBy
        foreach (var wonder in chosenWonders)
        {
            if (TrySpawnOnSuitableLocation(wonderCandidateTiles[wonder].Where(it => !blockedTiles.Contains(it)).ToList(), wonder))
                spawned.Add(wonder);
        }

        // If some wonders were not able to be spawned we will pull a wonder from the fallback list
        if (spawned.Count < numberToSpawn)
        {
            // Now we have to do some more calculations. Unfortunately we have to calculate candidate tiles for everyone.
            foreach (var it in allNaturalWonders)
                wonderCandidateTiles[it] = GetCandidateTilesForWonder(tileMap, it, tilesTooCloseToSpawnLocations);
            allNaturalWonders = allNaturalWonders.OrderBy(it => wonderCandidateTiles[it].Count).ToList();
            foreach (var wonder in allNaturalWonders)
            {
                if (TrySpawnOnSuitableLocation(wonderCandidateTiles[wonder].Where(it => !blockedTiles.Contains(it)).ToList(), wonder))
                    spawned.Add(wonder);
                if (spawned.Count >= numberToSpawn)
                    break;
            }
        }

        Log.Debug("Natural Wonders for this game: %s", string.Join(", ", spawned.Select(it => it.Name)));
    }

    private static List<Tile> GetCandidateTilesForWonder(TileMap tileMap, Terrain naturalWonder, ISet<Tile> tilesTooCloseToSpawnLocations) =>
        tileMap.Values.Where(tile =>
            tile.Resource == null
            && !tilesTooCloseToSpawnLocations.Contains(tile)
            && naturalWonder.OccursOn.Contains(tile.LastTerrain.Name)
            && FitsTerrainUniques(naturalWonder, tile)).ToList();

    private bool TrySpawnOnSuitableLocation(List<Tile> suitableLocations, Terrain wonder)
    {
        int minGroupSize;
        int maxGroupSize;
        var groupUnique = ((IHasUniques)wonder).GetMatchingUniques(UniqueType.NaturalWonderGroups).FirstOrDefault();
        if (groupUnique == null)
        {
            minGroupSize = 1;
            maxGroupSize = 1;
        }
        else
        {
            minGroupSize = GetIntParam(groupUnique, 0);
            maxGroupSize = GetIntParam(groupUnique, 1);
        }
        int targetGroupSize = minGroupSize == maxGroupSize ? maxGroupSize
            : new IntRange(minGroupSize, maxGroupSize).Random(Randomness.RNG);

        if (suitableLocations.Count >= minGroupSize)
        {
            var location = suitableLocations.Random(Randomness.RNG);
            var list = new List<Tile> { location };

            while (list.Count < targetGroupSize)
            {
                var allNeighbors = list.SelectMany(it => it.Neighbors).Except(list).ToHashSet();
                var candidates = suitableLocations.Where(it => allNeighbors.Contains(it)).ToList();
                if (candidates.Count == 0) break;
                list.Add(candidates.Random(Randomness.RNG));
            }
            if (list.Count >= minGroupSize)
            {
                foreach (var tileToConvert in list)
                {
                    PlaceNaturalWonder(wonder, tileToConvert);
                    // Add all tiles within a certain distance to a blacklist so NW:s don't cluster
                    tileToConvert.ForEachTileInDistance(tileToConvert.TileMap.MapParameters.MapSize.Height / 5, it => blockedTiles.Add(it));
                }

                Log.Debug("Natural Wonder %s @%s", wonder.Name, location.Position);

                return true;
            }
        }

        Log.Debug("No suitable location for %s", wonder.Name);
        return false;
    }

    public static void PlaceNaturalWonder(Terrain wonder, Tile location)
    {
        location.NaturalWonder = wonder.Name;
        var turnsIntoObject = wonder.TurnsInto == null ? null : location.Ruleset.Terrains.GetValueOrDefault(wonder.TurnsInto);
        if (turnsIntoObject != null)
        {
            ClearTile(location);
            location.SetBaseTerrain(turnsIntoObject);
        }
        else
        {
            ClearTile(location, wonder.OccursOn);
        }

        var conversionUniques = ((IHasUniques)wonder).GetMatchingUniques(UniqueType.NaturalWonderConvertNeighbors, GameContext.IgnoreConditionalsState).ToList();
        if (conversionUniques.Count == 0) return;

        foreach (var tile in location.Neighbors)
        {
            var state = new GameContext(tile: tile);
            foreach (var unique in conversionUniques)
            {
                if (!unique.ConditionalsApply(state)) continue;
                var convertTo = unique.Params[0];
                if (tile.BaseTerrain == convertTo || tile.TerrainFeatures.Contains(convertTo)) continue;
                var convertToTerrain = location.Ruleset.Terrains.GetValueOrDefault(convertTo);
                if (convertToTerrain == null) continue;
                if (((IHasUniques)convertToTerrain).HasUnique(UniqueType.FreshWater) && tile.IsAdjacentToCoast()) continue;
                if (convertToTerrain.Type == TerrainType.TerrainFeature && !convertToTerrain.OccursOn.Contains(tile.BaseTerrain)) continue;
                if (((IHasUniques)convertToTerrain).HasUnique(UniqueType.CoastalWater))
                    RemoveLakesNextToFutureCoast(tile, convertToTerrain);
                if (convertToTerrain.Type.IsBaseTerrain())
                {
                    ClearTile(tile);
                    tile.SetBaseTerrain(convertToTerrain);
                }
                if (convertToTerrain.Type == TerrainType.TerrainFeature)
                {
                    ClearTile(tile, tile.TerrainFeatures);
                    tile.AddTerrainFeature(convertTo);
                }
            }
        }
    }

    public static bool FitsTerrainUniques(Terrain naturalWonderOrTerrainFeature, Tile tile)
    {
        var asUniques = (IHasUniques)naturalWonderOrTerrainFeature;
        bool continentsRelevant = asUniques.HasUnique(UniqueType.NaturalWonderLargerLandmass)
            || asUniques.HasUnique(UniqueType.NaturalWonderSmallerLandmass);
        IReadOnlyList<int> sortedContinents = continentsRelevant ? tile.TileMap.ContinentsSortedBySize : [];

        return asUniques.UniqueObjects.All(unique =>
        {
            switch (unique.Type)
            {
                case UniqueType.NaturalWonderNeighborCount:
                    {
                        int count = tile.Neighbors.Count(it => MatchesWonderFilter(it, unique.Params[1]));
                        return count == GetIntParam(unique, 0);
                    }
                case UniqueType.NaturalWonderNeighborsRange:
                    {
                        int count = tile.Neighbors.Count(it => MatchesWonderFilter(it, unique.Params[2]));
                        return count >= GetIntParam(unique, 0) && count <= GetIntParam(unique, 1);
                    }
                case UniqueType.NaturalWonderSmallerLandmass:
                    return !sortedContinents.Take(GetIntParam(unique, 0)).Contains(tile.GetContinent());
                case UniqueType.NaturalWonderLargerLandmass:
                    return sortedContinents.Take(GetIntParam(unique, 0)).Contains(tile.GetContinent());
                case UniqueType.NaturalWonderLatitude:
                    {
                        float lower = tile.TileMap.MaxLatitude * GetIntParam(unique, 0) * 0.01f;
                        float upper = tile.TileMap.MaxLatitude * GetIntParam(unique, 1) * 0.01f;
                        float latitude = Math.Abs((float)tile.Latitude);
                        return latitude >= lower && latitude <= upper;
                    }
                default:
                    return true;
            }
        });
    }

    private static int GetIntParam(Unique unique, int index) =>
        int.Parse(unique.Params[index], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture);

    // location is being converted to a NW, tile is a neighbor to be converted to coast: Ensure that coast won't show invalid rivers or coast touching lakes
    private static void RemoveLakesNextToFutureCoast(Tile tile, Terrain coastTerrain)
    {
        var futureCoastTiles = new LinkedHashSet<Tile> { tile };
        var tilesToCheck = new List<Tile> { tile };
        int index = 0;
        while (index < tilesToCheck.Count)
        {
            foreach (var neighbor in tilesToCheck[index++].Neighbors)
            {
                if (!((IHasUniques)neighbor.GetBaseTerrain()).HasUnique(UniqueType.FreshWater)) continue;
                if (futureCoastTiles.Add(neighbor)) tilesToCheck.Add(neighbor);
            }
        }

        foreach (var lake in futureCoastTiles.Where(it => it != tile).ToList())
        {
            ClearTile(lake);
            lake.SetBaseTerrain(coastTerrain);
        }
        foreach (var futureCoast in futureCoastTiles)
        {
            foreach (var neighbor in futureCoast.Neighbors)
                futureCoast.SetConnectedByRiver(neighbor, false);
        }
    }

    /// <summary>Implements UniqueParameterType.SimpleTerrain</summary>
    private static bool MatchesWonderFilter(Tile tile, string filter)
    {
        if (filter == "Elevated") return tile.GetBaseTerrain().IsMountain || tile.IsHill();
        if (filter == "Water") return tile.IsWater;
        if (filter == "Land") return tile.IsLand;
        if (filter == Constants.Hill) return tile.IsHill();
        if (filter == tile.NaturalWonder) return true;
        if (filter == tile.LastTerrain.Name) return true;
        return tile.BaseTerrain == filter;
    }

    private static void ClearTile(Tile tile, IReadOnlyList<string>? exceptFeatures = null)
    {
        exceptFeatures ??= [];
        if (tile.TerrainFeatures.Count != 0 && !exceptFeatures.SequenceEqual(tile.TerrainFeatures))
            tile.SetTerrainFeatures(tile.TerrainFeatures.Where(it => exceptFeatures.Contains(it)).ToList());
        tile.TileResource = null;
        tile.RemoveImprovement();
        tile.SetTerrainTransients();
    }
}
