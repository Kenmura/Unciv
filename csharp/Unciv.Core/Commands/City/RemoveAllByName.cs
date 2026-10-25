namespace Unciv.Commands.City;

/// <summary>
/// Remove every queue entry of one construction. Kotlin: <c>CityScreenConstructionMenu</c> "Remove from the queue in all cities", which runs
/// <c>tryRemoveAllByName</c> per candidate city (not a puppet, not in resistance, not being razed) that has the construction queued; the menu
/// then reassigns the screen city's population (done here for the city of the command).
/// </summary>
[CommandType("remove-all-by-name")]
public sealed class RemoveAllByName : GameCommand
{
    public string CityId { get; init; } = "";
    public string Name { get; init; } = "";

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.TryChangeableCity(CityId, allowPuppet: false, out _, out var city, out var failure)) return failure;
        if (city.IsInResistance() || city.IsBeingRazed)
            return CommandValidation.Fail(CommandErrors.NotAllowed, $"The queue of {city.Name} cannot be changed now");
        return city.CityConstructions.IsBeingConstructedOrEnqueued(Name)
            ? CommandValidation.Ok
            : CommandValidation.Fail(CommandErrors.InvalidTarget, $"{Name} is not in the queue of {city.Name}");
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        var city = context.ValidatedCity(CityId);
        city.CityConstructions.RemoveAllByName(Name);
        city.ReassignPopulation();
        return [];
    }
}
