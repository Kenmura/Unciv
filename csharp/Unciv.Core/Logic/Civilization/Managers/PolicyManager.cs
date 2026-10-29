using System.Text.Json.Serialization;
using Unciv.Json;
using Unciv.Logic.Map;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Unique;
using Unciv.UI.Components.Extensions;
using Unciv.Utils;

namespace Unciv.Logic.Civilization.Managers;

// Ported from core/src/com/unciv/logic/civilization/managers/PolicyManager.kt (T056).
// Stripped: the purity annotations.
// Deviations:
// - Kotlin HashSet/HashMap of PolicyBranch (identity hash, arbitrary order) are insertion ordered here (ruleset order); the AI-only
//   priorityMap is a Dictionary because its order never reaches anything (getMaxPriority takes a max).
// - B013 (bnw-spec 4.6.2 to 4.6.3, not in Kotlin): ideologies. A civ chooses one ideology branch (free, once; Era above the branch's era or the Factory count), the
//   branch's tenets are bought by level (Level n+1 needs more Level n than Level n+1 tenets) with FreeTenets or culture. Free policies never buy tenets
//   and free tenets never buy social policies, so a free policy (Poland) is a social-branch policy by construction.
// - priorityMap uses IHasUniques.GetWeightForAiDecision (T087); a PolicyBranch is neither BaseUnit nor Building, so it is the AiChoiceWeight product.

public class PolicyManager : IsPartOfGameInfoSerialization
{
    /// <summary>Used in <see cref="GetCultureRefundMap"/> when refunding more policies than were bought with culture
    /// to indicate the "surplus" policies - those must have been adopted as free policies.</summary>
    public const int FreePolicyMarker = -1;

    private Civilization? civInfo;
    /// <summary>Kotlin <c>@Transient lateinit var</c>, set by <c>Civilization.setTransients</c>: reading before assignment throws.</summary>
    [JsonIgnore]
    public Civilization CivInfo
    {
        get => civInfo ?? throw new InvalidOperationException("lateinit property civInfo has not been initialized");
        set => civInfo = value;
    }

    // Needs to be separate from the actual adopted policies, so that
    //  in different game versions, policies can have different effects
    [JsonIgnore]
    internal UniqueMap PolicyUniques { get; } = new();

    [JsonPropertyName("freePolicies")]
    public int FreePolicies { get; set; }
    /// <summary>B013: free tenet picks, granted by choosing an ideology (<see cref="PolicyBranch.FirstAdopterFreePolicies"/> / <see cref="PolicyBranch.SecondAdopterFreePolicies"/>).</summary>
    [JsonPropertyName("freeTenets")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] // absent from Gods & Kings saves
    public int FreeTenets { get; set; }
    /// <summary>B013: the name of the chosen ideology branch, or <c>null</c> before the choice (B008 reads it for tourism).</summary>
    [JsonPropertyName("ideology")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] // absent from Gods & Kings saves
    public string? Ideology { get; set; }
    /// <summary>B013: the "you may choose an ideology" notification has been sent.</summary>
    [JsonPropertyName("ideologyPrompted")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] // absent from Gods & Kings saves
    public bool IdeologyPrompted { get; set; }
    [JsonPropertyName("storedCulture")]
    public int StoredCulture { get; set; }
    [JsonPropertyName("adoptedPolicies")]
    [JsonInclude]
    internal JavaHashSet<string> AdoptedPolicies { get; private set; } = new();
    [JsonPropertyName("numberOfAdoptedPolicies")]
    [JsonInclude]
    private int numberOfAdoptedPolicies;

    [JsonPropertyName("cultureOfLast8Turns")]
    [JsonInclude]
    private int[] cultureOfLast8Turns = new int[8];

    /// <summary>Indicates whether we should *check* if policy is adoptible, and if so open</summary>
    [JsonPropertyName("shouldOpenPolicyPicker")]
    public bool ShouldOpenPolicyPicker { get; set; }

    /// <summary>Used by NextTurnAction.PickPolicy.isChoice</summary>
    public bool ShouldShowPolicyPicker() => (ShouldOpenPolicyPicker || FreePolicies > 0 || FreeTenets > 0) && CanAdoptPolicy();

    /// <summary>A map pairing each <see cref="PolicyBranch"/> to its priority.</summary>
    [JsonIgnore]
    public Dictionary<PolicyBranch, int> PriorityMap
    {
        get
        {
            var value = new Dictionary<PolicyBranch, int>();
            foreach (var branch in Branches)
            {
                var victoryPriority = 0;
                foreach (var victoryType in CivInfo.GetPreferredVictoryTypes())
                    victoryPriority += branch.Priorities.GetValueOrDefault(victoryType, 0);
                var personalityPriority = CivInfo.GetPersonality().Priorities.GetValueOrDefault(branch.Name, 0);
                var branchPriority = (victoryPriority + personalityPriority) * ((IHasUniques)branch).GetWeightForAiDecision(CivInfo.State);
                value[branch] = KotlinMath.RoundToInt(branchPriority);
            }
            return value;
        }
    }

    /// <summary>A set of newly adoptable <see cref="PolicyBranch"/>es.</summary>
    [JsonIgnore]
    public LinkedHashSet<PolicyBranch> AdoptableBranches => new(SocialBranches.Where(it => IsAdoptable(it)));

    /// <summary>A set of incomplete <see cref="PolicyBranch"/>es including newly adoptable ones.</summary>
    [JsonIgnore]
    public LinkedHashSet<PolicyBranch> IncompleteBranches
    {
        get
        {
            var value = new LinkedHashSet<PolicyBranch>();
            foreach (var branch in SocialBranches)
            {
                if (branch.Policies.Any(it => IsAdoptable(it))) value.Add(branch);
            }
            return value;
        }
    }

    /// <summary>A set of completed <see cref="PolicyBranch"/>es.</summary>
    [JsonIgnore]
    public LinkedHashSet<PolicyBranch> CompletedBranches
    {
        get
        {
            var value = new LinkedHashSet<PolicyBranch>();
            foreach (var branch in SocialBranches)
            {
                if (branch.Policies.All(it => IsAdopted(it.Name))) value.Add(branch);
            }
            return value;
        }
    }

    /// <summary>A map pairing each <see cref="PolicyBranch"/> to how many of its child branches are adopted.</summary>
    [JsonIgnore]
    public Dictionary<PolicyBranch, int> BranchCompletionMap
    {
        get
        {
            var value = new Dictionary<PolicyBranch, int>();
            foreach (var branch in SocialBranches)
            {
                value[branch] = AdoptedPolicies.Count(it => branch.Policies.Contains(GetPolicyByName(it)));
            }
            return value;
        }
    }

    /// <summary>A set of all <see cref="PolicyBranch"/>es.</summary>
    [JsonIgnore]
    public LinkedHashSet<PolicyBranch> Branches => new(CivInfo.GameInfo.Ruleset.PolicyBranches.Values);

    /// <summary>B013: the branches that are not ideologies; the AI's branch-by-branch adoption and the completed-branch count look at these only.</summary>
    [JsonIgnore]
    public LinkedHashSet<PolicyBranch> SocialBranches => new(CivInfo.GameInfo.Ruleset.PolicyBranches.Values.Where(it => !it.PurchaseByLevel));

    /// <summary>B013: the ideology branches of the ruleset (empty in Civ V: Gods &amp; Kings).</summary>
    [JsonIgnore]
    public IEnumerable<PolicyBranch> IdeologyBranches => CivInfo.GameInfo.Ruleset.PolicyBranches.Values.Where(it => it.PurchaseByLevel);

    public PolicyManager Clone()
    {
        var toReturn = new PolicyManager
        {
            numberOfAdoptedPolicies = numberOfAdoptedPolicies,
            FreePolicies = FreePolicies,
            FreeTenets = FreeTenets,
            Ideology = Ideology,
            IdeologyPrompted = IdeologyPrompted,
            ShouldOpenPolicyPicker = ShouldOpenPolicyPicker,
            StoredCulture = StoredCulture,
            cultureOfLast8Turns = (int[])cultureOfLast8Turns.Clone(),
        };
        foreach (var policyName in AdoptedPolicies) toReturn.AdoptedPolicies.Add(policyName);
        return toReturn;
    }

    private LinkedHashMap<string, Policy> GetRulesetPolicies() => CivInfo.GameInfo.Ruleset.Policies;

    public Policy GetPolicyByName(string name) => GetRulesetPolicies()[name];

    public void SetTransients(Civilization civInfo)
    {
        CivInfo = civInfo;
        foreach (var policyName in AdoptedPolicies) AddPolicyToTransients(
            GetPolicyByName(policyName)
        );
    }

    private void AddPolicyToTransients(Policy policy)
    {
        foreach (var unique in policy.UniqueObjects)
        {
            PolicyUniques.AddUnique(unique);
        }
    }

    private void RemovePolicyFromTransients(Policy policy)
    {
        foreach (var unique in policy.UniqueObjects)
        {
            PolicyUniques.RemoveUnique(unique);
        }
    }

    public void AddCulture(int culture)
    {
        var couldAdoptPolicyBefore = CanAdoptPolicy();
        StoredCulture = (int)System.Math.Clamp((long)StoredCulture + culture, int.MinValue, int.MaxValue);
        if (!couldAdoptPolicyBefore && CanAdoptPolicy())
        {
            ShouldOpenPolicyPicker = true;
        }
    }

    public void EndTurn(int culture)
    {
        AddCulture(culture);
        AddCurrentCultureToCultureOfLast8Turns(culture);
    }

    // from https://forums.civfanatics.com/threads/the-number-crunching-thread.389702/
    // round down to nearest 5
    public int GetCultureNeededForNextPolicy() => GetPolicyCultureCost(numberOfAdoptedPolicies);

    /// <summary>
    /// Maps <paramref name="policiesToRemove"/> to a culture amount to refund.
    /// If more policies are removed than were bought with culture, the "extras" are returned
    /// with value <see cref="FreePolicyMarker"/>: do not refund in that case, grant the free policy back - or not.
    /// </summary>
    public LinkedHashMap<Policy, int> GetCultureRefundMap(IEnumerable<Policy> policiesToRemove, int refundPercentage)
    {
        var policyCostInput = numberOfAdoptedPolicies;

        var policyMap = new LinkedHashMap<Policy, int>();

        foreach (var policy in policiesToRemove)
        {
            if (policy.PolicyBranchType == Policy.PolicyBranchTypes.BranchComplete)
                continue;
            policyCostInput--;
            policyMap.Put(policy, policyCostInput < 0 ? FreePolicyMarker
                : KotlinMath.RoundToInt(GetPolicyCultureCost(policyCostInput) * refundPercentage / 100f));
        }

        return policyMap;
    }

    public int GetPolicyCultureCost(int numberOfAdoptedPolicies)
    {
        var policyCultureCost = 25 + Math.Pow((double)(numberOfAdoptedPolicies * 3), 2.01);
        var worldSizeModifier = CivInfo.GameInfo.TileMap.MapParameters.MapSize.GetPredefinedOrNextSmaller().GetPolicyCostPerCityModifier();
        var cityModifier = worldSizeModifier * (CivInfo.Cities.Count(it => !it.IsPuppet) - 1);

        CivInfo.ForEachMatchingUnique(UniqueType.LessPolicyCostFromCities, unique => cityModifier *= 1 - ParseFloat(unique.Params[0]) / 100);
        CivInfo.ForEachMatchingUnique(UniqueType.LessPolicyCost, unique => policyCultureCost *= unique.Params[0].ToPercent());
        if (CivInfo.IsHuman()) policyCultureCost *= CivInfo.GetDifficulty().PolicyCostModifier;
        policyCultureCost *= CivInfo.GameInfo.Speed.CultureCostModifier;
        var cost = KotlinMath.RoundToInt(policyCultureCost * (1 + cityModifier));
        return cost - (cost % 5);
    }

    private static float ParseFloat(string text) =>
        float.Parse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture);

    public JavaHashSet<string> GetAdoptedPolicies() => AdoptedPolicies;

    /// <summary>
    /// Gets the adopted policies as <see cref="Policy"/> objects that match <paramref name="policyFilter"/>. Lazy and uncached, use carefully.
    /// </summary>
    /// <param name="policyFilter"></param>
    /// <param name="gameContext">Passed to <see cref="Policy.MatchesFilter"/>; defaults to the civ's state</param>
    /// <param name="forRemoval">When <c>true</c> sorts the result by json position descending,
    ///     so a removal in order won't remove a branch start before its members.
    ///     Also, <c>BranchComplete</c> policies are skipped - you shouldn't try to remove them explicitly.</param>
    public IEnumerable<Policy> GetAdoptedPoliciesMatching(string policyFilter, GameContext? gameContext = null, bool forRemoval = false)
    {
        var context = gameContext ?? CivInfo.State;
        var rulesetPolicies = GetRulesetPolicies();
        var matchingPolicies = AdoptedPolicies
            .Select(it => rulesetPolicies.TryGetValue(it, out var policy) ? policy : null)
            .OfType<Policy>()
            .Where(it => it.MatchesFilter(policyFilter, context));
        if (!forRemoval) return matchingPolicies;
        return matchingPolicies
            .Where(it => it.PolicyBranchType != Policy.PolicyBranchTypes.BranchComplete)
            .OrderByDescending(it => IndexOfPolicy(rulesetPolicies, it));
    }

    private static int IndexOfPolicy(LinkedHashMap<string, Policy> rulesetPolicies, Policy policy)
    {
        var index = 0;
        foreach (var candidate in rulesetPolicies.Values)
        {
            if (candidate.Equals(policy)) return index;
            index++;
        }
        return -1;
    }

    public bool IsAdopted(string policyName) => AdoptedPolicies.Contains(policyName);

    /// <summary>
    /// Test whether a policy is adoptable according to the RuleSet (ignoring cost).
    /// Note: branch completion policies are automatic and therefore not adoptable in this test.
    /// </summary>
    /// <param name="policy">The Policy to check</param>
    /// <param name="checkEra">Include era test (with false the function returns whether the policy is adoptable now or in the future)</param>
    /// <returns><c>true</c> if the policy can be adopted, <c>false</c> if some rule prevents it (including when it's already adopted)</returns>
    public bool IsAdoptable(Policy policy, bool checkEra = true)
    {
        if (IsAdopted(policy.Name)) return false;
        if (policy.PolicyBranchType == Policy.PolicyBranchTypes.BranchComplete) return false;
        if (!policy.Requires.NotNull().All(it => GetAdoptedPolicies().Contains(it))) return false;
        if (policy.Branch.PurchaseByLevel)
        {
            if (!IdeologyAllows(policy, checkEra)) return false; // B013: an ideology is unlocked by era or Factories, not by the branch era alone
        }
        else if (checkEra && CivInfo.GameInfo.Ruleset.Eras[policy.Branch.Era].EraNumber > CivInfo.GetEraNumber()) return false;
        if (((IHasUniques)policy).GetMatchingUniques(UniqueType.OnlyAvailable, GameContext.IgnoreConditionalsState)
                .Any(it => !it.ConditionalsApply(CivInfo.State))) return false;
        if (((IHasUniques)policy).HasUnique(UniqueType.Unavailable, CivInfo.State)) return false;
        return true;
    }

    /// <summary>
    /// Is any policy, tenet or ideology adoptable now with what the civ has? The pools are separate (B013): free policies buy social policies only, free tenets
    /// tenets only, culture either; choosing an ideology is free. In Civ V this is the Kotlin test: a free policy or enough culture, and something adoptable.
    /// </summary>
    public bool CanAdoptPolicy()
    {
        if (CivInfo.IsSpectator()) return false;
        var cultureEnough = StoredCulture >= GetCultureNeededForNextPolicy();
        var ideologyChoice = CanChooseIdeology();
        if (FreePolicies == 0 && FreeTenets == 0 && !cultureEnough && !ideologyChoice) return false;
        foreach (var policy in GetRulesetPolicies().Values)
        {
            if (IsAdoptable(policy) && (IsIdeologyChoice(policy) || FreeFor(policy) > 0 || cultureEnough)) return true;
        }
        return false;
    }

    /// <summary>B013: can this very policy be adopted now, counting its own pool (free policy, free tenet or culture)?</summary>
    public bool CanAdoptPolicy(Policy policy)
    {
        if (CivInfo.IsSpectator() || !IsAdoptable(policy)) return false;
        return IsIdeologyChoice(policy) || FreeFor(policy) > 0 || StoredCulture >= GetCultureNeededForNextPolicy();
    }

    private static bool IsIdeologyChoice(Policy policy) => policy is PolicyBranch { PurchaseByLevel: true };

    private int FreeFor(Policy policy) => policy.IsTenet ? FreeTenets : FreePolicies;

    /// <summary>B013 (bnw-spec 4.6.2): are ideologies on in this ruleset (ModOptions <c>Enables ideologies</c>)?</summary>
    [JsonIgnore]
    public bool IdeologiesEnabled => ((IHasUniques)CivInfo.GameInfo.Ruleset.ModOptions).HasUnique(UniqueType.EnablesIdeologies);

    /// <summary>
    /// B013 (bnw-spec 4.6.2, <c>@101906a0</c> and the player <c>doTurn</c>): a major civ without an ideology may choose one once its era is past the ideology
    /// branches' era (Industrial, so Modern or later) or it owns the number of buildings that unlock ideologies (3 Factories).
    /// </summary>
    public bool CanChooseIdeology()
    {
        if (Ideology != null || !IdeologiesEnabled || CivInfo.IsSpectator() || !CivInfo.IsMajorCiv()) return false;
        return IdeologyBranches.Any(IdeologyUnlockedByEra) || IdeologyUnlockedByBuildings();
    }

    private bool IdeologyUnlockedByEra(PolicyBranch branch) =>
        CivInfo.GameInfo.Ruleset.Eras[branch.Era].EraNumber < CivInfo.GetEraNumber();

    /// <summary>The building count of <c>Building [n] of these unlocks Ideologies</c> reached (counted over all cities, puppets included).</summary>
    private bool IdeologyUnlockedByBuildings()
    {
        foreach (var building in CivInfo.GameInfo.Ruleset.Buildings.Values)
        {
            var unique = ((IHasUniques)building).GetMatchingUniques(UniqueType.IdeologyUnlockCount, GameContext.IgnoreConditionalsState).FirstOrDefault();
            if (unique == null) continue;
            if (CivInfo.Cities.Count(it => it.CityConstructions.IsBuilt(building.Name)) >= int.Parse(unique.Params[0], System.Globalization.CultureInfo.InvariantCulture))
                return true;
        }
        return false;
    }

    /// <summary>The notification reason when <see cref="CanChooseIdeology"/> holds: the era or the Factories (<c>CHOOSE_IDEOLOGY_ERA</c> / <c>CHOOSE_IDEOLOGY_FACTORIES</c>).</summary>
    public bool IdeologyUnlockedBecauseOfEra() => IdeologyBranches.Any(IdeologyUnlockedByEra);

    /// <summary>
    /// B013: may <paramref name="policy"/>, a member of an ideology branch, be adopted? The branch itself needs <see cref="CanChooseIdeology"/> (the era part is
    /// skipped when <paramref name="checkEra"/> is false); a tenet needs its branch chosen and the level rule (bnw-spec 4.6.3): a Level n+1 tenet only while the
    /// civ has more Level n tenets than Level n+1 ones.
    /// </summary>
    private bool IdeologyAllows(Policy policy, bool checkEra)
    {
        if (!IdeologiesEnabled) return false;
        if (policy is PolicyBranch) return Ideology == null && (!checkEra || CanChooseIdeology());
        if (Ideology != policy.Branch.Name) return false;
        return policy.Level <= 1 || GetTenetCount(policy.Branch, policy.Level - 1) > GetTenetCount(policy.Branch, policy.Level);
    }

    /// <summary>B013: how many tenets of <paramref name="level"/> the civ has adopted in <paramref name="branch"/>.</summary>
    public int GetTenetCount(PolicyBranch branch, int level) =>
        branch.Policies.Count(it => it.Level == level && IsAdopted(it.Name));

    /// <summary>
    /// B013 (bnw-spec 4.6.2): choose <paramref name="branch"/> as the ideology. It is adopted at no cost; the first civ in the game to follow it gets
    /// <see cref="PolicyBranch.FirstAdopterFreePolicies"/> free tenets, the second <see cref="PolicyBranch.SecondAdopterFreePolicies"/>, later ones none.
    /// Deviation: "first" is counted among the civs that follow it now (dead civs included), because there is no adoption history yet (B052 switching).
    /// </summary>
    public void ChooseIdeology(PolicyBranch branch)
    {
        if (!branch.PurchaseByLevel || !IsAdoptable(branch))
            throw new InvalidOperationException($"{branch.Name} cannot be chosen as ideology");
        var earlierAdopters = CivInfo.GameInfo.Civilizations.Count(it => !ReferenceEquals(it, CivInfo) && it.Policies.Ideology == branch.Name);
        Ideology = branch.Name;
        FreeTenets += earlierAdopters switch { 0 => branch.FirstAdopterFreePolicies, 1 => branch.SecondAdopterFreePolicies, _ => 0 };
        ShouldOpenPolicyPicker = FreeTenets > 0;
        AdoptedPolicies.Add(branch.Name);
        AddPolicyToTransients(branch);
        GameEventHooks.PolicyAdopted(CivInfo, branch.Name);

        CivInfo.Cache.UpdateCivResources();
        foreach (var city in CivInfo.Cities)
        {
            city.CityStats.Update(updateCivStats: true);
            city.ReassignPopulationDeferred();
        }
    }

    public void Adopt(Policy policy, bool branchCompletion = false)
    {
        if (IsIdeologyChoice(policy))
        {
            ChooseIdeology((PolicyBranch)policy);
            return;
        }
        if (!branchCompletion)
        {
            if (policy.IsTenet && FreeTenets > 0) FreeTenets--;
            else if (!policy.IsTenet && FreePolicies > 0) FreePolicies--;
            else if (!CivInfo.GameInfo.GameParameters.GodMode)
            {
                var cultureNeededForNextPolicy = GetCultureNeededForNextPolicy();
                if (cultureNeededForNextPolicy > StoredCulture) throw new InvalidOperationException(
                    "Trying to adopt a policy without enough culture????"
                );
                StoredCulture -= cultureNeededForNextPolicy;
                numberOfAdoptedPolicies++;
            }
        }

        AdoptedPolicies.Add(policy.Name);
        AddPolicyToTransients(policy);
        GameEventHooks.PolicyAdopted(CivInfo, policy.Name);

        if (!branchCompletion && !policy.Branch.PurchaseByLevel)
        {
            var branch = policy.Branch;
            if (branch.Policies.Count(it => IsAdopted(it.Name)) == branch.Policies.Count - 1) // All done apart from branch completion
            {
                Adopt(branch.Policies[^1], true); // add branch completion!
            }
        }

        //todo Can this be mapped downstream to a PolicyAction:NotificationAction?
        var triggerNotificationText = $"due to adopting [{policy.Name}]";
        foreach (var unique in policy.UniqueObjects)
        {
            if (!unique.IsTriggerable || unique.HasTriggerConditional() || !unique.ConditionalsApply(CivInfo.State))
                continue;
            var multiplier = unique.GetUniqueMultiplier(CivInfo.State); // evaluated once, like repeat()
            for (var i = 0; i < multiplier; i++)
                UniqueTriggerActivation.TriggerUnique(unique, CivInfo, triggerNotificationText: triggerNotificationText);
        }

        CivInfo.ForEachTriggeredUnique(UniqueType.TriggerUponAdoptingPolicyOrBelief, CivInfo.State, it => it.Params[0] == policy.Name, unique =>
        {
            UniqueTriggerActivation.TriggerUnique(unique, CivInfo, triggerNotificationText: triggerNotificationText);
        });

        CivInfo.Cache.UpdateCivResources();

        // This ALSO has the side-effect of updating the CivInfo statForNextTurn so we don't need to call it explicitly
        foreach (var city in CivInfo.Cities)
        {
            city.CityStats.Update(updateCivStats: true);
            city.ReassignPopulationDeferred();
        }

        if (!CanAdoptPolicy()) ShouldOpenPolicyPicker = false;
    }

    /// <param name="policy"></param>
    /// <param name="branchCompletion">Internal! Do not use in normal calls (used to recursively remove the "complete" object too)</param>
    /// <param name="assumeWasFree">If set, removal does not touch culture progression (numberOfAdoptedPolicies not decremented)</param>
    /// <exception cref="InvalidOperationException">when the given Policy is not adopted</exception>
    // Note: A policy gained as a free one is not marked as such, therefore we need the parameter
    // Note: a negative numberOfAdoptedPolicies would later throw in getCultureNeededForNextPolicy: -1.pow() gives NaN, which throws on toInt... Autosaved!
    public void RemovePolicy(Policy policy, bool branchCompletion = false, bool assumeWasFree = false)
    {
        if (!AdoptedPolicies.Remove(policy.Name))
            throw new InvalidOperationException($"Attempt to remove non-adopted Policy {policy.Name}");

        if (!assumeWasFree && numberOfAdoptedPolicies > 0)
        {
            numberOfAdoptedPolicies -= 1;
        }

        RemovePolicyFromTransients(policy);

        // if a branch is already marked as complete, revert it to incomplete
        if (!branchCompletion && !policy.Branch.PurchaseByLevel)
        {
            var branch = policy.Branch;
            if (branch.Policies.Count(it => IsAdopted(it.Name)) == branch.Policies.Count - 1)
            {
                RemovePolicy(branch.Policies[^1], true);
            }
        }

        CivInfo.Cache.UpdateCivResources();

        // This ALSO has the side-effect of updating the CivInfo statForNextTurn so we don't need to call it explicitly
        foreach (var city in CivInfo.Cities)
        {
            city.CityStats.Update(updateCivStats: true);
            city.ReassignPopulationDeferred();
        }
    }

    /// <summary>
    /// Return the highest priority among the given set of <see cref="PolicyBranch"/>es.
    /// Would return null if the given set is empty.
    /// </summary>
    public int? GetMaxPriority(IReadOnlySet<PolicyBranch> branchesToCompare)
    {
        int? max = null;
        foreach (var (branch, priority) in PriorityMap)
        {
            if (!branchesToCompare.Contains(branch)) continue;
            if (max == null || priority > max) max = priority;
        }
        return max;
    }

    public int GetCultureFromGreatWriter()
    {
        var sum = 0;
        foreach (var culture in cultureOfLast8Turns) sum += culture;
        return KotlinMath.FloatToInt(sum * CivInfo.GameInfo.Speed.CultureCostModifier);
    }

    private void AddCurrentCultureToCultureOfLast8Turns(int culture)
    {
        cultureOfLast8Turns[CivInfo.GameInfo.Turns % 8] = culture;
    }

    public bool AllPoliciesAdopted(bool checkEra) =>
        !GetRulesetPolicies().Values.Any(it => IsAdoptable(it, checkEra));
}
