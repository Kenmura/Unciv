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
    public void HelpAndBadOptionsAreHandled()
    {
        var output = new StringWriter();
        Assert.Equal(0, CliRunner.Run(["bench", "--help"], output, new StringWriter()));
        Assert.Contains("--scenario", output.ToString());
        Assert.Equal(2, CliRunner.Run(["bench", "--scenario", "huge"], new StringWriter(), new StringWriter()));
    }
}
