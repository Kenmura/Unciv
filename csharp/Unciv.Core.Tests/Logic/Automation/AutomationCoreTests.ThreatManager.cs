using Unciv.Core.Tests.Support;
using Unciv.Logic.Automation.Civilization;
using Unciv.View;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Core.Tests.Logic.Automation;

// T087. Port of tests/src/com/unciv/logic/civilization/managers/ThreatManagerTests.kt (same case names), plus C#-only cases for the
// ThreatManager members the Kotlin test does not reach.
public static partial class AutomationCoreTests
{
    [Collection("RulesetCache")]
    public class ThreatManagerTests
    {
        private readonly TestGame testGame = new();
        private readonly CivilizationObject civ;
        private readonly CivilizationObject neutralCiv;
        private readonly CivilizationObject enemyCiv;
        private readonly GameView gameView;
        private AutomationCache cache = new();

        private CivView CivView => gameView.CivView;

        private TileView View(TileClass tile) => gameView.TileMapView.GetTile(tile);

        public ThreatManagerTests()
        {
            civ = testGame.AddCiv();
            neutralCiv = testGame.AddCiv();
            enemyCiv = testGame.AddCiv();
            testGame.GameInfo.EngineOptions.VisibleMap = true; // Needed to be able to see the enemy units
            testGame.MakeHexagonalMap(10);
            civ.DiplomacyFunctions.MakeCivilizationsMeet(enemyCiv);
            civ.DiplomacyFunctions.MakeCivilizationsMeet(neutralCiv);
            civ.GetDiplomacyManager(enemyCiv)!.DeclareWar();
            gameView = new GameView(testGame.GameInfo, civ);
            cache = new AutomationCache();
        }

        [Fact]
        public void DistanceToClosestEnemyWithNoEnemies()
        {
            var centerTile = testGame.GetTile(0, 0);
            Assert.Equal(5, ThreatManager.GetDistanceToClosestEnemyUnit(CivView, cache, View(centerTile), 5, false));
        }

        [Fact]
        public void FindTilesWithEnemiesWithNoEnemies()
        {
            var centerTile = testGame.GetTile(0, 0);
            Assert.Empty(ThreatManager.GetTilesWithEnemyUnitsInDistance(CivView, cache, View(centerTile), 5));
        }

        [Fact]
        public void FindEnemiesOnTilesWithNoEnemies()
        {
            var centerTile = testGame.GetTile(0, 0);
            Assert.Empty(ThreatManager.GetEnemyUnitsOnTiles(CivView, ThreatManager.GetTilesWithEnemyUnitsInDistance(CivView, cache, View(centerTile), 5)));
        }

        [Fact]
        public void FindDistanceToEnemy()
        {
            var centerTile = testGame.GetTile(0, 0);
            testGame.AddUnit("Warrior", enemyCiv, testGame.GetTile(3, 0));
            testGame.AddUnit("Warrior", neutralCiv, testGame.GetTile(1, 1));
            Assert.Equal(3, ThreatManager.GetDistanceToClosestEnemyUnit(CivView, cache, View(centerTile), 5));
        }

        [Fact]
        public void FindDistanceToCloserEnemy()
        {
            var centerTile = testGame.GetTile(0, 0);
            testGame.AddUnit("Warrior", enemyCiv, testGame.GetTile(3, 0));
            testGame.AddUnit("Warrior", enemyCiv, testGame.GetTile(4, 0));
            Assert.Equal(3, ThreatManager.GetDistanceToClosestEnemyUnit(CivView, cache, View(centerTile), 5));
        }

        [Fact]
        public void FindDistanceToFartherEnemy()
        {
            var centerTile = testGame.GetTile(0, 0);
            Assert.Equal(2, ThreatManager.GetDistanceToClosestEnemyUnit(CivView, cache, View(centerTile), 2, false));
            // Cache results should say there is not a unit within a distance of 2
            // Therefore the warrior at distance 2 should not be checked
            testGame.AddUnit("Warrior", enemyCiv, testGame.GetTile(2, 0));
            testGame.AddUnit("Warrior", enemyCiv, testGame.GetTile(4, 0));
            Assert.Equal(4, ThreatManager.GetDistanceToClosestEnemyUnit(CivView, cache, View(centerTile), 4, false));
            Assert.Equal(4, ThreatManager.GetDistanceToClosestEnemyUnit(CivView, cache, View(centerTile), 5, false));
        }

        [Fact]
        public void FindDistanceToEnemyWrongCache()
        {
            var centerTile = testGame.GetTile(0, 0);
            testGame.AddUnit("Warrior", enemyCiv, testGame.GetTile(3, 0));
            Assert.Equal(3, ThreatManager.GetDistanceToClosestEnemyUnit(CivView, cache, View(centerTile), 5));
            testGame.GetTile(3, 0).MilitaryUnit!.RemoveFromTile();
            testGame.AddUnit("Warrior", enemyCiv, testGame.GetTile(4, 0));
            testGame.AddUnit("Warrior", enemyCiv, testGame.GetTile(4, 1));
            Assert.Equal(4, ThreatManager.GetDistanceToClosestEnemyUnit(CivView, cache, View(centerTile), 5));
            testGame.GetTile(4, 0).MilitaryUnit!.RemoveFromTile();
            Assert.Equal(4, ThreatManager.GetDistanceToClosestEnemyUnit(CivView, cache, View(centerTile), 5, false));
            testGame.GetTile(4, 1).MilitaryUnit!.RemoveFromTile();
            Assert.Equal(5, ThreatManager.GetDistanceToClosestEnemyUnit(CivView, cache, View(centerTile), 5, false));
        }

        [Fact]
        public void FindDistanceToEnemyCache()
        {
            var centerTile = testGame.GetTile(0, 0);
            testGame.AddUnit("Warrior", enemyCiv, testGame.GetTile(3, 0));
            Assert.Equal(3, ThreatManager.GetDistanceToClosestEnemyUnit(CivView, cache, View(centerTile), 5));
            // An enemy unit should never be spawned closer than we previously searched
            // Therefore our cache results should return 3 instead of the closer unit at a distance of 2
            testGame.AddUnit("Warrior", enemyCiv, testGame.GetTile(2, 0));
            Assert.Equal(3, ThreatManager.GetDistanceToClosestEnemyUnit(CivView, cache, View(centerTile), 5));
        }

        [Fact]
        public void FindTilesWithEnemyUnits()
        {
            var centerTile = testGame.GetTile(0, 0);
            testGame.AddUnit("Warrior", enemyCiv, testGame.GetTile(3, 0));
            testGame.AddUnit("Warrior", enemyCiv, testGame.GetTile(2, 0));
            testGame.AddUnit("Warrior", enemyCiv, testGame.GetTile(4, 0));
            Assert.Equal(3, ThreatManager.GetTilesWithEnemyUnitsInDistance(CivView, cache, View(centerTile), 5).Count);
            Assert.Equal(2, ThreatManager.GetTilesWithEnemyUnitsInDistance(CivView, cache, View(centerTile), 3).Count);
        }

        [Fact]
        public void FindTilesWithEnemyUnitsCache()
        {
            var centerTile = testGame.GetTile(0, 0);
            Assert.Equal(5, ThreatManager.GetDistanceToClosestEnemyUnit(CivView, cache, View(centerTile), 5, false));
            // We have stored in the cach that there is no enemy unit within a distance of 5
            // Therefore adding these units is illegal and should not be returned
            testGame.AddUnit("Warrior", enemyCiv, testGame.GetTile(3, 0));
            testGame.AddUnit("Warrior", enemyCiv, testGame.GetTile(2, 0));
            testGame.AddUnit("Warrior", enemyCiv, testGame.GetTile(4, 0));
            Assert.Empty(ThreatManager.GetTilesWithEnemyUnitsInDistance(CivView, cache, View(centerTile), 5));
            Assert.Empty(ThreatManager.GetTilesWithEnemyUnitsInDistance(CivView, cache, View(centerTile), 3));

            // Now it might be another turn, so it is allowed
            cache = new AutomationCache();
            Assert.Equal(3, ThreatManager.GetTilesWithEnemyUnitsInDistance(CivView, cache, View(centerTile), 5).Count);
            Assert.Equal(2, ThreatManager.GetTilesWithEnemyUnitsInDistance(CivView, cache, View(centerTile), 3).Count);
        }

        [Fact]
        public void FindTilesWithEnemyUnitsAfterExtendingSearch()
        {
            var centerTile = testGame.GetTile(0, 0);
            testGame.AddUnit("Warrior", enemyCiv, testGame.GetTile(2, 0));
            testGame.AddUnit("Warrior", enemyCiv, testGame.GetTile(4, 0));
            Assert.Single(ThreatManager.GetTilesWithEnemyUnitsInDistance(CivView, cache, View(centerTile), 3));
            // Extending the search must still include the cached closer enemy tile
            Assert.Equal(2, ThreatManager.GetTilesWithEnemyUnitsInDistance(CivView, cache, View(centerTile), 5).Count);
        }

        [Fact]
        public void FindDistanceToEnemyAfterFindTilesWithEnemyUnits()
        {
            var centerTile = testGame.GetTile(0, 0);
            testGame.AddUnit("Warrior", enemyCiv, testGame.GetTile(3, 0));
            Assert.Single(ThreatManager.GetTilesWithEnemyUnitsInDistance(CivView, cache, View(centerTile), 5));
            testGame.AddUnit("Warrior", enemyCiv, testGame.GetTile(2, 0));
            Assert.Single(ThreatManager.GetTilesWithEnemyUnitsInDistance(CivView, cache, View(centerTile), 5));
        }

        [Fact]
        public void FindEnemyUnitsOnTiles()
        {
            var centerTile = testGame.GetTile(0, 0);
            testGame.AddUnit("Warrior", enemyCiv, testGame.GetTile(3, 0));
            testGame.AddCity(enemyCiv, testGame.GetTile(3, 0));
            testGame.AddUnit("Bomber", enemyCiv, testGame.GetTile(3, 0));
            testGame.AddUnit("Warrior", enemyCiv, testGame.GetTile(2, 0));
            testGame.AddUnit("Warrior", enemyCiv, testGame.GetTile(4, 0));
            testGame.AddUnit("Warrior", neutralCiv, testGame.GetTile(-3, -3));
            Assert.Equal(4, ThreatManager.GetEnemyUnitsOnTiles(CivView, ThreatManager.GetTilesWithEnemyUnitsInDistance(CivView, cache, View(centerTile), 5)).Count);
            Assert.Equal(3, ThreatManager.GetEnemyUnitsOnTiles(CivView, ThreatManager.GetTilesWithEnemyUnitsInDistance(CivView, cache, View(centerTile), 3)).Count);
            Assert.Empty(ThreatManager.GetEnemyUnitsOnTiles(CivView, ThreatManager.GetTilesWithEnemyUnitsInDistance(CivView, cache, View(centerTile), 1)));
        }

        [Fact]
        public void DangerousTiles()
        {
            var centerTile = testGame.GetTile(0, 0);
            testGame.AddUnit("Warrior", civ, centerTile);
            testGame.AddUnit("Warrior", enemyCiv, testGame.GetTile(3, 0));
            testGame.AddUnit("Archer", enemyCiv, testGame.GetTile(-3, 0));
            var dangerousTiles = ThreatManager.GetDangerousTiles(cache, gameView.GetMapUnitView(centerTile.MilitaryUnit!), 3);
            Assert.Null(testGame.GetTile(3, 0).GetTilesInDistance(1).FirstOrDefault(tile => !dangerousTiles.Contains(View(tile))));
            Assert.Null(testGame.GetTile(-3, 0).GetTilesInDistance(2).FirstOrDefault(tile => !dangerousTiles.Contains(View(tile))));
        }

        // C#-only cases

        [Fact]
        public void EnemyCityCenterCountsAsEnemyEvenWithoutUnitsAndWithoutVisibility()
        {
            testGame.GameInfo.EngineOptions.VisibleMap = false;
            var enemyTile = testGame.GetTile(2, 0);
            testGame.AddCity(enemyCiv, enemyTile);
            enemyTile.SetExplored(civ, true);
            Assert.True(ThreatManager.DoesTileHaveMilitaryEnemy(CivView, View(enemyTile)));
            // an unexplored tile never has a visible enemy, even a city
            var unexplored = testGame.GetTile(-2, 0);
            testGame.AddCity(enemyCiv, unexplored);
            unexplored.SetExplored(civ, false);
            Assert.False(ThreatManager.DoesTileHaveMilitaryEnemy(CivView, View(unexplored)));
        }

        [Fact]
        public void CivilianEnemyUnitIsNoMilitaryEnemy()
        {
            var tile = testGame.GetTile(2, 0);
            testGame.AddUnit("Worker", enemyCiv, tile);
            Assert.False(ThreatManager.DoesTileHaveMilitaryEnemy(CivView, View(tile)));
        }

        [Fact]
        public void NeighboringCitiesAndCivilizationsAndCombinedForce()
        {
            var ourCity = testGame.AddCity(civ, testGame.GetTile(0, 0));
            testGame.AddCity(enemyCiv, testGame.GetTile(4, 0));
            testGame.AddCity(neutralCiv, testGame.GetTile(0, 4));
            testGame.AddCity(civ, testGame.GetTile(-4, 0));
            testGame.AddUnit("Warrior", enemyCiv, testGame.GetTile(5, 0));

            var pairs = ThreatManager.GetNeighboringCitiesOfOtherCivs(CivView).ToList();
            Assert.NotEmpty(pairs);
            Assert.All(pairs, pair => Assert.False(pair.Item2.OwningCiv().Equals(CivView)));
            Assert.Contains(pairs, pair => pair.Item1.Equals(gameView.GetCityView(ourCity)));

            var neighbors = ThreatManager.GetNeighboringCivilizations(CivView);
            Assert.Contains(gameView.GetForeignCivView(enemyCiv), neighbors);
            Assert.Contains(gameView.GetForeignCivView(neutralCiv), neighbors);
            Assert.DoesNotContain(CivView, neighbors);

            Assert.Equal(enemyCiv.GetStatForRanking(Unciv.UI.Screens.VictoryScreen.RankingType.Force), ThreatManager.GetCombinedForceOfWarringCivs(CivView));
        }
    }
}
