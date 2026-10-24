# Determinism Specification & Verification

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

### 2.5 Culture and Hashing
- `string.GetHashCode()` is randomized per process in .NET Core/.NET 5+ and is strictly banned.
- All strings and numbers parse and format with `CultureInfo.InvariantCulture`.
- String comparisons and ordering use `StringComparer.Ordinal` / `string.CompareOrdinal`.

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
- **Unordered Collections**: `.AddUnorderedMap(...)` explicitly sorts keys with `StringComparer.Ordinal` before hashing entries to ensure insertion-order independence.

### 3.3 Usage Example

```csharp
ulong hash = StateHasher.ComputeHash(hasher =>
{
    hasher.Add("turn").Add(gameState.Turn);
    hasher.Add("rng").Add(gameState.Random);
    hasher.AddSequence(gameState.Units, (h, unit) => unit.HashState(h));
});
```
