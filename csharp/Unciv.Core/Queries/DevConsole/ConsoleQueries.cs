using Unciv.DevConsole;
using Unciv.Logic.Map;
using Unciv.View;

namespace Unciv.Queries.DevConsole;

// C046: what the dev console popup asks the engine before it sends a line. Both queries run the same ConsoleRunner the logged run-console-command does,
// on the same selection (tile, unit, civ) the client sends along, so what the popup shows is what the command will do. Pure: the plan is built, never run.
// The console is a cheat, so, like the command, both refuse a game without God mode (and a line over ConsoleRunner.MaxLineLength) with the same text.

/// <summary>
/// The result of planning one console line. <see cref="Kind"/> is "Ok" (the command will run), "Hint" (a query or a usage text; the command refuses it) or "Error";
/// <see cref="Message"/> is the text to show (null for a plain OK).
/// </summary>
public sealed record ConsolePlanResult(string Kind, string? Message);

/// <summary>
/// The text that replaces the last <see cref="ReplaceLength"/> characters of the line with <see cref="Text"/> (null: nothing to complete), the list of
/// matching completions or usage text to show as <see cref="Message"/>, and whether that message is an error.
/// </summary>
public sealed record ConsoleAutocompleteResult(int ReplaceLength, string? Text, string? Message, bool IsError);

internal static class ConsoleQueryChecks
{
    /// <summary>Why the console cannot be used with this line, or null.</summary>
    public static string? Refusal(Logic.GameInfo game, string line)
    {
        if (!game.GameParameters.GodMode) return ConsoleRunner.GodModeRequired;
        return line.Length > ConsoleRunner.MaxLineLength ? ConsoleRunner.LineTooLong : null;
    }
}

// Plans the line as the viewer: the response the popup shows (error, hint, or the OK text) before it sends the command.
[QueryType("console.Plan")]
public sealed record ConsolePlanQuery(string Line, HexCoord? Tile = null, int? UnitId = null, string? Civ = null) : IQuery<ConsolePlanResult>
{
    public string Type => "console.Plan";

    public ConsolePlanResult Run(GameView view)
    {
        var game = view.Unwrap();
        if (ConsoleQueryChecks.Refusal(game, Line) is { } refusal) return new ConsolePlanResult(nameof(ConsoleResponseKind.Error), refusal);
        var response = ConsoleRunner.Plan(game, view.CivView.Unwrap().CivName, Line, new ConsoleSelection(Tile, UnitId, Civ)).Response;
        return new ConsolePlanResult(response.Kind.ToString(), response.Message);
    }
}

// Kotlin getAutocomplete: completes the last word of the line from the command tree and the game's names.
[QueryType("console.Autocomplete")]
public sealed record ConsoleAutocompleteQuery(string Line, HexCoord? Tile = null, int? UnitId = null, string? Civ = null) : IQuery<ConsoleAutocompleteResult>
{
    public string Type => "console.Autocomplete";

    public ConsoleAutocompleteResult Run(GameView view)
    {
        var game = view.Unwrap();
        if (ConsoleQueryChecks.Refusal(game, Line) is { } refusal) return new ConsoleAutocompleteResult(0, null, refusal, true);
        try
        {
            var completed = ConsoleRunner.Autocomplete(game, view.CivView.Unwrap().CivName, Line, new ConsoleSelection(Tile, UnitId, Civ));
            return completed is { } found
                ? new ConsoleAutocompleteResult(found.ReplaceLength, found.Result.Text, found.Result.Message, false)
                : new ConsoleAutocompleteResult(0, null, null, false);
        }
        catch (ConsoleErrorException error)
        {
            return new ConsoleAutocompleteResult(0, null, error.Error, true);
        }
    }
}
