using Unciv.Models.Ruleset.Unique;

namespace Unciv.Models.Ruleset;

// core/src/com/unciv/models/ruleset/Tutorial.kt
// Not ported yet: getCivilopediaTextLines, getSubCategory and getSortGroup (they take Ruleset, T034, and use the Civilopedia text types).

/// <summary>
/// Container for json-read "Tutorial" text, potentially decorated.
/// Two types for now - triggered (which can be hidden from Civilopedia via the usual unique) and Civilopedia-only.
/// Triggered ones are displayed in a Popup, the relation is via <c>name</c> (the <see cref="TutorialTrigger"/> enum name cleaned by dropping leading '_'
/// and replacing other '_' with blanks must match a json entry name exactly).
/// </summary>
public class Tutorial : RulesetObject
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
    public string MakeLink() => $"Tutorial/{Name}";
}
