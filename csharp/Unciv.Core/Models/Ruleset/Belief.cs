using Unciv.Logic;
using Unciv.Models.Ruleset.Unique;

namespace Unciv.Models.Ruleset;

// core/src/com/unciv/models/ruleset/Belief.kt
// Not ported yet: the Civilopedia members (getCivilopediaTextLines, getBeliefsMatching, getCivilopediaBeliefsEntry...), they need Ruleset (T034)
// and the Civilopedia text types.
public class Belief : RulesetObject
{
    public Belief()
    {
    }

    public Belief(BeliefType type)
    {
        Type = type;
    }

    public BeliefType Type { get; set; } = BeliefType.None;

    public override UniqueTarget GetUniqueTarget() =>
        Type.IsFounder() ? UniqueTarget.FounderBelief : UniqueTarget.FollowerBelief;

    public bool MatchesFilter(string filter, GameContext? state = null, bool multiFilter = true)
    {
        var self = (IHasUniques)this;
        if (multiFilter)
        {
            return MultiFilter.Filter(filter, it =>
                MatchesSingleFilter(it, state) ||
                (state != null && self.HasTagUnique(it, state)) ||
                (state == null && self.HasTagUnique(it)));
        }
        return MatchesSingleFilter(filter, state) ||
            (state != null && self.HasTagUnique(filter, state)) ||
            (state == null && self.HasTagUnique(filter));
    }

    public bool MatchesSingleFilter(string filter, GameContext? state = null)
    {
        _ = state;
        if (filter == Name) return true;
        if (Constants.All.Contains(filter)) return true;
        return filter == Type.ToString(); // Belief type
    }

    public override string MakeLink() => $"Belief/{Name}";
}

/// <summary>
/// Subtypes of Beliefs - directly deserialized.
/// Kotlin <c>isFollower</c> (Uniques processed per city) and <c>isFounder</c> (Uniques processed globally for founding civ only)
/// are in <see cref="BeliefTypeExtensions"/>.
/// </summary>
public enum BeliefType
{
    None,
    Pantheon,
    Founder,
    Follower,
    Enhancer,
    Any,
}

public static class BeliefTypeExtensions
{
    public static string GetColor(this BeliefType type) => type switch
    {
        BeliefType.Pantheon => "#44c6cc",
        BeliefType.Founder => "#c00000",
        BeliefType.Follower => "#ccaa44",
        BeliefType.Enhancer => "#72cc45",
        _ => "",
    };

    public static bool IsFollower(this BeliefType type) => type is BeliefType.Pantheon or BeliefType.Follower;

    public static bool IsFounder(this BeliefType type) => type is BeliefType.Founder or BeliefType.Enhancer;

    public static string? GetSubCategory(this BeliefType type) => type is BeliefType.None or BeliefType.Any ? null : type.ToString();
}
