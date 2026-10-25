# Authoritative server and networked play (design for T165 / C006)

Status: design, no engine code. Owner: server/network track (tickets T172+, see "Tickets"). Companion document:
`design/COMMANDS-IMPLEMENTATION.md` (command catalogue, human turn flow, engine query API; written by another agent).
This document does **not** repeat it: wherever it says "the command layer" it means the T164 split tickets, and the
events/commands named here are placeholders until that catalogue lands. Re-point `Depends: T164` to the concrete
tickets once they exist.

Legend: **[V]** verified by reading the C# or Kotlin source in this branch; **[I]** inferred, to be confirmed by the
implementing ticket.

## 0. Goal and non-goals

Online play with friends (2-8 humans, plus AI and city states), over our own server binary and protocol. No upstream
Unciv server, no Dropbox, no apiv2. The Kotlin stack (`core/.../logic/multiplayer`, `server/`) is Won't-port; it is read
only for lessons.

### Lessons from the Kotlin stack (what it got wrong)

1. **The server is a dumb file store [V].** `server/.../UncivServer.kt` exposes `PUT/GET /files/{id}` plus an optional
   `/auth` (flag `-no-auth`, default shown as enabled via `UncivServerAuth` env). It never runs game logic.
2. **Trust in the client [V].** `Multiplayer.resignPlayer` / `skipCurrentPlayerTurn` download the whole `GameInfo`, mutate
   it locally, call `nextTurn()` and upload it. Any client can write any state; there is no per-command validation.
3. **Every client holds the full truth [V/I].** The uploaded file is the entire `GameInfo` (all civs' explored tiles, all
   units). Fog of war is a UI rendering rule only; a modified client sees everything.
4. **Conflict handling by heuristic [V].** `hasNewerGameState` compares `turns` and `currentPlayer` of previews; two
   uploads race, last write wins. No sequence numbers, no log, no way to tell what happened.
5. **Polling and wall clock in the engine [V].** `GameInfo.currentTurnStartTime = Clock.System.now()`, turn timeout
   read inside `GameInfo`; clients poll for updates. The C# `GameInfo` already moved time out (starts at 0) [V].
6. **AI and turn processing run on whichever client ends the turn [V/I]**, so the AI round is not reproducible or auditable.

Design consequences: server runs the engine (single writer), clients get filtered views, everything is a sequenced
command, time is an input supplied by the host, push (not poll).

## 1. Architecture

```
 Godot client (C006)            Unciv.Client.Net (library)              Unciv.Server (headless host)
 UI, input, animation    <-->   session, mirror state, reconnect  <-->  Lobby / Sessions / Auth
                                                                         GameHost (one per game)
                                 Transport (pluggable)                     CommandProcessor<GameInfo>   (T164)
                                                                           TurnPolicy + Clock (host)
                                                                           VisibilityFilter -> per-recipient
                                                                           GameStore (snapshot + log)
```

- **Transport is an interface** (`ITransport`: reliable ordered message frames, connect/disconnect events). Protocol,
  lobby, turn policy and persistence are written and tested against an in-memory transport; real sockets are one
  ticket (S-transport) behind an owner decision (section 6).
- **One `GameHost` per game, single-threaded actor** (a channel of inbound items: client message, timer tick, AI result).
  This preserves the T012 rule "CommandProcessor is the single writer" and removes locking from the engine.
- The server owns the **only full `GameInfo`**. Clients own a *mirror*: a visibility-filtered projection (section 2),
  never a complete `GameInfo`.
- **AI** runs on the server, as commands issued by the AI civ through `Submit` (COMMANDS.md "open questions": recommended
  yes, logged individually) [V design, I effort]. AI time is bounded by the host; the AI round is performed when the
  turn policy says it is the AI's phase.
- **Time** enters only via the host `IClock` and the server-supplied fields of a command (COMMANDS.md rule 1). Timer
  expiry is converted into ordinary logged commands (`ForceEndTurn`/`Skip`, issued by actor `server`), so replay needs no clock.

## 2. Client view and event stream (requirement 1)

### 2.1 What exists [V]
`Unciv.Core/View` (T084/T085/T138) is a *read-only API over the full GameInfo* (`GameView(gameInfo, civ)`): it is how
engine code asks "what may civ X see". It is **not** a serializable client state. A Godot client cannot hold a
`GameInfo` (that is the leak we are fixing), so the wire form needs its own DTOs. The View classes are the *source of
truth for the filter*, not the wire format: the filter walks `GameView.civView` / `TileView` / `ForeignCityView` /
`ForeignMapUnitView`, which already encode "foreign" (reduced) vs "owned" (full) detail. Using them avoids a second,
divergent visibility rule. `Tile.IsExplored(civ)` / `IsVisible(civ)` and `Civilization.ExploredRegion` exist [V].

### 2.2 Wire form: JSON, versioned
JSON text frames using the existing `UncivJson` options (same as `CommandEnvelope.Payload`, so one serializer, one
registry pattern, golden-file testable). Rejected for v1: binary/MessagePack (premature; revisit if a measured snapshot
is too big, they compress well with permessage-deflate / gzip). Every message carries `protocol` version in `hello`.

### 2.3 Two kinds of server-to-client data
1. **`Snapshot` (client-specific full state)**: `ClientSnapshot { seq, turn, phase, you:civId, civs[], tiles[], cities[],
   units[], diplomacy[], pendingTrades[], notifications[], viewHash }`. Built by the visibility filter from the full
   state. Sent on join, reconnect, resync request, and every N turns (cadence is a tunable, not a contract).
2. **`ViewDelta` per accepted command** (`seq`, `events[]`): the *filtered* consequences of a command for that
   recipient. Clients apply deltas to their mirror in `seq` order; a gap triggers `ResyncRequest`.

Do **not** broadcast accepted `CommandEnvelope`s to other humans (COMMANDS.md already says that is lockstep-only).

### 2.4 Event vs diff: recommendation
Events (`GameEvent` past-tense facts) are the delta currency, because the client needs them for animation anyway
(UnitMoved, UnitDamaged, CityFounded). Each event type gets a **visibility classifier** next to its definition
(`IEventVisibility.AudienceFor(event, state) -> per-civ projection`), with three outcomes per recipient:
*full* (own thing), *redacted* (e.g. `UnitMoved` along a path only partly visible becomes `UnitAppeared`/`UnitDisappeared`
at the visible ends), *drop*. State-implied changes with no event (fog reveal when a unit moves: new tiles become
explored; yields change) are emitted as explicit **`TilesRevealed`** and **`StatsChanged`** deltas computed by the server by
diffing the recipient's filtered state before and after the command (cheap: only tiles/units in the touched region plus
the actor's own civ data). Rule: *no event type may be added without its classifier* (a reflection test enforces it,
like the ViewPurityTest idea). Recommendation: events plus server-computed reveals, not a generic object diff, because
a generic diff of `GameInfo` JSON would encode hidden state in its shape.

### 2.5 Notifications [V model, I delivery]
`Notification { Category, Text, Icons, Actions }` with `NotificationAction`s (location, tile, civ, city...) exists and is
`IsPartOfGameInfoSerialization`; each `Civilization.Notifications` is per civ already. Delivery: each civ's new
notifications are part of that civ's `ViewDelta` (a `NotificationAdded` event, only to the owner). Actions that carry
coordinates must be re-filtered: a notification whose action points at an unexplored tile (e.g. "enemy spotted") only
ever references tiles the recipient currently sees at creation time, else the action is stripped. Texts stay
untranslated keys + parameters; the client translates (T-L1 translation port). Popup alerts (`PopupAlert`) travel as
`AlertAdded` events the same way. Out-of-game "your turn" push is a lobby-level message (section 4), not a game event.

### 2.6 State hash across a filtered view [V + design]
T011 `StateHasher` is FNV-1a over the *full serialized GameInfo* (`EndToEndDeterminismTests.Hash` hashes
`UncivFiles.GameInfoToString(game, forceZip:false)`) [V]. A client cannot reproduce it. So: the server keeps the **full
hash** (authoritative, used for server replay checks and save integrity); each recipient gets a **`viewHash`** over the
canonical serialization of its own `ClientSnapshot`. The client recomputes `viewHash` of its mirror after applying each
delta batch at checkpoints (every end of turn, and on demand); mismatch => `ResyncRequest`. `viewHash` is an integrity
and bug detector, not a security control.

## 3. Visibility filter and anti-cheat (requirement 2)

Principle: **default deny**. The filter is a whitelist projection: only fields explicitly listed in a DTO are ever
serialized, so a newly added engine field cannot leak by omission.

| Data | Rule |
|---|---|
| Tiles | Never send unexplored tiles. Explored-but-fogged tiles: terrain, resource-if-revealed, last-seen improvement (`LastSeenImprovement` exists [V]) - no units, no live ownership change. Visible tiles: full public detail. |
| Foreign units | Only on currently visible tiles, reduced to `ForeignMapUnitView` fields (type, owner, health bucket, promotions visible). Invisible units (submarines, stealth uniques) obey the engine's own `isInvisible(viewer)`; the filter calls it, never reimplements it. |
| Foreign cities | Explored city: name, owner, tile, population, visible buildings per `ForeignCityView`; never production queue, stored production, tile worked assignments, or hidden stats. |
| Own civ | Full `CivView` data. |
| Other civs | `ForeignCivView`: name, nation, cities known, public diplomacy status. Never gold, science, policies, tech list beyond what rules reveal (use the game's own "can see" rules, e.g. embassy/spy reveals; the View class decides). |
| Diplomacy | Only relationships involving the recipient plus public facts (wars and peace are public per Civ rules). |
| Trade | Pending offers are visible only to the two parties. Declined/retracted offers are deleted from the other party's view. Trade-evaluation details (AI's willingness numbers) never leave the server. |
| Espionage | Spies are private to the owner; foreign spies are not visible unless the ruleset reveals them. Steal results are delivered as private notifications to the owner only. |
| RNG, seeds, `GameRandom` state, `gameId` seed, map seed | Never sent (a known seed predicts map-gen and combat). Snapshots carry no seed. |
| Whole save | No client can request the full `GameInfo` file; "download save" exports the *client snapshot*, not the server save. Host-side admins can export the real save. |

**Command validation** (on top of T164's per-command `Validate`): the server layer adds, before the engine sees the
envelope: (1) session is authenticated and bound to exactly one civ in this game (actor id is *injected*, never read from
payload, COMMANDS.md [V]); (2) phase/turn gate: actor is allowed to act in the current phase (section 4); (3) `ClientCommandId`
dedup (idempotent retries); (4) size and rate limits (max payload, N commands/s, max unacknowledged); (5) references to
units/cities/tiles must be ones the actor owns or *currently sees*; a rejected command returns only a generic code, never
"that unit exists but is not yours" (existence oracle). `Validate`'s error text must not embed hidden data; a test
enumerates all rejection messages for fixed hidden-state fixtures.

Residual leaks to accept or close later (list for the owner): timing side channels of AI/turn processing; the *count* of
turns/commands; map size; list of nations in game (public by lobby). Out of scope: collusion between humans, screen sharing.

## 4. Turn policy (requirement 3)

### 4.1 Modes
`TurnMode` is a lobby/game setting stored in the host's `GameSettings`, with `phase` held *in the server layer's state*
(COMMANDS.md open question: phase must live in state if simultaneous is supported; the processor does not assume sequential).

- **Sequential (default for v1, matches Unciv).** One human civ is `currentPlayer`; AIs run after the human block.
  `Validate` already checks "is it your turn" per command. Implemented by the T164 turn-flow tickets; this design only
  adds a clock and a skip.
- **Simultaneous (v2, design now, build later).** All humans act in the same turn; a turn ends when every active human has
  sent `EndTurn` (or timer fires); then the server runs the AI + turn-change as one deterministic sequence. Commands in
  the same turn from different humans are ordered by server arrival (`Seq`), which the log fixes. Conflicts (two units to
  the same tile) resolve by that order. Needs: per-civ `EndedTurn` flags, and an audit that no engine rule keeps a global
  "current civ" (verify in T164 notes; **[I]** since Kotlin `GameInfo.currentPlayerCiv` exists and is used by many
  `View`/UI paths, simultaneous mode likely needs `viewer` explicit everywhere, which the View classes already do [V]).
- **Play-by-mail / async.** Not a separate mode: sequential + no timer + persistence + push notification when it is your
  turn. A game with no one connected simply idles (server need not hold anything in RAM, section 5).

### 4.2 Timers, AFK, drop
- Turn timer: host-side `TurnTimer` per game (optional, per-turn seconds or per-phase base+increment). Fires into
  the actor queue; the effect is the logged server command `ForceEndTurn(civ)`.
- **AFK vs disconnect**: disconnected for < grace (default 2 min) = waiting; AFK = connected but no command for the turn timer.
  After timeout: `ForceEndTurn` for the civ (keeps the player, skips their remaining actions). After `K` consecutive timeouts
  (default 3): civ marked `Dropped`.
- **AI replacement of a dropped player**: `Dropped` civ flips to AI control (`PlayerType.AI`; `PlayerType` exists [V]) through a
  logged server command `SetController(civ, AI)`. The player may `Reclaim` at their next turn start (lobby host can disable).
  The Kotlin `resignPlayer` already does a civ-to-AI switch locally [V]; the command form is new. AI replacement must respect
  fog: the AI is allowed the full state (it is the server) but is the same code path as AI civs, so no new leak.
- **Resign / vote kick** are host-lobby messages that translate into the same commands.
- Turn-start summary ("what happened while you were away") = the notifications in the snapshot since the player's `lastAckSeq`.

## 5. Persistence and resync (requirement 4)

### 5.1 Files [V design on T100]
Per game a directory (or SQLite file; [owner/impl decision, default plain files]):
- `snapshot-<seq>.json.gz`: full server `GameInfo` serialized by `UncivFiles` (gzip+base64 JSON, Kotlin-compatible) [V];
  T140 (Done) wired civs so the round trip works [V]; the T011 hash over a save/load round trip is asserted by T102 [V].
- `commands.log`: append-only JSON Lines of `CommandEnvelope` (`Seq`, `Type`, `ActorId`, `Payload`), plus `Hash` checkpoints
  `{seq, turn, fullHash}` after each turn change. Server commands (timers, AI, controller changes) are in the same log.
- `game.json`: lobby/meta: players, civ bindings, identities (public keys), settings, turn policy; not part of GameInfo.
Write order: append + fsync log line *before* acknowledging an accepted command; snapshot written (tmp + rename) every N
turns / M commands. Recovery = load latest snapshot + `Replay` the tail, verify the next checkpoint hash. A corrupt tail
line is truncated and reported.

### 5.2 Resync
- Client reconnect: `Resume{gameId, lastSeq, lastViewHash}`. If the server's retained per-client delta ring contains
  `lastSeq+1`, it sends the missed `ViewDelta`s; else a fresh `Snapshot`. (Deltas are *regenerated by replaying the log tail
  through the filter*, so no per-client storage is needed beyond the log: replay on a scratch copy.) [I: cost of replaying
  from the snapshot per reconnect; mitigate by keeping the head state in RAM and storing recent filtered deltas in a ring.]
- Desync detection: server compares `fullHash` to the log checkpoint after replay (catches nondeterminism bugs, not cheating);
  clients compare `viewHash` (section 2.6).
- Faulted processor (`IsFaulted`, COMMANDS.md [V]): the game host reloads snapshot + log and refuses to continue past the
  failing command, quarantines it, and tells the lobby.
- **Save format interplay**: the server save is a normal Unciv save (`UncivFiles`), so it opens in the single-player game
  (`GameInfoSerializationVersion` and `BackwardCompatibility` apply [V]). The command log is *new*; saves carry the `seq` they
  correspond to in `game.json`. A log written by a later engine version is replayed only by the same engine version; across
  versions the snapshot is authoritative (log tail dropped after an upgrade; the host does an end-of-turn snapshot before
  upgrades). Determinism hashes from the Kotlin game are not comparable (T160 pending) [V].
- Retention: logs older than the latest snapshot are kept as a replay file (game history/anti-cheat audit, "watch replay").

## 6. Network stack, identity, auth (requirement 5)

### 6.1 Protocol messages (transport independent; JSON, one `type` string per frame)
Client to server: `Hello{protocol, clientVersion, rulesetHash, modList}`, `Auth{proof}`, `ListGames`, `CreateGame`, `JoinGame`,
`LeaveGame`, `LobbySet{civ, color, ready}`, `StartGame` (host), `Resume{gameId,lastSeq,lastViewHash}`, `Command{clientCmdId, type,
payload}` (the `CommandEnvelope` minus `Seq`/`ActorId`), `ResyncRequest`, `Chat`, `Ping`.
Server to client: `Welcome{sessionId, challenge}`, `AuthOk/AuthFail`, `Games`, `LobbyState`, `Snapshot`, `ViewDelta{seq, events}`,
`CommandResult{clientCmdId, accepted, code, message, seq}`, `TurnNotice{game, turn, whoseTurn, deadline}`, `Error{code}`, `Pong`.
Rules: strict schema, max frame size, unknown `type` is an `Error`, never an exception; every game-scoped message includes
`gameId`; `ruleset hash` mismatch rejects at join (mods are a lobby setting, mod files are *not* transferred in v1, see ticket T170).

### 6.2 Identity and auth (friends-only)
Recommended: **keypair identity + invite code**. The client generates an ECDSA P-256 keypair on first run (BCL
`ECDsa`, no third-party crypto; **Ed25519 is not in the .NET BCL [I]**). The public key is the player id. The server holds
an allow-list: the host mints a single-use or time-limited **invite token**; first connection presents token + public key and
is bound (trust-on-first-use) to a display name; later connections answer a server challenge by signing it. No passwords,
no account system, nothing to phish or leak from a server DB. Lost key = host re-invites. Alternative (rejected): shared
server password (one leak = everyone impersonated, no per-player attribution); OAuth/Steam/Discord login (external dependency,
against the "own networking" goal).

### 6.3 Transport options (decision D1)
| Option | Server | Client | Pros | Cons |
|---|---|---|---|---|
| **A. WebSocket (text frames) over TLS** (Kestrel `UseWebSockets`) | ASP.NET Core minimal host in `Unciv.Server` | `System.Net.WebSockets.ClientWebSocket` in the .NET Godot project (or Godot `WebSocketPeer`) | One port, proxy/CDN/Caddy friendly, works through most firewalls, easy to debug (JSON), future web client possible | ASP.NET dependency in the server project (not in Core); TCP head-of-line blocking (irrelevant for turn-based) |
| **B. Raw TCP + length-prefixed frames** | `System.Net.Sockets`/Pipelines | same | No ASP.NET; smallest dependency; simple | Hand-rolled framing/TLS (`SslStream`), no proxy ecosystem, no browser path |
| **C. ENet / Godot high-level multiplayer (UDP)** | Godot headless as server, or ENet bindings | Godot `ENetMultiplayerPeer` | Built into Godot | Server would have to run the Godot runtime or a native ENet binding; weaker fit for an authoritative **.NET** engine host; UDP/NAT traversal pain; no real need for unreliable channels in a turn-based game; Godot-specific code cannot be unit-tested in the engine test project |
**Recommendation: A**, JSON text frames, permessage-deflate on, ASP.NET Core confined to `Unciv.Server` (the Core stays dependency-free, and the transport interface keeps B possible). C is not recommended for this game.

### 6.4 Hosting and NAT (decision D2)
1. **Hosted VPS (recommended default)**: the headless server runs on a ~5 USD VPS, behind Caddy (automatic Let's Encrypt TLS) on
   443. No NAT problem; always on, so async/PBM works with all players offline. Cost: a domain (optional, ~10 USD/yr) and the VPS; the owner must
   operate it.
2. **Friend-hosted with an overlay (Tailscale/ZeroTier/WireGuard)**: no port forwarding, private network is the transport
   security; free; server only up when the host machine is. Players install the overlay.
3. **Direct host with port forwarding / IPv6**: works if the host controls the router; needs dynamic DNS and a TLS cert (self-signed + the client
   **pins the fingerprint** distributed in the invite code is acceptable for friends). CGNAT breaks it.
No relay/hole-punching infrastructure in v1: an authoritative server *is* the rendezvous point, which removes P2P NAT traversal altogether. The
dedicated-server binary must run as a headless console app (`Unciv.Server`), also embeddable "host a game" in the client later (same code, loopback).

### 6.5 Transport security
TLS 1.2+ everywhere (WSS). Invite code carries `host:port` and optionally a cert fingerprint (so self-signed works without CA). Signed-challenge
auth prevents replay (fresh server nonce per connection). Rate limits and a max-sessions cap per IP. No sensitive data at rest on the server
except public keys. Logs never contain snapshot content.

## 7. Godot side (requirement 6, C006 at design level)

- **Library vs scene split**: `Unciv.Client.Net` (plain C#, no Godot types; testable in `Unciv.Core.Tests` style with the in-memory
  transport) holds: transport client, auth, `ClientSession` state machine (Disconnected / Lobby / Syncing / InGame / Reconnecting),
  the **client mirror** (`ClientGameState` built from `Snapshot`, mutated by `ViewDelta`s), command submission with `clientCmdId`
  retry, and events surfaced as a plain .NET event/`Channel<>` of `ClientEvent`.
- **Godot** (`Unciv.Godot`, C006, C-track): lobby/join screens, invite-code entry, "your turn" toast/notification list, reconnect overlay, turn
  timer display, replay of events into animation (`C004`/`C005` consume the same `ClientEvent`s). Godot must marshal network events to the main
  thread (`CallDeferred`/a pump in `_Process`), because the socket loop runs off-thread.
- **Single player uses the same path**: a local `GameHost` + loopback transport, so offline and online share one code path; this is also the test
  rig. (Dependency note: C004/C005 depend on T164 only; they should program against the `ClientEvent` interface from S-clientlib, not
  `GameInfo`; re-point after review.)
- Local persistence on the client: identity key file, known servers, last `seq`/`viewHash` per game (a small JSON file; never the snapshot).

## 8. Decision points for the human owner

| # | Question | Recommendation | Blocks |
|---|---|---|---|
| D1 | Transport stack | A: WebSocket+TLS on Kestrel, interface-isolated | S-transport |
| D2 | Where does the game server run? | Hosted VPS (always on, PBM works); overlay VPN as free fallback | deployment doc, TLS ticket |
| D3 | Turn mode for v1 | Sequential only; simultaneous designed but deferred | S-simultaneous (not filed until chosen) |
| D4 | Identity | ECDSA keypair + invite tokens (no accounts) | none (S-identity proceeds with this default) |
| D5 | Drop policy defaults | 2 min grace, 3 timeouts, then AI, reclaimable | none (configurable) |
| D6 | Mods in multiplayer | Ruleset hash must match; no automatic mod download in v1 | T170 interplay |

## 9. Tickets

See `ticket.py list` for the live state. Dependency-ordered set (IDs in the backlog):
protocol spec/DTOs -> (identity | visibility filter | persistence | turn policy | host+lobby) -> event stream
classifiers -> client library -> e2e multi-client test -> transport (D1) -> C006 Godot UI. All of them depend on the
command-layer tickets split from T164 only where they need concrete commands/events.

## 10. Verified vs inferred summary

Verified: Kotlin server is a file store with optional auth; resign/skip mutate whole GameInfo client-side; T011 hash is over the full serialized
GameInfo; `UncivFiles` + T140 round trip; `CommandProcessor` semantics; View classes and `Tile.IsExplored/IsVisible`; Notification model.
Inferred: cost of snapshot size and replay-on-reconnect; Ed25519 absence from the BCL; how many engine paths use a global current civ (matters for
simultaneous mode); whether the View classes cover every field the filter needs (S-filter ticket must audit and file gaps); AI duration per round.
