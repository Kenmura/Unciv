namespace Unciv.Models.Ruleset.Unique;

// Seams for the parts of the unique system that other tickets own. `Unique`, `UniqueMap` and `IHasUniques` call these exactly
// as the Kotlin calls the real classes, so the owning ticket replaces the stub by deleting it here and adding the real class
// (same namespace, same member names); no caller changes.

/// <summary>
/// The members of a <c>DeprecatedUniqueType</c> entry that <see cref="Unique"/> reads, plus <c>@Deprecated(replaceWith=...)</c>.
/// T029 builds these from its table; T029 may add the other annotation fields (message, level).
/// </summary>
public sealed record DeprecatedUniqueTypeEntry(string Text, IReadOnlySet<UniqueFlag> Flags, string ReplaceWith);

/// <summary>Stub for T029 (<c>DeprecatedUniqueType.uniqueTypeMap</c>). T029 replaces the body with a lookup in its table.</summary>
public static class DeprecatedUniqueTypes
{
    // TODO(T029): fill with DeprecatedUniqueType.uniqueTypeMap
    // Internal so the tests can register entries (under placeholder texts no real unique uses) to reach the deprecated-type paths
    // of Unique until T029 lands. T029 makes this a read-only table again.
    internal static readonly System.Collections.Concurrent.ConcurrentDictionary<string, DeprecatedUniqueTypeEntry> UniqueTypeMap = new();

    public static DeprecatedUniqueTypeEntry? Lookup(string placeholderText) => UniqueTypeMap.GetValueOrDefault(placeholderText);
}
