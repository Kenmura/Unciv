using Unciv.Assets;
using Unciv.Commands.Empire;
using Unciv.Harness;
using Unciv.Queries.Empire;

namespace Unciv.Core.Tests.Assets;

public class TechScreenModelTests
{
    private static TechTreeEntry Tech(string name, int? column, int row, string state = "Locked", string[]? needs = null, int cost = 20, int progress = 0, int? turns = 5,
        string era = "Ancient era", string description = "Unlocks A\nUnlocks B") =>
        new(name, era, column, row, cost, progress, needs ?? [], state, null, turns, description);

    private static TechTreeResult Tree(IReadOnlyList<TechTreeEntry> techs, string[]? queue = null, string? current = null, int free = 0) =>
        new(current ?? queue?.FirstOrDefault(), queue ?? [], free, false, techs);

    // Agriculture -> Pottery -> Sailing ; Agriculture -> Mining -> Bronze ; Ghost (hidden) -> Hidden-child ; Orphan has no column
    private static TechTreeResult Sample(string[]? queue = null, string[]? researched = null, int free = 0)
    {
        var done = new HashSet<string>(researched ?? []);
        var order = queue ?? [];
        string State(string name, string[] needs)
        {
            if (done.Contains(name)) return "Researched";
            if (order.Contains(name)) return "Queued";
            return needs.All(done.Contains) ? "Researchable" : "Locked";
        }
        TechTreeEntry Make(string name, int? column, int row, params string[] needs) =>
            Tech(name, column, row, State(name, needs), needs) with { QueuePosition = order.ToList().IndexOf(name) is var i and >= 0 ? i : null };
        return Tree(
        [
            Make("Agriculture", 2, 4),
            Make("Pottery", 3, 3, "Agriculture"),
            Make("Sailing", 4, 3, "Pottery"),
            Make("Mining", 3, 6, "Agriculture"),
            Make("Bronze", 4, 6, "Mining"),
            Tech("Hidden", 3, 9, "Unavailable"),
            Tech("HiddenChild", 4, 9, "Locked", ["Hidden"]),
            Tech("Orphan", null, 1),
        ], queue, free: free);
    }

    [Fact]
    public void Layout_PlacesTechsByColumnAndRowFromTheSmallestUsed()
    {
        var layout = TechScreenModel.Layout(Sample());
        Assert.Equal(["Agriculture", "Pottery", "Mining", "Sailing", "Bronze", "HiddenChild"], layout.Nodes.Select(it => it.Name));
        var agriculture = layout.Nodes.Single(it => it.Name == "Agriculture");
        Assert.Equal((0, 1), (agriculture.GridColumn, agriculture.GridRow));
        var bronze = layout.Nodes.Single(it => it.Name == "Bronze");
        Assert.Equal((2, 3), (bronze.GridColumn, bronze.GridRow));
        Assert.Equal(3, layout.Columns);
        Assert.Equal(7, layout.Rows);
        Assert.Equal(["Ancient era", "Ancient era", "Ancient era"], layout.ColumnEras);
    }

    [Fact]
    public void Layout_LeavesOutHiddenAndColumnlessTechsAndLinksOnlyToPlacedPrerequisites()
    {
        var layout = TechScreenModel.Layout(Sample());
        Assert.DoesNotContain(layout.Nodes, it => it.Name is "Hidden" or "Orphan");
        Assert.Equal([("Agriculture", "Pottery"), ("Agriculture", "Mining"), ("Pottery", "Sailing"), ("Mining", "Bronze")], layout.Edges.Select(it => (it.From, it.To)));
        Assert.DoesNotContain(layout.Edges, it => it.From == "Hidden");
    }

    [Fact]
    public void Layout_EmptyAndSingleTree()
    {
        var empty = TechScreenModel.Layout(Tree([Tech("Orphan", null, 1)]));
        Assert.Empty(empty.Nodes);
        Assert.Equal((0, 0), (empty.Columns, empty.Rows));
        var single = TechScreenModel.Layout(Tree([Tech("Only", 7, 5)]));
        Assert.Equal((0, 0, 1, 1), (single.Nodes[0].GridColumn, single.Nodes[0].GridRow, single.Columns, single.Rows));
    }

    [Fact]
    public void Layout_OrdersTiesByRowThenOrdinalName()
    {
        var layout = TechScreenModel.Layout(Tree([Tech("b", 1, 2), Tech("a", 1, 2), Tech("Z", 1, 2), Tech("low", 1, 1), Tech("far", 0, 9)]));
        Assert.Equal(["far", "low", "Z", "a", "b"], layout.Nodes.Select(it => it.Name));
    }

    [Fact]
    public void Layout_ColoursByStateAndEraHeadingComesFromTheFirstNodeOfTheColumn()
    {
        var layout = TechScreenModel.Layout(Sample(queue: ["Agriculture"], researched: []));
        Assert.Equal(PlaceholderPalette.ForTechState("Queued"), layout.Nodes.Single(it => it.Name == "Agriculture").Fill);
        Assert.Equal(PlaceholderPalette.ForTechState("Locked"), layout.Nodes.Single(it => it.Name == "Bronze").Fill);
        var states = new[] { "Researched", "Queued", "Researchable", "Locked", "Unavailable" };
        Assert.Equal(states.Length, states.Select(PlaceholderPalette.ForTechState).Distinct().Count());
        Assert.Equal(PlaceholderPalette.ForTechState("Locked"), PlaceholderPalette.ForTechState("nonsense"));
        var eras = TechScreenModel.Layout(Tree([Tech("a", 0, 1, era: "Ancient era"), Tech("b", 2, 1, era: "Classical era")]));
        Assert.Equal(["Ancient era", "", "Classical era"], eras.ColumnEras);
    }

    [Fact]
    public void Detail_WordsEveryStateAndTurnCount()
    {
        Assert.Equal("researched", TechScreenModel.Detail(Tech("a", 1, 1, "Researched")));
        Assert.Equal("not available", TechScreenModel.Detail(Tech("a", 1, 1, "Unavailable")));
        Assert.Equal("20 science, 5 turns", TechScreenModel.Detail(Tech("a", 1, 1, "Researchable")));
        Assert.Equal("20 science, 1 turn", TechScreenModel.Detail(Tech("a", 1, 1, "Researchable", turns: 1)));
        Assert.Equal("20 science, 0 turns", TechScreenModel.Detail(Tech("a", 1, 1, "Researchable", turns: 0)));
        Assert.Equal("20 science", TechScreenModel.Detail(Tech("a", 1, 1, "Locked", turns: null)));
        Assert.Equal("1/20, 2 turns", TechScreenModel.Detail(Tech("a", 1, 1, "Queued", progress: 1, turns: 2)));
        Assert.Equal("7/20, 2 turns", TechScreenModel.Detail(Tech("a", 1, 1, "Queued", progress: 7, turns: 2)));
        Assert.Equal("Pottery\n20 science, 5 turns", TechScreenModel.Label(Tech("Pottery", 1, 1)));
    }

    [Fact]
    public void Header_ShowsCurrentResearchFreeTechsOrNothing()
    {
        Assert.Equal("Nothing is being researched", TechScreenModel.Header(Sample()));
        Assert.Equal("Researching Agriculture, 5 turns", TechScreenModel.Header(Sample(queue: ["Agriculture"])));
        Assert.Equal("Nothing is being researched", TechScreenModel.Header(Tree([Tech("a", 1, 1)], current: "Missing")));
        Assert.Equal("Choose 1 free technology", TechScreenModel.Header(Sample(free: 1)));
        Assert.Equal("Choose 2 free technologies", TechScreenModel.Header(Sample(queue: ["Agriculture"], free: 2)));
    }

    [Fact]
    public void QueueLines_AreNumberedWithTurns()
    {
        Assert.Empty(TechScreenModel.QueueLines(Sample()));
        Assert.Equal(["1. Agriculture, 5 turns", "2. Pottery, 5 turns"], TechScreenModel.QueueLines(Sample(queue: ["Agriculture", "Pottery"])));
        Assert.Equal(["1. Unknown"], TechScreenModel.QueueLines(Tree([], ["Unknown"])));
    }

    [Fact]
    public void UnlockLines_SplitTheEnginesDescriptionAndDropBlankLines()
    {
        var node = TechScreenModel.Layout(Tree([Tech("a", 1, 1, description: " Unlocks A \n\nUnlocks B\n")])).Nodes[0];
        Assert.Equal(["Unlocks A", "Unlocks B"], TechScreenModel.UnlockLines(node));
        Assert.Empty(TechScreenModel.UnlockLines(TechScreenModel.Layout(Tree([Tech("a", 1, 1, description: "")])).Nodes[0]));
    }

    [Fact]
    public void PathTo_ListsUnresearchedPrerequisitesFirstAndSkipsResearchedAndGivenOnes()
    {
        var tree = Sample();
        Assert.Equal(["Agriculture", "Mining", "Bronze"], TechScreenModel.PathTo(tree, "Bronze", new HashSet<string>()));
        Assert.Equal(["Agriculture"], TechScreenModel.PathTo(tree, "Agriculture", new HashSet<string>()));
        Assert.Equal(["Bronze"], TechScreenModel.PathTo(tree, "Bronze", new HashSet<string> { "Agriculture", "Mining" }));
        Assert.Equal([], TechScreenModel.PathTo(tree, "Bronze", new HashSet<string> { "Bronze" }));
        var partly = Sample(researched: ["Agriculture"]);
        Assert.Equal(["Mining", "Bronze"], TechScreenModel.PathTo(partly, "Bronze", new HashSet<string>()));
        Assert.Equal([], TechScreenModel.PathTo(partly, "Agriculture", new HashSet<string>()));
        Assert.Null(TechScreenModel.PathTo(tree, "HiddenChild", new HashSet<string>()));
        Assert.Null(TechScreenModel.PathTo(tree, "Nope", new HashSet<string>()));
    }

    [Fact]
    public void PathTo_DiamondListsASharedPrerequisiteOnceAndACycleTerminates()
    {
        var diamond = Tree([Tech("Root", 0, 1), Tech("Left", 1, 1, "Locked", ["Root"]), Tech("Right", 1, 2, "Locked", ["Root"]), Tech("Top", 2, 1, "Locked", ["Left", "Right"])]);
        Assert.Equal(["Root", "Left", "Right", "Top"], TechScreenModel.PathTo(diamond, "Top", new HashSet<string>()));
        var cycle = Tree([Tech("A", 0, 1, "Locked", ["B"]), Tech("B", 1, 1, "Locked", ["A"])]);
        Assert.Equal(["B", "A"], TechScreenModel.PathTo(cycle, "A", new HashSet<string>()));
    }

    [Fact]
    public void DependsOn_FollowsPrerequisitesTransitively()
    {
        var tree = Sample();
        Assert.True(TechScreenModel.DependsOn(tree, "Bronze", "Agriculture"));
        Assert.True(TechScreenModel.DependsOn(tree, "Bronze", "Mining"));
        Assert.False(TechScreenModel.DependsOn(tree, "Agriculture", "Agriculture"));
        Assert.False(TechScreenModel.DependsOn(tree, "Sailing", "Mining"));
        Assert.False(TechScreenModel.DependsOn(tree, "Mining", "Bronze"));
        Assert.False(TechScreenModel.DependsOn(tree, "Nope", "Mining"));
    }

    private static TechAction Action(TechTreeResult tree, string tech, string id) => TechScreenModel.Actions(tree, tech).Single(it => it.Id == id);

    private static List<string> QueueOf(TechAction action) => ((SetResearchQueue)action.Command!).Techs;

    [Fact]
    public void Actions_ResearchReplacesTheQueueWithThePath()
    {
        var research = Action(Sample(), "Bronze", "Research");
        Assert.True(research.Enabled);
        Assert.Equal(["Agriculture", "Mining", "Bronze"], QueueOf(research));
        var other = Action(Sample(queue: ["Agriculture", "Pottery"]), "Bronze", "Research");
        Assert.Equal(["Agriculture", "Mining", "Bronze"], QueueOf(other));
    }

    [Fact]
    public void Actions_ResearchIsDisabledWhenThePlanIsAlreadyThatQueue()
    {
        var tree = Sample(queue: ["Agriculture", "Mining", "Bronze"]);
        var research = Action(tree, "Bronze", "Research");
        Assert.False(research.Enabled);
        Assert.Null(research.Command);
        Assert.Equal("Already the research plan.", research.Why);
        Assert.True(Action(Sample(queue: ["Agriculture", "Mining", "Bronze"]), "Mining", "Research").Enabled);
    }

    [Fact]
    public void Actions_ResearchStaysEnabledForADifferentQueueOfTheSameLength()
    {
        var tree = Sample(queue: ["Pottery", "Sailing"], researched: ["Agriculture"]);
        var research = Action(tree, "Bronze", "Research");
        Assert.True(research.Enabled);
        Assert.Equal(["Mining", "Bronze"], QueueOf(research));
    }

    [Fact]
    public void Actions_EnqueueAppendsOnlyWhatIsNotQueued()
    {
        var tree = Sample(queue: ["Agriculture"]);
        Assert.Equal(["Agriculture", "Mining", "Bronze"], QueueOf(Action(tree, "Bronze", "Enqueue")));
        Assert.Equal(["Agriculture", "Pottery"], QueueOf(Action(tree, "Pottery", "Enqueue")));
        Assert.Equal(["Agriculture", "Pottery", "Sailing"], QueueOf(Action(Sample(queue: ["Agriculture", "Pottery"]), "Sailing", "Enqueue")));
        var queued = Action(tree, "Agriculture", "Enqueue");
        Assert.False(queued.Enabled);
        Assert.Equal("Already queued.", queued.Why);
    }

    [Fact]
    public void Actions_EnqueueOnAnEmptyQueueIsThePath()
    {
        Assert.Equal(["Agriculture", "Pottery"], QueueOf(Action(Sample(), "Pottery", "Enqueue")));
    }

    [Fact]
    public void Actions_DequeueDropsTheTechAndTheQueuedTechsThatNeedIt()
    {
        var tree = Sample(queue: ["Agriculture", "Mining", "Bronze", "Pottery"]);
        Assert.Equal(["Agriculture", "Pottery"], QueueOf(Action(tree, "Mining", "Dequeue")));
        Assert.Equal(["Agriculture", "Mining", "Bronze"], QueueOf(Action(tree, "Pottery", "Dequeue")));
        Assert.Equal(["Agriculture", "Mining", "Pottery"], QueueOf(Action(tree, "Bronze", "Dequeue")));
        Assert.Equal([], tree.Queue.Except(["Agriculture", "Mining", "Bronze", "Pottery"]));
    }

    [Fact]
    public void Actions_DequeueRefusesAnEmptyResultAndAnUnqueuedTech()
    {
        var tree = Sample(queue: ["Agriculture", "Mining"]);
        var dequeue = Action(tree, "Agriculture", "Dequeue");
        Assert.False(dequeue.Enabled);
        Assert.Contains("empty", dequeue.Why, StringComparison.Ordinal);
        var unqueued = Action(tree, "Pottery", "Dequeue");
        Assert.False(unqueued.Enabled);
        Assert.Equal("Not in the queue.", unqueued.Why);
        Assert.True(Action(tree, "Mining", "Dequeue").Enabled);
    }

    [Fact]
    public void Actions_ResearchedHiddenAndBlockedTechsOfferNothing()
    {
        var tree = Sample(researched: ["Agriculture"]);
        foreach (string id in new[] { "Research", "Enqueue", "Dequeue" })
        {
            var done = Action(tree, "Agriculture", id);
            Assert.False(done.Enabled);
            Assert.Equal("Already researched.", done.Why);
            Assert.Null(done.Command);
        }
        Assert.All(TechScreenModel.Actions(Sample(), "HiddenChild"), it => Assert.False(it.Enabled));
        Assert.Equal("A prerequisite cannot be researched.", Action(Sample(), "HiddenChild", "Research").Why);
        Assert.All(TechScreenModel.Actions(Tree([Tech("Hidden", 1, 1, "Unavailable")]), "Hidden"), it => Assert.Equal("This technology is not available.", it.Why));
        Assert.Empty(TechScreenModel.Actions(Sample(), "Nope"));
    }

    [Fact]
    public void Actions_FreeTechModeOffersOnlyTakingAResearchableTech()
    {
        var tree = Sample(free: 1);
        var take = Assert.Single(TechScreenModel.Actions(tree, "Agriculture"));
        Assert.True(take.Enabled);
        Assert.Equal("Agriculture", ((PickFreeTechnology)take.Command!).Tech);
        var locked = Assert.Single(TechScreenModel.Actions(tree, "Bronze"));
        Assert.False(locked.Enabled);
        Assert.Null(locked.Command);
        Assert.Equal("It cannot be researched yet.", locked.Why);
        Assert.True(Assert.Single(TechScreenModel.Actions(Sample(queue: ["Agriculture"], free: 1), "Agriculture")).Enabled);
    }

    private static TechTreeResult Ask(Unciv.Commands.GameSession session) =>
        new TechTreeQuery().Run(new Unciv.View.GameView(session.Game, session.Game.GetCivilization(ScenarioGame.HumanA)));

    [Fact]
    public void TheEnginesRealTreeLaysOutAndItsCommandsAreAcceptedByTheEngine()
    {
        var session = ScenarioGame.Create(solo: true);
        var tree = Ask(session);
        var layout = TechScreenModel.Layout(tree);
        Assert.True(layout.Nodes.Count > 30);
        Assert.All(layout.Edges, edge => Assert.Contains(layout.Nodes, it => it.Name == edge.To));
        Assert.Equal("Researched", tree.Techs.Single(it => it.Name == "Agriculture").State); // the scenario game starts with it
        var research = Action(tree, "Bronze Working", "Research");
        Assert.Equal(["Mining", "Bronze Working"], QueueOf(research));
        Assert.True(session.Submit(ScenarioGame.HumanA, research.Command!).Accepted);
        var after = Ask(session);
        Assert.Equal(["Mining", "Bronze Working"], after.Queue);
        var pottery = Action(after, "Pottery", "Enqueue");
        Assert.True(session.Submit(ScenarioGame.HumanA, pottery.Command!).Accepted);
        var third = Ask(session);
        var trimmed = Action(third, "Mining", "Dequeue");
        Assert.True(session.Submit(ScenarioGame.HumanA, trimmed.Command!).Accepted);
        Assert.Equal(["Pottery"], Ask(session).Queue);
    }

    [Fact]
    public void EraSpans_AreRunsOfEqualEraNamesAndEachEraIsOneSpan()
    {
        var spans = TechScreenModel.EraSpans(["Ancient era", "Ancient era", "Classical era", "", "Classical era", "Medieval era"]);
        Assert.Equal([new EraSpan("Ancient era", 0, 2), new EraSpan("Classical era", 2, 1), new EraSpan("Classical era", 4, 1), new EraSpan("Medieval era", 5, 1)], spans);
        var tree = Sample();
        Assert.Equal([new EraSpan("Ancient era", 0, 3)], TechScreenModel.Layout(tree).EraSpans);
        Assert.Empty(TechScreenModel.Layout(Tree([])).EraSpans);
    }

    [Fact]
    public void Nodes_CarryCostProgressTurnsQueuePositionAndIcon()
    {
        var tree = Tree([Tech("Pottery", 1, 1, "Queued", cost: 40, progress: 10, turns: 3) with { QueuePosition = 0 }, Tech("Done", 2, 1, "Researched")], ["Pottery"]);
        var layout = TechScreenModel.Layout(tree);
        var pottery = layout.Nodes.Single(it => it.Name == "Pottery");
        Assert.Equal((40, 10, 3, 0), (pottery.Cost, pottery.Progress, pottery.Turns, pottery.QueuePosition));
        Assert.Equal(0.25, pottery.Fraction);
        Assert.True(pottery.IsCurrent);
        Assert.Equal(AssetKey.Of(RulesetAssetKeys.TechIcon, "Pottery"), pottery.IconKey);
        var done = layout.Nodes.Single(it => it.Name == "Done");
        Assert.Equal(1.0, done.Fraction);
        Assert.False(done.IsCurrent);
        Assert.Empty(done.Unlocks);
    }

    [Fact]
    public void Unlocks_ListUnitsBuildingsWondersAndImprovementsOfTheTechByKindThenName()
    {
        var ruleset = Unciv.Models.Ruleset.RulesetCache.GetVanillaRuleset();
        var unlocks = TechScreenModel.Unlocks(ruleset, "Bronze Working");
        Assert.Contains(unlocks, it => it.Kind == "Unit" && it.Name == "Spearman");
        Assert.All(unlocks, it => Assert.Contains(it.Kind, new[] { "Unit", "Building", "Wonder", "Improvement" }));
        string[] kinds = ["Unit", "Building", "Wonder", "Improvement"];
        Assert.Equal(unlocks.OrderBy(it => Array.IndexOf(kinds, it.Kind)).ThenBy(it => it.Name, StringComparer.Ordinal).Select(it => it.Name), unlocks.Select(it => it.Name));
        var pottery = TechScreenModel.Unlocks(ruleset, "Pottery");
        Assert.Contains(pottery, it => it.Kind == "Building" && it.Name == "Granary");
        Assert.Empty(TechScreenModel.Unlocks(ruleset, "No such tech"));
        var layout = TechScreenModel.Layout(Ask(ScenarioGame.Create(solo: true)), ruleset);
        Assert.Contains(layout.Nodes.Single(it => it.Name == "Bronze Working").Unlocks, it => it.Name == "Spearman");
        Assert.Equal(["Bronze Working"], layout.Nodes.Where(it => TechScreenModel.Matches(it, "spearman")).Select(it => it.Name));
    }

    [Fact]
    public void Matches_FindsATechByNameUnlockOrStatAndEmptyQueryMatchesAll()
    {
        var node = TechScreenModel.Layout(Tree([Tech("Pottery", 1, 1, description: "Provides +1 Science\nUnlocks Granary")])).Nodes[0] with
        {
            Unlocks = [new TechUnlock("Building", "Granary", AssetKey.Of(RulesetAssetKeys.BuildingIcon, "Granary"))],
        };
        Assert.True(TechScreenModel.Matches(node, ""));
        Assert.True(TechScreenModel.Matches(node, "   "));
        Assert.True(TechScreenModel.Matches(node, "pott"));
        Assert.True(TechScreenModel.Matches(node, "GRANARY"));
        Assert.True(TechScreenModel.Matches(node, "science"));
        Assert.False(TechScreenModel.Matches(node, "archery"));
        var bullets = TechScreenModel.Layout(Tree([Tech("Farming", 1, 1, description: "Units enabled: \n • Warrior (upgrades to Spearman)\n+1 Food")])).Nodes[0];
        Assert.False(TechScreenModel.Matches(bullets, "spearman"));
        Assert.False(TechScreenModel.Matches(bullets, "units enabled"));
        Assert.True(TechScreenModel.Matches(bullets, "food"));
        Assert.True(TechScreenModel.Matches(node, "Topf", text => text == "Pottery" ? "Topferei" : text));
        Assert.False(TechScreenModel.Matches(node, "Topf"));
    }

    [Fact]
    public void ResearchLabel_NamesThePathWhenPrerequisitesAreNeeded()
    {
        Assert.Equal("Research now", TechScreenModel.Actions(Sample(researched: ["Agriculture"]), "Mining").Single(it => it.Id == "Research").Label);
        Assert.Equal("Research path (2 techs, 10 turns)", TechScreenModel.Actions(Sample(researched: ["Agriculture"]), "Bronze").Single(it => it.Id == "Research").Label);
        var noScience = Tree([Tech("a", 1, 1, "Researchable", turns: null), Tech("b", 2, 1, "Locked", ["a"], turns: null)]);
        Assert.Equal("Research path (2 techs)", TechScreenModel.Actions(noScience, "b").Single(it => it.Id == "Research").Label);
    }
}
