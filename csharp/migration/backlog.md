# Migration Backlog

Rules: see `PROTOCOL.md`. Tell an agent **"Begin Conversion work"** to start.
Seed tickets were chosen as leaf files (no engine dependencies beyond libGDX math/JVM stdlib). Expand as needed.

## Non-goals
See `PROTOCOL.md`: Unciv multiplayer/servers, libGDX UI, Android. Tickets for these are `Won't port`.

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
- State: Needs-Review [Claude]
- Claimed: 2026-10-24T01:31:07Z
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

### T019 Event bus
- State: Needs-Review [Antigravity]
- Depends: none
- Source: core/src/com/unciv/logic/event/EventBus.kt, Event.kt
- Fixtures: none - generate golden values
- Deliverables: csharp/Unciv.Core/Logic/Event/EventBus.cs, Event.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~EventBusTests`
- Claimed: 2026-10-24T01:28:10Z
- Notes: Listener invocation order must be deterministic (registration order). Replace Kotlin weak/threading specifics with deterministic single-threaded equivalents; note deviations.
  - Implementer notes (Antigravity): Ported `EventBus` and `EventReceiver` under `Unciv.Logic.Event`:
    - Synchronous, deterministic dispatch invoking matching listeners in strictly monotonic registration order (using `NextSeq` and stable `OrderBy`).
    - Full polymorphic dispatch: event types dispatch to listeners registered for the concrete event class, any base classes, and implemented interfaces (assignable from event type).
    - Deduplicated execution per event dispatch matching Kotlin's `getListeners` Set semantics.
    - Lifecycle management via `EventReceiver` (`Receive<T>`, `StopReceiving()`, `IDisposable`) and `EventBus.Reset()`.
    - WeakReferences and GDX render-thread dependencies removed for deterministic headless execution.
    - Ran Gate (7 passed) and Standard checks (build -warnaserror clean, format clean, full `dotnet test` 348/348 passed).

### T020 MultiFilter and IdChecker
- State: In-Progress [Antigravity]
- Depends: none
- Source: core/src/com/unciv/logic/MultiFilter.kt, logic/IdChecker.kt
- Fixtures: tests/src/com/unciv/logic/MultiFilterTests.kt, tests/src/com/unciv/logic/IdHelperTests.kt
- Deliverables: csharp/Unciv.Core/Logic/MultiFilter.cs, IdChecker.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~MultiFilterTests|FullyQualifiedName~IdCheckerTests`
- Claimed: 2026-10-24T01:34:12Z
- Notes: Pure string logic; keep Kotlin Regex/string semantics exact (culture-invariant).

### T021 Metadata models: LocaleCode, GameParameters, GameSetupInfo, Player, BaseRuleset, ModCategories
- State: Available
- Depends: T007
- Source: core/src/com/unciv/models/metadata/LocaleCode.kt, GameParameters.kt, GameSetupInfo.kt, Player.kt, BaseRuleset.kt, ModCategories.kt
- Fixtures: none - generate golden JSON by serializing from Kotlin
- Deliverables: csharp/Unciv.Core/Models/Metadata/*.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~MetadataTests`
- Claimed:
- Notes: Serialized field names must match Kotlin JSON. GameSettings/Migrations/OverviewPersistableData are client settings: out of scope unless a later ticket needs them.

### T022 Map size and parameters: MapSize, MapParameters, NeighborDirection, RoadStatus
- State: Available
- Depends: T006, T007, T010
- Source: core/src/com/unciv/logic/map/MapSize.kt, MapParameters.kt, NeighborDirection.kt, logic/map/tile/RoadStatus.kt
- Fixtures: none - generate golden values (incl. MapSize radius/size math, MapParameters JSON)
- Deliverables: csharp/Unciv.Core/Logic/Map/MapSize.cs, MapParameters.cs, NeighborDirection.cs, Tile/RoadStatus.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~MapParametersTests`
- Claimed:
- Notes: Unblocks half of T015. Watch Kotlin Int/Float rounding in MapSize custom-size logic (roundToInt is half-up).

### T023 Translation core (headless)
- State: Available
- Depends: T018
- Source: core/src/com/unciv/models/translations/Translations.kt, TranslationEntry.kt, TranslationCache.kt, TranslationFileReader.kt, TranslationExtensions.kt (`tr()` and its helpers)
- Fixtures: tests/src/com/unciv/logic/TranslationTests.kt, tests/src/com/unciv/logic/TutorialTranslationTests.kt (only cases not requiring libGDX/assets); otherwise generate golden values
- Deliverables: csharp/Unciv.Core/Models/Translations/*.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~TranslationTests`
- Claimed:
- Notes: Decided: ported headlessly now. File access behind an interface (no libGDX FileHandle). Exclude TranslationFileWriter (dev tooling; Won't port, T043). Translation templates/placeholders/conditionals `[]`/`<>` must match Kotlin exactly; culture-invariant ordering. Lookups must not depend on iteration order of hash collections.

### T024 Holiday dates
- State: Available
- Depends: none
- Source: core/src/com/unciv/logic/HolidayDates.kt
- Fixtures: tests/src/com/unciv/logic/HolidayDatesTest.kt
- Deliverables: csharp/Unciv.Core/Logic/HolidayDates.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~HolidayDatesTests`
- Claimed:
- Notes: Time is an explicit input (DateOnly/ints), never DateTime.Now. Use vetted rounding/calendar helpers.

### T025 Unique system: UniqueTarget, UniqueFlag, UniqueParameterType
- State: Available
- Depends: T018
- Source: core/src/com/unciv/models/ruleset/unique/UniqueTarget.kt, UniqueFlag.kt, UniqueParameterType.kt
- Fixtures: none - generate golden values (parameter matching/validation results)
- Deliverables: csharp/Unciv.Core/Models/Ruleset/Unique/UniqueTarget.cs, UniqueFlag.cs, UniqueParameterType.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~UniqueParameterTypeTests`
- Claimed:
- Notes: UniqueParameterType (797 LOC) references Ruleset for validation; introduce minimal interfaces (e.g. IRulesetView) and note them for T034 to implement. Enum ordinals/names must match Kotlin (serialized).

### T026 Unique system: UniqueType enum
- State: Available
- Depends: T025
- Source: core/src/com/unciv/models/ruleset/unique/UniqueType.kt
- Fixtures: none - generate golden values (text/params/targets per entry, from Kotlin)
- Deliverables: csharp/Unciv.Core/Models/Ruleset/Unique/UniqueType.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~UniqueTypeTests`
- Claimed:
- Notes: ~1.2k LOC mostly data. Preserve declaration order (ordinal) and placeholder text exactly. Consider a generated-from-Kotlin consistency test over all entries.

### T027 Unique system: Unique, UniqueMap, LocalUniqueCache, TemporaryUnique, IHasUniques, GameContext
- State: Available
- Depends: T026, T020
- Source: core/src/com/unciv/models/ruleset/unique/Unique.kt, UniqueMap.kt, LocalUniqueCache.kt, TemporaryUnique.kt, IHasUniques.kt, GameContext.kt, UniqueTriggerExecutors.kt
- Fixtures: tests/src/com/unciv/uniques/TimedUniquesTests.kt (where state-independent)
- Deliverables: csharp/Unciv.Core/Models/Ruleset/Unique/*.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~UniqueTests`
- Claimed:
- Notes: UniqueMap must preserve insertion order. GameContext depends on Civilization/City/Tile: use state interfaces, owned by this ticket, implemented later. `stateBasedRandom` formula comes from T017. UniqueTriggerActivation (1.5k LOC) is deferred to land with Civ/City/Unit.

### T028 Unique system: Conditionals and Countables
- State: Available
- Depends: T027
- Source: core/src/com/unciv/models/ruleset/unique/Conditionals.kt, Countables.kt
- Fixtures: tests/src/com/unciv/uniques/CountableTests.kt, tests/src/com/unciv/uniques/ExpressionTests.kt
- Deliverables: csharp/Unciv.Core/Models/Ruleset/Unique/Conditionals.cs, Countables.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~CountableTests|FullyQualifiedName~ExpressionTests`
- Claimed:
- Notes: Fixtures that need TestGame are deferred to T035; port the rest now and list the deferred cases in Notes. Expression evaluation: Double vs Int semantics exact.

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
- Depends: T027, T021
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
- Notes: File access behind an interface (no libGDX FileHandle). Load order of files/mods must be deterministic and equal to Kotlin's. Implements the interfaces left by T025/T027.

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
- Notes: Error ordering and text must be identical to Kotlin. TextSimilarity: exact algorithm port.

### T037 Golden test: load and validate Vanilla and bundled mod rulesets
- State: Available
- Depends: T036, T035
- Source: none (integration test)
- Fixtures: tests/src/com/unciv/logic/LoadGameTest.kt (ruleset portion), tests/src/com/unciv/logic/ModConstantsTests.kt
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
- Notes: Tile.kt is 1.3k LOC; split off Tile stats/improvements if needed. Civ/City/Unit references go behind interfaces from T027. Kotlin lazy/cached fields must not change observable behaviour.

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
- State: Available
- Depends: none
- Source: logic/multiplayer, logic/github, logic/UncivKtor.kt, logic/files/{FileChooser,PlatformSaverLoader}.kt, utils/{Display,PlatformSpecific,Concurrency,Log,DebugUtils}.kt, models/translations/TranslationFileWriter.kt, models/skins, models/tilesets, ui tests
- Fixtures: none
- Deliverables: csharp/migration/backlog.md (Won't-port list), PROTOCOL.md pointer
- Gate: `n/a (docs only)`
- Claimed:
- Notes: Docs-only ticket. Review each entry before listing: logic/files/UncivFiles and MapSaver, FileConversions, IMediaFinder may still be needed for save/load and are NOT on this list; handle them in the Phase E tickets.

### Planned (not yet ticketed)
Deferred until the ruleset and map tickets land (decided with the user): Civilization (split ~5: core, diplomacy, policy/tech managers, notifications/quests, vision/stats), Trade, City + CityStats + production, UnitManager/MapUnit (movement, actions), UniqueTriggerActivation, Battle, GameInfo, GameStarter, UncivFiles/MapSaver/IMediaFinder (save/load), end-to-end determinism over N turns (uses T011), then Automation (AI) last.


## Client track (Godot) — all tickets Blocked until the engine track delivers a playable core

Vision: a Civ 5-style client. Not a copy of Civ 5 assets (see Assets rule), but equivalent look and feel. Create detailed tickets here once their engine dependencies are `Done`; each must name an asset source and license.

### C001 Godot project skeleton
- State: Blocked
- Depends: T006 and a loadable game state (ticket to be added)
- Deliverables: csharp/Unciv.Godot/ project consuming Unciv.Core, empty scene, CI-friendly headless build
- Gate: `dotnet build Unciv.Godot` (headless; no editor required)

### C002 3D hex terrain rendering
- State: Blocked
- Depends: C001
- Scope: 3D hex tile meshes with per-terrain materials and elevation (grass, plains, desert, tundra, snow, hills, mountains, forest/jungle, water with depth), smooth blending between adjacent terrains, fog of war and visibility tinting, tile yields and improvements as in-world props.
- Notes: Grass rendered as real textured/shaded terrain with instanced vegetation; no flat sprite tiles.

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
