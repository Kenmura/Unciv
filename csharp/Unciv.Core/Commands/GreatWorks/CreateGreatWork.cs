using Unciv.Commands.Events;
using Unciv.Logic.GreatWorks;
using Unciv.Utils;

namespace Unciv.Commands.GreatWorks;

/// <summary>
/// A Great Writer, Artist or Musician standing in one of the actor's cities creates a Great Work (bnw-spec/SPEC.md section 4.2.3). The unit is
/// consumed and the work goes into the closest free slot of its kind among the actor's cities. Unavailable when no such slot is free or no
/// work is left to create.
/// </summary>
[CommandType("create-great-work")]
public sealed class CreateGreatWork : GameCommand
{
    public int UnitId { get; init; }

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.TryActiveActor(out var actor, out var failure)) return failure;
        if (!context.OwnUnit(actor, UnitId).TryGet(out var unit, out failure)) return failure;
        if (!unit.HasTile() || unit.GetTile().GetCity() is not { } city || !ReferenceEquals(city.Civ, actor) || !unit.GetTile().IsCityCenter())
            return CommandValidation.Fail(CommandErrors.NotAllowed, "The unit must stand in one of your cities");
        if (GreatWorkRules.ClassCreatedBy(unit, context.Game.Ruleset) is not { } greatWorkClass)
            return CommandValidation.Fail(CommandErrors.NotAllowed, $"{unit.Name} cannot create Great Works");
        if (GreatWorkRules.ChooseWork(actor, unit) == null)
            return CommandValidation.Fail(CommandErrors.NotAllowed, "There is no Great Work left to create");
        return GreatWorkRules.FindClosestFreeSlot(actor, unit.GetTile(), greatWorkClass) != null
            ? CommandValidation.Ok
            : CommandValidation.Fail(CommandErrors.NotAllowed, "No building of yours has a free slot for this Great Work");
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        var actor = context.Actor;
        var unit = context.OwnUnit(actor, UnitId).Value.NotNull();
        var (instance, location) = GreatWorkRules.Create(actor, unit);
        var work = context.Game.Ruleset.GreatWorks[instance.Name];
        return [new GreatWorkCreated(actor.CivId, work.Name, work.GreatWorkClass.ToString(), location.City.Id, location.Building.Name, location.Slot)];
    }
}
