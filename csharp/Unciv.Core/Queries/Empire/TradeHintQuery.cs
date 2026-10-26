using Unciv.Logic.Trade;
using Unciv.View;

namespace Unciv.Queries.Empire;

public sealed record TradeOfferRequest(string Name, string Type, int Amount);

// Hint: "accept" (they would take it), "counter-needed" (not as it stands, but adding gold from your treasury could do) or "refuse"
public sealed record TradeHintResult(string CivName, string Hint);

// T197: NEW feature, Kotlin never shows a human whether the AI would accept (the AI answers on its turn). Wraps
// TradeEvaluation.GetTradeAcceptability exactly as the AI's response to a trade request calls it (receiver's view, diplomatic gifts
// included), but only reports a bucket, never the number. Off unless GameParameters.TradeHintsEnabled, since repeated asks probe the
// AI valuation. T240: remote use is allowed because the civ is looked up among the viewer's known civs (EmpireQueryHelpers.KnownCiv, checked again against the view's known civs), the offers
// are the ones TradeOptionsQuery lists, the answer is only the bucket, and RemoteQueryHost gives the query an ask budget per bound civ
// (QueryExposure.AskBudget) so it cannot be used to bisect the valuation. Every offer must be in the available offers (TradeOptionsQuery) and within the amount available.
[QueryType("empire.TradeHint")]
public sealed record TradeHintQuery(string CivName, IReadOnlyList<TradeOfferRequest> OurOffers, IReadOnlyList<TradeOfferRequest> TheirOffers)
    : IQuery<TradeHintResult>
{
    public string Type => "empire.TradeHint";

    public TradeHintResult Run(GameView view)
    {
        var civ = view.CivView.Unwrap();
        if (!civ.GameInfo.GameParameters.TradeHintsEnabled)
            throw new QueryException(EmpireQueryCodes.HintsDisabled, "Trade hints are not enabled in this game");
        var other = EmpireQueryHelpers.KnownCiv(view, CivName);
        if (view.CivView.GetKnownCivs().All(it => it.GetCiv() != other)) // a defeated civ is not among the view's known civs: nobody left to trade with
            throw new QueryException(EmpireQueryCodes.UnknownCiv, $"You do not know a civilization \"{CivName}\"");
        if (other.IsHuman() || other.IsCityState)
            throw new QueryException(EmpireQueryCodes.NotAvailable, "Only AI major civilizations can be asked");
        var logic = new TradeLogic(civ, other);
        var trade = new Trade();
        foreach (var request in OurOffers ?? []) trade.OurOffers.Add(Resolve(request, logic.OurAvailableOffers));
        foreach (var request in TheirOffers ?? []) trade.TheirOffers.Add(Resolve(request, logic.TheirAvailableOffers));
        if (trade.OurOffers.Count == 0 && trade.TheirOffers.Count == 0)
            throw new QueryException(EmpireQueryCodes.BadRequest, "The trade is empty");

        var evaluation = TradeEvaluation.Default;
        if (!evaluation.IsTradeValid(trade, civ, other)) return new TradeHintResult(other.CivName, "refuse");
        int score = evaluation.GetTradeAcceptability(trade.Reverse(), other, civ, includeDiplomaticGifts: true);
        string hint;
        if (score >= 0) hint = "accept";
        else if (score == int.MinValue) hint = "refuse";
        else hint = -(long)score <= civ.Gold ? "counter-needed" : "refuse";
        return new TradeHintResult(other.CivName, hint);
    }

    private static TradeOffer Resolve(TradeOfferRequest request, TradeOffersList available)
    {
        if (request == null || !Enum.TryParse<TradeOfferType>(request.Type, out var type) || !Enum.IsDefined(type))
            throw new QueryException(EmpireQueryCodes.BadRequest, "Unknown trade offer type");
        var match = available.FirstOrDefault(it => it.Name == request.Name && it.Type == type && it.IsTradable());
        if (match == null)
            throw new QueryException(EmpireQueryCodes.BadRequest, $"\"{request.Name}\" is not available to trade");
        if (request.Amount <= 0 || request.Amount > match.Amount)
            throw new QueryException(EmpireQueryCodes.BadRequest, $"Amount of \"{request.Name}\" must be 1 to {match.Amount}");
        return match.Copy(amount: request.Amount);
    }
}
