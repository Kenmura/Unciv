namespace Unciv.Commands.City;

/// <summary>
/// Turn a specialist back into a free citizen. Kotlin: <c>SpecialistAllocationTable.getUnassignButton</c> (assigned &gt; 0, not a puppet),
/// i.e. <c>CityView.tryUnassignSpecialist</c>: -1, <c>manualSpecialists = true</c>, stats update.
/// </summary>
[CommandType("unassign-specialist")]
public sealed class UnassignSpecialist : GameCommand
{
    public string CityId { get; init; } = "";
    public string Specialist { get; init; } = "";

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.TryChangeableCity(CityId, allowPuppet: false, out _, out var city, out var failure)) return failure;
        if (!city.GetRuleset().Specialists.ContainsKey(Specialist))
            return CommandValidation.Fail(CommandErrors.InvalidTarget, $"There is no specialist \"{Specialist}\"");
        return city.Population.GetNewSpecialists()[Specialist] > 0
            ? CommandValidation.Ok
            : CommandValidation.Fail(CommandErrors.NotAllowed, $"{city.Name} has no {Specialist} to unassign");
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        var city = context.ValidatedCity(CityId);
        city.Population.SpecialistAllocations.Add(Specialist, -1);
        city.ManualSpecialists = true;
        city.CityStats.Update();
        return [];
    }
}
