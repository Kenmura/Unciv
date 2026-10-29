using Unciv.Assets;
using Unciv.Commands.City;
using Unciv.Logic.City;
using Unciv.Logic.Map;
using Unciv.Net.Visibility;
using Unciv.Queries.Cities;
using Unciv.Queries.Units;

namespace Unciv.Core.Tests.Assets;

public class CityScreenModelTests
{
    private static StatAmount Stat(string name, float value) => new(name, value);

    private static CityPanelResult Panel(bool puppet = false, int free = 1, string focus = "NoFocus", bool razed = false, bool resistance = false, bool capital = true,
        int foodForNextTurn = 3, int? starvation = null, int? growIn = 4, bool avoidGrowth = false, string construction = "Warrior") => new(
        "id1", "Ur", new TilePos(2, 3), capital, puppet, razed, resistance,
        new CityGrowth(4, free, 5, 14, foodForNextTurn, growIn, starvation), new CityBorders(3, 20, true),
        [Stat("Food", 7), Stat("Gold", -1.5f)],
        [new StatSource("Tile", [Stat("Food", 6)]), new StatSource("Population", [Stat("Food", 1), Stat("Gold", -1.5f)]), new StatSource("Building", [Stat("Culture", 2)])],
        [Stat("Luxury", 2), Stat("Unhappy", -1)],
        construction, [], [], [new NamedAmount("Scientist", 1)], [new NamedAmount("Artist", 1), new NamedAmount("Scientist", 2)],
        true, focus, avoidGrowth, [], null, null, [], [], []);

    private static CityTilesResult Tiles(params CityTile[] tiles) => new("id1", new TilePos(2, 3), 3, 1, tiles);

    private static CityTile Tile(int x, int y, string state, bool locked = false, params StatAmount[] stats) => new(new TilePos(x, y), state, locked, stats);

    private static CityTileCell Cell(string state, bool locked = false) => new(3, 5, state, locked, 0f, 0f, "", "");

    [Fact]
    public void Title_ShowsNameSizeAndFlags()
    {
        Assert.Equal("Ur (4) - Capital", CityScreenModel.Title(Panel()));
        Assert.Equal("Ur (4) - Puppet, Being razed, In resistance", CityScreenModel.Title(Panel(puppet: true, razed: true, resistance: true, capital: false)));
        Assert.Equal("Ur (4)", CityScreenModel.Title(Panel(capital: false)));
    }

    [Fact]
    public void InfoLines_DescribeGrowthBordersAndCitizens()
    {
        var lines = CityScreenModel.InfoLines(Panel());
        Assert.Equal("Food 5/14 (+3/turn), grows in 4 turns", lines[0]);
        Assert.Equal("Borders 3/20 culture", lines[1]);
        Assert.Equal("Free citizens 1   Happiness +1   Building Warrior", lines[2]);
        Assert.Contains("starving in 2 turns", CityScreenModel.InfoLines(Panel(foodForNextTurn: -2, starvation: 2))[0]);
        Assert.Contains("avoiding growth", CityScreenModel.InfoLines(Panel(avoidGrowth: true))[0]);
        Assert.Equal("Food 5/14 (+0/turn)", CityScreenModel.InfoLines(Panel(foodForNextTurn: 0, growIn: null))[0]);
        Assert.EndsWith("Building nothing", CityScreenModel.InfoLines(Panel(construction: ""))[2], StringComparison.Ordinal);
    }

    [Fact]
    public void StatBlock_ListsTotalsAndBreakdownsBySource()
    {
        Assert.Equal(["Food +7", "Gold -1.5"], CityScreenModel.StatLines(Panel()));
    }

    [Fact]
    public void YieldLabel_AbbreviatesStats()
    {
        Assert.Equal("F2 P1 G0.5 Fa1 X3", CityScreenModel.YieldLabel([Stat("Food", 2), Stat("Production", 1), Stat("Gold", 0.5f), Stat("Faith", 1), Stat("Xyz", 3)]));
        Assert.Equal("", CityScreenModel.YieldLabel([]));
        Assert.Equal("S1 C1 H1 ?1", CityScreenModel.YieldLabel([Stat("Science", 1), Stat("Culture", 1), Stat("Happiness", 1), Stat("", 1)]));
    }

    [Fact]
    public void Cells_AreCentredOnTheCityWithNorthUp()
    {
        var cells = CityScreenModel.Cells(Tiles(Tile(2, 3, "Center"), Tile(3, 3, "Worked", true, Stat("Food", 2)), Tile(2, 4, "Available")));
        Assert.Equal(3, cells.Count);
        var centre = cells.Single(it => it.State == "Center");
        Assert.Equal(0f, centre.Px, 4);
        Assert.Equal(0f, centre.Py, 4);
        var worked = cells.Single(it => it.State == "Worked");
        Assert.True(worked.Locked);
        Assert.Equal("F2", worked.Label);
        Assert.Equal(["Food"], worked.Yields.Select(it => it.Stat));
        Assert.NotEqual(0f, worked.Px);
        Assert.NotEqual(0f, cells[2].Py);
        // North is up: a tile whose world y is above the city's gets a negative Py (y down), one below it a positive Py.
        var cityWorld = HexMath.Hex2WorldCoords(new HexCoord(2, 3));
        var north = new[] { (2, 4), (3, 3), (1, 3), (2, 2), (3, 4), (1, 2) }.First(it => HexMath.Hex2WorldCoords(new HexCoord(it.Item1, it.Item2)).y > cityWorld.y + 0.01f);
        var south = new[] { (2, 4), (3, 3), (1, 3), (2, 2), (3, 4), (1, 2) }.First(it => HexMath.Hex2WorldCoords(new HexCoord(it.Item1, it.Item2)).y < cityWorld.y - 0.01f);
        var flipped = CityScreenModel.Cells(Tiles(Tile(north.Item1, north.Item2, "Available"), Tile(south.Item1, south.Item2, "Available")));
        Assert.True(flipped[0].Py < 0f);
        Assert.True(flipped[1].Py > 0f);
        var (minX, minY, maxX, maxY) = CityScreenModel.Bounds(cells);
        Assert.True(minX <= 0f && maxX >= 0f);
        Assert.True(minY <= 0f && maxY >= 0f);
        Assert.Equal((0f, 0f, 0f, 0f), CityScreenModel.Bounds([]));
    }

    [Theory]
    [InlineData("Center", false, "City center")]
    [InlineData("Worked", true, "Worked (locked)")]
    [InlineData("Worked", false, "Worked")]
    [InlineData("Taken", false, "Yields without a citizen")]
    [InlineData("Available", false, "click to work")]
    [InlineData("Unavailable", false, "Cannot be worked")]
    [InlineData("Foreign", false, "another civ")]
    [InlineData("Unowned", false, "Not owned")]
    public void TileTooltip_SaysWhatTheStateMeans(string state, bool locked, string expected)
    {
        var text = CityScreenModel.TileTooltip(Tile(1, 2, state, locked, Stat("Food", 2)));
        Assert.StartsWith("(1, 2) ", text, StringComparison.Ordinal);
        Assert.Contains(expected, text, StringComparison.Ordinal);
        Assert.EndsWith("Food 2", text, StringComparison.Ordinal);
        Assert.EndsWith("no yield", CityScreenModel.TileTooltip(Tile(0, 0, state)), StringComparison.Ordinal);
    }

    [Fact]
    public void ClickCommand_WorksOrStopsAccordingToTheState()
    {
        var work = Assert.IsType<WorkTile>(CityScreenModel.ClickCommand("id1", Cell("Available"), 1, false, out _));
        Assert.Equal("id1", work.CityId);
        Assert.Equal((3, 5), (work.Tile.x, work.Tile.y));
        var stop = Assert.IsType<StopWorkingTile>(CityScreenModel.ClickCommand("id1", Cell("Worked", true), 0, false, out _));
        Assert.Equal("id1", stop.CityId);
        Assert.Equal((3, 5), (stop.Tile.x, stop.Tile.y));
        Assert.Null(CityScreenModel.ClickCommand("id1", Cell("Worked"), 1, true, out var why));
        Assert.Contains("puppet", why, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Available", 0, false, "No free citizen")]
    [InlineData("Available", 1, true, "puppet")]
    [InlineData("Center", 1, false, "always worked")]
    [InlineData("Taken", 1, false, "cannot be worked")]
    [InlineData("Foreign", 1, false, "cannot be worked")]
    public void ClickCommand_ExplainsWhyNothingHappens(string state, int free, bool puppet, string reason)
    {
        Assert.Null(CityScreenModel.ClickCommand("id1", Cell(state), free, puppet, out var why));
        Assert.Contains(reason, why, StringComparison.Ordinal);
    }

    [Fact]
    public void LockCommand_PinsWorkedAndReleasesLockedTiles()
    {
        var pin = Assert.IsType<LockTile>(CityScreenModel.LockCommand("id1", Cell("Worked"), false, out _));
        Assert.Equal(("id1", 3, 5), (pin.CityId, pin.Tile.x, pin.Tile.y));
        var release = Assert.IsType<UnlockTile>(CityScreenModel.LockCommand("id1", Cell("Worked", true), false, out _));
        Assert.Equal(("id1", 3, 5), (release.CityId, release.Tile.x, release.Tile.y));
        Assert.Null(CityScreenModel.LockCommand("id1", Cell("Available"), false, out var why));
        Assert.Contains("worked tile", why, StringComparison.Ordinal);
        Assert.Null(CityScreenModel.LockCommand("id1", Cell("Worked"), true, out why));
        Assert.Contains("puppet", why, StringComparison.Ordinal);
    }

    [Fact]
    public void Specialists_OfferPlusAndMinusLikeTheEngine()
    {
        var rows = CityScreenModel.Specialists(Panel());
        Assert.Equal(["Artist", "Scientist"], rows.Select(it => it.Name));
        var artist = rows[0];
        Assert.Equal((0, 1, true, false), (artist.Assigned, artist.Max, artist.CanAdd, artist.CanRemove));
        var scientist = rows[1];
        Assert.Equal((1, 2, true, true), (scientist.Assigned, scientist.Max, scientist.CanAdd, scientist.CanRemove));
        Assert.All(CityScreenModel.Specialists(Panel(free: 0)), it => Assert.False(it.CanAdd));
        Assert.All(CityScreenModel.Specialists(Panel(puppet: true)), it => Assert.False(it.CanAdd || it.CanRemove));
        Assert.IsType<AssignSpecialist>(CityScreenModel.SpecialistCommand("id1", artist, add: true));
        var remove = Assert.IsType<UnassignSpecialist>(CityScreenModel.SpecialistCommand("id1", scientist, add: false));
        Assert.Equal("Scientist", remove.Specialist);
    }

    [Fact]
    public void Focuses_HideTheEngineHiddenOnesAndMarkTheCurrent()
    {
        var withFaith = CityScreenModel.Focuses(Panel(focus: "GoldFocus"), religionEnabled: true);
        Assert.DoesNotContain(withFaith, it => it.Focus == CityFocus.HappinessFocus);
        Assert.Contains(withFaith, it => it.Focus == CityFocus.FaithFocus);
        Assert.Equal(CityFocus.GoldFocus, withFaith.Single(it => it.Current).Focus);
        Assert.DoesNotContain(CityScreenModel.Focuses(Panel(), religionEnabled: false), it => it.Focus == CityFocus.FaithFocus);
        Assert.Equal(["Default", "Manual", "Food", "Production", "Gold + Food", "Production + Food"],
            new[] { CityFocus.NoFocus, CityFocus.Manual, CityFocus.FoodFocus, CityFocus.ProductionFocus, CityFocus.GoldGrowthFocus, CityFocus.ProductionGrowthFocus }.Select(CityScreenModel.FocusLabel));
    }

    [Fact]
    public void Foreign_ShowsOnlyTheSnapshotEntry()
    {
        var city = new CityDto
        {
            Name = "Memphis",
            Owner = "Egypt",
            X = 5,
            Y = 5,
            Population = 6,
            IsCapital = true,
            IsPuppet = false,
            IsBeingRazed = true,
            Health = 12,
            MaxHealth = 20,
            VisibleBuildings = ["Pyramids"],
        };
        var text = CityScreenModel.Foreign(city);
        Assert.Equal("Memphis", text.Title);
        Assert.Equal(["Owner: Egypt", "Population: 6", "Capital", "Being razed", "Defence: 12/20", "Wonders: Pyramids", "You cannot see inside this city."], text.Lines);
        var plain = CityScreenModel.Foreign(city with { IsCapital = false, IsPuppet = true, IsBeingRazed = false, VisibleBuildings = [] });
        Assert.Contains("Puppet city", plain.Lines);
        Assert.Contains("No wonders", plain.Lines);
    }

    [Fact]
    public void Meters_UseTheInfoLineWordsAndTheEnginesNumbers()
    {
        var city = Panel();
        var food = CityScreenModel.FoodMeter(city);
        var borders = CityScreenModel.BorderMeter(city);
        Assert.Equal(("Food", (float)city.Growth.FoodStored, (float)city.Growth.FoodToNextPopulation, CityScreenModel.InfoLines(city)[0]), (food.Stat, food.Value, food.Max, food.Caption));
        Assert.Equal(("Culture", (float)city.Borders.CultureStored, (float)city.Borders.CultureToNextTile), (borders.Stat, borders.Value, borders.Max));
    }
}
