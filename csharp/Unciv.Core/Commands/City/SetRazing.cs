using Unciv.Models.Ruleset.Unique;

namespace Unciv.Commands.City;

/// <summary>
/// Start or stop razing a city. Kotlin: <c>CityScreen.updateAnnexAndRazeCityButton</c>. "Raze city" is shown for cities that are not being razed
/// and not (puppet and annexable), enabled when <c>canBeDestroyed()</c> and the civ may annex; "Stop razing city" is shown for a razing city;
/// both call <c>CityView.trySetRazing</c>.
/// </summary>
[CommandType("set-razing")]
public sealed class SetRazing : GameCommand
{
    public string CityId { get; init; } = "";
    public bool Raze { get; init; }

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.TryChangeableCity(CityId, allowPuppet: true, out var actor, out var city, out var failure)) return failure;
        if (city.IsBeingRazed == Raze) // already in the requested state
            return CommandValidation.Fail(CommandErrors.NotAllowed, Raze ? $"{city.Name} is already being razed" : $"{city.Name} is not being razed");
        if (!Raze) return CommandValidation.Ok;
        return !actor.HasUnique(UniqueType.MayNotAnnexCities) && !city.IsPuppet && city.CanBeDestroyed()
            ? CommandValidation.Ok
            : CommandValidation.Fail(CommandErrors.NotAllowed, $"{city.Name} cannot be razed");
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        context.ValidatedCity(CityId).IsBeingRazed = Raze;
        return [];
    }
}
