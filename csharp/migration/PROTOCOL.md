# Kotlin → C# Migration Protocol

<!-- toc -->
## Contents

- [Trigger phrase](#trigger-phrase)
  - [No-prompt rule](#no-prompt-rule)
- [Goals and non-goals](#goals-and-non-goals)
- [Layout](#layout)
- [Environment](#environment)
- [Worktrees and the shared branch](#worktrees-and-the-shared-branch)
- [States](#states)
- [Work Loop](#work-loop)
  - [Context discipline](#context-discipline)
  - [Implement](#implement)
  - [Review (Multi-Persona Adversarial Review)](#review-multi-persona-adversarial-review)
- [Running until budget is exhausted](#running-until-budget-is-exhausted)
  - [Stop conditions](#stop-conditions)
- [Rules](#rules)
- [Ticket format](#ticket-format)
<!-- /toc -->

Any agent (Claude, Antigravity, or other) that is told **"Begin Conversion work"**
must follow this file exactly. No live agent-to-agent channel exists: git and
the `backlog/` folder (one file per ticket) are the only coordination mechanism.

## Trigger phrase

> **Begin Conversion work**

On seeing it, read this file, then run the Work Loop below **continuously**: finish a
ticket, then immediately start the next one, until a Stop Condition is met.

**Read the protocol and backlog from the `csharp-migration` branch, not from whatever
is checked out**: after your worktree exists, everything below refers to your worktree's copy.
**Never read the backlog files directly** (see Context discipline). Use `tools/ticket.py`.

### No-prompt rule

Never ask the user a question and never stop for a confirmation. Where this file or
the situation is ambiguous, pick the conservative reading, note it in the ticket's `Notes`, and
continue. Concretely: do not use AskUserQuestion; keep file writes inside your worktree
(temporary files go in your harness scratchpad); resolve rebase conflicts yourself (in `backlog/T###.md`
take `csharp-migration`'s text and re-apply only your own ticket's lines); a dirty worktree you own is
yours to commit or discard, one you do not own is left alone. Only the Stop Conditions below end the loop.

## Goals and non-goals

Long-term goal: a headless, **deterministic** C# engine driving a Godot client with a Civ 5-style UI (see `backlog/README.md`, Client track), playable online with friends over our **own** networking. The port exists to serve that goal.

Non-goals (mark such tickets `Won't port`, do not port them; see the exhaustive file-by-file inventory and rationale in `backlog/README.md` under `## Non-goals (Won't port)`):
- Unciv's multiplayer stack (`logic/multiplayer`, apiv2, Dropbox sync, `server/`) and any dependency on the original implementers' servers.
- libGDX UI/screens, Android/desktop launchers, platform-specific code.

## Layout

- `csharp/Unciv.Core/` – headless engine (no UI, no libGDX). Namespaces mirror Kotlin packages (`com.unciv.logic.map` → `Unciv.Logic.Map`).
- `csharp/Unciv.Core.Tests/` – xUnit tests of the engine (Core, Harness, Assets), one test class per ported Kotlin test class.
- `csharp/Unciv.App.Tests/` – xUnit tests of the app-layer projects `Unciv.Cli`, `Unciv.Server` and `Unciv.Client.Net` (the only test project that references them). Put a new test there when it needs one of those three projects; everything else goes in `Unciv.Core.Tests`. Test namespaces are `Unciv.Core.Tests.*` in both.
- `csharp/migration/backlog/` – the ticket ledger: one file per ticket (`T001.md`, `C005.md`, `A003.md`; Done tickets stay), plus `README.md` with the non-goals inventory and the track introductions.
- Kotlin sources under `core/` and `tests/` are the **source of truth** and are never modified by this effort.

## Environment

All dotnet commands run in the distrobox container `dotnet-dev`, from **your own worktree** (see Worktrees):

```
distrobox enter dotnet-dev -- bash -c 'cd <your-worktree>/csharp && <command>'
```

Gate commands in the backlog are written as the inner `<command>` only.

**Standard checks (T013).** In addition to a ticket's own Gate, *every* implementation and review must run, and pass, the full set from the `csharp/` directory before a ticket leaves `In-Progress`/`In-Review`:

```
python3 -I tools/gate.py        # from csharp/migration; enters dotnet-dev itself
```

It is `dotnet build -warnaserror`, `dotnet format --verify-no-changes` (the files your branch changed; the whole solution when a `.editorconfig`, `Directory.*.props`, `*.csproj` or `BannedSymbols*.txt` changed), the full suite and the coverage ratchet. The suite (both test projects) runs through `tools/ptest.py`: the `Unciv.Core.Tests` classes are split into shards (`Unciv.App.Tests` runs as one more process); a change confined to Cli/Server/Client.Net no longer rebuilds or reruns the engine tests, because `Unciv.Core.Tests` does not reference those projects. The classes are split into shards balanced by `test-timings.json` and each shard is its own `dotnet test` process (serial inside, because ~190 classes share the static `RulesetCache`, so xunit cannot parallelise them in-process; a final "remainder" shard runs any class without a timing, so a new class can never be skipped). The same pass is instrumented (coverlet `SingleHit`, one private copy of the output folder per shard because coverlet rewrites assemblies in place) and stores the full coverage report, so `tools/coverage.py --check` right after it is free. Expect about 2 minutes on 20 cores instead of the old ~3.5 minutes of tests plus a second instrumented run.

**Dev loop.** `python3 -I tools/gate.py --quick` builds, formats your changed files and runs only the test classes that mention a type declared in a changed `.cs` file (or are named `<File>Tests`); `tools/ptest.py --class NAME...` runs named classes. Both are approximations (one level of references; a changed non-`.cs` file selects everything) and never replace the gate. `tools/ptest.py --no-build` alone runs the whole suite sharded without coverage. `ptest.py` records class timings in the git common dir after every passing run; refresh the committed seed `test-timings.json` occasionally by copying `$(git rev-parse --git-common-dir)/unciv-ptest-timings.json` over it.

**Coverage ratchet (T122).** After the gate's single pass the report is cached under the git common dir (`unciv-cache/`), keyed by a hash of the contents of every source/project/test-data file, so it is shared by all worktrees and survives a rebase that changes none of them. `python3 -I tools/coverage.py --check` (from `csharp/migration/`) compares the `.cs` files your branch changed with `coverage-baseline.json`: with no cached report it first instruments only the affected test classes and, since a subset can only under-report, accepts when every changed file already meets its baseline there, otherwise it escalates to the full run (`--full` skips the shortcut; `--report` and `--update-baseline` always use the full report). It fails when line coverage fell below `coverage-baseline.json`, or when a new file is under 80% (add tests, or excuse it in the baseline's `excused` map with a reason naming a ticket). Implementers: after the check passes run `python3 -I tools/coverage.py --update-baseline` and commit the baseline change (it only ever raises or adds entries for your files). Reviewers (your rebased branch already contains the implementation, so name its parent): run `python3 -I tools/coverage.py --report --since <implementation-commit>^` and list the touched files with their uncovered line ranges in the review; an uncovered ported branch is a finding (demand a test or a ticket). `--all` checks every file.

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
  The push is fast-forward only. If it is rejected, the other agent published first: `git rebase csharp-migration`, re-read your ticket (`ticket.py show`), re-check your ticket choice still holds, and publish again.
- **Push to Remote on Review Pass:**
  Whenever a ticket passes review and is marked `Done`, push the integration branch to `origin`:
  ```
  git push origin csharp-migration
  ```
  This ensures `origin/csharp-migration` on GitHub is always kept up to date with verified progress.
- **A claim only counts once it is published.** Publish the claim commit before doing any real work on the ticket. If you lost the race, pick another ticket; do not keep working on the lost one.
- **Before each ticket and before each review:** `git rebase csharp-migration` so you see the other agent's published work. Reviewers review the implementer's *published* commits, never anyone's working files.
- Never push to `master`, never force-push or rewrite `csharp-migration`. Merging to `master` is the human's decision.

**Phase 4 (queued plans).** Tickets in phase 4 (`phases.txt`: UI redesign, unit models, Brave New World, feature-review items) are never offered by `ready`/`next` while any phase 1-3 ticket is open and not `Blocked`; when none is, they are offered automatically in the usual order. Do not hand-pick one earlier, and do not release or reorder them. Tickets in the B track default to phase 4; new C/T/A tickets for these plans must be added to the phase 4 line in `phases.txt`.

**Phase 5 (owner issue backlog).** Tickets converted from the owner's GitHub issues (Android, performance, ultra graphics, dedicated server, connection resiliency) are queued in phase 5 and follow the same rule one level up: never offered while any phase 1-4 ticket is open and not `Blocked`; released automatically afterwards. Do not hand-pick one earlier.

## States

`Available` → `In-Progress [Agent]` → `Needs-Review [Implementer]` → `In-Review [Reviewer]` → `Done`
A review may send a ticket back to `Available` (with notes, keeping the implementer's branch work) or to `Blocked`.

## Work Loop

0. Identify yourself by a stable name (`Claude` or `Antigravity`), ensure your worktree exists (see Worktrees), and `git rebase csharp-migration`.
1. **Review first:**
   - **Multi-Agent Mode:** Find a `Needs-Review` ticket whose implementer is NOT you. If one exists, go to Review.
   - **Single-Agent Mode (Budget for only 1 agent):** If running as the sole active agent, review your own completed `Needs-Review` ticket by dispatching an isolated **Adversarial Review Subagent** (applying `.agents/skills/adversarial-review/SKILL.md` with Auditor, Adversary, and Pragmatist personas). If the subagent approves, mark `Done`, commit `migration: review T### pass [Adversarial Review]`, and push to remote.
2. Otherwise **implement.** Pick what `python3 -I tools/ticket.py next` / `ready` offers (never hand-pick): it orders by delivery phase from `phases.txt` (1 CLI/engine, then 2 single player, then 3 multiplayer): take the first entry; later phases are reached only when earlier ones have nothing ready, so you may work on phase 2/3 items while phase 1 is unfinished. If none, report that and stop.
3. **Claim** with `python3 -I tools/ticket.py claim T### <You>`: it sets `In-Progress [You]` and `Claimed:` (UTC), commits as `migration: claim T### [You] <UTC timestamp>` and publishes immediately. Exit code 2 means the ticket was taken or has unmet dependencies: pick another. For a review, use `ticket.py state T### 'In-Review <You>' --publish`.

### Context discipline

- **Backlog access: only through `tools/ticket.py`; never `Read`, `grep -n`, `sed` or hand-edit the files in `backlog/`.** Run `python3 -I tools/ticket.py` from `csharp/migration/` (its docstring lists every command). Read: `ready`, `next <You>`, `show T### [Field]`, `list [State]`, `blockers T###`, `stale`, `check`. Write: `claim`, `state`, `reset`, `note`, `set`, `add`, `publish` (`archive` is a retired no-op). Every write regenerates `DEPENDENCY_MAP.md` (GitHub-rendered mermaid map of all incomplete tickets and their direct dependencies) in the same commit; `ticket.py graph` rebuilds it on demand. Every write touches one ticket's block only and is verified before the file is written; illegal state transitions, unknown dependency ids and dependency cycles are refused. Long Notes go in with `ticket.py note T### - <<'EOF' ... EOF`. If you need something the tool cannot do, add it to the tool in your own ticket's commit rather than hand-editing the backlog.
- **Parallel subagents.** When `python3 -I tools/ticket.py ready` lists several tickets, the orchestrator dispatches one subagent per ticket **in parallel** (one message, several Agent calls). Rules:
  - **One worktree per subagent**: `git -C /var/home/nickmarc/repos/Unciv worktree add .worktrees/<you>-T### -b agent/<you>-T### csharp-migration` (remove it after the ticket is Done and published). Never let two subagents share a working directory or branch; concurrent builds corrupt `bin/`/`obj/`.
  - Only dispatch tickets that are independent: no dependency between them (`ready` already guarantees deps are Done) and no overlap in Deliverables or in shared files (`IHasUniques.cs`, `*Seams.cs`, `Utils/*Extensions.cs`, `.csproj`). If two tickets would extend the same seam file, run them one after the other.
  - **Claim with the tool**: `python3 -I tools/ticket.py claim T### <You>` sets State and Claimed on that ticket only, commits only that ticket's `backlog/T###.md` and `DEPENDENCY_MAP.md`, rebases and fast-forwards `csharp-migration`, and exits 2 if you lost the race or the ticket is not claimable (pick another).
  - **Publishing is serialised by git**: `ticket.py publish` (or `state ... --publish`) rebases onto `csharp-migration` and fast-forwards it, retrying on rejection; `--origin` also runs `git push origin csharp-migration` (use on Done; safe to repeat). A rebase conflict aborts and is reported (conflict resolution is per file: for each conflicted `backlog/T###.md` take `csharp-migration`'s file and re-apply only your own ticket's changes with the tool; `DEPENDENCY_MAP.md` alone is rebuilt automatically).
  - Several tickets in `Needs-Review` by the same implementer are reviewed in parallel too, each by its own isolated review subagent.
  - `ready`/`next` order IMPLEMENT candidates by `[unblocks N]`, the number of incomplete tickets that depend on the ticket directly or transitively: dispatch from the top (subject to the overlap rule). A `REVIEW` row whose implementer subagent is still running is already being reviewed; do not dispatch it again.
  - After `ticket.py add`, commit and `ticket.py publish` straight away (ids are allocated against the published branch, but two unpublished adds can still collide; `ticket.py check` flags duplicates, `ticket.py renumber` fixes them).
  - **Never publish a non-building state to `csharp-migration`.** Every published commit (claim, implement, review, or a mid-ticket publish) must pass `tools/gate.py` (build, format, tests) on the rebased tree. WIP stays on your own agent branch, never on the shared branch (a `wip` commit on the shared branch once broke every other agent's rebase). If you find the tip broken, tell the orchestrator and do not mark anything Done until the tip builds.
  - **Enforced by the tool (T163).** `ticket.py state T### Done` itself refuses unless the worktree is clean, HEAD contains `csharp-migration`, `tools/gate.py` (build, format, tests) and `coverage.py --check` pass (it runs them; `ticket.py green` runs and stamps the exact tree beforehand, and a matching stamp makes `Done` instant), and no `TODO(T###)` marker naming the ticket remains outside the backlog files. `Done` combined with `--all` is refused. The only escape hatch is `--no-green REASON`, for backlog-only/docs tickets whose Deliverables list no `.cs/.csproj/.props/.editorconfig/.json/.py/.txt/.sln/.targets` file; the reason is recorded in Notes. Tool tests: `python3 -I -m unittest discover -s tools -p 'test_*.py'` (run when you change `tools/ticket.py`).
  - **Green before Done (no exceptions).** Marking a ticket `Done` (and any publish that follows) requires, on the tree rebased onto the *current* `csharp-migration` tip: `tools/gate.py` (build, format, tests) with exit code 0 and **0 failed** (read the exit code and the failed count; never chain the publish after the tests with `;`), and its coverage ratchet (`tools/coverage.py --check`, already done by `gate.py`). Before publishing run `git diff --stat <previous tip>..HEAD` and confirm it contains only your ticket's files plus your ticket's `backlog/T###.md` and `DEPENDENCY_MAP.md`: a rebase or conflict resolution that reverts another agent's files is a bug. Marking your ticket Done also invalidates every `TODO(T###)` marker and seam-file owner that names it: grep the whole tree (including `*Seams.cs` headers) and retarget them to open tickets in the same change. `wip` commits stay on your own branch. If `coverage.py --update-baseline` (raise/add only) is refused by the permission classifier, report it in the ticket Notes; the orchestrator runs it.
  - **Releases.** Every `state T### Done` is followed by `tools/release.py`, which builds the CLI (`dotnet publish Unciv.Cli`) and the Linux, Windows and macOS clients (`Unciv.Godot/tools/package.sh linux|windows|macos`, T258; `--linux-only` skips the last two; only Linux is ever run, the other two are file-presence checked and UNVERIFIED, see `GODOT.md`) from the Done tree and copies them to `csharp-releases/<ticket>-<sha>/{cli,client-linux,client-windows,client-macos}` in the **main checkout**, next to `csharp/`, with a `latest` link, a `manifest.json` and `releases.log` (newest 5 kept; the client export is retried up to twice after a Godot segfault, a known intermittent crash in its threaded font import; `latest` only moves forward; an already-released tree is not rebuilt). The folder is git-ignored: **never commit binaries**. A failed release only warns (the ticket stays Done): fix the cause and run `python3 -I tools/release.py T###`; `--no-release` skips it.
  - **Never wait with a self-matching `pgrep -f`.** A background loop such as `until ! pgrep -f "tools/coverage.py"; do sleep 5; done` matches its own shell (the pattern is in that shell's command line), so it never ends and leaves the agent and the session with a stale shell for hours. Wait on the process you started (`wait $pid`, or poll its output file/exit marker), or write the pattern as `"[c]overage.py"`. Before returning, make sure none of your background shells are left running.
  - **Never hold with a long `sleep`.** Do not start a background `sleep 900` (or longer) just to keep yourself alive or to "check back later": each one is a stale task in the user's list (a run once left 15 of them), and it can delay your report by the whole sleep. Wait only on a specific condition: a PID (`while kill -0 $pid; do sleep 5; done`, or `wait $pid`), a specific CI run (`gh run watch <run-id>`, or poll `gh run view <run-id> --json status,conclusion` with sleeps of 30 s at most), or a Monitor with an `until` loop on that condition. If nothing you started is still running, do not wait: finish and return.
  - The orchestrator keeps at most **4** subagents running at once (CPU/RAM: each runs a full `dotnet build/test`), refills the slot as each returns, and after each return re-runs `ticket.py ready`.
  - **Usage gate (orchestrator, before EVERY dispatch or refill).** Run `python3 -I tools/usage_gate.py check` (from `csharp/migration`): exit 0 = clear, 1 = BLOCKED (5-hour or 7-day plan usage at 98%+), 2 = "Unable to retrieve" (treated as clear). When blocked, do not spawn; start a Monitor (load via ToolSearch `select:Monitor`) on `python3 -I tools/usage_gate.py wait` (persistent, silent while waiting, one line per state change; polls every 300 s, no model tokens spent) and resume refilling when it prints `CLEAR`. Already-running agents are left alone. The script only reads the token Claude Code keeps in `~/.claude/.credentials.json`, never refreshes it, never sends it anywhere but api.anthropic.com. `status` prints the current numbers; `--strict` treats unreadable usage as blocked.
  - A ticket's Done-publish may land while another subagent works: before its own review/Done publish each subagent rebases and re-runs the Standard checks if `csharp-migration` moved in files it touches.
- **One ticket = one fresh subagent** when the harness has subagents: the orchestrator runs `ticket.py next`, then dispatches a subagent with the ticket ID, agent name and this file's path; the subagent does claim → implement/review → Gate → publish and returns a **3-line report** (ticket, outcome, commit). The orchestrator then loops. The orchestrator itself never reads Kotlin or C# sources, so its context stays flat. Without subagents, rely on auto-compaction.
- Run the Standard checks with output trimmed: `... 2>&1 | tail -40`, and read the full log only on failure.
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
- **The Auditor (Parity & Rules):** Diff C# against Kotlin line by line for exact behavioral parity (numeric wrapping, half-up rounding, nullability, collections). Run Standard checks (`tools/gate.py` (build, format, tests)). Enforce `BannedSymbols.txt` and determinism (seeded RNG, invariant culture, no unordered map iteration feeding state).
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
5. **UI tickets (client track):** a ticket that adds or changes a screen/feature the player sees must have an entry in `screenshots.json` (1-3 goldens with captions, or `{"none": reason}`) and a regenerated `SCREENSHOTS.md` (`python3 -I tools/screenshots.py`; `--check` must pass). Reviewers open the referenced PNGs and confirm the captions are true.
6. Confirm the diff adds no new `#pragma warning disable`, `SuppressMessage`, `NoWarn` or `.editorconfig`/`BannedSymbols*.txt` change unless the ticket is a policy ticket; any suppression needs a `JUSTIFY: T###` comment that you agree with.
7. Pass → `ticket.py state T### Done -m 'migration: review T### pass [You]' --publish --origin` (publishes and pushes to `origin`). Fail → `ticket.py note T### -` with the findings under `Review notes`, then `ticket.py state T### Available -m 'migration: review T### changes-requested [You]' --publish`.

## Running until budget is exhausted

Agents may be cut off at any moment by a usage limit, so the repo must always be recoverable:

- Commit and publish after every state change (claim, implement, review). Never hold uncommitted work across tickets.
- **Stale claims:** an `In-Progress` or `In-Review` ticket whose `Claimed:` timestamp is more than 3 hours old with no newer commit touching its deliverables is abandoned. `ticket.py stale` lists them; any agent may reset one with `ticket.py reset T### -m "stale"` (then `publish`) and keep the partial files for the next implementer to build on or discard.
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

- One ticket per commit series; commit only files belonging to that ticket plus its `backlog/T###.md`.
- Markdown docs carry a generated table of contents (`<!-- toc -->` block under the title). After adding or renaming a `##`/`###` heading run `python3 -I tools/toc.py` (CI's tools unit tests fail on a stale one); never edit the block by hand. `SCREENSHOTS.md` and `DEPENDENCY_MAP.md` get theirs from their generators.
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
