using System.Text.Json;

namespace Unciv.Net;

/// <summary>Stable protocol-level error codes carried by <see cref="Error"/>.</summary>
public static class ErrorCodes
{
    public const string FrameTooLarge = "frame-too-large";
    public const string Malformed = "malformed";
    public const string UnknownType = "unknown-type";
    public const string InvalidMessage = "invalid-message";
}

/// <summary>Base of every wire message. The JSON frame is the record's fields plus a <c>type</c> discriminator (see <see cref="ProtocolCodec"/>).</summary>
public abstract record Message;

// ---------------------------------------------------------------- client to host

public sealed record Hello : Message
{
    public required int Protocol { get; init; }
    public required string ClientVersion { get; init; }
    public required string RulesetHash { get; init; }
    public required List<string> ModList { get; init; }
}

public sealed record Join : Message
{
    public string? Password { get; init; }
    public required string PlayerKey { get; init; }
    public required string DisplayName { get; init; }
    public string? InviteToken { get; init; }
}

public sealed record Auth : Message
{
    public required string Proof { get; init; }
}

public sealed record Resume : Message
{
    public required string GameId { get; init; }
    public required long LastSeq { get; init; }
    public required string LastViewHash { get; init; }
}

public sealed record LobbySet : Message
{
    public required int Slot { get; init; }
    public string? Nation { get; init; }
    public string? Color { get; init; }
    public required bool Ready { get; init; }
}

public sealed record LobbyChat : Message
{
    public required string Text { get; init; }
    public string? Sender { get; init; }
}

/// <summary>A game command: the <c>CommandEnvelope</c> minus Seq/ActorId. <c>CommandType</c> is the command registry name (the JSON key is not <c>type</c>, which is the message discriminator).</summary>
public sealed record Command : Message
{
    public required string ClientCmdId { get; init; }
    public required string CommandType { get; init; }
    public required JsonElement Payload { get; init; }
}

/// <summary>
/// A read-only question about the game from the session's own point of view (T223). <c>QueryType</c> is the query registry name (not <c>type</c>, which is the
/// message discriminator); the viewer is the civ the session is bound to, never read from the frame. Answered by exactly one <see cref="QueryResult"/>.
/// </summary>
public sealed record Query : Message
{
    /// <summary>Client-chosen correlation id, echoed by the answer.</summary>
    public required long QueryId { get; init; }
    public required string QueryType { get; init; }
    public required JsonElement Payload { get; init; }
}

public sealed record ResyncRequest : Message;

public sealed record Leave : Message;

public sealed record Ping : Message;

// ---------------------------------------------------------------- host to client

public sealed record Welcome : Message
{
    public required int Protocol { get; init; }
    public required string SessionId { get; init; }
    public required string Challenge { get; init; }
    public required string HostKeyFingerprint { get; init; }
    public required bool RequiresPassword { get; init; }
}

public sealed record JoinOk : Message;

public sealed record JoinRefused : Message
{
    public required string Code { get; init; }
}

public sealed record LobbySlot
{
    public required int Slot { get; init; }
    public string? PlayerName { get; init; }
    public string? Nation { get; init; }
    public string? Color { get; init; }
    public required bool Ready { get; init; }
    public string? Kind { get; init; }
    public string? PlayerKeyId { get; init; }
    public bool? Connected { get; init; }
}

public sealed record LobbyState : Message
{
    public required JsonElement Settings { get; init; }
    public required List<LobbySlot> Slots { get; init; }
    public required string HostName { get; init; }
}

public sealed record GameStarting : Message;

public sealed record TurnStatus : Message
{
    public required int Turn { get; init; }
    public required List<string> Ended { get; init; }
    /// <summary>Host-supplied absolute deadline (Unix ms), or null when no timer runs.</summary>
    public long? Deadline { get; init; }
    public required string Phase { get; init; }
}

/// <summary>Client-specific full state. The collections hold opaque DTOs, typed later by the visibility-filter ticket.</summary>
public sealed record Snapshot : Message
{
    public required long Seq { get; init; }
    public required int Turn { get; init; }
    public required string Phase { get; init; }
    public required TurnStatus TurnStatus { get; init; }
    public required string You { get; init; }
    public required List<JsonElement> Civs { get; init; }
    public required List<JsonElement> Tiles { get; init; }
    public required List<JsonElement> Cities { get; init; }
    public required List<JsonElement> Units { get; init; }
    public required List<JsonElement> Diplomacy { get; init; }
    public required List<JsonElement> PendingTrades { get; init; }
    public required List<JsonElement> Notifications { get; init; }

    /// <summary>T224: the recipient's own popup alerts and pending decisions (empty for a message from a host that predates them).</summary>
    public List<JsonElement> PopupAlerts { get; init; } = [];
    public List<JsonElement> PendingDecisions { get; init; } = [];
    public required string ViewHash { get; init; }
}

public sealed record ViewDelta : Message
{
    public required long Seq { get; init; }

    /// <summary>The recipient's filtered engine events, for animation and sound. T224: they are NOT what updates the mirror, <see cref="Diff"/> is.</summary>
    public required List<JsonElement> Events { get; init; }

    /// <summary>T224: the change from the recipient's previous filtered snapshot to this one (a serialized <c>SnapshotDiff</c>), null when the host sends events only.</summary>
    public JsonElement? Diff { get; init; }

    /// <summary>T224: the <c>ViewHash</c> the mirror must have after applying <see cref="Diff"/>.</summary>
    public string? ViewHash { get; init; }
}

public sealed record CommandResult : Message
{
    public required string ClientCmdId { get; init; }
    public required bool Accepted { get; init; }
    public string? Code { get; init; }
    public long? Seq { get; init; }
}

/// <summary>The answer to a <see cref="Query"/>. A failure carries only a coarse <c>Code</c> (see <c>QueryWireCodes</c>), never free text, so it cannot reveal hidden state.</summary>
public sealed record QueryResult : Message
{
    public required long QueryId { get; init; }
    public required bool Ok { get; init; }
    public string? Code { get; init; }
    public JsonElement? Payload { get; init; }
}

public sealed record HostClosing : Message;

public sealed record Error : Message
{
    public required string Code { get; init; }
    public string? Detail { get; init; }
}

public sealed record Pong : Message;
