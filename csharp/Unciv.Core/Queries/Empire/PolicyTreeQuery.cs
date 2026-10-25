using Unciv.Models.Ruleset;
using Unciv.View;

namespace Unciv.Queries.Empire;

// Name: Policy name
// Kind: BranchStart, Member or BranchComplete
// Row, Column: Position in the branch
// Requires: Policies that must be adopted first
// State: Adopted, Adoptable (rules allow it now; the culture cost is separate), or Locked
// LockedReason: Why it is Locked: "requires:<policy>" (first missing one), "era:<era>", "complete-automatically" or "unavailable"
public sealed record PolicyEntry(string Name, string Kind, int Row, int Column, IReadOnlyList<string> Requires, string State, string? LockedReason);

// Adopted: Policies of the branch adopted so far (its opener counts), Total: opener plus members, without the completion bonus
public sealed record PolicyBranchEntry(string Name, string Era, string State, string? LockedReason, int Adopted, int Total,
    PolicyEntry Start, IReadOnlyList<PolicyEntry> Policies);

public sealed record PolicyTreeResult(int StoredCulture, int CultureNeededForNextPolicy, int FreePolicies, bool CanAdopt,
    bool ShouldShowPicker, IReadOnlyList<PolicyBranchEntry> Branches);

// T197: the policy tree of the viewer. Adoptability is PolicyManager.IsAdoptable; the reason a policy is locked repeats its checks in
// the same order, so it names the first rule that fails.
[QueryType("empire.PolicyTree")]
public sealed record PolicyTreeQuery : IQuery<PolicyTreeResult>
{
    public string Type => "empire.PolicyTree";

    public PolicyTreeResult Run(GameView view)
    {
        var civ = view.CivView.Unwrap();
        var policies = civ.Policies;
        var branches = new List<PolicyBranchEntry>();
        foreach (var branch in policies.Branches)
        {
            var members = branch.Policies.Where(it => it.PolicyBranchType != Policy.PolicyBranchTypes.BranchComplete).ToList();
            var all = branch.Policies.Select(it => Entry(civ, it)).ToList();
            int adopted = members.Count(it => policies.IsAdopted(it.Name)) + (policies.IsAdopted(branch.Name) ? 1 : 0);
            var start = Entry(civ, branch);
            branches.Add(new PolicyBranchEntry(branch.Name, branch.Era, start.State, start.LockedReason, adopted, members.Count + 1, start, all));
        }
        return new PolicyTreeResult(policies.StoredCulture, policies.GetCultureNeededForNextPolicy(), policies.FreePolicies,
            policies.CanAdoptPolicy(), policies.ShouldShowPolicyPicker(), branches);
    }

    private static PolicyEntry Entry(Logic.Civilization.Civilization civ, Policy policy)
    {
        var manager = civ.Policies;
        string state;
        string? reason = null;
        if (manager.IsAdopted(policy.Name)) state = "Adopted";
        else if (manager.IsAdoptable(policy)) state = "Adoptable";
        else
        {
            state = "Locked";
            reason = LockedReason(civ, policy);
        }
        return new PolicyEntry(policy.Name, policy.PolicyBranchType.ToString(), policy.Row, policy.Column, (policy.Requires ?? []).ToList(), state, reason);
    }

    private static string LockedReason(Logic.Civilization.Civilization civ, Policy policy)
    {
        var manager = civ.Policies;
        if (policy.PolicyBranchType == Policy.PolicyBranchTypes.BranchComplete) return "complete-automatically";
        var missing = (policy.Requires ?? []).FirstOrDefault(it => !manager.GetAdoptedPolicies().Contains(it));
        if (missing != null) return "requires:" + missing;
        if (!manager.IsAdoptable(policy, checkEra: false)) return "unavailable";
        return "era:" + policy.Branch.Era;
    }
}
