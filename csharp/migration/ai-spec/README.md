# civ5-ai-spec

Behavioral requirements for Civilization V's AI, reverse-engineered from the shipped engines (base, G&K, BNW), written for a
programmer porting similar behavior (target: Unciv). Start with [SPEC.md](SPEC.md). Evidence tags: **[O]** observed in code,
**[D]** shipped data, **[I]** inferred, **[?]** unknown.

- `SPEC.md` — the spec
- `data/` — shipped AI tables/tunables as TSV (seed for a ruleset)
- `tools/` — how to reproduce the extraction
