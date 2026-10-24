using Unciv.Core.Tests.Support;
using Unciv.Logic.Map.MapUnits;

namespace Unciv.Core.Tests.Logic.Filter;

// T103: port of tests/src/com/unciv/logic/filter/UnitFilterTests.kt.
[Collection("RulesetCache")]
public class UnitFilterTests
{
    [Fact]
    public void TestFilteredUnits()
    {
        var game = new TestGame();
        game.MakeHexagonalMap(3);
        var civ = game.AddCiv();
        var city = game.AddCity(civ, game.TileMap[2, 0]);

        var wetTile = game.TileMap[3, 1];
        var deSela = game.AddCiv(game.Ruleset.Nations["Lhasa"]);
        var city2 = game.AddCity(deSela, game.TileMap[-2, 1]);
        wetTile.SetBaseTerrain(game.Ruleset.Terrains[Constants.Coast]);
        var carrier = game.AddUnit("Carrier", civ, wetTile);
        var carried = game.AddUnit("Fighter", civ, wetTile);
        var warrior = game.AddUnit("Warrior", civ, city.GetCenterTile());
        var scout1 = game.AddUnit("Scout", civ, game.TileMap.Values.First());
        var scout2 = game.AddUnit("Scout", civ, game.TileMap.Values.Last());
        var scout3 = game.AddUnit("Scout", deSela, city2.GetCenterTile());
        var worker1 = game.AddUnit("Worker", civ, game.TileMap[2, 1]);
        var worker2 = game.AddUnit("Worker", civ, game.TileMap[2, -1]);
        var units = new List<MapUnit> { carrier, carried, warrior, scout1, scout2, scout3, worker1, worker2 };
        var filters = new (string Filter, List<MapUnit> Expected)[]
        {
            ("Military", [carrier, carried, warrior, scout1, scout2, scout3]),
            ("Civilian", [worker1, worker2]),
            (deSela.CivName, [scout3]),
            ("All", units),
            ("Melee", [carrier, warrior, scout1, scout2, scout3]),
        };
        var failures = new List<string>();
        foreach (var (filter, expected) in filters)
        {
            var filtered = units.Where(it => it.MatchesFilter(filter)).ToList();
            if (!expected.All(filtered.Contains) || filtered.Count != expected.Count)
                failures.Add($"Filter: {filter}\nExpected size: {expected.Count}\nResult size: {filtered.Count}\n");
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }
}
