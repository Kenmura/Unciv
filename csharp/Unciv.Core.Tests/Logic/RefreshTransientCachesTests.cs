using Unciv.Core.Tests.Support;
using Unciv.Logic.Files;
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
}
