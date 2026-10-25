using Unciv.Logic.Map;
using Unciv.Utils;

namespace Unciv.Commands.Units;

/// <summary>
/// One-turn move of one of the actor's units. Kotlin: <c>MapUnitView.tryMoveToTile</c> (<c>UnitMovement.moveToTile</c>), offered by
/// WorldMapHolder when <c>canMoveTo</c> and <c>canReach</c> hold. Here the destination must be reachable this turn; the authority
/// recomputes path and cost. The move may stop early (enemy revealed, zone of control); that is success with fewer steps.
/// Like the UI, a successful move clears an explore or move order.
/// </summary>
[CommandType("move-unit")]
public sealed class MoveUnit : GameCommand
{
    public int UnitId { get; init; }
    public HexCoord To { get; init; }

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.RequireActive().TryGet(out var actor, out var failure)) return failure;
        if (!context.OwnUnit(actor, UnitId).TryGet(out var unit, out failure)) return failure;
        if (!unit.HasMovement()) return CommandValidation.Fail(CommandErrors.NotAllowed, "The unit has no movement left");
        if (!context.TileAt(To).TryGet(out var tile, out failure)) return failure;
        if (tile == unit.CurrentTile) return CommandValidation.Fail(CommandErrors.InvalidTarget, "The unit is already there");
        return unit.Movement.CanMoveTo(tile) && unit.Movement.CanReachInCurrentTurn(tile)
            ? CommandValidation.Ok
            : CommandValidation.Fail(CommandErrors.InvalidTarget, "The unit cannot reach that tile this turn");
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        var unit = context.OwnUnit(context.Actor, UnitId).Value.NotNull();
        var origin = unit.CurrentTile;
        unit.Movement.MoveToTile(context.TileAt(To).Value.NotNull());
        if (unit.CurrentTile != origin && (unit.IsExploring() || unit.IsMoving())) unit.Action = null;
        return [];
    }
}
