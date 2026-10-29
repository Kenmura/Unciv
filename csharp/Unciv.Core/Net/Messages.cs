using System.Text.Json;

namespace Unciv.Net;

/// <summary>Stable protocol-level error codes carried by <see cref="Error"/>.</summary>
public static class ErrorCodes
{
    public const string FrameTooLarge = "frame-too-large";
    public const string Malformed = "malformed";
    public const string UnknownType = "unknown-type";
    public const string InvalidMessage = "invalid-message";

    /// <summary>T247: a mod file request the host will not serve (not listed, over a cap, changed on disk, or no manifest was sent). Carries no detail.</summary>
    public const string ModFileRefused = "mod-file-refused";

    /// <summary>T269: the peer has seen a newer host epoch than this host's (or the host is older than the client's stored epoch): another host took over, never silently accepted.</summary>
    public const string StaleEpoch = "stale-epoch";
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

    /// <summary>T247: true when this client can take the host's mod files. A ruleset mismatch then continues the handshake into a <see cref="ModManifest"/> instead of an immediate refusal. Absent (null) for a client that cannot.</summary>
    public bool? ModSync { get; init; }

    /// <summary>T247: with <see cref="ModSync"/>, the checksums (<c>ModEntry.Checksum</c>) of the mods installed on the client by name. A mod of the host that is missing or different here triggers the mod sync even when the ruleset hash is equal (the hash only covers rule names, not their values).</summary>
    public Dictionary<string, string>? ModChecksums { get; init; }

    /// <summary>T269: the game the client last played here (reconnect only). The host compares <see cref="KnownEpoch"/> only when this is its own game id; absent for a first join.</summary>
    public string? GameId { get; init; }

    /// <summary>T269: the highest host epoch the client has seen for <see cref="GameId"/>. A host with a lower epoch is superseded and answers <c>stale-epoch</c>.</summary>
    public int? KnownEpoch { get; init; }

    /// <summary>T269: true when this client understands host migration (<c>HostSuccessor</c>, <c>HostHandoff</c>). A host sends the plain <see cref="HostClosing"/> to a client without it.</summary>
    public bool? HostMigration { get; init; }
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

/// <summary>
/// C090: a map ping (look here, danger, help) on a tile, for the sender's allies. Not an engine command: it is not logged, hashed or replayed, and it changes no state. The host relays it as a
/// <see cref="MapPingNotice"/> to the sender and the allies (defensive-pact partners) who already know the tile; the sender is never read from the frame.
/// </summary>
public sealed record MapPing : Message
{
    public required int X { get; init; }
    public required int Y { get; init; }
    public required string Kind { get; init; }
}

/// <summary>T247: asks for one ruleset file of a mod listed in the <see cref="ModManifest"/> of this session. Only answered after the manifest, only for a listed file.</summary>
public sealed record ModFileRequest : Message
{
    public required string Mod { get; init; }
    public required string Path { get; init; }
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

    /// <summary>T269: the host epoch of this game (0 = original host). Absent from a host that predates host migration, which a client reads as 0.</summary>
    public int? Epoch { get; init; }
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

/// <summary>C090: a relayed <see cref="MapPing"/>. <c>Sender</c> is the display name the host has bound to the sending civ.</summary>
public sealed record MapPingNotice : Message
{
    public required int X { get; init; }
    public required int Y { get; init; }
    public required string Kind { get; init; }
    public required string Sender { get; init; }
}

/// <summary>One ruleset data file of a mod: a bare file name in the mod's <c>jsons</c> folder, its size in bytes and the SHA-256 (lower-case hex) of its UTF-8 content.</summary>
public sealed record ModFileEntry
{
    public required string Path { get; init; }
    public required long Size { get; init; }
    public required string Sha256 { get; init; }
}

/// <summary>One mod of the manifest: its name, its files (ordinal by path) and <c>Checksum</c>, the SHA-256 over the file list (see <c>ModManifestBuilder.ChecksumOf</c>).</summary>
public sealed record ModEntry
{
    public required string Name { get; init; }
    public required string Checksum { get; init; }
    public required List<ModFileEntry> Files { get; init; }
}

/// <summary>
/// T247: the host's answer to an authenticated join of a client whose rules differ, when the client said it can sync mods. The lobby's base ruleset and mod list as the host plays them, the host's
/// combined ruleset hash, and the downloadable mods (those that exist as folders on the host; built-in rulesets are never transferred). Followed by the client's <see cref="ModFileRequest"/>s.
/// </summary>
public sealed record ModManifest : Message
{
    public required string BaseRuleset { get; init; }
    public required List<string> Mods { get; init; }
    public required string RulesetHash { get; init; }
    public required List<ModEntry> Entries { get; init; }
}

/// <summary>T247: the answer to a <see cref="ModFileRequest"/>: the file's bytes, base64.</summary>
public sealed record ModFileData : Message
{
    public required string Mod { get; init; }
    public required string Path { get; init; }
    public required string Data { get; init; }
}

public sealed record HostClosing : Message;

public sealed record Error : Message
{
    public required string Code { get; init; }
    public string? Detail { get; init; }
}

public sealed record Pong : Message;

/// <summary>C090: the ping kinds the host relays (any other kind is dropped).</summary>
public static class MapPingKinds
{
    public const string Look = "Look";
    public const string Danger = "Danger";
    public const string Help = "Help";

    public static IReadOnlyList<string> All { get; } = [Look, Danger, Help];
}
