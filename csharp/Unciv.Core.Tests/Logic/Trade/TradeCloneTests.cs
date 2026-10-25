using Unciv.Logic.Trade;
using TradeObject = Unciv.Logic.Trade.Trade;

namespace Unciv.Core.Tests.Logic.Trading;

// T167 (found by the L1 soak): Trade.Clone must not share TradeOffer instances, because the turn handling decrements their Duration in place.
public class TradeCloneTests
{
    [Fact]
    public void CloneDoesNotShareOffersWithTheOriginal()
    {
        var trade = new TradeObject();
        trade.OurOffers.Add(new TradeOffer("Open Borders", TradeOfferType.Agreement, 1, 5));
        trade.TheirOffers.Add(new TradeOffer("Open Borders", TradeOfferType.Agreement, 1, 5));
        var clone = trade.Clone();
        clone.OurOffers[0].Duration--;
        clone.TheirOffers[0].Duration--;
        clone.OurOffers[0].Amount = 9;
        Assert.Equal(5, trade.OurOffers[0].Duration);
        Assert.Equal(5, trade.TheirOffers[0].Duration);
        Assert.Equal(1, trade.OurOffers[0].Amount);
        Assert.Equal(4, clone.OurOffers[0].Duration);
        Assert.Equal(4, clone.TheirOffers[0].Duration);
    }
}
