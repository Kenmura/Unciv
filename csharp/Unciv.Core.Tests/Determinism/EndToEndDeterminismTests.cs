using System.Globalization;
using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Logic.Files;
using Unciv.Logic.Map;
using Unciv.Logic.Simulation;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using Unciv.Utils;
using RulesetClass = Unciv.Models.Ruleset.Ruleset;

namespace Unciv.Core.Tests.Determinism;

// T102: the project's determinism gate. A whole AI-only game from one seed must produce the same state hash every turn, run after run,
// and a game saved and loaded mid-way must continue exactly like the original. (Per-turn hashes from the Kotlin game: KotlinGoldenTests, all 30 turns for seed 11, seed 3 up to Kotlin's own unseeded divergence at turn 23.)
[Collection("RulesetCache")]
public sealed class EndToEndDeterminismTests : IDisposable
{
    private const int Turns = 60;
    private readonly RulesetClass ruleset;

    public EndToEndDeterminismTests()
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

    private GameInfo NewGame(long gameSeed, int mapSeed, MapSize? size = null, params string[] nations) => NewGame(false, gameSeed, mapSeed, size, nations);

    private GameInfo NewGame(bool civ5Ai, long gameSeed, int mapSeed, MapSize? size = null, params string[] nations)
    {
        if (nations.Length == 0) nations = ["Babylon", "Greece", "Rome"];
        return GameStarter.StartNewGame(
            new GameSetupInfo(
                new GameParameters { UseCiv5Ai = civ5Ai, BaseRuleset = ruleset.Name, NumberOfCityStates = 0, VictoryTypes = ruleset.Victories.Keys.ToList(), Players = nations.Select((it, i) => new Player(it, i == 0 ? PlayerType.Human : PlayerType.AI)).ToList() },
                new MapParameters { Type = MapType.Pangaea, Shape = MapShape.Rectangular, Seed = mapSeed, MapSize = size ?? MapSize.Tiny }),
            gameSeed, 1000L);
    }

    private static ulong Hash(GameInfo game) => StateHasher.ComputeHash(h => h.Add(UncivFiles.GameInfoToString(game, forceZip: false)));

    private static void PlayOneTurn(GameInfo game)
    {
        game.SimulateUntilWin = true;
        game.SimulateMaxTurns = game.Turns + 1;
        game.NextTurn(2000L);
    }

    private static List<ulong> Play(GameInfo game, int turns)
    {
        var hashes = new List<ulong>();
        for (var i = 0; i < turns; i++)
        {
            PlayOneTurn(game);
            hashes.Add(Hash(game));
        }
        return hashes;
    }

    [Theory]
    [InlineData(1L, 3)]
    [InlineData(77L, 11)]
    public void SameSeedGivesTheSameHashEveryTurn(long gameSeed, int mapSeed)
    {
        var start = Hash(NewGame(gameSeed, mapSeed));
        Assert.Equal(start, Hash(NewGame(gameSeed, mapSeed)));
        var game = NewGame(gameSeed, mapSeed);
        var first = Play(game, Turns);
        var second = Play(NewGame(gameSeed, mapSeed), Turns);
        Assert.Equal(first, second);
        Assert.Equal(Turns, first.Distinct().Count()); // the game really advances
        Assert.Contains(game.Civilizations, it => it.Cities.Count > 0); // ... and the hashed state has cities in it
    }

    // T184: NextTurn is EndPlayerTurn + RunAutoPlayers + BeginPlayerTurn; walking the phases by hand (one civ per call) must give the same hashes.
    [Theory]
    [InlineData(1L, 3)]
    [InlineData(77L, 11)]
    public void PhasedTurnsGiveTheSameHashesAsNextTurn(long gameSeed, int mapSeed)
    {
        var viaNextTurn = Play(NewGame(gameSeed, mapSeed), 12);
        var game = NewGame(gameSeed, mapSeed);
        var phased = new List<ulong>();
        for (var i = 0; i < 12; i++)
        {
            game.SimulateUntilWin = true;
            game.SimulateMaxTurns = game.Turns + 1;
            game.EndPlayerTurn(2000L);
            while (game.RunAutoPlayers() is TurnPhase.AutoPlayers) { }
            game.BeginPlayerTurn(2000L);
            phased.Add(Hash(game));
        }
        Assert.Equal(viaNextTurn, phased);
    }

    // T200: a Simultaneous game with exactly one human at civ index 0 resolves a round in NextTurn's order, so (TurnState aside) its state is identical every turn.
    [Theory]
    [InlineData(1L, 3)]
    [InlineData(77L, 11)]
    public void OneHumanSimultaneousGameGivesTheSameHashesAsNextTurn(long gameSeed, int mapSeed)
    {
        var sequential = NewGame(gameSeed, mapSeed);
        var simultaneous = NewGame(gameSeed, mapSeed);
        // Barbarians are civ index 0 and Kotlin processes them after the last civ's wrap; "the human at civ index 0" needs them last in both games.
        foreach (var game in new[] { sequential, simultaneous })
        {
            var barbarians = game.Civilizations.Single(it => it.IsBarbarian);
            game.Civilizations.Remove(barbarians);
            game.Civilizations.Add(barbarians);
        }
        simultaneous.StartSimultaneousTurns();
        for (var i = 0; i < Turns; i++)
        {
            sequential.NextTurn(2000L);
            foreach (var civ in simultaneous.Civilizations.Where(simultaneous.IsActive).ToList()) simultaneous.EndHumanTurn(civ);
            simultaneous.BeginResolving();
            while (simultaneous.NextResolveStep() is { } step)
            {
                switch (step)
                {
                    case ResolveStep.ProcessEndTurn s: simultaneous.ProcessEndTurn(simultaneous.GetCivilization(s.CivId), 2000L); break;
                    case ResolveStep.RunAiTurn s: simultaneous.RunAiTurn(simultaneous.GetCivilization(s.CivId)); break;
                    case ResolveStep.StartNextTurn: simultaneous.StartNextTurn(); break;
                    case ResolveStep.BeginTurn s: simultaneous.BeginTurn(simultaneous.GetCivilization(s.CivId), 2000L); break;
                    default: throw new InvalidOperationException(step.ToString());
                }
            }
            var state = simultaneous.TurnState;
            simultaneous.TurnState = null; // the only difference between the two games
            var simultaneousHash = Hash(simultaneous);
            simultaneous.TurnState = state;
            Assert.Equal(Hash(sequential), simultaneousHash);
        }
        Assert.Equal(sequential.Turns, simultaneous.Turns);
    }

    [Fact]
    public void DifferentSeedsDiverge()
    {
        Assert.NotEqual(Hash(NewGame(1L, 3)), Hash(NewGame(2L, 3)));
        Assert.NotEqual(Play(NewGame(1L, 3), 3), Play(NewGame(1L, 4), 3));
    }

    [Fact]
    public void LargerMapAndMoreCivsStayDeterministic()
    {
        string[] nations = ["Babylon", "Greece", "Rome", "Egypt", "China"];
        var first = Play(NewGame(5L, 8, MapSize.Small, nations), 6);
        Assert.Equal(first, Play(NewGame(5L, 8, MapSize.Small, nations), 6));
    }

    [Fact]
    public void SaveLoadContinueMatchesTheOriginal()
    {
        var original = NewGame(9L, 5);
        Play(original, 5);
        var saved = UncivFiles.GameInfoToString(original, forceZip: false);
        var loaded = UncivFiles.GameInfoFromString(saved);
        Assert.Equal(Hash(original), Hash(loaded));
        Assert.Equal(Play(original, 6), Play(loaded, 6));
    }

    // T227: with the Civ 5 AI wired into the unit pass the game is just as deterministic, and the option (saved in the game parameters) survives a reload.
    [Theory]
    [InlineData(1L, 3)]
    [InlineData(77L, 11)]
    public void Civ5AiGameGivesTheSameHashEveryTurn(long gameSeed, int mapSeed)
    {
        var first = Play(NewGame(true, gameSeed, mapSeed), 40);
        Assert.Equal(first, Play(NewGame(true, gameSeed, mapSeed), 40));
        Assert.Equal(40, first.Distinct().Count());
        Assert.NotEqual(Play(NewGame(false, gameSeed, mapSeed), 40), first);   // the option really changes the AI
    }

    [Fact]
    public void Civ5AiGameSaveLoadContinueMatchesTheOriginal()
    {
        var original = NewGame(true, 9L, 5);
        Play(original, 20);
        var loaded = UncivFiles.GameInfoFromString(UncivFiles.GameInfoToString(original, forceZip: false));
        Assert.True(loaded.GameParameters.UseCiv5Ai);
        Assert.Equal(Hash(original), Hash(loaded));
        Assert.Equal(Play(original, 20), Play(loaded, 20));
    }

    [Fact]
    public void CloneContinuesLikeTheOriginal()
    {
        var original = NewGame(4L, 6);
        Play(original, 3);
        var clone = original.Clone();
        clone.SetTransients();
        Assert.Equal(Hash(original), Hash(clone));
        Assert.Equal(Play(original, 4), Play(clone, 4));
    }

    // The test host runs in globalization-invariant mode (no ICU, no named cultures): derive a hostile culture by hand.
    private static CultureInfo OddCulture()
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.NumberFormat.NegativeSign = "~";
        culture.NumberFormat.NumberDecimalSeparator = ",";
        culture.NumberFormat.NumberGroupSeparator = ".";
        culture.NumberFormat.PositiveSign = "#";
        return culture;
    }

    [Fact]
    public void CurrentCultureDoesNotChangeTheGame()
    {
        var invariant = Play(NewGame(3L, 2), 60);
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = OddCulture();
            Assert.Equal(invariant, Play(NewGame(3L, 2), 60));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void SimulationReportIsReproducibleAndCultureIndependent()
    {
        string Run()
        {
            var lines = new List<string>();
            new Simulation(NewGame(1L, 1), simulationsPerThread: 2, maxTurns: 6, statTurns: [2], output: lines.Add, seed: 21).Start();
            return string.Join('\n', lines);
        }

        var invariant = Run();
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = OddCulture();
            Assert.Equal(invariant, Run());
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void SimulationReportsAVictory()
    {
        // A single civ has nobody left to conquer: the first game ends at once with that civ as the winner (Start's victory branch).
        var lines = new List<string>();
        var sim = new Simulation(NewGame(1L, 1, MapSize.Tiny, "Babylon"), maxTurns: 6, output: lines.Add, seed: 4);
        sim.Start();
        Assert.Contains(lines, it => it.StartsWith("Babylon won ", StringComparison.Ordinal) && it.Contains(" victory on turn ", StringComparison.Ordinal));
        Assert.Equal(1, sim.NumWins["Babylon"].Value);
        Assert.Contains("\nBabylon:\n100.0% total win rate \n", sim.Text());
    }

    [Fact]
    public void SimulationPositionalArgumentsKeepTheKotlinMeaning()
    {
        // Kotlin Simulation(game, simulationsPerThread, threadsNumber): the seed must not take a positional slot.
        var lines = new List<string>();
        new Simulation(NewGame(1L, 1), 1, 2, 1, output: lines.Add).Start();
        Assert.Contains("Simulation step (2/2)", lines);
    }

    [Fact]
    public void HexCoordTextIsCultureIndependent()
    {
        // Regression: ToPrettyString is the JSON key of lastSeenImprovement; a culture with another minus sign changed the saved game.
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = OddCulture();
            Assert.Equal("(-2,6)", HexCoord.Of(-2, 6).ToPrettyString());
            Assert.Equal("HexCoord(x=-2, y=6)", HexCoord.Of(-2, 6).ToString());
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }
}
