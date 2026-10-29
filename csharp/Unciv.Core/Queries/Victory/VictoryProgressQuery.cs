using Unciv.Logic.Civilization;
using Unciv.Logic.Tourism;
using Unciv.Models.Ruleset;
using Unciv.View;
using VictoryRuleset = Unciv.Models.Ruleset.Victory;

namespace Unciv.Queries.Victory;

// Text: the milestone's unique text (a translation template, no numbers)
// Type: MilestoneType name
// Status: Completed, Partially (the first incomplete one) or Incomplete, as VictoryScreenOurVictory colors it
// Done/Total: progress numbers where the milestone type has them; Total is null where the civ count is hidden (Kotlin shows "?")
public sealed record MilestoneEntry(string Text, string Type, string Status, int? Done, int? Total);

// Own: the viewer's milestones in order (empty for a spectator, who has no "Our status" page)
// Civs: every major civ that can achieve this victory and that the viewer may see, worst first, as VictoryScreenGlobalVictory lists them;
//   Next is the civ's next milestone, null when it has completed all of them
public sealed record VictoryCivEntry(VictoryCivRef Civ, int MilestonesCompleted, MilestoneEntry? Next);

public sealed record VictoryEntry(string Name, string Header, int TotalMilestones, IReadOnlyList<MilestoneEntry> Own, IReadOnlyList<VictoryCivEntry> Civs);

public sealed record VictoryProgressResult(IReadOnlyList<VictoryEntry> Victories);

// T198: the "Our status" and "Global status" victory screen pages. Wraps VictoryManager and Milestone; the A003 scorers answer how
// attractive a victory is for an AI, not how close a civ is, so they are not used.
// T240 (deliberate deviation, remote-safe): Kotlin lists an anonymous row for every unmet civ. Here a civ appears only when the viewer
// may see it: known, itself, or defeated (VictoryQueryHelpers.Ref names it), and only when the game lets the viewer see other civs'
// stats (HideOtherCivilizationStats off, as CanViewCivStats). So neither the number of unmet civs nor their progress is exposed, and the
// "?" of a hidden civ count is decided from the viewer's knowledge, never from the listed civ's.
[QueryType("victory.Progress")]
public sealed record VictoryProgressQuery : IQuery<VictoryProgressResult>
{
    public string Type => "victory.Progress";

    public VictoryProgressResult Run(GameView view)
    {
        var viewer = view.CivView.Unwrap();
        var game = viewer.GameInfo;
        var majors = VictoryQueryHelpers.MajorCivs(game)
            .Where(it => (viewer.IsSpectator() || VictoryQueryHelpers.Ref(viewer, it).CivName != null) && VictoryQueryHelpers.CanViewCivStats(game, viewer, it)).ToList();
        var result = new List<VictoryEntry>();
        foreach (var victory in view.CivView.GetVictoriesShownInVictoryScreen())
        {
            var own = new List<MilestoneEntry>();
            if (!viewer.IsSpectator())
            {
                bool firstIncomplete = true;
                foreach (var milestone in victory.MilestoneObjects)
                {
                    string status;
                    bool completed = milestone.HasBeenCompletedBy(viewer);
                    if (completed) status = nameof(VictoryRuleset.CompletionStatus.Completed);
                    else if (firstIncomplete)
                    {
                        firstIncomplete = false;
                        status = nameof(VictoryRuleset.CompletionStatus.Partially);
                    }
                    else status = nameof(VictoryRuleset.CompletionStatus.Incomplete);
                    own.Add(Entry(milestone, viewer, viewer, completed, status));
                }
            }

            var civs = majors
                .Where(it => it.VictoryManager.GetAvailableVictories().Any(v => v.Name == victory.Name))
                .OrderBy(it => it.IsDefeated()).ThenBy(it => it.VictoryManager.AmountMilestonesCompleted(victory)) // stable, like sortedWith
                .Select(it =>
                {
                    var next = it.VictoryManager.GetNextMilestone(victory);
                    return new VictoryCivEntry(VictoryQueryHelpers.Ref(viewer, it), it.VictoryManager.AmountMilestonesCompleted(victory),
                        next == null ? null : Entry(next, it, viewer, false, nameof(VictoryRuleset.CompletionStatus.Partially)));
                }).ToList();
            result.Add(new VictoryEntry(victory.Name, victory.VictoryScreenHeader, victory.MilestoneObjects.Count, own, civs));
        }
        return new VictoryProgressResult(result);
    }

    private static MilestoneEntry Entry(Milestone milestone, Civilization civ, Civilization viewer, bool completed, string status)
    {
        var (done, total) = Progress(milestone, civ, viewer, completed);
        return new MilestoneEntry(milestone.UniqueDescription, (milestone.Type ?? throw new InvalidOperationException("Milestone has no type")).ToString(),
            status, done, total);
    }

    // The numbers of Milestone.GetVictoryScreenButtonHeaderText, without the text.
    private static (int? Done, int? Total) Progress(Milestone milestone, Civilization civ, Civilization viewer, bool completed)
    {
        var game = civ.GameInfo;
        switch (milestone.Type)
        {
            case MilestoneType.CompletePolicyBranches:
                {
                    int total = int.Parse(milestone.Params[0], System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture);
                    return (completed ? total : civ.GetCompletedPolicyBranchesCount(), total);
                }
            case MilestoneType.HaveCountable:
                {
                    int total = milestone.GetCountableToDo();
                    return (completed ? total : Math.Min(milestone.GetCountableDone(civ), total), total);
                }
            case MilestoneType.CaptureAllCapitals:
                {
                    int total = Milestone.CivsWithPotentialCapitalsToOwn(game).Count;
                    return (completed ? total : Milestone.OriginalMajorCapitalsOwned(civ), viewer.ShouldHideCivCount() ? null : total);
                }
            case MilestoneType.DestroyAllPlayers:
                {
                    int total = game.Civilizations.Count(it => it.IsMajorCiv()) - 1; // Don't count yourself
                    int done = completed ? total : total - game.GetAliveMajorCivs().Count(it => !ReferenceEquals(it, civ));
                    return (done, viewer.ShouldHideCivCount() ? null : total);
                }
            case MilestoneType.MoreCountableThanEachPlayer:
                {
                    var relevant = game.Civilizations.Where(it => milestone.GetMoreCountableThanOtherCivRelevant(civ, it)).ToList();
                    int done = relevant.Count(it => milestone.GetMoreCountableThanOtherCivPercent(civ, it) > 100f);
                    return (done, viewer.ShouldHideCivCount() ? null : relevant.Count);
                }
            case MilestoneType.AddedSSPartsInCapital:
                {
                    var incomplete = milestone.ParentVictory.RequiredSpaceshipPartsAsCounter.Clone();
                    int total = incomplete.SumValues();
                    incomplete.Remove(civ.VictoryManager.CurrentsSpaceshipParts);
                    return (total - incomplete.SumValues(), total);
                }
            case MilestoneType.InfluentialOverAllCivs:
                {
                    int total = InfluenceRules.VictoryRivals(civ).Count;
                    return (completed ? total : InfluenceRules.InfluentialCount(civ), viewer.ShouldHideCivCount() ? null : total);
                }
            case MilestoneType.WorldReligion:
                {
                    int total = game.Civilizations.Count(it => it.IsMajorCiv() && it.IsAlive()) - 1; // Don't count yourself
                    var religion = civ.ReligionManager.Religion;
                    int done;
                    if (completed) done = total;
                    else if (religion == null) done = 0;
                    else if (religion.IsPantheon()) done = 1;
                    else done = game.Civilizations.Count(it => it.IsMajorCiv() && it.IsAlive() && it.ReligionManager.IsMajorityReligionForCiv(religion));
                    return (done, total);
                }
            default: // BuiltBuilding, BuildingBuiltGlobally, WinDiplomaticVote, WinWorldLeaderVote, ScoreAfterTimeOut: no numbers
                return (null, null);
        }
    }
}
