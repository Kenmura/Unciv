using System.Text.Json.Serialization;
using Unciv.Models.Ruleset.Unique;
using Unciv.Utils;

namespace Unciv.Models.Ruleset.Tech;

// core/src/com/unciv/models/ruleset/tech/Technology.kt
// Not ported yet, they need Ruleset, Civilization and the Civilopedia/description types (T034, T046): getSortGroup, getSubCategory, getDescription,
// makeLink, getCivilopediaTextLines, matchesFilter/matchesSingleFilter (read gameInfo.ruleset.eras via the state).
public class Technology : RulesetObject
{
    public int Cost { get; set; }

    /// <summary>Kotlin `HashSet`: insertion-ordered here, so iteration is deterministic</summary>
    public LinkedHashSet<string> Prerequisites { get; set; } = [];

    /// <summary>The column that this tech is in the tech tree. Set when the ruleset is loaded, not in json.</summary>
    [JsonIgnore]
    public TechColumn? Column { get; set; }

    public int Row { get; set; }
    public string Quote { get; set; } = "";

    public override UniqueTarget GetUniqueTarget() => UniqueTarget.Tech;

    public string Era() => Column!.Era;

    public bool IsContinuallyResearchable() => ((IHasUniques)this).HasUnique(UniqueType.ResearchableMultipleTimes);

    /// <summary>
    /// Wrapper so that if the way to require a tech with a Unique ever changes, this only needs to change in one place.
    /// OnlyAvailableWhen can take multiple conditionals, in which case the true conditional is implicitly the conjunction of all those conditionals.
    /// With several conditionals, one of which requires this tech, IHasUniques.techsRequiredByUniques() lists this tech as required (because it is),
    /// but this does *not* identify that OnlyAvailableWhen as a requirement for this tech (because it's more complicated than that).
    /// </summary>
    public bool UniqueIsRequirementForThisTech(Unique.Unique unique) =>
        unique.Type == UniqueType.OnlyAvailable
        && unique.Modifiers.Count == 1
        && unique.Modifiers[0] is { } modifier && modifier.Type == UniqueType.ConditionalTech && modifier.Params[0] == Name;

    public bool UniqueIsNotRequirementForThisTech(Unique.Unique unique) => !UniqueIsRequirementForThisTech(unique);
}
