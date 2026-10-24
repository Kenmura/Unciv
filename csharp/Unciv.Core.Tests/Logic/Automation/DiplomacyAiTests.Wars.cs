using Unciv.Logic.Automation.Civilization;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Logic.Trade;
using Unciv.Models.Ruleset.Nation;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Core.Tests.Logic.Automation;

public static partial class DiplomacyAiTests
{
    // DiplomacyAutomation: peace offers, asking for help, border presence and denouncing.
    [Collection("RulesetCache")]
    public class WarAndOpinionTests
    {
        private static World WarWorld(bool bIsHuman = false, int bWarriors = 1)
        {
            var w = new World(bIsHuman);
            w.AddWarriors(w.A, 3, -5, 0);
            w.AddWarriors(w.B, bWarriors, 5, 0);
            w.Diplo(w.A, w.B).DeclareWar();
            ClearWarFlags(w);
            return w;
        }

        /// <summary>DeclareWar leaves DeclaredWar and DeclinedPeace flags (no peace right after a declaration): clear them.</summary>
        private static void ClearWarFlags(World w)
        {
            foreach (var flag in new[] { DiplomacyFlags.DeclaredWar, DiplomacyFlags.DeclinedPeace })
            {
                w.Diplo(w.A, w.B).RemoveFlag(flag);
                w.Diplo(w.B, w.A).RemoveFlag(flag);
            }
        }

        private static void OfferPeace(World w) =>
            w.RunUntil(() => DiplomacyAutomation.OfferPeaceTreaty(w.CivViewOf(w.A)), () => w.B.TradeRequests.Count != 0);

        // ---- peace

        [Fact]
        public void OfferPeaceTreaty_AsksTheEnemyForPeace()
        {
            var w = WarWorld();
            OfferPeace(w); // 80% of the turns pass the random "keep fighting" roll
            var request = Assert.Single(w.B.TradeRequests);
            Assert.Equal(w.A.CivId, request.RequestingCiv);
            Assert.True(request.Trade.IsPeaceTreaty() || request.Trade.TheirOffers.Any(it => it.Name == Constants.PeaceTreaty));
            Assert.Contains(request.Trade.OurOffers, it => it.Name == Constants.PeaceTreaty && it.Type == TradeOfferType.Treaty);
            Assert.DoesNotContain(request.Trade.OurOffers.Concat(request.Trade.TheirOffers), it => it.Type == TradeOfferType.Gold);
        }

        [Fact]
        public void OfferPeaceTreaty_AMajorEnemyIsPaidForPeaceWithOurGold()
        {
            var w = WarWorld();
            w.Evaluation.PeaceCost = -80; // we would have to pay 80
            w.A.AddGold(50);
            OfferPeace(w);
            var request = Assert.Single(w.B.TradeRequests);
            var gold = Assert.Single(request.Trade.TheirOffers, it => it.Type == TradeOfferType.Gold);
            Assert.Equal(50, gold.Amount); // as much as possible
        }

        [Fact]
        public void OfferPeaceTreaty_WithEnoughGold_PaysTheFullPrice()
        {
            var w = WarWorld();
            w.Evaluation.PeaceCost = -80;
            w.A.AddGold(500);
            OfferPeace(w);
            var gold = Assert.Single(w.B.TradeRequests[0].Trade.TheirOffers, it => it.Type == TradeOfferType.Gold);
            Assert.Equal(80, gold.Amount);
        }

        [Fact]
        public void OfferPeaceTreaty_ARichAggressorAsksTheEnemyToPay()
        {
            var w = WarWorld();
            w.Evaluation.PeaceCost = 250; // they should pay 250
            w.B.AddGold(120);
            OfferPeace(w);
            var gold = Assert.Single(w.B.TradeRequests[0].Trade.OurOffers, it => it.Type == TradeOfferType.Gold);
            Assert.Equal(120, gold.Amount); // at most what they have
        }

        [Fact]
        public void OfferPeaceTreaty_ABrokeEnemyIsNotAskedToPay()
        {
            var w = WarWorld();
            w.Evaluation.PeaceCost = 250;
            OfferPeace(w);
            Assert.DoesNotContain(w.B.TradeRequests[0].Trade.OurOffers, it => it.Type == TradeOfferType.Gold);
        }

        [Fact]
        public void OfferPeaceTreaty_AGameThatIsNotGoingBadlyKeepsFightingWhileMotivated()
        {
            var w = WarWorld();
            w.AddWarriors(w.A, 12, -8, -4);
            w.Diplo(w.A, w.B).SetFlag(DiplomacyFlags.Denunciation, 10);
            w.SetOpinion(w.A, w.B, -90f);
            Assert.True(w.Motivation(w.A, w.B, 10f) >= 10f);
            w.Game.GameInfo.Turns = 3;
            DiplomacyAutomation.OfferPeaceTreaty(w.CivViewOf(w.A));
            Assert.Empty(w.B.TradeRequests);
        }

        [Fact]
        public void OfferPeaceTreaty_DoesNotOfferWhenWeAreAboutToTakeACity()
        {
            var w = WarWorld();
            w.CityB.Health = 50;
            w.Game.GameInfo.Turns = 3;
            DiplomacyAutomation.OfferPeaceTreaty(w.CivViewOf(w.A));
            Assert.Empty(w.B.TradeRequests);
        }

        [Fact]
        public void OfferPeaceTreaty_Skips_DeclinedDeclaredAndAlreadyOffered()
        {
            var w = WarWorld();
            w.Diplo(w.A, w.B).SetFlag(DiplomacyFlags.DeclinedPeace, 5);
            for (var turn = 1; turn <= 30; turn++)
            {
                w.Game.GameInfo.Turns = turn;
                DiplomacyAutomation.OfferPeaceTreaty(w.CivViewOf(w.A));
            }
            Assert.Empty(w.B.TradeRequests);

            w.Diplo(w.A, w.B).RemoveFlag(DiplomacyFlags.DeclinedPeace);
            w.Diplo(w.B, w.A).SetFlag(DiplomacyFlags.DeclaredWar, 5);
            for (var turn = 1; turn <= 30; turn++)
            {
                w.Game.GameInfo.Turns = turn;
                DiplomacyAutomation.OfferPeaceTreaty(w.CivViewOf(w.A));
            }
            Assert.Empty(w.B.TradeRequests);

            w.Diplo(w.B, w.A).RemoveFlag(DiplomacyFlags.DeclaredWar);
            OfferPeace(w);
            var count = w.B.TradeRequests.Count;
            for (var turn = 1; turn <= 30; turn++)
            {
                w.Game.GameInfo.Turns = turn;
                DiplomacyAutomation.OfferPeaceTreaty(w.CivViewOf(w.A)); // the pending peace request is not repeated
            }
            Assert.Equal(count, w.B.TradeRequests.Count);
        }

        [Fact]
        public void OfferPeaceTreaty_DoesNothingAtPeace()
        {
            var w = new World();
            w.AddWarriors(w.A, 3, -5, 0);
            DiplomacyAutomation.OfferPeaceTreaty(w.CivViewOf(w.A));
            Assert.Empty(w.B.TradeRequests);
        }

        [Fact]
        public void OfferPeaceTreaty_CityStatesAreOfferedPeaceWithoutGold()
        {
            var w = new World(bIsCityState: true);
            w.AddWarriors(w.A, 3, -5, 0);
            w.AddWarriors(w.B, 1, 5, 0);
            w.Diplo(w.A, w.B).DeclareWar();
            ClearWarFlags(w);
            w.Evaluation.PeaceCost = -500;
            w.A.AddGold(500);
            OfferPeace(w);
            Assert.DoesNotContain(w.B.TradeRequests[0].Trade.TheirOffers, it => it.Type == TradeOfferType.Gold);
        }

        // ---- asking for help

        [Fact]
        public void AskForHelp_AsksAFriendWhoCanReachTheEnemy()
        {
            var w = WarWorld(bWarriors: 12);
            w.SetOpinion(w.A, w.T, 50f); // Friend
            Assert.True(DeclareWarPlanEvaluator.EvaluateJoinOurWarPlan(w.A, w.B, w.T, null) > 0f);
            DiplomacyAutomation.AskForHelp(w.CivViewOf(w.A));
            var request = Assert.Single(w.T.TradeRequests);
            Assert.Equal(w.A.CivId, request.RequestingCiv);
            // we ask them (their offers = our request for a war declaration on B): reversed from T's side it is T's offer
            var offer = Assert.Single(request.Trade.OurOffers);
            Assert.Equal((w.B.CivId, TradeOfferType.WarDeclaration), (offer.Name, offer.Type));
        }

        [Fact]
        public void AskForHelp_NeedsAFriendNotYetAtWarAndNotAskedBefore()
        {
            var w = WarWorld(bWarriors: 12);
            w.SetOpinion(w.A, w.T, 20f); // only Favorable
            DiplomacyAutomation.AskForHelp(w.CivViewOf(w.A));
            Assert.Empty(w.T.TradeRequests);
            w.SetOpinion(w.A, w.T, 50f);
            w.Diplo(w.T, w.A).SetFlag(DiplomacyFlags.DeclinedJoinWarOffer, 5);
            DiplomacyAutomation.AskForHelp(w.CivViewOf(w.A));
            Assert.Empty(w.T.TradeRequests);
            w.Diplo(w.T, w.A).RemoveFlag(DiplomacyFlags.DeclinedJoinWarOffer);
            w.Diplo(w.T, w.B).DeclareWar(); // already at war with the enemy
            DiplomacyAutomation.AskForHelp(w.CivViewOf(w.A));
            Assert.Empty(w.T.TradeRequests);
        }

        [Fact]
        public void AskForHelp_DoesNothingAtPeaceOrWhenWeAreFarStrongerThanTheEnemy()
        {
            var peace = new World();
            peace.SetOpinion(peace.A, peace.T, 50f);
            DiplomacyAutomation.AskForHelp(peace.CivViewOf(peace.A));
            Assert.Empty(peace.T.TradeRequests);

            var w = WarWorld(); // B is weak: no help needed (joinOurWar is 0)
            w.SetOpinion(w.A, w.T, 50f);
            DiplomacyAutomation.AskForHelp(w.CivViewOf(w.A));
            Assert.Empty(w.T.TradeRequests);
        }

        // ---- military presence near the border

        private static World BorderWorld(int nearbyUnits = 8, int farUnits = 0)
        {
            var w = new World(bIsHuman: true);
            w.AddWarriors(w.A, 8, -9, 0);
            var ring = w.CityA.GetCenterTile().GetTilesAtDistance(2).Where(it => it.GetOwner() != w.A).Take(nearbyUnits).ToList();
            Assert.Equal(nearbyUnits, ring.Count);
            foreach (var tile in ring) w.Game.AddUnit("Warrior", w.B, tile);
            if (farUnits > 0) w.AddWarriors(w.B, farUnits, 8, 0);
            w.A.ResetMilitaryMightCache();
            w.B.ResetMilitaryMightCache();
            return w;
        }

        private static bool Warned(World w) => w.Diplo(w.A, w.B).HasFlag(DiplomacyFlags.MilitaryPresenceNearBorderOrAttackedUsDespitePromise);

        [Fact]
        public void BorderPresence_ManyUnitsCloseToOurBorders_RaisesTheDemand()
        {
            var w = BorderWorld();
            DiplomacyAutomation.CheckMilitaryPresenceNearBorder(w.CivViewOf(w.A));
            Assert.True(Warned(w));
        }

        [Fact]
        public void BorderPresence_IgnoresAFewUnits()
        {
            var w = BorderWorld(nearbyUnits: 7);
            DiplomacyAutomation.CheckMilitaryPresenceNearBorder(w.CivViewOf(w.A));
            Assert.False(Warned(w));
        }

        [Fact]
        public void BorderPresence_IgnoresACivWhoseArmyIsMostlyElsewhere()
        {
            var w = BorderWorld(farUnits: 20);
            DiplomacyAutomation.CheckMilitaryPresenceNearBorder(w.CivViewOf(w.A));
            Assert.False(Warned(w));
        }

        [Theory]
        [InlineData("war")]
        [InlineData("promise")]
        [InlineData("friendship")]
        [InlineData("pact")]
        [InlineData("openBorders")]
        public void BorderPresence_IgnoresWarAllies_AndPromises(string reason)
        {
            var w = BorderWorld();
            switch (reason)
            {
                case "war": w.Diplo(w.A, w.B).DeclareWar(); break;
                case "promise": w.Diplo(w.A, w.B).SetFlag(DiplomacyFlags.AgreedToNotAttackUs, 10); break;
                case "friendship": w.Diplo(w.A, w.B).SetFlag(DiplomacyFlags.DeclarationOfFriendship, 10); break;
                case "pact": w.Diplo(w.A, w.B).SetFlag(DiplomacyFlags.DefensivePact, 10); break;
                default: w.Diplo(w.B, w.A).HasOpenBorders = true; break;
            }
            DiplomacyAutomation.CheckMilitaryPresenceNearBorder(w.CivViewOf(w.A));
            Assert.False(Warned(w));
        }

        [Fact]
        public void BorderPresence_OnlyHumansAreWarned_AndNotWhenWeAreVeryWeakOrStrongAgainstThem()
        {
            var ai = BorderWorld();
            ai.B.PlayerType = Unciv.Logic.Civilization.PlayerType.AI;
            DiplomacyAutomation.CheckMilitaryPresenceNearBorder(ai.CivViewOf(ai.A));
            Assert.False(Warned(ai));

            var strongB = BorderWorld(farUnits: 0);
            strongB.AddWarriors(strongB.B, 25, 8, 0); // far more force: VeryHigh threat, they stay silent
            DiplomacyAutomation.CheckMilitaryPresenceNearBorder(strongB.CivViewOf(strongB.A));
            Assert.False(Warned(strongB));
        }

        // ---- denouncing

        private static World OpinionWorld(float opinionOfB)
        {
            var w = new World();
            w.SetOpinion(w.A, w.B, opinionOfB);
            w.Diplo(w.A, w.B).SaveSmoothedOpinionOfOtherCiv();
            return w;
        }

        [Fact]
        public void Denounce_ACivWhoseOpinionCrashedIsDenounced()
        {
            // smoothed opinion 0 (initial), opinion -70: delta -70 <= -65 * 1.005^-70 = -45.8
            var w = new World();
            Assert.Equal(0f, w.Diplo(w.A, w.B).SmoothedOpinionOfOtherCiv);
            w.SetOpinion(w.A, w.B, -70f);
            DiplomacyAutomation.Denounce(w.CivViewOf(w.A));
            Assert.True(w.Diplo(w.A, w.B).HasFlag(DiplomacyFlags.Denunciation));
            Assert.False(w.Diplo(w.A, w.T).HasFlag(DiplomacyFlags.Denunciation)); // delta 0 > -65
        }

        [Fact]
        public void Denounce_MultipleDenouncementsRaiseTheBar()
        {
            // delta -52: enough alone (-50.6) but not when A already denounced T (x1.1 = -55.7)
            var alone = new World();
            alone.SetOpinion(alone.A, alone.B, -52f);
            DiplomacyAutomation.Denounce(alone.CivViewOf(alone.A));
            Assert.True(alone.Diplo(alone.A, alone.B).HasFlag(DiplomacyFlags.Denunciation));

            var w = new World();
            w.SetOpinion(w.A, w.B, -52f);
            w.Diplo(w.A, w.T).SetFlag(DiplomacyFlags.Denunciation, 10);
            DiplomacyAutomation.Denounce(w.CivViewOf(w.A));
            Assert.False(w.Diplo(w.A, w.B).HasFlag(DiplomacyFlags.Denunciation));
        }

        [Fact]
        public void Denounce_NeverForPacifistsFriendsOrEnemiesAtWar()
        {
            var never = new World();
            never.Game.Ruleset.Personalities.Put("Calm", new Personality { DenounceWillingness = 0f });
            never.A.Nation.Personality = "Calm";
            never.SetOpinion(never.A, never.B, -500f);
            DiplomacyAutomation.Denounce(never.CivViewOf(never.A));
            Assert.False(never.Diplo(never.A, never.B).HasFlag(DiplomacyFlags.Denunciation));

            var friends = new World();
            friends.SetOpinion(friends.A, friends.B, -500f);
            friends.Diplo(friends.A, friends.B).SetFlag(DiplomacyFlags.DeclarationOfFriendship, 10);
            DiplomacyAutomation.Denounce(friends.CivViewOf(friends.A));
            Assert.False(friends.Diplo(friends.A, friends.B).HasFlag(DiplomacyFlags.Denunciation));

            var war = new World();
            war.SetOpinion(war.A, war.B, -500f);
            war.Diplo(war.A, war.B).DeclareWar();
            DiplomacyAutomation.Denounce(war.CivViewOf(war.A));
            Assert.False(war.Diplo(war.A, war.B).HasFlag(DiplomacyFlags.Denunciation));
        }

        [Fact]
        public void Denounce_IgnoresCityStates()
        {
            var w = new World(bIsCityState: true);
            w.Diplo(w.A, w.B).SetModifier(DiplomaticModifiers.GaveUsGifts, -500f);
            DiplomacyAutomation.Denounce(w.CivViewOf(w.A));
            Assert.False(w.Diplo(w.A, w.B).HasFlag(DiplomacyFlags.Denunciation));
        }

        [Fact]
        public void Constants_AreTheKotlinValues()
        {
            Assert.Equal(-65f, DiplomacyAutomation.DenounceRequiredOpinionChangeInitial);
            Assert.Equal(1.005f, DiplomacyAutomation.DenounceRequiredOpinionChangeBase);
            Assert.Equal(1.1f, DiplomacyAutomation.ConcurrentDenouncementsRequiredOpinionChangeBase);
            Assert.NotNull((CivilizationObject?)null ?? OpinionWorld(0f).A);
        }
    }
}
