using Unciv.Logic.Trade;

namespace Unciv.Commands.Events;

// T181 (design/SERVER.md section 3, trade row): private-channel events. A trade proposal is live data for the recipient only; the proposer
// already knows its own offer. Participants() names the recipient alone, and EventFilter delivers to participants only, so a third civ
// (human or not) never receives either event. Offers are plain facts: no AI valuation or willingness number is ever part of an event.

/// <summary>Plain-data copies of trade offers.</summary>
public static class PrivateTradeFacts
{
    public static IReadOnlyList<TradeOfferFact> Of(IEnumerable<TradeOffer> offers) =>
        offers.Select(it => new TradeOfferFact(it.Name, it.Type, it.Amount, it.Duration)).ToList();
}

/// <summary><paramref name="FromCivId"/> proposed a trade to <paramref name="ToCivId"/>: <paramref name="Offered"/> is what the proposer gives, <paramref name="Requested"/> what it asks. Delivered to the recipient only.</summary>
[EventType("trade-requested", EventAudience.Participants)]
public sealed record TradeRequested(string FromCivId, string ToCivId, IReadOnlyList<TradeOfferFact> Offered, IReadOnlyList<TradeOfferFact> Requested) : EngineEvent
{
    public override IEnumerable<string> Participants() => [ToCivId];
}

/// <summary><paramref name="FromCivId"/> took back its pending offer to <paramref name="ToCivId"/>: the recipient deletes it from its view. Delivered to the recipient only.</summary>
[EventType("trade-withdrawn", EventAudience.Participants)]
public sealed record TradeWithdrawn(string FromCivId, string ToCivId) : EngineEvent
{
    public override IEnumerable<string> Participants() => [ToCivId];
}
