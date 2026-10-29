# Command layer design (T012)

<!-- toc -->
## Contents

- [Why this exists](#why-this-exists)
- [Model](#model)
- [Determinism rules (so that lockstep stays possible)](#determinism-rules-so-that-lockstep-stays-possible)
- [Authority models](#authority-models)
- [Mapping from Kotlin entry points (first command set)](#mapping-from-kotlin-entry-points-first-command-set)
- [Identifiers](#identifiers)
- [Open questions (decide when the first real commands are ported)](#open-questions-decide-when-the-first-real-commands-are-ported)
<!-- /toc -->

How a player's intent becomes a change to game state in the C# engine. This is the contract between
the Godot client, a headless server, tests and AI. The skeleton lives in `Unciv.Core/Commands/`
(`CommandTests` shows every rule below working against a toy state, because `GameInfo` is not ported yet).

## Why this exists

In the Kotlin game there is no such layer. The UI mutates `GameInfo` directly:

- Unit actions are **closures** (`UnitAction.action: (() -> Unit)?`, built by `UnitActions.getUnitActions`), so
  they cannot be sent over a wire, logged or replayed. The AI reaches the same code through
  `UnitActions.invokeUnitAction`.
- Everything else is a direct method call from UI code: `UnitMovement.moveToTile`, `Battle.moveAndAttack` /
  `attack`, `CityConstructions.addToQueue` / `purchaseConstruction` / `removeFromQueue`,
  `CityExpansionManager.buyTile`, `TechManager.addTechnology`, `PolicyManager.adopt`,
  `DiplomacyManager.declareWar` / `makePeace` / `signDeclarationOfFriendship`.
- `GameInfo.nextTurn` runs the end of the human's turn, the whole AI round and the start of the next human turn in
  one call, and is tied to the UI: it reads `Clock.System.now()` / `System.currentTimeMillis()` for turn timing
  (`currentTurnStartTime`, `totalTurnTimeSeconds`), and touches `UncivGame.Current.worldScreen` and `DebugUtils`.

A client/server game needs the opposite: one place where an intent is checked and applied, in a defined order, with
a record of what happened.

## Model

```
client intent ──► CommandEnvelope ──► authority: Validate ──► Apply ──► GameEvents ──► clients
                  {seq, type,                    (pure)       (deterministic)   (facts, past tense)
                   actorId, payload}
```

- **`ICommand<TState>`**: `Validate(state, actorId)` is pure and returns `CommandValidation` (ok, or a stable `Code` plus
  a message the UI may show). `Apply(state, actorId)` mutates the state and returns the `GameEvent`s it caused.
- **Actor identity is not in the payload.** The authority passes the authenticated session's civ id in. A client cannot
  claim to be someone else, and `Validate` is where "is it your turn / your unit" is enforced.
- **`CommandEnvelope`** is the wire form: `Seq` (assigned by the authority, `0` inbound), `Type` (stable string),
  `ActorId`, `Payload` (a JSON object). `CommandRegistry<TState>` maps `Type` to the command class and (de)serializes
  with `UncivJson` options. Unknown types and bad payloads are **rejected results**, not exceptions: they are untrusted
  input.
- **`CommandProcessor<TState>`** is the single writer of a game's state.
  - `Submit` (server role): decode, validate, apply, assign `Seq = last + 1`, append to the log, return a
    `CommandResult`. A rejected command changes nothing: no state, no log entry, no sequence number.
  - `Replay` (follower / lockstep role): applies an already numbered envelope. Out-of-order `Seq`, or a command that
    no longer validates, is a `CommandDesyncException`: the follower has diverged and must resync from a snapshot.
  - `Submit(actor, command)` serializes the command and decodes it again, so the in-process path applies exactly what
    the wire path would. Do not add a shortcut that skips this.
- **Faulting.** `Validate` passing means `Apply` must not fail. If it throws anyway the processor sets `IsFaulted`
  and refuses further work, because the state may be half modified. Recover by rebuilding from a snapshot plus the
  log. (We deliberately do not try to roll back: cloning a whole `GameInfo` per command is too costly, and a thrown
  `Apply` is a bug to fix, not a case to support.)
- **Events** are facts for presentation and sync (`UnitMoved`, `CityFounded`, `UnitDamaged`, ...), never requests.
  Clients animate them; they do not recompute game logic. Serialization of events uses the same registry pattern; it
  is added with the first real events (see open questions).

## Determinism rules (so that lockstep stays possible)

A command log plus an initial state must reproduce the final state with no other input. So inside `Validate`/`Apply`:

1. **Time** is never read. Anything time-like (turn timers, "minutes before force resign") is either outside the
   engine or an explicit field of the command, supplied by the authority and logged with it.
2. **Randomness** comes only from the RNG owned by the state (`GameRandom`, T010), advanced only by `Apply`.
   `Validate` never draws. Where Kotlin derives a generator from state with `stateBasedRandom(caller, seed)`
   (hash of caller name, seed and a `GameContext`/state hash) the port must reproduce the hash bit-for-bit (T017).
3. **Iteration order** over anything that feeds state is defined (see `AGENTS.md`).
4. **No UI**: no `UncivGame.Current`, `GUI`, popups or screens in `Apply`. Notifications and popup alerts become
   events or state, not calls.
5. After a turn the state hash (T011) is compared between authority and followers; a mismatch is a desync, handled
   as above.

Audit of the Kotlin logic (for the porting tickets, so they do not rediscover it): ~29 wall-clock reads outside
`multiplayer` (`GameInfo` turn timing, `MapParameters.seed` defaulting to `currentTimeMillis`), ~31 unseeded
`.random()` / `.shuffled()` / `.shuffle()` calls (mostly `GameStarter` and `mapgenerator` resource and start
placement, plus `AirInterception` and `Tile`), and 34 files in `logic/` that reference `UncivGame.` or `GUI.`.
Each needs a seeded or injected replacement when its file is ported.

## Authority models

The same command log serves both:

- **Authoritative server (the plan).** Clients send envelopes. The server runs `Submit`, then broadcasts either the
  accepted envelope (cheap, followers `Replay`) or the resulting events/diff. A reconnecting client gets the latest
  snapshot plus the log tail. The server runs AI civs itself (they produce commands through the same `Submit`, with
  the AI civ as actor), so AI never needs a client.
- **Lockstep (kept possible).** Every peer `Replay`s the same ordered log, which needs only the determinism rules
  above and state-hash exchange. Nothing in the skeleton depends on a server existing.

Visibility: the server must not leak fogged information. Broadcasting raw envelopes is only safe for lockstep between
peers that trust each other; for the server model, send per-recipient filtered events/diffs.

## Mapping from Kotlin entry points (first command set)

| Command | Kotlin entry point today |
|---|---|
| `EndTurn` | `GameInfo.nextTurn` (split: end player's turn, AI round, start next turn; AI and timing pulled out) |
| `MoveUnit` (path or target tile) | `UnitMovement.moveToTile`, `headTowards`, `getShortestPath` |
| `AttackWithUnit` / `MoveAndAttack` | `Battle.moveAndAttack`, `attack`, `attackOrNuke` |
| `UnitAction` family (fortify, sleep, explore, automate, found city, build improvement, pillage, upgrade, promote, great-person actions, religion actions) | closures from `UnitActions.getUnitActions` / `invokeUnitAction`; each becomes a command carrying the unit id plus its parameters |
| `SetCityProduction` (add / remove / reorder queue) | `CityConstructions.addToQueue`, `removeFromQueue`, `moveEntryToEnd` |
| `PurchaseConstruction` | `CityConstructions.purchaseConstruction` |
| `BuyTile` | `CityExpansionManager.buyTile` |
| `ChooseResearch` | `TechManager` research queue (`addTechnology` is the effect, not the intent) |
| `AdoptPolicy` | `PolicyManager.adopt` (guarded by `canAdoptPolicy`) |
| `DeclareWar`, `MakePeace`, `ProposeTrade` / `AcceptTrade` | `DiplomacyManager.declareWar`, `makePeace`, trade logic |
| `Resign` (and later lobby-level actions) | not part of the engine; handled by the server layer |

Rules for writing a command from a Kotlin entry point: the `Validate` is whatever the UI used to decide the action was
offered (`canAdoptPolicy`, `UnitAction.action != null`, `canReach`); the `Apply` is the closure body. Do not duplicate
rules: extract the predicate and call it from both.

## Identifiers

Commands refer to things by stable ids, never by object reference: civ id (string), city id (string, see
`City.pseudoRandomId`), tile by `HexCoord`, unit by its `MapUnit.id` (Int). Names of rulesets objects are strings.
No command may carry a value the authority could compute itself (such as a path's cost): the authority recomputes.

## Open questions (decide when the first real commands are ported)

- Event serialization and the per-recipient visibility filter (above).
- Simultaneous turns: today `Validate` checks "is it your turn" per command. A simultaneous-turn mode would need the
  phase to live in state; the processor does not assume sequential turns.
- Idempotency token for client retries (a `ClientCommandId` in the inbound envelope, deduplicated by the server).
- Snapshot cadence for reconnect (every N turns or every M commands).
- Whether `EndTurn` should itself return its AI-round commands as separate log entries, so followers need not run AI.
  Recommended: yes, log them as commands issued by the AI civ, so `Replay` never runs AI code.
