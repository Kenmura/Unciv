using Unciv.Assets;
using Unciv.Queries.Empire;

namespace Unciv.Core.Tests.Assets;

public class RivalRibbonModelTests
{
    private static OverviewCiv Civ(string name, bool cityState = false, bool defeated = false, bool war = false, params (string Key, int Value)[] rankings) =>
        new(name, cityState, defeated, war, rankings.ToDictionary(it => it.Key, it => it.Value));

    private static EmpireOverviewResult Overview(params OverviewCiv[] known) =>
        new(1, "Babylon", 0, 0f, 0f, 0f, 0f, 0, 1, 1, 0, 0, 0, new Dictionary<string, double>(), new Dictionary<string, int>(), known);

    private static readonly (string, int)[] Everything = [("Technologies", 7), ("Culture", 120), ("Gold", 345), ("Force", 88), ("Score", 5)];

    [Fact]
    public void OnlyKnownLivingMajorCivsGetARow()
    {
        var overview = Overview(Civ("Rome", rankings: Everything), Civ("Geneva", cityState: true), Civ("Greece", defeated: true), Civ("Persia", war: true));
        var rows = RivalRibbonModel.Rows(overview, new Dictionary<string, TradeOptionsResult?>());
        Assert.Equal(["Rome", "Persia"], rows.Select(it => it.CivName));
        Assert.True(rows[1].AtWar);
    }

    [Fact]
    public void FiguresAreTheEnginesRankingsInOrder()
    {
        var row = RivalRibbonModel.Rows(Overview(Civ("Rome", rankings: Everything)), new Dictionary<string, TradeOptionsResult?>()).Single();
        Assert.Equal(["Science", "Culture", "Gold", "Military"], row.Figures.Select(it => it.Id));
        Assert.Equal(["7", "120", "345", "88"], row.Figures.Select(it => it.Text));
        Assert.All(row.Figures, it => Assert.True(it.Known));
        Assert.Equal("icon/stat/Science", row.Figures[0].Icon.ToString());
        Assert.Equal("icon/ui/Shield", row.Figures[3].Icon.ToString());
    }

    [Fact]
    public void HiddenRankingsAreThePlaceholder()
    {
        var row = RivalRibbonModel.Rows(Overview(Civ("Rome")), new Dictionary<string, TradeOptionsResult?>()).Single();
        Assert.All(row.Figures, it => Assert.Equal(("?", false), (it.Text, it.Known)));
        Assert.Null(row.Luxuries);
    }

    [Fact]
    public void OnlyOneRankingMissingKeepsTheOthers()
    {
        var row = RivalRibbonModel.Rows(Overview(Civ("Rome", rankings: [("Gold", 3)])), new Dictionary<string, TradeOptionsResult?>()).Single();
        Assert.Equal(["?", "?", "3", "?"], row.Figures.Select(it => it.Text));
    }

    [Fact]
    public void LuxuriesAreTheTradableLuxuryOffersInOrder()
    {
        var options = new TradeOptionsResult("Rome",
            [],
            [
                new TradeOfferEntry("Wine", "Luxury_Resource", 1, 30, true),
                new TradeOfferEntry("Iron", "Strategic_Resource", 2, 30, true),
                new TradeOfferEntry("Silk", "Luxury_Resource", 0, 30, false),
                new TradeOfferEntry("Gold", "Gold", 50, -1, true),
                new TradeOfferEntry("Pearls", "Luxury_Resource", 1, 30, true),
            ], false);
        Assert.Equal(["Wine", "Pearls"], RivalRibbonModel.Luxuries(options));
        var row = RivalRibbonModel.Rows(Overview(Civ("Rome", rankings: Everything)), new Dictionary<string, TradeOptionsResult?> { ["Rome"] = options }).Single();
        Assert.Equal(["Wine", "Pearls"], row.Luxuries);
    }

    [Fact]
    public void NoOverviewMeansNoRows() => Assert.Empty(RivalRibbonModel.Rows(null, new Dictionary<string, TradeOptionsResult?>()));

    [Theory]
    [InlineData(0, "0")]
    [InlineData(-5, "-5")]
    [InlineData(9999, "9999")]
    [InlineData(10000, "10k")]
    [InlineData(12345, "12.3k")]
    [InlineData(-12345, "-12.3k")]
    [InlineData(int.MaxValue, "2147483.6k")]
    public void CompactShortensThousands(int value, string expected) => Assert.Equal(expected, RivalRibbonModel.Compact(value));
}
