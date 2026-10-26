using Unciv.Logic;
using Unciv.Utils;

namespace Unciv.Commands.Turn;

/// <summary>
/// Simultaneous turns, step 1 of resolving a round (T202): the server ends the turn of a human, <c>TurnManager.EndTurn</c> without moving on to another civ
/// (<see cref="GameInfo.ProcessEndTurn"/>). Valid only when <see cref="GameInfo.NextResolveStep"/> names it (<c>out-of-sequence</c> otherwise).
/// <see cref="Now"/> is the authority's logged time, as for <see cref="EndTurn"/> in a sequential game.
/// </summary>
[CommandType("process-end-turn")]
public sealed class ProcessEndTurn : GameCommand, ITimedCommand
{
    public string CivId { get; init; } = "";

    public long Now { get; set; }

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        var failure = context.RequireServer();
        if (!failure.IsValid) return failure;
        if (!context.TryResolve(CivId, out _, out failure)) return failure;
        failure = context.RequireStep(new ResolveStep.ProcessEndTurn(CivId));
        return failure.IsValid ? context.CheckClock(Now) : failure;
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        context.Game.ProcessEndTurn(context.Civ(CivId).Value.NotNull(), Now);
        return [];
    }
}
