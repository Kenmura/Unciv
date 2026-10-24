using Unciv.Models.Ruleset.Unique;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.Models.Ruleset;

// core/src/com/unciv/models/ruleset/GlobalUniques.kt
// Not ported yet: the Civilopedia members (getCivilopediaTextLines, and the civilopediaText that combine() concatenates), they need the Civilopedia text types.
public class GlobalUniques : RulesetObject
{
    public GlobalUniques()
    {
        Name = "Global Uniques";
    }

    public string MakeLink() => "Tutorial/Global Uniques";

    public List<string> UnitUniques { get; set; } = [];
    public override UniqueTarget GetUniqueTarget() => UniqueTarget.GlobalUniques;

    /// <returns>Whether or not there are global uniques that should be displayed to the user.</returns>
    public bool HasUniques() => UniqueObjects.Any(it => !it.IsHiddenToUsers()) || UnitUniques.Count != 0;

    public static string GetUniqueSourceDescription(UniqueObject unique)
    {
        if (unique.Modifiers.Count == 0)
            return "Global Effect";

        return unique.Modifiers[0].Type switch
        {
            UniqueType.ConditionalGoldenAge => "Golden Age",
            UniqueType.ConditionalHappy => "Happiness",
            UniqueType.ConditionalWLTKD => "We Love The King Day",
            _ => "Global Effect",
        };
    }

    /// <summary>
    /// We're not copying <see cref="RulesetObject.Name"/> which means any assignments in actual jsons differing from the default will be lost.
    /// </summary>
    public static GlobalUniques Combine(GlobalUniques globalUniques, params IHasUniques[] otherSources)
    {
        var result = new GlobalUniques();
        foreach (var source in new IHasUniques[] { globalUniques }.Concat(otherSources))
        {
            result.Uniques.AddRange(source.Uniques);
            if (source is not GlobalUniques sourceGlobal) continue;
            result.UnitUniques.AddRange(sourceGlobal.UnitUniques);
        }
        return result;
    }
}
