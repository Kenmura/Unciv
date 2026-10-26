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
        bool centre = false, TileUnit[]? units = null, StatAmount[]? yields = null) =>
        new(new TilePos(1, 2), terrain, features ?? [], wonder, resource, amount, improvement, pillaged, inProgress, turns, road, roadPillaged, river, owner, city, worked, false, centre, true,
            yields ?? [], [], units ?? [], null);

    private static string[] Texts(IEnumerable<TipLine> lines) => lines.Select(it => it.Text).ToArray();

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
        Assert.Equal(["Plains", "Warrior - Rome", "{Food}: 1, {Production}: 1.5"], Texts(lines));
        Assert.All(lines, it => Assert.False(it.Final));
    }

    [Fact]
    public void Unit_IsTheRulesetsOwnShortDescription()
    {
        var ruleset = ScenarioGame.LoadVanilla();
        foreach (string name in new[] { "Warrior", "Archer", "Settler", "Worker" })
        {
            var lines = TooltipModel.Unit(ruleset, name).NotNull();
            var unit = ruleset.Units[name];
            Assert.Equal(name, lines[0].Text);
            Assert.Equal($"{{Cost}}: {unit.Cost}", lines[1].Text);
            var description = BaseUnitDescriptions.GetShortDescription(unit);
            if (description.Length != 0) Assert.Equal(new TipLine(description, true), lines[^1]);
            Assert.Equal(description.Length != 0 ? 3 : 2, lines.Count);
        }
        Assert.Null(TooltipModel.Unit(ruleset, "Nothing"));
    }

    [Fact]
    public void Unit_WithoutCostOrDescriptionIsJustTheName()
    {
        var ruleset = new Unciv.Models.Ruleset.Ruleset();
        ruleset.Units["Plain"] = new Unciv.Models.Ruleset.Unit.BaseUnit { Name = "Plain", Movement = 2 };
        Assert.Equal(["Plain"], Texts(TooltipModel.Unit(ruleset, "Plain").NotNull()));
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
            Assert.Equal(new TipLine(BuildingDescriptions.GetShortDescription(building, multiline: true), true), lines[^1]);
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
}
