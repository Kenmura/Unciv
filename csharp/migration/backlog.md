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
- State: Blocked
- Depends: T039, T022
- Source: core/src/com/unciv/logic/map/HexMath.kt (`tilesAndNeighborUniqueIndex`, `getDistanceFromEdge`)
- Fixtures: tests/src/com/unciv/logic/map/HexmathTests.kt (`testTileNeighborMappingIsNonConflicting`)
- Deliverables: additions to csharp/Unciv.Core/Logic/Map/HexMath.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~HexMathTests`
- Notes: Deferred from T006.

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
- Depends: T034, T039, T045
- Source: core/src/com/unciv/logic/GameInfo.kt (and what it needs to stand alone: GameInfoPreview is not part of it)
- Fixtures: none yet - design pass first; break into smaller tickets before implementing
- Deliverables: to be defined when T039 is Done
- Gate: `n/a (planning ticket)`
- Claimed:
- Notes: Placeholder created by T023 so that `TODO(T046)` markers have a target. Waiting on it: `TranslationCache.ActiveMods` should use the running game's `gameParameters.mods + baseRuleset` instead of `Translations.TranslationActiveMods` when a game exists (see `ActiveModsCache` in TranslationCache.kt), and the `GameSetupInfo(gameInfo)` constructor of T045. An agent reaching this ticket should split it (GameInfo core / turn handling / serialization) and expand the backlog accordingly. **From T029:** also port `logic/BackwardCompatibility.kt` (all extensions of GameInfo/GameInfoPreview/ModOptions/TechManager; nothing of it was portable before GameInfo exists), with its call from `setTransients`.


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
- State: Available
- Depends: T031, T032
- Source: core/src/com/unciv/models/ruleset/unit/*.kt, models/ruleset/Building.kt, IConstruction.kt, PerpetualConstruction.kt
- Fixtures: tests/src/com/unciv/uniques/UnitUniquesTests.kt (state-dependent parts after T035)
- Deliverables: csharp/Unciv.Core/Models/Ruleset/Unit/*.cs, Building.cs, IConstruction.cs, PerpetualConstruction.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~RulesetUnitTests`
- Claimed:
- Notes: Cost/production math: Int vs Float and roundToInt (half-up helper) are the main traps. Methods needing City/Civilization go behind state interfaces from T027. **From T027:** `IHasUniques.getWeightForAiDecision` (the `is BaseUnit`/`is Building` part with `matchesFilter(unique.params[1], gameContext)`) is not ported; add it with BaseUnit/Building. `IRulesetUnitParameterView.HasGreatPersonUnique` must be overridden to `HasUnique(UniqueType.GreatPerson)` (Kotlin goes through the unique map, conditionals against `EmptyState`), as must `IRulesetTerrainParameterView.HasRegionUnique` (T025 note).

### T034 Ruleset and RulesetCache loader
- State: Available
- Depends: T031, T032, T033
- Source: core/src/com/unciv/models/ruleset/Ruleset.kt, RulesetCache.kt
- Fixtures: tests/src/com/unciv/uniques/ResourceTests.kt (static parts)
- Deliverables: csharp/Unciv.Core/Models/Ruleset/Ruleset.cs, RulesetCache.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~RulesetLoadTests`
- Claimed:
- Notes: File access behind an interface (no libGDX FileHandle). Load order of files/mods must be deterministic and equal to Kotlin's. Implements the interfaces left by T025 (`IRulesetParameterView` via `RulesetMapView<T>`) and T027.

### T035 Test-support port: TestGame helper and deferred fixtures
- State: Available
- Depends: T034
- Source: tests/src/com/unciv/testing/TestGame.kt
- Fixtures: tests/src/com/unciv/testing/TestGame.kt; deferred cases from T028, T030, T033
- Deliverables: csharp/Unciv.Core.Tests/Support/TestGame.cs, ported deferred tests
- Gate: `dotnet test --filter FullyQualifiedName~TestGameTests`
- Claimed:
- Notes: May need partial Tile/Civ stubs; if so mark Blocked and split: extend it again after T039 and the Civilization tickets. **From T027:** port `TimedUniquesTests.testConditionalTimedUniqueStrength` and `...Expires` (only `...IsTriggerable` is ported, in `TimedUniqueTests`).

### T036 RulesetValidator
- State: Available
- Depends: T034
- Source: core/src/com/unciv/models/ruleset/validation/*.kt
- Fixtures: tests/src/com/unciv/uniques/RulesetValidatorTests.kt, tests/src/com/unciv/uniques/UniqueErrorTests.kt, tests/src/com/unciv/uniques/AllObjectsTests.kt
- Deliverables: csharp/Unciv.Core/Models/Ruleset/Validation/*.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~RulesetValidatorTests`
- Claimed:
- Notes: Error ordering and text must be identical to Kotlin. TextSimilarity: exact algorithm port. `Suppression.IsValidFilter` and doc constants are already ported on `UniqueParameterTypeExtensions` in T025; reuse or delegate to them in `Suppression`. **From T027:** `Unique.GetReplacementText(Func<Unique,bool> hasNoErrors)` takes the validator as a delegate (TODO(T036)); pass `u => new UniqueValidator(ruleset).CheckUnique(u, null).Count == 0` or change the signature to take the ruleset. **From T029:** port `UniqueAutoUpdater` and the deferred `DeprecatedUniquesTest` cases 3, 5, 6, 7 (validator reports `ErrorOptionsOnly` for a `DeprecatedUniqueType` unique; auto-updater replacement, two-hop chain `DecreasedBuildingMaintenanceDeprecated` -> `BuildingMaintenanceOld` -> `BuildingMaintenance`, mixed ruleset; TestGame from T035). Pass the validator in as `Unique.GetReplacementText(hasNoErrors)` and add the 10 multi-alternative entries to the golden by running Kotlin with a real ruleset.


### T037 Golden test: load and validate Vanilla and bundled mod rulesets
- State: Available
- Depends: T036, T035
- Source: none (integration test)
- Fixtures: tests/src/com/unciv/logic/LoadGameTest.kt (ruleset portion); ModConstantsTests moved to T049
- Deliverables: csharp/Unciv.Core.Tests/Models/Ruleset/VanillaRulesetTests.cs
- Gate: `dotnet test --filter FullyQualifiedName~VanillaRulesetTests`
- Claimed:
- Notes: Asset JSON is read from the Kotlin repo's android/assets/jsons (read-only). Compare object counts/ids/uniques against Kotlin-generated dump (record how generated).

### T038 Tile core: Tile, TileHistory, TileNormalizer, TileStatFunctions, TileImprovementFunctions
- State: Available
- Depends: T022, T034
- Source: core/src/com/unciv/logic/map/tile/*.kt
- Fixtures: tests/src/com/unciv/logic/map/FreshWaterTests.kt, TileImprovementConstructionTests.kt
- Deliverables: csharp/Unciv.Core/Logic/Map/Tile/*.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~TileTests`
- Claimed:
- Notes: Tile.kt is 1.3k LOC; split off Tile stats/improvements if needed. Civ/City/Unit references go behind interfaces from T027. Kotlin lazy/cached fields must not change observable behaviour. Use namespace `Unciv.Logic.Map.Tiles` (matching `RoadStatus.cs` in T022) so the namespace does not collide with the `Tile` class name.

### T039 TileMap
- State: Available
- Depends: T038
- Source: core/src/com/unciv/logic/map/TileMap.kt
- Fixtures: tests/src/com/unciv/logic/map/VisibilityTests.kt, tests/src/com/unciv/logic/map/UpgradeTests.kt (map-only parts)
- Deliverables: csharp/Unciv.Core/Logic/Map/TileMap.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~TileMapTests`
- Claimed:
- Notes: Tile iteration order is game state: keep Kotlin's ordering. Completing it unblocks T015 (update that ticket's Depends).

### T040 Pathfinding: BFS, AStar, MapPathing, CarrierSlotMatcher, MapVisualization
- State: Available
- Depends: T039
- Source: core/src/com/unciv/logic/map/BFS.kt, AStar.kt, MapPathing.kt, CarrierSlotMatcher.kt, MapVisualization.kt
- Fixtures: tests/src/com/unciv/logic/map/PathfindingTests.kt, tests/src/com/unciv/logic/map/pathingmap/*
- Deliverables: csharp/Unciv.Core/Logic/Map/*.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~PathfindingTests`
- Claimed:
- Notes: Priority-queue tie-breaking must match Kotlin exactly (uses T001). CarrierSlotMatcher may need Unit types: use interfaces or defer part with a real ticket.

### T041 MapGenerator pipeline (audit existing partial work first)
- State: Available
- Depends: T039, T010, T016, T017
- Source: core/src/com/unciv/logic/map/mapgenerator/**
- Fixtures: tests/src/com/unciv/logic/map/MapGenerationTests.kt, ResourceDiversityTest.kt, mapgenerator/NaturalWonderGeneratorTests.kt
- Deliverables: csharp/Unciv.Core/Logic/Map/MapGenerator/*.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~MapGenerat`
- Claimed:
- Notes: Large (mapregions, resourceplacement, rivers, elevation, landmass). Split into sub-tickets at claim time if >1.5k LOC. Same seed must reproduce Kotlin maps: add golden maps generated from Kotlin.

### T042 OdMap import/export
- State: Available
- Depends: T039
- Source: core/src/com/unciv/logic/map/OdMapExport.kt, OdMapImport.kt, OdTerrain.kt
- Fixtures: tests/src/com/unciv/logic/OdMapExportTests.kt, tests/src/com/unciv/logic/OdMapImportTests.kt
- Deliverables: csharp/Unciv.Core/Logic/Map/OdMap*.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~OdMap`
- Claimed:
- Notes: Platform file IO behind an interface.

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
- State: Blocked
- Depends: Civilization, City and MapUnit tickets (not yet ticketed; see Planned), T028
- Source: core/src/com/unciv/models/ruleset/unique/UniqueTriggerActivation.kt, UniqueTriggerExecutors.kt
- Fixtures: tests/src/com/unciv/uniques/ (trigger-related tests; list them when unblocking)
- Deliverables: csharp/Unciv.Core/Models/Ruleset/Unique/UniqueTriggerActivation.cs, UniqueTriggerExecutors.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~UniqueTriggerTests`
- Claimed:
- Notes: Created from T027's review so the work is not lost: T027 deferred `UniqueTriggerExecutors.triggerGlobalAlerts` (needs `Civilization.addNotification`) and the 1.5k-LOC `UniqueTriggerActivation`. Split by trigger family if it exceeds scope.

### Planned (not yet ticketed)
Deferred until the ruleset and map tickets land (decided with the user): Civilization (split ~5: core, diplomacy, policy/tech managers, notifications/quests, vision/stats), Trade, City + CityStats + production, UnitManager/MapUnit (movement, actions), UniqueTriggerActivation, Battle, GameInfo, GameStarter, UncivFiles/MapSaver/IMediaFinder (save/load), end-to-end determinism over N turns (uses T011), then Automation (AI) last.


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
