using Unciv.Core.Tests.Support;
using Unciv.Logic.City.Managers;
using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Logic.Trade;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using TradeObject = Unciv.Logic.Trade.Trade;

namespace Unciv.Core.Tests.Logic.Trading;

// T086: the TradeLogic parts that need a map, cities and units, over TestGame (Kotlin has no TradeLogic fixture; hand-derived from TradeLogic.kt).
[Collection("RulesetCache")]
public class TradeLogicGameTests
{
    private sealed class NoEvaluation : ITradeEvaluation
    {
        public int GetTradeAcceptability(TradeObject trade, Civilization evaluator, Civilization tradePartner, bool includeDiplomaticGifts = false) => 0;
        public bool IsPeaceProposalEnabled(Civilization thirdCiv, Civilization civInfo) => false;
    }

    private static TradeLogic NewLogic(Civilization a, Civilization b) => new(a, b, new NoEvaluation());

    private static (TestGame Game, Civilization A, Civilization B) Setup(string[]? uniquesA = null, string[]? uniquesB = null, bool humanB = true)
    {
        var game = new TestGame();
        game.MakeHexagonalMap(6);
        var a = game.AddCiv(uniquesA, isPlayer: true);
        var b = game.AddCiv(uniquesB, isPlayer: humanB);
        game.AddCity(a, game.GetTile(-4, 0));
        game.AddCity(b, game.GetTile(4, 0));
        a.DiplomacyFunctions.MakeCivilizationsMeet(b);
        return (game, a, b);
    }

    [Fact]
    public void AcceptTrade_City_MovesTheCityAndAlertsTheHumanReceiverWhenItIsNotTheirsToLiberate()
    {
        var (game, a, b) = Setup();
        var c = game.AddCiv();
        var cCity = game.AddCity(c, game.GetTile(0, 4));
        var city = game.AddCity(a, game.GetTile(-2, 2));
        city.FoundingCivObject = c; // founded by C, owned by A
        Assert.NotNull(cCity);
        var tileOfUnit = game.GetTile(-3, 3);
        game.AddTileToCity(city, tileOfUnit);
        var unit = game.AddUnit("Warrior", a, game.GetTile(-2, 2)); // on the city center: teleported away when the city moves

        var logic = NewLogic(a, b);
        Assert.Contains(logic.OurAvailableOffers, it => it.Type == TradeOfferType.City && it.Name == city.Id);
        Assert.DoesNotContain(logic.OurAvailableOffers, it => it.Type == TradeOfferType.City && it.Name == a.GetCapital()!.Id);
        logic.CurrentTrade.OurOffers.Add(new TradeOffer(city.Id, TradeOfferType.City));
        logic.AcceptTrade(false);

        Assert.Same(b, city.Civ);
        Assert.Contains(city, b.Cities);
        Assert.DoesNotContain(city, a.Cities);
        var alert = Assert.Single(b.PopupAlerts, it => it.Type == AlertType.CityTraded);
        Assert.Equal(city.Id, alert.Value);
        Assert.NotEqual(city.GetCenterTile(), unit.CurrentTile);
    }

    [Fact]
    public void AcceptTrade_City_NoLiberationAlertWhenTheReceiverIsTheFounder()
    {
        var (game, a, b) = Setup();
        var city = game.AddCity(a, game.GetTile(-2, 2));
        city.FoundingCivObject = b;
        var logic = NewLogic(a, b);
        logic.CurrentTrade.OurOffers.Add(new TradeOffer(city.Id, TradeOfferType.City));
        logic.AcceptTrade(false);
        Assert.Same(b, city.Civ);
        Assert.DoesNotContain(b.PopupAlerts, it => it.Type == AlertType.CityTraded);
    }

    [Fact]
    public void Constructor_OneCityChallenger_OffersNoCities()
    {
        var (game, a, b) = Setup();
        game.AddCity(a, game.GetTile(-2, 2));
        game.GameInfo.GameParameters.OneCityChallenge = true;
        var logic = NewLogic(a, b);
        Assert.DoesNotContain(logic.OurAvailableOffers, it => it.Type == TradeOfferType.City);
    }

    [Fact]
    public void AcceptTrade_Embassy_ExploresAroundTheGiversCapitalAndSetsModifiers()
    {
        var embassies = new[] { UniqueType.EnablesEmbassies.Text() };
        var (game, a, b) = Setup(embassies, embassies);
        var logic = NewLogic(a, b);
        Assert.Contains(logic.OurAvailableOffers, it => it.Type == TradeOfferType.Embassy);
        Assert.Contains(logic.TheirAvailableOffers, it => it.Type == TradeOfferType.Embassy);
        var capitalTile = b.GetCapital()!.GetCenterTile();
        Assert.False(capitalTile.IsExplored(a));
        logic.CurrentTrade.TheirOffers.Add(new TradeOffer(Constants.AcceptEmbassy, TradeOfferType.Embassy));

        logic.AcceptTrade(false);

        Assert.True(capitalTile.IsExplored(a));
        // Kotlin quirk kept: their offer applies to theirDiploManager (b's view of a), so b "received" and a "established"
        Assert.Equal(1f, b.GetDiplomacyManager(a)!.GetModifier(DiplomaticModifiers.ReceivedEmbassy));
        Assert.Equal(2f, a.GetDiplomacyManager(b)!.GetModifier(DiplomaticModifiers.EstablishedEmbassy));
        Assert.DoesNotContain(NewLogic(a, b).TheirAvailableOffers, it => it.Type == TradeOfferType.Embassy);
    }

    [Fact]
    public void AcceptTrade_EmbassiesOnBothSides_BecomeSharedEmbassies()
    {
        var embassies = new[] { UniqueType.EnablesEmbassies.Text() };
        var (_, a, b) = Setup(embassies, embassies);
        var logic = NewLogic(a, b);
        logic.CurrentTrade.TheirOffers.Add(new TradeOffer(Constants.AcceptEmbassy, TradeOfferType.Embassy));
        logic.AcceptTrade(false);
        var second = NewLogic(a, b);
        second.CurrentTrade.OurOffers.Add(new TradeOffer(Constants.AcceptEmbassy, TradeOfferType.Embassy));

        second.AcceptTrade(false);

        // ourDiploManager has no EstablishedEmbassy (a only received one), so b's side applies: b's manager holds EstablishedEmbassy -> shared
        Assert.True(a.GetDiplomacyManager(b)!.HasModifier(DiplomaticModifiers.SharedEmbassies) || b.GetDiplomacyManager(a)!.HasModifier(DiplomaticModifiers.SharedEmbassies));
    }

    [Fact]
    public void AcceptTrade_Technology_IsResearchedByTheReceiver()
    {
        var (game, a, b) = Setup();
        var tech = game.Ruleset.Technologies.Keys.First(it => !a.Tech.IsResearched(it));
        var logic = NewLogic(a, b);
        logic.CurrentTrade.TheirOffers.Add(new TradeOffer(tech, TradeOfferType.Technology));
        logic.AcceptTrade(false);
        Assert.True(a.Tech.IsResearched(tech));
    }

    [Fact]
    public void Constructor_OpenBorders_AreOfferedWhenBothCivsEnableThemAndEmbassiesAreShared()
    {
        var uniques = new[] { UniqueType.EnablesOpenBorders.Text() };
        var (_, a, b) = Setup(uniques, uniques);
        a.GetDiplomacyManager(b)!.SetModifier(DiplomaticModifiers.SharedEmbassies, 3f); // the ruleset requires embassies for diplomacy
        b.GetDiplomacyManager(a)!.SetModifier(DiplomaticModifiers.SharedEmbassies, 3f);
        var logic = NewLogic(a, b);
        Assert.Contains(logic.OurAvailableOffers, it => it.Name == Constants.OpenBorders && it.Type == TradeOfferType.Agreement);
        Assert.Contains(logic.TheirAvailableOffers, it => it.Name == Constants.OpenBorders);
    }

    [Fact]
    public void AcceptTrade_DefensivePact_IsSignedFromTheirTreatyOffer()
    {
        var (_, a, b) = Setup();
        var logic = NewLogic(a, b);
        logic.CurrentTrade.TheirOffers.Add(new TradeOffer(Constants.DefensivePact, TradeOfferType.Treaty, 1, 20));
        logic.CurrentTrade.OurOffers.Add(new TradeOffer(Constants.DefensivePact, TradeOfferType.Treaty, 1, 20));
        logic.AcceptTrade(false);
        Assert.Equal(DiplomaticStatus.DefensivePact, a.GetDiplomacyManager(b)!.DiplomaticStatus);
    }

    [Fact]
    public void AcceptTrade_OpenBordersAgreement_SetsOpenBordersOnTheRecordedTrade()
    {
        var (_, a, b) = Setup();
        var logic = NewLogic(a, b);
        logic.CurrentTrade.OurOffers.Add(new TradeOffer(Constants.OpenBorders, TradeOfferType.Agreement, 1, 30));
        logic.AcceptTrade(false);
        Assert.True(b.GetDiplomacyManager(a)!.HasOpenBorders);
        Assert.False(a.GetDiplomacyManager(b)!.HasOpenBorders);
    }

    [Fact]
    public void AcceptTrade_StockpiledResource_MovesTheStockpileAndIgnoresUnknownResources()
    {
        var (game, a, b) = Setup();
        var resource = game.CreateResource(UniqueType.Stockpiled.Text());
        a.GainStockpiledResource(resource, 10);
        var logic = NewLogic(a, b);
        logic.CurrentTrade.OurOffers.Add(new TradeOffer(resource.Name, TradeOfferType.Stockpiled_Resource, 4));
        logic.CurrentTrade.OurOffers.Add(new TradeOffer("No such resource", TradeOfferType.Stockpiled_Resource, 4));

        logic.AcceptTrade(false);

        Assert.Equal(6, a.ResourceStockpiles[resource.Name]);
        Assert.Equal(4, b.ResourceStockpiles[resource.Name]);
    }

    [Fact]
    public void Constructor_ResourceOffers_ExcludeBonusAndMapLuxuryAndStrategicTypes()
    {
        var (game, a, b) = Setup();
        var lux = game.CreateResource();
        lux.ResourceType = ResourceType.Luxury;
        var strategic = game.CreateResource();
        strategic.ResourceType = ResourceType.Strategic;
        var bonus = game.CreateResource();
        bonus.ResourceType = ResourceType.Bonus;
        var received = game.CreateResource();
        received.ResourceType = ResourceType.Luxury;
        a.DetailedCivResources.Add(lux, "Cities", 2);
        a.DetailedCivResources.Add(strategic, "Cities", 5);
        a.DetailedCivResources.Add(bonus, "Cities", 3);
        a.DetailedCivResources.Add(received, "Trade", 1); // keeps its origin and adds an empty tradable entry

        var offers = NewLogic(a, b).OurAvailableOffers;

        Assert.Equal(TradeOfferType.Luxury_Resource, offers.Single(it => it.Name == lux.Name).Type);
        Assert.Equal(2, offers.Single(it => it.Name == lux.Name).Amount);
        Assert.Equal(TradeOfferType.Strategic_Resource, offers.Single(it => it.Name == strategic.Name).Type);
        Assert.Equal(5, offers.Single(it => it.Name == strategic.Name).Amount);
        Assert.DoesNotContain(offers, it => it.Name == bonus.Name);
        Assert.Equal(0, offers.Single(it => it.Name == received.Name).Amount);
        Assert.DoesNotContain(NewLogic(b, a).OurAvailableOffers, it => it.Name == lux.Name);
    }

    [Fact]
    public void Constructor_IntroductionsAndWarDeclarations_FollowTheModOptions()
    {
        var (game, a, b) = Setup();
        var c = game.AddCiv();
        game.AddCity(c, game.GetTile(0, -4)); // so it is not defeated
        a.DiplomacyFunctions.MakeCivilizationsMeet(c); // a knows c, b does not
        Assert.DoesNotContain(NewLogic(a, b).OurAvailableOffers, it => it.Type == TradeOfferType.Introduction);

        game.Ruleset.ModOptions = new ModOptions { Uniques = [UniqueType.TradeCivIntroductions.Text()] };
        var logic = NewLogic(a, b);
        Assert.Contains(logic.OurAvailableOffers, it => it.Type == TradeOfferType.Introduction && it.Name == c.CivId);

        b.DiplomacyFunctions.MakeCivilizationsMeet(c);
        Assert.Contains(NewLogic(a, b).OurAvailableOffers, it => it.Type == TradeOfferType.WarDeclaration && it.Name == c.CivId);
        game.Ruleset.ModOptions = new ModOptions { Uniques = [UniqueType.DiplomaticRelationshipsCannotChange.Text()] };
        Assert.DoesNotContain(NewLogic(a, b).OurAvailableOffers, it => it.Type == TradeOfferType.WarDeclaration);
    }

    private static (TestGame Game, Civilization A, Civilization B, Civilization C) SetupWar()
    {
        var (game, a, b) = Setup(humanB: false);
        var c = game.AddCiv();
        game.AddCity(c, game.GetTile(0, -4));
        a.DiplomacyFunctions.MakeCivilizationsMeet(c);
        b.DiplomacyFunctions.MakeCivilizationsMeet(c);
        return (game, a, b, c);
    }

    [Fact]
    public void AcceptTrade_WarDeclaration_BothSidesOfferingTheSameTargetIsATeamWar()
    {
        var (_, a, b, c) = SetupWar();
        var logic = NewLogic(a, b);
        logic.CurrentTrade.OurOffers.Add(new TradeOffer(c.CivId, TradeOfferType.WarDeclaration));
        logic.CurrentTrade.TheirOffers.Add(new TradeOffer(c.CivId, TradeOfferType.WarDeclaration));

        logic.AcceptTrade(false);

        Assert.Equal(DiplomaticStatus.War, a.GetDiplomacyManager(c)!.DiplomaticStatus);
        Assert.Equal(DiplomaticStatus.War, b.GetDiplomacyManager(c)!.DiplomaticStatus);
    }

    [Fact]
    public void AcceptTrade_WarDeclaration_GiverJoiningAnExistingWarIsAJoinWar()
    {
        var (_, a, b, c) = SetupWar();
        a.GetDiplomacyManager(c)!.DeclareWar();
        var logic = NewLogic(a, b);
        logic.CurrentTrade.TheirOffers.Add(new TradeOffer(c.CivId, TradeOfferType.WarDeclaration));

        logic.AcceptTrade(false);

        Assert.Equal(DiplomaticStatus.War, b.GetDiplomacyManager(c)!.DiplomaticStatus);
    }

    [Fact]
    public void ConquestExternals_AcceptTrade_RunsTheTradeWithoutEvaluatingGifts()
    {
        var (_, a, b) = Setup();
        var trade = new TradeObject();
        trade.OurOffers.Add(new TradeOffer(Constants.OpenBorders, TradeOfferType.Agreement, 1, 30));
        CityManagerSeams.UnportedConquest.AcceptTrade(a, b, trade);
        Assert.True(b.GetDiplomacyManager(a)!.HasOpenBorders);
    }
}
