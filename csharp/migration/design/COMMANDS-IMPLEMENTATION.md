# Command layer, human turn flow and engine query API (implementation design)

Status: design, no engine code. Refines `migration/COMMANDS.md` (T012 skeleton) and feeds `design/SERVER.md`
(T172-T182). Replaces the coarse ticket T164 by the tickets listed in section 10.
Marks: **[V]** verified against the Kotlin or C# source while writing this; **[I]** inferred, to be checked by the ticket named.

## 1. Summary of decisions

1. **Commands**: one `GameCommand` class per player intent, assembly-scanned by a `[CommandType("name")]` attribute (so domain
   tickets never edit a shared registry file), validated by the same predicate the Kotlin UI used to offer the action,
   applied by calling the existing engine function. Not built on the View `Try*` methods (they return only `bool`, some check
   nothing: `MapUnitView.TryMoveToTile`, `CityView.TryAddToQueue` [V]); those methods are used only as an **inventory** of what the UI mutates (about 60 of them).
2. **Turn flow**: `GameInfo.NextTurn` is split into three engine calls, `EndPlayerTurn`, `RunAutoPlayers` (one civ at a time) and
   `BeginPlayerTurn`; `NextTurn` becomes their composition with byte-identical hashes. Commands: `EndTurn` (human), `RunAiTurn(civ)` and
   `BeginTurn` (actor `server`). A pure `PendingDecisions` query says what a human must still decide before `EndTurn` is valid.
3. **AI is logged per AI civ, not per AI action.** This deliberately refines the COMMANDS.md recommendation and SERVER.md section 1
   ("AI as commands issued by the AI civ"): the AI code (`UnitAutomation`, `NextTurnAutomation`, ... about 100 files) mutates
   `GameInfo` directly; re-expressing it as commands would be a rewrite and changes nothing for replay, because replay runs the same
   engine version anyway (SERVER.md 5.2). One `RunAiTurn(civ)` log entry plus the checkpoint hash after each turn is enough to detect any divergence.
4. **State-affecting client settings move into the game** (per-civ `PlayerSettings`, logged in the header). Today rules read
   `UncivGame.Current.settings` equivalents through `GameClientState` (section 3.9). Without this a server and a client produce different states.
5. **Unit actions** are serialised as a descriptor `{Type, Key, Index}`; the authority regenerates `UnitActions.getUnitActions(unit, type)` and executes the matching entry. No closure ever crosses a boundary.
6. **Queries are serialisable request/response pairs** run by whoever holds the `GameInfo` (loopback host in single player, the server online),
   because the server design gives the Godot client a filtered mirror, not a `GameInfo` (SERVER.md 2.1, 7). They wrap the existing `View` classes (fog-correct) and never mutate.
7. **Events** come from a small sink inside the engine at existing choke points, not from diffing; fog reveals stay server-side diffs (SERVER.md 2.4).

## 2. Infrastructure (ticket T164, narrowed)

Existing skeleton (T012) [V]: `ICommand<TState>.Validate/Apply`, `CommandEnvelope{Seq,Type,ActorId,Payload}`, `CommandRegistry`, `CommandProcessor`
(Submit = decode, validate, apply, number, log; Replay = apply numbered envelopes; faults on a throwing `Apply`). Adds:

- `GameCommand : ICommand<GameInfo>` with a `CommandContext` that resolves ids and returns stable failure codes:
  `unknown-civ`, `not-your-turn`, `not-human`, `game-over`, `unknown-unit`, `not-your-unit`, `unknown-city`, `not-your-city`, `unknown-tile`,
  `pending-decisions`, `turn-resolving` (simultaneous: the round is being resolved), `already-ended` (simultaneous: the actor ended its turn), `out-of-sequence` (server step command that `NextResolveStep` does not name), `invalid-target`, `not-enough-<stat>`, `not-allowed` (UI predicate said no), `stale-reference` (alert/queue index no longer matches).
- Registry built by scanning `[CommandType]` (unique names, JSON round-trip test per class, ruleset objects by name, ids not references, no floats that must round-trip exactly: use ints).
- `GameSession` = `GameInfo` + `CommandProcessor<GameInfo>` + registry + the inputs a log needs (section 6).
- Turn check helper `CommandContext.RequireActive(actor)`: Sequential mode: `ActorId` is `CurrentPlayerCiv` and human; Simultaneous mode (hosted games, `design/SIMULTANEOUS-TURNS.md`, T200/T202): phase Playing, actor human, alive, not in `TurnState.Ended`; both: no `VictoryData` (except the post-victory commands in 3.1). Never test `CurrentPlayerCiv` directly in a command. The actor id `server` is reserved for 3.1 server commands; a client envelope claiming it is `malformed-command`.
- Purity rule, tested for every command: the full state hash (T102 `StateHasher` over `UncivFiles.GameInfoToString`) is equal before and after `Validate`, and after a rejected `Submit`. Engine predicates update lazy caches (`UpdateViewableTiles`, transients) so this will find real bugs.

## 3. Command catalogue

Naming: the wire `Type` is kebab-case of the class name. "Entry point" is the Kotlin function to call from `Apply` (same name in C#).
All commands: actor must be the current human player unless stated. Payload ids: civ id string, city id string, unit `MapUnit.Id`
(int), tile `HexCoord` (x,y ints), ruleset objects by name.

### 3.1 Turn and decisions (ticket T187; engine part T184)

| Command | Payload | Validate | Apply (entry point) |
|---|---|---|---|
| `EndTurn` | none (`Now` supplied by authority and logged) | current human; `PendingDecisions` has no blocking item (else `pending-decisions` listing them) | `GameInfo.EndPlayerTurn(now)` |
| `ForceEndTurn` | civ | actor `server` | as EndTurn, skips the pending check (timeouts, SERVER.md 4.2) |
| `RunAiTurn` | civ | actor `server`; civ is the next auto-processed civ | one iteration of the `NextTurn` auto loop |
| `BeginTurn` | none | actor `server`; no auto civ left before the next human | `BeginPlayerTurn(now)` |
| `MoveAutomatedUnits` | none | not `AutomatedUnitsMoveOnTurnStart`; `!HasMovedAutomatedUnits` | `CivView.TryAutomateAllUnits` + `HasMovedAutomatedUnits = true` (`TryMarkMovedAutomatedUnits`) |
| `SetPlayerSettings` | PlayerSettings subset | actor's own civ | writes `PlayerSettings` (3.9) |
| `AckAlert` | alert index + type + value | alert exists, is informational | remove from `PopupAlerts` |
| `RespondToAlert` | alert ref + choice | alert is a decision alert; choice legal | CityConquered: annex / puppet / raze / liberate / keep (`AlertPopup.kt` 560-650); DiplomaticMarriage; Demand accept/refuse (`Demand`); WarDeclaration-style "this means war" -> `DeclareWar`; RecapturedCivilian return/keep; Event option; DeclarationOfFriendship offer |
| `DismissPolicyPicker`, `DismissMoveSpies` | none | flag set | clears `PolicyManager.ShouldOpenPolicyPicker`, `EspionageManager.dismissedShouldMoveSpies` (saved state) |
| `Resign` | none | any time | civ becomes AI / defeated per `GameInfo` resign logic |

Traps: [V] the UI deletes popup alerts when it opens them (`AlertPopup.kt` ~135, ~679); `PopupAlerts` is serialised, so the removal must be a logged
command or the client and authority diverge. [V] `GameInfo.nextTurn` has a parameter `shouldGainTime` only for online multiplayer time banks; the
time bank is wall-clock state (`PlayerMinutesBeforeForceResign`) and must come from the logged `Now` or be dropped (multiplayer timers are T177's).

### 3.2 Movement and combat (T188)

`MoveUnit{unit, to}` (one-turn move, `UnitMovement.moveToTile`; stops early on discovered enemies, that is success with fewer steps),
`SetMoveOrder{unit, to}` (`headTowards` + `unit.action = moveTo x,y`, then `DoAction` continues), `SwapUnits{unit, to}` (`TrySwapMoveToTile`),
`Attack{attacker(unit|city), tile}` (`Battle.moveAndAttack` / `attackOrNuke`; defender chosen by `TargetHelper` at apply time), `Bombard{city, tile}`,
`Nuke{unit, tile}`, `AirSweep{unit, tile}`, `DisbandUnit{unit}`, `SetUnitDue{unit, due}`.
Validate = unit owned by actor, has movement, `CanReach`/`CanMoveTo`/`GetAttackableEnemies` contains the tile. The authority recomputes path and cost (never in the payload).
Traps: attack randomness only from the state RNG inside `Apply` [V: COMMANDS.md rule 2]; the UI "unit cycling cursor" (`UnitManager.nextPotentiallyDueAt`) is client presentation, not a command;
a human conquest produces a `CityConquered` alert only when `PlayerSettings` says so (3.9).

### 3.3 Unit actions (T189, T190) - see section 4.

### 3.4 City (T191)

`QueueConstruction{city, name, addToTop?, tile?}`, `RemoveFromQueue{city, index}`, `ReorderQueue{city, index, op: top|end|up|down}`, `PurchaseConstruction{city, name, stat}`,
`BuyTile{city, tile}`, `WorkTile{city, tile}`, `LockTile/UnlockTile/StopWorkingTile`, `AssignSpecialist/UnassignSpecialist{city, name}`, `SetManualSpecialists`, `SetCityFocus`,
`ReassignPopulation`, `ToggleAvoidGrowth`, `SellBuilding`, `AnnexCity`, `SetRazing`, `RenameCity`, `SetUnitSavedPromotion`, `SetConstructionDisabled`.
Validate: `CanAddToQueue`, `IsConstructionPurchaseAllowed` + `GetStatBuyCost` + `HasStatToBuy`, `Expansion.CanBuyTile`, city owned by actor and not puppet (production only).

### 3.5 Research, policy, religion, great people, espionage, congress (T192)

`SetResearchQueue{techs[]}` (UI edits a temp copy and commits on exit [V `TechPickerScreen`]; validates each tech researchable in sequence), `PickFreeTechnology{tech}`
(`getFreeTechnology`, requires membership in researchable set [V guard #4977]), `AdoptPolicy{policy}` (`CanAdoptPolicy`, `IsAdoptable`), `FoundPantheon/ChooseBeliefs/FoundReligion`
(belief names, religion name, display name), `ChooseFreeGreatPerson{unit}` (in `GetFreeGreatPersonOptions`), `MoveSpy{spy, city|null}`, `VoteDiplomatic{civ|null}`
(`MayVoteForDiplomaticVictory`), `SetGoldToScience{percent:int}`, `SetCivConstructionDisabled`. GodMode-only debug commands rejected unless `GameParameters.GodMode`.

### 3.6 Diplomacy, trade, city states (T193)

`DeclareWar{civ, reason}`, `Denounce`, `ProposeTrade{civ, offers}`, `RetractTrade{civ}`, `RespondToTradeRequest{requestId, accept}`, `MakeDemand{civ, demand}`, `GiftGold`,
`TributeGold/TributeWorker`, `PledgeProtection/WithdrawProtection`, `DiplomaticMarriage`. [V] human to AI trades are asynchronous: `TradeView.TryProposeStagedTrade`
only enqueues a `TradeRequest`; the AI answers during its own turn (`TradeAutomation.respondToTradeRequests`), so the verdict is a later notification, produced inside `RunAiTurn`.

### 3.7 Not engine commands

Lobby, identity, chat, kick, vote-to-resign (SERVER.md). Unit cycling cursor, camera, selection, tutorials, translation, sorting.

### 3.8 Determinism notes common to all commands

Time is an explicit logged field (`Now`) and only on 3.1 commands. Randomness: state RNG inside `Apply`; `Validate` never draws. Iteration order: the AGENTS.md rules.
The engine functions called from `Apply` already follow them (T102 passes); a new command adds only the order in which it calls them.

### 3.9 ClientState audit (ticket T183)

[V] `GameClientState` holds values Kotlin reads from `UncivGame.Current.settings`/`worldScreen`; those that change **state** are:
`AutomatedUnitsMoveOnTurnStart` (StartTurn runs `DoAction` for every unit), `CitiesAutoBombardAtEndOfTurn` (EndTurn),
`AutoAssignCityProduction` (`CityConstructions.ChooseNextConstruction`, gated by `IsUsersTurn()` = current civ's `PlayerId == ClientState.UserId`),
`AutoAssignSpecialistsInNewCities`, `AutomatedUnitsChoosePromotions` (AI always promotes), `AutoBuildingRoads`, `AutomatedWorkersReplaceImprovements`,
`StopAutomatedWorkersRemoveVegetation`, `UseAStarPathfinding` (changes paths: a game rule), `NotificationsLogMaxTurns`, and `AutoPlay != null` (see trap).
Trap [V]: `Battle.cs` ~774 raises the `CityConquered` alert for a human only if `ClientState.AutoPlay != null && !IsFullAutoPlayAI`; a headless host has `AutoPlay == null`
so a human civ would auto-conquer silently (same pattern in `UnitAutomation`, `WorkerAutomation`, `RoadBetweenCitiesAutomation`, `MayaCalendar`). Kotlin behaves the same without a world screen, so the
port is faithful; a server needs an explicit per-civ switch. Fix = per-civ `PlayerSettings`, stored in the game and in the log header; UI-only values (`AlternateUnitCycleOrder`, `Collator`, tutorials, audio) stay in `GameClientState`.

## 4. Unit actions as serialisable descriptors (T189, T190)

Kotlin `UnitAction.action` is a closure; `UnitActions.getUnitActions(unit)` is a lazy sequence; `invokeUnitAction(unit, type)` runs the first enabled action of a type [V].
C# `Models/UnitAction.cs` ports it with an `Action` delegate [V].

```
UnitActionDescriptor { Type: UnitActionType, Key: string?, Index: int, Params: map<string,string>? }
GetUnitActionDescriptors(unitId) -> [{ descriptor, enabled, titleTemplate, confirmation?, paramSchema?, priority }]   // query, no closure
ExecuteUnitAction { unitId, descriptor, confirmed: bool }                                                          // command
```

- `Key` is a stable parameter set by the action factories (a new read-only `UnitAction.Key`): target unit for `Upgrade`/`Transform`, improvement for `CreateImprovement`, unique text for `TriggerUnique` and the Hurry/TradeMission family, recipient civ for `GiftUnit`. `Title` is a translation template and is not a key.
- `Validate` regenerates `GetUnitActions(unit, type)`, selects by `(Key, Index)` (null key = first enabled, as `invokeUnitAction`), requires `Action != null`, requires `confirmed` when the regenerated action has a `Confirmation`, and checks `Params` against the schema (target tile for `Paradrop`/`AirSweep`, religion name for `FoundReligion`).
- `Apply` invokes the closure once, outside the sequence (several actions remove the unit or mutate the collections being enumerated).
- The list is state dependent: always validate against the **current** list. Worker orders (build/repair improvement with a queued improvement, stop, connect road) and promotions have their own commands (`SetWorkerOrder`, `PromoteUnit`, `SaveDefaultPromotions`) because they take structured arguments.

## 5. Human turn flow

### 5.1 What the engine does today (verified)

`GameInfo.NextTurn(nowMillis)` (C# `Logic/GameInfoTurn.cs`, identical to Kotlin `GameInfo.nextTurn`) [V]:
1. If the current civ is human and alive: add elapsed time to `TotalTurnTimeSeconds`, `TurnsPlayedAsHuman++`. Online multiplayer: update the force-resign minute bank.
2. If the current civ is human: `TurnManager.EndTurn`, then `SetNextPlayer` (index+1; when the index wraps to 0: `RecordRankingStats`, `Turns++`).
3. Loop while `IsSimulation || civ.IsAI() || (hotseat and defeated) || (online and spectator/defeated)`: `StartTurn`, `AutomateTurn`, (win/auto-play stop checks), `EndTurn`, `SetNextPlayer`.
4. The first civ that stops the loop becomes `CurrentPlayer`: `StartTurn`, spectator popup clear, `NotifyOfCloseEnemyUnits`, `NotificationCountAtStartTurn`.

Findings:
- A human civ is **never** passed to `AutomateTurn` by `NextTurn`; its decisions come only from the UI. Barbarians and city states are `PlayerType.AI` (barbarians branch inside `AutomateCivMoves`).
- **Quirk**: `GameStarter` never calls `TurnManager` [V grep], so on turn 1 the first human's `StartTurn` is not run; the new phase state must start "started".
- Human unit orders are not executed by `EndTurn`. `MapUnit.DoAction` (moveTo, explore, automate) runs at `StartTurn` only if `AutomatedUnitsMoveOnTurnStart`, otherwise on the player's "Move automated units" press (`NextTurnAction.MoveAutomatedUnits`, a required step before "Next turn") [V].
- `WorldScreen.nextTurn` clones the game, runs `nextTurn` on the clone and replaces the screen; there is no in-place API. A server mutates in place.
- Decisions that gate the next-turn button (`NextTurnAction` order) [V]: free great person, city with empty production (non-puppet), tech picker, policy picker, move spies, found/expand pantheon, found/enhance/reform religion, diplomatic vote; soft: due units, move automated units. Popup alerts (`AlertType`, 30 values) are modal in the UI but not enforced by the engine; some are information (WonderBuilt, TechResearched, GoldenAge, StartIntro...), some need a choice (CityConquered, DiplomaticMarriage, demands, Event, RecapturedCivilian, DeclarationOfFriendship, war-declaration choices).

### 5.2 Phased API (ticket T184)

```
GameInfo.EndPlayerTurn(now)            // step 1+2 above; no-op ending part if current civ is not human (as today)
GameInfo.RunAutoPlayers(now, max=1)    // loop body of step 3 for up to `max` civs; returns TurnPhase
GameInfo.BeginPlayerTurn(now)          // step 4
GameInfo.NextTurn(now)                 // = EndPlayerTurn; while (RunAutoPlayers(now,1) is AutoPlayers); BeginPlayerTurn  -> same hashes as today
TurnPhase = AwaitingInput(civ) | AutoPlayers(nextCiv) | GameOver(victoryData)
```
`TurnPhase` is derived (current civ, a non-serialised `started` flag, `VictoryData`); load re-derives it, so saves stay Kotlin compatible. The simulation flags (`SimulateUntilWin`, `SimulateMaxTurns`, `EngineOptions.SimulateUntilTurn`) remain handled inside `RunAutoPlayers` so the Simulation (T101) and T102 keep working; the T102 per-turn hash tests are the regression for the refactor.

### 5.3 Pending decisions (query + validation)

`PendingDecisions.For(civ)` returns an ordered list of `Decision { Kind, Subject, ResolvedBy, Blocking }` mirroring `NextTurnAction` order and `AlertType` classification
(5.1). Kinds: `FreeGreatPerson`, `CityProduction(city)`, `Technology`, `Policy`, `MoveSpies`, `Pantheon`, `FoundReligion`, `EnhanceReligion`, `ReformReligion`, `DiplomaticVote`,
`MoveAutomatedUnits`, `Alert(index)`, `TradeRequest(id)`, `PromotableUnits`, `DueUnits`. `ResolvedBy` names the command (e.g. `SetResearchQueue`). `EndTurn` rejects with `pending-decisions` while a blocking decision exists; the blocking set is the first group above plus `MoveAutomatedUnits` and decision alerts that leave state in limbo (CityConquered). Predicates already exist on `CivView` (`ShouldOpenTechPicker`, `ShouldShowPolicyPicker`, `CanFoundPantheon`, `MayVoteForDiplomaticVictory`...) and the free great person check is `GreatPeople.FreeGreatPeople > 0` (`WorldScreen.hasPendingFreeGreatPerson`, which also has a UI-only `deferFreeGreatPersonPicker` flag that is not state) [V]; the city check is `!IsPuppet && CurrentConstructionName == ""`. Each resolving command has a test that the decision disappears.

### 5.4 Server sequence and AI logging

```
client EndTurn  -> log entry (seq n)            Apply = EndPlayerTurn(now)
server RunAiTurn(c1) ... RunAiTurn(ck)           one log entry per AI civ, actor 'server', no checkpoint between them
server BeginTurn                                 BeginPlayerTurn(now); checkpoint {seq, turn, fullHash} after it
```
The host runs these as soon as the previous one returns, so AI time is bounded per civ and progress can be reported. `Now` for all four is the same logged value (as in `NextTurn`). Followers and replayers re-run the AI code; this requires the same engine version (already required, SERVER.md 5.2) and gives full-state hash verification at each checkpoint.
**Simultaneous turns are in scope for the MVP (owner decision; supersedes the earlier "sequential only")**, designed in `design/SIMULTANEOUS-TURNS.md`. The API above stays the Sequential path (single player, hotseat, T102 hashes). `EndPlayerTurn`/`BeginPlayerTurn` take the civ explicitly (T184), and T200 composes the simultaneous sequence on them: `EndTurn(h)` only marks the civ ended (new `UndoEndTurn` reverses it); when all active humans are ended or the host timer fires (`ForceEndTurn`), the host pumps server commands `ProcessEndTurn(h)` (civ-index order), `RunAiTurn(c)`, `StartNextTurn`, `BeginTurn(h)` driven by `GameInfo.NextResolveStep()`. `SetController` handles join/leave/AI takeover. The current-player readers that remain are listed and replaced in T201 [V list in SIMULTANEOUS-TURNS section 2]. Commands from several humans interleave in one `Seq` order; conflicts resolve first-applied-wins through ordinary state-dependent `Validate`.

## 6. Command log and replay (ticket T194; store = T176)

JSON Lines. Line 1 header `{formatVersion, engineVersion, rulesetHash, gameSetup, gameSeed, startClockMillis, playerSettings{civ->settings}}`; then
`{seq, type, actor, now?, payload}` for commands and `{checkpoint:seq, turn, fullHash}` after each `BeginTurn` (and on demand). Replay = `GameStarter.StartNewGame(header)` then `CommandProcessor.Replay` per entry;
the verifier stops at the first hash mismatch and reports its seq. Rejected commands are not logged (processor rule). Snapshot + tail: `UncivFiles` snapshot at seq k, replay k+1... Acceptance (the old T164 requirement): a `ScriptedHuman` plays 60+ turns; replay gives the same per-turn hash as the live run and
save/load/continue gives the same final hash; same seed twice gives the same log.
Hash = FNV (`StateHasher`) over the serialised `GameInfo` as in T102; Kotlin goldens remain T160's problem.

## 7. Events (ticket T186)

`GameInfo.EventSink` (null by default; set by the session while a command applies) is called from existing choke points: unit position set / movement memories, `AttackRecorder` and `Battle`, `CityFounder`, `CityConquestFunctions`, tile owner changes, `AddNotification`, `PopupAlerts.Add`, tech/policy/religion adoption, diplomacy status changes.
Event types carry `[EventType]`, registered by scan; SERVER.md requires a visibility classifier per type (T175). Order of events is the order of calls (deterministic); the sink is allocation-free when null.

## 8. Engine query API

### 8.1 Mechanism (ticket T185)
`IQuery<TResult>` + `QueryRegistry` + `QueryEnvelope{Type, ViewerId, Payload}`; `Run(GameView view)` returns a plain JSON record. `ViewerId` is the session's civ (like `ActorId`). Pure: state hash unchanged, no RNG draws.

Division of labour with the server design: **world state** the client draws (explored/visible tiles, units on tiles, cities, civ stats, notifications, awaiting-input signal) is the
`ClientSnapshot` and `ViewDelta` stream of T174/T175 (also in single player, through the loopback host); the **query API** answers derived questions that need engine code (previews, costs, panels, lists).
The client slices (design/CLIENT-SLICES.md, C009-C029) are re-pointed accordingly.

### 8.2 What already exists in C# (do not duplicate) [V]
`View/*` (T084, T085, T138, T139): `MapUnitView` (reachable tiles, shortest path, `CanReach`, attackable enemies, upgrades, promotions), `CombatantView` (strengths, modifiers, damage both ways),
`AttackableTileView`, `CityView`/`CityConstructionsView` (tile cost `GetGoldCostOfTile`, `CanBuyTile`, specialists, stat breakdowns, rejection reasons, buy costs), `TileView`, `CivView` (picker predicates, tech turns, victories, score breakdown, great-person points), `TradeView`, `ForeignCivView`, `DiplomacyManagerView.CanDeclareWar`.
Civilopedia text models (T095-T097, T108, T129), `EmpireOverviewCategories`, `RankingType`, `GameSetupInfo`, `GameStarter(setup, seed, clock)`, `Civilization.CalculateScoreBreakdown`, `VictoryManager` milestones, A003 scorers.
All of these return engine objects; none is serialisable or complete as a panel. Gaps, one per bullet:

| Need | Exists | Ticket |
|---|---|---|
| Reachable tiles with costs, path preview with turns per leg, attackable tiles, attack odds and damage ranges | per-call View methods; no multi-turn split, no DTOs | T195 |
| Tile purchase cost list, city panel, construction list with reasons and costs, worked tiles/specialists, tile info | `CityView`/`TileView` methods | T196 |
| Tech tree/research queue, policy tree, beliefs, free great persons, diplomacy and trade panels, notifications, overview | predicates and pieces | T197 |
| Trade evaluation for a human | **none in Kotlin** [V]: `diplomacyscreen` never calls `TradeEvaluation`; the AI answers on its turn. New optional `TradeHint` (bucketed, never the number) behind a game option | T197 |
| Victory progress, scores, rankings, demographics, history | `VictoryManager`, `CalculateScoreBreakdown`; A003 scorers are AI attractiveness, **not** player progress; T166 adapter reused for raw progress inputs only | T198 |
| New-game data: nations, difficulty, speeds, victories, map options, mod compatibility, validation | `GameSetupInfo`, `Ruleset`; selection rules live in Kotlin UI (`PlayerPickerTable.isChoosable`) | T199 |
| Unit actions in serialisable form | closure-based `UnitAction` | T189 (descriptor + query) |
| What the player still has to decide | `CivView` predicates, UI `NextTurnAction` | T184 |

Query traps: movement over unexplored tiles must not reveal terrain; damage estimates must not draw from the RNG; display Floats stay Float.

## 9. Cross-cutting semantic traps (collected)

1. Validate must be pure; verify with state hash before/after (lazy caches).
2. Popup alert removal by the UI is a state change (3.1).
3. `ClientState` settings are rules (3.9); `AutoPlay == null` changes human conquest.
4. First human turn 1 has no `StartTurn` (5.1); `NextTurn` as composition must keep that.
5. Human orders run only via `DoAction` (start of turn setting or explicit command).
6. Human to AI trades are asynchronous (3.6).
7. Payloads carry ids, not computed values (cost, path, damage); the authority recomputes.
8. Unit action list is state dependent; descriptors are validated against the current list.
9. No client-supplied time; `Now` is logged by the authority and used for `TotalTurnTimeSeconds`, `CurrentTurnStartTime`.
10. Single-writer: the host runs one command at a time; `RunAiTurn` can be long and blocks the game, not the host.
11. `UseAStarPathfinding` changes results: it is per game, not per player.

## 10. Build plan (dependency order)

| Order | Ticket | Scope | Depends |
|---|---|---|---|
| 1 | T183 | Engine player settings (3.9) | T071, T046 |
| 1 | T185 | Query infrastructure | T084, T085, T138, T139, T012 |
| 1 | T164 | Command infrastructure (narrowed) | T012, T071, T100, T140, T011 |
| 2 | T184 | Phased turn API (civ-parameterised), TurnPhase, PendingDecisions | T183, T102, T084 |
| 2 | T200, T201 | TurnState and simultaneous sequence; current-player reader audit | T184 (T183) |
| 2 | T186 | Event model and sink | T164 |
| 3 | T187 | Turn and decision commands | T164, T186, T184, T183 |
| 3 | T202 | Simultaneous-turn commands (UndoEndTurn, ProcessEndTurn, StartNextTurn, SetController, RequireActive) | T200, T187 |
| 3 | T188 | Movement and combat commands | T164, T186, T083 |
| 3 | T189 | Unit actions A (descriptors, orders) | T164, T186, T080, T185 |
| 3 | T191 | City commands | T164, T186 |
| 3 | T192 | Research/policy/religion/GP/espionage/vote | T164, T186 |
| 3 | T193 | Diplomacy, trade, city states | T164, T186, T086, T089 |
| 3 | T195-T199 | Query tickets (units, cities, empire, victory, setup) | T185 (+T166, T170, T099) |
| 4 | T190 | Unit actions B (unique families) | T189 |
| 5 | T194 | Command log, replay verifier, scripted human vs T102 hashes | T187, T188, T189, T191, T192, T102, T011 |
| 5b | T203 | Two-human determinism and conflict tests | T202, T188-T194 |
| 6 | T172-T182, T204-T207 (server track) | store/turn policy/event stream re-pointed to T194, T184+T187, T186 | see tickets |
| 7 | C004/C005/C006 | UI re-pointed to the command and query tickets | see tickets |

Parallelism: all "3" tickets write separate folders under `Unciv.Core/Commands/<Domain>/` and use attribute scanning, so they do not overlap in shared files (PROTOCOL.md overlap rule). T190 needs T189's descriptor classes. Dispatch limit 4 at a time.

## 11. Verified vs inferred, and open points

Verified: the `NextTurn` structure in Kotlin and C#; human civs are not automated by it; no `TurnManager` call in `GameStarter`; `NextTurnAction` decision order; alert handling and removal in `AlertPopup`; async human to AI trades; no trade evaluation in the Kotlin diplomacy UI; `GameClientState` settings list and the `AutoPlay == null` conquest path; View classes and their `Try*` mutators; `UnitAction` closure shape; tech picker commit logic.
Inferred / to be checked by the owning ticket: payload details of each diplomacy command (T193: which UI buttons map to trade offers vs `DiplomacyManager` calls); whether every Kotlin save ignores unknown fields so `PlayerSettings` can live in the save (T183); exact list of alerts that must block `EndTurn` (T184 reads `AlertPopup` per type); whether all unit-action families can be expressed with `Key`/`Params` (T190, may spin off tickets); cost of `RunAiTurn` per civ on large maps (T167 soak measures it); `SetGoldToScience` step size; which victory fields are public knowledge (T198); which `godMode` UI paths exist (T192).
Decision for the human owner: ask the owner only if they object to (a) the AI-per-civ logging refinement of SERVER.md, or (b) the optional `TradeHint` feature leaking AI valuation buckets.
