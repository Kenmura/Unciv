using Unciv.UI.Screens.VictoryScreen;
using Unciv.View;

namespace Unciv.Queries.Victory;

public sealed record HistoryPoint(int Turn, int Value);

public sealed record HistorySeries(VictoryCivRef Civ, IReadOnlyList<HistoryPoint> Points);

public sealed record HistoryResult(string Category, IReadOnlyList<HistorySeries> Series);

// Category: a RankingType name. FromTurn/ToTurn: optional inclusive zoom range (VictoryScreenCharts zoom)
// T198: the data of the Charts page. Per viewable major civ the recorded CivRankingHistory values in turn order, then the live value
// of the current turn for living civs (both as in Kotlin getLineChartData, which can list the current turn twice).
// Not available when the game does not show charts. Unlike Kotlin the tab is not hidden for a history of fewer than 2 turns (a UI nicety): the series are just short.
[QueryType("victory.History")]
public sealed record HistoryQuery(string Category = nameof(RankingType.Score), int? FromTurn = null, int? ToTurn = null) : IQuery<HistoryResult>
{
    public string Type => "victory.History";

    public HistoryResult Run(GameView view)
    {
        var viewer = view.CivView.Unwrap();
        var game = viewer.GameInfo;
        if (!Enum.GetNames<RankingType>().Contains(Category, StringComparer.Ordinal)
            || !RankingTypes.FilteredEntries(game.GameParameters).Contains(Enum.Parse<RankingType>(Category)))
            throw new QueryException(VictoryQueryCodes.BadRequest, $"Unknown ranking type \"{Category}\"");
        var category = Enum.Parse<RankingType>(Category);
        VictoryQueryHelpers.RequireTab(game, viewer, game.GameParameters.ShowCharts, "Charts");
        bool InZoom(int turn) => turn >= (FromTurn ?? int.MinValue) && turn <= (ToTurn ?? int.MaxValue);

        var series = new List<HistorySeries>();
        foreach (var civ in VictoryQueryHelpers.MajorCivs(game).Where(it => VictoryQueryHelpers.CanViewCivStats(game, viewer, it)))
        {
            var points = civ.StatsHistory
                .Where(it => InZoom(it.Key) && it.Value.ContainsKey(category))
                .OrderBy(it => it.Key)
                .Select(it => new HistoryPoint(it.Key, it.Value[category])).ToList();
            if (points.Count == 0) continue;
            if (!civ.IsDefeated() && InZoom(game.Turns) && (ReferenceEquals(civ, viewer) || viewer.IsSpectator() || game.VictoryData != null))
                points.Add(new HistoryPoint(game.Turns, civ.GetStatForRanking(category)));
            series.Add(new HistorySeries(VictoryQueryHelpers.Ref(viewer, civ), points));
        }
        return new HistoryResult(category.ToString(), series);
    }
}
