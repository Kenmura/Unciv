namespace Unciv.Commands.Turn;

/// <summary>Keep playing after a victory. Kotlin: <c>VictoryScreen</c> "One more turn...!" sets <c>GameInfo.oneMoreTurnMode</c>.</summary>
[CommandType("one-more-turn")]
public sealed class OneMoreTurn : GameCommand
{
    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.RequirePlayer().TryGet(out _, out var failure)) return failure;
        return context.Game.VictoryData != null && !context.Game.OneMoreTurnMode
            ? CommandValidation.Ok
            : CommandValidation.Fail(CommandErrors.NotAllowed, "There is no victory to continue past");
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        context.Game.OneMoreTurnMode = true;
        return [];
    }
}
