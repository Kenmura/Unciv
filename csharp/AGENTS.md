# C# engine rules for agents

Read `migration/PROTOCOL.md` first. This file lists what the compiler cannot check for you.
Everything the analyzers *can* check (banned APIs, naming, culture, unused code) fails the build, so run it.

## Before you finish a ticket
Run all three, inside `dotnet-dev`, from your own worktree's `csharp/`:

    dotnet build -warnaserror && dotnet format --verify-no-changes && dotnet test

If `dotnet format` complains, run `dotnet format` (no flag) and review the diff. It must touch only your files.
Review tickets using the multi-persona adversarial review skill (`.agents/skills/adversarial-review/SKILL.md`): apply **The Auditor**, **The Adversary**, and **The Pragmatist** lenses before passing.
When marking a ticket `Done` on passing review, publish and push progress to remote:

    git push origin csharp-migration

## Porting rules
- Port behaviour, not style. Do not "improve", simplify, or reorder the Kotlin logic; keep its quirks and note them in the ticket.
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
