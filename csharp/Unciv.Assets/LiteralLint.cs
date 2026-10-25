using System.Text.RegularExpressions;

namespace Unciv.Assets;

public sealed record LintViolation(string File, int Line, string Rule, string Text);

/// <summary>
/// "Assets by name only": client code outside <c>Unciv.Godot/Assets/</c> must not name resource paths (<c>res://</c>) or write colour literals.
/// Comment-only lines are ignored; everything else in a <c>.cs</c> file is scanned.
/// </summary>
public static partial class LiteralLint
{
    private static readonly (string Rule, Regex Pattern)[] Rules =
    [
        ("res:// literal", ResPath()),
        ("colour literal", ColourLiteral()),
    ];

    private static readonly string[] SkippedDirectories = ["bin", "obj", ".godot", ".mono", "Assets"];

    public static IReadOnlyList<LintViolation> Scan(string file, string text)
    {
        var result = new List<LintViolation>();
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith('*') || trimmed.StartsWith("/*", StringComparison.Ordinal)) continue;
            foreach (var (rule, pattern) in Rules)
            {
                if (pattern.IsMatch(lines[i])) result.Add(new LintViolation(file, i + 1, rule, trimmed.TrimEnd()));
            }
        }
        return result;
    }

    /// <summary>Scans every .cs file under <paramref name="clientRoot"/> except those in a top-level <c>Assets</c> directory (and build output).</summary>
    public static IReadOnlyList<LintViolation> ScanDirectory(string clientRoot)
    {
        var result = new List<LintViolation>();
        foreach (var path in Directory.EnumerateFiles(clientRoot, "*.cs", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(clientRoot, path).Replace('\\', '/');
            if (relative.Split('/')[..^1].Any(segment => SkippedDirectories.Contains(segment, StringComparer.Ordinal))) continue;
            result.AddRange(Scan(relative, File.ReadAllText(path)));
        }
        return result;
    }

    [GeneratedRegex("res://")]
    private static partial Regex ResPath();

    [GeneratedRegex("""new\s+Color\s*\(|\bColors\.|\bColor\.(Color8|FromHtml|FromString|FromOkHsl|FromHsv)\b|"#[0-9a-fA-F]{3,8}\b""")]
    private static partial Regex ColourLiteral();
}
