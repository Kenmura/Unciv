using Unciv.Logic.Civilization.Transients;
using Unciv.Logic.Map.Tiles;
using Unciv.Models;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Ruleset.Unit;
using Unciv.Models.StatTypes;
using Unciv.UI.Components.Extensions;
using Unciv.Utils;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.Logic.City;

// Ported from core/src/com/unciv/logic/city/CityStats.kt (T062).
//
// Stripped: the timeThis profiling wrappers and the purity annotations. DebugUtils.SUPERCHARGED is the game's EngineOptions.Supercharged (T048).
// Float/Double/Int arithmetic keeps the Kotlin evaluation order exactly (Float sums accumulate sequentially from 0f, sumOf { toDouble() }
// accumulates in Double). The members of CityPopulationManager / CityConstructions / CityReligionManager that this class calls are seam members
// (CivilizationContracts.cs) until the managers are ported.

public class StatTreeNode
{
    public LinkedHashMap<string, StatTreeNode> Children { get; } = new();
    private Stats? innerStats;

    public void SetInnerStat(Stat stat, float value)
    {
        innerStats ??= new Stats();
        innerStats[stat] = value;
    }

    private void AddInnerStats(Stats stats)
    {
        if (innerStats == null) innerStats = stats.Clone(); // Copy the stats instead of referencing them
        else innerStats.Add(stats); // What happens if we add 2 stats to the same leaf?
    }

    public void AddStats(Stats? newStats, params string[] hierarchyList)
    {
        if (newStats == null) return;
        if (newStats.IsEmpty()) return;
        if (hierarchyList.Length == 0)
        {
            AddInnerStats(newStats);
            return;
        }
        var childName = hierarchyList[0];
        if (!Children.ContainsKey(childName))
            Children.Put(childName, new StatTreeNode());
        Children[childName].AddStats(newStats, hierarchyList.Skip(1).ToArray());
    }

    public void Add(StatTreeNode otherTree)
    {
        if (otherTree.innerStats != null) AddInnerStats(otherTree.innerStats);
        foreach (var (key, value) in otherTree.Children)
        {
            if (!Children.ContainsKey(key)) Children.Put(key, value);
            else Children[key].Add(value);
        }
    }

    public StatTreeNode Clone()
    {
        var clone = new StatTreeNode { innerStats = innerStats?.Clone() };
        clone.Children.PutAll(Children.Select(it => new KeyValuePair<string, StatTreeNode>(it.Key, it.Value.Clone())).ToList());
        return clone;
    }

    public Stats TotalStats
    {
        get
        {
            var toReturn = new Stats();
            if (innerStats != null) toReturn.Add(innerStats);
            foreach (var child in Children.Values) toReturn.Add(child.TotalStats);
            return toReturn;
        }
    }
}

/// <summary>
/// Holds and calculates <see cref="Stats"/> for a city.
/// No field needs to be saved, all are calculated on the fly, so its field in <see cref="City"/> is transient.
/// </summary>
public class CityStats(City city)
{
    public City City { get; } = city;

    #region Fields, Transient

    public StatTreeNode BaseStatTree { get; set; } = new();

    public StatTreeNode StatPercentBonusTree { get; set; } = new();

    /// <summary>Computed from baseStatList and statPercentBonusList - this is so the players can see a breakdown</summary>
    public StatMap FinalStatList { get; set; } = [];

    public LinkedHashMap<string, float> HappinessList { get; set; } = new();

    public Stats StatsFromTiles { get; set; } = new();

    /// <summary>This is so we won't have to calculate this multiple times - takes a lot of time, especially on phones</summary>
    public Stats CurrentCityStats { get; set; } = new();

    #endregion
    #region Pure Functions

    private static float SumOfFloats(IEnumerable<float> values)
    {
        float sum = 0f;
        foreach (var value in values) sum += value;
        return sum;
    }

    private static double SumOfDoubles(IEnumerable<double> values)
    {
        double sum = 0.0;
        foreach (var value in values) sum += value;
        return sum;
    }

    private Stats GetStatsFromTradeRoute()
    {
        var stats = new Stats();
        var capitalForTradeRoutePurposes = City.Civ.GetCapital().NotNull();
        if (!ReferenceEquals(City, capitalForTradeRoutePurposes) && City.IsConnectedToCapital())
        {
            stats.Gold = capitalForTradeRoutePurposes.Population.Population * 0.15f + City.Population.Population * 1.1f - 1; // Calculated by http://civilization.wikia.com/wiki/Trade_route_(Civ5)
            City.ForEachMatchingUnique(UniqueType.StatsFromTradeRoute, unique =>
            {
                stats.Add(unique.Stats);
            });
            var percentageStats = new Stats();
            City.ForEachMatchingUnique(UniqueType.StatPercentFromTradeRoutes, unique =>
            {
                percentageStats[StatExtensions.ValueOf(unique.Params[1])] += unique.Params[0].ToFloat();
            });
            foreach (var pair in stats)
            {
                stats[pair.Key] *= percentageStats[pair.Key].ToPercent();
            }
        }
        return stats;
    }

    private Stats? GetStatsFromProduction(float production)
    {
        if (StatExtensions.IsStat(City.CityConstructions.CurrentConstructionName()))
        {
            var stats = new Stats();
            var stat = StatExtensions.ValueOf(City.CityConstructions.CurrentConstructionName());
            stats[stat] = production * GetStatConversionRate(stat);
            return stats;
        }
        return null;
    }

    public float GetStatConversionRate(Stat stat)
    {
        float conversionRate = 1 / 4f;
        var conversionUnique = City.Civ.GetMatchingUniques(UniqueType.ProductionToStatConversionBonus).FirstOrDefault(it => it.Params[0] == stat.ToString());
        if (conversionUnique != null)
        {
            conversionRate *= conversionUnique.Params[1].ToPercent();
        }
        return conversionRate;
    }

    private Stats? GetStatPercentBonusesFromRailroad()
    {
        var railroadImprovement = City.GetRuleset().RailroadImprovement;
        if (railroadImprovement == null) return null; // for mods
        var techEnablingRailroad = railroadImprovement.TechRequired;
        // If we conquered enemy cities connected by railroad, but we don't yet have that tech,
        // we shouldn't get bonuses, it's as if the tracks are laid out but we can't operate them.
        if ((techEnablingRailroad == null || City.Civ.Tech.IsResearched(techEnablingRailroad))
                && (City.IsCapital() || IsConnectedToCapital(RoadStatus.Railroad)))
            return new Stats(production: 25f);
        return null;
    }

    private Stats? GetStatPercentBonusesFromPuppetCity()
    {
        if (!City.IsPuppet) return null;
        return new Stats(science: -25f, culture: -25f);
    }

    public StatMap GetGrowthBonus(float totalFood)
    {
        var growthSources = new StatMap();
        // "[amount]% growth [cityFilter]"
        City.ForEachMatchingUnique(UniqueType.GrowthPercentBonus, City.State, unique =>
        {
            if (!City.MatchesFilter(unique.Params[1])) return;

            growthSources.Add(
                unique.GetSourceNameForUser(),
                new Stats(food: unique.Params[0].ToFloat() / 100f * totalFood)
            );
        });
        return growthSources;
    }

    public virtual bool HasExtraAnnexUnhappiness()
    {
        if (ReferenceEquals(City.Civ, City.FoundingCivObject) || City.IsPuppet) return false;
        return !City.ContainsBuildingUnique(UniqueType.RemovesAnnexUnhappiness);
    }

    public Stats GetStatsOfSpecialist(string specialistName)
    {
        if (!City.GetRuleset().Specialists.TryGetValue(specialistName, out var specialist))
            return new Stats();
        var stats = specialist.CloneStats();
        City.ForEachMatchingUnique(UniqueType.StatsFromSpecialist, City.State, unique =>
        {
            if (City.MatchesFilter(unique.Params[1]))
                stats.Add(unique.Stats);
        });
        City.ForEachMatchingUnique(UniqueType.StatsFromObject, City.State, unique =>
        {
            if (unique.Params[1] == specialistName)
                stats.Add(unique.Stats);
        });
        return stats;
    }

    private Stats GetStatsFromSpecialists(Counter<string> specialists)
    {
        var stats = new Stats();
        foreach (var (key, value) in specialists.Where(it => it.Value > 0).ToList()) // avoid concurrent modification when calculating construction costs
            stats.Add(GetStatsOfSpecialist(key) * value);
        return stats;
    }

    private StatTreeNode GetStatsFromUniquesBySource()
    {
        var sourceToStats = new StatTreeNode();

        var cityStateStatsMultipliers = City.Civ.GetMatchingUniques(UniqueType.BonusStatsFromCityStates).ToList();

        void AddUniqueStats(UniqueObject unique)
        {
            var stats = unique.Stats.Clone();
            if (unique.SourceObjectType == UniqueTarget.CityState)
                foreach (var multiplierUnique in cityStateStatsMultipliers)
                    stats[StatExtensions.ValueOf(multiplierUnique.Params[1])] *= multiplierUnique.Params[0].ToPercent();
            sourceToStats.AddStats(stats, unique.GetSourceNameForUser(), unique.SourceObjectName ?? "");
        }

        City.ForEachMatchingUnique(UniqueType.StatsPerCity, unique =>
        {
            if (City.MatchesFilter(unique.Params[1]))
                AddUniqueStats(unique);
        });

        // "[stats] per [amount] population [cityFilter]"
        City.ForEachMatchingUnique(UniqueType.StatsPerPopulation, unique =>
        {
            if (City.MatchesFilter(unique.Params[2]))
            {
                float amountOfEffects = City.Population.Population / unique.Params[1].ToInt();
                sourceToStats.AddStats(unique.Stats * amountOfEffects, unique.GetSourceNameForUser(), unique.SourceObjectName ?? "");
            }
        });

        City.ForEachMatchingUnique(UniqueType.StatsFromCitiesOnSpecificTiles, unique =>
        {
            if (City.GetCenterTile().MatchesTerrainFilter(unique.Params[1], City.Civ))
                AddUniqueStats(unique);
        });

        return sourceToStats;
    }

    private static Stats? GetStatPercentBonusesFromGoldenAge(bool isGoldenAge)
    {
        if (!isGoldenAge) return null;
        return new Stats(production: 20f, culture: 20f);
    }

    private StatTreeNode GetStatsPercentBonusesFromUniquesBySource(IConstruction currentConstruction)
    {
        var sourceToStats = new StatTreeNode();

        void AddUniqueStats(UniqueObject unique, Stat stat, float amount)
        {
            var stats = new Stats();
            stats.Add(stat, amount);
            sourceToStats.AddStats(stats, unique.GetSourceNameForUser(), unique.SourceObjectName ?? "");
        }

        City.ForEachMatchingUnique(UniqueType.StatPercentBonus, unique =>
        {
            AddUniqueStats(unique, StatExtensions.ValueOf(unique.Params[1]), unique.Params[0].ToFloat());
        });

        City.ForEachMatchingUnique(UniqueType.StatPercentBonusCities, unique =>
        {
            if (City.MatchesFilter(unique.Params[2]))
                AddUniqueStats(unique, StatExtensions.ValueOf(unique.Params[1]), unique.Params[0].ToFloat());
        });

        IEnumerable<UniqueObject> uniquesToCheck =
            currentConstruction is BaseUnit
                ? City.GetMatchingUniques(UniqueType.PercentProductionUnits)
            : currentConstruction is Building { } wonder && wonder.IsAnyWonder()
                ? City.GetMatchingUniques(UniqueType.PercentProductionWonders)
            : currentConstruction is Building { } building && !building.IsAnyWonder()
                ? City.GetMatchingUniques(UniqueType.PercentProductionBuildings)
            : []; // Science/Gold production

        foreach (var unique in uniquesToCheck)
        {
            if (ConstructionMatchesFilter(currentConstruction, unique.Params[1])
                && City.MatchesFilter(unique.Params[2]))
                AddUniqueStats(unique, Stat.Production, unique.Params[0].ToFloat());
        }

        City.ForEachMatchingUnique(UniqueType.StatPercentFromReligionFollowers, unique =>
        {
            AddUniqueStats(unique, StatExtensions.ValueOf(unique.Params[1]),
                MathF.Min(
                    unique.Params[0].ToFloat() * City.Religion.GetFollowersOfMajorityReligion(),
                    unique.Params[2].ToFloat()
                ));
        });

        if (currentConstruction is Building capitalBuilding
            && City.Civ.GetCapital()?.CityConstructions.BuiltBuildings.Contains(capitalBuilding.Name) == true)
        {
            City.ForEachMatchingUnique(UniqueType.PercentProductionBuildingsInCapital, unique =>
            {
                AddUniqueStats(unique, Stat.Production, unique.Params[0].ToFloat());
            });
        }

        return sourceToStats;
    }

    private Stats? GetStatPercentBonusesFromUnitSupply()
    {
        var supplyDeficit = City.Civ.Stats.GetUnitSupplyDeficit();
        if (supplyDeficit > 0)
            return new Stats(production: City.Civ.Stats.GetUnitSupplyProductionPenalty());
        return null;
    }

    private bool ConstructionMatchesFilter(IConstruction construction, string filter)
    {
        var state = City.State;
        if (construction is Building building) return building.MatchesFilter(filter, state);
        if (construction is BaseUnit unit) return unit.MatchesFilter(filter, state);
        return false;
    }

    public bool IsConnectedToCapital(RoadStatus roadType)
    {
        if (City.Civ.Cities.Count < 2) return false; // first city!

        // Railroad, or harbor from railroad
        return roadType == RoadStatus.Railroad
            ? City.IsConnectedToCapital(mediums => mediums.Any(it => it.RoadType() == RoadStatus.Railroad))
            : City.IsConnectedToCapital();
    }

    public RoadStatus GetRoadTypeOfConnectionToCapital()
    {
        return City.Civ.Cache.CitiesConnectedToCapitalToMediums.TryGetValue(City, out var mediums) && mediums.Count > 0
            ? mediums.Max(it => it.RoadType())
            : RoadStatus.None;
    }

    private float GetBuildingMaintenanceCosts()
    {
        // Same here - will have a different UI display.
        var buildingsMaintenance = City.CityConstructions.GetMaintenanceCosts(); // this is AFTER the bonus calculation!
        if (!City.Civ.IsHuman())
        {
            buildingsMaintenance *= City.Civ.GameInfo.GetDifficulty().AiBuildingMaintenanceModifier;
        }

        return buildingsMaintenance;
    }

    #endregion
    #region State-Changing Methods

    public void UpdateTileStats()
    {
        var stats = new Stats();
        var workedTiles = City.TilesInRange
            .Where(it =>
                City.Location == it.Position
                || City.IsWorked(it)
                || (ReferenceEquals(it.OwningCity, City) && (((IHasUniques?)it.GetUnpillagedTileImprovement())
                    ?.HasUnique(UniqueType.TileProvidesYieldWithoutPopulation, it.StateThisTile) == true
                        || it.TerrainHasUnique(UniqueType.TileProvidesYieldWithoutPopulation, it.StateThisTile))));
        foreach (var tile in workedTiles)
        {
            if (tile.IsBlockaded() && City.IsWorked(tile))
            {
                City.StopWorkingTile(tile);
                City.ShouldReassignPopulation = true;
                continue;
            }
            var tileStats = tile.Stats.GetTileStats(City, City.Civ);
            stats.Add(tileStats);
        }
        StatsFromTiles = stats;
    }

    // needs to be a separate function because we need to know the global happiness state
    // in order to determine how much food is produced in a city!
    public void UpdateCityHappiness(StatTreeNode statsFromBuildings,
                                    Stats? statsFromSpecialists = null,
                                    StatTreeNode? statsFromUniquesBySource = null)
    {
        statsFromSpecialists ??= GetStatsFromSpecialists(City.Population.GetNewSpecialists());
        statsFromUniquesBySource ??= GetStatsFromUniquesBySource();
        var civInfo = City.Civ;
        var newHappinessList = new LinkedHashMap<string, float>();
        // This calculation seems weird to me.
        // Suppose we calculate the modifier for an AI (non-human) player when the game settings has difficulty level 'prince'.
        // We first get the difficulty modifier for this civilization, which results in the 'chieftain' modifier (0.6) being used,
        // as this is a non-human player. Then we multiply that by the ai modifier in general, which is 1.0 for prince.
        // The end result happens to be 0.6, which seems correct. However, if we were playing on chieftain difficulty,
        // we would get back 0.6 twice and the modifier would be 0.36. Thus, in general there seems to be something wrong here
        // I don't know enough about the original whether they do something similar or not and can't be bothered to find where
        // in the source code this calculation takes place, but it would surprise me if they also did this double multiplication thing. ~xlenstra
        float unhappinessModifier = civInfo.GetDifficulty().UnhappinessModifier;
        if (!civInfo.IsHuman())
            unhappinessModifier *= civInfo.GameInfo.GetDifficulty().AiUnhappinessModifier;

        float unhappinessFromCity = -3f; // -3 happiness per city
        if (HasExtraAnnexUnhappiness())
            unhappinessFromCity -= 2f;

        float uniqueUnhappinessModifier = 0f;
        civInfo.ForEachMatchingUnique(UniqueType.UnhappinessFromCitiesPercentage, unique =>
        {
            uniqueUnhappinessModifier += unique.Params[0].ToFloat();
        });

        newHappinessList.Put("Cities", unhappinessFromCity * unhappinessModifier * uniqueUnhappinessModifier.ToPercent());

        float unhappinessFromCitizens = City.Population.Population;

        City.ForEachMatchingUnique(UniqueType.UnhappinessFromPopulationTypePercentageChange, unique =>
        {
            if (City.MatchesFilter(unique.Params[2]))
                unhappinessFromCitizens += (unique.Params[0].ToFloat() / 100f) * City.Population.GetPopulationFilterAmount(unique.Params[1]);
        });

        if (HasExtraAnnexUnhappiness())
            unhappinessFromCitizens *= 2f;

        if (unhappinessFromCitizens < 0) unhappinessFromCitizens = 0f;

        newHappinessList.Put("Population", -unhappinessFromCitizens * unhappinessModifier);

        if (HasExtraAnnexUnhappiness()) newHappinessList.Put("Occupied City", -2f); //annexed city

        float happinessFromSpecialists = KotlinMath.FloatToInt(statsFromSpecialists.Happiness);
        if (happinessFromSpecialists > 0) newHappinessList.Put("Specialists", happinessFromSpecialists);

        newHappinessList.Put("Buildings", KotlinMath.FloatToInt(statsFromBuildings.TotalStats.Happiness));

        newHappinessList.Put("Tile yields", StatsFromTiles.Happiness);

        var happinessBySource = statsFromUniquesBySource;
        foreach (var (source, stats) in happinessBySource.Children)
            if (stats.TotalStats.Happiness != 0f)
            {
                if (!newHappinessList.ContainsKey(source)) newHappinessList.Put(source, 0f);
                newHappinessList.Put(source, newHappinessList[source] + stats.TotalStats.Happiness);
            }

        // we don't want to modify the existing happiness list because that leads
        // to concurrency problems if we iterate on it while changing
        HappinessList = newHappinessList;
    }

    private void UpdateBaseStatList(StatTreeNode statsFromBuildings, Stats statsFromSpecialists,
                                    StatTreeNode statsFromUniquesBySource)
    {
        var newBaseStatTree = new StatTreeNode();

        // We don't edit the existing baseStatList directly, in order to avoid concurrency exceptions
        var newBaseStatList = new StatMap();

        newBaseStatTree.AddStats(new Stats(
            science: City.Population.Population,
            production: City.Population.GetFreePopulation()
        ), "Population");
        newBaseStatList["Tile yields"] = StatsFromTiles;
        newBaseStatList["Specialists"] = statsFromSpecialists;
        newBaseStatList["Trade routes"] = GetStatsFromTradeRoute();
        var statsFromGreatWorks = City.GetGreatWorkStats(); // B007
        if (!statsFromGreatWorks.IsEmpty()) newBaseStatList["Great Works"] = statsFromGreatWorks;
        newBaseStatTree.Children.Put("Buildings", statsFromBuildings);

        foreach (var (source, stats) in newBaseStatList)
            newBaseStatTree.AddStats(stats, source);

        newBaseStatTree.Add(statsFromUniquesBySource);
        BaseStatTree = newBaseStatTree;
    }

    private StatTreeNode GetStatPercentBonusList(IConstruction currentConstruction)
    {
        var newStatsBonusTree = new StatTreeNode();

        newStatsBonusTree.AddStats(GetStatPercentBonusesFromGoldenAge(City.Civ.GoldenAges.IsGoldenAge()), "Golden Age");
        newStatsBonusTree.AddStats(GetStatPercentBonusesFromRailroad(), "Railroad");
        newStatsBonusTree.AddStats(GetStatPercentBonusesFromPuppetCity(), "Puppet City");
        newStatsBonusTree.AddStats(GetStatPercentBonusesFromUnitSupply(), "Unit Supply");
        newStatsBonusTree.Add(GetStatsPercentBonusesFromUniquesBySource(currentConstruction));

        foreach (var building in City.CityConstructions.GetBuiltBuildings())
            newStatsBonusTree.AddStats(building.GetStatPercentageBonuses(City),
                "Buildings", building.Name);

        if (City.Civ.GameInfo.EngineOptions.Supercharged)
        {
            var stats = new Stats();
            foreach (var stat in StatExtensions.Entries) stats[stat] = 10000f;
            newStatsBonusTree.AddStats(stats, "Supercharged");
        }
        return newStatsBonusTree;
    }

    private void UpdateStatPercentBonusList(IConstruction currentConstruction)
    {
        StatPercentBonusTree = GetStatPercentBonusList(currentConstruction);
    }

    public virtual void Update(IConstruction? currentConstruction = null,
                               bool updateTileStats = true,
                               bool updateCivStats = true,
                               bool calculateGrowthModifiers = true)
    {
        currentConstruction ??= City.CityConstructions.GetCurrentConstruction();

        if (updateTileStats) UpdateTileStats();

        // We need to compute Tile yields before happiness

        var statsFromBuildings = City.CityConstructions.GetStats(); // this is performance heavy, so calculate once
        // Also performance-heavy, and previously computed twice with identical inputs (once inside
        // updateBaseStatList, once inside updateCityHappiness) - only baseStatTree is assigned between
        // the two, and neither reads it. Compute once and pass down.
        var statsFromSpecialists = GetStatsFromSpecialists(City.Population.GetNewSpecialists());
        var statsFromUniquesBySource = GetStatsFromUniquesBySource();
        UpdateBaseStatList(statsFromBuildings, statsFromSpecialists, statsFromUniquesBySource);
        UpdateCityHappiness(statsFromBuildings, statsFromSpecialists, statsFromUniquesBySource);
        UpdateStatPercentBonusList(currentConstruction);

        UpdateFinalStatList(currentConstruction, calculateGrowthModifiers); // again, we don't edit the existing currentCityStats directly, in order to avoid concurrency exceptions

        var newCurrentCityStats = new Stats();
        foreach (var stat in FinalStatList.Values) newCurrentCityStats.Add(stat);
        CurrentCityStats = newCurrentCityStats;

        if (updateCivStats) City.Civ.UpdateStatsForNextTurn();
    }

    private void UpdateFinalStatList(IConstruction currentConstruction, bool calculateGrowthModifiers = true)
    {
        var newFinalStatList = new StatMap(); // again, we don't edit the existing currentCityStats directly, in order to avoid concurrency exceptions

        foreach (var (key, value) in BaseStatTree.Children)
            newFinalStatList[key] = value.TotalStats.Clone();

        var statPercentBonusesSum = StatPercentBonusTree.TotalStats;

        foreach (var entry in newFinalStatList.Values)
            entry.Production *= statPercentBonusesSum.Production.ToPercent();

        // We only add the 'extra stats from production' AFTER we calculate the production INCLUDING BONUSES
        var statsFromProduction = GetStatsFromProduction(SumOfFloats(newFinalStatList.Values.Select(it => it.Production)));
        if (statsFromProduction != null && !statsFromProduction.IsEmpty())
        {
            var newTree = new StatTreeNode();
            newTree.Children.PutAll(BaseStatTree.Children);
            newTree.AddStats(statsFromProduction, "Production");
            BaseStatTree = newTree; // concurrency-safe addition
            newFinalStatList["Construction"] = statsFromProduction;
        }

        foreach (var entry in newFinalStatList.Values)
        {
            entry.Gold *= statPercentBonusesSum.Gold.ToPercent();
            entry.Culture *= statPercentBonusesSum.Culture.ToPercent();
            entry.Food *= statPercentBonusesSum.Food.ToPercent();
            entry.Faith *= statPercentBonusesSum.Faith.ToPercent();
        }

        // AFTER we've gotten all the gold stats figured out, only THEN do we plonk that gold into Science
        if (((IHasUniques)City.GetRuleset().ModOptions).HasUnique(UniqueType.ConvertGoldToScience))
        {
            float amountConverted = KotlinMath.DoubleToInt(SumOfDoubles(newFinalStatList.Values.Select(it => (double)it.Gold))
                    * City.Civ.Tech.GoldPercentConvertedToScience);
            if (amountConverted > 0) // Don't want you converting negative gold to negative science yaknow
                newFinalStatList["Gold -> Science"] = new Stats(science: amountConverted, gold: -amountConverted);
        }
        foreach (var entry in newFinalStatList.Values)
        {
            entry.Science *= statPercentBonusesSum.Science.ToPercent();
        }

        var nullifiedStats = new List<(UniqueObject Unique, Stat Stat)>();
        foreach (var pair in City.GetMatchingUniques(UniqueType.NullifiesStat).Select(it => (Unique: it, Stat: StatExtensions.ValueOf(it.Params[0]))))
            if (!nullifiedStats.Contains(pair)) nullifiedStats.Add(pair); // distinct(): by identity of the unique and the stat
        foreach (var (unique, statToBeRemoved) in nullifiedStats)
        {
            double removedAmount = SumOfDoubles(newFinalStatList.Values.Select(it => (double)it[statToBeRemoved]));

            var removed = new Stats();
            removed[statToBeRemoved] = -(float)removedAmount;
            newFinalStatList.Add(unique.GetSourceNameForUser(), removed);
        }

        /* Okay, food calculation is complicated.
        First we see how much food we generate. Then we apply production bonuses to it.
        Up till here, business as usual.
        Then, we deduct food eaten (from the total produced).
        Now we have the excess food, to which "growth" modifiers apply
        Some policies have bonuses for growth only, not general food production. */

        var foodEaten = CalcFoodEaten();
        newFinalStatList["Population"].Food -= foodEaten;

        float totalFood = SumOfFloats(newFinalStatList.Values.Select(it => it.Food));

        // Apply growth modifier only when positive food
        if (totalFood > 0 && calculateGrowthModifiers)
        {
            // Since growth bonuses are special, (applied afterwards) they will be displayed separately in the user interface as well.
            // All bonuses except We Love The King do apply even when unhappy
            var growthBonuses = GetGrowthBonus(totalFood);
            foreach (var growthBonus in growthBonuses)
            {
                newFinalStatList.Add($"[{growthBonus.Key}] ([Growth])", growthBonus.Value);
            }
            if (City.IsWeLoveTheKingDayActive() && City.Civ.GetHappiness() >= 0)
            {
                // We Love The King Day +25%, only if not unhappy
                var weLoveTheKingFood = new Stats(food: totalFood / 4);
                newFinalStatList.Add("We Love The King Day", weLoveTheKingFood);
            }
            // recalculate only when all applied - growth bonuses are not multiplicative
            // bonuses can allow a city to grow even with -100% unhappiness penalty, this is intended
            totalFood = SumOfFloats(newFinalStatList.Values.Select(it => it.Food));
        }

        var buildingsMaintenance = GetBuildingMaintenanceCosts(); // this is AFTER the bonus calculation!
        newFinalStatList["Maintenance"] = new Stats(gold: -(float)KotlinMath.FloatToInt(buildingsMaintenance));

        if (CanConvertFoodToProduction(totalFood, currentConstruction))
        {
            newFinalStatList["Excess food to production"] =
                new Stats(production: GetProductionFromExcessiveFood(totalFood), food: -totalFood);
        }

        var growthNullifyingUnique = City.GetMatchingUniques(UniqueType.NullifiesGrowth).FirstOrDefault();
        if (growthNullifyingUnique != null)
        {
            // Does not nullify negative growth (starvation)
            double currentGrowth = SumOfDoubles(newFinalStatList.Values.Select(it => (double)it[Stat.Food]));
            if (currentGrowth > 0)
                newFinalStatList.Add(
                    growthNullifyingUnique.GetSourceNameForUser(),
                    new Stats(food: -(float)currentGrowth)
                );
        }

        if (City.IsInResistance())
            newFinalStatList.Clear(); // NOPE

        if (SumOfFloats(newFinalStatList.Values.Select(it => it.Production)) < 1) // Minimum production for things to progress
            newFinalStatList["Production"] = new Stats(production: 1f);
        FinalStatList = newFinalStatList;
    }

    public bool CanConvertFoodToProduction(float food, IConstruction currentConstruction)
    {
        return food > 0
            && currentConstruction is INonPerpetualConstruction nonPerpetual
            && nonPerpetual.HasUnique(UniqueType.ConvertFoodToProductionWhenConstructed);
    }

    /// <summary>
    /// Calculate the conversion of the excessive food to production when
    /// <see cref="UniqueType.ConvertFoodToProductionWhenConstructed"/> is at front of the build queue
    /// <paramref name="food"/> is amount of excess Food generates this turn
    /// See for details: https://civilization.fandom.com/wiki/Settler_(Civ5)
    /// </summary>
    public static float GetProductionFromExcessiveFood(float food)
    {
        return food >= 4.0f ? 2.0f + KotlinMath.FloatToInt(food / 4.0f)
            : food >= 2.0f ? 2.0f
            : food >= 1.0f ? 1.0f
            : 0.0f;
    }

    private float CalcFoodEaten()
    {
        float foodEatenBySpecialists = 2f * City.Population.GetNumberOfSpecialists();
        float foodEaten = City.Population.Population * 2f - foodEatenBySpecialists;

        foodEaten += foodEatenBySpecialists;

        City.ForEachMatchingUnique(UniqueType.FoodConsumptionByPopulation, unique =>
        {
            if (City.MatchesFilter(unique.Params[2]))
            {
                float foodEatenByPopulationFilter = 2f * City.Population.GetPopulationFilterAmount(unique.Params[1]);
                foodEaten -= foodEatenByPopulationFilter * (1f - unique.Params[0].ToPercent());
            }
        });

        return foodEaten;
    }

    #endregion
}
