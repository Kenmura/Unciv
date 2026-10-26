using System.Diagnostics.CodeAnalysis;
using Unciv.Commands.Events;
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
    /// May <paramref name="civ"/> end its turn. Sequential: its turn has begun and not ended (a second EndPlayerTurn would advance the cycle again).
    /// Simultaneous (T202): the round is Playing (<c>turn-resolving</c>), the civ is one of the humans it waits for and has begun its turn
    /// (<c>not-your-turn</c>) and has not ended it yet (<c>already-ended</c>).
    /// </summary>
    public static CommandValidation CanEndTurnOf(this CommandContext context, CivilizationObject civ)
    {
        var game = context.Game;
        if (game.TurnState is { Mode: TurnMode.Simultaneous } state)
        {
            if (state.Phase == SimultaneousPhase.Resolving)
                return CommandValidation.Fail(CommandErrors.TurnResolving, "The turn is being resolved");
            if (!game.IsActiveHuman(civ) || !game.IsTurnStarted(civ))
                return CommandValidation.Fail(CommandErrors.NotYourTurn, $"{civ.CivId} is not playing this turn");
            return game.HasEndedTurn(civ)
                ? CommandValidation.Fail(CommandErrors.AlreadyEnded, $"{civ.CivId} has ended the turn")
                : CommandValidation.Ok;
        }
        return game.IsTurnStarted(civ) && game.TurnPhase is TurnPhase.AwaitingInput(var current) && current == civ
            ? CommandValidation.Ok
            : CommandValidation.Fail(CommandErrors.NotYourTurn, $"The turn of {civ.CivId} is not running");
    }

    /// <summary>
    /// Ends the turn of <paramref name="civ"/>: sequential, <c>EndPlayerTurn</c>; simultaneous, the civ is only marked ended, and the transition to
    /// Resolving happens in the same command when it was the last one the round waited for (a pure function of the state, so replay needs no extra entry).
    /// </summary>
    public static IReadOnlyList<GameEvent> EndTurnOf(this CommandContext context, CivilizationObject civ, long now)
    {
        var game = context.Game;
        if (game.TurnState is not { Mode: TurnMode.Simultaneous })
        {
            game.EndPlayerTurn(now, civ);
            return [];
        }
        game.EndHumanTurn(civ);
        return [new PlayerEndedTurn(civ.CivId, game.Turns), .. ResolvingEvents(game)];
    }

    /// <summary>The <see cref="TurnResolving"/> event when the round just began to be resolved (<see cref="GameInfo.BeginResolvingIfAllEnded"/>).</summary>
    public static IReadOnlyList<GameEvent> ResolvingEvents(GameInfo game) => game.BeginResolvingIfAllEnded() ? [new TurnResolving(game.Turns)] : [];

    /// <summary>A server step command of a simultaneous round is valid exactly when <see cref="GameInfo.NextResolveStep"/> names it (else <c>out-of-sequence</c>).</summary>
    public static CommandValidation RequireStep(this CommandContext context, ResolveStep step)
    {
        var failure = context.RequireServer();
        if (!failure.IsValid) return failure;
        if (context.Game.TurnState is not { Mode: TurnMode.Simultaneous })
            return CommandValidation.Fail(CommandErrors.NotAllowed, "The game does not have simultaneous turns");
        var next = context.Game.NextResolveStep();
        return next == step
            ? CommandValidation.Ok
            : CommandValidation.Fail(CommandErrors.OutOfSequence, $"The next step is {next?.ToString() ?? "none (the round is being played)"}, not {step}");
    }

    public static bool IsSimultaneous(this CommandContext context) => context.Game.TurnState is { Mode: TurnMode.Simultaneous };

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
