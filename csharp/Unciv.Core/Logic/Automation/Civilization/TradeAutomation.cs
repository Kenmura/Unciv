using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Logic.Trade;
using Unciv.Utils;
using static Unciv.Logic.Civilization.Notification;
using CivilizationClass = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Logic.Automation.Civilization;

// Ported from core/src/com/unciv/logic/automation/civilization/TradeAutomation.kt (T088).
// - TradeEvaluation (T089) is reached through TradeEvaluationSeam (TradeAutomationSeams.cs).
// - Kotlin HashMap<TradeOffer, Int> potentialAsks/counterofferAsks: TradeOffer.hashCode includes the identity hash of the enum, so there is no
//   reproducible JVM order; LinkedHashMap, insertion order (the tie-breaker of minBy/maxBy). The key hash includes the mutable amount, as in Kotlin.
// - `civInfo.tradeRequests` replaced wholesale by Kotlin `clear()`; the notifications keep Kotlin's argument order (the denial one swaps icon and name).

public static class TradeAutomation
{
    public static void RespondToTradeRequests(CivilizationClass civInfo, bool tradeAndChangeState)
    {
        foreach (var tradeRequest in civInfo.TradeRequests.ToList())
        {
            var otherCiv = civInfo.GameInfo.GetCivilization(tradeRequest.RequestingCiv);
            // Treat 'no trade' state as if all trades are invalid - thus AIs will not update its "turns to offer"
            if (!tradeAndChangeState || !TradeEvaluationSeam.Of(civInfo).IsTradeValid(tradeRequest.Trade, civInfo, (CivilizationClass)otherCiv))
                continue;

            var other = (CivilizationClass)otherCiv;
            var tradeLogic = TradeEvaluationSeam.NewLogic(civInfo, other);
            tradeLogic.CurrentTrade.Set(tradeRequest.Trade);
            /* We need to remove this here, so that if the trade is accepted, the updateDetailedCivResources()
             * in tradeLogic.acceptTrade() will not consider *both* the trade *and the trade offer as decreasing the
             * amount of available resources, since that will lead to "Our proposed trade is no longer valid" if we try to offer
             * the same resource to ANOTHER civ in this turn. Complicated!
             */
            civInfo.TradeRequests.Remove(tradeRequest);
            if (TradeEvaluationSeam.Of(civInfo).IsTradeAcceptable(tradeLogic.CurrentTrade, civInfo, other))
            {
                tradeLogic.AcceptTrade();
                other.AddNotification($"[{civInfo.CivName}] has accepted your trade request", NotificationCategory.Trade, NotificationIcon.Trade, civInfo.CivName);
            }
            else
            {
                var counteroffer = GetCounteroffer(civInfo, tradeRequest);
                if (counteroffer != null)
                {
                    other.AddNotification($"[{civInfo.CivName}] has made a counteroffer to your trade request", NotificationCategory.Trade, NotificationIcon.Trade, civInfo.CivName);
                    other.TradeRequests.Add(counteroffer);
                }
                else
                {
                    other.AddNotification($"[{civInfo.CivName}] has denied your trade request", NotificationCategory.Trade, civInfo.CivName, NotificationIcon.Trade);
                    tradeRequest.Decline(civInfo);
                }
            }
        }
        civInfo.TradeRequests.Clear();
    }

    /// <summary>
    /// A TradeRequest with the same ourOffers as <paramref name="tradeRequest"/> but with enough theirOffers
    /// added to make the deal acceptable. Will find a valid counteroffer if any exist, but is not
    /// guaranteed to find the best or closest one.
    /// </summary>
    private static TradeRequest? GetCounteroffer(CivilizationClass civInfo, TradeRequest tradeRequest)
    {
        var otherCiv = (CivilizationClass)civInfo.GameInfo.GetCivilization(tradeRequest.RequestingCiv);
        // AIs counteroffering each other could be problematic if they ping-pong back and forth forever
        // If this happens, that means our trade automation doesn't settle into an equilibrium that's favourable to both parties, so that should be updated when observed
        var evaluation = TradeEvaluationSeam.Of(civInfo);
        var deltaInOurFavor = evaluation.GetTradeAcceptability(tradeRequest.Trade, civInfo, otherCiv, true);
        if (deltaInOurFavor > 0) deltaInOurFavor = KotlinMath.FloatToInt(deltaInOurFavor / 1.1f); // They seem very interested in this deal, let's push it a bit.
        var tradeLogic = TradeEvaluationSeam.NewLogic(civInfo, otherCiv);

        tradeLogic.CurrentTrade.Set(tradeRequest.Trade);

        // What do they have that we would want?
        var potentialAsks = new LinkedHashMap<TradeOffer, int>();
        var counterofferAsks = new LinkedHashMap<TradeOffer, int>();
        var counterofferGifts = new List<TradeOffer>();

        FillPotentialAsks(potentialAsks, tradeLogic, tradeRequest, civInfo, otherCiv, evaluation);

        while (potentialAsks.Count != 0 && deltaInOurFavor < 0)
        {
            // Keep adding their worst offer until we get above the threshold
            var offerToAdd = MinByValue(potentialAsks);
            deltaInOurFavor += offerToAdd.Value;
            counterofferAsks.Put(offerToAdd.Key, offerToAdd.Value);
            potentialAsks.Remove(offerToAdd.Key);
        }
        if (deltaInOurFavor < 0)
            return null; // We couldn't get a good enough deal

        // At this point we are sure to find a good counteroffer
        while (deltaInOurFavor > 0)
        {
            // Now remove the best offer valued below delta until the deal is barely acceptable
            var offerToRemove = MaxByValue(counterofferAsks.Where(it => it.Value <= deltaInOurFavor));
            if (offerToRemove == null) break; // Nothing more can be removed, at least en bloc
            deltaInOurFavor -= offerToRemove.Value.Value;
            counterofferAsks.Remove(offerToRemove.Value.Key);
        }

        // Only ask for enough of each resource to get maximum price
        foreach (var ask in counterofferAsks.Keys.Where(it => it.Type == TradeOfferType.Luxury_Resource || it.Type == TradeOfferType.Strategic_Resource).ToList())
        {
            // Remove 1 amount as long as doing so does not change the price
            var originalValue = counterofferAsks[ask];
            while (ask.Amount > 1
                && originalValue == evaluation.EvaluateBuyCostWithInflation(
                    new TradeOffer(ask.Name, ask.Type, ask.Amount - 1, ask.Duration),
                    civInfo, otherCiv, tradeRequest.Trade))
            {
                ask.Amount--;
            }
        }

        deltaInOurFavor = AdjustGoldAsks(counterofferAsks, deltaInOurFavor, evaluation, civInfo, otherCiv, tradeRequest);

        // If the delta is still very in our favor consider sweetening the pot with some gold
        deltaInOurFavor = SweetenWithGold(counterofferAsks, counterofferGifts, deltaInOurFavor, tradeLogic, evaluation, civInfo, otherCiv, tradeRequest);

        tradeLogic.CurrentTrade.TheirOffers.AddRange(counterofferAsks.Keys);
        tradeLogic.CurrentTrade.OurOffers.AddRange(counterofferGifts);

        // Trades reversed, because when *they* get it then the 'ouroffers' become 'theiroffers'
        return new TradeRequest(civInfo.CivId, tradeLogic.CurrentTrade.Reverse());
    }

    /// <summary>What do they have that we would want? Fills <paramref name="potentialAsks"/> with the offers worth something to us.</summary>
    private static void FillPotentialAsks(LinkedHashMap<TradeOffer, int> potentialAsks, TradeLogic tradeLogic, TradeRequest tradeRequest,
        CivilizationClass civInfo, CivilizationClass otherCiv, ITradeAutomationEvaluation evaluation)
    {
        foreach (var offer in tradeLogic.TheirAvailableOffers)
        {
            if ((offer.Type == TradeOfferType.Gold || offer.Type == TradeOfferType.Gold_Per_Turn)
                && tradeRequest.Trade.OurOffers.Any(it => it.Type == offer.Type))
                continue; // Don't want to counteroffer straight gold for gold, that's silly
            if (!offer.IsTradable())
                continue; // For example resources gained by trade or CS
            if (offer.Type == TradeOfferType.City)
                continue; // Players generally don't want to give up their cities, and they might misclick
            if (offer.Type == TradeOfferType.Luxury_Resource)
                continue; // Don't ask for luxuries as counteroffer, players likely don't want to sell them if they didn't offer them already
            if (tradeLogic.CurrentTrade.TheirOffers.Any(it => it.Type == offer.Type && it.Name == offer.Name))
                continue; // So you don't get double offers of open borders declarations of war etc.
            if (offer.Type == TradeOfferType.Treaty)
                continue; // Don't try to counter with a defensive pact or research pact

            var value = evaluation.EvaluateBuyCostWithInflation(offer, civInfo, otherCiv, tradeRequest.Trade);
            if (value > 0)
                potentialAsks.Put(offer, value);
        }
    }

    /// <summary>Adjust any gold asked for. Returns the remaining <c>deltaInOurFavor</c>.</summary>
    private static int AdjustGoldAsks(LinkedHashMap<TradeOffer, int> counterofferAsks, int deltaInOurFavor, ITradeAutomationEvaluation evaluation,
        CivilizationClass civInfo, CivilizationClass otherCiv, TradeRequest tradeRequest)
    {
        var toRemove = new List<TradeOffer>();
        foreach (var goldAsk in counterofferAsks.Keys
            .Where(it => it.Type == TradeOfferType.Gold_Per_Turn || it.Type == TradeOfferType.Gold)
            .OrderByDescending(it => (int)it.Type).ToList()) // Do GPT first
        {
            var valueOfOne = evaluation.EvaluateBuyCostWithInflation(new TradeOffer(goldAsk.Name, goldAsk.Type, 1, goldAsk.Duration), civInfo, otherCiv, tradeRequest.Trade);
            var amountCanBeRemoved = deltaInOurFavor / valueOfOne;
            if (amountCanBeRemoved >= goldAsk.Amount)
            {
                deltaInOurFavor -= counterofferAsks[goldAsk];
                toRemove.Add(goldAsk);
            }
            else
            {
                deltaInOurFavor -= valueOfOne * amountCanBeRemoved;
                goldAsk.Amount -= amountCanBeRemoved;
            }
        }
        return deltaInOurFavor;
    }

    /// <summary>If the delta is still very in our favor consider sweetening the pot with some gold. Returns the remaining <c>deltaInOurFavor</c>.</summary>
    private static int SweetenWithGold(LinkedHashMap<TradeOffer, int> counterofferAsks, List<TradeOffer> counterofferGifts, int deltaInOurFavor,
        TradeLogic tradeLogic, ITradeAutomationEvaluation evaluation, CivilizationClass civInfo, CivilizationClass otherCiv, TradeRequest tradeRequest)
    {
        if (deltaInOurFavor >= 100)
        {
            deltaInOurFavor = (deltaInOurFavor * 2) / 3; // Only compensate some of it though, they're the ones asking us
            // First give some GPT, then lump sum - but only if they're not already offering the same
            foreach (var ourGold in tradeLogic.OurAvailableOffers
                .Where(it => it.IsTradable() && (it.Type == TradeOfferType.Gold || it.Type == TradeOfferType.Gold_Per_Turn))
                .OrderByDescending(it => (int)it.Type).ToList())
            {
                if (!tradeLogic.CurrentTrade.TheirOffers.Any(it => it.Type == ourGold.Type) &&
                    !counterofferAsks.Keys.Any(it => it.Type == ourGold.Type))
                {
                    var valueOfOne = evaluation.EvaluateSellCostWithInflation(new TradeOffer(ourGold.Name, ourGold.Type, 1, ourGold.Duration), civInfo, otherCiv, tradeRequest.Trade);
                    var amountToGive = Math.Min(deltaInOurFavor / valueOfOne, ourGold.Amount);
                    deltaInOurFavor -= amountToGive * valueOfOne;
                    if (amountToGive > 0)
                    {
                        counterofferGifts.Add(
                            new TradeOffer(
                                ourGold.Name,
                                ourGold.Type,
                                amountToGive,
                                ourGold.Duration));
                    }
                }
            }
        }
        return deltaInOurFavor;
    }

    /// <summary>Kotlin <c>minByOrNull { it.value }!!</c>: the first minimum in iteration order.</summary>
    private static KeyValuePair<TradeOffer, int> MinByValue(LinkedHashMap<TradeOffer, int> map)
    {
        KeyValuePair<TradeOffer, int>? best = null;
        foreach (var entry in map)
            if (best == null || entry.Value < best.Value.Value) best = entry;
        return best.NotNull();
    }

    /// <summary>Kotlin <c>maxByOrNull { it.value }</c>: the first maximum in iteration order, null when empty.</summary>
    private static KeyValuePair<TradeOffer, int>? MaxByValue(IEnumerable<KeyValuePair<TradeOffer, int>> entries)
    {
        KeyValuePair<TradeOffer, int>? best = null;
        foreach (var entry in entries)
            if (best == null || entry.Value > best.Value.Value) best = entry;
        return best;
    }

    public static void ExchangeLuxuries(CivilizationClass civInfo)
    {
        var knownCivs = civInfo.GetKnownCivs();

        // Player trades are... more complicated.
        // When the AI offers a trade, it's not immediately accepted,
        // so what if it thinks that it has a spare luxury and offers it to two human players?
        // What's to stop the AI "nagging" the player to accept a luxury trade?
        // We should A. add some sort of timer (20? 30 turns?) between luxury trade requests if they're denied - see DeclinedLuxExchange
        // B. have a way for the AI to keep track of the "pending offers" - see DiplomacyManager.resourcesFromTrade

        foreach (var otherCiv in knownCivs.Where(it =>
            it.IsMajorCiv() && !it.IsAtWarWith(civInfo)
                && !civInfo.GetDiplomacyManager(it).NotNull().HasFlag(DiplomacyFlags.DeclinedLuxExchange)).ToList())
        {
            var isEnemy = civInfo.GetDiplomacyManager(otherCiv).NotNull().IsRelationshipLevelLE(RelationshipLevel.Enemy);
            if (isEnemy || otherCiv.TradeRequests.Any(it => it.RequestingCiv == civInfo.CivId))
                continue;

            var trades = PotentialLuxuryTrades(civInfo, otherCiv);
            foreach (var trade in trades)
            {
                var tradeRequest = new TradeRequest(civInfo.CivId, trade.Reverse());
                otherCiv.TradeRequests.Add(tradeRequest);
            }
        }
    }

    private static List<Trade.Trade> PotentialLuxuryTrades(CivilizationClass civInfo, CivilizationClass otherCivInfo)
    {
        var tradeLogic = TradeEvaluationSeam.NewLogic(civInfo, otherCivInfo);
        var ourTradableLuxuryResources = tradeLogic.OurAvailableOffers
            .Where(it => it.Type == TradeOfferType.Luxury_Resource && it.Amount > 1).ToList();
        var theirTradableLuxuryResources = tradeLogic.TheirAvailableOffers
            .Where(it => it.Type == TradeOfferType.Luxury_Resource && it.Amount > 1).ToList();
        var weHaveTheyDont = ourTradableLuxuryResources
            .Where(resource => !tradeLogic.TheirAvailableOffers
                .Any(it => it.Name == resource.Name && it.Type == TradeOfferType.Luxury_Resource)).ToList();
        var theyHaveWeDont = theirTradableLuxuryResources
            .Where(resource => !tradeLogic.OurAvailableOffers
                .Any(it => it.Name == resource.Name && it.Type == TradeOfferType.Luxury_Resource))
            .OrderByDescending(it => civInfo.Cities.Count(city => city.DemandedResource == it.Name)) // Prioritize resources that get WLTKD
            .ToList();
        var trades = new List<Trade.Trade>();
        for (var i = 0; i <= Math.Min(weHaveTheyDont.Count - 1, theyHaveWeDont.Count - 1); i++)
        {
            var trade = new Trade.Trade();
            trade.OurOffers.Add(weHaveTheyDont[i].Copy(amount: 1));
            trade.TheirOffers.Add(theyHaveWeDont[i].Copy(amount: 1));
            trades.Add(trade);
        }
        return trades;
    }
}
