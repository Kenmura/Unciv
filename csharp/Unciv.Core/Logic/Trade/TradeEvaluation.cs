using Unciv.Logic.Automation.Civilization;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Logic.Civilization.Transients;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using Unciv.UI.Screens.VictoryScreen;
using Unciv.Utils;
using Unciv.View;
using CityObject = Unciv.Logic.City.City;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;
using TileObject = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Logic.Trade;

// core/src/com/unciv/logic/trade/TradeEvaluation.kt (T089). Purity annotations dropped (T027 policy).
// - Kotlin creates a fresh stateless `TradeEvaluation()` at every call site: TradeEvaluation.Default is the shared instance. GameClientState.TradeEvaluation
//   (and TradeLogic.Evaluation) can still replace it, which the tests use to stand in for the valuation.
// - Int sums that Kotlin keeps in Long (getTradeAcceptability) are long; every other Int/Float/Double expression keeps its Kotlin width, and the
//   Kotlin `toInt()` conversions are KotlinMath.FloatToInt / DoubleToInt (saturating, NaN to 0). Int arithmetic wraps.
// - Kotlin `throw Exception(...)` becomes InvalidOperationException.

public class TradeEvaluation : ITradeAutomationEvaluation
{
    /// <summary>The shared instance standing in for Kotlin's <c>TradeEvaluation()</c> (the class has no state).</summary>
    public static TradeEvaluation Default { get; } = new();

    public bool IsTradeValid(Trade trade, CivilizationObject offerer, CivilizationObject tradePartner)
    {
        // Edge case time! Guess what happens if you offer a peace agreement to the AI for all their cities except for the capital,
        // and then capture their capital THAT SAME TURN? It can agree, leading to the civilization getting instantly destroyed!
        // If a civ has never owned an original capital, which means it has not settled the first city yet,
        // it shouldn't be forbidden to trade with other civs owing to cities.size == 0.
        if ((offerer.HasEverOwnedOriginalCapital && trade.OurOffers.Count(it => it.Type == TradeOfferType.City) == offerer.Cities.Count)
            || (tradePartner.HasEverOwnedOriginalCapital && trade.TheirOffers.Count(it => it.Type == TradeOfferType.City) == tradePartner.Cities.Count))
        {
            return false;
        }

        // No way to tell who offers what in isOfferValid()
        var embassyOffer = new TradeOffer(Constants.AcceptEmbassy, TradeOfferType.Embassy, offerer.GameInfo.Speed);
        var theirDiploManager = tradePartner.GetDiplomacyManager(offerer).NotNull();
        var ourDiploManager = offerer.GetDiplomacyManager(tradePartner).NotNull();

        if (trade.OurOffers.Contains(embassyOffer)
            && (offerer.GetCapital() == null
                || theirDiploManager.HasModifier(DiplomaticModifiers.EstablishedEmbassy)
                || theirDiploManager.HasModifier(DiplomaticModifiers.SharedEmbassies)))
            return false;

        if (trade.TheirOffers.Contains(embassyOffer)
            && (tradePartner.GetCapital() == null
                || ourDiploManager.HasModifier(DiplomaticModifiers.EstablishedEmbassy)
                || ourDiploManager.HasModifier(DiplomaticModifiers.SharedEmbassies)))
            return false;

        foreach (var offer in trade.OurOffers)
            if (!IsOfferValid(offer, offerer, tradePartner))
                return false;

        foreach (var offer in trade.TheirOffers)
            if (!IsOfferValid(offer, tradePartner, offerer))
                return false;
        return true;
    }

    private static bool IsOfferValid(TradeOffer tradeOffer, CivilizationObject offerer, CivilizationObject tradePartner)
    {
        bool HasResource(TradeOffer offer)
        {
            var resourcesByName = offerer.GetCivResourcesByName();
            return resourcesByName.ContainsKey(offer.Name) && resourcesByName[offer.Name] >= offer.Amount;
        }

        return tradeOffer.Type switch
        {
            TradeOfferType.Embassy => true, // Already checked
            // if they go a little negative it's okay, but don't allowing going overboard (promising same gold to many)
            TradeOfferType.Gold => tradeOffer.Amount * 0.9f < offerer.Gold,
            TradeOfferType.Gold_Per_Turn => tradeOffer.Amount * 0.9f < offerer.Stats.StatsForNextTurn.Gold,
            // Current automation should prevent these from being offered anyway,
            //   these are a safeguard against future automation changes
            TradeOfferType.Treaty => tradeOffer.Name switch
            {
                Constants.PeaceTreaty => offerer.IsAtWarWith(tradePartner),
                Constants.ResearchAgreement => !offerer.GetDiplomacyManager(tradePartner).NotNull().HasFlag(DiplomacyFlags.ResearchAgreement),
                Constants.DefensivePact => !offerer.GetDiplomacyManager(tradePartner).NotNull().HasFlag(DiplomacyFlags.DefensivePact),
                _ => true, // potentional future treaties
            },
            TradeOfferType.Agreement => true,
            TradeOfferType.Luxury_Resource => HasResource(tradeOffer),
            TradeOfferType.Strategic_Resource => HasResource(tradeOffer),
            TradeOfferType.Stockpiled_Resource => HasResource(tradeOffer),
            TradeOfferType.Technology => true,
            TradeOfferType.Introduction => !tradePartner.Knows(tradeOffer.Name), // You can't introduce them to someone they already know!
            TradeOfferType.WarDeclaration => offerer.GetDiplomacyManager(tradeOffer.Name).NotNull().CanDeclareWar(),
            TradeOfferType.PeaceProposal => offerer.IsAtWarWith(offerer.GameInfo.GetCivilization(tradeOffer.Name)),
            TradeOfferType.City => offerer.Cities.Any(it => it.Id == tradeOffer.Name),
            _ => throw new ArgumentOutOfRangeException(nameof(tradeOffer)),
        };
    }

    public bool IsTradeAcceptable(Trade trade, CivilizationObject evaluator, CivilizationObject tradePartner) =>
        GetTradeAcceptability(trade, evaluator, tradePartner, true) >= 0;

    public int GetTradeAcceptability(Trade trade, CivilizationObject evaluator, CivilizationObject tradePartner, bool includeDiplomaticGifts = false)
    {
        var citiesAskedToSurrender = trade.OurOffers.Count(it => it.Type == TradeOfferType.City);
        var maxCitiesToSurrender = KotlinMath.FloatToInt(MathF.Ceiling((float)evaluator.Cities.Count / 5));
        if (citiesAskedToSurrender > maxCitiesToSurrender)
        {
            return int.MinValue;
        }

        // Never agree to declare war on more than one enemy at a time.
        // Not handled correctly by summing evalutation of individual war declarations,
        // since the AI will evaluate each war declaration as if it were the only one
        if (trade.OurOffers.Count(it => it.Type == TradeOfferType.WarDeclaration) > 1)
        {
            return int.MinValue;
        }

        // Some offers use Int.MAX_VALUE to prevent acceptance. Summing or multiplying
        // these costs can overflow and make a rejected trade look favorable.
        // Use Long for the calculation and clamp the final result to the Int range.
        long sumOfTheirOffers = 0;
        foreach (var offer in trade.TheirOffers.Where(it => it.Type != TradeOfferType.Treaty))
            sumOfTheirOffers = unchecked(sumOfTheirOffers + EvaluateBuyCostWithInflation(offer, evaluator, tradePartner, trade));

        long sumOfOurOffers = 0;
        foreach (var offer in trade.OurOffers)
            sumOfOurOffers = unchecked(sumOfOurOffers + EvaluateSellCostWithInflation(offer, evaluator, tradePartner, trade));

        var relationshipLevel = evaluator.GetDiplomacyManager(tradePartner).NotNull().RelationshipIgnoreAfraid();
        // If we're making a peace treaty, don't try to up the bargain for people you don't like.
        // Leads to spartan behaviour where you demand more, the more you hate the enemy...unhelpful
        if (!trade.OurOffers.Any(it => it.Name == Constants.PeaceTreaty || it.Name == Constants.ResearchAgreement))
        {
            if (relationshipLevel == RelationshipLevel.Enemy) sumOfOurOffers = KotlinMath.DoubleToLong(sumOfOurOffers * 1.5);
            else if (relationshipLevel == RelationshipLevel.Unforgivable) sumOfOurOffers = unchecked(sumOfOurOffers * 2);
        }
        if (trade.OurOffers.FirstOrDefault(it => it.Name == Constants.DefensivePact) != null)
        {
            if (relationshipLevel == RelationshipLevel.Ally)
            {
                //todo: Add more in depth evaluation here
            }
            else
            {
                return int.MinValue;
            }
        }
        int diplomaticGifts = includeDiplomaticGifts ? evaluator.GetDiplomacyManager(tradePartner).NotNull().GetGoldGifts() : 0;
        return (int)Math.Clamp(unchecked(sumOfTheirOffers - sumOfOurOffers + diplomaticGifts), int.MinValue, int.MaxValue);
    }

    public int EvaluateBuyCostWithInflation(TradeOffer offer, CivilizationObject civInfo, CivilizationObject tradePartner, Trade trade)
    {
        if (offer.Type != TradeOfferType.Gold && offer.Type != TradeOfferType.Gold_Per_Turn)
            return KotlinMath.DoubleToInt(EvaluateBuyCost(offer, civInfo, tradePartner, trade) / GetGoldInflation(civInfo));
        return EvaluateBuyCost(offer, civInfo, tradePartner, trade);
    }

    /// <summary>How much their offer is worth to us in gold</summary>
    private int EvaluateBuyCost(TradeOffer offer, CivilizationObject civInfo, CivilizationObject tradePartner, Trade trade)
    {
        var gameInfo = (GameInfo)civInfo.GameInfo;
        switch (offer.Type)
        {
            case TradeOfferType.Embassy: return KotlinMath.FloatToInt(30 * civInfo.GameInfo.Speed.GoldCostModifier);
            case TradeOfferType.Gold: return offer.Amount;
            // GPT loses value for each 'future' turn, meaning: gold now is more valuable than gold in the future
            // Empire-wide production tends to grow at roughly 2% per turn (quick speed), so let's take that as a base line,
            // which results in an efficiency of (0.98 + 0.98^2 + ... + 0.98^25) / 25 = 0.7772 compared to instant gold, assume this scales similarly on different game speed
            // Formula could be more sophisticated by taking into account estimated chance of the gpt-giver cancelling the trade after X amount of turns
            case TradeOfferType.Gold_Per_Turn: return unchecked(offer.Amount * offer.Duration * 4 / 5);
            case TradeOfferType.Treaty:
                return offer.Name switch
                {
                    // Since it will be evaluated twice, once when they evaluate our offer and once when they evaluate theirs
                    Constants.PeaceTreaty => EvaluatePeaceCostForThem(civInfo, tradePartner),
                    Constants.DefensivePact => 0,
                    Constants.ResearchAgreement => unchecked(-offer.Amount),
                    _ => 1000,
                };

            case TradeOfferType.Luxury_Resource: return BuyLuxury(offer, civInfo, tradePartner);

            case TradeOfferType.Strategic_Resource: return BuyStrategic(offer, civInfo, tradePartner);

            case TradeOfferType.Stockpiled_Resource:
                {
                    if (!gameInfo.Ruleset.TileResources.TryGetValue(offer.Name, out var resource)) return 0;
                    return LowestCost(resource, UniqueType.AiWillBuyAt, civInfo) ?? 0;
                }

            case TradeOfferType.Technology: // Currently unused
                return unchecked(KotlinMath.DoubleToInt(Math.Sqrt((double)gameInfo.Ruleset.Technologies[offer.Name].Cost)
                    * gameInfo.Speed.ScienceCostModifier) * 20);
            case TradeOfferType.Introduction: return IntroductionValue(gameInfo.Ruleset);
            case TradeOfferType.WarDeclaration: return BuyWarDeclaration(offer, civInfo, tradePartner, trade);
            case TradeOfferType.PeaceProposal: return BuyPeaceProposal(offer, civInfo);
            case TradeOfferType.City: return BuyCity(offer, civInfo, tradePartner);
            case TradeOfferType.Agreement:
                if (offer.Name == Constants.OpenBorders) return 100;
                throw new InvalidOperationException("Invalid agreement type!");
            default:
                throw new ArgumentOutOfRangeException(nameof(offer));
        }
    }

    private static int BuyLuxury(TradeOffer offer, CivilizationObject civInfo, CivilizationObject tradePartner)
    {
        var gameInfo = (GameInfo)civInfo.GameInfo;
        if (civInfo.GetDiplomacyManager(tradePartner).NotNull().HasFlag(DiplomacyFlags.ResourceTradesCutShort))
            return 0; // We don't trust you for resources

        var lowestExplicitBuyCost = LowestExplicitCost(gameInfo, offer.Name, UniqueType.AiWillBuyAt, civInfo);
        if (lowestExplicitBuyCost != null) return lowestExplicitBuyCost.Value;

        var weLoveTheKingPotential = civInfo.Cities.Count(it => it.DemandedResource == offer.Name) * 50;
        if (!civInfo.HasResource(offer.Name)) // we can't trade on resources, so we are only interested in 1 copy for ourselves
        {
            return weLoveTheKingPotential + (civInfo.GetHappiness() < 0 ? 450 // We're a lot more interested in luxury if low on happiness (AI is never low on happiness though)
                : civInfo.GetHappiness() < 10 ? 350
                : 300); // Higher than corresponding sell cost since a trade is mutually beneficial!
        }
        return 0;
    }

    private static int BuyStrategic(TradeOffer offer, CivilizationObject civInfo, CivilizationObject tradePartner)
    {
        var gameInfo = (GameInfo)civInfo.GameInfo;
        if (civInfo.GetDiplomacyManager(tradePartner).NotNull().HasFlag(DiplomacyFlags.ResourceTradesCutShort))
            return 0; // We don't trust you for resources

        var amountWillingToBuy = 2 - civInfo.GetResourceAmount(offer.Name);
        if (amountWillingToBuy <= 0) return 0; // we already have enough.
        var amountToBuyInOffer = Math.Min(amountWillingToBuy, offer.Amount);

        var lowestExplicitBuyCost = LowestExplicitCost(gameInfo, offer.Name, UniqueType.AiWillBuyAt, civInfo);
        if (lowestExplicitBuyCost != null) return lowestExplicitBuyCost.Value;

        var canUseForBuildings = civInfo.Cities
            .Any(city => city.CityConstructions.GetBuildableBuildings().Any(
                it => it.GetResourceRequirementsPerTurn(city.State).ContainsKey(offer.Name)));
        var canUseForUnits = civInfo.Cities
            .Any(city => city.CityConstructions.GetConstructableUnits().Any(
                it => it.GetResourceRequirementsPerTurn(civInfo.State).ContainsKey(offer.Name)));
        if (!canUseForBuildings && !canUseForUnits) return 0;

        return unchecked(50 * amountToBuyInOffer);
    }

    private static int BuyWarDeclaration(TradeOffer offer, CivilizationObject civInfo, CivilizationObject tradePartner, Trade trade)
    {
        var gameInfo = (GameInfo)civInfo.GameInfo;
        var civToDeclareWarOn = gameInfo.GetCivilization(offer.Name);
        if (trade.TheirOffers.Any(it => it.Type == TradeOfferType.WarDeclaration && it.Name == offer.Name)
            && trade.OurOffers.Any(it => it.Type == TradeOfferType.WarDeclaration && it.Name == offer.Name))
        {
            // Team war is handled in the selling method
            return 0;
        }
        if (civInfo.IsAtWarWith(civToDeclareWarOn))
        {
            // We shouldn't require them to pay us to join our war (no negative values)
            return Math.Max(KotlinMath.FloatToInt(20 * DeclareWarPlanEvaluator.EvaluateJoinOurWarPlan(civInfo, civToDeclareWarOn, tradePartner, null)), 0);
        }
        // Why should we pay you to go fight someone else?
        return 0;
    }

    private static int BuyPeaceProposal(TradeOffer offer, CivilizationObject civInfo)
    {
        var gameInfo = (GameInfo)civInfo.GameInfo;
        var thirdCiv = gameInfo.GetCivilization(offer.Name);

        // We're buying peace if it's city state that is our ally
        if (thirdCiv.IsCityState)
        {
            var allyCiv = thirdCiv.AllyCiv;
            if (allyCiv != null && allyCiv == civInfo)
            {
                // todo: More sophisticated formula needed, a reverse of [CityStateFunctions.influenceGainedByGift]
                var surplusInfluence = Math.Max(thirdCiv.GetDiplomacyManager(civInfo).NotNull().GetInfluence() - 60, 0f);
                return KotlinMath.FloatToInt(surplusInfluence * 15);
            }
        }

        // If we don't like third civ why should we pay
        return civInfo.GetDiplomacyManager(thirdCiv).NotNull().IsRelationshipLevelLT(RelationshipLevel.Neutral)
            ? int.MinValue // Maximum negative for deal to be rejected
            : 0; // Accepts peace proposal with no value
    }

    private static int BuyCity(TradeOffer offer, CivilizationObject civInfo, CivilizationObject tradePartner)
    {
        var city = tradePartner.Cities.FirstOrDefault(it => it.Id == offer.Name)
            ?? throw new InvalidOperationException("Got an offer for city id " + offer.Name + " which does't seem to exist for this civ!");
        int surrounded = SurroundedByOurCities(city, civInfo);
        if (civInfo.GetHappiness() + SumOfFloats(city.CityStats.HappinessList.Values) < 0)
            return 0; // we can't really afford to go into negative happiness because of buying a city
        var sumOfPop = city.Population.Population;
        var sumOfBuildings = city.CityConstructions.GetBuiltBuildings().Count();
        return unchecked((sumOfPop * 4 + sumOfBuildings + 4 + surrounded) * 100);
    }

    /// <summary>Kotlin <c>Iterable&lt;Float&gt;.sum()</c> added to an Int: a Float sum in iteration order, compared with the Int promoted to Float.</summary>
    private static float SumOfFloats(IEnumerable<float> values)
    {
        float sum = 0f;
        foreach (var value in values) sum += value;
        return sum;
    }

    private static int? LowestCost(TileResource resource, UniqueType type, CivilizationObject civInfo)
    {
        int? lowest = null;
        foreach (var unique in ((IHasUniques)resource).GetMatchingUniques(type, new GameContext(civInfo)))
        {
            var cost = unique.Params[0].ToInt();
            if (lowest == null || cost < lowest) lowest = cost;
        }
        return lowest;
    }

    private static int? LowestExplicitCost(GameInfo gameInfo, string resourceName, UniqueType type, CivilizationObject civInfo) =>
        LowestCost(gameInfo.Ruleset.TileResources[resourceName], type, civInfo);

    /// <summary>
    /// Determine if peace proposal button in trade window should be enabled or disabled
    /// Note that enabled peace proposal can still result in denied trade,
    /// e.g. if no counter offer that is worth it can be made
    ///
    /// Certain peace proposal buttons in trade window are disabled when:
    /// 1. Third civ is stronger, need to trade with them instead
    /// 2. Third civ is city state allied to civ we or trade partner is at war with
    /// 3. Third civ is human player, we don't know if they would agree to peace
    /// 4. War count down hasn't expired yet, countdown depends on game speed
    /// </summary>
    /// <param name="thirdCiv">Civilization for which peace proposal can be traded (mediated)</param>
    /// <param name="civInfo">Civilization at war with thirdCiv which trades peace proposal</param>
    public bool IsPeaceProposalEnabled(CivilizationObject thirdCiv, CivilizationObject civInfo)
    {
        var diploManager = civInfo.GetDiplomacyManager(thirdCiv).NotNull();

        // On standard speed 10 turns must pass before peace can be proposed
        if (diploManager.GetFlag(DiplomacyFlags.DeclaredWar) > 0) return false;

        // todo: We don't know if other human player would agree to peace
        if (thirdCiv.IsHuman()) return false;

        if (thirdCiv.IsCityState)
        {
            var allyCiv = thirdCiv.AllyCiv;
            if (allyCiv != null && civInfo.IsAtWarWith(allyCiv))
            {
                // City state is allied to civ with whom trade partner is at war with,
                // need to trade peace with that civ instead
                return false;
            }
        }

        var trade = new Trade();
        var peaceOffer = new TradeOffer(Constants.PeaceTreaty, TradeOfferType.Treaty, duration: civInfo.GameInfo.Speed.PeaceDealDuration);
        trade.OurOffers.Add(peaceOffer);
        trade.TheirOffers.Add(peaceOffer);

        // If trade is acceptable to thirdCiv it's either loosing the war (result > 0) or it's equal to trade partner (result == 0),
        // so opinion of thirdCiv doesn't matter because it would agree to peace due to logic in evaluatePeaceCostForThem()
        // Otherwise we need to trade peace with thirdCiv, instead of trade partner who is loosing the war and would always agree to peace
        return new TradeEvaluation().IsTradeAcceptable(trade, thirdCiv, civInfo);
    }

    private static int SurroundedByOurCities(CityObject city, CivilizationObject civInfo)
    {
        var borderingCivs = GetNeighbouringCivs(city);
        if (borderingCivs.Contains(civInfo))
            return 3; // if the city has a border with trading civ
        return 0;
    }

    private static HashSet<IGameContextCivilization> GetNeighbouringCivs(CityObject city)
    {
        var tilesList = city.GetTiles().ToHashSet();
        var cityPositionList = new List<TileObject>();

        foreach (var tiles in tilesList)
            foreach (var tile in tiles.Neighbors)
                if (!tilesList.Contains(tile))
                    cityPositionList.Add(tile);

        // Only membership is queried, so the set's order never reaches game state.
        return cityPositionList
            .Select(it => it.GetOwner())
            .OfType<IGameContextCivilization>()
            .ToHashSet();
    }

    public int EvaluateSellCostWithInflation(TradeOffer offer, CivilizationObject civInfo, CivilizationObject tradePartner, Trade trade)
    {
        if (offer.Type != TradeOfferType.Gold && offer.Type != TradeOfferType.Gold_Per_Turn)
            return KotlinMath.DoubleToInt(EvaluateSellCost(offer, civInfo, tradePartner, trade) / GetGoldInflation(civInfo));
        return EvaluateSellCost(offer, civInfo, tradePartner, trade);
    }

    /// <summary>How much our offer is worth to us in gold</summary>
    private int EvaluateSellCost(TradeOffer offer, CivilizationObject civInfo, CivilizationObject tradePartner, Trade trade)
    {
        var gameInfo = (GameInfo)civInfo.GameInfo;
        switch (offer.Type)
        {
            case TradeOfferType.Embassy:
                {
                    var tradePartnerDiplo = civInfo.GetDiplomacyManager(tradePartner).NotNull();
                    var baseSellCost =
                        tradePartnerDiplo.IsRelationshipLevelLE(RelationshipLevel.Enemy) ? 300 // arbitrarily chosen
                        : tradePartnerDiplo.IsRelationshipLevelLE(RelationshipLevel.Competitor) ? 60
                        : 30; // 30 is Civ V default (on standard only?)
                    return KotlinMath.FloatToInt(baseSellCost * civInfo.GameInfo.Speed.GoldCostModifier);
                }
            case TradeOfferType.Gold: return offer.Amount;
            case TradeOfferType.Gold_Per_Turn: return unchecked(offer.Amount * offer.Duration * 4 / 5);
            case TradeOfferType.Treaty:
                return offer.Name switch
                {
                    // Since it will be evaluated twice, once when they evaluate our offer and once when they evaluate theirs
                    Constants.PeaceTreaty => EvaluatePeaceCostForThem(civInfo, tradePartner),
                    Constants.DefensivePact => DiplomacyAutomation.WantsToSignDefensivePact(new GameView(gameInfo, civInfo).CivView, tradePartner) ? 0 : 100000,
                    Constants.ResearchAgreement => unchecked(-offer.Amount),
                    _ => 1000,
                    //Todo:AddDefensiveTreatyHere
                };
            case TradeOfferType.Luxury_Resource: return SellLuxury(offer, civInfo);
            case TradeOfferType.Strategic_Resource: return SellStrategic(offer, civInfo, tradePartner);
            case TradeOfferType.Stockpiled_Resource:
                {
                    if (!gameInfo.Ruleset.TileResources.TryGetValue(offer.Name, out var resource)) return 0;
                    return LowestCost(resource, UniqueType.AiWillSellAt, civInfo) ?? int.MaxValue;
                }
            case TradeOfferType.Technology:
                return unchecked(KotlinMath.DoubleToInt(Math.Sqrt((double)gameInfo.Ruleset.Technologies[offer.Name].Cost)) * 20);
            case TradeOfferType.Introduction: return IntroductionValue(gameInfo.Ruleset);
            case TradeOfferType.WarDeclaration: return SellWarDeclaration(offer, civInfo, tradePartner, trade);
            case TradeOfferType.PeaceProposal:
                {
                    // We're evaluating peace cost for third civ to be paid by requesting civ (tradePartner) to us (civInfo)
                    var thirdCiv = gameInfo.GetCivilization(offer.Name);
                    return EvaluatePeaceCostForThem(civInfo, thirdCiv);
                }
            case TradeOfferType.City:
                {
                    var city = civInfo.Cities.FirstOrDefault(it => it.Id == offer.Name)
                        ?? throw new InvalidOperationException("Got an offer to sell city id " + offer.Name + " which does't seem to exist for this civ!");

                    var distanceBonus = DistanceCityTradeModifier(civInfo, city);
                    var sumOfPop = city.Population.Population;
                    var sumOfBuildings = city.CityConstructions.GetBuiltBuildings().Count();
                    return Math.Max(unchecked((sumOfPop * 4 + sumOfBuildings * 1 + 4 + distanceBonus) * 100), 1000);
                }
            case TradeOfferType.Agreement:
                if (offer.Name == Constants.OpenBorders)
                {
                    return civInfo.GetDiplomacyManager(tradePartner).NotNull().RelationshipIgnoreAfraid() switch
                    {
                        RelationshipLevel.Unforgivable => 10000,
                        RelationshipLevel.Enemy => 2000,
                        RelationshipLevel.Competitor => 500,
                        RelationshipLevel.Neutral or RelationshipLevel.Afraid => 200,
                        RelationshipLevel.Favorable or RelationshipLevel.Friend or RelationshipLevel.Ally => 100,
                        _ => throw new ArgumentOutOfRangeException(nameof(offer)),
                    };
                }
                throw new InvalidOperationException("Invalid agreement type!");
            default:
                throw new ArgumentOutOfRangeException(nameof(offer));
        }
    }

    private static int SellLuxury(TradeOffer offer, CivilizationObject civInfo)
    {
        var gameInfo = (GameInfo)civInfo.GameInfo;
        var lowestExplicitSellCost = LowestExplicitCost(gameInfo, offer.Name, UniqueType.AiWillSellAt, civInfo);
        if (lowestExplicitSellCost != null) return lowestExplicitSellCost.Value;

        if (civInfo.GetResourceAmount(offer.Name) > 1) return 250; // fair price
        if (civInfo.HasUnique(UniqueType.RetainHappinessFromLuxury)) // If we retain 100% happiness, value it as a duplicate lux
            return 600 - KotlinMath.FloatToInt(civInfo.GetMatchingUniques(UniqueType.RetainHappinessFromLuxury)
                .First().Params[0].ToFloat() * 3.5f);
        return 600; // you want to take away our last lux of this type?!
    }

    private static int SellStrategic(TradeOffer offer, CivilizationObject civInfo, CivilizationObject tradePartner)
    {
        var gameInfo = (GameInfo)civInfo.GameInfo;
        if (gameInfo.SpaceResources.Contains(offer.Name) &&
            (civInfo.HasUnique(UniqueType.EnablesConstructionOfSpaceshipParts) ||
             tradePartner.HasUnique(UniqueType.EnablesConstructionOfSpaceshipParts)))
            return int.MaxValue; // We'd rather win the game, thanks

        var lowestExplicitSellCost = LowestExplicitCost(gameInfo, offer.Name, UniqueType.AiWillSellAt, civInfo);
        if (lowestExplicitSellCost != null) return lowestExplicitSellCost.Value;

        if (!civInfo.IsAtWar()) return unchecked(50 * offer.Amount);

        var canUseForUnits = gameInfo.Ruleset.Units.Values
            .Any(it => it.GetResourceRequirementsPerTurn(civInfo.State).ContainsKey(offer.Name)
                && it.IsBuildable(civInfo));
        if (!canUseForUnits) return unchecked(50 * offer.Amount);

        var amountLeft = civInfo.GetResourceAmount(offer.Name);

        // Each strategic resource starts costing 100 more when we ass the 5 resources baseline
        // That is to say, if I have 4 and you take one away, that's 200
        // take away the third, that's 300, 2nd 400, 1st 500

        // So if he had 5 left, and we want to buy 2, then we want to buy his 5th and 4th last resources,
        // So we'll calculate how much he'll sell his 4th for (200) and his 5th for (100)
        var totalCost = 0;

        // I know it's confusing, you're welcome to change to a more understandable way of counting if you can think of one...
        // (Kotlin's `a..b` range is empty when a > b, and counting up to Int.MAX_VALUE never overflows there.)
        for (long numberOfResource = unchecked(amountLeft - offer.Amount + 1); numberOfResource <= amountLeft; numberOfResource++)
        {
            totalCost = unchecked(totalCost + (numberOfResource > 5 ? 100 : (int)(6 - numberOfResource) * 100));
        }
        return totalCost;
    }

    private static int SellWarDeclaration(TradeOffer offer, CivilizationObject civInfo, CivilizationObject tradePartner, Trade trade)
    {
        var gameInfo = (GameInfo)civInfo.GameInfo;
        var civToDeclareWarOn = gameInfo.GetCivilization(offer.Name);
        float motivation;
        if (trade.TheirOffers.Any(it => it.Type == TradeOfferType.WarDeclaration && it.Name == offer.Name)
            && trade.OurOffers.Any(it => it.Type == TradeOfferType.WarDeclaration && it.Name == offer.Name))
            // Joint war
            motivation = DeclareWarPlanEvaluator.EvaluateTeamWarPlan(civInfo, civToDeclareWarOn, tradePartner, null);
        else if (tradePartner.IsAtWarWith(civToDeclareWarOn))
            motivation = DeclareWarPlanEvaluator.EvaluateJoinWarPlan(civInfo, civToDeclareWarOn, tradePartner, null);
        else
            motivation = DeclareWarPlanEvaluator.EvaluateDeclareWarPlan(civInfo, civToDeclareWarOn, null);

        // No amount of gold will make us declare a war we're this against - never accept
        if (motivation < -10f) return int.MaxValue;

        // Missing motivation costs 1000 gold per point, so 1000 min (-1 motivation) to 10000 max (-10 motivation)
        if (motivation < 0f) return Math.Min(KotlinMath.FloatToInt(-motivation * 1000), 10000);

        // If we already want to declare this war then thanks for the extra money I guess!
        return 1000;
    }

    /// <summary>
    /// This returns how much one gold is worth now in comparison to starting out the game
    /// Gold is worth less as the civilization has a higher income
    /// </summary>
    public double GetGoldInflation(CivilizationObject civInfo)
    {
        const double modifier = 1000.0;
        double goldPerTurn = civInfo.Stats.StatsForNextTurn.Gold;
        // To visualise the function, plug this into a 2d graphing calculator \frac{1000}{x^{1.2}+1.66*1000}
        // Goes from 1 at GPT = 0 to .923 at GPT = 100, .577 at GPT = 1000 and 0.415 at GPT = 10000
        // The current value of gold will never go below 40%, or the .4f that it is set to (being roughly the efficiency ratio between purchasing and upgrading units)
        // So this does not scale off to infinity
        return modifier / (Math.Pow(Math.Max(goldPerTurn, 1.0), 1.2) + (1.66 * modifier)) + .4;
    }

    /// <summary>
    /// This code returns a positive value if the city is significantly far away from the capital
    /// and given how this method is used this ends up making such cities more expensive. That's how
    /// I found it. I'm not sure it makes sense. One might also find arguments why cities closer to
    /// the capital are more expensive.
    /// </summary>
    private static int DistanceCityTradeModifier(CivilizationObject civInfo, CityObject city)
    {
        var distanceToCapital = civInfo.GetCapital().NotNull().GetCenterTile().AerialDistanceTo(city.GetCenterTile());

        if (distanceToCapital < 500) return 0;
        return unchecked((distanceToCapital - 500) * civInfo.GetEraNumber());
    }

    public int EvaluatePeaceCostForThem(CivilizationObject ourCiv, CivilizationObject otherCiv)
    {
        var ourCombatStrength = ourCiv.GetStatForRanking(RankingType.Force);
        var theirCombatStrength = otherCiv.GetStatForRanking(RankingType.Force);
        if (ourCombatStrength * 1.5f >= theirCombatStrength && theirCombatStrength * 1.5f >= ourCombatStrength)
            return 0; // we're roughly equal, there's no huge power imbalance
        if (ourCombatStrength > theirCombatStrength)
        {
            if (MotivationToAttackAutomation.HasAtLeastMotivationToAttack(ourCiv, new GameView((GameInfo)ourCiv.GameInfo, ourCiv).GetForeignCivView(otherCiv), 0f) <= 0) return 0;
            var absoluteAdvantage = unchecked(ourCombatStrength - theirCombatStrength);
            var percentageAdvantage = absoluteAdvantage / (float)theirCombatStrength;
            // We don't add the same constraint here. We should not make peace easily if we're
            // heavily advantaged.
            var totalAdvantage = KotlinMath.FloatToInt(absoluteAdvantage * percentageAdvantage);
            if (totalAdvantage < 0) //May be a negative number if strength disparity is such that it leads to integer overflow
                return 10000;    //in that rare case, the AI would accept peace against a defeated foe.
            return KotlinMath.DoubleToInt(totalAdvantage / (GetGoldInflation(otherCiv) * 2));
        }
        else
        {
            // This results in huge values for large power imbalances. However, we should not give
            // up everything just because there is a big power imbalance. There's a better chance to
            // recover if you don't give away all your cities for example.
            //
            // Example A (this would probably give us away everything):
            // absoluteAdvantage = 10000 - 100 = 9500
            // percentageAdvantage = 9500 / 100 = 95
            // return -(9500 * 95) * 10 = -9025000
            //
            // Example B (this is borderline)
            // absoluteAdvantage = 10000 - 2500 = 7500
            // percentageAdvantage = 7500 / 2500 = 3
            // return -(7500 * 3) * 10 = -225000
            //
            // Example C (this is fine):
            // absoluteAdvantage = 10000 - 5000 = 5000
            // percentageAdvantage = 5000 / 5000 = 1
            // return -(5000 * 1) * 10 = -50000
            //
            // Hence we cap the max cost at 100k which equals about 2 or 3 cities in the mid game
            // (stats ~30 each)
            var absoluteAdvantage = unchecked(theirCombatStrength - ourCombatStrength);
            var percentageAdvantage = absoluteAdvantage / (float)ourCombatStrength;
            var peaceCost = absoluteAdvantage * percentageAdvantage / (GetGoldInflation(ourCiv) * 2.0);
            return unchecked(-Math.Min(KotlinMath.DoubleToInt(peaceCost), ourCiv.GameInfo.Ruleset.ModOptions.Constants.MaxGoldTradeOffer));
        }
    }

    private static int IntroductionValue(Ruleset ruleSet)
    {
        var unique = ((IHasUniques)ruleSet.ModOptions).GetMatchingUniques(UniqueType.TradeCivIntroductions).FirstOrDefault();
        if (unique == null) return 0;
        return unique.Params[0].ToInt();
    }
}
