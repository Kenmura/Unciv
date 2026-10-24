using Unciv.Logic.Automation.Civilization;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Models.Ruleset.Nation;
using Unciv.UI.Screens.VictoryScreen;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Core.Tests.Logic.Automation;

public static partial class DiplomacyAiTests
{
    // DeclareWarPlanEvaluator. Armed world: force A 82, B 28 (3 and 1 warriors), T 0 (no units, coerced to the 100 floor).
    [Collection("RulesetCache")]
    public class EvaluatorTests
    {
        private static World ArmedWorld(int bWarriors = 1, int tX = 0, int tY = 6)
        {
            var w = new World(tX: tX, tY: tY);
            w.AddWarriors(w.A, 3, -5, 0);
            w.AddWarriors(w.B, bWarriors, 5, 0);
            return w;
        }

        private static int Force(CivilizationObject civ) => civ.GetStatForRanking(RankingType.Force);

        private static void ForbidWar(World w, CivilizationObject civ)
        {
            w.Game.Ruleset.Personalities.Put("NoWar", new Personality { DeclareWar = 0f });
            civ.Nation.Personality = "NoWar";
        }

        // ---- team war

        [Fact]
        public void TeamWar_PacifistPersonality_IsMinus1000()
        {
            var w = ArmedWorld();
            ForbidWar(w, w.A);
            Assert.Equal(-1000f, DeclareWarPlanEvaluator.EvaluateTeamWarPlan(w.A, w.B, w.T, 50f));
        }

        [Fact]
        public void TeamWar_TeamCivBelowNeutral_IsMinus1000()
        {
            var w = ArmedWorld();
            w.SetOpinion(w.A, w.T, -20f);
            Assert.Equal(-1000f, DeclareWarPlanEvaluator.EvaluateTeamWarPlan(w.A, w.B, w.T, 50f));
        }

        [Fact]
        public void TeamWar_GangingUpOnAMuchWeakerTarget_Costs20PerExtraForceRatio()
        {
            var w = ArmedWorld();
            // motivation 30 - 5 (Neutral team civ) = 25 -> multiplier 0.8. civForce 82 + teamForce max(0 - 0, 100) = 182 > 2 * 28:
            // 25 - 20 * (182 / 28 - 1) = 25 - 110 = -85, minus the final 20.
            Assert.Equal(-105f, DeclareWarPlanEvaluator.EvaluateTeamWarPlan(w.A, w.B, w.T, 30f));
        }

        [Fact]
        public void TeamWar_WithAComparableTarget_IsJustTheMotivationMinusTheFixedCosts()
        {
            var w = ArmedWorld(bWarriors: 4);
            var targetForce = Force(w.B);
            Assert.InRange(targetForce, 91, 227); // 182 <= 2 * target and 182 >= target * 0.8: neither force adjustment applies
            Assert.Equal(30f - 5f - 20f, DeclareWarPlanEvaluator.EvaluateTeamWarPlan(w.A, w.B, w.T, 30f));
        }

        [Fact]
        public void TeamWar_WithAStrongerTarget_LosesMotivationForTheMissingForce()
        {
            var w = ArmedWorld(bWarriors: 12);
            var civForce = Force(w.A);
            var targetForce = Force(w.B);
            const float multiplier = 0.8f; // motivation 25 after the neutral malus
            Assert.True(civForce + 100 < targetForce * multiplier);
            var expected = 25f - 30 * (targetForce * multiplier / (100 + civForce) - 1) - 20f;
            Assert.Equal(expected, DeclareWarPlanEvaluator.EvaluateTeamWarPlan(w.A, w.B, w.T, 30f));
        }

        [Fact]
        public void TeamWar_TeamCivWithNoAccessToTheTarget_Loses40()
        {
            var w = ArmedWorld(bWarriors: 4, tX: -10, tY: 0); // 14 tiles from B: not a neighbour
            Assert.Equal(30f - 5f - 40f - 20f, DeclareWarPlanEvaluator.EvaluateTeamWarPlan(w.A, w.B, w.T, 30f));
        }

        [Fact]
        public void TeamWar_AFriendlyTeamCivSkipsTheNeutralMalus_AndAnUnknownMotivationIsComputed()
        {
            var w = ArmedWorld(bWarriors: 4);
            w.SetOpinion(w.A, w.T, 20f); // Favorable
            var given = DeclareWarPlanEvaluator.EvaluateTeamWarPlan(w.A, w.B, w.T, 30f);
            Assert.Equal(30f - 20f, given);
            var computed = DeclareWarPlanEvaluator.EvaluateTeamWarPlan(w.A, w.B, w.T, null);
            Assert.Equal(w.Motivation(w.A, w.B) - 20f, computed);
        }

        [Fact]
        public void TeamWar_ATeamCivWithMuchMoreScore_LosesMotivation()
        {
            var w = ArmedWorld(bWarriors: 4);
            w.SetOpinion(w.A, w.T, 20f);
            w.Game.AddCity(w.T, w.Game.GetTile(3, 8));
            w.Game.AddCity(w.T, w.Game.GetTile(-3, 8));
            var civScore = w.A.GetStatForRanking(RankingType.Score);
            var teamScore = w.T.GetStatForRanking(RankingType.Score);
            var targetScore = w.B.GetStatForRanking(RankingType.Score);
            Assert.True(teamScore > civScore * 1.4f && teamScore >= targetScore);
            var expected = 30f - 20 * (teamScore / (civScore * 1.4f) - 1) - 20f;
            Assert.Equal(expected, DeclareWarPlanEvaluator.EvaluateTeamWarPlan(w.A, w.B, w.T, 30f));
        }

        // ---- join war

        [Fact]
        public void JoinWar_PacifistOrUnfriendly_IsMinus1000()
        {
            var w = ArmedWorld();
            Assert.Equal(-1000f, DeclareWarPlanEvaluator.EvaluateJoinWarPlan(w.A, w.B, w.T, 50f)); // Neutral <= Favorable
            w.SetOpinion(w.A, w.T, 50f);
            ForbidWar(w, w.A);
            Assert.Equal(-1000f, DeclareWarPlanEvaluator.EvaluateJoinWarPlan(w.A, w.B, w.T, 50f));
        }

        [Fact]
        public void JoinWar_AFriend_JustCostsTheFixed15()
        {
            var w = ArmedWorld();
            w.SetOpinion(w.A, w.T, 50f);
            // opinion 50 + 2 * 30 = 110 >= 80: no trust malus; T is a neighbour of B; forces 100 / 82 / 100 never come into play
            Assert.Equal(30f - 15f, DeclareWarPlanEvaluator.EvaluateJoinWarPlan(w.A, w.B, w.T, 30f));
        }

        [Fact]
        public void JoinWar_ALowMotivationDoesNotTrustTheThirdCiv()
        {
            var w = ArmedWorld();
            w.SetOpinion(w.A, w.T, 50f);
            // 50 + 0 < 80: motivation 0 -> -30; multiplier 1.4: 182 >= 100 * 1.4; -15
            Assert.Equal(-30f - 15f, DeclareWarPlanEvaluator.EvaluateJoinWarPlan(w.A, w.B, w.T, 0f));
        }

        [Fact]
        public void JoinWar_ADefensivePactPartnerIsTrustedWithoutOpinion()
        {
            var w = ArmedWorld();
            w.SetOpinion(w.A, w.T, 50f);
            w.Diplo(w.A, w.T).DiplomaticStatus = DiplomaticStatus.DefensivePact;
            Assert.Equal(0f - 15f, DeclareWarPlanEvaluator.EvaluateJoinWarPlan(w.A, w.B, w.T, 0f));
        }

        [Fact]
        public void JoinWar_TheThirdCivMustReachTheTarget_AndBeStrongEnough()
        {
            var w = ArmedWorld(bWarriors: 12, tX: -10, tY: 0);
            w.SetOpinion(w.A, w.T, 50f);
            var targetForce = System.Math.Max(Force(w.B) - 0.8f * 0, 100f);
            var civForce = Force(w.A);
            const float civToJoinForce = 100f;
            Assert.True(civToJoinForce < targetForce / 2);
            var motivation = 30f - 20f; // not a neighbour of the target
            motivation -= 10 * System.Math.Clamp(targetForce / civToJoinForce, -1000f, 1000f);
            var multiplier = motivation < 10 ? 1.4f : 1.3f;
            Assert.True(civToJoinForce + civForce < targetForce * multiplier);
            motivation -= System.Math.Clamp(20 * (targetForce * multiplier) / (civToJoinForce + civForce), -1000f, 1000f);
            Assert.Equal(motivation - 15, DeclareWarPlanEvaluator.EvaluateJoinWarPlan(w.A, w.B, w.T, 30f));
        }

        [Fact]
        public void JoinWar_UsesTheComputedMotivationWhenNoneIsGiven()
        {
            var w = ArmedWorld();
            w.SetOpinion(w.A, w.T, 50f);
            var motivation = w.Motivation(w.A, w.B);
            var expected = motivation - (80f - (50f + motivation * 2)) - 15; // opinion 50 + 2 * (-13) < 80
            Assert.Equal(1.4f, 1.4f); // multiplier 1.4: 100 + 82 < 100 * 1.4 is false
            Assert.Equal(expected, DeclareWarPlanEvaluator.EvaluateJoinWarPlan(w.A, w.B, w.T, null));
        }

        // ---- join our war

        [Fact]
        public void JoinOurWar_AnUnfavorableCiv_IsMinus1000()
        {
            var w = ArmedWorld();
            Assert.Equal(-1000f, DeclareWarPlanEvaluator.EvaluateJoinOurWarPlan(w.A, w.B, w.T, null));
        }

        [Fact]
        public void JoinOurWar_WhenWeOverpowerTheTarget_NeedsNoHelp()
        {
            var w = ArmedWorld();
            w.SetOpinion(w.A, w.T, 20f);
            Assert.Equal(0f, DeclareWarPlanEvaluator.EvaluateJoinOurWarPlan(w.A, w.B, w.T, null)); // 82 - 0 > 28 * 2
        }

        [Fact]
        public void JoinOurWar_WithAStrongTarget_WeighsTheRelativeForces()
        {
            var w = ArmedWorld(bWarriors: 12);
            w.SetOpinion(w.A, w.T, 20f);
            float targetForce = Force(w.B);
            float civForce = Force(w.A);
            const float thirdCivForce = 100f;
            var expected = System.Math.Min(20 * (1 - thirdCivForce / targetForce), 40f)
                + 20 * System.Math.Clamp(1 - civForce / targetForce, -40f, 40f) - 20;
            Assert.Equal(expected, DeclareWarPlanEvaluator.EvaluateJoinOurWarPlan(w.A, w.B, w.T, null));
        }

        [Fact]
        public void JoinOurWar_ACivThatCannotReachTheTarget_Loses50()
        {
            var w = ArmedWorld(bWarriors: 12, tX: -10, tY: 0);
            w.SetOpinion(w.A, w.T, 20f);
            float targetForce = Force(w.B);
            float civForce = Force(w.A);
            var expected = -50f + System.Math.Min(20 * (1 - 100f / targetForce), 40f)
                + 20 * System.Math.Clamp(1 - civForce / targetForce, -40f, 40f) - 20;
            Assert.Equal(expected, DeclareWarPlanEvaluator.EvaluateJoinOurWarPlan(w.A, w.B, w.T, 0f));
        }

        // ---- declare war / prepare

        [Fact]
        public void DeclareWar_PacifistPersonality_IsMinus1000()
        {
            var w = ArmedWorld();
            ForbidWar(w, w.A);
            Assert.Equal(-1000f, DeclareWarPlanEvaluator.EvaluateDeclareWarPlan(w.A, w.B, 50f));
        }

        [Fact]
        public void DeclareWar_ASurpriseWar_Costs40()
        {
            var w = ArmedWorld();
            Assert.Equal(10f, DeclareWarPlanEvaluator.EvaluateDeclareWarPlan(w.A, w.B, 50f));
            Assert.Equal(w.Motivation(w.A, w.B) - 40f, DeclareWarPlanEvaluator.EvaluateDeclareWarPlan(w.A, w.B, null));
        }

        [Fact]
        public void DeclareWar_APreparedWar_CostsThreeMotivationPerRemainingTurn()
        {
            var w = ArmedWorld();
            w.Diplo(w.A, w.B).SetFlag(DiplomacyFlags.WaryOf, -4);
            // turnsToPlan = max(10 - 30 / 10, 3) = 7; turnsToWait = 7 - 4 = 3
            Assert.Equal(30f - 9f, DeclareWarPlanEvaluator.EvaluateDeclareWarPlan(w.A, w.B, 30f));
            // a very high motivation floors the planning at 3 turns: turnsToWait = -1
            Assert.Equal(200f + 3f, DeclareWarPlanEvaluator.EvaluateDeclareWarPlan(w.A, w.B, 200f));
        }

        [Fact]
        public void StartPreparing_IsRefusedWhenAlreadyWary_AndOtherwiseCosts15()
        {
            var w = ArmedWorld();
            Assert.Equal(35f, DeclareWarPlanEvaluator.EvaluateStartPreparingWarPlan(w.A, w.B, 50f));
            Assert.Equal(w.Motivation(w.A, w.B) - 15f, DeclareWarPlanEvaluator.EvaluateStartPreparingWarPlan(w.A, w.B, null));
            w.Diplo(w.A, w.B).SetFlag(DiplomacyFlags.WaryOf, -1);
            Assert.Equal(0f, DeclareWarPlanEvaluator.EvaluateStartPreparingWarPlan(w.A, w.B, 50f));
        }
    }
}
