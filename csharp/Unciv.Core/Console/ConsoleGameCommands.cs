namespace Unciv.DevConsole;

/// <summary>
/// Kotlin: <c>ConsoleGameCommands</c>. <c>remove-spectator</c> is not ported: it saves, reloads and swaps the world screen (a client concern).
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
        return node;
    }
}
