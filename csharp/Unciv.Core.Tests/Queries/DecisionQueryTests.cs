using System.Text.Json;
using Unciv.Commands.Turn;
using Unciv.Commands.Units;
using Unciv.Core.Tests.Support;
using Unciv.Json;
using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Managers;
using Unciv.Logic.Files;
using Unciv.Models;
using Unciv.Models.Ruleset;
using Unciv.Queries;
using Unciv.Queries.Empire;
using Unciv.Queries.Units;
using Unciv.Utils;

namespace Unciv.Core.Tests.Queries;

// T220: the queries a client needs to answer blocking decisions. Each runs through the registry and json like a client, must not change the state,
// and must agree with what the answering command accepts.
[Collection("RulesetCache")]
public class DecisionQueryTests
{
    private readonly TestGame testGame = new();
    private readonly Civilization player;
    private readonly Civilization enemy;
    private readonly Civilization stranger;
    private readonly Civilization cityState;
    private readonly QueryRegistry registry = new(UncivJson.DefaultOptions);

    public DecisionQueryTests()
    {
        testGame.MakeHexagonalMap(6);
        player = testGame.AddCiv(testGame.Ruleset.Nations["Germany"], isPlayer: true);
        enemy = testGame.AddCiv(testGame.Ruleset.Nations["Egypt"]);
        stranger = testGame.AddCiv(testGame.Ruleset.Nations["Rome"]);
        cityState = testGame.AddCiv(cityStateType: "Cultured");
        registry.RegisterFromAssembly(typeof(QueryRegistry).Assembly);
        testGame.AddCity(player, testGame.GetTile(0, 0));
        testGame.AddCity(enemy, testGame.GetTile(3, 3));
        testGame.AddCity(stranger, testGame.GetTile(-3, -3));
        testGame.AddCity(cityState, testGame.GetTile(-3, 3));
        player.DiplomacyFunctions.MakeCivilizationsMeet(enemy);
        player.DiplomacyFunctions.MakeCivilizationsMeet(cityState);
        foreach (var civ in testGame.GameInfo.Civilizations) civ.PopupAlerts.Clear();
        // Founding a city next to foreign land can make civs meet; the stranger must stay unmet.
        player.Diplomacy.Remove(stranger.CivId);
        stranger.Diplomacy.Remove(player.CivId);
    }

    private QueryResponse Execute(IQuery query, string? viewer = null)
    {
        var envelope = registry.ToEnvelope(viewer ?? player.CivName, query);
        var wire = JsonSerializer.Deserialize<QueryEnvelope>(JsonSerializer.Serialize(envelope, UncivJson.DefaultOptions), UncivJson.DefaultOptions)!;
        return registry.Execute(wire, new QueryContext(testGame.GameInfo));
    }

    private T Ask<T>(IQuery<T> query) where T : notnull
    {
        ulong before = Hash();
        var response = Execute(query);
        Assert.True(response.Ok, $"{response.Code}: {response.Message}");
        Assert.Equal(before, Hash());
        return response.Result!.Value.Deserialize<T>(UncivJson.DefaultOptions)!;
    }

    private string Fail(IQuery query)
    {
        var response = Execute(query);
        Assert.False(response.Ok);
        return response.Code!;
    }

    private ulong Hash() => StateHasher.ComputeHash(h => h.Add(UncivFiles.GameInfoToString(testGame.GameInfo, forceZip: false)));

    // ---- pending decisions

    [Fact]
    public void PendingDecisionsListAlertsWithTheChoicesTheCommandAccepts()
    {
        var conquered = testGame.AddCity(player, testGame.GetTile(2, -2));
        conquered.FoundingCivObject = enemy;
        player.PopupAlerts.Clear();
        player.PopupAlerts.Add(new PopupAlert(AlertType.CityConquered, conquered.Id));
        player.PopupAlerts.Add(new PopupAlert(AlertType.WonderBuilt, "Pyramids"));
        player.PopupAlerts.Add(new PopupAlert(AlertType.Denounced, enemy.CivName));

        testGame.GameInfo.CurrentPlayerCiv = player;
        testGame.GameInfo.CurrentPlayer = player.CivId;
        var result = Ask(new PendingDecisionsQuery());
        var alerts = result.Decisions.Where(it => it.Kind == "Alert").ToList();
        Assert.Equal(3, alerts.Count);

        var city = alerts[0];
        Assert.True(city.Blocking);
        Assert.Equal("respond-to-alert", city.ResolvedBy);
        Assert.Equal(0, city.Index);
        Assert.Equal(conquered.Id, city.Alert!.Value);
        Assert.Equal(["annex", "puppet", "raze", "liberate"], city.Alert.Choices.Select(it => it.Choice));
        foreach (var choice in city.Alert.Choices)
        {
            var command = new RespondToAlert { Index = 0, AlertType = AlertType.CityConquered, Value = conquered.Id, Choice = choice.Choice };
            Assert.True(command.Validate(testGame.GameInfo, player.CivId).IsValid);
        }

        Assert.Equal("ack-alert", alerts[1].ResolvedBy);
        Assert.Empty(alerts[1].Alert!.Choices);
        Assert.Equal(["war"], alerts[2].Alert!.Choices.Select(it => it.Choice));
    }

    [Fact]
    public void ALiberateChoiceIsOnlyOfferedForACityFoundedByAnotherCiv()
    {
        var own = testGame.AddCity(player, testGame.GetTile(2, -2));
        player.PopupAlerts.Clear();
        player.PopupAlerts.Add(new PopupAlert(AlertType.CityConquered, own.Id));
        var choices = Ask(new PendingDecisionsQuery()).Decisions.Single(it => it.Kind == "Alert").Alert!.Choices.Select(it => it.Choice);
        Assert.DoesNotContain("liberate", choices);
    }

    [Fact]
    public void AnUnanswerableAlertIsMarkedAckOnly()
    {
        player.PopupAlerts.Add(new PopupAlert(AlertType.Event, "No such event"));
        var alert = Ask(new PendingDecisionsQuery()).Decisions.Single(it => it.Kind == "Alert").Alert!;
        Assert.True(alert.AckOnly);
        Assert.Empty(alert.Choices);
    }

    [Fact]
    public void EventAlertsCarryTheirTextAndChoices()
    {
        var ruleset = testGame.Ruleset;
        var eventObj = new Event { Name = "Test Event", Text = "Something happened", Choices = [new EventChoice { Text = "Fine" }] };
        ruleset.Events.Put("Test Event", eventObj);
        player.PopupAlerts.Add(new PopupAlert(AlertType.Event, "Test Event"));
        var alert = Ask(new PendingDecisionsQuery()).Decisions.Single(it => it.Kind == "Alert").Alert!;
        Assert.False(alert.AckOnly);
        Assert.Equal("Something happened", alert.EventText);
        Assert.Equal([("0", "Fine")], alert.Choices.Select(it => (it.Choice, it.Label)));
    }

    [Fact]
    public void PendingDecisionsOfASpectatorAreEmpty()
    {
        var spectator = testGame.AddCiv(testGame.Ruleset.Nations["Spectator"], isPlayer: true);
        Assert.Empty(Ask(new PendingDecisionsQuery(), spectator).Decisions);
    }

    private T Ask<T>(IQuery<T> query, Civilization viewer) where T : notnull
    {
        var response = Execute(query, viewer.CivName);
        Assert.True(response.Ok, $"{response.Code}: {response.Message}");
        return response.Result!.Value.Deserialize<T>(UncivJson.DefaultOptions)!;
    }

    // ---- promotions

    [Fact]
    public void PromotionChoicesFollowTheUnitsExperience()
    {
        var open = testGame.TileMap.TileList.First(it => it.GetCity() == null && it.OwningCity == null);
        var unit = testGame.AddUnit("Warrior", player, open);
        var none = Ask(new PromotionChoicesQuery(unit.Id));
        Assert.False(none.CanBePromoted);
        Assert.Empty(none.Promotions);
        Assert.Empty(Ask(new PromotableUnitsQuery()).UnitIds);

        unit.Promotions.XP = unit.Promotions.XpForNextPromotion();
        var result = Ask(new PromotionChoicesQuery(unit.Id));
        Assert.True(result.CanBePromoted);
        Assert.NotEmpty(result.Promotions);
        Assert.All(result.Promotions, it => Assert.True(it.Selectable));
        Assert.Equal(result.Promotions.Select(it => it.Name).Order(StringComparer.Ordinal), result.Promotions.Select(it => it.Name));
        Assert.Equal([unit.Id], Ask(new PromotableUnitsQuery()).UnitIds);
        Assert.False(result.CanSaveDefault); // not in a city

        var promote = new PromoteUnit { UnitId = unit.Id, Promotion = result.Promotions[0].Name };
        testGame.GameInfo.CurrentPlayerCiv = player;
        testGame.GameInfo.CurrentPlayer = player.CivId;
        Assert.True(promote.Validate(testGame.GameInfo, player.CivId).IsValid);

        unit.CurrentMovement = 0f; // PromoteUnit needs movement left
        Assert.False(Ask(new PromotionChoicesQuery(unit.Id)).Promotions.Any());
        Assert.Empty(Ask(new PromotableUnitsQuery()).UnitIds);
    }

    [Fact]
    public void PromotionChoicesAnswerForeignUnitsLikeMissingOnes()
    {
        var foreign = testGame.AddUnit("Warrior", enemy, testGame.GetTile(2, 2));
        Assert.Equal(UnitQueryCodes.UnknownUnit, Fail(new PromotionChoicesQuery(foreign.Id)));
        Assert.Equal(UnitQueryCodes.UnknownUnit, Fail(new PromotionChoicesQuery(-5)));
    }

    [Fact]
    public void PromotionsInACityCanBeSavedAsDefault()
    {
        var unit = testGame.AddUnit("Warrior", player, testGame.GetTile(0, 0));
        unit.Promotions.XP = unit.Promotions.XpForNextPromotion();
        Assert.True(Ask(new PromotionChoicesQuery(unit.Id)).CanSaveDefault);
    }

    // ---- spies

    [Fact]
    public void SpyDestinationsListExploredCitiesWithoutOurSpyAndMatchMoveSpy()
    {
        Assert.Equal(EmpireQueryCodes.NotAvailable, Fail(new SpyDestinationsQuery()));
        testGame.GameInfo.GameParameters.EspionageEnabled = true;
        var spy = player.EspionageManager.AddSpy();
        var dead = player.EspionageManager.AddSpy();
        dead.SetAction(SpyAction.Dead);
        var enemyCity = enemy.Cities.Single();
        var strangerCity = stranger.Cities.Single();

        var before = Ask(new SpyDestinationsQuery());
        Assert.DoesNotContain(before.Spies.Single(it => it.Name == spy.Name).Destinations, it => it.CityId == enemyCity.Id); // not explored yet

        enemyCity.GetCenterTile().SetExplored(player, true);
        strangerCity.GetCenterTile().SetExplored(player, true);
        var result = Ask(new SpyDestinationsQuery());
        var entry = result.Spies.Single(it => it.Name == spy.Name);
        Assert.Equal(Constants.SpyHideout, entry.Location);
        Assert.False(entry.CanMoveToHideout);
        var destinations = entry.Destinations;
        Assert.Contains(destinations, it => it.CityId == enemyCity.Id && it.Civ == enemy.CivName);
        var hidden = destinations.Single(it => it.CityId == strangerCity.Id);
        Assert.Equal("", hidden.Civ); // never met
        foreach (var city in testGame.GameInfo.GetCities())
        {
            var command = new MoveSpyProbe(spy.Name, city.Id);
            Assert.Equal(command.Accepted(testGame.GameInfo, player), destinations.Any(it => it.CityId == city.Id));
        }
        var deadEntry = result.Spies.Single(it => it.Name == dead.Name);
        Assert.False(deadEntry.IsAlive);
        Assert.Empty(deadEntry.Destinations);

        spy.MoveTo(enemyCity);
        var moved = Ask(new SpyDestinationsQuery()).Spies.Single(it => it.Name == spy.Name);
        Assert.True(moved.CanMoveToHideout);
        Assert.DoesNotContain(moved.Destinations, it => it.CityId == enemyCity.Id);
        Assert.Contains(moved.Destinations, it => it.CityId == strangerCity.Id);
    }

    [Fact]
    public void SpyCoupOddsAreOnlyGivenForASetUpSpyInACityState()
    {
        testGame.GameInfo.GameParameters.EspionageEnabled = true;
        var spy = player.EspionageManager.AddSpy();
        var city = cityState.Cities.Single();
        city.GetCenterTile().SetExplored(player, true);
        spy.MoveTo(city);
        spy.SetAction(SpyAction.Moving, 1);
        Assert.Null(Ask(new SpyDestinationsQuery()).Spies.Single().CoupChance);

        spy.SetAction(SpyAction.RiggingElections, 5);
        Assert.True(spy.CanDoCoup());
        var entry = Ask(new SpyDestinationsQuery()).Spies.Single();
        Assert.True(entry.CanDoCoup);
        Assert.Equal(spy.GetCoupChanceOfSuccess(false), entry.CoupChance);
    }

    private sealed record MoveSpyProbe(string Spy, string CityId)
    {
        public bool Accepted(GameInfo game, Civilization actor)
        {
            game.CurrentPlayerCiv = actor;
            game.CurrentPlayer = actor.CivId;
            return new global::Unciv.Commands.Empire.MoveSpy { Spy = Spy, CityId = CityId }.Validate(game, actor.CivId).IsValid;
        }
    }

    // ---- quests

    [Fact]
    public void CityStateQuestsListTheQuestsAssignedToTheViewer()
    {
        var json = "{\"assignedQuests\":["
            + $$"""{"questName":"Route","assigner":"{{cityState.CivName}}","assignee":"{{player.CivName}}","assignedOnTurn":0,"data1":"","data2":""},"""
            + $$"""{"questName":"Route","assigner":"{{cityState.CivName}}","assignee":"{{enemy.CivName}}","assignedOnTurn":0,"data1":"","data2":""}]}""";
        var manager = UncivJson.Deserialize<QuestManager>(json)!;
        cityState.QuestManager = manager;
        manager.SetTransients(cityState);

        var result = Ask(new CityStateQuestsQuery(cityState.CivName));
        var quest = Assert.Single(result.Quests);
        Assert.Equal("Route", quest.Name);
        Assert.False(quest.IsGlobal);
        Assert.Equal(testGame.Ruleset.Quests["Route"].Influence, quest.Influence);
        Assert.Equal(manager.GetAssignedQuestsFor(player).Single().GetDescription(), quest.Description);
        Assert.Null(quest.RemainingTurns); // Route does not expire
    }

    private void AssignContest(params Civilization[] assignees)
    {
        var quests = assignees.Select(it =>
            $$"""{"questName":"Contest Culture","assigner":"{{cityState.CivName}}","assignee":"{{it.CivName}}","assignedOnTurn":0,"data1":"0","data2":""}""");
        var manager = UncivJson.Deserialize<QuestManager>("{\"assignedQuests\":[" + string.Join(",", quests) + "]}")!;
        cityState.QuestManager = manager;
        manager.SetTransients(cityState);
    }

    [Fact]
    public void ContestStandingNamesTheLeadersButNeverACivTheViewerHasNotMet()
    {
        player.TotalCultureForContests = 50;
        enemy.TotalCultureForContests = 100;
        stranger.TotalCultureForContests = 100;
        AssignContest(player, enemy);
        var quest = Assert.Single(Ask(new CityStateQuestsQuery(cityState.CivName)).Quests);
        Assert.True(quest.IsGlobal);
        Assert.Contains(enemy.CivName, quest.Standing);
        Assert.Contains("Current leader", quest.Standing);

        AssignContest(player, stranger);
        Assert.Equal("", Assert.Single(Ask(new CityStateQuestsQuery(cityState.CivName)).Quests).Standing);

        AssignContest(player);
        Assert.Contains("Current leader", Assert.Single(Ask(new CityStateQuestsQuery(cityState.CivName)).Quests).Standing);
    }

    [Fact]
    public void CityStateQuestsRefuseUnmetCivsAndMajorCivs()
    {
        Assert.Equal(EmpireQueryCodes.UnknownCiv, Fail(new CityStateQuestsQuery(stranger.CivName)));
        Assert.Equal(EmpireQueryCodes.BadRequest, Fail(new CityStateQuestsQuery(enemy.CivName)));
        Assert.Equal(EmpireQueryCodes.UnknownCiv, Fail(new CityStateQuestsQuery("Atlantis")));
    }

    // ---- world congress

    [Fact]
    public void DiplomaticVoteListsKnownCivsOnlyWhileAVoteIsOpen()
    {
        Assert.False(Ask(new DiplomaticVoteQuery()).CanVote);
        player.AddFlag(CivFlags.TurnsTillNextDiplomaticVote.ToString(), 0);
        var result = Ask(new DiplomaticVoteQuery());
        Assert.True(result.CanVote);
        Assert.Equal(player.DiplomacyFunctions.GetKnownCivsSorted(false).Select(it => it.CivId), result.Candidates);
        Assert.DoesNotContain(stranger.CivId, result.Candidates);
        Assert.Contains(enemy.CivId, result.Candidates);
    }
}
