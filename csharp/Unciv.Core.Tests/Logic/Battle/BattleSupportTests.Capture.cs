using Unciv.Core.Tests.Support;
using Unciv.Logic.Battle;
using Unciv.Logic.Map;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;
using MapUnitClass = Unciv.Logic.Map.MapUnits.MapUnit;

namespace Unciv.Core.Tests.Logic.Battle;

// C#-only tests (T082): the Kotlin tree has no tests for BattleUnitCapture, AirInterception or GreatGeneralImplementation.getBestAffectedTroopsTile.
// Paths that reach the Battle seams (T083: takeDamage, addXp, triggerDefeatUniques, a real civ defeat) assert the seam exception.
public static partial class BattleSupportTests
{
    [Collection("RulesetCache")]
    public class BattleUnitCaptureTests
    {
        private readonly TestGame testGame = new();
        private readonly CivilizationObject attackerCiv;
        private readonly CivilizationObject defenderCiv;

        public BattleUnitCaptureTests()
        {
            testGame.MakeHexagonalMap(4);
            attackerCiv = testGame.AddCiv(isPlayer: true);
            defenderCiv = testGame.AddCiv(isPlayer: true);
            attackerCiv.DiplomacyFunctions.MakeCivilizationsMeet(defenderCiv);
            attackerCiv.GetDiplomacyManager(defenderCiv)!.DeclareWar();
        }

        [Fact]
        public void CapturingOwnUnitThrows()
        {
            var warrior = testGame.AddUnit("Warrior", attackerCiv, testGame.GetTile(0, 0));
            var worker = testGame.AddUnit("Worker", attackerCiv, testGame.GetTile(1, 0));
            var ex = Assert.Throws<ArgumentException>(() => BattleUnitCapture.CaptureCivilianUnit(new MapUnitCombatant(warrior), new MapUnitCombatant(worker)));
            Assert.Equal("Can't capture our own unit!", ex.Message);
        }

        [Fact]
        public void CapturedWorkerChangesOwnerStopsItsActionAndNotifiesTheDefender()
        {
            var warrior = testGame.AddUnit("Warrior", attackerCiv, testGame.GetTile(0, 0));
            var worker = testGame.AddUnit("Worker", defenderCiv, testGame.GetTile(1, 0));
            worker.Action = "Fortify";
            worker.Automated = true;

            BattleUnitCapture.CaptureCivilianUnit(new MapUnitCombatant(warrior), new MapUnitCombatant(worker), checkDefeat: false);

            Assert.Equal(attackerCiv, worker.Civ);
            Assert.Null(worker.Action);
            Assert.False(worker.Automated);
            Assert.Contains(defenderCiv.Notifications, it => it.Text == "An enemy [Warrior] has captured our [Worker]");
        }

        [Fact]
        public void CaptureWithDefeatCheckIsANoOpWhileTheDefenderSurvives()
        {
            var warrior = testGame.AddUnit("Warrior", attackerCiv, testGame.GetTile(0, 0));
            var worker = testGame.AddUnit("Worker", defenderCiv, testGame.GetTile(1, 0));
            testGame.AddCity(defenderCiv, testGame.GetTile(-3, 0));

            BattleUnitCapture.CaptureCivilianUnit(new MapUnitCombatant(warrior), new MapUnitCombatant(worker));

            Assert.Equal(attackerCiv, worker.Civ);
        }

        [Fact]
        public void CapturedSettlerIsReplacedByAWorkerForANonBarbarianCapturer()
        {
            var warrior = testGame.AddUnit("Warrior", attackerCiv, testGame.GetTile(0, 0));
            var settler = testGame.AddUnit("Settler", defenderCiv, testGame.GetTile(1, 0));

            BattleUnitCapture.CaptureCivilianUnit(new MapUnitCombatant(warrior), new MapUnitCombatant(settler), checkDefeat: false);

            Assert.True(settler.IsDestroyed);
            Assert.Null(defenderCiv.Units.GetUnitById(settler.Id));
            var replacement = attackerCiv.Units.GetCivUnits().Single(it => it.Name == "Worker");
            Assert.Equal(settler.Id, replacement.Id);
            Assert.Equal(new HexCoord(1, 0), replacement.CurrentTile.Position);
        }

        [Fact]
        public void CityStatesDestroyCapturedSettlersButTheDestructionReachesTheDefeatUniquesSeam()
        {
            var cityState = testGame.AddCiv(cityStateType: "Cultured");
            cityState.DiplomacyFunctions.MakeCivilizationsMeet(defenderCiv);
            cityState.GetDiplomacyManager(defenderCiv)!.DeclareWar();
            var guard = testGame.AddUnit("Warrior", cityState, testGame.GetTile(0, 0));
            var settler = testGame.AddUnit("Settler", defenderCiv, testGame.GetTile(1, 0));

            Assert.Throws<NotImplementedException>(() =>
                BattleUnitCapture.CaptureCivilianUnit(new MapUnitCombatant(guard), new MapUnitCombatant(settler), checkDefeat: false));

            Assert.True(settler.IsDestroyed);
            Assert.Contains(defenderCiv.Notifications, it => it.Text == "An enemy [Warrior] has destroyed our [Settler]");
        }

        [Fact]
        public void RecapturingOurOldUnitKeepsASettlerASettler()
        {
            var warrior = testGame.AddUnit("Warrior", attackerCiv, testGame.GetTile(0, 0));
            var settler = testGame.AddUnit("Settler", defenderCiv, testGame.GetTile(1, 0));
            settler.OriginalOwner = attackerCiv.CivId;
            Assert.Equal(attackerCiv, settler.OriginalOwningCiv);

            BattleUnitCapture.CaptureCivilianUnit(new MapUnitCombatant(warrior), new MapUnitCombatant(settler), checkDefeat: false);

            Assert.Equal(attackerCiv, settler.Civ);
            Assert.Equal("Settler", settler.Name);
        }

        [Fact]
        public void BarbariansKeepCapturedSettlersAsSettlers()
        {
            var barbarians = testGame.AddBarbarianCiv();
            var settler = testGame.AddUnit("Settler", defenderCiv, testGame.GetTile(1, 0));

            var position = BattleUnitCapture.CaptureOrConvertToWorker(settler, barbarians);

            Assert.Equal(new HexCoord(1, 0), position);
            Assert.Equal(barbarians, settler.Civ);
            Assert.Equal("Settler", settler.Name);
        }

        [Fact]
        public void TryCaptureMilitaryUnitRejectsNonUnitsAndSurvivingDefenders()
        {
            var warrior = testGame.AddUnit("Warrior", attackerCiv, testGame.GetTile(0, 0));
            var enemy = testGame.AddUnit("Warrior", defenderCiv, testGame.GetTile(1, 0));
            var city = testGame.AddCity(defenderCiv, testGame.GetTile(3, 0));
            var attackedTile = enemy.GetTile();

            Assert.False(BattleUnitCapture.TryCaptureMilitaryUnit(new MapUnitCombatant(warrior), new CityCombatant(city), attackedTile));
            Assert.False(BattleUnitCapture.TryCaptureMilitaryUnit(new CityCombatant(city), new MapUnitCombatant(enemy), attackedTile));
            Assert.False(BattleUnitCapture.TryCaptureMilitaryUnit(new MapUnitCombatant(warrior), new MapUnitCombatant(enemy), attackedTile)); // not defeated
            enemy.Health = 0;
            Assert.False(BattleUnitCapture.TryCaptureMilitaryUnit(new MapUnitCombatant(warrior), new MapUnitCombatant(enemy), attackedTile)); // nothing to capture it with
        }

        [Fact]
        public void GainFromDefeatingUnitPaysGoldAndRecruitsTheDefeatedUnit()
        {
            var capturer = testGame.AddCiv(isPlayer: true, uniques: ["When defeating a [Military] unit, earn [30] Gold and recruit it"]);
            capturer.DiplomacyFunctions.MakeCivilizationsMeet(defenderCiv);
            capturer.GetDiplomacyManager(defenderCiv)!.DeclareWar();
            var warrior = testGame.AddUnit("Warrior", capturer, testGame.GetTile(0, 0));
            var enemy = testGame.AddUnit("Warrior", defenderCiv, testGame.GetTile(1, 0));
            enemy.Health = 0;
            int goldBefore = capturer.Gold;

            bool captured = BattleUnitCapture.TryCaptureMilitaryUnit(new MapUnitCombatant(warrior), new MapUnitCombatant(enemy), enemy.GetTile());

            Assert.True(captured);
            Assert.Equal(goldBefore + 30, capturer.Gold);
            var recruited = capturer.Units.GetCivUnits().Single(it => it != warrior);
            Assert.Equal("Warrior", recruited.Name);
            Assert.Equal(50, recruited.Health);
            Assert.Equal(0f, recruited.CurrentMovement);
            Assert.Contains(capturer.Notifications, it => it.Text == "An enemy [Warrior] has joined us!");
            Assert.Contains(defenderCiv.Notifications, it => it.Text == "An enemy [Warrior] has captured our [Warrior]");
        }

        [Fact]
        public void UncapturableDefendersAreNeverCaptured()
        {
            var capturer = testGame.AddCiv(isPlayer: true, uniques: ["When defeating a [Military] unit, earn [30] Gold and recruit it"]);
            var warrior = testGame.AddUnit("Warrior", capturer, testGame.GetTile(0, 0));
            var enemy = testGame.AddDefaultMeleeUnitWithUniques(defenderCiv, testGame.GetTile(1, 0), "Uncapturable");
            enemy.Health = 0;
            int goldBefore = capturer.Gold;

            Assert.False(BattleUnitCapture.TryCaptureMilitaryUnit(new MapUnitCombatant(warrior), new MapUnitCombatant(enemy), enemy.GetTile()));
            Assert.Equal(goldBefore, capturer.Gold);
        }
    }

    [Collection("RulesetCache")]
    public class AirInterceptionTests
    {
        private readonly TestGame testGame = new();
        private readonly CivilizationObject attackerCiv;
        private readonly CivilizationObject defenderCiv;

        public AirInterceptionTests()
        {
            testGame.MakeHexagonalMap(4);
            attackerCiv = testGame.AddCiv(isPlayer: true);
            defenderCiv = testGame.AddCiv(isPlayer: true);
            attackerCiv.DiplomacyFunctions.MakeCivilizationsMeet(defenderCiv);
            attackerCiv.GetDiplomacyManager(defenderCiv)!.DeclareWar();
            // air units can only be put in cities (or carriers)
            testGame.AddCity(attackerCiv, testGame.GetTile(0, 0));
            testGame.AddCity(defenderCiv, testGame.GetTile(2, 0));
        }

        [Fact]
        public void UnitsThatCannotBeInterceptedAreNeverIntercepted()
        {
            var attacker = testGame.AddDefaultRangedUnitWithUniques(attackerCiv, testGame.GetTile(0, 1), "Cannot be intercepted");
            var interceptor = testGame.AddUnit("Fighter", defenderCiv, testGame.GetTile(2, 0));

            var result = AirInterception.TryInterceptAirAttack(new MapUnitCombatant(attacker), testGame.GetTile(0, 1), defenderCiv, null);

            Assert.Equal(DamageDealt.None, result);
            Assert.Equal(0, interceptor.AttacksThisTurn);
        }

        [Fact]
        public void NoInterceptorMeansNoDamage()
        {
            var attacker = testGame.AddUnit("Fighter", attackerCiv, testGame.GetTile(0, 0));
            testGame.AddUnit("Warrior", defenderCiv, testGame.GetTile(2, 0));

            Assert.Equal(DamageDealt.None, AirInterception.TryInterceptAirAttack(new MapUnitCombatant(attacker), testGame.GetTile(0, 1), defenderCiv, null));
        }

        [Fact]
        public void DefenderCannotInterceptItsOwnAttackers()
        {
            var attacker = testGame.AddUnit("Fighter", attackerCiv, testGame.GetTile(0, 0));
            var interceptor = testGame.AddUnit("Fighter", defenderCiv, testGame.GetTile(2, 0));

            Assert.Equal(DamageDealt.None,
                AirInterception.TryInterceptAirAttack(new MapUnitCombatant(attacker), testGame.GetTile(0, 1), defenderCiv, new MapUnitCombatant(interceptor)));
            Assert.Equal(0, interceptor.AttacksThisTurn);
        }

        [Fact]
        public void AnInterceptorTakesTheShotDamagesTheAttackerAndThenReachesTheXpSeam()
        {
            var attacker = testGame.AddUnit("Fighter", attackerCiv, testGame.GetTile(0, 0));
            var interceptor = testGame.AddUnit("Fighter", defenderCiv, testGame.GetTile(2, 0));

            // 100% chance fighter: the shot is always taken and always hits, so damage > 0 and Battle.addXp (T083) is reached
            Assert.Throws<NotImplementedException>(() =>
                AirInterception.TryInterceptAirAttack(new MapUnitCombatant(attacker), testGame.GetTile(0, 1), defenderCiv, null));

            Assert.Equal(1, interceptor.AttacksThisTurn);
            Assert.True(attacker.Health < 100);
        }

        [Fact]
        public void AirSweepWithoutInterceptorsUsesTheAttackAndSaysSo()
        {
            var attacker = testGame.AddUnit("Fighter", attackerCiv, testGame.GetTile(0, 0));
            attacker.Action = "Air Sweep";

            AirInterception.AirSweep(new MapUnitCombatant(attacker), testGame.GetTile(0, 2));

            Assert.Equal(1, attacker.AttacksThisTurn);
            Assert.Null(attacker.Action);
            Assert.Contains(attackerCiv.Notifications, it => it.Text == "Nothing tried to intercept our [Fighter]");
        }

        [Fact]
        public void AirSweepPrefersAirInterceptorsAndThenNeedsTheBattleDamageSeam()
        {
            var attacker = testGame.AddUnit("Fighter", attackerCiv, testGame.GetTile(0, 0));
            var airInterceptor = testGame.AddUnit("Fighter", defenderCiv, testGame.GetTile(2, 0));
            testGame.AddDefaultRangedUnitWithUniques(defenderCiv, testGame.GetTile(2, 1), "[100]% chance to intercept air attacks");

            // the land interceptor on the swept tile is passed over for the air one; Battle.takeDamage (T083) is the next step
            Assert.Throws<NotImplementedException>(() => AirInterception.AirSweep(new MapUnitCombatant(attacker), testGame.GetTile(2, 1)));

            Assert.Equal(1, airInterceptor.AttacksThisTurn);
        }

        [Fact]
        public void AirSweepAgainstALandInterceptorDealsNoDamage()
        {
            var attacker = testGame.AddUnit("Fighter", attackerCiv, testGame.GetTile(0, 0));
            var interceptor = testGame.AddDefaultRangedUnitWithUniques(defenderCiv, testGame.GetTile(2, 1), "[100]% chance to intercept air attacks");
            attacker.Action = "Air Sweep";

            // a land unit's interception range is 0: the swept tile has to be its own
            AirInterception.AirSweep(new MapUnitCombatant(attacker), testGame.GetTile(2, 1));

            Assert.Equal(1, interceptor.AttacksThisTurn);
            Assert.Equal(100, attacker.Health);
            Assert.Null(attacker.Action);
            Assert.Contains(attackerCiv.Notifications, it => it.Text == $"Our [Fighter] ([-0] HP) was attacked by an intercepting [{interceptor.Name}] ([-0] HP)");
            Assert.Contains(defenderCiv.Notifications, it => it.Text == $"Our [{interceptor.Name}] ([-0] HP) intercepted and attacked an enemy [Fighter] ([-0] HP)");
        }
    }

    [Collection("RulesetCache")]
    public class GreatGeneralImplementationTests
    {
        private readonly TestGame testGame = new();
        private readonly CivilizationObject civ;
        private readonly CivilizationObject enemy;

        public GreatGeneralImplementationTests()
        {
            testGame.MakeHexagonalMap(5);
            civ = testGame.AddCiv();
            enemy = testGame.AddCiv();
            civ.DiplomacyFunctions.MakeCivilizationsMeet(enemy);
        }

        [Fact]
        public void BestAffectedTroopsTileNeedsTheEnemyCityPriorityOfT090()
        {
            MapUnitClass general = testGame.AddUnit("Great General", civ, testGame.GetTile(0, 0));

            // TODO(T090): HeadTowardsEnemyCityAutomation.getEnemyCitiesByPriority is a seam
            Assert.Throws<NotImplementedException>(() => GreatGeneralImplementation.GetBestAffectedTroopsTile(general));
        }

        [Fact]
        public void BestAffectedTroopsTilePrefersTheTileWithTheMostFriendlyMilitaryUnitsAround()
        {
            var general = testGame.AddUnit("Great General", civ, testGame.GetTile(0, 0));
            // a lone warrior next to the general, and a cluster of three two tiles away
            testGame.AddUnit("Warrior", civ, testGame.GetTile(-1, 0));
            testGame.AddUnit("Warrior", civ, testGame.GetTile(1, 1));
            testGame.AddUnit("Warrior", civ, testGame.GetTile(1, 0));
            testGame.AddUnit("Warrior", civ, testGame.GetTile(0, 1));
            testGame.AddUnit("Warrior", enemy, testGame.GetTile(-2, 0));

            var best = GreatGeneralImplementation.GetBestAffectedTroopsTile(general, []);

            Assert.NotNull(best);
            // every candidate holds one of our warriors; the one surrounded by the most of ours (within 2 tiles) and the fewest foreign ones wins
            var scores = general.Movement.GetDistanceToTiles().Keys
                .Where(it => it.MilitaryUnit?.Civ == civ)
                .Select(it => (Tile: it, Score: 2 * it.GetTilesInDistance(2).Count(n => n.MilitaryUnit?.Civ == civ)
                    - it.GetTilesInDistance(2).Count(n => n.MilitaryUnit?.Civ != civ)))
                .ToList();
            Assert.Equal(scores.Max(it => it.Score), scores.First(it => it.Tile == best).Score);
            Assert.Equal(scores.First(it => it.Score == scores.Max(m => m.Score)).Tile, best); // first maximum wins
        }

        [Fact]
        public void BestAffectedTroopsTileIsNullWithoutReachableMilitaryUnitsAndEnemyCitiesPullTheChoiceTowardsThem()
        {
            var general = testGame.AddUnit("Great General", civ, testGame.GetTile(0, 0));
            Assert.Null(GreatGeneralImplementation.GetBestAffectedTroopsTile(general, []));

            testGame.AddUnit("Warrior", civ, testGame.GetTile(-1, 0));
            testGame.AddUnit("Warrior", civ, testGame.GetTile(1, 0));
            var city = testGame.AddCity(enemy, testGame.GetTile(4, 0));
            var best = GreatGeneralImplementation.GetBestAffectedTroopsTile(general, [city]);
            Assert.Equal(testGame.GetTile(1, 0), best);
        }

        [Fact]
        public void BattleSeamsStillWaitForT083AndT090()
        {
            var warrior = testGame.AddUnit("Warrior", civ, testGame.GetTile(0, 0));
            var other = testGame.AddUnit("Warrior", enemy, testGame.GetTile(1, 0));
            var ours = new MapUnitCombatant(warrior);
            var theirs = new MapUnitCombatant(other);
            Assert.Throws<NotImplementedException>(() => BattleSeams.GetExtraRangedAttackFakeUnits(ours));
            Assert.Throws<NotImplementedException>(() => BattleSeams.MayUseNuke(ours, other.GetTile()));
            Assert.Throws<NotImplementedException>(() => BattleSeams.TakeDamage(ours, theirs));
            Assert.Throws<NotImplementedException>(() => BattleSeams.AddXp(ours, 1, theirs));
            Assert.Throws<NotImplementedException>(() => BattleSeams.TriggerDefeatUniques(ours, theirs, other.GetTile()));
            Assert.Throws<NotImplementedException>(() => BattleSeams.GetEnemyCitiesByPriority(warrior));
            BattleSeams.DestroyIfDefeated(civ, enemy); // not defeated: nothing happens
            var vanquished = testGame.AddCiv();
            Assert.True(vanquished.IsDefeated()); // no cities, no units
            Assert.Throws<NotImplementedException>(() => BattleSeams.DestroyIfDefeated(vanquished, civ));
            BattleSeams.DestroyIfDefeated(enemy, civ); // not defeated: nothing happens
        }

        [Fact]
        public void DamageDealtAddsComponentwiseAndNoneIsZero()
        {
            Assert.Equal(new DamageDealt(4, 6), new DamageDealt(1, 2) + new DamageDealt(3, 4));
            Assert.Equal(new DamageDealt(1, 2), new DamageDealt(1, 2) + DamageDealt.None);
            Assert.Equal(new DamageDealt(int.MinValue, 0), new DamageDealt(int.MaxValue, 0) + new DamageDealt(1, 0)); // Kotlin Int overflow wraps
        }
    }
}
