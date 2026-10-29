# bnw-spec

Implementation spec for adding **Civilization V: Brave New World** (BNW) rules to the C# engine, reverse-engineered
from the shipped game (Steam install) and written for the `csharp-migration` port. Start with [SPEC.md](SPEC.md).

Evidence tags used throughout: **[O]** observed in the decompiled BNW engine, **[D]** shipped game data,
**[U]** shipped UI Lua, **[K]** known BNW rule not re-verified in the binary (verify before porting),
**[I]** inferred, **[?]** unknown, **[X]** a design decision for this port.

- `SPEC.md`: the spec (rules, formulas, engine design, ticket plan, unknowns)
- `data/*.tsv`: the merged BNW gameplay database as TSV (180 tables, numbers and identifiers only). This is the
  input for the ruleset generator (SPEC §3).
- `data/derived/`: diffs. `schema_gk_to_bnw.txt` lists every table and column BNW added. `defines_gk_to_bnw.tsv` lists
  new and changed tunables. `objects_unciv_gk_vs_bnw.txt` diffs objects by English name against Unciv's G&K JSON.
  `lua_bindings_bnw.tsv` maps the 969 Lua method names to engine functions.
- `tools/`: scripts to reproduce all of the above (see `tools/README.md`).

The decompiler output itself is derived from proprietary code and is **not** in this repo (same policy as
`../ai-spec`). No Firaxis prose, art, audio or fonts are included.
