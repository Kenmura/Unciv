namespace Unciv.Commands.Diplomacy;

/// <summary>
/// Demand gold from a city-state. Kotlin: <c>CityStateDiplomacyTable.getDemandTributeTable</c>, "Take [gold] gold (-15 Influence)", offered
/// while not at war and enabled when <c>getTributeWillingness(viewingCiv, demandingWorker = false)</c> is not negative, then
/// <c>cityStateFunctions.tributeGold</c>.
/// </summary>
[CommandType("tribute-gold")]
public sealed class TributeGold : GameCommand
{
    public string CivId { get; init; } = "";

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.TryOtherCiv(CivId, CivKind.CityState, out var actor, out var other, out var failure)) return failure;
        if (actor.IsAtWarWith(other))
            return CommandValidation.Fail(CommandErrors.NotAllowed, $"You are at war with {CivId}");
        return DiplomacyCommandChecks.CityState(other).GetTributeWillingness(actor, demandingWorker: false) < 0
            ? CommandValidation.Fail(CommandErrors.NotAllowed, $"{CivId} is not willing to pay tribute")
            : CommandValidation.Ok;
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        DiplomacyCommandChecks.CityState(context.ValidatedOther(CivId)).TributeGold(context.Actor);
        return [];
    }
}
