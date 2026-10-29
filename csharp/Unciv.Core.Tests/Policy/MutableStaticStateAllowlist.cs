namespace Unciv.Core.Tests.Policy;

// Allowlist for MutableStaticStateTests (T117). Key: "Namespace.Type.Member" (nested types use '+'; auto-property
// backing fields are reported by property name). Value: the JUSTIFY reason why the global state is safe.
internal static class MutableStaticStateAllowlist
{
    internal static readonly IReadOnlyDictionary<string, string> Entries = new Dictionary<string, string>
    {
        ["Unciv.Json.StatsSubclassConverter`1.Properties"] = "JUSTIFY: reflection metadata memo, filled once per generic instantiation.",
        ["Unciv.Logic.City.CityFocusExtensions.StatValuesCache"] = "JUSTIFY: memo of a pure function of enum values; cannot change results.",
        ["Unciv.Logic.Files.MapSaver.SaveZipped"] = "JUSTIFY: user preference toggled by the host before saving; a settings object will own it once UI/settings migrate.",
        ["Unciv.Logic.Files.UncivFiles.SaveZipped"] = "JUSTIFY: user preference toggled by the host before saving (Kotlin UncivFiles.saveZipped); a settings object will own it once UI/settings migrate.",
        ["Unciv.Models.Ruleset.RulesetCache.Cache"] = "JUSTIFY: process-wide ruleset cache keyed by name, populated at load.",
        ["Unciv.Models.Ruleset.RulesetCache.UniqueMisspellingThreshold"] = "JUSTIFY: host-supplied tuning value set at startup.",
        ["Unciv.Models.Ruleset.Ruleset.IsUniqueValid"] = "JUSTIFY: host-installed validation hook set once at startup.",
        ["Unciv.Models.Ruleset.Unique.Conditionals.CurrentSettings"] = "JUSTIFY: backing field of the host-installed Settings hook.",
        ["Unciv.Models.Ruleset.Unique.Conditionals.Settings"] = "JUSTIFY: host-installed settings hook set once at startup.",
        ["Unciv.Models.Ruleset.Unique.Countables.FilterToMatchingBuildingsCache"] = "JUSTIFY: thread-safe memo cache of a pure function of ruleset data; cannot change results.",
        ["Unciv.Models.Ruleset.Unique.Expressions.Expressions.Cache"] = "JUSTIFY: thread-safe memo cache of a pure parse function; cannot change results.",
        ["Unciv.Models.Ruleset.Unique.Expressions.Operator.Cache"] = "JUSTIFY: memo cache of a pure lookup populated on first use; cannot change results.",
        ["Unciv.Models.Ruleset.Unique.UniqueParameterTypeExtensions.CountablesProvider"] = "JUSTIFY: host-installed hook set once at startup.",
        ["Unciv.Models.Translations.FontRulesetIcons.RulesetObjectNameToChar"] = "JUSTIFY: host-populated glyph registry, written at ruleset load.",
        ["Unciv.Utils.TurnProbe.Current"] = "JUSTIFY: T261 thread-local measurement observer set by the bench; never read by game logic, state hashes are equal with and without it (BenchTests).",
        ["Unciv.Utils.TurnProbe.Observer"] = "JUSTIFY: T261 [ThreadStatic] backing field of TurnProbe.Current (per thread, so parallel tests do not see each other's observer).",
        ["Unciv.Models.Translations.TranslationCache.CachedLangData"] = "JUSTIFY: memo of parsed translation file, derived from immutable files.",
        ["Unciv.Models.Translations.Translations.Current"] = "JUSTIFY: host-installed current translations instance.",
        ["Unciv.UI.Screens.Civilopedia.FormattedLine.NameMapCacheInstance"] = "JUSTIFY: memo cache for civilopedia name lookup.",
        ["Unciv.Utils.Log.Backend"] = "JUSTIFY: process-wide logging configuration installed/changed by the host; isolated in non-parallel collection TranslationState.",
        ["Unciv.Utils.Log.DisableLogsFrom"] = "JUSTIFY: process-wide logging configuration installed/changed by the host; isolated in non-parallel collection TranslationState.",
        ["Unciv.Utils.Log.EnableLogsFrom"] = "JUSTIFY: process-wide logging configuration installed/changed by the host; isolated in non-parallel collection TranslationState.",
    };
}
