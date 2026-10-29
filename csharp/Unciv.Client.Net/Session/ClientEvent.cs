using System.Text.Json;
using Unciv.Net;
using Unciv.Net.Visibility;

namespace Unciv.Client.Net.Session;

/// <summary>Where a <see cref="ClientSession"/> is (SERVER.md section 7).</summary>
public enum ClientSessionState
{
    Disconnected,
    Joining,
    Lobby,
    /// <summary>Joined and waiting for the first (or a resumed) snapshot.</summary>
    Syncing,
    InGame,
    /// <summary>The connection was lost (or the host is closing); the session retries with the same key.</summary>
    Reconnecting
}

/// <summary>Client-side refusal codes (never sent by a host); host refusals use <c>JoinRefusedCodes</c>.</summary>
public static class ClientCodes
{
    public const string HostKeyChanged = "host-key-changed";

    /// <summary>T269: the host's epoch is below the one this client has seen (or the host reported <c>stale-epoch</c>): another host took over.</summary>
    public const string StaleEpoch = ErrorCodes.StaleEpoch;
    public const string HostKeyMismatch = "host-key-mismatch";
    public const string HostUntrusted = "host-untrusted";
    public const string ConnectFailed = "connect-failed";
    public const string ConnectionLost = "connection-lost";
    public const string ProtocolMismatch = "protocol-mismatch";
    public const string SessionClosed = "session-closed";
    public const string TooManyPending = "too-many-pending";
    public const string QueryTimeout = "query-timeout";
    public const string MalformedCommand = "malformed-command";
    public const string NotInGame = "not-in-game";
    public const string HandshakeTimeout = "handshake-timeout";
    public const string SnapshotRejected = "snapshot-rejected";
    public const string InternalError = "internal-error";
}

/// <summary>The final answer to one submitted command. <see cref="Code"/> is a host code or a <see cref="ClientCodes"/> value, never free text.</summary>
public sealed record CommandOutcome(string ClientCmdId, bool Accepted, string? Code, long? Seq);

/// <summary>What the UI layers consume (C004/C005 read these, never the wire messages). Delivered in order through <see cref="ClientSession.Events"/>.</summary>
public abstract record ClientEvent;

public sealed record StateChanged(ClientSessionState State) : ClientEvent;

public sealed record LobbyUpdated(LobbyState Lobby) : ClientEvent;

/// <summary>C090: an ally's map ping (or the player's own, echoed by the host).</summary>
public sealed record MapPingReceived(MapPingNotice Ping) : ClientEvent;

public sealed record LobbyChatReceived(string? Sender, string Text) : ClientEvent;

public sealed record GameStarted : ClientEvent;

/// <summary>The join (or a later seat check) was refused; the session is over. <see cref="Code"/> is a <c>JoinRefusedCodes</c> or <see cref="ClientCodes"/> value.</summary>
public sealed record JoinRefusedEvent(string Code) : ClientEvent;

/// <summary>The mirror was replaced by a full snapshot (join, resync, resume).</summary>
public sealed record SnapshotReplaced(ClientSnapshot Snapshot) : ClientEvent;

/// <summary>One delta was applied: <see cref="Events"/> are the viewer-visible engine events (animation, sound), <see cref="Snapshot"/> the new mirror.</summary>
public sealed record DeltaApplied(long Seq, IReadOnlyList<JsonElement> Events, ClientSnapshot Snapshot) : ClientEvent;

public sealed record TurnStatusChanged(TurnStatus Status) : ClientEvent;

/// <summary>The mirror may be wrong (gap, hash mismatch, malformed diff, reconnect); the session has asked for a full snapshot.</summary>
public sealed record Desynchronized(string Reason) : ClientEvent;

public sealed record CommandCompleted(CommandOutcome Outcome) : ClientEvent;

/// <summary>The host announced it is shutting down; the session waits and reconnects.</summary>
public sealed record HostClosed : ClientEvent;

public sealed record ProtocolError(string Code) : ClientEvent;

/// <summary>The session ended for good (left, refused, or reconnect attempts exhausted).</summary>
public sealed record SessionEnded(string Reason) : ClientEvent;
