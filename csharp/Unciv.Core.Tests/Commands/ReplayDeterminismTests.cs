using System.Globalization;
using Unciv.Commands;
using Unciv.Commands.Log;
using Unciv.Commands.Turn;
using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Logic.Files;
using Unciv.Logic.Map;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using RulesetClass = Unciv.Models.Ruleset.Ruleset;

namespace Unciv.Core.Tests.Commands;

// T194: a scripted human plays through the command processor; the log it leaves must replay to the same state hash at every checkpoint.
[Collection("RulesetCache")]
public sealed class ReplayDeterminismTests : IDisposable
{
    private const int Turns = 60;
    private const string Human = "Babylon";
    private readonly RulesetClass ruleset;

    public ReplayDeterminismTests()
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

    private GameSetupInfo Setup(int mapSeed) => new(
        new GameParameters
        {
            BaseRuleset = ruleset.Name,
            NumberOfCityStates = 0,
            VictoryTypes = ruleset.Victories.Keys.ToList(),
            Players = new[] { Human, "Greece", "Rome" }.Select((it, i) => new Player(it, i == 0 ? PlayerType.Human : PlayerType.AI)).ToList(),
        },
        new MapParameters { Type = MapType.Pangaea, Shape = MapShape.Rectangular, Seed = mapSeed, MapSize = MapSize.Tiny });

    private sealed record Live(GameSession Session, ScriptedHuman Player, StringWriter Text, CommandLogHeader Header);

    private Live Start(long gameSeed, int mapSeed)
    {
        var setup = Setup(mapSeed);
        var header = CommandLogHeader.Create(setup, gameSeed, 1000L);
        var game = GameStarter.StartNewGame(setup, gameSeed, 1000L);
        var session = new GameSession(game) { Clock = ScriptedHuman.DeterministicClock(2000L) };
        var text = new StringWriter(CultureInfo.InvariantCulture);
        return new Live(session, new ScriptedHuman(session, Human, new CommandLogWriter(text, header)), text, header);
    }

    private Live Play(long gameSeed, int mapSeed, int turns)
    {
        var live = Start(gameSeed, mapSeed);
        Assert.Equal(turns, live.Player.PlayTurns(turns));
        return live;
    }

    private static List<ulong> Checkpoints(CommandLog log) => log.Lines.OfType<CommandLogLine.Checkpoint>().Select(it => it.FullHash).ToList();

    [Theory]
    [InlineData(1L, 3)]
    [InlineData(77L, 11)]
    public void ReplayGivesTheLiveHashAtEveryCheckpoint(long gameSeed, int mapSeed)
    {
        var live = Play(gameSeed, mapSeed, Turns);
        var log = CommandLogReader.Read(live.Text.ToString());
        Assert.True(Checkpoints(log).Distinct().Count() >= Turns); // the game really advances
        Assert.Contains(live.Session.Game.Civilizations, it => it.CivId == Human && it.Cities.Count > 0); // the scripted human founded a city
        Assert.Contains(log.Lines, it => it is CommandLogLine.Command { Envelope.Type: "execute-unit-action" });

        var result = ReplayVerifier.Verify(log);
        Assert.True(result.Matches, $"diverged {result.DivergedAtSeq} after {result.CheckpointsVerified} of {Checkpoints(log).Count}");
        Assert.Null(result.DivergedAtSeq);
        Assert.Equal(Checkpoints(log).Count, result.CheckpointsVerified);
        Assert.Equal(live.Session.LastSeq, result.LastSeq);
        Assert.Equal(ReplayVerifier.FullHash(live.Session.Game), result.FinalHash);
    }

    [Fact]
    public void SameSeedTwiceGivesTheSameLogByteForByte()
    {
        var first = Play(5L, 8, 20).Text.ToString();
        Assert.Equal(first, Play(5L, 8, 20).Text.ToString());
        Assert.NotEqual(first, Play(6L, 8, 20).Text.ToString());
        Assert.DoesNotContain('\r', first);
        Assert.All(first.TrimEnd('\n').Split('\n'), line => Assert.StartsWith("{", line, StringComparison.Ordinal));
    }

    [Fact]
    public void SaveLoadContinueGivesTheSameFinalHash()
    {
        var live = Play(9L, 5, 20);
        var snapshotSeq = live.Session.LastSeq;
        var saved = UncivFiles.GameInfoToString(live.Session.Game, forceZip: false);

        var loaded = UncivFiles.GameInfoFromString(saved);
        Assert.Equal(ReplayVerifier.FullHash(live.Session.Game), ReplayVerifier.FullHash(loaded));
        // The original's clock has read once per timed command so far; the continuation must read the same values from here on.
        var clock = ScriptedHuman.DeterministicClock(2000L);
        for (var i = live.Session.Log.Count(it => it.Payload.TryGetProperty("now", out _)); i > 0; i--) clock();
        var continued = new GameSession(loaded, lastSeq: snapshotSeq) { Clock = clock };

        Assert.Equal(50, live.Player.PlayTurns(50));
        Assert.Equal(50, new ScriptedHuman(continued, Human).PlayTurns(50));
        Assert.Equal(ReplayVerifier.FullHash(live.Session.Game), ReplayVerifier.FullHash(loaded));
        Assert.Equal(live.Session.LastSeq, continued.LastSeq);
    }

    // T215: a loaded mid-game state must continue exactly like the live one (the unit list order used to differ: a load rebuilds it tile by tile).
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(8)]
    [InlineData(20)]
    [InlineData(30)]
    [InlineData(40)]
    [InlineData(55)]
    public void SnapshotPlusLogTailReproducesTheFinalHash(int snapshotTurn)
    {
        var live = Play(9L, 5, snapshotTurn);
        var snapshotSeq = live.Session.LastSeq;
        var snapshot = UncivFiles.GameInfoFromString(UncivFiles.GameInfoToString(live.Session.Game, forceZip: false));
        Assert.Equal(50, live.Player.PlayTurns(50));

        var log = CommandLogReader.Read(live.Text.ToString());
        var result = ReplayVerifier.VerifyTail(snapshot, snapshotSeq, log);
        Assert.True(result.Matches, $"{result.DivergedAtSeq} {result.CheckpointsVerified} snap {snapshotSeq}");
        Assert.Equal(live.Session.LastSeq, result.LastSeq);
        Assert.Equal(ReplayVerifier.FullHash(live.Session.Game), result.FinalHash);
        Assert.True(result.CheckpointsVerified >= 50);
    }

    [Fact]
    public void AnAiOnlyGameLoadedMidwayContinuesLikeTheLiveOne()
    {
        var live = Start(9L, 5).Session.Game;
        foreach (var g in new[] { live }) { g.SimulateUntilWin = true; }
        for (var i = 0; i < 20; i++) { live.SimulateMaxTurns = live.Turns + 1; live.NextTurn(2000L); }
        var loaded = UncivFiles.GameInfoFromString(UncivFiles.GameInfoToString(live, forceZip: false));
        for (var i = 0; i < 25; i++)
        {
            foreach (var g in new[] { live, loaded }) { g.SimulateMaxTurns = g.Turns + 1; g.NextTurn(2000L); }
            Assert.True(ReplayVerifier.FullHash(live) == ReplayVerifier.FullHash(loaded), $"diverged {i + 1} turns after loading");
        }
    }

    [Fact]
    public void TamperedPayloadIsADesync()
    {
        var text = Play(1L, 3, 12).Text.ToString();
        var lines = text.TrimEnd('\n').Split('\n').ToList();
        var index = lines.FindIndex(it => it.Contains("\"type\":\"execute-unit-action\"", StringComparison.Ordinal));
        Assert.True(index > 0);
        lines[index] = lines[index].Replace("\"UnitId\":", "\"UnitId\":99999", StringComparison.Ordinal);
        var tampered = CommandLogReader.Read(string.Join('\n', lines));
        Assert.Throws<CommandDesyncException>(() => ReplayVerifier.Verify(tampered));
    }

    [Fact]
    public void WrongCheckpointHashStopsReplayAtItsSeq()
    {
        var log = CommandLogReader.Read(Play(1L, 3, 12).Text.ToString());
        var checkpoints = log.Lines.OfType<CommandLogLine.Checkpoint>().ToList();
        var bad = checkpoints[5];
        var lines = log.Lines.Select(it => it == bad ? bad with { FullHash = bad.FullHash ^ 1 } : it).ToList();
        var result = ReplayVerifier.Verify(log with { Lines = lines });
        Assert.False(result.Matches);
        Assert.Equal(bad.Seq, result.DivergedAtSeq);
        Assert.Equal(5, result.CheckpointsVerified);
        Assert.Equal(bad.Seq, result.LastSeq); // it did not continue past the divergence
    }

    [Fact]
    public void ReorderedOrDroppedLinesAreADesyncAndATruncatedTailIsStillVerified()
    {
        var log = CommandLogReader.Read(Play(1L, 3, 6).Text.ToString());
        var checkpoint = log.Lines.OfType<CommandLogLine.Checkpoint>().Last();
        var misplaced = log.Lines.Select(it => it == checkpoint ? checkpoint with { Seq = checkpoint.Seq - 1 } : it).ToList();
        Assert.Throws<CommandDesyncException>(() => ReplayVerifier.Verify(log with { Lines = misplaced }));

        var dropped = log.Lines.Where((_, i) => i != 3).ToList(); // a command missing from the middle
        Assert.Throws<CommandDesyncException>(() => ReplayVerifier.Verify(log with { Lines = dropped }));

        var swapped = log.Lines.ToList();
        (swapped[1], swapped[2]) = (swapped[2], swapped[1]);
        Assert.Throws<CommandDesyncException>(() => ReplayVerifier.Verify(log with { Lines = swapped }));

        var truncated = log.Lines.Take(log.Lines.Count / 2).ToList();
        var result = ReplayVerifier.Verify(log with { Lines = truncated });
        Assert.True(result.Matches);
        Assert.True(result.CheckpointsVerified < Checkpoints(log).Count);
    }

    [Fact]
    public void ALogFromAnotherEngineVersionOrRulesetIsRefused()
    {
        var log = CommandLogReader.Read(Play(1L, 3, 3).Text.ToString());
        var otherEngine = log with { Header = log.Header with { EngineVersion = "0.0.0-other" } };
        Assert.Contains("engine", Assert.Throws<CommandLogException>(() => ReplayVerifier.Verify(otherEngine)).Message, StringComparison.Ordinal);
        var otherRuleset = log with { Header = log.Header with { RulesetHash = "0000000000000000" } };
        Assert.Contains("ruleset", Assert.Throws<CommandLogException>(() => ReplayVerifier.Verify(otherRuleset)).Message, StringComparison.Ordinal);
        var otherFormat = log with { Header = log.Header with { FormatVersion = 99 } };
        Assert.Contains("format", Assert.Throws<CommandLogException>(() => ReplayVerifier.Verify(otherFormat)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectedCommandsAreNotLogged()
    {
        var live = Start(1L, 3);
        var before = live.Text.ToString();
        var result = live.Session.Submit("Greece", new EndTurn()); // not Greece's turn
        Assert.False(result.Accepted);
        live.Player.PlayTurns(0);
        Assert.Equal(0, live.Session.LastSeq);
        Assert.Equal(before + CheckpointLine(live), live.Text.ToString());
    }

    private static string CheckpointLine(Live live) =>
        $"{{\"checkpoint\":0,\"turn\":{live.Session.Game.Turns.ToString(CultureInfo.InvariantCulture)},\"fullHash\":\"{ReplayVerifier.FullHash(live.Session.Game).ToString("x16", CultureInfo.InvariantCulture)}\"}}\n";

    [Fact]
    public void ReaderReportsBrokenLogs()
    {
        Assert.Throws<CommandLogException>(() => CommandLogReader.Read(""));
        Assert.Throws<CommandLogException>(() => CommandLogReader.Read("{not json"));
        var header = Play(1L, 3, 1).Text.ToString().Split('\n')[0];
        Assert.Throws<CommandLogException>(() => CommandLogReader.Read(header + "\n{not json"));
        Assert.Throws<CommandLogException>(() => CommandLogReader.Read(header + "\n[1]"));
        Assert.Throws<CommandLogException>(() => CommandLogReader.Read(header + "\n{\"seq\":1}"));
        Assert.Throws<CommandLogException>(() => CommandLogReader.Read(header + "\n{\"checkpoint\":1,\"turn\":1,\"fullHash\":\"zz\"}"));
        Assert.Empty(CommandLogReader.Read(header + "\n\n").Lines);
    }

    [Fact]
    public void WriterRefusesOutOfOrderCommands()
    {
        var live = Start(1L, 3);
        live.Player.PlayTurns(1);
        var writer = new CommandLogWriter(new StringWriter(CultureInfo.InvariantCulture), live.Header);
        Assert.Throws<InvalidOperationException>(() => writer.Write(live.Session.Log[1]));
    }

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
    public void CurrentCultureChangesNeitherTheLogNorItsReplay()
    {
        var invariant = Play(3L, 2, 20).Text.ToString();
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = OddCulture();
            Assert.Equal(invariant, Play(3L, 2, 20).Text.ToString());
            Assert.True(ReplayVerifier.Verify(CommandLogReader.Read(invariant)).Matches);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }
}
