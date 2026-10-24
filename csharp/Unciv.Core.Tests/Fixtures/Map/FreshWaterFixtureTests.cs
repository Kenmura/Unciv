using Unciv.Logic.Files;
using Unciv.Logic.Map;

namespace Unciv.Core.Tests.Fixtures.MapFixtures;

// T141: port of tests/src/com/unciv/logic/map/FreshWaterTests.kt (real vanilla ruleset, saved-map string).
[Collection("RulesetCache")]
public sealed class FreshWaterFixtureTests
{
    // The Kotlin string is zipped libGDX minimal JSON (unquoted keys and values), which the C# JSON layer does not read (TODO(T147)); same content as strict JSON.
    private const string TestingMap = """{"mapParameters":{"type":"Empty","mapSize":{"radius":2,"width":5,"height":3,"name":"Custom"},"baseRuleset":"Civ V - Vanilla"},"tileList":[{"baseTerrain":"Desert"},{"position":{"x":-1,"y":-1},"baseTerrain":"Desert"},{"position":{"x":1,"y":1},"baseTerrain":"Desert"},{"position":{"y":-1},"baseTerrain":"Desert"},{"position":{"y":1},"baseTerrain":"Desert"},{"position":{"x":1},"baseTerrain":"Desert"},{"position":{"x":-1},"baseTerrain":"Desert"},{"position":{"x":-2,"y":-2},"baseTerrain":"Desert","terrainFeatures":["Oasis"]},{"position":{"x":2,"y":2},"baseTerrain":"Lakes"},{"position":{"x":-1,"y":-2},"baseTerrain":"Desert","terrainFeatures":["Hill"]},{"position":{"x":1,"y":2},"baseTerrain":"Desert","terrainFeatures":["Hill"]},{"position":{"y":-2},"baseTerrain":"Desert"},{"position":{"y":2},"baseTerrain":"Desert"},{"position":{"x":1,"y":-1},"baseTerrain":"Desert"},{"position":{"x":-1,"y":1},"baseTerrain":"Desert"},{"position":{"x":2},"baseTerrain":"Desert"},{"position":{"x":-2},"baseTerrain":"Desert","hasBottomLeftRiver":true},{"position":{"x":2,"y":1},"baseTerrain":"Desert","terrainFeatures":["Flood plains"],"hasBottomLeftRiver":true},{"position":{"x":-2,"y":-1},"baseTerrain":"Desert","terrainFeatures":["Flood plains"]}]}""";

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
