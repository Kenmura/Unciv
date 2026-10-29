using Unciv.Logic.Map.MapGenerator.ResourcePlacement;
using Unciv.Logic.Map.Tiles;
using Unciv.Mathematics;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Translations;
using Unciv.Utils;

namespace Unciv.Logic.Map.MapGenerator.Mapregions;

// core/src/com/unciv/logic/map/mapgenerator/mapregions/MapRegions.kt (TileDataMap, MapRegions, and the file-level functions)
// Deviations: Civilization is the interface IMapRegionsCivilization (T055). Kotlin HashMap<Int,Int> whose iteration order feeds the result is a JavaHashMap.

public class TileDataMap(int size)
{
    private readonly MapGenTileData?[] data = new MapGenTileData?[size];
    public int Size => data.Length;

    public MapGenTileData? this[Tile tile]
    {
        get => data[tile.ZeroBasedIndex];
        set => data[tile.ZeroBasedIndex] = value;
    }

    /// <summary>Adds numbers to tileData in a similar way to closeStartPenalty, but for different types</summary>
    public void PlaceImpact(MapRegions.ImpactType type, Tile tile, int radius)
    {
        // Epicenter
        this[tile].NotNull().Impacts[type] = 99;
        if (radius <= 0) return;

        for (var ring = 1; ring <= radius; ring++)
        {
            var ringValue = radius - ring + 1;
            tile.ForEachTileAtDistance(ring, outerTile =>
            {
                var tileData = this[outerTile].NotNull();
                if (tileData.Impacts.TryGetValue(type, out var existing))
                    tileData.Impacts[type] = Math.Min(50, Math.Max(ringValue, existing) + 2);
                else
                    tileData.Impacts[type] = ringValue;
            });
        }
    }
}

public class MapRegions(Ruleset ruleset)
{
    public static readonly IReadOnlyDictionary<int, int> MinimumFoodForRing = new Dictionary<int, int> { [1] = 1, [2] = 4, [3] = 4 };
    public static readonly IReadOnlyDictionary<int, int> MinimumProdForRing = new Dictionary<int, int> { [1] = 0, [2] = 0, [3] = 2 };
    public static readonly IReadOnlyDictionary<int, int> MinimumGoodForRing = new Dictionary<int, int> { [1] = 3, [2] = 6, [3] = 8 };
    public const int MaximumJunk = 9;

    public static readonly IReadOnlyList<int> FirstRingFoodScores = [0, 8, 14, 19, 22, 24, 25];
    public static readonly IReadOnlyList<int> FirstRingProdScores = [0, 10, 16, 20, 20, 12, 0];
    public static readonly IReadOnlyList<int> SecondRingFoodScores = [0, 2, 5, 10, 20, 25, 28, 30, 32, 34, 35];
    public static readonly IReadOnlyList<int> SecondRingProdScores = [0, 10, 20, 25, 30, 35];

    /// <summary>Insertion-ordered like Kotlin's mapOf; at 14, both of us can settle a min distance expand without competing for tiles</summary>
    public static readonly LinkedHashMap<int, int> CloseStartPenaltyForRing = LinkedHashMapOf(
        (0, 99), (1, 97), (2, 95),
        (3, 92), (4, 89), (5, 69),
        (6, 57), (7, 24), (8, 20),
        (9, 16), (10, 12), (11, 8),
        (12, 6), (13, 4));

    public static readonly IReadOnlyDictionary<int, IReadOnlyList<float>> RandomLuxuryRatios = new Dictionary<int, IReadOnlyList<float>>
    {
        [1] = [1f],
        [2] = [0.55f, 0.45f],
        [3] = [0.40f, 0.33f, 0.27f],
        [4] = [0.35f, 0.25f, 0.25f, 0.15f],
        [5] = [0.25f, 0.25f, 0.20f, 0.15f, 0.15f],
        [6] = [0.20f, 0.20f, 0.20f, 0.15f, 0.15f, 0.10f],
        [7] = [0.20f, 0.20f, 0.15f, 0.15f, 0.10f, 0.10f, 0.10f],
        [8] = [0.20f, 0.15f, 0.15f, 0.10f, 0.10f, 0.10f, 0.10f, 0.10f],
    };

    // This number is 23 in G&K, but there's a bug where hills are exempt so this number brings
    // the result closer to the density and distribution that was probably intended.
    public const int BaseMinorDepositFrequency = 30;

    private static LinkedHashMap<int, int> LinkedHashMapOf(params (int Key, int Value)[] pairs)
    {
        var map = new LinkedHashMap<int, int>();
        foreach (var (key, value) in pairs) map[key] = value;
        return map;
    }

    public Ruleset Ruleset { get; } = ruleset;

    private readonly List<Region> regions = [];
    private TileDataMap tileData = null!; // JUSTIFY: T119 Kotlin lateinit

    // Test/downstream access (Kotlin private/internal members shared inside the module)
    internal IReadOnlyList<Region> Regions => regions;
    internal TileDataMap TileData => tileData;

    /// <summary>Creates <paramref name="numRegions"/> number of balanced regions for civ starting locations.</summary>
    public void GenerateRegions(TileMap tileMap, int numRegions)
    {
        if (numRegions <= 0) return; // Don't bother about regions, probably map editor
        if (tileMap.ContinentSizes.IsEmpty) throw new InvalidOperationException("No Continents on this map!");

        var mapParameters = tileMap.MapParameters;
        var radius = mapParameters.Shape == MapShape.Hexagonal || mapParameters.Shape == MapShape.FlatEarth
            ? (float)mapParameters.MapSize.Radius
            : (float)Math.Max(mapParameters.MapSize.Width / 2, mapParameters.MapSize.Height / 2);
        // A huge box including the entire map.
        var mapRect = new Rectangle(-radius, -radius, (radius * 2) + 1, (radius * 2) + 1);

        // Lots of small islands - just split ut the map in rectangles while ignoring Continents
        // 25% is chosen as limit so Four Corners maps don't fall in this category
        if (tileMap.UsingArchipelagoRegions())
        {
            // Make a huge rectangle covering the entire map
            var hugeRect = new Region(tileMap, mapRect, -1) // -1 meaning ignore continent data
            {
                AffectedByWorldWrap = false, // Might as well start at the seam
            };
            hugeRect.UpdateTiles();
            DivideRegion(hugeRect, numRegions);
            return;
        }
        // Continents type - distribute civs according to total fertility, then split as needed
        var continents = tileMap.ContinentSizes.Keys.ToList();
        var civsAddedToContinent = new JavaHashMap<int, int>(); // Continent ID, civs added
        var continentFertility = new Dictionary<int, int>(); // Continent ID, total fertility
        // Keep track of the even-q columns each continent is at, to figure out if they wrap
        var continentToColumnsItsIn = new Dictionary<int, HashSet<int>>();

        // Calculate continent fertilities and columns
        foreach (var tile in tileMap.Values)
        {
            var continent = tile.GetContinent();
            if (continent != -1)
            {
                continentFertility[continent] = unchecked(tile.GetTileFertility(true) +
                    continentFertility.GetValueOrDefault(continent));

                if (!continentToColumnsItsIn.ContainsKey(continent))
                    continentToColumnsItsIn[continent] = [];

                continentToColumnsItsIn[continent].Add(tile.GetColumn());
            }
        }

        // Assign regions to the best continents, giving half value for region #2 etc
        for (var i = 0; i < numRegions; i++)
        {
            // maxByOrNull: the first continent with the highest value wins
            var bestContinent = continents[0];
            var bestValue = int.MinValue;
            foreach (var continentId in continents)
            {
                var value = continentFertility[continentId] / (1 + (civsAddedToContinent.TryGetValue(continentId, out var added) ? added : 0));
                if (bestValue < value)
                {
                    bestContinent = continentId;
                    bestValue = value;
                }
            }
            civsAddedToContinent[bestContinent] = (civsAddedToContinent.TryGetValue(bestContinent, out var n) ? n : 0) + 1;
        }

        // Split up the continents
        foreach (var continent in civsAddedToContinent.Keys.ToList())
        {
            var continentRegion = new Region(tileMap, mapRect, continent);
            var cols = continentToColumnsItsIn[continent];
            // Set origin at the rightmost column which does not have a neighbor on the left
            continentRegion.Rect = continentRegion.Rect with
            {
                X = cols.Where(it => !cols.Contains(it - 1)).Max(),
                Width = cols.Count,
            };
            if (tileMap.MapParameters.WorldWrap)
            {
                // Check if the continent is wrapping - if the leftmost col is not the one we set origin by
                if (cols.Min() < continentRegion.Rect.X)
                    continentRegion.AffectedByWorldWrap = true;
            }
            continentRegion.UpdateTiles();
            DivideRegion(continentRegion, civsAddedToContinent[continent]);
        }
    }

    /// <summary>Recursive function, divides a region into <paramref name="numDivisions"/> pars of equal-ish fertility</summary>
    private void DivideRegion(Region region, int numDivisions)
    {
        if (numDivisions <= 1)
        {
            // We're all set, save the region and return
            regions.Add(region);
            return;
        }

        var firstDivisions = numDivisions / 2; // Since int division rounds down, works for all numbers
        var (first, second) = SplitRegion(region, (100 * firstDivisions) / numDivisions);
        DivideRegion(first, firstDivisions);
        DivideRegion(second, numDivisions - firstDivisions);
    }

    /// <summary>Splits a region in 2, with the first having <paramref name="firstPercent"/> of total fertility</summary>
    private static (Region, Region) SplitRegion(Region regionToSplit, int firstPercent)
    {
        var targetFertility = (regionToSplit.TotalFertility * firstPercent) / 100;

        var splitOffRegion = new Region(regionToSplit.TileMap, regionToSplit.Rect, regionToSplit.ContinentID);

        var widerThanTall = regionToSplit.Rect.Width > regionToSplit.Rect.Height;

        var bestSplitPoint = 1; // will be the size of the split-off region
        var closestFertility = 0;
        var cumulativeFertility = 0;

        var highestPointToTry = widerThanTall
            ? KotlinMath.FloatToInt(regionToSplit.Rect.Width)
            : KotlinMath.FloatToInt(regionToSplit.Rect.Height);
        var halfwayPoint = highestPointToTry / 2;

        for (var splitPoint = 1; splitPoint <= highestPointToTry; splitPoint++)
        {
            var splitRect = splitOffRegion.Rect;
            var nextRect = widerThanTall
                ? splitOffRegion.TileMap.GetTilesInRectangle(new Rectangle(
                    splitRect.X + splitPoint - 1, splitRect.Y,
                    1f, splitRect.Height))
                : splitOffRegion.TileMap.GetTilesInRectangle(new Rectangle(
                    splitRect.X, splitRect.Y + splitPoint - 1,
                    splitRect.Width, 1f));

            unchecked
            {
                cumulativeFertility += splitOffRegion.ContinentID == -1
                    ? nextRect.Sum(it => it.GetTileFertility(false))
                    : nextRect.Sum(it => it.GetContinent() == splitOffRegion.ContinentID ? it.GetTileFertility(true) : 0);
            }

            // Better than last try?
            var bestSplitPointFertilityDeltaFromTarget = Math.Abs(closestFertility - targetFertility);
            var currentSplitPointFertilityDeltaFromTarget = Math.Abs(cumulativeFertility - targetFertility);
            if (currentSplitPointFertilityDeltaFromTarget < bestSplitPointFertilityDeltaFromTarget
                || (currentSplitPointFertilityDeltaFromTarget == bestSplitPointFertilityDeltaFromTarget // same fertility split but better 'amount of tiles' split
                    && Math.Abs(halfwayPoint - splitPoint) < Math.Abs(halfwayPoint - bestSplitPoint)))
            { // current split point is closer to the halfway point
                bestSplitPoint = splitPoint;
                closestFertility = cumulativeFertility;
            }
        }

        if (widerThanTall)
        {
            splitOffRegion.Rect = splitOffRegion.Rect with { Width = bestSplitPoint };
            regionToSplit.Rect = regionToSplit.Rect with
            {
                X = splitOffRegion.Rect.X + splitOffRegion.Rect.Width,
                Width = regionToSplit.Rect.Width - bestSplitPoint,
            };
        }
        else
        {
            splitOffRegion.Rect = splitOffRegion.Rect with { Height = bestSplitPoint };
            regionToSplit.Rect = regionToSplit.Rect with
            {
                Y = splitOffRegion.Rect.Y + splitOffRegion.Rect.Height,
                Height = regionToSplit.Rect.Height - bestSplitPoint,
            };
        }
        splitOffRegion.UpdateTiles();
        regionToSplit.UpdateTiles();

        return (splitOffRegion, regionToSplit);
    }

    /// <summary>Buckets for startBias to region assignments, used only in <see cref="AssignRegions"/>. PositiveFallback is only for logging.</summary>
    private enum BiasTypes { Coastal, Positive, Negative, Random, PositiveFallback }

    /// <summary>B024: nations flagged PlaceFirstAlongOcean go first among the coast civs (stable otherwise, so rulesets without the flag are unchanged).</summary>
    private List<IMapRegionsCivilization> CoastCivsPlacedFirstAlongOceanFirst(List<IMapRegionsCivilization> coastCivs) =>
        coastCivs.OrderBy(it => Ruleset.Nations.TryGetValue(it.CivName, out var nation) && nation.PlaceFirstAlongOcean ? 0 : 1).ToList();

    public void AssignRegions(TileMap tileMap, IReadOnlyList<IMapRegionsCivilization> civilizations, GameParameters gameParameters)
    {
        if (civilizations.Count == 0) return;

        AssignRegionTypes();

        // Generate tile data for all tiles
        tileData = new TileDataMap(tileMap.Values.Count);
        foreach (var tile in tileMap.Values)
        {
            var newData = new MapGenTileData(tile, regions.FirstOrDefault(it => it.Tiles.Contains(tile)), Ruleset);
            tileData[tile] = newData;
        }

        // Sort regions by fertility so the worse regions get to pick first
        var sortedRegions = regions.OrderBy(it => it.TotalFertility).ToList();
        foreach (var region in sortedRegions) RegionStartFinder.FindStart(region, tileData);
        foreach (var region in regions)
        {
            StartNormalizer.NormalizeStart(tileMap[region.StartPosition.NotNull()], tileMap, tileData, Ruleset, isMinorCiv: false);
        }

        var civBiases = new Dictionary<IMapRegionsCivilization, List<string>>();
        foreach (var civ in civilizations) civBiases[civ] = civ.GetStartBias(Ruleset);
        // This ensures each civ can only be in one of the buckets
        var civsByBiasType = new Dictionary<BiasTypes, List<IMapRegionsCivilization>>();
        foreach (var (civ, startBias) in civBiases)
        {
            BiasTypes bucket;
            if (gameParameters.NoStartBias) bucket = BiasTypes.Random;
            else if (startBias.Any(bias => bias.EqualsPlaceholderText("Avoid []"))) bucket = BiasTypes.Negative;
            else if (startBias.Contains("Coast")) bucket = BiasTypes.Coastal;
            else if (startBias.Count != 0) bucket = BiasTypes.Positive;
            else bucket = BiasTypes.Random;

            if (!civsByBiasType.TryGetValue(bucket, out var list)) civsByBiasType[bucket] = list = [];
            list.Add(civ);
        }

        var coastBiasCivs = CoastCivsPlacedFirstAlongOceanFirst(civsByBiasType.GetValueOrDefault(BiasTypes.Coastal) ?? []);
        var positiveBiasCivs = civsByBiasType.GetValueOrDefault(BiasTypes.Positive)
            ?.OrderBy(it => civBiases[it].Count).ToList() // civs with only one desired region go first
            ?? [];
        var negativeBiasCivs = civsByBiasType.GetValueOrDefault(BiasTypes.Negative)
            ?.OrderByDescending(it => civBiases[it].Count).ToList() // Civs with more complex avoids go first
            ?? [];
        var randomCivs = civsByBiasType.GetValueOrDefault(BiasTypes.Random)
            ?.ToList() // We might fill this up as we go
            ?? [];
        var positiveBiasFallbackCivs = new List<IMapRegionsCivilization>(); // Civs who couldn't get their desired region at first pass
        var unpickedRegions = regions.ToList();

        // First assign coast bias civs
        foreach (var civ in coastBiasCivs)
        {
            // Try to find a coastal start, preferably a really coastal one
            var startRegion = unpickedRegions.Where(it => tileMap[it.StartPosition.NotNull()].IsAdjacentToCoast())
                .MaxByOrNull(it => it.GetTerrainAmount("Coastal"));
            if (startRegion != null)
            {
                LogAssignRegion(true, BiasTypes.Coastal, civ, startRegion);
                AssignCivToRegion(civ, startRegion);
                unpickedRegions.Remove(startRegion);
                continue;
            }
            // Else adjacent to a lake
            startRegion = unpickedRegions.Where(it => tileMap[it.StartPosition.NotNull()].Neighbors
                    .Any(neighbor => ((IHasUniques)neighbor.GetBaseTerrain()).HasUnique(UniqueType.FreshWater)))
                .MaxByOrNull(it => it.GetTerrainAmount("Coastal"));
            if (startRegion != null)
            {
                LogAssignRegion(true, BiasTypes.Coastal, civ, startRegion);
                AssignCivToRegion(civ, startRegion);
                unpickedRegions.Remove(startRegion);
                continue;
            }
            // Else adjacent to a river
            startRegion = unpickedRegions.Where(it => tileMap[it.StartPosition.NotNull()].IsAdjacentToRiver())
                .MaxByOrNull(it => it.GetTerrainAmount("Coastal"));
            if (startRegion != null)
            {
                LogAssignRegion(true, BiasTypes.Coastal, civ, startRegion);
                AssignCivToRegion(civ, startRegion);
                unpickedRegions.Remove(startRegion);
                continue;
            }
            // Else at least close to a river ????
            startRegion = unpickedRegions.Where(it => tileMap[it.StartPosition.NotNull()].Neighbors.Any(neighbor => neighbor.IsAdjacentToRiver()))
                .MaxByOrNull(it => it.GetTerrainAmount("Coastal"));
            if (startRegion != null)
            {
                LogAssignRegion(true, BiasTypes.Coastal, civ, startRegion);
                AssignCivToRegion(civ, startRegion);
                unpickedRegions.Remove(startRegion);
                continue;
            }
            // Else pick a random region at the end
            LogAssignRegion(false, BiasTypes.Coastal, civ);
            randomCivs.Add(civ);
        }

        // Next do positive bias civs
        foreach (var civ in positiveBiasCivs)
        {
            // Try to find a start that matches any of the desired regions, ideally with lots of desired terrain
            var preferred = civBiases[civ];
            var startRegion = unpickedRegions.Where(it => preferred.Contains(it.Type))
                .MaxByOrNull(it => SumOfTerrainCounts(it, preferred));
            if (startRegion != null)
            {
                LogAssignRegion(true, BiasTypes.Positive, civ, startRegion);
                AssignCivToRegion(civ, startRegion);
                unpickedRegions.Remove(startRegion);
                continue;
            }
            if (preferred.Count == 1)
            { // Civs with a single bias (only) get to look for a fallback region
                positiveBiasFallbackCivs.Add(civ);
            }
            else
            { // Others get random starts
                LogAssignRegion(false, BiasTypes.Positive, civ);
                randomCivs.Add(civ);
            }
        }

        // Do a second pass for fallback civs, choosing the region most similar to the desired type
        foreach (var civ in positiveBiasFallbackCivs)
        {
            var startRegion = GetFallbackRegion(civBiases[civ][0], unpickedRegions);
            LogAssignRegion(true, BiasTypes.PositiveFallback, civ, startRegion);
            AssignCivToRegion(civ, startRegion);
            unpickedRegions.Remove(startRegion);
        }

        // Next do negative bias ones (ie "Avoid []")
        foreach (var civ in negativeBiasCivs)
        {
            var avoidBias = civBiases[civ].Where(bias => bias.EqualsPlaceholderText("Avoid []")).ToList();
            var preferred = civBiases[civ].Where(bias => !bias.EqualsPlaceholderText("Avoid []")).ToList();
            var avoided = avoidBias.Select(it => it.GetPlaceholderParameters()[0]).ToList();
            // Try to find a region not of the avoided types, secondary sort by
            // least number of undesired terrains (weighed double) / most number of desired terrains
            var startRegion = unpickedRegions.Where(it => !avoided.Contains(it.Type))
                .MinByOrNull(it => NegativeBiasKey(it, preferred));
            if (startRegion != null)
            {
                LogAssignRegion(true, BiasTypes.Negative, civ, startRegion);
                AssignCivToRegion(civ, startRegion);
                unpickedRegions.Remove(startRegion);
                continue;
            }
            LogAssignRegion(false, BiasTypes.Negative, civ);
            randomCivs.Add(civ); // else pick a random region at the end
        }

        // Finally assign the remaining civs randomly
        foreach (var civ in randomCivs)
        {
            var rng = civ.StateBasedRandom("MapRegions.assignRegions");
            // throws if regions.size < civilizations.size or if the assigning mismatched - leads to popup on newgame screen
            if (unpickedRegions.Count == 0) throw new InvalidOperationException("Collection is empty.");
            var startRegion = unpickedRegions[rng.NextInt(unpickedRegions.Count)];
            LogAssignRegion(true, BiasTypes.Random, civ, startRegion);
            AssignCivToRegion(civ, startRegion);
            unpickedRegions.Remove(startRegion);
        }
    }

    /// <summary><c>region.terrainCounts.filterKeys { it in names }.values.sum()</c></summary>
    private static int SumOfTerrainCounts(Region region, List<string> names)
    {
        var sum = 0;
        foreach (var (terrain, count) in region.TerrainCounts)
            if (names.Contains(terrain)) sum = unchecked(sum + count);
        return sum;
    }

    /// <summary>
    /// Sort key of the negative-bias pass. Kotlin quirk kept on purpose: in MapRegions.kt the lambda body is
    /// `2 * avoidedSum` NEWLINE `- preferredSum`, which parses as two statements, so the key is just `-preferredSum`.
    /// </summary>
    internal static int NegativeBiasKey(Region region, List<string> preferred) => unchecked(-SumOfTerrainCounts(region, preferred));

    /// <summary>Sets region.type</summary>
    private void AssignRegionTypes()
    {
        var regionTypes = Ruleset.Terrains.Values.Where(it => MapRegionFunctions.GetRegionPriority(it) != null)
            .OrderBy(it => MapRegionFunctions.GetRegionPriority(it).NotNull()).ToList();

        foreach (var region in regions)
        {
            region.CountTerrains();

            foreach (var type in regionTypes)
            {
                // Test exclusion criteria first
                if (((IHasUniques)type).GetMatchingUniques(UniqueType.RegionRequireFirstLessThanSecond).Any(it =>
                        region.GetTerrainAmount(it.Params[0]) >= region.GetTerrainAmount(it.Params[1])))
                {
                    continue;
                }
                // Test inclusion criteria
                if (((IHasUniques)type).GetMatchingUniques(UniqueType.RegionRequirePercentSingleType).Any(it =>
                        region.GetTerrainAmount(it.Params[1]) >= (int.Parse(it.Params[0], System.Globalization.CultureInfo.InvariantCulture) * region.Tiles.Count) / 100)
                    || ((IHasUniques)type).GetMatchingUniques(UniqueType.RegionRequirePercentTwoTypes).Any(it =>
                        region.GetTerrainAmount(it.Params[1]) + region.GetTerrainAmount(it.Params[2])
                            >= (int.Parse(it.Params[0], System.Globalization.CultureInfo.InvariantCulture) * region.Tiles.Count) / 100))
                {
                    region.Type = type.Name;
                    break;
                }
            }
        }
    }

    private void LogAssignRegion(bool success, BiasTypes startBiasType, IMapRegionsCivilization civ, Region? region = null)
    {
        if (Log.Backend.IsRelease) return;

        var bias = civ.GetStartBias(Ruleset);
        var logCiv = civ.CivName + " " + "(" + string.Join(",", bias) + ")";
        var msg = success ? "(%s): %s to %s" : "no region (%s) found for %s";
        if (success)
            Log.Debug(new Tag("assignRegions"), msg, startBiasType, logCiv, region);
        else
            Log.Debug(new Tag("assignRegions"), msg, startBiasType, logCiv);
    }

    private void AssignCivToRegion(IMapRegionsCivilization civ, Region region)
    {
        var tile = region.TileMap[region.StartPosition.NotNull()];
        region.TileMap.AddStartingLocation(civ.CivId, tile);

        // Place impacts to keep city states etc at appropriate distance
        tileData.PlaceImpact(ImpactType.MinorCiv, tile, 6);
        tileData.PlaceImpact(ImpactType.Luxury, tile, 3);
        tileData.PlaceImpact(ImpactType.Strategic, tile, 0);
        tileData.PlaceImpact(ImpactType.Bonus, tile, 3);
    }

    /// <returns>the region most similar to a region of <paramref name="type"/></returns>
    private static Region GetFallbackRegion(string type, List<Region> candidates) =>
        candidates.MaxByOrNull(it => it.GetTerrainAmount(type))
            ?? throw new NullReferenceException("No candidate regions");

    public void PlaceResourcesAndMinorCivs(TileMap tileMap, IReadOnlyList<IMapRegionsCivilization> minorCivs)
    {
        PlaceNaturalWonderImpacts(tileMap);

        var (cityStateLuxuries, randomLuxuries) = LuxuryResourcePlacementLogic.AssignLuxuries(regions, tileData, Ruleset);
        MinorCivPlacer.PlaceMinorCivs(regions, tileMap, minorCivs, tileData, Ruleset);
        LuxuryResourcePlacementLogic.PlaceLuxuries(regions, tileMap, tileData, Ruleset, cityStateLuxuries, randomLuxuries);
        StrategicBonusResourcePlacementLogic.PlaceStrategicAndBonuses(tileMap, regions, tileData);
    }

    /// <summary>Places impacts from NWs that have been generated just prior to this step.</summary>
    private void PlaceNaturalWonderImpacts(TileMap tileMap)
    {
        foreach (var tile in tileMap.Values.Where(it => it.IsNaturalWonder()).ToList())
        {
            tileData.PlaceImpact(ImpactType.Bonus, tile, 1);
            tileData.PlaceImpact(ImpactType.Strategic, tile, 1);
            tileData.PlaceImpact(ImpactType.Luxury, tile, 1);
            tileData.PlaceImpact(ImpactType.MinorCiv, tile, 1);
        }
    }

    public enum ImpactType
    {
        Strategic,
        Luxury,
        Bonus,
        MinorCiv,
    }
}

/// <summary>The top-level (file-level) functions of MapRegions.kt</summary>
public static class MapRegionFunctions
{
    /// <summary>For dividing the map into Regions to determine start locations (Kotlin <c>Tile.getTileFertility</c>)</summary>
    public static int GetTileFertility(this Tile tile, bool checkCoasts)
    {
        var fertility = 0;
        foreach (var terrain in tile.AllTerrains)
        {
            var uniques = (IHasUniques)terrain;
            if (uniques.HasUnique(UniqueType.OverrideFertility))
                return ParseInt(uniques.GetMatchingUniques(UniqueType.OverrideFertility).First().Params[0]);
            uniques.ForEachMatchingUnique(UniqueType.AddFertility, GameContext.EmptyState, it =>
            {
                fertility = unchecked(fertility + ParseInt(it.Params[0]));
            });
        }
        if (tile.IsAdjacentToRiver()) fertility += 1;
        if (tile.IsAdjacentTo(Constants.FreshWater)) fertility += 1; // meaning total +2 for river
        if (checkCoasts && tile.IsAdjacentToCoast()) fertility += 2;
        return fertility;
    }

    private static int ParseInt(string text) => int.Parse(text, System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Kotlin <c>getRegionPriority</c>; a null terrain means "hybrid"</summary>
    public static int? GetRegionPriority(Terrain? terrain)
    {
        if (terrain == null) // ie "hybrid"
            return 99999; // a big number
        var uniques = (IHasUniques)terrain;
        if (!uniques.HasUnique(UniqueType.RegionRequirePercentSingleType)
            && !uniques.HasUnique(UniqueType.RegionRequirePercentTwoTypes))
            return null;
        return uniques.HasUnique(UniqueType.RegionRequirePercentSingleType)
            ? ParseInt(uniques.GetMatchingUniques(UniqueType.RegionRequirePercentSingleType).First().Params[2])
            : ParseInt(uniques.GetMatchingUniques(UniqueType.RegionRequirePercentTwoTypes).First().Params[3]);
    }

    /// <returns>a fake unique with the same conditionals, but sorted alphabetically.
    /// Used to save some memory and time when building resource lists.</returns>
    internal static Unique AnonymizeUnique(Unique unique) => new(
        "RULE" + " " + string.Join(" ", unique.Modifiers.OrderBy(it => it.Text, StringComparer.Ordinal).Select(it => "<" + it.Text + ">")));

    internal static bool IsWaterOnlyResource(TileResource resource, Ruleset ruleset) => resource.TerrainsCanBeFoundOn
        .All(terrainName => ruleset.Terrains[terrainName].Type == TerrainType.Water);

    /// <returns>a fake unique with conditionals that will satisfy the same conditions as terrainsCanBeFoundOn</returns>
    internal static Unique GetTerrainRule(Terrain terrain, Ruleset ruleset)
    {
        if (terrain.Type == TerrainType.TerrainFeature)
        {
            if (((IHasUniques)terrain).HasUnique(UniqueType.VisibilityElevation))
                return new Unique($"RULE <in [{terrain.Name}] tiles>");
            return new Unique($"RULE <in [{terrain.Name}] tiles> " + string.Join(" ", ruleset.Terrains.Values
                .Where(it => it.Type == TerrainType.TerrainFeature && ((IHasUniques)it).HasUnique(UniqueType.VisibilityElevation))
                .Select(it => $"<in tiles without [{it.Name}]>")));
        }
        return new Unique($"RULE <in [Featureless] [{terrain.Name}] tiles>");
    }
}
