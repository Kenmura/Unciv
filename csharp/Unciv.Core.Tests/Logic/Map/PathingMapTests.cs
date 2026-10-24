using Unciv.Core.Tests.Support;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using Unciv.Logic.Map.MapUnits;
using Unciv.Logic.Map.PathingMap;
using Unciv.Logic.Map.Tiles;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Core.Tests.Logic.Map;

// T093: port of tests/src/com/unciv/logic/map/pathingmap/PathingMapTest.kt over the real TestGame (every test, same names and expected debug maps).
[Collection("RulesetCache")]
public sealed class PathingMapTests
{
    private TileClass originTile;
    private readonly Civilization civInfo;
    private readonly TestGame testGame = new();

    public PathingMapTests()
    {
        testGame.MakeHexagonalMap(8);
        originTile = testGame.TileMap[0, 0];
        civInfo = testGame.AddCiv();
        for (var i = 0; i < 100; i++)
            testGame.TileMap.TileList[i].SetExplored(civInfo, true);
    }

    private static void AssertPositions(IEnumerable<HexCoord> expected, IEnumerable<TileClass>? actual) =>
        Assert.Equal(expected, actual!.Select(it => it.Position));

    [Fact] // This only exists to reduce how often we accidentally push with verbose logging enabled
    public void Verbose_pathing_logs_disabled() => Assert.Equal(PathingMap.NeverLog, PathingMap.VerbosePathfindingLogs);

    [Fact]
    public void ShortestPathEvenWhenItsWayMoreTiles()
    {
        // A straight road from 0,0 up the x axis
        foreach (var (x, y) in new[] { (0, 0), (1, 0), (2, 0), (3, 0), (4, 0), (5, 0),
                     // then straight down the y axis for 4 tiles
                     (5, 1), (5, 2), (5, 3), (5, 4),
                     // then straight down the x axis for 4 tiles
                     (4, 4), (3, 4), (2, 4), (1, 4), (0, 4) })
            testGame.GetTile(new HexCoord(x, y)).SetRoadStatus(RoadStatus.Railroad, civInfo);
        // The total roads are be 14 tiles, but only 1.4 movement. the direct route is 3 tiles, but
        // 3 movement.  So the road route should be chosen, despite gong way out of the way.
        var baseUnit = testGame.CreateBaseUnit();
        baseUnit.Movement = 1;
        var unit = testGame.AddUnit(baseUnit.Name, civInfo, originTile);
        var target = testGame.GetTile(new HexCoord(0, 4));

        var pathing = PathingMap.CreateUnitPathingMap(unit);
        var path = pathing.GetShortestPath(target)!;

        // expect movement along the railroad, even though it's 13 tiles
        AssertPositions([new HexCoord(3, 4), new HexCoord(0, 4)], path);
        Assert.Equal(1, pathing.GetCachedNode(target).Turns);
        Assert.Equal(FixedPointMovement.FpmFromMovement(0.3f), pathing.GetCachedNode(target).MoveUsedThisTurn);
        Assert.Equal("\n        -1     +0     +1     +2     +3     +4     +5     +6    \n  +5     /      /     1/1.0  1/1.0  1/1.0  0/1.0  0/1.0  0/1.0 \n  +4     /     1/0.3D 1/0.2  1/0.1  0/1.0  0/0.9  0/0.8  0/1.0 \n  +3     /     1/1.0  1/1.0  1/1.0  0/1.0  0/1.0  0/0.7  0/1.0 \n  +2     /      /      /      /      /     0/1.0  0/0.6  0/1.0 \n  +1     /     0/1.0  0/1.0  0/1.0  0/1.0  0/1.0  0/0.5  0/1.0 \n  +0    0/1.0  0/0.0S 0/0.1  0/0.2  0/0.3  0/0.4  0/0.5   /    \n  -1    0/1.0  0/1.0  0/1.0  0/1.0  0/1.0  0/1.0   /      /    \n", pathing.ToDebugString(target));
        // And affirm cache
        Assert.Equal(path, pathing.GetShortestPath(target)!);
    }

    [Fact]
    public void CanPauseBeforeMountainsToCrossWithoutDamage()
    {
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

        var pathing = PathingMap.CreateUnitPathingMap(unit);
        var path = pathing.GetShortestPath(target);

        AssertPositions([new HexCoord(0, 1), new HexCoord(0, 4), new HexCoord(0, 5), new HexCoord(0, 8)], path);
        Assert.Equal("\n        -3     -2     -1     +0     +1     +2     +3    \n  +8                         3/3.0D 4/1.0   /      /    \n  +7                  4/1.0  3/2.0  3/2.0  3/2.0  3/1.0 \n  +6            /     3/2.0  2/2.0  2/2.0  2/2.0  3/1.0 \n  +5     /     3/2.0  2/2.0  2/1.0  2/1.0  2/2.0  3/1.0 \n  +4    3/1.0  2/2.0  2/1.0  1/3.0  2/1.0  2/2.0  3/1.0 \n  +3    3/1.0  2/2.0  2/1.0  1/2.0  1/2.0  1/2.0  2/1.0 \n  +2    3/1.0  2/2.0  1/2.0  0/2.0  0/2.0  0/2.0  1/1.0 \n  +1    3/1.0  1/2.0  0/2.0  0/1.0  0/1.0  0/2.0  1/1.0 \n  +0    2/1.0  0/2.0  0/1.0  0/0.0S 0/1.0  0/2.0  1/1.0 \n  -1    1/1.0  0/2.0  0/1.0  0/1.0  0/2.0  1/1.0   /    \n  -2    1/1.0  0/2.0  0/2.0  0/2.0  1/1.0   /      /    \n  -3    1/1.0  1/1.0  1/1.0  1/1.0   /      /      /    \n", pathing.ToDebugString(target));
        // And affirm cache
        Assert.Equal(path, pathing.GetShortestPath(target)!);
    }

    [Fact]
    public void GetMovementToTilesAtPosition_returnsRightTiles()
    {
        testGame.SetTileTerrain(new HexCoord(0, 1), "Hill");
        var baseUnit = testGame.CreateBaseUnit();
        baseUnit.Movement = 2;
        var unit = testGame.AddUnit(baseUnit.Name, civInfo, originTile);
        unit.CurrentMovement = 2f;

        var pathing = PathingMap.CreateUnitPathingMap(unit);
        var path = pathing.GetMovementToTilesAtPosition();

        Assert.True(path.Count == 18, path.ToString());
        Assert.Equal("\n        -2     -1     +0     +1     +2    \n  +2     /      /      /     0/2.0  0/2.0 \n  +1     /     0/2.0  0/2.0  0/1.0  0/2.0 \n  +0    0/2.0  0/1.0  0/0.0S 0/1.0  0/2.0 \n  -1    0/2.0  0/1.0  0/1.0  0/2.0   /    \n  -2    0/2.0  0/2.0  0/2.0   /      /    \n", pathing.ToDebugString());
        // And affirm cache
        Assert.Same(path, pathing.GetMovementToTilesAtPosition());
    }

    [Fact]
    public void GetShortestPath_takesOverMaxTurns_returnsNull()
    {
        var baseUnit = testGame.CreateBaseUnit();
        baseUnit.Movement = 1;
        originTile = testGame.GetTile(new HexCoord(-8, -8));
        var targetTile = testGame.GetTile(new HexCoord(8, 8));
        var unit = testGame.AddUnit(baseUnit.Name, civInfo, originTile);
        unit.CurrentMovement = 1f;

        var pathing = PathingMap.CreateUnitPathingMap(unit);
        var path = pathing.GetShortestPath(targetTile, 5);

        Assert.Null(path);
        // And affirm cache
        Assert.Equal(path, pathing.GetShortestPath(targetTile, 5));
    }

    [Fact]
    public void GetShortestPath_takesOver63Turns_returnsNull()
    {
        testGame.MakeHexagonalMap(100);
        var baseUnit = testGame.CreateBaseUnit();
        baseUnit.Movement = 1;
        originTile = testGame.GetTile(new HexCoord(-50, 0));
        var targetTile = testGame.GetTile(new HexCoord(50, 0));
        var unit = testGame.AddUnit(baseUnit.Name, civInfo, originTile);
        unit.CurrentMovement = 1f;

        var pathing = PathingMap.CreateUnitPathingMap(unit);
        var path = pathing.GetShortestPath(targetTile);

        Assert.Null(path);
        // And affirm cache
        Assert.Equal(path, pathing.GetShortestPath(targetTile));
    }

    [Fact]
    public void GetShortestPath_toEveryTile_usesCacheCorrectly()
    {
        var baseUnit = testGame.CreateBaseUnit();
        baseUnit.Movement = 4;
        var unit = testGame.AddUnit(baseUnit.Name, civInfo, originTile);
        unit.CurrentMovement = 4f;

        var pathing = PathingMap.CreateUnitPathingMap(unit);
        foreach (var targetTile in testGame.TileMap.TileList)
        {
            var aerialDistance = originTile.AerialDistanceTo(targetTile);

            var path = pathing.GetShortestPath(targetTile);

            if (aerialDistance <= 4)
                Assert.Equal([targetTile], path);
            else
            {
                Assert.Equal(2, path!.Count);
                Assert.Equal(targetTile, path[1]);
            }
        }
    }

    [Fact]
    public void FindMatchingTilesInAttackRange_ignoresAttackRange()
    {
        var evemyCiv = testGame.AddBarbarianCiv();
        // Right side all hills, left side all roads.  A line of enemy warriors just north.
        foreach (var tile in testGame.TileMap.TileList)
        {
            if (tile.Position.x > 0) tile.AddTerrainFeature("Hill");
            if (tile.Position.x < 0) tile.SetRoadStatus(RoadStatus.Road, civInfo);
            if (tile.Position.y == 2) testGame.AddUnit("Warrior", evemyCiv, tile);
        }
        var baseUnit = testGame.CreateBaseUnit();
        baseUnit.Movement = 3;
        baseUnit.Range = 2;
        var unit = testGame.AddUnit(baseUnit.Name, civInfo, originTile);
        unit.CurrentMovement = 3f;

        var pathing = PathingMap.CreateUnitPathingMap(unit);
        var attackableTiles = pathing.BfsAllMatchingTiles(1, (tile, _) => tile.MilitaryUnit?.Civ == evemyCiv);

        // cant hit enemy at -4,2 or 3,2 because unit would have no movement left.
        var expected = new[]
        {
            new HexCoord(-3, 2), new HexCoord(-2, 2), new HexCoord(-1, 2),
            new HexCoord(0, 2), new HexCoord(1, 2), new HexCoord(2, 2),
        };
        static int ByXThenY(HexCoord l, HexCoord r) => l.x != r.x ? l.x.CompareTo(r.x) : l.y.CompareTo(r.y);
        var actual = attackableTiles.Select(it => it.Position).OrderBy(it => it, Comparer<HexCoord>.Create(ByXThenY)).ToList();
        Assert.Equal(expected, actual);
        Assert.Equal("\n        -5     -4     -3     -2     -1     +0     +1     +2    \n  +2     /      /     0/17.0* 0/17.0* 0/17.0* 0/17.0* 0/17.0* 0/17.0*\n  +1     /     0/3.0  0/2.5  0/2.0  0/1.5  0/1.0  0/2.0  0/3.0 \n  +0    0/3.0  0/2.5  0/2.0  0/1.5  0/1.0  0/0.0S 0/2.0  0/3.0 \n  -1    0/3.0  0/2.5  0/2.0  0/1.5  0/1.0  0/1.0  0/3.0   /    \n  -2    0/3.0  0/2.5  0/2.0  0/1.5  0/1.5  0/2.0  0/3.0   /    \n  -3    0/3.0  0/2.5  0/2.0  0/2.0  0/2.0  0/3.0   /      /    \n  -4    0/3.0  0/2.5  0/2.5  0/2.5  0/2.5  0/3.0   /      /    \n  -5    0/3.0  0/3.0  0/3.0  0/3.0  0/3.0   /      /      /    \n", pathing.ToDebugString());
        // The Kotlin test re-reads the same list here. A second search over the fully cached nodes offers nothing to the predicate
        // (only newly reached tiles are offered), a Kotlin quirk the port keeps.
        Assert.Empty(pathing.BfsAllMatchingTiles(1, (tile, _) => tile.MilitaryUnit?.Civ == evemyCiv));
    }

    [Fact]
    public void FindMatchingTilesInAttackRange_considersSelf()
    {
        var baseUnit = testGame.CreateBaseUnit();
        baseUnit.Movement = 1;
        var unit = testGame.AddUnit(baseUnit.Name, civInfo, originTile);
        unit.CurrentMovement = 1f;

        var pathing = PathingMap.CreateUnitPathingMap(unit);
        var attackableTiles = pathing.BfsAllMatchingTilesThisTurn((tile, _) => tile.CivilianUnit?.Civ == civInfo);

        HexCoord[] expected = [new(0, 0)];
        AssertPositions(expected, attackableTiles);
        // And affirm full recalculation using cached tiles has same result.
        AssertPositions(expected, pathing.BfsAllMatchingTilesThisTurn((tile, _) => tile.CivilianUnit?.Civ == civInfo));
    }

    private Civilization AddGreatGeneralCrossingCiv()
    {
        var civ = testGame.AddCiv(["Land units may cross [Mountain] tiles after the first [Great General] is earned"]);
        civ.PassThroughImpassableUnlocked = true;
        civ.PassableImpassables.Add("Mountain");
        return civ;
    }

    [Fact]
    public void SafeCacheIsPreferredOverDamageCacheWhenBothReachTargetEquallyFast()
    {
        // Two routes of identical length from origin to target: one straight through a Mountain
        // (damaging), one around through Plains (safe). The safe, damage-free route must win.
        testGame.SetTileTerrain(new HexCoord(1, 0), "Mountain");
        var civ = AddGreatGeneralCrossingCiv();
        var baseUnit = testGame.CreateBaseUnit();
        baseUnit.Movement = 3;
        var unit = testGame.AddUnit(baseUnit.Name, civ, originTile);
        var target = testGame.GetTile(new HexCoord(2, 0));

        var pathing = PathingMap.CreateUnitPathingMap(unit);
        var path = pathing.GetShortestPath(target);

        Assert.Equal([target], path);
        var node = pathing.GetCachedNode(target);
        Assert.Equal(0, node.DamagingTiles);
    }

    [Fact]
    public void OnlyRouteIsThroughDamage_stillFindsItViaDamageCache()
    {
        // Everything is mountains except a corridor: origin, one Mountain, then the target. With
        // only 1 movement, crossing the Mountain is unavoidable and forces ending a turn on it.
        foreach (var tile in testGame.TileMap.TileList)
            testGame.SetTileTerrain(tile.Position, "Mountain");
        testGame.SetTileTerrain(new HexCoord(0, 0), "Plains");
        testGame.SetTileTerrain(new HexCoord(2, 0), "Plains");
        var civ = AddGreatGeneralCrossingCiv();

        var baseUnit = testGame.CreateBaseUnit();
        baseUnit.Movement = 1;
        var unit = testGame.AddUnit(baseUnit.Name, civ, originTile);
        var target = testGame.GetTile(new HexCoord(2, 0));

        var pathing = PathingMap.CreateUnitPathingMap(unit);
        var path = pathing.GetShortestPath(target);

        Assert.Equal([testGame.GetTile(new HexCoord(1, 0)), target], path);
        var node = pathing.GetCachedNode(target);
        Assert.Equal(1, node.DamagingTiles);
    }

    [Fact]
    public void ChainOfMultipleDamagingTilesIsCrossedAndTallied()
    {
        // Everything is mountains except a corridor with 3 consecutive Mountain tiles to cross,
        // forced to stop on each one in turn (1 movement per turn).
        foreach (var tile in testGame.TileMap.TileList)
            testGame.SetTileTerrain(tile.Position, "Mountain");
        testGame.SetTileTerrain(new HexCoord(0, 0), "Plains");
        testGame.SetTileTerrain(new HexCoord(4, 0), "Plains");
        var civ = AddGreatGeneralCrossingCiv();

        var baseUnit = testGame.CreateBaseUnit();
        baseUnit.Movement = 1;
        var unit = testGame.AddUnit(baseUnit.Name, civ, originTile);
        var target = testGame.GetTile(new HexCoord(4, 0));

        var pathing = PathingMap.CreateUnitPathingMap(unit);
        var path = pathing.GetShortestPath(target);

        Assert.Equal(
            [
                testGame.GetTile(new HexCoord(1, 0)),
                testGame.GetTile(new HexCoord(2, 0)),
                testGame.GetTile(new HexCoord(3, 0)),
                target,
            ], path);
        var node = pathing.GetCachedNode(target);
        // Ending our turn there is the only option, crossing all 3 damaging tiles along the way.
        Assert.Equal(3, node.DamagingTiles);
    }

    [Fact]
    public void OccupiedTileDoesNotInterfereWithDamageAccounting()
    {
        // The direct route is blocked by another unit; the detour crosses a Mountain instead.
        // The two concepts (blocked-for-ending-turn vs damage) must not interfere with each other.
        testGame.SetTileTerrain(new HexCoord(1, 0), "Grassland");
        testGame.SetTileTerrain(new HexCoord(1, -1), "Mountain");
        testGame.SetTileTerrain(new HexCoord(2, -1), "Grassland");
        testGame.SetTileTerrain(new HexCoord(2, 0), "Grassland");
        var civ = AddGreatGeneralCrossingCiv();
        var blockerCiv = testGame.AddCiv();
        testGame.AddUnit("Warrior", blockerCiv, testGame.GetTile(new HexCoord(1, 0)));

        var baseUnit = testGame.CreateBaseUnit();
        baseUnit.Movement = 3;
        var unit = testGame.AddUnit(baseUnit.Name, civ, originTile);
        var target = testGame.GetTile(new HexCoord(2, 0));

        var pathing = PathingMap.CreateUnitPathingMap(unit);
        var path = pathing.GetShortestPath(target);

        Assert.Equal([target], path);
    }

    [Fact]
    public void CanRetroactivelyPauseOnADamagingTileToPushPastAnUnstoppableTileAfterIt()
    {
        foreach (var tile in testGame.TileMap.TileList)
            testGame.SetTileTerrain(tile.Position, "Ocean");
        testGame.SetTileTerrain(new HexCoord(0, 0), "Plains");
        testGame.SetTileTerrain(new HexCoord(0, 1), "Mountain");
        testGame.SetTileTerrain(new HexCoord(0, 2), "Plains");
        testGame.SetTileTerrain(new HexCoord(0, 3), "Plains");
        civInfo.PassThroughImpassableUnlocked = true;
        civInfo.PassableImpassables.Add("Mountain");
        testGame.AddUnit("Warrior", civInfo, testGame.GetTile(new HexCoord(0, 2)));

        var unit = testGame.AddUnit("Warrior", civInfo, originTile);
        unit.CurrentMovement = 2f;
        var target = testGame.GetTile(new HexCoord(0, 3));
        var pathing = PathingMap.CreateUnitPathingMap(unit);
        var path = pathing.GetShortestPath(target);

        Assert.Equal([testGame.GetTile(new HexCoord(0, 1)), target], path);
        Assert.Equal(1, pathing.GetCachedNode(target).DamagingTiles);
    }

    [Fact]
    public void ManyWarriorsConvergingOnRoad_doNotQueueSingleFile()
    {
        testGame.GameInfo.ClientState.UseAStarPathfinding = true; // the Kotlin test relies on the process-wide A* setting of earlier tests
        for (var x = -8; x <= 8; x++)
            testGame.GetTile(new HexCoord(x, 0)).SetRoadStatus(RoadStatus.Road, civInfo);
        foreach (var tile in testGame.TileMap.TileList)
            tile.SetExplored(civInfo, true);
        //make 18 warriors between [-8,-8] and [-7,0]
        var warriors = new List<MapUnit>();
        foreach (var tile in testGame.TileMap.TileList)
        {
            if (tile.Position.x < -6 && tile.Position.y < 1)
                warriors.Add(testGame.AddUnit("Warrior", civInfo, tile));
        }
        Assert.Equal(18, warriors.Count);
        // tell all 19 warriors to move 15 tiles to the right, and 8 up.
        var targets = warriors.Select(it => testGame.TileMap[it.GetTile().Position.x + 15, it.GetTile().Position.y + 8]).ToList();
        // Ideally, moving these 15 tiles takes them 8 turns
        for (var turn = 0; turn < 8; turn++)
        {
            foreach (var warrior in warriors)
                warrior.CurrentMovement = 2f;
            for (var i = 0; i < warriors.Count; i++)
                warriors[i].Movement.HeadTowards(targets[i]);
        }
        // Ensure all warriors moved more than halfway, and didn't end up single-file
        foreach (var warrior in warriors)
        {
            var position = warrior.GetTile().Position;
            Assert.True(position.x > 0, $"{warrior} at {position} should have moved to x>0");
        }
    }
}
