namespace Unciv.Commands.City;

/// <summary>
/// "Use default promotions" checkbox of <c>ConstructionInfoTable</c>, shown only when the city already has an entry for the unit
/// (<c>getUnitShouldUseSavedPromotion != null</c>); <c>CityView.trySetUnitShouldUseSavedPromotion</c> stores the value.
/// </summary>
[CommandType("set-unit-saved-promotion")]
public sealed class SetUnitSavedPromotion : GameCommand
{
    public string CityId { get; init; } = "";
    public string Unit { get; init; } = "";
    public bool Value { get; init; }

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.TryChangeableCity(CityId, allowPuppet: true, out _, out var city, out var failure)) return failure;
        return city.UnitShouldUseSavedPromotion.ContainsKey(Unit)
            ? CommandValidation.Ok
            : CommandValidation.Fail(CommandErrors.InvalidTarget, $"{city.Name} has no saved promotion choice for \"{Unit}\"");
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        context.ValidatedCity(CityId).UnitShouldUseSavedPromotion[Unit] = Value;
        return [];
    }
}
