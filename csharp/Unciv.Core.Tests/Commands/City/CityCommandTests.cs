using System.Text.Json;
using Unciv.Commands;
using Unciv.Commands.City;
using Unciv.Core.Tests.Support;
using Unciv.Json;
using Unciv.Logic.City;
using Unciv.Logic.Files;
using Unciv.Models.Ruleset;
using Unciv.Models.StatTypes;
using Unciv.Utils;
using CityObject = Unciv.Logic.City.City;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Core.Tests.Commands.City;

// T191: city commands over a hexagonal test map. a is the current player with a size-4 city, b is a second human.
[Collection("RulesetCache")]
public sealed class CityCommandTests
{
    private readonly TestGame testGame = new();
    private readonly CivilizationObject a;
    private readonly CivilizationObject b;
    private readonly CityObject city;
    private readonly GameSession session;

    public CityCommandTests()
    {
        testGame.MakeHexagonalMap(9);
        a = testGame.AddCiv(["Enables conversion of city production to [Gold]", "Enables conversion of city production to [Science]"], isPlayer: true);
        b = testGame.AddCiv(isPlayer: true);
        testGame.AddUnit("Warrior", b, testGame.GetTile(8, 0));
        city = testGame.AddCity(a, testGame.GetTile(0, 0), initialPopulation: 4);
        testGame.GameInfo.CurrentPlayerCiv = a;
        testGame.GameInfo.CurrentPlayer = a.CivId;
        a.AddGold(1000);
        session = new GameSession(testGame.GameInfo);
    }

    private ulong Hash() => StateHasher.ComputeHash(h => h.Add(UncivFiles.GameInfoToString(testGame.GameInfo, forceZip: false)));

    private CommandResult Ok(GameCommand command, string? actor = null)
    {
        var result = session.Submit(actor ?? a.CivId, command);
        Assert.True(result.Accepted, result.Validation.Message);
        return result;
    }

    private void AssertRejected(GameCommand command, string code, string? actor = null)
    {
        ulong before = Hash();
        int logged = session.Log.Count;
        var result = session.Submit(actor ?? a.CivId, command);
        Assert.False(result.Accepted);
        Assert.Equal(code, result.Validation.Code);
        Assert.Equal(before, Hash());
        Assert.Equal(logged, session.Log.Count);
    }

    private IReadOnlyList<string> Queue => city.CityConstructions.ConstructionQueue;

    private void ClearQueue()
    {
        city.CityConstructions.RemoveAll();
    }

    private TileClass FreeWorkableTile() => city.GetWorkableTiles().First(it => !it.IsCityCenter() && !city.IsWorked(it));

    private TileClass WorkedTile() => city.GetWorkedTiles().First(it => !it.IsCityCenter());

    // --- registration

    [Fact]
    public void CommandsAreRegisteredAndRoundTripThroughJson()
    {
        var names = GameCommandRegistry.Scan().ToDictionary(it => it.Name, it => it.CommandClass);
        Assert.Equal(typeof(QueueConstruction), names["queue-construction"]);
        Assert.Equal(typeof(SetRazing), names["set-razing"]);
        Assert.Equal(typeof(SetCivConstructionDisabled), names["set-civ-construction-disabled"]);

        string wire = JsonSerializer.Serialize(new ReorderQueue { CityId = "c", Index = 2, Move = QueueMove.End }, UncivJson.DefaultOptions);
        Assert.Contains("\"End\"", wire);
        Assert.Equal(QueueMove.End, JsonSerializer.Deserialize<ReorderQueue>(wire, UncivJson.DefaultOptions)!.Move);

        string buy = JsonSerializer.Serialize(new PurchaseConstruction { CityId = "c", Name = "Monument", Stat = Stat.Faith }, UncivJson.DefaultOptions);
        Assert.Equal(Stat.Faith, JsonSerializer.Deserialize<PurchaseConstruction>(buy, UncivJson.DefaultOptions)!.Stat);
        var queue = JsonSerializer.Deserialize<QueueConstruction>("""{"cityId":"c","name":"Wonder","tile":{"x":1,"y":-1}}""", UncivJson.DefaultOptions)!;
        Assert.Equal(new Unciv.Logic.Map.HexCoord(1, -1), queue.Tile);
    }

    [Fact]
    public void EveryCityCommandNeedsTheActiveActorAndAnOwnCity()
    {
        var commands = new GameCommand[]
        {
            new QueueConstruction { CityId = city.Id, Name = "Warrior" },
            new RemoveFromQueue { CityId = city.Id },
            new ReorderQueue { CityId = city.Id },
            new PurchaseConstruction { CityId = city.Id, Name = "Monument" },
            new SetCityFocus { CityId = city.Id, Focus = CityFocus.GoldFocus },
            new ToggleAvoidGrowth { CityId = city.Id },
            new ReassignPopulation { CityId = city.Id },
            new SetManualSpecialists { CityId = city.Id, Manual = true },
        };
        foreach (var command in commands)
        {
            AssertRejected(command, CommandErrors.NotYourTurn, b.CivId);
            AssertRejected(command, CommandErrors.UnknownCiv, "Atlantis");
        }
        AssertRejected(new ToggleAvoidGrowth { CityId = "nope" }, CommandErrors.UnknownCity);
    }

    // --- production queue

    [Fact]
    public void QueueConstructionAppendsOrInsertsOnTopAndRejectsWhatTheUiHides()
    {
        ClearQueue();
        Ok(new QueueConstruction { CityId = city.Id, Name = "Monument" });
        Ok(new QueueConstruction { CityId = city.Id, Name = "Warrior" });
        Ok(new QueueConstruction { CityId = city.Id, Name = "Warrior", AddToTop = true });
        Assert.Equal(["Warrior", "Monument", "Warrior"], Queue);

        AssertRejected(new QueueConstruction { CityId = city.Id, Name = "Monument" }, CommandErrors.NotAllowed); // building twice
        AssertRejected(new QueueConstruction { CityId = city.Id, Name = "Library" }, CommandErrors.NotAllowed); // not buildable yet
        AssertRejected(new QueueConstruction { CityId = city.Id, Name = "Nothing at all" }, CommandErrors.InvalidTarget);
        AssertRejected(new QueueConstruction { CityId = city.Id, Name = "" }, CommandErrors.InvalidTarget);
        AssertRejected(new QueueConstruction { CityId = city.Id, Name = "Warrior", Tile = new Unciv.Logic.Map.HexCoord(1, 0) }, CommandErrors.InvalidTarget);
    }

    [Fact]
    public void QueueConstructionPerpetualRulesAndQueueFull()
    {
        ClearQueue();
        Ok(new QueueConstruction { CityId = city.Id, Name = "Warrior" });
        Ok(new QueueConstruction { CityId = city.Id, Name = PerpetualConstruction.Gold.Name });
        AssertRejected(new QueueConstruction { CityId = city.Id, Name = PerpetualConstruction.Gold.Name }, CommandErrors.NotAllowed);
        AssertRejected(new QueueConstruction { CityId = city.Id, Name = PerpetualConstruction.Science.Name, AddToTop = true }, CommandErrors.NotAllowed);
        Ok(new QueueConstruction { CityId = city.Id, Name = "Warrior" }); // goes before the perpetual entry
        Assert.Equal(["Warrior", "Warrior", PerpetualConstruction.Gold.Name], Queue);

        while (!city.CityConstructions.IsQueueFull()) city.CityConstructions.AddToQueue("Warrior");
        AssertRejected(new QueueConstruction { CityId = city.Id, Name = "Warrior" }, CommandErrors.NotAllowed);
    }

    [Fact]
    public void QueueConstructionOnTheFirstSlotReassignsPopulation()
    {
        ClearQueue();
        city.ShouldReassignPopulation = true;
        Ok(new QueueConstruction { CityId = city.Id, Name = "Warrior" });
        Assert.False(city.ShouldReassignPopulation); // ReassignPopulation ran
        city.ShouldReassignPopulation = true;
        Ok(new QueueConstruction { CityId = city.Id, Name = "Monument" });
        Assert.True(city.ShouldReassignPopulation); // not first: untouched
    }

    [Fact]
    public void PuppetCitiesRejectProductionCommands()
    {
        city.IsPuppet = true;
        AssertRejected(new QueueConstruction { CityId = city.Id, Name = "Warrior" }, CommandErrors.NotAllowed);
        AssertRejected(new RemoveFromQueue { CityId = city.Id, Index = 0 }, CommandErrors.NotAllowed);
        AssertRejected(new ReorderQueue { CityId = city.Id, Index = 0, Move = QueueMove.Down }, CommandErrors.NotAllowed);
        AssertRejected(new SetCityFocus { CityId = city.Id, Focus = CityFocus.GoldFocus }, CommandErrors.NotAllowed);
        AssertRejected(new WorkTile { CityId = city.Id, Tile = FreeWorkableTile().Position }, CommandErrors.NotAllowed);
        AssertRejected(new SetConstructionDisabled { CityId = city.Id, Name = "Warrior", Disabled = true }, CommandErrors.NotAllowed);
        AssertRejected(new SellBuilding { CityId = city.Id, Building = "Palace" }, CommandErrors.NotAllowed);
    }

    [Fact]
    public void RemoveFromQueueIsPositionalAndLeavesIdleWhenEmptied()
    {
        ClearQueue();
        Ok(new QueueConstruction { CityId = city.Id, Name = "Monument" });
        Ok(new QueueConstruction { CityId = city.Id, Name = "Warrior" });

        AssertRejected(new RemoveFromQueue { CityId = city.Id, Index = 2 }, CommandErrors.InvalidTarget);
        AssertRejected(new RemoveFromQueue { CityId = city.Id, Index = -1 }, CommandErrors.InvalidTarget);
        Ok(new RemoveFromQueue { CityId = city.Id, Index = 0 });
        Assert.Equal(["Warrior"], Queue);
        Ok(new RemoveFromQueue { CityId = city.Id, Index = 0 });
        Assert.Equal([PerpetualConstruction.Idle.Name], Queue); // automatic = false: no construction automation
    }

    [Fact]
    public void ReorderQueueMovesEntriesAndRejectsPositionsTheUiDoesNotOffer()
    {
        ClearQueue();
        foreach (var name in new[] { "Monument", "Warrior", "Worker" }) Ok(new QueueConstruction { CityId = city.Id, Name = name });
        Ok(new QueueConstruction { CityId = city.Id, Name = PerpetualConstruction.Gold.Name });

        Ok(new ReorderQueue { CityId = city.Id, Index = 2, Move = QueueMove.Top });
        Assert.Equal(["Worker", "Monument", "Warrior", PerpetualConstruction.Gold.Name], Queue);
        Ok(new ReorderQueue { CityId = city.Id, Index = 0, Move = QueueMove.End });
        Assert.Equal(["Monument", "Warrior", "Worker", PerpetualConstruction.Gold.Name], Queue);
        Ok(new ReorderQueue { CityId = city.Id, Index = 1, Move = QueueMove.Up });
        Assert.Equal(["Warrior", "Monument", "Worker", PerpetualConstruction.Gold.Name], Queue);
        Ok(new ReorderQueue { CityId = city.Id, Index = 1, Move = QueueMove.Down });
        Assert.Equal(["Warrior", "Worker", "Monument", PerpetualConstruction.Gold.Name], Queue);

        AssertRejected(new ReorderQueue { CityId = city.Id, Index = 0, Move = QueueMove.Top }, CommandErrors.NotAllowed);
        AssertRejected(new ReorderQueue { CityId = city.Id, Index = 0, Move = QueueMove.Up }, CommandErrors.NotAllowed);
        AssertRejected(new ReorderQueue { CityId = city.Id, Index = 3, Move = QueueMove.Down }, CommandErrors.NotAllowed);
        AssertRejected(new ReorderQueue { CityId = city.Id, Index = 2, Move = QueueMove.End }, CommandErrors.NotAllowed); // last real entry
        AssertRejected(new ReorderQueue { CityId = city.Id, Index = 3, Move = QueueMove.Top }, CommandErrors.NotAllowed); // perpetual
        AssertRejected(new ReorderQueue { CityId = city.Id, Index = 9, Move = QueueMove.Up }, CommandErrors.InvalidTarget);
        AssertRejected(new ReorderQueue { CityId = city.Id, Index = 0, Move = (QueueMove)99 }, CommandErrors.NotAllowed);
    }

    [Fact]
    public void ABuildingThatCreatesAnImprovementNeedsAValidOwnTile()
    {
        var wonder = testGame.CreateBuilding("Creates a [Citadel] improvement on a specific tile");
        wonder.Cost = 100;
        var target = city.GetWorkableTiles().First(it => !it.IsCityCenter() && !it.IsWater && it.ImprovementFunctions.CanBuildImprovement(
            city.GetRuleset().TileImprovements["Citadel"], city.State));
        ClearQueue();

        AssertRejected(new QueueConstruction { CityId = city.Id, Name = wonder.Name }, CommandErrors.InvalidTarget); // tile missing
        AssertRejected(new QueueConstruction { CityId = city.Id, Name = wonder.Name, Tile = city.GetCenterTile().Position }, CommandErrors.InvalidTarget);
        AssertRejected(new QueueConstruction { CityId = city.Id, Name = wonder.Name, Tile = target.Position, AddToTop = true }, CommandErrors.NotAllowed);
        AssertRejected(new QueueConstruction { CityId = city.Id, Name = wonder.Name, Tile = new Unciv.Logic.Map.HexCoord(99, 99) }, CommandErrors.UnknownTile);

        Ok(new QueueConstruction { CityId = city.Id, Name = wonder.Name, Tile = target.Position });
        Assert.Same(target, city.CityConstructions.GetTileForImprovement("Citadel"));
        AssertRejected(new QueueConstruction { CityId = city.Id, Name = wonder.Name }, CommandErrors.NotAllowed); // building twice

        Ok(new RemoveFromQueue { CityId = city.Id, Index = 0 });
        Assert.Null(city.CityConstructions.GetTileForImprovement("Citadel"));

        AssertRejected(new PurchaseConstruction { CityId = city.Id, Name = wonder.Name }, CommandErrors.InvalidTarget);
        Ok(new PurchaseConstruction { CityId = city.Id, Name = wonder.Name, Tile = target.Position });
        Assert.True(city.CityConstructions.IsBuilt(wonder.Name));
        Assert.Equal("Citadel", target.Improvement);
    }

    [Fact]
    public void PurchaseFromTheQueueKeepsTheMarkedTile()
    {
        var wonder = testGame.CreateBuilding("Creates a [Citadel] improvement on a specific tile");
        wonder.Cost = 100;
        var targets = city.GetWorkableTiles().Where(it => !it.IsCityCenter() && !it.IsWater && it.ImprovementFunctions.CanBuildImprovement(
            city.GetRuleset().TileImprovements["Citadel"], city.State)).Take(2).ToList();
        ClearQueue();
        Ok(new QueueConstruction { CityId = city.Id, Name = wonder.Name, Tile = targets[1].Position });

        Ok(new PurchaseConstruction { CityId = city.Id, Name = wonder.Name, QueueIndex = 0 });

        Assert.Equal("Citadel", targets[1].Improvement);
        Assert.Null(targets[0].Improvement);
    }

    [Fact]
    public void RemoveAllByNameRemovesEveryEntryAndChecksTheCity()
    {
        ClearQueue();
        foreach (var name in new[] { "Warrior", "Monument", "Warrior" }) Ok(new QueueConstruction { CityId = city.Id, Name = name });
        AssertRejected(new RemoveAllByName { CityId = city.Id, Name = "Worker" }, CommandErrors.InvalidTarget);
        Ok(new RemoveAllByName { CityId = city.Id, Name = "Warrior" });
        Assert.Equal(["Monument"], Queue);
        city.IsBeingRazed = true;
        AssertRejected(new RemoveAllByName { CityId = city.Id, Name = "Monument" }, CommandErrors.NotAllowed);
    }

    [Fact]
    public void WorkTileRefusesTilesAnotherCityWorksOrBlockadedOnes()
    {
        var other = testGame.AddCity(a, testGame.GetTile(3, 0), initialPopulation: 2);
        testGame.AddTileToCity(other, testGame.GetTile(2, 0));
        testGame.GetTile(2, 0).SetOwningCity(other);
        city.ReassignPopulation(resetLocked: true);
        other.ReassignPopulation(resetLocked: true);
        var contested = city.GetWorkableTiles().Concat(other.GetWorkableTiles()).First(it => !it.IsCityCenter() && it.IsWorked());
        var workedBy = contested.GetWorkingCity()!;
        var notWorker = ReferenceEquals(workedBy, city) ? other : city;
        if (notWorker.GetWorkableTiles().Contains(contested))
            AssertRejected(new WorkTile { CityId = notWorker.Id, Tile = contested.Position }, CommandErrors.InvalidTarget);
        AssertRejected(new WorkTile { CityId = city.Id, Tile = city.GetCenterTile().Position }, CommandErrors.InvalidTarget);
    }

    [Fact]
    public void WorkTileRefusesTilesThatYieldNothing()
    {
        city.ReassignPopulation(resetLocked: true);
        Ok(new StopWorkingTile { CityId = city.Id, Tile = WorkedTile().Position });
        var tile = FreeWorkableTile();
        testGame.SetTileTerrain(tile.Position, "Mountain");
        city.CityStats.Update();
        AssertRejected(new WorkTile { CityId = city.Id, Tile = tile.Position }, CommandErrors.InvalidTarget);
    }

    [Fact]
    public void TileCommandsRejectUnknownAndForeignTiles()
    {
        var far = testGame.GetTile(8, 0).Position;
        AssertRejected(new UnlockTile { CityId = city.Id, Tile = new Unciv.Logic.Map.HexCoord(99, 99) }, CommandErrors.UnknownTile);
        AssertRejected(new StopWorkingTile { CityId = city.Id, Tile = far }, CommandErrors.InvalidTarget);
        AssertRejected(new LockTile { CityId = city.Id, Tile = far }, CommandErrors.InvalidTarget);
        AssertRejected(new UnassignSpecialist { CityId = city.Id, Specialist = "Mystic" }, CommandErrors.InvalidTarget);
        city.IsPuppet = true;
        city.UnitShouldUseSavedPromotion["Warrior"] = true;
        Ok(new SetUnitSavedPromotion { CityId = city.Id, Unit = "Warrior", Value = false }); // offered on puppets
        AssertRejected(new PurchaseConstruction { CityId = city.Id, Name = "Worker" }, CommandErrors.NotAllowed); // puppets may not buy
    }

    // --- purchase

    [Fact]
    public void PurchaseConstructionPaysTheComputedPriceAndCompletesTheBuilding()
    {
        ClearQueue();
        Ok(new QueueConstruction { CityId = city.Id, Name = "Monument" });
        int cost = city.GetRuleset().Buildings["Monument"].GetStatBuyCost(city, Stat.Gold)!.Value;
        int before = a.Gold;

        Ok(new PurchaseConstruction { CityId = city.Id, Name = "Monument", QueueIndex = 0 });

        Assert.Equal(before - cost, a.Gold);
        Assert.True(city.CityConstructions.IsBuilt("Monument"));
        Assert.DoesNotContain("Monument", Queue);
    }

    [Fact]
    public void PurchaseConstructionWithoutQueueEntryLeavesTheQueue()
    {
        ClearQueue();
        Ok(new QueueConstruction { CityId = city.Id, Name = "Warrior" });
        Ok(new PurchaseConstruction { CityId = city.Id, Name = "Monument" });
        Assert.True(city.CityConstructions.IsBuilt("Monument"));
        Assert.Equal(["Warrior"], Queue);
    }

    [Fact]
    public void PurchaseConstructionRejections()
    {
        ClearQueue();
        Ok(new QueueConstruction { CityId = city.Id, Name = "Warrior" });
        AssertRejected(new PurchaseConstruction { CityId = city.Id, Name = "Nothing at all" }, CommandErrors.InvalidTarget);
        AssertRejected(new PurchaseConstruction { CityId = city.Id, Name = PerpetualConstruction.Gold.Name }, CommandErrors.NotAllowed);
        AssertRejected(new PurchaseConstruction { CityId = city.Id, Name = "Monument", Stat = Stat.Food }, CommandErrors.NotAllowed);
        AssertRejected(new PurchaseConstruction { CityId = city.Id, Name = "Monument", QueueIndex = 0 }, CommandErrors.StaleReference); // entry 0 is the Warrior
        AssertRejected(new PurchaseConstruction { CityId = city.Id, Name = "Monument", QueueIndex = 5 }, CommandErrors.StaleReference);
        AssertRejected(new PurchaseConstruction { CityId = city.Id, Name = "Monument", QueueIndex = -2 }, CommandErrors.StaleReference);
        AssertRejected(new PurchaseConstruction { CityId = city.Id, Name = "Monument", Tile = new Unciv.Logic.Map.HexCoord(1, 0) }, CommandErrors.InvalidTarget);
        AssertRejected(new PurchaseConstruction { CityId = city.Id, Name = "Library" }, CommandErrors.NotAllowed);

        a.AddGold(-a.Gold);
        AssertRejected(new PurchaseConstruction { CityId = city.Id, Name = "Monument" }, CommandErrors.NotEnough("gold"));
    }

    [Fact]
    public void PurchaseConstructionOfAUnitIsBlockedByAUnitOnTheCityCenter()
    {
        testGame.AddUnit("Warrior", a, city.GetCenterTile());
        AssertRejected(new PurchaseConstruction { CityId = city.Id, Name = "Warrior" }, CommandErrors.PurchaseBlockedByUnit);
        Ok(new PurchaseConstruction { CityId = city.Id, Name = "Worker" }); // a civilian fits next to the warrior
        Assert.NotNull(city.GetCenterTile().CivilianUnit);
    }

    [Fact]
    public void PurchaseConstructionIsFreeInGodMode()
    {
        testGame.GameInfo.GameParameters.GodMode = true;
        a.AddGold(-a.Gold);
        Ok(new PurchaseConstruction { CityId = city.Id, Name = "Monument" });
        Assert.Equal(0, a.Gold);
        Assert.True(city.CityConstructions.IsBuilt("Monument"));
    }

    // --- tiles

    [Fact]
    public void BuyTileTakesTheTileAndTheGold()
    {
        var tile = testGame.TileMap.TileList.First(it => city.Expansion.CanBuyTile(it));
        int cost = city.Expansion.GetGoldCostOfTile(tile);
        int before = a.Gold;

        Ok(new BuyTile { CityId = city.Id, Tile = tile.Position });

        Assert.Equal(before - cost, a.Gold);
        Assert.Same(city, tile.GetCity());
    }

    [Fact]
    public void BuyTileRejections()
    {
        var tile = testGame.TileMap.TileList.First(it => city.Expansion.CanBuyTile(it));
        AssertRejected(new BuyTile { CityId = city.Id, Tile = city.GetCenterTile().Position }, CommandErrors.InvalidTarget); // owned
        AssertRejected(new BuyTile { CityId = city.Id, Tile = testGame.GetTile(8, 0).Position }, CommandErrors.InvalidTarget); // out of range
        AssertRejected(new BuyTile { CityId = city.Id, Tile = new Unciv.Logic.Map.HexCoord(99, 99) }, CommandErrors.UnknownTile);
        city.IsBeingRazed = true;
        AssertRejected(new BuyTile { CityId = city.Id, Tile = tile.Position }, CommandErrors.InvalidTarget);
        city.IsBeingRazed = false;
        a.AddGold(-a.Gold);
        AssertRejected(new BuyTile { CityId = city.Id, Tile = tile.Position }, CommandErrors.NotEnough("gold"));
    }

    [Fact]
    public void WorkStopLockAndUnlockTile()
    {
        city.ReassignPopulation(resetLocked: true);
        var worked = WorkedTile();
        Ok(new StopWorkingTile { CityId = city.Id, Tile = worked.Position });
        Assert.False(city.IsWorked(worked));
        Assert.True(city.Population.GetFreePopulation() > 0);

        Ok(new WorkTile { CityId = city.Id, Tile = worked.Position });
        Assert.True(city.IsWorked(worked));

        Ok(new LockTile { CityId = city.Id, Tile = worked.Position });
        Assert.Contains(worked.Position, city.LockedTiles);
        AssertRejected(new LockTile { CityId = city.Id, Tile = worked.Position }, CommandErrors.NotAllowed);
        Ok(new UnlockTile { CityId = city.Id, Tile = worked.Position });
        Assert.Empty(city.LockedTiles);
        AssertRejected(new UnlockTile { CityId = city.Id, Tile = worked.Position }, CommandErrors.InvalidTarget);

        Ok(new LockTile { CityId = city.Id, Tile = worked.Position });
        Ok(new StopWorkingTile { CityId = city.Id, Tile = worked.Position }); // also unlocks
        Assert.Empty(city.LockedTiles);
    }

    [Fact]
    public void WorkTileRejections()
    {
        city.ReassignPopulation(resetLocked: true);
        var worked = WorkedTile();
        var free = FreeWorkableTile();
        AssertRejected(new WorkTile { CityId = city.Id, Tile = worked.Position }, CommandErrors.InvalidTarget); // already worked
        AssertRejected(new WorkTile { CityId = city.Id, Tile = city.GetCenterTile().Position }, CommandErrors.InvalidTarget);
        AssertRejected(new WorkTile { CityId = city.Id, Tile = testGame.GetTile(8, 0).Position }, CommandErrors.InvalidTarget);
        AssertRejected(new WorkTile { CityId = city.Id, Tile = new Unciv.Logic.Map.HexCoord(99, 99) }, CommandErrors.UnknownTile);
        AssertRejected(new StopWorkingTile { CityId = city.Id, Tile = free.Position }, CommandErrors.InvalidTarget);
        AssertRejected(new LockTile { CityId = city.Id, Tile = free.Position }, CommandErrors.InvalidTarget);
        if (city.Population.GetFreePopulation() == 0)
            AssertRejected(new WorkTile { CityId = city.Id, Tile = free.Position }, CommandErrors.NotAllowed);
    }

    // --- citizens

    [Fact]
    public void SpecialistsAreAssignedWithinTheirSlotsAndFreeCitizens()
    {
        city.CityConstructions.AddBuilding("Market"); // one Merchant slot
        city.ManualSpecialists = true;
        city.ReassignPopulation(resetLocked: true);
        city.Population.SpecialistAllocations.Clear();
        Ok(new StopWorkingTile { CityId = city.Id, Tile = WorkedTile().Position });

        AssertRejected(new AssignSpecialist { CityId = city.Id, Specialist = "Scientist" }, CommandErrors.NotAllowed); // no slot
        AssertRejected(new AssignSpecialist { CityId = city.Id, Specialist = "Mystic" }, CommandErrors.InvalidTarget);
        AssertRejected(new UnassignSpecialist { CityId = city.Id, Specialist = "Merchant" }, CommandErrors.NotAllowed);

        Ok(new AssignSpecialist { CityId = city.Id, Specialist = "Merchant" });
        Assert.Equal(1, city.Population.GetNewSpecialists()["Merchant"]);
        Assert.True(city.ManualSpecialists);
        AssertRejected(new AssignSpecialist { CityId = city.Id, Specialist = "Merchant" }, CommandErrors.NotAllowed); // slots full

        Ok(new UnassignSpecialist { CityId = city.Id, Specialist = "Merchant" });
        Assert.Equal(0, city.Population.GetNewSpecialists()["Merchant"]);
    }

    [Fact]
    public void AssigningNeedsAFreeCitizen()
    {
        city.CityConstructions.AddBuilding("Market");
        city.ManualSpecialists = true;
        city.ReassignPopulation(resetLocked: true);
        city.Population.SpecialistAllocations.Clear();
        foreach (var tile in city.GetWorkableTiles().Where(it => !it.IsCityCenter() && !city.IsWorked(it)).Take(city.Population.GetFreePopulation()))
            city.WorkTile(tile);
        Assert.Equal(0, city.Population.GetFreePopulation());
        AssertRejected(new AssignSpecialist { CityId = city.Id, Specialist = "Merchant" }, CommandErrors.NotAllowed);
    }

    [Fact]
    public void ManualSpecialistsToggleAndAutoModeReassigns()
    {
        AssertRejected(new SetManualSpecialists { CityId = city.Id, Manual = false }, CommandErrors.NotAllowed);
        Ok(new SetManualSpecialists { CityId = city.Id, Manual = true });
        Assert.True(city.ManualSpecialists);
        AssertRejected(new SetManualSpecialists { CityId = city.Id, Manual = true }, CommandErrors.NotAllowed);
        city.ShouldReassignPopulation = true;
        Ok(new SetManualSpecialists { CityId = city.Id, Manual = false });
        Assert.False(city.ManualSpecialists);
        Assert.False(city.ShouldReassignPopulation);
    }

    [Fact]
    public void FocusReassignAndAvoidGrowth()
    {
        Ok(new SetCityFocus { CityId = city.Id, Focus = CityFocus.GoldFocus });
        Assert.Equal(CityFocus.GoldFocus, city.GetCityFocus());
        Ok(new SetCityFocus { CityId = city.Id, Focus = CityFocus.GoldFocus }); // re-applying is offered
        Ok(new SetCityFocus { CityId = city.Id, Focus = CityFocus.HappinessFocus }); // the stat icons of CityStatsTable offer it
        AssertRejected(new SetCityFocus { CityId = city.Id, Focus = (CityFocus)99 }, CommandErrors.NotAllowed);
        Assert.Equal(testGame.GameInfo.IsReligionEnabled(),
            new SetCityFocus { CityId = city.Id, Focus = CityFocus.FaithFocus }.Validate(testGame.GameInfo, a.CivId).IsValid);

        bool avoid = city.AvoidGrowth;
        Ok(new ToggleAvoidGrowth { CityId = city.Id });
        Assert.Equal(!avoid, city.AvoidGrowth);
        Ok(new ToggleAvoidGrowth { CityId = city.Id });
        Assert.Equal(avoid, city.AvoidGrowth);

        var locked = WorkedTile();
        city.LockTile(locked);
        Ok(new ReassignPopulation { CityId = city.Id });
        Assert.Contains(locked.Position, city.LockedTiles);
        Ok(new ReassignPopulation { CityId = city.Id, ResetLocked = true });
        Assert.Empty(city.LockedTiles);
    }

    // --- buildings

    [Fact]
    public void SellBuildingPaysATenthOnceThenRefusesUntilNextTurn()
    {
        city.CityConstructions.AddBuilding("Monument");
        city.CityConstructions.AddBuilding("Temple");
        int expected = city.GetGoldForSellingBuilding("Monument");
        int before = a.Gold;

        Ok(new SellBuilding { CityId = city.Id, Building = "Monument" });

        Assert.Equal(before + expected, a.Gold);
        Assert.False(city.CityConstructions.IsBuilt("Monument"));
        AssertRejected(new SellBuilding { CityId = city.Id, Building = "Temple" }, CommandErrors.NotAllowed);
        testGame.GameInfo.GameParameters.GodMode = true;
        Ok(new SellBuilding { CityId = city.Id, Building = "Temple" });
    }

    [Fact]
    public void SellBuildingRejections()
    {
        AssertRejected(new SellBuilding { CityId = city.Id, Building = "Monument" }, CommandErrors.InvalidTarget); // not built
        AssertRejected(new SellBuilding { CityId = city.Id, Building = "Nothing at all" }, CommandErrors.InvalidTarget);
        city.CityConstructions.AddBuilding(city.GetRuleset().Buildings.Values.First(it => it.IsAnyWonder()));
        var wonder = city.CityConstructions.GetBuiltBuildings().First(it => it.IsAnyWonder());
        AssertRejected(new SellBuilding { CityId = city.Id, Building = wonder.Name }, CommandErrors.NotAllowed);
    }

    // --- conquest

    [Fact]
    public void AnnexCityEndsPuppetStatus()
    {
        AssertRejected(new AnnexCity { CityId = city.Id }, CommandErrors.NotAllowed); // not a puppet
        city.IsPuppet = true;
        Ok(new AnnexCity { CityId = city.Id });
        Assert.False(city.IsPuppet);
        Assert.Equal(CityFocus.NoFocus, city.GetCityFocus());
    }

    [Fact]
    public void AnnexCityIsRefusedToCivsThatMayNotAnnex()
    {
        var civ = testGame.AddCiv(["May not annex cities"], isPlayer: true);
        var tile = testGame.GetTile(-6, 0);
        var puppet = testGame.AddCity(civ, tile);
        puppet.IsPuppet = true;
        testGame.GameInfo.CurrentPlayerCiv = civ;
        testGame.GameInfo.CurrentPlayer = civ.CivId;
        AssertRejected(new AnnexCity { CityId = puppet.Id }, CommandErrors.NotAllowed, civ.CivId);
        AssertRejected(new SetRazing { CityId = puppet.Id, Raze = true }, CommandErrors.NotAllowed, civ.CivId);
    }

    [Fact]
    public void SetRazingStartsStopsAndRespectsProtectedCities()
    {
        AssertRejected(new SetRazing { CityId = city.Id, Raze = true }, CommandErrors.NotAllowed); // capital
        AssertRejected(new SetRazing { CityId = city.Id, Raze = false }, CommandErrors.NotAllowed); // not razing
        var second = testGame.AddCity(a, testGame.GetTile(-6, 0));
        Ok(new SetRazing { CityId = second.Id, Raze = true });
        Assert.True(second.IsBeingRazed);
        AssertRejected(new SetRazing { CityId = second.Id, Raze = true }, CommandErrors.NotAllowed);
        Ok(new SetRazing { CityId = second.Id, Raze = false });
        Assert.False(second.IsBeingRazed);

        second.IsPuppet = true;
        AssertRejected(new SetRazing { CityId = second.Id, Raze = true }, CommandErrors.NotAllowed); // the UI offers annexing instead
        testGame.GameInfo.GameParameters.NoCityRazing = true;
        second.IsPuppet = false;
        AssertRejected(new SetRazing { CityId = second.Id, Raze = true }, CommandErrors.NotAllowed);
    }

    // --- flags

    [Fact]
    public void SavedPromotionAndDisabledConstructions()
    {
        AssertRejected(new SetUnitSavedPromotion { CityId = city.Id, Unit = "Warrior", Value = true }, CommandErrors.InvalidTarget); // no entry
        city.UnitShouldUseSavedPromotion["Warrior"] = true;
        Ok(new SetUnitSavedPromotion { CityId = city.Id, Unit = "Warrior", Value = false });
        Assert.False(city.UnitShouldUseSavedPromotion["Warrior"]);

        Ok(new SetConstructionDisabled { CityId = city.Id, Name = "Warrior", Disabled = true });
        Assert.Contains("Warrior", city.DisabledConstructions);
        AssertRejected(new SetConstructionDisabled { CityId = city.Id, Name = "Warrior", Disabled = true }, CommandErrors.NotAllowed);
        AssertRejected(new SetConstructionDisabled { CityId = city.Id, Name = PerpetualConstruction.Idle.Name, Disabled = true }, CommandErrors.NotAllowed);
        AssertRejected(new SetConstructionDisabled { CityId = city.Id, Name = "Nothing at all", Disabled = true }, CommandErrors.InvalidTarget);
        Ok(new SetConstructionDisabled { CityId = city.Id, Name = "Warrior", Disabled = false });
        AssertRejected(new SetConstructionDisabled { CityId = city.Id, Name = "Warrior", Disabled = false }, CommandErrors.NotAllowed);
        Assert.DoesNotContain("Warrior", city.DisabledConstructions);
    }

    [Fact]
    public void CivWideDisableReachesEveryCityAndIsUndone()
    {
        var other = testGame.AddCity(a, testGame.GetTile(-6, 0));
        Ok(new SetCivConstructionDisabled { CityId = city.Id, Name = "Monument", Disabled = true });
        Assert.Contains("Monument", city.DisabledConstructions);
        Assert.Contains("Monument", other.DisabledConstructions);
        Assert.Contains("Monument", a.DisabledCityConstructions);
        AssertRejected(new SetCivConstructionDisabled { CityId = city.Id, Name = "Monument", Disabled = true }, CommandErrors.NotAllowed);
        AssertRejected(new SetCivConstructionDisabled { CityId = city.Id, Name = PerpetualConstruction.Idle.Name, Disabled = true }, CommandErrors.NotAllowed);
        AssertRejected(new SetCivConstructionDisabled { CityId = city.Id, Name = "Nothing at all", Disabled = true }, CommandErrors.InvalidTarget);

        Ok(new SetCivConstructionDisabled { CityId = city.Id, Name = "Monument", Disabled = false });
        Assert.DoesNotContain("Monument", other.DisabledConstructions);
        Assert.DoesNotContain("Monument", a.DisabledCityConstructions);
        AssertRejected(new SetCivConstructionDisabled { CityId = city.Id, Name = "Monument", Disabled = false }, CommandErrors.NotAllowed);
    }
}
