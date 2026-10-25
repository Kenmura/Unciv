using Unciv.Commands;
using Unciv.Commands.Turn;
using Unciv.Logic;

namespace Unciv.GodotClient.Play;

/// <summary>What one End turn did: whether the engine took it, how many auto civs played, and every event the commands produced (the T175 stream, unfiltered).</summary>
public sealed record TurnOutcome(bool Accepted, string Message, int AiTurnsRun, bool NextTurnBegun, IReadOnlyList<GameEvent> Events);

/// <summary>
/// The single-player authority around a <see cref="GameSession"/> (C010): the one human's commands go in as that civ, and after End turn this
/// class issues the server commands the engine's turn flow needs (<see cref="RunAiTurn"/> per auto civ, then <see cref="BeginTurn"/> for the human),
/// exactly the sequence a hosted game's turn controller will issue (T202/T204). Everything goes through <see cref="GameSession"/>, so the command log
/// stays complete and replays headlessly. It reads no clock: the caller supplies one (wall time in the game, a counter in scenarios).
/// A second human in the game is not handled here (hotseat is C029): the flow stops when it is that civ's turn.
/// </summary>
public sealed class LocalHost(GameSession session, string humanCivId, Func<long> clock)
{
    public GameSession Session { get; } = session;

    public string HumanCivId { get; } = humanCivId;

    /// <summary>Raised after every accepted command, human or server, with its events.</summary>
    public event Action<CommandResult>? CommandAccepted;

    public CommandResult Submit(GameCommand command)
    {
        var result = Session.Submit(HumanCivId, command);
        if (result.Accepted) CommandAccepted?.Invoke(result);
        return result;
    }

    /// <summary>Ends the human's turn and plays everything up to the human's next turn (or the end of the game).</summary>
    public TurnOutcome EndTurn()
    {
        var events = new List<GameEvent>();
        var ended = Submit(new EndTurn { Now = clock() });
        if (!ended.Accepted) return new TurnOutcome(false, ended.Validation.Message, 0, false, events);
        events.AddRange(ended.Events);

        int aiTurns = 0;
        while (Session.Game.TurnPhase is TurnPhase.AutoPlayers(var next))
        {
            var ran = ServerCommand(new RunAiTurn { CivId = next.CivId });
            if (!ran.Accepted) return new TurnOutcome(false, ran.Validation.Message, aiTurns, false, events);
            events.AddRange(ran.Events);
            aiTurns++;
        }

        bool begun = false;
        if (Session.Game.TurnPhase is TurnPhase.AwaitingInput(var human) && human.CivId == HumanCivId && !Session.Game.IsTurnStarted(human))
        {
            var began = ServerCommand(new BeginTurn { CivId = HumanCivId, Now = clock() });
            if (!began.Accepted) return new TurnOutcome(false, began.Validation.Message, aiTurns, false, events);
            events.AddRange(began.Events);
            begun = true;
        }
        return new TurnOutcome(true, "", aiTurns, begun, events);
    }

    private CommandResult ServerCommand(GameCommand command)
    {
        var result = Session.SubmitAsServer(command);
        if (result.Accepted) CommandAccepted?.Invoke(result);
        return result;
    }
}
