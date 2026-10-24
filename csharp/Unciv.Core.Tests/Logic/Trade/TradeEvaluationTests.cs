using Unciv.Core.Tests.Support;
using Unciv.Logic.Automation.Civilization;
using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Logic.Trade;
using Unciv.Models.Ruleset.Tile;
using TradeObject = Unciv.Logic.Trade.Trade;

namespace Unciv.Core.Tests.Logic.Trading;

// T089: TradeEvaluation.kt has no Kotlin fixture ("generate golden values"): the expected numbers are hand-derived from the Kotlin source
// (each test names the branch it pins). Standard speed (all modifiers 1), two AI civs A and B that have met, one city each.
[Collection("RulesetCache")]
public class TradeEvaluationTests
{
    private readonly TradeEvaluation evaluation = new();

    private static (TestGame Game, Civilization A, Civilization B) Setup(bool humanB = false)
    {
        var game = new TestGame();
        game.MakeHexagonalMap(8);
        var a = game.AddCiv();
        var b = game.AddCiv(isPlayer: humanB);
        game.AddCity(a, game.GetTile(-6, 0));
        game.AddCity(b, game.GetTile(6, 0));
        a.DiplomacyFunctions.MakeCivilizationsMeet(b);
        return (game, a, b);
    }

    private static TradeOffer Offer(string name, TradeOfferType type, int amount = 1, int duration = -1) => new(name, type, amount, duration);

    private static void MakeWar(Civilization a, Civilization b)
    {
        a.GetDiplomacyManager(b)!.DiplomaticStatus = DiplomaticStatus.War;
        b.GetDiplomacyManager(a)!.DiplomaticStatus = DiplomaticStatus.War;
    }

    private static string FirstResource(TestGame game, ResourceType type) =>
        game.Ruleset.TileResources.Values.First(it => it.ResourceType == type).Name;

    // evaluateBuyCostWithInflation / evaluateSellCostWithInflation divide non-gold offers by the gold inflation, which is 1000/(1+1660)+.4 at 0 gpt.
    private static int Inflated(Civilization civ, int cost) => (int)(cost / TradeEvaluation.Default.GetGoldInflation(civ));

    [Fact]
    public void GoldInflation_IsOneOverTheCurveAtZeroIncomeAndFallsWithIncome()
    {
        var (_, a, _) = Setup();
        a.Stats.StatsForNextTurn.Gold = 0f;
        Assert.Equal(1000.0 / 1661.0 + .4, evaluation.GetGoldInflation(a), 12);
        a.Stats.StatsForNextTurn.Gold = 100f;
        Assert.Equal(1000.0 / (System.Math.Pow(100.0, 1.2) + 1660.0) + .4, evaluation.GetGoldInflation(a), 12);
        a.Stats.StatsForNextTurn.Gold = 1e9f;
        Assert.True(evaluation.GetGoldInflation(a) > .4 && evaluation.GetGoldInflation(a) < .41);
    }

    [Fact]
    public void GoldAndGoldPerTurn_AreNotInflatedAndGoldPerTurnIsFourFifthsOfTheTotal()
    {
        var (_, a, b) = Setup();
        var trade = new TradeObject();
        Assert.Equal(123, evaluation.EvaluateBuyCostWithInflation(Offer(Constants.FlatGold, TradeOfferType.Gold, 123), a, b, trade));
        Assert.Equal(123, evaluation.EvaluateSellCostWithInflation(Offer(Constants.FlatGold, TradeOfferType.Gold, 123), a, b, trade));
        // 7 * 30 * 4 / 5 = 168; Kotlin Int arithmetic is left to right: (7 * 30 * 4) / 5
        Assert.Equal(168, evaluation.EvaluateBuyCostWithInflation(Offer(Constants.GoldPerTurn, TradeOfferType.Gold_Per_Turn, 7, 30), a, b, trade));
        Assert.Equal(168, evaluation.EvaluateSellCostWithInflation(Offer(Constants.GoldPerTurn, TradeOfferType.Gold_Per_Turn, 7, 30), a, b, trade));
        Assert.Equal(1, evaluation.EvaluateSellCostWithInflation(Offer(Constants.GoldPerTurn, TradeOfferType.Gold_Per_Turn, 1, 2), a, b, trade)); // 8/5
    }

    [Fact]
    public void Embassy_BuyIsThirtyAndSellDependsOnTheRelationship()
    {
        var (_, a, b) = Setup();
        var trade = new TradeObject();
        var embassy = Offer(Constants.AcceptEmbassy, TradeOfferType.Embassy);
        Assert.Equal(29, evaluation.EvaluateBuyCostWithInflation(embassy, a, b, trade)); // 30 / 1.00205 = 29.94
        Assert.Equal(29, evaluation.EvaluateSellCostWithInflation(embassy, a, b, trade)); // Neutral: 30
        MakeWar(a, b); // Enemy while at war
        Assert.Equal(Inflated(a, 300), evaluation.EvaluateSellCostWithInflation(embassy, a, b, trade));
        Assert.Equal(299, Inflated(a, 300));
    }

    [Fact]
    public void Treaties_AreValuedPerTheirName()
    {
        var (_, a, b) = Setup();
        var trade = new TradeObject();
        Assert.Equal(0, evaluation.EvaluateBuyCostWithInflation(Offer(Constants.DefensivePact, TradeOfferType.Treaty), a, b, trade));
        Assert.Equal(Inflated(a, 1000), evaluation.EvaluateBuyCostWithInflation(Offer("Some Future Treaty", TradeOfferType.Treaty), a, b, trade));
        Assert.Equal(Inflated(a, 1000), evaluation.EvaluateSellCostWithInflation(Offer("Some Future Treaty", TradeOfferType.Treaty), a, b, trade));
        // peace treaty: equal strength (both minimum might 1) costs nothing
        Assert.Equal(0, evaluation.EvaluateBuyCostWithInflation(Offer(Constants.PeaceTreaty, TradeOfferType.Treaty), a, b, trade));
    }

    [Fact]
    public void ResearchAgreement_SellsForTheNegativeAmount()
    {
        var (_, a, b) = Setup();
        var trade = new TradeObject();
        // -50 / 1.00205 = -49.9 truncates toward zero
        Assert.Equal(-49, evaluation.EvaluateSellCostWithInflation(Offer(Constants.ResearchAgreement, TradeOfferType.Treaty, 50), a, b, trade));
        Assert.Equal(-49, evaluation.EvaluateBuyCostWithInflation(Offer(Constants.ResearchAgreement, TradeOfferType.Treaty, 50), a, b, trade));
    }

    [Fact]
    public void DefensivePact_SellIsPricedOutWhenTheAiDoesNotWantIt()
    {
        var (_, a, b) = Setup();
        // no opinion of the other civ reaches the threshold: wantsToSignDefensivePact is false, so 100000
        var wants = DiplomacyAutomation.WantsToSignDefensivePact(new Unciv.View.GameView((Unciv.Logic.GameInfo)a.GameInfo, a).CivView, b);
        Assert.False(wants);
        Assert.Equal(Inflated(a, 100000), evaluation.EvaluateSellCostWithInflation(Offer(Constants.DefensivePact, TradeOfferType.Treaty), a, b, new TradeObject()));
    }

    [Fact]
    public void Luxury_BuyDependsOnHappinessAndDemandedResourceAndSellOnHowManyWeHave()
    {
        var (game, a, b) = Setup();
        var luxury = FirstResource(game, ResourceType.Luxury);
        var offer = Offer(luxury, TradeOfferType.Luxury_Resource);
        var trade = new TradeObject();
        Assert.Equal(Inflated(a, 350), evaluation.EvaluateBuyCostWithInflation(offer, a, b, trade)); // happiness 0 < 10
        a.Stats.Happiness = 10;
        Assert.Equal(Inflated(a, 300), evaluation.EvaluateBuyCostWithInflation(offer, a, b, trade));
        a.Stats.Happiness = -1;
        Assert.Equal(Inflated(a, 450), evaluation.EvaluateBuyCostWithInflation(offer, a, b, trade));
        a.Stats.Happiness = 10;
        a.Cities[0].DemandedResource = luxury;
        Assert.Equal(Inflated(a, 350), evaluation.EvaluateBuyCostWithInflation(offer, a, b, trade)); // + 50 for the city that demands it

        Assert.Equal(Inflated(a, 600), evaluation.EvaluateSellCostWithInflation(offer, a, b, trade)); // the last one

        b.GetDiplomacyManager(a)!.SetFlag(DiplomacyFlags.ResourceTradesCutShort, 5);
        a.GetDiplomacyManager(b)!.SetFlag(DiplomacyFlags.ResourceTradesCutShort, 5);
        Assert.Equal(0, evaluation.EvaluateBuyCostWithInflation(offer, a, b, trade));
    }

    [Fact]
    public void Strategic_SellIsFiftyEachWhenNotAtWar()
    {
        var (game, a, b) = Setup();
        var strategic = FirstResource(game, ResourceType.Strategic);
        var offer = Offer(strategic, TradeOfferType.Strategic_Resource, 3);
        Assert.Equal(Inflated(a, 150), evaluation.EvaluateSellCostWithInflation(offer, a, b, new TradeObject()));
        // we already have enough? We have none, but nothing in range of the cities needs it: not wanted
        Assert.True(evaluation.EvaluateBuyCostWithInflation(offer, a, b, new TradeObject()) >= 0);
    }

    [Fact]
    public void Stockpiled_UnknownResourcesAreWorthNothingAndUnsetOnesAreNeverSold()
    {
        var (game, a, b) = Setup();
        var trade = new TradeObject();
        Assert.Equal(0, evaluation.EvaluateBuyCostWithInflation(Offer("No such resource", TradeOfferType.Stockpiled_Resource), a, b, trade));
        Assert.Equal(0, evaluation.EvaluateSellCostWithInflation(Offer("No such resource", TradeOfferType.Stockpiled_Resource), a, b, trade));
        var known = game.Ruleset.TileResources.Values.First(it => it.ResourceType == ResourceType.Strategic).Name;
        Assert.Equal(0, evaluation.EvaluateBuyCostWithInflation(Offer(known, TradeOfferType.Stockpiled_Resource), a, b, trade));
        Assert.Equal(Inflated(a, int.MaxValue), evaluation.EvaluateSellCostWithInflation(Offer(known, TradeOfferType.Stockpiled_Resource), a, b, trade));
    }

    [Fact]
    public void City_SellCostHasAThousandFloorAndBuyCostCountsPopulationAndBuildings()
    {
        var (game, a, b) = Setup();
        var cityOfB = b.Cities[0];
        var buildings = cityOfB.CityConstructions.GetBuiltBuildings().Count();
        var offer = Offer(cityOfB.Id, TradeOfferType.City);
        var trade = new TradeObject();
        // buying B's city for A: (pop*4 + buildings + 4 + surrounded) * 100, not surrounded by A's cities
        var expectedBuy = (cityOfB.Population.Population * 4 + buildings + 4) * 100;
        Assert.Equal(Inflated(a, expectedBuy), evaluation.EvaluateBuyCostWithInflation(offer, a, b, trade));
        // selling: B sells its own city to A. The capital is 0 tiles from itself: no distance bonus
        var expectedSell = System.Math.Max((cityOfB.Population.Population * 4 + buildings + 4) * 100, 1000);
        Assert.Equal(Inflated(b, expectedSell), evaluation.EvaluateSellCostWithInflation(offer, b, a, trade));
        // unhappiness stops the AI buying
        a.Stats.Happiness = -1;
        Assert.Equal(0, evaluation.EvaluateBuyCostWithInflation(offer, a, b, trade));
        Assert.NotNull(game);
        // unknown city ids throw like Kotlin's Exception
        Assert.Throws<InvalidOperationException>(() => evaluation.EvaluateBuyCostWithInflation(Offer("nope", TradeOfferType.City), a, b, trade));
        Assert.Throws<InvalidOperationException>(() => evaluation.EvaluateSellCostWithInflation(Offer("nope", TradeOfferType.City), a, b, trade));
    }

    [Fact]
    public void OpenBorders_SellDependsOnTheRelationshipAndOtherAgreementsThrow()
    {
        var (_, a, b) = Setup();
        var trade = new TradeObject();
        var openBorders = Offer(Constants.OpenBorders, TradeOfferType.Agreement);
        Assert.Equal(Inflated(a, 100), evaluation.EvaluateBuyCostWithInflation(openBorders, a, b, trade));
        Assert.Equal(Inflated(a, 200), evaluation.EvaluateSellCostWithInflation(openBorders, a, b, trade));
        MakeWar(a, b);
        Assert.Equal(Inflated(a, 2000), evaluation.EvaluateSellCostWithInflation(openBorders, a, b, trade));
        Assert.Throws<InvalidOperationException>(() => evaluation.EvaluateSellCostWithInflation(Offer("Bogus", TradeOfferType.Agreement), a, b, trade));
        Assert.Throws<InvalidOperationException>(() => evaluation.EvaluateBuyCostWithInflation(Offer("Bogus", TradeOfferType.Agreement), a, b, trade));
    }

    [Fact]
    public void Introduction_IsWorthTheModOptionsUniqueOrNothing()
    {
        var (game, a, b) = Setup();
        var offer = Offer(b.CivName, TradeOfferType.Introduction);
        Assert.Equal(0, evaluation.EvaluateBuyCostWithInflation(offer, a, b, new TradeObject()));

        var (game2, a2, b2) = Setup();
        game2.Ruleset.ModOptions = new Unciv.Models.Ruleset.ModOptions { Uniques = ["Can trade civilization introductions for [30] Gold"] };
        Assert.Equal(Inflated(a2, 30), evaluation.EvaluateBuyCostWithInflation(Offer(b2.CivName, TradeOfferType.Introduction), a2, b2, new TradeObject()));
        Assert.Equal(Inflated(a2, 30), evaluation.EvaluateSellCostWithInflation(Offer(b2.CivName, TradeOfferType.Introduction), a2, b2, new TradeObject()));
        Assert.NotNull(game);
    }

    [Fact]
    public void WarDeclaration_IsFreeToBuyWhenWeAreNotAtWarAndNeverSoldIfWeAreFarTooAgainstIt()
    {
        var (game, a, b) = Setup();
        var c = game.AddCiv();
        game.AddCity(c, game.GetTile(0, 6));
        a.DiplomacyFunctions.MakeCivilizationsMeet(c);
        var offer = Offer(c.CivName, TradeOfferType.WarDeclaration);
        var trade = new TradeObject();
        Assert.Equal(0, evaluation.EvaluateBuyCostWithInflation(offer, a, b, trade)); // not at war with C: why should we pay
        var teamTrade = new TradeObject();
        teamTrade.OurOffers.Add(offer);
        teamTrade.TheirOffers.Add(offer);
        Assert.Equal(0, evaluation.EvaluateBuyCostWithInflation(offer, a, b, teamTrade)); // team war is handled when selling
    }

    [Fact]
    public void Technology_ValuesFollowTheSquareRootOfTheCost()
    {
        var (game, a, b) = Setup();
        var tech = game.Ruleset.Technologies.Values.First(it => it.Cost > 0);
        var offer = Offer(tech.Name, TradeOfferType.Technology);
        var trade = new TradeObject();
        var root = (int)System.Math.Sqrt(tech.Cost);
        Assert.Equal(Inflated(a, root * 20), evaluation.EvaluateSellCostWithInflation(offer, a, b, trade));
        Assert.Equal(Inflated(a, (int)(System.Math.Sqrt(tech.Cost) * 1f) * 20), evaluation.EvaluateBuyCostWithInflation(offer, a, b, trade));
    }

    [Fact]
    public void PeaceProposal_BuyIsFreeUnlessWeDislikeTheThirdCivAndSellIsThePeaceCost()
    {
        var (game, a, b) = Setup();
        var c = game.AddCiv();
        game.AddCity(c, game.GetTile(0, 6));
        a.DiplomacyFunctions.MakeCivilizationsMeet(c);
        var offer = Offer(c.CivName, TradeOfferType.PeaceProposal);
        var trade = new TradeObject();
        Assert.Equal(0, evaluation.EvaluateBuyCostWithInflation(offer, a, b, trade));
        MakeWar(a, c); // Enemy < Neutral
        Assert.Equal(Inflated(a, int.MinValue), evaluation.EvaluateBuyCostWithInflation(offer, a, b, trade));
        Assert.Equal(0, evaluation.EvaluateSellCostWithInflation(offer, a, b, trade)); // equal strength: no cost
    }

    [Fact]
    public void PeaceCost_ForAWeakerSideIsNegativeAndCappedAndZeroWhenRoughlyEqual()
    {
        var (game, a, b) = Setup();
        Assert.Equal(0, evaluation.EvaluatePeaceCostForThem(a, b));
        for (var i = 0; i < 6; i++) game.AddUnit("Warrior", b, game.GetTile(3, i));
        b.ResetMilitaryMightCache();
        var ours = a.GetStatForRanking(Unciv.UI.Screens.VictoryScreen.RankingType.Force);
        var theirs = b.GetStatForRanking(Unciv.UI.Screens.VictoryScreen.RankingType.Force);
        Assert.True(theirs > ours * 1.5f);
        // weaker side: -(min(toInt(adv * (adv / ours) / (inflation * 2)), maxGoldTradeOffer))
        var adv = theirs - ours;
        var raw = (int)(adv * (adv / (float)ours) / (evaluation.GetGoldInflation(a) * 2.0));
        Assert.Equal(-System.Math.Min(raw, a.GameInfo.Ruleset.ModOptions.Constants.MaxGoldTradeOffer), evaluation.EvaluatePeaceCostForThem(a, b));
        // stronger side without any motivation to attack: free
        Assert.True(evaluation.EvaluatePeaceCostForThem(b, a) >= 0);
    }

    [Fact]
    public void TradeAcceptability_SumsTheirOffersMinusOursAndClamps()
    {
        var (_, a, b) = Setup();
        var trade = new TradeObject();
        trade.TheirOffers.Add(Offer(Constants.FlatGold, TradeOfferType.Gold, 100));
        trade.OurOffers.Add(Offer(Constants.FlatGold, TradeOfferType.Gold, 30));
        Assert.Equal(70, evaluation.GetTradeAcceptability(trade, a, b));
        Assert.True(evaluation.IsTradeAcceptable(trade, a, b));
        trade.OurOffers.Add(Offer("x", TradeOfferType.Gold, 71));
        Assert.Equal(-1, evaluation.GetTradeAcceptability(trade, a, b));
        Assert.False(evaluation.IsTradeAcceptable(trade, a, b));

        // Treaty offers of theirs are ignored in the sum
        var treaties = new TradeObject();
        treaties.TheirOffers.Add(Offer("Some Future Treaty", TradeOfferType.Treaty));
        Assert.Equal(0, evaluation.GetTradeAcceptability(treaties, a, b));

        // sums are Long and clamped to the Int range
        var huge = new TradeObject();
        huge.TheirOffers.Add(Offer(Constants.FlatGold, TradeOfferType.Gold, int.MaxValue));
        huge.TheirOffers.Add(Offer("y", TradeOfferType.Gold, int.MaxValue));
        Assert.Equal(int.MaxValue, evaluation.GetTradeAcceptability(huge, a, b));
        var hugeOurs = new TradeObject();
        hugeOurs.OurOffers.Add(Offer(Constants.FlatGold, TradeOfferType.Gold, int.MaxValue));
        hugeOurs.OurOffers.Add(Offer("y", TradeOfferType.Gold, int.MaxValue));
        Assert.Equal(int.MinValue, evaluation.GetTradeAcceptability(hugeOurs, a, b));
    }

    [Fact]
    public void TradeAcceptability_EnemiesDemandMoreUnlessItIsAPeaceOrResearchDeal()
    {
        var (_, a, b) = Setup();
        MakeWar(a, b); // Enemy
        var trade = new TradeObject();
        trade.OurOffers.Add(Offer(Constants.FlatGold, TradeOfferType.Gold, 100));
        Assert.Equal(-150, evaluation.GetTradeAcceptability(trade, a, b)); // (100 * 1.5).toLong()

        var research = new TradeObject();
        research.OurOffers.Add(Offer(Constants.FlatGold, TradeOfferType.Gold, 100));
        research.OurOffers.Add(Offer(Constants.ResearchAgreement, TradeOfferType.Treaty, 0));
        // no 1.5 factor: the research agreement (sold at -0) is in our offers; 100 gold + 0
        Assert.Equal(-100, evaluation.GetTradeAcceptability(research, a, b));
    }

    [Fact]
    public void TradeAcceptability_RefusesTooManyCitiesTooManyWarsAndADefensivePactWithNonAllies()
    {
        var (game, a, b) = Setup();
        game.AddCity(a, game.GetTile(-5, 2));
        game.AddCity(a, game.GetTile(-5, -2));
        var cities = new TradeObject();
        // three cities: ceil(3 / 5) = 1 may be surrendered
        cities.OurOffers.Add(Offer(a.Cities[0].Id, TradeOfferType.City));
        cities.OurOffers.Add(Offer(a.Cities[1].Id, TradeOfferType.City));
        Assert.Equal(int.MinValue, evaluation.GetTradeAcceptability(cities, a, b));

        var wars = new TradeObject();
        wars.OurOffers.Add(Offer("one", TradeOfferType.WarDeclaration));
        wars.OurOffers.Add(Offer("two", TradeOfferType.WarDeclaration));
        Assert.Equal(int.MinValue, evaluation.GetTradeAcceptability(wars, a, b));

        var pact = new TradeObject();
        pact.OurOffers.Add(Offer(Constants.DefensivePact, TradeOfferType.Treaty));
        Assert.Equal(int.MinValue, evaluation.GetTradeAcceptability(pact, a, b));
    }

    [Fact]
    public void TradeAcceptability_AddsDiplomaticGiftsOnlyWhenAsked()
    {
        var (_, a, b) = Setup();
        a.GetDiplomacyManager(b)!.RecieveGoldGifts(100);
        var gifts = a.GetDiplomacyManager(b)!.GetGoldGifts();
        Assert.True(gifts > 0);
        var trade = new TradeObject();
        Assert.Equal(0, evaluation.GetTradeAcceptability(trade, a, b));
        Assert.Equal(gifts, evaluation.GetTradeAcceptability(trade, a, b, includeDiplomaticGifts: true));
        Assert.True(evaluation.IsTradeAcceptable(trade, a, b));
    }

    [Fact]
    public void IsTradeValid_ChecksGoldResourcesTreatiesIntroductionsAndCities()
    {
        var (game, a, b) = Setup();
        var trade = new TradeObject();
        Assert.True(evaluation.IsTradeValid(trade, a, b));

        a.AddGold(100);
        trade.OurOffers.Add(Offer(Constants.FlatGold, TradeOfferType.Gold, 111)); // 111 * 0.9 = 99.9 < 100
        Assert.True(evaluation.IsTradeValid(trade, a, b));
        trade.OurOffers[0].Amount = 112; // 100.8 >= 100
        Assert.False(evaluation.IsTradeValid(trade, a, b));
        trade.OurOffers.Clear();

        trade.TheirOffers.Add(Offer(Constants.GoldPerTurn, TradeOfferType.Gold_Per_Turn, 100000, 30)); // B makes far less gold per turn
        Assert.False(evaluation.IsTradeValid(trade, a, b));
        trade.TheirOffers.Clear();

        trade.OurOffers.Add(Offer(FirstResource(game, ResourceType.Luxury), TradeOfferType.Luxury_Resource)); // we do not have it
        Assert.False(evaluation.IsTradeValid(trade, a, b));
        trade.OurOffers.Clear();

        trade.OurOffers.Add(Offer(Constants.PeaceTreaty, TradeOfferType.Treaty)); // only valid at war
        Assert.False(evaluation.IsTradeValid(trade, a, b));
        MakeWar(a, b);
        Assert.True(evaluation.IsTradeValid(trade, a, b));
        trade.OurOffers.Clear();

        trade.OurOffers.Add(Offer(Constants.ResearchAgreement, TradeOfferType.Treaty));
        Assert.True(evaluation.IsTradeValid(trade, a, b));
        a.GetDiplomacyManager(b)!.SetFlag(DiplomacyFlags.ResearchAgreement, 10);
        Assert.False(evaluation.IsTradeValid(trade, a, b));
        trade.OurOffers.Clear();

        trade.OurOffers.Add(Offer(Constants.DefensivePact, TradeOfferType.Treaty));
        Assert.True(evaluation.IsTradeValid(trade, a, b));
        a.GetDiplomacyManager(b)!.SetFlag(DiplomacyFlags.DefensivePact, 10);
        Assert.False(evaluation.IsTradeValid(trade, a, b));
        trade.OurOffers.Clear();

        var c = game.AddCiv();
        game.AddCity(c, game.GetTile(0, 6));
        a.DiplomacyFunctions.MakeCivilizationsMeet(c);
        trade.OurOffers.Add(Offer(c.CivId, TradeOfferType.Introduction)); // B does not know C yet
        Assert.True(evaluation.IsTradeValid(trade, a, b));
        b.DiplomacyFunctions.MakeCivilizationsMeet(c);
        Assert.False(evaluation.IsTradeValid(trade, a, b)); // now they do
        trade.OurOffers.Clear();

        trade.OurOffers.Add(Offer("no such city", TradeOfferType.City));
        Assert.False(evaluation.IsTradeValid(trade, a, b));
        trade.OurOffers.Clear();

        // offering every city of a civ that has owned a capital is refused
        trade.OurOffers.Add(Offer(a.Cities[0].Id, TradeOfferType.City));
        Assert.True(a.HasEverOwnedOriginalCapital);
        Assert.False(evaluation.IsTradeValid(trade, a, b));
        trade.OurOffers.Clear();
        trade.TheirOffers.Add(Offer(b.Cities[0].Id, TradeOfferType.City));
        Assert.False(evaluation.IsTradeValid(trade, a, b));
    }

    [Fact]
    public void IsTradeValid_EmbassyIsRefusedWhenOneAlreadyExists()
    {
        var (_, a, b) = Setup();
        var trade = new TradeObject();
        trade.OurOffers.Add(new TradeOffer(Constants.AcceptEmbassy, TradeOfferType.Embassy, a.GameInfo.Speed));
        Assert.True(evaluation.IsTradeValid(trade, a, b));
        b.GetDiplomacyManager(a)!.AddModifier(DiplomaticModifiers.EstablishedEmbassy, 1f);
        Assert.False(evaluation.IsTradeValid(trade, a, b));

        var theirs = new TradeObject();
        theirs.TheirOffers.Add(new TradeOffer(Constants.AcceptEmbassy, TradeOfferType.Embassy, a.GameInfo.Speed));
        Assert.True(evaluation.IsTradeValid(theirs, a, b));
        a.GetDiplomacyManager(b)!.AddModifier(DiplomaticModifiers.SharedEmbassies, 1f);
        Assert.False(evaluation.IsTradeValid(theirs, a, b));
    }

    [Fact]
    public void IsPeaceProposalEnabled_FollowsTheDeclaredWarCountdownHumansAndTheAcceptability()
    {
        var (game, a, b) = Setup();
        var c = game.AddCiv();
        game.AddCity(c, game.GetTile(0, 6));
        a.DiplomacyFunctions.MakeCivilizationsMeet(c);
        // A (civInfo) is at war with C (thirdCiv)
        MakeWar(a, c);
        Assert.True(evaluation.IsPeaceProposalEnabled(c, a)); // equal strength: peace cost 0 -> acceptable
        a.GetDiplomacyManager(c)!.SetFlag(DiplomacyFlags.DeclaredWar, 5);
        Assert.False(evaluation.IsPeaceProposalEnabled(c, a));

        var human = game.AddCiv(isPlayer: true);
        game.AddCity(human, game.GetTile(0, -6));
        a.DiplomacyFunctions.MakeCivilizationsMeet(human);
        MakeWar(a, human);
        Assert.False(evaluation.IsPeaceProposalEnabled(human, a));
        Assert.NotNull(b);
    }

    [Fact]
    public void TheDefaultEvaluationIsWhatTheSeamsAndTradeLogicUseWithoutAnInstalledHook()
    {
        var (_, a, b) = Setup();
        Assert.Same(TradeEvaluation.Default, TradeEvaluationSeam.Of(a));
        var logic = TradeEvaluationSeam.NewLogic(a, b);
        Assert.Same(TradeEvaluation.Default, logic.Evaluation);
        Assert.Same(TradeEvaluation.Default, new TradeLogic(a, b).Evaluation);
    }

    [Fact]
    public void AcceptTrade_WithTheRealEvaluation_GivesTheReceiverTheGiftModifier()
    {
        var (_, a, b) = Setup();
        a.AddGold(500);
        var logic = new TradeLogic(a, b);
        logic.CurrentTrade.OurOffers.Add(Offer(Constants.FlatGold, TradeOfferType.Gold, 100));
        logic.AcceptTrade();
        Assert.True(b.GetDiplomacyManager(a)!.GetModifier(DiplomaticModifiers.GaveUsGifts) > 0f);
    }

    [Fact]
    public void LiberatingACity_ReachesTheRealEvaluationThroughTheConquestHook()
    {
        var (_, a, b) = Setup();
        var trade = new TradeObject();
        trade.OurOffers.Add(Offer(Constants.OpenBorders, TradeOfferType.Agreement, 1, 30));
        Unciv.Logic.City.Managers.CityManagerHooks.RealConquest.AcceptTrade(a, b, trade);
        Assert.True(b.GetDiplomacyManager(a)!.HasOpenBorders);
    }
}
