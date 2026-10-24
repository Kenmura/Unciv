using Unciv.Core.Tests.Support;
using Unciv.Logic.Battle;
using MapUnitClass = Unciv.Logic.Map.MapUnits.MapUnit;

namespace Unciv.Core.Tests.Logic.Battle;

// Port of tests/src/com/unciv/logic/battle/AttackRecorderTest.kt (T082). The Kotlin test reaches the internal recorder API by reflection
// (AttackRecorderTestHelper.kt); here the internals are visible to the test assembly.
public static partial class BattleSupportTests
{
    [Collection("RulesetCache")]
    public class AttackRecorderTests
    {
        private readonly TestGame testGame = new();
        private readonly Unciv.Logic.GameInfo game;
        private readonly Unciv.Logic.Civilization.Civilization attackingCiv;
        private readonly Unciv.Logic.Civilization.Civilization defendingCiv;
        private readonly Unciv.Logic.Map.Tiles.Tile source;
        private readonly Unciv.Logic.Map.Tiles.Tile target;
        private readonly MapUnitClass attacker;
        private readonly MapUnitClass defender;

        public AttackRecorderTests()
        {
            testGame.MakeHexagonalMap(4);
            game = testGame.GameInfo;
            attackingCiv = testGame.AddCiv();
            defendingCiv = testGame.AddCiv();
            source = testGame.GetTile(0, 0);
            target = testGame.GetTile(1, 0);
            attacker = testGame.AddUnit("Archer", attackingCiv, source);
            defender = testGame.AddUnit("Warrior", defendingCiv, target);
        }

        private AttackRecorder Recorder() => new(new MapUnitCombatant(attacker), target);

        /// <summary>Emulates a future caller that explicitly reports an effect before applying it.</summary>
        private static void DealDamage(AttackRecorder recorder, MapUnitClass unit, int amount)
        {
            recorder.RecordDamage(unit, amount);
            unit.TakeDamage(amount);
        }

        [Fact]
        public void ExplicitDamageAndHealingRetainCumulativeHpLossRatherThanNetDamage()
        {
            defender.Health = 60;
            var recorder = Recorder();
            recorder.SnapshotTarget(new MapUnitCombatant(defender));

            DealDamage(recorder, defender, 10);
            recorder.MarkUnitAffected(defender);
            defender.HealBy(80);
            DealDamage(recorder, defender, 7);
            var record = recorder.Finish(AttackResolution.Completed).Targets.Single();

            Assert.Equal(60, record.HealthBefore);
            Assert.Equal(93, defender.Health);
            Assert.Equal(93, record.HealthAfter);
            Assert.Equal(17, record.DamageReceived);
            Assert.Equal(AttackParticipantOutcome.Survived, record.Outcome);
        }

        [Fact]
        public void ReportedActualHpLossStaysDistinctFromTheRawOverkillAmount()
        {
            defender.Health = 5;
            var recorder = Recorder();

            // The caller supplies actual HP lost; the recorder does not apply or calculate damage.
            recorder.RecordDamage(defender, 5);
            defender.TakeDamage(500);
            var record = recorder.Finish(AttackResolution.Completed).Targets.Single();

            Assert.True(defender.IsDestroyed);
            Assert.Null(defendingCiv.Units.GetUnitById(defender.Id));
            Assert.Equal(5, record.HealthBefore);
            Assert.Equal(0, record.HealthAfter);
            Assert.Equal(5, record.DamageReceived);
            Assert.Equal(AttackParticipantOutcome.Destroyed, record.Outcome);
        }

        [Fact]
        public void ExplicitDestructionRetainsPositiveHpWithoutInventingDamage()
        {
            defender.Health = 60;
            var recorder = Recorder();

            recorder.RecordDestruction(defender);
            defender.Destroy();
            var record = recorder.Finish(AttackResolution.Completed).Targets.Single();

            Assert.True(defender.IsDestroyed);
            Assert.Equal(defender.Id, record.UnitID);
            Assert.Equal(60, record.HealthBefore);
            Assert.Equal(60, record.HealthAfter);
            Assert.Equal(0, record.DamageReceived);
            Assert.Equal(AttackParticipantOutcome.Destroyed, record.Outcome);
        }

        [Fact]
        public void HealingAloneRecordsTheAffectedUnitBeforeItsHpChanges()
        {
            defender.Health = 60;
            var recorder = Recorder();

            recorder.MarkUnitAffected(defender);
            defender.HealBy(50);
            var record = recorder.Finish(AttackResolution.Completed).Targets.Single();

            Assert.Equal(defender.Id, record.UnitID);
            Assert.Equal(60, record.HealthBefore);
            Assert.Equal(100, record.HealthAfter);
            Assert.Equal(0, record.DamageReceived);
            Assert.Equal(AttackParticipantOutcome.Survived, record.Outcome);
        }

        [Fact]
        public void ZeroAndNegativeDamageDoNotInventHpLossOrAdditionalTargets()
        {
            attacker.Health = 60;
            var recorder = Recorder();

            DealDamage(recorder, attacker, 0);
            DealDamage(recorder, attacker, -9);
            DealDamage(recorder, defender, 0);
            var evt = recorder.Finish(AttackResolution.Completed);

            Assert.Equal(69, evt.Attacker!.HealthAfter);
            Assert.Equal(0, evt.Attacker.DamageReceived);
            Assert.Empty(evt.Targets);
        }

        [Fact]
        public void OverlappingRecordersAttributeOnlyTheirExplicitEffects()
        {
            var outer = Recorder();
            DealDamage(outer, attacker, 2);
            var inner = Recorder();
            DealDamage(inner, attacker, 3);
            DealDamage(outer, attacker, 5);
            attacker.TakeDamage(7);
            defender.TakeDamage(11); // No recorder: this does not register a target with either attack.
            var innerEvent = inner.Finish(AttackResolution.Completed);
            DealDamage(outer, attacker, 11);
            var outerEvent = outer.Finish(AttackResolution.Completed);

            Assert.Equal(3, innerEvent.Attacker!.DamageReceived);
            Assert.Equal(83, innerEvent.Attacker.HealthAfter);
            Assert.Equal(18, outerEvent.Attacker!.DamageReceived);
            Assert.Equal(72, outerEvent.Attacker.HealthAfter);
            Assert.Empty(innerEvent.Targets);
            Assert.Empty(outerEvent.Targets);
            Assert.Equal(89, defender.Health);
        }

        [Fact]
        public void IntendedTargetIsRetainedOnceEvenWhenItReceivesNoDamage()
        {
            var recorder = Recorder();
            recorder.SnapshotTarget(new MapUnitCombatant(defender));
            recorder.SnapshotTarget(new MapUnitCombatant(defender));

            var record = recorder.Finish(AttackResolution.Completed).Targets.Single();

            Assert.Equal(defender.Id, record.UnitID);
            Assert.Equal(100, record.HealthBefore);
            Assert.Equal(100, record.HealthAfter);
            Assert.Equal(0, record.DamageReceived);
            Assert.Equal(AttackParticipantOutcome.Survived, record.Outcome);
        }

        [Fact]
        public void AnotherGameWithIdenticalSaveAndUnitIdsIsRejectedBeforeMutation()
        {
            var otherGame = new TestGame();
            otherGame.MakeHexagonalMap(4);
            otherGame.GameInfo.GameId = game.GameId;
            var otherCiv = otherGame.AddCiv();
            var otherUnit = otherGame.AddUnit("Archer", otherCiv, otherGame.GetTile(0, 0));
            otherUnit.Health = 60;
            Assert.Equal(attacker.Id, otherUnit.Id);
            var recorder = Recorder();

            Assert.Throws<ArgumentException>(() => new AttackRecorder(new MapUnitCombatant(attacker), otherGame.GetTile(1, 0)));
            foreach (int amount in new[] { 0, -9, 9 })
                Assert.Throws<ArgumentException>(() => recorder.RecordDamage(otherUnit, amount));
            Assert.Throws<ArgumentException>(() => recorder.MarkUnitAffected(otherUnit));
            Assert.Throws<ArgumentException>(() => recorder.RecordDestruction(otherUnit));
            Assert.Throws<ArgumentException>(() => recorder.SnapshotTarget(new MapUnitCombatant(otherUnit)));
            Assert.Throws<ArgumentException>(() => recorder.RecordOutcome(new MapUnitCombatant(otherUnit), AttackParticipantOutcome.Withdrew));

            Assert.Equal(60, otherUnit.Health);
            Assert.Same(otherUnit, otherCiv.Units.GetUnitById(otherUnit.Id));
            Assert.Same(otherUnit, otherGame.GetTile(0, 0).MilitaryUnit);
            DealDamage(recorder, attacker, 2);
            var evt = recorder.Finish(AttackResolution.Completed);
            Assert.Equal(2, evt.Attacker!.DamageReceived);
            Assert.Empty(evt.Targets);
        }

        [Fact]
        public void FinishedRecorderRejectsReuseBeforeUnitChangesAndReleasesLiveState()
        {
            var recorder = Recorder();
            DealDamage(recorder, attacker, 7);
            var evt = recorder.Finish(AttackResolution.Completed);

            foreach (int amount in new[] { 0, -3, 3 })
                Assert.Throws<InvalidOperationException>(() => recorder.RecordDamage(attacker, amount));
            Assert.Throws<InvalidOperationException>(() => recorder.MarkUnitAffected(attacker));
            Assert.Throws<InvalidOperationException>(() => recorder.RecordDestruction(attacker));
            Assert.Throws<InvalidOperationException>(() => recorder.SnapshotTarget(new MapUnitCombatant(defender)));
            Assert.Throws<InvalidOperationException>(() => recorder.RecordOutcome(new MapUnitCombatant(defender), AttackParticipantOutcome.Withdrew));
            Assert.Throws<InvalidOperationException>(() => recorder.Finish(AttackResolution.Completed));
            Assert.Throws<InvalidOperationException>(() => recorder.FinishIncomplete());
            Assert.Equal(93, attacker.Health);
            Assert.False(attacker.IsDestroyed);
            Assert.Same(attacker, attackingCiv.Units.GetUnitById(attacker.Id));
            Assert.Equal(93, evt.Attacker!.HealthAfter);
            Assert.Equal(7, evt.Attacker.DamageReceived);
        }

        [Fact]
        public void IncompleteFinalizationPreservesPartialEffectsWithoutRollingThemBack()
        {
            var recorder = Recorder();
            recorder.SnapshotTarget(new MapUnitCombatant(defender));
            DealDamage(recorder, defender, 7);
            Assert.Throws<ArgumentException>(() => recorder.Finish(null));

            var evt = recorder.FinishIncomplete();
            Assert.Throws<InvalidOperationException>(() => recorder.RecordDamage(defender, 3));
            defender.TakeDamage(11);

            Assert.Equal(82, defender.Health);
            Assert.Null(evt.Resolution);
            Assert.Equal(93, evt.Targets.Single().HealthAfter);
            Assert.Equal(7, evt.Targets.Single().DamageReceived);
            Assert.Equal(AttackParticipantOutcome.Survived, evt.Targets.Single().Outcome);
        }

        [Fact]
        public void FinalHealthFollowsAnUnreportedUpgradeByStableIdWithoutRewritingOriginalIdentity()
        {
            attacker.Health = 63;
            attacker.InstanceName = "Original archers";
            var recorder = Recorder();
            DealDamage(recorder, attacker, 4);

            attacker.Upgrade.PerformUpgrade(attacker.Upgrade.GetUpgrades().First(it => it.UpgradedUnit.Name == "Composite Bowman"), isFree: true);
            var successor = attackingCiv.Units.GetUnitById(attacker.Id)!;
            successor.InstanceName = "New composite bowmen";
            successor.TakeDamage(9); // No callback replaces the recorder's old unit reference.
            var record = recorder.Finish(AttackResolution.Completed).Attacker!;

            Assert.NotSame(attacker, successor);
            Assert.Equal("Composite Bowman", successor.Name);
            Assert.Equal(attacker.Id, record.UnitID);
            Assert.Equal("Archer", record.Name);
            Assert.Equal("Original archers", record.InstanceName);
            Assert.Equal(63, record.HealthBefore);
            Assert.Equal(50, record.HealthAfter);
            Assert.Equal(4, record.DamageReceived);
            Assert.Equal(AttackParticipantOutcome.Survived, record.Outcome);
        }

        [Fact]
        public void RecordingApisDoNotApplyDamageHealingDestructionOrMovementThemselves()
        {
            defender.Health = 60;
            var recorder = Recorder();
            recorder.SnapshotTarget(new MapUnitCombatant(defender));
            recorder.RecordDamage(attacker, 7);
            recorder.MarkUnitAffected(defender);
            recorder.RecordDestruction(defender);

            Assert.Equal(100, attacker.Health);
            Assert.Equal(60, defender.Health);
            Assert.False(defender.IsDestroyed);
            Assert.Equal(source, attacker.GetTile());
            Assert.Equal(target, defender.GetTile());
            Assert.Equal(0, attacker.AttacksThisTurn);
            Assert.Same(defender, defendingCiv.Units.GetUnitById(defender.Id));
            var evt = recorder.Finish(AttackResolution.Completed);
            Assert.Equal(7, evt.Attacker!.DamageReceived);
            Assert.Equal(100, evt.Attacker.HealthAfter);
            Assert.Equal(AttackParticipantOutcome.Survived, evt.Targets.Single().Outcome);
        }

        [Fact]
        public void CaptureAndExplicitWithdrawalRetainTheParticipantsOriginalOwnerAndTile()
        {
            var captureRecorder = Recorder();
            captureRecorder.SnapshotTarget(new MapUnitCombatant(defender));
            string originalOwner = defender.Civ.CivId;
            defender.CapturedBy(attackingCiv);
            var captured = captureRecorder.Finish(AttackResolution.Completed).Targets.Single();
            Assert.Equal(originalOwner, captured.CivID);
            Assert.Equal(defender.Id, captured.UnitID);
            Assert.Equal(AttackParticipantOutcome.Captured, captured.Outcome);
            Assert.Equal(0, captured.DamageReceived);

            var withdrawing = testGame.AddUnit("Warrior", defendingCiv, testGame.GetTile(2, 0));
            var originalPosition = withdrawing.GetTile().Position;
            var withdrawalRecorder = new AttackRecorder(new MapUnitCombatant(attacker), withdrawing.GetTile());
            withdrawalRecorder.SnapshotTarget(new MapUnitCombatant(withdrawing));
            withdrawing.Movement.MoveToTile(testGame.GetTile(2, 1));
            withdrawalRecorder.RecordOutcome(new MapUnitCombatant(withdrawing), AttackParticipantOutcome.Withdrew);
            var withdrawnEvent = withdrawalRecorder.Finish(AttackResolution.Withdrawn);
            var withdrawn = withdrawnEvent.Targets.Single();
            Assert.Equal(AttackResolution.Withdrawn, withdrawnEvent.Resolution);
            Assert.Equal(originalPosition, withdrawn.Position);
            Assert.Equal(AttackParticipantOutcome.Withdrew, withdrawn.Outcome);
            Assert.Equal(0, withdrawn.DamageReceived);
        }

        [Fact]
        public void RecordOutcomeOnlyAcceptsCapturedOrWithdrew()
        {
            var recorder = Recorder();
            Assert.Throws<ArgumentException>(() => recorder.RecordOutcome(new MapUnitCombatant(defender), AttackParticipantOutcome.Survived));
            Assert.Throws<ArgumentException>(() => recorder.RecordOutcome(new MapUnitCombatant(defender), null));
        }
    }
}
