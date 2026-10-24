using Unciv.Models.StatTypes;

namespace Unciv.Models.Ruleset;

// core/src/com/unciv/models/ruleset/RulesetName.kt

/// <summary>
/// A ruleset name entry for checks that need to reason about translation keys across ruleset sources.
///
/// The same raw <see cref="Name"/> can be valid in more than one place, but still share a translation key.
/// <see cref="Source"/> identifies the object type or field that contributed it, and <see cref="OriginRuleset"/> helps
/// validation distinguish built-in reuse from modded name collisions.
/// </summary>
public sealed record RulesetName(string Name, string Source, string OriginRuleset);

public static class RulesetNameExtensions
{
    /// <summary>Kotlin <c>INamed.toRulesetName</c>. the source is the Kotlin <c>this::class.simpleName</c>.</summary>
    internal static RulesetName ToRulesetName(this INamed item, Ruleset ruleset, Func<Ruleset, IEnumerable<INamed>>? getINamed = null) =>
        new(
            item.Name,
            item.GetType().Name,
            (item as IRulesetObject)?.OriginRuleset is { Length: > 0 } origin ? origin : FindOrigin(item, ruleset, getINamed));

    /// <summary>Kotlin <c>Ruleset.rulesetName</c>; the default <paramref name="originRuleset"/> is the ruleset's own name.</summary>
    internal static RulesetName RulesetName(this Ruleset ruleset, string name, string source, string? originRuleset = null) =>
        new(name, source, originRuleset ?? ruleset.Name);

    private static string FindOrigin(INamed item, Ruleset ruleset, Func<Ruleset, IEnumerable<INamed>>? getINamed)
    {
        if (getINamed == null || ruleset.Name.Length != 0)
            return ruleset.Name;
        var componentRulesets = ruleset.Mods.Select(RulesetCache.Get).OfType<Ruleset>().ToList();
        for (int i = componentRulesets.Count - 1; i >= 0; i--)
        {
            var componentRuleset = componentRulesets[i];
            if (getINamed(componentRuleset).Any(it => it.Name == item.Name))
                return componentRuleset.Name;
        }
        return "";
    }
}
