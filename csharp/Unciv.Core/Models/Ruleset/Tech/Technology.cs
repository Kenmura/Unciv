using System.Text.Json.Serialization;
using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Models.Ruleset.Unique;
using Unciv.UI.ObjectDescriptions;
using Unciv.UI.Screens.Civilopedia;
using Unciv.Utils;

namespace Unciv.Models.Ruleset.Tech;

// core/src/com/unciv/models/ruleset/tech/Technology.kt
public class Technology : RulesetObject, ICivilopediaText, IHasUniques
{
    public override string MakeLink() => $"Technology/{Name}";

    public int GetSortGroup(Ruleset ruleset) => Column == null ? -1 : ((IHasUniques)this).Era(ruleset)?.EraNumber ?? -1;

    public string? GetSubCategory(Ruleset ruleset) => Column == null ? null : ((IHasUniques)this).Era(ruleset)?.Name;

    /// <summary>Get Civilization-specific description for TechPicker or AlertType.TechResearched</summary>
    public string GetDescription(Civilization viewingCiv) => TechnologyDescriptions.GetDescription(this, viewingCiv);

    public List<FormattedLine> GetCivilopediaTextLines(CivilopediaContext context) => TechnologyDescriptions.GetCivilopediaTextLines(this, context);

    Era? IHasUniques.Era(IRulesetTechView ruleset) => ruleset.GetEra(Era());

    public int Cost { get; set; }

    /// <summary>Kotlin `HashSet`: insertion-ordered here, so iteration is deterministic</summary>
    public LinkedHashSet<string> Prerequisites { get; set; } = [];

    /// <summary>The column that this tech is in the tech tree. Set when the ruleset is loaded, not in json.</summary>
    [JsonIgnore]
    public TechColumn? Column { get; set; }

    public int Row { get; set; }
    public string Quote { get; set; } = "";

    public override UniqueTarget GetUniqueTarget() => UniqueTarget.Tech;

    public string Era() => Column.NotNull().Era;

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

    public bool MatchesFilter(string filter, GameContext? state = null, bool multiFilter = true)
    {
        var self = (IHasUniques)this;
        bool Single(string it) =>
            MatchesSingleFilter(it, state) ||
            (state != null && self.HasTagUnique(it, state)) ||
            (state == null && self.HasTagUnique(it));
        return multiFilter ? MultiFilter.Filter(filter, Single) : Single(filter);
    }

    public bool MatchesSingleFilter(string filter, GameContext? state = null)
    {
        if (Constants.All.Contains(filter) || filter == Name || filter == Era()) return true;
        return ((IHasUniques)this).HasTagUnique(filter, state)
            || state?.GameInfo?.Ruleset.GetEra(Era())?.MatchesFilter(filter, state, false) == true;
    }

    public bool UniqueIsNotRequirementForThisTech(Unique.Unique unique) => !UniqueIsRequirementForThisTech(unique);
}
