using Unciv.Utils;

namespace Unciv.Commands.Units;

/// <summary>
/// Disband one of the actor's units. Kotlin: UiOnlyUnitActions.getDisbandAction, offered while <c>unit.hasMovement()</c>;
/// <c>unit.disband()</c> (gold refund inside own borders is part of it) then <c>civ.updateStatsForNextTurn()</c> (less upkeep).
/// The UI's confirmation question is client-only.
/// </summary>
[CommandType("disband-unit")]
public sealed class DisbandUnit : GameCommand
{
    public int UnitId { get; init; }

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.RequireActive().TryGet(out var actor, out var failure)) return failure;
        if (!context.OwnUnit(actor, UnitId).TryGet(out var unit, out failure)) return failure;
        return unit.HasMovement()
            ? CommandValidation.Ok
            : CommandValidation.Fail(CommandErrors.NotAllowed, "The unit has no movement left");
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        var unit = context.OwnUnit(context.Actor, UnitId).Value.NotNull();
        unit.Disband();
        unit.Civ.UpdateStatsForNextTurn();
        return [];
    }
}
