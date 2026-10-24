using Unciv.Logic.Files;
using Unciv.Logic.Map;

namespace Unciv.Core.Tests.Fixtures.MapFixtures;

// T141: port of tests/src/com/unciv/logic/map/FreshWaterTests.kt (real vanilla ruleset, saved-map string).
[Collection("RulesetCache")]
public sealed class FreshWaterFixtureTests
{
    // The original Kotlin string: zipped libGDX minimal JSON (unquoted keys and values), read via LenientJson (T147).
    private const string TestingMap = "H4sIAAAAAAAA/52SwU7DMAyGX6UPkB5WxMVHBhOHSqCBdpl6CKqh1tKmit3REvXdSdVb17GIS6Qkn//f+WNf6/ZVO12joGPwMrQIT3Urgwo3b/SD4J0uqWPI1DeVUsG9qpC+KoE71YQ62HYsth7Vh2bcdwYZBbZ0Tg5Jmhx0Q8boUQkZzIkFjn7i3tE5TQ08BtrJqHxrmYRsA76HdKOGsMyKf5ITeJuLEosR6qOYqM7TbHpjtkYqmbc71NI5ZDi+aCYuFgqTwKI+1yfk1TAjjZ7DZy19Npc+8eXXrBfQbWbuIy7auKnoo1yvJFdpfrASxj7HT9nTGR2I63Dli1YbuYxuZ6wtk9aEQy4i5ecZ+o/BWIy/FE5uFvkDAAA=";

    private static readonly HashSet<HexCoord> AdjacentToFreshWater =
    [
        new(-1, -1), new(1, 1), new(-1, -2), new(1, 2), new(2, 0), new(-2, 0), new(2, 1), new(-2, -1),
    ];

    [Fact]
    public void IsAdjacentToFreshWater()
    {
        var testGame = new Support.TestGame();
        var map = MapSaver.MapFromSavedString(TestingMap);
        map.SetTransients(testGame.Ruleset, false);

        foreach (var tile in map.Values)
        {
            var isFresh = tile.IsAdjacentTo(Constants.FreshWater);
            var shouldFresh = AdjacentToFreshWater.Contains(tile.Position);
            Assert.False(isFresh && !shouldFresh, $"Tile {tile} has fresh water but should not");
            Assert.False(!isFresh && shouldFresh, $"Tile {tile} should have fresh water but doesn't");
        }
    }
}
