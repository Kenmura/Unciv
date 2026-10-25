using Unciv.AI.Data;
using Unciv.AI.Operations;
using Unciv.Logic.Map;

namespace Unciv.Core.Tests.AI.Operations;

public class OperationTests
{
    private static readonly OperationTunables T = OperationTunables.FromDefines(GlobalAIDefines.LoadEmbedded(), AIEdition.BNW);

    private static OperationUnit U(int id, int x, ArmyRole role = ArmyRole.Melee, int hp = 100) => new(id, new HexCoord(x, 0), hp, role);

    private static OperationManager Manager() => new(T);

    [Fact]
    public void TunablesComeFromTheDefines()
    {
        Assert.Equal(5, T.MaxRecruitTurnsDefault);
        Assert.Equal(10, T.MaxRecruitTurnsEnemyTerritory);
        Assert.Equal(80, T.PercentHealthForOperation);
        Assert.Equal(5, T.DeployRange(OperationType.BasicCityAttack));
        Assert.Equal(2, T.DeployRange(OperationType.DestroyBarbarianCamp));
        Assert.Equal(5, T.DeployRange(OperationType.PillageEnemy));
        Assert.Equal(1, T.DeployRange(OperationType.NavalBombardment));
    }

    [Fact]
    public void ChoosesForwardMusterOnlyWhenSafeEnough()
    {
        var staging = new HexCoord(0, 0);
        var forward = new HexCoord(9, 0);
        Assert.Equal(forward, AIOperation.ChooseMuster(staging, forward, 20, T));
        Assert.Equal(staging, AIOperation.ChooseMuster(staging, forward, 21, T));
    }

    [Fact]
    public void RecruitsNearestHealthyUnitPerRoleAndLocksIt()
    {
        var m = Manager();
        var op = m.Start(OperationType.BasicCityAttack, new HexCoord(30, 0), new HexCoord(2, 0), [ArmyRole.Melee, ArmyRole.Ranged]);
        m.Update([U(1, 9), U(2, 3), U(3, 1, hp: 50), U(4, 5, ArmyRole.Ranged)], _ => true);
        Assert.Equal([2, 4], op.UnitIds.ToArray());
        Assert.Equal([2, 4], m.LockedUnits.ToArray());
        Assert.Equal(OperationState.Gathering, op.State);
    }

    [Fact]
    public void SecondOperationCannotTakeLockedUnits()
    {
        var m = Manager();
        m.Start(OperationType.BasicCityAttack, new HexCoord(30, 0), new HexCoord(0, 0), [ArmyRole.Melee]);
        var second = m.Start(OperationType.PillageEnemy, new HexCoord(20, 0), new HexCoord(0, 0), [ArmyRole.Melee]);
        m.Update([U(1, 1), U(2, 2)], _ => true);
        Assert.Equal([1], m.Operations[0].UnitIds.ToArray());
        Assert.Equal([2], second.UnitIds.ToArray());
    }

    [Fact]
    public void MarchesThroughGatheringAndMovingToTarget()
    {
        var m = Manager();
        var op = m.Start(OperationType.DestroyBarbarianCamp, new HexCoord(20, 0), new HexCoord(5, 0), [ArmyRole.Melee]);
        var orders = m.Update([U(1, 0)], _ => true);
        Assert.Equal(OperationState.Gathering, op.State);
        Assert.Equal(new OperationOrder(op.Id, 1, new HexCoord(5, 0)), Assert.Single(orders));

        orders = m.Update([U(1, 4)], _ => true);   // within muster radius 1
        Assert.Equal(OperationState.Moving, op.State);
        Assert.Equal(new HexCoord(20, 0), Assert.Single(orders).Destination);

        orders = m.Update([U(1, 17)], _ => true);   // barbarian camp range 2: 3 away is not yet there
        Assert.Equal(OperationState.Moving, op.State);
        orders = m.Update([U(1, 18)], _ => true);
        Assert.Equal(OperationState.AtTarget, op.State);
        Assert.Empty(orders);
        Assert.Contains(1, m.LockedUnits);

        m.Update([U(1, 18)], _ => false);   // camp destroyed
        Assert.Equal(OperationState.Succeeded, op.State);
        Assert.Empty(m.Operations);
        Assert.Empty(m.LockedUnits);
    }

    [Fact]
    public void TargetVanishingEarlyAbortsAndReleases()
    {
        var m = Manager();
        var op = m.Start(OperationType.BasicCityAttack, new HexCoord(30, 0), new HexCoord(0, 0), [ArmyRole.Melee]);
        m.Update([U(1, 0)], _ => true);
        m.Update([U(1, 0)], _ => false);
        Assert.Equal(OperationState.Aborted, op.State);
        Assert.Empty(m.LockedUnits);
        Assert.Empty(op.UnitIds);
    }

    [Fact]
    public void RecruitingTimesOutAfterTheDefaultOrEnemyTerritoryLimit()
    {
        var m = Manager();
        var a = m.Start(OperationType.BasicCityAttack, new HexCoord(30, 0), new HexCoord(0, 0), [ArmyRole.Ranged]);
        var b = m.Start(OperationType.BasicCityAttack, new HexCoord(31, 0), new HexCoord(0, 0), [ArmyRole.Ranged], enemyTerritory: true);
        for (int turn = 1; turn <= 5; turn++) m.Update([U(1, 0)], _ => true);
        Assert.Equal(OperationState.Recruiting, a.State);
        m.Update([U(1, 0)], _ => true);
        Assert.Equal(OperationState.Aborted, a.State);
        Assert.Equal(OperationState.Recruiting, b.State);
        for (int turn = 7; turn <= 11; turn++) m.Update([U(1, 0)], _ => true);
        Assert.Equal(OperationState.Aborted, b.State);
        Assert.Empty(m.Operations);
    }

    [Fact]
    public void WoundedOrDeadUnitsLeaveTheirSlotAndAreReplaced()
    {
        var m = Manager();
        var op = m.Start(OperationType.BasicCityAttack, new HexCoord(30, 0), new HexCoord(0, 0), [ArmyRole.Melee, ArmyRole.Melee]);
        m.Update([U(1, 1), U(2, 2), U(3, 8)], _ => true);
        Assert.Equal(OperationState.Gathering, op.State);

        m.Update([U(1, 1, hp: 79), U(3, 8)], _ => true);   // 2 died, 1 is wounded: 3 fills one slot, 1 is released
        Assert.Equal(OperationState.Recruiting, op.State);
        Assert.Equal([3], op.UnitIds.ToArray());
        Assert.DoesNotContain(1, m.LockedUnits);
        Assert.DoesNotContain(2, m.LockedUnits);
    }

    [Fact]
    public void MarchingArmyIgnoresWoundsButLosingEveryoneAborts()
    {
        var m = Manager();
        var op = m.Start(OperationType.BasicCityAttack, new HexCoord(30, 0), new HexCoord(0, 0), [ArmyRole.Melee]);
        m.Update([U(1, 0)], _ => true);
        m.Update([U(1, 0)], _ => true);
        Assert.Equal(OperationState.Moving, op.State);
        m.Update([U(1, 3, hp: 10)], _ => true);
        Assert.Equal([1], op.UnitIds.ToArray());
        m.Update([], _ => true);
        Assert.Equal(OperationState.Aborted, op.State);
    }

    [Fact]
    public void ExistsIgnoresFinishedOperationsAndAbortReleases()
    {
        var m = Manager();
        var target = new HexCoord(30, 0);
        var op = m.Start(OperationType.CityStateAttack, target, new HexCoord(0, 0), [ArmyRole.Melee]);
        Assert.True(m.Exists(OperationType.CityStateAttack, target));
        Assert.False(m.Exists(OperationType.BasicCityAttack, target));
        m.Update([U(1, 0)], _ => true);
        op.Abort(new SortedSet<int>());
        op.Abort(new SortedSet<int>());
        Assert.False(m.Exists(OperationType.CityStateAttack, target));
        Assert.Throws<ArgumentException>(() => m.Start(OperationType.FoundCity, target, target, []));
    }
}
