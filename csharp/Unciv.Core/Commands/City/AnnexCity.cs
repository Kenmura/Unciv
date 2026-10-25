namespace Unciv.Commands.City;

/// <summary>
/// Annex a puppet city. Kotlin: the "Annex city" button of <c>CityScreen.updateAnnexAndRazeCityButton</c>, shown for puppets unless the civ has
/// <c>MayNotAnnexCities</c>, calling <c>CityView.tryAnnexCity</c>. The conquest alert response of the turn flow uses the same <c>City.AnnexCity</c>.
/// </summary>
[CommandType("annex-city")]
public sealed class AnnexCity : GameCommand
{
    public string CityId { get; init; } = "";

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.TryChangeableCity(CityId, allowPuppet: true, out var actor, out var city, out var failure)) return failure;
        if (!city.IsPuppet) return CommandValidation.Fail(CommandErrors.NotAllowed, $"{city.Name} is not a puppet city");
        return actor.HasUnique(Models.Ruleset.Unique.UniqueType.MayNotAnnexCities)
            ? CommandValidation.Fail(CommandErrors.NotAllowed, "This civilization may not annex cities")
            : CommandValidation.Ok;
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        context.ValidatedCity(CityId).AnnexCity();
        return [];
    }
}
