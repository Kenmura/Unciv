using Unciv.Assets;
using Unciv.Commands.City;
using Unciv.Harness;
using Unciv.Models.StatTypes;
using Unciv.Queries.Cities;
using Unciv.Queries.Units;

namespace Unciv.Core.Tests.Assets;

public class CityProductionModelTests
{
    private static CityPanelResult Panel(bool puppet = false, bool razed = false, bool resistance = false, IReadOnlyList<QueueEntry>? queue = null,
        IReadOnlyList<CityBuilding>? buildings = null) => new(
        "id1", "Ur", new TilePos(2, 3), false, puppet, razed, resistance,
        new CityGrowth(4, 1, 5, 14, 3, 4, null), new CityBorders(3, 20, true), [], [], [],
        "", queue ?? [], [], [], [], true, "NoFocus", false, buildings ?? [], null, null, [], [], []);

    private static ConstructionBuyCost Gold(int cost, bool allowed = true) => new("Gold", cost, allowed);

    private static ConstructionEntry Entry(string name, string category = "Unit", string state = "buildable", bool displayed = true, int? turns = 5, int? cost = 30,
        int workDone = 0, IReadOnlyList<ConstructionRejection>? rejections = null, IReadOnlyList<ConstructionBuyCost>? buy = null) =>
        new(name, category, state, displayed, turns, cost, workDone, rejections ?? [], buy ?? []);

    private static ConstructionListResult List(params ConstructionEntry[] entries) => new("id1", entries);

    private static readonly QueueEntry[] ThreeQueue = [new("Warrior", 4, 30, 6), new("Monument", 1, 40, 0), new("Gold", null, null, 0)];

    [Fact]
    public void Queue_NumbersEntriesAndShowsTurnsAndProgress()
    {
        var rows = CityProductionModel.Queue(Panel(queue: ThreeQueue), List(), 0);
        Assert.Equal(3, rows.Count);
        Assert.Equal([0, 1, 2], rows.Select(it => it.Index));
        Assert.Equal(["Warrior", "Monument", "Gold"], rows.Select(it => it.Name));
        Assert.Equal("1. Warrior (4 turns, 6/30)", rows[0].Label);
        Assert.Equal("2. Monument (1 turn, 0/40)", rows[1].Label);
        Assert.Equal("3. Gold (perpetual)", rows[2].Label);
        Assert.Equal([false, false, true], rows.Select(it => it.IsPerpetual));
        var stalled = CityProductionModel.Queue(Panel(queue: [new QueueEntry("Wonder", null, 100, 0)]), List(), 0);
        Assert.Equal("1. Wonder (never, 0/100)", stalled[0].Label);
        Assert.False(stalled[0].IsPerpetual);
        Assert.Equal("1. Done (0 turns, 5/5)", CityProductionModel.Queue(Panel(queue: [new QueueEntry("Done", 0, 5, 5)]), List(), 0)[0].Label);
        Assert.Equal("1. Slow (2 turns, 0/9)", CityProductionModel.Queue(Panel(queue: [new QueueEntry("Slow", 2, 9, 0)]), List(), 0)[0].Label);
    }

    [Fact]
    public void Queue_ButtonsFollowTheEnginesReorderRules()
    {
        var rows = CityProductionModel.Queue(Panel(queue: ThreeQueue), List(), 0);
        Assert.Equal([false, true, false], rows.Select(it => it.CanTop));
        Assert.Equal([false, true, true], rows.Select(it => it.CanRaise));
        Assert.Equal([true, true, false], rows.Select(it => it.CanLower));
        Assert.All(rows, it => Assert.True(it.CanRemove));
        var single = Assert.Single(CityProductionModel.Queue(Panel(queue: [new QueueEntry("Warrior", 4, 30, 0)]), List(), 0));
        Assert.False(single.CanTop || single.CanRaise || single.CanLower);
        Assert.True(single.CanRemove);
        Assert.Empty(CityProductionModel.Queue(Panel(), List(), 0));
    }

    [Fact]
    public void Queue_OfAPuppetCannotBeChanged()
    {
        var rows = CityProductionModel.Queue(Panel(puppet: true, queue: ThreeQueue), List(), 0);
        Assert.All(rows, it => Assert.False(it.CanTop || it.CanRaise || it.CanLower || it.CanRemove));
    }

    [Fact]
    public void Queue_RowsCarryThePurchaseOptionsOfTheirEntry()
    {
        var list = List(Entry("Warrior", buy: [Gold(140)]), Entry("Monument", "Building", buy: [Gold(200), new ConstructionBuyCost("Faith", 90, true)]));
        var rows = CityProductionModel.Queue(Panel(queue: ThreeQueue), list, 500);
        Assert.Equal(["Buy 140 Gold"], rows[0].Buy.Select(it => it.Label));
        Assert.Equal(["Buy 200 Gold", "Buy 90 Faith"], rows[1].Buy.Select(it => it.Label));
        Assert.Empty(rows[2].Buy);
    }

    [Fact]
    public void QueueCommands_NameTheCityEntryAndMove()
    {
        var row = CityProductionModel.Queue(Panel(queue: ThreeQueue), List(), 0)[1];
        foreach (var move in Enum.GetValues<QueueMove>())
        {
            var command = Assert.IsType<ReorderQueue>(CityProductionModel.Reorder("id1", row, move));
            Assert.Equal(("id1", 1, move), (command.CityId, command.Index, command.Move));
        }
        var remove = Assert.IsType<RemoveFromQueue>(CityProductionModel.Remove("id9", row));
        Assert.Equal(("id9", 1), (remove.CityId, remove.Index));
    }

    [Theory]
    [InlineData("Unit", "Units")]
    [InlineData("Building", "Buildings")]
    [InlineData("Wonder", "Wonders")]
    [InlineData("NationalWonder", "National wonders")]
    [InlineData("Perpetual", "Other")]
    [InlineData("Whatever", "Other")]
    public void CategoryHeading_NamesEachCategory(string category, string heading)
    {
        Assert.Equal(heading, CityProductionModel.CategoryHeading(category));
        Assert.Equal(["Unit", "Building", "Wonder", "NationalWonder", "Perpetual"], CityProductionModel.Categories);
    }

    [Fact]
    public void Buildables_ListsOnlyDisplayedEntriesInTheEnginesOrder()
    {
        var list = List(Entry("Worker"), Entry("Hidden", displayed: false), Entry("Monument", "Building", turns: 1, cost: 40), Entry("Gold", "Perpetual", turns: null, cost: null));
        var rows = CityProductionModel.Buildables(Panel(), list, 0);
        Assert.Equal(["Worker", "Monument", "Gold"], rows.Select(it => it.Name));
        Assert.Equal(["Unit", "Building", "Perpetual"], rows.Select(it => it.Category));
        Assert.Equal(["buildable", "buildable", "buildable"], rows.Select(it => it.State));
        Assert.Equal("Worker - 5 turns, 30", rows[0].Label);
        Assert.Equal("5 turns, 30", rows[0].Detail);
        Assert.Equal("Monument - 1 turn, 40", rows[1].Label);
        Assert.Equal("Gold - perpetual", rows[2].Label);
        Assert.Equal("perpetual", rows[2].Detail);
        Assert.Empty(CityProductionModel.Buildables(Panel(), List(), 0));
    }

    [Fact]
    public void Buildables_CanQueueOnlyWhatTheEngineWouldAccept()
    {
        var rejection = new ConstructionRejection("Pop", "Requires at least [2] population", true);
        var hiddenRejection = new ConstructionRejection("Other", "internal", false);
        var list = List(
            Entry("Warrior"),
            Entry("QueuedUnit", state: "enqueued"),
            Entry("QueuedBuilding", "Building", state: "enqueued"),
            Entry("Done", "Building", state: "built"),
            Entry("Settler", state: "rejected", turns: null, rejections: [hiddenRejection, rejection]),
            Entry("Odd", state: "rejected", turns: null, rejections: [hiddenRejection]),
            Entry("Stuck", state: "rejected", turns: null));
        var rows = CityProductionModel.Buildables(Panel(), list, 0);
        Assert.Equal([true, true, false, false, false, false, false], rows.Select(it => it.CanQueue));
        Assert.Equal(["", "", "Already in the queue.", "Already built.", "Requires at least [2] population", "It cannot be built now.", "It cannot be built now."], rows.Select(it => it.QueueWhy));
        Assert.Equal("Settler - Requires at least [2] population", rows[4].Label);
        Assert.Equal("Requires at least [2] population", rows[4].Detail);
        Assert.Equal("Odd - cannot be built", rows[5].Label);
        Assert.Equal("Stuck - cannot be built", rows[6].Label);
        var puppet = CityProductionModel.Buildables(Panel(puppet: true), list, 0);
        Assert.All(puppet, it => Assert.False(it.CanQueue));
        Assert.All(puppet, it => Assert.Equal(CityProductionModel.PuppetWhy, it.QueueWhy));
    }

    [Fact]
    public void Buildables_PurchaseOptionsExplainWhyTheyAreDisabled()
    {
        var list = List(
            Entry("Cheap", buy: [Gold(100)]),
            Entry("Dear", buy: [Gold(300, allowed: false)]),
            Entry("Exact", buy: [Gold(250, allowed: false)]),
            Entry("Blocked", buy: [Gold(100, allowed: false)]),
            Entry("Holy", buy: [new ConstructionBuyCost("Faith", 500, false)]));
        var rows = CityProductionModel.Buildables(Panel(), list, 250);
        var cheap = Assert.Single(rows[0].Buy);
        Assert.Equal(("Gold", 100, true, "Buy 100 Gold", ""), (cheap.Stat, cheap.Cost, cheap.Allowed, cheap.Label, cheap.Why));
        Assert.False(Assert.Single(rows[1].Buy).Allowed);
        Assert.Equal("Need 50 more gold.", rows[1].Buy[0].Why);
        Assert.Equal("Exact cannot be bought with Gold in Ur now (a unit may stand in the way, or the city is in resistance).", rows[2].Buy[0].Why);
        Assert.Equal("Blocked cannot be bought with Gold in Ur now (a unit may stand in the way, or the city is in resistance).", rows[3].Buy[0].Why);
        Assert.Equal("Holy cannot be bought with Faith in Ur now (a unit may stand in the way, or the city is in resistance).", rows[4].Buy[0].Why);
        Assert.Equal("Buy 500 Faith", rows[4].Buy[0].Label);
    }

    [Fact]
    public void Buildables_TooltipListsCostWorkRejectionsAndPrices()
    {
        var entry = Entry("Library", "Building", turns: 7, cost: 90, workDone: 12,
            rejections: [new ConstructionRejection("A", "Needs a Granary", true), new ConstructionRejection("B", "hidden", false)], buy: [Gold(200)]);
        var tooltip = CityProductionModel.Buildables(Panel(), List(entry), 0)[0].Tooltip;
        Assert.Equal("Library (Building)\nCost 90 production, 7 turns\nAlready 12 production invested\nNeeds a Granary\nBuy: 200 Gold", tooltip);
        var plain = CityProductionModel.Buildables(Panel(), List(Entry("Gold", "Perpetual", turns: null, cost: null)), 0)[0].Tooltip;
        Assert.Equal("Gold (Other)", plain);
        Assert.Contains("Already 1 production invested", CityProductionModel.Buildables(Panel(), List(Entry("Warrior", workDone: 1)), 0)[0].Tooltip, StringComparison.Ordinal);
        Assert.Equal("Pyramids (Wonder)\nCost 5 production, 5 turns", CityProductionModel.Buildables(Panel(), List(Entry("Pyramids", "Wonder", cost: 5)), 0)[0].Tooltip);
        Assert.StartsWith("Heroic Epic (National wonder)", CityProductionModel.Buildables(Panel(), List(Entry("Heroic Epic", "NationalWonder")), 0)[0].Tooltip, StringComparison.Ordinal);
        Assert.Equal("Warrior (Unit)\nCost 30 production, never", CityProductionModel.Buildables(Panel(), List(Entry("Warrior", turns: null)), 0)[0].Tooltip);
    }

    [Fact]
    public void Commands_QueueAndPurchaseNameTheConstructionAndTheQueueEntry()
    {
        var panel = Panel(queue: [new QueueEntry("Warrior", 4, 30, 0), new QueueEntry("Monument", 4, 30, 0), new QueueEntry("Monument", 4, 30, 0)]);
        var row = CityProductionModel.Buildables(Panel(), List(Entry("Monument", "Building")), 0)[0];
        var add = Assert.IsType<QueueConstruction>(CityProductionModel.Enqueue("id1", row));
        Assert.Equal(("id1", "Monument", false), (add.CityId, add.Name, add.AddToTop));
        Assert.True(Assert.IsType<QueueConstruction>(CityProductionModel.Enqueue("id1", row, toTop: true)).AddToTop);
        var buy = Assert.IsType<PurchaseConstruction>(CityProductionModel.Purchase(panel, "Monument", new BuyOption("Faith", 5, true, "", "")));
        Assert.Equal(("id1", "Monument", Stat.Faith, 1), (buy.CityId, buy.Name, buy.Stat, buy.QueueIndex));
        var notQueued = Assert.IsType<PurchaseConstruction>(CityProductionModel.Purchase(panel, "Worker", new BuyOption("Gold", 5, true, "", "")));
        Assert.Equal((Stat.Gold, -1), (notQueued.Stat, notQueued.QueueIndex));
        Assert.Equal(0, ((PurchaseConstruction)CityProductionModel.Purchase(panel, "Warrior", new BuyOption("Gold", 5, true, "", ""))).QueueIndex);
    }

    [Fact]
    public void Buildings_SeparatePlainBuildingsFromWondersAndSortThem()
    {
        var ruleset = ScenarioGame.LoadVanilla();
        var panel = Panel(buildings: [new("Stonehenge", 0), new("Monument", 4), new("Heroic Epic", 0), new("Barracks", 9), new("Unknown thing", 3)]);
        var rows = CityProductionModel.Buildings(panel, ruleset);
        Assert.Equal(["Barracks", "Monument", "Unknown thing", "Heroic Epic", "Stonehenge"], rows.Select(it => it.Name));
        Assert.Equal(["Building", "Building", "Building", "NationalWonder", "Wonder"], rows.Select(it => it.Kind));
        Assert.Equal([true, true, true, false, false], rows.Select(it => it.CanSell));
        Assert.Equal([9, 4, 3, 0, 0], rows.Select(it => it.SellPrice));
        Assert.Equal("Barracks (sells for 9 gold)", rows[0].Label);
        Assert.Equal("Heroic Epic (national wonder)", rows[3].Label);
        Assert.Equal("Stonehenge (wonder)", rows[4].Label);
        Assert.All(CityProductionModel.Buildings(Panel(puppet: true, buildings: [new("Monument", 4)]), ruleset), it => Assert.False(it.CanSell));
        Assert.Empty(CityProductionModel.Buildings(Panel(), ruleset));
        var sell = Assert.IsType<SellBuilding>(CityProductionModel.Sell("id1", rows[1]));
        Assert.Equal(("id1", "Monument"), (sell.CityId, sell.Building));
    }

    [Fact]
    public void StateLine_DescribesPuppetRazingAndResistance()
    {
        Assert.Equal("", CityProductionModel.StateLine(Panel()));
        Assert.Equal("Puppet city: it chooses its own production and citizens. Annex it to take control.", CityProductionModel.StateLine(Panel(puppet: true)));
        Assert.Equal("This city is being razed: it loses a citizen every turn.", CityProductionModel.StateLine(Panel(razed: true)));
        Assert.Equal("In resistance: it produces nothing until the unrest ends.", CityProductionModel.StateLine(Panel(resistance: true)));
        Assert.Equal("Puppet city: it chooses its own production and citizens. Annex it to take control. This city is being razed: it loses a citizen every turn. In resistance: it produces nothing until the unrest ends.",
            CityProductionModel.StateLine(Panel(puppet: true, razed: true, resistance: true)));
        Assert.Contains("Annex it to take control. This city is being razed", CityProductionModel.StateLine(Panel(puppet: true, razed: true)), StringComparison.Ordinal);
        Assert.Contains("every turn. In resistance", CityProductionModel.StateLine(Panel(razed: true, resistance: true)), StringComparison.Ordinal);
    }

    [Fact]
    public void StateActions_OfferAnnexRazeOrStopRazing()
    {
        var plain = CityProductionModel.StateActions(Panel());
        var raze = Assert.IsType<SetRazing>(Assert.Single(plain).Command);
        Assert.Equal(("Raze city", "id1", true), (plain[0].Label, raze.CityId, raze.Raze));
        var puppet = CityProductionModel.StateActions(Panel(puppet: true));
        var annex = Assert.IsType<AnnexCity>(Assert.Single(puppet).Command);
        Assert.Equal(("Annex city", "id1"), (puppet[0].Label, annex.CityId));
        var razing = CityProductionModel.StateActions(Panel(razed: true));
        var stop = Assert.IsType<SetRazing>(Assert.Single(razing).Command);
        Assert.Equal(("Stop razing", false), (razing[0].Label, stop.Raze));
        var both = CityProductionModel.StateActions(Panel(puppet: true, razed: true));
        Assert.Equal(["Annex city", "Stop razing"], both.Select(it => it.Label));
        Assert.All(plain.Concat(puppet).Concat(razing), it => Assert.NotEmpty(it.Tooltip));
    }
}
