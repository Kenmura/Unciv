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

    /// <summary>T281: a standby message from a key that is not the designated, offered backup host (or no backup host is designated). One code for every case, so it is no oracle for who is designated.</summary>
    public const string NotDesignated = "not-designated";

    /// <summary>T281: the designated key sent an unusable endpoint list or port.</summary>
    public const string InvalidEndpoint = "invalid-endpoint";

    /// <summary>T281: the endpoint probe ran too recently (at most one per minute).</summary>
    public const string ProbeRateLimited = "probe-rate-limited";

    /// <summary>T281: the probe found nothing listening (closed port, timeout, no TLS).</summary>
    public const string ProbeFailed = "probe-failed";

    /// <summary>T281: the probe reached a listener whose certificate is not the announced fingerprint.</summary>
    public const string ProbeFingerprint = "probe-fingerprint";

    /// <summary>T272: the designated candidate broke the replication stream rules (an ack going backwards or beyond the host's log, a subscription for another game): the subscription is closed, no state flows until it subscribes again.</summary>
    public const string StandbyViolation = "standby-protocol";

    /// <summary>T282: a standby listener (a backup host that has not taken over) refuses everything but <c>Hello</c>: this endpoint is not a host yet.</summary>
    public const string NotHostYet = "not-host-yet";
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

    /// <summary>
    /// T273: proof that a promotion to <see cref="KnownEpoch"/> really happened (signed by the successor's seat key, see <see cref="PromotionProof"/>). A host persists "superseded" only for a claim it can verify;
    /// a bare <see cref="KnownEpoch"/> from a seat holder only makes that one connection refuse (a forged epoch can no longer fence a host for good).
    /// </summary>
    public PromotionProof? EpochProof { get; init; }
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

    /// <summary>T281: the name-free trust notice, present while the host intends a backup host, so a client can show it before it sends <c>Join</c> (the player is named in <c>LobbyState.BackupHostNotice</c> after joining).</summary>
    public string? BackupHostNotice { get; init; }

    /// <summary>T273: set by a host that was promoted from a standby (epoch above 0): the successor's signed statement that it took this game over at <see cref="Epoch"/> with this host key. Clients redirected by a <see cref="HostHandoff"/> require it.</summary>
    public PromotionProof? Promotion { get; init; }

    /// <summary>T282: true when this endpoint is a backup host's standby listener, not a host: it answers <c>Hello</c> so the host's reachability probe can tell it from a closed port, and refuses everything else with <c>not-host-yet</c>. Absent from a real host. A client that sees it ends the attempt.</summary>
    public bool? Standby { get; init; }
}

public sealed record JoinOk : Message
{
    /// <summary>T281: the current backup-host announcement (public information: an address and a fingerprint), absent while no backup host is designated.</summary>
    public HostSuccessor? Successor { get; init; }
}

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

    /// <summary>T281: the slot index of the designated backup host (absent: none).</summary>
    public int? BackupHost { get; init; }

    /// <summary>T281: the <see cref="Unciv.Net.Standby.StandbyStates"/> of that designation (absent while none).</summary>
    public string? BackupHostState { get; init; }

    /// <summary>T281: the fixed trust notice naming the backup host ("X holds a complete copy of this game..."), present while one is designated. Part of the message so a player cannot miss it.</summary>
    public string? BackupHostNotice { get; init; }
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

    /// <summary>T273: true when this snapshot answers a <see cref="Resume"/> whose <c>LastSeq</c> was ahead of the host (the host changed and some acknowledged commands were never replicated): the mirror is replaced and the player is told.</summary>
    public bool? RolledBack { get; init; }

    /// <summary>T273: with <see cref="RolledBack"/>, how many commands the client had applied that this host does not have.</summary>
    public long? LostCommands { get; init; }

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

/// <summary>
/// T273, design/HOST-MIGRATION.md section 2 (graceful hand-off): the host is quitting cleanly and the designated successor holds every command up to <c>FinalSeq</c>.
/// Sent instead of <see cref="HostClosing"/>, on the authenticated session, to the successor and to every client that advertised <c>Hello.HostMigration</c>. <c>Epoch</c> is the new host's epoch (the old one plus one),
/// <c>GameId</c> the game that is handed over, <c>Successor</c> the endpoints and fingerprint announced earlier (a client accepts it only when it equals its own announcement), <c>FinalHash</c> the full state hash (<c>ReplayVerifier.FullHash</c>, hex) of the game at
/// <c>FinalSeq</c> that the promoted game must reproduce, and <c>DepartedKeyId</c> the key of the quitting host's own player seat (absent for a dedicated host), which the new host treats as a clean leave.
/// </summary>
public sealed record HostHandoff : Message
{
    public required string GameId { get; init; }
    public required int Epoch { get; init; }
    public required SuccessorInfo Successor { get; init; }
    public required long FinalSeq { get; init; }
    public required string FinalHash { get; init; }
    public string? DepartedKeyId { get; init; }
}

/// <summary>
/// T273: the successor's statement that it took game <c>GameId</c> over at <c>Epoch</c>, with host key <c>Fingerprint</c>, signed with its seat key <c>KeyId</c> (<see cref="Unciv.Net.Identity.PlayerKeys.VerifyPromotion"/>).
/// Anyone can check the signature, but only the key the old host designated can be accepted as the signer, so a seat holder cannot invent a promotion.
/// </summary>
public sealed record PromotionProof
{
    public required string GameId { get; init; }
    public required int Epoch { get; init; }
    public required string KeyId { get; init; }
    public required string Fingerprint { get; init; }
    public required string Signature { get; init; }
}

// ---------------------------------------------------------------- backup host (T281, design/HOST-MIGRATION.md sections 1 and 3)

/// <summary>Host to the designated candidate only: asks it to become the backup host. Nothing flows until it answers <see cref="StandbyAccept"/>. A client ignores it unless it was addressed to its own key.</summary>
public sealed record StandbyOffer : Message
{
    public required string KeyId { get; init; }
}

/// <summary>
/// Candidate to host: consent. <c>Endpoints</c> are the addresses (host names or IPs) the candidate can be reached on and <c>Port</c> its listener, <c>Fingerprint</c> the host key fingerprint of that listener.
/// Accepted only from the designated key after <c>Auth</c> (otherwise <see cref="ErrorCodes.NotDesignated"/>); the host probes one endpoint (a TLS handshake, nothing else) before it counts as ready.
/// </summary>
public sealed record StandbyAccept : Message
{
    public required List<string> Endpoints { get; init; }
    public required int Port { get; init; }
    public required string Fingerprint { get; init; }
}

/// <summary>Candidate to host: refuses the offer, or later withdraws. The host runs without a backup host.</summary>
public sealed record StandbyDecline : Message;

/// <summary>Host to candidate: the designation ended (the host revoked it, or the seat changed hands). The candidate's copy on its own disk is not erased.</summary>
public sealed record StandbyRevoke : Message;

/// <summary>Host to the candidate: where the standby stands (<see cref="Unciv.Net.Standby.StandbyStates"/>). Counters only. <c>LagEntries</c> is how many log entries the successor is behind the host (0 before it subscribes).</summary>
public sealed record StandbyStatus : Message
{
    public required string State { get; init; }
    public required int LagEntries { get; init; }

    /// <summary>T272: the game the host runs (set by a running game, absent in the lobby). The candidate subscribes to the replication stream of this game, and keeps its standby store under it.</summary>
    public string? GameId { get; init; }
}

// ---------------------------------------------------------------- standby replication (T272, design/HOST-MIGRATION.md section 1)

/// <summary>
/// Candidate to host: starts (or resumes) the replication stream on the authenticated session. <c>HaveSeq</c> is the last log seq the candidate's standby store holds (-1: none) and <c>HaveLineSha256</c> the
/// sha256 (lower-case hex) of its last command line, so the host can tell a store that continues its log from one that diverged. Accepted only from the designated key that consented
/// (<see cref="ErrorCodes.NotDesignated"/> otherwise); the host answers with <see cref="StandbyMeta"/>, a <see cref="StandbySnapshot"/> when it cannot continue from <c>HaveSeq</c>, then <see cref="StandbyEntry"/> frames.
/// </summary>
public sealed record StandbySubscribe : Message
{
    public required string GameId { get; init; }
    public required long HaveSeq { get; init; }
    public string? HaveLineSha256 { get; init; }
}

/// <summary>
/// Host to the subscribed candidate only: <c>game.json</c> (without the host's own epoch bookkeeping and designation) as the successor must keep it, and, when the stream restarts from a snapshot
/// (<see cref="StandbySnapshot.Reset"/>), the command log's header line. Sent again whenever <c>game.json</c> changes (a seat rebinding).
/// </summary>
public sealed record StandbyMeta : Message
{
    public required int Epoch { get; init; }
    public string? Header { get; init; }
    public required string Meta { get; init; }
}

/// <summary>
/// Host to the subscribed candidate only: one chunk of a snapshot file (the <c>snapshot-&lt;seq&gt;.json.gz</c> bytes, base64) with the sha256 (hex) of the whole file. <c>Reset</c> replaces the successor's whole
/// store (the stream could not continue from its seq); without it the snapshot only adds to the store. The successor assembles chunks in order and keeps the file only when size and sha256 match.
/// </summary>
public sealed record StandbySnapshot : Message
{
    public required int Epoch { get; init; }
    public required long Seq { get; init; }
    public required int Part { get; init; }
    public required int Parts { get; init; }
    public required string Sha256 { get; init; }
    public required string Data { get; init; }
    public bool Reset { get; init; }
}

/// <summary>Host to the subscribed candidate only: the verbatim <c>commands.log</c> line (a command or a checkpoint) of log position <c>Seq</c>, in log order. The successor appends it durably, then acks.</summary>
public sealed record StandbyEntry : Message
{
    public required int Epoch { get; init; }
    public required long Seq { get; init; }
    public required string Line { get; init; }
}

/// <summary>Candidate to host: every entry up to <c>Seq</c> is durable in the standby store. Never decreasing and never above the host's last seq, otherwise the subscription is closed.</summary>
public sealed record StandbyAck : Message
{
    public required long Seq { get; init; }
}

/// <summary>Where a successor can be reached: <c>Endpoints</c> are <c>host:port</c> strings (the same form as the client's known-hosts keys) and <c>Fingerprint</c> the certificate the host's probe saw there.</summary>
public sealed record SuccessorInfo
{
    public required string KeyId { get; init; }
    public required List<string> Endpoints { get; init; }
    public required string Fingerprint { get; init; }
}

/// <summary>One human seat of the roster a <see cref="HostSuccessor"/> carries (what a later election counts).</summary>
public sealed record SuccessorSeat
{
    public required string KeyId { get; init; }
    public required bool Connected { get; init; }
}

/// <summary>
/// Host to every client: the backup-host announcement, in <see cref="JoinOk"/> and whenever it or the roster changes (public: addresses and a fingerprint, no game state). <c>Successor</c> is set only once
/// the standby is <c>ready</c>; <c>Status</c> is the <see cref="Unciv.Net.Standby.StandbyStates"/> value. A client takes it only from its authenticated host channel at an epoch it accepts.
/// </summary>
public sealed record HostSuccessor : Message
{
    public required int Epoch { get; init; }
    public SuccessorInfo? Successor { get; init; }
    public required string Status { get; init; }
    public required List<SuccessorSeat> Roster { get; init; }
}

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
