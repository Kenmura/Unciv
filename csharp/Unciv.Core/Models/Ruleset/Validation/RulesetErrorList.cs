using System.Collections;
using Unciv.Models.Ruleset.Unique;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.Models.Ruleset.Validation;

// Ported from core/src/com/unciv/models/ruleset/validation/RulesetErrorList.kt
// Deviation: Kotlin's RulesetErrorList is an ArrayList subclass whose add/addAll are overridden. C# List.Add is not virtual, so this is a wrapper exposing
// IReadOnlyList plus Add/AddAll with the Kotlin semantics (there is no unchecked add).

public sealed class RulesetError(string text, RulesetErrorSeverity errorSeverityToReport)
{
    public string Text { get; } = text;
    public RulesetErrorSeverity ErrorSeverityToReport { get; } = errorSeverityToReport;
}

/// <summary>Ordinal determines severity (least to most severe): comparisons and <c>maxOf</c> use it.</summary>
public enum RulesetErrorSeverity
{
    /// <summary>Only appears in mod checker - used for possible misspellings, etc</summary>
    OK,
    /// <summary>Only appears in mod checker</summary>
    WarningOptionsOnly,
    Warning,
    /// <summary>Only appears in mod checker</summary>
    ErrorOptionsOnly,
    Error,
}

public static class RulesetErrorSeverityExtensions
{
    /// <summary>Kotlin <c>RulesetErrorSeverity.color</c> (libGDX GREEN, YELLOW, ORANGE, RED).</summary>
    public static EngineColor Color(this RulesetErrorSeverity severity) => severity switch
    {
        RulesetErrorSeverity.OK => new EngineColor(0f, 1f, 0f, 1f),
        RulesetErrorSeverity.WarningOptionsOnly or RulesetErrorSeverity.Warning => new EngineColor(1f, 1f, 0f, 1f),
        RulesetErrorSeverity.ErrorOptionsOnly => new EngineColor(1f, 0.64705884f, 0f, 1f),
        _ => new EngineColor(1f, 0f, 0f, 1f),
    };

    /// <summary>Kotlin <c>RulesetErrorSeverity.iconName</c></summary>
    public static string IconName(this RulesetErrorSeverity severity) => severity switch
    {
        RulesetErrorSeverity.OK => "OtherIcons/Checkmark",
        RulesetErrorSeverity.WarningOptionsOnly or RulesetErrorSeverity.Warning or RulesetErrorSeverity.ErrorOptionsOnly => "OtherIcons/ExclamationMark",
        _ => "OtherIcons/Stop",
    };

    /// <summary>Kotlin <c>UniqueType.UniqueParameterErrorSeverity.getRulesetErrorSeverity</c></summary>
    public static RulesetErrorSeverity GetRulesetErrorSeverity(this UniqueParameterErrorSeverity severity) => severity switch
    {
        UniqueParameterErrorSeverity.PossibleFilteringUnique => RulesetErrorSeverity.WarningOptionsOnly,
        // Report Warning on the first pass of RulesetValidator only, where mods are checked standalone
        // but upgrade to error when the second pass asks, which runs only for combined or base rulesets.
        UniqueParameterErrorSeverity.RulesetSpecific => RulesetErrorSeverity.Warning,
        UniqueParameterErrorSeverity.RulesetInvariant => RulesetErrorSeverity.Error,
        _ => throw new ArgumentOutOfRangeException(nameof(severity)),
    };
}

/// <summary>
///  A container collecting errors in a <see cref="Ruleset"/>.
///  Mod-controlled warning suppression is handled in the <see cref="Add(RulesetError, IHasUniques?, UniqueObject?)"/> overloads that provide a source object,
///  which can host suppression uniques. <see cref="AddAll"/> is fine when the elements to add are all already checked.
/// </summary>
/// <param name="ruleset">The ruleset being validated (needed to check modOptions for suppression uniques). Leave null only for validation results that need no suppression checks.</param>
public sealed class RulesetErrorList(Ruleset? ruleset = null) : IReadOnlyList<RulesetError>
{
    private readonly List<RulesetError> items = [];

    private readonly IReadOnlySet<string> globalSuppressionFilters = ((IHasUniques?)ruleset?.ModOptions)?
        .GetMatchingUniques(UniqueType.SuppressWarnings, GameContext.IgnoreConditionalsState)
        .Select(it => it.Params[0])
        .ToHashSet() ?? new HashSet<string>();

    public int Count => items.Count;
    public RulesetError this[int index] => items[index];
    public IEnumerator<RulesetError> GetEnumerator() => items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Add an element, preventing duplicates (in which case the highest severity wins).</summary>
    /// <param name="element"></param>
    /// <param name="sourceObject">the originating object, which can host suppressions. When it is not known or not a IHasUniques, pass null.</param>
    /// <param name="sourceUnique">the originating unique, so look for suppression modifiers. Leave null if unavailable.</param>
    public bool Add(RulesetError element, IHasUniques? sourceObject, UniqueObject? sourceUnique = null)
    {
        // The dupe check may be faster than the Suppression check, so do it first?
        if (!RemoveLowerSeverityDuplicate(element)) return false;
        if (Suppression.IsErrorSuppressed(globalSuppressionFilters, sourceObject, sourceUnique, element))
            return false;
        items.Add(element);
        return true;
    }

    /// <summary>Shortcut: Add a new <see cref="RulesetError"/> built from <paramref name="text"/> and <paramref name="errorSeverityToReport"/>.</summary>
    public bool Add(string text, RulesetErrorSeverity errorSeverityToReport = RulesetErrorSeverity.Error, IHasUniques? sourceObject = null, UniqueObject? sourceUnique = null) =>
        Add(new RulesetError(text, errorSeverityToReport), sourceObject, sourceUnique);

    /// <summary>Add all elements with duplicate check, but without suppression check</summary>
    public bool AddAll(IEnumerable<RulesetError> elements)
    {
        bool result = false;
        foreach (var element in elements.ToList())
            if (AddWithDuplicateCheck(element)) result = true;
        return result;
    }

    private bool AddWithDuplicateCheck(RulesetError element)
    {
        if (!RemoveLowerSeverityDuplicate(element)) return false;
        items.Add(element);
        return true;
    }

    /// <returns>true if the element is not present, or it was removed due to having a lower severity</returns>
    private bool RemoveLowerSeverityDuplicate(RulesetError element)
    {
        // Suppress duplicates due to the double run of some checks for invariant/specific,
        // Without changing collection type or making RulesetError obey the equality contract
        var existing = items.FirstOrDefault(it => it.Text == element.Text);
        if (existing == null) return true;
        if (existing.ErrorSeverityToReport >= element.ErrorSeverityToReport) return false;
        items.Remove(existing);
        return true;
    }

    public RulesetErrorSeverity GetFinalSeverity()
    {
        if (items.Count == 0) return RulesetErrorSeverity.OK;
        return items.Max(it => it.ErrorSeverityToReport);
    }

    /// <returns>true means severe errors make the mod unplayable</returns>
    public bool IsError() => GetFinalSeverity() == RulesetErrorSeverity.Error;

    /// <returns>true means problems exist, Options screen mod checker or unit tests for vanilla ruleset should complain</returns>
    public bool IsNotOK() => GetFinalSeverity() != RulesetErrorSeverity.OK;

    /// <returns>true means at least errors impacting gameplay exist, new game screen should warn or block</returns>
    public bool IsWarnUser() => GetFinalSeverity() >= RulesetErrorSeverity.Warning;

    public string GetErrorText(bool unfiltered = false) =>
        GetErrorText(it => unfiltered || it.ErrorSeverityToReport > RulesetErrorSeverity.WarningOptionsOnly);

    public string GetErrorText(Func<RulesetError, bool> filter) =>
        string.Join("\n",
            items.Where(filter)
                .OrderByDescending(it => it.ErrorSeverityToReport) // stable, like sortedByDescending
                .Select(it => it.ErrorSeverityToReport + ": " +
                    // This will go through tr(), unavoidably, which will move the conditionals
                    // out of place. Prevent via kludge:
                    it.Text.Replace('<', '〈').Replace('>', '〉').Replace('[', '⟦').Replace(']', '⟧')));

    /// <summary>
    /// Helper factory for a single entry list (which can result in an empty list due to suppression).
    /// Note: Valid source for <see cref="AddAll"/> since suppression is already taken care of.
    /// </summary>
    public static RulesetErrorList Of(
        string text,
        RulesetErrorSeverity severity = RulesetErrorSeverity.Error,
        Ruleset? ruleset = null,
        IHasUniques? sourceObject = null,
        UniqueObject? sourceUnique = null)
    {
        var result = new RulesetErrorList(ruleset);
        result.Add(text, severity, sourceObject, sourceUnique);
        return result;
    }
}
