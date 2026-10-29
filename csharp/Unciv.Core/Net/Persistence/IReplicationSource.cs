namespace Unciv.Net.Persistence;

/// <summary>One line of <c>commands.log</c> as the file holds it (no newline): a command (<c>Seq</c> is its seq) or a checkpoint (<c>Seq</c> is the seq it follows).</summary>
public sealed record ReplicationLine(long Seq, bool IsCheckpoint, string Text);

/// <summary>A run of log lines and where the next read continues (<c>NextOffset</c> is only valid while <c>Generation</c> is the store's <see cref="IReplicationSource.LogGeneration"/>).</summary>
public sealed record ReplicationBatch(IReadOnlyList<ReplicationLine> Lines, long NextOffset, long Generation);

/// <summary>What a store write changed (<see cref="IReplicationSource.Changed"/>).</summary>
public enum ReplicationChange
{
    /// <summary>A command or checkpoint line was appended.</summary>
    Log,

    /// <summary>A snapshot file was written.</summary>
    Snapshot,

    /// <summary><c>game.json</c> was written.</summary>
    Meta,
}

/// <summary>
/// T272: what the host's standby replication reads from a store (design/HOST-MIGRATION.md section 1): the log lines verbatim, the newest snapshot file, <c>game.json</c>. A read never changes the store.
/// <see cref="Changed"/> fires after every durable write (a log line, a snapshot, <c>game.json</c>), inside the store's lock: a handler must only signal, never read the store.
/// </summary>
public interface IReplicationSource
{
    event Action<ReplicationChange>? Changed;

    /// <summary>Incremented whenever the log file is rewritten (rotation), which invalidates a saved read offset.</summary>
    long LogGeneration { get; }

    string ReadHeaderLine();

    /// <summary>The newest snapshot file and its seq, or null when the store holds none.</summary>
    (long Seq, byte[] Bytes)? ReadNewestSnapshot();

    /// <summary>
    /// Up to <paramref name="maxLines"/> lines of the log: commands with a seq above <paramref name="afterSeq"/> and checkpoints at or above it, in file order. Continues at <paramref name="offset"/>
    /// when it was returned for the current <paramref name="generation"/> (0 starts at the top). Only complete lines are returned, so a line being written is picked up by the next read.
    /// </summary>
    ReplicationBatch ReadReplicationBatch(long afterSeq, long offset, long generation, int maxLines);
}
