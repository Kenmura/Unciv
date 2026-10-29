namespace Unciv.Models.Ruleset;

// core/src/com/unciv/models/ruleset/PolicyBranch.kt
public class PolicyBranch : Policy
{
    public List<Policy> Policies { get; set; } = [];
    public Dictionary<string, int> Priorities { get; set; } = [];
    public string Era { get; set; } = "";

    /// <summary>
    /// Not in Kotlin (B013, bnw-spec 4.6): an ideology. Its <see cref="Policies"/> are the tenets (no opener beyond the branch itself, no completion bonus,
    /// no prerequisites), bought by <see cref="Policy.Level"/>; the branch policy itself is "adopted" by choosing the ideology, which costs nothing.
    /// BNW column <c>PolicyBranchTypes.PurchaseByLevel</c>.
    /// </summary>
    public bool PurchaseByLevel { get; set; }

    /// <summary>Free tenets for the first civ in the game to adopt this ideology (BNW <c>FirstAdopterFreePolicies</c>).</summary>
    public int FirstAdopterFreePolicies { get; set; }

    /// <summary>Free tenets for the second civ to adopt it (BNW <c>SecondAdopterFreePolicies</c>).</summary>
    public int SecondAdopterFreePolicies { get; set; }
}
