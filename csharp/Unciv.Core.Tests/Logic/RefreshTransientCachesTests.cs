using Unciv.Core.Tests.Support;
using Unciv.Logic.Files;
using Unciv.Logic.Map;
using Unciv.Utils;

namespace Unciv.Core.Tests.Logic;

// T208: GameInfo.RefreshTransientCaches (the soak asserts the whole running == reload property; these are the cheap unit cases).
[Collection("RulesetCache")]
public class RefreshTransientCachesTests
{
    [Fact]
    public void ReleasesWorkedTilesTheCivDoesNotOwnAndKeepsOwnedOnes()
    {
        var game = new TestGame();
        game.MakeHexagonalMap(3);
        var civ = game.AddCiv();
        var city = game.AddCity(civ, game.GetTile(0, 0));
        var stranger = game.GetTile(2, 0);
        city.WorkedTiles.Add(stranger.Position);
        Assert.Contains(stranger.Position, city.WorkedTiles);

        game.GameInfo.RefreshTransientCaches();

        Assert.DoesNotContain(stranger.Position, city.WorkedTiles);
    }

    [Fact]
    public void CanonicalizeRebuildsASetLikeALoadWould()
    {
        var grown = new JavaHashSet<string>();
        var names = Enumerable.Range(0, 40).Select(it => "Building" + it).ToList();
        foreach (var name in names) grown.Add(name);
        foreach (var name in names.Skip(8)) grown.Remove(name); // keeps the big table: iteration differs from a fresh set
        var reloaded = new JavaHashSet<string>();
        foreach (var name in grown) reloaded.Add(name);

        grown.Canonicalize();

        Assert.Equal(reloaded, grown);
        Assert.Equal(8, grown.Count);
    }

    // The civ's unit list order must follow MapUnit.Id, not tile order and not insertion order (T231).
    // The units sit on (2,0) and (0,0); the load path enumerates tiles in map order, which puts (0,0) first (the test below asserts it).
    private static (TestGame game, Unciv.Logic.Civilization.Civilization civ) TwoUnits(int idAt20, int idAt00)
    {
        var game = new TestGame();
        game.MakeHexagonalMap(3);
        var civ = game.AddCiv(game.Ruleset.Nations["Babylon"]); // a real nation, so the saved game can be loaded again
        game.AddUnit("Warrior", civ, game.GetTile(2, 0)).Id = idAt20;
        game.AddUnit("Warrior", civ, game.GetTile(0, 0)).Id = idAt00;
        return (game, civ);
    }

    [Fact]
    public void RefreshKeepsUnitListsInCreationIdOrder()
    {
        var (game, civ) = TwoUnits(idAt20: 9, idAt00: 5); // insertion order [9, 5]
        Assert.Equal(new[] { 9, 5 }, civ.Units.GetCivUnits().Select(it => it.Id).ToArray());

        game.GameInfo.RefreshTransientCaches();

        Assert.Equal(new[] { 5, 9 }, civ.Units.GetCivUnits().Select(it => it.Id).ToArray());
    }

    [Fact]
    public void LoadBuildsUnitListsInCreationIdOrder()
    {
        var (game, civ) = TwoUnits(idAt20: 5, idAt00: 9); // tile order [9, 5]
        Assert.Equal(new[] { 9, 5 }, game.GameInfo.TileMap.Values.SelectMany(it => it.GetUnits()).Select(it => it.Id).ToArray());
        game.GameInfo.CurrentPlayer = civ.CivName; // a saved game needs a current player
        var loaded = UncivFiles.GameInfoFromString(UncivFiles.GameInfoToString(game.GameInfo, forceZip: false));

        var loadedCiv = loaded.Civilizations.Single(it => it.Units.GetCivUnits().Any());
        Assert.Equal(new[] { 5, 9 }, loadedCiv.Units.GetCivUnits().Select(it => it.Id).ToArray());
    }

    private static int[] LoadedIds(TestGame game, Unciv.Logic.Civilization.Civilization civ)
    {
        game.GameInfo.CurrentPlayer = civ.CivName; // a saved game needs a current player
        var loaded = UncivFiles.GameInfoFromString(UncivFiles.GameInfoToString(game.GameInfo, forceZip: false));
        return loaded.GetCivilization(civ.CivName).Units.GetCivUnits().Select(it => it.Id).ToArray();
    }

    // T236: units sharing an id are ordered by tile order (then the tile's own order), not by where they sit in the list, on refresh and on load alike.
    [Fact]
    public void DuplicateUnitIdsAreOrderedByTileOnRefresh()
    {
        var (game, civ) = TwoUnits(idAt20: 5, idAt00: 5);
        var far = civ.Units.GetCivUnits().First();
        var near = civ.Units.GetCivUnits().Last();
        Assert.Equal(new[] { near, far }, game.GameInfo.TileMap.Values.SelectMany(it => it.GetUnits()).ToArray()); // a load meets the (0,0) unit first
        Assert.Same(far, civ.Units.GetCivUnits().First()); // the list holds them the other way round

        game.GameInfo.RefreshTransientCaches();

        Assert.Equal(new[] { near, far }, civ.Units.GetCivUnits().ToArray());
        Assert.Equal(new[] { 5, 5 }, LoadedIds(game, civ));
    }

    [Fact]
    public void DuplicateUnitIdsOnOneTileFollowTheTilesMilitaryBeforeCivilianOrder()
    {
        var game = new TestGame();
        game.MakeHexagonalMap(3);
        var civ = game.AddCiv(game.Ruleset.Nations["Babylon"]);
        var tile = game.GetTile(1, 0);
        var settler = game.AddUnit("Settler", civ, tile);
        var warrior = game.AddUnit("Warrior", civ, tile);
        settler.Id = 4;
        warrior.Id = 4;
        Assert.Equal(new[] { warrior, settler }, tile.GetUnits().ToArray()); // military first
        Assert.Same(settler, civ.Units.GetCivUnits().First()); // list order: settler added first

        game.GameInfo.RefreshTransientCaches();

        Assert.Equal(new[] { warrior, settler }, civ.Units.GetCivUnits().ToArray());
    }

    // T236: units without an id (very old saves) receive one on load; they must be sorted by that id, after the units that already had one.
    [Fact]
    public void UnitsWithoutIdAreNumberedBeforeTheyAreSortedOnLoad()
    {
        var game = new TestGame();
        game.MakeHexagonalMap(3);
        var civ = game.AddCiv(game.Ruleset.Nations["Babylon"]);
        game.AddUnit("Warrior", civ, game.GetTile(2, 0)).Id = Constants.NoId;
        game.AddUnit("Warrior", civ, game.GetTile(0, 0)).Id = Constants.NoId;
        game.AddUnit("Warrior", civ, game.GetTile(1, 0)).Id = 2;
        game.GameInfo.CurrentPlayer = civ.CivName;
        var json = UncivFiles.GameInfoToString(game.GameInfo, forceZip: false);

        var loaded = UncivFiles.GameInfoFromString(json);
        var units = loaded.GetCivilization(civ.CivName).Units.GetCivUnits().ToList();

        // (0,0) is met before (2,0), so it is numbered first: the counter of the saved game (3, the three units were created through AddUnit) continues: ids 4, 5, and the unit that kept its id 2 sorts before them (tile order alone would put it last)
        Assert.Equal(new[] { 2, 4, 5 }, units.Select(it => it.Id).ToArray());
        Assert.Equal(new[] { new HexCoord(1, 0), new HexCoord(0, 0), new HexCoord(2, 0) }, units.Select(it => it.CurrentTile.Position).ToArray());
        // and a refresh of the loaded game leaves that order alone
        loaded.RefreshTransientCaches();
        Assert.Equal(new[] { 2, 4, 5 }, loaded.GetCivilization(civ.CivName).Units.GetCivUnits().Select(it => it.Id).ToArray());
    }

    // T236 review: ties mixed with distinct ids, and three-plus ranked tiles (a tie broken by a partial rank would survive two-tile tests).
    [Fact]
    public void DuplicateIdsMixedWithDistinctIdsSortByIdThenTileOrder()
    {
        var game = new TestGame();
        game.MakeHexagonalMap(3);
        var civ = game.AddCiv(game.Ruleset.Nations["Babylon"]);
        var tiles = game.GameInfo.TileMap.Values.Take(8).ToList(); // map order
        var ids = new[] { 9, 5, 2, 5, 9, 5 }; // tile i gets ids[i] (i < 6); list order is reversed below
        var units = new List<Unciv.Logic.Map.MapUnits.MapUnit>();
        for (var i = ids.Length - 1; i >= 0; i--) units.Add(game.AddUnit("Warrior", civ, tiles[i]));
        foreach (var unit in units) unit.Id = ids[tiles.IndexOf(unit.CurrentTile)];

        game.GameInfo.RefreshTransientCaches();

        var order = civ.Units.GetCivUnits().Select(it => (it.Id, tiles.IndexOf(it.CurrentTile))).ToArray();
        Assert.Equal(new[] { (2, 2), (5, 1), (5, 3), (5, 5), (9, 0), (9, 4) }, order);
        Assert.Equal(order.Select(it => it.Id), LoadedIds(game, civ));
    }

    // T236 review: a save whose counter is 0 continues after the highest kept id, and numbers units of one tile military first.
    [Fact]
    public void MissingIdsContinueAfterTheHighestKeptIdWhenTheSavedCounterIsZero()
    {
        var game = new TestGame();
        game.MakeHexagonalMap(3);
        var civ = game.AddCiv(game.Ruleset.Nations["Babylon"]);
        var tile = game.GetTile(0, 0);
        game.AddUnit("Settler", civ, tile).Id = Constants.NoId;
        game.AddUnit("Warrior", civ, tile).Id = Constants.NoId;
        game.AddUnit("Warrior", civ, game.GetTile(1, 0)).Id = 7;
        game.GameInfo.CurrentPlayer = civ.CivName;
        var json = System.Text.RegularExpressions.Regex.Replace(UncivFiles.GameInfoToString(game.GameInfo, forceZip: false), "\"lastUnitId\":\\d+", "\"lastUnitId\":0");

        var loaded = UncivFiles.GameInfoFromString(json);

        var units = loaded.GetCivilization(civ.CivName).Units.GetCivUnits().ToList();
        Assert.Equal(new[] { 7, 8, 9 }, units.Select(it => it.Id).ToArray());
        Assert.Equal(new[] { "Warrior", "Warrior", "Settler" }, units.Select(it => it.Name).ToArray()); // the tile's military unit gets 8, its civilian 9
        Assert.Equal(10, loaded.GetNextUnitId());
    }
}
