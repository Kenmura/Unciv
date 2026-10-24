using Unciv.Json;

namespace Unciv.Logic.Trade;

// core/src/com/unciv/logic/trade/TradeOffersList.kt
// Kotlin overrides ArrayList.add only: AddRange (addAll, Kotlin's `+=`) and Insert stay plain bulk operations that do NOT merge,
// and `Add` merges only through this type, as Kotlin does through ArrayList<TradeOffer> (never upcast to List here).

public class TradeOffersList : List<TradeOffer>, IsPartOfGameInfoSerialization
{
    /// <summary>Adds the offer, or merges its amount into the existing offer with the same name and type (removing it at 0).</summary>
    public new bool Add(TradeOffer element)
    {
        var equivalentOffer = this.FirstOrDefault(it => it.Name == element.Name && it.Type == element.Type);
        if (equivalentOffer == null)
        {
            base.Add(element);
            return true;
        }
        equivalentOffer.Amount += element.Amount;
        if (equivalentOffer.Amount == 0) Remove(equivalentOffer);
        return true;
    }

    public TradeOffersList Without(TradeOffersList otherTradeOffersList)
    {
        var tradeOffersListCopy = new TradeOffersList();
        foreach (var offer in this) tradeOffersListCopy.Add(offer.Copy());
        foreach (var offer in otherTradeOffersList) tradeOffersListCopy.Add(offer.Copy(amount: -offer.Amount));
        return tradeOffersListCopy;
    }
}
