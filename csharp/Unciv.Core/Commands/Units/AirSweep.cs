using Unciv.Logic.Battle;
using Unciv.Logic.Map;
using Unciv.Utils;

namespace Unciv.Commands.Units;

/// <summary>
/// Air sweep over a tile by a unit that is preparing one. Kotlin: BattleTable.simulateAirsweep (<c>canAttack()</c> and the tile
/// in <c>getTilesInAttackRange</c>), then <c>MapUnitView.tryAirSweep</c> = <c>AirInterception.airSweep</c>.
/// </summary>
[CommandType("air-sweep")]
public sealed class AirSweep : GameCommand
{
    public int UnitId { get; init; }
    public HexCoord Target { get; init; }

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.RequireActive().TryGet(out var actor, out var failure)) return failure;
        if (!context.OwnUnit(actor, UnitId).TryGet(out var unit, out failure)) return failure;
        if (!context.TileAt(Target).TryGet(out var tile, out failure)) return failure;
        if (!unit.IsPreparingAirSweep()) return CommandValidation.Fail(CommandErrors.NotAllowed, "The unit is not preparing an air sweep");
        if (!unit.CanAttack()) return CommandValidation.Fail(CommandErrors.NotAllowed, "The unit cannot attack now");
        return unit.GetTile().GetTilesInDistanceRange(new IntRange(1, unit.GetRange())).Contains(tile)
            ? CommandValidation.Ok
            : CommandValidation.Fail(CommandErrors.InvalidTarget, "That tile is out of range");
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        var unit = context.OwnUnit(context.Actor, UnitId).Value.NotNull();
        AirInterception.AirSweep(new MapUnitCombatant(unit), context.TileAt(Target).Value.NotNull());
        return [];
    }
}
