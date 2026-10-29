using Unciv.Logic;
using Unciv.Logic.Archaeology;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using Unciv.Logic.Tourism;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;

namespace Unciv.Core.Tests.Logic.Archaeology;

// B032: the uniques the generated Brave New World ruleset puts on its techs reach the engine. Everything here runs on the real BNW ruleset
// and a generated map, not on a test-built ruleset.
[Collection("RulesetCache")]
public sealed class BnwTechUniquesTests : IDisposable
{
    public BnwTechUniquesTests() => RulesetCache.Clear();

    public void Dispose() => RulesetCache.Clear();

    private static GameInfo NewBnwGame()
    {
        Models.Ruleset.BnwRulesetTests.LoadBnw();
        return GameStarter.StartNewGame(
            new GameSetupInfo(
                new GameParameters
                {
                    BaseRuleset = BaseRuleset.Civ_V_BNW.FullName(),
                    NumberOfCityStates = 0,
                    Players = [new Player("Brazil", PlayerType.Human), new Player("Poland")],
                },
                new MapParameters { Type = MapType.Pangaea, Shape = MapShape.Rectangular, Seed = 3, MapSize = MapSize.Tiny }),
            5, 1000L);
    }

    private static int SiteCount(GameInfo game) =>
        game.TileMap.TileList.Count(it => it.Resource is ArchaeologyRules.AntiquitySite or ArchaeologyRules.HiddenAntiquitySite);

    [Fact]
    public void ResearchingArchaeologyInTheBnwRulesetSpawnsSitesOnTheGeneratedMapOnlyOnce()
    {
        var game = NewBnwGame();
        var a = game.Civilizations.First(it => it.IsMajorCiv());
        Assert.True(ArchaeologyRules.IsEnabled(game.Ruleset));
        Assert.Equal(0, SiteCount(game));
        Assert.False(game.ArchaeologySitesSpawned);

        a.Tech.AddTechnology("Pottery"); // an ordinary tech does not trigger
        Assert.Equal(0, SiteCount(game));

        a.Tech.AddTechnology("Archaeology");
        Assert.True(game.ArchaeologySitesSpawned);
        int sites = SiteCount(game);
        Assert.True(sites > 0);

        game.Civilizations.First(it => it.IsMajorCiv() && it != a).Tech.AddTechnology("Archaeology"); // the second civ does not spawn more
        Assert.Equal(sites, SiteCount(game));
    }

    [Fact]
    public void AResearchedTechsUniqueReachesTheTourismModifierWithoutAnyNationUnique()
    {
        var game = NewBnwGame();
        var majors = game.Civilizations.Where(it => it.IsMajorCiv()).ToList();
        var (a, b) = (majors[0], majors[1]);
        int before = TourismRules.ModifierWith(a, b);

        a.Tech.AddTechnology("The Internet");
        Assert.Equal(before + 100, TourismRules.ModifierWith(a, b));
        Assert.Equal(before, TourismRules.ModifierWith(b, a)); // only the civ that researched it
    }
}
