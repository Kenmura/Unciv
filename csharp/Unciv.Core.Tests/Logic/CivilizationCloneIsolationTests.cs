using Unciv.Core.Tests.Support;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using Unciv.Logic.Map.MapUnits;
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

// T219: City.Clone used to hand its source's tile sets and promotion maps to the clone.
[Collection("RulesetCache")]
public class CityCloneIsolationTests
{
    [Fact]
    public void CloneDoesNotShareTileSetsOrPromotionMapsAndKeepsTheirOrder()
    {
        var game = new TestGame();
        game.MakeHexagonalMap(3);
        var civ = game.AddCiv();
        var city = game.AddCity(civ, game.GetTile(0, 0));
        for (var i = 0; i < 40; i++) city.Tiles.Add(new HexCoord(i % 7 - 3, i / 7 - 3));
        city.WorkedTiles.Add(new HexCoord(1, 1));
        city.LockedTiles.Add(new HexCoord(2, 2));
        city.UnitShouldUseSavedPromotion.Put("Warrior", true);
        city.UnitToPromotions.Put("Warrior", new UnitPromotions());
        city.UnitToPromotions["Warrior"].Promotions.Add("Shock I");
        var tiles = city.Tiles.ToList();
        var worked = city.WorkedTiles.Count;
        var locked = city.LockedTiles.Count;

        var clone = city.Clone();
        Assert.Equal(tiles, clone.Tiles.ToList()); // the very same iteration order, not just the same members
        clone.Tiles.Add(new HexCoord(9, 9));
        clone.WorkedTiles.Add(new HexCoord(3, 3));
        clone.LockedTiles.Add(new HexCoord(4, 4));
        clone.UnitShouldUseSavedPromotion.Put("Archer", false);
        clone.UnitToPromotions["Warrior"].Promotions.Add("Shock II");
        clone.UnitToPromotions.Put("Archer", new UnitPromotions());

        Assert.Equal(tiles, city.Tiles.ToList());
        Assert.Equal(worked, city.WorkedTiles.Count);
        Assert.Equal(locked, city.LockedTiles.Count);
        Assert.Single(city.UnitShouldUseSavedPromotion);
        Assert.Single(city.UnitToPromotions);
        Assert.Equal(["Shock I"], city.UnitToPromotions["Warrior"].Promotions);
    }

    [Fact]
    public void CloneKeepsWorkedTilesAliasedToLockedTilesWhenTheSourceHasThem()
    {
        var game = new TestGame();
        game.MakeHexagonalMap(2);
        var city = game.AddCity(game.AddCiv(), game.GetTile(0, 0));
        city.WorkedTiles = city.LockedTiles;

        var clone = city.Clone();

        Assert.NotSame(city.LockedTiles, clone.LockedTiles);
        Assert.Same(clone.LockedTiles, clone.WorkedTiles);
    }
}
