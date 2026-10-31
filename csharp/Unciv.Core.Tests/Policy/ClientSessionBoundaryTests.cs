using System.Text.RegularExpressions;

namespace Unciv.Core.Tests.Policy;

// C037 (design/CLIENT-SLICES.md principle 2): the Godot client reads the world only through IGameSession. Only the session implementation
// (LocalHost) may name the engine's game objects.
public partial class ClientSessionBoundaryTests
{
    private static readonly string[] Allowed = ["Play/LocalHost.cs"];

    [GeneratedRegex(@"\b(GameInfo|GameSession|SnapshotBuilder|ViewDeltaBuilder|GameView|CommandProcessor)\b")]
    private static partial Regex ForbiddenRegex();

    [Fact]
    public void ClientCodeOutsideTheSessionImplementationDoesNotTouchTheEngineGame()
    {
        var root = FindRoot();
        var dir = Path.Combine(root, "Unciv.Godot");
        Assert.True(Directory.Exists(dir), "Unciv.Godot not found");
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(dir, file).Replace('\\', '/');
            if (relative.StartsWith("obj/", StringComparison.Ordinal) || relative.StartsWith("bin/", StringComparison.Ordinal) || relative.StartsWith(".godot/", StringComparison.Ordinal)) continue;
            if (Allowed.Contains(relative)) continue;
            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                var code = lines[i].Split("//")[0];
                if (ForbiddenRegex().IsMatch(code) && !lines[i].TrimStart().StartsWith("///", StringComparison.Ordinal))
                    offenders.Add($"{relative}:{i + 1}");
            }
        }
        Assert.True(offenders.Count == 0, "Client code must go through IGameSession, not the engine game: " + string.Join(", ", offenders));
    }

    [Fact]
    public void ScannerSeesTheForbiddenNames()
    {
        Assert.Matches(ForbiddenRegex(), "var g = new GameInfo();");
        Assert.DoesNotMatch(ForbiddenRegex(), "IGameSession session;");
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Unciv.Core"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("csharp root");
    }
}
