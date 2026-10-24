using Unciv.Core.Tests.Support;
using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Logic.Trade;

namespace Unciv.Core.Tests.Fixtures.CivilizationFixtures;

// T150: the five trade cases of tests/src/com/unciv/logic/civilization/diplomacy/GoldGiftingTests.kt
// (deferred from T142 until TradeEvaluation, T089, landed).
[Collection("RulesetCache")]
public sealed class GoldGiftingTradeFixtureTests
{
    private readonly TestGame testGame = new();
    private readonly Civilization a;
    private readonly Civilization b;
    private readonly DiplomacyManager aDiplomacy;
    private readonly DiplomacyManager bDiplomacy;

    public GoldGiftingTradeFixtureTests()
    {
        a = testGame.AddCiv();
        b = testGame.AddCiv();
        a.DiplomacyFunctions.MakeCivilizationsMeet(b);
        aDiplomacy = a.GetDiplomacyManager(b)!;
        bDiplomacy = b.GetDiplomacyManager(a)!;
        Constants().GoldGiftMultiplier = 1f;
        Constants().GoldGiftTradeMultiplier = 1f;
        Constants().GoldGiftDegradationMultiplier = 1f;
    }

    private Unciv.Models.ModConstants Constants() => a.GameInfo.Ruleset.ModOptions.Constants;

    [Fact]
    public void ExcessGoldFromATradeBecomeAGift()
    {
        a.AddGold(1000);
        Assert.Equal(0, bDiplomacy.GetGoldGifts());
        var tradeOffer = new TradeLogic(a, b);
        tradeOffer.CurrentTrade.OurOffers.Add(tradeOffer.OurAvailableOffers.First(it => it.Type == TradeOfferType.Gold));
        Assert.True(new TradeEvaluation().GetTradeAcceptability(tradeOffer.CurrentTrade.Reverse(), b, a, false) > 0);
        tradeOffer.AcceptTrade();
        Assert.Equal(0, aDiplomacy.GetGoldGifts());
        Assert.True(bDiplomacy.GetGoldGifts() > 0);
    }

    [Fact]
    public void CanAskFor90PercentOfGoldGiftBackAgainATurnLater()
    {
        a.AddGold(1000);
        var tradeOffer = new TradeLogic(a, b);
        tradeOffer.CurrentTrade.OurOffers.Add(tradeOffer.OurAvailableOffers.First(it => it.Type == TradeOfferType.Gold));
        tradeOffer.AcceptTrade();
        bDiplomacy.NextTurn();
        var tradeOffer2 = new TradeLogic(a, b);
        tradeOffer2.CurrentTrade.TheirOffers.Add(new TradeOffer("Gold", TradeOfferType.Gold, 900, testGame.GameInfo.Speed));
        Assert.True(new TradeEvaluation().GetTradeAcceptability(tradeOffer.CurrentTrade.Reverse(), b, a, false) > 0);
        tradeOffer2.AcceptTrade();
        Assert.True(bDiplomacy.GetGoldGifts() >= 0);
    }

    [Fact]
    public void GoldGiftedImpactTradeAcceptability()
    {
        a.AddGold(1000);
        var tradeOffer = new TradeLogic(a, b);
        tradeOffer.CurrentTrade.OurOffers.Add(tradeOffer.OurAvailableOffers.First(it => it.Type == TradeOfferType.Gold));
        Assert.True(new TradeEvaluation().GetTradeAcceptability(tradeOffer.CurrentTrade, b, a, true) < 0);
        tradeOffer.AcceptTrade();
        var tradeOffer2 = new TradeLogic(a, b);
        Assert.True(new TradeEvaluation().GetTradeAcceptability(tradeOffer2.CurrentTrade.Reverse(), b, a, true) > 0);
    }

    [Fact]
    public void GoldGiftedUsesPureGoldMultiplierConstant()
    {
        a.AddGold(1000);
        Constants().GoldGiftMultiplier = .5f;
        Constants().GoldGiftTradeMultiplier = 0f;
        var tradeOffer = new TradeLogic(a, b);
        tradeOffer.CurrentTrade.OurOffers.Add(tradeOffer.OurAvailableOffers.First(it => it.Type == TradeOfferType.Gold));
        tradeOffer.AcceptTrade();
        Assert.Equal(500, bDiplomacy.GetGoldGifts());
    }

    [Fact]
    public void GoldGiftedUsesGoldMultiplierConstant()
    {
        a.AddGold(1000);
        b.AddGold(500);
        Constants().GoldGiftMultiplier = 0f;
        Constants().GoldGiftTradeMultiplier = .5f;
        var tradeOffer = new TradeLogic(a, b);
        tradeOffer.CurrentTrade.OurOffers.Add(tradeOffer.OurAvailableOffers.First(it => it.Type == TradeOfferType.Gold));
        tradeOffer.CurrentTrade.TheirOffers.Add(tradeOffer.TheirAvailableOffers.First(it => it.Type == TradeOfferType.Gold));
        tradeOffer.AcceptTrade();
        Assert.Equal(250, bDiplomacy.GetGoldGifts());
    }
}
