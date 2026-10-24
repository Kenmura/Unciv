using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Logic.Trade;
using Unciv.UI.Screens.VictoryScreen;
using Unciv.Utils;
using Unciv.View;
using CivilizationClass = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Logic.Automation.Civilization;

// Ported from core/src/com/unciv/logic/automation/civilization/DeclareWarTargetAutomation.kt (T088).
// Kotlin sortedByDescending is a stable sort: LINQ OrderByDescending is stable too.

public static class DeclareWarTargetAutomation
{
    /// <summary>
    /// Chooses a target civilization along with a plan of attack.
    /// Note that this doesn't guarantee that we will declare war on them immediately, or that we will end up declaring war at all.
    /// </summary>
    public static void ChooseDeclareWarTarget(CivView civView, List<(CivilizationClass, float)> civAttackMotivations)
    {
        var highestValueTargets = civAttackMotivations.OrderByDescending(it => it.Item1.GetStatForRanking(RankingType.Score)).ToList();

        foreach (var target in highestValueTargets)
        {
            if (TryDeclareWarWithPlan(civView, target.Item1, target.Item2))
                return; // We have successfully found a plan and started executing it!
        }
    }

    /// <summary>Determines a war plan against this <paramref name="target"/> and executes it if able.</summary>
    private static bool TryDeclareWarWithPlan(CivView civView, CivilizationClass target, float motivation)
    {
        if (!target.IsCityState)
        {
            if (motivation > 5 && TryTeamWar(civView, target, motivation)) return true;

            if (motivation >= 15 && TryJoinWar(civView, target, motivation)) return true;
        }

        if (motivation >= 20 && DeclareWar(civView, target, motivation)) return true;

        if (motivation >= 15 && PrepareWar(civView, target, motivation)) return true;

        return false;
    }

    /// <summary>
    /// The safest option for war is to invite a new ally to join the war with us.
    /// Together we are stronger and are more likely to take down bigger threats.
    /// </summary>
    private static bool TryTeamWar(CivView civView, CivilizationClass target, float motivation)
    {
        var civInfo = civView.GetCiv();
        var potentialAllies = civInfo.GetDiplomacyManager(target).NotNull().GetCommonKnownCivs()
            .Where(it => it.IsMajorCiv()
                && !civInfo.GetDiplomacyManager(it).NotNull().HasFlag(DiplomacyFlags.DeclinedJoinWarOffer)
                && civInfo.GetDiplomacyManager(it).NotNull().IsRelationshipLevelGE(RelationshipLevel.Neutral)
                && !it.IsAtWarWith(target))
            .OrderByDescending(it => it.GetStatForRanking(RankingType.Force))
            .ToList();

        foreach (var thirdCiv in potentialAllies)
        {
            if (DeclareWarPlanEvaluator.EvaluateTeamWarPlan(civInfo, target, thirdCiv, motivation) <= 0) continue;

            // Send them an offer
            var tradeLogic = TradeEvaluationSeam.NewLogic(civInfo, thirdCiv);
            tradeLogic.CurrentTrade.OurOffers.Add(new TradeOffer(target.CivId, TradeOfferType.WarDeclaration, civInfo.GameInfo.Speed));
            tradeLogic.CurrentTrade.TheirOffers.Add(new TradeOffer(target.CivId, TradeOfferType.WarDeclaration, civInfo.GameInfo.Speed));

            thirdCiv.TradeRequests.Add(new TradeRequest(civInfo.CivId, tradeLogic.CurrentTrade.Reverse()));

            return true;
        }

        return false;
    }

    /// <summary>The next safest aproach is to join an existing war on the side of an ally that is already at war with <paramref name="target"/>.</summary>
    private static bool TryJoinWar(CivView civView, CivilizationClass target, float motivation)
    {
        var civInfo = civView.GetCiv();
        var potentialAllies = civInfo.GetDiplomacyManager(target).NotNull().GetCommonKnownCivs()
            .Where(it => it.IsMajorCiv()
                && !civInfo.GetDiplomacyManager(it).NotNull().HasFlag(DiplomacyFlags.DeclinedJoinWarOffer)
                && civInfo.GetDiplomacyManager(it).NotNull().IsRelationshipLevelGE(RelationshipLevel.Favorable)
                && it.IsAtWarWith(target)) // Must be a civ not already at war with them
            .OrderByDescending(it => it.GetStatForRanking(RankingType.Force))
            .ToList();

        foreach (var thirdCiv in potentialAllies)
        {
            if (DeclareWarPlanEvaluator.EvaluateJoinWarPlan(civInfo, target, thirdCiv, motivation) <= 0) continue;

            // Send them an offer
            var tradeLogic = TradeEvaluationSeam.NewLogic(civInfo, thirdCiv);
            tradeLogic.CurrentTrade.OurOffers.Add(new TradeOffer(target.CivId, TradeOfferType.WarDeclaration, civInfo.GameInfo.Speed));
            // TODO: Maybe add in payment requests in some situations
            thirdCiv.TradeRequests.Add(new TradeRequest(civInfo.CivId, tradeLogic.CurrentTrade.Reverse()));

            return true;
        }

        return false;
    }

    /// <summary>Lastly, if our motivation is high enough and we don't have any better plans then lets just declare war.</summary>
    private static bool DeclareWar(CivView civView, CivilizationClass target, float motivation)
    {
        var civInfo = civView.GetCiv();
        if (DeclareWarPlanEvaluator.EvaluateDeclareWarPlan(civInfo, target, motivation) > 0)
        {
            civInfo.GetDiplomacyManager(target).NotNull().DeclareWar();
            return true;
        }
        return false;
    }

    /// <summary>Slightly safter is to silently plan an invasion and declare war later.</summary>
    private static bool PrepareWar(CivView civView, CivilizationClass target, float motivation)
    {
        // TODO: We use negative values in WaryOf for now so that we aren't adding any extra fields to the save file
        // This will very likely change in the future and we will want to build upon it
        var diploManager = civView.GetCiv().GetDiplomacyManager(target).NotNull();
        if (DeclareWarPlanEvaluator.EvaluateStartPreparingWarPlan(civView.GetCiv(), target, motivation) > 0)
        {
            diploManager.SetFlag(DiplomacyFlags.WaryOf, -1);
            return true;
        }
        return false;
    }
}
