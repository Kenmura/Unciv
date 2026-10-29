using Unciv.Logic.Civilization.Managers;
using Unciv.Models.Ruleset;
using Unciv.View;

namespace Unciv.Queries.Empire;

// Name: Policy name
// Kind: BranchStart, Member or BranchComplete
// Row, Column: Position in the branch
// Requires: Policies that must be adopted first
// State: Adopted, Adoptable (rules allow it now; the culture cost is separate), or Locked
// LockedReason: Why it is Locked: "requires:<policy>" (first missing one), "era:<era>", "complete-automatically" or "unavailable"; B013 adds
// "ideology-locked" (no ideology can be chosen yet), "ideology:<branch>" (a tenet of an ideology the civ did not choose) and "tenet-level:<n>" (needs more Level n tenets)
public sealed record PolicyEntry(string Name, string Kind, int Row, int Column, IReadOnlyList<string> Requires, string State, string? LockedReason);

// Adopted: Policies of the branch adopted so far (its opener counts), Total: opener plus members, without the completion bonus
public sealed record PolicyBranchEntry(string Name, string Era, string State, string? LockedReason, int Adopted, int Total,
    PolicyEntry Start, IReadOnlyList<PolicyEntry> Policies);

// B013: FreeTenets: free tenet picks, Ideology: the chosen ideology branch (null before the choice), CanChooseIdeology: the unlock of bnw-spec 4.6.2 holds
public sealed record PolicyTreeResult(int StoredCulture, int CultureNeededForNextPolicy, int FreePolicies, bool CanAdopt,
    bool ShouldShowPicker, IReadOnlyList<PolicyBranchEntry> Branches, int FreeTenets = 0, string? Ideology = null, bool CanChooseIdeology = false);

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
            policies.CanAdoptPolicy(), policies.ShouldShowPolicyPicker(), branches, policies.FreeTenets, policies.Ideology, policies.CanChooseIdeology());
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
        if (policy.Branch.PurchaseByLevel) return IdeologyLockedReason(manager, policy);
        if (!manager.IsAdoptable(policy, checkEra: false)) return "unavailable";
        return "era:" + policy.Branch.Era;
    }

    // B013: the ideology branch is chosen by era or Factories; a tenet needs the choice, then the level rule (Level n+1 needs more Level n than Level n+1 tenets)
    private static string IdeologyLockedReason(PolicyManager manager, Policy policy)
    {
        if (!manager.IdeologiesEnabled) return "unavailable";
        if (policy is PolicyBranch) return manager.Ideology != null ? "unavailable" : "ideology-locked";
        if (manager.Ideology != policy.Branch.Name) return "ideology:" + policy.Branch.Name;
        return policy.Level > 1 && manager.GetTenetCount(policy.Branch, policy.Level - 1) <= manager.GetTenetCount(policy.Branch, policy.Level)
            ? "tenet-level:" + (policy.Level - 1).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : "unavailable";
    }
}
