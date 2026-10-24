using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Logic.Map;
using Unciv.Logic.Map.MapUnits;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using Unciv.Utils;
using AutomationObject = Unciv.Logic.Automation.Automation;
using CityObject = Unciv.Logic.City.City;
using CivilizationClass = Unciv.Logic.Civilization.Civilization;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Logic.Automation.Unit;

// Ported from core/src/com/unciv/logic/automation/unit/CityLocationTileRanker.kt (T087). Purity annotations dropped (T027 policy).
// Kotlin HashMap<Tile, Float> (identity hash, so no reproducible JVM order): LinkedHashMap, insertion order. This matters for the
// `baseTileMap.values.average()` below, whose Double sum depends on the order. The tile rank map is the ranker's output.
// DebugUtils.SHOW_SETTLER_SCORES / SETTLER_SCORES are the game's EngineOptions (T048).

public static class CityLocationTileRanker
{
    public class BestTilesToFoundCity
    {
        public LinkedHashMap<TileClass, float> TileRankMap { get; set; } = new();
        public TileClass? BestTile { get; set; }
        public float BestTileRank { get; set; }
    }

    /// <summary>Returns a hashmap of tiles to their ranking plus the a the highest value tile and its value</summary>
    public static BestTilesToFoundCity GetBestTilesToFoundCity(MapUnit unit, int? distanceToSearch, float minimumValue)
    {
        const float distanceModifier = 3f; // percentage penalty per aerial distance from unit (Settler)
        int range;
        if (distanceToSearch != null) range = distanceToSearch.Value;
        else
        {
            var distanceFromHome = unit.Civ.Cities.Count == 0 ? 0
                : unit.Civ.Cities.Min(it => it.GetCenterTile().AerialDistanceTo(unit.GetTile()));
            range = Math.Clamp(8 - distanceFromHome, 1, 5); // Restrict vision when far from home to avoid death marches
        }
        var nearbyCities = unit.Civ.GameInfo.GetCities()
            .Where(it => it.GetCenterTile().AerialDistanceTo(unit.GetTile()) <= 7 + range)
            .ToList();

        var uniques = unit.GetMatchingUniques(UniqueType.FoundCity).Concat(unit.GetMatchingUniques(UniqueType.FoundPuppetCity)).ToList();
        var possibleCityLocations = unit.GetTile().GetTilesInDistance(range)
            // Filter out tiles that we can't actually found on
            .Where(tile => uniques.Any(it => it.ConditionalsApply(new GameContext(unit: unit, tile: tile))))
            .Where(it => unit.Civ.HasExplored(it))
            .Where(it => CanSettleTile(it, unit.Civ, nearbyCities) && (unit.GetTile() == it || unit.Movement.CanMoveTo(it)));
        var bestTilesToFoundCity = new BestTilesToFoundCity();
        var baseTileMap = new LinkedHashMap<TileClass, float>();

        // Assume unexplored tiles are worth the average of the explored tiles around us
        var throwawayLuxuries = new HashSet<TileResource>();
        // onCoast doesn't matter here, it does not change baseTileMap
        unit.GetTile().ForEachTileInDistance(range + 2, tile =>
        {
            if (unit.Civ.HasExplored(tile)) RankTile(tile, unit.Civ, false, throwawayLuxuries, baseTileMap, 0f);
        });
        var unexploredTilePrior = baseTileMap.Count == 0 ? 0f : (float)baseTileMap.Values.Average(it => (double)it);

        var possibleTileLocationsWithRank = possibleCityLocations
            .Select(it =>
            {
                var tileValue = RankTileToSettle(it, unit.Civ, nearbyCities, baseTileMap, unexploredTilePrior);
                var distanceScore = Math.Clamp(unit.CurrentTile.AerialDistanceTo(it) * distanceModifier, 0f, 99f);
                tileValue *= (100 - distanceScore) / 100;
                if (tileValue >= minimumValue)
                    bestTilesToFoundCity.TileRankMap.Put(it, tileValue);

                return (Tile: it, Value: tileValue);
            }).Where(it => it.Value >= minimumValue)
            .OrderByDescending(it => it.Value) // stable, like Kotlin sortedByDescending
            .ToList();

        var bestReachableTile = possibleTileLocationsWithRank.Where(it => unit.Movement.CanReach(it.Tile)).Select(it => ((TileClass Tile, float Value)?)it).FirstOrDefault();
        if (bestReachableTile != null)
        {
            bestTilesToFoundCity.BestTile = bestReachableTile.Value.Tile;
            bestTilesToFoundCity.BestTileRank = bestReachableTile.Value.Value;
        }

        var engineOptions = unit.Civ.GameInfo.EngineOptions;
        if (engineOptions.ShowSettlerScores)
        {
            var scores = new Dictionary<HexCoord, float>();
            foreach (var entry in bestTilesToFoundCity.TileRankMap) scores[entry.Key.Position] = entry.Value;
            engineOptions.SettlerScores = scores;
        }

        return bestTilesToFoundCity;
    }

    private static bool CanSettleTile(TileClass tile, CivilizationClass civ, List<CityObject> nearbyCities)
    {
        var modConstants = civ.GameInfo.Ruleset.ModOptions.Constants;
        if (!tile.IsLand || tile.IsImpassible()) return false;
        if (tile.GetOwner() != null && tile.GetOwner() != civ) return false;
        foreach (var city in nearbyCities)
        {
            var distance = city.GetCenterTile().AerialDistanceTo(tile);
            // todo: AgreedToNotSettleNearUs is hardcoded for now but it may be better to softcode it below in getDistanceToCityModifier
            if (distance <= 6 && civ.Knows(city.Civ)
                && !civ.IsAtWarWith(city.Civ)
                // If the CITY OWNER knows that the UNIT OWNER agreed not to settle near them
                && city.Civ.GetDiplomacyManager(civ).NotNull()
                    .HasFlag(DiplomacyFlags.AgreedToNotSettleNearUs))
                return false;
            if (tile.GetContinent() == city.GetCenterTile().GetContinent())
            {
                if (distance <= modConstants.MinimalCityDistance) return false;
            }
            else
            {
                if (distance <= modConstants.MinimalCityDistanceOnDifferentContinents) return false;
            }
        }
        return true;
    }

    private static float RankTileToSettle(TileClass newCityTile, CivilizationClass civ, List<CityObject> nearbyCities,
        LinkedHashMap<TileClass, float> baseTileMap, float unexploredTilePrior = 0f)
    {
        var tileValue = 0f;
        tileValue += GetDistanceToCityModifier(newCityTile, nearbyCities, civ);

        var onCoast = newCityTile.IsAdjacentToCoast();
        var onHill = newCityTile.IsHill();
        var isNextToMountain = newCityTile.IsAdjacentTo(Constants.Mountain);
        var unImprovable = newCityTile.GetTerrainMatchingUniques(UniqueType.RestrictedBuildableImprovements)
            .Any(it => it.Params[0] == Constants.AllRoad);

        // Only count a luxury resource that we don't have yet as unique once
        var newUniqueLuxuryResources = new HashSet<TileResource>();

        if (onCoast) tileValue += 3;
        // Hills are free production and defence
        if (onHill) tileValue += 14;
        // Observatories are good, but current implementation not mod-friendly
        if (isNextToMountain) tileValue += 5;
        // This bonus for settling on river is a bit outsized for the importance, but otherwise they have a habit of settling 1 tile away
        if (newCityTile.IsAdjacentToRiver()) tileValue += 20;
        // We want to found the city on an oasis because it can't be improved otherwise
        if (unImprovable) tileValue += 3;

        var resource = newCityTile.TileResource; // smart-cast to non-null by canSeeResource in Kotlin
        if (civ.CanSeeResource(resource))
        {
            tileValue -= 4;
            // Settling on bonus resources tends to waste a food
            if (resource.NotNull().ResourceType == ResourceType.Bonus) tileValue -= 8;
            // Build on jungle luxuries for tempo
            if (resource.ResourceType == ResourceType.Luxury &&
                ((IHasUniques)newCityTile.LastTerrain).HasUnique(UniqueType.Vegetation) &&
                !((IHasUniques)newCityTile.LastTerrain).HasUnique(UniqueType.ProductionBonusWhenRemoved)
               ) tileValue += 10;
        }

        var tiles = 0;
        for (var i = 0; i <= 2; i++)
        {
            //Ideally, we shouldn't really count the center tile, as it's converted into 1 production 2 food anyways with special cases treated above, but doing so can lead to AI moving settler back and forth until forever
            var ring = i;
            newCityTile.ForEachTileAtDistance(ring, nearbyTile =>
            {
                tiles++;
                tileValue += RankTile(nearbyTile, civ, onCoast, newUniqueLuxuryResources, baseTileMap, unexploredTilePrior) * (3f / (ring + 1));
                //Tiles close to the city can be worked more quickly, and thus should gain higher weight.
            });
        }

        // Placing cities on the edge of the map is bad, we can't even build improvements on them!
        tileValue -= (HexMath.GetNumberOfTilesInHexagon(2) - tiles) * 2.4f;
        return tileValue;
    }

    private static float GetDistanceToCityModifier(TileClass newCityTile, List<CityObject> nearbyCities, CivilizationClass civ)
    {
        var modifier = 0f;
        foreach (var city in nearbyCities)
        {
            var distanceToCity = newCityTile.AerialDistanceTo(city.GetCenterTile());
            var distanceToCityModifier = distanceToCity switch
            {
                // NOTE: the line it.getCenterTile().aerialDistanceTo(unit.getTile()) <= X + range
                // above MUST have the constant X that is added to the range be higher or equal to the highest distance here + 1
                // If it is not higher the settler may get stuck when it ranks the same tile differently
                // as it moves away from the city and doesn't include it in the calculation
                // and values it higher than when it moves closer to the city
                7 => 2f,
                6 => 4f,
                5 => 8f, // Settling further away sacrifices tempo
                4 => 6f,
                3 => -25f,
                < 3 => -30f, // Even if it is a mod that lets us settle closer, lets still not do it
                _ => 0f
            };
            // We want a defensive ring around our capital
            if (city.Civ == civ)
            {
                distanceToCityModifier *= city.IsCapital() ? 2 : 1;
                modifier += distanceToCityModifier;
            }
        }
        return modifier;
    }

    private static float RankTile(TileClass rankTile, CivilizationClass civ, bool onCoast, HashSet<TileResource> newUniqueLuxuryResources,
        LinkedHashMap<TileClass, float> baseTileMap, float unexploredTilePrior = 0f)
    {
        // Unexplored tiles are unknown - estimate them as an average tile instead of reading the actual map
        if (!civ.HasExplored(rankTile)) return unexploredTilePrior;
        if (rankTile.GetCity() != null) return -1f;
        var locationSpecificTileValue = 0f;
        // Don't settle near but not on the coast
        if (rankTile.IsWater && !onCoast) locationSpecificTileValue -= 1;
        // Check if there are any new unique luxury resources
        var resource = rankTile.TileResource;
        if (civ.CanSeeResource(resource) &&
            resource.NotNull().ResourceType == ResourceType.Luxury &&
            !civ.HasResource(resource.NotNull()) &&
            !newUniqueLuxuryResources.Contains(resource.NotNull()))
        {
            locationSpecificTileValue += 10;
            newUniqueLuxuryResources.Add(resource.NotNull());
        }

        // Check if everything else has been calculated, if so return it
        if (baseTileMap.TryGetValue(rankTile, out var cachedValue)) return locationSpecificTileValue + cachedValue;
        if (rankTile.GetOwner() != null && rankTile.GetOwner() != civ) return 0f;

        var rankTileValue = AutomationObject.RankStatsValue(rankTile.Stats.GetTileStats(null, civ), AutomationObject.GetCivView(civ));

        if (civ.CanSeeResource(resource))
        {
            rankTileValue += resource.NotNull().ResourceType switch
            {
                ResourceType.Bonus => 1f,
                ResourceType.Strategic => 2f,
                ResourceType.Luxury => 10f, //very important for humans who might want to conquer the AI
                _ => throw new InvalidOperationException()
            };
        }
        if (rankTile.TerrainHasUnique(UniqueType.FreshWater)) rankTileValue += 0.5f;
        //Taking into account freshwater farm food, maybe less important in baseruleset mods
        if (rankTile.TerrainFeatures.Count != 0 && ((IHasUniques)rankTile.LastTerrain).HasUnique(UniqueType.ProductionBonusWhenRemoved)) rankTileValue += 0.7f;
        //Taking into account yields from forest chopping

        if (rankTile.IsNaturalWonder()) rankTileValue += 4;

        baseTileMap.Put(rankTile, rankTileValue);

        return rankTileValue + locationSpecificTileValue;
    }
}
