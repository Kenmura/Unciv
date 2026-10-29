using System.Text.Json.Serialization;
using Unciv.Logic;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Translations;
using Unciv.UI.ObjectDescriptions;
using Unciv.UI.Screens.Civilopedia;
using Unciv.Utils;

namespace Unciv.Models.Ruleset;

// core/src/com/unciv/models/ruleset/Policy.kt
public class Policy : RulesetObject, ICivilopediaText
{
    /// <summary>Indicates whether a <see cref="Policy"/> is a <see cref="PolicyBranch"/> starting policy, a normal one, or the branch completion</summary>
    public enum PolicyBranchTypes { BranchStart, Member, BranchComplete }

    public const string BranchCompleteSuffix = " Complete";

    private PolicyBranch? branch;
    private readonly KotlinLazy<PolicyBranchTypes> policyBranchType;

    public Policy()
    {
        policyBranchType = new KotlinLazy<PolicyBranchTypes>(() => this switch
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

    /// <summary>Not in Kotlin (B013, bnw-spec 4.6.3): the tenet level (1 to 3) of a policy in an ideology branch; 0 elsewhere.</summary>
    public int Level { get; set; }

    /// <summary>Not in Kotlin (B013): a member of an ideology branch (<see cref="PolicyBranch.PurchaseByLevel"/>), bought with a tenet pick instead of a policy pick.</summary>
    [JsonIgnore]
    public bool IsTenet => this is not PolicyBranch && Branch.PurchaseByLevel;

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

    public int GetSortGroup(Ruleset ruleset) =>
        (ruleset.Eras.TryGetValue(Branch.Era, out var branchEra) ? branchEra.EraNumber : 0) * 10000 +
        ruleset.PolicyBranches.Keys.ToList().IndexOf(Branch.Name) * 100 +
        (int)PolicyBranchType;

    public string? GetSubCategory(Ruleset ruleset) => Branch.Name;

    public List<FormattedLine> GetCivilopediaTextLines(CivilopediaContext context)
    {
        var ruleset = context.Ruleset;
        var lineList = new List<FormattedLine>();

        if (this is PolicyBranch policyBranch)
        {
            ruleset.Eras.TryGetValue(policyBranch.Era, out var era);
            var eraColor = era?.GetHexColor() ?? "";
            var eraLink = era?.MakeLink() ?? "";
            lineList.Add(new FormattedLine($"{{Unlocked at}} {{{Branch.Era}}}", header: 4, color: eraColor, link: eraLink));
        }
        else
        {
            lineList.Add(new FormattedLine($"Policy branch: [{Branch.Name}]", link: Branch.MakeLink()));
        }

        if (PolicyBranchType != PolicyBranchTypes.BranchComplete && Requires != null && Requires.Count != 0)
        {
            lineList.Add(new FormattedLine());
            if (Requires.Count == 1)
            {
                var first = Requires[0];
                lineList.Add(new FormattedLine($"Requires [{first}]", link: $"Policy/{first}"));
            }
            else
            {
                lineList.Add(new FormattedLine("Requires all of the following:"));
                foreach (var it in Requires)
                    lineList.Add(new FormattedLine(it, link: $"Policy/{it}"));
            }
        }

        var leadsTo = ruleset.Policies.Values.Where(it =>
            it.Requires != null && it.Requires.Contains(Name)
            && it.PolicyBranchType != PolicyBranchTypes.BranchComplete).ToList();
        if (leadsTo.Count != 0)
        {
            lineList.Add(new FormattedLine());
            if (leadsTo.Count == 1)
                lineList.Add(new FormattedLine($"Leads to [{leadsTo[0].Name}]", link: leadsTo[0].MakeLink()));
            else
            {
                lineList.Add(new FormattedLine("Leads to:"));
                foreach (var it in leadsTo)
                    lineList.Add(new FormattedLine(it.Name, link: it.MakeLink(), indent: 1));
            }
        }

        bool IsEnabledByPolicy(IRulesetObject rulesetObject) =>
            rulesetObject.GetMatchingUniques(UniqueType.OnlyAvailable, GameContext.IgnoreConditionalsState).Any(unique =>
                unique.GetModifiers(UniqueType.ConditionalAfterPolicyOrBelief).Any(modifier => modifier.Params[0] == Name))
            || rulesetObject.GetMatchingUniques(UniqueType.Unavailable).Any(unique =>
                unique.GetModifiers(UniqueType.ConditionalBeforePolicyOrBelief).Any(modifier => modifier.Params[0] == Name));

        var enabledBuildings = ruleset.Buildings.Values.Where(it => IsEnabledByPolicy(it)).ToList();
        var enabledUnits = ruleset.Units.Values.Where(it => IsEnabledByPolicy(it)).ToList();

        if (enabledBuildings.Count != 0 || enabledUnits.Count != 0)
        {
            lineList.Add(new FormattedLine("Enables:"));
            foreach (var building in enabledBuildings)
                lineList.Add(new FormattedLine(building.Name, link: building.MakeLink(), indent: 1));
            foreach (var unit in enabledUnits)
                lineList.Add(new FormattedLine(unit.Name, link: unit.MakeLink(), indent: 1));
        }

        bool IsDisabledByPolicy(IRulesetObject rulesetObject)
        {
            if (rulesetObject.GetMatchingUniques(UniqueType.OnlyAvailable, GameContext.IgnoreConditionalsState).Any(unique =>
                    unique.GetModifiers(UniqueType.ConditionalBeforePolicyOrBelief).Any(modifier => modifier.Params[0] == Name)))
                return true;

            if (rulesetObject.GetMatchingUniques(UniqueType.Unavailable, GameContext.IgnoreConditionalsState).Any(unique =>
                    unique.GetModifiers(UniqueType.ConditionalAfterPolicyOrBelief).Any(modifier => modifier.Params[0] == Name)))
                return true;

            return false;
        }

        var disabledBuildings = ruleset.Buildings.Values.Where(it => IsDisabledByPolicy(it)).ToList();
        var disabledUnits = ruleset.Units.Values.Where(it => IsDisabledByPolicy(it)).ToList();

        if (disabledBuildings.Count != 0 || disabledUnits.Count != 0)
        {
            lineList.Add(new FormattedLine("Disables:"));
            foreach (var building in disabledBuildings)
                lineList.Add(new FormattedLine(building.Name, link: building.MakeLink(), indent: 1));
            foreach (var unit in disabledUnits)
                lineList.Add(new FormattedLine(unit.Name, link: unit.MakeLink(), indent: 1));
        }

        this.UniquesToCivilopediaTextLines(lineList, context);

        return lineList;
    }
}
