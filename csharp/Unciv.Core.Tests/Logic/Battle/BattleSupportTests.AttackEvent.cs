using Unciv.Core.Tests.Support;
using Unciv.Json;
using Unciv.Logic.Battle;
using Unciv.Models.Ruleset.Unique;
using Unciv.Utils;

namespace Unciv.Core.Tests.Logic.Battle;

// Port of tests/src/com/unciv/logic/battle/AttackEventTest.kt (T082). The outer class only names the Gate (BattleSupportTests).
public static partial class BattleSupportTests
{
    internal static string[] Ids(JavaHashSet<string> set) => set.OrderBy(it => it, StringComparer.Ordinal).ToArray();

    internal static string[] SetOf(params string[] ids) => ids.OrderBy(it => it, StringComparer.Ordinal).ToArray();

    [Collection("RulesetCache")]
    public class AttackEventTests
    {
        private readonly TestGame testGame = new();
        private readonly Unciv.Logic.GameInfo game;
        private readonly Unciv.Logic.Civilization.Civilization attackingCiv;
        private readonly Unciv.Logic.Civilization.Civilization defendingCiv;
        private readonly Unciv.Logic.Civilization.Civilization observer;
        private readonly Unciv.Logic.Map.Tiles.Tile source;
        private readonly Unciv.Logic.Map.Tiles.Tile target;
        private readonly Unciv.Logic.Map.MapUnits.MapUnit attacker;
        private readonly Unciv.Logic.Map.MapUnits.MapUnit defender;

        public AttackEventTests()
        {
            testGame.MakeHexagonalMap(4);
            game = testGame.GameInfo;
            attackingCiv = testGame.AddCiv();
            defendingCiv = testGame.AddCiv();
            observer = testGame.AddCiv();
            source = testGame.GetTile(0, 0);
            target = testGame.GetTile(1, 0);
            attacker = testGame.AddUnit("Archer", attackingCiv, source);
            defender = testGame.AddUnit("Warrior", defendingCiv, target);

            attacker.InstanceName = "Original archers";
            defender.InstanceName = "Original defenders";
            attackingCiv.ViewableTiles = new HashSet<Unciv.Logic.Map.Tiles.Tile> { source, target };
            defendingCiv.ViewableTiles = new HashSet<Unciv.Logic.Map.Tiles.Tile> { target };
            observer.ViewableTiles = new HashSet<Unciv.Logic.Map.Tiles.Tile> { source, target };
        }

        [Fact]
        public void IdentityEndpointsAndWitnessesRemainThoseObservedBeforeMovementOrRenaming()
        {
            game.Turns = 12;
            var recorder = new AttackRecorder(new MapUnitCombatant(attacker), target);
            recorder.SnapshotTarget(new MapUnitCombatant(defender));
            attacker.Movement.MoveToTile(testGame.GetTile(-1, 0));
            defender.Movement.MoveToTile(testGame.GetTile(2, 0));
            attacker.InstanceName = "Renamed archers";
            defender.InstanceName = "Renamed defenders";
            defendingCiv.ViewableTiles = testGame.TileMap.Values.ToHashSet();
            observer.ViewableTiles = new HashSet<Unciv.Logic.Map.Tiles.Tile>();
            game.Turns++;

            var evt = recorder.Finish(AttackResolution.Completed);

            Assert.Equal(12, evt.Turn);
            Assert.Equal(source.Position, evt.SourceTile);
            Assert.Equal(target.Position, evt.TargetTile);
            Assert.Equal(attackingCiv.CivId, evt.Attacker!.CivID);
            Assert.Equal(SetOf(attackingCiv.CivId, observer.CivId), Ids(evt.CivIdsKnowingAttackSource));
            Assert.Equal(SetOf(attackingCiv.CivId, defendingCiv.CivId, observer.CivId), Ids(evt.CivIdsKnowingAttackTarget));
            Assert.Equal("Original archers", evt.Attacker.InstanceName);
            Assert.Equal("Original defenders", evt.Targets.Single().InstanceName);
            Assert.Equal(source.Position, evt.Attacker.Position);
            Assert.Equal(target.Position, evt.Targets.Single().Position);
            Assert.False(evt.Attacker.CivIdsThatKnowMe.Contains(defendingCiv.CivId));
            Assert.True(evt.Attacker.CivIdsThatKnowMe.Contains(observer.CivId));
        }

        [Fact]
        public void InvisibleOriginRequiresAMatchingCurrentDetectorRatherThanAStaleTileCache()
        {
            attacker.Promotions.AddPromotion(testGame.CreateUnitPromotion(UniqueType.Invisible.Text()).Name);
            defendingCiv.ViewableTiles = new HashSet<Unciv.Logic.Map.Tiles.Tile> { source, target };

            var undetected = new AttackEvent(new MapUnitCombatant(attacker), target);
            Assert.False(undetected.CivIdsKnowingAttackSource.Contains(defendingCiv.CivId));
            Assert.False(undetected.Attacker!.CivIdsThatKnowMe.Contains(defendingCiv.CivId));
            Assert.True(undetected.CivIdsKnowingAttackTarget.Contains(defendingCiv.CivId));
            Assert.True(undetected.CivIdsKnowingAttackSource.Contains(attackingCiv.CivId));

            testGame.AddDefaultMeleeUnitWithUniques(defendingCiv, testGame.GetTile(-1, 0),
                "Can see invisible [Melee] units");
            defendingCiv.ViewableTiles = new HashSet<Unciv.Logic.Map.Tiles.Tile> { source, target };
            var wrongDetector = new AttackEvent(new MapUnitCombatant(attacker), target);
            Assert.False(wrongDetector.CivIdsKnowingAttackSource.Contains(defendingCiv.CivId));

            testGame.AddDefaultMeleeUnitWithUniques(defendingCiv, testGame.GetTile(0, 1),
                "Can see invisible [Ranged] units");
            defendingCiv.ViewableTiles = new HashSet<Unciv.Logic.Map.Tiles.Tile> { source, target };
            var detected = new AttackEvent(new MapUnitCombatant(attacker), target);
            Assert.True(detected.CivIdsKnowingAttackSource.Contains(defendingCiv.CivId));
            Assert.True(detected.Attacker!.CivIdsThatKnowMe.Contains(defendingCiv.CivId));
            // Learning the origin now cannot add knowledge to an earlier record.
            Assert.False(undetected.CivIdsKnowingAttackSource.Contains(defendingCiv.CivId));
        }

        [Fact]
        public void CloningDeeplyDetachesParticipantDataAndAllWitnessCollections()
        {
            var evt = CompletedEvent();
            var clone = evt.Clone();
            Assert.NotSame(evt, clone);
            Assert.NotSame(evt.Attacker, clone.Attacker);
            Assert.NotSame(evt.Targets, clone.Targets);
            Assert.NotSame(evt.Targets.Single(), clone.Targets.Single());
            Assert.NotSame(evt.Attacker!.CivIdsThatKnowMe, clone.Attacker!.CivIdsThatKnowMe);
            Assert.NotSame(evt.Targets.Single().CivIdsThatKnowMe, clone.Targets.Single().CivIdsThatKnowMe);
            AssertParticipantEquals(evt.Attacker, clone.Attacker);
            AssertParticipantEquals(evt.Targets.Single(), clone.Targets.Single());

            clone.CivIdsKnowingAttackSource.Add(defendingCiv.CivId);
            clone.CivIdsKnowingAttackTarget.Clear();
            clone.Attacker.CivIdsThatKnowMe.Clear();
            clone.Targets.Single().CivIdsThatKnowMe.Clear();
            clone.Targets.Single().InstanceName = "Changed clone";
            clone.Targets.Single().DamageReceived = 99;
            clone.Targets.Clear();

            Assert.False(evt.CivIdsKnowingAttackSource.Contains(defendingCiv.CivId));
            Assert.True(evt.CivIdsKnowingAttackTarget.Contains(defendingCiv.CivId));
            Assert.True(evt.Attacker.CivIdsThatKnowMe.Contains(attackingCiv.CivId));
            Assert.True(evt.Targets.Single().CivIdsThatKnowMe.Contains(defendingCiv.CivId));
            Assert.Equal("Original defenders", evt.Targets.Single().InstanceName);
            Assert.Equal(5, evt.Targets.Single().DamageReceived);
        }

        [Fact]
        public void EventJsonRoundTripPreservesOutcomesIdentityHealthAndObservationPermissions()
        {
            game.Turns = 12;
            var evt = CompletedEvent();
            var restored = UncivJson.Deserialize<AttackEvent>(UncivJson.Serialize(evt))!;

            Assert.Equal(evt.Turn, restored.Turn);
            Assert.Equal(evt.SourceTile, restored.SourceTile);
            Assert.Equal(evt.TargetTile, restored.TargetTile);
            Assert.Equal(evt.Attacker!.CivID, restored.Attacker!.CivID);
            Assert.Equal(evt.Resolution, restored.Resolution);
            Assert.Equal(Ids(evt.CivIdsKnowingAttackSource), Ids(restored.CivIdsKnowingAttackSource));
            Assert.Equal(Ids(evt.CivIdsKnowingAttackTarget), Ids(restored.CivIdsKnowingAttackTarget));
            AssertParticipantEquals(evt.Attacker, restored.Attacker);
            AssertParticipantEquals(evt.Targets.Single(), restored.Targets.Single());
            restored.Attacker.CivIdsThatKnowMe.Clear();
            restored.Targets.Clear();
            Assert.True(evt.Attacker.CivIdsThatKnowMe.Contains(attackingCiv.CivId));
            Assert.Single(evt.Targets);
        }

        [Fact]
        public void JsonUsesTheKotlinFieldNames()
        {
            string json = UncivJson.Serialize(CompletedEvent());
            foreach (var name in new[] { "\"turn\"", "\"sourceTile\"", "\"targetTile\"", "\"civIdsKnowingAttackSource\"", "\"civIdsKnowingAttackTarget\"",
                         "\"resolution\"", "\"attacker\"", "\"targets\"", "\"unitID\"", "\"civID\"", "\"healthBefore\"", "\"healthAfter\"", "\"damageReceived\"",
                         "\"outcome\"", "\"civIdsThatKnowMe\"" })
                Assert.Contains(name, json);
        }

        private AttackEvent CompletedEvent()
        {
            attacker.Health = 75;
            defender.Health = 40;
            var recorder = new AttackRecorder(new MapUnitCombatant(attacker), target);
            recorder.SnapshotTarget(new MapUnitCombatant(defender));
            recorder.RecordDamage(attacker, 9);
            attacker.TakeDamage(9);
            recorder.RecordDamage(defender, 5);
            defender.TakeDamage(5);
            return recorder.Finish(AttackResolution.Completed);
        }

        private static void AssertParticipantEquals(AttackParticipant expected, AttackParticipant actual)
        {
            Assert.Equal(expected.UnitID, actual.UnitID);
            Assert.Equal(expected.CivID, actual.CivID);
            Assert.Equal(expected.Name, actual.Name);
            Assert.Equal(expected.InstanceName, actual.InstanceName);
            Assert.Equal(expected.Position, actual.Position);
            Assert.Equal(expected.HealthBefore, actual.HealthBefore);
            Assert.Equal(expected.HealthAfter, actual.HealthAfter);
            Assert.Equal(expected.DamageReceived, actual.DamageReceived);
            Assert.Equal(expected.Outcome, actual.Outcome);
            Assert.Equal(Ids(expected.CivIdsThatKnowMe), Ids(actual.CivIdsThatKnowMe));
        }
    }
}
