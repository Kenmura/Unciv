using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using Unciv.Logic.Map.MapUnits;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.AI.Operations;

/// <summary>
/// T235: game adapter for the operations layer (SPEC.md section 8). Each turn it restores the civ's saved operations, starts a new one when there is
/// a worthwhile target and enough free troops, lets every operation advance, walks the armies that are gathering or marching, and saves the result
/// into <see cref="Civilization.Civ5Ai"/>.
/// Scope of this port: city attacks (major civ and city-state) and barbarian camps. The spec's other kinds (colonize, naval, nukes, ...) need settler
/// escorts or sea logistics that the unit automation owns and are not started here. An army that is still on the way after
/// <see cref="MaxOperationTurns"/> turns is called off (the spec has no such limit; it is this port's guard against an unreachable target).
/// Units at the target are released to normal automation and the tactical queue, which finish the job.
/// </summary>
public static class OperationsAdapter
{
    /// <summary>At most this many operations run at once.</summary>
    public const int MaxOperations = 2;

    /// <summary>An operation older than this many turns is aborted.</summary>
    public const int MaxOperationTurns = 40;

    /// <summary>An army that found its target unreachable (e.g. across the sea) leaves it alone for this many turns.</summary>
    public const int BlockedTurns = 30;

    /// <summary>Targets farther than this from our nearest city are not considered.</summary>
    public const int MaxTargetDistance = 20;

    private static ArmyRole[] ArmyFor(OperationType type) =>
        type == OperationType.DestroyBarbarianCamp ? [ArmyRole.Melee, ArmyRole.Ranged] : [ArmyRole.Melee, ArmyRole.Melee, ArmyRole.Ranged];

    /// <summary>Runs the operations of one turn. Returns the ids of the units that were ordered to march (they must not also be automated this turn).</summary>
    public static HashSet<int> Run(Civilization civ, OperationTunables tunables)
    {
        ArgumentNullException.ThrowIfNull(civ);
        ArgumentNullException.ThrowIfNull(tunables);
        var state = civ.Civ5Ai ??= new Civ5AiState();
        Civ5AiStateSanitizer.Sanitize(state, civ);
        var manager = OperationManager.Restore(tunables, state);
        int turn = civ.GameInfo.Turns;

        foreach (var op in manager.Operations.Where(o => !o.IsFinished && turn - o.StartTurn > MaxOperationTurns).ToList())
            manager.Abort(op);

        var units = Snapshot(civ, manager);
        TryStart(civ, manager, state, units, turn);
        var orders = manager.Update(Snapshot(civ, manager), op => IsTargetValid(civ, op));
        var moved = new HashSet<int>();
        foreach (var order in orders)
        {
            var unit = civ.Units.GetUnitById(order.UnitId);
            if (unit == null) continue;
            moved.Add(unit.Id);
            var destination = civ.GameInfo.TileMap[order.Destination];
            if (unit.CurrentTile == destination || unit.CurrentMovement <= 0f) continue;
            try { unit.Movement.HeadTowards(destination); }
            catch (UnitMovement.UnreachableDestinationException)
            {
                var op = manager.Operations.FirstOrDefault(o => o.Id == order.OperationId);
                if (op == null) continue;
                state.BlockedTargets.RemoveAll(b => b.Position == op.Target);
                state.BlockedTargets.Add(new BlockedTarget { Position = op.Target, UntilTurn = turn + BlockedTurns });
                manager.Abort(op);
            }
        }
        state.BlockedTargets.RemoveAll(b => b.UntilTurn <= turn);
        manager.Save(state);
        return moved;
    }

    private static ArmyRole? RoleOf(MapUnit unit)
    {
        if (!unit.IsMilitary() || !unit.BaseUnit.IsLandUnit || unit.IsEmbarked()) return null;
        return unit.BaseUnit.IsRanged() ? ArmyRole.Ranged : ArmyRole.Melee;
    }

    /// <summary>
    /// The civ's land troops in unit-list order. A unit that stands in one of our cities and is not already held by an operation is a garrison
    /// and is left out, so an operation never takes the last defender away.
    /// </summary>
    private static List<OperationUnit> Snapshot(Civilization civ, OperationManager manager)
    {
        var result = new List<OperationUnit>();
        foreach (var unit in civ.Units.GetCivUnits())
        {
            var role = RoleOf(unit);
            if (role == null) continue;
            bool garrison = unit.CurrentTile.IsCityCenter() && unit.CurrentTile.GetCity()?.Civ == civ;
            if (garrison && !manager.LockedUnits.Contains(unit.Id)) continue;
            result.Add(new OperationUnit(unit.Id, unit.CurrentTile.Position, unit.Health, role.Value));
        }
        return result;
    }

    private static bool IsTargetValid(Civilization civ, AIOperation op)
    {
        var tile = civ.GameInfo.TileMap[op.Target];
        if (op.Type == OperationType.DestroyBarbarianCamp) return tile.Improvement == Constants.BarbarianEncampment;
        var city = tile.GetCity();
        return city != null && tile.IsCityCenter() && civ.IsAtWarWith(city.Civ);
    }

    private static void TryStart(Civilization civ, OperationManager manager, Civ5AiState state, List<OperationUnit> units, int turn)
    {
        if (manager.Operations.Count(o => !o.IsFinished) >= MaxOperations || civ.Cities.Count == 0) return;
        var ourCities = civ.Cities.Select(c => c.GetCenterTile().Position).ToList();
        var free = units.Where(u => !manager.LockedUnits.Contains(u.Id)).ToList();

        (OperationType Type, TileClass Tile, HexCoord Muster, int Rank, int Distance)? best = null;
        foreach (var tile in civ.ViewableTiles.OrderBy(t => t.Position.X).ThenBy(t => t.Position.Y))
        {
            OperationType type;
            var city = tile.GetCity();
            if (city != null && tile.IsCityCenter() && civ.IsAtWarWith(city.Civ))
                type = city.Civ.IsCityState ? OperationType.CityStateAttack : OperationType.BasicCityAttack;
            else if (tile.Improvement == Constants.BarbarianEncampment) type = OperationType.DestroyBarbarianCamp;
            else continue;
            if (manager.Exists(type, tile.Position) || state.BlockedTargets.Any(b => b.Position == tile.Position && b.UntilTurn > turn)) continue;
            var muster = ourCities.OrderBy(c => HexMath.GetDistance(c, tile.Position)).ThenBy(c => c.X).ThenBy(c => c.Y).First();
            int distance = HexMath.GetDistance(muster, tile.Position);
            if (distance > MaxTargetDistance) continue;
            // cities before camps, then the nearest target; the scan order breaks ties
            int rank = type == OperationType.DestroyBarbarianCamp ? 1 : 0;
            if (best is { } b && (rank, distance).CompareTo((b.Rank, b.Distance)) >= 0) continue;
            best = (type, tile, muster, rank, distance);
        }
        if (best is not { } chosen) return;

        var slots = ArmyFor(chosen.Type);
        foreach (var group in slots.GroupBy(r => r))
            if (free.Count(u => u.Role == group.Key) < group.Count()) return;

        var op = manager.Start(chosen.Type, chosen.Tile.Position, chosen.Muster, slots);
        op.StartTurn = turn;
    }
}
