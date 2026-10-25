using Unciv.AI.Data;
using Unciv.AI.Tactical;
using Unciv.Logic.Map;
using Unciv.Utils;

namespace Unciv.Core.Tests.AI.Tactical;

public class TacticalAITests
{
    private static readonly GlobalAIDefines Defines = GlobalAIDefines.LoadEmbedded();
    private static readonly IReadOnlyList<TacticalMoveDefinition> Moves = TacticalMoveDefinition.LoadEmbedded();

    private static TacticalMoveQueue NewQueue(AIEdition e = AIEdition.BNW) => new(Moves, Defines, e);

    [Fact]
    public void EveryTsvMoveMapsToAnEnumMemberAndBack()
    {
        foreach (var def in Moves)
        {
            var type = TacticalMoveTypeExtensions.Parse(def.Type);
            Assert.Equal(def.Type, type.ToTsvName());
            Assert.True(TacticalMoveTypeExtensions.TryParse(def.Type, out var again));
            Assert.Equal(type, again);
        }
        Assert.Equal(Moves.Count, Enum.GetValues<TacticalMoveType>().Length);
        Assert.Equal(TacticalMoveType.BastionOneTurn, TacticalMoveTypeExtensions.Parse("TACTICAL_BASTION_1_TURN"));
        Assert.False(TacticalMoveTypeExtensions.TryParse("TACTICAL_NOPE", out _));
        Assert.Throws<ArgumentException>(() => TacticalMoveTypeExtensions.Parse("TACTICAL_NOPE"));
    }

    [Fact]
    public void QueueWithoutRandomnessOrFlavorFollowsBasePriority()
    {
        var queue = NewQueue().Build(new TacticalFlavors(0, 0));
        Assert.Equal(TacticalMoveType.EmergencyPurchases, queue[0].Move);
        Assert.Equal(TacticalMoveType.CaptureCity, queue[1].Move);
        Assert.Equal(TacticalMoveType.DestroyHighUnit, queue[2].Move);
        Assert.Equal(queue.OrderByDescending(e => e.Priority).Select(e => e.Move), queue.Select(e => e.Move));
        // Priority ties keep table order: AIR_INTERCEPT and ATTACK_VERY_HIGH_PRIORITY_CIVILIAN are both 130
        int a = queue.ToList().FindIndex(e => e.Move == TacticalMoveType.AirIntercept);
        int b = queue.ToList().FindIndex(e => e.Move == TacticalMoveType.AttackVeryHighPriorityCivilian);
        Assert.True(a < b);
    }

    [Fact]
    public void QueueFiltersByEdition()
    {
        Assert.DoesNotContain(NewQueue(AIEdition.Base).Build(new TacticalFlavors(0, 0)), e => e.Move == TacticalMoveType.PillageCitadel);
        Assert.Contains(NewQueue(AIEdition.BNW).Build(new TacticalFlavors(0, 0)), e => e.Move == TacticalMoveType.PillageCitadel);
    }

    [Fact]
    public void FlavorsRaiseOffensiveAndDefensiveMovesByDampenedWeight()
    {
        var plain = NewQueue().Build(new TacticalFlavors(0, 0));
        var flavored = NewQueue().Build(new TacticalFlavors(10, 20));
        int Of(IReadOnlyList<TacticalQueueEntry> q, TacticalMoveType m) => q.Single(e => e.Move == m).Priority;
        Assert.Equal(Of(plain, TacticalMoveType.CaptureCity) + 3, Of(flavored, TacticalMoveType.CaptureCity));        // 0.3 * 10
        Assert.Equal(Of(plain, TacticalMoveType.DestroyHighUnit) + 6, Of(flavored, TacticalMoveType.DestroyHighUnit)); // 0.3 * 20
        Assert.Equal(Of(plain, TacticalMoveType.AncientRuins) + 4, Of(flavored, TacticalMoveType.AncientRuins));       // 0.3 * (50*10+50*20)/100 = 4.5 -> 4
        // weight 50 * flavor 7 = 350 -> 0.3 * 3.5 = 1.05 -> 1 (no early integer division)
        Assert.Equal(Of(plain, TacticalMoveType.AncientRuins) + 1, Of(NewQueue().Build(new TacticalFlavors(7, 0)), TacticalMoveType.AncientRuins));
        Assert.Equal(Of(plain, TacticalMoveType.EmergencyPurchases), Of(flavored, TacticalMoveType.EmergencyPurchases));
    }

    [Fact]
    public void RandomJitterIsSeededAndBounded()
    {
        var a = NewQueue().Build(new TacticalFlavors(0, 0), new GameRandom(42));
        var b = NewQueue().Build(new TacticalFlavors(0, 0), new GameRandom(42));
        Assert.Equal(a, b);
        var plain = NewQueue().Build(new TacticalFlavors(0, 0));
        foreach (var e in a)
        {
            int baseP = plain.Single(p => p.Move == e.Move).Priority;
            Assert.InRange(e.Priority - baseP, 0, 5);
        }
        Assert.Contains(a, e => e.Priority > plain.Single(p => p.Move == e.Move).Priority);
        var c = NewQueue().Build(new TacticalFlavors(0, 0), new GameRandom(43));
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void NegativeDamageUnitsAreNeverRecruited()
    {
        var target = new TacticalTarget(TacticalMoveType.CaptureCity, new HexCoord(0, 0), 1, 10);
        var r = Recruiter.Recruit(target, [new TacticalUnit(1, new HexCoord(1, 0), -5, false)], new HashSet<int>());
        Assert.Empty(r.Attackers);
    }

    [Fact]
    public void RunHandsEachMoveItsTargetsBestFirstInQueueOrder()
    {
        var targets = new TacticalTargetList();
        var lowCity = new TacticalTarget(TacticalMoveType.CaptureCity, new HexCoord(1, 1), 3);
        var highCity = new TacticalTarget(TacticalMoveType.CaptureCity, new HexCoord(2, 2), 9);
        var tie = new TacticalTarget(TacticalMoveType.CaptureCity, new HexCoord(3, 3), 3);
        var unit = new TacticalTarget(TacticalMoveType.DestroyHighUnit, new HexCoord(4, 4), 1, 30);
        foreach (var t in new[] { lowCity, highCity, tie, unit }) targets.Add(t);
        Assert.Equal(4, targets.Count);

        var seen = new List<(TacticalMoveType, IReadOnlyList<TacticalTarget>)>();
        TacticalMoveQueue.Run(NewQueue().Build(new TacticalFlavors(0, 0)), targets, (m, t) => seen.Add((m, t)));

        Assert.Equal(NewQueue().Build(new TacticalFlavors(0, 0)).Select(e => e.Move), seen.Select(s => s.Item1));
        Assert.Empty(seen[0].Item2);
        Assert.Equal([highCity, lowCity, tie], seen.Single(s => s.Item1 == TacticalMoveType.CaptureCity).Item2);
        Assert.Equal([unit], seen.Single(s => s.Item1 == TacticalMoveType.DestroyHighUnit).Item2);

        targets.Clear();
        Assert.Equal(0, targets.Count);
    }

    private static TacticalRecruiter Recruiter => TacticalRecruiter.FromDefines(Defines, AIEdition.BNW);

    [Fact]
    public void RecruiterReadsRangeAndOverkillFromDefines()
    {
        Assert.Equal(8, Recruiter.RecruitRange);
        Assert.Equal(25, Recruiter.OverkillPercent);
        Assert.Equal(10, TacticalRecruiter.FromDefines(Defines, AIEdition.Base).RecruitRange);
    }

    [Fact]
    public void RecruiterSpendsRangedBeforeMeleeUntilTheOverkillBudgetIsMet()
    {
        var target = new TacticalTarget(TacticalMoveType.DestroyHighUnit, new HexCoord(0, 0), 1, 40);   // budget 50
        var units = new[]
        {
            new TacticalUnit(1, new HexCoord(1, 0), 30, false),
            new TacticalUnit(2, new HexCoord(2, 0), 20, true),
            new TacticalUnit(3, new HexCoord(3, 0), 25, true),
            new TacticalUnit(4, new HexCoord(1, 1), 10, false),
        };
        var assigned = new HashSet<int>();
        var r = Recruiter.Recruit(target, units, assigned);
        Assert.Equal([3, 2, 1], r.Attackers.Select(u => u.Id).ToArray());   // 25, 20 ranged then melee 30 -> -25
        Assert.True(r.Satisfied);
        Assert.Equal(-25, r.RemainingBudget);
        Assert.Equal(new[] { 1, 2, 3 }, assigned.OrderBy(x => x));

        // a second target cannot take the same units
        var r2 = Recruiter.Recruit(target, units, assigned);
        Assert.Equal([4], r2.Attackers.Select(u => u.Id).ToArray());
        Assert.False(r2.Satisfied);
        Assert.Equal(40, r2.RemainingBudget);
    }

    [Fact]
    public void RecruiterRespectsRangeOperationsAndZeroDamage()
    {
        var target = new TacticalTarget(TacticalMoveType.CaptureCity, new HexCoord(0, 0), 1, 10);
        var units = new[]
        {
            new TacticalUnit(1, new HexCoord(8, 0), 50, false),            // exactly in range
            new TacticalUnit(2, new HexCoord(9, 0), 50, false),            // out of range
            new TacticalUnit(3, new HexCoord(1, 0), 50, false, InOperation: true),
            new TacticalUnit(4, new HexCoord(1, 0), 0, true),              // cannot hurt
        };
        var plain = Recruiter.Recruit(target, units, new HashSet<int>(), operationsCanRecruit: false);
        Assert.Equal([1], plain.Attackers.Select(u => u.Id).ToArray());
        var withOps = Recruiter.Recruit(target, units, new HashSet<int>(), operationsCanRecruit: true);
        Assert.Equal([3], withOps.Attackers.Select(u => u.Id).ToArray());   // nearer wins the tie on damage
    }

    [Fact]
    public void RecruiterBreaksTiesByDistanceThenIdAndHandlesEmptyOrHugeTargets()
    {
        var target = new TacticalTarget(TacticalMoveType.DestroyLowUnit, new HexCoord(0, 0), 1, int.MaxValue);
        var units = new[]
        {
            new TacticalUnit(7, new HexCoord(2, 0), 5, true),
            new TacticalUnit(5, new HexCoord(2, 0), 5, true),
            new TacticalUnit(9, new HexCoord(1, 0), 5, true),
        };
        var r = Recruiter.Recruit(target, units, new HashSet<int>());
        Assert.Equal([9, 5, 7], r.Attackers.Select(u => u.Id).ToArray());
        Assert.False(r.Satisfied);

        var none = Recruiter.Recruit(target, [], new HashSet<int>());
        Assert.Empty(none.Attackers);
        var zeroHp = Recruiter.Recruit(target with { TargetHp = 0 }, units, new HashSet<int>());
        Assert.Empty(zeroHp.Attackers);
        Assert.True(zeroHp.Satisfied);
    }

    [Fact]
    public void ArgumentsAreValidated()
    {
        var target = new TacticalTarget(TacticalMoveType.Heal, new HexCoord(0, 0), 0);
        Assert.Throws<ArgumentNullException>(() => Recruiter.Recruit(null!, [], new HashSet<int>()));
        Assert.Throws<ArgumentNullException>(() => Recruiter.Recruit(target, null!, new HashSet<int>()));
        Assert.Throws<ArgumentNullException>(() => Recruiter.Recruit(target, [], null!));
        Assert.Throws<ArgumentNullException>(() => TacticalRecruiter.FromDefines(null!, AIEdition.BNW));
        Assert.Throws<ArgumentNullException>(() => new TacticalMoveQueue(null!, Defines, AIEdition.BNW));
        Assert.Throws<ArgumentNullException>(() => new TacticalMoveQueue(Moves, null!, AIEdition.BNW));
        Assert.Throws<ArgumentNullException>(() => NewQueue().Build(null!));
        Assert.Throws<ArgumentNullException>(() => new TacticalTargetList().Add(null!));
        Assert.Throws<ArgumentNullException>(() => TacticalMoveQueue.Run(null!, new TacticalTargetList(), (_, _) => { }));
        Assert.Throws<ArgumentNullException>(() => TacticalMoveQueue.Run([], null!, (_, _) => { }));
        Assert.Throws<ArgumentNullException>(() => TacticalMoveQueue.Run([], new TacticalTargetList(), null!));
    }
}
