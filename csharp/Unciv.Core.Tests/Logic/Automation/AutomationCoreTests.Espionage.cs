using Unciv.Core.Tests.Support;
using Unciv.Logic.Automation.Unit;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Models;
using Unciv.Utils;
using CityObject = Unciv.Logic.City.City;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Core.Tests.Logic.Automation;

// T087. EspionageAutomation.kt has no Kotlin test fixture: C#-only cases, expected values derived by hand from the Kotlin source.
public static partial class AutomationCoreTests
{
    [Collection("RulesetCache")]
    public class EspionageAutomationTests
    {
        private readonly TestGame testGame = new();

        private (CivilizationObject Civ, CityObject Capital) CivWithCapital()
        {
            testGame.MakeHexagonalMap(12);
            var civ = testGame.AddCiv(isPlayer: false);
            var capital = testGame.AddCity(civ, testGame.GetTile(0, 0));
            return (civ, capital);
        }

        [Fact]
        public void FirstSpyGoesToTheCapital()
        {
            var (civ, capital) = CivWithCapital();
            var spy = civ.EspionageManager.AddSpy();
            new EspionageAutomation(civ).AutomateSpies();
            Assert.Same(capital, spy.GetCityOrNull());
            Assert.Equal(SpyAction.Moving, spy.Action);
        }

        [Fact]
        public void SpiesInWorkAreLeftAloneAndDeadOnesToo()
        {
            var (civ, capital) = CivWithCapital();
            var busy = civ.EspionageManager.AddSpy();
            busy.MoveTo(capital); // Moving counts as doing work
            var dead = civ.EspionageManager.AddSpy();
            dead.SetAction(SpyAction.Dead, 3);
            new EspionageAutomation(civ).AutomateSpies();
            Assert.Same(capital, busy.GetCityOrNull());
            Assert.Equal(SpyAction.Dead, dead.Action);
            Assert.Null(dead.GetCityOrNull());
        }

        [Fact]
        public void SecondSpyProtectsAnotherOfOurCities()
        {
            var (civ, capital) = CivWithCapital();
            var second = testGame.AddCity(civ, testGame.GetTile(0, 6));
            civ.EspionageManager.AddSpy().MoveTo(capital);
            var spy = civ.EspionageManager.AddSpy();
            new EspionageAutomation(civ).AutomateSpies();
            Assert.Same(second, spy.GetCityOrNull()); // counter-intelligence in our own city without a spy
        }

        [Fact]
        public void SpyStealsTechFromTheCivWithTheBestCityWhenOneKnowsTechsWeLack()
        {
            var (civ, capital) = CivWithCapital();
            var other = testGame.AddCiv();
            var small = testGame.AddCity(other, testGame.GetTile(8, 0));
            var big = testGame.AddCity(other, testGame.GetTile(8, 6), initialPopulation: 5);
            civ.DiplomacyFunctions.MakeCivilizationsMeet(other);
            small.GetCenterTile().SetExplored(civ, true);
            big.GetCenterTile().SetExplored(civ, true);
            var tech = testGame.Ruleset.Technologies.Keys.First(it => civ.Tech.CanBeResearched(it) && !other.Tech.IsResearched(it));
            other.Tech.AddTechnology(tech);
            Assert.NotEmpty(civ.EspionageManager.GetTechsToSteal(other));

            civ.EspionageManager.AddSpy().MoveTo(capital);
            var spy = civ.EspionageManager.AddSpy();
            for (var turn = 0; turn < 40; turn++) // whichever way the 8-in-10 draw falls, the spy ends up stealing: counter-intelligence is impossible (one city)
            {
                testGame.GameInfo.Turns = turn;
                spy.MoveTo(null);
                new EspionageAutomation(civ).AutomateSpies();
                Assert.Same(big, spy.GetCityOrNull()); // the most populous city
            }
        }

        [Fact]
        public void SpyRigsElectionsInTheCityStateWithTheMostInfluence()
        {
            var (civ, capital) = CivWithCapital();
            var cityStateA = testGame.AddCiv(cityStateType: "Cultured");
            var cityStateB = testGame.AddCiv(cityStateType: "Cultured");
            var cityA = testGame.AddCity(cityStateA, testGame.GetTile(8, 0));
            var cityB = testGame.AddCity(cityStateB, testGame.GetTile(8, 6));
            foreach (var cityState in new[] { cityStateA, cityStateB })
                civ.DiplomacyFunctions.MakeCivilizationsMeet(cityState);
            cityA.GetCenterTile().SetExplored(civ, true);
            cityB.GetCenterTile().SetExplored(civ, true);
            cityStateA.GetDiplomacyManager(civ)!.SetInfluence(10f);
            cityStateB.GetDiplomacyManager(civ)!.SetInfluence(40f);

            civ.EspionageManager.AddSpy().MoveTo(capital);
            var spy = civ.EspionageManager.AddSpy();
            for (var turn = 0; turn < 40; turn++)
            {
                testGame.GameInfo.Turns = turn;
                spy.MoveTo(null);
                new EspionageAutomation(civ).AutomateSpies();
                Assert.Same(cityB, spy.GetCityOrNull());
            }
        }

        [Fact]
        public void SpyWithNothingToDoGoesToTheBestScienceCityElsewhere()
        {
            var (civ, capital) = CivWithCapital();
            var foreign = testGame.AddCiv();
            var foreignCity = testGame.AddCity(foreign, testGame.GetTile(8, 0));
            foreignCity.GetCenterTile().SetExplored(civ, true);
            civ.EspionageManager.AddSpy().MoveTo(capital);
            var spy = civ.EspionageManager.AddSpy();
            new EspionageAutomation(civ).AutomateSpies();
            Assert.Same(foreignCity, spy.GetCityOrNull()); // all else is impossible: the final fallback
        }

        [Fact]
        public void SpyWithNowhereToGoStaysInTheHideout()
        {
            var (civ, capital) = CivWithCapital();
            civ.EspionageManager.AddSpy().MoveTo(capital);
            var spy = civ.EspionageManager.AddSpy();
            new EspionageAutomation(civ).AutomateSpies();
            Assert.Null(spy.GetCityOrNull());
        }

        private (CivilizationObject Civ, Spy Spy, CityObject CityStateCity) CoupSetup(int influence)
        {
            var (civ, capital) = CivWithCapital();
            var cityState = testGame.AddCiv(cityStateType: "Cultured");
            var csCity = testGame.AddCity(cityState, testGame.GetTile(8, 0));
            civ.DiplomacyFunctions.MakeCivilizationsMeet(cityState);
            csCity.GetCenterTile().SetExplored(civ, true);
            cityState.GetDiplomacyManager(civ)!.SetInfluence(influence);
            civ.EspionageManager.AddSpy().MoveTo(capital); // keeps the capital rule out of the way
            var spy = civ.EspionageManager.AddSpy();
            spy.MoveTo(csCity);
            spy.LevelUpSpy(2); // influence below the ally threshold (60) and a rank-3 spy: just above 70%
            spy.SetAction(SpyAction.RiggingElections, 5); // set up, not moved by the automation, but a coup candidate
            return (civ, spy, csCity);
        }

        private static int TurnWithCoupDraw(int spyCount, int spyIndex, bool wantCoup)
        {
            for (var turn = 0; ; turn++)
                if (new GameRandom(spyCount + spyIndex + turn).NextInt(100) < 20 == wantCoup) return turn;
        }

        [Fact]
        public void SpyStagesACoupWhenTheChanceIsHighAndTheDrawIsLow()
        {
            var (civ, spy, _) = CoupSetup(59);
            Assert.True(spy.GetCoupChanceOfSuccess(false) >= 0.7f);
            testGame.GameInfo.Turns = TurnWithCoupDraw(2, 1, true);
            new EspionageAutomation(civ).AutomateSpies();
            Assert.Equal(SpyAction.Coup, spy.Action);
        }

        [Fact]
        public void SpyDoesNotStageACoupOnAHighDrawOrALowChanceOrAfterAFriendlyAlly()
        {
            var (civ, spy, csCity) = CoupSetup(59);
            testGame.GameInfo.Turns = TurnWithCoupDraw(2, 1, false);
            new EspionageAutomation(civ).AutomateSpies();
            Assert.Equal(SpyAction.RiggingElections, spy.Action);

            testGame.GameInfo.Turns = TurnWithCoupDraw(2, 1, true);
            csCity.Civ.GetDiplomacyManager(civ)!.SetInfluence(-50f); // a low chance of success
            Assert.True(spy.GetCoupChanceOfSuccess(false) < 0.7f);
            new EspionageAutomation(civ).AutomateSpies();
            Assert.Equal(SpyAction.RiggingElections, spy.Action);

            csCity.Civ.GetDiplomacyManager(civ)!.SetInfluence(59f);
            var ally = testGame.AddCiv();
            civ.DiplomacyFunctions.MakeCivilizationsMeet(ally);
            ally.DiplomacyFunctions.MakeCivilizationsMeet(csCity.Civ);
            csCity.Civ.AllyCiv = ally;
            civ.GetDiplomacyManager(ally)!.AddModifier(DiplomaticModifiers.DeclarationOfFriendship, 50f);
            Assert.True(civ.GetDiplomacyManager(ally)!.IsRelationshipLevelGE(RelationshipLevel.Friend));
            new EspionageAutomation(civ).AutomateSpies();
            Assert.Equal(SpyAction.RiggingElections, spy.Action); // we do not coup the city-states of our friends
        }
    }
}
