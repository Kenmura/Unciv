using Unciv.Commands.Events;
using Unciv.Logic.GreatWorks;
using Unciv.Utils;

namespace Unciv.Commands.GreatWorks;

/// <summary>
/// Rearranges the actor's works: moves the work in one slot into another slot of the actor's cities (bnw-spec/SPEC.md section 4.2.3). If the
/// target slot holds a work, the two exchange places. Both works must fit the slot they go to.
/// </summary>
[CommandType("move-great-work")]
public sealed class MoveGreatWork : GameCommand
{
    public string FromCityId { get; init; } = "";
    public string FromBuilding { get; init; } = "";
    public int FromSlot { get; init; }
    public string ToCityId { get; init; } = "";
    public string ToBuilding { get; init; } = "";
    public int ToSlot { get; init; }

    private CommandValidation Resolve(CommandContext context, out GreatWorkLocation from, out GreatWorkLocation to)
    {
        from = default;
        to = default;
        if (!context.TryActiveActor(out var actor, out var failure)) return failure;
        if (!context.TrySlot(actor, FromCityId, FromBuilding, FromSlot, out var source, out failure)) return failure;
        if (!context.TrySlot(actor, ToCityId, ToBuilding, ToSlot, out var target, out failure)) return failure;
        from = source.Value;
        to = target.Value;
        if (!context.TryWorkIn(from, out var moving, out failure)) return failure;
        if (ReferenceEquals(from.City, to.City) && from.Building == to.Building && from.Slot == to.Slot)
            return CommandValidation.Fail(CommandErrors.InvalidTarget, "The work is already in that slot");
        if (!GreatWorkRules.SlotFits(to.Building, to.Slot, moving))
            return CommandValidation.Fail(CommandErrors.NotAllowed, $"{moving.Name} does not fit that slot");
        string? other = to.City.GetGreatWorkIn(to.Building.Name, to.Slot);
        if (other != null && context.Game.Ruleset.GreatWorks.TryGetValue(other, out var otherWork) && !GreatWorkRules.SlotFits(from.Building, from.Slot, otherWork))
            return CommandValidation.Fail(CommandErrors.NotAllowed, $"{other} does not fit the slot {moving.Name} leaves");
        return CommandValidation.Ok;
    }

    protected override CommandValidation ValidateCommand(CommandContext context) => Resolve(context, out _, out _);

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        Resolve(context, out var from, out var to);
        string work = from.City.GetGreatWorkIn(from.Building.Name, from.Slot).NotNull();
        GreatWorkRules.MoveWork(from, to);
        context.Actor.UpdateStatsForNextTurn();
        return [new GreatWorkMoved(context.ActorId, work, from.City.Id, from.Building.Name, from.Slot, to.City.Id, to.Building.Name, to.Slot)];
    }
}
