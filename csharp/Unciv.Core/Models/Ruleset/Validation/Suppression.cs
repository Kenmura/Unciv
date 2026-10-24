using Unciv.Models.Ruleset.Unique;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.Models.Ruleset.Validation;

// Ported from core/src/com/unciv/models/ruleset/validation/Suppression.kt
// Deviations: isValidFilter and the doc constants live on UniqueParameterTypeExtensions (T025) and UniqueTypeData; this class delegates to them.
// autoSuppressAllWarnings (a debug tool writing ModOptions.json through the libGDX json writer) is not ported.

/// <summary>
///  All public methods dealing with how Mod authors can suppress RulesetValidator output.
///  Current decisions:
///  * You cannot suppress <see cref="RulesetErrorSeverity.Error"/> level messages.
///  * Each suppression entry is either compared verbatim or as primitive wildcard pattern with '*' on both ends, case-insensitive.
///  * Minimum selectivity of 12 characters matching.
/// </summary>
public static class Suppression
{
    /// <summary>Determine whether <paramref name="parameterText"/> is a valid Suppression filter as implemented by <see cref="IsErrorSuppressed"/></summary>
    public static bool IsValidFilter(string parameterText) => UniqueParameterTypeExtensions.IsValidSuppressionFilter(parameterText);

    private static bool MatchesFilter(RulesetError error, string filter)
    {
        if (error.Text == filter) return true;
        if (!filter.EndsWith('*') || !filter.StartsWith('*')) return false;
        // Kotlin: removeSurrounding("*", "*") only removes when the text is long enough to hold both
        string inner = filter.Length >= 2 ? filter[1..^1] : filter;
        return error.Text.Contains(inner, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Determine if <paramref name="error"/> matches any suppression Unique in ModOptions or the <paramref name="sourceObject"/>, or any suppression modifier in <paramref name="sourceUnique"/></summary>
    internal static bool IsErrorSuppressed(
        IReadOnlyCollection<string> globalSuppressionFilters,
        IHasUniques? sourceObject,
        UniqueObject? sourceUnique,
        RulesetError error)
    {
        if (error.ErrorSeverityToReport >= RulesetErrorSeverity.Error) return false;
        if (sourceObject == null && globalSuppressionFilters.Count == 0) return false;

        static string GetWildcardFilter(UniqueObject unique) => unique.Params[0].StartsWith('*') ? unique.Params[0] : "*" + unique.Params[0] + "*";

        // Allow suppressing from ModOptions
        IEnumerable<string> suppressions = globalSuppressionFilters;
        // Allow suppressing from suppression uniques in the same Unique collection
        if (sourceObject != null)
            suppressions = suppressions.Concat(sourceObject.GetMatchingUniques(UniqueType.SuppressWarnings, GameContext.IgnoreConditionalsState).Select(GetWildcardFilter));
        // Allow suppressing from modifiers in the same Unique
        if (sourceUnique != null)
            suppressions = suppressions.Concat(sourceUnique.GetModifiers(UniqueType.SuppressWarnings).Select(GetWildcardFilter));

        foreach (string filter in suppressions)
            if (MatchesFilter(error, filter)) return true;
        return false;
    }
}
