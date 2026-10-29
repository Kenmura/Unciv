using System.Globalization;
using Unciv.Logic.Map.MapGenerator.Mapregions;
using Unciv.Logic.Map.Tiles;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using Unciv.Utils;

namespace Unciv.Logic.Map.MapGenerator.ResourcePlacement;

// core/src/com/unciv/logic/map/mapgenerator/resourceplacement/LuxuryResourcePlacementLogic.kt
// Deviations: see MapRegionResources.cs for the unseeded shuffled() calls (here: a "shuffleRng" per function). Kotlin ArrayList<Region> is List<Region>.
public static class LuxuryResourcePlacementLogic
{
    /// <summary>Assigns a luxury to each region. No luxury can be assigned to too many regions.
    /// Some luxuries are earmarked for city states. The rest are randomly distributed or
    /// don't occur at all in the map</summary>
    public static (List<string> CityStateLuxuries, List<string> RandomLuxuries) AssignLuxuries(List<Region> regions, TileDataMap tileData, Ruleset ruleset)
    {
        var globalRng = new GameContext(gameInfo: regions[0].TileMap.GameInfo).StateBasedRandom("LuxuryResourcePlacementLogic.assignLuxuries");

        // If there are any weightings defined in json, assume they are complete. If there are none, use flat weightings instead
        var fallbackWeightings = !ruleset.TileResources.Values.Any(it =>
            it.ResourceType == ResourceType.Luxury &&
            (((IHasUniques)it).HasUnique(UniqueType.ResourceWeighting) || ((IHasUniques)it).HasUnique(UniqueType.LuxuryWeightingForCityStates)));

        var maxRegionsWithLuxury = regions.Count > 14 ? 3
            : regions.Count > 10 ? 2
            : 1;

        var assignableLuxuries = ruleset.TileResources.Values.Where(it =>
            it.ResourceType == ResourceType.Luxury &&
            !((IHasUniques)it).HasUnique(UniqueType.LuxurySpecialPlacement) &&
            !((IHasUniques)it).HasUnique(UniqueType.CityStateOnlyResource) &&
            // B024: an unconditional "Doesn't generate naturally" (BNW Indonesian luxuries) keeps the resource out of the pool; conditional ones (G&K Marble) do not
            !((IHasUniques)it).UniqueObjects.Any(u => u.Type == UniqueType.NoNaturalGeneration && u.Modifiers.Count == 0)).ToList();
        var amountRegionsWithLuxury = new Dictionary<string, int>();
        // Init map
        foreach (var resource in ruleset.TileResources.Values)
            amountRegionsWithLuxury[resource.Name] = 0;

        foreach (var region in regions.OrderBy(it => MapRegionFunctions.GetRegionPriority(ruleset.Terrains.GetValueOrDefault(it.Type))).ToList())
        {
            var regionRng = new GameContext(gameInfo: region.TileMap.GameInfo, region: region).StateBasedRandom("LuxuryResourcePlacementLogic.assignLuxuries");
            var candidateLuxuries = GetCandidateLuxuries(
                assignableLuxuries,
                amountRegionsWithLuxury,
                maxRegionsWithLuxury,
                fallbackWeightings,
                region,
                ruleset);
            // If there are no candidates (mad modders???) just skip this region
            if (candidateLuxuries.Count == 0) continue;

            // Pick a luxury at random. Weight is reduced if the luxury has been picked before
            var regionConditional = new GameContext(region: region);
            region.Luxury = candidateLuxuries.RandomWeighted(regionRng, it =>
            {
                var weightingUnique = ((IHasUniques)it).GetMatchingUniques(UniqueType.ResourceWeighting, regionConditional).FirstOrDefault();
                var relativeWeight = weightingUnique == null ? 1f : ParseFloat(weightingUnique.Params[0]);
                return relativeWeight / (1f + amountRegionsWithLuxury[it.Name]);
            }).Name;
            amountRegionsWithLuxury[region.Luxury] = amountRegionsWithLuxury[region.Luxury] + 1;
        }

        var cityStateLuxuries = AssignCityStateLuxuries(
            4, // was probably intended to be "if (tileData.size > 5000) 4 else 3",
            assignableLuxuries,
            amountRegionsWithLuxury,
            fallbackWeightings,
            globalRng);

        var randomLuxuries = GetLuxuriesForRandomPlacement(
            assignableLuxuries, amountRegionsWithLuxury, tileData,
            ResourcePlacementRandom.ForShuffling(regions[0].TileMap, "LuxuryResourcePlacementLogic.getLuxuriesForRandomPlacement"));

        return (cityStateLuxuries, randomLuxuries);
    }

    private static List<string> GetLuxuriesForRandomPlacement(
        List<TileResource> assignableLuxuries,
        Dictionary<string, int> amountRegionsWithLuxury,
        TileDataMap tileData,
        GameRandom shuffleRng)
    {
        var remainingLuxuries = assignableLuxuries
            .Where(it => amountRegionsWithLuxury[it.Name] == 0)
            .Select(it => it.Name)
            .Shuffled(shuffleRng);

        var disabledPercent =
            100 - KotlinMath.FloatToInt(Math.Min(KotlinMath.Pow(tileData.Size, 0.2f) * 16, 100f)); // Approximately
        var targetDisabledLuxuries = (remainingLuxuries.Count * disabledPercent) / 100;
        return remainingLuxuries.Skip(targetDisabledLuxuries).ToList();
    }

    private static List<TileResource> GetCandidateLuxuries(
        List<TileResource> assignableLuxuries,
        Dictionary<string, int> amountRegionsWithLuxury,
        int maxRegionsWithLuxury,
        bool fallbackWeightings,
        Region region,
        Ruleset ruleset)
    {
        var regionConditional = new GameContext(region: region);

        var candidateLuxuries = assignableLuxuries.Where(it =>
            amountRegionsWithLuxury[it.Name] < maxRegionsWithLuxury &&
            // Check that it has a weight for this region type
            (fallbackWeightings ||
                ((IHasUniques)it).HasUnique(UniqueType.ResourceWeighting, regionConditional)) &&
            // Check that there is enough coast and that there is coast close enough to starting location if it is a water based resource
            ((region.GetTerrainAmount("Coastal") >= 12 &&
                region.TileMap[region.StartPosition.NotNull()].IsAdjacentToCoast()) ||
                !MapRegionFunctions.IsWaterOnlyResource(it, ruleset))).ToList();

        // If we couldn't find any options, pick from all luxuries. First try to not pick water luxuries on land regions
        if (candidateLuxuries.Count == 0)
        {
            candidateLuxuries = assignableLuxuries.Where(it =>
                amountRegionsWithLuxury[it.Name] < maxRegionsWithLuxury &&
                // Ignore weightings for this pass
                // Check that there is enough coast if it is a water based resource
                (region.GetTerrainAmount("Coastal") >= 12 ||
                    it.TerrainsCanBeFoundOn.Any(terrain => ruleset.Terrains[terrain].Type != TerrainType.Water))).ToList();
        }
        // If there are still no candidates, ignore water restrictions
        if (candidateLuxuries.Count == 0)
        {
            candidateLuxuries = assignableLuxuries.Where(it =>
                amountRegionsWithLuxury[it.Name] < maxRegionsWithLuxury
                // Ignore weightings and water for this pass
                ).ToList();
        }
        return candidateLuxuries;
    }

    private static List<string> AssignCityStateLuxuries(
        int targetCityStateLuxuries,
        List<TileResource> assignableLuxuries,
        Dictionary<string, int> amountRegionsWithLuxury,
        bool fallbackWeightings,
        GameRandom rng)
    {
        var cityStateLuxuries = new List<string>();
        for (var i = 0; i < targetCityStateLuxuries; i++)
        {
            var candidateLuxuries = assignableLuxuries.Where(it =>
                amountRegionsWithLuxury[it.Name] == 0 &&
                (fallbackWeightings || ((IHasUniques)it).HasUnique(UniqueType.LuxuryWeightingForCityStates))).ToList();
            if (candidateLuxuries.Count == 0) continue;

            var luxury = candidateLuxuries.RandomWeighted(rng, it =>
            {
                var weightingUnique = ((IHasUniques)it).GetMatchingUniques(UniqueType.LuxuryWeightingForCityStates).FirstOrDefault();
                return weightingUnique == null ? 1f : ParseFloat(weightingUnique.Params[0]);
            }).Name;
            cityStateLuxuries.Add(luxury);
            amountRegionsWithLuxury[luxury] = 1;
        }
        return cityStateLuxuries;
    }

    /// <summary>Places all Luxuries onto <paramref name="tileMap"/>. Assumes that assignLuxuries and placeMinorCivs have been called.</summary>
    public static void PlaceLuxuries(
        List<Region> regions,
        TileMap tileMap,
        TileDataMap tileData,
        Ruleset ruleset,
        List<string> cityStateLuxuries,
        List<string> randomLuxuries)
    {
        PlaceLuxuriesAtMajorCivStartLocations(regions, tileMap, ruleset, tileData, randomLuxuries);
        PlaceLuxuriesAtMinorCivStartLocations(tileMap, ruleset, regions, randomLuxuries, cityStateLuxuries, tileData);
        AddRegionalLuxuries(tileData, regions, tileMap, ruleset);
        AddRandomLuxuries(randomLuxuries, tileData, tileMap, regions, ruleset);

        var specialLuxuries = ruleset.TileResources.Values.Where(it =>
            it.ResourceType == ResourceType.Luxury &&
            ((IHasUniques)it).HasUnique(UniqueType.LuxurySpecialPlacement)).ToList();
        var placedSpecials = new Dictionary<string, int>();
        foreach (var special in specialLuxuries) placedSpecials[special.Name] = 0; // init map

        AddExtraLuxuryToStarts(
            tileMap,
            regions,
            randomLuxuries,
            specialLuxuries,
            cityStateLuxuries,
            tileData,
            ruleset,
            placedSpecials);

        FillSpecialLuxuries(specialLuxuries, tileMap, regions, placedSpecials, tileData);
    }

    /// <summary>top up marble-type specials if needed</summary>
    private static void FillSpecialLuxuries(
        List<TileResource> specialLuxuries,
        TileMap tileMap,
        List<Region> regions,
        Dictionary<string, int> placedSpecials,
        TileDataMap tileData)
    {
        var shuffleRng = ResourcePlacementRandom.ForShuffling(tileMap, "LuxuryResourcePlacementLogic.fillSpecialLuxuries");
        foreach (var special in specialLuxuries)
        {
            var targetNumber = KotlinMath.FloatToInt(regions.Count * tileMap.MapParameters.GetMapResources().GetSpecialLuxuriesTargetFactor());
            var numberToPlace = Math.Max(2, targetNumber - placedSpecials[special.Name]);
            MapRegionResources.TryAddingResourceToTiles(
                tileData, special, numberToPlace, tileMap.Values.ShuffledSequence(shuffleRng), 1f,
                true, 6, 0);
        }
    }

    private static void AddExtraLuxuryToStarts(
        TileMap tileMap,
        List<Region> regions,
        List<string> randomLuxuries,
        List<TileResource> specialLuxuries,
        List<string> cityStateLuxuries,
        TileDataMap tileData,
        Ruleset ruleset,
        Dictionary<string, int> placedSpecials)
    {
        if (tileMap.MapParameters.MapResources == MapResourceSetting.Sparse.GetLabel()) return;
        var shuffleRng = ResourcePlacementRandom.ForShuffling(tileMap, "LuxuryResourcePlacementLogic.addExtraLuxuryToStarts");
        foreach (var region in regions)
        {
            var tilesToCheck = tileMap[region.StartPosition.NotNull()].GetTilesInDistanceRange(new IntRange(1, 2));
            var candidateLuxuries = randomLuxuries.Shuffled(shuffleRng);
            if (!tileMap.MapParameters.GetStrategicBalance())
                candidateLuxuries.AddRange(specialLuxuries.Shuffled(shuffleRng)
                    .Select(it => it.Name)); // Include marble!
            candidateLuxuries.AddRange(cityStateLuxuries.Shuffled(shuffleRng));
            candidateLuxuries.AddRange(regions.Select(it => it.Luxury).OfType<string>().Shuffled(shuffleRng));
            foreach (var luxury in candidateLuxuries)
            {
                if (MapRegionResources.TryAddingResourceToTiles(
                        tileData,
                        ruleset.TileResources[luxury],
                        1,
                        tilesToCheck) > 0)
                {
                    if (placedSpecials.ContainsKey(luxury)) // Keep track of marble-type specials as they may be placed now.
                        placedSpecials[luxury] = placedSpecials[luxury] + 1;
                    break;
                }
            }
        }
    }

    private static void AddRandomLuxuries(
        List<string> randomLuxuries,
        TileDataMap tileData,
        TileMap tileMap,
        List<Region> regions,
        Ruleset ruleset)
    {
        if (randomLuxuries.Count == 0) return;
        var rng = new GameContext(gameInfo: tileMap.GameInfo).StateBasedRandom("LuxuryResourcePlacementLogic.addRandomLuxuries");
        var shuffleRng = ResourcePlacementRandom.ForShuffling(tileMap, "LuxuryResourcePlacementLogic.addRandomLuxuries");
        var targetRandomLuxuries = KotlinMath.FloatToInt(KotlinMath.Pow(tileData.Size, 0.45f)); // Approximately
        targetRandomLuxuries *= tileMap.MapParameters.GetMapResources().GetRandomLuxuriesPercent();
        targetRandomLuxuries /= 100;
        targetRandomLuxuries += rng.NextInt(regions.Count); // Add random number based on number of civs
        var minimumRandomLuxuries = KotlinMath.FloatToInt(KotlinMath.Pow(tileData.Size, 0.2f)); // Approximately
        var worldTiles = tileMap.Values.ShuffledSequence(shuffleRng);
        var index = 0;
        foreach (var luxury in randomLuxuries.Shuffled(shuffleRng))
        {
            int targetForThisLuxury;
            if (randomLuxuries.Count > 8)
                targetForThisLuxury = targetRandomLuxuries / 10;
            else
            {
                var minimum = Math.Max(3, minimumRandomLuxuries - index);
                targetForThisLuxury = Math.Max(
                    minimum,
                    KotlinMath.FloatToInt((targetRandomLuxuries * MapRegions.RandomLuxuryRatios[randomLuxuries.Count][index]) + 0.5f));
            }
            MapRegionResources.TryAddingResourceToTiles(
                tileData,
                ruleset.TileResources[luxury],
                targetForThisLuxury,
                worldTiles,
                0.25f,
                true,
                4,
                2);
            index++;
        }
    }

    private static void AddRegionalLuxuries(
        TileDataMap tileData,
        List<Region> regions,
        TileMap tileMap,
        Ruleset ruleset)
    {
        var rng = new GameContext(gameInfo: tileMap.GameInfo).StateBasedRandom("LuxuryResourcePlacementLogic.addRegionalLuxuries");
        var idealCivsForMapSize = Math.Max(2, tileData.Size / 500);
        var civCount = regions.Count;
        var regionTargetNumber =
            (tileData.Size / 600) - KotlinMath.FloatToInt(0.3f * Math.Abs(regions.Count - idealCivsForMapSize));
        regionTargetNumber += tileMap.MapParameters.GetMapResources().GetRegionalLuxuriesDelta();
        regionTargetNumber = Math.Max(1, regionTargetNumber);
        // We place atleast 2 regionals close to the civ, and there's no reason to have more copies than one for each other civ
        regionTargetNumber = Math.Min(civCount - 3, regionTargetNumber);
        foreach (var region in regions)
        {
            if (region.Luxury == null || !ruleset.TileResources.TryGetValue(region.Luxury, out var resource)) continue;

            bool IsShoreOfContinent(Tile tile, int continent) =>
                tile.IsWater && tile.Neighbors.Any(it => it.GetContinent() == continent);

            IEnumerable<Tile> candidates = MapRegionFunctions.IsWaterOnlyResource(resource, ruleset)
                ? tileMap.GetTilesInRectangle(region.Rect)
                    .Where(it => IsShoreOfContinent(it, region.ContinentID))
                : region.Tiles;
            var sortedCandidates = candidates
                .Select(it => (Tile: it, Key: HexMath.GetDistance(it.Position, region.StartPosition.NotNull()) + (rng.NextFloat() * 20f)))
                .ToList()
                .OrderBy(it => it.Key)
                .Select(it => it.Tile)
                .ToList();
            MapRegionResources.TryAddingResourceToTiles(
                tileData,
                resource,
                regionTargetNumber,
                sortedCandidates,
                0.4f,
                true,
                4,
                2);
        }
    }

    private static void PlaceLuxuriesAtMinorCivStartLocations(
        TileMap tileMap,
        Ruleset ruleset,
        List<Region> regions,
        List<string> randomLuxuries,
        List<string> cityStateLuxuries,
        TileDataMap tileData)
    {
        var rng = new GameContext(gameInfo: tileMap.GameInfo).StateBasedRandom("LuxuryResourcePlacementLogic.placeLuxuriesAtMinorCivStartLocations");
        var shuffleRng = ResourcePlacementRandom.ForShuffling(tileMap, "LuxuryResourcePlacementLogic.placeLuxuriesAtMinorCivStartLocations");
        foreach (var startLocation in tileMap.StartingLocationsByNation
            .Where(it => ruleset.Nations[it.Key].IsCityState).Select(it => it.Value.First()).ToList())
        {
            var region = regions.FirstOrDefault(it => it.Tiles.Contains(startLocation));
            var tilesToCheck = startLocation.GetTilesInDistanceRange(new IntRange(1, 2));
            // 75% probability that we first attempt to place a "city state" luxury, then a random or regional one
            // 25% probability of going the other way around
            var globalLuxuries = region?.Luxury != null
                ? randomLuxuries.Append(region.Luxury).ToList()
                : randomLuxuries;
            var candidateLuxuries = rng.NextInt(100) >= 25
                ? cityStateLuxuries.Shuffled(shuffleRng).Concat(globalLuxuries.Shuffled(shuffleRng)).ToList()
                : globalLuxuries.Shuffled(shuffleRng).Concat(cityStateLuxuries.Shuffled(shuffleRng)).ToList();
            // Now try adding one until we are successful
            foreach (var luxury in candidateLuxuries)
            {
                if (MapRegionResources.TryAddingResourceToTiles(
                        tileData,
                        ruleset.TileResources[luxury],
                        1,
                        tilesToCheck) > 0) break;
            }
        }
    }

    private static void PlaceLuxuriesAtMajorCivStartLocations(
        List<Region> regions,
        TileMap tileMap,
        Ruleset ruleset,
        TileDataMap tileData,
        List<string> randomLuxuries)
    {
        var shuffleRng = ResourcePlacementRandom.ForShuffling(tileMap, "LuxuryResourcePlacementLogic.placeLuxuriesAtMajorCivStartLocations");
        int totalFertility = 0, totalTiles = 0;
        unchecked
        {
            foreach (var region in regions)
            {
                totalFertility += region.TotalFertility;
                totalTiles += region.Tiles.Count;
            }
        }
        var averageFertilityDensity = totalFertility / (float)totalTiles;
        foreach (var region in regions)
        {
            var targetLuxuries = 2;
            if (tileMap.MapParameters.GetLegendaryStart())
                targetLuxuries++;
            if (region.TotalFertility / (float)region.Tiles.Count < averageFertilityDensity)
                targetLuxuries++;

            if (region.Luxury == null || !ruleset.TileResources.TryGetValue(region.Luxury, out var luxuryToPlace)) continue;
            // First check 2 inner rings
            // Kotlin Sequences: every enumeration shuffles and sorts anew
            var startTile = tileMap[region.StartPosition.NotNull()];
            var firstPass = ResourcePlacementRandom.Deferred(() => startTile.GetTilesInDistanceRange(new IntRange(1, 2))
                .Shuffled(shuffleRng).OrderBy(it => it.GetTileFertility(false))); // Check bad tiles first
            targetLuxuries -= MapRegionResources.TryAddingResourceToTiles(
                tileData,
                luxuryToPlace,
                targetLuxuries,
                firstPass,
                0.5f); // Skip every 2nd tile on first pass

            if (targetLuxuries > 0)
            {
                var secondPass = firstPass.Concat(ResourcePlacementRandom.Deferred(() => startTile.GetTilesAtDistance(3)
                    .Shuffled(shuffleRng).OrderBy(it => it.GetTileFertility(false)))); // Check bad tiles first
                targetLuxuries -= MapRegionResources.TryAddingResourceToTiles(
                    tileData,
                    luxuryToPlace,
                    targetLuxuries,
                    secondPass);
            }
            if (targetLuxuries > 0)
            {
                // Try adding in 1 luxury from the random rotation as compensation
                foreach (var luxury in randomLuxuries)
                {
                    if (MapRegionResources.TryAddingResourceToTiles(
                            tileData, ruleset.TileResources[luxury], 1, firstPass) > 0)
                        break;
                }
            }
        }
    }

    private static float ParseFloat(string text) => float.Parse(text, CultureInfo.InvariantCulture);
}
