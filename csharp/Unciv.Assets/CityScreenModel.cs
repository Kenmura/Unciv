using System.Globalization;
using Unciv.Commands;
using Unciv.Commands.City;
using Unciv.Logic.City;
using Unciv.Logic.Map;
using Unciv.Net.Visibility;
using Unciv.Queries.Cities;

namespace Unciv.Assets;

/// <summary>One tile cell of the city screen: where to draw it (<see cref="Px"/>, <see cref="Py"/> in tile-radius units around the city, y down), its engine state and a compact yield label.</summary>
public sealed record CityTileCell(int X, int Y, string State, bool Locked, float Px, float Py, string Label, string Tooltip)
{
    /// <summary>C069: the tile's yields as numbers (the view draws a stat icon and the value for each); <see cref="Label"/> is the same as text.</summary>
    public IReadOnlyList<Queries.Cities.StatAmount> Yields { get; init; } = [];
}

/// <summary>C069: a progress meter of the city header: the stat it is coloured by, the filled amount, the total and the caption beside it.</summary>
public sealed record CityMeter(string Stat, float Value, float Max, string Caption);

/// <summary>A specialist line: assigned of max slots, and whether the + and - buttons are offered (the engine refuses the same cases).</summary>
public sealed record SpecialistRow(string Name, int Assigned, int Max, bool CanAdd, bool CanRemove);

/// <summary>A citizen focus the player may choose; <see cref="Current"/> marks the active one.</summary>
public sealed record FocusChoice(CityFocus Focus, string Label, bool Current);

/// <summary>What a foreign (or otherwise not fully visible) city shows: only what the viewer's snapshot carries for it.</summary>
public sealed record ForeignCityText(string Title, IReadOnlyList<string> Lines);

/// <summary>
/// C017: the city screen's content as plain data, Godot-free so it is unit-tested. It formats the engine's <see cref="CityPanelResult"/> and
/// <see cref="CityTilesResult"/> and decides which command a click means; it computes no game value (yields, growth, costs, slots come from the engine answers).
/// A city of another civ is described from its snapshot <see cref="CityDto"/> alone, so nothing private can show.
/// </summary>
public static class CityScreenModel
{
    /// <summary>The circumradius of a tile hexagon in the units of <see cref="CityTileCell.Px"/> (neighbours are 1.732 apart, so a little under 1 leaves a gap).</summary>
    public const float HexCircumradius = 0.96f;

    /// <summary>Title line: name, size and the notable flags.</summary>
    public static string Title(CityPanelResult city)
    {
        var flags = new List<string>();
        if (city.IsCapital) flags.Add("Capital");
        if (city.IsPuppet) flags.Add("Puppet");
        if (city.IsBeingRazed) flags.Add("Being razed");
        if (city.IsInResistance) flags.Add("In resistance");
        string suffix = flags.Count == 0 ? "" : " - " + string.Join(", ", flags);
        return $"{city.Name} ({city.Growth.Population.ToString(CultureInfo.InvariantCulture)}){suffix}";
    }

    /// <summary>The info lines under the title: growth, borders, the citizen counts and happiness.</summary>
    public static IReadOnlyList<string> InfoLines(CityPanelResult city)
    {
        var growth = city.Growth;
        string food = $"Food {I(growth.FoodStored)}/{I(growth.FoodToNextPopulation)} ({Signed(growth.FoodForNextTurn)}/turn)";
        if (growth.FoodForNextTurn < 0 && growth.TurnsToStarvation is { } starve) food += $", starving in {I(starve)} turns";
        else if (city.AvoidGrowth) food += ", avoiding growth";
        else if (growth.TurnsToNewPopulation is { } grow) food += $", grows in {I(grow)} turns";
        string borders = $"Borders {I(city.Borders.CultureStored)}/{I(city.Borders.CultureToNextTile)} culture";
        float happiness = city.Happiness.Sum(it => it.Value);
        return
        [
            food,
            borders,
            $"Free citizens {I(growth.FreePopulation)}   Happiness {Signed(happiness)}   Building {(city.CurrentConstruction.Length == 0 ? "nothing" : city.CurrentConstruction)}",
        ];
    }

    /// <summary>C069: the growth meter ("Food 5/20 (+2/turn), grows in 8 turns"), the same words as the first info line.</summary>
    public static CityMeter FoodMeter(CityPanelResult city) =>
        new("Food", city.Growth.FoodStored, city.Growth.FoodToNextPopulation, InfoLines(city)[0]);

    /// <summary>C069: the border meter (culture stored toward the next tile), the same words as the second info line.</summary>
    public static CityMeter BorderMeter(CityPanelResult city) =>
        new("Culture", city.Borders.CultureStored, city.Borders.CultureToNextTile, InfoLines(city)[1]);

    /// <summary>The city's totals, one entry per nonzero stat, for the stat block.</summary>
    public static IReadOnlyList<string> StatLines(CityPanelResult city) =>
        city.TotalStats.Select(it => $"{it.Stat} {Signed(it.Value)}").ToList();

    /// <summary>A short label of a tile's yields, e.g. <c>F2 P1 G1</c>.</summary>
    public static string YieldLabel(IReadOnlyList<Queries.Cities.StatAmount> stats) =>
        string.Join(" ", stats.Select(it => $"{StatLetter(it.Stat)}{Number(it.Value)}"));

    /// <summary>
    /// The tile cells with their screen positions. The layout uses the engine's own hex maths (<see cref="HexMath.Hex2WorldCoords"/>), centred on the city,
    /// north up, scaled so a tile is one unit across its short axis; <see cref="Bounds"/> gives the extent to fit.
    /// </summary>
    public static IReadOnlyList<CityTileCell> Cells(CityTilesResult tiles)
    {
        var centre = HexMath.Hex2WorldCoords(new HexCoord(tiles.Center.X, tiles.Center.Y));
        var cells = new List<CityTileCell>();
        foreach (var tile in tiles.Tiles)
        {
            var world = HexMath.Hex2WorldCoords(new HexCoord(tile.Position.X, tile.Position.Y));
            string label = YieldLabel(tile.Stats);
            cells.Add(new CityTileCell(tile.Position.X, tile.Position.Y, tile.State, tile.Locked, world.x - centre.x, -(world.y - centre.y), label, TileTooltip(tile))
            {
                Yields = tile.Stats,
            });
        }
        return cells;
    }

    /// <summary>The smallest rectangle (min x, min y, max x, max y) around the cell centres.</summary>
    public static (float MinX, float MinY, float MaxX, float MaxY) Bounds(IReadOnlyList<CityTileCell> cells) =>
        cells.Count == 0
            ? (0f, 0f, 0f, 0f)
            : (cells.Min(it => it.Px), cells.Min(it => it.Py), cells.Max(it => it.Px), cells.Max(it => it.Py));

    public static string TileTooltip(CityTile tile)
    {
        string state = tile.State switch
        {
            "Center" => "City center",
            "Worked" => tile.Locked ? "Worked (locked)" : "Worked",
            "Taken" => "Yields without a citizen, or worked by another city",
            "Available" => "Available: click to work",
            "Unavailable" => "Cannot be worked now",
            "Foreign" => "Owned by another civ",
            _ => "Not owned",
        };
        string yield = tile.Stats.Count == 0 ? "no yield" : string.Join(", ", tile.Stats.Select(it => $"{it.Stat} {Number(it.Value)}"));
        return $"({I(tile.Position.X)}, {I(tile.Position.Y)}) {state}\n{yield}";
    }

    /// <summary>
    /// The command a plain click on a tile means: put a free citizen on an available tile, take the citizen off a worked one. Null with a reason in
    /// <paramref name="why"/> when the click does nothing (the engine would refuse the same). The city is never <c>puppet</c>-changeable, so a puppet gets no commands.
    /// </summary>
    public static GameCommand? ClickCommand(string cityId, CityTileCell cell, int freePopulation, bool isPuppet, out string why)
    {
        why = "";
        if (isPuppet)
        {
            why = "A puppet city manages its own citizens.";
            return null;
        }
        var position = new HexCoord(cell.X, cell.Y);
        switch (cell.State)
        {
            case "Worked":
                return new StopWorkingTile { CityId = cityId, Tile = position };
            case "Available":
                if (freePopulation > 0) return new WorkTile { CityId = cityId, Tile = position };
                why = "No free citizen: take one off another tile or a specialist first.";
                return null;
            case "Center":
                why = "The city center is always worked.";
                return null;
            default:
                why = "That tile cannot be worked.";
                return null;
        }
    }

    /// <summary>The command a lock click means: pin a worked tile, release a locked one. Null with a reason otherwise.</summary>
    public static GameCommand? LockCommand(string cityId, CityTileCell cell, bool isPuppet, out string why)
    {
        why = "";
        if (isPuppet)
        {
            why = "A puppet city manages its own citizens.";
            return null;
        }
        var position = new HexCoord(cell.X, cell.Y);
        if (cell.Locked) return new UnlockTile { CityId = cityId, Tile = position };
        if (cell.State == "Worked") return new LockTile { CityId = cityId, Tile = position };
        why = "Only a worked tile can be locked.";
        return null;
    }

    /// <summary>The specialist lines for every slot type the city has (or uses), in name order.</summary>
    public static IReadOnlyList<SpecialistRow> Specialists(CityPanelResult city)
    {
        var assigned = city.Specialists.ToDictionary(it => it.Name, it => it.Amount, StringComparer.Ordinal);
        var max = city.MaxSpecialists.ToDictionary(it => it.Name, it => it.Amount, StringComparer.Ordinal);
        bool changeable = !city.IsPuppet;
        return assigned.Keys.Union(max.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Select(name =>
            {
                int have = assigned.GetValueOrDefault(name);
                int slots = max.GetValueOrDefault(name);
                return new SpecialistRow(name, have, slots, changeable && have < slots && city.Growth.FreePopulation > 0, changeable && have > 0);
            })
            .Where(it => it.Max > 0 || it.Assigned > 0)
            .ToList();
    }

    public static GameCommand SpecialistCommand(string cityId, SpecialistRow row, bool add) =>
        add ? new AssignSpecialist { CityId = cityId, Specialist = row.Name } : new UnassignSpecialist { CityId = cityId, Specialist = row.Name };

    /// <summary>The focuses the player can choose (the engine's list minus the hidden ones), the active one marked.</summary>
    public static IReadOnlyList<FocusChoice> Focuses(CityPanelResult city, bool religionEnabled)
    {
        var choices = new List<FocusChoice>();
        foreach (var focus in CityFocusExtensions.Entries)
        {
            if (!focus.TableEnabled()) continue;
            if (focus == CityFocus.FaithFocus && !religionEnabled) continue;
            choices.Add(new FocusChoice(focus, FocusLabel(focus), focus.ToString() == city.CityFocus));
        }
        return choices;
    }

    /// <summary>Plain-text name of a focus for the dropdown ("Food", "Gold + Food"...).</summary>
    public static string FocusLabel(CityFocus focus) => focus switch
    {
        CityFocus.NoFocus => "Default",
        CityFocus.Manual => "Manual",
        CityFocus.GoldGrowthFocus => "Gold + Food",
        CityFocus.ProductionGrowthFocus => "Production + Food",
        _ => focus.ToString().Replace("Focus", "", StringComparison.Ordinal),
    };

    /// <summary>What the viewer may see of a city: its snapshot entry only. A city of the viewer shows its fuller panel instead (<see cref="CityPanelResult"/>).</summary>
    public static ForeignCityText Foreign(CityDto city)
    {
        var lines = new List<string> { "Owner: " + city.Owner, "Population: " + I(city.Population) };
        if (city.IsCapital) lines.Add("Capital");
        if (city.IsPuppet) lines.Add("Puppet city");
        if (city.IsBeingRazed) lines.Add("Being razed");
        lines.Add($"Defence: {I(city.Health)}/{I(city.MaxHealth)}");
        lines.Add(city.VisibleBuildings.Count == 0 ? "No wonders" : "Wonders: " + string.Join(", ", city.VisibleBuildings));
        lines.Add("You cannot see inside this city.");
        return new ForeignCityText(city.Name, lines);
    }

    private static string StatLetter(string stat) => stat switch
    {
        "Food" => "F",
        "Production" => "P",
        "Gold" => "G",
        "Science" => "S",
        "Culture" => "C",
        "Faith" => "Fa",
        "Happiness" => "H",
        _ => stat.Length == 0 ? "?" : stat[..1],
    };

    private static string I(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Number(float value) => value.ToString("0.#", CultureInfo.InvariantCulture);

    private static string Signed(float value) => (value >= 0 ? "+" : "") + Number(value);

    private static string Signed(int value) => (value >= 0 ? "+" : "") + I(value);
}
