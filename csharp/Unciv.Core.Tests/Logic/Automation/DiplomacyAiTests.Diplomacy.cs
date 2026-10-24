using Unciv.Logic.Automation.Civilization;
using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Logic.Trade;
using Unciv.Models.Ruleset.Nation;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Core.Tests.Logic.Automation;

public static partial class DiplomacyAiTests
{
    // DiplomacyAutomation. The "wants to ..." decisions are exact booleans; the offers behind a random roll are searched over turn numbers.
    [Collection("RulesetCache")]
    public class DiplomacyTests
    {
        private const string Embassies = "Enables establishment of embassies";
        private const string OpenBorders = "Enables Open Borders agreements";
        private const string ResearchAgreements = "Enables Research agreements";
        private const string DefensivePacts = "Enables Defensive Pacts";

        private static World ArmedWorld(bool bIsHuman = false, string[]? uniques = null, int bWarriors = 1)
        {
            var w = new World(bIsHuman, uniques: uniques);
            w.AddWarriors(w.A, 3, -5, 0);
            w.AddWarriors(w.B, bWarriors, 5, 0);
            return w;
        }

        private static void SetDiplomacyFocus(World w, CivilizationObject civ, float value)
        {
            w.Game.Ruleset.Personalities.Put("Diplomat", new Personality { Diplomacy = value });
            civ.Nation.Personality = "Diplomat";
        }

        // ---- declaration of friendship

        [Fact]
        public void WantsToSignDeclarationOfFriendship_RefusesAfterADeclineOrBelowFavorable()
        {
            var w = ArmedWorld();
            w.SetOpinion(w.A, w.B, 90f);
            Assert.True(DiplomacyAutomation.WantsToSignDeclarationOfFrienship(w.CivViewOf(w.A), w.B));
            w.SetOpinion(w.A, w.B, 10f); // Neutral
            Assert.False(DiplomacyAutomation.WantsToSignDeclarationOfFrienship(w.CivViewOf(w.A), w.B));
            w.SetOpinion(w.A, w.B, 90f);
            w.Diplo(w.A, w.B).SetFlag(DiplomacyFlags.DeclinedDeclarationOfFriendship, 5);
            Assert.False(DiplomacyAutomation.WantsToSignDeclarationOfFrienship(w.CivViewOf(w.A), w.B));
        }

        [Fact]
        public void WantsToSignDeclarationOfFriendship_ComesDownToTheMotivationSum()
        {
            // opinion 15 (Favorable): 15 - 40 = -25; B is VeryLow threat (28 / 82 < .5): -5; no friends yet (0 < 0.5): +10;
            // civsToKnow 1.5 < known 2: no wait malus; T is an unfriendly neighbour: no -20; minus 2 * motivationToAttack (-13 -> +26): 6 > 0
            var w = ArmedWorld();
            w.SetOpinion(w.A, w.B, 15f);
            Assert.True(DiplomacyAutomation.WantsToSignDeclarationOfFrienship(w.CivViewOf(w.A), w.B));
            // B the only civ that is not Favorable and near: -20 -> -14 < 0
            w.SetOpinion(w.A, w.T, 20f);
            Assert.False(DiplomacyAutomation.WantsToSignDeclarationOfFrienship(w.CivViewOf(w.A), w.B));
        }

        [Fact]
        public void WantsToSignDeclarationOfFriendship_WarmongerersAndManyFriendsAreUnattractive()
        {
            var w = ArmedWorld();
            w.SetOpinion(w.A, w.B, 90f);
            w.Diplo(w.A, w.B).SetModifier(DiplomaticModifiers.WarMongerer, -200f);
            Assert.False(DiplomacyAutomation.WantsToSignDeclarationOfFrienship(w.CivViewOf(w.A), w.B));
            w.Diplo(w.A, w.B).SetModifier(DiplomaticModifiers.WarMongerer, 0f);
            Assert.True(DiplomacyAutomation.WantsToSignDeclarationOfFrienship(w.CivViewOf(w.A), w.B));
            // A already has friends beyond a fourth of the civs (0.5): -120 * (2 - 0.5) / (2 known - 0.5) = -120
            w.Diplo(w.A, w.T).SetFlag(DiplomacyFlags.DeclarationOfFriendship, 10);
            w.Diplo(w.A, w.B).SetFlag(DiplomacyFlags.DeclarationOfFriendship, 10);
            Assert.False(DiplomacyAutomation.WantsToSignDeclarationOfFrienship(w.CivViewOf(w.A), w.B));
        }

        [Fact]
        public void WantsToSignDeclarationOfFriendship_StrongNeighboursAndWarsMakeFriendsMoreWelcome()
        {
            var w = ArmedWorld(bWarriors: 12); // B more than twice A's force: VeryHigh threat (+10)
            w.SetOpinion(w.A, w.B, 15f);
            w.Diplo(w.A, w.T).DeclareWar(); // desperate: +10 for the war with T
            Assert.True(DiplomacyAutomation.WantsToSignDeclarationOfFrienship(w.CivViewOf(w.A), w.B));
        }

        [Fact]
        public void OfferDeclarationOfFriendship_AlertsTheOtherCiv()
        {
            var w = ArmedWorld();
            SetDiplomacyFocus(w, w.A, 50f); // the roll (1..10) <= 2 * 10 always passes
            w.SetOpinion(w.A, w.B, 90f);
            w.SetOpinion(w.A, w.T, -50f); // not offered to T
            DiplomacyAutomation.OfferDeclarationOfFriendship(w.CivViewOf(w.A));
            var alert = Assert.Single(w.B.PopupAlerts, it => it.Type == AlertType.DeclarationOfFriendship);
            Assert.Equal(w.A.CivId, alert.Value);
        }

        [Fact]
        public void OfferDeclarationOfFriendship_SkipsDeclinedAndAlreadyFriendlyCivs()
        {
            var w = ArmedWorld();
            SetDiplomacyFocus(w, w.A, 50f);
            w.SetOpinion(w.A, w.B, 90f);
            w.SetOpinion(w.A, w.T, 90f);
            w.Diplo(w.A, w.B).SetFlag(DiplomacyFlags.DeclinedDeclarationOfFriendship, 5);
            w.Diplo(w.A, w.T).SetFlag(DiplomacyFlags.DeclarationOfFriendship, 5);
            DiplomacyAutomation.OfferDeclarationOfFriendship(w.CivViewOf(w.A));
            Assert.DoesNotContain(w.B.PopupAlerts, it => it.Type == AlertType.DeclarationOfFriendship);
            Assert.DoesNotContain(w.T.PopupAlerts, it => it.Type == AlertType.DeclarationOfFriendship);
        }

        // ---- embassies

        [Fact]
        public void WantsToAcceptEmbassy_Rules()
        {
            var w = ArmedWorld();
            w.SetOpinion(w.B, w.A, 0f);
            // their relationship is Neutral which is >= Afraid, and our capital is not explored by them: yes
            Assert.True(DiplomacyAutomation.WantsToAcceptEmbassy(w.CivViewOf(w.A), w.B));
            w.SetOpinion(w.B, w.A, -50f); // Enemy < Afraid
            Assert.False(DiplomacyAutomation.WantsToAcceptEmbassy(w.CivViewOf(w.A), w.B));
            w.SetOpinion(w.B, w.A, 0f);
            w.Diplo(w.A, w.B).SetFlag(DiplomacyFlags.DeclinedEmbassy, 5);
            Assert.False(DiplomacyAutomation.WantsToAcceptEmbassy(w.CivViewOf(w.A), w.B));
        }

        [Fact]
        public void WantsToAcceptEmbassy_WhenBothCapitalsAreExplored_TheyDenyAStrongerCiv()
        {
            var w = ArmedWorld();
            w.SetOpinion(w.B, w.A, 0f);
            w.Game.GameInfo.EngineOptions.VisibleMap = false;
            foreach (var tile in w.Game.TileMap.Values) { tile.SetExplored(w.A, false); tile.SetExplored(w.B, false); }
            // their capital is not known to us, ours is known to them: the force check. A 82 vs B 28: (82 - 28) / 28 = 1.93 > 0.5 -> deny
            w.CityA.GetCenterTile().SetExplored(w.B, true);
            Assert.False(w.B.GetCapital()!.GetCenterTile().IsExplored(w.A));
            Assert.False(DiplomacyAutomation.WantsToAcceptEmbassy(w.CivViewOf(w.A), w.B));
            // a weaker A (below +50%) is accepted: (28 - 28) / 28 = 0
            var equal = ArmedWorld(bWarriors: 3);
            equal.SetOpinion(equal.B, equal.A, 0f);
            equal.Game.GameInfo.EngineOptions.VisibleMap = false;
            foreach (var tile in equal.Game.TileMap.Values) { tile.SetExplored(equal.A, false); tile.SetExplored(equal.B, false); }
            equal.CityA.GetCenterTile().SetExplored(equal.B, true);
            Assert.True(DiplomacyAutomation.WantsToAcceptEmbassy(equal.CivViewOf(equal.A), equal.B));
        }

        [Fact]
        public void OfferToEstablishEmbassy_SendsAMutualEmbassyOffer()
        {
            var w = ArmedWorld(uniques: [Embassies]);
            w.SetOpinion(w.B, w.A, 0f);
            w.SetOpinion(w.A, w.T, -90f); // Unforgivable: ordered last, T's embassy also offered
            var turn = w.RunUntil(() => DiplomacyAutomation.OfferToEstablishEmbassy(w.CivViewOf(w.A)), () => w.B.TradeRequests.Count != 0);
            Assert.True(turn > 0);
            var request = w.B.TradeRequests[0];
            Assert.Equal(w.A.CivId, request.RequestingCiv);
            Assert.Contains(request.Trade.OurOffers, it => it.Type == TradeOfferType.Embassy);
            Assert.Contains(request.Trade.TheirOffers, it => it.Type == TradeOfferType.Embassy);
        }

        [Fact]
        public void OfferToEstablishEmbassy_RemembersARefusal()
        {
            var w = ArmedWorld(uniques: [Embassies]);
            w.SetOpinion(w.B, w.A, -50f); // B would not accept ours (Enemy < Afraid)
            w.RunUntil(() => DiplomacyAutomation.OfferToEstablishEmbassy(w.CivViewOf(w.A)), () => w.Diplo(w.A, w.B).HasFlag(DiplomacyFlags.DeclinedEmbassy));
            Assert.Empty(w.B.TradeRequests);
        }

        [Fact]
        public void OfferToEstablishEmbassy_OneWayOffersPayGoldPerTurnOrFlatGold()
        {
            // only A can trade embassies: the other side cannot be offered one, so A pays the embassy value
            var w = new World(aUniques: [Embassies]);
            w.AddWarriors(w.A, 3, -5, 0);
            w.SetOpinion(w.B, w.A, 0f);
            w.Evaluation.BuyCost = _ => 90;
            w.Game.GameInfo.Speed.DealDuration = 30;
            w.A.AddGold(1000);
            w.RunUntil(() => DiplomacyAutomation.OfferToEstablishEmbassy(w.CivViewOf(w.A)), () => w.B.TradeRequests.Count != 0);
            var request = w.B.TradeRequests[0];
            // embassyGptValue = 90 / 30 = 3; our gpt is below that in this empty economy -> flat gold 90 (gold >= value and gpt >= 0)
            var gold = Assert.Single(request.Trade.TheirOffers, it => it.Type is TradeOfferType.Gold or TradeOfferType.Gold_Per_Turn);
            Assert.True(gold.Type == TradeOfferType.Gold ? gold.Amount == 90 : gold.Amount == 3);
        }

        // ---- open borders

        [Fact]
        public void WantsToOpenBorders_Rules()
        {
            var w = ArmedWorld();
            Assert.False(DiplomacyAutomation.WantsToOpenBorders(w.CivViewOf(w.A), w.B)); // Neutral < Favorable
            w.SetOpinion(w.A, w.B, 20f);
            // their only city is visible to us: no early yes; motivation -13 is not > 0
            Assert.True(DiplomacyAutomation.WantsToOpenBorders(w.CivViewOf(w.A), w.B));
            w.Diplo(w.A, w.B).SetFlag(DiplomacyFlags.DeclinedOpenBorders, 5);
            Assert.False(DiplomacyAutomation.WantsToOpenBorders(w.CivViewOf(w.A), w.B));
        }

        [Fact]
        public void WantsToOpenBorders_NotWhenTheyFightOurFriends()
        {
            var w = ArmedWorld();
            w.SetOpinion(w.A, w.B, 20f);
            w.SetOpinion(w.A, w.T, 50f); // T is our Friend
            w.Diplo(w.B, w.T).DeclareWar();
            Assert.False(DiplomacyAutomation.WantsToOpenBorders(w.CivViewOf(w.A), w.B));
        }

        [Fact]
        public void WantsToOpenBorders_NotWhenWeWantToAttackThem()
        {
            var w = ArmedWorld();
            w.SetOpinion(w.A, w.B, 20f);
            w.AddWarriors(w.A, 12, -8, -4); // overwhelming force: the motivation to attack is above the threshold
            w.Diplo(w.A, w.B).SetFlag(DiplomacyFlags.Denunciation, 5);
            Assert.True(w.Motivation(w.A, w.B) > 20f * 0.5f);
            Assert.False(DiplomacyAutomation.WantsToOpenBorders(w.CivViewOf(w.A), w.B));
        }

        [Fact]
        public void OfferOpenBorders_NeedsTheUniqueAndOffersAMutualAgreement()
        {
            var w = ArmedWorld(uniques: [OpenBorders]);
            w.ShareEmbassies(w.A, w.B);
            w.SetOpinion(w.A, w.B, 20f);
            w.SetOpinion(w.A, w.T, -50f);
            w.RunUntil(() => DiplomacyAutomation.OfferOpenBorders(w.CivViewOf(w.A)), () => w.B.TradeRequests.Count != 0);
            var request = w.B.TradeRequests[0];
            Assert.Equal(w.A.CivId, request.RequestingCiv);
            Assert.Contains(request.Trade.OurOffers, it => it.Name == Constants.OpenBorders && it.Type == TradeOfferType.Agreement);
            Assert.Contains(request.Trade.TheirOffers, it => it.Name == Constants.OpenBorders && it.Type == TradeOfferType.Agreement);
        }

        [Fact]
        public void OfferOpenBorders_RemembersARefusal_AndIsSilentWithoutTheUnique()
        {
            var w = ArmedWorld(uniques: [OpenBorders]);
            w.ShareEmbassies(w.A, w.B);
            w.SetOpinion(w.A, w.B, 0f); // Neutral: not wanted
            w.RunUntil(() => DiplomacyAutomation.OfferOpenBorders(w.CivViewOf(w.A)), () => w.Diplo(w.A, w.B).HasFlag(DiplomacyFlags.DeclinedOpenBorders));
            Assert.Empty(w.B.TradeRequests);

            var plain = ArmedWorld();
            plain.SetOpinion(plain.A, plain.B, 20f);
            plain.Game.GameInfo.Turns = 5;
            DiplomacyAutomation.OfferOpenBorders(plain.CivViewOf(plain.A));
            Assert.Empty(plain.B.TradeRequests);
        }

        // ---- research agreements and defensive pacts

        [Fact]
        public void OfferResearchAgreement_IsNotOfferedWithoutTheUnique()
        {
            var w = ArmedWorld();
            DiplomacyAutomation.OfferResearchAgreement(w.CivViewOf(w.A));
            Assert.Empty(w.B.TradeRequests);
        }

        [Fact]
        public void OfferResearchAgreement_OffersTheSameTreatyBothWays()
        {
            var w = ArmedWorld(uniques: [ResearchAgreements]);
            w.ShareEmbassies(w.A, w.B);
            w.Diplo(w.A, w.B).SetFlag(DiplomacyFlags.DeclarationOfFriendship, 10);
            w.Diplo(w.B, w.A).SetFlag(DiplomacyFlags.DeclarationOfFriendship, 10);
            w.A.AddGold(5000);
            w.B.AddGold(5000);
            DiplomacyAutomation.OfferResearchAgreement(w.CivViewOf(w.A));
            var request = Assert.Single(w.B.TradeRequests);
            Assert.Equal(w.A.CivId, request.RequestingCiv);
            Assert.Contains(request.Trade.OurOffers, it => it.Name == Constants.ResearchAgreement && it.Type == TradeOfferType.Treaty);
            Assert.Contains(request.Trade.TheirOffers, it => it.Name == Constants.ResearchAgreement && it.Type == TradeOfferType.Treaty);
            // a request already pending is not repeated
            DiplomacyAutomation.OfferResearchAgreement(w.CivViewOf(w.A));
            Assert.Single(w.B.TradeRequests);
        }

        [Fact]
        public void WantsToSignDefensivePact_Rules()
        {
            var w = ArmedWorld();
            w.SetOpinion(w.A, w.B, 90f);
            Assert.True(DiplomacyAutomation.WantsToSignDefensivePact(w.CivViewOf(w.A), w.B));
            w.SetOpinion(w.A, w.B, 60f); // below 65 * inverse aggressive focus
            Assert.False(DiplomacyAutomation.WantsToSignDefensivePact(w.CivViewOf(w.A), w.B));
            w.SetOpinion(w.A, w.B, 90f);
            w.Diplo(w.A, w.B).SetFlag(DiplomacyFlags.DeclinedDefensivePact, 5);
            Assert.False(DiplomacyAutomation.WantsToSignDefensivePact(w.CivViewOf(w.A), w.B));
        }

        [Fact]
        public void WantsToSignDefensivePact_DoesNotAllyWithEnemiesOfOurFriends()
        {
            var w = ArmedWorld();
            w.SetOpinion(w.A, w.B, 90f);
            w.Diplo(w.A, w.T).SetFlag(DiplomacyFlags.DeclarationOfFriendship, 10);
            // T (our friend) thinks little of B
            w.SetOpinion(w.T, w.B, -20f);
            Assert.False(DiplomacyAutomation.WantsToSignDefensivePact(w.CivViewOf(w.A), w.B));
            w.SetOpinion(w.T, w.B, 20f);
            Assert.True(DiplomacyAutomation.WantsToSignDefensivePact(w.CivViewOf(w.A), w.B));
            // B's friend T thinks little of us
            w.Diplo(w.A, w.T).RemoveFlag(DiplomacyFlags.DeclarationOfFriendship);
            w.Diplo(w.B, w.T).SetFlag(DiplomacyFlags.DeclarationOfFriendship, 10);
            w.SetOpinion(w.T, w.A, -20f);
            Assert.False(DiplomacyAutomation.WantsToSignDefensivePact(w.CivViewOf(w.A), w.B));
        }

        [Fact]
        public void WantsToSignDefensivePact_ThreatLevelsWarsAndOverlappingPacts()
        {
            // opinion 80 -> motivation 0 before the threat term
            var w = ArmedWorld();
            w.SetOpinion(w.A, w.B, 80f);
            Assert.False(DiplomacyAutomation.WantsToSignDefensivePact(w.CivViewOf(w.A), w.B)); // B VeryLow: -7
            var strong = ArmedWorld(bWarriors: 12);
            strong.SetOpinion(strong.A, strong.B, 80f);
            Assert.True(DiplomacyAutomation.WantsToSignDefensivePact(strong.CivViewOf(strong.A), strong.B)); // VeryHigh: +10 - bracket 0
            strong.Diplo(strong.B, strong.T).SetFlag(DiplomacyFlags.DefensivePact, 10); // their own pact would drag us in: -15
            Assert.False(DiplomacyAutomation.WantsToSignDefensivePact(strong.CivViewOf(strong.A), strong.B));
            strong.Diplo(strong.T, strong.A).SetFlag(DiplomacyFlags.DefensivePact, 10); // ... T also has a pact with us: no longer "non overlapping"
            Assert.True(DiplomacyAutomation.WantsToSignDefensivePact(strong.CivViewOf(strong.A), strong.B));
        }

        [Fact]
        public void WantsToSignDefensivePact_ModerateThreatsAndWars()
        {
            var w = ArmedWorld(bWarriors: 3); // equal force: Medium (0)
            w.SetOpinion(w.A, w.B, 79f);
            Assert.False(DiplomacyAutomation.WantsToSignDefensivePact(w.CivViewOf(w.A), w.B));
            w.Diplo(w.A, w.T).DeclareWar(); // +5 for the war with a major civ
            Assert.True(DiplomacyAutomation.WantsToSignDefensivePact(w.CivViewOf(w.A), w.B));
            w.Diplo(w.A, w.B).SetModifier(DiplomaticModifiers.WarMongerer, -10f);
            Assert.False(DiplomacyAutomation.WantsToSignDefensivePact(w.CivViewOf(w.A), w.B));
        }

        [Fact]
        public void OfferDefensivePact_OffersTheTreatyToAFriend()
        {
            var w = ArmedWorld(uniques: [DefensivePacts]);
            w.ShareEmbassies(w.A, w.B);
            w.Game.Ruleset.Personalities.Put("Loyal", new Personality { Loyal = 0f });
            w.A.Nation.Personality = "Loyal"; // (1..10) <= 7 * inverse focus 2 = 14: the roll never skips... see below
            w.SetOpinion(w.A, w.B, 90f);
            w.Diplo(w.A, w.B).SetFlag(DiplomacyFlags.DeclarationOfFriendship, 10);
            w.Diplo(w.B, w.A).SetFlag(DiplomacyFlags.DeclarationOfFriendship, 10);
            // inverse focus 2: every roll is <= 14 -> continue (skip). A neutral personality skips on rolls <= 7: search the turns
            w.A.Nation.Personality = null;
            w.RunUntil(() => DiplomacyAutomation.OfferDefensivePact(w.CivViewOf(w.A)), () => w.B.TradeRequests.Count != 0);
            var request = w.B.TradeRequests[0];
            Assert.Contains(request.Trade.OurOffers, it => it.Name == Constants.DefensivePact && it.Type == TradeOfferType.Treaty);
            Assert.Contains(request.Trade.TheirOffers, it => it.Name == Constants.DefensivePact && it.Type == TradeOfferType.Treaty);
        }

        [Fact]
        public void OfferDefensivePact_RemembersARefusal_AndNeedsTheUnique()
        {
            var w = ArmedWorld(uniques: [DefensivePacts]);
            w.ShareEmbassies(w.A, w.B);
            w.SetOpinion(w.A, w.B, 90f);
            w.Diplo(w.A, w.B).SetFlag(DiplomacyFlags.DeclarationOfFriendship, 10);
            w.Diplo(w.B, w.A).SetFlag(DiplomacyFlags.DeclarationOfFriendship, 10);
            w.Diplo(w.A, w.T).SetFlag(DiplomacyFlags.DeclarationOfFriendship, 10);
            w.SetOpinion(w.T, w.B, -20f); // the pact would anger our friend T: refused
            w.RunUntil(() => DiplomacyAutomation.OfferDefensivePact(w.CivViewOf(w.A)), () => w.Diplo(w.A, w.B).HasFlag(DiplomacyFlags.DeclinedDefensivePact));
            Assert.Empty(w.B.TradeRequests);

            var plain = ArmedWorld();
            DiplomacyAutomation.OfferDefensivePact(plain.CivViewOf(plain.A));
            Assert.Empty(plain.B.TradeRequests);
        }

        // ---- declare war

        [Fact]
        public void DeclareWar_StopsEarlyWhenTheCivIsNotReady()
        {
            var w = ArmedWorld();
            w.SetOpinion(w.A, w.B, -90f);
            // 3 military units: fewer than 4 -> nothing happens
            DiplomacyAutomation.DeclareWar(w.CivViewOf(w.A));
            Assert.False(w.A.IsAtWarWith(w.B));
            Assert.False(w.Diplo(w.A, w.B).HasFlag(DiplomacyFlags.WaryOf));
        }

        [Fact]
        public void DeclareWar_PacifistsNeverDeclare()
        {
            var w = ArmedWorld();
            w.Game.Ruleset.Personalities.Put("NoWar", new Personality { DeclareWar = 0f });
            w.A.Nation.Personality = "NoWar";
            w.AddWarriors(w.A, 12, -8, -4);
            DiplomacyAutomation.DeclareWar(w.CivViewOf(w.A));
            Assert.False(w.A.IsAtWarWith(w.B));
        }

        [Fact]
        public void DeclareWar_RequiresPopulation()
        {
            var w = ArmedWorld();
            w.AddWarriors(w.A, 12, -8, -4);
            w.SetOpinion(w.A, w.B, -90f);
            DiplomacyAutomation.DeclareWar(w.CivViewOf(w.A)); // one city of population 1 < 12
            Assert.False(w.Diplo(w.A, w.B).HasFlag(DiplomacyFlags.WaryOf));
        }

        [Fact]
        public void DeclareWar_AMotivatedStrongCivStartsAWar()
        {
            var w = ArmedWorld();
            w.CityA.Population.SetPopulation(14);
            w.AddWarriors(w.A, 12, -8, -4);
            w.SetOpinion(w.A, w.B, -90f); // Unforgivable: +10
            w.Diplo(w.A, w.B).SetFlag(DiplomacyFlags.Denunciation, 10); // +5
            foreach (var tile in w.Game.TileMap.Values) tile.SetExplored(w.A, true);
            w.A.Stats.Happiness = 10; // a population of 14 is unhappy without luxuries
            Assert.True(w.A.GetHappiness() > 0);
            Assert.True(w.Motivation(w.A, w.B) >= 15f, "motivation " + w.Motivation(w.A, w.B));
            DiplomacyAutomation.DeclareWar(w.CivViewOf(w.A));
            // 15 <= motivation < 20: neither a team war nor a surprise war, the preparation starts
            Assert.False(w.A.IsAtWarWith(w.B));
            Assert.Equal(-1, w.Diplo(w.A, w.B).GetFlag(DiplomacyFlags.WaryOf));
        }

        [Fact]
        public void DeclareWar_IgnoresCivsWhoseCitiesWeHaveNotSeen()
        {
            var w = ArmedWorld();
            w.CityA.Population.SetPopulation(14);
            w.AddWarriors(w.A, 12, -8, -4);
            w.SetOpinion(w.A, w.B, -90f);
            w.Game.GameInfo.EngineOptions.VisibleMap = false;
            foreach (var tile in w.Game.TileMap.Values) tile.SetExplored(w.A, false);
            w.A.Stats.Happiness = 10;
            DiplomacyAutomation.DeclareWar(w.CivViewOf(w.A));
            Assert.False(w.A.IsAtWarWith(w.B));
            Assert.False(w.Diplo(w.A, w.B).HasFlag(DiplomacyFlags.WaryOf));
        }
    }
}
