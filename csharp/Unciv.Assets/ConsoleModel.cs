using Unciv.DevConsole;

namespace Unciv.Assets;

/// <summary>How a console response is shown: <see cref="Rgb"/> from the palette (green: the command ran; gold: usage or an answer; red: refused).</summary>
public enum ConsoleTone
{
    Ok,
    Hint,
    Error,
}

/// <summary>
/// C046: the rules of the developer console popup as plain data, Godot-free so they are unit-tested: the input history (Kotlin <c>addHistory</c> and
/// <c>navigateHistory</c>), how a completion edits the line (<c>onAutocomplete</c>) and how the engine's response kinds are shown. Deliberate deviation from Kotlin: a
/// history line is stored trimmed, and a stored history from a hand-edited settings file is made safe (<see cref="SanitizeHistory"/>).
/// </summary>
public static class ConsoleModel
{
    /// <summary>Kotlin <c>maxHistorySize</c>.</summary>
    public const int MaxHistory = 42;

    /// <summary>The history after a line ran. A blank line, and a line equal (ignoring case) to the last entry, change nothing. When the history is full, an older equal entry is
    /// removed first, else the oldest entry (Kotlin <c>addHistory</c>).</summary>
    public static List<string> AddHistory(IReadOnlyList<string> history, string line)
    {
        var result = history.ToList();
        string text = line.Trim();
        if (text.Length == 0) return result;
        if (result.Count > 0 && string.Equals(result[^1], text, StringComparison.OrdinalIgnoreCase)) return result;
        if (result.Count >= MaxHistory)
        {
            result.RemoveAll(it => string.Equals(it, text, StringComparison.OrdinalIgnoreCase));
            if (result.Count >= MaxHistory) result.RemoveAt(0);
        }
        result.Add(text);
        return result;
    }

    /// <summary>A stored history made safe: blank and over-long lines are dropped and only the newest <see cref="MaxHistory"/> lines stay.</summary>
    public static List<string> SanitizeHistory(IEnumerable<string>? stored) =>
        stored?.Where(it => !string.IsNullOrWhiteSpace(it) && it.Length <= ConsoleRunner.MaxLineLength).TakeLast(MaxHistory).ToList() ?? [];

    /// <summary>The history position after pressing up (<paramref name="delta"/> -1) or down (+1): clamped into the list, -1 for an empty history (Kotlin <c>navigateHistory</c>).
    /// <paramref name="current"/> may be <paramref name="count"/>, the "new line" position past the last entry.</summary>
    public static int Step(int count, int current, int delta) => count == 0 ? -1 : Math.Clamp(current + delta, 0, count - 1);

    /// <summary>The line after a completion: the last <paramref name="replaceLength"/> characters are replaced by <paramref name="completion"/> (Kotlin <c>onAutocomplete</c>).</summary>
    public static string ApplyCompletion(string line, int replaceLength, string completion) =>
        line[..Math.Max(0, line.Length - replaceLength)] + completion;

    /// <summary>The tone of a response kind as the engine names it (<see cref="ConsoleResponseKind"/>); anything unknown is shown as an error.</summary>
    public static ConsoleTone ToneOf(string kind) => kind switch
    {
        nameof(ConsoleResponseKind.Ok) => ConsoleTone.Ok,
        nameof(ConsoleResponseKind.Hint) => ConsoleTone.Hint,
        _ => ConsoleTone.Error,
    };

    public static Rgb ColourOf(ConsoleTone tone) => tone switch
    {
        ConsoleTone.Ok => PlaceholderPalette.HealthGood,
        ConsoleTone.Hint => PlaceholderPalette.HealthWarn,
        _ => PlaceholderPalette.ErrorText,
    };
}
