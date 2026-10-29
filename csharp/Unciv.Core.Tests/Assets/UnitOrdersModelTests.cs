using Unciv.Assets;
using Unciv.Commands.Units;
using Unciv.Models;
using Unciv.Models.Ruleset.Unit;
using Unciv.Net.Visibility;
using Unciv.Queries.Tiles;
using Unciv.Queries.Units;

namespace Unciv.Core.Tests.Assets;

public class UnitOrdersModelTests
{
    private static UnitActionInfo Action(string title, bool enabled = true, UnitActionType type = UnitActionType.Fortify) =>
        new(new UnitActionDescriptor(type, title), title, enabled, false, null, null);

    private static UnitDto Unit(OwnUnitDto? own, bool fortified = false) =>
        new() { Id = 1, Name = "Archer", Owner = "Babylon", X = 1, Y = 2, HealthBucket = 3, IsEmbarked = false, IsFortified = fortified, Own = own };

    private static OwnUnitDto Own(float moves = 1.5f, int health = 80, string? action = null) =>
        new() { Health = health, CurrentMovement = moves, Xp = 12, Action = action, Promotions = ["Accuracy I"] };

    private static TileInfoResult Tile(params ImprovementOption[] options) =>
        new(new TilePos(1, 2), "Plains", [], null, null, 0, null, false, null, 0, "None", false, false, null, null, null, false, false, true, [], [], [], options);

    [Fact]
    public void PanelShowsStrengthMovesHealthXpAndPromotions()
    {
        var info = UnitOrdersModel.Panel(Unit(Own()), new BaseUnit { Name = "Archer", Strength = 5, RangedStrength = 6, Range = 2, Movement = 2 });
        Assert.Equal("Archer", info.Title);
        Assert.Equal("Ranged 6, range 2, melee 5", info.Strength);
        Assert.Equal("Moves 1.5/2", info.Moves);
        Assert.Equal("Health 80/100", info.Health);
        Assert.Equal("XP 12", info.Xp);
        Assert.Equal(["Accuracy I"], info.Promotions);
        Assert.Equal("", info.Order);
    }

    [Fact]
    public void PanelMeleeCivilianOrdersAndForeignUnit()
    {
        Assert.Equal("Combat 8", UnitOrdersModel.Panel(Unit(Own()), new BaseUnit { Strength = 8, Movement = 2 }).Strength);
        Assert.Equal("", UnitOrdersModel.Panel(Unit(Own()), new BaseUnit { Movement = 2 }).Strength);
        Assert.Equal("Moves 1.5", UnitOrdersModel.Panel(Unit(Own()), null).Moves);
        Assert.Equal("Sleep", UnitOrdersModel.Panel(Unit(Own(action: "Sleep")), null).Order);
        Assert.Equal("x", UnitOrdersModel.Panel(Unit(Own(action: "x")), null).Order);
        Assert.Equal("Fortified", UnitOrdersModel.Panel(Unit(Own(), fortified: true), null).Order);
        var foreign = UnitOrdersModel.Panel(Unit(null), null);
        Assert.Equal("Health 75%", foreign.Health);
        Assert.Equal("", foreign.Moves);
        Assert.Equal("", foreign.Xp);
        Assert.Empty(foreign.Promotions);
    }

    [Fact]
    public void MenuListsEngineActionsInOrderThenImproveThenPromoteThenDisband()
    {
        var actions = new UnitActionsResult(1, [Action("Fortify"), Action("Upgrade to [Swordsman] ([50] gold)", enabled: false)]);
        var tile = Tile(new ImprovementOption("Farm", [], []), new ImprovementOption("Mine", ["Obsolete"], []));
        var promotions = new PromotionChoicesResult(1, 12, 10, true, true,
        [new PromotionChoice("Accuracy II", true, [], []), new PromotionChoice("Barrage I", false, [], [])]);
        var menu = UnitOrdersModel.Menu(actions, tile, promotions, hasMovement: true);
        Assert.Equal(
            ["Fortify", "Upgrade to Swordsman (50 gold)", "Build: Farm", "Build: Mine", "Promote: Accuracy II", "Promote: Barrage I", "Disband unit"],
            menu.Select(it => it.Label));
        Assert.Equal([true, false, true, false, true, false, true], menu.Select(it => it.Enabled));
        Assert.Same(actions.Actions[0], menu[0].Action);
        Assert.Equal("Obsolete", menu[3].Reason);
        Assert.Equal("Accuracy II", menu[4].Argument);
        Assert.Equal(MenuEntryKind.Disband, menu[^1].Kind);
    }

    [Fact]
    public void MenuWithoutMovementOrExtrasHasOnlyEngineActions()
    {
        var menu = UnitOrdersModel.Menu(new UnitActionsResult(1, [Action("Sleep")]), null, null, hasMovement: false);
        Assert.Equal(["Sleep"], menu.Select(it => it.Label));
    }

    [Fact]
    public void CleanDropsPlaceholderBrackets() => Assert.Equal("Construct Road (3 turns)", UnitOrdersModel.Clean(" {Construct [Road]} ([3] turns) "));

    [Fact]
    public void OverlaysKeepOnlyEnterableReachableTilesAndAttackTargetsWhenAttackPossible()
    {
        var reachable = new ReachableTilesResult(1, new TilePos(0, 0), 2f, [new ReachableTile(1, 0, 1f, 1f, true), new ReachableTile(2, 0, 2f, 0f, false)]);
        Assert.Equal([(1, 0)], UnitOrdersModel.ReachableTiles(reachable));
        var targets = new AttackableTargetsResult("attack", true, [new AttackTarget(3, 4, "Warrior", "Rome", false, [])]);
        Assert.Equal([(3, 4)], UnitOrdersModel.AttackTiles(targets));
        Assert.Empty(UnitOrdersModel.AttackTiles(targets with { CanAttack = false }));
    }

    [Fact]
    public void ClickIntentPrefersOwnUnitsThenAttackThenPath()
    {
        var attackable = new HashSet<(int X, int Y)> { (3, 3) };
        Assert.Equal(TileClickIntent.SelectOwn, UnitOrdersModel.ClickIntent((3, 3), true, true, attackable));
        Assert.Equal(TileClickIntent.None, UnitOrdersModel.ClickIntent((3, 3), false, false, attackable));
        Assert.Equal(TileClickIntent.AttackPreview, UnitOrdersModel.ClickIntent((3, 3), false, true, attackable));
        Assert.Equal(TileClickIntent.PathPreview, UnitOrdersModel.ClickIntent((4, 3), false, true, attackable));
    }

    private static CombatantPreview Fighter(string name, int health, int strength, params StrengthModifier[] modifiers) =>
        new(name, "Civ", false, false, health, 100, strength, strength, modifiers);

    private static AttackPreviewResult Battle(int minD, int maxD, int minA, int maxA, int defHealth = 100, int attHealth = 100, string outcome = "damage", bool can = true) =>
        new("battle", can, outcome, new TilePos(3, 3), new TilePos(2, 3), Fighter("Warrior", attHealth, 8, new StrengthModifier("Fortified", 25)), Fighter("Archer", defHealth, 5),
            minD, maxD, minA, maxA, false, false, 0);

    [Fact]
    public void CombatShowsFightersDamageRangesAndHealthAfter()
    {
        var text = UnitOrdersModel.Combat(Battle(20, 30, 10, 14));
        Assert.Equal("Warrior attacks Archer", text.Title);
        Assert.True(text.CanConfirm);
        Assert.Equal("Attack", text.ConfirmLabel);
        Assert.Contains("Warrior (Civ): strength 8, health 100/100", text.Lines);
        Assert.Contains("    +25% Fortified", text.Lines);
        Assert.Contains("Damage to Archer (Civ): 20-30", text.Lines);
        Assert.Contains("Damage to Warrior (Civ): 10-14", text.Lines);
        Assert.Contains("Archer (Civ) health afterwards: 70-80", text.Lines);
        Assert.DoesNotContain(text.Lines, it => it.Contains("destroyed", StringComparison.Ordinal));
    }

    [Fact]
    public void CombatSaysWhenAFighterWillOrMayBeDestroyed()
    {
        var kill = UnitOrdersModel.Combat(Battle(30, 40, 5, 5, defHealth: 20, attHealth: 5));
        Assert.Contains("Archer (Civ) will be destroyed", kill.Lines);
        Assert.Contains("Warrior (Civ) will be destroyed", kill.Lines);
        Assert.Contains("Archer (Civ) health afterwards: 0", kill.Lines);
        var maybe = UnitOrdersModel.Combat(Battle(10, 40, 1, 9, defHealth: 20, attHealth: 5));
        Assert.Contains("Archer (Civ) may be destroyed", maybe.Lines);
        Assert.Contains("Warrior (Civ) may be destroyed", maybe.Lines);
    }

    [Fact]
    public void CombatOutcomesCaptureOccupyAndNoDamageToAttackerForRanged()
    {
        Assert.Contains("Captures Archer (Civ)", UnitOrdersModel.Combat(Battle(0, 0, 0, 0, outcome: "capture")).Lines);
        Assert.Contains("Takes Archer (Civ)", UnitOrdersModel.Combat(Battle(0, 0, 0, 0, outcome: "occupy")).Lines);
        Assert.Contains("Archer (Civ) cannot be captured", UnitOrdersModel.Combat(Battle(0, 0, 0, 0, outcome: "none")).Lines);
        var ranged = UnitOrdersModel.Combat(Battle(5, 5, 0, 0));
        Assert.Contains("Damage to Archer (Civ): 5", ranged.Lines);
        Assert.DoesNotContain(ranged.Lines, it => it.StartsWith("Damage to Warrior (Civ)", StringComparison.Ordinal));
    }

    [Fact]
    public void CombatCannotAttackDisablesConfirmAndNukeAndSweepHaveTheirOwnWording()
    {
        var blocked = UnitOrdersModel.Combat(Battle(1, 2, 0, 0, can: false));
        Assert.False(blocked.CanConfirm);
        Assert.Contains("Cannot attack right now", blocked.Lines);
        var nuke = UnitOrdersModel.Combat(new AttackPreviewResult("nuke", true, null, new TilePos(1, 1), null, null, null, 0, 0, 0, 0, false, false, 2));
        Assert.Equal("Nuclear strike (blast radius 2)", nuke.Title);
        Assert.Equal("Launch", nuke.ConfirmLabel);
        var sweep = UnitOrdersModel.Combat(new AttackPreviewResult("airsweep", true, null, new TilePos(1, 1), null, Fighter("Fighter", 100, 9), null, 0, 0, 0, 0, false, false, 0));
        Assert.Equal("Air sweep", sweep.Title);
        Assert.Equal("Sweep", sweep.ConfirmLabel);
    }

    [Fact]
    public void PanelStandingActionBeatsFortifiedAndEmptyActionFallsBackToFortified()
    {
        Assert.Equal("Sleep", UnitOrdersModel.Panel(Unit(Own(action: "Sleep"), fortified: true), null).Order);
        Assert.Equal("Fortified", UnitOrdersModel.Panel(Unit(Own(action: ""), fortified: true), null).Order);
        Assert.Equal("", UnitOrdersModel.Panel(Unit(Own(action: ""), fortified: false), null).Order);
    }

    [Fact]
    public void PanelMovesUseTwoDecimalsAtMostWithoutTrailingZeros()
    {
        var archer = new BaseUnit { Movement = 2 };
        Assert.Equal("Moves 1.25/2", UnitOrdersModel.Panel(Unit(Own(moves: 1.25f)), archer).Moves);
        Assert.Equal("Moves 1/2", UnitOrdersModel.Panel(Unit(Own(moves: 1f)), archer).Moves);
        Assert.Equal("Moves 0.33", UnitOrdersModel.Panel(Unit(Own(moves: 1f / 3f)), null).Moves);
    }

    [Fact]
    public void MenuImproveEntryJoinsProblemsAndCarriesKindAndArgument()
    {
        var menu = UnitOrdersModel.Menu(new UnitActionsResult(1, []), Tile(new ImprovementOption("Mine", ["Obsolete", "Needs tech"], []), new ImprovementOption("Farm", [], [])), null, hasMovement: false);
        Assert.Equal(2, menu.Count);
        Assert.Equal(MenuEntryKind.Improve, menu[0].Kind);
        Assert.Equal("Mine", menu[0].Argument);
        Assert.False(menu[0].Enabled);
        Assert.Equal("Obsolete, Needs tech", menu[0].Reason);
        Assert.Null(menu[0].Action);
        Assert.Equal(MenuEntryKind.Improve, menu[1].Kind);
        Assert.Equal("Farm", menu[1].Argument);
        Assert.True(menu[1].Enabled);
        Assert.Null(menu[1].Reason);
    }

    [Fact]
    public void MenuPromoteEntryHasKindArgumentAndReason()
    {
        var promotions = new PromotionChoicesResult(1, 12, 10, true, true,
            [new PromotionChoice("Accuracy II", true, [], []), new PromotionChoice("Barrage I", false, [], [])]);
        var menu = UnitOrdersModel.Menu(new UnitActionsResult(1, []), null, promotions, hasMovement: false);
        Assert.Equal(2, menu.Count);
        Assert.Equal(MenuEntryKind.Promote, menu[0].Kind);
        Assert.Equal("Accuracy II", menu[0].Argument);
        Assert.Null(menu[0].Reason);
        Assert.Null(menu[0].Action);
        Assert.Equal(MenuEntryKind.Promote, menu[1].Kind);
        Assert.Equal("Barrage I", menu[1].Argument);
        Assert.False(menu[1].Enabled);
        Assert.Equal("Not enough experience", menu[1].Reason);
    }

    [Fact]
    public void MenuEngineAndDisbandEntriesHaveTheirKinds()
    {
        var menu = UnitOrdersModel.Menu(new UnitActionsResult(1, [Action("Sleep")]), null, null, hasMovement: true);
        Assert.Equal([MenuEntryKind.Engine, MenuEntryKind.Disband], menu.Select(it => it.Kind));
        Assert.Null(menu[0].Argument);
        Assert.Null(menu[0].Reason);
        Assert.Null(menu[1].Action);
        Assert.Null(menu[1].Argument);
        Assert.True(menu[1].Enabled);
    }

    [Fact]
    public void ClickOnOwnUnitSelectsItWhateverElseIsTrue()
    {
        var attackable = new HashSet<(int X, int Y)> { (3, 3) };
        Assert.Equal(TileClickIntent.SelectOwn, UnitOrdersModel.ClickIntent((9, 9), true, false, attackable));
        Assert.Equal(TileClickIntent.SelectOwn, UnitOrdersModel.ClickIntent((3, 3), true, false, attackable));
        Assert.Equal(TileClickIntent.SelectOwn, UnitOrdersModel.ClickIntent((9, 9), true, true, attackable));
    }

    [Fact]
    public void CombatListsFightersAttackerFirstThenModifiersWithSignsThenOutcomeInExactOrder()
    {
        var preview = new AttackPreviewResult("battle", true, "damage", new TilePos(3, 3), new TilePos(2, 3),
            Fighter("Warrior", 100, 8, new StrengthModifier("Fortified", 25), new StrengthModifier("Terrain", 0)),
            Fighter("Archer", 90, 5, new StrengthModifier("Flanked", -10), new StrengthModifier("Rough", -125)),
            20, 30, 10, 14, false, false, 0);
        Assert.Equal(
            [
                "Warrior (Civ): strength 8, health 100/100",
                "    +25% Fortified",
                "    +0% Terrain",
                "Archer (Civ): strength 5, health 90/100",
                "    -10% Flanked",
                "    -125% Rough",
                "Damage to Archer (Civ): 20-30",
                "Damage to Warrior (Civ): 10-14",
                "Archer (Civ) health afterwards: 60-70",
            ],
            UnitOrdersModel.Combat(preview).Lines);
    }

    [Fact]
    public void CombatDefenderDestroyedBoundaries()
    {
        static IReadOnlyList<string> Destroyed(int minD, int maxD) =>
            UnitOrdersModel.Combat(Battle(minD, maxD, 0, 0, defHealth: 20)).Lines.Where(it => it.Contains("destroyed", StringComparison.Ordinal)).ToList();
        Assert.Equal(["Archer (Civ) will be destroyed"], Destroyed(20, 20));
        Assert.Equal(["Archer (Civ) will be destroyed"], Destroyed(20, 25));
        Assert.Equal(["Archer (Civ) may be destroyed"], Destroyed(19, 20));
        Assert.Equal(["Archer (Civ) may be destroyed"], Destroyed(5, 40));
        Assert.Empty(Destroyed(19, 19));
        Assert.Empty(Destroyed(0, 0));
    }

    [Fact]
    public void CombatAttackerDestroyedBoundaries()
    {
        static IReadOnlyList<string> Destroyed(int minA, int maxA) =>
            UnitOrdersModel.Combat(Battle(1, 1, minA, maxA, attHealth: 5)).Lines.Where(it => it.Contains("destroyed", StringComparison.Ordinal)).ToList();
        Assert.Equal(["Warrior (Civ) will be destroyed"], Destroyed(5, 5));
        Assert.Equal(["Warrior (Civ) will be destroyed"], Destroyed(5, 9));
        Assert.Equal(["Warrior (Civ) may be destroyed"], Destroyed(4, 5));
        Assert.Equal(["Warrior (Civ) may be destroyed"], Destroyed(1, 9));
        Assert.Empty(Destroyed(4, 4));
        Assert.Empty(Destroyed(1, 1));
    }

    [Fact]
    public void CombatBothDestroyedLinesDefenderFirst()
    {
        var lines = UnitOrdersModel.Combat(Battle(20, 20, 5, 5, defHealth: 20, attHealth: 5)).Lines;
        Assert.Equal(["Archer (Civ) will be destroyed", "Warrior (Civ) will be destroyed"], lines.Where(it => it.Contains("destroyed", StringComparison.Ordinal)));
        Assert.Equal("Archer (Civ) will be destroyed", lines[^2]);
        var mixed = UnitOrdersModel.Combat(Battle(20, 20, 4, 5, defHealth: 20, attHealth: 5)).Lines;
        Assert.Equal(["Archer (Civ) will be destroyed", "Warrior (Civ) may be destroyed"], mixed.Where(it => it.Contains("destroyed", StringComparison.Ordinal)));
    }

    [Fact]
    public void CombatRangeCollapsesOnlyWhenMinEqualsMax()
    {
        var lines = UnitOrdersModel.Combat(Battle(7, 3, 6, 6)).Lines;
        Assert.Contains("Damage to Archer (Civ): 7-3", lines);
        Assert.Contains("Damage to Warrior (Civ): 6", lines);
        Assert.Contains("Archer (Civ) health afterwards: 97-93", lines);
        Assert.Contains("Damage to Archer (Civ): 4", UnitOrdersModel.Combat(Battle(4, 4, 0, 0)).Lines);
    }

    [Fact]
    public void CombatNonBattleKindsWithBothFightersShowNoOutcomeLines()
    {
        foreach (var kind in new[] { "airsweep", "nuke", "other" })
        {
            var preview = new AttackPreviewResult(kind, true, "damage", new TilePos(3, 3), new TilePos(2, 3), Fighter("Fighter", 100, 9), Fighter("Archer", 100, 5), 20, 30, 10, 14, false, false, 0);
            Assert.Equal(["Fighter (Civ): strength 9, health 100/100", "Archer (Civ): strength 5, health 100/100"], UnitOrdersModel.Combat(preview).Lines);
        }
    }

    [Fact]
    public void CombatBattleWithoutBothFightersShowsNoOutcome()
    {
        var noDefender = new AttackPreviewResult("battle", true, "damage", new TilePos(3, 3), null, Fighter("Warrior", 100, 8), null, 20, 30, 10, 14, false, false, 0);
        Assert.Equal(["Warrior (Civ): strength 8, health 100/100"], UnitOrdersModel.Combat(noDefender).Lines);
        var noAttacker = new AttackPreviewResult("battle", true, "damage", new TilePos(3, 3), null, null, Fighter("Archer", 100, 5), 20, 30, 10, 14, false, false, 0);
        Assert.Equal(["Archer (Civ): strength 5, health 100/100"], UnitOrdersModel.Combat(noAttacker).Lines);
    }

    [Fact]
    public void CombatTitleUsesQuestionMarkForMissingFighters()
    {
        Assert.Equal("? attacks ?", UnitOrdersModel.Combat(new AttackPreviewResult("battle", true, null, new TilePos(1, 1), null, null, null, 0, 0, 0, 0, false, false, 0)).Title);
        Assert.Equal("Warrior attacks ?", UnitOrdersModel.Combat(new AttackPreviewResult("battle", true, null, new TilePos(1, 1), null, Fighter("Warrior", 100, 8), null, 0, 0, 0, 0, false, false, 0)).Title);
        Assert.Equal("? attacks Archer", UnitOrdersModel.Combat(new AttackPreviewResult("battle", true, null, new TilePos(1, 1), null, null, Fighter("Archer", 100, 5), 0, 0, 0, 0, false, false, 0)).Title);
        Assert.Equal("Attack", UnitOrdersModel.Combat(new AttackPreviewResult("battle", true, null, new TilePos(1, 1), null, null, null, 0, 0, 0, 0, false, false, 0)).ConfirmLabel);
    }

    [Fact]
    public void CombatFighterLineFormatsFinalStrengthHealthAndMaxHealth()
    {
        var fighter = new CombatantPreview("Bomber", "Rome", false, false, 55, 120, 17, 17, []);
        var sweep = new AttackPreviewResult("airsweep", true, null, new TilePos(1, 1), null, fighter, null, 0, 0, 0, 0, false, false, 0);
        Assert.Equal(["Bomber (Rome): strength 17, health 55/120"], UnitOrdersModel.Combat(sweep).Lines);
    }

    [Fact]
    public void CombatCannotAttackLineComesLast()
    {
        var lines = UnitOrdersModel.Combat(Battle(1, 2, 0, 0, can: false)).Lines;
        Assert.Equal("Cannot attack right now", lines[^1]);
        Assert.Equal(1, lines.Count(it => it == "Cannot attack right now"));
        Assert.DoesNotContain("Cannot attack right now", UnitOrdersModel.Combat(Battle(1, 2, 0, 0)).Lines);
    }

    [Fact]
    public void PanelRangedStrengthUsesRangeAndMovementFromTheirOwnFields()
    {
        var info = UnitOrdersModel.Panel(Unit(Own(moves: 2f)), new BaseUnit { Strength = 7, RangedStrength = 9, Range = 3, Movement = 4 });
        Assert.Equal("Ranged 9, range 3, melee 7", info.Strength);
        Assert.Equal("Moves 2/4", info.Moves);
    }

    [Fact]
    public void CombatShowsAttackerDamageWhenOnlyTheMaximumIsPositive()
    {
        Assert.Contains("Damage to Warrior (Civ): 0-3", UnitOrdersModel.Combat(Battle(5, 5, 0, 3)).Lines);
    }

    [Fact]
    public void CombatFighterLineShowsFinalNotBaseStrength()
    {
        var fighter = new CombatantPreview("Bomber", "Rome", false, false, 55, 120, 10, 17, []);
        var sweep = new AttackPreviewResult("airsweep", true, null, new TilePos(1, 1), null, fighter, null, 0, 0, 0, 0, false, false, 0);
        Assert.Equal(["Bomber (Rome): strength 17, health 55/120"], UnitOrdersModel.Combat(sweep).Lines);
    }

    [Fact]
    public void PanelCarriesTheStructuredNumbers()
    {
        var info = UnitOrdersModel.Panel(Unit(Own()), new BaseUnit { Name = "Archer", Strength = 5, RangedStrength = 6, Range = 2, Movement = 2 });
        Assert.Equal((5, 6, 2, 1.5f, 2, 0.8, 12, true), (info.Combat, info.Ranged, info.Range, info.MovesLeft, info.MovesMax, info.Fraction, info.XpValue, info.IsOwn));
        var foreign = UnitOrdersModel.Panel(Unit(null), null);
        Assert.Equal((0.75, -1, false), (foreign.Fraction, foreign.XpValue, foreign.IsOwn));
    }

    [Fact]
    public void MenuEntriesHaveUniqueKeysIconsAndPrimaryActionsFirstFlag()
    {
        var actions = new UnitActionsResult(1, [Action("Fortify"), Action("Found city", type: UnitActionType.FoundCity), Action("Upgrade to [Swordsman] ([50] gold)", type: UnitActionType.Upgrade)]);
        var promotions = new PromotionChoicesResult(1, 12, 10, true, true, [new PromotionChoice("Accuracy II", true, [], [])]);
        var menu = UnitOrdersModel.Menu(actions, Tile(new ImprovementOption("Farm", [], [])), promotions, hasMovement: true);
        Assert.Equal(menu.Count, menu.Select(it => it.Key).Distinct().Count());
        Assert.All(menu, it => Assert.NotNull(it.IconKey));
        Assert.Equal([false, true, true, false, false, false], menu.Select(it => it.Primary));
        Assert.Equal("Improve:Farm", menu[3].Key);
        Assert.Equal("Disband", menu[^1].Key);
    }

    [Fact]
    public void EveryActionTypeHasAnIconThatExistsInThePack()
    {
        foreach (var type in Enum.GetValues<UnitActionType>())
            Assert.Contains(UnitOrdersModel.ActionIconName(type), RulesetAssetKeys.ActionIconNames);
    }

    [Fact]
    public void CombatCardHasBothFightersWithPredictedHealthAndTheOutcome()
    {
        var card = UnitOrdersModel.Combat(Battle(20, 30, 10, 14)).Card;
        Assert.NotNull(card);
        Assert.Equal(("Warrior", 8, 86, 90), (card.Attacker!.Name, card.Attacker.Strength, card.Attacker.HealthMin, card.Attacker.HealthMax));
        Assert.Equal(("Archer", 5, 70, 80), (card.Defender!.Name, card.Defender.Strength, card.Defender.HealthMin, card.Defender.HealthMax));
        Assert.Equal(["+25% Fortified"], card.Attacker.Modifiers);
        Assert.Equal(["Damage to Archer (Civ): 20-30", "Damage to Warrior (Civ): 10-14"], card.Outcome);
    }

    [Fact]
    public void CombatCardForCaptureKeepsHealthAndAirSweepHasOneFighter()
    {
        var capture = UnitOrdersModel.Combat(Battle(0, 0, 0, 0, outcome: "capture")).Card!;
        Assert.Equal((100, 100), (capture.Defender!.HealthMin, capture.Defender.HealthMax));
        Assert.Equal(["Captures Archer (Civ)"], capture.Outcome);
        var sweep = new AttackPreviewResult("airsweep", false, null, new TilePos(1, 1), null, Fighter("Fighter", 100, 9), null, 0, 0, 0, 0, false, false, 0);
        var card = UnitOrdersModel.Combat(sweep).Card!;
        Assert.Null(card.Defender);
        Assert.Equal(["Cannot attack right now"], card.Outcome);
    }
}
