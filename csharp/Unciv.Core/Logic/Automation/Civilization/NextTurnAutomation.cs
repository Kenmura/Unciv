using Unciv.Logic.Automation.Unit;
using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Models.Ruleset.Nation;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.StatTypes;
using Unciv.Utils;
using Unciv.View;
using CityObject = Unciv.Logic.City.City;
using CivilizationClass = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Logic.Automation.Civilization;

// Ported from core/src/com/unciv/logic/automation/civilization/NextTurnAutomation.kt (T094). Purity annotations and timeThis profiling dropped (T027 policy).
// - The member sets are split over NextTurnAutomation.cs (turn task list, diplomacy, policies, research, cities) and NextTurnAutomation.Units.cs.
// - The callers in TurnManager, MayaCalendar, UniqueTriggerActivation, Battle, UseGoldAutomation reach this class through the INextTurnAutomation
//   hook of GameClientState (NextTurnAutomationHook), which tests may replace; Kotlin calls the object directly.
// - Kotlin HashMap iteration order (PolicyManager.branchCompletionMap) is the Dictionary insertion order here (T044 policy ticket).
public static partial class NextTurnAutomation
{
    private static CityStateFunctions StateFunctions(CivilizationClass cityState) => (CityStateFunctions)cityState.CityStateFunctions;

    private static GameView ViewOf(CivilizationClass civ) => new((GameInfo)civ.GameInfo, civ);

    /// <summary>Top-level AI turn task list. <paramref name="tradeAndChangeState"/> false is 'forced' automation, such as skip turn.</summary>
    public static void AutomateCivMoves(CivilizationClass civInfo, bool tradeAndChangeState = true)
    {
        if (civInfo.IsBarbarian)
        {
            new BarbarianAutomation(civInfo).Automate();
            return;
        }
        if (civInfo.IsSpectator()) return; // When there's a spectator in multiplayer games, it's processed automatically, but shouldn't be able to actually do anything

        var civView = ViewOf(civInfo).CivView;

        RespondToPopupAlerts(civView);
        TradeAutomation.RespondToTradeRequests(civInfo, tradeAndChangeState);

        if (tradeAndChangeState && civInfo.IsMajorCiv())
        {
            if (!((IHasUniques)civView.Ruleset.ModOptions).HasUnique(UniqueType.DiplomaticRelationshipsCannotChange))
            {
                DiplomacyAutomation.DeclareWar(civView);
                DiplomacyAutomation.OfferPeaceTreaty(civView);
                DiplomacyAutomation.AskForHelp(civView);
                DiplomacyAutomation.OfferDeclarationOfFriendship(civView);
            }
            if (civView.IsReligionEnabled())
            {
                ReligionAutomation.SpendFaithOnReligion(civInfo);
            }

            DiplomacyAutomation.Denounce(civView);
            DiplomacyAutomation.OfferToEstablishEmbassy(civView);
            DiplomacyAutomation.OfferOpenBorders(civView);
            DiplomacyAutomation.OfferResearchAgreement(civView);
            DiplomacyAutomation.OfferDefensivePact(civView);
            DiplomacyAutomation.CheckMilitaryPresenceNearBorder(civView);
            TradeAutomation.ExchangeLuxuries(civInfo);

            IssueRequests(civView);
            AdoptPolicy(civView);
            FreeUpSpaceResources(civView);
        }
        else if (civView.IsCityState)
        {
            StateFunctions(civInfo).GetFreeTechForCityState();
            StateFunctions(civInfo).UpdateDiplomaticRelationshipForCityState();
        }

        using (TurnProbe.Measure("AI.ChooseTech")) ChooseTechToResearch(civView);
        using (TurnProbe.Measure("AI.CityBombardment")) AutomateCityBombardment(civView);
        if (tradeAndChangeState)
            using (TurnProbe.Measure("AI.UseGold")) UseGoldAutomation.UseGold(civInfo);
        if (tradeAndChangeState && !civView.IsCityState)
        {
            ProtectCityStates(civView);
            BullyCityStates(civView);
        }
        var cache = new AutomationCache();
        using (TurnProbe.Measure("AI.AutomateUnits")) AutomateUnits(civView, cache); // this is the most expensive part

        if (tradeAndChangeState && civInfo.IsMajorCiv())
        {
            if (civView.IsReligionEnabled())
            {
                // Can only be done now, as the prophet first has to decide to found/enhance a religion
                ReligionAutomation.ChooseReligiousBeliefs(civInfo);
            }
            if (civInfo.GameInfo.IsEspionageEnabled())
            {
                // Do after cities are conquered
                new EspionageAutomation(civInfo).AutomateSpies();
            }
        }

        using (TurnProbe.Measure("AI.AutomateCities")) AutomateCities(civView); // second most expensive
        if (tradeAndChangeState) TrainSettler(civView);
        // I'm not sure what will happen if we *don't* vote when we can, so automate vote even when forced automation
        TryVoteForDiplomaticVictory(civView);
    }

    public static void AutomateGoldToSciencePercentage(CivView civView)
    {
        // Don't let the AI run blindly with the default convert-gold-to-science ratio if that option is enabled
        var estimatedIncome = civView.GetGoldPerTurn();
        var projectedGold = civView.Gold + estimatedIncome;
        // (Kotlin todo) some cleverness, this is just wild guessing.
        var pissPoor = civView.GetEra().BaseUnitBuyCost;
        var stinkingRich = civView.GetEra().StartingGold * 10 + civView.Cities().Count * 2 * pissPoor;
        const float maxPercent = 0.8f;
        float value;
        if (civView.Gold <= 0) value = 0f;
        else if (projectedGold <= pissPoor) value = 0f;
        else value = Math.Min((projectedGold - pissPoor) * maxPercent / stinkingRich, maxPercent);
        civView.TrySetGoldPercentConvertedToScience(value);
    }

    private static void RespondToPopupAlerts(CivView civView)
    {
        var civInfo = civView.GetCiv();
        foreach (var popupAlert in civInfo.PopupAlerts.ToList()) // toList because this can trigger other things that give alerts, like Golden Age
        {
            RespondToDemandAlert(civView, popupAlert);
            if (popupAlert.Type == AlertType.DeclarationOfFriendship)
                RespondToDeclarationOfFriendship(civView, popupAlert);
        }
        civInfo.PopupAlerts.Clear(); // AIs don't care about popups.
    }

    private static void RespondToDemandAlert(CivView civView, PopupAlert popupAlert)
    {
        var civInfo = civView.GetCiv();
        foreach (var demand in Demand.Values)
        {
            if (popupAlert.Type != demand.DemandAlert) continue;
            var demandingCiv = civInfo.GameInfo.GetCivilization(popupAlert.Value);
            if (demandingCiv.IsDefeated()) return;
            var diploManager = civInfo.GetDiplomacyManager(demandingCiv).NotNull();
            if (Automation.ThreatAssessment(civView, ViewOf(civInfo).GetForeignCivView(demandingCiv)) >= ThreatLevel.High
                || diploManager.IsRelationshipLevelGT(RelationshipLevel.Ally))
                diploManager.AgreeToDemand(demand);
            else diploManager.RefuseDemand(demand);
            return;
        }
    }

    private static void RespondToDeclarationOfFriendship(CivView civView, PopupAlert popupAlert)
    {
        var civInfo = civView.GetCiv();
        var requestingCiv = civInfo.GameInfo.GetCivilization(popupAlert.Value);
        if (requestingCiv.IsDefeated()) return;
        var diploManager = civInfo.GetDiplomacyManager(requestingCiv).NotNull();
        if (civInfo.DiplomacyFunctions.CanSignDeclarationOfFriendshipWith(requestingCiv)
            && DiplomacyAutomation.WantsToSignDeclarationOfFrienship(civView, requestingCiv))
        {
            diploManager.SignDeclarationOfFriendship();
            requestingCiv.AddNotification($"We have signed a Declaration of Friendship with [{civView.CivName}]!", Notification.NotificationCategory.Diplomacy, NotificationIcon.Diplomacy, civView.CivName);
        }
        else
        {
            diploManager.OtherCivDiplomacy().SetFlag(DiplomacyFlags.DeclinedDeclarationOfFriendship, 10);
            requestingCiv.AddNotification($"[{civView.CivName}] has denied our Declaration of Friendship!", Notification.NotificationCategory.Diplomacy, NotificationIcon.Diplomacy, civView.CivName);
        }
    }

    internal static int ValueCityStateAlliance(CivView civView, CivilizationClass cityState, bool includeQuests = false)
    {
        var civInfo = civView.GetCiv();
        if (!cityState.IsCityState) return 0;
        var value = 0;

        var bonuses = CityStateFunctions.GetCityStateBonuses(cityState.CityStateType, RelationshipLevel.Ally).ToList();

        // Optimized version of canProvideStat so we don't need to fetch bonuses multiple times
        bool CanProvideStat(Stat stat) => bonuses.Any(it => it.Stats[stat] > 0);

        if (CanProvideStat(Stat.Culture))
            value += (int)civView.GetPersonalityValue(PersonalityValue.Culture) - 5;
        if (CanProvideStat(Stat.Faith))
            value += (int)civView.GetPersonalityValue(PersonalityValue.Faith) - 5;
        if (CanProvideStat(Stat.Production))
            value += (int)civView.GetPersonalityValue(PersonalityValue.Production) - 5;
        if (CanProvideStat(Stat.Science))
            // In case someone mods this in
            value += (int)civView.GetPersonalityValue(PersonalityValue.Science) - 5;
        if (CanProvideStat(Stat.Happiness))
        {
            if (civView.GetHappiness() < 10)
                value += 10 - civView.GetHappiness();
            value += (int)civView.GetPersonalityValue(PersonalityValue.Happiness) - 5;
        }
        if (CanProvideStat(Stat.Food))
        {
            value += 5;
            value += (int)civView.GetPersonalityValue(PersonalityValue.Food) - 5;
        }

        if (!cityState.IsAlive() || cityState.Cities.Count == 0 || civView.Cities().Count == 0)
            return value;

        // The more we have invested into the city-state the more the alliance is worth
        var ourInfluence = civInfo.Knows(cityState)
            ? (int)cityState.GetDiplomacyManager(civInfo).NotNull().GetInfluence()
            : 0;
        value += Math.Min(10, ourInfluence / 10); // don't let this spiral out of control

        if (ourInfluence < 30)
        {
            // Consider bullying for cash
            value -= 5;
        }

        if (cityState.AllyCiv != null && cityState.AllyCiv != civInfo)
        {
            // easier not to compete if a third civ has this locked down
            var thirdCivInfluence = (int)cityState.GetDiplomacyManager(cityState.AllyCiv).NotNull().GetInfluence();
            value -= (thirdCivInfluence - 30) / 10;
        }

        // Bonus for luxury resources we can get from them
        var ourResources = civInfo.DetailedCivResources.Select(supply => supply.Resource).ToList();
        value += cityState.DetailedCivResources.Count(it =>
            it.Resource.ResourceType == ResourceType.Luxury && !ourResources.Contains(it.Resource));

        if (includeQuests)
        {
            // Investing is better if there is an investment bonus quest active.
            value += (int)(cityState.QuestManager.GetInvestmentMultiplier(civInfo) * 10) - 10;
        }

        return value;
    }

    private static void ProtectCityStates(CivView civView)
    {
        var civInfo = civView.GetCiv();
        foreach (var state in civInfo.GetKnownCivs().Where(it => !it.IsDefeated() && it.IsCityState))
        {
            if (StateFunctions(state).OtherCivCanPledgeProtection(civInfo))
                StateFunctions(state).AddProtectorCiv(civInfo);
            //Always pledge to protect, as it makes it harder for others to demand tribute, and grants +10 resting Influence
        }
    }

    private static void BullyCityStates(CivView civView)
    {
        var civInfo = civView.GetCiv();
        foreach (var state in civInfo.GetKnownCivs().Where(it => !it.IsDefeated() && it.IsCityState).ToList())
        {
            var diplomacyManager = state.GetDiplomacyManager(civInfo).NotNull();
            if (!diplomacyManager.IsRelationshipLevelLT(RelationshipLevel.Friend)) continue;
            if (diplomacyManager.DiplomaticStatus != DiplomaticStatus.Peace) continue;
            if (ValueCityStateAlliance(civView, state) > 0) continue;
            if (StateFunctions(state).GetTributeWillingness(civInfo) < 0) continue;
            if (StateFunctions(state).GetTributeWillingness(civInfo, demandingWorker: true) > 0)
                StateFunctions(state).TributeWorker(civInfo);
            else
                StateFunctions(state).TributeGold(civInfo);
        }
    }

    private static void IssueRequests(CivView civView)
    {
        var civInfo = civView.GetCiv();
        foreach (var otherCiv in civInfo.GetKnownCivs().Where(it => it.IsMajorCiv()).ToList())
        {
            var diploManager = civInfo.GetDiplomacyManager(otherCiv).NotNull();
            foreach (var demand in Demand.Values)
            {
                if (diploManager.HasFlag(demand.ViolationOccurred))
                    OnDemandViolation(demand, civView, otherCiv);
            }
        }
    }

    private static void OnDemandViolation(Demand demand, CivView civView, CivilizationClass offendingCiv)
    {
        var civInfo = civView.GetCiv();
        // most demand violations are not checked during war
        if (civInfo.IsAtWarWith(offendingCiv) && demand != Demand.DoNotAttackUs)
            return;
        var diplomacyManager = civInfo.GetDiplomacyManager(offendingCiv).NotNull();
        if (diplomacyManager.HasFlag(demand.WillIgnoreViolation))
        {
        }
        // violation after promise (broke promise)
        else if (diplomacyManager.HasFlag(demand.AgreedToDemand))
        {
            offendingCiv.AddPopupAlert(new PopupAlert(demand.ViolationDiscoveredAlert, civView.CivId));
            diplomacyManager.SetFlag(demand.WillIgnoreViolation, 100, true);
            diplomacyManager.SetModifier(demand.BetrayedPromiseDiplomacyModifier, -20f);
            if (demand == Demand.DoNotAttackUs)
                // relationship penalty with all common known civs, similar to DoF backstabbing
                // (Kotlin todo) apply WarmongerHatred personality trait here?
                foreach (var thirdPartyCiv in diplomacyManager.GetCommonKnownCivs())
                {
                    thirdPartyCiv.GetDiplomacyManager(offendingCiv).NotNull()
                        .SetModifier(DiplomaticModifiers.BetrayedPromiseToNotAttackOtherCiv, -10f);
                }
            diplomacyManager.RemoveFlag(demand.AgreedToDemand);
        }
        // violation before promise
        else
        {
            var threatLevel = Automation.ThreatAssessment(civView, ViewOf(civInfo).GetForeignCivView(offendingCiv));
            if (threatLevel < ThreatLevel.High // don't piss them off for no reason please.
                || demand == Demand.DoNotAttackUs)
                offendingCiv.AddPopupAlert(new PopupAlert(demand.DemandAlert, civView.CivId));
        }
        diplomacyManager.RemoveFlag(demand.ViolationOccurred);
    }

    public sealed record CityDistance(CityObject City, int AerialDistance);

    public static CityDistance? GetForeignCityNearCapital(CityObject? capital, CivilizationClass foreignCiv)
    {
        if (capital == null || foreignCiv.Cities.Count == 0)
            return null;

        CityDistance? minDistance = null;
        foreach (var foreignCity in foreignCiv.Cities)
        {
            var currentDistance = foreignCity.GetCenterTile().AerialDistanceTo(capital.GetCenterTile());
            if (minDistance == null || currentDistance < minDistance.AerialDistance)
                minDistance = new CityDistance(foreignCity, currentDistance);
        }
        return minDistance;
    }
}
