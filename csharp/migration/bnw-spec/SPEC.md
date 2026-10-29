# Brave New World for the C# engine: Implementation Spec (v0.1)

Audience: implementers and reviewers on the `csharp-migration` track. Goal: a new base ruleset
**"Civ V - Brave New World"** that plays like Civ V with the BNW expansion (all DLC civs included, as in Unciv's
G&K ruleset), deterministic and lockstep-safe like the rest of the engine.

Scope: every BNW gameplay system. That covers trade routes, Great Works, tourism and culture victory, archaeology,
ideologies and public opinion, the World Congress and United Nations, the reworked policy tree, happiness and
uprising changes, espionage diplomats, city-state, religion and warmonger changes, 9 civs, new units, buildings,
wonders, beliefs and resources, map placement of new content, and AI hooks. Not in scope: the two BNW scenarios
(Civil War, Scramble for Africa), Firaxis art, audio and prose, tablet UI, and Steam features.

---------------------------------------------------------------------------------------------------

## 0. How to read this document

### Evidence tags

| Tag | Meaning |
|-----|---------|
| **[O]** | Observed in the decompiled BNW engine (`CvGameCore_Expansion2.dll`). Address given as `@1006ea20`. Control flow and constants are as stated. Function names are descriptive labels, taken from the Lua binding table where one exists (`data/derived/lua_bindings_bnw.tsv`). |
| **[D]** | Shipped data: the merged BNW database, exported to `data/<Table>.tsv`. |
| **[U]** | Shipped UI Lua (`Assets/DLC/Expansion2/UI/**.lua`, plain text). |
| **[K]** | Known BNW rule, from long-standing public documentation of the expansion, consistent with the data but **not** re-verified in this binary. Verify before porting (see §0.4). |
| **[I]** | Inferred from code or data. A name or meaning was deduced. |
| **[?]** | Unknown. Code exists but was not decoded. Do not guess. The ticket that needs it does the RE (method in §0.4). |
| **[X]** | Design decision for this port. Not a Civ V fact. |

### Provenance

| Item | Value |
|------|-------|
| BNW engine | `Assets/DLC/Expansion2/CvGameCore_Expansion2.dll`, SHA-256 `c5483b58…4e155a5`, 19,551 functions |
| Decompiler | Ghidra 12.1.4 headless (same run as `../ai-spec`) |
| Data | `cache/Civ5DebugDatabase.db`, the game's own merged base+G&K+BNW+DLC database (written Jul 9 2026), and `Localization-Merged.db` for English names |
| UI | `Assets/DLC/Expansion2/UI/InGame/**` |
| Constants | Values come from `data/Defines.tsv` (the game reads them at startup). The decompile shows *which* define a formula reads (`D.NAME`). Define-name labels were checked against several independent formulas. One mislabel is known: §4.3.2. |

### Clean-room note
As in `../ai-spec`: no decompiler output is committed. This document holds analyst-written behaviour and
pseudocode, plus numeric game data. The repo rule "never copy Firaxis art, icons, sounds, fonts or music" still
applies. The ruleset may carry names and numbers, but not Civilopedia prose or quotes.

### 0.4 Verifying [K] and [?] items
Two routes, in order of preference:
1. **Firaxis' published BNW game-core source.** The free *Sid Meier's Civilization V SDK* (Steam tools, app 16830)
   ships the C++ source of this DLL (`CvGameCoreDLL_Expansion2`). Its file names match the subsystems below
   (`CvTradeClasses.cpp`, `CvCultureClasses.cpp`, `CvVotingClasses.cpp`, `CvPolicyClasses.cpp`, `CvPlayer.cpp`,
   `CvGame.cpp`). Behaviour may be ported from it under the same clean-room note. A ticket that resolves a [K] or [?]
   item updates this spec and changes the tag to [O].
2. The decompile, using `tools/fn.py` (anchor on a define or text key, then follow callers and callees).

---------------------------------------------------------------------------------------------------

## 1. What BNW changes, at a glance

| Area | BNW change | Spec |
|------|-----------|------|
| Trade | Caravan / Cargo Ship units create **trade routes**: international (gold, science, religious pressure, tourism bonus) or domestic (food or production). Slots come from techs and buildings; range comes from techs and buildings. Plundering. | §4.1 |
| Culture | **Great Works** (Art, Artifact, Writing, Music) in building **slots**, with **theming bonuses**. Each work gives culture and tourism. | §4.2 |
| Tourism | Per-city **tourism** accumulates **influence** on each civ. Six influence levels. **Culture victory** = Influential on every living civ. | §4.3 |
| Archaeology | Historical events are recorded on tiles. Researching Archaeology spawns **antiquity sites**. **Archaeologists** dig them for a Landmark or an Artifact. | §4.4 |
| Great People | Great **Writer** and **Musician** are added. Artist, Writer and Musician create Great Works, or use Political Treatise, Golden Age or Concert Tour. Guild buildings. GP faith purchase. | §4.5 |
| Policies | Tree is now Tradition, Liberty, Honor, Piety, Patronage, **Aesthetics**, Commerce, **Exploration**, Rationalism. Freedom, Order and Autocracy become **ideologies** with 3 tenet levels. Wonders require branches. | §4.6 |
| Ideology | Unlocked by the Modern era or 3 Factories. First adopters get free tenets. **Public opinion** turns other civs' cultural pressure into unhappiness, revolts and ideology switching. | §4.6 |
| Diplomacy | **World Congress**, later the **United Nations**. Delegates, proposals, 18 resolutions, 3 international projects. **Diplomatic victory** by vote. | §4.7 |
| Happiness | Graduated penalties below 0. **Rebels** spawn at −10 or worse. **City revolts** at −20 or worse. | §4.8 |
| Espionage | **Diplomats**, election rigging changes, influence-based spy rank bonuses. | §4.9 |
| City-states | New quests (trade route), influence anchors, friendship on death, unit gift travel, purchase by Merchant of Venice. | §4.10 |
| Religion | Pressure scaled ×10. New beliefs (Reformation tier, faith-buy any GP, tourism beliefs). Trade routes carry pressure. | §4.11 |
| Military | Warmonger rework, Sapper, Heavy Charge, post-combat random promotions, very-unhappy combat penalty, AI free XP. | §4.12 |
| Content | 9 civs (Assyria, Brazil, Indonesia, Morocco, Poland, Portugal, Shoshone, Venice, Zulu) and their uniques. New units, buildings, wonders, beliefs, resources and improvements. Small tech-tree change. | §3, §4.13 |
| Map | Antiquity sites, new luxuries (Bison, Cocoa, plus Indonesia's Cloves, Pepper, Nutmeg), "start along ocean first". | §4.14 |
| Victory/score | Culture and diplomatic victories rewritten. Score counts policies, Great Works, beliefs and religion cities. | §4.15 |

---------------------------------------------------------------------------------------------------

## 2. Engine architecture for the port [X]

### 2.1 Ruleset, not hard-coding
- New `BaseRuleset.Civ_V_BNW` → full name `"Civ V - Brave New World"` (`Models/Metadata/BaseRuleset.cs`). The JSON goes
  in `android/assets/jsons/Civ V - Brave New World/`, where `RulesetFiles` already looks (`jsons/<fullName>`). This is a
  data-only addition; Kotlin sources are untouched.
- The JSON is **generated** by a tool (`bnw-spec/tools/gen_ruleset.py`, ticket B002) from `data/*.tsv`, starting from
  the G&K JSON so Unciv-specific fields are kept (uniques Unciv already models, unit types, images, name groups). A
  checked-in, hand-maintained `overrides.json` covers what cannot be generated. Regenerating must be idempotent.
- **Every BNW subsystem is switched on by the ruleset, never by the ruleset's name.** G&K and Vanilla games must
  behave exactly as today, and existing goldens must not move. Gate with ModOptions uniques (new `UniqueTarget.ModOptions`
  entries):
  `Enables international trade routes`, `Enables Great Works and tourism`, `Enables archaeology`,
  `Enables ideologies`, `Enables the World Congress`, `BNW happiness rules`.
- BNW tunables (the `Defines` rows used in §4) become `ModConstants` fields. Defaults equal the G&K behaviour or
  "off", and the BNW `ModOptions.json` sets the BNW values. Name them after the define (for example
  `TourismModifierOpenBorders`). Do not read `Defines.tsv` at runtime.
- **As built (B001).** `BaseRuleset.Civ_V_BNW` exists and `RulesetCache` skips it while its JSON folder is absent (B002 generates
  it); the six gates are `UniqueType`s 638-643 (target `ModOptions`, `NoConditionals`) in `UniqueTypeDataBnw.cs`, appended
  after the Kotlin rows. `ModConstants` has 76 tunables named after the defines (the §4 defines plus their siblings, e.g. both
  ends of a range), defaulting to the G&K value where G&K has one (religion pressure, gift friendship, settler gold,
  research agreement, hill influence, tech prereqs) and to 0 otherwise. `BASE_CITY_AIR_STACKING` is the existing
  `cityAirUnitCapacity`, and the `SCORE_*` rows that G&K already has stay in `scoreFrom*`. Nothing reads the new fields yet.
  Deliberate deviation: the JVM-golden `ModConstants` hash codes and the `UniqueType` count test were rebased on the extended
  classes. No decompiler output was available, so no [K]/[?] item was resolved by this ticket.

- **As built (B002).** `tools/gen_ruleset.py` (+ `tools/overrides.json`, `tools/test_gen_ruleset.py`) writes `android/assets/jsons/Civ V - Brave New World/`
  and `data/derived/gen_ruleset_report.txt` (removed, added and changed objects; the reviewer's log). `RulesetCache` no longer skips BNW and the
  seven tests loop over all three base rulesets. The ruleset validates with zero messages (`BnwRulesetTests`). Generated: the tech tree (81 techs, BNW
  rows, prerequisites and per-tech costs, The Internet), buildings and units (BNW numbers over the G&K objects, new BNW rows as new objects, `TheXxx`
  wonder names kept), the ModOptions gates and the 75 tunables of `ModConstants.cs` (value taken from the comment there, cross-checked against
  `Defines.tsv`; `BASE_CITY_AIR_STACKING` stays `cityAirUnitCapacity`). Hand-written in `overrides.json`: nine major civs (Venice becomes major) with
  their unique units and buildings, 16 city-states, Writer and Musician specialists and great-person names, Bison/Cocoa/Cloves/Pepper/Nutmeg.
  Deliberate deviations: (1) matched buildings keep Unciv's flat `culture` (it stands in for Great Work slots until B030); (2) the resource keeps the
  name `Gold Ore` (a resource named `Gold` collides with the stat in unique parameters); (3) the Cultural and Diplomatic victory types are removed and
  nations preferring them get `Neutral` until B032/B063 add milestone kinds (Utopia Project and United Nations are dropped, SPEC 3.3);
  (4) the new Archaeologist and International Space Station are `Unbuildable` until B040/B062 (Caravan and Cargo Ship became buildable in B006); (5) Machine Gun has no
  obsolete tech (BNW's Nuclear Fission contradicts its upgrade prerequisites); (6) civ abilities, tourism/trade/league columns, the 16 BNW beliefs,
  Hand-Axe/Bazooka/XCOM Squad (scenario-only) and the BNW policy tree are not emitted: they need the B003 `UniqueType`s and are listed under
  `pending` in the report/overrides for B010, B050 and the feature tickets; (7) Maria I's personality is a copy of Maria Theresa's; (8) the Antiquity Site resources wait for B040; (9) barbarian unit rows are skipped (same names as the normal units), `upgradesTo` comes from `Unit_ClassUpgrades.tsv`, Settler/Worker/Scout/Warrior keep the Unciv requiredTech Agriculture, and `minorLiberationFriendship` relies on its 105 default being the BNW value. No decompiler
  output was available, so no [K]/[?] item was resolved by this ticket.

### 2.2 New state (all JSON-serialized, saved and loaded, included in `Checksum`)

| Owner | New type / field | Holds |
|-------|------------------|-------|
| `GameInfo` | `TradeRoutes : List<TradeRoute>` | Every active route (global, as in Civ V `CvGameTrade`), in creation order. Ids are monotonic. |
| `GameInfo` | `League : League?` | World Congress state (§4.7). `null` until founded. |
| `GameInfo` | `GreatWorks : List<GreatWorkInstance>` | Every created work: index, type name, creator civ, era, turn. Used names are derived from the instances (each named work exists once per game); B007 stores instances in `GreatWorkInstances` and swap offers in `GreatWorkSwapOffers`, both null until used. |
| `Tile` / `GameInfo` | `Tile.ArchaeologyRecord`, `GameInfo.ArchaeologySitesSpawned`, `GameInfo.PendingDigs` | B010: the record of a tile (JSON `archaeology`), whether sites were spawned, and the digs a human civ still has to resolve. Null/false when unused (omitted from saves). |
| `GameInfo` | `Archaeology` | Spawn done flag, list of placed site records (§4.4). |
| `Tile` | `ArchaeologyRecord?` | `{ArtifactClass, Era, Player1, Player2, GreatWork?}`. Written by historical events, read by digs. |
| `Civilization` | `Trade : CivTradeManager` (transient + persisted) | Trade units, recent routes, plundered-route memory (30 turns, @101f7e40). |
| `Civilization` | `Culture : CivCultureManager` | `InfluenceOn[civ]`, `LastTurnInfluenceOn[civ]`, `LastTurnLifetimeCulture[civ]`, public opinion, preferred ideology, opinion unhappiness, tourism-bonus turns, swap offers. |
| `Civilization.Policies` | `Ideology`, `TenetCounts`, `FreeTenets`, `AnarchyTurns` | §4.6 |
| `City` | `GreatWorkSlots : Map<building, string?[]>` | The work in each slot of each building (ordered by slot index). |
| `City` | `Culture` transient | Cached city tourism and theming. Rebuilt on load. |

### 2.3 Turn order [X] (aligned with Civ V where observed)
At the start of a civ's turn, after the existing G&K steps:
1. Trade routes owned by the civ advance (§4.1.6). Completed ones end and return their unit.
2. Influence accumulates (§4.3.3). Public opinion is recomputed (§4.6.4).
3. Happiness consequences run: uprising counter, then revolt counter (§4.8).

Once per game turn, after all civs (in civ index order):
4. League turn (§4.7): found, start special session, count down session timers, resolve votes.
5. Culture victory check, then diplomatic victory resolution (§4.15).

All per-civ loops iterate civs in `GameInfo.Civilizations` order. That is the "player ID" order, and Civ V loops
players by ID everywhere it was observed (`@1006e5d0`, `@10264850`).

### 2.4 Commands, queries, events (see `COMMANDS.md`)
New commands (all pure `Validate`, deterministic `Apply`):
`EstablishTradeRoute{unitId, targetCityId, kind}`, `ChangeTradeUnitHome{unitId, cityId}`,
`CreateGreatWork{unitId}` (when the unit stands in a city), `PoliticalTreatise`/`ConcertTour`/(Artist golden age reuses
the existing GP action), `MoveGreatWork{fromCity, fromBuilding, fromSlot, toCity, toBuilding, toSlot}`,
`OfferGreatWorkSwap{slotRef}` / `TakeGreatWorkSwap{civ, slotRef, mySlotRef}`, `ChooseArchaeologyResult{unitId, choice}`,
`ChooseIdeology{branch}`, `AdoptTenet{policy}`, `SwitchIdeology{}`, `ProposeResolution{resolution, choice?}` /
`ProposeRepeal{activeId}`, `CommitVotes{proposalId, choice, count}` (repeatable until delegates run out),
`SetLeagueProjectProduction` (a city production *process*, reusing the existing construction command), and
`DesignateDiplomat{spyId}` (existing `MoveSpy` plus a flag).

Queries (for the Godot client, mirroring the BNW Lua surface in `lua_bindings_bnw.tsv`): `TradeRouteOptionsQuery`
(destinations with yields per side), `TradeRoutesQuery` (yours / to you / available), `CultureOverviewQuery` (works,
slots, theming, influence per civ, levels, trend, tourism breakdown), `LeagueQuery` (members, delegates breakdown,
proposals, active resolutions, project tiers, turns until session), `IdeologyQuery`, `ArchaeologyChoiceQuery`.

Events: `TradeRouteEstablished/Completed/Plundered`, `GreatWorkCreated/Moved/Swapped`, `InfluenceLevelChanged`,
`IdeologyAdopted/Switched`, `PublicOpinionChanged`, `LeagueFounded`, `SpecialSessionStarted`, `ResolutionProposed`,
`ResolutionEnacted/Repealed/Failed`, `ProjectCompleted{tiers}`, `RebelsSpawned`, `CityRevolted`, `AntiquitySitesSpawned`.

### 2.5 Determinism rules specific to BNW [X]
- Use integer `Times100` arithmetic exactly where the engine does (trade yields §4.1.4). Keep Civ V's truncating
  integer division. Floats are used only where the engine uses them: diplomatic-victory votes (`log`, `floor`) and
  the science `ceil(diff/2.0f)`. Implement each float formula once in a helper with golden tests.
- Every random draw goes through the seeded RNG with a stable *call-site label*. The engine passes labels too,
  for example `"Rebel count rand roll"` and `"random factor on dig sites"`. Keep them for debugging.
- Ties are broken by civ index, then city founding order, then tile index. Never by hash order.

---------------------------------------------------------------------------------------------------

## 3. Ruleset content

### 3.1 Generator (ticket B002)
Input: `data/*.tsv` plus the G&K JSON. Output: every JSON file of the BNW ruleset. Rules:
1. **Match** BNW rows to G&K objects by English name, with the renames in §3.3. A matched object keeps its G&K JSON
   entry, *numbers are replaced* by BNW values (cost, strength, yields, maintenance, tech prereqs, upgrades,
   obsolescence), and uniques are re-derived from §3.2 for every BNW-changed column. Unciv-only uniques with no
   Civ V column (UI hints, AI hints) are kept.
2. **New** rows become new objects. Their uniques are built from §3.2.
3. **Removed** objects are dropped (§3.3). Report them in the generator's log, which the reviewer reads.
4. Ordering: write objects in BNW `ID` order, so the tech tree and civilopedia order match the game.
5. Validate with the existing `RulesetValidator`: zero errors, and warnings listed in the ticket.

### 3.2 Column → Unique mapping (the new BNW hooks)
Every column and table BNW added (`data/derived/schema_gk_to_bnw.txt`) needs either a JSON field, an existing
`UniqueType`, or a **new** `UniqueType`. Before adding a new one, the B003 implementer greps `UniqueType.cs` for an
equivalent. Proposed texts follow Unciv conventions. `[amount]`/`[stat]` are parameters.

**Buildings** [D]
| Column | Port as |
|--------|---------|
| `GreatWorkSlotType`, `GreatWorkCount` | JSON fields `greatWorkSlotType` (`Art/Artifact`, `Writing`, `Music`), `greatWorkSlots` |
| `FreeGreatWork` | unique `Contains the Great Work [name] when built` (Parthenon → `GREAT_WORK_PARTHENON_FRIEZE`) |
| `Building_ThemingBonuses` | JSON list `themingBonuses: [{bonus, sameEra, uniqueEras, mustBeArt, mustBeArtifact, mustBeEqualArtArtifact, requiresOwner, requiresAnyButOwner, requiresSamePlayer, requiresUniquePlayers}]` (§4.2.4) |
| `GreatWorksTourismModifier` | `[+amount]% Tourism from Great Works [in this city]` |
| `LandmarksTourismPercent` | `[amount]% of Culture from Wonders, Landmarks and Natural Wonders added as Tourism` |
| `TechEnhancedTourism`+`EnhancedYieldTech` | `[+amount] Tourism <after discovering [tech]>` (Eiffel Tower 12 @ Radio; Mughal Fort 2 @ Flight) |
| `AllowsFoodTradeRoutes` / `AllowsProductionTradeRoutes` | `Allows domestic [Food]/[Production] trade routes from this city` (Granary / Workshop, Longhouse) |
| `TradeRouteLandDistanceModifier` / `TradeRouteSeaDistanceModifier` | `[+amount]% [Land]/[Sea] trade route range [from this city]` (Caravansary +50 land, Harbor +50 sea) |
| `TradeRouteLandGoldBonus` / `TradeRouteSeaGoldBonus` | `[+amount] Gold (×1/100) from [Land]/[Sea] trade routes from this city` (Caravansary 200, Harbor 100). Values are Times100. |
| `TradeRouteRecipientBonus` / `TradeRouteTargetBonus` | `[+amount] Gold to the owner of an international trade route from this city` / `… to this city` (Market, Bank, Bazaar 1/1; Colossus 2/1; National Treasury 4/2) |
| `NumTradeRouteBonus` | `[+amount] trade route slots` (Colossus 1, Petra 1) |
| `CityStateTradeRouteProductionModifier` | `[+amount]% Production for each trade route to a city-state` (Hanse 5) |
| `ExtraLeagueVotes` | `[+amount] Delegates in the World Congress` (Forbidden Palace 2) |
| `UnlockedByLeague` | building is only buildable via league project reward (ISS) |
| `NullifyInfluenceModifier` | `Other civs' cultural influence is nullified here` (Great Firewall: §4.3.4 [?]) |
| `GreatScientistBeakerModifier` | `[+amount]% Science from Great Scientists' bulbs` (ISS 33) |
| `InstantMilitaryIncrease` | `Doubles the number of military units` (Terracotta Army) |
| `PolicyBranchType` | JSON `requiredPolicyBranch` (wonder needs that branch *opened*) [K]. 12 wonders. |
| `XBuiltTriggersIdeologyChoice` | `Building [amount] of these unlocks Ideologies` (Factory 3) |
| `CityConnectionTradeRouteModifier`, `ThemingBonusHelp`, `LeagueCost` | first: the existing city-connection gold unique; second: UI only; third: unused [D] |
| `Building_DomainFreeExperiencePerGreatWork` | `[+amount] XP for [Land/Sea] units per Great Work in this city` |
| `Building_YieldChangesPerReligion` | `[+amount] [stat] for each religion in this city` |

**Policies / Branches** [D]: `Level` (tenet level 1–3), `GreatArtist/Musician/Writer/Merchant/Scientist/AdmiralRateModifier`,
`OpenBordersTourismModifier`, `TradeRouteTourismModifier`, `SharedReligionTourismModifier`, `SharedIdeologyTourismModifier`,
`CommonFoeTourismModifier`, `LessHappyTourismModifier` (§4.3.2), `LandTradeRouteGoldChange`, `SeaTradeRouteGoldChange`,
`SharedIdeologyTradeGoldChange`, `CityStateTradeChange`, `InternalTradeRouteYieldModifier` (§4.1.4),
`ThemingBonusMultiplier`, `Policy_GreatWorkYieldChanges`, `Policy_BuildingClassTourismModifiers`,
`Policy_TourismOnUnitCreation` (one-shot tourism when a GP of that class is born), `RiggingElectionModifier`,
`StealTechFasterModifier`, `MinorBullyScoreModifier`, `AfraidMinorPerTurnInfluence`, `ProtectedMinorPerTurnInfluence`,
`MilitaryUnitGiftExtraInfluence`, `AbleToAnnexCityStates`, `BuildingPurchaseCostModifier`, `FreeBuildingOnConquest`,
`EnablesSSPartHurry/Purchase`, `AddReformationBelief`, `SecondReligionPantheon`, `CityConnectionTradeRouteGoldModifier`.
Branch columns `FirstAdopterFreePolicies` / `SecondAdopterFreePolicies` / `PurchaseByLevel` / `LockedWithoutReligion`
become JSON fields on `PolicyBranch`. Each policy column becomes one unique. The generator owns the text; B003 owns the
`UniqueType` entries and their effects.

**Traits** [D] (one unique each): `TechFromCityConquer` (Assyria), `GoldenAgeTourismModifier` and the three
`GoldenAgeGreat*RateModifier` (Brazil), `UniqueLuxuryCities/Quantity/RequiresNewArea` (Indonesia),
`Trait_YieldChangesPerTradePartner` and `Trait_YieldChangesIncomingTradeRoute` (Morocco), `FreeSocialPoliciesPerEra`
(Poland), `TradeRouteResourceModifier` (Portugal), `ExtraFoundedCityTerritoryClaimRange` (Shoshone), `NumTradeRoutesModifier`,
`NoAnnexing` and `FreeUnit`@`FreeUnitPrereqTech` (Venice), `LevelExperienceModifier` (Zulu, existing). Changed G&K traits
also use `CapitalThemingBonusModifier`, `AfraidMinorPerTurnInfluence`, `AngerFreeIntrusionOfCityStates`,
`CityConnectionTradeRouteChange`, `FaithFromKills`, `LandTradeRouteRangeBonus`, `RiverTradeRoad`, `TradeBuildingModifier`,
`TradeReligionModifier`, `UnresearchedTechBonusFromKills`, `WorkerSpeedModifier`, `Trait_NoTrain`,
`Trait_MaintenanceModifierUnitCombats`. G&K's `TradeRouteChange` is gone.

**Units** [D]: `Trade` (trade unit flag), `BaseCultureTurnsToCount` (Writer), `OneShotTourism` /
`OneShotTourismPercentOthers` (Musician), `NumExoticGoods` (Nau), `CanBuyCityState` (Merchant of Venice),
`PurchaseOnly`, `MoveAfterPurchase`, `PolicyType` (unit needs a policy: Landsknecht), `FreePolicies`,
`Unit_UniqueNames.GreatWorkType` (a named GP creates that specific work).

**Promotions** [D]: `Sapper`, `HeavyCharge`, `IgnoreZOC`, `GoldenAgeValueFromKills`, `TradeMissionGold/InfluenceModifier`,
`NotWithUpgrade`, `HasPostCombatPromotions` + `UnitPromotions_PostCombatRandomPromotion` +
`PostCombatPromotionsExclusive`, `PromotionPrereqOr7..9` (more OR prereqs).

**Beliefs** [D]: `FaithBuildingTourism`, `Belief_BuildingClassTourism`, `Belief_BuildingClassFaithPurchase` (replaces
G&K `BuildingClassEnabled`/`EnablesFaithBuying`), `Belief_EraFaithUnitPurchase`, `FaithPurchaseAllGreatPeople`,
`Reformation` (the 5th, Reformation-tier belief), `SpyPressure`, `InquisitorPressureRetention`,
`OtherReligionPressureErosion`, `CityStateInfluenceModifier`, `ConvertsBarbarians`, `Belief_YieldChangeTradeRoute`.

**Techs** [D]: `AllowsWorldCongress` (Printing Press), `InternationalTradeRoutesChange` (+1 slot each: Animal Husbandry,
Sailing, Engineering, Compass, Banking, Biology, Railroad, Penicillin), `Technology_TradeRouteDomainExtraRange`,
`TriggersArchaeologicalSites` (Archaeology), `InfluenceSpreadModifier` (Internet +100), `ExtraVotesPerDiplomat`
(Globalization +1).

**Improvements** [D]: Landmark, Archaeological Dig, Feitoria (`OnlyCityStateTerritory`, `LuxuryCopiesSiphonedFromMinor`),
Chateau (`AdjacentLuxury`, `NoTwoAdjacent`), Kasbah, Brazilwood Camp, Terrace Farm. Plus the columns `RequiresImprovement`,
`Improvement_ValidImprovements`, `Improvement_YieldPerEra`, `RemovesResource`, `RequiresXAdjacentLand`, `IgnoreOwnership`,
`DisplacePillager`, `AllowsWalkWater`, `PromptWhenComplete`.

**Eras / Speeds** [D]: `Eras.LeaguePercent`, `TradeRouteFoodBonusTimes100`, `TradeRouteProductionBonusTimes100`,
`WarmongerPercent`, and `GameSpeeds.LeaguePercent` → JSON fields on `Era`/`Speed`.

**B003 result (declared hooks).** 105 `UniqueType` rows (ordinals 644..748, `UniqueTypeDataBnw.cs`, enum members after `BnwHappinessRules`),
declarations, parameter types and docs only; no effect is wired. Deliberate deviations from the proposed texts above: (1) the `[Land/Sea]`
and `[+amount]` forms are split into `Land`/`Sea` rows (`LandTradeRouteRange`, `SeaTradeRouteRange`, `GoldFromLandTradeRoutes`,
`GoldFromSeaTradeRoutes`) and use `[relativeAmount]`, because no new `UniqueParameterType` was added; (2) Great Work names use the
untyped `[param]` until B030 adds a Great Work parameter type; (3) the unit-XP domain uses `[mapUnitFilter]`. JSON-field columns
(theming bonuses, slots, `requiredPolicyBranch`, era/speed percents, branch adopter fields), `Level`, `NotWithUpgrade`,
`PromotionPrereqOr7..9`, `PolicyType`, `FreeUnit`@tech, `DisplacePillager` and `PromptWhenComplete` get no unique (fields, existing
conditionals or UI only; `RequiresImprovement`, `Improvement_ValidImprovements` and the 5th `Reformation` belief tier are JSON fields). Reuse instead of new rows: Great Person rate per era (conditional), `Ignores Zone of Control`,
`Cannot build [baseUnitFilter] units`, `[relativeAmount]% construction time for [improvementFilter] improvements` (Worker speed),
`[relativeAmount]% maintenance costs` (Trait_MaintenanceModifierUnitCombats, Trait_NoTrain), `May not annex cities` (Venice NoAnnexing), `Can move immediately once bought` (MoveAfterPurchase), `Can be purchased with [stat] [cityFilter]` (Belief_BuildingClassFaithPurchase, Belief_EraFaithUnitPurchase with `<starting from the [era]>`); 
the golden-age trait rates use `<during a Golden Age>` on the new rows `GreatPersonRateByType` (`[relativeAmount]% [greatPerson] generation`, per class; Kotlin only has the per-city `Great Person generation`) and `TourismDuringGoldenAge`.
 Rows kept next to a similar Kotlin unique are deliberate (per-class, per-promotion or per-city scope). Open [K]/[?] items are not resolved (no decompiler output in the repo); the spec values stand.

**B004 result (content that needs no new subsystem).** Wired in the engine, with tests (`BnwContentTests`): the improvement rules
`Can only be built in City-State territory` (replaces the own-border rule), `Can be built in any civ's territory`, `Must be built adjacent to a
luxury resource`, `Cannot be built adjacent to another [improvementFilter]`, `Requires [n] adjacent land tiles` and `Removes the resource from the
tile when built`; the Poland unique `[n] free Policies each time you enter a new era` (one per era passed; every policy branch, narrowed to the social branches by B051); the Shoshone unique `Founded cities claim [n] extra tiles of radius`, **read as n extra tiles** (the nearest unowned tiles
after the radius-1 ring, deterministic ring order). Zulu (`[-25]% XP required for promotions`) and Venice (`May not annex cities`,
`Cannot build [Settler] units`, free Merchant of Venice at Optics) use existing Kotlin uniques. New content in the generated ruleset: the
fifth belief tier `BeliefType.Reformation` (a founder-style tier appended after `Any`), the 13 beliefs BNW adds (the other 3 of the 16 exist in
G&K), and the improvements Feitoria, Chateau, Kasbah and Brazilwood Camp. [K] items kept at their spec values (no decompiler output in the repo):
the Religious Fervor purchase (land military units from the Industrial era at the Holy Warriors price), the Feitoria's one siphoned copy.
Still declared data only: tourism, trade route, spy, inquisitor, barbarian-conversion and Great People purchase effects (their subsystems).

**B050 result (the social policy tree, §4.6.1).** `gen_ruleset.py` now builds `Policies.json` from `PolicyBranchTypes.tsv`, `Policies.tsv` and
`Policy_PrereqPolicies.tsv` (structure: branch order, era prerequisite, grid, prerequisites, finisher last) plus the hand-kept effect table
`policyUniques` of `overrides.json`. Nine social branches: Tradition, Liberty, Honor, Piety (no era, `Only available <when religion is enabled>` is the
`LockedWithoutReligion` lock), Patronage and Aesthetics (Classical), Commerce and Exploration (Medieval), Rationalism (Renaissance); the G&K
exclusivity uniques (`Only available <before adopting [...]>`) of Piety/Rationalism are gone. Names follow the TSV (Mandate of Heaven, Religious
Tolerance, Philanthropy, Mercenary Army, Wagon Trains, Cultural Exchange; the G&K Educated Elite, Aesthetics-in-Patronage, Naval Tradition-in-Commerce
are replaced by the BNW membership). The 12 `Buildings.PolicyBranchType` wonders get `Only available <after adopting [Branch]>` (the branch policy is
adopted when the branch is opened; the rule reads "adopted" per [K], so it is a plain G&K unique, no engine change). Finishers: Tradition (growth,
free Aqueducts), Liberty (Great Person), Honor (gold from kills), Piety (free Great Prophet, +3 culture on Holy Sites), Patronage (allied
city-states gift Great People), Commerce (+1 gold on Trading Posts), Rationalism (1 free technology), Aesthetics (+100% theming bonuses, declared
data), Exploration (Antiquity Site reveal: B040, no effect yet). The G&K Great-Person-for-Faith purchases on the finishers are dropped; they return as
`Can purchase all Great People with Faith` (B011, belief To the Glory of God and the Industrial-era global uniques). Tests: `BnwPolicyTreeTests`.
**Deliberate deviations / open items.** Many stat columns keep the G&K wording where it matches the BNW meaning; the rest are listed in `policyPending` (report section
"pending policy effects"). The Exploration opener (`PROMOTION_NAVAL_TRADITION`) is +1 Movement and +1 Sight for naval units [K] (MovesChange and VisibilityChange 1 in UnitPromotions.tsv). B031 retuned Monarchy, Meritocracy, Warrior Code and Sovereignty to the BNW columns. No decompiler
output is in the repo, so the [K]/[?] items of §4.6.1 stand at their spec values.

### 3.3 Object diff vs Unciv G&K (`data/derived/objects_unciv_gk_vs_bnw.txt`)
- **Renames only** (same object, keep the Unciv name): The Pyramids, The Oracle, The Great Library, The Great
  Lighthouse, The Louvre (Unciv prefixes "The"); Railroads→Railroad and Pharmaceuticals→Penicillin (BNW tech names; take
  the BNW name and add a save-compat alias, §6.3); Persian Immortal→Immortal; Work Boats→Work Boat; Gold Ore→Gold
  (resource); case differences in beliefs and improvements. The generator holds an explicit rename table. Never fuzzy-match.
- **New buildings and wonders** [D]: Airport, Artists' Guild, Writers' Guild, Musicians' Guild, Caravansary, Zoo, Hotel,
  East India Company (replaces National Treasury in the tree; the class is kept), National Visitor Center, Broadway,
  Globe Theatre, Parthenon, Prora, Uffizi, International Space Station (league only), Royal Library (Hanse, Ikanda,
  Candi and Ducal Stable are civ uniques). Verify Borobudur and Red Fort: they are G&K content the Unciv ruleset may
  model differently.
- **Removed / changed**: Theatre → Zoo (BNW renames the class). United Nations is no longer a wonder: the UN is a
  League special session (§4.7). Apollo, Manhattan and Utopia are Civ V *projects*. Unciv models them as buildings;
  keep Apollo and Manhattan, and drop Utopia (BNW has no Utopia).
- **New units**: Caravan, Cargo Ship, Archaeologist, Great Writer, Great Musician, XCOM Squad, and the BNW uniques
  (Siege Tower, Pracinha, Kris Swordsman, Berber Cavalry, Winged Hussar, Nau, Comanche Riders, Pathfinder, Merchant of
  Venice, Great Galleass, Impi). Check `Hand-Axe` and `Bazooka` against scenario-only content before adding them.
- **Policies**: replace the policy JSON entirely (BNW tree: 9 branches × (opener + 5 + finisher), plus 3 ideologies ×
  16 tenets). Unciv-only "X Complete" finishers map to BNW finisher policies.
- **Beliefs**: +16 BNW beliefs (Charitable Missions, Divine Inspiration, Earth Mother, Evangelism, God of Craftsmen,
  God-King, Heathen Conversion, Jesuit Education, Religious Fervor, Sacred Sites, Sun God, Swords into Plowshares,
  Tears of the Gods, To the Glory of God, Underground Sect, Unity of the Prophets).
- **Resources**: Bison, Cocoa, Cloves, Pepper, Nutmeg (Indonesia-only luxuries, `Resources.CivilizationType`), and
  Antiquity Site / Hidden Antiquity Site (artifact resources, §4.4).
- **Nations**: the 8 missing majors (Assyria, Brazil, Indonesia, Morocco, Poland, Portugal, Shoshone, Zulu). Venice
  becomes a **major** civ: delete the Venice city-state and add the BNW city-states (Bogota, Buenos Aires, Byblos,
  Ife, Kiev, Kyzyl, Malacca, Melbourne, Ormus, Panama City, Riga, Samarkand, Sofia, Vilnius, Wellington, Antananarivo,
  M'banza-Kongo). Unciv's Dublin, Edinburgh, Helsinki and Rio de Janeiro do not exist in BNW: drop them (the default)
  or keep them as an [X] owner decision.
- **Techs** [D]: identical costs. Additions: **The Internet** (Future era, cost 8800, prereq Telecommunications,
  `InfluenceSpreadModifier` 100). Future Tech also requires The Internet. Ballistics now requires Railroad as well as
  Flight. What each tech unlocks changes (new units and buildings): regenerate it, do not diff it by hand.
- **Promotions**: Unciv's G&K promotion names diverge heavily from Civ V internals (`[X] ability` promotions,
  "Besiege" and so on). Map only promotions that BNW *added or changed* (the `_Expansion2` XML), and keep Unciv's
  modelling for the rest.

---------------------------------------------------------------------------------------------------

## 4. Rules and formulas

### 4.1 Trade routes

#### 4.1.1 Model
A route is `{Id, Origin(x,y), Dest(x,y), OriginOwner, DestOwner, Domain(Land|Sea), Kind, UnitId, Path[], UnitPathIndex,
MovingForward, CircuitsCompleted, CircuitsToComplete, TurnRouteComplete, OriginYields[6], DestYields[6]}`. This is the
field layout of the saved `TradeConnection` record (@101fdfe0, version-gated fields: `TurnRouteComplete` is version ≥2) [O].
`Kind`: `International` (0), `Food` (1), `Production` (2) [O] (a domestic route is Food or Production).

#### 4.1.2 Trade units [D]
Caravan: land, cost 75, 1 move, Animal Husbandry. Cargo Ship: sea, cost 100, 1 move, Sailing. Both are `Trade=1`. A
trade unit cannot be built when no slot is free ("NO_EXTRA_SLOTS"), or when no city could be a target for that domain
("NONE_OF_TYPE_LAND/SEA") (@10152ed0 text keys) [O]. A trade unit's `GetTradeUnitType` is resolved by domain (@101f73c0) [O].
A trade unit can rebase to another own city (`ChangeTradeUnitHome`, `ChooseTradeUnitNewHome` popup [U]).

#### 4.1.3 Slots and range [O]
- **Slots** `@101fa000`: `(Σ tech.InternationalTradeRoutesChange over known techs + Σ building.NumTradeRouteBonus over
  every building in every city) × (100 + trait.NumTradeRoutesModifier) / 100`. Venice gets ×2. A civ with no capital has
  0 slots. "Used" = routes whose origin owner is this civ (`GetNumInternationalTradeRoutesUsed`, @101f7330). Domestic
  routes use slots too [K].
- **Range** `@101f9e00(domain, originCity)`: base `Sea 20 / Land 10`, plus `trait.LandTradeRouteRangeBonus` (land only),
  plus `Σ Technology_TradeRouteDomainExtraRange` for that domain over known techs, then
  `× (100 + Σ building range % in the origin city for that domain) / 100`. Range is the maximum **path length** for that
  domain's unit (pathfinder steps, not air distance) [K]. Routes must be pathable when established [K].

#### 4.1.4 Yields (`GetTradeConnectionValueTimes100`, @101f96f0) [O]
All values are ×100. `IsInternational` = the two owners differ (a city-state destination counts as international,
@101f84e0).

**International, origin side** (owner = origin owner):
```
GOLD   = max(100, ((base + gptOrigin + gptDest + resource + exclusive + policy + yourBldg + theirBldg + trait)
                    * (100 + (domain == Sea ? 100 : 0) + river)) / 100)
SCIENCE= (scienceBase(origin→dest) + influenceScienceBonus(destOwner)) * 100
```
- `base = INTERNATIONAL_TRADE_BASE` (100) (@101f8c50).
- `gptOrigin`, `gptDest` = `city.goldRateTimes100 / INTERNATIONAL_TRADE_CITY_GPT_DIVISOR` (20) for each end city (@101f8dd0).
- `resource` (@101f8f00): +50 for every **strategic or luxury** resource type present in exactly one of the two
  cities (local, worked or owned), then `× (100 + trait.TradeRouteResourceModifier) / 100` (Portugal ×2).
- `exclusive` = `INTERNATIONAL_TRADE_EXCLUSIVE_CONNECTION` (0 in BNW) when no other civ's route ends at this destination
  (@101f7bf0, @101f7500).
- `policy` (@101f7c20, international only): land → `LandTradeRouteGoldChange`, sea → `SeaTradeRouteGoldChange`
  (policy totals, already ×100 in data); `+ SharedIdeologyTradeGoldChange × 100` when both civs follow the same
  ideology; `+ CityStateTradeChange` when the destination is a city-state.
- `yourBldg` (@101f9170): Σ over buildings in the origin city of `TradeRouteSeaGoldBonus` (sea) or `TradeRouteLandGoldBonus`
  (land), `× (100 + trait.TradeBuildingModifier) / 100`.
- `theirBldg` (@101f9430): only when neither end is a city-state. As origin: `TradeRouteRecipientBonus` sum `× 100`.
  As destination: the destination city's `TradeRouteTargetBonus × 100`. Then `× (100 + trait.TradeBuildingModifier) / 100`.
- `trait` (@101f7dd0): `Trait yield change per trade partner[yield] × 100` (Morocco), international and origin only.
- `river` (@101f9680): +25 when the domain is Land and the relevant city's plot is on a river.
- `scienceBase` (@101f8c50): `d = techsKnownBy(destOwner) − techsKnownBy(originOwner)`. If `d > 0`:
  `max(ceil(d / 2.0f), 1)`, else 0. (The divisor 2.0f is the constant at `0x1031a068`.)
- `influenceScienceBonus` = `GetInfluenceTradeRouteScienceBonus(destOwner)` (@1006e720): bonus science from cultural
  influence over the destination (§4.3.5).

**International, destination side** (owner = destination owner):
```
GOLD    = ((100 + yourBldg + theirBldg + incomingTrait [I]) * (100 + (domain==Sea ? 100 : 0) + river)) / 100
SCIENCE = scienceBase(dest→origin) * 100        (reverse tech difference, no influence bonus)
```
**Domestic** (only the destination city gains; @101f96f0 returns 0 for the origin):
```
FOOD (kind Food)       = (100 + policy.InternalTradeRouteYieldModifier + (domain==Sea ? 100 : 0))
                         * (speed.GrowthPercent [I] * (300 + era.TradeRouteFoodBonusTimes100) / 100) / 100
PRODUCTION (kind Prod) = same with (speed.TrainPercent + speed.ConstructPercent)/2 [I]
                         and era.TradeRouteProductionBonusTimes100
```
`era` is the origin owner's current era [I]. The speed-percent getters (`@1010be70`, `@101106a0`, `@10110480`) are
labelled from usage only. B020 must confirm which speed fields they are.
Worked example (Standard speed, Ancient era, land, no modifiers): food route = `100 × (100×300/100) / 100` = 300 → 3 Food.

Yields are stored per route and recomputed every turn [K]. They are added to the city's stats as their own breakdown
line ("Food/Production/Gold/Science from Trade Routes", @1003ec70 text keys) [O].

#### 4.1.5 Other effects of an international route
- **Tourism**: +`TOURISM_MODIFIER_TRADE_ROUTE` (25) % toward the destination owner while connected (§4.3.2) [O].
- **Religion**: each route carries religious pressure both ways between the end cities. `Belief_YieldChangeTradeRoute`
  and `trait.TradeReligionModifier` modify it [K]. The pressure amount is [?] (`GetNumTradeRoutesAddingPressure`
  @101abeb0 is the entry point).
- **City-state quest** "Connect a trade route": +`MINOR_QUEST_FRIENDSHIP_TRADE_ROUTE` (40) influence (@10129790) [O].
- **Hanse**: +5 % Production per route from that city to a city-state [D].
- **Diplomacy**: the AI likes trade partners (`OPINION_WEIGHT_TRADE_MAX`) [D]. `IsEnemyRoute` (@1020fe90) exposes routes
  of civs at war to the tactical AI [O].

#### 4.1.6 Lifecycle
1. `EstablishTradeRoute`: valid if the unit is a trade unit in its home city, the target is within range by a valid
   path for its domain, the owners are not at war, there is no embargo (§4.7.5), the route is not a duplicate of an
   existing origin→dest route, and (domestic) the origin has the enabling building. The unit leaves the map and is
   bound to the route [K].
2. The unit moves along `Path` back and forth. `CircuitsToComplete` is chosen so the route lasts about 30 turns at
   Standard speed [K]. **[?]** The exact formula was not located (the writer of `+0x184` was not found). The B020 RE
   step must decode `CvGameTrade::CreateTradeRoute` (start from callers of the AI scorer `@101fa1e0`). The [X]
   placeholder until then is `circuits = max(1, ceil(30 * speed.TrainPercent/100 / turnsPerCircuit))`.
3. Completion: the route ends and the unit reappears in the origin city, free to start a new route [K].
4. **Plunder**: an enemy (at war) unit that enters the plot where the trade unit currently is destroys the route and
   the unit, and takes gold [K]. The amount is [?] (decode the plunder mission, `TACTICAL_PLUNDER_TRADE_UNIT` @101c2db0
   is the AI side). Barbarians plunder too (`AI_TACTICAL_BARBARIAN_PRIORITY_PLUNDER_TRADE_UNIT`). Routes are also
   destroyed by war declaration between the owners, city capture, or loss of path [K]. Notifications:
   `TRADE_ROUTE_ESTABLISHED/BROKEN` [O].
5. Civs remember plundered/expired connections for 30 turns (@101f7e40) [O]; this feeds the AI scorer.

#### 4.1.6a As built (B020)
Code: `Unciv.Core/Logic/Trade/Routes` (`TradeRouteRules`, `TradeRouteYields`, `TradeRouteTurns`), command `establish-trade-route`,
queries `empire.TradeRoutes` and `empire.TradeRouteOptions`, events `trade-route-established/completed/plundered/broken`. Everything is
gated by the ModOptions unique `Enables international trade routes`, never by ruleset name. Decisions where this section left a term open:
- **Policy columns** ([I]): `LandTradeRouteGoldChange` / `SeaTradeRouteGoldChange` are hundredths of Gold (the unique reads `Gold (x1/100)`);
  `GoldFromCityStateTradeRoutes` and `GoldFromIdeologyTradeRoutes` are whole Gold (x100 here). The generator (B002) must emit
  `CityStateTradeChange` divided by 100 as whole Gold; it was found stored in hundredths. B003 had no flat range uniques, so B020 adds
  `[relativeAmount] land trade route range` and `[relativeAmount] sea trade route range` (UniqueType 749 and 750).
- **Resource term** [X]: a city "has" a resource when a tile it owns carries it (local, worked and owned collapse to owned).
- **River** [I]: the +25 applies per side, to the city whose centre tile is next to a river, land routes only.
- **Destination side** [I]: the destination city's own land/sea route gold bonus (`yourBldg`) and `TradeRouteTargetGold` pay the destination owner only;
  the Morocco-style `[stats] from incoming trade routes` unique (declared by B003) is the destination counterpart of the origin trait term.
- **City gold per turn** [I]: `gptOrigin` / `gptDest` use the city's gold without the trade routes' own gold, so the value does not feed back into itself (approximate: city gold percent bonuses applied to the route gold are not removed).
- **Capture** [X]: a captured city's routes end at the next turn start of any civ (no hook in conquest); until then the new owner sees the yields.
- **Plunder** [X]: only a unit entering the route unit's tile plunders; the route unit walking onto a standing enemy does not.
- **Domestic yields** [X]: food uses the speed's `modifier`, production its `productionCostModifier` (Unciv has no growth percent); the era
  fields `tradeRouteFoodBonusTimes100` / `tradeRouteProductionBonusTimes100` default to 0.
- **Range and path** [X]: breadth first over passable land (land unit) or water (sea unit), counted in steps, borders, fog and units ignored.
- **Circuits** [X]: `ceil(max(1, 30 * speed.productionCostModifier) / turnsPerCircuit)` with `turnsPerCircuit = ceil(2 * steps / movement)`.
  The unit walks `movement` path steps at the start of its owner's turn.
- **Plunder** [X]: an enemy military unit entering a non-city tile with the route's unit takes `100 + (origin gold per turn) * (turns left)`.
- **Breaking** [X]: war between the owners (at once on declaration, and rechecked at turn start), a city that changed owner, or a defeated owner.
  The unit is lost. "Loss of path" is not checked. Embargoes end routes too (B016, 4.7.9).
- **Still open** (seams in the code): `TODO(B051)` shared ideology gold, tourism / religion /
  quest / Hanse effects of §4.1.5 (B021 and later), `[stats] for each international trade route [cityFilter]` and
  (read by B035: `[relativeAmount]% Production for each trade route to a city-state`).

#### 4.1.6b As built (B006)
Code: `TradeRouteRules` (`SlotsFree`, `HasTarget`, `CheckRebase`, `Rebase`), `ExoticGoods`, commands `rebase-trade-unit` and `sell-exotic-goods`,
rejection reasons `NoTradeRouteSlot` and `NoTradeRouteTarget` in `BaseUnit.GetRejectionReasons`. All of it is behind the ModOptions gate.
- **Generator**: the tech and building trade columns come from B033's sweep (slots, ranges, building gold, domestic enablers). B006 only turns Caravan and
  Cargo Ship into buildable units (`Can establish trade routes`, no `Unbuildable`) and gives the Nau `Carries [1] exotic goods`.
- **Build restrictions** [O names, X rule]: a trade unit is rejected with `NoTradeRouteSlot` when `Slots - used routes - trade units on the map <= 0`
  (idle units hold a slot [X]) and, when built in a city, with `NoTradeRouteTarget` when no other city within the air distance of the range passes
  `Check` (international for another civ's city, Food or Production for an own city with the enabling building; slots ignored). Both are construction
  rejections, so spawning or gifting such a unit is not blocked. Water units keep the coastal-city rule.
- **Rebasing** [X]: `ChangeTradeUnitHome` is the command `rebase-trade-unit {unitId, targetCityId}`: an idle trade unit standing in an own city moves to
  another own city that its domain can reach within the range of its current city (a ship needs a port) and whose centre tile can hold it; it has no
  movement left afterwards. A unit that already runs a route is not on the map, so it cannot rebase. The path ignores borders and terrain cost, like route paths.
- **AI** [deferred]: the AI does not use trade units yet but may build them (they then idle and hold slots); the AI ticket must handle that.
- **Exotic goods** [K, X scale]: `sell-exotic-goods {unitId, targetCityId}`. A unit with `Carries [n] exotic goods` that has movement, is adjacent to a
  city of another civ (a city-state counts) that its owner has met and is not at war with, and has a load left (`n` minus the saved `exoticGoodsSold`)
  sells one load: gold `min + (max - min) * min(d, 40) / 40` with `d` the air distance from the seller's capital to the city (`min` without a capital),
  experience the same way between `exoticGoodsXpMin/Max`; min/max are the ruleset constants (BNW 100..400 and 10..30). The unit stays and has no movement left.

#### 4.1.7 AI scoring of an international route (`@101fa1e0`) [O]
`danger = 1 + Σ over path plots (1 if the plot is not visible to us, +1000 if it is owned by a civ we are at war with)`.
`score = ((techDiff(dest−origin) × 3 + (originGold − destGold) + religionPressureDelta × 2) × 10) / danger`. Here
`originGold` is the full origin-side gold; `destGold` is the destination-side gold (for majors), floored at 1 for
city-states; and religion pressure deltas are divided by `RELIGION_MISSIONARY_PRESSURE_MULTIPLIER`. Then `×2` if we
generate any tourism and are not yet connected to that civ. Domestic scorers sit next to it (@101fa670, @101fa880) [I].
Detail for the AI ticket (A-track).

### 4.2 Great Works

#### 4.2.1 Data [D]
- Slot types: `ART_ARTIFACT`, `LITERATURE`, `MUSIC`. Classes: `ART` and `ARTIFACT` (both fit Art/Artifact slots),
  `LITERATURE`, `MUSIC`. `GreatWorks.tsv`: 94 Art, 30 Artifact, 85 Literature (15 of them `ArchaeologyOnly`, the hidden
  writing finds), 70 Music. Each has an `EraType`, plus `ArtifactClassType` for artifacts.
- Slot buildings: Palace 1 Art, Cathedral 1, Museum 2, Hermitage 3, Sistine 2, Louvre 4, Uffizi 3, Parthenon 1 (+ free
  frieze). Writing: Heroic Epic 1, National Epic 1, Amphitheater 1, Royal Library 1, Great Library 2, Oxford 2, Globe 2.
  Music: Opera House 1, Broadcast Tower 1, Ceilidh Hall 1, Sydney 2, Broadway 3.

#### 4.2.2 Yield of a work [O]
Each filled slot gives `BASE_CULTURE_PER_GREAT_WORK` (2) Culture and `BASE_TOURISM_PER_GREAT_WORK` (2) Tourism.
`Policy_GreatWorkYieldChanges` adds per-work yields (Aesthetics/ideology tenets) [D]. City tourism and culture also add
theming bonuses (§4.2.4).
Resolved [X] (B028): `Policy_GreatWorkYieldChanges` is `YieldPerGreatWork` (Creative Expression +1 Culture), emitted by the generator and
summed in `GetGreatWorkStats`. `Building_DomainFreeExperiencePerGreatWork` (Royal Library: 10 per work, Land/Water/Air) is `UnitXPPerGreatWork`: a unit
built or bought in the city starts with `amount x works in the city` more XP (`BaseUnit.AddConstructionBonuses`). The Enhanced Culture trait
(France) is `[+100]% theming bonuses in your capital`. Taking a swap offer notifies the offerer.

#### 4.2.3 Creation and placement
- A Great Writer, Artist or Musician in a city with a free slot of its type can *create* a work. The unit is consumed
  and the work goes into the closest free matching slot of the owner. "Closest" is by plot distance from the unit,
  ties broken by city order [O names: `GetCityOfClosestGreatWorkSlot`/`GetBuildingOfClosestGreatWorkSlot` @1006dec0]
  [I tie-break]. No free slot means the action is unavailable [K].
- **Which** work: a GP with `Unit_UniqueNames.GreatWorkType` creates that named work [D]. Otherwise: **[?]**.
  Resolved [X] (B007): the first unused, non-`ArchaeologyOnly` work of the class in table order whose era is not later
  than the creator's era (a work without era counts as eligible); when none qualifies, the first unused one. Each named
  work exists once per game; a used work (`GameInfo.IsGreatWorkUsed`) is never created again, even if its building was sold.
  A work created with no era takes the creator's era. The `greatWork` parameter type (ordinal 62) names a work of
  `GreatWorks.json`.
- The Parthenon gives its frieze when built [D]: `Contains the Great Work [greatWork] when built` puts the work into the
  first free slot of that building fitting its class, created once, as if made by the builder (B007).
- Works stay with their building: they are captured with the city and destroyed if the building is sold or destroyed
  [K]. `ClearGreatWorks` exists (@10071100) [O].
- **Moving / swapping**: an owner may rearrange works among their own slots at any time (UI drag in
  `CultureOverview.lua` [U]). Swapping with other civs is via a designated swap offer per class (Art, Artifact, Writing).
  Another civ takes it by giving one of its works of that class. Notifications `GREAT_WORK_ART_SWAP`,
  `GREAT_WORK_WRITING_SWAP`, `ARTIFACT_SWAP` (@10071c40) [O]. Implemented [X] (B007): `offer-great-work-swap` designates (or withdraws) one work per class
  (Music cannot be offered); `take-great-work-swap` needs both civs to know each other and not be at war, the given work
  must be of the offered work's class, and the offer is used up. The "own creation" restriction is not applied (deviation,
  noted on B007).

#### 4.2.4 Theming bonuses [D]+[K]
A building with `themingBonuses` gets a bonus only when **all** its slots are filled. The first bonus row (in data order)
whose conditions all hold applies:
`sameEra` (all works from one era), `uniqueEras` (all different eras), `mustBeArt`, `mustBeArtifact`,
`mustBeEqualArtArtifact` (half art, half artifact), `requiresOwner` (all created by this civ), `requiresAnyButOwner`
(none created by this civ), `requiresSamePlayer` (all created by one civ), `requiresUniquePlayers` (each by a different
civ). Bonus value `×(100 + policy.ThemingBonusMultiplier)/100`, and `×(100 + trait.CapitalThemingBonusModifier)/100`
in the capital [K]. The theming bonus is added to city tourism (§4.3.1, @100720f0) [O] and to culture [K].
The Museum has 12 rows (bonus 2 or 1), and each wonder has one row (Louvre 4, Uffizi 3, Hermitage 3, Broadway 3, and others).
The engine function is `GetThemingBonus` (@10078c00). Resolved [X] (B007): the rows are tried in data order and the first
whose conditions all hold wins; a work with unknown era or creator never satisfies `sameEra`, `uniqueEras`, `requiresOwner`,
`requiresSamePlayer` or `requiresUniquePlayers`, and a work with no creator counts as "not by the owner". The policy
multiplier is applied first, then the capital one, each truncating. Theming bonus is added to the city's culture
(source "Great Works"); tourism is B031.

### 4.3 Tourism, influence, culture victory

#### 4.3.1 City tourism (`GetBaseTourism` @10072360, `GetBaseTourismBeforeModifiers` @100720f0) [O]
```
if city is razing or in resistance: 0
before = 2·numGreatWorks + themingBonuses + (2·numGreatWorks)·city.GreatWorksTourismModifier/100
       + (if city.LandmarksTourismPercent > 0:
            (cultureFromWonders + cultureFromImprovements(landmarks) + cultureFromNaturalWonders) · pct / 100)
       + (majority-religion belief FaithBuildingTourism · numFaithBuildings in city)
       + Σ belief.BuildingClassTourism for each building class present
       + Σ building.TechEnhancedTourism for each present building whose tech is known
mod    = max(0, player.tourismModifier) + (tourismBonusTurns > 0 ? TEMPORARY_TOURISM_BOOST_MOD(100) : 0)
       + league/holy-city tourism modifier (World Religion resolution, @1024bd30)
       + (golden age ? trait.GoldenAgeTourismModifier : 0)          // Brazil Carnival +100
       + Σ Policy_BuildingClassTourismModifiers for present building classes
tourism = mod > 0 ? before · (100 + mod) / 100 : before
```
Player tourism = Σ city tourism (@10073fa0).

#### 4.3.2 Tourism modifier toward a civ (`GetTourismModifierWith` @1006ea20) [O]
Additive percent; `M` = me, `T` = target:
1. T's team allows M's team open borders → `+TOURISM_MODIFIER_OPEN_BORDERS + policy.OpenBordersTourismModifier`.
2. M has a trade route connected to T → `+TOURISM_MODIFIER_TRADE_ROUTE + policy.TradeRouteTourismModifier`.
3. Both have ideologies and they differ → `TOURISM_MODIFIER_DIFFERENT_IDEOLOGIES` (−34). Then, if M's diplomat is in T's
   capital, `+TOURISM_MODIFIER_DIPLOMAT` (+25). The diplomat bonus applies **only** in the different-ideology case.
4. `policy.LessHappyTourismModifier` if M's excess happiness is greater than T's.
5. `policy.CommonFoeTourismModifier`, once per third major civ that both M and T are at war with.
6. `policy.SharedIdeologyTourismModifier` if both follow the same ideology.
7. While M is in a golden age: `trait.GoldenAgeTourismModifier`.
8. T's cities mostly follow M's founded religion → `+TOURISM_MODIFIER_SHARED_RELIGION + policy.SharedReligionTourismModifier`.

**Label note:** in the decompile, steps 1, 2 and 8 all read the global labelled `SHARED_RELIGION`. Either the label
anchor is off for those adjacent globals, or the engine reuses one getter. All three are 25 in BNW, so the result
is the same; port them as three separate constants.

#### 4.3.3 Influence [O]
- Per turn, for each **met, living, major** T ≠ M (@10073c20):
  `influencePerTurn(T) = (Σ_city tourism(city) [+ extra §4.3.6]) × (100 + modifierWith(T)) / 100`, where the modifier is
  evaluated once (on the capital). `InfluenceOn[T] += influencePerTurn(T)` each turn [K accumulation point].
- `GetInfluenceLevel(T)` (@1006e410): `pct = InfluenceOn[T] × 100 / T.lifetimeCulture` (0 if lifetime is 0). Level:
  `≥200 Dominant(5)`, `≥100 Influential(4)`, `≥60 Popular(3)`, `≥30 Familiar(2)`, `≥10 Exotic(1)`, else Unknown(0)
  (`CULTURE_LEVEL_*`).
- Trend (@1006c4d0): compare `InfluenceOn[T] × T.lifetimeCulture_lastTurn` with `LastTurnInfluenceOn[T] × T.lifetimeCulture`.
  Greater = Rising (1), smaller = Falling (−1), equal = Static (0). Both lifetime values must be > 0.
- Turns to Influential (@10073ec0): if the level is < Influential and the trend is rising,
  `ceil((INFLUENTIAL·T.lifetime/100 − InfluenceOn[T]) / (influencePerTurn(T) − INFLUENTIAL·T.culturePerTurn/100))`,
  else 999.
- Least-influenced civ for victory progress (@1006e5d0): minimum `pct` among the living majors we have not reached
  Influential with, optionally only the met ones.

#### 4.3.3a As built (B008)
`Logic/Tourism/TourismRules.cs` (4.3.1, 4.3.2) and `InfluenceRules.cs` (4.3.3, 4.3.5, 4.3.6); state in `Civilization.Culture`
(`CivCultureManager`: per-civ entries `influence`, `lastTurnInfluence`, `lastTurnLifetimeCulture`, plus `tourismBonusTurns`; null until the first
turn of a game with tourism). Influence accumulates in `TurnManager.StartTurn` after trade routes. Lifetime culture is `TotalCultureForContests`.
`CULTURE_LEVEL_*` are ModConstants (`cultureLevel*`). Deliberate choices, none verified against the binary (no decompile available):
- "[x]% Tourism" is one unique; a city counts all that apply, and 4.3.2 step 7 counts the golden-age ones once more (spec lists both).
- Culture from wonders/landmarks/natural wonders = own stats of built world wonders + tile culture of Landmark and natural wonder tiles [I].
- Influence per turn is clamped at 0; percentage and trend use 64-bit products; the blast uses `Speed.Modifier * 100` as "speed%".
- Not applied (still open): diplomat bonus (B020, seam), ideology choice (B013; `PolicyManager.Ideology`
  is only a field), (InfluenceSpreadModifier/NullifiesForeignInfluence: see 4.3.4a), city-state spy rank, conquest resistance and revolt
  benefits of 4.3.5, one-shot tourism of a Great Person's birth (B011). Level benefits kept at the [K] values: Familiar +1 route science
  (wired into `TradeRouteYields`, origin side), Exotic +1 spy rank. The blast is an engine function (`ApplyBlast`); the unit action is B011.

#### 4.3.4 Culture victory [D]+[K]
`Victories.VICTORY_CULTURAL.Influential = 1`: a civ wins when it is **Influential or higher on every other living major
civ**. The check runs once per turn after influence, and civs that were never met still count [K]. Notifications for
"someone is influential / no longer influential" exist (@10075be0) [O]. The Great Firewall's `NullifyInfluenceModifier`
reduces the *tech-based* influence modifier (`InfluenceSpreadModifier`, Internet +100%) against its owner [K]; exact
math is [?].

#### 4.3.4a As built (B009)
`Victories.json` Cultural = milestone `Influential over all civilizations` (`MilestoneType.InfluentialOverAllCivs`, `Victory.cs`): completed when
`InfluenceRules.IsInfluentialOverAll` (level >= Influential over every other living major civ, at least one such civ, met or not; city-states and
dead civs do not count). It is the ordinary end-of-turn victory check (`TurnManager`), which runs after the start-of-turn influence. Also added:
`Win the World Leader vote` (`WinWorldLeaderVote`, same flag as `Win diplomatic vote`; B063 sets it). The generator keeps Cultural (with the new
milestone and header, `overrides.json`) and still drops Diplomatic until B063. Internet/Great Firewall (the [?] is closed by a deliberate choice, no
decompile available): the generator writes `[+100]% influence spread from Tourism` on The Internet (from `Technologies.tsv`) and
`Other civs' cultural influence is nullified here` on the Great Firewall; step 9 of 4.3.2 adds the sum of the influence-spread uniques of M's techs to the
modifier toward T, but 0 when T owns a nullifying building. The unique's "here" is read as civ-wide (the Great Firewall is a wonder). With tourism off the Cultural victory can be selected but never completes. The Great Firewall changes nothing else (the data value 1 is read as "all of it").

#### 4.3.5 Benefits of influence levels [O names, K values]
Lua-exposed: `GetInfluenceTradeRouteScienceBonus(T)` (@1006e720, adds science to routes to T), `GetInfluenceCityStateSpyRankBonus`
(@1006e860), `GetInfluenceMajorCivSpyRankBonus(T)` (@1006e8d0). [K] BNW: Familiar+ gives +1 science per route to T and
Exotic+ gives +1 spy rank in T. Conquering T's cities shortens resistance at Popular+, and conquered cities do not
riot from Influential [K]. Decode all three functions in B030 and replace these [K]s.

#### 4.3.6 Tourism blast (Great Musician *Concert Tour*) [O]+[D]
`GetTourismBlastStrength(unit)` (@10073ff0): `strength = playerTourismPerTurn × unit.OneShotTourism (10) × speed% / 100`,
at least `MINIUMUM_TOURISM_BLAST_STRENGTH` (100). The engine name has this spelling. It is applied in the target civ's
territory: `InfluenceOn[that civ] += strength` [K]. `OneShotTourismPercentOthers` (20) gives 20% of it to every other
met civ [K]. `Policy_TourismOnUnitCreation` gives a one-shot tourism amount when a GP of that class is born [D].

### 4.4 Archaeology

#### 4.4.1 Historical record [O names, K events]
Each tile can hold one record `{ArtifactClass, Era, Player1, Player2, GreatWork}` (`GetArchaeologyArtifact*`,
@1017bef0). Classes [D]: `BATTLE_RANGED`, `BATTLE_MELEE`, `BARBARIAN_CAMP`, `ANCIENT_RUIN`, `RAZED_CITY`, `WRITING`.
Events that write it [K]: a unit killed in combat (melee or ranged by attacker type; Player1 = winner, Player2 =
loser), a barbarian camp cleared, an ancient ruin popped, a city razed. Era is the game era at the event. A newer event
overwrites an older one [K]. Recording starts at game start. That is the C# port's job (the tile field in §2.2).

#### 4.4.2 Spawning sites (`SpawnArchaeologySitesHistorically` @100dae20) [O]
Triggered the first time any team learns a tech with `TriggersArchaeologicalSites` (Archaeology) [O, via setHasTech].
1. `n = majorCivsEverAlive`. `count = MIN(5)·n + rand((MAX(8)·n − MIN·n)/2) + rand(same)` (two independent draws).
2. `hiddenWritings = count × PERCENT_SITES_HIDDEN(30)/100 × PERCENT_HIDDEN_SITES_WRITING(30)/100`. The writing pool is
   all `ArchaeologyOnly` works (`SELECT ID FROM GreatWorks WHERE ArchaeologyOnly='1'`).
3. Every land tile gets a weight `w(tile)` (@100d4080), 0 if any of these hold: it already has a site, it has a resource,
   it is water, impassable or a mountain, it has a city or a non-removable feature, or its improvement or feature
   forbids it. Otherwise: `raw = (10 − recordEra) × (recordClass + 1)`. Tiles without a record still get the base term
   (`recordEra = −1` behaviour [?]). Then `w = (raw + 10 + rand(10)) × (owned ? 8 : 9)`, scaled by era tables and by the
   count of sites within 3 tiles (fewer is better). The exact constants after `rand(10)` are [?]. Port them from the
   SDK source, or decode the rest of @100d4080.
4. Repeat `count` times: take the tile with the highest weight. Roll `rand(100) < PERCENT_SITES_HIDDEN(30)` for
   **Hidden** (resource `RESOURCE_HIDDEN_ARTIFACTS`, else `RESOURCE_ARTIFACTS`). A tile with no record gets a random
   record (class `rand(7)` mapped into the classes, era weighted by elapsed eras [?]). If the site is hidden and writings
   remain, assign a random unused writing and set the class to `WRITING`. Then recompute the weights of every tile
   within distance 3.
5. Hidden sites are visible only to civs that have the reveal: `Resources.PolicyReveal` (the Exploration finisher) [D].

#### 4.4.3 Archaeologist and digs [D]+[K]
Archaeologist: cost 200, 4 moves, Archaeology, civilian, `WorkRate` 100, build `BUILD_ARCHAEOLOGY_DIG`
(@101022f0) [O]. It can dig on its owner's land, on unowned land, and on others' land (with a diplomacy penalty and an
AI "stop digging" promise: `TXT_KEY_DIPLO_NO_DIG_PROMISE_*`) [O texts]. It is consumed when the dig completes. The
player chooses (`ChooseArchaeologyPopup` [U], @1018a790 [O]):
- **Landmark**: the site becomes the Landmark improvement for the plot owner. Its culture grows with eras elapsed since
  the record era [K]. On city-state land: +`LANDMARK_MINOR_FRIENDSHIP_CHANGE` (50) influence [D].
- **Artifact**: the work (class from the record, `GREAT_WORK_ARTIFACT` table query @1006ca10) goes into a free
  Art/Artifact slot. It needs a free slot [K]. Writing records yield a Great Work of Writing instead.
- Artifacts list Player1 and Player2 as "creators". This matters for theming (`requiresUniquePlayers` and so on) [K].
- AI economic strategies `NEED/ENOUGH_ARCHAEOLOGISTS` (@100bab30) [O].

#### 4.4.4 As built (B010)
Code: `Unciv.Core/Logic/Archaeology/ArchaeologyRules.cs` (header lists every choice), `ArchaeologyRecord.cs`, `GameInfoArchaeology.cs`,
`Commands/Archaeology/ChooseDigReward.cs` (command `choose-dig-reward`, event `DigResolved`). All of it is gated by the ModOptions unique
"Enables archaeology"; Civ V G&K is unchanged. Resolved here ([K]/[?] became [X], deterministic, from the seeded `StateBasedRandom` only):
- Record: stored on the tile (`Tile.ArchaeologyRecord`, JSON key `archaeology`). The tiles are the source of truth, so there is no separate list of
  placed records (deviation from the 4.4.2 wording). Game era = average era number of the living major civs [X]. Players: winner/attacker is
  Player1, loser/defender Player2; a ruin has Player1 only; a razed city has the razer as Player1 and the founder as Player2 [X].
- Spawn: runs once (`GameInfo.ArchaeologySitesSpawned`) when a tech with "Triggers the creation of archaeological sites" is learned. Weight
  `(raw + 10 + rand(10)) * (owned 8 / unowned 9) * 4 / (4 + nearbySites)`, `raw = (10 - eraNumber) * (class + 1)`, 11 without a record [X]. A
  record-less site gets class `rand(6) + 1`, era `rand(currentEra + 1)` and two different random major civs [X]. Hidden sites reveal through
  a resource `revealedBy` that may now name a policy ("Exploration Complete"; `TechManager.IsRevealed`, ruleset validator updated).
- Dig: only a unit whose "Can build [X] improvements" names the dig (the Archaeologist) can build "Archaeological Dig" (`MapUnit.CanBuildImprovement`).
  When it completes the builder is consumed. A human gets a `PendingDig` and answers with `choose-dig-reward` (artifact or Landmark); an AI
  resolves at once: artifact if a slot is free, else Landmark [X]. Artifact choice: first unused work of the record class and era, else the era-less one [X];
  creator = Player1, creator2 = Player2, theming reads Player1 only [X]. Landmark culture is "[+1 Culture] per era since discovery" times the
  eras between the record era and the owner's era [X]; city-state land adds `LandmarkMinorFriendshipChange` influence.
- Not done (follow-ups): the diplomacy penalty for digging on foreign land and the AI "stop digging" promise (A track); AI use of Archaeologists
  and `NEED/ENOUGH_ARCHAEOLOGISTS`; the BNW Great Artist Landmark option (B041).

### 4.5 Great People additions [D]+[K]
- Writers', Artists' and Musicians' Guilds hold specialists (+3 Culture each, +3 GPP of their type) [D, Specialists.tsv].
- **Great Writer**: create a work, or **Political Treatise**: instant culture = `BaseCultureTurnsToCount` (8) × current
  culture per turn [K: × speed?]. **Great Artist**: create a work, or a **Golden Age** (`GoldenAgeTurns` 8, scaled as
  today). **Great Musician**: create a work, or a **Concert Tour** in a foreign civ's territory (§4.3.6).
- Born-rate modifiers per type: policy `Great*RateModifier`, trait golden-age modifiers, resolution
  `ArtsyGreatPersonRateMod` / `ScienceyGreatPersonRateMod` (Arts / Sciences Funding ±33). "Artsy" means Writer, Artist
  and Musician [D]. "Sciencey" means Scientist, Engineer and Merchant [K].
- Faith purchase of Great People from the Industrial era. `FaithPurchaseAllGreatPeople` beliefs widen the list [K].
- GP improvements (Academy, Customs House, Manufactory, Citadel) get +culture under Historical Landmarks
  (`GreatPersonTileImprovementCulture` 2) [D].

#### 4.5a As built (B011)
- **Create Great Work** (`UnitActionType.CreateGreatWork`, also the B007 command `create-great-work`): a Great Artist/Writer/Musician (by unit name) in a city of its owner with a free slot; a unit named after the author of a work (`GreatWork.greatPerson` against the unit's name, letters and digits only) creates that unused work first, else the B007 choice. `ExecuteUnitAction` carries it.
- **Political Treatise** (`HurryPolicy` action of the unique `Great Work counts as [8] turns of culture`, Great Writer): culture = 8 x (culture per turn rounded down), saturating, no game speed [K]. **Golden Age**: the existing unique of the Great Artist (8 turns). **Concert Tour** (`ConcertTour`, unique `Gives [10] Tourism with the target civ and [20]% with other civs when expended`): in tiles owned by a met living major civ, `InfluenceRules.ApplyBlast`.
- **Birth tourism** (`[amount] Tourism when a [greatPerson] is born`): the summed amounts (negatives count 0) are added as influence over every met living major civ when a Great Person is born from points or chosen as a free Great Person [K].
- **Guilds**: Writer, Artist and Musician specialists give 3 culture and 3 points of their Great Person (Specialists.tsv); the guilds hold 2 each (already in the generated data). Name groups Artist/Writer/Musician are now generated from the people the works are named after.
- **Faith purchase**: the Great Scientist, Merchant and Engineer cost 1000 Faith from the Industrial era for everybody (global uniques) [K, base list not decoded]; the belief To the Glory of God (`Can purchase all Great People with Faith <starting from the [Industrial era]>`) widens it to every Great Person except the Prophet. Price and count follow the engine's increasing price (1000 + 250 x (n^2 + n), the Unciv formula; BNW adds 500 per purchase [?]), counted per unit kind.
- Not done (open, not in the title): the per-type born-rate modifiers (`GreatPersonRateByType`); the resolution rates and Historical Landmarks culture are done (B016, 4.7.9).

### 4.6 Social policies and ideologies

#### 4.6.1 Branches [D]
| Branch | Era prereq | Note |
|--------|-----------|------|
| Tradition, Liberty, Honor | — | |
| Piety | — | `LockedWithoutReligion` (religion off → locked) |
| Patronage, Aesthetics | Classical | Aesthetics is new |
| Commerce, Exploration | Medieval | Exploration is new |
| Rationalism | Renaissance | |
| Freedom, Order, Autocracy | Industrial (data) | **Ideologies**: `PurchaseByLevel`, first adopter 2 free tenets, second adopter 1 |

Wonders gated by branch (`Buildings.PolicyBranchType`): Pyramids (Liberty), Hanging Gardens (Tradition), Statue of Zeus
(Honor), Mosque of Djenne (Piety), Forbidden Palace (Patronage), Uffizi (Aesthetics), Big Ben (Commerce), Louvre
(Exploration), Porcelain Tower (Rationalism), Statue of Liberty (Freedom), Kremlin (Order), Prora (Autocracy) [D].
Each needs the branch *adopted* [K]. The finisher policies grant what `FreeFinishingPolicy` names [D].

#### 4.6.2 Unlocking an ideology [O]
In player `doTurn` (@10174320): if the civ can choose (`@101906a0`), has no ideology yet, and is human or AI, and either
`currentEra > ERA_INDUSTRIAL` (that is, Modern or later; notification `CHOOSE_IDEOLOGY_ERA`) **or** it owns
≥ `XBuiltTriggersIdeologyChoice` (3) Factories (`CHOOSE_IDEOLOGY_FACTORIES`), it is prompted. It then picks one of the
three (`ChooseIdeology`). Adopting gives free tenets: 2 to the first civ in the game to adopt **that** ideology, 1 to
the second (`First/SecondAdopterFreePolicies`) [D]+[K].

#### 4.6.3 Tenets [D]+[K]
Each ideology has 16 tenets: 7 Level 1, 6 Level 2, 3 Level 3 (`Policies.Level`). A tenet costs one policy pick (normal
culture cost; free tenets cost nothing). You may adopt a Level n+1 tenet only if your Level n tenet count exceeds your
Level n+1 count. (So L2 needs more L1 than L2 tenets, and L3 needs more L2 than L3.) [K]. Tenets have no prereq tree
otherwise. The UI shows the counts per level [U].

**B013 result (ideologies, §4.6.2 and §4.6.3; spec id B051).** `Policies.json` now builds Freedom, Order and Autocracy from `PolicyBranchTypes.tsv` /
`Policies.tsv`: JSON fields `purchaseByLevel`, `firstAdopterFreePolicies` (2), `secondAdopterFreePolicies` (1) on the branch and `level` (1 to 3) on the
16 tenets (7/6/3, checked by the generator). The ruleset loader gives an ideology no finisher and its tenets no prerequisite. The Factory carries
`Building [3] of these unlocks Ideologies`. Engine (`PolicyManager`, saved only when set so G&K saves are byte-identical): `Ideology`, `FreeTenets`,
`CanChooseIdeology` (ModOptions `Enables ideologies`; the civ's era above the ideology branches' era, i.e. Modern or later, or three Factories; major
civs only; once), `ChooseIdeology` (free; the branch policy itself becomes adopted, which is what the Statue of Liberty / Kremlin / Prora gate reads;
the civ gets 2 free tenets when no other civ follows it, 1 when exactly one does, else 0), the level rule (a Level n+1 tenet needs more Level n than
Level n+1 tenets), and separate pools: free policies buy social policies only, free tenets tenets only, culture either (a culture-bought tenet counts
as a policy pick, a free one does not). This is also the Poland narrowing: `[n] free Policies each time you enter a new era` can never buy a tenet or
the ideology, so it is limited to social branches without a unique change. Commands: the existing `adopt-policy` names an ideology branch (choose,
free) or a tenet, so no `ChooseIdeology`/`AdoptTenet` command exists (deviation from the §2.4 names). `PendingDecisions` gets the non-blocking
`Ideology` decision; free tenets keep the blocking `Policy` decision. The AI picks the ideology branch of highest `PriorityMap` value, then spends
free tenets, then culture on tenets before social branches. A one-time notification (`CHOOSE_IDEOLOGY_ERA` / `_FACTORIES`) is sent at turn start.
**Deliberate deviations / open items.** The three "Universal Healthcare" tenets are named `Universal Healthcare (Freedom|Order|Autocracy)` (policy
names are unique). Tenets carry no effects yet (stat columns: B010; tourism, trade, espionage and league columns: their subsystems); they are listed
under `policyPending`. "First adopter" counts the civs that follow the ideology now (dead civs included): there is no adoption history until
B052 (switching) adds one (done, 4.6.6). Public opinion, unhappiness, switching and anarchy are B052. The policy screen (C track) shows the ideology branches through
the generic tree with the new locked reasons `ideology-locked`, `ideology:<branch>` and `tenet-level:<n>`; a dedicated ideology UI is a client follow-up.

**B030 result (tenet effects, spec id B030).** The 48 tenets now carry their effects. Scalar `Policies.tsv` columns and the small tables are written in
`overrides.json` `policyUniques` (keyed by tenet name); the building-class tables (`Policy_BuildingClassHappiness`, `ProductionModifiers`,
`TourismModifiers`, `YieldModifiers`) are derived by the generator (`tenet_building_uniques`), so Universal Healthcare is twelve `[+1 Happiness] from every
[<national wonder>]` uniques per ideology. Wired and tested on the real ruleset (`BnwGeneratedRulesetTests`): Cult of Personality (common foe), Dictatorship of the
Proletariat (less happy), Cultural Revolution (shared ideology), Media Culture (Broadcast Tower tourism), Futurism (250 tourism per Writer/Artist/Musician born),
Creative Expression (+1 culture per Great Work), Iron Curtain (+50% Food and Production of domestic routes; the free Courthouse on conquest is declared data only),
Economic Union (`GoldFromIdeologyTradeRoutes`: +3 whole Gold per route to a civ with the same ideology, now read by `TradeRouteYields.PolicyGold`), plus stat
tenets (Five-Year Plan, Party Leadership, ...). Declared but not wired yet: the city-state, espionage, spaceship, purchase-cost and free-building rows.
No unique exists yet for six tenets (`policyPending`): Resettlement, Patriotic War, Lightning Warfare, Elite Forces, United Front and
Clausewitz's Legacy (Double Agents got `chance to catch enemy spies` in B020). [I] scales: Treaty Organization +4 and Gunboat Diplomacy +6 Influence are the hundredths columns /100.

#### 4.6.4 Public opinion (`DoPublicOpinion` @1006f9d0) [O]
Only for a civ with an ideology `I`:
1. `pressure[X] = leaguePressure(X)` (World Ideology resolution, @1024be50) for each ideology X.
2. For every other living major O with ideology `J`: `d = influenceLevel(O over me) − influenceLevel(me over O)`. If
   `d > 0`, then `pressure[J] += d`.
3. Let `mine = pressure[I]` and `others = Σ pressure[X≠I]`. If `others > mine`: dissatisfaction `D = others − mine`.
   The preferred ideology is the other ideology with the larger pressure (ties [?]: follow the observed comparison
   order Freedom, Autocracy, Order). The opinion is `Dissidents` (D < 3), `Civil Resistance` (3 ≤ D ≤ 4), or
   `Revolutionary Wave` (D ≥ 5). Otherwise the opinion is `Content`, with no preferred ideology and 0 unhappiness.
4. **Opinion unhappiness** (@1006c5d0): `(perCity, popDiv) = (1,10) | (2,5) | (4,3)` for the three levels. Unhappiness
   is `max(totalPopulation / popDiv, numCities × perCity)`. It is added to empire unhappiness.
5. The tooltip lists the civs that pressure you, with ideology icons (UI query).

#### 4.6.5 Switching ideology [U]+[O]
`SwitchIdeology` is allowed whenever opinion unhappiness > 0 (`SocialPolicyPopup.lua`: the button is enabled iff
`GetPublicOpinionUnhappiness() > 0`). You switch to the **preferred** ideology. Effects:
`SWITCH_POLICY_BRANCHES_ANARCHY_TURNS` (2) turns of anarchy. Your tenets are lost, and you get back
`max(0, oldTenets − SWITCH_POLICY_BRANCHES_TENETS_LOST (2))` as free tenets in the new ideology [K]. The AI decision is
@1018d360 [O]: switch when excess happiness ≤ `SUPER_UNHAPPY_THRESHOLD` (−20), opinion unhappiness ≥ 10, and
`(prefScore − curScore) + opinionUnhappiness ≥ 10`. It refuses to leave an ideology that clearly fits its grand
strategy (`IDEOLOGY_PERCENT_CLEAR_VICTORY_PREF`). The AI ideology choice weights are the `IDEOLOGY_SCORE_*` defines [D].

#### 4.6.6 As built (B014, spec id B052)
`Logic/Tourism/PublicOpinionRules.cs` (4.6.4), `PolicyManager.CanSwitchIdeology/SwitchIdeology` (4.6.5), command `switch-ideology` (no payload; the
spec's `SwitchIdeology{}`). Public opinion is **derived, not saved**: it reads the ideologies and the influence levels (`InfluenceRules.Level`), so nothing
goes stale. Saved (only when set, G&K saves stay identical): `PolicyManager.AnarchyTurns` and `IdeologyHistory` (every ideology the civ has followed).
- Opinion unhappiness is added to the happiness breakdown as `Public opinion` (negative) while the opinion is not Content.
- B013's "first adopter" now counts the civs that **ever** followed the ideology (history), so a civ that switched away or died still counts.
- Switching goes to the preferred ideology and needs opinion unhappiness > 0. Tenets (and unspent free tenets) are lost; `max(0, adoptedTenets - 2)` free
  tenets are granted in the new ideology and **no** first/second adopter bonus; `switchPolicyBranchesAnarchyTurns` (2) turns of anarchy start.
  Costs no culture and refunds none. A second switch (or a new choice) is refused while anarchy runs.
- **Anarchy** (the spec only gives the duration; the effects are a deliberate choice, [?] in the binary): no policy, tenet or ideology can be adopted,
  and the civ's culture and science per turn are 0 (gold, faith and the cities' own yields are unchanged). It counts down in `PolicyManager.EndTurn`,
  so it covers the rest of the switching turn and the next full turn.
- AI (`NextTurnAutomation.SwitchIdeology`, @1018d360): switches when happiness <= `superUnhappyThreshold`, opinion unhappiness >= 10 and
  `(preferred priority - current priority) + unhappiness >= 10`. The scores are the branch `PriorityMap` values (the `IDEOLOGY_SCORE_*` defines
  are not in the ruleset); the grand-strategy veto (`IDEOLOGY_PERCENT_CLEAR_VICTORY_PREF`) is not ported.
- Not done: the World Ideology resolution pressure is done (B016, 4.7.9); the pressure tooltip (step 5)
  is available as `PublicOpinion.Pressures` but no query/UI exposes it yet; uprisings, rebels and city revolt are done (B019, 4.8). The tie order Freedom,
  Autocracy, Order is kept as the spec's observed order ([?] stays open).

### 4.7 World Congress and United Nations

#### 4.7.1 Founding (`@10264850`, `FoundLeague` @10263720) [O]
Unless `GAMEOPTION_NO_LEAGUES` (which also disables `VICTORY_DIPLOMATIC`). Each game turn while no league exists, the
first living major civ (in civ order) that **satisfies the Congress condition** (`@101e5f60`: owns the
`AllowsWorldCongress` tech, Printing Press [I]) **and has met every other living major** founds it, provided at least
2 majors are alive. That civ becomes **host**. Founding picks the special session matching the current game era: the
session with the largest `EraTrigger` that is ≤ the game era, and at least the earliest session. It sets the session
interval to `session.TurnsBetweenSessions × speed.LeaguePercent / 100` (fallback `LEAGUE_SESSION_INTERVAL_BASE_TURNS` 12)
and announces `LEAGUE_SPECIAL_SESSION_START_WORLD_CONGRESS`. The first session's immediate proposal is
`CHANGE_LEAGUE_HOST` [D].

#### 4.7.2 Special sessions [D]
| Session | Era trigger | Civ delegates | Host bonus | Per CS ally | Turns between sessions | Recurring proposal |
|---------|-------------|---------------|------------|-------------|------------------------|--------------------|
| Start World Congress | Renaissance | 1 | +1 | 0 | 30 | — |
| Welcome City-States | Industrial | 2 | +1 | 1 | 25 | — |
| Leadership Council | Modern | 3 | +2 | 1 | 20 | — |
| United Nations | Atomic (`POSTMODERN`) | 4 | +2 | 2 | 10 | Diplomatic Victory (every session) |

Each turn: if the game era (@1024d960: the highest era among living majors [I]) is above the last triggered one, start
the matching special session (@102647c0). Otherwise run a normal turn.

#### 4.7.3 Delegates (`CalculateStartingVotesForMember` @1024e260) [O]
`votes = extra (member field) + core (session CivDelegates) + host (session HostDelegates if host)
+ Σ over allied city-states (session CityStateDelegates) + worldReligion (@1024a2e0) + worldIdeology (@1024a460)
+ wonders (city ExtraLeagueVotes, Forbidden Palace +2) + diplomats (per diplomat in a major capital: tech
ExtraVotesPerDiplomat, Globalization +1)`. World Religion: +`VotesForFollowingReligion` (2) for the religion's founder.
World Ideology: +`VotesForFollowingIdeology` (2) for each civ following it [D]. Failed diplomatic-victory votes give
`LeadersVoteBonusOnFail` (2) to the top vote-getters as `extra` [D]+[K].

#### 4.7.4 Session flow [K] (verify against `CvVotingClasses.cpp`)
1. Between sessions, the **proposers** get `LEAGUE_MEMBER_PROPOSALS_BASE` (1) proposal each: the host and the member
   with the most delegates (a second member if the host already is that member). A proposal enacts a resolution with
   its `ProposerDecision` choice, or repeals an active one. A resolution needs `TechPrereqAnyMember` known by any member,
   must not already be active or proposed, and (World Ideology) needs ideologies in play (@1025a790 and @1024dbe0 give
   the refusal reasons, which are the command's Validate codes) [O texts].
2. `LEAGUE_SESSION_SOON_WARNING_TURNS` (5) before the session, a notification is sent (@1024b320) [O].
3. At the session, members spend their delegates on each proposal (Yes/No, or one choice for multi-choice decisions).
   AI vote commitments from trade deals apply (`AddVoteCommitment`) [O name]. A resolution passes if Yes > No. A
   choice decision takes the plurality, with ties failing [K]. Results are announced (@102625e0, @10262d60) [O].
4. The Diplomatic Victory proposal (`AutomaticProposal` in the UN session) is voted each UN session. A civ wins when it
   receives ≥ `votesNeeded` (§4.7.6). Members can only vote for themselves or another *major* civ
   (`VoterDecision = MAJOR_CIV_MEMBER`) [D].

#### 4.7.5 Resolutions [D] (`Resolutions.tsv`)
| Resolution | Effect |
|------------|--------|
| Diplomatic Victory | §4.7.6. UN only, cannot be proposed by players. |
| Change League Host | Winner becomes host. |
| All City-States Embargo / Player Embargo (choice: other major) | No trade routes with the embargoed party [K: and no deals]. |
| Ban Luxury (choice: luxury) | That luxury gives no happiness (`NoResourceHappiness`). |
| Standing Army Tax | Unit maintenance gold +25%. |
| Scholars in Residence | Techs known by any member cost −20% (`MemberDiscoveredTechMod`). |
| World's Fair / International Games / International Space Station | Enable a league project (§4.7.7). Prereq techs: —, Radio, Satellites. |
| Cultural Heritage Sites | +3 Culture per World Wonder. |
| Natural Heritage Sites | +5 Culture per Natural Wonder. |
| Nuclear Non-Proliferation | Nukes cannot be built. Needs Advanced Ballistics. |
| World Religion (choice: religion) | +2 delegates to the founder, +50% holy city tourism, +25% spread strength. |
| World Ideology (choice: ideology, Radio) | +2 delegates per follower, and adds public-opinion pressure (`OtherIdeologyRebellionMod` 2). |
| Arts Funding / Sciences Funding | ±33% "artsy" / "sciencey" GP rate. |
| Historical Landmarks (Archaeology) | +2 Culture on GP improvements, +4 on Landmarks. |

Repeal restores the state. Effects are aggregated per player in `League.GetEffects(civ)` (the shape of the
`TXT_KEY_LEAGUE_OVERVIEW_EFFECT_SUMMARY_*` list, @1025f4b0) [O].

#### 4.7.6 Votes needed for diplomatic victory (@100d1830) [O]
```
civs = Σ over majors ever alive: alive ? 1.0 : 0.5          (float)
cs   = Σ over city-states ever alive (excluding [?] @1012a210): alive ? 1.0 : 0.5
civPart = max(0, DIPLO_VICTORY_CIV_DELEGATES_COEFFICIENT(1.443) · ln(civs) + DIPLO_VICTORY_CIV_DELEGATES_CONSTANT(7))
csPart  = max(0, DIPLO_VICTORY_CS_DELEGATES_COEFFICIENT(16.023) · ln(cs) + DIPLO_VICTORY_CS_DELEGATES_CONSTANT(-13.758))
needed  = (int) floor(civPart + csPart)
needed  = max(needed, UN.CivDelegates + 1 + UN.HostDelegates)
needed  = min(needed, UN.CivDelegates·(int)civs + UN.CityStateDelegates·(int)cs + UN.HostDelegates)
```
Example: 8 civs and 16 city-states, all alive → floor(10.0 + 30.67) = 40 of 66 possible. Use `float` (single precision)
and `MathF.Log` behind a golden-tested helper. The `DIPLO_VICTORY_ALGORITHM_THRESHOLD` / `BEYOND_ALGORITHM_MULTIPLIER` /
`TEAM_MULTIPLIER` / `DEFAULT_VOTE_PERCENT` defines exist but were not seen in this function [?]. Today's
`VictoryManager.VotesNeededForDiplomaticVictory` (G&K) stays for G&K rulesets.

#### 4.7.7 League projects [D]+[K]
World's Fair (cost 350/player), International Games (720/player), ISS (850/player). Cities contribute production via a
process (`PROCESS_*`). Total cost = `CostPerPlayer × numMajorMembers` × speed and era scaling (`Eras.LeaguePercent` [I]).
Reward tiers: Tier 1 if contribution ≥ `TIER_1_THRESHOLD` (0.5) × (cost / members); Tier 2 if ≥ `TIER_2_THRESHOLD` (1.0) ×
that; Tier 3 for the top contributor (`GetMemberContributionTier` @102528a0) [O name]. Tiers stack (lower tiers are
included). Rewards [D]:
- World's Fair: T1 +500 Golden Age points; T2 +1 free policy; T3 +20 turns of +100% culture (`CultureBonusTurns`).
- Games: T1 +3 Happiness; T2 +3 Happiness and +30 influence with all city-states; T3 +1 free policy and +20 turns of
  +100% tourism.
- ISS: T1 +4 turns of science (`BaseBeakersTurnsToCount`); T2 a free Great Scientist; T3 the ISS building.

#### 4.7.8 As built (B015)
`Logic/WorldCongress/` (`League` state, `LeagueData` tables, `LeagueRules`, the minimal `LeagueAi`), `GameInfo.League` (null and omitted from the save until
founded), commands `propose-resolution`, `propose-repeal`, `commit-votes`, query `empire.League`, events `league-founded`, `special-session-started`,
`resolution-proposed/enacted/repealed/failed`. The league turn runs in `GameInfo` where `Turns++` happens (sequential wrap and simultaneous `StartNextTurn`).
The tech hook (`Allows founding the World Congress` on Printing Press, `[+1] votes per Diplomat` on Globalization) and `[+2] Delegates` (Forbidden Palace) are
generated. Deliberate choices, none verified against the binary (no decompile available):
- Resolutions and special sessions are engine tables (`LeagueData`), not ruleset objects; resolutions use the 4.7.5 names.
- Session shape [K]: a session opens at the end of a round (AI votes at once), humans commit delegates during the next round, it is decided at the end of
  that round; unspent delegates abstain. The gap to the next session is `TurnsBetweenSessions x Speed.Modifier` percent (the speed has no LeaguePercent field; the
  values coincide). A special session holds the automatic proposals (Change League Host; in the UN also Diplomatic Victory) plus proposals already made.
- Delegates are spent per proposal, one choice per proposal and civ, from the pool of the delegates the civ had when the session opened. Yes/No passes with
  Yes > No; a vote on a civ takes the plurality and a tie or no vote fails [K]. Members are the major civs alive at founding.
- Proposers: the host and the member with the most delegates (next one if that is the host); `LEAGUE_MEMBER_PROPOSALS_BASE` proposals each per session gap.
  A resolution needs a tech known by any living member, must not be in force or proposed (same choice), and its choice must be valid (another member, a luxury,
  a founded religion, an ideology of the ruleset; World Ideology also needs a living member with an ideology).
- World Religion's holy-city tourism (+50%) is added to the city tourism modifier (4.3.1); the other effects of resolutions are B016 (4.7.9), projects B017.
- Diplomatic Victory: see 4.7.10 (B018). Delegates per diplomat in a capital is B020 (seam in `StartingVotes`).
- The AI proposes a choice-free resolution (picked by turn and civ index) and backs its own and its friends' proposals, opposes its enemies'.
- No pending-decision kind: the client reads `empire.League` (`CanPropose`, `Left`).

#### 4.7.9 Resolution effects as built (B016)
`LeagueEffects` (`Logic/WorldCongress/LeagueEffects.cs`) is `League.GetEffects(civ)`: the numbers of the enacted resolutions for one league member (no effects for
a non-member, a city-state or a game without a league); each system asks it, nothing else reads the table, so a repeal restores the state. After a decided
proposal `LeagueEffects.OnChanged` ends forbidden trade routes and refreshes the city and civ stats of the members. Effects, one per row of 4.7.5, with the
readings of the open [K] items (no decompile available, all deliberate):
- Change League Host: done in B015. World's Fair / International Games / ISS: `EnabledProjects(game)` lists the enabled project names (B017, 4.7.11, builds them).
- Embargoes: `IsEmbargoed(a, b)` is true when one side is the embargoed civ, or one side is a city-state under All City-States Embargo. It refuses a new route
  (`TradeRouteRules.Check`) and ends an existing one (`BreakInvalid`). Trade deals are not covered [K].
- Ban Luxury: the luxury is removed from the own, city-state-ally and traded luxury happiness lines.
- Standing Army Tax: unit maintenance gold x (1 + 25/100) in float before the Int conversion. Scholars in Residence: a tech some other living member knows costs
  x (1 - 20/100) (after the other tech cost modifiers).
- Cultural / Natural Heritage Sites: +3 culture per World Wonder in a city, +5 per Natural Wonder tile the city owns, in the city's base stats ("World Congress" line).
- Nuclear Non-Proliferation: nuclear units get a `DisabledBySetting` rejection (cannot be built or bought); existing ones stay usable.
- World Religion: spread pressure of cities and of Missionary / Prophet actions of the chosen religion x 1.25 (tourism +50% and the delegates were B015).
- World Ideology: `OtherIdeologyRebellionMod` (2) is the league pressure toward the chosen ideology in 4.6.4 step 1, for every member [K reading of @1024be50].
- Arts / Sciences Funding: +33 / -33 percent in the Great Person points breakdown of the Writer, Artist, Musician (artsy) and Scientist, Engineer, Merchant (sciencey).
- Historical Landmarks: +2 culture on each worked tile with an unpillaged Great Person improvement, +4 on each worked Landmark.
- Diplomatic Victory: 4.7.10 (B018).

#### 4.7.10 Diplomatic victory as built (B018)
`LeagueRules.VotesNeeded` implements 4.7.6 in `float` (`MathF.Log`, `MathF.Floor`), golden-tested (8 civs and 16 city-states give 40). `VotesNeeded(game, league)` counts
the civilizations list: a living major civ or city-state 1.0, a defeated one 0.5 (the `[?]` exclusion of `@1012a210` is not applied); the session is the current one
(the vote only exists in the United Nations session). The Diplomatic Victory proposal is a vote on a living member: the winner needs the plurality (a tie fails), at least
the votes needed and to be alive. A win (only when the Diplomatic victory is available to the winner; otherwise the vote fails with a notification) sets `VictoryManager.HasEverWonDiplomaticVote` and runs `UpdateWinningCiv`, so the game is won through the ruleset: the generated BNW
`VictoryTypes.json` keeps the `Diplomatic` victory with the milestone `Win the World Leader vote` (B009; the generator no longer removes the victory, the United Nations building
stays removed), and the victory counts only when it is among `GameParameters.VictoryTypes`. A failed vote gives every civ tied for the most votes `LeadersVoteBonusOnFail` (2)
`ExtraVotes` (`AddExtraVotes`; not when the victory is not enabled) [K reading of "top vote-getters"]; no vote at all gives nothing. Only the delegates of civs alive when the session is decided count
(also for the other votes: a civ that died keeps no delegates in a vote), and a dead candidate gets none. G&K `VictoryManager.VotesNeededForDiplomaticVictory` and the
G&K vote are unchanged; the AI victory scorer still reads the G&K figure (follow-up).

#### 4.7.11 League projects as built (B017)
`LeagueProjects` (`Logic/WorldCongress/LeagueProjects.cs`). A project resolution enables its project (`LeagueEffects.EnabledProjects`); cities of the living members work on the
process `"{project} Project"` (`PerpetualConstruction.LeagueProcess`, offered only while the project is enabled; the name differs from the project because the International Space
Station is also a building) and each turn put `round(production) + overflow` into `League.Projects` (per-civ contributions). Once per game turn in `LeagueRules.ProcessTurn`
(after all civs, so the civ order does not matter) a project whose total reaches its cost completes. Deliberate readings ([K]/[I], no decompile available):
- Cost = `CostPerPlayer` (350 / 720 / 850) x living members x `Speed.ProductionCostModifier`, half-up; the era scaling (`Eras.LeaguePercent` [I]) is not applied.
- Tier 1 from `LeagueProjectRewardTier1Threshold` (0.5) x (cost / living members), tier 2 from `...Tier2Threshold` (1) x that, tier 3 for the top contributor (all civs tied
  for a top above zero); the tiers stack (tier 3 gets the rewards of 1 and 2). Only living members are rewarded.
- Completion removes the project and the resolution(s) that enabled it (a new vote starts it again); repealing the resolution drops the progress. Event `project-completed`
  (`Project`, `Tiers` per living member), a notification for the humans, the `empire.League` query lists the enabled projects (`Projects`: cost, total, own contribution).
- Rewards: World's Fair 500 Golden Age points / 1 free policy / 20 turns of +100% culture in every city (`CivCultureManager.CultureBonusTurns`, "World's Fair" percent line);
  Games +3 happiness (permanent, `LeagueMember.ProjectHappiness`, "World Congress" happiness line) / another +3 and +30 influence with every known city-state / 1 free policy and 20 turns
  of `TourismBonusTurns`; Space Station 4 turns of the current science / a Great Scientist next to the capital / the International Space Station building in the capital.
- The AI only works on a project with a city that would otherwise be idle (follow-up: a proper AI valuation).

### 4.8 Happiness (BNW) [O]
- `IsEmpireUnhappy` = excess happiness < 0 (@10150dc0). **While unhappy**: production and gold get
  `max(−40, 2 × excess)` % (@1003ea70), and combat strength gets `max(−40, 2 × excess)` % (@1020fb90). (All from the
  `VERY_UNHAPPY_*_PER_UNHAPPY`/`MAX` defines.) Growth: −75% unhappy, −100% very unhappy (existing defines).
- `IsEmpireVeryUnhappy` = excess ≤ `VERY_UNHAPPY_THRESHOLD` (−10) (@10150e00): no settlers (existing), and **uprisings**
  (@1015c340). When very unhappy, if the uprising counter is < 1, set it to `UPRISING_COUNTER_MIN(4) + rand(UPRISING_COUNTER_POSSIBLE(3))`.
  At speeds above 100% it is scaled by speed%, halved on the first arming, and at least 1 (@10154110). Each turn it
  counts down. At 0, spawn rebels (@101541b0) and re-arm.
- **Rebels** (@101541b0): `num = (rand(numCities × UPRISING_NUM_CITY_COUNT(20) + 100) + UPRISING_NUM_BASE(100)) / 100`.
  Pick the city by `cityWeight + rand(10)` (weight from @100cc2f0 [?]). Pick a plot in the city's area with
  `rand(10)`, +4 for a forest/jungle feature [I], +7 more for a specific feature subtype [?], +4 if the plot has a
  route (`@1017e7c0(0x3f)`) [I]. The plot must be owned by this civ, not have a city, not be water, and not hold units.
  Spawn `num` barbarian units of the best available type there and notify `REBELS` [O].
- **City revolt** (@1016c270): when excess ≤ `SUPER_UNHAPPY_THRESHOLD` (−20) **and** public-opinion unhappiness > 0, a
  revolt counter starts at `REVOLT_COUNTER_MIN(5)` × speed% (only if > 100%), at least 1. At 0, a city flips
  (@1016a790 [?]: which city and to whom). The civ with the most influence and the preferred ideology is [K]. Then
  re-arm.

**As built (B019, `BnwHappinessRules`, gated by the `BNW happiness rules` unique; G&K is unchanged).**
- Graded penalty `max(-40, 2 x excess)` percent (-2 per point, cap -40, hard-coded) on each city's production and gross gold (`CityStats`, source "Unhappiness") and on unit
  strength (`BattleDamage`, modifier "Unhappiness"). The generated BNW `GlobalUniques.json` no longer has the fixed -50% production, -33% strength and rebel lines
  (`overrides.json`); growth -75%/-100% and "no Settlers" stay as global uniques.
- Uprising counter = the `RevoltSpawning` civ flag, armed at excess <= `veryUnhappyThreshold`: `min + rand(possible)`, x speed% above 100%, halved on the first arming, at least 1.
  At 0 the rebels spawn and it re-arms (full length) while still very unhappy; it is dropped when not. City revolt = the `CityRevolt` flag, armed at excess <= `superUnhappyThreshold`
  with opinion unhappiness > 0, `revoltCounterMin` x speed% above 100%, at least 1; at 0 a city flips and it re-arms.
- Rebels: count `(rand(cities x 20 + 100) + 100) / 100`; city weight population + rand(10) [?]; plot rand(10) +4 forest/jungle +4 route (+7 subtype [?] not applied), owned land, no city, no units;
  the unit is the strongest buildable land melee non-unique unit; owned by the barbarian civ; nothing spawns without one.
- City revolt target [?, a documented choice]: the receiving civ follows the preferred ideology and pressures us most (ties: civ order); the non-capital city nearest to one of its cities moves
  (`MoveToCiv`). Both civs get a notification. The `RebelsSpawned`/`CityRevolted` events of 2.4 have no hook yet.

### 4.9 Espionage [O]+[D]+[K]
- **Diplomats**: a spy placed in another *major* civ's capital may be a diplomat instead of a spy [K]. A diplomat
  cannot steal or be killed, gives +25% tourism only against a different ideology (§4.3.2 step 3) [O], gives
  delegates with Globalization (§4.7.3) [O], and allows viewing the target's votes (`MEMBER_INTRIGUE`) [O text].
- Coup chance: new `ESPIONAGE_COUP_SPY_LEVEL_DELTA_THREE/FOUR` (2.60/2.80) extend the existing per-level table [D].
  Coups drop other players' influence (`ESPIONAGE_COUP_OTHER_PLAYERS_INFLUENCE_DROP`) [D].
- Spy rank bonus from cultural influence over the target (§4.3.5) [O name].
- Policies `RiggingElectionModifier`, `StealTechFasterModifier` and `Policy espionage modifiers` (Lua
  `GetPolicyEspionage*`) [D]. The engine already has `EspionageManager`; extend it, do not replace it.

#### 4.9a As built (B020, spec id B071)
- **Diplomat** (`Spy.IsDiplomat`, saved as `isDiplomat`, false in every save, missing in older saves): `MoveSpy { Diplomat = true }` stations a spy as a diplomat when the World Congress is enabled in the
  ruleset and the target is the capital of another major civ. [K]: the rule "when it may be a diplomat" is not decoded; this is the conservative reading. After the 3 turns of
  establishing a network a diplomat stays in surveillance: it never steals and, since it takes part in neither theft nor coup, is never killed. `EspionageManager.GetDiplomats`
  counts only diplomats that finished moving (set up) in a living major civ's capital.
- Uses: the tourism step 3 bonus (`TourismRules.ModifierWith`, only against another ideology), delegates (`WorldCongressVotesPerDiplomat` times the diplomats, in `LeagueRules.StartingVotes`),
  and MEMBER_INTRIGUE: `LeagueQuery` adds the votes of civs whose capital holds one of the viewer's diplomats to the tally while a session is open.
- Spy rank bonus from influence: `Spy.GetEffectiveRank` adds `InfluenceRules.MajorCivSpyRankBonus` (+1 at Exotic, [K]) in a major civ's city; the stored rank is unchanged.
- Coup: `ESPIONAGE_COUP_OTHER_PLAYERS_INFLUENCE_DROP` (20) replaces Kotlin's guessed 10. The per-level table `ESPIONAGE_COUP_SPY_LEVEL_DELTA_ONE..FOUR` (1.5, 2.25, 2.6, 2.8; zero is 0)
  is the diminishing curve 3*(1-0.5^n) (`ESPIONAGE_COUP_MULTIPLY_CONSTANT` is its limit 3). How the engine applies it is [?]; deliberate deviation: the rank difference between the coup spy and the
  defending spy indexes the table at 10% per unit (a difference of 1 is the Kotlin 15%, 2 gives 22.5, 3 gives 26, 4 and more 28), applied only when the table is in the ruleset.
  `ESPIONAGE_COUP_NOBODY_BONUS` is not applied.
- Policies: `TechStealingSpeed` (Industrial Espionage, +100%) multiplies the stealing progress; `ElectionRiggingModifier` (Covert Action, +100%) multiplies the votes of the rigging spy;
  Double Agents (`CatchSpiesModifier` 100) is the new unique `[+100]% chance to catch enemy spies` ([I]: it scales the defending spy's skill in theft and coup).
- Not done: AI use of diplomats, the building espionage columns (Constabulary, Police Station, Intelligence Agency, Great Firewall).

### 4.10 City-states [D]
- New quest type: connect a trade route (+40). `QUEST_DISABLED_TRADE_ROUTE` = 0.
- Influence anchor (the resting point): default 0, `+5` if protected, `−20` if wary of you
  (`MINOR_FRIENDSHIP_ANCHOR_*`) [D]. Per-turn influence policies (`Afraid/ProtectedMinorPerTurnInfluence`).
- `FRIENDSHIP_NEUTRAL/FRIENDS/ALLIES_ON_DEATH` (0 / 15 / 45) [D]: the influence a civ keeps with a city-state that
  was conquered and is later liberated, by its relationship at death [K].
- Unit gifts travel `MINOR_UNIT_GIFT_TRAVEL_TURNS` (3) turns. `FRIENDSHIP_PER_UNIT_GIFTED` 2→5,
  `MINOR_LIBERATION_FRIENDSHIP` 60→105.
- The Merchant of Venice can buy a city-state (`CanBuyCityState`), which turns it into a puppet [K]. Venice cannot
  found or annex cities (`NoAnnexing`; Venice has no settlers via `Trait_NoTrain`) [D].
- Bully/tribute rework: `MinorBullyScoreModifier` policies (the bully score function @10137010) [O text].
- **Engine status (B021)**: wired: the Merchant of Venice buy (`BuyCityState` command, unique `Can buy a City-State`; [?] price = the diplomatic
  marriage price, unit spent, no ally requirement, cities become puppets), the anchors (`MinorFriendshipAnchor*` ModConstants; G&K keeps +10 protected),
  `Afraid/ProtectedMinorPerTurnInfluence` tenets (whole points per turn), `FRIENDSHIP_*_ON_DEATH` (applied when a battle destroys the city-state;
  worse than Neutral is left alone [?]), and the trade-route quest (`Trade Route`, +40; weights [X]). Venice's no-annex / no-Settlers were already wired.
- **Engine status (B034)**: wired: the Feitoria luxury siphon (`FeitoriaRules`: an unpillaged improvement with `Gains [n] copies of the luxury resource from the
  City-State` in city-state territory gives its civ n copies of every luxury that city-state's cities produce, origin "Feitoria"; [?] tiles record no builder,
  so the civ is the one the improvement is unique to), Charitable Missions (`[+30]% Influence gained with City-States` scales positive influence from gold gifts,
  unit gifts and quest rewards, `CityStateFunctions.ScaleInfluenceGain`), and Scholasticism (the existing Kotlin `Allied City-States provide [stat] equal to [n]%`,
  tested on the real ruleset; the old `TODO(B072)` seam note was stale).
  Not wired (follow-ups): `MinorBullyScoreModifier` / `% bullying strength`, `extra Influence from gifting military units`,
  `MINOR_UNIT_GIFT_TRAVEL_TURNS` and `FRIENDSHIP_PER_UNIT_GIFTED` (Unciv has no unit-gift-to-city-state action), `AllowsAnnexingCityStates`.

### 4.11 Religion [D]
- Pressure scale ×10: `RELIGION_ATHEISM_PRESSURE_PER_POP` 1000, `RELIGION_INITIAL_FOUNDING_CITY_PRESSURE` 5000, and
  `RELIGION_MISSIONARY_PRESSURE_MULTIPLIER` 10. This rescales numbers already in Unciv's religion code. Keep the
  G&K values for G&K rulesets (they are ModConstants).
- The 5th belief (Reformation) is gained from the Piety finisher (`AddReformationBelief`) [D]+[K].
- New belief effects (list in §3.2). Faith purchases are gated by `Belief_BuildingClassFaithPurchase` /
  `Belief_EraFaithUnitPurchase` instead of G&K's `BuildingClassEnabled`.
- Trade routes spread religion (§4.1.5). `SpyPressure` beliefs: a spy in a city exerts pressure [K].
- **Done (B022).** Scale: `ReligionAtheismPressurePerPop` (pop growth), `ReligionInitialFoundingCityPressure` (holy city) and
  `ReligionMissionaryPressureMultiplier` (missionary/prophet strength) are wired; [K, a documented choice] the multiplier also scales the
  pantheon's per-citizen pressure (200), the per-turn city-to-city pressure (speed value, holy city 5x) and spy pressure, and the AI's
  inquisitor threshold, so every pressure stays on one scale (G&K multiplier 1: unchanged). Trade routes [?, documented choice]: each
  active international route adds, every turn and without range limit, the other end's majority (major) religion to a city at the strength of
  a nearby city (it stacks with the range pressure of a partner that is also nearby), times `100 + TradeReligionPressureModifier` (Arabia, +100, by the route owner) percent. Reformation: with
  `AddReformationBelief` (policy Reformation) a founded religion gets one free Reformation belief; `SecondPantheon` (Religious Tolerance) one free
  Pantheon belief (the usual choose-beliefs decision). Beliefs: Messenger of the Gods = `[stats] for each international trade route [cityFilter]`
  (origin city side [K]); Evangelism (`OtherReligionPressureErosion` [?]) cuts the pressure of other religions into the owner's cities by N%;
  Unity of the Prophets (`InquisitorPressureRetention` [?]) lets the religion whose founder has it keep N% of its own pressure when someone's Inquisitor cleans a city;
  Underground Sect: each set-up spy in a city adds N (x scale) pressure per turn for its civ's religion. Faith purchases use the existing
  `May buy ... with Faith` and `Can purchase all Great People with Faith` uniques (tested on the real ruleset). Not done: Heathen Conversion
  (`ConvertsBarbarians`, no rule in the spec), Sacred Sites (its "[+2] Tourism from [Faith] buildings" already uses TourismFromBuildingsFlat, B031 check).

### 4.12 Military, warmonger, AI difficulty [D]
- Warmonger: `WARMONGER_THREAT_*` thresholds and weights lowered, `WARMONGER_ON_CITY_STATE_MULTIPLIER` 50, and
  `Eras.WarmongerPercent` scales the penalty by era (50% Ancient → 100% Modern) [K].
- Promotions: Sapper (cities adjacent to a sapper unit can be attacked at +`SAPPED_CITY_ATTACK_MODIFIER` 50% within
  `SAPPER_BONUS_RANGE` 2), Heavy Charge (Winged Hussar: the defender retreats or takes extra damage [K]), and post-combat random
  promotion (Kris Swordsman `MYSTIC_BLADE`: after its first combat it gets one of 8 promotions at random, exclusive) [D].
  Other unit stat changes are data.
- AI handicap: `HandicapInfos.AIFreeXP / AIFreeXPPercent`.
- `BASE_CITY_AIR_STACKING` 6, `SETTLER_GOLD_MULTIPLIER` 2→3, `RESEARCH_AGREEMENT_BOOST_DIVISOR` 6→3,
  `INFLUENCE_HILL_COST` 2→1, `NUM_AND_TECH_PREREQS` 5→6.
- `UNRESEARCHED_TECH_BONUS_FROM_KILLS_SLOPE/INTERCEPT` (9.68 / −114.07): science from kills for a trait [D, K which].
- Exotic goods (the Portuguese Nau, `NumExoticGoods` 1): sell next to a foreign city for gold in
  `[EXOTIC_GOODS_GOLD_MIN 100, MAX 400]` and XP in `[10, 30]`, scaled by distance from your capital [K].

### 4.13 Civilizations [D]
Uniques per civ (traits in §3.2). The unique units and buildings are in `Civilization_UnitClassOverrides` /
`Civilization_BuildingClassOverrides`. Notes for the engine:
- **Assyria**: steal a tech when conquering a city. **Brazil**: +100% tourism in golden ages, Brazilwood Camp. **Indonesia**:
  the first 3 cities founded on new landmasses get 2 of a unique luxury (Cloves, Pepper or Nutmeg) (`UniqueLuxury*`).
- **Morocco**: gold and culture per trade partner, and per incoming route (Kasbah). **Poland**: a free policy each era.
- **Portugal**: resource diversity ×2 on trade, Feitoria. **Shoshone**: 8-tile territory claim on founding, Pathfinder
  goody-hut choice (`ChooseGoodyHutReward` [U], `GoodyHuts.ChooseDescription`).
- **Venice**: ×2 trade slots, cannot train settlers or annex, free Merchant of Venice at Optics. **Zulu**: −25% XP needed.
- `Civilization_Start_Place_First_Along_Ocean` (only Venice) places that civ first on a coastal start (§4.14).

### 4.14 Map generation
- Antiquity sites are **not** placed at map generation. They spawn when Archaeology is researched (§4.4.2).
- New luxuries (Bison, Cocoa) use the existing resource placement with their `Resource_TerrainBooleans` /
  `Resource_FeatureBooleans` rows [D]. Indonesian luxuries never appear at map generation (`CivilizationType`) [D].
- `Civilization_Start_Place_First_Along_Ocean` → the region/start-placement code places that civ before others on a
  coastal start [K]. Add it as a `Nation` field.
- New natural wonders are already in Unciv. Verify `Natural_Wonder_Placement` against Unciv's natural wonder uniques.

#### 4.14a As built (B024)
- Cloves, Pepper and Nutmeg carry `Doesn't generate naturally` (`GeneratesNaturallyOn` skips them in every placement path, and `LuxuryResourcePlacementLogic.AssignLuxuries`
  keeps unconditional-`NoNaturalGeneration` luxuries out of the random/region luxury pool so they do not waste slots). Bison and Cocoa are placed by the existing region code from their generator-written
  `Generated ...` uniques; the BNW tables (`Resource_TerrainBooleans`, `Resource_FeatureBooleans`) were checked against `terrainsCanBeFoundOn`.
- `Nation.PlaceFirstAlongOcean` (JSON `placeFirstAlongOcean`, only Venice): `MapRegions.AssignRegions` sorts the coast-biased civs with the flagged
  ones first (stable, so rulesets without the flag assign exactly as before). All 12 `Civilization_Start_Along_Ocean` civs already have a `Coast`
  start bias in the generated nations. Venice is the only civ the flag changes.
- Not done: `GameStarter`'s non-region start fallback ordering ignores the flag (region assignment is the normal path).

### 4.15 Victory and score [D]
- Victories: Time, Science (Space Race), Domination, **Cultural** (`Influential`), **Diplomatic** (`DiploVote`).
  Apollo and the spaceship are unchanged. Utopia is gone.
- The culture victory check (§4.3.4) and the diplomatic victory check (§4.7.6) need victory-type JSON
  `milestones` support in Unciv's `Victory` model. B009 added the milestone kinds `Influential over all civilizations` and
  `Win the World Leader vote` (4.3.4a); B063 sets the vote flag.
- Score: +`SCORE_POLICY_MULTIPLIER` (4) per policy, +`SCORE_GREAT_WORK_MULTIPLIER` (4) per Great Work,
  +`SCORE_BELIEF_MULTIPLIER` (20) per belief, +`SCORE_RELIGION_CITIES_MULTIPLIER` (1) per city following your
  religion [D]+[K: same formula shape as existing score].

#### 4.15a As built (B025, spec id B090)
`Civilization.CalculateScoreBreakdown` adds four lines when the ruleset constant is non-zero (BNW values 4, 4, 20, 1; G&K keeps 0, so its six
lines, their map order and its total are untouched): `Policies` = adopted policies (branch openers and tenets count, the `BranchComplete`
bonus does not); `Great Works` = works standing in the civ's own cities; `Beliefs` = beliefs of the civ's religion (a pantheon-only religion counts);
`Religion` = cities of any civ whose majority religion is the civ's (`NumberOfCitiesFollowingThisReligion`). The new lines are integral, but the Cities/Tiles lines are not, so the JavaHashMap order of the keys (deterministic) decides the rounding of the total; the total is the map-order sum, asserted in `BnwScoreTests`. [K]/[?]: whether Civ V counts openers/tenets in "policies" and works only in own cities is unverified
(no decompiler output); the choice above is a deliberate deviation. The BNW population (4) and wonder (25) score constants are emitted by the generator since B029
(`SCORE_POPULATION_MULTIPLIER` / `SCORE_WONDER_MULTIPLIER` comments in `ModConstants.cs`, cross-checked with `Defines.tsv`); G&K keeps 3/40.

### 4.16 AI
The Civ V grand-strategy spec (`../ai-spec/SPEC.md`) already covers the BNW grand strategy columns: culture/tourism
weights and the UN strategy. BNW feature AI needed on top (A-track tickets):
- Trade route choice (§4.1.7) and the trade unit build need (`AICITYSTRATEGY_NEED_INTERNATIONAL_*_TRADE_ROUTE`, @10069ac0).
- Great Work placement and theming (the AI rearranges works, @10076ea0 / @10079770 / @1006d990), swap offers.
- Archaeologist need (§4.4.3), dig choice (landmark vs artifact).
- Ideology choice (`IDEOLOGY_SCORE_*`) and switching (§4.6.5).
- League: proposal choice, vote allocation, vote trading (`AddVoteCommitment`), project contribution (@10250660).
- Concert tour / tourism bombing (`ECONOMICAISTRATEGY_CONCERT_TOUR`, `AI_OPERATION_CONCERT_TOUR`).
- Tactical: plunder trade units, park on trade routes (@101c2db0).

---------------------------------------------------------------------------------------------------

## 5. Client (Godot) inventory [U]
BNW adds or changes these screens (names from `Assets/DLC/Expansion2/UI/InGame/Popups`). Each becomes a C-track ticket
with its own asset source and license (no Firaxis art):
TradeRouteOverview, ChooseInternationalTradeRoutePopup, ChooseTradeUnitNewHome, CultureOverview (Great Works, swap,
influence, public opinion), GreatWorkPopup, ChooseArchaeologyPopup, ChooseIdeologyPopup, the SocialPolicyPopup ideology
pane (tenets by level, public opinion, switch), LeagueOverview (members, delegates, proposals, votes, effects),
LeagueProjectPopup, LeagueSplash, ChooseFaithGreatPerson, ChooseGoodyHutReward, ChooseAdmiralNewPort, the
EspionageOverview diplomat mode, the TopPanel tourism readout, CityView Great Work slots and tourism, plot tooltips for
trade routes (`GetInternationalTradeRoutePlotToolTip`), and DiploVotePopup (UN).

## 6. Testing and compatibility [X]

### 6.1 Golden tests per formula
Every [O] formula in §4 gets an xUnit test with hand-computed cases. Use the worked examples above, plus the boundaries:
a 0 lifetime culture, an influence level exactly at a threshold, a tech difference of 1 (gives 1, since ceil(0.5) = 1),
a negative tech difference (0), a sea vs land route, a city-state destination, a dead civ counting 0.5, cs = 0
(`ln(0) = −inf` → clamped to 0), and uprising counters at fast and slow speeds.

### 6.2 Scenario tests
Headless CLI games with the BNW ruleset (seeded): 300 turns, AI-only, 8 civs. Assert no exceptions, that a league is
founded, that ideologies are adopted, that the save → load → compare checksum equality holds every 25 turns, and that
the command-log replay reproduces the state (lockstep requirement).

### 6.3 Save compatibility
G&K saves are unaffected (all new fields default to empty). BNW saves need `CompatibilityVersion` bumped once (B001: `CurrentCompatibilityNumber` 4 to 5; older saves still load, a save made by this build is refused by older ones).
Renamed objects (Railroads→Railroad, Pharmaceuticals→Penicillin) only matter inside the BNW ruleset, which is new, so
there are no old saves. Name the BNW objects as BNW does.

---------------------------------------------------------------------------------------------------

## 7. Ticket plan (new `B` track) [X]
Prefix `B` = "Brave New World track". B000 adds `"B": "Brave New World track"` to `TRACKS` in `tools/ticket.py`
(policy ticket). Every B ticket depends on the G&K engine pieces it extends being `Done`. Phase: 2 (single player)
unless noted. Each ticket's RE step resolves the [K] and [?] items it touches and updates this spec.

| Id | Title | Depends | Notes |
|----|-------|---------|-------|
| B000 | Add the B track to ticket tooling | — | policy |
| B001 | `BaseRuleset.Civ_V_BNW`, ModOptions gates, `ModConstants` for all §4 defines, `CompatibilityVersion` bump | B000 | no behaviour change for G&K |
| B002 | Ruleset generator `gen_ruleset.py` + `overrides.json` + generated JSON (§3) | B001 | validator clean |
| B003 | New `UniqueType`s for §3.2 (declarations + parameter types + docs only) | B001 | effects come in the feature tickets |
| B010 | Content uniques that need no new subsystem (traits, promotions, beliefs, improvements, policies that only change stats) | B002, B003 | many small ones; split if > 500 lines |
| B020 | Trade routes: model, slots, range, yields, lifecycle, plunder, commands, queries | B003 | RE: duration, plunder gold, religious pressure |
| B021 | Trade units: Caravan, Cargo Ship, rebasing, build restrictions; Exotic goods | B020 | |
| B030 | Great Works: instances, slots, creation, theming, moving, swapping | B003 | RE: work selection, `GetThemingBonus` precedence |
| B031 | Tourism and influence: city tourism, modifiers, influence, levels, trend, blast, level bonuses | B030, B020 | RE: §4.3.5 |
| B032 | Culture victory + Victory milestone kinds | B031 | |
| B040 | Archaeology: historical record writers, site spawning, Archaeologist, dig choices, Landmark | B030 | RE: weights in @100d4080 |
| B041 | Great Writer / Musician / Artist BNW actions, guild specialists, GP faith purchase | B030, B031 | |
| B050 | Policy tree: branch era gates, branch-gated wonders, finishers | B002 | |
| B051 | Ideologies: unlock, adopter bonuses, tenet levels | B050 | |
| B052 | Public opinion, opinion unhappiness, switching, anarchy | B051, B031 | |
| B060 | World Congress: founding, sessions, delegates, proposals, voting | B003, B051 | RE: §4.7.4 flow |
| B061 | Resolutions effects (all 18) | B060 | |
| B062 | League projects + rewards | B060 | |
| B063 | Diplomatic victory (UN) | B060 | |
| B070 | BNW happiness: graded penalties, uprisings, rebels, city revolt | B052 | RE: revolt target |
| B071 | Espionage diplomats, coup levels, influence spy bonus | B031, B060 | |
| B072 | City-state BNW changes, Merchant of Venice purchase, Venice restrictions | B020, B010 | |
| B073 | Religion BNW constants, belief effects, trade-route pressure | B020, B010 | |
| B074 | Warmonger, Sapper, other military data effects | B010 | |
| B080 | Map: Indonesia luxuries, new resources, start along ocean | B002 | |
| B090 | Score changes | B030, B051 | |
| B100 | Headless 300-turn BNW soak + replay/save equality | all above | phase 2 gate |
| A0xx | BNW AI (§4.16), one ticket per bullet | feature ticket | A-track |
| C0xx | BNW screens (§5), one ticket per screen | feature ticket + engine query | C-track, phase 2 |

## 8. Unknowns and risks (to be closed by the ticket noted)
1. Trade route duration formula (B020). 2. Plunder gold (B020). 3. Trade-route religious pressure amount (B073).
4. Great Work selection when a GP has no named work (resolved [X] in B007, §4.2.3). 5. Theming precedence details (resolved [X] in B007, §4.2.4). 6. Influence-level
benefits and their values (B031). 7. Great Firewall math (B031; built as a deliberate choice in B009, 4.3.4a). 8. Archaeology weight constants and random-record
generation (B040). 9. Proposal and voting flow details (B060), and the unused `DIPLO_VICTORY_ALGORITHM*` defines (B063).
10. City revolt target selection (B070). 11. Rebel unit-type choice and city weight (B070). 12. Speed-percent field
identities in domestic routes (B020). 13. Exact public-opinion tie-breaks (B052).
Risk: the scope is large (about 30 engine tickets plus AI and client). Ship in this order to get a playable BNW single
player early: B000–B003, B050–B051, B020–B021, B030–B032, B060–B063, then the rest.

## 9. Function index (BNW engine, for follow-up RE)

| Address | Label |
|---------|-------|
| 1006e410 | GetInfluenceLevel |
| 1006e5d0 | least-influenced civ (victory progress) |
| 1006ea20 | GetTourismModifierWith |
| 1006c4d0 | GetInfluenceTrend |
| 1006c5d0 | public-opinion unhappiness from dissatisfaction |
| 1006e720 / 1006e860 / 1006e8d0 | influence bonuses: trade science / CS spy rank / major spy rank |
| 1006f9d0 | DoPublicOpinion (and tooltip) |
| 1006dec0 | closest Great Work slot |
| 10071c40 | Great Work swap notifications |
| 100720f0 / 10072360 | city tourism before / after modifiers |
| 100724b0 | city tourism tooltip (lists every modifier) |
| 10073c20 / 10073fa0 / 10073ec0 / 10073ff0 | influence per turn / player tourism / turns to influential / tourism blast |
| 10075be0 | culture victory notifications |
| 10078c00 / 10078d10 | GetThemingBonus / tooltip |
| 100d1830 | votes needed for diplomatic victory |
| 100dae20 / 100d4080 | spawn antiquity sites / site plot weight |
| 1003ea70 / 1020fb90 | unhappy production-gold / combat penalties |
| 10150dc0 / 10150e00 | IsEmpireUnhappy / IsEmpireVeryUnhappy |
| 1015c340 / 10154110 / 101541b0 | uprising tick / arm counter / spawn rebels |
| 1016c270 / 101545a0 / 1016a790 | revolt tick / arm / city revolt |
| 10174320 | player doTurn (ideology prompt) |
| 1018d360 | AI ideology switch decision |
| 1018a790 | archaeology choice |
| 101f73c0 | trade unit for domain |
| 101f7330 | international trade routes used |
| 101f7bf0 / 101f7c20 / 101f7dd0 / 101f8c50 / 101f8dd0 / 101f8f00 / 101f9170 / 101f9430 / 101f9680 / 101f7e10 | trade value components (exclusive / policy / trait / base / GPT / resource / your building / their building / river / domain) |
| 101f96f0 | GetTradeConnectionValueTimes100 |
| 101f9e00 / 101fa000 | trade route range / slots |
| 101fa1e0 | AI international route score |
| 101fdfe0 / 101feb70 | trade connection (de)serializers (field layout) |
| 1024e260 | CalculateStartingVotesForMember |
| 10263720 / 10264850 | FoundLeague / league DoTurn |
| 1025a790 / 1024dbe0 | proposal validity reasons |
| 102625e0 / 10262d60 | vote result notifications |
