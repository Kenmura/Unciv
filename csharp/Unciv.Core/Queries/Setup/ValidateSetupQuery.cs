using Unciv.Logic.Civilization;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Validation;

namespace Unciv.Queries.Setup;

// Code: stable machine readable id; Message: the untranslated Kotlin text where one exists
public sealed record SetupError(string Code, string Message);

public sealed record SetupValidation(bool Ok, IReadOnlyList<SetupError> Errors, IReadOnlyList<string> RulesetWarnings);

// T199: the checks the new-game screen makes before Start is enabled (PlayerPickerTable.update, NewGameScreen.getErrorMessage) plus
// the RulesetValidator errors of the combined ruleset. Pure: works on a clone of the setup (FixUndesiredSizes mutates) and draws no
// RNG. Not checked here: reachability of a saved map file, the multiplayer server (client concerns), and 'players vs map size', which
// the Kotlin screen does not check either (GameStarter places what fits).
public sealed record ValidateSetupQuery(GameSetupInfo Setup, bool IsHostedGame = false)
{
    public const string Type = "setup.ValidateSetup";

    public SetupValidation Run()
    {
        var errors = new List<SetupError>();
        var warnings = new List<string>();
        var parameters = Setup.GameParameters;
        var ruleset = RulesetCache.GetComplexRuleset(parameters);
        var online = IsHostedGame || parameters.IsOnlineMultiplayer;

        void Add(string code, string message) => errors.Add(new SetupError(code, message));

        var choosableCount = ruleset.Nations.Values.Count(NewGameOptionsQuery.IsChoosable);
        if (parameters.Players.Count > choosableCount)
            Add("TooManyPlayers", "There are more players than playable nations in this ruleset");
        if (!parameters.RandomNumberOfPlayers && parameters.Players.Count < 2)
            Add("TooFewPlayers", "A game needs at least two players");

        var seenNations = new HashSet<string>(StringComparer.Ordinal);
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var player in parameters.Players)
        {
            if (player.ChosenCiv != Constants.Random)
            {
                if (!ruleset.Nations.TryGetValue(player.ChosenCiv, out var nation))
                    Add("UnknownNation", $"Unknown nation {player.ChosenCiv}");
                else if (!NewGameOptionsQuery.IsChoosable(nation))
                    Add("NationNotChoosable", $"Nation {player.ChosenCiv} cannot be chosen");
                // several spectators are allowed
                else if (player.ChosenCiv != Constants.Spectator && !seenNations.Add(player.ChosenCiv))
                    Add("DuplicateNation", $"Nation {player.ChosenCiv} is chosen more than once");
            }
            if (player.ChosenCiv == Constants.Spectator && player.PlayerType != PlayerType.Human)
                Add("SpectatorMustBeHuman", "Spectators can only be human");
            if (online && player.PlayerType == PlayerType.Human)
            {
                if (player.PlayerId == "") Add("MissingPlayerId", "Invalid player ID!");
                else if (!seenIds.Add(player.PlayerId)) Add("DuplicatePlayerId", $"Player ID {player.PlayerId} is used twice");
            }
        }

        if (!parameters.Players.Any(it => it.PlayerType == PlayerType.Human && !(it.ChosenCiv == Constants.Spectator && online)))
            Add("NoHumanPlayers", "No human players selected!");
        if (parameters.VictoryTypes.Count == 0)
            Add("NoVictoryTypes", "No victory conditions were selected!");
        else if (parameters.VictoryTypes.Any(it => !ruleset.Victories.ContainsKey(it)))
            Add("UnknownVictoryType", "A selected victory condition does not exist in this ruleset");
        if (!ruleset.Difficulties.ContainsKey(parameters.Difficulty))
            Add("UnknownDifficulty", $"Unknown difficulty {parameters.Difficulty}");
        if (!ruleset.Speeds.ContainsKey(parameters.Speed))
            Add("UnknownSpeed", $"Unknown game speed {parameters.Speed}");
        if (!ruleset.Eras.ContainsKey(parameters.StartingEra))
            Add("UnknownEra", $"Unknown starting era {parameters.StartingEra}");

        if (Setup.MapParameters.Name == "")
        {
            var message = Setup.MapParameters.Clone().MapSize.FixUndesiredSizes(Setup.MapParameters.WorldWrap);
            if (message != null) Add("MapSize", message);
        }

        var (_, rulesetErrors) = RulesetCache.CheckCombinedModLinks(parameters.Mods, parameters.BaseRuleset);
        foreach (var error in rulesetErrors)
        {
            if (error.ErrorSeverityToReport == RulesetErrorSeverity.Error) Add("RulesetError", error.Text);
            else if (error.ErrorSeverityToReport != RulesetErrorSeverity.OK) warnings.Add(error.Text);
        }

        return new SetupValidation(errors.Count == 0, errors, warnings);
    }
}
