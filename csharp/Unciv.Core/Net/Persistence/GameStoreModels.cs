using System.Text.Json;
using System.Text.Json.Serialization;
using Unciv.Commands;
using Unciv.Logic;

namespace Unciv.Net.Persistence;

/// <summary>The player a civ slot is bound to (SERVER.md 5.1 <c>game.json</c>).</summary>
public sealed record SlotBinding
{
    [JsonPropertyName("playerKey")]
    public string PlayerKey { get; init; } = "";

    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = "";
}

/// <summary>
/// <c>game.json</c>: what the host remembers about a game that is not part of <see cref="GameInfo"/>. The directory is portable: another host that opens it keeps the
/// bindings, so the clients reconnect with the same keys. The host secret (T230) is deliberately not here, see <see cref="IGameStore.HostSecret"/>.
/// </summary>
public sealed record GameMeta
{
    [JsonPropertyName("gameId")]
    public string GameId { get; init; } = "";

    /// <summary>The lobby settings as the lobby layer wrote them (opaque here).</summary>
    [JsonPropertyName("lobbySettings")]
    public JsonElement? LobbySettings { get; init; }

    /// <summary>Civ id to the player key bound to it, in ordinal order.</summary>
    [JsonPropertyName("slots")]
    public SortedDictionary<string, SlotBinding> Slots { get; init; } = new(StringComparer.Ordinal);

    [JsonPropertyName("hostFingerprint")]
    public string HostFingerprint { get; init; } = "";

    /// <summary>The join password hash (never the password), or null for an open game.</summary>
    [JsonPropertyName("passwordHash")]
    public string? PasswordHash { get; init; }

    [JsonPropertyName("turnPolicy")]
    public string TurnPolicy { get; init; } = "Simultaneous";
}

/// <summary>The store cannot give the game back (no readable game.json, header or snapshot that can be trusted) or was misused.</summary>
public sealed class GameStoreException(string message) : Exception(message);

/// <summary>Tunables of a <see cref="FileGameStore"/>.</summary>
public sealed record StoreOptions
{
    /// <summary>A snapshot is taken by <see cref="IGameStore.MaybeSnapshot"/> once this many turns passed since the last one.</summary>
    public int SnapshotEveryTurns { get; init; } = 5;

    /// <summary>Snapshots kept after a new one is written (at least 1).</summary>
    public int KeepSnapshots { get; init; } = 3;

    /// <summary>How many recent commands <see cref="IGameStore.Resume"/> can replay to a reconnecting client before it falls back to a full snapshot.</summary>
    public int RingSize { get; init; } = 512;

    /// <summary>How many <c>(actor, clientCmdId)</c> pairs the durable dedup table remembers.</summary>
    public int DedupSize { get; init; } = 8192;

    /// <summary>A snapshot rotates the log (older lines go to <c>history/</c>) once the log is larger than this many bytes; 0 never rotates.</summary>
    public long RotateLogBytes { get; init; } = 4 * 1024 * 1024;
}

/// <summary>How <see cref="FileGameStore.Open"/> got the game back.</summary>
public enum RecoveryOutcome
{
    /// <summary>Latest usable snapshot plus the replayed log tail, every checkpoint verified.</summary>
    Recovered,

    /// <summary>A checkpoint mismatch or an invalid command: the last good snapshot was loaded and the rest of the log was set aside. Tell the players.</summary>
    FellBackToSnapshot,

    /// <summary>No snapshot was usable; the game was rebuilt from the log header.</summary>
    ReplayedFromStart,
}

/// <summary>What recovery did, for the host to log and to tell the lobby about.</summary>
public sealed record RecoveryReport(
    RecoveryOutcome Outcome,
    bool WasCleanShutdown,
    long SnapshotSeq,
    long LastSeq,
    long ReplayedCommands,
    int CheckpointsVerified,
    int DroppedLogLines,
    long? DivergedAtSeq,
    IReadOnlyList<string> Notes);

public enum ResumeKind
{
    /// <summary>The client already has everything.</summary>
    UpToDate,

    /// <summary>Send the commands after the client's last seq (through the visibility filter).</summary>
    Deltas,

    /// <summary>The gap is not covered: send a fresh snapshot.</summary>
    Snapshot,
}

public sealed record ResumePlan(ResumeKind Kind, IReadOnlyList<CommandEnvelope> Commands);

/// <summary>A command the log holds for a client command id: the client's retry after a lost ack must not apply it again.</summary>
public sealed record LoggedOutcome(long Seq);
