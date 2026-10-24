# Unciv Command & Event Architecture

## 1. Problem Statement

In the original Kotlin Unciv codebase, game state mutations are executed imperatively and directly across UI controllers, automation managers, and data classes:

- UI clicks directly mutate `GameInfo`, `City`, `MapUnit`, and `Civilization` in memory.
- Multiplayer is implemented via external HTTP polling (`Dropbox` or custom server) that uploads and downloads full game saves (`GameInfo.json`) between turns.
- Mid-turn simultaneous turns or real-time actions are unsupported.
- Replaying turns, verifying actions against cheating, or running an authoritative server requires executing full UI/logic loops without a clear protocol boundary.

For the C# engine and modern clients (such as Godot 3D / Civ 5-style client), an **authoritative command/event layer** separates user intent from state mutation.

---

## 2. Core Principles

1. **Player Intent as Data (`ICommand`)**:
   Clients never mutate game state directly. Every user or AI action is packaged as an immutable, serializable command.

2. **Validation Pipeline (`Validate`)**:
   Pure query executed before mutation. Validates prerequisites, turn order, visibility, pathing, and costs without altering state.

3. **Deterministic Execution (`Execute`)**:
   Applying a valid command to `(State, Command, GameRandom)` is a pure, deterministic function. No wall-clock time, no system randomness, no external I/O.

4. **Domain Events (`IGameEvent`)**:
   Executing a command emits discrete domain events (`UnitMoved`, `CombatResolved`, `ConstructionStarted`, `TurnEnded`). Events inform UI animations, sounds, notifications, and client state updates.

5. **Authoritative Server & Lockstep Ready**:
   - In **Client-Server Mode**, the server executes commands and broadcasts events or diffs to clients.
   - In **Lockstep Mode**, all clients receive the ordered command stream and execute identical deterministic mutations locally.
   - In **Single-Player Mode**, the client runs the server processor directly in-process with zero network overhead.

---

## 3. Data Flow

```
+---------------+                    +--------------------+
|  Godot Client |                    | Authoritative Core |
+---------------+                    +--------------------+
        |                                      |
        |  1. Send Command (e.g. MoveUnit)     |
        |------------------------------------->|
        |                                      |  2. Validate(State)
        |                                      |     [Pass / Fail]
        |                                      |
        |                                      |  3. Execute(State, GameRandom)
        |                                      |     - Mutates state deterministically
        |                                      |     - Produces Domain Events
        |                                      |
        |  4. Broadcast Events (UnitMoved)     |
        |<-------------------------------------|
        |                                      |
        |  5. Play Animations & Sound Effects  |
        |     Update Client View               |
```

---

## 4. Command Hierarchy

All commands implement `ICommand`:

```csharp
public interface ICommand
{
    Guid CommandId { get; }
    string PlayerCiv { get; }
    int Turn { get; }
    string CommandType { get; }

    CommandResult Validate(IGameContext context);
    IReadOnlyList<IGameEvent> Execute(IGameContext context, GameRandom rng);
}
```

### Core Commands

| Command | Payload | Purpose |
|---|---|---|
| `MoveUnitCommand` | `int UnitId`, `HexCoord Target` | Moves unit along valid path, spending movement points. |
| `UnitActionCommand` | `int UnitId`, `string ActionType` | Fortify, Sleep, Disband, Setup, Explore, Pillage, etc. |
| `FoundCityCommand` | `int UnitId`, `string CityName` | Consumes settler, creates city at tile. |
| `SetCityConstructionCommand` | `int CityId`, `string ConstructionName` | Sets building/unit production queue. |
| `PurchaseItemCommand` | `int CityId`, `string ItemName`, `Stat Currency` | Spends Gold/Faith to purchase unit or building. |
| `ChooseResearchCommand` | `string TechName` | Updates civilization's active research target. |
| `AdoptPolicyCommand` | `string PolicyName` | Adopts policy branch or individual policy using Culture. |
| `EndTurnCommand` | none | Signals turn completion for the active civilization. |

---

## 5. Event Hierarchy

All events implement `IGameEvent`:

```csharp
public interface IGameEvent
{
    Guid EventId { get; }
    int Turn { get; }
    string EventType { get; }
}
```

### Core Events

| Event | Payload | Client Action |
|---|---|---|
| `UnitMovedEvent` | `UnitId`, `From`, `To`, `MovementRemaining` | Play move animation, update fog of war. |
| `CombatResolvedEvent` | `AttackerId`, `DefenderId`, `DamageDealt`, `DamageReceived`, `WasKilled` | Play attack effect, update unit health bars. |
| `CityFoundedEvent` | `CityId`, `OwnerCiv`, `Location`, `CityName` | Spawn 3D city mesh and territory borders. |
| `ConstructionCompletedEvent` | `CityId`, `ItemName` | Show notification, trigger completion sound. |
| `TurnStartedEvent` | `PlayerCiv`, `Turn` | Reset movement, evaluate turn start yield logic. |

---

## 6. Determinism, Replays, and Save States

Because command execution relies exclusively on seeded `GameRandom`, a complete match can be archived, transmitted, or reproduced from:

1. Initial Game Parameters (seed, map size, ruleset, civilizations).
2. The ordered log of `ICommand` entries.

Replaying the log guarantees 100% bit-exact reproduction of the world state, enabling instant desync detection, replay scrubbing, and regression testing.
