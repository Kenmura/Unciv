using Unciv.Logic.Automation.Civilization;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Logic.Trade;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Core.Tests.Logic.Automation;

public static partial class DiplomacyAiTests
{
    // DeclareWarTargetAutomation. A (82 force) picks a target among B (28 force, or stronger) with the given motivation.
    [Collection("RulesetCache")]
    public class TargetTests
    {
        private static World ArmedWorld(int bWarriors = 1, bool bIsCityState = false)
        {
            var w = new World(bIsCityState: bIsCityState);
            w.AddWarriors(w.A, 3, -5, 0);
            w.AddWarriors(w.B, bWarriors, 5, 0);
            return w;
        }

        private static void Choose(World w, params (CivilizationObject, float)[] targets) =>
            DeclareWarTargetAutomation.ChooseDeclareWarTarget(w.CivViewOf(w.A), [.. targets]);

        [Fact]
        public void NoTargets_DoNothing()
        {
            var w = ArmedWorld();
            Choose(w);
            Assert.False(w.A.IsAtWarWith(w.B));
            Assert.Empty(w.T.TradeRequests);
        }

        [Fact]
        public void LowMotivation_DoesNothing()
        {
            var w = ArmedWorld();
            Choose(w, (w.B, 10f));
            Assert.False(w.Diplo(w.A, w.B).HasFlag(DiplomacyFlags.WaryOf));
            Assert.False(w.A.IsAtWarWith(w.B));
        }

        [Fact]
        public void ModerateMotivation_StartsPreparingTheWar()
        {
            var w = ArmedWorld();
            // 25: the team war (25 - 5 - 110 - 20) fails, no one is at war with B, a surprise war needs > 40: prepare (25 - 15 > 0)
            Choose(w, (w.B, 25f));
            Assert.Equal(-1, w.Diplo(w.A, w.B).GetFlag(DiplomacyFlags.WaryOf));
            Assert.False(w.A.IsAtWarWith(w.B));
            Assert.Empty(w.T.TradeRequests);
        }

        [Fact]
        public void HighMotivation_DeclaresWar()
        {
            var w = ArmedWorld();
            Choose(w, (w.B, 60f));
            Assert.True(w.A.IsAtWarWith(w.B));
        }

        [Fact]
        public void Motivation20To40_IsTooLowForASurpriseWar_ButPreparesTheWar()
        {
            var w = ArmedWorld();
            Choose(w, (w.B, 30f)); // declareWar: 30 - 40 <= 0
            Assert.False(w.A.IsAtWarWith(w.B));
            Assert.Equal(-1, w.Diplo(w.A, w.B).GetFlag(DiplomacyFlags.WaryOf));
        }

        [Fact]
        public void ATeamWar_IsOfferedToACivThatCanHelp()
        {
            var w = ArmedWorld(bWarriors: 4);
            Choose(w, (w.B, 30f)); // 30 - 5 - 20 = 5 > 0 for T
            var request = Assert.Single(w.T.TradeRequests);
            Assert.Equal(w.A.CivId, request.RequestingCiv);
            // the request is the reverse of A's trade: both sides carry the war declaration against B
            var ours = Assert.Single(request.Trade.OurOffers);
            var theirs = Assert.Single(request.Trade.TheirOffers);
            Assert.Equal((w.B.CivId, TradeOfferType.WarDeclaration), (ours.Name, ours.Type));
            Assert.Equal((w.B.CivId, TradeOfferType.WarDeclaration), (theirs.Name, theirs.Type));
            Assert.False(w.A.IsAtWarWith(w.B));
        }

        [Fact]
        public void ATeamWar_IsNotOfferedAfterTheyDeclinedBefore()
        {
            var w = ArmedWorld(bWarriors: 4);
            w.Diplo(w.A, w.T).SetFlag(DiplomacyFlags.DeclinedJoinWarOffer, 5);
            Choose(w, (w.B, 30f));
            Assert.Empty(w.T.TradeRequests);
            Assert.Equal(-1, w.Diplo(w.A, w.B).GetFlag(DiplomacyFlags.WaryOf)); // falls through to preparing
        }

        [Fact]
        public void JoiningAnExistingWar_AsksTheFriendAtWarWithTheTarget()
        {
            var w = ArmedWorld();
            w.Diplo(w.T, w.B).DeclareWar();
            w.SetOpinion(w.A, w.T, 50f);
            Choose(w, (w.B, 30f)); // join: 30 - 15 = 15 > 0
            var request = Assert.Single(w.T.TradeRequests);
            Assert.Equal(w.A.CivId, request.RequestingCiv);
            // only our side asks: the reverse makes it their offer to declare war on B
            Assert.Empty(request.Trade.OurOffers);
            var asked = Assert.Single(request.Trade.TheirOffers);
            Assert.Equal((w.B.CivId, TradeOfferType.WarDeclaration), (asked.Name, asked.Type));
        }

        [Fact]
        public void CityStates_AreNeverTeamOrJoinTargets()
        {
            var w = ArmedWorld(bIsCityState: true);
            w.SetOpinion(w.A, w.T, 50f);
            Choose(w, (w.B, 30f));
            Assert.Empty(w.T.TradeRequests);
            Assert.Equal(-1, w.Diplo(w.A, w.B).GetFlag(DiplomacyFlags.WaryOf));
        }

        [Fact]
        public void TargetsAreTriedInOrderOfScore_AndTheFirstSuccessEndsTheSearch()
        {
            var w = ArmedWorld();
            w.Game.AddCity(w.T, w.Game.GetTile(3, 8)); // T now has the higher score
            Choose(w, (w.B, 60f), (w.T, 60f));
            Assert.True(w.A.IsAtWarWith(w.T));
            Assert.False(w.A.IsAtWarWith(w.B));
        }
    }
}
