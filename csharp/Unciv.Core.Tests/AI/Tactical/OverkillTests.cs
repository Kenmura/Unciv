using Unciv.AI.Data;
using Unciv.AI.Tactical;
using Unciv.Logic.Map;

namespace Unciv.Core.Tests.AI.Tactical;

public class OverkillTests
{
    private sealed class TableEstimator(Dictionary<int, int> damage) : ITacticalDamageEstimator
    {
        public int EstimateDamage(TacticalAttacker attacker, TacticalTarget target) => damage.GetValueOrDefault(attacker.Id);
    }

    private static readonly TacticalRecruiter Recruiter = TacticalRecruiter.FromDefines(GlobalAIDefines.LoadEmbedded(), AIEdition.BNW);   // range 8, overkill 25

    private static OverkillAssigner Assigner(Dictionary<int, int> damage, bool requireKill = true) =>
        new(Recruiter, new TableEstimator(damage), requireKill);

    private static TacticalTarget Target(int x, int hp) => new(TacticalMoveType.DestroyHighUnit, new HexCoord(x, 0), 1, hp);
    private static TacticalAttacker Unit(int id, int x, bool ranged = false, bool op = false) => new(id, new HexCoord(x, 1), ranged, op);

    [Fact]
    public void GroupsJustEnoughAttackersAndFreesTheRest()
    {
        // hp 40 -> budget 50: 30 + 25 is enough, the 20 and the 5 are left for other goals
        var plan = Assigner(new() { [1] = 30, [2] = 25, [3] = 20, [4] = 5 })
            .Assign([Target(0, 40)], [Unit(1, 1), Unit(2, 2), Unit(3, 3), Unit(4, 4)]);
        var a = Assert.Single(plan.Assignments);
        Assert.Equal([1, 2], a.Attackers.Select(u => u.Id).ToArray());
        Assert.True(a.Kills);
        Assert.Equal(-5, a.RemainingBudget);
        Assert.Equal([3, 4], plan.Unassigned.Select(u => u.Id).ToArray());
    }

    [Fact]
    public void TrimsRecruitsTheBudgetDoesNotNeed()
    {
        // ranged 5 is taken first, then melee 60 alone covers the 50 budget: the 5 is redundant
        var plan = Assigner(new() { [1] = 5, [2] = 60 }).Assign([Target(0, 40)], [Unit(1, 1, ranged: true), Unit(2, 2)]);
        Assert.Equal([2], plan.Assignments[0].Attackers.Select(u => u.Id).ToArray());
        Assert.Equal([1], plan.Unassigned.Select(u => u.Id).ToArray());
    }

    [Fact]
    public void SecondTargetNeverReusesCommittedUnits()
    {
        var plan = Assigner(new() { [1] = 50, [2] = 50, [3] = 50 })
            .Assign([Target(0, 40), Target(5, 40)], [Unit(1, 1), Unit(2, 2), Unit(3, 3)]);
        Assert.Equal([1], plan.Assignments[0].Attackers.Select(u => u.Id).ToArray());
        Assert.Equal([3], plan.Assignments[1].Attackers.Select(u => u.Id).ToArray());   // equal damage: nearest to (5,0)
        Assert.Equal([2], plan.Unassigned.Select(u => u.Id).ToArray());
    }

    [Fact]
    public void UnkillableTargetIsSkippedWithoutSpendingUnitsWhenKillIsRequired()
    {
        var damage = new Dictionary<int, int> { [1] = 10, [2] = 10, [3] = 30 };
        var units = new[] { Unit(1, 1), Unit(2, 2) };
        var plan = Assigner(damage).Assign([Target(0, 100), Target(3, 15)], units);
        Assert.Empty(plan.Assignments[0].Attackers);
        Assert.False(plan.Assignments[0].Kills);
        Assert.Empty(plan.Assignments[0].StagingTiles);
        Assert.Equal([1, 2], plan.Assignments[1].Attackers.Select(u => u.Id).OrderBy(i => i).ToArray());   // 10+10 >= 15, budget 18.75 -> 18
        Assert.True(plan.Assignments[1].Kills);

        var soft = Assigner(damage, requireKill: false).Assign([Target(0, 100)], units);
        Assert.Equal(2, soft.Assignments[0].Attackers.Count);
        Assert.False(soft.Assignments[0].Kills);
        Assert.Empty(soft.Unassigned);
    }

    [Fact]
    public void RespectsRangeOperationsAndZeroDamage()
    {
        var far = new TacticalAttacker(1, new HexCoord(50, 50), false);
        var plan = Assigner(new() { [1] = 99, [2] = 99, [3] = 0 })
            .Assign([Target(0, 10)], [far, Unit(2, 1, op: true), Unit(3, 2)], operationsCanRecruit: false);
        Assert.Empty(plan.Assignments[0].Attackers);
        Assert.Equal(3, plan.Unassigned.Count);
        var withOps = Assigner(new() { [2] = 99 }).Assign([Target(0, 10)], [Unit(2, 1, op: true)]);
        Assert.Equal([2], withOps.Assignments[0].Attackers.Select(u => u.Id).ToArray());
    }

    [Fact]
    public void StagingTilesAreSixFreeNeighboursOfferedOncePerTile()
    {
        var damage = new Dictionary<int, int> { [1] = 99, [2] = 99 };
        var target = Target(10, 10);
        var occupied = new[] { new HexCoord(10, 1) };   // one neighbour is taken; attacker and target tiles are excluded automatically
        var plan = Assigner(damage).Assign([target, Target(11, 10)], [Unit(1, 10), Unit(2, 11)], occupied);
        var first = plan.Assignments[0].StagingTiles;
        var second = plan.Assignments[1].StagingTiles;
        Assert.Equal(3, first.Count);   // 6 neighbours minus the occupied tile, the other target and the other attacker
        Assert.DoesNotContain(new HexCoord(10, 1), first);
        Assert.DoesNotContain(target.Position, first);
        Assert.All(first, t => Assert.Equal(1, HexMath.GetDistance(t, target.Position)));
        Assert.Empty(first.Intersect(second));
        Assert.DoesNotContain(plan.Assignments[1].Target.Position, second);
        var free = Assigner(damage).Assign([target], [Unit(1, 10)]);
        Assert.Equal(5, free.Assignments[0].StagingTiles.Count);   // the attacker stands on one neighbour
        Assert.Equal(free.Assignments[0].StagingTiles, Assigner(damage).Assign([target], [Unit(1, 10)]).Assignments[0].StagingTiles);
    }

    [Fact]
    public void SkippedTargetReportsFullBudgetAndStagingExcludesUnitsAndTargets()
    {
        var plan = Assigner(new() { [1] = 10 }).Assign([Target(0, 100)], [Unit(1, 1)]);
        Assert.Equal(125, plan.Assignments[0].RemainingBudget);
        var staged = Assigner(new() { [1] = 99 }).Assign([Target(0, 10)], [new TacticalAttacker(1, new HexCoord(1, 0), false)]);
        Assert.DoesNotContain(new HexCoord(1, 0), staged.Assignments[0].StagingTiles);
        Assert.Equal(5, staged.Assignments[0].StagingTiles.Count);
        var exact = Assigner(new() { [1] = 8, [2] = 5 }).Assign([Target(0, 10)], [Unit(1, 1), Unit(2, 2)]);   // budget 12: both needed
        Assert.Equal(2, exact.Assignments[0].Attackers.Count);
        Assert.Equal(-1, exact.Assignments[0].RemainingBudget);
    }

    [Fact]
    public void EdgeCasesAndArguments()
    {
        var a = Assigner(new() { [1] = 50 });
        var empty = a.Assign([], []);
        Assert.Empty(empty.Assignments);
        Assert.Empty(empty.Unassigned);
        Assert.Empty(a.Assign([Target(0, 0)], [Unit(1, 1)]).Assignments[0].Attackers);
        Assert.Empty(a.Assign([Target(0, -5)], [Unit(1, 1)]).Assignments[0].Attackers);
        Assert.Empty(a.Assign([Target(0, 10)], []).Assignments[0].Attackers);
        var huge = a.Assign([Target(0, int.MaxValue)], [Unit(1, 1)]);
        Assert.False(huge.Assignments[0].Kills);
        Assert.Throws<ArgumentNullException>(() => a.Assign(null!, []));
        Assert.Throws<ArgumentNullException>(() => a.Assign([], null!));
        Assert.Throws<ArgumentNullException>(() => a.Assign([null!], []));
        Assert.Throws<ArgumentNullException>(() => new OverkillAssigner(null!, new TableEstimator([])));
        Assert.Throws<ArgumentNullException>(() => new OverkillAssigner(Recruiter, null!));
    }
}
