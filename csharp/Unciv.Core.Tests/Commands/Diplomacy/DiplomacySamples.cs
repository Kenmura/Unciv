using Unciv.Commands;
using Unciv.Commands.Diplomacy;
using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Logic.Trade;

namespace Unciv.Core.Tests.Commands.Diplomacy;

// T193: a valid sample of each diplomacy command, all against one DiplomacyWorld (A acts; B, C are majors, S the city-state).
// Each sample gets a fresh world: the setups exclude each other (war with the city-state, a pledge, ...).
internal static class DiplomacySamples
{
    public const string BuyUnique = "Can spend Gold to annex or puppet a City-State that has been your Ally for [0] turns";

    public static readonly string[] Types =
    [
        "declare-war", "denounce", "offer-friendship", "respond-friendship", "make-demand", "respond-demand", "propose-trade",
        "retract-trade", "respond-trade-request", "dismiss-trade-request", "gift-gold", "gift-improvement", "tribute-gold",
        "tribute-worker", "pledge-protection", "withdraw-protection", "diplomatic-marriage", "negotiate-city-state-peace",
    ];

    public static DiplomacyWorld NewWorld() => new([BuyUnique]);

    public static GameCommand Sample(string type, DiplomacyWorld w)
    {
        var funcs = (CityStateFunctions)w.S.CityStateFunctions;
        switch (type)
        {
            case "declare-war":
                return new Unciv.Commands.Diplomacy.DeclareWar { CivId = w.B.CivId };
            case "denounce":
                return new Denounce { CivId = w.B.CivId };
            case "offer-friendship":
                return new OfferFriendship { CivId = w.B.CivId };
            case "respond-friendship":
                w.A.AddPopupAlert(new PopupAlert(AlertType.DeclarationOfFriendship, w.C.CivId));
                return new RespondToFriendship { CivId = w.C.CivId, Accept = true };
            case "make-demand":
                return new MakeDemand { CivId = w.B.CivId, Demand = "DoNotSettleNearUs" };
            case "respond-demand":
                w.A.AddPopupAlert(new PopupAlert(AlertType.DemandToStopSettlingCitiesNear, w.C.CivId));
                return new RespondToDemand { CivId = w.C.CivId, Demand = "DoNotSettleNearUs", Accept = true };
            case "propose-trade":
                return new ProposeTrade { CivId = w.B.CivId, OurOffers = [new TradeOfferSpec(Constants.FlatGold, nameof(TradeOfferType.Gold), 5)] };
            case "retract-trade":
                w.C.TradeRequests.Add(new TradeRequest(w.A.CivId, new Trade()));
                return new RetractTrade { CivId = w.C.CivId };
            case "respond-trade-request":
                w.A.TradeRequests.Add(new TradeRequest(w.B.CivId, new Trade()));
                return new RespondToTradeRequest { CivId = w.B.CivId, Accept = true };
            case "dismiss-trade-request":
                w.A.TradeRequests.Add(new TradeRequest(w.B.CivId, new Trade()));
                return new DismissTradeRequest { CivId = w.B.CivId };
            case "gift-gold":
                w.A.AddGold(1000);
                return new GiftGold { CivId = w.S.CivId, Amount = 250 };
            case "gift-improvement":
                w.S.Tech.AddTechnology("Iron Working");
                w.S.Tech.AddTechnology("Mining");
                var tile = w.CityS.GetTiles().First(it => it != w.CityS.GetCenterTile() && it.IsLand);
                tile.BaseTerrain = "Hill";
                tile.SetTerrainTransients();
                tile.TileResource = w.Game.Ruleset.TileResources["Iron"];
                tile.SetTransients();
                w.Diplo(w.S, w.A).SetInfluence(70f);
                w.A.AddGold(500);
                return new GiftImprovement { CivId = w.S.CivId, Tile = tile.Position, Improvement = "Mine" };
            case "tribute-gold":
                w.Game.Ruleset.ModOptions.Constants.TributeGlobalModifier = 1000;
                return new TributeGold { CivId = w.S.CivId };
            case "tribute-worker":
                w.Game.Ruleset.ModOptions.Constants.TributeGlobalModifier = 1000;
                return new TributeWorker { CivId = w.S.CivId };
            case "pledge-protection":
                return new PledgeProtection { CivId = w.S.CivId };
            case "withdraw-protection":
                w.Ok(new PledgeProtection { CivId = w.S.CivId });
                w.Diplo(w.S, w.A).RemoveFlag(DiplomacyFlags.RecentlyPledgedProtection);
                return new WithdrawProtection { CivId = w.S.CivId };
            case "diplomatic-marriage":
                w.Diplo(w.S, w.A).SetInfluence(100f);
                funcs.UpdateAllyCivForCityState();
                w.Diplo(w.A, w.S).RemoveFlag(DiplomacyFlags.MarriageCooldown);
                w.A.AddGold(funcs.GetDiplomaticMarriageCost());
                return new DiplomaticMarriage { CivId = w.S.CivId };
            case "negotiate-city-state-peace":
                w.MakeWar(w.A, w.S);
                return new NegotiateCityStatePeace { CivId = w.S.CivId };
            default:
                throw new ArgumentException("No diplomacy sample for " + type);
        }
    }
}
