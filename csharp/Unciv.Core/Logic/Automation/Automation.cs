using System.Globalization;
using Unciv.Logic.City;
using Unciv.Logic.Map;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Ruleset.Unit;
using Unciv.Models.StatTypes;
using Unciv.UI.Screens.VictoryScreen;
using Unciv.Utils;
using Unciv.View;
using CityObject = Unciv.Logic.City.City;
using CivilizationClass = Unciv.Logic.Civilization.Civilization;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Logic.Automation;

// Ported from core/src/com/unciv/logic/automation/Automation.kt (T087). Timers.kt (profiling) is dropped, so timeThis wrappers are
// plain bodies; purity annotations dropped (T027 policy).
// Callers inside namespace Unciv.Logic.* must alias the class (`using AutomationObject = Unciv.Logic.Automation.Automation;`),
// because the namespace of the same name wins name lookup there.
// Kotlin `hashMapOf<String, BaseUnit>()` (chooseMilitaryUnit) is a JavaHashMap: String keys iterate in a reproducible JVM bucket order.

public static class Automation
{
    /// <summary>Kotlin <c>GameView(civ.gameInfo, civ).civView</c>. The civ's game is always the concrete <see cref="GameInfo"/>.</summary>
    internal static CivView GetCivView(CivilizationClass civ) => new GameView((GameInfo)civ.GameInfo, civ).CivView;

    public static float RankTileForCityWork(TileClass tile, CityObject city)
    {
        var stats = tile.Stats.GetTileStats(city, city.Civ);
        return RankStatsForCityWork(stats, city, false);
    }

    public static float RankSpecialist(string specialist, CityObject city)
    {
        var stats = city.CityStats.GetStatsOfSpecialist(specialist);
        var rank = RankStatsForCityWork(stats, city, true);
        // derive GPP score
        var gpp = 0f;
        if (city.GetRuleset().Specialists.TryGetValue(specialist, out var specialistInfo)) // To solve problems in total remake mods
            gpp = specialistInfo.GreatPersonPoints.SumValues();
        gpp = gpp * (100 + city.CurrentGPPBonus) / 100;
        rank += gpp; // GPP weight
        return rank;
    }

    // More complicated logic to properly weigh Food vs other Stats (esp Production)
    private static float ParseFloat(string text) => float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture); // Kotlin String.toFloat()

    private static float GetFoodModWeight(CityObject city, float surplusFood)
    {
        var speed = city.Civ.GameInfo.Speed.Modifier;
        // Zero out Growth if close to Unhappiness limit
        if (city.Civ.GetHappiness() < -8)
            return 0f;
        if (city.Civ.IsAI())
        {
            // Value 1 Growth at less than 2 Production, but above 1 Production + 1 Gold
            return 1.5f;
        }
        // Human weights. May be different since AI Happiness is always "easier"
        // Only apply these for Default to not interfere with Focus weights
        if (city.GetCityFocus() == CityFocus.NoFocus)
        {
            if (city.Population.Population < 5)
                return 2f;
            if (surplusFood > city.Population.GetFoodToNextPopulation() / (10 * speed))
                return 0.75f; // get Growth just under Production
        }

        return 1f;
    }

    private static void ApplySpecialistAdjustments(Stats yieldStats, CityObject city)
    {
        // Specialist Happiness Percentage Change 0f-1f
        city.ForEachMatchingUnique(UniqueType.UnhappinessFromPopulationTypePercentageChange, city.State, unique =>
        {
            if (unique.Params[1] == "Specialists" && city.MatchesFilter(unique.Params[2]))
                yieldStats.Happiness -= ParseFloat(unique.Params[0]) / 100f; // relative val is negative, make positive
        });
        if (yieldStats.Science == 3f || yieldStats.Science >= 5f)
            yieldStats.Science *= 1.3f;
    }

    public static float RankStatsForCityWork(Stats stats, CityObject city, bool areWeRankingSpecialist)
    {
        var cityAIFocus = city.GetCityFocus();
        var yieldStats = stats.Clone();
        var cityStatsObj = city.CityStats;
        var civInfo = city.Civ;
        var allTechsAreResearched = civInfo.Tech.AllTechsAreResearched();

        if (areWeRankingSpecialist)
            ApplySpecialistAdjustments(yieldStats, city);

        var surplusFood = city.CityStats.CurrentCityStats[Stat.Food];
        var starving = surplusFood < 0;
        // If current Production converts Food into Production, then calculate increased Production Yield
        if (cityStatsObj.CanConvertFoodToProduction(surplusFood, city.CityConstructions.GetCurrentConstruction()))
        {
            // calculate delta increase of food->prod. This isn't linear
            yieldStats.Production += CityStats.GetProductionFromExcessiveFood(surplusFood + yieldStats.Food) - CityStats.GetProductionFromExcessiveFood(surplusFood);
            yieldStats.Food = 0f; // all food goes to 0
        }

        // Split Food Yield into feedFood, amount needed to not Starve
        // and growthFood, any amount above that
        var feedFood = 0f;
        if (starving)
            feedFood = Math.Max(Math.Min(yieldStats.Food, -surplusFood), 0f);
        var growthFood = yieldStats.Food - feedFood; // how much extra Food we yield
        // Avoid Growth, only count Food that gets you not-starving, but no more
        if (city.AvoidGrowth)
        {
            growthFood = 0f;
        }
        yieldStats.Food = 1f;

        // Apply base weights
        yieldStats.ApplyRankingWeights();

        var foodBaseWeight = yieldStats.Food;

        // If starving, need Food, so feedFood > 0
        // scale feedFood by 14(base weight)*8(super important)
        // By only scaling what we need to reach Not Starving by x8, we can pick a tile that gives
        // exactly as much Food as we need to Not Starve that also has other good yields instead of
        // always picking the Highest Food tile until Not Starving
        yieldStats.Food = feedFood * (foodBaseWeight * 8);
        // growthFood is any additional food not required to meet Starvation

        // Growth is penalized when Unhappy, see GlobalUniques.json
        // No Growth if <-10, 1/4 if <0
        // Reusing food growth code from CityStats.updateFinalStatList()
        var growthNullifyingUnique = false;
        city.ForEachMatchingUnique(UniqueType.NullifiesGrowth, city.State, _ =>
        {
            growthNullifyingUnique = true;
        });
        if (!growthNullifyingUnique)
        {
            var newGrowthFood = growthFood; // running count of growthFood
            var cityStats = new CityStats(city);
            var growthBonuses = cityStats.GetGrowthBonus(growthFood);
            foreach (var growthBonus in growthBonuses)
            {
                newGrowthFood += growthBonus.Value.Food;
            }
            if (city.IsWeLoveTheKingDayActive() && city.Civ.GetHappiness() >= 0)
            {
                newGrowthFood += growthFood / 4;
            }
            newGrowthFood = Math.Max(newGrowthFood, 0f); // floor to 0 for safety

            yieldStats.Food += newGrowthFood * foodBaseWeight * GetFoodModWeight(city, surplusFood);
        }

        if (city.Population.Population < 10)
        {
            // "small city" - we care more about food and less about global problems like gold science and culture
            // Food already handled above. Gold/Culture have low weights in Stats already
            yieldStats.Science /= 2;
        }

        if (city.Civ.Stats.StatsForNextTurn.Gold < 0)
        {
            // We have a global problem, we need to deal with it before it leads to science loss
            yieldStats.Gold *= 2;
        }

        if (city.Civ.GetHappiness() < 5)
        {
            yieldStats.Happiness *= 2;
        }

        if (allTechsAreResearched)
        {
            // Science is useless at this point
            yieldStats.Science *= 0;
        }

        if (city.CityConstructions.GetCurrentConstruction() is PerpetualConstruction)
        {
            // With 4:1 conversion of production to science, production is overvalued by a factor (12*4)/7 = 6.9
            yieldStats.Production /= 6;
        }

        // Apply City focus
        foreach (var stat in cityAIFocus.StatValuesForFocus())
        {
            var currentStat = yieldStats[stat];
            if (currentStat == 0f) continue;
            var statMultiplier = cityAIFocus.GetStatMultiplier(stat);
            yieldStats[stat] = currentStat * statMultiplier;
        }

        return yieldStats.Sum();
    }

    public static void TryTrainMilitaryUnit(CityObject city)
    {
        if (city.IsPuppet) return;
        if ((city.CityConstructions.GetCurrentConstruction() as BaseUnit)?.IsMilitary == true)
            return; // already training a military unit
        var chosenUnit = ChooseMilitaryUnit(city, city.Civ.GameInfo.Ruleset.Units.Values);
        if (chosenUnit != null)
            city.CityConstructions.SetCurrentConstruction(chosenUnit.Name);
    }

    public static BaseUnit? ChooseMilitaryUnit(CityObject city, IEnumerable<BaseUnit> availableUnits)
    {
        var rng = city.State.StateBasedRandom("Automation.chooseMilitaryUnit");
        var currentChoice = city.CityConstructions.GetCurrentConstruction();
        if (currentChoice is BaseUnit currentUnit && !currentUnit.IsCivilian()) return currentUnit;

        // if not coastal, removeShips == true so don't even consider ships
        var removeShips = true;
        var isMissingNavalUnitsForCityDefence = false;

        static bool IsNavalMeleeUnit(BaseUnit unit) => unit.IsMelee() && unit.Type.IsWaterUnit();
        if (city.IsCoastal())
        {
            // in the future this could be simplified by assigning every distinct non-lake body of
            // water their own ID like a continent ID
            var findWaterConnectedCitiesAndEnemies =
                new BFS(city.GetCenterTile(), it => it.IsWater && !it.IsImpassible() || it.IsCityCenter());
            findWaterConnectedCitiesAndEnemies.StepToEnd();

            var reachedTiles = findWaterConnectedCitiesAndEnemies.GetReachedTiles().ToList();
            var numberOfOurConnectedCities = reachedTiles
                .Count(it => it.IsCityCenter() && it.GetOwner() == city.Civ);
            var numberOfOurNavalMeleeUnits = reachedTiles
                .Sum(it => it.GetUnits().Count(unit => IsNavalMeleeUnit(unit.BaseUnit)));

            isMissingNavalUnitsForCityDefence = numberOfOurConnectedCities > numberOfOurNavalMeleeUnits;

            removeShips = !reachedTiles.Any(it =>
                (it.IsCityCenter() && it.GetOwner() != city.Civ)
                || (it.MilitaryUnit != null && it.MilitaryUnit.Civ != city.Civ)); // there is absolutely no reason for you to make water units on this body of water.
        }

        var civView = GetCivView(city.Civ);
        var militaryUnits = availableUnits
            .Where(it => it.IsMilitary)
            .Where(it => !(removeShips && it.IsWaterUnit))
            .Where(it => AllowSpendingResource(civView, it))
            // units that can't attack may be very strong meatshields, but current AI
            // has trouble using them properly, and this conveniently filters out aircraft carriers
            // (they're kinda useless compared to investing in battleships or extended-range bombers,
            // and are obsoleted at Stealth. Excluding units based on their carrying slots being
            // not needed filters out nuclear submarines and missile cruisers, which is not correct)
            .Where(it => !((IHasUniques)it).HasUnique(UniqueType.CannotAttack))
            // Only now do we filter out the constructable units because that's a heavier check
            .Where(it => it.IsBuildable(city.CityConstructions))
            .ToList();

        BaseUnit chosenUnit;
        if (!city.Civ.IsAtWar()
            && city.Civ.Cities.Any(it => it.GetCenterTile().MilitaryUnit == null)
            && militaryUnits.Any(it => it.IsRanged())) // this is for city defence so get a ranged unit if we can
        {
            chosenUnit = militaryUnits
                .Where(it => it.IsRanged())
                .MaxBy(it => it.Cost).NotNull();
        }
        else if (isMissingNavalUnitsForCityDefence && militaryUnits.Any(IsNavalMeleeUnit))
        {
            chosenUnit = militaryUnits
                .Where(IsNavalMeleeUnit)
                .MaxBy(it => it.Cost).NotNull();
        }
        else
        { // randomize type of unit and take the strongest of its kind
            var bestUnitsForType = new JavaHashMap<string, BaseUnit>();
            foreach (var unit in militaryUnits)
            {
                if (!bestUnitsForType.TryGetValue(unit.UnitType, out var best)
                    || best.GetForceEvaluation() < unit.GetForceEvaluation())
                {
                    bestUnitsForType.Put(unit.UnitType, unit);
                }
            }
            // Check the maximum force evaluation for the shortlist so we can prune useless ones (ie scouts)
            if (bestUnitsForType.IsEmpty) return null;
            var bestForce = bestUnitsForType.Max(it => it.Value.GetForceEvaluation());
            chosenUnit = bestUnitsForType
                .Where(it => it.Value.UniqueTo != null || it.Value.GetForceEvaluation() > bestForce / 3)
                .Select(it => it.Value).ToList().Random(rng);
        }
        return chosenUnit;
    }

    /// <summary>Determines whether <paramref name="civView"/> should be allocating military to fending off barbarians</summary>
    public static bool AfraidOfBarbarians(CivView civView)
    {
        var civInfo = civView.GetCiv();
        if (civInfo.IsCityState || civInfo.IsBarbarian)
            return false;

        if (civInfo.GameInfo.GameParameters.NoBarbarians)
            return false; // If there are no barbarians we are not afraid

        var speed = civInfo.GameInfo.Speed;
        if (civInfo.GameInfo.Turns > 200 * speed.BarbarianModifier)
            return false; // Very late in the game we are not afraid

        var multiplier = civInfo.GameInfo.GameParameters.RagingBarbarians ? 1.3f
            : 1f; // We're slightly more afraid of raging barbs

        // Past the early game we are less afraid
        if (civInfo.GameInfo.Turns > 120 * speed.BarbarianModifier * multiplier)
            multiplier /= 2;

        // If we have no cities or a lot of units we are not afraid
        if (civInfo.Cities.Count == 0 || civInfo.Units.GetCivUnits().Count() >= 4 * multiplier)
            return false;

        // If we have vision of our entire starting continent (ish) we are not afraid
        civInfo.GameInfo.TileMap.AssignContinents(TileMap.AssignContinentsMode.Ensure);
        var startingContinent = civInfo.GetCapital().NotNull().GetCenterTile().GetContinent();
        if (civInfo.GameInfo.TileMap.ContinentSizes.TryGetValue(startingContinent, out var startingContinentSize)
            && startingContinentSize < civInfo.ViewableTiles.Count * multiplier)
            return false;

        // Otherwise we're afraid
        return true;
    }

    /// <summary>Checks resource scarcity making a construction undesirable.</summary>
    public static bool AllowAutomatedConstruction(CivView civView, CityObject city, INonPerpetualConstruction construction) =>
        AllowSpendingResource(civView, construction, city);

    /// <summary>
    /// Determines whether the AI should be willing to spend strategic resources to build
    /// <paramref name="construction"/> for the civ, assumes that we are actually able to do so.
    /// </summary>
    public static bool AllowSpendingResource(CivView civView, INonPerpetualConstruction construction, CityObject? cityInfo = null)
    {
        var civInfo = civView.GetCiv();
        // City states do whatever they want
        if (civInfo.IsCityState)
            return true;

        // Spaceships are always allowed
        if (civInfo.GameInfo.SpaceResources.Contains(construction.Name))
            return true;

        var requiredResources = construction is BaseUnit baseUnit
            ? baseUnit.GetResourceRequirementsPerTurn(civInfo.State)
            : construction.GetResourceRequirementsPerTurn(cityInfo?.State ?? civInfo.State);
        // Does it even require any resources?
        if (requiredResources.Count == 0)
            return true;

        var civResources = civInfo.GetCivResourcesByName();

        // Rule of thumb: reserve 2-3 for spaceship, then reserve half each for buildings and units
        // Assume that no buildings provide any resources
        foreach (var (resource, amount) in requiredResources)
        {
            // Also count things under construction
            var futureForUnits = 0;
            var futureForBuildings = 0;

            foreach (var city in civInfo.Cities)
            {
                var otherConstruction = city.CityConstructions.GetCurrentConstruction();
                if (otherConstruction is Building otherBuilding)
                    futureForBuildings += otherBuilding.GetResourceRequirementsPerTurn(city.State)[resource];
                else
                    futureForUnits += otherConstruction.GetResourceRequirementsPerTurn(civInfo.State)[resource];
            }

            // Make sure we have some for space
            if (civInfo.GameInfo.SpaceResources.Contains(resource) && civResources[resource] - amount - futureForBuildings - futureForUnits < 2)
            {
                return false;
            }

            // Assume buildings remain useful
            var neededForBuilding = civInfo.Cache.LastEraResourceUsedForBuilding.ContainsKey(resource);
            // Don't care about old units
            var neededForUnits = civInfo.Cache.LastEraResourceUsedForUnit.TryGetValue(resource, out var lastEraForUnit)
                && lastEraForUnit >= civInfo.GetEraNumber();

            // No need to save for both
            if (!neededForBuilding || !neededForUnits)
            {
                continue;
            }

            var usedForUnits = civInfo.DetailedCivResources.Where(it => it.Resource.Name == resource && it.Origin == "Units").Sum(it => -it.Amount);
            var usedForBuildings = civInfo.DetailedCivResources.Where(it => it.Resource.Name == resource && it.Origin == "Buildings").Sum(it => -it.Amount);

            if (construction is Building)
            {
                // Will more than half the total resources be used for buildings after this construction?
                if (civResources[resource] + usedForUnits < usedForBuildings + amount + futureForBuildings)
                {
                    return false;
                }
            }
            else
            {
                // Will more than half the total resources be used for units after this construction?
                if (civResources[resource] + usedForBuildings < usedForUnits + amount + futureForUnits)
                {
                    return false;
                }
            }
        }
        return true;
    }

    public static ThreatLevel ThreatAssessment(CivView assessorView, ForeignCivView assessedView)
    {
        var assessor = assessorView.GetCiv();
        var assessed = assessedView.GetCiv();
        var powerLevelComparison =
            assessed.GetStatForRanking(RankingType.Force) / (float)assessor.GetStatForRanking(RankingType.Force);
        if (powerLevelComparison > 2) return ThreatLevel.VeryHigh;
        if (powerLevelComparison > 1.5f) return ThreatLevel.High;
        if (powerLevelComparison < 0.5f) return ThreatLevel.VeryLow;
        if (powerLevelComparison < (1 / 1.5f)) return ThreatLevel.Low;
        return ThreatLevel.Medium;
    }

    private static bool ImprovementIsRemovable(CityObject city, TileClass tile)
    {
        var gameContext = new GameContext(city.Civ, city, tile: tile);
        var improvement = tile.TileImprovement;
        if (improvement == null) return false;
        return !((IHasUniques)improvement).HasUnique(UniqueType.AutomatedUnitsWillNotReplace, gameContext) &&
            !((IHasUniques)improvement).HasUnique(UniqueType.Irremovable, gameContext);
    }

    /// <summary>Support <see cref="UniqueType.CreatesOneImprovement"/> unique - find best tile for placement automation</summary>
    public static TileClass? GetTileForConstructionImprovement(CityObject city, TileImprovement improvement)
    {
        var civ = city.Civ;
        var civView = GetCivView(civ);
        return city.GetTiles().Where(it =>
            (it.TileImprovement == null || ImprovementIsRemovable(city, it))
            && it.ImprovementFunctions.CanBuildImprovement(improvement, city.State)
        ).MaxBy(it =>
            // Needs to take into account future improvement layouts, and better placement of citadel-like improvements
            RankStatsValue(it.Stats.GetStatDiffForImprovement(improvement, civ, city, it.Stats.GetTileStats(city, civ)), civView) + (
                ((IHasUniques)improvement).HasUnique(UniqueType.DefensiveBonus)
                    ? it.AerialDistanceTo(city.GetCenterTile()) + it.GetDefensiveBonus(false)
                    : 0));
    }

    /// <summary>Ranks a tile for any purpose except the expansion algorithm of cities</summary>
    internal static float RankTile(TileClass? tile, CivView civView)
    {
        var civInfo = civView.GetCiv();
        if (tile == null) return 0f;
        var tileOwner = tile.GetOwner();
        if (tileOwner != null && tileOwner != civInfo) return 0f; // Already belongs to another civilization, useless to us
        var stats = tile.Stats.GetTileStats(null, civInfo);
        var rank = RankStatsValue(stats, civView);
        if (tile.Improvement == null) rank += 0.5f; // improvement potential!
        if (tile.IsPillaged()) rank += 0.6f;
        var resource = tile.TileResource;
        if (civInfo.CanSeeResource(resource))
        {
            if (resource.NotNull().ResourceType != ResourceType.Bonus) rank += 1f; // for usage
            if (tile.Improvement == null) rank += 1f; // improvement potential - resources give lots when improved!
            if (tile.IsPillaged()) rank += 1.1f; // even better, repair is faster
        }
        return rank;
    }

    public static float RankStatsValue(Stats stats, CivView civView)
    {
        var civInfo = civView.GetCiv();
        var rank = 0.0f;
        rank += stats.Food * 1.2f; //food get more value to keep city growing

        rank += civInfo.Gold < 0 && civInfo.Stats.StatsForNextTurn.Gold <= 0
            ? stats.Gold //build more gold infrastructure if in serious gold problems
                         // This could lead to oscilliatory behaviour however: gold problem -> build trade post -> no gold problem -> replace trade posts -> gold problem
            : stats.Gold / 3; // Gold is valued less than is the case for citizen assignment,
        //otherwise the AI would replace tiles with trade posts upon entering a golden age,
        //and replace the trade post again when the golden age ends.
        // We need a way to take golden age gold into account before the GA actually takes place
        rank += stats.Happiness;
        rank += stats.Production;
        rank += stats.Science;
        rank += stats.Culture;
        rank += stats.Faith;
        return rank;
    }
}

public enum ThreatLevel
{
    VeryLow,
    Low,
    Medium,
    High,
    VeryHigh
}
