using System.Diagnostics.CodeAnalysis;
using Unciv.Logic;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Commands.Turn;

/// <summary>
/// A command that reads the clock. The authority stamps <see cref="Now"/> (epoch milliseconds) before the command is logged
/// (<see cref="GameSession.Clock"/>); the logged value is what replay uses, so the engine never reads a wall clock. A client's own value is overwritten.
/// </summary>
public interface ITimedCommand
{
    long Now { get; set; }
}

/// <summary>The checks the turn and decision commands share (design/COMMANDS-IMPLEMENTATION.md sections 3.1 and 5).</summary>
internal static class TurnCommandChecks
{
    /// <summary>
    /// The actor is a human, non-spectator civ. Unlike <see cref="CommandContext.RequireActive"/> this holds for any time and after victory
    /// or defeat: acknowledging an alert, resigning and the settings commands are not tied to the turn.
    /// </summary>
    public static Resolved<CivilizationObject> RequirePlayer(this CommandContext context)
    {
        if (context.IsServer)
            return Resolved<CivilizationObject>.Fail(CommandErrors.NotHuman, "The server is not a player");
        var civ = context.Civ(context.ActorId);
        if (civ.Value == null) return civ;
        return civ.Value.IsHuman() && !civ.Value.IsSpectator()
            ? civ
            : Resolved<CivilizationObject>.Fail(CommandErrors.NotHuman, $"{context.ActorId} is not a human player");
    }

    public static CommandValidation RequireServer(this CommandContext context) =>
        context.IsServer
            ? CommandValidation.Ok
            : CommandValidation.Fail(CommandErrors.NotAllowed, "Only the server may issue this command");

    /// <summary>
    /// May <paramref name="civ"/> end its turn: its turn has begun and not ended (a second EndPlayerTurn would advance the cycle again).
    /// The one place T202 replaces for simultaneous turns.
    /// </summary>
    public static CommandValidation CanEndTurnOf(this CommandContext context, CivilizationObject civ)
    {
        // TODO(T202): simultaneous turns mark the civ ended instead of ending the cycle.
        return context.Game.IsTurnStarted(civ) && context.Game.TurnPhase is TurnPhase.AwaitingInput(var current) && current == civ
            ? CommandValidation.Ok
            : CommandValidation.Fail(CommandErrors.NotYourTurn, $"The turn of {civ.CivId} is not running");
    }

    public static CommandValidation CheckClock(this CommandContext context, long now) =>
        now < context.Game.CurrentTurnStartTime
            ? CommandValidation.Fail(CommandErrors.NotAllowed, "The time is before the start of the turn")
            : CommandValidation.Ok;

    public static bool TryResolve(this CommandContext context, string civId, [NotNullWhen(true)] out CivilizationObject? civ, out CommandValidation failure)
    {
        var resolved = context.Civ(civId);
        civ = resolved.Value;
        failure = resolved.Failure;
        return civ != null;
    }
}
