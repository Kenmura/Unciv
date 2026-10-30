using System.Text.RegularExpressions;
using Unciv.Core.Tests.Support;
using Unciv.Logic.City.Managers;
using Unciv.Logic.Files;
using Unciv.Logic.Map.Tiles;

namespace Unciv.Core.Tests.Logic;

// T293: moving a city to another civilization must hand the road owner of its tiles over as well, so that a live game and a reload of its save
// agree (loading re-assigns every city tile to the city's civ). The BNW 300-turn soak found a tile whose roadOwner differed after turn 250.
[Collection("RulesetCache")]
public class CityTransferRoadOwnerTests
{
    private static List<string> RoadOwners(string saved) =>
        Regex.Matches(saved, "\"roadOwner\":\"[^\"]*\"").Select(it => it.Value).ToList();

    [Fact]
    public void RoadOwnerOfCityTilesFollowsTheCityToItsNewCivAndSurvivesReload()
    {
        var game = new TestGame();
        game.MakeHexagonalMap(8);
        var oldCiv = game.AddCiv(game.Ruleset.Nations["Babylon"], isPlayer: true);
        var newCiv = game.AddCiv(game.Ruleset.Nations["Greece"]);
        game.AddCiv(game.Ruleset.Nations["Rome"]); // keeps the game going after Babylon loses its only city
        var city = game.AddCity(oldCiv, game.GetTile(0, 0));
        var roadTile = city.GetTiles().First(it => !it.IsCityCenter());
        roadTile.SetRoadStatus(RoadStatus.Road, oldCiv);
        Assert.Same(oldCiv, roadTile.GetRoadOwner());

        new CityConquestFunctions(city).MoveToCiv(newCiv);

        Assert.Same(newCiv, roadTile.GetRoadOwner());
        var saved = UncivFiles.GameInfoToString(game.GameInfo, forceZip: false);
        var live = RoadOwners(saved);
        Assert.Contains(live, it => it.Contains(newCiv.CivId, StringComparison.Ordinal));
        Assert.DoesNotContain(live, it => it.Contains(oldCiv.CivId, StringComparison.Ordinal));
        var reloaded = RoadOwners(UncivFiles.GameInfoToString(UncivFiles.GameInfoFromString(saved), forceZip: false));
        Assert.Equal(live, reloaded);
    }
}
