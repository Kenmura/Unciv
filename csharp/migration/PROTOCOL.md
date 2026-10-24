# Kotlin → C# Migration Protocol

Any agent (Claude, Antigravity, or other) that is told **"Begin Conversion work"**
must follow this file exactly. No live agent-to-agent channel exists: git and
`backlog.md` are the only coordination mechanism.

## Trigger phrase

> **Begin Conversion work**

On seeing it, read this file, then `csharp/migration/backlog.md`, then run the
Work Loop below. Do one ticket per invocation unless the user says to continue.

## Layout

- `csharp/Unciv.Core/` – headless engine (no UI, no libGDX). Namespaces mirror Kotlin packages (`com.unciv.logic.map` → `Unciv.Logic.Map`).
- `csharp/Unciv.Core.Tests/` – xUnit tests, one test class per ported Kotlin test class.
- `csharp/migration/backlog.md` – the ticket ledger.
- Kotlin sources under `core/` and `tests/` are the **source of truth** and are never modified by this effort.

## Environment

All dotnet commands run in the distrobox container `dotnet-dev`:

```
distrobox enter dotnet-dev -- bash -c 'cd /var/home/nickmarc/repos/Unciv/csharp && <command>'
```

Gate commands in the backlog are written as the inner `<command>` only. The repo
path is the same inside the container (home is shared).

## States

`Available` → `In-Progress [Agent]` → `Needs-Review [Implementer]` → `In-Review [Reviewer]` → `Done`
A review may send a ticket back to `Available` (with notes, keeping the implementer's branch work) or to `Blocked`.

## Work Loop

0. `git pull --rebase` (if a remote exists). Identify yourself by a stable name: `Claude` or `Antigravity`.
1. **Review first.** Find a `Needs-Review` ticket whose implementer is NOT you. If one exists, go to Review. Never review your own work.
2. Otherwise **implement.** Pick the first `Available` ticket whose `Depends` are all `Done`. If none, report that and stop.
3. **Claim** atomically: edit only that ticket's `State` to `In-Progress [You]` (or `In-Review [You]`), commit with message `migration: claim T### [You]`. If the commit conflicts with another agent's claim, drop yours and pick another.

### Context discipline

- One ticket per invocation keeps context small; do not roll straight into another ticket unless the user says to continue.
- Read only the files listed in the ticket. For anything else, `grep` instead of opening whole files.
- **Optional delegation:** if the ticket's Source is over ~500 lines or touches more than 3 files, and your harness supports subagents, delegate the *read-and-brief* step. The brief returns: files involved, semantic traps (numeric types, rounding, ordering, null handling), stripped or deferred members, and fixture cases to port.
- Never delegate the port itself, the claim/state commits, or the final Gate run. Run the Gate yourself.
- Subagent output is a brief, not evidence: verify any claim you rely on against the Kotlin source.

### Implement

1. Read every Source file listed in the ticket and its test fixtures (or use the subagent brief above, spot-checked).
2. Port tests first (xUnit, same cases, same names where possible), then the implementation. Preserve behaviour exactly, including quirks, integer/float semantics (Kotlin `Int` division, `Float` vs `Double`, rounding mode) and iteration order. Do not "improve" logic.
3. Replace libGDX/UI types with small engine-owned equivalents in `Unciv.Core` (e.g. `Vector2`) – never add UI dependencies. If a ticket needs a type owned by another ticket, stop and mark `Blocked` with a note.
4. Run the ticket's Gate command. It must exit 0, and the whole solution must still pass `dotnet test`.
5. Set `State` to `Needs-Review [You]`, fill `Notes` (deviations, assumptions, semantic traps), commit `migration: implement T### [You]`.

### Review

1. Diff the C# against the Kotlin line by line for behavioural fidelity: numeric types, rounding, null handling, ordering, equality/hash semantics, mutability, serialization names.
2. Check that ported tests cover every Kotlin test case and add cases for any untested branch you find.
3. Run the Gate and full `dotnet test` yourself. Do not trust the implementer's claim.
4. Pass → `Done`, commit `migration: review T### pass [You]`. Fail → set `Available`, append findings under `Review notes`, commit `migration: review T### changes-requested [You]`.

## Rules

- One ticket per commit series; commit only files belonging to that ticket plus `backlog.md`.
- Never mark `Done` without a passing Gate you ran in this session.
- New tickets discovered along the way are appended to the backlog as `Available` with correct `Depends`.
- Do not touch Gradle/Android build files.
- Generated golden values (for code without Kotlin tests) must come from running the Kotlin code; record how in `Notes`.

## Ticket format

```
### T### Title
- State: Available
- Depends: T###, T###  (or none)
- Source: Kotlin files
- Fixtures: Kotlin test files (or "none – generate golden values")
- Deliverables: C# files
- Gate: `dotnet test --filter FullyQualifiedName~Xxx`
- Notes:
```
