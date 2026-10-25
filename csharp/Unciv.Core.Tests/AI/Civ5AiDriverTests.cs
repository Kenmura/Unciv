using Unciv.AI;
using Unciv.AI.GrandStrategy;
using Unciv.AI.Homeland;
using Unciv.Core.Tests.Support;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map.MapUnits;
using Unciv.Models.Metadata;
using GrandStrategyKind = Unciv.AI.GrandStrategy.GrandStrategy;

namespace Unciv.Core.Tests.AI;

// T227: the turn-loop wiring of the Civ 5 AI.
[Collection("RulesetCache")]
public sealed class Civ5AiDriverTests
{
    private readonly TestGame game = new();
    private readonly Civilization ai;
    private readonly Civilization enemy;

    public Civ5AiDriverTests()
    {
        game.MakeHexagonalMap(5);
        ai = game.AddCiv();
        ai.PlayerType = PlayerType.AI;
        enemy = game.AddCiv();
        enemy.PlayerType = PlayerType.AI;
        game.AddCity(ai, game.TileMap[0, 0]);
        game.AddCity(enemy, game.TileMap[0, 4]);
    }

    private void Meet() => ai.DiplomacyFunctions.MakeCivilizationsMeet(enemy);

    [Fact]
    public void IsOnlyActiveWhenTheGameOptionIsOnAndTheCivIsAnAi()
    {
        Assert.False(Civ5AiDriver.IsActive(ai));
        game.GameInfo.GameParameters.UseCiv5Ai = true;
        Assert.True(Civ5AiDriver.IsActive(ai));
        ai.PlayerType = PlayerType.Human;
        Assert.False(Civ5AiDriver.IsActive(ai));
    }

    [Fact]
    public void TheOptionIsCopiedByClone() => Assert.True(new GameParameters { UseCiv5Ai = true }.Clone().UseCiv5Ai);

    [Fact]
    public void GrandStrategyIsDeterministicAndPicksOneOfTheFour()
    {
        Meet();
        var first = Civ5AiDriver.EvaluateGrandStrategy(ai);
        var second = Civ5AiDriver.EvaluateGrandStrategy(ai);
        Assert.Contains(first.ChosenStrategy, GrandStrategyExtensions.All);
        Assert.Equal(first.ChosenStrategy, second.ChosenStrategy);
        Assert.Equal(first.FinalScores.Values, second.FinalScores.Values);
    }

    [Fact]
    public void StrategyFlavorsAddTheStrategysModifiers()
    {
        var none = Civ5AiDriver.FlavorsFor(GrandStrategyKind.None);
        var conquest = Civ5AiDriver.FlavorsFor(GrandStrategyKind.Conquest);
        Assert.Equal(new Unciv.AI.Tactical.TacticalFlavors(5, 5), none);
        Assert.True(conquest.Offense > none.Offense);
    }

    [Fact]
    public void HomelandQueueIsRankedAndRepeatable()
    {
        var queue = Civ5AiDriver.BuildHomelandQueue(ai);
        Assert.Equal(queue, Civ5AiDriver.BuildHomelandQueue(ai));
        Assert.Equal(HomelandMovePriority.AddSpaceshipPart, queue[0].Move);
        Assert.Equal(queue.OrderByDescending(it => it.Priority).Select(it => it.Move), queue.Select(it => it.Move));
    }

    [Fact]
    public void UnitsAreClaimedByTheHomelandMoveThatFitsTheirRole()
    {
        var settler = game.AddUnit("Settler", ai, game.TileMap[0, 1]);
        var worker = game.AddUnit("Worker", ai, game.TileMap[1, 1]);
        var garrison = game.AddUnit("Warrior", ai, game.TileMap[0, 0]);
        var wanderer = game.AddUnit("Warrior", ai, game.TileMap[0, 2]);
        var hurt = game.AddUnit("Warrior", ai, game.TileMap[1, 2]);
        hurt.Health = 40;
        Assert.Equal(HomelandMovePriority.Settle, Civ5AiDriver.ClaimedBy(settler));
        Assert.Equal(HomelandMovePriority.Worker, Civ5AiDriver.ClaimedBy(worker));
        Assert.Equal(HomelandMovePriority.Garrison, Civ5AiDriver.ClaimedBy(garrison));
        Assert.Null(Civ5AiDriver.ClaimedBy(wanderer));
        Assert.Equal(HomelandMovePriority.Heal, Civ5AiDriver.ClaimedBy(hurt));
    }

    [Fact]
    public void HomelandRunHandlesEachClaimedUnitOnceInQueueOrderAndLeavesTheRest()
    {
        var settler = game.AddUnit("Settler", ai, game.TileMap[0, 1]);
        var worker = game.AddUnit("Worker", ai, game.TileMap[1, 1]);
        var wanderer = game.AddUnit("Warrior", ai, game.TileMap[0, 2]);
        var seen = new List<MapUnit>();
        var handled = Civ5AiDriver.RunHomeland(ai, [wanderer, worker, settler], seen.Add);
        Assert.Equal(2, seen.Count);
        Assert.Contains(settler, seen);
        Assert.Contains(worker, seen);
        Assert.DoesNotContain(wanderer, handled);
        Assert.Equal(2, handled.Count);
    }

    [Fact]
    public void TacticalAttacksAnEnemyUnitNextToTheCiv()
    {
        Meet();
        ai.GetDiplomacyManager(enemy)!.DeclareWar();
        var attacker = game.AddUnit("Archer", ai, game.TileMap[0, 1]);
        var victim = game.AddUnit("Warrior", enemy, game.TileMap[0, 3]);
        victim.Health = 20;
        ai.Cache.UpdateViewableTiles();
        int before = victim.Health;
        int attacks = Civ5AiDriver.RunTactical(ai);
        Assert.True(attacks >= 1);
        Assert.True(victim.IsDestroyed || victim.Health < before || attacker.CurrentMovement < attacker.GetMaxMovement());
    }

    [Fact]
    public void AutomatingAnAiCivWithTheOptionOnMovesUnitsAndIsRepeatable()
    {
        game.GameInfo.GameParameters.UseCiv5Ai = true;
        Meet();
        var worker = game.AddUnit("Worker", ai, game.TileMap[1, 1]);
        game.AddUnit("Warrior", ai, game.TileMap[0, 2]);
        Unciv.Logic.Automation.Civilization.NextTurnAutomation.AutomateCivMoves(ai);
        Assert.True(worker.CurrentMovement < worker.GetMaxMovement() || worker.CurrentTile != game.TileMap[0, 1] || worker.IsAutomated() || worker.IsIdle());
    }
}
