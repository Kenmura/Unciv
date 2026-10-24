using Unciv.Core.Tests.Support;

namespace Unciv.Core.Tests.Logic.Filter;

// T103: port of tests/src/com/unciv/logic/filter/PolicyFilterTests.kt (Policy filters).
[Collection("RulesetCache")]
public class PolicyFilterTests
{
    [Fact]
    public void TestPolicyMatchesFilter()
    {
        var game = new TestGame();
        game.MakeHexagonalMap(3);
        var civ = game.AddCiv();

        // Don't use a fake Policy without a branch, the policyFilter would stumble over a lateinit.
        var taggedPolicyBranch = game.CreatePolicyBranch(["Some marker"]);
        var policies = new[]
            {
                "Tradition", "Aristocracy", "Legalism", "Oligarchy", "Landed Elite", "Monarchy",
                "Liberty", "Citizenship", "Honor", "Piety",
            }
            .Select(it => game.Ruleset.Policies.TryGetValue(it, out var policy) ? policy : null)
            .OfType<Unciv.Models.Ruleset.Policy>()
            .Append(taggedPolicyBranch)
            .ToList();
        foreach (var policy in policies)
        {
            civ.Policies.FreePolicies++;
            civ.Policies.Adopt(policy);
        }

        var filters = new (string Filter, string[] Names, int Count)[]
        {
            ("Tradition Complete", ["Tradition Complete"], 1), // Completion policies are their own separate policies
            ("Tradition", ["Tradition"], 1), // Should just be the starter by itself
            ("[Tradition] branch", ["Tradition", "Tradition Complete", "Aristocracy", "Legalism", "Oligarchy", "Landed Elite", "Monarchy"], 7), // starter, completion policy and all policies in its branch
            ("Liberty Complete", [], 0), // Should not include completion branches that have not been completed
            ("[Liberty] branch", ["Liberty", "Citizenship"], 2), // Should only include polices actually gotten
            ("Military Tradition", [], 0),
            ("Some marker", [taggedPolicyBranch.Name], 1), // Should include uniques of policies
        };
        var failures = new List<string>();
        foreach (var (filter, names, count) in filters)
        {
            var filtered = civ.Policies.GetAdoptedPoliciesMatching(filter).ToList();
            var filteredNames = filtered.Select(it => it.Name).ToList();
            if (!names.All(filteredNames.Contains) || filtered.Count != count)
                failures.Add($"Filter: {filter}\nExpected result: {string.Join(", ", names)}, expected size: {count}\nResult: {string.Join(", ", filteredNames)}, size: {filtered.Count}\n");
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
        Assert.Equal(2, civ.GetCompletedPolicyBranchesCount()); // Tradition and taggedPolicyBranch
    }
}
