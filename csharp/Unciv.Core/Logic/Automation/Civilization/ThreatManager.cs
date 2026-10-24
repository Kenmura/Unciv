using Unciv.UI.Screens.VictoryScreen;
using Unciv.Utils;
using Unciv.View;

namespace Unciv.Logic.Automation.Civilization;

// Ported from core/src/com/unciv/logic/automation/civilization/ThreatManager.kt (T087). Purity annotations dropped (T027 policy).
// getDangerousTiles returns a Kotlin HashSet of TileViews, whose hashCode is an identity hash (no stable JVM order): LinkedHashSet, insertion order.

/// <summary>
/// Handles optimised operations related to finding threats or allies in an area.
/// Cached results live in the <see cref="AutomationCache"/> that is passed in.
/// </summary>
public static class ThreatManager
{
    /// <summary>
    /// Gets the distance to the closest visible enemy unit or city.
    /// The result value is cached and since it is called each turn in NextTurnAutomation.getUnitPriority
    /// each subsequent calls are likely to be free.
    /// </summary>
    public static int GetDistanceToClosestEnemyUnit(CivView civ, AutomationCache cache, TileView tile, int maxDist, bool takeLargerValues = true)
    {
        cache.DistanceToClosestEnemyTiles.TryGetValue(tile, out var tileData);
        // Needs to be a high value, but not the max value so we can still add to it. Example: nextTurnAutomation sorting
        var notFoundDistance = takeLargerValues ? 500000 : maxDist;
        var minDistanceToSearch = 1;
        // Look if we can return the cache or if we can reduce our search
        if (tileData != null)
        {
            var tilesWithEnemies = tileData.TilesWithEnemies;
            // Check the tiles where we have previously found an enemy, if so it must be the closest
            while (tilesWithEnemies.Count != 0)
            {
                var enemyTile = tilesWithEnemies[0];
                if (DoesTileHaveMilitaryEnemy(civ, enemyTile.Tile))
                    return takeLargerValues ? enemyTile.Distance : Math.Min(enemyTile.Distance, maxDist);
                // This tile is no longer valid
                tilesWithEnemies.RemoveAt(0);
            }

            if (tileData.DistanceSearched > maxDist)
            {
                // We have already searched past the range we want to search and haven't found any enemies
                return takeLargerValues ? notFoundDistance : maxDist;
            }

            // Only search the tiles that we haven't searched yet
            minDistanceToSearch = Math.Max(tileData.DistanceSearched + 1, 1);
        }

        if (tileData != null && tileData.TilesWithEnemies.Count != 0)
            throw new InvalidOperationException("There must be no elements in tile.data.tilesWithEnemies at this point");
        var tilesWithEnemyAtDistance = new List<(TileView Tile, int Distance)>();
        // Search for nearby enemies and store the results
        for (var i = minDistanceToSearch; i <= maxDist; i++)
        {
            foreach (var searchTile in tile.GetExploredTilesAtDistance(i))
            {
                if (DoesTileHaveMilitaryEnemy(civ, searchTile))
                    tilesWithEnemyAtDistance.Add((searchTile, i));
            }
            if (tilesWithEnemyAtDistance.Count != 0)
            {
                cache.DistanceToClosestEnemyTiles[tile] = new AutomationCache.ClosestEnemyTileData(i, tilesWithEnemyAtDistance);
                return i;
            }
        }
        cache.DistanceToClosestEnemyTiles[tile] = new AutomationCache.ClosestEnemyTileData(maxDist, []);
        return notFoundDistance;
    }

    /// <summary>
    /// Returns all tiles with enemy units on them in distance.
    /// Every tile is guaranteed to have an enemy.
    /// May be quicker than a manual search because of caching.
    /// Also ends up calculating and caching <see cref="GetDistanceToClosestEnemyUnit"/>.
    /// </summary>
    public static List<TileView> GetTilesWithEnemyUnitsInDistance(CivView civ, AutomationCache cache, TileView tile, int maxDist)
    {
        cache.DistanceToClosestEnemyTiles.TryGetValue(tile, out var tileData);

        // The list of tiles that we will return
        var tilesWithEnemies = new List<TileView>();
        // The list of tiles with distance that will be stored in cache.distanceToClosestEnemyTiles
        var tileDataTilesWithEnemies = tileData?.TilesWithEnemies ?? [];

        if (tileData != null)
        {
            // Add all tiles that we have previously found (Kotlin listIterator with remove())
            var index = 0;
            while (index < tileDataTilesWithEnemies.Count)
            {
                var tileWithDistance = tileDataTilesWithEnemies[index];
                // Check if the next tile is out of our search range, if so lets stop here
                if (tileWithDistance.Distance > maxDist) return tilesWithEnemies;
                // Check if the threat on the tile is still present
                if (DoesTileHaveMilitaryEnemy(civ, tileWithDistance.Tile))
                {
                    tilesWithEnemies.Add(tileWithDistance.Tile);
                    index++;
                }
                else tileDataTilesWithEnemies.RemoveAt(index);
            }
        }

        // We don't need to search for anything more if we have previously searched past maxDist
        if (tileData != null && maxDist <= tileData.DistanceSearched)
            return tilesWithEnemies;

        // Search all tiles that haven't been searched yet up until madDist
        var minDistanceToSearch = (tileData != null ? Math.Max(tileData.DistanceSearched, 0) : 0) + 1;

        for (var i = minDistanceToSearch; i <= maxDist; i++)
        {
            foreach (var searchTile in tile.GetExploredTilesAtDistance(i))
            {
                if (DoesTileHaveMilitaryEnemy(civ, searchTile))
                {
                    tilesWithEnemies.Add(searchTile);
                    tileDataTilesWithEnemies.Add((searchTile, i));
                }
            }
        }
        if (tileData != null)
            tileData.DistanceSearched = Math.Max(tileData.DistanceSearched, maxDist);
        else
        {
            // Cache our results for later
            cache.DistanceToClosestEnemyTiles[tile] = new AutomationCache.ClosestEnemyTileData(maxDist, tileDataTilesWithEnemies);
        }
        return tilesWithEnemies;
    }

    /// <summary>Returns all enemy military units on tiles</summary>
    public static List<ForeignMapUnitView> GetEnemyUnitsOnTiles(CivView civ, IEnumerable<TileView> tilesWithEnemyUnitsInDistance) =>
        tilesWithEnemyUnitsInDistance
            .SelectMany(enemyTile => enemyTile.GetVisibleUnits().Where(it => it.IsMilitary() && civ.IsAtWarWith(it.Civ())))
            .ToList();

    public static LinkedHashSet<TileView> GetDangerousTiles(AutomationCache cache, MapUnitView unitView, int distance = 3)
    {
        var civ = unitView.Civ();
        var tilesWithEnemyUnits = GetTilesWithEnemyUnitsInDistance(civ, cache, unitView.GetTile(), distance);
        var nearbyRangedEnemyUnits = GetEnemyUnitsOnTiles(civ, tilesWithEnemyUnits);

        var tilesInRangeOfAttack = nearbyRangedEnemyUnits
            .SelectMany(it => it.GetTile().GetExploredTilesInDistance(it.GetRange()));

        var tilesWithinBombardmentRange = tilesWithEnemyUnits
            .Where(it => it.IsCityCenter() && it.GetCity().NotNull().OwningCiv().IsAtWarWith(civ))
            .SelectMany(it => it.GetExploredTilesInDistance(it.GetCity().NotNull().GetBombardRange()));

        var tilesWithTerrainDamage = unitView.GetTile().GetExploredTilesInDistance(distance)
            .Where(it => unitView.GetDamageFromTerrain(it) > 0);

        return new LinkedHashSet<TileView>(tilesInRangeOfAttack.Concat(tilesWithinBombardmentRange).Concat(tilesWithTerrainDamage));
    }

    /// <summary>Returns true if the tile has a visible enemy, otherwise returns false.</summary>
    public static bool DoesTileHaveMilitaryEnemy(CivView civ, TileView tile)
    {
        if (!tile.IsExplored()) return false;
        if (tile.IsCityCenter() && tile.GetCity().NotNull().OwningCiv().IsAtWarWith(civ)) return true;
        if (!tile.IsVisible()) return false;
        return tile.GetVisibleUnits().Any(it => it.IsMilitary() && it.Civ().IsAtWarWith(civ));
    }

    /// <returns>a sequence of pairs of cities, the first city is our city and the second city is a nearby city that is not from our civ.</returns>
    public static IEnumerable<(ForeignCityView, ForeignCityView)> GetNeighboringCitiesOfOtherCivs(CivView civ) =>
        civ.Cities().SelectMany(ourCity => ourCity.GetNeighboringCities()
            .Where(it => !it.OwningCiv().Equals(civ))
            .Select(it => ((ForeignCityView)ourCity, it)))
            .ToList();

    public static LinkedHashSet<ForeignCivView> GetNeighboringCivilizations(CivView civ) => new(civ.Cities()
        .SelectMany(it => it.GetNeighboringCities())
        .Select(it => it.OwningCiv())
        .Where(it => !it.Equals(civ) && civ.Knows(it)));

    public static int GetCombinedForceOfWarringCivs(CivView civ) => civ.GetCivsAtWarWith().Sum(it => it.GetStatForRanking(RankingType.Force));
}
