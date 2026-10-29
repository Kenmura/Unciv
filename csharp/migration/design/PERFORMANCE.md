# Turn-time performance: harness, baseline and hotspots (T261)

<!-- toc -->
## Contents

- [The harness](#the-harness)
- [Baseline](#baseline)
  - [Per-turn growth](#per-turn-growth)
  - [Medium scenario, phases (ms per turn, run 3)](#medium-scenario-phases-ms-per-turn-run-3)
  - [Per civ (ms per turn, last quarter, start + automate + end)](#per-civ-ms-per-turn-last-quarter-start--automate--end)
  - [Checkpoint hash, small and medium](#checkpoint-hash-small-and-medium)
  - [Host-side view building (medium, turns 198-200, 3 recipients, 54 commands)](#host-side-view-building-medium-turns-198-200-3-recipients-54-commands)
- [Ranked hotspots](#ranked-hotspots)
- [Targets recorded for T262 and T263](#targets-recorded-for-t262-and-t263)
- [Follow-ups](#follow-ups)
<!-- /toc -->

GitHub issue #2: turns take a while to process. This page is the measurement that T262 (optimise) and T263 (parallelism spike) start from.
Nothing here is an optimisation; the only engine change is the `TurnProbe` measurement seam.

## The harness

`dotnet run -c Release --project Unciv.Cli -- bench [--scenario small|medium|all] [--turns N] [--json FILE|-] [--host-turns K] [--recipients R] [--no-probe]`
(`bench --help` prints the options). It is a CLI command, not a test: plain `dotnet test` never runs it. It generates two fixed-seed games itself (no save
files) on the generated Brave New World ruleset, plays them through the command layer the way the BNW soak does (scripted human, AI civs through `RunAiTurn`,
a state-hash checkpoint after every human turn start, nothing else scripted) and reports wall time per turn split by phase.

| Scenario | Civs | City states | Map | Seeds (game / map) | Turns |
|---|---|---|---|---|---|
| small | 4 | 2 | Small, Small Continents | 11 / 5 | 50 |
| medium | 8 | 8 | Medium, Continent and Islands | 23 / 9 | 200 |

- **Probe, not a clock in the engine.** `Unciv.Utils.TurnProbe` lets the engine announce nested, named phases (`TurnProbe.Measure("Start.Cities")`) to an optional
  observer; it reads no clock, no state, and costs one thread-static read when nobody listens. The bench is the observer and does all timing with `Stopwatch`
  (one narrowly justified suppression). Phases are in `TurnManager` (start, automate, end of a civ, with the civ id as subject), `GameInfo.RefreshTransientCaches`
  and `UpdateCivilizationState`, the AI (`AI.*`), the command layer (`Command.Apply`, subject = command type), the checkpoint hash (`Hash.*`) and the host view
  builders (`View.*`). Add a `Measure` line where a drill-down is needed; the bench lists every phase it sees.
- **Report.** Per turn: wall ms, state hash, city and unit counts, GC pause and gen-2 count, inclusive ms of the main phases. Per scenario: the top 20 phases by inclusive
  time (with exclusive time, calls, mean per call) for the whole run, the first quarter and the last quarter, the time per civ (start, automate, end), per command type, and
  the `lateMedianWallMs` (median of the last fifth of the turns) that T262's target uses. Inclusive time of a parent contains its children; exclusive does not.
- **Host cost.** For the last K turns (medium: 3) the bench acts as R recipients (default 3, the first living majors) of a `GameHost`: around every command it does what
  `GameHost.CaptureViewMarks` and `BroadcastDelta` do (`ViewDeltaBuilder.Capture` before; `BuildWithDiff` and `ProtocolCodec.Encode` after), without engine events. That time
  is taken out of the engine numbers (it is booked on a separate recorder and subtracted from the clock), so the engine columns are those of a game without recipients.
- **Proof of unchanged state.** `--no-probe` plays without the probe and without host measurement. `BenchTests` (Unciv.App.Tests, every build) compares the per-turn
  state hashes of a probed run with host measurement to a run without the probe: they must be equal. The JSON lists the hash of every turn (the golden list T262 freezes).
- **Tools not used.** `dotnet-trace`, `dotnet-counters` and a sampling profiler are not installed in the `dotnet-dev` container; only the Stopwatch instrumentation points
  above were used. They would add call-level detail below the listed phases (see follow-ups).

## Baseline

Machine: Intel Core Ultra 7 265 (20 logical cores), 30 GB, Fedora 44 container, .NET 10.0.9, Release build, workstation (non-server) GC, one game thread.
The machine was shared with other agents' builds and tests, so **timings vary by about +-30% between runs** (late medians of three consecutive medium runs:
933, 823 and 549 ms; the first used fewer probe points). Compare before/after in the same session, several runs each, never against these absolute numbers
alone. The shares between phases were stable across the runs.

### Per-turn growth

| Scenario | Median turn, first quarter | Median turn, last quarter | Late median (last fifth) | Total |
|---|---|---|---|---|
| small (turns 1-50) | 77 ms | 103 ms | 107 ms | 4.7 s |
| medium (turns 1-200) | 179 ms | 556 ms | 549 ms (run 2: 823) | 72 s (run 2: 99 s) |

The medium game ends with 17 civs (6 major AI, 8 live city states in the last quarter), 30 cities and about 230 units. The scripted human dies at turn 167, after which
the host plays its turns and the checkpoint hash stops: the per-turn hash numbers below are for the turns before that, the final-state hash is measured separately.
Per-turn spikes are large: the slowest medium turns (190-196) take 1.2-2.7 s while their neighbours take 0.6 s; the spike is in `RefreshTransientCaches` (up to 1.7 s
in one turn) and coincides with a gen-2 GC; the cause was not isolated (follow-up).

### Medium scenario, phases (ms per turn, run 3)

| Phase | First quarter (turns 1-50) | Last quarter (turns 151-200) | Calls per turn (last) |
|---|---|---|---|
| Command.Apply (all engine work of the commands) | 181 | 652 | 20 |
| RefreshTransientCaches | 106 | 380 | 19 |
| - UpdateCivilizationState (inside it) | 89 | 351 | 19 |
| - - CityStatsUpdate (`city.CityStats.Update`) | 33 | 141 | 322 |
| - - TileStatsAndHappiness | 12 | 88 | 322 |
| - - UnitVisibleTiles | 13 | 40 | 322 |
| - - UpdateOurTiles | 17 | 39 | 322 |
| - - UpdateSightAndResources | 12 | 23 | 322 |
| - - CitiesConnectedToCapital | n/a | 21 | 322 |
| TurnManager.AutomateTurn (AI) | 41 | 186 | 16 |
| - AI.AutomateUnits | 29 | 156 | 15 |
| - - AI.AutomateUnitMoves (per unit, 223 calls) | 21 | 142 | 223 |
| - - AI.Homeland (queue incl. unit moves it drives) | 18 | 70 | 15 |
| TurnManager.StartTurn | 20 | 60 | 17 |
| - Start.Cities | 13 | 47 | 17 |
| TurnManager.EndTurn | 8 | 23 | 17 |
| - End.Cities | 3 | 10 | 16 |
| AI.Tactical / AI.UseGold | 3.5 / 3.9 | 7 / 6 | 15 |
| Checkpoint hash (`Hash.FullHash`, human alive) | 5 | n/a (human dead) | 1 |

`AI.Homeland` and `AI.AutomateUnitMoves` overlap (the homeland queue calls the same unit moves). The command-layer overhead outside the phases is negligible
(`Command.Apply` self time 1.4 ms per turn). Per command type (whole run): `run-ai-turn` 3199 calls, 20 ms mean; `begin-turn` 10 ms; `end-turn` 9 ms; everything the scripted
human does (unit actions) is below 1 ms per call.

### Per civ (ms per turn, last quarter, start + automate + end)

Majors cost 30-40 ms each (Russia 40, China 39, Greece 35, England 32, Rome 32, Egypt 30); city states 3-11 ms (Antwerp 11, France 10, Quebec City 8, ...);
Barbarians 0.2; the dead human 0.7. A major's turn is roughly 55% AI (`AutomateTurn`), 35% start, 10% end. These sum to about 270 ms of the 650 ms of engine time:
**the other ~380 ms per turn is `RefreshTransientCaches`, which is charged to no civ** (it runs after every civ step).

### Checkpoint hash, small and medium

`ReplayVerifier.FullHash` serialises the whole game to JSON and FNV-hashes the text. With the human alive it costs 5 ms per turn on the medium game at turns 1-50 (4 ms
serialise, rest digest) and 2.4 ms on the small one; for the medium game's final state (turn 200, 30 cities, 17 civs) serialise is 9.3 ms and digest 1.9 ms. It is **one
checkpoint per human turn start, not per civ**, so it is under 2% of a late turn. The soak's 3 s per turn (B026) therefore does not come from the hash: it ran a Debug
build and, per 25 turns, a save/load round trip and a full replay, and scripted BNW actions through the dev console that this bench does not play.

### Host-side view building (medium, turns 198-200, 3 recipients, 54 commands)

| Step (per recipient, per command) | ms |
|---|---|
| `ViewDeltaBuilder.Capture` (before) | 0.49 |
| `SnapshotBuilder.Build` | 2.31 |
| `SnapshotDiff.Compute` | 1.71 |
| delta events (`Build`, no engine events passed) | 0.74 |
| `ProtocolCodec.Encode` | 0.10 |
| **Total** | **~5.4** |

A late turn has about 20 server commands (every `RunAiTurn`, the `BeginTurn`, the human's commands) and every connected player is a recipient for each of them, so the host
adds roughly 5.4 ms x 20 x recipients per turn: **about 110 ms per turn with one connected player, about 430 ms with four, about 860 ms with eight** at this game size, the same order as
the whole engine turn. The cost grows with the map explored and the unit count (the snapshot is rebuilt in full for every command). Only the first 3 recipients and 3 turns were
measured; the numbers are per recipient and linear in the recipients.

## Ranked hotspots

Ranked by their share of a late medium turn (last quarter, engine time about 650 ms per turn plus GC):

1. **`RefreshTransientCaches` is called after every civ step (19 times per turn) and each call runs `UpdateCivilizationState` for every civ (17), so the per-civ work
   (city stats, tile stats, vision, our tiles, connection to capital) runs 322 times per turn instead of 17.** About 380 ms per turn, 58% of the engine time, and 55 to 60% in
   every quarter; it grows with the city count (cities x civs x steps). The T218 rule (every step ends canonical, so a snapshot between steps reloads to the live state) is why
   it runs, but a civ whose state did not change in a step does not need its caches rebuilt. Largest inner items: `CityStats.Update` 141 ms, `UpdateTileStats`+happiness 88 ms,
   unit vision 40 ms, our tiles 39 ms. Optimising must keep every per-turn hash equal.
2. **AI unit automation, `AI.AutomateUnitMoves`: 142 ms per turn** (223 unit moves, 0.64 ms each, inclusive of pathfinding; 22% of the turn). Needs call-level data below the
   probe (pathfinding, `GetDistanceToTiles`, danger evaluation); candidate for a per-turn cache or a cheaper path query.
3. **Garbage collection: 10.7 s of 72 s in run 3 (15%), 47 gen-2 collections, 0.2 s per gen-2.** The turn allocates heavily (the `.ToList()` copies, LINQ, per-call dictionaries in
   the refresh); the biggest turns coincide with gen-2 collections. Not engine logic: allocation reduction in hotspot 1, or GC settings (concurrent/server GC, `TieredPGO`) in the host.
4. **`Start.Cities` (`CityTurnManager.StartTurn`) 47 ms per turn** and `End.Cities` 10 ms: per-city stats recomputation, partly the same `CityStats.Update` as hotspot 1.
5. **`UCS.CitiesConnectedToCapital` 21 ms** (part of hotspot 1): road/harbour connection BFS rerun for every civ at every step.
6. **Host view building, outside the engine turn: about 5.4 ms per recipient per command** (above). Not in the end-turn time of a single-player game, but it doubles it in an 8-player
   host game; an incremental diff builder (T178's follow-up) is the fix.

Small: the same order (refresh 52%, AI 20%, start 18%), turn 103 ms at turn 50.

## Targets recorded for T262 and T263

- T262 (optimise): the top three hotspots are 1 (refresh), 2 (unit moves) and 3 (allocation); target **at least 30% off the late median turn time of the medium scenario**
  (fallback bound from the ticket: 10%), measured as the median of three runs in the same session before and after, with every per-turn hash equal to the golden list.
- T263 (parallelism): per-recipient view building (hotspot 6) and the per-civ refresh work are the first candidates; the engine turn itself is sequential by rule.

## Follow-ups

- Call-level profiling inside `AI.AutomateUnitMoves` and the spike turns (install `dotnet-trace`; or add `Measure` points in pathfinding), and a GC-count column per phase.
- Incremental host view diff (T178 note), once the host cost per recipient is chosen as a target.
- A `--repeat N` option for the bench that reports the median of several runs (the machine noise is the main limit of the numbers above).
