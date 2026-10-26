using Unciv.UI.Screens.VictoryScreen;
using Unciv.View;

namespace Unciv.Queries.Victory;

public sealed record RankingEntry(VictoryCivRef Civ, int Value);

public sealed record RankingColumn(string Type, string Label, IReadOnlyList<RankingEntry> Entries);

// T198: the Rankings page. One column per RankingType (the additional ones only if the game shows them), major civs the viewer
// may view (GameParameters.HideOtherCivilizationStats), best first, defeated civs last, ties by civ name. Live values, like Kotlin.
// Not available when the game does not show the rankings tab.
[QueryType("victory.Rankings")]
public sealed record RankingsQuery : IQuery<IReadOnlyList<RankingColumn>>
{
    public string Type => "victory.Rankings";

    public IReadOnlyList<RankingColumn> Run(GameView view)
    {
        var viewer = view.CivView.Unwrap();
        var game = viewer.GameInfo;
        VictoryQueryHelpers.RequireTab(game, viewer, game.GameParameters.ShowRankings, "Rankings");
        var civs = VictoryQueryHelpers.MajorCivs(game).Where(it => VictoryQueryHelpers.CanViewCivStats(game, viewer, it)).ToList();
        return VictoryQueryHelpers.Categories(game).Select(category => new RankingColumn(category.ToString(), category.Label(),
            civs.Select(it => (Civ: it, Value: VictoryQueryHelpers.StatForRanking(viewer, it, category)))
                .OrderByDescending(it => it.Civ.IsDefeated() ? int.MinValue : it.Value)
                .ThenBy(it => it.Civ.CivName, StringComparer.Ordinal)
                .Select(it => new RankingEntry(VictoryQueryHelpers.Ref(viewer, it.Civ), it.Value)).ToList())).ToList();
    }
}
