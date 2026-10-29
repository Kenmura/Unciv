using Unciv.Logic;
using Unciv.Logic.Archaeology;
using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Transients;
using Unciv.Logic.GreatWorks;
using Unciv.Logic.Map;
using Unciv.Logic.Tourism;
using Unciv.Logic.Trade.Routes;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Unique;
using CityObject = Unciv.Logic.City.City;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.Core.Tests.Logic.Bnw;

// B033: every Brave New World feature that an engine ticket marked Done must fire on the REAL generated ruleset
// (android/assets/jsons/Civ V - Brave New World), not only on a test-built one: B027 and B032 were both gaps found only after the engine
// ticket was Done, because the generator had never written the uniques the engine reads. Each test starts a game on the generated
// ruleset, founds cities there and drives the engine's own rules. The expectations are the Technologies/Buildings/... columns of the
// BNW tables (bnw-spec/data), written out by hand.
[Collection("RulesetCache")]
public sealed class BnwGeneratedRulesetTests : IDisposable
{
    public BnwGeneratedRulesetTests() => RulesetCache.Clear();

    public void Dispose() => RulesetCache.Clear();

    private static GameInfo NewBnwGame()
    {
        Models.Ruleset.BnwRulesetTests.LoadBnw();
        return GameStarter.StartNewGame(
            new GameSetupInfo(
                new GameParameters
                {
                    BaseRuleset = BaseRuleset.Civ_V_BNW.FullName(),
                    NumberOfCityStates = 0,
                    Players = [new Player("Brazil", PlayerType.Human), new Player("Poland")],
                },
                new MapParameters { Type = MapType.Pangaea, Shape = MapShape.Rectangular, Seed = 3, MapSize = MapSize.Tiny }),
            5, 1000L);
    }

    /// <summary>The first major civ with a capital founded where its starting Settler stands.</summary>
    private static (GameInfo Game, CivilizationObject Civ, CityObject Capital) NewCapital()
    {
        var game = NewBnwGame();
        var civ = game.Civilizations.First(it => it.IsMajorCiv());
        var settler = civ.Units.GetCivUnits().First(it => it.HasUnique(UniqueType.FoundCity));
        var capital = civ.AddCity(settler.GetTile().Position, settler);
        return (game, civ, capital);
    }

    private static Ruleset Real() => Models.Ruleset.BnwRulesetTests.LoadBnw();

    private static List<string> UniqueTexts(Ruleset ruleset, string tech) =>
        ruleset.Technologies[tech].Uniques.ToList();

    // ---- technologies: the sweep of Technologies.tsv

    [Fact]
    public void EveryNonEmptyTechnologyColumnReachesTheRulesetOrIsDocumentedAsSkipped()
    {
        var r = Real();
        // Columns the generator writes (B033, B015, B010, B009) and columns the G&K Techs.json already states, one row each.
        var expected = new (string Tech, string Unique)[]
        {
            ("Animal Husbandry", "[+1] trade route slots"), ("Sailing", "[+1] trade route slots"), ("Engineering", "[+1] trade route slots"),
            ("Compass", "[+1] trade route slots"), ("Banking", "[+1] trade route slots"), ("Biology", "[+1] trade route slots"),
            ("Railroad", "[+1] trade route slots"), ("Penicillin", "[+1] trade route slots"),
            ("Compass", "[+10] sea trade route range"), ("Refrigeration", "[+10] sea trade route range"),
            ("Combustion", "[+10] land trade route range"),
            ("Globalization", "[+1] votes per Diplomat in the World Congress"), ("Printing Press", "Allows founding the World Congress"),
            ("The Internet", "[+100]% influence spread from Tourism"), ("Archaeology", "Triggers the creation of archaeological sites"),
            ("Astronomy", "[+1] Movement <for [Embarked] units>"), ("Steam Power", "[+1] Movement <for [Embarked] units>"),
            ("Optics", "Enables embarkation for land units"), ("Astronomy", "Enables [Embarked] units to enter ocean tiles"),
            ("Future Tech", "Can be continually researched"), ("Satellites", "Reveals the entire map"),
            ("Writing", "Enables establishment of embassies <Civilopedia link [Tutorials/Embassies]>"),
            ("Civil Service", "Enables Open Borders agreements <Civilopedia link [Tutorials/Open Borders]>"),
            ("Chivalry", "Enables Defensive Pacts <Civilopedia link [Tutorials/Defensive Pacts]>"),
            ("Education", "Enables Research agreements <Civilopedia link [Tutorials/Research Agreements]>"),
            ("Engineering", "Roads connect tiles across rivers"),
        };
        foreach (var (tech, unique) in expected) Assert.Contains(unique, UniqueTexts(r, tech));
        // Exactly the eight InternationalTradeRoutesChange techs carry a slot: no tech gains one by accident.
        Assert.Equal(
            ["Animal Husbandry", "Banking", "Biology", "Compass", "Engineering", "Penicillin", "Railroad", "Sailing"],
            r.Technologies.Values.Where(it => it.Uniques.Contains("[+1] trade route slots")).Select(it => it.Name).Order(StringComparer.Ordinal));
    }

    // ---- trade routes (B005/B020)

    [Fact]
    public void TradeRouteSlotsComeFromTheEightTechsAndFromColossusAndPetra()
    {
        var (_, civ, capital) = NewCapital();
        Assert.Equal(0, TradeRouteRules.Slots(civ));
        string[] techs = ["Animal Husbandry", "Sailing", "Engineering", "Compass", "Banking", "Biology", "Railroad", "Penicillin"];
        for (int i = 0; i < techs.Length; i++)
        {
            civ.Tech.AddTechnology(techs[i]);
            Assert.Equal(i + 1, TradeRouteRules.Slots(civ));
        }
        capital.CityConstructions.AddBuilding("Colossus");
        Assert.Equal(9, TradeRouteRules.Slots(civ));
        capital.CityConstructions.AddBuilding("Petra");
        Assert.Equal(10, TradeRouteRules.Slots(civ));
    }

    [Fact]
    public void TradeRangeComesFromTechsCaravansaryAndHarbor()
    {
        var (_, civ, capital) = NewCapital();
        Assert.Equal((10, 20), (TradeRouteRules.Range(civ, capital, TradeDomain.Land), TradeRouteRules.Range(civ, capital, TradeDomain.Sea)));
        civ.Tech.AddTechnology("Compass");
        Assert.Equal(30, TradeRouteRules.Range(civ, capital, TradeDomain.Sea));
        civ.Tech.AddTechnology("Combustion");
        Assert.Equal(20, TradeRouteRules.Range(civ, capital, TradeDomain.Land));
        civ.Tech.AddTechnology("Refrigeration");
        Assert.Equal(40, TradeRouteRules.Range(civ, capital, TradeDomain.Sea));

        capital.CityConstructions.AddBuilding("Caravansary");
        Assert.Equal(30, TradeRouteRules.Range(civ, capital, TradeDomain.Land)); // 20 x 150%
        Assert.Equal(40, TradeRouteRules.Range(civ, capital, TradeDomain.Sea));
        capital.CityConstructions.AddBuilding("Harbor");
        Assert.Equal(60, TradeRouteRules.Range(civ, capital, TradeDomain.Sea)); // 40 x 150%
    }

    [Fact]
    public void TradeBuildingsAddTheirGoldToTheCityAndToTheRouteTarget()
    {
        var (_, _, capital) = NewCapital();
        int Sum(UniqueType type) => TradeRouteRules.SumForCity(TradeRouteRules.BuildingUniques(capital, type), capital);
        int Whole(UniqueType type) => TradeRouteRules.BuildingUniques(capital, type).Sum(it => it.Params[0].ToInt());

        capital.CityConstructions.AddBuilding("Caravansary");
        capital.CityConstructions.AddBuilding("Harbor");
        Assert.Equal(200, Sum(UniqueType.GoldFromLandTradeRoutes)); // hundredths of Gold
        Assert.Equal(100, Sum(UniqueType.GoldFromSeaTradeRoutes));

        foreach (string name in new[] { "Market", "Bazaar", "Bank", "Colossus", "East India Company" })
            capital.CityConstructions.AddBuilding(name);
        Assert.Equal(1 + 1 + 1 + 2 + 4, Whole(UniqueType.TradeRouteOwnerGold));
        Assert.Equal(1 + 1 + 1 + 1 + 2, Whole(UniqueType.TradeRouteTargetGold));
    }

    [Fact]
    public void GranaryWorkshopAndLonghouseOpenTheDomesticRoutes()
    {
        var (_, _, capital) = NewCapital();
        bool Allows(string stat) => TradeRouteRules.BuildingUniques(capital, UniqueType.AllowsDomesticTradeRoutes).Any(it => it.Params[0] == stat);
        Assert.False(Allows("Food") || Allows("Production"));
        capital.CityConstructions.AddBuilding("Granary");
        Assert.True(Allows("Food"));
        Assert.False(Allows("Production"));
        capital.CityConstructions.AddBuilding("Workshop");
        Assert.True(Allows("Production"));
        Assert.Contains("Allows domestic [Production] trade routes from this city", Real().Buildings["Longhouse"].Uniques);
    }

    [Fact]
    public void TheBnwRulesetCarriesTheTradeAndTourismConstantsTheEngineReads()
    {
        var c = Real().ModOptions.Constants;
        Assert.Equal((100, 2, 2), (c.InternationalTradeBase, c.BaseTourismPerGreatWork, c.BaseCulturePerGreatWork));
        Assert.True(c.TourismModifierOpenBorders > 0 && c.TourismModifierTradeRoute > 0 && c.TourismModifierSharedReligion > 0);
    }

    // ---- Great Works (B007, B027)

    [Fact]
    public void BuildingAParthenonCreatesItsFriezeInsideIt()
    {
        var (game, civ, capital) = NewCapital();
        Assert.Equal(0, capital.GetNumberOfGreatWorks());
        capital.CityConstructions.AddBuilding("Parthenon");
        Assert.Equal(1, capital.GetNumberOfGreatWorks());
        Assert.Equal("Parthenon Frieze", capital.GetGreatWorkSlots().Single(it => it.Work != null).Work);
        Assert.True(game.IsGreatWorkUsed("Parthenon Frieze"));
        Assert.Equal(2f, capital.GetGreatWorkStats().Culture); // baseCulturePerGreatWork
        Assert.Equal(civ.CivId, game.GetGreatWork("Parthenon Frieze")!.Creator);
    }

    [Fact]
    public void AGreatWriterIsSpentOnTheWritingSlotOfAnAmphitheater()
    {
        var (game, civ, capital) = NewCapital();
        capital.CityConstructions.AddBuilding("Amphitheater");
        var writer = civ.Units.AddUnit("Great Writer", capital)!;
        Assert.True(GreatWorkRules.CanCreate(civ, writer, capital.GetCenterTile()));
        var (instance, location) = GreatWorkRules.Create(civ, writer);
        Assert.Equal("Amphitheater", location.Building.Name);
        Assert.Equal(capital.Id, location.City.Id);
        Assert.Equal(instance.Name, capital.GetGreatWorkSlots().Single(it => it.Work != null).Work);
        Assert.Equal(1, capital.GetNumberOfGreatWorks());
    }

    [Fact]
    public void EveryGreatWorkBuildingOfTheRulesetHasASlotTypeAndRealGreatWorksExist()
    {
        var r = Real();
        var slotted = r.Buildings.Values.Where(it => it.GreatWorkSlots > 0).ToList();
        Assert.Equal(20, slotted.Count);
        Assert.All(slotted, b => Assert.NotNull(b.GreatWorkSlotType));
        foreach (var type in Enum.GetValues<GreatWorkSlotType>())
            Assert.Contains(slotted, b => b.GreatWorkSlotType == type);
        Assert.NotEmpty(r.GreatWorks);
        Assert.All(r.GreatWorks.Values, w => Assert.Contains(w.GreatWorkClass.SlotType(), slotted.Select(it => it.GreatWorkSlotType)));
        // Theming rows exist on the buildings that theme (the museum: twelve rows)
        Assert.Equal(12, r.Buildings["Museum"].ThemingBonuses.Count);
    }

    // ---- tourism (B008)

    [Fact]
    public void TheEiffelTowerAndMughalFortGiveTourismOnlyAfterTheirTech()
    {
        var (_, civ, capital) = NewCapital();
        capital.CityConstructions.AddBuilding("Eiffel Tower");
        capital.CityConstructions.AddBuilding("Mughal Fort");
        Assert.Equal(0, TourismRules.CityTourismBeforeModifiers(capital));
        civ.Tech.AddTechnology("Flight");
        Assert.Equal(2, TourismRules.CityTourismBeforeModifiers(capital));
        civ.Tech.AddTechnology("Radio");
        Assert.Equal(14, TourismRules.CityTourismBeforeModifiers(capital));
    }

    [Fact]
    public void GreatWorksProduceTourismAndHotelsAirportsAndVisitorCentersMultiplyIt()
    {
        var (_, _, capital) = NewCapital();
        capital.CityConstructions.AddBuilding("Parthenon"); // one work: baseTourismPerGreatWork = 2
        Assert.Equal(2, TourismRules.CityTourismBeforeModifiers(capital));
        int GreatWorkPercent() => capital.GetMatchingUniques(UniqueType.TourismFromGreatWorks).Sum(it => it.Params[0].ToInt());
        int LandmarkPercent() => capital.GetMatchingUniques(UniqueType.LandmarksCultureAsTourism).Sum(it => it.Params[0].ToInt());
        Assert.Equal((0, 0), (GreatWorkPercent(), LandmarkPercent()));
        int last = TourismRules.CityTourismBeforeModifiers(capital);
        foreach (var (name, percent) in new[] { ("Hotel", 50), ("Airport", 100), ("National Visitor Center", 200) })
        {
            capital.CityConstructions.AddBuilding(name);
            Assert.Equal((percent, percent), (GreatWorkPercent(), LandmarkPercent()));
            int now = TourismRules.CityTourismBeforeModifiers(capital);
            Assert.True(now > last, name);
            last = now;
        }
    }

    [Fact]
    public void TheCulturalExchangePolicyCarriesTheThreeTourismModifiers()
    {
        var game = NewBnwGame();
        var uniques = game.Ruleset.Policies["Cultural Exchange"].Uniques.Select(it => new UniqueObject(it).Type).ToList();
        Assert.Equal([UniqueType.TourismOpenBorders, UniqueType.TourismTradeRoute, UniqueType.TourismSharedReligion], uniques);
    }

    // ---- archaeology (B010, B032)

    [Fact]
    public void ArchaeologyHasItsUnitImprovementsAndSiteResourcesInTheRuleset()
    {
        var r = Real();
        Assert.True(ArchaeologyRules.IsEnabled(r));
        Assert.NotNull(ArchaeologyRules.DigImprovement(r));
        Assert.NotNull(ArchaeologyRules.LandmarkImprovement(r));
        Assert.Contains(ArchaeologyRules.AntiquitySite, r.TileResources.Keys);
        Assert.Contains(ArchaeologyRules.HiddenAntiquitySite, r.TileResources.Keys);
        Assert.Contains("Archaeologist", r.Units.Keys);
        Assert.Equal("Archaeology", r.Units["Archaeologist"].RequiredTech);
    }

    [Fact]
    public void AnArchaeologistBuildsTheDigOnAnAntiquitySiteOfTheGeneratedMap()
    {
        var (game, civ, capital) = NewCapital();
        civ.Tech.AddTechnology("Archaeology"); // spawns the sites on the generated map
        var site = game.TileMap.TileList.First(it => it.Resource == ArchaeologyRules.AntiquitySite && it.IsLand && !it.IsImpassible());
        var dig = ArchaeologyRules.DigImprovement(game.Ruleset)!;
        Assert.True(site.ImprovementFunctions.CanBuildImprovement(dig, civ.State));
        Assert.True(capital.GetCenterTile().ImprovementFunctions.CanBuildImprovement(dig, civ.State) is false);
    }

    // ---- mixed: the units of the trade and culture features

    [Fact]
    public void TheCaravanTheCargoShipAndTheGreatPeopleCarryTheirBnwUniques()
    {
        var r = Real();
        Assert.Contains("Can establish trade routes", r.Units["Caravan"].Uniques);
        Assert.Contains("Can establish trade routes", r.Units["Cargo Ship"].Uniques);
        Assert.Contains(r.Units["Great Musician"].Uniques, it => it.StartsWith("Gives [10] Tourism", StringComparison.Ordinal));
        Assert.Contains("Great Work counts as [8] turns of culture", r.Units["Great Writer"].Uniques);
    }

    [Fact]
    public void TheLeagueAndWonderHooksOfTheBuildingColumnsAreStated()
    {
        var r = Real();
        Assert.Contains("[+2] Delegates in the World Congress", r.Buildings["Forbidden Palace"].Uniques);
        Assert.Contains("Other civs' cultural influence is nullified here", r.Buildings["Great Firewall"].Uniques);
        Assert.Contains("Building [3] of these unlocks Ideologies", r.Buildings["Factory"].Uniques);
        Assert.Contains("[+33]% Science from Great Scientists' bulbs", r.Buildings["International Space Station"].Uniques);
        Assert.Contains("Unbuildable", r.Buildings["International Space Station"].Uniques);
        Assert.Contains("Doubles the number of military units", r.Buildings["Terracotta Army"].Uniques);
        Assert.Contains("[+5]% Production for each trade route to a city-state", r.Buildings["Hanse"].Uniques);
    }
}
