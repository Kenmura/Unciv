using System.Globalization;
using Godot;
using Unciv.Assets;
using Unciv.Commands;
using Unciv.Commands.Turn;
using Unciv.Commands.Units;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Map;
using Unciv.GodotClient.Settings;
using Unciv.GodotClient.Ui;
using Unciv.Logic;
using Unciv.Logic.Map;
using Unciv.Models;
using Unciv.Net.Visibility;
using Unciv.Queries.Units;
using Unciv.Utils;
using Unciv.Models.Translations;
using Unciv.GodotClient.Localization;

namespace Unciv.GodotClient.Play;

/// <summary>
/// C010: the playable vertical slice. The map (C009) with a coloured block per visible unit, click to select one of your units, a path of dots to a
/// destination, a second click on it (or a right click) to move, and a plain Next turn button that ends the turn, lets the AI civs play and shows the result.
/// The screen reads the world only from the viewer's <see cref="ClientSnapshot"/> and the preview queries, and changes it only by submitting commands through
/// <see cref="LocalHost"/>; after every accepted command it redraws from a fresh snapshot.
/// </summary>
public partial class GameScreen : Control
{
    private const float DotSpacing = 0.4f;
    private const float DotLift = 0.12f;
    private const float StackOffset = 0.28f;
    private const float SelectedScale = 1.3f;
    private const float HudMargin = 12f;

    private readonly Dictionary<int, UnitNode> unitNodes = [];
    private IGameSession session = null!; // JUSTIFY: C010 assigned by Start before the screen is used
    private AssetRegistry assets = null!; // JUSTIFY: C010 assigned by Start before the screen is used
    private GameQueries queries = null!; // JUSTIFY: C010 assigned by Start before the screen is used
    private Node3D unitLayer = null!; // JUSTIFY: C010 assigned in _Ready before any use
    private Node3D dotLayer = null!; // JUSTIFY: C010 assigned in _Ready before any use
    private Label statusLabel = null!; // JUSTIFY: C010 assigned in _Ready before any use
    private int bannerTurn = -1;
    private ClientSnapshot snapshot = null!; // JUSTIFY: C010 assigned by Refresh in Start
    private int previewRequest;
    private PathPreviewResult? preview;
    private bool mapLoaded;
    private int selectionRequest;
    private HashSet<(int X, int Y)> attackTiles = [];
    private (string Kind, HexCoord Target)? pendingAttack;

    public MapView Map { get; private set; } = null!; // JUSTIFY: C010 assigned in _Ready before any use

    public HudView Hud { get; private set; } = null!; // JUSTIFY: C011 assigned in _Ready before any use

    public MinimapView Minimap { get; private set; } = null!; // JUSTIFY: C014 assigned in _Ready before any use

    /// <summary>Cities, borders, roads and improvements drawn on the map (C041).</summary>
    public MapOverlayLayer Overlays { get; private set; } = null!; // JUSTIFY: C041 assigned in _Ready before any use

    public UnitPanelView Panel { get; private set; } = null!; // JUSTIFY: C012 assigned in _Ready before any use

    public CombatPreviewView CombatPreview { get; private set; } = null!; // JUSTIFY: C012 assigned in _Ready before any use

    public TileOverlay ReachableOverlay { get; private set; } = null!; // JUSTIFY: C012 assigned in _Ready before any use

    public TileOverlay AttackOverlay { get; private set; } = null!; // JUSTIFY: C012 assigned in _Ready before any use

    /// <summary>C033: plays the movement and combat events of every command on the map. Off until a host turns <see cref="CombatPresenter.Enabled"/> on.</summary>
    public CombatPresenter Presenter { get; } = new() { Name = "Presenter" };

    public int? SelectedUnitId { get; private set; }

    /// <summary>C013: the clickable notification list.</summary>
    public NotificationListView Notifications { get; private set; } = null!; // JUSTIFY: C013 assigned in _Ready before any use

    /// <summary>C013: the modal popup for the first pending alert.</summary>
    public AlertPopupView Alerts { get; private set; } = null!; // JUSTIFY: C013 assigned in _Ready before any use

    /// <summary>C026: the tutorial popup; it shows only while the screen follows client settings (<see cref="BindSettings"/>) that have tutorials on.</summary>
    public TutorialPopupView Tutorials { get; private set; } = null!; // JUSTIFY: C026 assigned in _Ready before any use

    /// <summary>C024: the victory/defeat screen, above everything else.</summary>
    public GameOverScreen GameOver { get; private set; } = null!; // JUSTIFY: C024 assigned in _Ready before any use

    /// <summary>Raised when the player leaves the finished (or any) game from the game-over screen or the menu; the composition root shows the main menu.</summary>
    public event Action? MainMenuRequested;

    /// <summary>C044: the in-game menu (Escape or the Menu button).</summary>
    public GameMenuView GameMenu { get; private set; } = null!; // JUSTIFY: C044 assigned in _Ready before any use

    /// <summary>C044: the menu's "Victory status" page.</summary>
    public VictoryStatusView VictoryStatus { get; private set; } = null!; // JUSTIFY: C044 assigned in _Ready before any use

    public Button MenuButton { get; } = new() { Name = "MenuButton", Text = "Menu".Tr() };

    /// <summary>
    /// C044: the menu asks the owner whether the game has progress no save holds (the save controller knows). Without an owner's answer the menu assumes it has, so a
    /// destructive entry asks.
    /// </summary>
    public Func<bool>? HasUnsavedProgress { get; set; }

    /// <summary>C044: the owner opens its save dialog / the load dialog / the new-game setup / the options. The game screen keeps no files and no settings of its own.</summary>
    public event Action? SaveRequested;

    public event Action? LoadRequested;

    public event Action? NewGameRequested;

    public event Action? OptionsRequested;

    private bool resigned;

    private bool watchingAfterDefeat;
    private int gameOverCheck;
    private int gameOverTurn = -1;

    /// <summary>C038: the modal decision picker (free great person, religion, spies, vote, promotion).</summary>
    public DecisionPickerView Picker { get; private set; } = null!; // JUSTIFY: C038 assigned in _Ready before any use

    /// <summary>C038: opens the promotion picker for the selected unit while the engine offers a promotion.</summary>
    public Button PromoteButton { get; } = new() { Name = "PromoteButton", Text = "Promote".Tr(), Visible = false };

    private readonly HashSet<string> deferredAlerts = [];
    private bool reviewingAlerts;

    /// <summary>The city whose screen (<see cref="City"/>) is open: the hook that tells which city.</summary>
    public CityDto? OpenCity { get; private set; }

    /// <summary>The city screen (C017); created by <see cref="Start"/>.</summary>
    public CityScreen City { get; private set; } = null!; // JUSTIFY: C017 assigned in Start before any use

    public bool IsCityPanelVisible => City.Visible;

    /// <summary>C021: the diplomacy screen, opened by the Diplomacy button.</summary>
    public DiplomacyScreen Diplomacy { get; private set; } = null!; // JUSTIFY: C021 assigned in Start before any use

    public bool IsDiplomacyVisible => Diplomacy.Visible;

    public Button DiplomacyButton { get; } = new() { Name = "DiplomacyButton", Text = "Diplomacy".Tr() };

    /// <summary>C020: the policy screen, opened by the Policies button or the "Pick a policy" decision.</summary>
    public PolicyScreen Policies { get; private set; } = null!; // JUSTIFY: C020 assigned in Start before any use

    public bool IsPolicyScreenVisible => Policies.Visible;

    public Button PolicyButton { get; } = new() { Name = "PolicyButton", Text = "Policies".Tr() };

    /// <summary>Opens the policy screen, closing the city and diplomacy screens.</summary>
    public void OpenPolicyScreen()
    {
        ShowCity(null);
        if (Trade.Visible) Trade.Close();
        if (Diplomacy.Visible) Diplomacy.Close();
        if (Tech.Visible) Tech.Close();
        Deselect();
        SetStatus("Policies");
        Run(Policies.OpenAsync());
        ShowTutorial(TutorialTrigger.CultureAndPolicies);
    }

    /// <summary>Opens the diplomacy screen on the civ with this id (the first met civ when null), closing the city screen.</summary>
    public void OpenDiplomacyScreen(string? civId = null)
    {
        ShowCity(null);
        if (Policies.Visible) Policies.Close();
        Deselect();
        SetStatus("Diplomacy");
        if (Tech.Visible) Tech.Close();
        if (Overview.Visible) Overview.Close();
        if (Trade.Visible) Trade.Close();
        Run(Diplomacy.OpenAsync(civId));
    }

    /// <summary>C023: the empire overview, opened by the Overview button.</summary>
    public OverviewScreen Overview { get; private set; } = null!; // JUSTIFY: C023 assigned in Start before any use

    public bool IsOverviewVisible => Overview.Visible;

    public Button OverviewButton { get; } = new() { Name = "OverviewButton", Text = "Overview".Tr() };

    /// <summary>Opens the overview on a tab (the last shown when null), closing the other full screens.</summary>
    public void OpenOverviewScreen(string? tab = null)
    {
        ShowCity(null);
        if (Diplomacy.Visible) Diplomacy.Close();
        if (Tech.Visible) Tech.Close();
        Deselect();
        if (Trade.Visible) Trade.Close();
        SetStatus("Overview");
        Run(Overview.OpenAsync(tab));
    }

    /// <summary>Closes the overview and shows a city of the viewer's: the camera goes to it and its screen opens.</summary>
    private void JumpToCity(string name)
    {
        var city = snapshot.Cities.FirstOrDefault(it => it.Owner == session.ViewerCivId && it.Name == name);
        if (city == null) return;
        Overview.Close();
        Map.FocusOn(MapLayout.TileTop(city.X, city.Y));
        ShowCity(city);
    }

    /// <summary>Closes the overview, selects one of the viewer's units and centres the camera on it.</summary>
    private void JumpToUnit(int id)
    {
        if (!unitNodes.TryGetValue(id, out var node) || !node.IsOwn) return;
        Overview.Close();
        SelectUnit(node);
        Map.FocusOn(UnitPosition(new HexCoord(node.Unit.X, node.Unit.Y), 0));
    }

    /// <summary>C022: the trade screen, opened by the Trade button.</summary>
    public TradeScreen Trade { get; private set; } = null!; // JUSTIFY: C022 assigned in Start before any use

    public bool IsTradeVisible => Trade.Visible;

    public Button TradeButton { get; } = new() { Name = "TradeButton", Text = "Trade".Tr() };

    /// <summary>Opens the trade screen on the civ with this id (the first met major civ when null), closing the other screens.</summary>
    public void OpenTradeScreen(string? civId = null)
    {
        ShowCity(null);
        if (Diplomacy.Visible) Diplomacy.Close();
        if (Tech.Visible) Tech.Close();
        if (Overview.Visible) Overview.Close();
        Deselect();
        SetStatus("Trade");
        Run(Trade.OpenAsync(civId));
    }

    /// <summary>C027: the Civilopedia, opened by the Civilopedia button.</summary>
    public PediaScreen Pedia { get; private set; } = null!; // JUSTIFY: C027 assigned in Start before any use

    public bool IsPediaVisible => Pedia.Visible;

    public Button PediaButton { get; } = new() { Name = "PediaButton", Text = "Civilopedia".Tr() };

    /// <summary>Opens the Civilopedia on a link (<c>Unit/Warrior</c>), or where it was last left; it covers whatever screen is open and Close returns to it.</summary>
    public void OpenPediaScreen(string? link = null)
    {
        Tooltips.Unpoint();
        Pedia.Open(link);
    }

    /// <summary>C027: the one tooltip of the game screen; the map feeds it the tile under the pointer.</summary>
    public TooltipHost Tooltips { get; private set; } = null!; // JUSTIFY: C027 assigned in Start before any use

    /// <summary>The tile under the pointer (null over the HUD or off the map) and the BBCode its tooltip will show once the engine answered ("" until then).</summary>
    public HexCoord? HoveredTile { get; private set; }

    public string HoverTip { get; private set; } = "";

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion && Map != null && Tooltips != null) UpdateHover(motion.Position);
    }

    /// <summary>Follows the pointer: over the map (no widget under it) the tile under it is asked of the engine for the tooltip; over a widget the tip goes away.</summary>
    private void UpdateHover(Vector2 pointer)
    {
        var over = GetViewport().GuiGetHoveredControl();
        HexCoord? tile = over == null || over == this ? Map.PickTile(pointer) : null;
        if (tile == HoveredTile) return;
        HoveredTile = tile;
        HoverTip = "";
        Tooltips.Restart();
        if (tile is not { } coord)
        {
            Tooltips.Unpoint();
            return;
        }
        Tooltips.Point(() => HoverTip);
        Run(LoadHoverTip(coord));
    }

    private async Task LoadHoverTip(HexCoord coord)
    {
        var info = await queries.TileInfo(coord.X, coord.Y);
        if (HoveredTile != coord || info == null) return; // the pointer moved on, or the engine would not describe an unexplored tile
        HoverTip = TooltipModel.ToBbcode(TooltipModel.Tile(info, session.Ruleset), text => text.Tr());
    }

    /// <summary>C019: the tech screen, opened by the Technology button.</summary>
    public TechScreen Tech { get; private set; } = null!; // JUSTIFY: C019 assigned in Start before any use

    public bool IsTechVisible => Tech.Visible;

    public Button TechButton { get; } = new() { Name = "TechButton", Text = "Technology".Tr() };

    /// <summary>Opens the tech screen on a tech (the current research when null), closing the city and diplomacy screens.</summary>
    public void OpenTechScreen(string? tech = null)
    {
        ShowCity(null);
        if (Diplomacy.Visible) Diplomacy.Close();
        if (Policies.Visible) Policies.Close();
        if (Overview.Visible) Overview.Close();
        if (Trade.Visible) Trade.Close();
        Deselect();
        SetStatus("Technology");
        Run(Tech.OpenAsync(tech));
    }

    private ClientSettingsService? boundSettings;
    /// <summary>C026: false keeps tutorial popups away (scenarios that are about something else turn them off; the game never does).</summary>
    public bool TutorialsEnabled { get; set; } = true;

    private TutorialQueue tutorialQueue = null!; // JUSTIFY: C026 assigned in Start before any use

    /// <summary>
    /// C026: queues a tutorial (shown unless tutorials are off or it was seen) and shows the first waiting one. Tutorials appear only when the screen follows client
    /// settings: without a binding there is nowhere to remember what was seen, so nothing is shown.
    /// </summary>
    public void ShowTutorial(TutorialTrigger trigger)
    {
        if (!TutorialsEnabled || boundSettings == null || tutorialQueue == null) return;
        if (tutorialQueue.Enqueue(trigger, boundSettings.Current) == TutorialEnqueue.NothingToShow) MarkTutorialSeen(trigger);
        OpenTutorialPage();
    }

    private void CheckTutorials()
    {
        if (!TutorialsEnabled || boundSettings == null || !boundSettings.Current.ShowTutorials || GameOver.Visible) return;
        foreach (var trigger in TutorialModel.Detect(snapshot, session.Ruleset))
        {
            if (tutorialQueue.Enqueue(trigger, boundSettings.Current) == TutorialEnqueue.NothingToShow) MarkTutorialSeen(trigger);
        }
        OpenTutorialPage();
    }

    private void OpenTutorialPage()
    {
        if (Tutorials.Visible) return;
        if (tutorialQueue.Current is { } page) Tutorials.Open(page);
    }

    private void DismissTutorial()
    {
        if (tutorialQueue.Dismiss() is { } finished) MarkTutorialSeen(finished);
        if (tutorialQueue.Current is { } page) Tutorials.Open(page);
        else Tutorials.Close();
    }

    private void MarkTutorialSeen(TutorialTrigger trigger) => boundSettings?.Update(s => s.WithTutorialSeen(TutorialModel.SeenKey(trigger)));

    /// <summary>
    /// C028: follows the client settings: key bindings (map pan keys and the screen's hotkeys), scroll speed, edge-pan and the resource toggle apply now and after every
    /// change. Call after the screen entered the tree; calling it again replaces the earlier binding.
    /// </summary>
    public void BindSettings(ClientSettingsService settings)
    {
        if (boundSettings != null) boundSettings.Changed -= ApplySettings;
        boundSettings = settings;
        settings.Changed += ApplySettings;
        ApplySettings(settings.Current);
        if (session != null) CheckTutorials(); // the first look at the game came before the settings were known
    }

    private void ApplySettings(ClientSettings settings)
    {
        hotkeys = settings.KeyBindings;
        Map.KeyBindings = settings.KeyBindings;
        Map.ScrollSpeed = settings.ScrollSpeed;
        Map.EdgePan = settings.EdgePan;
        Map.ShowResources = settings.ShowResources;
        Overlays.ShowYields = settings.ShowYields;
    }

    private IReadOnlyDictionary<string, string> hotkeys = new Dictionary<string, string>();

    /// <summary>C028: runs the action a hotkey text is bound to (true when it was one of the game screen's actions and ran). Ignored while a full screen (city, diplomacy, tech, policies, overview, game over) or an alert covers the map.</summary>
    public bool TryHotkey(string keyText)
    {
        if (Tutorials.Visible)
        {
            if (keyText != "Space") return false;
            Tutorials.OkButton.EmitSignal(BaseButton.SignalName.Pressed);
            return true;
        }
        if (City.Visible || Diplomacy.Visible || Tech.Visible || Policies.Visible || Overview.Visible || GameOver.Visible || Alerts.Visible) return false;
        switch (Hotkeys.Resolve(hotkeys, keyText))
        {
            case HotkeyAction.EndTurn:
                OnNextTurn();
                return true;
            case HotkeyAction.NextUnit:
                SelectNextUnit();
                return true;
            case HotkeyAction.OpenTech:
                OpenTechScreen();
                return true;
            case HotkeyAction.OpenPolicies:
                OpenPolicyScreen();
                return true;
            case HotkeyAction.OpenDiplomacy:
                OpenDiplomacyScreen();
                return true;
            default:
                return false;
        }
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape } && IsVisibleInTree() && TryEscape()) GetViewport().SetInputAsHandled();
        if (@event is InputEventKey { Pressed: true, Echo: false } key && HotkeyInput.TextOf(key) is { } text && TryHotkey(text)) GetViewport().SetInputAsHandled();
    }

    /// <summary>C044: Escape closes the menu or the victory page when one is open, else opens the menu unless a screen or popup already covers the map (those take Escape themselves).</summary>
    private bool TryEscape()
    {
        if (GameMenu.Visible) return GameMenu.Cancel();
        if (VictoryStatus.Visible)
        {
            VictoryStatus.Close();
            return true;
        }
        if (City.Visible || Diplomacy.Visible || Tech.Visible || Policies.Visible || Trade.Visible || Overview.Visible || Alerts.Visible || GameOver.Visible) return false;
        OpenGameMenu();
        return true;
    }

    /// <summary>Opens the in-game menu. The entries depend on the engine's game-over status (a defeated civ cannot resign) and on whether the game has unsaved progress.</summary>
    public void OpenGameMenu() => Run(OpenGameMenuAsync());

    private async Task OpenGameMenuAsync()
    {
        var outcome = await queries.GameOver();
        bool unsaved = HasUnsavedProgress?.Invoke() ?? true;
        GameMenu.Open(GameMenuModel.Build(outcome?.Status ?? Unciv.Queries.Victory.GameOverQuery.Playing, unsaved));
    }

    private async Task OnMenuChosen(GameMenuKind kind)
    {
        GameMenu.Close();
        switch (kind)
        {
            case GameMenuKind.Save:
                SaveRequested?.Invoke();
                break;
            case GameMenuKind.Load:
                LoadRequested?.Invoke();
                break;
            case GameMenuKind.NewGame:
                NewGameRequested?.Invoke();
                break;
            case GameMenuKind.Options:
                OptionsRequested?.Invoke();
                break;
            case GameMenuKind.MainMenu:
                MainMenuRequested?.Invoke();
                break;
            case GameMenuKind.VictoryStatus:
                VictoryStatus.Open(VictoryStatusModel.Build(await queries.VictoryProgress()));
                break;
            case GameMenuKind.Resign:
                await Resign();
                break;
        }
    }

    /// <summary>The engine validates the actor and does the change; the screen only sends the command and shows what is left: the resigned screen with the way out.</summary>
    private async Task Resign()
    {
        var result = await session.SubmitAsync(new Resign());
        if (!result.Accepted)
        {
            SetStatus("Resign refused: " + result.Message);
            return;
        }
        var rankings = await queries.Rankings();
        var history = await queries.History();
        resigned = true;
        gameOverCheck++; // a check that was already asking must not close the screen
        Refresh();
        GameOver.Open(GameOverModel.BuildResigned(rankings, history), civ => OwnerColour(civ));
        SetStatus("You resigned.");
    }

    /// <summary>Opens the city screen for a city of the snapshot (the viewer's own in full, any other from its snapshot entry), or closes it with null.</summary>
    public void OpenCityScreen(CityDto? city) => ShowCity(city);

    public IReadOnlyDictionary<int, UnitNode> Units => unitNodes;

    public int Turn => snapshot.Turn;

    public string StatusText => statusLabel.Text;

    public int PathDotCount => dotLayer.GetChildCount();

    /// <summary>The destination of the path preview currently drawn; a click on it moves.</summary>
    public HexCoord? PreviewTarget { get; private set; }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore; // the map reads unhandled input; only the HUD widgets take the mouse
        Map = new MapView { Name = "Map" };
        AddChild(Map);
        Overlays = new MapOverlayLayer { Name = "Overlays" };
        AddChild(Overlays);
        unitLayer = new Node3D { Name = "Units" };
        AddChild(unitLayer);
        AddChild(Presenter);
        dotLayer = new Node3D { Name = "PathDots" };
        AddChild(dotLayer);
        ReachableOverlay = new TileOverlay(PlaceholderPalette.ReachableTint) { Name = "ReachableOverlay" };
        AttackOverlay = new TileOverlay(PlaceholderPalette.AttackTint) { Name = "AttackOverlay" };
        AddChild(ReachableOverlay);
        AddChild(AttackOverlay);

        statusLabel = new Label { Name = "StatusLabel" };
        AddChild(statusLabel);
        Hud = new HudView { Name = "Hud" };
        AddChild(Hud);
        AddChild(DiplomacyButton);
        DiplomacyButton.Pressed += () => OpenDiplomacyScreen();
        AddChild(TechButton);
        AddChild(TradeButton);
        TradeButton.Pressed += () => OpenTradeScreen();
        TechButton.Pressed += () => OpenTechScreen();
        AddChild(PolicyButton);
        PolicyButton.Pressed += OpenPolicyScreen;
        AddChild(OverviewButton);
        OverviewButton.Pressed += () => OpenOverviewScreen();
        AddChild(PediaButton);
        PediaButton.Pressed += () => OpenPediaScreen();
        Hud.ActionRequested += OnHudAction;
        Hud.EndTurnRequested += OnNextTurn;
        Notifications = new NotificationListView();
        AddChild(Notifications);
        Notifications.Chosen += OnNotificationChosen;
        Notifications.ReviewRequested += () => Run(ReviewAlerts());
        Panel = new UnitPanelView();
        AddChild(Panel);
        Panel.EntryChosen += OnMenuEntry;
        Panel.Confirmed += entry => Run(ExecuteEntry(entry, confirmed: true));
        CombatPreview = new CombatPreviewView();
        AddChild(CombatPreview);
        CombatPreview.ConfirmRequested += () => Run(ConfirmAttack());
        CombatPreview.CancelRequested += ClearAttackPreview;
        Minimap = new MinimapView { Name = "Minimap" };
        AddChild(Minimap);
        Minimap.Attach(Map);
        AddChild(PromoteButton);
        PromoteButton.Pressed += () => Run(OpenPromotionPicker());
        Picker = new DecisionPickerView();
        AddChild(Picker);
        Picker.Confirmed += (spec, commands) => Run(ConfirmPicker(spec, commands));
        Picker.Dismissed += spec => Run(DismissPicker(spec));
        Alerts = new AlertPopupView();
        AddChild(Alerts);
        Alerts.Answered += (popup, answer) => Run(AnswerAlert(popup, answer));
        Alerts.Deferred += popup =>
        {
            deferredAlerts.Add(popup.Key);
            Alerts.Close();
            Run(RefreshHud());
        };
        GameOver = new GameOverScreen();
        AddChild(GameOver);
        Tutorials = new TutorialPopupView();
        AddChild(Tutorials);
        Tutorials.Dismissed += DismissTutorial;
        GameOver.Chosen += kind => Run(OnGameOverChosen(kind));
        AddChild(MenuButton);
        MenuButton.Pressed += OpenGameMenu;
        VictoryStatus = new VictoryStatusView();
        AddChild(VictoryStatus);
        GameMenu = new GameMenuView();
        AddChild(GameMenu);
        GameMenu.Chosen += kind => Run(OnMenuChosen(kind));
        GetViewport().SizeChanged += Layout;
        Layout();

        Map.TileClicked += OnTileClicked;
        Map.TileRightClicked += OnTileRightClicked;
    }

    // Sized from the viewport, not from anchors: the screen may sit under a parent Control that has no size (the scenario runner).
    private void Layout()
    {
        Size = GetViewportRect().Size;
        Hud.LayoutIn(Size);
        if (City != null)
        {
            City.Position = new Vector2(0f, Hud.TopBarHeight);
            City.Size = new Vector2(Size.X, Size.Y - Hud.TopBarHeight);
        }
        if (Diplomacy != null)
        {
            Diplomacy.Position = new Vector2(0f, Hud.TopBarHeight);
            Diplomacy.Size = new Vector2(Size.X, Size.Y - Hud.TopBarHeight);
        }
        if (Trade != null)
        {
            Trade.Position = new Vector2(0f, Hud.TopBarHeight);
            Trade.Size = new Vector2(Size.X, Size.Y - Hud.TopBarHeight);
        }
        if (Tech != null)
        {
            Tech.Position = new Vector2(0f, Hud.TopBarHeight);
            Tech.Size = new Vector2(Size.X, Size.Y - Hud.TopBarHeight);
        }
        if (Overview != null)
        {
            Overview.Position = new Vector2(0f, Hud.TopBarHeight);
            Overview.Size = new Vector2(Size.X, Size.Y - Hud.TopBarHeight);
        }
        DiplomacyButton.Position = new Vector2(HudMargin, HudMargin + Hud.TopBarHeight);
        TechButton.Position = new Vector2(HudMargin + DiplomacyButton.GetCombinedMinimumSize().X + HudMargin, HudMargin + Hud.TopBarHeight);
        PolicyButton.Position = new Vector2(TechButton.Position.X + TechButton.GetCombinedMinimumSize().X + HudMargin, HudMargin + Hud.TopBarHeight);
        if (Policies != null)
        {
            Policies.Position = new Vector2(0f, Hud.TopBarHeight);
            Policies.Size = new Vector2(Size.X, Size.Y - Hud.TopBarHeight);
        }
        OverviewButton.Position = new Vector2(PolicyButton.Position.X + PolicyButton.GetCombinedMinimumSize().X + HudMargin, HudMargin + Hud.TopBarHeight);
        PediaButton.Position = new Vector2(HudMargin, Size.Y - 2f * HudMargin - 24f - PediaButton.GetCombinedMinimumSize().Y); // bottom left above the status line: the top row is full
        if (Pedia != null)
        {
            Pedia.Position = new Vector2(0f, Hud.TopBarHeight);
            Pedia.Size = new Vector2(Size.X, Size.Y - Hud.TopBarHeight);
        }
        TradeButton.Position = new Vector2(OverviewButton.Position.X + OverviewButton.GetCombinedMinimumSize().X + HudMargin, HudMargin + Hud.TopBarHeight);
        statusLabel.Position = new Vector2(HudMargin, Size.Y - HudMargin - 24f);
        Minimap.Position = new Vector2(Size.X - HudMargin - Minimap.Size.X, HudMargin + Hud.TopBarHeight);
        Notifications.Position = new Vector2(HudMargin, HudMargin + Hud.TopBarHeight + DiplomacyButton.GetCombinedMinimumSize().Y + HudMargin);
        MenuButton.Position = new Vector2(TradeButton.Position.X + TradeButton.GetCombinedMinimumSize().X + HudMargin, HudMargin + Hud.TopBarHeight);
        Alerts.LayoutIn(Size);
        GameOver.LayoutIn(Size);
        VictoryStatus.LayoutIn(Size);
        GameMenu.LayoutIn(Size);
        Picker.LayoutIn(Size);
        Tutorials.LayoutIn(Size);
        Panel.LayoutIn(Size);
        PlacePromoteButton();
        CombatPreview.LayoutIn(Size);
    }

    public override void _ExitTree()
    {
        GetViewport().SizeChanged -= Layout;
        if (session != null)
        {
            session.SnapshotChanged -= OnSnapshotChanged;
            session.EventsReceived -= OnEventsReceived;
        }
    }

    /// <summary>Connects the screen to a game and draws it, centred on the human's first unit. Call after the screen entered the tree; calling it again switches to another game (C016: load).</summary>
    public void Start(IGameSession gameSession, AssetRegistry registry)
    {
        if (session != null)
        {
            session.SnapshotChanged -= OnSnapshotChanged;
            session.EventsReceived -= OnEventsReceived;
            Presenter.Reset();
            SelectedUnitId = null;
            mapLoaded = false;
            bannerTurn = -1;
        }
        session = gameSession;
        assets = registry;
        queries = new GameQueries(session);
        var theme = new UiTheme(registry);
        Hud.Init(theme);
        Notifications.Init(theme);
        Alerts.Init(theme);
        GameOver.Init(theme);
        Tutorials.Init(theme);
        Tutorials.Close();
        tutorialQueue = new TutorialQueue(session.Ruleset);
        GameOver.Close();
        VictoryStatus.Init(theme);
        VictoryStatus.Close();
        GameMenu.Init(theme);
        GameMenu.Close();
        MenuButton.Theme = theme.Theme;
        resigned = false;
        watchingAfterDefeat = false;
        Picker.Init(theme);
        PromoteButton.Theme = theme.Theme;
        deferredAlerts.Clear();
        reviewingAlerts = false;
        City = new CityScreen(session, theme);
        AddChild(City);
        City.CommandAccepted += Refresh;
        City.Closed += () => ShowCity(null);
        if (Diplomacy != null)
        {
            RemoveChild(Diplomacy);
            Diplomacy.QueueFree();
        }
        Diplomacy = new DiplomacyScreen(session, theme);
        AddChild(Diplomacy);
        Diplomacy.CommandAccepted += Refresh;
        DiplomacyButton.Theme = theme.Theme;
        if (Tech != null)
        {
            RemoveChild(Tech);
            Tech.QueueFree();
        }
        if (Trade != null)
        {
            RemoveChild(Trade);
            Trade.QueueFree();
        }
        Trade = new TradeScreen(session, theme);
        AddChild(Trade);
        Trade.CommandAccepted += Refresh;
        TradeButton.Theme = theme.Theme;
        Tech = new TechScreen(session, theme);
        AddChild(Tech);
        Tech.CommandAccepted += Refresh;
        TechButton.Theme = theme.Theme;
        if (Policies != null)
        {
            RemoveChild(Policies);
            Policies.QueueFree();
        }
        Policies = new PolicyScreen(session, theme);
        AddChild(Policies);
        Policies.CommandAccepted += Refresh;
        PolicyButton.Theme = theme.Theme;
        if (Overview != null)
        {
            RemoveChild(Overview);
            Overview.QueueFree();
        }
        Overview = new OverviewScreen(session, theme);
        AddChild(Overview);
        Overview.CommandAccepted += Refresh;
        Overview.CityChosen += JumpToCity;
        Overview.UnitChosen += JumpToUnit;
        OverviewButton.Theme = theme.Theme;
        if (Tooltips != null)
        {
            RemoveChild(Tooltips);
            Tooltips.QueueFree();
        }
        Tooltips = new TooltipHost(theme) { Name = "Tooltips" };
        HoveredTile = null;
        HoverTip = "";
        if (Pedia != null)
        {
            RemoveChild(Pedia);
            Pedia.QueueFree();
        }
        Pedia = new PediaScreen(session.Ruleset, theme, Tooltips);
        AddChild(Pedia);
        PediaButton.Theme = theme.Theme;
        Panel.Init(theme);
        CombatPreview.Init(theme);
        MoveChild(Picker, -1);
        MoveChild(Alerts, -1); // the popups sit above every screen opened above
        MoveChild(GameOver, -1);
        MoveChild(VictoryStatus, -1);
        MoveChild(GameMenu, -1);
        MoveChild(Tutorials, -1);
        AddChild(Tooltips); // above everything, so a tip is never covered by a screen
        ReachableOverlay.Setup(registry);
        AttackOverlay.Setup(registry);
        session.EventsReceived += OnEventsReceived;
        Presenter.Bind(id => unitNodes.GetValueOrDefault(id), coord => UnitPosition(coord, 0));
        session.SnapshotChanged += OnSnapshotChanged; // commands made elsewhere (another screen, a scenario) change what the HUD shows
        Refresh();
        var first = snapshot.Units.FirstOrDefault(it => it.Own != null);
        if (first != null) Map.FocusOn(MapLayout.TileTop(first.X, first.Y));
        SetStatus("Click one of your units.");
    }

    /// <summary>Redraws everything from the session's current snapshot (the screen does this itself after its own commands; a caller that changed the game another way calls it).</summary>
    public void Refresh()
    {
        snapshot = session.Snapshot;
        Map.Load(assets, snapshot.Tiles, keepCamera: mapLoaded);
        mapLoaded = true;
        Minimap.Update(snapshot.Tiles, OwnerColour);
        Layout();
        Overlays.Rebuild(assets, session.Ruleset, Map, snapshot.Tiles, snapshot.Cities, OwnerColour);
        if (OpenCity != null) ShowCity(snapshot.Cities.FirstOrDefault(it => it.X == OpenCity.X && it.Y == OpenCity.Y));
        RebuildUnits();
        ClearPreview();
        Run(RefreshHud());
        if (SelectedUnitId is { } id && unitNodes.TryGetValue(id, out var selected) && selected.IsOwn)
            Map.Select(new HexCoord(selected.Unit.X, selected.Unit.Y));
        else SelectedUnitId = null;
        ApplySelectionScale();
        Run(RefreshSelection());
    }

    private Rgb? OwnerColour(string owner) => session.Ruleset.Nations.TryGetValue(owner, out var nation) ? ToRgb(nation.GetOuterColor()) : null;

    private static Rgb ToRgb(Unciv.Models.EngineColor colour) =>
        new((byte)Math.Round(colour.R * 255f, MidpointRounding.AwayFromZero), (byte)Math.Round(colour.G * 255f, MidpointRounding.AwayFromZero), (byte)Math.Round(colour.B * 255f, MidpointRounding.AwayFromZero));

    private void RebuildUnits()
    {
        foreach (var node in unitNodes.Values) node.QueueFree();
        unitNodes.Clear();
        var stack = new Dictionary<HexCoord, int>();
        foreach (var unit in snapshot.Units)
        {
            var coord = new HexCoord(unit.X, unit.Y);
            int index = stack.GetValueOrDefault(coord);
            stack[coord] = index + 1;
            var colour = session.Ruleset.Nations.TryGetValue(unit.Owner, out var nation) ? nation.GetOuterColor() : null;
            var node = new UnitNode { Name = $"Unit_{unit.Id}", Unit = unit };
            node.Place(UnitPosition(coord, index));
            node.Build(assets, session.Ruleset, session.Ruleset.Units.GetValueOrDefault(unit.Name), colour == null ? null : (colour.R, colour.G, colour.B));
            unitLayer.AddChild(node);
            unitNodes[unit.Id] = node;
        }
        Presenter.Refresh();
    }

    private Vector3 UnitPosition(HexCoord coord, int stackIndex)
    {
        var elevation = Map.TileNodeAt(coord)?.Elevation ?? 1f;
        var top = MapLayout.TileTop(coord.X, coord.Y, elevation);
        return top + new Vector3(stackIndex * StackOffset, 0f, stackIndex * StackOffset);
    }

    private void ApplySelectionScale()
    {
        foreach (var (id, node) in unitNodes) node.Scale = id == SelectedUnitId ? Vector3.One * SelectedScale : Vector3.One;
    }

    // Input

    private void OnTileClicked(HexCoord? coord)
    {
        if (coord is not { } tile)
        {
            Deselect();
            return;
        }
        var own = OwnUnitsAt(tile);
        var city = snapshot.Cities.FirstOrDefault(it => it.X == tile.X && it.Y == tile.Y);
        if (city != null && (own.Count == 0 ? SelectedUnitId == null : SelectedUnitId is { } selected && own.Any(it => it.Unit.Id == selected)))
        {
            ShowCity(city); // a click on a city opens it, unless a unit elsewhere is selected (then the click is a move target) or a unit there is still to be selected
            return;
        }
        switch (UnitOrdersModel.ClickIntent((tile.X, tile.Y), own.Count > 0, SelectedUnitId != null, attackTiles))
        {
            case TileClickIntent.SelectOwn:
                int at = SelectedUnitId is { } current ? own.FindIndex(it => it.Unit.Id == current) : -1;
                SelectUnit(own[(at + 1) % own.Count]);
                break;
            case TileClickIntent.AttackPreview:
                Run(ShowAttackPreview(tile));
                break;
            case TileClickIntent.PathPreview:
                if (PreviewTarget == tile) Run(MoveSelectedTo());
                else Run(ShowPreview(tile));
                break;
        }
    }

    private void OnTileRightClicked(HexCoord? coord)
    {
        if (coord is not { } tile || SelectedUnitId == null) return;
        if (attackTiles.Contains((tile.X, tile.Y))) Run(ShowAttackPreview(tile));
        else Run(RightClickMove(tile));
    }

    private async Task RightClickMove(HexCoord tile)
    {
        await ShowPreview(tile);
        if (PreviewTarget == tile) await MoveSelectedTo();
    }

    private List<UnitNode> OwnUnitsAt(HexCoord tile) =>
        unitNodes.Values.Where(it => it.IsOwn && it.Unit.X == tile.X && it.Unit.Y == tile.Y).OrderBy(it => it.Unit.Id).ToList();

    /// <summary>The Promote button sits at the bottom, to the right of the unit panel.</summary>
    private void PlacePromoteButton() =>
        PromoteButton.Position = new Vector2(HudMargin + Panel.GetCombinedMinimumSize().X + HudMargin, Size.Y - 48f - PromoteButton.GetCombinedMinimumSize().Y);

    /// <summary>Selects one of the viewer's units by id (what clicking it does); an unknown or foreign id selects nothing.</summary>
    public void SelectUnitById(int id)
    {
        if (unitNodes.TryGetValue(id, out var node) && node.IsOwn) SelectUnit(node);
    }

    private void SelectUnit(UnitNode node)
    {
        SelectedUnitId = node.Unit.Id;
        ClearPreview();
        Map.Select(new HexCoord(node.Unit.X, node.Unit.Y));
        ApplySelectionScale();
        SetStatus(DescribeSelected());
        Run(RefreshSelection());
    }

    /// <summary>Opens (or, with null, closes) the city screen for a city of the snapshot.</summary>
    private void ShowCity(CityDto? city)
    {
        if (city != null && Diplomacy.Visible) Diplomacy.Close();
        if (city != null && Tech.Visible) Tech.Close();
        if (city != null && Policies.Visible) Policies.Close();
        if (city != null && Overview.Visible) Overview.Close();
        if (city != null && Trade.Visible) Trade.Close();
        if (city != null) Deselect(); // C012: the unit panel gives way to the city screen
        OpenCity = city;
        if (city == null)
        {
            if (City.Visible) City.Close();
            return;
        }
        SetStatus("City: " + city.Name);
        Run(City.OpenAsync(city));
    }

    private void Deselect()
    {
        SelectedUnitId = null;
        ClearPreview();
        ApplySelectionScale();
        ClearSelectionUi();
        SetStatus("Click one of your units.");
    }

    private string DescribeSelected()
    {
        var unit = unitNodes[SelectedUnitId!.Value].Unit;
        string moves = (unit.Own?.CurrentMovement ?? 0f).ToString("0.##", CultureInfo.InvariantCulture);
        return $"{unit.Name}   moves {moves}   health {(unit.Own?.Health ?? 0).ToString(CultureInfo.InvariantCulture)}";
    }

    private async Task ShowPreview(HexCoord tile)
    {
        ClearPreview();
        int request = ++previewRequest;
        var unitNode = unitNodes[SelectedUnitId!.Value];
        if (unitNode.Unit.Own?.CurrentMovement is not > 0f)
        {
            SetStatus($"{DescribeSelected()}   (no movement left)");
            return;
        }
        var result = await queries.Path(unitNode.Unit.Id, tile.X, tile.Y);
        if (request != previewRequest) return; // a newer click or a deselect superseded this answer
        if (result is not { Reachable: true, FirstStep: not null })
        {
            SetStatus($"{DescribeSelected()}   (cannot reach that tile)");
            return;
        }
        preview = result;
        PreviewTarget = tile;
        DrawDots(new HexCoord(unitNode.Unit.X, unitNode.Unit.Y), result);
        SetStatus($"{DescribeSelected()}   path {result.Turns.ToString(CultureInfo.InvariantCulture)} turn(s): click again to move");
    }

    private void DrawDots(HexCoord from, PathPreviewResult path)
    {
        var points = new List<Vector3> { Lifted(from) };
        points.AddRange(path.Legs.SelectMany(leg => leg.Tiles).Select(it => Lifted(new HexCoord(it.X, it.Y))));
        for (int i = 1; i < points.Count; i++)
        {
            var start = points[i - 1];
            var end = points[i];
            int steps = Math.Max(1, (int)MathF.Ceiling(start.DistanceTo(end) / DotSpacing));
            for (int step = 1; step <= steps; step++)
            {
                var dot = MapMaterials.PathDot();
                dot.Position = start.Lerp(end, step / (float)steps);
                dotLayer.AddChild(dot);
            }
        }
    }

    private Vector3 Lifted(HexCoord coord)
    {
        var top = MapLayout.TileTop(coord.X, coord.Y, Map.TileNodeAt(coord)?.Elevation ?? 1f);
        return top + new Vector3(0f, DotLift, 0f);
    }

    private void ClearPreview()
    {
        previewRequest++;
        preview = null;
        PreviewTarget = null;
        foreach (var dot in dotLayer.GetChildren()) dot.QueueFree();
        ClearAttackPreview();
    }

    // C012: unit panel, menu and overlays

    private void ClearSelectionUi()
    {
        selectionRequest++;
        attackTiles = [];
        Panel.Clear();
        PromoteButton.Visible = false;
        ReachableOverlay.Clear();
        AttackOverlay.Clear();
        ClearAttackPreview();
    }

    private void ClearAttackPreview()
    {
        pendingAttack = null;
        CombatPreview.Clear();
    }

    private Vector3 TileTopAt(HexCoord coord) => MapLayout.TileTop(coord.X, coord.Y, Map.TileNodeAt(coord)?.Elevation ?? 1f);

    /// <summary>Asks the engine what the selected unit can do (actions, improvements, promotions, reach, attack targets) and shows the panel and both overlays; nothing is computed here.</summary>
    private async Task RefreshSelection()
    {
        int request = ++selectionRequest;
        if (SelectedUnitId is not { } id || !unitNodes.TryGetValue(id, out var node) || !node.IsOwn)
        {
            ClearSelectionUi();
            return;
        }
        var unit = node.Unit;
        var actions = await queries.Actions(id);
        var tile = await queries.TileInfo(unit.X, unit.Y, id);
        var promotions = await queries.Promotions(id);
        var reach = await queries.Reachable(id);
        var targets = await queries.AttackTargets(id);
        if (request != selectionRequest || SelectedUnitId != id) return; // superseded by another selection or a deselect
        session.Ruleset.Units.TryGetValue(unit.Name, out var baseUnit);
        var menu = UnitOrdersModel.Menu(actions ?? new UnitActionsResult(id, []), tile, promotions, unit.Own?.CurrentMovement > 0f);
        Panel.Show(UnitOrdersModel.Panel(unit, baseUnit), menu);
        PromoteButton.Visible = promotions is { Promotions.Count: > 0 };
        PlacePromoteButton();
        attackTiles = targets == null ? [] : [.. UnitOrdersModel.AttackTiles(targets)];
        ReachableOverlay.Show(reach == null ? [] : [.. UnitOrdersModel.ReachableTiles(reach).Select(it => new HexCoord(it.X, it.Y))], TileTopAt);
        AttackOverlay.Show([.. attackTiles.Select(it => new HexCoord(it.X, it.Y))], TileTopAt);
    }

    private async Task ShowAttackPreview(HexCoord tile)
    {
        ClearPreview();
        int request = ++previewRequest;
        if (SelectedUnitId is not { } id) return;
        var result = await queries.AttackPreview(id, tile.X, tile.Y);
        if (request != previewRequest) return;
        if (result == null)
        {
            SetStatus($"{DescribeSelected()}   (no battle preview for that tile)");
            return;
        }
        pendingAttack = (result.Kind, tile);
        CombatPreview.Show(UnitOrdersModel.Combat(result));
        SetStatus($"{DescribeSelected()}   confirm the attack or cancel");
    }

    private async Task ConfirmAttack()
    {
        if (pendingAttack is not { } pending || SelectedUnitId is not { } id) return;
        GameCommand command = pending.Kind switch
        {
            "nuke" => new Nuke { UnitId = id, Target = pending.Target },
            "airsweep" => new AirSweep { UnitId = id, Target = pending.Target },
            _ => new AttackWithUnit { UnitId = id, Target = pending.Target },
        };
        var result = await session.SubmitAsync(command);
        ClearAttackPreview();
        if (!result.Accepted)
        {
            SetStatus("Attack refused: " + result.Message);
            return;
        }
        Refresh();
        SetStatus("Attack made.");
    }

    private void OnMenuEntry(MenuEntry entry)
    {
        if (entry.Kind == MenuEntryKind.Engine && entry.Action?.Confirmation is { } confirmation)
            Panel.AskConfirmation(entry, confirmation.Question, confirmation.ConfirmText);
        else if (entry.Kind == MenuEntryKind.Disband)
            Panel.AskConfirmation(entry, "Disband this unit?", "Disband");
        else Run(ExecuteEntry(entry, confirmed: false));
    }

    private async Task ExecuteEntry(MenuEntry entry, bool confirmed)
    {
        if (SelectedUnitId is not { } id) return;
        GameCommand command = entry.Kind switch
        {
            MenuEntryKind.Engine => new ExecuteUnitAction { UnitId = id, Descriptor = entry.Action.NotNull().Descriptor, Confirmed = confirmed },
            MenuEntryKind.Improve => new SetWorkerOrder { UnitId = id, Order = WorkerOrderKind.Build, Improvement = entry.Argument.NotNull() },
            MenuEntryKind.Promote => new PromoteUnit { UnitId = id, Promotion = entry.Argument.NotNull() },
            _ => new DisbandUnit { UnitId = id },
        };
        var result = await session.SubmitAsync(command);
        if (!result.Accepted)
        {
            SetStatus($"{entry.Label} refused: {result.Message}");
            return;
        }
        Refresh();
        SetStatus($"{entry.Label}: done.");
    }

    private async Task MoveSelectedTo()
    {
        var path = preview;
        if (path?.FirstStep is not { } step || SelectedUnitId is not { } unitId) return;
        var result = await session.SubmitAsync(new MoveUnit { UnitId = unitId, To = new HexCoord(step.X, step.Y) });
        if (!result.Accepted)
        {
            SetStatus("Move refused: " + result.Message);
            return;
        }
        Refresh();
        SetStatus(DescribeSelected());
    }

    /// <summary>Asks the engine for the bar's numbers and for what is still pending, and shows them; announces a turn the first time it is seen.</summary>
    private async Task RefreshHud()
    {
        var bar = await queries.TopBar();
        var pending = await queries.Pending();
        if (bar == null) return; // a refused query leaves the old bar
        var action = HudModel.NextAction(pending, snapshot.TurnStatus, session.ViewerCivId);
        Hud.Update(bar, action, HudModel.CanEndTurn(pending));
        Layout();
        var rows = NotificationsModel.Rows(snapshot.Notifications);
        Notifications.Update(bar.Turn, rows);
        if (bar.Turn != bannerTurn)
        {
            bannerTurn = bar.Turn;
            Hud.ShowBanner(HudModel.Banner(bar.Turn, bar.Year, session.ViewerCivId));
        }
        ShowNextAlert(pending, reopen: false);
        await CheckGameOver();
        CheckTutorials();
    }

    /// <summary>
    /// C024: asks the engine whether the game is over for the viewer and opens the victory/defeat screen. The ranking and the history are asked only when the screen opens;
    /// a game that hides them from the viewer simply shows none. A defeated viewer who chose to keep watching is not asked again.
    /// </summary>
    private async Task CheckGameOver()
    {
        if (resigned) return; // the resigned screen stays until the player leaves; the civ is the AI's now
        int check = ++gameOverCheck;
        var outcome = await queries.GameOver();
        if (check != gameOverCheck) return; // a newer snapshot asked meanwhile
        if (outcome == null || !GameOverModel.IsShown(outcome) || (outcome.Status == Unciv.Queries.Victory.GameOverQuery.Defeated && watchingAfterDefeat))
        {
            GameOver.Close();
            return;
        }
        if (GameOver.Visible && GameOver.Current?.Status == outcome.Status && gameOverTurn == snapshot.Turn) return; // the same screen is not rebuilt, but a new turn changes the graph
        var rankings = await queries.Rankings();
        var history = await queries.History();
        if (check != gameOverCheck) return;
        gameOverTurn = snapshot.Turn;
        GameOver.Open(GameOverModel.Build(outcome, rankings, history), civ => OwnerColour(civ));
    }

    private async Task OnGameOverChosen(GameOverButtonKind kind)
    {
        switch (kind)
        {
            case GameOverButtonKind.OneMoreTurn:
                var result = await session.SubmitAsync(new OneMoreTurn());
                if (!result.Accepted)
                {
                    SetStatus("One more turn refused: " + result.Message);
                    return;
                }
                GameOver.Close();
                Refresh();
                SetStatus("The game goes on.");
                break;
            case GameOverButtonKind.KeepWatching:
                watchingAfterDefeat = true;
                GameOver.Close();
                SetStatus("Watching the game.");
                break;
            default:
                MainMenuRequested?.Invoke();
                break;
        }
    }

    /// <summary>
    /// Opens the first pending alert that asks for a decision and was not put off; with <paramref name="reopen"/> the player asked to review, which also opens the
    /// informational and the put-off alerts, one after the other until none is left. Closes the popup when there is nothing to show.
    /// </summary>
    private void ShowNextAlert(Unciv.Queries.Empire.PendingDecisionsResult? pending, bool reopen)
    {
        if (reopen)
        {
            deferredAlerts.Clear();
            reviewingAlerts = true;
        }
        var next = AlertPopupModel.Next(pending, snapshot, deferredAlerts, reviewingAlerts);
        if (next == null) reviewingAlerts = false;
        Notifications.SetWaiting(AlertPopupModel.WaitingCount(pending, snapshot, deferredAlerts));
        if (next == null) Alerts.Close();
        else if (Alerts.Current?.Key != next.Key || !Alerts.Visible) Alerts.Open(next);
    }

    private async Task AnswerAlert(AlertPopup popup, AlertButton answer)
    {
        var result = await session.SubmitAsync(answer.Command);
        if (!result.Accepted)
        {
            SetStatus($"{popup.Title}: {answer.Label} refused: {result.Message}");
            return;
        }
        Refresh();
    }

    private void OnNotificationChosen(NotificationRow row)
    {
        switch (row.Target)
        {
            case { Kind: NotificationTargetKind.Tile } tile:
                Map.FocusOn(UnitPosition(new HexCoord(tile.X, tile.Y), 0));
                break;
            case { Kind: NotificationTargetKind.Tech } tech:
                OpenTechScreen(tech.Name);
                break;
        }
    }

    // Events arrive before the screen redraws from the new snapshot, so the presenter can still take the nodes of units that just died.
    private void OnEventsReceived(EventBatch batch) => PresentEvents(batch.Events);

    /// <summary>Queues events for the presenter (C033). Call before <see cref="Refresh"/>, while the nodes of units the events destroy are still in the table.</summary>
    public bool PresentEvents(IReadOnlyList<GameEvent> events) => Presenter.Present(events, unitNodes);

    private void OnSnapshotChanged(ClientSnapshot changed)
    {
        snapshot = changed;
        Run(RefreshHud());
    }

    private void OnHudAction(HudAction action)
    {
        switch (action.Kind)
        {
            case HudActionKind.EndTurn:
                OnNextTurn();
                break;
            case HudActionKind.NextUnit:
                SelectNextUnit();
                break;
            case HudActionKind.Decision when action.Decision is { } decision:
                Run(OpenDecision(decision));
                break;
            default:
                SetStatus($"{action.Label}: this screen is not built yet.");
                break;
        }
    }

    /// <summary>C038: takes the player to what a pending decision asks for: a screen, a picker built from the engine's answer, or the engine's own command.</summary>
    public async Task OpenDecision(DecisionKind kind)
    {
        switch (DecisionPickerModel.RouteOf(kind))
        {
            case DecisionRoute.PolicyScreen:
                OpenPolicyScreen();
                return;
            case DecisionRoute.TechScreen:
                OpenTechScreen();
                return;
            case DecisionRoute.AlertPopup:
                await ReviewAlerts();
                return;
            case DecisionRoute.DiplomacyScreen:
                OpenDiplomacyScreen();
                return;
            case DecisionRoute.NextUnit:
                SelectNextUnit();
                return;
            case DecisionRoute.AutoCommand:
                var moved = await session.SubmitAsync(new Unciv.Commands.Turn.MoveAutomatedUnits());
                SetStatus(moved.Accepted ? "Automated units moved." : "Move automated units refused: " + moved.Message);
                Refresh();
                return;
            case DecisionRoute.CityScreen:
                var pending = await queries.Pending();
                string? cityName = pending?.Decisions.FirstOrDefault(it => it.Kind == nameof(DecisionKind.CityProduction))?.Subject;
                var city = snapshot.Cities.FirstOrDefault(it => it.Own != null && it.Name == cityName);
                if (city == null) SetStatus("Pick construction: no city is waiting.");
                else OpenCityScreen(city);
                return;
        }
        ShowCity(null);
        if (Diplomacy.Visible) Diplomacy.Close();
        if (Policies.Visible) Policies.Close();
        if (Tech.Visible) Tech.Close();
        var spec = await BuildPicker(kind);
        if (spec == null)
        {
            SetStatus($"{kind}: the engine offers nothing to choose now.");
            return;
        }
        Picker.Open(spec);
    }

    private async Task<PickerSpec?> BuildPicker(DecisionKind kind)
    {
        var ruleset = session.Ruleset;
        string DescribeUnit(string name) => ruleset.Units.TryGetValue(name, out var unit) ? unit.GetShortDescription() : "";
        string DescribeBelief(string name) => ruleset.Beliefs.TryGetValue(name, out var belief) ? string.Join("\n", belief.Uniques) : "";
        switch (kind)
        {
            case DecisionKind.FreeGreatPerson:
                var people = await queries.GreatPeople();
                return people == null ? null : DecisionPickerModel.FreeGreatPerson(people, DescribeUnit);
            case DecisionKind.MoveSpies:
                var spies = await queries.SpyDestinations();
                return spies == null ? null : DecisionPickerModel.MoveSpies(spies);
            case DecisionKind.DiplomaticVote:
                var vote = await queries.DiplomaticVote();
                return vote == null ? null : DecisionPickerModel.DiplomaticVote(vote, id => snapshot.Civs.FirstOrDefault(it => it.CivId == id)?.Name ?? id);
            default:
                var religion = await queries.Religion();
                if (religion == null) return null;
                return kind switch
                {
                    DecisionKind.Pantheon => DecisionPickerModel.Pantheon(religion, religion.State == "Pantheon", DescribeBelief),
                    DecisionKind.FoundReligion => DecisionPickerModel.FoundReligion(religion, DescribeBelief),
                    _ => DecisionPickerModel.ChooseBeliefs(religion, kind == DecisionKind.ReformReligion, DescribeBelief),
                };
        }
    }

    private async Task OpenPromotionPicker()
    {
        if (SelectedUnitId is not { } id || !unitNodes.TryGetValue(id, out var node)) return;
        var choices = await queries.Promotions(id);
        var spec = choices == null ? null : DecisionPickerModel.Promotion(choices, node.Unit.Name);
        if (spec == null) SetStatus("This unit cannot be promoted now.");
        else Picker.Open(spec);
    }

    private async Task ConfirmPicker(PickerSpec spec, IReadOnlyList<GameCommand> commands)
    {
        foreach (var command in commands)
        {
            var result = await session.SubmitAsync(command);
            if (!result.Accepted)
            {
                SetStatus($"{spec.Title} refused: {result.Message}");
                Refresh();
                return;
            }
        }
        Picker.Close();
        Refresh();
        SetStatus($"{spec.Title}: done.");
    }

    private async Task DismissPicker(PickerSpec spec)
    {
        if (spec.Dismiss != null) await session.SubmitAsync(spec.Dismiss);
        Picker.Close();
        Refresh();
    }

    /// <summary>The snapshot does not say which unit is "due" (the engine decides that), so the cycle visits the viewer's units that could still act: movement left, no standing order, not fortified.</summary>
    private void SelectNextUnit()
    {
        var waiting = unitNodes.Values
            .Where(it => it.IsOwn && it.Unit.Own is { CurrentMovement: > 0f, Action: null } && !it.Unit.IsFortified)
            .Select(it => it.Unit.Id).ToList();
        if (HudModel.NextDueUnit(waiting, SelectedUnitId) is not { } id)
        {
            SetStatus("No unit is waiting for orders.");
            return;
        }
        var node = unitNodes[id];
        SelectUnit(node);
        Map.FocusOn(UnitPosition(new HexCoord(node.Unit.X, node.Unit.Y), 0));
    }

    private async Task ReviewAlerts() => ShowNextAlert(await queries.Pending(), reopen: true);

    private void OnNextTurn() => Run(EndTurn());

    private async Task EndTurn()
    {
        var outcome = await session.EndTurnAsync();
        Refresh();
        SetStatus(outcome.Accepted
            ? $"Turn {snapshot.Turn.ToString(CultureInfo.InvariantCulture)}: {outcome.AiTurnsRun.ToString(CultureInfo.InvariantCulture)} civ(s) played, {outcome.Events.Count.ToString(CultureInfo.InvariantCulture)} event(s)."
            : "Cannot end the turn: " + outcome.Message);
    }

    /// <summary>Observes a UI-triggered Task: a transport failure of the session (IGameSession error contract) is logged and shown instead of vanishing.</summary>
    private async void Run(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception e)
        {
            GD.PushError("Session call failed: " + e);
            SetStatus("Connection problem: " + e.Message);
        }
    }

    private void SetStatus(string text) => statusLabel.Text = text.TrText();
}
