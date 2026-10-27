using System.Text.Json;
using Unciv.Core.Tests.Support;
using Unciv.Json;
using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Logic.Files;
using Unciv.Models.StatTypes;
using Unciv.Queries;
using Unciv.Queries.Cities;
using Unciv.Queries.Tiles;
using Unciv.Queries.Units;
using Unciv.Utils;
using CityObject = Unciv.Logic.City.City;

namespace Unciv.Core.Tests.Queries.Cities;

// T196: city and tile panel queries, compared with the engine calls the screens use.
[Collection("RulesetCache")]
public class CityQueryTests
{
    private readonly TestGame testGame = new();
    private readonly Civilization player;
    private readonly Civilization enemy;
    private readonly CityObject city;
    private readonly QueryRegistry registry = new(UncivJson.DefaultOptions);

    public CityQueryTests()
    {
        testGame.MakeHexagonalMap(6, "Grassland");
        player = testGame.AddCiv(testGame.Ruleset.Nations["Germany"], isPlayer: true);
        enemy = testGame.AddCiv(testGame.Ruleset.Nations["Egypt"]);
        city = testGame.AddCity(player, testGame.GetTile(0, 0), initialPopulation: 3);
        registry.RegisterFromAssembly(typeof(QueryRegistry).Assembly);
        foreach (var tile in testGame.TileMap.TileList) tile.SetExplored(player, true);
    }

    private QueryResponse Execute(Civilization viewer, IQuery query)
    {
        var envelope = registry.ToEnvelope(viewer.CivName, query);
        var wire = JsonSerializer.Deserialize<QueryEnvelope>(JsonSerializer.Serialize(envelope, UncivJson.DefaultOptions), UncivJson.DefaultOptions)!;
        return registry.Execute(wire, new QueryContext(testGame.GameInfo));
    }

    private T Ask<T>(Civilization viewer, IQuery<T> query) where T : notnull
    {
        var response = Execute(viewer, query);
        Assert.True(response.Ok, $"{response.Code}: {response.Message}");
        return response.Result!.Value.Deserialize<T>(UncivJson.DefaultOptions)!;
    }

    private static ulong Hash(GameInfo game) => StateHasher.ComputeHash(h => h.Add(UncivFiles.GameInfoToString(game, forceZip: false)));

    private T AskPure<T>(IQuery<T> query) where T : notnull
    {
        ulong before = Hash(testGame.GameInfo);
        var first = Ask(player, query);
        Assert.Equal(before, Hash(testGame.GameInfo));
        var second = Ask(player, query);
        Assert.Equal(JsonSerializer.Serialize(first, UncivJson.DefaultOptions), JsonSerializer.Serialize(second, UncivJson.DefaultOptions));
        return first;
    }

    // ---- city panel

    [Fact]
    public void CityPanelMatchesTheEngine()
    {
        city.CityConstructions.EditQueue(q => { q.Clear(); q.Add("Monument"); q.Add("Gold"); });
        city.CityConstructions.AddBuilding("Palace");
        city.CityStats.Update();

        var panel = AskPure(new CityPanelQuery(city.Id));
        Assert.Equal(city.Name, panel.Name);
        Assert.Equal(new TilePos(0, 0), panel.Position);
        Assert.Equal(city.Population.Population, panel.Growth.Population);
        Assert.Equal(city.Population.GetFoodToNextPopulation(), panel.Growth.FoodToNextPopulation);
        Assert.Equal(city.FoodForNextTurn(), panel.Growth.FoodForNextTurn);
        Assert.Equal(city.CityStats.CurrentCityStats.Food, panel.TotalStats.Single(it => it.Stat == "Food").Value);
        Assert.Equal(city.WorkedTiles.Count, panel.WorkedTiles.Count);
        Assert.Equal(panel.WorkedTiles.OrderBy(it => it.Position.X).ThenBy(it => it.Position.Y).Select(it => it.Position), panel.WorkedTiles.Select(it => it.Position));
        Assert.Equal(["Monument", "Gold"], panel.Queue.Select(it => it.Name));
        Assert.Equal(city.CityConstructions.TurnsToConstruction("Monument", true), panel.Queue[0].Turns);
        Assert.Equal(testGame.Ruleset.Buildings["Monument"].GetProductionCost(player, city), panel.Queue[0].Cost);
        Assert.Null(panel.Queue[1].Turns);
        Assert.Contains(panel.Buildings, it => it.Name == "Palace" && it.SellPrice == city.GetGoldForSellingBuilding("Palace"));
        Assert.True(panel.IsCapital);
        Assert.NotEmpty(panel.StatSources);
        Assert.Equal(city.CityConstructions.CurrentConstructionName(), panel.CurrentConstruction);
        Assert.Null(panel.Garrison);
    }

    [Fact]
    public void CityPanelListsSpecialistsLockedTilesAndGarrison()
    {
        var specialist = testGame.Ruleset.Specialists.Keys.First();
        city.Population.SpecialistAllocations[specialist] = 1;
        var tile = city.GetWorkedTiles().First(it => !it.IsCityCenter());
        city.LockedTiles.Add(tile.Position);
        testGame.AddUnit("Warrior", player, testGame.GetTile(0, 0));

        var panel = AskPure(new CityPanelQuery(city.Id));
        Assert.Contains(panel.Specialists, it => it.Name == specialist && it.Amount == 1);
        Assert.Contains(panel.WorkedTiles, it => it.Locked && it.Position == new TilePos(tile.Position.X, tile.Position.Y));
        Assert.Equal("Warrior", panel.Garrison);
        Assert.Equal(city.Population.GetMaxSpecialists().Where(it => it.Value != 0).Count(), panel.MaxSpecialists.Count);
    }

    [Fact]
    public void DuplicateQueueEntriesUseTheirOwnPosition()
    {
        city.CityConstructions.EditQueue(q => { q.Clear(); q.Add("Warrior"); q.Add("Warrior"); });
        city.CityConstructions.InProgressConstructions["Warrior"] = 30;
        var queue = AskPure(new CityPanelQuery(city.Id)).Queue;
        Assert.Equal(city.CityConstructions.TurnsToConstruction("Warrior", true), queue[0].Turns);
        Assert.False(city.CityConstructions.IsFirstConstructionOfItsKind(1, "Warrior"));
        Assert.Equal(city.CityConstructions.TurnsToConstruction("Warrior", false), queue[1].Turns);
        Assert.NotEqual(queue[0].Turns, queue[1].Turns); // stored production counts for the first entry only
    }

    [Fact]
    public void CityQueriesRejectUnknownAndForeignCities()
    {
        var foreign = testGame.AddCity(enemy, testGame.GetTile(4, 0));
        foreach (var query in new IQuery[] { new CityPanelQuery("nope"), new CityPanelQuery(foreign.Id), new ConstructionListQuery(foreign.Id), new BuyableTilesQuery("x"), new CityTilesQuery(foreign.Id) })
        {
            var response = Execute(player, query);
            Assert.False(response.Ok);
            Assert.Equal(UnitQueryCodes.UnknownCity, response.Code);
        }
    }

    // ---- construction list

    [Fact]
    public void ConstructionListStatesCostsAndReasons()
    {
        city.CityConstructions.EditQueue(q => { q.Clear(); q.Add("Monument"); });
        city.CityConstructions.AddBuilding("Palace");
        player.AddGold(5000 - player.Gold);

        var list = AskPure(new ConstructionListQuery(city.Id, OnlyDisplayed: false));
        var byName = list.Entries.ToDictionary(it => it.Name);
        Assert.Equal(testGame.Ruleset.Units.Count + testGame.Ruleset.Buildings.Count + 6, list.Entries.Count);

        var monument = byName["Monument"];
        Assert.Equal("enqueued", monument.State);
        Assert.Equal("Building", monument.Category);
        Assert.Equal(city.CityConstructions.TurnsToConstruction("Monument", true), monument.Turns);
        var building = testGame.Ruleset.Buildings["Monument"];
        Assert.Equal(building.GetProductionCost(player, city), monument.Cost);
        var gold = Assert.Single(monument.BuyCosts, it => it.Stat == "Gold");
        Assert.Equal(building.GetStatBuyCost(city, Stat.Gold), gold.Cost);
        Assert.True(gold.Allowed);

        Assert.Equal("built", byName["Palace"].State);
        Assert.Equal("buildable", byName["Warrior"].State);
        Assert.Equal("Unit", byName["Warrior"].Category);
        Assert.Equal("Wonder", byName["Stonehenge"].Category);
        Assert.Equal("rejected", byName["Stonehenge"].State);
        Assert.Null(byName["Stonehenge"].Turns);
        Assert.Contains(byName["Stonehenge"].Rejections, it => it.Reason == "RequiresTech");
        Assert.Equal("NationalWonder", byName["National College"].Category);

        Assert.Equal("Perpetual", byName["Gold"].Category);
        Assert.Equal("rejected", byName["Gold"].State); // needs a unique that enables gold production
        Assert.Equal("buildable", byName["Nothing"].State);
        Assert.Null(byName["Nothing"].Cost);
        city.CityConstructions.EditQueue(q => q.Add("Nothing"));
        Assert.Equal("enqueued", AskPure(new ConstructionListQuery(city.Id)).Entries.Single(it => it.Name == "Nothing").State);
    }

    [Fact]
    public void ConstructionListDefaultsToWhatTheScreenDisplays()
    {
        var displayed = AskPure(new ConstructionListQuery(city.Id));
        Assert.All(displayed.Entries, it => Assert.True(it.Displayed));
        var all = Ask(player, new ConstructionListQuery(city.Id, false));
        Assert.True(all.Entries.Count > displayed.Entries.Count);
        Assert.Equal(displayed.Entries.Select(it => it.Name), all.Entries.Where(it => it.Displayed).Select(it => it.Name));
    }

    [Fact]
    public void BuyCostIsNotAllowedWithoutGold()
    {
        player.AddGold(-player.Gold);
        var entry = AskPure(new ConstructionListQuery(city.Id)).Entries.Single(it => it.Name == "Warrior");
        Assert.Contains(entry.BuyCosts, it => it.Stat == "Gold" && !it.Allowed);
    }

    // ---- city tiles

    [Fact]
    public void CityTilesClassifyTheWorkRangeAsTheCommandsJudgeIt()
    {
        var result = AskPure(new CityTilesQuery(city.Id));
        Assert.Equal(city.GetWorkRange(), result.WorkRange);
        Assert.Equal(city.Population.GetFreePopulation(), result.FreePopulation);
        Assert.Equal(city.TilesInRange.Count, result.Tiles.Count);
        Assert.Single(result.Tiles, it => it.State == "Center");
        var worked = result.Tiles.Where(it => it.State == "Worked").Select(it => (it.Position.X, it.Position.Y)).Order().ToList();
        Assert.Equal(city.WorkedTiles.Select(it => (it.X, it.Y)).Order().ToList(), worked);
        Assert.Equal(result.Tiles.OrderBy(it => it.Position.X).ThenBy(it => it.Position.Y).Select(it => (it.Position.X, it.Position.Y)),
            result.Tiles.Select(it => (it.Position.X, it.Position.Y)));
        Assert.Contains(result.Tiles, it => it.State == "Available");
        foreach (var tile in result.Tiles.Where(it => it.State == "Available"))
        {
            var engineTile = testGame.GetTile(tile.Position.X, tile.Position.Y);
            Assert.Contains(engineTile, city.GetWorkableTiles());
            Assert.False(engineTile.ProvidesYield());
            Assert.NotEmpty(tile.Stats);
        }
    }

    private CityTile TileState(string state) => Ask(player, new CityTilesQuery(city.Id)).Tiles.First(it => it.State == state);

    [Fact]
    public void CityTilesReportFreeCitizensAfterAWorkerIsReleased()
    {
        Assert.Equal(0, Ask(player, new CityTilesQuery(city.Id)).FreePopulation);
        var released = city.WorkedTiles.First();
        city.StopWorkingTile(testGame.GetTile(released.X, released.Y));
        var result = Ask(player, new CityTilesQuery(city.Id));
        Assert.Equal(1, result.FreePopulation);
        Assert.Equal(city.Population.GetFreePopulation(), result.FreePopulation);
        Assert.Contains(result.Tiles, it => it.Position.X == released.X && it.Position.Y == released.Y && it.State == "Available");
    }

    [Fact]
    public void CityTilesCallATileAnotherCityWorksTaken()
    {
        var shared = Ask(player, new CityTilesQuery(city.Id)).Tiles.First(it => it.State == "Available");
        var other = testGame.AddCity(player, testGame.GetTile(4, 0), initialPopulation: 1);
        var tile = testGame.GetTile(shared.Position.X, shared.Position.Y);
        Assert.Same(player, tile.GetOwner());
        Assert.False(city.IsWorked(tile));
        other.WorkedTiles.Add(tile.Position);
        Assert.True(tile.ProvidesYield());
        var taken = Ask(player, new CityTilesQuery(city.Id)).Tiles.Single(it => it.Position == shared.Position);
        Assert.Equal("Taken", taken.State);
    }

    [Fact]
    public void CityTilesCallABlockadedTileUnavailable()
    {
        var free = TileState("Available");
        var tile = testGame.GetTile(free.Position.X, free.Position.Y);
        player.DiplomacyFunctions.MakeCivilizationsMeet(enemy);
        player.GetDiplomacyManager(enemy)!.DeclareWar();
        testGame.AddUnit("Warrior", enemy, tile);
        Assert.True(tile.IsBlockaded());
        Assert.Equal("Unavailable", Ask(player, new CityTilesQuery(city.Id)).Tiles.Single(it => it.Position == free.Position).State);
    }

    [Fact]
    public void CityTilesCallATileThatYieldsNothingUnavailable()
    {
        var free = TileState("Available");
        var tile = testGame.GetTile(free.Position.X, free.Position.Y);
        tile.BaseTerrain = "Snow";
        tile.SetTerrainTransients();
        Assert.False(tile.IsBlockaded());
        var result = Ask(player, new CityTilesQuery(city.Id)).Tiles.Single(it => it.Position == free.Position);
        Assert.Equal("Unavailable", result.State);
        Assert.Empty(result.Stats);
    }

    [Fact]
    public void CityTilesShowLockedForeignAndUnownedTiles()
    {
        var workedPosition = city.WorkedTiles.First();
        city.LockTile(testGame.GetTile(workedPosition.X, workedPosition.Y));
        var foreignTile = testGame.GetTile(2, 0);
        testGame.AddCity(enemy, foreignTile);
        var result = Ask(player, new CityTilesQuery(city.Id));
        Assert.Contains(result.Tiles, it => it.Position.X == workedPosition.X && it.Position.Y == workedPosition.Y && it.State == "Worked" && it.Locked);
        Assert.Equal(1, result.Tiles.Count(it => it.Locked));
        Assert.Contains(result.Tiles, it => it.Position.X == 2 && it.Position.Y == 0 && it.State == "Foreign");
    }

    // ---- buyable tiles

    [Fact]
    public void BuyableTilesMatchTheExpansionManager()
    {
        player.AddGold(100 - player.Gold);
        var result = AskPure(new BuyableTilesQuery(city.Id));
        Assert.Equal(100, result.Gold);
        Assert.Equal(city.Expansion.GetCultureToNextTile(), result.CultureToNextTile);
        Assert.NotEmpty(result.Tiles);
        Assert.Equal(city.Expansion.GetChoosableTiles().Count(), result.Tiles.Count);
        foreach (var entry in result.Tiles)
        {
            var tile = testGame.GetTile(entry.X, entry.Y);
            Assert.Null(tile.GetOwner());
            Assert.Equal(city.Expansion.GetGoldCostOfTile(tile), entry.Cost);
            Assert.Equal(city.Expansion.CanBuyTile(tile), entry.CanBuy);
            Assert.Equal(entry.Cost <= 100, entry.Affordable);
        }
        Assert.Contains(result.Tiles, it => it.CanBuy);
        Assert.Contains(result.Tiles, it => !it.CanBuy);
    }

    [Fact]
    public void BuyableTilesColdAndWarmCachesAgree()
    {
        var cold = Ask(player, new BuyableTilesQuery(city.Id));
        city.CityStats.Update();
        _ = city.Expansion.GetCultureToNextTile();
        var warm = Ask(player, new BuyableTilesQuery(city.Id));
        Assert.Equal(JsonSerializer.Serialize(cold, UncivJson.DefaultOptions), JsonSerializer.Serialize(warm, UncivJson.DefaultOptions));
    }

    [Fact]
    public void BuyableTilesSkipUnexploredTilesAndFlagPuppets()
    {
        var far = city.Expansion.GetChoosableTiles().First(it => it.AerialDistanceTo(city.GetCenterTile()) == 2);
        far.SetExplored(player, false);
        var result = Ask(player, new BuyableTilesQuery(city.Id));
        Assert.DoesNotContain(result.Tiles, it => it.X == far.Position.X && it.Y == far.Position.Y);
        city.IsPuppet = true;
        Assert.All(Ask(player, new BuyableTilesQuery(city.Id)).Tiles, it => Assert.False(it.CanBuy));
    }

    // ---- tile info

    [Fact]
    public void TileInfoReportsYieldsOwnerAndWorkStatus()
    {
        var worked = city.GetWorkedTiles().First(it => !it.IsCityCenter());
        worked.SetImprovement("Farm");
        city.LockedTiles.Add(worked.Position);
        city.CityStats.Update();

        var info = AskPure(new TileInfoQuery(worked.Position.X, worked.Position.Y));
        Assert.Equal("Grassland", info.BaseTerrain);
        Assert.Equal("Farm", info.Improvement);
        Assert.Equal(player.CivName, info.Owner);
        Assert.Equal(city.Name, info.OwningCity);
        Assert.Equal(city.Name, info.WorkedBy);
        Assert.True(info.IsLocked);
        Assert.False(info.IsCityCenter);
        var expected = worked.Stats.GetTileStats(city, player);
        Assert.Equal(expected.Food, info.Yields.Single(it => it.Stat == "Food").Value);
        Assert.NotEmpty(info.YieldBreakdown);
        Assert.Null(info.ImprovementOptions);

        var center = AskPure(new TileInfoQuery(0, 0));
        Assert.True(center.IsCityCenter);
    }

    [Fact]
    public void TileInfoListsVisibleUnitsOnly()
    {
        var enemyTile = testGame.GetTile(3, 3);
        testGame.AddUnit("Warrior", enemy, enemyTile);
        Assert.Empty(AskPure(new TileInfoQuery(3, 3)).Units); // nothing of ours sees it
        testGame.AddUnit("Warrior", player, testGame.GetTile(3, 2));
        player.Cache.UpdateViewableTiles();
        var seen = AskPure(new TileInfoQuery(3, 3)).Units;
        Assert.Equal(enemy.CivName, Assert.Single(seen).Civ);

        var tile = testGame.GetTile(2, 0);
        testGame.AddUnit("Warrior", player, tile);
        var info = AskPure(new TileInfoQuery(2, 0));
        Assert.Equal("Warrior", Assert.Single(info.Units).Name);
    }

    [Fact]
    public void TileInfoOfAForeignTileHasNoCityViewData()
    {
        var foreign = testGame.AddCity(enemy, testGame.GetTile(4, 0));
        testGame.AddUnit("Warrior", player, testGame.GetTile(2, 0)); // so the tile is visible
        player.Cache.UpdateViewableTiles();
        var info = AskPure(new TileInfoQuery(4, 0));
        Assert.Equal(enemy.CivName, info.Owner);
        Assert.False(info.IsLocked);
        Assert.True(info.IsCityCenter);
        Assert.Equal(foreign.Name, info.OwningCity);
    }

    [Fact]
    public void TileInfoOfAFoggedTileWithholdsLiveState()
    {
        // T223 review: an explored tile the viewer cannot see shows no owner, city, work, build progress, roads or yields (as SnapshotBuilder.BuildTile).
        var foreign = testGame.AddCity(enemy, testGame.GetTile(4, 0));
        var fogged = testGame.GetTile(4, 1);
        fogged.SetExplored(player, true);
        fogged.RoadStatus = Unciv.Logic.Map.Tiles.RoadStatus.Road;
        player.Cache.UpdateViewableTiles();
        Assert.False(testGame.GameInfo.TileMap[fogged.Position].IsVisible(player));
        var info = AskPure(new TileInfoQuery(4, 1));
        Assert.False(info.IsVisible);
        Assert.Null(info.Owner);
        Assert.Null(info.OwningCity);
        Assert.Null(info.WorkedBy);
        Assert.Null(info.ImprovementInProgress);
        Assert.Equal(0, info.TurnsToImprovement);
        Assert.Equal("None", info.Road);
        Assert.Empty(info.Yields);
        Assert.Empty(info.YieldBreakdown);
        Assert.False(info.IsCityCenter);
        Assert.Null(AskPure(new TileInfoQuery(4, 1, testGame.AddUnit("Worker", player, testGame.GetTile(1, 0)).Id)).ImprovementOptions);
        Assert.NotNull(foreign);
    }

    [Fact]
    public void TileInfoOffersImprovementsWithProblems()
    {
        var tile = testGame.GetTile(1, 0);
        var worker = testGame.AddUnit("Worker", player, tile);
        var info = AskPure(new TileInfoQuery(1, 0, worker.Id));
        var options = info.ImprovementOptions!.ToDictionary(it => it.Name);
        Assert.Empty(options["Farm"].Problems);
        Assert.NotEmpty(options["Farm"].StatDiff);
        Assert.Contains("MissingTech", options["Trading post"].Problems);
        Assert.Empty(options["Trading post"].StatDiff);
        Assert.DoesNotContain("Barbarian encampment", options.Keys);
        Assert.DoesNotContain(info.ImprovementOptions!, it => it.Problems.Contains("Unbuildable"));

        var warrior = testGame.AddUnit("Warrior", player, tile);
        var warriorInfo = AskPure(new TileInfoQuery(1, 0, warrior.Id));
        Assert.Null(warriorInfo.ImprovementOptions);
    }

    [Fact]
    public void TileInfoRejectsUnexploredTilesAndForeignUnits()
    {
        var hidden = testGame.GetTile(5, 0);
        hidden.SetExplored(player, false);
        var response = Execute(player, new TileInfoQuery(5, 0));
        Assert.Equal(UnitQueryCodes.UnknownTile, response.Code);

        var foreignUnit = testGame.AddUnit("Worker", enemy, testGame.GetTile(3, 0));
        Assert.Equal(UnitQueryCodes.UnknownUnit, Execute(player, new TileInfoQuery(1, 0, foreignUnit.Id)).Code);
    }

    [Fact]
    public void TileInfoShowsRiversRoadsAndResourcesOnlyWhenRevealed()
    {
        var tile = testGame.GetTile(1, 1);
        tile.RoadStatus = Unciv.Logic.Map.Tiles.RoadStatus.Road;
        tile.HasBottomRiver = true;
        tile.SetTileResource(testGame.Ruleset.TileResources["Iron"], majorDeposit: true);
        var info = AskPure(new TileInfoQuery(1, 1));
        Assert.Equal("Road", info.Road);
        Assert.True(info.HasRiver);
        Assert.Null(info.Resource); // Iron needs Iron Working
        Assert.Equal(0, info.ResourceAmount);
        player.Tech.AddTechnology("Iron Working");
        var revealed = Ask(player, new TileInfoQuery(1, 1));
        Assert.Equal("Iron", revealed.Resource);
        Assert.True(revealed.ResourceAmount > 0);
    }

    [Fact]
    public void QueriesAreRegistered()
    {
        Assert.Contains("cities.CityPanel", registry.RegisteredTypes);
        Assert.Contains("cities.ConstructionList", registry.RegisteredTypes);
        Assert.Contains("cities.BuyableTiles", registry.RegisteredTypes);
        Assert.Contains("cities.CityTiles", registry.RegisteredTypes);
        Assert.Contains("tiles.TileInfo", registry.RegisteredTypes);
    }
}
