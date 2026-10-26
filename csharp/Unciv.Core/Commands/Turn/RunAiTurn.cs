using Unciv.Logic;
using Unciv.Utils;

namespace Unciv.Commands.Turn;

/// <summary>
/// The server runs the turn of the next automatically played civ: the loop body of <c>GameInfo.nextTurn</c> (StartTurn, AutomateTurn, EndTurn, advance),
/// one civ per command so the log and checkpoints can be per civ. Takes no time: nothing in the loop reads the clock.
/// Simultaneous turns (T202): step 2 of the round, valid only for the civ <see cref="GameInfo.NextResolveStep"/> names (<c>out-of-sequence</c> otherwise).
/// </summary>
[CommandType("run-ai-turn")]
public sealed class RunAiTurn : GameCommand
{
    public string CivId { get; init; } = "";

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        var failure = context.RequireServer();
        if (!failure.IsValid) return failure;
        if (!context.TryResolve(CivId, out var civ, out failure)) return failure;
        if (context.IsSimultaneous()) return context.RequireStep(new ResolveStep.RunAiTurn(CivId));
        return context.Game.TurnPhase is TurnPhase.AutoPlayers(var next) && next == civ
            ? CommandValidation.Ok
            : CommandValidation.Fail(CommandErrors.NotAllowed, $"{CivId} is not the next civ to be played automatically");
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        if (context.IsSimultaneous()) context.Game.RunAiTurn(context.Civ(CivId).Value.NotNull());
        else context.Game.RunAutoPlayers();
        return [];
    }
}
