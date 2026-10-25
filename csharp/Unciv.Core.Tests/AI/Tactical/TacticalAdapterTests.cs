using Unciv.AI.Data;
using Unciv.AI.Tactical;
using Unciv.Core.Tests.Support;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;

namespace Unciv.Core.Tests.AI.Tactical;

[Collection("RulesetCache")]
public sealed class TacticalAdapterTests
{
    private readonly TestGame game = new();
    private readonly Civilization me;
    private readonly Civilization foe;

    public TacticalAdapterTests()
    {
        game.MakeHexagonalMap(4);
        me = game.AddCiv();
        foe = game.AddCiv();
        game.GameInfo.CurrentPlayerCiv = me;
        game.GameInfo.CurrentPlayer = me.CivId;
        me.DiplomacyFunctions.MakeCivilizationsMeet(foe);
    }

    private void War() => me.GetDiplomacyManager(foe)!.DeclareWar();
    private void SeeAll() => me.ViewableTiles = game.TileMap.Values.ToHashSet();

    private static TacticalRecruiter Recruiter() => new(8, 25);

    [Fact]
    public void NoTargetsWithoutWarOrVision()
    {
        game.AddUnit("Warrior", foe, game.GetTile(1, 0));
        SeeAll();
        Assert.Equal(0, TacticalAdapter.ScanTargets(me).Count);
        War();
        me.ViewableTiles = new HashSet<Unciv.Logic.Map.Tiles.Tile>();
        Assert.Equal(0, TacticalAdapter.ScanTargets(me).Count);
    }

    [Fact]
    public void ScansUnitsCiviliansAndCities()
    {
        game.AddCity(foe, game.GetTile(3, 0));
        game.AddUnit("Warrior", foe, game.GetTile(0, 1));
        game.AddUnit("Archer", foe, game.GetTile(0, 2));
        game.AddUnit("Settler", foe, game.GetTile(-2, 0));
        game.AddUnit("Worker", foe, game.GetTile(-3, 0));
        game.AddUnit("Warrior", me, game.GetTile(0, 0));
        War();
        SeeAll();
        var t = TacticalAdapter.ScanTargets(me);
        Assert.Single(t.For(TacticalMoveType.CaptureCity));
        Assert.Single(t.For(TacticalMoveType.AttackVeryHighPriorityCivilian));
        Assert.Single(t.For(TacticalMoveType.AttackHighPriorityCivilian));
        Assert.Single(t.For(TacticalMoveType.DestroyMediumUnit));
        Assert.Single(t.For(TacticalMoveType.DestroyLowUnit));
        Assert.Equal(new HexCoord(0, 2), t.For(TacticalMoveType.DestroyMediumUnit)[0].Position);
    }

    [Fact]
    public void UnitsNearOurCitiesAreHighPriorityAndWeakerRankFirst()
    {
        game.AddCity(me, game.GetTile(0, 0));
        War();
        var weak = game.AddUnit("Warrior", foe, game.GetTile(1, 0));
        weak.Health = 30;
        game.AddUnit("Warrior", foe, game.GetTile(0, 1));
        SeeAll();
        var high = TacticalAdapter.ScanTargets(me).For(TacticalMoveType.DestroyHighUnit);
        Assert.Equal(2, high.Count);
        Assert.Equal(new HexCoord(1, 0), high[0].Position);
        Assert.Equal(30, high[0].TargetHp);
    }

    [Fact]
    public void AttackersListOnlyMilitaryUnitsThatCanAttack()
    {
        game.AddUnit("Warrior", me, game.GetTile(0, 0));
        game.AddUnit("Archer", me, game.GetTile(1, 0));
        game.AddUnit("Worker", me, game.GetTile(2, 0));
        var spent = game.AddUnit("Warrior", me, game.GetTile(3, 0));
        spent.CurrentMovement = 0;
        var list = TacticalAdapter.GetAttackers(me);
        Assert.Equal(2, list.Count);
        Assert.False(list[0].IsRanged);
        Assert.True(list[1].IsRanged);
    }

    [Fact]
    public void EstimatorUsesCombatFormulaAndRejectsNonEnemies()
    {
        var mine = game.AddUnit("Warrior", me, game.GetTile(0, 0));
        game.AddUnit("Warrior", foe, game.GetTile(1, 0));
        var est = new TacticalAdapter.DamageEstimator(me);
        var attacker = new TacticalAttacker(mine.Id, mine.CurrentTile.Position, false);
        var target = new TacticalTarget(TacticalMoveType.DestroyLowUnit, new HexCoord(1, 0), 0, 100);
        Assert.Equal(0, est.EstimateDamage(attacker, target));   // not at war
        War();
        Assert.True(est.EstimateDamage(attacker, target) > 0);
        Assert.Equal(0, est.EstimateDamage(attacker, target with { Position = new HexCoord(2, 2) }));   // empty tile
        Assert.Equal(0, est.EstimateDamage(attacker with { Id = 9999 }, target));   // vanished unit
    }

    [Fact]
    public void ExecuteAttacksHurtsTheTarget()
    {
        var mine = game.AddUnit("Warrior", me, game.GetTile(0, 0));
        var enemy = game.AddUnit("Warrior", foe, game.GetTile(1, 0));
        War();
        SeeAll();
        var targets = TacticalAdapter.ScanTargets(me).For(TacticalMoveType.DestroyLowUnit);
        var assigner = new OverkillAssigner(Recruiter(), new TacticalAdapter.DamageEstimator(me), requireKill: false);
        var plan = assigner.Assign(targets, TacticalAdapter.GetAttackers(me));
        Assert.Equal(1, TacticalAdapter.ExecuteAttacks(me, plan));
        Assert.True(enemy.Health < 100 || enemy.IsDestroyed);
        Assert.True(mine.CurrentMovement < mine.GetMaxMovement());
    }

    [Fact]
    public void ExecuteSkipsVanishedAndExhaustedUnitsAndStopsWhenTargetGone()
    {
        var mine = game.AddUnit("Warrior", me, game.GetTile(0, 0));
        var other = game.AddUnit("Warrior", me, game.GetTile(0, 1));
        game.AddUnit("Warrior", foe, game.GetTile(1, 0));
        War();
        var target = new TacticalTarget(TacticalMoveType.DestroyLowUnit, new HexCoord(1, 0), 0, 100);
        var units = new[]
        {
            new TacticalUnit(9999, HexCoord.Zero, 10, false),
            new TacticalUnit(mine.Id, mine.CurrentTile.Position, 10, false),
            new TacticalUnit(other.Id, other.CurrentTile.Position, 10, false),
        };
        mine.CurrentMovement = 0;
        var plan = new OverkillPlan([new TacticalAssignment(target, units, 0, false, [])], []);
        Assert.Equal(1, TacticalAdapter.ExecuteAttacks(me, plan));
        // target tile now empty: nothing more happens
        game.GetTile(1, 0).MilitaryUnit!.RemoveFromTile();
        Assert.Equal(0, TacticalAdapter.ExecuteAttacks(me, plan));
    }

    [Fact]
    public void ExecuteSkipsUnitsThatCannotReachTarget()
    {
        var far = game.AddUnit("Warrior", me, game.GetTile(-4, 0));
        game.AddUnit("Warrior", foe, game.GetTile(4, 0));
        War();
        var target = new TacticalTarget(TacticalMoveType.DestroyLowUnit, new HexCoord(4, 0), 0, 100);
        var plan = new OverkillPlan([new TacticalAssignment(target, [new TacticalUnit(far.Id, far.CurrentTile.Position, 10, false)], 0, false, [])], []);
        Assert.Equal(0, TacticalAdapter.ExecuteAttacks(me, plan));
    }

    [Fact]
    public void DoTurnAttacksAndIsAMoveQueueClient()
    {
        game.AddUnit("Warrior", me, game.GetTile(0, 0));
        game.AddUnit("Archer", me, game.GetTile(-1, 0));
        var enemy = game.AddUnit("Warrior", foe, game.GetTile(1, 0));
        enemy.Health = 5;   // the assigner skips targets it cannot kill
        War();
        SeeAll();
        var queue = new TacticalMoveQueue(TacticalMoveDefinition.LoadEmbedded(), GlobalAIDefines.LoadEmbedded(), AIEdition.BNW);
        int attacks = TacticalAdapter.DoTurn(me, Recruiter(), queue, new TacticalFlavors(0, 0));
        Assert.True(attacks >= 1);
        Assert.True(enemy.Health < 100 || enemy.IsDestroyed);
    }
}
