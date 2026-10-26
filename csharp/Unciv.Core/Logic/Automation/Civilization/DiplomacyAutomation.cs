using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Logic.Trade;
using Unciv.Models;
using Unciv.Models.Ruleset.Nation;
using Unciv.Models.Ruleset.Unique;
using Unciv.UI.Screens.VictoryScreen;
using Unciv.Utils;
using Unciv.View;
using CivilizationClass = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Logic.Automation.Civilization;

// Ported from core/src/com/unciv/logic/automation/civilization/DiplomacyAutomation.kt (T088). Purity annotations dropped (T027 policy).
// - TradeEvaluation (T089) is reached through TradeEvaluationSeam (TradeAutomationEvaluation.cs).
// - Kotlin Float.pow is a Double pow narrowed to Float: KotlinMath.Pow. Int sums are unchecked loops (LINQ Sum(int) throws on overflow).
// - Kotlin `sortedByDescending` is stable, like OrderByDescending. `toSet()` keeps insertion order: LinkedHashSet.
// - Demand.cs (T059) uses the military-presence flag set by checkMilitaryPresenceNearBorder.

public static class DiplomacyAutomation
{
    private static GameView ViewOf(CivilizationClass civ) => new((GameInfo)civ.GameInfo, civ);

    internal static void OfferDeclarationOfFriendship(CivView civView)
    {
        var civInfo = civView.GetCiv();
        var civsThatWeCanDeclareFriendshipWith = civInfo.GetKnownCivs()
            .Where(it => civInfo.DiplomacyFunctions.CanSignDeclarationOfFriendshipWith(it)
                && !civInfo.GetDiplomacyManager(it).NotNull().HasFlag(DiplomacyFlags.DeclinedDeclarationOfFriendship))
            .OrderByDescending(it => it.GetDiplomacyManager(civInfo).NotNull().RelationshipLevel())
            .ToList();
        foreach (var otherCiv in civsThatWeCanDeclareFriendshipWith)
        {
            var rng = civInfo.GetDiplomacyManager(otherCiv).NotNull().State.StateBasedRandom("DiplomacyAutomation.offerDeclarationOfFriendship");
            // Default setting is 2, this will be changed according to different civ.
            if (new IntRange(1, 10).Random(rng)
                <= 2 * civInfo.GetPersonality().ScaledFocus(PersonalityValue.Diplomacy)
                && WantsToSignDeclarationOfFrienship(civView, otherCiv))
            {
                otherCiv.AddPopupAlert(new PopupAlert(AlertType.DeclarationOfFriendship, civView.CivId));
            }
        }
    }

    internal static bool WantsToSignDeclarationOfFrienship(CivView civView, CivilizationClass otherCiv)
    {
        var civInfo = civView.GetCiv();
        var diploManager = civInfo.GetDiplomacyManager(otherCiv).NotNull();
        if (diploManager.HasFlag(DiplomacyFlags.DeclinedDeclarationOfFriendship)) return false;
        // Shortcut, if it is below favorable then don't consider it
        if (diploManager.IsRelationshipLevelLT(RelationshipLevel.Favorable)) return false;

        var numOfFriends = civInfo.Diplomacy.Count(it => it.Value.HasFlag(DiplomacyFlags.DeclarationOfFriendship));
        var otherCivNumberOfFriends = otherCiv.Diplomacy.Count(it => it.Value.HasFlag(DiplomacyFlags.DeclarationOfFriendship));
        var knownCivs = civInfo.GetKnownCivs().Count(it => it.IsMajorCiv() && it.IsAlive());
        var allCivs = civInfo.GameInfo.Civilizations.Count(it => it.IsMajorCiv()) - 1; // Don't include us
        var deadCivs = civInfo.GameInfo.Civilizations.Count(it => it.IsMajorCiv() && !it.IsAlive());
        var allAliveCivs = allCivs - deadCivs;

        // Motivation should be constant as the number of civs changes
        var motivation = diploManager.OpinionOfOtherCiv() - 40f;

        // Warmongerers don't make good allies
        if (diploManager.HasModifier(DiplomaticModifiers.WarMongerer))
        {
            motivation += diploManager.GetModifier(DiplomaticModifiers.WarMongerer) * civInfo.GetPersonality().ScaledFocus(PersonalityValue.Diplomacy);
        }

        // If the other civ is stronger than we are compelled to be nice to them
        // If they are too weak, then thier friendship doesn't mean much to us
        motivation += Automation.ThreatAssessment(civView, ViewOf(civInfo).GetForeignCivView(otherCiv)) switch
        {
            ThreatLevel.VeryHigh => 10,
            ThreatLevel.High => 5,
            ThreatLevel.VeryLow => -5,
            _ => 0,
        };

        // Try to ally with a fourth of the civs in play
        var civsToAllyWith = 0.25f * allAliveCivs * civInfo.GetPersonality().ScaledFocus(PersonalityValue.Diplomacy);
        if (numOfFriends < civsToAllyWith)
        {
            // Goes from 10 to 0 once the civ gets 1/4 of all alive civs as friends
            motivation += 10 - 10 * numOfFriends / civsToAllyWith;
        }
        else
        {
            // Goes from 0 to -120 as the civ gets more friends, offset by civsToAllyWith
            motivation -= 120f * (numOfFriends - civsToAllyWith) / (knownCivs - civsToAllyWith);
        }

        // The more friends they have the less we should want to sign friendship (To promote teams)
        motivation -= otherCivNumberOfFriends * 10;

        // Goes from 0 to -50 as more civs die
        // this is meant to prevent the game from stalemating when a group of friends
        // conquers all oposition
        motivation -= 50f * deadCivs / allCivs;

        // Become more desperate as we have more wars
        motivation += civInfo.Diplomacy.Values.Count(it => it.OtherCiv.IsMajorCiv() && it.DiplomaticStatus == DiplomaticStatus.War) * 10;

        // Wait to declare frienships until more civs
        // Goes from -30 to 0 when we know 75% of allCivs
        var civsToKnow = 0.75f * allAliveCivs;
        motivation -= Math.Max((civsToKnow - knownCivs) / civsToKnow * 30f, 0f);

        // If they are the only non-friendly civ near us then they are the only civ to attack and expand into
        if (!ThreatManager.GetNeighboringCivilizations(civView).Select(it => it.GetCiv()).Any(it =>
                it.IsMajorCiv() && it != otherCiv
                    && civInfo.GetDiplomacyManager(it).NotNull().IsRelationshipLevelLT(RelationshipLevel.Favorable)))
            motivation -= 20;

        motivation -= MotivationToAttackAutomation.HasAtLeastMotivationToAttack(civInfo, ViewOf(civInfo).GetForeignCivView(otherCiv), motivation / 2f) * 2;

        return motivation > 0;
    }

    /// <summary>Try establishing embassy in other civs' capitals</summary>
    internal static void OfferToEstablishEmbassy(CivView civView)
    {
        var civInfo = civView.GetCiv();
        if (civView.GetCapital() == null) return;
        var civsThatWeCanEstablishEmbassyWith = civInfo.GetKnownCivs().Where(it =>
            civInfo.DiplomacyFunctions.CanEstablishEmbassyWith(it)
            && !civInfo.GetDiplomacyManager(it).NotNull().HasFlag(DiplomacyFlags.DeclinedEmbassy)
            && !AreWeOfferingTrade(civView, it, Constants.AcceptEmbassy))
            .OrderByDescending(it => it.GetDiplomacyManager(civInfo).NotNull().RelationshipLevel())
            .ToList();

        foreach (var otherCiv in civsThatWeCanEstablishEmbassyWith)
        {
            var rng = civInfo.GetDiplomacyManager(otherCiv).NotNull().State.StateBasedRandom("DiplomacyAutomation.offerToEstablishEmbassy");
            // Default setting is 3
            if (new IntRange(1, 10).Random(rng) < 7) continue;
            if (WantsToAcceptEmbassy(civView, otherCiv))
            {
                var tradeLogic = TradeEvaluationSeam.NewLogic(civInfo, otherCiv);
                var embassyOffer = new TradeOffer(Constants.AcceptEmbassy, TradeOfferType.Embassy, civInfo.GameInfo.Speed);
                tradeLogic.CurrentTrade.TheirOffers.Add(embassyOffer);

                // If possible offer mutual embassies (Civ V behavior) so we don't waste gold
                if (otherCiv.DiplomacyFunctions.CanEstablishEmbassyWith(civInfo))
                {
                    tradeLogic.CurrentTrade.OurOffers.Add(embassyOffer);
                }
                else
                { // Otherwise offer GPT (prefered) or flat gold for embassy in their capital
                    var embassyValue = TradeEvaluationSeam.Of(civInfo).EvaluateBuyCostWithInflation(embassyOffer, civInfo, otherCiv, tradeLogic.CurrentTrade);
                    var embassyGptValue = embassyValue / civInfo.GameInfo.Speed.DealDuration;
                    var ourGpt = civView.GetGoldPerTurn();
                    if (embassyGptValue >= 1 && embassyGptValue <= ourGpt)
                        tradeLogic.CurrentTrade.OurOffers.Add(new TradeOffer(Constants.GoldPerTurn, TradeOfferType.Gold_Per_Turn, embassyGptValue, civInfo.GameInfo.Speed));
                    else if (civView.Gold >= embassyValue && ourGpt >= 0)
                        tradeLogic.CurrentTrade.OurOffers.Add(new TradeOffer(Constants.FlatGold, TradeOfferType.Gold, embassyValue, civInfo.GameInfo.Speed));
                    // else let them make counter offer
                }

                otherCiv.TradeRequests.Add(new TradeRequest(civView.CivId, tradeLogic.CurrentTrade.Reverse()));
            }
            else
            {
                // Remember this for a few turns to save computation power
                civInfo.GetDiplomacyManager(otherCiv).NotNull().SetFlag(DiplomacyFlags.DeclinedEmbassy, 5);
            }
        }
    }

    internal static void OfferOpenBorders(CivView civView)
    {
        var civInfo = civView.GetCiv();
        if (!civView.HasUnique(UniqueType.EnablesOpenBorders)) return;

        var civsThatWeCanOpenBordersWith = civInfo.GetKnownCivs().Where(it =>
        {
            var ourDiploManager = civInfo.GetDiplomacyManager(it).NotNull();
            return it.IsMajorCiv()
                && !civInfo.IsAtWarWith(it)
                && it.HasUnique(UniqueType.EnablesOpenBorders)
                && !ourDiploManager.HasOpenBorders
                && !ourDiploManager.OtherCivDiplomacy().HasOpenBorders
                && civInfo.DiplomacyFunctions.MeetsEmbassyRequirementFor(it)
                && !ourDiploManager.HasFlag(DiplomacyFlags.DeclinedOpenBorders)
                && !AreWeOfferingTrade(civView, it, Constants.OpenBorders);
        }).OrderByDescending(it => it.GetDiplomacyManager(civInfo).NotNull().RelationshipLevel()).ToList();

        foreach (var otherCiv in civsThatWeCanOpenBordersWith)
        {
            var rng = civInfo.GetDiplomacyManager(otherCiv).NotNull().State.StateBasedRandom("DiplomacyAutomation.offerOpenBorders");
            // Default setting is 3
            if (new IntRange(1, 10).Random(rng) < 7) continue;
            if (WantsToOpenBorders(civView, otherCiv))
            {
                var tradeLogic = TradeEvaluationSeam.NewLogic(civInfo, otherCiv);
                tradeLogic.CurrentTrade.OurOffers.Add(new TradeOffer(Constants.OpenBorders, TradeOfferType.Agreement, civInfo.GameInfo.Speed));
                tradeLogic.CurrentTrade.TheirOffers.Add(new TradeOffer(Constants.OpenBorders, TradeOfferType.Agreement, civInfo.GameInfo.Speed));

                otherCiv.TradeRequests.Add(new TradeRequest(civView.CivId, tradeLogic.CurrentTrade.Reverse()));
            }
            else
            {
                // Remember this for a few turns to save computation power
                civInfo.GetDiplomacyManager(otherCiv).NotNull().SetFlag(DiplomacyFlags.DeclinedOpenBorders, 5);
            }
        }
    }

    /// <summary>Test if <paramref name="otherCiv"/> wants to accept our embassy in their capital</summary>
    public static bool WantsToAcceptEmbassy(CivView civView, CivilizationClass otherCiv)
    {
        var civInfo = civView.GetCiv();
        var theirDiploManager = otherCiv.GetDiplomacyManager(civInfo).NotNull();
        if (civInfo.GetDiplomacyManager(otherCiv).NotNull().HasFlag(DiplomacyFlags.DeclinedEmbassy)) return false;
        if (theirDiploManager.IsRelationshipLevelLT(RelationshipLevel.Afraid)) return false;

        // Being able to see their capital can give us an advantage later on, especially with espionage enabled
        if (!civInfo.GetCapital().NotNull().GetCenterTile().IsExplored(otherCiv)) return true;

        // Did they not discovered our capital yet?
        if (!otherCiv.GetCapital().NotNull().GetCenterTile().IsExplored(civInfo))
        {
            // If we're afraid of them deny embassy
            if (theirDiploManager.RelationshipLevel() == RelationshipLevel.Afraid) return false;

            // If they're much stronger than us deny embassy
            var ourCombatStrength = civInfo.GetStatForRanking(RankingType.Force);
            var theirCombatStrength = otherCiv.GetStatForRanking(RankingType.Force);
            var ourAbsoluteAdvantage = ourCombatStrength - theirCombatStrength;
            var percentageAdvantage = ourAbsoluteAdvantage / (float)theirCombatStrength;
            if (percentageAdvantage > 0.5) return false;
        }

        return true; // Relationship is Afraid or greater
    }

    public static bool WantsToOpenBorders(CivView civView, CivilizationClass otherCiv)
    {
        var civInfo = civView.GetCiv();
        var ourDiploManager = civInfo.GetDiplomacyManager(otherCiv).NotNull();
        if (ourDiploManager.HasFlag(DiplomacyFlags.DeclinedOpenBorders)) return false;
        if (ourDiploManager.IsRelationshipLevelLT(RelationshipLevel.Favorable)) return false;
        // Don't accept if they are at war with our friends, they might use our land to attack them
        if (civInfo.Diplomacy.Values.Any(it => it.IsRelationshipLevelGE(RelationshipLevel.Friend) && it.OtherCiv.IsAtWarWith(otherCiv)))
            return false;
        // Being able to see their cities can give us an advantage later on, especially with espionage enabled
        if (otherCiv.Cities.Count(it => !it.GetCenterTile().IsVisible(civInfo)) > otherCiv.Cities.Count * .8f)
            return true;
        if (MotivationToAttackAutomation.HasAtLeastMotivationToAttack(civInfo, ViewOf(civInfo).GetForeignCivView(otherCiv),
                ourDiploManager.OpinionOfOtherCiv() * civInfo.GetPersonality().ScaledFocus(PersonalityValue.Commerce) / 2) > 0)
            return false;
        return true;
    }

    internal static void OfferResearchAgreement(CivView civView)
    {
        var civInfo = civView.GetCiv();
        if (!civInfo.DiplomacyFunctions.CanSignResearchAgreement()) return; // don't waste your time

        var civsThatWeCanSignResearchAgreementWith = civInfo.GetKnownCivs().Where(it =>
            civInfo.DiplomacyFunctions.CanSignResearchAgreementWith(it)
                && !civInfo.GetDiplomacyManager(it).NotNull().HasFlag(DiplomacyFlags.DeclinedResearchAgreement)
                && !AreWeOfferingTrade(civView, it, Constants.ResearchAgreement))
            .OrderByDescending(it => it.Stats.StatsForNextTurn.Science)
            .ToList();

        foreach (var otherCiv in civsThatWeCanSignResearchAgreementWith)
        {
            // Always offer a research agreement we can sign - previously this was skipped ~50% of the time
            // by a random roll. A mutual RA is a free, balanced deal that raises the other civ's opinion of
            // us (so we get attacked less), on top of the shared science, so there is no reason to decline.
            var tradeLogic = TradeEvaluationSeam.NewLogic(civInfo, otherCiv);
            var cost = civInfo.DiplomacyFunctions.GetResearchAgreementCost(otherCiv);
            var tradeOffer = new TradeOffer(Constants.ResearchAgreement, TradeOfferType.Treaty, cost, civInfo.GameInfo.Speed);

            tradeLogic.CurrentTrade.OurOffers.Add(tradeOffer);
            tradeLogic.CurrentTrade.TheirOffers.Add(tradeOffer);
            otherCiv.TradeRequests.Add(new TradeRequest(civView.CivId, tradeLogic.CurrentTrade.Reverse()));
        }
    }

    internal static void OfferDefensivePact(CivView civView)
    {
        var civInfo = civView.GetCiv();
        if (!civInfo.DiplomacyFunctions.CanSignDefensivePact()) return; // don't waste your time

        var civsThatWeCanSignDefensivePactWith = civInfo.GetKnownCivs().Where(it =>
        {
            var ourDiploManager = civInfo.GetDiplomacyManager(it).NotNull();
            return civInfo.DiplomacyFunctions.CanSignDefensivePactWith(it)
                && !ourDiploManager.HasFlag(DiplomacyFlags.DeclinedDefensivePact)
                && ourDiploManager.OpinionOfOtherCiv() > 70f * civInfo.GetPersonality().InverseScaledFocus(PersonalityValue.Aggressive)
                && !AreWeOfferingTrade(civView, it, Constants.DefensivePact);
        }).ToList();

        foreach (var otherCiv in civsThatWeCanSignDefensivePactWith)
        {
            var rng = civInfo.GetDiplomacyManager(otherCiv).NotNull().State.StateBasedRandom("DiplomacyAutomation.offerDefensivePact");
            // Default setting is 3, this will be changed according to different civ.
            if (new IntRange(1, 10).Random(rng)
                <= 7 * civInfo.GetPersonality().InverseScaledFocus(PersonalityValue.Loyal)) continue;
            if (WantsToSignDefensivePact(civView, otherCiv))
            {
                //todo: Add more in depth evaluation here
                var tradeLogic = TradeEvaluationSeam.NewLogic(civInfo, otherCiv);
                var tradeOffer = new TradeOffer(Constants.DefensivePact, TradeOfferType.Treaty, civInfo.GameInfo.Speed);

                tradeLogic.CurrentTrade.OurOffers.Add(tradeOffer);
                tradeLogic.CurrentTrade.TheirOffers.Add(tradeOffer);
                otherCiv.TradeRequests.Add(new TradeRequest(civView.CivId, tradeLogic.CurrentTrade.Reverse()));
            }
            else
            {
                // Remember this for a few turns to save computation power
                civInfo.GetDiplomacyManager(otherCiv).NotNull().SetFlag(DiplomacyFlags.DeclinedDefensivePact, 5);
            }
        }
    }

    public static bool WantsToSignDefensivePact(CivView civView, CivilizationClass otherCiv)
    {
        var civInfo = civView.GetCiv();
        var ourDiploManager = civInfo.GetDiplomacyManager(otherCiv).NotNull();
        if (ourDiploManager.HasFlag(DiplomacyFlags.DeclinedDefensivePact)) return false;
        if (ourDiploManager.OpinionOfOtherCiv() < 65f * civInfo.GetPersonality().InverseScaledFocus(PersonalityValue.Aggressive)) return false;
        var commonknownCivs = ourDiploManager.GetCommonKnownCivs();

        foreach (var thirdCiv in commonknownCivs)
        {
            // If they have bad relations with any of our friends, don't consider it
            if (civInfo.GetDiplomacyManager(thirdCiv).NotNull().HasFlag(DiplomacyFlags.DeclarationOfFriendship)
                && thirdCiv.GetDiplomacyManager(otherCiv).NotNull().IsRelationshipLevelLT(RelationshipLevel.Favorable))
                return false;

            // If they have bad relations with any of our friends, don't consider it
            if (otherCiv.GetDiplomacyManager(thirdCiv).NotNull().HasFlag(DiplomacyFlags.DeclarationOfFriendship)
                && thirdCiv.GetDiplomacyManager(civInfo).NotNull().IsRelationshipLevelLT(RelationshipLevel.Neutral))
                return false;
        }

        var defensivePacts = civInfo.Diplomacy.Count(it => it.Value.HasFlag(DiplomacyFlags.DefensivePact));
        var otherCivNonOverlappingDefensivePacts = otherCiv.Diplomacy.Values.Count(it =>
            it.HasFlag(DiplomacyFlags.DefensivePact)
                && it.OtherCiv.GetDiplomacyManager(civInfo)?.HasFlag(DiplomacyFlags.DefensivePact) != true);
        var allCivs = civInfo.GameInfo.Civilizations.Count(it => it.IsMajorCiv()) - 1; // Don't include us
        var deadCivs = civInfo.GameInfo.Civilizations.Count(it => it.IsMajorCiv() && !it.IsAlive());
        var allAliveCivs = allCivs - deadCivs;

        // We have to already be at RelationshipLevel.Ally, so we must have 80 oppinion of them
        var motivation = ourDiploManager.OpinionOfOtherCiv() - 80;

        // Warmongerers don't make good allies
        if (ourDiploManager.HasModifier(DiplomaticModifiers.WarMongerer))
        {
            motivation += ourDiploManager.GetModifier(DiplomaticModifiers.WarMongerer) * civInfo.GetPersonality().ScaledFocus(PersonalityValue.Diplomacy);
        }

        // If they are stronger than us, then we value it a lot more
        // If they are weaker than us, then we don't value it
        motivation += Automation.ThreatAssessment(civView, ViewOf(civInfo).GetForeignCivView(otherCiv)) switch
        {
            ThreatLevel.VeryHigh => 10,
            ThreatLevel.High => 5,
            ThreatLevel.Low => -3,
            ThreatLevel.VeryLow => -7,
            _ => 0,
        };

        // If they have a defensive pact with another civ then we would get drawn into their battles as well
        motivation -= 15 * otherCivNonOverlappingDefensivePacts;

        // Becomre more desperate as we have more wars
        motivation += civInfo.Diplomacy.Values.Count(it => it.OtherCiv.IsMajorCiv() && it.DiplomaticStatus == DiplomaticStatus.War) * 5;

        // Try to have a defensive pact with 1/5 of all civs
        var civsToAllyWith = 0.20f * allAliveCivs * civInfo.GetPersonality().ScaledFocus(PersonalityValue.Diplomacy);
        // Goes from 0 to -40 as the civ gets more allies, offset by civsToAllyWith
        motivation -= Math.Max(40f * (defensivePacts - civsToAllyWith) / (allAliveCivs - civsToAllyWith), 0f);

        return motivation > 0;
    }

    internal static void DeclareWar(CivView civView)
    {
        if (civView.Cities().Count == 0 || civView.GetKnownCivs().Count == 0) return;
        if (civView.GetPersonalityValue(PersonalityValue.DeclareWar) == 0f) return;
        if (civView.GetHappiness() <= 0) return;

        var ourMilitaryUnits = civView.GetUnits().Count(it => !it.IsCivilian());
        if (ourMilitaryUnits < civView.Cities().Count) return;
        if (ourMilitaryUnits < 4) return; // to stop AI declaring war at the beginning of games when everyone isn't set up well enough
        // For mods we can't check the number of cities, so we will check the population instead.
        var totalPopulation = 0;
        foreach (var city in civView.Cities()) totalPopulation = unchecked(totalPopulation + city.GetPopulationCount());
        if (totalPopulation < 12) return; // FAR too early for that what are you thinking!

        //evaluate war
        var targetCivs = civView.GetKnownCivs()
            .Where(it => !(it.IsDefeated() || it.Equals(civView) || it.Cities().Count == 0 || !civView.GetDiplomacyManagerWith(it).NotNull().CanDeclareWar()
                || !it.Cities().Any(city => civView.HasExplored(city.GetCenterTile()))))
            .ToList();
        // If the AI declares war on a civ without knowing the location of any cities,
        // it'll just keep amassing an army and not sending it anywhere, and end up at a massive disadvantage.

        if (targetCivs.Count == 0) return;

        var targetCivsWithMotivation = targetCivs
            .Select(it => (it.GetCiv(), MotivationToAttackAutomation.HasAtLeastMotivationToAttack(civView.GetCiv(), it, 0f)))
            .Where(it => it.Item2 > 0).ToList();

        DeclareWarTargetAutomation.ChooseDeclareWarTarget(civView, targetCivsWithMotivation);
    }

    internal static void OfferPeaceTreaty(CivView civView)
    {
        var civInfo = civView.GetCiv();
        if (!civView.IsAtWar() || civView.Cities().Count == 0 || civInfo.Diplomacy.Count == 0) return;

        var enemiesCiv = civInfo.Diplomacy
            .Where(it => it.Value.DiplomaticStatus == DiplomaticStatus.War)
            .Select(it => it.Value.OtherCiv)
            .Where(it => !(it == civInfo || it.IsBarbarian || it.Cities.Count == 0
                        || it.GetDiplomacyManager(civInfo).NotNull().HasFlag(DiplomacyFlags.DeclaredWar)
                        || civInfo.GetDiplomacyManager(it).NotNull().HasFlag(DiplomacyFlags.DeclaredWar)))
            .Where(it => !civInfo.GetDiplomacyManager(it).NotNull().HasFlag(DiplomacyFlags.DeclinedPeace))
            // Don't allow AIs to offer peace to city states allied with their enemies
            .Where(it => !(it.IsCityState && it.AllyCiv != null && civInfo.IsAtWarWith(it.AllyCiv)))
            // ignore civs that we have already offered peace this turn as a counteroffer to another civ's peace offer
            .Where(it => !it.TradeRequests.Any(tradeRequest => tradeRequest.RequestingCiv == civView.CivId && tradeRequest.Trade.IsPeaceTreaty()))
            .ToList();

        foreach (var enemy in enemiesCiv)
        {
            if (MotivationToAttackAutomation.HasAtLeastMotivationToAttack(civInfo, ViewOf(civInfo).GetForeignCivView(enemy), 10f) >= 10)
            {
                // We can still fight. Refuse peace.
                continue;
            }

            if (enemy.Cities.Any(it => (float)it.Health / it.GetMaxHealth() < 0.5f)) // We are just about to take their city!
                continue;

            if (civInfo.GetStatForRanking(RankingType.Force) - 0.8f * ThreatManager.GetCombinedForceOfWarringCivs(ViewOf(civInfo).CivView) > 0)
            {
                var randomSeed = unchecked(civInfo.GameInfo.Civilizations.ToList().IndexOf(enemy) + civInfo.GetCivsAtWarWith().Count() + 123 * civInfo.GameInfo.Turns);
                if (civInfo.GameInfo.SeededRandom(randomSeed).NextInt(100) > 80) continue;
            }

            // pay for peace
            var tradeLogic = TradeEvaluationSeam.NewLogic(civInfo, enemy);
            var tradeOffer = new TradeOffer(Constants.PeaceTreaty, TradeOfferType.Treaty, civInfo.GameInfo.Speed);

            tradeLogic.CurrentTrade.OurOffers.Add(tradeOffer);
            tradeLogic.CurrentTrade.TheirOffers.Add(tradeOffer);

            if (enemy.IsMajorCiv())
            {
                var moneyWeNeedToPay = unchecked(-TradeEvaluationSeam.Of(civInfo).EvaluatePeaceCostForThem(civInfo, enemy));

                if (civView.Gold > 0 && moneyWeNeedToPay > 0)
                {
                    if (moneyWeNeedToPay > civView.Gold)
                    {
                        moneyWeNeedToPay = civView.Gold; // As much as possible
                    }
                    tradeLogic.CurrentTrade.OurOffers.Add(
                        new TradeOffer("Gold", TradeOfferType.Gold, moneyWeNeedToPay, civInfo.GameInfo.Speed));
                }
                else if (moneyWeNeedToPay < -100)
                {
                    var moneyTheyNeedToPay = Math.Min(unchecked(moneyWeNeedToPay < 0 ? -moneyWeNeedToPay : moneyWeNeedToPay), enemy.Gold);
                    if (moneyTheyNeedToPay > 0)
                    {
                        tradeLogic.CurrentTrade.TheirOffers.Add(
                            new TradeOffer("Gold", TradeOfferType.Gold, moneyTheyNeedToPay, civInfo.GameInfo.Speed));
                    }
                }
            }

            enemy.TradeRequests.Add(new TradeRequest(civView.CivId, tradeLogic.CurrentTrade.Reverse()));
        }
    }

    internal static void AskForHelp(CivView civView)
    {
        var civInfo = civView.GetCiv();
        if (!civView.IsAtWar() || civView.Cities().Count == 0 || civInfo.Diplomacy.Count == 0) return;

        var enemyCivs = civInfo.GetCivsAtWarWith().Where(it => it.IsMajorCiv())
            .OrderByDescending(it => it.GetStatForRanking(RankingType.Force))
            .ToList();
        foreach (var enemyCiv in enemyCivs)
        {
            var potentialAllies = ThreatManager.GetNeighboringCivilizations(ViewOf(enemyCiv).CivView).Select(it => it.GetCiv())
                .Where(it =>
                    civInfo.Knows(it) && !it.IsAtWarWith(enemyCiv)
                    && civInfo.GetDiplomacyManager(it).NotNull().IsRelationshipLevelGE(RelationshipLevel.Friend)
                    && !it.GetDiplomacyManager(civInfo).NotNull().HasFlag(DiplomacyFlags.DeclinedJoinWarOffer))
                .OrderByDescending(it => it.GetStatForRanking(RankingType.Force))
                .ToList();
            var civToAsk = potentialAllies.FirstOrDefault(it =>
                DeclareWarPlanEvaluator.EvaluateJoinOurWarPlan(civInfo, enemyCiv, it, null) > 0);
            if (civToAsk == null) continue;

            var tradeLogic = TradeEvaluationSeam.NewLogic(civInfo, civToAsk);
            // Kotlin note: add gold offer here
            tradeLogic.CurrentTrade.TheirOffers.Add(new TradeOffer(enemyCiv.CivId, TradeOfferType.WarDeclaration, civInfo.GameInfo.Speed));
            civToAsk.TradeRequests.Add(new TradeRequest(civView.CivId, tradeLogic.CurrentTrade.Reverse()));
        }
    }

    private static bool AreWeOfferingTrade(CivView civView, CivilizationClass otherCiv, string offerName) =>
        otherCiv.TradeRequests.Where(request => request.RequestingCiv == civView.CivId)
            .Any(trade => trade.Trade.OurOffers.Any(offer => offer.Name == offerName)
                    || trade.Trade.TheirOffers.Any(offer => offer.Name == offerName));

    private const int MinUnitsNearBorderToIssueDemand = 8;
    private const int MinPercentForceValueNearBorderToIssueDemand = 50; // minimum 1

    /// <summary>
    /// Checks if any civ have positioned large portions of their troops along our borders.
    /// Usually indicates an imminent attack.
    /// </summary>
    internal static void CheckMilitaryPresenceNearBorder(CivView civView)
    {
        var civInfo = civView.GetCiv();
        var nearbyTiles = new LinkedHashSet<Map.Tiles.Tile>(civInfo.Cities
            .SelectMany(it => it.GetTiles())
            .SelectMany(it => it.GetTilesInDistance(2))
            .Where(it => it.GetOwner() != civInfo) // skip open borders
            .Where(it => it.IsVisible(civInfo)));

        // only counts what is visible to us
        var nearbyUnitCountByCiv = new Counter<CivilizationClass>();
        var nearbyForceByCiv = new Counter<CivilizationClass>();

        foreach (var tile in nearbyTiles)
        {
            var unit = tile.MilitaryUnit;
            if (unit == null) continue;
            if (!unit.Civ.IsMajorCiv() || unit.Civ == civInfo || !unit.IsVisibleTo(civInfo))
                continue;
            nearbyUnitCountByCiv.Add(unit.Civ, 1);
            nearbyForceByCiv.Add(unit.Civ, unit.GetForceEvaluation());
        }

        void DecideWhetherToDenounce(CivilizationClass otherCiv)
        {
            // this ultimatum can currently only be made by AI to human players, to avoid abuse
            if (!otherCiv.IsHuman())
                return;
            var ourDiplomacy = civInfo.GetDiplomacyManager(otherCiv).NotNull();
            // don't check violation if we are at war
            if (ourDiplomacy.DiplomaticStatus == DiplomaticStatus.War)
                return;
            // ignore if they promised they would not attack us
            if (ourDiplomacy.HasFlag(DiplomacyFlags.AgreedToNotAttackUs))
                return;
            var theirDiplomacy = otherCiv.GetDiplomacyManager(civInfo).NotNull();
            // let's not doubt our allies
            if (ourDiplomacy.HasFlag(DiplomacyFlags.DeclarationOfFriendship)
                || ourDiplomacy.HasFlag(DiplomacyFlags.DefensivePact)
                || theirDiplomacy.HasOpenBorders)
                return;
            // ignore if they only have a few units near our borders (relevant in early game)
            if (nearbyUnitCountByCiv[otherCiv] < MinUnitsNearBorderToIssueDemand)
                return;
            var threatAssessment = Automation.ThreatAssessment(civView, ViewOf(civInfo).GetForeignCivView(otherCiv));
            // ignore if they are weak, stay silent if they are too strong
            if (threatAssessment == ThreatLevel.VeryLow || threatAssessment == ThreatLevel.VeryHigh)
                return;
            var nearbyForce = nearbyForceByCiv[otherCiv];
            // ignore if most of their military is elsewhere
            var forceCutoff = nearbyForce / (MinPercentForceValueNearBorderToIssueDemand / 100f);
            var totalForce = 0;
            foreach (var unit in otherCiv.Units.GetCivUnits().Where(it => it.IsMilitary()))
            {
                totalForce = unchecked(totalForce + unit.GetForceEvaluation());
                if (totalForce > forceCutoff)
                    return;
            }
            // let's ask what's up
            ourDiplomacy.SetFlag(
                DiplomacyFlags.MilitaryPresenceNearBorderOrAttackedUsDespitePromise,
                30,
                true);
        }

        foreach (var otherCiv in civInfo.GetKnownCivs().ToList())
            DecideWhetherToDenounce(otherCiv);
    }

    /// <summary>Denounce if the AI's opinion of the other civ drops by this amount or more</summary>
    public const float DenounceRequiredOpinionChangeInitial = -65f;
    /// <summary>Adjusts the willingness to denounce friends and enemies</summary>
    public const float DenounceRequiredOpinionChangeBase = 1.005f;
    /// <summary>Makes the AI less willing to denounce multiple civs at the same time</summary>
    public const float ConcurrentDenouncementsRequiredOpinionChangeBase = 1.1f;

    /// <summary>Check if the civ has become frustrated with other civs. If so, denounce those civs.</summary>
    internal static void Denounce(CivView civView)
    {
        var civInfo = civView.GetCiv();

        /*
         * This is not an official formula - modify or replace as needed.
         *
         * Whether to denounce is determined by how rapidly opinion has declined, the current relationship level, and personality traits.
         *
         * With the current formula, the AI will denounce if opinion drops rapidly from:
         * 135 to 50
         * 65 to 0
         * 0 to -50
         * -60 to -100
         * Adjust with DiplomacyManager.EMA_PERIOD and other constants (see above)
         */
        float RequiredOpinionChange(DiplomacyManager diplomacy, int numberOfActiveDenouncements)
        {
            var personality = diplomacy.CivInfo.GetPersonality();
            if (personality.DenounceWillingness == 0f)
                return float.NegativeInfinity; // never denounce
            var personalityModifier = 1f / personality.ScaledFocus(PersonalityValue.DenounceWillingness);
            var opinionModifier = KotlinMath.Pow(DenounceRequiredOpinionChangeBase, diplomacy.OpinionOfOtherCiv());
            var multipleDenouncementsModifier = KotlinMath.Pow(ConcurrentDenouncementsRequiredOpinionChangeBase, numberOfActiveDenouncements);
            return DenounceRequiredOpinionChangeInitial * personalityModifier * opinionModifier * multipleDenouncementsModifier;
        }

        // debugging: records every civ's opinion of every other civ
        Log.Debug(civView.CivName);
        void Debug(DiplomacyManager diplomacy, int numberOfActiveDenouncements)
        {
            Log.Debug(
                "-> %s: %.1f (%.1f), %.1f / %.1f",
                diplomacy.OtherCivName,
                (Func<object?>)(() => diplomacy.OpinionOfOtherCiv()),
                (Func<object?>)(() => diplomacy.SmoothedOpinionOfOtherCiv),
                (Func<object?>)(() => diplomacy.SmoothedOpinionDelta()),
                (Func<object?>)(() => RequiredOpinionChange(diplomacy, numberOfActiveDenouncements)));
        }

        var relationships = civInfo.GetKnownCivs()
            .Where(it => it.IsMajorCiv())
            .Select(it => civInfo.GetDiplomacyManager(it).NotNull()) // ok because known civs are by definition those we have a relationship with
            .ToList();

        var numberOfActiveDenouncements = relationships
            .Count(it => it.HasFlag(DiplomacyFlags.Denunciation));

        var denounceableRelationships = relationships
            .Select(it =>
            {
                Debug(it, numberOfActiveDenouncements);
                return it;
            }).ToList()
            .Where(it => it.DiplomaticStatus != DiplomaticStatus.War
                && !it.HasFlag(DiplomacyFlags.DeclarationOfFriendship)
                && !it.HasFlag(DiplomacyFlags.Denunciation))
            .ToList();

        foreach (var relationship in denounceableRelationships)
        {
            // Kotlin note: consider consequences of denouncing others
            // compare our current opinion with the smoothed opinion
            var opinionChange = relationship.SmoothedOpinionDelta();
            // denounce if opinion dropped too quickly
            if (opinionChange <= RequiredOpinionChange(relationship, numberOfActiveDenouncements))
            {
                relationship.Denounce();
                numberOfActiveDenouncements++;
            }
        }
    }
}
