using Unciv.Models;
using Unciv.Utils;

namespace Unciv.Logic.Civilization;

public partial class Civilization
{
    // Brave New World (B025, spec 4.15): per adopted policy (tenets and branch openers count, the BranchComplete bonus does not), per Great Work in
    // the civ's cities, per belief of its religion (a pantheon counts) and per city of any civ that follows its religion. Each source is added only
    // when its multiplier is non-zero, so G&K rulesets (all four default to 0) keep their breakdown, its map order and its total unchanged.
    private void AddBnwScore(JavaHashMap<string, double> scoreBreakdown, ModConstants modConstants)
    {
        if (modConstants.ScorePolicyMultiplier != 0)
            scoreBreakdown["Policies"] = (double)modConstants.ScorePolicyMultiplier * Policies.AdoptedPolicies
                .Count(it => Policies.GetPolicyByName(it).PolicyBranchType != Models.Ruleset.Policy.PolicyBranchTypes.BranchComplete);
        if (modConstants.ScoreGreatWorkMultiplier != 0)
            scoreBreakdown["Great Works"] = (double)modConstants.ScoreGreatWorkMultiplier * Cities.Sum(it => it.GetNumberOfGreatWorks());
        if (modConstants.ScoreBeliefMultiplier != 0)
            scoreBreakdown["Beliefs"] = (double)modConstants.ScoreBeliefMultiplier * (ReligionManager.Religion?.GetAllBeliefsOrdered().Count() ?? 0);
        if (modConstants.ScoreReligionCitiesMultiplier != 0)
            scoreBreakdown["Religion"] = (double)modConstants.ScoreReligionCitiesMultiplier * ReligionManager.NumberOfCitiesFollowingThisReligion();
    }
}
