using System.Globalization;
using System.Text;

namespace Unciv.Assets;

/// <summary>One piece of a line split by <see cref="UiText.Runs"/>: plain text, or the stat whose icon replaces a glyph.</summary>
public readonly record struct TextRun(string Text, string? Stat);

/// <summary>
/// C065: Godot-free text helpers of the shared UI components (design/ui-redesign.md 2.2 and 2.6): sign-coloured deltas built from tokens and the split of
/// Unciv's stat glyphs (food, gold, ...) out of a line so a view can draw their icons inline.
/// </summary>
public static class UiText
{
    private static readonly Dictionary<char, string> GlyphStats = new()
    {
        ['⁂'] = "Food",
        ['⚙'] = "Production",
        ['¤'] = "Gold",
        ['⍾'] = "Science",
        ['♪'] = "Culture",
        ['⌣'] = "Happiness",
        ['☮'] = "Faith",
    };

    /// <summary>The stat a Unciv font glyph stands for (<c>Fonts.Gold</c> is Gold), or null for any other character.</summary>
    public static string? StatForGlyph(char glyph) => GlyphStats.GetValueOrDefault(glyph);

    /// <summary>BBCode colour value (<c>#RRGGBB</c>) of a token.</summary>
    public static string Hex(Rgb colour) => string.Create(CultureInfo.InvariantCulture, $"#{colour.R:X2}{colour.G:X2}{colour.B:X2}");

    /// <summary>The colour of a signed delta: <see cref="UiTokens.Success"/> above zero, <see cref="UiTokens.DangerText"/> below, <see cref="UiTokens.TextMuted"/> at zero.</summary>
    public static Rgb DeltaColour(int value) => value > 0 ? UiTokens.Success : value < 0 ? UiTokens.DangerText : UiTokens.TextMuted;

    /// <summary>The signed text of a delta: "+3", "-2" (ASCII minus, so the tabular figures line up) or "0".</summary>
    public static string DeltaText(int value) => value > 0 ? "+" + value.ToString(CultureInfo.InvariantCulture) : value.ToString(CultureInfo.InvariantCulture);

    /// <summary>A delta as a BBCode colour span built from the tokens, e.g. <c>[color=#5DBB63]+3[/color]</c>.</summary>
    public static string Delta(int value) => $"[color={Hex(DeltaColour(value))}]{DeltaText(value)}[/color]";

    /// <summary>The tooltip of an icon button: "Label (Key)", or the label alone without a hotkey.</summary>
    public static string TooltipFor(string label, string? hotkey) => string.IsNullOrEmpty(hotkey) ? label : $"{label} ({hotkey})";

    /// <summary>The text of a count badge: empty at zero or below (the badge hides), the number up to 99, "99+" beyond.</summary>
    public static string BadgeText(int count) => count <= 0 ? "" : count > 99 ? "99+" : count.ToString(CultureInfo.InvariantCulture);

    /// <summary>Splits a line at its stat glyphs: "3 ¤ 2 ⚙" gives "3 ", Gold, " 2 ", Production. Text without glyphs is a single run; empty text gives none.</summary>
    public static IReadOnlyList<TextRun> Runs(string text)
    {
        var runs = new List<TextRun>();
        var plain = new StringBuilder();
        foreach (char c in text)
        {
            if (StatForGlyph(c) is not { } stat)
            {
                plain.Append(c);
                continue;
            }
            if (plain.Length > 0) runs.Add(new TextRun(plain.ToString(), null));
            plain.Clear();
            runs.Add(new TextRun(c.ToString(), stat));
        }
        if (plain.Length > 0) runs.Add(new TextRun(plain.ToString(), null));
        return runs;
    }
}

/// <summary>C065: the row order of a sortable table: numbers by value, anything else by ordinal text, stable for equal cells.</summary>
public static class TableSorter
{
    /// <summary>The cell as a finite number in invariant format ("12", "-3.5", "+4"), or null ("12%" and "1,200" are text).</summary>
    public static double? ParseNumber(string cell) =>
        double.TryParse(cell.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && double.IsFinite(value) ? value : null;

    /// <summary>
    /// The indices of <paramref name="rows"/> ordered by <paramref name="column"/>. A column sorts numerically when every non-empty cell parses as a number
    /// (empty cells go last either way). Equal cells keep their input order, descending included.
    /// </summary>
    public static IReadOnlyList<int> Order(IReadOnlyList<IReadOnlyList<string>> rows, int column, bool ascending)
    {
        string Cell(int row) => column < rows[row].Count ? rows[row][column] : "";
        bool numeric = Enumerable.Range(0, rows.Count).All(i => Cell(i).Length == 0 || ParseNumber(Cell(i)) != null);
        int Compare(int a, int b)
        {
            string x = Cell(a), y = Cell(b);
            if (x.Length == 0 || y.Length == 0) return x.Length == y.Length ? 0 : x.Length == 0 ? 1 : -1;
            int c = numeric ? ParseNumber(x)!.Value.CompareTo(ParseNumber(y)!.Value) : string.CompareOrdinal(x, y);
            return ascending ? c : -c;
        }
        return Enumerable.Range(0, rows.Count).OrderBy(i => i, Comparer<int>.Create((a, b) => Compare(a, b) is var c and not 0 ? c : a.CompareTo(b))).ToList();
    }
}
