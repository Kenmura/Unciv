using Unciv.Commands;

namespace Unciv.Harness;

/// <summary>
/// Headless replay of a scenario's command log (C008): builds a fresh session from the same seeds and applies the recorded
/// envelopes in order, with no UI of any kind. If the hash it ends with equals the one the Godot run reported, the UI submitted
/// exactly the commands it claims to and nothing else changed the game.
/// </summary>
public static class ScenarioReplay
{
    public static string ReplayHash(long gameSeed, int mapSeed, IReadOnlyList<CommandEnvelope> commands, bool solo = false, bool godMode = false, bool skirmish = false, bool diplomacy = false, bool rich = false, bool policies = false, bool alerts = false, bool victory = false, bool defeat = false, bool trade = false, bool decisions = false, bool crowd = false, bool espionage = false, bool proposals = false, int religion = 0, bool voteResult = false, bool tutorialTasks = false)
    {
        var session = ScenarioGame.Create(gameSeed, mapSeed, solo, godMode, skirmish, diplomacy, rich, policies, alerts, victory, defeat, trade, decisions, crowd, espionage, proposals, religion, voteResult, tutorialTasks);
        foreach (var envelope in commands) session.Replay(envelope);
        return ScenarioGame.StateHash(session);
    }

    /// <summary>Null when the replay reproduces the recorded hash, otherwise a one-line failure message.</summary>
    public static string? Verify(ScenarioResult result)
    {
        string replayed = ReplayHash(result.GameSeed, result.MapSeed, result.Commands, result.Solo, result.GodMode, result.Skirmish, result.Diplomacy, result.Rich, result.Policies, result.Alerts, result.Victory, result.Defeat, result.Trade, result.Decisions, result.Crowd, result.Espionage, result.Proposals, result.Religion, result.VoteResult, result.TutorialTasks);
        return replayed == result.StateHash
            ? null
            : $"state hash {result.StateHash} of the Godot run differs from the headless replay of its {result.Commands.Count} command(s): {replayed}";
    }
}
