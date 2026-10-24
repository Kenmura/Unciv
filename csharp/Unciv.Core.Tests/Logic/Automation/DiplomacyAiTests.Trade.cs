using Unciv.Logic.Automation.Civilization;
using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Logic.Trade;
using Unciv.Models.Ruleset.Tile;
using TradeObject = Unciv.Logic.Trade.Trade;

namespace Unciv.Core.Tests.Logic.Automation;

public static partial class DiplomacyAiTests
{
    // TradeAutomation over the stand-in TradeEvaluation (T089 is not ported yet).
    [Collection("RulesetCache")]
    public class TradeAutomationTests
    {
        private static TradeRequest GoldRequestFromB(World w, int amount)
        {
            // B asks A for gold: from A's side that is one of OUR offers (A would give the gold)
            var trade = new TradeObject();
            trade.OurOffers.Add(new TradeOffer(Constants.FlatGold, TradeOfferType.Gold, amount, -1));
            return new TradeRequest(w.B.CivId, trade);
        }

        [Fact]
        public void Respond_WithoutTradeAndChangeState_ClearsTheRequestsUntouched()
        {
            var w = new World();
            w.A.AddGold(100);
            w.A.TradeRequests.Add(GoldRequestFromB(w, 40));
            TradeAutomation.RespondToTradeRequests(w.A, false);
            Assert.Empty(w.A.TradeRequests);
            Assert.Equal(100, w.A.Gold);
            Assert.Equal(0, w.B.Gold);
        }

        [Fact]
        public void Respond_InvalidTrades_AreDroppedWithoutAnswer()
        {
            var w = new World(bIsHuman: true);
            w.A.AddGold(100);
            w.Evaluation.TradeValid = false;
            w.A.TradeRequests.Add(GoldRequestFromB(w, 40));
            var before = w.B.Notifications.Count;
            TradeAutomation.RespondToTradeRequests(w.A, true);
            Assert.Empty(w.A.TradeRequests);
            Assert.Equal(100, w.A.Gold);
            Assert.Equal(before, w.B.Notifications.Count);
        }

        [Fact]
        public void Respond_AnAcceptableTrade_IsAcceptedAndTheAskerIsTold()
        {
            var w = new World(bIsHuman: true);
            w.A.AddGold(100);
            w.A.TradeRequests.Add(GoldRequestFromB(w, 40));
            TradeAutomation.RespondToTradeRequests(w.A, true);
            Assert.Empty(w.A.TradeRequests);
            Assert.Equal(60, w.A.Gold);
            Assert.Equal(40, w.B.Gold);
            Assert.Contains(w.B.Notifications, it => it.Text == $"[{w.A.CivName}] has accepted your trade request");
        }

        [Fact]
        public void Respond_AnUnacceptableTradeWithNothingToAsk_IsDenied()
        {
            var w = new World(bIsHuman: true);
            w.A.AddGold(100);
            w.Evaluation.TradeAcceptable = false;
            w.Evaluation.Acceptability = -50;
            w.A.TradeRequests.Add(GoldRequestFromB(w, 40));
            TradeAutomation.RespondToTradeRequests(w.A, true);
            Assert.Empty(w.A.TradeRequests);
            Assert.Equal(100, w.A.Gold);
            Assert.Empty(w.B.TradeRequests);
            Assert.Contains(w.B.Notifications, it => it.Text == $"[{w.A.CivName}] has denied your trade request");
        }

        [Fact]
        public void Respond_AnUnacceptableTrade_GetsAMinimalCounteroffer()
        {
            var w = new World(bIsHuman: true);
            w.A.AddGold(100);
            w.B.AddGold(500);
            w.Evaluation.TradeAcceptable = false;
            w.Evaluation.Acceptability = -30;
            w.Evaluation.BuyCost = offer => offer.Type == TradeOfferType.Gold ? offer.Amount : 0;
            // B asks A for a strategic resource (not gold: B's own gold may then be asked for)
            var trade = new TradeObject();
            trade.OurOffers.Add(new TradeOffer("Iron", TradeOfferType.Strategic_Resource, 1, -1));
            w.A.TradeRequests.Add(new TradeRequest(w.B.CivId, trade));
            TradeAutomation.RespondToTradeRequests(w.A, true);

            Assert.Empty(w.A.TradeRequests);
            var counter = Assert.Single(w.B.TradeRequests);
            Assert.Equal(w.A.CivId, counter.RequestingCiv);
            // 500 gold is worth 500; the -30 deficit is closed by taking 30 gold (valueOfOne 1): from B's side B gives it
            var gold = Assert.Single(counter.Trade.OurOffers, it => it.Type == TradeOfferType.Gold);
            Assert.Equal(30, gold.Amount);
            Assert.Contains(w.B.Notifications, it => it.Text == $"[{w.A.CivName}] has made a counteroffer to your trade request");
        }

        [Fact]
        public void Respond_ADealVeryInOurFavour_SweetensThePotWithGold()
        {
            var w = new World();
            w.A.AddGold(100);
            w.Evaluation.TradeAcceptable = false;
            w.Evaluation.Acceptability = 200; // (int)(200 / 1.1f) = 181; 2/3 of it = 120 >= 100 -> we add gold
            w.Evaluation.SellCost = _ => 1;
            var trade = new TradeObject();
            trade.OurOffers.Add(new TradeOffer("Iron", TradeOfferType.Strategic_Resource, 1, -1));
            w.A.TradeRequests.Add(new TradeRequest(w.B.CivId, trade));
            TradeAutomation.RespondToTradeRequests(w.A, true);
            var counter = Assert.Single(w.B.TradeRequests);
            // our gift of min(120 / 1, 100 gold) goes into ourOffers, which the reversed request shows as their offers
            var gift = Assert.Single(counter.Trade.TheirOffers, it => it.Type == TradeOfferType.Gold);
            Assert.Equal(100, gift.Amount);
        }

        // ---- luxuries

        [Fact]
        public void ExchangeLuxuries_NeedsTradableLuxuriesOnBothSides()
        {
            var w = new World();
            TradeAutomation.ExchangeLuxuries(w.A);
            Assert.Empty(w.B.TradeRequests);
            Assert.Empty(w.T.TradeRequests);
        }

        [Fact]
        public void ExchangeLuxuries_SwapsOneOfEachSpareLuxury()
        {
            var w = new World();
            var wine = w.Game.CreateResource();
            wine.ResourceType = ResourceType.Luxury;
            var silk = w.Game.CreateResource();
            silk.ResourceType = ResourceType.Luxury;
            AddTradableResource(w.A, wine, 3);
            AddTradableResource(w.B, silk, 2);
            TradeAutomation.ExchangeLuxuries(w.A);
            var request = Assert.Single(w.B.TradeRequests);
            Assert.Equal(w.A.CivId, request.RequestingCiv);
            // reversed: B gives one silk, receives one wine
            var give = Assert.Single(request.Trade.OurOffers);
            var get = Assert.Single(request.Trade.TheirOffers);
            Assert.Equal((silk.Name, 1), (give.Name, give.Amount));
            Assert.Equal((wine.Name, 1), (get.Name, get.Amount));
            // a pending request from us is not repeated
            TradeAutomation.ExchangeLuxuries(w.A);
            Assert.Single(w.B.TradeRequests);
        }

        [Fact]
        public void ExchangeLuxuries_SkipsEnemiesDeclinedAndCityStates()
        {
            var w = new World();
            var wine = w.Game.CreateResource();
            wine.ResourceType = ResourceType.Luxury;
            var silk = w.Game.CreateResource();
            silk.ResourceType = ResourceType.Luxury;
            AddTradableResource(w.A, wine, 3);
            AddTradableResource(w.B, silk, 2);
            AddTradableResource(w.T, silk, 2);
            w.Diplo(w.A, w.B).SetFlag(DiplomacyFlags.DeclinedLuxExchange, 5);
            w.SetOpinion(w.A, w.T, -50f); // Enemy: no luxury trade
            TradeAutomation.ExchangeLuxuries(w.A);
            Assert.Empty(w.B.TradeRequests);
            Assert.Empty(w.T.TradeRequests);
        }

        private static void AddTradableResource(Civilization civ, TileResource resource, int amount) =>
            civ.DetailedCivResources.Add(resource, "Tiles", amount);
    }
}
