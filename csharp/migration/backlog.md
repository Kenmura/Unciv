# Migration Backlog

Rules: see `PROTOCOL.md`. Tell an agent **"Begin Conversion work"** to start.
Seed tickets were chosen as leaf files (no engine dependencies beyond libGDX math/JVM stdlib). Expand as needed.

### T001 LongPriorityQueue
- State: Needs-Review [Claude]
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

### T002 Perlin noise
- State: In-Progress [Antigravity]
- Depends: none
- Source: core/src/com/unciv/logic/map/mapgenerator/Perlin.kt
- Fixtures: none – generate golden values by running the Kotlin code at fixed inputs
- Deliverables: csharp/Unciv.Core/Logic/Map/MapGenerator/Perlin.cs, csharp/Unciv.Core.Tests/Logic/Map/MapGenerator/PerlinTests.cs
- Gate: `dotnet test --filter FullyQualifiedName~PerlinTests`
- Notes: Watch Float/Double and floor semantics.

### T003 Vector2 / Vector3 shims
- State: Available
- Depends: none
- Source: libGDX `com.badlogic.gdx.math.Vector2/Vector3` (only members used by Unciv logic code; grep `core/src/com/unciv/logic`)
- Fixtures: none – port the used members with float semantics; test against hand-checked values
- Deliverables: csharp/Unciv.Core/Math/Vector2.cs, Vector3.cs, matching tests
- Gate: `dotnet test --filter FullyQualifiedName~VectorTests`
- Notes: Must be 32-bit float, value or reference semantics matching Kotlin usage (libGDX vectors are mutable classes with equals by value).

### T004 Stat enum and Stats
- State: Available
- Depends: none
- Source: core/src/com/unciv/models/stats/Stat.kt, Stats.kt, INamed.kt, NamedStats.kt, SubStat.kt, GameResource.kt
- Fixtures: grep tests/ for `Stats`; otherwise generate golden values
- Deliverables: csharp/Unciv.Core/Models/Stats/*.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~StatsTests`
- Notes: Strip UI deps (Color, Fonts, NotificationIcon, UncivSound, `tr`). Keep translation keys as strings only; record stripped members in Notes so a later ticket can restore them.

### T005 Counter
- State: Available
- Depends: none
- Source: core/src/com/unciv/models/Counter.kt
- Fixtures: grep tests/ for `Counter`; otherwise generate golden values
- Deliverables: csharp/Unciv.Core/Models/Counter.cs, tests
- Gate: `dotnet test --filter FullyQualifiedName~CounterTests`
- Notes: Drop libGDX Json serialization; leave a TODO ticket for the JSON layer. Preserve LinkedHashMap insertion order.

### T006 HexMath
- State: Available
- Depends: T003
- Source: core/src/com/unciv/logic/map/HexMath.kt
- Fixtures: tests/src/com/unciv/logic/map/HexmathTests.kt
- Deliverables: csharp/Unciv.Core/Logic/Map/HexMath.cs, csharp/Unciv.Core.Tests/Logic/Map/HexMathTests.cs
- Gate: `dotnet test --filter FullyQualifiedName~HexMathTests`
- Notes: HexMath references `Tile` and Json. Port only the pure functions now; add follow-up tickets (with Depends on the Tile ticket) for the Tile/Json-dependent members. Fixtures use Ruleset/Terrain – port only cases that don't need them and list the rest in a follow-up ticket.
