using Unciv.Logic;
using Unciv.Logic.Map;

namespace Unciv.DevConsole;

/// <summary>
/// Entry point of the engine side of the dev console: turns a line of text into a <see cref="ConsolePlan"/> and offers autocomplete.
/// Applying goes through the <c>run-console-command</c> game command, never directly, so every console edit is logged and replayable.
/// </summary>
public static class ConsoleRunner
{
    public const int MaxLineLength = 1000;

    /// <summary>Why a game without God mode refuses the console (the command and the C046 queries say the same).</summary>
    public const string GodModeRequired = "The developer console needs God mode, enabled when the game was set up";

    public const string LineTooLong = "The console line is too long";

    private static readonly ConsoleCommandRoot Root = new();

    /// <summary>Plans (does not run) <paramref name="line"/>. Pure: the game is not changed.</summary>
    public static ConsolePlan Plan(GameInfo game, string actorId, string line, ConsoleSelection? selection = null)
    {
        var console = new ConsoleContext(game, actorId, selection ?? new ConsoleSelection());
        return Root.Handle(console, CliInput.SplitToCliInput(line.Trim()));
    }

    /// <summary>The text replacing the last word of <paramref name="line"/>, and the number of characters it replaces (Kotlin <c>getAutocomplete</c>).</summary>
    public static (int ReplaceLength, AutocompleteResult Result)? Autocomplete(GameInfo game, string actorId, string line, ConsoleSelection? selection = null)
    {
        var console = new ConsoleContext(game, actorId, selection ?? new ConsoleSelection());
        var raw = CliInput.SplitToCliInput(line);
        var parameters = raw.Count == 1 && Root.Subcommands.ContainsKey(raw[0].ToString()) ? [.. raw, CliInput.Empty] : raw;
        var result = Root.Autocomplete(console, parameters);
        if (result?.Text == null) return null;
        return (parameters.Count > 0 ? parameters[^1].OriginalLength() : 0, result);
    }

    /// <summary>Positions of all tiles matching <paramref name="filter"/> (case sensitive), in tile list order. Pure query (<c>tile find</c>).</summary>
    public static IReadOnlyList<HexCoord> FindTiles(GameInfo game, string filter) =>
        [.. game.TileMap.TileList.Where(it => it.MatchesFilter(filter)).Select(it => it.Position)];

    public static IEnumerable<(IReadOnlyList<string> Nodes, string Format)> EnumerateCommands() => Root.EnumerateCommands();
}
