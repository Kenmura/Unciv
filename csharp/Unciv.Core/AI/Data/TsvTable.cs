namespace Unciv.AI.Data;

// Shared reader for the shipped AI tables (tab separated, header row, no quoting). Empty cells are kept as "".
internal static class TsvTable
{
    internal static string ReadResource(string name)
    {
        var assembly = typeof(TsvTable).Assembly;
        using var stream = assembly.GetManifestResourceStream("Unciv.AI.Data." + name)
            ?? throw new InvalidOperationException("Missing embedded AI table " + name);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Data rows (header and blank lines skipped), each checked to have exactly the header's column count.</summary>
    internal static List<string[]> Rows(string text, string tableName, string[] expectedHeader)
    {
        var rows = new List<string[]>();
        bool headerSeen = false;
        int lineNumber = 0;
        foreach (string rawLine in text.Split('\n'))
        {
            lineNumber++;
            string line = rawLine.TrimEnd('\r');
            if (line.Length == 0) continue;
            string[] cells = line.Split('\t');
            if (!headerSeen)
            {
                if (!cells.AsSpan().SequenceEqual(expectedHeader))
                    throw new FormatException($"{tableName}: unexpected header '{line}'");
                headerSeen = true;
                continue;
            }
            if (cells.Length != expectedHeader.Length)
                throw new FormatException($"{tableName} line {lineNumber}: expected {expectedHeader.Length} columns, found {cells.Length}");
            rows.Add(cells);
        }
        if (!headerSeen) throw new FormatException($"{tableName}: missing header");
        return rows;
    }
}
