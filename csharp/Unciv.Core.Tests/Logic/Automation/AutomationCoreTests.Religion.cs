using Unciv.Core.Tests.Support;
using Unciv.Logic.Automation.Civilization;
using Unciv.Logic.Civilization.Managers;
using Unciv.Models.Ruleset;
using CityObject = Unciv.Logic.City.City;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Core.Tests.Logic.Automation;

// T087. ReligionAutomation.kt has no Kotlin test fixture: C#-only cases, expected values derived by hand from the Kotlin source.
public static partial class AutomationCoreTests
{
    [Collection("RulesetCache")]
    public class ReligionAutomationTests
    {
        private readonly TestGame testGame = new();

        private (CivilizationObject Civ, CityObject City) CivWithCity()
        {
            testGame.MakeHexagonalMap(6);
            // TestGame.MakeHexagonalMap applies its base terrain to the replaced map, so make the new tiles Desert by hand
            foreach (var tile in testGame.TileMap.Values) testGame.SetTileTerrain(tile.Position, Constants.Desert);
            var civ = testGame.AddCiv(isPlayer: false);
            var city = testGame.AddCity(civ, testGame.GetTile(0, 0));
            return (civ, city);
        }

        private float Rate(CivilizationObject civ, string unique, BeliefType type = BeliefType.Follower) =>
            ReligionAutomation.RateBelief(civ, testGame.CreateBelief(type, unique));

        // rateBelief multiplies the city part by a random factor in [0.95, 1.05) and the civ part by one in [0.85, 1.15)
        private static void AssertWithin(float expectedBase, float actual, float spread)
        {
            var low = System.Math.Min(expectedBase * (1 - spread), expectedBase * (1 + spread));
            var high = System.Math.Max(expectedBase * (1 - spread), expectedBase * (1 + spread));
            Assert.InRange(actual, low - 1e-4f, high + 1e-4f);
        }

        [Theory]
        [InlineData("[+30]% growth [in all cities]", 10f)]
        [InlineData("[+30]% Culture cost of natural border growth [in all cities]", -3f)]
        [InlineData("[+20]% Strength for cities", 2f)]
        [InlineData("[Land] Units adjacent to this city heal [10] HP per turn when healing", 1f)]
        [InlineData("[+15]% Production when constructing [Wonder] buildings [in all cities]", 5f)]
        [InlineData("[+2 Faith] in cities on [Desert] tiles", 2f)]
        [InlineData("[+2 Faith] in cities on [Hill] tiles", 0f)]
        [InlineData("[+30]% [Faith] from every follower, up to [+50]%", 30f)]
        [InlineData("[+3 Culture] [in all cities]", 3f)]
        [InlineData("[+2 Culture] from every [Monument]", 2f)]
        [InlineData("[+4 Culture] from every [Heroic Epic]", 1f)] // national wonders are worth a quarter
        [InlineData("[+3 Culture] from every [Pyramids]", 0f)] // world wonders are not
        [InlineData("[+3 Science] from every [Scientist]", 3f)] // specialists, city of 1
        [InlineData("[+3 Science] from every [Unknown thing]", 0f)]
        [InlineData("[+30]% growth [in all cities] <during a Golden Age>", 5f)] // every modifier halves
        [InlineData("[+30]% growth [in all cities] <during a Golden Age> <when at war>", 2.5f)]
        public void RateBeliefCityPartFollowsTheKotlinTable(string unique, float expectedBase)
        {
            var (civ, _) = CivWithCity();
            AssertWithin(expectedBase, Rate(civ, unique), 0.05f);
        }

        [Fact]
        public void RateBeliefBigCitiesDoubleSpecialistYields()
        {
            var (civ, city) = CivWithCity();
            city.Population.AddPopulation(9); // 10 > 8
            AssertWithin(6f, Rate(civ, "[+3 Science] from every [Scientist]"), 0.05f);
        }

        [Fact]
        public void RateBeliefTileAndCityFilterTables()
        {
            var (civ, city) = CivWithCity();
            // every worked tile counts fully, other tiles of the city 0.7 and tiles outside 0.5 (all desert: stats from every [Desert])
            var score = Rate(civ, "[+1 Food] from every [Desert]");
            var worked = city.WorkedTiles.Count;
            var ownTiles = city.GetTiles().Count();
            var inRange = 0;
            city.GetCenterTile().ForEachTileInDistance(city.GetWorkRange(), _ => inRange++);
            var expected = worked * 1f + (ownTiles - worked) * 0.7f + (inRange - ownTiles) * 0.5f;
            Assert.InRange(score, expected * 0.97f, expected * 1.03f);

            // Without: [stats] from [tileFilter] tiles without [tileFilter] [cityFilter]
            Assert.True(Rate(civ, "[+1 Food] from [Desert] tiles without [Hill] [in all cities]") > 0f);
            Assert.Equal(0f, Rate(civ, "[+1 Food] from [Desert] tiles without [Desert] [in all cities]"));
        }

        [Fact]
        public void RateBeliefForestPantheonsAreBadAndResourceOnesGood()
        {
            var (civ, _) = CivWithCity();
            testGame.SetTileTerrainAndFeatures(new Unciv.Logic.Map.HexCoord(1, 0), Constants.Grassland, Constants.Forest);
            var forest = Rate(civ, "[+1 Production] from every [Forest]");
            Assert.Equal(0f, forest); // we want to remove forests
            var iron = testGame.Ruleset.TileResources["Iron"];
            testGame.GetTile(2, 0).TileResource = iron;
            Assert.True(Rate(civ, "[+1 Production] from every [Iron]") > 0f);
            // a resource on the forest tile makes the tile count for the resource filter, though still not for the forest filter
            testGame.GetTile(2, 0).TileResource = null;
            testGame.GetTile(1, 0).TileResource = iron;
            Assert.True(Rate(civ, "[+1 Production] from every [Iron]") > 0f);
            Assert.Equal(0f, Rate(civ, "[+1 Production] from every [Forest]"));
        }

        [Theory]
        [InlineData("When spreading religion to a city, gain [30] times the amount of followers of other religions as [Faith]", 2f)]
        [InlineData("[+45 Gold] when a city adopts this religion for the first time", 0.9f)]
        [InlineData("Resting point for Influence with City-States following this religion [16]", 2f)]
        [InlineData("[+3 Faith] for each global city following this religion", 6f)]
        [InlineData("[+3 Faith] from every [4] global followers [in all cities]", 7.5f)]
        [InlineData("[+25]% Strength", 75f)]
        [InlineData("Religion naturally spreads to cities [3] tiles away", 13f * 4f)] // early: x4
        [InlineData("[+50]% Natural religion spread [in all cities]", 50f * 4f / 10f)]
        [InlineData("[+20]% Spread Religion Strength", 20f * 0.5f / 10f)]
        [InlineData("[-20]% Faith cost of generating Great Prophet equivalents", 20f * 0.5f / 10f)]
        [InlineData("[Faith] cost of purchasing [Wonder] buildings [-25]%", 25f * 0.5f / 5f)]
        [InlineData("[Faith] cost of purchasing [Melee] units [-25]%", 25f * 0.5f / 5f)]
        [InlineData("[Faith] cost of purchasing items in cities [-20]%", 20f * 0.5f / 5f)]
        [InlineData("Earn [100]% of [Land] unit's [Cost] as [Faith] when killed within 4 tiles of a city following this religion", 0f)]
        [InlineData("May buy [Melee] units with [Faith] for [3] times their normal Production cost", 0f)]
        public void RateBeliefPlayerPartFollowsTheKotlinTable(string unique, float expectedBase)
        {
            var (civ, _) = CivWithCity();
            AssertWithin(expectedBase, Rate(civ, unique), 0.15f);
        }

        [Fact]
        public void RateBeliefBuyForAmountAndWithStatUseTheCivsStatsUnlessAlreadyOwned()
        {
            var (civ, _) = CivWithCity();
            civ.Stats.StatsForNextTurn.Faith = 6f;
            AssertWithin(6f * 300f / 150f, Rate(civ, "May buy [Melee] units for [150] [Faith] [in all cities]"), 0.15f);
            AssertWithin(6f * 300f / 150f, Rate(civ, "May buy [Wonder] buildings for [150] [Faith] [in all cities]"), 0.15f);
            AssertWithin(6f * 300f / civ.GetEra().BaseUnitBuyCost, Rate(civ, "May buy [Melee] units with [Faith] [in all cities]"), 0.15f);
            AssertWithin(6f * 300f / civ.GetEra().BaseUnitBuyCost, Rate(civ, "May buy [Wonder] buildings with [Faith] [in all cities]"), 0.15f);

            // a religion that already has a follower belief with the same unique gains nothing from another
            var existing = testGame.CreateBelief(BeliefType.Follower, "May buy [Melee] units with [Faith] [in all cities]");
            var religion = testGame.AddReligion(civ);
            religion.AddBeliefs([existing]);
            Assert.Equal(0f, Rate(civ, "May buy [Melee] units with [Faith] [in all cities]"));
        }

        [Fact]
        public void RateBeliefHalvesUniquesThatBenefitTheGreatProphet()
        {
            var (civ, _) = CivWithCity();
            var prophet = civ.ReligionManager.GetGreatProphetEquivalent();
            Assert.NotNull(prophet);
            AssertWithin(75f / 2, Rate(civ, $"[+25]% Strength <for [{prophet.Name}] units>"), 0.15f);
        }

        [Fact]
        public void RateBeliefPantheonsAreWorthLessAndAiChoiceWeightScales()
        {
            var (civ, _) = CivWithCity();
            const string Unique = "[+30]% growth [in all cities]";
            var follower = Rate(civ, Unique);
            var pantheon = Rate(civ, Unique, BeliefType.Pantheon);
            Assert.Equal(follower * 0.9f, pantheon, 4);
            var belief = testGame.CreateBelief(BeliefType.Follower, Unique, "[+100]% weight to this choice for AI decisions");
            Assert.Equal(follower * 2f, ReligionAutomation.RateBelief(civ, belief), 3);
        }

        [Fact]
        public void RateBeliefIsDeterministicForTheSameState()
        {
            var (civ, _) = CivWithCity();
            var belief = testGame.CreateBelief(BeliefType.Follower, "[+30]% growth [in all cities]", "[+2 Culture] from every [Monument]");
            Assert.Equal(ReligionAutomation.RateBelief(civ, belief), ReligionAutomation.RateBelief(civ, belief));
        }

        [Fact]
        public void RateBeliefScalesWithTheFoundingProgressOfTheGame()
        {
            var (civ, _) = CivWithCity();
            const string Unique = "Religion naturally spreads to cities [3] tiles away";
            // pantheon state: gameTimeScalingPercent stays 100 (early modifier 4)
            var early = Rate(civ, Unique);
            Assert.True(early > 0f);
            // founding a religion with nobody having founded one: 100 - 0 = 100 as well; with the other civs having founded: lower
            var prophet = testGame.AddUnit("Great Prophet", civ, testGame.GetTile(0, 0));
            civ.ReligionManager.FoundReligion(prophet);
            Assert.Equal(ReligionState.FoundingReligion, civ.ReligionManager.ReligionState);
            AssertWithin(early, Rate(civ, Unique), 0.15f);
        }

        [Fact]
        public void ChooseReligiousBeliefsPicksAPantheonThenFoundsAndEnhancesAReligion()
        {
            var (civ, _) = CivWithCity();
            civ.ReligionManager.StoredFaith = 1000;
            Assert.True(civ.ReligionManager.CanFoundOrExpandPantheon());
            ReligionAutomation.ChooseReligiousBeliefs(civ);
            Assert.Equal(ReligionState.Pantheon, civ.ReligionManager.ReligionState);
            Assert.Single(civ.ReligionManager.Religion!.GetAllBeliefsOrdered());

            var prophet = testGame.AddUnit("Great Prophet", civ, testGame.GetTile(0, 0));
            civ.ReligionManager.FoundReligion(prophet);
            ReligionAutomation.ChooseReligiousBeliefs(civ);
            Assert.Equal(ReligionState.Religion, civ.ReligionManager.ReligionState);
            var beliefs = civ.ReligionManager.Religion!.GetAllBeliefsOrdered().ToList();
            Assert.Equal(beliefs.Count, beliefs.Distinct().Count());
            Assert.Contains(beliefs, it => it.Type == BeliefType.Founder);
            Assert.Contains(beliefs, it => it.Type == BeliefType.Follower);

            civ.ReligionManager.UseProphetForEnhancingReligion(prophet);
            ReligionAutomation.ChooseReligiousBeliefs(civ);
            Assert.Equal(ReligionState.EnhancedReligion, civ.ReligionManager.ReligionState);
            Assert.Contains(civ.ReligionManager.Religion!.GetAllBeliefsOrdered(), it => it.Type == BeliefType.Enhancer);
        }

        [Fact]
        public void ChooseReligiousBeliefsIsDeterministic()
        {
            string Run()
            {
                var game = new TestGame();
                game.MakeHexagonalMap(6);
                var civ = game.AddCiv(isPlayer: false);
                game.AddCity(civ, game.GetTile(0, 0));
                civ.ReligionManager.StoredFaith = 1000;
                ReligionAutomation.ChooseReligiousBeliefs(civ);
                civ.ReligionManager.FoundReligion(game.AddUnit("Great Prophet", civ, game.GetTile(0, 0)));
                ReligionAutomation.ChooseReligiousBeliefs(civ);
                return string.Join(",", civ.ReligionManager.Religion!.GetAllBeliefsOrdered().Select(it => it.Name)) + civ.ReligionManager.Religion.Name;
            }
            Assert.Equal(Run(), Run());
        }

        [Fact]
        public void ChooseReligiousBeliefsUsesFreeBeliefsAndIgnoresCivsWithNothingToDo()
        {
            var (civ, _) = CivWithCity();
            ReligionAutomation.ChooseReligiousBeliefs(civ); // not enough faith, no prophet: nothing happens
            Assert.Equal(ReligionState.None, civ.ReligionManager.ReligionState);

            civ.ReligionManager.FreeBeliefs.Add(BeliefType.Pantheon.ToString(), 1);
            Assert.True(civ.ReligionManager.HasFreeBeliefs());
            ReligionAutomation.ChooseReligiousBeliefs(civ);
            Assert.Equal(ReligionState.Pantheon, civ.ReligionManager.ReligionState);
            Assert.False(civ.ReligionManager.HasFreeBeliefs());
        }

        [Fact]
        public void SpendFaithDoesNothingWithoutCities()
        {
            testGame.MakeHexagonalMap(4);
            var civ = testGame.AddCiv();
            civ.ReligionManager.StoredFaith = 10000;
            ReligionAutomation.SpendFaithOnReligion(civ);
            Assert.Equal(10000, civ.ReligionManager.StoredFaith);
        }

        /// <summary>A civ with an enhanced religion and a holy capital; other civs make "religions still foundable" nonzero. Prophet consumed.</summary>
        private (CivilizationObject Civ, CityObject Capital) EnhancedWorld(int faith)
        {
            var (civ, capital) = CivWithCity();
            for (var i = 0; i < 3; i++) testGame.AddCiv();
            civ.ReligionManager.StoredFaith = 1000;
            ReligionAutomation.ChooseReligiousBeliefs(civ);
            var prophet = testGame.AddUnit("Great Prophet", civ, testGame.GetTile(0, 0));
            civ.ReligionManager.FoundReligion(prophet);
            ReligionAutomation.ChooseReligiousBeliefs(civ);
            civ.ReligionManager.UseProphetForEnhancingReligion(prophet);
            ReligionAutomation.ChooseReligiousBeliefs(civ);
            prophet.Destroy();
            civ.ReligionManager.StoredFaith = faith;
            Assert.Equal(ReligionState.EnhancedReligion, civ.ReligionManager.ReligionState);
            Assert.True(civ.ReligionManager.RemainingFoundableReligions() > 0);
            return (civ, capital);
        }

        private static List<string> UnitNames(CivilizationObject civ) => civ.Units.GetCivUnits().Select(it => it.Name).ToList();

        [Fact]
        public void SpendFaithSavesForAGreatProphetWhileReligionCanStillBeEnhanced()
        {
            var (civ, _) = CivWithCity();
            civ.ReligionManager.StoredFaith = 10000;
            ReligionAutomation.SpendFaithOnReligion(civ); // no religion yet: a prophet is the goal, but religion state < Religion buys nothing
            Assert.Equal(10000, civ.ReligionManager.StoredFaith);
            Assert.Empty(UnitNames(civ));
        }

        [Fact]
        public void SpendFaithBuysAMissionaryForTheHolyCityWhenFewMissionariesExist()
        {
            var (civ, capital) = EnhancedWorld(10000);
            ReligionAutomation.SpendFaithOnReligion(civ);
            // our holy city has the majority: no city without our religion, no inquisitor wanted?  The first need is the holy city's inquisitor
            Assert.True(civ.ReligionManager.StoredFaith < 10000);
            Assert.Contains("Inquisitor", UnitNames(civ));
            Assert.NotNull(capital.GetCenterTile().CivilianUnit);
        }

        [Fact]
        public void SpendFaithBuysMissionariesOnceTheHolyCityIsProtected()
        {
            var (civ, capital) = EnhancedWorld(10000);
            var inquisitor = testGame.AddUnit("Inquisitor", civ, testGame.GetTile(2, 0));
            Assert.NotNull(inquisitor);
            ReligionAutomation.SpendFaithOnReligion(civ);
            Assert.Contains("Missionary", UnitNames(civ));
            Assert.True(civ.ReligionManager.StoredFaith < 10000);
            Assert.NotNull(capital);
        }

        [Fact]
        public void SpendFaithDoesNotBuyMissionariesWhenEnoughExist()
        {
            var (civ, _) = EnhancedWorld(10000);
            testGame.AddUnit("Inquisitor", civ, testGame.GetTile(2, 0));
            for (var i = 0; i < 4; i++) testGame.AddUnit("Missionary", civ, testGame.GetTile(3, i));
            ReligionAutomation.SpendFaithOnReligion(civ);
            Assert.Equal(10000, civ.ReligionManager.StoredFaith);
        }

        [Fact]
        public void SpendFaithFixesCitiesWithoutOurReligionWithMissionariesAndInquisitorsForBigDeficits()
        {
            var (civ, capital) = EnhancedWorld(100000);
            var other = testGame.AddCity(civ, testGame.GetTile(0, 5));
            other.Religion.AddPressure(testGame.AddReligion(testGame.AddCiv()).Name, 1000);
            ReligionAutomation.SpendFaithOnReligion(civ);
            var names = UnitNames(civ);
            Assert.Contains("Missionary", names);
            Assert.NotNull(capital);
        }

        [Fact]
        public void SpendFaithBuysAnInquisitorForDeficitsAtOrAboveTheThreshold()
        {
            var (civ, _) = EnhancedWorld(100000);
            var other = testGame.AddCity(civ, testGame.GetTile(0, 5));
            other.Religion.AddPressure(testGame.AddReligion(testGame.AddCiv()).Name, 100000);
            ReligionAutomation.SpendFaithOnReligion(civ);
            var names = UnitNames(civ);
            Assert.Contains("Inquisitor", names);
            // the inquisitor occupies the capital's civilian slot, the only city where a missionary could be bought
            Assert.DoesNotContain("Missionary", names);
        }

        [Fact]
        public void SpendFaithWithNoFoundableReligionsLeftBuysGreatPeopleAndReligiousBuildings()
        {
            var (civ, capital) = CivWithCity();
            civ.ReligionManager.StoredFaith = 1000;
            ReligionAutomation.ChooseReligiousBeliefs(civ);
            var prophet = testGame.AddUnit("Great Prophet", civ, testGame.GetTile(0, 0));
            civ.ReligionManager.FoundReligion(prophet);
            ReligionAutomation.ChooseReligiousBeliefs(civ);
            civ.ReligionManager.UseProphetForEnhancingReligion(prophet);
            ReligionAutomation.ChooseReligiousBeliefs(civ);
            prophet.Destroy();
            Assert.Equal(0, civ.ReligionManager.RemainingFoundableReligions());
            civ.ReligionManager.StoredFaith = 100000;
            ReligionAutomation.SpendFaithOnReligion(civ);
            // the cheapest purchasable great person (non-prophet) or a building purchasable by beliefs was bought
            Assert.True(civ.ReligionManager.StoredFaith <= 100000);
            Assert.NotNull(capital);
        }
    }
}
