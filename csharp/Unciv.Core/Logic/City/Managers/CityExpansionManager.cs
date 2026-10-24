using System.Text.Json.Serialization;
using Unciv.Json;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using Unciv.Logic.Map.MapUnits;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using Unciv.UI.Components.Extensions;
using Unciv.Utils;
using CityObject = Unciv.Logic.City.City;
using NotificationCategory = Unciv.Logic.Civilization.Notification.NotificationCategory;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Logic.City.Managers;

// Ported from core/src/com/unciv/logic/city/managers/CityExpansionManager.kt (T061).
//
// Stripped: the purity annotations. Kotlin's local exception classes in buyTile are private nested classes with the same names.
// Tile.GetCity() is typed City (retyped by T115); getOwner() is typed by the Tile seam (IGameContextCivilization); City, Civilization
// and MapUnit instances are compared by reference like Kotlin (none of the three has equals).

public class CityExpansionManager : IsPartOfGameInfoSerialization
{
    private CityObject? city;
    /// <summary>Kotlin <c>@Transient lateinit var</c>, set by <c>City.setTransients</c>: reading before assignment throws.</summary>
    [JsonIgnore]
    public CityObject City
    {
        get => city ?? throw new InvalidOperationException("lateinit property city has not been initialized");
        set => city = value;
    }

    [JsonPropertyName("cultureStored")]
    public int CultureStored { get; set; }

    public CityExpansionManager Clone()
    {
        var toReturn = new CityExpansionManager
        {
            CultureStored = CultureStored,
        };
        return toReturn;
    }

    public int TilesClaimed()
    {
        var tilesAroundCity = City.GetCenterTile().Neighbors
            .Select(it => it.Position).ToList();
        return City.Tiles.Count(it => it != City.Location && !tilesAroundCity.Contains(it));
    }

    // This one has conflicting sources -
    // http://civilization.wikia.com/wiki/Mathematics_of_Civilization_V says it's 20+(10(t-1))^1.1
    // https://www.reddit.com/r/civ/comments/58rxkk/how_in_gods_name_do_borders_expand_in_civ_vi/ has it
    //   (per game XML files) at 6*(t+0.4813)^1.3
    // The second seems to be more based, so I'll go with that
    // -- Note (added later) that this last link is specific to civ VI and not civ V
    public int GetCultureToNextTile()
    {
        double cultureToNextTile = 6 * Math.Pow(Math.Max(0, TilesClaimed()) + 1.4813, 1.3);

        cultureToNextTile *= City.Civ.GameInfo.Speed.CultureCostModifier;

        if (City.Civ.IsCityState)
            cultureToNextTile *= 1.5f;   // City states grow slower, perhaps 150% cost?

        City.ForEachMatchingUnique(UniqueType.BorderGrowthPercentage, unique =>
        {
            if (City.MatchesFilter(unique.Params[1]))
                cultureToNextTile *= unique.Params[0].ToPercent();
        });

        return KotlinMath.RoundToInt(cultureToNextTile);
    }

    public bool CanBuyTile(TileClass tile)
    {
        if (City.IsPuppet || City.IsBeingRazed) return false;
        if (tile.GetOwner() != null) return false;
        if (City.IsInResistance()) return false;
        if (!City.TilesInRange.Contains(tile)) return false;
        return tile.Neighbors.Any(it => ReferenceEquals(it.GetCity(), City));
    }

    private sealed class TriedToBuyNonContiguousTileException(string msg) : Exception(msg);

    private sealed class NotEnoughGoldToBuyTileException(string msg) : Exception(msg);

    public void BuyTile(TileClass tile)
    {
        var goldCost = GetGoldCostOfTile(tile);

        if (!tile.Neighbors.Any(it => ReferenceEquals(it.GetCity(), City)))
            throw new TriedToBuyNonContiguousTileException($"{City} tried to buy {tile}, but it owns none of the neighbors");

        if (City.Civ.Gold < goldCost && !City.Civ.GameInfo.GameParameters.GodMode)
            throw new NotEnoughGoldToBuyTileException($"{City} tried to buy {tile}, but lacks gold (cost {goldCost}, has {City.Civ.Gold})");

        City.Civ.AddGold(-goldCost);
        TakeOwnership(tile);

        // Reapply worked tiles optimization (aka CityFocus) - doing it here means AI profits too
        City.ReassignPopulationDeferred();
    }

    public int GetGoldCostOfTile(TileClass tile, int extraTiles = 0)
    {
        const int baseCost = 50;
        var distanceFromCenter = tile.AerialDistanceTo(City.GetCenterTile());
        double cost = baseCost * (distanceFromCenter - 1) + (TilesClaimed() + extraTiles) * 5.0;

        cost *= City.Civ.GameInfo.Speed.GoldCostModifier;

        City.ForEachMatchingUnique(UniqueType.TileCostPercentage, unique =>
        {
            if (City.MatchesFilter(unique.Params[1]))
                cost *= unique.Params[0].ToPercent();
        });

        return KotlinMath.RoundToInt(cost);
    }

    public IEnumerable<TileClass> GetChoosableTiles() => City.GetCenterTile().GetTilesInDistance(City.GetExpandRange())
        .Where(it => it.GetOwner() == null);

    public TileClass? ChooseNewTileToOwn()
    {
        // Technically, in the original a random tile with the lowest score was selected
        // However, doing this requires either caching it, which is way more work,
        // or selecting all possible tiles and only choosing one when the border expands.
        // But since the order in which tiles are selected in distance is kinda random anyways,
        // this is fine.
        TileClass? best = null;
        var bestRank = 0;
        foreach (var tile in GetChoosableTiles())
        {
            var rank = RankTileForExpansion(tile);
            if (best == null || rank < bestRank)
            {
                best = tile;
                bestRank = rank;
            }
        }
        return best;
    }

    // Ranks a tile for the expansion algorithm of cities
    public int RankTileForExpansion(TileClass tile)
    {
        // https://github.com/Gedemon/Civ5-DLL/blob/aa29e80751f541ae04858b6d2a2c7dcca454201e/CvGameCoreDLL_Expansion1/CvCity.cpp#L10301
        // Apparently this is not the full calculation. The exact tiles are also
        // dependent on which tiles are between the chosen tile and the city center
        // Exact details are not implemented, but can be found in CvAStar.cpp:2119,
        // function `InfluenceCost()`.
        // Implementing these will require an additional variable for each terrainType
        var distance = tile.AerialDistanceTo(City.GetCenterTile());

        // Higher score means tile is less likely to be picked
        var score = distance * 100;

        // Resources are good: less points
        var resource = tile.TileResource;
        if (City.Civ.CanSeeResource(resource))
        {
            if (resource.NotNull().ResourceType != ResourceType.Bonus) score -= 105;
            else if (distance <= City.GetWorkRange()) score -= 104;
        }
        else
        {
            // Water tiles without resources aren't great unless they're Atolls
            if (tile.IsWater) score += 3;
            // Can't work it anyways
            if (distance > City.GetWorkRange()) score += 100;
        }

        if (tile.NaturalWonder != null) score -= 105;

        // Straight up take the sum of all yields
        score -= KotlinMath.FloatToInt(tile.Stats.GetTileStats(City, City.Civ).Sum());

        // Check if we get access to better tiles from this tile
        var adjacentNaturalWonder = false;
        var isContested = false;

        foreach (var adjacentTile in tile.Neighbors.Where(it => City.Civ.HasExplored(it) && !ReferenceEquals(it.GetOwner(), City.Civ)).ToList())
        {
            // the neighbor tile is owned by another civ
            if (adjacentTile.GetOwner() != null)
            {
                isContested = true;
                continue;
            }
            // the neighbor tile is not owned by anyone
            var adjacentDistance = City.GetCenterTile().AerialDistanceTo(adjacentTile);
            var adjacentResource = adjacentTile.TileResource;
            if (City.Civ.CanSeeResource(adjacentResource) &&
                (adjacentDistance <= City.GetWorkRange() || adjacentResource.NotNull().ResourceType != ResourceType.Bonus)
            ) score -= 1;
            if (adjacentTile.NaturalWonder != null)
            {
                if (adjacentDistance <= City.GetWorkRange()) adjacentNaturalWonder = true;
                score -= 1;
            }
        }
        if (adjacentNaturalWonder) score -= 1;
        if (isContested) score -= 1;

        // Tiles not adjacent to owned land are very hard to acquire
        if (!tile.Neighbors.Any(it => it.GetCity() != null && it.GetCity().NotNull().Id == City.Id))
            score += 1000;

        return score;
    }

    #region state-changing functions
    public void Reset()
    {
        foreach (var tile in City.GetTiles())
            RelinquishOwnership(tile);

        // The only way to create a city inside an owned tile is if it's in your territory
        // In this case, if you don't assign control of the central tile to the city,
        // It becomes an invisible city and weird shit starts happening
        TakeOwnership(City.GetCenterTile());

        // can't take ownership of owned tiles (by other cities)
        City.GetCenterTile().ForEachTileInDistance(1, it => it.GetCity() == null, TakeOwnership);
    }

    private HexCoord? AddNewTileWithCulture()
    {
        var chosenTile = ChooseNewTileToOwn();
        if (chosenTile != null)
        {
            CultureStored -= GetCultureToNextTile();
            TakeOwnership(chosenTile);
            return chosenTile.Position;
        }
        return null;
    }

    /// <summary>
    /// Removes one tile from this city's owned tiles, unconditionally, and updates dependent
    /// things like worked tiles, locked tiles, and stats.
    /// </summary>
    /// <param name="tile">The tile to relinquish</param>
    public virtual void RelinquishOwnership(TileClass tile)
    {
        City.Tiles = WithoutItem(City.Tiles, tile.Position);
        foreach (var civCity in City.Civ.Cities)
        {
            if (civCity.IsWorked(tile))
            {
                civCity.StopWorkingTile(tile);
                civCity.Population.AutoAssignPopulation();
            }
        }

        tile.ImprovementFunctions.RemoveCreatesOneImprovementMarker();

        tile.SetOwningCity(null);

        City.Civ.Cache.UpdateOurTiles();
        City.CityStats.Update();

        tile.History.RecordRelinquishOwnership(tile);
    }

    /// <summary>
    /// Takes one tile into possession of this city, either unowned or owned by any other city.
    ///
    /// Also manages consequences like auto population reassign, stats, and displacing units
    /// that are no longer allowed on that tile.
    /// </summary>
    /// <param name="tile">The tile to take over</param>
    public void TakeOwnership(TileClass tile)
    {
        if (tile.IsCityCenter()) throw new InvalidOperationException("Trying to found a city in a tile that already has one");
        if (tile.GetCity() != null)
            ((CityObject)tile.GetCity().NotNull()).Expansion.RelinquishOwnership(tile);

        if (tile.IsBarbarianEncampment())
            tile.RemoveImprovement();

        City.Tiles = WithItem(City.Tiles, tile.Position);
        tile.SetOwningCity(City);
        City.Population.AutoAssignPopulation();
        City.Civ.Cache.UpdateOurTiles();
        City.CityStats.Update();

        foreach (var gameContextUnit in tile.GetUnits().ToList()) // toListed because we're modifying
        {
            var unit = (MapUnit)gameContextUnit;
            if (!unit.Civ.DiplomacyFunctions.CanPassThroughTiles(City.Civ))
                unit.Movement.TeleportToClosestMoveableTile();
            else if (ReferenceEquals(unit.Civ, City.Civ) && unit.IsSleeping())
            {
                // If the unit is sleeping and is a worker, it might want to build on this tile
                // So lets try to wake it up for the player to notice it
                if (unit.Cache.HasUniqueToBuildImprovements || unit.Cache.HasUniqueToCreateWaterImprovements)
                {
                    unit.Due = true;
                    unit.Action = null;
                }
            }
        }

        tile.History.RecordTakeOwnership(tile);
    }

    public void NextTurn(float culture)
    {
        CultureStored += KotlinMath.FloatToInt(culture);
        if (CultureStored >= GetCultureToNextTile())
        {
            var location = AddNewTileWithCulture();
            if (location != null)
            {
                var locations = LocationAction.Of(location.Value, City.Location);
                City.Civ.AddNotification($"[{City.Name}] has expanded its borders!", locations,
                    NotificationCategory.Cities, NotificationIcon.Culture);
            }
        }
    }

    public virtual void SetTransients()
    {
        var tiles = City.GetTiles();
        foreach (var tile in tiles)
            tile.SetOwningCity(City);
    }
    #endregion

    // Kotlin Set.withItem / withoutItem for HashSet: a copy (java.util.HashSet(Collection)) with the change; the original is not changed.
    private static JavaHashSet<T> WithItem<T>(JavaHashSet<T> set, T item) where T : notnull
    {
        var newSet = new JavaHashSet<T>(set);
        newSet.Add(item);
        return newSet;
    }

    private static JavaHashSet<T> WithoutItem<T>(JavaHashSet<T> set, T item) where T : notnull
    {
        var newSet = new JavaHashSet<T>(set);
        newSet.Remove(item);
        return newSet;
    }
}
