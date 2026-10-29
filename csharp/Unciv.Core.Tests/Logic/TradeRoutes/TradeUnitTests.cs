using Unciv.Commands;
using Unciv.Commands.Units;
using Unciv.Core.Tests.Support;
using Unciv.Logic;
using Unciv.Logic.Files;
using Unciv.Logic.Map.MapUnits;
using Unciv.Logic.Trade.Routes;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Unique;
using CityObject = Unciv.Logic.City.City;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Core.Tests.Logic.TradeRoutes;

// B006 (bnw-spec 4.1.2, 4.1.3, 4.12): trade units on the REAL generated Brave New World ruleset: Caravan and Cargo Ship are buildable
// under the slot and target restrictions, can rebase between own cities and start routes; the Nau sells exotic goods.
// World: A (human) has cities at (-4,0) and (-4,3), B (human) at (4,0), hexagonal map radius 12 of desert, everybody has met.
[Collection("RulesetCache")]
public sealed class TradeUnitTests
{
    private readonly TestGame game = new(BaseRuleset.Civ_V_BNW);
    private readonly CivilizationObject a, b;
    private readonly CityObject cityA, cityA2, cityB;
    private readonly GameSession session;
    private GameInfo G => game.GameInfo;

    public TradeUnitTests() : this(4)
    {
    }

    private TradeUnitTests(int bX)
    {
        game.MakeHexagonalMap(12);
        game.SetSpeed("Standard");
        G.GameParameters.BaseRuleset = BaseRuleset.Civ_V_BNW.FullName();
        a = game.AddCiv(game.Ruleset.Nations["Portugal"], isPlayer: true);
        b = game.AddCiv(game.Ruleset.Nations["Brazil"], isPlayer: true);
        G.EngineOptions.VisibleMap = true;
        cityA = game.AddCity(a, game.GetTile(-4, 0));
        cityA2 = game.AddCity(a, game.GetTile(-4, 3));
        cityB = game.AddCity(b, game.GetTile(bX, 0));
        a.DiplomacyFunctions.MakeCivilizationsMeet(b);
        a.PopupAlerts.Clear();
        b.PopupAlerts.Clear();
        G.CurrentPlayerCiv = a;
        G.CurrentPlayer = a.CivId;
        session = new GameSession(G);
    }

    private static List<RejectionReasonType> Reasons(CivilizationObject civ, CityObject city, string unit) =>
        civ.GameInfo.Ruleset.Units[unit].GetRejectionReasons(city.CityConstructions).Select(it => it.Type).ToList();

    private MapUnit Trader(string name, CityObject city)
    {
        city.GetCenterTile().CivilianUnit?.Destroy();
        return game.AddUnit(name, a, city.GetCenterTile());
    }

    private CommandResult Submit(GameCommand command) => session.Submit(a.CivId, command);

    private void Ocean(int x, int y) => game.SetTileTerrain(new(x, y), "Ocean");

    private void Coast(int x, int y) => game.SetTileTerrain(new(x, y), "Coast");

    /// <summary>Military ships may only enter foreign waters with open borders.</summary>
    private void OpenBorders()
    {
        a.GetDiplomacyManager(b)!.HasOpenBorders = true;
        b.GetDiplomacyManager(a)!.HasOpenBorders = true;
    }

    /// <summary>A valid sample of each trade unit command for GameCommandTests.</summary>
    internal static (GameInfo Game, string Actor, GameCommand Command) CommandSample(string type)
    {
        var w = new TradeUnitTests();
        w.a.Tech.AddTechnology("Animal Husbandry");
        if (type == "rebase-trade-unit")
            return (w.G, w.a.CivId, new RebaseTradeUnit { UnitId = w.Trader("Caravan", w.cityA).Id, TargetCityId = w.cityA2.Id });
        w.a.Tech.AddTechnology("Astronomy");
        w.OpenBorders();
        w.Ocean(3, 0);
        var nau = w.game.AddUnit("Nau", w.a, w.game.GetTile(3, 0));
        return (w.G, w.a.CivId, new SellExoticGoods { UnitId = nau.Id, TargetCityId = w.cityB.Id });
    }

    // ---- the generated data

    [Fact]
    public void BnwCaravanAndCargoShipCarryTheFlagAndAreNotUnbuildable()
    {
        foreach (var name in new[] { "Caravan", "Cargo Ship" })
        {
            var unique = ((IHasUniques)game.Ruleset.Units[name]).GetMatchingUniques(UniqueType.CanEstablishTradeRoutes).ToList();
            Assert.Single(unique);
            Assert.False(((IHasUniques)game.Ruleset.Units[name]).HasUnique(UniqueType.Unbuildable));
        }
        Assert.Equal(1, ExoticGoods.LoadsCarried(Nau(0, 8)));
    }

    [Fact]
    public void BnwTechsAndBuildingsGiveSlotsRangeAndGold()
    {
        Assert.Equal(0, TradeRouteRules.Slots(a)); // no tech: Animal Husbandry is the first slot
        foreach (var tech in new[] { "Animal Husbandry", "Sailing" }) a.Tech.AddTechnology(tech);
        Assert.Equal(2, TradeRouteRules.Slots(a));
        Assert.Equal(10, TradeRouteRules.Range(a, cityA, TradeDomain.Land));
        Assert.Equal(20, TradeRouteRules.Range(a, cityA, TradeDomain.Sea));
        a.Tech.AddTechnology("Compass"); // +1 slot, +10 sea range
        Assert.Equal(3, TradeRouteRules.Slots(a));
        Assert.Equal(30, TradeRouteRules.Range(a, cityA, TradeDomain.Sea));
        cityA.CityConstructions.AddBuilding("Caravansary"); // +50% land range
        cityA.CityConstructions.AddBuilding("Harbor"); // +50% sea range
        Assert.Equal(15, TradeRouteRules.Range(a, cityA, TradeDomain.Land));
        Assert.Equal(45, TradeRouteRules.Range(a, cityA, TradeDomain.Sea));
        a.Tech.AddTechnology("Combustion"); // +10 land range before the percent: (10 + 10) * 150 / 100
        Assert.Equal(30, TradeRouteRules.Range(a, cityA, TradeDomain.Land));
        cityA.CityConstructions.AddBuilding("Colossus"); // +1 slot
        Assert.Equal(4, TradeRouteRules.Slots(a));
        Assert.Equal(0, TradeRouteRules.Slots(b)); // b has a capital but no techs
    }

    [Fact]
    public void VeniceDoublesItsSlots()
    {
        var venice = game.AddCiv(game.Ruleset.Nations["Venice"], isPlayer: true);
        game.AddCity(venice, game.GetTile(0, 8));
        venice.Tech.AddTechnology("Animal Husbandry");
        Assert.Equal(2, TradeRouteRules.Slots(venice));
    }

    // ---- build restrictions

    [Fact]
    public void CaravanNeedsItsTechThenASlotAndATarget()
    {
        Assert.Contains(RejectionReasonType.RequiresTech, Reasons(a, cityA, "Caravan"));
        a.Tech.AddTechnology("Animal Husbandry");
        Assert.Empty(Reasons(a, cityA, "Caravan"));
        Assert.True(game.Ruleset.Units["Caravan"].IsBuildable(cityA.CityConstructions));
        // the unit holds the only slot, even before it starts a route
        var caravan = Trader("Caravan", cityA);
        Assert.Equal(0, TradeRouteRules.SlotsFree(a));
        Assert.Contains(RejectionReasonType.NoTradeRouteSlot, Reasons(a, cityA, "Caravan"));
        caravan.Destroy();
        Assert.Empty(Reasons(a, cityA, "Caravan"));
        // the civ-level check (no city) knows slots only
        Assert.Empty(game.Ruleset.Units["Caravan"].GetRejectionReasons(a));
    }

    [Fact]
    public void ARouteInUseAlsoHoldsItsSlot()
    {
        a.Tech.AddTechnology("Animal Husbandry");
        var caravan = Trader("Caravan", cityA);
        Assert.True(Submit(new EstablishTradeRoute { UnitId = caravan.Id, TargetCityId = cityB.Id }).Accepted);
        Assert.Equal(1, TradeRouteRules.SlotsUsed(a));
        Assert.Equal(0, TradeRouteRules.TradeUnitsAlive(a));
        Assert.Contains(RejectionReasonType.NoTradeRouteSlot, Reasons(a, cityA, "Caravan"));
    }

    [Fact]
    public void NoTargetInRangeBlocksTheBuildAndTheCaravansaryExtendsTheRangeExactly()
    {
        var far = new TradeUnitTests(bX: 11); // 15 steps from city A
        far.a.Tech.AddTechnology("Animal Husbandry");
        Assert.Contains(RejectionReasonType.NoTradeRouteTarget, Reasons(far.a, far.cityA, "Caravan"));
        far.cityA.CityConstructions.AddBuilding("Caravansary"); // range 10 * 150 / 100 = 15
        Assert.Empty(Reasons(far.a, far.cityA, "Caravan"));
        var farther = new TradeUnitTests(bX: 12); // 16 steps
        farther.a.Tech.AddTechnology("Animal Husbandry");
        farther.cityA.CityConstructions.AddBuilding("Caravansary");
        Assert.Contains(RejectionReasonType.NoTradeRouteTarget, Reasons(farther.a, farther.cityA, "Caravan"));
    }

    [Fact]
    public void AtWarOrUnmetOwnersAreNoTarget()
    {
        a.Tech.AddTechnology("Animal Husbandry");
        a.GetDiplomacyManager(b)!.DiplomaticStatus = Unciv.Logic.Civilization.Diplomacy.DiplomaticStatus.War;
        b.GetDiplomacyManager(a)!.DiplomaticStatus = Unciv.Logic.Civilization.Diplomacy.DiplomaticStatus.War;
        Assert.Contains(RejectionReasonType.NoTradeRouteTarget, Reasons(a, cityA, "Caravan"));
    }

    [Fact]
    public void OwnCityIsATargetOnlyWithTheDomesticBuilding()
    {
        a.Tech.AddTechnology("Animal Husbandry");
        var alone = new TradeUnitTests(bX: 12);
        alone.a.Tech.AddTechnology("Animal Husbandry");
        Assert.True(alone.cityA.GetCenterTile().AerialDistanceTo(alone.cityA2.GetCenterTile()) <= 10);
        Assert.Contains(RejectionReasonType.NoTradeRouteTarget, Reasons(alone.a, alone.cityA, "Caravan"));
        alone.cityA.CityConstructions.AddBuilding("Granary"); // Allows domestic Food routes
        Assert.Empty(Reasons(alone.a, alone.cityA, "Caravan"));
    }

    [Fact]
    public void CargoShipNeedsAPortAndWaterToCross()
    {
        a.Tech.AddTechnology("Sailing");
        Assert.Contains(RejectionReasonType.WaterUnitsInCoastalCities, Reasons(a, cityA2, "Cargo Ship"));
        Coast(-5, 0); // city A becomes a port, but no water leads to another city
        Assert.Contains(RejectionReasonType.NoTradeRouteTarget, Reasons(a, cityA, "Cargo Ship"));
        for (int x = -3; x <= 3; x++) Coast(x, 0); // a sea lane to city B
        Assert.Empty(Reasons(a, cityA, "Cargo Ship"));
    }

    [Fact]
    public void GnkIsUnaffectedByTheTradeRestrictions()
    {
        var gnk = new TestGame(BaseRuleset.Civ_V_GnK);
        gnk.MakeHexagonalMap(4);
        var civ = gnk.AddCiv(isPlayer: true);
        var city = gnk.AddCity(civ, gnk.GetTile(0, 0));
        var unit = gnk.CreateBaseUnit("Civilian", "Can establish trade routes");
        Assert.Equal(0, TradeRouteRules.Slots(civ));
        Assert.DoesNotContain(unit.GetRejectionReasons(city.CityConstructions).Select(it => it.Type),
            it => it is RejectionReasonType.NoTradeRouteSlot or RejectionReasonType.NoTradeRouteTarget);
    }

    // ---- establishing with a real Caravan

    [Fact]
    public void RealCaravanStartsARealRoute()
    {
        a.Tech.AddTechnology("Animal Husbandry");
        var caravan = Trader("Caravan", cityA);
        Assert.True(Submit(new EstablishTradeRoute { UnitId = caravan.Id, TargetCityId = cityB.Id }).Accepted);
        var route = Assert.Single(G.TradeRoutes!);
        Assert.Equal(TradeDomain.Land, route.Domain);
        Assert.Equal("Caravan", route.UnitName);
        Assert.True(route.OriginYields[TradeYield.Gold] >= 100); // base 100 plus a twentieth of the cities' gold
        Assert.Equal(100, route.DestYields[TradeYield.Gold]);
    }

    // ---- rebasing

    [Fact]
    public void RebaseMovesTheIdleUnitToAnotherOwnCityAndSpendsItsMovement()
    {
        a.Tech.AddTechnology("Animal Husbandry");
        var caravan = Trader("Caravan", cityA);
        var result = Submit(new RebaseTradeUnit { UnitId = caravan.Id, TargetCityId = cityA2.Id });
        Assert.True(result.Accepted, result.Validation.Message);
        Assert.Equal(cityA2.GetCenterTile(), caravan.GetTile());
        Assert.Null(cityA.GetCenterTile().CivilianUnit);
        Assert.Equal(0f, caravan.CurrentMovement);
        // no movement left now
        Assert.Equal(CommandErrors.NotAllowed, Submit(new RebaseTradeUnit { UnitId = caravan.Id, TargetCityId = cityA.Id }).Validation.Code);
    }

    [Fact]
    public void RebaseRefusals()
    {
        a.Tech.AddTechnology("Animal Husbandry");
        var caravan = Trader("Caravan", cityA);
        Code(caravan, cityA, CommandErrors.InvalidTarget); // already there
        Code(caravan, cityB, CommandErrors.NotYourCity);
        Assert.Equal(CommandErrors.UnknownCity, Submit(new RebaseTradeUnit { UnitId = caravan.Id, TargetCityId = "nope" }).Validation.Code);
        var worker = game.AddUnit("Worker", a, cityA2.GetCenterTile().Neighbors.First());
        Assert.Equal(CommandErrors.NotAllowed, Submit(new RebaseTradeUnit { UnitId = worker.Id, TargetCityId = cityA2.Id }).Validation.Code);
        Trader("Caravan", cityA2); // the target holds a civilian already
        Code(caravan, cityA2, CommandErrors.InvalidTarget);
        // out of range: a city 16 steps away
        var far = game.AddCity(a, game.GetTile(12, 0));
        Code(caravan, far, CommandErrors.InvalidTarget);
        // a ship cannot go to an inland city
        a.Tech.AddTechnology("Sailing");
        caravan.Destroy();
        Coast(-5, 0);
        var ship = Trader("Cargo Ship", cityA);
        Code(ship, cityA2, CommandErrors.InvalidTarget);
    }

    private void Code(MapUnit unit, CityObject target, string code) =>
        Assert.Equal(code, Submit(new RebaseTradeUnit { UnitId = unit.Id, TargetCityId = target.Id }).Validation.Code);

    // ---- exotic goods

    private MapUnit Nau(int x = 3, int y = 0)
    {
        a.Tech.AddTechnology("Astronomy");
        OpenBorders();
        Ocean(x, y);
        return game.AddUnit("Nau", a, game.GetTile(x, y));
    }

    [Fact]
    public void NauSellsAScaledLoadOnceForGoldAndExperience()
    {
        var nau = Nau();
        Assert.Equal(1, ExoticGoods.LoadsLeft(nau));
        int gold = a.Gold;
        var result = Submit(new SellExoticGoods { UnitId = nau.Id, TargetCityId = cityB.Id });
        Assert.True(result.Accepted, result.Validation.Message);
        // the capital is 8 tiles from city B: 100 + 300 * 8 / 40 gold and 10 + 20 * 8 / 40 experience
        Assert.Equal(gold + 160, a.Gold);
        Assert.Equal(14, nau.Promotions.XP);
        Assert.Equal(0, ExoticGoods.LoadsLeft(nau));
        Assert.Equal(0f, nau.CurrentMovement);
        nau.CurrentMovement = nau.GetMaxMovement();
        Assert.Equal(CommandErrors.NotAllowed, Submit(new SellExoticGoods { UnitId = nau.Id, TargetCityId = cityB.Id }).Validation.Code);
    }

    [Theory]
    [InlineData(1, 107, 10)] // 300 / 40 and 20 / 40 truncate
    [InlineData(8, 160, 14)]
    [InlineData(16, 220, 18)]
    public void ExoticGoodsScaleLinearlyWithTheDistanceFromTheCapital(int distance, int gold, int xp)
    {
        var nau = Nau();
        var target = distance == 8 ? cityB : game.AddCity(b, game.GetTile(-4 + distance, 0));
        Assert.Equal(distance, a.GetCapital()!.GetCenterTile().AerialDistanceTo(target.GetCenterTile()));
        Assert.Equal(gold, ExoticGoods.GoldFor(nau, target));
        Assert.Equal(xp, ExoticGoods.XpFor(nau, target));
    }

    [Fact]
    public void ExoticGoodsAreWorthTheMinimumWithoutACapital()
    {
        var c = game.AddCiv();
        game.Ruleset.Units["Nau"].SetRuleset(game.Ruleset);
        var nau = game.Ruleset.Units["Nau"].NewMapUnit(c);
        Assert.Equal(100, ExoticGoods.GoldFor(nau, cityB));
        Assert.Equal(10, ExoticGoods.XpFor(nau, cityB));
    }

    [Fact]
    public void ExoticGoodsRefusals()
    {
        var nau = Nau();
        Assert.Equal(CommandErrors.UnknownCity, Submit(new SellExoticGoods { UnitId = nau.Id, TargetCityId = "nope" }).Validation.Code);
        Assert.Equal(CommandErrors.InvalidTarget, Submit(new SellExoticGoods { UnitId = nau.Id, TargetCityId = cityA.Id }).Validation.Code); // own city
        nau.RemoveFromTile();
        Ocean(2, 0);
        nau.PutInTile(game.GetTile(2, 0)); // two tiles away
        Assert.Equal(CommandErrors.InvalidTarget, Submit(new SellExoticGoods { UnitId = nau.Id, TargetCityId = cityB.Id }).Validation.Code);
        nau.RemoveFromTile();
        nau.PutInTile(game.GetTile(3, 0));
        nau.CurrentMovement = 0f;
        Assert.Equal(CommandErrors.NotAllowed, Submit(new SellExoticGoods { UnitId = nau.Id, TargetCityId = cityB.Id }).Validation.Code);
        nau.CurrentMovement = nau.GetMaxMovement();
        a.GetDiplomacyManager(b)!.DiplomaticStatus = Unciv.Logic.Civilization.Diplomacy.DiplomaticStatus.War;
        Assert.Equal(CommandErrors.NotAllowed, Submit(new SellExoticGoods { UnitId = nau.Id, TargetCityId = cityB.Id }).Validation.Code);
        var plain = game.AddUnit("Worker", a, cityA2.GetCenterTile().Neighbors.First());
        Assert.Equal(CommandErrors.NotAllowed, Submit(new SellExoticGoods { UnitId = plain.Id, TargetCityId = cityB.Id }).Validation.Code);
    }

    [Fact]
    public void ExoticGoodsSoldSurvivesSaveLoadAndCloning()
    {
        var nau = Nau();
        Assert.True(Submit(new SellExoticGoods { UnitId = nau.Id, TargetCityId = cityB.Id }).Accepted);
        Assert.Equal(1, nau.Clone().ExoticGoodsSold);
        var loaded = UncivFiles.GameInfoFromString(UncivFiles.GameInfoToString(G, forceZip: false));
        var back = loaded.GetCivilization(a.CivId).Units.GetCivUnits().Single(it => it.Name == "Nau");
        Assert.Equal(1, back.ExoticGoodsSold);
        Assert.Equal(0, ExoticGoods.LoadsLeft(back));
    }
}
