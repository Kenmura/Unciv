using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Unciv.Commands.Log;
using Unciv.Json;
using Unciv.Net.Persistence;

namespace Unciv.Net.Standby;

/// <summary>The outcome of feeding the standby store one frame of the replication stream.</summary>
public enum StandbyApply
{
    /// <summary>Stored (or nothing to do): the frame was valid.</summary>
    Applied,

    /// <summary>A snapshot chunk was accepted and more are expected.</summary>
    Pending,

    /// <summary>The frame is not acceptable (<see cref="StandbyStore.LastRejection"/> says why); the store is unchanged apart from dropping a half-received snapshot.</summary>
    Rejected,
}

/// <summary>
/// T272: the successor's copy of a hosted game (design/HOST-MIGRATION.md section 1), under <c>standby/&lt;gameId&gt;/</c> in its data directory in exactly the host's game-directory format
/// (<c>game.json</c>, <c>commands.log</c>, <c>snapshot-&lt;seq&gt;.json.gz</c>), so promotion is <see cref="FileGameStore.Open"/> and the manual hand-off can use the same folder.
/// It holds full game state: nothing in the client UI may read from it (the UI renders only the filtered mirror). Every frame is validated before it is stored: a snapshot must match its size and sha256 and
/// arrive in order, a log line must parse as a command or checkpoint of the next seq, and a line is durable (fsynced) before <see cref="Apply(StandbyEntry)"/> returns so the caller may ack it.
/// One instance per game, used by one thread at a time.
/// </summary>
public sealed class StandbyStore : IDisposable
{
    private const string LogName = "commands.log";
    private const string MetaName = "game.json";
    private const int KeepSnapshots = 3;

    private readonly string directory;
    private readonly string gameId;
    private readonly StandbyLimits limits;
    private FileStream? log;
    private long lastSeq = -1;
    private long lastCheckpointSeq = -1;
    private string? lastLineSha;
    private bool commandSinceReset; // false while the log holds only the header and checkpoints (after a reset): the checkpoint line then stands in for the last command line
    private string? pendingHeader;
    private SnapshotAssembly? assembly;

    private StandbyStore(string directory, string gameId, StandbyLimits limits)
    {
        this.directory = directory;
        this.gameId = gameId;
        this.limits = limits;
    }

    /// <summary>Why the last call returned <see cref="StandbyApply.Rejected"/>.</summary>
    public string? LastRejection { get; private set; }

    /// <summary>The game directory (<c>standby/&lt;gameId&gt;</c> under the data root).</summary>
    public string DirectoryPath => directory;

    /// <summary>The last log seq the store holds, or -1 when it holds no usable copy yet (subscribing with it asks the host for a snapshot).</summary>
    public long HaveSeq => lastSeq;

    /// <summary>sha256 (hex) of the last command line (of the checkpoint line while the log holds no command), or null. The host compares it with its own line at that seq to tell a continuing store from a diverged one.</summary>
    public string? HaveLineSha256 => lastLineSha;

    /// <summary>
    /// The standby directory of <paramref name="gameId"/> under <paramref name="dataRoot"/>, or null when the id is not a safe single path component (it comes from the host, which could try a path trick).
    /// </summary>
    public static string? DirectoryFor(string dataRoot, string gameId)
    {
        if (gameId.Length is 0 or > 64 || gameId[0] == '.' || !gameId.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')) return null;
        return Path.Combine(dataRoot, "standby", gameId);
    }

    /// <summary>Opens (creating the directory when needed) the standby store of <paramref name="gameId"/> and recovers its position from what is on disk; a torn last log line is cut off.</summary>
    public static StandbyStore Open(string dataRoot, string gameId, StandbyLimits? limits = null)
    {
        var path = DirectoryFor(dataRoot, gameId) ?? throw new ArgumentException("Not a usable game id", nameof(gameId));
        var store = new StandbyStore(path, gameId, limits ?? new StandbyLimits());
        Directory.CreateDirectory(path);
        store.Recover();
        return store;
    }

    private string PathOf(string name) => Path.Combine(directory, name);

    private void Recover()
    {
        lastSeq = -1;
        lastCheckpointSeq = -1;
        lastLineSha = null;
        commandSinceReset = false;
        if (!File.Exists(PathOf(LogName)) || !File.Exists(PathOf(MetaName)) || FileGameStore.ListSnapshots(directory).Count == 0) return;
        LogScan scan;
        try
        {
            scan = LogScan.Read(PathOf(LogName));
        }
        catch (GameStoreException)
        {
            return; // an unreadable log is no copy: the host sends a snapshot and the store starts over
        }
        if (scan.DroppedBytes.Length > 0)
        {
            using var cut = new FileStream(PathOf(LogName), FileMode.Open, FileAccess.Write, FileShare.Read);
            cut.SetLength(scan.ValidBytes);
        }
        if (FileGameStore.ListSnapshots(directory)[0].Seq > scan.LastSeq) return; // a snapshot the log does not reach (a restart that stopped before its first line): not a usable copy
        lastSeq = scan.LastSeq;
        foreach (var line in scan.Lines)
            if (line is CommandLogLine.Checkpoint checkpoint) lastCheckpointSeq = checkpoint.Seq;
        string? newestCheckpointLine = null;
        foreach (var line in Encoding.UTF8.GetString(File.ReadAllBytes(PathOf(LogName))).Split('\n').Reverse())
        {
            if (line.Length == 0) continue;
            if (line.StartsWith("{\"checkpoint\"", StringComparison.Ordinal))
            {
                newestCheckpointLine ??= line;
                continue;
            }
            if (line.StartsWith("{\"seq\"", StringComparison.Ordinal))
            {
                lastLineSha = LineHash(line);
                commandSinceReset = true;
            }
            break; // the header when the log holds no command
        }
        if (!commandSinceReset && newestCheckpointLine != null) lastLineSha = LineHash(newestCheckpointLine);
        log = OpenLog();
    }

    private FileStream OpenLog() => new(PathOf(LogName), FileMode.Open, FileAccess.Write, FileShare.Read, 1);

    // ------------------------------------------------------------------ the stream

    /// <summary>Takes <c>game.json</c> (and, for a restart from a snapshot, the log header). Refused when it is not the meta of this game.</summary>
    public StandbyApply Apply(StandbyMeta meta)
    {
        GameMeta parsed;
        try
        {
            parsed = UncivJson.Deserialize<GameMeta>(meta.Meta) ?? throw new JsonException("empty");
        }
        catch (JsonException)
        {
            return Reject("meta-invalid");
        }
        if (!string.Equals(parsed.GameId, gameId, StringComparison.Ordinal)) return Reject("meta-game");
        if (meta.Header != null)
        {
            try
            {
                if (meta.Header.Contains('\n') || UncivJson.Deserialize<CommandLogHeader>(meta.Header) == null) return Reject("header-invalid");
            }
            catch (JsonException)
            {
                return Reject("header-invalid");
            }
            pendingHeader = meta.Header;
        }
        FileGameStore.WriteAtomic(PathOf(MetaName), Encoding.UTF8.GetBytes(meta.Meta));
        return StandbyApply.Applied;
    }

    /// <summary>One chunk of a snapshot. Chunks must arrive in order and agree on seq, part count and hash; the file is stored only when it is complete, within the size limit and its sha256 matches.</summary>
    public StandbyApply Apply(StandbySnapshot part)
    {
        byte[] data;
        try
        {
            data = Convert.FromBase64String(part.Data);
        }
        catch (FormatException)
        {
            return RejectSnapshot("chunk-invalid");
        }
        if (part.Parts is < 1 or > 4096 || part.Part < 0 || part.Part >= part.Parts || part.Seq < 0 || data.Length > limits.SnapshotChunkBytes * 4 || part.Sha256.Length != 64) return RejectSnapshot("chunk-invalid");
        if (part.Part == 0)
        {
            assembly = new SnapshotAssembly(part.Seq, part.Parts, part.Sha256.ToLowerInvariant(), part.Reset);
        }
        else if (assembly == null || assembly.Seq != part.Seq || assembly.Parts != part.Parts || assembly.NextPart != part.Part
            || !string.Equals(assembly.Sha, part.Sha256, StringComparison.OrdinalIgnoreCase) || assembly.Reset != part.Reset)
        {
            return RejectSnapshot("chunk-order");
        }
        if (assembly!.Bytes.Length + data.Length > limits.MaxSnapshotBytes) return RejectSnapshot("snapshot-too-large");
        assembly.Bytes.Write(data);
        assembly.NextPart++;
        if (assembly.NextPart < assembly.Parts) return StandbyApply.Pending;

        var complete = assembly;
        assembly = null;
        if (!string.Equals(Sha256Hex(complete.Bytes.ToArray()), complete.Sha, StringComparison.Ordinal)) return Reject("snapshot-hash");
        return complete.Reset ? InstallReset(complete) : InstallAdditional(complete);
    }

    private StandbyApply InstallReset(SnapshotAssembly snapshot)
    {
        if (pendingHeader == null || !File.Exists(PathOf(MetaName))) return Reject("snapshot-without-header");
        // Snapshot first, then the log: a crash in between leaves the new snapshot with the old log, which the next subscribe finds inconsistent (its seq no longer matches) and restarts.
        FileGameStore.WriteAtomic(PathOf(SnapshotName(snapshot.Seq)), snapshot.Bytes.ToArray());
        log?.Dispose();
        log = null;
        FileGameStore.WriteAtomic(PathOf(LogName), Encoding.UTF8.GetBytes(pendingHeader + "\n"));
        log = OpenLog();
        foreach (var (seq, path) in FileGameStore.ListSnapshots(directory))
            if (seq != snapshot.Seq) File.Delete(path);
        pendingHeader = null;
        lastSeq = snapshot.Seq;
        lastCheckpointSeq = -1;
        lastLineSha = null;
        commandSinceReset = false;
        return StandbyApply.Applied;
    }

    private StandbyApply InstallAdditional(SnapshotAssembly snapshot)
    {
        if (log == null || snapshot.Seq > lastSeq) return Reject("snapshot-ahead-of-log"); // a snapshot of a state the log does not reach yet could not be replayed forward
        FileGameStore.WriteAtomic(PathOf(SnapshotName(snapshot.Seq)), snapshot.Bytes.ToArray());
        foreach (var (_, path) in FileGameStore.ListSnapshots(directory).Skip(KeepSnapshots)) File.Delete(path);
        return StandbyApply.Applied;
    }

    /// <summary>One verbatim log line. It must be a command of the next seq or a checkpoint of the current one; a repeat of what is already stored is accepted and ignored. Durable when this returns.</summary>
    public StandbyApply Apply(StandbyEntry entry)
    {
        if (log == null) return Reject("no-store");
        var line = entry.Line;
        if (line.Length is 0 || line.Length > limits.MaxLineChars || line.Contains('\n') || line.Contains('\r')) return Reject("line-invalid");
        CommandLogLine parsed;
        try
        {
            parsed = CommandLogReader.Read("{}\n" + line).Lines.SingleOrDefault() ?? throw new FormatException("blank");
        }
        catch (Exception e) when (e is CommandLogException or FormatException or JsonException or InvalidOperationException or OverflowException)
        {
            return Reject("line-invalid");
        }

        switch (parsed)
        {
            case CommandLogLine.Command command:
                if (command.Envelope.Seq != entry.Seq) return Reject("line-seq");
                if (entry.Seq <= lastSeq) return StandbyApply.Applied; // already stored
                if (entry.Seq != lastSeq + 1) return Reject("line-gap");
                Append(line);
                lastSeq = entry.Seq;
                lastLineSha = Sha256Hex(Encoding.UTF8.GetBytes(line));
                commandSinceReset = true;
                return StandbyApply.Applied;
            case CommandLogLine.Checkpoint checkpoint:
                if (checkpoint.Seq != entry.Seq) return Reject("line-seq");
                if (checkpoint.Seq == lastCheckpointSeq && checkpoint.Seq == lastSeq) return StandbyApply.Applied;
                if (checkpoint.Seq != lastSeq) return checkpoint.Seq < lastSeq ? StandbyApply.Applied : Reject("line-gap");
                Append(line);
                lastCheckpointSeq = checkpoint.Seq;
                if (!commandSinceReset) lastLineSha = LineHash(line);
                return StandbyApply.Applied;
            default:
                return Reject("line-invalid");
        }
    }

    private void Append(string line)
    {
        var stream = log!;
        var before = stream.Length;
        try
        {
            stream.Seek(0, SeekOrigin.End);
            stream.Write(Encoding.UTF8.GetBytes(line + "\n"));
            stream.Flush(true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            try { stream.SetLength(before); }
            catch (Exception undo) when (undo is IOException or UnauthorizedAccessException) { /* Recover cuts a torn line on the next open */ }
            throw;
        }
    }

    private StandbyApply Reject(string reason)
    {
        LastRejection = reason;
        return StandbyApply.Rejected;
    }

    private StandbyApply RejectSnapshot(string reason)
    {
        assembly = null;
        return Reject(reason);
    }

    private static string SnapshotName(long seq) => "snapshot-" + seq.ToString("D12", CultureInfo.InvariantCulture) + ".json.gz";

    private static string Sha256Hex(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    /// <summary>The hash a host puts in <see cref="StandbySnapshot.Sha256"/>.</summary>
    public static string HashOf(byte[] bytes) => Sha256Hex(bytes);

    /// <summary>sha256 (hex) of a log line as <see cref="HaveLineSha256"/> computes it.</summary>
    public static string LineHash(string line) => Sha256Hex(Encoding.UTF8.GetBytes(line));

    public void Dispose()
    {
        log?.Dispose();
        log = null;
    }

    private sealed class SnapshotAssembly(long seq, int parts, string sha, bool reset)
    {
        public long Seq { get; } = seq;
        public int Parts { get; } = parts;
        public string Sha { get; } = sha;
        public bool Reset { get; } = reset;
        public int NextPart { get; set; }
        public MemoryStream Bytes { get; } = new();
    }
}
