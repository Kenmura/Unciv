using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Logic.Trade;
using NationObject = Unciv.Models.Ruleset.Nation.Nation;
using TradeObject = Unciv.Logic.Trade.Trade;

namespace Unciv.Core.Tests.Logic.Trading;

// T086: TradeLogic. Kotlin has no fixture for it; the cases are hand-derived from TradeLogic.kt over the Civilization seam fakes.
// TradeEvaluation (T089) is replaced by a stand-in.
public class TradeLogicTests
{
    private sealed class StandInEvaluation(int ours = 0, int theirs = 0, bool peaceProposal = true) : ITradeEvaluation
    {
        public List<(Civilization Evaluator, bool Gifts)> Calls { get; } = [];
        public int GetTradeAcceptability(TradeObject trade, Civilization evaluator, Civilization tradePartner, bool includeDiplomaticGifts = false)
        {
            Calls.Add((evaluator, includeDiplomaticGifts));
            return Calls.Count == 1 ? ours : theirs;
        }
        public bool IsPeaceProposalEnabled(Civilization thirdCiv, Civilization civInfo) => peaceProposal;
    }

    private static FakeGameInfoState NewInfo()
    {
        var info = new FakeGameInfoState();
        info.RulesetData.Difficulties["Prince"] = info.Difficulty; // UpdateStatsForNextTurn reads the AI difficulty
        return info;
    }

    private static Civilization AddCiv(FakeGameInfoState info, string name, PlayerType playerType = PlayerType.AI, string? cityStateType = null)
    {
        var civ = new Civilization(new NationObject { Name = name, CityStateType = cityStateType })
        {
            GameInfo = info,
            PlayerType = playerType,
            CityStateFunctions = new FakeCityStateFunctions(),
            QuestManager = new FakeQuestManager(),
        };
        civ.Units = new TestUnitManager(civ) { Count = 1 }; // so they are not considered defeated
        info.Civs.Add(civ);
        return civ;
    }

    private static void Meet(Civilization civ, Civilization other) => civ.DiplomacyFunctions.MakeCivilizationsMeet(other);

    private static DiplomacyManager Manager(Civilization civ, Civilization other) => civ.GetDiplomacyManager(other)!;

    private static (FakeGameInfoState Info, Civilization A, Civilization B) TwoCivs()
    {
        var info = NewInfo();
        var a = AddCiv(info, "A");
        var b = AddCiv(info, "B");
        Meet(a, b);
        return (info, a, b);
    }

    private static void SetWar(Civilization a, Civilization b)
    {
        Manager(a, b).DiplomaticStatus = DiplomaticStatus.War;
        Manager(b, a).DiplomaticStatus = DiplomaticStatus.War;
    }

    [Fact]
    public void Constructor_AtWar_OffersPeaceTreatyGoldAndGoldPerTurn()
    {
        var (_, a, b) = TwoCivs();
        SetWar(a, b);
        var logic = new TradeLogic(a, b, new StandInEvaluation());

        foreach (var offers in new[] { logic.OurAvailableOffers, logic.TheirAvailableOffers })
        {
            Assert.Contains(offers, it => it.Name == Constants.PeaceTreaty && it.Type == TradeOfferType.Treaty);
            Assert.Contains(offers, it => it.Name == Constants.FlatGold && it.Type == TradeOfferType.Gold);
            Assert.Contains(offers, it => it.Name == Constants.GoldPerTurn && it.Type == TradeOfferType.Gold_Per_Turn);
        }
        Assert.Empty(logic.CurrentTrade.OurOffers);
        Assert.Empty(logic.CurrentTrade.TheirOffers);
    }

    [Fact]
    public void Constructor_AtPeace_HasNoPeaceTreaty()
    {
        var (_, a, b) = TwoCivs();
        var logic = new TradeLogic(a, b, new StandInEvaluation());
        Assert.DoesNotContain(logic.OurAvailableOffers, it => it.Name == Constants.PeaceTreaty);
        Assert.Contains(logic.OurAvailableOffers, it => it.Type == TradeOfferType.Gold);
    }

    [Fact]
    public void Constructor_GoldOfferCarriesTheCivsGold()
    {
        var (_, a, b) = TwoCivs();
        a.AddGold(120);
        var logic = new TradeLogic(a, b, new StandInEvaluation());
        Assert.Equal(120, logic.OurAvailableOffers.Single(it => it.Type == TradeOfferType.Gold).Amount);
        Assert.Equal(0, logic.TheirAvailableOffers.Single(it => it.Type == TradeOfferType.Gold).Amount);
    }

    [Fact]
    public void Constructor_CityState_OffersNothingExceptEmbassy()
    {
        var info = NewInfo();
        var a = AddCiv(info, "A");
        var cs = AddCiv(info, "CS", cityStateType: "Cultured");
        Meet(a, cs);
        var logic = new TradeLogic(a, cs, new StandInEvaluation());
        Assert.DoesNotContain(logic.OurAvailableOffers, it => it.Type != TradeOfferType.Embassy);
        Assert.DoesNotContain(logic.TheirAvailableOffers, it => it.Type != TradeOfferType.Embassy);
    }

    [Fact]
    public void Constructor_PeaceProposal_AmountFollowsTheEvaluation()
    {
        var info = NewInfo();
        var a = AddCiv(info, "A");
        var b = AddCiv(info, "B");
        var c = AddCiv(info, "C");
        Meet(a, b);
        Meet(b, c);
        Meet(a, c);
        SetWar(a, c); // b knows c, c is at war with a

        var enabled = new TradeLogic(a, b, new StandInEvaluation(peaceProposal: true));
        Assert.Equal(1, enabled.OurAvailableOffers.Single(it => it.Type == TradeOfferType.PeaceProposal && it.Name == "C").Amount);
        var disabled = new TradeLogic(a, b, new StandInEvaluation(peaceProposal: false));
        // amount 0 keeps the offer in the list (it only disables the button), so the offer is there with amount 0
        Assert.Equal(0, disabled.OurAvailableOffers.Single(it => it.Type == TradeOfferType.PeaceProposal && it.Name == "C").Amount);
    }

    [Fact]
    public void Constructor_WarDeclarationOffer_ForACivBothKnowAndAreNotAtWarWith()
    {
        var info = NewInfo();
        var a = AddCiv(info, "A");
        var b = AddCiv(info, "B");
        var c = AddCiv(info, "C");
        Meet(a, b);
        Meet(a, c);
        Meet(b, c);
        var logic = new TradeLogic(a, b, new StandInEvaluation());
        Assert.Contains(logic.OurAvailableOffers, it => it.Type == TradeOfferType.WarDeclaration && it.Name == "C");
        Assert.Contains(logic.TheirAvailableOffers, it => it.Type == TradeOfferType.WarDeclaration && it.Name == "C");

        SetWar(a, c);
        var atWar = new TradeLogic(a, b, new StandInEvaluation());
        Assert.DoesNotContain(atWar.OurAvailableOffers, it => it.Type == TradeOfferType.WarDeclaration);
    }

    [Fact]
    public void AcceptTrade_RecordsTheTradeOnBothSides_AndReversesItForThem()
    {
        var (_, a, b) = TwoCivs();
        var logic = new TradeLogic(a, b, new StandInEvaluation());
        logic.CurrentTrade.OurOffers.Add(new TradeOffer("Spice", TradeOfferType.Luxury_Resource, 1, 30));

        logic.AcceptTrade(false);

        Assert.Same(logic.CurrentTrade, Assert.Single(Manager(a, b).Trades));
        var theirs = Assert.Single(Manager(b, a).Trades);
        Assert.Equal("Spice", Assert.Single(theirs.TheirOffers).Name);
        Assert.Empty(theirs.OurOffers);
    }

    [Fact]
    public void AcceptTrade_GoldMovesFromGiverToReceiver()
    {
        var (_, a, b) = TwoCivs();
        a.AddGold(100);
        b.AddGold(10);
        var logic = new TradeLogic(a, b, new StandInEvaluation());
        logic.CurrentTrade.OurOffers.Add(new TradeOffer(Constants.FlatGold, TradeOfferType.Gold, 40));
        logic.CurrentTrade.TheirOffers.Add(new TradeOffer(Constants.FlatGold, TradeOfferType.Gold, 5));

        logic.AcceptTrade(false);

        Assert.Equal(65, a.Gold);
        Assert.Equal(45, b.Gold);
    }

    [Fact]
    public void AcceptTrade_PeaceTreaty_MakesPeaceOnceAndSkipsGifts()
    {
        var (_, a, b) = TwoCivs();
        SetWar(a, b);
        var evaluation = new StandInEvaluation();
        var logic = new TradeLogic(a, b, evaluation);
        logic.CurrentTrade.OurOffers.Add(new TradeOffer(Constants.PeaceTreaty, TradeOfferType.Treaty, 1, 10));
        logic.CurrentTrade.TheirOffers.Add(new TradeOffer(Constants.PeaceTreaty, TradeOfferType.Treaty, 1, 10));

        logic.AcceptTrade();

        Assert.Equal(DiplomaticStatus.Peace, Manager(a, b).DiplomaticStatus);
        Assert.Equal(DiplomaticStatus.Peace, Manager(b, a).DiplomaticStatus);
        Assert.Empty(evaluation.Calls);
    }

    private static void RunGift(int ours, int theirs, bool weOfferSomething, bool theyOfferSomething, out StandInEvaluation evaluation, out DiplomacyManager ourManager, out DiplomacyManager theirManager)
    {
        var (_, a, b) = TwoCivs();
        a.AddGold(500);
        b.AddGold(500);
        evaluation = new StandInEvaluation(ours, theirs);
        var logic = new TradeLogic(a, b, evaluation);
        if (weOfferSomething) logic.CurrentTrade.OurOffers.Add(new TradeOffer(Constants.FlatGold, TradeOfferType.Gold, 10));
        if (theyOfferSomething) logic.CurrentTrade.TheirOffers.Add(new TradeOffer(Constants.FlatGold, TradeOfferType.Gold, 10));
        logic.AcceptTrade();
        ourManager = Manager(a, b);
        theirManager = Manager(b, a);
    }

    [Fact]
    public void AcceptTrade_WeBenefit_GiftsThroughOurManagerOnlyAndPureGiftIsWorthMore()
    {
        RunGift(50, -20, weOfferSomething: true, theyOfferSomething: false, out var evaluation, out var ours, out var theirs);
        Assert.Equal(["A", "B"], evaluation.Calls.Select(it => it.Evaluator.CivId).ToList());
        Assert.All(evaluation.Calls, it => Assert.False(it.Gifts));
        var tradeGift = ours.GetModifier(DiplomaticModifiers.GaveUsGifts); // gift = 50 - max(-20, 0) = 50 at the trade multiplier 0.8
        Assert.True(tradeGift > 0f);
        Assert.False(theirs.HasModifier(DiplomaticModifiers.GaveUsGifts));

        RunGift(50, -20, weOfferSomething: false, theyOfferSomething: true, out _, out var pureOurs, out _);
        Assert.Equal(tradeGift / 0.8f, pureOurs.GetModifier(DiplomaticModifiers.GaveUsGifts), 4); // ourOffers empty: pure gift multiplier 1.0
    }

    [Fact]
    public void AcceptTrade_TheyBenefit_GiftsThroughTheirManagerAndSubtractsOnlyAPositiveOurValue()
    {
        RunGift(-5, 60, weOfferSomething: true, theyOfferSomething: false, out _, out var ours, out var theirs);
        Assert.False(ours.HasModifier(DiplomaticModifiers.GaveUsGifts));
        var withNegativeOurs = theirs.GetModifier(DiplomaticModifiers.GaveUsGifts); // 60 - max(-5, 0) = 60

        RunGift(10, 60, weOfferSomething: true, theyOfferSomething: false, out _, out var ours2, out var theirs2);
        Assert.False(ours2.HasModifier(DiplomaticModifiers.GaveUsGifts));
        Assert.Equal(withNegativeOurs * 50f / 60f, theirs2.GetModifier(DiplomaticModifiers.GaveUsGifts), 4); // 60 - 10
    }

    [Fact]
    public void AcceptTrade_EqualValues_GiftsNothing()
    {
        RunGift(30, 30, weOfferSomething: true, theyOfferSomething: true, out _, out var ours, out var theirs);
        Assert.False(ours.HasModifier(DiplomaticModifiers.GaveUsGifts));
        Assert.False(theirs.HasModifier(DiplomaticModifiers.GaveUsGifts));
    }

    [Fact]
    public void AcceptTrade_ApplyGiftsFalse_NeverEvaluates()
    {
        var (_, a, b) = TwoCivs();
        var evaluation = new StandInEvaluation();
        var logic = new TradeLogic(a, b, evaluation);
        logic.AcceptTrade(false);
        Assert.Empty(evaluation.Calls);
    }

    [Fact]
    public void AcceptTrade_ResearchAgreement_ChargesBothAndSetsFlagsOnBoth()
    {
        var (_, a, b) = TwoCivs();
        a.AddGold(300);
        b.AddGold(300);
        var logic = new TradeLogic(a, b, new StandInEvaluation());
        logic.CurrentTrade.TheirOffers.Add(new TradeOffer(Constants.ResearchAgreement, TradeOfferType.Treaty, 100, 25));
        logic.CurrentTrade.OurOffers.Add(new TradeOffer(Constants.ResearchAgreement, TradeOfferType.Treaty, 100, 25));

        logic.AcceptTrade(false);

        // only their treaty offers are transferred (one side), which charges both civs once
        Assert.Equal(200, a.Gold);
        Assert.Equal(200, b.Gold);
        Assert.Equal(25, Manager(a, b).GetFlag(DiplomacyFlags.ResearchAgreement));
        Assert.Equal(25, Manager(b, a).GetFlag(DiplomacyFlags.ResearchAgreement));
    }

    [Fact]
    public void AcceptTrade_Embassy_WithoutACapitalThrowsBeforeAnyModifier()
    {
        var (_, a, b) = TwoCivs();
        var logic = new TradeLogic(a, b, new StandInEvaluation());
        // The embassy transfer explores around the giver's capital (getCapital()!!): without one it throws before any modifier is applied
        logic.CurrentTrade.TheirOffers.Add(new TradeOffer(Constants.AcceptEmbassy, TradeOfferType.Embassy));
        Assert.ThrowsAny<Exception>(() => logic.AcceptTrade(false));
        Assert.False(Manager(a, b).HasModifier(DiplomaticModifiers.ReceivedEmbassy));
    }

    [Fact]
    public void AcceptTrade_Introduction_MakesTheCivsMeet()
    {
        var info = NewInfo();
        var a = AddCiv(info, "A");
        var b = AddCiv(info, "B");
        var c = AddCiv(info, "C");
        Meet(a, b);
        Meet(a, c);
        Assert.False(b.Knows(c));
        var logic = new TradeLogic(a, b, new StandInEvaluation());
        logic.CurrentTrade.OurOffers.Add(new TradeOffer("C", TradeOfferType.Introduction));

        logic.AcceptTrade(false);

        Assert.True(b.Knows(c));
        Assert.True(c.Knows(b));
    }

    [Fact]
    public void AcceptTrade_WarDeclaration_DirectWarFromTheGiver()
    {
        var info = NewInfo();
        var a = AddCiv(info, "A");
        var b = AddCiv(info, "B");
        var c = AddCiv(info, "C");
        Meet(a, b);
        Meet(a, c);
        Meet(b, c);
        var logic = new TradeLogic(a, b, new StandInEvaluation());
        logic.CurrentTrade.OurOffers.Add(new TradeOffer("C", TradeOfferType.WarDeclaration));

        logic.AcceptTrade(false);

        Assert.Equal(DiplomaticStatus.War, Manager(a, c).DiplomaticStatus);
        Assert.NotEqual(DiplomaticStatus.War, Manager(b, c).DiplomaticStatus);
    }

    [Fact]
    public void AcceptTrade_PeaceProposal_MakesPeaceBetweenGiverAndThirdCivAndRecordsTheTreaty()
    {
        var info = NewInfo();
        var a = AddCiv(info, "A");
        var b = AddCiv(info, "B");
        var c = AddCiv(info, "C");
        Meet(a, b);
        Meet(a, c);
        SetWar(a, c);
        var logic = new TradeLogic(a, b, new StandInEvaluation());
        logic.CurrentTrade.OurOffers.Add(new TradeOffer("C", TradeOfferType.PeaceProposal));

        logic.AcceptTrade();

        Assert.Equal(DiplomaticStatus.Peace, Manager(a, c).DiplomaticStatus);
        Assert.Equal(DiplomaticStatus.Peace, Manager(c, a).DiplomaticStatus);
        Assert.Contains(Manager(a, c).Trades, it => it.IsPeaceTreaty());
    }

    [Fact]
    public void Evaluation_DefaultThrowsUntilTradeEvaluationIsPorted()
    {
        var (_, a, b) = TwoCivs();
        var logic = new TradeLogic(a, b);
        Assert.Throws<NotSupportedException>(() => logic.AcceptTrade());
    }
}
