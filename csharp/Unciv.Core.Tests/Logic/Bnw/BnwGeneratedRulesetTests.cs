using Unciv.Logic;
using Unciv.Logic.Archaeology;
using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Logic.Civilization.Transients;
using Unciv.Logic.GreatWorks;
using Unciv.Logic.Map;
using Unciv.Logic.Tourism;
using Unciv.Logic.Trade.Routes;
using Unciv.Models;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using Unciv.Utils;
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

    private static GameInfo NewBnwGame(string first = "Brazil")
    {
        Models.Ruleset.BnwRulesetTests.LoadBnw();
        return GameStarter.StartNewGame(
            new GameSetupInfo(
                new GameParameters
                {
                    BaseRuleset = BaseRuleset.Civ_V_BNW.FullName(),
                    NumberOfCityStates = 0,
                    Players = [new Player(first, PlayerType.Human), new Player("Poland")],
                },
                new MapParameters { Type = MapType.Pangaea, Shape = MapShape.Rectangular, Seed = 3, MapSize = MapSize.Tiny }),
            5, 1000L);
    }

    /// <summary>The first major civ with a capital founded where its starting Settler stands.</summary>
    private static (GameInfo Game, CivilizationObject Civ, CityObject Capital) NewCapital(string first = "Brazil")
    {
        var game = NewBnwGame(first);
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

    // B029: Eras.tsv TradeRouteFood/ProductionBonusTimes100 (hundredths) on the Unciv era names (POSTMODERN = Atomic; FUTURE spans Information and Future).
    [Fact]
    public void TheEraTradeBonusesComeFromTheEraTable()
    {
        var eras = Real().Eras;
        int[] expected = [0, 50, 100, 150, 200, 250, 300, 350, 350];
        string[] names = ["Ancient era", "Classical era", "Medieval era", "Renaissance era", "Industrial era", "Modern era", "Atomic era", "Information era", "Future era"];
        Assert.Equal(expected, names.Select(it => eras[it].TradeRouteFoodBonusTimes100));
        Assert.Equal(expected, names.Select(it => eras[it].TradeRouteProductionBonusTimes100));
    }

    // B029: CityStateTradeChange is stored in hundredths in Policies.tsv (200); the engine reads whole Gold.
    [Fact]
    public void MerchantConfederacyStatesWholeGoldPerCityStateRoute()
    {
        Assert.Contains("[+2] Gold from trade routes with city-states", Real().Policies["Merchant Confederacy"].Uniques);
        Assert.Contains("[+5]% Production for each trade route to a city-state", Real().Buildings["Hanse"].Uniques);
    }

    // B035: Hanse's '[+5]% Production for each trade route to a city-state' is read by CityStats (route from this city to a city-state's city).
    [Fact]
    public void HanseAddsItsProductionPercentPerRouteToACityState()
    {
        Models.Ruleset.BnwRulesetTests.LoadBnw();
        var game = GameStarter.StartNewGame(
            new GameSetupInfo(
                new GameParameters
                {
                    BaseRuleset = BaseRuleset.Civ_V_BNW.FullName(),
                    NumberOfCityStates = 1,
                    Players = [new Player("Brazil", PlayerType.Human), new Player("Poland")],
                },
                new MapParameters { Type = MapType.Pangaea, Shape = MapShape.Rectangular, Seed = 3, MapSize = MapSize.Small }),
            5, 1000L);
        var civ = game.Civilizations.First(it => it.IsMajorCiv());
        var cityState = game.Civilizations.First(it => it.IsCityState);
        var capital = FoundCity(civ);
        var csTile = game.TileMap.TileList.First(it => it.IsLand && !it.IsImpassible() && it.GetOwner() == null && it.GetCity() == null);
        var cityStateCity = cityState.AddCity(csTile.Position, null);
        capital.CityConstructions.AddBuilding("Hanse");
        float Bonus()
        {
            capital.CityStats.Update();
            return capital.CityStats.StatPercentBonusTree.TotalStats.Production;
        }
        void Route(string destCityId, string destOwner, string originCityId) => (game.TradeRoutes ??= []).Add(new TradeRoute
        {
            Id = game.TradeRoutes.Count + 1,
            OriginCityId = originCityId,
            DestCityId = destCityId,
            OriginOwner = civ.CivId,
            DestOwner = destOwner,
            Kind = TradeRouteKind.International,
        });

        float baseline = Bonus();
        Route(capital.Id, civ.CivId, capital.Id); // a route to a major civ (here itself) does not count
        Assert.Equal(baseline, Bonus(), 3);
        Route(cityStateCity.Id, cityState.CivId, cityStateCity.Id); // starts in the city-state, not in Hanse's city
        Assert.Equal(baseline, Bonus(), 3);
        Route(cityStateCity.Id, cityState.CivId, capital.Id);
        Assert.Equal(baseline + 5, Bonus(), 3);
        Route(cityStateCity.Id, cityState.CivId, capital.Id);
        Assert.Equal(baseline + 10, Bonus(), 3);
    }

    // B029/B025: the BNW score multipliers for population and wonders (SCORE_POPULATION_MULTIPLIER 4, SCORE_WONDER_MULTIPLIER 25); G&K keeps 3 and 40.
    [Fact]
    public void TheBnwRulesetCarriesTheBnwScoreMultipliers()
    {
        var c = Real().ModOptions.Constants;
        Assert.Equal((4, 25), (c.ScoreFromPopulation, c.ScoreFromWonders));
        var gk = new ModConstants();
        Assert.Equal((3, 40), (gk.ScoreFromPopulation, gk.ScoreFromWonders));
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

    private static void PlaceWork(GameInfo game, CityObject city, string building, int slot, string work, string era, string creator = "")
    {
        (game.GreatWorkInstances ??= []).Add(new GreatWorkInstance { Name = work, Creator = creator, Era = era });
        city.SetGreatWorkIn(building, slot, work);
    }

    // B028: Enhanced Culture (Napoleon) doubles the theming bonus of the capital only.
    [Fact]
    public void FrancesCapitalThemingBonusIsDoubledAndOtherCivsAreNot()
    {
        int Bonus(string civName)
        {
            var (game, _, capital) = NewCapital(civName);
            capital.CityConstructions.AddBuilding("The Great Library");
            PlaceWork(game, capital, "The Great Library", 0, "Odyssey", "Ancient era", "x");
            PlaceWork(game, capital, "The Great Library", 1, "Fables", "Classical era", "y");
            return capital.GetThemingBonuses();
        }
        Assert.Equal(2, Bonus("Brazil"));
        Assert.Equal(4, Bonus("France"));
    }

    // B028: Aesthetics Complete adds +100% to the theming bonus of every city (non-capital France is 2, the capital 4 -> 8).
    [Fact]
    public void AestheticsCompleteAndEnhancedCultureStack()
    {
        var (game, civ, capital) = NewCapital("France");
        capital.CityConstructions.AddBuilding("The Great Library");
        PlaceWork(game, capital, "The Great Library", 0, "Odyssey", "Ancient era", "x");
        PlaceWork(game, capital, "The Great Library", 1, "Fables", "Classical era", "y");
        civ.Policies.Adopt(game.Ruleset.Policies["Aesthetics Complete"], true);
        Assert.Equal(8, capital.GetThemingBonuses());
    }

    // B028: Building_DomainFreeExperiencePerGreatWork (Royal Library: 10 XP per work for land, water and air units built in the city).
    [Fact]
    public void UnitsBuiltInACityWithTheRoyalLibraryStartWithExperiencePerGreatWork()
    {
        var (game, civ, capital) = NewCapital();
        capital.CityConstructions.AddBuilding("Royal Library");
        Assert.Contains("[+10] XP for [Land] units per Great Work in this city", game.Ruleset.Buildings["Royal Library"].Uniques);
        int Xp(string unitName)
        {
            var unit = civ.Units.AddUnit(unitName, capital)!;
            game.Ruleset.Units[unitName].AddConstructionBonuses(unit, capital.CityConstructions);
            return unit.Promotions.XP;
        }
        Assert.Equal(0, Xp("Warrior"));
        PlaceWork(game, capital, "Royal Library", 0, "Odyssey", "Ancient era");
        Assert.Equal(10, Xp("Warrior"));
        capital.CityConstructions.AddBuilding("Amphitheater");
        PlaceWork(game, capital, "Amphitheater", 0, "Oedipus the King", "Ancient era"); // a second work in another building counts too
        Assert.Equal(20, Xp("Warrior"));
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

    // ---- ideology tenets (B030)

    /// <summary>A game of <paramref name="majors"/> major civs on the generated ruleset; every pair has met.</summary>
    private static GameInfo NewGameOf(int majors)
    {
        Models.Ruleset.BnwRulesetTests.LoadBnw();
        var players = new[] { "Brazil", "Poland", "Greece" }.Take(majors).Select(it => new Player(it, it == "Brazil" ? PlayerType.Human : PlayerType.AI)).ToList();
        var game = GameStarter.StartNewGame(
            new GameSetupInfo(
                new GameParameters { BaseRuleset = BaseRuleset.Civ_V_BNW.FullName(), NumberOfCityStates = 0, Players = players },
                new MapParameters { Type = MapType.Pangaea, Shape = MapShape.Rectangular, Seed = 3, MapSize = MapSize.Small }),
            5, 1000L);
        var civs = game.Civilizations.Where(it => it.IsMajorCiv()).ToList();
        foreach (var a in civs)
            foreach (var b in civs.Where(it => it != a))
                if (a.GetDiplomacyManager(b) == null) a.DiplomacyFunctions.MakeCivilizationsMeet(b);
        return game;
    }

    private static (CivilizationObject A, CivilizationObject B, CivilizationObject C) Majors(GameInfo game)
    {
        var majors = game.Civilizations.Where(it => it.IsMajorCiv()).ToList();
        return (majors[0], majors[1], majors.Count > 2 ? majors[2] : majors[1]);
    }

    private static CityObject FoundCity(CivilizationObject civ)
    {
        var settler = civ.Units.GetCivUnits().First(it => it.HasUnique(UniqueType.FoundCity));
        return civ.AddCity(settler.GetTile().Position, settler);
    }

    /// <summary>Makes <paramref name="civ"/> follow <paramref name="ideology"/> and adopts the tenets (the level rule is B013's and tested there).</summary>
    private static void Follow(CivilizationObject civ, string ideology, params string[] tenets)
    {
        civ.Tech.Era = civ.GameInfo.Ruleset.Eras["Modern era"];
        civ.Policies.ChooseIdeology(civ.GameInfo.Ruleset.PolicyBranches[ideology]);
        civ.Policies.FreeTenets = 100;
        foreach (var tenet in tenets) civ.Policies.Adopt(civ.GameInfo.Ruleset.Policies[tenet]);
    }

    [Fact]
    public void EveryTenetStatesItsEffectsExceptTheThreeWithoutAUniqueYet()
    {
        var r = Real();
        var tenets = r.PolicyBranches.Values.Where(it => it.PurchaseByLevel).SelectMany(it => it.Policies).ToList();
        Assert.Equal(48, tenets.Count);
        string[] pending = ["Clausewitz's Legacy", "Resettlement", "United Front"];
        Assert.Equal(pending, tenets.Where(it => it.Uniques.Count == 0).Select(it => it.Name).Order(StringComparer.Ordinal));
        // Universal Healthcare: one happiness unique per national wonder, in each of the three ideologies
        foreach (string ideology in new[] { "Freedom", "Order", "Autocracy" })
            Assert.Equal(12, r.Policies[$"Universal Healthcare ({ideology})"].Uniques.Count(it => it.Contains("Happiness] from every", StringComparison.Ordinal)));
        Assert.All(tenets.SelectMany(it => it.Uniques), text => Assert.NotNull(new UniqueObject(text).Type));
    }

    // B031: the policy members whose stat columns differ from the Gods & Kings wording carry the BNW columns (Policies.tsv, Policy_CapitalYieldPerPopChanges,
    // Policy_BuildingClassYieldChanges), and the retuned effects fire in the engine.
    [Fact]
    public void RetunedPolicyMembersStateTheirBnwColumns()
    {
        var policies = Real().Policies;
        Assert.Equal(["[+1 Gold] per [2] population [in capital]", "[-50]% Unhappiness from [Population] [in capital]"], policies["Monarchy"].Uniques);
        Assert.Contains("[+1 Happiness] for each international trade route [in all cities]", policies["Meritocracy"].Uniques);
        Assert.DoesNotContain(policies["Meritocracy"].Uniques, it => it.Contains("connected to capital", StringComparison.Ordinal));
        Assert.Contains("[Great General] is earned [50]% faster", policies["Warrior Code"].Uniques);
        string[] sovereignty = ["Library", "University", "Observatory", "Research Lab", "Public School"];
        Assert.Equal(sovereignty.Select(it => $"[+1 Gold] from every [{it}]"), policies["Sovereignty"].Uniques);
        Assert.All(new[] { "Monarchy", "Meritocracy", "Warrior Code", "Sovereignty" }.SelectMany(it => policies[it].Uniques), text => Assert.NotNull(new UniqueObject(text).Type));
    }

    [Fact]
    public void MonarchyPaysGoldPerTwoCitizensAndHalvesTheCapitalsPopulationUnhappiness()
    {
        var (game, civ, capital) = NewCapital();
        capital.Population.SetPopulation(6);
        capital.CityStats.Update();
        float gold = capital.CityStats.CurrentCityStats.Gold;
        float population = capital.CityStats.HappinessList["Population"];
        Assert.True(population < 0);
        civ.Policies.Adopt(game.Ruleset.Policies["Monarchy"], true);
        capital.CityStats.Update();
        Assert.Equal(gold + 3, capital.CityStats.CurrentCityStats.Gold, 3);
        Assert.Equal(population / 2, capital.CityStats.HappinessList["Population"], 3);
    }

    [Fact]
    public void SovereigntyPaysOneGoldFromEachScienceBuilding()
    {
        var (game, civ, capital) = NewCapital();
        capital.CityConstructions.AddBuilding("Library");
        capital.CityConstructions.AddBuilding("University");
        capital.CityStats.Update();
        float gold = capital.CityStats.CurrentCityStats.Gold;
        civ.Policies.Adopt(game.Ruleset.Policies["Sovereignty"], true);
        capital.CityStats.Update();
        Assert.Equal(gold + 2, capital.CityStats.CurrentCityStats.Gold, 3);
    }

    [Fact]
    public void WarriorCodeSpeedsUpGreatGeneralPointsAndMeritocracyPaysHappinessPerInternationalRoute()
    {
        var (game, civ, capital) = NewCapital();
        civ.Policies.Adopt(game.Ruleset.Policies["Warrior Code"], true);
        Assert.Contains(civ.GetMatchingUniques(UniqueType.GreatPersonEarnedFaster), it => it.Params[0] == "Great General" && it.Params[1] == "50");

        capital.CityStats.Update();
        float happiness = capital.CityStats.CurrentCityStats.Happiness;
        civ.Policies.Adopt(game.Ruleset.Policies["Meritocracy"], true);
        capital.CityStats.Update();
        Assert.Equal(happiness, capital.CityStats.CurrentCityStats.Happiness, 3); // no route yet
        (game.TradeRoutes ??= []).Add(new TradeRoute
        {
            Id = 1,
            OriginCityId = capital.Id,
            DestCityId = capital.Id,
            OriginOwner = civ.CivId,
            DestOwner = civ.CivId,
            Kind = TradeRouteKind.International,
        });
        capital.CityStats.Update();
        Assert.Equal(happiness + 1, capital.CityStats.CurrentCityStats.Happiness, 3);
    }

    [Fact]
    public void CultOfPersonalityAddsTourismOncePerCommonFoe()
    {
        var game = NewGameOf(3);
        var (a, b, c) = Majors(game);
        FoundCity(a);
        c.GetDiplomacyManager(a)!.DiplomaticStatus = DiplomaticStatus.War;
        c.GetDiplomacyManager(b)!.DiplomaticStatus = DiplomaticStatus.War;
        a.GetDiplomacyManager(c)!.DiplomaticStatus = DiplomaticStatus.War;
        b.GetDiplomacyManager(c)!.DiplomaticStatus = DiplomaticStatus.War;
        int before = TourismRules.ModifierWith(a, b);
        Follow(a, "Autocracy", "Cult of Personality");
        Assert.Equal(before + 50, TourismRules.ModifierWith(a, b));
        Assert.Equal(before, TourismRules.ModifierWith(b, a));
    }

    [Fact]
    public void CulturalRevolutionAddsTourismTowardCivsFollowingTheSameIdeologyOnly()
    {
        var game = NewGameOf(2);
        var (a, b, _) = Majors(game);
        Follow(a, "Order", "Cultural Revolution");
        Assert.Equal(0, TourismRules.ModifierWith(a, b)); // b has no ideology yet
        Follow(b, "Freedom");
        Assert.Equal(TourismRules.ModifierWith(a, b), game.Ruleset.ModOptions.Constants.TourismModifierDifferentIdeologies); // not shared: no +34
        b.Policies.Ideology = "Order";
        Assert.Equal(34, TourismRules.ModifierWith(a, b));
    }

    [Fact]
    public void DictatorshipOfTheProletariatAddsTourismTowardLessHappyCivs()
    {
        var game = NewGameOf(2);
        var (a, b, _) = Majors(game);
        FoundCity(a);
        FoundCity(b);
        Follow(a, "Order");
        // b founds more cities, which costs it happiness (unhappiness per city), until a is the happier civ
        for (int i = 0; i < 4 && a.GetHappiness() <= b.GetHappiness(); i++)
        {
            var tile = game.TileMap.TileList.First(it => it.IsLand && !it.IsImpassible() && it.GetOwner() == null && it.GetCity() == null);
            b.AddCity(tile.Position, null);
            b.Cache.UpdateCivResources();
        }
        Assert.True(a.GetHappiness() > b.GetHappiness(), $"{a.GetHappiness()} vs {b.GetHappiness()}");
        int before = TourismRules.ModifierWith(a, b);
        a.Policies.Adopt(game.Ruleset.Policies["Dictatorship of the Proletariat"]);
        Assert.Equal(before + 34, TourismRules.ModifierWith(a, b));
    }

    [Fact]
    public void MediaCultureAddsTourismToCitiesWithABroadcastTower()
    {
        var (game, civ, capital) = NewCapital();
        Follow(civ, "Freedom");
        Assert.Equal(0, TourismRules.CityTourismModifier(capital));
        civ.Policies.Adopt(game.Ruleset.Policies["Media Culture"]);
        Assert.Equal(0, TourismRules.CityTourismModifier(capital)); // no Broadcast Tower yet
        capital.CityConstructions.AddBuilding("Broadcast Tower");
        Assert.Equal(34, TourismRules.CityTourismModifier(capital));
    }

    [Fact]
    public void FuturismGivesTourismWhenAWriterArtistOrMusicianIsBorn()
    {
        var (game, civ, _) = NewCapital();
        Follow(civ, "Autocracy");
        Assert.Equal(0, InfluenceRules.ApplyBirthTourism(civ, "Great Writer"));
        civ.Policies.Adopt(game.Ruleset.Policies["Futurism"]);
        foreach (string person in new[] { "Great Writer", "Great Artist", "Great Musician" })
            Assert.Equal(250, InfluenceRules.ApplyBirthTourism(civ, person));
        Assert.Equal(0, InfluenceRules.ApplyBirthTourism(civ, "Great Scientist"));
    }

    [Fact]
    public void CreativeExpressionAddsCultureForEveryGreatWork()
    {
        var (game, civ, capital) = NewCapital();
        capital.CityConstructions.AddBuilding("Parthenon");
        Assert.Equal(2f, capital.GetGreatWorkStats().Culture);
        Follow(civ, "Freedom");
        civ.Policies.Adopt(game.Ruleset.Policies["Creative Expression"]);
        Assert.Equal(3f, capital.GetGreatWorkStats().Culture);
    }

    [Fact]
    public void EconomicUnionAddsGoldToRoutesWithCivsOfTheSameIdeology()
    {
        var game = NewGameOf(2);
        var (a, b, _) = Majors(game);
        var (from, to) = (FoundCity(a), FoundCity(b));
        int Gold() => TradeRouteYields.Compute(from, to, TradeDomain.Land, TradeRouteKind.International).Origin[TradeYield.Gold];
        int multiplier = 100 + (from.GetCenterTile().IsAdjacentToRiver() ? 25 : 0);
        int before = Gold();
        Follow(a, "Freedom", "Economic Union");
        Assert.Equal(before, Gold()); // b follows nothing
        Follow(b, "Order");
        Assert.Equal(before, Gold()); // another ideology
        b.Policies.Ideology = "Freedom";
        Assert.Equal(before + 300 * multiplier / 100, Gold()); // +3 Gold
    }

    [Fact]
    public void IronCurtainRaisesTheYieldOfDomesticRoutes()
    {
        var (game, civ, capital) = NewCapital();
        var second = civ.AddCity(game.TileMap.TileList.First(it => it.IsLand && !it.IsImpassible() && it.GetOwner() == null && it.GetCity() == null).Position, null);
        int Dest(TradeRouteKind kind, int stat) => TradeRouteYields.Compute(capital, second, TradeDomain.Land, kind).Dest[stat];
        civ.Tech.Era = game.Ruleset.Eras["Modern era"]; // Follow() moves the civ there; B029: the era's trade bonus is part of the base yield
        int food = Dest(TradeRouteKind.Food, TradeYield.Food);
        int production = Dest(TradeRouteKind.Production, TradeYield.Production);
        Assert.True(food > 0 && production > 0);
        Follow(civ, "Order", "Iron Curtain");
        Assert.Equal(food * 150 / 100, Dest(TradeRouteKind.Food, TradeYield.Food));
        Assert.Equal(production * 150 / 100, Dest(TradeRouteKind.Production, TradeYield.Production));
    }

    [Fact]
    public void StatTenetsReachTheCityStats()
    {
        var (game, civ, capital) = NewCapital();
        Follow(civ, "Order");
        capital.CityStats.Update(updateCivStats: true);
        var before = capital.CityStats.CurrentCityStats.Clone();
        civ.Policies.Adopt(game.Ruleset.Policies["Five-Year Plan"]);
        capital.CityStats.Update(updateCivStats: true);
        Assert.Equal(before.Production + 2f, capital.CityStats.CurrentCityStats.Production);
        before = capital.CityStats.CurrentCityStats.Clone();
        civ.Policies.Adopt(game.Ruleset.Policies["Party Leadership"]);
        capital.CityStats.Update(updateCivStats: true);
        var after = capital.CityStats.CurrentCityStats;
        Assert.True(after.Food >= before.Food + 1f && after.Production >= before.Production + 1f && after.Science >= before.Science + 1f && after.Gold >= before.Gold + 1f && after.Culture >= before.Culture + 1f);
    }

    [Fact]
    public void BuildingClassTenetsReachTheCityStats()
    {
        var (game, civ, capital) = NewCapital();
        float Stat(Func<Unciv.Models.StatTypes.Stats, float> pick)
        {
            capital.CityStats.Update(updateCivStats: true);
            return pick(capital.CityStats.CurrentCityStats);
        }
        // Happiness from a building class (Universal Healthcare: the Palace of the capital; Socialist Realism: the Monument)
        Follow(civ, "Order");
        float happiness = Stat(it => it.Happiness);
        civ.Policies.Adopt(game.Ruleset.Policies["Universal Healthcare (Order)"]);
        Assert.Equal(happiness + 1f, Stat(it => it.Happiness));
        capital.CityConstructions.AddBuilding("Monument");
        happiness = Stat(it => it.Happiness);
        civ.Policies.Adopt(game.Ruleset.Policies["Socialist Realism"]);
        Assert.Equal(happiness + 2f, Stat(it => it.Happiness));
        // Yield of a building class: Workers' Faculties, +25% Science from the Factory
        capital.CityConstructions.AddBuilding("Factory");
        float science = Stat(it => it.Science);
        civ.Policies.Adopt(game.Ruleset.Policies["Workers' Faculties"]);
        Assert.True(Stat(it => it.Science) >= science);
        Assert.Contains("[+25]% [Science] from every [Factory]", game.Ruleset.Policies["Workers' Faculties"].Uniques);
        // Capital yields: Third Alternative
        float food = Stat(it => it.Food);
        science = Stat(it => it.Science);
        civ.Policies.Adopt(game.Ruleset.Policies["Third Alternative"]);
        Assert.True(Stat(it => it.Food) >= food + 5f);
        Assert.True(Stat(it => it.Science) >= science + 5f);
    }

    [Fact]
    public void EconomicUnionPaysOnlyTheCivThatHasTheTenet()
    {
        var game = NewGameOf(2);
        var (a, b, _) = Majors(game);
        var (from, to) = (FoundCity(a), FoundCity(b));
        Follow(a, "Freedom", "Economic Union");
        Follow(b, "Freedom");
        int fromB = TradeRouteYields.Compute(to, from, TradeDomain.Land, TradeRouteKind.International).Origin[TradeYield.Gold];
        b.Policies.Ideology = "Order";
        Assert.Equal(fromB, TradeRouteYields.Compute(to, from, TradeDomain.Land, TradeRouteKind.International).Origin[TradeYield.Gold]);
    }

    // ---- the generic guard B027 and B032 lacked

    // BNW unique types (the BNW hook rows (from ContainsGreatWorkWhenBuilt)) that no object of the generated ruleset states yet. B030 emitted the
    // ideology tenets' types (TourismCommonFoe, TourismLessHappy, TourismSharedIdeology, DomesticTradeRouteYield, GoldFromIdeologyTradeRoutes, YieldPerGreatWork,
    // TourismFromBuildings, TourismOnGreatPersonBirth, ...); the rest wait for their feature tickets (city-states, religion, units, espionage, B006 trade
    // units); see gen_ruleset_report.txt.
    // A type that is neither emitted nor listed here fails, so a Done engine ticket cannot again read a unique the ruleset never carries.
    private static readonly string[] UnemittedBnwTypes =
    [
        "AllowsAnnexingCityStates",
        "AllowsWalkingOnWater",
        "AnnoyFreeCityStateIntrusion",
        "BuildingPurchaseCostModifier",
        "CreatesGreatWork",
        "FaithFromKills",
        "FreePoliciesWhenExpended",
        "GoldFromCityConnections",
        "GoldFromCityConnectionsPercent",
        "GoldenAgeFromKills",
        "OnlyBuiltAsLeagueProject",
        "PurchaseOnly",
        "RemovesResourceWhenBuilt",
        "RequiresAdjacentLand",
        "RiversActAsTradeRoads",
        "TechFromCityConquest",
        "TourismDuringGoldenAge",
        "TradeBuildingModifier",
        "TradeMissionGoldModifier",
        "TradeMissionInfluenceModifier",
        "UnresearchedTechFromKills",
    ];

    // ---- B024: map generation

    private static readonly string[] IndonesianLuxuries = ["Cloves", "Pepper", "Nutmeg"];

    [Fact]
    public void IndonesianLuxuriesAreNeverPlacedAtMapGeneration()
    {
        var r = Real();
        Assert.All(IndonesianLuxuries, name =>
        {
            Assert.Contains("Doesn't generate naturally", r.TileResources[name].Uniques);
            Assert.Equal(ResourceType.Luxury, r.TileResources[name].ResourceType);
        });
        var grass = new TileMap(2, r, false);
        var tile = grass.Values.First();
        tile.BaseTerrain = "Grassland";
        tile.SetTerrainTransients();
        Assert.All(IndonesianLuxuries, name => Assert.False(r.TileResources[name].GeneratesNaturallyOn(tile), name));
        Assert.True(r.TileResources["Cocoa"].GeneratesNaturallyOn(tile));
        Assert.True(r.TileResources["Bison"].GeneratesNaturallyOn(tile));
    }

    [Fact]
    public void IndonesianLuxuriesAreNotInTheLuxuryAssignmentPool()
    {
        var game = StartBnwGame(3);
        var regions = new Unciv.Logic.Map.MapGenerator.Mapregions.MapRegions(game.Ruleset);
        regions.GenerateRegions(game.TileMap, 5);
        regions.AssignRegions(game.TileMap, game.Civilizations.Where(it => it.IsMajorCiv()).Cast<Unciv.Logic.Map.MapGenerator.Mapregions.IMapRegionsCivilization>().ToList(), new GameParameters());
        var (cityStateLuxuries, randomLuxuries) = Unciv.Logic.Map.MapGenerator.ResourcePlacement.LuxuryResourcePlacementLogic.AssignLuxuries(
            regions.Regions.ToList(), regions.TileData, game.Ruleset);
        var assigned = cityStateLuxuries.Concat(randomLuxuries).Concat(regions.Regions.Select(it => it.Luxury)).ToList();
        Assert.All(IndonesianLuxuries, name => Assert.DoesNotContain(name, assigned));
    }

    [Fact]
    public void BisonAndCocoaAppearOnGeneratedMapsAndIndonesianLuxuriesNeverDo()
    {
        var placed = new HashSet<string>();
        foreach (var seed in new[] { 3L, 11L, 21L })
        {
            var game = StartBnwGame(seed);
            foreach (var tile in game.TileMap.Values)
                if (tile.Resource != null) placed.Add(tile.Resource);
        }
        Assert.Contains("Cocoa", placed);
        Assert.Contains("Bison", placed);
        Assert.DoesNotContain("Cloves", placed);
        Assert.DoesNotContain("Pepper", placed);
        Assert.DoesNotContain("Nutmeg", placed);
    }

    private static GameInfo StartBnwGame(long seed)
    {
        Models.Ruleset.BnwRulesetTests.LoadBnw();
        return GameStarter.StartNewGame(
            new GameSetupInfo(
                new GameParameters
                {
                    BaseRuleset = BaseRuleset.Civ_V_BNW.FullName(),
                    NumberOfCityStates = 6,
                    Players = [new Player("Brazil", PlayerType.Human), new Player("Poland"), new Player("Venice"), new Player("England"), new Player("Japan")],
                },
                new MapParameters { Type = MapType.Pangaea, Shape = MapShape.Rectangular, Seed = seed, MapSize = MapSize.Small }),
            5, 1000L);
    }

    private sealed class FakeStartCiv(string name) : Unciv.Logic.Map.MapGenerator.Mapregions.IMapRegionsCivilization
    {
        public string CivName => name;
        public string CivId => name;
        public List<string> GetStartBias(Ruleset ruleset) => ruleset.Nations[name].GetStartBias(ruleset).ToList();
        public GameRandom StateBasedRandom(string caller) => new(1);
    }

    // Region assignment on the REAL map of a started game: Venice (PlaceFirstAlongOcean, last in the player list) takes the most coastal start;
    // without the flag the first coast civ does, which proves the flag (not the list order) decides it.
    private static (string Venice, string England, string Best) AssignCoastStarts(bool placeFirst)
    {
        var game = StartBnwGame(3);
        var ruleset = game.Ruleset;
        var map = game.TileMap;
        ruleset.Nations["Venice"].PlaceFirstAlongOcean = placeFirst;
        map.StartingLocations.Clear();
        map.StartingLocationsByNation.Clear();
        var regions = new Unciv.Logic.Map.MapGenerator.Mapregions.MapRegions(ruleset);
        regions.GenerateRegions(map, 4);
        var civs = new[] { "England", "Japan", "Portugal", "Venice" }.Select(it => new FakeStartCiv(it)).ToList();
        regions.AssignRegions(map, civs, new GameParameters());
        var best = regions.Regions.Where(it => map[it.StartPosition!.Value].IsAdjacentToCoast())
            .MaxBy(it => it.GetTerrainAmount("Coastal"))!;
        string StartOf(string civ) => map.StartingLocationsByNation[civ].Single().Position.ToString();
        return (StartOf("Venice"), StartOf("England"), map[best.StartPosition!.Value].Position.ToString());
    }

    [Fact]
    public void VeniceIsPlacedFirstAlongTheOceanAndOnlyBecauseOfTheFlag()
    {
        Assert.True(Real().Nations["Venice"].PlaceFirstAlongOcean);
        Assert.Equal(1, Real().Nations.Values.Count(it => it.PlaceFirstAlongOcean));
        try
        {
            var flagged = AssignCoastStarts(true);
            Assert.Equal(flagged.Best, flagged.Venice);
            var unflagged = AssignCoastStarts(false);
            Assert.Equal(unflagged.Best, unflagged.England);
        }
        finally { RulesetCache.Clear(); }
    }

    // BuildingPurchaseCostModifier stays pending on purpose: Skyscrapers uses the generic '[stat] cost of purchasing [buildingFilter] buildings' unique.
    [Fact]
    public void EveryBnwUniqueTypeIsEmittedByTheRulesetOrListedAsPending()
    {
        var r = Real();
        var emitted = r.AllUniques().Select(it => it.Type).Where(it => it != null).Select(it => it!.Value).ToHashSet();
        var missing = Enum.GetValues<UniqueType>().Where(it => (int)it >= (int)UniqueType.ContainsGreatWorkWhenBuilt && !emitted.Contains(it)).Select(it => it.ToString()).Order(StringComparer.Ordinal).ToList();
        Assert.True(UnemittedBnwTypes.SequenceEqual(missing), string.Join(",", missing));
    }
}
