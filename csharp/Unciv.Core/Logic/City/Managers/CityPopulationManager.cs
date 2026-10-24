using System.Text.Json.Serialization;
using Unciv.Json;
using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Transients;
using Unciv.Logic.Map;
using Unciv.Models;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.StatTypes;
using Unciv.Utils;
using CityObject = Unciv.Logic.City.City;
using NotificationCategory = Unciv.Logic.Civilization.Notification.NotificationCategory;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Logic.City.Managers;

// Ported from core/src/com/unciv/logic/city/managers/CityPopulationManager.kt (T061).
//
// Stripped: the purity annotations and the timeThis profiling wrapper of autoAssignPopulation.
// Automation.rankStatsForCityWork / rankSpecialist / rankTileForCityWork go through CityPopulationManager.Automation (CityManagerSeams, default: the real Automation, T087). Kotlin `getNewSpecialists()` is an alias of specialistAllocations and is kept as such.
// The members tests override (AutoAssignPopulation, UnassignExtraPopulation, GetPopulationFilterAmount) are virtual so that City tests can
// count the calls without a full map and automation.

public class CityPopulationManager : IsPartOfGameInfoSerialization
{
    private CityObject? city;
    /// <summary>Kotlin <c>@Transient lateinit var</c>, set by <c>City.setTransients</c>: reading before assignment throws.</summary>
    [JsonIgnore]
    public CityObject City
    {
        get => city ?? throw new InvalidOperationException("lateinit property city has not been initialized");
        set => city = value;
    }

    /// <summary>Kotlin <c>var population = 1; private set</c>. Internal setter: engine tests set it without running addPopulation.</summary>
    [JsonPropertyName("population")]
    [JsonInclude]
    public int Population { get; internal set; } = 1;
    [JsonPropertyName("foodStored")]
    public int FoodStored { get; set; }

    [JsonPropertyName("specialistAllocations")]
    [JsonInclude]
    public Counter<string> SpecialistAllocations { get; private set; } = new();

    /// <summary>The <c>Automation</c> ranking calls (T087): the ported object unless a test installs a stand-in. Not serialized, not cloned.</summary>
    [JsonIgnore]
    internal ICityWorkAutomation Automation
    {
        get => automation ?? CityManagerSeams.RealCityWork;
        set => automation = value;
    }
    private ICityWorkAutomation? automation;

    public Counter<string> GetNewSpecialists() => SpecialistAllocations;

    #region pure functions
    public CityPopulationManager Clone()
    {
        var toReturn = new CityPopulationManager();
        toReturn.SpecialistAllocations.Add(SpecialistAllocations);
        toReturn.Population = Population;
        toReturn.FoodStored = FoodStored;
        return toReturn;
    }

    public int GetNumberOfSpecialists() => GetNewSpecialists().SumValues();

    public virtual int GetFreePopulation()
    {
        var workingPopulation = City.GetWorkedTiles().Count();
        return Population - workingPopulation - GetNumberOfSpecialists();
    }

    public int GetFoodToNextPopulation()
    {
        // civ v math, civilization.wikia
        double foodRequired = 15 + 8 * (Population - 1) + Math.Floor(Math.Pow((double)(Population - 1), 1.5));

        foodRequired *= City.Civ.GameInfo.Speed.Modifier;

        if (City.Civ.IsCityState)
            foodRequired *= 1.5f;
        if (!City.Civ.IsHuman())
            foodRequired *= City.Civ.GameInfo.GetDifficulty().AiCityGrowthModifier;
        return KotlinMath.DoubleToInt(foodRequired);
    }

    /// <summary>Take null to mean infinity.</summary>
    public int? GetNumTurnsToStarvation()
    {
        if (!City.IsStarving()) return null;
        return FoodStored / -City.FoodForNextTurn() + 1;
    }

    /// <summary>Take null to mean infinity.</summary>
    public int? GetNumTurnsToNewPopulation()
    {
        if (!City.IsGrowing()) return null;
        var roundedFoodPerTurn = (float)City.FoodForNextTurn();
        var remainingFood = GetFoodToNextPopulation() - FoodStored;
        var turnsToGrowth = KotlinMath.FloatToInt((float)Math.Ceiling(remainingFood / roundedFoodPerTurn));
        if (turnsToGrowth < 1) turnsToGrowth = 1;
        return turnsToGrowth;
    }
    #endregion

    /// <summary>Implements <c>UniqueParameterType.PopulationFilter</c></summary>
    public virtual int GetPopulationFilterAmount(string filter)
    {
        switch (filter)
        {
            case "Specialists": return GetNumberOfSpecialists();
            case "Population": return Population;
            case "Followers of the Majority Religion":
            case "Followers of this Religion":
                return City.Religion.GetFollowersOfMajorityReligion();
            case "Unemployed": return GetFreePopulation();
            default: return SpecialistAllocations[filter];
        }
    }

    public void NextTurn(int food)
    {
        FoodStored += food;
        if (food < 0)
            City.Civ.AddNotification($"[{City.Name}] is starving!",
                City.Location, NotificationCategory.Cities, NotificationIcon.Growth, NotificationIcon.Death);
        if (FoodStored < 0)
        {        // starvation!
            if (Population > 1) AddPopulation(-1);
            FoodStored = 0;
        }
        var foodNeededToGrow = GetFoodToNextPopulation();
        if (FoodStored < foodNeededToGrow) return;

        // What if the stores are already over foodNeededToGrow but NullifiesGrowth is in effect?
        // We could simply test food==0 - but this way NullifiesStat(food) will still allow growth:
        if (City.GetMatchingUniques(UniqueType.NullifiesGrowth).Any())
            return;

        // Hard block growth when using Avoid Growth, cap stored food
        if (City.AvoidGrowth)
        {
            FoodStored = foodNeededToGrow;
            return;
        }

        // growth!
        FoodStored -= foodNeededToGrow;
        var percentOfFoodCarriedOver = 0;
        City.ForEachMatchingUnique(UniqueType.CarryOverFood, unique =>
        {
            if (City.MatchesFilter(unique.Params[1]))
                percentOfFoodCarriedOver += unique.Params[0].ToInt();
        });
        percentOfFoodCarriedOver = Math.Min(percentOfFoodCarriedOver, 95);  // Try to avoid runaway food gain in mods, just in case
        FoodStored += KotlinMath.FloatToInt(foodNeededToGrow * percentOfFoodCarriedOver / 100f);
        AddPopulation(1);
        City.ShouldReassignPopulation = true;
        City.Civ.AddNotification($"[{City.Name}] has grown!", City.Location,
            NotificationCategory.Cities, NotificationIcon.Growth);
    }

    public void AddPopulation(int count)
    {
        var changedAmount = Math.Max(count, 1 - Population);
        Population += changedAmount;
        var freePopulation = GetFreePopulation();
        if (freePopulation < 0)
        {
            UnassignExtraPopulation();
            City.CityStats.Update();
        }
        else
        {
            AutoAssignPopulation();
        }

        if (City.Civ.GameInfo.IsReligionEnabled())
            City.Religion.UpdatePressureOnPopulationChange(changedAmount);
    }

    public void SetPopulation(int count) => AddPopulation(-Population + count);

    /// <summary>Only assigns free population</summary>
    internal virtual void AutoAssignPopulation()
    {
        City.CityStats.Update();  // calculate current stats with current assignments
        var freePopulation = GetFreePopulation();
        if (freePopulation <= 0) return;

        var cityStats = City.CityStats.CurrentCityStats;
        City.CurrentGPPBonus = City.GetGreatPersonPercentageBonus();  // pre-calculate for use in Automation.rankSpecialist
        float specialistFoodBonus = 2f;  // See CityStats.calcFoodEaten()
        specialistFoodBonus = 2f - specialistFoodBonus;

        var tilesToEvaluate = City.GetWorkableTiles()
            .Where(it => !it.IsBlockaded()).ToList();

        // Calculate stats once - but the *ranking of those stats* is dynamic and depends on what the city needs
        var tileStats = new Dictionary<TileClass, Stats>();
        foreach (var tile in tilesToEvaluate.Where(it => !it.ProvidesYield()))
            tileStats[tile] = tile.Stats.GetTileStats(City, City.Civ);

        var maxSpecialists = GetMaxSpecialists().ToList();

        for (var repetition = 0; repetition < freePopulation; repetition++)
        {
            //evaluate tiles
            // We need to make sure that we work the same tiles as last turn on a tile
            // so that our workers know to prioritize this tile and don't move to the other tile
            // This was just the easiest way I could think of.
            TileClass? bestTile = null;
            var bestTileRank = 0f;
            foreach (var tile in tilesToEvaluate.Where(it => !it.ProvidesYield())) // Changes with every tile assigned
            {
                var rank = Automation.RankStatsForCityWork(tileStats[tile], City, false);
                if (bestTile == null || CompareTileRank(bestTileRank, bestTile, rank, tile) < 0)
                {
                    bestTile = tile;
                    bestTileRank = rank;
                }
            }
            var valueBestTile = bestTile != null ? bestTileRank : 0f;

            string? bestJob = null;
            var valueBestSpecialist = 0f;
            if (!City.ManualSpecialists)
            {
                foreach (var job in maxSpecialists.Where(it => SpecialistAllocations[it.Key] < it.Value).Select(it => it.Key))
                {
                    var rank = Automation.RankSpecialist(job, City);
                    if (bestJob == null || AStar.JavaFloatCompare(valueBestSpecialist, rank) < 0)
                    {
                        bestJob = job;
                        valueBestSpecialist = rank;
                    }
                }
            }

            //assign population
            if (valueBestTile > valueBestSpecialist)
            {
                if (bestTile != null)
                {
                    City.WorkTile(bestTile);
                    cityStats.Food += tileStats[bestTile].Food;
                }
            }
            else if (bestJob != null)
            {
                SpecialistAllocations.Add(bestJob, 1);
                cityStats.Food += specialistFoodBonus;
            }
        }
        City.CityStats.Update();
    }

    /// <summary>Kotlin <c>compareBy({ it.value }, { it.key.longitude }, { it.key.latitude })</c> of two ranked tiles.</summary>
    private static int CompareTileRank(float rankA, TileClass tileA, float rankB, TileClass tileB)
    {
        var result = AStar.JavaFloatCompare(rankA, rankB); // Float.compareTo (compareBy on Float values)
        if (result != 0) return result;
        result = tileA.Longitude.CompareTo(tileB.Longitude);
        if (result != 0) return result;
        return tileA.Latitude.CompareTo(tileB.Latitude);
    }

    public virtual void UnassignExtraPopulation()
    {
        foreach (var tile in City.GetWorkedTiles().ToList())
        {
            if (!ReferenceEquals(tile.GetOwner(), City.Civ) || !ReferenceEquals(tile.GetWorkingCity(), City)
                    || tile.AerialDistanceTo(City.GetCenterTile()) > City.GetWorkRange())
                City.StopWorkingTile(tile);
        }

        // unassign specialists that cannot be (e.g. the city was captured and one of the specialist buildings was destroyed)
        foreach (var (specialistName, maxAmount) in GetMaxSpecialists().ToList())
            if (SpecialistAllocations[specialistName] > maxAmount)
                SpecialistAllocations[specialistName] = maxAmount;

        while (GetFreePopulation() < 0)
        {
            //evaluate tiles
            TileClass? worstWorkedTile = null;
            var worstTileRank = 0f;
            foreach (var tile in City.GetWorkedTiles())
            {
                var rank = Automation.RankTileForCityWork(tile, City) +
                    (tile.IsLocked() ? 10 : 0);
                if (worstWorkedTile == null || AStar.JavaFloatCompare(worstTileRank, rank) > 0)
                {
                    worstWorkedTile = tile;
                    worstTileRank = rank;
                }
            }
            var valueWorstTile = worstWorkedTile == null ? 0f
                : Automation.RankTileForCityWork(worstWorkedTile, City);

            //evaluate specialists
            string? worstAutoJob = City.ManualSpecialists ? null : MinSpecialist();
            var valueWorstSpecialist = 0f;
            if (worstAutoJob != null)
                valueWorstSpecialist = Automation.RankSpecialist(worstAutoJob, City);

            // un-assign population
            if (worstAutoJob != null && worstWorkedTile != null)
            {
                // choose between removing a specialist and removing a tile
                if (valueWorstTile < valueWorstSpecialist)
                    City.StopWorkingTile(worstWorkedTile);
                else
                    SpecialistAllocations.Add(worstAutoJob, -1);
            }
            else if (worstAutoJob != null)
                SpecialistAllocations.Add(worstAutoJob, -1);
            else if (worstWorkedTile != null)
                City.StopWorkingTile(worstWorkedTile);
            else
            {
                // It happens when "city.manualSpecialists == true"
                //  and population goes below the number of specialists, e.g. city is razing.
                // Let's give a chance to do the work automatically at least.
                var worstJob = MinSpecialist();
                if (worstJob == null) break; // sorry, we can do nothing about that
                SpecialistAllocations.Add(worstJob, -1);
            }
        }
    }

    /// <summary><c>specialistAllocations.keys.minByOrNull { Automation.rankSpecialist(it, city) }</c>: the first minimal key.</summary>
    private string? MinSpecialist()
    {
        string? worst = null;
        var worstRank = 0f;
        foreach (var key in SpecialistAllocations.Keys.ToList())
        {
            var rank = Automation.RankSpecialist(key, City);
            if (worst == null || AStar.JavaFloatCompare(worstRank, rank) > 0)
            {
                worst = key;
                worstRank = rank;
            }
        }
        return worst;
    }

    public Counter<string> GetMaxSpecialists()
    {
        var counter = new Counter<string>();
        foreach (var building in City.CityConstructions.GetBuiltBuildings())
            counter.Add(building.NewSpecialists());
        return counter;
    }
}
