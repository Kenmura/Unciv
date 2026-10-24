using Unciv.Core.Tests.Support;
using Unciv.Models.Ruleset.Unique;

namespace Unciv.Core.Tests.Logic.Filter;

// T103: port of tests/src/com/unciv/logic/filter/TileFilterTests.kt.
[Collection("RulesetCache")]
public class TileFilterTests
{
    [Fact]
    public void TestTileFilters()
    {
        var game = new TestGame();
        game.MakeHexagonalMap(3);
        var civ = game.AddCiv();
        game.AddCity(civ, game.TileMap[2, 0]);
        UniqueTriggerActivation.TriggerUnique(new Unique("Turn this tile into a [Coast] tile"), civ, tile: game.TileMap[-3, 0]);
        UniqueTriggerActivation.TriggerUnique(new Unique("Turn this tile into a [Coast] tile"), civ, tile: game.TileMap[3, 0]);

        game.AddCity(civ, game.TileMap[-2, 0], initialPopulation: 9);

        TestParcours.Run("Tile matches filter", (string test) => game.TileMap.TileList.Count(it => it.MatchesFilter(test, civ)),
            ("All", 37),
            ("worked", 8),
            ("Coastal", 6),
            ("Coast", 2),
            ("Land", 35),
            ("Farm", 0));
    }
}
