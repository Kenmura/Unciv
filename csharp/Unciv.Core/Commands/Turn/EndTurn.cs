using Unciv.Logic;

namespace Unciv.Commands.Turn;

/// <summary>
/// End the actor's turn. Kotlin: <c>NextTurnAction</c> (refused while a decision is pending) then <c>GameInfo.nextTurn</c>, whose first part is
/// <see cref="GameInfo.EndPlayerTurn"/>; the auto players and the next human's start are the server's <see cref="RunAiTurn"/> and <see cref="BeginTurn"/>.
/// <see cref="Now"/> is stamped by the authority, never trusted from a client.
/// T202, simultaneous turns: the civ is only marked as having ended its turn (<c>already-ended</c> if it did; <c>turn-resolving</c> while the round is resolved);
/// the command that completes the set also starts the resolution (event <c>turn-resolving</c>), and the server then issues the step commands.
/// </summary>
[CommandType("end-turn")]
public sealed class EndTurn : GameCommand, ITimedCommand
{
    public long Now { get; set; }

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.RequireActive(allowDefeated: true).TryGet(out var actor, out var failure)) return failure;
        failure = context.CanEndTurnOf(actor);
        if (!failure.IsValid) return failure;
        if (!context.IsSimultaneous()) // a simultaneous EndTurn only marks the civ; ProcessEndTurn carries the time
        {
            failure = context.CheckClock(Now);
            if (!failure.IsValid) return failure;
        }
        var blocking = PendingDecisions.BlockingFor(actor);
        return blocking.Count == 0
            ? CommandValidation.Ok
            : CommandValidation.Fail(CommandErrors.PendingDecisions,
                "Decisions pending: " + string.Join(", ", blocking.Select(it => it.Subject.Length == 0 ? it.Kind.ToString() : $"{it.Kind} {it.Subject}")));
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        return context.EndTurnOf(context.Actor, Now);
    }
}
