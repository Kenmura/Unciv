using CivilizationClass = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Logic;

// T184: where a game is in the human turn flow (design/COMMANDS-IMPLEMENTATION.md section 5.2). Derived from the game state, never serialized.

/// <summary>The phase a game is in, see <see cref="GameInfo.TurnPhase"/>.</summary>
public abstract record TurnPhase
{
    private TurnPhase()
    {
    }

    /// <summary>
    /// The civ is waiting to take its turn: either it has begun (<see cref="GameInfo.IsTurnStarted"/>) and wants commands,
    /// or the auto players are done and <see cref="GameInfo.BeginPlayerTurn"/> must start it.
    /// </summary>
    public sealed record AwaitingInput(CivilizationClass Civ) : TurnPhase;

    /// <summary>The civ is next to be processed automatically by <see cref="GameInfo.RunAutoPlayers"/> (AI, simulation, defeated hotseat human, online spectator).</summary>
    public sealed record AutoPlayers(CivilizationClass NextCiv) : TurnPhase;

    /// <summary>Someone has won and the game is not continuing in one-more-turn mode.</summary>
    public sealed record GameOver(VictoryData VictoryData) : TurnPhase;
}
