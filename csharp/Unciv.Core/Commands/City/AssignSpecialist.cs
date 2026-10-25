namespace Unciv.Commands.City;

/// <summary>
/// Turn a free citizen into a specialist. Kotlin: <c>SpecialistAllocationTable.getAssignButton</c> (assigned &lt; max slots from
/// <c>getMaxSpecialists</c>, specialist exists in the ruleset, free population &gt; 0, not a puppet), i.e.
/// <c>CityView.tryAssignSpecialist</c>: +1, <c>manualSpecialists = true</c>, stats update.
/// </summary>
[CommandType("assign-specialist")]
public sealed class AssignSpecialist : GameCommand
{
    public string CityId { get; init; } = "";
    public string Specialist { get; init; } = "";

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.TryChangeableCity(CityId, allowPuppet: false, out _, out var city, out var failure)) return failure;
        if (!city.GetRuleset().Specialists.ContainsKey(Specialist))
            return CommandValidation.Fail(CommandErrors.InvalidTarget, $"There is no specialist \"{Specialist}\"");
        if (city.Population.GetNewSpecialists()[Specialist] >= city.Population.GetMaxSpecialists()[Specialist])
            return CommandValidation.Fail(CommandErrors.NotAllowed, $"{city.Name} has no free {Specialist} slot");
        return city.Population.GetFreePopulation() > 0
            ? CommandValidation.Ok
            : CommandValidation.Fail(CommandErrors.NotAllowed, $"{city.Name} has no free citizen");
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        var city = context.ValidatedCity(CityId);
        city.Population.SpecialistAllocations.Add(Specialist, 1);
        city.ManualSpecialists = true;
        city.CityStats.Update();
        return [];
    }
}
