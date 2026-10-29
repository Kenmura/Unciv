# Civilization V AI — Behavioral Requirements Spec (v0.1)

<!-- toc -->
## Contents

- [0. How to read this document](#0-how-to-read-this-document)
  - [Evidence tags](#evidence-tags)
  - [Provenance](#provenance)
  - [Constant recovery method (so you can trust the numbers)](#constant-recovery-method-so-you-can-trust-the-numbers)
  - [Legal/clean-room note](#legalclean-room-note)
- [1. Architecture overview](#1-architecture-overview)
  - [1.1 The key architectural contrast with Unciv (read this first)](#11-the-key-architectural-contrast-with-unciv-read-this-first)
- [2. Flavor system](#2-flavor-system)
- [3. Grand strategy selection](#3-grand-strategy-selection)
  - [3.1 Strategies [D]](#31-strategies-d)
  - [3.2 Per-turn algorithm  [O] — *byte-for-byte identical in base, G&K, and BNW* (only struct strides differ)](#32-per-turn-algorithm--o--byte-for-byte-identical-in-base-gk-and-bnw-only-struct-strides-differ)
  - [3.3 Tunables [D] (`data/ai_defines.tsv`)](#33-tunables-d-dataaidefinestsv)
- [4. Victory scorers (what changed with G&K and BNW)](#4-victory-scorers-what-changed-with-gk-and-bnw)
  - [4.1 Conquest](#41-conquest)
  - [4.2 Culture](#42-culture)
  - [4.3 United Nations / Diplomatic](#43-united-nations--diplomatic)
  - [4.4 Spaceship](#44-spaceship)
  - [4.5 Summary of version differences](#45-summary-of-version-differences)
- [5. Economic / Military / City strategies  *(inventory only — trigger logic not yet decompiled)*](#5-economic--military--city-strategies--inventory-only--trigger-logic-not-yet-decompiled)
- [6. Tactical AI — unit movement in combat  [O unless noted]](#6-tactical-ai--unit-movement-in-combat--o-unless-noted)
  - [6.1 Per-turn pipeline](#61-per-turn-pipeline)
  - [6.2 Move → handler table (BNW; generated from the dispatcher `101e16f0`)](#62-move--handler-table-bnw-generated-from-the-dispatcher-101e16f0)
  - [6.3 Postures (zone stance) [O]](#63-postures-zone-stance-o)
  - [6.4 Move priority data [D]](#64-move-priority-data-d)
  - [6.5 Core attack-assignment algorithm ("overkill") — `BNW:101d2420`  [O]](#65-core-attack-assignment-algorithm-overkill--bnw101d2420--o)
<!-- /toc -->

Audience: a programmer re-implementing Civ 5–style AI (target: a port of Unciv; language-agnostic).
Scope of v0.1: **grand strategy / victory pursuit** (base, G&K, BNW) and the **tactical (unit-movement) layer**'s
architecture. Diplomacy scoring, homeland (non-combat) unit logic, operations/armies, and city production are
inventoried but **not yet decompiled** (see §9).

---------------------------------------------------------------------------------------------------

## 0. How to read this document

### Evidence tags
Every non-trivial statement carries one of these:

| Tag | Meaning |
|-----|---------|
| **[O]** | *Observed* in the decompiled engine code (Ghidra output). Control flow and constants are as stated. |
| **[D]** | *Data*: read directly from the game's shipped XML (`Assets/**/XML/AI/*.xml`). |
| **[I]** | *Inferred*: consistent with the code, but a name/meaning was deduced (e.g. which of two values is "mine"). Verify before relying on it. |
| **[?]** | *Unknown*: code exists but was not understood/decompiled. Do not guess; treat as a gap. |

### Provenance
| Item | Value |
|------|-------|
| Base engine | `CvGameCoreDLLFinal Release.dll` (SHA-256 `03f1b7f0…ee17b`), 14,941 functions |
| Gods & Kings engine | `Assets/DLC/Expansion/CvGameCore_Expansion1.dll`, 16,923 functions |
| Brave New World engine | `Assets/DLC/Expansion2/CvGameCore_Expansion2.dll`, 19,551 functions |
| Tooling | Ghidra 12.1.4 headless, JDK 21; scripts in `tools/` |
| Function addresses | Given as `BNW:100f8760` etc. The DLL has **no symbols**; names in this doc are descriptive labels chosen by the analyst. |

**Important:** the three games ship *three different engines*. Behavior of "Civ 5 with BNW" is the **BNW** column everywhere
below. Do not mix base-game formulas into a BNW-faithful port.

### Constant recovery method (so you can trust the numbers)
The engine caches ~1,600 XML "defines" into one global struct. Ghidra lost the struct base, so each constant's
address was recovered by anchoring on a known pair (the AI "gold gift to a minor" code, which reads two named
defines back-to-back) and then verified: **15/16** of the independent `MC_GIFT_WEIGHT_*` globals in that function
landed on the predicted addresses in both G&K and BNW (base used the same anchor). Constant *values* come from XML, not from the binary.

### Legal/clean-room note
The decompiler output (`decomp.c`) is derived from proprietary code and is **deliberately not included** in this repo.
This document contains only behavioral descriptions and pseudocode written by the analyst, plus numeric tunables
that ship as plain XML game data. Consider your project's licensing before porting from a derived description.

---------------------------------------------------------------------------------------------------

## 1. Architecture overview

The Civ 5 AI is a **layered, flavor-driven, priority-queue** system. Higher layers set *weights*; lower layers pick
concrete actions.

```
Personality flavors (per leader, 0..20)                       [D] PERSONALITY_FLAVOR_MIN/MAX_VALUE = 0/20
        │
        ▼
Grand Strategy (4)  ── one active at a time, re-evaluated each turn, 10-turn lock          §3, §4
        │   sets flavor modifiers (AIGrandStrategy_FlavorMods)
        ▼
Economic / Military / City strategies (data-driven triggers)  — adjust flavors, build needs   §5
        │
        ▼
 ┌───────────────────────┬───────────────────────────────┬─────────────────────────┐
 │ Tactical AI           │ Homeland AI                   │ Operations / Armies     │
 │ (combat, zones,       │ (non-combat & idle units:     │ (multi-unit planned     │
 │  targets, postures)   │  explore, settle, workers…)   │  attacks, escorts)      │
 └───────────────────────┴───────────────────────────────┴─────────────────────────┘
        §6                              §7                          §8
```
[I] This layering matches the class list recovered from RTTI: `CvPlayerAI`, `CvCityAI`, `CvCityStrategyAI`, `CvArmyAI`,
`CvAIOperation*` (≈20 subclasses), `CvTacticalMoveXMLEntry`, `CvEconomicAIStrategyXMLEntry`, `CvMilitaryAIStrategyXMLEntry`,
`CvAIGrandStrategyXMLEntry`, `CvDllDiplomacyAI`, `CvDllDealAI`, `CvWonderProductionAI`, `CvFlavorRecipient`.

### 1.1 The key architectural contrast with Unciv (read this first)
| | Civ 5 | Unciv (inspected: `UnitAutomation.kt`, `Personality.kt`) |
|---|---|---|
| Unit control unit-of-work | **Per move type.** A global, priority-ordered list of *moves*; each move handler scans targets and recruits whatever units can serve them. [O] | **Per unit.** `automateUnitMoves` runs a cascade of `try*` behaviors for each unit in turn. |
| Strategy | Scored every turn, hysteresis (§3). [O] | `Personality.preferredVictoryType` is a static string. |
| Tuning | ~1,600 XML defines + XML tables. [D] | Mostly code constants. |

A faithful port needs a *target-centric* tactical layer (§6), not just more per-unit rules.

---------------------------------------------------------------------------------------------------

## 2. Flavor system

- A **flavor** is an integer weight for a priority (e.g. `FLAVOR_OFFENSE`, `FLAVOR_CULTURE`). Each leader has a personality value per flavor,
  range `0..20` [D].
- Engine-wide limits: `FLAVOR_MIN_VALUE=-1000`, `FLAVOR_MAX_VALUE=1000`, `FLAVOR_RANDOMIZATION_RANGE=2`,
  `FLAVOR_EXPANDGROW_COEFFICIENT=8` [D]; ranges for *how* these are applied beyond §3 are **[?]**.
- Flavors on one side, **recipients** on the other: anything that takes flavors (`CvFlavorRecipient`) re-weights its choices
  when the player's flavors change [I].
- Grand strategies *modify* flavors while active (`AIGrandStrategy_FlavorMods`) [D], see `data/grand_strategy_flavors.tsv`.

---------------------------------------------------------------------------------------------------

## 3. Grand strategy selection

### 3.1 Strategies [D]
`AIGRANDSTRATEGY_CONQUEST`, `_CULTURE`, `_UNITED_NATIONS`, `_SPACESHIP`. Identical list in base, G&K, BNW (no expansion override file).
Each has: a **flavor vector** (`AIGrandStrategy_Flavors`: CONQUEST→OFFENSE 9, CULTURE→CULTURE 10, UN→DIPLOMACY 10, SPACESHIP→SPACESHIP 13) and a
**flavor-mod vector** applied while it is the active strategy. Full tables: `data/grand_strategy_flavors.tsv`.

### 3.2 Per-turn algorithm  [O] — *byte-for-byte identical in base, G&K, and BNW* (only struct strides differ)
Entry: `BNW:100f9720` (base `100bb1f0`, G&K `100e20b0`).

```text
function evaluateGrandStrategy(me):
    # 1) score every strategy
    for s in strategies:
        base       = dot(strategyFlavors[s], me.personalityFlavors)      # BNW:100f7880; Σ_f W[s][f] * myFlavor[f]
        scorer     = victoryScorer[s](me)                                # §4; returns -100 if that victory is disabled
        noise      = random(AI_GS_RAND_ROLL)                             # =50  ("Grand Strategy AI: GS rand roll.")
        stick      = AI_GRAND_STRATEGY_CURRENT_STRATEGY_WEIGHT if s == me.current else 0   # =50
        score[s]   = base + scorer + noise + stick

    # 2) discourage chasing what rivals already chase
    met = number of alive major civs I have met (includes me)            # [I] inferred: self counts, avoids div-by-0
    for s in strategies:
        n = count of entries equal to s in me.guessedStrategiesOfOthers  # 11 slots x 2 (primary+secondary guess) [O]
        delta = -(((n * score[s] * AI_GRAND_STRATEGY_OTHER_PLAYERS_GS_MULTIPLIER) / met) / 100)   # multiplier = 50
        if delta != 0: score[s] += delta

    # 3) commit only after the lock expires
    me.turnsActive += 1
    if me.current == NONE or me.turnsActive >= AI_GRAND_STRATEGY_NUM_TURNS_STRATEGY_MUST_BE_ACTIVE:   # =10
        best = argmax(score)           # strict '>' so ties resolve to the FIRST index [O]
        if best != me.current:
            me.current = best; me.turnsActive = 0; notifyStrategyChanged(me)
```
Notes
- `random(n)` is the engine's seeded RNG (`"…"` is its log tag). Whether it returns `[0,n)` or `[0,n]` is **[I]**: assume `[0,n)`.
- The same loop in all three engines means **expansion changes live entirely in the four scorers** (§4) and the XML.
- How other civs' strategies are *guessed* (`GUESS_POSITIVE_THRESHOLD=120`, `GUESS_LIKELY_THRESHOLD=70`, `GUESS_NO_CLUE_WEIGHT=40`, fn `BNW:100f9260`) is **[?]** — not yet read.

### 3.3 Tunables [D] (`data/ai_defines.tsv`)
| Define | Base | G&K | BNW |
|---|---|---|---|
| `AI_GS_RAND_ROLL` | 50 | 50 | 50 |
| `AI_GRAND_STRATEGY_CURRENT_STRATEGY_WEIGHT` | 50 | 50 | 50 |
| `AI_GRAND_STRATEGY_NUM_TURNS_STRATEGY_MUST_BE_ACTIVE` | 10 | 10 | 10 |
| `AI_GRAND_STRATEGY_OTHER_PLAYERS_GS_MULTIPLIER` | 50 | 50 | 50 |
| `AI_GRAND_STRATEGY_GUESS_{POSITIVE_THRESHOLD,LIKELY_THRESHOLD,NO_CLUE_WEIGHT}` | 120 / 70 / 40 | same | same |

---------------------------------------------------------------------------------------------------

## 4. Victory scorers (what changed with G&K and BNW)

**The set of victory types is unchanged** [D]: `VICTORY_TIME, _SPACE_RACE, _DOMINATION, _CULTURAL, _DIPLOMATIC`
(`CIV5Victories.xml` is the same in base and both expansions). **What changed is how the AI scores and pursues them.**
Every scorer returns **−100** if its victory is disabled in game options [O].

### 4.1 Conquest
Base / G&K — `base:100ba670`, `GK:100e1120` (identical size) [O]:
```text
if not victoryEnabled(DOMINATION) and not <FUN_10093f50 = ?>:  return -100          # second predicate [?]
s = 10 * <FUN_1006d3c0 = ?>                                                           # [?] likely a flavor/personality read
if turn >= AI_GS_CONQUEST_NOBODY_MET_FIRST_TURN (20) and I have met no other major:
        s += AI_GRAND_STRATEGY_CONQUEST_NOBODY_MET_WEIGHT                             # -50
if turn >= AI_GS_CONQUEST_MILITARY_STRENGTH_FIRST_TURN (60) and reference > 0:
        r = ((myStrength - reference) * AI_GRAND_STRATEGY_CONQUEST_POWER_RATIO_MULTIPLIER (100)) / reference
        if r > 0: r = r / 2                                                           # only the upside is halved
        s += r                                                                        # [I] which operand is "mine" vs reference
if I am at war with anyone: s += AI_GRAND_STRATEGY_CONQUEST_AT_WAR_WEIGHT             # +10
if "cramped" (rivals I've met share my landmasses; computed over map areas):
        s += AI_GRAND_STRATEGY_CONQUEST_CRAMPED_WEIGHT                                # +20   [I] interpretation
return s
```
BNW — `BNW:100f8760` (4.8 KB vs 3.2 KB) [O]. Same terms as above **except** the base term, plus an extra tail:
```text
v0, v1, v2, v5 = four per-player lookups with indices 0,1,2,5                          # [?] likely yields or flavors
s  = (12 - X) * ( Y + ( max(v0, v1, v2) - v5 ) )          # X, Y: unknown helper results [?]
... (all terms from base) ...
tail: scan other alive majors; if a met one satisfies an unidentified predicate (>0): return s - 50   # [?]
```
Takeaway for a port: BNW makes the baseline conquest desire depend on *stat comparisons* instead of a flat flavor read.
The exact meaning of `X, Y, v0..v5` needs another decompile pass before it can be reproduced exactly.

A second, related function (`base:100b9840`, `BNW:100f7960`) computes the **estimated score of *another* civ for conquest** from
how many minors/majors it has attacked/conquered [O]:
```text
guess = ((myStrength - ref)*100)/ref  (if ref>0)  +  5*minorsAttacked + 10*minorsConquered + 10*majorsAttacked + 15*majorsConquered
        # weights: AI_GRAND_STRATEGY_CONQUEST_WEIGHT_PER_{MINOR_ATTACKED=5, MINOR_CONQUERED=10, MAJOR_ATTACKED=10, MAJOR_CONQUERED=15}
```
[I] used to guess what rivals are pursuing (feeds the §3.2 "chasing" penalty).

### 4.2 Culture
**Base** — `base:100b96d0` [O]:
```text
if victory CULTURAL disabled: return -100
cities, plus = <FUN_100f1270>, <FUN_100f7a50>                                          # [?] city counts
if policyBranchesOpen > 4:  return 1000                                                # "branches" via FUN_10131e60
return ((AI_GS_CULTURE_MAX_CITIES (4) - (cities - plus)) + 1) * AI_GS_CULTURE_MAX_CITIES_MODIFIER (10)
       + AI_GS_CULTURE_POLICY_BRANCH_MOD (25) * policyBranchesOpen
```
**G&K** — `GK:100e0110` [O]: same, plus a flavor term:
```text
... + (myFlavor[GROWTH] - myFlavor[EXPANSION]) * AI_GS_CULTURE_MAX_CITIES_MODIFIER (10)
```
i.e. G&K nudges toward culture for tall (growth-heavy, expansion-light) leaders.

**BNW** — `BNW:100f8b40` (rewritten around **tourism / influence**) [O]:
```text
if victory CULTURAL disabled: return -100
s = ((10 - X) * myFlavor[FLAVOR_CULTURE] * 200) / 100                                  # X unknown [?]
myCulture, myTourism = <FUN_1015bef0>, <FUN_10073fa0>                                  # [I] lifetime culture / tourism
for each other alive major I should count (excludes me and civs failing two filters [?]):
        n += 1
        if theirCulture  < myCulture:  cultureAheadOf += 1  else cultureNotAhead += 1
        if theirTourism  < myTourism:  tourismAheadOf += 1  else tourismNotAhead += 1
if n > 0:
        if cultureNotAhead < cultureAheadOf:
                s += ((cultureAheadOf - cultureNotAhead) * AI_GS_CULTURE_AHEAD_WEIGHT (50)) / n
        if tourismNotAhead < tourismAheadOf:
                s += ((tourismAheadOf - tourismNotAhead) * AI_GS_CULTURE_TOURISM_AHEAD_WEIGHT (100)) / n
s += AI_GS_CULTURE_INFLUENTIAL_CIV_MOD (50) * <FUN_1006e510 = number of civs I'm Influential over> [I]
return s
```
The old policy-branch term and the 1000 short-circuit are **gone** [O].

Also BNW changes the *rival-estimate* culture function (`BNW:100f7a10`) [O]:
```text
cultureTerm = clamp(((x - c) * AI_GS_CULTURE_RATIO_MULTIPLIER) / c, ±MULT) + the unclamped part  # MULT 150 → 75 in BNW
tourismTerm = same with AI_GS_TOURISM_RATIO_MULTIPLIER (75)
```
Constant changes [D]: `AI_GS_CULTURE_RATIO_MULTIPLIER` 150→**75**; new `AI_GS_TOURISM_RATIO_MULTIPLIER=75`, `AI_GS_CULTURE_AHEAD_WEIGHT=50`,
`AI_GS_CULTURE_TOURISM_AHEAD_WEIGHT=100`, `AI_GS_CULTURE_INFLUENTIAL_CIV_MOD=50`. `AI_GS_CULTURE_MAX_CITIES`, `_MODIFIER`, `_POLICY_BRANCH_MOD`, `_RATIO_EARLY_*` are **unused** in BNW.

### 4.3 United Nations / Diplomatic
**Base / G&K** — `base:100ba920`, `GK:100e13d0` [O]:
```text
if victory DIPLOMATIC disabled: return -100
s    = AI_GRAND_STRATEGY_UN_EACH_MINOR_ATTACKED_WEIGHT (-30) * minorsIHaveAttacked
votes = 1 (+1 more if I hold a specific capability) ; add 1 per player that votes for me (self, allies/protected CS)
votes += max(0, <FUN_10188c50>) / 750                                                   # 0x2ee = 750; [?] likely gold-based
needed = <FUN_10093ad0>
if votes - needed >= 0: return 1000
return (votes*100/needed - c3) + c1 + c2 + s                                            # c1..c3: three player-specific modifiers, [?]
```
**BNW** — `BNW:100f8cf0` (World Congress / League; 2.7 KB) [O]:
```text
if victory DIPLOMATIC disabled: return -100
s = -30 * minorsIHaveAttacked
if no League/World Congress exists yet:
        s += ((10 - X) * myFlavor[FLAVOR_DIPLOMACY] * 150) / 100                          # X unknown [?]
else:
        myVotes = leagueVotes(me); bestRival = max over other alive non-minor majors of leagueVotes(them)
        unmetOrUnknown = count of rivals with no information ("local_14")                # [I]
        lead = myVotes - bestRival
if myVotes >= votesNeeded: return 1000
if myVotes >= 3/4 * votesNeeded: s += 40
if lead > 0:                          s += clamp5(lead,            limit=40)
elif lead + 2*unknown > 0:            s += clamp5(lead + 2*unknown, limit=20)
elif lead + 2*unknown < 0:            s += clamp5(lead + 2*unknown, limit=-40)
# lead + 2*unknown == 0 → no adjustment
return s + c1 - c3 + c2                                                                  # same three modifiers as base [?]
```
**Caveat on `clamp5`:** the three magnitudes 40 / 20 / −40 are certain, and the term is `5 × (lead…)`. But the decompiled compare-and-select
reads literally like `max(5·x, 40)` (and a constant −40 in the negative branch), which is almost certainly a decompiler artifact of a
`min`/`max` clamp — the intended behavior is a *bounded* bonus of `5·x` limited to ±40/20. **[?]** until checked against a game log;
implement as `clamp(5·x, -limit, +limit)` and expose the limits as tunables.
Separate helper `UN_SECURED_VOTE_MOD (300)` (`BNW:100f9040/9064`, base `100baba0`): `300 * (#votes I've secured) / votesNeeded` [O].
**Takeaway:** BNW turns diplomatic victory into a *vote race* (compare own votes against the best rival; reward ≥75%), whereas base
counts "friendly voters" abstractly.

### 4.4 Spaceship
`base:100bab10`, `BNW:100f8f70`; progress helper `base:100bac90`, `BNW:100f9130` [O]:
```text
if victory SPACE_RACE disabled: return -100
s = 5 * <FUN_100f0cb0 = ?>                                  # [?] tech/progress-related count
if I have built PROJECT_APOLLO_PROGRAM: s += AI_GS_SS_HAS_APOLLO_PROGRAM (150)
return s
# rival-progress helper:
if apolloBuilt: return 150
return ((techs_mine_vs_? - ref) * AI_GS_SS_TECH_PROGRESS_MOD (300)) / max(ref, 1)       # [I] operand roles
```
Unchanged in structure across versions [O]; same constants.

### 4.5 Summary of version differences
| Strategy | Base | G&K | BNW |
|---|---|---|---|
| Conquest | flat base `10·x`, power ratio, at-war, cramped | = base | stat-based base term `(12−X)(Y+max(v0..v2)−v5)`, extra −50 tail |
| Culture | city count + policy branches, 1000 cap | + (GROWTH−EXPANSION flavor) term | tourism/culture comparison vs. every civ + influential-civ bonus |
| UN | "friendly voters" count | = base | World Congress vote race, 75% bonus, lead clamp |
| Spaceship | Apollo + progress | = base | = base |
| Selection loop | — | identical | identical |

---------------------------------------------------------------------------------------------------

## 5. Economic / Military / City strategies  *(inventory only — trigger logic not yet decompiled)*

All three are **data-defined triggers** [D] (`CIV5AIEconomicStrategies.xml`, `CIV5AIMilitaryStrategies.xml`, `CIV5AICityStrategies.xml`). Each strategy,
when its condition holds, applies flavor modifiers / build needs. Counts:

| File | Base | G&K | BNW | Notable BNW additions |
|---|---|---|---|---|
| Economic | ~31 | +4 | +10 | `NEED_ARCHAEOLOGISTS`, `ENOUGH_ARCHAEOLOGISTS`, `NEED_MUSEUMS`, `NEED_GUILDS`, `CONCERT_TOUR`, `STARTED_PIETY`, `DEVELOPING_RELIGION`, `TECH_LEADER`, `OFFSHORE_EXPANSION_MAP` |
| Military | 25 | +1 | +1 | `NEED_AIR_CARRIER` |
| City | — | +5 | +14 | trade-route need/destination, `NEED_TOURISM_BUILDING`, `NEED_CULTURE_BUILDING`, `GOOD_AIRLIFT_CITY`, `KEY_SCIENCE_CITY` |

Base economic examples: `EARLY_EXPANSION`, `NEED_HAPPINESS(_CRITICAL)`, `LOSING_MONEY`, `TOO_MANY_UNITS`, `ISLAND_START`, `EXPAND_LIKE_CRAZY`, `GS_CULTURE/CONQUEST/DIPLOMACY/SPACESHIP(_HOMESTRETCH)`.
Base military examples: `AT_WAR`, `WAR_MOBILIZATION`, `WINNING_WARS`, `LOSING_WARS`, `EMPIRE_DEFENSE(_CRITICAL)`, `NEED_RANGED/MOBILE/NAVAL/AIR/ANTIAIR`, `ERADICATE_BARBARIANS`.
Related tunables: `AI_STRATEGY_EARLY_EXPANSION_NUM_CITIES_LIMIT=8`, `…_NUM_UNOWNED_TILES_REQUIRED=25`, `AI_STRATEGY_EARLY_EXPLORATION_STARTING_WEIGHT=100`, `…_WEIGHT_PER_FLAVOR=10`.

Port plan: implement the *engine* (strategy table + condition evaluator + flavor modifier application) first, then fill conditions per strategy as they are decompiled.

---------------------------------------------------------------------------------------------------

## 6. Tactical AI — unit movement in combat  [O unless noted]

Class located at roughly `BNW:101c0000–101e6000` (510 functions). Constructed by `BNW:101c2db0`.

### 6.1 Per-turn pipeline
Entry `BNW:101e1db0` = **plan** then **execute**:
```text
function tacticalDoTurn():
    plan()      # BNW:101dae50  — find targets, build tactical-map zones, choose postures, build ordered move list
    execute()   # BNW:101e1ac0  — walk the ordered move list
```
**Plan** (`BNW:101dae50`; summary, not every step read):
1. Clear the target list; for every map plot visible to me, classify it into a **target** with a type and a numeric priority [O; target
   *type enum names* **[?]**].  Observed target kinds: city, barbarian camp (special-cased via `BARBARIAN_CAMP_IMPROVEMENT`), pillageable improvement,
   citadel, strategic-resource improvement, trade unit, enemy units split into high/medium/low priority, enemy civilians in four priority levels, ancient ruins.
   (Where the sorted `(moveType, priority)` list at `this+0x44` is *built* was not located; ordering formula **[?]**, see §6.4.)

**Execute** (`BNW:101e1ac0`). Barbarian players take a separate path (`BNW:101d6800`, priorities `AI_TACTICAL_BARBARIAN_PRIORITY_*`).
Prelude: `101d6750()` then `101ca590()`, the latter **building the tactical zones and assigning each a posture** (§6.3; it also writes the
"Zone ID: n, <posture>" debug line). Then:
```text
for (moveType, priority) in sortedMoves:                       # presumably highest priority first [I]
    meta = lookup(moveType)
    if meta is missing or not meta.isZoneMove:                 # flag at meta+0x115 [O]; [I] = XML column DominanceZoneMove
        handler(moveType)                                      # §6.2 table
    else:
        for zone in zones:                                     # zones sorted/indexed by the prelude
            applies = false
            posture = zone.posture
            if   meta.name == CLOSE_ON_TARGET:                                       applies = true
            elif meta.name == POSTURE_WITHDRAW           and posture == WITHDRAW:           applies = true
            elif meta.name == POSTURE_HEDGEHOG           and posture == HEDGEHOG:           applies = true
            elif meta.name == POSTURE_SHORE_BOMBARDMENT  and posture == SHORE_BOMBARDMENT:  applies = true
            elif meta.name in {EMERGENCY_PURCHASES, DEFENSIVE_AIRLIFT}
                                                         and zone.field8 != 1 and zone.type == 2:   applies = true
            elif meta.name == OFFENSIVE_POSTURE_MOVES:                     # meta-move: expand to the zone's concrete posture
                 moveType = { 1: POSTURE_SIT_AND_BOMBARD, 2: POSTURE_ATTRIT_FROM_RANGE, 3: POSTURE_EXPLOIT_FLANKS,
                              4: POSTURE_STEAMROLL, 5: POSTURE_SURGICAL_CITY_STRIKE, 7: POSTURE_COUNTERATTACK }.get(posture)
                 applies = (moveType is not None)   # postures 0, 6, 8 are handled by their own dedicated entries above
            if applies and zoneIsActive(zone)                       # 101c6100 [?]
                       and ( someFlag(this+0x247c) or zone.type == 5 or (zone.type == 3 and not zone.flag44) ):
                 handler(moveType)                                  # run with this zone selected (this+0x60d8 = zone index)
```
The exact meaning of zone `type` values (2, 3, 5) and the activity gates are **[?]**. The structure — *iterate the move list, expand zone-scoped moves per zone
by posture, run each handler with the zone as context* — is what a port needs.

### 6.2 Move → handler table (BNW; generated from the dispatcher `101e16f0`)
Handler arguments are literal in the dispatcher; the enum meanings in parentheses are **[I]**, confirmed for the unit-priority ones by the engine's own log strings
("Looking at damaging low/medium/high priority…").

| Tactical move | Handler(args) | Meaning of args |
|---|---|---|
| `NONCOMBATANTS_TO_SAFETY` | `101d4cf0(0)` | combat=false |
| `TO_SAFETY` | `101d4cf0(1)` | combat=true |
| `CAPTURE_CITY` | `101d69d0()` | |
| `DAMAGE_CITY` | `101d6d90()` | |
| `DESTROY_HIGH/MEDIUM/LOW_UNIT` | `101d7320(7/6/5, kill=1, 0)` | target priority; **kill** |
| `ATTRIT_HIGH/MEDIUM/LOW_UNIT` | `101d7320(7/6/5, kill=0, 0)` | damage without needing a kill |
| `REPOSITION` | `101d3280()` | |
| `BARBARIAN_CAMP` | `101d7170()` | |
| `PILLAGE` / `_CITADEL` / `_RESOURCE` | `101d7aa0(3 / 0x17 / 0x18, now=1)` | improvement class |
| `PILLAGE*_NEXT_TURN` | `101d7aa0(…, now=0)` | stage for next turn |
| `PLUNDER_TRADE_UNIT` | `101d7df0(2); 101d7df0(0)` | land, then sea (domain ids) |
| `PARK_ON_TRADE_ROUTE` | `101ccfc0(2); 101ccfc0(0)` | |
| `ATTACK_{VERY_HIGH,HIGH,MEDIUM,LOW}_PRIORITY_CIVILIAN` | `101d7fc0(0xf/0x10/0x11/0x12)` | |
| `SAFE_BOMBARDS` | `101db790()` | |
| `HEAL` | `101d35f0()` | |
| `ANCIENT_RUINS` | `101d54b0(1)` | |
| `GARRISON_TO_ALLOW_BOMBARD / ALREADY_THERE / 1_TURN` | `101d4eb0(1,1) / (0,0) / (1,0)` | |
| `BASTION_ALREADY_THERE / 1_TURN` | `101d5110(0 / 1)` | |
| `GUARD_IMPROVEMENT_ALREADY_THERE / 1_TURN` | `101d52e0(0 / 1)` | |
| `AIR_INTERCEPT` / `AIR_SWEEP` | `101d37f0()` / `101d3a10()` | |
| `POSTURE_SIT_AND_BOMBARD` | `101e0370()` | |
| `POSTURE_ATTRIT_FROM_RANGE` | `101e06e0()` | |
| `POSTURE_EXPLOIT_FLANKS` | `101dbbf0()` | |
| `POSTURE_STEAMROLL` | `101e0870()` | |
| `POSTURE_SURGICAL_CITY_STRIKE` | `101e0950()` | |
| `POSTURE_HEDGEHOG` | `101e0ab0()` | |
| `POSTURE_COUNTERATTACK` | `101e0bb0()` | |
| `POSTURE_WITHDRAW` | `101d8380()` | |
| `POSTURE_SHORE_BOMBARDMENT` | `101dbd90()` | |
| `CLOSE_ON_TARGET` | `101de1e0(1)` | |
| `MOVE_OPERATIONS` | `101e1630()` | |
| `EMERGENCY_PURCHASES` | `101c4b80()` | |
| `DEFENSIVE_AIRLIFT` | `101d2170()` | |
| `ESCORT_EMBARKED_UNIT` | `101d3c20()` | |

BNW-only moves vs. base [D]: `DEFENSIVE_AIRLIFT, PARK_ON_TRADE_ROUTE, PILLAGE_CITADEL(+NEXT_TURN), PILLAGE_RESOURCE(+NEXT_TURN), PLUNDER_TRADE_UNIT, PILLAGE_NEXT_TURN`
(G&K adds `ESCORT_EMBARKED_UNIT, OFFENSIVE_POSTURE_MOVES`).

### 6.3 Postures (zone stance) [O]
Enum recovered from the engine's zone-debug strings (`BNW:101ca590`):

| Id | Posture |
|---|---|
| 0 | Withdraw |
| 1 | Sit and Bombard |
| 2 | Attrit from Range |
| 3 | Exploit Flanks |
| 4 | Steamroll |
| 5 | Surgical City Strike |
| 6 | Hedgehog |
| 7 | Counterattack |
| 8 | Shore Bombardment |

How a posture is *chosen* for a zone (dominance comparison using `AI_TACTICAL_MAP_UNIT_STRENGTH_MULTIPLIER=10`,
`AI_TACTICAL_MAP_DOMINANCE_PERCENTAGE=25`) is **[?]** — function located (`BNW:101e4c40`, `101e2740`), not read.

### 6.4 Move priority data [D]
`data/tactical_moves.tsv`. Columns: `Priority`, `OffenseFlavorWeight`, `DefenseFlavorWeight`, `OperationsCanRecruit`, `DominanceZoneMove`.
Highest priorities (BNW): `EMERGENCY_PURCHASES 200`, `CAPTURE_CITY 150`, `DESTROY_HIGH_UNIT 140`, `AIR_SWEEP 135`, `AIR_INTERCEPT 130`,
`ATTACK_VERY_HIGH_PRIORITY_CIVILIAN 130`, `PILLAGE_CITADEL 125`, `DESTROY_MEDIUM_UNIT 120`, `DESTROY_LOW_UNIT 110`, `OFFENSIVE_POSTURE_MOVES 105`.

**Effective priority formula = [?].** What is known:
- Inputs available: base `Priority`, `OffenseFlavorWeight`/`DefenseFlavorWeight` (0 or 100), leader flavors.
- Tunables: `AI_TACTICAL_FLAVOR_DAMPENING_FOR_MOVE_PRIORITIZATION = 0.3`, `AI_TACTICAL_MOVE_PRIORITY_RANDOMNESS = 6`.
- [I] plausible form: `priority = Priority + 0.3 * (OffenseWeight*offFlavor + DefenseWeight*defFlavor)/100 + rand(6)`. **Unverified. Do not hard-code
  without reading the sorter.**

### 6.5 Core attack-assignment algorithm ("overkill") — `BNW:101d2420`  [O]
Used by the unit-kill handlers to choose *how many* units to throw at one target:
```text
function assignAttackers(target, rangedFirst=true):
    budget = ((AI_TACTICAL_OVERKILL_PERCENT + 100) * target.hp) / 100       # overkill = 25  → 125% of target HP
    # pass 1: ranged / bombardment-capable attackers (12-byte entries: unit, expectedDamage, …)
    for a in rangedAttackers:
        if budget <= 0: break
        if a is usable:  commit(a); remember lastAttacker = a (if it can still act)
        budget -= a.expectedDamage
    # pass 2: melee attackers (24-byte entries: unit, expectedDamage, expectedSelfDamage-ish fields, …)
    for m in meleeAttackers:
        if budget <= 0: break
        if caller flag (param_4) set and entry.field5 > entry.field4: log + skip     # a per-entry gate [?]
        elif unit is still available and passes "can attack" checks (several predicates [?]):
             commit(m); budget -= m.expectedDamage
        # engine writes verbose log lines for each skip reason
    if resultKind == 1 and budget < 1: budget = 1                              # ensure at least minimal pressure [I]
    # post-step [I]: gather up to 6 neighbor plots (one per hex direction) that are free of units/prior targeting,
    #                used as staging/attack tiles for the follow-up loop
```
```
Inputs/outputs are sets of `(unit, expected damage, expected damage taken)` produced by a per-target "who can reach me" scan bounded by
`AI_TACTICAL_RECRUIT_RANGE` (BNW **8**, base/G&K **10** [D]). Exact per-unit damage estimators are **[?]**.

### 6.6 Tactical tunables [D] (BNW)
| Define | Value (base) |
|---|---|
| `AI_TACTICAL_RECRUIT_RANGE` | 8 (10) |
| `AI_TACTICAL_REPOSITION_RANGE` | 10 |
| `AI_TACTICAL_OVERKILL_PERCENT` | 25 |
| `AI_TACTICAL_MOVE_PRIORITY_RANDOMNESS` | 6 |
| `AI_TACTICAL_FLAVOR_DAMPENING_FOR_MOVE_PRIORITIZATION` | 0.3 |
| `AI_TACTICAL_MAP_UNIT_STRENGTH_MULTIPLIER` | 10 |
| `AI_TACTICAL_MAP_DOMINANCE_PERCENTAGE` | 25 |
| `AI_TACTICAL_MAP_TEMP_ZONE_RADIUS` | 5 |
| `AI_TACTICAL_MAP_TEMP_ZONE_TURNS` | 4 (5) |
| `AI_TACTICAL_MAP_BOMBARDMENT_ZONE_TURNS` | 1 |
| `AI_TACTICAL_BARBARIAN_RELEASE_VARIATION` | 15 |
| `AI_MILITARY_THREAT_WEIGHT_{MINOR,MAJOR,SEVERE,CRITICAL}` | 1 / 3 / 6 / 10 |
| `AI_MILITARY_CITY_THREAT_WEIGHT_CAPITAL` | 125 |
| `AI_MILITARY_BARBARIANS_FOR_MINOR_THREAT` | 2 |
| `AI_MILITARY_RECAPTURING_OWN_CITY` / `…CAPTURING_ORIGINAL_CAPITAL` | 150 / 250 (BNW only) |

### 6.7 Barbarian priorities [D]
`AI_TACTICAL_BARBARIAN_PRIORITY_*` (BNW): `ESCORT_CIVILIAN 30, PLUNDER_TRADE_UNIT 20, DESTROY_HIGH 16, DESTROY_MEDIUM 15, DESTROY_LOW 14, PILLAGE_CITADEL 14,
CAMP_DEFENSE 13, CIVILIAN_ATTACK 13, PILLAGE 12, ATTRIT_HIGH 10, TO_SAFETY 10, BLOCKADE_RESOURCE 10, CAPTURE_CITY 9, ATTRIT_MEDIUM 7, ATTRIT_LOW 6,
DAMAGE_CITY 5, PILLAGE_NEXT_TURN 4, AGGRESSIVE_MOVE 3, DESPERATE_ATTACK 0, PASSIVE_MOVE -1`. Note the inversion vs. majors: barbarians rate
*escort/plunder/kills* above city capture.

---------------------------------------------------------------------------------------------------

## 7. Homeland AI — non-combat & idle units  *(priorities only)*

Tunable priority table (`AI_HOMELAND_MOVE_PRIORITY_*`) [D], BNW. Higher runs first [I]; `RANDOMNESS=10`.
`ADD_SPACESHIP_PART 200 · ARCHAEOLOGIST 100 · TRADE_UNIT 100 · TREASURE 100 · ADMIRAL/AIRCRAFT/ARTIST/ENGINEER/GENERAL_GARRISON/MUSICIAN/PROPHET/SCIENTIST/SETTLE/WRITER 50 ·
ANCIENT_RUINS/GARRISON_CITY_STATE/INQUISITOR/MISSIONARY 40 · EXPLORE/EXPLORE_SEA 35 · HEAL/TO_SAFETY/WORKER/WORKER_SEA 30 · UPGRADE 25 · AIRLIFT 24 · SENTRY 20 ·
SPACESHIP_PART 20 · MOBILE_RESERVE 15 · GARRISON 10 · PATROL 0`.
Other: `HOMELAND_MAX_DEFENSIVE_MOVE_TURNS=4`, `MAX_UPGRADE_MOVE_TURNS=5`, `GREAT_PERSON_TURNS_TO_WAIT=10`, `ESTIMATE_TURNS_DISTANCE=8`.
Dispatcher and per-move logic: `BNW:10108490` (priority setup, 8 KB), `BNW:100f9d80`. **[?]** — not yet read.

**Insight:** the *same* "priority table + per-move handler" pattern as §6 governs non-combat units, so one generic `MoveQueue(moveType, priority, handler)`
abstraction can serve both layers.

---------------------------------------------------------------------------------------------------

## 8. Operations & armies *(inventory only)*

Multi-unit planned efforts (`CvAIOperation*`, `CvArmyAI`): `BasicCityAttack, SneakCityAttack, QuickSneakCityAttack, SmallCityAttack, CityStateAttack,
DestroyBarbarianCamp, PillageEnemy, FoundCity, QuickColonize, NotSoQuickColonize, MerchantDelegation, NavalBombardment, NavalSuperiority, CityCloseDefense,
RapidResponse, NavalAttack, NavalSneakAttack, NavalCityStateAttack, NukeAttack` [O: RTTI].
Tunables [D]: `OPERATIONAL_MAX_RECRUIT_TURNS_DEFAULT=5`, `…_ENEMY_TERRITORY=10`, `CITY_ATTACK_DEPLOY_RANGE=5`, `BARBARIAN_CAMP_DEPLOY_RANGE=2`,
`PILLAGE_ENEMY_DEPLOY_RANGE=5`, `NAVAL_BOMBARDMENT_DEPLOY_RANGE=1`, `PERCENT_HEALTH_FOR_OPERATION=80`, `PERCENT_DANGER_FOR_FORWARD_MUSTER=20`.
The tactical `MOVE_OPERATIONS` handler (`BNW:101e1630`) is what advances armies each turn; `OperationsCanRecruit` in the move table says which tactical moves may
*draw units out of* an operation (e.g. `DESTROY_LOW_UNIT: true`, `CAPTURE_CITY: false`) [D].

---------------------------------------------------------------------------------------------------

## 9. Gap list (ordered by value for a port)

1. **Tactical move priority formula** and the sorter (§6.4).
2. **Target classification** (target type enum + per-type priority numbers) in `BNW:101dae50`.
3. **Zone building + posture selection** (§6.3: `101e4c40`, `101e2740`, `101ca590`).
4. **Per-posture handlers** (`101e0370…101e0bb0`) and `CLOSE_ON_TARGET` (`101de1e0`) — how units are actually moved onto tiles (pathing, flanking, ZOC use).
5. **Damage/risk estimators** feeding §6.5 (expected damage, danger maps — `AI_*DANGER*` defines exist, 11 of them).
6. **Unknown helper calls** in §4 scorers (`X`, `Y`, `v0…v5`, the three UN modifiers, the BNW conquest tail).
7. **Rival-strategy guessing** (`BNW:100f9260`).
8. **Diplomacy AI** (`CvDllDiplomacyAI`, ~680 BNW defines in `data/diplomacy_ai_defines.tsv`): approach scoring, war evaluation, deals.
9. **Homeland handlers** (§7) and **city production AI** (`CvCityStrategyAI`, `CvWonderProductionAI`).
10. **Economic/Military/City strategy trigger conditions** (§5).

Suggested validation (cheap): run the real game with AI logging enabled and compare your port's grand-strategy choices over 100 turns on the same seed
(the engine emits `Grand Strategy AI:` and `Tactical AI` log lines — the strings are present in the DLLs). Verify the config key names in `config.ini`
before relying on this.

---------------------------------------------------------------------------------------------------

## 10. Porting recommendations for Unciv

1. **Add a flavor + grand-strategy layer** (§2–§4): small, well-specified, high leverage; `Personality` already exists. Use the BNW scorers; keep the 10-turn lock and ±noise —
   they are what makes the AI look non-robotic. Replace `preferredVictoryType` (static string) with the scored result; keep it as one input.
2. **Introduce a `TacticalMove` queue** alongside `UnitAutomation`: a sorted list of `(moveType, priority)` with handlers (§6.2). Run it *before* the per-unit cascade so that
   target-centric moves (city capture, kill high-value unit) claim units first; remaining units fall back to existing `try*` behaviors.
3. **Implement overkill assignment (§6.5)** — it alone fixes the common "AI sends one unit at a time" weakness.
4. Treat §6.4/§6.3 as **configurable data** (JSON like Unciv rulesets) so unknown formulas can be tuned empirically until the gaps in §9 are closed.
5. Make the data tables in `data/` your ruleset JSON seed; they are the actual shipped values.

---------------------------------------------------------------------------------------------------

## Appendix A — files in this folder

| Path | Content |
|---|---|
| `data/tactical_moves.tsv` | Move table with priority/flavor weights/flags, flagged by base/G&K/BNW presence [D] |
| `data/ai_defines.tsv` | All `GlobalAIDefines.xml` tunables, base vs G&K vs BNW [D] |
| `data/diplomacy_ai_defines.tsv` | `GlobalDiplomacyAIDefines.xml`, base vs BNW [D] |
| `data/grand_strategy_flavors.tsv` | Strategy flavor / flavor-mod / yield tables [D] |
| `tools/` | Ghidra export script, define-table builder, labeler (to reproduce) |

## Appendix B — evidence index (BNW addresses)

| Topic | Address |
|---|---|
| GS turn loop | `100f9720` |
| flavor dot-product | `100f7880` |
| conquest scorer / rival estimate | `100f8760` / `100f7960` |
| culture scorer / rival ratio | `100f8b40` / `100f7a10` |
| UN scorer / vote helper | `100f8cf0` / `100f9040,100f9064` |
| spaceship scorer / progress | `100f8f70` / `100f9130` |
| rival-strategy guess | `100f9260` |
| tactical ctor (move-id slots) | `101c2db0` |
| tactical DoTurn / plan / execute | `101e1db0` / `101dae50` / `101e1ac0` |
| move dispatcher | `101e16f0` |
| attack assignment (overkill) | `101d2420` |
| unit-kill handler | `101d7320` |
| zone/posture builder | `101ca590` |
| tactical defines loader | in the global defines init (see `tools/`) |
| homeland priorities | `10108490` |
