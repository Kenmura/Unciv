namespace Unciv.Commands.City;

/// <summary>
/// Remove one queue entry by position. Kotlin: the stop button of <c>CityConstructionsTable</c> (offered when <c>canCityBeChanged</c>), i.e.
/// <c>CityView.tryRemoveFromQueue(index, automatic = false)</c> then <c>tryReassignPopulation</c>. The index is positional; the
/// <c>automatic</c> flag is fixed to false for players.
/// </summary>
[CommandType("remove-from-queue")]
public sealed class RemoveFromQueue : GameCommand
{
    public string CityId { get; init; } = "";
    public int Index { get; init; }

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.TryChangeableCity(CityId, allowPuppet: false, out _, out var city, out var failure)) return failure;
        return Index >= 0 && Index < city.CityConstructions.ConstructionQueue.Count
            ? CommandValidation.Ok
            : CommandValidation.Fail(CommandErrors.InvalidTarget, $"There is no queue entry {Index}");
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        var city = context.ValidatedCity(CityId);
        city.CityConstructions.RemoveFromQueue(Index, automatic: false);
        city.ReassignPopulation();
        return [];
    }
}
