using Unciv.Commands;
using Unciv.Logic;

namespace Unciv.Net.Persistence;

/// <summary>
/// The host's durable record of one game (SERVER.md 5): an append-only command log, snapshots, game.json. A command is durable when
/// <see cref="Append"/> returns, so the host appends before it acknowledges.
/// </summary>
public interface IGameStore : IDisposable
{
    string GameId { get; }

    /// <summary>The seq of the last command in the log.</summary>
    long LastSeq { get; }

    /// <summary>The host secret (T230). Kept beside the game, never in <c>game.json</c>, a snapshot or the log, so nothing sent to a client can carry it.</summary>
    string? HostSecret { get; set; }

    GameMeta ReadMeta();

    void WriteMeta(GameMeta meta);

    /// <summary>Appends and fsyncs one accepted command (and the client command id it answered, for durable dedup). Seqs must be consecutive.</summary>
    void Append(CommandEnvelope envelope, string? clientCmdId = null);

    /// <summary>Appends every command <paramref name="session"/> accepted that the log does not hold yet (server commands, which have no client command id).</summary>
    void Sync(GameSession session);

    /// <summary>The full state hash after command <paramref name="seq"/> (the writer's checkpoint line).</summary>
    void AppendCheckpoint(long seq, int turn, ulong fullHash);

    /// <summary>Writes a snapshot of <paramref name="game"/> if it is a safe moment; false when it is not (nothing is written).</summary>
    bool TrySnapshot(GameInfo game);

    /// <summary>Takes a snapshot when the cadence says so (see <see cref="StoreOptions.SnapshotEveryTurns"/>).</summary>
    bool MaybeSnapshot(GameInfo game);

    /// <summary>Clean shutdown: snapshots (when safe), then writes the clean marker. The store is unusable afterwards.</summary>
    void CloseCleanly(GameInfo game);

    /// <summary>The seq a client command id was applied at, if this log has it (survives a restart).</summary>
    LoggedOutcome? FindOutcome(string actor, string clientCmdId);

    /// <summary>What a client that reconnects with <paramref name="lastSeq"/> needs.</summary>
    ResumePlan Resume(long lastSeq);
}
