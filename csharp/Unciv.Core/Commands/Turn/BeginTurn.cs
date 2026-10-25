using Unciv.Logic;

namespace Unciv.Commands.Turn;

/// <summary>
/// The server starts the turn of the civ the auto players stopped on: <see cref="GameInfo.BeginPlayerTurn"/> (the tail of <c>GameInfo.nextTurn</c>).
/// Valid once no auto civ is left before it.
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
        return context.Game.TurnPhase is TurnPhase.AwaitingInput(var next) && next == civ && !context.Game.IsTurnStarted(civ)
            ? CommandValidation.Ok
            : CommandValidation.Fail(CommandErrors.NotAllowed, $"The turn of {CivId} cannot begin now");
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        context.Game.BeginPlayerTurn(Now, context.Civ(CivId).Value);
        return [];
    }
}
