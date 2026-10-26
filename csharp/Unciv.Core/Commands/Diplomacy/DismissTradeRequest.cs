namespace Unciv.Commands.Diplomacy;

/// <summary>
/// "How about something else...": close the trade popup without an answer and open the diplomacy screen with the request as the staged trade.
/// Kotlin: <c>TradePopup.close</c> removes the request from <c>tradeRequests</c>; the proposer is not told and no decline flag is set. The
/// counter proposal is a normal <see cref="ProposeTrade"/> afterwards.
/// </summary>
[CommandType("dismiss-trade-request")]
public sealed class DismissTradeRequest : GameCommand
{
    /// <summary>The civ that proposed the trade.</summary>
    public string CivId { get; init; } = "";

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.RequireActive(allowEnded: true).TryGet(out var actor, out var failure)) return failure;
        if (!context.Civ(CivId).TryGet(out _, out failure)) return failure;
        return actor.TradeRequests.Any(it => it.RequestingCiv == CivId)
            ? CommandValidation.Ok
            : CommandValidation.Fail(CommandErrors.StaleReference, $"There is no trade request from {CivId}");
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        var actor = context.Actor;
        actor.TradeRequests.RemoveAll(it => it.RequestingCiv == CivId);
        return [];
    }
}
