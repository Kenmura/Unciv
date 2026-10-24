using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Translations;
using Unciv.UI.Screens.Civilopedia;

namespace Unciv.Models.Ruleset;

// core/src/com/unciv/models/ruleset/Tutorial.kt

/// <summary>
/// Container for json-read "Tutorial" text, potentially decorated.
/// Two types for now - triggered (which can be hidden from Civilopedia via the usual unique) and Civilopedia-only.
/// Triggered ones are displayed in a Popup, the relation is via <c>name</c> (the <see cref="TutorialTrigger"/> enum name cleaned by dropping leading '_'
/// and replacing other '_' with blanks must match a json entry name exactly).
/// </summary>
public class Tutorial : RulesetObject, ICivilopediaText
{
    // Has access to the full power of uniques for:
    // * Easier integration into Civilopedia
    // * HiddenWithoutReligion, HiddenFromCivilopedia work _directly_
    // * Future expansion - other meta tests to display or not are thinkable
    // -SomeTrog

    /// <summary>The subcategory to place this tutorial within the Civilopedia.</summary>
    public string? Category { get; set; }

    /// <summary>
    /// These lines will be displayed (when the Tutorial is _triggered_) one after another,
    /// and the Tutorial is marked as completed only once the last line is dismissed with "OK"
    /// </summary>
    public List<string>? Steps { get; set; }

    public override UniqueTarget GetUniqueTarget() => UniqueTarget.Tutorial;
    public override string MakeLink() => $"Tutorial/{Name}";

    public List<FormattedLine> GetCivilopediaTextLines(CivilopediaContext context) =>
        Steps?.Select(it => new FormattedLine(it)).ToList() ?? [];

    /// <summary>Gets the subcategory of this Tutorial: the category, or "Tutorials" if one is not provided.</summary>
    public string? GetSubCategory(Ruleset ruleset) => string.IsNullOrEmpty(Category) ? "Tutorials" : Category;

    /// <summary>Builds a sort group number based on the first two characters of the category.</summary>
    public int GetSortGroup(Ruleset ruleset)
    {
        if (Category == null) return -1; // Default to "Tutorials " on top
        var translatedCategory = Category.Tr().Take(2).ToArray();
        // Kotlin take(2).lowercase(): per UTF-16 char, as Char.code is a UTF-16 code unit
        var lowered = new string(translatedCategory).ToLowerInvariant();
        if (lowered.Length < 2) return lowered.Length == 0 ? throw new IndexOutOfRangeException() : lowered[0];
        return (lowered[0] << 16) + lowered[1];
    }
}
