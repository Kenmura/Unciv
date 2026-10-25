using Unciv.Commands.City;
using Unciv.Commands.Empire;
using Unciv.Commands.Turn;
using Unciv.Commands.Units;
using Unciv.Logic;
using Unciv.Models;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Commands.Log;

/// <summary>
/// A deterministic stand-in for a human (T194 acceptance, old T164) plus the host loop around it: each round it begins the human's turn when needed,
/// founds a city with a settler, explores with idle units, answers the pending decisions (production, research, policy, automated units) through
/// <see cref="GameSession.Submit(string, GameCommand)"/>, ends the turn (<see cref="ForceEndTurn"/> when a decision it cannot answer blocks), lets the AI civs
/// play through <see cref="RunAiTurn"/> and <see cref="BeginTurn"/>, and writes the log with a checkpoint after every turn change. It uses no UI code and no randomness.
/// </summary>
public sealed class ScriptedHuman(GameSession session, string civId, CommandLogWriter? writer = null)
{
    /// <summary>A clock for <see cref="GameSession.Clock"/>: 1000 ms later on every reading.</summary>
    public static Func<long> DeterministicClock(long start)
    {
        var now = start;
        return () => now += 1000;
    }

    /// <summary>
    /// Plays <paramref name="turns"/> more human turns, then lets the others play until the human's next turn has begun (so the game can be saved and loaded), or
    /// stops early when the game ends. Returns how many human turns were played.
    /// </summary>
    public int PlayTurns(int turns)
    {
        var game = session.Game;
        if (writer != null && writer.LastSeq == 0 && session.LastSeq == 0) Checkpoint();
        var played = 0;
        while (true)
        {
            switch (game.TurnPhase)
            {
                case TurnPhase.GameOver:
                    return played;
                case TurnPhase.AutoPlayers auto:
                    Server(new RunAiTurn { CivId = auto.NextCiv.CivId });
                    break;
                case TurnPhase.AwaitingInput waiting when !game.IsTurnStarted(waiting.Civ):
                    Server(new BeginTurn { CivId = waiting.Civ.CivId });
                    Checkpoint();
                    break;
                case TurnPhase.AwaitingInput waiting when waiting.Civ.CivId == civId && waiting.Civ.IsAlive():
                    if (played == turns) return played; // stopped where a snapshot can be loaded: the human's turn has begun
                    PlayHumanTurn(waiting.Civ);
                    played++;
                    break;
                default:
                    return played; // somebody else's turn (another human) or our civ is defeated
            }
        }
    }

    private void PlayHumanTurn(CivilizationObject civ)
    {
        foreach (var unit in civ.Units.GetCivUnits().ToList())
        {
            if (!unit.IsIdle()) continue;
            var actions = UnitActionDescriptor.Describe(unit).Where(it => it.Action.Action != null).ToList();
            var chosen = actions.FirstOrDefault(it => it.Descriptor.ActionType == UnitActionType.FoundCity);
            if (chosen.Descriptor == null && !unit.IsCivilian())
                chosen = actions.FirstOrDefault(it => it.Descriptor.ActionType == UnitActionType.Explore);
            if (chosen.Descriptor != null)
                Human(new ExecuteUnitAction { UnitId = unit.Id, Descriptor = chosen.Descriptor, Confirmed = true });
        }

        var answered = true;
        for (var guard = 0; guard < 32 && answered; guard++)
        {
            var blocking = PendingDecisions.BlockingFor(civ);
            if (blocking.Count == 0) break;
            answered = Answer(civ, blocking[0]);
        }

        if (!Human(new EndTurn()).Accepted) Server(new ForceEndTurn { CivId = civId });
        writer?.Sync(session);
    }

    private bool Answer(CivilizationObject civ, Decision decision)
    {
        var ruleset = session.Game.Ruleset;
        switch (decision.Kind)
        {
            case DecisionKind.CityProduction:
                var city = civ.Cities.First(it => it.Name == decision.Subject);
                string[] choices = ["Warrior", "Scout", "Monument", "Worker", "Granary", "Library", "Barracks", "Settler"];
                return choices.Any(name => Human(new QueueConstruction { CityId = city.Id, Name = name }).Accepted);
            case DecisionKind.Technology when civ.Tech.FreeTechs != 0:
                return ruleset.Technologies.Keys.Any(name => Human(new PickFreeTechnology { Tech = name }).Accepted);
            case DecisionKind.Technology:
                return ruleset.Technologies.Keys.Any(name => Human(new SetResearchQueue { Techs = [name] }).Accepted);
            case DecisionKind.Policy:
                return ruleset.PolicyBranches.Keys.Concat(ruleset.Policies.Keys).Any(name => Human(new AdoptPolicy { Policy = name }).Accepted);
            case DecisionKind.MoveAutomatedUnits:
                return Human(new MoveAutomatedUnits()).Accepted;
            default:
                return false;
        }
    }

    private CommandResult Human(GameCommand command)
    {
        var result = session.Submit(civId, command);
        writer?.Sync(session);
        return result;
    }

    private void Server(GameCommand command)
    {
        var result = session.SubmitAsServer(command);
        if (!result.Accepted)
            throw new InvalidOperationException($"The server command {command.Type} was refused: {result.Validation.Code} {result.Validation.Message}");
        writer?.Sync(session);
    }

    private void Checkpoint() => writer?.WriteCheckpoint(session.LastSeq, session.Game.Turns, ReplayVerifier.FullHash(session.Game));
}
