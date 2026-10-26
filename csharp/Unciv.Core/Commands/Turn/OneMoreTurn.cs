using Unciv.Logic;

namespace Unciv.Commands.Turn;

/// <summary>
/// Keep playing after a victory. Kotlin: <c>VictoryScreen</c> "One more turn...!" sets <c>GameInfo.oneMoreTurnMode</c>.
/// T226, simultaneous games: the switch changes the game for everybody, so only the host may flip it: the server (the host's own command, e.g. the result
/// of a vote it tallied) or the human of <see cref="TurnState.HostCivId"/>, and never a defeated civ. Sequential games keep the Kotlin rule (any human).
/// </summary>
[CommandType("one-more-turn")]
public sealed class OneMoreTurn : GameCommand
{
    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        var game = context.Game;
        if (game.TurnState is { Mode: TurnMode.Simultaneous } state)
        {
            if (!context.IsServer)
            {
                if (!context.RequirePlayer().TryGet(out var actor, out var failure)) return failure;
                if (actor.IsDefeated()) return CommandValidation.Fail(CommandErrors.NotAllowed, $"{actor.CivId} has been defeated");
                if (string.IsNullOrEmpty(state.HostCivId) || actor.CivId != state.HostCivId)
                    return CommandValidation.Fail(CommandErrors.NotAllowed, "Only the host can continue the game after a victory");
            }
        }
        else if (!context.RequirePlayer().TryGet(out _, out var failure))
        {
            return failure;
        }
        return game.VictoryData != null && !game.OneMoreTurnMode
            ? CommandValidation.Ok
            : CommandValidation.Fail(CommandErrors.NotAllowed, "There is no victory to continue past");
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        context.Game.OneMoreTurnMode = true;
        return [];
    }
}
