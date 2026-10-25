using Unciv.Commands;
using Unciv.Commands.Diplomacy;
using Unciv.Logic.Trade;
using Unciv.View;

namespace Unciv.Queries.Empire;

// C021: which diplomacy buttons the screen offers enabled for one civ. Each flag is the validation verdict of the very command the button submits, so the
// screen and the engine can never disagree. Gifts lists the gold amounts a gift command would accept now (ascending, as GiftGold.Amounts).
// The city-state flags are false for a major civ and the major-civ flags (Denounce, OfferPeace) are false for a city-state, because the commands refuse the wrong kind.
public sealed record DiplomacyActionsResult(string CivName, bool DeclareWar, bool Denounce, bool OfferPeace, bool NegotiateCityStatePeace,
    bool PledgeProtection, bool WithdrawProtection, bool TributeGold, bool TributeWorker, IReadOnlyList<int> Gifts);

[QueryType("empire.DiplomacyActions")]
public sealed record DiplomacyActionsQuery(string CivName) : IQuery<DiplomacyActionsResult>
{
    public string Type => "empire.DiplomacyActions";

    /// <summary>The peace proposal OfferPeace is judged by and the screen sends: our peace treaty offered, which the engine mirrors on their side.</summary>
    public static ProposeTrade PeaceOffer(string civId) =>
        new() { CivId = civId, OurOffers = [new TradeOfferSpec(Constants.PeaceTreaty, nameof(TradeOfferType.Treaty))] };

    public DiplomacyActionsResult Run(GameView view)
    {
        var civ = view.CivView.Unwrap();
        var other = EmpireQueryHelpers.KnownCiv(view, CivName);
        var game = (Unciv.Logic.GameInfo)civ.GameInfo;
        string id = other.CivId;
        bool Legal(GameCommand command) => command.Validate(game, civ.CivId).IsValid;
        var peace = DiplomacyActionsQuery.PeaceOffer(id);
        return new DiplomacyActionsResult(other.CivName, Legal(new DeclareWar { CivId = id }), Legal(new Denounce { CivId = id }), Legal(peace),
            Legal(new NegotiateCityStatePeace { CivId = id }), Legal(new PledgeProtection { CivId = id }), Legal(new WithdrawProtection { CivId = id }),
            Legal(new TributeGold { CivId = id }), Legal(new TributeWorker { CivId = id }),
            GiftGold.Amounts.Where(amount => Legal(new GiftGold { CivId = id, Amount = amount })).ToList());
    }
}
