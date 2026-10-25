using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Ruleset.Unit;
using Unciv.Models.StatTypes;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;
using NationObject = Unciv.Models.Ruleset.Nation.Nation;

namespace Unciv.DevConsole;

/// <summary>
/// Kotlin: <c>ConsoleCivCommands</c>. <c>remove</c> has no confirmation popup here (T234): confirming is the client's job before it sends the line.
/// </summary>
internal static class ConsoleCivCommands
{
    public static ConsoleCommandNode Create()
    {
        var node = new ConsoleCommandNode();
        node.Subcommands["activatetrigger"] = new ConsoleTriggerAction("civ");
        node.Subcommands["addstat"] = new ConsoleAction("civ addstat <stat> <amount> [civ]", (console, parameters) =>
        {
            var stat = parameters[0].ToStat();
            if (!StatExtensions.StatsWithCivWideField.Contains(stat)) throw new ConsoleErrorException($"{stat} is not civ-wide");
            int amount = parameters[1].ToInt();
            var civ = console.GetCivByNameOrSelected(parameters.Count > 2 ? parameters[2] : null);
            return ConsolePlan.Do(() => civ.AddStat(stat, amount));
        });
        node.Subcommands["setplayertype"] = new ConsoleAction("civ setplayertype <civName> <ai/human>", (console, parameters) =>
        {
            var civ = console.GetCivByName(parameters[0]);
            if (!civ.IsMajorCiv()) throw new ConsoleErrorException("Can only change player type for major civs");
            var type = parameters[1].EnumValue<PlayerType>();
            return ConsolePlan.Do(() => civ.PlayerType = type);
        });
        node.Subcommands["revealmap"] = new ConsoleAction("civ revealmap [civName]", (console, parameters) =>
        {
            var civ = console.GetCivByNameOrSelected(parameters.Count > 0 ? parameters[0] : null);
            return ConsolePlan.Do(() =>
            {
                foreach (var tile in civ.GameInfo.TileMap.Values) tile.SetExplored(civ, true);
            });
        });
        node.Subcommands["addpolicy"] = new ConsoleAction("civ addpolicy <civName> <policyName>", (console, parameters) =>
        {
            var civ = console.GetCivByName(parameters[0]);
            var policy = FindPolicy(console, parameters[1]);
            if (civ.Policies.IsAdopted(policy.Name)) return ConsolePlan.Hint($"{civ.CivId} already has adopted {policy.Name}");
            return ConsolePlan.Do(() =>
            {
                civ.Policies.FreePolicies++;
                civ.Policies.Adopt(policy);
            });
        });
        node.Subcommands["removepolicy"] = new ConsoleAction("civ removepolicy <civName> <policyName>", (console, parameters) =>
        {
            var civ = console.GetCivByName(parameters[0]);
            var policy = FindPolicy(console, parameters[1]);
            if (!civ.Policies.IsAdopted(policy.Name)) return ConsolePlan.Hint($"{civ.CivId} does not have {policy.Name}");
            return ConsolePlan.Do(() => civ.Policies.RemovePolicy(policy, assumeWasFree: true)); // See UniqueType.OneTimeRemovePolicy
        });
        node.Subcommands["addtechnology"] = new ConsoleAction("civ addtechnology <civName> <techName>", (console, parameters) =>
        {
            var civ = console.GetCivByName(parameters[0]);
            var tech = parameters[1].FindOrNull(console.Game.Ruleset.Technologies.Values, it => it.Name)
                ?? throw new ConsoleErrorException("Unrecognized technology");
            if (civ.Tech.IsResearched(tech.Name)) return ConsolePlan.Hint($"{civ.CivId} already has researched {tech.Name}");
            return ConsolePlan.Do(() => civ.Tech.AddTechnology(tech.Name, false));
        });
        node.Subcommands["removetechnology"] = new ConsoleAction("civ removetechnology <civName> <techName>", (console, parameters) =>
        {
            var civ = console.GetCivByName(parameters[0]);
            var tech = parameters[1].FindOrNull(console.Game.Ruleset.Technologies.Values, it => it.Name)
                ?? throw new ConsoleErrorException("Unrecognized technology");
            if (!civ.Tech.IsResearched(tech.Name)) return ConsolePlan.Hint($"{civ.CivId} does not have {tech.Name}");
            return ConsolePlan.Do(() => civ.Tech.TechsResearched.Remove(tech.Name));
        });
        node.Subcommands["checkcountable"] = new ConsoleAction("civ checkcountable <countable> [civName]", (console, parameters) =>
        {
            var civ = console.GetCivByNameOrSelected(parameters.Count > 1 ? parameters[1] : null);
            int? amount = Countables.GetCountableAmount(parameters[0].OriginalUnquoted(), new GameContext(civ));
            return ConsolePlan.Hint(amount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "Invalid countable");
        });
        // This is meant for Scenario building. It adds any Civ in form of one Settler which isn't already in the game.
        node.Subcommands["add"] = new ConsoleAction("civ add <newCivName>", (console, parameters) =>
        {
            var game = console.Game;
            var ruleset = game.Ruleset;
            var nation = parameters[0].Find(ruleset.Nations.Values.Where(n => !game.Civilizations.Any(civ => civ.Nation == n)), it => it.Name);
            var selectedTile = console.GetSelectedTile();
            var baseUnit = ruleset.Units.Values.FirstOrDefault(it => (it.UniqueTo == null || it.UniqueTo == nation.Name) && it.IsCityFounder()) // Not random - getting a Conquistador might be unfair
                ?? throw new ConsoleErrorException("No allowed Settler units in the ruleset");
            return ConsolePlan.Do(() => AddCiv(game, nation, baseUnit, selectedTile.Position));
        });
        node.Subcommands["remove"] = new ConsoleAction("civ remove <civName>", (console, parameters) =>
        {
            var civ = console.GetCivByName(parameters[0]);
            if (civ.CivId == console.Game.CurrentPlayer) throw new ConsoleErrorException("Suicide is not yet supported");
            return ConsolePlan.Do(() => Annihilate(console.Game, civ));
        });
        return node;
    }

    /// <summary>Kotlin <c>civ add</c>. Planning cannot tell whether a spot for the Settler exists, so the civ is taken back out when there is none.</summary>
    private static void AddCiv(GameInfo game, NationObject nation, BaseUnit baseUnit, HexCoord position)
    {
        var civ = new CivilizationObject(nation);
        if (nation.IsCityState)
        {
            var usedMajorCivs = game.Civilizations.Where(it => it.IsMajorCiv()).Select(it => it.Nation).ToList();
            var rng = new GameContext(gameInfo: game).StateBasedRandom("DevConsole civ add");
            if (!civ.CityStateFunctions.InitCityState(game.Ruleset, game.GameParameters.StartingEra, usedMajorCivs, rng)) return; // not allowed in this game
        }
        civ.GameInfo = game;
        civ.Cache.UpdateState();
        civ.SetTransients();
        game.Civilizations.Add(civ);
        var location = civ.Units.PlaceUnitNearTile(position, baseUnit);
        civ.Cache.UpdateViewableTiles();
        if (location == null) game.RemoveCivilization(civ);
    }

    /// <summary>Kotlin <c>civ remove</c>'s <c>annihilate</c>.</summary>
    private static void Annihilate(GameInfo game, CivilizationObject civ)
    {
        // kill units. Using destroy() would be slower and might have unwelcome side effects.
        foreach (var unit in civ.Units.GetCivUnits().ToList())
        {
            civ.Units.RemoveUnit(unit);
            unit.RemoveFromTile();
        }
        // Relinquish territory
        foreach (var city in civ.Cities)
        {
            foreach (var tile in city.GetTiles().ToList()) tile.SetOwningCity(null);
            city.GetCenterTile().RemoveImprovement();
        }
        // Remove tile references outside territory
        foreach (var tile in game.TileMap.TileList)
            if (ReferenceEquals(tile.GetRoadOwner(), civ)) tile.RemoveRoadOwner();
        game.RemoveCivilization(civ);
        // Make other civs forget the victim and recall spies
        foreach (var otherCiv in game.Civilizations)
        {
            otherCiv.Diplomacy.Remove(civ.CivId);
            if (otherCiv.IsCityState) otherCiv.QuestManager.RemoveQuestsFor(civ);
            otherCiv.EspionageManager.RecallAllSpiesFrom(civ);
        }
    }

    // Kotlin findCliInput<Policy>: yes this also finds PolicyBranch instances
    private static Policy FindPolicy(ConsoleContext console, CliInput name)
    {
        var ruleset = console.Game.Ruleset;
        return name.FindOrNull(ruleset.Policies.Values.Concat(ruleset.PolicyBranches.Values), it => it.Name)
            ?? throw new ConsoleErrorException("Unrecognized policy");
    }
}
