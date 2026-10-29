using System.Text.RegularExpressions;

namespace Unciv.Core.Tests.Policy;

// Marker ownership policy (T118). A TODO(T###) marker, a throwing NotSupported/NotImplemented seam and a *Seams*.cs file are
// promises that a backlog ticket will do the work. This test reads the ticket headers and States of migration/backlog/*.md
// (as text) and fails when a promise points at a ticket that does not exist or is already Done / Won't port,
// so a marker can no longer outlive its owner. Tickets that are Available, Blocked, In-Progress, Needs-Review or In-Review are open.
public partial class MarkerOwnershipTests
{
    private static readonly string CSharpRoot = FindCSharpRoot();

    private static string FindCSharpRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Unciv.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Unciv.sln not found above " + AppContext.BaseDirectory);
    }

    [GeneratedRegex(@"^### (T\d{3})\b")]
    private static partial Regex TicketHeaderRegex();

    [GeneratedRegex(@"^- State:\s*(.*)$")]
    private static partial Regex StateRegex();

    [GeneratedRegex(@"\bTODO\((T\d{3})\)")]
    private static partial Regex TodoRegex();

    [GeneratedRegex(@"\bT\d{3}\b")]
    private static partial Regex TicketIdRegex();

    [GeneratedRegex(@"JUSTIFY: T\d{3}\b")]
    private static partial Regex JustifyRegex();

    [GeneratedRegex(@"throw new (NotSupportedException|NotImplementedException)\b")]
    private static partial Regex ThrowRegex();

    // ticket id -> true if the ticket is open (not Done, not Won't port)
    private static Dictionary<string, bool> LoadTicketStates()
    {
        var states = new Dictionary<string, bool>();
        var dir = Path.Combine(CSharpRoot, "migration", "backlog");
        if (!Directory.Exists(dir)) return states;
        foreach (var path in Directory.EnumerateFiles(dir, "*.md").OrderBy(p => p, StringComparer.Ordinal))
        {
            string? current = null;
            foreach (var line in File.ReadLines(path))
            {
                var header = TicketHeaderRegex().Match(line);
                if (header.Success) { current = header.Groups[1].Value; continue; }
                if (current == null) continue;
                var state = StateRegex().Match(line);
                if (!state.Success) continue;
                var text = state.Groups[1].Value.Trim();
                states[current] = !(text.StartsWith("Done", StringComparison.Ordinal) || text.StartsWith("Won't port", StringComparison.Ordinal));
                current = null;
            }
        }
        return states;
    }

    private static IEnumerable<string> CoreFiles(params string[] projects) =>
        projects.SelectMany(project => Directory.EnumerateFiles(Path.Combine(CSharpRoot, project), "*.cs", SearchOption.AllDirectories))
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            // the policy tests mention the marker syntax in their messages
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "Policy" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);

    private static string Rel(string file) => Path.GetRelativePath(CSharpRoot, file);

    private static string? Problem(Dictionary<string, bool> states, string id) =>
        !states.TryGetValue(id, out var isOpen) ? $"{id} does not exist" : isOpen ? null : $"{id} is Done";

    [Fact]
    public void BacklogIsParsed()
    {
        var states = LoadTicketStates();
        Assert.True(states.Count > 50, "migration/backlog/*.md were not parsed");
        Assert.True(states.ContainsKey("T118"));
    }

    [Fact]
    public void EveryTodoMarkerNamesAnOpenTicket()
    {
        var states = LoadTicketStates();
        var offenders = new List<string>();
        foreach (var file in CoreFiles("Unciv.Core", "Unciv.Core.Tests", "Unciv.App.Tests"))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
                foreach (Match match in TodoRegex().Matches(lines[i]))
                    if (Problem(states, match.Groups[1].Value) is { } problem)
                        offenders.Add($"{Rel(file)}:{i + 1} ({problem})");
        }
        Assert.True(offenders.Count == 0,
            "TODO(T###) must name an existing ticket that is not Done: retarget the marker to its real owner or add a ticket: " + string.Join(", ", offenders));
    }

    [Fact]
    public void EveryThrowingSeamHasAnOwnerOrAJustification()
    {
        var states = LoadTicketStates();
        var offenders = new List<string>();
        foreach (var file in CoreFiles("Unciv.Core"))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (!ThrowRegex().IsMatch(lines[i])) continue;
                var owned = false;
                for (var j = int.Max(0, i - 3); j <= i && !owned; j++)
                {
                    if (JustifyRegex().IsMatch(lines[j])) owned = true;
                    else
                        foreach (Match match in TodoRegex().Matches(lines[j]))
                            if (Problem(states, match.Groups[1].Value) == null) owned = true;
                }
                if (!owned) offenders.Add($"{Rel(file)}:{i + 1}");
            }
        }
        Assert.True(offenders.Count == 0,
            "throw new NotSupportedException/NotImplementedException needs a TODO(T###) of an open ticket or a `// JUSTIFY: T### reason` on the same or the 3 previous lines: "
            + string.Join(", ", offenders));
    }

    // The header comment of a seam file is every comment line before its first declaration.
    private static IEnumerable<string> HeaderComment(string[] lines)
    {
        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("//", StringComparison.Ordinal)) yield return trimmed;
            else if (trimmed.Length > 0 && !trimmed.StartsWith("using ", StringComparison.Ordinal) && !trimmed.StartsWith("namespace ", StringComparison.Ordinal))
                yield break;
        }
    }

    [Fact]
    public void EverySeamFileHasAnOpenOwner()
    {
        var states = LoadTicketStates();
        var offenders = new List<string>();
        foreach (var file in CoreFiles("Unciv.Core").Where(path => Path.GetFileName(path).Contains("Seams", StringComparison.Ordinal)))
        {
            var owners = HeaderComment(File.ReadAllLines(file)).SelectMany(line => TicketIdRegex().Matches(line).Select(match => match.Value))
                .Distinct().ToList();
            if (owners.Count == 0) offenders.Add($"{Rel(file)} (no owner ticket named in the header comment)");
            else if (!owners.Any(id => Problem(states, id) == null)) offenders.Add($"{Rel(file)} (owners {string.Join(", ", owners)} are all Done or unknown)");
        }
        Assert.True(offenders.Count == 0,
            "A *Seams*.cs file must name at least one open owner ticket in its header comment; when all owners are Done the seam must be removed: " + string.Join("; ", offenders));
    }
}
