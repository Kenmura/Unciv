using System.Globalization;
using Unciv.Logic.Map.MapGenerator.Mapregions;
using Unciv.Logic.Map.Tiles;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using Unciv.Utils;
using ImpactType = Unciv.Logic.Map.MapGenerator.Mapregions.MapRegions.ImpactType;

namespace Unciv.Logic.Map.MapGenerator.ResourcePlacement;

// core/src/com/unciv/logic/map/mapgenerator/resourceplacement/StrategicBonusResourcePlacementLogic.kt
// Deviations: see MapRegionResources.cs for the unseeded shuffled() calls. Kotlin `HashMap<Unique, MutableList<Tile>>` (ruleLists) is a Dictionary:
// its iteration order only decides which list a tile is added to first, never the outcome. `totalPlaced += map` (MutableMap.plusAssign) is putAll, i.e. it
// REPLACES the entries, it does not add; kept.
public static class StrategicBonusResourcePlacementLogic
{
    /// <summary>There are a couple competing/complementary distribution systems at work here. First, major
    /// deposits are placed according to a frequency defined in the terrains themselves, for each
    /// tile that is eligible to get a major deposit, there is a weighted random choice between
    /// resource types.
    /// Minor deposits are placed by randomly picking a number of land tiles from anywhere on the
    /// map (so not stratified by terrain type) and assigning a weighted randomly picked resource.
    /// Bonuses are placed according to a frequency for a rule like "every 8 jungle hills", here
    /// implemented as a conditional.
    ///
    /// We need to build lists of all tiles following a given rule to place these, which is BY FAR
    /// the most expensive calculation in this entire class. To save some time we anonymize the
    /// uniques so we only have to make one list for each set of conditionals, so eg Wheat and
    /// Horses can share a list since they are both interested in Featureless Plains.
    /// We also save a list of all land tiles for minor deposit generation.</summary>
    internal static void PlaceStrategicAndBonuses(TileMap tileMap, List<Region> regions, TileDataMap tileData)
    {
        var ruleset = tileMap.Ruleset!;
        var strategicResources = ruleset.TileResources.Values.Where(it => it.ResourceType == ResourceType.Strategic).ToList();
        // As usual, if there are any relevant json definitions, assume they are complete
        var fallbackStrategic = !ruleset.TileResources.Values.Any(it =>
            (it.ResourceType == ResourceType.Strategic &&
                ((IHasUniques)it).HasUnique(UniqueType.ResourceWeighting)) ||
            ((IHasUniques)it).HasUnique(UniqueType.MinorDepositWeighting));

        // Determines number tiles per resource
        var bonusMultiplier = tileMap.MapParameters.GetMapResources().GetBonusFrequencyMultiplier();
        var landList = new List<Tile>(); // For minor deposits

        // Maps resource uniques for determining frequency/weighting/size to relevant tiles
        var ruleLists = BuildRuleLists(ruleset, tileMap, regions, fallbackStrategic, strategicResources); // For rule-based generation

        // Now go through the entire map to build lists
        var shuffleRng = ResourcePlacementRandom.ForShuffling(tileMap, "StrategicBonusResourcePlacementLogic.placeStrategicAndBonuses");
        foreach (var tile in tileMap.Values.ShuffledSequence(shuffleRng))
        {
            var terrainCondition = new GameContext(attackedTile: tile, region: regions.FirstOrDefault(it => it.Tiles.Contains(tile)));
            if (((IHasUniques)tile.GetBaseTerrain()).HasUnique(UniqueType.BlocksResources, terrainCondition)) continue; // Don't count snow hills
            if (tile.IsLand) landList.Add(tile);
            foreach (var (rule, list) in ruleLists)
            {
                if (rule.ConditionalsApply(terrainCondition)) list.Add(tile);
            }
        }

        // Keep track of total placed strategic resources in case we need to top them up later
        var totalPlaced = new Dictionary<TileResource, int>();
        foreach (var resource in strategicResources) totalPlaced[resource] = 0;

        PlaceMajorDepositsOnLand(ruleset, ruleLists, totalPlaced, tileData, fallbackStrategic);
        PlaceSmallDepositsOfModernStrategicResourcesOnCityStates(ruleset, strategicResources, tileMap, totalPlaced, tileData);
        PlaceMinorDepositsOnLand(bonusMultiplier, landList, tileData, strategicResources, fallbackStrategic, totalPlaced);
        PlaceMajorDepositsOnWater(ruleset, ruleLists, totalPlaced, tileData, fallbackStrategic);
        EnsureMinimumResourcesPerCiv(strategicResources, regions, totalPlaced, ruleset, landList, tileMap, tileData);
        PlaceBonusResources(ruleset, ruleLists, tileData, bonusMultiplier, tileMap);
        PlaceBonusInThirdRingOfStart(regions, ruleset, tileMap, tileData);
    }

    private static void PlaceBonusInThirdRingOfStart(
        List<Region> regions,
        Ruleset ruleset,
        TileMap tileMap,
        TileDataMap tileData)
    {
        var rng = new GameContext(gameInfo: tileMap.GameInfo).StateBasedRandom("StrategicBonusResourcePlacementLogic.placeBonusInThirdRingOfStart");
        var shuffleRng = ResourcePlacementRandom.ForShuffling(tileMap, "StrategicBonusResourcePlacementLogic.placeBonusInThirdRingOfStart");
        foreach (var region in regions)
        {
            string terrain;
            if (region.Type == "Hybrid")
            {
                // Kotlin filterNot { key == "Coastal" }.maxByOrNull { value }: first maximum in map iteration order
                var bestCount = 0;
                string? best = null;
                foreach (var (key, value) in region.TerrainCounts)
                {
                    if (key == "Coastal") continue;
                    if (best == null || bestCount < value)
                    {
                        best = key;
                        bestCount = value;
                    }
                }
                terrain = best!;
            }
            else terrain = region.Type;
            var resourceUnique = ((IHasUniques)ruleset.Terrains[terrain]).GetMatchingUniques(UniqueType.RegionExtraResource).FirstOrDefault();
            // If this region has an explicit "this is the bonus" unique go with that, else random appropriate
            TileResource resource;
            if (resourceUnique != null) resource = ruleset.TileResources[resourceUnique.Params[0]];
            else
            {
                var possibleResources =
                    ruleset.TileResources.Values.Where(it => it.ResourceType == ResourceType.Bonus && it.TerrainsCanBeFoundOn.Contains(terrain)).ToList();
                if (possibleResources.Count == 0) continue;
                resource = possibleResources.Random(rng);
            }
            var candidateTiles = tileMap[region.StartPosition!.Value].GetTilesAtDistance(3).Shuffled(shuffleRng);
            var amount = resourceUnique != null ? 2 : 1; // Place an extra if the region type requests it
            if (MapRegionResources.TryAddingResourceToTiles(tileData, resource, amount, candidateTiles) == 0)
            {
                // We couldn't place any, try adding a fish instead
                var fishyBonus = ruleset.TileResources.Values.Where(it =>
                    it.ResourceType == ResourceType.Bonus &&
                    it.TerrainsCanBeFoundOn.Any(terrainName => ruleset.Terrains[terrainName].Type == TerrainType.Water))
                    .ToList()
                    .RandomOrNull(rng);
                if (fishyBonus != null)
                    MapRegionResources.TryAddingResourceToTiles(tileData, fishyBonus, 1, candidateTiles);
            }
        }
    }

    private static void PlaceBonusResources(
        Ruleset ruleset,
        Dictionary<Unique, List<Tile>> ruleLists,
        TileDataMap tileData,
        float bonusMultiplier,
        TileMap tileMap)
    {
        // Figure out if bonus generation rates are defined in json. Assume that if there are any, the definitions are complete.
        var useFallbackBonuses = !ruleset.TileResources.Values.Any(it => ((IHasUniques)it).HasUnique(UniqueType.ResourceFrequency));
        var shuffleRng = ResourcePlacementRandom.ForShuffling(tileMap, "StrategicBonusResourcePlacementLogic.placeBonusResources");

        // Water-based bonuses go last and have extra impact, because coasts are very common and we don't want too much clustering
        var sortedResourceList = ruleset.TileResources.Values.OrderBy(it => MapRegionFunctions.IsWaterOnlyResource(it, ruleset)).ToList();

        foreach (var resource in sortedResourceList)
        {
            var extraImpact = MapRegionFunctions.IsWaterOnlyResource(resource, ruleset) ? 1 : 0;
            foreach (var rule in ((IHasUniques)resource).UniqueObjects.Where(it => it.Type == UniqueType.ResourceFrequency).ToList())
            {
                // Figure out which list applies, if any
                var simpleRule = MapRegionFunctions.AnonymizeUnique(rule);
                var list = FindList(ruleLists, simpleRule.Text);
                // If there is no matching list, it is because the rule was determined to be impossible and so can be safely skipped
                if (list == null) continue;
                // Place the resources
                MapRegionResources.PlaceResourcesInTiles(
                    tileData,
                    KotlinMath.FloatToInt(ParseFloat(rule.Params[0]) * bonusMultiplier),
                    list,
                    [resource],
                    0 + extraImpact,
                    2 + extraImpact,
                    false);
            }

            if (useFallbackBonuses && resource.ResourceType == ResourceType.Bonus)
            {
                // Since we haven't been able to generate any rule-based lists, just generate new ones on the fly
                // Increase impact to avoid clustering since there is no terrain type stratification.
                var fallbackList =
                    tileMap.Values.Where(it => resource.TerrainsCanBeFoundOn.Contains(it.LastTerrain.Name)).Shuffled(shuffleRng);
                MapRegionResources.PlaceResourcesInTiles(
                    tileData,
                    KotlinMath.FloatToInt(20 * bonusMultiplier),
                    fallbackList,
                    [resource],
                    2 + extraImpact,
                    2 + extraImpact,
                    false);
            }
        }
    }

    /// <summary>place up to 2 extra deposits of each resource type if there is &lt; 1 per civ</summary>
    private static void EnsureMinimumResourcesPerCiv(
        List<TileResource> strategicResources,
        List<Region> regions,
        Dictionary<TileResource, int> totalPlaced,
        Ruleset ruleset,
        List<Tile> landList,
        TileMap tileMap,
        TileDataMap tileData)
    {
        var shuffleRng = ResourcePlacementRandom.ForShuffling(tileMap, "StrategicBonusResourcePlacementLogic.ensureMinimumResourcesPerCiv");
        foreach (var resource in strategicResources)
        {
            var extraNeeded = Math.Min(2, regions.Count - totalPlaced[resource]);
            if (extraNeeded > 0)
            {
                var tilesToAddTo = !MapRegionFunctions.IsWaterOnlyResource(resource, ruleset)
                    ? landList
                    : tileMap.Values.Where(it => it.IsWater).ShuffledSequence(shuffleRng);

                MapRegionResources.TryAddingResourceToTiles(
                    tileData,
                    resource,
                    extraNeeded,
                    tilesToAddTo,
                    respectImpacts: true);
            }
        }
    }

    // Extra impact because we don't want them too clustered and there is usually lots to go around
    private static void PlaceMajorDepositsOnWater(
        Ruleset ruleset,
        Dictionary<Unique, List<Tile>> ruleLists,
        Dictionary<TileResource, int> totalPlaced,
        TileDataMap tileData,
        bool fallbackStrategic)
    {
        foreach (var terrain in ruleset.Terrains.Values.Where(it => it.Type == TerrainType.Water).ToList())
        {
            // Figure out if we generated a list for this terrain
            var list = FindList(ruleLists, MapRegionFunctions.GetTerrainRule(terrain, ruleset).Text);
            if (list == null) continue; // If not the terrain can be safely skipped
            PutAll(totalPlaced, MapRegionResources.PlaceMajorDeposits(
                tileData,
                ruleset,
                list,
                terrain,
                fallbackStrategic,
                4,
                3));
        }
    }

    private static void PlaceMinorDepositsOnLand(
        float bonusMultiplier,
        List<Tile> landList,
        TileDataMap tileData,
        List<TileResource> strategicResources,
        bool fallbackStrategic,
        Dictionary<TileResource, int> totalPlaced)
    {
        var rng = new GameContext(gameInfo: landList[0].TileMap.GameInfo).StateBasedRandom("StrategicBonusResourcePlacementLogic.placeMinorDepositsOnLand");
        var frequency = KotlinMath.FloatToInt(MapRegions.BaseMinorDepositFrequency * bonusMultiplier);
        var minorDepositsToAdd =
            (landList.Count / frequency) + 1; // I sometimes have division by zero errors on this line
        var minorDepositsAdded = 0;
        foreach (var tile in landList)
        {
            if (tile.Resource != null || tileData[tile]!.Impacts.ContainsKey(ImpactType.Strategic))
                continue;
            var conditionalTerrain = new GameContext(tile: tile);
            if (((IHasUniques)tile.GetBaseTerrain()).HasUnique(UniqueType.BlocksResources, conditionalTerrain))
                continue;
            var weightings = strategicResources.Select(it =>
            {
                if (fallbackStrategic)
                    return it.GeneratesNaturallyOn(tile) ? 1f : 0f;
                var uniques = ((IHasUniques)it).GetMatchingUniques(UniqueType.MinorDepositWeighting, conditionalTerrain).ToList();
                var sum = 0;
                foreach (var unique in uniques) sum = unchecked(sum + ParseInt(unique.Params[0]));
                return (float)sum;
            }).ToList();
            var weightSum = 0f;
            foreach (var weight in weightings) weightSum += weight;
            if (weightSum <= 0) continue;

            var resourceToPlace = strategicResources.RandomWeighted(weightings, rng);
            tile.SetTileResource(resourceToPlace, majorDeposit: false);
            tileData.PlaceImpact(ImpactType.Strategic, tile, rng.NextInt(2) + rng.NextInt(2));
            totalPlaced[resourceToPlace] = totalPlaced[resourceToPlace] + 1;
            minorDepositsAdded++;
            if (minorDepositsAdded >= minorDepositsToAdd)
                break;
        }
    }

    private static void PlaceSmallDepositsOfModernStrategicResourcesOnCityStates(
        Ruleset ruleset,
        List<TileResource> strategicResources,
        TileMap tileMap,
        Dictionary<TileResource, int> totalPlaced,
        TileDataMap tileData)
    {
        var rng = new GameContext(gameInfo: tileMap.GameInfo).StateBasedRandom("StrategicBonusResourcePlacementLogic.placeSmallDepositsOfModernStrategicResourcesOnCityStates");
        var lastEra = ruleset.Eras.Values.Max(it => it.EraNumber);
        var modernOptions = strategicResources.Where(it =>
            it.RevealedBy != null &&
            ruleset.Eras[ruleset.Technologies[it.RevealedBy].Era()].EraNumber >= lastEra / 2).ToList();

        if (modernOptions.Count > 0)
        {
            foreach (var cityStateLocation in tileMap.StartingLocationsByNation
                .Where(it => ruleset.Nations[it.Key].IsCityState).Select(it => it.Value.First()).ToList())
            {
                var resourceToPlace = modernOptions.Random(rng);
                totalPlaced[resourceToPlace] =
                    totalPlaced[resourceToPlace] + MapRegionResources.TryAddingResourceToTiles(
                        tileData,
                        resourceToPlace,
                        1,
                        cityStateLocation.GetTilesInDistanceRange(new IntRange(1, 3)));
            }
        }
    }

    private static void PlaceMajorDepositsOnLand(
        Ruleset ruleset,
        Dictionary<Unique, List<Tile>> ruleLists,
        Dictionary<TileResource, int> totalPlaced,
        TileDataMap tileData,
        bool fallbackStrategic)
    {
        foreach (var terrain in ruleset.Terrains.Values.Where(it => it.Type != TerrainType.Water).ToList())
        {
            // Figure out if we generated a list for this terrain
            var terrainRule = MapRegionFunctions.GetTerrainRule(terrain, ruleset);
            var list = FindList(ruleLists, terrainRule.Text);
            if (list == null) continue; // If not the terrain can be safely skipped
            PutAll(totalPlaced, MapRegionResources.PlaceMajorDeposits(
                tileData,
                ruleset,
                list,
                terrain,
                fallbackStrategic,
                2,
                2));
        }
    }

    private static Dictionary<Unique, List<Tile>> BuildRuleLists(
        Ruleset ruleset,
        TileMap tileMap,
        List<Region> regions,
        bool fallbackStrategic,
        List<TileResource> strategicResources)
    {
        var ruleLists = new Dictionary<Unique, List<Tile>>(); // For rule-based generation

        // Figure out which rules (sets of conditionals) need lists built
        foreach (var resource in ruleset.TileResources.Values.Where(it =>
            it.ResourceType == ResourceType.Strategic ||
            it.ResourceType == ResourceType.Bonus))
        {
            foreach (var rule in ((IHasUniques)resource).UniqueObjects.Where(unique =>
                unique.Type == UniqueType.ResourceFrequency ||
                unique.Type == UniqueType.ResourceWeighting ||
                unique.Type == UniqueType.MinorDepositWeighting))
            {
                // Weed out some clearly impossible rules straight away to save time later
                if (rule.Modifiers.Any(conditional =>
                    (conditional.Type == UniqueType.ConditionalOnWaterMaps && !tileMap.UsingArchipelagoRegions()) ||
                    (conditional.Type == UniqueType.ConditionalInRegionOfType && !regions.Any(region => region.Type == conditional.Params[0])) ||
                    (conditional.Type == UniqueType.ConditionalInRegionExceptOfType && regions.All(region => region.Type == conditional.Params[0]))))
                    continue;
                var simpleRule = MapRegionFunctions.AnonymizeUnique(rule);
                if (FindList(ruleLists, simpleRule.Text) == null) // Need to do text comparison since the uniques will not be equal otherwise
                    ruleLists[simpleRule] = [];
            }
        }

        // Make up some rules for placing strategics in a fallback situation
        if (fallbackStrategic)
        {
            var interestingTerrains = new LinkedHashSet<Terrain>(
                strategicResources.SelectMany(it => it.TerrainsCanBeFoundOn).Select(it => ruleset.Terrains[it]));
            foreach (var terrain in interestingTerrains)
            {
                var fallbackRule = terrain.Type == TerrainType.TerrainFeature
                    ? new Unique($"RULE <in [{terrain.Name}] tiles>")
                    : new Unique($"RULE <in [Featureless] [{terrain.Name}] tiles>");
                if (FindList(ruleLists, fallbackRule.Text) == null) // Need to do text comparison since the uniques will not be equal otherwise
                    ruleLists[fallbackRule] = [];
            }
        }
        return ruleLists;
    }

    /// <summary>Kotlin <c>ruleLists.filterKeys { it.text == text }.values.firstOrNull()</c></summary>
    private static List<Tile>? FindList(Dictionary<Unique, List<Tile>> ruleLists, string text)
    {
        foreach (var (rule, list) in ruleLists)
            if (rule.Text == text) return list;
        return null;
    }

    /// <summary>Kotlin <c>MutableMap.plusAssign(Map)</c>: putAll</summary>
    private static void PutAll(Dictionary<TileResource, int> target, Dictionary<TileResource, int> source)
    {
        foreach (var (key, value) in source) target[key] = value;
    }

    private static int ParseInt(string text) => int.Parse(text, CultureInfo.InvariantCulture);
    private static float ParseFloat(string text) => float.Parse(text, CultureInfo.InvariantCulture);
}
