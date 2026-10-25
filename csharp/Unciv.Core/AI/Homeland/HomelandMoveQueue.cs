using Unciv.AI.Data;
using Unciv.Utils;

namespace Unciv.AI.Homeland;

/// <summary>One slot of the per-turn homeland queue: a move and the effective priority it was ranked at.</summary>
public sealed record HomelandQueueEntry(HomelandMovePriority Move, int Priority);

/// <summary>
/// The prioritized move queue for non-combat and idle units (SPEC.md section 7), the same "priority table plus
/// per-move handler" pattern as the tactical layer. Higher priority runs first [I]. Each turn the queue is built
/// once; <see cref="Run"/> walks it so that e.g. spaceship parts (200) claim units before workers (30) and
/// garrisons (10). A move whose define is absent in the edition is not in the queue.
///
/// Effective priority is <c>base + rand(AI_HOMELAND_MOVE_PRIORITY_RANDOMNESS)</c>; the spec does not mention flavor scaling
/// here, so none is applied. The randomness formula itself is [?]/[I] (mirrors the tactical form).
/// Engine-agnostic: handlers receive only the move type; wiring to real units belongs to ticket A008.
/// </summary>
public sealed class HomelandMoveQueue
{
    private readonly List<(HomelandMovePriority Move, int Base)> moves = [];
    private readonly int randomness;

    public HomelandMoveQueue(GlobalAIDefines defines, AIEdition edition)
    {
        ArgumentNullException.ThrowIfNull(defines);
        foreach (var move in Enum.GetValues<HomelandMovePriority>())
        {
            string name = move.ToDefineName();
            if (defines.IsDefined(name, edition))
                moves.Add((move, defines.GetInt(name, edition)));
        }
        randomness = defines.GetInt("AI_HOMELAND_MOVE_PRIORITY_RANDOMNESS", edition);
        MaxDefensiveMoveTurns = defines.GetInt("AI_HOMELAND_MAX_DEFENSIVE_MOVE_TURNS", edition);
        MaxUpgradeMoveTurns = defines.GetInt("AI_HOMELAND_MAX_UPGRADE_MOVE_TURNS", edition);
        GreatPersonTurnsToWait = defines.GetInt("AI_HOMELAND_GREAT_PERSON_TURNS_TO_WAIT", edition);
        EstimateTurnsDistance = defines.GetInt("AI_HOMELAND_ESTIMATE_TURNS_DISTANCE", edition);
    }

    public int MaxDefensiveMoveTurns { get; }
    public int MaxUpgradeMoveTurns { get; }
    public int GreatPersonTurnsToWait { get; }
    public int EstimateTurnsDistance { get; }

    /// <summary>
    /// Ranks every move of the edition, highest effective priority first; ties keep enum (data-file) order.
    /// Jitter comes only from <paramref name="random"/> (none when null), one draw per move in enum order.
    /// </summary>
    public IReadOnlyList<HomelandQueueEntry> Build(GameRandom? random = null)
    {
        var entries = new List<HomelandQueueEntry>(moves.Count);
        foreach (var (move, basePriority) in moves)
        {
            int jitter = random != null && randomness > 0 ? random.NextInt(randomness) : 0;
            entries.Add(new HomelandQueueEntry(move, basePriority + jitter));
        }
        return entries.OrderByDescending(e => e.Priority).ToList();   // stable
    }

    /// <summary>Calls <paramref name="handler"/> for each queue entry, highest priority first.</summary>
    public static void Run(IReadOnlyList<HomelandQueueEntry> queue, Action<HomelandMovePriority> handler)
    {
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(handler);
        foreach (var entry in queue)
            handler(entry.Move);
    }
}
