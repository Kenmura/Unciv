using Unciv.Core.Tests.Support;
using Unciv.Logic.Automation.Unit;
using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Logic.Map;
using Unciv.Logic.Map.Tiles;
using Unciv.Models.Ruleset.Nation;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Core.Tests.Fixtures.MapFixtures;

// T145 / T093: port of tests/src/com/unciv/logic/map/PathfindingTests.kt over the real TestGame, all 58 Kotlin cases, for both parameter sets:
// PathfindingFixtureTests (ClassicPathfinding) and PathfindingAStarFixtureTests (AStarPathfinding, GameClientState.UseAStarPathfinding).
// Kotlin `assumeTrue(...)` / `assumeThat(...)` skips a case for the other parameter set; xUnit 2 has no dynamic skip, so the case returns at once.
public abstract class PathfindingFixtureTestsBase
{
    private readonly TestGame testGame = new();
    private readonly Civilization civInfo;
    private TileClass originTile;
    private readonly Civilization barbarianCiv;
    private readonly Civilization neutralCiv;

    private readonly bool useAStar;

    protected PathfindingFixtureTestsBase(bool useAStar)
    {
        this.useAStar = useAStar;
        testGame.GameInfo.ClientState.UseAStarPathfinding = useAStar;
        testGame.MakeHexagonalMap(8);
        originTile = testGame.TileMap[0, 0];
        civInfo = testGame.AddCiv();
        foreach (var tile in testGame.TileMap.Values)
            tile.SetExplored(civInfo, true);

        var barbarianNation = new Nation { Name = Constants.Barbarians }; // they are always enemies
        var neutralNation = new Nation { Name = "Huns" };
        barbarianCiv = new Civilization(barbarianNation);
        neutralCiv = new Civilization(neutralNation);
        testGame.AddCiv(barbarianNation);
        testGame.AddCiv(neutralNation);
        barbarianCiv.GameInfo = testGame.GameInfo;
        neutralCiv.GameInfo = testGame.GameInfo;
        barbarianCiv.Cache.UpdateState();
        neutralCiv.Cache.UpdateState();
        barbarianCiv.SetTransients();
        neutralCiv.SetTransients();
    }

    private void VerticalWall(int x, Action<TileClass> apply)
    {
        foreach (var tile in testGame.TileMap.TileList)
            if (tile.Position.X == x)
                apply(tile);
    }

    private static void Set(TileClass tile, string terrain)
    {
        tile.BaseTerrain = terrain;
        tile.SetTransients();
    }

    [Fact]
    public void ShortestPathByTurnsNotSumOfMovements()
    {
        // Naive Dijkstra would calculate distance to 0,3 to be 5 movement points through hills, and only 4 by going around hills.
        // However, from a *unit turn* perspective, going through the hills is 3 turns, and going around is 4, so through the hills is the correct solution
        testGame.SetTileTerrain(new HexCoord(0, 1), "Hill");
        testGame.SetTileTerrain(new HexCoord(0, 2), "Hill");
        var baseUnit = testGame.CreateBaseUnit();
        baseUnit.Movement = 1;

        var unit = testGame.AddUnit(baseUnit.Name, civInfo, originTile);
        // expect movement through hills (2 hill tiles plus one default desert)
        Assert.Equal(3, unit.Movement.GetShortestPath(testGame.GetTile(new HexCoord(0, 3))).Count);
    }

    [Fact]
    public void MaximizeRemainingMovementWhenReachingDestination()
    {
        // Moving in a direct path, you'd waste 5 movement points to get there, ending up with 0.
        // Moving around the hills, you'd waste 4 movement points, leaving you with one remaining
        testGame.SetTileTerrain(new HexCoord(0, 1), "Hill");
        testGame.SetTileTerrain(new HexCoord(0, 2), "Hill");
        var baseUnit = testGame.CreateBaseUnit();
        baseUnit.Movement = 5;

        var unit = testGame.AddUnit(baseUnit.Name, civInfo, originTile);
        unit.Movement.MoveToTile(testGame.GetTile(new HexCoord(0, 3)));
        Assert.Equal(1f, unit.CurrentMovement);
    }

    [Fact]
    public void GetShortestPathReturnsOnlyEndOfTurns()
    {
        testGame.SetTileTerrain(new HexCoord(0, 1), "Hill");
        testGame.SetTileTerrain(new HexCoord(0, 2), "Hill");
        var baseUnit = testGame.CreateBaseUnit();
        baseUnit.Movement = 3;
        var unit = testGame.AddUnit(baseUnit.Name, civInfo, originTile);
        var target = testGame.GetTile(new HexCoord(0, 3));

        var path = unit.Movement.GetShortestPath(target);

        Assert.True(path.Count == 2, string.Join(",", path));
        Assert.Same(target, path[1]);
    }

    [Fact]
    public void WhenThereIsNoRouteThenReturnNull()
    {
        // 2,0 is surrounded by Mountains
        foreach (var (x, y) in new[] { (1, 0), (1, 1), (2, 1), (3, 1), (3, 0), (3, -1), (2, -1), (1, -1) })
            testGame.SetTileTerrain(new HexCoord(x, y), "Mountain");
        var baseUnit = testGame.CreateBaseUnit();
        baseUnit.Movement = 1;
        var unit = testGame.AddUnit(baseUnit.Name, civInfo, originTile);

        var target = testGame.GetTile(new HexCoord(2, 0));
        var path = unit.Movement.GetShortestPath(target);

        Assert.Empty(path);
    }

    [Fact]
    public void GetMovementToTilesAtPosition_mountains_movesTo()
    {
        if (useAStar) return; // AStar deliberately does not include mountains
        VerticalWall(1, tile => Set(tile, "Mountain"));
        var unit = testGame.AddUnit("Warrior", civInfo, originTile);
        var paths = unit.Movement.GetMovementToTilesAtPosition(originTile.Position, 2f);
        Assert.True(paths.ContainsKey(testGame.TileMap[1, 0]), "getMovementToTilesAtPosition should move to mountains");
        Assert.False(paths.ContainsKey(testGame.TileMap[2, 0]), "getMovementToTilesAtPosition should not pass through mountains");
    }

    [Fact]
    public void GetMovementToTilesAtPosition_ocean_movesTo()
    {
        if (useAStar) return; // AStar deliberately does not include ocean (that we can't move into)
        VerticalWall(1, tile => Set(tile, "Ocean"));
        var unit = testGame.AddUnit("Warrior", civInfo, originTile);
        var paths = unit.Movement.GetMovementToTilesAtPosition(originTile.Position, 2f);
        Assert.True(paths.ContainsKey(testGame.TileMap[1, 0]), "getMovementToTilesAtPosition should move to ocean");
        Assert.False(paths.ContainsKey(testGame.TileMap[2, 0]), "getMovementToTilesAtPosition should not pass through ocean");
    }

    [Fact]
    public void GetMovementToTilesAtPosition_military_alliedCivilians_pathsThrough()
    {
        VerticalWall(1, tile => testGame.AddUnit("Worker", civInfo, tile));
        var unit = testGame.AddUnit("Warrior", civInfo, originTile);
        var paths = unit.Movement.GetMovementToTilesAtPosition(originTile.Position, 2f);
        Assert.True(paths.ContainsKey(testGame.TileMap[2, 0]), "getMovementToTilesAtPosition should path through allied civilians");
    }

    [Fact]
    public void GetShortestPath_military_alliedCivilianAtEndOfTurn_doesEndTurnOnCivilian()
    {
        VerticalWall(2, tile => testGame.AddUnit("Worker", civInfo, tile));
        testGame.AddUnit("Warrior", civInfo, testGame.TileMap[1, 1]);
        var unit = testGame.AddUnit("Warrior", civInfo, originTile);
        var paths = unit.Movement.GetShortestPath(testGame.TileMap[4, 4]);
        Assert.Equal(new List<TileClass> { testGame.TileMap[2, 2], testGame.TileMap[4, 4] }, paths);
    }

    [Fact]
    public void GetMovementToTilesAtPosition_military_neutralCivilians_doesNotPathThrough()
    {
        VerticalWall(1, tile => testGame.AddUnit("Worker", neutralCiv, tile));
        var unit = testGame.AddUnit("Warrior", civInfo, originTile);
        var paths = unit.Movement.GetMovementToTilesAtPosition(originTile.Position, 2f);
        Assert.True(paths.ContainsKey(testGame.TileMap[1, 0]), "getMovementToTilesAtPosition should move to neutral civilians");
        Assert.True(paths.ContainsKey(testGame.TileMap[2, 0]), "Classic getMovementToTilesAtPosition passes through neutral civilians");
    }

    [Fact]
    public void GetShortestPath_military_neutralCivilianAtEndOfTurn_doesNotEndTurnOnCivilian()
    {
        VerticalWall(2, tile => testGame.AddUnit("Worker", neutralCiv, tile));
        testGame.AddUnit("Warrior", civInfo, testGame.TileMap[1, 1]);
        var unit = testGame.AddUnit("Warrior", civInfo, originTile);
        var paths = unit.Movement.GetShortestPath(testGame.TileMap[4, 4]);
        Assert.Equal(new[] { 1, 3, 4 }, paths.Select(it => it.Position.X).ToArray());
    }

    [Fact]
    public void GetMovementToTilesAtPosition_military_enemyCivilians_canMoveTo()
    {
        VerticalWall(1, tile => testGame.AddUnit("Worker", barbarianCiv, tile));
        var unit = testGame.AddUnit("Warrior", civInfo, originTile);
        var paths = unit.Movement.GetMovementToTilesAtPosition(originTile.Position, 2f);
        Assert.True(paths.ContainsKey(testGame.TileMap[1, 0]), "getMovementToTilesAtPosition should move to enemy civilians");
        Assert.True(paths.ContainsKey(testGame.TileMap[2, 0]), "Classic getMovementToTilesAtPosition passes through enemy civilians");
    }

    [Fact]
    public void GetShortestPath_military_enemyCivilianAtEndOfTurn_doesEndTurnOnCivilian()
    {
        VerticalWall(2, tile => testGame.AddUnit("Worker", barbarianCiv, tile));
        testGame.AddUnit("Warrior", civInfo, testGame.TileMap[1, 1]);
        var unit = testGame.AddUnit("Warrior", civInfo, originTile);
        var paths = unit.Movement.GetShortestPath(testGame.TileMap[4, 4]);
        Assert.Equal(new List<TileClass> { testGame.TileMap[2, 2], testGame.TileMap[4, 4] }, paths);
    }

    [Fact]
    public void GetMovementToTilesAtPosition_military_alliedMilitary_pathsThrough()
    {
        VerticalWall(1, tile => testGame.AddUnit("Warrior", civInfo, tile));
        var unit = testGame.AddUnit("Warrior", civInfo, originTile);
        var paths = unit.Movement.GetMovementToTilesAtPosition(originTile.Position, 2f);
        Assert.True(paths.ContainsKey(testGame.TileMap[2, 0]), "getMovementToTilesAtPosition should path through allied military");
    }

    [Fact]
    public void GetShortestPath_military_alliedMilitaryAtEndOfTurn_doesNotEndTurnOnMilitary()
    {
        VerticalWall(2, tile => testGame.AddUnit("Warrior", civInfo, tile));
        testGame.AddUnit("Warrior", civInfo, testGame.TileMap[1, 1]);
        var unit = testGame.AddUnit("Warrior", civInfo, originTile);
        var paths = unit.Movement.GetShortestPath(testGame.TileMap[4, 4]);
        Assert.Equal(new[] { 1, 3, 4 }, paths.Select(it => it.Position.X).ToArray());
    }

    [Fact]
    public void GetMovementToTilesAtPosition_military_neutralMilitary_doesNotPathThrough()
    {
        VerticalWall(1, tile => testGame.AddUnit("Warrior", neutralCiv, tile));
        var unit = testGame.AddUnit("Warrior", civInfo, originTile);
        var paths = unit.Movement.GetMovementToTilesAtPosition(originTile.Position, 2f);
        Assert.True(paths.ContainsKey(testGame.TileMap[1, 0]), "getMovementToTilesAtPosition should move to neutral military");
        Assert.True(paths.ContainsKey(testGame.TileMap[2, 0]), "Classic getMovementToTilesAtPosition passes through neutral military");
    }

    [Fact]
    public void GetShortestPath_military_neutralMilitaryAtEndOfTurn_doesNotEndTurnOnMilitary()
    {
        VerticalWall(2, tile => testGame.AddUnit("Warrior", neutralCiv, tile));
        testGame.AddUnit("Warrior", civInfo, testGame.TileMap[1, 1]);
        var unit = testGame.AddUnit("Warrior", civInfo, originTile);
        var paths = unit.Movement.GetShortestPath(testGame.TileMap[4, 4]);
        Assert.Equal(new[] { 1, 3, 4 }, paths.Select(it => it.Position.X).ToArray());
    }

    [Fact]
    public void GetMovementToTilesAtPosition_military_enemyMilitary_canMoveTo()
    {
        VerticalWall(1, tile => testGame.AddUnit("Warrior", barbarianCiv, tile));
        var unit = testGame.AddUnit("Warrior", civInfo, originTile);
        var paths = unit.Movement.GetMovementToTilesAtPosition(originTile.Position, 2f);
        Assert.True(paths.ContainsKey(testGame.TileMap[1, 0]), "getMovementToTilesAtPosition should move to enemy military");
        Assert.False(paths.ContainsKey(testGame.TileMap[2, 0]), "getMovementToTilesAtPosition should not pass through enemy military");
    }

    [Fact]
    public void GetShortestPath_military_enemyMilitaryAtEndOfTurn_cannotPathThrough()
    {
        VerticalWall(2, tile => testGame.AddUnit("Warrior", barbarianCiv, tile));
        testGame.AddUnit("Warrior", civInfo, testGame.TileMap[1, 1]);
        var unit = testGame.AddUnit("Warrior", civInfo, originTile);
        var paths = unit.Movement.GetShortestPath(testGame.TileMap[4, 4]);
        Assert.Empty(paths);
    }

    [Fact]
    public void GetMovementToTilesAtPosition_military_alliedCity_pathsThrough()
    {
        VerticalWall(2, tile => testGame.AddCity(civInfo, tile));
        var unit = testGame.AddUnit("Warrior", civInfo, originTile);
        var paths = unit.Movement.GetMovementToTilesAtPosition(originTile.Position, 2f);
        Assert.True(paths.ContainsKey(testGame.TileMap[1, 0]), "getMovementToTilesAtPosition should path through allied tiles");
        Assert.True(paths.ContainsKey(testGame.TileMap[2, 0]), "getMovementToTilesAtPosition should path through allied cities");
    }

    [Fact]
    public void GetShortestPath_military_alliedCityAtEndOfTurn_doesEndTurnOnCity()
    {
        VerticalWall(2, tile => testGame.AddCity(civInfo, tile));
        testGame.AddUnit("Warrior", civInfo, testGame.TileMap[1, 1]);
        var unit = testGame.AddUnit("Warrior", civInfo, originTile);
        var paths = unit.Movement.GetShortestPath(testGame.TileMap[4, 4]);
        Assert.Equal(new List<TileClass> { testGame.TileMap[2, 2], testGame.TileMap[4, 4] }, paths);
    }

    [Fact]
    public void GetMovementToTilesAtPosition_military_neutralCity_doesNotPathThrough()
    {
        VerticalWall(2, tile => testGame.AddCity(neutralCiv, tile));
        var unit = testGame.AddUnit("Warrior", civInfo, originTile);
        var paths = unit.Movement.GetMovementToTilesAtPosition(originTile.Position, 2f);
        Assert.True(paths.ContainsKey(testGame.TileMap[1, 0]), "getMovementToTilesAtPosition should path through neutral tiles");
        Assert.False(paths.ContainsKey(testGame.TileMap[2, 0]), "getMovementToTilesAtPosition should not move to neutral cities");
    }

    [Fact]
    public void GetShortestPath_military_neutralCityAtEndOfTurn_cannotPathThrough()
    {
        VerticalWall(2, tile => testGame.AddCity(neutralCiv, tile));
        var unit = testGame.AddUnit("Warrior", civInfo, originTile);
        var paths = unit.Movement.GetShortestPath(testGame.TileMap[4, 4]);
        Assert.Empty(paths);
    }

    [Fact]
    public void GetMovementToTilesAtPosition_military_enemyCity_canMoveTo()
    {
        VerticalWall(2, tile => testGame.AddCity(barbarianCiv, tile));
        var unit = testGame.AddUnit("Warrior", civInfo, originTile);
        var paths = unit.Movement.GetMovementToTilesAtPosition(originTile.Position, 2f);
        Assert.True(paths.ContainsKey(testGame.TileMap[1, 0]), "getMovementToTilesAtPosition should path through enemy tiles");
        Assert.True(paths.ContainsKey(testGame.TileMap[2, 0]), "getMovementToTilesAtPosition should move to enemy cities");
    }

    [Fact]
    public void GetShortestPath_military_enemyCityAtEndOfTurn_cannotPathThrough()
    {
        VerticalWall(2, tile => testGame.AddCity(barbarianCiv, tile));
        testGame.AddUnit("Warrior", civInfo, testGame.TileMap[1, 1]);
        var unit = testGame.AddUnit("Warrior", civInfo, originTile);
        var paths = unit.Movement.GetShortestPath(testGame.TileMap[4, 4]);
        Assert.Empty(paths);
    }

    [Fact]
    public void GetMovementToTilesAtPosition_civilian_alliedCivilians_pathsThrough()
    {
        VerticalWall(1, tile => testGame.AddUnit("Worker", civInfo, tile));
        var unit = testGame.AddUnit("Worker", civInfo, originTile);
        var paths = unit.Movement.GetMovementToTilesAtPosition(originTile.Position, 2f);
        Assert.True(paths.ContainsKey(testGame.TileMap[2, 0]), "getMovementToTilesAtPosition should path through allied civilians");
    }

    [Fact]
    public void GetShortestPath_civilian_alliedCivilivanAtEndOfTurn_doesNotEndTurnOnCivilian()
    {
        VerticalWall(2, tile => testGame.AddUnit("Worker", civInfo, tile));
        testGame.AddUnit("Worker", civInfo, testGame.TileMap[1, 1]);
        var unit = testGame.AddUnit("Worker", civInfo, originTile);
        var paths = unit.Movement.GetShortestPath(testGame.TileMap[4, 4]);
        Assert.Equal(new[] { 1, 3, 4 }, paths.Select(it => it.Position.X).ToArray());
    }

    [Fact]
    public void GetMovementToTilesAtPosition_civilian_neutralCivilians_doesNotPathThrough()
    {
        VerticalWall(1, tile => testGame.AddUnit("Worker", neutralCiv, tile));
        var unit = testGame.AddUnit("Worker", civInfo, originTile);
        var paths = unit.Movement.GetMovementToTilesAtPosition(originTile.Position, 2f);
        Assert.True(paths.ContainsKey(testGame.TileMap[1, 0]), "getMovementToTilesAtPosition should move to neutral civilians");
        Assert.True(paths.ContainsKey(testGame.TileMap[2, 0]), "Classic getMovementToTilesAtPosition passes through neutral civilians");
    }

    [Fact]
    public void GetShortestPath_civilian_neutralCivilivanAtEndOfTurn_doesNotEndTurnOnCivilian()
    {
        VerticalWall(2, tile => testGame.AddUnit("Worker", neutralCiv, tile));
        testGame.AddUnit("Worker", civInfo, testGame.TileMap[1, 1]);
        var unit = testGame.AddUnit("Worker", civInfo, originTile);
        var paths = unit.Movement.GetShortestPath(testGame.TileMap[4, 4]);
        Assert.Equal(new[] { 1, 3, 4 }, paths.Select(it => it.Position.X).ToArray());
    }

    [Fact]
    public void GetMovementToTilesAtPosition_civilian_enemyCivilians_canMoveTo()
    {
        VerticalWall(1, tile => testGame.AddUnit("Worker", barbarianCiv, tile));
        var unit = testGame.AddUnit("Worker", civInfo, originTile);
        var paths = unit.Movement.GetMovementToTilesAtPosition(originTile.Position, 2f);
        Assert.True(paths.ContainsKey(testGame.TileMap[1, 0]), "getMovementToTilesAtPosition should move to enemy civilians");
        Assert.True(paths.ContainsKey(testGame.TileMap[2, 0]), "Classic getMovementToTilesAtPosition passes through enemy civilians");
    }

    [Fact]
    public void GetShortestPath_civilian_enemyCivilivanAtEndOfTurn_doesNotEndTurnOnCivilian()
    {
        VerticalWall(2, tile => testGame.AddUnit("Worker", barbarianCiv, tile));
        testGame.AddUnit("Worker", civInfo, testGame.TileMap[1, 1]);
        var unit = testGame.AddUnit("Worker", civInfo, originTile);
        var paths = unit.Movement.GetShortestPath(testGame.TileMap[4, 4]);
        Assert.Equal(new[] { 1, 3, 4 }, paths.Select(it => it.Position.X).ToArray());
    }

    [Fact]
    public void GetMovementToTilesAtPosition_civilian_alliedMilitary_pathsThrough()
    {
        VerticalWall(1, tile => testGame.AddUnit("Worker", civInfo, tile));
        var unit = testGame.AddUnit("Worker", civInfo, originTile);
        var paths = unit.Movement.GetMovementToTilesAtPosition(originTile.Position, 2f);
        Assert.True(paths.ContainsKey(testGame.TileMap[2, 0]), "getMovementToTilesAtPosition should path through allied military");
    }

    [Fact]
    public void GetShortestPath_civilian_alliedMilitaryAtEndOfTurn_doesEndTurnOnCivilian()
    {
        VerticalWall(2, tile => testGame.AddUnit("Warrior", civInfo, tile));
        testGame.AddUnit("Worker", civInfo, testGame.TileMap[1, 1]);
        var unit = testGame.AddUnit("Worker", civInfo, originTile);
        var paths = unit.Movement.GetShortestPath(testGame.TileMap[4, 4]);
        Assert.Equal(new List<TileClass> { testGame.TileMap[2, 2], testGame.TileMap[4, 4] }, paths);
    }

    [Fact]
    public void GetMovementToTilesAtPosition_civilian_neutralMilitary_doesNotPathThrough()
    {
        VerticalWall(1, tile => testGame.AddUnit("Warrior", neutralCiv, tile));
        var unit = testGame.AddUnit("Worker", civInfo, originTile);
        var paths = unit.Movement.GetMovementToTilesAtPosition(originTile.Position, 2f);
        Assert.True(paths.ContainsKey(testGame.TileMap[1, 0]), "getMovementToTilesAtPosition should move to neutral military");
        Assert.True(paths.ContainsKey(testGame.TileMap[2, 0]), "Classic getMovementToTilesAtPosition passes through neutral military");
    }

    [Fact]
    public void GetShortestPath_civilian_neutralMilitaryAtEndOfTurn_doesNotEndTurnOnCivilian()
    {
        VerticalWall(2, tile => testGame.AddUnit("Warrior", neutralCiv, tile));
        testGame.AddUnit("Worker", civInfo, testGame.TileMap[1, 1]);
        var unit = testGame.AddUnit("Worker", civInfo, originTile);
        var paths = unit.Movement.GetShortestPath(testGame.TileMap[4, 4]);
        Assert.Equal(new[] { 1, 3, 4 }, paths.Select(it => it.Position.X).ToArray());
    }

    [Fact]
    public void GetMovementToTilesAtPosition_civlian_enemyMilitary_canMoveTo()
    {
        VerticalWall(1, tile => testGame.AddUnit("Warrior", neutralCiv, tile));
        var unit = testGame.AddUnit("Worker", civInfo, originTile);
        var paths = unit.Movement.GetMovementToTilesAtPosition(originTile.Position, 2f);
        Assert.True(paths.ContainsKey(testGame.TileMap[1, 0]), "getMovementToTilesAtPosition should move to enemy military");
        Assert.True(paths.ContainsKey(testGame.TileMap[2, 0]), "Classic getMovementToTilesAtPosition passes through enemy military");
    }

    [Fact]
    public void GetShortestPath_civilian_enemyMilitaryAtEndOfTurn_cannotPathThrough()
    {
        VerticalWall(2, tile => testGame.AddUnit("Warrior", barbarianCiv, tile));
        testGame.AddUnit("Worker", civInfo, testGame.TileMap[1, 1]);
        var unit = testGame.AddUnit("Worker", civInfo, originTile);
        var paths = unit.Movement.GetShortestPath(testGame.TileMap[4, 4]);
        Assert.Empty(paths);
    }

    [Fact]
    public void GetMovementToTilesAtPosition_civilian_alliedCity_pathsThrough()
    {
        VerticalWall(2, tile => testGame.AddCity(civInfo, tile));
        var unit = testGame.AddUnit("Worker", civInfo, originTile);
        var paths = unit.Movement.GetMovementToTilesAtPosition(originTile.Position, 2f);
        Assert.True(paths.ContainsKey(testGame.TileMap[1, 0]), "getMovementToTilesAtPosition should move to allied tiles");
        Assert.True(paths.ContainsKey(testGame.TileMap[2, 0]), "getMovementToTilesAtPosition should move to allied cities");
    }

    [Fact]
    public void GetShortestPath_civilian_alliedCityAtEndOfTurn_doesEndTurnOnCity()
    {
        VerticalWall(2, tile => testGame.AddCity(civInfo, tile));
        testGame.AddUnit("Worker", civInfo, testGame.TileMap[1, 1]);
        var unit = testGame.AddUnit("Worker", civInfo, originTile);
        var paths = unit.Movement.GetShortestPath(testGame.TileMap[4, 4]);
        Assert.Equal(new List<TileClass> { testGame.TileMap[2, 2], testGame.TileMap[4, 4] }, paths);
    }

    [Fact]
    public void GetMovementToTilesAtPosition_civilian_neutralCity_doesNotPathThrough()
    {
        VerticalWall(2, tile => testGame.AddCity(neutralCiv, tile));
        var unit = testGame.AddUnit("Worker", civInfo, originTile);
        var paths = unit.Movement.GetMovementToTilesAtPosition(originTile.Position, 2f);
        Assert.True(paths.ContainsKey(testGame.TileMap[1, 0]), "getMovementToTilesAtPosition should pass through neutral cities");
        Assert.False(paths.ContainsKey(testGame.TileMap[2, 0]), "getMovementToTilesAtPosition should not move to neutral cities");
    }

    [Fact]
    public void GetShortestPath_civilian_neutralCityAtEndOfTurn_cannotPathThrough()
    {
        VerticalWall(2, tile => testGame.AddCity(neutralCiv, tile));
        var unit = testGame.AddUnit("Worker", civInfo, originTile);
        var paths = unit.Movement.GetShortestPath(testGame.TileMap[4, 4]);
        Assert.Empty(paths);
    }

    [Fact]
    public void GetMovementToTilesAtPosition_civilian_enemyCity_canMoveTo()
    {
        VerticalWall(2, tile => testGame.AddCity(barbarianCiv, tile));
        var unit = testGame.AddUnit("Worker", civInfo, originTile);
        var paths = unit.Movement.GetMovementToTilesAtPosition(originTile.Position, 2f);
        Assert.True(paths.ContainsKey(testGame.TileMap[1, 0]), "getMovementToTilesAtPosition should pass through enemy cities");
        Assert.True(paths.ContainsKey(testGame.TileMap[2, 0]), "Classic getMovementToTilesAtPosition moves to enemy cities");
    }

    [Fact]
    public void GetShortestPath_civilian_enemyCityAtEndOfTurn_cannotPathThrough()
    {
        VerticalWall(2, tile => testGame.AddCity(barbarianCiv, tile));
        testGame.AddUnit("Worker", civInfo, testGame.TileMap[1, 1]);
        var unit = testGame.AddUnit("Worker", civInfo, originTile);
        var paths = unit.Movement.GetShortestPath(testGame.TileMap[4, 4]);
        Assert.Empty(paths);
    }

    [Fact]
    public void GetMovementToTilesAtPosition_withFractionalMovementGreaterThanOne_ReturnsRightTiles()
    {
        testGame.SetTileTerrain(new HexCoord(0, 1), "Hill");
        var baseUnit = testGame.CreateBaseUnit();
        baseUnit.Movement = 2;
        var unit = testGame.AddUnit(baseUnit.Name, civInfo, originTile);
        unit.CurrentMovement = 1.5f;

        var path = unit.Movement.GetMovementToTilesAtPosition(originTile.Position, 1.5f);

        Assert.Equal(18, path.Count);
    }

    [Fact]
    public void GetMovementToTilesAtPosition_withFractionalMovementLessThanOne_ReturnsNeighbors()
    {
        testGame.SetTileTerrain(new HexCoord(0, 1), "Hill");
        var baseUnit = testGame.CreateBaseUnit();
        baseUnit.Movement = 2;
        var unit = testGame.AddUnit(baseUnit.Name, civInfo, originTile);
        unit.CurrentMovement = 0.5f;

        var path = unit.Movement.GetMovementToTilesAtPosition(originTile.Position, 0.5f);

        Assert.Equal(7, path.Count);
        foreach (var neighbor in originTile.Neighbors)
            Assert.True(path.ContainsKey(neighbor), $"expected to contain {neighbor}");
    }

    [Fact]
    public void GetMovementToTilesAtPosition_withVirtuallyNoMovement_ReturnsNeighbors()
    {
        if (useAStar) return; // AStar only returns Origin
        testGame.SetTileTerrain(new HexCoord(0, 1), "Hill");
        var baseUnit = testGame.CreateBaseUnit();
        baseUnit.Movement = 2;
        var unit = testGame.AddUnit(baseUnit.Name, civInfo, originTile);
        unit.CurrentMovement = 0.017f;

        var path = unit.Movement.GetMovementToTilesAtPosition(originTile.Position, 0.017f);

        Assert.Equal(7, path.Count);
        Assert.True(path.ContainsKey(originTile), $"expected to contain {originTile}");
        foreach (var neighbor in originTile.Neighbors)
            Assert.True(path.ContainsKey(neighbor), $"expected to contain {neighbor}");
    }

    [Fact]
    public void GetMovementToTilesAtPosition_withZeroMovement_ReturnsOrigin()
    {
        testGame.SetTileTerrain(new HexCoord(0, 1), "Hill");
        var baseUnit = testGame.CreateBaseUnit();
        baseUnit.Movement = 2;
        var unit = testGame.AddUnit(baseUnit.Name, civInfo, originTile);
        unit.CurrentMovement = 0f;

        var path = unit.Movement.GetMovementToTilesAtPosition(originTile.Position, 0f);

        Assert.Single(path);
    }

    [Fact]
    public void GetMovementToTilesAtPosition_whenLessThanFullMovement_reportsTotalMovementCorrectly()
    {
        VerticalWall(0, tile => tile.SetRoadStatus(RoadStatus.Road, civInfo));
        var unit = testGame.AddUnit("Archer", civInfo, originTile);
        unit.CurrentMovement = 1.6666667f;

        var paths = unit.Movement.GetMovementToTilesAtPosition(originTile.Position, 1.6666667f);

        var moreThanHalfMovement = testGame.TileMap[0, 3];
        Assert.Equal(0f, paths[originTile].TotalMovement, 0.01f);
        Assert.Equal(1.5f, paths[moreThanHalfMovement].TotalMovement, 0.01f);
    }

    [Fact]
    public void WhenPathingToStartDoNotCrash()
    {
        var baseUnit = testGame.CreateBaseUnit();
        baseUnit.Movement = 1;
        var unit = testGame.AddUnit(baseUnit.Name, civInfo, originTile);

        var target = testGame.GetTile(new HexCoord(0, 0));
        var path = unit.Movement.GetShortestPath(target);

        Assert.Single(path);
        Assert.Equal(new HexCoord(0, 0), path[0].Position);
    }

    [Fact]
    public void WhenMovementIsZeroThenCanMoveToAdjacent()
    {
        var baseUnit = testGame.CreateBaseUnit();
        baseUnit.Movement = 1;
        var unit = testGame.AddUnit(baseUnit.Name, civInfo, originTile);
        unit.CurrentMovement = 0f;
        var target = testGame.GetTile(new HexCoord(0, 1));

        Assert.True(unit.Movement.CanMoveTo(target));
    }

    [Fact]
    public void WhenMovementIsZeroThenCannotReachAdjacent()
    {
        var baseUnit = testGame.CreateBaseUnit();
        baseUnit.Movement = 1;
        var unit = testGame.AddUnit(baseUnit.Name, civInfo, originTile);
        unit.CurrentMovement = 0f;
        var target = testGame.GetTile(new HexCoord(0, 1));

        Assert.False(unit.Movement.CanReach(target));
    }

    [Fact]
    public void WhenPathingCostIs100ThenUseAllMovement()
    {
        var baseUnit = testGame.CreateBaseUnit();
        baseUnit.Movement = 3;
        var unit = testGame.AddUnit(baseUnit.Name, civInfo, originTile);
        // River between x==1 and x==2, except for the top tile
        foreach (var tile in testGame.TileMap.TileList)
        {
            if (tile.Position.X == 1 && tile.Position.Y < 7)
            {
                var upRight = testGame.TileMap.GetClockPositionNeighborTile(tile, 10);
                if (upRight != null)
                    tile.SetConnectedByRiver(upRight, true, true);
                var downRight = testGame.TileMap.GetClockPositionNeighborTile(tile, 12);
                if (downRight != null)
                    tile.SetConnectedByRiver(downRight, true, true);
            }
        }

        var target = testGame.GetTile(new HexCoord(5, 0));
        var path = unit.Movement.GetShortestPath(target);

        // Crossing the river ends the turn, then the rest of the route is one turn
        Assert.Equal(new[] { new HexCoord(2, 0), new HexCoord(5, 0) }, path.Select(it => it.Position).ToArray());
    }

    [Fact]
    public void GetShortestPath_avoidsUnfriendly()
    {
        var otherCiv = testGame.AddCiv();
        civInfo.DiplomacyFunctions.MakeCivilizationsMeet(otherCiv);
        civInfo.GetDiplomacyManager(otherCiv)!.AddModifier(DiplomaticModifiers.CapturedOurCities, -100f);
        otherCiv.GetDiplomacyManager(civInfo)!.AddModifier(DiplomaticModifiers.CapturedOurCities, -100f);
        var city = testGame.AddCity(otherCiv, testGame.TileMap[-5, -5]);
        var baseUnit = testGame.CreateBaseUnit();
        baseUnit.Movement = 1;
        originTile = testGame.TileMap[5, 0];
        var unit = testGame.AddUnit(baseUnit.Name, civInfo, originTile);
        unit.CurrentTile = originTile;
        var target = testGame.GetTile(new HexCoord(5, 2));

        // find a path
        var path1 = unit.Movement.GetShortestPath(target);
        Assert.Equal(2, path1.Count);
        // The other civ now owns the middle tile in that path
        path1[0].SetOwningCity(city);
        unit.Movement.ClearPathfindingCache();
        var path2 = unit.Movement.GetShortestPath(target);

        // Kotlin asserts path1's size again here (typo for path2): kept
        Assert.Equal(2, path1.Count);
        Assert.NotSame(path1[0], path2[0]);
    }

    [Fact]
    public void GetShortestPath_DoesNotAvoidAllies()
    {
        var otherCiv = testGame.AddCiv();
        civInfo.DiplomacyFunctions.MakeCivilizationsMeet(otherCiv);
        civInfo.GetDiplomacyManager(otherCiv)!.AddModifier(DiplomaticModifiers.BelieveSameReligion, 100f);
        civInfo.GetDiplomacyManager(otherCiv)!.HasOpenBorders = true;
        var city = testGame.AddCity(otherCiv, testGame.TileMap[-5, -5]);
        var baseUnit = testGame.CreateBaseUnit();
        baseUnit.Movement = 1;
        originTile = testGame.TileMap[5, 0];
        var unit = testGame.AddUnit(baseUnit.Name, civInfo, originTile);
        unit.CurrentTile = originTile;
        var target = testGame.GetTile(new HexCoord(5, 2));

        // find a path
        var path1 = unit.Movement.GetShortestPath(target);
        Assert.Equal(2, path1.Count);
        // The other civ now owns the middle tile in that path
        path1[0].SetOwningCity(city);
        unit.Movement.ClearPathfindingCache();
        var path2 = unit.Movement.GetShortestPath(target);

        // Kotlin asserts path1's size again here (typo for path2): kept
        Assert.Equal(2, path1.Count);
        Assert.Same(path1[0], path2[0]);
    }

    [Fact]
    public void ConnectRoadPathingMustIncludeBothEndpoints()
    {
        civInfo.Tech.AddTechnology("The Wheel", false);
        var worker = testGame.AddUnit("Worker", civInfo, originTile);
        var destination = testGame.GetTile(5, 3);

        var path = worker.Movement.GetRoadPath(destination) ?? [];

        Assert.True(path.Contains(originTile) && path.Contains(destination), "getRoadPath must contain both endpoints");
    }

    [Fact]
    public void ShortestPathEvenWhenItsWayMoreTiles()
    {
        if (!useAStar) return; // Classic pathfinding can't handle this case correctly

        // A straight road from 0,0 up the x axis, then straight down the y axis for 4 tiles, then straight down the x axis for 4 tiles
        foreach (var (x, y) in new[] { (0, 0), (1, 0), (2, 0), (3, 0), (4, 0), (5, 0), (5, 1), (5, 2), (5, 3), (5, 4), (4, 4), (3, 4), (2, 4), (1, 4), (0, 4) })
            testGame.GetTile(new HexCoord(x, y)).SetRoadStatus(RoadStatus.Railroad, civInfo);
        // The total roads are be 14 tiles, but only 1.4 movement. the direct route is 3 tiles, but
        // 3 movement.  So the road route should be chosen, despite gong way out of the way.
        var baseUnit = testGame.CreateBaseUnit();
        baseUnit.Movement = 1;
        var unit = testGame.AddUnit(baseUnit.Name, civInfo, originTile);
        var target = testGame.GetTile(new HexCoord(0, 4));

        var path = unit.Movement.GetShortestPath(target);

        // expect movement along the railroad, even though it's 13 tiles
        Assert.Equal([new HexCoord(3, 4), new HexCoord(0, 4)], path.Select(it => it.Position));
    }

    [Fact]
    public void CanPauseBeforeMountainsToCrossWithoutDamage()
    {
        if (!useAStar) return; // Classic pathfinding can't handle this case correctly

        // Everything is mountains
        foreach (var tile in testGame.TileMap.TileList)
            testGame.SetTileTerrain(tile.Position, "Mountain");
        testGame.SetTileTerrain(new HexCoord(0, 0), "Plains");
        testGame.SetTileTerrain(new HexCoord(0, 1), "Plains");
        // unit has two cross two mountains here, so MUST stop on 0,1 to safely cross
        testGame.SetTileTerrain(new HexCoord(0, 4), "Plains");
        testGame.SetTileTerrain(new HexCoord(0, 5), "Plains");
        // unit has two cross two mountains here, so MUST stop on 0,5 to safely cross
        testGame.SetTileTerrain(new HexCoord(0, 8), "Plains");

        // Enable Carthage LandUnitsCrossTerrainAfterUnitGained
        civInfo.PassThroughImpassableUnlocked = true;
        civInfo.PassableImpassables.Add("Mountain");

        var baseUnit = testGame.CreateBaseUnit();
        baseUnit.Movement = 3;
        var unit = testGame.AddUnit(baseUnit.Name, civInfo, originTile);
        var target = testGame.GetTile(new HexCoord(0, 8));

        var path = unit.Movement.GetShortestPath(target, avoidDamagingTerrain: true);

        Assert.Equal([new HexCoord(0, 1), new HexCoord(0, 4), new HexCoord(0, 5), new HexCoord(0, 8)], path.Select(it => it.Position));
    }

    [Fact]
    public void GetMovementToTilesAtPosition_withNegativeMovement_ReturnsOrigin()
    {
        testGame.SetTileTerrain(new HexCoord(0, 1), "Hill");
        if (!useAStar) return; // Classic pathfinding returns all neighbor tiles
        var baseUnit = testGame.CreateBaseUnit();
        baseUnit.Movement = 2;
        var unit = testGame.AddUnit(baseUnit.Name, civInfo, originTile);
        unit.CurrentMovement = -1f;

        var path = unit.Movement.GetMovementToTilesAtPosition(originTile.Position, -1f);

        Assert.Single(path);
    }

    [Fact]
    public void ConnectRoadAction()
    {
        civInfo.Tech.AddTechnology("The Wheel", false);
        var worker = testGame.AddUnit("Worker", civInfo, originTile);
        var destination = testGame.GetTile(5, 3);
        worker.AutomatedRoadConnectionDestination = destination.Position;

        new RoadToAutomation(civInfo).AutomateConnectRoad(worker, new HashSet<Unciv.View.TileView>());

        Assert.True(originTile.ImprovementInProgress == nameof(RoadStatus.Road), "Worker must have queued a Road");
        Assert.True(worker.AutomatedRoadConnectionPath?.Count > 0, "Worker must have a path");
    }
}

[Collection("RulesetCache")]
public sealed class PathfindingFixtureTests() : PathfindingFixtureTestsBase(false);

[Collection("RulesetCache")]
public sealed class PathfindingAStarFixtureTests() : PathfindingFixtureTestsBase(true);
