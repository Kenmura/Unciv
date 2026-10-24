using System.Text;

namespace Unciv.Core.Tests.Policy;

// T119: Kotlin `x!!` throws at once; the C# null-forgiving `x!` only silences the compiler, so the failure
// shows up later or never. Production code must use `.NotNull()` (NullExtensions) or a real null check.
// A `!` that is genuinely safe (e.g. definite assignment after a guard) needs `// JUSTIFY: T###` within 3 lines above.
public class NullForgivingTests
{
    private static readonly string[] ScannedProjects = ["Unciv.Core", "Unciv.Harness", "Unciv.Godot.Harness"];

    [Fact]
    public void ProductionCodeHasNoUnjustifiedNullForgivingOperator()
    {
        var root = FindRoot();
        var offenders = new List<string>();
        foreach (var project in ScannedProjects)
        {
            var dir = Path.Combine(root, project);
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                var sep = Path.DirectorySeparatorChar;
                if (file.Contains(sep + "obj" + sep, StringComparison.Ordinal) || file.Contains(sep + "bin" + sep, StringComparison.Ordinal)) continue;
                var raw = File.ReadAllLines(file);
                foreach (var line in FindNullForgivingLines(File.ReadAllText(file)))
                    if (!HasJustification(raw, line - 1))
                        offenders.Add($"{Path.GetRelativePath(root, file)}:{line}");
            }
        }

        Assert.True(offenders.Count == 0,
            "Null-forgiving '!' hides a null (Kotlin '!!' throws). Use .NotNull() or a null-safe rewrite, or add `// JUSTIFY: T### why` within 3 lines above: "
            + string.Join(", ", offenders));
    }

    [Fact]
    public void ScannerFindsOnlyRealNullForgivingOperators()
    {
        const string source = """
            var a = b!;
            var c = d!.e;
            var f = g![0];
            var h = (i)!;
            var j = k!!;
            var l = m<int>()!;
            var n = o! + 1;
            """;
        Assert.Equal([1, 2, 3, 4, 5, 6, 7], FindNullForgivingLines(source).Distinct());

        const string benign = """"
            if (a != b && !c) { }
            var s = "x!";
            var v = @"x!""y";
            var i = $"hello {a}!";
            var r = """raw!""";
            var ch = '!';
            // comment!
            /* block! */
            var t = x ? !y : !z;
            var f = (Func<bool>)(() => !q);
            var g = !(a);
            """";
        Assert.Empty(FindNullForgivingLines(benign));
    }

    // Postfix '!' sits directly after an identifier, ')', ']' or '>' (generic call); prefix negation and `!=` never do.
    // Strings, raw strings, char literals and comments are blanked first so their contents are ignored.
    internal static IEnumerable<int> FindNullForgivingLines(string source)
    {
        var code = Blank(source);
        var line = 1;
        for (var i = 0; i < code.Length; i++)
        {
            var c = code[i];
            if (c == '\n') { line++; continue; }
            if (c != '!') continue;
            if (i == 0) continue;
            var prev = code[i - 1];
            var next = i + 1 < code.Length ? code[i + 1] : ' ';
            if (next == '=' && (i + 2 >= code.Length || code[i + 2] != '=')) continue; // `!=`
            var postfixAfter = char.IsLetterOrDigit(prev) || prev is '_' or ')' or ']' or '!'
                || (prev == '>' && i >= 2 && code[i - 2] is not ('=' or '-'));
            if (postfixAfter) yield return line;
        }
    }

    private static string Blank(string s)
    {
        var sb = new StringBuilder(s.Length);
        var i = 0;
        void Keep(char ch) => sb.Append(ch == '\n' ? '\n' : ' ');
        while (i < s.Length)
        {
            var c = s[i];
            if (c == '/' && i + 1 < s.Length && s[i + 1] == '/')
            {
                while (i < s.Length && s[i] != '\n') { sb.Append(' '); i++; }
            }
            else if (c == '/' && i + 1 < s.Length && s[i + 1] == '*')
            {
                var end = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                end = end < 0 ? s.Length : end + 2;
                for (; i < end; i++) Keep(s[i]);
            }
            else if (c == '"' || ((c == '$' || c == '@') && IsStringStart(s, i)))
            {
                var start = i;
                while (s[i] != '"') i++;
                var verbatim = s.AsSpan(start, i - start).Contains('@');
                var quotes = 0;
                while (i + quotes < s.Length && s[i + quotes] == '"') quotes++;
                if (quotes >= 3)
                {
                    var close = s.IndexOf(new string('"', quotes), i + quotes, StringComparison.Ordinal);
                    var end = close < 0 ? s.Length : close + quotes;
                    for (var k = start; k < end; k++) Keep(s[k]);
                    i = end;
                }
                else
                {
                    var j = i + 1;
                    while (j < s.Length)
                    {
                        if (verbatim && s[j] == '"' && j + 1 < s.Length && s[j + 1] == '"') { j += 2; continue; }
                        if (!verbatim && s[j] == '\\') { j += 2; continue; }
                        if (s[j] == '"') break;
                        j++;
                    }
                    var end = int.Min(j + 1, s.Length);
                    for (var k = start; k < end; k++) Keep(s[k]);
                    i = end;
                }
            }
            else if (c == '\'' && i + 2 < s.Length && (s[i + 1] != '\\' ? s[i + 2] == '\'' : true))
            {
                var j = s[i + 1] == '\\' ? s.IndexOf('\'', i + 3) : i + 2;
                if (j < 0) j = s.Length - 1;
                for (; i <= j; i++) Keep(s[i]);
            }
            else
            {
                sb.Append(c);
                i++;
            }
        }
        return sb.ToString();
    }

    private static bool IsStringStart(string s, int i)
    {
        while (i < s.Length && s[i] is '$' or '@') i++;
        return i < s.Length && s[i] == '"';
    }

    private static bool HasJustification(string[] lines, int index)
    {
        for (var i = int.Max(0, index - 3); i <= index && i < lines.Length; i++)
            if (lines[i].Contains("JUSTIFY: T", StringComparison.Ordinal)) return true;
        return false;
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Unciv.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Unciv.sln not found");
    }
}
