using Unciv.Logic.Battle;
using Unciv.Logic.Map;
using Unciv.Utils;
using NukeLogic = Unciv.Logic.Battle.Nuke;

namespace Unciv.Commands.Units;

/// <summary>
/// Launch a nuclear weapon at a tile. Kotlin: BattleTable.simulateNuke (not the launch tile, <c>canAttack()</c>,
/// <c>mayUseNuke</c>, which includes range and war restrictions), then <c>MapUnitView.tryNuke</c> = <c>Nuke.NUKE</c>.
/// The UI's confirmation step is client-only.
/// </summary>
[CommandType("nuke")]
public sealed class Nuke : GameCommand
{
    public int UnitId { get; init; }
    public HexCoord Target { get; init; }

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.RequireActive().TryGet(out var actor, out var failure)) return failure;
        if (!context.OwnUnit(actor, UnitId).TryGet(out var unit, out failure)) return failure;
        if (!context.TileAt(Target).TryGet(out var tile, out failure)) return failure;
        if (!unit.IsNuclearWeapon()) return CommandValidation.Fail(CommandErrors.NotAllowed, "The unit is not a nuclear weapon");
        if (!unit.CanAttack()) return CommandValidation.Fail(CommandErrors.NotAllowed, "The unit cannot attack now");
        return NukeLogic.MayUseNuke(new MapUnitCombatant(unit), tile)
            ? CommandValidation.Ok
            : CommandValidation.Fail(CommandErrors.InvalidTarget, "That tile cannot be nuked");
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        var unit = context.OwnUnit(context.Actor, UnitId).Value.NotNull();
        NukeLogic.NUKE(new MapUnitCombatant(unit), context.TileAt(Target).Value.NotNull());
        return [];
    }
}
