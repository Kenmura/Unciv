using Unciv.Logic.Trade.Routes;
using Unciv.Models.Ruleset.Nation;
using Unciv.Models.Ruleset.Unique;
using Unciv.Utils;
using NotificationCategory = Unciv.Logic.Civilization.Notification.NotificationCategory;

namespace Unciv.Logic.Civilization.Diplomacy;

// core/src/com/unciv/logic/civilization/diplomacy/DeclareWar.kt (T068)
//

public static class DeclareWar
{
    /// <summary>
    /// Declares war with the other civ in this diplomacy manager.
    /// Handles all war effects and diplomatic changes with other civs and such.
    ///
    /// <paramref name="declareWarReason"/> changes what sort of effects the war has depending on how it was initiated.
    /// If it was a direct attack put <see cref="WarType.DirectWar"/> for the following effects.
    /// Influence with city states should only be set to -60 when they are attacked directly, not when their ally is attacked.
    /// </summary>
    internal static void Declare(DiplomacyManager diplomacyManager, DeclareWarReason declareWarReason)
    {
        var civInfo = diplomacyManager.CivInfo;
        var otherCiv = diplomacyManager.OtherCiv;
        var otherCivDiplomacy = diplomacyManager.OtherCivDiplomacy();

        if (otherCiv.IsCityState && declareWarReason.WarType == WarType.DirectWar)
            HandleCityStateDirectAttack(diplomacyManager);

        GameEventHooks.WarDeclared(civInfo, otherCiv);
        NotifyOfWar(diplomacyManager, declareWarReason);

        OnWarDeclared(diplomacyManager, true, declareWarReason.WarType);
        OnWarDeclared(otherCivDiplomacy, false, declareWarReason.WarType);

        ChangeOpinions(diplomacyManager, declareWarReason);

        BreakTreaties(diplomacyManager);

        TradeRouteTurns.OnWarDeclared(civInfo.GameInfo); // B020: routes between the two sides end now; a no-op without routes

        TriggerUniques(otherCiv, civInfo);
    }

    private static void TriggerUniques(Civilization otherCiv, Civilization warDeclarerCiv)
    {
        foreach (var unique in warDeclarerCiv.GetTriggeredUniques(UniqueType.TriggerUponDeclaringWarFiltered, warDeclarerCiv.State, it => otherCiv.MatchesFilter(it.Params[0])))
            UniqueTriggerActivation.TriggerUnique(unique, warDeclarerCiv);

        foreach (var unique in otherCiv.GetTriggeredUniques(UniqueType.TriggerUponBeingDeclaredWarUpon, otherCiv.State, it => warDeclarerCiv.MatchesFilter(it.Params[0])))
            UniqueTriggerActivation.TriggerUnique(unique, otherCiv);

        foreach (var unique in warDeclarerCiv.GetTriggeredUniques(UniqueType.TriggerUponEnteringWar, warDeclarerCiv.State, it => otherCiv.MatchesFilter(it.Params[0])))
            UniqueTriggerActivation.TriggerUnique(unique, warDeclarerCiv);

        foreach (var unique in otherCiv.GetTriggeredUniques(UniqueType.TriggerUponEnteringWar, otherCiv.State, it => warDeclarerCiv.MatchesFilter(it.Params[0])))
            UniqueTriggerActivation.TriggerUnique(unique, otherCiv);
    }

    private static void HandleCityStateDirectAttack(DiplomacyManager diplomacyManager)
    {
        var civInfo = diplomacyManager.CivInfo;
        var otherCiv = diplomacyManager.OtherCiv;
        var otherCivDiplomacy = diplomacyManager.OtherCivDiplomacy();

        otherCivDiplomacy.SetInfluence(-60f);
        civInfo.NumMinorCivsAttacked += 1;
        otherCiv.CityStateFunctions.CityStateAttacked(civInfo);

        // You attacked your own ally, you're a right bastard
        if (otherCiv.AllyCiv == civInfo)
        {
            otherCiv.CityStateFunctions.UpdateAllyCivForCityState();
            otherCivDiplomacy.SetInfluence(-120f);
            foreach (var knownCiv in civInfo.GetKnownCivs())
                knownCiv.GetDiplomacyManager(civInfo).NotNull().AddModifier(DiplomaticModifiers.BetrayedDeclarationOfFriendship, -10f);
        }
    }

    private static void NotifyOfWar(DiplomacyManager diplomacyManager, DeclareWarReason declareWarReason)
    {
        var civInfo = diplomacyManager.CivInfo;
        var otherCiv = diplomacyManager.OtherCiv;

        switch (declareWarReason.WarType)
        {
            case WarType.DirectWar:
                otherCiv.AddPopupAlert(new PopupAlert(AlertType.WarDeclaration, civInfo.CivName));

                otherCiv.AddNotification($"[{civInfo.CivName}] has declared war on us!",
                    NotificationCategory.Diplomacy, otherCiv.CivName, NotificationIcon.War, civInfo.CivName);

                foreach (var it in diplomacyManager.GetCommonKnownCivsWithSpectators())
                    it.AddNotification($"[{civInfo.CivName}] has declared war on [{otherCiv.CivName}]!",
                        NotificationCategory.Diplomacy, otherCiv.CivName, NotificationIcon.War, civInfo.CivName);
                break;

            case WarType.DefensivePactWar:
            case WarType.CityStateAllianceWar:
            case WarType.JoinWar:
            case WarType.ProtectedCityStateWar:
            case WarType.AlliedCityStateWar:
                {
                    var allyCiv = declareWarReason.AllyCiv.NotNull();
                    otherCiv.AddPopupAlert(new PopupAlert(AlertType.WarDeclaration, civInfo.CivName));
                    var aggressor = declareWarReason.WarType == WarType.DefensivePactWar ? otherCiv : civInfo;
                    var defender = declareWarReason.WarType == WarType.DefensivePactWar ? civInfo : otherCiv;

                    defender.AddNotification($"[{aggressor.CivName}] has joined [{allyCiv.CivName}] in the war against us!",
                        NotificationCategory.Diplomacy, defender.CivName, NotificationIcon.War, allyCiv.CivName, aggressor.CivName);

                    aggressor.AddNotification($"We have joined [{allyCiv.CivName}] in the war against [{defender.CivName}]!",
                        NotificationCategory.Diplomacy, defender.CivName, NotificationIcon.War, allyCiv.CivName, aggressor.CivName);

                    foreach (var it in diplomacyManager.GetCommonKnownCivsWithSpectators().Where(it => it != allyCiv))
                        it.AddNotification($"[{aggressor.CivName}] has joined [{allyCiv.CivName}] in the war against [{defender.CivName}]!",
                            NotificationCategory.Diplomacy, defender.CivName, NotificationIcon.War, allyCiv.CivName, aggressor.CivName);

                    allyCiv.AddNotification($"[{aggressor.CivName}] has joined us in the war against [{defender.CivName}]!",
                        NotificationCategory.Diplomacy, defender.CivName, NotificationIcon.War, allyCiv.CivName, aggressor.CivName);
                    break;
                }

            case WarType.TeamWar:
                {
                    var allyCiv = declareWarReason.AllyCiv.NotNull();
                    // We only want to send these notifications once, it doesn't matter who sends it though
                    if (IndexOf(civInfo.GameInfo.Civilizations, civInfo) > IndexOf(civInfo.GameInfo.Civilizations, allyCiv)) return;

                    otherCiv.AddPopupAlert(new PopupAlert(AlertType.WarDeclaration, civInfo.CivId));
                    otherCiv.AddPopupAlert(new PopupAlert(AlertType.WarDeclaration, allyCiv.CivId));

                    civInfo.AddNotification($"You and [{allyCiv.CivName}] have declared war against [{otherCiv.CivName}]!",
                        NotificationCategory.Diplomacy, otherCiv.CivName, NotificationIcon.War, allyCiv.CivName, civInfo.CivName);

                    allyCiv.AddNotification($"You and [{civInfo.CivName}] have declared war against [{otherCiv.CivName}]!",
                        NotificationCategory.Diplomacy, otherCiv.CivName, NotificationIcon.War, civInfo.CivName, allyCiv.CivName);

                    otherCiv.AddNotification($"[{civInfo.CivName}] and [{allyCiv.CivName}] have declared war against us!",
                        NotificationCategory.Diplomacy, otherCiv.CivName, NotificationIcon.War, allyCiv.CivName, civInfo.CivName);

                    foreach (var it in diplomacyManager.GetCommonKnownCivsWithSpectators().Where(it => it != allyCiv))
                        it.AddNotification($"[{civInfo.CivName}] and [{allyCiv.CivName}] have declared war against [{otherCiv.CivName}]!",
                            NotificationCategory.Diplomacy, otherCiv.CivName, NotificationIcon.War, allyCiv.CivName, civInfo.CivName);
                    break;
                }
        }
    }

    // List.indexOf uses equals, which for a Civilization is reference identity (-1 when absent)
    private static int IndexOf(IReadOnlyList<Civilization> civs, Civilization civ)
    {
        for (int i = 0; i < civs.Count; i++)
            if (ReferenceEquals(civs[i], civ)) return i;
        return -1;
    }

    /// <summary>Everything that happens to both sides equally when war is declared by one side on the other</summary>
    private static void OnWarDeclared(DiplomacyManager diplomacyManager, bool isOffensiveWar, WarType warType)
    {
        // Cancel all trades.
        foreach (var trade in diplomacyManager.Trades)
            foreach (var offer in trade.TheirOffers.Where(it => it.Duration > 0 && it.Name != Constants.DefensivePact).ToList())
                diplomacyManager.CivInfo.AddNotification($"[{offer.Name}] from [{diplomacyManager.OtherCiv.CivName}] has ended",
                    new DiplomacyAction(diplomacyManager.OtherCiv.CivId, true),
                    NotificationCategory.Trade, diplomacyManager.OtherCiv.CivName, NotificationIcon.Trade);
        diplomacyManager.Trades.Clear();
        diplomacyManager.CivInfo.TradeRequests.RemoveAll(it => it.RequestingCiv == diplomacyManager.OtherCiv.CivId);

        // Must come *before* state is "at war" so units know they're not allowed in tiles without open borders anymore
        diplomacyManager.UpdateHasOpenBorders();

        var civAtWarWith = diplomacyManager.OtherCiv;

        // If we attacked, then we need to end all of our defensive pacts acording to Civ 5
        if (isOffensiveWar)
            RemoveDefensivePacts(diplomacyManager);
        diplomacyManager.DiplomaticStatus = DiplomaticStatus.War;

        var theirDiplomacy = diplomacyManager.OtherCiv.GetDiplomacyManager(diplomacyManager.CivInfo).NotNull();
        // if we broke our promise to not attack them
        if (isOffensiveWar
            && theirDiplomacy.HasFlag(DiplomacyFlags.AgreedToNotAttackUs)
            && (warType == WarType.DirectWar || warType == WarType.JoinWar))
        {
            theirDiplomacy.SetFlag(DiplomacyFlags.MilitaryPresenceNearBorderOrAttackedUsDespitePromise, 30, true);
        }

        // Defensive pact chains are not allowed now
        if (diplomacyManager.CivInfo.IsMajorCiv())
        {
            if (!isOffensiveWar && warType != WarType.DefensivePactWar && !civAtWarWith.IsCityState)
                CallInDefensivePactAllies(diplomacyManager);
            CallInCityStateAllies(diplomacyManager);
        }

        if (diplomacyManager.CivInfo.IsCityState
            && diplomacyManager.CivInfo.CityStateFunctions.GetProtectorCivs().Contains(civAtWarWith))
        {
            diplomacyManager.CivInfo.CityStateFunctions.RemoveProtectorCiv(civAtWarWith, forced: true);
        }

        diplomacyManager.RemoveModifier(DiplomaticModifiers.YearsOfPeace);
        var minimumWarDuration = diplomacyManager.CivInfo.GameInfo.Ruleset.ModOptions.Constants.MinimumWarDuration;
        diplomacyManager.SetFlag(DiplomacyFlags.DeclinedPeace, minimumWarDuration); // AI won't propose peace for 10 turns
        diplomacyManager.SetFlag(DiplomacyFlags.DeclaredWar, minimumWarDuration); // AI won't agree to trade for 10 turns
        diplomacyManager.RemoveFlag(DiplomacyFlags.BorderConflict);

        // War results in removal of embassies for both sides
        diplomacyManager.CivInfo.DiplomacyFunctions.RemoveEmbassies(civAtWarWith);
    }

    private static void ChangeOpinions(DiplomacyManager diplomacyManager, DeclareWarReason declareWarReason)
    {
        var civInfo = diplomacyManager.CivInfo;
        var otherCiv = diplomacyManager.OtherCiv;
        var otherCivDiplomacy = diplomacyManager.OtherCivDiplomacy();
        var warType = declareWarReason.WarType;

        otherCivDiplomacy.SetModifier(DiplomaticModifiers.DeclaredWarOnUs, -20f);
        otherCivDiplomacy.RemoveModifier(DiplomaticModifiers.ReturnedCapturedUnits);

        // Apply warmongering
        if (warType == WarType.DirectWar || warType == WarType.JoinWar || warType == WarType.TeamWar)
        {
            foreach (var thirdCiv in civInfo.GetKnownCivs())
            {
                if (!thirdCiv.IsAtWarWith(otherCiv)
                    && thirdCiv.GetDiplomacyManager(otherCiv)?.IsRelationshipLevelGT(RelationshipLevel.Competitor) != false
                    && thirdCiv != declareWarReason.AllyCiv)
                {
                    // We don't want this modify to stack if there is a defensive pact
                    thirdCiv.GetDiplomacyManager(civInfo).NotNull().AddModifier(DiplomaticModifiers.WarMongerer, -5f);
                }
            }
        }

        // Apply shared enemy modifiers
        foreach (var thirdCiv in diplomacyManager.GetCommonKnownCivs())
        {
            if ((thirdCiv.IsAtWarWith(otherCiv) || thirdCiv == declareWarReason.AllyCiv) && !thirdCiv.IsAtWarWith(civInfo))
            {
                // Improve our relations
                if (thirdCiv.IsCityState) thirdCiv.GetDiplomacyManager(civInfo).NotNull().AddInfluence(10f);
                else thirdCiv.GetDiplomacyManager(civInfo).NotNull().AddModifier(DiplomaticModifiers.SharedEnemy, 5f * civInfo.GetPersonality().ModifierFocus(PersonalityValue.Loyal, .3f));
            }
            else if (thirdCiv.IsAtWarWith(civInfo))
            {
                // Improve their relations
                if (thirdCiv.IsCityState) thirdCiv.GetDiplomacyManager(otherCiv).NotNull().AddInfluence(10f);
                else thirdCiv.GetDiplomacyManager(otherCiv).NotNull().AddModifier(DiplomaticModifiers.SharedEnemy, 5f * civInfo.GetPersonality().ModifierFocus(PersonalityValue.Loyal, .3f));
            }
        }
    }

    private static void BreakTreaties(DiplomacyManager diplomacyManager)
    {
        var otherCiv = diplomacyManager.OtherCiv;
        var otherCivDiplomacy = diplomacyManager.OtherCivDiplomacy();

        bool betrayedFriendship = false;
        bool betrayedDefensivePact = false;
        if (diplomacyManager.HasFlag(DiplomacyFlags.DeclarationOfFriendship))
        {
            betrayedFriendship = true;
            diplomacyManager.RemoveFlag(DiplomacyFlags.DeclarationOfFriendship);
            otherCivDiplomacy.RemoveModifier(DiplomaticModifiers.DeclarationOfFriendship);
        }
        otherCivDiplomacy.RemoveFlag(DiplomacyFlags.DeclarationOfFriendship);

        if (diplomacyManager.HasFlag(DiplomacyFlags.DefensivePact))
        {
            betrayedDefensivePact = true;
            diplomacyManager.RemoveFlag(DiplomacyFlags.DefensivePact);
            otherCivDiplomacy.RemoveModifier(DiplomaticModifiers.DefensivePact);
        }
        otherCivDiplomacy.RemoveFlag(DiplomacyFlags.DefensivePact);

        if (betrayedFriendship || betrayedDefensivePact)
        {
            foreach (var knownCiv in diplomacyManager.CivInfo.GetKnownCivs())
            {
                var diploManager = knownCiv.GetDiplomacyManager(diplomacyManager.CivInfo).NotNull();
                if (betrayedFriendship)
                {
                    float amount = knownCiv == otherCiv ? -40f : -20f;
                    diploManager.AddModifier(DiplomaticModifiers.BetrayedDeclarationOfFriendship, amount * knownCiv.GetPersonality().ModifierFocus(PersonalityValue.Loyal, .3f));
                }
                if (betrayedDefensivePact)
                {
                    //Note: this stacks with Declaration of Friendship
                    float amount = knownCiv == otherCiv ? -20f : -10f;
                    diploManager.AddModifier(DiplomaticModifiers.BetrayedDefensivePact, amount * knownCiv.GetPersonality().ModifierFocus(PersonalityValue.Loyal, .3f));
                }
                diploManager.RemoveModifier(DiplomaticModifiers.DeclaredFriendshipWithOurAllies); // obviously this guy's declarations of friendship aren't worth much.
                diploManager.RemoveModifier(DiplomaticModifiers.SignedDefensivePactWithOurAllies);
            }
        }

        if (diplomacyManager.HasFlag(DiplomacyFlags.ResearchAgreement))
        {
            diplomacyManager.RemoveFlag(DiplomacyFlags.ResearchAgreement);
            diplomacyManager.TotalOfScienceDuringRA = 0;
            otherCivDiplomacy.TotalOfScienceDuringRA = 0;
        }
        otherCivDiplomacy.RemoveFlag(DiplomacyFlags.ResearchAgreement);

        // The other civ should keep any gifts we gave them
        // But we should not nesessarily take away their gifts
        otherCivDiplomacy.RemoveModifier(DiplomaticModifiers.GaveUsGifts);
    }

    /// <summary>
    /// Removes all defensive Pacts and trades. Notifies other civs.
    /// Note: Does not remove the flags and modifiers of the otherCiv if there is a defensive pact.
    /// This is so that we can apply more negative modifiers later.
    /// </summary>
    private static void RemoveDefensivePacts(DiplomacyManager diplomacyManager)
    {
        var civAtWarWith = diplomacyManager.OtherCiv;
        foreach (var thirdPartyDiploManager in diplomacyManager.CivInfo.Diplomacy.Values)
        {
            if (thirdPartyDiploManager.DiplomaticStatus != DiplomaticStatus.DefensivePact) continue;

            // Cancel the defensive pact functionality
            thirdPartyDiploManager.DiplomaticStatus = DiplomaticStatus.Peace;
            thirdPartyDiploManager.OtherCivDiplomacy().DiplomaticStatus = DiplomaticStatus.Peace;

            // We already removed the trades and functionality
            // But we don't want to remove the flags yet so we can process BetrayedDefensivePact later
            if (thirdPartyDiploManager.OtherCiv != civAtWarWith)
            {
                // Trades with defensive pact are now invalid
                var defensivePactOffer = thirdPartyDiploManager.Trades
                    .FirstOrDefault(trade => trade.OurOffers.Any(offer => offer.Name == Constants.DefensivePact));
                if (defensivePactOffer != null) thirdPartyDiploManager.Trades.Remove(defensivePactOffer);
                var theirDefensivePactOffer = thirdPartyDiploManager.OtherCivDiplomacy().Trades
                    .FirstOrDefault(trade => trade.OurOffers.Any(offer => offer.Name == Constants.DefensivePact));
                if (theirDefensivePactOffer != null) thirdPartyDiploManager.OtherCivDiplomacy().Trades.Remove(theirDefensivePactOffer);

                thirdPartyDiploManager.RemoveFlag(DiplomacyFlags.DefensivePact);
                thirdPartyDiploManager.OtherCivDiplomacy().RemoveFlag(DiplomacyFlags.DefensivePact);
            }
            foreach (var civ in thirdPartyDiploManager.GetCommonKnownCivsWithSpectators())
            {
                civ.AddNotification($"[{diplomacyManager.CivInfo.CivName}] cancelled their Defensive Pact with [{thirdPartyDiploManager.OtherCiv.CivName}]!",
                    NotificationCategory.Diplomacy, diplomacyManager.CivInfo.CivName, NotificationIcon.Diplomacy, thirdPartyDiploManager.OtherCiv.CivName);
            }

            thirdPartyDiploManager.OtherCiv.AddNotification($"[{diplomacyManager.CivInfo.CivName}] cancelled their Defensive Pact with us!",
                NotificationCategory.Diplomacy, diplomacyManager.CivInfo.CivName, NotificationIcon.Diplomacy, thirdPartyDiploManager.OtherCiv.CivName);

            thirdPartyDiploManager.CivInfo.AddNotification($"We have cancelled our Defensive Pact with [{thirdPartyDiploManager.OtherCiv.CivName}]!",
                NotificationCategory.Diplomacy, NotificationIcon.Diplomacy, thirdPartyDiploManager.OtherCiv.CivName);
        }
    }

    /// <summary>
    /// Goes through each DiplomacyManager with a defensive pact that is not already in the war.
    /// The civ that we are calling them in against should no longer have a defensive pact with us.
    /// </summary>
    private static void CallInDefensivePactAllies(DiplomacyManager diplomacyManager)
    {
        var civAtWarWith = diplomacyManager.OtherCiv;
        foreach (var ourDefensivePact in diplomacyManager.CivInfo.Diplomacy.Values.Where(ourDipManager =>
            ourDipManager.DiplomaticStatus == DiplomaticStatus.DefensivePact
                && !ourDipManager.OtherCiv.IsDefeated()
                && !ourDipManager.OtherCiv.IsAtWarWith(civAtWarWith)).ToList())
        {
            var ally = ourDefensivePact.OtherCiv;
            if (!civAtWarWith.Knows(ally)) civAtWarWith.DiplomacyFunctions.MakeCivilizationsMeet(ally, true);
            // Have the aggressor declare war on the ally.
            civAtWarWith.GetDiplomacyManager(ally).NotNull().DeclareWar(new DeclareWarReason(WarType.DefensivePactWar, diplomacyManager.CivInfo));
        }
    }

    private static void CallInCityStateAllies(DiplomacyManager diplomacyManager)
    {
        var civAtWarWith = diplomacyManager.OtherCiv;
        foreach (var thirdCiv in diplomacyManager.CivInfo.GetKnownCivs()
            .Where(it => it.IsCityState && it.AllyCiv == diplomacyManager.CivInfo).ToList())
        {
            if (thirdCiv.IsAtWarWith(civAtWarWith)) continue;
            if (!thirdCiv.Knows(civAtWarWith))
                // Our city state ally has not met them yet, so they have to meet first
                thirdCiv.DiplomacyFunctions.MakeCivilizationsMeet(civAtWarWith, warOnContact: true);
            thirdCiv.GetDiplomacyManager(civAtWarWith).NotNull().DeclareWar(new DeclareWarReason(WarType.CityStateAllianceWar, diplomacyManager.CivInfo));
        }
    }
}

public enum WarType
{
    /// <summary>One civ declared war on the other.</summary>
    DirectWar,
    /// <summary>A city state has joined a war through its alliance.</summary>
    CityStateAllianceWar,
    /// <summary>A civilization has joined a war through its defensive pact.</summary>
    DefensivePactWar,
    /// <summary>A civilization has joined a war through a trade.</summary>
    JoinWar,
    /// <summary>Two civilizations are starting a war through a trade.</summary>
    TeamWar,
    /// <summary>Someone attacked our protected city-state</summary>
    ProtectedCityStateWar,
    /// <summary>Someone attacked our allied city-state</summary>
    AlliedCityStateWar,
}

/// <summary>
/// Stores the reason for the war. We might want to add justified wars in the future.
/// <paramref name="allyCiv"/>: if the given <see cref="WarType"/> is <see cref="WarType.CityStateAllianceWar"/>, <see cref="WarType.DefensivePactWar"/> or <see cref="WarType.JoinWar"/>
/// the allyCiv needs to be given.
/// </summary>
public class DeclareWarReason(WarType warType, Civilization? allyCiv = null)
{
    public WarType WarType { get; } = warType;
    public Civilization? AllyCiv { get; } = allyCiv;
}
