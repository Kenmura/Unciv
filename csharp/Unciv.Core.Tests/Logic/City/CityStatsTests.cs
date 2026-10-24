using Unciv.Core.Tests.Models.Ruleset.Unique;
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
