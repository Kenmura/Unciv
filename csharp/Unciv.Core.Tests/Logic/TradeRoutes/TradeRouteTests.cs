using Unciv.Commands;
using Unciv.Commands.Events;
using Unciv.Commands.Units;
using Unciv.Core.Tests.Commands.Diplomacy;
using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Logic.Files;
using Unciv.Logic.Map.MapUnits;
using Unciv.Logic.Trade.Routes;
using Unciv.Models.StatTypes;
using CityObject = Unciv.Logic.City.City;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Core.Tests.Logic.TradeRoutes;

// B020 (bnw-spec 4.1, section 6.1): trade routes. Hand-computed cases on the diplomacy world: A (acts) at (-4,0), B at (4,0), AI C at (0,6),
// city-state S at (0,-6). The BNW constants are set to the spec values (base 100, divisor 20, no exclusive bonus), speed is Standard.
[Collection("RulesetCache")]
public class TradeRouteTests
{
    private static DiplomacyWorld NewWorld(string[]? aUniques = null, string[]? bUniques = null)
    {
        var w = new DiplomacyWorld(aUniques ?? ["[+1] trade route slots"], ["Enables international trade routes"], bUniques);
        var constants = w.Game.Ruleset.ModOptions.Constants;
        constants.InternationalTradeBase = 100;
        constants.InternationalTradeCityGptDivisor = 20;
        constants.InternationalTradeExclusiveConnection = 0;
        w.Game.SetSpeed("Standard");
        foreach (var civ in w.Game.GameInfo.Civilizations)
            foreach (var city in civ.Cities) SetGold(city, 0);
        return w;
    }

    /// <summary>A valid <c>establish-trade-route</c> on a fresh world, for the command sample test.</summary>
    internal static (DiplomacyWorld World, GameCommand Command) NewCommandSample()
    {
        var w = NewWorld();
        return (w, Cmd(Trader(w, w.A, CityOf(w.A)), CityOf(w.B)));
    }

    private static void SetGold(CityObject city, float gold) => city.CityStats.CurrentCityStats = new Stats { Gold = gold };

    private static CityObject CityOf(CivilizationObject civ, int index = 0) => civ.Cities[index];

    private static MapUnit Trader(DiplomacyWorld w, CivilizationObject civ, CityObject city, bool sea = false)
    {
        var baseUnit = w.Game.CreateBaseUnit(sea ? "Civilian Water" : "Civilian", "Can establish trade routes");
        baseUnit.Movement = 2;
        city.GetCenterTile().CivilianUnit?.Destroy(); // one civilian per tile
        return w.Game.AddUnit(baseUnit.Name, civ, city.GetCenterTile());
    }

    private static TradeRouteYields.Pair Yields(DiplomacyWorld w, CityObject from, CityObject to, bool sea = false, TradeRouteKind kind = TradeRouteKind.International) =>
        TradeRouteYields.Compute(from, to, sea ? TradeDomain.Sea : TradeDomain.Land, kind);

    private static EstablishTradeRoute Cmd(MapUnit unit, CityObject target, TradeRouteKind kind = TradeRouteKind.International) =>
        new() { UnitId = unit.Id, TargetCityId = target.Id, Kind = kind };

    private static void WaterCorridor(DiplomacyWorld w)
    {
        for (int x = -3; x <= 3; x++) w.Game.SetTileTerrain(new(x, 0), "Ocean");
    }

    private static void AddTechs(CivilizationObject civ, int count)
    {
        foreach (var tech in civ.GameInfo.Ruleset.Technologies.Keys.Where(it => !civ.Tech.IsResearched(it)).Take(count)) civ.Tech.AddTechnology(tech);
    }

    // ---- yields

    [Fact]
    public void PlainLandRouteGivesTheBaseGoldToBothEnds()
    {
        var w = NewWorld();
        var pair = Yields(w, CityOf(w.A), CityOf(w.B));
        Assert.Equal(100, pair.Origin[TradeYield.Gold]);
        Assert.Equal(100, pair.Dest[TradeYield.Gold]);
        Assert.Equal(0, pair.Origin[TradeYield.Science]);
        Assert.Equal(0, pair.Dest[TradeYield.Science]);
    }

    [Fact]
    public void SeaRouteDoublesTheGold()
    {
        var w = NewWorld();
        var pair = Yields(w, CityOf(w.A), CityOf(w.B), sea: true);
        Assert.Equal(200, pair.Origin[TradeYield.Gold]);
        Assert.Equal(200, pair.Dest[TradeYield.Gold]);
    }

    [Fact]
    public void CityGoldPerTurnAddsOneTwentiethOfEachEndToTheOriginOnly()
    {
        var w = NewWorld();
        SetGold(CityOf(w.A), 4);
        SetGold(CityOf(w.B), 10);
        var pair = Yields(w, CityOf(w.A), CityOf(w.B));
        Assert.Equal(100 + 20 + 50, pair.Origin[TradeYield.Gold]);
        Assert.Equal(100, pair.Dest[TradeYield.Gold]);
    }

    [Fact]
    public void RiverAddsAQuarterToALandRouteOnlyOnThatEnd()
    {
        var w = NewWorld();
        var tile = CityOf(w.A).GetCenterTile();
        tile.SetConnectedByRiver(tile.Neighbors.First(), true);
        var land = Yields(w, CityOf(w.A), CityOf(w.B));
        Assert.Equal(125, land.Origin[TradeYield.Gold]);
        Assert.Equal(100, land.Dest[TradeYield.Gold]);
        Assert.Equal(200, Yields(w, CityOf(w.A), CityOf(w.B), sea: true).Origin[TradeYield.Gold]);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 100)] // a lead of one still gives 1: ceil(0.5)
    [InlineData(2, 100)]
    [InlineData(3, 200)]
    [InlineData(4, 200)]
    public void ScienceIsHalfTheTechLeadRoundedUpToTheBehindEnd(int lead, int expected)
    {
        var w = NewWorld();
        AddTechs(w.B, lead);
        var pair = Yields(w, CityOf(w.A), CityOf(w.B));
        Assert.Equal(expected, pair.Origin[TradeYield.Science]);
        Assert.Equal(0, pair.Dest[TradeYield.Science]);
        var back = Yields(w, CityOf(w.B), CityOf(w.A));
        Assert.Equal(0, back.Origin[TradeYield.Science]);
        Assert.Equal(expected, back.Dest[TradeYield.Science]);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(0, 0)]
    [InlineData(-3, 0)]
    public void ScienceBaseBoundaries(int lead, int expected) => Assert.Equal(expected, TradeRouteYields.ScienceBase(lead));

    [Fact]
    public void ResourcesOnlyOneCityHasAddHalfAGoldEachAndPortugalScalesThem()
    {
        var w = NewWorld(["[+1] trade route slots", "[+100]% resources received from trade routes"]);
        var tile = w.Game.GetTile(-5, 0);
        w.Game.AddTileToCity(CityOf(w.A), tile);
        tile.SetTileResource("Iron");
        var pair = Yields(w, CityOf(w.A), CityOf(w.B));
        Assert.Equal(100 + 50 * 2, pair.Origin[TradeYield.Gold]);

        var other = w.Game.GetTile(5, 0);
        w.Game.AddTileToCity(CityOf(w.B), other);
        other.SetTileResource("Iron");
        Assert.Equal(100, Yields(w, CityOf(w.A), CityOf(w.B)).Origin[TradeYield.Gold]); // both have it
        other.SetTileResource("Coal");
        Assert.Equal(100 + 2 * 50 * 2, Yields(w, CityOf(w.A), CityOf(w.B)).Origin[TradeYield.Gold]);
    }

    [Fact]
    public void BuildingsAndPoliciesAddGoldAndTheCivWideModifierScalesTheBuildings()
    {
        var w = NewWorld(["[+1] trade route slots", "[+50]% yields from trade buildings", "[+30] Gold (x1/100) from land trade routes [in this city]"]);
        var market = w.Game.CreateBuilding("[+40] Gold (x1/100) from land trade routes [in this city]", "[+2] Gold to the owner of an international trade route from this city");
        CityOf(w.A).CityConstructions.AddBuilding(market);
        SetGold(CityOf(w.A), 0);
        var pair = Yields(w, CityOf(w.A), CityOf(w.B));
        // policy-like row 30 + building row 40 x 150% = 60 + owner's whole gold 2 x 100 x 150% = 300
        Assert.Equal(100 + 30 + 60 + 300, pair.Origin[TradeYield.Gold]);
    }

    [Fact]
    public void DestinationBuildingGoldGoesToTheDestinationOwner()
    {
        var w = NewWorld();
        var building = w.Game.CreateBuilding("[+3] Gold to the owner of an international trade route to this city");
        CityOf(w.B).CityConstructions.AddBuilding(building);
        SetGold(CityOf(w.B), 0);
        var pair = Yields(w, CityOf(w.A), CityOf(w.B));
        Assert.Equal(100 + 300, pair.Dest[TradeYield.Gold]);
        Assert.Equal(100, pair.Origin[TradeYield.Gold]);
    }

    [Fact]
    public void DestinationBuildingsWithRouteGoldBonusPayTheDestinationOwner()
    {
        var w = NewWorld();
        var building = w.Game.CreateBuilding("[+40] Gold (x1/100) from land trade routes [in this city]");
        CityOf(w.B).CityConstructions.AddBuilding(building);
        SetGold(CityOf(w.B), 0);
        Assert.Equal(140, Yields(w, CityOf(w.A), CityOf(w.B)).Dest[TradeYield.Gold]);
        Assert.Equal(100, Yields(w, CityOf(w.A), CityOf(w.B)).Origin[TradeYield.Gold]);
    }

    [Fact]
    public void ACitysOwnRouteGoldDoesNotFeedBackIntoItsRouteValue()
    {
        var (w, route, _) = Established();
        SetGold(CityOf(w.A), 4 + route.OriginYields[TradeYield.Gold] / 100f); // 4 of its own plus the route's 1
        SetGold(CityOf(w.B), route.DestYields[TradeYield.Gold] / 100f);
        TradeRouteTurns.StartTurn(w.A);
        Assert.Equal(120, route.OriginYields[TradeYield.Gold]);
    }

    [Fact]
    public void CityStateRoutesGetPolicyGoldAndNoBuildingGold()
    {
        var w = NewWorld(["[+1] trade route slots", "[+2] Gold from trade routes with city-states"]);
        var building = w.Game.CreateBuilding("[+2] Gold to the owner of an international trade route from this city");
        CityOf(w.A).CityConstructions.AddBuilding(building);
        SetGold(CityOf(w.A), 0);
        var toCityState = Yields(w, CityOf(w.A), CityOf(w.S));
        Assert.Equal(100 + 200, toCityState.Origin[TradeYield.Gold]);
        var toMajor = Yields(w, CityOf(w.A), CityOf(w.B));
        Assert.Equal(100 + 200, toMajor.Origin[TradeYield.Gold]); // building gold here instead of the policy
    }

    [Fact]
    public void MoroccoStyleTraitAddsGoldToTheSumAndOtherStatsOnTheirOwn()
    {
        var w = NewWorld(["[+1] trade route slots", "[+1 Gold, +2 Science] for each trade partner"], ["[+1 Faith] from incoming trade routes"]);
        var pair = Yields(w, CityOf(w.A), CityOf(w.B));
        Assert.Equal(100 + 100, pair.Origin[TradeYield.Gold]);
        Assert.Equal(200, pair.Origin[TradeYield.Science]);
        Assert.Equal(100, pair.Dest[TradeYield.Faith]);
    }

    [Fact]
    public void ExclusiveConnectionBonusEndsWhenAnotherCivHasARouteToTheSameCity()
    {
        var w = NewWorld();
        w.Game.Ruleset.ModOptions.Constants.InternationalTradeExclusiveConnection = 25;
        Assert.Equal(125, Yields(w, CityOf(w.A), CityOf(w.B)).Origin[TradeYield.Gold]);
        var unit = Trader(w, w.C, CityOf(w.C));
        w.Game.GameInfo.TradeRoutes = [new TradeRoute { Id = 9, OriginCityId = CityOf(w.C).Id, DestCityId = CityOf(w.B).Id, OriginOwner = w.C.CivId, DestOwner = w.B.CivId, UnitId = unit.Id }];
        Assert.Equal(100, Yields(w, CityOf(w.A), CityOf(w.B)).Origin[TradeYield.Gold]);
        // the route's own presence does not count against itself
        Assert.Equal(125, TradeRouteYields.Compute(CityOf(w.C), CityOf(w.B), TradeDomain.Land, TradeRouteKind.International, excludeRouteId: 9).Origin[TradeYield.Gold]);
    }

    [Fact]
    public void GoldNeverDropsBelowOneGold()
    {
        var w = NewWorld();
        w.Game.Ruleset.ModOptions.Constants.InternationalTradeBase = 0;
        Assert.Equal(100, Yields(w, CityOf(w.A), CityOf(w.B)).Origin[TradeYield.Gold]);
    }

    [Theory]
    [InlineData(TradeRouteKind.Food, false, 300)]
    [InlineData(TradeRouteKind.Production, false, 300)]
    [InlineData(TradeRouteKind.Food, true, 600)]
    public void DomesticRoutesYieldOnlyToTheDestination(TradeRouteKind kind, bool sea, int expected)
    {
        var w = NewWorld();
        var second = w.Game.AddCity(w.A, w.Game.GetTile(-6, 0));
        var pair = Yields(w, CityOf(w.A), second, sea, kind);
        int index = kind == TradeRouteKind.Food ? TradeYield.Food : TradeYield.Production;
        Assert.Equal(expected, pair.Dest[index]);
        Assert.All(pair.Origin, it => Assert.Equal(0, it));
        Assert.Equal(0, pair.Dest.Where((_, i) => i != index).Sum());
    }

    [Fact]
    public void DomesticYieldGrowsWithThePolicyPercentAndTheEra()
    {
        var w = NewWorld(["[+1] trade route slots", "[+50]% [Food] from domestic trade routes"]);
        var second = w.Game.AddCity(w.A, w.Game.GetTile(-6, 0));
        Assert.Equal(450, Yields(w, CityOf(w.A), second, kind: TradeRouteKind.Food).Dest[TradeYield.Food]);
        w.A.GetEra().TradeRouteFoodBonusTimes100 = 100;
        Assert.Equal(600, Yields(w, CityOf(w.A), second, kind: TradeRouteKind.Food).Dest[TradeYield.Food]);
    }

    // ---- slots and range

    [Theory]
    [InlineData(1, 0, 1)]
    [InlineData(3, 0, 3)]
    [InlineData(3, 100, 6)] // Venice
    [InlineData(1, 50, 1)] // truncating
    [InlineData(3, 50, 4)]
    public void SlotsAreFlatTimesOnePlusPercent(int flat, int percent, int expected)
    {
        var uniques = new List<string> { $"[+{flat}] trade route slots" };
        if (percent != 0) uniques.Add($"[+{percent}]% trade route slots");
        var w = NewWorld([.. uniques]);
        Assert.Equal(expected, TradeRouteRules.Slots(w.A));
    }

    [Fact]
    public void NoCapitalMeansNoSlots()
    {
        var w = NewWorld();
        Assert.Equal(1, TradeRouteRules.Slots(w.A));
        w.Game.GameInfo.TradeRoutes = null;
        CityOf(w.A).CityConstructions.RemoveBuilding("Palace");
        Assert.Equal(0, TradeRouteRules.Slots(w.A));
    }

    [Fact]
    public void RangeCombinesBaseFlatAndCityPercent()
    {
        var w = NewWorld(["[+1] trade route slots", "[+10] land trade route range", "[+10] sea trade route range"]);
        Assert.Equal(20, TradeRouteRules.Range(w.A, CityOf(w.A), TradeDomain.Land));
        Assert.Equal(30, TradeRouteRules.Range(w.A, CityOf(w.A), TradeDomain.Sea));
        var building = w.Game.CreateBuilding("[+50]% land trade route range [in this city]");
        CityOf(w.A).CityConstructions.AddBuilding(building);
        Assert.Equal(30, TradeRouteRules.Range(w.A, CityOf(w.A), TradeDomain.Land));
        Assert.Equal(30, TradeRouteRules.Range(w.A, CityOf(w.A), TradeDomain.Sea));
    }

    [Fact]
    public void PathBoundaryIsAnExactStepCountAndIsDeterministic()
    {
        var w = NewWorld();
        var from = CityOf(w.A).GetCenterTile();
        var to = CityOf(w.B).GetCenterTile();
        var path = TradeRouteRules.FindPath(from, to, TradeDomain.Land, 8);
        Assert.NotNull(path);
        Assert.Equal(9, path.Count);
        Assert.Equal(from.Position, path[0]);
        Assert.Equal(to.Position, path[^1]);
        Assert.Null(TradeRouteRules.FindPath(from, to, TradeDomain.Land, 7));
        Assert.Equal(path, TradeRouteRules.FindPath(from, to, TradeDomain.Land, 12));
        Assert.Null(TradeRouteRules.FindPath(from, from, TradeDomain.Land, 8));
        Assert.Null(TradeRouteRules.FindPath(from, to, TradeDomain.Sea, 20)); // no water in between
    }

    [Fact]
    public void SeaPathsNeedWaterAndLandPathsAvoidIt()
    {
        var w = NewWorld();
        WaterCorridor(w);
        var from = CityOf(w.A).GetCenterTile();
        var to = CityOf(w.B).GetCenterTile();
        Assert.Equal(9, TradeRouteRules.FindPath(from, to, TradeDomain.Sea, 20)!.Count);
        var land = TradeRouteRules.FindPath(from, to, TradeDomain.Land, 10)!;
        Assert.DoesNotContain(land.Skip(1).SkipLast(1), it => w.Game.GetTile(it).IsWater);
    }

    // ---- the command

    [Fact]
    public void EstablishingARouteConsumesTheUnitUsesASlotAndCreatesTheRoute()
    {
        var w = NewWorld();
        var unit = Trader(w, w.A, CityOf(w.A));
        var result = w.Ok(Cmd(unit, CityOf(w.B)));
        var route = Assert.Single(w.Game.GameInfo.TradeRoutes!);
        Assert.Equal(1, route.Id);
        Assert.Equal(w.A.CivId, route.OriginOwner);
        Assert.Equal(w.B.CivId, route.DestOwner);
        Assert.Equal(TradeDomain.Land, route.Domain);
        Assert.Equal(9, route.Path.Count);
        Assert.Equal(100, route.OriginYields[TradeYield.Gold]);
        Assert.Equal(100, route.DestYields[TradeYield.Gold]);
        Assert.Equal(1, TradeRouteRules.SlotsUsed(w.A));
        Assert.DoesNotContain(w.A.Units.GetCivUnits(), it => it.Id == unit.Id);
        Assert.Contains(result.Events, it => it is TradeRouteEstablished { RouteId: 1 });
        Assert.Contains(w.B.Notifications, it => it.Text.Contains("established a trade route"));
    }

    [Fact]
    public void ARouteMovesGoldIntoTheCityStatsLineAndOnlyWhereARouteTouches()
    {
        var w = NewWorld();
        var unit = Trader(w, w.A, CityOf(w.A));
        CityOf(w.A).CityStats.Update();
        Assert.DoesNotContain("Trade route yields", CityOf(w.A).CityStats.BaseStatTree.Children.Keys);
        w.Ok(Cmd(unit, CityOf(w.B)));
        CityOf(w.A).CityStats.Update();
        CityOf(w.B).CityStats.Update();
        CityOf(w.C).CityStats.Update();
        var route = w.Game.GameInfo.TradeRoutes![0];
        Assert.Equal(route.OriginYields[TradeYield.Gold] / 100f, CityOf(w.A).CityStats.BaseStatTree.Children["Trade route yields"].TotalStats.Gold, 0.001f);
        Assert.Equal(route.DestYields[TradeYield.Gold] / 100f, CityOf(w.B).CityStats.BaseStatTree.Children["Trade route yields"].TotalStats.Gold, 0.001f);
        Assert.DoesNotContain("Trade route yields", CityOf(w.C).CityStats.BaseStatTree.Children.Keys);
    }

    [Fact]
    public void RefusalsLeaveStateUntouched()
    {
        var w = NewWorld(["[+2] trade route slots"]);
        var far = w.Game.AddCity(w.B, w.Game.GetTile(8, 2));
        var unit = Trader(w, w.A, CityOf(w.A));
        w.Rejected(Cmd(unit, far), CommandErrors.InvalidTarget); // 12 steps, range 10
        w.Rejected(new EstablishTradeRoute { UnitId = unit.Id, TargetCityId = "nowhere" }, CommandErrors.UnknownCity);
        w.Rejected(Cmd(unit, CityOf(w.A)), CommandErrors.InvalidTarget);
        w.Rejected(Cmd(unit, CityOf(w.B), (TradeRouteKind)7), CommandErrors.InvalidTarget);
        w.Rejected(Cmd(unit, CityOf(w.B), TradeRouteKind.Food), CommandErrors.InvalidTarget);
        w.Rejected(Cmd(unit, CityOf(w.B)), CommandErrors.NotYourTurn, actor: w.B.CivId);
        var plain = w.Game.CreateBaseUnit("Civilian");
        plain.Movement = 2;
        unit.Destroy();
        var notTrader = w.Game.AddUnit(plain.Name, w.A, CityOf(w.A).GetCenterTile());
        w.Rejected(Cmd(notTrader, CityOf(w.B)), CommandErrors.NotAllowed);
        notTrader.Destroy();
        var outside = Trader(w, w.A, CityOf(w.A));
        outside.CurrentMovement = 0;
        w.Rejected(Cmd(outside, CityOf(w.B)), CommandErrors.NotAllowed);
        string name = outside.Name;
        outside.Destroy();
        var away = w.Game.AddUnit(name, w.A, w.Game.GetTile(-3, 3));
        w.Rejected(Cmd(away, CityOf(w.B)), CommandErrors.NotAllowed);
        away.Destroy();
        var home = Trader(w, w.A, CityOf(w.A)); // replaces the first one
        w.MakeWar(w.A, w.B);
        w.Rejected(Cmd(home, CityOf(w.B)), CommandErrors.NotAllowed);
    }

    [Fact]
    public void DistanceExactlyAtTheRangeIsAcceptedOneBeyondIsNot()
    {
        var w = NewWorld();
        var edge = w.Game.AddCity(w.B, w.Game.GetTile(6, 0));
        var over = w.Game.AddCity(w.B, w.Game.GetTile(7, 0));
        var unit = Trader(w, w.A, CityOf(w.A));
        w.Rejected(Cmd(unit, over), CommandErrors.InvalidTarget);
        w.Ok(Cmd(unit, edge));
        Assert.Equal(11, w.Game.GameInfo.TradeRoutes![0].Path.Count);
    }

    [Fact]
    public void ASecondRouteNeedsASlotAndADuplicateIsRefused()
    {
        var w = NewWorld(["[+2] trade route slots"]);
        var second = w.Game.AddCity(w.B, w.Game.GetTile(5, 1));
        w.Ok(Cmd(Trader(w, w.A, CityOf(w.A)), CityOf(w.B)));
        w.Rejected(Cmd(Trader(w, w.A, CityOf(w.A)), CityOf(w.B)), CommandErrors.NotAllowed);
        w.Ok(Cmd(Trader(w, w.A, CityOf(w.A)), second));
        w.Rejected(Cmd(Trader(w, w.A, CityOf(w.A)), CityOf(w.S)), CommandErrors.NotAllowed); // both slots in use
        Assert.Equal(2, TradeRouteRules.SlotsUsed(w.A));
    }

    [Fact]
    public void SeaRoutesNeedWaterAndAnEnabledRulesetIsRequired()
    {
        var w = NewWorld();
        var ship = Trader(w, w.A, CityOf(w.A), sea: true);
        w.Rejected(Cmd(ship, CityOf(w.B)), CommandErrors.InvalidTarget);
        WaterCorridor(w);
        w.Ok(Cmd(ship, CityOf(w.B)));
        Assert.Equal(TradeDomain.Sea, w.Game.GameInfo.TradeRoutes![0].Domain);
        Assert.Equal(200, w.Game.GameInfo.TradeRoutes[0].OriginYields[TradeYield.Gold]);

        var plain = new DiplomacyWorld(["[+1] trade route slots"]);
        var unit = Trader(plain, plain.A, CityOf(plain.A));
        plain.Rejected(Cmd(unit, CityOf(plain.B)), CommandErrors.NotAllowed);
        Assert.Null(plain.Game.GameInfo.TradeRoutes);
        Assert.Empty(TradeRouteRules.Routes(plain.Game.GameInfo));
    }

    [Fact]
    public void DomesticRoutesNeedTheBuildingAndGoToOwnCitiesOnly()
    {
        var w = NewWorld();
        var second = w.Game.AddCity(w.A, w.Game.GetTile(-6, 0));
        var unit = Trader(w, w.A, CityOf(w.A));
        w.Rejected(Cmd(unit, second, TradeRouteKind.Food), CommandErrors.NotAllowed);
        w.Rejected(Cmd(unit, second), CommandErrors.InvalidTarget);
        var granary = w.Game.CreateBuilding("Allows domestic [Food] trade routes from this city");
        CityOf(w.A).CityConstructions.AddBuilding(granary);
        w.Rejected(Cmd(unit, second, TradeRouteKind.Production), CommandErrors.NotAllowed);
        w.Ok(Cmd(unit, second, TradeRouteKind.Food));
        var route = w.Game.GameInfo.TradeRoutes![0];
        Assert.Equal(300, route.DestYields[TradeYield.Food]);
        Assert.Equal(0, route.OriginYields[TradeYield.Food]);
        Assert.Equal(second.Id, route.DestCityId);
    }

    // ---- lifecycle

    private static (DiplomacyWorld W, TradeRoute Route, int UnitId) Established()
    {
        var w = NewWorld();
        var unit = Trader(w, w.A, CityOf(w.A));
        int id = unit.Id;
        w.Ok(Cmd(unit, CityOf(w.B)));
        return (w, w.Game.GameInfo.TradeRoutes![0], id);
    }

    [Fact]
    public void CircuitsAreComputedFromPathLengthSpeedAndUnitMovement()
    {
        var (w, route, _) = Established();
        // 8 steps each way at 2 steps a turn: 8 turns a round trip; 30 turns at Standard: 4 circuits
        Assert.Equal(4, route.CircuitsToComplete);
        Assert.Equal(w.Game.GameInfo.Turns + 32, route.TurnRouteComplete);
    }

    [Fact]
    public void TheUnitWalksBackAndForthAndTheRouteCompletesReturningTheUnit()
    {
        var (w, route, _) = Established();
        var game = w.Game.GameInfo;
        TradeRouteTurns.StartTurn(w.A);
        Assert.Equal(2, route.UnitPathIndex);
        Assert.Equal(route.Path[2], route.CurrentPosition);
        for (int i = 0; i < 3; i++) TradeRouteTurns.StartTurn(w.A);
        Assert.Equal(8, route.UnitPathIndex);
        Assert.False(route.MovingForward);
        for (int i = 0; i < 4; i++) TradeRouteTurns.StartTurn(w.A);
        Assert.Equal(1, route.CircuitsCompleted);
        Assert.Equal(0, route.UnitPathIndex);
        Assert.True(route.MovingForward);
        // B's turn start does not move A's unit
        TradeRouteTurns.StartTurn(w.B);
        Assert.Equal(0, route.UnitPathIndex);
        for (int i = 0; i < 23; i++) TradeRouteTurns.StartTurn(w.A);
        Assert.Single(game.TradeRoutes!);
        TradeRouteTurns.StartTurn(w.A);
        Assert.Empty(game.TradeRoutes!);
        Assert.Contains(w.A.Units.GetCivUnits(), it => it.Name == route.UnitName && it.GetTile() == CityOf(w.A).GetCenterTile());
        Assert.True(TradeRouteTurns.WasRecentlyEnded(game, route.OriginCityId, route.DestCityId));
        Assert.Contains(w.A.Notifications, it => it.Text.Contains("is complete"));
    }

    [Fact]
    public void YieldsAreRecomputedAtTheStartOfEachTurn()
    {
        var (w, route, _) = Established();
        SetGold(CityOf(w.A), 5); // 4 of its own plus the route's 1
        SetGold(CityOf(w.B), 1);
        TradeRouteTurns.StartTurn(w.A);
        Assert.Equal(120, route.OriginYields[TradeYield.Gold]);
        AddTechs(w.B, 3);
        TradeRouteTurns.StartTurn(w.B);
        Assert.Equal(200, route.OriginYields[TradeYield.Science]);
    }

    [Fact]
    public void AnEnemyMilitaryUnitPlundersTheRouteAndTakesGold()
    {
        var (w, route, _) = Established();
        TradeRouteTurns.StartTurn(w.A);
        w.MakeWar(w.C, w.A);
        var tile = w.Game.GetTile(route.CurrentPosition);
        var raider = w.Game.AddDefaultMeleeUnitWithUniques(w.C, tile);
        int before = w.C.Gold;
        TradeRouteTurns.TryPlunder(raider, tile);
        // 100 + 1 gold a turn x 32 turns left
        Assert.Equal(before + 132, w.C.Gold);
        Assert.Empty(w.Game.GameInfo.TradeRoutes!);
        Assert.True(TradeRouteTurns.WasRecentlyEnded(w.Game.GameInfo, route.OriginCityId, route.DestCityId));
        Assert.Contains(w.A.Notifications, it => it.Text.Contains("plundered"));
        Assert.Contains(w.B.Notifications, it => it.Text.Contains("plundered"));
    }

    [Fact]
    public void PlunderNeedsWarAMilitaryUnitAndAnOpenTile()
    {
        var (w, route, _) = Established();
        TradeRouteTurns.StartTurn(w.A);
        var tile = w.Game.GetTile(route.CurrentPosition);
        var friend = w.Game.AddDefaultMeleeUnitWithUniques(w.C, tile);
        TradeRouteTurns.TryPlunder(friend, tile); // at peace
        Assert.Single(w.Game.GameInfo.TradeRoutes!);
        w.MakeWar(w.C, w.A);
        var trader = Trader(w, w.C, CityOf(w.C));
        trader.PutInTile(tile);
        TradeRouteTurns.TryPlunder(trader, tile); // not military
        Assert.Single(w.Game.GameInfo.TradeRoutes!);
        var home = CityOf(w.A).GetCenterTile();
        route.UnitPathIndex = 0;
        var raider = w.Game.AddDefaultMeleeUnitWithUniques(w.C, w.Game.GetTile(0, 3));
        TradeRouteTurns.TryPlunder(raider, home); // cities are safe
        Assert.Single(w.Game.GameInfo.TradeRoutes!);
    }

    [Fact]
    public void WarBetweenTheOwnersBreaksTheRouteAndLosesTheUnit()
    {
        var (w, route, unitId) = Established();
        w.Ok(new Unciv.Commands.Diplomacy.DeclareWar { CivId = w.B.CivId });
        Assert.Empty(w.Game.GameInfo.TradeRoutes!);
        Assert.True(TradeRouteTurns.WasRecentlyEnded(w.Game.GameInfo, route.OriginCityId, route.DestCityId));
        Assert.DoesNotContain(w.A.Units.GetCivUnits(), it => it.Id == unitId);
        Assert.Contains(w.B.Notifications, it => it.Text.Contains("was broken"));
        Assert.Equal(0, w.A.Units.GetCivUnits().Count(it => it.Name == route.UnitName));
    }

    [Fact]
    public void ACapturedCityBreaksItsRoutesAtTheNextTurnStart()
    {
        var (w, route, _) = Established();
        CityOf(w.B).MoveToCiv(w.C);
        TradeRouteTurns.StartTurn(w.A);
        Assert.Empty(w.Game.GameInfo.TradeRoutes!);
        Assert.True(TradeRouteTurns.WasRecentlyEnded(w.Game.GameInfo, route.OriginCityId, route.DestCityId));
    }

    [Fact]
    public void EndedRoutesAreForgottenAfterThirtyTurns()
    {
        var (w, route, _) = Established();
        w.MakeWar(w.A, w.B);
        TradeRouteTurns.StartTurn(w.A);
        var game = w.Game.GameInfo;
        Assert.Single(game.EndedTradeRoutes!);
        game.Turns += 29;
        Assert.True(TradeRouteTurns.WasRecentlyEnded(game, route.OriginCityId, route.DestCityId));
        TradeRouteTurns.StartTurn(w.A);
        Assert.Single(game.EndedTradeRoutes!);
        game.Turns += 1;
        Assert.False(TradeRouteTurns.WasRecentlyEnded(game, route.OriginCityId, route.DestCityId));
        TradeRouteTurns.StartTurn(w.A);
        Assert.Null(game.EndedTradeRoutes);
    }

    // ---- state, saves and queries

    [Fact]
    public void RoutesSerializeCloneAndGamesWithoutRoutesSaveWithoutTheFields()
    {
        var (w, route, _) = Established();
        var game = w.Game.GameInfo;
        string saved = UncivFiles.GameInfoToString(game, forceZip: false);
        Assert.Contains("tradeRoutes", saved);
        var back = System.Text.Json.JsonSerializer.Deserialize<List<TradeRoute>>(
            System.Text.Json.JsonSerializer.Serialize(game.TradeRoutes, Unciv.Json.UncivJson.DefaultOptions), Unciv.Json.UncivJson.DefaultOptions)!.Single();
        Assert.Equal(route.Id, back.Id);
        Assert.Equal(route.Path, back.Path);
        Assert.Equal(route.OriginYields, back.OriginYields);
        Assert.Equal(route.Domain, back.Domain);

        var clone = game.Clone();
        Assert.NotSame(game.TradeRoutes![0], clone.TradeRoutes![0]);
        Assert.Equal(route.Path, clone.TradeRoutes[0].Path);
        clone.TradeRoutes[0].UnitPathIndex = 5;
        Assert.Equal(0, route.UnitPathIndex);

        var plain = new DiplomacyWorld();
        string plainSave = UncivFiles.GameInfoToString(plain.Game.GameInfo, forceZip: false);
        Assert.DoesNotContain("tradeRoutes", plainSave, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("lastTradeRouteId", plainSave, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("endedTradeRoutes", plainSave, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AGameInfoStateThatIsNotARealGameHasNoRoutesAndIgnoresWrites()
    {
        IGameInfoState state = new FakeGameInfoState();
        state.TradeRoutes = [new TradeRoute { Id = 1 }];
        state.LastTradeRouteId = 5;
        state.EndedTradeRoutes = [new EndedTradeRoute("a", "b", "c", 1, false)];
        Assert.Null(state.TradeRoutes);
        Assert.Equal(0, state.LastTradeRouteId);
        Assert.Null(state.EndedTradeRoutes);
        Assert.Empty(TradeRouteRules.Routes(state));
    }

    [Fact]
    public void TurnStartIsANoOpInAGameWithoutTradeRoutes()
    {
        var plain = new DiplomacyWorld();
        ulong before = plain.Hash();
        TradeRouteTurns.StartTurn(plain.A);
        TradeRouteTurns.BreakInvalid(plain.Game.GameInfo);
        Assert.Equal(before, plain.Hash());
    }

    private static T Query<T>(DiplomacyWorld w, Unciv.Queries.IQuery<T> query, CivilizationObject viewer) where T : notnull
    {
        var registry = new Unciv.Queries.QueryRegistry(Unciv.Json.UncivJson.DefaultOptions);
        registry.RegisterFromAssembly(typeof(Unciv.Queries.QueryRegistry).Assembly);
        var envelope = registry.ToEnvelope(viewer.CivName, query);
        var response = registry.Execute(envelope, new Unciv.Queries.QueryContext(w.Game.GameInfo));
        Assert.True(response.Ok, response.Message);
        return System.Text.Json.JsonSerializer.Deserialize<T>(response.Result!.Value.GetRawText(), Unciv.Json.UncivJson.DefaultOptions)!;
    }

    [Fact]
    public void TheRoutesQueryListsYoursAndTheOnesToYou()
    {
        var (w, route, _) = Established();
        var mine = Query(w, new Unciv.Queries.Empire.TradeRoutesQuery(), w.A);
        Assert.Equal(1, mine.Slots);
        Assert.Equal(1, mine.Used);
        var row = Assert.Single(mine.Yours);
        Assert.Equal(route.Id, row.RouteId);
        Assert.Equal(32, row.TurnsLeft);
        Assert.Equal(100, row.YourYields[TradeYield.Gold]);
        Assert.Empty(mine.ToYou);
        var theirs = Query(w, new Unciv.Queries.Empire.TradeRoutesQuery(), w.B);
        Assert.Empty(theirs.Yours);
        Assert.Equal(0, theirs.Used);
        Assert.Equal(route.Id, Assert.Single(theirs.ToYou).RouteId);
    }

    [Fact]
    public void TheOptionsQueryOffersExactlyWhatTheCommandAccepts()
    {
        var w = NewWorld();
        var unit = Trader(w, w.A, CityOf(w.A));
        var options = Query(w, new Unciv.Queries.Empire.TradeRouteOptionsQuery(unit.Id), w.A);
        Assert.Equal("", options.Reason);
        Assert.Contains(options.Options, it => it.CityId == CityOf(w.B).Id && it.PathLength == 8 && it.YourYields[TradeYield.Gold] == 100);
        Assert.DoesNotContain(options.Options, it => it.CityId == CityOf(w.A).Id);
        Assert.Equal(options.Options.OrderBy(it => it.CityName, StringComparer.Ordinal).ThenBy(it => it.CityId, StringComparer.Ordinal).Select(it => it.CityId),
            options.Options.Select(it => it.CityId));
        foreach (var option in options.Options)
            Assert.Null(TradeRouteRules.CheckUnit(unit, w.Game.GameInfo.GetCities().First(it => it.Id == option.CityId), option.Kind, out _));
        unit.CurrentMovement = 0;
        var stuck = Query(w, new Unciv.Queries.Empire.TradeRouteOptionsQuery(unit.Id), w.A);
        Assert.NotEqual("", stuck.Reason);
        Assert.Empty(stuck.Options);
    }
}
