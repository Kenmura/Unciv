using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unciv.Core.Tests.Policy;

// T121: coupling ratchet. Counts the distinct `Unciv.*` namespaces a file depends on (`using` directives plus fully
// qualified references in code, minus the file's own namespace). A file over MaxNamespaces must be listed in
// CouplingRatchetBaseline.txt and may never depend on more namespaces than recorded; humans LOWER the numbers (or delete
// the line once the file is at or under the limit). Never raise a number to make a new violation pass.
// Set COUPLING_RATCHET_DUMP=<path> to write a fresh baseline candidate from the current tree.
// Baseline line format:  `path|count`.
// Also enforces layering: Unciv.Core must not reference Godot or the harness assemblies, and `Unciv.UI.*` namespaces
// may only be used when they are an engine-owned leaf package listed in AllowedUiNamespaces (the Kotlin UI types that
// the engine genuinely needs: stat chars, ranking types, ...). A new UI namespace needs a policy ticket.
public class CouplingRatchetTests
{
    private const int MaxNamespaces = 12;
    private const string Core = "Unciv.Core";

    // Engine-owned Unciv.UI.* packages that exist in Unciv.Core today (no libGDX dependency).
    private static readonly HashSet<string> AllowedUiNamespaces = new(StringComparer.Ordinal)
    {
        "Unciv.UI.Components",
        "Unciv.UI.Components.Extensions",
        "Unciv.UI.ObjectDescriptions",
        "Unciv.UI.Screens.Civilopedia",
        "Unciv.UI.Screens.MapEditorScreen",
        "Unciv.UI.Screens.OverviewScreen",
        "Unciv.UI.Screens.VictoryScreen",
    };

    [Fact]
    public void FilesStayWithinNamespaceLimitOrTheirShrinkOnlyBaseline()
    {
        var root = FindRoot();
        var baseline = LoadBaseline(root);
        var current = Measure(root).ToDictionary(kv => kv.Key, kv => kv.Value.Count, StringComparer.Ordinal);
        MaybeDump(current);

        var problems = new List<string>();
        foreach (var (key, count) in current.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            if (baseline.TryGetValue(key, out var cap))
            {
                if (count > cap) problems.Add($"{key} now depends on {count} Unciv namespaces (baseline {cap}): decouple it, do not raise the baseline");
            }
            else if (count > MaxNamespaces)
                problems.Add($"{key} depends on {count} Unciv namespaces (limit {MaxNamespaces}): decouple it, or (only for a faithful port of a giant Kotlin unit) add it to CouplingRatchetBaseline.txt");
        }
        foreach (var key in baseline.Keys.Where(k => !current.ContainsKey(k)).Order(StringComparer.Ordinal))
            problems.Add($"{key} is in CouplingRatchetBaseline.txt but no longer exists: remove the entry");

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void CoreDoesNotReferenceGodotHarnessOrUnlistedUiNamespaces()
    {
        var root = FindRoot();
        var problems = new List<string>();
        foreach (var (file, namespaces) in Measure(root))
        {
            foreach (var ns in namespaces)
            {
                if (ns == "Godot" || ns.StartsWith("Godot.", StringComparison.Ordinal)
                    || ns == "Unciv.Harness" || ns.StartsWith("Unciv.Harness.", StringComparison.Ordinal)
                    || ns == "Unciv.Godot" || ns.StartsWith("Unciv.Godot.", StringComparison.Ordinal))
                    problems.Add($"{file} references {ns}: Unciv.Core must not depend on Godot or harness code");
                else if ((ns == "Unciv.UI" || ns.StartsWith("Unciv.UI.", StringComparison.Ordinal)) && !AllowedUiNamespaces.Contains(ns))
                    problems.Add($"{file} references {ns}: not an engine-owned UI leaf package (AllowedUiNamespaces)");
            }
        }
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void ScannerCountsUsingsAndQualifiedReferencesButNotOwnNamespace()
    {
        const string source = """
            using Unciv.Logic;
            using static Unciv.Utils.Foo;
            using Alias = Unciv.Models.Ruleset.Thing;
            using System.Text;
            namespace Unciv.Logic.Map;
            // Unciv.Comment.Nope x;
            class A
            {
                Unciv.Logic.Civilization.Civ c;
                Unciv.Logic.Map.Self s;
                string t = "Unciv.String.Nope x";
                void F() { var x = Unciv.Logic.City.City.Create(); }
            }
            """;
        Assert.Equal(
            ["Unciv.Logic", "Unciv.Logic.City", "Unciv.Logic.Civilization", "Unciv.Models.Ruleset", "Unciv.Utils"],
            Scan(source).Order(StringComparer.Ordinal));
    }

    // Namespace of a type reference = the dotted chain minus its last segment (the type); for a `using static` the
    // chain minus the type as well. Heuristic (syntax only, no binding), which is enough for a ratchet.
    internal static HashSet<string> Scan(string text)
    {
        var tree = CSharpSyntaxTree.ParseText(text);
        var rootNode = tree.GetRoot();
        var own = rootNode.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>().Select(n => n.Name.ToString()).ToHashSet(StringComparer.Ordinal);
        var result = new HashSet<string>(StringComparer.Ordinal);

        foreach (var u in rootNode.DescendantNodes().OfType<UsingDirectiveSyntax>())
        {
            if (u.Name == null) continue;
            var name = u.Name.ToString();
            if (u.StaticKeyword.IsKind(SyntaxKind.StaticKeyword) || u.Alias != null) name = Parent(name);
            Add(result, own, name);
        }

        foreach (var node in rootNode.DescendantNodes().Where(n => n is QualifiedNameSyntax or MemberAccessExpressionSyntax))
        {
            if (node.Ancestors().Any(a => a is UsingDirectiveSyntax || (a is BaseNamespaceDeclarationSyntax ns && ns.Name.Span.Contains(node.Span)))) continue;
            if (node.Parent is QualifiedNameSyntax or MemberAccessExpressionSyntax) continue; // only maximal chains
            var chain = node.ToString();
            if (!chain.StartsWith("Unciv.", StringComparison.Ordinal) && chain != "Unciv") { AddForeign(result, chain); continue; }
            var segments = chain.Split('.').Select(s => s.Trim()).TakeWhile(s => s.Length > 0 && (char.IsLetter(s[0]) || s[0] == '_')).ToList();
            // Namespace = leading segments up to (not including) the first member that starts a type chain: we take all
            // but the last identifier, and for member accesses also drop the called member.
            var drop = node is MemberAccessExpressionSyntax ? 2 : 1;
            if (segments.Count > drop) Add(result, own, string.Join('.', segments.Take(segments.Count - drop)));
        }
        return result;
    }

    private static void AddForeign(HashSet<string> result, string chain)
    {
        if (chain.StartsWith("Godot.", StringComparison.Ordinal)) result.Add("Godot");
    }

    private static string Parent(string name)
    {
        var dot = name.LastIndexOf('.');
        return dot < 0 ? name : name[..dot];
    }

    private static void Add(HashSet<string> result, HashSet<string> own, string ns)
    {
        if (ns != "Unciv" && !ns.StartsWith("Unciv.", StringComparison.Ordinal) && ns != "Godot" && !ns.StartsWith("Godot.", StringComparison.Ordinal)) return;
        if (own.Contains(ns)) return;
        result.Add(ns);
    }

    private static Dictionary<string, HashSet<string>> Measure(string root)
    {
        var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var dir = Path.Combine(root, Core);
        var sep = Path.DirectorySeparatorChar;
        foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            if (file.Contains(sep + "obj" + sep, StringComparison.Ordinal) || file.Contains(sep + "bin" + sep, StringComparison.Ordinal)) continue;
            result[Path.GetRelativePath(root, file).Replace('\\', '/')] = Scan(File.ReadAllText(file));
        }
        return result;
    }

    private static Dictionary<string, int> LoadBaseline(string root)
    {
        var path = Path.Combine(root, "Unciv.Core.Tests", "Policy", "CouplingRatchetBaseline.txt");
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
        var dump = Environment.GetEnvironmentVariable("COUPLING_RATCHET_DUMP");
        if (string.IsNullOrEmpty(dump)) return;
        File.WriteAllLines(dump, current.Where(kv => kv.Value > MaxNamespaces).OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}|{kv.Value}"));
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Unciv.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Unciv.sln not found");
    }
}
