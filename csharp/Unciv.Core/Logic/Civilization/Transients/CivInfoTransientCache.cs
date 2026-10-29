using Unciv.Logic.Map;
using Unciv.Logic.Map.Tiles;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Ruleset.Unit;
using Unciv.Utils;
using CityObject = Unciv.Logic.City.City;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.Logic.Civilization.Transients;

// Ported from core/src/com/unciv/logic/civilization/transients/CivInfoTransientCache.kt (T058).
//
// Deviations:
// - MapUnit is reached through the IMapUnitState seam (implemented by MapUnit, T065); the managers are the contracts of CivilizationContracts.cs.
// - Kotlin keeps the unique buildings/improvements/units, the viewable tiles and the "viewed civs" in java.util.HashSet/HashMap keyed by
//   identity-hashed objects, i.e. in an arbitrary order that reaches the order of notifications and natural-wonder discovery. They are
//   insertion-ordered here (LinkedHashSet, OrderedDictionary), which is one of the legal Kotlin orders and deterministic.
//   citiesConnectedToCapitalToMediums keeps the order CapitalConnectionsFinder found the cities in.
// - DebugUtils.VISIBLE_MAP is the game's EngineOptions.VisibleMap (T048).
// - The timeThis profiling wrappers are dropped.

/// <summary>CivInfo class was getting too crowded</summary>
public class CivInfoTransientCache(Civilization civInfo)
{
    public Civilization CivInfo { get; } = civInfo;

    public Dictionary<string, int> LastEraResourceUsedForBuilding { get; set; } = [];

    public Dictionary<string, int> LastEraResourceUsedForUnit { get; } = [];

    /// <summary>Easy way to look up a Civilization's unique units and buildings</summary>
    public LinkedHashSet<BaseUnit> UniqueUnits { get; } = new();

    public LinkedHashSet<TileImprovement> UniqueImprovements { get; } = new();

    public LinkedHashSet<Building> UniqueBuildings { get; } = new();

    /// <summary>Contains mapping of cities to travel mediums from ALL civilizations connected by trade routes to the capital</summary>
    public OrderedDictionary<CityObject, HashSet<CapitalConnectionMedium>> CitiesConnectedToCapitalToMediums { get; set; } = [];

    /// <summary>Ally/friend city-state bonus UniqueMaps</summary>
    public IReadOnlyList<UniqueMap> CityStateBonusUniqueMaps { get; private set; } = [];

    public void UpdateState()
    {
        CivInfo.State = new GameContext(CivInfo);
    }

    public void SetTransients()
    {
        var ruleset = CivInfo.GameInfo.Ruleset;

        var state = CivInfo.State;

        foreach (var building in ruleset.Buildings.Values)
        {
            if (building.UniqueTo != null && CivInfo.MatchesFilter(building.UniqueTo))
                UniqueBuildings.Add(building);
        }

        foreach (var improvement in ruleset.TileImprovements.Values)
            if (improvement.UniqueTo != null && CivInfo.MatchesFilter(improvement.UniqueTo))
                UniqueImprovements.Add(improvement);

        foreach (var unit in ruleset.Units.Values)
        {
            if (unit.UniqueTo != null && CivInfo.MatchesFilter(unit.UniqueTo))
                UniqueUnits.Add(unit);
        }

        var buildingsToRequiredResources = ruleset.Buildings.Values
            .Where(it => ReferenceEquals(CivInfo.GetEquivalentBuilding(it), it))
            .Select(it => (Key: it, Value: ((IConstruction)it).RequiredResources(state)))
            .ToList();

        var unitsToRequiredResources = ruleset.Units.Values
            .Where(it => ReferenceEquals(CivInfo.GetEquivalentUnit(it), it))
            .Select(it => (Key: it, Value: ((IConstruction)it).RequiredResources(state)))
            .ToList();

        foreach (var resource in ruleset.TileResources.Values.Where(it => it.ResourceType == ResourceType.Strategic).Select(it => it.Name))
        {
            var applicableBuildings = buildingsToRequiredResources.Where(it => it.Value.Contains(resource)).Select(it => it.Key).ToList();
            var applicableUnits = unitsToRequiredResources.Where(it => it.Value.Contains(resource)).Select(it => it.Key).ToList();

            int? lastEraForBuilding = applicableBuildings.Count == 0 ? null : applicableBuildings.Max(it => ((IHasUniques)it).Era(ruleset)?.EraNumber ?? 0);
            int? lastEraForUnit = applicableUnits.Count == 0 ? null : applicableUnits.Max(it => ((IHasUniques)it).Era(ruleset)?.EraNumber ?? 0);

            if (lastEraForBuilding != null)
                LastEraResourceUsedForBuilding[resource] = lastEraForBuilding.Value;
            if (lastEraForUnit != null)
                LastEraResourceUsedForUnit[resource] = lastEraForUnit.Value;
        }
    }

    public void UpdateSightAndResources()
    {
        UpdateViewableTiles();
        UpdateHasActiveEnemyMovementPenalty();
        UpdateCivResources();
    }

    // This is a big performance
    public void UpdateViewableTiles(HexCoord? explorerPosition = null)
    {
        SetNewViewableTiles();

        UpdateViewableInvisibleTiles();

        UpdateLastSeenImprovements();

        // updating the viewable tiles also affects the explored tiles, obviously.
        // So why don't we play switcharoo with the explored tiles as well?
        // Well, because it gets REALLY LARGE so it's a lot of memory space,
        // and we never actually iterate on the explored tiles (only check contains()),
        // so there's no fear of concurrency problems.
        foreach (var tile in CivInfo.ViewableTiles)
            tile.SetExplored(CivInfo, true, explorerPosition);

        var viewedCivs = new OrderedDictionary<Civilization, Tile>();
        foreach (var tile in CivInfo.ViewableTiles)
        {
            var tileOwner = (Civilization?)tile.GetOwner();
            if (tileOwner != null) viewedCivs[tileOwner] = tile;
            var unitOwner = (Civilization?)tile.GetFirstUnit()?.Civ;
            if (unitOwner != null) viewedCivs[unitOwner] = tile;
        }

        if (!CivInfo.IsBarbarian)
        {
            foreach (var (metCiv, metTile) in viewedCivs)
            {
                if (ReferenceEquals(metCiv, CivInfo) || metCiv.IsBarbarian || CivInfo.Diplomacy.ContainsKey(metCiv.CivId)) continue;
                CivInfo.DiplomacyFunctions.MakeCivilizationsMeet(metCiv);
                if (!CivInfo.IsSpectator())
                    CivInfo.AddNotification($"We have encountered [{metCiv.CivName}]!",
                        metTile.Position,
                        Notification.NotificationCategory.Diplomacy, metCiv.CivName,
                        NotificationIcon.Diplomacy);
                metCiv.AddNotification($"We have encountered [{CivInfo.CivName}]!",
                    metTile.Position,
                    Notification.NotificationCategory.Diplomacy, CivInfo.CivName,
                    NotificationIcon.Diplomacy);
            }

            DiscoverNaturalWonders();
        }
    }

    private void UpdateViewableInvisibleTiles()
    {
        var newViewableInvisibleTiles = new Dictionary<Tile, IReadOnlySet<string>>();
        foreach (var unit in CivInfo.Units.GetCivUnits())
        {
            var invisibleUnitUniques = unit.GetMatchingUniques(UniqueType.CanSeeInvisibleUnits);
            if (!invisibleUnitUniques.Any()) continue;
            var visibleUnitFilters = invisibleUnitUniques.Select(it => it.Params[0])
                .ToList(); // save this, it'll be seeing a lot of use
            foreach (var tile in unit.ViewableTiles)
            {
                if (!newViewableInvisibleTiles.TryGetValue(tile, out var filters))
                    newViewableInvisibleTiles[tile] = filters = new HashSet<string>();
                ((HashSet<string>)filters).UnionWith(visibleUnitFilters);
            }
        }

        CivInfo.ViewableInvisibleUnitsTiles = newViewableInvisibleTiles;
    }

    public IReadOnlySet<Tile> OurTilesAndNeighboringTiles { get; private set; } = new LinkedHashSet<Tile>();

    /// <summary>
    /// Our tiles update pretty infrequently - most 'viewable tile' changes are due to unit movements,
    /// which means we can store this separately and use it 'as is' so we don't need to find the neighboring tiles every time
    /// a unit moves
    /// </summary>
    public void UpdateOurTiles()
    {
        // our owned tiles, still distinct; now we got a mix of owned, unowned and competitor-owned tiles, and **duplicates**
        // but a set is just as good at making them distinct as any other operation
        var ourTiles = new LinkedHashSet<Tile>();
        foreach (var city in CivInfo.Cities)
            foreach (var tile in city.GetTiles())
            {
                ourTiles.Add(tile);
                foreach (var neighbor in tile.Neighbors) ourTiles.Add(neighbor);
            }
        OurTilesAndNeighboringTiles = ourTiles;

        UpdateViewableTiles();
        UpdateCivResources();
    }

    private void SetNewViewableTiles()
    {
        // while spectating (or defeated in singleplayer, which grants the same rights) all map is visible
        if (CivInfo.HasSpectatorVision() || CivInfo.GameInfo.EngineOptions.VisibleMap)
        {
            CivInfo.ViewableTiles = new LinkedHashSet<Tile>(CivInfo.GameInfo.TileMap.Values);
            return;
        }

        if (CivInfo.IsDefeated())
        {
            // Avoid meeting dead city states when entering a tile owned by their former ally (#9245)
            // In that case ourTilesAndNeighboringTiles and getCivUnits will be empty, but the for
            // loop getKnownCivs/getAllyCiv would add tiles.
            CivInfo.ViewableTiles = new LinkedHashSet<Tile>();
            return;
        }

        var newViewableTiles = new LinkedHashSet<Tile>(OurTilesAndNeighboringTiles);
        newViewableTiles.AddAll(CivInfo.Units.GetCivUnits().SelectMany(unit => unit.ViewableTiles.Where(it => !ReferenceEquals(it.GetOwner(), CivInfo))));

        foreach (var otherCiv in CivInfo.GetKnownCivs())
        {
            if (ReferenceEquals(otherCiv.AllyCiv, CivInfo) || ReferenceEquals(otherCiv, CivInfo.AllyCiv))
                newViewableTiles.AddAll(otherCiv.Cities.SelectMany(it => it.GetTiles()));
        }

        newViewableTiles.AddAll(CivInfo.EspionageManager.GetTilesVisibleViaSpies());

        CivInfo.ViewableTiles = newViewableTiles; // to avoid concurrent modification problems
    }

    private void UpdateLastSeenImprovements()
    {
        if (CivInfo.PlayerType == PlayerType.AI) return; // don't bother for AI, they don't really use the info anyway

        foreach (var tile in CivInfo.ViewableTiles)
            CivInfo.SetLastSeenImprovement(tile.Position, tile.Improvement);
    }

    /// <summary>Discovers Natural Wonders in all viewable tiles. Used by sight updates.</summary>
    public void DiscoverNaturalWonders() => DiscoverNaturalWonders(CivInfo.ViewableTiles);

    /// <summary>Discovers Natural Wonders in <paramref name="tiles"/>. Used by sight updates, "Reveals the entire map", and DevConsole.</summary>
    public void DiscoverNaturalWonders(IEnumerable<Tile> tiles)
    {
        var newlyViewedNaturalWonders = new LinkedHashSet<Tile>();
        foreach (var tile in tiles)
        {
            if (tile.NaturalWonder != null && !CivInfo.NaturalWonders.Contains(tile.NaturalWonder))
                newlyViewedNaturalWonders.Add(tile);
        }

        foreach (var tile in newlyViewedNaturalWonders)
        {
            // GBR could be discovered twice otherwise!
            if (CivInfo.NaturalWonders.Contains(tile.NaturalWonder.NotNull()))
                continue;
            CivInfo.NaturalWonders.Add(tile.NaturalWonder.NotNull());
            if (CivInfo.IsSpectator()) continue; // don't trigger anything

            CivInfo.AddNotification($"We have discovered [{tile.NaturalWonder}]!",
                tile.Position, Notification.NotificationCategory.General, "StatIcons/Happiness");

            var statsGained = new Models.StatTypes.Stats();

            var discoveredNaturalWonders = CivInfo.GameInfo.Civilizations
                .Where(it => !ReferenceEquals(it, CivInfo) && it.IsMajorCiv())
                .SelectMany(it => it.NaturalWonders)
                .ToList();
            if (tile.TerrainHasUnique(UniqueType.GrantsStatsToFirstToDiscover)
                && !discoveredNaturalWonders.Contains(tile.NaturalWonder.NotNull()))
            {
                foreach (var unique in tile.GetTerrainMatchingUniques(UniqueType.GrantsStatsToFirstToDiscover))
                    statsGained.Add(unique.Stats);
            }

            CivInfo.ForEachMatchingUnique(UniqueType.StatBonusWhenDiscoveringNaturalWonder, unique =>
            {
                var normalBonus = Models.StatTypes.Stats.Parse(unique.Params[0]);
                var firstDiscoveredBonus = Models.StatTypes.Stats.Parse(unique.Params[1]);

                if (discoveredNaturalWonders.Contains(tile.NaturalWonder.NotNull()))
                    statsGained.Add(normalBonus);
                else
                    statsGained.Add(firstDiscoveredBonus);
            });

            string? naturalWonder = null;

            if (!statsGained.IsEmpty())
                naturalWonder = tile.NaturalWonder.NotNull();

            if (!statsGained.IsEmpty() && naturalWonder != null)
            {
                CivInfo.AddStats(statsGained);
                CivInfo.AddNotification($"We have received [{statsGained}] for discovering [{naturalWonder}]",
                    Notification.NotificationCategory.General, statsGained.ToString());
            }

            CivInfo.ForEachTriggeredUnique(UniqueType.TriggerUponDiscoveringNaturalWonder,
                new GameContext(CivInfo, tile: tile), _ => true, false,
                unique => TriggerUnique(unique, tile));

            // G&K in particular; update the happiness counter in the top bar in the world screen
            CivInfo.UpdateStatsForNextTurn();
        }
    }

    private void TriggerUnique(UniqueObject unique, Tile tile) =>
        UniqueTriggerActivation.TriggerUnique(unique, CivInfo, tile: tile, triggerNotificationText: "due to discovering a Natural Wonder");

    public void UpdateHasActiveEnemyMovementPenalty()
    {
        CivInfo.HasActiveEnemyMovementPenalty = CivInfo.HasUnique(UniqueType.EnemyUnitsSpendExtraMovement);
        CivInfo.EnemyMovementPenaltyUniques =
            CivInfo.GetMatchingUniques(UniqueType.EnemyUnitsSpendExtraMovement);
    }

    public void UpdateCitiesConnectedToCapital(bool initialSetup = false)
    {
        if (CivInfo.Cities.Count == 0) return; // No cities to connect

        List<CityObject> oldConnectedCities = initialSetup
            ? CivInfo.Cities.Where(it => it.ConnectedToCapitalStatus).Distinct().ToList()
            : CitiesConnectedToCapitalToMediums.Keys.ToList();
        var oldConnectedCitiesSet = new HashSet<CityObject>(oldConnectedCities);

        CitiesConnectedToCapitalToMediums = new CapitalConnectionsFinder(CivInfo).Find();

        var newConnectedCities = CitiesConnectedToCapitalToMediums.Keys.ToList();
        var newConnectedCitiesSet = new HashSet<CityObject>(newConnectedCities);

        foreach (var city in newConnectedCities)
            if (!oldConnectedCitiesSet.Contains(city) && ReferenceEquals(city.Civ, CivInfo) && !ReferenceEquals(city, CivInfo.GetCapital()))
                CivInfo.AddNotification($"[{city.Name}] has been connected to your capital!",
                    city.Location, Notification.NotificationCategory.Cities, NotificationIcon.Gold);

        // This may still contain cities that have just been destroyed by razing - thus the population test
        foreach (var city in oldConnectedCities)
            if (!newConnectedCitiesSet.Contains(city) && ReferenceEquals(city.Civ, CivInfo) && city.Population.Population > 0)
                CivInfo.AddNotification($"[{city.Name}] has been disconnected from your capital!",
                    city.Location, Notification.NotificationCategory.Cities, NotificationIcon.Gold);

        foreach (var city in CivInfo.Cities)
            city.ConnectedToCapitalStatus = newConnectedCitiesSet.Contains(city);
    }

    private void UpdateCityStateBonuses()
    {
        if (CivInfo.IsCityState) return;

        var newMaps = new List<UniqueMap>();
        foreach (var diplomacyManager in CivInfo.Diplomacy.Values)
        {
            var cityState = diplomacyManager.OtherCiv;
            if (!cityState.IsCityState || cityState.IsDefeated()) continue;
            UniqueMap uniqueMap;
            if (ReferenceEquals(cityState.AllyCiv, CivInfo))
                uniqueMap = cityState.CityStateType.AllyBonusUniqueMap;
            else if (cityState.GetDiplomacyManager(CivInfo).NotNull().GetInfluence() >= 30)
                uniqueMap = cityState.CityStateType.FriendBonusUniqueMap;
            else
                continue;
            newMaps.Add(uniqueMap);
        }
        CityStateBonusUniqueMaps = newMaps;
    }

    public void UpdateCivResources()
    {
        UpdateCityStateBonuses();
        var newDetailedCivResources = new ResourceSupplyList();
        foreach (var city in CivInfo.Cities) newDetailedCivResources.Add(city.GetResourcesGeneratedByCity());

        if (!CivInfo.IsCityState)
        {
            // First we get all these resources of each city state separately
            var cityStateProvidedResources = new ResourceSupplyList();
            var resourceBonusPercentage = 1f;
            CivInfo.ForEachMatchingUnique(UniqueType.CityStateResources, unique =>
            {
                resourceBonusPercentage += unique.Params[0].ToFloat() / 100;
            });
            foreach (var cityStateAlly in CivInfo.GetKnownCivs().Where(it => ReferenceEquals(it.AllyCiv, CivInfo)))
            {
                foreach (var resourceSupply in cityStateAlly.CityStateFunctions.GetCityStateResourcesForAlly())
                {
                    if (((IHasUniques)resourceSupply.Resource).HasUnique(UniqueType.CannotBeTraded, cityStateAlly.State)) continue;
                    var newAmount = KotlinMath.FloatToInt(resourceSupply.Amount * resourceBonusPercentage);
                    cityStateProvidedResources.Add(new ResourceSupplyList.ResourceSupply(resourceSupply.Resource, resourceSupply.Origin, newAmount));
                }
            }
            // Then we combine these into one
            newDetailedCivResources.AddByResource(cityStateProvidedResources, Constants.CityStates);
            Unciv.Logic.Civilization.Diplomacy.FeitoriaRules.AddSiphonedResources(CivInfo, newDetailedCivResources); // B034
        }

        CivInfo.ForEachMatchingUnique(UniqueType.ProvidesResources, unique =>
        {
            if (unique.SourceObjectType != UniqueTarget.Building && unique.SourceObjectType != UniqueTarget.Wonder) // already calculated in city
            {
                var resource = CivInfo.GameInfo.Ruleset.TileResources[unique.Params[1]];
                newDetailedCivResources.Add(
                    resource,
                    unique.GetSourceNameForUser(),
                    KotlinMath.FloatToInt(unique.Params[0].ToFloat() * CivInfo.GetResourceModifier(resource)));
            }
        });

        foreach (var diplomacyManager in CivInfo.Diplomacy.Values)
            newDetailedCivResources.Add(diplomacyManager.ResourcesFromTrade());

        foreach (var unit in CivInfo.Units.GetCivUnits())
            newDetailedCivResources.SubtractResourceRequirements(
                unit.GetResourceRequirementsPerTurn(), CivInfo.GameInfo.Ruleset, "Units");

        newDetailedCivResources.RemoveAll(it => it.Resource.IsCityWide);

        // Check if anything has actually changed so we don't update stats for no reason - this uses List equality which means it checks the elements
        if (CivInfo.DetailedCivResources.SequenceEqual(newDetailedCivResources)) return;

        var summarizedResourceSupply = newDetailedCivResources.SumByResource("All");

        var newResourceUniqueMap = new UniqueMap();
        foreach (var resource in summarizedResourceSupply)
            if (resource.Amount > 0)
                newResourceUniqueMap.AddUniques(resource.Resource.UniqueObjects);

        CivInfo.DetailedCivResources = newDetailedCivResources;
        CivInfo.SummarizedCivResourceSupply = summarizedResourceSupply;
        CivInfo.CivResourcesUniqueMap = newResourceUniqueMap;

        CivInfo.UpdateStatsForNextTurn(); // More or less resources = more or less happiness, with potential domino effects
    }

    public Proximity UpdateProximity(Civilization otherCiv, Proximity? preCalculated = null)
    {
        if (ReferenceEquals(otherCiv, CivInfo)) return Proximity.None;
        if (preCalculated != null)
        {
            // We usually want to update this for a pair of civs at the same time
            // Since this function *should* be symmetrical for both civs, we can just do it once
            CivInfo.Proximity[otherCiv.CivId] = preCalculated.Value;
            return preCalculated.Value;
        }
        if (CivInfo.Cities.Count == 0 || otherCiv.Cities.Count == 0)
        {
            CivInfo.Proximity[otherCiv.CivId] = Proximity.None;
            return Proximity.None;
        }

        var mapParams = CivInfo.GameInfo.TileMap.MapParameters;
        var minDistance = 100000; // a long distance
        var totalDistance = 0;
        var connections = 0;

        var proximity = Proximity.None;

        foreach (var ourCity in CivInfo.Cities)
        {
            foreach (var theirCity in otherCiv.Cities)
            {
                var distance = ourCity.GetCenterTile().AerialDistanceTo(theirCity.GetCenterTile());
                totalDistance = unchecked(totalDistance + distance);
                connections++;
                if (minDistance > distance) minDistance = distance;
            }
        }

        if (minDistance <= 7)
        {
            proximity = Proximity.Neighbors;
        }
        else if (connections > 0)
        {
            var averageDistance = totalDistance / connections;
            var mapFactor = mapParams.Shape == MapShape.Rectangular
                ? (mapParams.MapSize.Height + mapParams.MapSize.Width) / 2
                : (mapParams.MapSize.Radius * 3) / 2; // slightly less area than equal size rect

            var closeDistance = Math.Clamp(mapFactor * 25 / 100, 10, 20);
            var farDistance = Math.Clamp(mapFactor * 45 / 100, 20, 50);

            proximity = minDistance <= 11 && averageDistance <= closeDistance
                ? Proximity.Close
                : averageDistance <= farDistance
                    ? Proximity.Far
                    : Proximity.Distant;
        }

        // Check if different continents (unless already max distance, or water map)
        if (connections > 0 && proximity != Proximity.Distant && !CivInfo.GameInfo.TileMap.IsWaterMap()
            && CivInfo.GetCapital().NotNull().GetCenterTile().GetContinent() != otherCiv.GetCapital().NotNull().GetCenterTile().GetContinent())
        {
            // Different continents - increase separation by one step
            proximity = proximity switch
            {
                Proximity.Far => Proximity.Distant,
                Proximity.Close => Proximity.Far,
                Proximity.Neighbors => Proximity.Close,
                _ => proximity,
            };
        }

        // If there aren't many players (left) we can't be that far
        var numMajors = CivInfo.GameInfo.GetAliveMajorCivs().Count;
        if (numMajors <= 2 && proximity > Proximity.Close)
            proximity = Proximity.Close;
        if (numMajors <= 4 && proximity > Proximity.Far)
            proximity = Proximity.Far;

        CivInfo.Proximity[otherCiv.CivId] = proximity;

        return proximity;
    }
}
