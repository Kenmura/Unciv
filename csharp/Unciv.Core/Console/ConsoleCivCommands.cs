using Unciv.Logic.Civilization;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.StatTypes;

namespace Unciv.DevConsole;

/// <summary>
/// Kotlin: <c>ConsoleCivCommands</c>. Not ported: <c>activatetrigger</c> (needs the Unique validator and trigger activation, see ticket notes),
/// <c>add</c> and <c>remove</c> (scenario building, <c>remove</c> asks a confirmation popup).
/// </summary>
internal static class ConsoleCivCommands
{
    public static ConsoleCommandNode Create()
    {
        var node = new ConsoleCommandNode();
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
        return node;
    }

    // Kotlin findCliInput<Policy>: yes this also finds PolicyBranch instances
    private static Policy FindPolicy(ConsoleContext console, CliInput name)
    {
        var ruleset = console.Game.Ruleset;
        return name.FindOrNull(ruleset.Policies.Values.Concat(ruleset.PolicyBranches.Values), it => it.Name)
            ?? throw new ConsoleErrorException("Unrecognized policy");
    }
}
