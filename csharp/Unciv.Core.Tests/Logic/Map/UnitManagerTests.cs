using Unciv.Json;
using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Managers;
using Unciv.Logic.Map;
using Unciv.Logic.Map.MapUnits;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Ruleset.Unit;
using MapUnitClass = Unciv.Logic.Map.MapUnits.MapUnit;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Core.Tests.Logic.Map;

// T066: UnitPromotions, UnitUpgradeManager, UnitTurnManager and UnitManager over a real Civilization / TileMap. Placing a unit through
// TileMap.placeUnitNearTile needs the real UnitMovement (T067), so addUnit(baseUnit) / placeUnitNearTile are covered by the TestGame-based
// fixtures (T072); the list handling of UnitManager and the bail-out paths are covered here. UpgradeTests.kt is TestGame-based (T072).
public class UnitManagerTests
{
    private sealed record World(Civilization Civ, FakeGameInfoState Info, TileMap Map, Ruleset Ruleset)
    {
        public TileClass TileAt(int x, int y) => Map[HexCoord.Of(x, y)];

        public MapUnitClass Unit(string name = "Warrior", string type = "Land", int x = 2, int y = 2, Civilization? civ = null, params string[] uniques)
        {
            var owner = civ ?? Civ;
            var baseUnit = Ruleset.Units.TryGetValue(name, out var existing) ? existing : MapUnitFactory.NewBaseUnit(Ruleset, name, type, uniques: uniques);
            return MapUnitFactory.NewUnit(owner, baseUnit, TileAt(x, y));
        }
    }

    private static World NewWorld(Action<Ruleset>? configure = null, params string[] civUniques)
    {
        var ruleset = new Ruleset();
        ruleset.Terrains.Put("Grassland", new Terrain { Name = "Grassland", Type = TerrainType.Land });
        ruleset.Terrains.Put("Lava", new Terrain { Name = "Lava", Type = TerrainType.TerrainFeature, DamagePerTurn = 7 });
        ruleset.Terrains.Put("Coast", new Terrain { Name = "Coast", Type = TerrainType.Water });
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
        var civ = MapUnitFactory.NewCiv("Rome", info, civUniques);
        civ.PlayerType = PlayerType.Human; // AI civilizations record no notifications
        civ.Units = new UnitManager(civ);
        return new World(civ, info, map, ruleset);
    }

    private static void AddPromotions(Ruleset ruleset, params Promotion[] promotions)
    {
        foreach (var promotion in promotions) ruleset.UnitPromotions.Put(promotion.Name, promotion);
    }

    // ---- UnitPromotions

    [Fact]
    public void XpForNextPromotion_CostsTenTimesThePromotionNumber_AndUsesTheCivModifier()
    {
        var w = NewWorld();
        var unit = w.Unit();
        Assert.Equal(10, unit.Promotions.XpForNextPromotion());
        unit.Promotions.NumberOfPromotions = 2;
        Assert.Equal(30, unit.Promotions.XpForNextPromotion());
        Assert.Equal(30 + 40 + 50, unit.Promotions.XpForNextNPromotions(3));
        Assert.Equal(0, unit.Promotions.XpForNextNPromotions(0));

        var modified = NewWorld(null, "[50]% XP required for promotions").Unit();
        Assert.Equal(15, modified.Promotions.XpForNextPromotion());
        modified.Promotions.NumberOfPromotions = 2;
        Assert.Equal(45, modified.Promotions.XpForNextPromotion()); // (30 * 1.5f).toInt()
    }

    [Fact]
    public void TotalXpProduced_AddsTheXpAlreadySpent()
    {
        var unit = NewWorld().Unit();
        unit.Promotions.XP = 7;
        unit.Promotions.NumberOfPromotions = 3;
        Assert.Equal(7 + 3 * 4 * 5, unit.Promotions.TotalXpProduced());
    }

    [Fact]
    public void ValueOfPromotionsAndXp_SpendsAllXpOnPromotions()
    {
        var unit = NewWorld().Unit();
        // the example of the Kotlin comment: 2 chosen promotions, 1 free one, 50 XP; the 3rd promotion costs 30, leaving 20 of the next 40
        unit.Promotions.NumberOfPromotions = 2;
        unit.Promotions.XP = 50;
        unit.Promotions.Promotions.Add("A");
        unit.Promotions.Promotions.Add("B");
        unit.Promotions.Promotions.Add("C");
        Assert.Equal(3 + 1 + 20f / 40f, unit.Promotions.ValueOfPromotionsAndXp());
    }

    [Fact]
    public void AddPromotion_PaysXpForNormalPromotionsOnly()
    {
        var w = NewWorld(ruleset => AddPromotions(ruleset,
            new Promotion { Name = "Shock", Uniques = ["[+1] Movement"] },
            new Promotion { Name = "Gift", Uniques = ["This Promotion is free"] }));
        var unit = w.Unit();
        unit.Promotions.XP = 25;
        unit.Promotions.AddPromotion("Unknown");
        Assert.Empty(unit.Promotions.Promotions);

        unit.Promotions.AddPromotion("Shock");
        Assert.Equal((15, 1), (unit.Promotions.XP, unit.Promotions.NumberOfPromotions));
        Assert.Equal(3, unit.GetMaxMovement()); // UpdateUniques picked the promotion up
        unit.Promotions.AddPromotion("Shock"); // already there
        Assert.Equal((15, 1), (unit.Promotions.XP, unit.Promotions.NumberOfPromotions));

        unit.Promotions.AddPromotion("Gift"); // not "isFree", but its own unique says so
        Assert.Equal((15, 1), (unit.Promotions.XP, unit.Promotions.NumberOfPromotions));
        Assert.Contains("Gift", unit.Promotions.Promotions);
    }

    [Fact]
    public void AddPromotion_IsFreeSkipsTheCost_AndSkipPromotionIsNotKept()
    {
        var w = NewWorld(ruleset => AddPromotions(ruleset,
            new Promotion { Name = "Shock" },
            new Promotion { Name = "Once", Uniques = ["Doing so will consume this opportunity to choose a Promotion"] }));
        var unit = w.Unit();
        unit.Promotions.XP = 25;
        unit.Promotions.AddPromotion("Shock", isFree: true);
        Assert.Equal((25, 0), (unit.Promotions.XP, unit.Promotions.NumberOfPromotions));
        unit.Promotions.AddPromotion("Once"); // pays, but is not added
        Assert.Equal((15, 1), (unit.Promotions.XP, unit.Promotions.NumberOfPromotions));
        Assert.Equal(["Shock"], unit.Promotions.Promotions);
    }

    [Fact]
    public void RemovePromotion_RemovesItAndUpdatesTheUniques_OnlyWhenPresent()
    {
        var w = NewWorld(ruleset => AddPromotions(ruleset, new Promotion { Name = "Shock", Uniques = ["[+1] Movement"] }));
        var unit = w.Unit();
        unit.Promotions.AddPromotion("Shock", isFree: true);
        Assert.Equal(3, unit.GetMaxMovement());
        unit.Promotions.RemovePromotion("Shock");
        Assert.Empty(unit.Promotions.Promotions);
        Assert.Equal(2, unit.GetMaxMovement());
        unit.Promotions.RemovePromotion("Shock"); // not there: nothing happens
        Assert.Throws<KeyNotFoundException>(() => unit.Promotions.RemovePromotion("Unknown")); // Kotlin `!!`
    }

    [Fact]
    public void GetPromotions_SortedFollowsTheRulesetOrder_AndSkipsUnknownNames()
    {
        var w = NewWorld(ruleset => AddPromotions(ruleset, new Promotion { Name = "B" }, new Promotion { Name = "A" }, new Promotion { Name = "C" }));
        var unit = w.Unit();
        Assert.Empty(unit.Promotions.GetPromotions());
        unit.Promotions.Promotions.Add("C");
        unit.Promotions.Promotions.Add("A");
        unit.Promotions.Promotions.Add("Gone");
        unit.Promotions.Promotions.Add("B");
        Assert.Equal(["B", "A", "C"], unit.Promotions.GetPromotions(sorted: true).Select(it => it.Name));
        Assert.Equal(3, unit.Promotions.GetPromotions().Count());
    }

    [Fact]
    public void GetAvailablePromotions_ChecksTypePrerequisitesAndUniques()
    {
        var w = NewWorld(ruleset => AddPromotions(ruleset,
            new Promotion { Name = "Open", UnitTypes = ["Land"] },
            new Promotion { Name = "WrongType", UnitTypes = ["Water"] },
            new Promotion { Name = "Needs", UnitTypes = ["Land"], Prerequisites = ["Open"] },
            new Promotion { Name = "Hidden", UnitTypes = ["Land"], Uniques = ["Unavailable"] },
            new Promotion { Name = "Gated", UnitTypes = ["Land"], Uniques = ["Only available <when above [50] XP>"] },
            new Promotion { Name = "Free", UnitTypes = ["Land"], Uniques = ["This Promotion is free"] }));
        var unit = w.Unit();
        Assert.Equal(["Open", "Free"], unit.Promotions.GetAvailablePromotions().Select(it => it.Name).Where(it => it != "Gated"));
        unit.Promotions.AddPromotion("Open", isFree: true);
        Assert.Equal(["Needs", "Free"], unit.Promotions.GetAvailablePromotions().Select(it => it.Name).Where(it => it != "Gated"));
    }

    [Fact]
    public void CanBePromoted_NeedsAnAvailablePromotionAndEnoughXpOrAFreeOne()
    {
        var w = NewWorld(ruleset => AddPromotions(ruleset, new Promotion { Name = "Open", UnitTypes = ["Land"] }));
        var unit = w.Unit();
        Assert.False(unit.Promotions.CanBePromoted());
        unit.Promotions.XP = 10;
        Assert.True(unit.Promotions.CanBePromoted());
        Assert.True(unit.Promotions.CanAffordPromotions(1));
        Assert.False(unit.Promotions.CanAffordPromotions(2));

        var none = NewWorld().Unit();
        none.Promotions.XP = 100;
        Assert.False(none.Promotions.CanBePromoted()); // nothing available

        var free = NewWorld(ruleset => AddPromotions(ruleset, new Promotion { Name = "Free", UnitTypes = ["Land"], Uniques = ["This Promotion is free"] })).Unit();
        Assert.True(free.Promotions.CanBePromoted());
    }

    [Fact]
    public void GetPromotionTreeCandidates_ListsTheTypeAndAdoptedPromotions_AndFlagsBlockedOnes()
    {
        var w = NewWorld(ruleset => AddPromotions(ruleset,
            new Promotion { Name = "Open", UnitTypes = ["Land"] },
            new Promotion { Name = "Other", UnitTypes = ["Water"] },
            new Promotion { Name = "Adopted", UnitTypes = ["Water"] },
            new Promotion { Name = "Hidden", UnitTypes = ["Land"], Uniques = ["Unavailable"] }));
        var unit = w.Unit();
        unit.Promotions.Promotions.Add("Adopted");
        var candidates = unit.Promotions.GetPromotionTreeCandidates();
        Assert.Equal([("Open", false, false), ("Adopted", true, false), ("Hidden", false, true)],
            candidates.Select(it => (it.Promotion.Name, it.IsAdopted, it.IsBlockedByUniques)));
    }

    [Fact]
    public void Clone_CopiesTheDataAndEqualsComparesIt()
    {
        var w = NewWorld(ruleset => AddPromotions(ruleset, new Promotion { Name = "Shock" }));
        var unit = w.Unit();
        unit.Promotions.XP = 4;
        unit.Promotions.NumberOfPromotions = 2;
        unit.Promotions.Promotions.Add("Shock");
        var clone = unit.Promotions.Clone();
        Assert.NotSame(unit.Promotions, clone);
        Assert.NotSame(unit.Promotions.Promotions, clone.Promotions);
        Assert.Equal(unit.Promotions, clone);
        Assert.Equal(unit.Promotions.GetHashCode(), clone.GetHashCode());
        clone.Promotions.Add("More");
        Assert.NotEqual(unit.Promotions, clone);
        Assert.NotEqual<object>(unit.Promotions, "text");
        var other = w.Unit("Swordsman");
        var withUnit = unit.Promotions.Clone(other);
        Assert.Equal(["Shock"], withUnit.GetPromotions().Select(it => it.Name)); // the unit was set
        Assert.Throws<InvalidOperationException>(() => new UnitPromotions().XpForNextPromotion()); // lateinit unit
    }

    [Fact]
    public void UnitPromotions_RoundTripsThroughJson()
    {
        var promotions = new UnitPromotions
        {
            XP = 45,
            NumberOfPromotions = 3,
        };
        promotions.Promotions.Add("Shock I");
        promotions.Promotions.Add("Drill I");

        var json = UncivJson.Serialize(promotions);
        var back = UncivJson.Deserialize<UnitPromotions>(json);

        Assert.NotNull(back);
        Assert.Equal(45, back.XP);
        Assert.Equal(3, back.NumberOfPromotions);
        Assert.Equal(2, back.Promotions.Count);
        Assert.Contains("Shock I", back.Promotions);
        Assert.Contains("Drill I", back.Promotions);
        Assert.Equal(promotions, back);
        Assert.Equal(promotions.GetHashCode(), back.GetHashCode());

        // Default empty promotions object round-trips
        var empty = new UnitPromotions();
        var emptyBack = UncivJson.Deserialize<UnitPromotions>(UncivJson.Serialize(empty));
        Assert.NotNull(emptyBack);
        Assert.Equal(empty, emptyBack);
        Assert.Equal(0, emptyBack.XP);
        Assert.Equal(0, emptyBack.NumberOfPromotions);
        Assert.Empty(emptyBack.Promotions);

        // Explicit JSON string verifying expected Kotlin field names
        var rawJson = """{"XP":20,"promotions":["Barrage I"],"numberOfPromotions":1}""";
        var fromRaw = UncivJson.Deserialize<UnitPromotions>(rawJson);
        Assert.NotNull(fromRaw);
        Assert.Equal(20, fromRaw.XP);
        Assert.Equal(1, fromRaw.NumberOfPromotions);
        Assert.Equal(["Barrage I"], fromRaw.Promotions);
    }

    [Fact]
    public void AddPromotion_AndRemovePromotion_FirePromotionTriggers()
    {
        var w = NewWorld(ruleset =>
        {
            AddPromotions(ruleset,
                new Promotion
                {
                    Name = "Shock",
                    Uniques = ["Gain [50] [Gold]"], // direct effect without trigger conditionals
                },
                new Promotion
                {
                    Name = "Drill",
                    Uniques = ["Gain [10] [Gold] <upon entering a [Hill] tile>"], // trigger conditional (skipped by direct effects)
                });
        });

        var unit = w.Unit("Hero", "Land", 2, 2, null,
            "Gain [10] [Gold] <upon being promoted>",
            "Gain [20] [Gold] <upon gaining the [Shock] promotion>",
            "Gain [30] [Gold] <upon gaining the [Drill] promotion>",
            "Gain [40] [Gold] <upon losing the [Shock] promotion>");

        // Verify that unit triggered uniques are identified properly
        var uponPromotion = unit.GetTriggeredUniques(UniqueType.TriggerUponPromotion).ToList();
        Assert.Single(uponPromotion);
        Assert.Equal("Gain [10] [Gold] <upon being promoted>", uponPromotion[0].Text);

        var uponGainShock = unit.GetTriggeredUniques(UniqueType.TriggerUponPromotionGain, null, it => it.Params[0] == "Shock").ToList();
        Assert.Single(uponGainShock);
        Assert.Equal("Gain [20] [Gold] <upon gaining the [Shock] promotion>", uponGainShock[0].Text);

        var uponGainDrill = unit.GetTriggeredUniques(UniqueType.TriggerUponPromotionGain, null, it => it.Params[0] == "Drill").ToList();
        Assert.Single(uponGainDrill);
        Assert.Equal("Gain [30] [Gold] <upon gaining the [Drill] promotion>", uponGainDrill[0].Text);

        var uponLossShock = unit.GetTriggeredUniques(UniqueType.TriggerUponPromotionLoss, null, it => it.Params[0] == "Shock").ToList();
        Assert.Single(uponLossShock);
        Assert.Equal("Gain [40] [Gold] <upon losing the [Shock] promotion>", uponLossShock[0].Text);

        // Paid promotion: increments NumberOfPromotions, spends XP, triggers TriggerUponPromotion and TriggerUponPromotionGain("Shock")
        unit.Promotions.XP = 20;
        unit.Promotions.AddPromotion("Shock", isFree: false);
        Assert.Equal(10, unit.Promotions.XP);
        Assert.Equal(1, unit.Promotions.NumberOfPromotions);
        Assert.Contains("Shock", unit.Promotions.Promotions);

        // Free promotion: does not change NumberOfPromotions or XP, does not trigger TriggerUponPromotion, triggers TriggerUponPromotionGain("Drill")
        unit.Promotions.AddPromotion("Drill", isFree: true);
        Assert.Equal(10, unit.Promotions.XP);
        Assert.Equal(1, unit.Promotions.NumberOfPromotions);
        Assert.Contains("Drill", unit.Promotions.Promotions);

        // Removing promotion: triggers TriggerUponPromotionLoss("Shock")
        unit.Promotions.RemovePromotion("Shock");
        Assert.DoesNotContain("Shock", unit.Promotions.Promotions);

        // Removing non-acquired promotion: no-op, does not throw or trigger
        unit.Promotions.RemovePromotion("Shock");
    }

    // ---- UnitUpgradeManager

    private static World UpgradeWorld(params string[] civUniques) => NewWorld(ruleset =>
    {
        MapUnitFactory.NewBaseUnit(ruleset, "Warrior").UpgradesTo = "Swordsman";
        var sword = MapUnitFactory.NewBaseUnit(ruleset, "Swordsman");
        sword.Cost = 150;
        var knight = MapUnitFactory.NewBaseUnit(ruleset, "Knight");
        knight.Cost = 200;
        var rat = MapUnitFactory.NewBaseUnit(ruleset, "Ruin Hero");
        rat.Cost = 110;
    }, civUniques);

    [Fact]
    public void GetUpgrades_ListsRuinsThenUniqueThenNormalUpgrades()
    {
        var w = UpgradeWorld();
        var warrior = w.Ruleset.Units["Warrior"];
        warrior.Uniques = ["May upgrade to [Ruin Hero] through ruins-like effects", "Can upgrade to [Knight]"];
        warrior.SetRuleset(w.Ruleset);
        var unit = w.Unit();
        var upgrades = unit.Upgrade.GetUpgrades();
        Assert.Equal([("Ruin Hero", true), ("Knight", false), ("Swordsman", false)], upgrades.Select(it => (it.UpgradedUnit.Name, it.IsSpecial)));
        Assert.Equal(["May upgrade to [Ruin Hero] through ruins-like effects", "Can upgrade to [Knight]", null], upgrades.Select(it => it.Unique?.Text));
        Assert.All(upgrades, it => Assert.True(it.IsShownToPlayer));
        Assert.Equal(["Ruin Hero"], unit.Upgrade.GetUpgradesOfKind(true).Select(it => it.UpgradedUnit.Name));
        Assert.Equal(["Knight", "Swordsman"], unit.Upgrade.GetUpgradesOfKind(false).Select(it => it.UpgradedUnit.Name));

        w.Ruleset.Units["Warrior"].Uniques = [];
        w.Ruleset.Units["Warrior"].SetRuleset(w.Ruleset);
        var plain = w.Unit();
        Assert.Equal(["Swordsman"], plain.Upgrade.GetUpgradesOfKind(true).Select(it => it.UpgradedUnit.Name)); // no special one: the normal ones
    }

    [Fact]
    public void GetUpgrades_IsEmptyWithoutUpgrades()
    {
        var w = UpgradeWorld();
        Assert.Empty(w.Unit("Knight").Upgrade.GetUpgrades());
    }

    [Fact]
    public void CanUpgrade_IsUnavailableToItself_AvailableWhenNothingRejects()
    {
        var w = UpgradeWorld();
        var unit = w.Unit();
        Assert.Equal(UpgradeAvailability.Unavailable, unit.Upgrade.CanUpgrade(w.Ruleset.Units["Warrior"]));
        Assert.Equal(UpgradeAvailability.Available, unit.Upgrade.CanUpgrade(w.Ruleset.Units["Swordsman"]));
    }

    [Fact]
    public void CanUpgrade_GradesTheRejectionReasons()
    {
        var w = UpgradeWorld();
        var unit = w.Unit();
        var needsResource = w.Ruleset.Units["Swordsman"];
        needsResource.Uniques = ["Consumes [1] [Iron]"];
        needsResource.SetRuleset(w.Ruleset);
        w.Ruleset.TileResources.Put("Iron", new TileResource { Name = "Iron", ResourceType = ResourceType.Strategic });
        Assert.Equal(UpgradeAvailability.AvailableExceptResources, unit.Upgrade.CanUpgrade(needsResource));
        Assert.False(new PossibleUpgrade(needsResource, null, false, UpgradeAvailability.AvailableForFree, 0, new Unciv.Models.Counter<string>()).IsShownToPlayer);

        var needsTech = w.Ruleset.Units["Knight"];
        needsTech.RequiredTech = "Chivalry";
        Assert.Equal(UpgradeAvailability.AvailableForFree, unit.Upgrade.CanUpgrade(needsTech));

        // construction rejections (here: Unbuildable) do not matter for an upgrade
        var unbuildable = w.Ruleset.Units["Ruin Hero"];
        unbuildable.Uniques = ["Unbuildable"];
        unbuildable.SetRuleset(w.Ruleset);
        Assert.Equal(UpgradeAvailability.Available, unit.Upgrade.CanUpgrade(unbuildable));
    }

    [Fact]
    public void GetCostOfUpgrade_FollowsTheModConstants()
    {
        var w = UpgradeWorld();
        var unit = w.Unit();
        // base 10 + perProduction 2 * (150 - 100) = 110; no era multiplier; exponent 1; rounded down to a multiple of 5
        Assert.Equal(110, unit.Upgrade.GetCostOfUpgrade(w.Ruleset.Units["Swordsman"]));
        // never negative production difference
        Assert.Equal(10, unit.Upgrade.GetCostOfUpgrade(w.Ruleset.Units["Warrior"]));
        Assert.Equal(210, unit.Upgrade.GetCostOfUpgrade(w.Ruleset.Units["Knight"]));
        w.Info.SpeedData.Modifier = 1.5f;
        Assert.Equal(165, unit.Upgrade.GetCostOfUpgrade(w.Ruleset.Units["Swordsman"])); // 110 * 1.5

        var discounted = UpgradeWorld("[-50]% Gold cost of upgrading").Unit();
        Assert.Equal(55, discounted.Upgrade.GetCostOfUpgrade(discounted.Civ.GameInfo.Ruleset.Units["Swordsman"]));
    }

    [Fact]
    public void CanPerformUpgrade_FreeOnlyNeedsAvailability_PaidNeedsGoldMovementAndOwnTerritory()
    {
        var w = UpgradeWorld();
        var unit = w.Unit();
        var upgrade = unit.Upgrade.GetUpgrades().Single();
        var unavailable = new PossibleUpgrade(upgrade.UpgradedUnit, null, false, UpgradeAvailability.Unavailable, 0, new Unciv.Models.Counter<string>());
        var forFree = new PossibleUpgrade(upgrade.UpgradedUnit, null, false, UpgradeAvailability.AvailableForFree, 0, new Unciv.Models.Counter<string>());
        Assert.False(unit.Upgrade.CanPerformUpgrade(unavailable, true));
        Assert.True(unit.Upgrade.CanPerformUpgrade(forFree, true));
        Assert.False(unit.Upgrade.CanPerformUpgrade(forFree, false)); // paid ones need Available
        Assert.False(unit.Upgrade.CanPerformUpgrade(upgrade, false)); // no gold, no movement, no territory

        w.Civ.AddGold(upgrade.GoldCostOfUpgrade);
        unit.CurrentMovement = 1f;
        Assert.False(unit.Upgrade.CanPerformUpgrade(upgrade, false)); // not our territory
        var city = MapUnitFactory.NewCity(w.Civ, "Rome", HexCoord.Of(2, 2));
        w.Civ.Cities = [city];
        unit.CurrentTile.SetOwningCity(city);
        Assert.True(unit.Upgrade.CanPerformUpgrade(upgrade, false));
        w.Civ.AddGold(-1);
        Assert.False(unit.Upgrade.CanPerformUpgrade(upgrade, false));
    }

    [Fact]
    public void PerformUpgrade_ThrowsWhenNotAllowed_AndDoesNothingForADestroyedUnit()
    {
        var w = UpgradeWorld();
        var unit = w.Unit();
        var upgrade = unit.Upgrade.GetUpgrades().Single();
        var ex = Assert.Throws<InvalidOperationException>(() => unit.Upgrade.PerformUpgrade(upgrade, false));
        Assert.Equal("Cannot upgrade Warrior to Swordsman", ex.Message);
        unit.IsDestroyed = true;
        unit.Upgrade.PerformUpgrade(upgrade, false); // 'queued twice': ignored
    }

    // ---- UnitTurnManager

    [Fact]
    public void StartTurn_RestoresMovementAndCountsDownTheStatuses()
    {
        var w = NewWorld(ruleset => AddPromotions(ruleset, new Promotion { Name = "Short" }, new Promotion { Name = "Long" }));
        var unit = w.Unit();
        unit.AttacksThisTurn = 2;
        unit.Due = false;
        unit.AttacksSinceTurnStart.Add(HexCoord.Of(1, 1));
        foreach (var status in new[] { new MapUnitClass.UnitStatus("Short", 1), new MapUnitClass.UnitStatus("Long", 2) })
        {
            unit.StatusMap[status.Name] = status;
            status.SetTransients(unit);
        }
        new UnitTurnManager(unit).StartTurn();
        Assert.Equal((2f, 0, true), (unit.CurrentMovement, unit.AttacksThisTurn, unit.Due));
        Assert.Empty(unit.AttacksSinceTurnStart);
        Assert.Equal(["Long"], unit.StatusMap.Keys);
        Assert.Equal(1, unit.StatusMap["Long"].TurnsLeft);
        Assert.Equal(1, unit.Move().PathfindingCacheCleared);
        Assert.Single(unit.MovementMemories);
    }

    [Fact]
    public void StartTurn_WakesUnitsThatAreHealedUntilHealed_AndNotifies()
    {
        var w = NewWorld();
        var unit = w.Unit();
        unit.Action = "Fortify until healed";
        unit.Health = 80;
        new UnitTurnManager(unit).StartTurn();
        Assert.Equal("Fortify until healed", unit.Action);
        unit.Health = 100;
        new UnitTurnManager(unit).StartTurn();
        Assert.Null(unit.Action);
        Assert.Contains(w.Civ.Notifications, it => it.Text == "[[Warrior]] has fully healed");
    }

    [Fact]
    public void StartTurn_TeleportsOutOfForeignTerritoryWeCannotPassThrough()
    {
        var w = NewWorld();
        var other = MapUnitFactory.NewCiv("Egypt", w.Info);
        var city = MapUnitFactory.NewCity(other, "Thebes", HexCoord.Of(0, 0));
        other.Cities = [city];
        var unit = w.Unit();
        new UnitTurnManager(unit).StartTurn();
        Assert.Equal(0, unit.Move().Teleports); // unowned tile
        unit.CurrentTile.SetOwningCity(city);
        new UnitTurnManager(unit).StartTurn();
        Assert.Equal(1, unit.Move().Teleports); // peace, no open borders
    }

    [Fact]
    public void StartTurn_SleepingUnitsWakeWhenAnEnemyIsInSight()
    {
        var w = NewWorld();
        var enemyCiv = MapUnitFactory.NewCiv(Constants.Barbarians, w.Info);
        var sleeper = w.Unit();
        sleeper.Action = "Sleep";
        new UnitTurnManager(sleeper).StartTurn();
        Assert.Equal("Sleep", sleeper.Action); // nobody around

        var enemy = w.Unit("Raider", "Land", 3, 2, enemyCiv);
        w.TileAt(3, 2).MilitaryUnit = enemy;
        new UnitTurnManager(sleeper).StartTurn();
        Assert.Equal("Sleep", sleeper.Action); // the enemy is not on a tile we see
        w.Civ.ViewableTiles = new HashSet<TileClass> { w.TileAt(3, 2) };
        new UnitTurnManager(sleeper).StartTurn();
        Assert.Null(sleeper.Action);
    }

    [Fact]
    public void EndTurn_FortifyingCountsUpToTwo_AndHealsUnitsThatDidNotMove()
    {
        var w = NewWorld();
        var unit = w.Unit();
        unit.Health = 50;
        unit.CurrentMovement = 2f; // has not moved
        unit.Action = "Fortify";
        var turn = new UnitTurnManager(unit);
        turn.EndTurn();
        Assert.Equal(1, unit.TurnsFortified);
        Assert.Equal(60, unit.Health); // neutral territory heals 10
        turn.EndTurn();
        turn.EndTurn();
        Assert.Equal(2, unit.TurnsFortified);
        unit.Action = null;
        turn.EndTurn();
        Assert.Equal(0, unit.TurnsFortified);
    }

    [Fact]
    public void EndTurn_DoesNotHealAUnitThatActed_UnlessItHealsEvenAfterAction()
    {
        var w = NewWorld();
        var tired = w.Unit();
        tired.CurrentMovement = 2f;
        tired.Health = 50;
        tired.AttacksThisTurn = 1;
        new UnitTurnManager(tired).EndTurn();
        Assert.Equal(50, tired.Health);
        var tough = w.Unit("Tough", "Land", 2, 2, null, "Unit will heal every turn, even if it performs an action");
        tough.CurrentMovement = 2f;
        tough.Health = 50;
        tough.AttacksThisTurn = 1;
        new UnitTurnManager(tough).EndTurn();
        Assert.Equal(60, tough.Health);
    }

    [Fact]
    public void EndTurn_ClearsParadropAndAirSweepPreparations()
    {
        var w = NewWorld();
        var unit = w.Unit();
        unit.Action = "Paradrop";
        new UnitTurnManager(unit).EndTurn();
        Assert.Null(unit.Action);
        unit.Action = "Air Sweep";
        new UnitTurnManager(unit).EndTurn();
        Assert.Null(unit.Action);
    }

    [Fact]
    public void EndTurn_TerrainDamageHurtsAndCanDestroy()
    {
        var w = NewWorld();
        w.TileAt(2, 2).AddTerrainFeature("Lava");
        var unit = w.Unit();
        unit.Health = 100;
        unit.AttacksThisTurn = 1; // no healing
        new UnitTurnManager(unit).EndTurn();
        Assert.Equal(93, unit.Health);
        Assert.Contains(w.Civ.Notifications, it => it.Text == "Our [Warrior] took [7] tile damage");
        unit.Health = 5;
        new UnitTurnManager(unit).EndTurn();
        Assert.True(unit.IsDestroyed);
        Assert.Contains(w.Civ.Notifications, it => it.Text == "Our [Warrior] took [7] tile damage and was destroyed");
    }

    [Fact]
    public void EndTurn_ReligiousUnitsLoseFaithInForeignTerritory()
    {
        var w = NewWorld();
        var other = MapUnitFactory.NewCiv("Egypt", w.Info);
        var city = MapUnitFactory.NewCity(other, "Thebes", HexCoord.Of(0, 0));
        other.Cities = [city];
        var prophet = w.Unit("Prophet", "Land", 2, 2, null, "Religious Unit", "May enter foreign tiles without open borders, but loses [3] religious strength each turn it ends there");
        prophet.BaseUnit.ReligiousStrength = 5;
        prophet.CurrentTile.SetOwningCity(city);
        // peace and no open borders: the prophet cannot pass through
        var turn = new UnitTurnManager(prophet);
        turn.EndTurn();
        Assert.Equal(3, prophet.ReligiousStrengthLost);
        Assert.False(prophet.IsDestroyed);
        turn.EndTurn();
        Assert.True(prophet.IsDestroyed);
        Assert.Contains(w.Civ.Notifications, it => it.Text == "Your [Prophet] lost its faith after spending too long inside enemy territory!");
    }

    [Fact]
    public void EndTurn_WorkersFinishImprovementsAndRequestAPopulationReassignment()
    {
        var w = NewWorld(ruleset => ruleset.TileImprovements.Put("Farm", new TileImprovement { Name = "Farm" }));
        var city = MapUnitFactory.NewCity(w.Civ, "Rome", HexCoord.Of(0, 0));
        w.Civ.Cities = [city];
        var worker = w.Unit("Worker", "Land", 2, 2, null, "Can build [Land] improvements on tiles");
        worker.CurrentMovement = 1f;
        var tile = worker.CurrentTile;
        tile.SetOwningCity(city);
        tile.QueueImprovement("Farm", 1);
        Assert.False(city.ShouldReassignPopulation);
        new UnitTurnManager(worker).EndTurn();
        Assert.Equal("Farm", tile.Improvement);
        Assert.True(city.ShouldReassignPopulation);
        Assert.Empty(w.Info.TutorialTasks); // not the current player
    }

    [Fact]
    public void EndTurn_ImprovementsDamageAdjacentEnemyUnits()
    {
        var w = NewWorld(ruleset => ruleset.TileImprovements.Put("Fort", new TileImprovement { Name = "Fort", Uniques = ["Adjacent enemy units ending their turn take [10] damage"] }));
        var enemy = MapUnitFactory.NewCiv(Constants.Barbarians, w.Info); // always at war with everybody
        enemy.PlayerType = PlayerType.Human; // to record the notification
        var city = MapUnitFactory.NewCity(enemy, "Camp", HexCoord.Of(0, 0));
        enemy.Cities = [city];
        var fortTile = w.TileAt(2, 3);
        fortTile.SetOwningCity(city);
        fortTile.Improvement = "Fort";
        var unit = w.Unit();
        unit.AttacksThisTurn = 1; // no healing
        new UnitTurnManager(unit).EndTurn();
        Assert.Equal(90, unit.Health);
        Assert.Contains(w.Civ.Notifications, it => it.Text == "An enemy [Fort] has attacked our [Warrior]");
        unit.Health = 10;
        new UnitTurnManager(unit).EndTurn();
        Assert.True(unit.IsDestroyed);
        Assert.Contains(w.Civ.Notifications, it => it.Text == "An enemy [Fort] has destroyed our [Warrior]");
        Assert.Contains(enemy.Notifications, it => it.Text == "Your [Fort] has destroyed an enemy [Warrior]");
    }

    [Fact]
    public void EndTurn_ImprovementsDamageAdjacentEnemyUnits_EqualDamageCitadels_FirstExtremeWins()
    {
        var w = NewWorld(ruleset =>
        {
            ruleset.TileImprovements.Put("CitadelAlpha", new TileImprovement
            {
                Name = "CitadelAlpha",
                Uniques = ["Adjacent enemy units ending their turn take [15] damage"],
            });
            ruleset.TileImprovements.Put("CitadelBeta", new TileImprovement
            {
                Name = "CitadelBeta",
                Uniques = ["Adjacent enemy units ending their turn take [15] damage"],
            });
            ruleset.TileImprovements.Put("CitadelGamma", new TileImprovement
            {
                Name = "CitadelGamma",
                Uniques = ["Adjacent enemy units ending their turn take [25] damage"],
            });
        });

        var enemy = MapUnitFactory.NewCiv(Constants.Barbarians, w.Info);
        enemy.PlayerType = PlayerType.Human;
        var city = MapUnitFactory.NewCity(enemy, "Camp", HexCoord.Of(0, 0));
        enemy.Cities = [city];

        var unit = w.Unit();
        unit.AttacksThisTurn = 1; // prevent healing
        var neighbors = unit.CurrentTile.Neighbors;
        Assert.True(neighbors.Count >= 2);

        var firstNeighbor = neighbors[0];
        var secondNeighbor = neighbors[1];

        firstNeighbor.SetOwningCity(city);
        firstNeighbor.Improvement = "CitadelAlpha";
        secondNeighbor.SetOwningCity(city);
        secondNeighbor.Improvement = "CitadelBeta";

        // Equal damage citadels (15 each): first neighbor in Neighbors iteration wins (does not stack, picks first extreme)
        new UnitTurnManager(unit).EndTurn();
        Assert.Equal(85, unit.Health); // 100 - 15 = 85 (not 100 - 30)
        Assert.Contains(w.Civ.Notifications, it => it.Text == "An enemy [CitadelAlpha] has attacked our [Warrior]");
        Assert.DoesNotContain(w.Civ.Notifications, it => it.Text == "An enemy [CitadelBeta] has attacked our [Warrior]");

        // Now upgrade secondNeighbor to CitadelGamma (25 damage): higher damage overrides first extreme
        secondNeighbor.Improvement = "CitadelGamma";
        secondNeighbor.TileImprovement = null;
        w.Civ.Notifications.Clear();
        unit.AttacksThisTurn = 1;
        new UnitTurnManager(unit).EndTurn();
        Assert.Equal(60, unit.Health); // 85 - 25 = 60
        Assert.Contains(w.Civ.Notifications, it => it.Text == "An enemy [CitadelGamma] has attacked our [Warrior]");
        Assert.DoesNotContain(w.Civ.Notifications, it => it.Text == "An enemy [CitadelAlpha] has attacked our [Warrior]");
    }

    // ---- UnitManager

    [Fact]
    public void AddUnit_AppendsInOrder_AndRemoveUnitDeletesByReference()
    {
        var w = NewWorld();
        var a = w.Unit("Warrior", "Land", 0, 0);
        var b = w.Unit("Warrior", "Land", 1, 1);
        var c = w.Unit("Warrior", "Land", 2, 2);
        (a.Id, b.Id, c.Id) = (1, 2, 3);
        var units = (UnitManager)w.Civ.Units;
        Assert.Equal(0, units.GetCivUnitsSize());
        foreach (var unit in new[] { a, b, c }) units.AddUnit(unit, updateCivInfo: false);
        Assert.Equal([a, b, c], units.GetCivUnits());
        units.RemoveUnit(b, updateCivInfo: false);
        Assert.Equal([a, c], units.GetCivUnits());
        units.RemoveUnit(b, updateCivInfo: false); // not there: unchanged
        Assert.Equal(2, units.GetCivUnitsSize());
        Assert.Same(c, units.GetUnitById(c.Id));
        Assert.Null(units.GetUnitById(12345));
    }

    [Fact]
    public void GetCivUnits_IsASnapshot_SoModifyingWhileIteratingIsSafe()
    {
        var w = NewWorld();
        var units = (UnitManager)w.Civ.Units;
        var a = w.Unit();
        var b = w.Unit();
        units.AddUnit(a, false);
        units.AddUnit(b, false);
        foreach (var unit in units.GetCivUnits()) units.RemoveUnit(unit, false);
        Assert.Equal(0, units.GetCivUnitsSize());
    }

    [Fact]
    public void AddUnit_ByNameOrBaseUnitReturnsNullWithoutCitiesOrForAnUnknownName()
    {
        var w = NewWorld();
        var units = (UnitManager)w.Civ.Units;
        MapUnitFactory.NewBaseUnit(w.Ruleset, "Warrior");
        Assert.Null(units.AddUnit("Unknown"));
        Assert.Null(units.AddUnit("Warrior")); // no cities
        Assert.Null(units.AddUnit(w.Ruleset.Units["Warrior"]));
    }

    [Fact]
    public void AddUnit_WaterUnitWithoutANavalCityIsNotPlaced()
    {
        var w = NewWorld();
        var city = MapUnitFactory.NewCity(w.Civ, "Rome", HexCoord.Of(2, 2));
        w.Civ.Cities = [city];
        var boat = MapUnitFactory.NewBaseUnit(w.Ruleset, "Boat", "Water");
        Assert.Null(((UnitManager)w.Civ.Units).AddUnit(boat)); // the only city is inland
    }

    [Fact]
    public void GetDueUnits_AndIdleUnits_FollowTheListStartingAtTheNextDueUnit()
    {
        var w = NewWorld();
        var units = (UnitManager)w.Civ.Units;
        var list = Enumerable.Range(0, 4).Select(i => w.Unit("Warrior", "Land", i, 0)).ToList();
        foreach (var unit in list)
        {
            unit.CurrentMovement = 2f;
            units.AddUnit(unit, false);
        }
        list[1].CurrentMovement = 0f; // not idle
        list[2].Due = false;
        Assert.Equal([list[0], list[2], list[3]], units.GetIdleUnits());
        Assert.Equal([list[0], list[3]], units.GetDueUnits());

        Assert.Same(list[0], units.CycleThroughDueUnits());
        Assert.Equal([list[3], list[0]], units.GetDueUnits()); // rotated: the unit after the one cycled to is first
        Assert.Same(list[3], units.CycleThroughDueUnits());
        Assert.Same(list[0], units.CycleThroughDueUnits());
    }

    [Fact]
    public void CycleThroughDueUnits_SkipsTheGivenUnitUnlessItIsTheOnlyOne()
    {
        var w = NewWorld();
        var units = (UnitManager)w.Civ.Units;
        Assert.Null(units.CycleThroughDueUnits());
        var a = w.Unit();
        var b = w.Unit();
        a.CurrentMovement = b.CurrentMovement = 2f;
        units.AddUnit(a, false);
        units.AddUnit(b, false);
        Assert.Same(b, units.CycleThroughDueUnits(a));
        Assert.Same(b, units.CycleThroughDueUnits(a)); // the cursor is now on a, which is skipped again
    }

    [Fact]
    public void CycleThroughDueUnits_FallsBackToTheSkippedUnit_AndReturnsNullWhenNothingIsDue()
    {
        var w = NewWorld();
        var units = (UnitManager)w.Civ.Units;
        var only = w.Unit();
        only.CurrentMovement = 2f;
        units.AddUnit(only, false);
        Assert.Same(only, units.CycleThroughDueUnits(only));
        only.Due = false;
        Assert.Null(units.CycleThroughDueUnits());
    }

    [Fact]
    public void CycleThroughDueUnits_AlternateOrderPicksTheNearestUnit()
    {
        var w = NewWorld();
        w.Info.AlternateCycleOrder = true;
        var units = (UnitManager)w.Civ.Units;
        var far = w.Unit("Warrior", "Land", 4, 4);
        var near = w.Unit("Warrior", "Land", 1, 0);
        var skipped = w.Unit("Warrior", "Land", 0, 0);
        foreach (var unit in new[] { far, near, skipped })
        {
            unit.CurrentMovement = 2f;
            units.AddUnit(unit, false);
        }
        // measured from the skipped unit's tile
        Assert.Same(near, units.CycleThroughDueUnits(skipped));
        // without a skipped unit: from the tile (0, 0), as there is no capital
        w.Civ.Cities = [];
        Assert.Same(skipped, units.CycleThroughDueUnits());
        near.Due = far.Due = skipped.Due = false;
        Assert.Null(units.CycleThroughDueUnits());
    }

    [Fact]
    public void CycleThroughDueUnits_AlternateOrder_EqualDistanceTieBreak_FirstExtremeWins()
    {
        var w = NewWorld();
        w.Info.AlternateCycleOrder = true;
        var units = (UnitManager)w.Civ.Units;
        w.Civ.Cities = []; // No capital: center falls back to TileMap[0, 0]

        // Both units are at HexCoord distance 1 from (0, 0)
        var unitA = w.Unit("Warrior", "Land", 1, 0);
        var unitB = w.Unit("Warrior", "Land", 0, 1);
        Assert.Equal(1, unitA.CurrentTile.AerialDistanceTo(w.Map[HexCoord.Of(0, 0)]));
        Assert.Equal(1, unitB.CurrentTile.AerialDistanceTo(w.Map[HexCoord.Of(0, 0)]));

        unitA.CurrentMovement = 2f;
        unitB.CurrentMovement = 2f;

        // When unitA is added first, unitA is first in unitList and wins the tie-break
        units.AddUnit(unitA, false);
        units.AddUnit(unitB, false);
        Assert.Same(unitA, units.CycleThroughDueUnits());
        // Skipping unitA chooses the remaining equal-distance unit
        Assert.Same(unitB, units.CycleThroughDueUnits(unitA));

        // When unitB is added first, unitB is first in unitList and wins the tie-break
        var w2 = NewWorld();
        w2.Info.AlternateCycleOrder = true;
        var units2 = (UnitManager)w2.Civ.Units;
        w2.Civ.Cities = [];
        var unitA2 = w2.Unit("Warrior", "Land", 1, 0);
        var unitB2 = w2.Unit("Warrior", "Land", 0, 1);
        unitA2.CurrentMovement = 2f;
        unitB2.CurrentMovement = 2f;
        units2.AddUnit(unitB2, false);
        units2.AddUnit(unitA2, false);
        Assert.Same(unitB2, units2.CycleThroughDueUnits());
        Assert.Same(unitA2, units2.CycleThroughDueUnits(unitB2));
    }

    [Fact]
    public void NewMapUnit_CreatesATransientReadyUnitWithTheNextId()
    {
        var w = NewWorld();
        var baseUnit = MapUnitFactory.NewBaseUnit(w.Ruleset, "Warrior");
        var unit = baseUnit.NewMapUnit(w.Civ);
        Assert.Equal(("Warrior", "Rome", 1), (unit.Name, unit.Owner, unit.Id));
        Assert.Same(w.Civ, unit.Civ);
        Assert.Same(baseUnit, unit.BaseUnit);
        Assert.Equal(7, baseUnit.NewMapUnit(w.Civ, 7).Id);
        Assert.Equal(2, baseUnit.NewMapUnit(w.Civ).Id); // an explicit id does not use up a number
    }
}
