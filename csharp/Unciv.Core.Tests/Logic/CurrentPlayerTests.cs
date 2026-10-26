using Unciv.Core.Tests.Support;
using Unciv.Logic.Map;
using Unciv.Logic.Map.MapUnit.Actions;
using Unciv.Logic.Map.Tiles;
using Unciv.Models;
using Unciv.View;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Core.Tests.Logic;

// T201: the rules that used to ask "is this the current player" are civ-explicit (Civilization.IsActive).
[Collection("RulesetCache")]
public sealed class CurrentPlayerTests
{
    private sealed class World
    {
        public TestGame Game { get; } = new();
        public CivilizationObject A { get; }
        public CivilizationObject B { get; }
        public CivilizationObject Ai { get; }

        public World()
        {
            Game.MakeHexagonalMap(6);
            A = Game.AddCiv(isPlayer: true);
            B = Game.AddCiv(isPlayer: true);
            Ai = Game.AddCiv();
            Game.AddCity(A, Game.GetTile(HexCoord.Of(-3, 0)));
            Game.AddCity(B, Game.GetTile(HexCoord.Of(3, 0)));
            Game.AddCity(Ai, Game.GetTile(HexCoord.Of(0, 3)));
            Game.GameInfo.CurrentPlayerCiv = A;
        }

        public void GoSimultaneous() => Game.GameInfo.StartSimultaneousTurns();
    }

    private static GameView Viewed(World w, CivilizationObject civ) => new(w.Game.GameInfo, civ, false);

    [Fact]
    public void Sequential_IsActive_IsTheCurrentPlayerOnly()
    {
        var w = new World();
        Assert.True(w.A.IsActive());
        Assert.False(w.B.IsActive());
        Assert.False(w.Ai.IsActive());
        Assert.Equal(w.A.IsCurrentPlayer(), Viewed(w, w.A).CivView.IsActive());
    }

    [Fact]
    public void Sequential_OnlyTheCurrentHumanReassignsImmediately()
    {
        var w = new World();
        var (cityA, cityB) = (w.A.Cities[0], w.B.Cities[0]);
        cityA.ShouldReassignPopulation = cityB.ShouldReassignPopulation = false;
        cityA.ReassignPopulationDeferred();
        cityB.ReassignPopulationDeferred();
        Assert.False(cityA.ShouldReassignPopulation);
        Assert.True(cityB.ShouldReassignPopulation);
    }

    [Fact]
    public void Simultaneous_EveryActiveHumanReassignsImmediately_AiAndEndedHumansDefer()
    {
        var w = new World();
        w.GoSimultaneous();
        var (cityA, cityB, cityAi) = (w.A.Cities[0], w.B.Cities[0], w.Ai.Cities[0]);
        Assert.True(w.A.IsActive() && w.B.IsActive());
        Assert.False(w.Ai.IsActive());

        foreach (var city in new[] { cityA, cityB, cityAi }) city.ShouldReassignPopulation = false;
        cityA.ReassignPopulationDeferred();
        cityB.ReassignPopulationDeferred();
        cityAi.ReassignPopulationDeferred();
        Assert.False(cityA.ShouldReassignPopulation);
        Assert.False(cityB.ShouldReassignPopulation);
        Assert.True(cityAi.ShouldReassignPopulation);

        w.Game.GameInfo.EndHumanTurn(w.B);
        Assert.False(w.B.IsActive());
        cityB.ReassignPopulationDeferred();
        Assert.True(cityB.ShouldReassignPopulation);
        Assert.True(w.A.IsActive());
    }

    [Fact]
    public void CivView_AndCityView_FollowIsActive()
    {
        var w = new World();
        w.GoSimultaneous();
        Assert.True(Viewed(w, w.A).CivView.IsActive());
        Assert.True(Viewed(w, w.B).CivView.IsActive());
        Assert.False(Viewed(w, w.Ai).CivView.IsActive());
        w.Game.GameInfo.EndHumanTurn(w.B);
        Assert.False(Viewed(w, w.B).CivView.IsActive());
        Assert.True(Viewed(w, w.A).CivView.IsActive());
    }

    private static List<UnitAction> GiftActions(World w, CivilizationObject owner, CivilizationObject territoryOwner)
    {
        var tile = w.Game.GetTile(territoryOwner.Cities[0].Location);
        tile.SetOwningCity(territoryOwner.Cities[0]);
        owner.GetDiplomacyManagerOrMeet(territoryOwner);
        territoryOwner.GetDiplomacyManagerOrMeet(owner);
        owner.GetDiplomacyManager(territoryOwner)!.HasOpenBorders = true; // friendly territory, so only the current-player rule can hide the gift
        territoryOwner.GetDiplomacyManager(owner)!.HasOpenBorders = true;
        var unit = w.Game.AddUnit("Warrior", owner, null);
        unit.CurrentTile = tile;
        unit.CurrentMovement = unit.GetMaxMovement();
        tile.MilitaryUnit = unit;
        return UnitActions.GetUnitActions(unit, UnitActionType.GiftUnit).ToList();
    }

    [Fact]
    public void Gift_IsHiddenInOwnTerritoryAlways_AndInTheCurrentPlayersTerritoryOnlyWhenSequential()
    {
        var seq = new World();
        Assert.Empty(GiftActions(seq, seq.B, seq.A)); // Kotlin quirk kept: A is the current player
        Assert.Empty(GiftActions(seq, seq.A, seq.A));

        var sim = new World();
        sim.GoSimultaneous();
        Assert.Empty(GiftActions(sim, sim.B, sim.B)); // own territory
        Assert.NotEmpty(GiftActions(sim, sim.B, sim.A)); // nobody is "the" current player
    }

    [Fact]
    public void CityView_CanChangeState_ForEveryActiveHumanInSimultaneousPlay()
    {
        var w = new World();
        w.GoSimultaneous();
        var cityB = w.B.Cities[0];
        var tile = w.Game.GetTile(cityB.Location).GetTilesAtDistance(2).First(it => it.Neighbors.Any(n => ReferenceEquals(n.GetCity(), cityB)) && it.GetOwner() == null);
        w.B.AddGold(1000);
        var viewB = Viewed(w, w.B);
        var cityView = viewB.GetCityView(cityB);
        Assert.False(Viewed(w, w.A).GetCityView(cityB).TryBuyTile(viewB.GetTile(tile))); // not A's city
        w.Game.GameInfo.EndHumanTurn(w.B);
        Assert.False(cityView.TryBuyTile(viewB.GetTile(tile))); // B has ended its turn
        w.Game.GameInfo.UndoEndHumanTurn(w.B);
        Assert.True(cityView.TryBuyTile(viewB.GetTile(tile))); // B is active although A is the CurrentPlayer
        Assert.Same(cityB, tile.GetCity());
    }

    private static bool FinishWorkerImprovement(World w, CivilizationObject owner)
    {
        var tile = w.Game.GetTile(HexCoord.Of(owner == w.A ? -3 : 3, 1));
        var worker = w.Game.AddUnit("Worker", owner, tile);
        tile.ImprovementQueue.Add(new Tile.ImprovementQueueEntry("Farm", 1));
        tile.DoWorkerTurn(worker);
        return w.Game.GameInfo.ClientState.TutorialTasksCompleted(owner.CivId).Contains("Construct an improvement");
    }

    [Fact]
    public void TutorialTasks_Sequential_OnlyTheCurrentPlayerCompletesThem()
    {
        var w = new World();
        Assert.False(FinishWorkerImprovement(w, w.B));
        Assert.True(FinishWorkerImprovement(w, w.A));
    }

    [Fact]
    public void TutorialTasks_Simultaneous_AnyActiveHumanCompletesThem_AnEndedOneDoesNot()
    {
        var w = new World();
        w.GoSimultaneous();
        w.Game.GameInfo.EndHumanTurn(w.A);
        Assert.False(FinishWorkerImprovement(w, w.A));
        Assert.True(FinishWorkerImprovement(w, w.B)); // B is not the CurrentPlayer but is active
    }
}
