namespace Unciv.Assets;

/// <summary>
/// C071: the search filter of the tech tree and the policy screen, one matcher for both (design/ui-redesign.md S1). An empty or blank query matches everything; otherwise an item
/// matches when the trimmed query is part of one of its names or one of its lines, ignoring case (ordinal, so it does not depend on the culture).
/// </summary>
public static class SearchMatcher
{
    /// <summary>
    /// True when <paramref name="query"/> is blank, or part of a name (also as <paramref name="translate"/> turns it into what the player sees) or of a line (taken as it is,
    /// because lines are already composed text).
    /// </summary>
    public static bool Matches(string query, IEnumerable<string> names, IEnumerable<string> lines, Func<string, string>? translate = null)
    {
        string needle = query.Trim();
        if (needle.Length == 0) return true;
        bool Has(string text) => text.Contains(needle, StringComparison.OrdinalIgnoreCase);
        return names.Any(it => Has(it) || (translate != null && Has(translate(it)))) || lines.Any(Has);
    }
}
