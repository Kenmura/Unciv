using Unciv.Core.Tests.Support;
using Unciv.Logic.Files;

namespace Unciv.Core.Tests.Logic;

// T236: calling MakeCivilizationsMeet directly with a city state must leave the live tiles' exploredBy equal to a reload of the saved game.
// (T234 observed a difference; with real nations it does not reproduce, so these are regression tests of the property, not of a fix.)
[Collection("RulesetCache")]
public class MeetCityStateReloadTests
{
    private static List<string> ExploredBy(string saved) =>
        System.Text.RegularExpressions.Regex.Matches(saved, "\"exploredBy\":\\[[^\\]]*\\]").Select(it => it.Value).ToList();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExploredByAgreesBetweenLiveAndReloadAfterMeetingACityState(bool humanMajor)
    {
        var game = new TestGame();
        game.MakeHexagonalMap(8);
        var major = game.AddCiv(game.Ruleset.Nations["Babylon"], isPlayer: humanMajor);
        var bystander = game.AddCiv(game.Ruleset.Nations["Greece"], isPlayer: !humanMajor); // a game needs a human current player either way
        var state = game.AddCiv(game.Ruleset.Nations["Geneva"]);
        game.AddCity(major, game.GetTile(7, 0));
        game.AddCity(bystander, game.GetTile(-7, 0));
        var capitalTile = game.AddCity(state, game.GetTile(0, 0)).GetCenterTile();
        var human = humanMajor ? major : bystander;
        game.GameInfo.CurrentPlayer = human.CivName;
        game.GameInfo.CurrentPlayerCiv = human;
        Assert.False(capitalTile.IsExplored(major), "the capital is far outside the major civ's sight before the meeting");

        major.DiplomacyFunctions.MakeCivilizationsMeet(state);

        Assert.True(capitalTile.IsExplored(major), "meeting a city state reveals its capital tile");
        Assert.False(capitalTile.IsExplored(bystander));
        var live = ExploredBy(UncivFiles.GameInfoToString(game.GameInfo, forceZip: false));
        var loaded = UncivFiles.GameInfoFromString(UncivFiles.GameInfoToString(game.GameInfo, forceZip: false));
        var reloaded = ExploredBy(UncivFiles.GameInfoToString(loaded, forceZip: false));
        Assert.Contains(live, it => it.Contains("Babylon", StringComparison.Ordinal) && it.Contains("Geneva", StringComparison.Ordinal)); // the capital's entry
        Assert.Equal(live, reloaded);
    }
}
