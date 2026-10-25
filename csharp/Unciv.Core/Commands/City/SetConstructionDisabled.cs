using Unciv.Models.Ruleset;

namespace Unciv.Commands.City;

/// <summary>
/// Disable or enable a construction in this city only. Kotlin: <c>CityScreenConstructionMenu</c> "Disable in this city" (not yet disabled, not
/// <c>Idle</c>) and "Enable in this city" (disabled), offered when <c>canCityBeChanged</c>, via <c>tryDisableConstruction</c> / <c>tryEnableConstruction</c>.
/// </summary>
[CommandType("set-construction-disabled")]
public sealed class SetConstructionDisabled : GameCommand
{
    public string CityId { get; init; } = "";
    public string Name { get; init; } = "";
    public bool Disabled { get; init; }

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.TryChangeableCity(CityId, allowPuppet: false, out _, out var city, out var failure)) return failure;
        if (!CityCommandChecks.TryConstruction(city, Name, out var construction, out failure)) return failure;
        if (Disabled && ReferenceEquals(construction, PerpetualConstruction.Idle))
            return CommandValidation.Fail(CommandErrors.NotAllowed, "Idle cannot be disabled");
        return city.DisabledConstructions.Contains(Name) != Disabled
            ? CommandValidation.Ok
            : CommandValidation.Fail(CommandErrors.NotAllowed, Disabled ? $"{Name} is already disabled" : $"{Name} is not disabled");
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        var city = context.ValidatedCity(CityId);
        if (Disabled) city.DisabledConstructions.Add(Name);
        else city.DisabledConstructions.Remove(Name);
        city.ReassignPopulation(); // the menu's close callback of CityConstructionsTable
        return [];
    }
}
