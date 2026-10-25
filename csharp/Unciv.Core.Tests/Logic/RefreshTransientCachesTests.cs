using Unciv.Core.Tests.Support;
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

    [Fact]
    public void UnitListsAreKeptInCreationIdOrder()
    {
        var game = new TestGame();
        game.MakeHexagonalMap(3);
        var civ = game.AddCiv();
        var first = game.AddUnit("Warrior", civ, game.GetTile(2, 0));
        var second = game.AddUnit("Warrior", civ, game.GetTile(0, 0)); // earlier in tile order than the first unit
        first.Id = 5;
        second.Id = 9;

        game.GameInfo.RefreshTransientCaches();

        Assert.Equal(new[] { 5, 9 }, civ.Units.GetCivUnits().Select(it => it.Id).ToArray());
    }
}
