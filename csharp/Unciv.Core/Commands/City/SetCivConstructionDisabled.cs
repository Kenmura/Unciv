using Unciv.Models.Ruleset;

namespace Unciv.Commands.City;

/// <summary>
/// Disable or enable a construction in all cities, and for cities founded later. Kotlin: <c>CityScreenConstructionMenu</c> "Disable in all cities"
/// (<c>!isCivConstructionDisabled</c>, not <c>Idle</c>) and "Enable in all cities" (<c>isCivConstructionDisabled</c>), via
/// <c>CivView.tryDisableCivConstruction</c> / <c>tryEnableCivConstruction</c>.
/// </summary>
[CommandType("set-civ-construction-disabled")]
public sealed class SetCivConstructionDisabled : GameCommand
{
    /// <summary>The city whose screen the menu was opened on: it must be changeable, and it reassigns its citizens afterwards.</summary>
    public string CityId { get; init; } = "";
    public string Name { get; init; } = "";
    public bool Disabled { get; init; }

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.TryChangeableCity(CityId, allowPuppet: false, out var actor, out _, out var failure)) return failure;
        var ruleset = actor.GameInfo.Ruleset;
        bool known = ruleset.Buildings.ContainsKey(Name) || ruleset.Units.ContainsKey(Name)
            || PerpetualConstruction.IsNamePerpetual(Name);
        if (!known) return CommandValidation.Fail(CommandErrors.InvalidTarget, $"\"{Name}\" is not a building, unit or perpetual construction");
        if (Disabled && Name == PerpetualConstruction.Idle.Name)
            return CommandValidation.Fail(CommandErrors.NotAllowed, "Idle cannot be disabled");
        return actor.DisabledCityConstructions.Contains(Name) != Disabled
            ? CommandValidation.Ok
            : CommandValidation.Fail(CommandErrors.NotAllowed, Disabled ? $"{Name} is already disabled" : $"{Name} is not disabled");
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        var actor = context.Actor;
        foreach (var city in actor.Cities)
        {
            if (Disabled) city.DisabledConstructions.Add(Name);
            else city.DisabledConstructions.Remove(Name);
        }
        if (Disabled) actor.DisabledCityConstructions.Add(Name);
        else actor.DisabledCityConstructions.Remove(Name);
        context.ValidatedCity(CityId).ReassignPopulation(); // the menu's close callback of CityConstructionsTable
        return [];
    }
}
