namespace Unciv.Models.Ruleset.Validation;

// Ported from core/src/com/unciv/models/ruleset/validation/ModCompatibility.kt: only modNameFilter, which Conditionals needs.
// The rest (meetsBaseRequirements, meetsAllRequirements) needs Ruleset and RulesetCache and is ported with them.
public static class ModCompatibility
{
    /// <summary>A mod name matches a filter when equal, or when the filter is <c>*partial*</c> and the lower-cased name contains it.</summary>
    public static bool ModNameFilter(string modName, string filter)
    {
        if (modName == filter) return true;
        if (filter.Length < 3 || !filter.StartsWith('*') || !filter.EndsWith('*')) return false;
        string partialName = filter.Substring(1, filter.Length - 2).ToLowerInvariant();
        return modName.ToLowerInvariant().Contains(partialName, StringComparison.Ordinal);
    }
}
