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
        var targets = new AttackableTargetsResult("attack", true, [new AttackTarget(3, 3, "Warrior", "Rome", false, [])]);
        Assert.Equal([(3, 3)], UnitOrdersModel.AttackTiles(targets));
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
}
