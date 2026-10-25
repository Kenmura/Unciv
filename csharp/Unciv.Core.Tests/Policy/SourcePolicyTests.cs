using System.Text.RegularExpressions;

namespace Unciv.Core.Tests.Policy;

// Source-level policy for the C# tree (T013). Analyzers cannot see comments or build configuration, so these tests
// make sure that silencing a rule is always a visible, justified, ticket-linked decision.
public partial class SourcePolicyTests
{
    private static readonly string CSharpRoot = FindCSharpRoot();

    // DETERMINISM.md: string ordering must be ordinal. Heuristic (types are not visible to a regex): an OrderBy/ThenBy whose key is a
    // name/text-like member and that passes no comparer. Keys of other types are unaffected; add the comparer (or rename) when it fires.
    [GeneratedRegex(@"\b(OrderBy|ThenBy|OrderByDescending|ThenByDescending)\(\s*\w+\s*=>\s*[\w.!?]*\.(Name|CivName|FullName|Text|Title|Label)\s*(\(\))?\s*\)")]
    private static partial Regex StringKeyOrderingRegex();

    [Fact]
    public void StringKeyedOrderingUsesAnExplicitComparer()
    {
        Assert.Matches(StringKeyOrderingRegex(), "x.OrderBy(it => it.Name)");
        Assert.Matches(StringKeyOrderingRegex(), "x.ThenByDescending(c => c.Civ.CivName)");
        Assert.DoesNotMatch(StringKeyOrderingRegex(), "x.OrderBy(it => it.Name, StringComparer.Ordinal)");
        Assert.DoesNotMatch(StringKeyOrderingRegex(), "x.OrderBy(it => it.Cost)");

        var offenders = new List<string>();
        foreach (var file in SourceFiles("*.cs"))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
                if (StringKeyOrderingRegex().IsMatch(lines[i]))
                    offenders.Add($"{Path.GetRelativePath(CSharpRoot, file)}:{i + 1}");
        }

        Assert.True(offenders.Count == 0,
            "OrderBy/ThenBy on a string key needs StringComparer.Ordinal (DETERMINISM.md): " + string.Join(", ", offenders));
    }

    private static string FindCSharpRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Unciv.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Unciv.sln not found above " + AppContext.BaseDirectory);
    }

    // Generated code and this policy folder itself (which necessarily mentions the forbidden patterns) are skipped.
    private static IEnumerable<string> SourceFiles(string pattern) =>
        Directory.EnumerateFiles(CSharpRoot, pattern, SearchOption.AllDirectories)
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "Policy" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);

    [GeneratedRegex(@"JUSTIFY: T\d{3}\b")]
    private static partial Regex JustifyRegex();

    [GeneratedRegex(@"#pragma\s+warning\s+disable|SuppressMessage\s*\(")]
    private static partial Regex SuppressionRegex();

    [GeneratedRegex(@"\b(TODO|HACK|FIXME)\b(?!\(T\d{3}\))")]
    private static partial Regex UntrackedMarkerRegex();

    private static bool HasJustification(string[] lines, int index)
    {
        for (var i = int.Max(0, index - 3); i <= index; i++)
            if (JustifyRegex().IsMatch(lines[i])) return true;
        return false;
    }

    [Fact]
    public void EverySuppressionCarriesATicketLinkedJustification()
    {
        var offenders = new List<string>();
        foreach (var file in SourceFiles("*.cs"))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
                if (SuppressionRegex().IsMatch(lines[i]) && !HasJustification(lines, i))
                    offenders.Add($"{Path.GetRelativePath(CSharpRoot, file)}:{i + 1}");
        }

        Assert.True(offenders.Count == 0,
            "Suppressions need a `// JUSTIFY: T### reason` comment within the 3 lines above: " + string.Join(", ", offenders));
    }

    [Fact]
    public void NoWarnInProjectFilesCarriesAJustification()
    {
        var offenders = new List<string>();
        var files = SourceFiles("*.csproj").Concat(SourceFiles("*.props"));
        foreach (var file in files)
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
                if (lines[i].Contains("<NoWarn>", StringComparison.Ordinal) && !HasJustification(lines, i))
                    offenders.Add($"{Path.GetRelativePath(CSharpRoot, file)}:{i + 1}");
        }

        Assert.True(offenders.Count == 0,
            "<NoWarn> needs an XML comment `JUSTIFY: T### reason` within the 3 lines above: " + string.Join(", ", offenders));
    }

    // Disabling a rule for the whole tree is a policy change; per-file overrides in their own section are not.
    [Fact]
    public void EditorConfigDoesNotDisableRulesGlobally()
    {
        var offenders = new List<string>();
        var editorConfig = Path.Combine(CSharpRoot, ".editorconfig");
        var section = "";
        var lines = File.ReadAllLines(editorConfig);
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.StartsWith('[')) section = line;
            else if (section == "[*.cs]" && Regex.IsMatch(line, @"^dotnet_diagnostic\.\S+\.severity\s*=\s*none\b"))
                offenders.Add($".editorconfig:{i + 1}");
        }

        Assert.True(offenders.Count == 0,
            "Rules may not be set to `none` in the global [*.cs] section; scope the override to a file or change the policy via a ticket: "
            + string.Join(", ", offenders));
    }

    [Fact]
    public void WorkMarkersReferenceATicket()
    {
        var offenders = new List<string>();
        foreach (var file in SourceFiles("*.cs"))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
                if (UntrackedMarkerRegex().IsMatch(lines[i]))
                    offenders.Add($"{Path.GetRelativePath(CSharpRoot, file)}:{i + 1}");
        }

        Assert.True(offenders.Count == 0,
            "TODO/HACK/FIXME must be written as e.g. TODO(T014) so the work is tracked in the backlog: " + string.Join(", ", offenders));
    }
}
