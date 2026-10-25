using Unciv.Utils;

namespace Unciv.Commands.Units;

/// <summary>
/// Runs one of the unit actions the UI offers (T189, design section 4): fortify, sleep, explore, automate, found city, pillage,
/// upgrade, set up, skip, the stop actions and everything else <c>UnitActions.GetUnitActions</c> lists. The descriptor is resolved
/// against the unit's current actions; the action must be enabled (<c>Action != null</c>), and one that carries a confirmation
/// needs <see cref="Confirmed"/>. The closure runs once, after validation; the state is not touched by validation.
/// </summary>
[CommandType("execute-unit-action")]
public sealed class ExecuteUnitAction : GameCommand
{
    public int UnitId { get; init; }
    public UnitActionDescriptor Descriptor { get; init; } = new(default);
    public bool Confirmed { get; init; }

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.RequireActive().TryGet(out var actor, out var failure)) return failure;
        if (!context.OwnUnit(actor, UnitId).TryGet(out var unit, out failure)) return failure;
        var action = Descriptor.Resolve(unit);
        if (action == null)
            return CommandValidation.Fail(CommandErrors.StaleReference, $"The unit has no {Descriptor.ActionType} action \"{Descriptor.Key}\" ({Descriptor.Index}) any more");
        if (action.Action == null)
            return CommandValidation.Fail(CommandErrors.NotAllowed, $"The {Descriptor.ActionType} action is not possible right now");
        if (action.Confirmation != null && !Confirmed)
            return CommandValidation.Fail(CommandErrors.NotAllowed, action.Confirmation.Question);
        return CommandValidation.Ok;
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        var unit = context.OwnUnit(context.Actor, UnitId).Value.NotNull();
        Descriptor.Resolve(unit).NotNull().Action.NotNull()();
        return [];
    }
}
