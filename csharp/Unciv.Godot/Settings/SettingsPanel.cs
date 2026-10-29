using System.Globalization;
using Godot;
using Unciv.Assets;
using Unciv.Assets.Localization;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Localization;
using Unciv.GodotClient.Ui;
using Unciv.Models.Translations;

namespace Unciv.GodotClient.Settings;

/// <summary>
/// C043 / C075: the settings. Vertical tabs on the left (Display, Gameplay, Map, Sound, Keys, Language when there is a localization, Advanced), the selected page on the right
/// (each page scrolls on its own). Every control applies and saves through <see cref="ClientSettingsService"/> the moment it changes (the App applies volume, window mode and UI
/// scale to the window, the game screen follows the rest): switches for the yes/no options, sliders with a readout, a <see cref="Stepper"/> for the UI scale, a table of the
/// key bindings. A rebound key is checked by <see cref="Hotkeys"/>: an unusable key is refused, a key another action holds is swapped (both said inline on the Keys page).
/// The page's own text follows a language change.
/// </summary>
public partial class SettingsPanel : HBoxContainer
{
    private static readonly (HotkeyAction Action, string Label)[] ActionLabels =
    [
        (HotkeyAction.EndTurn, "End turn"),
        (HotkeyAction.EndTurnAlternate, "End turn (alternate)"),
        (HotkeyAction.NextUnit, "Next unit"),
        (HotkeyAction.OpenTech, "Technology"),
        (HotkeyAction.OpenPolicies, "Policies"),
        (HotkeyAction.OpenDiplomacy, "Diplomacy"),
        (HotkeyAction.OpenOverview, "Empire overview"),
        (HotkeyAction.OverviewCities, "Overview: Cities"),
        (HotkeyAction.OverviewUnits, "Overview: Units"),
        (HotkeyAction.OverviewPolitics, "Overview: Politics"),
        (HotkeyAction.OverviewStats, "Overview: Stats"),
        (HotkeyAction.OverviewResources, "Overview: Resources"),
        (HotkeyAction.OpenVictory, "Victory status"),
        (HotkeyAction.SaveGame, "Save game"),
        (HotkeyAction.LoadGame, "Load game"),
        (HotkeyAction.OpenOptions, "Options"),
        (HotkeyAction.ViewCapital, "View capital city"),
        (HotkeyAction.ToggleResources, "Toggle resource display"),
        (HotkeyAction.ToggleYields, "Toggle yield display"),
        (HotkeyAction.PlaceMarker, "Map marker"),
        (HotkeyAction.PingMap, "Ping allies"),
        (HotkeyAction.PanUp, "Pan up"),
        (HotkeyAction.PanDown, "Pan down"),
        (HotkeyAction.PanLeft, "Pan left"),
        (HotkeyAction.PanRight, "Pan right"),
        (HotkeyAction.OpenConsole, "Developer Console"),
        (HotkeyAction.OpenPedia, "Civilopedia"),
        (HotkeyAction.Automate, "Automate"),
        (HotkeyAction.StopAutomation, "Stop automation"),
        (HotkeyAction.StopMovement, "Stop movement"),
        (HotkeyAction.Sleep, "Sleep"),
        (HotkeyAction.SleepUntilHealed, "Sleep until healed"),
        (HotkeyAction.Fortify, "Fortify"),
        (HotkeyAction.FortifyUntilHealed, "Fortify until healed"),
        (HotkeyAction.Explore, "Explore"),
        (HotkeyAction.StopExploration, "Stop exploration"),
        (HotkeyAction.Upgrade, "Upgrade"),
        (HotkeyAction.Transform, "Transform"),
        (HotkeyAction.Pillage, "Pillage"),
        (HotkeyAction.Paradrop, "Paradrop"),
        (HotkeyAction.AirSweep, "Air Sweep"),
        (HotkeyAction.SetUp, "Set up"),
        (HotkeyAction.FoundCity, "Found city"),
        (HotkeyAction.Repair, "Repair"),
        (HotkeyAction.CreateImprovement, "Construct improvement"),
        (HotkeyAction.HurryResearch, "Hurry Research"),
        (HotkeyAction.HurryWonder, "Hurry Wonder"),
        (HotkeyAction.HurryBuilding, "Hurry Construction"),
        (HotkeyAction.ConductTradeMission, "Conduct Trade Mission"),
        (HotkeyAction.FoundReligion, "Found a Religion"),
        (HotkeyAction.TriggerUnique, "Trigger unique"),
        (HotkeyAction.SpreadReligion, "Spread Religion"),
        (HotkeyAction.RemoveHeresy, "Remove Heresy"),
        (HotkeyAction.EnhanceReligion, "Enhance a Religion"),
        (HotkeyAction.Skip, "Skip"),
        (HotkeyAction.AddInCapital, "Add in capital"),
        (HotkeyAction.DisbandUnit, "Disband unit"),
    ];

    private readonly ClientSettingsService service;
    private readonly UiTheme theme;
    private readonly VBoxContainer tabColumn = new() { Name = "SettingsTabs" };
    private readonly ButtonGroup tabGroup = new();
    private readonly List<(ListRow Tab, ScrollContainer Page)> tabs = [];
    private readonly List<Action> refreshers = [];
    private readonly Dictionary<HotkeyAction, Button> keyButtons = [];
    private readonly Label messageLabel = new() { Name = "OptionsMessage", AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly HSlider volume = new() { Name = "VolumeSlider", MinValue = 0, MaxValue = 1, Step = 0.05 };
    private readonly HSlider scroll = new() { Name = "ScrollSpeedSlider", MinValue = OptionsModel.MinScrollSpeed, MaxValue = OptionsModel.MaxScrollSpeed, Step = 0.25 };
    private readonly HSlider autoPlayTurns = new() { Name = "AutoPlayTurnsSlider", MinValue = AutoPlaySettings.MinTurns, MaxValue = AutoPlaySettings.MaxTurnsLimit, Step = 1 };
    private readonly OptionButton windowMode = new() { Name = "WindowModePicker" };
    private readonly OptionButton graphicsPicker = new() { Name = "GraphicsQualityPicker" };
    private readonly Label graphicsNote = new() { Name = "GraphicsNote", AutowrapMode = TextServer.AutowrapMode.WordSmart, Visible = false };
    private readonly Button graphicsRestart = new() { Name = "GraphicsRestartButton", Visible = false, SizeFlagsHorizontal = SizeFlags.ShrinkBegin };
    private readonly OptionButton packPicker = new() { Name = "AssetPackPicker" };
    private readonly Unciv.Models.Ruleset.Ruleset? tutorialRuleset;
    private readonly VBoxContainer tutorialList = new() { Name = "TutorialList", Visible = false };
    private readonly IReadOnlyList<string> packNames;
    private readonly Label nameLabel = new() { ThemeTypeVariation = UiVariants.LabelSecondary };
    private DataTable? keyTable;
    private Button? tutorialsButton;
    private Stepper scaleStepper = null!; // JUSTIFY: C075 assigned by BuildDisplay, which the constructor runs
    private ScrollContainer? currentPage;
    private bool refreshing;

    public SettingsPanel(ClientSettingsService service, UiTheme theme, ClientLocalization? localization = null, IEnumerable<string>? packDirectories = null, Unciv.Models.Ruleset.Ruleset? tutorialRuleset = null)
    {
        this.service = service;
        this.theme = theme;
        this.tutorialRuleset = tutorialRuleset;
        Name = "SettingsPanel";
        AddThemeConstantOverride("separation", theme.Px(UiTokens.S3));
        tabColumn.CustomMinimumSize = new Vector2(theme.Px(theme.Breakpoint == UiBreakpoint.Compact ? 104 : 148), 0);
        tabColumn.AddThemeConstantOverride("separation", theme.Px(UiTokens.S1));
        AddChild(tabColumn);

        BuildDisplay();
        BuildGameplay();
        BuildMap();
        BuildSound();
        BuildKeys();
        if (localization != null) BuildLanguage(localization);
        packNames = OptionsModel.PackNames(packDirectories ?? GodotAssetFiles.ListDirectories("assets/packs"));
        BuildAdvanced();
        SelectTab(0);

        service.Changed += OnChanged;
        Refresh();
    }

    private void OnChanged(ClientSettings _) => Refresh();

    public override void _ExitTree() => service.Changed -= OnChanged;

    /// <summary>The action waiting for its new key, or null.</summary>
    public HotkeyAction? Listening { get; private set; }

    public string Message => messageLabel.Text;

    public Button KeyButton(HotkeyAction action) => keyButtons[action];

    public HSlider VolumeSlider => volume;

    public HSlider ScrollSlider => scroll;

    public HSlider AutoPlayTurnsSlider => autoPlayTurns;

    public OptionButton WindowModePicker => windowMode;

    public OptionButton PackPicker => packPicker;

    /// <summary>C097: the graphics quality picker (Low, High, Ultra), the note under it and the restart button that shows when the choice needs another renderer.</summary>
    public OptionButton GraphicsPicker => graphicsPicker;

    public Label GraphicsNote => graphicsNote;

    public Button GraphicsRestartButton => graphicsRestart;

    /// <summary>The UI scale stepper (percent).</summary>
    public Stepper ScaleStepper => scaleStepper;

    /// <summary>The Tutorials expander, its list and the number of tutorials listed (for scenarios and tests).</summary>
    public Button? TutorialsButton => tutorialsButton;

    public int TutorialCount => tutorialList.GetChildCount() / 2;

    /// <summary>The untranslated names of the tabs in order.</summary>
    public IReadOnlyList<string> TabNames => tabs.Select(it => it.Tab.Title).ToList();

    public int SelectedTab { get; private set; } = -1;

    /// <summary>The scrolling page of the selected tab.</summary>
    public ScrollContainer CurrentPage => currentPage ?? throw new InvalidOperationException("no tab");

    public void SelectTab(int index)
    {
        if (index < 0 || index >= tabs.Count) return;
        SelectedTab = index;
        for (int i = 0; i < tabs.Count; i++)
        {
            tabs[i].Page.Visible = i == index;
            tabs[i].Tab.ButtonPressed = i == index;
        }
        currentPage = tabs[index].Page;
    }

    /// <summary>Selects the tab that holds <paramref name="control"/> and scrolls it into view (for scenarios and tests).</summary>
    public void Reveal(Control control)
    {
        for (var node = control.GetParent(); node != null; node = node.GetParent())
        {
            int index = tabs.FindIndex(it => it.Page == node);
            if (index < 0) continue;
            SelectTab(index);
            tabs[index].Page.EnsureControlVisible(control);
            return;
        }
    }

    /// <summary>The next key press (not Escape, which cancels) becomes the key of <paramref name="action"/>.</summary>
    public void BeginRebind(HotkeyAction action)
    {
        Listening = action;
        messageLabel.Text = "Press the new key, or Escape to cancel".Tr();
        Refresh();
    }

    /// <summary>Binds the listening action to a key text; false when nothing was listening or the key is not usable (the old binding stays).</summary>
    public bool TryBind(string? keyText)
    {
        if (Listening is not { } action) return false;
        var bound = Hotkeys.Rebind(service.Current.KeyBindings, action, keyText, out var swapped);
        if (bound == null)
        {
            messageLabel.Text = "That key cannot be used".Tr();
            return false;
        }
        Listening = null;
        service.Update(s => s with { KeyBindings = bound });
        messageLabel.Text = swapped is { } other
            ? "{0} and {1} swapped keys".Tr().Replace("{0}", LabelOf(action).Tr(), StringComparison.Ordinal).Replace("{1}", LabelOf(other).Tr(), StringComparison.Ordinal)
            : "";
        return true;
    }

    public void ResetKeys()
    {
        Listening = null;
        messageLabel.Text = "";
        service.Update(s => s with { KeyBindings = Hotkeys.ResetAll() });
    }

    public override void _Input(InputEvent @event)
    {
        if (Listening == null || @event is not InputEventKey { Pressed: true, Echo: false } key) return;
        if (key.Keycode == Key.Escape)
        {
            Listening = null;
            messageLabel.Text = "";
            Refresh();
        }
        else if (HotkeyInput.TextOf(key) is { } text) TryBind(text);
        else if (key.Keycode is Key.Ctrl or Key.Shift or Key.Alt or Key.Meta) return; // a modifier alone: wait for the real key
        else messageLabel.Text = "That key cannot be used".Tr();
        GetViewport().SetInputAsHandled();
    }

    // Pages

    private VBoxContainer AddPage(string title)
    {
        var tab = new ListRow(theme, title, group: tabGroup);
        int index = tabs.Count;
        tab.Pressed += () => SelectTab(index);
        tabColumn.AddChild(tab);
        var scroller = new ScrollContainer { Name = "Page_" + title, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, Visible = false };
        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", theme.Px(UiTokens.S2));
        scroller.AddChild(column);
        AddChild(scroller);
        tabs.Add((tab, scroller));
        return column;
    }

    private void BuildDisplay()
    {
        var page = AddPage("Display");
        page.AddChild(nameLabel);
        scaleStepper = new Stepper(theme, (int)(OptionsModel.MinUiScale * 100), (int)(OptionsModel.MaxUiScale * 100), 100, (int)(OptionsModel.UiScaleStep * 100), "Smaller UI", "Bigger UI", v => v.ToString(CultureInfo.InvariantCulture) + "%") { Name = "UiScaleStepper" };
        scaleStepper.Changed += v =>
        {
            if (!refreshing) service.Update(s => s with { UiScale = v / 100f });
        };
        AddRow(page, "UI scale", scaleStepper);
        foreach (var mode in OptionsModel.WindowModes) windowMode.AddItem(mode.Tr());
        windowMode.ItemSelected += index =>
        {
            if (!refreshing) service.Update(s => s with { WindowMode = OptionsModel.WindowModes[(int)index] });
        };
        AddRow(page, "Window mode", windowMode);
        foreach (var level in Enum.GetValues<GraphicsQuality>()) graphicsPicker.AddItem(level.ToString().Tr());
        graphicsPicker.ItemSelected += index =>
        {
            if (refreshing) return;
            var level = (GraphicsQuality)(int)index;
            // picking Ultra is a fresh try: it forgets an earlier failure (one attempt per choice, never a loop: see GraphicsModel.Startup)
            service.Update(s => s with { GraphicsLevel = level.ToString(), UltraFailed = level == GraphicsQuality.Ultra ? false : s.UltraFailed, RendererPending = "" });
        };
        AddRow(page, "Graphics quality", graphicsPicker);
        graphicsNote.AddThemeColorOverride("font_color", ProceduralAssets.ToColor(UiTokens.Warning));
        page.AddChild(graphicsNote);
        graphicsRestart.Pressed += () =>
        {
            var current = service.Current;
            // the same decision the start makes (and that showed this button), so the two never disagree
            if (GraphicsModel.Startup(current.Quality, current.UltraFailed, "", GraphicsRuntime.Renderer, GraphicsRuntime.Adapter).RestartInto is not { } wanted) return;
            if (!service.Update(s => s with { RendererPending = wanted })) return; // the marker is the loop guard: no restart without it on disk
            if (!GraphicsRuntime.Restart(wanted)) service.Update(s => s with { RendererPending = "" });
        };
        refreshers.Add(() => graphicsRestart.Text = "Restart to apply".Tr());
        page.AddChild(graphicsRestart);
    }

    private void BuildGameplay()
    {
        var page = AddPage("Gameplay");
        AddToggle(page, "Auto-assign city production", s => s.Player.AutoAssignCityProduction, (s, v) => s with { Player = s.Player with { AutoAssignCityProduction = v } });
        AddToggle(page, "Automated units move on turn start", s => s.Player.AutomatedUnitsMoveOnTurnStart, (s, v) => s with { Player = s.Player with { AutomatedUnitsMoveOnTurnStart = v } });
        AddToggle(page, "Cities auto bombard at end of turn", s => s.Player.CitiesAutoBombardAtEndOfTurn, (s, v) => s with { Player = s.Player with { CitiesAutoBombardAtEndOfTurn = v } });
        AddToggle(page, "Show tutorials", s => s.ShowTutorials, (s, v) => s with { ShowTutorials = v });
        page.AddChild(Heading("AutoPlay"));
        AddToggle(page, "Show AutoPlay button", s => s.AutoPlay.ShowButton, (s, v) => s with { AutoPlay = s.AutoPlay with { ShowButton = v } });
        AddToggle(page, "AutoPlay until victory", s => s.AutoPlay.UntilEnd, (s, v) => s with { AutoPlay = s.AutoPlay with { UntilEnd = v } });
        AddSlider(page, "Multi-turn AutoPlay amount", autoPlayTurns, (s, v) => s with { AutoPlay = s.AutoPlay with { MaxTurns = (int)Math.Round(v, MidpointRounding.AwayFromZero) } }, s => s.AutoPlay.MaxTurns, v => ((int)Math.Round(v, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture));
        AddToggle(page, "Full AutoPlay AI", s => s.AutoPlay.FullAutoPlayAI, (s, v) => s with { AutoPlay = s.AutoPlay with { FullAutoPlayAI = v } });
    }

    private void BuildMap()
    {
        var page = AddPage("Map");
        AddSlider(page, "Scroll speed", scroll, (s, v) => s with { ScrollSpeed = v }, s => s.ScrollSpeed, v => v.ToString("0.00", CultureInfo.InvariantCulture));
        AddToggle(page, "Scroll the map at the window edge", s => s.EdgePan, (s, v) => s with { EdgePan = v });
        AddToggle(page, "Show resources on the map", s => s.ShowResources, (s, v) => s with { ShowResources = v });
        AddToggle(page, "Show tile yields", s => s.ShowYields, (s, v) => s with { ShowYields = v });
    }

    private void BuildSound()
    {
        var page = AddPage("Sound");
        AddSlider(page, "Sound volume", volume, (s, v) => s with { SoundVolume = v }, s => s.SoundVolume, v => ((int)Math.Round(v * 100, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture) + "%");
    }

    private void BuildKeys()
    {
        var page = AddPage("Keys");
        keyTable = new DataTable([new DataColumn("Action")], actions: true) { Name = "KeyTable" };
        keyTable.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        page.AddChild(keyTable);
        var reset = new Button { Name = "ResetKeys", SizeFlagsHorizontal = SizeFlags.ShrinkBegin };
        refreshers.Add(() => reset.Text = "Reset keys".Tr());
        reset.Pressed += ResetKeys;
        page.AddChild(reset);
        messageLabel.AddThemeColorOverride("font_color", ProceduralAssets.ToColor(UiTokens.Warning));
        page.AddChild(messageLabel);
        string shownLanguage = "";
        refreshers.Add(() =>
        {
            // the table is rebuilt only when the action names changed (a language change): the key buttons it holds are the ones scenarios and the player click
            string language = service.Current.Language;
            if (language == shownLanguage) return;
            shownLanguage = language;
            keyButtons.Clear();
            keyTable.SetRows(ActionLabels.Select(it => (IReadOnlyList<string>)[it.Label.Tr()]).ToList(), KeyCell);
        });
    }

    private Control KeyCell(int row)
    {
        var action = ActionLabels[row].Action;
        var box = new HBoxContainer();
        box.AddThemeConstantOverride("separation", theme.Px(UiTokens.S2));
        var chip = new Button { Name = "Key_" + action, ThemeTypeVariation = UiVariants.ButtonChip, CustomMinimumSize = new Vector2(theme.Px(96), 0) };
        chip.Pressed += () => BeginRebind(action);
        keyButtons[action] = chip;
        var rebind = new Button { Name = "Rebind_" + action, Text = "Rebind".Tr(), ThemeTypeVariation = UiVariants.ButtonGhost };
        rebind.Pressed += () => BeginRebind(action);
        box.AddChild(chip);
        box.AddChild(rebind);
        return box;
    }

    private void BuildLanguage(ClientLocalization localization)
    {
        var page = AddPage("Language");
        AddRow(page, "Language", new LanguagePicker(service, localization));
    }

    private void BuildAdvanced()
    {
        var page = AddPage("Advanced");
        foreach (var pack in packNames) packPicker.AddItem(pack);
        packPicker.ItemSelected += index =>
        {
            if (!refreshing) service.Update(s => s with { AssetPack = packNames[(int)index] });
        };
        AddRow(page, "Asset pack (applies to the next game)", packPicker);
        if (tutorialRuleset == null) return;
        tutorialsButton = new Button { Name = "TutorialsButton", ToggleMode = true };
        tutorialsButton.Toggled += on => tutorialList.Visible = on;
        refreshers.Add(() =>
        {
            tutorialsButton.Text = "Tutorials".Tr();
            RebuildTutorialList();
        });
        var reset = new Button { Name = "ResetTutorials" };
        refreshers.Add(() => reset.Text = "Reset tutorials".Tr());
        reset.Pressed += () => service.Update(s => s with { TutorialsSeen = [] });
        var row = new HBoxContainer();
        row.AddChild(tutorialsButton);
        row.AddChild(reset);
        page.AddChild(row);
        page.AddChild(tutorialList);
    }

    private void RebuildTutorialList()
    {
        foreach (var child in tutorialList.GetChildren())
        {
            tutorialList.RemoveChild(child);
            child.QueueFree();
        }
        foreach (var entry in TutorialModel.List(tutorialRuleset!, service.Current))
        {
            var text = new Label { Visible = false, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(theme.Px(260), 0), Text = entry.Text.TrText() };
            var title = entry.Name.Tr();
            var button = new Button { Name = "Tutorial_" + entry.Name, Text = entry.Seen ? title + " *" : title, Alignment = HorizontalAlignment.Left };
            button.Pressed += () => text.Visible = !text.Visible;
            tutorialList.AddChild(button);
            tutorialList.AddChild(text);
        }
    }

    private static string LabelOf(HotkeyAction action) => ActionLabels.First(it => it.Action == action).Label;

    private Label Heading(string key)
    {
        var label = new Label { ThemeTypeVariation = UiVariants.LabelOverline };
        refreshers.Add(() => label.Text = key.Tr());
        return label;
    }

    private void AddRow(Container page, string key, Control control)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", theme.Px(UiTokens.S3));
        var label = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1f, CustomMinimumSize = new Vector2(theme.Px(110), 0), AutowrapMode = TextServer.AutowrapMode.WordSmart, VerticalAlignment = VerticalAlignment.Center };
        refreshers.Add(() => label.Text = key.Tr());
        row.AddChild(label);
        control.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
        row.AddChild(control);
        page.AddChild(row);
    }

    private void AddSlider(Container page, string key, HSlider slider, Func<ClientSettings, float, ClientSettings> write, Func<ClientSettings, float> read, Func<double, string> readout)
    {
        var shown = new Label { ThemeTypeVariation = UiVariants.LabelNumber, HorizontalAlignment = HorizontalAlignment.Right, CustomMinimumSize = new Vector2(theme.Px(UiTokens.S8), 0), VerticalAlignment = VerticalAlignment.Center };
        slider.CustomMinimumSize = new Vector2(theme.Px(theme.Breakpoint == UiBreakpoint.Compact ? 100 : 160), 0);
        slider.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        slider.ValueChanged += value =>
        {
            shown.Text = readout(value);
            if (!refreshing) service.Update(s => write(s, (float)value));
        };
        refreshers.Add(() =>
        {
            slider.Value = read(service.Current);
            shown.Text = readout(slider.Value);
        });
        var box = new HBoxContainer();
        box.AddThemeConstantOverride("separation", theme.Px(UiTokens.S2));
        box.AddChild(slider);
        box.AddChild(shown);
        AddRow(page, key, box);
    }

    private void AddToggle(Container page, string key, Func<ClientSettings, bool> read, Func<ClientSettings, bool, ClientSettings> write)
    {
        var box = new CheckButton { ThemeTypeVariation = UiVariants.ToggleSwitch, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        box.Toggled += on =>
        {
            if (!refreshing) service.Update(s => write(s, on));
        };
        refreshers.Add(() =>
        {
            box.Text = key.Tr();
            box.ButtonPressed = read(service.Current);
        });
        page.AddChild(box);
    }

    private static int IndexOf(IReadOnlyList<string> list, string value)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] == value) return i;
        }
        return -1;
    }

    private void Refresh()
    {
        refreshing = true;
        try
        {
            RefreshControls();
        }
        finally
        {
            refreshing = false;
        }
    }

    private void RefreshControls()
    {
        foreach (var refresh in refreshers) refresh();
        var s = service.Current;
        nameLabel.Text = $"{"Player".Tr()}: {s.PlayerName}";
        scaleStepper.Value = (int)MathF.Round(s.UiScale * 100f, MidpointRounding.AwayFromZero);
        windowMode.Select(Math.Max(0, IndexOf(OptionsModel.WindowModes, s.WindowMode)));
        packPicker.Select(Math.Max(0, IndexOf(packNames, s.AssetPack)));
        graphicsPicker.Select((int)s.Quality);
        for (int i = 0; i < graphicsPicker.ItemCount; i++) graphicsPicker.SetItemText(i, ((GraphicsQuality)i).ToString().Tr());
        var note = GraphicsRuntime.NoteFor(s);
        graphicsNote.Text = note.Tr();
        graphicsNote.Visible = note != "";
        graphicsRestart.Visible = GraphicsRuntime.NeedsRestart(s);
        foreach (var (action, button) in keyButtons)
            button.Text = Listening == action ? "..." : Hotkeys.KeyFor(s.KeyBindings, action);
        foreach (var (tab, _) in tabs) tab.Retranslate();
        for (int i = 0; i < OptionsModel.WindowModes.Count; i++) windowMode.SetItemText(i, OptionsModel.WindowModes[i].Tr());
    }
}
