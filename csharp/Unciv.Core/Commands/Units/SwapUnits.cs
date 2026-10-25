using Unciv.Logic.Map;
using Unciv.Utils;

namespace Unciv.Commands.Units;

/// <summary>
/// Swap the unit with a friendly unit of the same kind on the destination. Kotlin: <c>MapUnitView.trySwapMoveToTile</c>
/// (<c>swapMoveToTile</c>), offered by WorldMapHolder when <c>canSwapTo</c> holds (<c>UnitMovement.CanUnitSwapTo</c>).
/// </summary>
[CommandType("swap-units")]
public sealed class SwapUnits : GameCommand
{
    public int UnitId { get; init; }
    public HexCoord To { get; init; }
    public bool KeepEscorting { get; init; }

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.RequireActive().TryGet(out var actor, out var failure)) return failure;
        if (!context.OwnUnit(actor, UnitId).TryGet(out var unit, out failure)) return failure;
        if (!context.TileAt(To).TryGet(out var tile, out failure)) return failure;
        return unit.Movement.CanUnitSwapTo(tile)
            ? CommandValidation.Ok
            : CommandValidation.Fail(CommandErrors.InvalidTarget, "The unit cannot swap with that tile");
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        var unit = context.OwnUnit(context.Actor, UnitId).Value.NotNull();
        unit.Movement.SwapMoveToTile(context.TileAt(To).Value.NotNull(), KeepEscorting);
        return [];
    }
}
