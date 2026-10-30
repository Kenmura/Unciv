using System.Text.Json.Serialization;

namespace Unciv.Net.Standby;

/// <summary>The <see cref="StandbyStatus.State"/> values of a backup host designation (T281, replication states T272).</summary>
public static class StandbyStates
{
    /// <summary>No backup host designated.</summary>
    public const string None = "none";

    /// <summary>A player was offered the role and has not answered.</summary>
    public const string Offered = "offered";

    /// <summary>The candidate accepted but the endpoint probe has not succeeded (never run, rate-limited, or failed): promotion would not reach the clients.</summary>
    public const string Unreachable = "unreachable";

    /// <summary>Accepted and the host saw the announced certificate at the announced endpoint. Once the candidate subscribed, <c>ready</c> also means its store caught up with the host.</summary>
    public const string Ready = "ready";

    /// <summary>T272: reachable and subscribed, the host is sending the snapshot or the log tail the standby store is missing.</summary>
    public const string Syncing = "syncing";

    /// <summary>T272: reachable and subscribed but too far behind (too many unacked entries, no ack for too long, or a backed-up connection): entries are dropped and a snapshot resync follows once it catches up.</summary>
    public const string Lagging = "lagging";

    /// <summary>T292: the designated successor was subscribed but the stream ended because of a fault (a protocol violation, the host's store failing, a snapshot over the limit). Nothing is replicated until the successor subscribes again.</summary>
    public const string Stopped = "stopped";
}

/// <summary>The fixed trust notice every player sees before joining (design/HOST-MIGRATION.md section 1).</summary>
public static class StandbyNotice
{
    /// <summary>Before joining nobody is named yet (the Welcome is unauthenticated).</summary>
    public const string Generic = "The backup host holds a complete copy of this game, including what you cannot see. They are trusted like the host.";

    public static string For(string playerName) =>
        $"{playerName} holds a complete copy of this game, including what you cannot see. They are trusted like the host.";
}

/// <summary>What <c>game.json</c> keeps of a designation (<see cref="Persistence.GameMeta.Standby"/>), so a restarted host still knows who it offered the role to and what they announced. Whether the endpoint was reachable is not stored: it is probed again.</summary>
public sealed record StandbyRecord
{
    [JsonPropertyName("keyId")]
    public string KeyId { get; init; } = "";

    [JsonPropertyName("accepted")]
    public bool Accepted { get; init; }

    /// <summary>The <c>host:port</c> endpoints the candidate announced (only meaningful once accepted).</summary>
    [JsonPropertyName("endpoints")]
    public List<string> Endpoints { get; init; } = [];

    [JsonPropertyName("fingerprint")]
    public string Fingerprint { get; init; } = "";
}

/// <summary>Tunables of <see cref="StandbyCoordinator"/>; the defaults are the design's.</summary>
public sealed record StandbyLimits
{
    /// <summary>At most one endpoint probe per this long, whoever asks (the endpoints are attacker-chosen).</summary>
    public long ProbeMinIntervalMillis { get; init; } = 60_000;

    public TimeSpan ProbeTimeout { get; init; } = TimeSpan.FromSeconds(3);

    public int MinPort { get; init; } = 1024;

    public int MaxEndpoints { get; init; } = 4;

    public int MaxEndpointLength { get; init; } = 253;

    /// <summary>T272: unacked log entries beyond which the standby is lagging (design: 2000).</summary>
    public int MaxLagEntries { get; init; } = 2000;

    /// <summary>T272: no ack for this long while entries are outstanding marks the standby lagging (design: 30 s).</summary>
    public long MaxLagMillis { get; init; } = 30_000;

    /// <summary>Least time between two subscribes of the designated key (a subscribe can cost a snapshot read and send).</summary>
    public long MinSubscribeMillis { get; init; } = 1_000;

    /// <summary>T272: raw bytes of one <c>StandbySnapshot</c> chunk (well under the frame limit once base64 encoded).</summary>
    public int SnapshotChunkBytes { get; init; } = 256 * 1024;

    /// <summary>T272: largest snapshot a successor stores (and a host sends); bounds the successor's disk and memory use.</summary>
    public long MaxSnapshotBytes { get; init; } = 64L * 1024 * 1024;

    /// <summary>T272: log lines the host sends per step before it checks the connection's backlog again.</summary>
    public int BatchLines { get; init; } = 64;

    /// <summary>T272: the stream stops sending while this many frames wait in the session's outbound queue, so replication never fills the queue a slow-reader disconnect is judged on.</summary>
    public int MaxQueuedFrames { get; init; } = 96;

    /// <summary>T292: the successor's <c>commands.log</c> is rotated (the old log dropped, not kept) when a snapshot arrives and the log is larger than this, as the host does with its own log. 0 never rotates.</summary>
    public long RotateLogBytes { get; init; } = 4L * 1024 * 1024;

    /// <summary>T292: at most this many games keep a copy under <c>standby/</c>; opening one more removes the least recently written of the others (a game that ended long ago).</summary>
    public int MaxStandbyGames { get; init; } = 8;

    /// <summary>T272: a successor refuses a single command or checkpoint line longer than this.</summary>
    public int MaxLineChars { get; init; } = 4 * 1024 * 1024;
}

/// <summary>Dials one endpoint for the standby probe: a TLS handshake and nothing else (no payload is sent, so it is no amplifier). Returns the fingerprint the listener presented, or null when nothing usable answered.</summary>
public interface IEndpointProber
{
    Task<string?> PresentedFingerprintAsync(string host, int port, TimeSpan timeout, CancellationToken cancellationToken);
}

/// <summary>A prober for hosts without a network layer (and tests that must not dial): nothing is ever reachable.</summary>
public sealed class NullEndpointProber : IEndpointProber
{
    public Task<string?> PresentedFingerprintAsync(string host, int port, TimeSpan timeout, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
}
