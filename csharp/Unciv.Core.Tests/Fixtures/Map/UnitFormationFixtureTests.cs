using Unciv.Core.Tests.Support;
using Unciv.Logic.Battle;
using Unciv.Logic.Civilization;

namespace Unciv.Core.Tests.Fixtures.MapFixtures;

// T141: port of tests/src/com/unciv/logic/map/UnitFormationTests.kt over the real TestGame.
// T146 adds the two Battle.attack cases. Deferred (see T141 Notes): the Tile.startWorkingOnImprovement
// tail of `formation idle units` (T148).
[Collection("RulesetCache")]
public sealed class UnitFormationFixtureTests
{
    private readonly TestGame testGame = new();
    private Civilization civInfo = null!;

    private void SetUp(int size, string baseTerrain = Constants.Desert)
    {
        testGame.MakeHexagonalMap(size, baseTerrain);
        civInfo = testGame.AddCiv();
    }

    [Fact]
    public void BasicFormationFunctionalityCivilian()
    {
        SetUp(1);
        var centerTile = testGame.GetTile(0, 0);
        var civilianUnit = testGame.AddUnit("Worker", civInfo, centerTile);
        var militaryUnit = testGame.AddUnit("Warrior", civInfo, centerTile);
        Assert.True(civilianUnit.GetOtherEscortUnit() != null);
        Assert.False(civilianUnit.IsEscorting());
        civilianUnit.StartEscorting();
        Assert.True(civilianUnit.IsEscorting());
        Assert.True(militaryUnit.IsEscorting());
        Assert.True(civilianUnit.GetOtherEscortUnit() != null);
        civilianUnit.StopEscorting();
        Assert.False(civilianUnit.IsEscorting());
        Assert.False(militaryUnit.IsEscorting());
        Assert.True(civilianUnit.GetOtherEscortUnit() != null);
    }

    [Fact]
    public void BasicFormationFunctionalityMilitary()
    {
        SetUp(1);
        var centerTile = testGame.GetTile(0, 0);
        var civilianUnit = testGame.AddUnit("Worker", civInfo, centerTile);
        var militaryUnit = testGame.AddUnit("Warrior", civInfo, centerTile);
        Assert.True(militaryUnit.GetOtherEscortUnit() != null);
        Assert.False(militaryUnit.IsEscorting());
        militaryUnit.StartEscorting();
        Assert.True(militaryUnit.IsEscorting());
        Assert.True(civilianUnit.IsEscorting());
        Assert.True(militaryUnit.GetOtherEscortUnit() != null);
        militaryUnit.StopEscorting();
        Assert.False(militaryUnit.IsEscorting());
        Assert.False(civilianUnit.IsEscorting());
        Assert.True(militaryUnit.GetOtherEscortUnit() != null);
    }

    [Fact]
    public void BasicFormationNotAvailableFunctionality()
    {
        SetUp(1);
        var centerTile = testGame.GetTile(0, 0);
        var civilianUnit = testGame.AddUnit("Worker", civInfo, centerTile);
        Assert.False(civilianUnit.GetOtherEscortUnit() != null);
        Assert.False(civilianUnit.IsEscorting());
        civilianUnit.StartEscorting();
        Assert.False(civilianUnit.IsEscorting());
        civilianUnit.Destroy();
        var militaryUnit = testGame.AddUnit("Warrior", civInfo, centerTile);
        Assert.False(militaryUnit.GetOtherEscortUnit() != null);
        Assert.False(militaryUnit.IsEscorting());
        militaryUnit.StartEscorting();
        Assert.False(militaryUnit.IsEscorting());
    }

    [Fact]
    public void FormationIdleUnits()
    {
        SetUp(1);
        var centerTile = testGame.GetTile(0, 0);
        var civilianUnit = testGame.AddUnit("Worker", civInfo, centerTile);
        var militaryUnit = testGame.AddUnit("Warrior", civInfo, centerTile);
        civilianUnit.StartEscorting();
        Assert.True(civilianUnit.IsIdle());
        Assert.True(militaryUnit.IsIdle());
        civilianUnit.CurrentMovement = 0f;
        Assert.False(civilianUnit.IsIdle());
        Assert.False(militaryUnit.IsIdle());
    }

    [Fact]
    public void FormationMovement()
    {
        SetUp(3);
        var centerTile = testGame.GetTile(0, 0);
        var civilianUnit = testGame.AddUnit("Worker", civInfo, centerTile);
        var militaryUnit = testGame.AddUnit("Warrior", civInfo, centerTile);
        civilianUnit.StartEscorting();
        var targetTile = testGame.GetTile(0, 2);
        civilianUnit.Movement.MoveToTile(targetTile);
        Assert.True(civilianUnit.GetTile() == targetTile);
        Assert.True(militaryUnit.GetTile() == targetTile);
        Assert.True(civilianUnit.IsEscorting());
        Assert.True(militaryUnit.IsEscorting());
    }

    [Fact]
    public void StopFormationMovement()
    {
        SetUp(3);
        var centerTile = testGame.GetTile(0, 0);
        var civilianUnit = testGame.AddUnit("Worker", civInfo, centerTile);
        var militaryUnit = testGame.AddUnit("Warrior", civInfo, centerTile);
        civilianUnit.StartEscorting();
        civilianUnit.StopEscorting();
        var targetTile = testGame.GetTile(0, 2);
        civilianUnit.Movement.MoveToTile(targetTile);
        Assert.True(civilianUnit.GetTile() == targetTile);
        Assert.True(militaryUnit.GetTile() == centerTile);
        Assert.False(civilianUnit.IsEscorting());
        Assert.False(militaryUnit.IsEscorting());
    }

    [Fact]
    public void FormationCanMoveTo()
    {
        SetUp(3);
        var centerTile = testGame.GetTile(0, 0);
        var civilianUnit = testGame.AddUnit("Worker", civInfo, centerTile);
        var militaryUnit = testGame.AddUnit("Warrior", civInfo, centerTile);
        var targetTile = testGame.GetTile(0, 2);
        testGame.AddUnit("Worker", civInfo, targetTile); // blocking civilian
        Assert.False(civilianUnit.Movement.CanMoveTo(targetTile));
        Assert.True(militaryUnit.Movement.CanMoveTo(targetTile));
        civilianUnit.StartEscorting();
        Assert.False(militaryUnit.Movement.CanMoveTo(targetTile));
    }

    [Fact]
    public void FormationCanMoveToWater()
    {
        SetUp(3, "Ocean");
        var centerTile = testGame.GetTile(0, 0);
        centerTile.BaseTerrain = "Coast";
        centerTile.IsWater = true;
        centerTile.IsLand = false;
        civInfo.Tech.EmbarkedUnitsCanEnterOcean = true;
        civInfo.Tech.AddTechnology("Astronomy");
        var civilianUnit = testGame.AddUnit("Work Boats", civInfo, centerTile); // Can enter ocean
        var militaryUnit = testGame.AddUnit("Trireme", civInfo, centerTile); // Can't enter ocean
        var targetTile = testGame.GetTile(0, 1);
        targetTile.IsWater = true;
        targetTile.IsLand = false;
        targetTile.IsOcean = true;
        Assert.False(militaryUnit.Movement.CanMoveTo(targetTile));
        Assert.True(civilianUnit.Movement.CanMoveTo(targetTile));
        civilianUnit.StartEscorting();
        Assert.False(civilianUnit.Movement.CanMoveTo(targetTile));
    }

    [Fact]
    public void FormationHeadTowardsWithFasterUnits()
    {
        SetUp(5);
        var centerTile = testGame.GetTile(0, 0);
        var civilianUnit = testGame.AddUnit("Worker", civInfo, centerTile);
        var militaryUnit = testGame.AddUnit("Horseman", civInfo, centerTile); // 4 movement
        civilianUnit.StartEscorting();
        var targetTile = testGame.GetTile(0, 4);
        var expectedTile = testGame.GetTile(0, 2);
        militaryUnit.Movement.HeadTowards(targetTile);
        Assert.True(civilianUnit.GetTile() == expectedTile);
        Assert.True(militaryUnit.GetTile() == expectedTile);
        Assert.True(civilianUnit.IsEscorting());
        Assert.True(militaryUnit.IsEscorting());
        Assert.True(militaryUnit.CurrentMovement == 2f);
        Assert.False(militaryUnit.IsIdle(), "The unit should not be idle if it's escort has no movement points");
    }

    [Fact]
    public void GetDistanceToTilesWhenInFormation()
    {
        SetUp(5);
        var centerTile = testGame.GetTile(0, 0);
        var civilianUnit = testGame.AddUnit("Worker", civInfo, centerTile);
        var militaryUnit = testGame.AddUnit("Horseman", civInfo, centerTile); // 4 movement
        civilianUnit.StartEscorting();
        var civilianDistanceToTiles = civilianUnit.Movement.GetDistanceToTiles();
        Assert.All(militaryUnit.Movement.GetDistanceToTiles().Keys, it => Assert.True(civilianDistanceToTiles.ContainsKey(it)));

        // Test again with caching
        civilianUnit.StopEscorting();
        militaryUnit.Movement.GetDistanceToTiles();
        civilianUnit.Movement.GetDistanceToTiles();
        civilianUnit.StartEscorting();
        civilianDistanceToTiles = civilianUnit.Movement.GetDistanceToTiles();
        Assert.All(militaryUnit.Movement.GetDistanceToTiles().Keys, it => Assert.True(civilianDistanceToTiles.ContainsKey(it)));
    }

    [Fact]
    public void TestEscortAttackAndMoveCivilianUnit()
    {
        SetUp(3);
        var enemyCiv = testGame.AddCiv();
        civInfo.DiplomacyFunctions.MakeCivilizationsMeet(enemyCiv);
        civInfo.GetDiplomacyManager(enemyCiv)!.DeclareWar();
        var centerTile = testGame.GetTile(0, 0);
        var enemyTile = testGame.GetTile(2, 2);
        var scout = testGame.AddUnit("Warrior", civInfo, centerTile);
        var civilianUnit = testGame.AddUnit("Worker", civInfo, centerTile);
        var enemyUnit = testGame.AddUnit("Warrior", enemyCiv, enemyTile);
        enemyUnit.Health = 1; // Needs to be killable by the scout
        scout.StartEscorting();
        Assert.True(scout.IsEscorting());
        Assert.True(civilianUnit.IsEscorting());

        Assert.Single(TargetHelper.GetAttackableEnemies(scout, scout.Movement.GetDistanceToTiles()));
        Battle.Attack(new MapUnitCombatant(scout), new MapUnitCombatant(enemyUnit));
        Assert.Equal(0, enemyUnit.Health);
        Assert.Equal(enemyTile, scout.GetTile());
        Assert.Equal(enemyTile, civilianUnit.GetTile());
        Assert.True(scout.IsEscorting());
        Assert.True(civilianUnit.IsEscorting());
    }

    [Fact]
    public void TestEscortAttackWithRangedUnit()
    {
        SetUp(3);
        var enemyCiv = testGame.AddCiv();
        testGame.GameInfo.EngineOptions.VisibleMap = true;
        civInfo.DiplomacyFunctions.MakeCivilizationsMeet(enemyCiv);
        civInfo.GetDiplomacyManager(enemyCiv)!.DeclareWar();
        var centerTile = testGame.GetTile(0, 0);
        var enemyTile = testGame.GetTile(3, 3);
        var archer = testGame.AddUnit("Archer", civInfo, centerTile);
        var civilianUnit = testGame.AddUnit("Worker", civInfo, centerTile);
        var enemyUnit = testGame.AddUnit("Warrior", enemyCiv, enemyTile);
        enemyUnit.Health = 1; // Needs to be killable by the scout
        archer.StartEscorting();
        Assert.True(archer.IsEscorting());
        Assert.True(civilianUnit.IsEscorting());

        Assert.Single(TargetHelper.GetAttackableEnemies(archer, archer.Movement.GetDistanceToTiles()));
        Battle.Attack(new MapUnitCombatant(archer), new MapUnitCombatant(enemyUnit));
        Assert.Equal(0, enemyUnit.Health);
        Assert.True(archer.IsEscorting());
        Assert.True(civilianUnit.IsEscorting());
        Assert.Equal(archer.GetTile(), civilianUnit.GetTile());
    }

    [Fact]
    public void TestEscortAttackWithMilitaryUnitHavingIgnoreTerrainCost()
    {
        SetUp(3);
        var enemyCiv = testGame.AddCiv();
        civInfo.DiplomacyFunctions.MakeCivilizationsMeet(enemyCiv);
        civInfo.GetDiplomacyManager(enemyCiv)!.DeclareWar();
        var centerTile = testGame.GetTile(0, 0);
        var forestTile = testGame.GetTile(1, 1);
        var enemyTile = testGame.GetTile(2, 2);
        var scout = testGame.AddUnit("Warrior", civInfo, centerTile);
        var civilianUnit = testGame.AddUnit("Worker", civInfo, centerTile);
        var enemyUnit = testGame.AddUnit("Warrior", enemyCiv, enemyTile);
        enemyUnit.Health = 1; // Needs to be killable by the scout
        forestTile.AddTerrainFeature("Forest");
        scout.StartEscorting();
        Assert.True(scout.IsEscorting());
        Assert.True(civilianUnit.IsEscorting());
        Assert.Empty(TargetHelper.GetAttackableEnemies(scout, scout.Movement.GetDistanceToTiles()));
    }

    [Fact]
    public void TestEscortPathWithHillsOneTurnCivilian()
    {
        SetUp(3);
        var centerTile = testGame.GetTile(0, 0);
        var hillTile = testGame.GetTile(1, 1);
        var destinationTile = testGame.GetTile(1, 2);
        var militaryUnit = testGame.AddUnit("Mechanized Infantry", civInfo, centerTile);
        var civilianUnit = testGame.AddUnit("Worker", civInfo, centerTile);
        hillTile.AddTerrainFeature("Hill");
        destinationTile.AddTerrainFeature("Hill");
        civilianUnit.StartEscorting();
        civilianUnit.Movement.MoveToTile(destinationTile);
        Assert.Equal(destinationTile, civilianUnit.GetTile());
        Assert.Equal(destinationTile, militaryUnit.GetTile());
    }

    [Fact]
    public void TestEscortPathWithHillsOneTurnMilitary()
    {
        SetUp(3);
        var centerTile = testGame.GetTile(0, 0);
        var hillTile = testGame.GetTile(1, 1);
        var destinationTile = testGame.GetTile(1, 2);
        var militaryUnit = testGame.AddUnit("Mechanized Infantry", civInfo, centerTile);
        var civilianUnit = testGame.AddUnit("Worker", civInfo, centerTile);
        hillTile.AddTerrainFeature("Hill");
        destinationTile.AddTerrainFeature("Hill");
        militaryUnit.StartEscorting();
        militaryUnit.Movement.MoveToTile(destinationTile);
        Assert.Equal(destinationTile, civilianUnit.GetTile());
        Assert.Equal(destinationTile, militaryUnit.GetTile());
    }

    [Fact]
    public void TestEscortWithIgnoreTerrainCostUnit()
    {
        SetUp(5);
        var centerTile = testGame.GetTile(0, 0);
        var marsh = testGame.GetTile(1, 1);
        marsh.AddTerrainFeature("Marsh");
        var jungle = testGame.GetTile(2, 2);
        jungle.AddTerrainFeature("Jungle");
        testGame.GetTile(3, 3).AddTerrainFeature("Hill");
        testGame.GetTile(3, 4).AddTerrainFeature("Hill");
        var destinationTile = testGame.GetTile(4, 5);
        var militaryUnit = testGame.AddUnit("Scout", civInfo, centerTile);
        testGame.AddUnit("Worker", civInfo, centerTile);
        militaryUnit.StartEscorting();
        var shortestPath = militaryUnit.Movement.GetShortestPath(destinationTile);
        Assert.Equal(3, shortestPath.Count);
        Assert.DoesNotContain(jungle, shortestPath);
        Assert.DoesNotContain(marsh, shortestPath);
    }
}
