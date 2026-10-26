using Unciv.Utils;
namespace Unciv.Commands.Turn;

/// <summary>
/// The server ends a human's turn without asking (timeouts, design/SERVER.md 4.2): <see cref="EndTurn"/> without the pending-decision check.
/// T226: first the blocking decisions the civ left open are settled the way the AI would, each reported with a <c>decision-auto-resolved</c> event
/// (<see cref="DefaultDecisions"/>); one that cannot be settled stays in its state. Simultaneous turns (T202): marks the civ ended like <see cref="EndTurn"/>,
/// the timer's order (civ index) is the host's.
/// </summary>
[CommandType("force-end-turn")]
public sealed class ForceEndTurn : GameCommand, ITimedCommand
{
    public string CivId { get; init; } = "";

    public long Now { get; set; }

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        var failure = context.RequireServer();
        if (!failure.IsValid) return failure;
        if (!context.TryResolve(CivId, out var civ, out failure)) return failure;
        if (!civ.IsHuman()) return CommandValidation.Fail(CommandErrors.NotHuman, $"{CivId} is not a human player");
        failure = context.CanEndTurnOf(civ);
        if (!failure.IsValid || context.IsSimultaneous()) return failure;
        return context.CheckClock(Now);
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        var civ = context.Civ(CivId).Value.NotNull();
        return [.. DefaultDecisions.Resolve(civ), .. context.EndTurnOf(civ, Now)];
    }
}
