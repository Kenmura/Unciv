using System.Text.RegularExpressions;

namespace Unciv.Assets;

public sealed record LintViolation(string File, int Line, string Rule, string Text);

/// <summary>
/// "Assets by name only": client code outside <c>Unciv.Godot/Assets/</c> must not name resource paths (<c>res://</c>) or write colour literals.
/// C025 adds "all labels through Tr()": a literal with a letter in it assigned to <c>Text</c>, <c>PlaceholderText</c> or <c>TooltipText</c> must end in <c>.Tr()</c>.
/// Comment-only lines and block comments are ignored; everything else in a <c>.cs</c> file is scanned.
/// </summary>
public static partial class LiteralLint
{
    private static readonly (string Rule, Regex Pattern)[] Rules =
    [
        ("res:// literal", ResPath()),
        ("colour literal", ColourLiteral()),
        ("untranslated text literal", UntranslatedText()),
    ];

    private static readonly string[] BuildOutput = ["bin", "obj", ".godot", ".mono"];

    public static IReadOnlyList<LintViolation> Scan(string file, string text)
    {
        var result = new List<LintViolation>();
        var lines = text.Split('\n');
        var inBlockComment = false;
        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (inBlockComment)
            {
                inBlockComment = !trimmed.Contains("*/", StringComparison.Ordinal);
                continue;
            }
            if (trimmed.StartsWith("//", StringComparison.Ordinal)) continue;
            if (trimmed.StartsWith("/*", StringComparison.Ordinal))
            {
                inBlockComment = !trimmed.Contains("*/", StringComparison.Ordinal);
                continue;
            }
            foreach (var (rule, pattern) in Rules)
            {
                if (pattern.IsMatch(lines[i])) result.Add(new LintViolation(file, i + 1, rule, trimmed.TrimEnd()));
            }
        }
        return result;
    }

    /// <summary>
    /// Scans every .cs file under <paramref name="clientRoot"/> except those under its top-level <c>Assets</c> directory and build output (bin, obj, .godot, .mono at any depth).
    /// Scope is <c>Unciv.Godot</c> only: the C000/C008 harness project legitimately builds its own test scenes.
    /// </summary>
    public static IReadOnlyList<LintViolation> ScanDirectory(string clientRoot)
    {
        var result = new List<LintViolation>();
        foreach (var path in Directory.EnumerateFiles(clientRoot, "*.cs", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(clientRoot, path).Replace('\\', '/');
            var directories = relative.Split('/')[..^1];
            if (directories.Length > 0 && directories[0] == "Assets") continue;
            if (directories.Any(segment => BuildOutput.Contains(segment, StringComparer.Ordinal))) continue;
            result.AddRange(Scan(relative, File.ReadAllText(path)));
        }
        return result;
    }

    [GeneratedRegex(""""\b(res|uid|user)://|"://"""")]
    private static partial Regex ResPath();

    [GeneratedRegex("""\b(Text|PlaceholderText|TooltipText)\s*=\s*"(?=[^"]*\p{L})[^"]*"(?!\s*\.Tr\()""")]
    private static partial Regex UntranslatedText();

    [GeneratedRegex("""new\s+(Godot\.)?Color\s*\(|\bColor\b[^;=]*=\s*new\s*\(|\bColors\.|\bColor\.(Color8|FromHtml|FromString|FromOkHsl|FromHsv)\b|"#[0-9a-fA-F]{3,8}\b""")]
    private static partial Regex ColourLiteral();
}
