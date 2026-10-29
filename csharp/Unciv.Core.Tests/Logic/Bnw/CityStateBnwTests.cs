using Unciv.Commands;
using Unciv.Commands.Units;
using Unciv.Core.Tests.Support;
using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Logic.Civilization.Managers;
using Unciv.Logic.Map.MapUnits;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Unique;
using CityObject = Unciv.Logic.City.City;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Core.Tests.Logic.Bnw;

// B021 (bnw-spec 4.10): city-state changes of Brave New World on the REAL generated ruleset: the Merchant of Venice buys a city-state,
// the influence anchors, the tenets' per-turn influence, friendship kept on death and the trade-route quest. G&K stays as it was.
// World: Venice (human) has a city at (-4,0), the Mercantile city-state a city at (4,0); everybody has met.
[Collection("RulesetCache")]
public sealed class CityStateBnwTests
{
    private readonly TestGame game;
    private readonly CivilizationObject venice, cityState;
    private readonly CityObject veniceCity, csCity;
    private readonly GameSession session;
    private GameInfo G => game.GameInfo;

    public CityStateBnwTests() : this(BaseRuleset.Civ_V_BNW)
    {
    }

    private CityStateBnwTests(BaseRuleset baseRuleset)
    {
        game = new TestGame(baseRuleset);
        game.MakeHexagonalMap(12);
        game.SetSpeed("Standard");
        G.GameParameters.BaseRuleset = baseRuleset.FullName();
        venice = game.AddCiv(game.Ruleset.Nations[baseRuleset == BaseRuleset.Civ_V_BNW ? "Venice" : "Rome"], isPlayer: true);
        cityState = game.AddCiv(cityStateType: "Mercantile");
        G.EngineOptions.VisibleMap = true;
        veniceCity = game.AddCity(venice, game.GetTile(-4, 0));
        csCity = game.AddCity(cityState, game.GetTile(4, 0));
        venice.DiplomacyFunctions.MakeCivilizationsMeet(cityState);
        venice.PopupAlerts.Clear();
        G.CurrentPlayerCiv = venice;
        G.CurrentPlayer = venice.CivId;
        session = new GameSession(G);
    }

    /// <summary>A valid sample of the buy command for GameCommandTests.</summary>
    internal static (GameInfo Game, string Actor, GameCommand Command) CommandSample()
    {
        var w = new CityStateBnwTests();
        var merchant = w.Merchant(3, 0);
        return (w.G, w.venice.CivId, new BuyCityState { UnitId = merchant.Id, CivId = w.cityState.CivId });
    }

    private MapUnit Merchant(int x, int y, string name = "Merchant of Venice")
    {
        venice.AddGold(10000 - venice.Gold);
        return game.AddUnit(name, venice, game.GetTile(x, y));
    }

    private CommandResult Buy(MapUnit unit) => session.Submit(venice.CivId, new BuyCityState { UnitId = unit.Id, CivId = cityState.CivId });

    private DiplomacyManager CsSide => cityState.GetDiplomacyManager(venice)!;

    // ---- the generated data

    [Fact]
    public void OnlyTheMerchantOfVeniceCarriesTheBuyUniqueAndTheQuestAndConstantsAreGenerated()
    {
        var r = game.Ruleset;
        Assert.True(((IHasUniques)r.Units["Merchant of Venice"]).HasUnique(UniqueType.CanBuyCityState));
        Assert.False(((IHasUniques)r.Units["Great Merchant"]).HasUnique(UniqueType.CanBuyCityState));
        var quest = r.Quests["Trade Route"];
        Assert.Equal(40f, quest.Influence);
        Assert.Equal(QuestName.TradeRoute, quest.QuestNameInstance);
        var c = r.ModOptions.Constants;
        Assert.Equal((0, 5, -20), (c.MinorFriendshipAnchorDefault, c.MinorFriendshipAnchorModProtected, c.MinorFriendshipAnchorModWaryOf));
        Assert.Equal((0, 15, 45), (c.FriendshipNeutralOnDeath, c.FriendshipFriendsOnDeath, c.FriendshipAlliesOnDeath));
    }

    // ---- the Merchant of Venice buys a city-state

    [Fact]
    public void AMerchantOfVeniceNextToTheCityBuysTheCityStateAsAPuppetAndIsSpent()
    {
        var merchant = Merchant(3, 0);
        int price = ((CityStateFunctions)cityState.CityStateFunctions).GetDiplomaticMarriageCost();
        Assert.True(price > 0);
        float gold = venice.Gold;
        Assert.True(Buy(merchant).Accepted);
        Assert.Equal(gold - price, venice.Gold);
        Assert.Equal(venice, csCity.Civ);
        Assert.True(csCity.IsPuppet);
        Assert.True(cityState.IsDefeated());
        Assert.DoesNotContain(merchant, venice.Units.GetCivUnits());
        Assert.Contains(venice.PopupAlerts, it => it.Type == AlertType.DiplomaticMarriage && it.Value == csCity.Id);
    }

    [Fact]
    public void ThePurchaseNeedsNoAllianceButGoldAPeaceAndANeighbouringCity()
    {
        var merchant = Merchant(3, 0);
        Assert.True(CsSide.GetInfluence() < 60f); // not an ally, still allowed
        venice.AddGold(-venice.Gold + 10);
        Assert.False(Buy(merchant).Accepted); // too poor
        venice.AddGold(10000);
        merchant.CurrentMovement = 0f;
        Assert.False(Buy(merchant).Accepted); // no movement left
        merchant.CurrentMovement = merchant.GetMaxMovement();
        venice.GetDiplomacyManager(cityState)!.DeclareWar();
        Assert.False(Buy(merchant).Accepted); // at war
        Assert.Equal(cityState, csCity.Civ);
    }

    [Fact]
    public void AMerchantFarFromTheCityOrWithoutTheUniqueCannotBuy()
    {
        Assert.False(Buy(Merchant(0, 0)).Accepted);
        game.GetTile(3, 0).CivilianUnit?.Destroy();
        Assert.False(Buy(Merchant(3, 0, "Great Merchant")).Accepted);
        Assert.Equal(cityState, csCity.Civ);
        Assert.False(cityState.IsDefeated());
    }

    // ---- influence anchors

    [Fact]
    public void ProtectionAnchorsInfluenceAtFiveInBnwButTenInGodsAndKings()
    {
        ((CityStateFunctions)cityState.CityStateFunctions).AddProtectorCiv(venice);
        Assert.Equal(DiplomaticStatus.Protector, CsSide.DiplomaticStatus);
        Assert.Equal(5f, CsSide.GetCityStateInfluenceRestingPoint());
        CsSide.SetFlag(DiplomacyFlags.WaryOf, 10);
        Assert.Equal(-15f, CsSide.GetCityStateInfluenceRestingPoint()); // 0 + 5 - 20

        var gk = new CityStateBnwTests(BaseRuleset.Civ_V_GnK);
        ((CityStateFunctions)gk.cityState.CityStateFunctions).AddProtectorCiv(gk.venice);
        Assert.Equal(10f, gk.CsSide.GetCityStateInfluenceRestingPoint());
        gk.CsSide.SetFlag(DiplomacyFlags.WaryOf, 10);
        Assert.Equal(-10f, gk.CsSide.GetCityStateInfluenceRestingPoint());
    }

    // ---- per-turn influence of the tenets

    private static void Adopt(CivilizationObject civ, string ideology, string tenet)
    {
        civ.Tech.Era = civ.GameInfo.Ruleset.Eras["Modern era"];
        civ.Policies.ChooseIdeology(civ.GameInfo.Ruleset.PolicyBranches[ideology]);
        civ.Policies.FreeTenets = 100;
        civ.Policies.Adopt(civ.GameInfo.Ruleset.Policies[tenet]);
    }

    [Fact]
    public void TreatyOrganizationAddsFourInfluencePerTurnWithProtectedCityStatesOnly()
    {
        Adopt(venice, "Order", "Treaty Organization");
        CsSide.SetInfluenceWithoutSideEffects(40f);
        CsSide.NextTurn();
        float withoutProtection = CsSide.Influence;
        CsSide.SetInfluenceWithoutSideEffects(40f);
        ((CityStateFunctions)cityState.CityStateFunctions).AddProtectorCiv(venice);
        CsSide.NextTurn();
        Assert.Equal(withoutProtection + 4f, CsSide.Influence);
    }

    [Fact]
    public void GunboatDiplomacyAddsSixInfluencePerTurnWithAfraidCityStatesOnly()
    {
        Adopt(venice, "Autocracy", "Gunboat Diplomacy");
        CsSide.SetInfluenceWithoutSideEffects(0f);
        Assert.NotEqual(RelationshipLevel.Afraid, CsSide.RelationshipLevel()); // no army: the city-state is not afraid
        CsSide.NextTurn();
        Assert.Equal(0f, CsSide.Influence);

        for (int i = 0; i < 12; i++) game.AddUnit("Swordsman", venice, game.GetTile(1 + (i % 3), -2 + (i / 3)));
        Assert.Equal(RelationshipLevel.Afraid, CsSide.RelationshipLevel());
        CsSide.NextTurn();
        Assert.Equal(6f, CsSide.Influence);
    }

    // ---- friendship kept on death

    /// <summary>The player takes the only city of the city-state: it is defeated and destroyed the way a battle does.</summary>
    private void Conquer()
    {
        csCity.MoveToCiv(venice);
        Unciv.Logic.Battle.Battle.DestroyIfDefeated(cityState, venice);
        Assert.True(cityState.IsDefeated());
    }

    [Theory]
    [InlineData(70f, 45f)] // ally
    [InlineData(35f, 15f)] // friend
    [InlineData(10f, 0f)]  // neutral
    public void ADeadCityStateKeepsTheInfluenceOfItsRelationshipAtDeath(float before, float kept)
    {
        CsSide.SetInfluence(before); // with side effects: the ally is the civ with the most influence
        Conquer();
        Assert.Equal(kept, CsSide.Influence);
    }

    [Fact]
    public void WorseThanNeutralRelationshipsAreLeftAloneAndGodsAndKingsKeepsEverything()
    {
        CsSide.SetInfluenceWithoutSideEffects(-25f);
        Conquer();
        Assert.Equal(-25f, CsSide.Influence);

        var gk = new CityStateBnwTests(BaseRuleset.Civ_V_GnK);
        gk.CsSide.SetInfluenceWithoutSideEffects(70f);
        gk.Conquer();
        Assert.Equal(70f, gk.CsSide.Influence);
    }

    // ---- the trade route quest

    [Fact]
    public void TheTradeRouteQuestIsOfferedWithASlotAndCompletesWithARoute()
    {
        var impl = QuestName.TradeRoute.GetImplementation();
        Assert.False(impl.IsValid(cityState, venice)); // no trade slot yet
        venice.Tech.AddTechnology("Animal Husbandry");
        Assert.True(impl.IsValid(cityState, venice));
        var assigned = new AssignedQuest(G, game.Ruleset.Quests["Trade Route"], cityState, venice);
        Assert.False(impl.IsComplete(cityState, assigned));

        var caravan = game.AddUnit("Caravan", venice, veniceCity.GetCenterTile());
        var established = session.Submit(venice.CivId, new EstablishTradeRoute { UnitId = caravan.Id, TargetCityId = csCity.Id });
        Assert.True(established.Accepted, established.ToString());
        Assert.True(impl.IsComplete(cityState, assigned));
        Assert.False(impl.IsValid(cityState, venice)); // already trading with it
    }

    [Fact]
    public void NoTradeRouteQuestWithoutTradeRoutesInTheRuleset()
    {
        var gk = new CityStateBnwTests(BaseRuleset.Civ_V_GnK);
        Assert.DoesNotContain("Trade Route", gk.game.Ruleset.Quests.Keys);
        Assert.False(QuestName.TradeRoute.GetImplementation().IsValid(gk.cityState, gk.venice));
    }
}
