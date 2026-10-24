using Unciv.Logic.Map;
using Unciv.Utils;

namespace Unciv.Commands;

public interface IGameContext
{
    int CurrentTurn { get; set; }
    string CurrentPlayerCiv { get; set; }
}

public interface ICommand
{
    string CommandId { get; }
    string PlayerCiv { get; }
    int Turn { get; }
    string CommandType { get; }

    CommandResult Validate(IGameContext context);
    IReadOnlyList<IGameEvent> Execute(IGameContext context, GameRandom rng);
}

public readonly record struct CommandResult(bool IsSuccess, string? ErrorMessage = null)
{
    public static CommandResult Success() => new(true);
    public static CommandResult Failure(string message) => new(false, message);
}

public interface IGameEvent
{
    string EventId { get; }
    int Turn { get; }
    string EventType { get; }
}

public record UnitMovedEvent(
    string EventId,
    int Turn,
    int UnitId,
    HexCoord From,
    HexCoord To,
    float MovementRemaining) : IGameEvent
{
    public string EventType => "UnitMoved";
}

public record CityConstructionSetEvent(
    string EventId,
    int Turn,
    int CityId,
    string ConstructionName) : IGameEvent
{
    public string EventType => "CityConstructionSet";
}

public record TurnEndedEvent(
    string EventId,
    int Turn,
    string PlayerCiv) : IGameEvent
{
    public string EventType => "TurnEnded";
}

public class MoveUnitCommand : ICommand
{
    public string CommandId { get; init; } = "";
    public string PlayerCiv { get; init; } = "";
    public int Turn { get; init; }
    public string CommandType => "MoveUnit";

    public int UnitId { get; init; }
    public HexCoord Target { get; init; }

    public CommandResult Validate(IGameContext context)
    {
        if (context.CurrentPlayerCiv != PlayerCiv)
            return CommandResult.Failure("It is not this player's turn.");
        if (UnitId <= 0)
            return CommandResult.Failure("Invalid UnitId.");
        return CommandResult.Success();
    }

    public IReadOnlyList<IGameEvent> Execute(IGameContext context, GameRandom rng)
    {
        var validation = Validate(context);
        if (!validation.IsSuccess)
            throw new InvalidOperationException(validation.ErrorMessage);

        return new List<IGameEvent>
        {
            new UnitMovedEvent(
                EventId: $"{CommandId}_evt",
                Turn: context.CurrentTurn,
                UnitId: UnitId,
                From: HexCoord.Zero,
                To: Target,
                MovementRemaining: 1.0f)
        };
    }
}

public class SetCityConstructionCommand : ICommand
{
    public string CommandId { get; init; } = "";
    public string PlayerCiv { get; init; } = "";
    public int Turn { get; init; }
    public string CommandType => "SetCityConstruction";

    public int CityId { get; init; }
    public string ConstructionName { get; init; } = "";

    public CommandResult Validate(IGameContext context)
    {
        if (context.CurrentPlayerCiv != PlayerCiv)
            return CommandResult.Failure("It is not this player's turn.");
        if (CityId <= 0)
            return CommandResult.Failure("Invalid CityId.");
        if (string.IsNullOrWhiteSpace(ConstructionName))
            return CommandResult.Failure("Construction name cannot be empty.");
        return CommandResult.Success();
    }

    public IReadOnlyList<IGameEvent> Execute(IGameContext context, GameRandom rng)
    {
        var validation = Validate(context);
        if (!validation.IsSuccess)
            throw new InvalidOperationException(validation.ErrorMessage);

        return new List<IGameEvent>
        {
            new CityConstructionSetEvent(
                EventId: $"{CommandId}_evt",
                Turn: context.CurrentTurn,
                CityId: CityId,
                ConstructionName: ConstructionName)
        };
    }
}

public class EndTurnCommand : ICommand
{
    public string CommandId { get; init; } = "";
    public string PlayerCiv { get; init; } = "";
    public int Turn { get; init; }
    public string CommandType => "EndTurn";

    public CommandResult Validate(IGameContext context)
    {
        if (context.CurrentPlayerCiv != PlayerCiv)
            return CommandResult.Failure("It is not this player's turn.");
        return CommandResult.Success();
    }

    public IReadOnlyList<IGameEvent> Execute(IGameContext context, GameRandom rng)
    {
        var validation = Validate(context);
        if (!validation.IsSuccess)
            throw new InvalidOperationException(validation.ErrorMessage);

        context.CurrentTurn += 1;

        return new List<IGameEvent>
        {
            new TurnEndedEvent(
                EventId: $"{CommandId}_evt",
                Turn: context.CurrentTurn - 1,
                PlayerCiv: PlayerCiv)
        };
    }
}

public class CommandProcessor
{
    private readonly IGameContext context;
    private readonly GameRandom rng;
    private readonly List<ICommand> executedCommands = new();
    private readonly List<IGameEvent> eventHistory = new();

    public CommandProcessor(IGameContext context, GameRandom rng)
    {
        this.context = context;
        this.rng = rng;
    }

    public IReadOnlyList<ICommand> ExecutedCommands => executedCommands;
    public IReadOnlyList<IGameEvent> EventHistory => eventHistory;

    public CommandResult Process(ICommand command, out IReadOnlyList<IGameEvent> emittedEvents)
    {
        var validation = command.Validate(context);
        if (!validation.IsSuccess)
        {
            emittedEvents = Array.Empty<IGameEvent>();
            return validation;
        }

        var events = command.Execute(context, rng);
        executedCommands.Add(command);
        eventHistory.AddRange(events);
        emittedEvents = events;
        return CommandResult.Success();
    }
}
