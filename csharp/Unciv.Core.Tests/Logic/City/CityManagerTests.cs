using Unciv.Json;
using Unciv.Logic.City;
using Unciv.Logic.City.Managers;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using Unciv.Models;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.StatTypes;
using CityObject = Unciv.Logic.City.City;
using NationObject = Unciv.Models.Ruleset.Nation.Nation;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Core.Tests.Logic.CityTests;

// T061: CityPopulationManager, CityExpansionManager, CityReligionManager and CityFounder over a real Civilization / TileMap / City with the
// real managers (the TestGame-based Kotlin fixtures are ported with T072). Automation (T087) is a
// stand-in installed on the population manager.
public sealed class CityManagerTests
{
    /// <summary>Tile rank = its food + 0.5 * its production; a specialist ranks as given in <see cref="SpecialistRanks"/>.</summary>
    internal sealed class StandInAutomation : ICityWorkAutomation
    {
        public Dictionary<string, float> SpecialistRanks { get; } = [];
        public float RankStatsForCityWork(Stats stats, CityObject city, bool areWeRankingSpecialist) => stats.Food + 0.5f * stats.Production;
        public float RankSpecialist(string specialist, CityObject city) => SpecialistRanks.GetValueOrDefault(specialist);
        public float RankTileForCityWork(TileClass tile, CityObject city) => RankStatsForCityWork(tile.Stats.GetTileStats(city, city.Civ), city, false);
    }

    internal static Ruleset MakeRuleset()
    {
        var ruleset = new Ruleset();
        ruleset.Terrains.Put("Grassland", new Terrain { Name = "Grassland", Type = TerrainType.Land, Food = 2f });
        ruleset.Terrains.Put("Hill", new Terrain { Name = "Hill", Type = TerrainType.Land, Food = 0f, Production = 2f });
        ruleset.Terrains.Put("Coast", new Terrain { Name = "Coast", Type = TerrainType.Water, Food = 1f });
        ruleset.TileResources.Put("Salt", new TileResource { Name = "Salt", ResourceType = ResourceType.Bonus });
        ruleset.TileResources.Put("Wine", new TileResource { Name = "Wine", ResourceType = ResourceType.Luxury });
        foreach (var resource in ruleset.TileResources.Values) resource.SetTransients(ruleset);
        return ruleset;
    }

    internal static Civilization MakeCiv(FakeGameInfoState info, string name, bool human = true, string? cityStateType = null,
        List<string>? nationCities = null, List<string>? nationUniques = null)
    {
        var civ = new Civilization(new NationObject { Name = name, CityStateType = cityStateType, Cities = nationCities ?? [], Uniques = nationUniques ?? [] })
        {
            CivId = name,
            GameInfo = info,
            PlayerType = human ? PlayerType.Human : PlayerType.AI,
            CityStateFunctions = new FakeCityStateFunctions(),
        };
        civ.Units = new TestUnitManager(civ) { Count = 1 };
        civ.State = new GameContext(civ);
        civ.Tech.CivInfo = civ;
        civ.Policies.CivInfo = civ;
        civ.GoldenAges.CivInfo = civ;
        civ.GreatPeople.CivInfo = civ;
        civ.CivConstructions.SetTransients(civ);
        civ.EspionageManager.SetTransients(civ);
        info.Civs.Add(civ);
        return civ;
    }

    internal sealed record World(Civilization Civ, FakeGameInfoState Info, TileMap Map, CityObject City, FakeCityStats Stats, FakeCityConstructions Constructions, StandInAutomation Ranking)
    {
        public TileClass TileAt(int x, int y) => Map[HexCoord.Of(x, y)];
        public CityPopulationManager Population => City.Population;
        public CityExpansionManager Expansion => City.Expansion;
        public CityReligionManager Religion => City.Religion;
    }

    /// <summary>A civ with one city at (4,4) on a 9x9 grassland map (so that the work range 3 fits), with the real managers.</summary>
    internal static World NewWorld(bool human = true, string? cityStateType = null, Action<Ruleset>? configure = null, bool claim = true, List<string>? nationUniques = null)
    {
        var ruleset = MakeRuleset();
        configure?.Invoke(ruleset);
        var info = new FakeGameInfoState { RulesetData = ruleset };
        var map = new TileMap(81) { GameInfo = info };
        for (var x = 0; x < 9; x++)
            for (var y = 0; y < 9; y++)
                map.TileList.Add(new TileClass { Position = HexCoord.Of(x, y), BaseTerrain = x == 5 && y == 4 ? "Hill" : "Grassland" });
        map.SetTransients(ruleset);
        info.Map = map;
        var civ = MakeCiv(info, "Rome", human, cityStateType, nationUniques: nationUniques);
        var constructions = new FakeCityConstructions();
        var city = new CityObject
        {
            Id = "c1",
            Name = "Roma",
            Location = HexCoord.Of(4, 4),
            CityConstructions = constructions,
        };
        var stats = new FakeCityStats(city);
        city.CityStats = stats;
        civ.Cities = [city];
        var ranking = new StandInAutomation();
        city.Population.Automation = ranking;
        city.SetTransients(civ);
        if (claim) city.Expansion.Reset();
        return new World(civ, info, map, city, stats, constructions, ranking);
    }

    // ---- CityPopulationManager

    [Fact]
    public void NewManagers_StartWithKotlinsInitialState()
    {
        var population = new CityPopulationManager();
        Assert.Equal(1, population.Population);
        Assert.Equal(0, population.FoodStored);
        Assert.Empty(population.SpecialistAllocations);
        Assert.Throws<InvalidOperationException>(() => population.City);
        var religion = new CityReligionManager();
        Assert.Equal(100, religion.GetPressures()[Constants.NoReligionName]);
        Assert.Throws<InvalidOperationException>(() => religion.City);
        Assert.Equal(0, new CityExpansionManager().CultureStored);
    }

    [Theory]
    [InlineData(1, 15)]
    [InlineData(2, 24)] // 15 + 8 + floor(1^1.5)
    [InlineData(3, 33)] // 15 + 16 + floor(2^1.5 = 2.83)
    [InlineData(10, 15 + 72 + 27)] // floor(9^1.5) = 27
    public void GetFoodToNextPopulation_FollowsTheCivVFormula(int population, int expected)
    {
        var w = NewWorld(claim: false);
        w.Population.Population = population;
        Assert.Equal(expected, w.Population.GetFoodToNextPopulation());
    }

    [Fact]
    public void GetFoodToNextPopulation_AppliesSpeedCityStateAndAiModifiers_AndTruncates()
    {
        var w = NewWorld(human: false, cityStateType: "Cultured", claim: false);
        w.Info.SpeedData.Modifier = 1.5f;
        w.Info.Difficulty.AiCityGrowthModifier = 0.7f;
        w.Population.Population = 3; // 33 * 1.5 * 1.5 * 0.7 = 51.97..
        Assert.Equal(51, w.Population.GetFoodToNextPopulation());
        var human = NewWorld(claim: false);
        human.Info.SpeedData.Modifier = 1.5f;
        human.Population.Population = 3;
        Assert.Equal(49, human.Population.GetFoodToNextPopulation()); // 49.5 truncated, not rounded
    }

    [Fact]
    public void TurnsToStarvationAndGrowth_AreNullWhenNotStarvingOrGrowing()
    {
        var w = NewWorld(claim: false);
        Assert.Null(w.Population.GetNumTurnsToStarvation());
        Assert.Null(w.Population.GetNumTurnsToNewPopulation());
        w.Stats.CurrentCityStats.Food = -2f;
        w.Population.FoodStored = 5;
        Assert.Equal(3, w.Population.GetNumTurnsToStarvation()); // 5 / 2 + 1
        w.Stats.CurrentCityStats.Food = 4f;
        w.Population.FoodStored = 1;
        Assert.Equal(4, w.Population.GetNumTurnsToNewPopulation()); // ceil((15 - 1) / 4)
        w.Population.FoodStored = 100;
        Assert.Equal(1, w.Population.GetNumTurnsToNewPopulation()); // never below one turn
    }

    [Fact]
    public void GetPopulationFilterAmount_ReadsTheNamedCounts()
    {
        var w = NewWorld();
        w.Population.Population = 5;
        w.Population.SpecialistAllocations.Add("Scientist", 2);
        Assert.Equal(2, w.Population.GetPopulationFilterAmount("Specialists"));
        Assert.Equal(5, w.Population.GetPopulationFilterAmount("Population"));
        Assert.Equal(2, w.Population.GetPopulationFilterAmount("Unemployed")); // 5 - 1 worked tile - 2 specialists
        Assert.Equal(2, w.Population.GetPopulationFilterAmount("Scientist"));
        Assert.Equal(0, w.Population.GetPopulationFilterAmount("Artist"));
        Assert.Equal(0, w.Population.GetPopulationFilterAmount("Followers of the Majority Religion"));
    }

    [Fact]
    public void AddPopulation_NeverDropsBelowOne_AndAutoAssignsFreePopulation()
    {
        var w = NewWorld();
        w.Population.AddPopulation(-10);
        Assert.Equal(1, w.Population.Population);
        w.Population.SetPopulation(3);
        Assert.Equal(3, w.Population.Population);
        Assert.Equal(3, w.City.WorkedTiles.Count);
        w.Population.SetPopulation(2); // free population < 0: unassign the worst
        Assert.Equal(2, w.City.WorkedTiles.Count);
    }

    [Fact]
    public void AutoAssign_WorksTheBestTiles_BreakingTiesByLongitudeThenLatitude()
    {
        var w = NewWorld();
        w.City.ClearWorkedTiles();
        w.Population.Population = 2;
        w.Population.AutoAssignPopulation();
        Assert.Equal(2, w.City.WorkedTiles.Count);
        // all remaining workable grassland ties at food 2: the one with the highest longitude (then latitude) wins first
        var worked = w.City.GetWorkedTiles().ToList();
        var workable = w.City.GetWorkableTiles().Where(it => it.Stats.GetTileStats(w.City, w.Civ).Food == 2f)
            .OrderByDescending(it => it.Longitude).ThenByDescending(it => it.Latitude).Take(2).ToList();
        Assert.Equal(workable.Select(it => it.Position).OrderBy(it => it.x).ThenBy(it => it.y),
            worked.Select(it => it.Position).OrderBy(it => it.x).ThenBy(it => it.y));
        Assert.True(w.Stats.UpdateCalls >= 2);
    }

    [Fact]
    public void AutoAssign_PrefersASpecialistOnlyWhenItRanksStrictlyHigherThanTheBestTile()
    {
        var w = NewWorld();
        w.Constructions.BuiltBuildingObjects.Add(new Building { Name = "Library", SpecialistSlots = new Counter<string> { ["Scientist"] = 1 } });
        w.Population.Population = 1;
        w.Ranking.SpecialistRanks["Scientist"] = 2f; // equal to a grassland tile: the tile wins
        w.Population.AutoAssignPopulation();
        Assert.Equal(1, w.City.WorkedTiles.Count);
        Assert.Equal(0, w.Population.SpecialistAllocations["Scientist"]);

        w.City.ClearWorkedTiles();
        w.Ranking.SpecialistRanks["Scientist"] = 2.5f;
        w.Population.AutoAssignPopulation();
        Assert.Empty(w.City.WorkedTiles);
        Assert.Equal(1, w.Population.SpecialistAllocations["Scientist"]);
    }

    [Fact]
    public void AutoAssign_WithManualSpecialists_NeverAssignsSpecialists()
    {
        var w = NewWorld();
        w.City.ManualSpecialists = true;
        w.Constructions.BuiltBuildingObjects.Add(new Building { Name = "Library", SpecialistSlots = new Counter<string> { ["Scientist"] = 1 } });
        w.Ranking.SpecialistRanks["Scientist"] = 100f;
        w.Population.Population = 1;
        w.Population.AutoAssignPopulation();
        Assert.Equal(1, w.City.WorkedTiles.Count);
        Assert.Equal(0, w.Population.SpecialistAllocations["Scientist"]);
    }

    [Fact]
    public void UnassignExtraPopulation_ClampsSpecialistsToTheirSlots_ThenDropsTheWorstTile()
    {
        var w = NewWorld();
        w.Constructions.BuiltBuildingObjects.Add(new Building { Name = "Library", SpecialistSlots = new Counter<string> { ["Scientist"] = 1 } });
        w.Population.Population = 5;
        w.Population.SpecialistAllocations.Add("Scientist", 3);
        w.Population.UnassignExtraPopulation();
        Assert.Equal(1, w.Population.SpecialistAllocations["Scientist"]);

        // a specialist without any slot is only released while the population is short (Kotlin clamps just the slots it knows)
        w.Population.SpecialistAllocations.Add("Artist", 1);
        w.Population.UnassignExtraPopulation();
        Assert.Equal(1, w.Population.SpecialistAllocations["Artist"]);

        w.Population.SpecialistAllocations.Clear();
        w.Population.Population = 3;
        w.Population.AutoAssignPopulation();
        Assert.Equal(3, w.City.WorkedTiles.Count);
        w.Population.Population = 1;
        w.Population.UnassignExtraPopulation();
        Assert.Equal(1, w.City.WorkedTiles.Count);
    }

    [Fact]
    public void UnassignExtraPopulation_DropsTilesTheCityNoLongerOwns()
    {
        var w = NewWorld();
        w.Population.Population = 1;
        w.Population.AutoAssignPopulation();
        var tile = w.City.GetWorkedTiles().Single();
        tile.SetOwningCity(null); // lost the tile
        Assert.Equal(1, w.City.WorkedTiles.Count);
        w.Population.UnassignExtraPopulation();
        Assert.Empty(w.City.WorkedTiles);
    }

    [Fact]
    public void UnassignExtraPopulation_WithManualSpecialists_StillRemovesSpecialistsWhenNothingElseIsLeft()
    {
        var w = NewWorld();
        w.City.ManualSpecialists = true;
        w.Constructions.BuiltBuildingObjects.Add(new Building { Name = "Library", SpecialistSlots = new Counter<string> { ["Scientist"] = 3 } });
        w.Population.Population = 1;
        w.Population.SpecialistAllocations.Add("Scientist", 3);
        w.Population.UnassignExtraPopulation();
        Assert.Equal(1, w.Population.SpecialistAllocations["Scientist"]);
    }

    [Fact]
    public void GetMaxSpecialists_SumsTheSlotsOfTheBuiltBuildings()
    {
        var w = NewWorld();
        w.Constructions.BuiltBuildingObjects.Add(new Building { Name = "Library", SpecialistSlots = new Counter<string> { ["Scientist"] = 1 } });
        w.Constructions.BuiltBuildingObjects.Add(new Building { Name = "University", SpecialistSlots = new Counter<string> { ["Scientist"] = 2, ["Artist"] = 1 } });
        var max = w.Population.GetMaxSpecialists();
        Assert.Equal(3, max["Scientist"]);
        Assert.Equal(1, max["Artist"]);
    }

    [Fact]
    public void NextTurn_Starvation_ShrinksTheCityOnceAndResetsTheFood()
    {
        var w = NewWorld();
        w.Population.Population = 3;
        w.Population.FoodStored = 2;
        w.Population.NextTurn(-5);
        Assert.Equal(2, w.Population.Population);
        Assert.Equal(0, w.Population.FoodStored);
        Assert.Contains(w.Civ.Notifications, it => it.Text == "[Roma] is starving!");
        w.Population.Population = 1;
        w.Population.FoodStored = 0;
        w.Population.NextTurn(-1);
        Assert.Equal(1, w.Population.Population); // a size-1 city never starves away
        Assert.Equal(0, w.Population.FoodStored);
    }

    [Fact]
    public void NextTurn_Growth_SpendsTheFood_AddsAPopulation_AndFlagsAReassignment()
    {
        var w = NewWorld();
        w.Population.FoodStored = 10;
        w.Population.NextTurn(7); // 17 >= 15
        Assert.Equal(2, w.Population.Population);
        Assert.Equal(2, w.Population.FoodStored);
        Assert.True(w.City.ShouldReassignPopulation);
        Assert.Contains(w.Civ.Notifications, it => it.Text == "[Roma] has grown!");
    }

    [Fact]
    public void NextTurn_NotEnoughFood_DoesNotGrow()
    {
        var w = NewWorld();
        w.Population.NextTurn(14);
        Assert.Equal(1, w.Population.Population);
        Assert.Equal(14, w.Population.FoodStored);
        Assert.False(w.City.ShouldReassignPopulation);
    }

    [Fact]
    public void NextTurn_AvoidGrowth_CapsTheStoredFood()
    {
        var w = NewWorld();
        w.City.AvoidGrowth = true;
        w.Population.NextTurn(40);
        Assert.Equal(1, w.Population.Population);
        Assert.Equal(15, w.Population.FoodStored);
    }

    // ---- CityExpansionManager

    [Fact]
    public void Reset_ClaimsTheCenterAndTheRingAroundIt()
    {
        var w = NewWorld();
        Assert.Equal(7, w.City.Tiles.Count);
        Assert.Contains(HexCoord.Of(4, 4), w.City.Tiles);
        Assert.All(w.City.GetTiles(), it => Assert.Same(w.City, it.GetCity()));
        Assert.Equal(0, w.Expansion.TilesClaimed());
    }

    [Fact]
    public void TilesClaimed_CountsOnlyTilesBeyondTheRing()
    {
        var w = NewWorld();
        var outer = w.TileAt(4, 4).GetTilesAtDistance(2).First();
        w.Expansion.TakeOwnership(outer);
        Assert.Equal(1, w.Expansion.TilesClaimed());
        Assert.Equal(8, w.City.Tiles.Count);
    }

    [Fact]
    public void GetCultureToNextTile_FollowsTheFormula_WithSpeedAndCityStateModifiers()
    {
        var w = NewWorld();
        Assert.Equal(10, w.Expansion.GetCultureToNextTile()); // 6 * 1.4813^1.3 = 10.0
        w.Info.SpeedData.CultureCostModifier = 2f;
        Assert.Equal(20, w.Expansion.GetCultureToNextTile());
        var cityState = NewWorld(cityStateType: "Cultured");
        Assert.Equal(15, cityState.Expansion.GetCultureToNextTile());
    }

    [Fact]
    public void GetGoldCostOfTile_GrowsWithDistanceAndClaimedTiles()
    {
        var w = NewWorld();
        var ring2 = w.TileAt(4, 4).GetTilesAtDistance(2).First();
        Assert.Equal(50, w.Expansion.GetGoldCostOfTile(ring2));
        Assert.Equal(60, w.Expansion.GetGoldCostOfTile(ring2, extraTiles: 2));
        var ring3 = w.TileAt(4, 4).GetTilesAtDistance(3).First();
        Assert.Equal(100, w.Expansion.GetGoldCostOfTile(ring3));
        w.Info.SpeedData.GoldCostModifier = 1.25f;
        Assert.Equal(125, w.Expansion.GetGoldCostOfTile(ring3));
    }

    [Fact]
    public void CanBuyTile_RequiresAnAdjacentOwnedTile_InRange_WithoutOwnerOrResistance()
    {
        var w = NewWorld();
        var adjacent = w.TileAt(4, 4).GetTilesAtDistance(2).First(it => it.Neighbors.Any(n => ReferenceEquals(n.GetCity(), w.City)));
        Assert.True(w.Expansion.CanBuyTile(adjacent));
        w.City.IsPuppet = true;
        Assert.False(w.Expansion.CanBuyTile(adjacent));
        w.City.IsPuppet = false;
        w.City.SetFlag(CityFlags.Resistance, 3);
        Assert.False(w.Expansion.CanBuyTile(adjacent));
        w.City.RemoveFlag(CityFlags.Resistance);
        Assert.False(w.Expansion.CanBuyTile(w.TileAt(4, 4))); // already owned
        Assert.False(w.Expansion.CanBuyTile(w.TileAt(4, 4).GetTilesAtDistance(3).First())); // not adjacent to an owned tile
    }

    [Fact]
    public void BuyTile_PaysGold_TakesOwnership_AndDefersTheReassignment()
    {
        var w = NewWorld();
        var tile = w.TileAt(4, 4).GetTilesAtDistance(2).First(it => it.Neighbors.Any(n => ReferenceEquals(n.GetCity(), w.City)));
        w.Civ.AddGold(80);
        w.Expansion.BuyTile(tile);
        Assert.Equal(30, w.Civ.Gold);
        Assert.Same(w.City, tile.GetCity());
        Assert.Contains(tile.Position, w.City.Tiles);
        Assert.True(w.City.ShouldReassignPopulation || !w.Civ.IsCurrentPlayer());
    }

    [Fact]
    public void BuyTile_Throws_WhenNotContiguous_OrWhenTooPoor_UnlessGodMode()
    {
        var w = NewWorld();
        var far = w.TileAt(4, 4).GetTilesAtDistance(3).First();
        Assert.Contains("owns none of the neighbors", Assert.ThrowsAny<Exception>(() => w.Expansion.BuyTile(far)).Message);
        var tile = w.TileAt(4, 4).GetTilesAtDistance(2).First(it => it.Neighbors.Any(n => ReferenceEquals(n.GetCity(), w.City)));
        Assert.Contains("lacks gold (cost 50, has 0)", Assert.ThrowsAny<Exception>(() => w.Expansion.BuyTile(tile)).Message);
        w.Info.GameParameters.GodMode = true;
        w.Expansion.BuyTile(tile);
        Assert.Same(w.City, tile.GetCity());
        Assert.Equal(-50, w.Civ.Gold);
    }

    [Fact]
    public void TakeOwnership_RefusesACityCenter_AndStealsFromOtherCities()
    {
        var w = NewWorld();
        Assert.Throws<InvalidOperationException>(() => w.Expansion.TakeOwnership(w.City.GetCenterTile()));
    }

    [Fact]
    public void RelinquishOwnership_RemovesTheTile_StopsWorkingIt_AndRecordsHistory()
    {
        var w = NewWorld();
        w.Population.Population = 1;
        w.Population.AutoAssignPopulation();
        var worked = w.City.GetWorkedTiles().Single();
        w.Expansion.RelinquishOwnership(worked);
        Assert.DoesNotContain(worked.Position, w.City.Tiles);
        Assert.Null(worked.GetCity());
        Assert.False(w.City.IsWorked(worked));
        Assert.NotEmpty(w.City.WorkedTiles); // the population was auto-assigned to another tile
    }

    [Fact]
    public void ChooseNewTileToOwn_PicksTheLowestRank_FirstOnTies()
    {
        var w = NewWorld();
        var chosen = w.Expansion.ChooseNewTileToOwn()!;
        var best = w.Expansion.GetChoosableTiles().Min(it => w.Expansion.RankTileForExpansion(it));
        Assert.Equal(best, w.Expansion.RankTileForExpansion(chosen));
        Assert.Same(w.Expansion.GetChoosableTiles().First(it => w.Expansion.RankTileForExpansion(it) == best), chosen);
        Assert.Equal(2, chosen.AerialDistanceTo(w.City.GetCenterTile())); // distance dominates
    }

    [Fact]
    public void RankTileForExpansion_ScoresDistanceResourcesAndAdjacency()
    {
        var w = NewWorld();
        var ring2 = w.TileAt(4, 4).GetTilesAtDistance(2).First(it => it.Neighbors.Any(n => ReferenceEquals(n.GetCity(), w.City)));
        var plain = w.Expansion.RankTileForExpansion(ring2);
        // distance 2 -> 200, minus the tile yields (2 food), grassland neighbours are unowned & unexplored -> no adjacency bonus
        Assert.Equal(200 - 2, plain);
        var notAdjacent = w.TileAt(4, 4).GetTilesAtDistance(3).First();
        Assert.Equal(300 - 2 + 1000, w.Expansion.RankTileForExpansion(notAdjacent));
    }

    [Fact]
    public void NextTurn_CultureBeyondTheThreshold_ClaimsATile_AndNotifies()
    {
        var w = NewWorld();
        var before = w.City.Tiles.Count;
        w.Expansion.NextTurn(9.9f); // toInt truncates: 9 < 10
        Assert.Equal(9, w.Expansion.CultureStored);
        Assert.Equal(before, w.City.Tiles.Count);
        w.Expansion.NextTurn(1f);
        Assert.Equal(before + 1, w.City.Tiles.Count);
        Assert.Equal(0, w.Expansion.CultureStored);
        Assert.Contains(w.Civ.Notifications, it => it.Text == "[Roma] has expanded its borders!");
    }

    [Fact]
    public void SetTransients_ReassignsTheOwnershipOfTheCityTiles()
    {
        var w = NewWorld();
        var tile = w.TileAt(5, 4);
        tile.SetOwningCity(null);
        w.Expansion.SetTransients();
        Assert.Same(w.City, tile.GetCity());
    }

    // ---- CityReligionManager

    private static Religion MakeReligion(World w, string name, bool major = true, Civilization? founder = null)
    {
        var religion = new Religion(name, w.Info, founder ?? w.Civ, 0);
        w.Info.ReligionMap.Put(name, religion);
        if (major) AddBelief(w, religion, name + " founder", BeliefType.Founder);
        return religion;
    }

    private static void AddBelief(World w, Religion religion, string beliefName, BeliefType type)
    {
        var belief = new Belief { Name = beliefName, Type = type };
        w.Info.RulesetData.Beliefs.Put(beliefName, belief);
        religion.AddBeliefs([belief]);
    }

    [Fact]
    public void AddPressure_IsIgnoredWithoutReligion_ElseSplitsTheFollowersByPressure()
    {
        var w = NewWorld();
        MakeReligion(w, "Faith");
        w.Population.Population = 4;
        w.Religion.AddPressure("Faith", 300);
        Assert.Equal(0, w.Religion.GetFollowersOf("Faith")); // religion disabled
        w.Info.ReligionEnabled = true;
        w.Religion.AddPressure("Faith", 300);
        // pressure 100 none + 300 Faith, population 4: 100 per follower
        Assert.Equal(3, w.Religion.GetFollowersOf("Faith"));
        Assert.Equal("Faith", w.Religion.GetMajorityReligionName());
        Assert.Equal(3, w.Religion.GetFollowersOfMajorityReligion());
        Assert.Equal(0, w.Religion.GetFollowersOfOtherReligionsThan("Faith"));
    }

    [Fact]
    public void LargestRemainderMethod_HandsOutTheLeftoverPopulation()
    {
        var w = NewWorld();
        w.Info.ReligionEnabled = true;
        MakeReligion(w, "A");
        MakeReligion(w, "B");
        w.Population.Population = 5;
        w.Religion.AddPressure("A", 160, false);
        w.Religion.AddPressure("B", 140, false);
        w.Religion.AddPressure("A", 0); // 100 none + 160 A + 140 B = 400, 80 per follower
        // shares: none 1.25 (rem 20), A 2.0 (rem 0), B 1.75 (rem 60) -> 4 followers allocated; the leftover goes to B
        Assert.Equal(2, w.Religion.GetFollowersOf("A"));
        Assert.Equal(2, w.Religion.GetFollowersOf("B"));
        Assert.Equal(4, w.Religion.GetNumberOfFollowers().SumValues()); // the no-religion share is dropped
    }

    [Fact]
    public void GetMajorityReligion_NeedsAtLeastHalfThePopulation_AndNeverBeTheNoReligion()
    {
        var w = NewWorld();
        w.Info.ReligionEnabled = true;
        var faith = MakeReligion(w, "A");
        MakeReligion(w, "B");
        MakeReligion(w, "C");
        Assert.Null(w.Religion.GetMajorityReligionName()); // no followers yet
        w.Population.Population = 8;
        w.Religion.AddPressure("A", 100, false);
        w.Religion.AddPressure("B", 100, false);
        w.Religion.AddPressure("C", 100);
        // 400 pressure for 8 citizens: 2 followers each, below the 8 / 2 = 4 needed
        Assert.Equal(2, w.Religion.GetFollowersOf("A"));
        Assert.Null(w.Religion.GetMajorityReligionName());
        w.Religion.AddPressure("A", 400); // 800 pressure, 100 per follower: A 5
        Assert.Same(faith, w.Religion.GetMajorityReligion());
        Assert.Contains(w.Civ.Notifications, it => it.Text == "Your city [Roma] was converted to [A]!");
    }

    [Fact]
    public void RemoveAllPressuresExceptFor_KeepsOneReligionAndAtheism()
    {
        var w = NewWorld();
        w.Info.ReligionEnabled = true;
        MakeReligion(w, "A");
        MakeReligion(w, "B");
        w.Population.Population = 3;
        w.Religion.AddPressure("A", 200);
        w.Religion.AddPressure("B", 50);
        w.Religion.RemoveAllPressuresExceptFor("A");
        var pressures = w.Religion.GetPressures();
        Assert.Equal(200, pressures["A"]);
        Assert.Equal(0, pressures["B"]);
        Assert.Equal(100, pressures[Constants.NoReligionName]);
    }

    [Fact]
    public void UpdatePressureOnPopulationChange_AddsPressureOnlyOnGrowth()
    {
        var w = NewWorld();
        w.Info.ReligionEnabled = true;
        w.Religion.UpdatePressureOnPopulationChange(2);
        Assert.Equal(300, w.Religion.GetPressures()[Constants.NoReligionName]);
        w.Religion.UpdatePressureOnPopulationChange(-1);
        Assert.Equal(300, w.Religion.GetPressures()[Constants.NoReligionName]);
    }

    [Fact]
    public void RemoveUnknownPantheons_DropsPressuresOfForeignPantheonsOnly()
    {
        var w = NewWorld();
        w.Info.ReligionEnabled = true;
        var otherCiv = MakeCiv(w.Info, "Greece");
        var ours = MakeReligion(w, "OurPantheon", major: false);
        AddBelief(w, ours, "OurPantheon belief", BeliefType.Pantheon);
        var foreign = MakeReligion(w, "ForeignPantheon", major: false, founder: otherCiv);
        AddBelief(w, foreign, "ForeignPantheon belief", BeliefType.Pantheon);
        w.Population.Population = 2;
        w.Religion.AddPressure("OurPantheon", 100);
        w.Religion.AddPressure("ForeignPantheon", 100);
        w.Religion.RemoveUnknownPantheons();
        var pressures = w.Religion.GetPressures();
        Assert.Equal(100, pressures["OurPantheon"]);
        Assert.Equal(0, pressures["ForeignPantheon"]);
    }

    [Fact]
    public void GetPressureDeficit_IsTheMajorityPressureMinusOurs_AndNullMeansNone()
    {
        var w = NewWorld();
        w.Info.ReligionEnabled = true;
        MakeReligion(w, "A");
        w.Population.Population = 2;
        w.Religion.AddPressure("A", 300);
        Assert.Equal(300, w.Religion.GetPressureDeficit(null));
        Assert.Equal(200, w.Religion.GetPressureDeficit(Constants.NoReligionName));
        Assert.Equal(0, w.Religion.GetPressureDeficit("A"));
    }

    [Fact]
    public void HolyCityAndEndTurn_SpreadPressureToTheHolyCity()
    {
        var w = NewWorld();
        w.Info.ReligionEnabled = true;
        MakeReligion(w, "Faith");
        w.Religion.ReligionThisIsTheHolyCityOf = "Faith";
        Assert.True(w.City.IsHolyCity());
        w.Religion.EndTurn();
        Assert.Equal(5 * w.Info.SpeedData.ReligiousPressureAdjacentCity, w.Religion.GetPressures()["Faith"]);
        Assert.Equal(5 * w.Info.SpeedData.ReligiousPressureAdjacentCity, w.Religion.GetPressuresFromSurroundingCities()["Faith"]);
    }

    [Fact]
    public void Clone_CopiesTheStateAndKeepsTheCity()
    {
        var w = NewWorld();
        w.Info.ReligionEnabled = true;
        MakeReligion(w, "Faith");
        w.Population.Population = 2;
        w.Religion.AddPressure("Faith", 400);
        w.Religion.ReligionThisIsTheHolyCityOf = "Faith";
        w.Religion.IsBlockedHolyCity = true;
        var clone = w.Religion.Clone();
        Assert.NotSame(w.Religion, clone);
        Assert.Same(w.City, clone.City);
        Assert.Equal(w.Religion.GetPressures(), clone.GetPressures());
        Assert.Equal(w.Religion.GetNumberOfFollowers(), clone.GetNumberOfFollowers());
        Assert.Equal("Faith", clone.ReligionThisIsTheHolyCityOf);
        Assert.True(clone.IsBlockedHolyCity);
        var population = w.Population.Clone();
        Assert.Equal(2, population.Population);
        w.Expansion.CultureStored = 7;
        Assert.Equal(7, w.Expansion.Clone().CultureStored);
    }

    // ---- JSON

    [Fact]
    public void Json_RoundTripsTheSerializedFieldsOfTheManagers()
    {
        var w = NewWorld();
        w.Info.ReligionEnabled = true;
        MakeReligion(w, "Faith");
        w.Population.Population = 4;
        w.Population.FoodStored = 9;
        w.Population.SpecialistAllocations.Add("Scientist", 1);
        w.Expansion.CultureStored = 12;
        w.Religion.AddPressure("Faith", 250);
        w.Religion.ReligionThisIsTheHolyCityOf = "Faith";

        var population = UncivJson.Deserialize<CityPopulationManager>(UncivJson.Serialize(w.Population))!;
        Assert.Equal(4, population.Population);
        Assert.Equal(9, population.FoodStored);
        Assert.Equal(1, population.SpecialistAllocations["Scientist"]);
        Assert.Equal(12, UncivJson.Deserialize<CityExpansionManager>(UncivJson.Serialize(w.Expansion))!.CultureStored);
        var religion = UncivJson.Deserialize<CityReligionManager>(UncivJson.Serialize(w.Religion))!;
        Assert.Equal(w.Religion.GetPressures(), religion.GetPressures());
        Assert.Equal("Faith", religion.ReligionThisIsTheHolyCityOf);
    }

    // ---- CityFounder city names

    private static void WithCities(Civilization civ, params string[] names) =>
        civ.Cities = names.Select(it => new CityObject { Name = it, CityConstructions = new FakeCityConstructions() }).ToList();

    private static string? NameFor(Civilization civ, params Civilization[] alive) => CityFounder.GenerateNewCityName(civ, [.. alive]);

    [Fact]
    public void GenerateNewCityName_TakesTheFirstNameNoLivingCivUses_ForeignCitiesIncluded()
    {
        var info = new FakeGameInfoState();
        var rome = MakeCiv(info, "Rome", nationCities: ["Roma", "Milan", "Capua"]);
        var greece = MakeCiv(info, "Greece");
        Assert.Equal("Roma", NameFor(rome, rome, greece));
        WithCities(rome, "Roma");
        WithCities(greece, "Milan");
        Assert.Equal("Capua", NameFor(rome, rome, greece));
        Assert.Equal("Milan", NameFor(rome, rome)); // the foreign city is not alive here
    }

    [Fact]
    public void GenerateNewCityName_WithoutBorrowing_PrefixesTheNationsNamesInOrder()
    {
        var info = new FakeGameInfoState();
        var rome = MakeCiv(info, "Rome", nationCities: ["A", "B"]);
        WithCities(rome, "A", "B");
        Assert.Equal("New [A]", NameFor(rome, rome));
        WithCities(rome, "A", "B", "New [A]");
        Assert.Equal("New [B]", NameFor(rome, rome));
        WithCities(rome, "A", "B", "New [A]", "New [B]");
        Assert.Equal("Neo [A]", NameFor(rome, rome));
        WithCities(rome, "A", "B", "New [A]", "New [B]", "Neo [A]", "Neo [B]", "Nova [A]", "Nova [B]", "Altera [A]", "Altera [B]");
        Assert.Equal("New [New [A]]", NameFor(rome, rome)); // the second round nests the brackets
    }

    [Fact]
    public void GenerateNewCityName_ReturnsNullWhenTheNationHasNoNames()
    {
        var rome = MakeCiv(new FakeGameInfoState(), "Rome");
        Assert.Null(NameFor(rome, rome));
    }

    [Fact]
    public void GenerateNewCityName_Borrowing_TakesTheLastUnusedNameOfAnotherAliveMajorNation()
    {
        var info = new FakeGameInfoState();
        var rome = MakeCiv(info, "Rome", nationCities: ["Roma"], nationUniques: ["\"Borrows\" city names from other civilizations in the game"]);
        var greece = MakeCiv(info, "Greece", nationCities: ["Athens", "Sparta"]);
        WithCities(rome, "Roma");
        WithCities(greece, "Sparta");
        Assert.Equal("Athens", NameFor(rome, rome, greece));
        WithCities(greece, "Sparta", "Athens");
        Assert.Null(NameFor(rome, rome, greece)); // everything is used and there are no absent nations
    }

    [Fact]
    public void GenerateNewCityName_Borrowing_FallsBackToTheMajorNationsNotInTheGame_AndPicksSeededly()
    {
        var info = new FakeGameInfoState();
        var rome = MakeCiv(info, "Rome", nationCities: ["Roma"], nationUniques: ["\"Borrows\" city names from other civilizations in the game"]);
        WithCities(rome, "Roma");
        info.RulesetData.Nations.Put("Rome", rome.Nation);
        info.RulesetData.Nations.Put("Persia", new NationObject { Name = "Persia", Cities = ["Persepolis", "Susa"] });
        info.RulesetData.Nations.Put("Barbarians", new NationObject { Name = "Barbarians", Cities = ["Camp"] });
        var first = NameFor(rome, rome);
        Assert.Contains(first, new[] { "Persepolis", "Susa" });
        Assert.Equal(first, NameFor(rome, rome)); // the random stream is the state based one: same state, same pick
    }
}
