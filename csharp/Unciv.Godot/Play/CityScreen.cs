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
    private const string DesktopHint = "Click: work or free. Right click: lock.";
    private const string TouchHint = "Tap: work or free. Long press: lock.";

    private static string Hint => DisplayProfile.Current.Touch ? TouchHint : DesktopHint;
    private const int GridMargin = 8;

    private readonly IGameSession session;
    private readonly GameQueries queries;
    private readonly UiTheme theme;
    private readonly ScreenFrame frame;
    private readonly Label info = new() { Name = "Info", ThemeTypeVariation = UiVariants.LabelSecondary, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, SizeFlagsHorizontal = SizeFlags.ExpandFill, VerticalAlignment = VerticalAlignment.Center };
    private readonly VBoxContainer side = new() { Name = "Side" };
    private readonly Control grid = new() { Name = "Grid", ClipContents = true, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
    private readonly Label message = new() { Name = "Message", ThemeTypeVariation = UiVariants.LabelSmall };
    private readonly Dictionary<(int X, int Y), CityTileButton> tileButtons = [];
    private readonly Dictionary<(string Name, bool Add), Button> specialistButtons = [];
    private readonly Dictionary<string, StatChip> statChips = [];
    private readonly PanelContainer meters = new() { Name = "Meters", ThemeTypeVariation = UiVariants.PanelCard };
    private IReadOnlyList<CityTileCell> cells = [];
    private OptionButton? focusChoice;
    private readonly HBoxContainer middle = new() { Name = "Middle", SizeFlagsVertical = SizeFlags.ExpandFill };
    private readonly Segmented tabs = new(["Citizens", "Production"], "Citizens") { Name = "Tabs" };
    private readonly CityProductionView productionView;
    private readonly CityRenameView renameView;
    private readonly CityConfirmView confirmView;
    private readonly Button renameButton = new() { Name = "RenameCity", Text = "Rename".Tr(), Visible = false, ThemeTypeVariation = UiVariants.ButtonGhost };
    private bool showProduction;
    private int generation;
    private string infoText = "";
    private ConstructionListResult? lastList;
    private int lastGold;

    public CityScreen(IGameSession gameSession, UiTheme uiTheme)
    {
        session = gameSession;
        queries = new GameQueries(gameSession);
        theme = uiTheme;
        Name = "CityPanel";
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        Theme = uiTheme.Theme;

        frame = new ScreenFrame(uiTheme, "", closeLabel: "Close city");
        frame.CloseRequested += Close;
        AddChild(frame);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", uiTheme.Px(UiTokens.S2));
        frame.SetContent(column);
        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", uiTheme.Px(UiTokens.S2));
        tabs.ButtonFor("Citizens")!.Name = "ShowCitizens";
        tabs.ButtonFor("Production")!.Name = "ShowProduction";
        tabs.Chosen += option => SetProduction(option == "Production");
        top.AddChild(tabs);
        top.AddChild(info);
        renameButton.Pressed += OpenRename;
        top.AddChild(renameButton);
        column.AddChild(top);
        column.AddChild(meters);
        column.AddChild(middle);
        productionView = new CityProductionView(Send, SetMessage, action => confirmView!.Open(action), uiTheme);
        column.AddChild(productionView);
        message.ClipText = true;
        column.AddChild(message);
        side.AddThemeConstantOverride("separation", uiTheme.Px(UiTokens.S2));
        var sideScroll = new ScrollContainer { Name = "SideScroll", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(uiTheme.Px(200), 0f) };
        side.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        sideScroll.AddChild(side);
        middle.AddThemeConstantOverride("separation", uiTheme.Px(UiTokens.S3));
        middle.AddChild(sideScroll);
        middle.AddChild(grid);
        grid.Resized += LayoutGrid;
        renameView = new CityRenameView(command => Fire(Send(command)), uiTheme);
        AddChild(renameView); // a PanelContainer lays every child over the whole panel: the popup covers the screen
        confirmView = new CityConfirmView(command => Fire(Send(command)), uiTheme);
        AddChild(confirmView);
        SetProduction(false);
    }

    public override void _EnterTree() => theme.LayoutChanged += OnLayoutChanged;

    public override void _ExitTree() => theme.LayoutChanged -= OnLayoutChanged;

    /// <summary>Sizes are fixed when a view is built, so a change of scale or breakpoint builds the open city's content again from the answers it has.</summary>
    private void OnLayoutChanged()
    {
        if (Panel != null && Tiles != null && lastList != null && Visible) ShowAnswers(Panel, Tiles, lastList, lastGold);
    }

    /// <summary>The "Confirm" popup of a destructive city command (Raze).</summary>
    public CityConfirmView ConfirmPopup => confirmView;

    /// <summary>The "Rename city" popup (C048).</summary>
    public CityRenameView RenamePopup => renameView;

    /// <summary>The header button that opens the rename popup; only an own city can be renamed.</summary>
    public Button RenameButton => renameButton;

    /// <summary>Opens the rename popup for the city on show (what the Rename button does); nothing for a foreign city or while loading.</summary>
    public void OpenRename()
    {
        if (Panel == null || IsForeign) return;
        renameView.Open(Panel.CityId, Panel.Name);
    }

    /// <summary>The production half (queue, buildable list, buildings), shown by the Production tab.</summary>
    public CityProductionView Production => productionView;

    public bool ShowingProduction => showProduction;

    /// <summary>Switches between the citizens view (tiles, specialists, focus) and the production view (what the Citizens / Production tabs do).</summary>
    public void SetProduction(bool production)
    {
        showProduction = production;
        middle.Visible = !production;
        meters.Visible = !production && Panel != null;
        productionView.Visible = production;
        tabs.ButtonFor("Citizens")!.SetPressedNoSignal(!production);
        tabs.ButtonFor("Production")!.SetPressedNoSignal(production);
        SetMessage(production || Panel == null ? "" : Hint);
    }

    public Button? Tab(bool production) => tabs.ButtonFor(production ? "Production" : "Citizens");

    /// <summary>The city on show (the snapshot entry it was opened with).</summary>
    public CityDto? City { get; private set; }

    /// <summary>The engine's numbers for the city on show; null for a city of another civ (and while loading).</summary>
    public CityPanelResult? Panel { get; private set; }

    public CityTilesResult? Tiles { get; private set; }

    /// <summary>True when the city on show is not the viewer's: only its snapshot entry is displayed.</summary>
    public bool IsForeign { get; private set; }

    public string TitleText => frame.TitleText;

    public string InfoText => infoText;

    public string MessageText => message.Text;

    public IReadOnlyList<CityTileCell> Cells => cells;

    /// <summary>C056: the hover tooltip of a stat in the stat block (its sources and total), null when the city has no such stat.</summary>
    public string? StatTooltip(string stat) => statChips.GetValueOrDefault(stat)?.TooltipText;

    public IReadOnlyList<string> SideTexts => AllLabels(side).Select(it => it.Text).ToList();

    /// <summary>Raised after the engine accepted a command made here, so the owner refreshes the world (the city is then opened again with the new snapshot entry).</summary>
    public event Action? CommandAccepted;

    /// <summary>C054: raised with each accepted command (the tutorial tasks learn from what the player did).</summary>
    public event Action<GameCommand>? CommandSent;

    public event Action? Closed;

    public CityTileButton? TileButton(int x, int y) => tileButtons.GetValueOrDefault((x, y));

    public Button? SpecialistButton(string specialist, bool add) => specialistButtons.GetValueOrDefault((specialist, add));

    public bool HasFocusChoice => focusChoice != null;

    /// <summary>Shows a city of the snapshot: the viewer's own with the engine's full answer, any other from its snapshot entry alone.</summary>
    public async Task OpenAsync(CityDto city)
    {
        int ticket = ++generation;
        City = city;
        IsForeign = city.Own == null || city.Owner != session.ViewerCivId;
        Visible = true;
        renameView.Cancel();
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
        renameButton.Visible = true;
        lastList = list;
        lastGold = bar?.Gold ?? 0;
        ShowAnswers(panel, tiles, list, lastGold);
        SetTabs(visible: true);
    }

    private void ShowAnswers(CityPanelResult panel, CityTilesResult tiles, ConstructionListResult list, int gold)
    {
        Rebuild(panel, tiles);
        productionView.Show(panel, list, gold, session.Ruleset);
    }

    public void Close()
    {
        generation++;
        confirmView.Cancel();
        productionView.ClearPendingFocus();
        Visible = false;
        renameView.Cancel();
        renameButton.Visible = false;
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
        tabs.Visible = visible;
    }

    private static string Id(CityDto city) => city.Own!.Id;

    private void ShowForeign(CityDto city)
    {
        renameButton.Visible = false;
        ClearSide();
        ClearGrid();
        SetTabs(visible: false);
        SetProduction(false);
        var text = CityScreenModel.Foreign(city);
        frame.TitleLabel.Text = text.Title.TrText();
        info.Text = "";
        infoText = "";
        meters.Visible = false;
        foreach (string line in text.Lines) side.AddChild(new Label { Text = line.TrText() });
        SetMessage("");
    }

    private void Rebuild(CityPanelResult panel, CityTilesResult tiles)
    {
        frame.TitleLabel.Text = CityScreenModel.Title(panel).TrText();
        var infoLines = CityScreenModel.InfoLines(panel);
        infoText = string.Join("\n", infoLines.Select(it => it.TrText()));
        info.Text = infoLines[2].TrText();
        ShowMeters(panel);
        ClearSide();
        var chips = new HFlowContainer { Name = "Stats" };
        chips.AddThemeConstantOverride("h_separation", theme.Px(UiTokens.S2));
        chips.AddThemeConstantOverride("v_separation", theme.Px(UiTokens.S1));
        foreach (var stat in panel.TotalStats)
        {
            string value = $"{(stat.Value >= 0 ? "+" : "")}{stat.Value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)}";
            var chip = new StatChip(theme, stat.Stat, value) { Name = "Stat_" + stat.Stat, MouseFilter = MouseFilterEnum.Stop };
            chip.TooltipText = TooltipModel.ToPlain(TooltipModel.StatBreakdown(stat.Stat, panel.StatSources, stat.Value), text => text.Tr());
            statChips[stat.Stat] = chip;
            chips.AddChild(chip);
        }
        side.AddChild(chips);
        var specialists = CityScreenModel.Specialists(panel);
        if (specialists.Count > 0) side.AddChild(new SectionHeader("Specialists"));
        foreach (var row in specialists) side.AddChild(SpecialistLine(panel, row));
        focusChoice = new OptionButton { Name = "Focus", Disabled = panel.IsPuppet };
        var focuses = CityScreenModel.Focuses(panel, religionEnabled: true);
        for (int i = 0; i < focuses.Count; i++)
        {
            focusChoice.AddItem(focuses[i].Label, i);
            if (focuses[i].Current) focusChoice.Selected = i;
        }
        focusChoice.ItemSelected += index => ChooseFocus(focuses[(int)index].Focus);
        side.AddChild(new SectionHeader("Citizen focus"));
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

    private void ShowMeters(CityPanelResult panel)
    {
        foreach (var child in meters.GetChildren())
        {
            meters.RemoveChild(child);
            child.QueueFree();
        }
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", theme.Px(UiTokens.S4));
        foreach (var meter in new[] { CityScreenModel.FoodMeter(panel), CityScreenModel.BorderMeter(panel) })
        {
            var column = new VBoxContainer { Name = "Meter_" + meter.Stat, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            column.AddThemeConstantOverride("separation", 0);
            column.AddChild(new Label { Text = meter.Caption.TrText(), ThemeTypeVariation = UiVariants.LabelSmall, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis });
            column.AddChild(new StatBar(theme, meter.Stat, meter.Value, meter.Max));
            row.AddChild(column);
        }
        meters.AddChild(row);
        meters.Visible = !showProduction;
    }

    private Control SpecialistLine(CityPanelResult panel, SpecialistRow row)
    {
        var line = new HBoxContainer { Name = "Specialist_" + row.Name };
        line.AddThemeConstantOverride("separation", theme.Px(UiTokens.S1));
        line.AddChild(new Label { Text = $"{row.Name.TrText()} {row.Assigned}/{row.Max}", SizeFlagsHorizontal = SizeFlags.ExpandFill });
        foreach (bool add in new[] { false, true })
        {
            var button = new Button { Name = (add ? "Add_" : "Remove_") + row.Name, Text = add ? "+" : "-", Disabled = add ? !row.CanAdd : !row.CanRemove, ThemeTypeVariation = UiVariants.ButtonChip };
            button.Pressed += () => Fire(Send(CityScreenModel.SpecialistCommand(panel.CityId, row, add)));
            specialistButtons[(row.Name, add)] = button;
            line.AddChild(button);
        }
        return line;
    }

    private CityTileButton TileCell(CityPanelResult panel, CityTilesResult tiles, CityTileCell cell)
    {
        var button = new CityTileButton(theme, cell);
        // C100: a long press locks or unlocks the tile (the touch stand-in for the right click); the release that ends it is not also a click
        var longPress = new LongPress(button, () => OnTileClicked(panel, tiles, cell, lockIt: true));
        button.Pressed += () =>
        {
            if (!longPress.TakeFired()) OnTileClicked(panel, tiles, cell, lockIt: false);
        };
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

    /// <summary>Submits a command; true when the engine accepted it (a refusal is shown in the message line).</summary>
    private async Task<bool> Send(GameCommand command)
    {
        var result = await session.SubmitAsync(command);
        if (!result.Accepted)
        {
            SetMessage("Refused: " + result.Message);
            return false;
        }
        CommandSent?.Invoke(command);
        CommandAccepted?.Invoke();
        return true;
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
        const float radius = CityScreenModel.HexCircumradius; // tile-radius units: a hexagon is 2 radii wide and 0.866 * 2 high
        float width = maxX - minX + 2f * radius;
        float height = maxY - minY + CityTileButton.HeightFor(2f * radius);
        float scale = MathF.Min((grid.Size.X - GridMargin * 2) / width, (grid.Size.Y - GridMargin * 2) / height);
        if (scale <= 0f) return;
        var origin = new Vector2((grid.Size.X - width * scale) / 2f, (grid.Size.Y - height * scale) / 2f);
        var cellSize = new Vector2(2f * radius * scale, CityTileButton.HeightFor(2f * radius * scale));
        foreach (var cell in cells)
        {
            var button = tileButtons[(cell.X, cell.Y)];
            button.Size = cellSize;
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
        statChips.Clear();
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
}
