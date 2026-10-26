using Unciv.Logic;
using Unciv.Utils;

namespace Unciv.Commands.Turn;

/// <summary>
/// The server starts the turn of the civ the auto players stopped on: <see cref="GameInfo.BeginPlayerTurn"/> (the tail of <c>GameInfo.nextTurn</c>).
/// Valid once no auto civ is left before it.
/// Simultaneous turns (T202): step 4 of the round, valid only for the civ <see cref="GameInfo.NextResolveStep"/> names (<c>out-of-sequence</c> otherwise).
/// </summary>
[CommandType("begin-turn")]
public sealed class BeginTurn : GameCommand, ITimedCommand
{
    public string CivId { get; init; } = "";

    public long Now { get; set; }

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        var failure = context.RequireServer();
        if (!failure.IsValid) return failure;
        if (!context.TryResolve(CivId, out var civ, out failure)) return failure;
        if (context.IsSimultaneous())
        {
            failure = context.RequireStep(new ResolveStep.BeginTurn(CivId));
            return failure.IsValid ? context.CheckClock(Now) : failure;
        }
        return context.Game.TurnPhase is TurnPhase.AwaitingInput(var next) && next == civ && !context.Game.IsTurnStarted(civ)
            ? CommandValidation.Ok
            : CommandValidation.Fail(CommandErrors.NotAllowed, $"The turn of {CivId} cannot begin now");
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        var civ = context.Civ(CivId).Value.NotNull();
        if (context.IsSimultaneous()) context.Game.BeginTurn(civ, Now);
        else context.Game.BeginPlayerTurn(Now, civ);
        return [];
    }
}
