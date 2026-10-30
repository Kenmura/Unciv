# Host-run authoritative game and networked play (design for T165 / C006)

<!-- toc -->
## Contents

- [0. Goal and non-goals](#0-goal-and-non-goals)
  - [Lessons from the Kotlin stack (what it got wrong)](#lessons-from-the-kotlin-stack-what-it-got-wrong)
- [1. Architecture](#1-architecture)
- [2. Client view and event stream (requirement 1)](#2-client-view-and-event-stream-requirement-1)
  - [2.1 What exists [V]](#21-what-exists-v)
  - [2.2 Wire form: JSON, versioned](#22-wire-form-json-versioned)
  - [2.3 Two kinds of server-to-client data](#23-two-kinds-of-server-to-client-data)
  - [2.4 Event vs diff: recommendation](#24-event-vs-diff-recommendation)
  - [2.5 Notifications [V model, I delivery]](#25-notifications-v-model-i-delivery)
  - [2.6 State hash across a filtered view [V + design]](#26-state-hash-across-a-filtered-view-v--design)
- [3. Visibility filter and anti-cheat (requirement 2)](#3-visibility-filter-and-anti-cheat-requirement-2)
- [4. Turn policy: simultaneous turns (owner decision 2)](#4-turn-policy-simultaneous-turns-owner-decision-2)
  - [4.1 Timers, AFK, disconnect, join and leave](#41-timers-afk-disconnect-join-and-leave)
- [5. Persistence and resync (host-owned, requirement 4)](#5-persistence-and-resync-host-owned-requirement-4)
  - [5.1 Files [V design on T100]](#51-files-v-design-on-t100)
  - [5.2 Resync](#52-resync)
  - [5.3 When the host quits (manual hand-off; optional automatic host migration, T268)](#53-when-the-host-quits-manual-hand-off-optional-automatic-host-migration-t268)
- [6. Network stack, identity, security (host-a-lobby, requirement 5)](#6-network-stack-identity-security-host-a-lobby-requirement-5)
  - [6.1 Protocol messages (transport independent; JSON, one `type` per frame)](#61-protocol-messages-transport-independent-json-one-type-per-frame)
  - [6.2 Identity and join (no accounts server)](#62-identity-and-join-no-accounts-server)
  - [6.3 Transport (replaces decision D1)](#63-transport-replaces-decision-d1)
  - [6.3a Transport robustness and TLS platform matrix (T225)](#63a-transport-robustness-and-tls-platform-matrix-t225)
  - [6.4 Transport security (recommendation: TLS 1.3 + host-key fingerprint pinned on first use, fingerprint carried in the invite)](#64-transport-security-recommendation-tls-13--host-key-fingerprint-pinned-on-first-use-fingerprint-carried-in-the-invite)
  - [6.5 Hosting, lobby and NAT](#65-hosting-lobby-and-nat)
  - [6.6 Trust model (state it to players)](#66-trust-model-state-it-to-players)
- [7. Godot side (C006 and the host/lobby slices)](#7-godot-side-c006-and-the-hostlobby-slices)
- [8. Decisions (owner decisions override the earlier recommendations D1-D6)](#8-decisions-owner-decisions-override-the-earlier-recommendations-d1-d6)
  - [Owner decisions still open (each has a default; work proceeds on the default)](#owner-decisions-still-open-each-has-a-default-work-proceeds-on-the-default)
- [9. Tickets](#9-tickets)
- [10. Verified vs inferred summary](#10-verified-vs-inferred-summary)
<!-- /toc -->

Status: design, no engine code; revised after the OWNER decisions (host-a-lobby model, simultaneous turns for the MVP; section 8).
Owner: server/network track (tickets T172+, see "Tickets"). Simultaneous-turn engine analysis: `design/SIMULTANEOUS-TURNS.md` (read it with sections 4-5). Companion document:
`design/COMMANDS-IMPLEMENTATION.md` (command catalogue, human turn flow, engine query API; written by another agent).
This document does **not** repeat it: wherever it says "the command layer" it means the T164 split tickets, and the
events/commands named here are placeholders until that catalogue lands. Re-point `Depends: T164` to the concrete
tickets once they exist.

Legend: **[V]** verified by reading the C# or Kotlin source in this branch; **[I]** inferred, to be confirmed by the
implementing ticket.

## 0. Goal and non-goals

Online play with friends (2-8 humans, plus AI and city states), **Terraria-style**: one player *hosts* a lobby on a port and friends connect directly by `address:port`. No always-on VPS, no accounts service, no reverse proxy. The same server code runs as a dedicated headless `Unciv.Server` or in-process behind the client's "Host" button. No upstream
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
 Godot client (C006)            Unciv.Client.Net (library)         Host = Unciv.Server (dedicated) OR in-process in the client
 UI, input, animation    <-->   session, mirror state, reconnect  <-->  Lobby / Sessions / Join auth
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
- The host owns the **only full `GameInfo`** (and the authoritative saves, section 5). An in-process host trusts its own player (honor system, section 6.6); a dedicated host is the answer for a group that does not.
- The server owns the **only full `GameInfo`**. Clients own a *mirror*: a visibility-filtered projection (section 2),
  never a complete `GameInfo`.
- **AI** runs on the host and is logged **per AI civ** (`RunAiTurn(civ)`), not per action (`design/COMMANDS-IMPLEMENTATION.md` section 1.3 refines the earlier "AI as commands" idea: the AI code mutates `GameInfo` directly). The AI round runs once per turn, after all humans have ended it (or the timer fired); see `design/SIMULTANEOUS-TURNS.md`.
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
1. **`Snapshot` (client-specific full state)**: `ClientSnapshot { seq, turn, phase, turnStatus{ended[], deadline}, you:civId, civs[], tiles[], cities[],
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

**Amended by T224 (mirror correctness).** Events alone cannot keep the mirror equal to a fresh snapshot (many fields have no event, and events can be
truncated). The mirror is therefore moved by `ViewDelta.Diff`: after every accepted command the host builds the recipient's filtered `ClientSnapshot`
again and sends `SnapshotDiff.Compute(previous, fresh)` (`ViewDeltaBuilder.BuildWithDiff`), plus the `viewHash` the mirror must reach. The diff is
computed from the whitelisted DTOs only, never from `GameInfo`, so it cannot carry more than the snapshot could. Events stay in the delta for animation
and sound only. `ClientSnapshot` also carries `PopupAlerts` and `PendingDecisions` (the recipient's own), so a reconnecting player sees what blocks End turn.

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
| RNG, seeds, `GameRandom` state, `gameId` seed, map seed, **host secret** | Never sent. Snapshots carry no seed and no secret. Withholding the seed alone is not enough (see "Host secret" below). |
| Whole save | No client can request the full `GameInfo` file; "download save" exports the *client snapshot*, not the server save. Host-side admins can export the real save. |

**Host secret (T230).** Engine rolls are *state-based*: the seed is a hash of public data (turn number, tile position, civ and unit ids,
`gameId`), so hiding the map seed does not hide combat outcomes; a modified client could compute any attack's roll and pick the best tile.
Every state-based seed (`GameContext.StateBasedRandom`, `BattleDamage.GetRandomness`, captures, espionage, pillage, ruins, religion, unique
conditionals, `MapUnitView.StateBasedRandom`, ...) is therefore mixed with a per-game 64-bit secret (`GameInfo.RandomSalt`,
`Randomness.SaltedSeed`, a splitmix64 finalizer). Rules: (1) the host generates it once at game start with `HostSecret.Generate()` (OS CSPRNG) and
applies it right after `StartNewGame` (`HostSecret.ApplyTo`); (2) it is `[JsonIgnore]`: it is not in the save, the full-state hash, any
`ClientSnapshot`/delta/DTO, and no view member exposes it (`GameView.RandomSalt` is `internal`); (3) it is persisted host-side only, in the game
store's `game.json` (T176) and in the command-log header (`hostSecret`, omitted when 0), which replay needs; any log shared with a client must use
`CommandLogHeader.WithoutSecret()`; (4) after every load, restore or crash recovery the host re-applies it from the store (a save alone does not carry it),
and `ReplayVerifier` applies the header's secret on `StartGame`/`VerifyTail`; (5) secret 0 = unchanged Kotlin behaviour (single player, CLI, tests).
Same secret gives identical rolls and hashes, so replay and resume stay deterministic; a different secret plays a different game.
Not covered: map generation and the game starter run before the secret exists (the map is public once explored); the secret is 64 bits and not
a cryptographic MAC, enough that rolls cannot be predicted or searched from outcomes by a client.

**Command validation** (on top of T164's per-command `Validate`): the server layer adds, before the engine sees the
envelope: (1) session is authenticated and bound to exactly one civ in this game (actor id is *injected*, never read from
payload, COMMANDS.md [V]); (2) turn gate: the engine's `RequireActive(actor)` (sequential: actor is the current civ; simultaneous: actor is human, alive, not ended, and the phase is Playing; section 4); (3) `ClientCommandId`
dedup (idempotent retries); (4) size and rate limits (max payload, N commands/s, max unacknowledged); (5) references to
units/cities/tiles must be ones the actor owns or *currently sees*; a rejected command returns only a generic code, never
"that unit exists but is not yours" (existence oracle). `Validate`'s error text must not embed hidden data; a test
enumerates all rejection messages for fixed hidden-state fixtures.

Residual leaks to accept or close later (list for the owner): timing side channels of AI/turn processing; the *count* of
turns/commands; map size; list of nations in game (public by lobby). Out of scope: collusion between humans, screen sharing.

## 4. Turn policy: simultaneous turns (owner decision 2)

Full analysis, verified facts and the conflict table are in `design/SIMULTANEOUS-TURNS.md`. Summary of what the server layer does:

- `TurnMode` is a game setting stored **in the game state** (`GameInfo.TurnState`, section 5 of that doc), not in the server layer: the engine's command gate must know who may act, otherwise replay of the log without a host would accept commands the host refused. Two modes: **Simultaneous** (every hosted game, the MVP) and **Sequential** (single player, hotseat, tests; byte-identical to today's `NextTurn`, T102 hashes unchanged).
- **Playing phase.** All living human civs are *active* in parallel. The host applies commands **one at a time in arrival order** (single-writer actor); `Seq` is the canonical order, the log is that order. A command is validated and applied against the live state at that moment, so combat, movement and city founding resolve immediately (the player sees the result) and conflicts are first-come-first-served.
- **Ending.** `EndTurn` marks the civ *ended* (cheap, no turn processing yet); `UndoEndTurn` clears the mark while the phase is still Playing. When every active human is ended, or the deadline fires (host issues logged `ForceEndTurn(civ)` per remaining civ, civ-index order), the host pumps the **Resolving** phase: `ProcessEndTurn(h)` for each human in civ-index order, `RunAiTurn(c)` for each auto civ in civ-index order (city states, barbarians, AI, defeated/spectator), `StartNextTurn` (ranking stats, `Turns++`), `BeginTurn(h)` for each human in civ-index order. The engine exposes `NextResolveStep()`, so the host only loops "ask engine, submit that server command" and crash recovery can resume a half-resolved turn from state. Human commands arriving in Resolving are rejected with `turn-resolving`.
- A hosted game with one human at civ index 0 produces exactly the sequential hashes (T200 asserts it): the new model is a generalisation, not a fork.

### 4.1 Timers, AFK, disconnect, join and leave
- Turn timer: optional lobby setting (seconds per turn, default off for friends; `0` = wait for everybody). The host owns the clock (`IClock`), the effect is the logged `ForceEndTurn`. The deadline is part of `turnStatus` so every client can show it.
- **Disconnect**: a dropped human does not block the turn forever. After the grace period (default 120 s, `DropGraceSeconds`) the host logs `ForceEndTurn(civ)` for that civ (marks it ended; the player may come back and `UndoEndTurn` while the phase is still Playing). Lobby setting `OnDisconnect = ai` (T267: the default; `wait` only force-ends) flips the civ to AI after `K` consecutive absent turns (default 3) via the logged server command `SetController(civ, AI)`, applied at the turn boundary (when the round has resolved, before anyone acts), never mid-turn; `Reclaim` returns it at the next turn start. An explicit leave or kick is different (next bullet).
- **Joining mid-game** (a free human slot, or reclaiming an AI-controlled slot): allowed between turns only (Resolving ends, Playing starts), by key-bound slot (section 6.2) - never into a civ another key owns. `SetController(civ, Human, playerKeyId)` is logged. A new joiner receives a full snapshot (section 2.3).
- **Leaving**: explicit `Leave` or a host kick (T267) changes nothing mid-turn: the civ stays human, its turn is force-ended at once (nobody is there), and `SetController(civ, AI)` is logged at the next turn boundary regardless of `K` and of `OnDisconnect`. A dropped connection is not a leave (grace plus `K` rules); the same player connecting again before the boundary cancels the takeover, and afterwards reclaims as after any takeover. The last living human is never handed over (a kick is refused with `not-allowed`; at the boundary a request that would hand over the last human is dropped and the civ stays human). A host rebind of a seat is the exception: the old player is replaced, so the civ is handed to the AI at once and the new key takes it straight back. Or `Resign` if the player says so. The Kotlin resign path already switches a civ to AI locally [V]; here it is a command.
- Resign, vote kick and chat are lobby messages that translate into the same commands.
- Turn-start summary: the notifications in the snapshot since the player's `lastAckSeq`.
- Async/play-by-mail is not a mode: simultaneous + no timer + a persistent host save. If the host quits, the game is saved and can be resumed (section 5.3).

## 5. Persistence and resync (host-owned, requirement 4)

### 5.1 Files [V design on T100]
Per game a directory under the host's data dir (`games/<gameId>/`; dedicated server: `--data-dir`; in-process: the user data dir). Plain files by default:
- `snapshot-<seq>.json.gz`: full server `GameInfo` serialized by `UncivFiles` (gzip+base64 JSON, Kotlin-compatible) [V]; T140 (Done) wired civs so the round trip works [V]; T102 asserts the T011 hash across save/load [V]. The simultaneous-turn fields (`TurnState`) are additive (Kotlin `UncivJson` has `ignoreUnknownFields = true` [V: `json/UncivJson.kt:23`]; the C# side ignores unknown members by default [I]), so the snapshot still opens in single player.
- `commands.log`: append-only JSON Lines, format owned by T194 (header incl. `turnMode`, `playerSettings`; entries `{seq,type,actor,now,payload}`; `{checkpoint:seq,turn,fullHash}` after every `BeginTurn` batch). Entries of both humans, server commands (`ProcessEndTurn`, `RunAiTurn`, `StartNextTurn`, `BeginTurn`, `ForceEndTurn`, `SetController`) interleave in one total order.
- `game.json`: lobby/meta (not part of `GameInfo`): `LobbySettings` (section 6.5), slot bindings (civ -> player public key id, display name), host key fingerprint, join-password *hash*, turn policy.
Write order: append + fsync the log line *before* acknowledging an accepted command; snapshot (tmp + rename) every N turns and always at a clean shutdown. Recovery = latest snapshot + `Replay` of the tail + verify the next checkpoint; a corrupt tail line is truncated and reported; a half-resolved turn resumes through `NextResolveStep()`.

### 5.2 Resync
- Client reconnect: `Resume{gameId, lastSeq, lastViewHash}` after the key challenge (section 6.2). If the host's per-client delta ring still has `lastSeq+1` it sends the missed `ViewDelta`s, else a fresh `Snapshot`. Deltas are regenerated by replaying the tail through the filter on a scratch copy, or kept in a small ring (decision left to T176; start with the ring). With simultaneous turns the gap is typically a few commands of another human, so a snapshot is usually the cheaper reply.
- Desync detection: host compares `fullHash` to the log checkpoint after replay (catches nondeterminism, not cheating); clients compare `viewHash` (2.6).
- Faulted processor (`IsFaulted`, COMMANDS.md [V]): the host reloads snapshot + log, refuses to continue past the failing command, quarantines it and tells the lobby.
- Save interplay: the host save is a normal Unciv save, so it opens in single player; a log is replayed only by the engine version that wrote it, across versions the snapshot is authoritative (the host snapshots at end of turn before an upgrade). Kotlin hashes are not comparable (T160) [V].

### 5.3 When the host quits (manual hand-off; optional automatic host migration, T268)
- **Clean quit**: the host stops accepting commands after the current command, takes a snapshot at the next safe point (end of Playing if a turn is mid-flight: mid-turn state is saved as is, including each civ's *ended* flag, which is why `TurnState` is in the game state), writes the `clean` marker, sends `HostClosing` to clients and closes. Clients keep their identity key, last address and `lastSeq` and show "host went away; waiting".
- **Crash**: recovery from snapshot + log on next start (5.1).
- **Resume**: the host chooses "Host saved game"; slots stay bound to the same player keys; clients reconnect with `Resume` to the same `address:port` (or a new one; the invite can be re-copied). Players need not be online at the same time: a game can be played like a persistent world as long as *the host* process is up.
- **Manual host change**: zip the game directory and hand it to a friend; their host process loads the same bindings and clients reconnect with the same keys (new address). No protocol feature is needed, only the doc. This stays the fallback when everything else fails.
- **Automatic host migration (T268, go, opt-in, default off).** Full design, rejected alternatives, threat model per message and acceptance criteria: `design/HOST-MIGRATION.md`. Summary: the host designates one consenting player as *backup host* at lobby time and streams it the snapshot and the command log asynchronously (the successor sees full state, a stated trust change, section 6.6); a clean quit hands over losslessly (`HostHandoff`), a crash promotes the successor only after its own loss window plus a quorum of signed `HostLossVote`s from connected humans; a persisted `hostEpoch` fences the old host (`stale-epoch`) so there is never a second host; clients get the successor's endpoint and fingerprint from the host in advance (`HostSuccessor`, pinned, no TOFU prompt) and reconnect with the same keys through `Resume`, rolled back by a snapshot if they were ahead. The departing host's seat follows the T267 rule (section 4.1). Tickets T269, T272-T275.
- Retention: logs older than the latest snapshot are kept as a replay file (history, audit).

## 6. Network stack, identity, security (host-a-lobby, requirement 5)

### 6.1 Protocol messages (transport independent; JSON, one `type` per frame)
Client to host: `Hello{protocol, clientVersion, rulesetHash, modList, modSync?, modChecksums?}`, `Join{password?, playerKey, displayName, inviteToken?}`, `Auth{proof}`, `Resume{gameId,lastSeq,lastViewHash}`, `LobbySet{slot, nation, color, ready}`, `LobbyChat`, `Command{clientCmdId, type, payload}` (the `CommandEnvelope` minus `Seq`/`ActorId`), `Query{queryId, queryType, payload}` (T223, read-only; the viewer is the session's civ), `MapPing{x,y,kind}` (C090), `ModFileRequest{mod,path}` (T247), `ResyncRequest`, `Leave`, `Ping`.
Host to client: `Welcome{sessionId, challenge, hostKeyFingerprint, requiresPassword}`, `JoinOk/JoinRefused{code}`, `LobbyState{settings, slots[], hostName}`, `GameStarting`, `Snapshot`, `ViewDelta{seq, events}`, `CommandResult{clientCmdId, accepted, code, seq}`, `QueryResult{queryId, ok, code, payload}` (T223), `TurnStatus{turn, ended[], deadline, phase}`, `MapPingNotice{x, y, kind, sender}` (C090), `ModManifest{baseRuleset, mods, rulesetHash, entries[]}` and `ModFileData{mod, path, data}` (T247), `StandbyOffer{keyId}`, `StandbyRevoke`, `StandbyStatus{state, lagEntries}` and `HostSuccessor{epoch, successor, status, roster[]}` (T281, backup host, design/HOST-MIGRATION.md; the client sends `StandbyAccept{endpoints[], port, fingerprint}` and `StandbyDecline`), `StandbyMeta`, `StandbySnapshot`, `StandbyEntry` (T272, standby replication; the designated successor sends `StandbySubscribe` and `StandbyAck`), `HostClosing`, `Error{code}`, `Pong`. The protocol version is 3 since T272 (2 since T281; new message types).
Remote queries (T223, `Net/Remote`): the host dispatches `Query` on the game actor via `RemoteQueryHost` (order: `turn-resolving` while Resolving, exposure policy, per-session and per-bound-civ token buckets (a reconnect does not reset the civ one) with a per-query cost, size-bounded per-view-seq cache); `RemoteQueryPolicy` holds an allow/deny decision with a reason for every registered query (a test fails when a new query has none; an undecided query is denied); a denied query answers exactly like an unknown one; every query failure collapses to one `query-rejected` code with no text. Clients use `RemoteQueryClient` (async, correlation by `queryId`).
Map pings (C090): `MapPing{x,y,kind}` with kind Look, Danger or Help is not an engine command (not logged, hashed or replayed). `GameHost` drops it for an unknown kind, a tile off the map or not explored by the sender, and beyond `HostLimits.MaxPingPerWindow` (4) per `PingWindowMillis` (5 s) per session; otherwise it sends a `MapPingNotice` (sender = the seat's bound display name, never from the frame) to the sender and to every open session whose civ has a defensive pact with the sender AND has explored the tile, so a ping never shows an ally a place they do not know. The engine has no teams, so "teammates/allies" means defensive-pact partners. The sender name is stripped of control characters and cut to `MaxPingNameChars` (24); another session of the sender's own civ gets nothing. The client shows a ping for 8 s and never stores it.
Rules: strict schema, max frame size, unknown `type` is an `Error`, never an exception; `rulesetHash` mismatch rejects at join (`ruleset-mismatch`) unless the client said `modSync` in its Hello, in which case the mod sync below runs (T247; before it, in v1, mod files were never transferred, T170); `JoinRefused` codes do not reveal whether a name or slot exists beyond what the lobby already shows.

Mod sync (T247): a `Hello` with `modSync: true` and a mismatching `rulesetHash` (or, because the hash covers rule names only, with `modChecksums` that lack a downloadable host mod or differ from it) does not end in a refusal: the handshake goes on (Welcome, the client checks the host key as always, Join with password or invite, Auth key proof), and only an authenticated, admitted connection gets a `ModManifest` instead of `JoinOk`. Nothing about the mods is revealed before that, and the invite token is checked but not consumed on this connection (the re-join needs it). The manifest lists the lobby's base ruleset and mods, the host's combined `rulesetHash`, and one entry per downloadable mod (a mod that exists as a folder on the host; built-in rulesets are never sent): its name, the files (`path`, `size`, SHA-256 of the UTF-8 content) and a `checksum` = SHA-256 over the sorted `path NUL size NUL sha LF` lines, so equal checksums mean equal mods. The client validates the manifest before requesting anything, shows the player what is missing or different and how big it is, and downloads only after a yes: one `ModFileRequest` per file, answered by `ModFileData` (base64) or an `Error{mod-file-refused}` with no detail. The host serves only a file listed in the manifest it sent to that connection, once per connection, after re-reading it and checking size and SHA-256 again; with no manifest sent every request is refused. When the host cannot offer anything (no downloadable mod, or one is over the caps) the authenticated joiner gets `JoinRefused{mod-sync-unavailable}`.
Security rules for the transfer (transferred mod content is untrusted data from another player, and the host's content is untrusted for the client): (1) **only ruleset data**: the allowed paths are exactly the fixed ruleset json names of `RulesetFile.Values`, directly in the mod's `jsons` folder; images, sounds, music, atlases, scripts and any other file are never listed, requested or written, and nothing is executed (the json loader reads data); (2) **no path traversal**: file names come from the allow-list and mod names must match `[A-Za-z0-9][A-Za-z0-9 _.'-]{0,63}` with no trailing space or dot, no Windows device name and no built-in ruleset name, so no separator, `..`, drive, stream or hidden folder can occur, and the written path is re-checked to be the staging folder; (3) **caps**: 2 MiB per file, 16 MiB per mod, 32 MiB and 8 mods per sync, enforced by the host when building and by the client on the manifest and on every received byte; (4) **verification**: each file must equal its listed size and SHA-256 and be strict UTF-8, the staged mod must rebuild to the manifest's checksum and load as a ruleset before anything is replaced, and after the hot-load the combined ruleset must hash like the host's `rulesetHash`; (5) **no silent overwrite**: files are written under a hidden `.sync-staging-*` folder, an already installed different mod is moved to `.sync-backup-NAME` (one backup kept, with the images and sounds it had, which the new copy does not contain) and any failure of the swap restores it; the manifest must be self-consistent (entries only for its own base ruleset and mods; this is a sanity check, the real guard against a host naming an unrelated installed mod is the confirmation prompt that lists every name and the backup); one manifest is sent per connection (a repeat join is answered `auth-failed`); "nothing to offer" is remembered for 5 s and a served file that fails re-verification drops the cached manifest; the client's installed-mod checksums travel in the Hello, i.e. before the host key is checked (a privacy trade-off: names and content hashes of installed mods are visible to whatever address the player connects to), a player who declines the download cannot join that lobby; a mod whose name differs from an installed one only in case is refused; (6) one download per join attempt (a host that still disagrees afterwards is refused, never looped on), a per-file answer timeout, and no download without the player's confirmation. The client then hot-loads the mods into `RulesetCache` (`RulesetCache.ReloadSingleRuleset`, on the thread that owns the cache), ends the session with `mods-synced` and joins again with the host's hash, so no restart is needed. A mod's non-ruleset assets (images, sounds) are not transferred; a mod that depends on them shows placeholders until installed by hand.

### 6.2 Identity and join (no accounts server)
Two independent secrets with different jobs:
1. **Admission** = a **lobby password** (optional, host-chosen; `none` is allowed for LAN) or a single-use **invite token** the host can mint for one friend. It answers "may this person enter the lobby at all". It travels only inside the encrypted channel (6.4) and the host stores only a salted hash.
2. **Slot ownership** = the client's **ECDSA P-256 keypair** (BCL `ECDsa`; Ed25519 is not in the BCL [I]). At first join the key is bound to a display name and, on slot choice, to a civ (trust on first use). Reconnect and resume prove possession by signing a fresh host challenge. A leaked password therefore cannot hijack a seat that is already taken. Lost key = host clears the slot binding in the lobby UI.
No account database, nothing to phish, nothing to run 24/7. Rejected: shared password as the only identity (any holder impersonates anyone), OAuth/Steam/Discord (external dependency).

### 6.3 Transport (replaces decision D1)
**Raw TCP with length-prefixed JSON frames, wrapped in TLS (`SslStream`), behind `ITransport`.** No ASP.NET/Kestrel, no reverse proxy, so the in-process host in the Godot client and the dedicated `Unciv.Server` use the same `System.Net.Sockets` code and there is nothing extra to deploy. WebSocket stays a possible second `ITransport` implementation (browser client), not built. ENet/UDP rejected again (NAT pain, Godot-bound).
Default port `47821` (configurable). IPv4 and IPv6 dual-stack listen.

### 6.3a Transport robustness and TLS platform matrix (T225)

- **Frames:** a wire frame is a 4-byte big-endian length plus UTF-8; the top bit means "more chunks follow". A message is cut into chunks of at most `MaxFrameBytes` (4 MiB) and reassembled up to `MaxMessageBytes` (32 MiB), so a large-map snapshot never throws. Messages under 4 MiB keep the old wire format.
- **Outbound queue:** the host wraps each session transport in `BoundedSendTransport` (default 256 frames or 64 MiB queued). Sends never wait on the peer; a peer over the bound is disconnected as a slow reader instead of stalling the actor.
- **Liveness:** clients ping every 20 s once joined (`KeepAliveInterval`); the host answers `Ping` on the session reader, not the actor, so an AI turn never makes a live client look dead; any inbound frame counts as liveness and a session silent for 90 s (`HostLimits.IdleTimeoutMillis`, checked in `GameHost.Tick`) is dropped. A client that hears nothing for 75 s (`HostSilenceTimeout`) treats the host as dead and reconnects.
- **Inbound caps:** the host reads at most `MaxFrameBytes` (4 MiB, no continuation chunks) per message from clients; only clients accept the 32 MiB chunked messages (snapshots). A client that does not ping is dropped after the idle timeout, and a test that jumps a `ManualClock` by more than 90 s without inbound traffic detaches its sessions.
- **Resync limit:** at most one `ResyncRequest` snapshot per session per 2 s; extra requests coalesce into one snapshot sent by the next `Tick` after the window.
- **TLS matrix:** both ends enable `Tls13 | Tls12` (TLS 1.3 preferred, TLS 1.2 as the fallback, still with the pinned host key). Verified in CI: Linux (SslStream/OpenSSL) negotiates TLS 1.3 (test accepts 1.2 or 1.3). Windows (SChannel) is covered by the T179 verification. Not verified: macOS (SslStream over Network.framework/Secure Transport), Android in server mode, and WASM (no raw TCP in browsers, so the web export cannot join a host directly). The client reports `NegotiatedProtocol` on the TCP transport for diagnostics.
- **Frame loss (T249):** TCP never loses a frame silently, so loss is modelled at the frame level: `NetFrameLossTests` puts a deterministic lossy link (drops chosen `ViewDelta` ordinals) between a real `ClientSession` and a real `ServerLobby`/`GameHost`. Two lost deltas are detected as `sequence-gap` on the next delta, one `ResyncRequest` heals the mirror to the host's seq, and the next delta applies normally. Lost frames that are never followed by another frame are healed by the next delta, the 20 s keep-alive, or the host-silence reconnect; a lost `CommandResult` frame is not injected or tested here.
- **Join and ping during an AI turn (T249):** the lobby (`Hello`/`Join`/`Auth`/`Ping`) runs on `LobbyHost`'s own readers and the in-game `Ping` on the session reader, neither on the game actor. `LobbyJoinAndPing_AreAnsweredWhileAnAiTurnOccupiesTheGameActor` holds the actor busy and completes two full `Hello`/`Join`/`Auth` handshakes on new connections (a new player is refused with a code because a started lobby has no free slot, a known key is seated again) plus a `Ping`/`Pong` on each before releasing it. Joining a started game as a new player is not supported (`AllowJoinInProgress` is not honoured by `LobbyHost` yet).
- **Unverified platforms, manual steps (T249):** these cannot be run on the Linux CI/dev machine and are NOT claimed as working. To verify, build `Unciv.Server` and the client on the target, host from Linux (`dotnet run --project Unciv.Server`), join with the target's client using the invite fingerprint, and check `TcpTlsTransport.NegotiatedProtocol` on the client (TLS 1.2 or 1.3, handshake succeeds only with the right fingerprint; a wrong fingerprint must be refused).
  - macOS: run the client on macOS 13+ (Intel and Apple silicon), join over LAN; repeat with the macOS machine as host (`Unciv.Server` or the Host button) and join from Linux.
  - Android: Android is client-only in v1. Install the APK, join a Linux host by invite over Wi-Fi, confirm `NegotiatedProtocol`; hosting from Android is out of scope until someone runs the same steps in server mode (the certificate generation via `HostCertificate.Generate` needs checking on the Android crypto provider).
  - WASM: browsers have no raw TCP, so the web export cannot join a host; there is nothing to test for TLS until a WebSocket gateway exists (a new ticket if wanted); the manual check is that the web export cannot reach a host and fails cleanly.

### 6.4 Transport security (recommendation: TLS 1.3 + host-key fingerprint pinned on first use, fingerprint carried in the invite)
- On first run the host generates a **self-signed ECDSA P-256 certificate** (the *host key*, stored in `host-key.pfx`/PEM in the data dir, long-lived). The server presents it in the TLS handshake.
- The **invite code** is a short string `unciv://<host>:<port>/<fp-base32>[/<token>]` where `fp` is the SHA-256 of the host certificate's public key (first 128 bits, grouped for reading aloud). A client that joins through an invite verifies the fingerprint *before* sending anything: no first-use window, MITM-proof.
- A client that types only `address:port` gets **TOFU**: the UI shows the fingerprint, "trust this host?", and stores it in `known-hosts.json` (like SSH). A later mismatch is a hard stop with a "host key changed" screen, never a silent accept.
- A **shared lobby secret as the transport key** (PSK) is rejected: password-derived keys are offline-brute-forceable by anyone who records a handshake, and the password already has a job (admission). The password is only ever sent over the pinned, encrypted channel.
- TLS 1.3 only (`SslProtocols.Tls13`), no client certificates (the identity signature is in-protocol), AEAD ciphers by platform default. Unencrypted mode exists only as an explicit `--insecure-lan` flag for tests and is off by default.
- Hardening: max frame size, handshake and join timeouts, per-IP connection and failed-join limits (back-off after 5 bad passwords), max sessions, frames never logged with snapshot content.

### 6.5 Hosting, lobby and NAT
**Who hosts.** Either a dedicated headless `Unciv.Server` (a friend's always-on PC, a home server, a cheap VPS if someone *chooses* one: nothing here depends on it) or the **Host** button in the client, which starts the same `GameHost` in-process, listening on the port, with the host player connected through the in-memory/loopback transport (one code path, 7). The host's save directory is the game (5).

**Dedicated executable (T265).** `dotnet run --project Unciv.Server -- --port 47821 --max-players 4 --auto-start --min-humans 2 --print-invite` opens a `ServerLobby` on a TCP+TLS port (host key in `--data-dir/host-key.pfx`, the fingerprint players pin), prints `LISTENING <port>` and `INVITE unciv://...` on stdout and logs the rest as levelled lines. There is no host player: friends join, pick a seat and ready up like in any lobby, and `--auto-start` starts the game once every seated human is ready and `--min-humans` have joined (without it the lobby waits for an admin; there is no remote admin console). `--settings FILE` is the `LobbySettings` JSON (`UncivJson`), the command line overrides it. `ServerLobby` now makes the started game simultaneous (`LobbySettings.TurnMode`) and gives it the host clock, as the other host paths always did. **Persistence, resume, stop (T270).** `--game-id ID` keeps the game in `DATA-DIR/games/ID` (the T176 store: `game.json`, `commands.log`, snapshots, `clean` marker). If the folder already holds a game the server resumes it *before the port opens*: `ServerLobby.ResumeGameAsync` restores the saved seats as bound human slots (so exactly those keys reconnect through the late-seat path and everybody else gets `game-already-started`), gives the session the host clock (without it a resumed game refuses `EndTurn`), applies the stored turn timer rules (`game.json` `timerPolicy`; the turn options of `--settings` are ignored on resume and the log says so), restores the backup-host designation and finishes a round the stop interrupted. Recovery notes (fell back to a snapshot, commands lost) are logged as warnings and the resume line prints the state hash (`ReplayVerifier.FullHash`) so an operator can compare it with the saved one. A new game from the lobby is saved from its first moment (header with `TurnMode`, first snapshot, seats, lobby settings without the password). A lock file `games/ID.lock` keeps two servers off one game. Without `--game-id` nothing is saved (a warning says so). `SIGINT`/`SIGTERM` (Linux and macOS; Windows only Ctrl+C) close the port, finish the running command and round, snapshot, write the clean marker, send `HostClosing` and exit 0; a second signal ends the process at once. `--password-file` reads the join password from the first line of a file (warns when others can read it), the password is never logged or written to disk. Exit codes: 0 stopped (also after a signal), 1 failure while running (port in use, game folder in use, lobby that cannot start), 2 bad command line or settings, 3 the saved game cannot be loaded (a supervisor should not restart on 3).

**Run guide and distribution (T266).** `Unciv.Server/README.md` is the operator guide (ports, data dir, invite and fingerprint, resume, signals, firewall, systemd example); self-contained linux-x64 and win-x64 builds are release assets (`Unciv-server-<rid>`), the Windows one UNVERIFIED.

**Reaching the host (MVP scope).**
| Path | MVP | Notes |
|---|---|---|
| Same LAN | yes | Show the host's LAN addresses in the Host screen; invite = `192.168.x.y:47821`. |
| Manual port forward (TCP 47821) / public IPv6 | yes, documented | Doc page with router steps, "test from another network", firewall prompts on Windows/Linux. The Host screen shows local addresses and offers *Copy invite*; the public IP is "look it up (e.g. your router page)": no phone-home service. |
| Overlay VPN (Tailscale, ZeroTier, WireGuard) | yes, documented | Zero router config, works behind CGNAT; the host uses the overlay address in the invite. This is the recommended answer to CGNAT. |
| UPnP/NAT-PMP auto port mapping | optional ticket | BCL-only minimal SSDP+SOAP/NAT-PMP, off by default, shown as "Try to open the port automatically"; failures are reported, never fatal. Not a dependency of anything. |
| LAN discovery | optional ticket | UDP broadcast beacon (`47822`): lobby name, version, slots free, port; "Games on your network" list in the join screen. Beacons carry no secrets. |
| NAT hole punching / relay | out of scope | Needs a rendezvous server, which is exactly the infrastructure the owner ruled out. If ever wanted, the overlay VPN covers it. |

**Lobby.** `LobbySettings` (host-editable until Start; players see them read-only):
`gameName`, `map` (`MapParameters`: type, shape, size, world wrap, resources, seed field hidden), `speed`, `difficulty`, `era`, `victoryTypes`, `maxMajorCivs`, `cityStateCount`, `aiFill` (empty slots become AI civs when the game starts), `noBarbarians`, `ruleset` + `mods` with **`rulesetHash`** (joiners with a different hash are refused with the host's list), `turnMode` (fixed Simultaneous in the UI), `turnTimerSeconds`, `onDisconnect`, `password`, `maxPlayers`, `allowJoinInProgress`. The settings map onto `GameSetupInfo`/`GameParameters` and the `NewGame(setup, seed, clock)` entry point (T199); the seed is chosen by the host and never sent.
`Slot{index, kind: Human|AI|Closed|Open, nation|Random, color, playerKeyId, displayName, ready, connected}`. Rules: nation uniqueness and `Random` resolution by the host (using `PlayerPickerTable`-equivalent rules from T199), a joiner takes the first Open slot or a chosen one, everyone sets *ready*, the host can Start when all human slots are ready (or force), kick, close slots, chat. Starting builds the game with each human slot's `PlayerId` = player key id, writes `game.json`, takes the first snapshot and sends everyone `Snapshot`.

### 6.6 Trust model (state it to players)
Clients are untrusted (sections 2-3). The **host is trusted**: it holds the full `GameInfo` and the save, and an in-process host's player can in principle inspect their own process. Friends-only; a group that wants hidden-information integrity against the host uses a dedicated server run by a third party and no backup host. With the optional backup host (`design/HOST-MIGRATION.md`, off by default) the designated successor also holds the full state and the salt, so two players are trusted; every joiner sees who before joining. The anti-cheat filter still matters: it stops a modified *client*, which is the realistic threat.

## 7. Godot side (C006 and the host/lobby slices)

- **Library vs scene split**: `Unciv.Client.Net` (plain C#, no Godot types, tested over the in-memory transport) holds the transport client, key store and known-hosts store, `ClientSession` state machine (Disconnected / Joining / Lobby / Syncing / InGame / Reconnecting), the **client mirror** (`ClientGameState` from `Snapshot`, mutated by `ViewDelta`s), `TurnStatus`, command submission with `clientCmdId` retry, `ClientEvent` stream.
- **Godot** (`Unciv.Godot`): Host screen (settings, address list, Copy invite, Start hosting), Join screen (address:port or invite, TOFU prompt, password), Lobby screen (slots, nation pick, ready, chat), in-game multiplayer bar (who has ended, deadline, End turn / Undo end turn, reconnect overlay), main-thread pump. See `design/CLIENT-SLICES.md` slices C006, C034-C036.
- **Single player uses the same path**: an in-process `GameHost` + loopback (and hotseat uses `TurnMode.Sequential`); this is also the test rig. C004/C005 consume `ClientEvent`s, not `GameInfo`.
- Local persistence on the client: identity key, `known-hosts.json`, recent invites, last `seq`/`viewHash` per game; never the snapshot.

## 8. Decisions (owner decisions override the earlier recommendations D1-D6)

| # | Question | Decision | Source |
|---|---|---|---|
| D1 | Transport stack | **TCP + TLS 1.3 frames behind `ITransport`** (WebSocket/Kestrel dropped: no proxy, nothing to deploy) | consequence of D2 |
| D2 | Where does the game run? | **Player-hosted lobby on a port, joined by address:port** (Terraria model); dedicated headless server or in-process Host button; no VPS/Caddy/accounts | OWNER |
| D3 | Turn mode | **Simultaneous turns, required for the MVP**; sequential stays for single player/hotseat/tests | OWNER |
| D4 | Identity and join | **Lobby password / invite token for admission + client ECDSA key bound to the slot**; no accounts service | OWNER (no auth service), recommendation for the rest |
| D5 | Drop policy | `ForceEndTurn` after 120 s grace, player may resume and undo; optional AI takeover (`onDisconnect=ai`, 3 absent turns), reclaimable | recommendation |
| D6 | Mods | Ruleset hash must match; no mod download in v1 | recommendation |
| D7 | Transport security | **TLS 1.3, self-signed host key, fingerprint in the invite, TOFU for typed addresses** | recommendation |
| D8 | NAT | Manual forward + overlay VPN docs for the MVP; UPnP and LAN discovery optional tickets; no hole punching | OWNER (scope delegated) |
| D9 | Host leaves | Game saved, resumable by the same or another host process; optional automatic migration to one designated backup host (T268, `design/HOST-MIGRATION.md`), off by default | OWNER |
| D10 | Conflict model | Arrival-order, immediate application, first come first served (doc SIMULTANEOUS-TURNS) | recommendation |

### Owner decisions still open (each has a default; work proceeds on the default)
1. **Turn timer default.** Default: off for a new lobby (friends), configurable per lobby; `ForceEndTurn` order by civ index.
2. **Disconnected player at turn end.** Default: `ForceEndTurn` after 120 s, undoable on return; AI takeover opt-in. Alternative: the host always waits.
3. **Latency fairness.** Arrival order means a low-latency player wins contested tiles. Default: accept it (the host's own player has zero latency). Alternative: a short per-turn "simultaneous reveal" batching mode, rejected for the MVP because it removes immediate combat results.
4. **Human-to-human trade requests when the recipient is offline.** Default: the request stays pending across turns (Kotlin behaviour). Alternative: expire at turn end.
5. **Host trust.** Default: the host is trusted (6.6). Alternative: always require a dedicated server for ranked-style play.
6. **Joining mid-game.** Default: allowed between turns into an Open or AI-controlled slot with a free nation, host can disable.
7. **Port and discovery.** Default port 47821 and LAN beacon port 47822; change if the owner prefers others.

## 9. Tickets

See `ticket.py list` for live state. Dependency-ordered set:
protocol DTOs (T172) -> (host identity/join auth T173 | visibility filter T174 | persistence T176 | turn policy T177) -> engine simultaneous turns (T200-T203, engine track, parallel to the above) -> host core (T178) -> lobby (T204) -> transport (T179) -> client library (T180) -> in-process host (T205) -> end-to-end (T182) -> Godot (C006, C034-C036). Optional: LAN discovery (T206), UPnP (T207), host migration (T269, T272-T275; design/HOST-MIGRATION.md). Command catalogue tickets (T164, T186-T194) are unchanged except for the notes added by this revision.

## 10. Verified vs inferred summary

Verified: the Kotlin server is a file store; resign/skip mutate the whole GameInfo client-side; T011 hash is over the full serialized GameInfo; `UncivFiles` + T140 round trip; `CommandProcessor` semantics; View classes and `Tile.IsExplored/IsVisible`; Notification model; Kotlin `ignoreUnknownFields`; the exact list of engine readers of the current player (`design/SIMULTANEOUS-TURNS.md` section 2); combat randomness is state-derived, with no global RNG stream; human-to-human trade is a queued `TradeRequest`.
Inferred: snapshot size and per-reconnect cost; Ed25519 absence from the BCL; that C# `System.Text.Json` ignores unknown members for `TurnState` (T200 tests it); that `SslStream` TLS 1.3 with a self-signed ECDSA certificate works on every Godot export platform we target (T179 verifies on Linux/Windows); AI duration per round; whether the View classes cover every field the filter needs (T174 audits).
