using System.Globalization;
using Unciv.Logic.Map.MapGenerator.Mapregions;
using Unciv.Logic.Map.Tiles;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using Unciv.Utils;
using ImpactType = Unciv.Logic.Map.MapGenerator.Mapregions.MapRegions.ImpactType;

namespace Unciv.Logic.Map.MapGenerator.ResourcePlacement;

// core/src/com/unciv/logic/map/mapgenerator/resourceplacement/MapRegionResources.kt
// Deviations: Kotlin Sequence<Tile> is IEnumerable<Tile>. Kotlin's argument-less shuffled() draws from the unseeded kotlin.random.Random.Default,
// which the engine does not have (BannedSymbols); every such call site shuffles with a GameRandom derived from the game state instead
// (see ResourcePlacementRandom), so shuffled orders differ from the JVM's (which are not reproducible there either), while all seeded draws keep Kotlin's sequence.

/// <summary>Shuffling for the Kotlin call sites that use the unseeded <c>shuffled()</c>.</summary>
internal static class ResourcePlacementRandom
{
    /// <summary>A deterministic stand-in for <c>Random.Default</c>, one per call site (<paramref name="caller"/>).</summary>
    internal static GameRandom ForShuffling(TileMap tileMap, string caller) =>
        new GameContext(gameInfo: tileMap.GameInfo).StateBasedRandom(caller + ".shuffled");

    /// <summary>Kotlin <c>Sequence.shuffled()</c>: lazy, and every enumeration shuffles anew.</summary>
    internal static IEnumerable<T> ShuffledSequence<T>(this IEnumerable<T> source, GameRandom random)
    {
        foreach (var item in source.Shuffled(random)) yield return item;
    }
}

/// <summary>This class deals with the internals of *how* to place resources in tiles
/// It does not contain the logic of *when* to do so</summary>
public static class MapRegionResources
{
    private static ImpactType ImpactTypeOf(ResourceType type) => type switch
    {
        ResourceType.Strategic => ImpactType.Strategic,
        ResourceType.Bonus => ImpactType.Bonus,
        ResourceType.Luxury => ImpactType.Luxury,
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    /// <summary>Given a <paramref name="tileList"/> and possible <paramref name="resourceOptions"/>, will place a resource on every <paramref name="frequency"/> tiles.
    /// Tries to avoid impacts, but falls back to lowest impact otherwise.
    /// Goes through the list in order, so pre-shuffle it!
    /// Assumes all tiles in the list are of the same terrain type when generating weightings, irrelevant if only one option.</summary>
    /// <returns>a map of the resources in the options list to number placed.</returns>
    public static Dictionary<TileResource, int> PlaceResourcesInTiles(
        TileDataMap tileData, int frequency, List<Tile> tileList, List<TileResource> resourceOptions,
        int baseImpact = 0, int randomImpact = 0, bool majorDeposit = false)
    {
        if (tileList.Count == 0 || resourceOptions.Count == 0) return [];
        if (frequency == 0) return [];
        var impactType = ImpactTypeOf(resourceOptions[0].ResourceType);
        var conditionalTerrain = new GameContext(attackedTile: tileList.FirstOrDefault());
        var weightings = new Dictionary<TileResource, float>();
        foreach (var option in resourceOptions)
        {
            var unique = ((IHasUniques)option).GetMatchingUniques(UniqueType.ResourceWeighting, conditionalTerrain).FirstOrDefault();
            weightings[option] = unique != null ? ParseFloat(unique.Params[0]) : 1f;
        }
        var amountToPlace = (tileList.Count / frequency) + 1;
        var amountPlaced = 0;
        var detailedPlaced = new Dictionary<TileResource, int>();
        foreach (var option in resourceOptions) detailedPlaced[option] = 0;
        var fallbackTiles = new List<Tile>();
        // First pass - avoid impacts entirely
        foreach (var tile in tileList)
        {
            if (tile.Resource != null) continue;
            var possibleResourcesForTile = resourceOptions.Where(it => it.GeneratesNaturallyOn(tile)).ToList();
            if (possibleResourcesForTile.Count == 0) continue;

            if (tileData[tile]!.Impacts.ContainsKey(impactType))
            {
                fallbackTiles.Add(tile); // Taken but might be a viable fallback tile
            }
            else
            {
                var rng = new GameContext(tile: tile).StateBasedRandom("MapRegionResources.placeResourcesInTiles");
                // Add a resource to the tile
                var resourceToPlace = possibleResourcesForTile.RandomWeighted(rng, it => weightings.GetValueOrDefault(it, 0f));
                tile.SetTileResource(resourceToPlace, majorDeposit);
                tileData.PlaceImpact(impactType, tile, baseImpact + rng.NextInt(randomImpact + 1));
                amountPlaced++;
                detailedPlaced[resourceToPlace] += 1;
                if (amountPlaced >= amountToPlace)
                    return detailedPlaced;
            }
        }
        // Second pass - place on least impacted tiles
        while (amountPlaced < amountToPlace && fallbackTiles.Count > 0)
        {
            // Sorry, we do need to re-sort the list for every pass since new impacts are made with every placement
            var bestTile = fallbackTiles.MinByOrNull(it => tileData[it]!.Impacts[impactType])!;
            var rng = new GameContext(tile: bestTile).StateBasedRandom("MapRegionResources.placeResourcesInTiles");
            fallbackTiles.Remove(bestTile);
            var possibleResourcesForTile = resourceOptions.Where(it => it.GeneratesNaturallyOn(bestTile)).ToList();
            var resourceToPlace = possibleResourcesForTile.RandomWeighted(rng, it => weightings.GetValueOrDefault(it, 0f));
            bestTile.SetTileResource(resourceToPlace, majorDeposit);
            tileData.PlaceImpact(impactType, bestTile, baseImpact + rng.NextInt(randomImpact + 1));
            amountPlaced++;
            detailedPlaced[resourceToPlace] += 1;
        }
        return detailedPlaced;
    }

    /// <summary>Attempts to place <paramref name="amount"/> <paramref name="resource"/> on <paramref name="tiles"/>, checking tiles in order. A <paramref name="ratio"/> below 1 means skipping
    /// some tiles, ie ratio = 0.25 will put a resource on every 4th eligible tile. Can optionally respect impact flags,
    /// and places impact if <paramref name="baseImpact"/> &gt;= 0. Returns number of placed resources.</summary>
    public static int TryAddingResourceToTiles(
        TileDataMap tileData, TileResource resource, int amount, IEnumerable<Tile> tiles, float ratio = 1f,
        bool respectImpacts = false, int baseImpact = -1, int randomImpact = 0, bool majorDeposit = false)
    {
        if (amount <= 0) return 0;
        var amountAdded = 0;
        var ratioProgress = 1f;
        var impactType = ImpactTypeOf(resource.ResourceType);

        foreach (var tile in tiles)
        {
            if (tile.Resource == null && resource.GeneratesNaturallyOn(tile))
            {
                if (ratioProgress >= 1f &&
                    !(respectImpacts && tileData[tile]!.Impacts.ContainsKey(impactType)))
                {
                    tile.SetTileResource(resource, majorDeposit);
                    ratioProgress -= 1f;
                    amountAdded++;
                    if (baseImpact + randomImpact >= 0)
                    {
                        var rng = new GameContext(tile: tile).StateBasedRandom("MapRegionResources.tryAddingResourceToTiles");
                        tileData.PlaceImpact(impactType, tile, baseImpact + rng.NextInt(randomImpact + 1));
                    }
                    if (amountAdded >= amount) break;
                }
                ratioProgress += ratio;
            }
        }
        return amountAdded;
    }

    /// <summary>Attempts to place major deposits in a <paramref name="tileList"/> consisting exclusively of <paramref name="terrain"/> tiles.
    /// Lifted out of the main function to allow postponing water resources.</summary>
    /// <returns>a map of resource types to placed deposits.</returns>
    public static Dictionary<TileResource, int> PlaceMajorDeposits(
        TileDataMap tileData, Ruleset ruleset, List<Tile> tileList, Terrain terrain, bool fallbackWeightings, int baseImpact, int randomImpact)
    {
        if (tileList.Count == 0) return [];

        var terrainUniques = (IHasUniques)terrain;
        var frequency = terrainUniques.HasUnique(UniqueType.MajorStrategicFrequency)
            ? int.Parse(terrainUniques.GetMatchingUniques(UniqueType.MajorStrategicFrequency).First().Params[0], CultureInfo.InvariantCulture)
            : 25;

        var terrainRule = MapRegionFunctions.GetTerrainRule(terrain, ruleset);
        var resourceOptions = ruleset.TileResources.Values.Where(it =>
            it.ResourceType == ResourceType.Strategic &&
            ((fallbackWeightings && it.TerrainsCanBeFoundOn.Contains(terrain.Name)) ||
                ((IHasUniques)it).UniqueObjects.Any(unique => MapRegionFunctions.AnonymizeUnique(unique).Text == terrainRule.Text))).ToList();

        return resourceOptions.Count > 0
            ? PlaceResourcesInTiles(tileData, frequency, tileList, resourceOptions, baseImpact, randomImpact, true)
            : [];
    }

    private static float ParseFloat(string text) => float.Parse(text, CultureInfo.InvariantCulture);
}
