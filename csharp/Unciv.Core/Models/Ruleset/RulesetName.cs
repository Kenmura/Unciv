namespace Unciv.Models.Ruleset;

// core/src/com/unciv/models/ruleset/RulesetName.kt
// Not ported yet: the INamed.toRulesetName and Ruleset.rulesetName helpers, which need Ruleset and RulesetCache (T034).

/// <summary>
/// A ruleset name entry for checks that need to reason about translation keys across ruleset sources.
///
/// The same raw <see cref="Name"/> can be valid in more than one place, but still share a translation key.
/// <see cref="Source"/> identifies the object type or field that contributed it, and <see cref="OriginRuleset"/> helps
/// validation distinguish built-in reuse from modded name collisions.
/// </summary>
public sealed record RulesetName(string Name, string Source, string OriginRuleset);
