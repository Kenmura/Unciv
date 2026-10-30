namespace Unciv.Harness;

/// <summary>
/// T290: parser for <c>bench.env</c>, the way to set the <c>UNCIV_BENCH_*</c> variables of the gpu-bench scenario on a device where environment variables cannot be set (a stock Android phone).
/// One <c>NAME=value</c> per line; blank lines and lines starting with <c>#</c> are ignored; an optional leading <c>export </c> and one pair of surrounding quotes around the value are removed.
/// </summary>
public static class BenchEnvFile
{
    /// <summary>Parses the text; later lines override earlier ones. Lines without '=' or with an empty name are skipped.</summary>
    public static Dictionary<string, string> Parse(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            if (line.StartsWith("export ", StringComparison.Ordinal)) line = line[7..].TrimStart();
            int eq = line.IndexOf('=', StringComparison.Ordinal);
            if (eq <= 0) continue;
            string name = line[..eq].Trim();
            string value = line[(eq + 1)..].Trim();
            if (name.Length == 0) continue;
            if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[^1] == value[0]) value = value[1..^1];
            result[name] = value;
        }
        return result;
    }

    /// <summary>Environment variable first (so a desktop run behaves as before), then the file's value.</summary>
    public static string? Lookup(string name, IReadOnlyDictionary<string, string> file, Func<string, string?> environment)
        => environment(name) ?? (file.TryGetValue(name, out string? value) ? value : null);
}
