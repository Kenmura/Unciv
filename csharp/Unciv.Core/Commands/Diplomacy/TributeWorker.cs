namespace Unciv.Commands.Diplomacy;

/// <summary>
/// Demand a worker from a city-state. Kotlin: <c>CityStateDiplomacyTable.getDemandTributeTable</c>, "Take worker (-50 Influence)", enabled when
/// <c>getTributeWillingness(viewingCiv, demandingWorker = true)</c> is not negative, then <c>cityStateFunctions.tributeWorker</c>, which picks the
/// unit with the state's seeded RNG. Like Kotlin, nothing happens (not even the influence loss) when no worker unit can be built.
/// </summary>
[CommandType("tribute-worker")]
public sealed class TributeWorker : GameCommand
{
    public string CivId { get; init; } = "";

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.TryOtherCiv(CivId, CivKind.CityState, out var actor, out var other, out var failure)) return failure;
        if (actor.IsAtWarWith(other))
            return CommandValidation.Fail(CommandErrors.NotAllowed, $"You are at war with {CivId}");
        return DiplomacyCommandChecks.CityState(other).GetTributeWillingness(actor, demandingWorker: true) < 0
            ? CommandValidation.Fail(CommandErrors.NotAllowed, $"{CivId} is not willing to give a worker")
            : CommandValidation.Ok;
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        DiplomacyCommandChecks.CityState(context.ValidatedOther(CivId)).TributeWorker(context.Actor);
        return [];
    }
}
