namespace Unciv.Commands.Diplomacy;

/// <summary>
/// Pledge to protect a city-state. Kotlin: <c>CityStateDiplomacyTable.getPledgeToProtectButton</c>, shown while the city-state's status toward us is
/// not Protector and enabled when <c>otherCivCanPledgeProtection(viewingCiv)</c>, then <c>addProtectorCiv</c>.
/// </summary>
[CommandType("pledge-protection")]
public sealed class PledgeProtection : GameCommand
{
    public string CivId { get; init; } = "";

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.TryOtherCiv(CivId, CivKind.CityState, out var actor, out var other, out var failure)) return failure;
        return DiplomacyCommandChecks.CityState(other).OtherCivCanPledgeProtection(actor)
            ? CommandValidation.Ok
            : CommandValidation.Fail(CommandErrors.NotAllowed, $"You cannot pledge to protect {CivId} now");
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        DiplomacyCommandChecks.CityState(context.ValidatedOther(CivId)).AddProtectorCiv(context.Actor);
        return [];
    }
}
