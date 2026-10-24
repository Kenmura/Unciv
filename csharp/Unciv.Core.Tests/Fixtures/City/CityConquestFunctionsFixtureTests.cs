using Unciv.Core.Tests.Support;
using Unciv.Logic.City.Managers;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Translations;
using CityObject = Unciv.Logic.City.City;

namespace Unciv.Core.Tests.Fixtures.CityFixtures;

// T072: port of tests/src/com/unciv/logic/city/CityConquestFunctionsTest.kt over the real TestGame.
[Collection("RulesetCache")]
public sealed class CityConquestFunctionsFixtureTests
{
    private readonly TestGame testGame = new();
    private readonly Civilization attackerCiv;
    private Civilization defenderCiv;
    private CityObject defenderCity;
    private CityConquestFunctions cityConquestFunctions;

    private readonly string gainFreeBuildingTestingUniqueText = UniqueType.GainFreeBuildings.Text().FillPlaceholders("Monument", "in all cities");

    public CityConquestFunctionsFixtureTests()
    {
        testGame.MakeHexagonalMap(3);
        attackerCiv = testGame.AddCiv();
        defenderCiv = testGame.AddCiv();

        defenderCity = AddCity(defenderCiv, testGame.GetTile(HexCoord.Zero), initialPopulation: 10);
        cityConquestFunctions = new CityConquestFunctions(defenderCity);

        // otherwise test crashes when puppetying city
        testGame.GameInfo.CurrentPlayerCiv = testGame.AddCiv();
        testGame.GameInfo.CurrentPlayer = testGame.GameInfo.CurrentPlayerCiv.CivId;
    }

    /// <summary>Battle.destroyIfDefeated is a no-op stand-in installed on every city (the tests never look at it).</summary>
    private sealed class StandInConquestExternals : ICityConquestExternals
    {
        public void DestroyIfDefeated(Civilization attackedCiv, Civilization attacker, HexCoord? notificationLocation) { }
        public void AcceptTrade(Civilization ourCiv, Civilization theirCiv, Unciv.Logic.Trade.Trade trade) { }
    }

    private CityObject AddCity(Civilization civ, Unciv.Logic.Map.Tiles.Tile tile, bool replacePalace = false, int initialPopulation = 1)
    {
        var city = testGame.AddCity(civ, tile, replacePalace, initialPopulation);
        city.ConquestExternals = new StandInConquestExternals();
        return city;
    }

    // Note: diplomacy repercussions for capturing/liberating cities are tested in DiplomacyManagerTest

    [Fact]
    public void ShouldChangeOwnerUponCapture()
    {
        cityConquestFunctions.PuppetCity(attackerCiv);

        Assert.Same(attackerCiv, defenderCity.Civ);
        Assert.Equal(defenderCiv.CivId, defenderCity.PreviousOwner);
    }

    [Fact]
    public void ShouldPlunderGoldUponCapture()
    {
        void ResetPlunderState()
        {
            testGame.GameInfo.Turns = 50;
            attackerCiv.AddGold(-attackerCiv.Gold);
            if (defenderCity.Civ == defenderCiv) return;
            cityConquestFunctions.PuppetCity(defenderCiv);
            defenderCity.Population.SetPopulation(10);
            defenderCity.TurnAcquired = 0;
        }

        // Do not assume because TileBasedRandom ensures deterministic results to hardcode those
        // Ranges are from hardcoded function in getGoldForCapturingCity - all modifiers are == 1
        // Repeat test without reinitializing the CityConquestFunctions.tileBasedRandom to get coverage
        for (var i = 0; i < 20; i++)
        {
            ResetPlunderState();

            cityConquestFunctions.PuppetCity(attackerCiv);

            Assert.InRange(attackerCiv.Gold, 120, 159); // Range for city size 10 and all modifiers 1
        }
    }

    [Fact]
    public void ShouldPlunderMoreGoldTheMorePeopleLiveInTheCapturedCity()
    {
        var largeCity = AddCity(defenderCiv, testGame.GetTile(1, 0), initialPopulation: 20);
        var smallCity = AddCity(defenderCiv, testGame.GetTile(0, 1), initialPopulation: 5);
        var secondAttackerCiv = testGame.AddCiv();
        testGame.GameInfo.Turns = 50;

        new CityConquestFunctions(largeCity).PuppetCity(attackerCiv);
        new CityConquestFunctions(smallCity).PuppetCity(secondAttackerCiv);

        Assert.True(attackerCiv.Gold > secondAttackerCiv.Gold);
        Assert.InRange(attackerCiv.Gold, 220, 259); // population 20
        Assert.InRange(secondAttackerCiv.Gold, 70, 109); // population 5
    }

    [Fact]
    public void ShouldPlunderMoreGoldWithBuildingUniquesUponCapture()
    {
        var building = testGame.CreateBuilding(UniqueType.GoldFromCapturingCity.Text().FillPlaceholders("+100"));
        var city = AddCity(defenderCiv, testGame.GetTile(1, 0), initialPopulation: 10);
        city.CityConstructions.AddBuilding(building.Name);

        var secondAttackerCiv = testGame.AddCiv();
        testGame.GameInfo.Turns = 50;

        new CityConquestFunctions(city).PuppetCity(secondAttackerCiv);
        cityConquestFunctions.PuppetCity(attackerCiv);

        Assert.True(attackerCiv.Gold < secondAttackerCiv.Gold);
        Assert.InRange(attackerCiv.Gold, 120, 159); // Range for city size 10 and all modifiers 1
        Assert.InRange(secondAttackerCiv.Gold, 240, 319); // Doubled by Unique
    }

    [Fact]
    public void ABurialTombEquivalentShouldOnlyApplyPerCity()
    {
        // A building is assumed to give empire-wide bonuses unless this conditional is applied
        // See Unique.isLocalEffect
        var uniqueTemplate = UniqueType.GoldFromCapturingCity.Text() + " <" + UniqueType.ConditionalInThisCity.Text() + ">";
        // replace defender civ with one that has a Nation-wide 'bonus' multiplying plundered gold by 1.5
        defenderCiv = testGame.AddCiv([uniqueTemplate.FillPlaceholders("+50")]);
        // define building giving multiplier 2
        var building = testGame.CreateBuilding(uniqueTemplate.FillPlaceholders("+100"));

        // add six cities, each with that building (but we'll conquer only the last of them)
        for (var clockDirection = 2; clockDirection <= 12; clockDirection += 2)
        {
            var pos = HexMath.GetClockPositionToHexcoord(clockDirection).Times(3);
            defenderCity = AddCity(defenderCiv, testGame.GetTile(pos), initialPopulation: 4);
            defenderCity.CityConstructions.AddBuilding(building.Name);
        }
        cityConquestFunctions = new CityConquestFunctions(defenderCity);

        testGame.GameInfo.Turns = 50; // turnModifier = 1
        const int baseGoldFirst = 60, baseGoldLast = 99; // for city size 4
        var expectedFirst = baseGoldFirst * 3;
        var expectedLast = baseGoldLast * 3; // expected modifiers: 2 * 1.5

        cityConquestFunctions.PuppetCity(attackerCiv);

        Assert.True(attackerCiv.Gold >= expectedFirst && attackerCiv.Gold <= expectedLast,
            $"plundered gold should be in range {expectedFirst}..{expectedLast}, actual={attackerCiv.Gold}");
    }

    [Fact]
    public void ShouldNeverDestroyGlobalWondersUponCapture()
    {
        var wonders = new List<string>();
        // remove randomness
        for (var it = 0; it < 20; it++)
        {
            var wonder = testGame.CreateWonder(UniqueType.ScienceFromResearchAgreements.Text().FillPlaceholders($"{it}"));
            defenderCity.CityConstructions.AddBuilding(wonder.Name);
            wonders.Add(wonder.Name);
        }

        cityConquestFunctions.PuppetCity(attackerCiv);

        foreach (var wonder in wonders)
            Assert.Contains(wonder, defenderCity.CityConstructions.BuiltBuildings);
    }

    [Fact]
    public void ShouldAlwaysDestroyNationalWondersUponCapture()
    {
        var wonders = new List<string>();
        // remove randomness
        for (var it = 0; it < 20; it++)
        {
            var nationalWonder = testGame.CreateBuilding();
            nationalWonder.IsNationalWonder = true;
            defenderCity.CityConstructions.AddBuilding(nationalWonder.Name);
            wonders.Add(nationalWonder.Name);
        }

        cityConquestFunctions.PuppetCity(attackerCiv);

        foreach (var wonder in wonders)
            Assert.DoesNotContain(wonder, defenderCity.CityConstructions.BuiltBuildings);
    }

    [Fact]
    public void ShouldKillPopulationUponCapture()
    {
        var largeCity = AddCity(defenderCiv, testGame.GetTile(1, 0), initialPopulation: 20);
        var smallCity = AddCity(defenderCiv, testGame.GetTile(0, 1), initialPopulation: 5);

        cityConquestFunctions.PuppetCity(attackerCiv);
        new CityConquestFunctions(largeCity).PuppetCity(attackerCiv);
        new CityConquestFunctions(smallCity).PuppetCity(attackerCiv);

        Assert.Equal(7, defenderCity.Population.Population);
        Assert.Equal(14, largeCity.Population.Population);
        Assert.Equal(3, smallCity.Population.Population);
    }

    [Fact]
    public void ShouldNeverReducePopulationBelow1UponCapture()
    {
        var onePopCity = AddCity(defenderCiv, testGame.GetTile(1, 0), initialPopulation: 1);

        new CityConquestFunctions(onePopCity).PuppetCity(attackerCiv);

        Assert.Equal(1, onePopCity.Population.Population);
    }

    [Fact]
    public void ShouldSetCityInResistanceUponCapture()
    {
        cityConquestFunctions.PuppetCity(attackerCiv);
        Assert.True(defenderCity.IsInResistance());
    }

    [Fact]
    public void ShouldRelocateCapitalToLargestUponCityMoving()
    {
        var smallerCity = AddCity(defenderCiv, testGame.GetTile(1, 0), initialPopulation: 5);
        var largerCity = AddCity(defenderCiv, testGame.GetTile(0, 1), initialPopulation: 8);

        cityConquestFunctions.MoveToCiv(attackerCiv);

        Assert.True(largerCity.IsCapital());
        Assert.False(smallerCity.IsCapital());
    }

    [Fact]
    public void ShouldRemoveFreeBuildingsUponCityMoving()
    {
        var freeBuildingCiv = testGame.AddCiv([gainFreeBuildingTestingUniqueText]);
        var city = AddCity(freeBuildingCiv, testGame.GetTile(0, 1), initialPopulation: 10);

        new CityConquestFunctions(city).MoveToCiv(attackerCiv);

        Assert.False(city.CityConstructions.ContainsBuildingOrEquivalent("Monument"));
    }

    [Fact]
    public void ShouldGiveFreeBuildingsUponCityMoving()
    {
        var freeBuildingCiv = testGame.AddCiv([gainFreeBuildingTestingUniqueText]);

        cityConquestFunctions.MoveToCiv(freeBuildingCiv);

        Assert.True(defenderCity.CityConstructions.ContainsBuildingOrEquivalent("Monument"));
    }

    [Fact]
    public void ShouldAnnexCity()
    {
        cityConquestFunctions.PuppetCity(attackerCiv);

        cityConquestFunctions.AnnexCity();

        Assert.False(defenderCity.IsPuppet);
    }
}
