using System.Text.Json.Serialization;
using Unciv.Logic;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Translations;

namespace Unciv.Models.Ruleset;

// core/src/com/unciv/models/ruleset/Policy.kt
// Not ported yet: getSortGroup, getSubCategory and the Civilopedia text lines, they need Ruleset (T034) and the Civilopedia text types.
public class Policy : RulesetObject
{
    /// <summary>Indicates whether a <see cref="Policy"/> is a <see cref="PolicyBranch"/> starting policy, a normal one, or the branch completion</summary>
    public enum PolicyBranchTypes { BranchStart, Member, BranchComplete }

    public const string BranchCompleteSuffix = " Complete";

    private PolicyBranch? branch;
    private readonly Lazy<PolicyBranchTypes> policyBranchType;

    public Policy()
    {
        policyBranchType = new Lazy<PolicyBranchTypes>(() => this switch
        {
            PolicyBranch => PolicyBranchTypes.BranchStart,
            _ when IsBranchCompleteByName(Name) => PolicyBranchTypes.BranchComplete,
            _ => PolicyBranchTypes.Member,
        });
    }

    /// <summary>Kotlin `lateinit var`, not in json - added in gameBasics</summary>
    [JsonIgnore]
    public PolicyBranch Branch
    {
        get => branch ?? throw new InvalidOperationException("lateinit property branch has not been initialized");
        set => branch = value;
    }

    public int Row { get; set; }
    public int Column { get; set; }
    public List<string>? Requires { get; set; }

    /// <summary>Indicates whether this <see cref="Policy"/> is a <see cref="PolicyBranch"/> starting policy, a normal one, or the branch completion</summary>
    [JsonIgnore]
    public PolicyBranchTypes PolicyBranchType => policyBranchType.Value;

    public override UniqueTarget GetUniqueTarget() => UniqueTarget.Policy;

    /// <summary>
    /// Some tests to count policies by completion or not use only the String collection without instantiating them.
    /// To keep the hardcoding in one place, this is public and should be used instead of duplicating it.
    /// </summary>
    public static bool IsBranchCompleteByName(string name) => name.EndsWith(BranchCompleteSuffix, StringComparison.Ordinal);

    public bool MatchesFilter(string filter, GameContext? state = null)
    {
        var self = (IHasUniques)this;
        return MultiFilter.Filter(filter, it =>
            MatchesSingleFilter(it) ||
            (state != null && self.HasTagUnique(it, state)) ||
            (state == null && self.HasTagUnique(it)));
    }

    // Remember policy branches are duplicated in `policies` (as subclass carrying more information),
    // so filtering by a policy branch name matches only the branch itself, filtering by "[name] branch"
    // will match all policies in that branch plus the branch itself (since the loader sets a branch's branch to itself).
    public bool MatchesSingleFilter(string filter)
    {
        if (Constants.All.Contains(filter)) return true;
        if (filter == Name) return true;
        return filter == $"[{Branch.Name}] branch";
    }

    /// <summary>Used in PolicyPickerScreen to display Policy properties</summary>
    public string GetDescription()
    {
        return (PolicyBranchType == PolicyBranchTypes.Member ? Name.Tr() + "\n" : "") +
            string.Join("\n", UniqueObjects
                .Where(it => !(it.IsHiddenToUsers()
                    || it.Type == UniqueType.OnlyAvailable
                    || it.Type == UniqueType.OneTimeGlobalAlert))
                .Select(it => $"• {it.GetDisplayText().Tr()}"));
    }

    public override string MakeLink() => $"Policy/{Name}";
}
