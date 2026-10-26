using Unciv.Assets;
using Unciv.Commands.City;
using Unciv.Commands.Turn;
using Unciv.Commands.Units;
using Unciv.Logic.Map;
using Unciv.Net.Visibility;

namespace Unciv.Core.Tests.Assets;

/// <summary>C054: which tutorial tasks the viewer's snapshot and commands prove done.</summary>
public sealed class TutorialTaskModelTests
{
    private static ClientSnapshot Snapshot(int turn = 1, string? tech = null, string cityOwner = "", bool ownRecord = true) => new()
    {
        Seq = 0,
        Turn = turn,
        Phase = "Playing",
        TurnStatus = new TurnStatusDto { Ended = [], Phase = "Playing" },
        You = "Me",
        Civs = [new CivDto { CivId = "Me", Name = "Me", IsCityState = false, IsMajor = true, IsBarbarian = false, IsDefeated = false, Own = ownRecord ? new OwnCivDto { Gold = 0, Happiness = 0, CurrentTech = tech, Techs = [], Policies = [], Spies = [] } : null }],
        Tiles = [],
        Cities = cityOwner == "" ? [] : [new CityDto { Name = "C", Owner = cityOwner, X = 0, Y = 0, Population = 1, IsCapital = true, IsPuppet = false, IsBeingRazed = false, Health = 1, MaxHealth = 1, VisibleBuildings = [] }],
        Units = [],
        Diplomacy = [],
        PendingTrades = [],
        Notifications = [],
    };

    [Fact]
    public void NothingIsDoneAtTheStart() => Assert.Empty(TutorialTaskModel.FromSnapshot(Snapshot()));

    [Fact]
    public void AnOwnCityMeansFoundCityButAForeignOneDoesNot()
    {
        Assert.Equal([TutorialTaskModel.FoundCity], TutorialTaskModel.FromSnapshot(Snapshot(cityOwner: "Me")));
        Assert.Empty(TutorialTaskModel.FromSnapshot(Snapshot(cityOwner: "Rome")));
    }

    [Fact]
    public void AResearchTargetMeansPickTechnology()
    {
        Assert.Equal([TutorialTaskModel.PickTechnology], TutorialTaskModel.FromSnapshot(Snapshot(tech: "Pottery")));
        Assert.Empty(TutorialTaskModel.FromSnapshot(Snapshot(ownRecord: false)));
    }

    [Fact]
    public void TurnTwoMeansPassATurn()
    {
        Assert.Empty(TutorialTaskModel.FromSnapshot(Snapshot(turn: 1)));
        Assert.Equal([TutorialTaskModel.PassATurn], TutorialTaskModel.FromSnapshot(Snapshot(turn: 2)));
    }

    [Fact]
    public void CommandsMapToTheirTasks()
    {
        Assert.Equal(TutorialTaskModel.MoveUnitTask, TutorialTaskModel.FromCommand(new MoveUnit { UnitId = 1, To = new HexCoord(0, 0) }));
        Assert.Equal(TutorialTaskModel.PickConstruction, TutorialTaskModel.FromCommand(new QueueConstruction { CityId = "c", Name = "Warrior" }));
        Assert.Equal(TutorialTaskModel.PickConstruction, TutorialTaskModel.FromCommand(new PurchaseConstruction { CityId = "c", Name = "Warrior" }));
        Assert.Equal(TutorialTaskModel.ReassignWorkedTiles, TutorialTaskModel.FromCommand(new WorkTile { CityId = "c", Tile = new HexCoord(0, 0) }));
        Assert.Equal(TutorialTaskModel.ReassignWorkedTiles, TutorialTaskModel.FromCommand(new StopWorkingTile { CityId = "c", Tile = new HexCoord(0, 0) }));
        Assert.Equal(TutorialTaskModel.ReassignWorkedTiles, TutorialTaskModel.FromCommand(new ReassignPopulation { CityId = "c" }));
        Assert.Equal(TutorialTaskModel.ReassignWorkedTiles, TutorialTaskModel.FromCommand(new LockTile { CityId = "c", Tile = new HexCoord(0, 0) }));
        Assert.Null(TutorialTaskModel.FromCommand(new EndTurn()));
    }
}
