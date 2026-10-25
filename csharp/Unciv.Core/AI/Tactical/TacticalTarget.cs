using Unciv.Logic.Map;

namespace Unciv.AI.Tactical;

/// <summary>
/// A thing worth moving units towards this turn (SPEC.md section 6.1 "Plan"): the tactical move that serves it,
/// the tile it sits on, its priority within that move, and the hit points a recruiter has to bring down.
/// </summary>
public sealed record TacticalTarget(TacticalMoveType Move, HexCoord Position, int Priority, int TargetHp = 0);

/// <summary>
/// The targets found by the plan phase, grouped by move. Targets of one move come back highest priority first;
/// equal priorities keep insertion order, so the result never depends on hash order.
/// </summary>
public sealed class TacticalTargetList
{
    private readonly List<TacticalTarget> targets = [];

    public int Count => targets.Count;

    public void Add(TacticalTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        targets.Add(target);
    }

    public void Clear() => targets.Clear();

    public IReadOnlyList<TacticalTarget> For(TacticalMoveType move) =>
        targets.Where(t => t.Move == move).OrderByDescending(t => t.Priority).ToList();   // OrderBy is stable
}
