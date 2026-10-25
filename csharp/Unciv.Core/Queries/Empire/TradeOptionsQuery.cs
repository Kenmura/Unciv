using Unciv.Logic.Trade;
using Unciv.View;

namespace Unciv.Queries.Empire;

// Name: Offer name (resource, city id, civ id, treaty constant)
// Type: TradeOfferType name
// Amount: Quantity, or the gold available; 0 for a peace proposal that is not currently possible
// Duration: Turns the deal lasts, -1 for immediate offers
// Tradable: false when the offer is shown but cannot be staged (Amount 0)
public sealed record TradeOfferEntry(string Name, string Type, int Amount, int Duration, bool Tradable);

public sealed record TradeOptionsResult(string CivName, IReadOnlyList<TradeOfferEntry> Ours, IReadOnlyList<TradeOfferEntry> Theirs,
    bool HasPendingOfferFromUs);

// T197: everything that could be put on each side of a trade with a met civ (TradeLogic Our/TheirAvailableOffers), in the engine's order.
[QueryType("empire.TradeOptions")]
public sealed record TradeOptionsQuery(string CivName) : IQuery<TradeOptionsResult>
{
    public string Type => "empire.TradeOptions";

    public TradeOptionsResult Run(GameView view)
    {
        var other = EmpireQueryHelpers.KnownCiv(view, CivName);
        var trade = view.CivView.GetTradeView(view.GetForeignCivView(other));
        return new TradeOptionsResult(other.CivName, trade.OurAvailableOffers().Select(Entry).ToList(),
            trade.TheirAvailableOffers().Select(Entry).ToList(), trade.HasPendingOfferFromUs());
    }

    internal static TradeOfferEntry Entry(TradeOffer offer) =>
        new(offer.Name, offer.Type.ToString(), offer.Amount, offer.Duration, offer.IsTradable());
}
