using Godot;
using Unciv.Assets;
using Unciv.Commands;
using Unciv.Commands.City;
using Unciv.GodotClient.Ui;
using Unciv.Logic.City;
using Unciv.Net.Visibility;
using Unciv.Queries.Cities;
using Unciv.Models.Translations;
using Unciv.GodotClient.Localization;

namespace Unciv.GodotClient.Play;

/// <summary>
/// C017: the city screen, part A: a header (name, size, growth, borders), the city's stats with a per-source tooltip, the tile grid (worked, locked, available and
/// unavailable tiles; click to put a citizen on a tile or take it off, right click to lock or unlock), specialists, the citizen focus and "Reset citizens".
/// It draws the engine's answers (<see cref="CityScreenModel"/> formats them) and changes the city only by commands through the <see cref="IGameSession"/>.
/// A city of another civ shows only its snapshot entry (<see cref="CityScreenModel.Foreign"/>): no query about its insides is ever asked.
/// </summary>
public partial class CityScreen : PanelContainer
{
    private const string Hint = "Click: work or free. Right click: lock.";
    private const int GridMargin = 8;
    private const int CellFontSize = 9;

    private readonly IGameSession session;
    private readonly GameQueries queries;
    private readonly UiTheme theme;
    private readonly Label title = new() { Name = "Title" };
    private readonly Label info = new() { Name = "Info" };
    private readonly VBoxContainer side = new() { Name = "Side" };
    private readonly Control grid = new() { Name = "Grid", ClipContents = true, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
    private readonly Label message = new() { Name = "Message" };
    private readonly Dictionary<(int X, int Y), Button> tileButtons = [];
    private readonly Dictionary<(string Name, bool Add), Button> specialistButtons = [];
    private IReadOnlyList<CityTileCell> cells = [];
    private OptionButton? focusChoice;
    private readonly HBoxContainer middle = new() { Name = "Middle", SizeFlagsVertical = SizeFlags.ExpandFill };
    private readonly Button citizensTab = new() { Name = "ShowCitizens", Text = "Citizens".Tr(), ToggleMode = true };
    private readonly Button productionTab = new() { Name = "ShowProduction", Text = "Production".Tr(), ToggleMode = true };
    private readonly CityProductionView productionView;
    private bool showProduction;
    private int generation;

    public CityScreen(IGameSession gameSession, UiTheme uiTheme)
    {
        session = gameSession;
        queries = new GameQueries(gameSession);
        theme = uiTheme;
        Name = "CityPanel";
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        Theme = uiTheme.Theme;

        var column = new VBoxContainer();
        AddChild(column);
        title.AddThemeFontSizeOverride("font_size", uiTheme.Px(20));
        var head = new HBoxContainer();
        column.AddChild(head);
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        head.AddChild(title);
        citizensTab.Pressed += () => SetProduction(false);
        productionTab.Pressed += () => SetProduction(true);
        head.AddChild(citizensTab);
        head.AddChild(productionTab);
        message.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        message.ClipText = true;
        message.HorizontalAlignment = HorizontalAlignment.Right;
        head.AddChild(message);
        var close = new Button { Name = "CityPanelClose", Text = "Close city".Tr() };
        close.Pressed += Close;
        head.AddChild(close);
        column.AddChild(info);
        column.AddChild(middle);
        productionView = new CityProductionView(Send, SetMessage, uiTheme);
        column.AddChild(productionView);
        side.AddThemeConstantOverride("separation", 2);
        side.CustomMinimumSize = new Vector2(uiTheme.Px(190), 0f);
        middle.AddChild(side);
        middle.AddChild(grid);
        grid.Resized += LayoutGrid;
        SetProduction(false);
    }

    /// <summary>The production half (queue, buildable list, buildings), shown by the Production tab.</summary>
    public CityProductionView Production => productionView;

    public bool ShowingProduction => showProduction;

    /// <summary>Switches between the citizens view (tiles, specialists, focus) and the production view (what the Citizens / Production tabs do).</summary>
    public void SetProduction(bool production)
    {
        showProduction = production;
        middle.Visible = !production;
        productionView.Visible = production;
        citizensTab.SetPressedNoSignal(!production);
        productionTab.SetPressedNoSignal(production);
        SetMessage(production || Panel == null ? "" : Hint);
    }

    public Button? Tab(bool production) => production ? productionTab : citizensTab;

    /// <summary>The city on show (the snapshot entry it was opened with).</summary>
    public CityDto? City { get; private set; }

    /// <summary>The engine's numbers for the city on show; null for a city of another civ (and while loading).</summary>
    public CityPanelResult? Panel { get; private set; }

    public CityTilesResult? Tiles { get; private set; }

    /// <summary>True when the city on show is not the viewer's: only its snapshot entry is displayed.</summary>
    public bool IsForeign { get; private set; }

    public string TitleText => title.Text;

    public string InfoText => info.Text;

    public string MessageText => message.Text;

    public IReadOnlyList<CityTileCell> Cells => cells;

    public IReadOnlyList<string> SideTexts => AllLabels(side).Select(it => it.Text).ToList();

    /// <summary>Raised after the engine accepted a command made here, so the owner refreshes the world (the city is then opened again with the new snapshot entry).</summary>
    public event Action? CommandAccepted;

    public event Action? Closed;

    public Button? TileButton(int x, int y) => tileButtons.GetValueOrDefault((x, y));

    public Button? SpecialistButton(string specialist, bool add) => specialistButtons.GetValueOrDefault((specialist, add));

    public bool HasFocusChoice => focusChoice != null;

    /// <summary>Shows a city of the snapshot: the viewer's own with the engine's full answer, any other from its snapshot entry alone.</summary>
    public async Task OpenAsync(CityDto city)
    {
        int ticket = ++generation;
        City = city;
        IsForeign = city.Own == null || city.Owner != session.ViewerCivId;
        Visible = true;
        if (IsForeign)
        {
            Panel = null;
            Tiles = null;
            ShowForeign(city);
            return;
        }
        var panel = await queries.CityPanel(Id(city));
        var tiles = await queries.CityTiles(Id(city));
        var list = await queries.ConstructionList(Id(city));
        var bar = await queries.TopBar();
        if (ticket != generation) return; // a newer open or a close superseded this answer
        if (panel == null || tiles == null || list == null)
        {
            Panel = null;
            Tiles = null;
            ShowForeign(city);
            SetMessage("The engine would not show this city.");
            return;
        }
        Panel = panel;
        Tiles = tiles;
        Rebuild(panel, tiles);
        productionView.Show(panel, list, bar?.Gold ?? 0, session.Ruleset);
        SetTabs(visible: true);
    }

    public void Close()
    {
        generation++;
        Visible = false;
        City = null;
        Panel = null;
        Tiles = null;
        SetProduction(false);
        Closed?.Invoke();
    }

    /// <summary>Chooses a citizen focus (what the dropdown does).</summary>
    public void ChooseFocus(CityFocus focus)
    {
        if (Panel == null) return;
        Fire(Send(new SetCityFocus { CityId = Panel.CityId, Focus = focus }));
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (Visible && @event is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }

    private static IEnumerable<Label> AllLabels(Node node) =>
        node.GetChildren().SelectMany(it => it is Label label ? [label] : AllLabels(it));

    private void SetTabs(bool visible)
    {
        citizensTab.Visible = visible;
        productionTab.Visible = visible;
    }

    private static string Id(CityDto city) => city.Own!.Id;

    private void ShowForeign(CityDto city)
    {
        ClearSide();
        ClearGrid();
        SetTabs(visible: false);
        SetProduction(false);
        var text = CityScreenModel.Foreign(city);
        title.Text = text.Title.TrText();
        info.Text = "";
        foreach (string line in text.Lines) side.AddChild(new Label { Text = line.TrText() });
        SetMessage("");
    }

    private void Rebuild(CityPanelResult panel, CityTilesResult tiles)
    {
        title.Text = CityScreenModel.Title(panel).TrText();
        info.Text = string.Join("\n", CityScreenModel.InfoLines(panel).Select(it => it.TrText()));
        ClearSide();
        foreach (var stat in panel.TotalStats)
        {
            var label = new Label { Name = "Stat_" + stat.Stat, Text = $"{stat.Stat.ToString().TrText()} {(stat.Value >= 0 ? "+" : "")}{stat.Value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)}", MouseFilter = MouseFilterEnum.Stop };
            label.TooltipText = CityScreenModel.StatBreakdown(panel, stat.Stat).TrText();
            label.AddThemeColorOverride("font_color", ToColour(PlaceholderPalette.ForStat(stat.Stat)));
            side.AddChild(label);
        }
        var specialists = CityScreenModel.Specialists(panel);
        if (specialists.Count > 0) side.AddChild(new Label { Text = "Specialists".Tr() });
        foreach (var row in specialists) side.AddChild(SpecialistLine(panel, row));
        focusChoice = new OptionButton { Name = "Focus", Disabled = panel.IsPuppet };
        var focuses = CityScreenModel.Focuses(panel, religionEnabled: true);
        for (int i = 0; i < focuses.Count; i++)
        {
            focusChoice.AddItem(focuses[i].Label, i);
            if (focuses[i].Current) focusChoice.Selected = i;
        }
        focusChoice.ItemSelected += index => ChooseFocus(focuses[(int)index].Focus);
        side.AddChild(focusChoice);
        var reset = new Button { Name = "ResetCitizens", Text = "Reset citizens".Tr(), Disabled = panel.IsPuppet };
        reset.Pressed += () => Fire(Send(new ReassignPopulation { CityId = panel.CityId, ResetLocked = true }));
        side.AddChild(reset);

        ClearGrid();
        cells = CityScreenModel.Cells(tiles);
        foreach (var cell in cells) grid.AddChild(TileCell(panel, tiles, cell));
        LayoutGrid();
        SetMessage(showProduction ? "" : Hint);
    }

    private Control SpecialistLine(CityPanelResult panel, SpecialistRow row)
    {
        var line = new HBoxContainer { Name = "Specialist_" + row.Name };
        line.AddChild(new Label { Text = $"{row.Name.TrText()} {row.Assigned}/{row.Max}", SizeFlagsHorizontal = SizeFlags.ExpandFill });
        foreach (bool add in new[] { false, true })
        {
            var button = new Button { Name = (add ? "Add_" : "Remove_") + row.Name, Text = add ? "+" : "-", Disabled = add ? !row.CanAdd : !row.CanRemove };
            button.Pressed += () => Fire(Send(CityScreenModel.SpecialistCommand(panel.CityId, row, add)));
            specialistButtons[(row.Name, add)] = button;
            line.AddChild(button);
        }
        return line;
    }

    private Button TileCell(CityPanelResult panel, CityTilesResult tiles, CityTileCell cell)
    {
        string text = string.Join("\n", cell.Label.Split(' ', StringSplitOptions.RemoveEmptyEntries).Chunk(2).Select(it => string.Join(' ', it)));
        var button = new Button { Name = $"Tile_{cell.X}_{cell.Y}", Text = text.TrText(), TooltipText = cell.Tooltip.TrText(), ClipText = true };
        button.AddThemeFontSizeOverride("font_size", theme.Px(CellFontSize));
        var fill = ToColour(PlaceholderPalette.ForCityTile(cell.State));
        foreach (string state in new[] { "normal", "hover", "pressed", "focus", "disabled" })
        {
            var box = new StyleBoxFlat { BgColor = state == "hover" ? fill.Lightened(0.2f) : fill };
            int border = cell.Locked ? 3 : 1;
            box.SetBorderWidthAll(theme.Px(border));
            box.BorderColor = cell.Locked ? ToColour(PlaceholderPalette.LockedOutline) : ToColour(PlaceholderPalette.PanelBorder);
            button.AddThemeStyleboxOverride(state, box);
        }
        button.Pressed += () => OnTileClicked(panel, tiles, cell, lockIt: false);
        button.GuiInput += input =>
        {
            if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right })
            {
                OnTileClicked(panel, tiles, cell, lockIt: true);
                button.AcceptEvent();
            }
        };
        tileButtons[(cell.X, cell.Y)] = button;
        return button;
    }

    private void OnTileClicked(CityPanelResult panel, CityTilesResult tiles, CityTileCell cell, bool lockIt)
    {
        string why;
        var command = lockIt
            ? CityScreenModel.LockCommand(panel.CityId, cell, panel.IsPuppet, out why)
            : CityScreenModel.ClickCommand(panel.CityId, cell, tiles.FreePopulation, panel.IsPuppet, out why);
        if (command == null)
        {
            SetMessage(why);
            return;
        }
        Fire(Send(command));
    }

    private async Task Send(GameCommand command)
    {
        var result = await session.SubmitAsync(command);
        if (!result.Accepted)
        {
            SetMessage("Refused: " + result.Message);
            return;
        }
        CommandAccepted?.Invoke();
    }

    /// <summary>Observes a UI-triggered Task: a transport failure is shown instead of vanishing (IGameSession error contract).</summary>
    private async void Fire(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception e)
        {
            GD.PushError("Session call failed: " + e);
            SetMessage("Connection problem: " + e.Message);
        }
    }

    private void LayoutGrid()
    {
        if (cells.Count == 0) return;
        var (minX, minY, maxX, maxY) = CityScreenModel.Bounds(cells);
        const float cellSide = 1.4f; // tile-radius units; neighbours are at least 1.5 apart, so cells never overlap
        float width = maxX - minX + cellSide;
        float height = maxY - minY + cellSide;
        float scale = MathF.Min((grid.Size.X - GridMargin * 2) / width, (grid.Size.Y - GridMargin * 2) / height);
        if (scale <= 0f) return;
        var origin = new Vector2((grid.Size.X - width * scale) / 2f, (grid.Size.Y - height * scale) / 2f);
        foreach (var cell in cells)
        {
            var button = tileButtons[(cell.X, cell.Y)];
            button.Size = new Vector2(cellSide * scale, cellSide * scale);
            button.Position = origin + new Vector2((cell.Px - minX) * scale, (cell.Py - minY) * scale);
        }
    }

    private void ClearSide()
    {
        foreach (var child in side.GetChildren())
        {
            side.RemoveChild(child); // at once: a queued free would leave the old rows in the layout until the frame ends
            child.QueueFree();
        }
        specialistButtons.Clear();
        focusChoice = null;
    }

    private void ClearGrid()
    {
        foreach (var child in grid.GetChildren())
        {
            grid.RemoveChild(child);
            child.QueueFree();
        }
        tileButtons.Clear();
        cells = [];
    }

    private void SetMessage(string text) => message.Text = text.TrText();

    private static Color ToColour(Rgb rgb) => new(rgb.R / 255f, rgb.G / 255f, rgb.B / 255f);
}
