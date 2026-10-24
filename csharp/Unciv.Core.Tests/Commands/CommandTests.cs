using Unciv.Commands;
using Unciv.Logic.Map;
using Unciv.Utils;

namespace Unciv.Core.Tests.Commands;

public class TestGameContext : IGameContext
{
    public int CurrentTurn { get; set; } = 1;
    public string CurrentPlayerCiv { get; set; } = "Rome";
}

public class CommandTests
{
    [Fact]
    public void MoveUnitCommand_ValidatesTurnAndUnitId()
    {
        var context = new TestGameContext { CurrentPlayerCiv = "Rome", CurrentTurn = 1 };
        var validCommand = new MoveUnitCommand
        {
            CommandId = "cmd_1",
            PlayerCiv = "Rome",
            Turn = 1,
            UnitId = 101,
            Target = HexCoord.Of(1, 2)
        };
        var result = validCommand.Validate(context);
        Assert.True(result.IsSuccess);

        var wrongCiv = new MoveUnitCommand
        {
            CommandId = "cmd_2",
            PlayerCiv = "Greece",
            Turn = 1,
            UnitId = 101,
            Target = HexCoord.Of(1, 2)
        };
        Assert.False(wrongCiv.Validate(context).IsSuccess);

        var invalidUnit = new MoveUnitCommand
        {
            CommandId = "cmd_3",
            PlayerCiv = "Rome",
            Turn = 1,
            UnitId = 0,
            Target = HexCoord.Of(1, 2)
        };
        Assert.False(invalidUnit.Validate(context).IsSuccess);
    }

    [Fact]
    public void MoveUnitCommand_ExecutesAndEmitsEvent()
    {
        var context = new TestGameContext();
        var rng = new GameRandom(42);
        var command = new MoveUnitCommand
        {
            CommandId = "cmd_move",
            PlayerCiv = "Rome",
            Turn = 1,
            UnitId = 5,
            Target = HexCoord.Of(3, 4)
        };

        var events = command.Execute(context, rng);
        Assert.Single(events);
        var moved = Assert.IsType<UnitMovedEvent>(events[0]);
        Assert.Equal(5, moved.UnitId);
        Assert.Equal(HexCoord.Of(3, 4), moved.To);
    }

    [Fact]
    public void SetCityConstructionCommand_ValidatesAndEmitsEvent()
    {
        var context = new TestGameContext();
        var rng = new GameRandom(42);
        var emptyName = new SetCityConstructionCommand
        {
            CommandId = "cmd_empty",
            PlayerCiv = "Rome",
            Turn = 1,
            CityId = 1,
            ConstructionName = ""
        };
        Assert.False(emptyName.Validate(context).IsSuccess);

        var valid = new SetCityConstructionCommand
        {
            CommandId = "cmd_monument",
            PlayerCiv = "Rome",
            Turn = 1,
            CityId = 1,
            ConstructionName = "Monument"
        };
        Assert.True(valid.Validate(context).IsSuccess);
        var events = valid.Execute(context, rng);
        Assert.Single(events);
        var constructionSet = Assert.IsType<CityConstructionSetEvent>(events[0]);
        Assert.Equal("Monument", constructionSet.ConstructionName);
    }

    [Fact]
    public void EndTurnCommand_AdvancesTurnAndEmitsEvent()
    {
        var context = new TestGameContext { CurrentTurn = 5 };
        var rng = new GameRandom(42);
        var endTurn = new EndTurnCommand
        {
            CommandId = "cmd_end",
            PlayerCiv = "Rome",
            Turn = 5
        };

        Assert.True(endTurn.Validate(context).IsSuccess);
        var events = endTurn.Execute(context, rng);
        Assert.Equal(6, context.CurrentTurn);
        Assert.Single(events);
        var turnEnded = Assert.IsType<TurnEndedEvent>(events[0]);
        Assert.Equal(5, turnEnded.Turn);
    }

    [Fact]
    public void CommandProcessor_ProcessesPipelineAndMaintainsHistory()
    {
        var context = new TestGameContext();
        var rng = new GameRandom(42);
        var processor = new CommandProcessor(context, rng);

        var moveCmd = new MoveUnitCommand
        {
            CommandId = "cmd_1",
            PlayerCiv = "Rome",
            Turn = 1,
            UnitId = 1,
            Target = HexCoord.Of(1, 0)
        };
        var res1 = processor.Process(moveCmd, out var events1);
        Assert.True(res1.IsSuccess);
        Assert.Single(events1);

        var invalidCmd = new MoveUnitCommand
        {
            CommandId = "cmd_invalid",
            PlayerCiv = "WrongCiv",
            Turn = 1,
            UnitId = 1,
            Target = HexCoord.Of(2, 0)
        };
        var res2 = processor.Process(invalidCmd, out var events2);
        Assert.False(res2.IsSuccess);
        Assert.Empty(events2);

        var endTurnCmd = new EndTurnCommand
        {
            CommandId = "cmd_2",
            PlayerCiv = "Rome",
            Turn = 1
        };
        var res3 = processor.Process(endTurnCmd, out var events3);
        Assert.True(res3.IsSuccess);
        Assert.Single(events3);

        Assert.Equal(2, processor.ExecutedCommands.Count);
        Assert.Equal(2, processor.EventHistory.Count);
        Assert.Equal(2, context.CurrentTurn);
    }

    [Fact]
    public void ReplayDeterminism_TwoProcessorsProduceIdenticalOutcomes()
    {
        var commands = new ICommand[]
        {
            new MoveUnitCommand { CommandId = "c1", PlayerCiv = "Rome", Turn = 1, UnitId = 10, Target = HexCoord.Of(0, 1) },
            new SetCityConstructionCommand { CommandId = "c2", PlayerCiv = "Rome", Turn = 1, CityId = 20, ConstructionName = "Granary" },
            new EndTurnCommand { CommandId = "c3", PlayerCiv = "Rome", Turn = 1 }
        };

        var ctxA = new TestGameContext();
        var procA = new CommandProcessor(ctxA, new GameRandom(12345));

        var ctxB = new TestGameContext();
        var procB = new CommandProcessor(ctxB, new GameRandom(12345));

        foreach (var cmd in commands)
        {
            procA.Process(cmd, out _);
            procB.Process(cmd, out _);
        }

        Assert.Equal(ctxA.CurrentTurn, ctxB.CurrentTurn);
        Assert.Equal(procA.EventHistory.Count, procB.EventHistory.Count);
        for (int i = 0; i < procA.EventHistory.Count; i++)
        {
            Assert.Equal(procA.EventHistory[i].EventType, procB.EventHistory[i].EventType);
            Assert.Equal(procA.EventHistory[i].EventId, procB.EventHistory[i].EventId);
        }
    }
}
