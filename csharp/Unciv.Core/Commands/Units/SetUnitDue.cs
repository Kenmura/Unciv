using Unciv.Utils;

namespace Unciv.Commands.Units;

/// <summary>
/// Set whether a unit is due for the "next unit" cycle. Kotlin: <c>UnitActions.addSkipAction</c>, which is offered only
/// while <c>unit.hasMovement()</c> and toggles <c>unit.due</c>; the command carries the wanted value so it is idempotent.
/// </summary>
[CommandType("set-unit-due")]
public sealed class SetUnitDue : GameCommand
{
    public int UnitId { get; init; }
    public bool Due { get; init; }

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
        context.OwnUnit(context.Actor, UnitId).Value.NotNull().Due = Due;
        return [];
    }
}
