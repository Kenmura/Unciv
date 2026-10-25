using Unciv.Logic.City;

namespace Unciv.Commands.City;

/// <summary>
/// Choose the citizen focus. Kotlin: <c>CitizenManagementTable</c> lists the focuses with <c>tableEnabled</c> (faith only when religion is
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
        bool offered = Focus.TableEnabled() && (Focus != CityFocus.FaithFocus || city.Civ.GameInfo.IsReligionEnabled());
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
