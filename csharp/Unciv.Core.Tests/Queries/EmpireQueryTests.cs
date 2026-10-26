using System.Text.Json;
using Unciv.Core.Tests.Support;
using Unciv.Json;
using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Logic.Files;
using Unciv.Logic.Map;
using Unciv.Logic.Trade;
using Unciv.Models.Ruleset;
using Unciv.Queries;
using Unciv.Queries.Empire;
using Unciv.Utils;

namespace Unciv.Core.Tests.Queries.Empire;

// T197: the empire panel queries. Every query is run through the registry and json like a client would, and none may change the saved state.
[Collection("RulesetCache")]
public class EmpireQueryTests
{
    private readonly TestGame testGame = new();
    private readonly Civilization player;
    private readonly Civilization enemy;
    private readonly Civilization stranger;
    private readonly QueryRegistry registry = new(UncivJson.DefaultOptions);

    public EmpireQueryTests()
    {
        testGame.MakeHexagonalMap(6);
        player = testGame.AddCiv(testGame.Ruleset.Nations["Germany"], isPlayer: true);
        enemy = testGame.AddCiv(testGame.Ruleset.Nations["Egypt"]);
        stranger = testGame.AddCiv(testGame.Ruleset.Nations["Rome"]);
        registry.RegisterFromAssembly(typeof(QueryRegistry).Assembly);
        testGame.AddCity(player, testGame.GetTile(0, 0));
        testGame.AddCity(enemy, testGame.GetTile(3, 3));
        player.DiplomacyFunctions.MakeCivilizationsMeet(enemy);
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

    private string Fail(IQuery query)
    {
        var response = Execute(player, query);
        Assert.False(response.Ok);
        return response.Code!;
    }

    private static ulong Hash(GameInfo game) => StateHasher.ComputeHash(h => h.Add(UncivFiles.GameInfoToString(game, forceZip: false)));

    // ---- tech

    [Fact]
    public void TechTreeStatesFollowTheTechManager()
    {
        var tech = player.Tech;
        tech.TechsResearched.Add("Agriculture");
        tech.TechsToResearch.Add("Pottery");
        tech.TechsToResearch.Add("Writing");
        var result = Ask(new TechTreeQuery());
        TechTreeEntry Entry(string name) => result.Techs.Single(it => it.Name == name);

        Assert.Equal("Researched", Entry("Agriculture").State);
        Assert.Null(Entry("Agriculture").Turns);
        Assert.Equal("Queued", Entry("Pottery").State);
        Assert.Equal(0, Entry("Pottery").QueuePosition);
        Assert.Equal(1, Entry("Writing").QueuePosition);
        Assert.Equal("Researchable", Entry("Mining").State);
        Assert.Equal("Locked", Entry("Currency").State); // needs Bronze Working's prerequisite chain
        Assert.Equal("Pottery", result.CurrentTech);
        Assert.Equal(["Pottery", "Writing"], result.Queue);
        Assert.Equal(tech.CostOfTech("Pottery"), Entry("Pottery").Cost);
        Assert.Equal(["Agriculture"], Entry("Pottery").Prerequisites);
        Assert.False(string.IsNullOrEmpty(Entry("Pottery").Description));
        Assert.Equal(result.Techs.Select(it => it.Name).Order(StringComparer.Ordinal), result.Techs.Select(it => it.Name));
    }

    [Fact]
    public void TechTurnsAreNullWithoutScienceAndCeilingOtherwise()
    {
        var before = Ask(new TechTreeQuery()).Techs.Single(it => it.Name == "Mining");
        float science = player.Stats.StatsForNextTurn.Science;
        if (science <= 0f) Assert.Null(before.Turns);
        player.Stats.StatsForNextTurn.Science = 10f;
        var cost = player.Tech.RemainingScienceToTech("Mining");
        var after = Ask(new TechTreeQuery()).Techs.Single(it => it.Name == "Mining");
        Assert.Equal(System.Math.Max(1, (int)System.Math.Ceiling(cost / 10.0)), after.Turns);
    }

    // ---- policies

    [Fact]
    public void PolicyTreeNamesTheFirstReasonAPolicyIsLocked()
    {
        var result = Ask(new PolicyTreeQuery());
        var tradition = result.Branches.Single(it => it.Name == "Tradition");
        Assert.Equal("Adoptable", tradition.Start.State);
        var second = tradition.Policies.First(it => it.Requires.Count > 0);
        Assert.Equal("Locked", second.State);
        Assert.Equal("requires:" + second.Requires[0], second.LockedReason);
        var complete = tradition.Policies.Single(it => it.Kind == "BranchComplete");
        Assert.Equal("complete-automatically", complete.LockedReason);
        Assert.Equal(player.Policies.GetCultureNeededForNextPolicy(), result.CultureNeededForNextPolicy);

        player.Policies.StoredCulture = 100000;
        player.Policies.Adopt(player.Policies.GetPolicyByName("Tradition"));
        result = Ask(new PolicyTreeQuery());
        tradition = result.Branches.Single(it => it.Name == "Tradition");
        Assert.Equal("Adopted", tradition.Start.State);
        Assert.Equal(1, tradition.Adopted);
        Assert.Equal(tradition.Policies.Count(it => it.Kind != "BranchComplete") + 1, tradition.Total);
        Assert.True(result.CanAdopt);
        Assert.Contains(tradition.Policies, it => it.State == "Adoptable" && it.Requires.Contains("Tradition"));
    }

    // ---- religion and great people

    [Fact]
    public void ReligionListsOnlyBeliefsNobodyHolds()
    {
        var pantheon = testGame.Ruleset.Beliefs.Values.First(it => it.Type == BeliefType.Pantheon);
        var religion = testGame.AddReligion(enemy);
        religion.AddBelief(pantheon);
        var result = Ask(new ReligionQuery());
        Assert.True(result.Enabled);
        Assert.DoesNotContain(pantheon.Name, result.Available["Pantheon"]);
        Assert.Equal(testGame.Ruleset.Beliefs.Values.Count(it => it.Type == BeliefType.Follower), result.Available["Follower"].Count);
        Assert.Equal("None", result.State);
        Assert.Null(result.ReligionName);
        Assert.Empty(result.Slots);
        Assert.DoesNotContain(religion.Name, result.AvailableIcons);
        Assert.Equal(testGame.Ruleset.Religions.Count, result.AvailableIcons.Count); // the test religion has no ruleset icon
    }

    [Fact]
    public void GreatPeopleDoNotInitialiseTheSavedPointCounter()
    {
        player.GreatPeople.GreatPersonPointsCounter["Great Scientist"] = 30;
        player.GreatPeople.FreeGreatPeople = 1;
        var before = Hash(testGame.GameInfo);
        var result = Ask(new GreatPeopleQuery());
        Assert.Equal(before, Hash(testGame.GameInfo));
        var scientist = result.GreatPeople.Single(it => it.Name == "Great Scientist");
        Assert.Equal(30, scientist.Points);
        Assert.Equal(player.GreatPeople.GetPointsRequiredForGreatPerson("Great Scientist"), scientist.Required);
        Assert.Contains("Great Scientist", result.FreeOptions);
        Assert.Equal(1, result.FreeGreatPeople);
    }

    // ---- diplomacy and trade

    [Fact]
    public void DiplomacyPanelOnlyAnswersForMetCivs()
    {
        Assert.Equal(EmpireQueryCodes.UnknownCiv, Fail(new DiplomacyPanelQuery(stranger.CivName)));
        Assert.Equal(EmpireQueryCodes.UnknownCiv, Fail(new DiplomacyPanelQuery(player.CivName)));
        Assert.Equal(EmpireQueryCodes.UnknownCiv, Fail(new DiplomacyPanelQuery("Nobody")));

        player.GetDiplomacyManager(enemy)!.DeclareWar();
        enemy.GetDiplomacyManager(player)!.AddModifier(Unciv.Logic.Civilization.Diplomacy.DiplomaticModifiers.DeclaredWarOnUs, -10f);
        player.GetDiplomacyManager(enemy)!.SetFlag(Unciv.Logic.Civilization.Diplomacy.DiplomacyFlags.DeclinedPeace, 5);
        var panel = Ask(new DiplomacyPanelQuery(enemy.CivName));
        Assert.Equal("War", panel.Status);
        Assert.Contains(panel.Modifiers, it => it.Name == "DeclaredWarOnUs" && it.Value < 0f);
        Assert.Contains(panel.Flags, it => it.Name == "DeclinedPeace" && it.Value == 5f);
        Assert.False(panel.CanDeclareWar);
        Assert.Equal(enemy.GetDiplomacyManager(player)!.RelationshipLevel().ToString(), panel.Relationship);
        Assert.Null(panel.Influence);
    }

    [Fact]
    public void TradeOptionsAreTheEnginesAvailableOffers()
    {
        player.AddGold(40);
        player.GetDiplomacyManager(enemy)!.DeclareWar();
        var logic = new TradeLogic(player, enemy);
        var result = Ask(new TradeOptionsQuery(enemy.CivName));
        Assert.Equal(logic.OurAvailableOffers.Select(it => (it.Name, it.Type.ToString(), it.Amount)),
            result.Ours.Select(it => (it.Name, it.Type, it.Amount)));
        Assert.Equal(logic.TheirAvailableOffers.Count, result.Theirs.Count);
        Assert.Contains(result.Ours, it => it.Type == "Treaty" && it.Name == Constants.PeaceTreaty); // at war after meeting
        Assert.Equal(EmpireQueryCodes.UnknownCiv, Fail(new TradeOptionsQuery(stranger.CivName)));
    }

    [Fact]
    public void TradeOptionsNameCitiesAndCivsForOffersWhoseNameIsAnId()
    {
        var ours = testGame.AddCity(player, testGame.GetTile(0, 4));
        var theirs = testGame.AddCity(enemy, testGame.GetTile(6, 6));
        player.DiplomacyFunctions.MakeCivilizationsMeet(stranger);
        enemy.DiplomacyFunctions.MakeCivilizationsMeet(stranger);
        enemy.GetDiplomacyManager(stranger)!.DeclareWar();
        var result = Ask(new TradeOptionsQuery(enemy.CivName));
        Assert.Equal(ours.Name, result.Ours.Single(it => it.Type == "City").DisplayName);
        Assert.Equal(theirs.Name, result.Theirs.Where(it => it.Type == "City").Select(it => it.DisplayName).Single());
        Assert.Equal(ours.Id, result.Ours.Single(it => it.Type == "City").Name);
        foreach (var type in new[] { TradeOfferType.Introduction, TradeOfferType.WarDeclaration, TradeOfferType.PeaceProposal })
        {
            var offer = new TradeOffer(stranger.CivId, type, 1, testGame.GameInfo.Speed);
            Assert.Equal(stranger.CivName, TradeOptionsQuery.DisplayNameOf(testGame.GameInfo, player, enemy, offer));
        }
        Assert.Null(TradeOptionsQuery.DisplayNameOf(testGame.GameInfo, player, enemy, new TradeOffer("nobody", TradeOfferType.Introduction, 1, testGame.GameInfo.Speed)));
        Assert.Null(TradeOptionsQuery.DisplayNameOf(testGame.GameInfo, player, enemy, new TradeOffer("nobody", TradeOfferType.City, 1, testGame.GameInfo.Speed)));
        Assert.Null(TradeOptionsQuery.DisplayNameOf(testGame.GameInfo, player, enemy, new TradeOffer(stranger.CivId, TradeOfferType.Treaty, 1, testGame.GameInfo.Speed)));
        Assert.All(result.Ours.Concat(result.Theirs).Where(it => it.Type != "City"), it => Assert.Null(it.DisplayName));
    }

    [Fact]
    public void TradeHintIsOffByDefault()
    {
        Assert.Equal(EmpireQueryCodes.HintsDisabled, Fail(new TradeHintQuery(enemy.CivName, [], [])));
    }

    [Fact]
    public void TradeHintBucketsMatchTheEvaluationTheAiUses()
    {
        testGame.GameInfo.GameParameters.TradeHintsEnabled = true;
        player.AddGold(300);
        enemy.AddGold(500);
        player.GetDiplomacyManager(enemy)!.MakePeace();
        var before = Hash(testGame.GameInfo);

        TradeHintResult Hint(TradeOfferRequest[] ours, TradeOfferRequest[] theirs) => Ask(new TradeHintQuery(enemy.CivName, ours, theirs));
        var gift = Hint([new(Constants.FlatGold, "Gold", 200)], []);
        Assert.Equal("accept", gift.Hint);
        var greedy = Hint([], [new(Constants.FlatGold, "Gold", 500)]);
        Assert.Equal("refuse", greedy.Hint);
        var halfway = Hint([new(Constants.FlatGold, "Gold", 100)], [new(Constants.FlatGold, "Gold", 200)]);
        Assert.Equal("counter-needed", halfway.Hint);

        // The bucket agrees with the AI's own test on the same trade.
        var trade = new Trade();
        trade.OurOffers.Add(new TradeOffer(Constants.FlatGold, TradeOfferType.Gold, 200, -1));
        Assert.True(TradeEvaluation.Default.IsTradeAcceptable(trade.Reverse(), enemy, player));
        Assert.Equal(before, Hash(testGame.GameInfo));
    }

    [Fact]
    public void TradeHintRejectsOffersThatAreNotAvailable()
    {
        testGame.GameInfo.GameParameters.TradeHintsEnabled = true;
        Assert.Equal(EmpireQueryCodes.BadRequest, Fail(new TradeHintQuery(enemy.CivName, [new(Constants.FlatGold, "Gold", 100000)], [])));
        Assert.Equal(EmpireQueryCodes.BadRequest, Fail(new TradeHintQuery(enemy.CivName, [new("Unobtainium", "Luxury_Resource", 1)], [])));
        Assert.Equal(EmpireQueryCodes.BadRequest, Fail(new TradeHintQuery(enemy.CivName, [new(Constants.FlatGold, "Nonsense", 1)], [])));
        Assert.Equal(EmpireQueryCodes.BadRequest, Fail(new TradeHintQuery(enemy.CivName, [], [])));
        Assert.Equal(EmpireQueryCodes.UnknownCiv, Fail(new TradeHintQuery(stranger.CivName, [], [])));
    }

    // ---- notifications and overview

    [Fact]
    public void NotificationActionsOnUnexploredTilesAreStripped()
    {
        testGame.GetTile(0, 0).SetExplored(player, true);
        var unexplored = testGame.TileMap.TileList.First(it => !it.IsExplored(player));
        player.Notifications.Clear();
        player.AddNotification("Look here", [new LocationAction(new HexCoord(0, 0)), new LocationAction(unexplored.Position),
            new TechAction("Pottery"), new DiplomacyAction("Egypt", true)], Notification.NotificationCategory.Diplomacy, "TechIcons/Pottery");
        var result = Ask(new NotificationsQuery());
        var entry = Assert.Single(result.Notifications);
        Assert.Equal("Diplomacy", entry.Category);
        Assert.Equal("Look here", entry.Text);
        Assert.Equal(["TechIcons/Pottery"], entry.Icons);
        Assert.Equal(["Location", "Tech", "Diplomacy"], entry.Actions.Select(it => it.Kind));
        Assert.Equal(new Unciv.Queries.Units.TilePos(0, 0), entry.Actions[0].Tile);
        Assert.Equal("Pottery", entry.Actions[1].Name);
        Assert.True(entry.Actions[2].ShowTrade);
    }

    [Fact]
    public void EveryNotificationActionKindIsConverted()
    {
        testGame.GetTile(0, 0).SetExplored(player, true);
        var origin = new HexCoord(0, 0);
        player.Notifications.Clear();
        player.AddNotification("All", [new CityAction(origin), new MapUnitAction(origin, 7), new PromoteUnitAction("Warrior", origin, 8),
            new MayaLongCountAction(), new CivilopediaAction("Wonder/Pyramids"), new OverviewAction(Unciv.UI.Screens.OverviewScreen.EmpireOverviewCategories.Stats, "x"),
            new PolicyAction("Tradition"), new EspionageAction(), new LinkAction("https://example.org"), new ReligionAction("Faith"),
            new CityAction(new HexCoord(5, 5))], Notification.NotificationCategory.General);
        var actions = Ask(new NotificationsQuery()).Notifications.Single().Actions;
        Assert.Equal(["City", "MapUnit", "PromoteUnit", "MayaLongCount", "Civilopedia", "Overview", "Policy", "Espionage", "Link", "Religion"],
            actions.Select(it => it.Kind).Take(10));
        Assert.Equal(7, actions[1].UnitId);
        Assert.Equal("Warrior", actions[2].Name);
        Assert.Equal(("Stats", "x"), (actions[5].Name, actions[5].Detail));
        Assert.Equal("Faith", actions[9].Name);
        Assert.Equal(testGame.TileMap.TileList.First(it => it.Position == new HexCoord(5, 5)).IsExplored(player) ? 11 : 10, actions.Count);
    }

    [Fact]
    public void ReligionShowsOwnBeliefsAndFreeBeliefsToChoose()
    {
        var belief = testGame.Ruleset.Beliefs.Values.First(it => it.Type == BeliefType.Pantheon);
        testGame.AddReligion(player).AddBelief(belief);
        player.ReligionManager.FreeBeliefs["Follower"] = 2;
        var result = Ask(new ReligionQuery());
        Assert.Equal([new BeliefEntry(belief.Name, "Pantheon")], result.OwnBeliefs);
        Assert.Equal(2, result.ToChoose["Follower"]);
        Assert.Equal(["Follower", "Follower"], result.Slots);
        Assert.NotNull(result.ReligionName);
        Assert.DoesNotContain(belief.Name, result.Available["Pantheon"]);
    }

    [Fact]
    public void OverviewReportsOwnStatsAndHidesUnmetCivs()
    {
        player.GetDiplomacyManager(enemy)!.DeclareWar();
        var overview = Ask(new EmpireOverviewQuery());
        Assert.Equal(player.CivName, overview.CivName);
        Assert.Equal(1, overview.Cities);
        Assert.Equal(player.Gold, overview.Gold);
        Assert.Equal(enemy.CivName, Assert.Single(overview.KnownCivs).CivName);
        Assert.Equal(Enum.GetValues<Unciv.UI.Screens.VictoryScreen.RankingType>().Length, overview.Rankings.Count);
        Assert.True(overview.KnownCivs[0].AtWar);
        Assert.NotEmpty(overview.KnownCivs[0].Rankings);

        testGame.GameInfo.GameParameters.ShowRankings = false;
        Assert.Empty(Ask(new EmpireOverviewQuery()).KnownCivs[0].Rankings);
    }

    [Fact]
    public void TopBarReportsStatsGoldenAgeAndResources()
    {
        player.AddGold(40);
        var bar = Ask(new EmpireTopBarQuery());
        var stats = player.Stats.StatsForNextTurn;
        Assert.Equal(player.Gold, bar.Gold);
        Assert.Equal(stats.Gold, bar.GoldPerTurn);
        Assert.Equal(stats.Science, bar.SciencePerTurn);
        Assert.Equal(stats.Culture, bar.CulturePerTurn);
        Assert.Equal(stats.Faith, bar.FaithPerTurn);
        Assert.Equal(player.GetHappiness(), bar.Happiness);
        Assert.Equal(testGame.GameInfo.GetYear(0), bar.Year);
        Assert.Equal(0, bar.GoldenAgeTurnsLeft);
        Assert.Equal(player.GoldenAges.HappinessRequiredForNextGoldenAge(), bar.GoldenAgeNeeded);
        player.GoldenAges.EnterGoldenAge();
        Assert.Equal(player.GoldenAges.TurnsLeftForCurrentGoldenAge, Ask(new EmpireTopBarQuery()).GoldenAgeTurnsLeft);
        Assert.All(bar.Resources, it => Assert.NotEqual(0, it.Amount));
    }

    [Fact]
    public void TopBarListsLuxuryAndStrategicResourcesSummedOverOriginsButNotBonus()
    {
        var rules = testGame.Ruleset.TileResources;
        var supply = new Unciv.Models.Ruleset.Tile.ResourceSupplyList();
        supply.Add(new Unciv.Models.Ruleset.Tile.ResourceSupplyList.ResourceSupply(rules["Iron"], "Tiles", 2));
        supply.Add(new Unciv.Models.Ruleset.Tile.ResourceSupplyList.ResourceSupply(rules["Iron"], "Trade", 1));
        supply.Add(new Unciv.Models.Ruleset.Tile.ResourceSupplyList.ResourceSupply(rules["Wheat"], "Tiles", 4));
        supply.Add(new Unciv.Models.Ruleset.Tile.ResourceSupplyList.ResourceSupply(rules["Silk"], "Tiles", 1));
        player.SummarizedCivResourceSupply = supply;
        var resources = Ask(new EmpireTopBarQuery()).Resources;
        Assert.Equal([("Iron", "Strategic", 3), ("Silk", "Luxury", 1)], resources.Select(it => (it.Name, it.Type, it.Amount)).OrderBy(it => it.Type, StringComparer.Ordinal).Reverse().ToList());
    }

    [Fact]
    public void NoEmpireQueryChangesTheState()
    {
        testGame.GameInfo.GameParameters.TradeHintsEnabled = true;
        player.AddGold(50);
        player.AddNotification("Hello", [new LocationAction(new HexCoord(0, 0))], Notification.NotificationCategory.General);
        var before = Hash(testGame.GameInfo);
        IQuery[] queries = [new TechTreeQuery(), new PolicyTreeQuery(), new ReligionQuery(), new GreatPeopleQuery(), new NotificationsQuery(),
            new EmpireOverviewQuery(), new EmpireTopBarQuery(), new DiplomacyPanelQuery(enemy.CivName), new TradeOptionsQuery(enemy.CivName),
            new TradeHintQuery(enemy.CivName, [new(Constants.FlatGold, "Gold", 1)], [])];
        foreach (var query in queries)
        {
            var response = Execute(player, query);
            Assert.True(response.Ok, $"{query.Type}: {response.Code} {response.Message}");
            Assert.Equal(before, Hash(testGame.GameInfo));
        }
        Assert.All(queries, it => Assert.Contains(it.Type, registry.RegisteredTypes));
    }
}
