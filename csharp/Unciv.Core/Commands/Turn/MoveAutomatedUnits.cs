namespace Unciv.Commands.Turn;

/// <summary>
/// Let the automated units act now. Kotlin: <c>NextTurnAction.MoveAutomatedUnits</c> ("Move automated units" while the player's setting does not do it
/// at turn start): <c>CivView.tryAutomateAllUnits</c> then <c>tryMarkMovedAutomatedUnits</c>.
/// </summary>
[CommandType("move-automated-units")]
public sealed class MoveAutomatedUnits : GameCommand
{
    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.RequireActive().TryGet(out var actor, out var failure)) return failure;
        if (context.Game.GetPlayerSettings(actor.CivId).AutomatedUnitsMoveOnTurnStart)
            return CommandValidation.Fail(CommandErrors.NotAllowed, "Automated units already move at the start of the turn");
        return actor.HasMovedAutomatedUnits
            ? CommandValidation.Fail(CommandErrors.NotAllowed, "The automated units have already moved this turn")
            : CommandValidation.Ok;
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        var actor = context.Actor;
        // Kotlin iterates the live unit list; a unit that removes itself (founding a city) would break the enumeration, so iterate a snapshot.
        foreach (var unit in actor.Units.GetCivUnits().ToList()) unit.DoAction();
        actor.HasMovedAutomatedUnits = true;
        return [];
    }
}
