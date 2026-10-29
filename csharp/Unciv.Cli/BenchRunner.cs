using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Unciv.Commands;
using Unciv.Commands.Log;
using Unciv.Commands.Turn;
using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using Unciv.Models.Metadata;
using Unciv.Net;
using Unciv.Net.Visibility;
using Unciv.Utils;

namespace Unciv.Cli;

/// <summary>
/// T261: the turn-time profiling harness. <c>bench</c> generates fixed-seed games (no saves needed), plays them through the command layer exactly like the
/// BNW soak (scripted human, AI civs through <see cref="RunAiTurn"/>, a state-hash checkpoint after every human turn start) and reports where the wall time of
/// each turn goes. The engine only announces named phases through <see cref="TurnProbe"/>; every clock reading is made here, so the engine stays clock-free
/// and the state hashes are identical with and without the instrumentation (<c>--no-probe</c> proves it per run, BenchTests per build).
/// </summary>
public static class BenchRunner
{
    public const string Usage = """
        bench [--scenario small|medium|all] [--turns N] [--json FILE|-] [--ruleset NAME] [--host-turns K] [--recipients R] [--no-probe]
              Profile end-turn time. small: 4 civs + 2 city states, Small map, 50 turns. medium: 8 civs + 8 city states, Medium map, 200 turns.
              Per turn: wall time of the whole turn (the human's turn, every AI civ, the checkpoint hash), split by phase; at the end the top 20 phases by
              inclusive time, the time per civ and per command type, and (medium, the last K turns, default 3) the host-side ViewDelta cost per recipient
              (R recipients, default 3) measured around every command. --json writes the report ('-' = standard output). --turns replaces the turn count.
              --no-probe runs without the phase probe (wall time and hashes only; the per-turn state hashes must be equal to a probed run).
              Use a Release build for numbers: dotnet run -c Release --project Unciv.Cli -- bench --scenario small --turns 10
        """;

    private static readonly string[] Nations = ["Babylon", "Greece", "Rome", "Egypt", "China", "England", "France", "Russia"];

    /// <summary>A named, fully seeded game.</summary>
    public sealed record Scenario(string Name, int Majors, int CityStates, string MapSizeName, string MapTypeName, long GameSeed, long MapSeed, int Turns, int HostTurns);

    public static readonly Scenario Small = new("small", 4, 2, "Small", MapType.SmallContinents, 11L, 5L, 50, 0);

    public static readonly Scenario Medium = new("medium", 8, 8, "Medium", MapType.ContinentAndIslands, 23L, 9L, 200, 3);

    /// <summary>What a scenario run is told besides the scenario.</summary>
    public sealed record Options(bool Probe = true, int? Turns = null, int? HostTurns = null, int Recipients = 3, string? Ruleset = null);

    // The phases that make up a turn, in the order the report lists them per turn.
    private static readonly string[] TurnColumns =
    [
        "Command.Apply", "TurnManager.StartTurn", "TurnManager.AutomateTurn", "TurnManager.EndTurn", "RefreshTransientCaches",
        "RecordRankingStats", "ProcessLeagueTurn", "Hash.FullHash",
    ];

    // The phases that are attributed to a civ (the subject of the scope).
    private static readonly string[] CivPhases = ["TurnManager.StartTurn", "TurnManager.AutomateTurn", "TurnManager.EndTurn"];

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static int Run(CliArgs cli, TextWriter output, Action<CliArgs> loadRulesets)
    {
        var scenarioName = cli.Get("scenario") ?? "all";
        var turns = cli.Get("turns") == null ? (int?)null : cli.GetInt("turns", 0, 1, 10_000);
        var hostTurns = cli.Get("host-turns") == null ? (int?)null : cli.GetInt("host-turns", 0, 0, 1000);
        var recipients = cli.GetInt("recipients", 3, 1, 8);
        var jsonPath = cli.Get("json");
        var noProbe = cli.Flag("no-probe");
        var ruleset = cli.Get("ruleset");
        loadRulesets(cli);
        cli.RejectUnknown();
        var scenarios = scenarioName switch
        {
            "small" => [Small],
            "medium" => [Medium],
            "all" => new[] { Small, Medium },
            _ => throw new UsageException($"--scenario '{scenarioName}' is not one of: small, medium, all"),
        };
        var options = new Options(!noProbe, turns, hostTurns, recipients, ruleset);
        var results = scenarios.Select(it => RunAndPrint(it, options, output)).ToList();
        var report = new JsonObject
        {
            ["machine"] = Machine(),
            ["probe"] = options.Probe,
            ["ruleset"] = options.Ruleset ?? BaseRuleset.Civ_V_BNW.FullName(),
            ["scenarios"] = new JsonArray(results.Select(ToJson).ToArray()),
        };
        var text = report.ToJsonString(JsonOptions);
        if (jsonPath == "-") output.WriteLine(text);
        else if (jsonPath != null) File.WriteAllText(jsonPath, text + "\n");
        return CliRunner.ExitOk;
    }

    private static JsonObject Machine() => new()
    {
        ["os"] = RuntimeInformation.OSDescription,
        ["architecture"] = RuntimeInformation.OSArchitecture.ToString(),
        ["processorCount"] = Environment.ProcessorCount,
        ["runtime"] = RuntimeInformation.FrameworkDescription,
        ["optimized"] = !System.Diagnostics.Debugger.IsAttached && IsOptimized(),
        ["serverGc"] = System.Runtime.GCSettings.IsServerGC,
    };

    private static bool IsOptimized() =>
        typeof(BenchRunner).Assembly.GetCustomAttributes(typeof(System.Diagnostics.DebuggableAttribute), false)
            .Cast<System.Diagnostics.DebuggableAttribute>().All(it => !it.IsJITOptimizerDisabled);

    // ---- results

    public sealed class TurnRecord
    {
        public int Turn { get; init; }
        public double WallMs { get; init; }
        public string Hash { get; init; } = "";
        public int Cities { get; init; }
        public int Units { get; init; }
        public double GcPauseMs { get; init; }
        public int Gen2Collections { get; init; }
        public Dictionary<string, double> InclusiveMs { get; init; } = [];
    }

    public sealed class ScenarioResult
    {
        public required Scenario Scenario { get; init; }
        public required string Ruleset { get; init; }
        public List<TurnRecord> Turns { get; } = [];
        public required Recorder Engine { get; init; }
        public Recorder? Host { get; set; }
        public int HostRecipients { get; set; }
        public int HostCommands { get; set; }
        public int HostFromTurn { get; set; }
        public double TotalSeconds { get; set; }
        public string Summary { get; set; } = "";
        public int HumanAliveUntilTurn { get; set; }
        public double FinalSerializeMs { get; set; }
        public double FinalDigestMs { get; set; }

        public IReadOnlyList<string> Hashes => Turns.Select(it => it.Hash).ToList();
    }

    // ---- the clock (the only place of the solution outside the server layer that reads one for the engine's sake)

#pragma warning disable RS0030 // JUSTIFY: T261 the bench is the measuring tool: the engine reports phases through TurnProbe and only this class reads a clock
    private static long Timestamp() => System.Diagnostics.Stopwatch.GetTimestamp();

    private static double TicksToMs(long ticks) => ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
#pragma warning restore RS0030

    /// <summary>Totals of one phase name: how often it ran, the time it took with its children and without.</summary>
    public sealed class Agg
    {
        public long Count;
        public long Inclusive;
        public long Exclusive;

        public void Add(long inclusive, long exclusive)
        {
            Count++;
            Inclusive += inclusive;
            Exclusive += exclusive;
        }
    }

    /// <summary>Nested phase timing: one stack, totals per phase and per (phase, subject), and the same for the turn being played.</summary>
    public sealed class Recorder
    {
        private readonly record struct Frame(string Name, string? Subject, long Start);

        private readonly List<Frame> stack = [];
        private readonly List<long> children = [];

        public long Skew;

        public Dictionary<string, Agg> Total { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, Agg> BySubject { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, Agg> Turn { get; private set; } = new(StringComparer.Ordinal);

        public Dictionary<string, Agg> TurnSubject { get; private set; } = new(StringComparer.Ordinal);

        public List<Dictionary<string, Agg>> TurnHistory { get; } = [];

        public List<Dictionary<string, Agg>> TurnSubjectHistory { get; } = [];

        public long Now() => Timestamp() - Skew;

        public void Enter(string name, string? subject)
        {
            stack.Add(new Frame(name, subject, Now()));
            children.Add(0);
        }

        public void Exit(string name, string? subject)
        {
            var top = stack[^1];
            if (top.Name != name) throw new InvalidOperationException($"phase '{name}' ended while '{top.Name}' is open");
            var inclusive = Now() - top.Start;
            var exclusive = inclusive - children[^1];
            stack.RemoveAt(stack.Count - 1);
            children.RemoveAt(children.Count - 1);
            if (children.Count > 0) children[^1] += inclusive;
            Bump(Total, name, inclusive, exclusive);
            Bump(Turn, name, inclusive, exclusive);
            if (subject == null) return;
            var key = name + "|" + subject;
            Bump(BySubject, key, inclusive, exclusive);
            Bump(TurnSubject, key, inclusive, exclusive);
        }

        private static void Bump(Dictionary<string, Agg> map, string key, long inclusive, long exclusive)
        {
            if (!map.TryGetValue(key, out var agg)) map[key] = agg = new Agg();
            agg.Add(inclusive, exclusive);
        }

        /// <summary>Closes the books of one turn: its per-phase totals go to the history.</summary>
        public void EndTurn()
        {
            TurnHistory.Add(Turn);
            TurnSubjectHistory.Add(TurnSubject);
            Turn = new Dictionary<string, Agg>(StringComparer.Ordinal);
            TurnSubject = new Dictionary<string, Agg>(StringComparer.Ordinal);
        }
    }

    /// <summary>The probe: routes the engine's phases to the engine recorder, or (while the host measurement runs) to the host recorder.</summary>
    private sealed class BenchProbe : ITurnProbe
    {
        private readonly Recorder engine;

        public BenchProbe(Recorder engine)
        {
            this.engine = engine;
            Active = engine;
        }

        public Recorder Active { get; set; }

        public bool Paused { get; set; }

        /// <summary>Called when a command is about to be applied / was applied: the host measurement hangs on these.</summary>
        public Action? BeforeCommand { get; set; }

        public Action? AfterCommand { get; set; }

        public void Enter(string phase, string? subject)
        {
            if (Paused) return;
            if (ReferenceEquals(Active, engine) && phase == "Command.Apply") BeforeCommand?.Invoke();
            Active.Enter(phase, subject);
        }

        public void Exit(string phase, string? subject)
        {
            if (Paused) return;
            Active.Exit(phase, subject);
            if (ReferenceEquals(Active, engine) && phase == "Command.Apply") AfterCommand?.Invoke();
        }
    }

    // ---- running a scenario

    private static GameSetupInfo Setup(Scenario scenario, string ruleset) => new(
        new GameParameters
        {
            BaseRuleset = ruleset,
            UseCiv5Ai = true,
            NumberOfCityStates = scenario.CityStates,
            EspionageEnabled = true,
            NoBarbarians = false,
            VictoryTypes = ["Time"],   // a conquest or a victory must not end the measured game early
            MaxTurns = 10_000,
            Players = Enumerable.Range(0, scenario.Majors)
                .Select(i => new Player(Nations[i % Nations.Length], i == 0 ? PlayerType.Human : PlayerType.AI)).ToList(),
        },
        new MapParameters { Type = scenario.MapTypeName, Shape = MapShape.Rectangular, Seed = scenario.MapSeed, MapSize = new MapSize(scenario.MapSizeName), BaseRuleset = ruleset });

    /// <summary>
    /// Plays <paramref name="options"/>.Turns turns of the scenario (a turn = one <see cref="ScriptedHuman.PlayTurns"/> round: the human, every AI civ, the checkpoint).
    /// Runs on a big-stack thread like the soak. The per-turn state hash is taken after the turn, outside the timing.
    /// </summary>
    public static ScenarioResult RunScenario(Scenario scenario, Options options, TextWriter? log = null)
    {
        ScenarioResult? result = null;
        Exception? failure = null;
#pragma warning disable RS0030 // JUSTIFY: T261 the bench runs the game on one big-stack thread like the soak tests (deep AI recursion); the engine itself is not threaded
        var worker = new Thread(() =>
        {
            try { result = RunOnThread(scenario, options, log); }
            catch (Exception e) { failure = e; }
        }, 256 * 1024 * 1024)
        { IsBackground = true };
        worker.Start();
        worker.Join();
#pragma warning restore RS0030
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        return result!;
    }

    private static ScenarioResult RunOnThread(Scenario scenario, Options options, TextWriter? log)
    {
        var ruleset = options.Ruleset ?? BaseRuleset.Civ_V_BNW.FullName();
        var setup = Setup(scenario, ruleset);
        var turns = options.Turns ?? scenario.Turns;
        var hostTurns = options.Probe ? Math.Min(options.HostTurns ?? scenario.HostTurns, turns) : 0;
        var header = CommandLogHeader.Create(setup, scenario.GameSeed, 1000L);
        var game = GameStarter.StartNewGame(setup, scenario.GameSeed, 1000L);
        var session = new GameSession(game) { Clock = ScriptedHuman.DeterministicClock(2000L) };
        var writer = new CommandLogWriter(new StringWriter(CultureInfo.InvariantCulture), header);
        var human = new ScriptedHuman(session, Nations[0], writer);

        var engine = new Recorder();
        var result = new ScenarioResult { Scenario = scenario, Ruleset = ruleset, Engine = engine };
        var probe = new BenchProbe(engine);
        var hostFrom = turns - hostTurns + 1;
        result.HostFromTurn = hostTurns > 0 ? hostFrom : 0;
        var host = hostTurns > 0 ? new HostMeasurement(game, probe, options.Recipients, result) : null;
        var total = Timestamp();
        TurnProbe.Current = options.Probe ? probe : null;
        try
        {
            for (var played = 1; played <= turns; played++)
            {
                if (host != null && played == hostFrom) host.Start();
                var gcPause = GC.GetTotalPauseDuration();
                var gen2 = GC.CollectionCount(2);
                var start = engine.Now();
                var done = PlayRound(session, human, writer);
                var wall = engine.Now() - start;
                if (done == 0) break;
                if (options.Probe) engine.EndTurn();
                // Outside the timing and the probe: the per-turn state hash (the golden list of T262 and the equality test).
                probe.Paused = true;
                var hash = ReplayVerifier.FullHash(game).ToString("x16", CultureInfo.InvariantCulture);
                probe.Paused = false;
                var record = new TurnRecord
                {
                    Turn = game.Turns,
                    WallMs = TicksToMs(wall),
                    Hash = hash,
                    GcPauseMs = (GC.GetTotalPauseDuration() - gcPause).TotalMilliseconds,
                    Gen2Collections = GC.CollectionCount(2) - gen2,
                    Cities = game.Civilizations.Sum(it => it.Cities.Count),
                    Units = game.Civilizations.Sum(it => it.Units.GetCivUnits().Count()),
                };
                if (options.Probe && game.GetCivilization(Nations[0]).IsAlive()) result.HumanAliveUntilTurn = game.Turns;
                if (options.Probe)
                {
                    var last = engine.TurnHistory[^1];
                    foreach (var column in TurnColumns)
                        record.InclusiveMs[column] = last.TryGetValue(column, out var agg) ? TicksToMs(agg.Inclusive) : 0;
                }
                result.Turns.Add(record);
                if (played % 10 == 0 || played == turns)
                    log?.WriteLine(string.Create(CultureInfo.InvariantCulture, $"[bench {scenario.Name}] turn {record.Turn,3}: {record.WallMs,8:F1} ms, cities {record.Cities}, units {record.Units}"));
            }
        }
        finally
        {
            TurnProbe.Current = null;
        }
        // The checkpoint hash of the final state, split (the checkpoints stop when the scripted human is gone, so the per-turn numbers can hide it).
        probe.Paused = true;
        var t0 = Timestamp();
        var text = Unciv.Logic.Files.UncivFiles.GameInfoToString(game, forceZip: false);
        var t1 = Timestamp();
        _ = StateHasher.ComputeHash(h => h.Add(text));
        var t2 = Timestamp();
        probe.Paused = false;
        result.FinalSerializeMs = TicksToMs(t1 - t0);
        result.FinalDigestMs = TicksToMs(t2 - t1);
        result.TotalSeconds = TicksToMs(Timestamp() - total) / 1000.0;
        result.Summary = string.Create(CultureInfo.InvariantCulture,
            $"turn={game.Turns} civs={game.Civilizations.Count} cities={game.Civilizations.Sum(it => it.Cities.Count)} units={game.Civilizations.Sum(it => it.Units.GetCivUnits().Count())}");
        return result;
    }

    // One round: the human plays (or, when the human is gone, the host keeps the civs going) until the next human turn has begun.
    private static int PlayRound(GameSession session, ScriptedHuman human, CommandLogWriter writer)
    {
        var game = session.Game;
        if (game.GetCivilization(Nations[0]).IsAlive()) return human.PlayTurns(1);
        var before = game.Turns;
        var steps = 0;
        while (game.Turns < before + 1 && game.TurnPhase is not TurnPhase.GameOver && steps++ < 100_000)
        {
            GameCommand command = game.TurnPhase switch
            {
                TurnPhase.AutoPlayers auto => new RunAiTurn { CivId = auto.NextCiv.CivId },
                TurnPhase.AwaitingInput waiting when !game.IsTurnStarted(waiting.Civ) => new BeginTurn { CivId = waiting.Civ.CivId },
                TurnPhase.AwaitingInput waiting => new ForceEndTurn { CivId = waiting.Civ.CivId },
                _ => throw new InvalidOperationException("Unexpected turn phase " + game.TurnPhase),
            };
            var result = session.SubmitAsServer(command);
            if (!result.Accepted) throw new InvalidOperationException($"{command.Type} refused: {result.Validation.Message}");
            writer.Sync(session);
        }
        return game.Turns - before;
    }

    /// <summary>
    /// What the host does after every accepted command for each connected recipient (GameHost.CaptureViewMarks / BroadcastDelta): capture the recipient's
    /// view before, build the snapshot diff and delta after, encode it. Measured on its own recorder around each command; the time it takes is added to the
    /// engine recorder's skew, so the engine numbers are those of a game without recipients. Events are not passed (the filtered-event part is small).
    /// </summary>
    private sealed class HostMeasurement(GameInfo game, BenchProbe probe, int recipientCount, ScenarioResult result)
    {
        private readonly Recorder recorder = new();
        private List<(Civilization Civ, ClientSnapshot Snapshot)> recipients = [];
        private List<ViewDeltaBuilder.ViewMark> marks = [];
        private long seq;

        public void Start()
        {
            result.Host = recorder;
            var status = new TurnStatusDto { Ended = [], Deadline = null, Phase = "Playing" };
            recipients = game.Civilizations.Where(it => it.IsMajorCiv() && it.IsAlive()).Take(recipientCount)
                .Select(it => (it, SnapshotBuilder.Build(game, it, 0, status))).ToList();
            result.HostRecipients = recipients.Count;
            probe.BeforeCommand = Before;
            probe.AfterCommand = After;
        }

        private void Before() => Measure(() =>
        {
            marks = [];
            foreach (var (civ, _) in recipients)
            {
                recorder.Enter("Host.Capture", null);
                marks.Add(ViewDeltaBuilder.Capture(game, civ));
                recorder.Exit("Host.Capture", null);
            }
        });

        private void After() => Measure(() =>
        {
            var status = new TurnStatusDto { Ended = [], Deadline = null, Phase = "Playing" };
            seq++;
            result.HostCommands++;
            for (var i = 0; i < recipients.Count; i++)
            {
                var (civ, before) = recipients[i];
                recorder.Enter("Host.BuildDelta", null);
                var (delta, after) = ViewDeltaBuilder.BuildWithDiff(game, civ, before, marks[i], [], seq, status);
                recorder.Exit("Host.BuildDelta", null);
                recorder.Enter("Host.Encode", null);
                _ = ProtocolCodec.Encode(delta);
                recorder.Exit("Host.Encode", null);
                recipients[i] = (civ, after);
            }
        });

        private void Measure(Action work)
        {
            var start = Timestamp();
            var inner = probe.Active;
            probe.Active = recorder;
            try { work(); }
            finally
            {
                probe.Active = inner;
                inner.Skew += Timestamp() - start;
            }
        }
    }

    // ---- report

    private static double R(double value, int digits) => Math.Round(value, digits, MidpointRounding.ToEven);

    private static double Ms(long ticks) => R(TicksToMs(ticks), 3);

    private static double Median(List<double> values)
    {
        if (values.Count == 0) return 0;
        var sorted = values.Order().ToList();
        return sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;
    }

    /// <summary>The median wall time of the last fifth of the turns (the number T262's target is measured against).</summary>
    public static double LateMedianMs(ScenarioResult result)
    {
        var count = Math.Max(1, result.Turns.Count / 5);
        return Median(result.Turns.TakeLast(count).Select(it => it.WallMs).ToList());
    }

    private static Dictionary<string, Agg> Sum(IEnumerable<Dictionary<string, Agg>> turns)
    {
        var sum = new Dictionary<string, Agg>(StringComparer.Ordinal);
        foreach (var turn in turns)
            foreach (var (key, agg) in turn)
            {
                if (!sum.TryGetValue(key, out var total)) sum[key] = total = new Agg();
                total.Count += agg.Count;
                total.Inclusive += agg.Inclusive;
                total.Exclusive += agg.Exclusive;
            }
        return sum;
    }

    private static JsonObject AggJson(string name, Agg agg, int turns) => new()
    {
        ["phase"] = name,
        ["calls"] = agg.Count,
        ["inclusiveMs"] = Ms(agg.Inclusive),
        ["exclusiveMs"] = Ms(agg.Exclusive),
        ["inclusiveMsPerTurn"] = turns == 0 ? 0 : R(TicksToMs(agg.Inclusive) / turns, 3),
        ["exclusiveMsPerTurn"] = turns == 0 ? 0 : R(TicksToMs(agg.Exclusive) / turns, 3),
        ["meanMsPerCall"] = agg.Count == 0 ? 0 : R(TicksToMs(agg.Inclusive) / agg.Count, 4),
    };

    private static JsonArray TopPhases(Dictionary<string, Agg> sum, int turns, int top) =>
        new(sum.OrderByDescending(it => it.Value.Inclusive).ThenBy(it => it.Key, StringComparer.Ordinal).Take(top)
            .Select(it => (JsonNode)AggJson(it.Key, it.Value, turns)).ToArray());

    private static JsonNode Window(ScenarioResult result, int from, int count)
    {
        var turns = result.Turns.Skip(from).Take(count).ToList();
        var phases = Sum(result.Engine.TurnHistory.Skip(from).Take(count));
        var subjects = Sum(result.Engine.TurnSubjectHistory.Skip(from).Take(count));
        return new JsonObject
        {
            ["fromTurn"] = turns.Count == 0 ? 0 : turns[0].Turn,
            ["toTurn"] = turns.Count == 0 ? 0 : turns[^1].Turn,
            ["turns"] = turns.Count,
            ["medianWallMs"] = R(Median(turns.Select(it => it.WallMs).ToList()), 2),
            ["meanWallMs"] = R(turns.Count == 0 ? 0 : turns.Average(it => it.WallMs), 2),
            ["phases"] = TopPhases(phases, turns.Count, 20),
            ["civs"] = CivRows(subjects, turns.Count),
        };
    }

    private static JsonArray CivRows(Dictionary<string, Agg> subjects, int turns)
    {
        var civs = subjects.Keys.Where(it => CivPhases.Any(phase => it.StartsWith(phase + "|", StringComparison.Ordinal))).Select(it => it[(it.IndexOf('|', StringComparison.Ordinal) + 1)..]).Distinct(StringComparer.Ordinal).ToList();
        IEnumerable<JsonObject> rows = civs.Select(civ =>
        {
            var row = new JsonObject { ["civ"] = civ };
            long all = 0;
            foreach (var phase in CivPhases)
            {
                var ticks = subjects.TryGetValue(phase + "|" + civ, out var agg) ? agg.Inclusive : 0;
                all += ticks;
                row[phase] = R(turns == 0 ? 0 : TicksToMs(ticks) / turns, 3);
            }
            row["totalMsPerTurn"] = R(turns == 0 ? 0 : TicksToMs(all) / turns, 3);
            return row;
        });
        return new JsonArray(rows.OrderByDescending(it => (double)it["totalMsPerTurn"]!).ThenBy(it => (string)it["civ"]!, StringComparer.Ordinal).Select(it => (JsonNode)it).ToArray());
    }

    private static JsonObject ToJson(ScenarioResult result)
    {
        var scenario = result.Scenario;
        var count = result.Turns.Count;
        var quarter = Math.Max(1, count / 4);
        var json = new JsonObject
        {
            ["name"] = scenario.Name,
            ["majors"] = scenario.Majors,
            ["cityStates"] = scenario.CityStates,
            ["mapSize"] = scenario.MapSizeName,
            ["mapType"] = scenario.MapTypeName,
            ["gameSeed"] = scenario.GameSeed,
            ["mapSeed"] = scenario.MapSeed,
            ["ruleset"] = result.Ruleset,
            ["turnsPlayed"] = count,
            ["finalState"] = result.Summary,
            ["totalSeconds"] = R(result.TotalSeconds, 2),
            ["humanAliveUntilTurn"] = result.HumanAliveUntilTurn,
            ["finalHashSerializeMs"] = R(result.FinalSerializeMs, 2),
            ["finalHashDigestMs"] = R(result.FinalDigestMs, 2),
            ["lateMedianWallMs"] = R(LateMedianMs(result), 2),
            ["turns"] = new JsonArray(result.Turns.Select(it => (JsonNode)new JsonObject
            {
                ["turn"] = it.Turn,
                ["wallMs"] = R(it.WallMs, 2),
                ["hash"] = it.Hash,
                ["cities"] = it.Cities,
                ["units"] = it.Units,
                ["gcPauseMs"] = R(it.GcPauseMs, 2),
                ["gen2"] = it.Gen2Collections,
                ["inclusiveMs"] = new JsonObject(it.InclusiveMs.Select(p => KeyValuePair.Create(p.Key, (JsonNode?)R(p.Value, 2)))),
            }).ToArray()),
        };
        if (result.Engine.TurnHistory.Count == count && count > 0)
        {
            json["all"] = Window(result, 0, count);
            json["firstQuarter"] = Window(result, 0, quarter);
            json["lastQuarter"] = Window(result, count - quarter, quarter);
            json["commands"] = new JsonArray(result.Engine.BySubject.Where(it => it.Key.StartsWith("Command.Apply|", StringComparison.Ordinal))
                .OrderByDescending(it => it.Value.Inclusive)
                .Select(it => (JsonNode)AggJson(it.Key["Command.Apply|".Length..], it.Value, count)).ToArray());
        }
        if (result.Host != null && result.HostCommands > 0)
        {
            var perRecipientCommand = Math.Max(1, result.HostCommands * Math.Max(1, result.HostRecipients));
            var host = new JsonObject
            {
                ["fromTurn"] = result.HostFromTurn,
                ["recipients"] = result.HostRecipients,
                ["commands"] = result.HostCommands,
                ["note"] = "ViewDeltaBuilder.Capture before and BuildWithDiff plus ProtocolCodec.Encode after every command, per recipient, without engine events; engine times above exclude this",
            };
            foreach (var (name, agg) in result.Host.Total.OrderBy(it => it.Key, StringComparer.Ordinal))
            {
                var row = AggJson(name, agg, 0);
                row["msPerRecipientCommand"] = R(TicksToMs(agg.Inclusive) / perRecipientCommand, 3);
                ((JsonObject)(host["phases"] ??= new JsonObject()))[name] = row;
            }
            json["host"] = host;
        }
        return json;
    }

    /// <summary>A short text of the report for the terminal.</summary>
    public static void WriteSummary(ScenarioResult result, TextWriter output)
    {
        var count = result.Turns.Count;
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"scenario {result.Scenario.Name}: {count} turns in {result.TotalSeconds:F1} s ({result.Summary}); late median {LateMedianMs(result):F1} ms/turn"));
        if (result.Engine.TurnHistory.Count != count || count == 0) return;
        var last = Sum(result.Engine.TurnHistory.Skip(count - Math.Max(1, count / 4)));
        var turns = Math.Max(1, count / 4);
        output.WriteLine("  last quarter, inclusive ms per turn (top 12):");
        foreach (var (name, agg) in last.OrderByDescending(it => it.Value.Inclusive).ThenBy(it => it.Key, StringComparer.Ordinal).Take(12))
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"    {name,-36} {TicksToMs(agg.Inclusive) / turns,9:F2} (self {TicksToMs(agg.Exclusive) / turns:F2})"));
    }

    private static ScenarioResult RunAndPrint(Scenario scenario, Options options, TextWriter output)
    {
        output.WriteLine($"running scenario {scenario.Name} ({scenario.Majors} civs, {scenario.CityStates} city states, {scenario.MapSizeName} {scenario.MapTypeName}, {options.Turns ?? scenario.Turns} turns)");
        var result = RunScenario(scenario, options, output);
        WriteSummary(result, output);
        return result;
    }
}
