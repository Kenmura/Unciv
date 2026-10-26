using Unciv.Commands.Events;
using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Utils;

namespace Unciv.Commands.Turn;

/// <summary>
/// Simultaneous turns (T202): the server hands a major civ to a human (a join, or a player taking its civ back) or to the AI (a leave, an AI takeover),
/// flipping <c>PlayerType</c> and <c>PlayerId</c>. Kotlin has no counterpart (<c>Multiplayer</c> edits the save). It is a logged command, so replay reaches the same
/// state; the host issues it at the boundary of a round and the engine accepts it only while the round is Playing (<c>turn-resolving</c> otherwise).
/// <para>
/// To the AI: the civ leaves the ended set and is played in step 2 of the round (its turn, if begun, is not started a second time); the last human of the
/// game cannot be handed over (<c>not-allowed</c>), or nobody could end the round. When the civ was the last one the round waited for, the round starts
/// resolving in this command. To a human: its turn begins now (<c>StartTurn</c>), the civ must be alive and the key not used by another civ.
/// </para>
/// Only major, non-spectator civs can change hands; it is <c>not-allowed</c> in Sequential games and when the civ is already so controlled.
/// </summary>
[CommandType("set-controller")]
public sealed class SetController : GameCommand
{
    public string CivId { get; init; } = "";

    /// <summary><see cref="PlayerType.Human"/> or <see cref="PlayerType.AI"/>.</summary>
    public PlayerType Controller { get; init; } = PlayerType.AI;

    /// <summary>The lobby key (public key id) of the human taking the civ; ignored for the AI. Not echoed in any event.</summary>
    public string PlayerId { get; init; } = "";

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        var failure = context.RequireServer();
        if (!failure.IsValid) return failure;
        if (!context.TryResolve(CivId, out var civ, out failure)) return failure;
        if (!context.IsSimultaneous())
            return CommandValidation.Fail(CommandErrors.NotAllowed, "Only simultaneous turns can change the controller of a civ");
        if (context.Game.TurnState is { Phase: SimultaneousPhase.Resolving })
            return CommandValidation.Fail(CommandErrors.TurnResolving, "The turn is being resolved");
        if (!civ.IsMajorCiv() || civ.IsSpectator())
            return CommandValidation.Fail(CommandErrors.NotAllowed, $"{CivId} is not a civilization a player can control");
        if (!Enum.IsDefined(Controller))
            return CommandValidation.Fail(CommandErrors.InvalidTarget, "Unknown controller");
        if (civ.PlayerType == Controller)
            return CommandValidation.Fail(CommandErrors.NotAllowed, $"{CivId} is already controlled by {Controller}");
        if (Controller == PlayerType.Human)
        {
            if (civ.IsDefeated()) return CommandValidation.Fail(CommandErrors.NotAllowed, $"{CivId} has been defeated");
            if (PlayerId.Length != 0 && context.Game.Civilizations.Any(it => it.PlayerId == PlayerId))
                return CommandValidation.Fail(CommandErrors.NotAllowed, "That player already controls a civilization");
            return CommandValidation.Ok;
        }
        return context.Game.Civilizations.Any(it => it != civ && it.IsHuman() && !it.IsSpectator())
            ? CommandValidation.Ok
            : CommandValidation.Fail(CommandErrors.NotAllowed, "The last human player cannot be handed to the AI");
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        var game = context.Game;
        var civ = context.Civ(CivId).Value.NotNull();
        var human = Controller == PlayerType.Human;
        game.SetHumanController(civ, human, PlayerId);
        return [new ControllerChanged(CivId, human), .. TurnCommandChecks.ResolvingEvents(game)];
    }
}
