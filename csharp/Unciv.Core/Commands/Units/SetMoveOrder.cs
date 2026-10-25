using Unciv.Logic.Map;
using Unciv.Logic.Map.MapUnits;
using Unciv.Utils;

namespace Unciv.Commands.Units;

/// <summary>
/// Multi-turn move order. Kotlin: WorldMapHolder.moveUnitToTargetTile, i.e. <c>headTowards</c> now, then
/// <c>MapUnitView.trySetMoveToAction</c> (<c>unit.action = "moveTo x,y"</c>) unless the unit arrived, so <c>DoAction</c> continues
/// next turn. Offered when <c>canReach</c> holds (a shortest path exists). When the unit could not get any closer this turn
/// nothing changes (the UI treats that as a dud and keeps e.g. fortify).
/// </summary>
[CommandType("set-move-order")]
public sealed class SetMoveOrder : GameCommand
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
        return unit.Movement.CanReach(tile)
            ? CommandValidation.Ok
            : CommandValidation.Fail(CommandErrors.InvalidTarget, "The unit cannot reach that tile");
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        var unit = context.OwnUnit(context.Actor, UnitId).Value.NotNull();
        var target = context.TileAt(To).Value.NotNull();
        var origin = unit.CurrentTile;
        try
        {
            unit.Movement.HeadTowards(target);
        }
        catch (UnitMovement.UnreachableDestinationException)
        {
            // The UI ignores this as well: the board changed between the check and the move; nothing moved.
        }
        if (unit.CurrentTile == origin) return []; // a dud, like the UI: leave the current action alone
        if (unit.IsExploring() || unit.IsMoving()) unit.Action = null;
        if (unit.CurrentTile != target) unit.Action = $"moveTo {To.x},{To.y}";
        return [];
    }
}
