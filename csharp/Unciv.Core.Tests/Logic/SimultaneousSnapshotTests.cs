using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Logic.Files;
using Unciv.Logic.Map;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using Unciv.Utils;
using RulesetClass = Unciv.Models.Ruleset.Ruleset;

namespace Unciv.Core.Tests.Logic;

// T218: a snapshot taken before ANY resolve step of a simultaneous round (city states on, several seeds, many rounds) reproduces the live hash,
// and stays equal after the step is performed on both.
[Collection("RulesetCache")]
public sealed class SimultaneousSnapshotTests : IDisposable
{
    private readonly RulesetClass ruleset;

    public SimultaneousSnapshotTests()
    {
        RulesetCache.Clear();
        var jsons = Path.Combine(RepoRoot(), "android", "assets", "jsons");
        RulesetCache.LoadRulesets(new DirectoryRulesetFileSource(jsons, Path.Combine(jsons, "no mods here")), noMods: true);
        ruleset = RulesetCache.GetVanillaRuleset();
    }

    public void Dispose() => RulesetCache.Clear();

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "android", "assets", "jsons"))) return dir.FullName;
        throw new DirectoryNotFoundException("android/assets/jsons not found above " + AppContext.BaseDirectory);
    }

    private static string Save(GameInfo game) => UncivFiles.GameInfoToString(game, forceZip: false);

    private static ulong Hash(GameInfo game) => StateHasher.ComputeHash(h => h.Add(Save(game)));

    private static void Perform(GameInfo game, ResolveStep step)
    {
        switch (step)
        {
            case ResolveStep.ProcessEndTurn s: game.ProcessEndTurn(game.GetCivilization(s.CivId), 2000L); break;
            case ResolveStep.RunAiTurn s: game.RunAiTurn(game.GetCivilization(s.CivId)); break;
            case ResolveStep.StartNextTurn: game.StartNextTurn(); break;
            case ResolveStep.BeginTurn s: game.BeginTurn(game.GetCivilization(s.CivId), 2000L); break;
            default: throw new InvalidOperationException(step.ToString());
        }
    }

    [Theory]
    [InlineData(1L)]
    [InlineData(5L)]
    [InlineData(9L)]
    [InlineData(13L)]
    public void ASnapshotBeforeEveryResolveStepReproducesTheLiveHash(long seed)
    {
        var game = GameStarter.StartNewGame(
            new GameSetupInfo(
                new GameParameters
                {
                    BaseRuleset = ruleset.Name,
                    NumberOfCityStates = 2,
                    VictoryTypes = ruleset.Victories.Keys.ToList(),
                    Players =
                    [
                        new Player("Babylon", PlayerType.Human), new Player("Greece", PlayerType.Human),
                        new Player("Rome", PlayerType.AI), new Player("Egypt", PlayerType.AI),
                    ],
                },
                new MapParameters { Type = MapType.Pangaea, Shape = MapShape.Rectangular, Seed = seed, MapSize = MapSize.Tiny }),
            seed, 1000L);
        game.StartSimultaneousTurns();
        for (var round = 1; round <= 40; round++)
        {
            foreach (var civ in game.Civilizations.Where(game.IsActive).ToList()) game.EndHumanTurn(civ);
            game.BeginResolving();
            while (game.NextResolveStep() is { } step)
            {
                var loaded = UncivFiles.GameInfoFromString(Save(game));
                Assert.True(Hash(game) == Hash(loaded), $"seed {seed} round {round} before {step}: " + FirstDifference(Save(game), Save(loaded)));
                Perform(game, step);
                Perform(loaded, step);
                Assert.True(Hash(game) == Hash(loaded), $"seed {seed} round {round} after {step}: " + FirstDifference(Save(game), Save(loaded)));
            }
        }
    }

    private static string FirstDifference(string a, string b)
    {
        var i = 0;
        while (i < a.Length && i < b.Length && a[i] == b[i]) i++;
        var from = System.Math.Max(0, i - 200);
        return $"@{i}\nlive:   ...{a[from..System.Math.Min(a.Length, i + 120)]}\nloaded: ...{b[from..System.Math.Min(b.Length, i + 120)]}";
    }
}
