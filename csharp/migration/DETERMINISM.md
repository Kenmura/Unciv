# Determinism Specification & Verification

<!-- toc -->
## Contents

- [1. Goal](#1-goal)
- [2. Core Invariants](#2-core-invariants)
  - [2.1 Seeded Randomness](#21-seeded-randomness)
  - [2.2 Time and Clocks](#22-time-and-clocks)
  - [2.3 Numeric Representation and Rounding](#23-numeric-representation-and-rounding)
  - [2.4 Collection and Iteration Order](#24-collection-and-iteration-order)
  - [2.5 Culture and Hashing](#25-culture-and-hashing)
  - [2.6 Debug and cheat flags](#26-debug-and-cheat-flags)
- [3. State Hashing Harness (`StateHasher`)](#3-state-hashing-harness-statehasher)
  - [3.1 Algorithm](#31-algorithm)
  - [3.2 Canonical Handling](#32-canonical-handling)
  - [3.3 Byte layout (for reproducing hashes outside C#)](#33-byte-layout-for-reproducing-hashes-outside-c)
  - [3.4 Usage Example](#34-usage-example)
<!-- /toc -->

## 1. Goal

The headless C# Unciv engine is designed to be **100% bit-exact and deterministic** across platforms (Linux, Windows, macOS, Android, WebAssembly) and across consecutive runs.

Determinism enables:
1. **Command Log Replays & Lockstep Networking**: Given the same initial game state parameters (map seed, ruleset, civilization setup) and the ordered sequence of commands (`ICommand`), all clients and servers produce bit-for-bit identical state without transmitting massive save files mid-turn.
2. **Instant Desync Detection**: By exchanging or logging canonical 64-bit state hashes (`StateHasher`), participants immediately detect when any client or server state diverges.
3. **Automated Regression & AI Testing**: Multi-turn simulations run against fixed seeds produce invariant state hashes, catching subtle logic regressions or float divergence before release.

---

## 2. Core Invariants

### 2.1 Seeded Randomness
- Engine logic must never use `System.Random` or `System.Security.Cryptography.RandomNumberGenerator`. Both are banned at compile time via Roslyn analyzers (`RS0030`).
- All random number generation uses `Unciv.Utils.GameRandom`, which faithfully replicates Kotlin's `kotlin.random.XorWowRandom` bit-for-bit.
- Seed sequence reproducibility is verified against JVM stdlib golden sequences.

### 2.2 Time and Clocks
- Wall-clock time (`DateTime.Now`, `DateTime.UtcNow`, `Stopwatch`, `Environment.TickCount`) is strictly banned inside engine state and logic.
- Engine progress is driven exclusively by discrete turn counters and sequential command sequences (`LastSeq + 1`).

### 2.3 Numeric Representation and Rounding
- **Widths**: Kotlin `Int` is C# `int`, `Float` is `float`, `Double` is `double`. No implicit promotion to wider types is permitted.
- **Rounding Semantics**:
  - Half-up rounding (Kotlin `roundToInt()`) is implemented via `KotlinMath.RoundToInt(float)`. Bare `Math.Round` is banned because .NET defaults to banker's (half-to-even) rounding.
  - Truncation toward zero and saturation (Kotlin `Float.toInt()` / `Double.toInt()`) is handled via `KotlinMath.FloatToInt` / `KotlinMath.DoubleToInt`.
  - Half-even rounding (when explicitly intended by Kotlin `round()`) passes `MidpointRounding.ToEven` explicitly.
- **Division and Modulo**:
  - Unchecked integer arithmetic wraps on overflow, matching JVM semantics.
  - Modulo operations where the divisor's sign must be preserved use `KotlinMath.Mod(val, m)` to mirror Kotlin's `Int.mod`.

### 2.4 Collection and Iteration Order
- Iteration order of hash-based collections (`Dictionary<K, V>`, `HashSet<T>`) is undefined and must **never** feed game mutation, serialization output, or random seeds.
- Collections where insertion order matters use `OrderedDictionary<K, V>` or `Counter<K>`.
- Any sorting over game state must use stable ordering (`OrderBy` / `ThenBy`), as `List<T>.Sort()` and `Array.Sort()` are unstable quicksort implementations and are banned.
- Kotlin's own `HashSet`/`HashMap` iterate in JVM hash order, which no C# collection reproduces. Where a Kotlin call site iterates one, port it with `JavaHashSet`/`JavaHashMap` (bit-exact JVM bucket order for String/Int/Long/Boolean/`HexCoord` keys) or, for enum and identity-hash keys, with an insertion-ordered collection; `HASH_ORDER_AUDIT.md` lists every occurrence and the policy (T044).

### 2.5 Culture and Hashing
- `string.GetHashCode()` is randomized per process in .NET Core/.NET 5+ and is strictly banned. Where Kotlin hashes into state (`GameContext.stateBasedRandom`), use `JavaHash` and `Randomness.HashOf` (T017), which reproduce the JVM values bit-for-bit.
- All strings and numbers parse and format with `CultureInfo.InvariantCulture`.
- String comparisons and ordering use `StringComparer.Ordinal` / `string.CompareOrdinal`.


### 2.6 Debug and cheat flags
- Upstream `DebugUtils` is a process-wide mutable object whose flags the rules read: `VISIBLE_MAP` (vision), `SUPERCHARGED` (city yields), `SIMULATE_UNTIL_TURN` (turn handling; the engine resets it itself), `CIV_IDS_IN_EXPERIMENT_GROUP` (AI A/B switches). A static cannot take part in "initial state + command log reproduces the game", so these live in `Unciv.Logic.EngineOptions` (T048), an instance owned by the game (the `GameInfo` port holds it, `TODO(T046)`), included in the state hash, changed only through a command.
- The pure display flags (`SHOW_TILE_COORDS`, `SHOW_TILE_IMAGE_LOCATIONS`, `SHOW_FPS`) are client concerns and not ported. `ShowSettlerScores`/`SettlerScores` are diagnostics output that rules never read; they are kept on the object but excluded from the hash.
- Porting rule: wherever Kotlin reads `DebugUtils.X`, take an `EngineOptions` from the game being operated on; never add a static.
---

## 3. State Hashing Harness (`StateHasher`)

The engine provides `Unciv.Utils.StateHasher` as the canonical hashing utility for engine state.

### 3.1 Algorithm
- 64-bit FNV-1a hash (`offset_basis = 14695981039346656037`, `prime = 1099511628211`).
- Platform, endianness, and architecture invariant.

### 3.2 Canonical Handling
- **Floats and Doubles**: NaN values across IEEE 754 representations are canonicalized (`0x7fc00000` for 32-bit float, `0x7ff8000000000000` for 64-bit double).
- **Strings**: Hashed by length prefix followed by UTF-16 code units; `null` is distinguished with a dedicated sentinel.
- **Engine Data Types**: Dedicated `.Add(...)` overloads for `HexCoord`, `Vector2`, `Vector3`, `Stats`, `StatMap`, `Counter<K>`, and `GameRandom`.
- **Domain Objects**: Any game object can implement `IStateHashable` to serialize its state directly into the hasher.
- **Unordered Collections**: `.AddUnorderedMap(...)` and `.AddUnorderedSet(...)` sort by an ordinal key string before hashing, so insertion order does not matter. The key strings must be unique; duplicates throw `ArgumentException` (the sort could not otherwise be independent of the source order).
- **Enums**: `.Add(enumValue)` hashes the member *name*, stable when members are reordered or renumbered.
- **Order-sensitive on purpose**: `Add(Counter<K>)`, `Add(StatMap)` and `AddSequence` hash in iteration order, because Kotlin `LinkedHashMap`/list order is game relevant. Two Counters that are *equal* (equality ignores order) can therefore hash differently; that is a real state difference.

### 3.3 Byte layout (for reproducing hashes outside C#)

The hash is FNV-1a 64 over this byte stream, starting from the offset basis, all integers two's complement **little endian**:

| Value | Bytes |
|---|---|
| `bool` | 1 byte, `0` or `1` |
| `int` | 4 bytes |
| `long` | 8 bytes |
| `float` | the 4 bytes of `floatToIntBits` (any NaN is `0x7fc00000`; `-0f` differs from `0f`) |
| `double` | the 8 bytes of `doubleToLongBits` (any NaN is `0x7ff8000000000000`) |
| `string` | `int` length in UTF-16 code units, then each unit as 2 bytes; `null` is the single `int` `-1` |
| `HexCoord` | `int x`, `int y` |
| `Vector2` / `Vector3` | the `float` components |
| `Stats` | `float` production, food, gold, science, culture, happiness, faith |
| `StatMap`, `Counter<K>` | `int` count, then per entry in **iteration order**: key (`string`, `Counter` uses `key.ToString()`), then the value |
| `IStateHashable` | `int` `0` for null, else `int` `1` then the object's own fields |
| `GameRandom` | an `IStateHashable`: `x y z w v addend` as `int`s |
| sequence | `int` count, then the items |
| unordered map / set | `int` count, then entries sorted by ordinal (UTF-16 code unit) comparison of the key string: key, then value (set: key only) |
| enum | the member name as a `string` |

Reference values (verified with an independent Python implementation, see `StateHasherTests.GoldenHashes_PinAlgorithmAndByteLayout`):
`Add(42)` is `8D9AADC8352FDF7F`, `Add("Unciv")` is `D969796611AEC9B7`, `Add(HexCoord.Of(2, 3))` is `06CD822B3B579854`,
`Add(new GameRandom(42))` is `D0EA959456A64405`. FNV-1a 64 detects desync; it is not an integrity check (collisions are possible, about 2^-64 per comparison).

### 3.4 Usage Example

```csharp
ulong hash = StateHasher.ComputeHash(hasher =>
{
    hasher.Add("turn").Add(gameState.Turn);
    hasher.Add("rng").Add(gameState.Random);
    hasher.AddSequence(gameState.Units, (h, unit) => unit.HashState(h));
});
```
