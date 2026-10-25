using Unciv.Commands.Events;

namespace Unciv.Commands.Diplomacy;

/// <summary>
/// Take back the offer we sent to a civ. Kotlin: the "Retract offer" button of <c>TradeTable</c> (shown while
/// <c>tradeView.hasPendingOfferFromUs()</c>) then <c>tradeView.tryRetractOffer()</c>.
/// </summary>
[CommandType("retract-trade")]
public sealed class RetractTrade : GameCommand
{
    public string CivId { get; init; } = "";

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.TryOtherCiv(CivId, CivKind.Major, out var actor, out var other, out var failure)) return failure;
        return other.TradeRequests.Any(it => it.RequestingCiv == actor.CivId)
            ? CommandValidation.Ok
            : CommandValidation.Fail(CommandErrors.StaleReference, $"You have no pending offer with {CivId}");
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        var actor = context.Actor;
        context.ValidatedOther(CivId).TradeRequests.RemoveAll(it => it.RequestingCiv == actor.CivId);
        actor.Cache.UpdateCivResources();
        return [new TradeWithdrawn(actor.CivId, CivId)];
    }
}
