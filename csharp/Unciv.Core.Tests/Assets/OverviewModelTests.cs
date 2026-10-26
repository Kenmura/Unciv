using Unciv.Assets;
using Unciv.Net.Visibility;
using Unciv.Queries.Empire;
using Unciv.Queries.Victory;

namespace Unciv.Core.Tests.Assets;

public class OverviewModelTests
{
    private static CityDto City(string name, string owner = "Me", bool own = true, bool capital = false, bool puppet = false, bool razing = false, int pop = 3, params string[] queue) => new()
    {
        Name = name,
        Owner = owner,
        X = 2,
        Y = 3,
        Population = pop,
        IsCapital = capital,
        IsPuppet = puppet,
        IsBeingRazed = razing,
        Health = 20,
        MaxHealth = 30,
        VisibleBuildings = [],
        Own = own ? new OwnCityDto { Id = name, FoodStored = 0, CultureStored = 0, Buildings = ["Monument"], ConstructionQueue = queue.ToList() } : null,
    };

    private static UnitDto Unit(int id, string name, string owner = "Me", bool own = true, string? action = null, bool automated = false, bool fortified = false, bool embarked = false) => new()
    {
        Id = id,
        Name = name,
        Owner = owner,
        X = 4,
        Y = 5,
        HealthBucket = 4,
        IsEmbarked = embarked,
        IsFortified = fortified,
        Own = own ? new OwnUnitDto { Health = 90, CurrentMovement = 1.5f, Xp = 0, Action = action, Promotions = [], Automated = automated } : null,
    };

    private static ClientSnapshot Snapshot(IEnumerable<CityDto> cities, IEnumerable<UnitDto> units) => new()
    {
        Seq = 1,
        Turn = 1,
        Phase = "Playing",
        TurnStatus = new TurnStatusDto { Ended = [], Phase = "Playing" },
        You = "Me",
        Civs = [],
        Tiles = [],
        Cities = cities.ToList(),
        Units = units.ToList(),
        Diplomacy = [],
        PendingTrades = [],
        Notifications = [],
    };

    [Fact]
    public void CityTableListsOnlyTheViewersCitiesCapitalFirstThenByName()
    {
        var table = OverviewModel.CityTable(Snapshot([City("Zed"), City("Ur", "Rome", own: false), City("Abu", puppet: true, queue: ["Granary", "Library"]), City("Kish", capital: true, razing: true)], []));
        Assert.Equal(["Kish", "Abu", "Zed"], table.Rows.Select(it => it.Key));
        Assert.Equal(["Kish", "3", "20/30", "-", "1", "Capital, Razing"], table.Rows[0].Cells);
        Assert.Equal(["Abu", "3", "20/30", "Granary", "1", "Puppet"], table.Rows[1].Cells);
        Assert.Equal("-", table.Rows[2].Cells[5]);
        Assert.Equal((2, 3), (table.Rows[0].X, table.Rows[0].Y));
    }

    [Fact]
    public void ForeignCitiesWithoutPrivateDetailNeverAppearEvenIfTheOwnerNameMatches()
    {
        var table = OverviewModel.CityTable(Snapshot([City("Spy", "Me", own: false)], []));
        Assert.Equal(0, table.Count);
        Assert.Equal("You have no cities.", table.Empty);
    }

    [Fact]
    public void AnEntryOfAnotherCivIsNeverListedEvenWithPrivateDetail()
    {
        var snapshot = Snapshot([City("Theirs", "Rome")], [Unit(2, "Spearman", "Rome")]);
        Assert.Equal(0, OverviewModel.CityTable(snapshot).Count);
        Assert.Equal(0, OverviewModel.UnitTable(snapshot).Count);
    }

    [Fact]
    public void UnitTableListsOnlyOwnUnitsByNameThenIdAndWordsTheirOrder()
    {
        var table = OverviewModel.UnitTable(Snapshot([], [Unit(9, "Warrior"), Unit(3, "Warrior", action: "Sleep"), Unit(1, "Worker", automated: true), Unit(5, "Archer", fortified: true),
            Unit(6, "Boat", embarked: true), Unit(7, "Spearman", "Rome", own: false), Unit(8, "Scout", "Me", own: false)]));
        Assert.Equal(["5", "6", "3", "9", "1"], table.Rows.Select(it => it.Key));
        Assert.Equal(["Archer", "4,5", "90", "1.5", "Fortified"], table.Rows[0].Cells);
        Assert.Equal("Embarked", table.Rows[1].Cells[4]);
        Assert.Equal("Sleep", table.Rows[2].Cells[4]);
        Assert.Equal("-", table.Rows[3].Cells[4]);
        Assert.Equal("Automated", table.Rows[4].Cells[4]);
        Assert.Equal("You have no units.", OverviewModel.UnitTable(Snapshot([], [])).Empty);
    }

    [Fact]
    public void StatsLinesWordTheEnginesNumbersAndBreakdown()
    {
        var overview = new EmpireOverviewResult(12, "Babylon", 120, 3.5f, 4f, -1f, 0f, 8, 2, 5, 7, 1, 90,
            new Dictionary<string, double> { ["Cities"] = 20, ["Population"] = 12.5 }, new Dictionary<string, int>(), []);
        var lines = OverviewModel.StatsLines(overview);
        Assert.Equal("Babylon - turn 12", lines[0]);
        Assert.Contains("Gold: 120 (+3.5 per turn)", lines);
        Assert.Contains("Culture: -1 per turn", lines);
        Assert.Contains("Score: 90", lines);
        Assert.Equal(["  Cities: 20", "  Population: 12.5"], lines.Skip(lines.Count - 2));
    }

    [Fact]
    public void KnownCivsShowStatusKindAndScoreOnlyWhenTheEngineGaveIt()
    {
        var overview = new EmpireOverviewResult(1, "Me", 0, 0f, 0f, 0f, 0f, 0, 0, 0, 0, 0, 0, new Dictionary<string, double>(), new Dictionary<string, int>(),
        [
            new OverviewCiv("Rome", false, false, true, new Dictionary<string, int> { ["Score"] = 44 }),
            new OverviewCiv("Sparta", true, false, false, new Dictionary<string, int>()),
            new OverviewCiv("Troy", false, true, false, new Dictionary<string, int>()),
            new OverviewCiv("Ur", false, false, false, new Dictionary<string, int>()),
        ]);
        var table = OverviewModel.KnownCivs(overview);
        Assert.Equal(["Rome", "Major", "At war", "44"], table.Rows[0].Cells);
        Assert.Equal(["Sparta", "City-state", "Peace", "-"], table.Rows[1].Cells);
        Assert.Equal("Defeated", table.Rows[2].Cells[2]);
        Assert.Equal("Peace", table.Rows[3].Cells[2]);
        Assert.Equal(0, OverviewModel.KnownCivs(overview with { KnownCivs = [] }).Count);
    }

    [Fact]
    public void ResourceTableIsTheEnginesListInItsOrder()
    {
        var table = OverviewModel.ResourceTable(new EmpireTopBarResult(1, 0, 0, 0f, 0f, 0f, 0f, 0, 0, 0, 0, [new TopBarResource("Iron", "Strategic", 4), new TopBarResource("Silk", "Luxury", 1)]));
        Assert.Equal([["Iron", "Strategic", "4"], ["Silk", "Luxury", "1"]], table.Rows.Select(it => it.Cells.ToArray()));
        Assert.Equal(0, OverviewModel.ResourceTable(new EmpireTopBarResult(1, 0, 0, 0f, 0f, 0f, 0f, 0, 0, 0, 0, [])).Count);
    }

    [Fact]
    public void RankingTablesKeepTheEnginesOrderHideUnmetNamesMarkTheViewerAndDefeated()
    {
        var column = new RankingColumn("Score", "Score",
        [
            new RankingEntry(new VictoryCivRef("Me", false, true), 50),
            new RankingEntry(new VictoryCivRef(null, false, false), 40),
            new RankingEntry(new VictoryCivRef("Troy", true, false), 99),
        ]);
        var tables = OverviewModel.RankingTables([column, column with { Label = "Force" }]);
        Assert.Equal(["Score", "Force"], tables.Select(it => it.Title));
        var rows = tables[0].Table.Rows;
        Assert.Equal(["1", "Me (you)", "50"], rows[0].Cells);
        Assert.Equal(["2", "Unknown civilization", "40"], rows[1].Cells);
        Assert.Equal(["3", "Troy", "Defeated"], rows[2].Cells);
        Assert.Empty(OverviewModel.RankingTables([]));
    }

    [Fact]
    public void ReligionLinesCoverDisabledPantheonOwnReligionAndChoices()
    {
        Assert.Equal(["Religion is not enabled in this game."], OverviewModel.ReligionLines(new ReligionResult(false, "None", 0, null, null, [], false, false, 0, new Dictionary<string, int>(), new Dictionary<string, IReadOnlyList<string>>(), [], [])));
        var pantheon = OverviewModel.ReligionLines(new ReligionResult(true, "None", 12, null, null, [], true, false, 25, new Dictionary<string, int>(), new Dictionary<string, IReadOnlyList<string>> { ["Pantheon"] = ["A", "B"] }, [], []));
        Assert.Contains("You hold no beliefs yet.", pantheon);
        Assert.Contains("You can found a pantheon (25 faith).", pantheon);
        Assert.Contains("Free Pantheon beliefs: 2", pantheon);
        var expand = OverviewModel.ReligionLines(new ReligionResult(true, "Pantheon", 12, null, null, [], false, true, 40, new Dictionary<string, int>(), new Dictionary<string, IReadOnlyList<string>>(), [], []));
        Assert.Contains("You can add to your pantheon (40 faith).", expand);
        var own = OverviewModel.ReligionLines(new ReligionResult(true, "FoundingReligion", 5, "Zoro", "Zoroastrianism", [new BeliefEntry("Fire", "Pantheon")], false, false, 0,
            new Dictionary<string, int> { ["Founder"] = 1 }, new Dictionary<string, IReadOnlyList<string>>(), [], []));
        Assert.Contains("Religion: Zoroastrianism", own);
        Assert.Contains("Beliefs: Fire (Pantheon)", own);
        Assert.Contains("To choose: 1 Founder belief(s)", own);
    }

    [Fact]
    public void SpyRowsOfferHideoutThenEngineDestinationsAndTheCoupOnlyWhenAllowed()
    {
        var result = new SpyDestinationsResult(
        [
            new SpyEntry("Ann", 2, "Coup", "Sparta", true, true, [new SpyDestination("c1", "Ur", "Babylon", false), new SpyDestination("c2", "Kish", "", false)], true, 0.455f),
            new SpyEntry("Bob", 1, "Moving", "Hideout", true, false, [], false, null),
            new SpyEntry("Cy", 1, "Dead", "Hideout", false, false, [], false, null),
        ]);
        var rows = OverviewModel.SpyRows(result);
        Assert.Equal("Ann (rank 2): Coup in Sparta", rows[0].Summary);
        Assert.Equal(["Hideout", "Ur (Babylon)", "Kish"], rows[0].Moves.Select(it => it.Label));
        Assert.Null(rows[0].Moves[0].Command.CityId);
        Assert.Equal("c1", rows[0].Moves[1].Command.CityId);
        Assert.Equal("Ann", rows[0].Moves[2].Command.Spy);
        Assert.False(rows[0].Coup!.Stage); // already staged: the button calls it off
        Assert.Equal("Call off the coup", rows[0].CoupLabel);
        Assert.Empty(rows[1].Moves);
        Assert.Null(rows[1].Coup);
        Assert.Equal("", rows[1].CoupLabel);
        Assert.Equal("Cy: dead", rows[2].Summary);
        var staging = OverviewModel.SpyRows(new SpyDestinationsResult([new SpyEntry("Dee", 1, "Spying", "Ur", true, true, [], true, 0.5f)]));
        Assert.True(staging[0].Coup!.Stage);
        Assert.Equal("Stage a coup (50% chance)", staging[0].CoupLabel);
    }

    private static OverviewCiv Civ(string name, bool cityState = false, bool defeated = false, bool atWar = false) => new(name, cityState, defeated, atWar, new Dictionary<string, int>());

    private static DiplomacyPanelResult Panel(string status = "Peace", string relationship = "Neutral", float? influence = null, bool ally = false, bool ours = false, bool theirs = false, int treaty = 0) =>
        new("X", influence != null, false, status, relationship, [], [], influence, ally, treaty, false, ours, theirs, false, false, false, false, false, 0);

    [Fact]
    public void PoliticsIsTheLastTab()
    {
        Assert.Equal(OverviewModel.Politics, OverviewModel.Tabs[^1]);
        Assert.Equal("Politics", OverviewModel.Politics);
    }

    [Fact]
    public void PoliticsTableHasOneRowPerMetCivInTheEnginesOrderWithItsPanelWorded()
    {
        var table = OverviewModel.PoliticsTable([
            new PoliticsEntry(Civ("Rome"), Panel("War", "Enemy")),
            new PoliticsEntry(Civ("Athens", cityState: true), Panel("Protector", "Friend", influence: 42.5f, ally: true)),
            new PoliticsEntry(Civ("Greece"), Panel("DefensivePact", "Ally", ours: true, theirs: true, treaty: 7)),
        ]);
        Assert.Equal(["Civilization", "Kind", "Status", "Attitude", "Influence", "Borders", "Peace treaty"], table.Columns);
        Assert.Equal(["Rome", "Athens", "Greece"], table.Rows.Select(it => it.Key));
        Assert.Equal(["Rome", "Major", "War", "Enemy", "-", "-", "-"], table.Rows[0].Cells);
        Assert.Equal(["Athens", "City-state", "Protector", "Friend", "42.5 (ally)", "-", "-"], table.Rows[1].Cells);
        Assert.Equal(["Greece", "Major", "Defensive pact", "Ally", "-", "Both ways", "7 turns"], table.Rows[2].Cells);
        Assert.Equal("You have met nobody yet.", table.Empty);
    }

    [Fact]
    public void PoliticsInfluenceOfANonAllyHasNoAllyMarkAndBordersNameTheDirection()
    {
        var table = OverviewModel.PoliticsTable([
            new PoliticsEntry(Civ("Athens", cityState: true), Panel(influence: 10f, ours: true)),
            new PoliticsEntry(Civ("Rome"), Panel(theirs: true)),
        ]);
        Assert.Equal("10", table.Rows[0].Cells[4]);
        Assert.Equal("You may enter", table.Rows[0].Cells[5]);
        Assert.Equal("They may enter", table.Rows[1].Cells[5]);
    }

    [Fact]
    public void PoliticsDefeatedCivIsDefeatedAndACivWithoutPanelShowsDashesAndTheWarFlag()
    {
        var table = OverviewModel.PoliticsTable([
            new PoliticsEntry(Civ("Rome", defeated: true, atWar: true), Panel("War")),
            new PoliticsEntry(Civ("Greece", atWar: true), null),
            new PoliticsEntry(Civ("Egypt"), null),
        ]);
        Assert.Equal("Defeated", table.Rows[0].Cells[2]);
        Assert.Equal(["Greece", "Major", "War", "-", "-", "-", "-"], table.Rows[1].Cells);
        Assert.Equal("Peace", table.Rows[2].Cells[2]);
    }

    [Fact]
    public void PoliticsTableOfNobodyIsEmpty()
    {
        Assert.Equal(0, OverviewModel.PoliticsTable([]).Count);
    }

    [Fact]
    public void VoteLinesSayRefusedClosedOrListTheCandidatesByName()
    {
        Assert.Equal(["The engine would not show the diplomatic vote."], OverviewModel.VoteLines(null, it => it));
        Assert.Equal(["No diplomatic vote is open for you now."], OverviewModel.VoteLines(new DiplomaticVoteResult(false, ["x"]), it => it));
        Assert.Equal(["A diplomatic vote is open for you.", "You can vote for: Rome, Greece.", "You may also abstain."],
            OverviewModel.VoteLines(new DiplomaticVoteResult(true, ["id1", "id2"]), id => id == "id1" ? "Rome" : "Greece"));
    }
}
