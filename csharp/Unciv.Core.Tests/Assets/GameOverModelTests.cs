using Unciv.Assets;
using Unciv.Queries.Victory;

namespace Unciv.Core.Tests.Assets;

public class GameOverModelTests
{
    private static VictoryCivRef Civ(string? name, bool defeated = false, bool viewer = false) => new(name, defeated, viewer);

    private static GameOverResult Result(string status) => new(status, "Rome", "Domination", 40, "text");

    [Fact]
    public void PlayingShowsNothing()
    {
        Assert.False(GameOverModel.IsShown(Result("Playing")));
        Assert.True(GameOverModel.IsShown(Result("Won")));
        Assert.True(GameOverModel.IsShown(Result("Defeated")));
    }

    [Fact]
    public void WonAndLostWordTheTitleAndOfferOneMoreTurn()
    {
        var won = GameOverModel.Build(Result("Won"), null, null);
        Assert.Equal("You have won a [Domination] Victory!", won.Title);
        Assert.Equal("text", won.Text);
        Assert.Equal(["OneMoreTurn", "MainMenu"], won.Buttons.Select(it => it.Id));
        var lost = GameOverModel.Build(Result("Lost"), null, null);
        Assert.Equal("[Rome] has won a [Domination] Victory!", lost.Title);
        Assert.Equal([GameOverButtonKind.OneMoreTurn, GameOverButtonKind.MainMenu], lost.Buttons.Select(it => it.Kind));
    }

    [Fact]
    public void DefeatOffersToKeepWatchingNotOneMoreTurn()
    {
        var defeated = GameOverModel.Build(new GameOverResult("Defeated", null, null, 0, "gone"), null, null);
        Assert.Equal("Defeated", defeated.Title);
        Assert.Equal([GameOverButtonKind.KeepWatching, GameOverButtonKind.MainMenu], defeated.Buttons.Select(it => it.Kind));
        Assert.Equal("", GameOverModel.Build(Result("Playing"), null, null).Title);
    }

    [Fact]
    public void RankingSharesPlacesForTiesAndListsTheDefeatedLastWithoutAPlace()
    {
        var column = new RankingColumn("Score", "Score",
        [
            new RankingEntry(Civ("Babylon", viewer: true), 100),
            new RankingEntry(Civ("Rome"), 100),
            new RankingEntry(Civ("Greece"), 50),
            new RankingEntry(Civ(null, defeated: true, viewer: true), 7),
        ]);
        var rows = GameOverModel.Ranking([new RankingColumn("Gold", "Gold", []), column]);
        Assert.Equal([1, 1, 3, 0], rows.Select(it => it.Place));
        Assert.Equal(["Babylon", "Rome", "Greece", "???"], rows.Select(it => it.Civ));
        Assert.True(rows[0].IsViewer);
        Assert.True(rows[3].IsDefeated);
        Assert.True(rows[3].IsViewer);
        Assert.False(rows[1].IsViewer);
        Assert.Equal("1. Babylon: 100", GameOverModel.RowText(rows[0]));
        Assert.Equal("??? (defeated): 7", GameOverModel.RowText(rows[3]));
    }

    [Fact]
    public void RankingFallsBackToTheFirstColumnAndIsEmptyWithoutAny()
    {
        var gold = new RankingColumn("Gold", "Gold", [new RankingEntry(Civ("Babylon"), 5)]);
        Assert.Equal(5, Assert.Single(GameOverModel.Ranking([gold])).Value);
        Assert.Empty(GameOverModel.Ranking(null));
        Assert.Empty(GameOverModel.Ranking([]));
    }

    [Fact]
    public void GraphScalesIntoTheUnitSquareWithHighestValuesOnTop()
    {
        var history = new HistoryResult("Score",
        [
            new HistorySeries(Civ("Babylon", viewer: true), [new HistoryPoint(30, 40), new HistoryPoint(10, 0), new HistoryPoint(20, 10)]),
            new HistorySeries(Civ("Rome", defeated: true), [new HistoryPoint(10, 20), new HistoryPoint(40, 5)]),
            new HistorySeries(Civ("Empty"), []),
        ]);
        var graph = GameOverModel.Graph(history);
        Assert.Equal((10, 40, 0, 40), (graph.FirstTurn, graph.LastTurn, graph.MinValue, graph.MaxValue));
        Assert.Equal(["Babylon", "Rome"], graph.Lines.Select(it => it.Civ));
        Assert.Equal([new GraphPoint(0f, 1f), new GraphPoint(10f / 30f, 0.75f), new GraphPoint(20f / 30f, 0f)], graph.Lines[0].Points);
        Assert.True(graph.Lines[0].IsViewer);
        Assert.True(graph.Lines[1].IsDefeated);
        Assert.Equal(new GraphPoint(0f, 0.5f), graph.Lines[1].Points[0]);
    }

    [Fact]
    public void GraphDrawsASinglePointAtTheMiddleAndNothingWithoutData()
    {
        var one = GameOverModel.Graph(new HistoryResult("Score", [new HistorySeries(Civ("Babylon"), [new HistoryPoint(3, 9)])]));
        Assert.Equal(new GraphPoint(0.5f, 0.5f), Assert.Single(Assert.Single(one.Lines).Points));
        Assert.Empty(GameOverModel.Graph(null).Lines);
        Assert.Empty(GameOverModel.Graph(new HistoryResult("Score", [])).Lines);
    }
}
