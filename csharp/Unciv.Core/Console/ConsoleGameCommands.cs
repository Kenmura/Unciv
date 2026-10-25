namespace Unciv.DevConsole;

/// <summary>
/// Kotlin: <c>ConsoleGameCommands</c>. <c>remove-spectator</c> is only the state change: the Kotlin command also confirms, saves an autosave and
/// reloads it to clear up the rest, which is the client's business (T234).
/// </summary>
internal static class ConsoleGameCommands
{
    public static ConsoleCommandNode Create()
    {
        var node = new ConsoleCommandNode();
        node.Subcommands["setdifficulty"] = new ConsoleAction("game setdifficulty <difficulty>", (console, parameters) =>
        {
            var difficulty = parameters[0].FindOrNull(console.Game.Ruleset.Difficulties.Values, it => it.Name)
                ?? throw new ConsoleErrorException("Unrecognized difficulty");
            return ConsolePlan.Do(() =>
            {
                console.Game.Difficulty = difficulty.Name;
                console.Game.SetTransients();
            });
        });
        node.Subcommands["setturn"] = new ConsoleAction("game setturn <nonNegativeAmount>", (console, parameters) =>
        {
            int turn = parameters[0].ToInt();
            // Deviation from Kotlin: the format says non-negative, Kotlin does not check; a negative turn count corrupts turn arithmetic.
            if (turn < 0) throw new ConsoleErrorException("Number out of range");
            return ConsolePlan.Do(() =>
            {
                console.Game.Turns = turn;
                console.Game.SetTransients();
            });
        });
        node.Subcommands["add-spectator"] = new ConsoleAction("game add-spectator", (console, _) =>
        {
            if (console.Game.Civilizations.Any(it => it.Nation.IsSpectator)) throw new ConsoleErrorException("Spectator already exists");
            return ConsolePlan.Do(() => console.Game.GetSpectator(""));
        });
        node.Subcommands["remove-spectator"] = new ConsoleAction("game remove-spectator", (console, _) =>
        {
            var game = console.Game;
            var spectator = game.Civilizations.FirstOrDefault(it => it.Nation.IsSpectator)
                ?? throw new ConsoleErrorException("No Spectator in this game");
            return ConsolePlan.Do(() =>
            {
                if (game.CurrentPlayer == spectator.CivId) game.CurrentPlayer = "";
                game.RemoveCivilization(spectator);
                // dunno why a spectator makes it into everybody's diplomacy maps
                foreach (var civ in game.Civilizations) civ.Diplomacy.Remove(Constants.Spectator);
            });
        });
        return node;
    }
}
