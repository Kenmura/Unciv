namespace Unciv.Commands.City;

/// <summary>
/// Re-run the citizen assignment. Kotlin: "Reset Citizens" of <c>CitizenManagementTable</c> (offered when <c>canCityBeChanged</c>) calls
/// <c>tryReassignPopulation(resetLocked = true)</c>; the flag keeps the engine's parameter for the other callers.
/// </summary>
[CommandType("reassign-population")]
public sealed class ReassignPopulation : GameCommand
{
    public string CityId { get; init; } = "";
    public bool ResetLocked { get; init; }

    protected override CommandValidation ValidateCommand(CommandContext context) =>
        context.TryChangeableCity(CityId, allowPuppet: false, out _, out _, out var failure) ? CommandValidation.Ok : failure;

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        context.ValidatedCity(CityId).ReassignPopulation(ResetLocked);
        return [];
    }
}
