using Unciv.Logic.Map;

namespace Unciv.AI.Tactical;

/// <summary>A unit available for tactical attacks, before anyone has estimated what it would do to a particular target.</summary>
public sealed record TacticalAttacker(int Id, HexCoord Position, bool IsRanged, bool InOperation = false);

/// <summary>
/// The per-unit damage estimator (SPEC.md section 6.5 leaves it unknown, [?]). The engine supplies it; it must be
/// deterministic and return the expected damage dealt to the target, or zero or less when the unit cannot attack it.
/// </summary>
public interface ITacticalDamageEstimator
{
    int EstimateDamage(TacticalAttacker attacker, TacticalTarget target);
}

/// <summary>What was decided for one target. Attackers is empty when the target was skipped.</summary>
public sealed record TacticalAssignment(
    TacticalTarget Target,
    IReadOnlyList<TacticalUnit> Attackers,
    int RemainingBudget,
    bool Kills,
    IReadOnlyList<HexCoord> StagingTiles);

/// <summary>All assignments, in target order, and the attackers nobody needed (free for other tactical goals).</summary>
public sealed record OverkillPlan(IReadOnlyList<TacticalAssignment> Assignments, IReadOnlyList<TacticalAttacker> Unassigned);

/// <summary>
/// Coordinates several units against a list of targets (SPEC.md section 6.5). For each target, in the order given,
/// it asks the estimator for every free attacker's damage, lets <see cref="TacticalRecruiter"/> pick units until the
/// overkill budget is met, then drops any recruit the budget does not need (weakest first) so no unit is over-allocated.
/// With <c>requireKill</c> a target whose recruits cannot reach its full hit points is skipped and nobody is committed
/// to it. The neighbouring free tiles of a handled target (up to six) are reported as staging tiles, and each tile
/// is offered to one target only. Tiles holding an attacker or a target are never staged; callers add other occupied tiles.
/// Trimming is this port's own design (the original lets the last recruit overshoot); attacker ids must be unique.
/// </summary>
public sealed class OverkillAssigner(TacticalRecruiter recruiter, ITacticalDamageEstimator estimator, bool requireKill = true)
{
    private readonly TacticalRecruiter recruiter = recruiter ?? throw new ArgumentNullException(nameof(recruiter));
    private readonly ITacticalDamageEstimator estimator = estimator ?? throw new ArgumentNullException(nameof(estimator));

    public OverkillPlan Assign(
        IEnumerable<TacticalTarget> targets,
        IEnumerable<TacticalAttacker> attackers,
        IEnumerable<HexCoord>? occupiedTiles = null,
        bool operationsCanRecruit = true)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(attackers);

        var pool = attackers.ToList();
        var taken = new HashSet<int>();
        var blocked = new HashSet<HexCoord>(occupiedTiles ?? []);
        foreach (var a in pool) blocked.Add(a.Position);
        var targetList = targets.ToList();
        foreach (var t in targetList)
        {
            ArgumentNullException.ThrowIfNull(t);
            blocked.Add(t.Position);
        }
        var results = new List<TacticalAssignment>();

        foreach (var target in targetList)
        {
            results.Add(AssignOne(target, pool, taken, blocked, operationsCanRecruit));
        }

        return new OverkillPlan(results, pool.Where(a => !taken.Contains(a.Id)).ToList());
    }

    private TacticalAssignment AssignOne(
        TacticalTarget target, List<TacticalAttacker> pool, HashSet<int> taken, HashSet<HexCoord> blocked, bool operationsCanRecruit)
    {
        var candidates = pool
            .Where(a => !taken.Contains(a.Id) && (operationsCanRecruit || !a.InOperation)
                && HexMath.GetDistance(a.Position, target.Position) <= recruiter.RecruitRange)
            .Select(a => new TacticalUnit(a.Id, a.Position, estimator.EstimateDamage(a, target), a.IsRanged, a.InOperation))
            .ToList();

        var scratch = new HashSet<int>(taken);
        var recruitment = recruiter.Recruit(target, candidates, scratch, operationsCanRecruit);
        var chosen = Trim(recruitment.Attackers.ToList(), target);

        long total = chosen.Sum(u => (long)u.ExpectedDamage);
        bool kills = target.TargetHp > 0 && chosen.Count > 0 && total >= target.TargetHp;
        if (chosen.Count == 0 || (requireKill && !kills))
            return new TacticalAssignment(target, [], (int)Math.Clamp(Budget(target), int.MinValue, int.MaxValue), false, []);

        foreach (var u in chosen) taken.Add(u.Id);
        int budget = (int)Math.Clamp(Budget(target) - total, int.MinValue, int.MaxValue);
        return new TacticalAssignment(target, chosen, budget, kills, Stage(target, blocked));
    }

    private long Budget(TacticalTarget target) => ((long)recruiter.OverkillPercent + 100) * target.TargetHp / 100;

    /// <summary>Removes recruits the budget can do without, weakest first (then highest id), keeping the acting order.</summary>
    private List<TacticalUnit> Trim(List<TacticalUnit> chosen, TacticalTarget target)
    {
        long budget = Budget(target);
        long total = chosen.Sum(u => (long)u.ExpectedDamage);
        foreach (var u in chosen.OrderBy(c => c.ExpectedDamage).ThenByDescending(c => c.Id).ToList())
        {
            if (total - u.ExpectedDamage < budget) continue;
            total -= u.ExpectedDamage;
            chosen.Remove(u);
        }
        return chosen;
    }

    private static List<HexCoord> Stage(TacticalTarget target, HashSet<HexCoord> blocked)
    {
        var tiles = new List<HexCoord>();
        foreach (var tile in HexMath.GetHexCoordsAtDistance(target.Position, 1, 1, false))
        {
            if (blocked.Contains(tile) || tile == target.Position) continue;
            blocked.Add(tile);
            tiles.Add(tile);
        }
        return tiles;
    }
}
