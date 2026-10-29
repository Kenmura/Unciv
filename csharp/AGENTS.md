# C# engine rules for agents

Read `migration/PROTOCOL.md` first. This file lists what the compiler cannot check for you.
Everything the analyzers *can* check (banned APIs, naming, culture, unused code) fails the build, so run it.

## Before you finish a ticket
From your own worktree's `csharp/` (under `.worktrees/<you>/csharp/`) run the gate. It enters `dotnet-dev` itself and does, in order:
`dotnet build -warnaserror`, `dotnet format --verify-no-changes` (your changed files; everything if a style/project file changed), the whole
test suite split across processes and instrumented for coverage in ONE pass, then the coverage ratchet:

    python3 -I migration/tools/gate.py

While developing use the dev loop instead (build, format your files, only the test classes that mention the types you changed, about 30 s):

    python3 -I migration/tools/gate.py --quick

`--quick` is an approximation and never a substitute for the gate. `python3 -I migration/tools/ptest.py --class FooTests` runs named classes only.

If `dotnet format` complains, run `dotnet format` (no flag) and review the diff. It must touch only your files.
Review tickets using the multi-persona adversarial review skill (`.agents/skills/adversarial-review/SKILL.md`): apply **The Auditor**, **The Adversary**, and **The Pragmatist** lenses before passing.
When marking a ticket `Done` on passing review, publish and push progress to remote:

    git push origin csharp-migration

## Porting rules
- Port behaviour, not style. Default to the Kotlin logic and quirks; do not "improve" casually. Exact Kotlin parity is no longer a hard requirement (owner decision): an intentional change that improves the engine (determinism, save/load equality, correctness) is fine if it is deterministic, tested, and recorded in the ticket Notes as a deliberate deviation. Reviewers must not fail a ticket for deviating from Kotlin per se.
- Mirror Kotlin names (PascalCase for members, Kotlin-style camelCase private fields). Do not add public API the Kotlin did not have.
- `Int` is `int`, `Float` is `float`, `Double` is `double`. Never widen silently. Arithmetic wraps (unchecked), as in Kotlin.
- Kotlin `LinkedHashMap`/`List`/sorted collections are ordered: do not substitute `Dictionary`/`HashSet` where order can reach game state or output.
- Rounding: `roundToInt` is half-up, .NET `Math.Round` is half-even. Use a vetted helper, never the bare call.
- Randomness only from the seeded RNG (T010). Time only as an explicit input.

## When a rule blocks you
`BannedSymbols.txt` and `.editorconfig` are the policy. Do not edit them, and do not widen `NoWarn`, in a feature ticket.
- Need a banned behaviour? Add a small, tested helper in `Unciv.Core` that does it deterministically, and use that.
- A rule is genuinely wrong for your port? Suppress it at the narrowest scope with a comment within 3 lines above:
  `// JUSTIFY: T### why the Kotlin behaviour requires this`. `SourcePolicyTests` fails otherwise.
- A rule is wrong in general? Append a ticket to the backlog and mark yours `Blocked` with a note.

## Markers
Write `TODO(T014)`, never bare `TODO`/`HACK`/`FIXME`; the ticket must exist in the backlog.
