using System.Text.Json.Nodes;
using Unciv.Cli;
using Unciv.Models.Ruleset;

namespace Unciv.Core.Tests.Cli;

// T261: the turn-time bench. Its numbers are for a human (Release build, minutes); the build only proves that the instrumentation changes nothing:
// the per-turn state hashes of a probed run (with the host-side ViewDelta measurement on for the last turns) equal those of a run without the probe.
[Collection("RulesetCache")]
public sealed class BenchTests : IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("unciv-bench-").FullName;

    public void Dispose()
    {
        RulesetCache.Clear();
        Directory.Delete(dir, recursive: true);
    }

    private JsonNode Bench(params string[] args)
    {
        var path = Path.Combine(dir, $"bench{Directory.GetFiles(dir).Length}.json");
        var output = new StringWriter();
        var error = new StringWriter();
        var code = CliRunner.Run(["bench", "--scenario", "small", "--json", path, .. args], output, error);
        Assert.True(code == 0, error.ToString());
        return JsonNode.Parse(File.ReadAllText(path))!;
    }

    private static List<string> Hashes(JsonNode report) =>
        report["scenarios"]![0]!["turns"]!.AsArray().Select(it => (string)it!["hash"]!).ToList();

    [Fact]
    public void StateHashesAreIdenticalWithAndWithoutTheProbe()
    {
        var probed = Bench("--turns", "10", "--host-turns", "3", "--recipients", "2");
        var plain = Bench("--turns", "10", "--no-probe");
        Assert.Equal(10, Hashes(plain).Count);
        Assert.Equal(Hashes(plain), Hashes(probed));
        Assert.Equal(10, Hashes(plain).Distinct().Count());
    }

    [Fact]
    public void TheReportNamesThePhasesAndTheHostCost()
    {
        var scenario = Bench("--turns", "6", "--host-turns", "2", "--recipients", "2")["scenarios"]![0]!;
        var phases = scenario["all"]!["phases"]!.AsArray().Select(it => (string)it!["phase"]!).ToList();
        Assert.Contains("Command.Apply", phases);
        Assert.Contains("TurnManager.AutomateTurn", phases);
        Assert.Contains("RefreshTransientCaches", phases);
        Assert.NotEmpty(scenario["all"]!["civs"]!.AsArray());
        Assert.True((int)scenario["host"]!["commands"]! > 0);
        Assert.NotNull(scenario["host"]!["phases"]!["Host.BuildDelta"]);
    }

    [Fact]
    public void GoldenFileMatchesMismatchesAndSkipsUnlistedScenarios()
    {
        var hashes = Hashes(Bench("--turns", "4", "--no-probe"));
        var golden = Path.Combine(dir, "golden.json");
        string Run(out int code)
        {
            var output = new StringWriter();
            code = CliRunner.Run(["bench", "--scenario", "small", "--turns", "4", "--no-probe", "--golden", golden], output, new StringWriter());
            return output.ToString();
        }

        File.WriteAllText(golden, new JsonObject { ["small"] = new JsonArray(hashes.Select(it => (JsonNode?)it).ToArray()) }.ToJsonString());
        Assert.Contains("4 turn hashes identical", Run(out var code));
        Assert.Equal(0, code);

        var bad = hashes.ToList();
        bad[2] = "deadbeef";
        File.WriteAllText(golden, new JsonObject { ["small"] = new JsonArray(bad.Select(it => (JsonNode?)it).ToArray()) }.ToJsonString());
        Assert.Contains("1 turn hashes differ, first at turn 3", Run(out code));
        Assert.Equal(1, code);

        File.WriteAllText(golden, new JsonObject { ["small"] = new JsonArray(hashes.Take(2).Select(it => (JsonNode?)it).ToArray()) }.ToJsonString());
        Assert.Contains("differ, first at turn 3", Run(out code));
        Assert.Equal(1, code);

        File.WriteAllText(golden, "{}");
        Assert.Contains("nothing was compared", Run(out code));
        Assert.Equal(1, code);

        File.WriteAllText(golden, new JsonObject { ["small"] = new JsonArray(1, 2, 3, 4) }.ToJsonString());
        Assert.Contains("4 turn hashes differ", Run(out code)); // non-string elements are a mismatch, not a crash
        Assert.Equal(1, code);

        File.WriteAllText(golden, "[]");
        Assert.Equal(2, CliRunner.Run(["bench", "--scenario", "small", "--turns", "2", "--no-probe", "--golden", golden], new StringWriter(), new StringWriter()));
    }

    // The golden list is frozen at the pre-T262 engine: after a deliberate engine change regenerate it (bench --json, see design/PERFORMANCE.md).
    [Fact]
    public void TheCommittedGoldenListStillMatchesTheEngine()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "migration", "design", "perf-golden.json"))) root = root.Parent;
        Assert.NotNull(root);
        var output = new StringWriter();
        var code = CliRunner.Run(["bench", "--scenario", "small", "--turns", "25", "--no-probe", "--golden", Path.Combine(root.FullName, "migration", "design", "perf-golden.json")],
            output, new StringWriter());
        Assert.True(code == 0, output.ToString());
        Assert.Contains("25 turn hashes identical", output.ToString());
    }

    [Fact]
    public void HelpAndBadOptionsAreHandled()
    {
        var output = new StringWriter();
        Assert.Equal(0, CliRunner.Run(["bench", "--help"], output, new StringWriter()));
        Assert.Contains("--scenario", output.ToString());
        Assert.Equal(2, CliRunner.Run(["bench", "--scenario", "huge"], new StringWriter(), new StringWriter()));
    }
}
