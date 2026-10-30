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
/// <c>snapshot-&lt;seq&gt;.json.gz</c> (<see cref="UncivFiles"/> gzip+base64, so they open in single player), the <c>clean</c> marker,
/// <c>history/</c> (rotated logs) and <c>corrupt/</c> (log tails recovery set aside). Paths inside are relative names only, so the directory can be zipped and opened elsewhere.
/// Every file other than the log is replaced atomically (temp file, fsync, rename); the log is appended and fsynced before <see cref="Append"/> returns.
/// </summary>
public sealed class FileGameStore : IGameStore, IReplicationSource
{
    private const string LogName = "commands.log";
    private const string MetaName = "game.json";
    private const string CleanName = "clean";
    private const string SnapshotPrefix = "snapshot-";
    private const string SnapshotSuffix = ".json.gz";

    private readonly object gate = new();
    private readonly string directory;
    private readonly StoreOptions options;
    private readonly List<CommandEnvelope> ring = [];
    private readonly Dictionary<(string Actor, string CmdId), long> dedup = [];
    private readonly Queue<(string Actor, string CmdId)> dedupOrder = new();
    private Stream log;
    private long lastCheckpointSeq = -1;
    private ulong lastCheckpointHash;
    private int? lastSnapshotTurn;
    private bool closed;
    private bool faulted;
    private long logGeneration;

    private FileGameStore(string directory, StoreOptions options, Stream log, string gameId)
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

    public long HostSecret
    {
        get => ReadMeta().HostSecret;
        set
        {
            lock (gate) WriteAtomic(PathOf(MetaName), Encoding.UTF8.GetBytes(UncivJson.Serialize(ReadMeta() with { HostSecret = value })));
        }
    }

    public GameMeta ReadMeta()
    {
        lock (gate)
        {
            return ReadMetaFile(PathOf(MetaName));
        }
    }

    private static GameMeta ReadMetaFile(string path)
    {
        try
        {
            return UncivJson.Deserialize<GameMeta>(File.ReadAllText(path, Encoding.UTF8)) ?? throw new GameStoreException("game.json is empty");
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new GameStoreException("game.json cannot be read: " + e.Message);
        }
    }

    public void WriteMeta(GameMeta meta)
    {
        // The secret and the epochs are not the lobby layer's to set or erase: they stay what the store holds (HostSecret, RaiseEpochs).
        lock (gate)
        {
            var held = ReadMeta();
            WriteAtomic(PathOf(MetaName), Encoding.UTF8.GetBytes(UncivJson.Serialize(meta with { GameId = GameId, HostSecret = held.HostSecret, HostEpoch = held.HostEpoch, SeenEpoch = held.SeenEpoch, Promotion = held.Promotion })));
            Changed?.Invoke(ReplicationChange.Meta);
        }
    }

    /// <summary>T269: records this host's epoch and the highest epoch seen from a peer. Neither ever goes down.</summary>
    public void RaiseEpochs(int hostEpoch, int seenEpoch)
    {
        lock (gate)
        {
            var held = ReadMeta();
            var host = Math.Max(held.HostEpoch, hostEpoch);
            var seen = Math.Max(held.SeenEpoch, seenEpoch);
            if (host == held.HostEpoch && seen == held.SeenEpoch) return;
            WriteAtomic(PathOf(MetaName), Encoding.UTF8.GetBytes(UncivJson.Serialize(held with { HostEpoch = host, SeenEpoch = seen })));
        }
    }

    /// <summary>
    /// T273: this directory becomes the game of a promoted host. <c>game.json</c> gets <paramref name="epoch"/> and <paramref name="proof"/> (the successor's signed statement, whose game and epoch must match),
    /// and the log header line gets <c>HostEpoch</c> too (the log is rewritten atomically, so a crash leaves a complete old or new file). The epoch never goes down.
    /// </summary>
    /// <exception cref="GameStoreException">the epoch is not above the one the directory holds, or the proof is for another game or epoch.</exception>
    public void PromoteTo(int epoch, PromotionProof proof)
    {
        lock (gate)
        {
            ThrowIfClosed();
            var held = ReadMeta();
            if (epoch <= held.HostEpoch) throw new GameStoreException("A promotion must raise the host epoch");
            if (proof.Epoch != epoch || !string.Equals(proof.GameId, GameId, StringComparison.Ordinal)) throw new GameStoreException("The promotion proof is for another game or epoch");
            WriteAtomic(PathOf(MetaName), Encoding.UTF8.GetBytes(UncivJson.Serialize(held with { HostEpoch = epoch, SeenEpoch = Math.Max(held.SeenEpoch, epoch), Promotion = proof })));

            var all = File.ReadAllBytes(PathOf(LogName));
            var headerEnd = Array.IndexOf(all, (byte)'\n') + 1;
            var header = UncivJson.Deserialize<CommandLogHeader>(Encoding.UTF8.GetString(all, 0, headerEnd).Trim()) ?? throw new GameStoreException("The command log has no readable header");
            var rewritten = Encoding.UTF8.GetBytes(UncivJson.Serialize(header with { HostEpoch = epoch }) + "\n").Concat(all.Skip(headerEnd)).ToArray();
            log.Dispose();
            WriteAtomic(PathOf(LogName), rewritten);
            log = OpenLog(PathOf(LogName));
            logGeneration++;
        }
    }

    // ------------------------------------------------------------------ replication source (T272)

    /// <inheritdoc />
    public event Action<ReplicationChange>? Changed;

    /// <inheritdoc />
    public long LogGeneration
    {
        get
        {
            lock (gate) return logGeneration;
        }
    }

    /// <inheritdoc />
    public string ReadHeaderLine()
    {
        lock (gate)
        {
            using var stream = new FileStream(PathOf(LogName), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var bytes = new byte[Math.Min(stream.Length, 1 << 20)];
            var read = stream.Read(bytes, 0, bytes.Length);
            var end = Array.IndexOf(bytes, (byte)'\n', 0, read);
            if (end < 0) throw new GameStoreException("The command log has no complete header line");
            return Encoding.UTF8.GetString(bytes, 0, end);
        }
    }

    /// <inheritdoc />
    public (long Seq, byte[] Bytes)? ReadNewestSnapshot()
    {
        lock (gate)
        {
            foreach (var (seq, path) in ListSnapshots(directory))
            {
                try
                {
                    return (seq, File.ReadAllBytes(path));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // retention removed it between listing and reading: try the next one
                }
            }
            return null;
        }
    }

    /// <inheritdoc />
    public ReplicationBatch ReadReplicationBatch(long afterSeq, long offset, long generation, int maxLines)
    {
        lock (gate)
        {
            var lines = new List<ReplicationLine>();
            using var stream = new FileStream(PathOf(LogName), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var length = stream.Length;
            var position = offset > 0 && generation == logGeneration && offset <= length ? offset : 0;
            if (position == 0)
            {
                var head = new byte[Math.Min(length, 1 << 20)];
                var read = stream.Read(head, 0, head.Length);
                var headerEnd = Array.IndexOf(head, (byte)'\n', 0, read);
                if (headerEnd < 0) return new ReplicationBatch(lines, 0, logGeneration);
                position = headerEnd + 1;
            }

            // Windows of 1 MiB; a single line longer than the window (or nothing complete in it) doubles it until the rest of the file is in view.
            var window = 1L << 20;
            while (lines.Count < maxLines && position < length)
            {
                var chunk = new byte[Math.Min(length - position, window)];
                stream.Seek(position, SeekOrigin.Begin);
                stream.ReadExactly(chunk);
                var consumed = 0;
                while (lines.Count < maxLines)
                {
                    var end = Array.IndexOf(chunk, (byte)'\n', consumed);
                    if (end < 0) break;
                    var entry = ParseLine(Encoding.UTF8.GetString(chunk, consumed, end - consumed));
                    consumed = end + 1;
                    if (entry != null && (entry.IsCheckpoint ? entry.Seq >= afterSeq : entry.Seq > afterSeq)) lines.Add(entry);
                }
                if (consumed == 0)
                {
                    if (chunk.Length >= length - position) break; // a torn last line: the next read picks it up once complete
                    window *= 2;
                }
                position += consumed;
            }
            return new ReplicationBatch(lines, position, logGeneration);
        }
    }

    private static ReplicationLine? ParseLine(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            if (root.TryGetProperty("checkpoint", out var checkpoint)) return new ReplicationLine(checkpoint.GetInt64(), true, text);
            return root.TryGetProperty("seq", out var seq) ? new ReplicationLine(seq.GetInt64(), false, text) : null;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    // ------------------------------------------------------------------ creating

    /// <summary>Starts a new game directory: <paramref name="header"/> as line 1 of the log, the meta file, and a snapshot of the new game when it is given.</summary>
    public static FileGameStore Create(string directory, string gameId, CommandLogHeader header, GameMeta meta, GameInfo? start = null, StoreOptions? options = null)
    {
        if (File.Exists(Path.Combine(directory, LogName))) throw new GameStoreException($"{directory} already holds a game");
        CreatePrivateDirectory(directory);
        // game.json first, the log last: a crash in between leaves a directory Create accepts again (the log is what makes it a game), never a log without its meta.
        WriteAtomic(Path.Combine(directory, MetaName), Encoding.UTF8.GetBytes(UncivJson.Serialize(meta with { GameId = gameId, HostSecret = meta.HostSecret != 0 ? meta.HostSecret : header.HostSecret })));
        var logPath = Path.Combine(directory, LogName);
        WriteAtomic(logPath, Encoding.UTF8.GetBytes(UncivJson.Serialize(header) + "\n"));
        var store = new FileGameStore(directory, options ?? new StoreOptions(), OpenLog(logPath), gameId);
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
            Changed?.Invoke(ReplicationChange.Log);
        }
    }

    public void Sync(GameSession session)
    {
        lock (gate)
        {
            var log = session.Log;
            var first = log.Count;
            while (first > 0 && log[first - 1].Seq > LastSeq) first--; // only the unlogged tail
            for (var i = first; i < log.Count; i++) Append(log[i]);
        }
    }

    public void AppendCheckpoint(long seq, int turn, ulong fullHash)
    {
        lock (gate)
        {
            ThrowIfClosed();
            if (seq != LastSeq) throw new GameStoreException($"A checkpoint for seq {seq} at seq {LastSeq}");
            if (lastCheckpointSeq == seq)
            {
                if (lastCheckpointHash != fullHash) throw new GameStoreException($"The log already holds a different state hash for seq {seq}: the game changed without a logged command");
                return; // already there (a snapshot writes one, so does the host's checkpoint after a turn change)
            }
            var line = new JsonObject
            {
                ["checkpoint"] = seq,
                ["turn"] = turn,
                ["fullHash"] = fullHash.ToString("x16", CultureInfo.InvariantCulture),
            };
            WriteDurably(line.ToJsonString());
            lastCheckpointSeq = seq;
            lastCheckpointHash = fullHash;
            Changed?.Invoke(ReplicationChange.Log);
        }
    }

    private void WriteDurably(string line)
    {
        if (faulted) throw new GameStoreException("An earlier log write failed and could not be undone; reopen the game (recovery cuts the torn line off)");
        var before = log.Length;
        try
        {
            log.Seek(0, SeekOrigin.End);
            log.Write(Encoding.UTF8.GetBytes(line + "\n"));
            if (log is FileStream file) file.Flush(true); // flushed to the device before the host acknowledges
            else log.Flush();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A half-written line must not stay: the next append would be glued to it and recovery would cut off everything acknowledged after it.
            try
            {
                log.SetLength(before);
            }
            catch (Exception undo) when (undo is IOException or UnauthorizedAccessException)
            {
                faulted = true;
            }
            throw new GameStoreException("The command log could not be written (disk full or I/O error): " + e.Message);
        }
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
        // A snapshot that disagrees with a checkpoint the log already has is refused: recovery would reject it, and the game could not come back from it.
        AppendCheckpoint(seq, game.Turns, ReplayVerifier.FullHash(game));
        WriteAtomic(PathOf(SnapshotName(seq)), Encoding.UTF8.GetBytes(UncivFiles.GameInfoToString(game, forceZip: true)));
        lastSnapshotTurn = game.Turns;
        Retain(seq);
        Changed?.Invoke(ReplicationChange.Snapshot);
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
        CreatePrivateDirectory(history);
        WriteAtomic(Path.Combine(history, "commands-" + snapshotSeq.ToString("D12", CultureInfo.InvariantCulture) + ".log"), all);

        var (prefixLength, checkpointLine) = SplitAt(all, snapshotSeq);
        var headerEnd = Array.IndexOf(all, (byte)'\n') + 1;
        var rotated = new List<byte>(all.AsSpan(0, headerEnd).ToArray());
        if (checkpointLine != null) rotated.AddRange(Encoding.UTF8.GetBytes(checkpointLine + "\n"));
        rotated.AddRange(all.AsSpan(prefixLength).ToArray());

        log.Dispose();
        WriteAtomic(PathOf(LogName), rotated.ToArray());
        log = OpenLog(PathOf(LogName));
        logGeneration++;
        foreach (var (seq, path) in ListSnapshots(directory))
            if (seq < snapshotSeq) File.Delete(path);
    }

    // Where the log's lines about seqs after `seq` begin (a byte offset), and the checkpoint line for `seq` itself when the log has one.
    internal static (int PrefixLength, string? CheckpointLine) SplitAt(byte[] all, long seq)
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

    // Unbuffered: a failed write must not leave bytes in a .NET buffer that the next append would send after the line that was rolled back.
    private static FileStream OpenLog(string path) => new(path, FileMode.Open, FileAccess.Write, FileShare.Read, 1);

    /// <summary>Test seam: wraps the log stream (to inject I/O errors).</summary>
    internal void WrapLog(Func<Stream, Stream> wrap)
    {
        lock (gate) log = wrap(log);
    }

    // The game directory holds the host secret (game.json, the log header): owner only where the platform has modes.
    private static void CreatePrivateDirectory(string directory)
    {
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(directory);
        else Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private static FileStreamOptions PrivateCreate()
    {
        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        return options;
    }

    internal static void WriteAtomic(string path, byte[] bytes)
    {
        var temp = path + ".tmp";
        using (var stream = new FileStream(temp, PrivateCreate()))
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
        var meta = ReadMetaFile(Path.Combine(directory, MetaName));
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
        SetAsideNewerSnapshots(directory, recovered.SnapshotSeq, notes);
        var discarded = recovered.Fallback ? scan.LastSeq - recovered.SnapshotSeq : 0;
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
        var lastCheckpoint = scan.Lines.OfType<CommandLogLine.Checkpoint>().LastOrDefault();
        store.lastCheckpointSeq = lastCheckpoint?.Seq ?? -1;
        store.lastCheckpointHash = lastCheckpoint?.FullHash ?? 0;
        store.lastSnapshotTurn = recovered.SnapshotTurn;
        foreach (var command in scan.Commands) store.Remember(command);
        // T230: a snapshot does not carry the secret; every recovery path installs the host's before the first command.
        var secret = meta.HostSecret != 0 ? meta.HostSecret : scan.Header.HostSecret;
        Unciv.Utils.HostSecret.ApplyTo(recovered.Game, secret);
        var session = new GameSession(recovered.Game, lastSeq: scan.LastSeq);
        var report = new RecoveryReport(recovered.Outcome, wasClean, recovered.SnapshotSeq, scan.LastSeq, recovered.Replayed, recovered.Verified,
            scan.DroppedLines, recovered.DivergedAtSeq, notes)
        { DiscardedCommands = discarded };
        return new OpenedGame(store, session, report);
    }

    // A snapshot newer than the one recovery settled on was rejected (ahead of the log, or disagreeing with it). It must not stay: once the log grows past its seq it would
    // look like a snapshot of the new history, and it would crowd the good ones out of the retention window.
    private static void SetAsideNewerSnapshots(string directory, long keptSeq, List<string> notes)
    {
        foreach (var (seq, path) in ListSnapshots(directory))
        {
            if (seq <= keptSeq) continue;
            SetAside(directory, File.ReadAllBytes(path), "rejected-snapshot-" + seq.ToString("D12", CultureInfo.InvariantCulture), ".json.gz");
            File.Delete(path);
            notes.Add($"Set aside rejected snapshot {seq}");
        }
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

    private static void SetAside(string directory, byte[] bytes, string name, string extension = ".log")
    {
        if (bytes.Length == 0) return;
        var folder = Path.Combine(directory, "corrupt");
        CreatePrivateDirectory(folder);
        var target = Path.Combine(folder, name + extension);
        for (var n = 2; File.Exists(target); n++) target = Path.Combine(folder, name + "-" + n.ToString(CultureInfo.InvariantCulture) + extension); // never overwrite earlier evidence
        WriteAtomic(target, bytes);
    }

    private static void Truncate(string path, long length)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
        stream.SetLength(length);
        stream.Flush(true);
    }
}
