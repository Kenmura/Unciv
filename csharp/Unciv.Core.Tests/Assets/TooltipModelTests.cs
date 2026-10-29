using Unciv.Assets;
using Unciv.Harness;
using Unciv.Queries.Cities;
using Unciv.Queries.Tiles;
using Unciv.Queries.Units;
using Unciv.UI.ObjectDescriptions;
using Unciv.Utils;

namespace Unciv.Core.Tests.Assets;

/// <summary>C027: tooltip content builders. Unit and building text must equal the engine's own descriptions.</summary>
[Collection("RulesetCache")]
public class TooltipModelTests
{
    private static TileInfoResult TileOf(
        string terrain = "Grassland", string[]? features = null, string? wonder = null, string? resource = null, int amount = 0, string? improvement = null, bool pillaged = false,
        string? inProgress = null, int turns = 0, string road = "None", bool roadPillaged = false, bool river = false, string? owner = null, string? city = null, string? worked = null,
        bool centre = false, TileUnit[]? units = null, StatAmount[]? yields = null, bool visible = true, StatSource[]? breakdown = null) =>
        new(new TilePos(1, 2), terrain, features ?? [], wonder, resource, amount, improvement, pillaged, inProgress, turns, road, roadPillaged, river, owner, city, worked, false, centre, visible,
            yields ?? [], breakdown ?? [], units ?? [], null);

    private static string[] Texts(IEnumerable<TipLine> lines) => lines.Select(it => it.Text).ToArray();

    // C088
    [Fact]
    public void CitySite_ValidSiteListsYieldsResourcesWondersAndTheUnexploredCount()
    {
        var site = new CitySiteResult(new TilePos(1, 2), true, [], 3, [], [new StatAmount("Food", 12f), new StatAmount("Production", 7f)], [new NamedAmount("Wheat", 2)], ["Krakatoa"], 5);
        var lines = TooltipModel.CitySite(site);
        Assert.Equal(["City site", "{Food}: 12", "{Production}: 7", "{Wheat} (2)", "Krakatoa", "[5] unexplored tiles"], Texts(lines));
        Assert.Equal(TipKind.Heading, lines[0].Kind);
        Assert.Equal(TipKind.Footer, lines[^1].Kind);
    }

    [Fact]
    public void CitySite_InvalidSiteSaysWhyAndNamesTheNearCity()
    {
        var site = new CitySiteResult(new TilePos(1, 2), false,
            [new CitySiteProblem(CitySiteProblemCodes.Water, null), new CitySiteProblem(CitySiteProblemCodes.TooCloseToCity, "Berlin"),
             new CitySiteProblem(CitySiteProblemCodes.Impassable, null), new CitySiteProblem(CitySiteProblemCodes.ForeignTerritory, null), new CitySiteProblem("FutureProblem", null)], 3, [], [], [], [], 0);
        Assert.Equal(["Cannot found a city here", "Cannot found a city on water", "Too close to [Berlin]", "Cannot found a city on impassable terrain", "In foreign territory", "FutureProblem"], Texts(TooltipModel.CitySite(site)));
    }

    [Fact]
    public void Tile_ListsTerrainFirstAndOnlyWhatIsThere()
    {
        var ruleset = ScenarioGame.LoadVanilla();
        Assert.Equal(["Grassland"], Texts(TooltipModel.Tile(TileOf(), ruleset)));
        Assert.Equal(["Plains", "Forest", "Fresh Water"], Texts(TooltipModel.Tile(TileOf("Plains", ["Forest", "Fresh Water"]), ruleset)));
        Assert.Equal(["Hill", "Krakatoa"], Texts(TooltipModel.Tile(TileOf("Hill", wonder: "Krakatoa"), ruleset)));
    }

    [Fact]
    public void Tile_ShowsTheAmountOfStrategicResourcesOnly()
    {
        var ruleset = ScenarioGame.LoadVanilla();
        Assert.Equal(["Plains", "{Iron} (4)"], Texts(TooltipModel.Tile(TileOf("Plains", resource: "Iron", amount: 4), ruleset)));
        Assert.Equal(["Plains", "Gems"], Texts(TooltipModel.Tile(TileOf("Plains", resource: "Gems", amount: 1), ruleset)));
        Assert.Equal(["Plains", "Mystery"], Texts(TooltipModel.Tile(TileOf("Plains", resource: "Mystery", amount: 1), ruleset)));
    }

    [Fact]
    public void Tile_ImprovementsRoadsRiversAndOwnership()
    {
        var ruleset = ScenarioGame.LoadVanilla();
        Assert.Equal(["Plains", "Farm"], Texts(TooltipModel.Tile(TileOf("Plains", improvement: "Farm"), ruleset)));
        Assert.Equal(["Plains", "[Farm] (Pillaged!)"], Texts(TooltipModel.Tile(TileOf("Plains", improvement: "Farm", pillaged: true), ruleset)));
        Assert.Equal(["Plains", "{Mine} - 3"], Texts(TooltipModel.Tile(TileOf("Plains", inProgress: "Mine", turns: 3), ruleset)));
        Assert.Equal(["Plains", "{Mine} ({Under construction})"], Texts(TooltipModel.Tile(TileOf("Plains", inProgress: "Mine", turns: -1), ruleset)));
        Assert.Equal(["Plains", "Road"], Texts(TooltipModel.Tile(TileOf("Plains", road: "Road"), ruleset)));
        Assert.Equal(["Plains", "[Road] (Pillaged!)"], Texts(TooltipModel.Tile(TileOf("Plains", road: "Road", roadPillaged: true), ruleset)));
        Assert.Equal(["Plains", "River"], Texts(TooltipModel.Tile(TileOf("Plains", river: true), ruleset)));
        Assert.Equal(["Plains", "Owned by [Ur]", "Worked by [Ur]"], Texts(TooltipModel.Tile(TileOf("Plains", owner: "Babylon", city: "Ur", worked: "Ur"), ruleset)));
        Assert.Equal(["Plains", "Babylon"], Texts(TooltipModel.Tile(TileOf("Plains", owner: "Babylon"), ruleset)));
    }

    [Fact]
    public void Tile_InTheFogHidesWhatIsGoingOnNow()
    {
        var ruleset = ScenarioGame.LoadVanilla();
        var lines = TooltipModel.Tile(TileOf("Plains", resource: "Iron", amount: 4, improvement: "Farm", pillaged: true, inProgress: "Mine", turns: 3, road: "Road", roadPillaged: true,
            owner: "Babylon", city: "Ur", worked: "Ur", yields: [new StatAmount("Food", 2f)], visible: false), ruleset);
        Assert.Equal(["Plains", "Iron", "Farm", "Road", "Owned by [Ur]"], Texts(lines));
    }

    [Fact]
    public void Tile_CityCentreStartsWithTheCityAndHidesTheRoad()
    {
        var ruleset = ScenarioGame.LoadVanilla();
        var lines = TooltipModel.Tile(TileOf("Plains", road: "Road", owner: "Babylon", city: "Ur", centre: true), ruleset);
        Assert.Equal(["Ur", "Plains", "Owned by [Ur]"], Texts(lines));
        Assert.Equal(["Plains"], Texts(TooltipModel.Tile(TileOf("Plains", city: "Ur"), ruleset)));
    }

    [Fact]
    public void Tile_UnitsAndYieldsComeLast()
    {
        var ruleset = ScenarioGame.LoadVanilla();
        var lines = TooltipModel.Tile(TileOf("Plains", units: [new TileUnit(1, "Warrior", "Rome")], yields: [new StatAmount("Food", 1f), new StatAmount("Production", 1.5f)]), ruleset);
        Assert.Equal(["Plains", "Warrior - Rome", "{Food}: 1", "{Production}: 1.5"], Texts(lines));
        Assert.All(lines, it => Assert.False(it.Final));
    }

    [Fact]
    public void Tile_YieldsAreFollowedByTheirBreakdownOnlyOnSeenTiles()
    {
        var ruleset = ScenarioGame.LoadVanilla();
        StatAmount[] yields = [new("Food", 3f), new("Gold", 1f)];
        StatSource[] sources = [new("Base terrain", [new StatAmount("Food", 2f)]), new("Farm", [new StatAmount("Food", 1f)]), new("River", [new StatAmount("Gold", 1f)])];
        Assert.Equal(["Plains", "{Food}: 3", "{Gold}: 1", "{Food}", "{Base terrain}: +2", "{Farm}: +1", "{Total}: +3", "{Gold}", "{River}: +1", "{Total}: +1"],
            Texts(TooltipModel.Tile(TileOf("Plains", yields: yields, breakdown: sources), ruleset)));
        // the engine's own yield is shown as the total even when the sources do not add up to it
        Assert.Equal("{Total}: +5", Texts(TooltipModel.Tile(TileOf("Plains", yields: [new StatAmount("Food", 5f)], breakdown: sources), ruleset))[^1]);
        // a tile in the fog tells nothing of its yields or where they come from
        Assert.Equal(["Plains"], Texts(TooltipModel.Tile(TileOf("Plains", yields: yields, breakdown: sources, visible: false), ruleset)));
        // no breakdown known: just the yield line
        Assert.Equal(["Plains", "{Food}: 3"], Texts(TooltipModel.Tile(TileOf("Plains", yields: [new StatAmount("Food", 3f)]), ruleset)));
    }

    [Fact]
    public void StatBreakdown_ShowsTheEnginesTotalWhenGiven()
    {
        StatSource[] sources = [new("Tile", [new StatAmount("Food", 6f)]), new("Population", [new StatAmount("Food", 1f)])];
        Assert.Equal(["{Food}", "{Tile}: +6", "{Population}: +1", "{Total}: +9.5"], Texts(TooltipModel.StatBreakdown("Food", sources, 9.5f)));
        Assert.Equal("{Total}: +7", Texts(TooltipModel.StatBreakdown("Food", sources))[^1]);
        Assert.Equal("{Total}: +0", Texts(TooltipModel.StatBreakdown("Food", sources, 0f))[^1]);
    }

    [Fact]
    public void ToPlain_TranslatesOnlyNonFinalLinesAndDoesNotEscape()
    {
        Assert.Equal("A\nb [x]\n[C]", TooltipModel.ToPlain([new TipLine("a"), new TipLine("b [x]", true), new TipLine("[c]")], it => it.ToUpperInvariant()));
        Assert.Equal("", TooltipModel.ToPlain([], it => it));
    }

    [Fact]
    public void Unit_IsTheRulesetsOwnShortDescription()
    {
        var ruleset = ScenarioGame.LoadVanilla();
        foreach (string name in new[] { "Warrior", "Archer", "Settler", "Worker" })
        {
            var lines = TooltipModel.Unit(ruleset, name).NotNull();
            var unit = ruleset.Units[name];
            Assert.Equal(new TipLine(name, Kind: TipKind.Heading), lines[0]);
            Assert.Equal(new TipLine($"{{Cost}}: {unit.Cost}", Kind: TipKind.Stat, Stat: "Production"), lines[1]);
            var description = BaseUnitDescriptions.GetShortDescription(unit);
            int at = 2;
            if (description.Length != 0) Assert.Equal(new TipLine(description, true), lines[at++]);
            if (unit.RequiredTech != null) Assert.Equal(new TipLine($"Requires [{unit.RequiredTech}]", Kind: TipKind.Footer), lines[at++]);
            Assert.Equal(at, lines.Count);
        }
        Assert.Null(TooltipModel.Unit(ruleset, "Nothing"));
    }

    [Fact]
    public void Unit_WithoutCostOrDescriptionIsJustTheName()
    {
        var ruleset = new Unciv.Models.Ruleset.Ruleset();
        ruleset.Units["Plain"] = new Unciv.Models.Ruleset.Unit.BaseUnit { Name = "Plain", Movement = 2 };
        Assert.Equal(["Plain"], Texts(TooltipModel.Unit(ruleset, "Plain").NotNull()));
        ruleset.Units["Free"] = new Unciv.Models.Ruleset.Unit.BaseUnit { Name = "Free", Movement = 2, Cost = 0 };
        Assert.Equal(["Free"], Texts(TooltipModel.Unit(ruleset, "Free").NotNull()));
    }

    [Fact]
    public void Building_IsTheRulesetsOwnMultilineDescription()
    {
        var ruleset = ScenarioGame.LoadVanilla();
        foreach (string name in new[] { "Monument", "Granary", "Stonehenge", "Barracks" })
        {
            var lines = TooltipModel.Building(ruleset, name).NotNull();
            var building = ruleset.Buildings[name];
            Assert.Equal(name, lines[0].Text);
            Assert.Equal(building.Cost > 0, lines.Any(it => it.Text == $"{{Cost}}: {building.Cost}"));
            Assert.Equal(new TipLine(BuildingDescriptions.GetShortDescription(building, multiline: true), true), lines.Single(it => it.Final));
            Assert.Equal(building.RequiredTech == null ? TipKind.Body : TipKind.Footer, lines[^1].Kind);
        }
        Assert.Null(TooltipModel.Building(ruleset, "Nothing"));
    }

    [Fact]
    public void Building_WithoutCostOrDescriptionIsJustTheName()
    {
        var ruleset = new Unciv.Models.Ruleset.Ruleset();
        ruleset.Buildings["Empty"] = new Unciv.Models.Ruleset.Building { Name = "Empty", Cost = -1 };
        Assert.Equal(["Empty"], Texts(TooltipModel.Building(ruleset, "Empty").NotNull()));
    }

    [Fact]
    public void StatBreakdown_ListsContributingSourcesAndTheirSum()
    {
        StatSource[] sources =
        [
            new("Base terrain", [new StatAmount("Food", 2f), new StatAmount("Production", 1f)]),
            new("Farm", [new StatAmount("Food", 1.5f)]),
            new("River", [new StatAmount("Gold", 1f)]),
            new("Zero", [new StatAmount("Food", 0f)]),
        ];
        Assert.Equal(["{Food}", "{Base terrain}: +2", "{Farm}: +1.5", "{Total}: +3.5"], Texts(TooltipModel.StatBreakdown("Food", sources)));
        Assert.Equal(["{Faith}", "{Total}: +0"], Texts(TooltipModel.StatBreakdown("Faith", sources)));
        Assert.Equal(["{Gold}", "{Upkeep}: -2", "{Total}: -2"], Texts(TooltipModel.StatBreakdown("Gold", [new StatSource("Upkeep", [new StatAmount("Gold", -2f)])])));
    }

    [Fact]
    public void ToBbcode_TranslatesOnlyNonFinalLinesAndEscapesBrackets()
    {
        var text = TooltipModel.ToBbcode([new TipLine("a"), new TipLine("b [x]", true), new TipLine("[c]")], it => it.ToUpperInvariant());
        Assert.Equal("A\nb [lb]x]\n[lb]C]", text);
        Assert.Equal("", TooltipModel.ToBbcode([], it => it));
    }

    [Fact]
    public void Tile_HeadingIsTheCityOnACentreElseTheTerrain_YieldsAreStatLines()
    {
        var ruleset = ScenarioGame.LoadVanilla();
        var plain = TooltipModel.Tile(TileOf("Plains", yields: [new StatAmount("Food", 2f), new StatAmount("Gold", 1f)]), ruleset);
        Assert.Equal([TipKind.Heading, TipKind.Stat, TipKind.Stat], plain.Select(it => it.Kind));
        Assert.Equal(["Food", "Gold"], plain.Where(it => it.Kind == TipKind.Stat).Select(it => it.Stat));
        var city = TooltipModel.Tile(TileOf("Plains", owner: "Rome", city: "Rome", centre: true), ruleset);
        Assert.Equal([new TipLine("Rome", Kind: TipKind.Heading), new TipLine("Plains"), new TipLine("Owned by [Rome]")], city.Take(3));
    }

    [Fact]
    public void ToBbcode_StylesEachKindAndPutsConsecutiveStatsOnOneRow()
    {
        var text = TooltipModel.ToBbcode(
        [
            new TipLine("Head", Kind: TipKind.Heading), new TipLine("body"), new TipLine("1", Kind: TipKind.Stat, Stat: "Food"), new TipLine("2", Kind: TipKind.Stat, Stat: "Gold"),
            new TipLine("src", Kind: TipKind.Source), new TipLine("Requires [x]", Kind: TipKind.Footer),
        ], it => it);
        string[] rows = text.Split('\n');
        Assert.Equal(5, rows.Length);
        Assert.StartsWith("[b][color=#ECE8DF]Head[/color][/b]", rows[0], StringComparison.Ordinal);
        Assert.Equal("body", rows[1]);
        Assert.Contains("1[/color][/b]    [b][color=", rows[2], StringComparison.Ordinal);
        Assert.Contains(UiText.Hex(PlaceholderPalette.ForStat("Food")), rows[2], StringComparison.Ordinal);
        Assert.Contains(UiText.Hex(PlaceholderPalette.ForStat("Gold")), rows[2], StringComparison.Ordinal);
        Assert.Contains(UiText.Hex(UiTokens.TextSecondary), rows[3], StringComparison.Ordinal);
        Assert.StartsWith("[font_size=12][color=" + UiText.Hex(UiTokens.TextMuted), rows[4], StringComparison.Ordinal);
    }
}
