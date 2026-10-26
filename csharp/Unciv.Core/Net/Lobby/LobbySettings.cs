using Unciv.Commands.Log;
using Unciv.Logic.Map;
using Unciv.Models.Metadata;

namespace Unciv.Net.Lobby;

/// <summary>
/// Host-editable lobby settings (SERVER.md 6.5).
/// Holds map, speed, difficulty, era, victories, civ counts, ruleset, mods, and ruleset hash.
/// Maps to <see cref="GameSetupInfo"/> enforcing GodMode and TradeHintsEnabled off.
/// </summary>
public sealed class LobbySettings
{
    public string GameName { get; set; } = "Multiplayer Game";
    public MapParameters Map { get; set; } = new();
    public string Speed { get; set; } = "Standard";
    public string Difficulty { get; set; } = "Prince";
    public string Era { get; set; } = "Ancient era";
    public List<string> VictoryTypes { get; set; } = ["Domination", "Scientific", "Cultural", "Diplomatic"];
    public int MaxMajorCivs { get; set; } = 4;
    public int CityStateCount { get; set; } = 4;
    public bool AiFill { get; set; } = true;
    public bool NoBarbarians { get; set; }
    public string Ruleset { get; set; } = "Civ V - Gods & Kings";
    public List<string> Mods { get; set; } = [];
    public string TurnMode { get; set; } = "Simultaneous";
    public int TurnTimerSeconds { get; set; } = 120;
    public string OnDisconnect { get; set; } = "wait";
    public string? Password { get; set; }
    public int MaxPlayers { get; set; } = 8;
    public bool AllowJoinInProgress { get; set; } = true;

    /// <summary>
    /// Maps lobby settings onto <see cref="GameSetupInfo"/> / <see cref="GameParameters"/> for starting a game.
    /// Anti-cheat enforcement: GodMode and TradeHintsEnabled are explicitly forced off.
    /// </summary>
    public GameSetupInfo ToGameSetupInfo()
    {
        var setup = new GameSetupInfo
        {
            MapParameters = Map.Clone()
        };
        var parameters = setup.GameParameters;
        parameters.Speed = Speed;
        parameters.Difficulty = Difficulty;
        parameters.StartingEra = Era;
        parameters.VictoryTypes = [.. VictoryTypes];
        parameters.NoBarbarians = NoBarbarians;
        parameters.BaseRuleset = Ruleset;
        parameters.Mods = [.. Mods];
        parameters.NumberOfCityStates = CityStateCount;
        parameters.IsOnlineMultiplayer = true;

        // SERVER.md 6.5 anti-cheat / fair play rules:
        parameters.GodMode = false;
        parameters.TradeHintsEnabled = false;

        return setup;
    }

    /// <summary>
    /// Computes the ruleset hash for this lobby's ruleset and mods.
    /// Refuses joiners whose ruleset hash does not match the host's.
    /// </summary>
    public string ComputeRulesetHash()
    {
        return CommandLogHeader.RulesetHashOf(ToGameSetupInfo().GameParameters);
    }
}
