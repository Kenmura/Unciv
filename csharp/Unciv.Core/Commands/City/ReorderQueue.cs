using Unciv.Models.Ruleset;

namespace Unciv.Commands.City;

public enum QueueMove
{
    Top,
    End,
    Up,
    Down,
}

/// <summary>
/// Move a queue entry. Kotlin: <c>CityConstructionsTable</c> raise/lower buttons (<c>index &gt; 0</c> / <c>index != lastIndex</c>) and the
/// <c>CityScreenConstructionMenu</c> "Move to the top" (<c>index &gt; 0</c>, not perpetual) and "to the end" (not perpetual,
/// <c>index &lt; entries without perpetuals - 1</c>); all followed by <c>tryReassignPopulation</c>.
/// </summary>
[CommandType("reorder-queue")]
public sealed class ReorderQueue : GameCommand
{
    public string CityId { get; init; } = "";
    public int Index { get; init; }
    public QueueMove Move { get; init; }

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.TryChangeableCity(CityId, allowPuppet: false, out _, out var city, out var failure)) return failure;
        var queue = city.CityConstructions.ConstructionQueue;
        if (Index < 0 || Index >= queue.Count)
            return CommandValidation.Fail(CommandErrors.InvalidTarget, $"There is no queue entry {Index}");
        bool perpetual = PerpetualConstruction.IsNamePerpetual(queue[Index]);
        bool allowed = Move switch
        {
            QueueMove.Top => !perpetual && Index > 0,
            QueueMove.End => !perpetual && Index < queue.Count(it => !PerpetualConstruction.IsNamePerpetual(it)) - 1,
            QueueMove.Up => Index > 0,
            QueueMove.Down => Index != queue.Count - 1,
            _ => false,
        };
        return allowed
            ? CommandValidation.Ok
            : CommandValidation.Fail(CommandErrors.NotAllowed, $"Entry {Index} cannot be moved {Move}");
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        var city = context.ValidatedCity(CityId);
        var constructions = city.CityConstructions;
        switch (Move)
        {
            case QueueMove.Top: constructions.MoveEntryToTop(Index); break;
            case QueueMove.End: constructions.MoveEntryToEnd(Index); break;
            case QueueMove.Up: constructions.RaisePriority(Index); break;
            case QueueMove.Down: constructions.LowerPriority(Index); break;
        }
        city.ReassignPopulation();
        return [];
    }
}
