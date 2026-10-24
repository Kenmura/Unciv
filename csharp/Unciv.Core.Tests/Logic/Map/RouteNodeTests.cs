using Unciv.Core.Tests.Support;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Logic.Map.PathingMap;

namespace Unciv.Core.Tests.Logic.Map;

// T093: port of tests/src/com/unciv/logic/map/pathingmap/RouteNodeTest.kt, plus the argument checks and the uninitialized node.
[Collection("RulesetCache")]
public sealed class RouteNodeTests
{
    [Fact]
    public void BitsInRouteNodeRoundTrip()
    {
        var testGame = new TestGame();
        testGame.MakeHexagonalMap(209); //209 is smallest radius that uses all bits in tile index
        const int zeroBasedIndex = (1 << 17) | 1; // 18 bits. value is 131073
        var tile = testGame.TileMap.TileList[zeroBasedIndex];
        const RelationshipLevel relationship = RelationshipLevel.Unforgivable;
        var pbmMoveThisTurn = FixedPointMovement.FpmFromFixedPointBits((1 << 8) | 1); //9 bits. value is 257 aka 13.00
        var moveThisTurn = FixedPointMovement.FpmFromFixedPointBits((1 << 8) | 1); //9 bits. value is 257 aka 13.00
        const int turns = (1 << 5) | 1; // 6 bits. value is 33
        var parentTile = testGame.TileMap.GetClockPositionNeighborTile(tile, 12)!;
        const int damagingTiles = 3;
        var underestimatedTotal = FixedPointMovement.FpmFromFixedPointBits((1 << 13) | 1); //15 bits. value is 8193 aka 409.65move

        var node = new RouteNode(tile, relationship, pbmMoveThisTurn, moveThisTurn, turns, parentTile, damagingTiles, true);

        Assert.Equal(tile.ZeroBasedIndex, node.TileIdx);
        Assert.Equal(tile, node.GetTile(testGame.TileMap));
        Assert.Equal(12, node.ParentClockDir);
        Assert.Equal(parentTile, node.ParentTile(testGame.TileMap));
        Assert.False(node.CanStopOn);
        Assert.Equal(moveThisTurn, node.MoveUsedThisTurn);
        Assert.Equal(pbmMoveThisTurn, node.MoveSinceStoppable);
        Assert.Equal(turns, node.Turns);
        Assert.Equal(damagingTiles, node.DamagingTiles);
        Assert.False(node.EndTurnWithoutMoreDamage);
        Assert.Equal(relationship, node.RelationshipLevel);
        Assert.True(node.Initialized);
        Assert.Contains("turns=33", node.ToString());
        Assert.Contains("turns=33", node.ToString(testGame.TileMap));

        var prioritized = new PrioritizedNode(node, underestimatedTotal);
        Assert.Equal(tile.ZeroBasedIndex, prioritized.TileIdx);
        Assert.Equal(underestimatedTotal, prioritized.UnderestimatedTotal);
        Assert.Contains("underestimatedTotal=", prioritized.ToString());

        var reRouteNode = new RouteNode(prioritized.Bits);
        Assert.Equal(tile.ZeroBasedIndex, reRouteNode.TileIdx);
        Assert.Equal(tile, reRouteNode.GetTile(testGame.TileMap));
        // PrioritizedNode drops parentTile
        Assert.Equal(moveThisTurn, reRouteNode.MoveUsedThisTurn);
        Assert.Equal(pbmMoveThisTurn, reRouteNode.MoveSinceStoppable);
        Assert.Equal(turns, reRouteNode.Turns);
        Assert.Equal(damagingTiles, reRouteNode.DamagingTiles);
        Assert.Equal(relationship, reRouteNode.RelationshipLevel);
        Assert.True(reRouteNode.Initialized);
    }

    [Fact]
    public void RootNodeHasNoParentAndNoPathingNodeIsMarked()
    {
        var testGame = new TestGame();
        testGame.MakeHexagonalMap(3);
        var tile = testGame.TileMap[0, 0];

        var root = RouteNode.RootNode(tile, FixedPointMovement.FpmOne);
        Assert.Equal(14, root.ParentClockDir);
        Assert.Same(tile, root.ParentTile(testGame.TileMap));
        Assert.True(root.CanStopOn);
        Assert.False(root.IsNoPathingNode);

        var noPath = RouteNode.NoPathingNode(tile, 2);
        Assert.True(noPath.IsNoPathingNode);
        Assert.Equal(2, noPath.Turns);
        Assert.Equal(RouteNode.MaxDamagingTiles, noPath.DamagingTiles);
        Assert.Equal(RelationshipLevel.Unforgivable, noPath.RelationshipLevel);
    }

    [Fact]
    public void UninitializedNodeRejectsFieldReads()
    {
        var node = new RouteNode();
        Assert.False(node.Initialized);
        Assert.Throws<ArgumentException>(() => node.Turns);
        Assert.Throws<ArgumentException>(() => new PrioritizedNode(0L).TileIdx);
    }

    [Fact]
    public void ConstructorRejectsOutOfRangeFields()
    {
        var testGame = new TestGame();
        testGame.MakeHexagonalMap(3);
        var tile = testGame.TileMap[0, 0];
        var neighbor = testGame.TileMap[1, 0];
        var farTile = testGame.TileMap[3, 0];
        var zero = FixedPointMovement.FpmZero;
        var tooBig = FixedPointMovement.FpmFromFixedPointBits(512);

        Assert.Throws<ArgumentException>(() => new RouteNode(tile, RelationshipLevel.Ally, zero, tooBig, 0, tile, 0, false));
        Assert.Throws<ArgumentException>(() => new RouteNode(tile, RelationshipLevel.Ally, tooBig, zero, 0, tile, 0, false));
        Assert.Throws<ArgumentException>(() => new RouteNode(tile, RelationshipLevel.Ally, FixedPointMovement.FpmFromFixedPointBits(-1), zero, 0, tile, 0, false));
        Assert.Throws<ArgumentException>(() => new RouteNode(tile, RelationshipLevel.Ally, zero, FixedPointMovement.FpmFromFixedPointBits(-1), 0, tile, 0, false));
        Assert.Throws<ArgumentException>(() => new RouteNode(tile, RelationshipLevel.Ally, zero, zero, -1, tile, 0, false));
        Assert.Throws<ArgumentException>(() => new RouteNode(tile, RelationshipLevel.Ally, zero, zero, RouteNode.MaxTurns + 1, tile, 0, false));
        Assert.Throws<ArgumentException>(() => new RouteNode(tile, RelationshipLevel.Ally, zero, zero, 0, tile, -1, false));
        Assert.Throws<ArgumentException>(() => new RouteNode(tile, RelationshipLevel.Ally, zero, zero, 0, tile, RouteNode.MaxDamagingTiles + 1, false));
        // not a neighbor: the clock position is -1, so the parent field is not positive
        Assert.Throws<ArgumentException>(() => new RouteNode(tile, RelationshipLevel.Ally, zero, zero, 0, farTile, 0, false));
        Assert.Equal(testGame.TileMap.GetNeighborTileClockPosition(tile, neighbor), new RouteNode(tile, RelationshipLevel.Ally, zero, zero, 0, neighbor, 0, false).ParentClockDir);
    }

    [Fact]
    public void PrioritizedNodeRejectsOutOfRangeEstimates()
    {
        var testGame = new TestGame();
        testGame.MakeHexagonalMap(3);
        var node = RouteNode.RootNode(testGame.TileMap[0, 0], FixedPointMovement.FpmZero);

        Assert.Throws<ArgumentException>(() => new PrioritizedNode(node, FixedPointMovement.FpmZero));
        Assert.Throws<ArgumentException>(() => new PrioritizedNode(node, FixedPointMovement.FpmFromFixedPointBits(1 << 14)));
    }
}
