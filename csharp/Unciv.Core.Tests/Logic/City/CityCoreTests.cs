using Unciv.Core.Tests.Models.Ruleset.Unique;
using Unciv.Logic.City;
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
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.Core.Tests.Logic.CityTests;

// T060: City, CityFocus, CityResources and CityEspionageManager over a real Civilization / TileMap with fake sub-objects
// (CityTest.kt is TestGame-based and is ported with T072).
public class CityCoreTests
{
    private static Ruleset MakeRuleset()
    {
        var ruleset = new Ruleset();
        ruleset.Terrains.Put("Grassland", new Terrain { Name = "Grassland", Type = TerrainType.Land, Food = 2f });
        ruleset.Terrains.Put("Coast", new Terrain { Name = "Coast", Type = TerrainType.Water, Food = 1f });
        ruleset.TileImprovements.Put("Mine", new TileImprovement { Name = "Mine" });
        ruleset.TileResources.Put("Iron", new TileResource { Name = "Iron", ResourceType = ResourceType.Strategic, ImprovedBy = ["Mine"] });
        ruleset.TileResources.Put("Wine", new TileResource { Name = "Wine", ResourceType = ResourceType.Luxury, ImprovedBy = ["Mine"] });
        ruleset.TileResources.Put("Salt", new TileResource { Name = "Salt", ResourceType = ResourceType.Bonus, ImprovedBy = ["Mine"] });
        return ruleset;
    }

    private sealed record World(Civilization Civ, FakeGameInfoState Info, TileMap Map, FakeCityState Fake)
    {
        public CityObject City => Fake.City;
        public TileClass TileAt(int x, int y) => Map[HexCoord.Of(x, y)];
    }

    /// <summary>A civ with one city at (2,2) on a 5x5 grassland map, with SetTransients run.</summary>
    private static World NewWorld(bool human = true, string[]? nationUniques = null, Action<Ruleset>? configure = null,
        string? cityStateType = null, bool coastWestOfTheCity = false)
    {
        var ruleset = MakeRuleset();
        configure?.Invoke(ruleset);
        foreach (var resource in ruleset.TileResources.Values) resource.SetTransients(ruleset);
        var info = new FakeGameInfoState { RulesetData = ruleset };
        var map = new TileMap(25) { GameInfo = info };
        for (var x = 0; x < 5; x++)
            for (var y = 0; y < 5; y++)
                map.TileList.Add(new TileClass { Position = HexCoord.Of(x, y), BaseTerrain = coastWestOfTheCity && x == 1 && y == 2 ? "Coast" : "Grassland" });
        map.SetTransients(ruleset);
        info.Map = map;
        var civ = new Civilization(new NationObject { Name = "Rome", Uniques = nationUniques?.ToList() ?? [], CityStateType = cityStateType })
        {
            CivId = "Rome",
            GameInfo = info,
            PlayerType = human ? PlayerType.Human : PlayerType.AI,
            Units = new FakeUnitManager { Count = 1 },
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

    private static Building Make(string name, Action<Building>? init = null)
    {
        var building = new Building { Name = name, Cost = 100 };
        init?.Invoke(building);
        return building;
    }

    // ---- CityFocus

    [Fact]
    public void CityFocus_LabelsAndTableFlags()
    {
        Assert.Equal("Default", CityFocus.NoFocus.Label());
        Assert.Equal("Manual", CityFocus.Manual.Label());
        Assert.Equal(Stat.Food.Character().ToString(), CityFocus.FoodFocus.Label());
        Assert.Equal($"{Stat.Gold.Character()} {Stat.Food.Character()}", CityFocus.GoldGrowthFocus.Label());
        Assert.Equal($"{Stat.Production.Character()} {Stat.Food.Character()}", CityFocus.ProductionGrowthFocus.Label());
        Assert.False(CityFocus.HappinessFocus.TableEnabled());
        Assert.All(CityFocusExtensions.Entries.Where(it => it != CityFocus.HappinessFocus), it => Assert.True(it.TableEnabled()));
        Assert.Equal(11, CityFocusExtensions.Entries.Count);
        Assert.Equal(CityFocus.NoFocus, CityFocusExtensions.Entries[0]); // order matters for the CitizenManagementTable
    }

    [Fact]
    public void CityFocus_StatMultipliers()
    {
        Assert.Equal(1f, CityFocus.NoFocus.GetStatMultiplier(Stat.Food));
        Assert.Equal(1f, CityFocus.Manual.GetStatMultiplier(Stat.Gold));
        Assert.Equal(3.05f, CityFocus.FoodFocus.GetStatMultiplier(Stat.Food));
        Assert.Equal(1f, CityFocus.FoodFocus.GetStatMultiplier(Stat.Gold));
        Assert.Equal(3.05f, CityFocus.HappinessFocus.GetStatMultiplier(Stat.Happiness));
        Assert.Equal(2f, CityFocus.GoldGrowthFocus.GetStatMultiplier(Stat.Gold));
        Assert.Equal(1.5f, CityFocus.GoldGrowthFocus.GetStatMultiplier(Stat.Food));
        Assert.Equal(1f, CityFocus.GoldGrowthFocus.GetStatMultiplier(Stat.Science));
        Assert.Equal(2f, CityFocus.ProductionGrowthFocus.GetStatMultiplier(Stat.Production));
        Assert.Equal(1.5f, CityFocus.ProductionGrowthFocus.GetStatMultiplier(Stat.Food));
    }

    [Fact]
    public void CityFocus_StatValuesForFocus_ListsTheStatsWithAMultiplier_InStatOrder()
    {
        Assert.Empty(CityFocus.NoFocus.StatValuesForFocus());
        Assert.Equal([Stat.Culture], CityFocus.CultureFocus.StatValuesForFocus());
        Assert.Equal([Stat.Food, Stat.Gold], CityFocus.GoldGrowthFocus.StatValuesForFocus());
        Assert.Equal([Stat.Production, Stat.Food], CityFocus.ProductionGrowthFocus.StatValuesForFocus());
    }

    [Fact]
    public void CityFocus_SafeValueOf_FindsTheFocusOfAStat_ElseNoFocus()
    {
        foreach (var stat in StatExtensions.Entries)
            Assert.Equal(stat, CityFocusExtensions.SafeValueOf(stat) is var focus && focus.FocusStat() is { } s ? s : stat);
        Assert.Equal(CityFocus.FoodFocus, CityFocusExtensions.SafeValueOf(Stat.Food));
        Assert.Equal(CityFocus.FaithFocus, CityFocusExtensions.SafeValueOf(Stat.Faith));
    }

    [Fact]
    public void CityFocus_ZeroFoodFocuses_AreTheNonFoodSingleStatFocuses()
    {
        Assert.Equal(
            [CityFocus.CultureFocus, CityFocus.FaithFocus, CityFocus.GoldFocus, CityFocus.HappinessFocus, CityFocus.ProductionFocus, CityFocus.ScienceFocus],
            CityFocusExtensions.ZeroFoodFocuses);
        Assert.DoesNotContain(CityFocus.FoodFocus, CityFocusExtensions.ZeroFoodFocuses);
    }

    [Fact]
    public void City_Focus_IsStoredByName_AndUnknownNamesFallBackToNoFocus()
    {
        var city = new CityObject();
        Assert.Equal(CityFocus.NoFocus, city.GetCityFocus());
        city.SetCityFocus(CityFocus.GoldGrowthFocus);
        Assert.Equal(CityFocus.GoldGrowthFocus, city.GetCityFocus());
    }

    // ---- Flags

    [Fact]
    public void Flags_SetHasGetRemove()
    {
        var w = NewWorld();
        Assert.False(w.City.HasFlag(CityFlags.Resistance));
        w.City.SetFlag(CityFlags.Resistance, 5);
        Assert.True(w.City.HasFlag(CityFlags.Resistance));
        Assert.True(w.City.IsInResistance());
        Assert.Equal(5, w.City.GetFlag(CityFlags.Resistance));
        w.City.RemoveFlag(CityFlags.Resistance);
        Assert.False(w.City.IsInResistance());
        Assert.Throws<KeyNotFoundException>(() => w.City.GetFlag(CityFlags.Resistance));
    }

    [Fact]
    public void SetFlag_AdjustWithGameSpeed_RoundsHalfUp()
    {
        var w = NewWorld();
        w.Info.SpeedData.Modifier = 1.5f;
        w.City.SetFlag(CityFlags.WeLoveTheKing, 15, adjustWithGameSpeed: true);
        Assert.Equal(23, w.City.GetFlag(CityFlags.WeLoveTheKing)); // 22.5 -> 23, not half-even 22
        Assert.True(w.City.IsWeLoveTheKingDayActive());
        w.City.SetFlag(CityFlags.ResourceDemand, 15);
        Assert.Equal(15, w.City.GetFlag(CityFlags.ResourceDemand)); // not adjusted without the flag
    }

    [Fact]
    public void ResetWLTKD_RemovesBothFlagsAndTheDemandedResource()
    {
        var w = NewWorld();
        w.City.SetFlag(CityFlags.WeLoveTheKing, 4);
        w.City.SetFlag(CityFlags.ResourceDemand, 9);
        w.City.SetFlag(CityFlags.Resistance, 2);
        w.City.DemandedResource = "Wine";
        w.City.ResetWLTKD();
        Assert.False(w.City.HasFlag(CityFlags.WeLoveTheKing));
        Assert.False(w.City.HasFlag(CityFlags.ResourceDemand));
        Assert.True(w.City.HasFlag(CityFlags.Resistance));
        Assert.Equal("", w.City.DemandedResource);
    }

    // ---- Transients, tiles

    [Fact]
    public void SetTransients_WiresTheSubObjectsAndTheCachedTiles()
    {
        var w = NewWorld();
        Assert.Same(w.Civ, w.City.Civ);
        Assert.Same(w.Map, w.City.TileMap);
        Assert.Same(w.TileAt(2, 2), w.City.GetCenterTile());
        Assert.Same(w.TileAt(2, 2), w.City.GetCenterTileOrNull());
        Assert.Same(w.City, w.Fake.PopulationFake.City);
        Assert.Same(w.City, w.Fake.ConstructionsFake.City);
        Assert.Same(w.City, w.Fake.ExpansionFake.City);
        Assert.Same(w.City, w.City.Espionage.City);
        Assert.Equal(GameContext.ForCity(w.City), w.City.State);
        // cityWorkRange 3: every tile of a 5x5 grid within 3 steps of the center
        Assert.All(w.City.TilesInRange, it => Assert.True(it.AerialDistanceTo(w.TileAt(2, 2)) <= 3));
        Assert.Contains(w.TileAt(2, 2), w.City.TilesInRange);
        Assert.Equal(w.Info.RulesetData.ModOptions.Constants.CityWorkRange, w.City.GetWorkRange());
        Assert.Equal(w.Info.RulesetData.ModOptions.Constants.CityExpandRange, w.City.GetExpandRange());
        Assert.Equal(w.Info.RulesetData.ModOptions.Constants.BaseCityBombardRange, w.City.GetBombardRange());
    }

    [Fact]
    public void Lateinit_PropertiesThrowBeforeAssignment()
    {
        var city = new CityObject();
        Assert.Throws<InvalidOperationException>(() => city.Civ);
        Assert.Throws<InvalidOperationException>(() => city.TileMap);
        Assert.Throws<InvalidOperationException>(() => city.GetCenterTile());
        Assert.Null(city.GetCenterTileOrNull());
        Assert.Throws<InvalidOperationException>(() => city.CityConstructions);
        Assert.Throws<InvalidOperationException>(() => city.Population);
    }

    [Fact]
    public void SetTransients_KeepsAnExistingId_AndGivesAPseudoRandomOneOtherwise()
    {
        var w = NewWorld();
        Assert.Equal("c1", w.City.Id);
        var fresh = new FakeCityState("x", new FakeCiv("Rome")) { CivData = w.Civ, LocationData = HexCoord.Of(0, 0) };
        fresh.City.Id = CityObject.NoId;
        fresh.City.SetTransients(w.Civ);
        Assert.NotEqual(CityObject.NoId, fresh.City.Id);
        Assert.Equal(36, fresh.City.Id.Length);
        Assert.Equal('4', fresh.City.Id[14]); // a version 4 uuid
        Assert.Equal(CityObject.PseudoRandomId(w.Civ), CityObject.PseudoRandomId(w.Civ)); // deterministic for the same state
    }

    [Fact]
    public void GetTiles_ResolvesThePositionsInSetOrder_AndGetWorkedTiles()
    {
        var w = NewWorld();
        w.City.Tiles.Add(HexCoord.Of(2, 2));
        w.City.Tiles.Add(HexCoord.Of(1, 2));
        w.City.WorkedTiles.Add(HexCoord.Of(1, 2));
        Assert.Equal([w.TileAt(2, 2), w.TileAt(1, 2)], w.City.GetTiles().OrderBy(it => it.Position.x).Reverse());
        Assert.Equal([w.TileAt(1, 2)], w.City.GetWorkedTiles());
        Assert.True(w.City.IsWorked(w.TileAt(1, 2)));
        Assert.False(w.City.IsWorked(w.TileAt(2, 2)));
    }

    [Fact]
    public void WorkTile_NeedsAWorkableTile_AndReplacesTheSetInsteadOfMutatingIt()
    {
        var w = NewWorld();
        var tile = w.TileAt(1, 2);
        Assert.Throws<ArgumentException>(() => w.City.WorkTile(tile)); // not owned by the civ: not workable
        tile.SetOwningCity(w.City);
        var before = w.City.WorkedTiles;
        Assert.True(w.City.WorkTile(tile));
        Assert.False(w.City.WorkTile(tile)); // already worked
        Assert.NotSame(before, w.City.WorkedTiles);
        Assert.Empty(before);
        Assert.True(w.City.IsWorked(tile));
    }

    [Fact]
    public void LockTile_NeedsAWorkedTile_AndStopWorkingUnlocks()
    {
        var w = NewWorld();
        var tile = w.TileAt(1, 2);
        tile.SetOwningCity(w.City);
        Assert.Throws<ArgumentException>(() => w.City.LockTile(tile));
        w.City.WorkTile(tile);
        Assert.True(w.City.LockTile(tile));
        Assert.False(w.City.LockTile(tile));
        Assert.True(w.City.LockedTiles.Contains(tile.Position));
        Assert.True(w.City.StopWorkingTile(tile));
        Assert.False(w.City.IsWorked(tile));
        Assert.False(w.City.LockedTiles.Contains(tile.Position));
        Assert.False(w.City.StopWorkingTile(tile));
        Assert.False(w.City.UnlockTile(tile));
    }

    [Fact]
    public void ClearWorkedTiles_EmptiesBothSets()
    {
        var w = NewWorld();
        w.City.WorkedTiles.Add(HexCoord.Of(1, 1));
        w.City.LockedTiles.Add(HexCoord.Of(1, 1));
        w.City.ClearWorkedTiles();
        Assert.Equal(0, w.City.WorkedTiles.Count);
        Assert.Equal(0, w.City.LockedTiles.Count);
    }

    [Fact]
    public void GetWorkableTiles_AreTheTilesInRangeOwnedByTheCivilization()
    {
        var w = NewWorld();
        Assert.Empty(w.City.GetWorkableTiles());
        w.TileAt(3, 2).SetOwningCity(w.City);
        Assert.Equal([w.TileAt(3, 2)], w.City.GetWorkableTiles());
    }

    [Fact]
    public void IsCoastal_FollowsAdjacentCoastTiles_AndIsNavalForWaterCenters()
    {
        var inland = NewWorld();
        Assert.False(inland.City.IsCoastal());
        Assert.False(inland.City.IsNaval());
        Assert.False(inland.City.IsBlockaded()); // inland cities are never blockaded
        var w = NewWorld(coastWestOfTheCity: true);
        Assert.True(w.City.IsCoastal());
        Assert.True(w.City.IsNaval());
    }

    // ---- Neighbours, garrison, capital, holy city

    [Fact]
    public void GetNeighboringCities_ExcludesSelfDistantAndUnexploredCities()
    {
        var w = NewWorld();
        var near = new FakeCityState("near", new FakeCiv("Rome")) { CivData = w.Civ, LocationData = HexCoord.Of(3, 2) };
        var far = new FakeCityState("far", new FakeCiv("Rome")) { CivData = w.Civ, LocationData = HexCoord.Of(0, 0) };
        var hidden = new FakeCityState("hidden", new FakeCiv("Rome")) { CivData = w.Civ, LocationData = HexCoord.Of(2, 3) };
        w.Civ.Cities = [w.Fake, near, far, hidden];
        foreach (var fake in new[] { near, far, hidden }) fake.City.SetTransients(w.Civ);
        foreach (var tile in new[] { w.TileAt(2, 2), w.TileAt(3, 2), w.TileAt(0, 0) }) tile.SetExplored(w.Civ, true);

        Assert.Equal([near.City], w.City.GetNeighboringCities(maxDistance: 1));
        Assert.Equal([near.City, far.City], w.City.GetNeighboringCities());
    }

    [Fact]
    public void IsCapital_ReadsTheIndicatesCapitalUnique()
    {
        var w = NewWorld();
        Assert.False(w.City.IsCapital());
        w.Fake.Capital = true;
        Assert.True(w.City.IsCapital());
    }

    [Fact]
    public void IsHolyCity_NeedsAHolyReligionThatIsNotBlocked()
    {
        var w = NewWorld();
        Assert.False(w.City.IsHolyCity());
        w.Fake.ReligionData.ReligionThisIsTheHolyCityOf = "Buddhism";
        Assert.True(w.City.IsHolyCity());
        Assert.True(w.City.IsHolyCityOf("Buddhism"));
        Assert.False(w.City.IsHolyCityOf("Taoism"));
        Assert.False(w.City.IsHolyCityOf(null));
        w.Fake.ReligionData.BlockedHolyCity = true;
        Assert.False(w.City.IsHolyCity());
    }

    [Fact]
    public void CanBeDestroyed_RespectsRulesAndModOptions()
    {
        var w = NewWorld();
        Assert.True(w.City.CanBeDestroyed());
        w.City.IsOriginalCapital = true;
        Assert.False(w.City.CanBeDestroyed());
        w.City.IsOriginalCapital = false;
        w.Fake.Capital = true;
        Assert.False(w.City.CanBeDestroyed());
        Assert.True(w.City.CanBeDestroyed(justCaptured: true));
        w.Fake.Capital = false;
        w.Fake.ReligionData.ReligionThisIsTheHolyCityOf = "Buddhism";
        Assert.False(w.City.CanBeDestroyed());
        w.Info.GameParameters.NoCityRazing = true;
        w.Fake.ReligionData.ReligionThisIsTheHolyCityOf = null;
        Assert.False(w.City.CanBeDestroyed());
    }

    // ---- Health, strength, selling, buying

    [Fact]
    public void MaxHealthAndStrength_SumTheBuiltBuildings()
    {
        var w = NewWorld();
        Assert.Equal(200, w.City.GetMaxHealth());
        Assert.Equal(0f, w.City.GetStrength());
        w.Fake.BuiltBuildingObjects.Add(Make("Walls", it => { it.CityHealth = 50; it.CityStrength = 5.5; }));
        w.Fake.BuiltBuildingObjects.Add(Make("Castle", it => { it.CityHealth = 30; it.CityStrength = 3.25; }));
        Assert.Equal(280, w.City.GetMaxHealth());
        Assert.Equal(8.75f, w.City.GetStrength());
    }

    [Fact]
    public void GetGoldForSellingBuilding_IsATenthOfTheCost_UsingIntegerDivision()
    {
        var w = NewWorld();
        w.Info.RulesetData.Buildings.Put("Library", Make("Library", it => it.Cost = 79));
        Assert.Equal(7, w.City.GetGoldForSellingBuilding("Library"));
        Assert.Throws<KeyNotFoundException>(() => w.City.GetGoldForSellingBuilding("Nope"));
    }

    [Fact]
    public void SellBuilding_RemovesItPaysGoldAndReassignsThePopulation()
    {
        var w = NewWorld();
        var library = Make("Library", it => it.Cost = 120);
        w.Info.RulesetData.Buildings.Put("Library", library);
        w.Fake.BuiltBuildingObjects.Add(library);
        var gold = w.Civ.Gold;

        w.City.SellBuilding("Library");

        Assert.Empty(w.Fake.BuiltBuildingObjects);
        Assert.Equal(gold + 12, w.Civ.Gold);
        Assert.True(w.City.HasSoldBuildingThisTurn);
        Assert.Equal(1, w.Fake.PopulationFake.UnassignCalls);
        Assert.Equal(1, w.Fake.PopulationFake.AutoAssignCalls);
    }

    [Fact]
    public void HasStatToBuy_FreeOrGodModeOrEnoughReserve()
    {
        var w = NewWorld();
        w.Civ.AddGold(50);
        Assert.True(w.City.HasStatToBuy(Stat.Gold, 0));
        Assert.True(w.City.HasStatToBuy(Stat.Gold, 50));
        Assert.False(w.City.HasStatToBuy(Stat.Gold, 51));
        w.Info.GameParameters.GodMode = true;
        Assert.True(w.City.HasStatToBuy(Stat.Gold, 5000));
    }

    [Fact]
    public void AddStat_RoutesProductionAndFoodToTheCity_AndTheRestToTheCiv()
    {
        var w = NewWorld();
        w.City.AddStat(Stat.Production, 7);
        w.City.AddStat(Stat.Food, 3);
        w.City.AddStat(Stat.Gold, 11);
        Assert.Equal(7, w.Fake.ConstructionsFake.ProductionAdded);
        Assert.Equal(3, w.Fake.PopulationFake.FoodStored);
        Assert.Equal(11, w.Civ.Gold);
        Assert.Equal(3, w.City.GetStatReserve(Stat.Food));
        Assert.Equal(11, w.City.GetStatReserve(Stat.Gold));
    }

    [Fact]
    public void AddGameResource_StoredFoodAndStockpiledResources()
    {
        var w = NewWorld(configure: ruleset =>
            ruleset.TileResources.Put("Pigs", new TileResource { Name = "Pigs", ResourceType = ResourceType.Bonus, Uniques = ["Stockpiled", "City-level resource"], ImprovedBy = ["Mine"] }));
        w.City.AddGameResource(SubStat.StoredFood, 4);
        Assert.Equal(4, w.Fake.PopulationFake.FoodStored);
        Assert.Equal(4, w.City.GetGameResource(SubStat.StoredFood));
        w.City.AddGameResource(Stat.Food, 1);
        Assert.Equal(5, w.Fake.PopulationFake.FoodStored);
        w.City.AddGameResource(Stat.Production, 2);
        Assert.Equal(2, w.Fake.ConstructionsFake.ProductionAdded);

        var iron = w.Info.RulesetData.TileResources["Iron"];
        w.City.AddGameResource(iron, 9); // not stockpiled: ignored
        Assert.Equal(0, w.City.ResourceStockpiles["Iron"]);
        Assert.Equal(0, w.Civ.ResourceStockpiles["Iron"]);
        var pigs = w.Info.RulesetData.TileResources["Pigs"];
        Assert.True(pigs.IsStockpiled);
        Assert.True(pigs.IsCityWide);
        w.City.AddGameResource(pigs, 6);
        Assert.Equal(6, w.City.ResourceStockpiles["Pigs"]);
        Assert.Equal(6, w.City.GetAvailableResourceAmount(pigs));
        Assert.Equal(6, w.City.GetAvailableResourceAmount("Pigs"));
        Assert.Equal(0, w.City.GetAvailableResourceAmount("Unknown"));
    }

    [Fact]
    public void FoodForNextTurn_RoundsHalfUp_AndDecidesGrowingAndStarving()
    {
        var w = NewWorld();
        w.Fake.StatsFake.CurrentCityStats = new Stats(food: 2.5f);
        Assert.Equal(3, w.City.FoodForNextTurn());
        Assert.True(w.City.IsGrowing());
        Assert.False(w.City.IsStarving());
        w.Fake.StatsFake.CurrentCityStats = new Stats(food: -2.5f);
        Assert.Equal(-2, w.City.FoodForNextTurn()); // half up: -2.5 -> -2
        Assert.True(w.City.IsStarving());
        w.Fake.StatsFake.CurrentCityStats = new Stats(food: 0.4f);
        Assert.False(w.City.IsGrowing());
        Assert.False(w.City.IsStarving());
    }

    [Fact]
    public void GetMaxAirUnits_AddsTheCarryExtraAirUnitsUniques()
    {
        var w = NewWorld();
        var capacity = w.Info.RulesetData.ModOptions.Constants.CityAirUnitCapacity;
        Assert.Equal(capacity, w.City.GetMaxAirUnits());
        w.Fake.BuiltUniques.AddUnique(new UniqueObject("Can carry [2] extra [Air] units", UniqueTarget.Building, "Airport"));
        w.Fake.BuiltUniques.AddUnique(new UniqueObject("Can carry [5] extra [Missile] units", UniqueTarget.Building, "Bunker"));
        Assert.Equal(capacity + 2, w.City.GetMaxAirUnits());
    }

    // ---- Population

    [Fact]
    public void ReassignPopulation_ResetLocked_ClearsBothSets_ElseRestartsFromTheLockedTiles()
    {
        var w = NewWorld();
        w.City.WorkedTiles.Add(HexCoord.Of(1, 1));
        w.City.LockedTiles.Add(HexCoord.Of(2, 1));
        w.Fake.PopulationFake.SpecialistAllocations.Add("Scientist", 2);
        w.City.ShouldReassignPopulation = true;

        w.City.ReassignPopulation();

        Assert.Same(w.City.LockedTiles, w.City.WorkedTiles); // Kotlin shares the reference
        Assert.Equal(0, w.Fake.PopulationFake.SpecialistAllocations["Scientist"]);
        Assert.False(w.City.ShouldReassignPopulation);
        Assert.Equal(1, w.Fake.PopulationFake.AutoAssignCalls);

        w.City.ReassignPopulation(resetLocked: true);
        Assert.Equal(0, w.City.WorkedTiles.Count);
        Assert.Equal(0, w.City.LockedTiles.Count);
        Assert.Equal(2, w.Fake.PopulationFake.AutoAssignCalls);
    }

    [Fact]
    public void ReassignPopulation_ManualFocusKeepsTheWorkedTiles_AndManualSpecialistsKeepTheAllocation()
    {
        var w = NewWorld();
        w.City.SetCityFocus(CityFocus.Manual);
        w.City.WorkedTiles.Add(HexCoord.Of(1, 1));
        w.City.ManualSpecialists = true;
        w.Fake.PopulationFake.SpecialistAllocations.Add("Scientist", 2);

        w.City.ReassignPopulation();

        Assert.True(w.City.WorkedTiles.Contains(HexCoord.Of(1, 1)));
        Assert.Equal(2, w.Fake.PopulationFake.SpecialistAllocations["Scientist"]);

        w.City.ReassignAllPopulation();
        Assert.False(w.City.ManualSpecialists);
        Assert.Equal(0, w.City.WorkedTiles.Count);
        Assert.Equal(0, w.Fake.PopulationFake.SpecialistAllocations["Scientist"]);
    }

    [Fact]
    public void ReassignPopulationDeferred_IsImmediateOnlyForTheCurrentHumanPlayer()
    {
        var w = NewWorld();
        w.City.ReassignPopulationDeferred();
        Assert.True(w.City.ShouldReassignPopulation); // not the current player
        Assert.Equal(0, w.Fake.PopulationFake.AutoAssignCalls);

        w.Info.CurrentPlayer = w.Civ;
        w.City.ReassignPopulationDeferred();
        Assert.False(w.City.ShouldReassignPopulation);
        Assert.Equal(1, w.Fake.PopulationFake.AutoAssignCalls);

        var ai = NewWorld(human: false);
        ai.Info.CurrentPlayer = ai.Civ;
        ai.City.ReassignPopulationDeferred();
        Assert.True(ai.City.ShouldReassignPopulation);
        Assert.Equal(0, ai.Fake.PopulationFake.AutoAssignCalls);
    }

    [Fact]
    public void ResetSpecialistsControl_OnlyHumansWhoseTurnItIsApplyTheSetting()
    {
        var w = NewWorld();
        w.City.ResetSpecialistsControl(autoAssignSpecialistsInNewCities: false);
        Assert.True(w.City.ManualSpecialists);
        w.City.ResetSpecialistsControl(autoAssignSpecialistsInNewCities: true);
        Assert.False(w.City.ManualSpecialists);

        w.Info.GameParameters.IsOnlineMultiplayer = true;
        w.Civ.PlayerId = "me";
        w.City.ResetSpecialistsControl(false, localUserId: "someone else");
        Assert.False(w.City.ManualSpecialists); // not our turn: default
        w.City.ResetSpecialistsControl(false, localUserId: "me");
        Assert.True(w.City.ManualSpecialists);

        var ai = NewWorld(human: false);
        ai.City.ResetSpecialistsControl(false);
        Assert.False(ai.City.ManualSpecialists);
    }

    [Fact]
    public void ResetDisabledConstructions_CopiesTheCivSettingOnlyForHumans()
    {
        var w = NewWorld();
        w.Civ.DisabledCityConstructions.Add("Library");
        w.City.DisabledConstructions.Add("Stale");
        w.City.ResetDisabledConstructions();
        Assert.Equal(["Library"], w.City.DisabledConstructions);

        var ai = NewWorld(human: false);
        ai.Civ.DisabledCityConstructions.Add("Library");
        ai.City.DisabledConstructions.Add("Stale");
        ai.City.ResetDisabledConstructions();
        Assert.Empty(ai.City.DisabledConstructions);
    }

    // ---- Clone

    [Fact]
    public void Clone_CopiesTheFields_AndSharesTheTileSetsAndPromotionMaps()
    {
        var w = NewWorld();
        var city = w.City;
        city.Name = "Roma";
        city.Health = 150;
        city.PreviousOwner = "Egypt";
        city.TurnAcquired = 12;
        city.IsPuppet = true;
        city.IsOriginalCapital = true;
        city.IsBeingRazed = true;
        city.AvoidGrowth = true;
        city.ManualSpecialists = true;
        city.ConnectedToCapitalStatus = true;
        city.DemandedResource = "Wine";
        city.ShouldReassignPopulation = true;
        city.AttackedThisTurn = true;
        city.HasSoldBuildingThisTurn = true;
        city.SetCityFocus(CityFocus.FaithFocus);
        city.SetFlag(CityFlags.Resistance, 3);
        city.ResourceStockpiles.Add("Iron", 4);
        city.DisabledConstructions.Add("Library");
        city.Tiles.Add(HexCoord.Of(1, 1));
        city.FoundingCivObject = w.Civ;
        var clone = Clone(w);

        Assert.NotSame(city, clone);
        Assert.Equal("c1", clone.Id);
        Assert.Equal("Roma", clone.Name);
        Assert.Equal(HexCoord.Of(2, 2), clone.Location);
        Assert.Equal(150, clone.Health);
        Assert.Equal("Egypt", clone.PreviousOwner);
        Assert.Equal(12, clone.TurnAcquired);
        Assert.True(clone.IsPuppet && clone.IsOriginalCapital && clone.IsBeingRazed && clone.AvoidGrowth && clone.ManualSpecialists);
        Assert.True(clone.ConnectedToCapitalStatus && clone.ShouldReassignPopulation && clone.AttackedThisTurn && clone.HasSoldBuildingThisTurn);
        Assert.Equal("Wine", clone.DemandedResource);
        Assert.Equal(CityFocus.FaithFocus, clone.GetCityFocus());
        Assert.Equal(3, clone.GetFlag(CityFlags.Resistance));
        Assert.Equal(4, clone.ResourceStockpiles["Iron"]);
        Assert.NotSame(city.ResourceStockpiles, clone.ResourceStockpiles);
        Assert.Equal(["Library"], clone.DisabledConstructions);
        Assert.NotSame(city.DisabledConstructions, clone.DisabledConstructions);
        Assert.Same(city.Tiles, clone.Tiles);
        Assert.Same(city.WorkedTiles, clone.WorkedTiles);
        Assert.Same(city.LockedTiles, clone.LockedTiles);
        Assert.Same(city.UnitToPromotions, clone.UnitToPromotions);
        Assert.Same(city.UnitShouldUseSavedPromotion, clone.UnitShouldUseSavedPromotion);
        Assert.Same(w.Civ, WithTransients(clone, w).FoundingCivObject); // the foundingCiv string is copied
    }

    private static CityObject Clone(World w)
    {
        w.Fake.PopulationFake.CloneResult = new FakeCityPopulation();
        w.Fake.ConstructionsFake.CloneResult = new FakeCityConstructions();
        w.Fake.ExpansionFake.CloneResult = new FakeCityExpansion();
        w.Fake.ReligionData.CloneResult = new FakeCityReligion();
        return w.City.Clone();
    }

    private static CityObject WithTransients(CityObject clone, World w)
    {
        clone.Civ = w.Civ;
        return clone;
    }

    // ---- MatchesFilter

    [Fact]
    public void MatchesFilter_SimpleStateFilters()
    {
        var w = NewWorld();
        var city = w.City;
        Assert.True(city.MatchesFilter("in this city"));
        Assert.True(city.MatchesFilter("in all cities"));
        Assert.True(city.MatchesFilter(Constants.LowercaseAll));
        Assert.True(city.MatchesFilter(Constants.UppercaseAll));
        Assert.True(city.MatchesFilter("in your cities"));
        Assert.True(city.MatchesFilter("Your"));
        Assert.False(city.MatchesFilter("Your", null));
        Assert.False(city.MatchesFilter("Capital"));
        Assert.False(city.MatchesFilter("Puppeted"));
        city.IsPuppet = true;
        city.IsBeingRazed = true;
        city.SetFlag(CityFlags.Resistance, 1);
        w.Fake.Capital = true;
        Assert.True(city.MatchesFilter("in capital"));
        Assert.True(city.MatchesFilter("in puppeted cities"));
        Assert.True(city.MatchesFilter("Razing"));
        Assert.True(city.MatchesFilter("in resisting cities"));
        Assert.False(city.MatchesFilter("Coastal"));
        Assert.False(city.MatchesFilter("Holy"));
        Assert.False(city.MatchesFilter("Garrisoned"));
        Assert.False(city.MatchesFilter("in City-State cities"));
        Assert.True(city.MatchesFilter("in cities following this religion"));
    }

    [Fact]
    public void MatchesFilter_ViewingCivFilters()
    {
        var w = NewWorld();
        var other = new Civilization(new NationObject { Name = "Egypt" }) { GameInfo = w.Info };
        Assert.False(w.City.MatchesFilter("Foreign"));
        Assert.True(w.City.MatchesFilter("Foreign", other));
        Assert.False(w.City.MatchesFilter("Foreign", null));
        Assert.False(w.City.MatchesFilter("Your", other));
        Assert.True(w.City.MatchesFilter("Annexed")); // founded by nobody (diplomatic marriage): foundingCivObject (null) != civ, and not a puppet
        w.City.FoundingCivObject = w.Civ;
        Assert.False(w.City.MatchesFilter("Annexed"));
        w.City.FoundingCivObject = other;
        Assert.True(w.City.MatchesFilter("in annexed cities"));
        w.City.IsPuppet = true;
        Assert.False(w.City.MatchesFilter("in annexed cities"));
    }

    [Fact]
    public void MatchesFilter_MultiFilters_AndTheSingleFilterSwitch()
    {
        var w = NewWorld();
        w.City.IsPuppet = true;
        Assert.True(w.City.MatchesFilter("{Puppeted} {Your}"));
        Assert.False(w.City.MatchesFilter("{Puppeted} {Capital}"));
        Assert.True(w.City.MatchesFilter("non-[Capital]"));
        Assert.False(w.City.MatchesFilter("non-[Puppeted]"));
        Assert.False(w.City.MatchesFilter("non-[Puppeted]", w.Civ, multiFilter: false)); // taken as one unknown filter
    }

    [Fact]
    public void MatchesFilter_NonOccupiedUsesTheExtraAnnexUnhappiness()
    {
        var w = NewWorld();
        Assert.True(w.City.MatchesFilter("Non-occupied"));
        w.Fake.StatsFake.ExtraAnnexUnhappiness = true;
        Assert.False(w.City.MatchesFilter("in all non-occupied cities"));
        w.City.IsPuppet = true;
        Assert.True(w.City.MatchesFilter("Non-occupied"));
    }

    [Fact]
    public void MatchesFilter_WonderAndReligionFilters()
    {
        var w = NewWorld();
        Assert.False(w.City.MatchesFilter("in all cities with a world wonder"));
        w.Fake.BuiltBuildingObjects.Add(Make("Pyramids", it => it.IsWonder = true));
        Assert.True(w.City.MatchesFilter("in all cities with a world wonder"));

        Assert.False(w.City.MatchesFilter("in all cities in which the majority religion is a major religion"));
        var religion = new Religion("Buddhism", w.Info, w.Civ.CivId, 0);
        w.Fake.ReligionData.Majority = religion;
        Assert.False(w.City.MatchesFilter("in all cities in which the majority religion is a major religion")); // no founder belief
        Assert.False(w.City.MatchesFilter("in all cities in which the majority religion is an enhanced religion"));
        // Kotlin compares the viewing civ's own religion with the majority religion
        Assert.False(w.City.MatchesFilter("in cities following our religion"));
        w.Civ.ReligionManager.Religion = religion;
        Assert.True(w.City.MatchesFilter("in cities following our religion"));
    }

    // ---- Uniques

    private static UniqueObject LocalUnique(string text) => new(text, UniqueTarget.Building, "Test");

    private static List<UniqueObject> SortedByText(IEnumerable<UniqueObject> uniques) => uniques.OrderBy(it => it.Text, StringComparer.Ordinal).ToList();

    [Fact]
    public void Uniques_LocalAndNonLocalBuildingUniques_AreSeparatedByIsLocalEffect()
    {
        var w = NewWorld();
        var local = LocalUnique("Provides [1] [Iron] <in this city>");
        var global = LocalUnique("Provides [3] [Iron]");
        w.Fake.BuiltUniques.AddUnique(local);
        w.Fake.BuiltUniques.AddUnique(global);

        Assert.Equal([local], w.City.GetLocalMatchingUniques(UniqueType.ProvidesResources));
        Assert.Equal([global], w.City.GetMatchingUniquesWithNonLocalEffects(UniqueType.ProvidesResources));
        var each = new List<UniqueObject>();
        w.City.ForEachLocalMatchingUnique(UniqueType.ProvidesResources, w.City.State, each.Add);
        Assert.Equal([local], each);
        each.Clear();
        w.City.ForEachMatchingUniqueWithNonLocalEffects(UniqueType.ProvidesResources, w.City.State, each.Add);
        Assert.Equal([global], each);
        // without the civ: both the local and non-local building uniques
        Assert.Equal([local, global], w.City.GetMatchingUniques(UniqueType.ProvidesResources, includeCivUniques: false));
        each.Clear();
        w.City.ForEachMatchingUnique(UniqueType.ProvidesResources, w.City.State, false, each.Add);
        Assert.Equal([local, global], each);
    }

    [Fact]
    public void Uniques_IncludingTheCiv_ListsTheCivsUniquesThenTheLocalOnes()
    {
        var w = NewWorld(nationUniques: ["Provides [5] [Iron]"]);
        var local = LocalUnique("Provides [1] [Iron] <in this city>");
        w.Fake.BuiltUniques.AddUnique(local);
        var matching = w.City.GetMatchingUniques(UniqueType.ProvidesResources).ToList();
        Assert.Equal(2, matching.Count);
        Assert.Equal("Provides [5] [Iron]", matching[0].Text);
        Assert.Same(local, matching[1]);
        var each = new List<UniqueObject>();
        w.City.ForEachMatchingUnique(UniqueType.ProvidesResources, each.Add);
        Assert.Equal(matching, each);
        each.Clear();
        w.City.ForEachAffectingMatchingUnique(UniqueType.ProvidesResources, w.City.State, each.Add);
        Assert.Equal([local, matching[0]], each); // local first
    }

    [Fact]
    public void Uniques_TimedTriggerablesAreSkipped()
    {
        var w = NewWorld();
        w.Fake.BuiltUniques.AddUnique(LocalUnique("Provides [1] [Iron] <in this city> <for [5] turns>"));
        Assert.Empty(w.City.GetLocalMatchingUniques(UniqueType.ProvidesResources));
        Assert.Empty(w.City.GetMatchingUniques(UniqueType.ProvidesResources, includeCivUniques: false));
    }

    [Fact]
    public void ContainsBuildingUnique_ChecksTheBuiltBuildingUniqueMap()
    {
        var w = NewWorld();
        Assert.False(w.City.ContainsBuildingUnique(UniqueType.ProvidesExtraLuxuryFromCityResources));
        w.Fake.BuiltUniques.AddUnique(LocalUnique("Provides 1 extra copy of each improved luxury resource near this City"));
        Assert.True(w.City.ContainsBuildingUnique(UniqueType.ProvidesExtraLuxuryFromCityResources));
    }

    [Fact]
    public void TriggeredUniques_LocalOnesComeFromLocalBuildingUniques()
    {
        var w = NewWorld();
        var trigger = LocalUnique("Gain [100] Gold <upon entering a Golden Age> <in this city>");
        var global = LocalUnique("Gain [50] Gold <upon entering a Golden Age>");
        w.Fake.BuiltUniques.AddUnique(trigger);
        w.Fake.BuiltUniques.AddUnique(global);
        Assert.Equal([trigger], w.City.GetLocalTriggeredUniques(UniqueType.TriggerUponEnteringGoldenAge));
        Assert.Equal([trigger, global], SortedByText(w.City.GetTriggeredUniques(UniqueType.TriggerUponEnteringGoldenAge, includeCivUniques: false)));
        var each = new List<UniqueObject>();
        w.City.ForEachLocalTriggeredUnique(UniqueType.TriggerUponEnteringGoldenAge, w.City.State, each.Add);
        Assert.Equal([trigger], each);
        each.Clear();
        w.City.ForEachTriggeredUnique(UniqueType.TriggerUponEnteringGoldenAge, null, null, false, each.Add);
        Assert.Equal([trigger, global], SortedByText(each));
        each.Clear();
        w.City.ForEachLocalTriggeredUnique(UniqueType.TriggerUponEnteringGoldenAge, w.City.State, _ => false, each.Add);
        Assert.Empty(each);
    }

    // ---- CityResources

    private static void AddResourceTile(World w, int x, int y, string resource, int amount)
    {
        var tile = w.TileAt(x, y);
        tile.SetTileResource(resource, updateCache: true);
        tile.ResourceAmount = amount;
        tile.Improvement = "Mine"; // resources of tiles outside the city center only count with their improvement
        w.City.Tiles.Add(HexCoord.Of(x, y));
    }

    [Fact]
    public void CityResources_TilesAddStrategicAmountsAndOneForOtherTypes()
    {
        var w = NewWorld();
        AddResourceTile(w, 1, 1, "Iron", 4);
        AddResourceTile(w, 3, 3, "Wine", 6);
        AddResourceTile(w, 3, 2, "Salt", 5);
        var supply = w.City.GetResourcesGeneratedByCity();
        Assert.Equal(4, supply.SumBy("Iron"));
        Assert.Equal(1, supply.SumBy("Wine"));
        Assert.Equal(1, supply.SumBy("Salt"));
        Assert.Equal("Tiles", supply.Get(w.Info.RulesetData.TileResources["Iron"], "Tiles")!.Origin);
        // civ-wide resources come from the civ, not from the city
        w.Civ.ResourceStockpiles.Add("Iron", 0);
        Assert.Equal(0, w.City.GetAvailableResourceAmount("Iron"));
    }

    [Fact]
    public void CityResources_LuxuryGetsOneExtraFromTheBuildingUnique()
    {
        var w = NewWorld();
        AddResourceTile(w, 3, 3, "Wine", 1);
        w.Fake.BuiltUniques.AddUnique(LocalUnique("Provides 1 extra copy of each improved luxury resource near this City"));
        Assert.Equal(2, w.City.GetResourcesGeneratedByCity().SumBy("Wine"));
    }

    [Fact]
    public void CityResources_BuildingUniquesAndModifiers()
    {
        var w = NewWorld();
        AddResourceTile(w, 1, 1, "Iron", 4);
        w.Fake.BuiltUniques.AddUnique(LocalUnique("Provides [2] [Iron]"));
        w.Fake.BuiltUniques.AddUnique(LocalUnique("[+50]% [Iron] resource production"));
        var supply = w.City.GetResourcesGeneratedByCity();
        // (4 from the tile, 2 from the building) * 1.5, each entry truncated on its own
        Assert.Equal(4 * 3 / 2 + 2 * 3 / 2, supply.SumBy("Iron"));
        Assert.Equal(9, supply.SumBy("Iron"));
    }

    [Fact]
    public void CityResources_BuildingsConsumeTheirRequiredResource_UnlessFree()
    {
        var w = NewWorld();
        AddResourceTile(w, 1, 1, "Iron", 4);
        w.Fake.BuiltBuildingObjects.Add(Make("Forge", it => it.RequiredResource = "Iron"));
        w.Fake.BuiltBuildingObjects.Add(Make("Smithy", it => it.RequiredResource = "Iron"));
        Assert.Equal(2, w.City.GetResourcesGeneratedByCity().SumBy("Iron"));
        w.Fake.FreeBuildingsProvidedFromThisCity[w.City.Id] = ["Forge"];
        Assert.Equal(3, w.City.GetResourcesGeneratedByCity().SumBy("Iron"));
    }

    [Fact]
    public void CityResources_CityStateCapitalProvidesItsMercantileResource()
    {
        var w = NewWorld(cityStateType: "Mercantile");
        w.Civ.CityStateResource = "Wine";
        w.Fake.Capital = true;
        var supply = w.City.GetResourcesGeneratedByCity();
        Assert.Equal(1, supply.SumBy("Wine"));
        Assert.Equal("Mercantile City-State", supply.Get(w.Info.RulesetData.TileResources["Wine"], "Mercantile City-State")!.Origin);
    }

    [Fact]
    public void CityResources_AvailableToTheCityOnlyListsCityWideResources()
    {
        var w = NewWorld(configure: ruleset =>
            ruleset.TileResources.Put("Pigs", new TileResource { Name = "Pigs", ResourceType = ResourceType.Bonus, Uniques = ["City-level resource"], ImprovedBy = ["Mine"] }));
        AddResourceTile(w, 1, 1, "Iron", 4);
        AddResourceTile(w, 3, 3, "Pigs", 1);
        var available = CityResources.GetCityResourcesAvailableToCity(w.City);
        Assert.Equal(1, available.SumBy("Pigs"));
        Assert.Equal(0, available.SumBy("Iron"));
        Assert.Equal(1, w.City.GetAvailableResourceAmount("Pigs"));
    }

    [Fact]
    public void CityResources_StatPercentFromObjectToResource_UsesTheBuildingStats()
    {
        var w = NewWorld();
        w.Info.RulesetData.Buildings.Put("Library", Make("Library", it => it.Culture = 4f));
        w.Fake.BuiltBuildingObjects.Add(w.Info.RulesetData.Buildings["Library"]);
        w.Fake.BuiltUniques.AddUnique(LocalUnique("[50]% of [Culture] from every [Library] in the city added to [Iron]"));
        Assert.Equal(2, w.City.GetResourcesGeneratedByCity().SumBy("Iron"));
    }

    // ---- CityEspionageManager

    [Fact]
    public void Espionage_HasSpyOf_And_GetAllStationedSpies()
    {
        var w = NewWorld();
        Assert.False(w.City.Espionage.HasSpyOf(w.Civ));
        Assert.Empty(w.City.Espionage.GetAllStationedSpies());
        var spy = w.Civ.EspionageManager.AddSpy();
        spy.MoveTo(w.City);
        Assert.True(w.City.Espionage.HasSpyOf(w.Civ));
        Assert.Equal([spy], w.City.Espionage.GetAllStationedSpies());
    }

    [Theory]
    [InlineData(SpyFleeReason.CityDestroyed, "After the city of [c1] was destroyed, your spy [{0}] has fled back to our hideout.")]
    [InlineData(SpyFleeReason.CityCaptured, "After the city of [c1] was conquered, your spy [{0}] has fled back to our hideout.")]
    [InlineData(SpyFleeReason.CityBought, "After the city of [c1] was taken over, your spy [{0}] has fled back to our hideout.")]
    [InlineData(SpyFleeReason.CityTakenOverByMarriage, "After the city of [c1] was taken over, your spy [{0}] has fled back to our hideout.")]
    [InlineData(SpyFleeReason.Other, "Due to the chaos ensuing in [c1], your spy [{0}] has fled back to our hideout.")]
    public void Espionage_RemoveAllPresentSpies_SendsEachSpyHomeWithTheMatchingNotification(SpyFleeReason reason, string template)
    {
        var w = NewWorld();
        var spy = w.Civ.EspionageManager.AddSpy();
        spy.MoveTo(w.City);
        w.City.Espionage.RemoveAllPresentSpies(reason);
        Assert.Null(spy.GetCityOrNull());
        Assert.False(w.City.Espionage.HasSpyOf(w.Civ));
        Assert.Contains(w.Civ.Notifications, it => it.Text == string.Format(System.Globalization.CultureInfo.InvariantCulture, template, spy.Name));
    }

    [Fact]
    public void Espionage_GetAllStationedSpies_ChecksEveryCivilization()
    {
        var w = NewWorld();
        var other = new Civilization(new NationObject { Name = "Egypt" }) { GameInfo = w.Info, CityStateFunctions = new FakeCityStateFunctions() };
        other.EspionageManager.SetTransients(other);
        w.Info.Civs.Add(other);
        var spy = other.EspionageManager.AddSpy();
        spy.MoveTo(w.City);
        Assert.Equal([spy], w.City.Espionage.GetAllStationedSpies());
        Assert.True(w.City.Espionage.HasSpyOf(other));
    }

    [Fact]
    public void Espionage_CityIsLateinit()
    {
        Assert.Throws<InvalidOperationException>(() => new CityEspionageManager().City);
    }

    // ---- Destroying

    [Fact]
    public void DestroyCity_RelinquishesTilesRemovesTheCityAndSendsSpiesHome()
    {
        var w = NewWorld();
        var other = new FakeCityState("c2", new FakeCiv("Rome")) { CivData = w.Civ, LocationData = HexCoord.Of(0, 0) };
        w.Civ.Cities = [w.Fake, other];
        other.City.SetTransients(w.Civ);
        w.City.Tiles.Add(HexCoord.Of(2, 2));
        w.City.Tiles.Add(HexCoord.Of(1, 2));
        var spy = w.Civ.EspionageManager.AddSpy();
        spy.MoveTo(w.City);

        w.City.DestroyCity();

        Assert.Equal([other.City], w.Civ.Cities);
        Assert.Equal([w.TileAt(2, 2), w.TileAt(1, 2)], w.Fake.ExpansionFake.Relinquished.OrderByDescending(it => it.Position.x));
        Assert.Null(spy.GetCityOrNull());
    }

    [Fact]
    public void DestroyCity_IsRefusedForAnOriginalCapital_UnlessTheSafetiesAreOverridden()
    {
        var w = NewWorld();
        w.City.IsOriginalCapital = true;
        w.City.DestroyCity();
        Assert.Equal([w.City], w.Civ.Cities);
        w.City.DestroyCity(overrideSafeties: true);
        Assert.Empty(w.Civ.Cities);
    }

    [Fact]
    public void TryUpdateRoadStatus_UsesTheBestImprovementTheCivHasTheTechFor()
    {
        var w = NewWorld(configure: ruleset =>
        {
            ruleset.TileImprovements.Put("Road", new TileImprovement { Name = "Road", TechRequired = "The Wheel" });
            ruleset.TileImprovements.Put("Railroad", new TileImprovement { Name = "Railroad", TechRequired = "Railroad" });
        });
        w.City.TryUpdateRoadStatus();
        Assert.Equal(Unciv.Logic.Map.Tiles.RoadStatus.None, w.TileAt(2, 2).RoadStatus);
        w.Civ.Tech.TechsResearched.Add("The Wheel");
        w.City.TryUpdateRoadStatus();
        Assert.Equal(Unciv.Logic.Map.Tiles.RoadStatus.Road, w.TileAt(2, 2).RoadStatus);
        w.Civ.Tech.TechsResearched.Add("Railroad");
        w.City.TryUpdateRoadStatus();
        Assert.Equal(Unciv.Logic.Map.Tiles.RoadStatus.Railroad, w.TileAt(2, 2).RoadStatus);
    }

    [Fact]
    public void ConquestFunctions_AreNotPortedYet()
    {
        var w = NewWorld();
        Assert.Throws<InvalidOperationException>(() => w.City.AnnexCity());
        Assert.Throws<InvalidOperationException>(() => w.City.PuppetCity(w.Civ));
        Assert.Throws<InvalidOperationException>(() => w.City.LiberateCity(w.Civ));
        Assert.Throws<InvalidOperationException>(() => w.City.MoveToCiv(w.Civ));
    }

    [Fact]
    public void FoundingCivObject_ResolvesTheSerializedCivIdLazily()
    {
        var w = NewWorld();
        Assert.True(w.City.HasDiplomaticMarriage());
        w.City.FoundingCivObject = w.Civ;
        Assert.False(w.City.HasDiplomaticMarriage());
        Assert.Same(w.Civ, w.City.FoundingCivObject);
        w.City.FoundingCivObject = null;
        Assert.Null(w.City.FoundingCivObject);
    }

    [Fact]
    public void Json_IncludesThePrivateAndInternalSerializedFields()
    {
        var info = new System.Text.Json.JsonSerializerOptions { TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver() }.GetTypeInfo(typeof(CityObject));
        var names = info.Properties.Select(it => it.Name).ToList();
        Assert.Contains("foundingCiv", names);
        Assert.Contains("cityAIFocus", names);
        Assert.Contains("flagsCountdown", names);
    }
}
