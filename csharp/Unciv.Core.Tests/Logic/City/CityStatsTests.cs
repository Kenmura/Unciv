using Unciv.Core.Tests.Models.Ruleset.Unique;
using Unciv.Core.Tests.Support;
using Unciv.Logic.City;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using Unciv.Logic.Map.Tiles;
using Unciv.Models;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Ruleset.Unit;
using Unciv.Models.StatTypes;
using CityObject = Unciv.Logic.City.City;
using NationObject = Unciv.Models.Ruleset.Nation.Nation;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Core.Tests.Logic.CityTests;

// T062: CityStats, StatTreeNode and GreatPersonPointsBreakdown over a real Civilization / TileMap with fake sub-objects
// (the TestGame-based stat cases of CityTest.kt are ported with T072).
public class CityStatsTests
{
    private sealed record World(Civilization Civ, FakeGameInfoState Info, TileMap Map, FakeCityState Fake)
    {
        public CityObject City => Fake.City;
        public CityStats Stats { get; } = new(Fake.City);
    }

    /// <summary>A civ with one city at (2,2) on a 5x5 grassland map, with SetTransients run and a real <see cref="CityStats"/> (not the fake).</summary>
    private static World NewWorld(Action<Ruleset>? configure = null, string[]? nationUniques = null, bool human = true)
    {
        var ruleset = new Ruleset();
        ruleset.Terrains.Put("Grassland", new Terrain { Name = "Grassland", Type = TerrainType.Land, Food = 2f });
        configure?.Invoke(ruleset);
        var info = new FakeGameInfoState { RulesetData = ruleset };
        var map = new TileMap(25) { GameInfo = info };
        for (var x = 0; x < 5; x++)
            for (var y = 0; y < 5; y++)
                map.TileList.Add(new TileClass { Position = HexCoord.Of(x, y), BaseTerrain = "Grassland" });
        map.SetTransients(ruleset);
        info.Map = map;
        var civ = new Civilization(new NationObject { Name = "Rome", Uniques = nationUniques?.ToList() ?? [] })
        {
            CivId = "Rome",
            GameInfo = info,
            PlayerType = human ? PlayerType.Human : PlayerType.AI,
            CityStateFunctions = new FakeCityStateFunctions(),
        };
        civ.State = new GameContext(civ);
        civ.Tech.CivInfo = civ;
        civ.Policies.CivInfo = civ;
        civ.GoldenAges.CivInfo = civ;
        civ.GreatPeople.CivInfo = civ;
        civ.CivConstructions.SetTransients(civ);
        civ.EspionageManager.SetTransients(civ);
        info.Civs.Add(civ);
        var fake = new FakeCityState("c1", new FakeCiv("Rome")) { CivData = civ, LocationData = HexCoord.Of(2, 2) };
        civ.Cities = [fake];
        fake.City.SetTransients(civ);
        return new World(civ, info, map, fake);
    }

    // ---- StatTreeNode

    [Fact]
    public void StatTreeNode_AddStats_BuildsTheHierarchy_AndTotalsIt()
    {
        var tree = new StatTreeNode();
        tree.AddStats(new Stats(food: 1f), "A", "B");
        tree.AddStats(new Stats(food: 2f, gold: 3f), "A");
        tree.AddStats(new Stats(production: 4f));
        tree.AddStats(new Stats(), "ignored");
        tree.AddStats(null, "ignored");
        Assert.Equal(["A"], tree.Children.Keys);
        Assert.Equal(["B"], tree.Children["A"].Children.Keys);
        Assert.Equal(1f, tree.Children["A"].Children["B"].TotalStats.Food);
        Assert.Equal(3f, tree.Children["A"].TotalStats.Food);
        Assert.Equal(new Stats(food: 3f, gold: 3f, production: 4f).ToString(), tree.TotalStats.ToString());
    }

    [Fact]
    public void StatTreeNode_AddStats_CopiesTheStats_AndAddsToTheSameLeaf()
    {
        var stats = new Stats(food: 1f);
        var tree = new StatTreeNode();
        tree.AddStats(stats, "A");
        tree.AddStats(stats, "A");
        Assert.Equal(1f, stats.Food); // not referenced
        Assert.Equal(2f, tree.TotalStats.Food);
    }

    [Fact]
    public void StatTreeNode_SetInnerStat_Add_AndClone()
    {
        var tree = new StatTreeNode();
        tree.SetInnerStat(Stat.Gold, 5f);
        var other = new StatTreeNode();
        other.AddStats(new Stats(gold: 1f), "X");
        other.AddStats(new Stats(gold: 2f), "Y");
        tree.AddStats(new Stats(gold: 10f), "X");
        tree.Add(other);
        Assert.Equal(["X", "Y"], tree.Children.Keys);
        Assert.Equal(11f, tree.Children["X"].TotalStats.Gold);
        Assert.Equal(18f, tree.TotalStats.Gold);

        var clone = tree.Clone();
        clone.AddStats(new Stats(gold: 100f), "X");
        Assert.Equal(18f, tree.TotalStats.Gold);
        Assert.Equal(118f, clone.TotalStats.Gold);
    }

    [Fact]
    public void StatTreeNode_Add_SharesTheChildrenOfTheOtherTreeWhenAbsent()
    {
        // Kotlin quirk: children[key] = value (no copy) when the key is new
        var other = new StatTreeNode();
        other.AddStats(new Stats(gold: 1f), "X");
        var tree = new StatTreeNode();
        tree.Add(other);
        tree.AddStats(new Stats(gold: 1f), "X");
        Assert.Equal(2f, other.TotalStats.Gold);
    }

    // ---- pure helpers

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(0.99f, 0f)]
    [InlineData(1f, 1f)]
    [InlineData(1.9f, 1f)]
    [InlineData(2f, 2f)]
    [InlineData(3.9f, 2f)]
    [InlineData(4f, 3f)]
    [InlineData(7.99f, 3f)]
    [InlineData(8f, 4f)]
    [InlineData(100f, 27f)]
    public void GetProductionFromExcessiveFood(float food, float expected) =>
        Assert.Equal(expected, CityStats.GetProductionFromExcessiveFood(food));

    [Fact]
    public void GetStatConversionRate_IsAQuarter_ModifiedByTheMatchingUnique()
    {
        var plain = NewWorld();
        Assert.Equal(0.25f, plain.Stats.GetStatConversionRate(Stat.Gold));

        var bonus = NewWorld(nationUniques: ["Production to [Gold] conversion rate +[50]%", "[+50]% Production to [Science] conversion"]);
        // whichever unique texts exist, a rate for a stat without a unique is untouched
        Assert.Equal(0.25f, bonus.Stats.GetStatConversionRate(Stat.Science));
    }

    [Fact]
    public void GetStatsOfSpecialist_UnknownSpecialist_IsEmpty()
    {
        var w = NewWorld();
        Assert.True(w.Stats.GetStatsOfSpecialist("Nobody").IsEmpty());
    }

    [Fact]
    public void GetStatsOfSpecialist_AddsTheStatsUniquesMatchingTheSpecialist()
    {
        var w = NewWorld(r => r.Specialists.Put("Scientist", new Specialist { Name = "Scientist", Science = 3f }), ["[+1 Science] from every [Scientist]"]);
        var stats = w.Stats.GetStatsOfSpecialist("Scientist");
        Assert.Equal(4f, stats.Science);
        Assert.Equal(0f, stats.Culture);
    }

    // ---- hasExtraAnnexUnhappiness

    [Fact]
    public void HasExtraAnnexUnhappiness_FalseForTheFoundingCivAndPuppets_TrueForAnnexedCities()
    {
        var w = NewWorld();
        w.City.FoundingCivObject = w.Civ;
        Assert.False(w.Stats.HasExtraAnnexUnhappiness());
        w.City.FoundingCivObject = new Civilization(new NationObject { Name = "Greece" }) { CivId = "Greece" };
        Assert.True(w.Stats.HasExtraAnnexUnhappiness());
        w.City.IsPuppet = true;
        Assert.False(w.Stats.HasExtraAnnexUnhappiness());
    }

    // ---- update

    [Fact]
    public void Update_ProducesTheFinalStatList_InSourceOrder()
    {
        var w = NewWorld();
        w.Fake.PopulationData = 3;
        w.Fake.PopulationFake.FreePopulation = 3;
        w.Fake.ConstructionsFake.CurrentConstruction = new Building { Name = "Monument", Cost = 40 };
        w.Fake.ConstructionsFake.CurrentConstructionNameData = "Monument";
        w.Fake.ConstructionsFake.StatsData.AddStats(new Stats(gold: 2f, culture: 1f), "Monument");
        w.Stats.Update(updateCivStats: false);

        // "Buildings" is assigned before the stat map is added; empty stats (specialists, trade routes) add no node
        Assert.Equal(["Population", "Buildings", "Tile yields"], w.Stats.BaseStatTree.Children.Keys);
        Assert.Contains("Population", w.Stats.FinalStatList.Keys);
        Assert.Equal(3f, w.Stats.FinalStatList["Population"].Science);
        // food eaten: 2 per citizen
        Assert.Equal(-6f, w.Stats.FinalStatList["Population"].Food);
        Assert.Equal(2f, w.Stats.FinalStatList["Buildings"].Gold);
        Assert.Equal(1f, w.Stats.FinalStatList["Buildings"].Culture);
        Assert.Equal(w.Stats.FinalStatList.Values.Sum(it => it.Gold), w.Stats.CurrentCityStats.Gold);
    }

    [Fact]
    public void Update_ProductionCanConvertToAStat_WhenTheCurrentConstructionIsAStatName()
    {
        var w = NewWorld();
        w.Fake.PopulationData = 1;
        w.Fake.PopulationFake.FreePopulation = 8;
        w.Fake.ConstructionsFake.CurrentConstruction = new PerpetualStatConversion("Gold");
        w.Fake.ConstructionsFake.CurrentConstructionNameData = "Gold";
        w.Stats.Update(updateCivStats: false);
        var production = w.Stats.FinalStatList.Where(it => it.Key != "Construction").Sum(it => it.Value.Production);
        Assert.True(production >= 8f);
        Assert.Equal(production * 0.25f, w.Stats.FinalStatList["Construction"].Gold);
        Assert.Contains("Production", w.Stats.BaseStatTree.Children.Keys);
    }

    [Fact]
    public void Update_MinimumProductionIsOne()
    {
        var w = NewWorld();
        w.Fake.PopulationData = 1;
        w.Fake.ConstructionsFake.CurrentConstruction = new PerpetualStatConversion("Gold");
        w.Fake.ConstructionsFake.CurrentConstructionNameData = "";
        w.Stats.Update(updateCivStats: false);
        Assert.True(w.Stats.FinalStatList.Values.Sum(it => it.Production) >= 1f);
    }

    [Fact]
    public void Update_Resistance_ClearsEverythingButTheMinimumProduction()
    {
        var w = NewWorld();
        w.Fake.PopulationData = 2;
        w.Fake.ConstructionsFake.CurrentConstruction = new PerpetualStatConversion("Gold");
        w.Fake.ConstructionsFake.CurrentConstructionNameData = "";
        w.City.FlagsCountdown[nameof(CityFlags.Resistance)] = 3;
        w.Stats.Update(updateCivStats: false);
        Assert.Equal(["Production"], w.Stats.FinalStatList.Keys);
        Assert.Equal(1f, w.Stats.CurrentCityStats.Production);
        Assert.Equal(0f, w.Stats.CurrentCityStats.Food);
    }

    [Fact]
    public void Update_Maintenance_IsTruncatedToWholeGold()
    {
        var w = NewWorld();
        w.Fake.PopulationData = 1;
        w.Fake.ConstructionsFake.CurrentConstruction = new PerpetualStatConversion("Gold");
        w.Fake.ConstructionsFake.CurrentConstructionNameData = "";
        w.Fake.ConstructionsFake.MaintenanceCosts = 3.9f;
        w.Stats.Update(updateCivStats: false);
        Assert.Equal(-3f, w.Stats.FinalStatList["Maintenance"].Gold);
    }

    [Fact]
    public void UpdateCityHappiness_CitiesAndPopulationUnhappiness()
    {
        var w = NewWorld();
        w.Fake.PopulationData = 4;
        w.City.FoundingCivObject = w.Civ;
        w.Stats.UpdateCityHappiness(new StatTreeNode());
        Assert.Equal(["Cities", "Population", "Buildings", "Tile yields"], w.Stats.HappinessList.Keys);
        Assert.Equal(-3f * w.Civ.GetDifficulty().UnhappinessModifier, w.Stats.HappinessList["Cities"]);
        Assert.Equal(-4f * w.Civ.GetDifficulty().UnhappinessModifier, w.Stats.HappinessList["Population"]);
    }

    [Fact]
    public void UpdateCityHappiness_AnnexedCityDoublesPopulationUnhappiness()
    {
        var w = NewWorld();
        w.Fake.PopulationData = 4;
        w.City.FoundingCivObject = new Civilization(new NationObject { Name = "Greece" }) { CivId = "Greece" };
        w.Stats.UpdateCityHappiness(new StatTreeNode());
        var modifier = w.Civ.GetDifficulty().UnhappinessModifier;
        Assert.Equal(-5f * modifier, w.Stats.HappinessList["Cities"]);
        Assert.Equal(-8f * modifier, w.Stats.HappinessList["Population"]);
        Assert.Equal(-2f, w.Stats.HappinessList["Occupied City"]);
    }

    [Fact]
    public void UpdateCityHappiness_BuildingAndSpecialistHappiness_AreTruncated()
    {
        var w = NewWorld();
        var buildings = new StatTreeNode();
        buildings.AddStats(new Stats(happiness: 2.9f), "Temple");
        w.Stats.UpdateCityHappiness(buildings, new Stats(happiness: 1.9f));
        Assert.Equal(2f, w.Stats.HappinessList["Buildings"]);
        Assert.Equal(1f, w.Stats.HappinessList["Specialists"]);
    }

    [Fact]
    public void CanConvertFoodToProduction_NeedsFood_AndTheUnique()
    {
        var w = NewWorld();
        var settler = new BaseUnit { Name = "Settler", Uniques = ["Excess Food converted to Production when under construction"] };
        var warrior = new BaseUnit { Name = "Warrior" };
        Assert.True(w.Stats.CanConvertFoodToProduction(1f, settler));
        Assert.False(w.Stats.CanConvertFoodToProduction(0f, settler));
        Assert.False(w.Stats.CanConvertFoodToProduction(5f, warrior));
        Assert.False(w.Stats.CanConvertFoodToProduction(5f, new PerpetualStatConversion("Gold")));
    }

    [Fact]
    public void GetRoadTypeOfConnectionToCapital_IsNoneWhenNotConnected()
    {
        var w = NewWorld();
        Assert.Equal(RoadStatus.None, w.Stats.GetRoadTypeOfConnectionToCapital());
    }

    // ---- GreatPersonPointsBreakdown

    private static World GreatPersonWorld(params string[] unitNames)
    {
        var w = NewWorld(r =>
        {
            foreach (var name in unitNames) r.Units.Put(name, new BaseUnit { Name = name });
            r.Specialists.Put("Scientist", new Specialist { Name = "Scientist", GreatPersonPoints = new Counter<string> { ["Great Scientist"] = 3 } });
        });
        return w;
    }

    [Fact]
    public void GreatPersonPoints_SumsSpecialistsAndBuildings_FiltersNonUnits()
    {
        var w = GreatPersonWorld("Great Scientist", "Great Artist");
        w.Fake.PopulationFake.SpecialistAllocations["Scientist"] = 2;
        w.Fake.PopulationFake.SpecialistAllocations["Unknown"] = 5; // not a specialist of the ruleset
        var wonder = new Building { Name = "Louvre" };
        wonder.GreatPersonPoints["Great Artist"] = 2;
        wonder.GreatPersonPoints["Great Bard"] = 9; // not a unit
        w.Fake.BuiltBuildingObjects.Add(wonder);

        var breakdown = new GreatPersonPointsBreakdown(w.City);
        Assert.Equal(["Specialists", "Louvre"], breakdown.BasePoints.Select(it => it.Source));
        Assert.Equal("Building/Louvre", breakdown.BasePoints[1].PediaLink);
        Assert.Equal(new[] { "Great Scientist", "Great Artist", "Great Bard" }.Order(), breakdown.AllNames.Order());

        var sum = breakdown.Sum();
        Assert.Equal(6, sum["Great Scientist"]);
        Assert.Equal(2, sum["Great Artist"]);
        Assert.False(sum.ContainsKey("Great Bard"));
        Assert.Equal(sum, w.City.GetGreatPersonPoints());
    }

    [Fact]
    public void GreatPersonPoints_PercentageBonuses_AreAdditive_AndRoundHalfUp()
    {
        var w = GreatPersonWorld("Great Scientist");
        w.Civ.Nation.Uniques = ["[+25]% Great Person generation [in all cities]", "[+25]% Great Person generation [in all cities]", "[+50]% Great Scientist generation"];
        w.Fake.PopulationFake.SpecialistAllocations["Scientist"] = 1; // 3 points
        var breakdown = new GreatPersonPointsBreakdown(w.City);
        // Whatever the unique texts resolve to, the sum is 3 * (100 + bonus) / 100 rounded half up in 1/1000 fixed point
        var bonus = breakdown.PercentBonuses.Sum(it => it.Counter["Great Scientist"]);
        var expected = (3 * 1000 + 3 * 1000 * bonus / 100 + 500) / 1000;
        Assert.Equal(expected, breakdown.Sum()["Great Scientist"]);
    }

    [Fact]
    public void GreatPersonPoints_PercentageBonusBelowMinus100_RemovesTheEntry()
    {
        var w = GreatPersonWorld("Great Scientist");
        w.Fake.PopulationFake.SpecialistAllocations["Scientist"] = 1;
        var breakdown = new GreatPersonPointsBreakdown(w.City);
        var bonus = new GreatPersonPointsBreakdown.Entry("Mod", isAllGP: true);
        bonus.Counter.Add("Great Scientist", -100);
        breakdown.PercentBonuses.Add(bonus);
        Assert.False(breakdown.Sum().ContainsKey("Great Scientist"));
    }

    [Fact]
    public void GreatPersonPoints_FixedPointRounding_HalfUp()
    {
        var w = GreatPersonWorld("Great Scientist");
        w.Fake.PopulationFake.SpecialistAllocations["Scientist"] = 1; // 3 points = 3000
        var breakdown = new GreatPersonPointsBreakdown(w.City);
        var bonus = new GreatPersonPointsBreakdown.Entry("Mod", isAllGP: true);
        bonus.Counter.Add("Great Scientist", 17); // 3000 + 3000 * 17 / 100 = 3510 -> 4
        breakdown.PercentBonuses.Add(bonus);
        Assert.Equal(4, breakdown.Sum()["Great Scientist"]);
        bonus.Counter.Add("Great Scientist", -2); // 3000 + 3000 * 15 / 100 = 3450 -> 3
        Assert.Equal(3, breakdown.Sum()["Great Scientist"]);
    }

    [Fact]
    public void GreatPersonPercentageBonus_IsZeroWithoutUniques()
    {
        var w = GreatPersonWorld();
        Assert.Equal(0, w.City.GetGreatPersonPercentageBonus());
        Assert.Equal(0, GreatPersonPointsBreakdown.GetGreatPersonPercentageBonus(w.City));
    }

    /// <summary>A construction that is not a building or unit (a stat name such as "Gold", the perpetual constructions).</summary>
    private sealed class PerpetualStatConversion(string name) : IConstruction
    {
        public string Name { get; set; } = name;
        public Counter<string> GetResourceRequirementsPerTurn(GameContext? state = null) => new();
        public IReadOnlySet<string> RequiredResources(GameContext? state = null) => new HashSet<string>();
        public Counter<string> GetStockpiledResourceRequirements(GameContext state) => new();
        public bool IsBuildable(CityConstructions cityConstructions) => true;
        public bool ShouldBeDisplayed(CityConstructions cityConstructions) => true;
    }
}

// T124: the CityStats / GreatPersonPointsBreakdown branches that need a real map, cities and diplomacy (TestGame, as CityTest.kt does).
[Collection("RulesetCache")]
public class CityStatsGameTests
{
    private static (TestGame Game, Civilization Civ, CityObject City) Setup(string[]? nationUniques = null)
    {
        var game = new TestGame();
        game.MakeHexagonalMap(6);
        var civ = game.AddCiv(nationUniques, isPlayer: true);
        var city = game.AddCity(civ, game.GetTile(0, 0));
        return (game, civ, city);
    }

    private static void AddBuilding(TestGame game, CityObject city, Action<Building> configure, params string[] uniques)
    {
        var building = game.CreateBuilding(uniques);
        configure(building);
        city.CityConstructions.AddBuilding(building);
    }

    [Fact]
    public void NullifiesStat_RemovesTheWholeStatTotal_AndTwoUniquesDoNotDoubleIt()
    {
        var (game, _, city) = Setup(["Nullifies [Gold] [in all cities]", "Nullifies [Gold] [in all cities]"]);
        AddBuilding(game, city, it => it.Gold = 7f);
        city.CityStats.Update(updateCivStats: false);
        Assert.Equal(0f, city.CityStats.FinalStatList.Values.Sum(it => it.Gold));
    }

    [Fact]
    public void NullifiesStat_Positive_ShowsAsANegativeEntry()
    {
        var (game, _, city) = Setup(["Nullifies [Gold] [in all cities]"]);
        AddBuilding(game, city, it => it.Gold = 7f);
        city.CityStats.Update(updateCivStats: false);
        Assert.Contains(city.CityStats.FinalStatList.Values, it => it.Gold <= -7f);
        Assert.Equal(0f, city.CityStats.CurrentCityStats.Gold);
    }

    [Fact]
    public void TradeRoute_ConnectedNonCapitalCity_GetsGoldFromBothPopulations_WithFlatAndPercentUniques()
    {
        var (game, civ, capital) = Setup(["[+2 Gold] from each Trade Route", "[+50]% [Gold] from Trade Routes"]);
        var city = game.AddCity(civ, game.GetTile(3, 0), initialPopulation: 2);
        Assert.Same(capital, civ.GetCapital());

        city.CityStats.Update(updateCivStats: false);
        Assert.DoesNotContain("Trade routes", city.CityStats.BaseStatTree.Children.Keys); // not connected yet

        civ.Cache.CitiesConnectedToCapitalToMediums[city] = [];
        city.CityStats.Update(updateCivStats: false);
        var expected = (1 * 0.15f + 2 * 1.1f - 1 + 2f) * 1.5f;
        Assert.Equal(expected, city.CityStats.BaseStatTree.Children["Trade routes"].TotalStats.Gold, 0.0001f);

        capital.CityStats.Update(updateCivStats: false); // the capital itself never has a trade route
        Assert.DoesNotContain("Trade routes", capital.CityStats.BaseStatTree.Children.Keys);
    }

    [Fact]
    public void GrowthBonus_AppliesOnlyToPositiveFood_AndWeLoveTheKingDayAddsAQuarter()
    {
        var (game, _, city) = Setup(["[+50]% growth [in all cities]"]);
        AddBuilding(game, city, it => it.Food = 20f);
        city.CityStats.Update(updateCivStats: false);
        var plainFood = city.CityStats.FinalStatList.Where(it => !it.Key.Contains("Growth")).Sum(it => it.Value.Food);
        Assert.True(plainFood > 0f);
        var growth = Assert.Single(city.CityStats.FinalStatList, it => it.Key.EndsWith("([Growth])"));
        Assert.Equal(plainFood * 0.5f, growth.Value.Food, 0.001f);
        Assert.DoesNotContain("We Love The King Day", city.CityStats.FinalStatList.Keys);

        city.SetFlag(CityFlags.WeLoveTheKing, 10);
        city.CityStats.Update(updateCivStats: false);
        Assert.Equal(plainFood / 4, city.CityStats.FinalStatList["We Love The King Day"].Food, 0.001f);

        city.CityStats.Update(updateCivStats: false, calculateGrowthModifiers: false);
        Assert.DoesNotContain("We Love The King Day", city.CityStats.FinalStatList.Keys);
        Assert.DoesNotContain(city.CityStats.FinalStatList.Keys, it => it.EndsWith("([Growth])"));
    }

    [Fact]
    public void GrowthBonus_IsNotAppliedWhenFoodIsNotPositive()
    {
        var (_, _, city) = Setup(["[+50]% growth [in all cities]"]);
        city.SetFlag(CityFlags.WeLoveTheKing, 10);
        city.Population.AddPopulation(30); // eats more than the tiles make
        city.CityStats.Update(updateCivStats: false);
        Assert.True(city.CityStats.FinalStatList.Values.Sum(it => it.Food) <= 0f);
        Assert.DoesNotContain("We Love The King Day", city.CityStats.FinalStatList.Keys);
        Assert.DoesNotContain(city.CityStats.FinalStatList.Keys, it => it.EndsWith("([Growth])"));
    }

    [Fact]
    public void StatPercentBonus_DependsOnTheConstructionType()
    {
        var (game, _, city) = Setup([
            "[+10]% Production when constructing [Military] units [in all cities]",
            "[+20]% Production when constructing [All] buildings [in all cities]",
            "[+30]% Production when constructing [All] wonders [in all cities]",
            "[+5]% [Production] [in all cities]",
        ]);
        var warrior = game.Ruleset.Units["Warrior"];
        warrior.SetRuleset(game.Ruleset);
        var building = game.CreateBuilding();
        var wonder = game.CreateWonder();

        float Bonus(IConstruction construction)
        {
            city.CityStats.Update(construction, updateCivStats: false);
            return city.CityStats.StatPercentBonusTree.TotalStats.Production;
        }

        Assert.Equal(15f, Bonus(warrior));
        Assert.Equal(25f, Bonus(building));
        Assert.Equal(35f, Bonus(wonder));
        Assert.Equal(5f, Bonus(new PerpetualStatConversionForTest("Gold")));
    }

    [Fact]
    public void TileStatsCurrent_TracksHappinessTheStatsObjectAndStoppedWorking()
    {
        var (_, civ, city) = Setup();
        Assert.False(city.CityStats.TileStatsCurrent); // never computed
        city.CityStats.UpdateTileStats();
        Assert.True(city.CityStats.TileStatsCurrent);

        city.CityStats.CurrentCityStats = new Stats(); // a nested Update replaced the object that countables read
        Assert.False(city.CityStats.TileStatsCurrent);
        city.CityStats.UpdateTileStats();
        Assert.True(city.CityStats.TileStatsCurrent);

        civ.Stats.Happiness += 1; // the one civ stat unique conditionals read
        Assert.False(city.CityStats.TileStatsCurrent);
        civ.Stats.Happiness -= 1;
        Assert.True(city.CityStats.TileStatsCurrent); // back under the happiness it was computed with

        // the skipped computation equals a forced one
        city.CityStats.UpdateTileStats();
        var forced = city.CityStats.StatsFromTiles.Clone();
        city.CityStats.Update(null, updateTileStats: !city.CityStats.TileStatsCurrent, updateCivStats: false);
        Assert.Equal(forced, city.CityStats.StatsFromTiles);
    }

    [Fact]
    public void UpdateTileStats_SumsTheCentreAndWorkedTiles_AndStopsWorkingBlockadedOnes()
    {
        var (game, civ, city) = Setup();
        var worked = game.GetTile(1, 0);
        game.AddTileToCity(city, worked);
        game.SetTileTerrain(worked.Position, "Grassland");
        city.WorkedTiles.Add(worked.Position);
        city.CityStats.UpdateTileStats();
        var center = city.GetCenterTile().Stats.GetTileStats(city, civ);
        var withWorked = city.CityStats.StatsFromTiles.Food;
        Assert.Equal(center.Food + worked.Stats.GetTileStats(city, civ).Food, withWorked);

        city.WorkedTiles.Remove(worked.Position);
        city.CityStats.UpdateTileStats();
        Assert.Equal(center.Food, city.CityStats.StatsFromTiles.Food);
    }

    [Fact]
    public void GreatPersonBoostWithFriendship_AddsBothCivsUniques_OnlyWithADeclarationOfFriendship()
    {
        const string boost = "When declaring friendship, both parties gain a [25]% boost to great person generation";
        var game = new TestGame();
        game.MakeHexagonalMap(6);
        var a = game.AddCiv([boost], isPlayer: true);
        var b = game.AddCiv([boost], isPlayer: true);
        var city = game.AddCity(a, game.GetTile(-4, 0));
        game.AddCity(b, game.GetTile(4, 0));
        a.DiplomacyFunctions.MakeCivilizationsMeet(b);
        Assert.Equal(0, city.GetGreatPersonPercentageBonus());

        a.GetDiplomacyManager(b)!.SignDeclarationOfFriendship();
        Assert.Equal(50, city.GetGreatPersonPercentageBonus()); // our own unique plus the friend's
        var scientist = game.Ruleset.Specialists.Keys.First(it => game.Ruleset.Specialists[it].GreatPersonPoints.ContainsKey("Great Scientist"));
        city.Population.SpecialistAllocations[scientist] = 1;
        var breakdown = new GreatPersonPointsBreakdown(city);
        Assert.Equal(2, breakdown.PercentBonuses.Count(it => it.Source == "Declaration of Friendship"));
        Assert.All(breakdown.PercentBonuses, it => Assert.True(it.IsAllGP));
        Assert.Equal(25, breakdown.PercentBonuses[0].Counter["Great Scientist"]);
    }

    [Fact]
    public void GreatPersonEarnedFaster_OnlyAppliesToGreatPeopleWithBasePoints()
    {
        var (game, _, city) = Setup(["[Great Scientist] is earned [50]% faster", "[Great Artist] is earned [100]% faster"]);
        var scientist = game.Ruleset.Specialists.Keys.First(it => game.Ruleset.Specialists[it].GreatPersonPoints.ContainsKey("Great Scientist"));
        city.Population.SpecialistAllocations[scientist] = 1;
        var breakdown = new GreatPersonPointsBreakdown(city);
        var entry = Assert.Single(breakdown.PercentBonuses);
        Assert.Equal(50, entry.Counter["Great Scientist"]);
        Assert.False(entry.IsAllGP);
        var points = game.Ruleset.Specialists[scientist].GreatPersonPoints["Great Scientist"];
        Assert.Equal((points * 1000 + points * 1000 * 50 / 100 + 500) / 1000, breakdown.Sum()["Great Scientist"]);
    }

    private sealed class PerpetualStatConversionForTest(string name) : IConstruction
    {
        public string Name { get; set; } = name;
        public Counter<string> GetResourceRequirementsPerTurn(GameContext? state = null) => new();
        public IReadOnlySet<string> RequiredResources(GameContext? state = null) => new HashSet<string>();
        public Counter<string> GetStockpiledResourceRequirements(GameContext state) => new();
        public bool IsBuildable(CityConstructions cityConstructions) => true;
        public bool ShouldBeDisplayed(CityConstructions cityConstructions) => true;
    }
}
