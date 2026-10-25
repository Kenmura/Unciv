using Unciv.Logic.City;

namespace Unciv.Commands.City;

/// <summary>
/// Choose the citizen focus. Kotlin: <c>CitizenManagementTable</c> and the stat icons of <c>CityStatsTable</c> list the focuses (faith only when religion is
/// enabled) when <c>canCityBeChanged</c>; <c>CityView.trySetCityFocus</c> sets it and calls <c>reassignPopulation</c>. Re-applying the current focus is offered.
/// </summary>
[CommandType("set-city-focus")]
public sealed class SetCityFocus : GameCommand
{
    public string CityId { get; init; } = "";
    public CityFocus Focus { get; init; }

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.TryChangeableCity(CityId, allowPuppet: false, out _, out var city, out var failure)) return failure;
        // CitizenManagementTable offers the TableEnabled focuses, CityStatsTable one per stat (including happiness); both skip faith without religion.
        bool offered = Enum.IsDefined(Focus) && (Focus != CityFocus.FaithFocus || city.Civ.GameInfo.IsReligionEnabled());
        return offered
            ? CommandValidation.Ok
            : CommandValidation.Fail(CommandErrors.NotAllowed, $"{Focus} cannot be chosen");
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        var city = context.ValidatedCity(CityId);
        city.SetCityFocus(Focus);
        city.ReassignPopulation();
        return [];
    }
}
