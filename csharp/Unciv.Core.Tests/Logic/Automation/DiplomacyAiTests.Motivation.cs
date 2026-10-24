using Unciv.Logic.Automation.Civilization;
using Unciv.Logic.Battle;
using Unciv.Logic.Civilization.Diplomacy;
using CityObject = Unciv.Logic.City.City;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Core.Tests.Logic.Automation;

public static partial class DiplomacyAiTests
{
    // MotivationToAttackAutomation.hasAtLeastMotivationToAttack. Each case compares against a baseline of the same world: the
    // delta is the single modifier the Kotlin line adds.
    [Collection("RulesetCache")]
    public class MotivationTests
    {
        private static World ArmedWorld(bool bIsHuman = false, int bX = 4, bool bIsCityState = false)
        {
            var w = new World(bIsHuman, bX, bIsCityState);
            w.AddWarriors(w.A, 3, -5, 0);
            w.AddWarriors(w.B, 1, bX == 12 ? 11 : bX + 1, 0);
            return w;
        }

        private static float Baseline(World w) => w.Motivation(w.A, w.B, -1000f);

        [Fact]
        public void Baseline_IsHandDerived()
        {
            // forceA 82 + 100 + cityStrength 11 = 193 vs forceB 28 + 100 + 11 = 139: ratio 1.388 -> +2 ("> 1.2f").
            // Base motivation -15 (neutral personality), score ratio 38/38 -> 0, production 4/4 -> 0, distance 8 -> no distance modifier,
            // T is a neighbour below Friend so there is no "No other threats". Land path found: -3 + 3 = 0.
            var w = ArmedWorld();
            Assert.Equal(82, w.A.GetStatForRanking(Unciv.UI.Screens.VictoryScreen.RankingType.Force));
            Assert.Equal(28, w.B.GetStatForRanking(Unciv.UI.Screens.VictoryScreen.RankingType.Force));
            Assert.Equal(11f, new CityCombatant(w.CityA).GetCityStrength());
            Assert.Equal(-13f, Baseline(w));
        }

        [Fact]
        public void NoCityOfTheTargetNearUs_IsZero()
        {
            var w = ArmedWorld(bX: 12); // 16 tiles away: not a neighbouring city (12)
            Assert.Equal(0f, Baseline(w));
        }

        [Fact]
        public void NoMilitaryUnits_IsZero()
        {
            var w = new World();
            Assert.Equal(0f, Baseline(w));
        }

        [Fact]
        public void ShortCircuit_SkipsThePathfinding()
        {
            var w = ArmedWorld();
            // The sum before the path modifier is -13 - 0 = -13: asking for at least 0 returns it without the (here neutral) path search.
            Assert.Equal(-13f, w.Motivation(w.A, w.B, 0f));
            Assert.Equal(-13f, w.Motivation(w.A, w.B, -13f));
        }

        [Theory]
        [InlineData(7, -3f)] // distance 11: "> 10" -> -3
        [InlineData(-1, 5f)] // distance 3: "< 6" -> close cities +5
        public void CityDistance_Modifiers(int bX, float delta)
        {
            var w = ArmedWorld();
            var baseline = Baseline(w);
            var near = ArmedWorld(bX: bX);
            Assert.Equal(baseline + delta, Baseline(near));
        }

        [Theory]
        [InlineData(DiplomacyFlags.ResearchAgreement, -5f)]
        [InlineData(DiplomacyFlags.DeclarationOfFriendship, -10f)]
        [InlineData(DiplomacyFlags.DefensivePact, -15f)]
        [InlineData(DiplomacyFlags.Denunciation, 5f)]
        public void Flags_Modifiers(DiplomacyFlags flag, float delta)
        {
            var w = ArmedWorld();
            var baseline = Baseline(w);
            w.Diplo(w.A, w.B).SetFlag(flag, 10);
            Assert.Equal(baseline + delta, Baseline(w));
        }

        [Theory]
        [InlineData(-90f, 10f)] // Unforgivable
        [InlineData(-50f, 5f)] // Enemy
        [InlineData(-20f, 2f)] // Competitor
        [InlineData(0f, 0f)] // Neutral
        [InlineData(20f, -2f)] // Favorable
        [InlineData(50f, -5f)] // Friend
        [InlineData(90f, -10f)] // Ally
        public void RelationshipLevel_Modifiers(float opinion, float delta)
        {
            var w = ArmedWorld();
            var baseline = Baseline(w);
            w.SetOpinion(w.A, w.B, opinion);
            Assert.Equal(baseline + delta, Baseline(w));
        }

        [Fact]
        public void PlanningAnAttackOnTheTarget_AddsTheRemainingPlanningTurns()
        {
            var w = ArmedWorld();
            var baseline = Baseline(w);
            w.Diplo(w.A, w.B).SetFlag(DiplomacyFlags.WaryOf, -4);
            // -(-4) * aggressive 1 / 2 = +2, instead of the other-civs term (0 planned)
            Assert.Equal(baseline + 2f, Baseline(w));
        }

        [Fact]
        public void PlanningAnAttackOnAnotherCiv_Costs5PerPlan()
        {
            var w = ArmedWorld();
            var baseline = Baseline(w);
            w.Diplo(w.A, w.T).SetFlag(DiplomacyFlags.WaryOf, -1);
            Assert.Equal(baseline - 5f, Baseline(w));
        }

        [Fact]
        public void TheirDefensivePactAllies_AreDeterrence()
        {
            var w = ArmedWorld();
            var baseline = Baseline(w);
            w.AddWarriors(w.T, 3, 0, 8); // same force as A: (82 + 100) / 193 = 0.94 -> "> .8" -> -2
            w.Diplo(w.B, w.T).SetFlag(DiplomacyFlags.DefensivePact, 10);
            Assert.Equal(baseline - 2f, Baseline(w));
        }

        [Fact]
        public void ConcurrentWars_CostTwentyEach()
        {
            var w = ArmedWorld();
            var baseline = Baseline(w);
            w.Diplo(w.A, w.T).DeclareWar();
            Assert.Equal(baseline - 20f, Baseline(w));
        }

        [Fact]
        public void TheirConcurrentWars_Add3()
        {
            var w = ArmedWorld();
            var baseline = Baseline(w);
            w.Diplo(w.B, w.T).DeclareWar();
            Assert.Equal(baseline + 3f, Baseline(w));
        }

        [Fact]
        public void CityState_TargetsAreLessAttractive()
        {
            var w = ArmedWorld(bIsCityState: true);
            Assert.True(w.B.IsCityState);
            var baseline = Baseline(w);
            w.B.AllyCiv = w.A;
            Assert.Equal(baseline - 20f, Baseline(w));
        }

        [Fact]
        public void WonderAndVictoryBuildings_RaiseTheMotivation()
        {
            var w = ArmedWorld();
            var baseline = Baseline(w);
            var wonder = w.Game.CreateWonder();
            w.CityB.CityConstructions.AddBuilding(wonder.Name);
            w.CityA.CityConstructions.AddBuilding(wonder.Name); // wonders raise the score: both sides own one, the score ratio stays 1
            Assert.Equal(baseline + 1f, Baseline(w)); // "Owned Wonders" = count of built wonders
        }

        [Fact]
        public void AboutToWin_Adds15PerVictoryConstruction()
        {
            var w = ArmedWorld();
            var baseline = Baseline(w);
            var victoryBuilding = w.Game.CreateBuilding("Triggers a Cultural Victory upon completion");
            w.CityB.CityConstructions.AddToQueue(victoryBuilding);
            Assert.Equal(baseline + 15f, Baseline(w));
        }

        // ---- attack paths

        private static void MakeBarrier(World w, string terrain)
        {
            foreach (var tile in w.Game.TileMap.Values.Where(it => it.Position.X == 0 && it.GetCity() == null))
                w.Game.SetTileTerrain(tile.Position, terrain);
        }

        [Fact]
        public void AttackPath_OverWater_ValuesTheAmphibiousRouteAtOne()
        {
            var w = ArmedWorld();
            var baseline = Baseline(w); // land route: -3 + 3
            MakeBarrier(w, Constants.Ocean);
            // no land route, a land-and-sea one: -3 + 1
            Assert.Equal(baseline - 2f, Baseline(w));
        }

        [Fact]
        public void AttackPath_Walled_IsMinus50WhenTheEnemyCannotBeReached()
        {
            var w = ArmedWorld();
            MakeBarrier(w, Constants.Mountain);
            Assert.Equal(-13f - 50f + 0f, Baseline(w) + 0f, 3);
        }

        private sealed class StandInPathing(List<TileClass>? land, List<TileClass>? amphibious) : ICityAttackPathing
        {
            public int LandCalls { get; private set; }
            public int AmphibiousCalls { get; private set; }

            public List<TileClass>? GetLandAttackPath(CityObject from, CityObject destination, int maxTurns)
            {
                LandCalls++;
                Assert.Equal(17, maxTurns);
                return land;
            }

            public List<TileClass>? GetAmphibiousAttackPath(CityObject from, CityObject destination, int maxTurns)
            {
                AmphibiousCalls++;
                Assert.Equal(17, maxTurns);
                return amphibious;
            }
        }

        [Fact]
        public void AStarSeam_IsUsedWhenInstalled()
        {
            var w = ArmedWorld();
            var baseline = Baseline(w);
            var short16 = Enumerable.Range(0, 15).Select(_ => w.CityA.GetCenterTile()).ToList();
            var long16 = Enumerable.Range(0, 16).Select(_ => w.CityA.GetCenterTile()).ToList();

            var landOnly = new StandInPathing(short16, null);
            w.Game.GameInfo.ClientState.AttackPathing = landOnly;
            Assert.Equal(baseline, Baseline(w));
            Assert.Equal(1, landOnly.LandCalls);
            Assert.Equal(0, landOnly.AmphibiousCalls);

            // a land path of 16 tiles is too long; the amphibious one of 15 counts for 1
            var seaOnly = new StandInPathing(long16, short16);
            w.Game.GameInfo.ClientState.AttackPathing = seaOnly;
            Assert.Equal(baseline - 2f, Baseline(w));
            Assert.Equal(1, seaOnly.AmphibiousCalls);

            // no path at all: the BFS fallback (open desert, 8 tiles away): minAttackDistance - 10 is below 0 -> coerced to 0
            w.Game.GameInfo.ClientState.AttackPathing = new StandInPathing(null, long16);
            Assert.Equal(baseline - 3f, Baseline(w));
        }
    }
}
