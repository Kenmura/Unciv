using Unciv.Logic.Civilization;
using Unciv.UI.Screens.VictoryScreen;
using Unciv.Utils;
using Unciv.View;

namespace Unciv.Queries.Victory;

// Rank: 1-based position of the viewer among the living major civs (0 when the viewer is not among them)
// Value: the viewer's entry; null when the viewer is defeated or not a major civ
// Best/Worst: null when no major civ is left; Average: rounded half up
public sealed record DemographicColumn(string Type, string Label, int Rank, RankingEntry? Value, RankingEntry? Best, int Average, RankingEntry? Worst);

// T198: the Demographics page. Uses the values frozen at the end of the last completed turn (CivRankingHistory), so that mid-turn
// changes cannot probe other civs; falls back to the live value when there is no history. Like Kotlin, all living major civs
// count, HideOtherCivilizationStats does not apply, and unmet civs are anonymous.
[QueryType("victory.Demographics")]
public sealed record DemographicsQuery : IQuery<IReadOnlyList<DemographicColumn>>
{
    public string Type => "victory.Demographics";

    public IReadOnlyList<DemographicColumn> Run(GameView view)
    {
        var viewer = view.CivView.Unwrap();
        var game = viewer.GameInfo;
        VictoryQueryHelpers.RequireTab(game, viewer, game.GameParameters.ShowDemographics, "Demographics");
        var majors = VictoryQueryHelpers.MajorCivs(game).Where(it => it.IsAlive() || ReferenceEquals(it, viewer)).ToList();
        var columns = new List<DemographicColumn>();
        foreach (var category in VictoryQueryHelpers.Categories(game))
        {
            var sorted = majors.Select(it => (Civ: it, Value: VictoryQueryHelpers.StatFromHistory(it, category))).OrderByDescending(it => it.Value).ToList();
            RankingEntry Entry((Civilization Civ, int Value) it) => new(VictoryQueryHelpers.Ref(viewer, it.Civ), it.Value);
            bool hasViewer = sorted.Any(it => ReferenceEquals(it.Civ, viewer));
            var own = hasViewer && !viewer.IsDefeated() && viewer.IsMajorCiv() ? Entry(sorted.First(it => ReferenceEquals(it.Civ, viewer))) : null;
            int average = sorted.Count == 0 ? 0 : KotlinMath.RoundToInt(unchecked(sorted.Sum(it => it.Value)) / (float)sorted.Count);
            columns.Add(new DemographicColumn(category.ToString(), category.Label(),
                sorted.FindIndex(it => ReferenceEquals(it.Civ, viewer)) + 1, own,
                sorted.Count == 0 ? null : Entry(sorted[0]), average, sorted.Count == 0 ? null : Entry(sorted[^1])));
        }
        return columns;
    }
}
