using Unciv.Commands.Events;
using Unciv.Logic;
using Unciv.Logic.Civilization;
using NotificationCategory = Unciv.Logic.Civilization.Notification.NotificationCategory;

namespace Unciv.Commands.Turn;

/// <summary>
/// Give the actor's civ to the AI. Kotlin: <c>Multiplayer.resignPlayer</c>: the civ becomes an AI (no player id, human counters reset), the turn moves on if it
/// was theirs (<c>GameInfo.nextTurn</c>: here <see cref="GameInfo.EndPlayerTurn"/>, the AI civ is then played by <see cref="RunAiTurn"/>) and every civ is told.
/// Allowed at any time, also after victory. Simultaneous turns (T202): the civ is handed to the AI like <see cref="SetController" /> does (no EndPlayerTurn), refused with <c>turn-resolving</c> while the round is being resolved. The online-only check that the game is in sync is the transport's job.
/// </summary>
[CommandType("resign")]
public sealed class Resign : GameCommand, ITimedCommand
{
    public long Now { get; set; }

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.RequirePlayer().TryGet(out var actor, out var failure)) return failure;
        if (actor.IsDefeated()) return CommandValidation.Fail(CommandErrors.NotAllowed, $"{actor.CivId} has been defeated");
        if (context.Game.TurnState is { Mode: TurnMode.Simultaneous, Phase: SimultaneousPhase.Resolving })
            return CommandValidation.Fail(CommandErrors.TurnResolving, "The turn is being resolved; resign when it is played again"); // T202
        return context.IsSimultaneous() ? CommandValidation.Ok : context.CheckClock(Now);
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        var game = context.Game;
        var civ = context.Actor;
        var simultaneous = context.IsSimultaneous();
        var wasRunning = !simultaneous && game.IsTurnStarted(civ) && game.TurnPhase is TurnPhase.AwaitingInput(var current) && current == civ;

        var events = new List<GameEvent>();
        if (simultaneous) game.SetHumanController(civ, false, ""); // T202: leaves the ended set, the AI plays it in step 2 of the round
        else
        {
            civ.PlayerType = PlayerType.AI;
            civ.PlayerId = "";
        }
        civ.TurnsPlayedAsHuman = 0;
        civ.TotalTurnTimeSeconds = 0;

        if (wasRunning) game.EndPlayerTurn(Now, civ);

        foreach (var other in game.Civilizations)
            other.AddNotification($"[{civ.CivId}] resigned and is now controlled by AI", NotificationCategory.General, civ.CivId);
        if (simultaneous)
        {
            events.Add(new ControllerChanged(civ.CivId, false));
            events.AddRange(TurnCommandChecks.ResolvingEvents(game)); // the resigned civ may have been the last one the round waited for
        }
        return events;
    }
}
