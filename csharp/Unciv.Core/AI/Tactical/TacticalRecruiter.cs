using Unciv.AI.Data;
using Unciv.Logic.Map;

namespace Unciv.AI.Tactical;

/// <summary>A unit that could be thrown at a target: where it is, what it would deal, and how it may be used.</summary>
public sealed record TacticalUnit(int Id, HexCoord Position, int ExpectedDamage, bool IsRanged, bool InOperation = false);

/// <summary>The units chosen for one target, in the order they should act, and the damage budget left over.</summary>
public sealed record TacticalRecruitment(IReadOnlyList<TacticalUnit> Attackers, int RemainingBudget)
{
    public bool Satisfied => RemainingBudget <= 0;
}

/// <summary>
/// Picks, for one target, which of the civilization's units to commit (SPEC.md section 6.5, "overkill" assignment).
/// The budget is (OVERKILL_PERCENT + 100)% of the target's hit points; ranged units are spent first, then melee,
/// each pass strongest first and, on equal damage, nearest then lowest id. Only units within
/// AI_TACTICAL_RECRUIT_RANGE are candidates. The per-unit damage estimator and the engine's extra "can attack"
/// predicates are unknown (SPEC [?]), so callers pre-filter and supply ExpectedDamage.
/// </summary>
public sealed class TacticalRecruiter(int recruitRange, int overkillPercent)
{
    public int RecruitRange { get; } = recruitRange;
    public int OverkillPercent { get; } = overkillPercent;

    public static TacticalRecruiter FromDefines(GlobalAIDefines defines, AIEdition edition)
    {
        ArgumentNullException.ThrowIfNull(defines);
        return new TacticalRecruiter(
            defines.GetInt("AI_TACTICAL_RECRUIT_RANGE", edition),
            defines.GetInt("AI_TACTICAL_OVERKILL_PERCENT", edition));
    }

    /// <summary>
    /// Chooses attackers for <paramref name="target"/> and adds their ids to <paramref name="assigned"/>, so that
    /// later targets cannot recruit them again. Units already in <paramref name="assigned"/> are skipped, as are
    /// units belonging to an operation unless <paramref name="operationsCanRecruit"/> (the move's flag in the TSV).
    /// </summary>
    public TacticalRecruitment Recruit(
        TacticalTarget target,
        IEnumerable<TacticalUnit> candidates,
        ISet<int> assigned,
        bool operationsCanRecruit = true)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(assigned);

        long budgetLong = (((long)OverkillPercent + 100) * target.TargetHp) / 100;
        int budget = (int)Math.Clamp(budgetLong, int.MinValue, int.MaxValue);

        var eligible = candidates
            .Where(u => u.ExpectedDamage > 0 && !assigned.Contains(u.Id) && (operationsCanRecruit || !u.InOperation))
            .Select(u => (Unit: u, Distance: HexMath.GetDistance(u.Position, target.Position)))
            .Where(e => e.Distance <= RecruitRange)
            .OrderByDescending(e => e.Unit.ExpectedDamage)
            .ThenBy(e => e.Distance)
            .ThenBy(e => e.Unit.Id)
            .Select(e => e.Unit)
            .ToList();

        var chosen = new List<TacticalUnit>();
        foreach (bool ranged in new[] { true, false })
        {
            foreach (var unit in eligible.Where(u => u.IsRanged == ranged))
            {
                if (budget <= 0) break;
                chosen.Add(unit);
                budget -= unit.ExpectedDamage;
            }
        }

        foreach (var unit in chosen) assigned.Add(unit.Id);
        return new TacticalRecruitment(chosen, budget);
    }
}
