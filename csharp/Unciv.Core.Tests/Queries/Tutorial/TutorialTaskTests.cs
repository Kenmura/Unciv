using System.Text.Json;
using Unciv.Commands;
using Unciv.Commands.Turn;
using Unciv.Core.Tests.Support;
using Unciv.Json;
using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Logic.Files;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Unique;
using Unciv.Queries;
using Unciv.Queries.Tutorial;
using Unciv.UI.Screens.Civilopedia;
using Unciv.Utils;
using CityObject = Unciv.Logic.City.City;

namespace Unciv.Core.Tests.Queries.Tutorial;

// C054: the tutorial task query and its two commands. The query runs through the registry and json like a client would.
[Collection("RulesetCache")]
public class TutorialTaskTests
{
    private readonly TestGame testGame = new();
    private readonly Civilization player;
    private readonly Civilization other;
    private readonly QueryRegistry registry = new(UncivJson.DefaultOptions);

    public TutorialTaskTests()
    {
        testGame.MakeHexagonalMap(6);
        player = testGame.AddCiv(testGame.Ruleset.Nations["Germany"], isPlayer: true);
        other = testGame.AddCiv(testGame.Ruleset.Nations["Egypt"]);
        registry.RegisterFromAssembly(typeof(QueryRegistry).Assembly);
        testGame.Ruleset.Events.Clear();
        playerCity = testGame.AddCity(player, testGame.GetTile(0, 0)); // a civ without cities or units counts as defeated
        otherCity = testGame.AddCity(other, testGame.GetTile(3, 3));
    }

    private readonly CityObject playerCity;
    private readonly CityObject otherCity;

    private Event AddTask(string name, string choiceText = "", params string[] uniques)
    {
        var task = new Event { Name = name, Presentation = Event.PresentationType.Floating, Text = "" };
        foreach (var unique in uniques) task.Uniques.Add(unique);
        if (choiceText != "")
        {
            var choice = new EventChoice { Name = "c", Text = choiceText };
            choice.Uniques.Add($"Mark tutorial [{name}] complete");
            task.Choices.Add(choice);
        }
        testGame.Ruleset.Events[name] = task;
        return task;
    }

    private TutorialTaskResult Ask(bool show = true, Civilization? viewer = null)
    {
        var envelope = registry.ToEnvelope((viewer ?? player).CivName, new TutorialTaskQuery(show));
        var wire = JsonSerializer.Deserialize<QueryEnvelope>(JsonSerializer.Serialize(envelope, UncivJson.DefaultOptions), UncivJson.DefaultOptions)!;
        var response = registry.Execute(wire, new QueryContext(testGame.GameInfo));
        Assert.True(response.Ok, $"{response.Code}: {response.Message}");
        return response.Result!.Value.Deserialize<TutorialTaskResult>(UncivJson.DefaultOptions)!;
    }

    private static ulong Hash(GameInfo game) => StateHasher.ComputeHash(h => h.Add(UncivFiles.GameInfoToString(game, forceZip: false)));

    private const string TutorialsOn = "Only available <if tutorials are enabled>";

    [Fact]
    public void NoEventsMeansNoTask()
    {
        var result = Ask();
        Assert.Equal("", result.Event);
        Assert.Empty(result.Lines);
        Assert.Empty(result.Choices);
    }

    [Fact]
    public void TheFirstAvailableFloatingEventInRulesetOrderIsShown()
    {
        var first = AddTask("A", "", TutorialsOn, "Unavailable <if tutorial [A] is completed>");
        AddTask("B", "", TutorialsOn);
        Assert.Equal("A", Ask().Event);
        testGame.GameInfo.ClientState.AddCompletedTutorialTask("A");
        Assert.Equal("B", Ask().Event);
        Assert.Equal(Event.PresentationType.Floating, first.Presentation);
    }

    [Fact]
    public void OnlyFloatingEventsCount()
    {
        var alert = AddTask("Alert", "", TutorialsOn);
        alert.Presentation = Event.PresentationType.Alert;
        var none = AddTask("None", "", TutorialsOn);
        none.Presentation = Event.PresentationType.None;
        Assert.Equal("", Ask().Event);
    }

    [Fact]
    public void TheViewersTutorialSettingDecidesAndIsNotKeptAfterwards()
    {
        AddTask("A", "", TutorialsOn);
        Assert.Equal("", Ask(show: false).Event);
        Assert.Equal("A", Ask(show: true).Event);
        // the process-wide conditional settings are untouched by either query
        Assert.False(Conditionals.Settings.ShowTutorials);
    }

    [Fact]
    public void TutorialsOffHidesEvenAnEventWithoutTheTutorialCondition()
    {
        AddTask("Plain");
        Assert.Equal("Plain", Ask().Event);
        Assert.Equal("", Ask(show: false).Event);
    }

    [Fact]
    public void AnEventWithoutTextHasNoEmptyFirstLine()
    {
        var task = AddTask("A");
        task.CivilopediaText.Add(new FormattedLine("Body"));
        Assert.Equal(["Body"], Ask().Lines.Select(it => it.Text));
    }

    [Fact]
    public void ADefeatedViewerSeesNothing()
    {
        AddTask("A", "", TutorialsOn);
        Assert.False(player.IsDefeated());
        Assert.Equal("A", Ask().Event);
        player.Cities.ToList().ForEach(it => it.DestroyCity(true));
        player.HasEverOwnedOriginalCapital = true;
        Assert.True(player.IsDefeated());
        Assert.Equal("", Ask().Event);
    }

    [Fact]
    public void ConditionsUseTheViewersOwnCivilization()
    {
        AddTask("City", "", TutorialsOn, "Only available <when number of [Cities] is more than [1]>");
        testGame.AddCity(player, testGame.GetTile(0, 2));
        Assert.Equal("City", Ask(viewer: player).Event);
        Assert.Equal("", Ask(viewer: other).Event);
    }

    [Fact]
    public void LinesAreTheEventTextThenTheCivilopediaLines()
    {
        var task = AddTask("A", "Got it", TutorialsOn);
        task.Text = "Intro";
        task.CivilopediaText.Add(new FormattedLine("Move a unit!", centered: true));
        task.CivilopediaText.Add(new FormattedLine(extraImage: "Tutorials/Move unit", imageSize: 140f));
        task.CivilopediaText.Add(new FormattedLine("No size")); // NaN image size is not json: sent as 0
        var result = Ask();
        Assert.Equal(["Intro", "Move a unit!", "", "No size"], result.Lines.Select(it => it.Text));
        Assert.Equal([false, true, false, false], result.Lines.Select(it => it.Centered));
        Assert.Equal(0f, result.Lines[3].ImageSize);
        Assert.Equal("Tutorials/Move unit", result.Lines[2].ExtraImage);
        Assert.Equal(140f, result.Lines[2].ImageSize);
        var choice = Assert.Single(result.Choices);
        Assert.Equal(0, choice.Index);
        Assert.Equal("Got it", choice.Text);
    }

    [Fact]
    public void AnEventWhoseChoicesAllFailIsNotShown()
    {
        var task = AddTask("A", "Got it", TutorialsOn);
        task.Choices[0].Uniques.Add("Only available <when number of [Cities] is more than [5]>");
        Assert.Equal("", Ask().Event);
    }

    [Fact]
    public void TheTradeRouteTaskCountsAsCompletedWhileACityIsConnected()
    {
        AddTask("Route", "", TutorialsOn, $"Unavailable <if tutorial [{TutorialTasks.TradeRouteTask}] is completed>");
        Assert.Equal("Route", Ask().Event);
        player.Cache.CitiesConnectedToCapitalToMediums[playerCity] = []; // the capital is connected to itself: not a trade route
        Assert.Equal("Route", Ask().Event);
        var second = testGame.AddCity(player, testGame.GetTile(0, 2));
        player.Cache.CitiesConnectedToCapitalToMediums[second] = [];
        Assert.Equal("", Ask().Event);
        Assert.DoesNotContain(TutorialTasks.TradeRouteTask, testGame.GameInfo.ClientState.TutorialTasksCompleted); // a query does not write
    }

    [Fact]
    public void ACityOfAnotherCivDoesNotCompleteTheTradeRouteTask()
    {
        AddTask("Route", "", TutorialsOn, $"Unavailable <if tutorial [{TutorialTasks.TradeRouteTask}] is completed>");
        var foreignSecond = testGame.AddCity(other, testGame.GetTile(3, 0));
        player.Cache.CitiesConnectedToCapitalToMediums[otherCity] = [];
        player.Cache.CitiesConnectedToCapitalToMediums[foreignSecond] = []; // not a capital, but not the viewer's either
        Assert.Equal("Route", Ask().Event);
    }

    [Fact]
    public void QueryIsPure()
    {
        AddTask("A", "Got it", TutorialsOn);
        var before = Hash(testGame.GameInfo);
        Ask();
        Ask(show: false);
        Assert.Equal(before, Hash(testGame.GameInfo));
        Assert.Empty(testGame.GameInfo.ClientState.TutorialTasksCompleted);
    }

    [Fact]
    public void ScopedSettingsNestAndRestore()
    {
        var outer = new Fixed(true);
        var inner = new Fixed(false);
        using (Conditionals.UseSettings(outer))
        {
            Assert.True(Conditionals.Settings.ShowTutorials);
            using (Conditionals.UseSettings(inner)) Assert.False(Conditionals.Settings.ShowTutorials);
            Assert.True(Conditionals.Settings.ShowTutorials);
        }
        Assert.False(Conditionals.Settings.ShowTutorials);
        Assert.False(Conditionals.Settings.IsTutorialTaskCompleted("x"));
    }

    private sealed class Fixed(bool show) : IConditionalsSettings
    {
        public bool ShowTutorials => show;
        public bool IsTutorialTaskCompleted(string task) => false;
    }

    // ---- CompleteTutorialTask

    [Fact]
    public void CompleteRecordsOnlyKnownTasks()
    {
        AddTask("A", "", TutorialsOn, "Unavailable <if tutorial [Known] is completed>");
        var cmd = new CompleteTutorialTask { Task = "Known" };
        Assert.True(cmd.Validate(testGame.GameInfo, player.CivName).IsValid);
        var before = Hash(testGame.GameInfo);
        cmd.Apply(testGame.GameInfo, player.CivName);
        Assert.Equal(["Known"], testGame.GameInfo.ClientState.TutorialTasksCompleted);
        Assert.Equal(before, Hash(testGame.GameInfo)); // client state is not part of the game hash
        Assert.Equal(CommandErrors.InvalidTarget, new CompleteTutorialTask { Task = "Nope" }.Validate(testGame.GameInfo, player.CivName).Code);
        Assert.Equal(CommandErrors.InvalidTarget, new CompleteTutorialTask { Task = "" }.Validate(testGame.GameInfo, player.CivName).Code);
        Assert.False(new CompleteTutorialTask { Task = "Known" }.Validate(testGame.GameInfo, "server").IsValid);
    }

    [Theory]
    [InlineData("Create a trade route")]
    [InlineData("Conquer a city")]
    [InlineData("Construct an improvement")]
    public void EngineCompletedTasksAreKnownWithoutEvents(string task) =>
        Assert.True(new CompleteTutorialTask { Task = task }.Validate(testGame.GameInfo, player.CivName).IsValid);

    [Fact]
    public void ChoiceMarkersAndChoiceNamesCountAsKnown()
    {
        AddTask("A", "Got it", TutorialsOn); // its choice "Mark tutorial [A] complete"
        Assert.True(new CompleteTutorialTask { Task = "A" }.Validate(testGame.GameInfo, player.CivName).IsValid);
        var task = AddTask("B", "", TutorialsOn);
        var choice = new EventChoice { Name = "x", Text = "x" };
        choice.Uniques.Add("Unavailable <if tutorial [InChoice] is completed>");
        task.Choices.Add(choice);
        Assert.True(new CompleteTutorialTask { Task = "InChoice" }.Validate(testGame.GameInfo, player.CivName).IsValid);
    }

    [Fact]
    public void ARulesetWithoutAnyMentionKnowsNothing()
    {
        AddTask("A", "", TutorialsOn, "Unavailable <if tutorial [Other] is completed>");
        Assert.False(new CompleteTutorialTask { Task = "A" }.Validate(testGame.GameInfo, player.CivName).IsValid);
    }

    // ---- ChooseTutorialTaskChoice

    [Fact]
    public void ChoosingTriggersTheChoiceForTheActor()
    {
        AddTask("A", "Got it", TutorialsOn, "Unavailable <if tutorial [A] is completed>");
        var cmd = new ChooseTutorialTaskChoice { Event = "A", Choice = 0 };
        Assert.True(cmd.Validate(testGame.GameInfo, player.CivName).IsValid);
        cmd.Apply(testGame.GameInfo, player.CivName);
        Assert.Contains("A", testGame.GameInfo.ClientState.TutorialTasksCompleted);
        Assert.Equal("", Ask().Event);
        Assert.Equal(CommandErrors.StaleReference, cmd.Validate(testGame.GameInfo, player.CivName).Code);
    }

    [Fact]
    public void ChoosingRejectsWrongEventsAndIndexes()
    {
        AddTask("A", "Got it", TutorialsOn);
        AddTask("B", "Got it", TutorialsOn);
        var game = testGame.GameInfo;
        Assert.Equal(CommandErrors.StaleReference, new ChooseTutorialTaskChoice { Event = "B", Choice = 0 }.Validate(game, player.CivName).Code);
        Assert.Equal(CommandErrors.StaleReference, new ChooseTutorialTaskChoice { Event = "", Choice = 0 }.Validate(game, player.CivName).Code);
        Assert.Equal(CommandErrors.InvalidTarget, new ChooseTutorialTaskChoice { Event = "A", Choice = 1 }.Validate(game, player.CivName).Code);
        Assert.Equal(CommandErrors.InvalidTarget, new ChooseTutorialTaskChoice { Event = "A", Choice = -1 }.Validate(game, player.CivName).Code);
        Assert.False(new ChooseTutorialTaskChoice { Event = "A", Choice = 0 }.Validate(game, "server").IsValid);
    }

    [Fact]
    public void ChoosingAnEventWithNoChoicesIsRefused()
    {
        AddTask("A", "", TutorialsOn);
        Assert.Equal(CommandErrors.InvalidTarget, new ChooseTutorialTaskChoice { Event = "A", Choice = 0 }.Validate(testGame.GameInfo, player.CivName).Code);
    }

    [Fact]
    public void ChoosingWithNoCurrentTaskIsRefused()
    {
        Assert.Equal(CommandErrors.StaleReference, new ChooseTutorialTaskChoice { Event = "A", Choice = 0 }.Validate(testGame.GameInfo, player.CivName).Code);
    }

    [Fact]
    public void ChoosingAnEventWhoseChoicesAllFailIsRefused()
    {
        var task = AddTask("A", "Got it", TutorialsOn);
        task.Choices[0].Uniques.Add("Only available <when number of [Cities] is more than [5]>");
        Assert.Equal(CommandErrors.StaleReference, new ChooseTutorialTaskChoice { Event = "A", Choice = 0 }.Validate(testGame.GameInfo, player.CivName).Code);
    }
}
