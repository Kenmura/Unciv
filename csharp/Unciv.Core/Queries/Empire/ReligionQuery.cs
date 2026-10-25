using Unciv.Logic.Civilization.Managers;
using Unciv.Models.Ruleset;
using Unciv.View;

namespace Unciv.Queries.Empire;

public sealed record BeliefEntry(string Name, string Type);

// State: ReligionState of the viewer (None, Pantheon, FoundingReligion, Religion, EnhancingReligion, EnhancedReligion)
// ToChoose: Beliefs still to pick per type, for the current state (founding, enhancing, or free beliefs), by BeliefType name
// Available: Beliefs nobody holds yet, per type name (Pantheon, Founder, Follower, Enhancer), in ruleset order
public sealed record ReligionResult(bool Enabled, string State, int Faith, string? ReligionName, string? DisplayName,
    IReadOnlyList<BeliefEntry> OwnBeliefs, bool CanFoundPantheon, bool CanExpandPantheon, int FaithForPantheon,
    IReadOnlyDictionary<string, int> ToChoose, IReadOnlyDictionary<string, IReadOnlyList<string>> Available);

// T197: the religion panel and belief pickers of the viewer. Wraps ReligionManager (GetBeliefsToChooseAt*, FreeBeliefsAsEnums,
// NumberOfBeliefsAvailable logic); a belief is available when no religion of the game has it (GetReligionWithBelief).
[QueryType("empire.Religion")]
public sealed record ReligionQuery : IQuery<ReligionResult>
{
    public string Type => "empire.Religion";

    public ReligionResult Run(GameView view)
    {
        var civView = view.CivView;
        var civ = civView.Unwrap();
        var manager = civ.ReligionManager;
        var religion = manager.Religion;
        var own = religion == null ? [] : religion.GetAllBeliefsOrdered().Select(it => new BeliefEntry(it.Name, it.Type.ToString())).ToList();
        var toChoose = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var counters = manager.ReligionState switch
        {
            ReligionState.FoundingReligion => civView.GetBeliefsToChooseAtFounding(),
            ReligionState.EnhancingReligion => civView.GetBeliefsToChooseAtEnhancing(),
            _ => null,
        };
        if (counters != null)
            foreach (var (type, count) in counters)
                toChoose[type.ToString()] = count;
        if (civView.HasFreeBeliefs())
            foreach (var (type, count) in civView.FreeBeliefsAsEnums())
                toChoose[type.ToString()] = toChoose.GetValueOrDefault(type.ToString()) + count;

        var available = new SortedDictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        if (civView.IsReligionEnabled())
            foreach (var type in new[] { BeliefType.Pantheon, BeliefType.Founder, BeliefType.Follower, BeliefType.Enhancer })
                available[type.ToString()] = civ.GameInfo.Ruleset.Beliefs.Values
                    .Where(it => it.Type == type && manager.GetReligionWithBelief(it) == null)
                    .Select(it => it.Name).ToList();

        return new ReligionResult(civView.IsReligionEnabled(), manager.ReligionState.ToString(), manager.StoredFaith, religion?.Name,
            religion?.GetReligionDisplayName(), own, civView.CanFoundPantheon(), civView.CanExpandPantheon(), manager.FaithForPantheon(),
            toChoose, available);
    }
}
