using Unciv.Assets;
using Unciv.Logic;
using Unciv.Net.Visibility;
using Unciv.Queries.Empire;

namespace Unciv.Core.Tests.Assets;

public class HudModelTests
{
    private static EmpireTopBarResult Bar(int gold = 120, float gpt = 4.5f, int golden = 0, params TopBarResource[] resources) =>
        new(Turn: 12, Year: -3200, Gold: gold, GoldPerTurn: gpt, SciencePerTurn: 7.4f, CulturePerTurn: 2f, FaithPerTurn: -0.5f, Happiness: 9,
            GoldenAgeTurnsLeft: golden, GoldenAgeStored: 30, GoldenAgeNeeded: 500, Resources: resources);

    private static PendingDecisionsResult Pending(params (DecisionKind Kind, bool Blocking)[] decisions) =>
        new(decisions.Select((it, i) => new PendingDecisionEntry(it.Kind.ToString(), "", "cmd", it.Blocking, -1, null)).ToList());

    [Fact]
    public void TopBarListsStatsGoldenAgeResourcesAndTurn()
    {
        var stats = HudModel.TopBar(Bar(resources: [new TopBarResource("Iron", "Strategic", 3)]));
        Assert.Equal(["Gold", "Science", "Culture", "Happiness", "Faith", "GoldenAge", "Resource:Iron", "Turn"], stats.Select(it => it.Id));
        Assert.Equal("120 (+5)", stats[0].Text);
        Assert.Equal("+7", stats[1].Text);
        Assert.Equal("+2", stats[2].Text);
        Assert.Equal("9", stats[3].Text);
        Assert.Equal("-1", stats[4].Text);
        Assert.Equal("30/500", stats[5].Text);
        Assert.Equal("3", stats[6].Text);
        Assert.Equal("icon/resource/Iron", stats[6].Icon.ToString());
        Assert.Equal("Turn 12 - 3200 BC", stats[7].Text);
        Assert.Null(stats[7].Icon);
        Assert.Equal("icon/stat/Gold", stats[0].Icon.ToString());
    }

    [Fact]
    public void ActiveGoldenAgeShowsTurnsLeft() => Assert.Equal("Golden Age 8", HudModel.TopBar(Bar(golden: 8))[5].Text);

    [Fact]
    public void NegativeGoldChangeAndZeroAreSigned()
    {
        Assert.Equal("5 (-3)", HudModel.TopBar(Bar(5, -2.5f))[0].Text);
        Assert.Equal("5 (+0)", HudModel.TopBar(Bar(5, 0f))[0].Text);
    }

    [Fact]
    public void YearTextUsesBcAndAd()
    {
        Assert.Equal("1 BC", HudModel.YearText(-1));
        Assert.Equal("0 AD", HudModel.YearText(0));
        Assert.Equal("1500 AD", HudModel.YearText(1500));
        Assert.Equal("Your turn, Babylon\nTurn 2 - 4000 BC", HudModel.Banner(2, -4000, "Babylon"));
    }

    [Fact]
    public void NoDecisionMeansEndTurn()
    {
        Assert.Equal(new HudAction(HudActionKind.EndTurn, "Next turn", null, true), HudModel.NextAction(Pending()));
        Assert.Equal(HudActionKind.EndTurn, HudModel.NextAction(null).Kind);
        Assert.True(HudModel.CanEndTurn(null));
    }

    [Fact]
    public void FirstDecisionNamesTheButton()
    {
        var due = HudModel.NextAction(Pending((DecisionKind.DueUnits, false), (DecisionKind.Technology, true)));
        Assert.Equal((HudActionKind.NextUnit, "Next unit"), (due.Kind, due.Label));
        var tech = HudModel.NextAction(Pending((DecisionKind.Technology, true)));
        Assert.Equal((HudActionKind.Decision, "Pick a technology", DecisionKind.Technology), (tech.Kind, tech.Label, tech.Decision));
        Assert.All(Enum.GetValues<DecisionKind>(), kind => Assert.NotEqual(kind.ToString(), HudModel.NextAction(Pending((kind, true))).Label));
    }

    [Fact]
    public void OnlyBlockingDecisionsRefuseEndTurn()
    {
        Assert.True(HudModel.CanEndTurn(Pending((DecisionKind.DueUnits, false), (DecisionKind.TradeRequest, false))));
        Assert.False(HudModel.CanEndTurn(Pending((DecisionKind.DueUnits, false), (DecisionKind.Policy, true))));
    }

    [Fact]
    public void UnknownDecisionKindFallsBackToEndTurn()
    {
        var odd = new PendingDecisionsResult([new PendingDecisionEntry("FromTheFuture", "", "cmd", false, -1, null)]);
        Assert.Equal(HudActionKind.EndTurn, HudModel.NextAction(odd).Kind);
    }

    [Fact]
    public void SimultaneousStatusOverridesTheButton()
    {
        var ended = new TurnStatusDto { Ended = ["Babylon"], Phase = "Playing" };
        var undo = HudModel.NextAction(Pending(), ended, "Babylon");
        Assert.Equal((HudActionKind.UndoEndTurn, "Undo end turn", true), (undo.Kind, undo.Label, undo.Enabled));
        Assert.Equal(HudActionKind.EndTurn, HudModel.NextAction(Pending(), ended, "Greece").Kind);
        var resolving = HudModel.NextAction(Pending(), new TurnStatusDto { Ended = [], Phase = "Resolving" }, "Babylon");
        Assert.Equal((HudActionKind.Resolving, false), (resolving.Kind, resolving.Enabled));
    }

    [Fact]
    public void NextDueUnitCyclesInIdOrder()
    {
        Assert.Null(HudModel.NextDueUnit([], null));
        Assert.Equal(2, HudModel.NextDueUnit([9, 2, 5], null));
        Assert.Equal(5, HudModel.NextDueUnit([9, 2, 5], 2));
        Assert.Equal(2, HudModel.NextDueUnit([9, 2, 5], 9));
        Assert.Equal(5, HudModel.NextDueUnit([9, 2, 5], 3));
        Assert.Equal(2, HudModel.NextDueUnit([9, 2, 5], 100));
    }
}
