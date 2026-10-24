using Unciv.Core.Tests.Support;
using Unciv.Logic.Automation;
using Unciv.Logic.City;
using Unciv.Models;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.StatTypes;
using Unciv.UI.Screens.VictoryScreen;
using Unciv.View;
using AutomationObject = Unciv.Logic.Automation.Automation;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Core.Tests.Logic.Automation;

// T087. Automation.kt has no Kotlin test fixture: these are C#-only cases, with expected values derived by hand from the Kotlin source.
public static partial class AutomationCoreTests
{
    [Collection("RulesetCache")]
    public class AutomationTests
    {
        private readonly TestGame testGame = new();

        private CivView ViewOf(CivilizationObject civ) => new GameView(testGame.GameInfo, civ).CivView;

        private (CivilizationObject Civ, City City) CivWithCity(bool human = true)
        {
            testGame.MakeHexagonalMap(8, Constants.Grassland);
            var civ = testGame.AddCiv(isPlayer: human);
            var city = testGame.AddCity(civ, testGame.GetTile(0, 0));
            city.CityStats.Update();
            return (civ, city);
        }

        [Fact]
        public void RankStatsValueWeightsFoodAndDividesGoldUnlessBroke()
        {
            var (civ, _) = CivWithCity();
            var stats = new Stats(production: 1f, food: 2f, gold: 3f, science: 4f, culture: 5f, happiness: 6f, faith: 7f);
            // food * 1.2 + gold / 3 + the other stats
            var expected = 0f;
            expected += 2f * 1.2f;
            expected += 3f / 3;
            expected += 6f;
            expected += 1f;
            expected += 4f;
            expected += 5f;
            expected += 7f;
            Assert.Equal(expected, AutomationObject.RankStatsValue(stats, ViewOf(civ)));

            civ.AddGold(-10 - civ.Gold);
            civ.Stats.StatsForNextTurn.Gold = -1f;
            Assert.Equal(expected + 3f - 3f / 3, AutomationObject.RankStatsValue(stats, ViewOf(civ)), 4);
        }

        [Fact]
        public void RankTileIsZeroForOtherCivsAndAddsImprovementPotential()
        {
            var (civ, city) = CivWithCity();
            var other = testGame.AddCiv();
            var tile = testGame.GetTile(2, 0);
            Assert.Equal(0f, AutomationObject.RankTile(null, ViewOf(civ)));

            var stats = tile.Stats.GetTileStats(null, civ);
            Assert.Equal(AutomationObject.RankStatsValue(stats, ViewOf(civ)) + 0.5f, AutomationObject.RankTile(tile, ViewOf(civ)));

            // an improved tile loses the improvement potential
            tile.SetImprovement("Farm");
            var improvedStats = tile.Stats.GetTileStats(null, civ);
            Assert.Equal(AutomationObject.RankStatsValue(improvedStats, ViewOf(civ)), AutomationObject.RankTile(tile, ViewOf(civ)));
            // a pillaged one gets 0.6 back
            tile.SetPillaged();
            var pillagedStats = tile.Stats.GetTileStats(null, civ);
            Assert.Equal(AutomationObject.RankStatsValue(pillagedStats, ViewOf(civ)) + 0.6f, AutomationObject.RankTile(tile, ViewOf(civ)), 4);

            // a tile of another civ is useless to us; one of ours is not
            var foreignCity = testGame.AddCity(other, testGame.GetTile(-5, 0));
            tile.SetOwningCity(foreignCity);
            Assert.Equal(0f, AutomationObject.RankTile(tile, ViewOf(civ)));
            tile.SetOwningCity(city);
            Assert.NotEqual(0f, AutomationObject.RankTile(tile, ViewOf(civ)));
        }

        [Fact]
        public void RankTileCountsVisibleResources()
        {
            var (civ, _) = CivWithCity();
            var luxury = testGame.Ruleset.TileResources.Values.First(it => it.ResourceType == ResourceType.Luxury && it.RevealedBy == null);
            var tile = testGame.GetTile(2, 0);
            tile.TileResource = luxury;
            var stats = tile.Stats.GetTileStats(null, civ);
            // luxury: +1 for usage, +1 for improvement potential, plus the base +0.5
            Assert.Equal(AutomationObject.RankStatsValue(stats, ViewOf(civ)) + 0.5f + 1f + 1f, AutomationObject.RankTile(tile, ViewOf(civ)), 4);
        }

        [Fact]
        public void RankTileForCityWorkAndRankSpecialist()
        {
            var (_, city) = CivWithCity();
            var tile = testGame.GetTile(1, 0);
            var tileStats = tile.Stats.GetTileStats(city, city.Civ);
            Assert.Equal(AutomationObject.RankStatsForCityWork(tileStats, city, false), AutomationObject.RankTileForCityWork(tile, city));

            var name = testGame.CreateSpecialist();
            testGame.Ruleset.Specialists[name].GreatPersonPoints = new Counter<string> { ["Great Scientist"] = 3 };
            city.CurrentGPPBonus = 100;
            var withBonus = AutomationObject.RankSpecialist(name, city);
            city.CurrentGPPBonus = 0;
            var without = AutomationObject.RankSpecialist(name, city);
            Assert.Equal(3f, withBonus - without, 4); // 3 points, doubled by +100%, minus the unboosted 3
            Assert.Equal(AutomationObject.RankStatsForCityWork(city.CityStats.GetStatsOfSpecialist(name), city, true) + 3f, without, 4);

            // a specialist that the ruleset does not know gets no GPP score (total remake mods)
            testGame.Ruleset.Specialists.Remove(name);
            Assert.Equal(AutomationObject.RankStatsForCityWork(city.CityStats.GetStatsOfSpecialist(name), city, true), AutomationObject.RankSpecialist(name, city));
        }

        [Fact]
        public void RankStatsForCityWorkSpecialistBoostAndSmallCityScience()
        {
            var (_, city) = CivWithCity(human: false);
            // 3 science (and 5+) get x1.3 for specialists only; 4 does not
            var three = new Stats(science: 3f);
            Assert.True(AutomationObject.RankStatsForCityWork(three, city, true) > AutomationObject.RankStatsForCityWork(three, city, false));
            var four = new Stats(science: 4f);
            Assert.Equal(AutomationObject.RankStatsForCityWork(four, city, false), AutomationObject.RankStatsForCityWork(four, city, true));
            var five = new Stats(science: 5f);
            Assert.True(AutomationObject.RankStatsForCityWork(five, city, true) > AutomationObject.RankStatsForCityWork(five, city, false));
        }

        [Fact]
        public void RankStatsForCityWorkWeighsFoodGoldHappinessAndProduction()
        {
            var (civ, city) = CivWithCity(human: false);
            var food = new Stats(food: 2f);
            var growing = AutomationObject.RankStatsForCityWork(food, city, false);
            Assert.True(growing > 0f);
            city.AvoidGrowth = true; // only food that stops starvation counts
            Assert.Equal(0f, AutomationObject.RankStatsForCityWork(food, city, false));
            city.AvoidGrowth = false;

            var gold = new Stats(gold: 2f);
            var normalGold = AutomationObject.RankStatsForCityWork(gold, city, false);
            civ.Stats.StatsForNextTurn.Gold = -5f; // a gold problem doubles gold
            Assert.Equal(normalGold * 2, AutomationObject.RankStatsForCityWork(gold, city, false), 4);
            civ.Stats.StatsForNextTurn.Gold = 5f;

            var happiness = new Stats(happiness: 2f);
            civ.Stats.Happiness = 0; // below 5 doubles happiness
            var unhappy = AutomationObject.RankStatsForCityWork(happiness, city, false);
            civ.Stats.Happiness = 100;
            Assert.Equal(unhappy / 2, AutomationObject.RankStatsForCityWork(happiness, city, false), 4);
            civ.Stats.Happiness = 0;

            // all techs researched: science is useless
            foreach (var tech in testGame.Ruleset.Technologies.Keys) civ.Tech.AddTechnology(tech);
            Assert.Equal(0f, AutomationObject.RankStatsForCityWork(new Stats(science: 10f), city, false));
        }

        [Fact]
        public void RankStatsForCityWorkAppliesTheCityFocus()
        {
            var (_, city) = CivWithCity(human: false);
            var gold = new Stats(gold: 10f);
            var noFocus = AutomationObject.RankStatsForCityWork(gold, city, false);
            city.SetCityFocus(CityFocus.GoldFocus);
            Assert.Equal(noFocus * CityFocus.GoldFocus.GetStatMultiplier(Stat.Gold), AutomationObject.RankStatsForCityWork(gold, city, false), 4);
            // a stat that is zero skips the multiplier
            Assert.Equal(0f, AutomationObject.RankStatsForCityWork(new Stats(), city, false));
        }

        [Fact]
        public void ThreatAssessmentFollowsThePowerRatioBands()
        {
            testGame.MakeHexagonalMap(10);
            var a = testGame.AddCiv();
            var b = testGame.AddCiv();
            var foreignA = new GameView(testGame.GameInfo, b).GetForeignCivView(a);
            var foreignB = new GameView(testGame.GameInfo, a).GetForeignCivView(b);
            var tiles = testGame.TileMap.Values.ToList();
            for (var i = 0; i < 6; i++) testGame.AddUnit("Warrior", a, tiles[i]);
            a.ResetMilitaryMightCache();
            var bTiles = tiles.Skip(30).ToList();

            // grow b one warrior at a time: a's view of b walks through Medium, High and VeryHigh, b's view of a through Medium, Low, VeryLow
            var seenAssessedByA = new HashSet<ThreatLevel>();
            var seenAssessedByB = new HashSet<ThreatLevel>();
            for (var i = 0; i < 18; i++)
            {
                testGame.AddUnit("Warrior", b, bTiles[i]);
                b.ResetMilitaryMightCache();
                a.ResetMilitaryMightCache();
                var ratio = b.GetStatForRanking(RankingType.Force) / (float)a.GetStatForRanking(RankingType.Force);
                var expectedByA = ratio > 2 ? ThreatLevel.VeryHigh : ratio > 1.5f ? ThreatLevel.High : ratio < 0.5f ? ThreatLevel.VeryLow : ratio < 1 / 1.5f ? ThreatLevel.Low : ThreatLevel.Medium;
                Assert.Equal(expectedByA, AutomationObject.ThreatAssessment(ViewOf(a), foreignB));
                seenAssessedByA.Add(expectedByA);
                var reverse = a.GetStatForRanking(RankingType.Force) / (float)b.GetStatForRanking(RankingType.Force);
                var expectedByB = reverse > 2 ? ThreatLevel.VeryHigh : reverse > 1.5f ? ThreatLevel.High : reverse < 0.5f ? ThreatLevel.VeryLow : reverse < 1 / 1.5f ? ThreatLevel.Low : ThreatLevel.Medium;
                var actualByB = AutomationObject.ThreatAssessment(ViewOf(b), foreignA);
                Assert.Equal(expectedByB, actualByB);
                seenAssessedByB.Add(actualByB);
            }
            Assert.Contains(ThreatLevel.Medium, seenAssessedByA);
            Assert.Contains(ThreatLevel.High, seenAssessedByA);
            Assert.Contains(ThreatLevel.VeryHigh, seenAssessedByA);
            Assert.Contains(ThreatLevel.Low, seenAssessedByB);
            Assert.Contains(ThreatLevel.VeryLow, seenAssessedByB);
        }

        [Fact]
        public void AfraidOfBarbariansBranches()
        {
            var (civ, _) = CivWithCity();
            testGame.GameInfo.Turns = 1;
            Assert.True(AutomationObject.AfraidOfBarbarians(ViewOf(civ)));

            testGame.GameInfo.GameParameters.NoBarbarians = true;
            Assert.False(AutomationObject.AfraidOfBarbarians(ViewOf(civ)));
            testGame.GameInfo.GameParameters.NoBarbarians = false;

            testGame.GameInfo.Turns = 100000; // very late in the game: not afraid
            Assert.False(AutomationObject.AfraidOfBarbarians(ViewOf(civ)));
            testGame.GameInfo.Turns = 1;

            var cityState = testGame.AddCiv(cityStateType: "Cultured");
            Assert.False(AutomationObject.AfraidOfBarbarians(ViewOf(cityState)));
            Assert.False(AutomationObject.AfraidOfBarbarians(ViewOf(testGame.AddCiv()))); // no cities

            for (var i = 0; i < 4; i++) testGame.AddUnit("Warrior", civ, testGame.GetTile(i + 1, 0)); // 4 units are enough
            Assert.False(AutomationObject.AfraidOfBarbarians(ViewOf(civ)));
        }

        [Fact]
        public void AfraidOfBarbariansRagingBarbariansAndSeenContinent()
        {
            var (civ, _) = CivWithCity();
            testGame.GameInfo.GameParameters.RagingBarbarians = true;
            testGame.GameInfo.Turns = 1;
            for (var i = 0; i < 4; i++) testGame.AddUnit("Warrior", civ, testGame.GetTile(i + 1, 0));
            Assert.True(AutomationObject.AfraidOfBarbarians(ViewOf(civ))); // 4 < 4 * 1.3 with raging barbarians
            testGame.GameInfo.Turns = 190; // past the early game: the multiplier halves (4 units >= 4 * 0.65), but before the late game
            Assert.False(AutomationObject.AfraidOfBarbarians(ViewOf(civ)));
        }

        [Fact]
        public void AfraidOfBarbariansNotWhenTheStartingContinentIsSeen()
        {
            testGame.MakeHexagonalMap(2, Constants.Grassland); // the city sees the whole continent of 19 tiles
            var civ = testGame.AddCiv(isPlayer: true);
            testGame.AddCity(civ, testGame.GetTile(0, 0));
            civ.Cache.UpdateViewableTiles();
            testGame.GameInfo.Turns = 1;
            Assert.Equal(19, civ.ViewableTiles.Count);
            testGame.GameInfo.GameParameters.RagingBarbarians = true; // multiplier 1.3: 19 * 1.3 > 19
            Assert.False(AutomationObject.AfraidOfBarbarians(ViewOf(civ)));
            testGame.GameInfo.GameParameters.RagingBarbarians = false; // multiplier 1: 19 < 19 is false, so we stay afraid
            Assert.True(AutomationObject.AfraidOfBarbarians(ViewOf(civ)));
        }

        [Fact]
        public void AllowSpendingResourceRules()
        {
            var (civ, city) = CivWithCity();
            var noResource = testGame.CreateBaseUnit("Melee");
            Assert.True(AutomationObject.AllowSpendingResource(ViewOf(civ), noResource));
            Assert.True(AutomationObject.AllowAutomatedConstruction(ViewOf(civ), city, noResource));

            var iron = testGame.Ruleset.TileResources["Iron"];
            var cityState = testGame.AddCiv(cityStateType: "Cultured");
            var needsIron = testGame.CreateBaseUnit("Melee", "Consumes [1] [Iron]");
            var ironBuilding = testGame.CreateBuilding("Consumes [1] [Iron]");
            Assert.True(AutomationObject.AllowSpendingResource(ViewOf(cityState), needsIron)); // city states do whatever they want

            // nothing is reserved for both kinds of use: allowed
            Assert.True(AutomationObject.AllowSpendingResource(ViewOf(civ), needsIron));

            // Iron is used by buildings and by units, 4 in stock and units already use 3: a further unit is refused, a building is not
            civ.Cache.LastEraResourceUsedForBuilding["Iron"] = 0;
            civ.Cache.LastEraResourceUsedForUnit["Iron"] = 99;
            civ.DetailedCivResources.Add(iron, "Tiles", 4);
            civ.DetailedCivResources.Add(iron, "Units", -3);
            civ.SummarizedCivResourceSupply.Add(iron, "All", 1);
            Assert.False(AutomationObject.AllowSpendingResource(ViewOf(civ), needsIron));
            Assert.True(AutomationObject.AllowSpendingResource(ViewOf(civ), ironBuilding, city));
            // buildings in turn are refused when the buildings already take the stock
            civ.DetailedCivResources.Add(iron, "Buildings", -5);
            Assert.False(AutomationObject.AllowSpendingResource(ViewOf(civ), ironBuilding, city));

            // units of a past era are not saved for
            civ.Cache.LastEraResourceUsedForUnit["Iron"] = -1;
            Assert.True(AutomationObject.AllowSpendingResource(ViewOf(civ), needsIron));
        }

        [Fact]
        public void AllowSpendingResourceKeepsSpaceshipResourcesAndPartsAllowed()
        {
            var (civ, _) = CivWithCity();
            var part = testGame.CreateBaseUnit("Melee");
            testGame.GameInfo.SpaceResources.Add(part.Name);
            Assert.True(AutomationObject.AllowSpendingResource(ViewOf(civ), part));

            var aluminum = testGame.Ruleset.TileResources["Aluminum"];
            testGame.GameInfo.SpaceResources.Add("Aluminum");
            var needsAluminum = testGame.CreateBaseUnit("Melee", "Consumes [1] [Aluminum]");
            civ.SummarizedCivResourceSupply.Add(aluminum, "All", 2);
            Assert.False(AutomationObject.AllowSpendingResource(ViewOf(civ), needsAluminum)); // 2 - 1 < 2: keep it for the spaceship
            civ.SummarizedCivResourceSupply.Add(aluminum, "All", 5);
            Assert.True(AutomationObject.AllowSpendingResource(ViewOf(civ), needsAluminum));
        }

        [Fact]
        public void AllowSpendingResourceCountsFutureConstructionsOfOtherCities()
        {
            var (civ, city) = CivWithCity();
            var iron = testGame.Ruleset.TileResources["Iron"];
            testGame.GameInfo.SpaceResources.Add("Iron");
            var needsIron = testGame.CreateBaseUnit("Melee", "Consumes [1] [Iron]");
            var ironBuilding = testGame.CreateBuilding("Consumes [1] [Iron]");
            civ.SummarizedCivResourceSupply.Add(iron, "All", 3);
            Assert.True(AutomationObject.AllowSpendingResource(ViewOf(civ), needsIron)); // 3 - 1 >= 2
            city.CityConstructions.SetCurrentConstruction(ironBuilding.Name);
            Assert.False(AutomationObject.AllowSpendingResource(ViewOf(civ), needsIron)); // the building under construction needs one too: 3 - 1 - 1 < 2
            city.CityConstructions.SetCurrentConstruction(needsIron.Name);
            Assert.False(AutomationObject.AllowSpendingResource(ViewOf(civ), ironBuilding, city)); // so does a unit under construction
        }

        [Fact]
        public void ChooseMilitaryUnitAndTryTrainMilitaryUnit()
        {
            var (_, city) = CivWithCity();
            var chosen = AutomationObject.ChooseMilitaryUnit(city, testGame.Ruleset.Units.Values);
            Assert.NotNull(chosen);
            Assert.True(chosen.IsMilitary);
            Assert.Null(AutomationObject.ChooseMilitaryUnit(city, []));

            // a military unit in production stays; a civilian one is replaced
            city.CityConstructions.SetCurrentConstruction(chosen.Name);
            Assert.Same(chosen, AutomationObject.ChooseMilitaryUnit(city, []));
            AutomationObject.TryTrainMilitaryUnit(city);
            Assert.Equal(chosen.Name, city.CityConstructions.GetCurrentConstruction().Name);

            city.CityConstructions.SetCurrentConstruction("Worker");
            AutomationObject.TryTrainMilitaryUnit(city);
            Assert.True(testGame.Ruleset.Units[city.CityConstructions.GetCurrentConstruction().Name].IsMilitary);

            city.IsPuppet = true;
            city.CityConstructions.SetCurrentConstruction("Worker");
            AutomationObject.TryTrainMilitaryUnit(city);
            Assert.Equal("Worker", city.CityConstructions.GetCurrentConstruction().Name);
        }

        [Fact]
        public void ChooseMilitaryUnitPrefersARangedDefenderForCitiesWithoutAMilitaryUnit()
        {
            var (_, city) = CivWithCity();
            var ranged = testGame.CreateBaseUnit("Archery");
            ranged.Strength = 5;
            ranged.RangedStrength = 7;
            ranged.Cost = 40;
            var costlyRanged = testGame.CreateBaseUnit("Archery");
            costlyRanged.Strength = 5;
            costlyRanged.RangedStrength = 9;
            costlyRanged.Cost = 70;
            var melee = testGame.CreateBaseUnit("Melee");
            melee.Strength = 8;
            melee.Cost = 20;
            // the city is at peace and has no military unit: the most expensive ranged unit
            Assert.Same(costlyRanged, AutomationObject.ChooseMilitaryUnit(city, [melee, ranged, costlyRanged]));
        }

        [Fact]
        public void GetTileForConstructionImprovementPicksTheBestAllowedTile()
        {
            var (civ, city) = CivWithCity();
            var improvement = testGame.CreateTileImprovement();
            Assert.Null(AutomationObject.GetTileForConstructionImprovement(city, improvement)); // buildable nowhere

            var custom = testGame.CreateTileImprovement();
            custom.TerrainsCanBeBuiltOn.Add(Constants.Grassland);
            custom.Food = 3f;
            var chosen = AutomationObject.GetTileForConstructionImprovement(city, custom);
            Assert.NotNull(chosen);
            Assert.Contains(chosen, city.GetTiles());
            Assert.NotNull(civ);
        }
    }
}
