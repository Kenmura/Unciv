using Unciv.Core.Tests.Support;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using Unciv.Logic.Trade;

namespace Unciv.Core.Tests.Logic;

// T219: a Civilization clone (hence GameInfo.Clone) must not share mutable state with its source, or playing the clone ages the original.
[Collection("RulesetCache")]
public class CivilizationCloneIsolationTests
{
    [Fact]
    public void CloneDoesNotShareTradeRequestsPopupAlertsNeutralRoadsOrNotificationLogs()
    {
        var game = new TestGame();
        var civ = game.AddCiv();
        civ.TradeRequests.Clear();
        civ.PopupAlerts.Clear();
        civ.NeutralRoads.Clear();
        civ.NotificationsLogs.Clear();
        civ.TradeRequests.Add(new TradeRequest("Other", new Trade()));
        civ.PopupAlerts.Add(new PopupAlert(AlertType.DemandToStopSettlingCitiesNear, "x"));
        civ.NeutralRoads.Add(new HexCoord(1, 1));
        var log = new Civilization.NotificationsLog(3);
        log.Notifications.Add(new Notification());
        civ.NotificationsLogs.Add(log);

        var clone = civ.Clone();
        clone.TradeRequests[0].Trade.OurOffers.Add(new TradeOffer("Gold", TradeOfferType.Gold, 5, 1));
        clone.PopupAlerts[0].Value = "changed";
        clone.NeutralRoads.Add(new HexCoord(2, 2));
        clone.NotificationsLogs[0].Notifications.Clear();

        Assert.Empty(civ.TradeRequests[0].Trade.OurOffers);
        Assert.Equal("x", civ.PopupAlerts[0].Value);
        Assert.Single(civ.NeutralRoads);
        Assert.Single(civ.NotificationsLogs[0].Notifications);
        Assert.Equal(3, clone.NotificationsLogs[0].Turn);
    }
}
