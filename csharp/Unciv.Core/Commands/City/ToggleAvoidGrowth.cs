namespace Unciv.Commands.City;

/// <summary>Flip "Avoid Growth". Kotlin: <c>CityView.tryToggleAvoidGrowth</c> (offered when <c>canCityBeChanged</c>): flips the flag, then <c>reassignPopulation</c>.</summary>
[CommandType("toggle-avoid-growth")]
public sealed class ToggleAvoidGrowth : GameCommand
{
    public string CityId { get; init; } = "";

    protected override CommandValidation ValidateCommand(CommandContext context) =>
        context.TryChangeableCity(CityId, allowPuppet: false, out _, out _, out var failure) ? CommandValidation.Ok : failure;

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        var city = context.ValidatedCity(CityId);
        city.AvoidGrowth = !city.AvoidGrowth;
        city.ReassignPopulation();
        return [];
    }
}
