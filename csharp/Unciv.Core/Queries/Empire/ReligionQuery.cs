using Unciv.Logic.Civilization.Managers;
using Unciv.Models.Ruleset;
using Unciv.View;

namespace Unciv.Queries.Empire;

public sealed record BeliefEntry(string Name, string Type);

// State: ReligionState of the viewer (None, Pantheon, FoundingReligion, Religion, EnhancingReligion, EnhancedReligion)
// ToChoose: Beliefs still to pick per type, for the current state (founding, enhancing, or free beliefs), by BeliefType name
// Available: Beliefs nobody holds yet, per type name (Pantheon, Founder, Follower, Enhancer), in ruleset order
// Slots: The belief type of each slot in the order FoundReligion / ChooseBeliefs expect their Beliefs (ToChoose is sorted by name and merges founding with free beliefs, so it cannot give that order); empty when nothing is to be chosen
// AvailableIcons: The ruleset's religion icons no religion of the game uses yet (what FoundReligion.ReligionName accepts)
public sealed record ReligionResult(bool Enabled, string State, int Faith, string? ReligionName, string? DisplayName,
    IReadOnlyList<BeliefEntry> OwnBeliefs, bool CanFoundPantheon, bool CanExpandPantheon, int FaithForPantheon,
    IReadOnlyDictionary<string, int> ToChoose, IReadOnlyDictionary<string, IReadOnlyList<string>> Available,
    IReadOnlyList<string> Slots, IReadOnlyList<string> AvailableIcons);

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
            foreach (var type in new[] { BeliefType.Pantheon, BeliefType.Founder, BeliefType.Follower, BeliefType.Enhancer, BeliefType.Reformation })
            {
                // Rulesets without a Reformation tier (G&K) get no key, so their overview is unchanged
                if (type == BeliefType.Reformation && !civ.GameInfo.Ruleset.Beliefs.Values.Any(it => it.Type == type)) continue;
                available[type.ToString()] = civ.GameInfo.Ruleset.Beliefs.Values
                    .Where(it => it.Type == type && manager.GetReligionWithBelief(it) == null)
                    .Select(it => it.Name).ToList();
            }

        var slotCounters = manager.ReligionState switch
        {
            ReligionState.FoundingReligion => civView.GetBeliefsToChooseAtFounding(),
            ReligionState.EnhancingReligion => civView.GetBeliefsToChooseAtEnhancing(),
            _ => civView.HasFreeBeliefs() ? civView.FreeBeliefsAsEnums() : null,
        };
        var slots = new List<string>();
        if (slotCounters != null)
            foreach (var (type, count) in slotCounters)
                for (int i = 0; i < count; i++) slots.Add(type.ToString());
        var icons = civView.IsReligionEnabled()
            ? civ.GameInfo.Ruleset.Religions.Where(it => !civ.GameInfo.Religions.ContainsKey(it)).ToList()
            : [];

        return new ReligionResult(civView.IsReligionEnabled(), manager.ReligionState.ToString(), manager.StoredFaith, religion?.Name,
            religion?.GetReligionDisplayName(), own, civView.CanFoundPantheon(), civView.CanExpandPantheon(), manager.FaithForPantheon(),
            toChoose, available, slots, icons);
    }
}
