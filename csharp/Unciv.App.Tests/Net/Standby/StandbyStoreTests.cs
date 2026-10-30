using System.Text;
using System.Text.Json;
using Unciv.Commands;
using Unciv.Commands.Log;
using Unciv.Json;
using Unciv.Net;
using Unciv.Net.Persistence;
using Unciv.Net.Standby;

namespace Unciv.Core.Tests.Net.Standby;

/// <summary>T272: the successor's store validates every frame before it stores it (design/HOST-MIGRATION.md section 1).</summary>
public sealed class StandbyStoreTests : IDisposable
{
    private const string GameId = "game-1";
    private readonly string root = Directory.CreateTempSubdirectory("unciv-standby-store-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(root, recursive: true); } catch (IOException) { }
    }

    private static string Command(long seq) => "{\"seq\":" + seq + ",\"type\":\"Noop\",\"actor\":\"Babylon\",\"payload\":{}}";

    private static string Checkpoint(long seq) => "{\"checkpoint\":" + seq + ",\"turn\":1,\"fullHash\":\"" + seq.ToString("x16", System.Globalization.CultureInfo.InvariantCulture) + "\"}";

    private static StandbyMeta Meta(string gameId = GameId, string? header = "{}") =>
        new() { Epoch = 0, Header = header, Meta = UncivJson.Serialize(new GameMeta { GameId = gameId }) };

    private static IEnumerable<StandbySnapshot> Chunks(byte[] bytes, long seq, bool reset, int chunk)
    {
        var parts = Math.Max(1, (bytes.Length + chunk - 1) / chunk);
        for (var part = 0; part < parts; part++)
            yield return new StandbySnapshot
            {
                Epoch = 0,
                Seq = seq,
                Part = part,
                Parts = parts,
                Sha256 = StandbyStore.HashOf(bytes),
                Data = Convert.ToBase64String(bytes, part * chunk, Math.Min(chunk, bytes.Length - part * chunk)),
                Reset = reset,
            };
    }

    private StandbyStore Primed(long snapshotSeq = 0)
    {
        var store = StandbyStore.Open(root, GameId);
        Assert.Equal(StandbyApply.Applied, store.Apply(Meta()));
        foreach (var part in Chunks(Encoding.UTF8.GetBytes("snapshot-bytes-" + new string('x', 100)), snapshotSeq, true, 40))
            Assert.NotEqual(StandbyApply.Rejected, store.Apply(part));
        return store;
    }

    private static StandbyEntry Entry(string line, long seq) => new() { Epoch = 0, Seq = seq, Line = line };

    [Theory]
    [InlineData("game-1", true)]
    [InlineData("3f2a9c1e-0000-4000-8000-123456789abc", true)]
    [InlineData("", false)]
    [InlineData("..", false)]
    [InlineData(".hidden", false)]
    [InlineData("a/b", false)]
    [InlineData("a\\b", false)]
    [InlineData("x y", false)]
    public void GameIdFromTheHost_MustBeASafeDirectoryName(string id, bool ok)
    {
        var path = StandbyStore.DirectoryFor(root, id);
        Assert.Equal(ok, path != null);
        if (ok) Assert.Equal(Path.Combine(root, "standby", id), path);
    }

    [Theory]
    [InlineData("con")]
    [InlineData("CON")]
    [InlineData("nul")]
    [InlineData("Aux.txt")]
    [InlineData("com1")]
    [InlineData("LPT9.log")]
    [InlineData("prn")]
    [InlineData("game.")]
    public void WindowsReservedNamesAndTrailingDots_AreRefused(string id) => Assert.Null(StandbyStore.DirectoryFor(root, id));

    [Theory]
    [InlineData("com10")]
    [InlineData("console")]
    [InlineData("lpt0x")]
    [InlineData("a.con")]
    public void NamesThatOnlyLookLikeReservedOnes_AreFine(string id) => Assert.NotNull(StandbyStore.DirectoryFor(root, id));

    [Fact]
    public void Consent_IsPerGame_AndCanBeCleared()
    {
        Assert.False(StandbyStore.HasConsent(root, GameId));
        Assert.True(StandbyStore.RecordConsent(root, GameId));
        Assert.True(StandbyStore.HasConsent(root, GameId));
        Assert.False(StandbyStore.HasConsent(root, "game-2"));
        Assert.False(StandbyStore.RecordConsent(root, "../x"));
        StandbyStore.ClearConsent(root, GameId);
        Assert.False(StandbyStore.HasConsent(root, GameId));
        File.WriteAllText(Path.Combine(StandbyStore.DirectoryFor(root, GameId)!, "consent.json"), "{not json");
        Assert.False(StandbyStore.HasConsent(root, GameId)); // a damaged record is no consent
    }

    [Fact]
    public void ALogOverTheLimit_IsRotatedAtTheNextSnapshot_AndStillRecovers()
    {
        var limits = new StandbyLimits { RotateLogBytes = 600 };
        var store = StandbyStore.Open(root, GameId, limits);
        Assert.Equal(StandbyApply.Applied, store.Apply(Meta()));
        foreach (var part in Chunks(Encoding.UTF8.GetBytes("snapshot-0"), 0, true, 40)) store.Apply(part);
        for (var seq = 1; seq <= 12; seq++) Assert.Equal(StandbyApply.Applied, store.Apply(Entry(Command(seq), seq)));
        Assert.Equal(StandbyApply.Applied, store.Apply(Entry(Checkpoint(12), 12)));
        var logPath = Path.Combine(store.DirectoryPath, "commands.log");
        var before = new FileInfo(logPath).Length;
        Assert.True(before > limits.RotateLogBytes);
        var lastLine = StandbyStore.LineHash(Command(12));

        foreach (var part in Chunks(Encoding.UTF8.GetBytes("snapshot-12"), 12, false, 40)) Assert.NotEqual(StandbyApply.Rejected, store.Apply(part));
        Assert.True(new FileInfo(logPath).Length < before);
        Assert.False(Directory.Exists(Path.Combine(store.DirectoryPath, "history"))); // the old log is dropped, not kept
        Assert.Single(Directory.GetFiles(store.DirectoryPath, "snapshot-*"));
        Assert.Equal(12, store.HaveSeq);
        Assert.Equal(StandbyApply.Applied, store.Apply(Entry(Command(13), 13)));
        store.Dispose();

        using var reopened = StandbyStore.Open(root, GameId, limits);
        Assert.Equal(13, reopened.HaveSeq);
        Assert.Equal(StandbyStore.LineHash(Command(13)), reopened.HaveLineSha256);
        Assert.NotEqual(lastLine, reopened.HaveLineSha256);
    }

    [Fact]
    public void TheStandbyTree_KeepsOnlyTheNewestGames()
    {
        var limits = new StandbyLimits { MaxStandbyGames = 2 };
        foreach (var (id, age) in new[] { ("old-1", 4), ("old-2", 3), ("old-3", 2) })
        {
            StandbyStore.Open(root, id, limits).Dispose();
            var dir = StandbyStore.DirectoryFor(root, id)!;
            File.WriteAllText(Path.Combine(dir, "x"), "x");
            foreach (var file in Directory.GetFiles(dir)) File.SetLastWriteTimeUtc(file, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(-age));
            Directory.SetLastWriteTimeUtc(dir, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(-age));
        }
        StandbyStore.RecordConsent(root, "old-1"); // still a successor for it: never pruned
        StandbyStore.Open(root, "new-3", limits).Dispose(); // written now: newer than the fixed 2026 dates above
        Assert.True(Directory.Exists(StandbyStore.DirectoryFor(root, "old-1")!));
        Assert.False(Directory.Exists(StandbyStore.DirectoryFor(root, "old-2")!));
        Assert.True(Directory.Exists(StandbyStore.DirectoryFor(root, "old-3")!));
        Assert.True(Directory.Exists(StandbyStore.DirectoryFor(root, "new-3")!));
    }

    [Fact]
    public void ResetSnapshot_InstallsTheStore_AndEntriesAppendVerbatim()
    {
        using var store = Primed(5);
        Assert.Equal(5, store.HaveSeq);
        Assert.Equal(StandbyApply.Applied, store.Apply(Entry(Checkpoint(5), 5)));
        Assert.Equal(StandbyApply.Applied, store.Apply(Entry(Command(6), 6)));
        Assert.Equal(StandbyApply.Applied, store.Apply(Entry(Command(7), 7)));
        Assert.Equal(StandbyApply.Applied, store.Apply(Entry(Command(7), 7))); // a repeat is ignored
        Assert.Equal(StandbyApply.Applied, store.Apply(Entry(Checkpoint(7), 7)));
        Assert.Equal(StandbyApply.Applied, store.Apply(Entry(Checkpoint(7), 7)));
        Assert.Equal(7, store.HaveSeq);
        Assert.Equal(StandbyStore.LineHash(Command(7)), store.HaveLineSha256);

        var log = File.ReadAllText(Path.Combine(store.DirectoryPath, "commands.log")).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(["{}", Checkpoint(5), Command(6), Command(7), Checkpoint(7)], log);
        Assert.True(File.Exists(Path.Combine(store.DirectoryPath, "snapshot-000000000005.json.gz")));
    }

    [Fact]
    public void TamperedChunk_IsRejected_AndLeavesNothingInstalled()
    {
        using var store = StandbyStore.Open(root, GameId);
        Assert.Equal(StandbyApply.Applied, store.Apply(Meta()));
        var chunks = Chunks(Encoding.UTF8.GetBytes(new string('a', 200)), 0, true, 64).ToList();
        Assert.Equal(StandbyApply.Pending, store.Apply(chunks[0]));
        Assert.Equal(StandbyApply.Pending, store.Apply(chunks[1] with { Data = Convert.ToBase64String(Encoding.UTF8.GetBytes(new string('b', 64))) }));
        Assert.Equal(StandbyApply.Pending, store.Apply(chunks[2]));
        Assert.Equal(StandbyApply.Rejected, store.Apply(chunks[3]));
        Assert.Equal("snapshot-hash", store.LastRejection);
        Assert.Equal(-1, store.HaveSeq);
        Assert.Empty(Directory.GetFiles(store.DirectoryPath, "snapshot-*"));
        Assert.False(File.Exists(Path.Combine(store.DirectoryPath, "commands.log")));
    }

    [Fact]
    public void TruncatedOrReorderedOrOversizedSnapshot_IsRejected()
    {
        using var store = StandbyStore.Open(root, GameId);
        store.Apply(Meta());
        var chunks = Chunks(Encoding.UTF8.GetBytes(new string('a', 200)), 0, true, 64).ToList();
        store.Apply(chunks[0]);
        Assert.Equal(StandbyApply.Rejected, store.Apply(chunks[2])); // a part missing
        Assert.Equal("chunk-order", store.LastRejection);
        Assert.Equal(StandbyApply.Rejected, store.Apply(chunks[1])); // and the assembly was dropped
        Assert.Equal(StandbyApply.Rejected, store.Apply(chunks[0] with { Data = "not base64!" }));
        Assert.Equal(StandbyApply.Rejected, store.Apply(chunks[0] with { Sha256 = "short" }));
        Assert.Equal(StandbyApply.Rejected, store.Apply(chunks[0] with { Parts = 0 }));
        Assert.Equal(StandbyApply.Rejected, store.Apply(chunks[0] with { Parts = 9999 }));

        using var small = StandbyStore.Open(Path.Combine(root, "small"), GameId, new StandbyLimits { MaxSnapshotBytes = 100 });
        small.Apply(Meta());
        Assert.Equal(StandbyApply.Pending, small.Apply(chunks[0]));
        Assert.Equal(StandbyApply.Rejected, small.Apply(chunks[1])); // 128 bytes in total exceeds the 100 byte limit
        Assert.Equal("snapshot-too-large", small.LastRejection);
    }

    [Fact]
    public void ResetWithoutHeader_AndMetaOfAnotherGame_AreRejected()
    {
        using var store = StandbyStore.Open(root, GameId);
        Assert.Equal(StandbyApply.Rejected, store.Apply(Meta("another-game")));
        Assert.Equal("meta-game", store.LastRejection);
        Assert.Equal(StandbyApply.Rejected, store.Apply(new StandbyMeta { Epoch = 0, Meta = "not json" }));
        Assert.Equal(StandbyApply.Rejected, store.Apply(Meta(header: "{}\n{}")));
        Assert.Equal(StandbyApply.Applied, store.Apply(Meta(header: null)));
        var only = Chunks(Encoding.UTF8.GetBytes("x"), 0, true, 64).Single();
        Assert.Equal(StandbyApply.Rejected, store.Apply(only));
        Assert.Equal("snapshot-without-header", store.LastRejection);
    }

    [Fact]
    public void BadLines_GapsAndForeignSeqs_AreRejected()
    {
        using var store = Primed(3);
        Assert.Equal(StandbyApply.Rejected, store.Apply(Entry(Command(5), 5))); // gap
        Assert.Equal("line-gap", store.LastRejection);
        Assert.Equal(StandbyApply.Rejected, store.Apply(Entry(Command(4), 9))); // declared seq differs from the line
        Assert.Equal(StandbyApply.Rejected, store.Apply(Entry("not json", 4)));
        Assert.Equal(StandbyApply.Rejected, store.Apply(Entry("{\"seq\":4}", 4)));
        Assert.Equal(StandbyApply.Rejected, store.Apply(Entry(Command(4) + "\n" + Command(5), 4)));
        Assert.Equal(StandbyApply.Rejected, store.Apply(Entry(Checkpoint(9), 9))); // a checkpoint of a seq the log has not reached
        Assert.Equal(StandbyApply.Rejected, store.Apply(Entry("", 4)));
        Assert.Equal(StandbyApply.Rejected, store.Apply(Entry(new string('a', 10), 4) with { Line = new string('a', 5 * 1024 * 1024) }));
        Assert.Equal(3, store.HaveSeq);
        Assert.Equal(StandbyApply.Applied, store.Apply(Entry(Command(4), 4)));

        using var empty = StandbyStore.Open(Path.Combine(root, "empty"), GameId);
        Assert.Equal(StandbyApply.Rejected, empty.Apply(Entry(Command(1), 1)));
        Assert.Equal("no-store", empty.LastRejection);
    }

    [Fact]
    public void Reopen_ResumesFromDisk_AndCutsATornLine()
    {
        string directory;
        using (var store = Primed(2))
        {
            store.Apply(Entry(Command(3), 3));
            store.Apply(Entry(Command(4), 4));
            directory = store.DirectoryPath;
        }
        File.AppendAllText(Path.Combine(directory, "commands.log"), "{\"seq\":5,\"type\":\"No"); // the successor crashed mid-write

        using var reopened = StandbyStore.Open(root, GameId);
        Assert.Equal(4, reopened.HaveSeq);
        Assert.Equal(StandbyStore.LineHash(Command(4)), reopened.HaveLineSha256);
        Assert.Equal(StandbyApply.Applied, reopened.Apply(Entry(Command(5), 5)));
        Assert.EndsWith(Command(5) + "\n", File.ReadAllText(Path.Combine(directory, "commands.log")));
    }

    [Fact]
    public void ARestartThatStoppedBeforeItsFirstLine_IsNotAUsableCopy()
    {
        using (var store = Primed(8)) Assert.Equal(8, store.HaveSeq);
        using var reopened = StandbyStore.Open(root, GameId);
        Assert.Equal(-1, reopened.HaveSeq); // header only, but a snapshot at 8: the next subscribe asks for a fresh one
    }

    [Fact]
    public void AdditionalSnapshots_AddToTheStore_KeepThreeAndNeverRunAheadOfTheLog()
    {
        using var store = Primed(0);
        foreach (var seq in new[] { 1, 2, 3, 4 }) store.Apply(Entry(Command(seq), seq));
        foreach (var seq in new[] { 1, 2, 3, 4 })
            foreach (var part in Chunks(Encoding.UTF8.GetBytes("snap" + seq), seq, false, 64))
                Assert.Equal(StandbyApply.Applied, store.Apply(part));
        Assert.Equal(3, Directory.GetFiles(store.DirectoryPath, "snapshot-*").Length);
        Assert.False(File.Exists(Path.Combine(store.DirectoryPath, "snapshot-000000000000.json.gz")));
        Assert.Equal(StandbyApply.Rejected, store.Apply(Chunks(Encoding.UTF8.GetBytes("ahead"), 9, false, 64).Single()));
        Assert.Equal("snapshot-ahead-of-log", store.LastRejection);
    }
}

/// <summary>T272: the host store's replication reads: verbatim lines, a resumable offset, rotation, and the change signal.</summary>
public sealed class FileGameStoreReplicationTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("unciv-replication-store-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(root, recursive: true); } catch (IOException) { }
    }

    private FileGameStore NewStore(StoreOptions? options = null) =>
        FileGameStore.Create(Path.Combine(root, "g"), "g", new CommandLogHeader(), new GameMeta(), null, options);

    private static CommandEnvelope Envelope(long seq) => new(seq, "Noop", "Babylon", JsonDocument.Parse("{}").RootElement.Clone());

    [Fact]
    public void Batches_ContinueByOffset_AndFilterBySeq()
    {
        using var store = NewStore();
        for (var seq = 1; seq <= 5; seq++) store.Append(Envelope(seq));
        store.AppendCheckpoint(5, 1, 0xabc);
        store.Append(Envelope(6));

        var first = store.ReadReplicationBatch(2, 0, 0, 2);
        Assert.Equal([3L, 4L], first.Lines.Select(it => it.Seq));
        var second = store.ReadReplicationBatch(4, first.NextOffset, first.Generation, 10);
        Assert.Equal([(5L, false), (5L, true), (6L, false)], second.Lines.Select(it => (it.Seq, it.IsCheckpoint)));
        Assert.Contains("\"fullHash\":\"0000000000000abc\"", second.Lines[1].Text);
        Assert.Empty(store.ReadReplicationBatch(6, second.NextOffset, second.Generation, 10).Lines);

        store.Append(Envelope(7));
        Assert.Equal([7L], store.ReadReplicationBatch(6, second.NextOffset, second.Generation, 10).Lines.Select(it => it.Seq));
        // A checkpoint is selected at its own seq (the stream's duplicate-tolerant restart point), commands only above it.
        Assert.Equal([(5L, true), (6L, false), (7L, false)], store.ReadReplicationBatch(5, 0, -1, 10).Lines.Select(it => (it.Seq, it.IsCheckpoint)));
    }

    [Fact]
    public void ATornLastLine_IsPickedUpOnceComplete()
    {
        using var store = NewStore();
        store.Append(Envelope(1));
        var path = Path.Combine(store.DirectoryPath, "commands.log");
        var batch = store.ReadReplicationBatch(0, 0, 0, 10);
        Assert.Single(batch.Lines);
        using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) stream.Write(Encoding.UTF8.GetBytes("{\"seq\":2,\"type\":\"Noop\""));
        Assert.Empty(store.ReadReplicationBatch(1, batch.NextOffset, batch.Generation, 10).Lines);
    }

    [Fact]
    public void LongLines_AreReadWhole()
    {
        using var store = NewStore();
        var big = JsonDocument.Parse("{\"text\":\"" + new string('z', 3 * 1024 * 1024) + "\"}").RootElement.Clone();
        store.Append(new CommandEnvelope(1, "Noop", "Babylon", big));
        store.Append(Envelope(2));
        var lines = store.ReadReplicationBatch(0, 0, 0, 10).Lines;
        Assert.Equal([1L, 2L], lines.Select(it => it.Seq));
        Assert.True(lines[0].Text.Length > 3 * 1024 * 1024);
    }

    [Fact]
    public void Rotation_BumpsTheGeneration_AndAStaleOffsetRestartsFromTheTop()
    {
        using var store = NewStore(new StoreOptions { RotateLogBytes = 1 });
        Assert.Equal(0, store.LogGeneration);
        for (var seq = 1; seq <= 3; seq++) store.Append(Envelope(seq));
        var batch = store.ReadReplicationBatch(0, 0, store.LogGeneration, 2);
        var before = store.LogGeneration;
        Assert.True(store.TrySnapshot(new Unciv.Logic.GameInfo())); // rotates: the log restarts at the snapshot
        Assert.True(store.LogGeneration > before);
        store.Append(Envelope(4));
        var after = store.ReadReplicationBatch(3, batch.NextOffset, batch.Generation, 10);
        Assert.Equal([(3L, true), (4L, false)], after.Lines.Select(it => (it.Seq, it.IsCheckpoint)));
    }

    [Fact]
    public void Changed_FiresForLogMetaAndSnapshotWrites_AndNewestSnapshotAndHeaderAreReadable()
    {
        using var store = NewStore();
        var changes = new List<ReplicationChange>();
        store.Changed += changes.Add;
        store.Append(Envelope(1));
        store.WriteMeta(store.ReadMeta());
        Assert.True(store.TrySnapshot(new Unciv.Logic.GameInfo()));
        Assert.Contains(ReplicationChange.Log, changes);
        Assert.Contains(ReplicationChange.Meta, changes);
        Assert.Contains(ReplicationChange.Snapshot, changes);
        Assert.Equal(1, store.ReadNewestSnapshot()!.Value.Seq);
        Assert.Equal(UncivJson.Serialize(new CommandLogHeader()), store.ReadHeaderLine());
    }

    [Fact]
    public void ALineThatIsNotJson_IsSkipped_NotSent()
    {
        using var store = NewStore();
        store.Append(Envelope(1));
        using (var raw = new FileStream(Path.Combine(((FileGameStore)store).DirectoryPath, "commands.log"), FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
            raw.Write(Encoding.UTF8.GetBytes("garbage line\n"));
        Assert.Equal([1L], store.ReadReplicationBatch(0, 0, 0, 10).Lines.Select(it => it.Seq));
    }

    [Fact]
    public void ASnapshotThatCannotBeRead_FallsBackToAnOlderOne()
    {
        if (OperatingSystem.IsWindows() || Environment.UserName == "root") return;
        using var store = NewStore();
        store.Append(Envelope(1));
        Assert.True(store.TrySnapshot(new Unciv.Logic.GameInfo()));
        store.Append(Envelope(2));
        var older = store.ReadNewestSnapshot()!.Value.Seq;
        Assert.True(store.TrySnapshot(new Unciv.Logic.GameInfo { Turns = 9 }));
        var newest = Directory.GetFiles(store.DirectoryPath, "snapshot-*").OrderBy(it => it, StringComparer.Ordinal).Last();
        File.SetUnixFileMode(newest, UnixFileMode.None);
        Assert.Equal(older, store.ReadNewestSnapshot()!.Value.Seq);
    }
}
