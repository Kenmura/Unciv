using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Logic.Map.MapUnits;
using Unciv.Logic.Map.Tiles;
using Unciv.Logic.Tourism;
using Unciv.Logic.WorldCongress;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.StatTypes;
using Unciv.UI.Components.Extensions;
using Unciv.Utils;

namespace Unciv.Logic.Civilization.Transients;

// Ported from core/src/com/unciv/logic/civilization/transients/CivInfoStatsForNextTurn.kt (T058).
//
// Deviations:
// - MapUnit is reached through the IMapUnitState seam (implemented by MapUnit, T065); the managers are the contracts of CivilizationContracts.cs.
// - getHappinessBreakdown returns a JavaHashMap: the Kotlin HashMap<String, Float> is iterated in java.util.HashMap order (String keys
//   have a deterministic JVM hash) and Civilization.updateStatsForNextTurn sums its values as Float, so the order is observable.
// - Kotlin Float/Double/Int arithmetic is kept (no widening); sumOf over Int wraps.
// - The timeThis profiling wrappers and the purity annotations are dropped.

/// <summary>CivInfo class was getting too crowded</summary>
public class CivInfoStatsForNextTurn(Civilization civInfo)
{
    public Civilization CivInfo { get; } = civInfo;

    /// <summary>Happiness for next turn</summary>
    public int Happiness { get; set; }

    public Models.StatTypes.Stats StatsForNextTurn { get; set; } = new();

    private int GetUnitMaintenance()
    {
        const float baseUnitCost = 0.5f;
        var freeUnits = 3;
        CivInfo.ForEachMatchingUnique(UniqueType.FreeUnits, CivInfo.State, unique =>
        {
            freeUnits = unchecked(freeUnits + unique.Params[0].ToInt());
        });

        IEnumerable<MapUnit> unitsToPayFor = CivInfo.Units.GetCivUnits();
        if (CivInfo.HasUnique(UniqueType.UnitsInCitiesNoMaintenance))
            unitsToPayFor = unitsToPayFor.Where(it => !(it.GetTile().IsCityCenter() && it.CanGarrison()));

        // Each unit starts with 1f aka 100% of cost, and then the discount is added.
        // Note all discounts are in the form of -X%, such as -25 for 25% reduction

        var costsToPay = new List<float>();

        // We IGNORE the conditionals when we get them civ-wide, so we won't need to do the same thing for EVERY unit in the civ.
        // This leads to massive memory and CPU time savings when calculating the maintenance!
        var civwideDiscountUniques = CivInfo.GetMatchingUniques(UniqueType.UnitMaintenanceDiscount, GameContext.IgnoreConditionalsState)
            .ToList();

        foreach (var unit in unitsToPayFor)
        {
            var stateForConditionals = unit.Cache.State;
            var unitMaintenance = 1f;
            var uniquesThatApply = unit.GetMatchingUniques(
                UniqueType.UnitMaintenanceDiscount,
                stateForConditionals
            ).Concat(civwideDiscountUniques.Where(it => it.ConditionalsApply(stateForConditionals)));
            foreach (var unique in uniquesThatApply)
                unitMaintenance *= unique.Params[0].ToPercent();
            costsToPay.Add(unitMaintenance);
        }

        // Sort by descending maintenance, then drop most expensive X units to make them free
        // If more free than units left, runs sum on empty sequence
        costsToPay = costsToPay.OrderByDescending(it => it).ToList();
        // Kotlin Sequence.drop throws for a negative count
        if (freeUnits < 0) throw new ArgumentException($"Requested element count {freeUnits} is less than zero.");
        double paidSum = 0.0;
        foreach (var cost0 in costsToPay.Skip(freeUnits)) paidSum += (double)cost0;
        var numberOfUnitsToPayFor = (float)Math.Max(0.0, paidSum);

        // as game progresses Maintenance cost rises
        var turnLimit = (float)CivInfo.GameInfo.Speed.NumTotalTurns();
        var gameProgress = Math.Min(CivInfo.GameInfo.Turns / turnLimit, 1f);

        var cost = baseUnitCost * numberOfUnitsToPayFor * (1 + gameProgress);
        cost = KotlinMath.Pow(cost, 1 + gameProgress / 3); // Why 3? To spread 1 to 1.33

        if (!CivInfo.IsHuman())
            cost *= CivInfo.GameInfo.GetDifficulty().AiUnitMaintenanceModifier;

        int armyTax = LeagueEffects.For(CivInfo).UnitMaintenancePercent; // B016: Standing Army Tax
        if (armyTax != 0) cost *= 1 + armyTax / 100f;

        return KotlinMath.FloatToInt(cost);
    }

    private Models.StatTypes.Stats GetTransportationUpkeep()
    {
        // NOTE: When this logic changes, you might need to update the UI-only ImprovementPickerScreen.getMaintenance function too.

        var transportationUpkeep = new Models.StatTypes.Stats();
        // we no longer use .flatMap, because there are a lot of tiles and keeping them all in a list
        // just to go over them once is a waste of memory - there are low-end phones who don't have much ram

        var ignoredTileTypes =
            CivInfo.GetMatchingUniques(UniqueType.NoImprovementMaintenanceInSpecificTiles)
                .Select(it => it.Params[0]).ToHashSet(); // needs to be .toHashSet()ed,
        // Because we go over every tile in every city and check if it's in this list, which can get real heavy.

        void AddMaintenanceUniques(TileImprovement road, UniqueType type, GameContext state)
        {
            ((IHasUniques)road).ForEachMatchingUnique(type, state, unique =>
            {
                transportationUpkeep.Add(StatExtensions.ValueOf(unique.Params[1]), unique.Params[0].ToFloat());
            });
        }

        foreach (var city in CivInfo.Cities)
        {
            foreach (var tile in city.GetTiles())
            {
                if (tile.IsCityCenter()) continue;
                if (tile.GetUnpillagedRoad() == RoadStatus.None) continue; // Cheap checks before pricey checks
                if (ignoredTileTypes.Any(it => tile.MatchesFilter(it, CivInfo))) continue;
                var road = tile.GetUnpillagedRoadImprovement().NotNull(); // covered by RoadStatus.None test
                var gameContext = new GameContext(CivInfo, tile: tile);
                AddMaintenanceUniques(road, UniqueType.ImprovementMaintenance, gameContext);
                AddMaintenanceUniques(road, UniqueType.ImprovementAllMaintenance, gameContext);
            }
        }

        // tabulate neutral roads
        foreach (var position in CivInfo.NeutralRoads)
        {
            var tile = CivInfo.GameInfo.TileMap[position];
            if (tile.GetUnpillagedRoad() == RoadStatus.None) continue; // Cheap checks before pricey checks
            var road = tile.GetUnpillagedRoadImprovement().NotNull(); // covered by RoadStatus.None test
            var gameContext = new GameContext(CivInfo, tile: tile);
            AddMaintenanceUniques(road, UniqueType.ImprovementAllMaintenance, gameContext);
        }

        CivInfo.ForEachMatchingUnique(UniqueType.RoadMaintenance, unique =>
        {
            transportationUpkeep.TimesInPlace(unique.Params[0].ToPercent());
        });

        return transportationUpkeep;
    }

    public int GetUnitSupply()
    {
        /* TotalSupply = BaseSupply + NumCities*modifier + Population*modifier
         * In civ5, it seems population modifier is always 0.5, so i hardcoded it down below */
        var supply = unchecked(GetBaseUnitSupply() + GetUnitSupplyFromCities() + GetUnitSupplyFromPop());

        if (CivInfo.IsMajorCiv() && CivInfo.PlayerType == PlayerType.AI)
            supply = KotlinMath.FloatToInt(supply * (1f + CivInfo.GetDifficulty().AiUnitSupplyModifier));
        return supply;
    }

    public int GetBaseUnitSupply()
    {
        return unchecked(CivInfo.GetDifficulty().UnitSupplyBase +
            CivInfo.GetMatchingUniques(UniqueType.BaseUnitSupply).SumOf(it => it.Params[0].ToInt()));
    }

    public int GetUnitSupplyFromCities()
    {
        return unchecked(CivInfo.Cities.Count *
            (CivInfo.GetDifficulty().UnitSupplyPerCity
             + CivInfo.GetMatchingUniques(UniqueType.UnitSupplyPerCity).SumOf(it => it.Params[0].ToInt())));
    }

    public int GetUnitSupplyFromPop()
    {
        var totalSupply = CivInfo.Cities.SumOf(it => it.Population.Population) * CivInfo.GameInfo.Ruleset.ModOptions.Constants.UnitSupplyPerPopulation;

        CivInfo.ForEachMatchingUnique(UniqueType.UnitSupplyPerPop, unique =>
        {
            var applicablePopulation = CivInfo.Cities
                .Where(it => it.MatchesFilter(unique.Params[2], null))
                .SumOf(it => it.Population.Population / unique.Params[1].ToInt());
            totalSupply += unique.Params[0].ToDouble() * applicablePopulation;
        });
        return KotlinMath.DoubleToInt(totalSupply);
    }

    public int GetUnitSupplyDeficit() => Math.Max(0, unchecked(CivInfo.Units.GetCivUnitsSize() - GetUnitSupply()));

    /// <summary>Per each supply missing, a player gets -10% production. Capped at -70%.</summary>
    public float GetUnitSupplyProductionPenalty() => -Math.Min(GetUnitSupplyDeficit() * 10f, 70f);

    public StatMap GetStatMapForNextTurn()
    {
        var statMap = new StatMap();
        foreach (var city in CivInfo.Cities)
        {
            foreach (var entry in city.CityStats.FinalStatList)
                statMap.Add(entry.Key, entry.Value);
        }

        //City-States bonuses
        foreach (var otherCiv in CivInfo.GetKnownCivs())
        {
            if (!otherCiv.IsCityState) continue;
            if (otherCiv.GetDiplomacyManager(CivInfo).NotNull().RelationshipIgnoreAfraid() != RelationshipLevel.Ally)
                continue;
            CivInfo.ForEachMatchingUnique(UniqueType.CityStateStatPercent, unique =>
            {
                var stats = new Models.StatTypes.Stats();
                stats.Add(
                    StatExtensions.ValueOf(unique.Params[0]),
                    otherCiv.Stats.StatsForNextTurn[StatExtensions.ValueOf(unique.Params[0])] * unique.Params[1].ToFloat() / 100f
                );
                statMap.Add(
                    Constants.CityStates,
                    stats
                );
            });
        }

        statMap["Transportation upkeep"] = GetTransportationUpkeep() * -1;
        statMap["Unit upkeep"] = new Models.StatTypes.Stats(gold: -(float)GetUnitMaintenance());

        if (CivInfo.GetHappiness() > 0)
        {
            var excessHappinessConversion = new Models.StatTypes.Stats();
            CivInfo.ForEachMatchingUnique(UniqueType.ExcessHappinessToGlobalStat, unique =>
            {
                excessHappinessConversion.Add(StatExtensions.ValueOf(unique.Params[1]), unique.Params[0].ToFloat() / 100f * CivInfo.GetHappiness());
            });
            statMap.Add("Policies", excessHappinessConversion);
        }

        // negative gold hurts science
        // if we have - or 0, then the techs will never be complete and the tech button
        // will show a negative number of turns and int.max, respectively
        if (SumOfFloats(statMap.Values.Select(it => it.Gold)) < 0 && CivInfo.Gold < 0)
        {
            var scienceDeficit = MathF.Max(SumOfFloats(statMap.Values.Select(it => it.Gold)),
                1 - SumOfFloats(statMap.Values.Select(it => it.Science))
            ); // Leave at least 1
            statMap["Treasury deficit"] = new Models.StatTypes.Stats(science: scienceDeficit);
        }
        var goldDifferenceFromTrade = CivInfo.Diplomacy.Values.SumOf(it => it.GoldPerTurn());
        if (goldDifferenceFromTrade != 0)
            statMap["Trade"] = new Models.StatTypes.Stats(gold: (float)goldDifferenceFromTrade);

        foreach (var (key, value) in GetGlobalStatsFromUniques())
            statMap.Add(key, value);

        return statMap;
    }

    /// <summary>Kotlin <c>Iterable&lt;Float&gt;.sum()</c>: accumulates in Float.</summary>
    private static float SumOfFloats(IEnumerable<float> values)
    {
        var sum = 0f;
        foreach (var value in values) sum += value;
        return sum;
    }

    public JavaHashMap<string, float> GetHappinessBreakdown()
    {
        var statMap = new JavaHashMap<string, float>();

        statMap["Base happiness"] = (float)CivInfo.GetDifficulty().BaseHappiness;

        var happinessPerUniqueLuxury = 4f + CivInfo.GetDifficulty().ExtraHappinessPerLuxury;
        CivInfo.ForEachMatchingUnique(UniqueType.BonusHappinessFromLuxury, unique =>
        {
            happinessPerUniqueLuxury += unique.Params[0].ToInt();
        });

        var bannedLuxuries = LeagueEffects.For(CivInfo); // B016: Ban Luxury
        var ownedLuxuries = CivInfo.GetCivResourceSupply().Select(it => it.Resource)
            .Where(it => it.ResourceType == ResourceType.Luxury && !bannedLuxuries.IsLuxuryBanned(it.Name))
            .ToList();

        var relevantLuxuries = CivInfo.GetCivResourceSupply()
            .Select(it => it.Resource)
            .Count(it => it.ResourceType == ResourceType.Luxury && !bannedLuxuries.IsLuxuryBanned(it.Name)
                         && !((IHasUniques)it).GetMatchingUniques(UniqueType.ObsoleteWith)
                             .Any(unique => CivInfo.Tech.IsResearched(unique.Params[0])));
        statMap["Luxury resources"] = relevantLuxuries * happinessPerUniqueLuxury;

        var happinessBonusForCityStateProvidedLuxuries =
            CivInfo.GetMatchingUniques(UniqueType.CityStateLuxuryHappiness).SumOf(it => it.Params[0].ToInt()) / 100f;

        var luxuriesProvidedByCityStates = CivInfo.GetKnownCivs()
            .Where(it => it.IsCityState && ReferenceEquals(it.AllyCiv, CivInfo))
            .SelectMany(it => it.GetCivResourceSupply().Select(res => res.Resource))
            .Distinct()
            .Count(it => it.ResourceType == ResourceType.Luxury && ownedLuxuries.Contains(it));

        statMap["City-State Luxuries"] =
            happinessPerUniqueLuxury * luxuriesProvidedByCityStates * happinessBonusForCityStateProvidedLuxuries;

        var luxuriesAllOfWhichAreTradedAway = CivInfo.DetailedCivResources
            .Where(it => it.Amount < 0 && it.Resource.ResourceType == ResourceType.Luxury
                         && (it.Origin == "Trade" || it.Origin == "Trade request"))
            .Select(it => it.Resource)
            .Where(it => !ownedLuxuries.Contains(it) && !bannedLuxuries.IsLuxuryBanned(it.Name))
            .ToList();

        statMap["Traded Luxuries"] =
            luxuriesAllOfWhichAreTradedAway.Count * happinessPerUniqueLuxury *
            CivInfo.GetMatchingUniques(UniqueType.RetainHappinessFromLuxury)
                .SumOf(it => it.Params[0].ToInt()) / 100f;

        foreach (var city in CivInfo.Cities)
        {
            // There appears to be a concurrency problem? In concurrent thread in ConstructionsTable.getConstructionButtonDTOs
            // Literally no idea how, since happinessList is ONLY replaced, NEVER altered.
            // Oh well, toList() should solve the problem, wherever it may come from.
            foreach (var (key, value) in city.CityStats.HappinessList.ToList())
                statMap[key] = value + (statMap.TryGetValue(key, out var existing) ? existing : 0f);
        }

        var transportUpkeep = GetTransportationUpkeep();
        if (transportUpkeep.Happiness != 0f)
            statMap["Transportation Upkeep"] = -transportUpkeep.Happiness;

        foreach (var (key, value) in GetGlobalStatsFromUniques())
            statMap[key] = value.Happiness + (statMap.TryGetValue(key, out var existing) ? existing : 0f);

        int projectHappiness = LeagueProjects.Happiness(CivInfo); // B017: International Games reward
        if (projectHappiness != 0) statMap["World Congress"] = projectHappiness;

        var opinionUnhappiness = PublicOpinionRules.Unhappiness(CivInfo); // B014: BNW public opinion; 0 without an ideology
        if (opinionUnhappiness != 0) statMap["Public opinion"] = -opinionUnhappiness;

        return statMap;
    }

    private StatMap GetGlobalStatsFromUniques()
    {
        var statMap = new StatMap();
        var religion = CivInfo.ReligionManager.Religion;
        if (religion != null)
        {
            religion.FounderBeliefUniqueMap.ForEachMatchingUnique(
                UniqueType.StatsFromGlobalCitiesFollowingReligion, CivInfo.State, unique =>
                {
                    statMap.Add(
                        "Religion",
                        unique.Stats * CivInfo.ReligionManager.NumberOfCitiesFollowingThisReligion()
                    );
                });

            religion.FounderBeliefUniqueMap.ForEachMatchingUnique(
                UniqueType.StatsFromGlobalFollowers, CivInfo.State, unique =>
                {
                    statMap.Add(
                        "Religion",
                        unique.Stats * (float)CivInfo.ReligionManager.NumberOfFollowersFollowingThisReligion(unique.Params[2])
                        / unique.Params[1].ToFloat()
                    );
                });
        }

        CivInfo.ForEachMatchingUnique(UniqueType.StatsPerPolicies, unique =>
        {
            var amount = CivInfo.Policies.AdoptedPolicies
                .Count(it => !Policy.IsBranchCompleteByName(it)) / unique.Params[1].ToInt();
            statMap.Add("Policies", unique.Stats * amount);
        });

        CivInfo.ForEachMatchingUnique(UniqueType.Stats, unique =>
        {
            if (unique.SourceObjectType != UniqueTarget.Building && unique.SourceObjectType != UniqueTarget.Wonder)
                statMap.Add(unique.GetSourceNameForUser(), unique.Stats);
        });

        CivInfo.ForEachMatchingUnique(UniqueType.StatsPerStat, unique =>
        {
            var amount = CivInfo.GetStatReserve(StatExtensions.ValueOf(unique.Params[2])) / unique.Params[1].ToInt();
            statMap.Add("Stats", unique.Stats * amount);
        });

        var statsPerNaturalWonder = new Models.StatTypes.Stats(happiness: 1f);

        CivInfo.ForEachMatchingUnique(UniqueType.StatsFromNaturalWonders, unique =>
        {
            statsPerNaturalWonder.Add(unique.Stats);
        });

        statMap.Add("Natural Wonders", statsPerNaturalWonder * CivInfo.NaturalWonders.Count);

        if (statMap.ContainsKey(Constants.CityStates))
        {
            CivInfo.ForEachMatchingUnique(UniqueType.BonusStatsFromCityStates, unique =>
            {
                var bonusPercent = unique.Params[0].ToPercent();
                var bonusStat = StatExtensions.ValueOf(unique.Params[1]);
                var cityStatesStats = statMap[Constants.CityStates];
                cityStatesStats[bonusStat] *= bonusPercent;
            });
        }

        return statMap;
    }
}
