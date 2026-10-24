using Unciv.Logic.Map.Tiles;
using Unciv.Mathematics;
using Unciv.Models.Ruleset.Tile;
using Unciv.Utils;

namespace Unciv.Logic.Map.MapGenerator.Mapregions;

// core/src/com/unciv/logic/map/mapgenerator/mapregions/RegionStartFinder.kt
// Deviation: Kotlin's identity-hashed HashSet<Tile> become insertion-ordered LinkedHashSet, so ties (maxByOrNull: first wins) are deterministic.
public static class RegionStartFinder
{
    /// <summary>Attempts to find a good start close to the center of <paramref name="region"/>. Calls setRegionStart with the position</summary>
    internal static void FindStart(Region region, TileDataMap tileData)
    {
        var fallbackTiles = new LinkedHashSet<Tile>();
        // Priority: 1. Adjacent to river, 2. Adjacent to coast or fresh water, 3. Other.

        // First check center rect, then middle. Only check the outer area if no good sites found
        var centerTiles = new LinkedHashSet<Tile>(region.TileMap.GetTilesInRectangle(GetCentralRectangle(region.Rect, 0.33f)));
        if (FindGoodPosition(centerTiles, region, tileData, fallbackTiles)) return;

        var middleDonut = new LinkedHashSet<Tile>(region.TileMap.GetTilesInRectangle(GetCentralRectangle(region.Rect, 0.67f))
            .Where(it => !centerTiles.Contains(it)));
        if (FindGoodPosition(middleDonut, region, tileData, fallbackTiles)) return;

        // Now check the outer tiles. For these we don't care about rivers, coasts etc
        var outerDonut = region.TileMap.GetTilesInRectangle(region.Rect)
            .Where(it => !centerTiles.Contains(it) && !middleDonut.Contains(it));
        if (FindEdgePosition(outerDonut, region, tileData, fallbackTiles)) return;

        FindFallbackPosition(fallbackTiles, tileData, region);
    }

    private static bool FindGoodPosition(IEnumerable<Tile> centerTiles, Region region, TileDataMap tileData, LinkedHashSet<Tile> fallbackTiles)
    {
        var riverTiles = new LinkedHashSet<Tile>();
        var wetTiles = new LinkedHashSet<Tile>();
        var dryTiles = new LinkedHashSet<Tile>();
        foreach (var tile in centerTiles)
        {
            if (tileData[tile].NotNull().IsTwoFromCoast)
                continue; // Don't even consider tiles two from coast
            if (region.ContinentID != -1 && region.ContinentID != tile.GetContinent())
                continue; // Wrong continent
            if (tile.IsLand && !tile.IsImpassible())
            {
                EvaluateTileForStart(tile, tileData);
                if (tile.IsAdjacentToRiver())
                    riverTiles.Add(tile);
                else if (tile.IsAdjacentToCoast() || tile.IsAdjacentTo(Constants.FreshWater))
                    wetTiles.Add(tile);
                else
                    dryTiles.Add(tile);
            }
        }
        // Did we find a good start position?
        foreach (var list in new[] { riverTiles, wetTiles, dryTiles })
        {
            if (list.Any(it => tileData[it].NotNull().IsGoodStart))
            {
                SetRegionStart(region, list
                    .Where(it => tileData[it].NotNull().IsGoodStart).MaxByOrNull(it => tileData[it].NotNull().StartScore).NotNull().Position, tileData);
                return true;
            }
            if (list.Count != 0) // Save the best not-good-enough spots for later fallback
                fallbackTiles.Add(list.MaxByOrNull(it => tileData[it].NotNull().StartScore).NotNull());
        }
        return false;
    }

    private static bool FindEdgePosition(IEnumerable<Tile> outerDonut, Region region, TileDataMap tileData, LinkedHashSet<Tile> fallbackTiles)
    {
        var dryTiles = new LinkedHashSet<Tile>();
        foreach (var tile in outerDonut)
        {
            if (region.ContinentID != -1 && region.ContinentID != tile.GetContinent())
                continue; // Wrong continent
            if (tile.IsLand && !tile.IsImpassible())
            {
                EvaluateTileForStart(tile, tileData);
                dryTiles.Add(tile);
            }
        }
        // Were any of them good?
        if (dryTiles.Any(it => tileData[it].NotNull().IsGoodStart))
        {
            // Find the one closest to the center
            var centerX = region.Rect.X + (region.Rect.Width / 2);
            var centerY = region.Rect.Y + (region.Rect.Height / 2);
            var closestToCenter = dryTiles.Where(it => tileData[it].NotNull().IsGoodStart)
                .MinByOrNull(it =>
                    (region.TileMap.GetIfTileExistsOrNull(KotlinMath.RoundToInt(centerX), KotlinMath.RoundToInt(centerY))
                        ?? region.TileMap.Values.First())
                    .AerialDistanceTo(it)).NotNull();

            SetRegionStart(region, closestToCenter.Position, tileData);
            return true;
        }
        if (dryTiles.Count != 0)
            fallbackTiles.Add(dryTiles.MaxByOrNull(it => tileData[it].NotNull().StartScore).NotNull());
        return false;
    }

    private static void FindFallbackPosition(LinkedHashSet<Tile> fallbackTiles, TileDataMap tileData, Region region)
    {
        // Fallback time. Just pick the one with best score
        var fallbackPosition = fallbackTiles.MaxByOrNull(it => tileData[it].NotNull().StartScore);
        if (fallbackPosition != null)
        {
            SetRegionStart(region, fallbackPosition.Position, tileData);
            return;
        }

        // Something went extremely wrong and there is somehow no place to start. Spawn some land and start there
        var panicPosition = HexCoord.Of(KotlinMath.FloatToInt(region.Rect.X), KotlinMath.FloatToInt(region.Rect.Y));
        var panicTerrain = region.TileMap.Ruleset.NotNull().Terrains.Values.First(it => it.Type == TerrainType.Land).Name;
        region.TileMap[panicPosition].BaseTerrain = panicTerrain;
        region.TileMap[panicPosition].SetTerrainFeatures([]);
        SetRegionStart(region, panicPosition, tileData);
    }

    /// <returns>a scaled according to <paramref name="proportion"/> Rectangle centered over <paramref name="originalRect"/></returns>
    private static Rectangle GetCentralRectangle(Rectangle originalRect, float proportion)
    {
        var width = originalRect.Width * proportion;
        var height = originalRect.Height * proportion;
        var x = originalRect.X + ((originalRect.Width - width) / 2);
        var y = originalRect.Y + ((originalRect.Height - height) / 2);

        // round values
        return new Rectangle(
            KotlinMath.RoundToInt(x),
            KotlinMath.RoundToInt(y),
            KotlinMath.RoundToInt(width),
            KotlinMath.RoundToInt(height));
    }

    /// <summary>
    /// Evaluates a tile for starting position, setting isGoodStart and startScore in
    /// MapGenTileData. Assumes that all tiles have corresponding MapGenTileData.
    /// </summary>
    private static void EvaluateTileForStart(Tile tile, TileDataMap tileData)
    {
        var localData = tileData[tile].NotNull();

        var totalFood = 0;
        var totalProd = 0;
        var totalGood = 0;
        var totalJunk = 0;
        var totalRivers = 0;
        var totalScore = 0;

        if (tile.IsAdjacentToCoast()) totalScore += 40;

        // Go through all rings
        for (var ring = 1; ring <= 3; ring++)
        {
            // Sum up the values for this ring
            tile.ForEachTileAtDistance(ring, outerTile =>
            {
                var outerTileData = tileData[outerTile].NotNull();
                if (outerTileData.IsJunk)
                    totalJunk++;
                else
                {
                    if (outerTileData.IsFood) totalFood++;
                    if (outerTileData.IsProd) totalProd++;
                    if (outerTileData.IsGood) totalGood++;
                    if (outerTile.IsAdjacentToRiver()) totalRivers++;
                }
            });
            // Check for minimum levels. We still keep on calculating final score in case of failure
            if (totalFood < MapRegions.MinimumFoodForRing[ring]
                || totalProd < MapRegions.MinimumProdForRing[ring]
                || totalGood < MapRegions.MinimumGoodForRing[ring])
            {
                localData.IsGoodStart = false;
            }

            // Ring-specific scoring
            switch (ring)
            {
                case 1:
                    {
                        var foodScore = MapRegions.FirstRingFoodScores[totalFood];
                        var prodScore = MapRegions.FirstRingProdScores[totalProd];
                        totalScore += foodScore + prodScore + totalRivers + (totalGood * 2) - (totalJunk * 3);
                        break;
                    }
                case 2:
                    {
                        var foodScore = totalFood > 10
                            ? MapRegions.SecondRingFoodScores[^1]
                            : MapRegions.SecondRingFoodScores[totalFood];
                        var effectiveTotalProd = totalProd >= totalFood * 2
                            ? totalProd
                            : (totalFood + 1) / 2; // Can't use all that production without food
                        var prodScore = effectiveTotalProd > 5
                            ? MapRegions.SecondRingProdScores[^1]
                            : MapRegions.SecondRingProdScores[effectiveTotalProd];
                        totalScore += foodScore + prodScore + totalRivers + (totalGood * 2) - (totalJunk * 3);
                        break;
                    }
                default:
                    totalScore += totalFood + totalProd + totalGood + totalRivers - (totalJunk * 2);
                    break;
            }
        }
        // Too much junk?
        if (totalJunk > MapRegions.MaximumJunk)
        {
            localData.IsGoodStart = false;
        }

        // Finally check if this is near another start
        if (localData.CloseStartPenalty > 0)
        {
            localData.IsGoodStart = false;
            totalScore -= (totalScore * localData.CloseStartPenalty) / 100;
        }
        localData.StartScore = totalScore;
    }

    private static void SetRegionStart(Region region, HexCoord position, TileDataMap tileData)
    {
        region.StartPosition = position;

        foreach (var (ring, penalty) in MapRegions.CloseStartPenaltyForRing)
        {
            region.TileMap[position].ForEachTileAtDistance(ring, outerTile =>
            {
                tileData[outerTile].NotNull().AddCloseStartPenalty(penalty);
            });
        }
    }
}
