# Kotlin → C# Migration Protocol

Any agent (Claude, Antigravity, or other) that is told **"Begin Conversion work"**
must follow this file exactly. No live agent-to-agent channel exists: git and
`backlog.md` are the only coordination mechanism.

## Trigger phrase

> **Begin Conversion work**

On seeing it, read this file, then run the Work Loop below **continuously**: finish a
ticket, then immediately start the next one, until a Stop Condition is met.

**Read the protocol and backlog from the `csharp-migration` branch, not from whatever
is checked out**: after your worktree exists, everything below refers to your worktree's copy.
**Never read `backlog.md` whole** (see Context discipline). Use `tools/ticket.py`.

### No-prompt rule

Never ask the user a question and never stop for a confirmation. Where this file or
the situation is ambiguous, pick the conservative reading, note it in the ticket's `Notes`, and
continue. Concretely: do not use AskUserQuestion; keep file writes inside your worktree
(temporary files go in your harness scratchpad); resolve rebase conflicts yourself (in `backlog.md`
take `csharp-migration`'s text and re-apply only your own ticket's lines); a dirty worktree you own is
yours to commit or discard, one you do not own is left alone. Only the Stop Conditions below end the loop.

## Goals and non-goals

Long-term goal: a headless, **deterministic** C# engine driving a Godot client with a Civ 5-style UI (see `backlog.md`, Client track), playable online with friends over our **own** networking. The port exists to serve that goal.

Non-goals (mark such tickets `Won't port`, do not port them; see the exhaustive file-by-file inventory and rationale in `backlog.md` under `## Non-goals (Won't port)`):
- Unciv's multiplayer stack (`logic/multiplayer`, apiv2, Dropbox sync, `server/`) and any dependency on the original implementers' servers.
- libGDX UI/screens, Android/desktop launchers, platform-specific code.

## Layout

- `csharp/Unciv.Core/` – headless engine (no UI, no libGDX). Namespaces mirror Kotlin packages (`com.unciv.logic.map` → `Unciv.Logic.Map`).
- `csharp/Unciv.Core.Tests/` – xUnit tests, one test class per ported Kotlin test class.
- `csharp/migration/backlog.md` – the ticket ledger.
- Kotlin sources under `core/` and `tests/` are the **source of truth** and are never modified by this effort.

## Environment

All dotnet commands run in the distrobox container `dotnet-dev`, from **your own worktree** (see Worktrees):

```
distrobox enter dotnet-dev -- bash -c 'cd <your-worktree>/csharp && <command>'
```

Gate commands in the backlog are written as the inner `<command>` only.

**Standard checks (T013).** In addition to a ticket's own Gate, *every* implementation and review must run, and pass, the full set from the `csharp/` directory before a ticket leaves `In-Progress`/`In-Review`:

```
dotnet build -warnaserror && dotnet format --verify-no-changes && dotnet test
```

The build enforces `Directory.Build.props`, `.editorconfig` and `BannedSymbols.txt`. See `csharp/AGENTS.md` for the rules agents should follow and how to handle a blocking rule. Home is shared with the container, so worktree paths are identical inside it.

## Worktrees and the shared branch

Never work in the main checkout (`/var/home/nickmarc/repos/Unciv`); it belongs to the human. Agents (and parallel subagents) never share a working directory: concurrent builds corrupt `bin/`/`obj/`, and checkouts move files under each other.

- **Integration branch:** `csharp-migration`. It is the shared board and is never checked out anywhere. If it doesn't exist, create it from `master` (`git branch csharp-migration master`).
- **Your worktree (first run only):**
  Create your worktree inside the repository under `.worktrees/` (which is git-ignored and stays within workspace boundaries to prevent terminal sandbox permission prompts):
  ```bash
  git -C /var/home/nickmarc/repos/Unciv worktree add .worktrees/<you> -b agent/<you> csharp-migration
  ```
  If `.worktrees/<you>` already exists, reuse it. Never use sibling `../Unciv-wt-*` directories: if one exists for your branch, `git worktree remove` it (when clean) and re-add it under `.worktrees/`. Work only there, on branch `agent/<you>`.
- **Publish** (do this after *every* state change: claim, implement, review, reset, backlog expansion):
  ```
  git rebase csharp-migration && git push . HEAD:csharp-migration
  ```
  The push is fast-forward only. If it is rejected, the other agent published first: `git rebase csharp-migration`, re-read `backlog.md`, re-check your ticket choice still holds, and publish again.
- **Push to Remote on Review Pass:**
  Whenever a ticket passes review and is marked `Done`, push the integration branch to `origin`:
  ```
  git push origin csharp-migration
  ```
  This ensures `origin/csharp-migration` on GitHub is always kept up to date with verified progress.
- **A claim only counts once it is published.** Publish the claim commit before doing any real work on the ticket. If you lost the race, pick another ticket; do not keep working on the lost one.
- **Before each ticket and before each review:** `git rebase csharp-migration` so you see the other agent's published work. Reviewers review the implementer's *published* commits, never anyone's working files.
- Never push to `master`, never force-push or rewrite `csharp-migration`. Merging to `master` is the human's decision.

## States

`Available` → `In-Progress [Agent]` → `Needs-Review [Implementer]` → `In-Review [Reviewer]` → `Done`
A review may send a ticket back to `Available` (with notes, keeping the implementer's branch work) or to `Blocked`.

## Work Loop

0. Identify yourself by a stable name (`Claude` or `Antigravity`), ensure your worktree exists (see Worktrees), and `git rebase csharp-migration`.
1. **Review first:**
   - **Multi-Agent Mode:** Find a `Needs-Review` ticket whose implementer is NOT you. If one exists, go to Review.
   - **Single-Agent Mode (Budget for only 1 agent):** If running as the sole active agent, review your own completed `Needs-Review` ticket by dispatching an isolated **Adversarial Review Subagent** (applying `.agents/skills/adversarial-review/SKILL.md` with Auditor, Adversary, and Pragmatist personas). If the subagent approves, mark `Done`, commit `migration: review T### pass [Adversarial Review]`, and push to remote.
2. Otherwise **implement.** Pick the first `Available` ticket whose `Depends` are all `Done`. If none, report that and stop.
3. **Claim** with `python3 -I tools/ticket.py claim T### <You>`: it sets `In-Progress [You]` and `Claimed:` (UTC), commits as `migration: claim T### [You] <UTC timestamp>` and publishes immediately. Exit code 2 means the ticket was taken or has unmet dependencies: pick another. For a review, use `ticket.py state T### 'In-Review <You>' --publish`.

### Context discipline

- **Backlog access: only through `tools/ticket.py`; never `Read`, `grep -n`, `sed` or hand-edit `backlog.md`/`backlog-archive.md`.** Run `python3 -I tools/ticket.py` from `csharp/migration/` (its docstring lists every command). Read: `ready`, `next <You>`, `show T### [Field]`, `list [State]`, `blockers T###`, `stale`, `check`. Write: `claim`, `state`, `reset`, `note`, `set`, `add`, `publish`, `archive`. Every write touches one ticket's block only and is verified before the file is written; illegal state transitions, unknown dependency ids and dependency cycles are refused. Long Notes go in with `ticket.py note T### - <<'EOF' ... EOF`. If you need something the tool cannot do, add it to the tool in your own ticket's commit rather than hand-editing the backlog.
- **Parallel subagents.** When `python3 -I tools/ticket.py ready` lists several tickets, the orchestrator dispatches one subagent per ticket **in parallel** (one message, several Agent calls). Rules:
  - **One worktree per subagent**: `git -C /var/home/nickmarc/repos/Unciv worktree add .worktrees/<you>-T### -b agent/<you>-T### csharp-migration` (remove it after the ticket is Done and published). Never let two subagents share a working directory or branch; concurrent builds corrupt `bin/`/`obj/`.
  - Only dispatch tickets that are independent: no dependency between them (`ready` already guarantees deps are Done) and no overlap in Deliverables or in shared files (`IHasUniques.cs`, `*Seams.cs`, `Utils/*Extensions.cs`, `.csproj`). If two tickets would extend the same seam file, run them one after the other.
  - **Claim with the tool**: `python3 -I tools/ticket.py claim T### <You>` sets State and Claimed on that ticket only, commits only `backlog.md`, rebases and fast-forwards `csharp-migration`, and exits 2 if you lost the race or the ticket is not claimable (pick another).
  - **Publishing is serialised by git**: `ticket.py publish` (or `state ... --publish`) rebases onto `csharp-migration` and fast-forwards it, retrying on rejection; `--origin` also runs `git push origin csharp-migration` (use on Done; safe to repeat). A rebase conflict aborts and is reported (backlog.md: take `csharp-migration`'s text and re-apply only your own ticket's changes with the tool).
  - Several tickets in `Needs-Review` by the same implementer are reviewed in parallel too, each by its own isolated review subagent.
  - The orchestrator keeps at most **4** subagents running at once (CPU/RAM: each runs a full `dotnet build/test`), refills the slot as each returns, and after each return re-runs `ticket.py ready`.
  - A ticket's Done-publish may land while another subagent works: before its own review/Done publish each subagent rebases and re-runs the Standard checks if `csharp-migration` moved in files it touches.
- **One ticket = one fresh subagent** when the harness has subagents: the orchestrator runs `ticket.py next`, then dispatches a subagent with the ticket ID, agent name and this file's path; the subagent does claim → implement/review → Gate → publish and returns a **3-line report** (ticket, outcome, commit). The orchestrator then loops. The orchestrator itself never reads Kotlin or C# sources, so its context stays flat. Without subagents, rely on auto-compaction.
- Run the Standard checks with output trimmed: `... 2>&1 | tail -40`, and read the full log only on failure.
- Periodically run `python3 -I tools/ticket.py archive` (and commit) when many tickets are Done.
- Read only the files listed in the ticket. For anything else, `grep` instead of opening whole files.
- **Optional delegation:** if the ticket's Source is over ~500 lines or touches more than 3 files, and your harness supports subagents, delegate the *read-and-brief* step. The brief returns: files involved, semantic traps (numeric types, rounding, ordering, null handling), stripped or deferred members, and fixture cases to port.
- Never delegate the port itself, the claim/state commits, or the final Gate run. Run the Gate yourself.
- Subagent output is a brief, not evidence: verify any claim you rely on against the Kotlin source.

### Implement

1. Read every Source file listed in the ticket and its test fixtures (or use the subagent brief above, spot-checked).
2. Port tests first (xUnit, same cases, same names where possible), then the implementation. Preserve behaviour exactly, including quirks, integer/float semantics (Kotlin `Int` division, `Float` vs `Double`, rounding mode) and iteration order. Do not "improve" logic.
3. Replace libGDX/UI types with small engine-owned equivalents in `Unciv.Core` (e.g. `Vector2`) – never add UI dependencies. If a ticket needs a type owned by another ticket, stop and mark `Blocked` with a note.
4. Run the ticket's Gate command, then the Standard checks (see Environment). Both must exit 0.
5. Record `Notes` (deviations, assumptions, semantic traps) with `ticket.py note T### -`, then `ticket.py state T### 'Needs-Review <You>' --all -m 'migration: implement T### [You]' --publish` (one commit with your deliverables and the backlog change, published). (In Single-Agent Mode: Immediately proceed to Review via an isolated Adversarial Review subagent rather than waiting for an external agent).

### Review (Multi-Persona Adversarial Review)

All reviews must apply the multi-persona adversarial review skill ([`csharp/.agents/skills/adversarial-review/SKILL.md`](../.agents/skills/adversarial-review/SKILL.md)). A reviewer (or an isolated reviewer subagent) evaluates the changes through three distinct lenses:
- **The Auditor (Parity & Rules):** Diff C# against Kotlin line by line for exact behavioral parity (numeric wrapping, half-up rounding, nullability, collections). Run Standard checks (`dotnet build -warnaserror && dotnet format --verify-no-changes && dotnet test`). Enforce `BannedSymbols.txt` and determinism (seeded RNG, invariant culture, no unordered map iteration feeding state).
- **The Adversary (Hostile Edge Cases):** Actively attempt to break the implementation with boundary inputs (`NaN`, extreme coordinates, empty maps, invalid states). Demand or write unit tests covering failure modes.
- **The Pragmatist (Simplicity & Maintainability):** Eliminate premature abstractions and unnecessary architectural bloat (YAGNI). Ensure clean integration and maintainable tests.

1. Diff the C# against the Kotlin line by line for behavioural fidelity: numeric types, rounding, null handling, ordering, equality/hash semantics, mutability, serialization names.
2. **Determinism checklist** – confirm each of these, or reject:
   - Randomness goes only through the engine's seeded RNG (T010), never `System.Random` or time.
   - No hash-ordered iteration (`Dictionary`/`HashSet`) feeds game state or output where Kotlin used insertion-ordered or sorted collections.
   - Float/Double/Int widths and rounding match Kotlin; no silent `float`→`double` promotion; Kotlin `Int` overflow and division semantics preserved.
   - No wall-clock, culture-sensitive formatting/parsing, or threading nondeterminism in engine logic.
3. Check that ported tests cover every Kotlin test case and add cases for any untested branch you find.
4. Run the Gate and the Standard checks yourself. Do not trust the implementer's claim.
5. Confirm the diff adds no new `#pragma warning disable`, `SuppressMessage`, `NoWarn` or `.editorconfig`/`BannedSymbols*.txt` change unless the ticket is a policy ticket; any suppression needs a `JUSTIFY: T###` comment that you agree with.
6. Pass → `ticket.py state T### Done -m 'migration: review T### pass [You]' --publish --origin` (publishes and pushes to `origin`). Fail → `ticket.py note T### -` with the findings under `Review notes`, then `ticket.py state T### Available -m 'migration: review T### changes-requested [You]' --publish`.

## Running until budget is exhausted

Agents may be cut off at any moment by a usage limit, so the repo must always be recoverable:

- Commit and publish after every state change (claim, implement, review). Never hold uncommitted work across tickets.
- **Stale claims:** an `In-Progress` or `In-Review` ticket whose `Claimed:` timestamp is more than 3 hours old with no newer commit touching its deliverables is abandoned. Any agent may reset it to `Available` (commit `migration: reset stale T###`) and keep the partial files for the next implementer to build on or discard.
- If a usage limit is hit mid-ticket, do not mark anything `Done`. Stop; the stale-claim rule handles the rest.
- If a rate limit reports a reset time, wait for it and resume the loop. If the harness cannot wait, stop.

### Stop conditions

Stop and report only when one of these is true:
1. No `Needs-Review` ticket for you and no `Available` ticket with all dependencies `Done` (list what is blocked and on what).
2. Usage budget for the current period is exhausted and cannot be resumed.
3. The same ticket has failed its Gate or been sent back 3 times: mark it `Blocked` with notes, then continue with other tickets.
4. The environment is broken (`dotnet-dev` unavailable, build of the untouched solution fails). Report instead of working around it.

When the backlog runs low, an agent that finds nothing `Available` should first try to **expand it**: scan the Kotlin tree for the next leaf files whose dependencies are all `Done`, append tickets in the standard format, commit, publish, and continue.

## Rules

- One ticket per commit series; commit only files belonging to that ticket plus `backlog.md`.
- Never mark `Done` without a passing Gate you ran in this session.
- New tickets discovered along the way are appended to the backlog as `Available` with correct `Depends`.
- Do not touch Gradle/Android build files.
- Do not edit `Directory.Build.props`, `Directory.Packages.props`, `BannedSymbols*.txt` or `csharp/.editorconfig` except in a ticket whose purpose is policy.
- **Assets:** never copy Firaxis/Civ 5 art, icons, sounds, fonts or music. Record the source and license of every asset in `csharp/migration/ASSETS.md`. Unciv's own assets keep their original license; check before reuse.
- Ported files keep the original MPL-2.0 license notice (see repo `LICENSE`).
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
- Claimed:   (UTC timestamp, set when claimed)
- Notes:
```
