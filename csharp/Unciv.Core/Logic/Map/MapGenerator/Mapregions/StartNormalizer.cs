using Unciv.Logic.Map.Tiles;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.StatTypes;
using Unciv.Utils;

namespace Unciv.Logic.Map.MapGenerator.Mapregions;

// core/src/com/unciv/logic/map/mapgenerator/mapregions/StartNormalizer.kt
// Ensures that starting positions of civs have enough yield that they aren't at a disadvantage.
// Deviations: Kotlin's unseeded shuffled() / random() (Random.Default) have no counterpart in the engine (no unseeded randomness), so they
// draw from a state based GameRandom per call site; MapRegionResources (T077) is reached through MapRegionResourcesSeams;
// TileResource.generatesNaturallyOn (not yet ported on TileResource) is GeneratesNaturallyOn below.
public static class StartNormalizer
{
    /// <summary>Attempts to improve the start on <paramref name="startTile"/> as needed to make it decent.
    /// Relies on startPosition having been set previously.
    /// Assumes unchanged baseline values ie citizens eat 2 food each, similar production costs.
    /// If <paramref name="isMinorCiv"/> is true, different weightings will be used.</summary>
    internal static void NormalizeStart(Tile startTile, TileMap tileMap, TileDataMap tileData, Ruleset ruleset, bool isMinorCiv)
    {
        // Remove ice-like features adjacent to start
        foreach (var tile in startTile.Neighbors)
        {
            var lastTerrain = tile.TerrainFeatureObjects.LastOrDefault(it => it.Impassable);
            if (lastTerrain != null)
                tile.RemoveTerrainFeature(lastTerrain.Name);
        }

        if (!isMinorCiv && tileMap.MapParameters.GetStrategicBalance())
            PlaceStrategicBalanceResources(startTile, ruleset, tileData);

        NormalizeProduction(startTile, isMinorCiv, ruleset, tileData);

        var foodBonusesNeeded = CalculateFoodBonusesNeeded(startTile, isMinorCiv, ruleset, tileMap);
        PlaceFoodBonuses(isMinorCiv, startTile, ruleset, foodBonusesNeeded);

        // Minor civs are done, go on with grassiness checks for major civs
        if (isMinorCiv) return;

        AddProductionBonuses(startTile);
    }

    private static GameRandom ShuffleRandom(Tile startTile, string caller) =>
        new GameContext(tile: startTile).StateBasedRandom("StartNormalizer." + caller + ".unseeded");

    private static void NormalizeProduction(Tile startTile, bool isMinorCiv, Ruleset ruleset, TileDataMap tileData)
    {
        var rng = new GameContext(civInfo: startTile.GetOwner(), tile: startTile).StateBasedRandom("StartNormalizer.normalizeProduction");
        // evaluate production potential
        var innerProduction = startTile.Neighbors.Sum(it => KotlinMath.FloatToInt(GetPotentialYield(it, Stat.Production)));
        var outerProduction = startTile.GetTilesAtDistance(2).Sum(it => KotlinMath.FloatToInt(GetPotentialYield(it, Stat.Production)));
        // for very early production we ideally want tiles that also give food
        var earlyProduction = startTile.GetTilesInDistanceRange(new IntRange(1, 2)).Sum(it =>
            GetPotentialYield(it, Stat.Food, unimproved: true) > 0f
                ? KotlinMath.FloatToInt(GetPotentialYield(it, Stat.Production, unimproved: true))
                : 0);

        // If terrible, try adding a hill to a dry flat tile
        if (innerProduction == 0 || (innerProduction < 2 && outerProduction < 8) || (isMinorCiv && innerProduction < 4))
        {
            var hillSpot = startTile.Neighbors
                .Where(it => it.IsLand && it.TerrainFeatures.Count == 0 && !it.IsAdjacentTo(Constants.FreshWater) && !it.IsImpassible())
                .ToList().RandomOrNull(rng);
            var hillEquivalent = ruleset.Terrains.Values
                .FirstOrDefault(it => it.Type == TerrainType.TerrainFeature && it.Production >= 2
                    && !((IHasUniques)it).HasUnique(UniqueType.RareFeature))?.Name;
            if (hillSpot != null && hillEquivalent != null)
                hillSpot.AddTerrainFeature(hillEquivalent);
        }

        // If bad early production, add a small strategic resource to SECOND ring (not for minors)
        if (!isMinorCiv && innerProduction < 3 && earlyProduction < 6)
        {
            var shuffleRng = ShuffleRandom(startTile, "normalizeProduction");
            var lastEraNumber = ruleset.Eras.Values.Max(it => it.EraNumber);
            var earlyEras = ruleset.Eras.Where(it => it.Value.EraNumber <= lastEraNumber / 3).Select(it => it.Key).ToHashSet();
            var validResources = ruleset.TileResources.Values.Where(it =>
                it.ResourceType == ResourceType.Strategic
                && (it.RevealedBy == null || earlyEras.Contains(ruleset.Technologies[it.RevealedBy].Era()))).Shuffled(shuffleRng);
            var candidateTiles = startTile.GetTilesAtDistance(2).Shuffled(shuffleRng);
            foreach (var resource in validResources)
            {
                var resourcesAdded = MapRegionResourcesSeams.TryAddingResourceToTiles(tileData, resource, 1, candidateTiles, majorDeposit: false);
                if (resourcesAdded > 0) break;
            }
        }
    }

    private static void PlaceStrategicBalanceResources(Tile startTile, Ruleset ruleset, TileDataMap tileData)
    {
        var shuffleRng = ShuffleRandom(startTile, "placeStrategicBalanceResources");
        var candidateTiles = startTile.GetTilesInDistanceRange(new IntRange(1, 2)).Shuffled(shuffleRng)
            .Concat(startTile.GetTilesAtDistance(3).Shuffled(shuffleRng)).ToList();
        foreach (var resource in ruleset.TileResources.Values.Where(it => ((IHasUniques)it).HasUnique(UniqueType.StrategicBalanceResource)).ToList())
        {
            if (MapRegionResourcesSeams.TryAddingResourceToTiles(tileData, resource, 1, candidateTiles, majorDeposit: true) == 0)
            {
                // Fallback mode - force placement, even on an otherwise inappropriate terrain. Do still respect water and impassible tiles!
                var resourceTiles = MapRegionFunctions.IsWaterOnlyResource(resource, ruleset)
                    ? candidateTiles.Where(it => it.IsWater && !it.IsImpassible()).ToList()
                    : candidateTiles.Where(it => it.IsLand && !it.IsImpassible()).ToList();
                MapRegionResourcesSeams.PlaceResourcesInTiles(tileData, 999, resourceTiles, [resource], majorDeposit: true);
            }
        }
    }

    /// <summary>Check for very food-heavy starts that might still need some stone to help with production</summary>
    private static void AddProductionBonuses(Tile startTile)
    {
        var ruleset = startTile.TileMap.Ruleset!;
        var rng = new GameContext(gameInfo: startTile.TileMap.GameInfo).StateBasedRandom("StartNormalizer.addProductionBonuses");
        var grassTypePlots = startTile.GetTilesInDistanceRange(new IntRange(1, 2)).Where(it =>
            it.IsLand
            && GetPotentialYield(it, Stat.Food, unimproved: true) >= 2f // Food neutral natively
            && GetPotentialYield(it, Stat.Production) == 0f // Production can't even be improved
        ).ToList();
        var plainsTypePlots = startTile.GetTilesInDistanceRange(new IntRange(1, 2)).Where(it =>
            it.IsLand
            && GetPotentialYield(it, Stat.Food) >= 2f // Something that can be improved to food neutral
            && GetPotentialYield(it, Stat.Production, unimproved: true) >= 1f // Some production natively
        ).ToList();
        var productionBonusesNeeded =
            grassTypePlots.Count >= 9 && plainsTypePlots.Count == 0 ? 2
            : grassTypePlots.Count >= 6 && plainsTypePlots.Count <= 4 ? 1
            : 0;
        var productionBonuses = ruleset.TileResources.Values.Where(it => it.ResourceType == ResourceType.Bonus && it.Production > 0).ToList();

        if (productionBonuses.Count != 0)
        {
            while (productionBonusesNeeded > 0 && grassTypePlots.Count != 0)
            {
                var plot = grassTypePlots.Random(rng);
                grassTypePlots.Remove(plot);

                if (plot.TileResource != null) continue;

                var bonusToPlace = productionBonuses.Where(it => GeneratesNaturallyOn(it, plot)).ToList().RandomOrNull(rng);
                if (bonusToPlace != null)
                {
                    plot.TileResource = bonusToPlace;
                    productionBonusesNeeded--;
                }
            }
        }
    }

    private static int CalculateFoodBonusesNeeded(Tile startTile, bool minorCiv, Ruleset ruleset, TileMap tileMap)
    {
        // evaluate food situation
        // Food^2/4 because excess food is really good and lets us work other tiles or run specialists!
        // 2F is worth 1, 3F is worth 2, 4F is worth 4, 5F is worth 6 and so on
        var innerFood = startTile.Neighbors.Sum(it => FoodValue(it));
        var outerFood = startTile.GetTilesAtDistance(2).Sum(it => FoodValue(it));
        var totalFood = innerFood + outerFood;
        // we want at least some two-food tiles to keep growing
        var innerNativeTwoFood = startTile.Neighbors.Count(it => GetPotentialYield(it, Stat.Food, unimproved: true) >= 2f);
        var outerNativeTwoFood = startTile.GetTilesAtDistance(2).Count(it => GetPotentialYield(it, Stat.Food, unimproved: true) >= 2f);
        var totalNativeTwoFood = innerNativeTwoFood + outerNativeTwoFood;

        // Determine number of needed bonuses. Different weightings for minor and major civs.
        int bonusesNeeded;
        if (minorCiv)
        {
            // From 2 to 0
            if (totalFood < 12 || innerFood < 4) bonusesNeeded = 2;
            else if (totalFood < 16 || innerFood < 9) bonusesNeeded = 1;
            else bonusesNeeded = 0;
        }
        else
        {
            // From 5 to 0
            if (innerFood == 0 && totalFood < 4) bonusesNeeded = 5;
            else if (totalFood < 6) bonusesNeeded = 4;
            else if (totalFood < 8 || (totalFood < 12 && innerFood < 5)) bonusesNeeded = 3;
            else if ((totalFood < 17 && innerFood < 9) || totalNativeTwoFood < 2) bonusesNeeded = 2;
            else if ((totalFood < 24 && innerFood < 11) || totalNativeTwoFood == 2 || innerNativeTwoFood == 0 || totalFood < 20) bonusesNeeded = 1;
            else bonusesNeeded = 0;
        }
        if (tileMap.MapParameters.GetLegendaryStart())
            bonusesNeeded += 2;

        // Attempt to place one grassland at a plains-only spot (nor for minors)
        if (!minorCiv && bonusesNeeded < 3 && totalNativeTwoFood == 0)
        {
            var shuffleRng = ShuffleRandom(startTile, "calculateFoodBonusesNeeded");
            var twoFoodTerrain = ruleset.Terrains.Values.FirstOrDefault(it => it.Type == TerrainType.Land && it.Food >= 2)?.Name;
            var candidateInnerSpots = startTile.Neighbors
                .Where(it => it.IsLand && !it.IsImpassible() && it.TerrainFeatures.Count == 0 && it.Resource == null);
            var candidateOuterSpots = startTile.GetTilesAtDistance(2)
                .Where(it => it.IsLand && !it.IsImpassible() && it.TerrainFeatures.Count == 0 && it.Resource == null);
            var spot = candidateInnerSpots.Shuffled(shuffleRng).FirstOrDefault()
                ?? candidateOuterSpots.Shuffled(shuffleRng).FirstOrDefault();
            if (twoFoodTerrain != null && spot != null)
                spot.BaseTerrain = twoFoodTerrain;
            else
                bonusesNeeded = 3; // Irredeemable plains situation
        }
        return bonusesNeeded;
    }

    private static int FoodValue(Tile tile)
    {
        var food = GetPotentialYield(tile, Stat.Food);
        return KotlinMath.FloatToInt((float)Math.Pow(food, 2) / 4);
    }

    private static void PlaceFoodBonuses(bool minorCiv, Tile startTile, Ruleset ruleset, int foodBonusesNeeded)
    {
        var rng = new GameContext(tile: startTile).StateBasedRandom("StartNormalizer.placeFoodBonuses");
        var shuffleRng = ShuffleRandom(startTile, "placeFoodBonuses");
        var bonusesStillNeeded = foodBonusesNeeded;
        var oasisEquivalent = ruleset.Terrains.Values.FirstOrDefault(it =>
            it.Type == TerrainType.TerrainFeature
            && ((IHasUniques)it).HasUnique(UniqueType.RareFeature)
            && it.Food >= 2
            && it.Food + it.Production + it.Gold >= 3
            && it.OccursOn.Any(baseName => ruleset.Terrains[baseName].Type == TerrainType.Land));
        // One oasis per start is enough. Don't bother finding a place if there is no good oasis equivalent
        var canPlaceOasis = oasisEquivalent != null;
        var placedInFirst = 0; // Attempt to put first 2 in inner ring and next 3 in second ring
        var placedInSecond = 0;
        var rangeForBonuses = minorCiv ? 2 : 3;

        // Start with list of candidate plots sorted in ring order 1,2,3
        var candidatePlots = startTile.GetTilesInDistanceRange(new IntRange(1, rangeForBonuses))
            .Where(it => it.Resource == null && (oasisEquivalent == null || !it.TerrainFeatureObjects.Contains(oasisEquivalent)))
            .Shuffled(shuffleRng).OrderBy(it => it.AerialDistanceTo(startTile)).ToList();

        // Place food bonuses (and oases) as able
        while (bonusesStillNeeded > 0 && candidatePlots.Count != 0)
        {
            var plot = candidatePlots[0];
            candidatePlots.Remove(plot); // remove the plot as it has now been tried, whether successfully or not
            if (((IHasUniques)plot.GetBaseTerrain()).HasUnique(UniqueType.BlocksResources, new GameContext(attackedTile: plot)))
                continue; // Don't put bonuses on snow hills

            var validBonuses = ruleset.TileResources.Values.Where(it =>
                it.ResourceType == ResourceType.Bonus && it.Food >= 1 && GeneratesNaturallyOn(it, plot)).ToList();
            var goodPlotForOasis = canPlaceOasis && oasisEquivalent!.OccursOn.Contains(plot.LastTerrain.Name);

            if (validBonuses.Count != 0 || goodPlotForOasis)
            {
                if (goodPlotForOasis)
                {
                    plot.AddTerrainFeature(oasisEquivalent!.Name);
                    canPlaceOasis = false;
                }
                else
                    plot.SetTileResource(validBonuses.Random(rng), null, rng);

                if (plot.AerialDistanceTo(startTile) == 1)
                {
                    placedInFirst++;
                    if (placedInFirst == 2) // Resort the list in ring order 2,3,1
                        candidatePlots = candidatePlots.OrderBy(it => Math.Abs(it.AerialDistanceTo(startTile) * 10 - 22)).ToList();
                }
                else if (plot.AerialDistanceTo(startTile) == 2)
                {
                    placedInSecond++;
                    if (placedInSecond == 3) // Resort the list in ring order 3,1,2
                        candidatePlots = candidatePlots.OrderByDescending(it => Math.Abs(it.AerialDistanceTo(startTile) * 10 - 17)).ToList();
                }
                bonusesStillNeeded--;
            }
        }
    }

    private static float GetPotentialYield(Tile tile, Stat stat, bool unimproved = false)
    {
        var baseYield = tile.Stats.GetTileStats((Unciv.Models.Ruleset.Unique.IGameContextCivilization?)null)[stat];
        if (unimproved) return baseYield;

        var improvements = tile.TileMap.Ruleset!.TileImprovements.Values
            .Where(it => !((IHasUniques)it).HasUnique(UniqueType.GreatImprovement)
                && it.UniqueTo == null
                && it.TerrainsCanBeBuiltOn.Contains(tile.LastTerrain.Name))
            .Select(it => it[stat]).ToList();
        var bestImprovementYield = improvements.Count == 0 ? (float?)null : improvements.Max();
        return baseYield + (bestImprovementYield ?? 0f);
    }

    /// <summary><c>TileResource.generatesNaturallyOn(tile)</c></summary>
    private static bool GeneratesNaturallyOn(TileResource resource, Tile tile)
    {
        if (!resource.TerrainsCanBeFoundOn.Contains(tile.LastTerrain.Name)) return false;
        var gameContext = new GameContext(tile: tile);
        var self = (IHasUniques)resource;
        if (self.HasUnique(UniqueType.NoNaturalGeneration, gameContext)) return false;
        if (tile.AllTerrains.Any(it => ((IHasUniques)it).HasUnique(UniqueType.BlocksResources, gameContext))) return false;

        var smallerLandmassUniques = self.GetMatchingUniques(UniqueType.NaturalWonderSmallerLandmass, gameContext).ToList();
        var largerLandmassUniques = self.GetMatchingUniques(UniqueType.NaturalWonderLargerLandmass, gameContext).ToList();

        if (smallerLandmassUniques.Count != 0 || largerLandmassUniques.Count != 0)
        {
            var sortedContinents = tile.TileMap.ContinentsSortedBySize;
            foreach (var unique in smallerLandmassUniques)
                if (sortedContinents.Take(int.Parse(unique.Params[0], System.Globalization.CultureInfo.InvariantCulture)).Contains(tile.GetContinent())) return false;
            foreach (var unique in largerLandmassUniques)
                if (!sortedContinents.Take(int.Parse(unique.Params[0], System.Globalization.CultureInfo.InvariantCulture)).Contains(tile.GetContinent())) return false;
        }
        if (tile.Temperature != null && tile.Humidity != null) // Only works when in map generation
        {
            foreach (var unique in self.GetMatchingUniques(UniqueType.TileGenerationConditions, gameContext))
            {
                var p = unique.Params.Select(it => double.Parse(it, System.Globalization.CultureInfo.InvariantCulture)).ToList();
                if (tile.Temperature < p[0] || tile.Temperature > p[1]) return false;
                if (tile.Humidity < p[2] || tile.Humidity > p[3]) return false;
            }
        }
        return true;
    }
}
