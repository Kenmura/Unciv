using System.Globalization;
using System.Text;
using System.Text.Json;
using Unciv.Commands;
using Unciv.Commands.Log;
using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Logic.Files;
using Unciv.Logic.Map;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using Unciv.Net.Persistence;
using Unciv.Utils;
using RulesetClass = Unciv.Models.Ruleset.Ruleset;

namespace Unciv.Core.Tests.Net.Persistence;

// T176: the host's game store. A scripted human plays (T194); everything it does is fed into a store the way the host would, and the tests then break the files.
[Collection("RulesetCache")]
public sealed class NetPersistenceTests : IDisposable
{
    private const string Human = "Babylon";
    private const long Secret = 0x5EC12E7A11CEL;
    private readonly RulesetClass ruleset;
    private readonly string root = Directory.CreateTempSubdirectory("unciv-store-").FullName;
    private int sequence;

    public NetPersistenceTests()
    {
        RulesetCache.Clear();
        var jsons = JsonsPath();
        RulesetCache.LoadRulesets(new DirectoryRulesetFileSource(jsons, Path.Combine(jsons, "no mods here")), noMods: true);
        ruleset = RulesetCache.GetVanillaRuleset();
    }

    public void Dispose()
    {
        RulesetCache.Clear();
        Directory.Delete(root, recursive: true);
    }

    private static string JsonsPath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "android", "assets", "jsons");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException("android/assets/jsons not found above " + AppContext.BaseDirectory);
    }

    private GameSetupInfo Setup() => new(
        new GameParameters
        {
            BaseRuleset = ruleset.Name,
            NumberOfCityStates = 0,
            VictoryTypes = ruleset.Victories.Keys.ToList(),
            Players = new[] { Human, "Greece", "Rome" }.Select((it, i) => new Player(it, i == 0 ? PlayerType.Human : PlayerType.AI)).ToList(),
        },
        new MapParameters { Type = MapType.Pangaea, Shape = MapShape.Rectangular, Seed = 5, MapSize = MapSize.Tiny });

    private sealed class Live
    {
        public required GameSession Session { get; init; }
        public required ScriptedHuman Player { get; init; }
        public required StringWriter Text { get; init; }
        public required FileGameStore Store { get; set; }
        public required string Directory { get; init; }
        public int Fed { get; set; }
    }

    private Live Start(StoreOptions? options = null)
    {
        var setup = Setup();
        var header = CommandLogHeader.Create(setup, 9L, 1000L, null, Secret);
        var game = HostSecret.ApplyTo(GameStarter.StartNewGame(setup, 9L, 1000L), Secret);
        var dir = Path.Combine(root, "game" + sequence++);
        var store = FileGameStore.Create(dir, "g1", header, new GameMeta(), game, options);
        var session = new GameSession(game) { Clock = ScriptedHuman.DeterministicClock(2000L) };
        var text = new StringWriter(CultureInfo.InvariantCulture);
        return new Live { Session = session, Player = new ScriptedHuman(session, Human, new CommandLogWriter(text, header)), Text = text, Store = store, Directory = dir };
    }

    // Plays turns and appends to the store what the scripted human logged (the host's append-before-ack), then snapshots when asked.
    private static void Play(Live live, int turns, bool snapshot = false, bool withIds = false)
    {
        Assert.Equal(turns, live.Player.PlayTurns(turns));
        var lines = CommandLogReader.Read(live.Text.ToString()).Lines;
        foreach (var line in lines.Skip(live.Fed))
        {
            if (line is CommandLogLine.Command command) live.Store.Append(command.Envelope, withIds ? "cmd-" + command.Envelope.Seq : null);
            else if (line is CommandLogLine.Checkpoint checkpoint) live.Store.AppendCheckpoint(checkpoint.Seq, checkpoint.Turn, checkpoint.FullHash);
        }
        live.Fed = lines.Count;
        if (snapshot) Assert.True(live.Store.TrySnapshot(live.Session.Game));
    }

    // The same line with its hash changed in the last digit: still a valid line, but the state cannot match it.
    private static string TamperHash(string checkpointLine) => checkpointLine[..^3] + (checkpointLine[^3] == 'a' ? 'b' : 'a') + "\"}";

    private static ulong Hash(GameInfo game) => ReplayVerifier.FullHash(game);

    private static string LogPath(Live live) => Path.Combine(live.Directory, "commands.log");

    private static IEnumerable<string> Snapshots(Live live) => Directory.GetFiles(live.Directory, "snapshot-*").Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal);

    private static OpenedGame Reopen(Live live, StoreOptions? options = null)
    {
        live.Store.Dispose();
        return FileGameStore.Open(live.Directory, options);
    }

    [Fact]
    public void RecoveryIsLatestSnapshotPlusVerifiedTail()
    {
        var live = Start();
        Play(live, 6, snapshot: true);
        var snapshotSeq = live.Session.LastSeq;
        Play(live, 8);

        using var opened = Reopen(live);
        Assert.Equal(Hash(live.Session.Game), Hash(opened.Session.Game));
        Assert.Equal(live.Session.LastSeq, opened.Store.LastSeq);
        Assert.Equal(live.Session.LastSeq, opened.Session.LastSeq);
        Assert.Equal(RecoveryOutcome.Recovered, opened.Report.Outcome);
        Assert.Equal(snapshotSeq, opened.Report.SnapshotSeq);
        Assert.True(opened.Report.ReplayedCommands > 0);
        Assert.True(opened.Report.CheckpointsVerified >= 8);
        Assert.False(opened.Report.WasCleanShutdown);
        Assert.Equal(0, opened.Report.DroppedLogLines);
    }

    [Fact]
    public void RecoveredGameKeepsPlayingToTheSameHashAsTheLiveOne()
    {
        var live = Start();
        Play(live, 5, snapshot: true);
        Play(live, 3);
        using var opened = Reopen(live);

        var clock = ScriptedHuman.DeterministicClock(2000L);
        for (var i = live.Session.Log.Count(it => it.Payload.TryGetProperty("now", out _)); i > 0; i--) clock();
        var continued = new GameSession(opened.Session.Game, lastSeq: opened.Store.LastSeq) { Clock = clock };
        Assert.Equal(10, live.Player.PlayTurns(10));
        Assert.Equal(10, new ScriptedHuman(continued, Human).PlayTurns(10));
        Assert.Equal(Hash(live.Session.Game), Hash(continued.Game));
    }

    [Fact]
    public void TheLogTheStoreWritesIsTheT194LogAndReplaysFromItsHeader()
    {
        var live = Start();
        Play(live, 12);
        live.Store.Dispose();
        var log = CommandLogReader.Read(File.ReadAllText(LogPath(live)));
        var result = ReplayVerifier.Verify(log);
        Assert.True(result.Matches);
        Assert.Equal(live.Session.LastSeq, result.LastSeq);
        Assert.Equal(Hash(live.Session.Game), result.FinalHash);
        Assert.Equal(live.Text.ToString(), File.ReadAllText(LogPath(live))); // byte for byte what CommandLogWriter writes
    }

    [Fact]
    public void SnapshotsOpenThroughUncivFiles()
    {
        var live = Start();
        Play(live, 4, snapshot: true);
        var name = Snapshots(live).Last();
        var loaded = UncivFiles.GameInfoFromString(File.ReadAllText(Path.Combine(live.Directory, name)));
        Assert.Equal(Hash(live.Session.Game), Hash(loaded));
        Assert.Matches(@"^snapshot-\d{12}\.json\.gz$", name);
    }

    // ---------------------------------------------------------------- crashes

    [Fact]
    public void KillMidWriteLeavesATornLineThatRecoveryCutsOff()
    {
        var live = Start();
        Play(live, 4, snapshot: true);
        Play(live, 3);
        live.Store.Dispose();
        var good = File.ReadAllBytes(LogPath(live));
        File.AppendAllText(LogPath(live), "{\"seq\":99999,\"type\":\"end-tu", Encoding.UTF8); // the host died inside the write

        using var opened = FileGameStore.Open(live.Directory);
        Assert.Equal(Hash(live.Session.Game), Hash(opened.Session.Game));
        Assert.Equal(1, opened.Report.DroppedLogLines);
        Assert.Contains(opened.Report.Notes, it => it.Contains("Cut off 1", StringComparison.Ordinal));
        Assert.Equal(good, File.ReadAllBytes(LogPath(live)));
        Assert.Single(Directory.GetFiles(Path.Combine(live.Directory, "corrupt")));
    }

    [Fact]
    public void KillBeforeTheLastLineIsCompleteDropsOnlyThatCommand()
    {
        var live = Start();
        Play(live, 4, snapshot: true);
        Play(live, 2);
        live.Store.Dispose();
        var text = File.ReadAllText(LogPath(live));
        var lastLine = text.TrimEnd('\n').Split('\n')[^1];
        File.WriteAllText(LogPath(live), text[..^(lastLine.Length / 2 + 1)]); // cut inside the last line
        var survivors = CommandLogReader.Read(File.ReadAllText(LogPath(live)).Substring(0, File.ReadAllText(LogPath(live)).LastIndexOf('\n') + 1));

        using var opened = FileGameStore.Open(live.Directory);
        Assert.Equal(1, opened.Report.DroppedLogLines);
        Assert.Equal(survivors.Lines.OfType<CommandLogLine.Command>().Last().Envelope.Seq, opened.Store.LastSeq);
        Assert.EndsWith("\n", File.ReadAllText(LogPath(live)), StringComparison.Ordinal);
    }

    [Fact]
    public void CorruptTailIsTruncatedReportedAndAppendingContinues()
    {
        var live = Start();
        Play(live, 4, snapshot: true);
        var seqBefore = live.Session.LastSeq;
        live.Store.Dispose();
        File.AppendAllText(LogPath(live), "not json at all\n\0\0\0\0\n{\"seq\":" + (seqBefore + 1) + "}\n", Encoding.UTF8);

        using var opened = FileGameStore.Open(live.Directory);
        Assert.Equal(seqBefore, opened.Store.LastSeq);
        Assert.Equal(3, opened.Report.DroppedLogLines);
        Assert.Equal(Hash(live.Session.Game), Hash(opened.Session.Game));
        var tail = Directory.GetFiles(Path.Combine(live.Directory, "corrupt")).Single();
        Assert.Contains("not json at all", File.ReadAllText(tail), StringComparison.Ordinal);

        // The log is clean again: a new command appends and a further reopen finds nothing to drop.
        var game = opened.Session.Game;
        var result = opened.Session.Submit(Human, new Unciv.Commands.Turn.EndTurn());
        opened.Store.Sync(opened.Session);
        Assert.Equal(result.Accepted, opened.Store.LastSeq > seqBefore);
        opened.Store.Dispose();
        using var again = FileGameStore.Open(live.Directory);
        Assert.Equal(0, again.Report.DroppedLogLines);
        Assert.NotNull(game);
    }

    [Fact]
    public void ALineThatBreaksTheSeqOrderEndsTheValidLog()
    {
        var live = Start();
        Play(live, 4, snapshot: true);
        live.Store.Dispose();
        var lines = File.ReadAllText(LogPath(live)).TrimEnd('\n').Split('\n').ToList();
        var commandIndex = lines.FindLastIndex(it => it.Contains("\"seq\":", StringComparison.Ordinal));
        lines.Insert(commandIndex, lines[commandIndex]); // the same command twice
        File.WriteAllText(LogPath(live), string.Join('\n', lines) + "\n");

        using var opened = FileGameStore.Open(live.Directory);
        Assert.True(opened.Report.DroppedLogLines >= 1);
        Assert.Equal(RecoveryOutcome.Recovered, opened.Report.Outcome);
    }

    [Fact]
    public void LeftoverTempFilesFromAnInterruptedWriteAreRemoved()
    {
        var live = Start();
        Play(live, 3, snapshot: true);
        File.WriteAllText(Path.Combine(live.Directory, "snapshot-000000099999.json.gz.tmp"), "half");
        using var opened = Reopen(live);
        Assert.Empty(Directory.GetFiles(live.Directory, "*.tmp"));
        Assert.Equal(Hash(live.Session.Game), Hash(opened.Session.Game));
    }

    [Fact]
    public void ACorruptNewestSnapshotFallsBackToAnOlderOneAndTheTailReplaysLonger()
    {
        var live = Start();
        Play(live, 3, snapshot: true);
        var older = live.Session.LastSeq;
        Play(live, 3, snapshot: true);
        var newest = Snapshots(live).Last();
        File.WriteAllText(Path.Combine(live.Directory, newest), "this is not a save");

        using var opened = Reopen(live);
        Assert.Equal(RecoveryOutcome.Recovered, opened.Report.Outcome);
        Assert.Equal(older, opened.Report.SnapshotSeq);
        Assert.Equal(Hash(live.Session.Game), Hash(opened.Session.Game));
        Assert.Contains(opened.Report.Notes, it => it.Contains("cannot be read", StringComparison.Ordinal));
        Assert.DoesNotContain(newest, Snapshots(live));
        Assert.Single(Directory.GetFiles(Path.Combine(live.Directory, "corrupt"), "snapshot-*"));
    }

    [Fact]
    public void ASnapshotThatDisagreesWithTheLogCheckpointIsNotTrusted()
    {
        var live = Start();
        Play(live, 3, snapshot: true);
        var older = live.Session.LastSeq;
        var olderFile = Snapshots(live).Last();
        Play(live, 3, snapshot: true);
        var newest = Snapshots(live).Last();
        // The newest file holds the older state: loadable, but its hash is not the log's checkpoint for that seq.
        File.Copy(Path.Combine(live.Directory, olderFile), Path.Combine(live.Directory, newest), overwrite: true);

        using var opened = Reopen(live);
        Assert.Equal(older, opened.Report.SnapshotSeq);
        Assert.Contains(opened.Report.Notes, it => it.Contains("does not match the log's checkpoint", StringComparison.Ordinal));
        Assert.Equal(Hash(live.Session.Game), Hash(opened.Session.Game));
    }

    [Fact]
    public void ASnapshotAheadOfTheLogIsSkipped()
    {
        var live = Start();
        Play(live, 3, snapshot: true);
        var earlier = live.Session.LastSeq;
        Play(live, 3, snapshot: true);
        live.Store.Dispose();
        var lines = File.ReadAllText(LogPath(live)).TrimEnd('\n').Split('\n');
        var keep = lines.TakeWhile(it => !it.StartsWith("{\"seq\":" + (earlier + 1) + ",", StringComparison.Ordinal)).ToList();
        File.WriteAllText(LogPath(live), string.Join('\n', keep) + "\n");

        using var opened = FileGameStore.Open(live.Directory);
        Assert.Equal(earlier, opened.Report.SnapshotSeq);
        Assert.Contains(opened.Report.Notes, it => it.Contains("ahead of the log", StringComparison.Ordinal));
    }

    [Fact]
    public void ACheckpointMismatchFallsBackToTheLastGoodSnapshotAndDropsTheTail()
    {
        var live = Start();
        Play(live, 4, snapshot: true);
        var snapshotSeq = live.Session.LastSeq;
        var atSnapshot = Hash(live.Session.Game);
        Play(live, 4);
        live.Store.Dispose();
        var lines = File.ReadAllText(LogPath(live)).TrimEnd('\n').Split('\n');
        var tamperedIndex = Array.FindIndex(lines, it => it.StartsWith("{\"checkpoint\":", StringComparison.Ordinal) && !it.StartsWith("{\"checkpoint\":" + snapshotSeq + ",", StringComparison.Ordinal)
            && JsonDocument.Parse(it).RootElement.GetProperty("checkpoint").GetInt64() > snapshotSeq);
        lines[tamperedIndex] = TamperHash(lines[tamperedIndex]);
        File.WriteAllText(LogPath(live), string.Join('\n', lines) + "\n");

        using var opened = FileGameStore.Open(live.Directory);
        Assert.Equal(RecoveryOutcome.FellBackToSnapshot, opened.Report.Outcome);
        Assert.Equal(snapshotSeq, opened.Report.SnapshotSeq);
        Assert.Equal(snapshotSeq, opened.Store.LastSeq);
        Assert.NotNull(opened.Report.DivergedAtSeq);
        Assert.Equal(live.Session.LastSeq - snapshotSeq, opened.Report.DiscardedCommands); // what the fallback threw away is reported, not silent
        Assert.Equal(atSnapshot, Hash(opened.Session.Game));
        Assert.Single(Directory.GetFiles(Path.Combine(live.Directory, "corrupt"), "diverged-after-*"));
        // A client that was ahead gets a fresh snapshot, and the game goes on from the snapshot.
        Assert.Equal(ResumeKind.Snapshot, opened.Store.Resume(live.Session.LastSeq).Kind);
        var result = opened.Session.Submit(Human, new Unciv.Commands.Turn.EndTurn());
        opened.Store.Sync(opened.Session);
        Assert.Equal(result.Accepted ? snapshotSeq + 1 : snapshotSeq, opened.Store.LastSeq);
    }

    [Fact]
    public void WithoutAnySnapshotTheGameIsRebuiltFromTheLogHeader()
    {
        var live = Start();
        Play(live, 6);
        live.Store.Dispose();
        foreach (var file in Directory.GetFiles(live.Directory, "snapshot-*")) File.Delete(file);

        using var opened = FileGameStore.Open(live.Directory);
        Assert.Equal(RecoveryOutcome.ReplayedFromStart, opened.Report.Outcome);
        Assert.Equal(Hash(live.Session.Game), Hash(opened.Session.Game));
        Assert.Equal(live.Session.LastSeq, opened.Report.ReplayedCommands);
    }

    [Fact]
    public void WhenNothingCanBeReplayedOpenFailsWithAStoreException()
    {
        var live = Start();
        Play(live, 4);
        live.Store.Dispose();
        foreach (var file in Directory.GetFiles(live.Directory, "snapshot-*")) File.Delete(file);
        var lines = File.ReadAllText(LogPath(live)).TrimEnd('\n').Split('\n');
        var index = Array.FindLastIndex(lines, it => it.StartsWith("{\"checkpoint\":", StringComparison.Ordinal));
        lines[index] = TamperHash(lines[index]);
        File.WriteAllText(LogPath(live), string.Join('\n', lines) + "\n");

        Assert.Throws<GameStoreException>(() => FileGameStore.Open(live.Directory));
    }

    [Fact]
    public void AMissingOrBrokenLogHeaderIsAStoreException()
    {
        var live = Start();
        live.Store.Dispose();
        File.WriteAllText(LogPath(live), "{broken\n");
        Assert.Throws<GameStoreException>(() => FileGameStore.Open(live.Directory));
        File.WriteAllText(LogPath(live), "no newline at all");
        Assert.Throws<GameStoreException>(() => FileGameStore.Open(live.Directory));
        File.WriteAllBytes(LogPath(live), [0xFF, 0xFE, (byte)'\n']);
        Assert.Throws<GameStoreException>(() => FileGameStore.Open(live.Directory));
    }

    // ---------------------------------------------------------------- shutdown, snapshots, retention

    [Fact]
    public void CleanShutdownSnapshotsAndWritesTheMarker()
    {
        var live = Start();
        Play(live, 4);
        live.Store.CloseCleanly(live.Session.Game);
        Assert.True(File.Exists(Path.Combine(live.Directory, "clean")));
        Assert.Contains(Snapshots(live), it => it.EndsWith(live.Session.LastSeq.ToString("D12", CultureInfo.InvariantCulture) + ".json.gz", StringComparison.Ordinal));
        Assert.Throws<ObjectDisposedException>(() => live.Store.Append(new CommandEnvelope(live.Session.LastSeq + 1, "x", "a", JsonDocument.Parse("{}").RootElement)));

        using var opened = FileGameStore.Open(live.Directory);
        Assert.True(opened.Report.WasCleanShutdown);
        Assert.Equal(0, opened.Report.ReplayedCommands);
        Assert.Equal(live.Session.LastSeq, opened.Report.SnapshotSeq);
        Assert.False(File.Exists(Path.Combine(live.Directory, "clean"))); // running again
        Assert.Equal(Hash(live.Session.Game), Hash(opened.Session.Game));
    }

    [Fact]
    public void ACleanMarkerForAnotherSeqIsNotTrusted()
    {
        var live = Start();
        Play(live, 3);
        live.Store.CloseCleanly(live.Session.Game);
        File.WriteAllText(Path.Combine(live.Directory, "clean"), "1");
        using var opened = FileGameStore.Open(live.Directory);
        Assert.False(opened.Report.WasCleanShutdown);
    }

    [Fact]
    public void NoSnapshotIsTakenWhileASimultaneousRoundIsResolving()
    {
        var live = Start();
        Play(live, 2);
        var game = live.Session.Game;
        game.TurnState = new TurnState { Mode = TurnMode.Simultaneous, Phase = SimultaneousPhase.Resolving };
        var before = Snapshots(live).ToList();
        Assert.False(FileGameStore.IsSafeToSnapshot(game));
        Assert.False(live.Store.TrySnapshot(game));
        Assert.False(live.Store.MaybeSnapshot(game));
        Assert.Equal(before, Snapshots(live));

        game.TurnState = new TurnState { Mode = TurnMode.Simultaneous, Phase = SimultaneousPhase.Playing, Ended = [Human] }; // a mid-turn state with an ended civ is fine
        Assert.True(FileGameStore.IsSafeToSnapshot(game));
        game.TurnState = null;
    }

    [Fact]
    public void CleanShutdownWhileResolvingWritesNoSnapshotButTheLogCarriesRecovery()
    {
        var live = Start();
        Play(live, 3);
        var game = live.Session.Game;
        var before = Snapshots(live).ToList();
        game.TurnState = new TurnState { Mode = TurnMode.Simultaneous, Phase = SimultaneousPhase.Resolving };
        live.Store.CloseCleanly(game);
        Assert.Equal(before, Snapshots(live));
        game.TurnState = null;
        using var opened = FileGameStore.Open(live.Directory);
        Assert.Equal(Hash(game), Hash(opened.Session.Game));
    }

    [Fact]
    public void SnapshotCadenceFollowsTheTurnCounter()
    {
        var live = Start(new StoreOptions { SnapshotEveryTurns = 3, KeepSnapshots = 10 });
        Play(live, 1);
        Assert.False(live.Store.MaybeSnapshot(live.Session.Game));
        Play(live, 2);
        Assert.True(live.Store.MaybeSnapshot(live.Session.Game));
        Assert.False(live.Store.MaybeSnapshot(live.Session.Game));
        Assert.Equal(2, Snapshots(live).Count());
    }

    [Fact]
    public void OldSnapshotsArePrunedToTheNewestFew()
    {
        var live = Start(new StoreOptions { KeepSnapshots = 2, RotateLogBytes = 0 });
        for (var i = 0; i < 4; i++) Play(live, 2, snapshot: true);
        Assert.Equal(2, Snapshots(live).Count());
        Assert.EndsWith(live.Session.LastSeq.ToString("D12", CultureInfo.InvariantCulture) + ".json.gz", Snapshots(live).Last(), StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(live.Directory, "history")));
    }

    [Fact]
    public void RotationMovesTheOldLogToHistoryAndTheGameStillRecovers()
    {
        var live = Start(new StoreOptions { RotateLogBytes = 1, KeepSnapshots = 5 });
        Play(live, 3, snapshot: true);
        var rotatedAt = live.Session.LastSeq;
        Play(live, 3);

        var history = Path.Combine(live.Directory, "history", "commands-" + rotatedAt.ToString("D12", CultureInfo.InvariantCulture) + ".log");
        Assert.True(File.Exists(history));
        Assert.Equal(rotatedAt, CommandLogReader.Read(File.ReadAllText(history)).Lines.OfType<CommandLogLine.Command>().Last().Envelope.Seq);
        Assert.Equal(new[] { rotatedAt }.Select(it => "snapshot-" + it.ToString("D12", CultureInfo.InvariantCulture) + ".json.gz"), Snapshots(live)); // older snapshots cannot replay forward any more
        Assert.Equal(rotatedAt + 1, CommandLogReader.Read(File.ReadAllText(LogPath(live))).Lines.OfType<CommandLogLine.Command>().First().Envelope.Seq);

        using var opened = Reopen(live);
        Assert.Equal(Hash(live.Session.Game), Hash(opened.Session.Game));
        Assert.Equal(live.Session.LastSeq, opened.Store.LastSeq);
        Assert.Equal(RecoveryOutcome.Recovered, opened.Report.Outcome);
    }

    [Fact]
    public void ARotatedLogWithoutSnapshotsCannotBeRebuilt()
    {
        var live = Start(new StoreOptions { RotateLogBytes = 1 });
        Play(live, 3, snapshot: true);
        live.Store.Dispose();
        foreach (var file in Directory.GetFiles(live.Directory, "snapshot-*")) File.Delete(file);
        Assert.Throws<GameStoreException>(() => FileGameStore.Open(live.Directory));
    }

    // ---------------------------------------------------------------- dedup, resume, secrets, files

    private static CommandEnvelope Envelope(long seq, string actor = "a") => new(seq, "test-command", actor, JsonDocument.Parse("{\"x\":" + seq + "}").RootElement);

    private FileGameStore NewBareStore(StoreOptions? options = null)
    {
        var setup = Setup();
        return FileGameStore.Create(Path.Combine(root, "bare" + sequence++), "bare", CommandLogHeader.Create(setup, 1L, 1L), new GameMeta(), null, options);
    }

    [Fact]
    public void RecoveryRebuildsTheDedupTableAndTheResumeRingFromTheLog()
    {
        var live = Start();
        Play(live, 3, snapshot: true, withIds: true);
        var last = live.Session.Log[^1];
        Assert.Equal(new LoggedOutcome(last.Seq), live.Store.FindOutcome(last.ActorId, "cmd-" + last.Seq));
        Assert.Null(live.Store.FindOutcome("someone", "cmd-" + last.Seq));

        using var opened = Reopen(live);
        Assert.Equal(new LoggedOutcome(last.Seq), opened.Store.FindOutcome(last.ActorId, "cmd-" + last.Seq));
        Assert.Null(opened.Store.FindOutcome(last.ActorId, "cmd-other"));
        Assert.Equal(ResumeKind.Deltas, opened.Store.Resume(last.Seq - 2).Kind);
        Assert.Equal(new[] { last.Seq - 1, last.Seq }, opened.Store.Resume(last.Seq - 2).Commands.Select(it => it.Seq));
    }

    [Fact]
    public void TheDedupTableForgetsTheOldestIdsBeyondItsSize()
    {
        using var store = NewBareStore(new StoreOptions { DedupSize = 2 });
        for (var i = 1; i <= 3; i++) store.Append(Envelope(i), "c" + i);
        Assert.Null(store.FindOutcome("a", "c1"));
        Assert.Equal(new LoggedOutcome(2), store.FindOutcome("a", "c2"));
        Assert.Equal(new LoggedOutcome(3), store.FindOutcome("a", "c3"));
        store.Append(Envelope(4), "c3"); // a repeated id keeps its first seq
        Assert.Equal(new LoggedOutcome(3), store.FindOutcome("a", "c3"));
    }

    [Fact]
    public void ResumeSendsDeltasFromTheRingOrASnapshot()
    {
        using var store = NewBareStore(new StoreOptions { RingSize = 3 });
        Assert.Equal(ResumeKind.UpToDate, store.Resume(0).Kind);
        for (var i = 1; i <= 5; i++) store.Append(Envelope(i));

        Assert.Equal(ResumeKind.UpToDate, store.Resume(5).Kind);
        var deltas = store.Resume(3);
        Assert.Equal(ResumeKind.Deltas, deltas.Kind);
        Assert.Equal(new long[] { 4, 5 }, deltas.Commands.Select(it => it.Seq));
        Assert.Equal(new long[] { 3, 4, 5 }, store.Resume(2).Commands.Select(it => it.Seq));
        Assert.Equal(ResumeKind.Snapshot, store.Resume(1).Kind); // seq 2 left the ring
        Assert.Equal(ResumeKind.Snapshot, store.Resume(6).Kind); // a client ahead of the host (after a rollback)
        Assert.Equal(ResumeKind.Snapshot, store.Resume(-1).Kind);
    }

    [Fact]
    public void ResumeOnAnEmptyLogIsUpToDate()
    {
        using var store = NewBareStore();
        Assert.Equal(ResumeKind.UpToDate, store.Resume(0).Kind);
        Assert.Equal(ResumeKind.Snapshot, store.Resume(3).Kind);
    }

    [Fact]
    public void AppendInTheWrongOrderIsRefused()
    {
        using var store = NewBareStore();
        Assert.Throws<GameStoreException>(() => store.Append(Envelope(2)));
        store.Append(Envelope(1));
        Assert.Throws<GameStoreException>(() => store.Append(Envelope(1)));
        Assert.Throws<GameStoreException>(() => store.AppendCheckpoint(7, 1, 1UL));
        store.AppendCheckpoint(1, 1, 1UL);
        store.AppendCheckpoint(1, 1, 1UL); // writing the same checkpoint again changes nothing
        Assert.Equal(1, store.LastSeq);
    }

    [Fact]
    public void CreateRefusesADirectoryThatAlreadyHoldsAGame()
    {
        var live = Start();
        live.Store.Dispose();
        Assert.Throws<GameStoreException>(() => FileGameStore.Create(live.Directory, "g1", CommandLogHeader.Create(Setup(), 1L, 1L), new GameMeta()));
    }

    [Fact]
    public void MetaRoundTripsAndTheStoreKeepsTheHostSecretItself()
    {
        var live = Start();
        Play(live, 4, snapshot: true);
        Assert.Equal(Secret, live.Store.HostSecret); // taken from the log header at Create
        var meta = new GameMeta
        {
            LobbySettings = JsonDocument.Parse("{\"maxPlayers\":4}").RootElement,
            Slots = new SortedDictionary<string, SlotBinding>(StringComparer.Ordinal) { ["Babylon"] = new() { PlayerKey = "KEY-1", DisplayName = "Ann" }, ["Greece"] = new() { PlayerKey = "KEY-2", DisplayName = "Bob" } },
            HostFingerprint = "AOIFR-RXSYD",
            PasswordHash = "hash",
            TurnPolicy = "Simultaneous",
            HostSecret = 77, // the lobby layer cannot change it
        };
        live.Store.WriteMeta(meta with { GameId = "ignored" });
        var read = live.Store.ReadMeta();
        Assert.Equal("g1", read.GameId);
        Assert.Equal("KEY-2", read.Slots["Greece"].PlayerKey);
        Assert.Equal(4, read.LobbySettings!.Value.GetProperty("maxPlayers").GetInt32());
        Assert.Equal("hash", read.PasswordHash);
        Assert.Equal(Secret, read.HostSecret);

        live.Store.HostSecret = 123;
        Assert.Equal(123, live.Store.HostSecret);
        Assert.Equal("KEY-1", live.Store.ReadMeta().Slots["Babylon"].PlayerKey); // setting it keeps the rest
    }

    // T230: a saved game does not carry the secret, so every way back to a running game has to install it before the first command.
    [Fact]
    public void EveryRecoveryPathInstallsTheHostSecretAndTheSnapshotsDoNotHoldIt()
    {
        var live = Start();
        Play(live, 4, snapshot: true);
        Assert.Equal(0, UncivFiles.GameInfoFromString(File.ReadAllText(Path.Combine(live.Directory, Snapshots(live).Last()))).RandomSalt);
        Play(live, 2);
        using (var recovered = Reopen(live))
        {
            Assert.Equal(RecoveryOutcome.Recovered, recovered.Report.Outcome);
            Assert.Equal(Secret, recovered.Session.Game.RandomSalt);
        }

        foreach (var file in Directory.GetFiles(live.Directory, "snapshot-*")) File.Delete(file);
        using (var rebuilt = FileGameStore.Open(live.Directory))
        {
            Assert.Equal(RecoveryOutcome.ReplayedFromStart, rebuilt.Report.Outcome);
            Assert.Equal(Secret, rebuilt.Session.Game.RandomSalt);
            Assert.Equal(Hash(live.Session.Game), Hash(rebuilt.Session.Game));
        }
    }

    [Fact]
    public void ResumeAfterCrashContinuesIdenticallyWithTheSecretAndTheFallbackInstallsItToo()
    {
        var live = Start();
        Play(live, 4, snapshot: true);
        var snapshotSeq = live.Session.LastSeq;
        Play(live, 3);
        using (var opened = Reopen(live))
        {
            var clock = ScriptedHuman.DeterministicClock(2000L);
            for (var i = live.Session.Log.Count(it => it.Payload.TryGetProperty("now", out _)); i > 0; i--) clock();
            var continued = new GameSession(opened.Session.Game, lastSeq: opened.Store.LastSeq) { Clock = clock };
            Assert.Equal(Secret, continued.Game.RandomSalt);
            Assert.Equal(8, live.Player.PlayTurns(8));
            Assert.Equal(8, new ScriptedHuman(continued, Human).PlayTurns(8));
            Assert.Equal(Hash(live.Session.Game), Hash(continued.Game));
        }

        var lines = File.ReadAllText(LogPath(live)).TrimEnd('\n').Split('\n');
        var index = Array.FindLastIndex(lines, it => it.StartsWith("{\"checkpoint\":", StringComparison.Ordinal));
        Assert.True(JsonDocument.Parse(lines[index]).RootElement.GetProperty("checkpoint").GetInt64() > snapshotSeq);
        lines[index] = TamperHash(lines[index]);
        File.WriteAllText(LogPath(live), string.Join('\n', lines) + "\n");
        using var fallback = FileGameStore.Open(live.Directory);
        Assert.Equal(RecoveryOutcome.FellBackToSnapshot, fallback.Report.Outcome);
        Assert.Equal(Secret, fallback.Session.Game.RandomSalt);
    }

    [Fact]
    public void TheDirectoryIsPortableAndKeepsTheBindings()
    {
        var live = Start();
        Play(live, 4, snapshot: true);
        live.Store.WriteMeta(new GameMeta { Slots = new SortedDictionary<string, SlotBinding>(StringComparer.Ordinal) { ["Babylon"] = new() { PlayerKey = "KEY-1", DisplayName = "Ann" } } });
        live.Store.HostSecret = 42;
        live.Store.Dispose();
        var moved = Path.Combine(root, "elsewhere", "g1-copy");
        Directory.CreateDirectory(moved);
        foreach (var file in Directory.GetFiles(live.Directory)) File.Copy(file, Path.Combine(moved, Path.GetFileName(file)));

        using var opened = FileGameStore.Open(moved);
        Assert.Equal("KEY-1", opened.Store.ReadMeta().Slots["Babylon"].PlayerKey);
        Assert.Equal("g1", opened.Store.GameId);
        Assert.Equal(Hash(live.Session.Game), Hash(opened.Session.Game));
        Assert.Equal(42, opened.Store.HostSecret);
        Assert.Equal(42, opened.Session.Game.RandomSalt);
    }

    [Fact]
    public void AppendMakesTheLineVisibleToAnotherReaderImmediately()
    {
        using var store = NewBareStore();
        store.Append(Envelope(1), "c1");
        var path = Path.Combine(store.DirectoryPath, "commands.log");
        using var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var text = new StreamReader(reader).ReadToEnd();
        Assert.EndsWith("\"clientCmdId\":\"c1\"}\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void SyncAppendsOnlyWhatTheLogDoesNotHold()
    {
        var live = Start();
        Play(live, 2);
        var held = live.Store.LastSeq;
        Assert.Equal(1, live.Player.PlayTurns(1)); // played, not yet in the store
        Assert.True(live.Session.LastSeq > held);
        live.Store.Sync(live.Session);
        Assert.Equal(live.Session.LastSeq, live.Store.LastSeq);
        live.Store.Sync(live.Session);
        Assert.Equal(live.Session.LastSeq, live.Store.LastSeq);
    }

    [Fact]
    public void AStoreThatNeverSnapshottedSnapshotsAtTheFirstChance()
    {
        using var store = NewBareStore();
        var game = GameStarter.StartNewGame(Setup(), 9L, 1000L);
        Assert.True(store.MaybeSnapshot(game));
        Assert.False(store.MaybeSnapshot(game));
    }

    [Fact]
    public void ACheckpointLineWithAnImpossibleHashIsJustACorruptTail()
    {
        var live = Start();
        Play(live, 3, snapshot: true);
        live.Store.Dispose();
        File.AppendAllText(LogPath(live), "{\"checkpoint\":" + live.Session.LastSeq + ",\"turn\":1,\"fullHash\":\"fffffffffffffffff\"}\n");
        using var opened = FileGameStore.Open(live.Directory);
        Assert.Equal(1, opened.Report.DroppedLogLines);
        Assert.Equal(Hash(live.Session.Game), Hash(opened.Session.Game));
    }

    [Fact]
    public void ACleanMarkerIsNotTrustedWhenTheLogHasAnUnreadableTail()
    {
        var live = Start();
        Play(live, 3);
        live.Store.CloseCleanly(live.Session.Game);
        File.AppendAllText(LogPath(live), "garbage\n");
        using var opened = FileGameStore.Open(live.Directory);
        Assert.False(opened.Report.WasCleanShutdown);
        Assert.Equal(1, opened.Report.DroppedLogLines);
    }

    [Fact]
    public void ASnapshotOlderThanTheRotatedLogIsNeverUsed()
    {
        var live = Start(new StoreOptions { RotateLogBytes = 1 });
        var stale = File.ReadAllBytes(Path.Combine(live.Directory, Snapshots(live).Single()));
        var staleName = Snapshots(live).Single();
        Play(live, 3, snapshot: true);
        live.Store.Dispose();
        File.WriteAllBytes(Path.Combine(live.Directory, staleName), stale); // comes back, e.g. restored from a backup
        File.WriteAllText(Path.Combine(live.Directory, Snapshots(live).Last()), "broken"); // and the current one is lost

        var exception = Assert.Throws<GameStoreException>(() => FileGameStore.Open(live.Directory));
        Assert.Contains("rotated", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSnapshotCadenceContinuesFromTheRecoveredSnapshot()
    {
        var live = Start(new StoreOptions { SnapshotEveryTurns = 5 });
        Play(live, 3, snapshot: true);
        using var opened = Reopen(live, new StoreOptions { SnapshotEveryTurns = 5 });
        Assert.False(opened.Store.MaybeSnapshot(opened.Session.Game));
    }

    // ------------------------------------------------------------------ T176 review: crashes at every offset, hostile files, concurrency

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from)) File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
    }

    // The seq of the last complete, parseable line in the bytes (command or checkpoint), -1 when none: an independent reading of what survived a cut.
    private static long SurvivingSeq(byte[] bytes, int headerLength)
    {
        var text = Encoding.UTF8.GetString(bytes[headerLength..]);
        var complete = text[..(text.LastIndexOf('\n') + 1)];
        long last = -1;
        foreach (var line in complete.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            using var document = JsonDocument.Parse(line);
            last = document.RootElement.TryGetProperty("seq", out var seq) ? seq.GetInt64() : document.RootElement.GetProperty("checkpoint").GetInt64();
        }
        return last;
    }

    [Fact]
    public void ACrashAtAnyByteOffsetOfTheLogRecoversTheLongestVerifiedPrefix()
    {
        var live = Start();
        Play(live, 4, snapshot: true);
        Play(live, 5);
        live.Store.Dispose();
        var full = File.ReadAllBytes(LogPath(live));
        var headerLength = Array.IndexOf(full, (byte)'\n') + 1;
        var lineStarts = new List<int>();
        for (var i = headerLength; i < full.Length; i++)
            if (i == headerLength || full[i - 1] == (byte)'\n') lineStarts.Add(i);

        var offsets = new SortedSet<int>();
        for (var i = 0; i <= headerLength; i += 41) offsets.Add(i);
        offsets.Add(headerLength - 1);
        offsets.Add(headerLength);
        foreach (var start in lineStarts)
            foreach (var delta in new[] { -2, -1, 0, 1, 2 })
                if (start + delta >= headerLength && start + delta <= full.Length) offsets.Add(start + delta);
        for (var i = headerLength; i < full.Length; i += 53) offsets.Add(i);
        offsets.Add(full.Length);

        var referenceHashes = new Dictionary<long, ulong>();
        var work = Path.Combine(root, "crash");
        foreach (var offset in offsets)
        {
            if (Directory.Exists(work)) Directory.Delete(work, true);
            CopyDirectory(live.Directory, work);
            File.WriteAllBytes(Path.Combine(work, "commands.log"), full[..offset]);

            if (offset <= headerLength - 1)
            {
                // The header is written atomically with the file, so a log without it is not a crash state: it must still fail cleanly.
                Assert.Throws<GameStoreException>(() => FileGameStore.Open(work));
                continue;
            }

            using var opened = FileGameStore.Open(work);
            var expected = System.Math.Max(0, SurvivingSeq(full[..offset], headerLength));
            Assert.Equal(expected, opened.Store.LastSeq);
            Assert.Equal(RecoveryOutcome.Recovered, opened.Report.Outcome);
            if (!referenceHashes.TryGetValue(expected, out var reference))
            {
                // Independent of snapshots: replay the surviving lines from the header.
                var prefix = Encoding.UTF8.GetString(full[..offset]);
                prefix = prefix[..(prefix.LastIndexOf('\n') + 1)];
                reference = ReplayVerifier.Verify(CommandLogReader.Read(prefix)).FinalHash;
                referenceHashes[expected] = reference;
            }
            Assert.Equal(reference, Hash(opened.Session.Game));
            Assert.True(File.ReadAllBytes(Path.Combine(work, "commands.log")).Length <= offset);
        }
        Assert.True(offsets.Count > 60, "the survey must be wide: " + offsets.Count);
    }

    [Fact]
    public void ACrashDuringACheckpointOrSnapshotWriteAtAnyOffsetNeverLosesTheGame()
    {
        var live = Start();
        Play(live, 4, snapshot: true);
        Play(live, 3, snapshot: true);
        live.Store.Dispose();
        var finalHash = Hash(live.Session.Game);
        var work = Path.Combine(root, "hostile");
        foreach (var name in Directory.GetFiles(live.Directory, "snapshot-*").Select(Path.GetFileName).OfType<string>())
        {
            var original = File.ReadAllBytes(Path.Combine(live.Directory, name));
            var offsets = Enumerable.Range(0, 30).Select(i => i * (original.Length - 1) / 29).Concat([0, 1, original.Length - 1]).Distinct().ToList();
            foreach (var offset in offsets)
            {
                foreach (var mode in new[] { "truncate", "flip", "zero-tail" })
                {
                    if (Directory.Exists(work)) Directory.Delete(work, true);
                    CopyDirectory(live.Directory, work);
                    var damaged = (byte[])original.Clone();
                    var bytes = mode switch
                    {
                        "truncate" => damaged[..offset],
                        "flip" => Flip(damaged, offset),
                        _ => ZeroTail(damaged, offset),
                    };
                    File.WriteAllBytes(Path.Combine(work, name), bytes);

                    using var opened = FileGameStore.Open(work);
                    Assert.Equal(finalHash, Hash(opened.Session.Game));
                    Assert.Equal(live.Session.LastSeq, opened.Store.LastSeq);
                }
            }
        }

        static byte[] Flip(byte[] bytes, int at)
        {
            bytes[at] ^= 0x55;
            return bytes;
        }

        static byte[] ZeroTail(byte[] bytes, int at)
        {
            Array.Clear(bytes, at, bytes.Length - at);
            return bytes;
        }
    }

    [Fact]
    public void ACorruptOrMissingGameJsonOrAJunkCleanMarkerFailsCleanlyOrIsIgnored()
    {
        var live = Start();
        Play(live, 3, snapshot: true);
        live.Store.Dispose();
        var meta = File.ReadAllBytes(Path.Combine(live.Directory, "game.json"));
        var work = Path.Combine(root, "meta");
        for (var offset = 0; offset < meta.Length; offset += 11)
        {
            if (Directory.Exists(work)) Directory.Delete(work, true);
            CopyDirectory(live.Directory, work);
            File.WriteAllBytes(Path.Combine(work, "game.json"), meta[..offset]);
            Assert.Throws<GameStoreException>(() => FileGameStore.Open(work));
        }
        File.Delete(Path.Combine(work, "game.json"));
        Assert.Throws<GameStoreException>(() => FileGameStore.Open(work));

        CopyDirectory(live.Directory, work + "2");
        File.WriteAllBytes(Path.Combine(work + "2", "clean"), [0xFF, 0xFE, 0, 1]);
        using var opened = FileGameStore.Open(work + "2");
        Assert.False(opened.Report.WasCleanShutdown);
        Assert.Equal(Hash(live.Session.Game), Hash(opened.Session.Game));
    }

    [Fact]
    public void ACrashBetweenCreatingTheMetaAndTheLogLeavesADirectoryCreateAcceptsAgain()
    {
        var live = Start();
        live.Store.Dispose();
        File.Delete(Path.Combine(live.Directory, "commands.log")); // the log is the last thing Create writes
        Assert.Throws<GameStoreException>(() => FileGameStore.Open(live.Directory)); // not a game yet, and says so cleanly
        var setup = Setup();
        using var again = FileGameStore.Create(live.Directory, "g1", CommandLogHeader.Create(setup, 9L, 1000L, null, Secret), new GameMeta());
        Assert.Equal(0, again.LastSeq);
    }

    [Fact]
    public void StoreFilesAreOwnerOnlyBecauseGameJsonAndTheLogHeaderHoldTheHostSecret()
    {
        if (OperatingSystem.IsWindows()) return;
        var live = Start(new StoreOptions { RotateLogBytes = 1 });
        Play(live, 3, snapshot: true); // rotates: history/ holds a copy of the header too
        File.AppendAllText(LogPath(live), "garbage\n");
        live.Store.Dispose();
        using var opened = FileGameStore.Open(live.Directory); // creates corrupt/
        const UnixFileMode fileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        const UnixFileMode directoryMode = fileMode | UnixFileMode.UserExecute;
        foreach (var file in Directory.GetFiles(live.Directory, "*", SearchOption.AllDirectories))
            Assert.True(File.GetUnixFileMode(file) == fileMode, file + " is " + File.GetUnixFileMode(file));
        Assert.Equal(directoryMode, File.GetUnixFileMode(live.Directory));
        foreach (var dir in Directory.GetDirectories(live.Directory)) Assert.Equal(directoryMode, File.GetUnixFileMode(dir));
        Assert.Contains(Directory.GetDirectories(live.Directory), it => it.EndsWith("history", StringComparison.Ordinal));
        Assert.Contains(Directory.GetDirectories(live.Directory), it => it.EndsWith("corrupt", StringComparison.Ordinal));
        opened.Store.HostSecret = Secret; // rewrites game.json: the mode survives
        Assert.Equal(fileMode, File.GetUnixFileMode(Path.Combine(live.Directory, "game.json")));
    }

    // A stream that fails the way a full disk does: part of the write lands, then IOException.
    private sealed class FlakyStream(Stream inner) : Stream
    {
        public bool FailNextWrite { get; set; }

        public bool FailSetLength { get; set; }

        public override bool CanRead => false;

        public override bool CanSeek => true;

        public override bool CanWrite => true;

        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

        public override void SetLength(long value)
        {
            if (FailSetLength) throw new IOException("cannot truncate");
            inner.SetLength(value);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (!FailNextWrite)
            {
                inner.Write(buffer, offset, count);
                return;
            }
            FailNextWrite = false;
            inner.Write(buffer, offset, count / 2);
            inner.Flush();
            throw new IOException("No space left on device");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }

    // A store that holds all but the last command of a three-turn game, with its log wrapped so a write can fail.
    private (FileGameStore Store, FlakyStream Flaky, CommandEnvelope Next, string Path, Live Live) StoreMissingTheLastCommand()
    {
        var live = Start();
        Assert.Equal(3, live.Player.PlayTurns(3));
        var commands = CommandLogReader.Read(live.Text.ToString()).Lines.OfType<CommandLogLine.Command>().Select(it => it.Envelope).ToList();
        foreach (var envelope in commands.SkipLast(1)) live.Store.Append(envelope);
        FlakyStream? flaky = null;
        live.Store.WrapLog(it => flaky = new FlakyStream(it));
        return (live.Store, flaky!, commands[^1], LogPath(live), live);
    }

    [Fact]
    public void AFailedAppendSurfacesAsAStoreExceptionAndLeavesNoHalfLineBehind()
    {
        var (store, flaky, next, path, live) = StoreMissingTheLastCommand();
        var lengthBefore = new FileInfo(path).Length;
        flaky.FailNextWrite = true;
        Assert.Throws<GameStoreException>(() => store.Append(next));
        Assert.Equal(lengthBefore, new FileInfo(path).Length); // the torn half is gone
        Assert.Equal(next.Seq - 1, store.LastSeq);
        store.Append(next, "retry-1"); // the log is still usable, and the retry is what the client does
        Assert.Equal(next.Seq, store.LastSeq);
        store.Dispose();
        using var opened = FileGameStore.Open(live.Directory);
        Assert.Equal(next.Seq, opened.Store.LastSeq);
        Assert.Equal(0, opened.Report.DroppedLogLines);
        Assert.Equal(next.Seq, opened.Store.FindOutcome(next.ActorId, "retry-1")?.Seq);
    }

    [Fact]
    public void WhenTheHalfLineCannotBeUndoneTheStoreRefusesFurtherAppendsAndRecoveryCutsItOff()
    {
        var (store, flaky, next, path, live) = StoreMissingTheLastCommand();
        flaky.FailNextWrite = true;
        flaky.FailSetLength = true;
        Assert.Throws<GameStoreException>(() => store.Append(next));
        flaky.FailSetLength = false;
        var refused = Assert.Throws<GameStoreException>(() => store.Append(next));
        Assert.Contains("reopen", refused.Message, StringComparison.Ordinal);
        store.Dispose();
        Assert.False(File.ReadAllText(path).EndsWith('\n') && File.ReadAllText(path).Split('\n')[^2].Length < 5);
        using var opened = FileGameStore.Open(live.Directory);
        Assert.Equal(next.Seq - 1, opened.Store.LastSeq);
        Assert.Equal(1, opened.Report.DroppedLogLines);
    }

    [Fact]
    public void ASnapshotThatRecoveryRejectedIsSetAsideSoItCannotBeTakenForTheNewHistory()
    {
        var live = Start(new StoreOptions { KeepSnapshots = 2 });
        Play(live, 3, snapshot: true);
        var older = live.Session.LastSeq;
        Play(live, 3, snapshot: true);
        var newest = live.Session.LastSeq;
        live.Store.Dispose();
        // The log loses its end (a restored backup, a cut from bit rot) while the newest snapshot survives: that snapshot is ahead of the log.
        var lines = File.ReadAllText(LogPath(live)).TrimEnd('\n').Split('\n');
        var keep = Array.FindLastIndex(lines, it => it.StartsWith("{\"seq\":" + (older + 1) + ",", StringComparison.Ordinal));
        File.WriteAllText(LogPath(live), string.Join('\n', lines[..(keep + 1)]) + "\n");

        using var opened = FileGameStore.Open(live.Directory, new StoreOptions { KeepSnapshots = 2 });
        Assert.Equal(older, opened.Report.SnapshotSeq);
        Assert.DoesNotContain(Snapshots(live), it => it.Contains(newest.ToString("D12", CultureInfo.InvariantCulture), StringComparison.Ordinal));
        Assert.Single(Directory.GetFiles(Path.Combine(live.Directory, "corrupt"), "rejected-snapshot-*.json.gz"));
        Assert.Contains(opened.Report.Notes, it => it.Contains("Set aside rejected snapshot", StringComparison.Ordinal));
        // The set-aside file is a real snapshot, not lost data: it still loads.
        Assert.NotNull(UncivFiles.GameInfoFromString(File.ReadAllText(Directory.GetFiles(Path.Combine(live.Directory, "corrupt"), "rejected-snapshot-*").Single())));
    }

    [Fact]
    public void ACorruptTailIsNeverOverwrittenByALaterOneAfterTheSameSeq()
    {
        var live = Start();
        Play(live, 3, snapshot: true);
        live.Store.Dispose();
        File.AppendAllText(LogPath(live), "first-garbage");
        FileGameStore.Open(live.Directory).Dispose();
        File.AppendAllText(LogPath(live), "second-garbage");
        FileGameStore.Open(live.Directory).Dispose();
        var kept = Directory.GetFiles(Path.Combine(live.Directory, "corrupt")).Select(File.ReadAllText).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(["first-garbage", "second-garbage"], kept);
    }

    [Fact]
    public void AGameIdIsOnlyEverRecordedNeverUsedAsAPathSoTraversalNamesStayInsideTheDirectory()
    {
        foreach (var id in new[] { "../escape", "/etc/passwd", "..\\..\\x", "CON", "a/../../b" })
        {
            var dir = Path.Combine(root, "id" + sequence++);
            var before = Directory.GetFileSystemEntries(root).Length;
            using var store = FileGameStore.Create(dir, id, CommandLogHeader.Create(Setup(), 9L, 1000L, null, Secret), new GameMeta());
            Assert.Equal(id, store.ReadMeta().GameId);
            Assert.Equal(before + 1, Directory.GetFileSystemEntries(root).Length); // only the game directory appeared
            using var opened = FileGameStore.Open(dir);
            Assert.Equal(id, opened.Store.GameId);
        }
    }

    [Fact]
    public void ConcurrentAppendsReadersAndMetaWritesStayConsistent()
    {
        var live = Start();
        Assert.Equal(6, live.Player.PlayTurns(6));
        var lines = CommandLogReader.Read(live.Text.ToString()).Lines;
        live.Store.Dispose();
        var dir = Path.Combine(root, "concurrent");
        using var store = FileGameStore.Create(dir, "g1", CommandLogHeader.Create(Setup(), 9L, 1000L, null, Secret), new GameMeta());
        var done = false;
        var failures = new System.Collections.Concurrent.ConcurrentBag<Exception>();
        var readers = Enumerable.Range(0, 4).Select(index => new Thread(() =>
        {
            try
            {
                while (!Volatile.Read(ref done))
                {
                    var last = store.LastSeq;
                    var plan = store.Resume(System.Math.Max(0, last - 3));
                    if (plan.Kind == ResumeKind.Deltas) Assert.Equal(Enumerable.Range((int)System.Math.Max(0, last - 3) + 1, plan.Commands.Count).Select(it => (long)it), plan.Commands.Select(it => it.Seq));
                    _ = store.FindOutcome(Human, "cmd-1");
                    if (index == 0) store.WriteMeta(new GameMeta { HostFingerprint = "fp" + last });
                    if (index == 1) store.HostSecret = Secret;
                    Assert.Equal(Secret, store.ReadMeta().HostSecret);
                }
            }
            catch (Exception e)
            {
                failures.Add(e);
            }
        })).ToList();
        foreach (var thread in readers) thread.Start();
        foreach (var line in lines)
        {
            if (line is CommandLogLine.Command command) store.Append(command.Envelope, "cmd-" + command.Envelope.Seq);
            else if (line is CommandLogLine.Checkpoint checkpoint) store.AppendCheckpoint(checkpoint.Seq, checkpoint.Turn, checkpoint.FullHash);
        }
        Volatile.Write(ref done, true);
        foreach (var thread in readers) thread.Join();
        Assert.Empty(failures);
        store.Dispose();
        using var opened = FileGameStore.Open(dir);
        Assert.Equal(Hash(live.Session.Game), Hash(opened.Session.Game));
        Assert.Equal(0, opened.Report.DroppedLogLines);
    }

    [Fact]
    public void ARealSimultaneousGameSnapshotsMidRoundButNotWhileResolvingAndComesBackIdentical()
    {
        var setup = new GameSetupInfo(
            new GameParameters
            {
                BaseRuleset = ruleset.Name,
                NumberOfCityStates = 1,
                VictoryTypes = ruleset.Victories.Keys.ToList(),
                Players = [new Player(Human, PlayerType.Human), new Player("Greece", PlayerType.Human), new Player("Rome", PlayerType.AI)],
            },
            new MapParameters { Type = MapType.Pangaea, Shape = MapShape.Rectangular, Seed = 5, MapSize = MapSize.Tiny });
        var game = HostSecret.ApplyTo(GameStarter.StartNewGame(setup, 9L, 1000L), Secret);
        game.StartSimultaneousTurns();
        using var store = FileGameStore.Create(Path.Combine(root, "sim"), "g1", CommandLogHeader.Create(setup, 9L, 1000L, null, Secret), new GameMeta()); // no start snapshot: the first one is taken mid-round

        game.EndHumanTurn(game.GetCivilization(Human)); // one human ended, the other still plays
        Assert.True(store.TrySnapshot(game));
        store.Dispose();
        using (var opened = FileGameStore.Open(Path.Combine(root, "sim")))
        {
            Assert.Equal(Hash(game), Hash(opened.Session.Game));
            Assert.Equal([Human], opened.Session.Game.TurnState!.Ended);
            Assert.Equal(Secret, opened.Session.Game.RandomSalt);
        }

        game.EndHumanTurn(game.GetCivilization("Greece"));
        game.BeginResolving();
        using var again = FileGameStore.Open(Path.Combine(root, "sim"));
        Assert.False(again.Store.TrySnapshot(game)); // Resolving: refused, nothing written
        Assert.Single(Directory.GetFiles(Path.Combine(root, "sim"), "snapshot-*"));
    }

    [Fact]
    public void ASnapshotThatDisagreesWithTheCheckpointTheLogAlreadyHoldsIsRefusedNotWrittenPoisoned()
    {
        var live = Start();
        Play(live, 2, snapshot: true);
        var before = File.ReadAllBytes(LogPath(live));
        var snapshotsBefore = Snapshots(live).ToList();
        live.Session.Game.Turns += 1; // the game moved without a logged command
        var exception = Assert.Throws<GameStoreException>(() => live.Store.TrySnapshot(live.Session.Game));
        Assert.Contains("without a logged command", exception.Message, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(LogPath(live)));
        Assert.Equal(snapshotsBefore, Snapshots(live));
        live.Session.Game.Turns -= 1;
        Assert.True(live.Store.TrySnapshot(live.Session.Game)); // the same state again is fine (idempotent)
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-4)]
    public void RetentionNeverDeletesTheOnlyGoodSnapshotWhateverTheOptionsSay(int keep)
    {
        var live = Start(new StoreOptions { KeepSnapshots = keep });
        Play(live, 2, snapshot: true);
        Play(live, 2, snapshot: true);
        var newest = live.Session.LastSeq;
        Assert.Equal(["snapshot-" + newest.ToString("D12", CultureInfo.InvariantCulture) + ".json.gz"], Snapshots(live));
        using var opened = Reopen(live);
        Assert.Equal(Hash(live.Session.Game), Hash(opened.Session.Game));
    }

    [Fact]
    public void ALogLineThatSkipsASeqEndsTheValidLogEvenWhenLaterLinesFollow()
    {
        var live = Start();
        Play(live, 3, snapshot: true);
        Play(live, 3);
        live.Store.Dispose();
        var lines = File.ReadAllText(LogPath(live)).TrimEnd('\n').Split('\n').ToList();
        var index = lines.FindLastIndex(it => it.StartsWith("{\"seq\":", StringComparison.Ordinal));
        while (!lines[index - 1].StartsWith("{\"seq\":", StringComparison.Ordinal)) index = lines.FindLastIndex(index - 1, it => it.StartsWith("{\"seq\":", StringComparison.Ordinal));
        var lostSeq = JsonDocument.Parse(lines[index - 1]).RootElement.GetProperty("seq").GetInt64();
        lines.RemoveAt(index - 1); // two commands in a row: take the first away, the second now skips a seq
        File.WriteAllText(LogPath(live), string.Join('\n', lines) + "\n");

        using var opened = FileGameStore.Open(live.Directory);
        Assert.Equal(lostSeq - 1, opened.Store.LastSeq);
        Assert.True(opened.Report.DroppedLogLines >= 1);
    }

    [Fact]
    public void TheHostSecretComesFromTheLogHeaderWhenGameJsonHoldsNone()
    {
        var live = Start();
        Play(live, 3, snapshot: true);
        live.Store.Dispose();
        var metaPath = Path.Combine(live.Directory, "game.json");
        File.WriteAllText(metaPath, Unciv.Json.UncivJson.Serialize(Unciv.Json.UncivJson.Deserialize<GameMeta>(File.ReadAllText(metaPath))! with { HostSecret = 0 }));
        using var opened = FileGameStore.Open(live.Directory);
        Assert.Equal(Secret, opened.Session.Game.RandomSalt);
        Assert.Equal(Hash(live.Session.Game), Hash(opened.Session.Game));
    }
}
