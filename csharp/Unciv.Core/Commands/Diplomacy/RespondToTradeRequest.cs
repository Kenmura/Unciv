using Unciv.Logic.Automation.Civilization;
using Unciv.Logic.Civilization;
using static Unciv.Logic.Civilization.Notification;

namespace Unciv.Commands.Diplomacy;

/// <summary>
/// Answer a trade another civ proposed (<see cref="ProposeTrade"/>). Kotlin: <c>TradePopup</c>, "Sounds good!" accepts through
/// <c>TradeLogic.acceptTrade()</c> and tells the proposer, "Not this time." runs <c>tradeRequest.decline</c> and tells the proposer. At most one
/// request per proposer is pending, so the proposer names it. It may arrive at any time while the request is pending, and the trade is checked
/// again when it is accepted (<c>TradeEvaluation.isTradeValid</c>, as <c>TurnManager.startTurn</c> does): a request that cannot be honoured any
/// more is <see cref="CommandErrors.StaleReference"/> and is dropped by the next turn start.
/// Deviation from the popup: the request leaves the list before the trade is applied (as <c>TradeAutomation</c> does for an AI) instead of at
/// close, so the proposer's resources are not counted twice while the trade is applied.
/// </summary>
[CommandType("respond-trade-request")]
public sealed class RespondToTradeRequest : GameCommand
{
    /// <summary>The civ that proposed the trade.</summary>
    public string CivId { get; init; } = "";

    public bool Accept { get; init; }

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.RequireActive(allowEnded: true).TryGet(out var actor, out var failure)) return failure;
        if (!context.Civ(CivId).TryGet(out var proposer, out failure)) return failure;
        var request = actor.TradeRequests.FirstOrDefault(it => it.RequestingCiv == CivId);
        if (request == null)
            return CommandValidation.Fail(CommandErrors.StaleReference, $"There is no trade request from {CivId}");
        if (!Accept) return CommandValidation.Ok;
        return proposer.IsDefeated() || !TradeEvaluationSeam.Of(actor).IsTradeValid(request.Trade, actor, proposer)
            ? CommandValidation.Fail(CommandErrors.StaleReference, $"The trade offered by {CivId} can no longer be honoured")
            : CommandValidation.Ok;
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        var actor = context.Actor;
        var proposer = context.ValidatedOther(CivId);
        var request = actor.TradeRequests.First(it => it.RequestingCiv == CivId);
        actor.TradeRequests.Remove(request);
        if (Accept)
        {
            var logic = TradeEvaluationSeam.NewLogic(actor, proposer);
            logic.CurrentTrade.Set(request.Trade);
            logic.AcceptTrade();
            proposer.AddNotification($"[{actor.CivName}] has accepted your trade request", NotificationCategory.Trade, actor.CivName, NotificationIcon.Trade);
        }
        else
        {
            request.Decline(actor);
            proposer.AddNotification($"[{actor.CivName}] has denied your trade request", NotificationCategory.Trade, actor.CivName, NotificationIcon.Trade);
        }
        return [];
    }
}
