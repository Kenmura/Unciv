using Unciv.Core.Tests.Support;
using Unciv.Json;
using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Managers;
using Unciv.Models.Ruleset;
using Unciv.Utils;
using CityObject = Unciv.Logic.City.City;
using TileClass = Unciv.Logic.Map.Tiles.Tile;
using UncivCiv = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Core.Tests.Fixtures.CivilizationFixtures;

// T151: port of tests/src/com/unciv/logic/civilization/QuestTests.kt over the real TestGame; QuestManager round-trips through UncivJson (the C# json()).
[Collection("RulesetCache")]
public sealed class QuestFixtureTests
{
    private const string TestRouteAndGPJson = "{\"assignedQuests\":[{\"questName\":\"Acquire Great Person\",\"assigner\":\"Nation-1\",\"assignee\":\"Nation-0\",\"assignedOnTurn\":37,\"data1\":\"Great Artist\"},{\"questName\":\"Route\",\"assigner\":\"Nation-1\",\"assignee\":\"Nation-0\",\"assignedOnTurn\":42}],\"globalQuestCountdown\":33,\"individualQuestCountdown\":{\"Nation-0\":12}}";
    private const string TestClearBarbarianJson = "{\"assignedQuests\":[{\"questName\":\"Clear Barbarian Camp\",\"assigner\":\"Nation-1\",\"assignee\":\"Nation-0\",\"assignedOnTurn\":37,\"data1\":0,\"data2\":0}],\"globalQuestCountdown\":3,\"individualQuestCountdown\":{\"Nation-0\":3}}";

    private readonly TestGame testGame = new();
    private readonly UncivCiv civ;
    private readonly CityObject city;
    private readonly UncivCiv cityState;

    public QuestFixtureTests()
    {
        civ = testGame.AddCiv();
        cityState = testGame.AddCiv(cityStateType: "Maritime");
        civ.DiplomacyFunctions.MakeCivilizationsMeet(cityState);
        testGame.MakeHexagonalMap(3);
        city = testGame.AddCity(civ, testGame.GetTile(-2, 0));
        testGame.AddCity(cityState, testGame.GetTile(2, 0));
        testGame.GameInfo.Turns = QuestManager.INDIVIDUAL_QUEST_FIRST_POSSIBLE_TURN;
    }

    private QuestManager Manager => cityState.QuestManager;

    private void LoadManager(string testJson = TestRouteAndGPJson)
    {
        cityState.QuestManager = UncivJson.Deserialize<QuestManager>(testJson)!;
        Manager.SetTransients(cityState);
    }

    private List<AssignedQuest> AssignedQuests() => Manager.GetAssignedQuestsFor(civ).ToList();

    private (TileClass CampTile, float Influence) SetupBarbarianCamp()
    {
        var campTile = testGame.GetTile(0, 0);
        testGame.GameInfo.Barbarians.SetTransients(testGame.GameInfo);
        testGame.GameInfo.Barbarians.CreateNewCamp(campTile);
        LoadManager(TestClearBarbarianJson);
        return (campTile, AssignedQuests()[0].Quest.Influence);
    }

    private void CheckFulfillment(QuestName questName, float influence)
    {
        Manager.EndTurn();
        Assert.True(AssignedQuests().All(it => it.QuestNameInstance != questName), $"{questName} quest should have been removed");
        Assert.True(influence == cityState.GetDiplomacyManager(civ)!.GetInfluence(), "Should have gained influence by fulfilling the " + questName + " quest");
    }

    [Fact]
    public void TestSerializeQuestManager()
    {
        for (var i = 0; i < QuestManager.INDIVIDUAL_QUEST_FIRST_POSSIBLE_TURN_RAND; i++)
        {
            Manager.EndTurn();
            testGame.GameInfo.Turns++;
        }
        // The state-based random in QuestManager is not actually deterministic in Kotlin, so only fragments are expected
        var mustContain = new[] { "\"assignedQuests\":[{\"questName\":\"", "\"assigner\":\"Nation-1\",\"assignee\":\"Nation-0\"", "\"individualQuestCountdown\":{\"Nation-0\":" };

        var data = UncivJson.Serialize(Manager);

        Assert.True(mustContain.All(it => data.Contains(it, StringComparison.Ordinal)), "Serialized json for QuestManager must contain all expected fragments: " + data);
    }

    [Fact]
    public void TestDeserializeQuestManager()
    {
        LoadManager();

        var assignedQuests = AssignedQuests();
        Assert.Equal(2, assignedQuests.Count);
        Assert.Equal(QuestName.GreatPerson, assignedQuests[0].QuestNameInstance);
        Assert.Equal("Great Artist", assignedQuests[0].Data1);
        Assert.Equal(QuestName.Route, assignedQuests[1].QuestNameInstance);
    }

    [Fact]
    public void TestFulfillGreatPeson()
    {
        LoadManager();
        var influence = AssignedQuests()[0].Quest.Influence;

        testGame.AddUnit("Great Artist", civ, testGame.GetTile(-2, 0));

        CheckFulfillment(QuestName.GreatPerson, influence);
    }

    [Fact]
    public void TestFulfillRoute()
    {
        LoadManager();
        var influence = AssignedQuests()[1].Quest.Influence;
        civ.Tech.AddTechnology("The Wheel", false);

        testGame.GetTile(-1, 0).SetImprovement("Road", civ);
        testGame.GetTile(0, 0).SetImprovement("Road", civ);
        testGame.GetTile(1, 0).SetImprovement("Road", civ);
        civ.Cache.UpdateCitiesConnectedToCapital();

        CheckFulfillment(QuestName.Route, influence);
    }

    [Fact]
    public void TestFulfillClearBarbarianCamp()
    {
        var (campTile, influence) = SetupBarbarianCamp();
        var clearer = testGame.AddUnit("Warrior", civ, testGame.GetTile(-1, 0));

        clearer.Movement.HeadTowards(campTile);

        CheckFulfillment(QuestName.ClearBarbarianCamp, influence);
    }

    [Fact]
    public void TestClearBarbarianCampObsoletedByExpansion()
    {
        var (campTile, _) = SetupBarbarianCamp();
        Assert.Null(campTile.OwningCity);
        civ.PlayerType = PlayerType.Human; // Let's test the notification too in this case
        civ.AddGold(city.Expansion.GetGoldCostOfTile(campTile));

        city.Expansion.BuyTile(campTile);

        Assert.Same(city, campTile.OwningCity);
        Assert.False(Manager.HaveQuestsFor(civ), "Expansion should have cleared the ClearBarbarianCamp quest");
        Assert.True(civ.Notifications.Any(it => it.Text.Contains(QuestName.ClearBarbarianCamp.GetValue(), StringComparison.Ordinal)), "The player should have been notified ClearBarbarianCamp got obsolete");
    }

    [Fact]
    public void TestAlternativeBarbarianCampsAreUsed()
    {
        testGame.MakeHexagonalMap(9); // enough for 6 or 7 camps
        for (var i = 0; i < 3; i++)
            testGame.CreateTileImprovement("Marks a barbarian camp");
        testGame.AddCiv(testGame.Ruleset.Nations[Constants.Barbarians]);
        var (campTile, _) = SetupBarbarianCamp();
        var barbs = testGame.GameInfo.Barbarians;

        for (var i = 0; i < 9; i++)
            barbs.PlaceBarbarianEncampment(forTesting: true);

        // The placement pipeline itself produces valid results
        var improvementNames = barbs.Encampments.Select(it => testGame.TileMap[it.Position].Improvement).ToHashSet();
        Assert.False(improvementNames.Count == 0 || improvementNames.Contains(null), "After placing several encampments, none should have no tile improvements");

        // The map only fits 6-7 camps due to the spacing rule: sample the improvement choice directly instead
        var sampledImprovements = Enumerable.Range(0, 30).Select(seed =>
        {
            barbs.CreateNewCamp(campTile, new GameRandom(seed));
            return campTile.Improvement;
        }).ToHashSet();
        Assert.True(sampledImprovements.Count > 1, "After creating several camps, there should be more than one improvement used, but we see only " + string.Join(", ", sampledImprovements));
    }
}
