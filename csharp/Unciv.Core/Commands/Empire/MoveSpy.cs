namespace Unciv.Commands.Empire;

/// <summary>
/// Move a spy to a city or back to the hideout. Kotlin: <c>EspionageScreen</c> (spy alive, espionage enabled, location is the hideout or an
/// explored city where the civ has no spy yet and that is not the spy's current location) then <c>spy.moveTo</c>.
/// </summary>
[CommandType("move-spy")]
public sealed class MoveSpy : GameCommand
{
    /// <summary>The spy's name, unique within the civ.</summary>
    public string Spy { get; init; } = "";

    /// <summary>The target city, or null for the hideout.</summary>
    public string? CityId { get; init; }

    /// <summary>BNW (B020): station the spy as a diplomat (needs the capital of another major civ).</summary>
    public bool Diplomat { get; init; }

    private Logic.City.City? FindCity(CommandContext context) =>
        context.Game.GetCities().FirstOrDefault(it => it.Id == CityId);

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.RequireActive().TryGet(out var actor, out var failure)) return failure;
        if (!context.Game.IsEspionageEnabled())
            return CommandValidation.Fail(CommandErrors.NotAllowed, "Espionage is disabled");
        var spy = actor.EspionageManager.SpyList.FirstOrDefault(it => it.Name == Spy);
        if (spy == null)
            return CommandValidation.Fail(CommandErrors.InvalidTarget, $"There is no spy \"{Spy}\"");
        if (!spy.IsAlive())
            return CommandValidation.Fail(CommandErrors.NotAllowed, $"{Spy} is dead");
        var city = CityId == null ? null : FindCity(context);
        if (CityId != null)
        {
            if (city == null) return CommandValidation.Fail(CommandErrors.UnknownCity, $"There is no city \"{CityId}\"");
            if (!actor.HasExplored(city.GetCenterTile()))
                return CommandValidation.Fail(CommandErrors.InvalidTarget, $"{city.Name} has not been explored");
            if (spy.GetCityOrNull() != city && city.Espionage.HasSpyOf(actor)) // the same spy may change between spy and diplomat
                return CommandValidation.Fail(CommandErrors.NotAllowed, $"A spy is already in {city.Name}");
        }
        if (Diplomat && (city == null || !spy.CanBeDiplomatIn(city)))
            return CommandValidation.Fail(CommandErrors.NotAllowed, "A diplomat needs the capital of another major civilization in a game with the World Congress");
        return spy.GetCityOrNull() == city && spy.IsDiplomat == Diplomat
            ? CommandValidation.Fail(CommandErrors.NotAllowed, $"{Spy} is already there")
            : CommandValidation.Ok;
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        var spy = context.Actor.EspionageManager.SpyList.First(it => it.Name == Spy);
        spy.MoveTo(CityId == null ? null : FindCity(context), Diplomat);
        return [];
    }
}
