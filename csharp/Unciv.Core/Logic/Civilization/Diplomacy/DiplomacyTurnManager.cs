using Unciv.Logic.Civilization.Transients;
using Unciv.Logic.Trade;
using Unciv.UI.Components.Extensions;
using Unciv.Utils;
using NotificationCategory = Unciv.Logic.Civilization.Notification.NotificationCategory;
using TradeObject = Unciv.Logic.Trade.Trade;
using UniqueType = Unciv.Models.Ruleset.Unique.UniqueType;

namespace Unciv.Logic.Civilization.Diplomacy;

// core/src/com/unciv/logic/civilization/diplomacy/DiplomacyTurnManager.kt (T068). The Kotlin extension functions of DiplomacyManager are static
// extension methods here (`diplomacyManager.NextTurn()`).
//
// Stripped: the timeThis profiling wrapper of nextTurn.
// Quirk kept: `ourOffers.union(theirOffers)` is a LinkedHashSet over TradeOffer's data-class equals (which includes the duration), so an offer
// of theirs that equals one of ours (e.g. both sides of a peace treaty with the same duration) is not in the union and is not counted down.

public static class DiplomacyTurnManager
{
    public static void NextTurn(this DiplomacyManager self)
    {
        self.NextTurnTrades();
        self.RemoveUntenableTrades();
        self.UpdateHasOpenBorders();
        self.NextTurnDiplomaticModifiers();
        self.NextTurnFlags();
        if (self.CivInfo.IsCityState && self.OtherCiv.IsMajorCiv())
            self.NextTurnCityStateInfluence();
        if (self.CivInfo.IsMajorCiv())
            self.SaveSmoothedOpinionOfOtherCiv();
    }

    private static void RemoveUntenableTrades(this DiplomacyManager self)
    {
        var civInfo = self.CivInfo;
        var otherCiv = self.OtherCiv;
        foreach (var trade in self.Trades.ToList())
        {
            // Every cancelled trade can change this - if 1 resource is missing,
            // don't cancel all trades of that resource, only cancel one (the first one, as it happens, since they're added chronologically)
            var negativeCivResources = civInfo.GetCivResourceSupply()
                .Where(it => it.Amount < 0 && !it.Resource.IsStockpiled).Select(it => it.Resource.Name).ToList();

            foreach (var offer in trade.OurOffers)
            {
                if ((offer.Type == TradeOfferType.Luxury_Resource || offer.Type == TradeOfferType.Strategic_Resource)
                    && (negativeCivResources.Contains(offer.Name) || !civInfo.GameInfo.Ruleset.TileResources.ContainsKey(offer.Name)))
                {
                    self.Trades.Remove(trade);
                    var otherCivTrades = otherCiv.GetDiplomacyManager(civInfo).NotNull().Trades;
                    otherCivTrades.RemoveAll(it => it.EqualTrade(trade.Reverse()));

                    // Can't cut short peace treaties!
                    if (trade.TheirOffers.Any(it => it.Name == Constants.PeaceTreaty))
                        self.RemakePeaceTreaty(trade.TheirOffers.First(it => it.Name == Constants.PeaceTreaty).Duration);

                    civInfo.AddNotification($"One of our trades with [{otherCiv.CivName}] has been cut short",
                        new DiplomacyAction(otherCiv.CivId, true),
                        NotificationCategory.Trade, NotificationIcon.Trade, otherCiv.CivName);
                    otherCiv.AddNotification($"One of our trades with [{civInfo.CivName}] has been cut short",
                        new DiplomacyAction(civInfo.CivId, true),
                        NotificationCategory.Trade, NotificationIcon.Trade, civInfo.CivName);
                    // If you cut a trade short, we're not going to trust you with per-turn trades for a while
                    self.OtherCivDiplomacy().SetFlag(DiplomacyFlags.ResourceTradesCutShort, civInfo.GameInfo.Speed.DealDuration * 2);
                    civInfo.Cache.UpdateCivResources();
                }
            }
        }
    }

    private static void RemakePeaceTreaty(this DiplomacyManager self, int durationLeft)
    {
        var treaty = new TradeObject();
        treaty.OurOffers.Add(new TradeOffer(Constants.PeaceTreaty, TradeOfferType.Treaty, duration: durationLeft));
        treaty.TheirOffers.Add(new TradeOffer(Constants.PeaceTreaty, TradeOfferType.Treaty, duration: durationLeft));
        self.Trades.Add(treaty);
        self.OtherCiv.GetDiplomacyManager(self.CivInfo).NotNull().Trades.Add(treaty.Reverse());
    }

    private static void NextTurnCityStateInfluence(this DiplomacyManager self)
    {
        var civInfo = self.CivInfo;
        var otherCiv = self.OtherCiv;
        var initialRelationshipLevel = self.RelationshipIgnoreAfraid(); // Enough since only >= Friend is notified

        float restingPoint = self.GetCityStateInfluenceRestingPoint();
        // We don't use `getInfluence()` here, as then during war with the ally of this CS,
        // our influence would be set to -59, overwriting the old value, which we want to keep
        // as it should be restored once the war ends (though we keep influence degradation from time during the war)
        if (self.Influence > restingPoint)
        {
            float decrement = self.GetCityStateInfluenceDegrade();
            self.SetInfluence(Math.Max(restingPoint, self.Influence - decrement));
        }
        else if (self.Influence < restingPoint)
        {
            float increment = self.GetCityStateInfluenceRecovery();
            self.SetInfluence(Math.Min(restingPoint, self.Influence + increment));
        }

        // B021 (bnw-spec 4.10): the tenets' per-turn influence with afraid and with protected city-states. Whole points ([B030] scale).
        if (!civInfo.IsDefeated() && otherCiv.IsMajorCiv())
        {
            float bonus = 0f;
            if (self.DiplomaticStatus == DiplomaticStatus.Protector)
                bonus += otherCiv.GetMatchingUniques(UniqueType.ProtectedCityStateInfluencePerTurn).Sum(it => it.Params[0].ToInt());
            if (otherCiv.HasUnique(UniqueType.AfraidCityStateInfluencePerTurn) && self.RelationshipLevel() == RelationshipLevel.Afraid)
                bonus += otherCiv.GetMatchingUniques(UniqueType.AfraidCityStateInfluencePerTurn).Sum(it => it.Params[0].ToInt());
            if (bonus != 0f) self.AddInfluence(bonus);
        }

        if (!civInfo.IsDefeated()) // don't display city state relationship notifications when the city state is currently defeated
        {
            var notificationActions = civInfo.CityStateFunctions.GetNotificationActions();
            if (self.GetTurnsToRelationshipChange() == 1)
            {
                var text = $"Your relationship with [{civInfo.CivName}] is about to degrade";
                otherCiv.AddNotification(text, notificationActions, NotificationCategory.Diplomacy, civInfo.CivName, NotificationIcon.Diplomacy);
            }

            if (initialRelationshipLevel >= RelationshipLevel.Friend && initialRelationshipLevel != self.RelationshipIgnoreAfraid())
            {
                var text = $"Your relationship with [{civInfo.CivName}] degraded";
                otherCiv.AddNotification(text, notificationActions, NotificationCategory.Diplomacy, civInfo.CivName, NotificationIcon.Diplomacy);
            }

            // Potentially notify about afraid status
            if (self.GetInfluence() < 30 // We usually don't want to bully our friends
                && !self.HasFlag(DiplomacyFlags.NotifiedAfraid)
                && civInfo.CityStateFunctions.GetTributeWillingness(otherCiv) > 0
                && otherCiv.IsMajorCiv())
            {
                self.SetFlag(DiplomacyFlags.NotifiedAfraid, 20); // Wait 20 turns until next reminder
                var text = $"[{civInfo.CivName}] is afraid of your military power!";
                otherCiv.AddNotification(text, notificationActions, NotificationCategory.Diplomacy, civInfo.CivName, NotificationIcon.Diplomacy);
            }
        }
    }

    private static float GetCityStateInfluenceRecovery(this DiplomacyManager self)
    {
        var civInfo = self.CivInfo;
        var otherCiv = self.OtherCiv;
        if (self.GetInfluence() >= self.GetCityStateInfluenceRestingPoint())
            return 0f;

        float increment = 1f; // sic: personality does not matter here

        float modifierPercent = 0f;

        if (otherCiv.HasUnique(UniqueType.CityStateInfluenceRecoversTwiceNormalRate))
            modifierPercent += 100f;

        string? religion = civInfo.Cities.Count == 0 || civInfo.GetCapital() == null ? null
            : civInfo.GetCapital().NotNull().Religion.GetMajorityReligionName();
        if (religion != null && religion == otherCiv.ReligionManager.Religion?.Name)
            modifierPercent += 50f; // 50% quicker recovery when sharing a religion

        return Math.Max(0f, increment) * Math.Max(0f, modifierPercent).ToPercent();
    }

    private static void NextTurnFlags(this DiplomacyManager self)
    {
        var civInfo = self.CivInfo;
        var otherCiv = self.OtherCiv;
        var flagsCountdown = self.FlagsCountdown;
        foreach (var flag in flagsCountdown.Keys.ToList())
        {
            // We want negative flags to keep on going negative to keep track of time
            flagsCountdown[flag] = flagsCountdown[flag] - 1;

            // If we have uniques that make city states grant military units faster when at war with a common enemy, add higher numbers to this flag
            if (flag == nameof(DiplomacyFlags.ProvideMilitaryUnit) && civInfo.IsMajorCiv() && otherCiv.IsCityState &&
                civInfo.GameInfo.Civilizations.Any(it => civInfo.IsAtWarWith(it) && otherCiv.IsAtWarWith(it)))
            {
                foreach (var unique in civInfo.GetMatchingUniques(UniqueType.CityStateMoreGiftedUnits))
                {
                    flagsCountdown[nameof(DiplomacyFlags.ProvideMilitaryUnit)] =
                        flagsCountdown[nameof(DiplomacyFlags.ProvideMilitaryUnit)] - unique.Params[0].ToInt() + 1;
                    if (flagsCountdown[nameof(DiplomacyFlags.ProvideMilitaryUnit)] <= 0)
                    {
                        flagsCountdown[nameof(DiplomacyFlags.ProvideMilitaryUnit)] = 0;
                        break;
                    }
                }
            }

            // At the end of every turn
            if (flag == nameof(DiplomacyFlags.ResearchAgreement))
                self.TotalOfScienceDuringRA = unchecked(self.TotalOfScienceDuringRA + KotlinMath.FloatToInt(civInfo.Stats.StatsForNextTurn.Science));

            // These modifiers decrease slightly @ 50
            if (flagsCountdown[flag] == 50)
            {
                switch (flag)
                {
                    case nameof(DiplomacyFlags.RememberAttackedProtectedMinor):
                        self.AddModifier(DiplomaticModifiers.AttackedProtectedMinor, 5f);
                        break;
                    case nameof(DiplomacyFlags.RememberBulliedProtectedMinor):
                        self.AddModifier(DiplomaticModifiers.BulliedProtectedMinor, 5f);
                        break;
                }
            }

            // Only when flag is expired
            if (flagsCountdown[flag] == 0)
            {
                switch (flag)
                {
                    case nameof(DiplomacyFlags.ResearchAgreement):
                        if (!self.OtherCivDiplomacy().HasFlag(DiplomacyFlags.ResearchAgreement))
                            self.ScienceFromResearchAgreement();
                        break;
                    case nameof(DiplomacyFlags.DefensivePact):
                        self.DiplomaticStatus = DiplomaticStatus.Peace;
                        break;
                    // This is confusingly named - in fact, the civ that has the flag set is the MAJOR civ
                    case nameof(DiplomacyFlags.ProvideMilitaryUnit):
                        // Do not unset the flag - they may return soon, and we'll continue from that point on
                        if (civInfo.Cities.Count == 0 || otherCiv.Cities.Count == 0)
                            continue;
                        otherCiv.CityStateFunctions.GiveMilitaryUnitToPatron(civInfo);
                        break;

                    case nameof(DiplomacyFlags.RecentlyAttacked):
                        civInfo.CityStateFunctions.AskForUnitGifts(otherCiv);
                        break;
                    // These modifiers don't tick down normally, instead there is a threshold number of turns
                    case nameof(DiplomacyFlags.RememberDestroyedProtectedMinor): // 125
                        self.RemoveModifier(DiplomaticModifiers.DestroyedProtectedMinor);
                        break;
                    case nameof(DiplomacyFlags.RememberAttackedProtectedMinor): // 75
                        self.RemoveModifier(DiplomaticModifiers.AttackedProtectedMinor);
                        break;
                    case nameof(DiplomacyFlags.RememberBulliedProtectedMinor): // 75
                        self.RemoveModifier(DiplomaticModifiers.BulliedProtectedMinor);
                        break;
                    case nameof(DiplomacyFlags.RememberSidedWithProtectedMinor): // 25
                        self.RemoveModifier(DiplomaticModifiers.SidedWithProtectedMinor);
                        break;
                    default:
                        foreach (var demand in Demand.Values)
                        {
                            if (demand.AgreedToDemand.ToString() == flag) self.AddModifier(demand.FulfilledPromiseDiplomacyModifier, 10f);
                        }
                        break;
                }

                flagsCountdown.Remove(flag);
            }
            else if (flag == nameof(DiplomacyFlags.WaryOf) && flagsCountdown[flag] < -10)
            {
                // Used in DeclareWarTargetAutomation.declarePlannedWar to count the number of turns preparing
                // If we have been preparing for over 10 turns then cancel our attack plan
                flagsCountdown.Remove(flag);
            }
        }
    }

    private static void ScienceFromResearchAgreement(this DiplomacyManager self)
    {
        // https://forums.civfanatics.com/resources/research-agreements-bnw.25568/
        int scienceFromResearchAgreement = Math.Min(self.TotalOfScienceDuringRA, self.OtherCivDiplomacy().TotalOfScienceDuringRA);
        self.CivInfo.Tech.ScienceFromResearchAgreements = unchecked(self.CivInfo.Tech.ScienceFromResearchAgreements + scienceFromResearchAgreement);
        self.OtherCiv.Tech.ScienceFromResearchAgreements = unchecked(self.OtherCiv.Tech.ScienceFromResearchAgreements + scienceFromResearchAgreement);
        self.TotalOfScienceDuringRA = 0;
        self.OtherCivDiplomacy().TotalOfScienceDuringRA = 0;
    }

    // union = LinkedHashSet over the data-class equals (see the header); eager like Kotlin's, as the durations are changed afterwards
    private static List<TradeOffer> Union(TradeObject trade) => trade.OurOffers.Concat(trade.TheirOffers).Distinct().ToList();

    private static void NextTurnTrades(this DiplomacyManager self)
    {
        var civInfo = self.CivInfo;
        var otherCiv = self.OtherCiv;
        foreach (var trade in self.Trades.ToList())
        {
            foreach (var offer in Union(trade).Where(it => it.Duration > 0).ToList())
                offer.Duration--;

            if (trade.OurOffers.All(it => it.Duration <= 0) && trade.TheirOffers.All(it => it.Duration <= 0))
            {
                self.Trades.Remove(trade);
                foreach (var offer in Union(trade).Where(it => it.Duration == 0).ToList()) // this was a timed trade
                {
                    var direction = trade.TheirOffers.Contains(offer) ? "from" : "to";
                    civInfo.AddNotification($"[{offer.Name}] {direction} [{otherCiv.CivName}] has ended",
                        new DiplomacyAction(otherCiv.CivId, true),
                        NotificationCategory.Trade, otherCiv.CivName, NotificationIcon.Trade);

                    civInfo.UpdateStatsForNextTurn(); // if they were bringing us gold per turn
                    if (Union(trade) // if resources were involved
                            .Any(it => it.Type == TradeOfferType.Luxury_Resource || it.Type == TradeOfferType.Strategic_Resource))
                        civInfo.Cache.UpdateCivResources();
                }
            }

            foreach (var offer in trade.TheirOffers.Where(it => it.Duration <= 3).ToList())
            {
                if (offer.Duration == 3)
                    civInfo.AddNotification($"[{offer.Name}] from [{otherCiv.CivName}] will end in [3] turns",
                        new DiplomacyAction(otherCiv.CivId, true),
                        NotificationCategory.Trade, otherCiv.CivName, NotificationIcon.Trade);
                else if (offer.Duration == 1)
                    civInfo.AddNotification($"[{offer.Name}] from [{otherCiv.CivName}] will end next turn",
                        new DiplomacyAction(otherCiv.CivId, true),
                        NotificationCategory.Trade, otherCiv.CivName, NotificationIcon.Trade);
            }
        }
    }

    private static void NextTurnDiplomaticModifiers(this DiplomacyManager self)
    {
        var civInfo = self.CivInfo;
        var otherCiv = self.OtherCiv;
        var rng = self.State.StateBasedRandom("DiplomacyManager.nextTurnDiplomaticModifiers");
        if (self.DiplomaticStatus == DiplomaticStatus.Peace)
            self.AccumulateToAtMost(DiplomaticModifiers.YearsOfPeace, 0.5f, 30f);
        else
            self.RevertToZero(DiplomaticModifiers.YearsOfPeace, 0.5f); // war makes you forget the good ol' days

        int openBorders = 0;
        if (self.HasOpenBorders) openBorders += 1;
        if (self.OtherCivDiplomacy().HasOpenBorders) openBorders += 1;
        if (openBorders > 0) self.AccumulateToAtMost(DiplomaticModifiers.OpenBorders, openBorders / 8f); // so if we both have open borders it'll grow by 0.25 per turn
        else self.RevertToZero(DiplomaticModifiers.OpenBorders, 1 / 8f);

        // Negatives
        self.RevertToZero(DiplomaticModifiers.DeclaredWarOnUs, 1 / 8f); // this disappears real slow - it'll take 160 turns to really forget, this is war declaration we're talking about
        self.RevertToZero(DiplomaticModifiers.WarMongerer, 1 / 2f); // warmongering gives a big negative boost when it happens but they're forgotten relatively quickly, like WWII amirite
        self.RevertToZero(DiplomaticModifiers.CapturedOurCities, 1 / 4f); // if you captured our cities, though, that's harder to forget
        self.RevertToZero(DiplomaticModifiers.BetrayedDeclarationOfFriendship, 1 / 8f); // That's a bastardly thing to do
        self.RevertToZero(DiplomaticModifiers.BetrayedDefensivePact, 1 / 16f); // That's an outrageous thing to do
        self.RevertToZero(DiplomaticModifiers.RefusedToNotSettleCitiesNearUs, 1 / 4f);
        self.RevertToZero(DiplomaticModifiers.RefusedToPromiseNotToAttackUs, 1 / 2f);
        self.RevertToZero(DiplomaticModifiers.BulliedProtectedMinor, 1 / 2f); // Decays at same rate as warmongering
        self.RevertToZero(DiplomaticModifiers.AttackedProtectedMinor, 1 / 2f); // Decays at same rate as warmongering
        self.RevertToZero(DiplomaticModifiers.DestroyedProtectedMinor, 1 / 4f); // Decays slower, similar to capturing cities
        foreach (var demand in Demand.Values)
            self.RevertToZero(demand.BetrayedPromiseDiplomacyModifier, 1 / 8f);
        self.RevertToZero(DiplomaticModifiers.BetrayedPromiseToNotAttackOtherCiv, 1 / 8f); // betrayals are remembered for a long time
        self.RevertToZero(DiplomaticModifiers.UnacceptableDemands, 1 / 4f);
        self.RevertToZero(DiplomaticModifiers.StealingTerritory, 1 / 4f);
        self.RevertToZero(DiplomaticModifiers.DenouncedOurAllies, 1 / 4f);
        self.RevertToZero(DiplomaticModifiers.DenouncedOurEnemies, 1 / 4f);
        self.RevertToZero(DiplomaticModifiers.Denunciation, 1 / 8f); // That's personal, it'll take a long time to fade
        self.RevertToZero(DiplomaticModifiers.SpiedOnUs, 1 / 4f);
        self.RevertToZero(DiplomaticModifiers.StoleOurAlly, 1 / 2f); // Fair enough, don't like it but not directly against us per se

        // Positives
        self.RevertToZero(DiplomaticModifiers.GaveUsUnits, 1 / 4f);
        self.RevertToZero(DiplomaticModifiers.LiberatedCity, 1 / 8f);
        if (self.HasModifier(DiplomaticModifiers.GaveUsGifts))
        {
            float giftLoss = self.RelationshipLevel() switch
            {
                RelationshipLevel.Ally => 1f,
                RelationshipLevel.Friend => 1.5f,
                RelationshipLevel.Favorable => 2f,
                RelationshipLevel.Neutral => 2.5f,
                RelationshipLevel.Competitor => 5f,
                RelationshipLevel.Enemy => 7.5f,
                RelationshipLevel.Unforgivable => 10f,
                _ => 2.5f,
            } * civInfo.GameInfo.Ruleset.ModOptions.Constants.GoldGiftDegradationMultiplier;
            // We should subtract a certain amount from this balanced each turn
            // Assuming neutral relations we will subtract the higher of either:
            //  2.5% of the total amount or roughly 50 gold per turn (a value of ~.5 without inflation)
            // This ensures that the amount can be reduced to zero but scales with larger numbers
            float amountLost = Math.Max(Math.Abs(self.GetModifier(DiplomaticModifiers.GaveUsGifts)) * giftLoss / 100, giftLoss / 5);
            self.RevertToZero(DiplomaticModifiers.GaveUsGifts, amountLost);
        }

        self.SetFriendshipBasedModifier();

        self.SetDefensivePactBasedModifier();

        self.SetReligionBasedModifier();

        if (!self.HasFlag(DiplomacyFlags.DeclarationOfFriendship))
            self.RevertToZero(DiplomaticModifiers.DeclarationOfFriendship, 1 / 2f); //decreases slowly and will revert to full if it is declared later

        if (!self.HasFlag(DiplomacyFlags.DefensivePact))
            self.RevertToZero(DiplomaticModifiers.DefensivePact, 1f);

        if (!otherCiv.IsCityState) return;

        if (self.IsRelationshipLevelLT(RelationshipLevel.Friend))
        {
            if (self.HasFlag(DiplomacyFlags.ProvideMilitaryUnit))
                self.RemoveFlag(DiplomacyFlags.ProvideMilitaryUnit);
            return;
        }

        int variance = new[] { -1, 0, 1 }.Random(rng);

        var provideMilitaryUnitUniques = CityStateFunctions
            .GetCityStateBonuses(otherCiv.CityStateType, self.RelationshipIgnoreAfraid(), UniqueType.CityStateMilitaryUnits)
            .Where(it => it.ConditionalsApply(civInfo.State)).ToList();
        if (provideMilitaryUnitUniques.Count == 0) self.RemoveFlag(DiplomacyFlags.ProvideMilitaryUnit);

        foreach (var unique in provideMilitaryUnitUniques)
        {
            // Reset the countdown if it has ended, or if we have longer to go than the current maximum (can happen when going from friend to ally)
            if (!self.HasFlag(DiplomacyFlags.ProvideMilitaryUnit) || self.GetFlag(DiplomacyFlags.ProvideMilitaryUnit) > unique.Params[0].ToInt())
                self.SetFlag(DiplomacyFlags.ProvideMilitaryUnit, unique.Params[0].ToInt() + variance);
        }
    }

    /// <summary>
    /// Uses Quick speed as baseline for turn based adjustment of diplomatic modifiers.
    /// This way, the values set in <see cref="NextTurnDiplomaticModifiers"/> will apply 1:1 to Quick speed, which is the most popular speed.
    /// </summary>
    private const float SpeedAdjustmentNormalizationFactor = 0.67f;

    // amount: always positive, so you don't need to think about it
    private static void RevertToZero(this DiplomacyManager self, DiplomaticModifiers modifier, float amount)
    {
        if (!self.HasModifier(modifier)) return;
        float currentAmount = self.GetModifier(modifier);
        float speedAdjustedAmount = SpeedAdjustmentNormalizationFactor * amount / self.CivInfo.GameInfo.Speed.Modifier;
        if (speedAdjustedAmount >= Math.Abs(currentAmount)) self.DiplomaticModifiers.Remove(modifier.ToString());
        else if (currentAmount > 0) self.AddModifier(modifier, -speedAdjustedAmount);
        else self.AddModifier(modifier, speedAdjustedAmount);
    }

    // amount: should always be positive; maxAmount: modifier value will not increase beyond this value
    private static void AccumulateToAtMost(this DiplomacyManager self, DiplomaticModifiers modifier, float amount, float maxAmount = float.MaxValue)
    {
        float currentAmount = self.GetModifier(modifier);
        // no effect if >= max
        if (currentAmount >= maxAmount) return;
        float speedAdjustedAmount = SpeedAdjustmentNormalizationFactor * amount / self.CivInfo.GameInfo.Speed.Modifier;
        // do not increase beyond max
        if (speedAdjustedAmount >= maxAmount - currentAmount) self.SetModifier(modifier, maxAmount);
        else self.AddModifier(modifier, speedAdjustedAmount);
    }
}
