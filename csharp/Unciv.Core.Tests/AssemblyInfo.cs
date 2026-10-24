// The engine's Log, Translations.Current and similar process-wide statics are shared by every test class (map generation,
// ruleset loading etc. all log), so per-collection serialization cannot make LogTests deterministic. The whole suite runs in seconds.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
