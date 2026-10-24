using Unciv.Core.Tests.Support;
using Unciv.Logic.Automation.Civilization;
using Unciv.Logic.Automation.Unit;
using Unciv.Logic.Civilization.Managers;
using Unciv.Logic.Map.Tiles;
using Unciv.Models.StatTypes;
using Unciv.View;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Core.Tests.Logic.Automation;

// T092: port of tests/src/com/unciv/logic/automation/unit/WorkerAutomationTest.kt over the real TestGame.
// The Kotlin setUp sets `useAStarPathfinding = true`; the A* PathingMap is T093, so these run on the default (non-A*) road pathing.
// The Kotlin cases that drive whole turns through NextTurnAutomation.automateCivMoves (should build improvements in turns / without roads /
// should build roads in turns; the fourth, @Ignore'd, is not ported) run the original loop (T094; T092 had adapted them to automate only the worker).
[Collection("RulesetCache")]
public class WorkerAutomationTests
{
    private readonly TestGame testGame = new();
    private readonly CivilizationObject civInfo;
    private readonly WorkerAutomation workerAutomation;

    public WorkerAutomationTests()
    {
        testGame.MakeHexagonalMap(7);
        civInfo = testGame.AddCiv();
        workerAutomation = new WorkerAutomation(civInfo, 3);
    }

    private MapUnitView ViewOf(Unciv.Logic.Map.MapUnits.MapUnit unit) => new GameView(testGame.GameInfo, civInfo).GetMapUnitView(unit);

    private void Research(params string[] improvements)
    {
        foreach (var improvement in improvements)
            civInfo.Tech.TechsResearched.Add(testGame.Ruleset.TileImprovements[improvement].TechRequired!);
    }

    private void Automate(Unciv.Logic.Map.MapUnits.MapUnit unit) => workerAutomation.AutomateWorkerAction(ViewOf(unit), []);

    [Fact]
    public void ShouldReplaceAlreadyExistingImprovementToEnableResource()
    {
        Research(nameof(RoadStatus.Road), "Farm", "Mine");
        civInfo.Tech.TechsResearched.Add(testGame.Ruleset.TileResources["Iron"].RevealedBy!);
        testGame.AddCity(civInfo, testGame.TileMap[0, 0]);

        var currentTile = testGame.TileMap[1, 1]; // owned by city
        currentTile.SetImprovementBasic("Farm"); // Set existing improvement
        currentTile.SetTileResource("Iron"); // This tile also has a resource needs to be enabled by a building a Mine

        Automate(testGame.AddUnit("Worker", civInfo, currentTile));

        Assert.Equal("Mine", currentTile.ImprovementInProgress);
        Assert.True(currentTile.TurnsToImprovement > 0);
    }

    [Fact]
    public void ShouldRemoveTerrainFeatureEnableResource()
    {
        Research("Remove Forest", "Plantation");
        testGame.AddCity(civInfo, testGame.TileMap[0, 0]);

        var currentTile = testGame.TileMap[1, 1];
        currentTile.AddTerrainFeature("Hill");
        currentTile.AddTerrainFeature("Forest");
        currentTile.SetTileResource("Citrus");
        currentTile.ResourceAmount = 1;

        Automate(testGame.AddUnit("Worker", civInfo, currentTile));

        Assert.Equal("Remove Forest", currentTile.ImprovementInProgress);
        Assert.True(currentTile.TurnsToImprovement > 0);
    }

    [Fact]
    public void ShouldBuildImprovementOnUnseenResource()
    {
        Research(nameof(RoadStatus.Road), "Farm");
        var currentTile = testGame.TileMap[1, 1];
        var city = testGame.AddCity(civInfo, testGame.TileMap[0, 0]);
        // Currently worked tile is prioritized for worker actions
        city.ClearWorkedTiles();
        city.WorkTile(currentTile);
        currentTile.BaseTerrain = Constants.Grassland;
        currentTile.SetTileResource("Iron");

        Automate(testGame.AddUnit("Worker", civInfo, currentTile));

        Assert.Equal("Farm", currentTile.ImprovementInProgress); // it can't see Iron and has nothing else to do
        Assert.True(currentTile.TurnsToImprovement > 0);
    }

    [Fact]
    public void ShouldPrioritiseBetterTiles()
    {
        Research(nameof(RoadStatus.Road), "Farm", "Mine");
        testGame.AddCity(civInfo, testGame.TileMap[0, 0]);
        foreach (var tile in testGame.TileMap[0, 0].GetTilesInDistance(3))
            tile.BaseTerrain = Constants.Grassland;

        var currentTile = testGame.TileMap[1, 1];
        currentTile.BaseTerrain = Constants.Grassland;
        currentTile.AddTerrainFeature(Constants.Hill);
        currentTile.SetTileResource("Gold Ore");

        Automate(testGame.AddUnit("Worker", civInfo, currentTile));

        Assert.Equal("Mine", currentTile.ImprovementInProgress); // the Gold Ore luxury resource
        Assert.True(currentTile.TurnsToImprovement > 0);
    }

    [Fact]
    public void ShouldRepairPillagedTile()
    {
        Research("Mine");
        civInfo.Tech.TechsResearched.Add(testGame.Ruleset.TileResources["Iron"].RevealedBy!);
        testGame.AddCity(civInfo, testGame.TileMap[0, 0]);

        var currentTile = testGame.TileMap[1, 1]; // owned by city
        currentTile.SetTileResource("Iron"); // This tile also has a resource needs to be enabled by a building a Mine
        currentTile.SetImprovement("Mine");
        currentTile.SetPillaged();

        Automate(testGame.AddUnit("Worker", civInfo, currentTile));

        Assert.Equal("Repair", currentTile.ImprovementInProgress);
        Assert.True(currentTile.TurnsToImprovement > 0);
    }

    [Fact]
    public void AutomatedWorkersShouldNotTargetCreatesOneImprovementMarkers()
    {
        Research("Farm");
        var city = testGame.AddCity(civInfo, testGame.TileMap[0, 0]);

        var workerTile = city.GetCenterTile();
        var markedTile = testGame.TileMap[1, 1];
        markedTile.BaseTerrain = Constants.Grassland;
        markedTile.ImprovementFunctions.MarkForCreatesOneImprovement("Farm");

        var worker = testGame.AddUnit("Worker", civInfo, workerTile);
        worker.CurrentMovement = 2f;

        Automate(worker);

        Assert.NotEqual(markedTile, worker.GetTile());
        Assert.True(markedTile.IsMarkedForCreatesOneImprovement());
        Assert.Equal("Farm", markedTile.ImprovementInProgress);
        Assert.Equal(-1, markedTile.TurnsToImprovement);
    }

    [Fact]
    public void WorkerOrdersShouldNotOverwriteCreatesOneImprovementMarkers()
    {
        Research("Farm");
        testGame.AddCity(civInfo, testGame.TileMap[0, 0]);

        var markedTile = testGame.TileMap[1, 1];
        markedTile.BaseTerrain = Constants.Grassland;
        markedTile.ImprovementFunctions.MarkForCreatesOneImprovement("Farm");

        var worker = testGame.AddUnit("Worker", civInfo, markedTile);
        markedTile.StartWorkingOnImprovement(testGame.Ruleset.TileImprovements["Farm"], civInfo, worker);

        Assert.Equal("Farm", markedTile.ImprovementInProgress);
        Assert.Equal(-1, markedTile.TurnsToImprovement);
    }

    // The Kotlin loop of the NextTurnAutomation-driven cases: whole civ turns through NextTurnAutomation.AutomateCivMoves.
    private void RunTurns(Unciv.Logic.Map.MapUnits.MapUnit worker, int turns, Action beforeAutomation, Action? afterTurn = null)
    {
        for (var i = 0; i < turns; i++)
        {
            worker.CurrentMovement = 2f;
            foreach (var unit in civInfo.Units.GetCivUnits().ToList())
                if (unit != worker && unit.IsCivilian()) unit.Disband(); // Disband any workers that may have been built in this time period
            beforeAutomation();
            NextTurnAutomation.AutomateCivMoves(civInfo);
            new TurnManager(civInfo).EndTurn();
            testGame.GameInfo.Turns++; // Invalidate WorkerAutomationCache
            afterTurn?.Invoke();
        }
    }

    [Fact]
    public void ShouldBuildImprovementsInTurns()
    {
        Research(nameof(RoadStatus.Road), "Farm");
        var centerTile = testGame.TileMap[0, 0]; // owned by city
        var city = testGame.AddCity(civInfo, centerTile);
        civInfo.AddGold(100000000);
        foreach (var tile in centerTile.GetTilesInDistanceRange(new Unciv.Utils.IntRange(2, 3)))
            city.Expansion.BuyTile(tile);
        foreach (var tile in centerTile.GetTilesInDistance(3))
            tile.BaseTerrain = Constants.Plains;
        city.ReassignAllPopulation();
        var worker = testGame.AddUnit("Worker", civInfo, centerTile);

        RunTurns(worker, 38, () =>
        {
            civInfo.AddGold(-civInfo.Gold); // Prevent any sort of worker spawning
            civInfo.Policies.FreePolicies = 0;
            civInfo.AddStat(Stat.Science, -100000);
        }, () =>
        {
            if (city.Population.Population != 1) city.Population.AddPopulation(1 - city.Population.Population); // Set the population back to 1
        });

        var finishedCount = centerTile.GetTilesInDistance(3).Count(it => it.Improvement != null && !it.IsCityCenter());
        var inProgressCount = centerTile.GetTilesInDistance(3).Count(it => it.TurnsToImprovement != 0);
        Assert.True(inProgressCount <= 1, $"Worker improvements in progress was greater than 1, actual: {inProgressCount}");
        Assert.True(finishedCount >= 5, $"Worker should have built at least 5 improvements but only built {finishedCount}");
    }

    [Fact]
    public void ShouldBuildImprovementsInTurnsWithoutRoads()
    {
        Research("Farm");
        var city1 = testGame.AddCity(civInfo, testGame.TileMap[-2, 1]);
        var city2 = testGame.AddCity(civInfo, testGame.TileMap[2, -1]);
        var cities = new[] { city1, city2 };
        civInfo.AddGold(100000000);
        foreach (var city in cities)
            foreach (var tile in city.GetCenterTile().GetTilesInDistance(3))
            {
                if (tile.OwningCity == null) city.Expansion.BuyTile(tile);
                tile.BaseTerrain = Constants.Grassland;
            }
        foreach (var city in cities) city.ReassignAllPopulation();
        var worker = testGame.AddUnit("Worker", civInfo, city1.GetCenterTile());

        RunTurns(worker, 38, () =>
        {
            civInfo.AddGold(-civInfo.Gold);
            civInfo.Policies.FreePolicies = 0;
            civInfo.AddStat(Stat.Science, -100000);
        }, () =>
        {
            foreach (var city in cities)
                if (city.Population.Population != 1) city.Population.AddPopulation(1 - city.Population.Population);
        });

        var checkedTiles = cities.SelectMany(it => it.GetCenterTile().GetTilesInDistance(3)).Distinct().ToList();
        var finishedCount = checkedTiles.Count(it => it.Improvement != null && !it.IsCityCenter());
        var inProgressCount = checkedTiles.Count(it => it.TurnsToImprovement != 0);
        Assert.True(inProgressCount <= 1, $"Worker improvements in progress was greater than 1, actual: {inProgressCount}");
        Assert.True(finishedCount >= 5, $"Worker should have built at least 5 improvements but only built {finishedCount}");
    }

    [Fact]
    public void ShouldBuildRoadsInTurns()
    {
        Research(nameof(RoadStatus.Road));
        civInfo.Tech.TechsResearched.Remove(testGame.Ruleset.TileImprovements["Farm"].TechRequired!);

        var city1 = testGame.AddCity(civInfo, testGame.TileMap[3, 3]);
        var city2 = testGame.AddCity(civInfo, testGame.TileMap[-3, -3]);
        // preexisting road along half, just to complicate things
        foreach (var x in new[] { 3, 2, 1, 0 })
            testGame.TileMap[x, x].SetRoadStatus(RoadStatus.Railroad, civInfo);
        civInfo.AddGold(100000000);
        foreach (var city in new[] { city1, city2 })
            foreach (var tile in city.GetCenterTile().GetTilesInDistance(3))
                if (tile.OwningCity == null) city.Expansion.BuyTile(tile);
        foreach (var city in new[] { city1, city2 })
        {
            city.Population.AddPopulation(10); // Make sure that we want to build a road between the cities
            for (var i = 0; i < 3; i++) city.CityConstructions.AddBuilding("Stock Exchange"); // gold per turn so we want the roads
        }
        var worker = testGame.AddUnit("Worker", civInfo, city1.GetCenterTile());

        RunTurns(worker, 25, () =>
        {
            civInfo.AddGold(-civInfo.Gold);
            civInfo.Policies.FreePolicies = 0;
        }, () => civInfo.Tech.TechsResearched.Remove(testGame.Ruleset.TileImprovements["Farm"].TechRequired!)); // the civ will annoyingly try to research it again

        var finishedCount = testGame.TileMap[0, 0].GetTilesInDistance(5).Count(it => it.RoadStatus != RoadStatus.None && !it.IsCityCenter());
        var inProgressCount = testGame.TileMap[0, 0].GetTilesInDistance(5).Count(it => it.TurnsToImprovement != 0);
        Assert.True(inProgressCount <= 1, $"Worker roads in progress was greater than 1, actual: {inProgressCount}");
        Assert.True(finishedCount >= 5, $"Worker should have built 5 roads but only built {finishedCount}");
        civInfo.Cache.UpdateCitiesConnectedToCapital();
        Assert.True(city2.IsConnectedToCapital(), "Worker should have built roads to connect the two cities");
    }

    // ---- C#-only branch cases (derived by hand from WorkerAutomation.kt) ----

    private Unciv.Logic.Map.MapUnits.MapUnit WaterWorker(Unciv.Logic.Map.Tiles.Tile tile)
    {
        var boatType = testGame.CreateBaseUnit("Civilian Water", "May create improvements on water resources");
        boatType.Movement = 2;
        return testGame.AddUnit(boatType.Name, civInfo, tile);
    }

    private Unciv.Logic.Map.Tiles.Tile SeaResource(int x, int y, string resource = "Oil")
    {
        civInfo.Tech.AddTechnology("Sailing");
        var tile = testGame.SetTileTerrain(Unciv.Logic.Map.HexCoord.Of(x, y), "Coast");
        tile.SetTileResource(resource);
        tile.SetExplored(civInfo, true);
        return tile;
    }

    [Fact]
    public void WorkBoatsHeadTowardsAndImproveAnOceanResource()
    {
        var city = testGame.AddCity(civInfo, testGame.TileMap[0, 0]);
        var target = SeaResource(3, 0, "Fish");
        for (var x = 1; x <= 2; x++) testGame.SetTileTerrain(Unciv.Logic.Map.HexCoord.Of(x, 0), "Coast"); // a water route to the fish
        foreach (var tile in testGame.TileMap[0, 0].GetTilesInDistance(3)) { testGame.AddTileToCity(city, tile); tile.SetOwningCity(city); }
        city.TilesInRange.Add(target);
        var boat = WaterWorker(testGame.TileMap[1, 0]);

        boat.CurrentMovement = 1f; // not enough to arrive this turn
        Assert.True(workerAutomation.AutomateWorkBoats(ViewOf(boat))); // moving counts as progress
        Assert.NotEqual(Unciv.Logic.Map.HexCoord.Of(1, 0), boat.GetTile().Position);

        boat.PutInTile(target);
        boat.CurrentMovement = 2f;
        Assert.True(workerAutomation.AutomateWorkBoats(ViewOf(boat))); // already there: creates the improvement
        Assert.True(boat.IsDestroyed); // the work boat is consumed
        Assert.Equal("Fishing Boats", target.Improvement);
    }

    [Fact]
    public void WorkBoatsWithoutAResourceInReachDoNothing()
    {
        testGame.AddCity(civInfo, testGame.TileMap[0, 0]);
        var boat = WaterWorker(testGame.SetTileTerrain(Unciv.Logic.Map.HexCoord.Of(-1, 0), "Coast"));
        Assert.False(workerAutomation.AutomateWorkBoats(ViewOf(boat)));
    }

    [Fact]
    public void AlphaFrontierStyleWorkersAlsoTryWorkBoats()
    {
        var city = testGame.AddCity(civInfo, testGame.TileMap[0, 0]);
        var target = SeaResource(2, 0);
        foreach (var tile in target.GetTilesInDistance(1)) { testGame.AddTileToCity(city, tile); tile.SetOwningCity(city); }
        var both = testGame.CreateBaseUnit("Civilian", "Can build [Land] improvements on tiles", "May create improvements on water resources");
        both.Movement = 2;
        var worker = testGame.AddUnit(both.Name, civInfo, testGame.TileMap[1, 0]);
        Automate(worker);
        Assert.NotNull(worker.GetTile()); // the boat target is out of its land reach; it did not crash on the dual-purpose path
    }

    [Fact]
    public void CityStateWorkersWithNothingToDoWander()
    {
        var cityState = testGame.AddCiv(cityStateType: "Cultured");
        testGame.AddCity(cityState, testGame.TileMap[0, 0]);
        var worker = testGame.AddUnit("Worker", cityState, testGame.TileMap[1, 0]);
        var start = worker.GetTile();
        new WorkerAutomation(cityState, 3).AutomateWorkerAction(new GameView(testGame.GameInfo, cityState).GetMapUnitView(worker), []);
        Assert.True(worker.GetTile() != start || worker.CurrentTile.ImprovementInProgress != null || start.ImprovementInProgress != null);
    }

    [Fact]
    public void WorkersHeadTowardsAnUndevelopedCity()
    {
        Research("Farm");
        testGame.AddCity(civInfo, testGame.TileMap[-5, 0]);
        var other = testGame.AddCity(civInfo, testGame.TileMap[5, 0]);
        foreach (var tile in other.GetCenterTile().GetTilesInDistance(2)) { testGame.AddTileToCity(other, tile); tile.SetOwningCity(other); tile.BaseTerrain = Constants.Grassland; }
        var worker = testGame.AddUnit("Worker", civInfo, testGame.TileMap[-4, 0]);
        testGame.AddUnit("Worker", civInfo, testGame.TileMap[-3, 0]); // a second worker near the first city only
        var start = worker.GetTile();
        Automate(worker);
        Assert.True(worker.GetTile() != start || start.ImprovementInProgress != null, "it either works nearby or heads for the other city");
    }

    [Fact]
    public void HumanWorkersMayBeToldNotToReplaceImprovementsOrRemoveVegetation()
    {
        var human = testGame.AddCiv(isPlayer: true);
        var clientState = testGame.GameInfo.ClientState;
        clientState.AutomatedWorkersReplaceImprovements = false;
        clientState.StopAutomatedWorkersRemoveVegetation = true;
        clientState.AutoPlay = new NoAutoPlay();
        human.Tech.TechsResearched.Add(testGame.Ruleset.TileImprovements["Farm"].TechRequired!);
        testGame.AddCity(human, testGame.TileMap[0, 0]);
        var tile = testGame.TileMap[1, 1];
        tile.BaseTerrain = Constants.Grassland;
        tile.AddTerrainFeature("Forest");
        tile.SetImprovementBasic("Farm");
        var worker = testGame.AddUnit("Worker", human, tile);
        new WorkerAutomation(human, 3).AutomateWorkerAction(new GameView(testGame.GameInfo, human).GetMapUnitView(worker), []);
        Assert.NotEqual("Remove Forest", tile.ImprovementInProgress);
    }

    private sealed class NoAutoPlay : Unciv.Logic.IAutoPlay
    {
        public bool IsAutoPlaying() => false;
        public void StopAutoPlay() { }
    }

    [Fact]
    public void ExistingFortsAreWorthHalfAndBonusResourcesAreImprovedJustEnough()
    {
        Research("Farm", "Mine", "Fort");
        testGame.AddCity(civInfo, testGame.TileMap[0, 0]);
        var fortTile = testGame.TileMap[1, 1];
        fortTile.BaseTerrain = Constants.Grassland;
        fortTile.SetImprovementBasic("Fort");
        Assert.True(WorkerAutomation.IsImprovementProbablyAFort(testGame.Ruleset.TileImprovements["Fort"]));
        Assert.False(WorkerAutomation.IsImprovementProbablyAFort(testGame.Ruleset.TileImprovements["Farm"]));
        Automate(testGame.AddUnit("Worker", civInfo, fortTile));

        civInfo.Tech.AddTechnology("Animal Husbandry");
        var bonusTile = testGame.TileMap[-1, -1];
        bonusTile.BaseTerrain = Constants.Grassland;
        bonusTile.SetTileResource("Cattle");
        bonusTile.SetImprovementBasic("Pasture");
        Automate(testGame.AddUnit("Worker", civInfo, bonusTile));
        Assert.True(float.IsFinite(workerAutomation.GetBasePriority(bonusTile, ViewOf(testGame.AddUnit("Worker", civInfo, bonusTile)))));
    }

    [Fact]
    public void WorkersSwapWithAnOtherCivilianOnTheirTarget()
    {
        Research("Farm");
        civInfo.Tech.AddTechnology("Pottery");
        testGame.AddCity(civInfo, testGame.TileMap[0, 0]);
        foreach (var tile in testGame.TileMap[0, 0].GetTilesInDistance(3)) tile.BaseTerrain = Constants.Grassland;
        var settler = testGame.AddUnit("Settler", civInfo, testGame.TileMap[1, 1]);
        var worker = testGame.AddUnit("Worker", civInfo, testGame.TileMap[3, 3]);
        Automate(worker);
        Assert.False(settler.IsDestroyed);
        Assert.False(worker.IsDestroyed);
    }
}
