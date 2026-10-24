using Unciv.Logic.Map.Tiles;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Translations;
using Unciv.Utils;

namespace Unciv.Logic.Map.MapGenerator.Mapregions;

// core/src/com/unciv/logic/map/mapgenerator/mapregions/MinorCivPlacer.kt
// Deviations: Civilization is the seam IMapRegionsCivilization (T055); Kotlin's unseeded shuffled() in assignMinorCivsDirectlyToRegions
// draws from the state based random of the first civ (the engine has no unseeded randomness).
public static class MinorCivPlacer
{
    /// <summary>Assigns <paramref name="civs"/> to regions or "uninhabited" land and places them. Depends on
    /// assignLuxuries having been called previously.
    /// Note: can silently fail to place all city states if there is too little room.
    /// Currently our GameStarter fills out with random city states, Civ V behavior is to
    /// forget about the discarded city states entirely.</summary>
    internal static void PlaceMinorCivs(List<Region> regions, TileMap tileMap, IReadOnlyList<IMapRegionsCivilization> civs, TileDataMap tileData, Ruleset ruleset)
    {
        if (civs.Count == 0) return;

        // Some but not all city states are assigned to regions directly. Determine the CS density.
        var unassignedCivs = AssignMinorCivsDirectlyToRegions(civs, regions);

        // Some city states are assigned to "uninhabited" continents - unless it's an archipelago type map
        // (Because then every continent will have been assigned to a region anyway)
        var uninhabitedCoastal = new List<Tile>();
        var uninhabitedHinterland = new List<Tile>();
        var civAssignedToUninhabited = new List<IMapRegionsCivilization>();
        if (!tileMap.UsingArchipelagoRegions())
        {
            SpreadCityStatesBetweenHabitedAndUninhabited(tileMap, regions, tileData, uninhabitedCoastal, uninhabitedHinterland,
                civs, unassignedCivs, civAssignedToUninhabited, ruleset);
        }

        AssignCityStatesToRegionsWithCommonLuxuries(regions, unassignedCivs);
        SpreadCityStatesEvenlyBetweenRegions(unassignedCivs, regions);
        AssignRemainingCityStatesToWorstFertileRegions(regions, unassignedCivs);

        // After we've finished assigning, NOW we actually place them
        PlaceAssignedMinorCivs(civAssignedToUninhabited, tileMap, uninhabitedCoastal, tileData, ruleset, uninhabitedHinterland, regions);
    }

    private static void SpreadCityStatesBetweenHabitedAndUninhabited(
        TileMap tileMap, List<Region> regions, TileDataMap tileData,
        List<Tile> uninhabitedCoastal, List<Tile> uninhabitedHinterland,
        IReadOnlyList<IMapRegionsCivilization> civs, List<IMapRegionsCivilization> unassignedCivs,
        List<IMapRegionsCivilization> civAssignedToUninhabited, Ruleset ruleset)
    {
        var uninhabitedContinents = tileMap.ContinentSizes
            .Where(it => it.Value >= 4 // Don't bother with tiny islands
                && !regions.Any(region => region.ContinentID == it.Key))
            .Select(it => it.Key).ToHashSet();
        var numInhabitedTiles = 0;
        var numUninhabitedTiles = 0;
        // Go through the entire map to build the data
        foreach (var tile in tileMap.Values)
        {
            if (!CanPlaceMinorCiv(tile, tileData)) continue;
            var continent = tile.GetContinent();
            if (uninhabitedContinents.Contains(continent))
            {
                if (tile.IsAdjacentToCoast())
                    uninhabitedCoastal.Add(tile);
                else
                    uninhabitedHinterland.Add(tile);
                numUninhabitedTiles++;
            }
            else
                numInhabitedTiles++;
        }
        // Determine how many minor civs to put on uninhabited continents.
        var maxByUninhabited = (3 * civs.Count * numUninhabitedTiles) / (numInhabitedTiles + numUninhabitedTiles);
        var maxByRatio = (civs.Count + 1) / 2;
        var targetForUninhabited = Math.Min(maxByRatio, maxByUninhabited);
        // Prefer Coast-bias CS for wilderness slots (often coastal).
        var ordered = unassignedCivs.Where(it => PrefersCoastalStart(it, ruleset))
            .Concat(unassignedCivs.Where(it => !PrefersCoastalStart(it, ruleset))).ToList();
        var civsToAssign = ordered.Take(targetForUninhabited).ToList();
        unassignedCivs.RemoveAll(it => civsToAssign.Contains(it));
        civAssignedToUninhabited.AddRange(civsToAssign);
    }

    /// <summary>If there are still unassigned minor civs, assign extra ones to regions that share their
    /// luxury type with two others, as compensation. Because starting close to a city state is good??</summary>
    private static void AssignCityStatesToRegionsWithCommonLuxuries(List<Region> regions, List<IMapRegionsCivilization> unassignedCivs)
    {
        if (unassignedCivs.Count == 0) return;
        var regionsWithCommonLuxuries = regions.Where(it => regions.Count(other => other.Luxury == it.Luxury) >= 3).ToList();
        // assign one civ each to regions with common luxuries if there are enough to go around
        if (regionsWithCommonLuxuries.Count != 0 && regionsWithCommonLuxuries.Count <= unassignedCivs.Count)
        {
            foreach (var region in regionsWithCommonLuxuries)
            {
                var civToAssign = unassignedCivs[0];
                unassignedCivs.Remove(civToAssign);
                region.AssignedMinorCivs.Add(civToAssign);
            }
        }
    }

    /// <summary>Add one extra to each region as long as there are enough to go around</summary>
    private static void SpreadCityStatesEvenlyBetweenRegions(List<IMapRegionsCivilization> unassignedCivs, List<Region> regions)
    {
        if (unassignedCivs.Count == 0) return;
        while (unassignedCivs.Count >= regions.Count)
        {
            foreach (var region in regions)
            {
                var civToAssign = unassignedCivs[0];
                unassignedCivs.Remove(civToAssign);
                region.AssignedMinorCivs.Add(civToAssign);
            }
        }
    }

    /// <summary>At this point there is at least for sure less remaining city states than regions.
    /// Sort regions by fertility and put extra city states in the worst ones.</summary>
    private static void AssignRemainingCityStatesToWorstFertileRegions(List<Region> regions, List<IMapRegionsCivilization> unassignedCivs)
    {
        if (unassignedCivs.Count == 0) return;
        var worstRegions = regions.OrderBy(it => it.TotalFertility).Take(unassignedCivs.Count).ToList();
        foreach (var region in worstRegions)
        {
            var civToAssign = unassignedCivs[0];
            unassignedCivs.Remove(civToAssign);
            region.AssignedMinorCivs.Add(civToAssign);
        }
    }

    /// <summary>Actually place the minor civs, after they have been sorted into groups and assigned to regions</summary>
    private static void PlaceAssignedMinorCivs(
        List<IMapRegionsCivilization> civAssignedToUninhabited, TileMap tileMap, List<Tile> uninhabitedCoastal,
        TileDataMap tileData, Ruleset ruleset, List<Tile> uninhabitedHinterland, List<Region> regions)
    {
        // All minor civs are assigned - now place them
        // First place the "uninhabited continent" ones, preferring coastal starts
        TryPlaceMinorCivsInTiles(civAssignedToUninhabited, tileMap, uninhabitedCoastal, tileData, ruleset);
        TryPlaceMinorCivsInTiles(civAssignedToUninhabited, tileMap, uninhabitedHinterland, tileData, ruleset);
        // Fallback to a random region for civs that couldn't be placed in the wilderness
        foreach (var unplacedCiv in civAssignedToUninhabited)
        {
            var rng = unplacedCiv.StateBasedRandom("MinorCivPlacer.placeAssignedMinorCivs");
            regions.Random(rng).AssignedMinorCivs.Add(unplacedCiv);
        }

        // Now place the ones assigned to specific regions.
        foreach (var region in regions)
            TryPlaceMinorCivsInTiles(region.AssignedMinorCivs, tileMap, region.Tiles.ToList(), tileData, ruleset);
    }

    private static List<IMapRegionsCivilization> AssignMinorCivsDirectlyToRegions(IReadOnlyList<IMapRegionsCivilization> civs, List<Region> regions)
    {
        var minorCivRatio = (float)civs.Count / regions.Count;
        var minorCivPerRegion =
            minorCivRatio > 14f ? 10 // lol
            : minorCivRatio > 11f ? 8
            : minorCivRatio > 8f ? 6
            : minorCivRatio > 5.7f ? 4
            : minorCivRatio > 4.35f ? 3
            : minorCivRatio > 2.7f ? 2
            : minorCivRatio > 1.35f ? 1
            : 0;
        var unassignedCivs = civs.Shuffled(civs[0].StateBasedRandom("MinorCivPlacer.assignMinorCivsDirectlyToRegions"));
        if (minorCivPerRegion > 0)
        {
            foreach (var region in regions)
            {
                var civsToAssign = unassignedCivs.Take(minorCivPerRegion).ToList();
                region.AssignedMinorCivs.AddRange(civsToAssign);
                unassignedCivs.RemoveAll(it => civsToAssign.Contains(it));
            }
        }
        return unassignedCivs;
    }

    /// <summary>Attempts to place civs from <paramref name="civsToPlace"/> in tiles from <paramref name="tileList"/>. Assumes that
    /// <paramref name="tileList"/> is pre-vetted and only contains habitable land tiles.
    /// Respects start biases (nation field + StartBias uniques on nation / CityStateType),
    /// including <c>Avoid [terrainFilter]</c>, same idea as GameStarter major placement.
    /// Will modify both <paramref name="civsToPlace"/> and <paramref name="tileList"/> as it goes!</summary>
    private static void TryPlaceMinorCivsInTiles(List<IMapRegionsCivilization> civsToPlace, TileMap tileMap, List<Tile> tileList, TileDataMap tileData, Ruleset ruleset)
    {
        // More constrained biases (and coastal) first so they do not lose scarce matching tiles.
        var ordered = civsToPlace
            .OrderByDescending(it => it.GetStartBias(ruleset).Count)
            .ThenByDescending(it => PrefersCoastalStart(it, ruleset)).ToList();
        civsToPlace.Clear();
        civsToPlace.AddRange(ordered);

        while (tileList.Count != 0 && civsToPlace.Count != 0)
        {
            var civToAdd = civsToPlace[0];
            var preferred = FilterTilesByStartBias(tileList, civToAdd.GetStartBias(ruleset));
            var pool = preferred.Count != 0 ? preferred : tileList;

            var rng = new GameContext(gameInfo: tileMap.GameInfo).StateBasedRandom("MinorCivPlcer.tryPlaceMinorCivsInTiles");
            var chosenTile = pool.Random(rng);
            tileList.Remove(chosenTile);
            var data = tileData[chosenTile].NotNull();
            // If the randomly chosen tile is too close to a player or a city state, discard it
            if (data.Impacts.ContainsKey(MapRegions.ImpactType.MinorCiv))
                continue;
            // Otherwise, go ahead and place the minor civ
            civsToPlace.Remove(civToAdd);
            PlaceMinorCiv(civToAdd, tileMap, chosenTile, tileData, ruleset);
        }
    }

    /// <summary>Prefer tiles matching positive start biases; drop tiles matching <c>Avoid [filter]</c>.</summary>
    public static List<Tile> FilterTilesByStartBias(List<Tile> tiles, IEnumerable<string> startBiases)
    {
        var preferred = tiles;
        foreach (var startBias in startBiases)
        {
            if (startBias.EqualsPlaceholderText("Avoid []"))
            {
                var tileToAvoid = startBias.GetPlaceholderParameters()[0];
                preferred = preferred.Where(tile => !tile.GetTilesInDistance(1).Any(it => it.MatchesTerrainFilter(tileToAvoid, null))).ToList();
            }
            else
                preferred = preferred.Where(tile => tile.GetTilesInDistance(1).Any(it => it.MatchesTerrainFilter(startBias, null))).ToList();
        }
        return preferred;
    }

    /// <summary>True when nation or city-state type start bias includes Coast.</summary>
    public static bool PrefersCoastalStart(IMapRegionsCivilization civ, Ruleset ruleset) =>
        civ.GetStartBias(ruleset).Contains("Coast");

    private static bool CanPlaceMinorCiv(Tile tile, TileDataMap tileData) =>
        !tile.IsWater && !tile.IsImpassible()
        && !tileData[tile].NotNull().IsJunk
        && !((IHasUniques)tile.GetBaseTerrain()).GetMatchingUniques(UniqueType.HasQuality).Any(it => it.Params[0] == "Undesirable") // So we don't get snow hills
        && tile.Neighbors.Count == 6; // Avoid map edges

    private static void PlaceMinorCiv(IMapRegionsCivilization civ, TileMap tileMap, Tile tile, TileDataMap tileData, Ruleset ruleset)
    {
        tileMap.AddStartingLocation(civ.CivId, tile);
        tileData.PlaceImpact(MapRegions.ImpactType.MinorCiv, tile, 4);
        tileData.PlaceImpact(MapRegions.ImpactType.Luxury, tile, 3);
        tileData.PlaceImpact(MapRegions.ImpactType.Strategic, tile, 0);
        tileData.PlaceImpact(MapRegions.ImpactType.Bonus, tile, 3);

        StartNormalizer.NormalizeStart(tile, tileMap, tileData, ruleset, isMinorCiv: true);
    }
}
