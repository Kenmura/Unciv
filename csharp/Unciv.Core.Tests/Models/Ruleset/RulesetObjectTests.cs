using Unciv.Core.Tests.Models.Ruleset.Unique;
using Unciv.Json;
using Unciv.Models;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.StatTypes;
using Unciv.Models.Translations;
using PolicyObject = Unciv.Models.Ruleset.Policy;

namespace Unciv.Core.Tests.Models.Ruleset;

/// <summary>
/// The ruleset base objects of T030. The Kotlin tests for them (VictoryAvailabilityTests, VictoryMilestoneTests) need a whole game
/// (TestGame, T035) and are ported with that ticket; these cover the deserialization defaults and the logic that does not need one.
/// </summary>
public class RulesetObjectTests
{
    public sealed class StatsObject : RulesetStatsObject
    {
        public override UniqueTarget GetUniqueTarget() => UniqueTarget.Building;

        public override string MakeLink() => "";
    }

    private static T Read<T>(string json) => UncivJson.Deserialize<T>(json)!;

    private static GameContext ReligionContext(bool religionEnabled) =>
        new(gameInfo: new FakeGameInfo { ReligionEnabled = religionEnabled });

    // region RulesetObject

    [Fact]
    public void RulesetObjectsReadNameUniquesAndBuildUniqueObjects()
    {
        var belief = Read<Belief>("""{ "name": "Tithe", "type": "Pantheon", "uniques": ["[+1 Gold] from every [Farm]", "[+1 Gold] from every [Farm]"] }""");
        Assert.Equal("Tithe", belief.Name);
        Assert.Equal("Tithe", belief.ToString());
        Assert.Equal("", belief.OriginRuleset);
        Assert.Equal(2, belief.Uniques.Count);
        Assert.Equal(2, belief.UniqueObjects.Count);
        Assert.All(belief.UniqueObjects, it => Assert.Equal(UniqueTarget.FollowerBelief, it.SourceObjectType));
        Assert.Equal(2, belief.UniqueMap.GetUniques(UniqueType.StatsFromObject).Count());
        Assert.Equal("", new Tutorial().Name);
    }

    // region Specialist

    [Fact]
    public void SpecialistReadsColorAndGreatPersonPoints()
    {
        var specialist = Read<Specialist>("""{ "name": "Scientist", "science": 3, "color": [1, 2, 3], "greatPersonPoints": { "Great Scientist": 3 } }""");
        Assert.Equal("Scientist", specialist.Name);
        Assert.Equal(3f, specialist.Science);
        Assert.Equal([1, 2, 3], specialist.Color);
        Assert.Equal(3, specialist.GreatPersonPoints.Get("Great Scientist"));
        Assert.Empty(new Specialist().Color);
    }

    [Fact]
    public void StatsSubclassesReadStatsAndOwnFieldsFromOneObject()
    {
        var specialist = Read<StatsObject>("""{ "NAME": "S", "Gold": "2.5", "faith": 1, "unknown": [1], "originRuleset": "X", "uniques": ["u"] }""");
        Assert.Equal(("S", 2.5f, 1f, 0f), (specialist.Name, specialist.Gold, specialist.Faith, specialist.Food));
        Assert.Equal("X", specialist.OriginRuleset);
        Assert.Equal(["u"], specialist.Uniques);
        Assert.Single(specialist.UniqueObjects);
        Assert.Throws<System.Text.Json.JsonException>(() => Read<Specialist>("""{ "gold": true }"""));
        Assert.Throws<System.Text.Json.JsonException>(() => Read<Specialist>("[]"));
        Assert.Throws<NotSupportedException>(() => UncivJson.Serialize(new Specialist()));
    }

    // region Speed

    [Fact]
    public void SpeedDefaultsFollowModifierOnlyAtConstruction()
    {
        var speed = new Speed();
        Assert.Equal("Quick", Speed.Default);
        Assert.Equal("Standard", Speed.DefaultForSimulation);
        Assert.Equal("Quick", new Unciv.Models.Metadata.GameParameters().Speed);
        Assert.Equal(1f, speed.GoldCostModifier);
        Assert.Equal(6.5f, speed.CityStateTributeScalingInterval);
        Assert.Equal(6, speed.ReligiousPressureAdjacentCity);
        Assert.Equal(10, speed.PeaceDealDuration);
        Assert.Equal(30, speed.DealDuration);
        Assert.Equal(-4000f, speed.StartYear);

        // Like Kotlin, the other modifiers do not follow a modifier read from json
        var json = Read<Speed>("""{ "name": "Epic", "modifier": 1.5, "goldCostModifier": 2, "turns": [] }""");
        Assert.Equal(1.5f, json.Modifier);
        Assert.Equal(2f, json.GoldCostModifier);
        Assert.Equal(1f, json.ProductionCostModifier);
        Assert.Equal(UniqueTarget.Speed, json.GetUniqueTarget());
        Assert.Equal("Speed/Epic", json.MakeLink());
    }

    [Fact]
    public void SpeedStatCostModifiersMapCostStatsAndDefaultToOne()
    {
        var speed = Read<Speed>("""{ "productionCostModifier": 2, "goldCostModifier": 3, "scienceCostModifier": 4, "cultureCostModifier": 5, "faithCostModifier": 6 }""");
        Assert.Equal(7, speed.StatCostModifiers.Count);
        Assert.Equal(2f, speed.GetStatCostModifier(Stat.Production));
        Assert.Equal(3f, speed.GetStatCostModifier(Stat.Gold));
        Assert.Equal(4f, speed.GetStatCostModifier(Stat.Science));
        Assert.Equal(5f, speed.GetStatCostModifier(Stat.Culture));
        Assert.Equal(6f, speed.GetStatCostModifier(Stat.Faith));
        Assert.Equal(1f, speed.GetStatCostModifier(Stat.Food));
        Assert.Equal(1f, speed.GetStatCostModifier(Stat.Happiness));
    }

    private static Speed SpeedWithTurns() => Read<Speed>("""
        { "startYear": -4000, "turns": [
            { "yearsPerTurn": 50, "untilTurn": 10 },
            { "yearsPerTurn": 25.5, "untilTurn": 20 },
            { "yearsPerTurn": 1, "untilTurn": 30 } ] }
        """);

    [Fact]
    public void SpeedParsesYearsPerTurnAndTotalTurns()
    {
        var speed = SpeedWithTurns();
        Assert.Equal(3, speed.YearsPerTurn.Count);
        Assert.Equal(new Speed.YearsPerTurnRow(25.5f, 20), speed.YearsPerTurn[1]);
        Assert.Equal(30, speed.NumTotalTurns());
        Assert.Throws<InvalidOperationException>(() => new Speed().NumTotalTurns());
    }

    [Theory]
    [InlineData(-5, -4000f)]  // never projected backwards
    [InlineData(0, -4000f)]
    [InlineData(1, -3950f)]
    [InlineData(10, -3500f)]
    [InlineData(11, -3474.5f)]
    [InlineData(20, -3245f)]
    [InlineData(21, -3244f)]
    [InlineData(30, -3235f)]
    [InlineData(40, -3225f)] // the last interval extends past its end
    public void SpeedTurnToYearInterpolatesPerInterval(int turn, float expected)
    {
        Assert.Equal(expected, SpeedWithTurns().TurnToYear(turn));
    }

    [Fact]
    public void SpeedRowMissingAKeyThrows()
    {
        var speed = Read<Speed>("""{ "turns": [ { "yearsPerTurn": 5 } ] }""");
        Assert.Throws<KeyNotFoundException>(() => speed.YearsPerTurn);
    }

    // region Policy and PolicyBranch

    [Fact]
    public void PolicyBranchTypeIsDerivedFromClassAndName()
    {
        Assert.Equal(PolicyObject.PolicyBranchTypes.BranchStart, Read<PolicyBranch>("""{ "name": "Tradition" }""").PolicyBranchType);
        Assert.Equal(PolicyObject.PolicyBranchTypes.BranchComplete, Read<PolicyObject>("""{ "name": "Tradition Complete" }""").PolicyBranchType);
        Assert.Equal(PolicyObject.PolicyBranchTypes.Member, Read<PolicyObject>("""{ "name": "Aristocracy" }""").PolicyBranchType);
        Assert.True(PolicyObject.IsBranchCompleteByName("Liberty Complete"));
        Assert.False(PolicyObject.IsBranchCompleteByName("Complete Liberty"));
        Assert.False(PolicyObject.IsBranchCompleteByName("Complete"));
        Assert.Equal(UniqueTarget.Policy, new PolicyObject().GetUniqueTarget());
        Assert.Equal("Policy/X", new PolicyObject { Name = "X" }.MakeLink());
    }

    [Fact]
    public void PolicyBranchReadsPoliciesPrioritiesAndEra()
    {
        var branch = Read<PolicyBranch>("""
            { "name": "Tradition", "era": "Ancient era", "priorities": { "Cultural": 3 },
              "policies": [ { "name": "Aristocracy", "row": 1, "column": 2, "requires": ["Tradition"] } ] }
            """);
        Assert.Equal("Ancient era", branch.Era);
        Assert.Equal(3, branch.Priorities["Cultural"]);
        var policy = Assert.Single(branch.Policies);
        Assert.Equal((1, 2), (policy.Row, policy.Column));
        Assert.Equal(["Tradition"], policy.Requires);
        Assert.Null(new PolicyObject().Requires);
    }

    [Fact]
    public void PolicyBranchIsLateinit()
    {
        Assert.Throws<InvalidOperationException>(() => new PolicyObject().Branch);
    }

    [Fact]
    public void PolicyFiltersMatchNameBranchAndUniques()
    {
        var branch = Read<PolicyBranch>("""{ "name": "Tradition" }""");
        branch.Branch = branch;
        var policy = Read<PolicyObject>("""{ "name": "Aristocracy", "uniques": ["[+1 Gold] from every [Farm]"] }""");
        policy.Branch = branch;

        Assert.True(policy.MatchesFilter("All"));
        Assert.True(policy.MatchesFilter("all"));
        Assert.True(policy.MatchesFilter("Aristocracy"));
        Assert.True(policy.MatchesFilter("[Tradition] branch"));
        Assert.True(branch.MatchesFilter("[Tradition] branch"));
        Assert.False(policy.MatchesFilter("Tradition"));
        Assert.False(policy.MatchesFilter("[Liberty] branch"));
        Assert.True(policy.MatchesFilter("{Aristocracy} {[Tradition] branch}"));
        Assert.False(policy.MatchesFilter("non-[Aristocracy]"));
        Assert.True(policy.MatchesFilter("non-[Liberty]"));
        // Unique tags are matched with and without a state
        Assert.True(policy.MatchesFilter("[] from every []"));
        Assert.True(policy.MatchesFilter("[] from every []", GameContext.EmptyState));
        Assert.False(policy.MatchesFilter("nothing like this"));
        Assert.False(policy.MatchesFilter("nothing like this", GameContext.EmptyState));
    }

    [Fact]
    public void PolicyDescriptionListsVisibleUniquesAndSkipsAvailabilityOnes()
    {
        var policy = Read<PolicyObject>("""
            { "name": "Aristocracy",
              "uniques": ["[+1 Gold] from every [Farm]", "Only available <when religion is enabled>", "Unavailable <when religion is enabled> <hidden from users>"] }
            """);
        policy.Branch = Read<PolicyBranch>("""{ "name": "Tradition" }""");
        string line = "[+1 Gold] from every [Farm]".Tr();
        Assert.Equal("Aristocracy".Tr() + "\n• " + line, policy.GetDescription());

        var branch = Read<PolicyBranch>("""{ "name": "Tradition", "uniques": ["[+1 Gold] from every [Farm]"] }""");
        Assert.Equal("• " + line, branch.GetDescription());
        Assert.Equal("", Read<PolicyObject>("""{ "name": "Aristocracy Complete" }""").GetDescription());
    }

    // region Belief

    [Fact]
    public void BeliefTypeProperties()
    {
        Assert.True(BeliefType.Pantheon.IsFollower());
        Assert.True(BeliefType.Follower.IsFollower());
        Assert.False(BeliefType.Founder.IsFollower());
        Assert.True(BeliefType.Founder.IsFounder());
        Assert.True(BeliefType.Enhancer.IsFounder());
        Assert.False(BeliefType.None.IsFounder());
        Assert.False(BeliefType.None.IsFollower());
        Assert.False(BeliefType.Any.IsFounder());
        Assert.Equal("#44c6cc", BeliefType.Pantheon.GetColor());
        Assert.Equal("#c00000", BeliefType.Founder.GetColor());
        Assert.Equal("#ccaa44", BeliefType.Follower.GetColor());
        Assert.Equal("#72cc45", BeliefType.Enhancer.GetColor());
        Assert.Equal("", BeliefType.None.GetColor());
        Assert.Equal("", BeliefType.Any.GetColor());
        Assert.Null(BeliefType.None.GetSubCategory());
        Assert.Null(BeliefType.Any.GetSubCategory());
        Assert.Equal("Founder", BeliefType.Founder.GetSubCategory());
        // The enum order is the Civilopedia sort group; Reformation (B004) is appended after the Kotlin entries
        Assert.Equal([0, 1, 2, 3, 4, 5, 6], Enum.GetValues<BeliefType>().Select(it => (int)it));
    }

    [Fact]
    public void BeliefUniqueTargetFollowsType()
    {
        Assert.Equal(BeliefType.None, new Belief().Type);
        Assert.Equal(UniqueTarget.FollowerBelief, new Belief().GetUniqueTarget());
        Assert.Equal(UniqueTarget.FollowerBelief, new Belief(BeliefType.Pantheon).GetUniqueTarget());
        Assert.Equal(UniqueTarget.FounderBelief, new Belief(BeliefType.Founder).GetUniqueTarget());
        Assert.Equal(UniqueTarget.FounderBelief, new Belief(BeliefType.Enhancer).GetUniqueTarget());
        Assert.Equal("Belief/X", new Belief { Name = "X" }.MakeLink());
    }

    [Fact]
    public void BeliefFilters()
    {
        var belief = Read<Belief>("""{ "name": "Tithe", "type": "Founder", "uniques": ["[+1 Gold] from every [Farm]"] }""");
        Assert.True(belief.MatchesFilter("Tithe"));
        Assert.True(belief.MatchesFilter("All"));
        Assert.True(belief.MatchesFilter("Founder"));
        Assert.False(belief.MatchesFilter("Follower"));
        Assert.True(belief.MatchesFilter("non-[Follower]"));
        Assert.True(belief.MatchesFilter("{Tithe} {Founder}"));
        Assert.True(belief.MatchesFilter("[] from every []"));
        Assert.True(belief.MatchesFilter("[] from every []", GameContext.EmptyState));
        // Without multiFilter, the operators are not interpreted
        Assert.False(belief.MatchesFilter("non-[Follower]", multiFilter: false));
        Assert.False(belief.MatchesFilter("{Tithe} {Founder}", GameContext.EmptyState, multiFilter: false));
        Assert.True(belief.MatchesFilter("Founder", multiFilter: false));
        Assert.True(belief.MatchesFilter("[] from every []", GameContext.EmptyState, multiFilter: false));
        Assert.False(belief.MatchesFilter("nothing", multiFilter: false));
    }

    // region Event

    [Fact]
    public void EventReadsPresentationTextAndChoices()
    {
        var evt = Read<Event>("""{ "name": "Plague", "text": "Oh no", "presentation": "None", "choices": [ { "name": "A", "text": "Pray", "keyShortcut": "a" } ] }""");
        Assert.Equal(Event.PresentationType.None, evt.Presentation);
        Assert.Equal("Oh no", evt.Text);
        Assert.Equal("Event/Plague", evt.MakeLink());
        var choice = Assert.Single(evt.Choices);
        Assert.Equal(("Pray", "a", ""), (choice.Text, choice.KeyShortcut, choice.MakeLink()));
        Assert.Equal(UniqueTarget.EventChoice, choice.GetUniqueTarget());
        Assert.Equal(UniqueTarget.Event, evt.GetUniqueTarget());
        Assert.Equal(Event.PresentationType.Alert, new Event().Presentation);
    }

    [Fact]
    public void EventMatchingChoices()
    {
        var evt = Read<Event>("""
            { "name": "E", "choices": [
                { "name": "Hidden", "uniques": ["Unavailable <when religion is enabled>"] },
                { "name": "Second" }, { "name": "Third" } ] }
            """);
        var on = ReligionContext(true);
        var off = ReligionContext(false);
        Assert.Equal(["Second", "Third"], evt.GetMatchingChoices(on)!.Select(it => it.Name));
        Assert.Equal(["Hidden", "Second", "Third"], evt.GetMatchingChoices(off)!.Select(it => it.Name));

        evt.Uniques.Add("Only the first available choice is offered");
        evt = Read<Event>("""
            { "name": "E", "uniques": ["Only the first available choice is offered"], "choices": [
                { "name": "Hidden", "uniques": ["Unavailable <when religion is enabled>"] },
                { "name": "Second" }, { "name": "Third" } ] }
            """);
        Assert.Equal(["Second"], evt.GetMatchingChoices(on)!.Select(it => it.Name));
        Assert.Equal(["Hidden"], evt.GetMatchingChoices(off)!.Select(it => it.Name));
    }

    [Fact]
    public void EventMatchingChoicesNullAndEmptyCases()
    {
        // Event not available
        var unavailable = Read<Event>("""{ "name": "E", "uniques": ["Unavailable <when religion is enabled>"], "choices": [ { "name": "A" } ] }""");
        Assert.Null(unavailable.GetMatchingChoices(ReligionContext(true)));
        Assert.NotNull(unavailable.GetMatchingChoices(ReligionContext(false)));
        // No choices: empty, not null
        var noChoices = Read<Event>("""{ "name": "E" }""");
        Assert.Empty(noChoices.GetMatchingChoices(GameContext.EmptyState)!);
        // Only unavailable choices: null
        var allHidden = Read<Event>("""{ "name": "E", "choices": [ { "name": "A", "uniques": ["Unavailable <when religion is enabled>"] } ] }""");
        Assert.Null(allHidden.GetMatchingChoices(ReligionContext(true)));
    }

    // region Quest

    [Fact]
    public void QuestReadsFieldsAndDefaults()
    {
        var quest = Read<Quest>("""
            { "name": "Clear Barbarian Camp", "description": "Do it", "type": "Global", "influence": 12.5, "duration": 30,
              "minimumCivs": 3, "weightForCityStateType": { "Militaristic": 2 } }
            """);
        Assert.Equal(QuestName.ClearBarbarianCamp, quest.QuestNameInstance);
        Assert.True(quest.IsGlobal());
        Assert.False(quest.IsIndividual());
        Assert.Equal((12.5f, 30, 3, "Do it"), (quest.Influence, quest.Duration, quest.MinimumCivs, quest.Description));
        Assert.Equal(2f, quest.WeightForCityStateType["Militaristic"]);

        var defaults = new Quest();
        Assert.Equal(QuestType.Individual, defaults.Type);
        Assert.True(defaults.IsIndividual());
        Assert.Equal(40f, defaults.Influence);
        Assert.Equal(1, defaults.MinimumCivs);
        Assert.Equal(0, defaults.Duration);
        Assert.Equal(QuestName.None, defaults.QuestNameInstance);
    }

    [Fact]
    public void QuestNameValuesAndFind()
    {
        Assert.Equal(19, Enum.GetValues<QuestName>().Length);
        Assert.Equal("", QuestName.None.GetValue());
        Assert.Equal("Acquire Great Person", QuestName.GreatPerson.GetValue());
        Assert.Equal("Contest Technologies", QuestName.ContestTech.GetValue());
        foreach (var name in Enum.GetValues<QuestName>())
            Assert.Equal(name, QuestNameExtensions.Find(name.GetValue()));
        Assert.Equal(QuestName.None, QuestNameExtensions.Find("No such quest"));
        Assert.Equal(QuestName.None, QuestNameExtensions.Find("route")); // case sensitive
    }

    // region RuinReward

    [Fact]
    public void RuinRewardExcludesDifficulties()
    {
        var reward = Read<RuinReward>("""{ "name": "Gold", "notification": "Found [x]", "excludedDifficulties": ["Settler"], "weight": 5, "color": "#fff" }""");
        Assert.Equal((5, "Found [x]", "#fff", ""), (reward.Weight, reward.Notification, reward.Color, reward.MakeLink()));
        Assert.Equal(UniqueTarget.Ruins, reward.GetUniqueTarget());
        Assert.True(((IHasUniques)reward).IsUnavailableBySettings(new FakeGameInfo { DifficultyName = "Settler" }));
        Assert.False(((IHasUniques)reward).IsUnavailableBySettings(new FakeGameInfo { DifficultyName = "Prince" }));
        Assert.Equal(1, new RuinReward().Weight);
    }

    [Fact]
    public void RuinRewardFallsBackToTheDefaultAvailabilityTest()
    {
        var reward = Read<RuinReward>("""{ "name": "Prophet", "uniques": ["Only available <when religion is enabled>"] }""");
        Assert.True(((IHasUniques)reward).IsUnavailableBySettings(new FakeGameInfo { ReligionEnabled = false }));
        Assert.False(((IHasUniques)reward).IsUnavailableBySettings(new FakeGameInfo { ReligionEnabled = true }));
    }

    // region GlobalUniques

    [Fact]
    public void GlobalUniquesDefaultsAndHasUniques()
    {
        var empty = new GlobalUniques();
        Assert.Equal("Global Uniques", empty.Name);
        Assert.Equal("Tutorial/Global Uniques", empty.MakeLink());
        Assert.Equal(UniqueTarget.GlobalUniques, empty.GetUniqueTarget());
        Assert.False(empty.HasUniques());
        Assert.True(Read<GlobalUniques>("""{ "unitUniques": ["[+1 Movement] <for [All] units>"] }""").HasUniques());
        Assert.True(Read<GlobalUniques>("""{ "uniques": ["[+1 Gold] from every [Farm]"] }""").HasUniques());
        // A json name overrides the default
        Assert.Equal("Other", Read<GlobalUniques>("""{ "name": "Other" }""").Name);
    }

    [Fact]
    public void GlobalUniquesHiddenUniquesDoNotCount()
    {
        Assert.False(Read<GlobalUniques>("""{ "uniques": ["[+1 Gold] from every [Farm] <hidden from users>"] }""").HasUniques());
    }

    [Fact]
    public void GlobalUniquesSourceDescription()
    {
        static string Describe(string text) =>
            GlobalUniques.GetUniqueSourceDescription(new Unciv.Models.Ruleset.Unique.Unique(text, UniqueTarget.Global, "x"));

        Assert.Equal("Global Effect", Describe("[+1 Gold] from every [Farm]"));
        Assert.Equal("Golden Age", Describe("[+1 Gold] from every [Farm] <during a Golden Age>"));
        Assert.Equal("Happiness", Describe("[+1 Gold] from every [Farm] <while the empire is happy>"));
        Assert.Equal("We Love The King Day", Describe("[+1 Gold] from every [Farm] <during We Love The King Day>"));
        Assert.Equal("Global Effect", Describe("[+1 Gold] from every [Farm] <when religion is enabled>"));
        Assert.Equal("Golden Age", new Unciv.Models.Ruleset.Unique.Unique("[+1 Gold] from every [Farm] <during a Golden Age>", UniqueTarget.Global, "x").GetSourceNameForUser());
    }

    [Fact]
    public void GlobalUniquesCombineConcatenatesInOrder()
    {
        var first = Read<GlobalUniques>("""{ "uniques": ["a"], "unitUniques": ["ua"] }""");
        var second = Read<GlobalUniques>("""{ "uniques": ["b"], "unitUniques": ["ub"] }""");
        var option = new ModOptions { Uniques = ["c"] };
        var combined = GlobalUniques.Combine(first, second, option);
        Assert.Equal(["a", "b", "c"], combined.Uniques);
        Assert.Equal(["ua", "ub"], combined.UnitUniques);
        Assert.Equal("Global Uniques", combined.Name);
        // Sources are untouched
        Assert.Equal(["a"], first.Uniques);
        Assert.Equal(["ua"], first.UnitUniques);
        Assert.Equal(["a"], GlobalUniques.Combine(first).Uniques);
    }

    // region ModOptions

    [Fact]
    public void ModOptionsDefaults()
    {
        var options = new ModOptions();
        Assert.Equal("ModOptions", options.Name);
        Assert.False(options.IsBaseRuleset);
        Assert.Equal("master", options.DefaultBranch);
        Assert.Equal(("", "", "", 0), (options.ModUrl, options.Author, options.LastUpdated, options.ModSize));
        Assert.Null(options.Unitset);
        Assert.Null(options.Tileset);
        Assert.Empty(options.Topics);
        Assert.Empty(options.BeliefsToRemove);
        Assert.Equal(new ModConstants(), options.Constants);
        Assert.Equal(UniqueTarget.ModOptions, options.GetUniqueTarget());
    }

    [Fact]
    public void ModOptionsReadsJson()
    {
        var options = Read<ModOptions>("""
            { "isBaseRuleset": true, "unitset": "Civ5", "tileset": "Fantasy", "uniques": ["[+1 Gold] from every [Farm]"],
              "buildingsToRemove": ["Granary", "Granary", "Mill"], "policyBranchesToRemove": ["Tradition"], "victoriesToRemove": ["Time"],
              "constants": { "maxXPfromBarbarians": 12 }, "topics": ["a"], "modSize": 7, "author": "me" }
            """);
        Assert.True(options.IsBaseRuleset);
        Assert.Equal(("Civ5", "Fantasy"), (options.Unitset, options.Tileset));
        Assert.Equal(2, options.BuildingsToRemove.Count);
        Assert.Contains("Mill", options.BuildingsToRemove);
        Assert.Contains("Tradition", options.PolicyBranchesToRemove);
        Assert.Contains("Time", options.VictoriesToRemove);
        Assert.Equal(12, options.Constants.MaxXPfromBarbarians);
        Assert.Equal(["a"], options.Topics);
        Assert.Equal((7, "me"), (options.ModSize, options.Author));
        Assert.Single(options.UniqueObjects);
        Assert.Equal(UniqueTarget.ModOptions, options.UniqueObjects[0].SourceObjectType);
        Assert.Single(options.UniqueMap.GetUniques(UniqueType.StatsFromObject));
    }

    // region Tutorial

    [Fact]
    public void TutorialReadsCategoryAndSteps()
    {
        var tutorial = Read<Tutorial>("""{ "name": "Introduction", "category": "Basics", "steps": ["one", "two"] }""");
        Assert.Equal(("Introduction", "Basics"), (tutorial.Name, tutorial.Category));
        Assert.Equal(["one", "two"], tutorial.Steps);
        Assert.Equal("Tutorial/Introduction", tutorial.MakeLink());
        Assert.Equal(UniqueTarget.Tutorial, tutorial.GetUniqueTarget());
        var plain = new Tutorial();
        Assert.Null(plain.Category);
        Assert.Null(plain.Steps);
    }

    // region Victory

    private static Victory VictoryWith(params string[] milestones) =>
        Read<Victory>($$"""{ "name": "Domination", "milestones": [{{string.Join(",", milestones.Select(it => $"\"{it}\""))}}] }""");

    [Fact]
    public void VictoryDefaultsAndMakeLink()
    {
        var victory = new Victory();
        Assert.Equal("Do things to win!", victory.VictoryScreenHeader);
        Assert.False(victory.HiddenInVictoryScreen);
        Assert.StartsWith("Your civilization stands above all others!", victory.VictoryString, StringComparison.Ordinal);
        Assert.StartsWith("You have been defeated.", victory.DefeatString, StringComparison.Ordinal);
        Assert.Empty(victory.Milestones);
        Assert.Empty(victory.MilestoneObjects);
        Assert.False(victory.EnablesMaxTurns());
        Assert.Equal(UniqueTarget.Victory, victory.GetUniqueTarget());
        Assert.Equal("Victory/Domination", VictoryWith().MakeLink());
    }

    [Fact]
    public void MilestonesMatchTheirTypeByPlaceholderText()
    {
        var victory = VictoryWith(
            "Build [Apollo Program]", "Anyone should build [Manhattan Project]", "Add all [Spaceship Parts] in capital", "Destroy all players",
            "Capture all capitals", "Complete [5] Policy branches", "Become the world religion", "Win diplomatic vote",
            "Have highest score after max turns", "Have more [Cities] than each player's [Cities]", "Have at least [100] [Gold]",
            "Influential over all civilizations", "Win the World Leader vote", "Unknown milestone");
        var milestones = victory.MilestoneObjects;
        Assert.Equal(14, milestones.Count);
        Assert.Equal(Enum.GetValues<MilestoneType>().Cast<MilestoneType?>().Append(null), milestones.Select(it => it.Type));
        Assert.Equal(["Apollo Program"], milestones[0].Params);
        Assert.Equal(["Cities", "Cities"], milestones[9].Params);
        Assert.Equal(["100", "Gold"], milestones[10].Params);
        Assert.Empty(milestones[3].Params);
        Assert.Equal("Build [Apollo Program]", milestones[0].UniqueDescription);
        Assert.True(victory.EnablesMaxTurns());
        Assert.Same(victory.MilestoneObjects, victory.MilestoneObjects);
    }

    [Fact]
    public void MilestoneTypeTexts()
    {
        Assert.Equal("Have at least [amount] [countable]", MilestoneType.HaveCountable.GetText());
        Assert.Equal("Win diplomatic vote", MilestoneType.WinDiplomaticVote.GetText());
        Assert.Equal("Influential over all civilizations", MilestoneType.InfluentialOverAllCivs.GetText());
        Assert.Equal("Win the World Leader vote", MilestoneType.WinWorldLeaderVote.GetText());
        Assert.Equal(13, Enum.GetValues<MilestoneType>().Length);
    }

    [Fact]
    public void MilestoneCountableToDoParsesLikeToIntOrNull()
    {
        Assert.Equal(100, VictoryWith("Have at least [100] [Gold]").MilestoneObjects[0].GetCountableToDo());
        Assert.Equal(5, VictoryWith("Have at least [+5] [Gold]").MilestoneObjects[0].GetCountableToDo());
        Assert.Equal(-3, VictoryWith("Have at least [-3] [Gold]").MilestoneObjects[0].GetCountableToDo());
        Assert.Equal(0, VictoryWith("Have at least [lots] [Gold]").MilestoneObjects[0].GetCountableToDo());
        Assert.Equal(0, VictoryWith("Have at least [99999999999] [Gold]").MilestoneObjects[0].GetCountableToDo());
        Assert.Equal(0, VictoryWith("Have at least [ 5] [Gold]").MilestoneObjects[0].GetCountableToDo());
        Assert.Equal(0, VictoryWith("Have at least [1.5] [Gold]").MilestoneObjects[0].GetCountableToDo());
        Assert.Equal(0, VictoryWith("Have at least [] [Gold]").MilestoneObjects[0].GetCountableToDo());
    }

    [Fact]
    public void VictorySpaceshipPartsCounterCountsDuplicates()
    {
        var victory = Read<Victory>("""{ "name": "Space", "requiredSpaceshipParts": ["Booster", "Booster", "Cockpit"] }""");
        Assert.Equal(2, victory.RequiredSpaceshipPartsAsCounter.Get("Booster"));
        Assert.Equal(1, victory.RequiredSpaceshipPartsAsCounter.Get("Cockpit"));
        Assert.Equal(3, victory.RequiredSpaceshipPartsAsCounter.SumValues());
        Assert.Equal(0, new Victory().RequiredSpaceshipPartsAsCounter.SumValues());
    }

    [Fact]
    public void VictoryEnumsMatchKotlin()
    {
        Assert.Equal(["Completed", "Partially", "Incomplete"], Enum.GetNames<Victory.CompletionStatus>());
        Assert.Equal(["Production", "Gold", "Culture", "Science", "Faith", "Military", "CityStates", "Score"], Enum.GetNames<Victory.FocusType>());
    }

    // region RulesetName

    [Fact]
    public void RulesetNameIsAValueRecord()
    {
        Assert.Equal(new RulesetName("a", "b", "c"), new RulesetName("a", "b", "c"));
        Assert.NotEqual(new RulesetName("a", "b", "c"), new RulesetName("a", "b", "d"));
    }

    [Fact]
    public void TutorialTriggerStillAvailable()
    {
        // Tutorial names are matched with TutorialTrigger names, see Tutorial
        Assert.NotEmpty(Enum.GetValues<TutorialTrigger>());
    }
}
