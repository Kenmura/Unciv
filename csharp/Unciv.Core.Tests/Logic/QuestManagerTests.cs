using Unciv.Json;
using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Logic.Civilization.Managers;
using Unciv.Logic.Map;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using Unciv.Utils;
using CityObject = Unciv.Logic.City.City;
using GeneratorClass = Unciv.Logic.Map.MapGenerator.MapGenerator;
using RulesetClass = Unciv.Models.Ruleset.Ruleset;
using UncivCiv = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Core.Tests.Logic;

// T069: QuestManager, AssignedQuest, QuestImplementation and QuestTargetHelpers on a small real game (vanilla ruleset, generated Tiny map).
// The TestGame-based QuestTests.kt fixtures are T072.
[Collection("RulesetCache")]
public sealed class QuestManagerTests : IDisposable
{
    private readonly RulesetClass ruleset;
    private readonly GameInfo game;
    private readonly UncivCiv babylon;
    private readonly UncivCiv greece;
    private readonly UncivCiv geneva;

    public QuestManagerTests()
    {
        RulesetCache.Clear();
        var jsons = JsonsPath();
        RulesetCache.LoadRulesets(new DirectoryRulesetFileSource(jsons, Path.Combine(jsons, "no mods here")), noMods: true);
        ruleset = RulesetCache.GetVanillaRuleset();
        var mapParameters = new MapParameters { Type = MapType.Pangaea, Shape = MapShape.Rectangular, Seed = 3, MapSize = MapSize.Tiny };
        game = new GameInfo
        {
            GameParameters = new GameParameters { BaseRuleset = ruleset.Name },
            TileMap = new GeneratorClass(ruleset).GenerateMap(mapParameters),
            Difficulty = "Prince",
            GameId = "test-game",
        };
        foreach (var (nation, type) in new[] { ("Babylon", PlayerType.Human), ("Greece", PlayerType.AI), ("Geneva", PlayerType.AI), (Constants.Barbarians, PlayerType.AI) })
            game.Civilizations.Add(new UncivCiv(nation) { PlayerType = type });
        game.TileMap.GameInfo = game;
        foreach (var civ in game.Civilizations)
            civ.CityStateFunctions = new FakeCityStateFunctions();
        game.SetTransients();
        babylon = game.Civilizations[0];
        greece = game.Civilizations[1];
        geneva = game.Civilizations[2];
    }

    public void Dispose() => RulesetCache.Clear();

    private static string JsonsPath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "android", "assets", "jsons");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException("android/assets/jsons not found above " + AppContext.BaseDirectory);
    }

    // ---- helpers ----

    private UncivCiv AddCityTo(UncivCiv civ, int landIndex)
    {
        var land = game.TileMap.Values.Where(it => it.IsLand && !it.IsImpassible()).ToList();
        // wire the city by hand (no founding, so no population auto-assignment)
        var city = new CityObject { Id = civ.CivId + "-city", Name = civ.CivId + " City", Location = land[landIndex].Position };
        civ.Cities = [city];
        city.SetTransients(civ);
        return civ;
    }

    /// <summary>Geneva (city state, capital) knows both majors and is at peace with them; every civ has a city.</summary>
    private void SetUpCities()
    {
        // each civ owns a settler, so that none counts as defeated
        var land = game.TileMap.Values.Where(it => it.IsLand && !it.IsImpassible()).ToList();
        for (var i = 0; i < 3; i++)
            Assert.NotNull(game.TileMap.PlaceUnitNearTile(land[i * 7 + 3].Position, "Settler", game.Civilizations[i]));
        AddCityTo(geneva, 0);
        AddCityTo(babylon, 12);
        AddCityTo(greece, 24);
        foreach (var major in new[] { babylon, greece })
        {
            geneva.DiplomacyFunctions.MakeCivilizationsMeet(major);
            geneva.GetDiplomacyManager(major)!.DiplomaticStatus = DiplomaticStatus.Peace;
            major.GetDiplomacyManager(geneva)!.DiplomaticStatus = DiplomaticStatus.Peace;
        }
        foreach (var civ in game.Civilizations)
            civ.Notifications.Clear(); // the first-contact gifts
    }

    private static string QuestJson(string quest, string assignee, int turn = 0, string data1 = "", string data2 = "") =>
        $$"""{"questName":"{{quest}}","assigner":"Geneva","assignee":"{{assignee}}","assignedOnTurn":{{turn}},"data1":"{{data1}}","data2":"{{data2}}"}""";

    /// <summary>Replaces Geneva's manager by one deserialized from the given assigned quests (the save format) plus extra top-level fields.</summary>
    private QuestManager LoadQuests(string extraFields = "", params string[] quests)
    {
        var json = "{\"assignedQuests\":[" + string.Join(",", quests) + "]" + extraFields + "}";
        var manager = UncivJson.Deserialize<QuestManager>(json)!;
        geneva.QuestManager = manager;
        manager.SetTransients(geneva);
        return manager;
    }

    private int Notifications(UncivCiv civ, string text) => civ.Notifications.Count(it => it.Text.Contains(text, StringComparison.Ordinal));

    // ---- AssignedQuest ----

    [Fact]
    public void AssignedQuest_RoundTripsAndClones()
    {
        var quest = new AssignedQuest("Give Gold", "Geneva", "Babylon", 7, "Greece", "x");
        var clone = UncivJson.Deserialize<AssignedQuest>(UncivJson.Serialize(quest))!;
        Assert.Equal(("Give Gold", "Geneva", "Babylon", 7, "Greece", "x"), (clone.QuestName, clone.Assigner, clone.Assignee, clone.AssignedOnTurn, clone.Data1, clone.Data2));
        var copy = quest.Clone();
        Assert.NotSame(quest, copy);
        Assert.Equal("Babylon", copy.Assignee);
        Assert.Equal("(uninitialized)", copy.ToString());
        Assert.Throws<InvalidOperationException>(() => copy.Quest);
        Assert.Throws<InvalidOperationException>(() => copy.AssigneeCiv);
        Assert.Throws<InvalidOperationException>(() => copy.AssignerCiv);
        Assert.Throws<InvalidOperationException>(() => copy.IsGlobal());
        Assert.Equal("(uninitialized)", new QuestManager().ToString());
    }

    [Fact]
    public void AssignedQuest_TransientsDurationAndRemainingTurns()
    {
        var manager = LoadQuests("", QuestJson("Give Gold", "Babylon", turn: 10, data1: "Greece"), QuestJson("Route", "Greece", turn: 10));
        var giveGold = manager.GetAssignedQuestsFor(babylon).Single();
        Assert.Same(geneva, giveGold.AssignerCiv);
        Assert.Same(babylon, giveGold.AssigneeCiv);
        Assert.Equal(QuestName.GiveGold, giveGold.QuestNameInstance);
        Assert.True(giveGold.IsIndividual());
        Assert.True(giveGold.DoesExpire());
        Assert.Equal(KotlinMath.FloatToInt(game.Speed.Modifier * 30), giveGold.GetDuration());
        game.Turns = 10;
        Assert.Equal(giveGold.GetDuration(), giveGold.GetRemainingTurns());
        Assert.False(giveGold.IsExpired());
        game.Turns = 10 + giveGold.GetDuration() + 5;
        Assert.Equal(0, giveGold.GetRemainingTurns());
        Assert.True(giveGold.IsExpired());
        Assert.Equal(20f, giveGold.GetInfluence());
        Assert.Contains("We are suffering great poverty", giveGold.GetDescription(), StringComparison.Ordinal);
        Assert.Contains("Give Gold(Individual, assigner=Geneva, assignee=Babylon, d1=Greece, d2=, ", giveGold.ToString(), StringComparison.Ordinal);
        var route = manager.GetAssignedQuestsFor("Greece").Single();
        Assert.False(route.DoesExpire());
        Assert.False(route.IsExpired());
        Assert.True(manager.HaveQuestsFor(babylon));
        Assert.False(manager.HaveQuestsFor(geneva));
        Assert.Equal("Geneva:[" + giveGold + ", " + route + "]", manager.ToString());
    }

    // ---- save format / clone ----

    [Fact]
    public void QuestManager_SavesAndClonesAllFields()
    {
        var manager = LoadQuests(
            ""","globalQuestCountdown":12,"individualQuestCountdown":{"Babylon":3,"Greece":-1},"unitsToKillForCiv":{"Greece":4},"unitsKilledFromCiv":{"Greece":{"Babylon":2}}""",
            QuestJson("Invest", "Babylon", data1: "25"));
        var json = UncivJson.Serialize(manager);
        Assert.Contains("\"globalQuestCountdown\":12", json, StringComparison.Ordinal);
        Assert.Contains("\"unitsKilledFromCiv\":{\"Greece\":{\"Babylon\":2}}", json, StringComparison.Ordinal);
        var clone = manager.Clone();
        Assert.Equal(json, UncivJson.Serialize(clone));
        Assert.Equal(manager.UnitsToKill(greece), clone.UnitsToKill(greece));
        Assert.Equal(2, clone.UnitsKilledSoFar(greece, babylon));
        clone.SetTransients(geneva);
        Assert.NotSame(manager.GetAssignedQuestsFor(babylon).Single(), clone.GetAssignedQuestsFor(babylon).Single());
        var reloaded = UncivJson.Deserialize<QuestManager>(json)!;
        Assert.Equal(json, UncivJson.Serialize(reloaded));
    }

    // ---- investment, wantsDead ----

    [Fact]
    public void InvestmentMultiplier_AndWantsDead()
    {
        SetUpCities();
        var manager = LoadQuests("", QuestJson("Invest", "Babylon", data1: "25"), QuestJson("Conquer City State", "Greece", data1: "Sparta"));
        Assert.Equal(1.25f, manager.GetInvestmentMultiplier(babylon));
        Assert.Equal(1f, manager.GetInvestmentMultiplier(greece));
        Assert.True(manager.WantsDead("Sparta"));
        Assert.False(manager.WantsDead("Babylon"));
    }

    // ---- rewards and removals ----

    [Fact]
    public void BarbarianCampCleared_RewardsTheClearerAndRemovesAllMatchingQuests()
    {
        SetUpCities();
        var manager = LoadQuests("", QuestJson("Clear Barbarian Camp", "Babylon", data1: "3", data2: "4"), QuestJson("Clear Barbarian Camp", "Greece", data1: "3", data2: "4"),
            QuestJson("Clear Barbarian Camp", "Greece", data1: "5", data2: "6"));
        var before = geneva.GetDiplomacyManager(babylon)!.Influence;
        manager.BarbarianCampCleared(babylon, HexCoord.Of(3, 4));
        Assert.Equal(before + 50f, geneva.GetDiplomacyManager(babylon)!.Influence);
        Assert.Empty(manager.GetAssignedQuestsFor(babylon));
        Assert.Single(manager.GetAssignedQuestsFor(greece));
        Assert.Equal(1, Notifications(babylon, "rewarded you with [50] influence for completing the [Clear Barbarian Camp] quest"));
        manager.BarbarianCampCleared(babylon, HexCoord.Of(5, 6)); // nobody matching: nothing is rewarded, the quest goes away
        Assert.Empty(manager.GetAssignedQuestsFor(greece));
        Assert.Equal(1, Notifications(babylon, "rewarded you"));
    }

    [Fact]
    public void CityStateConquered_ReceivedGoldGift_AndBullied()
    {
        SetUpCities();
        var manager = LoadQuests("", QuestJson("Conquer City State", "Babylon", data1: "Sparta"), QuestJson("Give Gold", "Babylon", data1: "Greece"),
            QuestJson("Bully City State", "Babylon", data1: "Geneva"), QuestJson("Route", "Babylon"), QuestJson("Invest", "Babylon", data1: "25"), QuestJson("Find Player", "Greece", data1: "Babylon"));
        var sparta = new UncivCiv("Sparta");
        sparta.CivId = "Sparta";
        manager.CityStateConquered(sparta, greece); // wrong attacker: kept
        Assert.Equal(6, manager.GetAssignedQuestsFor(babylon).Count() + manager.GetAssignedQuestsFor(greece).Count());
        manager.CityStateConquered(sparta, babylon);
        Assert.Equal(5, manager.GetAssignedQuestsFor(babylon).Count() + manager.GetAssignedQuestsFor(greece).Count());
        manager.ReceivedGoldGift(greece);
        Assert.Equal(5, manager.GetAssignedQuestsFor(babylon).Count() + manager.GetAssignedQuestsFor(greece).Count());
        manager.ReceivedGoldGift(babylon);
        Assert.Equal(4, manager.GetAssignedQuestsFor(babylon).Count() + manager.GetAssignedQuestsFor(greece).Count());
        manager.CityStateBullied(geneva, babylon); // rewards the Bully quest, then revokes the other individual quests and Invest
        Assert.Empty(manager.GetAssignedQuestsFor(babylon));
        Assert.Single(manager.GetAssignedQuestsFor(greece));
        Assert.Equal(1, Notifications(babylon, "cancelled the quests they had given you because you demanded tribute from them"));
        manager.CityStateBullied(greece, babylon); // someone else was bullied: only the (non-existing) matching quests are touched
        Assert.Single(manager.GetAssignedQuestsFor(greece));
        manager.CityStateBullied(geneva, babylon); // nothing to revoke: no second notification
        Assert.Equal(1, Notifications(babylon, "cancelled the quests"));
    }

    [Fact]
    public void RemoveQuestsFor_HasNoSideEffects()
    {
        SetUpCities();
        var manager = LoadQuests(""","individualQuestCountdown":{"Babylon":3,"Greece":4}""", QuestJson("Route", "Babylon"), QuestJson("Route", "Greece"));
        manager.RemoveQuestsFor(babylon);
        Assert.Empty(manager.GetAssignedQuestsFor(babylon));
        Assert.Single(manager.GetAssignedQuestsFor(greece));
        Assert.DoesNotContain("Babylon\":3", UncivJson.Serialize(manager), StringComparison.Ordinal);
        Assert.Empty(babylon.Notifications);
    }

    // ---- war with major pseudo-quest ----

    [Fact]
    public void WasAttackedBy_AsksKnownMajorsForAssistance_AndKillsAreCounted()
    {
        SetUpCities();
        var manager = geneva.QuestManager;
        manager.WasAttackedBy(greece); // Greece has no units: the minimum of 3 units to kill
        Assert.True(manager.IsWarWithMajorActive(greece));
        Assert.Equal(3, manager.UnitsToKill(greece));
        Assert.Equal(1, Notifications(babylon, "[Geneva] is being attacked by [Greece]! Kill [3] of the attacker's military units"));
        Assert.Equal(0, Notifications(greece, "is being attacked")); // no "Hey Bob help us against Bob"
        Assert.Equal(0, manager.UnitsKilledSoFar(greece, babylon));
        Assert.Equal(0, manager.UnitsToKill(babylon));

        manager.JustMet(babylon);
        Assert.Equal(2, Notifications(babylon, "is being attacked by [Greece]"));

        manager.MilitaryUnitKilledBy(babylon, babylon); // not the active war target
        Assert.Equal(0, manager.UnitsKilledSoFar(babylon, babylon));
        manager.MilitaryUnitKilledBy(babylon, greece);
        manager.MilitaryUnitKilledBy(babylon, greece);
        Assert.Equal(2, manager.UnitsKilledSoFar(greece, babylon));
        var before = geneva.GetDiplomacyManager(babylon)!.Influence;
        manager.MilitaryUnitKilledBy(babylon, greece);
        Assert.Equal(before + 100f, geneva.GetDiplomacyManager(babylon)!.Influence);
        Assert.Equal(1, Notifications(babylon, "is deeply grateful for your assistance in the war against [Greece]"));
        Assert.False(manager.IsWarWithMajorActive(greece));
        Assert.Equal(0, manager.UnitsKilledSoFar(greece, babylon));
    }

    [Fact]
    public void MilitaryUnitKilledBy_NoCreditWhenAtWarWithTheKillerOrUnmet()
    {
        SetUpCities();
        var manager = geneva.QuestManager;
        manager.WasAttackedBy(greece);
        geneva.GetDiplomacyManager(babylon)!.DiplomaticStatus = DiplomaticStatus.War;
        manager.MilitaryUnitKilledBy(babylon, greece);
        Assert.Equal(0, manager.UnitsKilledSoFar(greece, babylon));
        manager.MilitaryUnitKilledBy(new UncivCiv("Nobody") { CivId = "Nobody" }, greece);
        Assert.Equal(0, manager.UnitsKilledSoFar(greece, babylon));
    }

    [Fact]
    public void EndTurn_EndsWarWithMajorWhenNoLongerAtWar_AndNotifiesOthersWhoDidNotWin()
    {
        SetUpCities();
        var manager = geneva.QuestManager;
        manager.WasAttackedBy(greece);
        manager.EndTurn(); // Geneva is at peace with Greece
        Assert.False(manager.IsWarWithMajorActive(greece));
        Assert.Equal(1, Notifications(babylon, "[Geneva] no longer needs your assistance against [Greece]."));
    }

    // ---- EndTurn ----

    [Fact]
    public void EndTurn_DefeatedCityStateForgetsEverything_AndCitylessOneDoesNothing()
    {
        var manager = LoadQuests(""","globalQuestCountdown":5""", QuestJson("Route", "Babylon"));
        manager.EndTurn(); // Geneva has no city: defeated or not, there is nothing to clear... but it is defeated without cities and units
        Assert.Empty(manager.GetAssignedQuestsFor(babylon));
        Assert.Contains("\"globalQuestCountdown\":-1", UncivJson.Serialize(manager), StringComparison.Ordinal);
    }

    [Fact]
    public void EndTurn_SeedsCountdowns_AndEventuallyAssignsAQuest()
    {
        SetUpCities();
        game.Turns = 100;
        var manager = geneva.QuestManager;
        for (var i = 0; i < 80 && !manager.HaveQuestsFor(babylon); i++)
            manager.EndTurn();
        Assert.True(manager.HaveQuestsFor(babylon));
        Assert.Equal(1, Notifications(babylon, "[Geneva] assigned you a new quest: ["));
        var json = UncivJson.Serialize(manager);
        Assert.Contains("\"individualQuestCountdown\":{", json, StringComparison.Ordinal);
    }

    [Fact]
    public void EndTurn_BeforeTurn30SeedsNothing()
    {
        SetUpCities();
        game.Turns = 5;
        geneva.QuestManager.EndTurn();
        var json = UncivJson.Serialize(geneva.QuestManager);
        Assert.Contains("\"globalQuestCountdown\":-1", json, StringComparison.Ordinal);
        Assert.Contains("\"individualQuestCountdown\":{}", json, StringComparison.Ordinal);
    }

    [Fact]
    public void EndTurn_FirstPossibleTurnSeedsBelowTheRandomLimit()
    {
        SetUpCities();
        game.Turns = QuestManager.GLOBAL_QUEST_FIRST_POSSIBLE_TURN;
        geneva.QuestManager.EndTurn();
        var json = UncivJson.Serialize(geneva.QuestManager);
        Assert.DoesNotContain("\"globalQuestCountdown\":-1", json, StringComparison.Ordinal);
    }

    // ---- global contests ----

    [Fact]
    public void GlobalContest_PaysTheWinnerWhenItExpires()
    {
        SetUpCities();
        greece.PlayerType = PlayerType.Human; // AI civs get no notifications
        game.Turns = 100;
        var manager = LoadQuests(""","globalQuestCountdown":99""",
            QuestJson("Contest Culture", "Babylon", turn: 10, data1: "10"), QuestJson("Contest Culture", "Greece", turn: 10, data1: "10"));
        babylon.TotalCultureForContests = 60;
        greece.TotalCultureForContests = 30;
        var quest = manager.GetAssignedQuestsFor(babylon).Single();
        Assert.StartsWith("Current leader(s): [", manager.GetScoreStringForGlobalQuest(quest), StringComparison.Ordinal);
        Assert.DoesNotContain("you:", manager.GetScoreStringForGlobalQuest(quest), StringComparison.Ordinal);
        Assert.Contains("you:", manager.GetScoreStringForGlobalQuest(manager.GetAssignedQuestsFor(greece).Single()), StringComparison.Ordinal);
        var before = geneva.GetDiplomacyManager(babylon)!.Influence;
        manager.EndTurn();
        Assert.Empty(manager.GetAssignedQuestsFor(babylon));
        Assert.Empty(manager.GetAssignedQuestsFor(greece));
        Assert.True(geneva.GetDiplomacyManager(babylon)!.Influence > before);
        Assert.Equal(1, Notifications(greece, "The [Contest Culture] quest for [Geneva] has ended. It was won by [{Babylon}]."));
    }

    [Fact]
    public void GlobalContest_NobodyWinsWithZeroScore_AndTiesShareTheWin()
    {
        SetUpCities();
        var manager = LoadQuests("", QuestJson("Contest Technologies", "Babylon", data1: "0"), QuestJson("Contest Technologies", "Greece", data1: "0"),
            QuestJson("Invest", "Babylon", data1: "25"));
        var tech = manager.GetAssignedQuestsFor(babylon).First();
        Assert.Equal("", manager.GetScoreStringForGlobalQuest(tech)); // zero score: no leaders
        Assert.Equal("", manager.GetScoreStringForGlobalQuest(manager.GetAssignedQuestsFor(babylon).Last())); // Invest is no competition
        babylon.Tech.TechsResearched.Add("Writing");
        greece.Tech.TechsResearched.Add("Writing");
        var text = manager.GetScoreStringForGlobalQuest(tech);
        Assert.Contains("Babylon", text, StringComparison.Ordinal);
        Assert.Contains("Greece", text, StringComparison.Ordinal);
        Assert.DoesNotContain("you:", text, StringComparison.Ordinal);
    }

    [Fact]
    public void GetScoreStringForGlobalQuest_RejectsIndividualOrForeignQuests()
    {
        var manager = LoadQuests("", QuestJson("Route", "Babylon"));
        Assert.Throws<ArgumentException>(() => manager.GetScoreStringForGlobalQuest(manager.GetAssignedQuestsFor(babylon).Single()));
        var other = new QuestManager();
        other.SetTransients(babylon);
        Assert.Throws<ArgumentException>(() => other.GetScoreStringForGlobalQuest(manager.GetAssignedQuestsFor(babylon).Single()));
    }

    // ---- individual quests ----

    [Fact]
    public void IndividualQuest_ExpiresAndIsNotified()
    {
        SetUpCities();
        game.Turns = 500;
        var manager = LoadQuests("", QuestJson("Give Gold", "Babylon", turn: 1, data1: "Greece"));
        manager.HandleIndividualQuests();
        Assert.Empty(manager.GetAssignedQuestsFor(babylon));
        Assert.Equal(1, Notifications(babylon, "[Geneva] no longer needs your help with the [Give Gold] quest."));
    }

    [Fact]
    public void IndividualQuest_IsDroppedWhenTheAssigneeIsAtWar_AndKeptOtherwise()
    {
        SetUpCities();
        var manager = LoadQuests("", QuestJson("Give Gold", "Babylon", turn: 0, data1: "Greece"), QuestJson("Give Gold", "Greece", turn: 0, data1: "Babylon"));
        geneva.GetDiplomacyManager(babylon)!.DiplomaticStatus = DiplomaticStatus.War;
        manager.HandleIndividualQuests();
        Assert.Empty(manager.GetAssignedQuestsFor(babylon));
        Assert.Single(manager.GetAssignedQuestsFor(greece));
    }

    [Fact]
    public void IndividualQuest_CompletesAndPaysTheReward()
    {
        SetUpCities();
        var manager = LoadQuests("", QuestJson("Find Natural Wonder", "Babylon", data1: "Mount Fuji"));
        manager.HandleIndividualQuests();
        Assert.Single(manager.GetAssignedQuestsFor(babylon)); // not found yet
        babylon.NaturalWonders.Add("Mount Fuji");
        var before = geneva.GetDiplomacyManager(babylon)!.Influence;
        manager.HandleIndividualQuests();
        Assert.Empty(manager.GetAssignedQuestsFor(babylon));
        Assert.Equal(before + 40f, geneva.GetDiplomacyManager(babylon)!.Influence);
        Assert.Equal(1, Notifications(babylon, "rewarded you with [40] influence for completing the [Find Natural Wonder] quest."));
    }

    [Fact]
    public void HandleObsoleteGlobalQuests_RemovesObsoleteOnes()
    {
        SetUpCities();
        var manager = LoadQuests("", QuestJson("Conquer City State", "Babylon", data1: "Nowhere"), QuestJson("Find Natural Wonder", "Greece", data1: "Mount Fuji"));
        manager.HandleObsoleteGlobalQuests(); // "Nowhere" is not a civilization: counts as defeated
        Assert.Empty(manager.GetAssignedQuestsFor(babylon));
        Assert.Single(manager.GetAssignedQuestsFor(greece));
        Assert.Equal(1, Notifications(babylon, "no longer needs your help with the [Conquer City State] quest"));
    }

    // ---- QuestImplementation ----

    [Fact]
    public void Implementations_NoneIsNeverValid_AndDefaultsApply()
    {
        SetUpCities();
        Assert.False(QuestName.None.IsValid(geneva, babylon));
        var quest = new AssignedQuest(game, new Quest { Name = "Contest Culture" }, geneva, babylon);
        Assert.False(QuestName.None.IsComplete(geneva, quest));
        Assert.False(QuestName.None.IsObsolete(geneva, quest));
        Assert.Equal(("", ""), QuestName.None.GetAssignedQuestData(geneva, new Quest(), babylon));
        Assert.IsType<DiplomacyAction>(Assert.Single(QuestName.None.GetNotificationActions(geneva, quest)));
        Assert.Same(QuestName.Route.GetImplementation(), QuestName.Route.GetImplementation());
        foreach (var name in Enum.GetValues<QuestName>())
            Assert.NotNull(name.GetImplementation());
        Assert.Equal(("0", ""), QuestName.ContestTech.GetAssignedQuestData(geneva, new Quest(), babylon));
        babylon.TotalCultureForContests = 7;
        babylon.TotalFaithForContests = 8;
        Assert.Equal(("7", ""), QuestName.ContestCulture.GetAssignedQuestData(geneva, new Quest(), babylon));
        Assert.Equal(("8", ""), QuestName.ContestFaith.GetAssignedQuestData(geneva, new Quest(), babylon));
        Assert.True(QuestName.ContestCulture.IsValid(geneva, babylon));
        Assert.Equal(game.IsReligionEnabled(), QuestName.ContestFaith.IsValid(geneva, babylon));
        Assert.Equal(("50", ""), QuestName.Invest.GetAssignedQuestData(geneva, ruleset.Quests["Invest"], babylon));
        Assert.False(QuestName.Invest.IsComplete(geneva, quest));
        Assert.False(QuestName.ContestCulture.IsComplete(geneva, quest));
        Assert.False(QuestName.ContestFaith.IsComplete(geneva, quest));
        Assert.False(QuestName.ContestTech.IsComplete(geneva, quest));
    }

    [Fact]
    public void Implementations_ClearBarbarianCamp()
    {
        SetUpCities();
        Assert.False(QuestName.ClearBarbarianCamp.IsValid(geneva, babylon)); // no camp around
        var capitalTile = geneva.GetCapital()!.GetCenterTile();
        var camp = capitalTile.GetTilesInDistance(3).First(it => it.IsLand && !it.IsImpassible() && it.GetOwner() == null);
        camp.SetImprovementBasic(Constants.BarbarianEncampment);
        Assert.True(QuestName.ClearBarbarianCamp.IsValid(geneva, babylon));
        var (x, y) = QuestName.ClearBarbarianCamp.GetAssignedQuestData(geneva, ruleset.Quests["Clear Barbarian Camp"], babylon);
        Assert.Equal((camp.Position.x.ToString(System.Globalization.CultureInfo.InvariantCulture), camp.Position.y.ToString(System.Globalization.CultureInfo.InvariantCulture)), (x, y));
        var assigned = new AssignedQuest(game, ruleset.Quests["Clear Barbarian Camp"], geneva, babylon, 0, x, y);
        var actions = QuestName.ClearBarbarianCamp.GetNotificationActions(geneva, assigned);
        Assert.IsType<LocationAction>(actions[0]);
        Assert.IsType<DiplomacyAction>(actions[1]);
        Assert.False(QuestName.ClearBarbarianCamp.IsComplete(geneva, assigned));
        Assert.False(QuestName.ClearBarbarianCamp.IsObsolete(geneva, assigned));
        camp.RemoveImprovement();
        Assert.True(QuestName.ClearBarbarianCamp.IsObsolete(geneva, assigned));
    }

    [Fact]
    public void Implementations_FindNaturalWonder_AndFindPlayer()
    {
        SetUpCities();
        var wonder = QuestName.FindNaturalWonder.IsValid(geneva, babylon);
        Assert.Equal(game.TileMap.NaturalWonders.Count > 0, wonder);
        if (wonder)
        {
            var (name, _) = QuestName.FindNaturalWonder.GetAssignedQuestData(geneva, new Quest(), babylon);
            Assert.Contains(name, game.TileMap.NaturalWonders);
            var assigned = new AssignedQuest(game, new Quest { Name = "Find Natural Wonder" }, geneva, babylon, 0, name);
            Assert.False(QuestName.FindNaturalWonder.IsComplete(geneva, assigned));
            babylon.NaturalWonders.Add(name);
            Assert.True(QuestName.FindNaturalWonder.IsComplete(geneva, assigned));
            Assert.False(QuestName.FindNaturalWonder.IsValid(geneva, babylon) && game.TileMap.NaturalWonders.Count == 1);
        }

        Assert.True(babylon.Knows(greece)); // the majors met when the game was set up
        var findable = QuestName.FindPlayer.IsValid(geneva, babylon);
        var greeceAssigned = new AssignedQuest(game, new Quest { Name = "Find Player" }, geneva, babylon, 0, "Greece");
        Assert.Equal(babylon.HasMetCivTerritory(greece), QuestName.FindPlayer.IsComplete(geneva, greeceAssigned));
        Assert.Equal(!babylon.HasMetCivTerritory(greece), findable);
        if (findable)
            Assert.Equal(("Greece", ""), QuestName.FindPlayer.GetAssignedQuestData(geneva, new Quest(), babylon));
        Assert.False(QuestName.FindPlayer.IsComplete(geneva, new AssignedQuest(game, new Quest { Name = "Find Player" }, geneva, babylon, 0, "Nobody")));
        Assert.True(QuestName.FindPlayer.IsObsolete(geneva, new AssignedQuest(game, new Quest { Name = "Find Player" }, geneva, babylon, 0, "Nobody")));
        Assert.False(QuestName.FindPlayer.IsObsolete(geneva, greeceAssigned));
    }

    [Fact]
    public void Implementations_GiveGold_PledgeToProtect_AndDenounce()
    {
        SetUpCities();
        Assert.False(QuestName.GiveGold.IsValid(geneva, babylon)); // nobody bullied us
        Assert.False(QuestName.PledgeToProtect.IsValid(geneva, babylon));
        Assert.False(QuestName.DenounceCiv.IsValid(geneva, babylon));
        geneva.GetDiplomacyManager(greece)!.SetFlag(DiplomacyFlags.Bullied, 5);
        geneva.GetDiplomacyManager(babylon)!.SetFlag(DiplomacyFlags.Bullied, 9);
        Assert.True(QuestName.GiveGold.IsValid(geneva, babylon));
        Assert.Equal(("Babylon", ""), QuestName.GiveGold.GetAssignedQuestData(geneva, new Quest(), greece)); // most recent bully: the highest remaining countdown
        Assert.True(QuestName.PledgeToProtect.IsValid(geneva, babylon));
        Assert.Equal(("Babylon", ""), QuestName.PledgeToProtect.GetAssignedQuestData(geneva, new Quest(), greece));
        var quest = new AssignedQuest(game, new Quest { Name = "Pledge to Protect" }, geneva, greece, 0, "Babylon");
        Assert.False(QuestName.PledgeToProtect.IsComplete(geneva, quest));
        Assert.False(QuestName.GiveGold.IsComplete(geneva, quest));

        // Denounce: Greece must know the bully (Babylon), not be at war with it, not have denounced it already, not be human-vs-human
        Assert.False(QuestName.DenounceCiv.IsValid(geneva, new UncivCiv("Nobody"))); // the challenger does not know the bully
        greece.GetDiplomacyManager(babylon)!.DiplomaticStatus = DiplomaticStatus.Peace;
        Assert.True(QuestName.DenounceCiv.IsValid(geneva, greece));
        Assert.Equal(("Babylon", ""), QuestName.DenounceCiv.GetAssignedQuestData(geneva, new Quest(), greece));
        var denounce = new AssignedQuest(game, new Quest { Name = "Denounce Civilization" }, geneva, greece, 0, "Babylon");
        Assert.False(QuestName.DenounceCiv.IsComplete(geneva, denounce));
        greece.GetDiplomacyManager(babylon)!.SetFlag(DiplomacyFlags.Denunciation, 10);
        Assert.True(QuestName.DenounceCiv.IsComplete(geneva, denounce));
        Assert.False(QuestName.DenounceCiv.IsValid(geneva, greece));
        greece.GetDiplomacyManager(babylon)!.RemoveFlag(DiplomacyFlags.Denunciation);
        greece.GetDiplomacyManager(babylon)!.DiplomaticStatus = DiplomaticStatus.War;
        Assert.False(QuestName.DenounceCiv.IsValid(geneva, greece));
        greece.GetDiplomacyManager(babylon)!.DiplomaticStatus = DiplomaticStatus.Peace;
        greece.PlayerType = PlayerType.Human;
        Assert.False(QuestName.DenounceCiv.IsValid(geneva, greece)); // human vs human
        Assert.False(QuestName.DenounceCiv.IsObsolete(geneva, denounce));
    }

    [Fact]
    public void Implementations_ConquerAndBullyCityState_NeedAnotherCityStateNearby()
    {
        SetUpCities();
        Assert.False(QuestName.ConquerCityState.IsValid(geneva, babylon));
        Assert.False(QuestName.BullyCityState.IsValid(geneva, babylon));
        var quest = new AssignedQuest(game, new Quest { Name = "Bully City State" }, geneva, babylon, 0, "Geneva");
        Assert.False(QuestName.BullyCityState.IsComplete(geneva, quest));
        Assert.False(QuestName.ConquerCityState.IsComplete(geneva, quest));
        Assert.False(QuestName.BullyCityState.IsObsolete(geneva, quest)); // Geneva itself is alive
        Assert.True(QuestName.ConquerCityState.IsObsolete(geneva, new AssignedQuest(game, new Quest { Name = "Conquer City State" }, geneva, babylon, 0, "Nobody")));
    }

    [Fact]
    public void Implementations_Route()
    {
        SetUpCities();
        Assert.False(QuestName.Route.IsValid(geneva, new UncivCiv("Nobody") { CivId = "Nobody" })); // challenger without cities
        var connected = babylon.IsCapitalConnectedToCity(geneva.GetCapital()!);
        var quest = new AssignedQuest(game, new Quest { Name = "Route" }, geneva, babylon);
        Assert.Equal(connected, QuestName.Route.IsComplete(geneva, quest));
        // capitals are far apart on the Tiny map or on another continent; whatever the geometry, validity is a plain boolean without exceptions
        _ = QuestName.Route.IsValid(geneva, babylon);
    }

    [Fact]
    public void Implementations_ConnectResource_ConstructWonder_GreatPerson()
    {
        SetUpCities();
        _ = QuestName.ConnectResource.IsValid(geneva, babylon);
        var resource = ruleset.TileResources.Values.First();
        var connect = new AssignedQuest(game, new Quest { Name = "Connect Resource" }, geneva, babylon, 0, resource.Name);
        Assert.False(QuestName.ConnectResource.IsComplete(geneva, connect));
        babylon.DetailedCivResources.Add(resource, "Tiles", 1);
        Assert.True(QuestName.ConnectResource.IsComplete(geneva, connect));

        var construct = new AssignedQuest(game, new Quest { Name = "Construct Wonder" }, geneva, babylon, 0, "Pyramids");
        Assert.False(QuestName.ConstructWonder.IsComplete(geneva, construct));
        Assert.False(QuestName.ConstructWonder.IsObsolete(geneva, construct));
        babylon.Cities[0].CityConstructions.BuiltBuildings.Add("Pyramids");
        Assert.True(QuestName.ConstructWonder.IsComplete(geneva, construct));
        Assert.False(QuestName.ConstructWonder.IsObsolete(geneva, construct));
        var greeceConstruct = new AssignedQuest(game, new Quest { Name = "Construct Wonder" }, geneva, greece, 0, "Pyramids");
        Assert.True(QuestName.ConstructWonder.IsObsolete(geneva, greeceConstruct)); // somebody else built it
        _ = QuestName.ConstructWonder.IsValid(geneva, greece);

        _ = QuestName.GreatPerson.IsValid(geneva, babylon);
        var gp = new AssignedQuest(game, new Quest { Name = "Acquire Great Person" }, geneva, babylon, 0, "Great Scientist");
        Assert.False(QuestName.GreatPerson.IsComplete(geneva, gp));
    }

    [Fact]
    public void Implementations_SpreadReligion_NeedsAMajorReligion()
    {
        SetUpCities();
        Assert.False(QuestName.SpreadReligion.IsValid(geneva, babylon));
        Assert.Throws<InvalidOperationException>(() => QuestName.SpreadReligion.GetAssignedQuestData(geneva, new Quest(), babylon));
        var quest = new AssignedQuest(game, new Quest { Name = "Spread Religion" }, geneva, babylon, 0, "My Faith", "My Faith");
        Assert.True(QuestName.SpreadReligion.IsComplete(geneva, quest) == (geneva.GetCapital()!.Religion.GetMajorityReligion() == null)); // no such religion: only "no majority" matches
    }
}
