using Unciv.Logic.Automation.Civilization;
using Unciv.Logic.Automation.Unit;
using Unciv.Logic.City;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Nation;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Ruleset.Unit;
using Unciv.Models.StatTypes;
using Unciv.UI.Screens.VictoryScreen;
using AutomationObject = Unciv.Logic.Automation.Automation;
using CityObject = Unciv.Logic.City.City;
using CivilizationClass = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Logic.Automation.City;

// Ported from core/src/com/unciv/logic/automation/city/ConstructionAutomation.kt (T158, split from T091).
//
// Deviations:
// - timeThis profiling and the purity annotations are dropped (T027 policy).
// - Kotlin `UncivGame.Current.screen is CityScreen` (suppresses the "has started working on" notification while the city screen is open)
//   is a client-UI query the headless engine cannot make; it is treated as false.
// - The unused Kotlin field `buildingsForVictory` (computed, never read) is dropped.
// - WorkerAutomation.hasWorkableSeaResource / isNotBonusResourceOrWorkable are WorkerAutomation statics (T092).
// - NextTurnAutomation.getForeignCityNearCapital is NextTurnAutomation (T094).
// - Kotlin Sequence pipelines stay lazy LINQ pipelines (the buildable caches are filled during iteration, as in Kotlin).
// - `maxBy` on Float keys uses Kotlin's Float.compareTo total order (NaN greatest), not LINQ's.
// - Kotlin Iterable<Float>.average() accumulates in Double; an empty list gives NaN.
public class ConstructionAutomation
{
    private readonly CityConstructions cityConstructions;
    private readonly CityObject city;
    private readonly CivilizationClass civInfo;

    private readonly List<ConstructionChoice> relativeCostEffectiveness = [];
    private readonly GameContext cityState;

    private readonly Personality personality;

    private readonly List<string> constructionsToAvoid;

    private readonly Dictionary<string, bool> buildableBuildings = [];
    private readonly Dictionary<string, bool> buildableUnits = [];
    private readonly IEnumerable<Building> buildings;
    private readonly IEnumerable<Building> nonWonders;
    private readonly IEnumerable<BaseUnit> units;

    private readonly int militaryUnits;
    private readonly float workers;
    private readonly int cities;
    private readonly bool allTechsAreResearched;

    private readonly bool isAtWar;

    private readonly IReadOnlySet<string> spaceshipParts;

    private readonly bool cityIsOverAverageProduction;

    public ConstructionAutomation(CityConstructions cityConstructions)
    {
        this.cityConstructions = cityConstructions;
        city = cityConstructions.City;
        civInfo = city.Civ;
        cityState = city.State;
        personality = civInfo.GetPersonality();

        constructionsToAvoid = ((IHasUniques)personality).GetMatchingUniques(UniqueType.WillNotBuild, cityState)
            .Select(it => it.Params[0]).ToList();

        buildings = city.GetRuleset().Buildings.Values
            .Where(it => !((civInfo.IsHuman() && city.DisabledConstructions.Contains(it.Name)) || ShouldAvoidConstruction(it)));

        nonWonders = buildings.Where(it => !it.IsAnyWonder())
            .Where(it => !(buildableBuildings.TryGetValue(it.Name, out var known) && !known)); // if we already know that this building can't be built here then don't even consider it

        units = city.GetRuleset().Units.Values
            .Where(it => !((buildableUnits.TryGetValue(it.Name, out var known) && !known) // if we already know that this unit can't be built here then don't even consider it
                || (civInfo.IsHuman() && city.DisabledConstructions.Contains(it.Name)) || ShouldAvoidConstruction(it)));

        var civUnits = civInfo.Units.GetCivUnits().ToList();
        militaryUnits = civUnits.Count(it => it.BaseUnit.IsMilitary);
        workers = civUnits.Count(it => it.Cache.HasUniqueToBuildImprovements);
        cities = civInfo.Cities.Count;
        allTechsAreResearched = civInfo.Tech.AllTechsAreResearched();

        isAtWar = civInfo.IsAtWar();

        spaceshipParts = civInfo.GameInfo.SpaceResources;

        var averageProduction = Average(civInfo.Cities.Select(it => it.CityStats.CurrentCityStats.Production));
        cityIsOverAverageProduction = city.CityStats.CurrentCityStats.Production >= averageProduction;
    }

    private int TotalPopulation()
    {
        var sum = 0;
        foreach (var it in civInfo.Cities) sum = unchecked(sum + it.Population.Population);
        return sum;
    }

    private static float ToFloat(string text) => float.Parse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture);

    private static double Average(IEnumerable<float> values)
    {
        double sum = 0.0;
        int count = 0;
        foreach (var value in values)
        {
            sum += value;
            count++;
        }
        return count == 0 ? double.NaN : sum / count;
    }

    private bool ShouldAvoidConstruction(IConstruction construction)
    {
        var stateForConditionals = cityState;
        foreach (var toAvoid in constructionsToAvoid)
        {
            if (construction is Building building && building.MatchesFilter(toAvoid, stateForConditionals))
                return true;
            if (construction is BaseUnit baseUnit && baseUnit.MatchesFilter(toAvoid, stateForConditionals))
                return true;
        }
        return false;
    }

    private sealed class ConstructionChoice(IConstruction choice, float choiceModifier, int remainingWork)
    {
        public IConstruction Choice { get; } = choice;
        public float ChoiceModifier { get; } = choiceModifier;
        public int RemainingWork { get; } = remainingWork;
    }

    private void AddChoice(List<ConstructionChoice> choices, IConstruction choice, float choiceModifier)
    {
        var extraAiModifier = civInfo.IsAI() && choice is INonPerpetualConstruction nonPerpetual
            ? ((IHasUniques)nonPerpetual).GetWeightForAiDecision(new GameContext(civInfo, city))
            : 1f;
        // Kotlin also records productionForConstruction(choice.name) (never read); it is a pure query, so it is not evaluated here.
        choices.Add(new ConstructionChoice(choice, choiceModifier * extraAiModifier, cityConstructions.GetRemainingWork(choice.Name)));
    }

    private IEnumerable<T> FilterBuildable<T>(IEnumerable<T> source) where T : INonPerpetualConstruction =>
        source.Where(it =>
        {
            var cache = it is Building ? buildableBuildings : buildableUnits;
            if (!cache.ContainsKey(it.Name))
                cache[it.Name] = it.IsBuildable(cityConstructions);
            return cache[it.Name];
        });

    public void ChooseNextConstruction()
    {
        if (cityConstructions.GetCurrentConstruction() is not PerpetualConstruction) return; // don't want to be stuck on these forever

        AddBuildingChoices();

        if (!city.IsPuppet)
        {
            AddSpaceshipPartChoice();
            AddWorkerChoice();
            AddWorkBoatChoice();
            AddMilitaryUnitChoice();
        }

        IConstruction chosenConstruction;
        if (relativeCostEffectiveness.Count == 0)
        {
            // choose one of the special constructions instead
            // add science!
            if (PerpetualConstruction.Science.IsBuildable(cityConstructions) && !allTechsAreResearched) chosenConstruction = PerpetualConstruction.Science;
            else if (PerpetualConstruction.Gold.IsBuildable(cityConstructions)) chosenConstruction = PerpetualConstruction.Gold;
            else if (PerpetualConstruction.Culture.IsBuildable(cityConstructions) && !civInfo.Policies.AllPoliciesAdopted(true)) chosenConstruction = PerpetualConstruction.Culture;
            else if (PerpetualConstruction.Faith.IsBuildable(cityConstructions)) chosenConstruction = PerpetualConstruction.Faith;
            else chosenConstruction = PerpetualConstruction.LeagueProcesses.Values.FirstOrDefault(it => it.IsBuildable(cityConstructions)) // B017
                ?? (IConstruction)PerpetualConstruction.Idle;
        }
        else
        {
            chosenConstruction = MaxByScore(relativeCostEffectiveness).Choice;
        }
        // todo: All bad things are build anyways at the moment, maybe let's stop doing that and chose perpetual construction instead

        // Do not notify while in resistance (you can't do anything about it) - still notify for puppets ("annex already!")
        // Also do not notify when the decision hasn't changed - duh!
        // (Kotlin also skips the notification while the city screen is open; see the file header.)
        var noNotification = city.IsInResistance()
            || civInfo.IsAI() // Optimization: addNotification filters anyway, but saves a string builder and a CityAction instantiation
            || cityConstructions.CurrentConstructionName() == chosenConstruction.Name;
        cityConstructions.SetCurrentConstruction(chosenConstruction.Name);
        if (noNotification) return;

        civInfo.AddNotification(
            $"[{city.Name}] has started working on [{chosenConstruction.Name}]",
            CityAction.WithLocation(city),
            Notification.NotificationCategory.Production,
            NotificationIcon.Construction
        );
    }

    /// <summary>Kotlin <c>maxBy { (it.choiceModifier / it.remainingWork.coerceAtLeast(1)).coerceAtLeast(0f) }</c>: first maximum, Float.compareTo order.</summary>
    private static ConstructionChoice MaxByScore(List<ConstructionChoice> choices)
    {
        static float Score(ConstructionChoice it)
        {
            var value = it.ChoiceModifier / Math.Max(it.RemainingWork, 1);
            return value < 0f ? 0f : value; // coerceAtLeast(0f): NaN stays NaN
        }

        var best = choices[0];
        var maxValue = Score(best);
        for (int i = 1; i < choices.Count; i++)
        {
            var value = Score(choices[i]);
            if (KotlinFloatLess(maxValue, value))
            {
                best = choices[i];
                maxValue = value;
            }
        }
        return best;
    }

    /// <summary>Kotlin <c>Float.compareTo(other) &lt; 0</c> (total order: -0.0 &lt; 0.0, NaN above everything).</summary>
    private static bool KotlinFloatLess(float a, float b)
    {
        if (float.IsNaN(a)) return false;
        if (float.IsNaN(b)) return true;
        if (a < b) return true;
        if (a > b) return false;
        return a == 0f && float.IsNegative(a) && !float.IsNegative(b);
    }

    private void AddMilitaryUnitChoice()
    {
        if (!isAtWar && !cityIsOverAverageProduction) return; // don't make any military units here. Infrastructure first!
        // There is a risk however, that these cities run out of things to build, and start to construct nothing
        if (civInfo.Stats.GetUnitSupplyDeficit() > 0) return; // we don't want more units if it's already hurting our empire
        // todo: add worker disbandment and consumption of great persons if under attack & short on unit supply
        if (!isAtWar && (civInfo.Stats.StatsForNextTurn.Gold < 0 || militaryUnits > Math.Max(7, cities * 5))) return;
        if (civInfo.Gold < -50) return;

        var militaryUnit = AutomationObject.ChooseMilitaryUnit(city, units);
        if (militaryUnit == null) return;
        var unitsToCitiesRatio = cities / (float)(militaryUnits + 1);
        // most buildings and civ units contribute the the civ's growth, military units are anti-growth
        var modifier = 1 + (float)Math.Sqrt(unitsToCitiesRatio) / 2;
        if (isAtWar) modifier *= 2;

        if (AutomationObject.AfraidOfBarbarians(AutomationObject.GetCivView(civInfo))) modifier = 2f; // military units are pro-growth if pressured by barbs
        if (!cityIsOverAverageProduction) modifier /= 5; // higher production cities will deal with this

        var civilianUnit = city.GetCenterTile().CivilianUnit;
        if (civilianUnit != null && civilianUnit.HasUnique(UniqueType.FoundCity)
                && !city.GetCenterTile().GetTilesInDistance(city.GetExpandRange()).Any(it => it.MilitaryUnit?.Civ == civInfo))
            modifier = 5f; // there's a settler just sitting here, doing nothing - BAD

        if (!civInfo.IsAIOrAutoPlaying()) modifier /= 2; // Players prefer to make their own unit choices usually
        modifier *= personality.ModifierFocus(PersonalityValue.Military, .3f);
        AddChoice(relativeCostEffectiveness, militaryUnit, modifier);
    }

    private void AddWorkBoatChoice()
    {
        // Does the ruleset even have "Workboats"?
        var buildableWorkboatUnits = FilterBuildable(units
            .Where(it => ((IHasUniques)it).HasUnique(UniqueType.CreateWaterImprovements)
                && AutomationObject.AllowAutomatedConstruction(AutomationObject.GetCivView(civInfo), city, it)))
            .Distinct()
            .ToList();
        if (buildableWorkboatUnits.Count == 0) return;

        // Is there already a Workboat nearby?
        // todo Still ignores whether that boat can reach the not-yet-found tile to improve
        var twoTurnsMovement = buildableWorkboatUnits.Max(it => it.Movement) * 2;
        var alreadyHasWorkBoat = city.GetCenterTile().GetTilesInDistance(twoTurnsMovement)
            .Any(it => it.CivilianUnit is { } unit && unit.Cache.HasUniqueToCreateWaterImprovements && unit.Civ == civInfo);
        if (alreadyHasWorkBoat) return;

        // Search for a tile justifying producing a Workboat
        // todo should workboatAutomationSearchMaxTiles depend on game state?
        bool FindTileWorthImproving()
        {
            var searchMaxTiles = civInfo.GameInfo.Ruleset.ModOptions.Constants.WorkboatAutomationSearchMaxTiles;
            var bfs = new BFS(city.GetCenterTile(), it =>
                (it.IsWater || it.IsCityCenter())
                && (it.GetOwner() == null || it.IsFriendlyTerritory(civInfo))
                && it.IsExplored(civInfo)); // Sending WB's through unexplored terrain would be cheating
            do
            {
                var tile = bfs.NextStep();
                if (tile == null) break;
                // Define what makes a tile worth sending a Workboat to
                // todo Prepare for mods that allow improving water tiles without a resource?
                if (ReferenceEquals(tile.GetOwner(), civInfo)
                    && WorkerAutomation.HasWorkableSeaResource(tile, civInfo)
                    && WorkerAutomation.IsNotBonusResourceOrWorkable(tile, civInfo)) return true;
            } while (bfs.Size() < searchMaxTiles);
            return false;
        }

        if (!FindTileWorthImproving()) return;

        AddChoice(relativeCostEffectiveness, buildableWorkboatUnits.OrderBy(it => it.Cost).First(), 6f); // Improving coastal luxuries etc. is quite important
    }

    private void AddWorkerChoice()
    {
        var workerEquivalents = FilterBuildable(units
            .Where(it => ((IHasUniques)it).HasUnique(UniqueType.BuildImprovements)
                && AutomationObject.AllowAutomatedConstruction(AutomationObject.GetCivView(civInfo), city, it)));
        if (!workerEquivalents.Any()) return; // for mods with no worker units

        // Dedicate 1 worker for the first city (CS), then 1.5 workers for the first 5 cities, from then on build one more worker for every city.
        var numberOfWorkersWeWant = cities <= 1 ? 1f : cities <= 5 ? cities * 1.8f : 9f + 1.3f * (cities - 5);

        if (workers < numberOfWorkersWeWant)
        {
            var modifier = numberOfWorkersWeWant / (workers + 0.17f); // The worse our worker to city ratio is, the more desperate we are
            AddChoice(relativeCostEffectiveness, workerEquivalents.OrderBy(it => it.Cost).First(), modifier);
        }
    }

    private void AddSpaceshipPartChoice()
    {
        if (!cityIsOverAverageProduction) return; // don't waste time building them in low-production cities
        if (!civInfo.HasUnique(UniqueType.EnablesConstructionOfSpaceshipParts)) return;
        var spaceshipPart = FilterBuildable(nonWonders.Cast<INonPerpetualConstruction>().Concat(units)
            .Where(it => spaceshipParts.Contains(it.Name))).FirstOrDefault();
        if (spaceshipPart == null) return;
        var modifier = 20f; //We're weighing Apollo program according to personality. If we decided to invest in that, we might as well commit to it.
        AddChoice(relativeCostEffectiveness, spaceshipPart, modifier);
    }

    private void AddBuildingChoices()
    {
        var cityBaseline = ComputeCityBaseline();
        foreach (var building in FilterBuildable(buildings))
        {
            if (building.IsWonder && city.IsPuppet) continue;
            // We shouldn't try to build wonders in undeveloped cities and empires
            if (building.IsWonder && (!cityIsOverAverageProduction || TotalPopulation() < 12)) continue;
            AddChoice(relativeCostEffectiveness, building, GetValueOfBuilding(building, cityBaseline));
        }
    }

    private float GetValueOfBuilding(Building building, Stats cityBaseline)
    {
        var value = 0f;
        value += ApplyBuildingStats(building, cityBaseline);
        value += GetMilitaryBuildingValue(building);
        value += GetVictoryBuildingValue(building);
        value += GetOnetimeUniqueBonuses(building);
        return value;
    }

    private float GetOnetimeUniqueBonuses(Building building)
    {
        var value = 0f;
        var uniques = (IHasUniques)building;
        if (building.IsWonder)
        {
            // Buildings generally don't have these uniques, and Wonders generally only one of these, so we can save some time by not checking every building for every unique
            if (!building.IsNationalWonder)
                value -= civInfo.GameInfo.GetAliveMajorCivs().OrderByDescending(it => it.GetStatForRanking(RankingType.Technologies)).ToList().IndexOf(civInfo);
            // Wonders are a one-time occurence: value less if someone is going to build them before us anyways
            if (uniques.HasUnique(UniqueType.OneTimeFreePolicy) || uniques.HasUnique(UniqueType.OneTimeAmountFreePolicies)) value += civInfo.GetPersonality().Culture;
            else if (uniques.HasUnique(UniqueType.OneTimeFreeTech) || uniques.HasUnique(UniqueType.OneTimeAmountFreeTechs)) value += civInfo.GetPersonality().Science;
            else if (uniques.HasUnique(UniqueType.OneTimeAmountFreeUnits) || uniques.HasUnique(UniqueType.OneTimeFreeUnit)) value += civInfo.GetPersonality().Production; //Pyramids, Louvre
            else if (uniques.HasUnique(UniqueType.OneTimeFreeGreatPerson)) value += civInfo.GetPersonality().Science; // Will pick scientist
            else if (uniques.HasUnique(UniqueType.OneTimeEnterGoldenAge) || uniques.HasUnique(UniqueType.GoldenAgeLength) || uniques.HasUnique(UniqueType.OneTimeEnterGoldenAgeTurns)) value += civInfo.GetPersonality().Expansion; // Relatively more important on many cities
            else if (uniques.HasUnique(UniqueType.EnemyUnitsSpendExtraMovement)) value += civInfo.GetPersonality().DeclareWar;
            else if (uniques.HasUnique(UniqueType.OneTimeGainPopulation) || uniques.HasUnique(UniqueType.OneTimeGainPopulationRandomCity)) value += civInfo.GetPersonality().Food;
            else if (uniques.HasUnique(UniqueType.StatPercentFromTradeRoutes)) value += civInfo.GetPersonality().Gold;
            else if (uniques.HasUnique(UniqueType.Strength)) value += civInfo.GetPersonality().Military;
            else if (uniques.HasUnique(UniqueType.StatPercentBonusCities)) value += civInfo.GetPersonality().Culture; // Sistine Chapel in base game, but players seem to "expect" culture civs to build more wonders in general
        }
        else if (uniques.HasUnique(UniqueType.CreatesOneImprovement)) value += 5f; // District-type buildings, should be weighed by the stats (incl. adjacencies) of the improvement
        else if (uniques.HasUnique(UniqueType.ProvidesResources)) value += 3f; // Should be weighed by how much we need the resources
        else if (uniques.HasUnique(UniqueType.StatPercentFromObjectToResource)) value += 1.5f; // Should be weighed by the amount of active improvementFilter/buildingFilter in the city
        else if (building.RequiredResource != null && civInfo.GameInfo.SpaceResources.Contains(building.RequiredResource)) value += -4f; // This may need to be reverted when resource management is bugfixed elsewhere
        return value;
    }

    private float GetVictoryBuildingValue(Building building)
    {
        var value = 0f;
        if (!cityIsOverAverageProduction) return value;
        var uniques = (IHasUniques)building;
        if (uniques.HasUnique(UniqueType.TriggersCulturalVictory)
            || uniques.HasUnique(UniqueType.TriggersVictory)) value += 20f; // if we're this close to actually winning, we don't care what your preferred victory type is
        if (uniques.HasUnique(UniqueType.EnablesConstructionOfSpaceshipParts)) value += 10f * personality.ModifierFocus(PersonalityValue.Science, .3f);
        return value;
    }

    private float GetMilitaryBuildingValue(Building building)
    {
        var value = 0f;
        var warModifier = isAtWar ? 1f : .5f;
        // If this city is the closest city to another civ, that makes it a likely candidate for attack
        if (civInfo.GetKnownCivs()
                .Select(it => NextTurnAutomation.GetForeignCityNearCapital(it.GetCapital(), civInfo))
                .OfType<NextTurnAutomation.CityDistance>()
                .Any(it => ReferenceEquals(it.City, city)))
            warModifier *= 2f;
        value += warModifier * building.CityHealth / city.GetMaxHealth() * personality.InverseModifierFocus(PersonalityValue.Aggressive, .3f);
        value += warModifier * (float)building.CityStrength / (city.GetStrength() + 3) * personality.InverseModifierFocus(PersonalityValue.Aggressive, .3f); // The + 3 here is to reduce the priority of building walls immedietly

        var uniques = (IHasUniques)building;
        uniques.ForEachMatchingUnique(UniqueType.UnitStartingExperience, cityState, experienceUnique =>
        {
            var modifier = ToFloat(experienceUnique.Params[1]) / 5;
            modifier *= cityIsOverAverageProduction ? 1f : 0.2f; // You shouldn't be cranking out units anytime soon
            modifier *= personality.ModifierFocus(PersonalityValue.Military, 0.3f);
            modifier *= Math.Max(personality.ModifierFocus(PersonalityValue.Aggressive, 0.2f), 1f); // Defensive civs can still want a good military
            value += modifier;
        });
        if (uniques.HasUnique(UniqueType.EnablesNuclearWeapons) && !civInfo.HasUnique(UniqueType.EnablesNuclearWeapons))
            value += 10f * personality.ModifierFocus(PersonalityValue.Military, 0.3f);
        return value;
    }

    private float ApplyBuildingStats(Building building, Stats cityBaseline)
    {
        var buildingStats = GetStatDifferenceFromBuilding(building.Name, cityBaseline);
        buildingStats.Add(GetBuildingStatsFromUniques(building, buildingStats));

        buildingStats.Food *= 3;

        buildingStats.Production *= 2;

        buildingStats.Gold *= 2; // Everything's weighed by rankStatsValue, which ranks gold at 0.3, let's make that 0.6 (vs Science being 1)

        if (civInfo.GetHappiness() < 10 || civInfo.GetHappiness() < civInfo.Cities.Count)
            buildingStats.Happiness *= 3;

        if (city.CityStats.CurrentCityStats.Culture < 2)
            buildingStats.Culture *= 2; // We need to start growing borders

        foreach (var stat in Enum.GetValues<Stat>())
            buildingStats[stat] *= personality.ScaledFocus(PersonalityValueExtensions.FromStat(stat));

        return AutomationObject.RankStatsValue(buildingStats.Clone(), AutomationObject.GetCivView(civInfo));
    }

    // NOT readonly safe, since it alters the tile ownership of real tiles
    private Stats ComputeCityBaseline()
    {
        var baselineCity = city.Clone();
        baselineCity.SetTransients(city.Civ); // Will break the owned tiles. Needs to be reverted before leaving this function
        //todo: breaks city connection; trade route gold is currently not considered for markets etc.
        baselineCity.CityStats.Update(updateCivStats: false, calculateGrowthModifiers: false); // Don't consider growth penalties for food values (we can work more mines/specialists instead of farms)
        var baseline = baselineCity.CityStats.CurrentCityStats;
        city.Expansion.SetTransients(); // Revert owned tiles to original city
        return baseline;
    }

    // NOT readonly safe, since it alters the tile ownership of real tiles
    private Stats GetStatDifferenceFromBuilding(string building, Stats cityBaseline)
    {
        var newCity = city.Clone();
        newCity.SetTransients(city.Civ); // Will break the owned tiles. Needs to be reverted before leaving this function
        newCity.CityConstructions.BuiltBuildings.Add(building);
        newCity.CityConstructions.SetTransients();
        newCity.CityStats.Update(updateCivStats: false, calculateGrowthModifiers: false);
        city.Expansion.SetTransients(); // Revert owned tiles to original city
        return newCity.CityStats.CurrentCityStats - cityBaseline;
    }

    private Stats GetBuildingStatsFromUniques(Building building, Stats buildingStats)
    {
        var stats = new Stats();
        ((IHasUniques)building).ForEachMatchingUnique(UniqueType.StatPercentBonusCities, cityState, unique =>
        {
            var statType = Enum.Parse<Stat>(unique.Params[1]);
            var relativeAmount = ToFloat(unique.Params[0]) / 100f;
            var amount = civInfo.Stats.StatsForNextTurn[statType] * relativeAmount;
            stats[statType] += amount;
        });

        ((IHasUniques)building).ForEachMatchingUnique(UniqueType.CarryOverFood, cityState, unique =>
        {
            if (city.MatchesFilter(unique.Params[1]) && int.Parse(unique.Params[0], System.Globalization.CultureInfo.InvariantCulture) != 0)
            {
                var foodGain = city.CityStats.CurrentCityStats.Food + buildingStats.Food;
                var relativeAmount = ToFloat(unique.Params[0]) / 100f;
                stats[Stat.Food] += foodGain * relativeAmount; // Essentialy gives us the food per turn this unique saves us
            }
        });
        return stats;
    }
}
