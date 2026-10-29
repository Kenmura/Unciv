# Simultaneous human turns: engine analysis and model (owner decision, MVP requirement)

<!-- toc -->
## Contents

- [1. Requirement and decision](#1-requirement-and-decision)
- [2. What the engine assumes today (verified)](#2-what-the-engine-assumes-today-verified)
- [3. The turn model](#3-the-turn-model)
  - [3.1 State (new, additive, in `GameInfo`)](#31-state-new-additive-in-gameinfo)
  - [3.2 Sequence](#32-sequence)
  - [3.3 Commands (T202)](#33-commands-t202)
- [4. Ordering, determinism, replay and hash](#4-ordering-determinism-replay-and-hash)
- [5. Conflicts between humans in the same turn](#5-conflicts-between-humans-in-the-same-turn)
- [6. Cross-human interactions in the same turn](#6-cross-human-interactions-in-the-same-turn)
- [7. Visibility (per-player) under interleaving](#7-visibility-per-player-under-interleaving)
- [8. Engine change list (tickets)](#8-engine-change-list-tickets)
- [9. Verified vs inferred](#9-verified-vs-inferred)
<!-- /toc -->

Status: design, no engine code. Companion to `design/SERVER.md` (hosting, lobby, security) and `design/COMMANDS-IMPLEMENTATION.md` (commands, phased turn API).
Marks: **[V]** verified by reading the cited source in this branch; **[I]** inferred, to be confirmed by the ticket named.

## 1. Requirement and decision

All humans act in the same turn at the same time. A turn ends when every active human has ended it, or the timer fires. Then the AI round and the turn processing run **once**. Sequential play is not enough.

**Model (recommended and adopted): per-civ phased turns with all living human civs active in parallel; the host serialises command application in arrival order, immediately, and logs that order.**
- Commands apply the moment the host dequeues them, against live state (combat, moves, founding resolve at command time; the player sees the result at once).
- The canonical order is the host-stamped `Seq`. The log is the order. Replay applies the log in `Seq` order, so interleaving of two humans is just a sequence.
- Turn processing is a deterministic civ-index-ordered sequence of logged server commands, driven by an engine-owned state machine.
- Sequential mode stays (single player, hotseat, tests) and is byte-identical to today's `NextTurn`.

## 2. What the engine assumes today (verified)

`GameInfo` holds a single `CurrentPlayer` (civ id string) and `CurrentPlayerCiv` (`Logic/GameInfo.cs:119,194`; Kotlin `GameInfo.kt:171`). `GameInfoTurn.NextTurn` is a rotation over `Civilizations` by index: the current human `EndTurn`s, the index advances (`Turns++` and `RecordRankingStats` when it wraps to 0), a `while` loop runs `StartTurn` / `AutomateTurn` / `EndTurn` for each AI, spectator, hotseat-defeated or online-defeated civ, and the first remaining civ becomes current and gets `StartTurn`, `NotifyOfCloseEnemyUnits`, `NotificationCountAtStartTurn` (`Logic/GameInfoTurn.cs:38-148`) [V].

The turn work itself is **per civ and does not read the current player** [V]: `TurnManager.StartTurn/EndTurn(civ)` (`Logic/Civilization/Managers/TurnManager.cs:31,312`) touch only that civ's cities, units, diplomacy (`diplomacy.NextTurn()` for its own side), tech, policies, religion, notifications and flags. `StartTurn` is where movement resets, healing and `DoAction` orders happen (`UnitTurnManager.StartTurn`, `StartTurn` line 80-88); `EndTurn` collects gold/science/culture/faith, runs `CityTurnManager.EndTurn` and the unit end-of-turn. So the *rule* work generalises to "any order of civs", provided the order is fixed and logged.

Every reader of "current player" in `Unciv.Core` (grep of `CurrentPlayerCiv|IsCurrentPlayer|IsUsersTurn|CurrentPlayer`, 42 hits, plus the Kotlin twins) [V]:

| Reader | What it does | Simultaneous handling |
|---|---|---|
| `GameInfoTurn.NextTurn` (lines 41-140) | the rotation itself | re-expressed on civ-parameterised phases (T184), sim sequence in T200 |
| `GameInfo.cs:289-296` `GetPlayerToViewAs` | which civ the local UI views | UI-only (client knows its civ); not used by rules |
| `GameInfo.cs:595-602` | init `CurrentPlayer` at load | stays; in Simultaneous it names the lowest-index human as a nominal cursor, `TurnState` is authoritative |
| `GameInfo.cs:756` `IsUsersTurn()` (= `CurrentPlayer.PlayerId == ClientState.UserId`) | read by `CityConstructions.ChooseNextConstruction:889` (auto-assign production only for the local user, or any when not online) | T183 already removes `UserId` from rules (per-civ `PlayerSettings.AutoAssignCityProduction`); no sim-specific work |
| `Civilization.cs:567` `IsCurrentPlayer()` | used below | audit ticket T201 |
| `Civilization.cs:584` `HasSpectatorVision` (defeated human in single player) | map reveal for a defeated sole human | single-player-only (`!IsOnlineMultiplayer`); keep, hosted games set `IsOnlineMultiplayer` |
| `Civilization.cs:1320` `RevealMapWhenDefeated` | same | same |
| `City.cs:549` `ReassignPopulationDeferred` (`IsCurrentPlayer && IsHuman`: reassign now, else defer to next `StartTurn` flag) | **behavioural**: only the current human reassigns immediately | T201: condition becomes `IsHuman() && civ is active` (Playing phase and not ended); otherwise deferred, as today for AI |
| `Tile.cs:1073`, `Battle.cs:781` | tutorial-task bookkeeping (`AddCompletedTutorialTask`) | client state only; T201 makes it a no-op without a client |
| `UnitActions.cs:234` `GetGiftActions` (`recipient.IsCurrentPlayer()` => no gift) | hides "gift unit" in the current player's own territory | T201: replace by `recipient == unit.Civ`-style check (the intent), same result in sequential |
| `View/CityView.cs:187` `CanChangeState` (`Viewer.IsCurrentPlayer()`), `View/CivView.cs:69,73` | UI gating | T201: `IsActive(viewer)` from `TurnState` |
| `Logic/Files/UncivFiles.cs:269` autosave name uses `CurrentPlayer` | cosmetic | host autosave uses the game dir, not this |
| `Simulation/SimulationStep.cs`, `Simulation.cs` | AI-vs-AI simulation | untouched, sequential |
| `IsOnlineMultiplayer` flag (`GameInfo.cs`, `City.cs:171`, `Civilization.cs:967`, `GameInfoTurn:67`) | online branches: force-resign minutes, defeated/spectator auto-processing, leader display suffix, specialist reset | hosted games use `IsOnlineMultiplayer = true`; `UpdateMinutesBeforeForceResign` reads the clock and is superseded by the host timer (T177); leave dormant |

Other single-point state [V]: `GameInfo.CurrentTurnStartTime` (one per game: fine, it is the shared turn start), `lastUnitId` counter (`GameInfo.cs:132`, unit ids are allocated in command order: deterministic given the log), `civ.TotalTurnTimeSeconds` / `TurnsPlayedAsHuman` (updated in `NextTurn` from `nowMillis - CurrentTurnStartTime`; in sim each human's own `ProcessEndTurn` carries its own logged `now`).

**Not a current-player assumption but a rotation assumption** [V/I]: `Civilizations` order is the processing order; in sequential play a human at index 1 sees the AI at index 0 already moved this turn. In sim the order is re-defined (section 3).

**Randomness is state-derived, not a stream** [V]: combat (`Battle.cs:407` `StateBasedRandom("Battle.takeDamage")`, `BattleDamage.cs:263` `GameRandom(Turns * tilePositionHash)`), automation and uniques all derive their RNG from game state (`GameContext.StateBasedRandom`, `Utils/Randomness.cs`). There is no global `Random` whose position depends on who acted first, so reordering two independent commands does not shift unrelated randomness. (Corollary, a known quirk kept: two attacks from the same tile in one turn get the same `GetRandomness`.)

Not an engine problem but a UI/Kotlin assumption [V]: `WorldScreen.nextTurn` clones the game and replaces the screen; `NextTurnAction` ordering; the popup-alert removal-on-open. Those are handled by the command design (T187), unchanged.

## 3. The turn model

### 3.1 State (new, additive, in `GameInfo`)
```
TurnState { Mode: Sequential | Simultaneous,
            Phase: Playing | Resolving,
            Ended: ordered set<civId>,        // humans that pressed End turn (Playing)
            Cursor: ResolveStep?,             // Resolving: next step, derived deterministically
            Started: set<civId> }             // civs whose StartTurn ran (replaces the 'started' flag of T184)
```
Serialised as an optional field (Kotlin ignores unknown fields [V `json/UncivJson.kt:23`]; the C# reader ignoring unknown members is [I], tested in T200). `Sequential` games never write it, so their saves are unchanged. `TurnPhase` of T184 is derived from this.

### 3.2 Sequence
```
Playing        all living humans active; any number of commands; EndTurn(h) / UndoEndTurn(h)
   all active humans Ended, or timer: ForceEndTurn(h) for the rest (civ-index order)
Resolving      (host loops: step = game.NextResolveStep(); submit the matching server command)
   1. ProcessEndTurn(h)   for each human h, civ-index order       = GameInfo.EndPlayerTurn(civ=h, now)   (TurnManager.EndTurn)
   2. RunAiTurn(c)        for each auto civ, civ-index order       = StartTurn, AutomateTurn, EndTurn     (city states, barbarians, AI, defeated/spectator)
   3. StartNextTurn       once                                     = RecordRankingStats; Turns++
   4. BeginTurn(h)        for each human h, civ-index order        = GameInfo.BeginPlayerTurn(civ=h, now): StartTurn, NotifyOfCloseEnemyUnits, NotificationCountAtStartTurn
Playing        (checkpoint {seq, turn, fullHash} written after step 4)
```
Why humans' `EndTurn` first and one AI round after: it is the owner's wording, it maps to existing code unchanged, and it equals sequential play when the only human is at civ index 0 (human `EndTurn`, then the AI `while` loop in index order, wrap, `Turns++`, human `StartTurn`). **T200 asserts this equality against T102 hashes** as the regression for the whole model. Defeated humans and spectators in a hosted game are auto civs in step 2 (the existing `ShouldAutoProcessOnlinePlayer` rule).
The first turn: `GameStarter` never calls `TurnManager` [V per COMMANDS-IMPLEMENTATION 5.1], so all humans begin "started" (`Started` = all humans).

Victory: `UpdateWinningCiv` runs inside `StartTurn`/`EndTurn` and already posts `GameHasBeenWon` to every other human [V `TurnManager.cs:420-436`]; in sim the check fires in Resolving, and `VictoryData` makes the phase `GameOver` (post-victory commands as in T187).

### 3.3 Commands (T202)
- `EndTurn` (human, Playing): Validate = `RequireActive` and no blocking pending decision; Apply = add to `Ended` (Simultaneous) or `EndPlayerTurn` (Sequential, as T187). `UndoEndTurn`: remove from `Ended`; only in Playing. `ForceEndTurn(civ)` (server) ignores pending decisions.
- Server commands `ProcessEndTurn(civ)`, `RunAiTurn(civ)`, `StartNextTurn`, `BeginTurn(civ)` are valid only for the step `NextResolveStep()` names (replay rejects anything else with `out-of-sequence`).
- `SetController(civ, Human|AI, playerKeyId?)` (server): flips `PlayerType`/`PlayerId`; legal in Playing between commands for a civ that is not the middle of anything, in practice issued at the Playing/Resolving boundary. A civ that becomes AI mid-Playing is added to the auto set for step 2 and removed from `Ended`.
- `RequireActive(actor)` replaces "actor is CurrentPlayerCiv": Sequential => as today; Simultaneous => Playing, actor human, alive, not in `Ended`. An ended human may still *answer* incoming requests (`RespondToTradeRequest`, `AckAlert`) because those do not spend the turn: they are allowed for any alive human in Playing, so ending the turn early does not make a player unreachable for a peace offer. (Their units cannot act.)
- Rejection codes added: `turn-resolving`, `already-ended`, `out-of-sequence`.
- **As built (T202).** `EndTurn`/`ForceEndTurn` mark the civ ended and the command that completes the set also calls `BeginResolving` and returns the `turn-resolving` event (a pure function of state, so the log needs no extra entry); `UndoEndTurn` is Playing-only. The step commands are `process-end-turn`, `run-ai-turn`, `start-next-turn`, `begin-turn` (the last two reuse the T187 names with a simultaneous branch). `SetController` joins/leaves/reclaims in Playing only (a joiner's turn begins at once, a leaver is played by step 2 without a second `StartTurn`; the last human cannot leave); `Resign` is the same hand-over, refused with `turn-resolving` during a resolve. `RequireActive(allowEnded: true)` is used by the four request-answering diplomacy commands.

## 4. Ordering, determinism, replay and hash

**Chosen order: host arrival order, applied immediately, `Seq` stamped by the single-writer host and written to the log before the ack.** For Resolving, civ-index order, driven by the engine cursor, also logged.

Rejected alternative: *batch* all human intents for the turn and apply them at turn end in a deterministic sort (civ index, then client sequence). It gives order independence from latency, but (a) it destroys immediate feedback (a Move needs the discovered terrain and ZOC stops; an Attack needs the result to continue), (b) many commands depend on earlier ones within a civ (found city then buy tile), which would have to be speculated or validated against a prediction, (c) it needs a second rule layer ("what was legal at issue time vs at apply time"). Rejected.

Properties and what tests prove (T203):
1. **Replay equals live.** Applying the logged `Seq` order reproduces every checkpoint hash. Wall-clock arrival, thread scheduling and network order are all absorbed by the log. The verifier (T194) needs no change except the header's `turnMode` and per-entry actors; it already compares at checkpoints and stops at the first diverging `Seq`.
2. **Same log twice => same hash**, byte-identical logs for the same scripted inputs and seed.
3. **Order sensitivity is real and defined.** Swapping two *conflicting* commands changes the outcome: the later one gets a deterministic rejection code. Swapping two *independent* commands of different civs (no shared tile, city, unit, diplomacy pair) must give the same final hash, **except** for the allocation counters (`lastUnitId`, event/notification indices, `Notifications` list order) which depend on order. Therefore the test asserts equality of a *canonicalised* hash (per-civ state hash with ids normalised) for independent swaps and full-hash equality for the identical order. This is the explicit determinism contract: *the full state is a function of the log order; independent commands commute on rule state*.
4. **Sequential equivalence** for one human at civ index 0 (section 3.2), on a 60-turn scripted run.
5. **Two-human ScriptedHuman replay**: both humans play 60 turns with interleaved commands drawn from a seeded shuffle of their per-turn scripts; the log replays to the same per-turn hashes; snapshot at turn k + log tail reproduces the final hash.
6. **Hash across filtered views**: `fullHash` stays server-side; each client's `viewHash` is computed over its own snapshot (SERVER.md 2.6). Interleaving changes nothing there: each recipient gets deltas in `Seq` order.
7. Time: `Now` is supplied by the host and logged on the entries that need it (`ProcessEndTurn`, `BeginTurn`, `RunAiTurn`); `ForceEndTurn` is the logged result of a timer, so replay needs no clock.

## 5. Conflicts between humans in the same turn

Validation always runs against the live state at the head of the queue, so every conflict below resolves as "first applied wins; the second is rejected by its own validation (state-dependent predicate), with a stable code, state unchanged (T164 purity rule)".

| Conflict | Outcome | Basis |
|---|---|---|
| Two units (different civs) move to the same tile | The first arrives. The second `MoveUnit` stops before the tile or is rejected `invalid-target`: `UnitMovement.CanMoveTo` refuses a tile with a foreign military unit (it is an attack target only if at war, via `Attack`); civilians stack rules as today | `UnitMovement` [V port], `MoveUnit` validation T188 |
| Two units attack the same unit/city | Serialised: the first changes health/ownership; the second recomputes against the new state (`TargetHelper` picks the defender at apply time [V per COMMANDS-IMPLEMENTATION 3.2]); a dead target gives `invalid-target` | T188 |
| Both found a city on the same or neighbouring tiles | First founder wins; the second `CanFoundCity`/`FoundCity` predicate fails (tile owned, min distance). The settler is **not** consumed and the player is told by the rejection | `UnitActions`, `CityFounder` T189 |
| A declares war on B while B accepts A's peace offer | Both orders are legal and deterministic. War first: `DeclareWar` removes pending requests between the pair (`DeclareWar.kt:155`) [V], so B's accept fails `stale-reference`. Peace first: the treaty is applied, then A's `DeclareWar` is an ordinary declaration against a civ at peace (subject to the usual peace-treaty restrictions, e.g. the 'recently made peace' limits in `DiplomacyManager`), so it may succeed or be rejected `not-allowed`; the log shows which | `DiplomacyManager`, T193 |
| Both sides propose conflicting treaties | Both stored as separate `TradeRequest`s; each recipient may accept one; accepting re-validates both sides' ability to supply (`TradeLogic`/`IsTradeValid`, which `TurnManager.StartTurn` also uses [V]); the loser's accept returns `stale-reference` | T193 |
| Two civs buy/claim the same tile (border growth, `BuyTile`) | First applied owns it; the second `CanBuyTile` fails | `Expansion.CanBuyTile` [V view method] |
| Both claim a one-per-game wonder / natural wonder reward / ruin | First `QueueConstruction`+completion at turn processing: wonder completion is resolved in civ-index order inside `ProcessEndTurn` (`CityConstructions` checks `IsBuilt` by other civs at that time); ruins are consumed by the first unit entering | turn processing / `RuinsManager` [V port] |
| Great person, religion founding limits, pantheon names | First applied; `FoundReligion`/`ChooseBeliefs` validity checks count already-founded religions and taken beliefs [V `ReligionManager`] | T192 |
| Two humans same research/policy | Independent state, no conflict | |
| Trade to a human who has ended | Delivered live; recipient may respond (section 3.3) | T193 |
| Stack of moves where A's unit is killed by B before A's queued `SetMoveOrder` runs | Orders execute in `BeginTurn(civ)` `DoAction`, after the kill; a missing unit is a no-op | `MapUnit.DoAction` [V] |

Within Resolving, all cross-civ effects follow civ-index order, the same deterministic rule the sequential game uses.

Information that must not leak through the interleaving (SERVER.md 3 still applies, per recipient): (1) a human sees another human's *visible* actions live, by design (units moving in sight); (2) `TurnStatus.ended[]` is public to the lobby (like "waiting for players"); (3) rejection codes are generic (no existence oracle: "unit exists but is not yours" is `not-allowed`); (4) timing of a second human's pending decision is not disclosed; (5) the *order* of commands is private (clients get only their filtered deltas, each carrying a `seq` that is the host's, so gaps in seq reveal that "something happened" but nothing about what: acceptable, listed as a residual leak in SERVER.md 3).

## 6. Cross-human interactions in the same turn

- **Trade, peace, friendship, demands, alliances** are the queued `TradeRequest` (`Civilization.TradeRequests`, `Logic/Trade/Trade.cs:64`; `View/TradeView.cs:48-66` enqueue/retract) [V]. Kotlin shows a request to a human via `TradePopup` (`WorldScreen.update` checks `viewingCiv.tradeRequests`) [V]. AI civs answer inside `AutomateCivMoves` (`TradeAutomation.RespondToTradeRequests`) [V]. For two humans nothing in the engine requires them to be in different turns, so the same queue works while both are active: the proposer's `ProposeTrade` command adds the request; the host pushes `TradeRequested` to the recipient (private-channel filter T181); the recipient answers with `RespondToTradeRequest{requestId, accept}` at any time in Playing; acceptance re-validates (`IsTradeValid`) and calls `TradeLogic.AcceptTrade`. Declining or a stale offer notifies the proposer. A request to an offline human stays pending (Kotlin behaviour; open decision 4 in SERVER.md); `TurnManager.StartTurn` still prunes requests that can no longer be supplied.
- **AI -> human**: unchanged; the AI adds `TradeRequest`s to the human during step 2 and the human sees them at the next Playing phase.
- **Popup alerts / notifications** are per civ lists already [V `Civilization.PopupAlerts`, `Notifications`]; other-civ events (first contact, war declared on you) add to the other civ's list immediately; delivery is an event to that recipient only. `NotifyOfCloseEnemyUnits` runs in each human's `BeginTurn`.
- **Decision alerts** (`CityConquered` etc.) are per civ and resolved by the owning human's `RespondToAlert`; with T183 the host sets `HumanConquestPrompt` so a human conquest yields the alert rather than silent auto-conquer [V `Battle.cs:774`].
- **Pending decisions** (`PendingDecisions.For(civ)`, T184) are already civ-parameterised and independent per civ [V design]; `EndTurn` consults only the actor's.
- **Combat resolves at command time**, never deferred: a deferred model would need a second set of validity rules and break the UI loop. City bombard/attack and `Nuke` likewise.

## 7. Visibility (per-player) under interleaving
The filter runs per accepted command per recipient (SERVER.md 2.4). Simultaneity adds two cases for T175/T181: (a) a recipient sees *live* changes outside their own commands (another human moving in their sight), so `ViewDelta`s are pushed unsolicited at any time, and the client mirror must tolerate deltas between its own `CommandResult` and the next command; (b) a unit that moves out of sight between two deltas is emitted as `UnitDisappeared` at the visible end, never as a path. Both are handled by the existing event-classifier rule; T203 adds a two-human fixture asserting that human B's mirror never contains tiles/units only human A could see, after an interleaved sequence.

## 8. Engine change list (tickets)
| Ticket | Change |
|---|---|
| T184 (edit) | `EndPlayerTurn(civ, now)`, `BeginPlayerTurn(civ, now)` take the civ explicitly (default `CurrentPlayerCiv`); `Started` is per civ. No behaviour change in sequential. |
| T183 (note) | `IsUsersTurn/UserId` out of rules (already the scope); confirm each human has its own `PlayerSettings` |
| T200 (new) | `TurnState`, `TurnMode`, `NextResolveStep()`, civ-parameterised phases composed into the simultaneous sequence, one-human == sequential hash test |
| T201 (new) | current-player reader audit and replacement (table in section 2) |
| T202 (new) | commands: simultaneous `EndTurn`, `UndoEndTurn`, `ProcessEndTurn`, `StartNextTurn`, `SetController`, `RequireActive`, codes |
| T203 (new) | two-human conflict, ordering, replay and visibility tests |
| T187/T164/T186/T193/T194/T174/T177 (notes) | gate helper, events (`PlayerEndedTurn`, `TurnStatusChanged`), human-to-human trade responses, log header `turnMode`, `turnStatus` in the snapshot, timer/disconnect policy |

## 9. Verified vs inferred
Verified: structure of `NextTurn`; per-civ `TurnManager`; the complete list of current-player readers (section 2); state-derived RNG (no global stream); `TradeRequest` queue and its Kotlin/AI handling; popup alerts and notifications are per civ; Kotlin `ignoreUnknownFields`; `DeclareWar` clears pending requests.
Inferred: that unit/city validation predicates (`CanMoveTo`, `CanFoundCity`, `CanBuyTile`, wonder `IsBuilt`) reject a stale second command in every conflict listed (T203 proves each, and files tickets for any that mutate or accept); that independent commands commute on rule state apart from id counters (T203 tests; if a shared lazily-computed cache breaks it, that is a bug to fix, not a model change); that the C# JSON ignores the unknown `TurnState` member in older readers; AI-round duration with many civs.
