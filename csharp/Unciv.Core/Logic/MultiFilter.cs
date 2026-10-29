namespace Unciv.Logic;

/// <summary>
/// Implements <c>and</c> (<c>{filter1} {filter2}</c>) and <c>not</c> (<c>non-[filter]</c>) logic
/// on top of single-term filter predicates.
/// Ported from <c>core/src/com/unciv/logic/MultiFilter.kt</c>.
/// </summary>
public static class MultiFilter
{
    private const char AndPrefixChar = '{';
    private const string AndPrefix = "{";
    private const string AndSeparator = "} {";
    private const char AndSuffixChar = '}';
    private const string AndSuffix = "}";
    private const string NotPrefix = "non-[";
    private const char NotSuffixChar = ']';
    private const string NotSuffix = "]";

    public static bool Filter(
        string input,
        Func<string, bool> filterFunction,
        bool forUniqueValidityTests = false)
    {
        if (IsAnd(input))
        {
            return GetAndFilters(input)
                .All(it => Filter(it, filterFunction, forUniqueValidityTests));
        }

        if (IsNot(input))
        {
            bool internalResult = Filter(GetNotFilter(input), filterFunction, forUniqueValidityTests);
            return forUniqueValidityTests ? internalResult : !internalResult;
        }

        return filterFunction(input);
    }

    /// <summary>
    /// T262: <see cref="Filter(string, Func{string, bool}, bool)"/> with the state its predicate needs passed in, so a <c>static</c> lambda (allocated once)
    /// replaces a capturing one (a closure and a delegate per call); the filters are evaluated on every tile of every stat update.
    /// </summary>
    public static bool Filter<TState>(
        string input,
        TState state,
        Func<string, TState, bool> filterFunction,
        bool forUniqueValidityTests = false)
    {
        if (IsAnd(input))
        {
            foreach (var part in GetAndFilters(input))
                if (!Filter(part, state, filterFunction, forUniqueValidityTests)) return false;
            return true;
        }

        if (IsNot(input))
        {
            bool internalResult = Filter(GetNotFilter(input), state, filterFunction, forUniqueValidityTests);
            return forUniqueValidityTests ? internalResult : !internalResult;
        }

        return filterFunction(input, state);
    }

    public static IEnumerable<string> GetAllSingleFilters(string input)
    {
        if (IsAnd(input))
        {
            return GetAndFilters(input).SelectMany(GetAllSingleFilters);
        }

        if (IsNot(input))
        {
            return GetAllSingleFilters(GetNotFilter(input));
        }

        return [input];
    }

    public static bool IsAnd(string input) =>
        input.StartsWith(AndPrefixChar)
        && input.EndsWith(AndSuffixChar)
        && input.Contains(AndSeparator, StringComparison.Ordinal);

    public static IEnumerable<string> GetAndFilters(string input) =>
        RemoveSurrounding(input, AndPrefix, AndSuffix)
            .Split(AndSeparator, StringSplitOptions.None);

    public static bool IsNot(string input) =>
        input.EndsWith(NotSuffixChar)
        && input.StartsWith(NotPrefix, StringComparison.Ordinal);

    public static string GetNotFilter(string input) =>
        RemoveSurrounding(input, NotPrefix, NotSuffix);

    private static string RemoveSurrounding(string input, string prefix, string suffix)
    {
        if (input.Length >= prefix.Length + suffix.Length
            && input.StartsWith(prefix, StringComparison.Ordinal)
            && input.EndsWith(suffix, StringComparison.Ordinal))
        {
            return input.Substring(prefix.Length, input.Length - prefix.Length - suffix.Length);
        }

        return input;
    }
}
