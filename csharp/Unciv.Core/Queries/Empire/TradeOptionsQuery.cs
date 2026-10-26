using Unciv.Logic.Civilization;
using Unciv.Logic.Trade;
using Unciv.View;

namespace Unciv.Queries.Empire;

// Name: Offer name (resource, city id, civ id, treaty constant)
// Type: TradeOfferType name
// Amount: Quantity, or the gold available; 0 for a peace proposal that is not currently possible
// Duration: Turns the deal lasts, -1 for immediate offers
// Tradable: false when the offer is shown but cannot be staged (Amount 0)
// DisplayName: C022: what to show for an offer whose Name is an id: the city's name for a City offer, the civ's name for an Introduction or war declaration;
//   null for every other offer (Name is already the text)
public sealed record TradeOfferEntry(string Name, string Type, int Amount, int Duration, bool Tradable, string? DisplayName = null);

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
        var game = view.Unwrap();
        var civ = view.CivView.Unwrap();
        TradeOfferEntry Named(TradeOffer offer) => Entry(offer, DisplayNameOf(game, civ, other, offer));
        return new TradeOptionsResult(other.CivName, trade.OurAvailableOffers().Select(Named).ToList(),
            trade.TheirAvailableOffers().Select(Named).ToList(), trade.HasPendingOfferFromUs());
    }

    internal static TradeOfferEntry Entry(TradeOffer offer, string? displayName = null) =>
        new(offer.Name, offer.Type.ToString(), offer.Amount, offer.Duration, offer.IsTradable(), displayName);

    internal static string? DisplayNameOf(Unciv.Logic.GameInfo game, Civilization civ, Civilization other, TradeOffer offer) => offer.Type switch
    {
        TradeOfferType.City => civ.Cities.Concat(other.Cities).FirstOrDefault(it => it.Id == offer.Name)?.Name,
        TradeOfferType.Introduction or TradeOfferType.WarDeclaration or TradeOfferType.PeaceProposal => game.GetCivilizationOrNull(offer.Name)?.CivName,
        _ => null,
    };
}
