using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Models.Ruleset.Nation;
using Unciv.UI.Screens.VictoryScreen;
using Unciv.Utils;
using Unciv.View;
using CivilizationClass = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Logic.Automation.Civilization;

// Ported from core/src/com/unciv/logic/automation/civilization/DeclareWarPlanEvaluator.kt (T088). Purity annotations dropped (T027 policy).
// Kotlin Int/Float mixing is kept: forces are Int stats, the motivations Float. Int sums are unchecked loops (LINQ Sum(int) throws on overflow).

/// <summary>Contains the logic for evaluating how we want to declare war on another civ.</summary>
public static class DeclareWarPlanEvaluator
{
    private static GameView ViewOf(CivilizationClass civ) => new((GameInfo)civ.GameInfo, civ);

    private static int ForceOfCivsAtWarWith(CivilizationClass civ)
    {
        var sum = 0;
        foreach (var other in civ.GetCivsAtWarWith()) sum = unchecked(sum + other.GetStatForRanking(RankingType.Force));
        return sum;
    }

    /// <summary>
    /// How much motivation <paramref name="civInfo"/> has to do a team war with <paramref name="teamCiv"/> against <paramref name="target"/>.
    ///
    /// This style of declaring war favors fighting stronger civilizations.
    /// </summary>
    /// <returns>The movtivation of the plan. If it is &gt; 0 then we can declare the war.</returns>
    public static float EvaluateTeamWarPlan(CivilizationClass civInfo, CivilizationClass target, CivilizationClass teamCiv, float? givenMotivation)
    {
        var teamCivDiplo = civInfo.GetDiplomacyManager(teamCiv).NotNull();
        if (civInfo.GetPersonality()[PersonalityValue.DeclareWar] == 0f) return -1000f;
        if (teamCivDiplo.IsRelationshipLevelLT(RelationshipLevel.Neutral)) return -1000f;

        var motivation = givenMotivation
            ?? MotivationToAttackAutomation.HasAtLeastMotivationToAttack(civInfo, ViewOf(civInfo).GetForeignCivView(target), 0f);

        if (teamCivDiplo.IsRelationshipLevelEQ(RelationshipLevel.Neutral)) motivation -= 5f;
        // Make sure that they can actually help us with the target
        if (!ThreatManager.GetNeighboringCivilizations(ViewOf(teamCiv).CivView).Any(it => it.GetCiv() == target))
        {
            motivation -= 40f;
        }

        var civForce = civInfo.GetStatForRanking(RankingType.Force);
        var targetForce = target.GetStatForRanking(RankingType.Force);
        var teamCivForce = Math.Max(teamCiv.GetStatForRanking(RankingType.Force) - 0.8f * ThreatManager.GetCombinedForceOfWarringCivs(ViewOf(teamCiv).CivView), 100f);

        // A higher motivation means that we can be riskier
        var multiplier = motivation < 5 ? 1.2f
            : motivation < 10 ? 1.1f
            : motivation < 20 ? 1f
            : 0.8f;
        if (civForce + teamCivForce < targetForce * multiplier)
        {
            // We are weaker then them even with our combined forces
            // If they have twice our combined force we will have -30 motivation
            motivation -= 30 * ((targetForce * multiplier) / (teamCivForce + civForce) - 1);
        }
        else if (civForce + teamCivForce > targetForce * 2)
        {
            // Why gang up on such a weaker enemy when we can declare war ourselves?
            // If our combined force is twice their force we will have -20 motivation
            motivation -= 20 * ((civForce + teamCivForce) / targetForce - 1);
        }

        var civScore = civInfo.GetStatForRanking(RankingType.Score);
        var teamCivScore = teamCiv.GetStatForRanking(RankingType.Score);
        var targetCivScore = target.GetStatForRanking(RankingType.Score);

        if (teamCivScore > civScore * 1.4f && teamCivScore >= targetCivScore)
        {
            // If teamCiv has more score than us and the target they are likely in a good position already
            motivation -= 20 * ((teamCivScore / (civScore * 1.4f)) - 1);
        }
        return motivation - 20f;
    }

    /// <summary>
    /// How much motivation <paramref name="civInfo"/> has to join <paramref name="civToJoin"/> in their war against <paramref name="target"/>.
    ///
    /// Favors protecting allies.
    /// </summary>
    /// <returns>The movtivation of the plan. If it is &gt; 0 then we can declare the war.</returns>
    public static float EvaluateJoinWarPlan(CivilizationClass civInfo, CivilizationClass target, CivilizationClass civToJoin, float? givenMotivation)
    {
        var thirdCivDiplo = civInfo.GetDiplomacyManager(civToJoin).NotNull();
        if (civInfo.GetPersonality()[PersonalityValue.DeclareWar] == 0f) return -1000f;
        if (thirdCivDiplo.IsRelationshipLevelLE(RelationshipLevel.Favorable)) return -1000f;

        var motivation = givenMotivation
            ?? MotivationToAttackAutomation.HasAtLeastMotivationToAttack(civInfo, ViewOf(civInfo).GetForeignCivView(target), 0f);
        // We need to be able to trust the thirdCiv at least somewhat
        if (thirdCivDiplo.DiplomaticStatus != DiplomaticStatus.DefensivePact &&
            thirdCivDiplo.OpinionOfOtherCiv() + motivation * 2 < 80)
        {
            motivation -= 80f - (thirdCivDiplo.OpinionOfOtherCiv() + motivation * 2);
        }
        if (!ThreatManager.GetNeighboringCivilizations(ViewOf(civToJoin).CivView).Any(it => it.GetCiv() == target))
        {
            motivation -= 20f;
        }

        var targetForce = Math.Max(target.GetStatForRanking(RankingType.Force) - 0.8f * ForceOfCivsAtWarWith(target), 100f);
        var civForce = civInfo.GetStatForRanking(RankingType.Force);

        // They need to be at least half the targets size, and we need to be stronger than the target together
        var civToJoinForce = Math.Max(civToJoin.GetStatForRanking(RankingType.Force) - 0.8f * ForceOfCivsAtWarWith(civToJoin), 100f);
        if (civToJoinForce < targetForce / 2)
        {
            // Make sure that there is no wrap around
            motivation -= 10 * Math.Clamp(targetForce / civToJoinForce, -1000f, 1000f);
        }

        // A higher motivation means that we can be riskier
        var multiplier = motivation < 10 ? 1.4f
            : motivation < 15 ? 1.3f
            : motivation < 20 ? 1.2f
            : motivation < 25 ? 1f
            : 0.8f;
        if (civToJoinForce + civForce < targetForce * multiplier)
        {
            motivation -= Math.Clamp(20 * (targetForce * multiplier) / (civToJoinForce + civForce), -1000f, 1000f);
        }

        return motivation - 15;
    }

    /// <summary>
    /// How much motivation <paramref name="civInfo"/> has for <paramref name="civToJoin"/> to join them in their war against <paramref name="target"/>.
    /// </summary>
    /// <returns>The movtivation of the plan. If it is &gt;= 0 then we can accept their war offer.</returns>
    public static float EvaluateJoinOurWarPlan(CivilizationClass civInfo, CivilizationClass target, CivilizationClass civToJoin, float? givenMotivation)
    {
        if (civInfo.GetDiplomacyManager(civToJoin).NotNull().IsRelationshipLevelLT(RelationshipLevel.Favorable)) return -1000f;
        var motivation = givenMotivation ?? 0f;
        if (!ThreatManager.GetNeighboringCivilizations(ViewOf(civToJoin).CivView).Any(it => it.GetCiv() == target))
        {
            motivation -= 50f;
        }

        var targetForce = target.GetStatForRanking(RankingType.Force);
        var civForce = civInfo.GetStatForRanking(RankingType.Force);

        // If we have more force than all enemies and overpower this enemy then we don't need help
        if (civForce - ThreatManager.GetCombinedForceOfWarringCivs(ViewOf(civInfo).CivView) > targetForce * 2) return 0f;

        // They should to be at least half the targets size
        var thirdCivForce = Math.Max(civToJoin.GetStatForRanking(RankingType.Force) - 0.8f * ForceOfCivsAtWarWith(civToJoin), 100f);
        motivation += Math.Min(20 * (1 - thirdCivForce / (float)targetForce), 40f);

        // If we have less relative force then the target then we have more motivation to accept
        motivation += 20 * Math.Clamp(1 - civForce / (float)targetForce, -40f, 40f);

        return motivation - 20;
    }

    /// <summary>
    /// How much motivation <paramref name="civInfo"/> has to declare war against <paramref name="target"/> this turn.
    /// This can be through a prepared war or a suprise war.
    /// </summary>
    /// <returns>The movtivation of the plan. If it is &gt; 0 then we can declare the war.</returns>
    public static float EvaluateDeclareWarPlan(CivilizationClass civInfo, CivilizationClass target, float? givenMotivation)
    {
        if (civInfo.GetPersonality()[PersonalityValue.DeclareWar] == 0f) return -1000f;
        var motivation = givenMotivation
            ?? MotivationToAttackAutomation.HasAtLeastMotivationToAttack(civInfo, ViewOf(civInfo).GetForeignCivView(target), 0f);

        var diploManager = civInfo.GetDiplomacyManager(target).NotNull();

        if (diploManager.HasFlag(DiplomacyFlags.WaryOf) && diploManager.GetFlag(DiplomacyFlags.WaryOf) < 0)
        {
            var turnsToPlan = Math.Max(10 - (motivation / 10), 3f);
            var turnsToWait = turnsToPlan + diploManager.GetFlag(DiplomacyFlags.WaryOf);
            return motivation - turnsToWait * 3;
        }

        return motivation - 40;
    }

    /// <summary>How much motivation <paramref name="civInfo"/> has to start preparing for a war agaist <paramref name="target"/>.</summary>
    /// <returns>The motivation of the plan. If it is &gt; 0 then we can start planning the war.</returns>
    public static float EvaluateStartPreparingWarPlan(CivilizationClass civInfo, CivilizationClass target, float? givenMotivation)
    {
        var motivation = givenMotivation
            ?? MotivationToAttackAutomation.HasAtLeastMotivationToAttack(civInfo, ViewOf(civInfo).GetForeignCivView(target), 0f);

        // TODO: We use negative values in WaryOf for now so that we aren't adding any extra fields to the save file
        // This will very likely change in the future and we will want to build upon it
        var diploManager = civInfo.GetDiplomacyManager(target).NotNull();
        if (diploManager.HasFlag(DiplomacyFlags.WaryOf)) return 0f;

        return motivation - 15;
    }
}
