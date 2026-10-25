using Unciv.AI;
using Unciv.AI.Operations;
using Unciv.Core.Tests.Support;
using Unciv.Json;
using Unciv.Logic.Civilization;
using Unciv.Models.Metadata;

namespace Unciv.Core.Tests.AI.Operations;

// T235: operations wired into the Civ 5 AI driver, and persisted with the civ.
[Collection("RulesetCache")]
public sealed class OperationsAdapterTests
{
    private readonly TestGame game = new();
    private readonly Civilization ai;
    private readonly Civilization enemy;

    public OperationsAdapterTests()
    {
        game.MakeHexagonalMap(8);
        ai = game.AddCiv();
        ai.PlayerType = PlayerType.AI;
        enemy = game.AddCiv();
        enemy.PlayerType = PlayerType.AI;
        game.AddCity(ai, game.TileMap[0, 0]);
        game.AddCity(enemy, game.TileMap[0, 6]);
        ai.DiplomacyFunctions.MakeCivilizationsMeet(enemy);
    }

    private void ArmUp()
    {
        game.AddUnit("Warrior", ai, game.TileMap[1, 1]);
        game.AddUnit("Warrior", ai, game.TileMap[1, 2]);
        game.AddUnit("Archer", ai, game.TileMap[-1, 1]);
        ai.Cache.UpdateViewableTiles();
    }

    [Fact]
    public void TheCiv5AiIsOnByDefault() => Assert.True(new GameParameters().UseCiv5Ai);

    [Fact]
    public void NoOperationStartsAtPeace()
    {
        ArmUp();
        Civ5AiDriver.RunOperations(ai);
        Assert.Empty(ai.Civ5Ai!.Operations);
    }

    [Fact]
    public void AtWarAnArmyIsRecruitedAndMarchedAndSavedWithTheCiv()
    {
        ArmUp();
        ai.GetDiplomacyManager(enemy)!.DeclareWar();
        ai.Cache.UpdateViewableTiles();
        ai.ViewableTiles = new HashSet<Unciv.Logic.Map.Tiles.Tile>(ai.ViewableTiles) { game.TileMap[0, 6] };   // sight on the enemy city

        var moved = Civ5AiDriver.RunOperations(ai);
        var record = Assert.Single(ai.Civ5Ai!.Operations);
        Assert.Equal(OperationType.BasicCityAttack, record.Type);
        Assert.Equal(game.TileMap[0, 6].Position, record.Target);
        Assert.Equal(game.GameInfo.Turns, record.StartTurn);
        Assert.All(record.Filled, id => Assert.NotNull(id));
        Assert.NotEmpty(moved);

        // survives a JSON round trip and a clone, and the next turn carries on from it
        var json = UncivJson.Serialize(ai.Civ5Ai);
        var back = UncivJson.Deserialize<Civ5AiState>(json)!;
        Assert.Equal(json, UncivJson.Serialize(back));
        Assert.Equal(json, UncivJson.Serialize(ai.Clone().Civ5Ai));
        Civ5AiDriver.RunOperations(ai);
        Assert.Single(ai.Civ5Ai.Operations);
    }

    [Fact]
    public void OperationEndsWhenTheWarEnds()
    {
        ArmUp();
        ai.GetDiplomacyManager(enemy)!.DeclareWar();
        ai.ViewableTiles = new HashSet<Unciv.Logic.Map.Tiles.Tile>(ai.ViewableTiles) { game.TileMap[0, 6] };
        Civ5AiDriver.RunOperations(ai);
        Assert.Single(ai.Civ5Ai!.Operations);
        ai.GetDiplomacyManager(enemy)!.MakePeace();
        Civ5AiDriver.RunOperations(ai);
        Assert.Empty(ai.Civ5Ai.Operations);
    }

    [Fact]
    public void ABlockedTargetIsLeftAloneUntilItsTurnComes()
    {
        ArmUp();
        ai.GetDiplomacyManager(enemy)!.DeclareWar();
        ai.ViewableTiles = new HashSet<Unciv.Logic.Map.Tiles.Tile>(ai.ViewableTiles) { game.TileMap[0, 6] };
        int turn = game.GameInfo.Turns;
        ai.Civ5Ai = new Civ5AiState { BlockedTargets = [new BlockedTarget { Position = game.TileMap[0, 6].Position, UntilTurn = turn + 1 }] };
        Civ5AiDriver.RunOperations(ai);
        Assert.Empty(ai.Civ5Ai.Operations);
        Assert.Single(ai.Civ5Ai.BlockedTargets);
        Assert.Equal(UncivJson.Serialize(ai.Civ5Ai), UncivJson.Serialize(UncivJson.Deserialize<Civ5AiState>(UncivJson.Serialize(ai.Civ5Ai))));
        game.GameInfo.Turns = turn + 1;
        Civ5AiDriver.RunOperations(ai);
        Assert.Single(ai.Civ5Ai.Operations);
        Assert.Empty(ai.Civ5Ai.BlockedTargets);
    }

    [Fact]
    public void StaleOperationsAreCalledOff()
    {
        ArmUp();
        ai.GetDiplomacyManager(enemy)!.DeclareWar();
        ai.ViewableTiles = new HashSet<Unciv.Logic.Map.Tiles.Tile>(ai.ViewableTiles) { game.TileMap[0, 6] };
        Civ5AiDriver.RunOperations(ai);
        ai.Civ5Ai!.Operations[0].StartTurn = game.GameInfo.Turns - OperationsAdapter.MaxOperationTurns - 1;
        Civ5AiDriver.RunOperations(ai);
        Assert.DoesNotContain(ai.Civ5Ai.Operations, o => o.StartTurn < game.GameInfo.Turns);
    }

    [Fact]
    public void GrandStrategyAndItsLockCounterArePersisted()
    {
        Assert.Null(ai.Civ5Ai);
        var first = Civ5AiDriver.AdvanceGrandStrategy(ai);
        Assert.Equal(first.ChosenStrategy, ai.Civ5Ai!.CurrentStrategy);
        Assert.Equal(0, ai.Civ5Ai.TurnsActive);   // changed from None: the lock counter restarts
        var pure = Civ5AiDriver.EvaluateGrandStrategy(ai);
        Assert.Equal(first.ChosenStrategy, pure.ChosenStrategy);   // hysteresis keeps it, the lock holds it
        Assert.Equal(0, ai.Civ5Ai.TurnsActive);                    // the pure form does not advance
        Assert.Equal(first.ChosenStrategy, Civ5AiDriver.AdvanceGrandStrategy(ai).ChosenStrategy);
        Assert.Equal(1, ai.Civ5Ai.TurnsActive);
    }

    [Fact]
    public void RestoreRejectsAMismatchedRecord()
    {
        var state = new Civ5AiState();
        state.Operations.Add(new OperationRecord { Id = 1, Slots = [ArmyRole.Melee], Filled = [] });
        var tunables = Unciv.AI.Operations.OperationTunables.FromDefines(Unciv.AI.Data.GlobalAIDefines.LoadEmbedded(), Unciv.AI.Data.AIEdition.BNW);
        Assert.Throws<ArgumentException>(() => OperationManager.Restore(tunables, state));
    }
}
