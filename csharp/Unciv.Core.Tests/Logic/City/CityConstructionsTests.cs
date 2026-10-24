using Unciv.Core.Tests.Logic.Map;
using Unciv.Core.Tests.Models.Ruleset.Unique;
using Unciv.Json;
using Unciv.Logic.City;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using Unciv.Logic.Map.MapUnits;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Ruleset.Unit;
using Unciv.Models.StatTypes;
using Unciv.Utils;
using CityObject = Unciv.Logic.City.City;
using NationObject = Unciv.Models.Ruleset.Nation.Nation;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Core.Tests.Logic.CityTests;

// T063: CityConstructions over a real Civilization / TileMap / City (fake population, stats, expansion and religion). The TestGame-based
// FreeBuildingTests and the production fixtures are ported with T072.
public class CityConstructionsTests
{
    private sealed record World(Civilization Civ, FakeGameInfoState Info, CityObject City, Ruleset Ruleset)
    {
        public CityConstructions Constructions => City.CityConstructions;
    }

    private static World NewWorld(bool human = true)
    {
        var ruleset = new Ruleset();
        ruleset.Terrains.Put("Grassland", new Terrain { Name = "Grassland", Type = TerrainType.Land, Food = 2f });
        ruleset.UnitTypes.Put("Melee", new UnitType { Name = "Melee" });
        ruleset.Eras.Put("Ancient", new Unciv.Models.Ruleset.Tech.Era { Name = "Ancient" });
        var info = new FakeGameInfoState { RulesetData = ruleset };
        var map = new TileMap(25) { GameInfo = info };
        for (var x = 0; x < 5; x++)
            for (var y = 0; y < 5; y++)
                map.TileList.Add(new TileClass { Position = HexCoord.Of(x, y), BaseTerrain = "Grassland" });
        map.SetTransients(ruleset);
        info.Map = map;
        var civ = new Civilization(new NationObject { Name = "Rome" })
        {
            CivId = "Rome",
            GameInfo = info,
            PlayerType = human ? PlayerType.Human : PlayerType.AI,
            CityStateFunctions = new FakeCityStateFunctions(),
        };
        civ.Units = new TestUnitManager(civ);
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
        fake.City.CityConstructions = new CityConstructions();
        fake.City.SetTransients(civ);
        return new World(civ, info, fake.City, ruleset);
    }

    private static Building AddBuilding(World w, string name, Action<Building>? init = null)
    {
        var building = new Building { Name = name, Cost = 100 };
        init?.Invoke(building);
        building.Ruleset = w.Ruleset;
        w.Ruleset.Buildings.Put(name, building);
        return building;
    }

    private static void Queue(World w, params string[] names) => w.Constructions.EditQueue(it => { it.Clear(); it.AddRange(names); });

    // ---- queue

    [Fact]
    public void EmptyQueue_ReadsAsIdle()
    {
        var w = NewWorld();
        Assert.Equal("", w.Constructions.CurrentConstructionName());
        Assert.Same(PerpetualConstruction.Idle, w.Constructions.GetCurrentConstruction());
        Assert.True(w.Constructions.IsQueueEmptyOrIdle());
        Assert.False(w.Constructions.IsQueueFull());
    }

    [Fact]
    public void GetConstruction_ThrowsForUnknownNames()
    {
        var w = NewWorld();
        AddBuilding(w, "Monument");
        Assert.Equal("Monument", w.Constructions.GetConstruction("Monument").Name);
        Assert.Same(PerpetualConstruction.Gold, w.Constructions.GetConstruction("Gold"));
        var e = Assert.ThrowsAny<Exception>(() => w.Constructions.GetConstruction("Nope"));
        Assert.Equal("Nope is not a building or a unit!", e.Message);
    }

    [Fact]
    public void EditQueue_ReplacesTheQueueInsteadOfMutatingIt()
    {
        var w = NewWorld();
        var before = w.Constructions.ConstructionQueue;
        w.Constructions.EditQueue(it => it.Add("Gold"));
        Assert.NotSame(before, w.Constructions.ConstructionQueue);
        Assert.Empty(before);
        Assert.Equal(["Gold"], w.Constructions.ConstructionQueue);
    }

    [Fact]
    public void AddToQueue_BuildingsGoBeforeATrailingPerpetualConstruction()
    {
        var w = NewWorld();
        AddBuilding(w, "Monument");
        AddBuilding(w, "Granary");
        w.Civ.Tech.TechsResearched.Add("none");
        w.Constructions.AddToQueue("Monument");
        w.Constructions.AddToQueue("Nothing");
        w.Constructions.AddToQueue("Granary");
        Assert.Equal(["Monument", "Granary", "Nothing"], w.Constructions.ConstructionQueue);
        Assert.True(w.Constructions.CurrentConstructionIsUserSet);
        w.Constructions.AddToQueue("Nothing"); // perpetual constructions replace each other
        Assert.Equal(["Monument", "Granary", "Nothing"], w.Constructions.ConstructionQueue);
    }

    [Fact]
    public void AddToQueue_RejectsDuplicateBuildingsAndAFullQueue()
    {
        var w = NewWorld();
        for (var i = 0; i < 10; i++) AddBuilding(w, $"B{i}");
        AddBuilding(w, "Extra");
        w.Constructions.AddToQueue("B0");
        w.Constructions.AddToQueue("B0");
        Assert.Equal(["B0"], w.Constructions.ConstructionQueue);
        for (var i = 1; i < 10; i++) w.Constructions.AddToQueue($"B{i}");
        Assert.True(w.Constructions.IsQueueFull());
        w.Constructions.AddToQueue("Extra");
        Assert.Equal(10, w.Constructions.ConstructionQueue.Count);
    }

    [Fact]
    public void AddToQueue_AddToTopAndPerpetualAtTop()
    {
        var w = NewWorld();
        AddBuilding(w, "Monument");
        AddBuilding(w, "Granary");
        w.Constructions.AddToQueue("Monument");
        w.Constructions.AddToQueue(w.Constructions.GetConstruction("Granary"), addToTop: true);
        Assert.Equal(["Granary", "Monument"], w.Constructions.ConstructionQueue);
        Queue(w, "Gold");
        w.Constructions.AddToQueue(PerpetualConstruction.Idle, addToTop: true);
        Assert.Equal(["Nothing"], w.Constructions.ConstructionQueue);
    }

    [Fact]
    public void RemoveFromQueue_RefillsWithIdleUnlessAutomatic()
    {
        var w = NewWorld();
        Queue(w, "Gold");
        w.Constructions.RemoveFromQueue(0, automatic: false);
        Assert.Equal(["Nothing"], w.Constructions.ConstructionQueue);
        Assert.False(w.Constructions.CurrentConstructionIsUserSet);
        Queue(w, "Gold");
        w.Constructions.RemoveFromQueue(0, automatic: true);
        Assert.Empty(w.Constructions.ConstructionQueue);
    }

    [Fact]
    public void RemoveFromQueue_KeepsTheRestAndMarksTheQueueUserSet()
    {
        var w = NewWorld();
        AddBuilding(w, "Monument");
        AddBuilding(w, "Granary");
        Queue(w, "Monument", "Granary");
        w.Constructions.RemoveFromQueue(0, automatic: true);
        Assert.Equal(["Granary"], w.Constructions.ConstructionQueue);
        Assert.True(w.Constructions.CurrentConstructionIsUserSet);
    }

    [Fact]
    public void RemoveAllByNameAndRemoveAll()
    {
        var w = NewWorld();
        AddBuilding(w, "Monument");
        AddBuilding(w, "Granary");
        Queue(w, "Monument", "Granary", "Monument");
        w.Constructions.RemoveAllByName("Monument");
        Assert.Equal(["Granary"], w.Constructions.ConstructionQueue);
        w.Constructions.RemoveAll();
        Assert.Equal(["Nothing"], w.Constructions.ConstructionQueue);
        Assert.False(w.Constructions.CurrentConstructionIsUserSet);
    }

    [Fact]
    public void MoveAndPriorityOperations()
    {
        var w = NewWorld();
        Queue(w, "a", "b", "c", "Gold");
        w.Constructions.MoveEntryToTop(2);
        Assert.Equal(["c", "a", "b", "Gold"], w.Constructions.ConstructionQueue);
        w.Constructions.MoveEntryToTop(0); // no-op
        w.Constructions.MoveEntryToTop(9); // no-op
        Assert.Equal(["c", "a", "b", "Gold"], w.Constructions.ConstructionQueue);
        Assert.Equal(0, w.Constructions.RaisePriority(1));
        Assert.Equal(["a", "c", "b", "Gold"], w.Constructions.ConstructionQueue);
        Assert.Equal(0, w.Constructions.RaisePriority(0));
        Assert.Equal(2, w.Constructions.LowerPriority(1));
        Assert.Equal(["a", "b", "c", "Gold"], w.Constructions.ConstructionQueue);
        Assert.Equal(3, w.Constructions.LowerPriority(3));
        Assert.Equal(0, w.Constructions.ConstructionQueue.ToList().IndexOf("a"));
    }

    [Fact]
    public void IsFirstConstructionOfItsKind_AndEnqueuedQueries()
    {
        var w = NewWorld();
        Queue(w, "a", "b", "a");
        Assert.True(w.Constructions.IsFirstConstructionOfItsKind(0, "a"));
        Assert.False(w.Constructions.IsFirstConstructionOfItsKind(2, "a"));
        Assert.True(w.Constructions.IsBeingConstructed("a"));
        Assert.True(w.Constructions.IsEnqueuedForLater("b"));
        Assert.False(w.Constructions.IsEnqueuedForLater("zzz"));
        Assert.True(w.Constructions.IsBeingConstructedOrEnqueued("b"));
        Assert.False(w.Constructions.IsBeingConstructedOrEnqueued("zzz"));
    }

    // ---- production

    [Fact]
    public void AddProductionPoints_PerpetualConstructionsFeedTheOverflow()
    {
        var w = NewWorld();
        AddBuilding(w, "Monument");
        Queue(w, "Gold");
        w.Constructions.AddProductionPoints(7);
        Assert.Equal(7, w.Constructions.ProductionOverflow);
        Assert.Empty(w.Constructions.InProgressConstructions);
        Queue(w, "Monument");
        w.Constructions.AddProductionPoints(5);
        w.Constructions.AddProductionPoints(6);
        Assert.Equal(11, w.Constructions.GetWorkDone("Monument"));
        Assert.Equal(0, w.Constructions.GetWorkDone("Granary"));
    }

    [Fact]
    public void TurnsToConstruction_UsesTheRemainingWorkAndTheOverflow()
    {
        var w = NewWorld();
        AddBuilding(w, "Monument", it => it.Cost = 100);
        Queue(w, "Monument");
        w.City.CityStats.CurrentCityStats = new Stats(production: 7f);
        Assert.Equal(15, w.Constructions.TurnsToConstruction("Monument")); // ceil(100 / 7)
        w.Constructions.InProgressConstructions["Monument"] = 40;
        Assert.Equal(9, w.Constructions.TurnsToConstruction("Monument")); // ceil(60 / 7)
        Assert.Equal(15, w.Constructions.TurnsToConstruction("Monument", useStoredProduction: false));
        w.Constructions.ProductionOverflow = 60;
        Assert.Equal(1, w.Constructions.TurnsToConstruction("Monument"));
        w.Constructions.InProgressConstructions["Monument"] = 140;
        Assert.Equal(0, w.Constructions.TurnsToConstruction("Monument"));
        Assert.Equal(0, w.Constructions.GetRemainingWork("Gold"));
    }

    [Fact]
    public void ProductionForConstruction_OfTheCurrentConstructionRoundsHalfUp()
    {
        var w = NewWorld();
        AddBuilding(w, "Monument");
        Queue(w, "Monument");
        w.City.CityStats.CurrentCityStats = new Stats(production: 2.5f);
        Assert.Equal(3, w.Constructions.ProductionForConstruction("Monument"));
    }

    [Fact]
    public void GetProductionMarkup_NamesTheCategoryAndTurns()
    {
        var w = NewWorld();
        AddBuilding(w, "Monument");
        AddBuilding(w, "Pyramids", it => it.IsWonder = true);
        w.Ruleset.Units.Put("Warrior", new BaseUnit { Name = "Warrior", Cost = 20 });
        Assert.Equal("", w.Constructions.GetProductionMarkup(w.Ruleset).Text);
        Queue(w, "Gold");
        var gold = w.Constructions.GetProductionMarkup(w.Ruleset);
        Assert.Equal("{Gold}", gold.Text);
        Assert.Equal("", gold.Link);
        w.City.CityStats.CurrentCityStats = new Stats(production: 10f);
        Queue(w, "Pyramids");
        Assert.Equal("Wonder/Pyramids", w.Constructions.GetProductionMarkup(w.Ruleset).Link);
        Assert.Equal("{Pyramids} - 10" + Unciv.UI.Components.Fonts.Turn, w.Constructions.GetProductionMarkup(w.Ruleset).Text);
        Queue(w, "Monument");
        Assert.Equal("Building/Monument", w.Constructions.GetProductionMarkup(w.Ruleset).Link);
        Queue(w, "Warrior");
        Assert.Equal("Unit/Warrior", w.Constructions.GetProductionMarkup(w.Ruleset).Link);
    }

    [Fact]
    public void EndTurn_AddsTheProductionAndTheOverflowToANonPerpetualConstruction()
    {
        var w = NewWorld();
        AddBuilding(w, "Monument");
        Queue(w, "Monument");
        w.Constructions.ProductionOverflow = 3;
        w.Constructions.EndTurn(new Stats(production: 4.5f));
        Assert.Equal(8, w.Constructions.GetWorkDone("Monument")); // round(4.5) + 3
        Assert.Equal(0, w.Constructions.ProductionOverflow);
    }

    [Fact]
    public void ConstructIfEnough_BuildsTheBuildingAndCapsTheOverflow()
    {
        var w = NewWorld();
        var monument = AddBuilding(w, "Monument", it => it.Cost = 30);
        Queue(w, "Monument");
        w.Constructions.InProgressConstructions["Monument"] = 45;
        w.City.CityStats.CurrentCityStats = new Stats(production: 5f);
        w.Constructions.ConstructIfEnough();
        Assert.True(w.Constructions.IsBuilt("Monument"));
        Assert.Equal([monument], w.Constructions.GetBuiltBuildings());
        Assert.Equal(15, w.Constructions.ProductionOverflow); // min(max(30, 5), 45 - 30)
        Assert.DoesNotContain("Monument", w.Constructions.InProgressConstructions.Keys);
        Assert.Equal(1, w.Civ.CivConstructions.BuiltItemsWithIncreasingCost["Monument"]);
        Assert.Contains(w.Civ.Notifications, it => it.Text == "[Monument] has been built in [c1]");
        Assert.DoesNotContain("Monument", w.Constructions.ConstructionQueue);
    }

    // ---- buildings

    [Fact]
    public void AddAndRemoveBuilding_MaintainTheBuiltSetTheUniqueMapAndTheEquivalentCache()
    {
        var w = NewWorld();
        var palace = AddBuilding(w, "Palace", it => it.Uniques = ["Indicates the capital city"]);
        var upgraded = AddBuilding(w, "Big Palace", it => it.Replaces = "Palace");
        w.Constructions.AddBuilding(palace, tryAddFreeBuildings: false);
        Assert.True(w.Constructions.IsBuilt("Palace"));
        Assert.True(w.Constructions.BuiltBuildingUniqueMap.HasUnique(UniqueType.IndicatesCapital));
        Assert.True(w.City.IsCapital());
        Assert.False(w.Constructions.ContainsBuildingOrEquivalent("Big Palace"));
        w.Constructions.AddBuilding(upgraded, tryAddFreeBuildings: false);
        Assert.True(w.Constructions.ContainsBuildingOrEquivalent("Big Palace"));
        w.Constructions.RemoveBuilding("Palace");
        Assert.False(w.Constructions.IsBuilt("Palace"));
        Assert.False(w.Constructions.BuiltBuildingUniqueMap.HasUnique(UniqueType.IndicatesCapital));
        w.Constructions.RemoveBuilding("Unknown building"); // no-op
        Assert.Equal([upgraded], w.Constructions.GetBuiltBuildings());
    }

    [Fact]
    public void AddBuilding_RaisesTheCityHealthProportionally()
    {
        var w = NewWorld();
        w.City.Health = 100;
        var walls = AddBuilding(w, "Walls", it => it.CityHealth = 50);
        w.Constructions.AddBuilding(walls, tryAddFreeBuildings: false);
        Assert.Equal(100 + (int)(50f * 100f / 200f), w.City.Health); // GetMaxHealth() was 200 before the building counted
    }

    [Fact]
    public void SetTransients_BuildsTheBuildingListInTheOrderOfTheBuiltSetAndFailsForUnknownBuildings()
    {
        var w = NewWorld();
        AddBuilding(w, "Monument");
        AddBuilding(w, "Granary");
        w.Constructions.BuiltBuildings.Add("Granary");
        w.Constructions.BuiltBuildings.Add("Monument");
        w.Constructions.SetTransients();
        Assert.Equal(w.Constructions.BuiltBuildings.ToList(), w.Constructions.GetBuiltBuildings().Select(it => it.Name).ToList());
        w.Constructions.BuiltBuildings.Add("Ghost");
        var e = Assert.Throws<InvalidOperationException>(() => w.Constructions.SetTransients());
        Assert.Equal("Building Ghost is not found!", e.Message);
    }

    [Fact]
    public void GetMaintenanceCosts_AppliesTheBuildingMaintenanceUniques()
    {
        var w = NewWorld();
        var temple = AddBuilding(w, "Temple", it => { it.Maintenance = 2; it.Gold = 1f; });
        var library = AddBuilding(w, "Library", it => { it.Maintenance = 1; it.Science = 1f; });
        w.Constructions.AddBuilding(temple, tryAddFreeBuildings: false);
        w.Constructions.AddBuilding(library, tryAddFreeBuildings: false);
        Assert.Equal(3f, w.Constructions.GetMaintenanceCosts());
        var stats = w.Constructions.GetStats();
        Assert.Equal(["Temple", "Library"], stats.Children.Keys.ToList());
    }

    [Fact]
    public void CheapestStatBuilding_PicksTheFirstOfTheCheapestBuildableOnes()
    {
        var w = NewWorld();
        AddBuilding(w, "Library", it => { it.Cost = 80; it.Science = 2f; });
        AddBuilding(w, "Observatory", it => { it.Cost = 60; it.Science = 3f; });
        AddBuilding(w, "Great Library", it => { it.Cost = 10; it.Science = 5f; it.IsWonder = true; });
        AddBuilding(w, "Granary", it => { it.Cost = 5; it.Food = 1f; });
        Assert.Equal("Observatory", w.Constructions.CheapestStatBuilding(Stat.Science)?.Name);
        Assert.Equal("Granary", w.Constructions.CheapestStatBuilding(Stat.Food)?.Name);
        Assert.Null(w.Constructions.CheapestStatBuilding(Stat.Culture));
    }

    // ---- rejection reasons

    [Fact]
    public void Building_RejectionReasons()
    {
        var w = NewWorld();
        var monument = AddBuilding(w, "Monument", it => it.RequiredTech = "Masonry");
        var reasons = monument.GetRejectionReasons(w.Constructions).ToList();
        Assert.Equal([RejectionReasonType.RequiresTech], reasons.Select(it => it.Type).ToList());
        Assert.Equal("Masonry not researched!", reasons[0].ErrorMessage);
        Assert.False(w.Constructions.CanAddToQueue(monument));
        w.Civ.Tech.TechsResearched.Add("Masonry");
        Assert.True(monument.IsBuildable(w.Constructions));
        w.Constructions.AddBuilding(monument, tryAddFreeBuildings: false);
        Assert.Equal([RejectionReasonType.AlreadyBuilt], monument.GetRejectionReasons(w.Constructions).Select(it => it.Type).ToList());
    }

    [Fact]
    public void Building_RequiredBuildingAndUnbuildableAndUniqueTo()
    {
        var w = NewWorld();
        AddBuilding(w, "Granary");
        var well = AddBuilding(w, "Well", it => it.RequiredBuilding = "Granary");
        var types = well.GetRejectionReasons(w.Constructions).ToList();
        Assert.Equal([RejectionReasonType.RequiresBuildingInThisCity], types.Select(it => it.Type).ToList());
        Assert.Equal("Requires a [Granary] in this city", types[0].ErrorMessage);
        var hidden = AddBuilding(w, "Hidden", it => it.Uniques = ["Unbuildable"]);
        Assert.Equal([RejectionReasonType.Unbuildable], hidden.GetRejectionReasons(w.Constructions).Select(it => it.Type).ToList());
        Assert.True(((INonPerpetualConstruction)hidden).IsPurchasable(w.Constructions));
        var special = AddBuilding(w, "Special", it => it.UniqueTo = "Greece");
        Assert.Equal("Unique to Greece", special.GetRejectionReasons(w.Constructions).Single().ErrorMessage);
        Assert.False(((INonPerpetualConstruction)special).IsPurchasable(w.Constructions));
    }

    [Fact]
    public void Wonder_RejectionReasonsWhenBuiltElsewhereOrAlreadyBuilt()
    {
        var w = NewWorld();
        var pyramids = AddBuilding(w, "Pyramids", it => it.IsWonder = true);
        Assert.Empty(pyramids.GetRejectionReasons(w.Constructions));
        w.Constructions.AddBuilding(pyramids, tryAddFreeBuildings: false);
        var types = pyramids.GetRejectionReasons(w.Constructions).Select(it => it.Type).ToList();
        Assert.Contains(RejectionReasonType.AlreadyBuilt, types);
        Assert.Contains(RejectionReasonType.WonderAlreadyBuilt, types);
        w.Constructions.RemoveBuilding(pyramids);
        w.City.IsPuppet = true;
        Assert.Equal([RejectionReasonType.PuppetWonder], pyramids.GetRejectionReasons(w.Constructions).Select(it => it.Type).ToList());
    }

    [Fact]
    public void Building_ConsumesResourcesRejection()
    {
        var w = NewWorld();
        w.Ruleset.TileResources.Put("Iron", new TileResource { Name = "Iron", ResourceType = ResourceType.Strategic });
        var forge = AddBuilding(w, "Forge", it => it.Uniques = ["Consumes [2] [Iron]"]);
        var reason = forge.GetRejectionReasons(w.Constructions).Single();
        Assert.Equal(RejectionReasonType.ConsumesResources, reason.Type);
        Assert.Equal("Need [2] more [Iron]", reason.ErrorMessage);
    }

    [Fact]
    public void ValidateInProgressConstructions_RefundsObsoleteWondersAsGold()
    {
        var w = NewWorld();
        var pyramids = AddBuilding(w, "Pyramids", it => it.IsWonder = true);
        AddBuilding(w, "Monument");
        Queue(w, "Monument");
        w.Constructions.InProgressConstructions["Pyramids"] = 50;
        w.Constructions.InProgressConstructions["Monument"] = 9;
        var gold = w.Civ.Gold;
        // another city of ours built the wonder: the in-progress points are refunded
        var other = new FakeCityState("c2", new FakeCiv("Rome")) { CivData = w.Civ, LocationData = HexCoord.Of(0, 0) };
        other.ConstructionsFake.BuiltNames.Add("Pyramids");
        w.Civ.Cities = [w.City, other];
        w.Constructions.ValidateInProgressConstructions();
        Assert.Equal(gold + 50, w.Civ.Gold);
        Assert.DoesNotContain("Pyramids", w.Constructions.InProgressConstructions.Keys);
        Assert.Equal(9, w.Constructions.GetWorkDone("Monument"));
        Assert.Contains(w.Civ.Notifications, it => it.Text == "Excess production for [Pyramids] converted to [50] gold");
        Assert.Same(pyramids, w.Ruleset.Buildings["Pyramids"]);
    }

    // ---- units

    [Fact]
    public void BaseUnit_RejectionReasonsAndPurchasing()
    {
        var w = NewWorld();
        var warrior = new BaseUnit { Name = "Warrior", UnitType = "Melee", Cost = 20, RequiredTech = "Bronze Working", UniqueTo = "Greece" };
        warrior.SetRuleset(w.Ruleset);
        w.Ruleset.Units.Put("Warrior", warrior);
        var types = warrior.GetRejectionReasons(w.Constructions).Select(it => it.Type).ToList();
        Assert.Contains(RejectionReasonType.RequiresTech, types);
        Assert.Contains(RejectionReasonType.UniqueToOtherNation, types);
        Assert.False(warrior.IsBuildable(w.Constructions));
        Assert.False(warrior.CanBePurchasedWithStat(w.City, Stat.Gold));

        var archer = new BaseUnit { Name = "Archer", UnitType = "Melee", Cost = 30 };
        archer.SetRuleset(w.Ruleset);
        w.Ruleset.Units.Put("Archer", archer);
        Assert.True(archer.IsBuildable(w.Constructions));
        Assert.True(archer.IsBuildable(w.Civ));
        Assert.True(archer.CanBePurchasedWithStat(w.City, Stat.Gold));
        Assert.False(archer.CanBePurchasedWithStat(w.City, Stat.Faith));
        Assert.Equal([archer], w.Constructions.GetConstructableUnits().ToList());
    }

    [Fact]
    public void BaseUnit_CannotBePurchasedUnique()
    {
        var w = NewWorld();
        var archer = new BaseUnit { Name = "Archer", UnitType = "Melee", Cost = 30, Uniques = ["Cannot be purchased"] };
        archer.SetRuleset(w.Ruleset);
        w.Ruleset.Units.Put("Archer", archer);
        Assert.False(archer.CanBePurchasedWithStat(w.City, Stat.Gold));
    }

    [Fact]
    public void CompleteConstruction_OfAUnitPlacesItAppliesTheStartingBonusesAndNotifies()
    {
        var w = NewWorld();
        MapUnitFactory.AddUnitTypes(w.Ruleset);
        var warriorBase = MapUnitFactory.NewBaseUnit(w.Ruleset, "Warrior");
        warriorBase.Cost = 20;
        warriorBase.SetRuleset(w.Ruleset);
        var unit = MapUnitFactory.NewUnit(w.Civ, warriorBase, w.City.GetCenterTile());
        var units = (TestUnitManager)w.Civ.Units;
        units.AddedUnitResult = unit;
        var barracks = AddBuilding(w, "Barracks", it => it.Uniques =
            ["New [Land] units start with [15] XP [in all cities]", "All newly-trained [Land] units [in all cities] receive the [Drill I] promotion"]);
        w.Constructions.AddBuilding(barracks, tryAddFreeBuildings: false);
        // the city saved the promotions of a former Warrior: only the ones in the promotion tree are applied, in tree order, while XP lasts
        foreach (var name in new[] { "Shock I", "Drill I" })
            w.Ruleset.UnitPromotions.Put(name, new Promotion { Name = name });
        var saved = new UnitPromotions { XP = 5 };
        saved.Promotions.Add("Shock I");
        saved.Promotions.Add("Drill I");
        saved.Promotions.Add("Unknown");
        w.City.UnitToPromotions.Put("Warrior", saved);
        w.City.UnitShouldUseSavedPromotion.Put("Warrior", true);
        var tree = unit.Promo().Tree;
        tree.Possible.AddRange(["Drill I", "Shock I"]);
        tree.Nodes.AddRange(["Shock I", "Drill I", "Other"]);
        Queue(w, "Warrior");
        w.Constructions.InProgressConstructions["Warrior"] = 20;

        Assert.True(w.Constructions.CompleteConstruction(warriorBase));

        Assert.Equal([(warriorBase, w.City)], units.AddedUnits);
        // Drill I was granted free; of the saved ones Shock I (first in tree order) is bought for 10 of the 15 XP, then 5 < 20 XP stops the loop
        Assert.Equal(5, unit.Promotions.XP);
        Assert.Equal(["Drill I", "Shock I"], unit.Promotions.Promotions.OrderBy(it => it, StringComparer.Ordinal));
        Assert.Equal(1, unit.Promotions.NumberOfPromotions);
        Assert.DoesNotContain("Warrior", w.Constructions.InProgressConstructions.Keys);
        Assert.Contains(w.Civ.Notifications, it => it.Text == "[Warrior] has been built in [c1]");
    }

    [Fact]
    public void CompleteConstruction_OfAUnitThatCannotBePlacedChangesNothing()
    {
        var w = NewWorld();
        MapUnitFactory.AddUnitTypes(w.Ruleset);
        var warriorBase = MapUnitFactory.NewBaseUnit(w.Ruleset, "Warrior");
        warriorBase.SetRuleset(w.Ruleset);
        Queue(w, "Warrior");
        Assert.False(w.Constructions.CompleteConstruction(warriorBase)); // TestUnitManager.AddUnit returns null
        Assert.Equal(["Warrior"], w.Constructions.ConstructionQueue);
        Assert.Empty(w.Civ.Notifications);
    }

    // ---- purchase

    [Fact]
    public void IsConstructionPurchaseAllowed_ChecksTheReserveAndGodMode()
    {
        var w = NewWorld();
        var monument = AddBuilding(w, "Monument");
        w.Civ.AddGold(50 - w.Civ.Gold);
        Assert.False(w.Constructions.IsConstructionPurchaseAllowed(monument, Stat.Gold, 60));
        Assert.True(w.Constructions.IsConstructionPurchaseAllowed(monument, Stat.Gold, 40));
        Assert.True(w.Constructions.IsConstructionPurchaseAllowed(monument, Stat.Gold, 0));
        Assert.False(w.Constructions.IsConstructionPurchaseAllowed(monument, Stat.Production, 0));
        w.Info.GameParameters.GodMode = true;
        Assert.True(w.Constructions.IsConstructionPurchaseAllowed(monument, Stat.Gold, 600));
    }

    [Fact]
    public void PurchaseConstruction_BuildsPaysAndLeavesTheQueueValid()
    {
        var w = NewWorld();
        var monument = AddBuilding(w, "Monument");
        AddBuilding(w, "Granary");
        Queue(w, "Monument", "Granary");
        w.Civ.AddGold(500 - w.Civ.Gold);
        var cost = monument.GetStatBuyCost(w.City, Stat.Gold).NotNull();
        Assert.True(w.Constructions.PurchaseConstruction("Monument", 0, automatic: false));
        Assert.True(w.Constructions.IsBuilt("Monument"));
        Assert.Equal(500 - cost, w.Civ.Gold);
        Assert.Equal(["Granary"], w.Constructions.ConstructionQueue);
        Assert.False(w.Constructions.PurchaseConstruction("Gold", -1, automatic: false)); // perpetual: not a purchase
    }

    // ---- misc

    [Fact]
    public void Clone_CopiesTheSerializedFieldsAndNotTheTransients()
    {
        var w = NewWorld();
        AddBuilding(w, "Monument");
        w.Constructions.BuiltBuildings.Add("Monument");
        w.Constructions.InProgressConstructions["Granary"] = 4;
        w.Constructions.EditQueue(it => it.Add("Gold"));
        w.Constructions.ProductionOverflow = 9;
        w.Constructions.CurrentConstructionIsUserSet = true;
        w.Constructions.FreeBuildingsProvidedFromThisCity["c9"] = ["Monument"];
        var clone = w.Constructions.Clone();
        Assert.Equal(["Monument"], clone.BuiltBuildings.ToList());
        Assert.Equal(4, clone.InProgressConstructions["Granary"]);
        Assert.Equal(["Gold"], clone.ConstructionQueue);
        Assert.Equal(9, clone.ProductionOverflow);
        Assert.True(clone.CurrentConstructionIsUserSet);
        Assert.Equal(["Monument"], clone.FreeBuildingsProvidedFromThisCity["c9"]);
        clone.InProgressConstructions["Granary"] = 5;
        clone.FreeBuildingsProvidedFromThisCity["c9"].Add("Other");
        Assert.Equal(4, w.Constructions.InProgressConstructions["Granary"]);
        Assert.Single(w.Constructions.FreeBuildingsProvidedFromThisCity["c9"]);
        Assert.Throws<InvalidOperationException>(() => clone.City);
    }

    [Fact]
    public void Json_RoundTripKeepsTheQueueOrderAndTheJavaHashOrder()
    {
        var c = new CityConstructions();
        foreach (var name in new[] { "Walls", "Monument", "Granary", "Library" }) c.BuiltBuildings.Add(name);
        c.InProgressConstructions["Pyramids"] = 12;
        c.EditQueue(it => it.AddRange(["Pyramids", "Gold"]));
        c.CurrentConstructionIsUserSet = true;
        c.ProductionOverflow = 3;
        var json = UncivJson.Serialize(c);
        Assert.Contains("\"constructionQueue\":[\"Pyramids\",\"Gold\"]", json, StringComparison.OrdinalIgnoreCase);
        var back = UncivJson.Deserialize<CityConstructions>(json).NotNull();
        Assert.Equal(c.BuiltBuildings.ToList(), back.BuiltBuildings.ToList());
        Assert.Equal(["Pyramids", "Gold"], back.ConstructionQueue);
        Assert.Equal(12, back.InProgressConstructions["Pyramids"]);
        Assert.True(back.CurrentConstructionIsUserSet);
        Assert.Equal(3, back.ProductionOverflow);
    }

    [Fact]
    public void PerpetualConstruction_IsBuildableNeedsTheEnablingUnique()
    {
        var w = NewWorld();
        Assert.True(PerpetualConstruction.Idle.IsBuildable(w.Constructions));
        Assert.False(PerpetualConstruction.Gold.IsBuildable(w.Constructions));
        Assert.False(w.Constructions.CanAddToQueue(PerpetualConstruction.Gold));
        Assert.True(PerpetualConstruction.Idle.ShouldBeDisplayed(w.Constructions));
        Assert.Equal("", PerpetualConstruction.Idle.GetProductionTooltip(w.City));
    }
}
