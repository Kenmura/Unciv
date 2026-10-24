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
- Depends: T034, T039, T045, T055, T060, T065
- Source: core/src/com/unciv/logic/GameInfo.kt (fields, setTransients, queries; turn handling is T071)
- Fixtures: tests/src/com/unciv/logic/SerializationTests.kt GameInfo parts, LoadGameTest.kt (TestGame-based: T072)
- Deliverables: csharp/Unciv.Core/Logic/GameInfo.cs, tests (+ GameInfo implements `IGameContextGameInfo`/`IGameInfoState`; retype the seam-typed GameInfo fields in TileMap/Civilization/City/MapUnit)
- Gate: `dotnet test --filter FullyQualifiedName~GameInfoTests`
- Claimed:
- Notes: SEAM RULE (set by T053, applies to T053-T072): the Tile -> TileMap -> Civilization -> City -> MapUnit -> GameInfo port order is acyclic because each ticket compiles only against classes that are already Done plus small engine-owned interfaces (`partial` extensions of the `IGameContext*` interfaces in `Models/Ruleset/Unique/GameContext.cs` / `ConditionalsSeams.cs`, or a new `I<Thing>` for types with no IGameContext seam, e.g. managers), in a file named `<Area>Seams.cs`, containing exactly the members the ticket calls. A field/parameter whose Kotlin type is a not-yet-ported class is typed by its seam. The ticket that later ports that class implements the seam, then retypes the earlier tickets' seam-typed fields/params to the concrete class (a mechanical edit of those files, listed in its Deliverables), keeping reference-identity Equals/GetHashCode (Kotlin Civilization/City/MapUnit/Tile have none). Do not add public API Kotlin lacks beyond the seams. TestGame-based fixtures are deferred to T035/T072. Top of the graph: GameInfo is ported LAST among the logic classes and uses everything concretely. Waiting on it: `TranslationCache.ActiveMods` should use the running game's `gameParameters.mods + baseRuleset` instead of `Translations.TranslationActiveMods` when a game exists (see `ActiveModsCache` in TranslationCache.kt), and the `GameSetupInfo(gameInfo)` constructor of T045. Scope: fields, serialization shape, setTransients, queries (getCities, getCivilization...), difficulty/speed accessors. Turn handling (`nextTurn`) and BackwardCompatibility moved to T071. 918 LOC: delegate the brief. Replace the `TODO(T046)` markers.

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
- State: Available
- Depends: T039, T010, T016, T017, T073, T074, T075, T076, T077
- Source: core/src/com/unciv/logic/map/mapgenerator/MapGenerator.kt (rest of pipeline; sub-tickets cover other files)
- Fixtures: tests/src/com/unciv/logic/map/MapGenerationTests.kt, ResourceDiversityTest.kt, mapgenerator/NaturalWonderGeneratorTests.kt
- Deliverables: csharp/Unciv.Core/Logic/Map/MapGenerator/*.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~MapGenerat`
- Claimed:
- Notes: Large (mapregions, resourceplacement, rivers, elevation, landmass). Split into sub-tickets at claim time if >1.5k LOC. Same seed must reproduce Kotlin maps: add golden maps generated from Kotlin.
  - Split at claim: sub-tickets T073-T077 cover all other files (~4.2k LOC). T041 itself = MapGenerator.kt (817 LOC) orchestration + golden Kotlin maps for same-seed reproduction; becomes ready when subs are Done.
  - Reset (split into sub-tickets; re-dispatch after subs Done); was 'In-Progress [Claude]'.

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
- Depends: T028, T055, T056, T057, T060, T065, T066
- Source: core/src/com/unciv/models/ruleset/unique/UniqueTriggerActivation.kt, UniqueTriggerExecutors.kt
- Fixtures: tests/src/com/unciv/uniques/ (trigger-related tests; list them when claiming; TestGame-based ones in T072)
- Deliverables: csharp/Unciv.Core/Models/Ruleset/Unique/UniqueTriggerActivation.cs, UniqueTriggerExecutors.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~UniqueTriggerTests`
- Claimed:
- Notes: Created from T027's review: T027 deferred `UniqueTriggerExecutors.triggerGlobalAlerts` (needs `Civilization.addNotification`, ported by T055) and the 1.5k-LOC `UniqueTriggerActivation`. Split by trigger family if it exceeds scope. Wires the `TODO(T052)` call sites left by T054, T063, T065 and `EventChoice.triggerChoice` (T030). Uses concrete Civilization, City, MapUnit and their managers; no new seams needed.

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
- State: In-Progress [Claude]
- Depends: T034, T045, T039, T053
- Source: core/src/com/unciv/logic/civilization/Civilization.kt, ExploredRegion.kt, PopupAlert.kt
- Fixtures: none directly (TestGame-based: T072)
- Deliverables: csharp/Unciv.Core/Logic/Civilization/Civilization.cs, ExploredRegion.cs, PopupAlert.cs, CivilizationSeams.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~CivilizationCoreTests`
- Claimed: 2026-10-24T10:35Z
- Notes: SEAM RULE (set by T053, applies to T053-T072): the Tile -> TileMap -> Civilization -> City -> MapUnit -> GameInfo port order is acyclic because each ticket compiles only against classes that are already Done plus small engine-owned interfaces (`partial` extensions of the `IGameContext*` interfaces in `Models/Ruleset/Unique/GameContext.cs` / `ConditionalsSeams.cs`, or a new `I<Thing>` for types with no IGameContext seam, e.g. managers), in a file named `<Area>Seams.cs`, containing exactly the members the ticket calls. A field/parameter whose Kotlin type is a not-yet-ported class is typed by its seam. The ticket that later ports that class implements the seam, then retypes the earlier tickets' seam-typed fields/params to the concrete class (a mechanical edit of those files, listed in its Deliverables), keeping reference-identity Equals/GetHashCode (Kotlin Civilization/City/MapUnit/Tile have none). Do not add public API Kotlin lacks beyond the seams. TestGame-based fixtures are deferred to T035/T072. ~1.6k LOC; delegate the read-and-brief step; split by member group at claim time if it exceeds scope. Civilization is ported BEFORE its managers, City, MapUnit and GameInfo, so every field of those types is a seam: `IGameInfoState : IGameContextGameInfo` (ruleset, turns, tileMap, civilizations, speed, difficulty, getCities...), `ITechManager`, `IPolicyManager`, `IReligionManager`, `IDiplomacyManager`, `IGoldenAgeManager`, `IGreatPersonManager`, `IVictoryManager`, `IRuinsManager`, `IEspionageManager`, `IQuestManager`, `ICivConstructions`, `ICivInfoTransientCache`, `ICivInfoStats`, `IUnitManager`, `ICityState` (extends IGameContextCity) and `IMapUnitState` (extends IGameContextUnit), each holding only the members Civilization.kt calls. It implements `IGameContextCivilization` (T027) and the Tile seam added by T053; it retypes Tile's owner-civ seam to `Civilization`. Notification/CivRankingHistory/NotificationAction types already exist (T051). `addNotification` is ported here. Deferred: members that call UniqueTriggerActivation (T052); turn handling (T070); anything on `civInfo.cities` beyond the `ICityState` seam goes to T060. `TODO(T0xx)` markers must name the owner ticket from this list (T056 tech/policy, T057 religion/ruins/victory/espionage, T058 stats/cache/constructions, T059 diplomacy, T060-T064 city, T065-T067 unit, T069 quests, T070 turn, T046 GameInfo).

### T056 Civilization managers: TechManager, PolicyManager, GoldenAgeManager, GreatPersonManager
- State: Available
- Depends: T055
- Source: core/src/com/unciv/logic/civilization/managers/TechManager.kt, PolicyManager.kt, GoldenAgeManager.kt, GreatPersonManager.kt
- Fixtures: tests/src/com/unciv/logic/civilization/GreatPersonSelectionTest.kt and the tech/policy parts of managers/* (TestGame-based: T072)
- Deliverables: csharp/Unciv.Core/Logic/Civilization/Managers/*.cs, tests (+ retype Civilization fields from ITechManager/IPolicyManager/IGoldenAgeManager/IGreatPersonManager to the concrete classes)
- Gate: `dotnet test --filter FullyQualifiedName~CivManagerTests`
- Claimed:
- Notes: SEAM RULE (set by T053, applies to T053-T072): the Tile -> TileMap -> Civilization -> City -> MapUnit -> GameInfo port order is acyclic because each ticket compiles only against classes that are already Done plus small engine-owned interfaces (`partial` extensions of the `IGameContext*` interfaces in `Models/Ruleset/Unique/GameContext.cs` / `ConditionalsSeams.cs`, or a new `I<Thing>` for types with no IGameContext seam, e.g. managers), in a file named `<Area>Seams.cs`, containing exactly the members the ticket calls. A field/parameter whose Kotlin type is a not-yet-ported class is typed by its seam. The ticket that later ports that class implements the seam, then retypes the earlier tickets' seam-typed fields/params to the concrete class (a mechanical edit of those files, listed in its Deliverables), keeping reference-identity Equals/GetHashCode (Kotlin Civilization/City/MapUnit/Tile have none). Do not add public API Kotlin lacks beyond the seams. TestGame-based fixtures are deferred to T035/T072. ~1.2k LOC. Concrete Civilization (T055); City/MapUnit through their seams. Implements the manager seams defined by T055. `TechManager` json field names must match Kotlin (saves). Deferred: calls into UniqueTriggerActivation (T052).

### T057 Civilization managers: ReligionManager (+Religion), RuinsManager, VictoryManager, EspionageManager (+Spy)
- State: Available
- Depends: T055
- Source: core/src/com/unciv/logic/civilization/managers/ReligionManager.kt, RuinsManager.kt, VictoryManager.kt, EspionageManager.kt, core/src/com/unciv/models/Religion.kt, Spy.kt
- Fixtures: tests/src/com/unciv/logic/civilization/EspionageTests.kt (TestGame-based: T072); VictoryAvailabilityTests/VictoryMilestoneTests deferred by T030
- Deliverables: csharp/Unciv.Core/Logic/Civilization/Managers/*.cs, csharp/Unciv.Core/Models/Religion.cs, Spy.cs, tests (+ retype Civilization seams to concrete)
- Gate: `dotnet test --filter FullyQualifiedName~CivReligionVictoryTests`
- Claimed:
- Notes: SEAM RULE (set by T053, applies to T053-T072): the Tile -> TileMap -> Civilization -> City -> MapUnit -> GameInfo port order is acyclic because each ticket compiles only against classes that are already Done plus small engine-owned interfaces (`partial` extensions of the `IGameContext*` interfaces in `Models/Ruleset/Unique/GameContext.cs` / `ConditionalsSeams.cs`, or a new `I<Thing>` for types with no IGameContext seam, e.g. managers), in a file named `<Area>Seams.cs`, containing exactly the members the ticket calls. A field/parameter whose Kotlin type is a not-yet-ported class is typed by its seam. The ticket that later ports that class implements the seam, then retypes the earlier tickets' seam-typed fields/params to the concrete class (a mechanical edit of those files, listed in its Deliverables), keeping reference-identity Equals/GetHashCode (Kotlin Civilization/City/MapUnit/Tile have none). Do not add public API Kotlin lacks beyond the seams. TestGame-based fixtures are deferred to T035/T072. ~1.1k LOC. `Logic/Civilization/Managers/ReligionState.cs` already exists (T028): make Religion implement `IGameContextReligion`/`ReligionState` as appropriate and the ReligionManager implement the seam T055 defined. Also finishes `Milestone.hasBeenCompletedBy/getFocus/...` deferred by T030 and `Victory` members that only needed a Civilization. Spy touches City/Tile: use concrete Tile, City seam.

### T058 Civilization transients: CivInfoTransientCache, CivInfoStatsForNextTurn, CapitalConnectionsFinder, CivConstructions
- State: Available
- Depends: T055, T054
- Source: core/src/com/unciv/logic/civilization/transients/CivInfoTransientCache.kt, CivInfoStatsForNextTurn.kt, CapitalConnectionsFinder.kt, civilization/CivConstructions.kt
- Fixtures: tests/src/com/unciv/logic/civilization/CapitalConnectionsFinderTests.kt (TestGame-based: T072)
- Deliverables: csharp/Unciv.Core/Logic/Civilization/Transients/*.cs, CivConstructions.cs, tests (+ retype Civilization seams)
- Gate: `dotnet test --filter FullyQualifiedName~CivTransientsTests`
- Claimed:
- Notes: SEAM RULE (set by T053, applies to T053-T072): the Tile -> TileMap -> Civilization -> City -> MapUnit -> GameInfo port order is acyclic because each ticket compiles only against classes that are already Done plus small engine-owned interfaces (`partial` extensions of the `IGameContext*` interfaces in `Models/Ruleset/Unique/GameContext.cs` / `ConditionalsSeams.cs`, or a new `I<Thing>` for types with no IGameContext seam, e.g. managers), in a file named `<Area>Seams.cs`, containing exactly the members the ticket calls. A field/parameter whose Kotlin type is a not-yet-ported class is typed by its seam. The ticket that later ports that class implements the seam, then retypes the earlier tickets' seam-typed fields/params to the concrete class (a mechanical edit of those files, listed in its Deliverables), keeping reference-identity Equals/GetHashCode (Kotlin Civilization/City/MapUnit/Tile have none). Do not add public API Kotlin lacks beyond the seams. TestGame-based fixtures are deferred to T035/T072. ~1.2k LOC. Concrete Civilization and Tile; cities via the `ICityState` seam (extend it with `cityStats`, `isCapital`, `getCenterTile`, buildings...). Resource/uniques caches must keep Kotlin's iteration order (LinkedHashMap) as they feed stats. `CivConstructions` calls `City`/`CityConstructions`: seam members, retyped by T060/T063.

### T059 Diplomacy core: DiplomacyManager, DiplomacyFunctions, Demand, Trade, TradeOffer, TradeOffersList
- State: Available
- Depends: T055
- Source: core/src/com/unciv/logic/civilization/diplomacy/DiplomacyManager.kt, DiplomacyFunctions.kt, Demand.kt (and DiplomacyFlags/DiplomaticStatus/RelationshipLevel if in the same package), logic/trade/Trade.kt, TradeOffer.kt, TradeOffersList.kt
- Fixtures: tests/src/com/unciv/logic/civilization/diplomacy/*, DefensivePactTests.kt (TestGame-based: T072)
- Deliverables: csharp/Unciv.Core/Logic/Civilization/Diplomacy/*.cs, csharp/Unciv.Core/Logic/Trade/*.cs, tests (+ retype Civilization.IDiplomacyManager)
- Gate: `dotnet test --filter FullyQualifiedName~DiplomacyCoreTests`
- Claimed:
- Notes: SEAM RULE (set by T053, applies to T053-T072): the Tile -> TileMap -> Civilization -> City -> MapUnit -> GameInfo port order is acyclic because each ticket compiles only against classes that are already Done plus small engine-owned interfaces (`partial` extensions of the `IGameContext*` interfaces in `Models/Ruleset/Unique/GameContext.cs` / `ConditionalsSeams.cs`, or a new `I<Thing>` for types with no IGameContext seam, e.g. managers), in a file named `<Area>Seams.cs`, containing exactly the members the ticket calls. A field/parameter whose Kotlin type is a not-yet-ported class is typed by its seam. The ticket that later ports that class implements the seam, then retypes the earlier tickets' seam-typed fields/params to the concrete class (a mechanical edit of those files, listed in its Deliverables), keeping reference-identity Equals/GetHashCode (Kotlin Civilization/City/MapUnit/Tile have none). Do not add public API Kotlin lacks beyond the seams. TestGame-based fixtures are deferred to T035/T072. ~1.5k LOC. Pure data/state parts of diplomacy and the trade data classes. Deferred: `DiplomacyTurnManager`, `DeclareWar`, `CityStateFunctions` (T068; they need City and MapUnit); `TradeLogic`/`TradeEvaluation` (Planned, after T068); anything calling `NextTurnAutomation`/Battle (Planned: Battle, Automation) goes behind a seam or a `TODO(T0xx)` that names a real ticket (add one to Planned if none exists).

### T060 City core: City, CityFocus, CityResources, CityEspionageManager
- State: Available
- Depends: T055, T056, T057, T058
- Source: core/src/com/unciv/logic/city/City.kt, CityFocus.kt, CityResources.kt, managers/CityEspionageManager.kt
- Fixtures: tests/src/com/unciv/logic/city/CityTest.kt (TestGame-based: T072)
- Deliverables: csharp/Unciv.Core/Logic/City/City.cs, CityFocus.cs, CityResources.cs, CityEspionageManager.cs, CitySeams.cs, tests (+ retype Civilization.cities / Tile city seams to City)
- Gate: `dotnet test --filter FullyQualifiedName~CityCoreTests`
- Claimed:
- Notes: SEAM RULE (set by T053, applies to T053-T072): the Tile -> TileMap -> Civilization -> City -> MapUnit -> GameInfo port order is acyclic because each ticket compiles only against classes that are already Done plus small engine-owned interfaces (`partial` extensions of the `IGameContext*` interfaces in `Models/Ruleset/Unique/GameContext.cs` / `ConditionalsSeams.cs`, or a new `I<Thing>` for types with no IGameContext seam, e.g. managers), in a file named `<Area>Seams.cs`, containing exactly the members the ticket calls. A field/parameter whose Kotlin type is a not-yet-ported class is typed by its seam. The ticket that later ports that class implements the seam, then retypes the earlier tickets' seam-typed fields/params to the concrete class (a mechanical edit of those files, listed in its Deliverables), keeping reference-identity Equals/GetHashCode (Kotlin Civilization/City/MapUnit/Tile have none). Do not add public API Kotlin lacks beyond the seams. TestGame-based fixtures are deferred to T035/T072. ~1.2k LOC. Concrete Civilization and its managers (T055-T058), concrete Tile. City's own sub-objects are seams: `ICityConstructions`, `ICityStats`, `ICityPopulationManager`, `ICityExpansionManager`, `ICityReligionManager`, plus `IMapUnitState` for units. Implements `IGameContextCity` (T027) and retypes the city seam members in Civilization (T055), Tile (T053) and the managers. `City` json field names must match Kotlin.

### T061 City managers: CityPopulationManager, CityExpansionManager, CityReligionManager, CityFounder
- State: Available
- Depends: T060
- Source: core/src/com/unciv/logic/city/managers/CityPopulationManager.kt, CityExpansionManager.kt, CityReligionManager.kt, CityFounder.kt
- Fixtures: tests/src/com/unciv/logic/city/managers/* (TestGame-based: T072)
- Deliverables: csharp/Unciv.Core/Logic/City/Managers/*.cs, tests (+ retype City seams)
- Gate: `dotnet test --filter FullyQualifiedName~CityManagerTests`
- Claimed:
- Notes: SEAM RULE (set by T053, applies to T053-T072): the Tile -> TileMap -> Civilization -> City -> MapUnit -> GameInfo port order is acyclic because each ticket compiles only against classes that are already Done plus small engine-owned interfaces (`partial` extensions of the `IGameContext*` interfaces in `Models/Ruleset/Unique/GameContext.cs` / `ConditionalsSeams.cs`, or a new `I<Thing>` for types with no IGameContext seam, e.g. managers), in a file named `<Area>Seams.cs`, containing exactly the members the ticket calls. A field/parameter whose Kotlin type is a not-yet-ported class is typed by its seam. The ticket that later ports that class implements the seam, then retypes the earlier tickets' seam-typed fields/params to the concrete class (a mechanical edit of those files, listed in its Deliverables), keeping reference-identity Equals/GetHashCode (Kotlin Civilization/City/MapUnit/Tile have none). Do not add public API Kotlin lacks beyond the seams. TestGame-based fixtures are deferred to T035/T072. ~1.2k LOC. Implements the ICityPopulationManager/ICityExpansionManager/ICityReligionManager seams from T060. `CityFounder` is required by TestGame (T035). Tile-ownership and border expansion iteration order is game state.

### T062 CityStats and GreatPersonPointsBreakdown
- State: Available
- Depends: T060, T058
- Source: core/src/com/unciv/logic/city/CityStats.kt, GreatPersonPointsBreakdown.kt
- Fixtures: stat-related cases in tests/src/com/unciv/logic/city/CityTest.kt (TestGame-based: T072)
- Deliverables: csharp/Unciv.Core/Logic/City/CityStats.cs, GreatPersonPointsBreakdown.cs, tests (+ retype City.ICityStats)
- Gate: `dotnet test --filter FullyQualifiedName~CityStatsTests`
- Claimed:
- Notes: SEAM RULE (set by T053, applies to T053-T072): the Tile -> TileMap -> Civilization -> City -> MapUnit -> GameInfo port order is acyclic because each ticket compiles only against classes that are already Done plus small engine-owned interfaces (`partial` extensions of the `IGameContext*` interfaces in `Models/Ruleset/Unique/GameContext.cs` / `ConditionalsSeams.cs`, or a new `I<Thing>` for types with no IGameContext seam, e.g. managers), in a file named `<Area>Seams.cs`, containing exactly the members the ticket calls. A field/parameter whose Kotlin type is a not-yet-ported class is typed by its seam. The ticket that later ports that class implements the seam, then retypes the earlier tickets' seam-typed fields/params to the concrete class (a mechanical edit of those files, listed in its Deliverables), keeping reference-identity Equals/GetHashCode (Kotlin Civilization/City/MapUnit/Tile have none). Do not add public API Kotlin lacks beyond the seams. TestGame-based fixtures are deferred to T035/T072. ~830 LOC, Float/Int mixing everywhere: preserve exact evaluation order and rounding (half-up helper, T010-era KotlinMath). Also provides what `Countables` tests (stat-per-turn countables) deferred by T028 need.

### T063 CityConstructions
- State: Available
- Depends: T060, T062
- Source: core/src/com/unciv/logic/city/CityConstructions.kt
- Fixtures: production-related cases in tests/src/com/unciv/logic/civilization/FreeBuildingTests.kt (TestGame-based: T072)
- Deliverables: csharp/Unciv.Core/Logic/City/CityConstructions.cs, tests (+ retype City.ICityConstructions and CivConstructions seam members)
- Gate: `dotnet test --filter FullyQualifiedName~CityConstructionsTests`
- Claimed:
- Notes: SEAM RULE (set by T053, applies to T053-T072): the Tile -> TileMap -> Civilization -> City -> MapUnit -> GameInfo port order is acyclic because each ticket compiles only against classes that are already Done plus small engine-owned interfaces (`partial` extensions of the `IGameContext*` interfaces in `Models/Ruleset/Unique/GameContext.cs` / `ConditionalsSeams.cs`, or a new `I<Thing>` for types with no IGameContext seam, e.g. managers), in a file named `<Area>Seams.cs`, containing exactly the members the ticket calls. A field/parameter whose Kotlin type is a not-yet-ported class is typed by its seam. The ticket that later ports that class implements the seam, then retypes the earlier tickets' seam-typed fields/params to the concrete class (a mechanical edit of those files, listed in its Deliverables), keeping reference-identity Equals/GetHashCode (Kotlin Civilization/City/MapUnit/Tile have none). Do not add public API Kotlin lacks beyond the seams. TestGame-based fixtures are deferred to T035/T072. ~1.1k LOC; delegate the brief. Construction queue order and `constructionQueue` json format are game state. Unit production (`constructUnit`) goes through the `IMapUnitState`/`IUnitManager` seams (placement is T066); `UniqueTriggerActivation` calls are `TODO(T052)`. Also fills in `BaseUnit`/`Building` members deferred by T033 that only needed City/Civilization (list the ones taken in Notes).

### T064 City turn and conquest: CityTurnManager, CityConquestFunctions
- State: Available
- Depends: T060, T061, T063
- Source: core/src/com/unciv/logic/city/managers/CityTurnManager.kt, CityConquestFunctions.kt
- Fixtures: tests/src/com/unciv/logic/city/CityConquestFunctionsTest.kt (TestGame-based: T072)
- Deliverables: csharp/Unciv.Core/Logic/City/Managers/CityTurnManager.cs, CityConquestFunctions.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~CityTurnTests`
- Claimed:
- Notes: SEAM RULE (set by T053, applies to T053-T072): the Tile -> TileMap -> Civilization -> City -> MapUnit -> GameInfo port order is acyclic because each ticket compiles only against classes that are already Done plus small engine-owned interfaces (`partial` extensions of the `IGameContext*` interfaces in `Models/Ruleset/Unique/GameContext.cs` / `ConditionalsSeams.cs`, or a new `I<Thing>` for types with no IGameContext seam, e.g. managers), in a file named `<Area>Seams.cs`, containing exactly the members the ticket calls. A field/parameter whose Kotlin type is a not-yet-ported class is typed by its seam. The ticket that later ports that class implements the seam, then retypes the earlier tickets' seam-typed fields/params to the concrete class (a mechanical edit of those files, listed in its Deliverables), keeping reference-identity Equals/GetHashCode (Kotlin Civilization/City/MapUnit/Tile have none). Do not add public API Kotlin lacks beyond the seams. TestGame-based fixtures are deferred to T035/T072. ~600 LOC. Conquest touches units, diplomacy and religion: concrete T055-T063 classes, MapUnit via `IMapUnitState` (retyped by T065).

### T065 MapUnit core: MapUnit, MapUnitCache
- State: Available
- Depends: T055, T056, T060
- Source: core/src/com/unciv/logic/map/mapunit/MapUnit.kt, MapUnitCache.kt
- Fixtures: tests/src/com/unciv/logic/map/UnitFormationTests.kt (TestGame-based: T072)
- Deliverables: csharp/Unciv.Core/Logic/Map/MapUnit/MapUnit.cs, MapUnitCache.cs, MapUnitSeams.cs, tests (+ retype Tile/Civilization/City unit seams to MapUnit)
- Gate: `dotnet test --filter FullyQualifiedName~MapUnitCoreTests`
- Claimed:
- Notes: SEAM RULE (set by T053, applies to T053-T072): the Tile -> TileMap -> Civilization -> City -> MapUnit -> GameInfo port order is acyclic because each ticket compiles only against classes that are already Done plus small engine-owned interfaces (`partial` extensions of the `IGameContext*` interfaces in `Models/Ruleset/Unique/GameContext.cs` / `ConditionalsSeams.cs`, or a new `I<Thing>` for types with no IGameContext seam, e.g. managers), in a file named `<Area>Seams.cs`, containing exactly the members the ticket calls. A field/parameter whose Kotlin type is a not-yet-ported class is typed by its seam. The ticket that later ports that class implements the seam, then retypes the earlier tickets' seam-typed fields/params to the concrete class (a mechanical edit of those files, listed in its Deliverables), keeping reference-identity Equals/GetHashCode (Kotlin Civilization/City/MapUnit/Tile have none). Do not add public API Kotlin lacks beyond the seams. TestGame-based fixtures are deferred to T035/T072. ~1.4k LOC; delegate the brief. Concrete Civilization, City, Tile. Sub-objects are seams: `IUnitMovement` (T067), `IUnitPromotions`/`IUnitTurnManager`/`IUnitUpgradeManager` (T066). Implements `IGameContextUnit` (T027) and `IMapUnitState`. Deferred: UniqueTriggerActivation calls (T052), Battle/Automation calls (Planned; `TODO` must name a real ticket).

### T066 MapUnit sub-objects and UnitManager: UnitPromotions, UnitTurnManager, UnitUpgradeManager, UnitManager
- State: Available
- Depends: T065, T060
- Source: core/src/com/unciv/logic/map/mapunit/UnitPromotions.kt, UnitTurnManager.kt, UnitUpgradeManager.kt, civilization/managers/UnitManager.kt
- Fixtures: tests/src/com/unciv/logic/map/UpgradeTests.kt (TestGame-based: T072)
- Deliverables: csharp/Unciv.Core/Logic/Map/MapUnit/*.cs, csharp/Unciv.Core/Logic/Civilization/Managers/UnitManager.cs, tests (+ retype MapUnit/Civilization seams)
- Gate: `dotnet test --filter FullyQualifiedName~UnitManagerTests`
- Claimed:
- Notes: SEAM RULE (set by T053, applies to T053-T072): the Tile -> TileMap -> Civilization -> City -> MapUnit -> GameInfo port order is acyclic because each ticket compiles only against classes that are already Done plus small engine-owned interfaces (`partial` extensions of the `IGameContext*` interfaces in `Models/Ruleset/Unique/GameContext.cs` / `ConditionalsSeams.cs`, or a new `I<Thing>` for types with no IGameContext seam, e.g. managers), in a file named `<Area>Seams.cs`, containing exactly the members the ticket calls. A field/parameter whose Kotlin type is a not-yet-ported class is typed by its seam. The ticket that later ports that class implements the seam, then retypes the earlier tickets' seam-typed fields/params to the concrete class (a mechanical edit of those files, listed in its Deliverables), keeping reference-identity Equals/GetHashCode (Kotlin Civilization/City/MapUnit/Tile have none). Do not add public API Kotlin lacks beyond the seams. TestGame-based fixtures are deferred to T035/T072. ~870 LOC. UnitManager owns `civ.units`; its placement logic (`placeUnitNearTile`, `addUnit`) is what CityConstructions/CityFounder seams call. Unit iteration order is game state.

### T067 Unit movement: MovementCost, UnitMovement
- State: Available
- Depends: T065, T054
- Source: core/src/com/unciv/logic/map/mapunit/movement/MovementCost.kt, UnitMovement.kt (and PathsToTilesWithinTurn if present)
- Fixtures: tests/src/com/unciv/logic/map/UnitMovementTests.kt, FixedPointMovementTest.kt (TestGame-based: T072)
- Deliverables: csharp/Unciv.Core/Logic/Map/MapUnit/Movement/*.cs, tests (+ retype MapUnit.IUnitMovement)
- Gate: `dotnet test --filter FullyQualifiedName~UnitMovementTests`
- Claimed:
- Notes: SEAM RULE (set by T053, applies to T053-T072): the Tile -> TileMap -> Civilization -> City -> MapUnit -> GameInfo port order is acyclic because each ticket compiles only against classes that are already Done plus small engine-owned interfaces (`partial` extensions of the `IGameContext*` interfaces in `Models/Ruleset/Unique/GameContext.cs` / `ConditionalsSeams.cs`, or a new `I<Thing>` for types with no IGameContext seam, e.g. managers), in a file named `<Area>Seams.cs`, containing exactly the members the ticket calls. A field/parameter whose Kotlin type is a not-yet-ported class is typed by its seam. The ticket that later ports that class implements the seam, then retypes the earlier tickets' seam-typed fields/params to the concrete class (a mechanical edit of those files, listed in its Deliverables), keeping reference-identity Equals/GetHashCode (Kotlin Civilization/City/MapUnit/Tile have none). Do not add public API Kotlin lacks beyond the seams. TestGame-based fixtures are deferred to T035/T072. ~1.2k LOC; delegate the brief. Priority-queue/path tie-breaking must match Kotlin exactly (T001). Float movement costs: do not widen to double. Also lets T040 (MapPathing, CarrierSlotMatcher) replace its seam members with concrete UnitMovement calls: record that follow-up here.

### T068 Diplomacy turn and city states: DiplomacyTurnManager, DeclareWar, CityStateFunctions
- State: Available
- Depends: T059, T064, T066
- Source: core/src/com/unciv/logic/civilization/diplomacy/DiplomacyTurnManager.kt, DeclareWar.kt, CityStateFunctions.kt
- Fixtures: tests/src/com/unciv/logic/civilization/diplomacy/* remaining cases (TestGame-based: T072)
- Deliverables: csharp/Unciv.Core/Logic/Civilization/Diplomacy/*.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~DiplomacyTurnTests`
- Claimed:
- Notes: SEAM RULE (set by T053, applies to T053-T072): the Tile -> TileMap -> Civilization -> City -> MapUnit -> GameInfo port order is acyclic because each ticket compiles only against classes that are already Done plus small engine-owned interfaces (`partial` extensions of the `IGameContext*` interfaces in `Models/Ruleset/Unique/GameContext.cs` / `ConditionalsSeams.cs`, or a new `I<Thing>` for types with no IGameContext seam, e.g. managers), in a file named `<Area>Seams.cs`, containing exactly the members the ticket calls. A field/parameter whose Kotlin type is a not-yet-ported class is typed by its seam. The ticket that later ports that class implements the seam, then retypes the earlier tickets' seam-typed fields/params to the concrete class (a mechanical edit of those files, listed in its Deliverables), keeping reference-identity Equals/GetHashCode (Kotlin Civilization/City/MapUnit/Tile have none). Do not add public API Kotlin lacks beyond the seams. TestGame-based fixtures are deferred to T035/T072. ~1.6k LOC (CityStateFunctions is 850); split at claim time if needed. Concrete Civilization, City, MapUnit. Battle/NextTurnAutomation/CityCombatant references: seam or `TODO(T0xx)` naming a real Planned ticket.

### T069 QuestManager and quest helpers
- State: Available
- Depends: T055, T060, T065
- Source: core/src/com/unciv/logic/civilization/managers/quests/QuestManager.kt, QuestTargetHelpers.kt
- Fixtures: tests/src/com/unciv/logic/civilization/QuestTests.kt (TestGame-based: T072)
- Deliverables: csharp/Unciv.Core/Logic/Civilization/Managers/Quests/*.cs, tests (+ retype Civilization.IQuestManager)
- Gate: `dotnet test --filter FullyQualifiedName~QuestManagerTests`
- Claimed:
- Notes: SEAM RULE (set by T053, applies to T053-T072): the Tile -> TileMap -> Civilization -> City -> MapUnit -> GameInfo port order is acyclic because each ticket compiles only against classes that are already Done plus small engine-owned interfaces (`partial` extensions of the `IGameContext*` interfaces in `Models/Ruleset/Unique/GameContext.cs` / `ConditionalsSeams.cs`, or a new `I<Thing>` for types with no IGameContext seam, e.g. managers), in a file named `<Area>Seams.cs`, containing exactly the members the ticket calls. A field/parameter whose Kotlin type is a not-yet-ported class is typed by its seam. The ticket that later ports that class implements the seam, then retypes the earlier tickets' seam-typed fields/params to the concrete class (a mechanical edit of those files, listed in its Deliverables), keeping reference-identity Equals/GetHashCode (Kotlin Civilization/City/MapUnit/Tile have none). Do not add public API Kotlin lacks beyond the seams. TestGame-based fixtures are deferred to T035/T072. ~840 LOC. Also implements `QuestName -> IQuestImplementation` delegation deferred by T030.

### T070 Civilization TurnManager
- State: Available
- Depends: T068, T064, T066, T069
- Source: core/src/com/unciv/logic/civilization/managers/TurnManager.kt
- Fixtures: tests/src/com/unciv/logic/NextTurnLatencyTest.kt is a benchmark: do not port
- Deliverables: csharp/Unciv.Core/Logic/Civilization/Managers/TurnManager.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~CivTurnTests`
- Claimed:
- Notes: SEAM RULE (set by T053, applies to T053-T072): the Tile -> TileMap -> Civilization -> City -> MapUnit -> GameInfo port order is acyclic because each ticket compiles only against classes that are already Done plus small engine-owned interfaces (`partial` extensions of the `IGameContext*` interfaces in `Models/Ruleset/Unique/GameContext.cs` / `ConditionalsSeams.cs`, or a new `I<Thing>` for types with no IGameContext seam, e.g. managers), in a file named `<Area>Seams.cs`, containing exactly the members the ticket calls. A field/parameter whose Kotlin type is a not-yet-ported class is typed by its seam. The ticket that later ports that class implements the seam, then retypes the earlier tickets' seam-typed fields/params to the concrete class (a mechanical edit of those files, listed in its Deliverables), keeping reference-identity Equals/GetHashCode (Kotlin Civilization/City/MapUnit/Tile have none). Do not add public API Kotlin lacks beyond the seams. TestGame-based fixtures are deferred to T035/T072. ~385 LOC. Wires startTurn/endTurn across the now-concrete managers, cities and units. NextTurnAutomation calls: `TODO(T0xx)` naming the Automation ticket in Planned.

### T071 GameInfo turn handling and BackwardCompatibility
- State: Available
- Depends: T046, T070
- Source: core/src/com/unciv/logic/GameInfo.kt (nextTurn and turn-related members), BackwardCompatibility.kt (GameInfo/GameInfoPreview extensions)
- Fixtures: tests/src/com/unciv/logic/GameSerializationTests.kt (TestGame-based: T072)
- Deliverables: csharp/Unciv.Core/Logic/GameInfoTurn.cs, BackwardCompatibility.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~GameInfoTurnTests`
- Claimed:
- Notes: SEAM RULE (set by T053, applies to T053-T072): the Tile -> TileMap -> Civilization -> City -> MapUnit -> GameInfo port order is acyclic because each ticket compiles only against classes that are already Done plus small engine-owned interfaces (`partial` extensions of the `IGameContext*` interfaces in `Models/Ruleset/Unique/GameContext.cs` / `ConditionalsSeams.cs`, or a new `I<Thing>` for types with no IGameContext seam, e.g. managers), in a file named `<Area>Seams.cs`, containing exactly the members the ticket calls. A field/parameter whose Kotlin type is a not-yet-ported class is typed by its seam. The ticket that later ports that class implements the seam, then retypes the earlier tickets' seam-typed fields/params to the concrete class (a mechanical edit of those files, listed in its Deliverables), keeping reference-identity Equals/GetHashCode (Kotlin Civilization/City/MapUnit/Tile have none). Do not add public API Kotlin lacks beyond the seams. TestGame-based fixtures are deferred to T035/T072. Remaining GameInfo members not taken by T046, plus the BackwardCompatibility work moved here from T046 (all extensions of GameInfo/GameInfoPreview/ModOptions/TechManager; call from `setTransients`). Fulfils the T029 note.

### T072 Deferred TestGame-based fixtures sweep (Tile, Civilization, City, MapUnit, Quest)
- State: Available
- Depends: T035, T062, T063, T064, T067, T068, T069, T071
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
- State: In-Progress [Claude]
- Depends: T039, T010, T016, T017
- Source: core/src/com/unciv/logic/map/mapgenerator/RiverGenerator.kt, NaturalWonderGenerator.kt
- Fixtures: tests/src/com/unciv/logic/map/mapgenerator/NaturalWonderGeneratorTests.kt
- Deliverables: csharp/Unciv.Core/Logic/Map/MapGenerator/*.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~MapGenerat`
- Claimed: 2026-10-24T10:32Z
- Notes: Sub-ticket of T041 (split at claim time, source ~5k LOC). Perlin.cs already exists. Same seed must reproduce Kotlin; compile against seam members for anything owned by unported tickets (SEAM RULE in T053).

### T075 MapGenerator C: mapregions Region, MapGenTileData, MapRegions, RegionStartFinder
- State: In-Progress [Claude]
- Depends: T039, T010, T016, T017
- Source: core/src/com/unciv/logic/map/mapgenerator/mapregions/Region.kt, MapGenTileData.kt, MapRegions.kt, RegionStartFinder.kt
- Fixtures: tests/src/com/unciv/logic/map/MapGenerationTests.kt
- Deliverables: csharp/Unciv.Core/Logic/Map/MapGenerator/MapRegions/*.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~MapGenerat`
- Claimed: 2026-10-24T10:32Z
- Notes: Sub-ticket of T041 (split at claim time, source ~5k LOC). Perlin.cs already exists. Same seed must reproduce Kotlin; compile against seam members for anything owned by unported tickets (SEAM RULE in T053).

### T076 MapGenerator D: mapregions StartNormalizer, MinorCivPlacer
- State: Available
- Depends: T075
- Source: core/src/com/unciv/logic/map/mapgenerator/mapregions/StartNormalizer.kt, MinorCivPlacer.kt
- Fixtures: tests/src/com/unciv/logic/map/MapGenerationTests.kt
- Deliverables: csharp/Unciv.Core/Logic/Map/MapGenerator/MapRegions/*.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~MapGenerat`
- Claimed:
- Notes: Sub-ticket of T041 (split at claim time, source ~5k LOC). Perlin.cs already exists. Same seed must reproduce Kotlin; compile against seam members for anything owned by unported tickets (SEAM RULE in T053).

### T077 MapGenerator E: resourceplacement (Luxury, StrategicBonus, MapRegionResources)
- State: Available
- Depends: T075
- Source: core/src/com/unciv/logic/map/mapgenerator/resourceplacement/*.kt
- Fixtures: tests/src/com/unciv/logic/map/ResourceDiversityTest.kt
- Deliverables: csharp/Unciv.Core/Logic/Map/MapGenerator/ResourcePlacement/*.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~MapGenerat`
- Claimed:
- Notes: Sub-ticket of T041 (split at claim time, source ~5k LOC). Perlin.cs already exists. Same seed must reproduce Kotlin; compile against seam members for anything owned by unported tickets (SEAM RULE in T053).

### Planned (not yet ticketed)
Deferred until the ruleset and map tickets land (decided with the user). Civilization, City, MapUnit, diplomacy core, quests and GameInfo are now ticketed (T046, T053-T072, acyclic via seam interfaces, see the SEAM RULE in T053). Still unticketed: TradeLogic/TradeEvaluation, Battle, GameStarter, UncivFiles/MapSaver/IMediaFinder (save/load), end-to-end determinism over N turns (uses T011), then Automation (AI) last.

## Client track (Godot) — all tickets Blocked until the engine track delivers a playable core

Vision: a Civ 5-style client. Not a copy of Civ 5 assets (see Assets rule), but equivalent look and feel. Create detailed tickets here once their engine dependencies are `Done`; each must name an asset source and license.

### C000 Headless Godot test & snapshot harness
- State: Done (full ticket: backlog-archive.md)

### C001 Godot project skeleton
- State: Blocked
- Depends: C000, T006, and a loadable game state (ticket to be added)
- Deliverables: csharp/Unciv.Godot/ project consuming Unciv.Core, empty scene, CI-friendly headless build
- Gate: `dotnet build Unciv.Godot` (headless; no editor required)

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
- Depends: A002, T030
- Source: `csharp/migration/ai-spec/SPEC.md` §4 (Conquest §4.1, Culture §4.2, Diplomatic §4.3, Spaceship §4.4)
- Deliverables: `csharp/Unciv.Core/AI/GrandStrategy/Scorers/` (`ConquestScorer`, `CultureScorer`, `DiplomaticScorer`, `SpaceshipScorer`), tests
- Gate: `dotnet test --filter FullyQualifiedName~VictoryScorerTests`
- Notes: Uses Brave New World formulas (Tourism/Influence ahead comparisons, World Congress voting power, stat ratios). Returns -100 if victory condition disabled.

### A004 Target-centric Tactical AI: TacticalMoveQueue & recruitment
- State: Blocked
- Depends: A001, T039, T040
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
- Depends: A004
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
