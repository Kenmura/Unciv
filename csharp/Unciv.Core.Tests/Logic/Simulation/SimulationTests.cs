using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using Unciv.Logic.Simulation;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using RulesetClass = Unciv.Models.Ruleset.Ruleset;

namespace Unciv.Core.Tests.Logic.Simulation;

// T101: Simulation/SimulationStep/MutableInt have no Kotlin tests; the expectations are derived from the Kotlin source.
[Collection("RulesetCache")]
public sealed class SimulationTests : IDisposable
{
    private readonly RulesetClass ruleset;

    public SimulationTests()
    {
        RulesetCache.Clear();
        var jsons = JsonsPath();
        RulesetCache.LoadRulesets(new DirectoryRulesetFileSource(jsons, Path.Combine(jsons, "no mods here")), noMods: true);
        ruleset = RulesetCache.GetVanillaRuleset();
    }

    public void Dispose() => RulesetCache.Clear();

    private static string JsonsPath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "android", "assets", "jsons");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException("android/assets/jsons not found above " + AppContext.BaseDirectory);
    }

    private GameInfo NewGame() =>
        GameStarter.StartNewGame(
            new GameSetupInfo(
                new GameParameters
                {
                    BaseRuleset = ruleset.Name,
                    NumberOfCityStates = 0,
                    Players = [new Player("Babylon", PlayerType.Human), new Player("Greece"), new Player("Rome")],
                },
                new MapParameters { Type = MapType.Pangaea, Shape = MapShape.Rectangular, Seed = 3, MapSize = MapSize.Tiny }),
            5, 1000L);

    [Fact]
    public void MutableIntCountsAndWraps()
    {
        var value = new MutableInt();
        Assert.Equal(0, value.Get());
        value.Inc();
        value.Add(4);
        Assert.Equal(5, value.Value);
        value.Set(int.MaxValue);
        value.Inc();
        Assert.Equal(int.MinValue, value.Get());
    }

    [Fact]
    public void StepStartsWithMinusOneForEveryMajorCivAndSampledTurn()
    {
        var game = NewGame();
        var step = new SimulationStep(game, [10, 20]);
        Assert.Equal(["Babylon", "Greece", "Rome"], step.TurnStatsPop.Keys.OrderBy(it => it, StringComparer.Ordinal));
        Assert.Equal([10, 20, -1], step.TurnStatsPop["Greece"].Keys);
        Assert.All(step.TurnStatsCities["Rome"].Values, it => Assert.Equal(-1, it.Value));
        Assert.Null(step.Winner);
        Assert.Equal(game.Turns, step.Turns);
        Assert.Equal(game.CurrentPlayer, step.CurrentPlayer);
    }

    [Fact]
    public void StepSavesTheCurrentTurnWithoutVictory()
    {
        var game = NewGame();
        game.Turns = 10;
        var step = new SimulationStep(game, [10]);
        step.SaveTurnStats(game);
        var civ = game.Civilizations.First(it => it.CivId == "Greece");
        Assert.Equal(civ.Cities.Count, step.TurnStatsCities["Greece"][10].Value);
        Assert.Equal(civ.Cities.Sum(it => it.Population.Population), step.TurnStatsPop["Greece"][10].Value);
        Assert.Equal(-1, step.TurnStatsPop["Greece"][-1].Value);
        game.Turns = 12;
        step.Update(game);
        Assert.Equal(12, step.Turns);
    }

    [Fact]
    public void AddTalliesWinsAndSummaries()
    {
        var game = NewGame();
        var sim = new Unciv.Logic.Simulation.Simulation(game, statTurns: [10]);
        var step = new SimulationStep(game, [10]);
        step.TurnStatsPop["Greece"][10].Set(8);
        step.TurnStatsCities["Greece"][10].Set(2);
        step.TurnStatsPop["Greece"][-1].Set(9);
        step.TurnStatsCities["Greece"][-1].Set(0); // no cities: avg pop 0
        foreach (var civ in new[] { "Babylon", "Rome" })
        {
            step.TurnStatsCities[civ][-1].Set(1);
            step.TurnStatsPop[civ][-1].Set(3);
        }
        step.Winner = "Greece";
        var victory = ruleset.Victories.Keys.First();
        step.VictoryType = victory;
        sim.Add(step);
        Assert.Equal(1, sim.NumWins["Greece"].Value);
        Assert.Equal(0, sim.NumWins["Rome"].Value);
        var text = sim.Text();
        Assert.Contains("\nGreece:\n100.0% total win rate \n", text);
        Assert.Contains($"{victory}: 100%    ", text);
        Assert.Contains("@10: popSum avg=8.0 cnt=1\n", text);
        Assert.Contains("@END: popSum avg=9.0 cnt=1\n", text);
        Assert.DoesNotContain("Rome:", text);
        Assert.DoesNotContain("Average speed", text);
    }

    [Fact]
    public void ExperimentGroupAddsGroupSections()
    {
        var game = NewGame();
        var sim = new Unciv.Logic.Simulation.Simulation(game, civIdsInExperimentGroup: new HashSet<string> { "Greece" });
        var text = sim.Text();
        Assert.Contains("Experiment group (Greece):\n0.0% total win rate (expected 33.3% if no effect)\n", text);
        Assert.Contains("Control group (Babylon, Rome):\n0.0% total win rate (expected 66.7% if no effect)\n", text);
    }

    [Fact]
    public void LargeSamplesReportAPValue()
    {
        var game = NewGame();
        var sim = new Unciv.Logic.Simulation.Simulation(game, civIdsInExperimentGroup: new HashSet<string> { "Greece" });
        for (var i = 0; i < 40; i++)
        {
            var step = new SimulationStep(game);
            foreach (var civ in new[] { "Babylon", "Greece", "Rome" })
            {
                step.TurnStatsCities[civ][-1].Set(1);
                step.TurnStatsPop[civ][-1].Set(1);
            }
            if (i < 30)
            {
                step.Winner = "Greece";
                step.VictoryType = ruleset.Victories.Keys.First();
            }
            sim.Add(step);
        }
        var text = sim.Text();
        Assert.Contains("75.0% total win rate (expected 33.3% if no effect)\none-tail binomial pval = ", text);
        Assert.DoesNotContain("pval = NaN", text);
    }

    [Fact]
    public void PlaysGamesDeterministicallyFromTheSeed()
    {
        string Run(long seed)
        {
            var lines = new List<string>();
            var game = NewGame();
            new Unciv.Logic.Simulation.Simulation(game, simulationsPerThread: 2, threadsNumber: 1, maxTurns: 3,
                statTurns: [1], output: lines.Add, seed: seed).Start();
            return string.Join('\n', lines);
        }

        var first = Run(9);
        Assert.Equal(first, Run(9));
        Assert.Contains("Starting new game with major civs: ", first);
        Assert.Contains("Simulation step (2/2)", first);
        Assert.Contains("Draw", first);
    }
}
