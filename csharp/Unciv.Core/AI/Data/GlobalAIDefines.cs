using System.Collections.Immutable;
using System.Globalization;

namespace Unciv.AI.Data;

// The ~450 tunables of Civ 5's GlobalAIDefines.xml (data/ai_defines.tsv), one value per edition.
// A define missing in an edition (empty cell) does not exist there: TryGet* return false, Get* throw.
// Values keep their file order. A value written without a decimal point is an integer define; GetInt rejects the rest.
public sealed class GlobalAIDefines
{
    private static readonly ImmutableArray<string> Header = ["name", "base", "gk", "bnw"];

    private readonly OrderedDictionary<string, string?[]> entries = new();

    private GlobalAIDefines() { }

    public static GlobalAIDefines Parse(string tsv)
    {
        var result = new GlobalAIDefines();
        foreach (string[] cells in TsvTable.Rows(tsv, "ai_defines", Header.AsSpan()))
        {
            var values = new string?[3];
            for (int i = 0; i < 3; i++)
            {
                string cell = cells[i + 1];
                if (cell.Length == 0) continue;
                if (!double.TryParse(cell, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                    throw new FormatException($"ai_defines: '{cells[0]}' has non-numeric value '{cell}'");
                values[i] = cell;
            }
            if (!result.entries.TryAdd(cells[0], values))
                throw new FormatException($"ai_defines: duplicate define '{cells[0]}'");
        }
        return result;
    }

    public static GlobalAIDefines LoadEmbedded() => Parse(TsvTable.ReadResource("ai_defines.tsv"));

    public int Count => entries.Count;

    /// <summary>All define names in file order, including those absent in some editions.</summary>
    public IEnumerable<string> Names => entries.Keys;

    public bool IsDefined(string name, AIEdition edition) => entries.TryGetValue(name, out var v) && v[(int)edition] != null;

    public bool TryGetDouble(string name, AIEdition edition, out double value)
    {
        if (entries.TryGetValue(name, out var v) && v[(int)edition] is { } text)
        {
            value = double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
            return true;
        }
        value = 0;
        return false;
    }

    public double GetDouble(string name, AIEdition edition) =>
        TryGetDouble(name, edition, out double value) ? value : throw Missing(name, edition);

    /// <summary>The value as the engine's 32-bit float.</summary>
    public float GetFloat(string name, AIEdition edition) => (float)GetDouble(name, edition);

    /// <summary>The value of an integer define. Throws for a define written with a decimal point.</summary>
    public int GetInt(string name, AIEdition edition)
    {
        if (!entries.TryGetValue(name, out var v) || v[(int)edition] is not { } text) throw Missing(name, edition);
        if (!int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value))
            throw new FormatException($"Define {name} is not an integer: {text}");
        return value;
    }

    public bool GetBool(string name, AIEdition edition) => GetInt(name, edition) != 0;

    private static KeyNotFoundException Missing(string name, AIEdition edition) =>
        new($"AI define {name} does not exist in {edition}");
}
