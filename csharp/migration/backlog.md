# Migration Backlog

Rules: see `PROTOCOL.md`. Tell an agent **"Begin Conversion work"** to start.
Seed tickets were chosen as leaf files (no engine dependencies beyond libGDX math/JVM stdlib). Expand as needed.

## Non-goals (Won't port)

See `PROTOCOL.md`. The target is a headless, 100% deterministic C# engine driving a Godot client. The following subsystems and files from the upstream Kotlin codebase are permanently out of scope and marked **Won't port** (formalized under **T043**):

1. **Multiplayer & Server Stack (`core/src/com/unciv/logic/multiplayer/`, `server/`)**:
   - `ApiVersion.kt`, `FriendList.kt`, `Multiplayer.kt`, `MultiplayerFiles.kt`, `MultiplayerGamePreview.kt`, `OnlineMultiplayerEvents.kt`, `ServerFeatureSet.kt`, `apiv2/`, `chat/`, `storage/`, and all code under `server/`.
   - *Reason:* The project will implement its own multiplayer networking for Godot; there is no dependency on the upstream Unciv custom multiplayer servers, Dropbox sync, or original server infrastructure.

2. **GitHub & Online Mod / Update Integration (`core/src/com/unciv/logic/github/`, `core/src/com/unciv/logic/UncivKtor.kt`)**:
   - `Github.kt`, `GithubAPI.kt`, `Zip.kt`, `UncivKtor.kt`.
   - *Reason:* Online mod downloading from GitHub repositories, release checking, and Ktor HTTP client infrastructure are client/tooling concerns that do not belong in the headless engine.

3. **Platform-Specific File Pickers & Storage Wrappers (`core/src/com/unciv/logic/files/{FileChooser,PlatformSaverLoader}.kt`)**:
   - `FileChooser.kt`, `PlatformSaverLoader.kt`.
   - *Reason:* Native Android/desktop file chooser dialogs and OS-specific storage wrappers. *(Note: Pure engine save/load abstractions in `UncivFiles.kt`, `MapSaver.kt`, `FileConversions.kt`, and `IMediaFinder.kt` remain deferred to the engine save/load ticket and are NOT ruled out).*

4. **Platform & Host Utilities (`core/src/com/unciv/utils/{Display,PlatformSpecific,Concurrency,Log,DebugUtils}.kt`)**:
   - `Display.kt`: libGDX display queries, DPI scaling, and screen sizing (engine reads it only through `GameSettings`, see 9).
   - `PlatformSpecific.kt`: Native platform hooks (Android clipboard, vibration, external browser launch).
   - `Concurrency.kt`: libGDX main/GL thread dispatchers (`launchOnGLThread`, etc.). The engine users (`AlternatingStateManager` `Concurrency.run`, `RulesetCache` `Concurrency.parallelize`) run their work **synchronously and in order**: ruleset loading in the engine is single-threaded and deterministic.
   - `Log.kt`: only the libGDX `Gdx.app.log` backend is Won't-port. The engine **does** get a thin `Log` facade (191 call sites in 42 engine files): see **T047**; do not silently drop log calls in the meantime, leave `// TODO(T047)` at the site.
   - `DebugUtils.kt`: the UI-only flags (`SHOW_TILE_COORDS`, `SHOW_TILE_IMAGE_LOCATIONS`, `SHOW_FPS`) are Won't-port. The flags the game rules read are **not**: `SIMULATE_UNTIL_TURN` (`GameInfo`), `SUPERCHARGED` (`CityStats`), `VISIBLE_MAP` (`CivInfoTransientCache`), `CIV_IDS_IN_EXPERIMENT_GROUP`, `SETTLER_SCORES`/`SHOW_SETTLER_SCORES` (`CityLocationTileRanker`). They become an explicit engine options object, see **T048**.

5. **Translation Authoring Dev Tooling (`core/src/com/unciv/models/translations/TranslationFileWriter.kt`)** and translation rendering glue:
   - `TranslationFileWriter.kt`; `ui/components/fonts/DiacriticSupport.kt` and the libGDX font glyph remapping (fake alphabets for bitmap fonts); the `Gdx.files.internal` based translation file lookup (replaced by `ITranslationFileHandle`, T023).
   - *Reason:* Developer tooling for updating `.properties` files, generating template diffs, and formatting translation sources. The engine only needs headless read-only translation runtime support.

6. **2D Scene2D Skins & Sprite Sheet Models (`core/src/com/unciv/models/skins/`, `core/src/com/unciv/models/tilesets/`)**:
   - `SkinCache.kt`, `SkinConfig.kt`, `SkinStrings.kt`: libGDX Scene2D UI skins and bitmap font configurations.
   - `TileSetCache.kt`, `TileSetConfig.kt`, `TileSet.kt`: 2D hex sprite sheets and libGDX texture atlas packing.
   - *Reason:* Superseded entirely by the Godot 2D/3D client rendering architecture.

7. **libGDX UI and Launchers (`core/src/com/unciv/ui/`, `android/`, `desktop/`)**:
   - Screens, Scene2D actors, popups, stage wrappers, sound/music playback hooks, image loading, and desktop/android launcher harnesses.
   - *Reason:* Client concerns handled exclusively by the Godot client track.
   - **But 106 engine files import `com.unciv.ui.*`.** These types are needed by the engine and are **not** Won't-port; each is ported as engine data, replaced, or cut at the call site by whoever ports the first file that needs it (check this list first, do not redo the analysis):
     - Port as engine data/enums/functions: `ui.screens.victoryscreen.RankingType` (12 uses), `ui.components.MayaCalendar` (4), `ui.components.UnitMovementMemoryType` (3), `ui.audio.UncivSound` (enum field of `Stat`; keep the enum, drop playback), `ui.screens.overviewscreen.EmpireOverviewCategories` (3), `ui.screens.mapeditorscreen.MapGeneratorSteps`, `ui.components.extensions.{toPercent (31), colorFromRGB, isLargerThan, getNeedMoreAmountString}`, `ui.components.fonts.Fonts` stat characters (already in `Stat.cs`).
     - Decide once as an engine text model (the client wants the same text): `ui.screens.civilopediascreen.{FormattedLine (15), ICivilopediaText, CivilopediaCategories}` and `ui.objectdescriptions.*` (`FormattedLineListBuilder`, `BaseUnit/Building/Technology/Nation/Resource/ImprovementDescriptions`, `uniquesToCivilopediaTextLines`). The ruleset object tickets (T030-T033) must say how they handle `getCivilopediaTextLines`/`getShortDescription`.
     - Cut at the call site: `ui.images.ImageGetter`/`Portrait*`/`AtlasPreview` (icon names are strings in the engine), `ui.audio.SoundPlayer`/`MusicTrackChooserFlags`, `ui.popups.*` and `ui.screens.*Screen` references inside notification actions (a notification action becomes data: a type plus parameters), `ui.crashhandling`, `ui.components.widgets.*`, `ui.components.input.KeyboardBinding`.

9. **Client settings and multiplayer residue reachable from the engine**:
   - `models/metadata/GameSettings.kt` (and `GameSettingsMultiplayer`, `ChatWebSocket`/`FriendList` imports) is a client settings model, not ported. Engine code that reads `UncivGame.Current.settings.*` (language for `tr()`, ...) takes explicit parameters instead (`Translations.CurrentLanguage` is the stand-in, T023).
   - `logic.multiplayer.isUsersTurn` (used by `CityConstructions`) is replaced by the command layer's actor/turn validation (COMMANDS.md). `FriendList.Friend` (used by `IdChecker`) lives on as a nested data class in the port.

8. **libGDX UI Unit Tests (`tests/src/com/unciv/ui/`)**:
   - `KeyBindingTests.kt`, `LineChartTests.kt`, `ModFontsTests.kt`, `NativeBitmapFontDataTest.kt`, `EditorMapHolderMemoryTest.kt`, `DevConsoleTests.kt`, `RankingTypeTests.kt`, `WorldMapMovementOverlayTest.kt`.
   - *Reason:* Tests verifying libGDX UI widgets, fonts, key shortcuts, and Scene2D layouts.


## Engine track

### T001 LongPriorityQueue
- State: Done (full ticket: backlog-archive.md)

### T002 Perlin noise
- State: Done (full ticket: backlog-archive.md)

### T003 Vector2 / Vector3 shims
- State: Done (full ticket: backlog-archive.md)

### T004 Stat enum and Stats
- State: Done (full ticket: backlog-archive.md)

### T005 Counter
- State: Done (full ticket: backlog-archive.md)

### T006 HexMath
- State: Done (full ticket: backlog-archive.md)

### T007 JSON layer for ported data classes (Counter, Stats, ...)
- State: Done (full ticket: backlog-archive.md)

### T010 Seeded deterministic RNG
- State: Done (full ticket: backlog-archive.md)

### T016 Kotlin random extensions (IntRange.random, Collection.random, shuffled)
- State: Done (full ticket: backlog-archive.md)

### T011 State-hash determinism harness
- State: Done (full ticket: backlog-archive.md)

### T012 Command layer design
- State: Done (full ticket: backlog-archive.md)

### T017 JVM hash codes and state-based randomness helpers
- State: Done (full ticket: backlog-archive.md)

### T013 Style & analyzer baseline
- State: Done (full ticket: backlog-archive.md)

### T015 HexMath Tile/Map dependent functions
- State: Done
- Depends: T039, T022
- Source: core/src/com/unciv/logic/map/HexMath.kt (`tilesAndNeighborUniqueIndex`, `getDistanceFromEdge`)
- Fixtures: tests/src/com/unciv/logic/map/HexmathTests.kt (`testTileNeighborMappingIsNonConflicting`)
- Deliverables: additions to csharp/Unciv.Core/Logic/Map/HexMath.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~HexMathTests`
- Claimed: 2026-10-24T10:33Z
- Notes: Deferred from T006.
  - Implemented GetDistanceFromEdge and TilesAndNeighborUniqueIndex in HexMath.cs; ported testTileNeighborMappingIsNonConflicting. No Kotlin test for GetDistanceFromEdge: hand-derived cases (hexagonal/rectangular, wrap/no-wrap). Kotlin Int.sign -> Math.Sign; Int division truncates identically. Also ticket.py now inserts a missing '- Claimed:' line (older tickets lacked it, claim failed).

### T018 Utils: collection/string extensions, exceptions, Versioning
- State: Done (full ticket: backlog-archive.md)

### T044 Iteration-order policy for java.util.HashMap / HashSet
- State: Done (full ticket: backlog-archive.md)

### T019 Event bus
- State: Done (full ticket: backlog-archive.md)

### T020 MultiFilter and IdChecker
- State: Done (full ticket: backlog-archive.md)

### T021 Metadata models: LocaleCode, GameParameters, GameSetupInfo, Player, BaseRuleset, ModCategories
- State: Done (full ticket: backlog-archive.md)

### T045 GameSetupInfo
- State: Done (full ticket: backlog-archive.md)

### T047 Log facade
- State: Done (full ticket: backlog-archive.md)

### T048 Engine options replacing the DebugUtils flags the rules read
- State: Done (full ticket: backlog-archive.md)

### T049 Model leaves: ModConstants, UncivSound, TutorialTrigger
- State: Done (full ticket: backlog-archive.md)

### T046 GameInfo core and the hooks waiting for it
- State: Available
- Depends: T034, T039, T045, T055, T060, T065, T078
- Source: core/src/com/unciv/logic/GameInfo.kt (fields, setTransients, queries; turn handling is T071)
- Fixtures: tests/src/com/unciv/logic/SerializationTests.kt GameInfo parts, LoadGameTest.kt (TestGame-based: T072)
- Deliverables: csharp/Unciv.Core/Logic/GameInfo.cs, tests (+ GameInfo implements `IGameContextGameInfo`/`IGameInfoState`; retype the seam-typed GameInfo fields in TileMap/Civilization/City/MapUnit)
- Gate: `dotnet test --filter FullyQualifiedName~GameInfoTests`
- Claimed:
- Notes: SEAM RULE (set by T053, applies to T053-T072): the Tile -> TileMap -> Civilization -> City -> MapUnit -> GameInfo port order is acyclic because each ticket compiles only against classes that are already Done plus small engine-owned interfaces (`partial` extensions of the `IGameContext*` interfaces in `Models/Ruleset/Unique/GameContext.cs` / `ConditionalsSeams.cs`, or a new `I<Thing>` for types with no IGameContext seam, e.g. managers), in a file named `<Area>Seams.cs`, containing exactly the members the ticket calls. A field/parameter whose Kotlin type is a not-yet-ported class is typed by its seam. The ticket that later ports that class implements the seam, then retypes the earlier tickets' seam-typed fields/params to the concrete class (a mechanical edit of those files, listed in its Deliverables), keeping reference-identity Equals/GetHashCode (Kotlin Civilization/City/MapUnit/Tile have none). Do not add public API Kotlin lacks beyond the seams. TestGame-based fixtures are deferred to T035/T072. Top of the graph: GameInfo is ported LAST among the logic classes and uses everything concretely. Waiting on it: `TranslationCache.ActiveMods` should use the running game's `gameParameters.mods + baseRuleset` instead of `Translations.TranslationActiveMods` when a game exists (see `ActiveModsCache` in TranslationCache.kt), and the `GameSetupInfo(gameInfo)` constructor of T045. Scope: fields, serialization shape, setTransients, queries (getCities, getCivilization...), difficulty/speed accessors. Turn handling (`nextTurn`) and BackwardCompatibility moved to T071. 918 LOC: delegate the brief. Replace the `TODO(T046)` markers.
  - Backlog audit: now Depends on T078 (BarbarianManager is a serialized GameInfo field). BarbarianAutomation is T094. Also retype the GameInfo-typed parameter of MapGenerator.generateMap (T041) and GameSetupInfo(gameInfo) (TODO(T046)). Kotlin GameInfo.recordRankingStats calls CivRankingHistory.recordRankingStats(Civilization) (deferred by T051): port it here or in T071. GameStarter is T099, UncivFiles T100.
  - T057 added to the IGameInfoState seam: Religions (JavaHashMap<string, Religion>, Kotlin HashMap order), GetCivilization(civID) returning Civilization, GetCities() returning ICityState, GetAliveMajorCivs(); GameInfo implements them and Religion/ReligionManager/VictoryManager/Milestone are retyped to the concrete GameInfo. GameInfo.setTransients must call religion.setTransients(this) for every religion.
  - From T114 (T059 review): DiplomacyFunctions.GetKnownCivsSorted forwards (includeCityStates, includeDefeated, null, it != civ && civ.Knows(it)) to IGameInfoState.GetCivsSorted; T114 tests it only against a fake that applies the filter, so the real GameInfo.GetCivsSorted needs its own sorting tests.

### T022 Map size and parameters: MapSize, MapParameters, NeighborDirection, RoadStatus
- State: Done (full ticket: backlog-archive.md)

### T023 Translation core (headless)
- State: Done (full ticket: backlog-archive.md)

### T024 Holiday dates
- State: Done (full ticket: backlog-archive.md)

### T025 Unique system: UniqueTarget, UniqueFlag, UniqueParameterType
- State: Done (full ticket: backlog-archive.md)

### T026 Unique system: UniqueType enum
- State: Done (full ticket: backlog-archive.md)

### T027 Unique system: Unique, UniqueMap, LocalUniqueCache, TemporaryUnique, IHasUniques, GameContext
- State: Done
- Depends: T026, T020
- Source: core/src/com/unciv/models/ruleset/unique/Unique.kt, UniqueMap.kt, LocalUniqueCache.kt, TemporaryUnique.kt, IHasUniques.kt, GameContext.kt, UniqueTriggerExecutors.kt
- Fixtures: tests/src/com/unciv/uniques/TimedUniquesTests.kt (where state-independent)
- Deliverables: csharp/Unciv.Core/Models/Ruleset/Unique/*.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~UniqueTests`
- Claimed: 2026-10-24T03:21:29Z
- Notes: UniqueMap must preserve insertion order. GameContext depends on Civilization/City/Tile: use state interfaces, owned by this ticket, implemented later. `stateBasedRandom` formula comes from T017. UniqueTriggerActivation (1.5k LOC) is deferred to T052, which lands with Civ/City/Unit. Have `Unique` implement `IUniqueTargetView` and `IHasUniques` implement `IRulesetObjectParameterView` (from T025).
  - Implementer notes (Claude):
    - Deliverables (all `Models/Ruleset/Unique/`): `Unique.cs`, `UniqueMap.cs`, `LocalUniqueCache.cs`, `TemporaryUnique.cs`, `IHasUniques.cs`, `GameContext.cs`, `UniquePendingTickets.cs`; plus `Logic/Battle/CombatAction.cs` (the 3-value enum from the bottom of `BattleDamage.kt`, the battle ticket keeps it) and `KotlinTrim`/`KotlinTrimEnd` in `Utils/StringExtensions.cs`. Tests: `UniqueTests`, `GameContextUniqueTests`, `UniqueMapUniqueTests`, `LocalUniqueCacheUniqueTests`, `IHasUniquesUniqueTests`, `TimedUniqueTests`, fakes in `UniqueTestFakes.cs`, goldens in `Golden/{Unique,GameContext,UniqueMap}.golden.tsv`.
    - **State interfaces** (as the ticket asked): `GameContext` reads `Civilization`, `City`, `MapUnit`, `Tile`, `Region`, `ICombatant`, `GameInfo` only through `IGameContext{Civilization,City,Unit,Tile,Combatant,Region,GameInfo}`; `Unique.GetGameProgressModifier` through `IGameProgressCivilization`; `LocalUniqueCache` through `ILocalUniqueCache{Civilization,City}`; `IHasUniques.IsHiddenFromCivilopedia` through `IRulesetFeatureView` (Ruleset implements it, T034). The classes that own those types implement the interfaces when ported; nothing in T027 changes. `IGameContextCombatant.Unit`/`.City` are non-null exactly for `MapUnitCombatant`/`CityCombatant` (Kotlin's `is` tests).
    - **Seams for other tickets** in `UniquePendingTickets.cs`: `Conditionals.ConditionalApplies` and `Countables.GetCountableAmount` (throw `NotImplementedException`, T028 deletes these two classes and adds the real ones with the same names/signatures), `DeprecatedUniqueTypes.Lookup` + `DeprecatedUniqueTypeEntry(Text, Flags, ReplaceWith)` (returns null, T029 fills it from its table). A unique with a real conditional therefore throws in `ConditionalsApply` until T028; uniques with no modifiers, timed uniques and `ignoreConditionals` contexts work now.
    - **Deviations:** `timeThis` profiling dropped. `GameContext.IgnoreConditionals` (static) is `GameContext.IgnoreConditionalsState` (the instance property has the same PascalCase name); the public `GameContext` constructor derives an omitted *or null* `gameInfo` from the other members (C# cannot tell a default argument from an explicit null), but the factories (`ForCity/ForUnit/ForCiv/ForCombatants`) and `Copy` pass `gameInfo` explicitly through a private constructor, so a null there stays null like Kotlin. `TemporaryUnique.unique` is `UniqueText` (a member cannot share the name of the type `Unique`; JSON name unchanged). `Unique.getReplacementText(ruleset)` takes a `Func<Unique,bool> hasNoErrors` instead of a `Ruleset` (TODO(T036): `UniqueValidator`). `getDeprecationAnnotation()` returns the deprecated-type entry (no `UniqueType` entry is `@Deprecated`). `toFloat()` follows `Float.parseFloat` (ASCII whitespace trimmed, `f/F/d/D` suffix accepted) and `toInt()` follows `Integer.parseInt` (strict); an earlier note here claimed both were strict, which was wrong (found by the adversarial review). `UniqueMap.getUniques/getTagUniques` return live views of the lists like Kotlin's `asSequence()`.
    - **Orders reproduced:** `UniqueMap.tagUniqueMap` is a `JavaHashMap` (T044), so `getAllUniques()` follows `java.util.HashMap` bucket order; `typedUniqueMap` is a `SortedDictionary<UniqueType,...>` = `EnumMap` ordinal order (matters for `getTriggeredUniques`); lists keep insertion order and duplicates. The typed map shares the tag map's list instance, as in Kotlin.
    - **Quirks kept:** `GameContext` equality is memberwise (a data class), so `IgnoreMultiplicationForCaching` equals `IgnoreConditionals` and every `ignoreConditionals`-only context gets multiplier 1. `getUniqueMultiplier` multiplies the (possibly 0) multipliers and clamps at 0 only at the end. `hasUnique` excludes timed uniques but `hasMatchingUnique`/`hasMatchingTagUnique` do not. `List<TemporaryUnique>.endTurn` decrements `turnsLeft == 0` to `-1` (never expires) and only removes uniques that reach 0 from 1. `GameContext.GetHashCode` relies on `HexCoord.GetHashCode` (31x+y), `JavaHash` for strings/floats/bool/long.
    - **Verification (goldens from the real Kotlin, Unciv 4.22.13 jar, `java` single-file harnesses in `dotnet-dev`):** 2009 uniques (645 uniques of the bundled rulesets that load as plain JSON, the `UniqueType` texts not among them (564, so every type text is covered), and 800 variants of the first 400 with 1-2 modifiers appended): placeholderText, params, type, modifiers (+types), isTimedTriggerable, isTriggerable, allParams, isLocalEffect, isOtherModifierType, hasFlag x4, isHiddenToUsers, hasTriggerConditional, isModifiedBy*, getDisplayText, toString, stats bits. `GameContext.hashCode()` and the first ints of `stateBasedRandom("caller")`/`("Chance", 7)` for 19 scenarios built from real `Civilization`/`Tile`/`MapUnit`/`MapUnitCombatant`/`GameInfo` objects (NaN movement, unplaced units, negative turns, empty game id...), and the data-class equality of the companion states. A `UniqueMap` filled from 1220 uniques: `getAllUniques` order, `getUniques`/`getMatchingUniques`/`hasUnique`/`hasMatchingUnique` for every type, tag lookups, `getTriggeredUniques` for 5 triggers, after `removeUnique` and `clear`. The harness sources are not committed (throwaway, like T026).
    - **Not covered by Kotlin comparison:** `LocalUniqueCache`, `IHasUniques` defaults, `TemporaryUnique`, the game-progress arithmetic, the adjacent-tile multiplier and `GameContext`'s relevant*/resource/stat members are covered by hand-written tests derived from reading the Kotlin (no Kotlin objects exist for them yet).
    - **Deferred (not ported, with owner):** `IHasUniques.requiredTechnologies/era/techColumn/availableInEra` (T032: Technology, Era, TechColumn) and `getWeightForAiDecision` (T033: BaseUnit, Building; T032: Personality); `isHiddenFromCivilopedia(ruleset)` overload (reads `UncivGame.getGameInfoOrNull()`, a client global: callers use the `gameInfo` overload); `UniqueTriggerExecutors.triggerGlobalAlerts` (needs `Civilization.addNotification`, ports with `UniqueTriggerActivation`, T052); `TimedUniquesTests.testConditionalTimedUniqueStrength`/`...Expires` (TestGame + policy adoption + BattleDamage + TurnManager: T035 and later). `testConditionalTimedUniqueIsTriggerable` is ported.
    - Ran Gate (`~UniqueTests`, 47/47) and Standard checks (build -warnaserror 0 warnings, format clean, 748/748).
  - Self-review follow-up (Claude, independent adversarial subagent, verdict APPROVED, no blocking findings). Fixed: (1) `GameContext.Copy(...)` added with Kotlin data-class `copy` semantics (stored `gameInfo` is kept, never re-derived; `CopyArg<T>` distinguishes "keep" from "set to null"; callers: `Nuke.kt:205`, `QuestManager.kt:218`); (2) the `gameInfo = null` deviation narrowed: factories and `Copy` use a private all-members constructor so an explicit null stays null; (3) the `toFloat()` note and `Unique` doc comment corrected (see Deviations); (4) the three lazy members publish through an immutable box with `Volatile`/`Interlocked` (Kotlin `lazy` is synchronized); (5) `int.MinValue / -1` in `ForEveryAmountCountable` wraps like the JVM instead of throwing; (6) `LocalUniqueCache.Cache` property added (Kotlin `val cache`); (7) tests added: `DeprecatedUniqueSeamTests` reaches `DeprecatedType`, `HasFlag` and the sign/repeated-parameter/OR-alternative paths of `GetReplacementText` through `DeprecatedUniqueTypes.UniqueTypeMap` (now `internal`, the only seam; hand-derived from the Kotlin, not JVM goldens, T029 still owes the golden), `GameContext` Copy/explicit-null/Region-hash/City-combatant-hash/thread-stability tests, and the `IsUnavailableBySettings`/`IsHiddenFromCivilopedia(gameInfo)` no-conditional path. Not fixed, with owner: tests for `ForEveryCountable`/`ForEveryAmountCountable` arithmetic and `IsUnavailableBySettings` with real game-based conditionals need `Conditionals`/`Countables` (T028). Re-ran Standard checks.
  - Review (Adversarial Review subagent, single-agent mode, Auditor/Adversary/Pragmatist): Pass. Re-ran Standard checks (build -warnaserror 0 warnings, format clean, 773/773) and the Gate (`~UniqueTests` 53/53); no suppressions or policy-file changes in the diff. Verified all eight self-review fixes against the Kotlin. Non-blocking, left as is: `Copy` cannot clear `combatAction` (documented; no Kotlin caller does) and `CopyArg` is heavier than the two real call sites need (kept for data-class fidelity), `TemporaryUnique` throws `ArgumentOutOfRangeException` where Kotlin throws `NoSuchElementException` (caller bug in both), `DeprecatedUniqueTypes.UniqueTypeMap` is an `internal` mutable test seam that T029 must turn back into a read-only table.

### T028 Unique system: Conditionals and Countables
- State: Done
- Depends: T027
- Source: core/src/com/unciv/models/ruleset/unique/Conditionals.kt, Countables.kt, expressions/{Expressions,Node,Operator,Parser,Tokenizer}.kt (the expression parser behind `ExpressionTests`)
- Fixtures: tests/src/com/unciv/uniques/CountableTests.kt, tests/src/com/unciv/uniques/ExpressionTests.kt
- Deliverables: csharp/Unciv.Core/Models/Ruleset/Unique/Conditionals.cs, Countables.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~CountableTests|FullyQualifiedName~ExpressionTests`
- Claimed: 2026-10-24T03:57Z
- Notes: Fixtures that need TestGame are deferred to T035; port the rest now and list the deferred cases in Notes. Expression evaluation: Double vs Int semantics exact. Wire `Countables` into `UniqueParameterTypeExtensions.CountablesProvider` (or replace `TODO(T028)` in `UniqueParameterType.cs`). **From T027:** delete the stub classes `Conditionals` and `Countables` in `Unique/UniquePendingTickets.cs` and add the real ones (same names and signatures: `Conditionals.ConditionalApplies(Unique?, Unique, GameContext)`, `Countables.GetCountableAmount(string, GameContext) : int?`). `Unique.ConditionalsApply`/`GetUniqueMultiplier` already call them; add tests for a real conditional and for `ForEveryCountable`/`ForEveryAmountCountable`. `GameContext` is a class with state interfaces (`IGameContext*`), use its `Relevant*` members and `StateBasedRandom`. **Also from T027's review:** (a) rewrite the two tests that pin the stub (`UniqueTests.ConditionalsApplyShortCircuits`, `LocalUniqueCacheUniqueTests.ExplicitGameContext...` assert `Assert.Throws<NotImplementedException>`); (b) add tests for the `?? 0`, `multiplier / perEvery` and multiplicative stacking of `ForEveryCountable`/`ForEveryAmountCountable` (incl. `perEvery = -1` with a countable of `int.MinValue`, which wraps like the JVM; `perEvery = 0` throws on both sides, with different exception types); (c) test `IHasUniques.IsUnavailableBySettings` with real game-based conditionals (`Unavailable`/`OnlyAvailable`). | **Implementation (Claude):** Done as Conditionals.cs, Countables.cs, Expressions/{Operator,Node,Tokenizer,Parser,Expressions}.cs, ConditionalsSeams.cs, Validation/ModCompatibility.cs (only `ModNameFilter`), Logic/Civilization/Managers/ReligionState.cs. Stubs removed from UniquePendingTickets.cs; `CountablesProvider` now defaults to `CountablesParameterProvider` (Countables). Tests: ExpressionTests, CountableTests, ConditionalsTests, UniqueMultiplierTests (+ the two stub-pinning tests rewritten, `IsUnavailableBySettings` real-conditional test added). Gate 32/32; full suite 834/834; build -warnaserror and format clean; no suppressions. **Design:** the game classes (GameInfo, Civilization, City, MapUnit, Tile, Region, Ruleset, Religion, Speed) are not ported, so every member Conditionals/Countables read is a seam: `partial` extensions of the `IGameContext*` interfaces in ConditionalsSeams.cs (plus `IGameContextRuleset : IRulesetParameterView`, `IGameContextSpeed`, `IGameContextReligion`); T046+ implement them, nothing here changes. Tests use data-driven fakes in UniqueTestFakes.cs. `Countables` is a sealed class with the entries as static instances holding the Kotlin overrides as delegates (C# has no enum-with-bodies); the Kotlin reflection check became `Overrides*` flags. Companion statics are `GetAllKnownValuesForAutocomplete` / `GetErrorSeverityOfText` (C# cannot overload static and instance). **Verified against the JVM (java 25 + kotlin-stdlib 2.3.0, throwaway harness not committed):** `Double.roundToInt` (13 inputs incl. +-0.5, 2.5, -2.5, 0.49999999999999994, saturation), `Math.pow` NaN/+-1^inf/0^-1 cases, `maxOrNull/minOrNull` with NaN and +-0.0 (Kotlin uses Math.max/min semantics: 0.0 beats -0.0, NaN wins; first guess of `max < e` was wrong), `Double.toString` forms (JavaDouble). **Hand-derived from Kotlin source (not run):** all parser/AST texts and exception positions, every conditional case, every countable eval, ForEvery arithmetic. **Semantic traps kept:** `Float.MIN_VALUE` is `float.Epsilon` (not `float.MinValue`) for the below-amount lower limit; float*float stays float; `Int` sums wrap (own `SumOf`, LINQ `Sum` would throw); `%` by 0 throws DivideByZeroException (JVM ArithmeticException); `relevantCiv!!` in compareEra is a NullReferenceException; tokenizer and ToToken are lazy iterators so parse errors surface in Kotlin's order; countable token position is the closing bracket; `Operator.Of` keeps the overwrite order (UnaryOrBinary Minus wins); FilteredBuildings cache is keyed by filter only and never invalidated (Kotlin quirk, kept). **Deviations:** (1) `ConditionalChance` hashes the Unique by text+source type+source name because Kotlin `Unique` has no `hashCode` (JVM identity hash, different every run, so Kotlin's result is not reproducible); (2) `UncivGame.Current.settings` (tutorial conditionals) is the static `Conditionals.Settings` seam, default "tutorials off, nothing completed"; (3) `timeThis` dropped; (4) autocomplete cache is a ConditionalWeakTable; (5) added `KotlinMath.RoundToInt(double)`; (6) `ModCompatibility` holds only `ModNameFilter` (rest belongs to the Ruleset tickets). **Deferred (need TestGame/Ruleset/RulesetValidator, T035/T036/T037 owners):** CountableTests.testRulesetValidation, testForEveryWithInvalidCountable, testPerCountableForGlobalAndLocalResources, testStatOrResourcePerTurn (real city stats), testStatsCountables (real happiness), testKnownCivsCountable, testOwnedTilesCountable, testFilteredCitiesCountable, testFilteredBuildingsCountable (real stats), testPoliciesCountables, testEraNumberCountable, testSpeedModifierCountable, countableCanUseContext (the unit/resource parts), and testAllCountableAutocompleteValuesMatch over the real Vanilla ruleset (here: empty and fake rulesets). ExpressionTests.testExpressionsWithCountables is ported over fakes. T052 (UniqueTriggerActivation) is still blocked on the Civilization tickets. | **Review (Adversarial Review subagent, single-agent mode, Auditor/Adversary/Pragmatist): Pass.** Standard checks re-run (build -warnaserror 0 warnings, format clean, 834/834). Compared Conditionals.kt/.cs in full and Countables, Operator, Node, Tokenizer, Parser, Expressions side by side; Kotlin tests: every CountableTests/ExpressionTests case is ported or deferred (deferred ones are collected under T035, which carries them). The ConditionalChance deviation is genuine (Kotlin `Unique` has no hashCode, so the JVM identity hash varies per run) and the text+source hash is deterministic. JVM claims re-verified with kotlin-stdlib 2.4.20 (the version the project uses; implementer used 2.3.0): maxOrNull/minOrNull with NaN and +-0.0, roundToInt edge cases, Math.pow(1,NaN)/(-1,inf) all match the C#. Stubs deleted, the two stub-pinning tests rewritten, T027 follow-ups (a)(b)(c) done, no suppressions. Non-blocking nits: (1) `Math.Pow` vs `StrictMath`/intrinsic `Math.pow` can differ by 1 ulp for non-trivial operands (invisible after roundToInt except at exact .5 boundaries); (2) `Turns % ToInt(P(0))` with Turns = int.MinValue and divisor -1 would throw OverflowException where the JVM gives 0 (unreachable in practice); (3) Equals() on Civilization/Unit in ConditionalFirstCivTo*/AdjacentUnit must be reference identity when T046+ implements the seams (Kotlin Civilization/MapUnit have no equals).

### T029 Deprecated uniques and BackwardCompatibility
- State: Done
- Depends: T027
- Source: core/src/com/unciv/models/ruleset/unique/DeprecatedUniques.kt, logic/BackwardCompatibility.kt
- Fixtures: tests/src/com/unciv/uniques/DeprecatedUniquesTest.kt
- Deliverables: csharp/Unciv.Core/Models/Ruleset/Unique/DeprecatedUniques.cs, Logic/BackwardCompatibility.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~DeprecatedUniquesTests`
- Claimed:
- Notes: BackwardCompatibility touches GameInfo/Civ/City; port only parts whose types exist, record the rest as TODO(T###) with a real ticket. **From T027:** fill `DeprecatedUniqueTypes.Lookup` (stub in `Unique/UniquePendingTickets.cs`, returns `DeprecatedUniqueTypeEntry(Text, Flags, ReplaceWith)`) from your `DeprecatedUniqueType` table. `Unique.GetReplacementText` is ported but untested against Kotlin because no deprecated entry resolves yet: add a golden over every `DeprecatedUniqueType` entry (replacement text for unique texts with signed/unsigned and repeated parameters). `DeprecatedUniqueTypes.UniqueTypeMap` is currently an `internal` mutable `ConcurrentDictionary` that `DeprecatedUniqueSeamTests` registers entries in: make it a read-only table again and move those tests onto real entries. | **Implementation (Claude):** `Unique/DeprecatedUniques.cs` (enum `DeprecatedUniqueType`, 210 entries in Kotlin order, + `DeprecatedUniqueTypeEntry` + `DeprecatedUniqueTypes.{Entries,Get,Lookup}`, generated from the Kotlin source by a throwaway script and counted against it; targets/docDescription dropped as unused at runtime). `UniquePendingTickets.cs` deleted (it only held the stub); the table is read-only again. `DeprecatedUniqueSeamTests` replaced by `DeprecatedUniquesTests` (9 tests, Gate 9/9, full suite 836/836, build -warnaserror and format clean, no suppressions). Ported fixture tests 1 and 2 (chains; half-deprecated hop N/A as no UniqueType is deprecated). **Golden over every entry:** `Golden/DeprecatedUniques.golden.tsv` (600 rows = 200 single-alternative entries x unsigned/`-`/`+` parameters `10*(i+1)`) produced by the real Kotlin `Unique.getReplacementText(vanilla)` through a throwaway gradle test (not committed); the C# matches all 600. The 10 multi-alternative (`" OR "`) entries depend on `UniqueValidator` and have only a hand-derived test (`StrengthPlusVs`). **Deferred:** fixture tests 3, 5, 6, 7 (UniqueValidator/UniqueAutoUpdater/TestGame) are appended to T036's Notes; `BackwardCompatibility.kt` has no portable part (every function is an extension of GameInfo/GameInfoPreview/ModOptions/TechManager, none ported): no `BackwardCompatibility.cs` created, the port is appended to T046's Notes. | **Review (Adversarial Review subagent, single-agent mode): Pass.** Mechanical 210-entry comparison vs Kotlin, Standard checks and Gate re-run (836/836, 9/9), no suppressions.

### T030 Ruleset base objects: RulesetObject, Specialist, Speed, Policy, PolicyBranch, Belief, Event, Quest, RuinReward, GlobalUniques, ModOptions, Victory, Tutorial
- State: Done
- Depends: T027, T021, T049
- Source: core/src/com/unciv/models/ruleset/{RulesetObject,Specialist,Speed,Policy,PolicyBranch,Belief,Event,Quest,RuinReward,GlobalUniques,ModOptions,Victory,Tutorial,RulesetName}.kt
- Fixtures: tests/src/com/unciv/models/ruleset/VictoryAvailabilityTests.kt, VictoryMilestoneTests.kt (state-dependent parts after T035)
- Deliverables: csharp/Unciv.Core/Models/Ruleset/*.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~RulesetObjectTests`
- Claimed: 2026-10-24T04:30Z
- Notes: Json field names/defaults must match Kotlin (rulesets load from JSON). Victory.kt is 454 LOC; split off if it exceeds scope and note it. **From T027:** `Unique.GetSourceNameForUser` has a private copy of `GlobalUniques.getUniqueSourceDescription` (marked TODO(T030)); move it to `GlobalUniques` and call that. `RulesetObject` implements `IHasUniques`; its default interface methods are callable only through an `IHasUniques`-typed reference, so use `((IHasUniques)this)` inside the class or re-expose the ones Kotlin callers use (see `IHasUniquesUniqueTests.TestObject` for the minimal shape: cached `UniqueObjects`/`UniqueMap` via `UniqueObjectsProvider()`/`UniqueMapProvider()`, `Name` with a setter, `Uniques` as `List<string>`). **Implemented (Claude):** ported the data model, JSON defaults and the logic that needs no game state. Deferred (need Ruleset/T034, Civilization/GameInfo/T046, T052, or the Civilopedia text types, which have no ticket yet): `ICivilopediaText` (civilopediaText, getCivilopediaTextLines/Header, getSortGroup, getSubCategory, makeLink-as-interface), `Specialist.colorObject` (T050 EngineColor), `Event/EventChoice.triggerChoice` (T052), `QuestName` -> `IQuestImplementation` delegation, `Belief.getBeliefsMatching/getCivilopedia*Entry`, `Milestone.hasBeenCompletedBy/getFocus/getVictoryScreen*/getMoreCountable*/getCountableDone/getFormattedLine`, `RulesetName` extension helpers, `GlobalUniques.civilopediaText` combining. Fixtures VictoryAvailabilityTests/VictoryMilestoneTests need TestGame (T035) and are not ported; tests in `RulesetObjectTests` cover defaults, JSON reading and the pure logic. Traps: (1) `Stats` is `IEnumerable`, so System.Text.Json treated any subclass (NamedStats/Specialist/RulesetStatsObject, later Building etc.) as a collection; added `StatsSubclassConverterFactory` (Json/, read-only, flat object with stat keys plus own properties). (2) C# cannot call `super` on a default interface method: `IHasUniques.IsUnavailableBySettings` body moved to static `IsUnavailableBySettingsDefault`; `RulesetObject`/`RulesetStatsObject` expose a virtual that `RuinReward` overrides. (3) Kotlin `val` fields (RuinReward.weight, Victory.milestones...) are set by json, so they are settable properties. (4) `Speed` per-stat modifiers default to `modifier` at construction only (a json `modifier` does not propagate). (5) `Unique.GetSourceNameForUser` now calls `GlobalUniques.GetUniqueSourceDescription`; `GameParameters` uses `Speed.Default`. (6) Enums renamed to avoid member clashes: `Event.PresentationType`, `Victory.FocusType`, `Policy.PolicyBranchTypes`; `BeliefType.color/isFollower/isFounder` and `MilestoneType.text`, `QuestName.value` are extension methods. Gate and Standard checks pass (882 tests).

### T031 Ruleset tile objects: Terrain, TerrainType, TileImprovement, TileResource, ResourceType, ResourceSupplyList
- State: Done
- Depends: T030
- Source: core/src/com/unciv/models/ruleset/tile/*.kt
- Fixtures: none - generate golden values; tests/src/com/unciv/logic/map/TileImprovementConstructionTests.kt later with T038
- Deliverables: csharp/Unciv.Core/Models/Ruleset/Tile/*.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~RulesetTileTests`
- Claimed: 2026-10-24T04:39Z
- Notes: Ordered collections stay ordered (ResourceSupplyList). **Implemented (Claude):** ported ResourceType, TerrainType, Terrain, TileResource, TileImprovement, ResourceSupplyList in Unciv.Models.Ruleset.Tile; tests in RulesetTileTests (22). No Kotlin tests exist: expectations derived by hand from the Kotlin source (no golden run). Deferred (need Ruleset/T034, Civilization/MapUnit/Tile/T046-T048, engine colour T050, or Civilopedia text types): Terrain.displayAs/getColor/civilopedia members, TileResource.getImprovements/isImprovedBy/setTransients(ruleset)/getImprovingImprovement/generatesNaturallyOn, TileImprovement.getTurnsToBuild/getConstructorUnits/getCreatingUnits/getDescription/civilopedia members, ResourceSupplyList.subtractResourceRequirements, ResourceType.getColor (use GetColorString). Traps: (1) ResourceSupplyList is a wrapper (not an ArrayList subclass) so Add keeps merge semantics; it returns true when a merged entry cancels to 0 and is removed (as Kotlin); ResourceSupply is a class with Kotlin data-class equality and internal Amount setter; ApplyModifiers uses Kotlin float->int truncation (NaN -> 0). (2) Terrain.RGB needs [JsonPropertyName("RGB")] only for clarity (json is case-insensitive). (3) TileResource.ImprovementStats is nullable like Kotlin. (4) enum `.name` filters use ToString(); ResourceType/TerrainType keep Kotlin ordinals. (5) IHasUniques default methods are called through ((IHasUniques)this). Gate and Standard checks pass (904 tests).

### T032 Ruleset nation and tech objects: Nation, CityStateType, Difficulty, Personality(Value), Era, TechColumn, Technology
- State: Done
- Depends: T030
- Source: core/src/com/unciv/models/ruleset/nation/*.kt, models/ruleset/tech/*.kt
- Fixtures: tests/src/com/unciv/logic/map/CityStateTypeStartBiasTests.kt (if type-only)
- Deliverables: csharp/Unciv.Core/Models/Ruleset/Nation/*.cs, Tech/*.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~RulesetNationTechTests`
- Claimed: 2026-10-24T04:54Z
- Notes: Colors: engine-owned color type, no libGDX Color. **From T027:** `IHasUniques` still lacks `requiredTechnologies`, `era`, `techColumn`, `availableInEra` (need Technology/Era/TechColumn) and the Personality half of `getWeightForAiDecision`; add them to `IHasUniques.cs` (see its class remarks). | Implemented: all Nation/Tech types as data+pure logic. Deferred to T034 (need Ruleset/Civilopedia types/GameInfo): makeLink, getSortGroup/getSubCategory, civilopedia text, Nation.getStartBias, Era.getStartingUnits/getEraGatedObjects/matchesFilter, Technology.matchesFilter/getDescription, Difficulty.getSortGroup. Fixture CityStateTypeStartBiasTests needs RulesetCache: only its unique-map assertion ported. IHasUniques: added IRulesetTechView (stand-in for Ruleset) + RequiredTechnologies/Era/TechColumn/AvailableInEra (maxByOrNull = first max kept); getWeightForAiDecision still TODO(T033). Deviations: Technology.Prerequisites is LinkedHashSet (deterministic order, Kotlin HashSet); Era.IconRGB public (private in Kotlin, STJ needs public); Nation.CHARCOAL=0x111111, PURPLE=a020f0 built locally (EngineColor untouched); PersonalityValue.get(stat) -> PersonalityValueExtensions.FromStat; Personality.ToString = NationDescriptions.getShortDescription. Player.cs TODO retargeted to T034.

### T033 Ruleset unit and construction objects: BaseUnit, BaseUnitCost, Promotion, UnitType, UnitNameGroup, Building, IConstruction, PerpetualConstruction
- State: Done
- Depends: T031, T032
- Source: core/src/com/unciv/models/ruleset/unit/*.kt, models/ruleset/Building.kt, IConstruction.kt, PerpetualConstruction.kt
- Fixtures: tests/src/com/unciv/uniques/UnitUniquesTests.kt (state-dependent parts after T035)
- Deliverables: csharp/Unciv.Core/Models/Ruleset/Unit/*.cs, Building.cs, IConstruction.cs, PerpetualConstruction.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~RulesetUnitTests`
- Claimed: 2026-10-24T04:59Z
- Notes: Cost/production math: Int vs Float and roundToInt (half-up helper) are the main traps. Methods needing City/Civilization go behind state interfaces from T027. **From T027:** `IHasUniques.getWeightForAiDecision` (the `is BaseUnit`/`is Building` part with `matchesFilter(unique.params[1], gameContext)`) is not ported; add it with BaseUnit/Building. `IRulesetUnitParameterView.HasGreatPersonUnique` must be overridden to `HasUnique(UniqueType.GreatPerson)` (Kotlin goes through the unique map, conditionals against `EmptyState`), as must `IRulesetTerrainParameterView.HasRegionUnique` (T025 note). **Implemented by Claude (T033).** Seams (new): `IRulesetConstructionView` (what BaseUnit/UnitType/UnitNameGroup/Building read from Ruleset; T034 implements it), `IConstructionCivilization`/`IConstructionCity`/`IConstructionGameInfo` (cost and buy-cost math; City/Civilization implement them in T046+), `IGameContextRuleset.GetEra` added (Building.isHiddenByStartingEra). **Ported:** all data/JSON fields, IConstruction/INonPerpetualConstruction (purchase reasons, gold cost, getBaseBuyCost, stockpile/required resources), RejectionReason(+Type) data, PerpetualConstruction names/map, BaseUnit (ruleset-unique overlay via explicit IHasUniques overrides, filters with the key-only filter cache quirk, evaluateForce with Float.pow via double, upgrade units), BaseUnitCost, Building (stats, percent bonuses, production/buy costs, filters, isStatRelated), UnitType, Promotion.getBaseNameAndLevel, UnitNameGroup.getUnits. Also T027/T025 notes: BaseUnit implements `IRulesetUnitParameterView.HasGreatPersonUnique` and Terrain `IRulesetTerrainParameterView` (HasRegionUnique via unique map), TileResource `IRulesetResourceParameterView`. **Deferred (comments in files):** everything needing CityConstructions/MapUnit/Tile (getRejectionReasons, notMetRejections, isBuildable, shouldBeDisplayed, construct, newMapUnit, addConstructionBonuses, isPurchasable, PerpetualConstruction.isBuildable/tooltips) and descriptions/Civilopedia (T034/T046). `BaseUnit.CanBePurchasedWithStat(city)` throws NotSupportedException (its Kotlin reads getRejectionReasons). Traps: Kotlin `Float.pow` = Math.pow on doubles (1.45f/0.3f exponents widened exactly); toInt truncation; BaseUnit/Building `cachedMatchesFilterResult` is keyed by filter alone (state ignored), kept; UnitType.movementType made public (json); Building.percentStatBonus made public (json). Fixture UnitUniquesTests needs TestGame (T035); tests are hand-derived in RulesetUnitTests.cs. Test fakes FakeGameInfo/FakeCiv/FakeCity unsealed to subclass. **Review (Adversarial, pass after fixes):** fixed BaseUnit.Promotions to JavaHashSet (JVM hash order, custom json converter), non-exception-caching lazies for Type/IsLandUnit/IsWaterUnit, HasTagUniqueIgnoringConditionals override; tests added. Accepted low items: Promotion color objects not cached, PerpetualConstructionsMap built via ToDictionary. **Fixes re-reviewed independently (Adversarial subagent): pass; 1026/1026 tests, build/format clean; non-blocking: isGreatPerson/isMilitary still System.Lazy.**

### T034 Ruleset and RulesetCache loader
- State: Done
- Depends: T031, T032, T033
- Source: core/src/com/unciv/models/ruleset/Ruleset.kt, RulesetCache.kt
- Fixtures: tests/src/com/unciv/uniques/ResourceTests.kt (static parts)
- Deliverables: csharp/Unciv.Core/Models/Ruleset/Ruleset.cs, RulesetCache.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~RulesetLoadTests`
- Claimed: 2026-10-24T05:16Z
- Notes: File access behind an interface (no libGDX FileHandle). Load order of files/mods must be deterministic and equal to Kotlin's. Implements the interfaces left by T025 (`IRulesetParameterView` via `RulesetMapView<T>`) and T027. **Implemented (Claude):** Ruleset, RulesetFile (the enum; `RulesetFile.Values` = `entries`), RulesetCache (static; JavaHashMap so Values order = Kotlin HashMap), RulesetFiles.cs (`IRulesetFolder`/`IRulesetFileSource` + Directory implementations, replacing FileHandle/Gdx.files; console mode = no global jsons folder), Utils/LinkedHashMap (ordered map for the ruleset maps), Json/LenientJson (quotes libGDX-style unquoted keys, needed by the shipped global Tutorials.json). Ruleset implements IRulesetParameterView (explicit, via covariant adapter over the ordered maps), IRulesetConstructionView, IRulesetFeatureView, IRulesetTechView, IGameContextRuleset. Also ported what the ticket needed: TileResource.GetImprovements/IsImprovedBy/SetTransients, Era.MatchesFilter, Technology.MatchesFilter, RoadStatus.Improvement, RulesetName helpers (ToRulesetName/RulesetName). **Deviations:** loading is sequential, mods in ordinal folder-name order (Kotlin: parallel, listFiles order); the load-time println dropped (no wall clock). Era.matchesSingleFilter has no UncivGame.Current.gameInfo fallback (false without a state). Fallback CityStateTypes filtering by UniqueValidator.checkUnique is the hook `Ruleset.IsUniqueValid` (default accepts all) - TODO(T036): install the validator. Not ported: getErrorList, checkCombinedModLinks (T036), allICivilopediaText/Civilopedia text types. ModOptions.updateDeprecations is empty upstream. Kotlin quirks kept: Policies.json loads before VictoryTypes.json (branch priorities only know earlier victories); removing a policy branch leaves the branch's own entry in `policies`; a policy at the same row/column as the branch (0,0 default) evicts it. No Kotlin unit test exists for the loader (ResourceTests needs TestGame, T035): tests derived by hand + a smoke load/combine of the shipped Vanilla and G&K rulesets from android/assets/jsons (RulesetLoadTests, 46 tests; they use [Collection("RulesetCache")] since the cache is static). Gate + Standard checks pass (1072 tests).

### T035 Test-support port: TestGame helper and deferred fixtures
- State: Available
- Depends: T034, T038, T039, T046, T053, T054, T057, T061, T065
- Source: tests/src/com/unciv/testing/TestGame.kt
- Fixtures: tests/src/com/unciv/testing/TestGame.kt; deferred cases from T028, T030, T033
- Deliverables: csharp/Unciv.Core.Tests/Support/TestGame.cs, ported deferred tests
- Gate: `dotnet test --filter FullyQualifiedName~TestGameTests`
- Claimed:
- Notes: Previously Blocked on the missing Civilization/City/MapUnit tickets (Claude, 2026-10-24: TestGame.kt constructs GameInfo, TileMap, Tile, Civilization, City, MapUnit, CityFounder, Religion; a stub would invent API). Those now exist as T053-T071; this ticket ports TestGame over the real classes and nothing else. The per-area TestGame-based fixtures are T072. **From T027:** port `TimedUniquesTests.testConditionalTimedUniqueStrength` and `...Expires` (only `...IsTriggerable` is ported, in `TimedUniqueTests`). Also the deferred T028/T030/T033 cases that only need TestGame.

### T036 RulesetValidator
- State: Done
- Depends: T034
- Source: core/src/com/unciv/models/ruleset/validation/*.kt
- Fixtures: tests/src/com/unciv/uniques/RulesetValidatorTests.kt, tests/src/com/unciv/uniques/UniqueErrorTests.kt, tests/src/com/unciv/uniques/AllObjectsTests.kt
- Deliverables: csharp/Unciv.Core/Models/Ruleset/Validation/*.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~RulesetValidatorTests`
- Claimed: 2026-10-24T05:30Z
- Notes: Error ordering and text must be identical to Kotlin. TextSimilarity: exact algorithm port. `Suppression.IsValidFilter` and doc constants are already ported on `UniqueParameterTypeExtensions` in T025; reuse or delegate to them in `Suppression`. **From T027:** `Unique.GetReplacementText(Func<Unique,bool> hasNoErrors)` takes the validator as a delegate (TODO(T036)); pass `u => new UniqueValidator(ruleset).CheckUnique(u, null).Count == 0` or change the signature to take the ruleset. **From T029:** port `UniqueAutoUpdater` and the deferred `DeprecatedUniquesTest` cases 3, 5, 6, 7 (validator reports `ErrorOptionsOnly` for a `DeprecatedUniqueType` unique; auto-updater replacement, two-hop chain `DecreasedBuildingMaintenanceDeprecated` -> `BuildingMaintenanceOld` -> `BuildingMaintenance`, mixed ruleset; TestGame from T035). Pass the validator in as `Unique.GetReplacementText(hasNoErrors)` and add the 10 multi-alternative entries to the golden by running Kotlin with a real ruleset. **Implemented (Claude):** ported RulesetErrorList (+RulesetError, RulesetErrorSeverity with Color/IconName, UniqueParameterErrorSeverity.GetRulesetErrorSeverity), TextSimilarity, Suppression (delegates to the T025 members; `autoSuppressAllWarnings` debug tool not ported), UniqueValidator, UniqueAutoUpdater, ModCompatibility (full), RulesetValidator + BaseRulesetValidator, `Ruleset.GetErrorList` (extension `RulesetValidatorExtensions.GetErrorList`), `RulesetCache.CheckCombinedModLinks`, `Unique.GetReplacementText(Ruleset)` (extension in `UniqueValidatorExtensions`; the delegate overload stays), and `Ruleset.IsUniqueValid` now defaults to the real validator. Added `Message` to `DeprecatedUniqueTypeEntry` (extracted from the Kotlin annotations; all 210 are DeprecationLevel.ERROR so they report ErrorOptionsOnly). Deferred (UI/graphics-bound, no engine equivalent; they only add image/tileset lines): AtlasPreview texture checks (`initTextureNamesCache`, `uncachedImageExists`), `checkTilesetSanity` (TileSetConfig, TileSetCache, Gdx.files), `checkCivilopediaText` and the unit-set-art civilopediaText check (ICivilopediaText has no ticket). Deviations: IRulesetFolder is read-only so `UniqueAutoUpdater.AutoupdateUniques` returns the new file texts instead of writing them; messages printing a FileHandle path print the folder name; the map-preselection check reads `ruleset.FolderLocation` for the mod folder; `MapUnitCache.UnitMovementUniques` and the four `CityStatePersonality` names are local copies (those types need MapUnit/Civilization). Traps: (1) the event circular-trigger message joins a java.util.HashSet, so it uses `JavaHashSet`, and the auto-update replacement map is a `JavaHashMap` (iteration order feeds the text replace); (2) the `ModConstant 'x}'` message keeps the stray `}` of the Kotlin template; (3) `Float` in "Negative year interval" is formatted Kotlin-style for plain values only (no exponent form); (4) `RulesetErrorList` is a wrapper, not an ArrayList subclass, so there is no unchecked Add. Tests: RulesetValidatorTests (18, all Kotlin cases), UniqueErrorTests (8; `testEducatedEliteGreatPersonGifting` needs a game, not ported), AllObjectsTests (2; the allICivilopediaText case is not ported), DeprecatedUniquesValidatorTests (cases 3, 5, 6, 7 and a 30-line golden `DeprecatedUniquesValidated.golden.tsv` for the 10 multi-alternative entries produced by running the Kotlin `getReplacementText(vanilla)` through a throwaway gradle test, not committed), RulesetValidatorUnitTests (hand-derived). `TestGame` (T035) is replaced by a small `ValidatorTestRuleset` helper (G&K clone + createXxx) in the tests; T035 can swap it. Both shipped base rulesets validate with zero lines. Gate and Standard checks pass (1144 tests).


### T037 Golden test: load and validate Vanilla and bundled mod rulesets
- State: Available
- Depends: T036, T035
- Source: none (integration test)
- Fixtures: tests/src/com/unciv/logic/LoadGameTest.kt (ruleset portion); ModConstantsTests moved to T049
- Deliverables: csharp/Unciv.Core.Tests/Models/Ruleset/VanillaRulesetTests.cs
- Gate: `dotnet test --filter FullyQualifiedName~VanillaRulesetTests`
- Claimed:
- Notes: Asset JSON is read from the Kotlin repo's android/assets/jsons (read-only). Compare object counts/ids/uniques against Kotlin-generated dump (record how generated).

### T038 Tile core (part 1): TileHistory
- State: Done
- Depends: T022, T034
- Source: core/src/com/unciv/logic/map/tile/*.kt
- Fixtures: none (TileHistory has no Kotlin tests; ported tests assert the documented behaviour and JSON format)
- Deliverables: csharp/Unciv.Core/Logic/Map/Tile/*.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~TileTests`
- Claimed: 2026-10-24T05:45Z
- Notes: Tile.kt is 1.3k LOC; split off Tile stats/improvements if needed. Civ/City/Unit references go behind interfaces from T027. Kotlin lazy/cached fields must not change observable behaviour. Use namespace `Unciv.Logic.Map.Tiles` (matching `RoadStatus.cs` in T022) so the namespace does not collide with the `Tile` class name.
  **Claude 2026-10-24 (scope reduced):** Tile.kt, TileNormalizer, TileStatFunctions and TileImprovementFunctions use TileMap, GameInfo, Civilization, City, MapUnit, UnitMovement and MapGenerator, none of which exist in C# (same blocker as T035); stubbing them would invent API. Moved to T053 (Blocked). Delivered here: `TileHistory.cs` (TileHistory, TileHistoryState, CityCenterType, JSON converter `{"12":["China","R"]}`, null owner written as null) plus `ITileHistorySource` seam (Turns, OwnerCivID, IsCityCenter, IsCityCapital) that Tile must implement. Quirks kept: TileHistoryState has no equals (reference equality), so TileHistory.Equals needs identical state instances; Clone shares state instances; GetState is floorEntry on a SortedList. Tests in `Unciv.Core.Tests/Logic/Map/TileTests.cs`. Standard checks pass.

### T053 Tile core (part 2a): Tile
- State: Done
- Depends: T038
- Source: core/src/com/unciv/logic/map/tile/Tile.kt
- Fixtures: tests/src/com/unciv/logic/map/FreshWaterTests.kt, TileImprovementConstructionTests.kt (both need TestGame: deferred to T072; port only what runs without it)
- Deliverables: csharp/Unciv.Core/Logic/Map/Tile/Tile.cs, TileSeams.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~TileTests`
- Claimed: 2026-10-24T10:01Z
- Notes: SEAM RULE (set by T053, applies to T053-T072): the Tile -> TileMap -> Civilization -> City -> MapUnit -> GameInfo port order is acyclic because each ticket compiles only against classes that are already Done plus small engine-owned interfaces (`partial` extensions of the `IGameContext*` interfaces in `Models/Ruleset/Unique/GameContext.cs` / `ConditionalsSeams.cs`, or a new `I<Thing>` for types with no IGameContext seam, e.g. managers), in a file named `<Area>Seams.cs`, containing exactly the members the ticket calls. A field/parameter whose Kotlin type is a not-yet-ported class is typed by its seam. The ticket that later ports that class implements the seam, then retypes the earlier tickets' seam-typed fields/params to the concrete class (a mechanical edit of those files, listed in its Deliverables), keeping reference-identity Equals/GetHashCode (Kotlin Civilization/City/MapUnit/Tile have none). Do not add public API Kotlin lacks beyond the seams. TestGame-based fixtures are deferred to T035/T072. Split from T038. Tile is the BOTTOM of the cycle: it is ported first, against seams only. Defines `ITileMapState` (what Tile reads from TileMap: get/getIfTileExistsOrNull, neighbours/tilesAtDistance, ruleset, mapParameters, isWaterMap/bounds, starting locations, gameInfo as `IGameContextGameInfo`), and extends `IGameContextCivilization` (owner, `isCityState`, `isCurrentPlayer`, known tiles, hasUnique...), `IGameContextCity` (tile's owning city, city centre) and `IGameContextUnit` (`militaryUnit/civilianUnit/airUnits`, `isCivilian`, movement-type checks) with exactly what Tile.kt calls. T039 (TileMap) implements `ITileMapState` and retypes `Tile.tileMap`; T055/T060/T065 retype owner/city/unit members. Tile implements `IGameContextTile` and `ITileHistorySource` (T038); namespace `Unciv.Logic.Map.Tiles`. Tile.kt is 1.3k LOC: delegate the read-and-brief step. Kotlin lazy/cached fields must not change observable behaviour. Deferred, with owner: members that call TileStatFunctions/TileImprovementFunctions/TileNormalizer delegate to T054 (leave the delegating members for T054 to add if they would need not-yet-ported classes, and say so here); `UniqueTriggerActivation` calls to T052; UnitMovement calls (`canPassThrough` etc.) to T067. IMPLEMENTED (T053): Tile.cs + TileSeams.cs in Logic/Map/Tile (namespace Unciv.Logic.Map.Tiles). Seams: `ITileMapState` + `TerrainListData` (T039 moves it into TileMap) and partial extensions of IGameContextGameInfo (GetCivilization, GetModConstants, EngineOptions), IGameContextCivilization (PlayerType, IsSpectator, IsTileViewable, GetLastSeenImprovement, Knows, IsAtWarWith, IsConsideredFriendlyTerritoryOf, CanSeeResource, CanPassThroughTiles, ForestsAndJunglesAreRoads, NeutralRoads, CheckExploredRegionTilePosition, MatchesFilter(.., multiFilter)), IGameContextCity (Name, Location, IsWorked, IsTileLocked, HasJustBeenConquered, IsCapital, ReassignPopulationDeferred), IGameContextUnit (Clone, SetCurrentTile, Owner, AssignOwner, SetTransients, IsEmbarked, CacheState). Because the partial interfaces gained members, the test fakes in UniqueTestFakes.cs are now `partial` (FakeUnit unsealed) with throwing virtual stubs in UniqueTestFakes.TileSeams.cs. Tile fields typed by seam: tileMap (ITileMapState), owningCity (IGameContextCity), military/civilian/air units (IGameContextUnit), roadOwnerObject; getOwner/getRoadOwner return IGameContextCivilization. Deviations: (1) DebugUtils.VISIBLE_MAP is read from `civ.GameInfo.EngineOptions.VisibleMap` (T043 EngineOptions is per-game). (2) setTileResource(TileResource, majorDeposit, rng): Kotlin default `Random.Default` is nondeterministic, so rng is `GameRandom?` and null with a null majorDeposit throws. (3) getRulesetIncompatibility returns a LinkedHashSet (insertion order) instead of a JVM HashSet. (4) Kotlin `Exception` -> InvalidOperationException (messages kept). (5) GUI.clearUndoCheckpoints (setExplored) dropped, UI concern. (6) TerrainHasUnique/GetMatchingUniques take `GameContext? = null` meaning `stateThisTile` (a field cannot be a default argument). (7) Neighbors and isAdjacentToCoast are cached on first use even if the tile map was not yet set (same as Kotlin lazy). (8) Serialization: Tile has no custom JSON converter yet; it is wired when GameInfo serialization is ported (T046+). DEFERRED TO T054 (needs TileImprovementFunctions/TileStatFunctions/TileNormalizer/TileImprovement.getTurnsToBuild/UnitMovement, none ported): fields `improvementFunctions` and `stats`; removeImprovement, setImprovement (x2), setBaseTerrain, doWorkerTurn, setPillaged (+clearAllPathfindingCaches), startWorkingOnImprovement, queueImprovement(TileImprovement, civ, unit), the `convertTerrains` parameter of setConnectedByRiver (MapGenerator.Helpers.convertTerrains). T054 edits Tile.cs to add these. Fixtures FreshWaterTests/TileImprovementConstructionTests need TestGame (T072); TileTests.Tile.cs tests Tile against a fake ITileMapState. Gate and Standard checks pass (1185 tests).

### T039 TileMap
- State: Done
- Depends: T038, T053
- Source: core/src/com/unciv/logic/map/TileMap.kt
- Fixtures: tests/src/com/unciv/logic/map/VisibilityTests.kt, UpgradeTests.kt (TestGame-based: map-only parts now, the rest in T072)
- Deliverables: csharp/Unciv.Core/Logic/Map/TileMap.cs, tests (+ retype `Tile.tileMap` to TileMap in T053 files)
- Gate: `dotnet test --filter FullyQualifiedName~TileMapTests`
- Claimed: 2026-10-24T10:13Z
- Notes: SEAM RULE (set by T053, applies to T053-T072): the Tile -> TileMap -> Civilization -> City -> MapUnit -> GameInfo port order is acyclic because each ticket compiles only against classes that are already Done plus small engine-owned interfaces (`partial` extensions of the `IGameContext*` interfaces in `Models/Ruleset/Unique/GameContext.cs` / `ConditionalsSeams.cs`, or a new `I<Thing>` for types with no IGameContext seam, e.g. managers), in a file named `<Area>Seams.cs`, containing exactly the members the ticket calls. A field/parameter whose Kotlin type is a not-yet-ported class is typed by its seam. The ticket that later ports that class implements the seam, then retypes the earlier tickets' seam-typed fields/params to the concrete class (a mechanical edit of those files, listed in its Deliverables), keeping reference-identity Equals/GetHashCode (Kotlin Civilization/City/MapUnit/Tile have none). Do not add public API Kotlin lacks beyond the seams. TestGame-based fixtures are deferred to T035/T072. Uses concrete Tile (T053), implements `ITileMapState`. TileMap talks to GameInfo/Civilization/MapUnit only through seams (`IGameContextGameInfo`, `IGameContextCivilization`, `IGameContextUnit`; unit placement/removal and `setTransients` civ hooks go through the seam members it calls). Tile iteration order is game state: keep Kotlin's ordering. Completing it unblocks T015, T040, T041, T042 and T055.
  IMPLEMENTED (Claude): TileMap.cs (+ TileMapSeams.cs, Math/Rectangle.cs); Tile.TileMap retyped to TileMap, ITileMapState removed, TerrainListData moved into TileMap, tile unique cache is TileMap.TileUniqueMapCache (ConcurrentDictionary with a by-content List<string> comparer). Tile tests now use a real TileMap. VisibilityTests ported in full (hand-built ruleset, no TestGame); UpgradeTests need civs/units: deferred to T072. Deviations: (1) getInitializationTerrain (T041) inlined privately; (2) assignContinents flood-fills privately instead of BFS (T040 may switch; only set/size are used); (3) switchPlayersNation not ported (unreachable in Kotlin, needs Civilization ctor): TODO(T055); (4) placeUnitNearTile/stripPlayer go through new seam members in TileMapSeams.cs (GetBaseUnit, NewMapUnit, RemoveUnit, ForEachMatchingUnique, UpdateStatsForNextTurn, UpdateCivResources; unit: BaseUnit, CanMoveOnWater, CanPassThrough, CanMoveTo, SetOriginalOwner, SetCacheState, PutInTile, RemoveFromTile, GetMaxMovement, SetCurrentMovement, AddMovementMemory, AddPromotion, HasResourceRequirementsPerTurn): T055/T060/T067 implement them, no tests of placeUnitNearTile yet (needs units, T072); (5) continentSizes is a JavaHashMap (iteration order feeds continentsSortedBySize ties); getRulesetIncompatibility returns JavaHashSet; naturalWonders/resources are LinkedHashSet like Kotlin toSet(); startingLocationsByNation stays Dictionary<string,HashSet<Tile>> (Tile identity-hash in Kotlin too); (6) StartingLocation.Usage enum is UsageType (property named Usage); JSON attributes not added yet (like Tile). Quirks kept: getTilesAtDistance(<=0) throws eagerly if origin is off-map; get() of a null matrix cell throws InvalidOperationException (Kotlin NPE); getViewableTiles minOf throws on empty. Standard checks pass.
  Review notes (adversarial review PASS, non-blocking): startingLocationsByNation outer map is insertion-ordered Dictionary while Kotlin HashMap<String> iterates in string-hash order (GameStarter reads keys/values): use JavaHashMap in the GameStarter ticket; placeUnitNearTile/stripPlayer untested until T072; T040 may replace the flood-fill with BFS.

### T040 Pathfinding: BFS, AStar, MapPathing, CarrierSlotMatcher, MapVisualization
- State: Done
- Depends: T039
- Source: core/src/com/unciv/logic/map/BFS.kt, AStar.kt, MapPathing.kt, CarrierSlotMatcher.kt, MapVisualization.kt
- Fixtures: tests/src/com/unciv/logic/map/PathfindingTests.kt, tests/src/com/unciv/logic/map/pathingmap/*
- Deliverables: csharp/Unciv.Core/Logic/Map/*.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~PathfindingTests`
- Claimed: 2026-10-24T10:25Z
- Notes: Priority-queue tie-breaking must match Kotlin exactly (uses T001). CarrierSlotMatcher and MapPathing need MapUnit/UnitMovement: compile them against `IGameContextUnit`/`IMapUnitState` seam members (see the SEAM RULE in T053); T067 replaces them with concrete UnitMovement calls. PathfindingTests that need TestGame are in T072.
  - IMPLEMENTED (T040): BFS.cs, AStar.cs, MapPathing.cs, CarrierSlotMatcher.cs, MapVisualization.cs, MapPathingSeams.cs in Logic/Map (namespace Unciv.Logic.Map). Seams added: IGameContextGameInfo.TileMap; IGameContextCivilization.HasExplored(tile), CanBuildImprovementOn(tile, improvement) (= tile.improvementFunctions.canBuildImprovement(improvement, civ.state); T054's TileImprovementFunctions or T055 implements it); IGameContextUnit.MovementMemoryPositions, IsVisibleTo(civ). Civilization/MapUnit are typed by IGameContextCivilization/IGameContextUnit (T055/T067 retype; MatchesFilter(filter) already existed). Deviations: (1) java.util.PriorityQueue is reproduced by an internal binary heap (AStar.JavaPriorityQueue, same siftUp/siftDown/poll) with Float.compareTo semantics (-0f<0f, NaN greatest); tie order verified against JDK output in tests. (2) The private unused getPath(unit,...) overload of MapPathing is not ported (no Kotlin caller; T067 adds it if needed). (3) getConnection default cost/heuristic are null defaults resolved inside. (4) getReachedTiles returns a live read-only IReadOnlyCollection of the key set. (5) Log.debug calls dropped. (6) TileMap.assignContinents still uses its private flood fill (BFS only fed a set/count; not switched). (7) CarrierSlotMatcher.SlotRule is a record struct; unitToSlot a Dictionary (only keyed/value-searched, values unique). PathfindingTests.kt, PathingMapTest.kt, RouteNodeTest.kt all run on TestGame/real units (and pathingmap classes are not in this ticket): deferred to T067/T072. PathfindingTests.cs holds hand-derived tests for BFS/AStar/MapPathing/CarrierSlotMatcher/MapVisualization. FakeUnit.MatchesFilter made virtual; stubs in UniqueTestFakes.MapPathingSeams.cs. Gate and Standard checks pass (1260 tests).

### T041 MapGenerator pipeline (audit existing partial work first)
- State: Done
- Depends: T039, T010, T016, T017, T073, T074, T075, T076, T077
- Source: core/src/com/unciv/logic/map/mapgenerator/MapGenerator.kt (rest of pipeline; sub-tickets cover other files)
- Fixtures: tests/src/com/unciv/logic/map/MapGenerationTests.kt, ResourceDiversityTest.kt, mapgenerator/NaturalWonderGeneratorTests.kt
- Deliverables: csharp/Unciv.Core/Logic/Map/MapGenerator/*.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~MapGenerat`
- Claimed: 2026-10-24T10:52Z
- Notes: Large (mapregions, resourceplacement, rivers, elevation, landmass). Split into sub-tickets at claim time if >1.5k LOC. Same seed must reproduce Kotlin maps: add golden maps generated from Kotlin.
  - Split at claim: sub-tickets T073-T077 cover all other files (~4.2k LOC). T041 itself = MapGenerator.kt (817 LOC) orchestration + golden Kotlin maps for same-seed reproduction; becomes ready when subs are Done.
  - Reset (split into sub-tickets; re-dispatch after subs Done); was 'In-Progress [Claude]'.
  - From T074 review: delete csharp/Unciv.Core/Logic/Map/MapGenerator/NaturalWonderSeams.cs by adding the real Tile.SetConnectedByRiver(other, newValue, convertTerrains) overload (MapGenerator.Helpers.convertTerrains) and test that RiverGenerator.ContinueRiverOn converts terrain.
  - Backlog audit: files to delete/absorb when this lands: MapGeneratorSeams.cs (TerrainOccursRange, GetTileRadius), NaturalWonderSeams.cs (SetConnectedByRiver convertTerrains, TODO(T041)), the private GetInitializationTerrain copy in TileMap.cs (use MapLandmassGenerator's). MapRegionsSeams.cs belongs to T076/T077 (they own the five NotSupportedException stubs); the generateMap gameInfo parameter is retyped by T046. GameStarter (T099) depends on this ticket.
  - Backlog audit: T076 and T077 are Done but Mapregions/MapRegionResourcesSeams.cs still throws NotSupportedException ('ported by T077') in TryAddingResourceToTiles / PlaceResourcesInTiles, which StartNormalizer calls: the full pipeline (MapGenerator -> MapRegions -> StartNormalizer) would crash. This ticket must replace both bodies with calls to the real MapRegionResources class (delete the seam file) and its same-seed golden-map test must run a ruleset with city-state/major starts so StartNormalizer's resource placement executes.
  - Implemented MapGenerator.cs (Logic/Map/MapGenerator/): generateMap, generateSingleStep, mirror, resolveAutoMapSize, convertTerrains + Helpers, lakes/coasts, ancient ruins, fallback resource spreading, humidity/temperature, vegetation, rare features, ice (incl. flat-earth walls), nested TerrainOccursRange (with the (Terrain, Unique) ctor, lower limits nudged by Float.ulp), GetTileRadius.
    - Seams removed: MapGeneratorSeams.cs, NaturalWonderSeams.cs, Mapregions/MapRegionResourcesSeams.cs (StartNormalizer now calls MapRegionResources directly; no throwing seam is left, MapRegionsSeams.cs only delegates to real classes). TileMap's private GetInitializationTerrain copy deleted (uses MapLandmassGenerator's). Tile.SetConnectedByRiver gained the real convertTerrains parameter (tested). Added Nation.GetStartBias and made Civilization implement IMapRegionsCivilization (needed by the full pipeline).
    - Deviations: CoroutineScope -> CancellationToken; seed 0 (Kotlin: currentTimeMillis) throws unless a currentTimeMillis delegate is passed (no wall clock in the engine); gameInfo is the IGameInfoState seam (T046 retypes); lake flood-fill takes water tiles in map order (Kotlin HashSet<Tile> identity order is arbitrary); Kotlin Double.sign kept NaN-safe; consoleTimings/debug output stripped.
    - Golden values: generated by running the REAL Kotlin MapGenerator. Throw-away JUnit test tests/src/com/unciv/logic/map/GoldenDumpTmp.kt (not committed) run with `./gradlew --offline :tests:test --tests '*GoldenDumpTmp*'`, vanilla ruleset (RulesetCache.getVanillaRuleset() = Civ_V_Vanilla), MapSize.Tiny, map editor mode (no GameInfo), 5 type/shape/wrap combos x seeds 1,7 + fourway/leftright mirroring + Empty; one line per tile in tileList order. Kotlin run twice: byte-identical for these. Stored in Unciv.Core.Tests/Logic/Map/MapGenerator/Golden/MapGenerator.golden.tsv; all 13 match in C#. Per-step (generateSingleStep chain) dumps were also compared during development: all 12 steps identical.
    - The full pipeline with civilizations (MapRegions/StartNormalizer/MinorCivPlacer) is NOT golden: Kotlin uses unseeded Random there and two Kotlin runs of the same seed differ. C# test instead runs 3 majors + 3 city states end to end (no NotSupportedException, 6 starting locations, resources and a natural wonder placed) and checks determinism; testSizeForAuto is ported (average start distance 13 +/-1.25).
    - Tests ported: MapGenerationTests.kt (all types x shapes x wraps, 3 terrain-count tests, auto size). ResourceDiversityTest is @Ignore("Slow") in Kotlin and not ported.
  - Review notes (adversarial review PASS, non-blocking): river-step HashSet could be LinkedHashSet and the header comment about Kotlin set order is inaccurate (mutableSetOf is insertion ordered); TopBottom/AroundCenterTile mirroring untested; HumidFrom exact value not asserted; `_ = gameParameters` in ResolveAutoMapSize.

### T042 OdMap import/export
- State: Done
- Depends: T039
- Source: core/src/com/unciv/logic/map/OdMapExport.kt, OdMapImport.kt, OdTerrain.kt
- Fixtures: tests/src/com/unciv/logic/OdMapExportTests.kt, tests/src/com/unciv/logic/OdMapImportTests.kt
- Deliverables: csharp/Unciv.Core/Logic/Map/OdMap*.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~OdMap`
- Claimed: 2026-10-24T10:24Z
- Notes: Platform file IO behind an interface.
  - Seam: file IO behind IOdMapFile (OpenRead/OpenWrite) + OdMapPathFile; Kotlin FileHandle replaced. libGDX Pixmap/PixmapIO replaced by internal OdMapPng (RGBA8 encode, decode of all non-interlaced PNG types; Adam7 rejected with UncivShowableException). Exported PNG/zip bytes differ from libGDX output but decode to identical pixels. OdTerrain enum is a sealed class with Entries. provinces.json read via System.Text.Json + LenientJson (stands in for libGDX JsonReader). Tests: 4 import + 5 export Kotlin cases ported against the real GnK ruleset, plus extras (corrupt png, missing png, is_sea, JSON well-formedness, PNG codec). Metadata JSON string in Kotlin (raw-string quirk) yields valid JSON with map_date key; reproduced.
  - Review round 1 fixes: lenient JSON accessors now follow libGDX defaults (null/object/array -> default; asBoolean string equalsIgnoreCase true, number != 0); test added.

### T043 Record Won't-port scope
- State: Done (full ticket: backlog-archive.md)
  - Backlog audit (2026-10-24): deferred items named in #3/#7 now have tickets: engine save/load = T098 (FileConversions, MapSaver, IMediaFinder) and T100 (UncivFiles); Civilopedia/FormattedLine text model = T095-T097; Views (core/src/com/unciv/view) = T084/T085; Battle = T081-T083; Automation = T087-T094; UnitActions = T079/T080; TradeLogic/Evaluation = T086/T089; GameStarter = T099; Simulation = T101; end-to-end determinism = T102. Additional Won't-port decisions: T105. The 'Planned (not yet ticketed)' paragraph was removed.

### T050 Engine UI-package leaf types and EngineColor
- State: Done
- Depends: T007, T021, T022, T023
- Source: core/src/com/unciv/ui/screens/victoryscreen/RankingType.kt, core/src/com/unciv/ui/components/MayaCalendar.kt, core/src/com/unciv/ui/components/MapArrowType.kt (`UnitMovementMemoryType`), core/src/com/unciv/ui/screens/overviewscreen/EmpireOverviewCategories.kt, core/src/com/unciv/ui/screens/mapeditorscreen/MapGeneratorSteps.kt, core/src/com/unciv/ui/components/extensions/FormattingExtensions.kt, core/src/com/unciv/ui/components/extensions/Scene2dExtensions.kt (`colorFromRGB`), core/src/com/unciv/ui/components/extensions/TimeExtensions.kt (`isLargerThan`), core/src/com/unciv/models/ModConstants.kt (`ImmutableColor`)
- Fixtures: none - generate golden values by running the Kotlin classes on the JVM
- Deliverables: csharp/Unciv.Core/UI/RankingType.cs, MayaCalendar.cs, UnitMovementMemoryType.cs, EmpireOverviewCategories.cs, MapGeneratorSteps.cs, FormattingExtensions.cs, EngineColor.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~UiLeafTypesTests`
- Claimed: 2026-10-24T04:43Z
- Notes: Implements the UI-package leaf types carved out for the engine in T043 (#7) and the engine-owned color type (`EngineColor` / `ImmutableColor` / `ColorFromRGB`) needed by `Specialist` (T030), `Terrain` (T031), `Nation`/`CityStateType`/`Era` (T032), `Promotion` (T033), and `ModConstants` (T049). Strip libGDX `Image`/`Actor`/`Popup`/`BaseScreen` UI methods; keep `MayaCalendar.startTurnForMaya` deferred (`TODO` on Civilization). **Implemented (Claude):** files in Unciv.Core/UI/ (+ColorExtensions.cs for ColorFromRGB). Namespaces mirror Kotlin packages; `EngineColor`/`ImmutableColor` live in `Unciv.Models`, `ColorFromRGB` in `Unciv.UI.Components.Extensions`. EngineColor is a mutable class with libGDX clamp semantics and int-bits equality; only Set/Add/Sub/Mul(float)/Lerp/Cpy/ToIntBits/ToString ported (ImmutableColor throws NotSupportedException on those); constants White/Black/DarkGray/Firebrick are ImmutableColor instances. Kotlin enums with behaviour became plain enums + extension methods (`RankingType.Label()/IdForSerialization()/IsVanilla()/IconName()`, `EmpireOverviewCategories.IconName()/ShortcutChar()/ScrollAlign()` (libGDX Align ints), `MapGeneratorSteps.Label()/CopyParameters()`); createTab/showDisabled/testState/getPersistDataClass/getImage stripped. Only `UnitMovementMemoryType` ported from MapArrowType.kt (enums cannot implement the marker interface; MiscArrowTypes/TintedMapArrow are drawing concerns). MayaCalendar: `startTurnForMaya`/`isNewCycle`/`openPopup` deferred; `YearToBaktuns` exposed for the later isNewCycle (TODO on Civilization). FormattingExtensions: Duration->TimeSpan, Instant->DateTimeOffset, Date->DateTime; `UncivDateFormat.ParseDate` ignores the quoted 'Z' (SimpleDateFormat quirk) and yields wall time. Golden Maya values came from an independent Python re-implementation of the Kotlin arithmetic (JVM run not done; the arithmetic is pure Int). Traps: `Tr` on `[n] Unit` strips brackets in FormatShort only; Kotlin `%` and `/` truncate like C#. Gate and Standard checks pass (928 tests).

### T051 Serialization leaf models: LastSeenImprovement, CivRankingHistory, Notification
- State: Done
- Depends: T006, T007, T050
- Source: core/src/com/unciv/json/LastSeenImprovement.kt, core/src/com/unciv/logic/civilization/CivRankingHistory.kt, core/src/com/unciv/logic/civilization/Notification.kt, core/src/com/unciv/logic/civilization/NotificationActions.kt, core/src/com/unciv/logic/civilization/NotificationIcon.kt
- Fixtures: tests/src/com/unciv/logic/SerializationTests.kt (`test LastSeenImprovement serialization roundtrip`, `test CivRankingHistory serialization roundtrip`, `test Notification serialization roundtrip`)
- Deliverables: csharp/Unciv.Core/Json/LastSeenImprovement.cs, csharp/Unciv.Core/Logic/Civilization/CivRankingHistory.cs, Notification.cs, NotificationActions.cs, NotificationIcon.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~SerializationLeafTests`
- Claimed: 2026-10-24T04:50Z
- Notes: Notification actions are ported as pure serializable data (`LocationAction`, `TechAction`, `PolicyAction`, `CityAction`, `DiplomacyAction`, `MayaLongCountAction`, `CivilopediaAction`, `PromoteUnitAction`, `MapUnitAction`, `OverviewAction`, `EspionageAction`, `ReligionAction`); `execute(WorldScreen)` is UI and omitted per T043 (#7). `CivRankingHistory.recordRankingStats(Civilization)` is deferred (`TODO` on Civilization). IMPL NOTES: NotificationIcon lives in Kotlin NotificationIcons.kt (constants only; getImage is UI, omitted). JSON via [JsonConverter] attributes on the types (UncivJson untouched), libGDX prototype format (default-valued action fields omitted; HexCoord compact). LastSeenImprovement and CivRankingHistory wrap JavaHashMap so field order matches java.util.HashMap; inner ranking maps are insertion-ordered IReadOnlyDictionary. Kotlin NotificationActionsDeserializer semantics kept (known class names in fixed order, unknown names/fields ignored). Omitted until Civilization/City/MapUnit exist: MapUnitAction(unit)/(units), PromoteUnitAction(unit), DiplomacyAction(civ), CityAction.withLocation, CivRankingHistory.recordRankingStats; also Notification.execute/addNotificationIconsTo/round-robin index and the NotificationCategory typealias. System.Text.Json escapes apostrophes as \u0027 (valid, round-trips; libGDX writes raw). No Kotlin-generated goldens needed (fixtures are roundtrip tests).

### T052 UniqueTriggerActivation and UniqueTriggerExecutors
- State: Available
- Depends: T028, T046, T055, T056, T057, T060, T061, T063, T064, T065, T066, T068, T070
- Source: core/src/com/unciv/models/ruleset/unique/UniqueTriggerActivation.kt, UniqueTriggerExecutors.kt
- Fixtures: tests/src/com/unciv/uniques/ (trigger-related tests; list them when claiming; TestGame-based ones in T072)
- Deliverables: csharp/Unciv.Core/Models/Ruleset/Unique/UniqueTriggerActivation.cs, UniqueTriggerExecutors.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~UniqueTriggerTests`
- Claimed:
- Notes: Created from T027's review: T027 deferred `UniqueTriggerExecutors.triggerGlobalAlerts` (needs `Civilization.addNotification`, ported by T055) and the 1.5k-LOC `UniqueTriggerActivation`. Split by trigger family if it exceeds scope. Wires the `TODO(T052)` call sites left by T054, T063, T065 and `EventChoice.triggerChoice` (T030). Uses concrete Civilization, City, MapUnit and their managers; no new seams needed.
  - Backlog audit: Depends extended to every ticket whose call sites it wires (T046 GameInfo, T061, T063, T064, T068, T070 hold TODO(T052) markers; T056/T057 policy/religion adoption too). Call sites of NextTurnAutomation.chooseGreatPerson (GameView) are TODO(T094). Note ReligionState.cs carries a TODO(T052) that is really T057's (ReligionManager port moves that enum): fixed by T104/T057. T072 now depends on this ticket.
  - T057 left Models/Ruleset/Unique/UniqueTriggerActivation.cs with the three `TriggerUnique` overloads (civ+city/unit/tile, city, unit) as stubs returning false; ReligionManager.ChooseBeliefs and RuinsManager.SelectNextRuinsReward already call them with their final arguments. Replace the bodies (keep the signatures), then add tests for the founding/enhancing/adopting-belief triggers and ruins rewards (RuinsManager only remembers a reward when a trigger returns true).
  - T058: CivInfoTransientCache.TriggerUnique (Transients/CivInfoTransientCache.cs) is the no-op seam for the TriggerUponDiscoveringNaturalWonder call `UniqueTriggerActivation.triggerUnique(unique, civInfo, tile=tile, triggerNotificationText="due to discovering a Natural Wonder")`; T052 wires it.
  - From T056: TechManager, PolicyManager and GoldenAgeManager call UniqueTriggerActivation.triggerUnique through the single internal seam `Managers/TriggerUniqueSeam.TriggerUnique(unique, civInfo, triggerNotificationText)` (currently a no-op, marked TODO(T052)). T052 must replace its body with the real call and then may inline/delete the seam. Call sites: TechManager.AddTechnology/MoveToNewEra, PolicyManager.Adopt, GoldenAgeManager.EnterGoldenAge/EndTurn.
  - DiplomacyManager.TriggerUnique() (private, throws, TODO(T052)) is the call site of UniqueTriggerActivation.triggerUnique(unique, civ) for TriggerUponSigningPeace/DeclaringFriendship/SigningDefensivePact; replace its body.
  - From T114 (T056 review): add tests that every TriggerUniqueSeam.TriggerUnique call site fires (TechManager: AddTechnology tech uniques/TriggerUponResearch, MoveToNewEra era uniques/TriggerUponEnteringEra[Unfiltered]; DiplomacyManager.MakePeace TriggerUponSigningPeace via TriggerUnique(unique, civ), which throws NotImplemented until then).

### T054 Tile core (part 2b): TileStatFunctions, TileImprovementFunctions, TileNormalizer, ImprovementFunctions
- State: Done
- Depends: T053
- Source: core/src/com/unciv/logic/map/tile/TileStatFunctions.kt, TileImprovementFunctions.kt, TileNormalizer.kt, civilization/managers/ImprovementFunctions.kt
- Fixtures: tests/src/com/unciv/logic/map/TileImprovementConstructionTests.kt (needs TestGame: T072)
- Deliverables: csharp/Unciv.Core/Logic/Map/Tile/TileStatFunctions.cs, TileImprovementFunctions.cs, TileNormalizer.cs, csharp/Unciv.Core/Logic/Civilization/Managers/ImprovementFunctions.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~TileStatTests`
- Claimed: 2026-10-24T10:24Z
- Notes: SEAM RULE (set by T053, applies to T053-T072): the Tile -> TileMap -> Civilization -> City -> MapUnit -> GameInfo port order is acyclic because each ticket compiles only against classes that are already Done plus small engine-owned interfaces (`partial` extensions of the `IGameContext*` interfaces in `Models/Ruleset/Unique/GameContext.cs` / `ConditionalsSeams.cs`, or a new `I<Thing>` for types with no IGameContext seam, e.g. managers), in a file named `<Area>Seams.cs`, containing exactly the members the ticket calls. A field/parameter whose Kotlin type is a not-yet-ported class is typed by its seam. The ticket that later ports that class implements the seam, then retypes the earlier tickets' seam-typed fields/params to the concrete class (a mechanical edit of those files, listed in its Deliverables), keeping reference-identity Equals/GetHashCode (Kotlin Civilization/City/MapUnit/Tile have none). Do not add public API Kotlin lacks beyond the seams. TestGame-based fixtures are deferred to T035/T072. ~850 LOC. Uses concrete Tile (T053) and the Civilization/City/MapUnit seams (add the members it calls to the T053 seams files). `TileImprovementFunctions` calls `UniqueTriggerActivation.triggerUnique` (lines ~287/293): leave those two call sites as a clearly marked hook (`TODO(T052)`) and let T052 wire them. Adds the delegating Tile members deferred by T053. Stat results are Float arithmetic: keep exact order of operations.
  - Implemented. Seams added in Logic/Map/Tile/TileFunctionsSeams.cs: GameInfo.HandleObsoleteGlobalQuestsOfAliveCityStates; Civ.ForEachMatchingUnique(type, gameContext, action), IsDefeated, SetLastSeenImprovement, UniqueImprovements, GetTriggeredUniques, GetGameProgressModifier(unique), AddNotification(text, actions, category, icons); City.ForEachMatchingUnique (moved up from IConstructionCity, GameContext? param), GetCenterTile, UpdateCityStats, AddStat, RemoveCreateOneImprovementConstruction; Unit.GetTriggeredUniques, ClearPathfindingCache. T060/T046/T048 implement them.
    Tile got ImprovementFunctions, Stats, RemoveImprovement, SetImprovement x2, SetBaseTerrain, SetPillaged (+ClearAllPathfindingCaches). STILL DEFERRED (need TileImprovement.getTurnsToBuild, Civilization.gainStockpiledResource, tutorial settings): doWorkerTurn, startWorkingOnImprovement, queueImprovement(TileImprovement, civ, unit); the owning tickets of those must add them.
    UniqueTriggerActivation.triggerUnique is the no-op private hook TileImprovementFunctions.TriggerUnique marked TODO(T052) (both call sites route through it).
    Deviations: EnumMap -> Dictionary; Pair<String,Stats> -> value tuple; canImprovementBeBuiltHere defaults (resourceIsVisible, gameContext) are nullable params resolved in the body; ImprovementBuildingProblem permanent/reportable flags are extension methods IsPermanent/IsReportable; riverTerrain lazy is an explicit cache; getTileStatsBreakdown source name null prints "null" like Kotlin.
    Quirks kept: clearImprovement in TileNormalizer also clears the improvement queue (stopWorkingOnImprovement) and does not reset the tileImprovement cache; closestCity is the first minimum; notification is emitted once per stat inside the loop.
    Fixture TileImprovementConstructionTests needs TestGame (T072); tests here use fakes (TileStatTests, 37 cases).
  - Post-review fix (multiplier hoisted out of loop, matches Kotlin repeat() evaluating count once) re-reviewed: pass.

### T055 Civilization core: Civilization, ExploredRegion, PopupAlert
- State: Done
- Depends: T034, T045, T039, T053
- Source: core/src/com/unciv/logic/civilization/Civilization.kt, ExploredRegion.kt, PopupAlert.kt
- Fixtures: none directly (TestGame-based: T072)
- Deliverables: csharp/Unciv.Core/Logic/Civilization/Civilization.cs, ExploredRegion.cs, PopupAlert.cs, CivilizationSeams.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~CivilizationCoreTests`
- Claimed: 2026-10-24T10:35Z
- Notes: SEAM RULE (set by T053, applies to T053-T072): the Tile -> TileMap -> Civilization -> City -> MapUnit -> GameInfo port order is acyclic because each ticket compiles only against classes that are already Done plus small engine-owned interfaces (`partial` extensions of the `IGameContext*` interfaces in `Models/Ruleset/Unique/GameContext.cs` / `ConditionalsSeams.cs`, or a new `I<Thing>` for types with no IGameContext seam, e.g. managers), in a file named `<Area>Seams.cs`, containing exactly the members the ticket calls. A field/parameter whose Kotlin type is a not-yet-ported class is typed by its seam. The ticket that later ports that class implements the seam, then retypes the earlier tickets' seam-typed fields/params to the concrete class (a mechanical edit of those files, listed in its Deliverables), keeping reference-identity Equals/GetHashCode (Kotlin Civilization/City/MapUnit/Tile have none). Do not add public API Kotlin lacks beyond the seams. TestGame-based fixtures are deferred to T035/T072. ~1.6k LOC; delegate the read-and-brief step; split by member group at claim time if it exceeds scope. Civilization is ported BEFORE its managers, City, MapUnit and GameInfo, so every field of those types is a seam: `IGameInfoState : IGameContextGameInfo` (ruleset, turns, tileMap, civilizations, speed, difficulty, getCities...), `ITechManager`, `IPolicyManager`, `IReligionManager`, `IDiplomacyManager`, `IGoldenAgeManager`, `IGreatPersonManager`, `IVictoryManager`, `IRuinsManager`, `IEspionageManager`, `IQuestManager`, `ICivConstructions`, `ICivInfoTransientCache`, `ICivInfoStats`, `IUnitManager`, `ICityState` (extends IGameContextCity) and `IMapUnitState` (extends IGameContextUnit), each holding only the members Civilization.kt calls. It implements `IGameContextCivilization` (T027) and the Tile seam added by T053; it retypes Tile's owner-civ seam to `Civilization`. Notification/CivRankingHistory/NotificationAction types already exist (T051). `addNotification` is ported here. Deferred: members that call UniqueTriggerActivation (T052); turn handling (T070); anything on `civInfo.cities` beyond the `ICityState` seam goes to T060. `TODO(T0xx)` markers must name the owner ticket from this list (T056 tech/policy, T057 religion/ruins/victory/espionage, T058 stats/cache/constructions, T059 diplomacy, T060-T064 city, T065-T067 unit, T069 quests, T070 turn, T046 GameInfo).
  - Implemented: Civilization.cs (+ Proximity, CivFlags, CivilizationInfoPreview, nested NotificationsLog/HistoricalAttackMemory), ExploredRegion.cs, PopupAlert.cs (AlertType), CivilizationSeams.cs, Diplomacy/CityStatePersonality.cs (tiny leaf the Civilization field needs; T059/T068 must not re-port it), tests CivilizationCoreTests (+ CivilizationTestFakes). Gate + Standard checks pass.
    SEAMS (CivilizationSeams.cs, namespace Unciv.Logic.Civilization): IGameInfoState : IConstructionGameInfo (new Ruleset/Speed/Civilizations hide the IGameContext* ones; GameInfo implements both), ITechManager, IPolicyManager, IReligionManager (+IFounderReligion : IGameContextReligion), IGoldenAgeManager, IGreatPersonManager, IVictoryManager, IRuinsManager, IEspionageManager, IQuestManager, ICivConstructions, IDiplomacyManager, IDiplomacyFunctions, ITradeRequest, ICityStateFunctions, ICivInfoTransientCache, ICivInfoStats, IUnitManager, IMapUnitState : IGameContextUnit, ICityState : IGameContextCity. No member was added to any IGameContext* interface (the test fakes implement those). Civilization implements IGameContextCivilization (all partial extensions), IConstructionCivilization, IGameProgressCivilization, ILocalUniqueCacheCivilization; explicit interface impls cast the IGameContextCivilization argument to Civilization (it is the only implementer).
    HOW LATER TICKETS WIRE IN: the manager fields (Tech, Policies, CivConstructions, QuestManager, ReligionManager, GoldenAges, GreatPeople, EspionageManager, VictoryManager, RuinsManager, Units, DiplomacyFunctions, CityStateFunctions, Stats, Cache) are lateinit-style properties (throw until set) because Kotlin initializes them with `Xxx(this)` and nothing exists to construct: the owning ticket replaces the property by `= new XxxManager()` / ctor wiring (TODO(T0xx) on each) and retypes it to the concrete class, then retypes Diplomacy (JavaHashMap<string, IDiplomacyManager>), TradeRequests, Cities (IReadOnlyList<ICityState>). Retyping Tile/TileMap/etc. params from IGameContextCivilization to Civilization was NOT done (they still compile and Civilization is passed unchanged; doing it is a mechanical follow-up and would collide with parallel tickets editing those files).
    NAMESPACE TRAP: class Civilization lives in namespace Unciv.Logic.Civilization, so from any other namespace under Unciv.Logic the simple name `Civilization` binds to the namespace; use an alias (`using CivilizationClass = Unciv.Logic.Civilization.Civilization;`) or full name. Same for `Proximity` inside Civilization (the property Civilization.Proximity hides the enum; alias ProximityLevel is used).
    DEVIATIONS / DEFERRED: addCity (CityFounder) TODO(T061); getWorkerAutomation/workerAutomationCache (unit-automation port); isAIOrAutoPlaying's UI auto-play check is `false` headless; Kotlin GameResource is passed as `object` (Stat | SubStat | TileResource) in addGameResource/getGameResource; timeThis wrappers dropped; Civilization.NeutralRoads is HashSet<HexCoord> (seam type ISet) not a Java HashSet (only iterated by CivInfoStatsForNextTurn T058 - if order matters there, change the seam); matchesSingleFilter reads the other civ's diplomacy via `state.CivInfo as Civilization`; Civilization.Cities is replaced wholesale as in Kotlin.
    SEMANTIC TRAPS kept: Iterable<Float>.sum() accumulates in float (UpdateStatsForNextTurn: manual loop, then half-up roundToInt) and Iterable<Double>.sum() in map order (calculateScoreBreakdown is a JavaHashMap<string,double>: "Cities","Population","Tiles","Wonders","Technologies","Future Tech" keep java.util.HashMap order); diplomacy/proximity/flagsCountdown are JavaHashMap so getKnownCivs/forEachKnownCiv iterate in java.util order; ExploredRegion.getWidth for a single tile is 2*mapRadius+1 (the topLeft.x == bottomRight.x case takes the swapped branch) - ported as is; militaryMight uses sqrt(float) via double and truncating toInt; maxByOrNull picks the first maximum; getTurnsBetweenDiplomaticVotes truncates 15*speed.modifier.
  - Backlog audit: TileMap.cs has two TODO(T055) markers: TileMap.switchPlayersNation (Kotlin marks it 'currently unreachable', constructs a Civilization). Port it here after Civilization exists (or, if left out, say so here and in T104). Also T104 lists the Player.ChosenNation/SetNationTransient leftovers.

### T056 Civilization managers: TechManager, PolicyManager, GoldenAgeManager, GreatPersonManager
- State: Done
- Depends: T055
- Source: core/src/com/unciv/logic/civilization/managers/TechManager.kt, PolicyManager.kt, GoldenAgeManager.kt, GreatPersonManager.kt
- Fixtures: tests/src/com/unciv/logic/civilization/GreatPersonSelectionTest.kt and the tech/policy parts of managers/* (TestGame-based: T072)
- Deliverables: csharp/Unciv.Core/Logic/Civilization/Managers/*.cs, tests (+ retype Civilization fields from ITechManager/IPolicyManager/IGoldenAgeManager/IGreatPersonManager to the concrete classes)
- Gate: `dotnet test --filter FullyQualifiedName~CivManagerTests`
- Claimed: 2026-10-24T11:04Z
- Notes: SEAM RULE (set by T053, applies to T053-T072): the Tile -> TileMap -> Civilization -> City -> MapUnit -> GameInfo port order is acyclic because each ticket compiles only against classes that are already Done plus small engine-owned interfaces (`partial` extensions of the `IGameContext*` interfaces in `Models/Ruleset/Unique/GameContext.cs` / `ConditionalsSeams.cs`, or a new `I<Thing>` for types with no IGameContext seam, e.g. managers), in a file named `<Area>Seams.cs`, containing exactly the members the ticket calls. A field/parameter whose Kotlin type is a not-yet-ported class is typed by its seam. The ticket that later ports that class implements the seam, then retypes the earlier tickets' seam-typed fields/params to the concrete class (a mechanical edit of those files, listed in its Deliverables), keeping reference-identity Equals/GetHashCode (Kotlin Civilization/City/MapUnit/Tile have none). Do not add public API Kotlin lacks beyond the seams. TestGame-based fixtures are deferred to T035/T072. ~1.2k LOC. Concrete Civilization (T055); City/MapUnit through their seams. Implements the manager seams defined by T055. `TechManager` json field names must match Kotlin (saves). Deferred: calls into UniqueTriggerActivation (T052).
  - T057 added to the seams you retype: ITechManager.IsResearched(INonPerpetualConstruction)/CanBeResearched(string)/AddTechnology(string, bool showNotification = true) and IPolicyManager.CompletedBranches (used by EspionageManager, Spy, Milestone). TechManager.techsResearched is read in iteration order by EspionageManager.GetTechsToSteal (feeds a seeded random pick), so it must keep java.util.HashSet order.
  - Implemented TechManager, PolicyManager, GoldenAgeManager, GreatPersonManager (Logic/Civilization/Managers). Civilization.Tech/Policies/GoldenAges/GreatPeople are now the concrete classes with Kotlin initializers (`= new()`); ITechManager/IPolicyManager/IGoldenAgeManager/IGreatPersonManager are gone (T057's additions to them - IsResearched(construction), CanBeResearched, AddTechnology, CompletedBranches - are real members now; CompletedBranches is a set of PolicyBranch objects as in Kotlin, callers use .Count). Seams extended: IGameInfoState.NotifyExploredResources, ICityState.GetGreatPersonPoints/EditConstructionQueue/ValidateInProgressConstructions. T057's and T058's tests (CivReligionVictoryTests, CivTransientsTests) now use the real managers instead of FakeTech/FakePolicies.
    Deviations: (1) UniqueTriggerActivation.triggerUnique is not ported (T052): all calls go through the no-op Managers/TriggerUniqueSeam (TODO(T052), noted on T052). (2) Kotlin name clashes: `var allTechsAreResearched` + `fun allTechsAreResearched()` -> private field + AllTechsAreResearched(); `var scienceFromResearchAgreements` + private fun of the same name -> GetScienceFromResearchAgreements(). (3) PolicyManager.priorityMap needs IHasUniques.getWeightForAiDecision which is not ported; a PolicyBranch is neither BaseUnit nor Building, so the AiChoiceWeight product is applied inline (PolicyManager.GetWeightForAiDecision). (4) Kotlin HashSet<PolicyBranch>/HashSet<BaseUnit> results (identity hash, arbitrary order) are LinkedHashSet in ruleset order; getAdoptedPoliciesMatching(forRemoval) keeps the stable descending sort. Kotlin HashSet<String>/HashMap<String,Int> fields are JavaHashSet/JavaHashMap (java order). (5) Fonts.infinity inlined as U+221E in TurnsToTech. (6) Math.Pow(double,double) for the policy cost (2.01 exponent) - not bit-checked against the JVM. (7) Tests are hand-derived over fakes (Kotlin GoldenAgeManagerTest ported case by case; GreatPersonSelectionTest is TestGame-based -> T072). Semantic traps kept: IntArray.sum() wraps, Float/Int arithmetic in costOfTech, maxByOrNull/minByOrNull first-extreme, repeat(multiplier) evaluated once, singleOrNull = exactly one match. JSON converter follow-up is on T109.

### T057 Civilization managers: ReligionManager (+Religion), RuinsManager, VictoryManager, EspionageManager (+Spy)
- State: Done
- Depends: T055
- Source: core/src/com/unciv/logic/civilization/managers/ReligionManager.kt, RuinsManager.kt, VictoryManager.kt, EspionageManager.kt, core/src/com/unciv/models/Religion.kt, Spy.kt
- Fixtures: tests/src/com/unciv/logic/civilization/EspionageTests.kt (TestGame-based: T072); VictoryAvailabilityTests/VictoryMilestoneTests deferred by T030
- Deliverables: csharp/Unciv.Core/Logic/Civilization/Managers/*.cs, csharp/Unciv.Core/Models/Religion.cs, Spy.cs, tests (+ retype Civilization seams to concrete)
- Gate: `dotnet test --filter FullyQualifiedName~CivReligionVictoryTests`
- Claimed: 2026-10-24T10:57Z
- Notes: SEAM RULE (set by T053, applies to T053-T072): the Tile -> TileMap -> Civilization -> City -> MapUnit -> GameInfo port order is acyclic because each ticket compiles only against classes that are already Done plus small engine-owned interfaces (`partial` extensions of the `IGameContext*` interfaces in `Models/Ruleset/Unique/GameContext.cs` / `ConditionalsSeams.cs`, or a new `I<Thing>` for types with no IGameContext seam, e.g. managers), in a file named `<Area>Seams.cs`, containing exactly the members the ticket calls. A field/parameter whose Kotlin type is a not-yet-ported class is typed by its seam. The ticket that later ports that class implements the seam, then retypes the earlier tickets' seam-typed fields/params to the concrete class (a mechanical edit of those files, listed in its Deliverables), keeping reference-identity Equals/GetHashCode (Kotlin Civilization/City/MapUnit/Tile have none). Do not add public API Kotlin lacks beyond the seams. TestGame-based fixtures are deferred to T035/T072. ~1.1k LOC. `Logic/Civilization/Managers/ReligionState.cs` already exists (T028): make Religion implement `IGameContextReligion`/`ReligionState` as appropriate and the ReligionManager implement the seam T055 defined. Also finishes `Milestone.hasBeenCompletedBy/getFocus/...` deferred by T030 and `Victory` members that only needed a Civilization. Spy touches City/Tile: use concrete Tile, City seam.
  - Backlog audit: Logic/Civilization/Managers/ReligionState.cs holds a 'TODO(T052)' marker saying the enum moves with ReligionManager: that is this ticket (move the enum into ReligionManager.cs / its Kotlin file layout and delete the marker). SpyFleeReason / Spy used by TradeLogic (T086).
  - Implemented (Claude). Religion (Models/Religion.cs), ReligionManager (+ReligionState enum moved in, ToDisplayString = Kotlin toString), RuinsManager, VictoryManager, EspionageManager, Spy/SpyAction (Models/Spy.cs; nested enum Spy.CanStealTech is `Spy.CanStealTechResult` because C# forbids a type and a method of one name), and Milestone.hasBeenCompletedBy/getFocus/getVictoryScreenButtonHeaderText/getMoreCountable*/getCountableDone (Victory.cs; the UI members getVictoryScreenButtons/getFormattedLine/getCivilopediaTextLines stay deferred with the UI types). Civilization.religionManager/espionageManager/victoryManager/ruinsManager are now concrete (`= new()`); IReligionManager/IFounderReligion/IVictoryManager/IRuinsManager/IEspionageManager are gone. VictoryManager.getAvailableVictories returns List<Victory>.
      - Seam members added (CivilizationSeams.cs): IGameInfoState.Religions (JavaHashMap, java.util.HashMap order)/GetCivilization (Civilization)/GetCities (ICityState)/GetAliveMajorCivs; ITechManager.IsResearched(INonPerpetualConstruction)/CanBeResearched/AddTechnology; IPolicyManager.CompletedBranches; ICivConstructions.AddBoughtItemsWithIncreasingPrice; IUnitManager.AddUnit; IMapUnitState.Civ/GetTile/Religion/HasUnique; ICityState.Civ/Religion (new ICityReligionManager)/IsHolyCity/IsHolyCityOf/FoundingCivObject/GetMatchingUniques+ForEachMatchingUnique(includeCivUniques); ICityStateFunctions.UpdateAllyCivForCityState; IDiplomacyManager.AddSpiedOnUsModifier/SetDiscoveredSpiesInOurCitiesFlag/HasAgreedToNotSendSpiesFlag/GetInfluence/SetInfluence/AddInfluence/ReduceInfluence. Each ticket that ports the class implements these and retypes.
      - Stubs with markers: Models/Ruleset/Unique/UniqueTriggerActivation.cs (3 TriggerUnique overloads returning false, TODO(T052)); Logic/Map/MapUnit/Actions/UnitActionModifiers.cs (GetUsableUnitActionUniques returns empty, TODO(T079): until then ReligionManager.MaySpreadReligionAtAll is false); ReligionManager.AddConstructionBonuses no-op (TODO(T106): BaseUnit.addConstructionBonuses); ReligionManager.CityOf and Spy.GetCityOrNull cast Tile.GetCity() to ICityState (TODO(T060)).
      - Kotlin `assert(...)` in Spy.setAction/incrementTechStealingProgress is not ported (JVM assertions are off by default). DiplomaticVictoryVotesCast stays the IDictionary seam from T055 (voting results are order-insensitive except breakdown ties, which list in vote order).
      - Not covered here (TestGame-based, T072): EspionageTests, full ReligionManager.FoundReligion/UseProphetForEnhancingReligion/MaySpread flows (need MapUnit/Tile cities), Milestone ScoreAfterTimeOut positive case (needs a TileMap for the score), RuinsManager.SelectNextRuinsReward effects (need T052).

### T058 Civilization transients: CivInfoTransientCache, CivInfoStatsForNextTurn, CapitalConnectionsFinder, CivConstructions
- State: Done
- Depends: T055, T054
- Source: core/src/com/unciv/logic/civilization/transients/CivInfoTransientCache.kt, CivInfoStatsForNextTurn.kt, CapitalConnectionsFinder.kt, civilization/CivConstructions.kt
- Fixtures: tests/src/com/unciv/logic/civilization/CapitalConnectionsFinderTests.kt (TestGame-based: T072)
- Deliverables: csharp/Unciv.Core/Logic/Civilization/Transients/*.cs, CivConstructions.cs, tests (+ retype Civilization seams)
- Gate: `dotnet test --filter FullyQualifiedName~CivTransientsTests`
- Claimed: 2026-10-24T11:04Z
- Notes: SEAM RULE (set by T053, applies to T053-T072): the Tile -> TileMap -> Civilization -> City -> MapUnit -> GameInfo port order is acyclic because each ticket compiles only against classes that are already Done plus small engine-owned interfaces (`partial` extensions of the `IGameContext*` interfaces in `Models/Ruleset/Unique/GameContext.cs` / `ConditionalsSeams.cs`, or a new `I<Thing>` for types with no IGameContext seam, e.g. managers), in a file named `<Area>Seams.cs`, containing exactly the members the ticket calls. A field/parameter whose Kotlin type is a not-yet-ported class is typed by its seam. The ticket that later ports that class implements the seam, then retypes the earlier tickets' seam-typed fields/params to the concrete class (a mechanical edit of those files, listed in its Deliverables), keeping reference-identity Equals/GetHashCode (Kotlin Civilization/City/MapUnit/Tile have none). Do not add public API Kotlin lacks beyond the seams. TestGame-based fixtures are deferred to T035/T072. ~1.2k LOC. Concrete Civilization and Tile; cities via the `ICityState` seam (extend it with `cityStats`, `isCapital`, `getCenterTile`, buildings...). Resource/uniques caches must keep Kotlin's iteration order (LinkedHashMap) as they feed stats. `CivConstructions` calls `City`/`CityConstructions`: seam members, retyped by T060/T063.
  - T057 added ICivConstructions.AddBoughtItemsWithIncreasingPrice(name, amount) (= boughtItemsWithIncreasingPrice.add(name, amount)), used by ReligionManager.GenerateProphet.
  - Implemented CivConstructions, CivInfoTransientCache, CivInfoStatsForNextTurn, CapitalConnectionsFinder (+ CapitalConnectionMedium as enum with RoadType()/CheckRailroad() extensions). Civilization.Stats/Cache/CivConstructions are now the concrete classes (constructed in the Civilization constructor like Kotlin's `val stats = CivInfoStatsForNextTurn(this)`); the interfaces ICivInfoStats/ICivInfoTransientCache/ICivConstructions and their test fakes are gone. Civilization.UpdateStatsForNextTurn now sums GetHappinessBreakdown() (a JavaHashMap: java.util.HashMap order is observable through the Float sum) and GetStatMapForNextTurn().Values.
    Seam members added for the not-yet-ported types (implementers: T059 DiplomacyManager, T060/T063 City+CityConstructions, T065/T066 MapUnit/UnitManager, T057 Religion/Victory/Espionage managers, T068 CityStateFunctions, T046 GameInfo): IDiplomacyManager.GetInfluence/ResourcesFromTrade/GoldPerTurn/IsRelationshipIgnoreAfraidAlly; ICityState.Civ/ConnectedToCapitalStatus/ContainsBuildingUnique/IsBlockaded/GetResourcesGeneratedByCity/FinalStatList/HappinessList/GetLocalMatchingUniques/FreeBuildingsProvidedFromThisCity/CheapestStatBuilding/CompleteConstruction/IsBeingConstructedOrEnqueued/IsBuildable; IMapUnitState.GetMatchingUniques/ViewableTiles/GetTile/CanGarrison/GetResourceRequirementsPerTurn; ICityStateFunctions.GetCityStateResourcesForAlly; IEspionageManager.GetTilesVisibleViaSpies; IReligionManager.NumberOfCitiesFollowingThisReligion/NumberOfFollowersFollowingThisReligion; IVictoryManager.CurrentsSpaceshipParts; IGameInfoState.GetAliveMajorCivs. Implementers retype them to the concrete classes. The finder casts GameInfo.GetCities() to ICityState.
    Determinism deviations (all noted in file headers): Kotlin hashSetOf/HashMap keyed by identity-hashed Tile/City/Civilization/Building (viewable tiles, uniqueBuildings/Units/Improvements, viewedCivs, citiesConnectedToCapitalToMediums, newlyViewedNaturalWonders) iterate in an arbitrary order that reaches notification order; the port uses insertion order (LinkedHashSet/OrderedDictionary). EnumSet<CapitalConnectionMedium> is a HashSet. The three private free-building maps are System Dictionary/HashSet via the existing AddToMapOfSets helper (insertion order, not java.util order; nothing iterates them for rules). getHappinessBreakdown keeps java.util.HashMap order. Kotlin Sequence.drop(negative) throws, kept. sumOf over Int wraps (TransientsSupport.SumOf). timeThis profiling and purity annotations dropped; DebugUtils.VISIBLE_MAP is EngineOptions.VisibleMap.
    UniqueTriggerActivation.triggerUnique (natural-wonder trigger) is not ported (T052): CivInfoTransientCache.TriggerUnique is its no-op seam, noted on T052. ResourceSupplyList gained SubtractResourceRequirements and RemoveAll(predicate) (needed by updateCivResources). Tests: CivTransientsTests over the seam fakes (proximity short-cuts, unit supply formula, happiness base, free building grant/memory, countConstructedObjects, clone); the TestGame-based CapitalConnectionsFinderTests and finder behaviour on real maps are T072. No JSON attributes on CivConstructions: GameInfo serialization (T046) owns the field mapping of its private maps.
    Rebase note: T057 published first and retyped the Religion/Victory/Ruins/Espionage managers and added IGameInfoState.GetCities/GetAliveMajorCivs and IDiplomacyManager.GetInfluence; T058 kept those, dropped its duplicate seam members, and replaced ICivConstructions.AddBoughtItemsWithIncreasingPrice by `CivConstructions.BoughtItemsWithIncreasingPrice.Add` in ReligionManager.
  - Review round 1 fixes: AddFreeBuildings keeps the civ-wide GainFreeBuildings uniques lazy (re-evaluated per city like the Kotlin Sequence); cities.take(n) throws for negative n (TakeCities); ToInt rejects whitespace. Non-blocking left: thin coverage of maintenance/upkeep/finder paths (finder fixture is T072, rest needs real City/MapUnit from T060/T065 and is covered by T072's sweep).

### T059 Diplomacy core: DiplomacyManager, DiplomacyFunctions, Demand, Trade, TradeOffer, TradeOffersList
- State: Done
- Depends: T055
- Source: core/src/com/unciv/logic/civilization/diplomacy/DiplomacyManager.kt, DiplomacyFunctions.kt, Demand.kt (and DiplomacyFlags/DiplomaticStatus/RelationshipLevel if in the same package), logic/trade/Trade.kt, TradeOffer.kt, TradeOffersList.kt, TradeOfferType.kt
- Fixtures: tests/src/com/unciv/logic/civilization/diplomacy/*, DefensivePactTests.kt (TestGame-based: T072)
- Deliverables: csharp/Unciv.Core/Logic/Civilization/Diplomacy/*.cs, csharp/Unciv.Core/Logic/Trade/*.cs, tests (+ retype Civilization.IDiplomacyManager)
- Gate: `dotnet test --filter FullyQualifiedName~DiplomacyCoreTests`
- Claimed: 2026-10-24T11:06Z
- Notes: SEAM RULE (set by T053, applies to T053-T072): the Tile -> TileMap -> Civilization -> City -> MapUnit -> GameInfo port order is acyclic because each ticket compiles only against classes that are already Done plus small engine-owned interfaces (`partial` extensions of the `IGameContext*` interfaces in `Models/Ruleset/Unique/GameContext.cs` / `ConditionalsSeams.cs`, or a new `I<Thing>` for types with no IGameContext seam, e.g. managers), in a file named `<Area>Seams.cs`, containing exactly the members the ticket calls. A field/parameter whose Kotlin type is a not-yet-ported class is typed by its seam. The ticket that later ports that class implements the seam, then retypes the earlier tickets' seam-typed fields/params to the concrete class (a mechanical edit of those files, listed in its Deliverables), keeping reference-identity Equals/GetHashCode (Kotlin Civilization/City/MapUnit/Tile have none). Do not add public API Kotlin lacks beyond the seams. TestGame-based fixtures are deferred to T035/T072. ~1.5k LOC. Pure data/state parts of diplomacy and the trade data classes. Deferred: `DiplomacyTurnManager`, `DeclareWar`, `CityStateFunctions` (T068; they need City and MapUnit); `TradeLogic`/`TradeEvaluation` (Planned, after T068); anything calling `NextTurnAutomation`/Battle (Planned: Battle, Automation) goes behind a seam or a `TODO(T0xx)` that names a real ticket (add one to Planned if none exists).
  - Backlog audit: TradeOfferType.kt added to Source (enum used by TradeOffer). Deferred pieces now have tickets: TradeLogic = T086, TradeEvaluation = T089 (the 'Planned' paragraph is gone). Demand.kt's DiplomacyAutomation call (wantsToSign...) is a seam: TODO(T088). TradeView/DiplomacyManagerView are T084.
  - T057 added to the IDiplomacyManager seam for Spy: AddSpiedOnUsModifier(float) = addModifier(DiplomaticModifiers.SpiedOnUs, amount), SetDiscoveredSpiesInOurCitiesFlag(amount, adjustWithGameSpeed) = setFlag(DiscoveredSpiesInOurCities, ...), HasAgreedToNotSendSpiesFlag() = hasFlag(AgreedToNotSendSpies), GetInfluence/SetInfluence/AddInfluence/ReduceInfluence; Models/Spy.cs should call the real enums/members after the retype. ICityStateFunctions gained UpdateAllyCivForCityState (T068).
  - Implemented. Files: Logic/Civilization/Diplomacy/{DiplomacyManager,DiplomacyFunctions,Demand,DiplomacyFlags,DiplomaticModifiers,DiplomaticStatus,RelationshipLevel}.cs, Logic/Trade/{Trade(+TradeRequest),TradeOffer,TradeOffersList,TradeOfferType}.cs, tests in Unciv.Core.Tests/Logic/Diplomacy/DiplomacyCoreTests.cs (the TestGame-based DiplomacyManagerTests/GoldGiftingTests/DefensivePactTests cases that need maps/cities/units/city-state functions stay with T072; the map-free ones are ported).
    Retyped: Civilization.Diplomacy (JavaHashMap<string, DiplomacyManager>), DiplomacyFunctions (now `= new DiplomacyFunctions(this)` in the constructor), TradeRequests (List<TradeRequest>), GetDiplomacyManager*; removed IDiplomacyManager/IDiplomacyFunctions/ITradeRequest and their test fakes. Also rewired the T057/T058 users of the old IDiplomacyManager helper members (Spy, CapitalConnectionsFinder, CivInfoStatsForNextTurn) onto real calls and ported CivReligionVictoryTests to real managers.
    Seams extended (implementers must add): IGameInfoState.GetCivsSorted (T046), IQuestManager.JustMet (T069), ICityStateFunctions.GetTributeWillingness/CanProvideStat (T068), IMapUnitState.CurrentTile/TeleportToClosestMoveableTile (T066), ICityState.GetCenterTile (T060), ICityReligionManager.GetMajorityReligionName (T061).
    Deviations: DeclareWar() throws NotImplementedException (TODO(T068)); UniqueTriggerActivation.triggerUnique calls (peace/friendship/defensive-pact triggers) go through a private TriggerUnique() that throws (TODO(T052)); makePeaceOneSide builds `new Trade()` instead of TradeLogic(...).currentTrade (TradeLogic's constructor is side-effect free; T086); recieveGoldGifts/getGoldInflation copy TradeEvaluation.getGoldInflation (T089 should switch to TradeEvaluation); TradeOffer.getOfferText not ported (translation/Fonts/UncivGame); meetCiv skips the settings tutorial task; RelationshipLevel.color is Color() extension, operator plus is Plus(); Demand is a class with static instances (per-entry show override), not an enum. TradeOffersList derives List<TradeOffer> and hides Add (Kotlin overrides add only; AddRange/addAll stays non-merging as in Kotlin). TradeOffer.Equals(TradeOffer) is the Kotlin overload (ignores duration); Equals(object) is the data-class equality. Quirk kept: signDefensivePact returns (not continues) on a spectator third civ. DiplomacyCoreTests is in the TranslationState collection because Denounce logs through the global Log.Backend.

### T060 City core: City, CityFocus, CityResources, CityEspionageManager
- State: Done
- Depends: T055, T056, T057, T058
- Source: core/src/com/unciv/logic/city/City.kt, CityFocus.kt, CityResources.kt, managers/CityEspionageManager.kt
- Fixtures: tests/src/com/unciv/logic/city/CityTest.kt (TestGame-based: T072)
- Deliverables: csharp/Unciv.Core/Logic/City/City.cs, CityFocus.cs, CityResources.cs, CityEspionageManager.cs, CitySeams.cs, tests (+ retype Civilization.cities / Tile city seams to City)
- Gate: `dotnet test --filter FullyQualifiedName~CityCoreTests`
- Claimed: 2026-10-24T11:28Z
- Notes: SEAM RULE (set by T053, applies to T053-T072): the Tile -> TileMap -> Civilization -> City -> MapUnit -> GameInfo port order is acyclic because each ticket compiles only against classes that are already Done plus small engine-owned interfaces (`partial` extensions of the `IGameContext*` interfaces in `Models/Ruleset/Unique/GameContext.cs` / `ConditionalsSeams.cs`, or a new `I<Thing>` for types with no IGameContext seam, e.g. managers), in a file named `<Area>Seams.cs`, containing exactly the members the ticket calls. A field/parameter whose Kotlin type is a not-yet-ported class is typed by its seam. The ticket that later ports that class implements the seam, then retypes the earlier tickets' seam-typed fields/params to the concrete class (a mechanical edit of those files, listed in its Deliverables), keeping reference-identity Equals/GetHashCode (Kotlin Civilization/City/MapUnit/Tile have none). Do not add public API Kotlin lacks beyond the seams. TestGame-based fixtures are deferred to T035/T072. ~1.2k LOC. Concrete Civilization and its managers (T055-T058), concrete Tile. City's own sub-objects are seams: `ICityConstructions`, `ICityStats`, `ICityPopulationManager`, `ICityExpansionManager`, `ICityReligionManager`, plus `IMapUnitState` for units. Implements `IGameContextCity` (T027) and retypes the city seam members in Civilization (T055), Tile (T053) and the managers. `City` json field names must match Kotlin.
  - Backlog audit: CityFocus uses Automation.* (rankStatsValue etc.): TODO(T087). City.getLandAttackPath/getAmphibiousAttackPath/getRoadPath use PathingMap: seam, TODO(T093).
  - T057 added City members to the ICityState seam (Civ, Religion as ICityReligionManager, IsHolyCity, IsHolyCityOf, FoundingCivObject, GetMatchingUniques/ForEachMatchingUnique with includeCivUniques); City implements them. Retype the ICityState uses in Logic/Civilization/Managers (ReligionManager, EspionageManager, VictoryManager), Models/Spy.cs and drop the `(ICityState)Tile.GetCity()` casts (ReligionManager.CityOf, Spy.GetCityOrNull, ReligionManager.MaySpreadReligionNow, TODO(T060)).
  - Implemented. Files: Logic/City/{City,CityFocus,CityResources,CityEspionageManager,CitySeams}.cs; tests Unciv.Core.Tests/Logic/City/CityCoreTests.cs (66 cases over a real Civilization/TileMap with fake sub-objects; CityTest.kt itself stays with T072). Standard checks and Gate pass.
      - Seams (CitySeams.cs): ICityConstructions (T063), ICityStats (T062), ICityPopulationManager + ICityExpansionManager (T061), IUnitPromotions (T066); ICityReligionManager (CivilizationSeams.cs) gained Clone/SetTransients/IsBlockedHolyCity/GetUniques/GetAllUniques/ForEachMatchingUnique(x2)/ForEachUnique (T061). City holds them as lateinit-style properties (Population, CityConstructions, Expansion, Religion, CityStats); the porting ticket replaces each by Kotlin's initializer (`= new X()`), retypes it and drops the seam-only members.
      - ICityState is gone: Civilization.Cities, GetCities, CivConstructions, Spy, EspionageManager, TechManager, ReligionManager ... now use City directly. Members that live on Kotlin sub-objects are called through them (city.CityConstructions.GetBuiltBuildings(), city.CityStats.Update(...), city.Population.Population). IGameContextCity/IConstructionCity/ILocalUniqueCacheCity members are implemented explicitly on City. Test fake: FakeCityState now wraps a real City with data-holding sub-object fakes (implicit conversion to City).
      - Tile city seams (OwningCity/GetCity/GetWorkingCity/SetOwningCity, TileStatFunctions/TileImprovementFunctions) are NOT retyped: the Tile/Pathfinding tests use fake civilizations whose cities cannot be real City objects. Follow-up T115 (Depends T060, T035); the casts in ReligionManager.CityOf and Spy.GetCityOrNull are marked TODO(T115).
      - Deferred, marked or noted on the owning tickets: City.getLandAttackPath/getAmphibiousAttackPath/getRoadPath/getRoadPathToAny + PathingMap caches + clearCaches (T093), getGreatPersonPercentageBonus (T062; getGreatPersonPoints goes through ICityStats.GetGreatPersonPoints until then), annexCity/puppetCity/liberateCity/moveToCiv throw InvalidOperationException until CityConquestFunctions (T064).
      - Deviations: resetSpecialistsControl reads UncivGame settings, so it takes autoAssignSpecialistsInNewCities and localUserId as parameters (callers: CityFounder/conquest tickets); tilesInRange is a LinkedHashSet<Tile> (Kotlin HashSet of identity-hashed tiles has no deterministic order); Kotlin Int sums (sumOf) wrap (unchecked); matchesFilter(filter, viewingCiv = civ) is two overloads so an explicit null differs from the default; CityFocus is an enum plus CityFocusExtensions (the UI KeyboardBinding is stripped; the audit's Automation dependency is KDoc only, so no TODO(T087)); CityEspionageManager/SpyFleeReason live in Logic/City (Kotlin package city.managers) because the only other City manager they would share a folder with is not ported yet.
      - Semantic traps kept: forEachMatchingUnique(includeCivUniques=false) passes `state`, not the gameContext argument, to the unique maps; the Population/Tile sets are replaced (copy-on-write withItem/withoutItem) never mutated, and reassignPopulation(resetLocked=false) aliases workedTiles = lockedTiles; Kotlin `foundingCivObject != civ` is true for a null founder (diplomatic marriage).
  - Review round 1 fixes: [JsonInclude] on foundingCiv/cityAIFocus/flagsCountdown (+ metadata test), per-group double sums in StatPercentFromObjectToResource, unchecked Int sum in GetAvailableResourceAmount, T065 note. Trailing-default overloads of forEach*Unique left as callers pass State (seam simplification).

### T061 City managers: CityPopulationManager, CityExpansionManager, CityReligionManager, CityFounder
- State: In-Review [Antigravity]
- Depends: T060
- Source: core/src/com/unciv/logic/city/managers/CityPopulationManager.kt, CityExpansionManager.kt, CityReligionManager.kt, CityFounder.kt
- Fixtures: tests/src/com/unciv/logic/city/managers/* (TestGame-based: T072)
- Deliverables: csharp/Unciv.Core/Logic/City/Managers/*.cs, tests (+ retype City seams)
- Gate: `dotnet test --filter FullyQualifiedName~CityManagerTests`
- Claimed: 2026-10-24T12:26:30Z
- Notes: SEAM RULE (set by T053, applies to T053-T072): the Tile -> TileMap -> Civilization -> City -> MapUnit -> GameInfo port order is acyclic because each ticket compiles only against classes that are already Done plus small engine-owned interfaces (`partial` extensions of the `IGameContext*` interfaces in `Models/Ruleset/Unique/GameContext.cs` / `ConditionalsSeams.cs`, or a new `I<Thing>` for types with no IGameContext seam, e.g. managers), in a file named `<Area>Seams.cs`, containing exactly the members the ticket calls. A field/parameter whose Kotlin type is a not-yet-ported class is typed by its seam. The ticket that later ports that class implements the seam, then retypes the earlier tickets' seam-typed fields/params to the concrete class (a mechanical edit of those files, listed in its Deliverables), keeping reference-identity Equals/GetHashCode (Kotlin Civilization/City/MapUnit/Tile have none). Do not add public API Kotlin lacks beyond the seams. TestGame-based fixtures are deferred to T035/T072. ~1.2k LOC. Implements the ICityPopulationManager/ICityExpansionManager/ICityReligionManager seams from T060. `CityFounder` is required by TestGame (T035). Tile-ownership and border expansion iteration order is game state.
  - Backlog audit: CityPopulationManager's Automation calls (autoAssignPopulation ranking): TODO(T087).
  - T057 defined ICityReligionManager (AddPressure, GetMajorityReligion returning Religion?, GetFollowersOf, IsProtectedByInquisitor, ReligionThisIsTheHolyCityOf): CityReligionManager implements it, then retype City.religion and delete the seam (ReligionManager/Spy use it through ICityState.Religion).
  - From T060: City.Population/Expansion/Religion are lateinit-style properties typed ICityPopulationManager/ICityExpansionManager/ICityReligionManager (CitySeams.cs, CivilizationSeams.cs). Implement them, retype the City properties and replace the lateinit by Kotlin's initializers, drop seam-only members (e.g. ICityPopulationManager.City/GetPopulationFilterAmount stay as real members). City.ResetSpecialistsControl takes the UncivGame settings as parameters (autoAssignSpecialistsInNewCities, localUserId); CityFounder passes them. Civilization.addCity (TODO(T061) in Civilization.cs) is yours too.
  - Implemented (Unciv.Core/Logic/City/Managers: CityPopulationManager, CityExpansionManager, CityReligionManager, CityFounder, CityManagerSeams; tests Unciv.Core.Tests/Logic/City/CityManagerTests.cs, 48 tests). City.Population/Expansion/Religion are now concrete with Kotlin initializers; ICityPopulationManager/ICityExpansionManager/ICityReligionManager seams deleted; ICityConstructions.AddBuilding(building, tryAddFreeBuildings = true) added for CityFounder (T063 implements). Civilization.AddCity(location, unit, autoAssignSpecialistsInNewCities = true, localUserId = null) added (settings are explicit params, default = GameSettings default).
    - Seams added to IMapUnitState (T065 implements): IsSleeping, HasUniqueToBuildImprovements, HasUniqueToCreateWaterImprovements, Due, Action, GetTriggeredUniques(trigger, ctx). FoundCity/AddCity take IMapUnitState? for `unit` (T065 retype).
    - CityManagerSeams (internal static): Automation (ICityWorkAutomation: RankStatsForCityWork/RankSpecialist/RankTileForCityWork) -> T087 replaces by direct Automation calls; GreatPersonPercentageBonus(city) -> T062 adds City.getGreatPersonPercentageBonus and replaces. Both default to throwing NotSupportedException; tests install stand-ins (CityManagerTests runs sequentially).
    - Test-only virtuals: CityPopulationManager.AutoAssignPopulation/UnassignExtraPopulation/GetPopulationFilterAmount, CityExpansionManager.RelinquishOwnership/SetTransients, CityReligionManager.SetTransients/AddPressure/GetMajorityReligion(Name)/GetFollowersOf/IsProtectedByInquisitor (City tests' fakes now subclass the real managers). Population.Population has an internal setter (Kotlin private set) with [JsonInclude].
    - Traps kept: remainders HashMap -> JavaHashMap (tie order); maxByOrNull = first max with Float.compareTo (AStar.JavaFloatCompare made internal); Int division for pressurePerFollower and population/2; Counter.get(null)=0 in getPressureDeficit; withItem/withoutItem copy JavaHashSet; foodRequired is Double math, toInt saturating; Tile.getCity()/getOwner() still IGameContextCity/Civilization seams (T115) so casts are used.
    - Not testable yet: CityFounder.FoundCity end to end needs CityConstructions (T063) and CityStats (T062) (lateinit seams); only the name generation (internal GenerateNewCityName/BorrowCityName) is tested. T072 ports the TestGame fixtures.
  - Correction: CityManagerSeams only holds the Automation seam (instance property CityPopulationManager.Automation, T087 replaces it); the great person bonus seam was dropped because T062 added City.GetGreatPersonPercentageBonus. IMapUnitState got no new members: the code uses MapUnit directly (T065 landed). Review fixes: CityFounder passes `unit` to the civ-trigger call; Float.compareTo tie semantics in specialist/worst-tile loops; SpecialistAllocations private setter.

### T062 CityStats and GreatPersonPointsBreakdown
- State: Done
- Depends: T060, T058
- Source: core/src/com/unciv/logic/city/CityStats.kt, GreatPersonPointsBreakdown.kt
- Fixtures: stat-related cases in tests/src/com/unciv/logic/city/CityTest.kt (TestGame-based: T072)
- Deliverables: csharp/Unciv.Core/Logic/City/CityStats.cs, GreatPersonPointsBreakdown.cs, tests (+ retype City.ICityStats)
- Gate: `dotnet test --filter FullyQualifiedName~CityStatsTests`
- Claimed: 2026-10-24T12:00Z
- Notes: SEAM RULE (set by T053, applies to T053-T072): the Tile -> TileMap -> Civilization -> City -> MapUnit -> GameInfo port order is acyclic because each ticket compiles only against classes that are already Done plus small engine-owned interfaces (`partial` extensions of the `IGameContext*` interfaces in `Models/Ruleset/Unique/GameContext.cs` / `ConditionalsSeams.cs`, or a new `I<Thing>` for types with no IGameContext seam, e.g. managers), in a file named `<Area>Seams.cs`, containing exactly the members the ticket calls. A field/parameter whose Kotlin type is a not-yet-ported class is typed by its seam. The ticket that later ports that class implements the seam, then retypes the earlier tickets' seam-typed fields/params to the concrete class (a mechanical edit of those files, listed in its Deliverables), keeping reference-identity Equals/GetHashCode (Kotlin Civilization/City/MapUnit/Tile have none). Do not add public API Kotlin lacks beyond the seams. TestGame-based fixtures are deferred to T035/T072. ~830 LOC, Float/Int mixing everywhere: preserve exact evaluation order and rounding (half-up helper, T010-era KotlinMath). Also provides what `Countables` tests (stat-per-turn countables) deferred by T028 need.
  - From T060: City.GetGreatPersonPoints() delegates to ICityStats.GetGreatPersonPoints() (seam member); once GreatPersonPointsBreakdown exists make City.GetGreatPersonPoints call GreatPersonPointsBreakdown(this).Sum(), drop the seam member, and add City.GetGreatPersonPercentageBonus (Kotlin getGreatPersonPercentageBonus, deferred from T060). Retype City.CityStats (ICityStats) to CityStats and construct it as CityStats(this).
  - T062 implemented: CityStats.cs (+StatTreeNode), GreatPersonPointsBreakdown.cs, CityStatsTests. City.CityStats is now the concrete CityStats (lazily `new CityStats(this)`, settable); ICityStats removed; City.GetGreatPersonPoints/GetGreatPersonPercentageBonus added. Seam members added for T061/T063 to implement: ICityConstructions.{CurrentConstructionName,GetStats (StatTreeNode),GetMaintenanceCosts}; ICityPopulationManager.{GetNewSpecialists,GetFreePopulation,GetNumberOfSpecialists}; ICityReligionManager.GetFollowersOfMajorityReligion. CityStats.Update and HasExtraAnnexUnhappiness are virtual only so test fakes can count calls (FakeCityStats : CityStats). Float order kept (Float sums accumulate from 0f, sumOf{toDouble} in Double); timeThis/purity annotations stripped; DebugUtils.SUPERCHARGED = GameInfo.EngineOptions.Supercharged. Kotlin traps kept: baseStatTree.children["Buildings"]/Add share child nodes without copying; Update NPE on missing "Population" node (pop 0) is a KeyNotFoundException here. TestGame-based CityTest stat fixtures remain T072. CivManagerTests GreatPersonPoints fake now feeds a synthetic building through the real breakdown.

### T063 CityConstructions
- State: In-Progress [Claude]
- Depends: T060, T062
- Source: core/src/com/unciv/logic/city/CityConstructions.kt
- Fixtures: production-related cases in tests/src/com/unciv/logic/civilization/FreeBuildingTests.kt (TestGame-based: T072)
- Deliverables: csharp/Unciv.Core/Logic/City/CityConstructions.cs, tests (+ retype City.ICityConstructions and CivConstructions seam members)
- Gate: `dotnet test --filter FullyQualifiedName~CityConstructionsTests`
- Claimed: 2026-10-24T12:09Z
- Notes: SEAM RULE (set by T053, applies to T053-T072): the Tile -> TileMap -> Civilization -> City -> MapUnit -> GameInfo port order is acyclic because each ticket compiles only against classes that are already Done plus small engine-owned interfaces (`partial` extensions of the `IGameContext*` interfaces in `Models/Ruleset/Unique/GameContext.cs` / `ConditionalsSeams.cs`, or a new `I<Thing>` for types with no IGameContext seam, e.g. managers), in a file named `<Area>Seams.cs`, containing exactly the members the ticket calls. A field/parameter whose Kotlin type is a not-yet-ported class is typed by its seam. The ticket that later ports that class implements the seam, then retypes the earlier tickets' seam-typed fields/params to the concrete class (a mechanical edit of those files, listed in its Deliverables), keeping reference-identity Equals/GetHashCode (Kotlin Civilization/City/MapUnit/Tile have none). Do not add public API Kotlin lacks beyond the seams. TestGame-based fixtures are deferred to T035/T072. ~1.1k LOC; delegate the brief. Construction queue order and `constructionQueue` json format are game state. Unit production (`constructUnit`) goes through the `IMapUnitState`/`IUnitManager` seams (placement is T066); `UniqueTriggerActivation` calls are `TODO(T052)`. Also fills in `BaseUnit`/`Building` members deferred by T033 that only needed City/Civilization (list the ones taken in Notes).
  - Backlog audit: Automation.* calls are TODO(T087); ConstructionAutomation (chooseNextConstruction) is TODO(T091). Must also provide BaseUnit.CanBePurchasedWithStat(city) (currently throws NotSupportedException because getRejectionReasons is not ported; BaseUnit.cs ~line 206) and the other BaseUnit/Building getRejectionReasons-family members deferred by T033: remove the NotSupportedException.
  - From T060: City.CityConstructions is a lateinit-style property typed ICityConstructions (CitySeams.cs); implement it, retype, replace by Kotlin's initializer, and drop seam-only members that Kotlin City reaches differently (IsBuildable(Building) is building.isBuildable(cityConstructions); EditQueue mirrors editQueue).

### T064 City turn and conquest: CityTurnManager, CityConquestFunctions
- State: Available
- Depends: T060, T061, T063
- Source: core/src/com/unciv/logic/city/managers/CityTurnManager.kt, CityConquestFunctions.kt
- Fixtures: tests/src/com/unciv/logic/city/CityConquestFunctionsTest.kt (TestGame-based: T072)
- Deliverables: csharp/Unciv.Core/Logic/City/Managers/CityTurnManager.cs, CityConquestFunctions.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~CityTurnTests`
- Claimed:
- Notes: SEAM RULE (set by T053, applies to T053-T072): the Tile -> TileMap -> Civilization -> City -> MapUnit -> GameInfo port order is acyclic because each ticket compiles only against classes that are already Done plus small engine-owned interfaces (`partial` extensions of the `IGameContext*` interfaces in `Models/Ruleset/Unique/GameContext.cs` / `ConditionalsSeams.cs`, or a new `I<Thing>` for types with no IGameContext seam, e.g. managers), in a file named `<Area>Seams.cs`, containing exactly the members the ticket calls. A field/parameter whose Kotlin type is a not-yet-ported class is typed by its seam. The ticket that later ports that class implements the seam, then retypes the earlier tickets' seam-typed fields/params to the concrete class (a mechanical edit of those files, listed in its Deliverables), keeping reference-identity Equals/GetHashCode (Kotlin Civilization/City/MapUnit/Tile have none). Do not add public API Kotlin lacks beyond the seams. TestGame-based fixtures are deferred to T035/T072. ~600 LOC. Conquest touches units, diplomacy and religion: concrete T055-T063 classes, MapUnit via `IMapUnitState` (retyped by T065).
  - From T060: City.AnnexCity/PuppetCity/LiberateCity/MoveToCiv throw InvalidOperationException (TODO(T064)); replace the bodies by CityConquestFunctions(this).annexCity() etc. City.DestroyCity is ported, but only exercised against fake sub-objects.

### T065 MapUnit core: MapUnit, MapUnitCache
- State: Available
- Depends: T055, T056, T060
- Source: core/src/com/unciv/logic/map/mapunit/MapUnit.kt, MapUnitCache.kt
- Fixtures: tests/src/com/unciv/logic/map/UnitFormationTests.kt (TestGame-based: T072)
- Deliverables: csharp/Unciv.Core/Logic/Map/MapUnit/MapUnit.cs, MapUnitCache.cs, MapUnitSeams.cs, tests (+ retype Tile/Civilization/City unit seams to MapUnit)
- Gate: `dotnet test --filter FullyQualifiedName~MapUnitCoreTests`
- Claimed: 2026-10-24T12:20:25Z
- Notes: SEAM RULE (set by T053, applies to T053-T072): the Tile -> TileMap -> Civilization -> City -> MapUnit -> GameInfo port order is acyclic because each ticket compiles only against classes that are already Done plus small engine-owned interfaces (`partial` extensions of the `IGameContext*` interfaces in `Models/Ruleset/Unique/GameContext.cs` / `ConditionalsSeams.cs`, or a new `I<Thing>` for types with no IGameContext seam, e.g. managers), in a file named `<Area>Seams.cs`, containing exactly the members the ticket calls. A field/parameter whose Kotlin type is a not-yet-ported class is typed by its seam. The ticket that later ports that class implements the seam, then retypes the earlier tickets' seam-typed fields/params to the concrete class (a mechanical edit of those files, listed in its Deliverables), keeping reference-identity Equals/GetHashCode (Kotlin Civilization/City/MapUnit/Tile have none). Do not add public API Kotlin lacks beyond the seams. TestGame-based fixtures are deferred to T035/T072. ~1.4k LOC; delegate the brief. Concrete Civilization, City, Tile. Sub-objects are seams: `IUnitMovement` (T067), `IUnitPromotions`/`IUnitTurnManager`/`IUnitUpgradeManager` (T066). Implements `IGameContextUnit` (T027) and `IMapUnitState`. Deferred: UniqueTriggerActivation calls (T052), Battle/Automation calls (Planned; `TODO` must name a real ticket).
  - Backlog audit: MapUnit.doAction -> UnitAutomation.automateUnitMoves/automatedExplore via GameView: TODO(T090). Battle/combatant calls: BattleDamage/MapUnitCombatant = T081, Battle = T083. UnitActions = T079/T080.
  - T057 added to the IMapUnitState seam: Civ, GetTile (both `new` Civilization/Tile), Religion (get/set string?), HasUnique(UniqueType, GameContext?). MapUnit implements them; ReligionManager/RuinsManager use IMapUnitState (unit.cache.state comes from IGameContextUnit.CacheState).
  - From T060: City.GetGarrison, City.DestroyCity (air units, teleport) and IGameContextCity.CenterTileMilitaryUnitCanGarrison cast Tile.MilitaryUnit/AirUnits to IMapUnitState (TODO(T065) in City.cs); retype with MapUnit.
  -   - IMPLEMENTED (T065): MapUnit.cs, MapUnitCache.cs, MapUnitSeams.cs, UnitMovementMemoryType.cs in Logic/Map/MapUnit, namespace `Unciv.Logic.Map.MapUnits` (NOT ...MapUnit: T057's `Unciv.Logic.Map.MapUnit.Actions` namespace would shadow the class name inside Unciv.Logic.Map; files in namespace Unciv.Logic.Map.* use the alias `MapUnitClass = Unciv.Logic.Map.MapUnits.MapUnit`). Tests: MapUnitCoreTests.cs (72 tests), MapUnitTestSupport.cs (FakeUnitMovement/FakeUnitPromotions/FakeUnitUpgradeManager + MapUnitFactory building real MapUnits over real Civilizations; T066/T067 replace the fakes). Gate and Standard checks pass (1915 tests).
      - SEAMS (MapUnitSeams.cs): IUnitMovement (CanMoveTo, CanPassThrough, GetCannotMoveToReason, TeleportToClosestMoveableTile, ClearPathfindingCache, GetDistanceToTilesKeys = `getDistanceToTiles().keys`, CanReach, HeadTowards, MoveToTile), IUnitUpgradeManager (empty marker), IUnitPromotions (moved here from CitySeams.cs and extended: Promotions JavaHashSet<string>, NumberOfPromotions, AddPromotion, GetPromotions(sorted), Clone(MapUnit), SetTransients(MapUnit)), IBarbarianManager (SpawnBarbarian; T078). There is no IUnitTurnManager: MapUnit.kt never calls it. MapUnit.Movement/Upgrade/Promotions are Lateinit-style properties like Civilization.Units (T067/T066 set `= new UnitMovement(this)` etc. in the initializer, and retype). UnitActionNames (internal) holds the UnitActionType.value strings (TODO(T079): use UnitActionType).
      - Seams added to existing interfaces (test fakes updated): IUnitManager.AddUnit(MapUnit, bool updateCivInfo = true) (T066 implements; it is `UnitManager.addUnit(mapUnit, updateCivInfo)`), IQuestManager.BarbarianCampCleared(civ, position), IGameInfoState.Barbarians (IBarbarianManager).
      - Retyped: Tile.MilitaryUnit/CivilianUnit/AirUnits/GetUnits/GetFirstUnit/RemoveUnit now MapUnit (IGameContextTile.MilitaryUnit/AirUnits/GetUnits are explicit); City.GetGarrison returns MapUnit?, DestroyCity/CenterTileMilitaryUnitCanGarrison no longer cast. NOT retyped (stay on the IMapUnitState/IGameContextUnit seams, which MapUnit implements): IUnitManager.GetCivUnits/NewMapUnit/AddUnit(baseUnit)/RemoveUnit and IGameContextCivilization.NewMapUnit (T066 retypes UnitManager), TileMap.PlaceUnitNearTile return (T066 via NewMapUnit), ReligionManager/RuinsManager/GreatPersonManager/CivInfoStatsForNextTurn/UnitActionModifiers parameters (IMapUnitState, legitimately an interface MapUnit implements; T079 retypes UnitActionModifiers), CarrierSlotMatcher (IGameContextUnit.MatchesFilter only).
      - Deviations: (1) getMovementString dropped (UncivGame settings number format: UI). (2) Kotlin default parameter `gameContext = cache.state` is `GameContext? = null` (null = cache.state); matchesFilter's `state` default is null = EmptyState. IGameContextUnit/IMapUnitState members whose shape differs (Civ, GetTile, MatchesFilter(filter), HasUnique, GetMatchingUniques, Destroy(), Clone(), GetMaxMovement(), ViewableTiles...) are explicit interface implementations. (3) MapUnitCache.doubleMovementInTerrain/paradropDestinationTileFilters are Dictionary (looked up by key or scanned with none{}); viewableTiles is a HashSet<Tile> (identity hash, order undefined in Kotlin too). abilityToTimesUsed/statusMap are JavaHashMap (iteration order = HashMap). (4) Float.pow in getForceEvaluation is Math.Pow on the widened doubles narrowed to float (Kotlin Float.pow), `ulp` is MathF.BitIncrement(x)-x. (5) Kotlin Exception -> InvalidOperationException (messages kept); `movementMemories.last()` on empty throws ArgumentOutOfRange instead of NoSuchElement. (6) Deferred with owners: doAction's UnitAutomation.automateUnitMoves/automatedExplore (TODO(T090)), moveThroughTile's BattleUnitCapture.captureCivilianUnit (TODO(T082)), all UniqueTriggerActivation.triggerUnique calls reach the T052 stub. (7) JSON: transient members carry [JsonIgnore]; the persisted names are the PascalCase properties and the private `escorting` field; the Gdx-compatible wiring (names, IUnitPromotions/IsPartOfGameInfoSerialization converters) is T110. UnitStatus/UnitMovementMemory have the parameterless deserialization constructors. (8) TileTests/Pathfinding test fakes for units: the TileUnit fake was replaced by real MapUnits (MapUnitFactory); the IsBlockaded test uses barbarians as the always-at-war enemy.
  - REVIEW CHANGES-REQUESTED [Antigravity]:
    - Auditor: Parity, determinism, math, and seams are high quality. However, standard checks (`dotnet test`) fail on 2 policy tests:
      1. Policy violation (T119 - Null-forgiving '!'): 8 unjustified null-forgiving operators found in `MapUnit.cs` (lines 373, 397, 400, 618, 976, 1153, 1165) and `MapUnitCache.cs` (line 127). Under T119, replace these with `.NotNull()` (from `Unciv.Logic.Map.Tiles` or `NullExtensions`), a null-safe refactor (e.g. `OfType<TileImprovement>()` in MapUnitCache, local null-checked variable in StartEscorting), or add `// JUSTIFY: T065 why` within 3 lines above if genuinely safe.
      2. Policy violation (T121 - Coupling Ratchet): Both `MapUnit.cs` and `City.cs` fail `CouplingRatchetTests` with 13 Unciv namespace dependencies (limit 12).
         - In `MapUnit.cs`: `using NotificationCategory = Unciv.Logic.Civilization.Notification.NotificationCategory;` causes the syntax scanner to count `Unciv.Logic.Civilization.Notification` as a 13th namespace. Change this to alias `Notification = Unciv.Logic.Civilization.Notification;` or use `Notification.NotificationCategory` directly to keep `MapUnit.cs` <= 12 namespaces.
         - In `City.cs`: Retyping unit seams to concrete `MapUnit` added `Unciv.Logic.Map.MapUnits`, pushing `City.cs` to 13 namespaces. Decouple an unused namespace or address via coupling ratchet baseline.
    - Adversary: Tested edge cases (unplaced units, negative health, unassigned lateinit fields, carrier slots) are well covered by MapUnitCoreTests.
    - Pragmatist: Clean implementation and seam boundaries.

### T066 MapUnit sub-objects and UnitManager: UnitPromotions, UnitTurnManager, UnitUpgradeManager, UnitManager
- State: Available
- Depends: T065, T060
- Source: core/src/com/unciv/logic/map/mapunit/UnitPromotions.kt, UnitTurnManager.kt, UnitUpgradeManager.kt, civilization/managers/UnitManager.kt
- Fixtures: tests/src/com/unciv/logic/map/UpgradeTests.kt (TestGame-based: T072)
- Deliverables: csharp/Unciv.Core/Logic/Map/MapUnit/*.cs, csharp/Unciv.Core/Logic/Civilization/Managers/UnitManager.cs, tests (+ retype MapUnit/Civilization seams)
- Gate: `dotnet test --filter FullyQualifiedName~UnitManagerTests`
- Claimed:
- Notes: SEAM RULE (set by T053, applies to T053-T072): the Tile -> TileMap -> Civilization -> City -> MapUnit -> GameInfo port order is acyclic because each ticket compiles only against classes that are already Done plus small engine-owned interfaces (`partial` extensions of the `IGameContext*` interfaces in `Models/Ruleset/Unique/GameContext.cs` / `ConditionalsSeams.cs`, or a new `I<Thing>` for types with no IGameContext seam, e.g. managers), in a file named `<Area>Seams.cs`, containing exactly the members the ticket calls. A field/parameter whose Kotlin type is a not-yet-ported class is typed by its seam. The ticket that later ports that class implements the seam, then retypes the earlier tickets' seam-typed fields/params to the concrete class (a mechanical edit of those files, listed in its Deliverables), keeping reference-identity Equals/GetHashCode (Kotlin Civilization/City/MapUnit/Tile have none). Do not add public API Kotlin lacks beyond the seams. TestGame-based fixtures are deferred to T035/T072. ~870 LOC. UnitManager owns `civ.units`; its placement logic (`placeUnitNearTile`, `addUnit`) is what CityConstructions/CityFounder seams call. Unit iteration order is game state.
  - From T060: City.UnitToPromotions is JavaHashMap<string, IUnitPromotions> (marker interface in CitySeams.cs); make UnitPromotions implement/replace it. City.GetGarrison and DestroyCity reach units through the IMapUnitState / IGameContextUnit seams (cast from Tile.MilitaryUnit/AirUnits), to be retyped with MapUnit (T065).

### T067 Unit movement: MovementCost, UnitMovement
- State: Available
- Depends: T065, T054
- Source: core/src/com/unciv/logic/map/mapunit/movement/MovementCost.kt, UnitMovement.kt (and PathsToTilesWithinTurn if present), core/src/com/unciv/logic/map/pathingmap/FixedPointMovement.kt
- Fixtures: tests/src/com/unciv/logic/map/UnitMovementTests.kt, FixedPointMovementTest.kt (TestGame-based: T072)
- Deliverables: csharp/Unciv.Core/Logic/Map/MapUnit/Movement/*.cs, tests (+ retype MapUnit.IUnitMovement)
- Gate: `dotnet test --filter FullyQualifiedName~UnitMovementTests`
- Claimed:
- Notes: SEAM RULE (set by T053, applies to T053-T072): the Tile -> TileMap -> Civilization -> City -> MapUnit -> GameInfo port order is acyclic because each ticket compiles only against classes that are already Done plus small engine-owned interfaces (`partial` extensions of the `IGameContext*` interfaces in `Models/Ruleset/Unique/GameContext.cs` / `ConditionalsSeams.cs`, or a new `I<Thing>` for types with no IGameContext seam, e.g. managers), in a file named `<Area>Seams.cs`, containing exactly the members the ticket calls. A field/parameter whose Kotlin type is a not-yet-ported class is typed by its seam. The ticket that later ports that class implements the seam, then retypes the earlier tickets' seam-typed fields/params to the concrete class (a mechanical edit of those files, listed in its Deliverables), keeping reference-identity Equals/GetHashCode (Kotlin Civilization/City/MapUnit/Tile have none). Do not add public API Kotlin lacks beyond the seams. TestGame-based fixtures are deferred to T035/T072. ~1.2k LOC; delegate the brief. Priority-queue/path tie-breaking must match Kotlin exactly (T001). Float movement costs: do not widen to double. Also lets T040 (MapPathing, CarrierSlotMatcher) replace its seam members with concrete UnitMovement calls: record that follow-up here.
  - Backlog audit: FixedPointMovement.kt (88 LOC, no dependencies; FixedPointMovementTest is in Fixtures) added to Source. The rest of the pathingmap package (PathingMap, PathingMapCache, A* pathfinder, RouteNode) is T093: UnitMovement's path-cache hooks and City.getLandAttackPath/getRoadPath stay seams marked TODO(T093). Timers/timeThis calls are dropped.

### T068 Diplomacy turn and city states: DiplomacyTurnManager, DeclareWar, CityStateFunctions
- State: Available
- Depends: T059, T064, T066
- Source: core/src/com/unciv/logic/civilization/diplomacy/DiplomacyTurnManager.kt, DeclareWar.kt, CityStateFunctions.kt
- Fixtures: tests/src/com/unciv/logic/civilization/diplomacy/* remaining cases (TestGame-based: T072)
- Deliverables: csharp/Unciv.Core/Logic/Civilization/Diplomacy/*.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~DiplomacyTurnTests`
- Claimed:
- Notes: SEAM RULE (set by T053, applies to T053-T072): the Tile -> TileMap -> Civilization -> City -> MapUnit -> GameInfo port order is acyclic because each ticket compiles only against classes that are already Done plus small engine-owned interfaces (`partial` extensions of the `IGameContext*` interfaces in `Models/Ruleset/Unique/GameContext.cs` / `ConditionalsSeams.cs`, or a new `I<Thing>` for types with no IGameContext seam, e.g. managers), in a file named `<Area>Seams.cs`, containing exactly the members the ticket calls. A field/parameter whose Kotlin type is a not-yet-ported class is typed by its seam. The ticket that later ports that class implements the seam, then retypes the earlier tickets' seam-typed fields/params to the concrete class (a mechanical edit of those files, listed in its Deliverables), keeping reference-identity Equals/GetHashCode (Kotlin Civilization/City/MapUnit/Tile have none). Do not add public API Kotlin lacks beyond the seams. TestGame-based fixtures are deferred to T035/T072. ~1.6k LOC (CityStateFunctions is 850); split at claim time if needed. Concrete Civilization, City, MapUnit. Battle/NextTurnAutomation/CityCombatant references: seam or `TODO(T0xx)` naming a real Planned ticket.
  - Backlog audit: CityStateFunctions' NextTurnAutomation calls: TODO(T094). DeclareWar is called by Battle (T083) and TradeLogic (T086).
  - DeclareWar(): DiplomacyManager.DeclareWar() throws NotImplementedException (TODO(T068)); implement it as DeclareWar.declareWar(this, reason) with a DeclareWarReason parameter, and implement the ICityStateFunctions members GetTributeWillingness/CanProvideStat that T059 added to the seam.
  - From T114 (T059 review): DiplomacyManager.DeclareWar() must gain the DeclareWarReason parameter (Kotlin declareWar(declareWarReason = DeclareWarReason(WarType.DirectWar))) when DeclareWar is ported.

### T069 QuestManager and quest helpers
- State: Available
- Depends: T055, T060, T065, T078
- Source: core/src/com/unciv/logic/civilization/managers/quests/QuestManager.kt, QuestTargetHelpers.kt, AssignedQuest.kt, IQuestImplementation.kt, QuestImplementation.kt
- Fixtures: tests/src/com/unciv/logic/civilization/QuestTests.kt (TestGame-based: T072)
- Deliverables: csharp/Unciv.Core/Logic/Civilization/Managers/Quests/*.cs, tests (+ retype Civilization.IQuestManager)
- Gate: `dotnet test --filter FullyQualifiedName~QuestManagerTests`
- Claimed:
- Notes: SEAM RULE (set by T053, applies to T053-T072): the Tile -> TileMap -> Civilization -> City -> MapUnit -> GameInfo port order is acyclic because each ticket compiles only against classes that are already Done plus small engine-owned interfaces (`partial` extensions of the `IGameContext*` interfaces in `Models/Ruleset/Unique/GameContext.cs` / `ConditionalsSeams.cs`, or a new `I<Thing>` for types with no IGameContext seam, e.g. managers), in a file named `<Area>Seams.cs`, containing exactly the members the ticket calls. A field/parameter whose Kotlin type is a not-yet-ported class is typed by its seam. The ticket that later ports that class implements the seam, then retypes the earlier tickets' seam-typed fields/params to the concrete class (a mechanical edit of those files, listed in its Deliverables), keeping reference-identity Equals/GetHashCode (Kotlin Civilization/City/MapUnit/Tile have none). Do not add public API Kotlin lacks beyond the seams. TestGame-based fixtures are deferred to T035/T072. ~840 LOC. Also implements `QuestName -> IQuestImplementation` delegation deferred by T030.
  - Backlog audit: AssignedQuest.kt, IQuestImplementation.kt and QuestImplementation.kt (the QuestName -> IQuestImplementation delegation, ~385 LOC) added to Source; ~1.2k LOC total, split at claim time (QuestManager vs AssignedQuest/QuestImplementation) if needed. QuestImplementation calls CityStateFunctions (T068) and BarbarianManager.getBarbarianEncampmentForQuest (T078 already covered via T065/T066? no: T069 uses it through the seam, T078 supplies it). Add T078 to Depends if the concrete class is needed.
  - Correction to the previous audit bullet: T069 now Depends on T078 (BarbarianManager.getBarbarianEncampmentForQuest is called by QuestImplementation); CityStateFunctions (T068) stays a seam here.

### T070 Civilization TurnManager
- State: Available
- Depends: T068, T064, T066, T069
- Source: core/src/com/unciv/logic/civilization/managers/TurnManager.kt
- Fixtures: tests/src/com/unciv/logic/NextTurnLatencyTest.kt is a benchmark: do not port
- Deliverables: csharp/Unciv.Core/Logic/Civilization/Managers/TurnManager.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~CivTurnTests`
- Claimed:
- Notes: SEAM RULE (set by T053, applies to T053-T072): the Tile -> TileMap -> Civilization -> City -> MapUnit -> GameInfo port order is acyclic because each ticket compiles only against classes that are already Done plus small engine-owned interfaces (`partial` extensions of the `IGameContext*` interfaces in `Models/Ruleset/Unique/GameContext.cs` / `ConditionalsSeams.cs`, or a new `I<Thing>` for types with no IGameContext seam, e.g. managers), in a file named `<Area>Seams.cs`, containing exactly the members the ticket calls. A field/parameter whose Kotlin type is a not-yet-ported class is typed by its seam. The ticket that later ports that class implements the seam, then retypes the earlier tickets' seam-typed fields/params to the concrete class (a mechanical edit of those files, listed in its Deliverables), keeping reference-identity Equals/GetHashCode (Kotlin Civilization/City/MapUnit/Tile have none). Do not add public API Kotlin lacks beyond the seams. TestGame-based fixtures are deferred to T035/T072. ~385 LOC. Wires startTurn/endTurn across the now-concrete managers, cities and units. NextTurnAutomation calls: `TODO(T0xx)` naming the Automation ticket in Planned.
  - Backlog audit: MayaCalendar.startTurnForMaya (TurnManager.kt:59; Civilization-dependent half of T050's MayaCalendar, plus isNewCycle/openPopup data) is ported here; MayaCalendar.cs has a TODO(T050) pointing at a Done ticket, change it to a real implementation (marker removed). NextTurnAutomation.automateGoldToSciencePercentage / automateCityBombardment are TODO(T094).

### T071 GameInfo turn handling and BackwardCompatibility
- State: Available
- Depends: T046, T070
- Source: core/src/com/unciv/logic/GameInfo.kt (nextTurn and turn-related members), BackwardCompatibility.kt (GameInfo/GameInfoPreview extensions)
- Fixtures: tests/src/com/unciv/logic/GameSerializationTests.kt (TestGame-based: T072)
- Deliverables: csharp/Unciv.Core/Logic/GameInfoTurn.cs, BackwardCompatibility.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~GameInfoTurnTests`
- Claimed:
- Notes: SEAM RULE (set by T053, applies to T053-T072): the Tile -> TileMap -> Civilization -> City -> MapUnit -> GameInfo port order is acyclic because each ticket compiles only against classes that are already Done plus small engine-owned interfaces (`partial` extensions of the `IGameContext*` interfaces in `Models/Ruleset/Unique/GameContext.cs` / `ConditionalsSeams.cs`, or a new `I<Thing>` for types with no IGameContext seam, e.g. managers), in a file named `<Area>Seams.cs`, containing exactly the members the ticket calls. A field/parameter whose Kotlin type is a not-yet-ported class is typed by its seam. The ticket that later ports that class implements the seam, then retypes the earlier tickets' seam-typed fields/params to the concrete class (a mechanical edit of those files, listed in its Deliverables), keeping reference-identity Equals/GetHashCode (Kotlin Civilization/City/MapUnit/Tile have none). Do not add public API Kotlin lacks beyond the seams. TestGame-based fixtures are deferred to T035/T072. Remaining GameInfo members not taken by T046, plus the BackwardCompatibility work moved here from T046 (all extensions of GameInfo/GameInfoPreview/ModOptions/TechManager; call from `setTransients`). Fulfils the T029 note.
  - Backlog audit: GameInfo.recordRankingStats needs CivRankingHistory.recordRankingStats(Civilization) (deferred by T051); take it here unless T046 did. Save/load of the result is T100, GameStarter T099.

### T072 Deferred TestGame-based fixtures sweep (Tile, Civilization, City, MapUnit, Quest)
- State: Available
- Depends: T035, T052, T062, T063, T064, T067, T068, T069, T071
- Source: none (ports tests only)
- Fixtures: tests/src/com/unciv/logic/map/FreshWaterTests.kt, TileImprovementConstructionTests.kt, VisibilityTests.kt, UpgradeTests.kt, UnitMovementTests.kt, FixedPointMovementTest.kt, UnitFormationTests.kt, tests/src/com/unciv/logic/city/*, tests/src/com/unciv/logic/civilization/** (incl. diplomacy), GameSerializationTests.kt, PathfindingTests.kt and the deferred cases listed by T055-T071
- Deliverables: csharp/Unciv.Core.Tests/** ported tests
- Gate: `dotnet test --filter FullyQualifiedName~Fixtures`
- Claimed:
- Notes: Created because T035 (TestGame) can only exist once the whole chain is ported, and each earlier ticket deferred its TestGame-based fixtures. Port them over the real TestGame, one test class per Kotlin class; any real behavioural bug found goes back to the owning ticket as a review note. Split by Kotlin test directory at claim time if too large. Also ports the Countables/Conditionals tests T028 deferred that need real game state, if T035 did not.

### T073 MapGenerator A: MapGenerationRandomness, MapResourceSetting, MapElevationGenerator, MapLandmassGenerator
- State: Done
- Depends: T039, T010, T016, T017
- Source: core/src/com/unciv/logic/map/mapgenerator/MapGenerationRandomness.kt, MapResourceSetting.kt, MapElevationGenerator.kt, MapLandmassGenerator.kt
- Fixtures: tests/src/com/unciv/logic/map/MapGenerationTests.kt
- Deliverables: csharp/Unciv.Core/Logic/Map/MapGenerator/*.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~MapGenerat`
- Claimed: 2026-10-24T10:26Z
- Notes: Sub-ticket of T041 (split at claim time, source ~5k LOC). Perlin.cs already exists. Same seed must reproduce Kotlin; compile against seam members for anything owned by unported tickets (SEAM RULE in T053).
  - IMPLEMENTED (T073): MapGenerationRandomness.cs, MapElevationGenerator.cs, MapLandmassGenerator.cs, MapGeneratorSeams.cs in Logic/Map/MapGenerator (namespace Unciv.Logic.Map.MapGenerator); tests in Unciv.Core.Tests/Logic/Map/MapGenerator/MapGenerationTests.cs. MapResourceSetting was already ported in MapParameters.cs (T022): no new file. KotlinMath gained DoubleToLong and Pow(float,float) (Float.pow = Math.pow rounded to float). Deviations: (1) MapGenerator.kt is T041's, so MapGeneratorSeams.cs holds `TerrainOccursRange` (internal; everything except the `(Terrain, Unique)` ctor and Terrain.getGenerationConditions, which T041 adds) and `MapGeneratorSeams.GetTileRadius` (= MapGenerator.getTileRadius); T041 moves both into MapGenerator and deletes the file. (2) MapLandmassGenerator.GetInitializationTerrain is internal here and a private copy remains in TileMap.cs (T039); T041 may point TileMap at this one. (3) getNoise's `noiseFunction` lambda is a Func delegate; Kotlin default args kept. (4) chooseSpreadOutLocations: baseTerrainsToChosenTiles is a JavaHashMap (Java iteration order matters for the stable sortedBy tie-break), suitableTilesGrouped sets are LinkedHashSet. (5) createSpiral: Float hypot is Math.Sqrt(x*x+y*y) in double then rounded to float (no Math.Hypot in .NET); Kotlin Exception -> InvalidOperationException (getInitializationTerrain water fallback catches it). (6) retryLoweringWaterLevel takes (function, predicate) as C# cannot put an optional lambda before a trailing one. Fixtures: MapGenerationTests.kt needs the full MapGenerator (T041); its all-types x shapes x wrap loop is ported against the landmass generator with a hand-built ruleset, the terrain-count and sizeForAuto tests stay with T041. Same-seed-as-Kotlin golden values were not available (Kotlin tests only check aggregates). Gate and Standard checks pass (1242 tests).

### T074 MapGenerator B: RiverGenerator, NaturalWonderGenerator
- State: Done
- Depends: T039, T010, T016, T017
- Source: core/src/com/unciv/logic/map/mapgenerator/RiverGenerator.kt, NaturalWonderGenerator.kt
- Fixtures: tests/src/com/unciv/logic/map/mapgenerator/NaturalWonderGeneratorTests.kt
- Deliverables: csharp/Unciv.Core/Logic/Map/MapGenerator/*.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~MapGenerat`
- Claimed: 2026-10-24T10:32Z
- Notes: Sub-ticket of T041 (split at claim time, source ~5k LOC). Perlin.cs already exists. Same seed must reproduce Kotlin; compile against seam members for anything owned by unported tickets (SEAM RULE in T053).
  - IMPLEMENTED (T074): RiverGenerator.cs, NaturalWonderGenerator.cs, NaturalWonderSeams.cs, tests NaturalWonderGeneratorTests.cs (3 Kotlin fixtures ported with hand-built ruleset, TestGame is T072; plus FitsTerrainUniques, SpawnNaturalWonders and RiverGenerator tests). Seams (extension methods in NaturalWonderSeams.cs so T054/T041 instance members win; they delete the file): Tile.SetBaseTerrain (skips TileNormalizer.normalizeToRuleset, TODO T054), Tile.RemoveImprovement (SetImprovementBasic(null) + clears pillaged; skips visibility/city/quest updates, T054), Tile.SetConnectedByRiver(.., convertTerrains) (flag ignored, T041 MapGenerator.Helpers.convertTerrains). RiverDirections enum uses clock positions as values with an explicit entries list (Kotlin order). Lazy sequences materialised in the same order; groupBy+minBy replaced by min-key filter (same element order). Float/Double arithmetic kept (weight/totalWeight float sum compared to double).
  - Update after rebase onto T054: Tile.SetBaseTerrain/RemoveImprovement now real (T054); NaturalWonderSeams.cs only keeps the SetConnectedByRiver(convertTerrains) seam (T041).

### T075 MapGenerator C: mapregions Region, MapGenTileData, MapRegions, RegionStartFinder
- State: Done
- Depends: T039, T010, T016, T017
- Source: core/src/com/unciv/logic/map/mapgenerator/mapregions/Region.kt, MapGenTileData.kt, MapRegions.kt, RegionStartFinder.kt
- Fixtures: tests/src/com/unciv/logic/map/MapGenerationTests.kt
- Deliverables: csharp/Unciv.Core/Logic/Map/MapGenerator/MapRegions/*.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~MapGenerat`
- Claimed: 2026-10-24T10:32Z
- Notes: Sub-ticket of T041 (split at claim time, source ~5k LOC). Perlin.cs already exists. Same seed must reproduce Kotlin; compile against seam members for anything owned by unported tickets (SEAM RULE in T053).
  - Ported Region, MapGenTileData, MapRegions (+TileDataMap, getTileFertility, getRegionPriority, anonymizeUnique, isWaterOnlyResource, getTerrainRule as MapRegionFunctions), RegionStartFinder into Unciv.Logic.Map.MapGenerator.Mapregions (namespace "Mapregions" to avoid clashing with class MapRegions). Source LOC was ~950, no split needed.
    Seams (Mapregions/MapRegionsSeams.cs): IMapRegionsCivilization (T055: CivName, CivId, GetStartBias, StateBasedRandom) replaces Civilization; MapRegionsSeams.NormalizeStart (T076), PlaceMinorCivs (T076), AssignLuxuries/PlaceLuxuries/PlaceStrategicAndBonuses (T077) throw NotSupportedException until those tickets replace them. So AssignRegions / PlaceResourcesAndMinorCivs are untested end-to-end until T076/T077 (AssignRegions calls NormalizeStart).
    Deviations: libGDX Rectangle is a readonly struct so Region.Rect is a settable property. Kotlin HashSet<Tile> (identity hash, order not reproducible on the JVM either) -> insertion-ordered LinkedHashSet; maxByOrNull/minByOrNull = first extremum (MapRegionsSelection helpers). civsAddedToContinent and Region.TerrainCounts are JavaHashMap (iteration order feeds results / toString). Region implements IGameContextRegion incl. libGDX Rectangle.hashCode.
    Trap: HexMath.getRow truncates toward zero (faithful), so adjacent regions can share a row of tiles at negative rows; Region.Tiles overlap is Kotlin behaviour, not a bug. No Kotlin tests cover these classes; tests in Unciv.Core.Tests/Logic/Map/MapGenerator/Mapregions/MapRegionsTests.cs.

### T076 MapGenerator D: mapregions StartNormalizer, MinorCivPlacer
- State: Done
- Depends: T075
- Source: core/src/com/unciv/logic/map/mapgenerator/mapregions/StartNormalizer.kt, MinorCivPlacer.kt
- Fixtures: tests/src/com/unciv/logic/map/MapGenerationTests.kt
- Deliverables: csharp/Unciv.Core/Logic/Map/MapGenerator/MapRegions/*.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~MapGenerat`
- Claimed: 2026-10-24T10:42Z
- Notes: Sub-ticket of T041 (split at claim time, source ~5k LOC). Perlin.cs already exists. Same seed must reproduce Kotlin; compile against seam members for anything owned by unported tickets (SEAM RULE in T053).
  - T076 implemented: StartNormalizer.cs, MinorCivPlacer.cs (Mapregions/), MapRegionsSeams NormalizeStart/PlaceMinorCivs now call them; tests in MinorCivPlacerTests.cs.
    Deviations: (1) Kotlin unseeded shuffled()/random() (Random.Default) draw from a state-based GameRandom per call site (StartNormalizer.<fn>.unseeded; MinorCivPlacer uses civs[0].StateBasedRandom), so those exact draws cannot match Kotlin by construction. (2) Civilization is IMapRegionsCivilization. (3) TileResource.generatesNaturallyOn is not ported on TileResource: private GeneratesNaturallyOn copy in StartNormalizer.cs; T077 (or whoever ports it onto TileResource) should switch StartNormalizer to it and delete the copy. (4) NEW SEAM for T077: Mapregions/MapRegionResourcesSeams.cs (TryAddingResourceToTiles, PlaceResourcesInTiles) throws NotSupportedException until T077 replaces the bodies with MapRegionResources calls; until then StartNormalizer with strategic balance / major civ low production is unusable. Tile.SetTileResource gets rng explicitly (no Random.Default).

### T077 MapGenerator E: resourceplacement (Luxury, StrategicBonus, MapRegionResources)
- State: Done
- Depends: T075
- Source: core/src/com/unciv/logic/map/mapgenerator/resourceplacement/*.kt
- Fixtures: tests/src/com/unciv/logic/map/ResourceDiversityTest.kt
- Deliverables: csharp/Unciv.Core/Logic/Map/MapGenerator/ResourcePlacement/*.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~MapGenerat`
- Claimed: 2026-10-24T10:42Z
- Notes: Sub-ticket of T041 (split at claim time, source ~5k LOC). Perlin.cs already exists. Same seed must reproduce Kotlin; compile against seam members for anything owned by unported tickets (SEAM RULE in T053).
  - Ported MapRegionResources, LuxuryResourcePlacementLogic, StrategicBonusResourcePlacementLogic into Logic/Map/MapGenerator/ResourcePlacement (namespace ...ResourcePlacement). MapRegionsSeams.AssignLuxuries/PlaceLuxuries/PlaceStrategicAndBonuses now delegate to them. No split (source 931 LOC).
    Also added TileResource.GeneratesNaturallyOn (was deferred in TileResource.cs; required by placement).
    Deviations: Kotlin argument-less shuffled() uses unseeded Random.Default (nondeterministic on the JVM too); the engine bans that, so each call site shuffles with GameContext(gameInfo).StateBasedRandom("<site>.shuffled"). All seeded draws (stateBasedRandom, randomWeighted) keep Kotlin's sequence and call order. Sequence.shuffled() stays lazy and reshuffles per enumeration (ShuffledSequence).
    Quirks kept: totalPlaced += map is putAll (replaces); regionTargetNumber = min(civCount-3, ...); fallbackStrategic precedence (A&&B)||C; region priority sort puts null first, stable; float sums are sequential Float adds.
    ResourceDiversityTest.kt needs the full MapGenerator (T041); own xUnit tests exercise the logic directly on a small all-land fallback ruleset.

### T078 Barbarians: BarbarianManager, BarbarianEncampment
- State: Available
- Depends: T060, T065, T066
- Source: core/src/com/unciv/logic/automation/civilization/BarbarianManager.kt, BarbarianEncampment.kt (the data/spawn logic; BarbarianAutomation.kt is T094)
- Fixtures: none - generate golden values (seeded encampment spawning)
- Deliverables: csharp/Unciv.Core/Logic/Civilization/BarbarianManager.cs, BarbarianEncampment.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~BarbarianManagerTests`
- Claimed:
- Notes: Created by the backlog audit: GameInfo.barbarians is a serialized GameInfo field (T046 depends on this ticket), and no ticket covered it. ~330 LOC. Random draws via the engine RNG in Kotlin order; encampment/unit iteration order is game state. Seam rule as T053: earlier tickets' call sites are TODO(T###) or seam-typed; this ticket implements the seam and retypes the callers (list them in Deliverables when claiming). Standard format; ~LOC given in title where known. TestGame-based Kotlin tests need T035.

### T079 UnitAction model and unit actions A: UnitAction, UnitActionModifiers, UnitActionsPillage, UnitActionsUpgrade, UnitActionsGreatPerson, UnitActionsReligion
- State: Available
- Depends: T052, T057, T063, T066, T067
- Source: core/src/com/unciv/models/UnitAction.kt, core/src/com/unciv/logic/map/mapunit/actions/UnitActionModifiers.kt, UnitActionsPillage.kt, UnitActionsUpgrade.kt, UnitActionsGreatPerson.kt, UnitActionsReligion.kt
- Fixtures: none directly (UpgradeTests are T072)
- Deliverables: csharp/Unciv.Core/Models/UnitAction.cs, csharp/Unciv.Core/Logic/Map/MapUnit/Actions/*.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~UnitActionsATests`
- Claimed:
- Notes: Created by the backlog audit: no ticket covered mapunit/actions/* or models/UnitAction.kt. ~830 LOC. UnitAction keeps only data (type, title key, action lambda becomes a command/delegate; UI icon/confirmation fields are data). UnitActionsPillage is called by Battle (T083). UI hooks (UncivGame.settings, GUI) are cut per Non-goals #7. Seam rule as T053: earlier tickets' call sites are TODO(T###) or seam-typed; this ticket implements the seam and retypes the callers (list them in Deliverables when claiming). Standard format; ~LOC given in title where known. TestGame-based Kotlin tests need T035.
  - T057 left Logic/Map/MapUnit/Actions/UnitActionModifiers.cs with only `GetUsableUnitActionUniques(IMapUnitState, UniqueType)` as a stub (TODO(T079)) returning an empty sequence; ReligionManager.MaySpreadReligionAtAll calls it. Implement the real class in this file and retype the parameter to MapUnit.

### T080 Unit actions B: UnitActions, UnitActionsFromUniques
- State: Available
- Depends: T079, T061, T068
- Source: core/src/com/unciv/logic/map/mapunit/actions/UnitActions.kt, UnitActionsFromUniques.kt
- Fixtures: none
- Deliverables: csharp/Unciv.Core/Logic/Map/MapUnit/Actions/UnitActions.cs, UnitActionsFromUniques.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~UnitActionsBTests`
- Claimed:
- Notes: Created by the backlog audit. ~755 LOC. UnitActions calls UnitAutomation (automatedExplore/automateUnitMoves via GameView): leave a TODO(T090) seam. GUI.getWorldScreen/UncivGame.settings.* are UI and dropped; UnitActionsFromUniques.getFoundCityAction uses CityFounder (T061). Seam rule as T053: earlier tickets' call sites are TODO(T###) or seam-typed; this ticket implements the seam and retypes the callers (list them in Deliverables when claiming). Standard format; ~LOC given in title where known. TestGame-based Kotlin tests need T035.

### T081 Battle A: ICombatant, MapUnitCombatant, CityCombatant, BattleConstants, AttackableTile, BattleDamage
- State: Available
- Depends: T060, T065, T067, T062, T035
- Source: core/src/com/unciv/logic/battle/ICombatant.kt, MapUnitCombatant.kt, CityCombatant.kt, BattleConstants.kt, AttackableTile.kt, BattleDamage.kt
- Fixtures: tests/src/com/unciv/logic/battle/BattleDamageTest.kt
- Deliverables: csharp/Unciv.Core/Logic/Battle/*.cs (CombatAction.cs already exists from T027), tests
- Gate: `dotnet test --filter FullyQualifiedName~BattleDamageTests`
- Claimed:
- Notes: Created by the backlog audit (Battle was only in the stale Planned paragraph). ~650 LOC. BattleDamage.kt keeps the CombatAction enum already in Logic/Battle/CombatAction.cs. UncivSound fields stay as the T049 enum. Float vs Int damage arithmetic and rounding must match Kotlin exactly. Seam rule as T053: earlier tickets' call sites are TODO(T###) or seam-typed; this ticket implements the seam and retypes the callers (list them in Deliverables when claiming). Standard format; ~LOC given in title where known. TestGame-based Kotlin tests need T035.

### T082 Battle B: attack recording, TargetHelper, BattleUnitCapture, AirInterception, GreatGeneralImplementation
- State: Available
- Depends: T081, T046, T067, T035
- Source: core/src/com/unciv/logic/battle/AttackEvent.kt, AttackParticipant.kt, AttackRecorder.kt, TargetHelper.kt, BattleUnitCapture.kt, AirInterception.kt, GreatGeneralImplementation.kt
- Fixtures: tests/src/com/unciv/logic/battle/AttackEventTest.kt, AttackRecorderTest.kt, TargetHelperTest.kt, tests/src/com/unciv/testing/AttackRecorderTestHelper.kt
- Deliverables: csharp/Unciv.Core/Logic/Battle/*.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~BattleSupportTests`
- Claimed:
- Notes: Created by the backlog audit. ~900 LOC. GreatGeneralImplementation calls SpecificUnitAutomation/HeadTowardsEnemyCityAutomation.getEnemyCitiesByPriority: seam, TODO(T091)/TODO(T090). AttackEvent/AttackParticipant are IsPartOfGameInfoSerialization JSON (field names). TargetHelper timeThis dropped (T027 policy). Seam rule as T053: earlier tickets' call sites are TODO(T###) or seam-typed; this ticket implements the seam and retypes the callers (list them in Deliverables when claiming). Standard format; ~LOC given in title where known. TestGame-based Kotlin tests need T035.

### T083 Battle C: Battle, Nuke
- State: Available
- Depends: T081, T082, T052, T064, T068, T079, T035
- Source: core/src/com/unciv/logic/battle/Battle.kt, Nuke.kt
- Fixtures: tests/src/com/unciv/logic/battle/BattleTest.kt
- Deliverables: csharp/Unciv.Core/Logic/Battle/Battle.cs, Nuke.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~BattleTests`
- Claimed:
- Notes: Created by the backlog audit. ~1.2k LOC (Battle.kt 883): delegate the brief. Uses UnitActionsPillage (T079), CityConquestFunctions (T064), DeclareWar (T068), UniqueTriggerActivation (T052); NextTurnAutomation.valueCityStateAlliance is a TODO(T094) seam; UncivGame/worldScreen/tutorial-task lines are UI and dropped. Attack order and RNG draw order are game state. Seam rule as T053: earlier tickets' call sites are TODO(T###) or seam-typed; this ticket implements the seam and retypes the callers (list them in Deliverables when claiming). Standard format; ~LOC given in title where known. TestGame-based Kotlin tests need T035.

### T084 Views A: View, GameView, OwnedView, CivView, ForeignCivView, TileView, TileMapView, DiplomacyManagerView, TradeView, SpyView
- State: Available
- Depends: T046, T057, T059
- Source: core/src/com/unciv/view/View.kt, GameBasedView.kt, OwnedView.kt, GameView.kt, CivView.kt, ForeignCivView.kt, TileView.kt, TileMapView.kt, DiplomacyManagerView.kt, TradeView.kt, SpyView.kt
- Fixtures: tests/src/com/unciv/view/ViewPurityTest.kt (Kotlin purity annotations: port only the structural checks that make sense), GameViewMovementHistoryTest.kt (TestGame: T103)
- Deliverables: csharp/Unciv.Core/View/*.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~ViewTests`
- Claimed:
- Notes: Created by the backlog audit: core/src/com/unciv/view/* (1.6k LOC) is the read-only API that every Automation file, Battle, MapUnit, TurnManager, UniqueTriggerActivation and TradeEvaluation call (GameView(gameInfo, civ).civView / getMapUnitView) and no ticket covered it. ~730 LOC here. Drop the yairm210.purity annotations (record them as comments). Seam rule as T053: earlier tickets' call sites are TODO(T###) or seam-typed; this ticket implements the seam and retypes the callers (list them in Deliverables when claiming). Standard format; ~LOC given in title where known. TestGame-based Kotlin tests need T035.
  - From T114 (T059 review): TradeOffer.getOfferText is not ported (translation + Fonts + UncivGame.Current.gameInfo); port it with the Trade view/UI text (or a Translation port) and test it.

### T085 Views B: CityView, ForeignCityView, CityConstructionsView, MapUnitView, ForeignMapUnitView, CombatantView, AttackableTileView
- State: Available
- Depends: T084, T081, T062, T063, T066, T067
- Source: core/src/com/unciv/view/CityView.kt, ForeignCityView.kt, CityConstructionsView.kt, MapUnitView.kt, ForeignMapUnitView.kt, CombatantView.kt, AttackableTileView.kt
- Fixtures: tests/src/com/unciv/view/ViewPurityTest.kt (shared with T084)
- Deliverables: csharp/Unciv.Core/View/*.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~ViewCityUnitTests`
- Claimed:
- Notes: Created by the backlog audit. ~885 LOC. See T084 for why Views exist. Seam rule as T053: earlier tickets' call sites are TODO(T###) or seam-typed; this ticket implements the seam and retypes the callers (list them in Deliverables when claiming). Standard format; ~LOC given in title where known. TestGame-based Kotlin tests need T035.

### T086 TradeLogic
- State: Available
- Depends: T059, T068, T057
- Source: core/src/com/unciv/logic/trade/TradeLogic.kt
- Fixtures: none - generate golden values (trade acceptance, offer filtering)
- Deliverables: csharp/Unciv.Core/Logic/Trade/TradeLogic.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~TradeLogicTests`
- Claimed:
- Notes: Created by the backlog audit (TradeLogic was 'Planned, after T068'). ~290 LOC. TradeOfferType.kt is ported by T059 together with TradeOffer. TradeEvaluation (T089) is separate because it needs the diplomacy automation (T088).
  - DiplomacyManager.MakePeaceOneSide builds a plain Trade instead of TradeLogic(...).currentTrade; switch it to TradeLogic when this lands (optional, behaviour is identical).

### T087 Automation core: Automation, AutomationCache, ThreatManager, CityLocationTileRanker, ReligionAutomation, EspionageAutomation
- State: Available
- Depends: T085, T057
- Source: core/src/com/unciv/logic/automation/Automation.kt, Timers.kt (profiling: drop, keep nothing), civilization/AutomationCache.kt, civilization/ThreatManager.kt, unit/CityLocationTileRanker.kt, civilization/ReligionAutomation.kt, unit/EspionageAutomation.kt
- Fixtures: tests/src/com/unciv/logic/civilization/managers/ThreatManagerTests.kt (TestGame: T035 via T085)
- Deliverables: csharp/Unciv.Core/Logic/Automation/**/*.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~AutomationCoreTests`
- Claimed:
- Notes: Created by the backlog audit (Automation was only 'Planned, last'). ~1.9k LOC: split by file at claim time. Also owns IHasUniques.getWeightForAiDecision plus the Personality weighting deferred by T027/T032 (marker TODO(T033) in IHasUniques.cs is stale: fix it here) and the Automation.* call sites left as TODO in CityFocus (T060), CityPopulationManager (T061), CityConstructions (T063). DebugUtils flags come from the T048 engine options (SETTLER_SCORES in CityLocationTileRanker). Seam rule as T053: not-yet-ported callees/callers in other automation tickets are TODO(T###) seams that name the owning ticket below; the owning ticket implements the seam and retypes the callers. Automation uses GameView (T084/T085). timeThis profiling is dropped (T027 policy).

### T088 Diplomacy AI: MotivationToAttackAutomation, DeclareWarPlanEvaluator, DeclareWarTargetAutomation, DiplomacyAutomation, TradeAutomation
- State: Available
- Depends: T087, T086, T081, T068
- Source: core/src/com/unciv/logic/automation/civilization/MotivationToAttackAutomation.kt, DeclareWarPlanEvaluator.kt, DeclareWarTargetAutomation.kt, DiplomacyAutomation.kt, TradeAutomation.kt
- Fixtures: none - generate golden values
- Deliverables: csharp/Unciv.Core/Logic/Automation/Civilization/*.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~DiplomacyAiTests`
- Claimed:
- Notes: Created by the backlog audit. ~1.6k LOC: split by file at claim time. DiplomacyAutomation/TradeAutomation call TradeEvaluation (T089, ported after this ticket because TradeEvaluation calls back into these): seam TODO(T089). Demand.kt (T059) calls DiplomacyAutomation: replace its TODO(T088). Seam rule as T053: not-yet-ported callees/callers in other automation tickets are TODO(T###) seams that name the owning ticket below; the owning ticket implements the seam and retypes the callers. Automation uses GameView (T084/T085). timeThis profiling is dropped (T027 policy).

### T089 TradeEvaluation
- State: Available
- Depends: T086, T088, T085
- Source: core/src/com/unciv/logic/trade/TradeEvaluation.kt
- Fixtures: none - generate golden values (offer valuation per TradeOfferType)
- Deliverables: csharp/Unciv.Core/Logic/Trade/TradeEvaluation.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~TradeEvaluationTests`
- Claimed:
- Notes: Created by the backlog audit (was 'Planned, after T068'). ~580 LOC; uses DiplomacyAutomation, MotivationToAttackAutomation, DeclareWarPlanEvaluator (T088) through GameView. Wires the TODO(T089) seams in T088. Float/Int valuation arithmetic must match Kotlin.
  - DiplomacyManager.GetGoldInflation (private) is a copy of TradeEvaluation.getGoldInflation; replace it with the TradeEvaluation call.

### T090 Unit automation core: UnitAutomation, BattleHelper, HeadTowardsEnemyCityAutomation, AirUnitAutomation, UseGoldAutomation
- State: Available
- Depends: T087, T088, T082, T083
- Source: core/src/com/unciv/logic/automation/unit/UnitAutomation.kt, BattleHelper.kt, HeadTowardsEnemyCityAutomation.kt, AirUnitAutomation.kt, civilization/UseGoldAutomation.kt
- Fixtures: none - generate golden values
- Deliverables: csharp/Unciv.Core/Logic/Automation/Unit/*.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~UnitAutomationTests`
- Claimed:
- Notes: Created by the backlog audit. ~1.5k LOC: split by file at claim time. Wires MapUnit.doAction (T065), UnitActions (T080) and GreatGeneralImplementation (T082) call sites left as TODO(T090). Calls to later automation tickets (CivilianUnitAutomation, SpecificUnitAutomation, WorkerAutomation T091/T092, NextTurnAutomation T094) are TODO seams. Seam rule as T053: not-yet-ported callees/callers in other automation tickets are TODO(T###) seams that name the owning ticket below; the owning ticket implements the seam and retypes the callers. Automation uses GameView (T084/T085). timeThis profiling is dropped (T027 policy).

### T091 Civilian automation: CivilianUnitAutomation, SpecificUnitAutomation, ReligiousUnitAutomation, ConstructionAutomation
- State: Available
- Depends: T090, T080, T063
- Source: core/src/com/unciv/logic/automation/unit/CivilianUnitAutomation.kt, SpecificUnitAutomation.kt, ReligiousUnitAutomation.kt, city/ConstructionAutomation.kt
- Fixtures: none - generate golden values
- Deliverables: csharp/Unciv.Core/Logic/Automation/Unit/*.cs, csharp/Unciv.Core/Logic/Automation/City/ConstructionAutomation.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~CivilianAutomationTests`
- Claimed:
- Notes: Created by the backlog audit. ~1.2k LOC. ConstructionAutomation is called by CityConstructions.chooseNextConstruction (T063, TODO(T091)). Wires the SpecificUnitAutomation seam of GreatGeneralImplementation (T082). Seam rule as T053: not-yet-ported callees/callers in other automation tickets are TODO(T###) seams that name the owning ticket below; the owning ticket implements the seam and retypes the callers. Automation uses GameView (T084/T085). timeThis profiling is dropped (T027 policy).

### T092 Worker and road automation: WorkerAutomation, RoadBetweenCitiesAutomation, RoadToAutomation
- State: Available
- Depends: T091, T080, T035
- Source: core/src/com/unciv/logic/automation/unit/WorkerAutomation.kt, RoadBetweenCitiesAutomation.kt, RoadToAutomation.kt
- Fixtures: tests/src/com/unciv/logic/automation/unit/WorkerAutomationTest.kt
- Deliverables: csharp/Unciv.Core/Logic/Automation/Unit/*.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~WorkerAutomationTests`
- Claimed:
- Notes: Created by the backlog audit. ~1.2k LOC. Civilization.workerAutomation (T055 seam) is TODO(T092). Tile/improvement choice order is game state. Seam rule as T053: not-yet-ported callees/callers in other automation tickets are TODO(T###) seams that name the owning ticket below; the owning ticket implements the seam and retypes the callers. Automation uses GameView (T084/T085). timeThis profiling is dropped (T027 policy).

### T093 PathingMap: PathingMap, PathingMapCache, PathingMapAStarPathfinder, RouteNode, PrioritizedNode
- State: Available
- Depends: T067, T088, T090, T092
- Source: core/src/com/unciv/logic/map/pathingmap/PathingMap.kt, PathingMapCache.kt, PathingMapAStarPathfinder.kt, RouteNode.kt, PrioritizedNode.kt (FixedPointMovement.kt is ported by T067)
- Fixtures: tests/src/com/unciv/logic/map/pathingmap/PathingMapTest.kt, RouteNodeTest.kt (TestGame: T035 via T092)
- Deliverables: csharp/Unciv.Core/Logic/Map/PathingMap/*.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~PathingMapTests`
- Claimed:
- Notes: Created by the backlog audit (T040 deferred these to 'T067/T072' but neither listed them). ~1.5k LOC. Retype the seams left by City.getLandAttackPath/getAmphibiousAttackPath/getRoadPath (T060) and UnitMovement's path-cache hooks (T067) to the concrete classes. Priority queue / path tie-breaking must match Kotlin (T001); Float movement costs stay float. ALWAYS_LOG/VERBOSE_PATHFINDING_LOGS flags come from engine options (T048) or are dropped. Seam rule as T053: not-yet-ported callees/callers in other automation tickets are TODO(T###) seams that name the owning ticket below; the owning ticket implements the seam and retypes the callers. Automation uses GameView (T084/T085). timeThis profiling is dropped (T027 policy).
  - From T060: City.getLandAttackPath/getAmphibiousAttackPath/getRoadPath/getRoadPathToAny, the three PathingMap caches (landAttackPathing, amphibiousAttackPathing, potentialRoadPathing) and City.clearCaches were NOT ported with City (PathingMap does not exist yet); add them to Logic/City/City.cs here (and TODO-free).

### T094 NextTurnAutomation and BarbarianAutomation (automation integration)
- State: Available
- Depends: T088, T090, T091, T092, T078, T083, T035
- Source: core/src/com/unciv/logic/automation/civilization/NextTurnAutomation.kt, BarbarianAutomation.kt
- Fixtures: tests/src/com/unciv/logic/automation/civilization/NextTurnAutomationTest.kt
- Deliverables: csharp/Unciv.Core/Logic/Automation/Civilization/NextTurnAutomation.cs, BarbarianAutomation.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~NextTurnAutomationTests`
- Claimed:
- Notes: Created by the backlog audit. ~855 LOC. Final automation ticket: replaces every TODO(T094) seam (TurnManager.automate and cityBombardment/goldToScience T070, CityStateFunctions T068, Battle.valueCityStateAlliance T083, UniqueTriggerActivation.chooseGreatPerson T052, Civilization.automateTurn) and runs one full AI turn through all automation tickets. Seam rule as T053: not-yet-ported callees/callers in other automation tickets are TODO(T###) seams that name the owning ticket below; the owning ticket implements the seam and retypes the callers. Automation uses GameView (T084/T085). timeThis profiling is dropped (T027 policy).

### T095 Civilopedia text model A: FormattedLine, ICivilopediaText, CivilopediaCategories, FormattedLineListBuilder, DescriptionHelpers
- State: Done
- Depends: T034
- Source: core/src/com/unciv/ui/screens/civilopediascreen/FormattedLine.kt, ICivilopediaText.kt, CivilopediaCategories.kt, core/src/com/unciv/ui/objectdescriptions/FormattedLineListBuilder.kt, DescriptionHelpers.kt
- Fixtures: none - generate golden values (line text/link/format data)
- Deliverables: csharp/Unciv.Core/UI/Screens/Civilopedia/*.cs, csharp/Unciv.Core/UI/ObjectDescriptions/*.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~CivilopediaTextTests`
- Claimed: 2026-10-24T10:53Z
- Notes: Created by the backlog audit: Non-goals #7 says to decide the Civilopedia/FormattedLine engine text model once, and T030/T031/T032/T033 deferred their getCivilopediaTextLines/makeLink/getSortGroup/getSubCategory/getShortDescription members to 'the Civilopedia text types, which have no ticket yet'. Decision: the engine owns the data model (FormattedLine as plain data: text, link, icon NAME strings, colour as EngineColor/hex, size/format flags; no Image/Actor/Table rendering, no ImageGetter, no KeyboardBinding), the Godot client renders it. ~1.1k LOC. Port ICivilopediaText as an interface with default methods like IHasUniques. T095-T097 also finish the deferred members on the ruleset objects (list the ones taken in Notes). Seam rule as T053.
  - IMPLEMENTED (T095). Files: UI/Screens/Civilopedia/{FormattedLine,ICivilopediaText(+SimpleCivilopediaText),CivilopediaCategories}.cs, UI/ObjectDescriptions/{FormattedLineListBuilder,DescriptionHelpers}.cs, UI/EngineColors.cs (libGDX Colors name table + Color.valueOf), tests UI/Civilopedia/CivilopediaTextTests.cs. Gate + Standard checks pass (1472 tests). Design: FormattedLine is plain data (sealed class, [JsonConstructor] on the all-optional ctor); rendering/IconDisplay/align/layout constants/pixmap crop are client concerns and dropped. Seams: the Kotlin `UncivGame.Current` ruleset/gameInfo is an explicit `CivilopediaContext(Ruleset, IGameContextGameInfo?)` passed to FormattedLine(Unique, ctx), GetCivilopediaTextLines, AssembleCivilopediaText, the builder and UniquesToCivilopediaTextLines; the validator's ImageGetter/uncachedImageExists checks are `IFormattedLineImageCheck` passed to UnsupportedReasons (returns IEnumerable<string>).
    DEVIATIONS: (1) nested enum FormattedLine.LinkType is `LinkTypeEnum` (C# forbids a property with the same name as a nested type). (2) SeparatorType is top-level, the builder interface is `IFormattedLineListBuilder` and the entry point is static `FormattedLineListBuilder.BuildCivilopediaText(ctx, block, defaults, capacity)`; Kotlin default arguments from `defaults()` are nullable params (null = take from defaults). (3) leadingSeparator lambda param of UniquesToCivilopediaTextLines: null = one empty line, `noLeadingSeparator: true` = Kotlin null. (4) CivilopediaCategories is an enum + extension methods (Label, HeaderIcon, GetCategoryIterator, FromLink); getImage and KeyboardBinding binding dropped (client). GetCategoryIterator(Belief) lacks the two virtual entries (T108). (5) Kotlin FormattedLine equals/hashCode throw: kept (InvalidOperationException). (6) `UnsupportedReasons`: the Kotlin `icon` check tests `link` (sic) and `hasNormalContent()` counts size/color so a separator with size/color is reported despite the doc: both kept and tested. (7) `isValidInternalLink` regex re-implemented without Regex (exactly one '/', non-empty sides). (8) The name->category cache is the Kotlin single-entry static cache keyed on ruleset identity (also ignores later ruleset edits, as Kotlin).
    DEFERRED MEMBERS TAKEN: IRulesetObject now extends ICivilopediaText; RulesetObject/RulesetStatsObject have `CivilopediaText` and abstract `MakeLink()`; MakeLink added to CityStateType, Difficulty, Nation, Personality, Era, Technology, Terrain, TileImprovement, TileResource, BaseUnit, Promotion, UnitNameGroup, UnitType, Building (others already had it, now `override`); GlobalUniques.GetCivilopediaTextLines and Combine's civilopediaText. Also SimpleCivilopediaText. Not taken, owned by new T108: all other getCivilopediaTextLines/Header/getSortGroup/getSubCategory/getIconName, Belief entries, Ruleset.allICivilopediaText, validator checkCivilopediaText. renderCivilopediaText (MarkupRenderer) is client. Note for T096/T097/T108: override the interface default by listing `ICivilopediaText` in the class base list and declaring a public method (see GlobalUniques).
  - Review PASS (non-blocking, unfixed): (1) FormattedLine name->category static cache is two unsynchronised statics (same window as Kotlin, single-threaded there) and ignores GameInfo changes; (2) EngineColors.ValueOf throws FormatException for non-ASCII Unicode digits in a colour ("#٣٣٣", IsHex uses char.IsDigit like Kotlin, Java parseInt accepts them); fix when a hostile-mod hardening pass happens (catch in ParseColor or hand-parse). Also no JSON writer support for NaN ImageSize.

### T096 Civilopedia text model B: Building, Improvement, Resource, Tile descriptions
- State: Available
- Depends: T095, T062, T054
- Source: core/src/com/unciv/ui/objectdescriptions/BuildingDescriptions.kt, ImprovementDescriptions.kt, ResourceDescriptions.kt, TileDescription.kt
- Fixtures: none - generate golden values
- Deliverables: csharp/Unciv.Core/UI/ObjectDescriptions/*.cs, tests (+ the deferred Building/TileImprovement/TileResource/Terrain civilopedia members)
- Gate: `dotnet test --filter FullyQualifiedName~ObjectDescriptionsBTests`
- Claimed:
- Notes: Created by the backlog audit. ~740 LOC. Takes the Building/TileImprovement/TileResource/Terrain getCivilopediaTextLines, getShortDescription, makeLink, getSortGroup, displayAs members deferred by T030/T031/T033. Seam rule as T053.

### T097 Civilopedia text model C: BaseUnit, Nation, Technology descriptions
- State: In-Progress [Claude]
- Depends: T095, T062, T057
- Source: core/src/com/unciv/ui/objectdescriptions/BaseUnitDescriptions.kt, NationDescriptions.kt, TechnologyDescriptions.kt
- Fixtures: none - generate golden values
- Deliverables: csharp/Unciv.Core/UI/ObjectDescriptions/*.cs, tests (+ the deferred BaseUnit/Nation/Technology/Era civilopedia members)
- Gate: `dotnet test --filter FullyQualifiedName~ObjectDescriptionsCTests`
- Claimed: 2026-10-24T12:22Z
- Notes: Created by the backlog audit. ~1.0k LOC. Takes the Nation/Era/Technology/BaseUnit members deferred by T032/T033/T034 (makeLink, getSortGroup/getSubCategory, civilopedia text, Nation.getStartBias, Era.getStartingUnits/getEraGatedObjects/matchesFilter, Technology.matchesFilter/getDescription, Difficulty/...) - recheck T032's Notes for the exact list. Seam rule as T053.

### T098 Save/load A: FileConversions, MapSaver, IMediaFinder
- State: Done
- Depends: T034, T039, T042, T049
- Source: core/src/com/unciv/logic/files/FileConversions.kt, MapSaver.kt, IMediaFinder.kt
- Fixtures: tests/src/com/unciv/logic/MediaFinderTests.kt, MapSaver parts of tests/src/com/unciv/logic/SerializationTests.kt
- Deliverables: csharp/Unciv.Core/Logic/Files/FileConversions.cs, MapSaver.cs, IMediaFinder.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~FilesATests`
- Claimed: 2026-10-24T10:52Z
- Notes: Created by the backlog audit: Non-goals #3 keeps these engine save/load abstractions 'deferred to the engine save/load ticket' that did not exist. ~370 LOC. Use the engine's IRulesetFolder/ITranslationFileHandle style file abstraction (no FileHandle/Gdx.files), gzip+base64 via System.IO.Compression with byte-identical output where Kotlin is deterministic. UncivGame.Current references become explicit parameters.
  - Implemented FileConversions, MapSaver, IMediaFinder (+Sounds/Music/Voices/Images/LabeledSounds), and new IUncivFileHandle/IMediaEnvironment seams (replace Gdx FileHandle / Gdx.files / UncivGame.Current). Tests: FilesATests (in-memory file fake).
    Deviations: ReadJson decodes eagerly so a corrupt tail of a zipped file also falls back to plain text (Kotlin only on bad header); gzip bytes not JVM-identical (decode-compatible); Unzip rejects whitespace like Java basic decoder. MapSaver.SaveMap/GetMaps take a localFile resolver instead of UncivGame.Current.files. super.listMediaFolders -> MediaFinderDefaults.ListMediaFolders. IMediaFinder.GetRuleset defaults to null.
    GAP: TileMap and TileMap.Preview have no JSON wiring yet (TileMap.cs says so), so MapSaver TileMap/Preview round trips cannot be tested; the zip/trim/fallback/file logic is. Needs a later ticket for TileMap/Tile JSON wiring.
  - Follow-up ticket T107 created for the TileMap/Tile JSON wiring gap.

### T099 GameStarter
- State: Available
- Depends: T041, T046, T061, T066, T078, T098
- Source: core/src/com/unciv/logic/GameStarter.kt
- Fixtures: tests/src/com/unciv/logic/LoadGameTest.kt (new-game parts; TestGame-free), tests/src/com/unciv/logic/map/CityStateTypeStartBiasTests.kt
- Deliverables: csharp/Unciv.Core/Logic/GameStarter.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~GameStarterTests`
- Claimed:
- Notes: Created by the backlog audit (GameStarter was 'Planned'). ~675 LOC. Same seed must produce the same GameInfo hash (T011 harness) - golden hashes generated by the Kotlin code. startingLocationsByNation must be a JavaHashMap (T039 review note: Kotlin HashMap<String> iteration order). Uses T039's placeUnitNearTile/stripPlayer (untested until T072; add cases) and the Player.ChosenNation/SetNationTransient members left in Player.cs (T104 list). UncivGame.Current/AlertType/PopupAlert multiplayer bits: UI/Won't port, note what was cut. Seam rule as T053.

### T100 Save/load B: UncivFiles
- State: Available
- Depends: T071, T098, T107
- Source: core/src/com/unciv/logic/files/UncivFiles.kt (engine part: save/load GameInfo and GameInfoPreview, gzip, autosave naming, compat version checks)
- Fixtures: tests/src/com/unciv/logic/SerializationTests.kt, GameSerializationTests.kt (TestGame: T072)
- Deliverables: csharp/Unciv.Core/Logic/Files/UncivFiles.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~UncivFilesTests`
- Claimed:
- Notes: Created by the backlog audit. ~585 LOC, minus platform saver/loader (PlatformSaverLoader/FileChooser are Won't port) and GameSettings (client, Non-goals #9). Round trip GameInfo -> file -> GameInfo must preserve the T011 state hash. Save format stays Kotlin-compatible (gzip+base64 JSON) so existing saves load; GameInfoSerializationVersion / compatibility migrations run through T071's BackwardCompatibility.

### T101 Simulation: Simulation, SimulationStep, MutableInt
- State: Available
- Depends: T099, T094, T071, T011
- Source: core/src/com/unciv/logic/simulation/Simulation.kt, SimulationStep.kt, MutableInt.kt
- Fixtures: none - generate golden values
- Deliverables: csharp/Unciv.Core/Logic/Simulation/*.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~SimulationTests`
- Claimed:
- Notes: Created by the backlog audit. ~390 LOC. Headless multi-game AI simulation runner; threads/Concurrency become a sequential loop (Non-goals #4), UncivGame.Current/settings replaced by parameters, Timers dropped. Used by T102.

### T102 End-to-end determinism over N turns
- State: Available
- Depends: T101, T100, T072, T011, T107
- Source: (new) uses core/src/com/unciv/logic/GameStarter.kt, GameInfo.nextTurn and the AI automation via the C# ports
- Fixtures: none - golden state hashes per turn generated by the Kotlin game for fixed seeds
- Deliverables: csharp/Unciv.Core.Tests/Determinism/*.cs (N-turn AI-only games, same seed twice and vs Kotlin goldens), tests
- Gate: `dotnet test --filter FullyQualifiedName~EndToEndDeterminism`
- Claimed:
- Notes: Created by the backlog audit: the stale Planned paragraph listed 'end-to-end determinism over N turns (uses T011)'. Runs AI-only games (several seeds, map sizes, vanilla + one mod) for N turns through GameStarter, TurnManager and NextTurnAutomation, compares the T011 state hash every turn with goldens exported from the Kotlin game, then save -> load -> continue and checks hash equality. Any divergence goes back to the owning ticket as a review note. Also run with DETERMINISM.md / HASH_ORDER_AUDIT.md checks.

### T103 Remaining Kotlin tests sweep: uniques, filters, tutorials
- State: Available
- Depends: T072, T052, T098, T097
- Source: tests/src/com/unciv/uniques/CarrierTests.kt, EventCircularTriggersTest.kt, GlobalUniquesTests.kt, TileUniquesTests.kt, logic/filter/CityCivFilterTests.kt, PolicyFilterTests.kt, TechFilterTests.kt, TileFilterTests.kt, UnitFilterTests.kt, logic/TutorialTranslationTests.kt, view/GameViewMovementHistoryTest.kt
- Fixtures: (the test files above)
- Deliverables: csharp/Unciv.Core.Tests/**, ported tests
- Gate: `dotnet test`
- Claimed:
- Notes: Created by the backlog audit: these Kotlin tests were named by no ticket (they all need TestGame, Civilization/City/MapUnit, filters or the Civilopedia/tutorial data). One test class per Kotlin class; also ports any remaining T027/T028/T030/T033/T034/T036/T057 cases still deferred (check their Notes) and the TimedUniquesTests cases listed in T035 if T035 did not. Real bugs go back to the owning ticket as review notes.
  - Backlog audit: besides the ported-over-TestGame cases, also covers T028's deferred CountableTests/ExpressionTests cases that need real game state, T036's UniqueErrorTests.testEducatedEliteGreatPersonGifting and the T036/T057 deferred validator cases, and anything the Kotlin ResourceTests (tests/src/com/unciv/uniques/ResourceTests.kt) needs from T106.

### T104 Marker, seam and stale-comment sweep (final)
- State: Available
- Depends: T102, T103, T106, T097, T093, T089, T094, T100
- Source: (new) csharp/Unciv.Core, csharp/Unciv.Core.Tests
- Fixtures: none
- Deliverables: edits in the files below, no new API
- Gate: `dotnet test --filter FullyQualifiedName~SourcePolicyTests`
- Claimed:
- Notes: Created by the backlog audit. Final hygiene ticket, run when every porting ticket is Done. (1) grep -rn 'TODO(T' csharp/Unciv.Core csharp/Unciv.Core.Tests: every marker must name a ticket that is not Done (fix or delete the stale ones). Known stale at audit time: IHasUniques.cs (TODO(T033), owner T087 for getWeightForAiDecision), Player.cs (TODO(T034)/Nation transients: port Player.chosenNation/setNationTransient(ruleset) and the Nation constructor here if GameStarter T099 did not), Translations.cs TryReadTranslationForLanguage (TODO(T034): RulesetCache should be the caller), MayaCalendar.cs (TODO(T050): owner T070), Tile.cs:533 (upstream note 'Generic-ify', leave as plain comment), ReligionState.cs (TODO(T052) should be T057). (2) Delete every *Seams.cs / PendingTickets file and 'NotSupportedException(... not ported ...)' stub whose owner ticket is Done, e.g. MapGeneratorSeams.cs, NaturalWonderSeams.cs, MapRegionsSeams.cs (T041/T076/T077), BaseUnit.CanBePurchasedWithStat (T063), CityStats/TileMap/Tile seams. (3) grep -rn 'NotSupportedException\|NotImplementedException' csharp/Unciv.Core: only intentional guards (Counter.ZERO, EngineColor, JavaHashMap treeify) may remain. (4) Re-run python3 -I tools/ticket.py section Planned: it must be empty/removed.
  - Backlog audit, after T055 landed: TileMap.cs still carries TODO(T055) (lines ~21, ~855) for switchPlayersNation, which T055 did not port; T055 will be Done, so this ticket owns it: port TileMap.switchPlayersNation (Kotlin: 'currently unreachable', constructs a Civilization) or record it as Won't-port-unreachable in the code comment and drop the marker. Also check CivilizationSeams.cs / *Seams.cs files created by T055-T072: each must be deleted by its owner ticket; list survivors here.

### T105 Won't-port record from the coverage audit
- State: Won't port
- Depends: none
- Source: core/src/com/unciv/models/metadata/GameSettings.kt, GameSettingsMigrations.kt, OverviewPersistableData.kt, core/src/com/unciv/logic/AlternatingStateManager.kt, core/src/com/unciv/json/DurationSerializer.kt, core/src/com/unciv/logic/automation/Timers.kt, tests/src/com/unciv/testing/{BaseTestRunner,GdxTestRunner,TestRunnerFactory,BasicTests,TestParcours,GitInfo,MeasureDuration,MeasureMemory,MemorySnaphots,RedirectOutput}.kt, tests/src/com/unciv/dev/*.kt
- Fixtures: none
- Deliverables: none
- Gate: none
- Claimed:
- Notes: Created by the backlog audit; the files above were named by no ticket. Decisions (conservative): GameSettings/GameSettingsMigrations/OverviewPersistableData are client settings (Non-goals #9); AlternatingStateManager and DurationSerializer are used only by multiplayer/chat UI popups (ui/popups/options, worldscreen/chat, status) so they fall under Non-goals #1; Timers (timeThis profiling) is dropped (T027 policy) and its few engine call sites carry no behaviour; the Kotlin test runner/benchmark/dev-UI files are JVM/libGDX harness code (TestGame is T035; BasicTests/NextTurnLatencyTest are benchmarks). If a later ticket finds engine logic in one of them, add a real ticket instead.

### T106 Deferred ruleset-object members (T030-T033 leftovers)
- State: Available
- Depends: T063, T066, T052, T057, T062
- Source: core/src/com/unciv/models/ruleset/tile/TileResource.kt, TileImprovement.kt, ResourceSupplyList.kt, core/src/com/unciv/models/ruleset/nation/Nation.kt, Era.kt (nation/ and tech/), tech/Technology.kt, core/src/com/unciv/models/ruleset/Victory.kt (Milestone), Belief.kt, Specialist.kt, unit/BaseUnit.kt, Building.kt, PerpetualConstruction.kt (only the members still unported)
- Fixtures: tests/src/com/unciv/models/ruleset/VictoryAvailabilityTests.kt, VictoryMilestoneTests.kt (T057 lists them; do whichever T057 did not), tests/src/com/unciv/logic/map/CityStateTypeStartBiasTests.kt, tests/src/com/unciv/uniques/ResourceTests.kt
- Deliverables: csharp/Unciv.Core/Models/Ruleset/**/*.cs (members on the existing classes), tests
- Gate: `dotnet test --filter FullyQualifiedName~DeferredRulesetMembersTests`
- Claimed:
- Notes: Created by the backlog audit: the Notes of T030/T031/T032/T033 list members deferred to 'T034/T046/T052' that no ticket took (confirmed absent in csharp/Unciv.Core). To port: TileResource.getImprovements/isImprovedBy/setTransients(ruleset)/getImprovingImprovement/generatesNaturallyOn; TileImprovement.getTurnsToBuild/getConstructorUnits/getCreatingUnits; ResourceSupplyList.subtractResourceRequirements; Nation.getStartBias; Era.getStartingUnits/getEraGatedObjects/matchesFilter; Technology.matchesFilter; Milestone.hasBeenCompletedBy/getFocus/getVictoryScreen*/getMoreCountable*/getCountableDo...; Belief.getBeliefsMatching; Specialist.colorObject (EngineColor); BaseUnit/Building/PerpetualConstruction getRejectionReasons-family members (isBuildable, shouldBeDisplayed, construct, newMapUnit, addConstructionBonuses, isPurchasable, tooltips) that T063 did NOT take (check T063's Notes first; no overlap), the Building/BaseUnit.matchesFilter 'is Building' part and the conditional-filter pieces named in T033's Notes. Civilopedia text members (getCivilopediaTextLines, makeLink, getSortGroup, getShortDescription, getDescription) are T095-T097, not here. Seam rule as T053.
  - T057: ReligionManager.AddConstructionBonuses (TODO(T106)) is a no-op stand-in for BaseUnit.addConstructionBonuses(unit, cityConstructions); call the real one from ReligionManager.GenerateProphet when it is ported.

### T107 TileMap and Tile JSON wiring, MapSaver round trips
- State: Done
- Depends: T098, T039, T053, T054
- Source: core/src/com/unciv/logic/map/TileMap.kt, core/src/com/unciv/logic/map/tile/Tile.kt (serialization parts), core/src/com/unciv/logic/files/MapSaver.kt
- Fixtures: none - generate golden map JSON by running the Kotlin MapSaver/Json on a small generated map
- Deliverables: csharp/Unciv.Core/Logic/Map/TileMap.cs, Tile.cs (JSON wiring), MapSaver round-trip tests under csharp/Unciv.Core.Tests/
- Gate: dotnet test --filter FullyQualifiedName~MapSaverRoundTrip
- Claimed: 2026-10-24T10:56Z
- Notes: Found by T098: TileMap and TileMap.Preview have no JSON wiring (TileMap.cs says so), so MapSaver TileMap/Preview round trips are untested. Wire Kotlin-compatible JSON (tileList, mapParameters, ruleset names, startingLocations, etc.) and add round-trip and golden tests; remove the TODO in TileMap.cs.
  - Golden: Logic/Map/Golden/MapSaver.golden.json came from the real Kotlin MapSaver.mapToSavedString (saveZipped=false) via a throwaway GdxTestRunner test (not committed) that built TileMap(3, 2, Civ_V_GnK) and set rivers, roads, queue, history, exploredBy, starting locations; run with ./gradlew --offline :tests:test --tests ...
    Wiring is by JsonConverters (TileJsonConverter nested in Tile, TileMapJsonConverter/StartingLocationJsonConverter/PreviewJsonConverter nested in TileMap): Kotlin field names, Gdx default omission (0/false/null/empty/-1/Usage.Player), unknown fields ignored, lists accept plain array or {"class","items"}; tile and tileList re-serialize byte-identical to Kotlin (test).
    Deviations: exploredBy (Java HashSet order in Kotlin) is written ordinal-sorted; Tile units (militaryUnit/civilianUnit/airUnits) throw NotSupportedException on read (non-empty) and write: follow-up T110 (needs T065). RoadStatus/Usage names parse case-insensitively. TileMap.Preview has a read-only converter; its MapParameters setter is now internal.
    Not compared: mapParameters output (MapParameters' own wiring writes defaults that Kotlin omits; only read/values are asserted).

### T110 Tile unit JSON wiring (militaryUnit, civilianUnit, airUnits)
- State: Available
- Depends: T107, T065
- Source: core/src/com/unciv/logic/map/tile/Tile.kt (serialized unit fields)
- Fixtures: none - generate golden JSON by running the Kotlin Json on a map with units
- Deliverables: csharp/Unciv.Core/Logic/Map/Tile/Tile.cs (TileJsonConverter), tests
- Gate: dotnet test --filter FullyQualifiedName~TileUnitJson
- Claimed:
- Notes: Found by T107: Tile's JSON converter throws NotSupportedException when a tile holds units (MapUnit not ported, units typed by the IGameContextUnit seam) and when the JSON has militaryUnit/civilianUnit/airUnits. After T065, retype and wire them (MapUnit JSON too), replace the throws, and add round-trip tests with units.

### T108 Civilopedia text model D: remaining ruleset objects' civilopedia members, Ruleset.allICivilopediaText, validator checkCivilopediaText
- State: Done
- Depends: T095
- Source: core/src/com/unciv/models/ruleset/{Policy,Speed,Victory,Belief,Tutorial}.kt, unit/{UnitType,UnitNameGroup,Promotion}.kt, nation/{CityStateType,Personality,Difficulty}.kt (Civilopedia members only), Ruleset.kt (allICivilopediaText), validation/RulesetValidator.kt (checkCivilopediaText, civilopediaText check in checkUnit)
- Fixtures: none - generate golden values
- Deliverables: csharp/Unciv.Core/Models/Ruleset/**, UI/ObjectDescriptions/*.cs, tests
- Gate: dotnet test --filter FullyQualifiedName~CivilopediaTextDTests
- Claimed: 2026-10-24T11:20Z
- Notes: Created by T095: the Civilopedia members of the ruleset objects that T096/T097 do not list. T095 already added MakeLink to every IRulesetObject (abstract on RulesetObject/RulesetStatsObject), GlobalUniques.getCivilopediaTextLines and civilopediaText in Combine. Remaining: getCivilopediaTextLines/Header/getSortGroup/getSubCategory/getIconName for Policy, Speed, Victory, Belief (+ getCivilopediaBeliefsEntry/getCivilopediaReligionEntry, getBeliefsMatching, then add the two virtual entries to CivilopediaCategories.Belief in GetCategoryIterator), Tutorial, UnitType, UnitNameGroup, Promotion, CityStateType, Personality, Difficulty (Era/Nation/Technology/BaseUnit are T097; Building/Improvement/Resource/Terrain are T096); Ruleset.allICivilopediaText; RulesetValidator.checkCivilopediaText using FormattedLine.UnsupportedReasons with an IFormattedLineImageCheck. Override pattern (see GlobalUniques): list ICivilopediaText in the class base list and declare a public GetCivilopediaTextLines(CivilopediaContext).
  - T108 implemented. Ported Civilopedia members (ICivilopediaText re-listed on each class, public GetCivilopediaTextLines(CivilopediaContext)/GetSortGroup/GetSubCategory/header/GetIconName) for Policy, Speed, Victory (+Milestone.GetFormattedLine), Belief (+GetBeliefsMatching/GetCivilopediaTextMatching/beliefs+religions entries, added to CivilopediaCategories.Belief), Tutorial, UnitType, UnitNameGroup, Promotion, Personality, Difficulty; Ruleset.AllICivilopediaText; RulesetValidator.CheckCivilopediaText + the unit-set-art warning in CheckUnit. Tests: CivilopediaTextDTests (48 hand-derived) + AllICivilopediaText duplicate-reference test in AllObjectsTests.
    Seams/deviations: (1) CivilopediaContext gained optional Collator (default invariant-culture comparer) and Language (default "en") standing in for UncivGame.Current.settings collator/locale (UnitNameGroup names sort + wikipedia link, Religions entry sort). (2) RulesetValidator.ImageCheck (IFormattedLineImageCheck) replaces uncachedImageExists; default assumes every image exists until a client supplies the atlas check. (3) GetCategoryIterator takes an optional CivilopediaContext (needed for the two virtual Belief entries). (4) UnitType/Personality helpers live in UI/ObjectDescriptions/UnitTypeDescriptions.cs and PersonalityDescriptions.cs (Kotlin: BaseUnitDescriptions.getUnitTypeCivilopediaTextLines, NationDescriptions Personality part); T097 may move them into its BaseUnitDescriptions/NationDescriptions. (5) New UI/Components/Fonts.cs (only the stat symbols) and UI/ObjectDescriptions/KotlinNumberText.cs (Kotlin Float.toString / Float.toInt). (6) UnitNameGroup.GetSortGroup reads the first unit's ICivilopediaText.GetSortGroup, which is the interface default 0 until T097 overrides it on BaseUnit.
    Quirks kept: Speed defaults use constructor-time modifier; Policy IsEnabledByPolicy's Unavailable half does not ignore conditionals (never matches "before adopting" without a civ); Tutorial.GetSortGroup throws for an empty category as Kotlin does; Difficulty prints "null" for a null aiDifficultyLevel; Unit-set-art regex is unquoted like Kotlin. Promotion.getDescription still not ported.

### T109 Hardening sweep: non-blocking review findings from T033-T095
- State: Done
- Depends: none
- Source: C# files named in the notes (no Kotlin source: parity fixes and missing tests)
- Fixtures: none
- Deliverables: fixes and tests in csharp/Unciv.Core and csharp/Unciv.Core.Tests
- Gate: dotnet test
- Claimed: 2026-10-24T11:23Z
- Notes: Owns every non-blocking finding that adversarial reviews left unfixed. Fix each (with a test) and tick it off with ticket.py note.
  - Findings to fix (source ticket: finding):
    - T095: FormattedLine name->category static cache is two unsynchronised statics and ignores GameInfo changes (Kotlin same, but make thread-safe or document); EngineColors.ValueOf throws FormatException for non-ASCII Unicode digits ("#٣٣٣": IsHex uses char.IsDigit like Kotlin, Java parseInt accepts them) - catch in ParseColor or hand-parse so it matches Kotlin; no JSON writer support for NaN ImageSize.
    - T076: NaN comparison in GeneratesNaturallyOn; an unused ruleset parameter.
    - T073: NaN edge case in PowSigned; thin RNG-stream test coverage (MapGenerationRandomness).
    - T054: untested ported branches (ProductionBonusWhenRemoved, RestrictedBuildableImprovements, and others the review listed).
    - T053: untested Tile members CanCivPassThrough, ProvidesYield, GetWorkingCity, IsLocked, ProvidesResources, UnitHeight/TileHeight.
    - T033: BaseUnit isGreatPerson and isMilitary use System.Lazy, which caches exceptions (Kotlin lazy does not); mirror the fix made for Type/IsLandUnit/IsWaterUnit.
    Later hardening findings from other tickets: add them here with ticket.py note.
  - T041: river-step HashSet could be LinkedHashSet and its header comment about Kotlin set order is inaccurate (mutableSetOf is insertion ordered); TopBottom/AroundCenterTile mirroring untested; HumidFrom exact value not asserted; '_ = gameParameters' in ResolveAutoMapSize.
  - Flaky test seen once under the full parallel run (T058): Unciv.Core.Tests.Utils.LogTests.LambdaParametersAreEvaluatedOnlyWhenLogging (LogTests.cs:143) failed, passed on rerun and in isolation; likely shared static Log state across parallel test classes.
  - T058 review non-blocking: add regression tests for AddFreeBuildings lazy per-city re-evaluation and the negative-amount TakeCities throw; CivConstructions Take(n) is lazy (Kotlin take copies) - add .ToList() if City port makes city-list mutation reachable; CivInfoTransientCache.IsCityConnectedToCapital is a convenience not in Kotlin (drop at T060 retype).
  - From T056: (a) the serialized fields of TechManager/PolicyManager/GoldenAgeManager/GreatPersonManager are JavaHashSet<string>/JavaHashMap<string,int>/Counter<string>/int[] (Kotlin HashSet/HashMap keep java.util order). The JSON layer (game save/load ticket) needs converters for JavaHashSet/JavaHashMap (BaseUnit.Promotions has a private one) and a camelCase name check: techsResearched, techsInProgress, techsToResearch, freeTechs, repeatingTechsResearched, scienceOfLast8Turns, scienceFromResearchAgreements, overflowScience (private), goldPercentConvertedToScience; policies: freePolicies, storedCulture, adoptedPolicies (internal), numberOfAdoptedPolicies (private), cultureOfLast8Turns (private), shouldOpenPolicyPicker; goldenAges: storedHappiness, numberOfGoldenAges (private), turnsLeftForCurrentGoldenAge; greatPeople: all public vars. Private/internal members are serialized in Kotlin (Gdx Json uses fields), so the JSON layer must include them. (b) Unciv.Core.Tests.Utils.LogTests is flaky when the whole suite runs in parallel (the global Log backend sees debug lines of other tests, e.g. "D [MapElevationGenerator] ..." inside Assert.Single/Equal); seen 3 times while working on T056, passes alone and on rerun. Isolate it in an xunit collection or give Log a per-test backend.
  - T059 follow-ups (non-blocking): TradeOffer.getOfferText (translation + Fonts + UncivGame.Current.gameInfo city lookup) is not ported, belongs with the Trade UI (T084) or a Translation port; DiplomacyFunctions.getKnownCivsSorted relies on GameInfo.getCivsSorted (collator + tr) being implemented by T046 via IGameInfoState.GetCivsSorted; DiplomacyFunctions.meetCiv skips settings.addCompletedTutorialTask("Meet another civilization") (client-side). Test flake seen while testing T059 (about 1 run in 4): Unciv.Core.Tests.Utils.LogTests fails (e.g. ThrowablesAreAppended, DebugAndErrorCarryTheCallingClassAsTag) because some test class outside the "TranslationState" collection logs through the process-wide Log.Backend while LogTests replaces it; find the culprit class and put it in the collection.
  - From T056 review (non-blocking): CivManagerTests lacks cases for TechManager.GetBestRoadAvailable, the ocean/RoadsConnectAcrossRivers booleans in UpdateTransientBooleans, IsObsolete, LimitOverflowScience clamp, UpdateResearchProgress overflow completion, the policy-branch-unlocked / known-civ era / Mayan Long Count notifications, PolicyManager.AdoptableBranches, RemovePolicy assumeWasFree / non-adopted throw, odd refundPercentage rounding (33), negative last-8-turns sums. T052 must add tests that TriggerUniqueSeam call sites fire. If a UI port enumerates PolicyManager.BranchCompletionMap/PriorityMap it should become ordered (LinkedHashMap).
  - T059 review non-blocking: (1) untested in DiplomacyCoreTests: MakePeace/MakePeaceOneSide, UpdateHasOpenBorders teleport branch, city-state resting point/degrade/GetTurnsToRelationshipChange, IsConsideredFriendlyTerritory, SetReligionBasedModifier, embassy/research/defensive-pact can-sign checks, GetKnownCivsSorted (T072 TestGame fixtures should cover). (2) TradeOffer.GetHashCode uses randomized HashCode.Combine (Kotlin data-class hash is deterministic): make it a fixed combine before TradeOffers are ever hashed. (3) TradeOffersList.Add hides List.Add: save loading (T071/T072) must not rely on IList.Add merging. (4) DiplomacyCoreTests [Collection("TranslationState")] is redundant now that xUnit parallelism is off. (5) DeclareWar() needs the DeclareWarReason parameter when T068 lands.
  - From T108 review: Belief.GetBeliefsMatching/GetCivilopediaTextMatching pass gameInfo=null to IsHiddenFromCivilopedia (Kotlin uses UncivGame.getGameInfoOrNull()); add an optional gameInfo/CivilopediaContext parameter. Also KotlinNumberText.FloatToString differs from Java for subnormals (1.0E-45 vs 1.4E-45).
  - Implemented by Claude. Flaky LogTests fixed and published separately (commit 80d94a311): [assembly: CollectionBehavior(DisableTestParallelization = true)] in Unciv.Core.Tests/AssemblyInfo.cs (map generation, ruleset loading etc. all log through the global Log, so per-collection serialization was not enough); 5 consecutive full runs green (suite ~8s).
    Fixed with tests: T095 FormattedLine name->category cache is now one immutable record published via a volatile write; EngineColors.ValueOf hand-parses digits (Unicode digits like Java); ImageSize/Padding NaN round-trip through JSON. T076 StartNormalizer's private GeneratesNaturallyOn copy (had the NaN comparison bug) removed in favour of TileResource.GeneratesNaturallyOn, NaN test added; the "unused ruleset parameter" was verified not present. T073 PowSigned NaN (Math.Sign threw) fixed + test; RNG-stream coverage verified adequate (ChooseSpreadOutLocations tests + Kotlin golden maps). T054 ProductionBonusWhenRemoved and RestrictedBuildableImprovements tests ("others the review listed" were never enumerated). T053 tests for CanCivPassThrough, ProvidesYield, GetWorkingCity, IsLocked, ProvidesResources, UnitHeight/TileHeight. T033 isGreatPerson/isMilitary retry after exceptions. T041 river set is LinkedHashSet (RiverGenerator takes ICollection<Tile>), header comment corrected, TopBottom/AroundCenterTile tests, HumidFrom exact value asserted, unused gameParameters dropped. T058 TakeCities returns a copy, negative-amount throw test, snapshot test, city-local-unique isolation test.
    Split off: T112 (drop IsCityConnectedToCapital after T060), T113 (T056 (a) JSON converters for Java collections), T114 (findings appended to T109 during this work: T056 review, T059 follow-ups/review, T108 review).

### T111 T057 review follow-ups: vote map order, unchecked sums, BeliefType parsing, seeded-path tests
- State: Available
- Depends: T057, T052, T060, T065
- Source: csharp/Unciv.Core/Logic/Civilization/Managers/{ReligionManager,EspionageManager,VictoryManager}.cs, Models/Spy.cs
- Fixtures: none
- Deliverables: fixes + tests in csharp/Unciv.Core.Tests/Logic/CivReligionVictoryTests.cs
- Gate: dotnet test --filter FullyQualifiedName~CivReligionVictoryTests
- Claimed:
- Notes: From T057 review (non-blocking): (1) IGameInfoState.DiplomaticVictoryVotesCast is IDictionary but Kotlin is java.util.HashMap: use JavaHashMap (vote-tie text order). (2) Enumerable.Sum throws on int overflow where Kotlin sumOf wraps (GetStartingSpyRank, Spy.GetEfficiencyModifier): unchecked loop. (3) Enum.Parse<BeliefType> accepts numeric strings, Kotlin valueOf does not. (4) Add tests for RuinsManager.SelectNextRuinsReward, GenerateProphet, FoundReligion/UseProphetForEnhancingReligion, ChooseBeliefs founding/enhancing, Spy.InitiateCoup/CanMoveTo, GetTilesVisibleViaSpies, with Kotlin-derived golden seeds (after T052/T060/T065 make them testable). (5) Share the duplicated ParseInt helper.

### T112 Hardening: drop CivInfoTransientCache.IsCityConnectedToCapital convenience at the City retype
- State: Done
- Depends: T060
- Source: csharp/Unciv.Core/Logic/Civilization/Transients/CivInfoTransientCache.cs (not in Kotlin)
- Fixtures: none
- Deliverables: remove the non-Kotlin convenience, call CitiesConnectedToCapitalToMediums directly (Civilization.IsCapitalConnectedToCity)
- Gate: dotnet test
- Claimed: 2026-10-24T12:00Z
- Notes: From T058 review (split off T109).
  - Removed non-Kotlin CivInfoTransientCache.IsCityConnectedToCapital; Civilization.IsCapitalConnectedToCity now uses Cache.CitiesConnectedToCapitalToMediums.ContainsKey directly. Standard checks pass (1843 tests).

### T113 Hardening: JSON converters for JavaHashSet/JavaHashMap serialized manager fields
- State: Done
- Depends: none
- Source: TechManager, PolicyManager, GoldenAgeManager, GreatPersonManager (T056)
- Fixtures: none
- Deliverables: converters in csharp/Unciv.Core/Json plus round-trip tests with Kotlin field names
- Gate: dotnet test
- Claimed: 2026-10-24T11:35Z
- Notes: From T056 (split off T109): serialized fields are JavaHashSet<string>/JavaHashMap<string,int>/Counter<string>/int[] (Kotlin HashSet/HashMap keep java.util order); BaseUnit.Promotions has a private converter. camelCase names to check: techsResearched, techsInProgress, techsToResearch, freeTechs, repeatingTechsResearched, scienceOfLast8Turns, scienceFromResearchAgreements, overflowScience (private), goldPercentConvertedToScience; policies: freePolicies, storedCulture, adoptedPolicies (internal), numberOfAdoptedPolicies (private), cultureOfLast8Turns (private), shouldOpenPolicyPicker; goldenAges: storedHappiness, numberOfGoldenAges (private), turnsLeftForCurrentGoldenAge; greatPeople: all public vars. Private/internal members are serialized in Kotlin (Gdx Json uses fields).
  - Implemented: Json/JavaHashConverters.cs (JavaHashSetConverterFactory: array; JavaHashMapConverterFactory: object, string keys only, int values via JsonValueReader.ReadInt incl. verbose/string forms), registered in UncivJson; BaseUnit's private PromotionSetConverter removed in favour of the shared one. TechManager/PolicyManager/GoldenAgeManager/GreatPersonManager serialized members carry [JsonPropertyName(kotlinCamelName)] (+[JsonInclude] for private/internal: overflowScience, adoptedPolicies, numberOfAdoptedPolicies, cultureOfLast8Turns, numberOfGoldenAges); PolicyManager.AdoptedPolicies got a private setter so it deserializes. allTechsAreResearched stays unserialized (Kotlin @Transient). Golden values: ran real libGDX 1.14.2 Json (OutputType.json) via java over a class with the same field names; techsResearched/techsInProgress order and read-order independence are asserted in Unciv.Core.Tests/Json/JavaHashConvertersTests.cs.
    Known differences: Gdx omits fields equal to the class-default prototype (empty sets, zero ints, default 0.6f); C# always writes every field (readable by Gdx; reader tolerates both). System.Text.Json writes properties before fields so private-field members come last (order irrelevant to readers).
  - From T114 (T059 review): TradeOffersList.Add hides List.Add and merges; IList<TradeOffer>.Add does NOT merge (pinned by a T114 test). Saved lists never hold duplicates, so the JSON converter may use plain adds; just do not rely on IList.Add for merging.

### T114 Hardening sweep 2: non-blocking findings from T056, T059, T108 reviews
- State: Done
- Depends: none
- Source: C# files named in the notes (no Kotlin source: parity fixes and missing tests)
- Fixtures: none
- Deliverables: fixes and tests in csharp/Unciv.Core and csharp/Unciv.Core.Tests
- Gate: dotnet test
- Claimed: 2026-10-24T11:35Z
- Notes: Moved out of T109 (appended there while it was in progress). Fix each with a test; tick off with ticket.py note. (1) T056: CivManagerTests lacks cases for TechManager.GetBestRoadAvailable, the ocean/RoadsConnectAcrossRivers booleans in UpdateTransientBooleans, IsObsolete, LimitOverflowScience clamp, UpdateResearchProgress overflow completion, the policy-branch-unlocked / known-civ era / Mayan Long Count notifications, PolicyManager.AdoptableBranches, RemovePolicy assumeWasFree / non-adopted throw, odd refundPercentage rounding (33), negative last-8-turns sums; T052 must add tests that TriggerUniqueSeam call sites fire; if a UI port enumerates PolicyManager.BranchCompletionMap/PriorityMap it should become ordered (LinkedHashMap). (2) T059: TradeOffer.getOfferText not ported (belongs with Trade UI T084 or a Translation port); DiplomacyFunctions.getKnownCivsSorted relies on IGameInfoState.GetCivsSorted being implemented by T046; DiplomacyFunctions.meetCiv skips settings.addCompletedTutorialTask (client-side). Untested in DiplomacyCoreTests: MakePeace/MakePeaceOneSide, UpdateHasOpenBorders teleport branch, city-state resting point/degrade/GetTurnsToRelationshipChange, IsConsideredFriendlyTerritory, SetReligionBasedModifier, embassy/research/defensive-pact can-sign checks, GetKnownCivsSorted. TradeOffer.GetHashCode uses randomized HashCode.Combine (Kotlin data-class hash is deterministic): make it a fixed combine. TradeOffersList.Add hides List.Add: save loading must not rely on IList.Add merging. DiplomacyCoreTests [Collection(TranslationState)] is redundant now that xUnit parallelism is off. DeclareWar() needs the DeclareWarReason parameter when T068 lands. (3) T108: Belief.GetBeliefsMatching/GetCivilopediaTextMatching pass gameInfo=null to IsHiddenFromCivilopedia (Kotlin uses UncivGame.getGameInfoOrNull()): add an optional gameInfo/CivilopediaContext parameter; KotlinNumberText.FloatToString differs from Java for subnormals (1.0E-45 vs 1.4E-45).
  - Done (all findings handled with tests unless stated): (1) T056: new CivManagerTests for GetBestRoadAvailable, ocean/rivers/embark/road-speed booleans, IsObsolete, LimitOverflowScience clamp (all branches), UpdateResearchProgress overflow completion, policy-branch-unlocked + known-civ era + Mayan Long Count notifications, AdoptableBranches, RemovePolicy assumeWasFree/non-adopted throw (pins the Kotlin quirk that the recursive branch-completion removal does not pass assumeWasFree), refund rounding 33 and 2 (half-up), negative last-8-turns sums (truncate toward zero). T052's TriggerUniqueSeam call-site tests and T068's DeclareWarReason were handed to those tickets as notes. BranchCompletionMap/PriorityMap: not a defect (Kotlin HashMap with identity hashes; C# Dictionary keeps insertion order, nothing iterates them outside PolicyManager). (2) T059: untested DiplomacyCoreTests cases added (MakePeace incl. ally city-state treaty and -10 influence, UpdateHasOpenBorders teleport + ally branch, city-state resting point/degrade/GetTurnsToRelationshipChange, IsConsideredFriendlyTerritory, SetReligionBasedModifier, embassy/research/defensive-pact can-sign, GetKnownCivsSorted via a fake GetCivsSorted); TradeOffer.GetHashCode is now the fixed Kotlin data-class combine (Java String hash, enum ordinal); IList.Add non-merging pinned by a test (note on T113); redundant [Collection(TranslationState)] removed; getOfferText note on T084; GetCivsSorted note on T046; meetCiv tutorial-task skip is a documented client-side deviation, not a defect. Test support: FakeCity.Civ virtual, FakeUnitManager.CivUnits, FakeCityStateFunctions.TributeWillingness, FakeGameInfoState.GetCivsSorted. (3) T108: Belief.GetBeliefsMatching/GetCivilopediaTextMatching take an optional IGameContextGameInfo (tested); KotlinNumberText.FloatToString renders 1.4E-45 like Java (single-digit shortest forms widened to the closest two-digit decimal; tested).

### T115 Retype Tile city seams to City and move the Tile/Pathfinding test fakes onto real City objects
- State: Available
- Depends: T060, T035
- Source: csharp/Unciv.Core/Logic/Map/Tile/Tile.cs, TileStatFunctions.cs, TileImprovementFunctions.cs, csharp/Unciv.Core/Logic/Civilization/Managers/ReligionManager.cs, csharp/Unciv.Core/Models/Spy.cs
- Fixtures: csharp/Unciv.Core.Tests/Logic/Map/TileTests*.cs, TileStatTests.cs, PathfindingTests.cs
- Deliverables: Tile.OwningCity/GetCity/GetWorkingCity/SetOwningCity, TileStatFunctions and TileImprovementFunctions city parameters typed City instead of IGameContextCity; drop ReligionManager.CityOf and the Spy.GetCityOrNull cast; Tile tests build real City objects (TestGame, T035) instead of FakeCity subclasses
- Gate: `dotnet test`
- Claimed:
- Notes: Deferred from T060: Tile's tests use fake civilizations (TileCiv/StatCiv/PathCiv, IGameContextCivilization) whose cities cannot be real City objects (City.Civ is the concrete Civilization), so retyping Tile's city seams needs TestGame (T035) to build real civilizations. T060 implements the IGameContextCity members on City (explicitly), so the retype is a mechanical edit plus the test rewrite. Code that waits for it is marked TODO(TNEW).
  - The marker above is TODO(T115).

### T116 Policy: ban System.Lazy, add KotlinLazy (Kotlin lazy never caches exceptions)
- State: Done
- Depends: none
- Source: none (policy ticket: new build-time rule, no Kotlin source)
- Fixtures: none
- Deliverables: csharp/Unciv.Core/Utils/KotlinLazy.cs, BannedSymbols.txt entry for System.Lazy<T>, migrate every existing 'new Lazy<' (43 in Unciv.Core, 2 pass a thread-safety mode), tests
- Gate: dotnet test --filter FullyQualifiedName~KotlinLazy
- Claimed: 2026-10-24T12:03Z
- Notes:
  - POLICY TICKET: this ticket may edit BannedSymbols.txt, .editorconfig, Directory.Build.props and SourcePolicyTests (PROTOCOL: only policy tickets may). Found by a codebase pattern scan (numbers approximate, regex based). Adding the rule must leave the build green: fix or allowlist (with // JUSTIFY: T### comment) every existing violation in the same ticket. Many other tickets run in parallel, so rebase often, keep both sides, and keep the diff to the rule plus its violations.
    Why: System.Lazy<T> in its default mode caches exceptions; Kotlin lazy does not (it retries). Adversarial reviews found this bug repeatedly (T033 BaseUnit Type/IsLandUnit/IsWaterUnit, T033 re-review isGreatPerson/isMilitary, T055 cityStateType, T058). Design: KotlinLazy<T> (thread-safe like Kotlin SYNCHRONIZED: lock + retry on exception, no caching of failures) plus an unsafe/publication-only variant if needed; ban T:System.Lazy`1 in BannedSymbols.txt with a message naming KotlinLazy. Migrate all call sites in Unciv.Core (not tests unless they use it) and add tests: success cached, exception not cached and retried, thread safety.
  - KotlinLazy<T> (Unciv.Utils): Kotlin SYNCHRONIZED lazy port (lock, volatile flag, retry on exception, initializer released after success, null results cached). Bans T:System.Lazy`1 and System.Threading.LazyInitializer in BannedSymbols.txt (verified RS0030 fires). All 45 'new Lazy<' sites migrated mechanically (Civilization's PublicationOnly arg dropped: Kotlin source uses default by lazy = SYNCHRONIZED). Tests: KotlinLazyTests (cache, retry, null, concurrency). Standard checks pass.

### T117 Policy: reflection test forbidding mutable static state in Unciv.Core
- State: Done
- Depends: none
- Source: none (policy ticket: new build-time rule, no Kotlin source)
- Fixtures: none
- Deliverables: csharp/Unciv.Core.Tests/Policy/MutableStaticStateTests.cs (reflection over the Unciv.Core assembly), allowlist file with JUSTIFY reasons, fixes for violations
- Gate: dotnet test --filter FullyQualifiedName~MutableStaticStateTests
- Claimed: 2026-10-24T12:03Z
- Notes:
  - POLICY TICKET: this ticket may edit BannedSymbols.txt, .editorconfig, Directory.Build.props and SourcePolicyTests (PROTOCOL: only policy tickets may). Found by a codebase pattern scan (numbers approximate, regex based). Adding the rule must leave the build green: fix or allowlist (with // JUSTIFY: T### comment) every existing violation in the same ticket. Many other tickets run in parallel, so rebase often, keep both sides, and keep the diff to the rule plus its violations.
    Why: shared mutable statics made LogTests flaky under parallel xUnit (fixed in T109) and break determinism. Currently about 60 static fields; known ones: Log.Backend {get;set;}, Log.EnableLogsFrom/DisableLogsFrom (mutable HashSets), EventBus.NextSeq, MapSize/other static caches. Rule: reflect over every type in Unciv.Core; fail on static fields that are not readonly/const and on public static properties with setters, and on static readonly fields of mutable collection types, unless listed in an allowlist with a JUSTIFY reason. For legitimate global state (Log backend, event bus sequence) prefer making it an explicit instance/parameter or document why it is safe, and make the tests that touch it non-parallel via an xUnit collection.
  - Reflection test MutableStaticStateTests (+ MutableStaticStateAllowlist.cs, every entry must start with JUSTIFY: and be still a violation, else a second test fails as stale). Flags non-readonly/non-const static fields, public static setters, static readonly fields of mutable collection/array types. 58 existing violations allowlisted: constant lookup tables (mutable type only by convenience) and true globals (EventBus, Log, RulesetCache, Translations.Current, host hooks, memo caches). Cleanup of the true globals is follow-up T123 (depends T117). Compiler-generated types skipped. Non-parallel collection for Log/EventBus tests left to T123.

### T118 Policy: marker check - every TODO(T###) names an open ticket, every NotSupported/NotImplemented seam has an owner
- State: Done
- Depends: none
- Source: none (policy ticket: new build-time rule, no Kotlin source)
- Fixtures: none
- Deliverables: csharp/Unciv.Core.Tests/Policy/ MarkerOwnershipTests (reads backlog.md/backlog-archive.md ticket headers and states), fixes for existing violations
- Gate: dotnet test --filter FullyQualifiedName~MarkerOwnershipTests
- Claimed: 2026-10-24T12:12:08Z
- Notes:
  - POLICY TICKET: this ticket may edit BannedSymbols.txt, .editorconfig, Directory.Build.props and SourcePolicyTests (PROTOCOL: only policy tickets may). Found by a codebase pattern scan (numbers approximate, regex based). Adding the rule must leave the build green: fix or allowlist (with // JUSTIFY: T### comment) every existing violation in the same ticket. Many other tickets run in parallel, so rebase often, keep both sides, and keep the diff to the rule plus its violations.
    Why: the backlog audit found stale/unowned markers (TODO(T050) after T050 was Done, TODO(T034) in Player.cs/Translations.cs, TODO(T033) in IHasUniques.cs, ReligionState.cs naming the wrong ticket) and a throwing seam (MapRegionResourcesSeams.cs) that stayed after its owner was Done and would have crashed the pipeline. Rules (tests, parsing the ticket ids and States from csharp/migration/backlog.md and backlog-archive.md; do NOT shell out to ticket.py): (1) every TODO(T###) in Unciv.Core and Unciv.Core.Tests must name a ticket that exists and is NOT Done (a Done ticket's markers are stale); (2) every throw new NotSupportedException/NotImplementedException in Unciv.Core must have a TODO(T###) on the same or previous 3 lines naming an open ticket, or a JUSTIFY comment; (3) a *Seams*.cs file whose owner tickets are all Done (named in its header comment) fails. Fix all current violations (about 58 TODO markers and 20 throws exist now).
  - Added MarkerOwnershipTests (4 tests: parses backlog.md/backlog-archive.md text; TODO(T###) must name an existing non-Done ticket, scanning Unciv.Core + Unciv.Core.Tests except Policy/; NotSupported/NotImplemented throws need an open TODO or JUSTIFY: T### within 3 lines; *Seams*.cs header comment must name an open owner). Fixes: stale markers retargeted (T033->T087, T050->T070, T034/T055->T104 which owns them per its notes), two already-retyped T059 and the T054 upstream note de-marked; 11 permanent throws got JUSTIFY: T118; BaseUnit throw -> TODO(T106); seam headers gained open owners (T046/T061/T063/T065/T066/T115/T104). Rebase often: new markers by other tickets are checked by this test.

### T119 Policy: forbid unchecked null-forgiving '!' (Kotlin !! throws, C# ! hides the null)
- State: Done
- Depends: none
- Source: none (policy ticket: new build-time rule, no Kotlin source)
- Fixtures: none
- Deliverables: csharp/Unciv.Core/Utils/NullExtensions.cs (NotNull helper that throws NullReferenceException like Kotlin !!), source-scanning policy test, migrate existing uses
- Gate: dotnet test --filter FullyQualifiedName~NullForgivingTests
- Claimed: 2026-10-24T12:06Z
- Notes:
  - POLICY TICKET: this ticket may edit BannedSymbols.txt, .editorconfig, Directory.Build.props and SourcePolicyTests (PROTOCOL: only policy tickets may). Found by a codebase pattern scan (numbers approximate, regex based). Adding the rule must leave the build green: fix or allowlist (with // JUSTIFY: T### comment) every existing violation in the same ticket. Many other tickets run in parallel, so rebase often, keep both sides, and keep the diff to the rule plus its violations.
    Why: about 90 uses of the null-forgiving operator in Unciv.Core. Kotlin's !! throws NullPointerException immediately; C# '!' only silences the compiler and the failure appears later or not at all, a parity difference. Rule: a source scan test (SourcePolicyTests style, ignore strings/comments) that fails on postfix '!' unless the line carries // JUSTIFY: T### (e.g. definite assignment after a guard), plus a NotNull()/Required() helper that throws NullReferenceException with the expression text for ported !! sites. Migrate the existing uses; check each against the Kotlin source to decide NotNull() vs a real null-safe rewrite.
  - - Added Unciv.Utils.NullExtensions.NotNull() (class and Nullable<struct> overloads, [NotNull] flow annotation, CallerArgumentExpression message, throws NullReferenceException = Kotlin !! NPE).
    - NullForgivingTests: lexer-based scan (strings/raw strings/chars/comments blanked) of Unciv.Core, Unciv.Harness, Unciv.Godot.Harness; postfix '!' fails unless '// JUSTIFY: T###' within 3 lines above. Scanner self-test included. Tests projects are not scanned (test code may use '!').
    - ~215 sites migrated mechanically: 'X!' -> 'X.NotNull()', 'X!.Value' (nullable struct) -> 'X.NotNull()' (exception type is now NRE not InvalidOperationException). Sites that were framework annotations (GetString, Activator, Deserialize) also became NotNull (fail early). Kept '!' with JUSTIFY: lateinit fields (null!), default! on unconstrained generics, out-value of TryGetValue, KotlinLazy initializer.

### T120 Policy: size ratchet - maximum file length and method length with a shrink-only baseline
- State: Done
- Depends: none
- Source: none (policy ticket: new build-time rule, no Kotlin source)
- Fixtures: none
- Deliverables: csharp/Unciv.Core.Tests/Policy/SizeRatchetTests.cs, baseline file (allowlist of current offenders with their current line counts), tests
- Gate: dotnet test --filter FullyQualifiedName~SizeRatchetTests
- Claimed: 2026-10-24T12:16:10Z
- Notes:
  - POLICY TICKET: this ticket may edit BannedSymbols.txt, .editorconfig, Directory.Build.props and SourcePolicyTests (PROTOCOL: only policy tickets may). Found by a codebase pattern scan (numbers approximate, regex based). Adding the rule must leave the build green: fix or allowlist (with // JUSTIFY: T### comment) every existing violation in the same ticket. Many other tickets run in parallel, so rebase often, keep both sides, and keep the diff to the rule plus its violations.
    Why: files over 1000 lines: UniqueTypeData.cs 2592 (a data table), Tile.cs 1496, Civilization.cs 1443, UniqueParameterType.cs 1286, TileMap.cs 1156, plus City.cs 945 and others near it. Methods over 100 lines: Conditionals.ConditionalApplies 305, Ruleset.Load 185, MapRegions.AssignRegions 168, JavaDecimalFormat.Parse 119, TileImprovementFunctions.CanImprovementBeBuiltHere 102. Many mirror giant Kotlin functions, so a hard limit would fight faithful ports: use a RATCHET. Test fails if (a) a file exceeds 1500 lines or a method exceeds 120 lines and is not in the baseline, or (b) a baselined file/method GROWS beyond its recorded size (the baseline may only shrink; the test rewrites nothing, humans lower the numbers). Method length via a Roslyn syntax scan (Microsoft.CodeAnalysis.CSharp as a test-only package; Directory.Packages.props is editable in this policy ticket) or a documented brace-matching scan. Do not refactor existing offenders here; note candidates for splitting.
  - Roslyn (Microsoft.CodeAnalysis.CSharp 4.14.0, test-only) scan. Limits: file 1500 lines, method 120 lines (declaration span, ctors/operators included, local funcs counted in parent; overloads: largest). Baseline: Unciv.Core.Tests/Policy/SizeRatchetBaseline.txt; test fails on new offender, on growth, and on stale entry. SIZE_RATCHET_DUMP=<path> writes a fresh candidate list. Baseline exact for UniqueTypeData.cs, ConditionalApplies, Ruleset.Load, AssignRegions, GetKnownValuesForAutocomplete (122). Tile.cs (1496) and Civilization.cs (1443) deliberately generous (1650/1600) because T061/T063/T065 are growing them: lower after they land. Split candidates listed in the baseline header.

### T121 Policy: coupling ratchet - cap distinct Unciv namespaces imported per file, shrink-only baseline
- State: Done
- Depends: none
- Source: none (policy ticket: new build-time rule, no Kotlin source)
- Fixtures: none
- Deliverables: csharp/Unciv.Core.Tests/Policy/CouplingRatchetTests.cs, baseline file, tests
- Gate: dotnet test --filter FullyQualifiedName~CouplingRatchetTests
- Claimed: 2026-10-24T12:14Z
- Notes:
  - POLICY TICKET: this ticket may edit BannedSymbols.txt, .editorconfig, Directory.Build.props and SourcePolicyTests (PROTOCOL: only policy tickets may). Found by a codebase pattern scan (numbers approximate, regex based). Adding the rule must leave the build green: fix or allowlist (with // JUSTIFY: T### comment) every existing violation in the same ticket. Many other tickets run in parallel, so rebase often, keep both sides, and keep the diff to the rule plus its violations.
    Why: Civilization.cs imports 18 distinct Unciv namespaces, TechManager 12, Ruleset/City 11, TileMap/MapGenerator 10: god-class risk. Same ratchet idea as the size ratchet: count distinct 'using Unciv...' namespaces plus fully qualified Unciv references per file (and optionally per-namespace cross-references), fail when a file not in the baseline exceeds 12 or a baselined file grows. Also add a layering test: Unciv.Core/Models and Logic must not reference Unciv.UI.* types other than the engine-owned leaf packages listed in backlog Non-goals #7 (Fonts stat chars, RankingType, etc.), and nothing in Unciv.Core may reference Godot/harness assemblies. Do not refactor existing offenders.
  - Implemented CouplingRatchetTests (Roslyn syntax scan of Unciv.Core: using directives + fully qualified refs, minus own namespace; heuristic, no binding) with CouplingRatchetBaseline.txt (path|count, same format family as T120). Limit 12. Baseline: Civilization.cs 24 (today 22, +2 headroom because T061/T063/T065 are growing it), TechManager.cs 14 (exact). Layering tests: Core must not reference Godot, Unciv.Harness, Unciv.Godot; Unciv.UI.* only from AllowedUiNamespaces (the 7 engine-owned leaf namespaces present today). Backlog Non-goals #7 was not readable (tool-only access), so the list is the observed current set; a new UI namespace needs a policy ticket. City/MapUnit/Ruleset are under 12 so unlisted. Per-namespace cross-reference counting left out (optional in ticket).
  - Review PASS (code). Standard checks were green on b2caf80b2+T121 (1911 tests) before T063 WIP seams landed on csharp-migration; the current tip fails to build in CivilizationSeams.cs/City.cs (ICityConstructions, IUnitPromotions), unrelated to T121. Civilization.cs baseline 24 is deliberate headroom (measured 22): lower to 22 after T061/T063/T065 land.

### T122 Policy: coverage reporting and ratchet for changed files (coverlet)
- State: Needs-Review [Claude]
- Depends: none
- Source: none (policy ticket: new build-time rule, no Kotlin source)
- Fixtures: none
- Deliverables: coverlet.collector in Unciv.Core.Tests via Directory.Packages.props, tools script csharp/migration/tools/coverage.py (line coverage per file, baseline file), PROTOCOL.md Standard-checks section update
- Gate: python3 -I csharp/migration/tools/coverage.py --check
- Claimed: 2026-10-24T12:22Z
- Notes:
  - POLICY TICKET: this ticket may edit BannedSymbols.txt, .editorconfig, Directory.Build.props and SourcePolicyTests (PROTOCOL: only policy tickets may). Found by a codebase pattern scan (numbers approximate, regex based). Adding the rule must leave the build green: fix or allowlist (with // JUSTIFY: T### comment) every existing violation in the same ticket. Many other tickets run in parallel, so rebase often, keep both sides, and keep the diff to the rule plus its violations.
    Why: adversarial reviews keep reporting untested ported branches (T053: CanCivPassThrough/ProvidesYield/GetWorkingCity/IsLocked/ProvidesResources/UnitHeight; T054: ProductionBonusWhenRemoved/RestrictedBuildableImprovements; T073 thin RNG-stream coverage; T107, T114). Add coverlet (collector), a script that runs dotnet test with coverage in dotnet-dev and writes per-file line and branch coverage, a baseline of current coverage, and a --check mode that fails when a file's coverage drops below its baseline (ratchet; new files must reach 80% line coverage unless excused with a reason). Report the files a ticket touched in the Review step. Keep the run time reasonable (collect once per ticket, not per test run); document in PROTOCOL.md under Standard checks.
  - Implemented tools/coverage.py (coverlet cobertura, one cached instrumented run via dotnet-dev; --check/--report/--update-baseline/--all/--run), migration/coverage-baseline.json (244 files, grandfathered at current line%), PROTOCOL.md Standard-checks paragraph, .gitignore for .coverage/. coverlet.collector was already in Directory.Packages.props/test csproj. Coverlet injects Coverlet.Core.Instrumentation.Tracker types that broke ArchitectureTests.AllTypesLiveUnderTheUncivNamespace and MutableStaticStateTests under coverage: both now skip that namespace (JUSTIFY-free, narrow). Default check scope = .cs files changed vs csharp-migration; new files need >=80% or an "excused" baseline entry; baseline only ever ratchets up.

### T123 Policy follow-up: eliminate allowlisted mutable statics (EventBus, Log, RulesetCache, Translations.Current, host hooks) in Unciv.Core
- State: Available
- Depends: T117
- Source: none (follow-up of T117)
- Fixtures: none
- Deliverables: csharp/Unciv.Core.Tests/Policy/MutableStaticStateAllowlist.cs entries tagged (T123) removed by converting to instance/injected state or frozen collections; non-parallel xUnit collection for Log/EventBus tests
- Gate: dotnet test --filter FullyQualifiedName~MutableStaticStateTests
- Claimed:
- Notes:

### T124 Hardening: CityStats test coverage gaps from T062 review
- State: Available
- Depends: T062
- Source: csharp/Unciv.Core/Logic/City/CityStats.cs, GreatPersonPointsBreakdown.cs
- Fixtures: none
- Deliverables: tests in CityStatsTests.cs for NullifiesStat distinct, trade routes, GetGrowthBonus + We Love The King Day, StatPercentBonus by construction type, UpdateTileStats, GreatPersonBoostWithFriendship and GreatPersonEarnedFaster (uses TestGame once T072 lands)
- Gate: dotnet test --filter FullyQualifiedName~CityStatsTests
- Claimed:
- Notes:

### T125 Policy: harden NullForgivingTests scanner (interpolation holes, cast false positive, JUSTIFY binding)
- State: Available
- Depends: T119
- Source: none
- Fixtures: none
- Deliverables: csharp/Unciv.Core.Tests/Policy/NullForgivingTests.cs
- Gate: dotnet test --filter FullyQualifiedName~NullForgivingTests
- Claimed:
- Notes: POLICY TICKET follow-up from T119 review. Scanner gaps: (1) $"{x!}" interpolation holes are blanked (false negative); (2) '!' at line start after newline missed; (3) '(bool)!x' and 'a >!b' flagged (false positive); (4) JUSTIFY comment is not tied to the specific '!' nor checked for ticket id. Fix with tests in the scanner self-test.

### T126 PromotionTree port (IPromotionTree seam, CityConstructions saved promotions)
- State: Available
- Depends: T063, T066
- Source: core/src/com/unciv/ui/screens/pickerscreens/PromotionTree.kt
- Fixtures: none
- Deliverables: csharp/Unciv.Core/Logic/Map/MapUnit/PromotionTree.cs, tests; MapUnit implements IMapUnitState.CreatePromotionTree (replacing the IPromotionTree seam), removes the marker
- Gate: dotnet test --filter FullyQualifiedName~PromotionTreeTests
- Claimed:
- Notes: Added by T063: CityConstructions.CompleteConstruction builds PromotionTree(unit) for saved promotions; its collator/translation sort needs a headless equivalent.

### T127 Lower size-ratchet baseline for Tile.cs and Civilization.cs after T061/T063/T065 land
- State: Available
- Depends: T061, T063, T065, T120
- Source:
- Fixtures: none
- Deliverables: csharp/Unciv.Core.Tests/Policy/SizeRatchetBaseline.txt
- Gate: dotnet test --filter FullyQualifiedName~SizeRatchetTests
- Claimed:
- Notes: Follow-up of T120: Tile.cs (1650) and Civilization.cs (1600) caps were deliberately generous; set them to actual size (or delete the entries if <=1500).
  - T121 (coupling ratchet): Civilization.cs coupling baseline is 24 against a measured 22, deliberate headroom for T061/T063/T065; lower it to the measured value once they land.

### T128 Decouple City.cs from the MapUnits namespace and lower its coupling baseline (13 -> 12)
- State: Available
- Depends: T065
- Source: none (policy follow-up of T065/T121)
- Fixtures: none
- Deliverables: csharp/Unciv.Core/Logic/City/City.cs (and seams), CouplingRatchetBaseline.txt
- Gate: dotnet test --filter FullyQualifiedName~CouplingRatchetTests
- Claimed:
- Notes: T065 had to add City.cs|13 to CouplingRatchetBaseline.txt because City now depends on the MapUnits namespace. Remove the dependency by going through a seam (or whatever Kotlin structure allows), then lower the baseline entry to the limit (12) and delete it if it no longer exceeds.
  - T061 (Claude): City.cs now imports Unciv.Logic.City.Managers (its 3 managers), so its CouplingRatchetBaseline entry is 14, not 13; decoupling City.cs (lowering to <= 12) covers it.

### T129 Civilopedia text model C2: Building/Improvement sections of Nation and Technology descriptions
- State: Available
- Depends: T096, T097
- Source: core/src/com/unciv/ui/objectdescriptions/NationDescriptions.kt (addUniqueBuildingsText, addUniqueImprovementsText), TechnologyDescriptions.kt (buildings/wonders enabled sections of getDescription and getCivilopediaTextLines)
- Fixtures: none - generate golden values
- Deliverables: csharp/Unciv.Core/UI/ObjectDescriptions/NationDescriptions.cs, TechnologyDescriptions.cs, tests
- Gate: dotnet test --filter FullyQualifiedName~ObjectDescriptionsC2Tests
- Claimed:
- Notes: Split from T097 at claim time: those sections call Building.getShortDescription, BuildingDescriptions.addBuildingDifferences, TileImprovement.getShortDecription and ImprovementDescriptions.getDifferences, all owned by T096. T097 leaves a TODO(this ticket) at each omitted spot. Replace those TODOs, add the missing lines in Kotlin order, and extend the T097 golden tests.

## Client track (Godot) — all tickets Blocked until the engine track delivers a playable core

Vision: a Civ 5-style client. Not a copy of Civ 5 assets (see Assets rule), but equivalent look and feel. Create detailed tickets here once their engine dependencies are `Done`; each must name an asset source and license.

### C000 Headless Godot test & snapshot harness
- State: Done (full ticket: backlog-archive.md)

### C001 Godot project skeleton
- State: Blocked
- Depends: C000, T006, T099, T100
- Deliverables: csharp/Unciv.Godot/ project consuming Unciv.Core, empty scene, CI-friendly headless build
- Gate: `dotnet build Unciv.Godot` (headless; no editor required)
  - Backlog audit: C001's Depends named 'a loadable game state (ticket to be added)': that is now GameStarter (T099, new game) and UncivFiles (T100, load). A-track Depends extended with the engine tickets they use (A003 Civilization/Victory/Diplomacy state, A004/A006 MapUnit, movement, combat and the Kotlin unit automation they replace).

### C002 3D hex terrain rendering
- State: Blocked
- Depends: C001
- Scope: 3D hex tile meshes with per-terrain materials and elevation (grass, plains, desert, tundra, snow, hills, mountains, forest/jungle, water with depth), smooth blending between adjacent terrains, fog of war and visibility tinting, tile yields and improvements as in-world props.
- Notes: Grass rendered as real textured/shaded terrain with instanced vegetation; no flat sprite tiles. Validation includes automated snapshot capture and golden image verification via C000 harness.

### C003 Resource and improvement props
- State: Blocked
- Depends: C002
- Scope: 3D resource props, including **horses** (animated or posed horse herds on the Horses resource tile), cattle, sheep, wheat, iron, etc., plus improvements (farms, pastures, mines, roads).

### C004 3D military units
- State: Blocked
- Depends: C002
- Scope: 3D unit models with idle, move, attack, damage and death animations for combat units; formation or multiple figures per unit; selection, path preview and attack-range overlays; combat resolved by the engine, presented by the client.

### C005 Civ 5-style UI shell
- State: Blocked
- Depends: C002
- Scope: top bar (yields, science/culture/gold/happiness), minimap, unit panel, city screen, tech/policy trees, diplomacy, turn banner, notifications, tooltips.

### C006 Networked play client
- State: Blocked
- Depends: T012, C001
- Scope: client for our own authoritative server (to be designed; separate server ticket needed), lobby, turn notifications, reconnect/resume.

## Game AI track (Civ 5 behavioral spec)

Architecture and behavioral requirements reverse-engineered from Civilization V (base, Gods & Kings, Brave New World).
Specification document: `csharp/migration/ai-spec/SPEC.md`. Shipped XML/TSV tuning data: `csharp/migration/ai-spec/data/`.

**Key Architectural Principle:**
Unlike Unciv's legacy per-unit automation (`automateUnitMoves` cascading individual unit heuristics), Civ 5 uses a **layered, flavor-driven, target-centric priority queue**:
1. Leader personality flavors (0..20) + Grand Strategy evaluation (10-turn commitment lock, hysteresis).
2. Economic, Military, and City strategies adjusting flavors and needs.
3. Target-centric Tactical AI (`TacticalMoveQueue`: city capture, high-value kill, overkill prevention recruit units first).
4. Homeland AI (`HomelandMoveQueue`: civilian/non-combat units and idle garrison).
5. Operations & Armies (multi-unit planned attacks/escorts).

All tickets start in `Blocked` until foundational engine systems (GameInfo, TileMap, Unit, Ruleset) are in place, but can be unblocked as their specific prerequisites land.

### A001 AI defines & data tables loader
- State: Done (full ticket: backlog-archive.md)

### A002 Leader flavor system & Grand Strategy evaluation
- State: Done (full ticket: backlog-archive.md)

### A003 Victory scorers (BNW)
- State: Blocked
- Depends: A002, T030, T055, T057, T059
- Source: `csharp/migration/ai-spec/SPEC.md` §4 (Conquest §4.1, Culture §4.2, Diplomatic §4.3, Spaceship §4.4)
- Deliverables: `csharp/Unciv.Core/AI/GrandStrategy/Scorers/` (`ConquestScorer`, `CultureScorer`, `DiplomaticScorer`, `SpaceshipScorer`), tests
- Gate: `dotnet test --filter FullyQualifiedName~VictoryScorerTests`
- Notes: Uses Brave New World formulas (Tourism/Influence ahead comparisons, World Congress voting power, stat ratios). Returns -100 if victory condition disabled.
  - Backlog audit: Depends extended with T055/T057/T059 (Civilization, victory/culture/religion managers, diplomacy state the scorers read).

### A004 Target-centric Tactical AI: TacticalMoveQueue & recruitment
- State: Blocked
- Depends: A001, T039, T040, T065, T067, T081, T090
- Source: `csharp/migration/ai-spec/SPEC.md` §6.1, §6.2, §6.4
- Deliverables: `csharp/Unciv.Core/AI/Tactical/` (`TacticalMoveQueue`, `TacticalMoveType`, `TacticalTarget`, `TacticalRecruiter`), tests
- Gate: `dotnet test --filter FullyQualifiedName~TacticalAITests`
- Notes: Replaces per-unit movement loops with a prioritized move queue. Critical high-value moves (e.g. `CAPTURE_CITY`, `DESTROY_HIGH_UNIT`) scan the board for valid targets and recruit the best candidate units across the civilization before lower-priority moves run.

### A005 Tactical overkill assignment & coordinated multi-unit attacks
- State: Blocked
- Depends: A004
- Source: `csharp/migration/ai-spec/SPEC.md` §6.4, §6.5
- Deliverables: `csharp/Unciv.Core/AI/Tactical/OverkillAssigner.cs`, damage estimator integrations, tests
- Gate: `dotnet test --filter FullyQualifiedName~OverkillTests`
- Notes: Solves the "one unit attacking at a time" weakness. Groups sufficient attackers to guarantee destruction without over-allocating, freeing remaining units for other tactical goals.

### A006 Homeland AI priority move queue
- State: Blocked
- Depends: A004, T091, T092
- Source: `csharp/migration/ai-spec/SPEC.md` §7
- Deliverables: `csharp/Unciv.Core/AI/Homeland/` (`HomelandMoveQueue`, `HomelandMovePriority`), tests
- Gate: `dotnet test --filter FullyQualifiedName~HomelandAITests`
- Notes: Handles non-combat and idle units (workers, settlers, great people, trade units, exploration, garrison) using the same prioritized move-queue architecture with `AI_HOMELAND_MOVE_PRIORITY_*` tunables.

### A007 Operations and armies layer
- State: Blocked
- Depends: A004, A005
- Source: `csharp/migration/ai-spec/SPEC.md` §8
- Deliverables: `csharp/Unciv.Core/AI/Operations/` (`AIOperation`, `ArmyAI`, operation types: `CityAttack`, `BarbarianCamp`, etc.), tests
- Gate: `dotnet test --filter FullyQualifiedName~OperationTests`
- Notes: Coordinates multi-unit strategic movements, muster staging areas, recruitment locks, and forward deployments.
