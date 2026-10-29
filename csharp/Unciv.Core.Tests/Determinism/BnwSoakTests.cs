using System.Diagnostics;
using System.Globalization;
using Unciv.Commands;
using Unciv.Commands.City;
using Unciv.Commands.DevConsole;
using Unciv.Commands.GreatWorks;
using Unciv.Commands.Log;
using Unciv.Commands.Turn;
using Unciv.Commands.Units;
using Unciv.Commands.WorldCongress;
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

// B026: the first whole-game exercise of the Brave New World engine (trade routes, tourism, Great Works, ideologies, World Congress, espionage,
// religion, city states, happiness) on the generated BNW ruleset: a seeded 300-turn game with 8 civs, played through the command processor by a
// scripted human plus the AI civs, with a save/load round trip every 25 turns and a replay of the whole command log. Env-gated like the G&K soak
// (UNCIV_SOAK=1, Category=Soak): wall time is only reported, never asserted beyond the generous runaway budget. A short variant runs in every build.
[Collection("RulesetCache")]
[Trait("Category", "Soak")]
public sealed class BnwSoakTests : IDisposable
{
    private const int TurnLimit = 300;
    private const string Human = "Babylon";
    private static readonly TimeSpan GameTimeout = TimeSpan.FromMinutes(40);

    private static readonly string[] Nations =
        ["Babylon", "Greece", "Rome", "Egypt", "China", "England", "France", "Russia"];

    private readonly ITestOutputHelper output;
    private readonly RulesetClass ruleset;

    public BnwSoakTests(ITestOutputHelper output)
    {
        this.output = output;
        RulesetCache.Clear();
        var jsons = SoakTests.JsonsPath();
        RulesetCache.LoadRulesets(new DirectoryRulesetFileSource(jsons, Path.Combine(jsons, "no mods here")), noMods: true);
        ruleset = RulesetCache.Get(BaseRuleset.Civ_V_BNW.FullName()).NotNull();
    }

    public void Dispose() => RulesetCache.Clear();

    private GameSetupInfo Setup(MapSize size, int majors, int cityStates, int mapSeed, string[]? victories) => new(
        new GameParameters
        {
            BaseRuleset = ruleset.Name,
            UseCiv5Ai = true,
            NumberOfCityStates = cityStates,
            EspionageEnabled = true,
            GodMode = true,   // the scripted BNW actions use the dev console to hand the human a trade unit, a Great Writer and a Great Work building
            NoBarbarians = false,
            VictoryTypes = victories?.ToList() ?? ruleset.Victories.Keys.ToList(),
            MaxTurns = TurnLimit,
            Players = Enumerable.Range(0, majors)
                .Select(i => new Player(Nations[i % Nations.Length], i == 0 ? PlayerType.Human : PlayerType.AI)).ToList()
        },
        new MapParameters { Type = size == MapSize.Small ? MapType.SmallContinents : MapType.ContinentAndIslands, Shape = MapShape.Rectangular, Seed = mapSeed, MapSize = size });

    private sealed record Live(GameSession Session, ScriptedHuman Player, StringWriter Text, CommandLogWriter Writer);

    private static Live Start(GameSetupInfo setup, long gameSeed)
    {
        var header = CommandLogHeader.Create(setup, gameSeed, 1000L);
        var game = GameStarter.StartNewGame(setup, gameSeed, 1000L);
        var session = new GameSession(game) { Clock = ScriptedHuman.DeterministicClock(2000L) };
        var text = new StringWriter(CultureInfo.InvariantCulture);
        var writer = new CommandLogWriter(text, header);
        return new Live(session, new ScriptedHuman(session, Human, writer), text, writer);
    }

    private static string Summary(GameInfo game)
    {
        var majors = game.Civilizations.Where(it => it.IsMajorCiv() && !it.IsDefeated()).ToList();
        var states = game.Civilizations.Count(it => it.IsCityState && !it.IsDefeated());
        var ideologies = majors.Count(it => it.Policies.Ideology != null);
        var routes = game.TradeRoutes?.Count ?? 0;
        var greatWorks = game.Civilizations.Sum(it => it.Cities.Sum(c => c.GetNumberOfGreatWorks()));
        var league = game.League;
        return $"majors={majors.Count} cityStates={states} cities={game.Civilizations.Sum(it => it.Cities.Count)} religions={game.Religions.Count} "
            + $"ideologies={ideologies} tradeRoutes={routes} greatWorks={greatWorks} "
            + $"league={(league == null ? "none" : $"members={league.Members.Count} projects={league.Projects.Count} session={league.SessionIndex}")}";
    }


    private readonly List<string> scripted = [];

    private int Accepted(string prefix) => scripted.Count(it => it.StartsWith(prefix, StringComparison.Ordinal));

    // The scripted actions are skipped silently when the engine refuses them; these assertions make sure the BNW features were really exercised.
    private void AssertScriptedFeaturesHappened(GameInfo game, bool soak)
    {
        Assert.True(Accepted("trade route") > 0, "no trade route was established");
        Assert.True(Accepted("great work by") > 0, "no Great Work was created");
        Assert.NotNull(game.League);
        Assert.True(Accepted("propose") + Accepted("vote") > 0, "no World Congress proposal or vote was accepted");
        if (soak) Assert.Contains(game.Civilizations, it => it.IsMajorCiv() && it.Policies.Ideology != null);
    }

    private bool Try(Live live, string what, GameCommand command)
    {
        var result = live.Session.Submit(Human, command);
        live.Writer.Sync(live.Session);
        scripted.Add(result.Accepted ? what : "refused " + what + ": " + result.Validation.Message);
        return result.Accepted;
    }

    // BNW AI (A-track) does not found routes, create works or propose resolutions yet, so the human's scripted turn does: every attempt is an ordinary
    // logged command and is skipped when the engine refuses it (no unit, no slot, not the host, no session open).
    private void ScriptBnwActions(Live live, bool full)
    {
        var game = live.Session.Game;
        var civ = game.GetCivilization(Human);
        if (!civ.IsAlive()) return;
        // A civilian cannot share a tile with another civilian: spawn in the first city whose centre has no civilian (the automated workers move around).
        if (full) ScriptEconomy(live, civ);
        ScriptLeague(live, civ);
    }

    private void ScriptEconomy(Live live, Civilization civ)
    {
        var game = live.Session.Game;
        var capital = civ.Cities.FirstOrDefault(it => it.GetCenterTile().GetUnits().All(u => !u.IsCivilian())) ?? civ.GetCapital();
        if (capital == null) return;
        foreach (var blocker in capital.GetCenterTile().GetUnits().Where(u => u.IsCivilian() && u.Civ == civ).ToList())
            Try(live, "disband civilian blocking the city centre", new DisbandUnit { UnitId = blocker.Id });
        var tile = capital.GetCenterTile().Position;
        bool Console(string line) => Try(live, "console " + line, new RunConsoleCommand { Line = line, Tile = tile });
        if (game.League == null && !civ.Tech.IsResearched("Printing Press"))
        {
            // Make the scripted human the host of the World Congress (the first major that has the founding tech and knows everybody), so that
            // it can propose resolutions, vote and put production into the league projects below.
            Console($"civ addtechnology {Human} \"Printing Press\"");
            foreach (var other in game.Civilizations.Where(it => it.IsMajorCiv() && it != civ && it.IsAlive()))
                Console($"diplomacy meet {Human} {other.CivName}");
        }
        TradeUnitAndRoute(live, civ, capital, Console);
        if (game.Turns >= 60) GreatWork(live, civ, Console);
    }

    // Every turn (a World Congress session is open for one turn only): propose between sessions, vote while one is open, work on enacted projects.
    private void ScriptLeague(Live live, Civilization civ)
    {
        if (live.Session.Game.League is { } league)
        {
            if (!league.InSession)
            {
                foreach (var name in new[] { "World's Fair", "International Games", "International Space Station", "Open Door", "Sanctions" })
                    if (Try(live, "propose " + name, new ProposeResolution { Resolution = name })) break;
            }
            else
            {
                foreach (var proposal in league.Proposals.ToList())
                    Try(live, "vote " + proposal.Resolution, new CommitVotes { ProposalId = proposal.Id, Choice = "yes", Count = 1 });
            }
            foreach (var city in civ.Cities.Take(2))
                foreach (var project in new[] { "World's Fair Project", "International Games Project", "International Space Station" })
                    if (!city.CityConstructions.ConstructionQueue.Contains(project) && Try(live, "queue " + project, new QueueConstruction { CityId = city.Id, Name = project, AddToTop = true })) break;
        }
    }

    private void TradeUnitAndRoute(Live live, Civilization civ, Unciv.Logic.City.City capital, Func<string, bool> console)
    {
        var existing = civ.Units.GetCivUnits().Where(it => it.Name == "Caravan").Select(it => it.Id).ToHashSet();
        if (!console("unit add Babylon Caravan")) return;
        var unit = civ.Units.GetCivUnits().FirstOrDefault(it => it.Name == "Caravan" && !existing.Contains(it.Id));
        if (unit == null) return;
        var targets = live.Session.Game.GetCities().Where(it => it != capital).OrderBy(it => it.Id, StringComparer.Ordinal).ToList();
        foreach (var kind in new[] { TradeRouteKind.International, TradeRouteKind.Food, TradeRouteKind.Production })
            foreach (var target in targets)
                if (Try(live, "trade route " + kind, new EstablishTradeRoute { UnitId = unit.Id, TargetCityId = target.Id, Kind = kind })) return;
        Try(live, "disband unused caravan", new DisbandUnit { UnitId = unit.Id });
    }

    private void GreatWork(Live live, Civilization civ, Func<string, bool> console)
    {
        foreach (var (person, building) in new[] { ("Great Writer", "The Great Library"), ("Great Musician", "Opera House"), ("Great Artist", "Museum") })
        {
            console($"city addbuilding \"{building}\"");
            var existing = civ.Units.GetCivUnits().Where(it => it.Name == person).Select(it => it.Id).ToHashSet();
            if (!console($"unit add Babylon \"{person}\"")) continue;
            var unit = civ.Units.GetCivUnits().FirstOrDefault(it => it.Name == person && !existing.Contains(it.Id));
            if (unit == null) continue;
            if (!Try(live, "great work by " + person, new CreateGreatWork { UnitId = unit.Id })) Try(live, "disband unused " + person, new DisbandUnit { UnitId = unit.Id });
        }
    }

    // The scripted human plays until it is conquered; after that the host just keeps the turns going (a dead human cannot act, ForceEndTurn passes
    // its turn), so the rest of the game still runs through logged commands.
    private static int PlayChunk(Live live, int turns)
    {
        var game = live.Session.Game;
        if (game.GetCivilization(Human).IsAlive()) return live.Player.PlayTurns(turns);
        var target = game.Turns + turns;
        var steps = 0;
        while (game.Turns < target && game.TurnPhase is not TurnPhase.GameOver && steps++ < 100_000)
        {
            GameCommand command = game.TurnPhase switch
            {
                TurnPhase.AutoPlayers auto => new RunAiTurn { CivId = auto.NextCiv.CivId },
                TurnPhase.AwaitingInput waiting when !game.IsTurnStarted(waiting.Civ) => new BeginTurn { CivId = waiting.Civ.CivId },
                TurnPhase.AwaitingInput waiting => new ForceEndTurn { CivId = waiting.Civ.CivId },
                _ => throw new InvalidOperationException("Unexpected turn phase " + game.TurnPhase),
            };
            var result = live.Session.SubmitAsServer(command);
            Assert.True(result.Accepted, $"{command.Type} refused: {result.Validation.Message}");
            live.Writer.Sync(live.Session);
        }
        return game.Turns - (target - turns);
    }

    private void Run(MapSize size, int majors, int cityStates, long gameSeed, int mapSeed, int turns, int saveEvery, bool replay, string[]? victories = null, bool soak = false)
    {
        Exception? failure = null;
        var worker = new Thread(() =>
        {
            try { RunOnThread(size, majors, cityStates, gameSeed, mapSeed, turns, saveEvery, replay, victories, soak); }
            catch (Exception ex) { failure = ex; }
        }, 256 * 1024 * 1024)
        { IsBackground = true };
        worker.Start();
        if (!worker.Join(GameTimeout)) Assert.Fail($"BNW soak did not finish within {GameTimeout}");
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private void RunOnThread(MapSize size, int majors, int cityStates, long gameSeed, int mapSeed, int turns, int saveEvery, bool replay, string[]? victories, bool soak)
    {
        var live = Start(Setup(size, majors, cityStates, mapSeed, victories), gameSeed);
        var game = live.Session.Game;
        var total = Stopwatch.StartNew();
        var played = 0;
        while (played < turns && game.TurnPhase is not TurnPhase.GameOver)
        {
            var chunk = Stopwatch.StartNew();
            var step = System.Math.Min(saveEvery, turns - played);
            var done = 0;
            for (var i = 0; i < step; i++)
            {
                var one = PlayChunk(live, 1);
                done += one;
                if (one == 0) break;
                if (i < step - 1) ScriptBnwActions(live, full: false);
            }
            played += done;
            ScriptBnwActions(live, full: true);
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"[bnw] turn {game.Turns,3}: {chunk.Elapsed.TotalSeconds / System.Math.Max(1, done):F2}s/turn over {done}, elapsed {total.Elapsed.TotalSeconds,7:F1}s, {Summary(game)}"));
            if (done == 0) break;
            // Save/load round trip: the reloaded game saves to the same text (the unordered religion sets are sorted by Normalise).
            var text = UncivFiles.GameInfoToString(game, forceZip: false);
            var loaded = UncivFiles.GameInfoFromString(text);
            SoakTests.AssertSameText(SoakTests.Normalise(text), SoakTests.Saved(loaded), $"after turn {game.Turns}: the game and a reload of its save");
            Assert.Equal(SoakTests.Digest(game), SoakTests.Digest(loaded));
        }
        output.WriteLine("[bnw] scripted: " + string.Join(", ", scripted.GroupBy(it => it).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => g.Key + " x" + g.Count())));
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"[bnw] DONE winner={game.Civilizations.FirstOrDefault(it => it.IsMajorCiv() && it.VictoryManager.HasWon())?.CivName ?? "none"} turns={game.Turns} total={total.Elapsed.TotalSeconds:F1}s avg={total.Elapsed.TotalSeconds / System.Math.Max(1, game.Turns):F2}s/turn final: {Summary(game)}"));
        // Wall time is only a runaway guard in the opt-in soak runs, never a pass/fail of the default build.
        if (soak) Assert.True(total.Elapsed.TotalSeconds / System.Math.Max(1, game.Turns) < 5.0, "average turn time budget (5 s) exceeded");
        AssertScriptedFeaturesHappened(game, soak);
        foreach (var civ in game.Civilizations.Where(it => it.IsMajorCiv() && !it.IsDefeated()))
            Assert.False(float.IsNaN(civ.Stats.StatsForNextTurn.Gold), $"{civ.CivName} gold is NaN");
        // With only the Time victory the game must really run the whole length (a conquest of the human or a victory would end it early).
        if (victories is { Length: 1 }) Assert.True(game.Turns >= turns, $"the game stopped at turn {game.Turns}");
        if (!replay) return;

        var log = CommandLogReader.Read(live.Text.ToString());
        var result = ReplayVerifier.Verify(log);
        Assert.True(result.Matches, $"replay diverged at seq {result.DivergedAtSeq} after {result.CheckpointsVerified} checkpoints");
        Assert.True(result.CheckpointsVerified > 0 && result.CheckpointsVerified == log.Lines.OfType<CommandLogLine.Checkpoint>().Count(), "replay verified fewer checkpoints than the log holds");
        Assert.Equal(live.Session.LastSeq, result.LastSeq);
        Assert.Equal(ReplayVerifier.FullHash(game), result.FinalHash);
        output.WriteLine($"[bnw] replay of {live.Session.LastSeq} commands matched at {result.CheckpointsVerified} checkpoints");
    }

    // Runs in every build: a short BNW game with the same checks (save/load every 20 turns, full replay).
    [Fact]
    public void Bnw80TurnsOnASmallMapSavesAndReplays() => Run(MapSize.Small, 6, 4, 11L, 5, 80, 20, replay: true);

    [SoakFact]
    public void Bnw300TurnsEightCivsSavesAndReplays() => Run(MapSize.Medium, 8, 8, 23L, 9, TurnLimit, 25, replay: true, victories: ["Time"], soak: true);

    [SoakFact]
    public void Bnw300TurnsOnASmallMapSavesAndReplays() => Run(MapSize.Small, 6, 4, 11L, 5, TurnLimit, 25, replay: true, soak: true);
}
