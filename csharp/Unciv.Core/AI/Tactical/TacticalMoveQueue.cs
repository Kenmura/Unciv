using Unciv.AI.Data;
using Unciv.Utils;

namespace Unciv.AI.Tactical;

/// <summary>One slot of the per-turn queue: a move and the effective priority it was ranked at.</summary>
public sealed record TacticalQueueEntry(TacticalMoveType Move, int Priority);

/// <summary>The leader's offense and defense flavors that scale a move's base priority (0-20 in the shipped data).</summary>
public sealed record TacticalFlavors(int Offense, int Defense);

/// <summary>
/// The prioritized move queue that replaces per-unit movement loops (SPEC.md section 6.1/6.4). Each turn the queue
/// is built once from the move table; <see cref="Run"/> then walks it from the top, handing every move its own
/// targets so that critical moves (capture a city, kill a high-value unit) recruit units civilization-wide before
/// lower-priority moves see them.
///
/// The effective priority is [I] in the spec (unverified):
/// <c>Priority + trunc(DAMPENING * (OffenseWeight * offenseFlavor + DefenseWeight * defenseFlavor) / 100) + rand(RANDOMNESS)</c>.
/// </summary>
public sealed class TacticalMoveQueue
{
    private readonly IReadOnlyList<TacticalMoveDefinition> moves;
    private readonly double dampening;
    private readonly int randomness;

    public TacticalMoveQueue(IReadOnlyList<TacticalMoveDefinition> moves, GlobalAIDefines defines, AIEdition edition)
    {
        ArgumentNullException.ThrowIfNull(moves);
        ArgumentNullException.ThrowIfNull(defines);
        this.moves = moves.Where(m => m.IsInEdition(edition)).ToList();
        dampening = defines.GetDouble("AI_TACTICAL_FLAVOR_DAMPENING_FOR_MOVE_PRIORITIZATION", edition);
        randomness = defines.GetInt("AI_TACTICAL_MOVE_PRIORITY_RANDOMNESS", edition);
    }

    /// <summary>
    /// Ranks every move of the edition, highest effective priority first; ties keep table order.
    /// The random jitter comes only from <paramref name="random"/> (none when null), one draw per move in table order.
    /// </summary>
    public IReadOnlyList<TacticalQueueEntry> Build(TacticalFlavors flavors, GameRandom? random = null)
    {
        ArgumentNullException.ThrowIfNull(flavors);
        var entries = new List<TacticalQueueEntry>(moves.Count);
        foreach (var def in moves)
        {
            long weighted = ((long)(def.OffenseFlavorWeight ?? 0) * flavors.Offense + (long)(def.DefenseFlavorWeight ?? 0) * flavors.Defense) / 100;
            int bonus = (int)(dampening * weighted);
            int jitter = random != null && randomness > 0 ? random.NextInt(randomness) : 0;
            entries.Add(new TacticalQueueEntry(TacticalMoveTypeExtensions.Parse(def.Type), def.Priority + bonus + jitter));
        }
        return entries.OrderByDescending(e => e.Priority).ToList();   // stable
    }

    /// <summary>Calls <paramref name="handler"/> for each queue entry in order with that move's targets, best first.</summary>
    public static void Run(
        IReadOnlyList<TacticalQueueEntry> queue,
        TacticalTargetList targets,
        Action<TacticalMoveType, IReadOnlyList<TacticalTarget>> handler)
    {
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(handler);
        foreach (var entry in queue)
            handler(entry.Move, targets.For(entry.Move));
    }
}
