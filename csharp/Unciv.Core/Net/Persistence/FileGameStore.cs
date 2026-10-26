using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Unciv.Commands;
using Unciv.Commands.Log;
using Unciv.Json;
using Unciv.Logic;
using Unciv.Logic.Files;

namespace Unciv.Net.Persistence;

/// <summary>A game recovered (or created) by <see cref="FileGameStore"/>: the store to keep appending to, a session over the recovered state, and what recovery did.</summary>
public sealed record OpenedGame(FileGameStore Store, GameSession Session, RecoveryReport Report) : IDisposable
{
    public void Dispose() => Store.Dispose();
}

/// <summary>
/// <see cref="IGameStore"/> over a directory <c>games/&lt;gameId&gt;/</c> (SERVER.md 5.1):
/// <c>game.json</c>, <c>commands.log</c> (JSON Lines in the T194 format, plus an optional <c>clientCmdId</c> member per command that readers of the T194 format ignore),
/// <c>snapshot-&lt;seq&gt;.json.gz</c> (<see cref="UncivFiles"/> gzip+base64, so they open in single player), <c>host.secret</c>, the <c>clean</c> marker,
/// <c>history/</c> (rotated logs) and <c>corrupt/</c> (log tails recovery set aside). Paths inside are relative names only, so the directory can be zipped and opened elsewhere.
/// Every file other than the log is replaced atomically (temp file, fsync, rename); the log is appended and fsynced before <see cref="Append"/> returns.
/// </summary>
public sealed class FileGameStore : IGameStore
{
    private const string LogName = "commands.log";
    private const string MetaName = "game.json";
    private const string SecretName = "host.secret";
    private const string CleanName = "clean";
    private const string SnapshotPrefix = "snapshot-";
    private const string SnapshotSuffix = ".json.gz";

    private readonly object gate = new();
    private readonly string directory;
    private readonly StoreOptions options;
    private readonly List<CommandEnvelope> ring = [];
    private readonly Dictionary<(string Actor, string CmdId), long> dedup = [];
    private readonly Queue<(string Actor, string CmdId)> dedupOrder = new();
    private FileStream log;
    private long lastCheckpointSeq = -1;
    private int? lastSnapshotTurn;
    private bool closed;

    private FileGameStore(string directory, StoreOptions options, FileStream log, string gameId)
    {
        this.directory = directory;
        this.options = options;
        this.log = log;
        GameId = gameId;
    }

    public string GameId { get; }

    /// <summary>The game directory.</summary>
    public string DirectoryPath => directory;

    public long LastSeq { get; private set; }

    public string? HostSecret
    {
        get
        {
            lock (gate)
            {
                var path = PathOf(SecretName);
                return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null;
            }
        }
        set
        {
            lock (gate)
            {
                if (value == null) File.Delete(PathOf(SecretName));
                else WriteAtomic(PathOf(SecretName), Encoding.UTF8.GetBytes(value));
            }
        }
    }

    public GameMeta ReadMeta()
    {
        lock (gate)
        {
            return UncivJson.Deserialize<GameMeta>(File.ReadAllText(PathOf(MetaName), Encoding.UTF8)) ?? throw new GameStoreException("game.json is empty");
        }
    }

    public void WriteMeta(GameMeta meta)
    {
        lock (gate) WriteAtomic(PathOf(MetaName), Encoding.UTF8.GetBytes(UncivJson.Serialize(meta with { GameId = GameId })));
    }

    // ------------------------------------------------------------------ creating

    /// <summary>Starts a new game directory: <paramref name="header"/> as line 1 of the log, the meta file, and a snapshot of the new game when it is given.</summary>
    public static FileGameStore Create(string directory, string gameId, CommandLogHeader header, GameMeta meta, GameInfo? start = null, StoreOptions? options = null)
    {
        if (File.Exists(Path.Combine(directory, LogName))) throw new GameStoreException($"{directory} already holds a game");
        Directory.CreateDirectory(directory);
        var logPath = Path.Combine(directory, LogName);
        WriteAtomic(logPath, Encoding.UTF8.GetBytes(UncivJson.Serialize(header) + "\n"));
        var store = new FileGameStore(directory, options ?? new StoreOptions(), OpenLog(logPath), gameId);
        store.WriteMeta(meta);
        if (start != null) store.WriteSnapshot(start, 0);
        return store;
    }

    // ------------------------------------------------------------------ appending

    public void Append(CommandEnvelope envelope, string? clientCmdId = null)
    {
        lock (gate)
        {
            ThrowIfClosed();
            if (envelope.Seq != LastSeq + 1) throw new GameStoreException($"Log out of order: expected seq {LastSeq + 1}, got {envelope.Seq}");
            var line = new JsonObject
            {
                ["seq"] = envelope.Seq,
                ["type"] = envelope.Type,
                ["actor"] = envelope.ActorId,
                ["payload"] = JsonNode.Parse(envelope.Payload.GetRawText()),
            };
            if (clientCmdId != null) line["clientCmdId"] = clientCmdId;
            WriteDurably(line.ToJsonString());
            LastSeq = envelope.Seq;
            Remember(new LoggedCommand(envelope, clientCmdId));
        }
    }

    public void Sync(GameSession session)
    {
        lock (gate)
        {
            foreach (var envelope in session.Log)
                if (envelope.Seq > LastSeq) Append(envelope);
        }
    }

    public void AppendCheckpoint(long seq, int turn, ulong fullHash)
    {
        lock (gate)
        {
            ThrowIfClosed();
            if (seq != LastSeq) throw new GameStoreException($"A checkpoint for seq {seq} at seq {LastSeq}");
            if (lastCheckpointSeq == seq) return; // already there (a snapshot writes one, so does the host's checkpoint after a turn change)
            var line = new JsonObject
            {
                ["checkpoint"] = seq,
                ["turn"] = turn,
                ["fullHash"] = fullHash.ToString("x16", CultureInfo.InvariantCulture),
            };
            WriteDurably(line.ToJsonString());
            lastCheckpointSeq = seq;
        }
    }

    private void WriteDurably(string line)
    {
        log.Seek(0, SeekOrigin.End);
        log.Write(Encoding.UTF8.GetBytes(line + "\n"));
        log.Flush(true); // flushed to the device before the host acknowledges
    }

    private void Remember(LoggedCommand command)
    {
        ring.Add(command.Envelope);
        if (ring.Count > options.RingSize) ring.RemoveRange(0, ring.Count - options.RingSize);
        if (command.ClientCmdId == null) return;
        var key = (command.Envelope.ActorId, command.ClientCmdId);
        if (dedup.TryAdd(key, command.Envelope.Seq)) dedupOrder.Enqueue(key);
        while (dedupOrder.Count > options.DedupSize) dedup.Remove(dedupOrder.Dequeue());
    }

    public LoggedOutcome? FindOutcome(string actor, string clientCmdId)
    {
        lock (gate) return dedup.TryGetValue((actor, clientCmdId), out var seq) ? new LoggedOutcome(seq) : null;
    }

    public ResumePlan Resume(long lastSeq)
    {
        lock (gate)
        {
            if (lastSeq == LastSeq) return new ResumePlan(ResumeKind.UpToDate, []);
            if (lastSeq > LastSeq || lastSeq < 0 || ring.Count == 0 || ring[0].Seq > lastSeq + 1) return new ResumePlan(ResumeKind.Snapshot, []);
            return new ResumePlan(ResumeKind.Deltas, ring.Where(it => it.Seq > lastSeq).ToList());
        }
    }

    // ------------------------------------------------------------------ snapshots

    /// <summary>
    /// A snapshot taken while a simultaneous round is being resolved does not load back to the live state (T200 finding 4), so it is only taken in the Playing phase
    /// (or in a sequential game). The caller retries at the next safe point.
    /// </summary>
    public static bool IsSafeToSnapshot(GameInfo game) => game.TurnState is not { Phase: SimultaneousPhase.Resolving };

    public bool TrySnapshot(GameInfo game)
    {
        lock (gate)
        {
            ThrowIfClosed();
            if (!IsSafeToSnapshot(game)) return false;
            WriteSnapshot(game, LastSeq);
            return true;
        }
    }

    public bool MaybeSnapshot(GameInfo game)
    {
        lock (gate)
        {
            return (lastSnapshotTurn is not { } last || game.Turns - last >= options.SnapshotEveryTurns) && TrySnapshot(game);
        }
    }

    private void WriteSnapshot(GameInfo game, long seq)
    {
        // The checkpoint at the snapshot's seq lets recovery check the snapshot itself against the log.
        if (lastCheckpointSeq != seq) AppendCheckpoint(seq, game.Turns, ReplayVerifier.FullHash(game));
        WriteAtomic(PathOf(SnapshotName(seq)), Encoding.UTF8.GetBytes(UncivFiles.GameInfoToString(game, forceZip: true)));
        lastSnapshotTurn = game.Turns;
        Retain(seq);
    }

    private static string SnapshotName(long seq) => SnapshotPrefix + seq.ToString("D12", CultureInfo.InvariantCulture) + SnapshotSuffix;

    internal static IReadOnlyList<(long Seq, string Path)> ListSnapshots(string directory) =>
        Directory.GetFiles(directory, SnapshotPrefix + "*" + SnapshotSuffix)
            .Select(it => (Name: Path.GetFileName(it), Path: it))
            .Select(it => (Ok: long.TryParse(it.Name[SnapshotPrefix.Length..^SnapshotSuffix.Length], NumberStyles.None, CultureInfo.InvariantCulture, out var seq), Seq: seq, it.Path))
            .Where(it => it.Ok)
            .OrderByDescending(it => it.Seq)
            .Select(it => (it.Seq, it.Path))
            .ToList();

    // ------------------------------------------------------------------ retention

    private void Retain(long snapshotSeq)
    {
        if (options.RotateLogBytes > 0 && log.Length > options.RotateLogBytes) RotateLog(snapshotSeq);
        var keep = Math.Max(1, options.KeepSnapshots);
        foreach (var (_, path) in ListSnapshots(directory).Skip(keep)) File.Delete(path);
    }

    // The whole old log goes to history/ (a replay file for audit); commands.log restarts at the snapshot: header, its checkpoint, the lines after it. Snapshots older than
    // the new start could no longer be replayed forward, so they are dropped. A crash at any step leaves a complete commands.log, old or new.
    private void RotateLog(long snapshotSeq)
    {
        var all = File.ReadAllBytes(PathOf(LogName));
        var history = Path.Combine(directory, "history");
        Directory.CreateDirectory(history);
        WriteAtomic(Path.Combine(history, "commands-" + snapshotSeq.ToString("D12", CultureInfo.InvariantCulture) + ".log"), all);

        var (prefixLength, checkpointLine) = SplitAt(all, snapshotSeq);
        var headerEnd = Array.IndexOf(all, (byte)'\n') + 1;
        var rotated = new List<byte>(all.AsSpan(0, headerEnd).ToArray());
        if (checkpointLine != null) rotated.AddRange(Encoding.UTF8.GetBytes(checkpointLine + "\n"));
        rotated.AddRange(all.AsSpan(prefixLength).ToArray());

        log.Dispose();
        WriteAtomic(PathOf(LogName), rotated.ToArray());
        log = OpenLog(PathOf(LogName));
        foreach (var (seq, path) in ListSnapshots(directory))
            if (seq < snapshotSeq) File.Delete(path);
    }

    // Where the log's lines about seqs after `seq` begin (a byte offset), and the checkpoint line for `seq` itself when the log has one.
    private static (int PrefixLength, string? CheckpointLine) SplitAt(byte[] all, long seq)
    {
        var position = Array.IndexOf(all, (byte)'\n') + 1; // after the header
        string? checkpointLine = null;
        while (position < all.Length)
        {
            var end = Array.IndexOf(all, (byte)'\n', position);
            if (end < 0) break;
            var line = Encoding.UTF8.GetString(all, position, end - position);
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var isCheckpoint = root.TryGetProperty("checkpoint", out var checkpoint);
            var lineSeq = isCheckpoint ? checkpoint.GetInt64() : root.GetProperty("seq").GetInt64();
            if (lineSeq > seq) break;
            if (isCheckpoint && lineSeq == seq) checkpointLine = line;
            position = end + 1;
        }
        return (position, checkpointLine);
    }

    // ------------------------------------------------------------------ shutdown

    public void CloseCleanly(GameInfo game)
    {
        lock (gate)
        {
            ThrowIfClosed();
            TrySnapshot(game); // not taken mid-resolve; the log then carries the recovery
            WriteAtomic(PathOf(CleanName), Encoding.UTF8.GetBytes(LastSeq.ToString(CultureInfo.InvariantCulture)));
            closed = true;
            log.Dispose();
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            closed = true;
            log.Dispose();
        }
    }

    private void ThrowIfClosed()
    {
        if (closed) throw new ObjectDisposedException(nameof(FileGameStore));
    }

    // ------------------------------------------------------------------ files

    private string PathOf(string name) => Path.Combine(directory, name);

    private static FileStream OpenLog(string path) => new(path, FileMode.Open, FileAccess.Write, FileShare.Read);

    internal static void WriteAtomic(string path, byte[] bytes)
    {
        var temp = path + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes);
            stream.Flush(true);
        }
        File.Move(temp, path, overwrite: true);
    }

    // ------------------------------------------------------------------ opening and recovery

    /// <summary>
    /// Opens a game directory after a stop or a crash. Recovery = newest snapshot that loads and agrees with the log, plus the replayed tail with its checkpoints verified;
    /// a torn or corrupt tail of the log is cut off and kept under <c>corrupt/</c>. If the replay disagrees with a checkpoint (or a command no longer validates) the
    /// last good snapshot is loaded and the log after it is set aside, never leaving the game quarantined (<see cref="RecoveryOutcome.FellBackToSnapshot"/>).
    /// </summary>
    /// <exception cref="GameStoreException">neither a snapshot nor the log's start can be replayed.</exception>
    public static OpenedGame Open(string directory, StoreOptions? options = null)
    {
        options ??= new StoreOptions();
        var meta = UncivJson.Deserialize<GameMeta>(File.ReadAllText(Path.Combine(directory, MetaName), Encoding.UTF8)) ?? throw new GameStoreException("game.json is empty");
        var logPath = Path.Combine(directory, LogName);
        var scan = LogScan.Read(logPath);
        var notes = new List<string>();
        var cleanPath = Path.Combine(directory, CleanName);
        var wasClean = File.Exists(cleanPath) && File.ReadAllText(cleanPath, Encoding.UTF8).Trim() == scan.LastSeq.ToString(CultureInfo.InvariantCulture) && scan.DroppedLines == 0;
        File.Delete(cleanPath); // the game is running again
        foreach (var leftover in Directory.GetFiles(directory, "*.tmp")) File.Delete(leftover); // a write the crash interrupted

        if (scan.DroppedBytes.Length > 0)
        {
            notes.Add($"Cut off {scan.DroppedLines} unreadable log line(s) after seq {scan.LastSeq}");
            SetAside(directory, scan.DroppedBytes, "tail-after-" + scan.LastSeq.ToString("D12", CultureInfo.InvariantCulture));
            Truncate(logPath, scan.ValidBytes);
        }

        var recovered = Recover(directory, scan, notes);
        if (recovered.Fallback)
        {
            // Keep the log up to the snapshot only; the rest is kept for audit.
            var all = File.ReadAllBytes(logPath);
            var (kept, _) = SplitAt(all, recovered.SnapshotSeq);
            SetAside(directory, all[kept..], "diverged-after-" + recovered.SnapshotSeq.ToString("D12", CultureInfo.InvariantCulture));
            Truncate(logPath, kept);
            scan = LogScan.Read(logPath);
        }

        var store = new FileGameStore(directory, options, OpenLog(logPath), meta.GameId)
        {
            LastSeq = scan.LastSeq,
        };
        store.lastCheckpointSeq = scan.Lines.OfType<CommandLogLine.Checkpoint>().LastOrDefault()?.Seq ?? -1;
        store.lastSnapshotTurn = recovered.SnapshotTurn;
        foreach (var command in scan.Commands) store.Remember(command);
        var session = new GameSession(recovered.Game, lastSeq: scan.LastSeq);
        var report = new RecoveryReport(recovered.Outcome, wasClean, recovered.SnapshotSeq, scan.LastSeq, recovered.Replayed, recovered.Verified,
            scan.DroppedLines, recovered.DivergedAtSeq, notes);
        return new OpenedGame(store, session, report);
    }

    private sealed record Recovered(GameInfo Game, RecoveryOutcome Outcome, bool Fallback, long SnapshotSeq, int SnapshotTurn, long Replayed, int Verified, long? DivergedAtSeq);

    private static Recovered Recover(string directory, LogScan scan, List<string> notes)
    {
        var log = scan.ToLog();
        (GameInfo Game, long Seq)? good = null;
        long? diverged = null;
        foreach (var (seq, path) in ListSnapshots(directory))
        {
            if (seq > scan.LastSeq)
            {
                notes.Add($"Skipped snapshot {seq}: it is ahead of the log (last seq {scan.LastSeq})");
                continue;
            }
            if (seq < scan.BaseSeq)
            {
                notes.Add($"Skipped snapshot {seq}: the log starts after seq {scan.BaseSeq}");
                continue;
            }
            GameInfo snapshot;
            try
            {
                snapshot = UncivFiles.GameInfoFromString(File.ReadAllText(path, Encoding.UTF8));
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                notes.Add($"Snapshot {seq} cannot be read: {e.Message}");
                SetAside(directory, File.ReadAllBytes(path), "snapshot-" + seq.ToString("D12", CultureInfo.InvariantCulture));
                File.Delete(path);
                continue;
            }

            var checkpoint = scan.Lines.OfType<CommandLogLine.Checkpoint>().FirstOrDefault(it => it.Seq == seq);
            if (checkpoint != null && checkpoint.FullHash != ReplayVerifier.FullHash(snapshot))
            {
                notes.Add($"Snapshot {seq} does not match the log's checkpoint for it");
                continue;
            }

            var attempt = TryReplay(snapshot, seq, log, out var result);
            if (attempt == null && result is { Matches: true })
            {
                return new Recovered(result.Game, RecoveryOutcome.Recovered, false, seq, snapshot.Turns, result.LastSeq - seq, result.CheckpointsVerified, null);
            }
            diverged ??= result?.DivergedAtSeq;
            notes.Add($"Replaying the log after snapshot {seq} failed: {attempt ?? $"checkpoint mismatch at seq {result?.DivergedAtSeq}"}");
            good ??= (UncivFiles.GameInfoFromString(File.ReadAllText(path, Encoding.UTF8)), seq); // the newest snapshot that is itself consistent with the log
            break; // older snapshots would replay into the same failure
        }

        if (good is { } fallback)
        {
            notes.Add($"Fell back to snapshot {fallback.Seq}; the log after it was set aside");
            return new Recovered(fallback.Game, RecoveryOutcome.FellBackToSnapshot, true, fallback.Seq, fallback.Game.Turns, 0, 0, diverged);
        }

        if (scan.BaseSeq != 0) throw new GameStoreException("No snapshot can be used and the log has been rotated: the game cannot be rebuilt");
        var failure = TryReplayFromStart(log, out var fromStart);
        if (failure != null || fromStart is not { Matches: true })
            throw new GameStoreException("No snapshot can be used and the log does not replay from its start: " + (failure ?? $"checkpoint mismatch at seq {fromStart?.DivergedAtSeq}"));
        notes.Add("No snapshot could be used; the game was rebuilt from the log header");
        return new Recovered(fromStart.Game, RecoveryOutcome.ReplayedFromStart, false, 0, fromStart.Game.Turns, fromStart.LastSeq, fromStart.CheckpointsVerified, null);
    }

    private static string? TryReplay(GameInfo snapshot, long seq, CommandLog log, out ReplayResult? result)
    {
        try
        {
            result = ReplayVerifier.VerifyTail(snapshot, seq, log);
            return null;
        }
        catch (Exception e) when (e is CommandDesyncException or CommandApplyException or CommandLogException or InvalidOperationException)
        {
            result = null;
            return e.Message;
        }
    }

    private static string? TryReplayFromStart(CommandLog log, out ReplayResult? result)
    {
        try
        {
            result = ReplayVerifier.Verify(log);
            return null;
        }
        catch (Exception e) when (e is CommandDesyncException or CommandApplyException or CommandLogException or InvalidOperationException)
        {
            result = null;
            return e.Message;
        }
    }

    private static void SetAside(string directory, byte[] bytes, string name)
    {
        if (bytes.Length == 0) return;
        var folder = Path.Combine(directory, "corrupt");
        Directory.CreateDirectory(folder);
        WriteAtomic(Path.Combine(folder, name + ".log"), bytes);
    }

    private static void Truncate(string path, long length)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
        stream.SetLength(length);
        stream.Flush(true);
    }
}
