using Unciv.AI.Operations;
using Unciv.Logic.Civilization;
using GrandStrategyKind = Unciv.AI.GrandStrategy.GrandStrategy;

namespace Unciv.AI;

/// <summary>
/// T235: makes a loaded <see cref="Civ5AiState"/> safe to run. A save is untrusted input (hand-edited, corrupt, hostile): whatever it holds must never make a
/// turn throw. Records that cannot be run are dropped, numbers are clamped into a sane range, and lists that came back null are replaced by empty ones.
/// Valid state passes through unchanged, so the pass is idempotent and never alters a normal game.
/// </summary>
internal static class Civ5AiStateSanitizer
{
    internal const int MaxSlots = 16;
    internal const int MaxCounter = 1000;
    private const int MaxTurnsActive = 100_000;

    internal static GrandStrategyKind SafeStrategy(GrandStrategyKind strategy) => Enum.IsDefined(strategy) ? strategy : GrandStrategyKind.None;

    internal static int SafeTurnsActive(int turnsActive) => Math.Clamp(turnsActive, 0, MaxTurnsActive);

    public static void Sanitize(Civ5AiState state, Civilization civ)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(civ);
        var map = civ.GameInfo.TileMap;
        int turn = civ.GameInfo.Turns;

        state.CurrentStrategy = SafeStrategy(state.CurrentStrategy);
        state.TurnsActive = SafeTurnsActive(state.TurnsActive);

        state.BlockedTargets = [.. (state.BlockedTargets ?? []).Where(b => b != null && map.Contains(b.Position))];
        foreach (var blocked in state.BlockedTargets) blocked.UntilTurn = Math.Min(blocked.UntilTurn, turn + OperationsAdapter.BlockedTurns);

        var unitIds = civ.Units.GetCivUnits().Select(u => u.Id).ToHashSet();
        var claimed = new HashSet<int>();
        var seen = new HashSet<int>();
        var kept = new List<OperationRecord>();
        foreach (var record in (state.Operations ?? []).Where(r => r != null).OrderBy(r => r.Id))
        {
            if (record.Id < 1 || record.Id >= int.MaxValue - MaxCounter || !seen.Add(record.Id)) continue;
            if (!Enum.IsDefined(record.Type) || record.State is not (OperationState.Recruiting or OperationState.Gathering or OperationState.Moving or OperationState.AtTarget)) continue;
            if (record.Slots == null || record.Filled == null || record.Slots.Count == 0 || record.Slots.Count > MaxSlots || record.Filled.Count != record.Slots.Count) continue;
            if (record.Slots.Any(r => !Enum.IsDefined(r))) continue;
            if (!map.Contains(record.Target) || !map.Contains(record.Muster)) continue;

            record.MaxRecruitTurns = Math.Clamp(record.MaxRecruitTurns, 0, MaxCounter);
            record.TurnsRecruiting = Math.Clamp(record.TurnsRecruiting, 0, MaxCounter);
            record.StartTurn = Math.Clamp(record.StartTurn, turn - MaxCounter, turn);
            // a unit that is gone, or already held by another operation (or slot), simply leaves its slot
            for (int i = 0; i < record.Filled.Count; i++)
                if (record.Filled[i] is int id && (!unitIds.Contains(id) || !claimed.Add(id))) record.Filled[i] = null;
            kept.Add(record);
        }
        state.Operations = kept;
        state.NextOperationId = Math.Clamp(Math.Max(state.NextOperationId, kept.Count == 0 ? 1 : kept.Max(r => r.Id) + 1), 1, int.MaxValue - MaxCounter);
    }
}
