namespace Unciv.Models.Ruleset;

// core/src/com/unciv/models/ruleset/PolicyBranch.kt
public class PolicyBranch : Policy
{
    public List<Policy> Policies { get; set; } = [];
    public Dictionary<string, int> Priorities { get; set; } = [];
    public string Era { get; set; } = "";
}
