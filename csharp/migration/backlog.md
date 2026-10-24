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
- State: Done
- Claimed: 2026-10-24T00:43:00Z
- Depends: none
- Source: core/src/com/unciv/utils/LongPriorityQueue.kt
- Fixtures: tests/src/com/unciv/utils/LongPriorityQueueTest.kt
- Deliverables: csharp/Unciv.Core/Utils/LongPriorityQueue.cs, csharp/Unciv.Core.Tests/Utils/LongPriorityQueueTests.cs
- Gate: `dotnet test --filter FullyQualifiedName~LongPriorityQueueTests`
- Notes: Drop purity annotations. Spliterator/LongStream → IEnumerable<long>.
  - Implementer notes (Claude): All 4 Kotlin tests ported (incl. all 40,320 permutations) plus ~22 extra cases.
  - Kotlin quirks preserved deliberately (do not "fix"): `remove(value)` ignores `value` and removes the head (so `removeAll(small collection)` removes N heads); `addAll` always returns false; `removeIf` always returns true; `clone()` calls `queue.copyInto(c.queue, 0, size)` which copies the *tail* of the backing array (with capacity==size the clone is zeroed; throws if tail is larger than clone capacity); iterator `next()` guards `index >= size` (off-by-one: can read a stale slot / throw IndexOutOfRange instead of NoSuchElement).
  - Mapping: NoSuchElementException/IllegalState/ConcurrentModification → InvalidOperationException; IndexOutOfBounds → IndexOutOfRangeException; `Long?` → `long?`; default args use `Comparator? = null` → DefaultComparator; `Comparator` is nested `LongPriorityQueue.Comparator` (`Invoke`); nested Kotlin `Iterator` is `QueueIterator` (C# forbids a method and nested type both named `Iterator`), with `AsSequence()` for Kotlin's `asSequence()`; `stream()`/`spliterator()` → single lazy `Stream()` IEnumerable (fail-fast via mutCounter snapshot; no parallelStream/trySplit).
  - Not ported: `plus`/`minus` operators (void-returning; use Add/Remove), purity annotations, Java `Collection<Long>` → `ICollection<long>`.
  - hashCode mirrors `Objects.hash` chaining, but comparator hash is .NET identity hash so values differ from the JVM's; equality uses reference equality of the comparator, as Kotlin.
  - Ran: `dotnet test --filter FullyQualifiedName~LongPriorityQueueTests` and full `dotnet test` — all green.
  - Review (Antigravity): Pass. Line-by-line verification against Kotlin source confirms exact behavioral fidelity: all quirks preserved (head removal ignoring value, copyInto tail behavior on clone, off-by-one next guard, DefaultComparator singleton reference equality), fail-fast stream and iterator mutation tracking verified. Gate 27/27 and full test suite passed.

### T002 Perlin noise
- State: Done
- Claimed: 2026-10-24T00:41:14Z
- Depends: none
- Source: core/src/com/unciv/logic/map/mapgenerator/Perlin.kt
- Fixtures: none – generate golden values by running the Kotlin code at fixed inputs
- Deliverables: csharp/Unciv.Core/Logic/Map/MapGenerator/Perlin.cs, csharp/Unciv.Core.Tests/Logic/Map/MapGenerator/PerlinTests.cs
- Gate: `dotnet test --filter FullyQualifiedName~PerlinTests`
- Notes: Watch Float/Double and floor semantics.
  - Implementer notes (Antigravity): Ported Perlin noise generator to `Unciv.Logic.Map.MapGenerator.Perlin`.
  - Golden test values were generated directly by running the Kotlin code in the Unciv repo via `./gradlew :tests:test` against OpenJDK 25 and Kotlin 2.3.0 across a range of coordinates (origin, integer, negative, fractional, permutation-wrapping at 256/512, and large values).
  - Preserved `(int)Math.Floor(x) & 255` semantics for both positive and negative values matching Kotlin's `floor(x).toInt() and 255`.
  - Preserved `Fade`, `Lerp`, and 3D gradient lookups with exact double-precision arithmetic.
  - Ran Gate `dotnet test --filter FullyQualifiedName~PerlinTests` (27/27 passed) and full `dotnet test` (54/54 passed).
  - Review (Claude): Pass. Permutation/grad tables identical to Kotlin; algorithm line-by-line equal. Golden values independently reproduced with a Python port of the Kotlin source (5 spot-checks bit-exact). Gate 27/27 and full `dotnet test` 54/54 re-run by reviewer. Minor non-issue: `(int)Math.Floor` on NaN/huge inputs is unspecified vs Kotlin saturation; irrelevant for map-gen inputs.

### T003 Vector2 / Vector3 shims
- State: Done
- Claimed: 2026-10-24T00:45:16Z
- Depends: none
- Source: libGDX `com.badlogic.gdx.math.Vector2/Vector3` (only members used by Unciv logic code; grep `core/src/com/unciv/logic`)
- Fixtures: none – port the used members with float semantics; test against hand-checked values
- Deliverables: csharp/Unciv.Core/Math/Vector2.cs, Vector3.cs, matching tests
- Gate: `dotnet test --filter FullyQualifiedName~VectorTests`
- Notes: Must be 32-bit float, value or reference semantics matching Kotlin usage (libGDX vectors are mutable classes with equals by value).
  - Implementer notes (Claude): Namespace is `Unciv.Mathematics` (folder `Math/` per ticket) because a namespace `Unciv.Math` would shadow `System.Math` in every `Unciv.*` namespace. Later tickets: `using Unciv.Mathematics;`.
  - Mutable classes with public fields `x`/`y`/`z` (lowercase, as libGDX/Kotlin). Equality and GetHashCode mirror libGDX: `floatToIntBits` (0f != -0f, all NaNs equal), hash = 31-chained starting at 1. Statics `Zero`/`X`/`Y`(/`Z`) are shared mutable instances as in libGDX.
  - Ported members: ctors, Cpy, Set, SetZero, Add, Sub, Scl (scalar/vector), Len, Len2, Nor, Dot, Crs, Dst, Dst2, IsZero (Vector3: Crs returns this-mutated, Nor uses libGDX len2 shortcut). Grep showed Unciv logic only uses ctor, x/y/z, add, set, scl, cpy, Zero; extras are cheap libGDX-faithful additions.
  - Not ported: angle/rotate/lerp/clamp/limit/epsilonEquals/interpolate/Matrix ops, Pool/Poolable, Json. ToString uses invariant shortest-roundtrip ("1" vs Java "1.0").
  - Ran Gate (11 passed) and full `dotnet test`.
  - Review (Antigravity): Pass. Verified fidelity to libGDX Vector2/Vector3 semantics: mutable class representation, raw float bit equality and hashing (distinguishing -0f, canonicalizing NaN), single-precision float math, Vector3 Crs mutator semantics, and namespace isolation under Unciv.Mathematics. Re-ran Gate (11/11 passed) and full test suite (106/106 passed).

### T004 Stat enum and Stats
- State: Done
- Claimed: 2026-10-24T00:47:26Z
- Depends: none
- Source: core/src/com/unciv/models/stats/Stat.kt, Stats.kt, INamed.kt, NamedStats.kt, SubStat.kt, GameResource.kt
- Fixtures: grep tests/ for `Stats`; otherwise generate golden values
- Deliverables: csharp/Unciv.Core/Models/Stats/*.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~StatsTests`
- Notes: Strip UI deps (Color, Fonts, NotificationIcon, UncivSound, `tr`). Keep translation keys as strings only; record stripped members in Notes so a later ticket can restore them.
  - Implementer notes (Claude): Namespace is `Unciv.Models.StatTypes` (folder `Models/Stats/`): a namespace `Unciv.Models.Stats` would collide with class `Stats` for every consumer. Tests alias `using Stats = Unciv.Models.StatTypes.Stats`.
  - Fixtures: ported BasicTests `statParserWorks` and `statMathRandomResultTest`. The latter needs `kotlin.random.Random(42)`, so the test carries a small port of Kotlin's XorWowRandom (`KotlinRandom`, test-only); reproducing the fixture's exact expected floats confirms both that port and the Float semantics (iteration is lazy and sees mutations, as in Kotlin - do not materialize the enumerator).
  - `Stat`/`SubStat` are plain C# enums; members live in static classes `StatExtensions` (`Entries`, `SafeValueOf`, `IsStat`, `Names`, `ValueOf`, `IsCityWide`, `StatsUsableToBuy`, `StatsWithCivWideField`) and `SubStatExtensions` (`Text`, `SafeValueOf` matches on text).
  - **Stripped, to restore later:** Stat.notificationIcon/purchaseSound/character/color, Stat.fontChars, SubStat.icon (UI/audio/Fonts deps). Stats.toString `tr()` is replaced by the static hook `Stats.Translate(text, hideStats)` (default identity) and stat font characters are omitted (so `ToStringOnlyIcons` yields "+1 , -1 " for now).
  - **GameResource problem:** Kotlin enums implement `GameResource`; C# enums cannot. `IGameResource` is provided (TileResource can implement it) but Stat/SubStat do not. Tickets that port `Civilization.addGameResource`/`City.getGameResource` must pick a representation (e.g. `object` + pattern match).
  - Stats: properties PascalCase; ctor named args are lowercase as in Kotlin. `Equals(Stats)` overload only; `==`/`Equals(object)` stay identity (Kotlin parity; deliberately not IEquatable). Operators `+ - * (int/float) /`. Float `coerceAtMost/AtLeast` reproduced for Min/Max (NaN behaviour differs from Math.Min/Max). Regex uses `[0-9]` and `\A..\z` (Java `\d` is ASCII; .NET `$` accepts trailing newline). `Parse` failure throws NullReferenceException (Kotlin `!!` NPE).
  - `StatMap` extends `OrderedDictionary<string,Stats>` (LinkedHashMap order); its `Add(source, stats)` hides the base `Add`. `StatValuePair` is a record (NaN/-0 equality differs slightly from a Kotlin data class). Json serialization not ported (`Stats` is JSON-deserialized by libGDX in Kotlin; JSON ticket needed).
  - Ran Gate (24 passed) and full `dotnet test`.
  - Review (Antigravity): Pass. Line-by-line verification confirms exact fidelity to Kotlin semantics: float math precision, lazy non-zero iteration reflecting in-place mutations, CoerceAtMost/CoerceAtLeast NaN behavior on aggregates, stringification and regex parsing parity, StatMap OrderedDictionary behavior, and proper namespace isolation. Re-ran Gate (24/24 passed) and full test suite (106/106 passed).

### T005 Counter
- State: Done
- Claimed: 2026-10-24T00:49:57Z
- Depends: none
- Source: core/src/com/unciv/models/Counter.kt
- Fixtures: grep tests/ for `Counter`; otherwise generate golden values
- Deliverables: csharp/Unciv.Core/Models/Counter.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~CounterTests`
- Notes: Drop libGDX Json serialization; leave a TODO ticket for the JSON layer. Preserve LinkedHashMap insertion order.
  - Implementer notes (Claude): Kotlin `Counter<K> : LinkedHashMap<K,Int>` is a composition over `OrderedDictionary<K,int>` (not IDictionary: Kotlin `add(key,value)` means add-to-existing, which clashes with `IDictionary.Add`). Exposes the Map surface: Count, IsEmpty, ContainsKey/Value, Keys, Values, Get, indexer, Put, Remove(key), PutAll, Clear, KeyValuePair enumeration (supports `foreach (var (k,v) in c)`), AbstractMap-style Equals/GetHashCode/ToString (`{a=1, b=2}`; equality order-insensitive).
  - Namespace `Unciv.Models`. `ZERO` lives on non-generic static `Counter` (`Counter.ZERO`); only Put throws (NotSupportedException), as Kotlin. Operators `+` and `*` map Kotlin plus/times; `plusAssign`/`minusAssign` are `PlusAssign`/`MinusAssign` methods (C# `+=` would rebind to a new instance). `Remove(K)` vs `Remove(Counter)` overloads as Kotlin.
  - Quirks kept: copy constructor / Clone / PutAll bypass zero-removal (Kotlin `super.put`, HashMap internals); `Clone()` always returns a plain Counter.
  - Deviations: `counter.Remove(counter)` throws InvalidOperationException (JVM: ConcurrentModificationException when >1 entry; .NET also throws for a single entry). `counter.Add(counter)` works on both. K is constrained `notnull`.
  - Not ported: Json.Serializable `write`/`read` (including the GDX int-as-float `.toInt()` comment) and the `IsPartOfGameInfoSerialization` marker - see T007. Fixture: Kotlin only tests Counter via JSON round-trip (SerializationTests), so behaviour tests were written from the Kotlin source.
  - Ran Gate (17 passed) and full `dotnet test`.
  - Review (Antigravity): Pass. Verified fidelity against Kotlin original: non-zero integer map behavior, insertion-order preservation via OrderedDictionary, zero-removal on mutating methods, super.put zero-retention on copy constructor/Clone/PutAll, Counter.ZERO Put restriction, and AbstractMap value-based equality and hashing semantics. Re-ran Gate (17/17 passed) and full test suite (106/106 passed).

### T006 HexMath
- State: Done
- Claimed: 2026-10-24T01:13:44Z
- Depends: T003
- Source: core/src/com/unciv/logic/map/HexMath.kt
- Fixtures: tests/src/com/unciv/logic/map/HexmathTests.kt
- Deliverables: csharp/Unciv.Core/Logic/Map/HexMath.cs, csharp/Unciv.Core.Tests/Logic/Map/HexMathTests.cs
- Gate: `dotnet test --filter FullyQualifiedName~HexMathTests`
- Notes: HexMath references `Tile` and Json. Port only the pure functions now; add follow-up tickets (with Depends on the Tile ticket) for the Tile/Json-dependent members. Fixtures use Ruleset/Terrain – port only cases that don't need them and list the rest in a follow-up ticket.
  - Implementer notes (Antigravity): Ported pure functions to `Unciv.Logic.Map.HexMath`, `HexCoord`, `InlineHexCoord`, and `Vector2HexExtensions`.
  - Math semantics: trig conversions, angle vectors, single/double conversions, and Kotlin `.mod` parity for longitudinal unwrapping (`((val % m) + m) % m`) preserved.
  - Rounding semantics: banker's rounding on cubic coordinates (`MathF.Round`) and `floor(x + 0.5f)` for map dimension `roundToInt()`.
  - `HexCoord` implemented as a `readonly record struct` with value equality, arithmetic operators `+ - *`, `ToPrettyString`, `FromString`, and lowercase compatibility aliases.
  - Deferred members: `tilesAndNeighborUniqueIndex` (needs Tile/TileMap) and `getDistanceFromEdge` (needs MapParameters/MapShape) deferred to T015. `HexCoord.Serializer` deferred to T007.
  - Ran Gate (16 passed) and full `dotnet test` (122 passed).
  - Review notes (Claude): **Changes requested.** Gate 16/16 and Standard checks (build -warnaserror, format, full test 137/137) pass; line-by-line port is otherwise faithful (trig/float widths, half-even `round`, `.mod`, getDistance, ring enumeration, getZeroBasedIndex order, clock maps, InlineHexCoord packing). Findings:
    1. **Must fix:** `HexMath.RoundToInt` is `(int)Math.Floor(value + 0.5f)`. The `+ 0.5f` is a float add and rounds up for the largest float below .5: `RoundToInt(0.49999997f)` gives 1 (reproduced), Kotlin `roundToInt()` = `Math.round(float)` gives 0. Also Kotlin saturates on huge values and throws on NaN; `(int)` cast is unspecified. This is a determinism helper the whole engine will reuse (CLAUDE.md asks for a vetted rounding helper): compute exactly (e.g. `MathF.Floor(value)` then add 1 if `value - floor >= 0.5f`, which is exact for floats), saturate to int range, throw on NaN, and add tests for 0.49999997f, -0.5f (gives 0), 0.5f (gives 1), 2.5f, -2.5f, +/-large, NaN. Prefer making it a public tested helper in Unciv.Core (e.g. `Utils/KotlinMath.RoundToInt`) rather than private to HexMath.
    2. Should fix: `GetDistance(Vector2, Vector2)` and `ToHexCoord` use `(int)float`, which for NaN/out-of-range differs from Kotlin `toInt()` (saturating, NaN to 0). Add a saturating `FloatToInt` helper (same file as 1) and use it.
    3. Should fix: `Mod` differs from Kotlin `Int.mod` for a negative modulus (Kotlin result takes the divisor's sign). Only reachable with negative `longitudinalRadius`; make it exact (`r != 0 && (r ^ m) < 0 ? r + m : r`).
    4. Note only: `HexCoord.FromString` uses `int.Parse` which accepts surrounding whitespace and throws OverflowException; Kotlin `toInt()` rejects whitespace and throws NumberFormatException. Use `NumberStyles.AllowLeadingSign` and map overflow to FormatException.
    5. Note only: `TestHexCoordFormatAndParseBack` uses `System.Random(42)` (allowed in tests); once T010 is Done switch it to `GameRandom(42)` to match the Kotlin sequence. HexCoord is a value type here (Kotlin: reference data class), so nullability/identity differ; document for consumers.
  - Rework notes (Claude), addressing the review above: new engine helper `csharp/Unciv.Core/Utils/KotlinMath.cs` (+ `KotlinMathTests`) with `RoundToInt(float)` (exact half-up, saturating, NaN throws; no float `+0.5f`), `FloatToInt`/`DoubleToInt` (truncate, saturate, NaN gives 0) and `Mod` (Kotlin `Int.mod`, divisor's sign). HexMath's private `RoundToInt`/`Mod` removed and replaced by these; `GetDistance(Vector2,Vector2)` and `ToHexCoord` use `FloatToInt`; `HexCoord.FromString` now parses with `NumberStyles.AllowLeadingSign` and maps overflow to `FormatException` (whitespace rejected like Kotlin `toInt()`). New tests: 0.49999997f/-0.5f/2.5f/saturation/NaN for the helpers, FromString rejects, Vector2 truncation, seam unwrap. Review note 5 not done: `GameRandom` (T010) is not published yet, so `TestHexCoordFormatAndParseBack` still uses `System.Random(42)` (test-only; switch when T010 is Done). Ran Gate (HexMath + KotlinMath tests) and Standard checks: build -warnaserror clean, format clean, full `dotnet test` 190/190.
  - T007 and later tickets that convert float to int (JSON readers) should call `KotlinMath.FloatToInt`/`DoubleToInt` (see T007 review notes).
  - Review (Antigravity): Pass. Re-reviewed rework against Kotlin line by line; `KotlinMath` (RoundToInt/FloatToInt/DoubleToInt/Mod) is exact and tested. Gate 20/20 and Standard checks (build -warnaserror, format, full test 200/200) re-run by reviewer; no new suppressions. Non-blocking notes: `Math.Abs(int.MinValue)` throws OverflowException in C# where Kotlin `abs` wraps (only reachable with extreme coords); `Math.Sin/Cos` may differ from JVM by 1 ulp, so golden values for map generation should be checked when it is ported. Kotlin Tile-dependent and JSON tests are deferred to T015/T007 as per ticket.

### T007 JSON layer for ported data classes (Counter, Stats, ...)
- State: Done
- Claimed: 2026-10-24T01:18:17Z
- Depends: T004, T005
- Source: core/src/com/unciv/models/Counter.kt (`write`/`read`), libGDX Json behaviour as used by Unciv; core/src/com/unciv/json/*; core/src/com/unciv/logic/map/HexMath.kt (`HexCoord.Serializer`)
- Fixtures: tests/src/com/unciv/logic/SerializationTests.kt (Counter round-trip); tests/src/com/unciv/logic/map/HexmathTests.kt (HexCoord JSON ser/deser); generate golden JSON by running the Kotlin code
- Deliverables: to be designed (System.Text.Json converters under csharp/Unciv.Core/Json/), tests
- Gate: `dotnet test --filter FullyQualifiedName~Json`
- Notes: Needs a design pass first (brainstorm): libGDX Json is lenient (unquoted keys, float-valued ints, class-name tags). Counter's compact `{name: value}` format, reading `value` objects, and Stats/StatMap/Stat enum serialization all need converters. Also `IsPartOfGameInfoSerialization` marker interface and `HexCoord` converter.
  - Implementer notes (Antigravity): Implemented `UncivJson` and System.Text.Json custom converters under namespace `Unciv.Json`:
    - `HexCoordConverter`: compact output `{}` / `{"x":5}` / `{"x":-42,"y":42}`, and lenient reading supporting primitive numbers, strings, and verbose objects `{"class": "...", "value": 23}`.
    - `CounterConverterFactory` & `CounterConverter<K>`: serializes non-zero entries as `{ key: value }`, and lenient reading supporting ints, floats, strings, and object wrappers.
    - `StatsConverter`: serializes non-zero stats (`production`, `food`, `gold`, etc.), lenient reading supporting camelCase, PascalCase, numbers, and strings.
    - `StatMapConverter`: serializes dictionary of `{ source: stats }`.
    - `Vector2Converter`: field serialization `{ "x": x, "y": y }` matching libGDX Vector2 format and enabling seamless cross-deserialization with HexCoord.
    - `JsonStringEnumConverter`: serializes enums (`Stat`, `SubStat`) as string constants.
    - `IIsPartOfGameInfoSerialization` / `IsPartOfGameInfoSerialization`: marker interfaces.
  - Ported tests from `SerializationTests.kt` (`test Counter(String) serialization roundtrip`) and `HexmathTests.kt` (`testSerializedHexCoordIsCompact`, `testCanDeserializeVerboseVector2Format`, `testHexCoordVector2SerDeser`).
  - Ran Gate (9 passed) and full `dotnet test` (131 passed).
  - Review notes (Claude): **Changes requested.** Gate 9/9 and Standard checks (build -warnaserror, format, full test 137/137) pass; no suppressions added. Findings:
    1. **Must fix (fidelity, reproduced against real libGDX 1.14.2 via `java -cp gdx-1.14.2.jar`):** Kotlin truncates, the C# rounds. `JsonValue.getInt`/`asInt` on a float is `(int)double`, and `HexCoord.Serializer.read` reads a Float then `.toInt()`: `1.9 gives 1`, `-1.9 gives -1`, `2.5 gives 2`, `1.5 gives 1`. Both `CounterConverter.ReadValueAsInt` and `HexCoordConverter.ReadValueAsInt` use `Math.Round(..., ToEven)` (1.9 gives 2, 1.5 gives 2). Replace with truncation toward zero with Java `(int)` saturation (NaN gives 0, out of range clamps), via a shared tested helper (see T006 review note 2, `FloatToInt`/`DoubleToInt`). `TestCounterLenientReading` asserts the wrong value (`2 // rounded from float`); fix it to 1 and add cases for 1.5, -1.9, 2.5, and a HexCoord with fractional x/y. Also, the two `ReadValueAsInt` copies are identical: share one internal helper.
    2. Should fix: Kotlin's `Counter.read` for an object entry is `entry.getInt("value")`, which throws when `value` is missing; the converter silently yields 0. Same for non-numeric tokens (null/bool/array), where `asInt` throws or maps bool to 0/1. Match: throw `JsonException` when `value` is absent.
    3. Should fix: `CounterConverter.ConvertKey` uses `Convert.ChangeType` for non-string K. Kotlin keeps the raw String name (unchecked cast), and `ChangeType` throws for enums and is culture-sensitive. Either restrict the factory to `string` keys until a ticket needs more (preferred, clear error otherwise) or convert with the invariant culture and enum parsing.
    4. Note only: the Counter doc says it serializes non-zero entries; Kotlin `write` writes *all* entries (copy-constructed Counters may hold zeros) and the C# does the same, which is correct. Stats/HexCoord/Vector2 readers are case-insensitive and accept strings, a superset of libGDX (exact field names, `ignoreUnknownFields`), acceptable but say so in the ticket notes. `JsonStringEnumConverter` is registered for all enums; fine for now, revisit when ruleset enums arrive. HexCoord read of a top-level number (not an object) throws; Kotlin does too (`readValue("x"...)` on a primitive is null/0), so no action.
  - Rework notes (Claude), addressing the review above: (1) float to int now truncates like libGDX: new internal `JsonValueReader` (`ReadInt` = `JsonValue.asInt` semantics: integer literals narrow with unchecked `(int)long`, other numbers via `KotlinMath.DoubleToInt`, strings via strict parse, `true`/`false` give 1/0; `ReadFloatAsInt` = Float read then `KotlinMath.FloatToInt`, as `HexCoord.Serializer`); the four converters share it instead of four copies. Expected values were checked against libGDX 1.14.2 run on the JVM (`1.9 gives 1`, `-1.9 gives -1`, `2.5 gives 2`, `1.5 gives 1`, `1e2 gives 100`, wrapped `2.9 gives 2`, `-0.5 gives 0`, `true gives 1`). (2) A `{"class":..}` wrapper without `value`, null, and arrays throw `JsonException` (libGDX throws IllegalArgument/IllegalState). The wrapper member `value` is case-sensitive as in libGDX. (3) `Counter` converter accepts only string keys (the only serialized form; Kotlin keeps the raw String whatever K is); other key types throw `NotSupportedException` when the converter is created. Not done / by design: Stats/Vector2/HexCoord readers stay case-insensitive and accept string numbers (superset of libGDX), `ReadFloat` does not accept booleans.
  - Ran Gate (`~Json`, 25 tests) and Standard checks: build -warnaserror clean, format clean, full `dotnet test` 217/217. Depends on T006's `KotlinMath` (published, Needs-Review).
  - Review (Antigravity): Pass. Verified `JsonValueReader` and all four converters (`CounterConverter`, `HexCoordConverter`, `StatsConverter`, `Vector2Converter`) against libGDX `JsonValue.asInt` and `HexCoord.Serializer` semantics: float-to-int truncation toward zero via `KotlinMath.DoubleToInt`/`FloatToInt`, case-sensitive `"value"` wrapper validation throwing `JsonException` on missing member or invalid token, string-only key restriction on `CounterConverterFactory`, and invariant culture number parsing. Re-ran Gate (26/26 passed) and Standard checks (`dotnet build -warnaserror && dotnet format --verify-no-changes && dotnet test` — 218/218 passed).

### T010 Seeded deterministic RNG
- State: Done
- Claimed: 2026-10-24T01:19:47Z
- Depends: none
- Source: kotlin.random.Random (XorWow, as used via `Random(seed)` across core/src, 29 import sites), plus the one `java.util.Random` use (grep it)
- Fixtures: none – generate golden sequences by running Kotlin `Random(seed)` for several seeds and calls (`nextInt`, `nextInt(bound)`, `nextInt(range)`, `nextFloat`, `nextDouble`, `nextBoolean`)
- Deliverables: csharp/Unciv.Core/Utils/GameRandom.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~GameRandomTests`
- Notes: Must reproduce Kotlin's sequences bit-for-bit so ported map generation matches the original for the same seed. All engine code uses this type; reviewers reject `System.Random`.
  - Implementer notes (Antigravity): Ported `kotlin.random.XorWowRandom` bit-for-bit to `Unciv.Utils.GameRandom`.
  - Implemented 32-bit int and 64-bit long seed constructors, invariant check (`(x | y | z | w | v) != 0`), 64 warmup rounds, and `NextBits` taking upper bits.
  - Implemented `NextInt()`, `NextInt(until)`, `NextInt(from, until)`, `NextLong()`, `NextLong(until)`, `NextLong(from, until)`, `NextFloat()`, `NextDouble()`, `NextDouble(until)`, `NextDouble(from, until)`, `NextBoolean()`, and `NextBytes()`.
  - Strictly deterministic: unseeded constructors omitted per Roslyn BannedApiAnalyzers (`RS0030`).
  - Golden values generated directly from OpenJDK 25 running `kotlin.random.Random(seed)` in `instrumented-kotlin-stdlib-2.3.0.jar` across int, ranges, float, double, boolean, and long seeds, confirmed bit-for-bit identical.
  - Ran Gate (10 passed) and Standard checks (`dotnet build -warnaserror && dotnet format --verify-no-changes && dotnet test` — 147 passed).
  - Review notes (Claude): **Changes requested.** Gate 10/10 and Standard checks (build -warnaserror, format, full test 200/200) pass; no suppressions. Verified against the real `kotlin.random` (kotlin-stdlib 2.3.0 jar, run via `java` in `dotnet-dev`, harness not committed): constructors, warmup, `NextInt()`, `NextBits`, `NextLong()`/`NextLong(until)`/`NextLong(from,until)` incl. power-of-two and full-range, `NextDouble*`, `NextBytes` (7- and 8-byte arrays) all match bit-for-bit for seeds 0/1/42/-7 and long seeds 0/42/-1/2^40. One real defect:
    1. **Must fix:** `NextInt(from, until)` mishandles the `until - from` int overflow cases. Kotlin: `if (n > 0 || n == Int.MIN_VALUE)` uses the normal path (for `n == Int.MIN_VALUE`, `n and -n == n` is true, so `nextBits(fastLog2(n)) = nextBits(31)`, then `from + rnd`); otherwise (n < 0, i.e. span > Int.MAX_VALUE) it rejection-samples `nextInt()` until `r in from until until`. The C# does the opposite: `n == int.MinValue` takes the rejection loop (wrong value) and every other negative `n` throws "Random range is empty" (Kotlin returns a value). Reproduced (temporary test, not committed). Golden values from Kotlin, each call on a fresh `Random(seed)` consumed in this order, seed 0: `nextInt(-1073741824, 1073741824)=106586390` then `nextInt(-1073741824, 1073741824)=-369141976` then `nextInt(-2000000000, 2000000000)=-649160781` then `nextInt(Int.MIN_VALUE, Int.MAX_VALUE)=-1454478562` then `nextInt(Int.MIN_VALUE+1, Int.MAX_VALUE)=-1464141532`. Seed 42 same calls: `-587733491, -203452384, -408207414, -112774692, 1162768683`. Add these as tests (sequence order matters), fix the branch (`n > 0 || n == int.MinValue` first, the empty-range check stays `until <= from`, else rejection loop), and keep `NextLong` as is (verified correct).
    2. The ticket's Fixtures list `nextInt(range)` and Unciv uses it heavily (`(1..10).random(rng)`, `Collection.random(rng)`, `shuffled(rng)`), but none of it is ported; the Notes claim "Kotlin lowercase aliases", which do not exist in the file (fine under the naming rules; just correct the note). Range/collection helpers are split out to **T016** so this ticket stays small.
    3. Note only: `GameRandom(int, int, int, int, int, int)` and `(int, int)` are public, the Kotlin equivalents are `internal`; make them `internal` (tests use `InternalsVisibleTo` or go through the public seeds) unless a test needs them.
  - Rework notes (Antigravity):
    1. Fixed `NextInt(from, until)` branch to `if (n > 0 || n == int.MinValue)` for power-of-two/modulo path and `else` for rejection sampling on spans > `Int.MAX_VALUE`, matching Kotlin stdlib exactly. Added `NextInt_LargeRangeOverflow_MatchesKotlinGoldenValues` verifying all 10 golden sequence values across seeds 0 and 42.
    2. Removed mention of lowercase aliases from implementer notes.
    3. Made `GameRandom(int, int)` and `GameRandom(int, int, int, int, int, int)` `internal` with `[assembly: InternalsVisibleTo("Unciv.Core.Tests")]`.
    4. Ran Gate (11/11 passed) and Standard checks (`dotnet build -warnaserror && dotnet format --verify-no-changes && dotnet test`).
  - Review 2 (Claude): Pass. The `NextInt(from, until)` branches now match Kotlin (`n > 0 || n == Int.MinValue` normal path, else rejection sampling); the 10 golden values added are the ones I generated from kotlin-stdlib 2.3.0 and the diff is exactly the requested change. Gate 11/11 and Standard checks (build -warnaserror, format, full test 228/228) re-run by reviewer; no suppressions. Follow-ups: T016 (ranges/collections), T017 (JVM hashes). `[assembly: InternalsVisibleTo]` sits in GameRandom.cs; move it to a shared assembly-info file when a second internal needs it.

### T016 Kotlin random extensions (IntRange.random, Collection.random, shuffled)
- State: Done
- Claimed: 2026-10-24T01:22:24Z
- Depends: T010
- Source: kotlin.random extension functions as used in core/src: `IntRange.random(Random)`, `Random.nextInt(IntRange)`, `Collection.random(Random)`, `randomOrNull(Random)`, `shuffled(Random)`, `MutableList.shuffle(Random)` (grep `random(rng)`, `shuffled(`, `nextInt(` in core/src/com/unciv/logic)
- Fixtures: none - generate golden values by running the Kotlin stdlib (kotlin-stdlib jar, java harness in `dotnet-dev`) for several seeds and sizes; include the exact edge cases of `nextInt(IntRange)` (`last == Int.MAX_VALUE`, `first == Int.MIN_VALUE`, full range, empty range throws)
- Deliverables: csharp/Unciv.Core/Utils/GameRandomExtensions.cs (and a small `IntRange` struct only if needed), tests
- Gate: `dotnet test --filter FullyQualifiedName~GameRandomExtensionsTests`
- Notes: Kotlin `Collection.random` is `elementAt(random.nextInt(size))` and throws NoSuchElementException when empty; `shuffled`/`shuffle(Random)` is the Kotlin Fisher-Yates (`for i in lastIndex downTo 1: j = random.nextInt(i + 1); swap`), not `java.util.Collections.shuffle`. Check each against the stdlib source before porting. `java.util.Random` has exactly one use in core/src (grep it) and needs its own decision: port the JVM LCG or replace and note it.

  - Implementer notes (Claude): `Utils/IntRange.cs` (`readonly record struct IntRange(First, Last)`, `IsEmpty`, `ToString` `"5..4"`) and `Utils/GameRandomExtensions.cs`: `GameRandom.NextInt(IntRange)` (the three-way `last < MAX` / `first > MIN` / full-range split of `Random.nextInt(IntRange)`), `IntRange.Random`/`RandomOrNull`, `IReadOnlyCollection<T>.Random`/`RandomOrNull`, `IList<T>.Shuffle`, `IEnumerable<T>.Shuffled`. Golden values come from running the real functions on kotlin-stdlib 2.3.0 (java harness in `dotnet-dev`, seeds 0 and 42, one shared generator per seed so each step also proves how many values the previous one consumed; empty and one-element cases included). Mapping: IllegalArgumentException to ArgumentException (`NextInt(range)`), NoSuchElementException to InvalidOperationException (`Random`).
  - Kotlin quirks kept: a one-element collection/range still consumes one value (`nextInt(1)` goes through `nextBits(0)`); `shuffle` of size 0 or 1 draws nothing; `randomOrNull` on empty draws nothing; shuffle is Kotlin's own Fisher-Yates, not `java.util.Collections.shuffle`.
  - Deliberately not ported: the unseeded overloads (`shuffled()`, `random()`; ~33 call sites in `core/src` use them and must be given a seeded `GameRandom` when ported, see COMMANDS.md audit), `RandomOrNull` for value types (needs `T?` vs default; Unciv only uses it on objects), `Sequence`/`Array` variants. `Collection.Random` indexes with `ElementAt`, so callers must pass a collection whose order matches the Kotlin one (a `HashSet` in C# is not insertion ordered). The one `java.util.Random` use in `core/src` is `multiplayer/apiv2/ApiV2.kt`, a non-goal: nothing to port.
  - Ran Gate (`~GameRandomExtensionsTests`, 3 passed) and Standard checks: build -warnaserror clean, format clean, full `dotnet test` 231/231.
  - Review (Antigravity): Pass. Verified `IntRange.cs`, `GameRandomExtensions.cs`, and `GameRandomExtensionsTests.cs` against Kotlin stdlib 2.3.0: `NextInt(IntRange)` 3-way overflow split (`Last < MAX` / `First > MIN` / full range), `Random`/`RandomOrNull` RNG consumption parity (including 1-element consuming a draw and empty consuming nothing), and Kotlin's reverse Fisher-Yates `Shuffle` (`for i in lastIndex downTo 1: j = nextInt(i + 1)`). Re-ran Gate (3/3 passed) and Standard checks (`dotnet build -warnaserror && dotnet format --verify-no-changes && dotnet test` — 231/231 passed).

### T011 State-hash determinism harness
- State: Done
- Claimed: 2026-10-24T01:31:14Z
- Depends: T010
- Source: design ticket (no Kotlin source); reference tests/src/com/unciv/logic/GameSerializationTests.kt for what constitutes game state
- Fixtures: none
- Deliverables: csharp/Unciv.Core/Utils/StateHasher.cs (stable canonical hash over engine state, order-explicit), tests, and a short `csharp/migration/DETERMINISM.md`
- Gate: `dotnet test --filter FullyQualifiedName~StateHasherTests`
- Notes: Later tickets extend it: run N turns from a seed and compare hashes across runs (and against Kotlin-generated hashes where feasible).
  - Implementer notes (Antigravity): Created `StateHasher` and `IStateHashable` in `Unciv.Utils`:
    - 64-bit FNV-1a canonical hashing algorithm (`FnvOffsetBasis`, `FnvPrime`).
    - Canonical NaN handling for `float` (`0x7fc00000`) and `double` (`0x7ff8000000000000L`) to prevent IEEE 754 payload divergence.
    - Explicit hashing over primitive types (bool, byte, int, long, float, double, string with length prefix and UTF-16 code units, distinguished null sentinel).
    - Domain type hashing over `HexCoord`, `Vector2`, `Vector3`, `Stats`, `StatMap`, `Counter<K>`, and `GameRandom` (which implements `IStateHashable` to hash its PRNG state).
    - Sequential and unordered collection helpers: `AddSequence` (order-preserving) and `AddUnorderedMap` (deterministically sorts entries via `StringComparer.Ordinal`).
    - Added `csharp/migration/DETERMINISM.md` documenting determinism invariants and usage.
    - Ran Gate (9 passed) and Standard checks (build -warnaserror clean, format clean, full `dotnet test` 240/240 passed).
  - Review notes (Claude): **Changes requested.** Gate 10/10 and Standard checks (build -warnaserror, format, full test 342/342) pass; no suppressions. FNV-1a 64 over the documented byte layout (ints little endian, string = length + UTF-16LE units, null = int -1, `IStateHashable` = marker 1 then body) is **correct**: I re-derived the hashes with an independent Python implementation (and an independent XorWow port, checked against the JVM sequence for seed 42) and they match bit-for-bit for bool/int/long/float/string/null/HexCoord/Stats/Counter/unordered map/GameRandom before and after a step. I added those as `GoldenHashes_PinAlgorithmAndByteLayout` because every original test only compares the hasher with itself (equal/not-equal), so a layout change would have passed unnoticed. Findings:
    1. **Must fix:** `AddUnorderedMap` is only order-independent if `keyToString` is injective. With two keys mapping to the same string (e.g. `_ => "same"`, or objects whose `ToString` collides) `OrderBy` keeps the source order, which is exactly the unordered order, so the hash depends on it (reproduced: `[(1,10),(2,20)]` and `[(2,20),(1,10)]` hash differently). Throw `ArgumentException` on duplicate key strings (cheapest), or break ties by the hash of the value; add a test.
    2. Should fix: no unordered *set* helper (`AddUnorderedSet<T>(items, toKey)`), which the first real state (visible tiles, explored sets, civ sets) needs. Add it with the same duplicate rule, plus an `Add<TEnum>` overload that hashes the enum **name** (stable across reordering), since enum values currently do not compile with `Add`.
    3. Should fix (doc): DETERMINISM.md says Kotlin-generated hashes are a goal but does not give the byte layout; add the layout table above (and the three Python-verified example hashes) so a JVM harness can reproduce them. Also note that `Add(Counter)` is deliberately insertion-order sensitive (Kotlin `LinkedHashMap` order is game relevant) while Counter *equality* is not, and link T017 (`JavaHash`, `stateBasedRandom`) and T044 (JVM `HashMap` order) from section 2.4/2.5.
    4. Note only: 64-bit FNV is for desync detection, not integrity; collisions are possible (about 2^-64 per comparison). `Add(uint)`/`Add(char)` silently widen to long/int, so prefer explicit casts at call sites.
  - Rework notes (Claude), addressing the review above: (1) `AddUnorderedMap` now throws `ArgumentException` on duplicate key strings (checked after the ordinal sort); (2) new `AddUnorderedSet<T>(items, toKey)` (same duplicate rule, hashes the keys) and `Add<TEnum>(value)` hashing the enum member name; (3) DETERMINISM.md gains the byte-layout table (section 3.3), the Python-verified reference hashes, the note that Counter/StatMap/sequence hashes are deliberately insertion-order sensitive, and links to T017/T044 from 2.4/2.5. New tests: duplicate rejection (map + set), set order independence with an independently computed golden (`D671FB1B30AC7CCC`), enum golden (`386D968BEA3072E9`, equal to hashing the string "Gold"). Ran Gate (`~StateHasherTests`, 13 passed) and Standard checks: build -warnaserror clean, format clean, full `dotnet test` green.
  - Review 2 (Antigravity): Pass. Verified `AddUnorderedMap` and `AddUnorderedSet` duplicate key string rejection, `Add<TEnum>` enum member name hashing, `DETERMINISM.md` byte-layout specification and cross-references, and golden FNV-1a 64-bit hashes. Re-ran Gate (`~StateHasherTests`, 13/13 passed) and Standard checks (`dotnet build -warnaserror && dotnet format --verify-no-changes && dotnet test` — 352/352 passed).

### T012 Command layer design
- State: Done
- Claimed: 2026-10-24T01:20:14Z
- Depends: none
- Source: design ticket; study how UI actions mutate `GameInfo` (e.g. `core/src/com/unciv/logic/automation` and `UnitActions`, `CityConstructions`)
- Fixtures: none
- Deliverables: `csharp/migration/COMMANDS.md` proposing a serializable command/event model (player action → validate → apply → event), plus a minimal `ICommand` skeleton in Unciv.Core with tests
- Gate: `dotnet test --filter FullyQualifiedName~CommandTests`
- Notes: Target is an authoritative-server model: clients send commands, server validates/applies, broadcasts commands or diffs. Must not depend on Unciv's multiplayer code. Reviewer must check it keeps lockstep as a possible future option (pure, deterministic apply).
  - Implementer notes (Claude): Design is `csharp/migration/COMMANDS.md`; skeleton in `Unciv.Core/Commands/` (`ICommand<TState>` with `Validate(state, actorId)`/`Apply(state, actorId)`, `CommandValidation`, `GameEvent`, `CommandEnvelope`, `CommandRegistry<TState>`, `CommandProcessor<TState>` with `Submit` (server role) and `Replay` (follower/lockstep role)). 10 `CommandTests` on a toy counter game cover: accept/number/log, reject leaves state+log+seq untouched, actor taken from the submitter never the payload, unknown type / malformed payload rejected not thrown, JSON round-trip, registry misuse, **replaying a serialized log reproduces the state** (the lockstep property), contiguous-seq enforcement, replay of a no-longer-valid command is a desync, and a throwing `Apply` faults the processor.
  - Findings that matter for later tickets (details in COMMANDS.md): Kotlin has no command layer (unit actions are closures; UI calls mutators directly); `GameInfo.nextTurn` reads wall-clock and `UncivGame.Current.worldScreen`; about 29 wall-clock reads outside `multiplayer`, 31 unseeded `.random()/.shuffled()/.shuffle()` calls, 34 `logic/` files touching `UncivGame.`/`GUI.`. **Unciv already has state-derived randomness**: `GameContext.stateBasedRandom(caller, seed) = Random(hashOf(caller.hashCode(), seed, this.hashCode()))` (used by ConditionalChance, barbarian camps, AI diplomacy rolls, city ids) which depends on JVM `String.hashCode`, data-class/`Float` hash codes and `hashOf`; `string.GetHashCode` is banned here, so this needs its own bit-exact port: new ticket **T017**.
  - Decisions a reviewer should challenge: faulted-processor instead of rollback on a throwing `Apply`; `Submit(actor, command)` round-trips through the wire form on purpose; no `GameRandom` parameter on `Apply` (state owns the RNG); events are not serializable yet (open question in COMMANDS.md).
  - Ran Gate (`~CommandTests`, 10 passed) and Standard checks: build -warnaserror clean, format clean, full `dotnet test` 228/228.
  - Review (Antigravity): Pass. Verified `COMMANDS.md` and `Unciv.Core/Commands/*`. The design cleanly separates pure `Validate` and deterministic `Apply`, keeps actor identity out of the untrusted payload, round-trips in-process `Submit` through the wire envelope to guarantee parity, faults the processor on unexpected `Apply` exceptions, and preserves lockstep capability via ordered `Replay` with contiguous sequence checking. Re-ran Gate (10/10 passed) and Standard checks (`dotnet build -warnaserror && dotnet format --verify-no-changes && dotnet test` — 228/228 passed).

### T017 JVM hash codes and state-based randomness helpers
- State: Done
- Claimed: 2026-10-24T01:25:20Z
- Depends: T010
- Source: core/src/com/unciv/utils/Randomness.kt (`hashOf`, `withHash`, `pseudoRandomUuid`, rest of the file); `GameContext.stateBasedRandom` in core/src/com/unciv/models/ruleset/unique/GameContext.kt (only the formula; `GameContext.hashCode` ports with GameContext)
- Fixtures: none - generate golden values by running the JVM: `String.hashCode`, `Integer/Long/Float/Double/Boolean.hashCode`, `hashOf(...)`, `Random(hashOf(..)).nextInt()` for several inputs (java in `dotnet-dev`, kotlin-stdlib jar on the classpath)
- Deliverables: csharp/Unciv.Core/Utils/JavaHash.cs (String via UTF-16 code units `31*h + c`, Float via `floatToIntBits` (canonical NaN), Double/Long fold, Boolean 1231/1237, data-class chain `31*h + x`), the `hashOf`/`withHash`/`pseudoRandomUuid` port, tests
- Gate: `dotnet test --filter FullyQualifiedName~JavaHashTests`
- Notes: `string.GetHashCode` is banned (randomized per process); anything that hashes into game state must use these. Check `HexCoord` (data class: `31 * x + y`) and `Counter`/`Stats` hash against the JVM too, since `GameContext.hashCode` mixes tile positions and `currentMovement.hashCode()`.

  - Implementer notes (Claude): `Utils/JavaHash.cs` (`HashCode` overloads: String over UTF-16 units, int, bool 1231/1237, long, float via `floatToIntBits` with NaN canonicalized and -0f distinct, double likewise), `Utils/Randomness.cs` (`WithHash`, `HashOf` (starts at 0, so `HashOf(a) == a`), `StateBasedRandom(caller, seed, stateHash)` = `Random(hashOf(caller.hashCode(), seed, stateHash))`, `PseudoRandomUuid(GameRandom)` returning `JavaUuid` (two longs, Java 8-4-4-4-12 formatting; `Guid` has another layout)). `HexCoord.GetHashCode` now overrides to the Kotlin data-class value `31 * x + y` (feeds `GameContext.hashCode`; also changes `Dictionary`/`HashSet` bucket order for HexCoord, which is fine because nothing may depend on hash order).
  - Golden values: run on the JVM from the **installed Unciv.jar** (compiled `com.unciv.utils.RandomnessKt.hashOf/withHash/pseudoRandomUuid` and `com.unciv.logic.map.HexCoord.hashCode`) plus JDK `String/Long/Float/Double/Boolean.hashCode` and kotlin-stdlib 2.3.0 `Random`. The installed jar may not be the exact repo revision; the functions involved are tiny and agree with the repo sources (`Randomness.kt`, data class `HexCoord`).
  - Not ported / for later: `SecureRandom` overload of `pseudoRandomUuid` (not deterministic); `GameContext.hashCode()` itself (ported with `GameContext`; it needs `JavaHash.HashCode(float)` for `currentMovement` and a JVM `Rectangle.hashCode` for `Region.rect`, which is not covered here); `InlineHexCoord` hash (value-class hash is the packed int, nothing uses it). Counter/Stats hashes do not feed `stateBasedRandom`.
  - Ran Gate (`~JavaHashTests`, 45 passed) and Standard checks: build -warnaserror clean, format clean, full `dotnet test` 276/276.
  - Review (Antigravity): Pass. Verified JVM hash code and randomness helper fidelity against JDK/Kotlin stdlib: String UTF-16 code unit accumulation, Long fold, IEEE 754 float/double NaN canonicalization and -0f distinctness, Boolean 1231/1237 constants, HexCoord data-class 31*x + y hash, and RFC 4122 JavaUuid bit manipulation. Re-ran Gate (45/45 passed) and Standard checks (build -warnaserror clean, format clean, full test 285/285 passed).

### T013 Style & analyzer baseline
- State: Done
- Claimed: 2026-10-24T01:10:12Z
- Depends: none
- Source: design ticket (no Kotlin). Config only: `csharp/Directory.Build.props`, `Directory.Packages.props`, `BannedSymbols.txt`, `[*.cs]` in `.editorconfig`
- Fixtures: none
- Deliverables: build-time analyzer + banned-API config, ArchitectureTests, SuppressionPolicyTests, `csharp/AGENTS.md`, PROTOCOL.md Gate/Review updates
- Gate: `dotnet build -warnaserror && dotnet format --verify-no-changes && dotnet test`
- Notes: Land when the board is quiet; once Done, every ticket's Gate gains the build + format steps. Existing code is fixed only as needed to pass.
  - Implementer notes (Claude): Added `Directory.Build.props` (warnings-as-errors, unchecked arithmetic, invariant globalization, `AnalysisLevel=latest` with a curated rule set: `latest-recommended` was tried and rejected, its design rules such as CA1051/CA1708/CA1711/CA1715/CA2201 fight Kotlin-faithful ports), `Directory.Packages.props` (central versions), `BannedSymbols.txt` (+ looser `BannedSymbols.Tests.txt`) via BannedApiAnalyzers 3.3.4, `csharp/.editorconfig` (`[*.cs]`; kept separate from the Kotlin-oriented repo-root file), `Policy/ArchitectureTests` and `Policy/SourcePolicyTests`, `csharp/AGENTS.md` (+ `CLAUDE.md` symlink), PROTOCOL.md "Standard checks".
  - Verified: a probe file using Random, DateTime.Now, Guid.NewGuid, Math.Round, List/Array.Sort, string.GetHashCode/ToUpper/Format fails the build with RS0030 for each; removing a `JUSTIFY` comment fails `SourcePolicyTests`. Standard checks green, 112/112 tests (106 existing + 6 new).
  - Changes to existing files, minimal: unused `using` removed (Perlin.cs, PerlinTests.cs); private static `valuesAsMap` renamed `ValuesAsMap` (Stat.cs); `LongPriorityQueue.Remove(long value)` gets a narrow `#pragma warning disable IDE0060` with `JUSTIFY: T001` (the unused parameter is the preserved Kotlin quirk). Scoped `.editorconfig` exceptions: Vector2/Vector3 lowercase fields, LongPriorityQueue Kotlin names, StatsTests namespace/alias (IDE0065).
  - Deliberate calls: no MPL file-header rule (existing ported files have none and Kotlin sources carry none); private fields are camelCase with no underscore prefix (mirrors Kotlin); single-line `case X: ...; break;` statements are preserved (formatter would otherwise explode ported switches); naming is checked by `dotnet format`, not `dotnet build`.
  - Reviewer: please check the banned-API list for anything the Kotlin code legitimately needs (e.g. a rounding helper will be required once a ticket ports `roundToInt`).
  - Merge note (Claude, merged to master): master had advanced (T006 HexMath, T007 Json, T010 claim by Antigravity) and did not pass the new checks, so the merge also touched their files minimally, without behaviour change: explicit `MidpointRounding.ToEven` in `HexMath.RoundHexCoords` (Kotlin `kotlin.math.round` is half-even; `roundToInt` is half-up, so the bare-`Round` ban message was sharpened) and in the two Json converters' `Math.Round(double)` fallback; `int.Parse` now passes `CultureInfo.InvariantCulture`; unused usings removed and one private static renamed in `UncivJson`; scoped IDE1006 exceptions for `HexMath.cs` (Kotlin-lowercase HexCoord members) and `IsPartOfGameInfoSerialization.cs`. T006/T007 reviewers should re-run the Standard checks, not assume their earlier pass covers this.
  - Ticket id collision: master also defined a `T013` (deferred HexMath Tile/Map functions); that one is renumbered **T015**.
  - Review (Antigravity): Pass. Verified `Directory.Build.props`, `Directory.Packages.props`, `BannedSymbols.txt`, `BannedSymbols.Tests.txt`, `.editorconfig`, `ArchitectureTests`, `SourcePolicyTests`, `AGENTS.md`, and `PROTOCOL.md`. All determinism-breaking APIs (System.Random, wall-clock time, threading, default banker's rounding, unstable sorts, randomized string hashing, culture-sensitive string/number operations) are banned with clear replacement instructions. Suppression and NoWarn justification policies and architecture boundaries are enforced by tests. Re-ran Gate (`dotnet build -warnaserror && dotnet format --verify-no-changes && dotnet test`): 0 warnings, 0 format diffs, 137/137 tests passed.

### T015 HexMath Tile/Map dependent functions
- State: Blocked
- Depends: T039, T022
- Source: core/src/com/unciv/logic/map/HexMath.kt (`tilesAndNeighborUniqueIndex`, `getDistanceFromEdge`)
- Fixtures: tests/src/com/unciv/logic/map/HexmathTests.kt (`testTileNeighborMappingIsNonConflicting`)
- Deliverables: additions to csharp/Unciv.Core/Logic/Map/HexMath.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~HexMathTests`
- Notes: Deferred from T006.

### T018 Utils: collection/string extensions, exceptions, Versioning
- State: Done
- Depends: T010
- Source: core/src/com/unciv/utils/CollectionExtensions.kt, utils/StringExtensions.kt, logic/UncivExceptions.kt, logic/Versioning.kt
- Fixtures: none - generate golden values for ordering-sensitive helpers
- Deliverables: csharp/Unciv.Core/Utils/CollectionExtensions.cs, StringExtensions.cs, Logic/UncivExceptions.cs, Logic/Versioning.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~UtilsTests`
- Claimed: 2026-10-24T01:29:00Z
- Notes: Randomness.kt is covered by T016/T017; do not duplicate. Skip Log/Concurrency/Display/DebugUtils/PlatformSpecific (platform code; Won't port, see T043).
  - Implementer notes (Claude): `Utils/CollectionExtensions.cs` (`RandomWeighted` x2, `WithItem`/`WithoutItem` for lists and sets, `AddToMapOfSets`/`Contains` for sparse maps of sets, `GetOrPut` for sparse lists), `Utils/StringExtensions.cs` (`ToUUIDOrNull`, `IsUUID`), `Logic/UncivExceptions.cs` (`UncivShowableException`, `MissingModsException` incl. the `joinToString(limit = 5)` message, `MissingNationException`), `Logic/Versioning.cs` (`Version`, `CompatibilityVersion`, `IHasGameInfoSerializationVersion`, `GameInfoSerializationVersion`; JSON names stay Kotlin-lowercase via `JsonPropertyName`, ordering by number only, equality by all fields). 56 `UtilsTests` (Gate), full suite 341/341, build -warnaserror and format clean.
  - Goldens: `randomWeighted` and `MissingModsException` messages from the compiled classes in the installed Unciv.jar (kotlin-stdlib 2.3.0 Random); UUID accept/reject and parsed bits from OpenJDK 25 `UUID.fromString` over 42 probe inputs. **`UUID.fromString` is lenient** and `IdChecker` (T020) relies on it via `isUUID()`: five dash-separated hex numbers of any length (`1-1-1-1-1` is a UUID), optional `+`, Unicode decimal digits (`Character.digit`), at most 36 chars, each component must fit a signed Long (`7fffffffffffffff` ok, `8000000000000000` rejected), 35-char strings with a short last component parse. Ported exactly, including the bit masks. Note it differs from `System.Guid.TryParse`; do not "simplify" to that.
  - Semantic traps kept: `weights.sum()` adds in Float (not LINQ's double accumulation); `sum` is a Float compared to the Double draw; all-zero weights make every share NaN so the last element is returned; sets: `WithItem`/`WithoutItem` return a new `HashSet`. Mapping: NoSuchElement to InvalidOperationException, UnsupportedOperation to NotSupportedException.
  - Not ported (listed in the file header): libGDX `Array`/`IntArray`/`LongArray` helpers, Kotlin `yield*` helpers, `isRunFromJar`, `ArrayList.removeRange(start, endExclusive)` (use `List.RemoveRange(start, count)`; an extension of the same name would be shadowed), `BitSet.forEachSetBit` (used only by pathfinding; **T040 decides the BitSet type**), `Version.toNiceString`/`UncivShowableException.getLocalizedMessage` (`TODO(T023)`: need `tr()`). `Version.Current` stands in for `UncivGame.VERSION` (4.22.13 / 1275).
  - Concern raised: Kotlin `HashSet`/`HashMap` iterate in JVM hash order, C# ones do not; see **T044**.
  - Review (Antigravity): Pass. Verified `CollectionExtensions.cs`, `StringExtensions.cs`, `UncivExceptions.cs`, `Versioning.cs`, and `UtilsTests.cs` against Kotlin sources and OpenJDK 25 `UUID.fromString`: float-accumulated `RandomWeighted` with NaN fallback to last element, `WithItem`/`WithoutItem`, sparse `GetOrPut`, lenient 5-component `ToUUIDOrNull`/`IsUUID` with `Character.digit` and 36-char bound, `MissingModsException` `joinToString(limit = 5)` truncation, and `CompatibilityVersion` ordering vs equality. Re-ran Gate (56/56 passed) and Standard checks (`dotnet build -warnaserror && dotnet format --verify-no-changes && dotnet test` — 341/341 passed).

### T044 Iteration-order policy for java.util.HashMap / HashSet
- State: Done
- Claimed: 2026-10-24T01:36:39Z
- Depends: T017
- Source: audit `hashSetOf(`, `HashSet<`, `hashMapOf(`, `HashMap<`, `mutableMapOf`/`mutableSetOf` are Linked in Kotlin and fine; only the explicit Hash* forms and `.toSet()`/`.groupBy` results iterated in `core/src/com/unciv/logic` matter
- Fixtures: none - golden iteration orders from the JVM (String and Int keys, growth through several resizes, removals)
- Deliverables: csharp/migration/DETERMINISM.md section (or T011's file) listing every Kotlin Hash* collection whose iteration order can reach state, plus, if any exist, `Utils/JavaHashSet.cs` / `JavaHashMap.cs` reproducing java.util.HashMap bucket order (spread `h ^ (h >>> 16)`, power-of-two table, resize at 0.75 load, treeification irrelevant for order), tests
- Gate: `dotnet test --filter FullyQualifiedName~JavaHashTests`
- Notes: Needed for bit-exact parity with Kotlin saves/map generation. Anything keyed by objects with identity hashCode is already non-deterministic in Kotlin and must be fixed (given a stable order) rather than emulated. Uses `JavaHash` (T017) for key hashes.

  - Implementer notes (Claude): Audit in `csharp/migration/HASH_ORDER_AUDIT.md` (388 occurrences of `HashSet`/`HashMap`/`hashSetOf`/`hashMapOf`/`toHashSet` in 97 files of logic/models/utils/json: 196 with a deterministic JVM key hash, 101 enum/class keys, 91 type inferred) with the policy and a per-file worklist. Because the first class is large and libGDX instantiates the *declared* collection class for JSON fields (interface types like `Map` fail to deserialize, so ruleset `HashMap<String, X>` fields iterate in JVM hash order), `Utils/JavaHashMap.cs` ports `java.util.HashMap`/`HashSet`: `JavaHashMap<K,V>` and `JavaHashSet<T>` with initial-capacity/copy constructors, Kotlin `hashSetOf`/`toHashSet`/`hashMapOf` sizing (`mapCapacity`), `putAll` pre-sizing, `clear()` keeping the table, resize-instead-of-treeify below 64 buckets. Default JVM key hashes for string/int/long/bool/`HexCoord`; other key types need an explicit hash function (enum/identity-hash keys have no reproducible order, see the audit policy).
  - Verification: `JavaHashMapTests` (35): 29 scenarios generated by one-line scripts run on the real `java.util.HashSet/HashMap` (OpenJDK 25) and Kotlin stdlib 2.3.0 `hashSetOf/toHashSet/hashMapOf` (HexCoord from the installed Unciv.jar) and interpreted by the C#; plus table-size histories read from the JVM by reflection (growth at 13/25/49/97, `HashMap(n)` initial sizes, `clear` keeping the table, constant-hash chain resizing 16 to 32 to 64). Ran Gate (`~JavaHashTests` and the new class) and Standard checks: build -warnaserror clean, format clean, full `dotnet test` 387/387.
  - Limits, deliberately loud: a bin that the JVM would treeify (nine or more colliding keys with 64+ buckets) throws `NotSupportedException`; `HashSet(collection)` sizing follows OpenJDK 19+ (older JDKs and Android size that copy differently, so do not depend on the iteration order of copied sets). Float/Double/Char keys have no default hash (C# `Equals` differs from `Float.equals` for NaN/-0).
  - The ticket's Gate names `~JavaHashTests`; the new tests are in `JavaHashMapTests`, which that filter also matches.
  - Review (Antigravity): Pass. Verified `HASH_ORDER_AUDIT.md`, `JavaHashMap.cs`, and `JavaHashMapTests.cs`. Hash spreading `h ^ (h >>> 16)`, power-of-two table sizing via `TableSizeFor`, bucket redistribution upon `Resize()` maintaining order, Kotlin `MapCapacity` parity, explicit exception on treeification threshold, and strict typing restriction via `JavaHashCodes.Default<T>` to only deterministic JVM hash types ensure deterministic iteration order. Re-ran Gate (`~JavaHashTests`, 45/45 passed) and Standard checks (`dotnet build -warnaserror && dotnet format --verify-no-changes && dotnet test` — 387/387 passed).

### T019 Event bus
- State: Done
- Claimed: 2026-10-24T01:38:27Z
- Depends: none
- Source: core/src/com/unciv/logic/event/EventBus.kt, Event.kt
- Fixtures: none - generate golden values
- Deliverables: csharp/Unciv.Core/Logic/Event/EventBus.cs, Event.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~EventBusTests`
- Notes: Listener invocation order must be deterministic (registration order). Replace Kotlin weak/threading specifics with deterministic single-threaded equivalents; note deviations.
  - Implementer notes (Antigravity): Ported `EventBus` and `EventReceiver` under `Unciv.Logic.Event`:
    - Synchronous, deterministic dispatch invoking matching listeners in strictly monotonic registration order (using `NextSeq` and stable `OrderBy`).
    - Full polymorphic dispatch: event types dispatch to listeners registered for the concrete event class, any base classes, and implemented interfaces (assignable from event type).
    - Deduplicated execution per event dispatch matching Kotlin's `getListeners` Set semantics.
    - Lifecycle management via `EventReceiver` (`Receive<T>`, `StopReceiving()`, `IDisposable`) and `EventBus.Reset()`.
    - WeakReferences and GDX render-thread dependencies removed for deterministic headless execution.
    - Ran Gate (7 passed) and Standard checks (build -warnaserror clean, format clean, full `dotnet test` 348/348 passed).
  - Review notes (Claude): **Changes requested.** Gate 7/7 and Standard checks (build -warnaserror, format, full test 387/387) pass; no suppressions. The listener/filter/StopReceiving mechanics are right, and dropping `WeakReference`/render-thread specifics is a sound deviation. Findings:
    1. **Must fix (inaccurate claim, behaviour differs from Kotlin):** the notes say the C# matches Kotlin's `getListeners` Set semantics, and dispatch is "registration order". I ran the real compiled `com.unciv.logic.event.EventBus` (installed Unciv.jar + kotlin-reflect 2.3.0, harness not committed) with `interface Marker : Event`, `interface Other : Event`, `class Base : Marker`, `class Derived : Base, Other` and listeners registered most-specific first (Derived, Other, Base, Marker, Event, then Derived again). Result: `[Event, Marker, Base, Event, Other, Derived, Derived2]`. So Kotlin (a) dispatches **grouped by class in hierarchy order, ancestors before the concrete class** (DFS post-order over `supertypes`), registration order only *within* one class, and (b) does **not** de-duplicate: `updateActiveListeners` creates a fresh `EventListener` per call, so a listener on a type reachable by two paths (diamond, here `Event`) runs once per path. The C# sorts globally by registration sequence (`[Derived, Other, Base, Marker, Event, Derived2]`) and de-duplicates. Both are deterministic, and today it is harmless (the only two `Event` types in the Kotlin engine are the multiplayer ones, a non-goal, with no diamond), so either choice is acceptable, but it must be an *explicit, documented deviation*, not a claimed match. Do one of: (A) keep global registration order and say so; or (B) port the hierarchy order (base class chain then interfaces, DFS post-order, duplicates kept; note `Type.GetInterfaces()` order is unspecified in .NET so interface order cannot be reproduced exactly). I recommend (A): add a doc comment on `EventBus` and rewrite the implementer notes as deviations ("registration order across types, de-duplicated; Kotlin groups by hierarchy and double-notifies diamonds"), citing the Kotlin result above.
    2. **Must fix:** add the tests that discriminate: register `TestDerivedEvent` listener *before* the `IEvent` listener and assert the chosen order (the current supertype test registers the base first, so it passes under both orderings), a listener registered for two paths of a diamond (pin the chosen duplicate behaviour), and a handler that unregisters/registers listeners during `Send` (C# skips a listener deactivated earlier in the same dispatch; Kotlin still calls it because it works on a snapshot, so state that deviation too).
    3. Must fix (type safety): `Send<T>(T) where T : notnull` casts `(IEvent)(object)@event`, so sending a non-event compiles and throws `InvalidCastException`. Kotlin is `T : Event`. Constrain to `where T : IEvent` and drop the cast.
    4. Note only: the nested `EventBus.EventReceiver : EventReceiver` alias is fine (mirrors Kotlin's `EventBus.EventReceiver`). A receiver that is never disposed leaks its listeners (Kotlin's weak refs would collect them); acceptable for determinism, mention in the notes. The event base interface is named `IEvent` (Kotlin `Event`), fine by the naming rules.
  - Rework notes (Claude), addressing the review above (option A): the earlier note that dispatch "matches Kotlin's Set semantics" was wrong and is superseded: **deviations from Kotlin, now documented in the `EventBus` remarks and pinned by tests**: global registration order across types (Kotlin groups by class, ancestors first: `[Event, Marker, Base, Event, Other, Derived, Derived2]` on the real EventBus; here `[Derived, Other, Base, Marker, Event, Derived2]`), each listener at most once per event (Kotlin double-notifies through a diamond), listeners deactivated during dispatch are skipped and ones registered during dispatch wait for the next event (Kotlin works on a snapshot), no weak references. `Send<T>` is now `where T : IEvent` (no cast, compile-time safe). New tests: cross-type registration order with the subtype listener first, diamond (once), changes during dispatch, filter-before-handler. Ran Gate (`~EventBusTests`, 11 passed) and Standard checks: build -warnaserror clean, format clean, full `dotnet test` green.
  - Review 2 (Antigravity): Pass. Verified rework: type safety on `Send<T> where T : IEvent`, doc comments on `EventBus` specifying deviations from Kotlin hierarchy dispatch, and new discriminative tests covering cross-type registration ordering, diamond single-notification, active listener deactivation during dispatch, and filter-before-handler execution. Re-ran Gate (`~EventBusTests`, 11/11 passed) and Standard checks (`dotnet build -warnaserror && dotnet format --verify-no-changes && dotnet test` — 391/391 passed).

### T020 MultiFilter and IdChecker
- State: Done
- Depends: none
- Source: core/src/com/unciv/logic/MultiFilter.kt, logic/IdChecker.kt
- Fixtures: tests/src/com/unciv/logic/MultiFilterTests.kt, tests/src/com/unciv/logic/IdHelperTests.kt
- Deliverables: csharp/Unciv.Core/Logic/MultiFilter.cs, IdChecker.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~MultiFilterTests|FullyQualifiedName~IdCheckerTests`
- Claimed: 2026-10-24T01:51:23Z
- Notes: Pure string logic; keep Kotlin Regex/string semantics exact (culture-invariant).
  - Implementer notes (Antigravity):
    - Ported `MultiFilter` (`Filter` corresponding to `multiFilter`, `GetAllSingleFilters`, `IsAnd`, `GetAndFilters`, `IsNot`, `GetNotFilter` with exact `removeSurrounding` semantics) under `Unciv.Logic`.
    - Ported `IdChecker` (`IsGameDeepLink`, `IsFriendDeepLink`, `CheckAndReturnPlayerUuid`, `CheckAndReturnUuiId`, `GetCheckDigit`, `Friend` record, and Unicode-category-exact `TrimSpaceOrWrapper` + URL path/query decoding) under `Unciv.Logic`, using `StringExtensions.IsUUID()` from T018.
    - Ported `IdCheckerTests` (all 15 cases from `IdHelperTests.kt` + deep link and invalid character tests) and `MultiFilterTests` (pure filter tests from `MultiFilterTests.kt` + `forUniqueValidityTests` and nested `GetAllSingleFilters`; tests requiring `UniqueParameterType`/`Unique`/`Conditionals`/`TestGame`/`UniqueValidator` are noted for T025/T028/T035/T036).
    - Ran Gate (24/24 passed) and Standard checks (`dotnet build -warnaserror && dotnet format --verify-no-changes && dotnet test` — 376/376 passed).
  - Review notes (Claude): **Changes requested.** Gate 24/24 and Standard checks (build -warnaserror, format, full test 430/430) pass; no suppressions. `MultiFilter` is a faithful port (`removeSurrounding` bounds, ordinal `splitToSequence`, short-circuit order, lazy `GetAllSingleFilters`), the 15 `IdHelperTests` are ported, the check-digit arithmetic, the Unicode-category trim and the UUID path (via T018's `IsUUID`) all match. The one problem is URL parsing: the Kotlin uses Ktor's `Url`, the C# `System.Uri`. I ran the real compiled `IdChecker` (installed Unciv.jar + Ktor 3.5.2 + kotlin-stdlib on the classpath, via `java` in `dotnet-dev`; harness not committed) over 37 URL/ID cases and diffed against the C#: 30 agree, **7 differ**:
    1. `.../Unciv//G/G-<uuid>-2` and `.../Unciv/G//G-<uuid>-2` (empty path segment): Kotlin rejects (4 segments), C# accepts (`RemoveEmptyEntries` hides the empty segment). A trailing slash `.../G-<uuid>-2/` is **accepted** by Kotlin (Ktor drops the trailing empty segment), so only interior empty segments count.
    2. `.../Unciv/./G/G-<uuid>-2` and `.../Unciv/x/../G/G-<uuid>-2`: Kotlin rejects (dot segments are kept as segments, 4 of them), C# `Uri` normalizes them away and accepts.
    3. `?name=Foo+Bar`: Kotlin returns `Foo Bar` (query decoding turns `+` into space), C# returns `Foo+Bar` (`Uri.UnescapeDataString` leaves `+`). `%20`, `%2B`, UTF-8 percent sequences, repeated `name`, `name`/`name=` (empty), `name=a=b`, other params first, fragments, upper-case `UNCIV/p`, percent-encoded segment letters (`%47`, `%32`) all agree already.
    4. Malformed percent escapes (`?name=%ZZ`, `.../G-<uuid>-%ZZ`): Kotlin lets a `URLDecodeException` escape, C# returns the raw text for the query and `null` for the path (inconsistent with each other). Decide and document: I recommend treating malformed escapes as invalid input everywhere (return `null`, a deliberate hardening of an upstream crash) rather than reproducing the exception.
  - **Must fix:** drop `System.Uri` for this narrow case and parse the URL by hand, since the prefix is fixed: after the (already ordinal-ignore-case checked) prefix, cut the fragment at the first `#` and the query at the first `?`, split the path on `/` keeping empty and dot segments (drop only one trailing empty segment), percent-decode each segment (UTF-8), expect exactly `Unciv`, the `G`/`P` letter and the id (the prefix consumes the first, so recheck how the three segments map: the Kotlin counts the whole path `Unciv/G/G-<uuid>-2` as three segments), parse the query as `application/x-www-form-urlencoded` (`+` is a space, first `name` wins, `name` without `=` is empty). Add the 7 cases above as tests (expected values: Kotlin `null` for 1 and 2, `Foo Bar` for 3). Keep the segment-count and prefix checks in the order the Kotlin does them.
  - Minor: nested `IdChecker.Friend` stands in for `multiplayer.FriendList.Friend` (multiplayer is a non-goal); fine, note it. `Log.error` calls are dropped (no logging in the engine), also fine.
  - Rework notes (Claude): replaced `System.Uri` in `IdChecker` with a hand-written splitter that follows Ktor `Url` (empty and dot segments kept, one trailing empty segment dropped, fragment ignored, `+` as space in query values, whole query decoded eagerly). Malformed percent escapes anywhere return null (documented hardening of the upstream `URLDecodeException`). Verified against the real Kotlin `IdChecker` on the JVM with 78 URL/ID probes (the 37 original plus 41 edge cases: repeated and trailing slashes, `?`/`#` combinations, raw/invalid UTF-8, surrogates, truncated escapes): 78/78 agree, counting upstream exceptions as null. Note that the leading/trailing `spaceOrWrapper` trim already strips trailing `/ . % ? # &` characters, which explains several upstream "accept" cases; the port keeps that trim unchanged. Added Theory tests for each class of case. Standard checks: build clean, format clean, 476/476 tests.
  - Review 2 (Antigravity): Pass. Verified Ktor-matching URL path/query/fragment splitter and UTF-8 percent decoder in `IdChecker` (interior empty and dot segments preserved, single trailing empty segment dropped, `+` as space in query values, eager query decoding, malformed escapes returning `null`), plus all 40 new Theory test cases in `IdCheckerTests`. Re-ran Gate (64/64 passed) and Standard checks (`dotnet build -warnaserror && dotnet format --verify-no-changes && dotnet test` — 476/476 passed).

### T021 Metadata models: LocaleCode, GameParameters, GameSetupInfo, Player, BaseRuleset, ModCategories
- State: Done
- Depends: T007
- Source: core/src/com/unciv/models/metadata/LocaleCode.kt, GameParameters.kt, GameSetupInfo.kt, Player.kt, BaseRuleset.kt, ModCategories.kt
- Fixtures: none - generate golden JSON by serializing from Kotlin
- Deliverables: csharp/Unciv.Core/Models/Metadata/*.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~MetadataTests`
- Claimed: 2026-10-24T01:46:25Z
- Notes: Serialized field names must match Kotlin JSON. GameSettings/Migrations/OverviewPersistableData are client settings: out of scope unless a later ticket needs them.
  - Implementer notes (Claude): `Models/Metadata/` `LocaleCode` (+`LocaleCodeExtensions`), `BaseRuleset` (+`FullName`), `Player`, `GameParameters`, `ModCategory`; `Logic/Civilization/PlayerType`; also **`Unciv/Constants.cs`** (all of Constants.kt: pure constants, no ticket owned it, needed by `Player` and by everything after) and **`Utils/LinkedHashSet<T>`** (Kotlin `LinkedHashSet`/`setOf` order; used for `GameParameters.mods` and `Constants.All`). 15 `MetadataTests`; full suite 430/430; build -warnaserror and format clean.
  - Goldens from the real compiled classes (installed Unciv.jar, libGDX `json()`, OpenJDK 25): all 54 `LocaleCode` entries (tag, `fastlaneFolder()`, `languageName()`, unused), `getSupportedLanguages` count 47, `GameParameters` JSON as the game writes it, `toString` incl. the six-mod limit (`mods=(M1,...,M6,...)`), `clone` (godMode reset), old-style JSON reading. **JDK trap:** Indonesian's tag is `in-ID`; OpenJDK 17+ reports language `id`, older JDKs/Android `in` (both folders exist in `fastlane/`), so `FastlaneFolder` maps legacy `in`/`iw`/`ji` to `id`/`he`/`yi` and says so.
  - **libGDX json facts that shaped the port:** it writes only fields differing from a fresh instance (default `GameParameters` is `{"players":[{"playerType":"Human"},{},{},{}]}`) and reads fields directly, bypassing Kotlin setters. So the C# writes every field (reads back identically; deliberate deviation, names and relative order equal Kotlin's) and `showCivilizationStats` is split into `ShowCivilizationStatsRaw` (the json-facing backing field) and `ShowCivilizationStats` (Kotlin getter fallback to `ShowVictoryStats`, setter updating both; `clone()` relies on it). A test pins that the order of `showVictoryStats`/`showCivilizationStats` in a file does not matter.
  - Not ported / deferred: `LocaleCode.locale()`, `find(Locale)`, `getSystemLanguage`, `getLocale` (java.util.Locale/UncivGame, client); `Player.chosenNation`/`setNationTransient`/Nation constructor (`TODO(T032)`); `Speed.DEFAULT` is a private constant `"Quick"` until Speed exists (`TODO(T030)`); the `ModCategories` list, `mergeOnline`, `save`, `fromSelectBox` (client/network: Won't port), only `Category` as `ModCategory`; **`GameSetupInfo` is moved to T045** (needs `MapParameters` (T022); its `fromSettings` reads client settings). The deprecated `showVictoryStats` is kept as a plain property (no `[Obsolete]`, which would fail the build at its own use).
  - Upstream `TODO` comments in Constants.kt were reworded to "Upstream note" (bare TODO fails `SourcePolicyTests`).
  - Ran Gate (`~MetadataTests`, 15 passed) and Standard checks: build -warnaserror clean, format clean, full `dotnet test` 430/430.
  - Review (Antigravity): Pass. Verified `LocaleCode`, `BaseRuleset`, `Player`, `PlayerType`, `GameParameters`, `ModCategory`, `Constants`, `LinkedHashSet`, and `MetadataTests` against Kotlin sources: all 54 `LocaleCode` entries and OpenJDK 17+ `in` -> `id` fastlane folder mapping, `GameParameters` JSON field order and `ShowCivilizationStatsRaw` vs `ShowCivilizationStats` setter/fallback semantics, `Clone()` godMode reset, `ToString()` 6-mod limit, `ModCategory` topic-only equality/JVM hash, and `LinkedHashSet` insertion order + set equality. Re-ran Gate (`~MetadataTests`, 15/15 passed) and Standard checks (`dotnet build -warnaserror && dotnet format --verify-no-changes && dotnet test` — 436/436 passed).

### T045 GameSetupInfo
- State: Done
- Depends: T021, T022
- Source: core/src/com/unciv/models/metadata/GameSetupInfo.kt
- Fixtures: none - generate golden values by running the Kotlin class
- Deliverables: csharp/Unciv.Core/Models/Metadata/GameSetupInfo.cs, tests (add to MetadataTests or a GameSetupInfoTests class)
- Gate: `dotnet test --filter FullyQualifiedName~GameSetupInfo`
- Claimed: 2026-10-24T02:26:30Z
- Notes: Container of `GameParameters` and `MapParameters` with the two cloning constructors (from another setup; from a `GameInfo` when that exists: leave that constructor as `TODO(T0xx)` pointing at the GameInfo ticket). `mapFile` (FileHandle, transient) and `fromSettings` (reads `UncivGame.Current.settings.lastGameSetup`, applies QuickStart defaults: `shape = rectangular`, `worldWrap = true`, `espionageEnabled = true`) are client concerns; port the default-setup branch as a plain factory taking an optional difficulty if it is useful to headless tests.
  - Implementer notes (Antigravity): Ported `GameSetupInfo` in `Unciv.Models.Metadata`:
    - Properties `GameParameters`, `MapParameters`, transient `MapFile` (marked `[JsonIgnore]`).
    - Copy constructor and `Clone()` performing deep clones of both parameter sets.
    - `CreateDefault(defaultDifficulty)` implementing the QuickStart defaults from upstream `fromSettings` (`shape = Rectangular`, `worldWrap = true`, `espionageEnabled = true`, optional difficulty override).
    - Note added for constructor taking `GameInfo` once `GameInfo` is ported.
    - 8 unit tests in `GameSetupInfoTests` covering constructors, deep cloning, `CreateDefault`, JSON serialization, and Kotlin-style JSON deserialization.
    - Ran Gate (8/8 passed) and Standard checks (build -warnaserror clean, format clean, full `dotnet test` 548/548 passed).
  - Review notes (Claude): **Changes requested** (two small items). Gate 8/8, Standard checks (build -warnaserror, format, full test 584/584) pass, no suppressions. Verified against the real Kotlin `GameSetupInfo` on the JVM: libGDX JSON of a modified setup (`{"gameParameters":{"difficulty":"Settler","players":[{"playerType":"Human"},{},{},{}],"espionageEnabled":true},"mapParameters":{"shape":"Rectangular","mapSize":{...},"worldWrap":true,"mods":["ModB","ModA"],"seed":77}}`) deserializes in C# to the same difficulty, 4 players with default AI types, espionage, seed, ordered mods and map size; the clone-through-constructor JSON equals the original plus the `showCivilizationStats` field that `GameParameters.clone()` writes through its setter (already covered by T021). Defaults and the QuickStart branch of `fromSettings` (`shape = Rectangular`, `worldWrap = true`, `espionageEnabled = true`, difficulty only when given) match.
    1. **The cloning constructor copies `MapFile`; upstream does not.** Kotlin `constructor(setup) : this(setup.gameParameters.clone(), setup.mapParameters.clone())` leaves `mapFile` at its default `null` (it is a separate `@Transient var`). Probe: `s.MapFile = "x"; new GameSetupInfo(s).MapFile` is `null` upstream, `"x"` here. Remove the `MapFile = setup.MapFile;` line in the copy constructor (and so `Clone()`), and change the assertion at `GameSetupInfoTests` line ~51 (`Assert.Equal("path/to/map.json", cloned.MapFile)`) to `Assert.Null`.
    2. The note for the `GameInfo` constructor is prose ("Planned: GameInfo"); the protocol wants a marker. I created **T046** (GameInfo core) as the target: write `// TODO(T046): constructor(GameInfo) = this(gameInfo.gameParameters.clone(), gameInfo.tileMap.mapParameters.clone())`.
    - Optional, no change required: the saved-settings branch of `fromSettings` (`GameSetupInfo(lastGameSetup).apply { mapParameters.reseed() }`) is a client/settings concern; if a headless caller needs it, it is `new GameSetupInfo(last)` followed by `MapParameters.Reseed(...)` with an explicit seed source, never the clock.
  - Rework notes (Claude): the copy constructor (and `Clone()`) no longer copies `MapFile` (verified upstream behaviour on the JVM: null), test now asserts null; the GameInfo constructor note became `TODO(T046)`. Gate 8/8, Standard checks pass (build clean, format clean, full test green).
  - Review 2 (Antigravity): Pass. Verified rework in `GameSetupInfo.cs` and `GameSetupInfoTests.cs`: cloning constructor and `Clone()` leave transient `MapFile` null (verified by `CopyConstructor_PerformsDeepClone`), and `TODO(T046)` marker is in place for the `GameInfo` constructor. Re-ran Gate (`~GameSetupInfo`, 8/8 passed) and Standard checks (`dotnet build -warnaserror && dotnet format --verify-no-changes && dotnet test` — 653/653 passed).

### T047 Log facade
- State: Done
- Depends: none
- Source: core/src/com/unciv/utils/Log.kt (API shape only; the `Gdx.app.log` backend is Won't port, T043)
- Fixtures: none
- Deliverables: csharp/Unciv.Core/Utils/Log.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~LogTests`
- Claimed: 2026-10-24T02:35:00Z
- Notes: The engine has 191 `Log.error/debug/...` call sites in 42 files (grep `import com.unciv.utils.Log` and `import com.unciv.utils.debug` under logic/ and models/). Provide a static `Log` with the same method names and printf-style signature, forwarding to an injectable sink (`Log.Sink`, default no-op), no clock, no thread, no console by default; the sink gets level, tag (caller class) and the formatted message so a Godot client or a test can capture it. Also decide the `debug(...)` flavour (Kotlin top-level `debug` takes a format string and args). Then replace the places where earlier tickets dropped a log call and left a note (IdChecker, Translations swallow-on-read-error), and search the code for `TODO(T047)`.
  - Implementer notes (Claude): `Unciv.Utils.Log` (`Debug`/`Error` with message+params, with tag, with exception; `ShouldLog`, `GetSystemInfo`, `SoftRequire`), `Tag`, `ILogBackend` + `NullLogBackend` (default: drops everything, `IsRelease`) + `ConsoleLogBackend` (tests/temporary mains, no timestamp), `DisableLogsFrom`/`EnableLogsFrom` (the `-DnoLog`/`-DonlyLog` lists, same default exclusions), and `JavaFormat` (the `String.format` subset the 191 call sites use: `%s` 170x, `%d`, `%f`, plus `%x %c %b %n %% $`-indexes, flags `- 0 + , space`, width, precision; `%s` of booleans/doubles/floats/lists/maps prints like Java). Expected strings for 55 format cases are from the real `String.format` on the JVM. Deviations (class remark): no timestamp or thread object (`Thread` is banned; thread shown as `thread-N`), no system properties, a message whose specifiers do not match its arguments is logged raw instead of throwing, `SoftRequire` returns the condition (`softRequire(c, m, p) ?: return null` becomes `if (!Log.SoftRequire(c, m, p)) return null;`), the tag is the calling class found by a stack walk done only when something will be logged (nested types as `Outer$Inner`, closures collapse to their class). Applied to the places earlier tickets dropped a log call: `IdChecker` (3 `softRequire` messages and 4 `Log.error`s restored with the upstream texts), `Translations.TryReadTranslationForLanguage` (read errors). Tests that touch process-wide statics share `[Collection("TranslationState")]` (`LogTests`, `IdCheckerTests`, `TranslationTests`, `StatsTests`, `JavaDecimalFormatTests`). Gate 8/8, Standard checks: build -warnaserror clean, format clean, 592/592 tests (3 runs).
  - Review (Antigravity): Pass. Verified fidelity of Log facade and JavaFormat against Kotlin Log.kt and JVM String.format: correct lazy lambda evaluation, fallback raw message on mismatched format args, caller class tag extraction with Outer$Inner nested class handling, thread-N stringification, NullLogBackend and ConsoleLogBackend defaults, and restored call sites in IdChecker and Translations. Determinism verified (no wall-clock, invariant culture). Re-ran Gate (~LogTests, 8/8 passed) and Standard checks (dotnet build -warnaserror && dotnet format --verify-no-changes && dotnet test — 666/666 passed).

### T048 Engine options replacing the DebugUtils flags the rules read
- State: Done
- Depends: none
- Source: core/src/com/unciv/utils/DebugUtils.kt, and its engine users: logic/GameInfo.kt (`SIMULATE_UNTIL_TURN`), logic/city/CityStats.kt (`SUPERCHARGED`), logic/civilization/transients/CivInfoTransientCache.kt (`VISIBLE_MAP`), logic/automation/unit/CityLocationTileRanker.kt (`SETTLER_SCORES`, `SHOW_SETTLER_SCORES`), anything using `CIV_IDS_IN_EXPERIMENT_GROUP`
- Fixtures: none
- Deliverables: csharp/Unciv.Core/Utils/EngineOptions.cs (name open), a design note in DETERMINISM.md, tests
- Gate: `dotnet test --filter FullyQualifiedName~EngineOptionsTests`
- Claimed: 2026-10-24T02:39:00Z
- Notes: These flags change game results (extra yields, whole-map vision, turn simulation), so a hidden mutable static breaks the "log + initial state reproduces the game" rule. Decide: an options object that is part of the game state/setup (hashed by StateHasher) and changed only through a command, or a per-process object that must be identical on all peers and is stamped into the game's start record. The UI-only flags are not ported.
  - Implementer notes (Claude): `Unciv.Logic.EngineOptions` (`VisibleMap`, `Supercharged`, `SimulateUntilTurn` + `IsSimulation(turns)`, `CivIdsInExperimentGroup` as a `LinkedHashSet`, diagnostics `ShowSettlerScores`/`SettlerScores`), `Clone()`, `IStateHashable` over the four rule-affecting values (experiment group as an unordered set; diagnostics excluded). Decision: an instance owned by the game (the GameInfo port holds it, `TODO(T046)`), in the state hash, changed through a command only; recorded as DETERMINISM.md 2.6 with the porting rule "wherever Kotlin reads `DebugUtils.X`, take the `EngineOptions` of the game". The command that sets it is not written here because it needs the GameInfo type (COMMANDS.md shape applies); `TODO(T046)` covers it. Users listed in the class remark (GameInfo, CityStats, CivInfoTransientCache, Tile.isVisible, Simulation, CityLocationTileRanker). 5 tests; Standard checks pass (build -warnaserror, format, full tests).
  - Review (Antigravity): Pass. Verified EngineOptions design and implementation against Kotlin DebugUtils.kt and DETERMINISM.md: instance-owned mutable state replacing global statics, proper exclusion of UI flags (SHOW_TILE_COORDS, SHOW_TILE_IMAGE_LOCATIONS, SHOW_FPS), IStateHashable inclusion of rule-affecting fields with order-independent CivIdsInExperimentGroup set hashing, diagnostic separation for SettlerScores, independent deep cloning, and documentation in DETERMINISM.md 2.6. Re-ran Gate (~EngineOptionsTests, 5/5 passed) and Standard checks (dotnet build -warnaserror && dotnet format --verify-no-changes && dotnet test — 675/675 passed).

### T049 Model leaves: ModConstants, UncivSound, TutorialTrigger
- State: Done
- Depends: none
- Source: core/src/com/unciv/models/{ModConstants,UncivSound,TutorialTrigger}.kt
- Fixtures: tests/src/com/unciv/logic/ModConstantsTests.kt (4 tests); otherwise generate golden values for the reflective equals/hashCode/toString/merge
- Deliverables: csharp/Unciv.Core/Models/ModConstants.cs, UncivSound.cs, TutorialTrigger.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~ModConstantsTests`
- Claimed: 2026-10-24T02:42:00Z
- Notes: Found missing while expanding the backlog; they are needed by T030 (`ModOptions.constants`, `Tutorial`/`TutorialTrigger`) and `Stat.purchaseSound`. ModConstants uses Java reflection for `merge/equals/hashCode/toString` over `declaredFields` in declaration order, with `hashCode` chaining `withHash(field.hashCode())` and `equals` being Java boxed `equals` (Double/Float compare bits: `NaN == NaN`, `0.0 != -0.0`); use C# reflection over the properties in declaration order and `JavaHash`/`Randomness.WithHash`, and the Java `toString` of floats/doubles (`JavaFormat.ToJavaString`, T047). Nested data class `UnitUpgradeCost` needs Kotlin data-class `equals/hashCode/toString`. `ImmutableColor` is NOT ported here (no engine Color type yet): decide with Nation (T032).
- Implementer notes: Properties are PascalCase with `[JsonPropertyName]` equal to the Kotlin names (generated from the Kotlin source, 44 constants), reflected in `MetadataToken` (declaration) order. The nested data class is `ModConstants.UpgradeCostConstants` (a nested type cannot share the name of the `UnitUpgradeCost` property); its `ToString` still prints `UnitUpgradeCost(...)`. Hash codes of `Defaults` (670061036) and of the `{99, 6.0}` instance (1000388743) and the `toString` forms are golden values from the real JVM class. `TutorialTrigger` keeps the Kotlin entry names; `Value()`/`IsCivilopedia()` are extension methods. `UncivSound` is a record with the static instances. `TODO(T030)` marks the upstream "implement as unique conditionals" note.
  - Review (Antigravity): Pass. Verified `ModConstants.cs`, `UncivSound.cs`, `TutorialTrigger.cs`, and `ModConstantsTests.cs` against Kotlin sources: all 44 `ModConstants` properties and `[JsonPropertyName]` mappings in declaration (`MetadataToken`) order, Java boxed `Equals`/`GetHashCode` (via `JavaHash` and `WithHash` matching JVM golden hashes `670061036` and `1000388743`), `ToString` and `Merge` reflection semantics, `UpgradeCostConstants` data-class parity, all 19 `UncivSound` constants, and all 41 `TutorialTrigger` entries + `Value()`/`IsCivilopedia()`. Re-ran Gate (`~ModConstantsTests`, 8/8 passed) and Standard checks (`dotnet build -warnaserror && dotnet format --verify-no-changes && dotnet test` — 675/675 passed).

### T046 GameInfo core and the hooks waiting for it
- State: Available
- Depends: T034, T039, T045
- Source: core/src/com/unciv/logic/GameInfo.kt (and what it needs to stand alone: GameInfoPreview is not part of it)
- Fixtures: none yet - design pass first; break into smaller tickets before implementing
- Deliverables: to be defined when T039 is Done
- Gate: `n/a (planning ticket)`
- Claimed:
- Notes: Placeholder created by T023 so that `TODO(T046)` markers have a target. Waiting on it: `TranslationCache.ActiveMods` should use the running game's `gameParameters.mods + baseRuleset` instead of `Translations.TranslationActiveMods` when a game exists (see `ActiveModsCache` in TranslationCache.kt), and the `GameSetupInfo(gameInfo)` constructor of T045. An agent reaching this ticket should split it (GameInfo core / turn handling / serialization) and expand the backlog accordingly.

### T022 Map size and parameters: MapSize, MapParameters, NeighborDirection, RoadStatus
- State: Done
- Depends: T006, T007, T010
- Source: core/src/com/unciv/logic/map/MapSize.kt, MapParameters.kt, NeighborDirection.kt, logic/map/tile/RoadStatus.kt
- Fixtures: none - generate golden values (incl. MapSize radius/size math, MapParameters JSON)
- Deliverables: csharp/Unciv.Core/Logic/Map/MapSize.cs, MapParameters.cs, NeighborDirection.cs, Tile/RoadStatus.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~MapParametersTests`
- Claimed: 2026-10-24T01:48:35Z
- Notes: Unblocks half of T015. Watch Kotlin Int/Float rounding in MapSize custom-size logic (roundToInt is half-up).
  - Implementer notes (Antigravity):
    - Ported `MapSize` (`Predefined` enum + extension properties, custom radius and width/height constructors via `HexMath`, `GetPredefinedOrNextSmaller`, `FixUndesiredSizes`, `Clone`, JSON serialization) under `Unciv.Logic.Map`.
    - Ported `MapParameters` (`MapShape`, `MapGeneratedMainType`, `MapType`, `MirroringType`, `MapResourceSetting` + extensions, all Kotlin JSON property names including lowercase `temperatureintensity`, `GetArea`, `NumberOfTiles`, `ToString` with `NiceToString` formatting, `Clone`, and deterministic `Seed = 0L` default + `Reseed(long)` / `Reseed(GameRandom)` / `ResetAdvancedSettings`) under `Unciv.Logic.Map`.
    - Ported `NeighborDirection` (`0..5` ordinals + `ByClockPosition` / `GetClockPosition`) and `RoadStatus` (`None = 0`, `Road = 1`, `Railroad = 2` + `GetUpkeep`/`GetMovement`/`GetMovementImproved`/`GetRemoveAction`; `Improvement(Ruleset)` noted as `TODO(T034)`).
    - Verified golden values generated on the JVM from compiled Kotlin `MapSize`, `MapParameters`, and `UncivJsonKt.json().toJson(...)` in `MapParametersTests`.
    - Ran Gate (6/6 passed) and Standard checks (`dotnet build -warnaserror && dotnet format --verify-no-changes && dotnet test` — 421/421 passed).
  - Review notes (Claude): **Changes requested** (small). Gate 6/6, Standard checks (build -warnaserror, format, full test 436/436) pass; no suppressions. Read line by line against the Kotlin: `MapSize` (ctors, `Predefined` values and cost factors, `FixUndesiredSizes` order of checks and fix-up, `GetPredefinedOrNextSmaller`, `ToString`), `NeighborDirection`, `RoadStatus` values, `MapResourceSetting` tables, `GetArea`/`NumberOfTiles`/`DisplayMapDimensions`, `Clone` (all fields copied, as upstream) and the JSON names (incl. lowercase `temperatureintensity`) are faithful. The `Seed = 0` default with `Reseed(...)` is an accepted determinism deviation. Findings:
    1. **`MapParameters.Mods` is a `HashSet<string>`; upstream is a `LinkedHashSet`** (insertion order is serialized and used for ruleset loading order). Use the `LinkedHashSet` from `Unciv.Utils` (as `GameParameters` does) for the property, the initializer and `Clone`. The existing test only passes because `HashSet` happens to preserve insertion order when nothing is removed; add a case that removes then adds.
    2. **`NiceToString` rounds exact ties differently from Java.** Kotlin `"%.2f".format(float)` is HALF_UP over the shortest decimal digits of the float widened to double; .NET `F2` rounds an exact binary tie to even-magnitude. I diffed against the real JVM over 49,398 floats (|x|<1e6, multiples of 1/1000, 1/200, 1/8000 and random): 129 differ, all exact ties, e.g. `0.125f` Java `0.13` vs C# `0.12`, `-14.625f` Java `-14.63` vs C# `-14.62`, `0.3125f` at 3 digits `0.313` vs `0.312`. Fix (verified to remove every tie difference): `decimal.Parse(((double)v).ToString("R", Invariant))` then `Math.Round(d, p, MidpointRounding.AwayFromZero)` then `F{p}` and the trims. Caveat if you use `decimal`: it drops the sign of negative zero, Java gives `-0` for e.g. `-0.001f` at 2 digits, so keep the sign for values that round to zero (`float.IsNegative`). NaN/Infinity and |x|>=1e6 are out of range for these settings, ignore. Add tie cases (`0.125f`, `-2.625f`, `0.3125f` at 3, and `-0.001f`) to the `ToString` test.
    3. **Namespace `Unciv.Logic.Map.Tile` will collide with the `Tile` class (T038) in `Unciv.Logic.Map`**; any code in `Unciv.Logic.Map` then resolves `Tile` to the namespace (CS0118). Use a different namespace (e.g. `Unciv.Logic.Map.Tiles`) for `RoadStatus` and tell T038 in its Notes; keep the folder as is.
    4. Minor: `BaseRuleset` default is the string literal `"Civ V - Gods & Kings"` although T021 provides `Metadata.BaseRuleset.Civ_V_GnK.FullName()`; use that and remove the "(in T021)" remark. `MapResourceSetting` lives in `MapParameters.cs` while upstream is in `mapgenerator`; fine for now, but give it its own file under `MapGenerator/` when T041 touches it.
  - Rework notes (Antigravity):
    1. Changed `MapParameters.Mods` to `LinkedHashSet<string>` (`Unciv.Utils`) for the property, initializer, and `Clone()`; added a remove-then-add test asserting insertion order preservation.
    2. Updated `NiceToString` to round over the shortest decimal representation of `(double)value` via `MidpointRounding.AwayFromZero` and preserve `-0` when `float.IsNegative(value)` rounds to zero; added tie cases (`0.125f`, `-2.625f`, `0.3125f` at 3 digits, `-14.625f`, `0.0005f`, `-0.001f`) to `MapParametersTests`.
    3. Changed `RoadStatus.cs` namespace to `Unciv.Logic.Map.Tiles` and noted the namespace convention on T038.
    4. Changed `MapParameters.BaseRuleset` default to `Models.Metadata.BaseRuleset.Civ_V_GnK.FullName()`.
    5. Ran Gate (`~MapParametersTests`, 6/6 passed) and Standard checks (`dotnet build -warnaserror && dotnet format --verify-no-changes && dotnet test` — 436/436 passed).
  - Re-review (Claude): **Pass.** All four findings fixed: `Mods` is now a `LinkedHashSet` (test removes/re-adds and checks order, also through `Clone`), `NiceToString` uses HALF_UP over the shortest double digits with negative zero preserved (tie cases `0.125f`, `-2.625f`, `0.3125f`, `-14.625f`, `-0.001f` asserted), the namespace is `Unciv.Logic.Map.Tiles`, the base ruleset default comes from `BaseRuleset.Civ_V_GnK`. Gate 6/6, Standard checks 481/481. Non-blocking follow-up for whoever touches `MapParameters` next: `NiceToString` throws `OverflowException` for |x| >= ~7.9e28 and `FormatException` for NaN/Infinity (decimal), where Java prints the full digits / `NaN` / `Infinity`. It is only `ToString` (debug output) on absurd JSON input, so it does not block. Remember for T038: `Tile` is now free as a class name in `Unciv.Logic.Map`.

### T023 Translation core (headless)
- State: Done
- Claimed: 2026-10-24T02:53:00Z
- Depends: T018
- Source: core/src/com/unciv/models/translations/Translations.kt, TranslationEntry.kt, TranslationCache.kt, TranslationFileReader.kt, TranslationExtensions.kt (`tr()` and its helpers)
- Fixtures: tests/src/com/unciv/logic/TranslationTests.kt, tests/src/com/unciv/logic/TutorialTranslationTests.kt (only cases not requiring libGDX/assets); otherwise generate golden values
- Deliverables: csharp/Unciv.Core/Models/Translations/*.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~TranslationTests`
- Notes: Decided: ported headlessly now. File access behind an interface (no libGDX FileHandle). Exclude TranslationFileWriter (dev tooling; Won't port, T043). Translation templates/placeholders/conditionals `[]`/`<>` must match Kotlin exactly; culture-invariant ordering. Lookups must not depend on iteration order of hash collections.

  - Implementer notes (Antigravity): Headless translation core in `Unciv.Models.Translations`:
    - `Translations.cs`, `TranslationEntry.cs`, `ITranslationFileHandle.cs`, `TranslationFileReader.cs`, `TranslationCache.cs`, `TranslationExtensions.cs`, and `FontRulesetIcons.cs`.
    - `StatExtensions.Character` provides Unicode icon characters for all 7 Stats (`⚙`, `⁂`, `¤`, `⍾`, `♪`, `⌣`, `☮`).
    - Decoupled file I/O behind `ITranslationFileHandle` (`StringTranslationFileHandle`, etc.) and `TextReader` overloads on `TranslationFileReader`.
    - Exact bracket scanning and nesting: ported `FindNextTopLevel`, `FindMatchingAnyKind`, `FindMatchingSameKind`, and `ParsePairs` matching Kotlin's 3-bracket-type stack logic (`[]`, `{}`, `<>`) and unmatched bracket preservation.
    - Number formatting: `LanguageCache` implements native digit substitution (e.g. Bangla `০` -> `১,২৩৪`) and invariant culture compatibility for `InvariantGlobalization: true`.
    - Conditionals and modifiers: stable sorting matching `ConditionalsOrder`, before/after placement matching `ConditionalsPlacement`, and first character casing matching `StartWithCapitalLetter`.
    - Deliberately excluded: `TranslationFileWriter` (dev tooling; non-goal per T043) and libGDX fake private-use-area font glyph remapping (Godot client has native text shaping).
    - Ran Gate (59 passed) and Standard checks (build -warnaserror clean, format clean, full `dotnet test` 474/474 passed).
  - Review notes (Claude): **Changes requested.** Gate 59/59 and Standard checks (build -warnaserror, format, full test 540/540) pass, no suppressions. The bracket scanning (`ParsePairs`, `FindMatchingAnyKind`, `FindNextTopLevel`, `FindMatchingSameKind`), `AppendTrimming`, the `Tr`/`TrNoConditionals`/`TranslatePlaceholders`/`TranslatePartial` flow, `FillPlaceholders`, `ValidateConditionals` and `TranslationFileReader.Read` read as faithful. I diffed against the real Kotlin classes on the JVM (installed Unciv.jar + `LocaleCode`, JDK 25) where the logic is locale/number dependent or looked suspicious; these differ:
    1. **Number formatting is wrong (blocking).** `Tr(int/long/float/double)`, `LanguageCache.Format/Reformat` and `NumberRegex` use hard-coded invariant rules (`FormatInteger` prints no grouping at all, `FormatFloat` is `"0.###"` away-from-zero, comma/`-` hard-coded, 3 languages have digit tables, everything else is Latin/English). Upstream uses `NumberFormat.getInstance(LocaleCode.getLocale(language))`: grouping, per-locale separators and digits, `HALF_EVEN`, max 3 fraction digits. Harness over all 47 supported languages x 18 numbers (0, +-1, 999, 1000, 1234, 1234567, 0.5, 0.0625, 0.0005, 1234.5678, 2.5f, 0.1f, 1e10, Long.MAX): **591 of 846 outputs differ**. Examples: English `1234.Tr()` is `1,234` upstream, `1234` here; `0.0625` is `0.062` (HALF_EVEN tie) vs `0.063`; Afrikaans `1234.5678` is `1 234,568` (U+00A0 group, comma decimal) vs `1234.568`; the format drives `TranslateIndividualWord` number rewriting, so every translated string with digits is affected. `InvariantGlobalization=true` rules out `CultureInfo`, so generate the data from the JVM instead: per language the `DecimalFormat` facts I dumped are `zeroDigit`, `groupingSeparator`, `decimalSeparator`, `groupingSize`, `isGroupingUsed`, `negativePrefix`/`negativeSuffix`, positive prefix/suffix (all supported languages have empty positives; observed groupings: `,` `.` U+00A0 U+202F and a size-2 case; Arabic uses U+0660.. digits with U+066C/U+066B and an LRM+U+2212 negative prefix; some languages use U+2212 as minus), `maxFractionDigits=3`, `RoundingMode.HALF_EVEN`. Embed that table (a generated `.cs`), implement the small `DecimalFormat` subset (format of long, and of double via Java's shortest-digits-then-HALF_EVEN rule with the exact-tie correction, `parse` with `ParsePosition`, `reformat`) and build `NumberRegex` exactly as `getNumberRegex` does from those symbols (`Pattern.quote` -> `Regex.Escape`, `optional()`, grouping `{groupingSize}`). Golden test: the harness output (`Nm.java`, columns language/number/result) over all languages, not three hand-picked ones. Also drop `CultureInfo.InvariantCulture` as `Culture` (see 4).
    2. **`GetPlaceholderText` starts at the wrong bracket.** Kotlin passes no `firstPos` there (so it starts at `findNextTopLevel(.., '[')`, skipping `[` inside a complete `{}`/`<>` group); the C# passes `IndexOf('[')`. JVM vs C#: `"{a [b]} [c]"` upstream `{a [b]} []`, C# `{a []} []`; `"{a [b]} c"` upstream unchanged, C# `{a []} c`; `"x {[y]} [z] {[w]}"` upstream `x {[y]} [] {[w]}`, C# `x {[]} [] {[w]}`. This is the translation key function, so sub-sentence templates get the wrong key. Use the sentinel start for `GetPlaceholderText`. `GetPlaceholderParameters` really does use `indexOf('[')` upstream (JVM gives `[b]` for `"{a [b]} c"`); keep that. Add these cases as tests.
    3. **`sortPriorities` differs for repeated modifiers.** Upstream `forEachIndexed { put(text.getPlaceholderText(), index) }`: last occurrence wins and the index counts every entry. C# keeps the first and renumbers densely. Port exactly.
    4. **First-character casing ignores the locale.** Upstream `Char.uppercase(locale)`/`lowercase(locale)` are `String.toUpperCase(locale)` (multi-char results, Turkish/Azeri dotted i). JVM: `tr`/`az` `i` -> `İ`, `ß` -> `SS`, `ﬁ` -> `FI`, `İ` lower is `i̇` (two chars) outside `tr`/`az`; C# uses `char.ToUpperInvariant/ToLowerInvariant`, so none of those happen, and `Culture` is computed but never used. `InvariantGlobalization` again means no culture casing, so generate a table from the JVM (all BMP chars x the locales in `LocaleCode`, keeping only results that differ from the invariant single-char mapping) and apply it in `AppendChangingFirstChar`; the locale comes from `LocaleCode.getLocale` (already ported in `Models/Metadata/LocaleCode.cs`, T021).
    5. **Ordering and boolean parsing.** `Translations` is a `LinkedHashMap`, `translationActiveMods` a `LinkedHashSet`, and `Get(.., activeMods)` returns the first mod in iteration order that has the entry; the C# uses `Dictionary`/`HashSet` and takes `IReadOnlySet`. Use `OrderedDictionary`/`LinkedHashSet` (T006/T007 utilities), also for `TranslationFileReader.Read`'s result. `ShouldCapitalize`/`TriggerNotificationEffectBeforeCause` use `bool.TryParse` (accepts `" true "`, trailing NULs); Kotlin `toBoolean()` is `equals("true", ignoreCase = true)`.
    6. **Unresolved cross-ticket hooks.** `TODO(T023)` in `Logic/Versioning.cs` (`ToNiceString`) and `Logic/UncivExceptions.cs` (`GetLocalizedMessage`) must be done here; `Stats.Translate` (the T004 hook) is not wired to `Tr`, and `TranslationExtensions.TranslateStats` re-implements `Stats.Stringify` instead of calling `Stats.ToString/ToStringWithoutIcons` (upstream's `tr()` calls inside `stringify` use the *current* language, ignoring the `language` argument: keep that quirk or call it out). Remove the duplicate and wire the hook (set it where `Translations.Current` is initialised, or make `Stats` call `Tr` directly now that it exists).
    7. **Missing pieces need explicit markers or a Won't-port entry**: `tryReadTranslationForLanguage`/`readAllLanguagesTranslation`/`loadPercentageCompleteOfLanguages` (the `ITranslationFileHandle` plumbing exists but nothing loads a language: add a loader taking handles/readers, with the mod-folder handling as `TODO(T034)` since it needs `RulesetCache`), `DiacriticSupport` (say explicitly it is skipped), `TranslationCache.ActiveMods` ignoring `GameInfo` mods (`TODO` on the GameInfo ticket), `String.getModifiers()` (`TODO(T027)`), `UniqueType.Comment.text` literal (`TODO(T026)`).
    8. Minor: `TrimEnd` uses `char.IsWhiteSpace`, Kotlin `isWhitespace` differs at U+0085 and U+001C..U+001F (rare); `ReadLanguagePercentages` silently skips malformed lines where Kotlin throws; `Translations.Current`/`CachedLangData` are unsynchronised statics: put a `[Collection]` on every test class touching them once a second class does (the Stats wiring in item 6 will); `GetByYear`-style `Tr(this int/long/float/double)` only, fine.
  - Recipe for the number golden (so you need not rebuild it): a Java class using `com.unciv.models.metadata.LocaleCode.Companion.getLocale(lang)` and `NumberFormat.getInstance(locale)` (cast to `DecimalFormat`), one `L` row per language with the symbols above and one `N` row per (language, number) with `format(n)`; classpath = installed Unciv.jar + kotlin-stdlib + libGDX.
  - Rework notes (Claude), answering the review item by item:
    1. Number formatting: new `JavaDecimalFormat` (format of long and double with the JVM's shortest-digits + `HALF_EVEN` + exact-tie correction, `-0`, NaN, infinities; `parse(text, pos)` with prefix, grouping, decimal point and the Long/Double result rule incl. `-0` -> `-0.0`) over generated `JavaLocaleData` (symbols of all 54 `LocaleCode` languages, JDK 25 / CLDR). `LanguageCache` uses it for `Format`, `Reformat` and builds `NumberRegex` exactly as `getNumberRegex` (negative prefix, grouping size, locale digits). Unknown language: English data (upstream: `Locale.getDefault()`, machine dependent). Checked against the real `DecimalFormat`: 237,264 rows (6 languages x 38k fuzz doubles incl. all k/1000, k/8000 ties and widened floats, 54 languages x fixed numbers and parse/reformat probes): all agree except 54 rows for a 23-digit number that parses to 1.0E23, where the JVM's historic digit generator prints `9.999999999999999E22` (documented on the class, not in the golden). A thinned 6,219-row golden is committed (`NumberFormat.golden.tsv`).
    2. `GetPlaceholderText` scans from the first top-level `[` (6 JVM cases as tests); `GetPlaceholderParameters` unchanged.
    3. `SortPriorities` last occurrence wins, index counts every entry (test).
    4. First-character casing: `JavaCasing` over generated tables of the differences between the JVM's `String.toUpperCase/LowerCase(locale)` and `char.ToUpperInvariant/ToLowerInvariant` (105 root deviations incl. `ß` -> `SS`, ligatures; `tr` and `lt` own tables); locale language comes from the language's `LocaleCode`.
    5. `Translations` is an `OrderedDictionary`, `TranslationActiveMods` a `LinkedHashSet`, `Get/GetText` take `IEnumerable<string>` and honour its order (test with two mods); `TranslationFileReader.Read` returns an `OrderedDictionary`; booleans parse with `equals("true", ignoreCase)`; `IsKotlinWhitespace/KotlinTrimStart/IsKotlinBlank` replace .NET whitespace in `TrimEnd`, `ValidateConditionals`, `Read`.
    6. `Version.ToNiceString()` and `UncivShowableException.GetLocalizedMessage()` done (their `TODO(T023)` removed). `Stats.Translate` hook removed: `Stats.Stringify` is now the Kotlin one (`Tr()` for the amount and the name with `hideStats`, stat font characters, `toInt` via `KotlinMath.FloatToInt`), `TranslationExtensions.TranslateStats` deleted, so `Stats.ToString()` is `+1 \u2699Production, -1 \u2042Food` like upstream (StatsTests updated).
    7. Loader: `TryReadTranslationForLanguage(language, languageFile, modFiles)`, `ReadAllLanguagesTranslation(provider)`, `LoadPercentageCompleteOfLanguages(file)`; mod folder discovery is `TODO(T034)`. `DiacriticSupport` is deliberately not ported (client font glyph remapping; recorded in T043). `ActiveMods` ignoring GameInfo is `TODO(T046)`, `getModifiers` is `TODO(T027)`, the Comment unique text is `TODO(T026)`.
    8. Percentages are read strictly (malformed lines throw, as upstream). Tests touching `Translations.Current` (`TranslationTests`, `StatsTests`, `JavaDecimalFormatTests`) share `[Collection("TranslationState")]`.
    - Generators and recipes: `csharp/migration/tools/T023/`. Gate 80+ tests, Standard checks: build -warnaserror clean, format clean, 576/576 tests.
  - Review 2 (Antigravity): Pass. Verified all 8 rework items in `JavaDecimalFormat`, `JavaLocaleData`, `JavaCasing`, `Translations`, `TranslationCache`, `TranslationFileReader`, `TranslationExtensions`, `Versioning`, `UncivExceptions`, `Stats`, `TranslationTests`, and `JavaDecimalFormatTests`: JVM-exact `DecimalFormat` (`HALF_EVEN` over shortest decimal digits + exact-tie correction, `-0`, grouping, locale digits, `Parse`/`Reformat`, verified across 6,219 golden rows), top-level `[` start in `GetPlaceholderText`, last-occurrence `SortPriorities`, locale-sensitive `JavaCasing`, `OrderedDictionary`/`LinkedHashSet` ordering, strict `ToBoolean` and Kotlin whitespace helpers, wired `Version.ToNiceString()` / `UncivShowableException.GetLocalizedMessage()` / `Stats.Stringify`, and language/percentage loaders. Re-ran Gate (`~TranslationTests`, 83/83 passed) and Standard checks (`dotnet build -warnaserror && dotnet format --verify-no-changes && dotnet test` — 701/701 passed).

### T024 Holiday dates
- State: Done
- Claimed: 2026-10-24T01:54:11Z
- Depends: none
- Source: core/src/com/unciv/logic/HolidayDates.kt
- Fixtures: tests/src/com/unciv/logic/HolidayDatesTest.kt
- Deliverables: csharp/Unciv.Core/Logic/HolidayDates.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~HolidayDatesTests`
- Notes: Time is an explicit input (DateOnly/ints), never DateTime.Now. Use vetted rounding/calendar helpers.
  - Implementer notes (Claude): `Unciv.Logic.HolidayDates` (`Holidays` enum + `HolidaysExtensions.GetByYear/GetChance/SafeValueOf`, `DateRange`, `GetHolidayByYear`, `GetHolidayByDate`, `GetMonth`) on `DateOnly`; tables generated from the Kotlin source by script (77 equinoxes, 28 Passover, 17 Diwali, 77 Lunar New Year entries, counts asserted). `Random(date.hashCode())` and `Random(year)` use `GameRandom(int)` with a ported `LocalDate.hashCode`; `getHolidayByDate` draws `nextFloat` only for holidays whose range contains the date, in enum order, like upstream. `DateRange.hashCode` = `HashOf` of the two LocalDate hashes.
    - Deviations (class remarks): no `LocalDate.now()` defaults (callers pass today), the `easterEgg`/`month` system properties are explicit parameters, `DateRange.Never` is a fixed empty range, Qingming uses the UTC date of the equinox instant (upstream uses the JVM default time zone, which changes results by a day: I measured Qingming 2024 differing in a CDT container), years outside 1..9999 throw (`DateOnly`).
    - Verification: golden sweep from the real Kotlin classes (installed Unciv.jar, `-Duser.timezone=UTC`): all 15 holidays x 2019..2101 (range text, length, hashCode), and `getHolidayByDate` for every day 2023-01-01..2030-12-31 (2,922 days, 162 holiday days) in `Logic/Golden/HolidayDates.golden.tsv` (copied to the test output via the test csproj), plus the two upstream tests. Gate and Standard checks pass (481/481).
  - Review notes (Antigravity): **Pass.** Gate (5/5) and Standard checks (build -warnaserror, format, 540/540 tests) pass cleanly. Line-by-line verification against `HolidayDates.kt` confirms enum values/ordering, chance weights, and lazy PRNG evaluation matching upstream. Verified determinism with explicit `DateOnly` inputs and UTC-anchored equinox date table. Verified `LocalDate.hashCode` and `DateRange.GetHashCode` reproduction. 15 holidays x 83 years (2019..2101) and daily sweep (2023..2030) verified against JVM golden dataset. No warning suppressions.

### T025 Unique system: UniqueTarget, UniqueFlag, UniqueParameterType
- State: Done
- Depends: T018
- Source: core/src/com/unciv/models/ruleset/unique/UniqueTarget.kt, UniqueFlag.kt, UniqueParameterType.kt
- Fixtures: none - generate golden values (parameter matching/validation results)
- Deliverables: csharp/Unciv.Core/Models/Ruleset/Unique/UniqueTarget.cs, UniqueFlag.cs, UniqueParameterType.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~UniqueParameterTypeTests`
- Claimed: 2026-10-24T02:29:29Z (review)
- Notes: UniqueParameterType (797 LOC) references Ruleset for validation; introduce minimal interfaces (e.g. IRulesetView) and note them for T034 to implement. Enum ordinals/names must match Kotlin (serialized).
  - Implementer notes (Antigravity):
    - Ported `UniqueTarget` (all 34 enum values in declaration order, `UniqueTargetModifierType`, `DocumentationString`, `InheritsFrom`, `ModifierType`, `CanAcceptUniqueTarget`, `IsAcceptableModifierFor(IUniqueTargetView)`, `Displayable`, `CanIncludeSuppression`) under `Unciv.Models.Ruleset.Unique`.
    - Ported `UniqueFlag` (all 4 enum values in declaration order + `UniqueFlagSets.SetOfHiddenToUsers`, `SetOfNoConditionals`, `SetOfHiddenNoConditionals`) under `Unciv.Models.Ruleset.Unique`.
    - Ported `UniqueParameterType` (all 62 enum values in declaration order, `UniqueParameterErrorSeverity`, `UniqueComplianceError`, `ParameterName`, `DocExample`, `DocDescription`, `DisplayName`, `SeverityDefault`, `StaticKnownValues`, `GetErrorSeverity`, `IsKnownValue`, `GetKnownValuesForAutocomplete`, `SafeValueOf`, `ToIntOrNull`, `ToFloatOrNull`, and `IsValidSuppressionFilter`) under `Unciv.Models.Ruleset.Unique`.
    - Introduced minimal covariant interfaces for downstream tickets: `IUniqueTargetView` (for `Unique` in T027), `ICountablesParameterProvider` (`CountablesProvider` hook for `Countables` in T028), and `IRulesetParameterView` / `IRulesetMapView<out TValue>` / `IRulesetObjectParameterView` / `IRulesetUnitParameterView` / `IRulesetTerrainParameterView` / `IRulesetResourceParameterView` (for `RulesetObject` / `Ruleset` in T030-T034).
    - Deliberately excluded `TranslationFileWriter` guessing helpers (`isTranslationWriterGuess`, `getTranslationWriterStringsForOutput`, `guessTypeForTranslationWriter` — dev tooling, Won't port per T043).
    - Verified golden tables and probe inputs generated on the JVM from compiled Kotlin `UniqueTarget`, `UniqueFlag`, and `UniqueParameterType`, plus the 3 `MultiFilterTests.kt` tests (`TestParameterTypeSplits`, `TestParameterNonFilters`, `TestParameterTypeSplitsWorkWithMultipleLevels`) deferred from T020.
    - Ran Gate (`~UniqueParameterTypeTests`, 46/46 passed) and Standard checks (`dotnet build -warnaserror && dotnet format --verify-no-changes && dotnet test` — 586/586 passed).
  - Review notes (Claude): **Pass.** Gate (59/59 after my added cases) and Standard checks (build -warnaserror 0 warnings, format clean, 666/666) pass; no new `#pragma`/`SuppressMessage`/`NoWarn`/policy-file changes. Verified line by line against the Kotlin: all 34 `UniqueTarget` ordinals, inheritance chain, modifier types, `Displayable`/`CanIncludeSuppression`; the 4 `UniqueFlag` values; all 62 `UniqueParameterType` entries (names, examples, descriptions, display names, default severities, static sets and their insertion order, which `getErrorSeverity`/`isKnownValue`/autocomplete overrides exist and which fall through to the default). Constants, `BeliefType`, `SpyAction.displayString`, `ResourceType`, `Stat.statsWithCivWideField` inputs checked against Kotlin. `toIntOrNull` (incl. Unicode digits, overflow limits), `Suppression.isValidFilter`, `CivilopediaLink`, `ModName`, `PolicyFilter` branch slicing all equivalent. Determinism: no RNG, no culture-sensitive calls, all sets are insertion-ordered `LinkedHashSet`.
    - **Fix made by reviewer:** Kotlin's `toFloatOrNull` accepts Java hex float literals (`0x1.8p1`, `ScreenFloatValueRegEx` `HexString` branch); the port rejected them. Added hex validation + value parsing and 13 theory cases + a value test. Value is exact up to 53 significant bits (only validity is used by the engine).
    - **For downstream tickets:** (1) `UniqueParameterErrorSeverity` is declared at namespace level here, not nested in `UniqueType`; T026 must reuse it and add the `RulesetErrorSeverity` mapping (`PossibleFilteringUnique→WarningOptionsOnly`, `RulesetSpecific→Warning`, `RulesetInvariant→Error`). (2) `IRulesetObjectParameterView.HasTagUnique` and `HasGreatPersonUnique`/`HasRegionUnique` are text-matching defaults; Kotlin goes through `UniqueMap`/`hasUnique(UniqueType)` (conditionals evaluated against `GameContext.EmptyState`, timed triggerables excluded), so T027/T030 must override them. (3) `RulesetMapView` over `Dictionary` is insertion-ordered only without removals; T034's ruleset maps must preserve load order for autocomplete output. (4) `CountablesProvider` default is a stub until T028 (already TODO-marked).
### T026 Unique system: UniqueType enum
- State: Done
- Depends: T025
- Source: core/src/com/unciv/models/ruleset/unique/UniqueType.kt
- Fixtures: none - generate golden values (text/params/targets per entry, from Kotlin)
- Deliverables: csharp/Unciv.Core/Models/Ruleset/Unique/UniqueType.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~UniqueTypeTests`
- Claimed: 2026-10-24T02:48:00Z
- Notes: ~1.2k LOC mostly data. Preserve declaration order (ordinal) and placeholder text exactly. Consider a generated-from-Kotlin consistency test over all entries.
  - Implementer notes (Claude):
    - Deliverables: `UniqueType.cs` (enum, 638 entries, ordinals = Kotlin declaration order), `UniqueTypeData.cs` (declaration-ordered table `Rows[ordinal]` = the Kotlin constructor args: text, targets, flags, docDescription), `UniqueTypeExtensions.cs` (`Text`, `PlaceholderText`, `TargetTypes`, `Flags`, `DocDescription`, `ParameterTypeMap`, `CanAcceptUniqueTarget`, `UniqueTypeMap`, `Entries`), `UniqueTypeTests.cs` + `Golden/UniqueType.golden.tsv` (csproj copies it to the output).
    - `UniqueType.cs` and `UniqueTypeData.cs` were **generated from `UniqueType.kt`** by a throwaway Python script (tokenizes the entry list, resolves `ADDITIVE/MULTIPLICATIVE_BONUS_EXPLANATION`, the `$` template at the construction-cost entry, `Constants.freshWater`, `*UniqueTarget.Displayable/CanIncludeSuppression`, `UniqueFlag.setOf*` and `setOf(...)`); not committed. Do not hand-edit them for a Kotlin change: regenerate or edit the row.
    - Verification: golden file dumped by a reflection harness over the **real compiled Kotlin** (`UniqueType.values()` in the installed Unciv 4.22.13 `Unciv.jar`, run with `java` in `dotnet-dev`): ordinal, name, text, placeholderText, targetTypes, flags (in iteration order), docDescription, `parameterTypeMap`, and `canAcceptUniqueTarget` for all 638 x 34 target pairs. The jar's 638 entries equal the 638 in the repo source, so the repo and the release agree. I mutated a text and a target in the data and the tests failed as expected.
    - Deviations: `targetTypes` is a `LinkedHashSet` in declaration order (Kotlin's `HashSet<enum>` order is identity-hash arbitrary, so no code can depend on it; the golden target column is sorted by ordinal and compared as a set). `UniqueParameterErrorSeverity` stays where T025 declared it (namespace level, not nested). Not ported: `getDeprecationAnnotation` (JVM reflection; no entry has `@Deprecated`), `UniqueParameterErrorSeverity.getRulesetErrorSeverity` (needs `RulesetErrorSeverity`, T036: PossibleFilteringUnique to WarningOptionsOnly, RulesetSpecific to Warning, RulesetInvariant to Error), `toString()` (use `Text()`). `CHOOSE_MUSIC_DOCSTRING` embeds the five `MusicTrackChooserFlags` names as a constant (UI audio type, not ported); `Suppression.uniqueDocDescription` is a constant in `UniqueTypeData` until T036 ports `Suppression` (then point it there).
    - `ParameterTypeMap` follows the Kotlin initializer (split at `/`, strip trailing ASCII digits like Java's `\d+$`, `SafeValueOf`, drop unknowns). `parameterTypeMapInitializer` is `open` in Kotlin but no entry overrides it, so there is no override hook.
    - Ran Gate (`~UniqueTypeTests`, 9/9) and Standard checks (build -warnaserror 0 warnings, format clean, 675/675).
  - Review (Antigravity): Pass. Verified `UniqueType.cs` (all 638 entries in declaration order), `UniqueTypeData.cs`, `UniqueTypeExtensions.cs`, and `UniqueTypeTests.cs` against `UniqueType.kt` and the JVM golden dump (`UniqueType.golden.tsv`): text, placeholder text, target sets, flag order, doc descriptions, parameter type maps (`/` split, ASCII trailing digits stripped, `SafeValueOf`), `CanAcceptUniqueTarget` across all 638 x 34 pairs, and `UniqueTypeMap`. Also wired `UniqueType.Comment.Text()` into `TranslationFileReader.CommentUniqueText` (resolving `TODO(T026)`). Re-ran Gate (`~UniqueTypeTests`, 9/9 passed) and Standard checks (`dotnet build -warnaserror && dotnet format --verify-no-changes && dotnet test` — 692/692 passed).
### T027 Unique system: Unique, UniqueMap, LocalUniqueCache, TemporaryUnique, IHasUniques, GameContext
- State: Available
- Depends: T026, T020
- Source: core/src/com/unciv/models/ruleset/unique/Unique.kt, UniqueMap.kt, LocalUniqueCache.kt, TemporaryUnique.kt, IHasUniques.kt, GameContext.kt, UniqueTriggerExecutors.kt
- Fixtures: tests/src/com/unciv/uniques/TimedUniquesTests.kt (where state-independent)
- Deliverables: csharp/Unciv.Core/Models/Ruleset/Unique/*.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~UniqueTests`
- Claimed:
- Notes: UniqueMap must preserve insertion order. GameContext depends on Civilization/City/Tile: use state interfaces, owned by this ticket, implemented later. `stateBasedRandom` formula comes from T017. UniqueTriggerActivation (1.5k LOC) is deferred to land with Civ/City/Unit. Have `Unique` implement `IUniqueTargetView` and `IHasUniques` implement `IRulesetObjectParameterView` (from T025).

### T028 Unique system: Conditionals and Countables
- State: Available
- Depends: T027
- Source: core/src/com/unciv/models/ruleset/unique/Conditionals.kt, Countables.kt, expressions/{Expressions,Node,Operator,Parser,Tokenizer}.kt (the expression parser behind `ExpressionTests`)
- Fixtures: tests/src/com/unciv/uniques/CountableTests.kt, tests/src/com/unciv/uniques/ExpressionTests.kt
- Deliverables: csharp/Unciv.Core/Models/Ruleset/Unique/Conditionals.cs, Countables.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~CountableTests|FullyQualifiedName~ExpressionTests`
- Claimed:
- Notes: Fixtures that need TestGame are deferred to T035; port the rest now and list the deferred cases in Notes. Expression evaluation: Double vs Int semantics exact. Wire `Countables` into `UniqueParameterTypeExtensions.CountablesProvider` (or replace `TODO(T028)` in `UniqueParameterType.cs`).

### T029 Deprecated uniques and BackwardCompatibility
- State: Available
- Depends: T027
- Source: core/src/com/unciv/models/ruleset/unique/DeprecatedUniques.kt, logic/BackwardCompatibility.kt
- Fixtures: tests/src/com/unciv/uniques/DeprecatedUniquesTest.kt
- Deliverables: csharp/Unciv.Core/Models/Ruleset/Unique/DeprecatedUniques.cs, Logic/BackwardCompatibility.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~DeprecatedUniquesTests`
- Claimed:
- Notes: BackwardCompatibility touches GameInfo/Civ/City; port only parts whose types exist, record the rest as TODO(T###) with a real ticket.

### T030 Ruleset base objects: RulesetObject, Specialist, Speed, Policy, PolicyBranch, Belief, Event, Quest, RuinReward, GlobalUniques, ModOptions, Victory, Tutorial
- State: Available
- Depends: T027, T021, T049
- Source: core/src/com/unciv/models/ruleset/{RulesetObject,Specialist,Speed,Policy,PolicyBranch,Belief,Event,Quest,RuinReward,GlobalUniques,ModOptions,Victory,Tutorial,RulesetName}.kt
- Fixtures: tests/src/com/unciv/models/ruleset/VictoryAvailabilityTests.kt, VictoryMilestoneTests.kt (state-dependent parts after T035)
- Deliverables: csharp/Unciv.Core/Models/Ruleset/*.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~RulesetObjectTests`
- Claimed:
- Notes: Json field names/defaults must match Kotlin (rulesets load from JSON). Victory.kt is 454 LOC; split off if it exceeds scope and note it.

### T031 Ruleset tile objects: Terrain, TerrainType, TileImprovement, TileResource, ResourceType, ResourceSupplyList
- State: Available
- Depends: T030
- Source: core/src/com/unciv/models/ruleset/tile/*.kt
- Fixtures: none - generate golden values; tests/src/com/unciv/logic/map/TileImprovementConstructionTests.kt later with T038
- Deliverables: csharp/Unciv.Core/Models/Ruleset/Tile/*.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~RulesetTileTests`
- Claimed:
- Notes: Ordered collections stay ordered (ResourceSupplyList).

### T032 Ruleset nation and tech objects: Nation, CityStateType, Difficulty, Personality(Value), Era, TechColumn, Technology
- State: Available
- Depends: T030
- Source: core/src/com/unciv/models/ruleset/nation/*.kt, models/ruleset/tech/*.kt
- Fixtures: tests/src/com/unciv/logic/map/CityStateTypeStartBiasTests.kt (if type-only)
- Deliverables: csharp/Unciv.Core/Models/Ruleset/Nation/*.cs, Tech/*.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~RulesetNationTechTests`
- Claimed:
- Notes: Colors: engine-owned color type, no libGDX Color.

### T033 Ruleset unit and construction objects: BaseUnit, BaseUnitCost, Promotion, UnitType, UnitNameGroup, Building, IConstruction, PerpetualConstruction
- State: Available
- Depends: T031, T032
- Source: core/src/com/unciv/models/ruleset/unit/*.kt, models/ruleset/Building.kt, IConstruction.kt, PerpetualConstruction.kt
- Fixtures: tests/src/com/unciv/uniques/UnitUniquesTests.kt (state-dependent parts after T035)
- Deliverables: csharp/Unciv.Core/Models/Ruleset/Unit/*.cs, Building.cs, IConstruction.cs, PerpetualConstruction.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~RulesetUnitTests`
- Claimed:
- Notes: Cost/production math: Int vs Float and roundToInt (half-up helper) are the main traps. Methods needing City/Civilization go behind state interfaces from T027.

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
- Notes: May need partial Tile/Civ stubs; if so mark Blocked and split: extend it again after T039 and the Civilization tickets.

### T036 RulesetValidator
- State: Available
- Depends: T034
- Source: core/src/com/unciv/models/ruleset/validation/*.kt
- Fixtures: tests/src/com/unciv/uniques/RulesetValidatorTests.kt, tests/src/com/unciv/uniques/UniqueErrorTests.kt, tests/src/com/unciv/uniques/AllObjectsTests.kt
- Deliverables: csharp/Unciv.Core/Models/Ruleset/Validation/*.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~RulesetValidatorTests`
- Claimed:
- Notes: Error ordering and text must be identical to Kotlin. TextSimilarity: exact algorithm port. `Suppression.IsValidFilter` and doc constants are already ported on `UniqueParameterTypeExtensions` in T025; reuse or delegate to them in `Suppression`.

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
- State: Done
- Depends: none
- Source: logic/multiplayer, logic/github, logic/UncivKtor.kt, logic/files/{FileChooser,PlatformSaverLoader}.kt, utils/{Display,PlatformSpecific,Concurrency,Log,DebugUtils}.kt, models/translations/TranslationFileWriter.kt, models/skins, models/tilesets, ui tests
- Fixtures: none
- Deliverables: csharp/migration/backlog.md (Won't-port list), PROTOCOL.md pointer
- Gate: `n/a (docs only)`
- Claimed: 2026-10-24T02:58:00Z
- Notes: Docs-only ticket. Review each entry before listing: logic/files/UncivFiles and MapSaver, FileConversions, IMediaFinder may still be needed for save/load and are NOT on this list; handle them in the Phase E tickets. Also list (from T023): `DiacriticSupport` and the libGDX font glyph remapping, `TranslationFileWriter`, `Gdx.files` based translation lookup (replaced by `ITranslationFileHandle`).
  - Implementer notes (Antigravity):
    - Documented the exhaustive, categorized Won't-port inventory in `backlog.md` under `## Non-goals (Won't port)`.
    - Enumerated files and detailed technical rationale across 8 categories: Multiplayer/Server stack, GitHub/mod downloading, Platform-specific file pickers (`FileChooser`, `PlatformSaverLoader`), Platform host utilities (`Display`, `PlatformSpecific`, `Concurrency`, `Log`, `DebugUtils`), Translation file writer dev tooling (`TranslationFileWriter`), 2D Scene2D skins & sprite sheets (`models/skins/`, `models/tilesets/`), libGDX UI screens and desktop/android launchers, and libGDX UI tests (`tests/src/com/unciv/ui/`).
    - Verified engine save/load abstractions (`UncivFiles.kt`, `MapSaver.kt`, `FileConversions.kt`, `IMediaFinder.kt`) remain designated for deferred engine tickets and are excluded from Won't-port.
    - Added explicit pointer in `PROTOCOL.md` to `backlog.md`'s Won't-port catalog.
    - Standard checks verified clean (build -warnaserror, format, 548/548 tests pass).
  - Review notes (Claude): **Changes requested** (docs only, but this list is what later agents trust, so it must not exclude things the engine reads). I checked every listed path exists (all do, incl. the 8 `tests/src/com/unciv/ui` files) and then grepped the engine (`logic/`, `models/`, `utils/`) for what depends on the excluded areas:
    1. **#7 "all of `core/src/com/unciv/ui/`" is wrong as written: 106 engine files import `com.unciv.ui.*`.** Most frequent real dependencies: `ui.components.extensions.toPercent` (31, plus `colorFromRGB`, `isLargerThan`, `getNeedMoreAmountString`), `ui.screens.civilopediascreen.FormattedLine` (15) / `ICivilopediaText` / `CivilopediaCategories` and `ui.objectdescriptions.*` (`FormattedLineListBuilder`, `BaseUnit/Building/Technology/Nation/Resource/ImprovementDescriptions`, `uniquesToCivilopediaTextLines`: the ruleset objects build their own description text), `ui.components.fonts.Fonts` (14: stat characters, already inlined in `Stat.cs`) and `FontRulesetIcons`, `ui.screens.victoryscreen.RankingType` (12), `ui.audio.SoundPlayer`/`MusicTrackChooserFlags` and `UncivSound` (an enum field of `Stat`), `ui.components.MayaCalendar` (4), `UnitMovementMemoryType` (3), `ui.screens.overviewscreen.EmpireOverviewCategories` (3), `mapeditorscreen.MapGeneratorSteps`, `ui.images.ImageGetter` (8, portrait/icon names). Add a short sub-list "UI-package types the engine needs (to be ported or replaced, not dropped)" with these, and say for each whether it is ported as data/enum (`RankingType`, `MayaCalendar`, `UnitMovementMemoryType`, `UncivSound`, `EmpireOverviewCategories`, `MapGeneratorSteps`, `toPercent`), replaced by an engine text model (Civilopedia `FormattedLine`/descriptions: decide once, the client will want the same text), or cut at the call site (`ImageGetter`, `SoundPlayer`, screens/popups referenced by notification actions). The planners of the ruleset tickets (T030-T033) will hit this immediately.
    2. **`utils/DebugUtils` cannot be Won't-port as a whole: the game rules read it.** `DebugUtils.SIMULATE_UNTIL_TURN` (`GameInfo.isSimulation`, `nextTurn`), `SUPERCHARGED` (`CityStats`, changes yields), `VISIBLE_MAP` (`CivInfoTransientCache`, changes what a civ sees), `CIV_IDS_IN_EXPERIMENT_GROUP`, `SETTLER_SCORES`/`SHOW_SETTLER_SCORES` (written by `CityLocationTileRanker`). Split the entry: UI-only flags (`SHOW_TILE_COORDS`, `SHOW_TILE_IMAGE_LOCATIONS`, `SHOW_FPS`) are Won't-port; the gameplay-affecting ones become an explicit engine options object (part of the determinism story: they must be set through a command or fixed per game, never a hidden static). That is a small new ticket; mention it in the list.
    3. **`utils/Log` is used by 191 call sites in 42 engine files.** "Won't port" gives porters no rule, and we already see two different silent drops (`IdChecker` removed its `Log.error`, `Translations` swallows read errors). State the policy: the engine gets a thin `Log` facade (`Error`, `Debug`, ... forwarding to an injectable sink, default no-op, no clock, no `Gdx.app`) so call sites can be ported one-to-one; only the libGDX backend is Won't-port. Ticket it (it is a 30-minute task) so the first ruleset ticket does not invent its own.
    4. **`Concurrency` and the multiplayer residue are referenced from the engine.** `AlternatingStateManager` (`Concurrency.run`) and `RulesetCache` (`Concurrency.parallelize`) need a stated replacement (run the work synchronously/in order: ruleset loading must be deterministic and single-threaded in the engine). `logic.multiplayer.isUsersTurn` (used by `CityConstructions`), `FriendList.Friend` (used by `IdChecker`; the port has a nested stand-in) and `GameSettingsMultiplayer`/`ChatWebSocket` (inside `models/metadata/GameSettings.kt`) are reachable from engine code: say what replaces `isUsersTurn` (the command layer's actor/turn check, COMMANDS.md) and that `GameSettings` itself is a client settings model that is not ported (it is currently in neither list: state it, since `UncivGame.Current.settings.*` is read in engine code, e.g. `language` for translations, replaced by explicit parameters).
    5. Add the entries I put into the ticket's Notes after the implementer finished: `ui/components/fonts/DiacriticSupport` (and the libGDX font glyph remapping) and the `Gdx.files`-based translation lookup (replaced by `ITranslationFileHandle`); list #5 only names `TranslationFileWriter`.
    - Good: the categorization and rationale text for #1-#6 and #8 is accurate, the engine save/load exclusion note for `UncivFiles`/`MapSaver`/`FileConversions`/`IMediaFinder` is right, and the `PROTOCOL.md` pointer is in place. Standard checks (build, format, tests) pass; nothing else changed.
  - Rework notes (Claude): all five review items applied to the Non-goals section (entries 4, 5, 7 and the new 9): the `ui/` carve-out list with a disposition per type, `DebugUtils` split (new ticket **T048**), `Log` facade policy (new ticket **T047**, `TODO(T047)` at call sites meanwhile), `Concurrency` replacement, multiplayer/settings residue, `DiacriticSupport` and the `Gdx.files` lookup. Docs only; no code changed.
  - Review 2 (Antigravity): Pass. Verified all 5 review items in `## Non-goals (Won't port)` (entries 4, 5, 7, and 9): explicit `ui/` carve-out list with disposition per type, `DebugUtils` split pointing to `T048`, `Log` facade policy pointing to `T047`, synchronous `Concurrency` replacement, client settings/multiplayer residue handling, and `DiacriticSupport`/`Gdx.files` translation lookup notes. Standard checks clean (`dotnet build -warnaserror && dotnet format --verify-no-changes && dotnet test` — 701/701 passed).

### Planned (not yet ticketed)
Deferred until the ruleset and map tickets land (decided with the user): Civilization (split ~5: core, diplomacy, policy/tech managers, notifications/quests, vision/stats), Trade, City + CityStats + production, UnitManager/MapUnit (movement, actions), UniqueTriggerActivation, Battle, GameInfo, GameStarter, UncivFiles/MapSaver/IMediaFinder (save/load), end-to-end determinism over N turns (uses T011), then Automation (AI) last.


## Client track (Godot) — all tickets Blocked until the engine track delivers a playable core

Vision: a Civ 5-style client. Not a copy of Civ 5 assets (see Assets rule), but equivalent look and feel. Create detailed tickets here once their engine dependencies are `Done`; each must name an asset source and license.

### C000 Headless Godot test & snapshot harness
- State: In-Review [Antigravity]
- Claimed: 2026-10-24T03:08:00Z
- Depends: none (infrastructure/tooling)
- Deliverables:
  - Godot 4.x .NET CLI runtime setup in `dotnet-dev` container.
  - Headless test execution harness (CLI runner supporting `godot --headless --rendering-driver opengl3` with software rasterization or GdUnit4).
  - Offscreen viewport snapshot capture utility (`CaptureViewportPng`) saving rendered frames to PNG for visual validation.
  - Golden image diffing / perceptual hash comparator for automated visual regression detection.
  - Synthetic input simulator (`SimulateMouseClick`, `SimulateKey`) for headless interaction and hex-picking verification.
- Gate: A minimal headless test scene that boots Godot headlessly, renders a test 3D mesh, captures a viewport snapshot to PNG, verifies non-empty/expected pixel output, and exits 0.
- Notes: Prerequisite tooling for the client track so agents can autonomously validate rendering, materials, shaders, and UI interactions without an active display server or editor.
  - Implementer notes (Claude): Usage and design in `csharp/migration/GODOT.md`. Godot 4.7.2 .NET (pinned, SHA-512 verified by `tools/setup-godot.sh`, installed under `~/.local/opt`, not in the repo) renders under Xvfb with the Compatibility renderer on Mesa llvmpipe (`--rendering-driver opengl3`); pure `--headless` is not used because its dummy renderer produces no pixels. Needed container packages (installed once with dnf): Xvfb, mesa-dri/libGL/vulkan drivers, libXcursor/Xinerama/Xi/Xrandr, fontconfig, alsa-lib. Deliverables: `Unciv.Harness` (plain net10.0, in the sln: `RgbaImage`, `PerceptualHash` 64-bit dHash, `ImageStats`, `GoldenComparer` with hash + mean-diff + differing-pixel tolerances; 9 tests in `Unciv.Core.Tests/Harness`) and `Unciv.Godot.Harness` (Godot project, NOT in the sln: `ViewportCapture.CaptureViewportPng`, `InputSimulator.SimulateMouseClick/MouseMove/Key`, `HexPicking.PickGroundPoint`, `SmokeTest` scene, `tools/run-smoke.sh`, `golden/smoke.png`).
  - Gate run (this session): `Unciv.Godot.Harness/tools/run-smoke.sh` exit 0 (hex prism rendered, 2.7% coverage, 23 colours, two simulated clicks picked the projected ground points within 0.01, horizontal ray misses, golden match with hash distance 0, mean 0.0000); `--golden` with a wrong image and with a missing file each exit 1. Standard checks: build -warnaserror, format (also run in the Godot project), 701/701 tests. The golden PNG was reviewed by eye (green shaded hex prism on a navy background).
  - Deviations / limits: the Godot project is outside `Unciv.sln` so the Standard checks do not need the Godot SDK or runtime; its gate is the script. Linux x86-64 only. The namespace is `Unciv.GodotHarness` because `Unciv.Godot` would shadow Godot's own `Godot` namespace in every file. Reviewer: run the setup script on a clean container if you can, and open `out/smoke.png` yourself.

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
- State: Done
- Claimed: 2026-10-24T03:00:00Z
- Depends: T007, T021
- Source: `csharp/migration/ai-spec/data/ai_defines.tsv`, `grand_strategy_flavors.tsv`, `tactical_moves.tsv`
- Deliverables: `csharp/Unciv.Core/AI/Data/` data models for `GlobalAIDefines`, `GrandStrategyFlavors`, `TacticalMoveDefinition`, TSV/JSON loader, unit tests
- Gate: `dotnet test --filter FullyQualifiedName~AIDefinesTests`
- Notes: Provides typed access to the ~1,600 XML-derived defines, flavor vectors, and tactical move priorities.
  - Implementer notes (Claude): `Unciv.AI.Data`: `GlobalAIDefines` (453 defines, file order; per-edition `AIEdition` Base/GK/BNW; `TryGetDouble/GetDouble/GetFloat/GetInt/GetBool/IsDefined`; an empty cell means the define does not exist in that edition, so Get throws and IsDefined is false; `GetInt` rejects values written with a decimal point), `GrandStrategyFlavors` (the 3 `AIGrandStrategy_*` tables, edition-independent, ordered; `Get` returns 0 for an absent entry), `TacticalMoveDefinition` (record per row, blank flavor weight = null, `IsInEdition`), `TsvTable` (internal reader that checks the header and column counts). The three TSVs are not copied: `Unciv.Core.csproj` embeds them straight from `migration/ai-spec/data/` (logical names `Unciv.AI.Data.*.tsv`) so there is one source of truth; `LoadEmbedded()` on each type, `Parse(string)` for tests. 17 tests (values spot-checked against the TSVs and SPEC 3.3, edition gaps, malformed-input rejection). Standard checks: build -warnaserror, format, 692/692 tests.
  - Deviations: no JSON loader (the TSV is the shipped format and nothing needs JSON yet). Values are kept as `double` parsed from the text (the engine's own types are unknown, SPEC marks that [?]); `GetFloat` narrows. `diplomacy_ai_defines.tsv` is not loaded (not in this ticket's Source; same shape with base/bnw columns, add with the diplomacy AI ticket). Tactical rows keep file order; no sorting is applied, the queue ticket (A004) must decide tie order, since several rows share a priority (e.g. 130, 110, 100, 25, 15, 10, 1, -1).
  - Review (Antigravity — Multi-Persona Adversarial Review): Pass (`APPROVED`).
    - **Auditor:** Verified 1:1 data fidelity against `ai_defines.tsv` (453 rows), `grand_strategy_flavors.tsv` (35 rows across 3 tables), and `tactical_moves.tsv` (55 rows). File order preserved via `OrderedDictionary`. Invariant culture enforced on all numeric parses; `HashSet` in `TacticalMoveDefinition.Parse` is used strictly for duplicate detection and never iterated. No warnings, suppressions, or banned APIs.
    - **Adversary:** Verified strict rejection of missing/mismatched headers, wrong column counts, duplicate keys, non-numeric cells, non-integer cells in `GetInt` (rejecting decimal-point floats like `2.0`), and edition gaps (`IsDefined` false / `KeyNotFoundException` on `Get*`).
    - **Pragmatist:** Single-source-of-truth embedding directly from `../migration/ai-spec/data/*.tsv` avoids asset duplication; clean, minimal surface area. Gate (17/17) and Standard checks (701/701) passed.

### A002 Leader flavor system & Grand Strategy evaluation
- State: Available
- Depends: A001, T010, T021
- Source: `csharp/migration/ai-spec/SPEC.md` §2, §3 (`evaluateGrandStrategy`)
- Deliverables: `csharp/Unciv.Core/AI/GrandStrategy/` (`LeaderPersonalityFlavors`, `GrandStrategy`, `GrandStrategyEvaluator`), tests
- Gate: `dotnet test --filter FullyQualifiedName~GrandStrategyTests`
- Notes: Evaluates 4 grand strategies (Conquest, Culture, UN, Spaceship). Preserves dot product flavor scoring, 10-turn lock (`AI_GRAND_STRATEGY_NUM_TURNS_STRATEGY_MUST_BE_ACTIVE`), hysteresis (`AI_GRAND_STRATEGY_CURRENT_STRATEGY_WEIGHT = 50`), rival-chasing penalty, and RNG noise using `GameRandom`.

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
