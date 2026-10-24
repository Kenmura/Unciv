using Unciv.Core.Tests.Support;
using Unciv.Logic.Battle;
using Unciv.Logic.Map;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Core.Tests.Logic.Battle;

// Port of tests/src/com/unciv/logic/battle/TargetHelperTest.kt (T082), plus C#-only cases for the branches the Kotlin test does not reach.
// The Kotlin setUp's `useAStarPathfinding = true` is not ported: the A* branches of UnitMovement are TODO(T093) (the default, off, is used).
public static partial class BattleSupportTests
{
    [Collection("RulesetCache")]
    public class TargetHelperTests
    {
        private readonly CivilizationObject attackerCiv;
        private readonly CivilizationObject defenderCiv;
        private const float DefaultWarriorMovePoints = 2f;
        private const float MovementLeftAfterMovingToAttackTile = 2f;
        private readonly TestGame testGame = new();

        public TargetHelperTests()
        {
            testGame.MakeHexagonalMap(6);
            attackerCiv = testGame.AddCiv();
            defenderCiv = testGame.AddCiv();

            attackerCiv.DiplomacyFunctions.MakeCivilizationsMeet(defenderCiv);
            attackerCiv.GetDiplomacyManager(defenderCiv)!.DeclareWar();
        }

        [Fact]
        public void ShouldRetrieveCityBombardableTiles()
        {
            var attackerCity = testGame.AddCity(attackerCiv, testGame.GetTile(HexCoord.Zero));

            testGame.AddUnit("Warrior", attackerCiv, testGame.GetTile(HexCoord.Zero)); // own unit in own city, not bombardable
            testGame.AddUnit("Warrior", attackerCiv, testGame.GetTile(1, 0)); // own unit in own city range, not bombardable
            testGame.AddUnit("Warrior", attackerCiv, testGame.GetTile(3, 3)); // own unit outside city range, not bombardable
            testGame.AddUnit("Warrior", defenderCiv, testGame.GetTile(1, 1)); // enemy unit inside city range, bombardable
            testGame.AddUnit("Warrior", defenderCiv, testGame.GetTile(-3, -3)); // enemy unit outside city range, not bombardable

            var tile = testGame.SetTileTerrain(new HexCoord(-2, -1), Constants.Coast);
            testGame.AddTileToCity(attackerCity, tile);
            testGame.AddUnit("Submarine", defenderCiv, tile); // enemy unit inside city range, invisible, not bombardable

            var bombardableTiles = TargetHelper.GetBombardableTiles(attackerCity).ToList();

            Assert.Single(bombardableTiles);
            Assert.Contains(testGame.GetTile(1, 1), bombardableTiles);
        }

        private Unciv.Logic.Map.MapUnits.MapUnit Attacker(string name, TileClass tile)
        {
            var unit = testGame.AddUnit(name, attackerCiv, tile);
            unit.CurrentMovement = DefaultWarriorMovePoints;
            return unit;
        }

        private static List<AttackableTile> Enemies(Unciv.Logic.Map.MapUnits.MapUnit unit) =>
            TargetHelper.GetAttackableEnemies(unit, unit.Movement.GetDistanceToTiles());

        [Fact]
        public void ShouldGetAttackableTileWhenMeleeNextToEnemyUnit()
        {
            var attackFromTile = testGame.GetTile(HexCoord.Zero);
            var attackedTile = testGame.GetTile(1, 0);
            var attackerUnit = Attacker("Warrior", attackFromTile);
            testGame.AddUnit("Warrior", defenderCiv, attackedTile);

            var attackableEnemies = Enemies(attackerUnit);

            Assert.Single(attackableEnemies);
            var attackableEnemy = attackableEnemies[0];
            Assert.Equal(attackFromTile, attackableEnemy.TileToAttackFrom);
            Assert.Equal(attackedTile, attackableEnemy.TileToAttack);
            Assert.Equal(DefaultWarriorMovePoints, attackableEnemy.MovementLeftAfterMovingToAttackTile);
        }

        [Fact]
        public void ShouldGetNoAttackableTilesWhenUnitNextToAlliedUnit()
        {
            var attackerUnit = Attacker("Warrior", testGame.GetTile(HexCoord.Zero));
            testGame.AddUnit("Warrior", attackerCiv, testGame.GetTile(1, 0));

            Assert.Empty(Enemies(attackerUnit));
        }

        [Fact]
        public void ShouldGetAttackableTileWhenMeleeEnemyReachable()
        {
            var attackedTile = testGame.GetTile(2, 0); // move one tile -> melee next to each other -> attack
            var attackerUnit = Attacker("Warrior", testGame.GetTile(HexCoord.Zero));
            testGame.AddUnit("Warrior", defenderCiv, attackedTile);

            var attackableEnemies = Enemies(attackerUnit);

            Assert.Single(attackableEnemies);
            var attackableEnemy = attackableEnemies[0];
            Assert.Equal(testGame.GetTile(1, 0), attackableEnemy.TileToAttackFrom); // the attacker unit has moved...
            Assert.Equal(attackedTile, attackableEnemy.TileToAttack);
            Assert.Equal(DefaultWarriorMovePoints - 1, attackableEnemy.MovementLeftAfterMovingToAttackTile); // ...and thus when attacking has one fewer move point
        }

        [Fact]
        public void ShouldGetNoAttackableTilesWhenMeleeEnemyOutOfReach()
        {
            var attackerUnit = Attacker("Warrior", testGame.GetTile(HexCoord.Zero));
            testGame.AddUnit("Warrior", defenderCiv, testGame.GetTile(3, 0)); // out of reach

            Assert.Empty(Enemies(attackerUnit));
        }

        [Fact]
        public void ShouldGetAttackableTilesWhenRangedNextToMeleeEnemyUnitWithZoc()
        {
            var attackerTile = testGame.GetTile(HexCoord.Zero);
            var attackedTile = testGame.GetTile(1, 0);
            var attackerUnit = Attacker("Archer", attackerTile);
            attackerUnit.CurrentMovement = MovementLeftAfterMovingToAttackTile;
            testGame.AddUnit("Warrior", defenderCiv, attackedTile);

            var attackableEnemies = Enemies(attackerUnit);

            Assert.Equal(4, attackableEnemies.Count);
            Assert.Equal(attackerTile, attackableEnemies[0].TileToAttackFrom);
            Assert.Equal(attackedTile, attackableEnemies[0].TileToAttack);
            Assert.Equal(MovementLeftAfterMovingToAttackTile, attackableEnemies[0].MovementLeftAfterMovingToAttackTile);
            Assert.Equal(testGame.GetTile(-1, -1), attackableEnemies[1].TileToAttackFrom);
            Assert.Equal(attackedTile, attackableEnemies[1].TileToAttack);
            Assert.Equal(MovementLeftAfterMovingToAttackTile - 1, attackableEnemies[1].MovementLeftAfterMovingToAttackTile);
            Assert.Equal(testGame.GetTile(0, 1), attackableEnemies[2].TileToAttackFrom);
            Assert.Equal(attackedTile, attackableEnemies[2].TileToAttack);
            Assert.Equal(MovementLeftAfterMovingToAttackTile - 1, attackableEnemies[2].MovementLeftAfterMovingToAttackTile);
            Assert.Equal(testGame.GetTile(-1, 0), attackableEnemies[3].TileToAttackFrom);
            Assert.Equal(attackedTile, attackableEnemies[3].TileToAttack);
            Assert.Equal(MovementLeftAfterMovingToAttackTile - 1, attackableEnemies[3].MovementLeftAfterMovingToAttackTile);
        }

        [Fact]
        public void ShouldGetAttackableTilesWhenRangedAlreadyInRangeToEnemyUnit()
        {
            var tileToAttackFrom = testGame.GetTile(-1, 0);
            var tileToAttack = testGame.GetTile(1, 0);
            var attackerUnit = Attacker("Archer", tileToAttackFrom);
            attackerUnit.CurrentMovement = MovementLeftAfterMovingToAttackTile;
            var combatant = new MapUnitCombatant(testGame.AddUnit("Warrior", defenderCiv, tileToAttack));

            var attackableEnemies = Enemies(attackerUnit);

            Assert.Equal(4, attackableEnemies.Count);
            Assert.Equal(new AttackableTile(tileToAttackFrom, tileToAttack, MovementLeftAfterMovingToAttackTile, combatant), attackableEnemies[0]);
            Assert.Contains(new AttackableTile(testGame.GetTile(0, 1), tileToAttack, MovementLeftAfterMovingToAttackTile - 1, combatant), attackableEnemies);
            Assert.Contains(new AttackableTile(testGame.GetTile(-1, -1), tileToAttack, MovementLeftAfterMovingToAttackTile - 1, combatant), attackableEnemies);
            Assert.Contains(new AttackableTile(testGame.GetTile(HexCoord.Zero), tileToAttack, MovementLeftAfterMovingToAttackTile - 1, combatant), attackableEnemies);
        }

        [Fact]
        public void ShouldGetAttackableTilesWhenRangedMovableInRangeToEnemyUnit()
        {
            var attackedTile = testGame.GetTile(1, 0);
            var attackerUnit = Attacker("Archer", testGame.GetTile(-2, 0));
            attackerUnit.CurrentMovement = MovementLeftAfterMovingToAttackTile;
            testGame.AddUnit("Warrior", attackerCiv, testGame.GetTile(0, 1)); // otherwise the archer cannot see the enemy warrior
            testGame.AddUnit("Warrior", defenderCiv, attackedTile);

            var attackableEnemies = Enemies(attackerUnit);

            Assert.Single(attackableEnemies);
            Assert.Equal(testGame.GetTile(-1, 0), attackableEnemies[0].TileToAttackFrom);
            Assert.Equal(attackedTile, attackableEnemies[0].TileToAttack);
            Assert.Equal(MovementLeftAfterMovingToAttackTile - 1, attackableEnemies[0].MovementLeftAfterMovingToAttackTile);
        }

        [Fact]
        public void ShouldGetNoAttackableTilesWhenEnemyUnitIsOutOfSight()
        {
            var attackerUnit = Attacker("Archer", testGame.GetTile(-2, 0)); // two tile radius sight -> cannot see enemy warrior
            attackerUnit.CurrentMovement = MovementLeftAfterMovingToAttackTile;
            testGame.AddUnit("Warrior", defenderCiv, testGame.GetTile(1, 0));

            Assert.Empty(Enemies(attackerUnit));
        }

        [Fact]
        public void ShouldGetNoAttackableTilesWhenTerrainObstacle()
        {
            testGame.SetTileFeatures(HexCoord.Zero, Constants.Hill);
            testGame.SetTileFeatures(new HexCoord(1, 1), Constants.Hill);
            testGame.SetTileFeatures(new HexCoord(0, -1), Constants.Hill);

            var attackerUnit = Attacker("Archer", testGame.GetTile(-1, 0));
            attackerUnit.CurrentMovement = MovementLeftAfterMovingToAttackTile;
            testGame.AddUnit("Warrior", defenderCiv, testGame.GetTile(1, 0));

            Assert.Empty(Enemies(attackerUnit));
        }

        [Fact]
        public void ShouldGetNoAttackableTilesWhenHasCannotAttackUnique()
        {
            var attackerUnit = testGame.AddDefaultMeleeUnitWithUniques(attackerCiv, testGame.GetTile(HexCoord.Zero), "Cannot attack");
            attackerUnit.CurrentMovement = 2f;
            testGame.AddUnit("Warrior", defenderCiv, testGame.GetTile(1, 0));

            Assert.Empty(Enemies(attackerUnit));
        }

        // ---- C#-only cases

        [Fact]
        public void StayOnTileOnlyAttacksFromTheCurrentTile()
        {
            var attackFromTile = testGame.GetTile(HexCoord.Zero);
            var attackerUnit = Attacker("Warrior", attackFromTile);
            testGame.AddUnit("Warrior", defenderCiv, testGame.GetTile(2, 0));
            Assert.Single(Enemies(attackerUnit));

            Assert.Empty(TargetHelper.GetAttackableEnemies(attackerUnit, attackerUnit.Movement.GetDistanceToTiles(), stayOnTile: true));
        }

        [Fact]
        public void TilesToCheckReplacesTheCivsViewableTiles()
        {
            var attackFromTile = testGame.GetTile(-1, 0);
            var attackedTile = testGame.GetTile(1, 0);
            var attackerUnit = Attacker("Archer", attackFromTile);
            testGame.AddUnit("Warrior", defenderCiv, attackedTile);

            Assert.NotEmpty(TargetHelper.GetAttackableEnemies(attackerUnit, attackerUnit.Movement.GetDistanceToTiles(), [attackedTile], true));
            Assert.Empty(TargetHelper.GetAttackableEnemies(attackerUnit, attackerUnit.Movement.GetDistanceToTiles(), [], true));
        }

        [Fact]
        public void ContainsAttackableEnemyIsFalseForOwnUnitsEmptyTilesAndPeace()
        {
            var attackerUnit = Attacker("Warrior", testGame.GetTile(HexCoord.Zero));
            var combatant = new MapUnitCombatant(attackerUnit);
            Assert.False(TargetHelper.ContainsAttackableEnemy(testGame.GetTile(1, 0), combatant)); // empty
            testGame.AddUnit("Warrior", attackerCiv, testGame.GetTile(1, 0));
            Assert.False(TargetHelper.ContainsAttackableEnemy(testGame.GetTile(1, 0), combatant)); // own unit
            var thirdCiv = testGame.AddCiv();
            testGame.AddUnit("Warrior", thirdCiv, testGame.GetTile(0, 1));
            Assert.False(TargetHelper.ContainsAttackableEnemy(testGame.GetTile(0, 1), combatant)); // not at war
            testGame.AddUnit("Warrior", defenderCiv, testGame.GetTile(-1, 0));
            Assert.True(TargetHelper.ContainsAttackableEnemy(testGame.GetTile(-1, 0), combatant));
        }

        [Fact]
        public void CityJustConqueredCannotBeAttackedAndEnemyCityCanBe()
        {
            var attackerUnit = Attacker("Warrior", testGame.GetTile(HexCoord.Zero));
            var city = testGame.AddCity(defenderCiv, testGame.GetTile(1, 0));
            var combatant = new MapUnitCombatant(attackerUnit);
            Assert.True(TargetHelper.ContainsAttackableEnemy(testGame.GetTile(1, 0), combatant));
            city.HasJustBeenConquered = true;
            Assert.False(TargetHelper.ContainsAttackableEnemy(testGame.GetTile(1, 0), combatant));
        }

        [Fact]
        public void CanOnlyAttackUnitsFiltersTheDefender()
        {
            var attackerUnit = testGame.AddDefaultMeleeUnitWithUniques(attackerCiv, testGame.GetTile(HexCoord.Zero), "Can only attack [Water] units");
            testGame.AddUnit("Warrior", defenderCiv, testGame.GetTile(1, 0));
            Assert.False(TargetHelper.ContainsAttackableEnemy(testGame.GetTile(1, 0), new MapUnitCombatant(attackerUnit)));
        }

        [Fact]
        public void BattleSeamNukesAndCombatantOfTileAreWired()
        {
            Assert.Null(BattleSeams.GetMapCombatantOfTile(testGame.GetTile(1, 0)));
            var civilian = testGame.AddUnit("Worker", defenderCiv, testGame.GetTile(1, 0));
            Assert.Equal(civilian, ((MapUnitCombatant)BattleSeams.GetMapCombatantOfTile(testGame.GetTile(1, 0))!).Unit);
            var warrior = testGame.AddUnit("Warrior", defenderCiv, testGame.GetTile(1, 0));
            Assert.Equal(warrior, ((MapUnitCombatant)BattleSeams.GetMapCombatantOfTile(testGame.GetTile(1, 0))!).Unit);
            testGame.AddCity(defenderCiv, testGame.GetTile(2, 0));
            Assert.IsType<CityCombatant>(BattleSeams.GetMapCombatantOfTile(testGame.GetTile(2, 0)));
        }
    }
}
