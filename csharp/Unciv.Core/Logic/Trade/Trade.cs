using Unciv.Json;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Models.Ruleset.Nation;
using Unciv.Utils;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Logic.Trade;

// core/src/com/unciv/logic/trade/Trade.kt (Trade and TradeRequest)

public class Trade : IsPartOfGameInfoSerialization
{
    public TradeOffersList TheirOffers { get; } = new();

    public TradeOffersList OurOffers { get; } = new();

    public Trade Reverse()
    {
        var newTrade = new Trade();
        newTrade.TheirOffers.AddRange(OurOffers.Select(it => it.Copy()).ToList());
        newTrade.OurOffers.AddRange(TheirOffers.Select(it => it.Copy()).ToList());
        return newTrade;
    }

    public bool EqualTrade(Trade trade)
    {
        if (trade.OurOffers.Count != OurOffers.Count
            || trade.TheirOffers.Count != TheirOffers.Count
        ) return false;

        foreach (var offer in trade.OurOffers)
            if (!OurOffers.Any(it => it.Equals(offer)))
                return false;
        foreach (var offer in trade.TheirOffers)
            if (!TheirOffers.Any(it => it.Equals(offer)))
                return false;
        return true;
    }

    public Trade Clone()
    {
        var toReturn = new Trade();
        toReturn.TheirOffers.AddRange(TheirOffers);
        toReturn.OurOffers.AddRange(OurOffers);
        return toReturn;
    }

    public void Set(Trade trade)
    {
        OurOffers.Clear();
        OurOffers.AddRange(trade.OurOffers);
        TheirOffers.Clear();
        TheirOffers.AddRange(trade.TheirOffers);
    }

    public bool IsPeaceTreaty() => OurOffers.Any(it => it.Type == TradeOfferType.Treaty && it.Name == Constants.PeaceTreaty);
}

public class TradeRequest : IsPartOfGameInfoSerialization
{
    /// <summary>Kotlin <c>lateinit var</c>: reading before assignment throws.</summary>
    public string RequestingCiv
    {
        get => requestingCiv ?? throw new InvalidOperationException("lateinit property requestingCiv has not been initialized");
        set => requestingCiv = value;
    }
    private string? requestingCiv;

    /// <summary>Their offers are what they offer us, and our offers are what they want in return. Kotlin <c>lateinit var</c>.</summary>
    public Trade Trade
    {
        get => trade ?? throw new InvalidOperationException("lateinit property trade has not been initialized");
        set => trade = value;
    }
    private Trade? trade;

    /// <summary>For json serialization.</summary>
    public TradeRequest()
    {
    }

    public TradeRequest(string requestingCiv, Trade trade)
    {
        RequestingCiv = requestingCiv;
        Trade = trade;
    }

    public void Decline(CivilizationObject decliningCiv)
    {
        var requestingCivInfo = (CivilizationObject)decliningCiv.GameInfo.GetCivilization(RequestingCiv);
        var requestingCivDiploManager = requestingCivInfo.GetDiplomacyManager(decliningCiv).NotNull();
        // the numbers of the flags (20,5) are the amount of turns to wait until offering again
        if (Trade.OurOffers.All(it => it.Type == TradeOfferType.Luxury_Resource)
            && Trade.TheirOffers.All(it => it.Type == TradeOfferType.Luxury_Resource))
            requestingCivDiploManager.SetFlag(DiplomacyFlags.DeclinedLuxExchange, 5 - KotlinMath.FloatToInt(requestingCivInfo.GetPersonality()[PersonalityValue.Commerce] / 2));
        if (Trade.OurOffers.Any(it => it.Name == Constants.ResearchAgreement))
            requestingCivDiploManager.SetFlag(DiplomacyFlags.DeclinedResearchAgreement, 15 - KotlinMath.FloatToInt(requestingCivInfo.GetPersonality()[PersonalityValue.Science]));
        if (Trade.OurOffers.Any(it => it.Name == Constants.DefensivePact))
            requestingCivDiploManager.SetFlag(DiplomacyFlags.DeclinedDefensivePact, 10);
        if (Trade.OurOffers.Any(it => it.Name == Constants.OpenBorders))
            requestingCivDiploManager.SetFlag(DiplomacyFlags.DeclinedOpenBorders, decliningCiv.IsAI() ? 5 : 10);
        if (Trade.OurOffers.Any(it => it.Name == Constants.AcceptEmbassy)
            || Trade.TheirOffers.Any(it => it.Name == Constants.AcceptEmbassy))
            requestingCivDiploManager.SetFlag(DiplomacyFlags.DeclinedEmbassy, decliningCiv.IsAI() ? 5 : 10);
        if (Trade.TheirOffers.Any(it => it.Type == TradeOfferType.WarDeclaration))
            requestingCivDiploManager.SetFlag(DiplomacyFlags.DeclinedJoinWarOffer, decliningCiv.IsAI() ? 5 : 10);
        if (Trade.OurOffers.Any(it => it.Type == TradeOfferType.WarDeclaration))
            requestingCivDiploManager.OtherCivDiplomacy().SetFlag(DiplomacyFlags.DeclinedJoinWarOffer, decliningCiv.IsAI() ? 5 : 10);

        if (Trade.IsPeaceTreaty()) requestingCivDiploManager.SetFlag(DiplomacyFlags.DeclinedPeace, 3);
    }
}
