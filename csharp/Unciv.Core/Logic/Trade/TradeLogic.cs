using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using Unciv.Utils;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;
using SpyFleeReason = Unciv.Logic.City.SpyFleeReason;

namespace Unciv.Logic.Trade;

// core/src/com/unciv/logic/trade/TradeLogic.kt (T086)
//
// TradeEvaluation (T089) is reached through the replaceable instance property TradeLogic.Evaluation (no mutable static state), which defaults to
// TradeEvaluation.Default; tests and TradeEvaluationSeam.NewLogic install a stand-in or the client state's evaluation.
// Kotlin's `ourAvailableOffers += list` is a plusAssign (addAll): TradeOffersList.AddRange, which does not merge.

/// <summary>The members of Kotlin <c>TradeEvaluation</c> that TradeLogic calls.</summary>
internal interface ITradeEvaluation
{
    /// <summary><c>TradeEvaluation().getTradeAcceptability(trade, evaluator, tradePartner, includeDiplomaticGifts)</c></summary>
    int GetTradeAcceptability(Trade trade, CivilizationObject evaluator, CivilizationObject tradePartner, bool includeDiplomaticGifts = false);
    /// <summary><c>TradeEvaluation().isPeaceProposalEnabled(thirdCiv, civInfo)</c></summary>
    bool IsPeaceProposalEnabled(CivilizationObject thirdCiv, CivilizationObject civInfo);
}

public class TradeLogic
{
    /// <summary>The valuation used by this logic: <see cref="TradeEvaluation.Default"/> unless a stand-in is installed. Not serialized.</summary>
    internal ITradeEvaluation Evaluation { get; set; } = TradeEvaluation.Default;

    public CivilizationObject OurCivilization { get; }
    public CivilizationObject OtherCivilization { get; }

    /// <summary>Contains everything we could offer the other player, whether we've actually offered it or not</summary>
    public TradeOffersList OurAvailableOffers { get; set; } = new();
    public TradeOffersList TheirAvailableOffers { get; set; } = new();
    public Trade CurrentTrade { get; } = new();

    public TradeLogic(CivilizationObject ourCivilization, CivilizationObject otherCivilization)
        : this(ourCivilization, otherCivilization, TradeEvaluation.Default)
    {
    }

    internal TradeLogic(CivilizationObject ourCivilization, CivilizationObject otherCivilization, ITradeEvaluation evaluation)
    {
        OurCivilization = ourCivilization;
        OtherCivilization = otherCivilization;
        Evaluation = evaluation;

        // Embassy trade availability depends solely on our ability to trade it
        var embassyOffer = new TradeOffer(Constants.AcceptEmbassy, TradeOfferType.Embassy, ourCivilization.GameInfo.Speed);
        if (ourCivilization.DiplomacyFunctions.CanEstablishEmbassyWith(otherCivilization))
            TheirAvailableOffers.Add(embassyOffer);

        if (ourCivilization.DiplomacyFunctions.CanOfferEmbassyTo(otherCivilization))
            OurAvailableOffers.Add(embassyOffer);

        // Other trade items are added as usual for both sides
        OurAvailableOffers.AddRange(GetAvailableOffers(ourCivilization, otherCivilization));
        TheirAvailableOffers.AddRange(GetAvailableOffers(otherCivilization, ourCivilization));
    }

    private TradeOffersList GetAvailableOffers(CivilizationObject civInfo, CivilizationObject otherCiv)
    {
        var offers = new TradeOffersList();
        if (civInfo.IsCityState || otherCiv.IsCityState) return offers;

        if (civInfo.IsAtWarWith(otherCiv))
            offers.Add(new TradeOffer(Constants.PeaceTreaty, TradeOfferType.Treaty, civInfo.GameInfo.Speed));

        if (civInfo.DiplomacyFunctions.MeetsEmbassyRequirementFor(otherCiv)
            && !otherCiv.GetDiplomacyManager(civInfo).NotNull().HasOpenBorders
            && civInfo.HasUnique(UniqueType.EnablesOpenBorders)
            && otherCiv.HasUnique(UniqueType.EnablesOpenBorders))
        {
            offers.Add(new TradeOffer(Constants.OpenBorders, TradeOfferType.Agreement, civInfo.GameInfo.Speed));
        }

        if (civInfo.DiplomacyFunctions.CanSignResearchAgreementNoCostWith(otherCiv))
            offers.Add(new TradeOffer(Constants.ResearchAgreement, TradeOfferType.Treaty,
                civInfo.DiplomacyFunctions.GetResearchAgreementCost(otherCiv), civInfo.GameInfo.Speed));

        if (civInfo.DiplomacyFunctions.CanSignDefensivePactWith(otherCiv))
            offers.Add(new TradeOffer(Constants.DefensivePact, TradeOfferType.Treaty, civInfo.GameInfo.Speed));

        foreach (var entry in civInfo.GetPerTurnResourcesWithOriginsForTrade()
            .Where(it => it.Resource.ResourceType != ResourceType.Bonus)
            .Where(it => it.Origin == Constants.Tradable)
            .ToList())
        {
            var resourceTradeOfferType = entry.Resource.ResourceType == ResourceType.Luxury ? TradeOfferType.Luxury_Resource
                : TradeOfferType.Strategic_Resource;
            offers.Add(new TradeOffer(entry.Resource.Name, resourceTradeOfferType, entry.Amount, civInfo.GameInfo.Speed));
        }

        foreach (var entry in civInfo.GetStockpiledResourcesForTrade())
        {
            offers.Add(new TradeOffer(entry.Resource.Name, TradeOfferType.Stockpiled_Resource, entry.Amount, civInfo.GameInfo.Speed));
        }

        offers.Add(new TradeOffer(Constants.FlatGold, TradeOfferType.Gold, civInfo.Gold, civInfo.GameInfo.Speed));
        offers.Add(new TradeOffer(Constants.GoldPerTurn, TradeOfferType.Gold_Per_Turn, KotlinMath.FloatToInt(civInfo.Stats.StatsForNextTurn.Gold), civInfo.GameInfo.Speed));

        if (!civInfo.IsOneCityChallenger() && !otherCiv.IsOneCityChallenger())
            foreach (var city in civInfo.Cities.Where(it => !(it.IsCapital() || it.IsInResistance())).ToList())
                offers.Add(new TradeOffer(city.Id, TradeOfferType.City, civInfo.GameInfo.Speed));

        var otherCivsWeKnow = civInfo.GetKnownCivs()
            .Where(it => it != otherCiv && it.IsMajorCiv() && !it.IsDefeated())
            .ToList();

        if (((IHasUniques)civInfo.GameInfo.Ruleset.ModOptions).HasUnique(UniqueType.TradeCivIntroductions))
        {
            var civsWeKnowAndTheyDont = otherCivsWeKnow
                .Where(it => !otherCiv.Knows(it) && !it.IsDefeated())
                .ToList();
            foreach (var thirdCiv in civsWeKnowAndTheyDont)
            {
                offers.Add(new TradeOffer(thirdCiv.CivId, TradeOfferType.Introduction, civInfo.GameInfo.Speed));
            }
        }

        if (!((IHasUniques)civInfo.GameInfo.Ruleset.ModOptions).HasUnique(UniqueType.DiplomaticRelationshipsCannotChange))
        {
            var civsWeBothKnow = otherCivsWeKnow
                .Where(it => otherCiv.Knows(it))
                .ToList();
            var civsWeArentAtWarWith = civsWeBothKnow
                .Where(it => civInfo.GetDiplomacyManager(it).NotNull().CanDeclareWar())
                .ToList();
            foreach (var thirdCiv in civsWeArentAtWarWith)
            {
                offers.Add(new TradeOffer(thirdCiv.CivId, TradeOfferType.WarDeclaration, civInfo.GameInfo.Speed));
            }
        }

        var thirdCivsAtWarTheyKnow = otherCiv.GetKnownCivs().Where(it =>
            it.IsAtWarWith(civInfo) && !it.IsDefeated()
            && !((IHasUniques)it.GameInfo.Ruleset.ModOptions).HasUnique(UniqueType.DiplomaticRelationshipsCannotChange)).ToList();

        foreach (var thirdCiv in thirdCivsAtWarTheyKnow)
        {
            // Setting amount to 0 makes TradeOffer.isTradable() return false and also disables the button in trade window
            var amount = Evaluation.IsPeaceProposalEnabled(thirdCiv, civInfo) ? 1 : 0;
            offers.Add(new TradeOffer(thirdCiv.CivId, TradeOfferType.PeaceProposal, amount, civInfo.GameInfo.Speed));
        }

        return offers;
    }

    public void AcceptTrade(bool applyGifts = true)
    {
        var ourDiploManager = OurCivilization.GetDiplomacyManager(OtherCivilization).NotNull();
        var theirDiploManager = OtherCivilization.GetDiplomacyManager(OurCivilization).NotNull();

        ourDiploManager.Trades.Add(CurrentTrade);
        ourDiploManager.UpdateHasOpenBorders();
        theirDiploManager.Trades.Add(CurrentTrade.Reverse());
        theirDiploManager.UpdateHasOpenBorders();

        // We shouldn't evaluate trades if we are doing a peace treaty
        // Their value can be so big it throws the gift system out of wack
        // Also, offers for peace/war with a third nation are not a "gift"
        var shouldChangeRelationshipDueToGiftedValue = applyGifts
            && !CurrentTrade.OurOffers.Any(it =>
                it.Name == Constants.PeaceTreaty
                || it.Type == TradeOfferType.WarDeclaration
                || it.Type == TradeOfferType.PeaceProposal);

        if (shouldChangeRelationshipDueToGiftedValue)
        {
            // Must evaluate before moving, or else cities have already moved and we get an exception
            var ourGoldValueOfTrade = Evaluation.GetTradeAcceptability(CurrentTrade, OurCivilization, OtherCivilization, includeDiplomaticGifts: false);
            var theirGoldValueOfTrade = Evaluation.GetTradeAcceptability(CurrentTrade.Reverse(), OtherCivilization, OurCivilization, includeDiplomaticGifts: false);

            if (ourGoldValueOfTrade > theirGoldValueOfTrade)
            {
                var isPureGift = CurrentTrade.OurOffers.Count == 0;
                ourDiploManager.GiftGold(ourGoldValueOfTrade - Math.Max(theirGoldValueOfTrade, 0), isPureGift);
            }
            else if (theirGoldValueOfTrade > ourGoldValueOfTrade)
            {
                var isPureGift = CurrentTrade.TheirOffers.Count == 0;
                theirDiploManager.GiftGold(theirGoldValueOfTrade - Math.Max(ourGoldValueOfTrade, 0), isPureGift);
            }
        }

        // Transfer of cities needs to happen before peace treaty, to avoid our units teleporting out of areas that soon will be ours
        foreach (var offer in CurrentTrade.TheirOffers.Where(it => it.Type != TradeOfferType.Treaty).ToList())
            TransferTrade(OtherCivilization, OurCivilization, offer);
        foreach (var offer in CurrentTrade.OurOffers.Where(it => it.Type != TradeOfferType.Treaty).ToList())
            TransferTrade(OurCivilization, OtherCivilization, offer);

        // Transfter of treaties should only be done from one side to avoid double signing and notifying
        foreach (var offer in CurrentTrade.TheirOffers.Where(it => it.Type == TradeOfferType.Treaty).ToList())
            TransferTrade(OtherCivilization, OurCivilization, offer);

        // Diplomatic modifiers for embassy depend on whether a civ gives its embassy, accepts it or both
        if (CurrentTrade.OurOffers.Any(it => it.Type == TradeOfferType.Embassy))
            ApplyEmbassyOffer(ourDiploManager);
        if (CurrentTrade.TheirOffers.Any(it => it.Type == TradeOfferType.Embassy))
            ApplyEmbassyOffer(theirDiploManager);

        OurCivilization.Cache.UpdateCivResources();
        OurCivilization.UpdateStatsForNextTurn();

        OtherCivilization.Cache.UpdateCivResources();
        OtherCivilization.UpdateStatsForNextTurn();
    }

    private static void ApplyEmbassyOffer(DiplomacyManager diploManager)
    {
        if (diploManager.HasModifier(DiplomaticModifiers.EstablishedEmbassy))
        {
            diploManager.ReplaceModifier(DiplomaticModifiers.EstablishedEmbassy, DiplomaticModifiers.SharedEmbassies, 3f);
            diploManager.OtherCivDiplomacy().ReplaceModifier(DiplomaticModifiers.ReceivedEmbassy, DiplomaticModifiers.SharedEmbassies, 3f);
        }
        else
        {
            diploManager.AddModifier(DiplomaticModifiers.ReceivedEmbassy, 1f);
            diploManager.OtherCivDiplomacy().AddModifier(DiplomaticModifiers.EstablishedEmbassy, 2f);
        }
    }

    // instant transfers
    private void TransferTrade(CivilizationObject from, CivilizationObject to, TradeOffer offer)
    {
        switch (offer.Type)
        {
            case TradeOfferType.Embassy:
                from.GetCapital().NotNull().GetCenterTile().ForEachTileInDistance(2, tile => tile.SetExplored(to, true));
                break;
            case TradeOfferType.Gold:
                to.AddGold(offer.Amount);
                from.AddGold(-offer.Amount);
                break;
            case TradeOfferType.Stockpiled_Resource:
                {
                    var resource = from.GameInfo.Ruleset.TileResources.GetValueOrDefault(offer.Name);
                    if (resource == null) return;
                    to.GainStockpiledResource(resource, offer.Amount);
                    from.GainStockpiledResource(resource, -offer.Amount);
                    break;
                }
            case TradeOfferType.Technology:
                to.Tech.AddTechnology(offer.Name);
                break;
            case TradeOfferType.City:
                {
                    var city = from.Cities.First(it => it.Id == offer.Name);

                    city.Espionage.RemoveAllPresentSpies(SpyFleeReason.CityBought);
                    city.MoveToCiv(to);
                    foreach (var unit in city.GetCenterTile().GetUnits().ToList())
                        unit.Movement.TeleportToClosestMoveableTile();
                    foreach (var tile in city.GetTiles().ToList())
                    {
                        foreach (var unit in tile.GetUnits().ToList())
                        {
                            if (!unit.Civ.DiplomacyFunctions.CanPassThroughTiles(to) && !unit.Cache.CanEnterForeignTerrain)
                                unit.Movement.TeleportToClosestMoveableTile();
                        }
                    }
                    to.Cache.UpdateOurTiles();
                    from.Cache.UpdateOurTiles();

                    // suggest an option to liberate the city
                    if (to.IsHuman()
                        && city.FoundingCivObject != null
                        && from != city.FoundingCivObject // can't liberate if the city actually belongs to those guys
                        && to != city.FoundingCivObject) // can't liberate if it's our city
                        to.PopupAlerts.Add(new PopupAlert(AlertType.CityTraded, city.Id));
                    break;
                }
            case TradeOfferType.Treaty:
                // Note: Treaties are not transfered from both sides due to notifications and double signing
                if (offer.Name == Constants.PeaceTreaty) to.GetDiplomacyManager(from).NotNull().MakePeace();
                if (offer.Name == Constants.ResearchAgreement)
                {
                    to.AddGold(-offer.Amount);
                    from.AddGold(-offer.Amount);
                    to.GetDiplomacyManager(from).NotNull().SetFlag(DiplomacyFlags.ResearchAgreement, offer.Duration);
                    from.GetDiplomacyManager(to).NotNull().SetFlag(DiplomacyFlags.ResearchAgreement, offer.Duration);
                }
                if (offer.Name == Constants.DefensivePact) to.GetDiplomacyManager(from).NotNull().SignDefensivePact(offer.Duration);
                break;
            case TradeOfferType.Introduction:
                to.DiplomacyFunctions.MakeCivilizationsMeet(to.GameInfo.GetCivilization(offer.Name));
                break;
            case TradeOfferType.WarDeclaration:
                {
                    var nameOfCivToDeclareWarOn = offer.Name;
                    WarType warType;
                    if (CurrentTrade.TheirOffers.Any(it => it.Type == TradeOfferType.WarDeclaration && it.Name == nameOfCivToDeclareWarOn)
                        && CurrentTrade.OurOffers.Any(it => it.Type == TradeOfferType.WarDeclaration && it.Name == nameOfCivToDeclareWarOn))
                        warType = WarType.TeamWar;
                    else if (CurrentTrade.TheirOffers.Any(it => it.Type == TradeOfferType.WarDeclaration && it.Name == nameOfCivToDeclareWarOn && OurCivilization.IsAtWarWith(to.GameInfo.GetCivilization(it.Name)))
                        || CurrentTrade.OurOffers.Any(it => it.Type == TradeOfferType.WarDeclaration && it.Name == nameOfCivToDeclareWarOn && OtherCivilization.IsAtWarWith(to.GameInfo.GetCivilization(it.Name))))
                        warType = WarType.JoinWar;
                    else warType = WarType.DirectWar;
                    switch (warType)
                    {
                        case WarType.TeamWar:
                        case WarType.JoinWar:
                            from.GetDiplomacyManager(nameOfCivToDeclareWarOn).NotNull().DeclareWar(new DeclareWarReason(warType, to));
                            break;
                        case WarType.DirectWar:
                            // from will always be the declaring Civ
                            from.GetDiplomacyManager(nameOfCivToDeclareWarOn).NotNull().DeclareWar(new DeclareWarReason(warType));
                            break;
                        default:
                            throw new InvalidOperationException($"Unhandled WarType: {warType} found within TradeOfferType.WarDeclaration");
                    }
                    break;
                }
            case TradeOfferType.PeaceProposal:
                {
                    // Convert PeaceProposal to peaceTreaty and apply to warring civs
                    var trade = new Trade();
                    var peaceOffer = new TradeOffer(Constants.PeaceTreaty, TradeOfferType.Treaty, from.GameInfo.Speed);
                    trade.OurOffers.Add(peaceOffer);
                    trade.TheirOffers.Add(peaceOffer);

                    var thirdCiv = from.GameInfo.GetCivilization(offer.Name);
                    var tradePartnerDiplo = from.GetDiplomacyManager(thirdCiv).NotNull();
                    tradePartnerDiplo.Trades.Add(trade);
                    tradePartnerDiplo.UpdateHasOpenBorders();

                    var thirdDiplo = thirdCiv.GetDiplomacyManager(from).NotNull();
                    thirdDiplo.Trades.Add(trade.Reverse());
                    thirdDiplo.UpdateHasOpenBorders();

                    tradePartnerDiplo.MakePeace();
                    break;
                }
        }
    }
}
