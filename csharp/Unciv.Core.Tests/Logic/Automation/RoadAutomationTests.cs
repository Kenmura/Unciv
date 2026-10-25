using Unciv.Core.Tests.Support;
using Unciv.Logic;
using Unciv.Logic.Automation.Unit;
using Unciv.Logic.Map;
using Unciv.Logic.Map.Tiles;
using Unciv.View;
using CityObject = Unciv.Logic.City.City;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;
using MapUnitClass = Unciv.Logic.Map.MapUnits.MapUnit;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Core.Tests.Logic.Automation;

// T092: C#-only cases for RoadToAutomation and RoadBetweenCitiesAutomation (the Kotlin tests only reach them through WorkerAutomationTest and
// PathfindingTests.connectRoadAction). Expected values are derived by hand from the Kotlin source.
[Collection("RulesetCache")]
public class RoadAutomationTests
{
    private readonly TestGame testGame = new();
    private readonly CivilizationObject civInfo;

    public RoadAutomationTests()
    {
        testGame.MakeHexagonalMap(8, Constants.Grassland);
        civInfo = testGame.AddCiv(isPlayer: true); // notifications are only kept for humans
        foreach (var tile in testGame.TileMap.Values)
            tile.SetExplored(civInfo, true);
    }

    private TileClass T(int x, int y) => testGame.GetTile(x, y);

    private void KnowRoads() => civInfo.Tech.AddTechnology("The Wheel", false);

    private MapUnitClass ConnectingWorker(TileClass from, TileClass to)
    {
        var worker = testGame.AddUnit("Worker", civInfo, from);
        worker.AutomatedRoadConnectionDestination = to.Position;
        worker.Automated = true;
        worker.Action = Constants.ConnectRoadAction;
        return worker;
    }

    private void Run(MapUnitClass worker) => new RoadToAutomation(civInfo).AutomateConnectRoad(worker, new HashSet<TileView>());

    private bool Notified(string text) => civInfo.Notifications.Any(it => it.Text == text);

    [Fact]
    public void WithoutAnyRoadTechNothingHappens()
    {
        var worker = ConnectingWorker(T(0, 0), T(3, 0));
        Run(worker);
        Assert.Null(worker.AutomatedRoadConnectionPath);
        Assert.Equal(Constants.ConnectRoadAction, worker.Action);
    }

    [Fact]
    public void WithoutDestinationTheAutomationIsStopped()
    {
        KnowRoads();
        var worker = ConnectingWorker(T(0, 0), T(3, 0));
        worker.AutomatedRoadConnectionDestination = null;
        Run(worker);
        Assert.False(worker.Automated);
        Assert.Null(worker.Action);
    }

    [Fact]
    public void AnUnreachableDestinationFails()
    {
        KnowRoads();
        testGame.SetTileTerrain(T(3, 0).Position, Constants.Ocean);
        var worker = ConnectingWorker(T(0, 0), T(3, 0));
        Run(worker);
        Assert.False(worker.Automated);
        Assert.Null(worker.AutomatedRoadConnectionDestination);
        Assert.True(Notified("Connect road failed!"));
    }

    [Fact]
    public void AWorkerMovedOffItsPathCancelsTheOperation()
    {
        KnowRoads();
        var worker = ConnectingWorker(T(0, 0), T(3, 0));
        worker.AutomatedRoadConnectionPath = [T(1, 0).Position, T(2, 0).Position, T(3, 0).Position];
        Run(worker);
        Assert.False(worker.Automated);
        Assert.True(Notified("Connect road cancelled!"));
    }

    [Fact]
    public void ABuildableStartTileStartsTheRoadThere()
    {
        KnowRoads();
        var worker = ConnectingWorker(T(0, 0), T(3, 0));
        Run(worker);
        Assert.Equal(nameof(RoadStatus.Road), T(0, 0).ImprovementInProgress);
        Assert.NotEmpty(worker.AutomatedRoadConnectionPath!);
    }

    [Fact]
    public void WorkersMoveOnToTheNextTileThatNeedsARoad()
    {
        KnowRoads();
        var start = T(0, 0);
        start.SetRoadStatus(RoadStatus.Road, civInfo);
        var worker = ConnectingWorker(start, T(3, 0));
        Run(worker);
        Assert.NotEqual(start, worker.GetTile());
        Assert.Contains(worker.GetTile().Position, worker.AutomatedRoadConnectionPath!);
        Assert.Equal(nameof(RoadStatus.Road), worker.GetTile().ImprovementInProgress);
    }

    [Fact]
    public void ARoadedLastTileCompletesTheConnection()
    {
        KnowRoads();
        var destination = T(2, 0);
        foreach (var tile in new[] { T(0, 0), T(1, 0), destination })
            tile.SetRoadStatus(RoadStatus.Road, civInfo);
        var worker = ConnectingWorker(destination, destination);
        worker.AutomatedRoadConnectionPath = [destination.Position];
        Run(worker);
        Assert.False(worker.Automated);
        Assert.True(Notified("Connect road completed"));
    }

    [Fact]
    public void ARoadedMiddleTileWithoutMovementLeftDoesNothing()
    {
        KnowRoads();
        var start = T(0, 0);
        start.SetRoadStatus(RoadStatus.Road, civInfo);
        var worker = ConnectingWorker(start, T(3, 0));
        worker.CurrentMovement = 0f;
        Run(worker);
        Assert.Equal(start, worker.GetTile());
        Assert.True(worker.Automated);
        Assert.Null(start.ImprovementInProgress);
    }

    [Fact]
    public void APillagedRoadIsRepairedFirst()
    {
        KnowRoads();
        var start = T(0, 0);
        start.SetRoadStatus(RoadStatus.Road, civInfo);
        start.RoadIsPillaged = true;
        var worker = ConnectingWorker(start, T(3, 0));
        Run(worker);
        Assert.Equal(Constants.Repair, start.ImprovementInProgress);
    }

    [Fact]
    public void StoppingTheAutomationCancelsARoadInProgress()
    {
        KnowRoads();
        var worker = ConnectingWorker(T(0, 0), T(3, 0));
        Run(worker);
        Assert.NotNull(T(0, 0).ImprovementInProgress);
        new RoadToAutomation(civInfo).StopAndCleanAutomation(worker);
        Assert.Null(T(0, 0).ImprovementInProgress);
        Assert.Null(worker.AutomatedRoadConnectionDestination);
    }

    [Fact]
    public void ShouldBuildRoadOnTileFollowsTheKotlinConditions()
    {
        KnowRoads();
        var automation = new RoadToAutomation(civInfo);
        Assert.True(automation.ShouldBuildRoadOnTile(T(1, 0)));
        T(1, 0).SetRoadStatus(RoadStatus.Road, civInfo);
        Assert.False(automation.ShouldBuildRoadOnTile(T(1, 0))); // already the best road
        T(1, 0).RoadIsPillaged = true;
        Assert.True(automation.ShouldBuildRoadOnTile(T(1, 0))); // pillaged: rebuild
        testGame.AddCity(civInfo, T(3, 0));
        Assert.False(automation.ShouldBuildRoadOnTile(T(3, 0))); // city center
        var marked = T(2, 0);
        marked.ImprovementFunctions.MarkForCreatesOneImprovement("Farm");
        Assert.False(automation.ShouldBuildRoadOnTile(marked));
    }

    // ---- RoadBetweenCitiesAutomation, reached through WorkerAutomation ----

    private (WorkerAutomation Worker, MapUnitClass Unit, CityObject A, CityObject B) TwoCities()
    {
        KnowRoads();
        var a = testGame.AddCity(civInfo, T(-3, 0));
        var b = testGame.AddCity(civInfo, T(3, 0));
        var unit = testGame.AddUnit("Worker", civInfo, T(-2, 0));
        return (new WorkerAutomation(civInfo, 1), unit, a, b);
    }

    [Fact]
    public void CitiesWithoutRoadsGetAPlanOverTheMapPathingRoute()
    {
        var (worker, unit, a, b) = TwoCities();
        var nearby = worker.RoadBetweenCitiesAutomation.GetNearbyCitiesToConnect(unit);
        Assert.Contains(a, nearby);
        Assert.Contains(b, nearby);
        Assert.True(worker.RoadBetweenCitiesAutomation.TilesOfRoadsMap.Count >= 7);
        Assert.Contains(T(0, 0), worker.RoadBetweenCitiesAutomation.TilesOfRoadsMap.Keys);
    }

    [Fact]
    public void WithoutRoadTechThereIsNothingToConnect()
    {
        testGame.AddCity(civInfo, T(-3, 0));
        testGame.AddCity(civInfo, T(3, 0));
        var unit = testGame.AddUnit("Worker", civInfo, T(-2, 0));
        var worker = new WorkerAutomation(civInfo, 1);
        Assert.Empty(worker.RoadBetweenCitiesAutomation.GetNearbyCitiesToConnect(unit));
        Assert.False(worker.RoadBetweenCitiesAutomation.TryConnectingCities(unit, [testGame.AddCity(civInfo, T(5, 5))]));
    }

    [Fact]
    public void AHumanWhoSwitchedAutoRoadsOffOnAClientBuildsNone()
    {
        KnowRoads();
        testGame.GameInfo.SetPlayerSettings(civInfo.CivId, new PlayerSettings { AutoBuildingRoads = false, HumanConquestPrompt = true });
        var automation = new RoadBetweenCitiesAutomation(civInfo, 1);
        Assert.Equal(RoadStatus.None, automation.BestRoadAvailable);
        testGame.GameInfo.ClientState.IsFullAutoPlayAI = true; // full AI autoplay ignores the setting
        Assert.Equal(RoadStatus.Road, new RoadBetweenCitiesAutomation(civInfo, 1).BestRoadAvailable);
        Assert.Equal(RoadStatus.Road, new RoadBetweenCitiesAutomation(civInfo, 2, new RoadBetweenCitiesAutomation(civInfo, 1)).BestRoadAvailable);
    }

    [Fact]
    public void TryConnectingCitiesWalksToTheBestTileAndStartsTheRoad()
    {
        var (worker, unit, _, _) = TwoCities();
        var candidates = worker.RoadBetweenCitiesAutomation.GetNearbyCitiesToConnect(unit);
        Assert.True(worker.RoadBetweenCitiesAutomation.TryConnectingCities(unit, candidates));
        Assert.Equal(nameof(RoadStatus.Road), unit.GetTile().ImprovementInProgress);
        Assert.False(worker.RoadBetweenCitiesAutomation.TryConnectingCities(unit, []));
    }

    [Fact]
    public void ThreeCitiesPickTheShortestOfTheirPlans()
    {
        KnowRoads();
        var middle = testGame.AddCity(civInfo, T(0, 0));
        testGame.AddCity(civInfo, T(-4, 0));
        testGame.AddCity(civInfo, T(4, 0));
        var unit = testGame.AddUnit("Worker", civInfo, T(1, 0));
        var worker = new WorkerAutomation(civInfo, 1);
        var nearby = worker.RoadBetweenCitiesAutomation.GetNearbyCitiesToConnect(unit);
        Assert.Contains(middle, nearby);
        var plan = worker.RoadBetweenCitiesAutomation.TilesOfRoadsMap[T(0, 0)];
        Assert.True(plan.FromCity == middle || plan.ToCity == middle);
    }

    [Fact]
    public void RailroadsPreferPlansOverExistingRoads()
    {
        civInfo.Tech.AddTechnology("The Wheel", false);
        civInfo.Tech.AddTechnology("Railroads", false);
        Assert.Equal(RoadStatus.Railroad, civInfo.Tech.GetBestRoadAvailable());
        var capital = testGame.AddCity(civInfo, T(-4, 0));
        testGame.AddCity(civInfo, T(4, 0));
        testGame.AddCity(civInfo, T(0, 4));
        for (var x = -4; x <= 4; x++) T(x, 0).SetRoadStatus(RoadStatus.Road, civInfo);
        var unit = testGame.AddUnit("Worker", civInfo, T(0, 0));
        var worker = new WorkerAutomation(civInfo, 1);
        Assert.NotEmpty(worker.RoadBetweenCitiesAutomation.GetNearbyCitiesToConnect(unit));
        Assert.Contains(capital, worker.RoadBetweenCitiesAutomation.GetNearbyCitiesToConnect(unit));
        var plan = worker.RoadBetweenCitiesAutomation.TilesOfRoadsMap[T(0, 0)];
        Assert.True(plan.Tiles.All(it => it.RoadStatus > RoadStatus.None || it.IsCityCenter()), "the plan reuses the existing road");
    }

    private sealed class FakeRoadPathing : ICityRoadPathing
    {
        public int Calls { get; private set; }
        public List<TileClass>? GetRoadPath(CityObject from, CityObject destination)
        {
            Calls++;
            return MapPathing.GetRoadPath(from.Civ, from.GetCenterTile(), destination.GetCenterTile());
        }

        public List<TileClass>? GetRoadPathToAny(CityObject city, IReadOnlySet<TileClass> destinations, int maxBfsReachPadding)
        {
            Calls++;
            var target = destinations.OrderBy(it => it.AerialDistanceTo(city.GetCenterTile())).First();
            return MapPathing.GetRoadPath(city.Civ, city.GetCenterTile(), target);
        }
    }

    [Fact]
    public void AStarRoadPathingIsUsedWhenAPathingIsInstalled()
    {
        var pathing = new FakeRoadPathing();
        testGame.GameInfo.EngineOptions.RoadPathing = pathing;
        var (worker, unit, _, _) = TwoCities();
        Assert.NotEmpty(worker.RoadBetweenCitiesAutomation.GetNearbyCitiesToConnect(unit));
        Assert.True(pathing.Calls > 0);
    }

    [Fact]
    public void ACityWithoutNeighbourPlansIsConnectedToTheCapitalByBfs()
    {
        KnowRoads();
        testGame.AddCity(civInfo, T(-7, 0)); // capital, too far (> 8) to be a neighbouring city
        var far = testGame.AddCity(civInfo, T(3, 0));
        var unit = testGame.AddUnit("Worker", civInfo, T(2, 0));
        var worker = new WorkerAutomation(civInfo, 1);
        var nearby = worker.RoadBetweenCitiesAutomation.GetNearbyCitiesToConnect(unit);
        Assert.Contains(far, nearby);
        Assert.True(worker.RoadBetweenCitiesAutomation.TilesOfRoadsMap.Count > 0);
    }

    [Fact]
    public void ACityWithoutNeighbourPlansIsConnectedToTheCapitalByAStarWhenInstalled()
    {
        var pathing = new FakeRoadPathing();
        testGame.GameInfo.EngineOptions.RoadPathing = pathing;
        KnowRoads();
        testGame.AddCity(civInfo, T(-7, 0));
        var far = testGame.AddCity(civInfo, T(3, 0));
        var unit = testGame.AddUnit("Worker", civInfo, T(2, 0));
        var worker = new WorkerAutomation(civInfo, 1);
        Assert.Contains(far, worker.RoadBetweenCitiesAutomation.GetNearbyCitiesToConnect(unit));
        Assert.True(pathing.Calls > 0);
    }
}
