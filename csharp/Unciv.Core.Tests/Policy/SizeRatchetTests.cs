using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unciv.Core.Tests.Policy;

// T120: size ratchet. Many types mirror giant Kotlin files/functions, so a hard limit would fight faithful ports.
// Instead: a file over MaxFileLines or a method over MaxMethodLines must be listed in SizeRatchetBaseline.txt, and a
// listed file/method may never grow past its recorded size. The test rewrites nothing: humans LOWER the numbers
// (or delete the line once the item is under the limit). Set SIZE_RATCHET_DUMP=<path> to write a fresh baseline
// candidate from the current tree (then lower/curate by hand; never raise a number to make a new violation pass).
// Baseline line format:  `path|size`  (file)   or   `path::Type.Method|size`  (method; overloads: largest wins).
public class SizeRatchetTests
{
    private const int MaxFileLines = 1500;
    private const int MaxMethodLines = 120;
    private static readonly string[] ScannedProjects = ["Unciv.Core", "Unciv.Harness", "Unciv.Godot.Harness"];

    [Fact]
    public void FilesAndMethodsStayWithinLimitsOrTheirShrinkOnlyBaseline()
    {
        var root = FindRoot();
        var baseline = LoadBaseline(root);
        var current = Measure(root);
        MaybeDump(current);

        var problems = new List<string>();
        foreach (var (key, size) in current.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            var limit = key.Contains("::", StringComparison.Ordinal) ? MaxMethodLines : MaxFileLines;
            if (baseline.TryGetValue(key, out var cap))
            {
                if (size > cap) problems.Add($"{key} grew to {size} (baseline {cap}): split it, do not raise the baseline");
            }
            else if (size > limit)
                problems.Add($"{key} is {size} lines (limit {limit}): split it, or (only for a faithful port of a giant Kotlin unit) add it to SizeRatchetBaseline.txt");
        }
        foreach (var key in baseline.Keys.Where(k => !current.ContainsKey(k)).Order(StringComparer.Ordinal))
            problems.Add($"{key} is in SizeRatchetBaseline.txt but no longer exists (renamed/split/deleted): remove the entry");

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void MethodScannerMeasuresDeclarationsWithoutLeadingTrivia()
    {
        const string source = "namespace N;\n// c\nclass A\n{\n    // doc\n    void F()\n    {\n        int x;\n    }\n    void F(int a) { }\n    class B { int G() => 1; }\n}\n";
        var sizes = MeasureMethods("a.cs", source);
        Assert.Equal(4, sizes["a.cs::A.F"]);
        Assert.Equal(1, sizes["a.cs::A.B.G"]);
    }

    private static Dictionary<string, int> Measure(string root)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var project in ScannedProjects)
        {
            var dir = Path.Combine(root, project);
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                var sep = Path.DirectorySeparatorChar;
                if (file.Contains(sep + "obj" + sep, StringComparison.Ordinal) || file.Contains(sep + "bin" + sep, StringComparison.Ordinal)) continue;
                var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
                var text = File.ReadAllText(file);
                result[rel] = CountLines(text);
                foreach (var (key, size) in MeasureMethods(rel, text))
                    result[key] = size;
            }
        }
        return result;
    }

    private static int CountLines(string text)
    {
        var count = 0;
        foreach (var c in text) if (c == '\n') count++;
        return text.Length > 0 && text[^1] != '\n' ? count + 1 : count;
    }

    private static Dictionary<string, int> MeasureMethods(string rel, string text)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        var tree = CSharpSyntaxTree.ParseText(text);
        foreach (var node in tree.GetRoot().DescendantNodes())
        {
            string? name = node switch
            {
                MethodDeclarationSyntax m => m.Identifier.Text,
                ConstructorDeclarationSyntax c => c.Identifier.Text,
                DestructorDeclarationSyntax d => "~" + d.Identifier.Text,
                OperatorDeclarationSyntax o => "operator" + o.OperatorToken.Text,
                ConversionOperatorDeclarationSyntax => "conversion",
                _ => null
            };
            if (name == null) continue;
            var types = node.Ancestors().OfType<BaseTypeDeclarationSyntax>().Select(t => t.Identifier.Text).Reverse();
            var span = node.GetLocation().GetLineSpan();
            var size = span.EndLinePosition.Line - span.StartLinePosition.Line + 1;
            var key = $"{rel}::{string.Join('.', types.Append(name))}";
            result[key] = result.TryGetValue(key, out var old) ? System.Math.Max(old, size) : size;
        }
        return result;
    }

    private static Dictionary<string, int> LoadBaseline(string root)
    {
        var path = Path.Combine(root, "Unciv.Core.Tests", "Policy", "SizeRatchetBaseline.txt");
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var bar = line.LastIndexOf('|');
            Assert.True(bar > 0, $"Bad baseline line: {raw}");
            result[line[..bar]] = int.Parse(line[(bar + 1)..].Split('#')[0].Trim(), CultureInfo.InvariantCulture);
        }
        return result;
    }

    private static void MaybeDump(Dictionary<string, int> current)
    {
        var dump = Environment.GetEnvironmentVariable("SIZE_RATCHET_DUMP");
        if (string.IsNullOrEmpty(dump)) return;
        var lines = current
            .Where(kv => kv.Value > (kv.Key.Contains("::", StringComparison.Ordinal) ? MaxMethodLines : MaxFileLines))
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => $"{kv.Key}|{kv.Value}");
        File.WriteAllLines(dump, lines);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Unciv.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Unciv.sln not found");
    }
}
