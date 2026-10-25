namespace Unciv.Commands.Empire;

/// <summary>
/// Choose the free great person. Kotlin: <c>GreatPersonPickerScreen.confirmAction</c>: the unit must be among
/// <c>getFreeGreatPersonOptions()</c>, then <c>chooseFreeGreatPerson</c>. That returns null when the unit cannot be placed; nothing
/// is consumed then and the command is a no-op.
/// </summary>
[CommandType("choose-free-great-person")]
public sealed class ChooseFreeGreatPerson : GameCommand
{
    public string Unit { get; init; } = "";

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.RequireActive().TryGet(out var actor, out var failure)) return failure;
        return actor.GreatPeople.GetFreeGreatPersonOptions().Any(it => it.Name == Unit)
            ? CommandValidation.Ok
            : CommandValidation.Fail(CommandErrors.NotAllowed, $"{Unit} is not a free great person option");
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        context.Actor.GreatPeople.ChooseFreeGreatPerson(Unit);
        return [];
    }
}
