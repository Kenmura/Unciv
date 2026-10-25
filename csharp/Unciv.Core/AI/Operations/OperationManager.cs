using Unciv.AI.Tactical;
using Unciv.Logic.Map;

namespace Unciv.AI.Operations;

/// <summary>
/// The civilization's list of running operations (SPEC.md section 8) and the recruitment lock they share.
/// Operations run in id order, so earlier operations claim units first and the result never depends on hash order.
/// </summary>
public sealed class OperationManager(OperationTunables tunables)
{
    private readonly List<AIOperation> operations = [];
    private readonly SortedSet<int> locked = [];
    private int nextId = 1;

    /// <summary>T235: continues from saved state; the recruitment lock is rebuilt from the held units, so it never has to be saved.</summary>
    public static OperationManager Restore(OperationTunables tunables, Civ5AiState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var manager = new OperationManager(tunables) { nextId = state.NextOperationId };
        foreach (var record in state.Operations.OrderBy(it => it.Id))
        {
            var op = AIOperation.Restore(record, tunables);
            manager.operations.Add(op);
            if (!op.IsFinished) foreach (int id in op.UnitIds) manager.locked.Add(id);
        }
        return manager;
    }

    /// <summary>T235: writes the live operations back to the saved state, .</summary>
    public void Save(Civ5AiState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        state.NextOperationId = nextId;
        state.Operations = [.. operations.Where(o => !o.IsFinished).Select(o => o.ToRecord())];
    }

    public IReadOnlyList<AIOperation> Operations => operations;

    /// <summary>Ids of every unit held by an operation: feed these to <see cref="TacticalUnit.InOperation"/>.</summary>
    public IReadOnlySet<int> LockedUnits => locked;

    /// <summary>Starts an operation; its id is the next one in sequence.</summary>
    public AIOperation Start(OperationType type, HexCoord target, HexCoord muster, IEnumerable<ArmyRole> slots, bool enemyTerritory = false)
    {
        var op = new AIOperation(nextId++, type, target, muster, slots, enemyTerritory, tunables);
        operations.Add(op);
        return op;
    }

    /// <summary>Whether a target already has a live operation of the given type (to avoid duplicates).</summary>
    public bool Exists(OperationType type, HexCoord target) =>
        operations.Any(o => !o.IsFinished && o.Type == type && o.Target == target);

    /// <summary>Gives up an operation now: its units are unlocked and it leaves the list.</summary>
    public void Abort(AIOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        operation.Abort(locked);
        operations.Remove(operation);
    }

    /// <summary>
    /// Runs one turn for every live operation in id order and drops the finished ones.
    /// <paramref name="targetValid"/> is asked once per operation.
    /// </summary>
    public IReadOnlyList<OperationOrder> Update(IReadOnlyList<OperationUnit> units, Func<AIOperation, bool> targetValid)
    {
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(targetValid);
        var orders = new List<OperationOrder>();
        foreach (var op in operations.ToList())
        {
            orders.AddRange(op.Update(units, locked, targetValid(op)));
        }
        operations.RemoveAll(o => o.IsFinished);
        return orders;
    }
}
