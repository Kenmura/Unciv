using Unciv.Logic.Trade;
using CivilizationClass = Unciv.Logic.Civilization.Civilization;

namespace Unciv.View;

// Ported from core/src/com/unciv/view/TradeView.kt (T138). Stripped: the purity annotations.
// TradeOffer.getOfferText stays unported (UI-side text, see TradeOffer.cs).

/// <summary>View of a trade negotiation between <c>civ</c> (always the viewer's own civ) and <c>otherCiv</c>.
/// Always built fresh, same as <see cref="TradeLogic"/> itself - staged offers live in this instance only.</summary>
public class TradeView : OwnedView<CivilizationClass>
{
    private readonly CivilizationClass civ;
    private readonly CivilizationClass otherCiv;
    private readonly TradeLogic tradeLogic;

    public TradeView(CivilizationClass civ, CivilizationClass otherCiv, GameView gameView)
        : base(civ, civ, false, gameView)
    {
        this.civ = civ;
        this.otherCiv = otherCiv;
        tradeLogic = new TradeLogic(civ, otherCiv);
    }

    internal override CivilizationClass Owner() => civ;

    // Data retrieval - lists are the live mutable instances, callers may add/remove offers directly
    public TradeOffersList OurAvailableOffers() => tradeLogic.OurAvailableOffers;

    public TradeOffersList TheirAvailableOffers() => tradeLogic.TheirAvailableOffers;

    public TradeOffersList OurStagedOffers() => tradeLogic.CurrentTrade.OurOffers;

    public TradeOffersList TheirStagedOffers() => tradeLogic.CurrentTrade.TheirOffers;

    public bool HasPendingOfferFromUs() => otherCiv.TradeRequests.Any(it => it.RequestingCiv == civ.CivId);

    // Actions - staging
    public bool SetStagedTrade(Trade trade)
    {
        tradeLogic.CurrentTrade.Set(trade);
        return true;
    }

    /// <summary>Restores a trade we already sent to <c>otherCiv</c> (if any) into staging, so it can be re-displayed or retracted.</summary>
    public bool TryLoadOurPendingOffer()
    {
        var existing = otherCiv.TradeRequests.FirstOrDefault(it => it.RequestingCiv == civ.CivId);
        if (existing == null) return false;
        tradeLogic.CurrentTrade.Set(existing.Trade.Reverse());
        return true;
    }

    // Actions - proposing
    public bool TryProposeStagedTrade()
    {
        if (tradeLogic.CurrentTrade.OurOffers.Count == 0 && tradeLogic.CurrentTrade.TheirOffers.Count == 0) return false;
        otherCiv.TradeRequests.Add(new TradeRequest(civ.CivId, tradeLogic.CurrentTrade.Reverse()));
        civ.Cache.UpdateCivResources();
        return true;
    }

    public bool TryRetractOffer()
    {
        if (!HasPendingOfferFromUs()) return false;
        otherCiv.TradeRequests.RemoveAll(it => it.RequestingCiv == civ.CivId);
        civ.Cache.UpdateCivResources();
        return true;
    }
}
