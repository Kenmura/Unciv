using System.Diagnostics;
using System.Globalization;
using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Logic.Files;
using Unciv.Logic.Map;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using Unciv.Utils;
using Xunit.Abstractions;
using RulesetClass = Unciv.Models.Ruleset.Ruleset;

namespace Unciv.Core.Tests.Determinism;

/// <summary>
/// T167: a test that only runs when the soak is requested. Run the soak with
/// <c>UNCIV_SOAK=1 dotnet test --filter Category=Soak --logger "console;verbosity=detailed"</c>; without the variable (the default and
/// CI Standard runs) the tests are skipped, so they cost nothing.
/// </summary>
public sealed class SoakFactAttribute : FactAttribute
{
    public SoakFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("UNCIV_SOAK") != "1") Skip = "Soak test: set UNCIV_SOAK=1 and filter Category=Soak.";
    }
}

// T167: L1 acceptance soak. Long AI-only games at realistic scale (Small and Medium maps, 6+ major civs, city states with quests,
// barbarians, religion, espionage, diplomacy and trade), run to a victory or the turn limit. Asserts no exception, no hang and that
// the game state stays sane, and prints the turn-time and memory figures that the ticket records as budgets.
// Note: Unciv has no "Standard" map size; its sizes are Tiny, Small, Medium, Large, Huge, so "Standard" is Medium here.
[Collection("RulesetCache")]
[Trait("Category", "Soak")]
public sealed class SoakTests : IDisposable
{
    private const int TurnLimit = 300;
    private static readonly TimeSpan GameTimeout = TimeSpan.FromMinutes(25);

    private static readonly string[] Nations =
        ["Babylon", "Greece", "Rome", "Egypt", "China", "England", "France", "Russia"];

    private readonly ITestOutputHelper output;
    private readonly RulesetClass ruleset;

    public SoakTests(ITestOutputHelper output)
    {
        this.output = output;
        RulesetCache.Clear();
        var jsons = JsonsPath();
        RulesetCache.LoadRulesets(new DirectoryRulesetFileSource(jsons, Path.Combine(jsons, "no mods here")), noMods: true);
        ruleset = RulesetCache.Get(BaseRuleset.Civ_V_GnK.FullName()).NotNull();
    }

    public void Dispose() => RulesetCache.Clear();

    internal static string JsonsPath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "android", "assets", "jsons");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException("android/assets/jsons not found above " + AppContext.BaseDirectory);
    }

    private GameInfo NewGame(MapSize size, int majors, int cityStates, long gameSeed, int mapSeed)
    {
        var parameters = new GameParameters
        {
            BaseRuleset = ruleset.Name,
            UseCiv5Ai = true,   // T227: the soak plays the A-track AI
            NumberOfCityStates = cityStates,
            EspionageEnabled = true,
            NoBarbarians = false,
            VictoryTypes = ruleset.Victories.Keys.ToList(),
            MaxTurns = TurnLimit,
            Players = Enumerable.Range(0, majors)
                .Select(i => new Player(Nations[i % Nations.Length], i == 0 ? PlayerType.Human : PlayerType.AI)).ToList()
        };
        return GameStarter.StartNewGame(
            new GameSetupInfo(parameters, new MapParameters { Type = size == MapSize.Small ? MapType.SmallContinents : MapType.ContinentAndIslands, Shape = MapShape.Rectangular, Seed = mapSeed, MapSize = size }),
            gameSeed, 1000L);
    }

    private sealed record SoakResult(int Turns, string? Winner, double MaxTurnSeconds, double TotalSeconds, long PeakManagedBytes, List<ulong> SampledHashes, GameInfo Game);

    internal static ulong Hash(GameInfo game) => StateHasher.ComputeHash(h => h.Add(UncivFiles.GameInfoToString(game, forceZip: false)));

    // Plays up to TurnLimit turns, stops at the first victory. Runs on its own thread so a hang fails the test instead of the run.
    private SoakResult Play(GameInfo game, string label, bool sampleHashes, int stopAt = TurnLimit)
    {
        SoakResult? result = null;
        Exception? failure = null;
        var worker = new Thread(() =>
        {
            try { result = PlayOnThread(game, label, sampleHashes, stopAt); }
            catch (Exception ex) { failure = ex; }
        }, 256 * 1024 * 1024)
        { IsBackground = true };
        worker.Start();
        if (!worker.Join(GameTimeout))
        {
            Assert.Fail($"{label}: soak game did not finish within {GameTimeout} (hang or runaway turn time), reached turn {game.Turns}");
        }
        if (failure != null) throw new InvalidOperationException($"{label}: exception on turn {game.Turns}", failure);
        return result.NotNull();
    }

    private SoakResult PlayOnThread(GameInfo game, string label, bool sampleHashes, int stopAt)
    {
        var total = Stopwatch.StartNew();
        double maxTurn = 0;
        long peak = 0;
        var hashes = new List<ulong>();
        string? winner = null;
        var slowest = 0;
        while (game.Turns < stopAt && winner == null)
        {
            game.SimulateUntilWin = true;
            game.SimulateMaxTurns = game.Turns + 1;
            var turnWatch = Stopwatch.StartNew();
            game.NextTurn(2000L);
            var seconds = turnWatch.Elapsed.TotalSeconds;
            if (seconds > maxTurn) { maxTurn = seconds; slowest = game.Turns; }
            winner = game.Civilizations.FirstOrDefault(it => it.IsMajorCiv() && it.VictoryManager.HasWon())?.CivName;
            if (game.Turns % 25 == 0 || winner != null)
            {
                var managed = GC.GetTotalMemory(forceFullCollection: false);
                peak = System.Math.Max(peak, managed);
                if (sampleHashes) hashes.Add(Hash(game));
                output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"[{label}] turn {game.Turns,3}: elapsed {total.Elapsed.TotalSeconds,7:F1}s, last turn {seconds:F2}s, managed {managed / 1048576,5} MB, {Summary(game)}"));
            }
        }
        var final = GC.GetTotalMemory(forceFullCollection: true);
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"[{label}] DONE turns={game.Turns} winner={winner ?? "(none)"} total={total.Elapsed.TotalSeconds:F1}s avgTurn={total.Elapsed.TotalSeconds / System.Math.Max(1, game.Turns):F2}s maxTurn={maxTurn:F2}s (turn {slowest}) peakManaged={peak / 1048576}MB liveAfterGC={final / 1048576}MB"));
        return new SoakResult(game.Turns, winner, maxTurn, total.Elapsed.TotalSeconds, System.Math.Max(peak, final), hashes, game);
    }

    internal static string Excerpt(string text, int at)
    {
        var start = System.Math.Max(0, at - 200);
        return text.Substring(start, System.Math.Min(400, text.Length - start));
    }

    private static string Summary(GameInfo game)
    {
        var majors = game.Civilizations.Where(it => it.IsMajorCiv() && !it.IsDefeated()).ToList();
        var states = game.Civilizations.Where(it => it.IsCityState && !it.IsDefeated()).ToList();
        var spies = game.Civilizations.Sum(it => it.EspionageManager.SpyList.Count);
        var quests = states.Count(it => it.QuestManager.ToString().Contains("[]", StringComparison.Ordinal) == false);
        var barbarianUnits = game.GetBarbarianCivilization().Units.GetCivUnits().Count();
        var trades = game.Civilizations.Sum(it => it.Diplomacy.Values.Sum(d => d.Trades.Count));
        return $"majors={majors.Count} cityStates={states.Count} cities={game.Civilizations.Sum(it => it.Cities.Count)} religions={game.Religions.Count} "
            + $"spies={spies} statesWithQuests={quests} barbarianUnits={barbarianUnits} camps={game.Barbarians.Encampments.Count} activeTrades={trades}";
    }

    private void AssertSane(GameInfo game, SoakResult result, int expectedMajors, int expectedCityStates)
    {
        Assert.Equal(expectedMajors, game.Civilizations.Count(it => it.IsMajorCiv()));
        Assert.Equal(expectedCityStates, game.Civilizations.Count(it => it.IsCityState));
        Assert.True(result.Turns == TurnLimit || result.Winner != null, $"stopped at turn {result.Turns} with no winner");
        Assert.Contains(game.Civilizations, it => it.IsMajorCiv() && it.Cities.Count > 0);
        foreach (var civ in game.Civilizations.Where(it => it.IsMajorCiv() && !it.IsDefeated()))
        {
            Assert.False(float.IsNaN(civ.Stats.StatsForNextTurn.Gold), $"{civ.CivName} gold is NaN");
        }
        output.WriteLine("Final: " + Summary(game));
        // Budgets, deliberately generous (measured on the dev container: avg 0.12-0.21 s/turn, max 1.0 s, 83 MB managed): they catch a runaway, not a 2x change.
        var peakRss = System.Diagnostics.Process.GetCurrentProcess().PeakWorkingSet64;
        output.WriteLine($"Budget: turns={result.Turns} total={result.TotalSeconds:F1}s avg={result.TotalSeconds / result.Turns:F2}s/turn max={result.MaxTurnSeconds:F2}s peakManaged={result.PeakManagedBytes / 1048576}MB processPeakRss={peakRss / 1048576}MB");
        Assert.True(result.TotalSeconds / result.Turns < 5.0, "average turn time budget (5 s) exceeded");
        Assert.True(result.MaxTurnSeconds < 60.0, "slowest turn budget (60 s) exceeded");
        Assert.True(result.PeakManagedBytes < 1024L * 1048576, "managed memory budget (1 GB) exceeded");
        Assert.True(peakRss < 3072L * 1048576, "process peak working set budget (3 GB) exceeded");
    }

    // A single test per size: Small 6 civs + 6 city states, and Medium 8 civs + 10 city states, both to 300 turns or a victory.
    [SoakFact]
    public void SmallMapSixCivsSixCityStates300Turns()
    {
        var first = Play(NewGame(MapSize.Small, 6, 6, 11L, 5), "small-A", sampleHashes: true);
        AssertSane(first.Game, first, 6, 6);
        // Determinism at scale: same seeds give the same sampled state hashes, turn by turn.
        var second = Play(NewGame(MapSize.Small, 6, 6, 11L, 5), "small-B", sampleHashes: true);
        Assert.Equal(first.Turns, second.Turns);
        Assert.Equal(first.Winner, second.Winner);
        Assert.Equal(first.SampledHashes, second.SampledHashes);
    }

    // A compact, order-independent digest of what a player sees: if a reloaded game and the original ever disagree on this, they have diverged.
    internal static string Digest(GameInfo game) => string.Join(";", game.Civilizations.Select(it => string.Create(CultureInfo.InvariantCulture,
        $"{it.CivName}:{it.Gold}:{it.Cities.Count}:{it.Cities.Sum(c => c.Population.Population)}:{it.Units.GetCivUnits().Count()}:{it.Tech.TechsResearched.Count}:{it.Policies.FreePolicies}:{it.IsDefeated()}")));

    // Religion followers/cities are JVM-style hash sets whose order depends on how the set was built: sorted before comparing text (builtBuildings are compared in order).
    internal static string Normalise(string saved) => System.Text.RegularExpressions.Regex.Replace(
        System.Text.RegularExpressions.Regex.Replace(saved, "\"pointsForNextGreatGeneral\":\\d+", "\"pointsForNextGreatGeneral\":0"),
        "\"(followers|cities)\":\\[([^\\]\\[]*)\\]",
        m => $"\"{m.Groups[1].Value}\":[{string.Join(",", m.Groups[2].Value.Split(',').OrderBy(x => x, StringComparer.Ordinal))}]");

    // T208: the running game must continue exactly like a reload of its own save (lockstep replay, join-in-progress): the original and two loads of its
    // save agree on the full saved text after every civ's turn (GameInfo.RefreshTransientCaches makes the lazily maintained caches canonical).
    [SoakFact]
    public void SaveLoadOfALongRunningGameReplaysIdentically() => SaveLoadReplaysIdentically(11L, 5);

    [SoakFact]
    public void SaveLoadOfAnotherLongRunningGameReplaysIdentically() => SaveLoadReplaysIdentically(29L, 8);

    internal static string Saved(GameInfo game) => Normalise(UncivFiles.GameInfoToString(game, forceZip: false));

    private void SaveLoadReplaysIdentically(long gameSeed, int mapSeed)
    {
        var original = NewGame(MapSize.Small, 6, 6, gameSeed, mapSeed);
        Play(original, "saveload", sampleHashes: false, stopAt: 150);
        var text = UncivFiles.GameInfoToString(original, forceZip: false);
        var loaded = UncivFiles.GameInfoFromString(text);
        var loaded2 = UncivFiles.GameInfoFromString(text);
        // T219: a Clone plays on exactly like the original too (GameInfoCloneIsolationTests walks both object graphs to prove they share no mutable object).
        var clone = original.Clone();
        clone.SetTransients();
        Assert.Equal(Digest(original), Digest(clone));
        // Loading runs BackwardCompatibility.MigrateGreatGeneralPools like upstream, which rewrites the legacy, otherwise unread field
        // pointsForNextGreatGeneral; Normalise masks it.
        AssertSameText(Normalise(text), Normalise(UncivFiles.GameInfoToString(loaded, forceZip: false)), "right after loading");
        Assert.Equal(Digest(original), Digest(loaded));
        for (var i = 0; i < 50; i++)
        {
            // Civ by civ, so a divergence is found at the step that causes it, not a whole turn later.
            var all = new[] { original, loaded, loaded2, clone };
            foreach (var g in all) { g.SimulateUntilWin = true; g.SimulateMaxTurns = g.Turns + 1; g.EndPlayerTurn(2000L); }
            for (var running = true; running;)
            {
                var phases = all.Select(g => g.RunAutoPlayers(1)).ToList();
                running = phases[0] is TurnPhase.AutoPlayers;
                Assert.All(phases, ph => Assert.Equal(running, ph is TurnPhase.AutoPlayers));
                AssertSameText(Saved(original), Saved(loaded), $"after one more civ, {i + 1} turns after loading: the running game and a reload of its save");
                AssertSameText(Saved(original), Saved(clone), $"after one more civ, {i + 1} turns after loading: the running game and its clone");
            }
            foreach (var g in all) g.BeginPlayerTurn(2000L);
            var expected = Saved(original);
            AssertSameText(expected, Saved(loaded), $"the running game and a reload of its save, {i + 1} turns after loading");
            AssertSameText(expected, Saved(loaded2), $"the running game and a second reload, {i + 1} turns after loading");
            AssertSameText(expected, Saved(clone), $"the running game and its clone, {i + 1} turns after loading");
            Assert.True(Digest(original) == Digest(loaded), $"digest differs {i + 1} turns after loading");
        }
        Assert.Equal(Hash(loaded), Hash(loaded2));
    }

    internal static void AssertSameText(string expected, string actual, string when)
    {
        if (expected == actual) return;
        var at = 0;
        while (at < expected.Length && at < actual.Length && expected[at] == actual[at]) at++;
        Assert.Fail($"saved text differs {when}, first at char {at}: A ...{Excerpt(expected, at)}... B ...{Excerpt(actual, at)}...");
    }

    [SoakFact]
    public void MediumMapEightCivsTenCityStates300Turns()
    {
        var result = Play(NewGame(MapSize.Medium, 8, 10, 23L, 9), "medium", sampleHashes: false);
        AssertSane(result.Game, result, 8, 10);
    }
}
