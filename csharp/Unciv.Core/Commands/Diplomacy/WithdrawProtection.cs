namespace Unciv.Commands.Diplomacy;

/// <summary>
/// Revoke our protection of a city-state. Kotlin: <c>CityStateDiplomacyTable.getRevokeProtectionButton</c>, enabled when
/// <c>otherCivCanWithdrawProtection(viewingCiv)</c>, then <c>removeProtectorCiv</c>.
/// </summary>
[CommandType("withdraw-protection")]
public sealed class WithdrawProtection : GameCommand
{
    public string CivId { get; init; } = "";

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.TryOtherCiv(CivId, CivKind.CityState, out var actor, out var other, out var failure)) return failure;
        return DiplomacyCommandChecks.CityState(other).OtherCivCanWithdrawProtection(actor)
            ? CommandValidation.Ok
            : CommandValidation.Fail(CommandErrors.NotAllowed, $"You cannot withdraw your protection of {CivId} now");
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        DiplomacyCommandChecks.CityState(context.ValidatedOther(CivId)).RemoveProtectorCiv(context.Actor);
        return [];
    }
}
