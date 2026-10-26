using System.Text.Json;
using Unciv.Core.Tests.Support;
using Unciv.Json;
using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Logic.Files;
using Unciv.Queries;
using Unciv.Queries.Victory;
using Unciv.UI.Screens.VictoryScreen;
using Unciv.Utils;

namespace Unciv.Core.Tests.Queries.Victory;

// T198: the victory screen queries. Every query runs through the registry and json like a client would, and none may change the saved state.
[Collection("RulesetCache")]
public class VictoryQueryTests
{
    private readonly TestGame testGame = new();
    private readonly Civilization player;
    private readonly Civilization enemy;
    private readonly Civilization stranger;
    private readonly QueryRegistry registry = new(UncivJson.DefaultOptions);

    public VictoryQueryTests()
    {
        testGame.MakeHexagonalMap(6);
        player = testGame.AddCiv(testGame.Ruleset.Nations["Germany"], isPlayer: true);
        enemy = testGame.AddCiv(testGame.Ruleset.Nations["Egypt"]);
        stranger = testGame.AddCiv(testGame.Ruleset.Nations["Rome"]);
        registry.RegisterFromAssembly(typeof(QueryRegistry).Assembly);
        testGame.AddCity(player, testGame.GetTile(0, 0));
        testGame.AddCity(enemy, testGame.GetTile(3, 3));
        testGame.AddCity(stranger, testGame.GetTile(-3, -3));
        foreach (var civ in new[] { player, enemy, stranger }) civ.HasEverOwnedOriginalCapital = true; // else a civ without units counts as defeated
        player.Diplomacy.Remove(stranger.CivId); // founding a city nearby may have introduced them
        stranger.Diplomacy.Remove(player.CivId);
        player.DiplomacyFunctions.MakeCivilizationsMeet(enemy);
        var parameters = testGame.GameInfo.GameParameters;
        parameters.ShowCivilizationStats = true;
        parameters.ShowRankings = true;
        parameters.ShowDemographics = true;
        parameters.ShowCharts = true;
        parameters.HideOtherCivilizationStats = false;
    }

    private QueryResponse Execute(Civilization viewer, IQuery query)
    {
        var envelope = registry.ToEnvelope(viewer.CivName, query);
        var wire = JsonSerializer.Deserialize<QueryEnvelope>(JsonSerializer.Serialize(envelope, UncivJson.DefaultOptions), UncivJson.DefaultOptions)!;
        return registry.Execute(wire, new QueryContext(testGame.GameInfo));
    }

    private T Ask<T>(IQuery<T> query, Civilization? viewer = null) where T : notnull
    {
        var response = Execute(viewer ?? player, query);
        Assert.True(response.Ok, $"{response.Code}: {response.Message}");
        return response.Result!.Value.Deserialize<T>(UncivJson.DefaultOptions)!;
    }

    private string Fail(IQuery query, Civilization? viewer = null)
    {
        var response = Execute(viewer ?? player, query);
        Assert.False(response.Ok);
        return response.Code!;
    }

    private static ulong Hash(GameInfo game) => StateHasher.ComputeHash(h => h.Add(UncivFiles.GameInfoToString(game, forceZip: false)));

    private void SetVictories(params string[] names)
    {
        testGame.GameInfo.GameParameters.VictoryTypes.Clear();
        testGame.GameInfo.GameParameters.VictoryTypes.AddRange(names);
    }

    private static Dictionary<RankingType, int> Stats(int value) => Enum.GetValues<RankingType>().ToDictionary(it => it, _ => value);

    // ---- victory progress

    [Fact]
    public void ProgressListsOwnMilestonesWithStatusAndNumbers()
    {
        SetVictories("Scientific");
        var entry = Assert.Single(Ask(new VictoryProgressQuery()).Victories);
        Assert.Equal("Scientific", entry.Name);
        Assert.Equal(entry.TotalMilestones, entry.Own.Count);
        Assert.Equal("Partially", entry.Own[0].Status); // nothing is complete, so the first one is under way
        Assert.All(entry.Own.Skip(1), it => Assert.Equal("Incomplete", it.Status));
        var parts = entry.Own.Single(it => it.Type == "AddedSSPartsInCapital");
        Assert.Equal(0, parts.Done);
        Assert.True(parts.Total > 0);

        player.VictoryManager.CurrentsSpaceshipParts.Add(testGame.Ruleset.Victories["Scientific"].RequiredSpaceshipParts[0], 1);
        var after = Ask(new VictoryProgressQuery()).Victories[0].Own.Single(it => it.Type == "AddedSSPartsInCapital");
        Assert.Equal(1, after.Done);
        Assert.Equal(parts.Total, after.Total);
    }

    [Fact]
    public void ProgressNumbersFollowEachMilestoneType()
    {
        testGame.Ruleset.Victories["Custom"] = new Unciv.Models.Ruleset.Victory
        {
            Name = "Custom",
            RequiredSpaceshipParts = ["Apollo Program"],
            Milestones =
            [
                "Complete [3] Policy branches", "Have at least [2] [Cities]", "Capture all capitals", "Destroy all players",
                "Have more [Cities] than each player's [Cities]", "Become the world religion", "Build [Palace]",
            ],
        };
        SetVictories("Custom");
        testGame.AddCity(player, testGame.GetTile(1, 0));
        var own = Assert.Single(Ask(new VictoryProgressQuery()).Victories).Own;
        MilestoneEntry Of(string type) => own.Single(it => it.Type == type);
        Assert.Equal((0, 3), (Of("CompletePolicyBranches").Done, Of("CompletePolicyBranches").Total));
        Assert.Equal((2, 2), (Of("HaveCountable").Done, Of("HaveCountable").Total));
        Assert.Equal("Completed", Of("HaveCountable").Status);
        Assert.Equal((1, 3), (Of("CaptureAllCapitals").Done, Of("CaptureAllCapitals").Total));
        Assert.Equal((0, 2), (Of("DestroyAllPlayers").Done, Of("DestroyAllPlayers").Total));
        Assert.Equal((2, 2), (Of("MoreCountableThanEachPlayer").Done, Of("MoreCountableThanEachPlayer").Total)); // 2 cities beat the 1 of each rival
        Assert.Equal((0, 2), (Of("WorldReligion").Done, Of("WorldReligion").Total));
        Assert.Null(Of("BuiltBuilding").Done);

        testGame.AddReligion(player);
        Assert.InRange(Ask(new VictoryProgressQuery()).Victories[0].Own.Single(it => it.Type == "WorldReligion").Done!.Value, 0, 1); // pantheon counts 1, a religion nobody follows 0
    }

    [Fact]
    public void ProgressHidesCivCountsWhenTheNumberOfPlayersIsRandom()
    {
        testGame.Ruleset.Victories["Custom"] = new Unciv.Models.Ruleset.Victory { Name = "Custom", Milestones = ["Destroy all players"] };
        SetVictories("Custom");
        testGame.GameInfo.GameParameters.RandomNumberOfPlayers = true;
        testGame.GameInfo.GameParameters.MaxNumberOfPlayers = 9;
        var milestone = Assert.Single(Assert.Single(Ask(new VictoryProgressQuery()).Victories).Own);
        Assert.Null(milestone.Total);
        Assert.NotNull(milestone.Done);
    }

    [Fact]
    public void ProgressGlobalListsKnownMajorCivsWorstFirstAndOmitsUnmetOnes()
    {
        SetVictories("Domination");
        var entry = Assert.Single(Ask(new VictoryProgressQuery()).Victories);
        Assert.Equal(2, entry.Civs.Count); // T240: the unmet civ gets no anonymous row either (Kotlin shows one)
        Assert.Contains(entry.Civs, it => it.Civ.IsViewer && it.Civ.CivName == player.CivName);
        Assert.Contains(entry.Civs, it => it.Civ.CivName == enemy.CivName);
        Assert.DoesNotContain(entry.Civs, it => it.Civ.CivName == null);
        Assert.DoesNotContain(stranger.CivName, JsonSerializer.Serialize(entry, UncivJson.DefaultOptions));
    }

    // T240 leak tests: nothing about a civ the viewer has not met may depend on, or appear in, the answer.
    [Fact]
    public void ProgressDoesNotChangeWhenAnUnmetCivsStateChanges()
    {
        SetVictories("Domination", "Scientific");
        var before = JsonSerializer.Serialize(Ask(new VictoryProgressQuery()), UncivJson.DefaultOptions);
        stranger.VictoryManager.CurrentsSpaceshipParts.Add("Apollo Program", 3);
        stranger.Policies.AdoptedPolicies.Add("Liberty");
        Assert.Equal(before, JsonSerializer.Serialize(Ask(new VictoryProgressQuery()), UncivJson.DefaultOptions));
    }

    [Fact]
    public void ProgressListsAnUnmetCivOnlyOnceItIsMet()
    {
        SetVictories("Domination");
        Assert.Equal(2, Ask(new VictoryProgressQuery()).Victories[0].Civs.Count);
        player.DiplomacyFunctions.MakeCivilizationsMeet(stranger);
        var civs = Ask(new VictoryProgressQuery()).Victories[0].Civs;
        Assert.Equal(3, civs.Count);
        Assert.Contains(civs, it => it.Civ.CivName == stranger.CivName);
    }

    [Fact]
    public void ProgressHidesRivalsWhenTheGameRestrictsToOwnStats()
    {
        SetVictories("Domination");
        testGame.GameInfo.GameParameters.HideOtherCivilizationStats = true;
        var civs = Assert.Single(Ask(new VictoryProgressQuery()).Victories).Civs;
        var own = Assert.Single(civs);
        Assert.True(own.Civ.IsViewer);
    }

    [Theory]
    [InlineData("Capture all capitals")]
    [InlineData("Destroy all players")]
    [InlineData("Have more [Cities] than each player's [Cities]")]
    public void ProgressHiddenCivCountFollowsTheViewerNotTheListedCiv(string milestone)
    {
        testGame.Ruleset.Victories["Custom"] = new Unciv.Models.Ruleset.Victory { Name = "Custom", Milestones = [milestone] };
        SetVictories("Custom");
        testGame.GameInfo.GameParameters.RandomNumberOfPlayers = true;
        testGame.GameInfo.GameParameters.MaxNumberOfPlayers = 3;
        enemy.DiplomacyFunctions.MakeCivilizationsMeet(stranger);
        Assert.True(player.ShouldHideCivCount());
        Assert.False(enemy.ShouldHideCivCount()); // the enemy knows every civ, the viewer does not
        var civs = Assert.Single(Ask(new VictoryProgressQuery()).Victories).Civs;
        var rival = Assert.Single(civs, it => it.Civ.CivName == enemy.CivName);
        Assert.Null(rival.Next!.Total); // hidden for the viewer, so hidden in the rival's row too
    }

    [Fact]
    public void ProgressRunsRemotelyAndLeaksNothingOfUnmetCivs()
    {
        SetVictories("Domination");
        var host = new Unciv.Net.Remote.RemoteQueryHost(registry, Unciv.Net.Remote.RemoteQueryPolicy.Default);
        var query = new Unciv.Net.Query { QueryId = 1, QueryType = "victory.Progress", Payload = JsonDocument.Parse("{}").RootElement };
        var result = host.Handle("s", player.CivName, query, new QueryContext(testGame.GameInfo), 1, false, 0);
        Assert.True(result.Ok);
        var json = result.Payload!.Value.GetRawText();
        Assert.DoesNotContain(stranger.CivName, json);
        Assert.Equal(2, result.Payload.Value.GetProperty("Victories")[0].GetProperty("Civs").GetArrayLength());
    }

    [Fact]
    public void DefeatedCivsAreNamedAndSortLast()
    {
        SetVictories("Domination");
        testGame.GameInfo.Turns = 1;
        stranger.HasEverOwnedOriginalCapital = true;
        stranger.Cities = [];
        Assert.True(stranger.IsDefeated());
        var civs = Assert.Single(Ask(new VictoryProgressQuery()).Victories).Civs;
        Assert.Equal(stranger.CivName, civs[^1].Civ.CivName);
        Assert.True(civs[^1].Civ.IsDefeated);
        var ranking = Ask(new RankingsQuery())[0].Entries;
        Assert.Equal(stranger.CivName, ranking[^1].Civ.CivName);
        Assert.Equal(0, ranking[^1].Value);
    }

    [Fact]
    public void ProgressOfSpectatorHasNoOwnPage()
    {
        SetVictories("Domination");
        var spectator = testGame.AddCiv(testGame.Ruleset.Nations[Constants.Spectator]);
        var entry = Assert.Single(Ask(new VictoryProgressQuery(), spectator).Victories);
        Assert.Empty(entry.Own);
        Assert.Equal(3, entry.Civs.Count); // a spectator knows every civ
    }

    // ---- scores

    [Fact]
    public void ScoresReturnTheBreakdownInEngineOrderAndTheTotal()
    {
        var result = Ask(new ScoresQuery());
        Assert.Equal(player.CalculateScoreBreakdown().Select(it => it.Key), result.Breakdown.Select(it => it.Name));
        Assert.Equal(player.CalculateTotalScore(), result.Total);
        Assert.Equal(result.Breakdown.Sum(it => it.Value), result.Total, 9);
    }

    [Fact]
    public void ScoresAreEmptyForMinorCivs()
    {
        var cityState = testGame.AddCiv(testGame.Ruleset.Nations.Values.First(it => it.IsCityState));
        var result = Ask(new ScoresQuery(), cityState);
        Assert.Empty(result.Breakdown);
        Assert.Equal(0.0, result.Total);
    }

    // ---- rankings

    [Fact]
    public void RankingsSortBestFirstAndAnonymizeUnmetCivs()
    {
        enemy.AddGold(500 - enemy.Gold);
        player.AddGold(100 - player.Gold);
        stranger.AddGold(300 - stranger.Gold);
        var columns = Ask(new RankingsQuery());
        Assert.Equal(RankingTypes.FilteredEntries(testGame.GameInfo.GameParameters).Select(it => it.ToString()), columns.Select(it => it.Type));
        var gold = columns.Single(it => it.Type == "Gold");
        Assert.Equal(["Gold", "Gold"], [gold.Label, gold.Type]);
        Assert.Equal([500, 300, 100], gold.Entries.Select(it => it.Value));
        Assert.Equal([enemy.CivName, null, player.CivName], gold.Entries.Select(it => it.Civ.CivName));
        Assert.True(gold.Entries[2].Civ.IsViewer);
        Assert.DoesNotContain(stranger.CivName, JsonSerializer.Serialize(columns, UncivJson.DefaultOptions));
    }

    [Fact]
    public void RankingsOnlyListTheViewerWhenOtherStatsAreHidden()
    {
        testGame.GameInfo.GameParameters.HideOtherCivilizationStats = true;
        var entries = Ask(new RankingsQuery())[0].Entries;
        Assert.Equal(player.CivName, Assert.Single(entries).Civ.CivName);

        testGame.GameInfo.VictoryData = new VictoryData(enemy, "Domination", 5); // after the game ends everything is shown
        Assert.Equal(3, Ask(new RankingsQuery())[0].Entries.Count);
    }

    [Fact]
    public void RankingsAreNotAvailableWhenTheTabIsOff()
    {
        testGame.GameInfo.GameParameters.ShowRankings = false;
        Assert.Equal(VictoryQueryCodes.NotAvailable, Fail(new RankingsQuery()));
        Assert.NotEmpty(Ask(new RankingsQuery(), testGame.AddCiv(testGame.Ruleset.Nations[Constants.Spectator])));
        testGame.GameInfo.GameParameters.ShowRankings = true;
        testGame.GameInfo.GameParameters.ShowCivilizationStats = false;
        Assert.Equal(VictoryQueryCodes.NotAvailable, Fail(new RankingsQuery()));
    }

    [Fact]
    public void AdditionalRankingTypesFollowTheGameParameter()
    {
        testGame.GameInfo.GameParameters.ShowAdditionalRankingTypes = false;
        Assert.DoesNotContain(Ask(new RankingsQuery()), it => it.Type == "TilesExplored");
        testGame.GameInfo.GameParameters.ShowAdditionalRankingTypes = true;
        Assert.Contains(Ask(new RankingsQuery()), it => it.Type == "TilesExplored");
    }

    // ---- demographics

    [Fact]
    public void DemographicsUseTheTurnSnapshotNotLiveValues()
    {
        testGame.GameInfo.Turns = 4;
        player.StatsHistory[3] = Stats(10);
        enemy.StatsHistory[3] = Stats(30);
        stranger.StatsHistory[4] = Stats(20);
        enemy.AddGold(9999 - enemy.Gold); // a mid-turn change must not leak
        var gold = Ask(new DemographicsQuery()).Single(it => it.Type == "Gold");
        Assert.Equal(2 + 1, gold.Rank);
        Assert.Equal(10, gold.Value!.Value);
        Assert.Equal(30, gold.Best!.Value);
        Assert.Equal(enemy.CivName, gold.Best.Civ.CivName);
        Assert.Equal(10, gold.Worst!.Value);
        Assert.Equal(20, gold.Average);
    }

    [Fact]
    public void DemographicsFallBackToLiveValuesAndRoundAverageHalfUp()
    {
        player.AddGold(1 - player.Gold);
        enemy.AddGold(2 - enemy.Gold);
        stranger.AddGold(0 - stranger.Gold);
        var gold = Ask(new DemographicsQuery()).Single(it => it.Type == "Gold");
        Assert.Equal(1, gold.Average); // 3 / 3
        enemy.AddGold(3 - enemy.Gold);
        Assert.Equal(1, Ask(new DemographicsQuery()).Single(it => it.Type == "Gold").Average); // 4 / 3 = 1.33
        stranger.AddGold(1 - stranger.Gold);
        Assert.Equal(2, Ask(new DemographicsQuery()).Single(it => it.Type == "Gold").Average); // 5 / 3 = 1.67
        testGame.GameInfo.Turns = 1;
        player.StatsHistory[0] = Stats(1);
        enemy.StatsHistory[0] = Stats(2);
        stranger.StatsHistory[0] = Stats(2);
        Assert.Equal(2, Ask(new DemographicsQuery()).Single(it => it.Type == "Gold").Average); // 5 / 3 from the latest snapshots
    }

    [Fact]
    public void DemographicsOfSpectatorHaveNoOwnValue()
    {
        var spectator = testGame.AddCiv(testGame.Ruleset.Nations[Constants.Spectator]);
        var column = Ask(new DemographicsQuery(), spectator)[0];
        Assert.Null(column.Value);
        Assert.Equal(0, column.Rank);
        Assert.NotNull(column.Best);
    }

    [Fact]
    public void DemographicsAreNotAvailableWhenTheTabIsOff()
    {
        testGame.GameInfo.GameParameters.ShowDemographics = false;
        Assert.Equal(VictoryQueryCodes.NotAvailable, Fail(new DemographicsQuery()));
    }

    // ---- history

    [Fact]
    public void HistoryListsRecordedTurnsThenTheLiveValue()
    {
        testGame.GameInfo.Turns = 5;
        player.StatsHistory[1] = Stats(1);
        player.StatsHistory[3] = Stats(3);
        enemy.StatsHistory[2] = Stats(7);
        var result = Ask(new HistoryQuery("Gold"));
        Assert.Equal("Gold", result.Category);
        Assert.Equal(2, result.Series.Count); // stranger has no history
        var own = result.Series.Single(it => it.Civ.IsViewer);
        Assert.Equal([(1, 1), (3, 3), (5, player.GetStatForRanking(RankingType.Gold))], own.Points.Select(it => (it.Turn, it.Value)));

        var zoomed = Ask(new HistoryQuery("Gold", FromTurn: 2, ToTurn: 4));
        Assert.Equal([(3, 3)], zoomed.Series.Single(it => it.Civ.IsViewer).Points.Select(it => (it.Turn, it.Value)));
        Assert.Equal(enemy.CivName, Assert.Single(zoomed.Series, it => !it.Civ.IsViewer).Civ.CivName);
    }

    [Fact]
    public void HistoryZoomIsInclusiveAndDropsTheLivePointOutsideTheRange()
    {
        testGame.GameInfo.Turns = 5;
        player.StatsHistory[2] = Stats(2);
        player.StatsHistory[3] = Stats(3);
        List<int> Turns(HistoryQuery q) => Ask(q).Series.SelectMany(it => it.Points).Select(it => it.Turn).ToList();
        Assert.Equal([2, 3], Turns(new HistoryQuery("Gold", FromTurn: 2, ToTurn: 3)));
        Assert.Equal([2, 3, 5], Turns(new HistoryQuery("Gold", FromTurn: 2, ToTurn: int.MaxValue)));
        Assert.Empty(Turns(new HistoryQuery("Gold", FromTurn: 4, ToTurn: 3)));
        Assert.Equal([2, 3, 5], Turns(new HistoryQuery("Gold", FromTurn: int.MinValue)));
    }

    [Fact]
    public void RankingTiesBreakByCivName()
    {
        var gold = Ask(new RankingsQuery()).Single(it => it.Type == "Gold");
        Assert.Equal(gold.Entries.Select(it => it.Civ.CivName).Where(it => it != null).Order(StringComparer.Ordinal), gold.Entries.Select(it => it.Civ.CivName).Where(it => it != null));
    }

    [Fact]
    public void DefeatedViewerSeesEveryNameAndNoOwnValue()
    {
        player.Cities = [];
        Assert.True(player.IsDefeated());
        var column = Ask(new DemographicsQuery())[0];
        Assert.True(column.Rank > 0);
        Assert.Null(column.Value);
        var civs = Assert.Single(Ask(new RankingsQuery()).Take(1)).Entries;
        Assert.DoesNotContain(civs, it => it.Civ.CivName == null);
    }

    [Fact]
    public void HistoryRejectsUnknownCategoriesAndHiddenCharts()
    {
        Assert.Equal(VictoryQueryCodes.BadRequest, Fail(new HistoryQuery("Nonsense")));
        Assert.Equal(VictoryQueryCodes.BadRequest, Fail(new HistoryQuery("99")));
        Assert.Equal(VictoryQueryCodes.BadRequest, Fail(new HistoryQuery("1")));
        Assert.Equal(VictoryQueryCodes.BadRequest, Fail(new HistoryQuery(" Gold")));
        testGame.GameInfo.GameParameters.ShowAdditionalRankingTypes = false;
        Assert.Equal(VictoryQueryCodes.BadRequest, Fail(new HistoryQuery("TilesExplored")));
        testGame.GameInfo.GameParameters.ShowCharts = false;
        Assert.Equal(VictoryQueryCodes.NotAvailable, Fail(new HistoryQuery()));
    }

    [Fact]
    public void HistoryHidesOtherCivsWhenOtherStatsAreHidden()
    {
        testGame.GameInfo.GameParameters.HideOtherCivilizationStats = true;
        player.StatsHistory[0] = Stats(1);
        enemy.StatsHistory[0] = Stats(1);
        Assert.True(Assert.Single(Ask(new HistoryQuery()).Series).Civ.IsViewer);
    }

    [Fact]
    public void GameOverIsPlayingUntilSomeoneWinsOrTheViewerIsDestroyed()
    {
        Assert.Equal(new GameOverResult("Playing", null, null, 0, ""), Ask(new GameOverQuery()));
    }

    [Fact]
    public void GameOverNamesTheWinnerAndPicksTheVictoryOrDefeatText()
    {
        var victory = testGame.Ruleset.Victories.Values.First();
        testGame.GameInfo.VictoryData = new VictoryData(enemy.CivName, victory.Name, 12);
        var asWinner = Ask(new GameOverQuery(), enemy);
        Assert.Equal(new GameOverResult("Won", enemy.CivName, victory.Name, 12, victory.VictoryString), asWinner);
        var asLoser = Ask(new GameOverQuery());
        Assert.Equal(new GameOverResult("Lost", enemy.CivName, victory.Name, 12, victory.DefeatString), asLoser);
    }

    [Fact]
    public void GameOverUsesTheDefaultTextsForAVictoryTheRulesetDoesNotDefine()
    {
        testGame.GameInfo.VictoryData = new VictoryData(player.CivName, "Mystery", 3);
        Assert.Equal(new Unciv.Models.Ruleset.Victory().VictoryString, Ask(new GameOverQuery()).Text);
    }

    [Fact]
    public void GameOverEndsWithOneMoreTurnButADefeatedViewerStaysDefeated()
    {
        testGame.GameInfo.VictoryData = new VictoryData(enemy.CivName, "Domination", 5);
        testGame.GameInfo.OneMoreTurnMode = true;
        Assert.Equal("Playing", Ask(new GameOverQuery()).Status);
        player.HasEverOwnedOriginalCapital = true;
        foreach (var city in player.Cities.ToList()) city.DestroyCity(overrideSafeties: true);
        var defeated = Ask(new GameOverQuery());
        Assert.Equal("Defeated", defeated.Status);
        Assert.Null(defeated.Winner);
        Assert.Equal(new Unciv.Models.Ruleset.Victory().DefeatString, defeated.Text);
    }

    [Fact]
    public void NoVictoryQueryChangesTheState()
    {
        player.StatsHistory[0] = Stats(1);
        var before = Hash(testGame.GameInfo);
        IQuery[] queries = [new VictoryProgressQuery(), new ScoresQuery(), new RankingsQuery(), new DemographicsQuery(), new HistoryQuery(), new GameOverQuery()];
        foreach (var query in queries)
        {
            var response = Execute(player, query);
            Assert.True(response.Ok, $"{query.Type}: {response.Code} {response.Message}");
            Assert.Equal(before, Hash(testGame.GameInfo));
        }
        Assert.All(queries, it => Assert.Contains(it.Type, registry.RegisteredTypes));
    }
}
