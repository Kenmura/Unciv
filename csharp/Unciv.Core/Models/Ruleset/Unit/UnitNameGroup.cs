using Unciv.Models.Ruleset.Unique;

namespace Unciv.Models.Ruleset.Unit;

// core/src/com/unciv/models/ruleset/unit/UnitNameGroup.kt
// Not ported yet, they need the Civilopedia text types, FormattedLine and the game settings collator (T034): makeLink, getCivilopediaTextLines,
// getSortGroup, getSubCategory.
public class UnitNameGroup : RulesetObject
{
    /// <summary>A list of names available for this historical figure group.</summary>
    public List<string> UnitNames { get; set; } = [];

    public UnitNameGroup Clone() => new()
    {
        // RulesetObject fields
        Name = Name,
        Uniques = Uniques,

        // UnitNameGroup fields
        UnitNames = UnitNames,
    };

    /// <summary>Retrieve a list of units that match this unit group name instance.</summary>
    public List<BaseUnit> GetUnits(IRulesetConstructionView ruleset, GameContext? gameContext = null)
    {
        gameContext ??= GameContext.IgnoreConditionalsState;
        var self = (IHasUniques)this;
        return ruleset.GetAllUnits().Where(unit =>
            ((IHasUniques)unit).GetMatchingUniques(UniqueType.OneTimeUnitGetsName).Any(unique =>
                // Match by using either the direct name, or a tag
                unique.Params[1] == Name || self.HasTagUnique(unique.Params[1], gameContext))).ToList();
    }

    public override UniqueTarget GetUniqueTarget() => UniqueTarget.EventChoice;
}
