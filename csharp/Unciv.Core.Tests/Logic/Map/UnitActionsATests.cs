using Unciv.Core.Tests.Support;
using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Managers;
using Unciv.Logic.Map;
using Unciv.Logic.Map.MapUnit.Actions;
using Unciv.Logic.Map.MapUnits;
using Unciv.Models;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tech;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Ruleset.Unit;
using Unciv.Models.Translations;
using Unciv.UI.Components;
using Unciv.Utils;
using CityObject = Unciv.Logic.City.City;
using MapUnitClass = Unciv.Logic.Map.MapUnits.MapUnit;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Core.Tests.Logic.Map;

// T079: UnitAction (model), UnitActionModifiers, UnitActionsPillage, UnitActionsUpgrade, UnitActionsGreatPerson, UnitActionsReligion over a real
// Civilization / TileMap / MapUnit with fake movement (see MapUnitCoreTests). The Kotlin fixtures that exercise these are TestGame-based
// (UpgradeTests: T072; the city-bound hurry/trade-mission/religion actions need real cities: T072/T035).
public class UnitActionsATests
{
    private sealed record World(Civilization Civ, FakeGameInfoState Info, TileMap Map, Ruleset Ruleset)
    {
        public TileClass TileAt(int x, int y) => Map[HexCoord.Of(x, y)];

        public MapUnitClass Unit(string name = "Warrior", int x = 2, int y = 2, Civilization? civ = null, params string[] uniques)
        {
            var owner = civ ?? Civ;
            var baseUnit = Ruleset.Units.TryGetValue(name, out var existing) ? existing : MapUnitFactory.NewBaseUnit(Ruleset, name, "Land", uniques: uniques);
            var unit = MapUnitFactory.NewUnit(owner, baseUnit, TileAt(x, y));
            unit.CurrentMovement = unit.GetMaxMovement();
            return unit;
        }
    }

    private static World NewWorld(Action<Ruleset>? configure = null)
    {
        var ruleset = new Ruleset();
        ruleset.Terrains.Put("Grassland", new Terrain { Name = "Grassland", Type = TerrainType.Land });
        ruleset.TileImprovements.Put("Farm", new TileImprovement { Name = "Farm" });
        MapUnitFactory.AddUnitTypes(ruleset);
        configure?.Invoke(ruleset);
        var info = new FakeGameInfoState { RulesetData = ruleset };
        var map = new TileMap(25) { GameInfo = info };
        for (var x = 0; x < 5; x++)
        {
            for (var y = 0; y < 5; y++)
                map.TileList.Add(new TileClass { Position = HexCoord.Of(x, y), BaseTerrain = "Grassland" });
        }
        map.SetTransients(ruleset);
        info.Map = map;
        var civ = MapUnitFactory.NewCiv("Rome", info);
        civ.ReligionManager.CivInfo = civ;
        return new World(civ, info, map, ruleset);
    }

    private const string Action = "May found a religion";

    // ---- UnitAction / UnitActionType

    [Fact]
    public void UnitActionType_ValuesAreTheDefaultLabels()
    {
        Assert.Equal("Stop Escort formation", UnitActionType.StopEscortFormation.Value());
        Assert.Equal("Upgrade to [unitType] ([goldCost] gold)", UnitActionType.Upgrade.Value());
        Assert.Equal("Repair", UnitActionType.Repair.Value());
        Assert.Equal($"{{Hurry Research}} ({Fonts.Death})", UnitActionType.HurryResearch.Value());
        Assert.Equal($"{{Conduct Trade Mission}} ({Fonts.Death})", UnitActionType.ConductTradeMission.Value());
        Assert.Equal("Spread [religionName]", UnitActionType.SpreadReligion.Value());
        Assert.Equal("Add in capital", UnitActionType.AddInCapital.Value());
        foreach (var type in Enum.GetValues<UnitActionType>())
            Assert.NotEmpty(type.Value());
        Assert.Throws<ArgumentOutOfRangeException>(() => ((UnitActionType)999).Value());
    }

    [Fact]
    public void UnitAction_TitleDefaultsToTheTypeValue_AndEqualsComparesTypeCurrentAndActionReference()
    {
        Action run = () => { };
        var plain = new UnitAction(UnitActionType.Sleep);
        Assert.Equal("Sleep", plain.Title);
        Assert.Null(plain.Action);
        Assert.Equal("UnitAction(type=Sleep, title='Sleep', isCurrentAction=False)", plain.ToString());
        var titled = new UnitAction(UnitActionType.Sleep, "Nap", true, null, new UnitActionConfirmation("Sure?", "Yes"), run);
        Assert.Equal("Nap", titled.Title);
        Assert.Equal("Sure?", titled.Confirmation!.Question);
        Assert.Equal("Yes", titled.Confirmation.ConfirmText);

        Assert.True(plain.Equals(plain));
        Assert.True(plain.Equals(new UnitAction(UnitActionType.Sleep, "other title")));
        Assert.Equal(plain.GetHashCode(), new UnitAction(UnitActionType.Sleep).GetHashCode());
        Assert.False(plain.Equals(new UnitAction(UnitActionType.Fortify)));
        Assert.False(plain.Equals(new UnitAction(UnitActionType.Sleep, isCurrentAction: true)));
        Assert.False(plain.Equals(null));
        Assert.False(plain.Equals("Sleep"));
        Assert.True(new UnitAction(UnitActionType.Sleep, action: run).Equals(new UnitAction(UnitActionType.Sleep, action: run)));
        Assert.False(new UnitAction(UnitActionType.Sleep, action: run).Equals(new UnitAction(UnitActionType.Sleep, action: () => { })));
        Assert.NotEqual(plain.GetHashCode(), new UnitAction(UnitActionType.Sleep, action: run).GetHashCode());
    }

    // ---- UnitActionModifiers

    [Fact]
    public void Modifiers_LimitedTimes_CountsUsagesPerUniqueTextWithoutConditionals()
    {
        var w = NewWorld();
        var unit = w.Unit("Prophet", 2, 2, null, $"{Action} <[2] times>");
        var unique = unit.GetMatchingUniques(UniqueType.MayFoundReligion).Single();

        Assert.True(UnitActionModifiers.CanUse(unit, unique));
        Assert.Equal($"(2/2, 1{Fonts.Movement})", UnitActionModifiers.GetSideEffectString(unit, unique));

        UnitActionModifiers.ActivateSideEffects(unit, unique);
        Assert.Equal(1, unit.AbilityToTimesUsed[Action]);
        Assert.Equal(1, unit.CurrentMovement); // 2 movement - 1
        Assert.True(UnitActionModifiers.CanUse(unit, unique));
        Assert.Equal($"(1/2, 1{Fonts.Movement})", UnitActionModifiers.GetSideEffectString(unit, unique));

        UnitActionModifiers.ActivateSideEffects(unit, unique);
        Assert.Equal(2, unit.AbilityToTimesUsed[Action]);
        Assert.False(UnitActionModifiers.CanUse(unit, unique));
        Assert.Empty(UnitActionModifiers.GetUsableUnitActionUniques(unit, UniqueType.MayFoundReligion));
        Assert.False(UnitActionModifiers.CanActivateSideEffects(unit, unique));
    }

    [Fact]
    public void Modifiers_OnceWithExtraTimes_AddsTheExtraUniquesAndHidesThemFromTheUsableList()
    {
        var w = NewWorld();
        var unit = w.Unit("Prophet", 2, 2, null, $"{Action} <once>", $"{Action} <[2] additional time(s)>");
        var usable = UnitActionModifiers.GetUsableUnitActionUniques(unit, UniqueType.MayFoundReligion).ToList();
        var once = Assert.Single(usable);
        Assert.True(once.HasModifier(UniqueType.UnitActionOnce));
        Assert.StartsWith("(3/3", UnitActionModifiers.GetSideEffectString(unit, once), StringComparison.Ordinal);

        for (var i = 0; i < 3; i++)
        {
            Assert.True(UnitActionModifiers.CanUse(unit, once));
            unit.CurrentMovement = 2;
            UnitActionModifiers.ActivateSideEffects(unit, once);
        }

        Assert.False(UnitActionModifiers.CanUse(unit, once));
    }

    [Fact]
    public void Modifiers_AfterWhichConsumed_ConsumesTheUnitOnTheLastUsage()
    {
        var w = NewWorld();
        var unit = w.Unit("Prophet", 2, 2, null, $"{Action} <[2] times> <after which this unit is consumed>");
        var unique = unit.GetMatchingUniques(UniqueType.MayFoundReligion).Single();

        UnitActionModifiers.ActivateSideEffects(unit, unique);
        Assert.False(unit.IsDestroyed);
        Assert.StartsWith("(1/2, " + Fonts.Death, UnitActionModifiers.GetSideEffectString(unit, unique), StringComparison.Ordinal);

        UnitActionModifiers.ActivateSideEffects(unit, unique);
        Assert.True(unit.IsDestroyed);
        Assert.Equal(1, unit.AbilityToTimesUsed[Action]); // the consuming branch does not count the usage
    }

    [Fact]
    public void Modifiers_ConsumeUnit_ShowsTheDeathSymbolAndConsumes()
    {
        var w = NewWorld();
        var unit = w.Unit("Prophet", 2, 2, null, $"{Action} <by consuming this unit>");
        var unique = unit.GetMatchingUniques(UniqueType.MayFoundReligion).Single();
        Assert.Equal($"({Fonts.Death})", UnitActionModifiers.GetSideEffectString(unit, unique));
        Assert.Equal($"{{Found}} ({Fonts.Death})", UnitActionModifiers.ActionTextWithSideEffects("Found", unique, unit));

        UnitActionModifiers.ActivateSideEffects(unit, unique);
        Assert.True(unit.IsDestroyed);
    }

    [Fact]
    public void Modifiers_MovementCost_UsesTheLargestCostAndRequiresTheSmallestRequired()
    {
        var w = NewWorld();
        var plain = w.Unit("Plain", 0, 0, null, Action);
        var costly = w.Unit("Costly", 0, 1, null, $"{Action} <for [1] movement> <requires [2] movement>");
        var all = w.Unit("All", 0, 2, null, $"{Action} <for all movement>");
        var plainUnique = plain.GetMatchingUniques(UniqueType.MayFoundReligion).Single();
        var costlyUnique = costly.GetMatchingUniques(UniqueType.MayFoundReligion).Single();
        var allUnique = all.GetMatchingUniques(UniqueType.MayFoundReligion).Single();

        Assert.Equal($"(1{Fonts.Movement})", UnitActionModifiers.GetSideEffectString(plain, plainUnique));
        Assert.Equal($"(2{Fonts.Movement})", UnitActionModifiers.GetSideEffectString(plain, plainUnique, defaultAllMovement: true));
        Assert.Equal($"(2{Fonts.Movement})", UnitActionModifiers.GetSideEffectString(all, allUnique));

        costly.CurrentMovement = 1.5f; // ceil(1.5) = 2 >= required 2
        Assert.True(UnitActionModifiers.CanActivateSideEffects(costly, costlyUnique));
        costly.CurrentMovement = 1f;
        Assert.False(UnitActionModifiers.CanActivateSideEffects(costly, costlyUnique));

        UnitActionModifiers.ActivateSideEffects(all, allUnique);
        Assert.Equal(0, all.CurrentMovement);
        Assert.False(UnitActionModifiers.CanActivateSideEffects(all, allUnique)); // "for all movement" still requires 1
    }

    [Fact]
    public void Modifiers_StatsCost_WithoutACityOnlyCivWideStatsCanBePaid()
    {
        var w = NewWorld();
        var gold = w.Unit("Gold", 0, 0, null, $"{Action} <costs [+10 Gold] stats>");
        var production = w.Unit("Production", 0, 1, null, $"{Action} <costs [+10 Production] stats>");
        var goldUnique = gold.GetMatchingUniques(UniqueType.MayFoundReligion).Single();
        var productionUnique = production.GetMatchingUniques(UniqueType.MayFoundReligion).Single();

        w.Civ.AddGold(5 - w.Civ.Gold);
        Assert.False(UnitActionModifiers.CanActivateSideEffects(gold, goldUnique));
        w.Civ.AddGold(25 - w.Civ.Gold);
        Assert.True(UnitActionModifiers.CanActivateSideEffects(gold, goldUnique));
        Assert.False(UnitActionModifiers.CanActivateSideEffects(production, productionUnique)); // no city to spend a city-wide stat

        Assert.Equal($"(10 {Fonts.Gold}, 1{Fonts.Movement})", UnitActionModifiers.GetSideEffectString(gold, goldUnique));
        UnitActionModifiers.ActivateSideEffects(gold, goldUnique);
        Assert.Equal(15, w.Civ.Gold);
        UnitActionModifiers.ActivateSideEffects(production, productionUnique); // no city: nothing to charge, and no crash
        Assert.Equal(15, w.Civ.Gold);
    }

    [Fact]
    public void Modifiers_StockpileCost_NeedsTheResourceAmountAndIsListedInTheSideEffects()
    {
        var w = NewWorld();
        var unit = w.Unit("Stock", 0, 0, null, $"{Action} <costs [3] [Oil]>");
        var unique = unit.GetMatchingUniques(UniqueType.MayFoundReligion).Single();
        Assert.False(UnitActionModifiers.CanActivateSideEffects(unit, unique));
        Assert.Equal($"(3 Oil, 1{Fonts.Movement})", UnitActionModifiers.GetSideEffectString(unit, unique));
        UnitActionModifiers.ActivateSideEffects(unit, unique); // unknown resource: ignored
        Assert.Equal(1, unit.CurrentMovement);
    }

    [Fact]
    public void Modifiers_RemovingPromotion_RemovesAStatusFirstElseThePromotion()
    {
        var w = NewWorld();
        var unit = w.Unit("Cleric", 0, 0, null, $"{Action} <removing the [Blessed] promotion/status>");
        var unique = unit.GetMatchingUniques(UniqueType.MayFoundReligion).Single();

        unit.SetStatus("Blessed", 3);
        unit.GivePromotion(new Promotion { Name = "Blessed" });
        UnitActionModifiers.ActivateSideEffects(unit, unique);
        Assert.False(unit.HasStatus("Blessed"));
        Assert.Contains("Blessed", unit.Promotions.Promotions); // the status was removed instead

        unit.CurrentMovement = 2;
        UnitActionModifiers.ActivateSideEffects(unit, unique);
        Assert.DoesNotContain("Blessed", unit.Promotions.Promotions);

        FontRulesetIcons.RulesetObjectNameToChar["Blessed"] = 'B';
        try
        {
            Assert.Equal($"(1{Fonts.Movement}, -B)", UnitActionModifiers.GetSideEffectString(unit, unique));
        }
        finally
        {
            FontRulesetIcons.RulesetObjectNameToChar.Remove("Blessed");
        }
    }

    [Fact]
    public void Modifiers_GetUseFrequency_ReadsTheFirstApplicablePriorityModifier()
    {
        var w = NewWorld();
        var unit = w.Unit("Prophet", 0, 0, null, $"{Action} <with [5] priority>", Action);
        var uniques = unit.GetMatchingUniques(UniqueType.MayFoundReligion).ToList();
        var prioritized = uniques.Single(it => it.Modifiers.Count > 0);
        var plain = uniques.Single(it => it.Modifiers.Count == 0);

        Assert.Equal(5f, UnitActionModifiers.GetUseFrequency(unit, prioritized, 1f));
        Assert.Equal(1.5f, UnitActionModifiers.GetUseFrequency(unit, plain, 1.5f));
        Assert.Equal(2f, UnitActionModifiers.GetUseFrequency(unit, null, 2f));
    }

    // ---- UnitActionsPillage

    private static TileClass FarmTile(World w, params string[] farmUniques)
    {
        w.Ruleset.TileImprovements.Put("Farm", new TileImprovement { Name = "Farm", Uniques = farmUniques.ToList() });
        var tile = w.TileAt(2, 2);
        tile.Improvement = "Farm";
        tile.SetTerrainTransients();
        return tile;
    }

    [Fact]
    public void Pillage_CanPillageHonoursTransportCannotPillageAndWar()
    {
        var w = NewWorld();
        var tile = FarmTile(w);
        var unit = w.Unit("Warrior", 2, 2);
        Assert.True(UnitActionsPillage.CanPillage(unit, tile));
        Assert.False(UnitActionsPillage.CanPillage(unit, w.TileAt(3, 3))); // nothing to pillage

        unit.IsTransported = true;
        Assert.False(UnitActionsPillage.CanPillage(unit, tile));
        unit.IsTransported = false;

        var pacifist = w.Unit("Pacifist", 2, 2, null, "Unable to pillage tiles");
        Assert.False(UnitActionsPillage.CanPillage(pacifist, tile));
    }

    [Fact]
    public void Pillage_NoActionForCiviliansNothingToPillageOrOwnTiles()
    {
        var w = NewWorld();
        var tile = FarmTile(w);
        var worker = w.Unit("Worker", 2, 2);
        worker.BaseUnit.Strength = 0;
        Assert.True(worker.IsCivilian());
        Assert.Empty(UnitActionsPillage.GetPillageActions(worker, tile));

        var warrior = w.Unit("Warrior", 3, 3);
        Assert.Empty(UnitActionsPillage.GetPillageActions(warrior, w.TileAt(3, 3))); // its own tile has nothing to pillage
    }

    [Fact]
    public void Pillage_ActionHasTitleConfirmationAndIsDisabledWithoutMovement()
    {
        var w = NewWorld();
        var tile = FarmTile(w);
        var unit = w.Unit("Warrior", 2, 2);
        var action = Assert.Single(UnitActionsPillage.GetPillageActions(unit, tile));
        Assert.Equal(UnitActionType.Pillage, action.Type);
        Assert.Equal("Pillage [Farm]", action.Title);
        Assert.Equal("Are you sure you want to pillage this [Farm]?", action.Confirmation!.Question);
        Assert.Equal("Pillage", action.Confirmation.ConfirmText);
        Assert.NotNull(action.Action);

        unit.CurrentMovement = 0;
        Assert.Null(Assert.Single(UnitActionsPillage.GetPillageActions(unit, tile)).Action);
    }

    [Fact]
    public void Pillage_Execution_DestroysTheImprovementWithoutARepairActionPaysMovementAndLoots()
    {
        var w = NewWorld();
        var tile = FarmTile(w, "Pillaging this improvement yields [+10 Gold]", "Pillaging this improvement yields [+4 Gold] <(modified by game speed)>");
        w.Info.SpeedData.Modifier = 2f;
        var unit = w.Unit("Warrior", 2, 2);
        unit.Health = 50;
        w.Civ.AddGold(0 - w.Civ.Gold);

        UnitActionsPillage.GetPillageAction(unit, tile)!.Action!();

        Assert.Equal(18, w.Civ.Gold); // 10 + 4 * game speed modifier 2
        Assert.Null(tile.Improvement); // no "Repair" improvement in the ruleset: destroyed outright
        Assert.Equal(1, unit.CurrentMovement);
        Assert.Equal(75, unit.Health); // improvements heal 25
    }

    [Fact]
    public void Pillage_Execution_AppliesTheYieldAndHealthUniquesAndFreePillaging()
    {
        var w = NewWorld(ruleset => ruleset.TileImprovements.Put("Repair", new TileImprovement { Name = "Repair" }));
        var tile = FarmTile(w, "Pillaging this improvement yields [+10 Gold]", "Destroyed when pillaged");
        var unit = w.Unit("Raider", 2, 2, null, "[200]% Yield from pillaging tiles", "[100]% Health from pillaging tiles", "No movement cost to pillage");
        unit.Health = 10;
        w.Civ.AddGold(0 - w.Civ.Gold);

        UnitActionsPillage.GetPillageAction(unit, tile)!.Action!();

        Assert.Equal(30, w.Civ.Gold); // 10 * (1 + 200/100)
        Assert.Equal(60, unit.Health); // 25 * (1 + 100/100) = 50
        Assert.Equal(2, unit.CurrentMovement); // free pillage
        // Kotlin quirk kept: DestroyedWhenPillaged is looked up with getImprovementToPillage() AFTER setPillaged() marked it pillaged, so it finds nothing
        Assert.Equal("Farm", tile.Improvement);
        Assert.True(tile.ImprovementIsPillaged);
    }

    [Fact]
    public void Pillage_Execution_WithARepairActionKeepsTheImprovementPillaged()
    {
        var w = NewWorld(ruleset => ruleset.TileImprovements.Put("Repair", new TileImprovement { Name = "Repair" }));
        var tile = FarmTile(w);
        var unit = w.Unit("Warrior", 2, 2);

        UnitActionsPillage.GetPillageAction(unit, tile)!.Action!();

        Assert.Equal("Farm", tile.Improvement);
        Assert.True(tile.ImprovementIsPillaged);
        Assert.False(UnitActionsPillage.CanPillage(unit, tile));
    }

    [Fact]
    public void Pillage_RandomLootIsTwoRollsUpToTheStatAndDeterministicPerTurnAndTile()
    {
        var results = new List<int>();
        for (var run = 0; run < 2; run++)
        {
            var w = NewWorld();
            var tile = FarmTile(w, "Pillaging this improvement yields approximately [+6 Gold]");
            var unit = w.Unit("Warrior", 2, 2);
            w.Civ.AddGold(0 - w.Civ.Gold);
            UnitActionsPillage.GetPillageAction(unit, tile)!.Action!();
            results.Add(w.Civ.Gold);
        }

        Assert.Equal(results[0], results[1]);
        Assert.InRange(results[0], 0, 12);
    }

    // ---- UnitActionsUpgrade

    [Fact]
    public void Upgrade_Title_ListsTheGoldCostAndNewResourceRequirements()
    {
        var w = NewWorld();
        var target = MapUnitFactory.NewBaseUnit(w.Ruleset, "Swordsman");
        var plain = new PossibleUpgrade(target, null, false, UpgradeAvailability.Available, 60, new Counter<string>());
        Assert.Equal("Upgrade to [Swordsman] ([60] gold)", UnitActionsUpgrade.GetUpgradeTitle(plain));

        var resources = new Counter<string>();
        resources.Add("Iron", 1);
        resources.Add("Horses", 2);
        var costly = new PossibleUpgrade(target, null, false, UpgradeAvailability.AvailableExceptResources, 75, resources);
        Assert.Equal("Upgrade to [Swordsman]\n([75] gold, [1 Iron, 2 Horses])", UnitActionsUpgrade.GetUpgradeTitle(costly));

        var action = new UpgradeUnitAction("title", costly, null);
        Assert.Equal(UnitActionType.Upgrade, action.Type);
        Assert.Same(target, action.UnitToUpgradeTo);
        Assert.Same(costly, action.Upgrade);
        Assert.Null(action.AssociatedUnique);
    }

    [Fact]
    public void Upgrade_Actions_AreOnlyOfferedOnOwnTerritory()
    {
        var w = NewWorld();
        var unit = w.Unit("Warrior", 2, 2);
        Assert.Empty(UnitActionsUpgrade.GetUpgradeActions(unit)); // unowned tile
    }

    // ---- UnitActionsGreatPerson / UnitActionsReligion

    [Fact]
    public void GreatPerson_HurryPolicyAndResearchActionsAreOfferedPerUnique()
    {
        var w = NewWorld();
        var unit = w.Unit("Great Writer", 2, 2, null, "Can generate a large amount of culture", "Can hurry technology research");
        var tile = w.TileAt(2, 2);

        var research = Assert.Single(UnitActionsGreatPerson.GetHurryResearchActions(unit, tile));
        Assert.Equal(UnitActionType.HurryResearch, research.Type);
        Assert.NotNull(research.AssociatedUnique);
        Assert.Null(research.Action); // no current technology

        var policy = Assert.Single(UnitActionsGreatPerson.GetHurryPolicyActions(unit, tile));
        Assert.Equal(UnitActionType.HurryPolicy, policy.Type);
        Assert.NotNull(policy.Action);
        unit.CurrentMovement = 0;
        Assert.Null(Assert.Single(UnitActionsGreatPerson.GetHurryPolicyActions(unit, tile)).Action);
    }

    [Fact]
    public void GreatPerson_CityBoundActionsAreDisabledOutsideCityCenters()
    {
        var w = NewWorld();
        var unit = w.Unit("Great Engineer", 2, 2, null, "Can speed up the construction of a wonder", "Can speed up construction of a building",
            "Can undertake a trade mission with City-State, giving a large sum of gold and [30] Influence");
        var tile = w.TileAt(2, 2);

        var wonder = Assert.Single(UnitActionsGreatPerson.GetHurryWonderActions(unit, tile));
        Assert.Equal(UnitActionType.HurryWonder, wonder.Type);
        Assert.Null(wonder.Action);
        var building = Assert.Single(UnitActionsGreatPerson.GetHurryBuildingActions(unit, tile));
        Assert.Equal(UnitActionType.HurryBuilding, building.Type);
        Assert.Null(building.Action);
        var trade = Assert.Single(UnitActionsGreatPerson.GetConductTradeMissionActions(unit, tile));
        Assert.Equal(UnitActionType.ConductTradeMission, trade.Type);
        Assert.Null(trade.Action);
    }

    [Fact]
    public void Religion_NoActionsWithoutReligionOrACity()
    {
        var w = NewWorld();
        var unit = w.Unit("Prophet", 2, 2, null, $"{Action} <by consuming this unit>");
        var tile = w.TileAt(2, 2);
        Assert.Empty(UnitActionsReligion.GetFoundReligionActions(unit, tile));
        Assert.Empty(UnitActionsReligion.GetEnhanceReligionActions(unit, tile));
        Assert.Empty(UnitActionsReligion.GetSpreadReligionActions(unit, tile));
        Assert.Empty(UnitActionsReligion.GetRemoveHeresyActions(unit, tile));
    }
}

// The same actions over the real TestGame (cities, constructions, religion, city states): Kotlin has no direct fixtures for them (UpgradeTests: T072).
[Collection("RulesetCache")]
public sealed class UnitActionsATestsOnTestGame
{
    private readonly TestGame testGame = new();
    private readonly Civilization civ;
    private readonly CityObject capital;

    public UnitActionsATestsOnTestGame()
    {
        testGame.MakeHexagonalMap(5);
        civ = testGame.AddCiv();
        capital = testGame.AddCity(civ, testGame.GetTile(HexCoord.Zero));
    }

    private MapUnitClass Unit(TileClass tile, string baseUnitName) => testGame.AddUnit(baseUnitName, civ, tile);

    /// <summary>Makes <paramref name="tile"/> part of <paramref name="city"/>'s territory after a unit already stands on it.</summary>
    private void Claim(CityObject city, TileClass tile)
    {
        testGame.AddTileToCity(city, tile);
        tile.SetOwningCity(city);
    }

    private MapUnitClass UnitWithUniques(TileClass tile, params string[] uniques) =>
        Unit(tile, testGame.CreateBaseUnit(null, uniques).Name);

    // ---- Upgrade

    [Fact]
    public void Upgrade_ActionsListTheOwnedTileUpgradeAndPerformIt()
    {
        var target = testGame.CreateBaseUnit();
        target.Cost = 100;
        var source = testGame.CreateBaseUnit();
        source.Cost = 50;
        source.UpgradesTo = target.Name;
        var unit = Unit(capital.GetCenterTile(), source.Name);

        civ.AddGold(-civ.Gold);
        var poor = Assert.IsType<UpgradeUnitAction>(Assert.Single(UnitActionsUpgrade.GetUpgradeActions(unit)));
        Assert.Null(poor.Action); // shown, but we lack the gold
        Assert.Equal(UnitActionType.Upgrade, poor.Type);
        Assert.Same(target, poor.UnitToUpgradeTo);
        Assert.Equal(UnitActionsUpgrade.GetUpgradeTitle(poor.Upgrade), poor.Title);
        Assert.StartsWith($"Upgrade to [{target.Name}] ([", poor.Title, StringComparison.Ordinal);

        civ.AddGold(100000);
        var rich = Assert.IsType<UpgradeUnitAction>(Assert.Single(UnitActionsUpgrade.GetUpgradeActions(unit)));
        Assert.NotNull(rich.Action);
        rich.Action();
        Assert.Equal(target.Name, civ.Units.GetCivUnits().Single().Name);
    }

    [Fact]
    public void Upgrade_ActionsAreNotOfferedOutsideOwnTerritory()
    {
        var target = testGame.CreateBaseUnit();
        var source = testGame.CreateBaseUnit();
        source.UpgradesTo = target.Name;
        var unit = Unit(testGame.GetTile(4, 0), source.Name);
        Assert.Empty(UnitActionsUpgrade.GetUpgradeActions(unit));
    }

    // ---- Great person

    [Fact]
    public void GreatPerson_HurryResearchAddsScienceAndConsumesUnlessTheTechCannotBeHurried()
    {
        var unit = UnitWithUniques(testGame.GetTile(1, 0), "Can hurry technology research");
        var tile = unit.GetTile();
        var tech = testGame.Ruleset.Technologies.Values.Skip(3).First();
        civ.Tech.TechsToResearch.Add(tech.Name);
        int before = civ.Tech.ResearchOfTech(tech.Name);
        civ.Tech.ScienceOfLast8Turns[0] = 40;
        int science = civ.Tech.GetScienceFromGreatScientist();
        Assert.True(science > 0);

        var action = Assert.Single(UnitActionsGreatPerson.GetHurryResearchActions(unit, tile));
        Assert.NotNull(action.Action);
        action.Action();
        Assert.True(unit.IsDestroyed);
        Assert.True(civ.Tech.CostOfTech(tech.Name) <= science + before);
        Assert.True(civ.Tech.IsResearched(tech.Name));

        var second = UnitWithUniques(testGame.GetTile(1, 0), "Can hurry technology research");
        var tech2 = new Technology { Name = "Unhurriable", Uniques = ["Cannot be hurried"] };
        testGame.Ruleset.Technologies.Put(tech2.Name, tech2);
        civ.Tech.TechsToResearch.Clear();
        civ.Tech.TechsToResearch.Add(tech2.Name);
        Assert.Null(Assert.Single(UnitActionsGreatPerson.GetHurryResearchActions(second, tile)).Action);
    }

    [Fact]
    public void GreatPerson_HurryPolicyAddsCultureAndConsumes()
    {
        var unit = UnitWithUniques(testGame.GetTile(1, 0), "Can generate a large amount of culture");
        int before = civ.Policies.StoredCulture;
        var action = Assert.Single(UnitActionsGreatPerson.GetHurryPolicyActions(unit, unit.GetTile()));
        action.Action!();
        Assert.True(unit.IsDestroyed);
        Assert.Equal(before + civ.Policies.GetCultureFromGreatWriter(), civ.Policies.StoredCulture);
    }

    [Fact]
    public void GreatPerson_HurryWonderAddsProductionToTheWonderInTheCityCenter()
    {
        var wonder = testGame.CreateWonder();
        wonder.Cost = 5000;
        capital.CityConstructions.SetCurrentConstruction(wonder.Name);
        var unit = UnitWithUniques(capital.GetCenterTile(), "Can speed up the construction of a wonder");

        var action = Assert.Single(UnitActionsGreatPerson.GetHurryWonderActions(unit, capital.GetCenterTile()));
        Assert.NotNull(action.Action);
        action.Action();
        Assert.True(unit.IsDestroyed);
        int expected = KotlinMath.FloatToInt((300 + 30 * capital.Population.Population) * testGame.GameInfo.Speed.ProductionCostModifier);
        Assert.Equal(expected, capital.CityConstructions.InProgressConstructions[wonder.Name]);
    }

    [Fact]
    public void GreatPerson_HurryWonderIsDisabledForBuildingsAndOutsideTheCityCenter()
    {
        var building = testGame.CreateBuilding();
        building.Cost = 500;
        capital.CityConstructions.SetCurrentConstruction(building.Name);
        var inCity = UnitWithUniques(capital.GetCenterTile(), "Can speed up the construction of a wonder");
        Assert.Null(Assert.Single(UnitActionsGreatPerson.GetHurryWonderActions(inCity, capital.GetCenterTile())).Action);
    }

    [Fact]
    public void GreatPerson_HurryBuildingAddsProductionAndTitlesTheAmount()
    {
        var building = testGame.CreateBuilding();
        building.Cost = 100;
        capital.CityConstructions.SetCurrentConstruction(building.Name);
        var unit = UnitWithUniques(capital.GetCenterTile(), "Can speed up construction of a building");

        var action = Assert.Single(UnitActionsGreatPerson.GetHurryBuildingActions(unit, capital.GetCenterTile()));
        int remaining = capital.CityConstructions.GetRemainingWork(building.Name);
        int expected = KotlinMath.FloatToInt(MathF.Min((300 + 30 * capital.Population.Population) * testGame.GameInfo.Speed.ProductionCostModifier, remaining - 1f));
        Assert.Equal($"Hurry Construction (+[{expected}]⚙)", action.Title);
        Assert.NotNull(action.Action);
        action.Action();
        Assert.True(unit.IsDestroyed);
        Assert.Equal(expected, capital.CityConstructions.InProgressConstructions[building.Name]);
    }

    [Fact]
    public void GreatPerson_HurryBuildingHasNoActionWhenNothingIsLeftToAddAndIsDisabledOutsideCities()
    {
        var building = testGame.CreateBuilding();
        building.Cost = 1;
        capital.CityConstructions.SetCurrentConstruction(building.Name);
        var unit = UnitWithUniques(capital.GetCenterTile(), "Can speed up construction of a building");
        Assert.Empty(UnitActionsGreatPerson.GetHurryBuildingActions(unit, capital.GetCenterTile()));

        var outside = UnitWithUniques(testGame.GetTile(1, 0), "Can speed up construction of a building");
        var disabled = Assert.Single(UnitActionsGreatPerson.GetHurryBuildingActions(outside, outside.GetTile()));
        Assert.Null(disabled.Action);
        Assert.Equal(UnitActionType.HurryBuilding.Value(), disabled.Title);
    }

    [Fact]
    public void GreatPerson_TradeMissionPaysGoldAndInfluenceInAForeignCityState()
    {
        var cityState = testGame.AddCiv(cityStateType: "Cultured");
        var csCity = testGame.AddCity(cityState, testGame.GetTile(3, 0));
        civ.DiplomacyFunctions.MakeCivilizationsMeet(cityState);
        var unit = UnitWithUniques(testGame.GetTile(-3, 0), "Can undertake a trade mission with City-State, giving a large sum of gold and [30] Influence",
            "[50]% Gold from Great Merchant trade missions");
        Claim(csCity, unit.GetTile());
        int goldBefore = civ.Gold;
        float influenceBefore = cityState.GetDiplomacyManager(civ)!.Influence;

        var action = Assert.Single(UnitActionsGreatPerson.GetConductTradeMissionActions(unit, unit.GetTile()));
        Assert.NotNull(action.Action);
        action.Action();

        Assert.True(unit.IsDestroyed);
        float gold = (350 + 50 * civ.GetEraNumber()) * testGame.GameInfo.Speed.GoldCostModifier * 1.5f;
        Assert.Equal(goldBefore + KotlinMath.FloatToInt(gold), civ.Gold);
        Assert.Equal(influenceBefore + 30f, cityState.GetDiplomacyManager(civ)!.Influence);
    }

    [Fact]
    public void GreatPerson_TradeMissionIsDisabledInOwnCitiesAndMajorCivCities()
    {
        var unit = UnitWithUniques(capital.GetCenterTile(), "Can undertake a trade mission with City-State, giving a large sum of gold and [30] Influence");
        Assert.Null(Assert.Single(UnitActionsGreatPerson.GetConductTradeMissionActions(unit, capital.GetCenterTile())).Action);

        var other = testGame.AddCiv();
        var otherCity = testGame.AddCity(other, testGame.GetTile(3, 0));
        var visitor = UnitWithUniques(testGame.GetTile(-3, 0), "Can undertake a trade mission with City-State, giving a large sum of gold and [30] Influence");
        Claim(otherCity, visitor.GetTile());
        Assert.Null(Assert.Single(UnitActionsGreatPerson.GetConductTradeMissionActions(visitor, visitor.GetTile())).Action);
    }

    // ---- Religion

    private static void SetReligionState(Civilization target, ReligionState state) =>
        typeof(ReligionManager).GetProperty(nameof(ReligionManager.ReligionState))!.SetValue(target.ReligionManager, state);

    [Fact]
    public void Religion_FoundReligionStartsFoundingInTheCityAndConsumesTheProphet()
    {
        var plain = UnitWithUniques(capital.GetCenterTile(), "May found a religion");
        var action = Assert.Single(UnitActionsReligion.GetFoundReligionActions(plain, capital.GetCenterTile()));
        Assert.Equal(UnitActionType.FoundReligion, action.Type);
        Assert.Equal("Found a Religion", action.Title);
        action.Action!();
        Assert.Equal(ReligionState.FoundingReligion, civ.ReligionManager.ReligionState);
        Assert.True(plain.IsDestroyed);
    }

    [Fact]
    public void Religion_FoundReligionWithActionModifiersUsesTheSideEffects()
    {
        var unit = UnitWithUniques(capital.GetCenterTile(), "May found a religion <[2] times>");
        var action = Assert.Single(UnitActionsReligion.GetFoundReligionActions(unit, capital.GetCenterTile()));
        Assert.Equal($"{{Found a Religion}} (2/2, 1{Fonts.Movement})", action.Title);
        action.Action!();
        Assert.False(unit.IsDestroyed);
        Assert.Equal(1, unit.AbilityToTimesUsed["May found a religion"]);

        var outside = UnitWithUniques(testGame.GetTile(1, 0), "May found a religion");
        Assert.Null(Assert.Single(UnitActionsReligion.GetFoundReligionActions(outside, outside.GetTile())).Action);
    }

    [Fact]
    public void Religion_EnhanceReligionNeedsAFoundedReligionAndEnhancesInTheCityCenter()
    {
        var unit = UnitWithUniques(capital.GetCenterTile(), "May enhance a religion");
        Assert.Empty(UnitActionsReligion.GetEnhanceReligionActions(unit, capital.GetCenterTile())); // no religion yet

        var religion = testGame.AddReligion(civ);
        religion.DisplayName = "Pastafarianism";
        for (var i = 0; i < 2; i++)
        {
            testGame.CreateBelief(BeliefType.Follower);
            testGame.CreateBelief(BeliefType.Enhancer);
        }
        SetReligionState(civ, ReligionState.Religion);

        var action = Assert.Single(UnitActionsReligion.GetEnhanceReligionActions(unit, capital.GetCenterTile()));
        Assert.Equal("Enhance [Pastafarianism]", action.Title);
        action.Action!();
        Assert.Equal(ReligionState.EnhancingReligion, civ.ReligionManager.ReligionState);
        Assert.True(unit.IsDestroyed);

        SetReligionState(civ, ReligionState.Religion);
        var modified = UnitWithUniques(capital.GetCenterTile(), "May enhance a religion <by consuming this unit>");
        var modifiedAction = Assert.Single(UnitActionsReligion.GetEnhanceReligionActions(modified, capital.GetCenterTile()));
        Assert.Equal($"{{Enhance [Pastafarianism]}} ({Fonts.Death})", modifiedAction.Title);
        modifiedAction.Action!();
        Assert.True(modified.IsDestroyed);
    }

    private MapUnitClass Missionary(TileClass tile, string religionName, params string[] extraUniques)
    {
        var baseUnit = testGame.CreateBaseUnit(null, ["Can Spread Religion", .. extraUniques]);
        baseUnit.ReligiousStrength = 100;
        var unit = Unit(tile, baseUnit.Name);
        unit.Religion = religionName;
        return unit;
    }

    [Fact]
    public void Religion_SpreadReligionAddsPressureAndNotifiesTheOwner()
    {
        var religion = testGame.AddReligion(civ);
        var foreign = testGame.AddCiv(isPlayer: true);
        var foreignCity = testGame.AddCity(foreign, testGame.GetTile(3, 0));
        var unit = Missionary(testGame.GetTile(-3, 0), religion.Name, "Removes other religions when spreading religion",
            "[50]% Spread Religion Strength");
        Claim(foreignCity, unit.GetTile());
        var other = testGame.AddReligion(foreign).Name;
        foreignCity.Religion.AddPressure(other, 300);

        var action = Assert.Single(UnitActionsReligion.GetSpreadReligionActions(unit, unit.GetTile()));
        Assert.Equal(UnitActionType.SpreadReligion, action.Type);
        Assert.Equal($"{{Spread [{religion.GetReligionDisplayName()}]}} (1{Fonts.Movement})", action.Title);
        Assert.NotNull(action.Action);
        action.Action();

        var pressures = foreignCity.Religion.GetPressures();
        Assert.Equal(150, pressures[religion.Name]); // 100 * 1.5
        Assert.False(pressures.ContainsKey(other)); // removed by the unique
        Assert.Contains(foreign.Notifications, it => it.Text.Contains("has converted", StringComparison.Ordinal));
    }

    [Fact]
    public void Religion_SpreadReligionHasNoActionWithoutACityOrAReligion()
    {
        var religion = testGame.AddReligion(civ);
        var noCity = Missionary(testGame.GetTile(-3, 0), religion.Name);
        Assert.Empty(UnitActionsReligion.GetSpreadReligionActions(noCity, noCity.GetTile()));
        var noReligion = Missionary(capital.GetCenterTile(), null!);
        noReligion.Religion = null;
        Assert.Empty(UnitActionsReligion.GetSpreadReligionActions(noReligion, capital.GetCenterTile()));
    }

    [Fact]
    public void Religion_RemoveHeresyClearsForeignPressuresAndFlipsTheBlockedHolyCity()
    {
        var religion = testGame.AddReligion(civ);
        var rival = testGame.AddCiv(isPlayer: true);
        var rivalReligion = testGame.AddReligion(rival);
        capital.Religion.AddPressure(rivalReligion.Name, 200);
        capital.Religion.ReligionThisIsTheHolyCityOf = rivalReligion.Name;
        var unit = UnitWithUniques(capital.GetCenterTile(), "Can remove other religions from cities");
        unit.Religion = religion.Name;

        var action = Assert.Single(UnitActionsReligion.GetRemoveHeresyActions(unit, capital.GetCenterTile()));
        Assert.Equal($"{{Remove Heresy}} (1{Fonts.Movement})", action.Title);
        action.Action!();

        Assert.False(capital.Religion.GetPressures().ContainsKey(rivalReligion.Name));
        Assert.True(capital.Religion.IsBlockedHolyCity);
        Assert.Contains(rival.Notifications, it => it.Text.Contains("has removed your religion", StringComparison.Ordinal));

        // the rightful religion restores its blocked holy city
        capital.Religion.ReligionThisIsTheHolyCityOf = religion.Name;
        capital.Religion.AddPressure(rivalReligion.Name, 50);
        civ.PlayerType = PlayerType.Human;
        civ.Notifications.Clear();
        unit.CurrentMovement = 2;
        UnitActionsReligion.GetRemoveHeresyActions(unit, capital.GetCenterTile()).Single().Action!();
        Assert.False(capital.Religion.IsBlockedHolyCity);
        Assert.Contains(civ.Notifications, it => it.Text.Contains("has restored", StringComparison.Ordinal));
    }

    [Fact]
    public void Religion_RemoveHeresyHasNoActionForForeignCitiesOrUnitsWithoutReligion()
    {
        var religion = testGame.AddReligion(civ);
        var unit = UnitWithUniques(capital.GetCenterTile(), "Can remove other religions from cities");
        unit.Religion = religion.Name;
        // founding the city left some "no religion" pressure, which counts as foreign pressure for the check (Atheism is never removed)
        Assert.Contains(Constants.NoReligionName, capital.Religion.GetPressures().Keys);
        Assert.Single(UnitActionsReligion.GetRemoveHeresyActions(unit, capital.GetCenterTile()));

        var foreign = testGame.AddCiv();
        var foreignCity = testGame.AddCity(foreign, testGame.GetTile(3, 0));
        foreignCity.Religion.AddPressure(testGame.AddReligion(foreign).Name, 100);
        var visitor = UnitWithUniques(testGame.GetTile(-3, 0), "Can remove other religions from cities");
        Claim(foreignCity, visitor.GetTile());
        visitor.Religion = religion.Name;
        Assert.Empty(UnitActionsReligion.GetRemoveHeresyActions(visitor, visitor.GetTile())); // not our city

        var noReligion = UnitWithUniques(testGame.GetTile(0, -1), "Can remove other religions from cities");
        noReligion.Religion = null;
        Assert.Empty(UnitActionsReligion.GetRemoveHeresyActions(noReligion, capital.GetCenterTile()));
        Assert.Empty(UnitActionsReligion.GetRemoveHeresyActions(UnitWithUniques(testGame.GetTile(1, 0), "Can remove other religions from cities"), testGame.GetTile(1, 0)));
    }
}
