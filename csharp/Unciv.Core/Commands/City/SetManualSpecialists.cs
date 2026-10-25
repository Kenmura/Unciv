namespace Unciv.Commands.City;

/// <summary>
/// Switch between automatic and manual specialists. Kotlin: the toggle of <c>SpecialistAllocationTable</c> (offered when <c>canCityBeChanged</c>):
/// <c>tryEnableManualSpecialists</c> sets the flag; <c>tryDisableManualSpecialists</c> clears it and calls <c>reassignPopulation</c>.
/// The UI only offers the opposite of the current value, so a no-op is rejected.
/// </summary>
[CommandType("set-manual-specialists")]
public sealed class SetManualSpecialists : GameCommand
{
    public string CityId { get; init; } = "";
    public bool Manual { get; init; }

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.TryChangeableCity(CityId, allowPuppet: false, out _, out var city, out var failure)) return failure;
        return city.ManualSpecialists != Manual
            ? CommandValidation.Ok
            : CommandValidation.Fail(CommandErrors.NotAllowed, $"Specialists are already {(Manual ? "manual" : "automatic")}");
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        var city = context.ValidatedCity(CityId);
        city.ManualSpecialists = Manual;
        if (!Manual) city.ReassignPopulation();
        return [];
    }
}
