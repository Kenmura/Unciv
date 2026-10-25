using System.Globalization;
using System.Text;
using Unciv.Logic.Civilization.Diplomacy;

namespace Unciv.DevConsole;

/// <summary>Kotlin: <c>ConsoleDiplomacyCommands</c>. Planning must not mutate: "meeting" (which creates the managers) happens in the effect.</summary>
internal static class ConsoleDiplomacyCommands
{
    public static ConsoleCommandNode Create()
    {
        var node = new ConsoleCommandNode();
        node.Subcommands["list"] = new ConsoleAction("diplomacy list <civName> <civName>", (console, parameters) =>
        {
            var (civ1, civ2) = GetCivs(console, parameters);
            var diplo = civ1.GetDiplomacyManager(civ2);
            if (diplo == null) return ConsolePlan.Hint("not met");
            var sb = new StringBuilder();
            sb.Append(CultureInfo.InvariantCulture, $"Relation: {diplo.RelationshipLevel()}\n");
            if (diplo.CivInfo.IsCityState) sb.Append(CultureInfo.InvariantCulture, $"Influence: {diplo.Influence}\n");
            foreach (var (modifier, value) in diplo.DiplomaticModifiers) sb.Append(CultureInfo.InvariantCulture, $"{modifier} = {value}\n");
            foreach (var (flag, count) in diplo.FlagsCountdown) sb.Append(CultureInfo.InvariantCulture, $"{flag}: {count} turns\n");
            return ConsolePlan.Hint(sb.ToString());
        });
        node.Subcommands["meet"] = new ConsoleAction("diplomacy meet <civName> <civName>", (console, parameters) =>
        {
            var (civ1, civ2) = GetCivs(console, parameters);
            return ConsolePlan.Do(() => civ1.GetDiplomacyManagerOrMeet(civ2));
        });
        node.Subcommands["declare-war"] = new ConsoleAction("diplomacy declare-war <civName> <civName>", (console, parameters) =>
        {
            var (civ1, civ2) = GetCivs(console, parameters);
            return ConsolePlan.Do(() => civ1.GetDiplomacyManagerOrMeet(civ2).DeclareWar(new DeclareWarReason(WarType.DirectWar)));
        });
        node.Subcommands["make-peace"] = new ConsoleAction("diplomacy make-peace <civName> <civName>", (console, parameters) =>
        {
            var (civ1, civ2) = GetCivs(console, parameters);
            return ConsolePlan.Do(() => civ1.GetDiplomacyManagerOrMeet(civ2).MakePeace());
        });
        node.Subcommands["setflag"] = new ConsoleAction("diplomacy setflag <civName> <civName> <diplomacyFlag> <amount>", (console, parameters) =>
        {
            var (civ1, civ2) = GetCivs(console, parameters);
            var flag = parameters[2].EnumValue<DiplomacyFlags>();
            int amount = parameters[3].ToInt();
            return ConsolePlan.Do(() => civ1.GetDiplomacyManagerOrMeet(civ2).SetFlag(flag, amount));
        });
        node.Subcommands["removeflag"] = new ConsoleAction("diplomacy removeflag <civName> <civName> <diplomacyFlag>", (console, parameters) =>
        {
            var (civ1, civ2) = GetCivs(console, parameters);
            var flag = parameters[2].EnumValue<DiplomacyFlags>();
            return ConsolePlan.Do(() => civ1.GetDiplomacyManagerOrMeet(civ2).RemoveFlag(flag));
        });
        node.Subcommands["addmodifier"] = new ConsoleAction("diplomacy addmodifier <civName> <civName> <diplomaticModifier> <amount>", (console, parameters) =>
        {
            var (civ1, civ2) = GetCivs(console, parameters);
            var modifier = parameters[2].EnumValue<DiplomaticModifiers>();
            float amount = parameters[3].ToFloat();
            return ConsolePlan.Do(() => civ1.GetDiplomacyManagerOrMeet(civ2).AddModifier(modifier, amount));
        });
        node.Subcommands["removemodifier"] = new ConsoleAction("diplomacy removemodifier <civName> <civName> <diplomaticModifier>", (console, parameters) =>
        {
            var (civ1, civ2) = GetCivs(console, parameters);
            var modifier = parameters[2].EnumValue<DiplomaticModifiers>();
            return ConsolePlan.Do(() => civ1.GetDiplomacyManagerOrMeet(civ2).RemoveModifier(modifier));
        });
        node.Subcommands["setinfluence"] = new ConsoleAction("diplomacy setinfluence <civName> <civName> <amount>", (console, parameters) =>
        {
            var (civ1, civ2) = GetCivs(console, parameters);
            if (!civ1.IsCityState || !civ2.IsMajorCiv()) throw new ConsoleErrorException("first civ must be the city-state, second a major civ");
            float amount = parameters[2].ToFloat();
            return ConsolePlan.Do(() => civ1.GetDiplomacyManagerOrMeet(civ2).SetInfluence(amount));
        });
        node.Subcommands["denounce"] = new ConsoleAction("diplomacy denounce <civName> <civName>", (console, parameters) =>
        {
            var (civ1, civ2) = GetCivs(console, parameters);
            return ConsolePlan.Do(() => civ1.GetDiplomacyManagerOrMeet(civ2).Denounce());
        });
        return node;
    }

    private static (Logic.Civilization.Civilization, Logic.Civilization.Civilization) GetCivs(ConsoleContext console, IReadOnlyList<CliInput> parameters)
    {
        if (parameters.Count < 2) throw new ConsoleErrorException("command needs two civ names");
        var civ1 = console.GetCivByName(parameters[0]);
        var civ2 = console.GetCivByName(parameters[1]);
        // Deviation from Kotlin: a civ has no diplomacy with itself, the manager lookup would fail.
        if (ReferenceEquals(civ1, civ2)) throw new ConsoleErrorException("command needs two different civs");
        if (civ1.IsDefeated() || civ2.IsDefeated()) throw new ConsoleErrorException("both civs must be alive");
        return (civ1, civ2);
    }
}
