using Unciv.Logic.Civilization.Managers;
using Unciv.Logic.Civilization.Transients;
using Unciv.Models;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Ruleset.Unit;
using Unciv.Models.StatTypes;
using Unciv.Utils;
using CityObject = Unciv.Logic.City.City;
using CivilizationClass = Unciv.Logic.Civilization.Civilization;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Logic.Automation.Civilization;

// Ported from core/src/com/unciv/logic/automation/civilization/ReligionAutomation.kt (T087). Purity annotations dropped (T027 policy).
// Kotlin `hashSetOf<Belief>()` (chooseBeliefs, pickBeliefOfType exclusions): Belief has an identity hashCode, so there is no reproducible
// JVM order; LinkedHashSet, insertion order. `associateBy(...)` is a LinkedHashMap (insertion order, first key position kept), kept as a list of pairs.
// Kotlin `?: return@filter false` inside a filter lambda becomes a local function returning false for a null buy cost.

public static class ReligionAutomation
{
    #region faith spending

    public static void SpendFaithOnReligion(CivilizationClass civInfo)
    {
        if (civInfo.Cities.Count == 0) return;

        // Save for great prophet
        if (civInfo.ReligionManager.ReligionState != ReligionState.EnhancedReligion
            && (civInfo.ReligionManager.RemainingFoundableReligions() != 0 || civInfo.ReligionManager.ReligionState > ReligionState.Pantheon))
        {
            BuyGreatProphetInAnyCity(civInfo);
            return;
        }

        if (civInfo.ReligionManager.RemainingFoundableReligions() == 0)
        {
            BuyGreatPerson(civInfo);
            TryBuyAnyReligiousBuilding(civInfo);
            return;
        }

        // If we don't have majority in all our own cities, build missionaries and inquisitors to solve this
        //todo (upstream): in late game, this may not be worth it
        var citiesWithoutOurReligion = civInfo.Cities.Where(it => it.Religion.GetMajorityReligion() != civInfo.ReligionManager.Religion.NotNull()).ToList();
        // The original had a cap at 4 missionaries total, but 1/4 * the number of cities should be more appropriate imo
        if (citiesWithoutOurReligion.Count >
            4 * civInfo.Units.GetCivUnits().Count(it =>
                it.HasUnique(UniqueType.CanSpreadReligion)
                || it.HasUnique(UniqueType.CanRemoveHeresy)))
        {
            var (city, pressureDifference) = citiesWithoutOurReligion.Select(city =>
                (City: city, PressureDeficit: city.Religion.GetPressureDeficit(civInfo.ReligionManager.Religion?.Name)))
                .MaxBy(it => it.PressureDeficit);
            if (pressureDifference >= Constants.AiPreferInquisitorOverMissionaryPressureDifference * civInfo.GameInfo.Ruleset.ModOptions.Constants.ReligionMissionaryPressureMultiplier)
                BuyInquisitorNear(civInfo, city);
            BuyMissionaryInAnyCity(civInfo);
            return;
        }

        // Get an inquisitor to defend our holy city
        var holyCity = civInfo.ReligionManager.GetHolyCity();
        if (holyCity != null
            && civInfo.Cities.Contains(holyCity)
            && !civInfo.Units.GetCivUnits().Any(it => it.HasUnique(UniqueType.PreventSpreadingReligion))
            && !holyCity.Religion.IsProtectedByInquisitor())
        {
            BuyInquisitorNear(civInfo, holyCity);
            return;
        }

        // Just buy missionaries to spread our religion outside of our civ
        if (civInfo.Units.GetCivUnits().Count(it => it.HasUnique(UniqueType.CanSpreadReligion)) < 4)
        {
            BuyMissionaryInAnyCity(civInfo);
            return;
        }
        // Todo: declare war if enemy missionaries enter our civ without permission
    }

    private static void TryBuyAnyReligiousBuilding(CivilizationClass civInfo)
    {
        foreach (var city in civInfo.Cities)
        {
            if (city.Religion.GetMajorityReligion() == null) continue;
            var buildings = city.Religion.GetMajorityReligion().NotNull().BuildingsPurchasableByBeliefs;
            var buildingToBePurchased = buildings
                .Select(civInfo.GetEquivalentBuilding)
                .Where(it => ((INonPerpetualConstruction)it).IsPurchasable(city.CityConstructions))
                .Where(it => it.GetStatBuyCost(city, Stat.Faith) is { } cost && cost <= civInfo.ReligionManager.StoredFaith)
                .MinBy(it => it.GetStatBuyCost(city, Stat.Faith).NotNull());
            if (buildingToBePurchased == null) continue;
            city.CityConstructions.PurchaseConstruction(buildingToBePurchased, -1, true, Stat.Faith);
            return;
        }
    }

    /// <summary>Cheapest (first on ties) of the units that some city can purchase with faith, as Kotlin's associateBy + filter + minByOrNull chain.</summary>
    private static BaseUnit? CheapestPurchasableWithFaith(CivilizationClass civInfo, IEnumerable<BaseUnit> units)
    {
        var candidates = units.Distinct()
            // Get list of cities it can be built in
            .Select(unit => (Unit: unit, Cities: civInfo.Cities.Where(it => ((INonPerpetualConstruction)unit).IsPurchasable(it.CityConstructions) && unit.CanBePurchasedWithStat(it, Stat.Faith)).ToList()))
            .Where(it => it.Cities.Count != 0)
            .ToList();
        if (candidates.Count == 0) return null;
        // And from that list determine the cheapest price
        return candidates.MinBy(it => it.Cities.Min(city => it.Unit.GetStatBuyCost(city, Stat.Faith).NotNull())).Unit;
    }

    private static void BuyMissionaryInAnyCity(CivilizationClass civInfo)
    {
        if (civInfo.ReligionManager.ReligionState < ReligionState.Religion) return;
        var missionaries = civInfo.GameInfo.Ruleset.Units.Values.Where(unit => ((IHasUniques)unit).HasUnique(UniqueType.CanSpreadReligion))
            .Select(civInfo.GetEquivalentUnit).ToList();

        var missionaryConstruction = CheapestPurchasableWithFaith(civInfo, missionaries);
        if (missionaryConstruction == null) return;

        var hasUniqueToTakeCivReligion = ((IHasUniques)missionaryConstruction).HasUnique(UniqueType.TakeReligionOverBirthCity);

        var validCitiesToBuy = civInfo.Cities.Where(it =>
            it.GetCenterTile().CivilianUnit == null // can't purchase them here
            && (hasUniqueToTakeCivReligion || it.Religion.GetMajorityReligion() == civInfo.ReligionManager.Religion)
            && missionaryConstruction.GetStatBuyCost(it, Stat.Faith) is { } cost && cost <= civInfo.ReligionManager.StoredFaith
            && ((INonPerpetualConstruction)missionaryConstruction).IsPurchasable(it.CityConstructions)
            && missionaryConstruction.CanBePurchasedWithStat(it, Stat.Faith)).ToList();
        if (validCitiesToBuy.Count == 0) return;

        var citiesWithBonusCharges = validCitiesToBuy.Where(city =>
            city.GetMatchingUniques(UniqueType.UnitStartingPromotions).Any(it =>
            {
                var promotionName = it.Params[2];
                if (!city.GetRuleset().UnitPromotions.TryGetValue(promotionName, out var promotion)) return false;
                return ((IHasUniques)promotion).HasUnique(UniqueType.CanSpreadReligion);
            })).ToList();
        var holyCity = validCitiesToBuy.FirstOrDefault(it => it.IsHolyCityOf(civInfo.ReligionManager.Religion.NotNull().Name));

        var cityToBuyMissionary =
            citiesWithBonusCharges.Count != 0 ? citiesWithBonusCharges[0]
            : holyCity != null ? holyCity
            : validCitiesToBuy[0];

        cityToBuyMissionary.CityConstructions.PurchaseConstruction(missionaryConstruction, -1, true, Stat.Faith);
    }

    private static void BuyGreatProphetInAnyCity(CivilizationClass civInfo)
    {
        if (civInfo.ReligionManager.ReligionState < ReligionState.Religion) return;
        var greatProphetEquivalent = civInfo.ReligionManager.GetGreatProphetEquivalent();
        if (greatProphetEquivalent == null) return;
        var greatProphetUnit = civInfo.GetEquivalentUnit(greatProphetEquivalent);
        var cityToBuyGreatProphet = civInfo.Cities
            .Where(it => ((INonPerpetualConstruction)greatProphetUnit).IsPurchasable(it.CityConstructions))
            .Where(it => greatProphetUnit.CanBePurchasedWithStat(it, Stat.Faith))
            .Where(it => greatProphetUnit.GetStatBuyCost(it, Stat.Faith) is { } cost && cost <= civInfo.ReligionManager.StoredFaith)
            .MinBy(it => greatProphetUnit.GetStatBuyCost(it, Stat.Faith).NotNull());
        if (cityToBuyGreatProphet == null) return;
        cityToBuyGreatProphet.CityConstructions.PurchaseConstruction(greatProphetUnit, -1, true, Stat.Faith);
    }

    private static void BuyInquisitorNear(CivilizationClass civInfo, CityObject city)
    {
        if (civInfo.ReligionManager.ReligionState < ReligionState.Religion) return;
        var inquisitors = civInfo.GameInfo.Ruleset.Units.Values.Where(it =>
            ((IHasUniques)it).HasUnique(UniqueType.CanRemoveHeresy) || ((IHasUniques)it).HasUnique(UniqueType.PreventSpreadingReligion))
            .Select(civInfo.GetEquivalentUnit).ToList();

        var inquisitorConstruction = CheapestPurchasableWithFaith(civInfo, inquisitors);
        if (inquisitorConstruction == null) return;

        var hasUniqueToTakeCivReligion = ((IHasUniques)inquisitorConstruction).HasUnique(UniqueType.TakeReligionOverBirthCity);

        var validCitiesToBuy = civInfo.Cities.Where(it =>
            (hasUniqueToTakeCivReligion || it.Religion.GetMajorityReligion() == civInfo.ReligionManager.Religion)
            && inquisitorConstruction.GetStatBuyCost(it, Stat.Faith) is { } cost && cost <= civInfo.ReligionManager.StoredFaith
            && ((INonPerpetualConstruction)inquisitorConstruction).IsPurchasable(it.CityConstructions)
            && inquisitorConstruction.CanBePurchasedWithStat(it, Stat.Faith));
        var cityToBuy = validCitiesToBuy
            .MinBy(it => it.GetCenterTile().AerialDistanceTo(city.GetCenterTile()));
        if (cityToBuy == null) return;

        cityToBuy.CityConstructions.PurchaseConstruction(inquisitorConstruction, -1, true, Stat.Faith);
    }

    private static void BuyGreatPerson(CivilizationClass civInfo)
    {
        var greatPersonUnit = civInfo.GameInfo.Ruleset.Units.Values.Where(it =>
            ((IHasUniques)it).HasUnique(UniqueType.GreatPerson) && !((IHasUniques)it).HasUnique(UniqueType.MayFoundReligion)); //we want to exclude great prophets from the list
        var greatPersonConstruction = CheapestPurchasableWithFaith(civInfo, greatPersonUnit);
        if (greatPersonConstruction == null) return;

        var validCitiesToBuy = civInfo.Cities.Where(it =>
            greatPersonConstruction.GetStatBuyCost(it, Stat.Faith) is { } cost && cost <= civInfo.ReligionManager.StoredFaith).ToList();

        if (validCitiesToBuy.Count == 0) return;

        var cityToBuy = validCitiesToBuy[0];

        cityToBuy.CityConstructions.PurchaseConstruction(greatPersonConstruction, -1, true, Stat.Faith);
    }

    #endregion

    #region rate beliefs

    public static float RateBelief(CivilizationClass civInfo, Belief belief)
    {
        var score = 0f; // Roughly equivalent to the sum of stats gained across all cities

        foreach (var city in civInfo.Cities)
        {
            city.GetCenterTile().ForEachTileInDistance(city.GetWorkRange(), tile =>
            {
                var tileRng = tile.StateThisTile.StateBasedRandom("ReligionAutomation.rateBelief");
                var tileScore = BeliefBonusForTile(belief, tile, city);

                score += tileScore * (city.IsWorked(tile) ? 1f // worked
                    : ReferenceEquals(tile.GetCity(), city) ? 0.7f // workable
                    : 0.5f) // unavailable - for now
                    * (tileRng.NextFloat() * 0.05f + 0.975f);
            });

            var cityRng = city.State.StateBasedRandom("ReligionAutomation.rateBelief");
            score += BeliefBonusForCity(civInfo, belief, city) * (cityRng.NextFloat() * 0.1f + 0.95f);
        }

        var civRng = civInfo.State.StateBasedRandom("ReligionAutomation.rateBelief");
        score += BeliefBonusForPlayer(civInfo, belief) * (civRng.NextFloat() * 0.3f + 0.85f);

        // All of these Random.nextFloat() don't exist in the original, but I've added them to make things a bit more random.

        if (belief.Type == BeliefType.Pantheon)
            score *= 0.9f;

        score *= ((IHasUniques)belief).GetWeightForAiDecision(new GameContext(civInfo));

        return score;
    }

    private static float BeliefBonusForTile(Belief belief, TileClass tile, CityObject city)
    {
        var bonusYield = 0f;
        foreach (var unique in belief.UniqueObjects)
        {
            switch (unique.Type)
            {
                case UniqueType.StatsFromObject:
                    {
                        var resource = tile.TileResource;
                        if (tile.MatchesFilter(unique.Params[1]))
                        {
                            if (!((IHasUniques)tile.LastTerrain).HasUnique(UniqueType.ProductionBonusWhenRemoved) ||
                                !tile.LastTerrain.MatchesFilter(unique.Params[1]) //forest pantheons are bad, as we want to remove the forests
                               ) bonusYield += unique.Stats.Sum();
                            else if (resource != null && (resource.MatchesFilter(unique.Params[1]) ||
                                                          resource.IsImprovedBy(unique.Params[1]))
                                    ) bonusYield += unique.Stats.Sum(); //resource pantheons are good, as we want to work the tile anyways
                        }
                        else if (resource != null && (resource.MatchesFilter(unique.Params[1]) ||
                                                      resource.IsImprovedBy(unique.Params[1]))
                                ) bonusYield += unique.Stats.Sum(); //resource pantheons are good, as we want to work the tile anyways
                        break;
                    }
                case UniqueType.StatsFromTilesWithout:
                    if (city.MatchesFilter(unique.Params[3]) &&
                        tile.MatchesFilter(unique.Params[1]) &&
                        !tile.MatchesFilter(unique.Params[2])
                       ) bonusYield += unique.Stats.Sum();
                    break;
            }
        }
        return bonusYield;
    }

    private static float BeliefBonusForCity(CivilizationClass civInfo, Belief belief, CityObject city)
    {
        var score = 0f;
        var ruleSet = civInfo.GameInfo.Ruleset;
        foreach (var unique in belief.UniqueObjects)
        {
            var modifier = KotlinMath.Pow(0.5f, unique.Modifiers.Count);
            // Multiply by 3/10 if has an obsoleted era
            // Multiply by 2 if enough pop/followers (best implemented with conditionals, so left open for now)
            // If obsoleted, continue
            score += modifier * unique.Type switch
            {
                UniqueType.GrowthPercentBonus => unique.Params[0].ToFloat() / 3f,
                UniqueType.BorderGrowthPercentage => -unique.Params[0].ToFloat() / 10f,
                UniqueType.StrengthForCities => unique.Params[0].ToFloat() / 10f, // Modified by personality
                UniqueType.CityHealingUnits => unique.Params[1].ToFloat() / 10f,
                UniqueType.PercentProductionBuildings => unique.Params[0].ToFloat() / 3f,
                UniqueType.PercentProductionWonders => unique.Params[0].ToFloat() / 3f,
                UniqueType.PercentProductionUnits => unique.Params[0].ToFloat() / 3f,
                UniqueType.StatsFromCitiesOnSpecificTiles =>
                    city.GetCenterTile().MatchesFilter(unique.Params[1])
                        ? unique.Stats.Sum() // Modified by personality
                        : 0f,
                UniqueType.StatsFromObject => StatsFromObjectValue(unique, ruleSet, city),
                UniqueType.StatsFromTradeRoute =>
                    unique.Stats.Sum() *
                    (city.IsConnectedToCapital() ? 1f
                        : 0f), //no yields from the belief yet, also for pantheons it's quite low-tempo
                UniqueType.StatPercentFromReligionFollowers =>
                    Math.Min(unique.Params[0].ToFloat() * city.Population.Population, unique.Params[2].ToFloat()),
                UniqueType.StatsPerCity =>
                    city.MatchesFilter(unique.Params[1])
                        ? unique.Stats.Sum() * 1f //free yields
                        : 0f,
                _ => 0f
            };
        }

        return score;
    }

    private static float StatsFromObjectValue(Unique unique, Ruleset ruleSet, CityObject city)
    {
        if (ruleSet.Buildings.TryGetValue(unique.Params[1], out var building))
            return unique.Stats.Sum() *
                (building.IsNationalWonder ? 0.25f //there's at most 1 copy of each of these in our empire, and the AI is slow at getting it
                    : 1f); // Yields from regular buildings won't need the upfront purchase cost as is the case with religion buildings, but they may have weird requirements (gardens etc.)
        if (ruleSet.Specialists.ContainsKey(unique.Params[1]))
            return unique.Stats.Sum() *
                (city.Population.Population > 8f ? 2f
                    : 1f);
        return unique.Stats.Sum() * 0f; //yields from world wonders and great improvements - the latter needs additional AI logic to be used correctly
    }

    private static float BeliefBonusForPlayer(CivilizationClass civInfo, Belief belief)
    {
        var score = 0f;
        var numberOfFoundedReligions = civInfo.GameInfo.Civilizations.Count(
            it => it.ReligionManager.Religion != null && it.ReligionManager.ReligionState >= ReligionState.Religion);
        var maxNumberOfReligions = numberOfFoundedReligions + civInfo.ReligionManager.RemainingFoundableReligions();

        // adjusts scores of certain beliefs as game evolves (adapted from Civ 5 DLL files on AI belief selection)
        // enable differentiation of early vs late founding of religion and early vs late enhancement of religion
        // this is mainly for mods which may shuffle enhancer and founder beliefs w.r.t. base Unciv
        var gameTimeScalingPercent = 100;
        switch (civInfo.ReligionManager.ReligionState)
        {
            case ReligionState.FoundingReligion:
                gameTimeScalingPercent = 100 - (numberOfFoundedReligions * 100 / maxNumberOfReligions);
                break;
            case ReligionState.EnhancingReligion:
                var amountOfEnhancedReligions = civInfo.GameInfo.Civilizations.Count(
                    it => it.ReligionManager.Religion != null && it.ReligionManager.ReligionState == ReligionState.EnhancedReligion);
                gameTimeScalingPercent = 100 - (amountOfEnhancedReligions * 100 / maxNumberOfReligions);
                break;
                // pantheon shouldn't matter
        }
        var goodEarlyModifier = gameTimeScalingPercent < 33 ? 1f
            : gameTimeScalingPercent < 66 ? 2f
            : 4f;
        var goodLateModifier = gameTimeScalingPercent < 33 ? 2f
            : gameTimeScalingPercent < 66 ? 1f
            : 1 / 2f;

        foreach (var unique in belief.UniqueObjects)
        {
            var modifier =
                unique.GetModifiers(UniqueType.ConditionalOurUnit).Any(it => it.Params[0] == civInfo.ReligionManager.GetGreatProphetEquivalent()?.Name) ? 1 / 2f
                : 1f;
            // Some city-filters are modified by personality (non-enemy foreign cities)
            score += modifier * unique.Type switch
            {
                UniqueType.KillUnitPlunderNearCity => 0f, //can be very strong, but the AI currently isn't farming barb camps
                UniqueType.BuyUnitsForAmountStat or UniqueType.BuyBuildingsForAmountStat =>
                    civInfo.ReligionManager.Religion != null
                    && civInfo.ReligionManager.Religion.FollowerBeliefUniqueMap.GetUniques(unique.Type.NotNull()).Any()
                        ? 0f
                        // This is something completely different from the original, but I have no idea
                        // what happens over there
                        : civInfo.Stats.StatsForNextTurn[StatExtensions.ValueOf(unique.Params[2])] * 300f / unique.Params[1].ToFloat(), //the costs of these are probably similar to the baseUnitBuyCost
                UniqueType.BuyUnitsWithStat or UniqueType.BuyBuildingsWithStat =>
                    civInfo.ReligionManager.Religion != null
                    && civInfo.ReligionManager.Religion.FollowerBeliefUniqueMap.GetUniques(unique.Type.NotNull()).Any()
                        ? 0f
                        // This is something completely different from the original, but I have no idea
                        // what happens over there
                        : civInfo.Stats.StatsForNextTurn[StatExtensions.ValueOf(unique.Params[1])] * 300f / civInfo.GetEra().BaseUnitBuyCost,
                //baseUnitBuyCost is 200 in Standard speed pre-Renaissance, but overvalue such as to let the high-faith civs pick the good faith sinks
                UniqueType.BuyUnitsByProductionCost =>
                    0f, //Holy Warriors is a waste if we don't buy units with it, and if we buy units with it'll cost us great persons
                UniqueType.StatsWhenSpreading =>
                    unique.Params[0].ToFloat() / 15f,
                UniqueType.StatsWhenAdoptingReligion =>
                    unique.Stats.Sum() / 50f,
                UniqueType.RestingPointOfCityStatesFollowingReligionChange =>
                    unique.Params[0].ToFloat() / 8f,
                UniqueType.StatsFromGlobalCitiesFollowingReligion =>
                    unique.Stats.Sum() * 2f, //free yields that are potentially more than our own number of cities would allow
                UniqueType.StatsFromGlobalFollowers =>
                    10f * (unique.Stats.Sum() / unique.Params[1].ToFloat()),
                UniqueType.Strength =>
                    unique.Params[0].ToFloat() * 3f, //combat strength from beliefs is very strong
                UniqueType.ReligionSpreadDistance =>
                    (10f + unique.Params[0].ToFloat()) * goodEarlyModifier,
                UniqueType.NaturalReligionSpreadStrength =>
                    unique.Params[0].ToFloat() * goodEarlyModifier / 10f, //We should weigh this according to cityFilter; Religious Texts is way stronger than Religious Unity
                UniqueType.SpreadReligionStrength =>
                    unique.Params[0].ToFloat() * goodLateModifier / 10f, //Cheaper missionaries are generally better than stronger missionaries
                UniqueType.FaithCostOfGreatProphetChange =>
                    -unique.Params[0].ToFloat() * goodLateModifier / 10f, //It's only about 1 more prophet, due to the increasing costs.
                UniqueType.BuyBuildingsDiscount or UniqueType.BuyUnitsDiscount =>
                    -unique.Params[2].ToFloat() * goodLateModifier / 5f,
                UniqueType.BuyItemsDiscount =>
                    -unique.Params[1].ToFloat() * goodLateModifier / 5f,
                _ => 0f
            };
        }

        return score;
    }

    internal static void ChooseReligiousBeliefs(CivilizationClass civInfo)
    {
        ChoosePantheon(civInfo);
        FoundReligion(civInfo);
        EnhanceReligion(civInfo);
        ChooseFreeBeliefs(civInfo);
    }

    private static void ChoosePantheon(CivilizationClass civInfo)
    {
        if (!civInfo.ReligionManager.CanFoundOrExpandPantheon()) return;
        // So looking through the source code of the base game available online,
        // the functions for choosing beliefs total in at around 400 lines.
        // https://github.com/Gedemon/Civ5-DLL/blob/aa29e80751f541ae04858b6d2a2c7dcca454201e/CvGameCoreDLL_Expansion1/CvReligionClasses.cpp
        // line 4426 through 4870.
        // This is way too much work for now, so I'll just choose a random pantheon instead.
        // Should probably be changed later, but it works for now.
        var chosenPantheon = PickBeliefOfType(civInfo, BeliefType.Pantheon);
        if (chosenPantheon == null) return; // panic!
        civInfo.ReligionManager.ChooseBeliefs(
            [chosenPantheon],
            useFreeBeliefs: civInfo.ReligionManager.UsingFreeBeliefs());
    }

    private static void FoundReligion(CivilizationClass civInfo)
    {
        if (civInfo.ReligionManager.ReligionState != ReligionState.FoundingReligion) return;
        var rng = civInfo.State.StateBasedRandom("ReligionAutomation.foundReligion");
        var usedReligions = new LinkedHashSet<string>(civInfo.GameInfo.Religions.Values.Select(it => it.Name));
        var availableReligions = civInfo.GameInfo.Ruleset.Religions.Where(it => !usedReligions.Contains(it)).ToList();
        var favoredReligion = civInfo.Nation.FavoredReligion is { } favored && availableReligions.Contains(favored) ? favored : null;
        var allFavoredReligions = new LinkedHashSet<string>(civInfo.GameInfo.Civilizations.Select(it => it.Nation.FavoredReligion).OfType<string>());
        var nonFavoredReligions = availableReligions.Where(it => !allFavoredReligions.Contains(it)).ToList();
        var chosenReligion = favoredReligion
            ?? nonFavoredReligions.RandomOrNull(rng) // allow other civs to found their own favoured religion when possible
            ?? availableReligions.RandomOrNull(rng);
        if (chosenReligion == null) return; // Wait what? How did we pass the checking when using a great prophet but not this?

        civInfo.ReligionManager.FoundReligion(chosenReligion, chosenReligion);

        var chosenBeliefs = ChooseBeliefs(civInfo, civInfo.ReligionManager.GetBeliefsToChooseAtFounding()).ToList();
        civInfo.ReligionManager.ChooseBeliefs(chosenBeliefs);
    }

    private static void EnhanceReligion(CivilizationClass civInfo)
    {
        if (civInfo.ReligionManager.ReligionState != ReligionState.EnhancingReligion) return;
        civInfo.ReligionManager.ChooseBeliefs(
            ChooseBeliefs(civInfo, civInfo.ReligionManager.GetBeliefsToChooseAtEnhancing()).ToList());
    }

    private static void ChooseFreeBeliefs(CivilizationClass civInfo)
    {
        if (!civInfo.ReligionManager.HasFreeBeliefs()) return;
        civInfo.ReligionManager.ChooseBeliefs(
            ChooseBeliefs(civInfo, civInfo.ReligionManager.FreeBeliefsAsEnums()).ToList(),
            useFreeBeliefs: true);
    }

    private static LinkedHashSet<Belief> ChooseBeliefs(CivilizationClass civInfo, Counter<BeliefType> beliefsToChoose)
    {
        var chosenBeliefs = new LinkedHashSet<Belief>();
        // The `continue`s should never be reached, but just in case I'd rather have the AI have a
        // belief less than make the game crash. The `continue`s should only be reached whenever
        // there are not enough beliefs to choose, but there should be, as otherwise we could
        // not have used a great prophet to found/enhance our religion.
        foreach (var belief in Enum.GetValues<BeliefType>())
        {
            if (belief == BeliefType.None) continue;
            for (var i = 0; i < beliefsToChoose[belief]; i++)
            {
                var picked = PickBeliefOfType(civInfo, belief, chosenBeliefs);
                if (picked == null) continue;
                chosenBeliefs.Add(picked);
            }
        }
        return chosenBeliefs;
    }

    private static Belief? PickBeliefOfType(CivilizationClass civInfo, BeliefType beliefType, LinkedHashSet<Belief>? additionalBeliefsToExclude = null)
    {
        additionalBeliefsToExclude ??= [];
        return civInfo.GameInfo.Ruleset.Beliefs.Values
            .Where(it =>
                (it.Type == beliefType || beliefType == BeliefType.Any)
                && !additionalBeliefsToExclude.Contains(it)
                && civInfo.ReligionManager.GetReligionWithBelief(it) == null
                && !((IHasUniques)it).GetMatchingUniques(UniqueType.OnlyAvailable, GameContext.IgnoreConditionalsState)
                    .Any(unique => !unique.ConditionalsApply(civInfo.State))
                && !((IHasUniques)it).GetMatchingUniques(UniqueType.Unavailable, civInfo.State).Any())
            .MaxBy(it => RateBelief(civInfo, it));
    }

    #endregion
}
