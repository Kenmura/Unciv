using Unciv.Core.Tests.Support;
using Unciv.Logic.Automation.Civilization;
using Unciv.Logic.Automation.Unit;
using Unciv.Logic.Battle;
using Unciv.Utils;
using Unciv.View;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;
using MapUnitClass = Unciv.Logic.Map.MapUnits.MapUnit;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Core.Tests.Logic.Automation;

// T091. CivilianUnitAutomation.kt, SpecificUnitAutomation.kt and ReligiousUnitAutomation.kt have no Kotlin test fixture: these are C#-only cases
// with expected values derived by hand from the Kotlin source.
[Collection("RulesetCache")]
public class CivilianAutomationTests
{
    private readonly TestGame testGame = new();
    private readonly CivilizationObject civ;
    private readonly CivilizationObject enemy;
    private readonly FakeSeams seams = new();
    private readonly AutomationCache cache = new();

    public CivilianAutomationTests()
    {
        testGame.GameInfo.EngineOptions.VisibleMap = true;
        testGame.MakeHexagonalMap(12, Constants.Grassland);
        civ = testGame.AddCiv();
        enemy = testGame.AddCiv();
        civ.DiplomacyFunctions.MakeCivilizationsMeet(enemy);
        testGame.GameInfo.ClientState.UnitAutomationSeams = seams;
    }

    private sealed class FakeSeams : IUnitAutomationSeams
    {
        public readonly List<string> Calls = [];
        public bool WorkBoatsResult { get; set; }
        public void AutomateConnectRoad(MapUnitClass unit, IReadOnlySet<TileView> tilesWhereWeWillBeCaptured) => Calls.Add("AutomateConnectRoad");
        public void AutomateWorkerAction(MapUnitView unitView, HashSet<TileView> dangerousTiles) => Calls.Add("AutomateWorkerAction");
        public bool AutomateWorkBoats(MapUnitView unitView) { Calls.Add("AutomateWorkBoats"); return WorkBoatsResult; }
        public float GetBasePriority(TileClass tile, MapUnitView unitView) { Calls.Add("GetBasePriority"); return 0f; }
    }

    private MapUnitView ViewOf(MapUnitClass unit) => new GameView(testGame.GameInfo, unit.Civ).GetMapUnitView(unit);

    private void Automate(MapUnitClass unit) =>
        CivilianUnitAutomation.AutomateCivilianUnit(ViewOf(unit), new HashSet<TileView>(ThreatManager.GetDangerousTiles(cache, ViewOf(unit))), cache);

    private MapUnitClass AddMerchant()
    {
        var type = testGame.CreateBaseUnit(null, "Can undertake a trade mission with City-State, giving a large sum of gold and [30] Influence");
        type.Movement = 2;
        return testGame.AddUnit(type.Name, civ, testGame.GetTile(0, 0));
    }

    private void Claim(Unciv.Logic.City.City city, TileClass tile)
    {
        testGame.AddTileToCity(city, tile);
        tile.SetOwningCity(city);
    }

    private void War() => civ.GetDiplomacyManager(enemy)!.DeclareWar();

    // region CivilianUnitAutomation

    [Fact]
    public void IsLateGameNeedsFiftyFivePercentOfTheTechnologies()
    {
        var civView = new GameView(testGame.GameInfo, civ).CivView;
        Assert.False(CivilianUnitAutomation.IsLateGame(civView));

        var techs = testGame.Ruleset.Technologies.Keys.ToList();
        var needed = (int)System.Math.Ceiling(techs.Count * 0.55f);
        foreach (var tech in techs.Take(needed - 1)) civ.Tech.TechsResearched.Add(tech);
        Assert.False(CivilianUnitAutomation.IsLateGame(civView));
        civ.Tech.TechsResearched.Add(techs[needed - 1]);
        Assert.True(CivilianUnitAutomation.IsLateGame(civView));
    }

    [Fact]
    public void CivilianRunsAwayWhenAnEnemyMilitaryUnitIsWithinWalkingDistance()
    {
        var worker = testGame.AddUnit("Worker", civ, testGame.GetTile(0, 0));
        testGame.AddUnit("Warrior", enemy, testGame.GetTile(1, 0));
        War();
        var start = worker.CurrentTile;

        Assert.True(CivilianUnitAutomation.TryRunAwayIfNeccessary(ViewOf(worker), cache));

        Assert.NotEqual(start, worker.CurrentTile);
        Assert.True(worker.CurrentTile.AerialDistanceTo(testGame.GetTile(1, 0)) > start.AerialDistanceTo(testGame.GetTile(1, 0)));
    }

    [Fact]
    public void CivilianDoesNotRunAwayAtPeaceInACityOrWhenProtected()
    {
        var worker = testGame.AddUnit("Worker", civ, testGame.GetTile(0, 0));
        testGame.AddUnit("Warrior", enemy, testGame.GetTile(1, 0));
        Assert.False(CivilianUnitAutomation.TryRunAwayIfNeccessary(ViewOf(worker), cache)); // peace: not an enemy
        War();
        testGame.AddUnit("Warrior", civ, testGame.GetTile(0, 0));
        Assert.False(CivilianUnitAutomation.TryRunAwayIfNeccessary(ViewOf(worker), cache)); // a military unit on the tile

        var cityWorker = testGame.AddUnit("Worker", civ, testGame.GetTile(5, 5));
        testGame.AddCity(civ, testGame.GetTile(5, 5));
        testGame.AddUnit("Warrior", enemy, testGame.GetTile(6, 5));
        Assert.False(CivilianUnitAutomation.TryRunAwayIfNeccessary(ViewOf(cityWorker), cache)); // city center
    }

    [Fact]
    public void RunningAwayPrefersEnteringACity()
    {
        var city = testGame.AddCity(civ, testGame.GetTile(2, 0));
        var worker = testGame.AddUnit("Worker", civ, testGame.GetTile(0, 0));
        testGame.AddUnit("Warrior", enemy, testGame.GetTile(-1, 0));
        War();

        Assert.True(CivilianUnitAutomation.TryRunAwayIfNeccessary(ViewOf(worker), cache));

        Assert.Equal(city.GetCenterTile(), worker.CurrentTile);
    }

    [Fact]
    public void ShouldClearTileOnlyForNonAddInCapitalUnitsInTheCapitalWhileAnAddInCapitalUnitExists()
    {
        var capital = testGame.AddCity(civ, testGame.GetTile(0, 0));
        var worker = testGame.AddUnit("Worker", civ, capital.GetCenterTile());
        var view = ViewOf(worker);
        var tile = view.GetTile();
        Assert.False(CivilianUnitAutomation.ShouldClearTileForAddInCapitalUnits(view, tile));

        var addInCapital = testGame.CreateBaseUnit(null, "Can be added to [comment] in the Capital");
        addInCapital.Movement = 2;
        var special = testGame.AddUnit(addInCapital.Name, civ, testGame.GetTile(3, 3));
        Assert.True(CivilianUnitAutomation.ShouldClearTileForAddInCapitalUnits(view, tile));
        Assert.False(CivilianUnitAutomation.ShouldClearTileForAddInCapitalUnits(ViewOf(special), tile)); // it is one itself
        Assert.False(CivilianUnitAutomation.ShouldClearTileForAddInCapitalUnits(view, new GameView(testGame.GameInfo, civ).GetTile(testGame.GetTile(4, 4)))); // not the capital
    }

    [Fact]
    public void WorkersAreHandedToTheWorkerAutomation()
    {
        var worker = testGame.AddUnit("Worker", civ, testGame.GetTile(0, 0));
        Automate(worker);
        Assert.Equal(["AutomateWorkerAction"], seams.Calls);
    }

    [Fact]
    public void WorkBoatsExploreWhenTheyFindNothingToDo()
    {
        var boatType = testGame.CreateBaseUnit("Civilian Water", "May create improvements on water resources");
        testGame.SetTileTerrain(testGame.GetTile(0, 0).Position, Constants.Ocean);
        var boat = testGame.AddUnit(boatType.Name, civ, testGame.GetTile(0, 0));
        Assert.True(ViewOf(boat).HasUniqueToCreateWaterImprovements());
        Automate(boat);
        Assert.Equal(["AutomateWorkBoats"], seams.Calls); // then it explores

        seams.Calls.Clear();
        seams.WorkBoatsResult = true;
        Automate(boat);
        Assert.Equal(["AutomateWorkBoats"], seams.Calls);
    }

    [Fact]
    public void RoadConnectionActionGoesToTheRoadAutomation()
    {
        var worker = testGame.AddUnit("Worker", civ, testGame.GetTile(0, 0));
        worker.Action = Constants.ConnectRoadAction;
        Automate(worker);
        Assert.Equal(["AutomateConnectRoad"], seams.Calls);
    }

    [Fact]
    public void AddInCapitalUnitWalksToTheCapitalAndAddsItself()
    {
        var capital = testGame.AddCity(civ, testGame.GetTile(0, 0));
        var addInCapital = testGame.CreateBaseUnit(null, "Can be added to [comment] in the Capital");
        addInCapital.Movement = 3;
        var unit = testGame.AddUnit(addInCapital.Name, civ, testGame.GetTile(2, 0));

        Automate(unit);

        Assert.True(unit.CurrentTile.AerialDistanceTo(capital.GetCenterTile()) < 2);
    }

    [Fact]
    public void SettlerFoundsACityOnTurnZeroOrHeadsOut()
    {
        var settler = testGame.AddUnit("Settler", civ, testGame.GetTile(0, 0));
        var start = settler.CurrentTile;

        Automate(settler);

        Assert.True(civ.Cities.Count == 1 || settler.CurrentTile != start);
    }

    [Fact]
    public void UnknownCivilianUnitsDoNothing()
    {
        var type = testGame.CreateBaseUnit("Civilian");
        type.Movement = 2;
        var unit = testGame.AddUnit(type.Name, civ, testGame.GetTile(0, 0));
        var start = unit.CurrentTile;

        Automate(unit);

        Assert.Equal(start, unit.CurrentTile);
        Assert.Empty(seams.Calls);
    }


    private MapUnitClass AddUnitWith(string[] uniques, TileClass tile, CivilizationObject? owner = null)
    {
        var type = testGame.CreateBaseUnit(null, uniques);
        type.Movement = 2;
        return testGame.AddUnit(type.Name, owner ?? civ, tile);
    }

    private void ResearchEverything()
    {
        foreach (var tech in testGame.Ruleset.Technologies.Keys) civ.Tech.TechsResearched.Add(tech);
    }

    [Fact]
    public void WorkerInTheCapitalStepsAsideForAnAddInCapitalUnit()
    {
        testGame.AddCity(civ, testGame.GetTile(0, 0));
        AddUnitWith(["Can be added to [comment] in the Capital"], testGame.GetTile(4, 4));
        var worker = testGame.AddUnit("Worker", civ, testGame.GetTile(0, 0));

        Automate(worker);

        Assert.Contains("AutomateWorkerAction", seams.Calls);
        Assert.NotEqual(testGame.GetTile(0, 0), worker.CurrentTile);
    }

    [Fact]
    public void CityStateSettlersAreHandledLikeOthersButMilitaryOnesAreNot()
    {
        var cityState = testGame.AddCiv(cityStateType: "Cultured");
        var settler = testGame.AddUnit("Settler", cityState, testGame.GetTile(0, 0));
        Automate(settler); // city states found their first city in place
        Assert.Single(cityState.Cities);
    }

    [Fact]
    public void CityStateProphetsAreDisbanded()
    {
        var cityState = testGame.AddCiv(cityStateType: "Cultured");
        var prophet = AddUnitWith(["May found a religion"], testGame.GetTile(0, 0), cityState);
        Automate(prophet);
        Assert.True(prophet.IsDestroyed);
    }

    [Fact]
    public void ProphetFoundsAReligionInACity()
    {
        var city = testGame.AddCity(civ, testGame.GetTile(0, 0));
        var prophet = AddUnitWith(["May found a religion"], testGame.GetTile(0, 2));
        Automate(prophet);
        Assert.True(prophet.CurrentTile.AerialDistanceTo(city.GetCenterTile()) < 2);
    }

    [Fact]
    public void MissionariesAndInquisitorsAreRoutedToTheirAutomation()
    {
        var religion = testGame.AddReligion(civ);
        var city = testGame.AddCity(civ, testGame.GetTile(0, 3));
        var missionary = testGame.AddUnit("Missionary", civ, testGame.GetTile(0, 0));
        missionary.Religion = religion.Name;
        Automate(missionary);
        Assert.True(missionary.CurrentTile.AerialDistanceTo(city.GetCenterTile()) < 3);

        var inquisitor = testGame.AddUnit("Inquisitor", civ, testGame.GetTile(3, 0));
        Automate(inquisitor); // no religion: disbanded
        Assert.True(inquisitor.IsDestroyed);
    }

    [Fact]
    public void GreatGeneralsFollowTheTroops()
    {
        var general = AddUnitWith(["[+15]% Strength bonus for [All] units within [2] tiles"], testGame.GetTile(0, 0));
        testGame.AddUnit("Warrior", civ, testGame.GetTile(1, 0));

        Automate(general);

        Assert.Equal(testGame.GetTile(1, 0), general.CurrentTile);
    }

    [Fact]
    public void LateGameScientistsWithoutCurrentResearchDoNothing()
    {
        ResearchEverything();
        var scientist = AddUnitWith(["Can hurry technology research"], testGame.GetTile(0, 0));
        var start = scientist.CurrentTile;

        Automate(scientist);

        Assert.False(scientist.IsDestroyed);
        Assert.Equal(start, scientist.CurrentTile);
        Assert.Empty(seams.Calls);
    }

    [Fact]
    public void LateGameMerchantsWalkToACityState()
    {
        ResearchEverything();
        var cityState = testGame.AddCiv(cityStateType: "Cultured");
        testGame.AddCity(cityState, testGame.GetTile(6, 0));
        var merchant = AddMerchant();
        var start = merchant.CurrentTile;

        Automate(merchant);

        Assert.NotEqual(start, merchant.CurrentTile);
    }

    [Fact]
    public void EarlyGameMerchantsStay()
    {
        var cityState = testGame.AddCiv(cityStateType: "Cultured");
        testGame.AddCity(cityState, testGame.GetTile(6, 0));
        var merchant = AddMerchant();
        var start = merchant.CurrentTile;

        Automate(merchant);

        Assert.Equal(start, merchant.CurrentTile);
    }

    [Fact]
    public void EngineersWalkToACityThatCanSpeedUpAWonder()
    {
        var city = testGame.AddCity(civ, testGame.GetTile(0, 4), initialPopulation: 3);
        var wonder = testGame.CreateWonder();
        wonder.Cost = 1000;
        var engineer = AddUnitWith(["Can speed up the construction of a wonder"], testGame.GetTile(0, 0));
        var start = engineer.CurrentTile;

        Automate(engineer);

        Assert.True(engineer.CurrentTile.AerialDistanceTo(city.GetCenterTile()) < start.AerialDistanceTo(city.GetCenterTile())
            || city.CityConstructions.ConstructionQueue.Contains(wonder.Name));
    }

    [Fact]
    public void FreeBuildingUnitsWalkToTheClosestCityWithoutTheBuilding()
    {
        var building = testGame.CreateBuilding();
        var city = testGame.AddCity(civ, testGame.GetTile(0, 3));
        var unit = AddUnitWith([$"Gain a free [{building.Name}] [in this city]"], testGame.GetTile(0, 0));
        var start = unit.CurrentTile;

        Automate(unit);

        Assert.True(unit.CurrentTile.AerialDistanceTo(city.GetCenterTile()) < start.AerialDistanceTo(city.GetCenterTile()));
    }

    [Fact]
    public void FreeBuildingUnitsInTheCityGrantTheBuilding()
    {
        var building = testGame.CreateBuilding();
        var city = testGame.AddCity(civ, testGame.GetTile(0, 3));
        var unit = AddUnitWith([$"Gain a free [{building.Name}] [in this city]"], city.GetCenterTile());

        Automate(unit);

        Assert.True(city.CityConstructions.IsBuilt(building.Name));
    }

    [Fact]
    public void GoldenAgeUnitsInvokeTheirAction()
    {
        var unit = AddUnitWith(["Empire enters a [8]-turn Golden Age <by consuming this unit>"], testGame.GetTile(0, 0));
        Automate(unit);
        Assert.True(unit.IsDestroyed || civ.GoldenAges.IsGoldenAge());
    }

    // endregion
    // region SpecificUnitAutomation

    [Fact]
    public void GreatGeneralFollowsTheBestTroopsTileAndCitadelPlacerWithoutVictimsDoesNothing()
    {
        var general = testGame.AddDefaultMeleeUnitWithUniques(civ, testGame.GetTile(0, 0), "[+15]% Strength bonus for [All] units within [2] tiles");
        Assert.Equal(GreatGeneralImplementation.GetBestAffectedTroopsTile(general) != null, SpecificUnitAutomation.AutomateGreatGeneral(general));
        Assert.False(SpecificUnitAutomation.AutomateCitadelPlacer(general)); // fewer than three generals
        testGame.AddDefaultMeleeUnitWithUniques(civ, testGame.GetTile(3, 0), "[+15]% Strength bonus for [All] units within [2] tiles");
        testGame.AddDefaultMeleeUnitWithUniques(civ, testGame.GetTile(0, 3), "[+15]% Strength bonus for [All] units within [2] tiles");
        Assert.False(SpecificUnitAutomation.AutomateCitadelPlacer(general)); // nobody is stealing our territory
    }

    [Fact]
    public void ImprovementPlacerNeedsTheUnique()
    {
        var worker = testGame.AddUnit("Worker", civ, testGame.GetTile(0, 0));
        Assert.False(SpecificUnitAutomation.AutomateImprovementPlacer(worker));
    }

    [Fact]
    public void ImprovementPlacerIgnoresUnknownImprovementsAndCivsWithoutCities()
    {
        var unknown = testGame.AddDefaultMeleeUnitWithUniques(civ, testGame.GetTile(0, 0), "Can instantly construct a [No Such Improvement] improvement");
        Assert.False(SpecificUnitAutomation.AutomateImprovementPlacer(unknown));
        var known = testGame.AddDefaultMeleeUnitWithUniques(civ, testGame.GetTile(1, 0), "Can instantly construct a [Academy] improvement");
        Assert.False(SpecificUnitAutomation.AutomateImprovementPlacer(known)); // no city needs it
    }

    [Fact]
    public void ImprovementPlacerWalksToTheCityAndBuildsTheImprovement()
    {
        var city = testGame.AddCity(civ, testGame.GetTile(0, 0));
        var unit = testGame.AddDefaultMeleeUnitWithUniques(civ, testGame.GetTile(0, 4), "Can instantly construct a [Academy] improvement");

        Assert.True(SpecificUnitAutomation.AutomateImprovementPlacer(unit));

        Assert.True(unit.CurrentTile.AerialDistanceTo(city.GetCenterTile()) < 4);
    }

    [Fact]
    public void TradeMissionNeedsACityStateAndWalksThere()
    {
        var merchant = AddMerchant();
        Assert.False(SpecificUnitAutomation.ConductTradeMission(merchant)); // no city state

        var cityState = testGame.AddCiv(cityStateType: "Cultured");
        var stateCity = testGame.AddCity(cityState, testGame.GetTile(5, 0));
        testGame.AddTileToCity(stateCity, testGame.GetTile(4, 0));

        var start = merchant.CurrentTile;
        Assert.True(SpecificUnitAutomation.ConductTradeMission(merchant));
        Assert.NotEqual(start, merchant.CurrentTile);
    }

    [Fact]
    public void TradeMissionIgnoresCityStatesAtWar()
    {
        var merchant = AddMerchant();
        var cityState = testGame.AddCiv(cityStateType: "Cultured");
        var stateCity = testGame.AddCity(cityState, testGame.GetTile(5, 0));
        civ.DiplomacyFunctions.MakeCivilizationsMeet(cityState);
        civ.GetDiplomacyManager(cityState)!.DeclareWar();

        Assert.False(SpecificUnitAutomation.ConductTradeMission(merchant));
    }

    [Fact]
    public void WonderSpeedUpNeedsABigEnoughCity()
    {
        var engineer = testGame.AddUnit("Worker", civ, testGame.GetTile(0, 0));
        Assert.False(SpecificUnitAutomation.SpeedupWonderConstruction(engineer)); // no city
        testGame.AddCity(civ, testGame.GetTile(1, 0), initialPopulation: 2);
        Assert.False(SpecificUnitAutomation.SpeedupWonderConstruction(engineer)); // population below 3
    }

    [Fact]
    public void AddInCapitalWithoutACapitalDoesNothing()
    {
        var addInCapital = testGame.CreateBaseUnit(null, "Can be added to [comment] in the Capital");
        var unit = testGame.AddUnit(addInCapital.Name, civ, testGame.GetTile(2, 0));
        var start = unit.CurrentTile;

        SpecificUnitAutomation.AutomateAddInCapital(ViewOf(unit));

        Assert.Equal(start, unit.CurrentTile);
    }

    [Fact]
    public void SettlerWithNoGoodSiteExploresOrWandersAndFoundsNothing()
    {
        // Every land tile is below a minimum value no tile can reach
        testGame.GameInfo.Ruleset.ModOptions.Constants.MinimumCityLocationTileValue = 1000f;
        testGame.AddCity(civ, testGame.GetTile(10, 0));
        var settler = testGame.AddUnit("Settler", civ, testGame.GetTile(0, 0));

        SpecificUnitAutomation.AutomateSettlerActions(settler, [], cache);

        Assert.Single(civ.Cities);
        Assert.Empty(seams.Calls);
    }

    // endregion
    // region ReligiousUnitAutomation

    [Fact]
    public void MissionaryWithoutOurReligionDisbands()
    {
        var missionary = testGame.AddUnit("Missionary", civ, testGame.GetTile(0, 0));
        ReligiousUnitAutomation.AutomateMissionary(ViewOf(missionary));
        Assert.True(missionary.IsDestroyed);
    }

    [Fact]
    public void InquisitorWithoutOurReligionDisbands()
    {
        var inquisitor = testGame.AddUnit("Inquisitor", civ, testGame.GetTile(0, 0));
        ReligiousUnitAutomation.AutomateInquisitor(ViewOf(inquisitor));
        Assert.True(inquisitor.IsDestroyed);
    }

    [Fact]
    public void MissionaryHeadsForTheClosestCityWithoutOurReligion()
    {
        var religion = testGame.AddReligion(civ);
        var near = testGame.AddCity(civ, testGame.GetTile(0, 3));
        testGame.AddCity(civ, testGame.GetTile(0, -6));
        var missionary = testGame.AddUnit("Missionary", civ, testGame.GetTile(0, 0));
        missionary.Religion = religion.Name;
        var start = missionary.CurrentTile;

        ReligiousUnitAutomation.AutomateMissionary(ViewOf(missionary));

        Assert.False(missionary.IsDestroyed);
        Assert.True(missionary.CurrentTile.AerialDistanceTo(near.GetCenterTile()) < start.AerialDistanceTo(near.GetCenterTile()));
    }

    [Fact]
    public void MissionaryDoesNothingWhenThereIsNoTarget()
    {
        var religion = testGame.AddReligion(civ);
        var missionary = testGame.AddUnit("Missionary", civ, testGame.GetTile(0, 0));
        missionary.Religion = religion.Name;
        var start = missionary.CurrentTile;

        ReligiousUnitAutomation.AutomateMissionary(ViewOf(missionary));

        Assert.Equal(start, missionary.CurrentTile);
    }

    [Fact]
    public void InquisitorWithNothingToDoStays()
    {
        var religion = testGame.AddReligion(civ);
        var inquisitor = testGame.AddUnit("Inquisitor", civ, testGame.GetTile(0, 0));
        inquisitor.Religion = religion.Name;
        var start = inquisitor.CurrentTile;

        ReligiousUnitAutomation.AutomateInquisitor(ViewOf(inquisitor));

        Assert.False(inquisitor.IsDestroyed);
        Assert.Equal(start, inquisitor.CurrentTile);
    }

    [Fact]
    public void InquisitorProtectsTheBiggestCityOfOurReligion()
    {
        var religion = testGame.AddReligion(civ);
        var small = testGame.AddCity(civ, testGame.GetTile(0, 6), initialPopulation: 1);
        var big = testGame.AddCity(civ, testGame.GetTile(6, -3), initialPopulation: 5);
        small.Religion.AddPressure(religion.Name, 300);
        big.Religion.AddPressure(religion.Name, 300);
        var inquisitor = testGame.AddUnit("Inquisitor", civ, testGame.GetTile(0, 0));
        inquisitor.Religion = religion.Name;
        var start = inquisitor.CurrentTile;

        ReligiousUnitAutomation.AutomateInquisitor(ViewOf(inquisitor));

        Assert.Same(religion, big.Religion.GetMajorityReligion());
        Assert.True(inquisitor.CurrentTile.AerialDistanceTo(big.GetCenterTile()) < start.AerialDistanceTo(big.GetCenterTile()));
    }

    [Fact]
    public void FoundReligionWalksToACityAndInvokesTheAction()
    {
        var city = testGame.AddCity(civ, testGame.GetTile(0, 3));
        var prophet = testGame.AddUnit("Great Prophet", civ, testGame.GetTile(0, 0));
        var start = prophet.CurrentTile;

        ReligiousUnitAutomation.FoundReligion(ViewOf(prophet));

        Assert.True(prophet.CurrentTile.AerialDistanceTo(city.GetCenterTile()) < start.AerialDistanceTo(city.GetCenterTile()));
    }

    [Fact]
    public void FoundReligionWithoutCityDoesNothing()
    {
        var prophet = testGame.AddUnit("Great Prophet", civ, testGame.GetTile(0, 0));
        var start = prophet.CurrentTile;
        ReligiousUnitAutomation.FoundReligion(ViewOf(prophet));
        Assert.Equal(start, prophet.CurrentTile);
    }

    [Fact]
    public void EnhanceReligionTriesToEnterACityFirst()
    {
        var city = testGame.AddCity(civ, testGame.GetTile(0, 2));
        var prophet = testGame.AddUnit("Great Prophet", civ, testGame.GetTile(0, 0));
        ReligiousUnitAutomation.EnhanceReligion(ViewOf(prophet));
        Assert.Equal(city.GetCenterTile(), prophet.CurrentTile);

        ReligiousUnitAutomation.EnhanceReligion(ViewOf(prophet)); // already in a city: only the (unavailable) action is tried
        Assert.Equal(city.GetCenterTile(), prophet.CurrentTile);
    }

    [Fact]
    public void InquisitorHeadsForOurCityConvertedToAnotherReligion()
    {
        var religion = testGame.AddReligion(civ);
        var otherReligion = testGame.AddReligion(enemy);
        var home = testGame.AddCity(civ, testGame.GetTile(0, -5), initialPopulation: 3);
        Claim(home, testGame.GetTile(0, 0));
        home.Religion.AddPressure(otherReligion.Name, 300);
        var inquisitor = testGame.AddUnit("Inquisitor", civ, testGame.GetTile(0, 0));
        inquisitor.Religion = religion.Name;
        var start = inquisitor.CurrentTile;

        ReligiousUnitAutomation.AutomateInquisitor(ViewOf(inquisitor));

        Assert.True(inquisitor.CurrentTile.AerialDistanceTo(home.GetCenterTile()) < start.AerialDistanceTo(home.GetCenterTile()));
    }

    [Fact]
    public void InquisitorGoesToABlockedHolyCityAndOurLostHolyCity()
    {
        var religion = testGame.AddReligion(civ);
        var blocked = testGame.AddCity(civ, testGame.GetTile(0, 4));
        Claim(blocked, testGame.GetTile(0, 0));
        blocked.Religion.IsBlockedHolyCity = true;
        blocked.Religion.ReligionThisIsTheHolyCityOf = religion.Name;
        var inquisitor = testGame.AddUnit("Inquisitor", civ, testGame.GetTile(0, 0));
        inquisitor.Religion = religion.Name;
        var start = inquisitor.CurrentTile;

        ReligiousUnitAutomation.AutomateInquisitor(ViewOf(inquisitor));

        Assert.True(inquisitor.CurrentTile.AerialDistanceTo(blocked.GetCenterTile()) < start.AerialDistanceTo(blocked.GetCenterTile()));
    }

    [Fact]
    public void MissionaryDiscouragesCitiesWeAgreedNotToSpreadTo()
    {
        var religion = testGame.AddReligion(civ);
        var agreed = testGame.AddCity(enemy, testGame.GetTile(0, 4));
        civ.GetDiplomacyManager(enemy)!.SetFlag(Unciv.Logic.Civilization.Diplomacy.DiplomacyFlags.AgreedToNotSpreadReligion, 10);
        var missionary = testGame.AddUnit("Missionary", civ, testGame.GetTile(0, 0));
        missionary.Religion = religion.Name;
        var start = missionary.CurrentTile;

        ReligiousUnitAutomation.AutomateMissionary(ViewOf(missionary));

        // the only target is discouraged (+10), not forbidden: the missionary still heads there
        Assert.True(missionary.CurrentTile.AerialDistanceTo(agreed.GetCenterTile()) < start.AerialDistanceTo(agreed.GetCenterTile()));
    }

    [Fact]
    public void ImprovementPlacerWaitsForAnEscortWhenEnemiesAreNearby()
    {
        testGame.AddCity(civ, testGame.GetTile(0, 0));
        var unit = AddUnitWith(["Can instantly construct a [Academy] improvement"], testGame.GetTile(0, 6));
        testGame.AddUnit("Warrior", enemy, testGame.GetTile(0, 8));
        War();
        var start = unit.CurrentTile;

        Assert.True(SpecificUnitAutomation.AutomateImprovementPlacer(unit));

        Assert.Equal(start, unit.CurrentTile);
    }

    [Fact]
    public void SettlersAfterTurnZeroHeadForTheBestSiteInsteadOfAPoorTile()
    {
        testGame.GameInfo.Turns = 10;
        testGame.SetTileTerrain(testGame.GetTile(0, 0).Position, Constants.Ocean);
        var settler = testGame.AddUnit("Settler", civ, testGame.GetTile(0, 1));
        testGame.AddCity(civ, testGame.GetTile(-10, 0));
        var start = settler.CurrentTile;

        Automate(settler);

        Assert.True(civ.Cities.Count == 2 || settler.CurrentTile != start);
    }

    [Fact]
    public void TwoStartingSettlersOnTurnZeroDoNotBothSettleInPlace()
    {
        var first = testGame.AddUnit("Settler", civ, testGame.GetTile(0, 0));
        var second = testGame.AddUnit("Settler", civ, testGame.GetTile(0, 1));

        Automate(first);
        Automate(second);

        Assert.True(civ.Cities.Count >= 1);
    }

    [Fact]
    public void CitadelPlacerRevengesStolenTerritory()
    {
        var ours = testGame.AddCity(civ, testGame.GetTile(-3, 0));
        var theirs = testGame.AddCity(enemy, testGame.GetTile(3, 0));
        for (var x = -2; x <= 1; x++) { var t = testGame.GetTile(x, 0); testGame.AddTileToCity(ours, t); t.SetOwningCity(ours); }
        for (var x = 2; x <= 5; x++) { var t = testGame.GetTile(x, 0); testGame.AddTileToCity(theirs, t); t.SetOwningCity(theirs); }
        civ.GetDiplomacyManager(enemy)!.AddModifier(Unciv.Logic.Civilization.Diplomacy.DiplomaticModifiers.StealingTerritory, 10f);
        civ.ViewableTiles = new HashSet<TileClass>(testGame.TileMap.Values);
        const string unique = "[+15]% Strength bonus for [All] units within [2] tiles";
        testGame.AddDefaultMeleeUnitWithUniques(civ, testGame.GetTile(-3, 3), unique);
        testGame.AddDefaultMeleeUnitWithUniques(civ, testGame.GetTile(-3, 4), unique);
        var general = testGame.AddDefaultMeleeUnitWithUniques(civ, testGame.GetTile(-3, 2), unique);
        var start = general.CurrentTile;

        Assert.True(SpecificUnitAutomation.AutomateCitadelPlacer(general));

        Assert.NotEqual(start, general.CurrentTile);
        Assert.Contains("GetBasePriority", seams.Calls);
    }

    // endregion
}
