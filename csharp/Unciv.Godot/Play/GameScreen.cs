using System.Globalization;
using Godot;
using Unciv.Assets;
using Unciv.Commands;
using Unciv.Commands.Events;
using Unciv.Commands.Turn;
using Unciv.Commands.Units;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Localization;
using Unciv.GodotClient.Map;
using Unciv.GodotClient.Settings;
using Unciv.GodotClient.Ui;
using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using Unciv.Models;
using Unciv.Models.Translations;
using Unciv.Net.Visibility;
using Unciv.Queries.Empire;
using Unciv.Queries.Units;
using Unciv.Utils;

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
    private PanelContainer statusPanel = null!; // JUSTIFY: C068 assigned in _Ready before any use
    private string statusText = "";
    private int statusFrames;
    private const int StatusFrames = 240; // frames, not seconds: captures must not depend on machine speed
    private int bannerTurn = -1;
    private int rivalsTurn = -1;
    private int rivalsRequest;
    private ClientSnapshot snapshot = null!; // JUSTIFY: C010 assigned by Refresh in Start
    private int previewRequest;
    private PathPreviewResult? preview;
    private bool mapLoaded;
    private int selectionRequest;
    private HashSet<(int X, int Y)> attackTiles = [];
    private (string Kind, HexCoord Target)? pendingAttack;

    public MapView Map { get; private set; } = null!; // JUSTIFY: C010 assigned in _Ready before any use

    public WorldHud World { get; private set; } = null!; // JUSTIFY: C067 assigned in _Ready before any use

    public HudView Hud { get; private set; } = null!; // JUSTIFY: C011 assigned in _Ready before any use

    public MinimapView Minimap { get; private set; } = null!; // JUSTIFY: C014 assigned in _Ready before any use

    /// <summary>Cities, borders, roads and improvements drawn on the map (C041).</summary>
    public MapOverlayLayer Overlays { get; private set; } = null!; // JUSTIFY: C041 assigned in _Ready before any use

    public UnitPanelView Panel { get; private set; } = null!; // JUSTIFY: C012 assigned in _Ready before any use

    public CombatPreviewView CombatPreview { get; private set; } = null!; // JUSTIFY: C012 assigned in _Ready before any use

    public TileOverlay ReachableOverlay { get; private set; } = null!; // JUSTIFY: C012 assigned in _Ready before any use

    /// <summary>C088: the work radius of the city site under the pointer while a unit that can found a city is selected.</summary>
    public TileOverlay SiteOverlay { get; private set; } = null!; // JUSTIFY: C088 assigned in _Ready before any use

    private bool foundsCities;

    public TileOverlay AttackOverlay { get; private set; } = null!; // JUSTIFY: C012 assigned in _Ready before any use

    /// <summary>C033: plays the movement and combat events of every command on the map. Off until a host turns <see cref="CombatPresenter.Enabled"/> on.</summary>
    public CombatPresenter Presenter { get; } = new() { Name = "Presenter" };

    public int? SelectedUnitId { get; private set; }

    /// <summary>C013: the clickable notification list.</summary>
    public NotificationListView Notifications { get; private set; } = null!; // JUSTIFY: C013 assigned in _Ready before any use

    /// <summary>C013: the modal popup for the first pending alert.</summary>
    public AlertPopupView Alerts { get; private set; } = null!; // JUSTIFY: C013 assigned in _Ready before any use

    /// <summary>C039: the modal popup for the first incoming trade or peace proposal.</summary>
    public TradeProposalView Proposals { get; private set; } = null!; // JUSTIFY: C039 assigned in _Ready before any use

    /// <summary>C026: the tutorial popup; it shows only while the screen follows client settings (<see cref="BindSettings"/>) that have tutorials on.</summary>
    public TutorialPopupView Tutorials { get; private set; } = null!; // JUSTIFY: C026 assigned in _Ready before any use

    /// <summary>C053: the World Congress result, above the world and below the victory screen.</summary>
    public VoteResultScreen VoteResult { get; private set; } = null!; // JUSTIFY: C053 assigned in _Ready before any use

    /// <summary>C024: the victory/defeat screen, above everything else.</summary>
    public GameOverScreen GameOver { get; private set; } = null!; // JUSTIFY: C024 assigned in _Ready before any use

    /// <summary>Raised when the player leaves the finished (or any) game from the game-over screen or the menu; the composition root shows the main menu.</summary>
    public event Action? MainMenuRequested;

    /// <summary>C044: the in-game menu (Escape or the Menu button).</summary>
    public GameMenuView GameMenu { get; private set; } = null!; // JUSTIFY: C044 assigned in _Ready before any use

    /// <summary>C044: the menu's "Victory status" page.</summary>
    public VictoryStatusView VictoryStatus { get; private set; } = null!; // JUSTIFY: C044 assigned in _Ready before any use

    /// <summary>C046: the developer console (the console hotkey); it does not block the map, so tiles and units can be selected for the next line.</summary>
    public ConsoleView ConsolePopup { get; private set; } = null!; // JUSTIFY: C046 assigned in _Ready before any use

    public Button MenuButton => World.Menu;

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

    /// <summary>C047: the menu of the AutoPlay button.</summary>
    public AutoPlayMenuView AutoPlayMenu { get; private set; } = null!; // JUSTIFY: C047 assigned in _Ready before any use

    /// <summary>C047: the running AutoPlay as the client tracks it, null while idle.</summary>
    public AutoPlayRun? AutoPlay { get; private set; }

    /// <summary>C047: the last finished run's status line (also shown in the status label).</summary>
    public string? AutoPlayResult { get; private set; }

    /// <summary>C047: frames to wait between two auto-played turns, so the player sees each turn and can press Stop; scenarios raise it to capture a run midway.</summary>
    public int AutoPlayFramesPerTurn { get; set; } = 1;

    private int autoPlayGeneration;

    /// <summary>C040: the overlay shown while End turn is processed off the UI thread.</summary>
    public NextTurnProgressView TurnOverlay { get; private set; } = null!; // JUSTIFY: C040 assigned in _Ready before any use

    /// <summary>C040: True from pressing Next turn until the host answered; input is ignored meanwhile.</summary>
    public bool IsEndingTurn => turnCancellation != null;

    /// <summary>C036: the multiplayer turn bar (who ended, deadline, connection marks, toasts); hidden unless the session is a simultaneous game (<see cref="ITurnRoster"/>).</summary>
    public TurnBarView TurnBar { get; private set; } = null!; // JUSTIFY: C036 assigned in _Ready before any use

    /// <summary>C045: the players-and-chat panel of a hosted game (the Players button toggles it); the button is hidden unless the session is an <see cref="IGameSocial"/>.</summary>
    public SocialView Social { get; private set; } = null!; // JUSTIFY: C045 assigned in _Ready before any use

    public Button PlayersButton => World.Players;

    private IGameSocial? social;

    /// <summary>C036: the clock the countdown reads (Unix ms); wall time in the game, fixed in scenarios.</summary>
    public Func<long> NowMs { get; set; } = static () => (long)(Time.GetUnixTimeFromSystem() * 1000.0);

    /// <summary>C036: true while the host resolves the round of a simultaneous game.</summary>
    public bool IsResolving => snapshot?.TurnStatus.Phase == "Resolving" && session is ITurnRoster;

    private string? shownCountdown;

    /// <summary>The turn bar sits centred below the two rows of screen buttons, so it covers none of them.</summary>
    private float TurnBarTop() => World.NavBottom + HudMargin;

    private CancellationTokenSource? turnCancellation;

    /// <summary>
    /// C029: raised, with the civ id, when End turn finished and the next turn belongs to another human on this device (hotseat). The listener must <see cref="Conceal"/> the screen at once (nothing is
    /// drawn from the finished turn first), show the pass-the-device cover and later <see cref="Start"/> the screen with the other human's session and <see cref="Reveal"/> it.
    /// </summary>
    public event Action<string>? HandOverRequested;

    /// <summary>C029: the session the screen currently shows (a hotseat owner swaps it for the next human's with <see cref="Start"/>).</summary>
    public IGameSession? Session => session;

    /// <summary>C029: true between <see cref="Conceal"/> and <see cref="Reveal"/>: nothing of the game is drawn and no input reaches it.</summary>
    public bool IsConcealed { get; private set; }

    /// <summary>
    /// C029: hides everything the previous player could see (map, units, overlays, HUD, panels, popups) and stops the screen taking input. Used while the device changes hands in a hotseat game;
    /// the state itself is replaced by the next <see cref="Start"/>. Safe to call repeatedly.
    /// </summary>
    public void Conceal()
    {
        IsConcealed = true;
        turnCancellation?.Cancel();
        Alerts.Close();
        Proposals.Close();
        Picker.Close();
        GameMenu.Close();
        AutoPlayMenu.Close();
        ConsolePopup.Close();
        Tutorials.Close();
        VictoryStatus.Close();
        TurnOverlay.Close();
        ClearPreview();
        SelectedUnitId = null;
        ClearSelectionUi();
        SetStatus("", quiet: true);
        Visible = false;
        ProcessMode = ProcessModeEnum.Disabled;
        foreach (var layer in WorldLayers()) layer.Visible = false;
    }

    /// <summary>C029: shows the screen again after <see cref="Conceal"/> (the caller started it with the next player's session first).</summary>
    public void Reveal()
    {
        IsConcealed = false;
        foreach (var layer in WorldLayers()) layer.Visible = true;
        ProcessMode = ProcessModeEnum.Inherit;
        Visible = true;
    }

    // The 3D parts do not follow the screen's own visibility (a Node3D under a Control is a separate branch), so they are hidden one by one.
    private IEnumerable<Node3D> WorldLayers() => [Map, Overlays, unitLayer, dotLayer, ReachableOverlay, AttackOverlay, SiteOverlay];

    private bool watchingAfterDefeat;
    private int gameOverCheck;
    private int gameOverTurn = -1;

    /// <summary>C038: the modal decision picker (free great person, religion, spies, vote, promotion).</summary>
    public DecisionPickerView Picker { get; private set; } = null!; // JUSTIFY: C038 assigned in _Ready before any use

    /// <summary>C038: opens the promotion picker for the selected unit while the engine offers a promotion.</summary>
    public Button PromoteButton { get; } = new() { Name = "PromoteButton", Text = "Promote".Tr(), Visible = false };

    private readonly HashSet<string> deferredAlerts = [];
    private readonly HashSet<string> toastedAlerts = [];
    private readonly HashSet<string> deferredProposals = [];
    private readonly OfferTracker offerTracker = new();
    private bool reviewingAlerts;

    /// <summary>The city whose screen (<see cref="City"/>) is open: the hook that tells which city.</summary>
    public CityDto? OpenCity { get; private set; }

    /// <summary>The city screen (C017); created by <see cref="Start"/>.</summary>
    public CityScreen City { get; private set; } = null!; // JUSTIFY: C017 assigned in Start before any use

    public bool IsCityPanelVisible => City.Visible;

    /// <summary>C021: the diplomacy screen, opened by the Diplomacy button.</summary>
    public DiplomacyScreen Diplomacy { get; private set; } = null!; // JUSTIFY: C021 assigned in Start before any use

    public bool IsDiplomacyVisible => Diplomacy.Visible;

    public Button DiplomacyButton => World.Diplomacy;

    /// <summary>C020: the policy screen, opened by the Policies button or the "Pick a policy" decision.</summary>
    public PolicyScreen Policies { get; private set; } = null!; // JUSTIFY: C020 assigned in Start before any use

    public bool IsPolicyScreenVisible => Policies.Visible;

    public Button PolicyButton => World.Policies;

    /// <summary>Opens the policy screen, closing the city and diplomacy screens.</summary>
    public void OpenPolicyScreen()
    {
        ShowCity(null);
        if (Trade.Visible) Trade.Close();
        if (Diplomacy.Visible) Diplomacy.Close();
        if (Tech.Visible) Tech.Close();
        Deselect();
        SetStatus("Policies", quiet: true);
        Run(Policies.OpenAsync());
        ShowTutorial(TutorialTrigger.CultureAndPolicies);
    }

    /// <summary>Opens the diplomacy screen on the civ with this id (the first met civ when null), closing the city screen.</summary>
    public void OpenDiplomacyScreen(string? civId = null)
    {
        ShowCity(null);
        if (Policies.Visible) Policies.Close();
        Deselect();
        SetStatus("Diplomacy", quiet: true);
        if (Tech.Visible) Tech.Close();
        if (Overview.Visible) Overview.Close();
        if (Espionage.Visible) Espionage.Close();
        if (Trade.Visible) Trade.Close();
        Run(Diplomacy.OpenAsync(civId));
    }

    /// <summary>C023: the empire overview, opened by the Overview button.</summary>
    public OverviewScreen Overview { get; private set; } = null!; // JUSTIFY: C023 assigned in Start before any use

    public bool IsOverviewVisible => Overview.Visible;

    public Button OverviewButton => World.Overview;

    /// <summary>Opens the overview on a tab (the last shown when null), closing the other full screens.</summary>
    public void OpenOverviewScreen(string? tab = null)
    {
        ShowCity(null);
        if (Diplomacy.Visible) Diplomacy.Close();
        if (Tech.Visible) Tech.Close();
        Deselect();
        if (Trade.Visible) Trade.Close();
        if (Espionage.Visible) Espionage.Close();
        SetStatus("Overview", quiet: true);
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

    public Button TradeButton => World.Trade;

    /// <summary>Opens the trade screen on the civ with this id (the first met major civ when null), with the offer of <paramref name="stageFrom"/> staged as a counter proposal, closing the other screens.</summary>
    public void OpenTradeScreen(string? civId = null, PendingTradeDto? stageFrom = null)
    {
        ShowCity(null);
        if (Diplomacy.Visible) Diplomacy.Close();
        if (Tech.Visible) Tech.Close();
        if (Overview.Visible) Overview.Close();
        if (Espionage.Visible) Espionage.Close();
        Deselect();
        SetStatus("Trade", quiet: true);
        Run(Trade.OpenAsync(civId, stageFrom));
    }

    /// <summary>C027: the Civilopedia, opened by the Civilopedia button.</summary>
    public PediaScreen Pedia { get; private set; } = null!; // JUSTIFY: C027 assigned in Start before any use

    public bool IsPediaVisible => Pedia.Visible;

    public Button PediaButton => World.Civilopedia;

    /// <summary>Opens the Civilopedia on a link (<c>Unit/Warrior</c>), or where it was last left; it covers whatever screen is open and Close returns to it.</summary>
    public void OpenPediaScreen(string? link = null)
    {
        Tooltips.Unpoint();
        Pedia.Open(link);
    }

    /// <summary>C051: the espionage screen, opened by the Espionage button.</summary>
    public EspionageScreen Espionage { get; private set; } = null!; // JUSTIFY: C051 assigned in Start before any use

    public bool IsEspionageVisible => Espionage.Visible;

    public Button EspionageButton => World.Espionage;

    /// <summary>Opens the espionage screen, closing the other full screens.</summary>
    public void OpenEspionageScreen()
    {
        ShowCity(null);
        if (Diplomacy.Visible) Diplomacy.Close();
        if (Tech.Visible) Tech.Close();
        if (Policies.Visible) Policies.Close();
        if (Overview.Visible) Overview.Close();
        if (Trade.Visible) Trade.Close();
        Deselect();
        SetStatus("Espionage", quiet: true);
        Run(Espionage.OpenAsync());
    }

    /// <summary>"Assign spies" on the espionage screen: the multi-spy picker over it, built from the engine's answer. The screen is refreshed once the picker is answered.</summary>
    private async Task OpenSpyAssignment()
    {
        var spies = await queries.SpyDestinations();
        if (spies == null)
        {
            SetStatus("Espionage is not enabled in this game.");
            return;
        }
        Picker.Open(DecisionPickerModel.MoveSpies(spies));
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
        SiteOverlay.Clear();
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
        if (!foundsCities) return;
        // C088: with a settler selected the tooltip says what a city here would get (it replaces the tile's own text, which would cover the map), and the overlay marks its work radius
        var site = await queries.CitySite(coord.X, coord.Y);
        if (HoveredTile != coord || !foundsCities || site == null) return;
        HoverTip = TooltipModel.ToBbcode(TooltipModel.CitySite(site), text => text.Tr());
        SiteOverlay.Show([.. site.Tiles.Select(it => new HexCoord(it.X, it.Y))], TileTopAt);
    }

    /// <summary>C019: the tech screen, opened by the Technology button.</summary>
    public TechScreen Tech { get; private set; } = null!; // JUSTIFY: C019 assigned in Start before any use

    public bool IsTechVisible => Tech.Visible;

    public Button TechButton => World.Technology;

    /// <summary>Opens the tech screen on a tech (the current research when null), closing the city and diplomacy screens.</summary>
    public void OpenTechScreen(string? tech = null)
    {
        ShowCity(null);
        if (Diplomacy.Visible) Diplomacy.Close();
        if (Policies.Visible) Policies.Close();
        if (Overview.Visible) Overview.Close();
        if (Espionage.Visible) Espionage.Close();
        if (Trade.Visible) Trade.Close();
        Deselect();
        SetStatus("Technology", quiet: true);
        Run(Tech.OpenAsync(tech));
    }

    private ClientSettingsService? boundSettings;
    /// <summary>C026: false keeps tutorial popups away (scenarios that are about something else turn them off; the game never does).</summary>
    public bool TutorialsEnabled { get; set; } = true;

    /// <summary>
    /// C054: shows the floating tutorial task card (needs <see cref="BindSettings"/> with Show tutorials on). The game turns it on; scenarios that are about
    /// something else leave it off so their pictures do not change.
    /// </summary>
    public bool TaskCardEnabled { get; set; }

    /// <summary>C054: the floating card of the current tutorial task.</summary>
    public TutorialTaskCard TaskCard { get; private set; } = null!; // JUSTIFY: C054 assigned in _Ready before any use

    private readonly HashSet<string> reportedTasks = [];

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
        if (!TutorialsEnabled || boundSettings == null || !boundSettings.Current.ShowTutorials || GameOver.Visible || VoteResult.Visible) return;
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
        Overview?.BindSettings(settings);
        ApplySettings(settings.Current);
        if (session != null) CheckTutorials(); // the first look at the game came before the settings were known
    }

    private void ApplySettings(ClientSettings settings)
    {
        if (session != null && snapshot != null) Run(RefreshTaskCard()); // C054: Show tutorials may have changed
        hotkeys = settings.KeyBindings;
        Map.KeyBindings = settings.KeyBindings;
        Map.ScrollSpeed = settings.ScrollSpeed;
        Map.EdgePan = settings.EdgePan;
        Map.ShowResources = settings.ShowResources;
        Overlays.ShowYields = settings.ShowYields;
        Hud.ShowAutoPlayButton(settings.AutoPlay.ShowButton);
        if (!settings.AutoPlay.ShowButton)
        {
            AutoPlayMenu.Close();
            if (AutoPlay != null) Run(StopAutoPlayAsync());
        }
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
        if (ConsolePopup.Visible) return false; // keys belong to the console's input while it is open
        if (IsEndingTurn || City.Visible || Diplomacy.Visible || Tech.Visible || Policies.Visible || Overview.Visible || Espionage.Visible || GameOver.Visible || VoteResult.Visible || Alerts.Visible || Proposals.Visible) return false;
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
            case HotkeyAction.OpenConsole:
                OpenConsole();
                return true;
            case HotkeyAction.OpenPedia:
                if (Pedia.Visible) Pedia.Close();
                else OpenPediaScreen();
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
        if (IsEndingTurn) return false; // C040: nothing opens while the turn is processed
        if (AutoPlayMenu.Visible)
        {
            AutoPlayMenu.Close();
            return true;
        }
        if (ConsolePopup.Visible) return ConsolePopup.Cancel();
        if (GameMenu.Visible) return GameMenu.Cancel();
        if (VictoryStatus.Visible)
        {
            VictoryStatus.Close();
            return true;
        }
        if (City.Visible || Diplomacy.Visible || Tech.Visible || Policies.Visible || Trade.Visible || Overview.Visible || Espionage.Visible || Alerts.Visible || Proposals.Visible || GameOver.Visible || VoteResult.Visible) return false;
        OpenGameMenu();
        return true;
    }

    /// <summary>C046: opens the developer console with the history and "Keep open" choice from the client settings (the screen's own memory when no settings are bound).</summary>
    public void OpenConsole()
    {
        if (session == null) return;
        ConsolePopup.Open(boundSettings?.Current.ConsoleHistory ?? ConsolePopup.History, boundSettings?.Current.KeepConsoleOpen ?? ConsolePopup.KeepOpen);
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
                ReportTaskDone(TutorialTaskModel.OpenOptions);
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

    public string StatusText => statusText;

    /// <summary>C068: whether the transient message line is on screen.</summary>
    public bool StatusShown => statusPanel.Visible;

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
        SiteOverlay = new TileOverlay(PlaceholderPalette.SiteTint) { Name = "SiteOverlay" };
        AddChild(ReachableOverlay);
        AddChild(AttackOverlay);
        AddChild(SiteOverlay);

        statusLabel = new Label { Name = "StatusLabel" };
        statusPanel = new PanelContainer { Name = "StatusPanel", Visible = false, MouseFilter = MouseFilterEnum.Ignore, ThemeTypeVariation = UiVariants.PanelTooltip };
        statusLabel.MouseFilter = MouseFilterEnum.Ignore;
        statusPanel.AddChild(statusLabel);
        AddChild(statusPanel);
        Hud = new HudView { Name = "Hud" };
        World = new WorldHud();
        AddChild(World);
        World.Requested += OnNav;
        World.Rivals.ExpandedChanged += _ => Run(RefreshRivals(snapshot.Turn, force: true));
        Hud.ActionRequested += OnHudAction;
        Hud.EndTurnRequested += OnNextTurn;
        Hud.AutoPlayRequested += OnAutoPlayButton;
        AutoPlayMenu = new AutoPlayMenuView();
        AddChild(AutoPlayMenu);
        AutoPlayMenu.Chosen += kind => Run(StartAutoPlayAsync(kind));
        Notifications = new NotificationListView();
        Notifications.Chosen += OnNotificationChosen;
        Notifications.ReviewRequested += () => Run(ReviewAlerts());
        TaskCard = new TutorialTaskCard();
        AddChild(TaskCard);
        TaskCard.ChoiceChosen += (eventName, index) => Run(ChooseTaskChoice(eventName, index));
        Panel = new UnitPanelView();
        AddChild(Panel);
        Panel.EntryChosen += OnMenuEntry;
        Panel.Confirmed += entry => Run(ExecuteEntry(entry, confirmed: true));
        CombatPreview = new CombatPreviewView();
        AddChild(CombatPreview);
        CombatPreview.ConfirmRequested += () => Run(ConfirmAttack());
        CombatPreview.CancelRequested += ClearAttackPreview;
        Minimap = new MinimapView { Name = "Minimap" };
        Minimap.Attach(Map);
        World.Mount(Hud, Notifications, Minimap);
        Panel.SetPromoteChip(PromoteButton);
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
        Proposals = new TradeProposalView();
        AddChild(Proposals);
        Proposals.Answered += (popup, answer) => Run(AnswerProposal(popup, answer));
        Proposals.Deferred += popup =>
        {
            deferredProposals.Add(popup.Key);
            Proposals.Close();
            Run(RefreshHud());
        };
        TurnBar = new TurnBarView();
        AddChild(TurnBar);
        Social = new SocialView();
        AddChild(Social);
        Social.ChatSubmitted += text => Run(SendChat(text));
        Social.KickRequested += civ => Run(Admin(civ, null));
        Social.RebindRequested += (civ, key) => Run(Admin(civ, key));
        TurnOverlay = new NextTurnProgressView();
        AddChild(TurnOverlay);
        VoteResult = new VoteResultScreen();
        AddChild(VoteResult);
        VoteResult.Continued += () => Run(AcknowledgeVoteResult());
        GameOver = new GameOverScreen();
        AddChild(GameOver);
        Tutorials = new TutorialPopupView();
        AddChild(Tutorials);
        Tutorials.Dismissed += DismissTutorial;
        GameOver.Chosen += kind => Run(OnGameOverChosen(kind));
        VictoryStatus = new VictoryStatusView();
        AddChild(VictoryStatus);
        GameMenu = new GameMenuView();
        AddChild(GameMenu);
        GameMenu.Chosen += kind => Run(OnMenuChosen(kind));
        ConsolePopup = new ConsoleView();
        AddChild(ConsolePopup);
        ConsolePopup.StateChanged += (history, keepOpen) => boundSettings?.Update(s => s with { ConsoleHistory = [.. history], KeepConsoleOpen = keepOpen });
        GetViewport().SizeChanged += Layout;
        Layout();

        Map.TileClicked += OnTileClicked;
        Map.TileRightClicked += OnTileRightClicked;
    }

    // Sized from the viewport, not from anchors: the screen may sit under a parent Control that has no size (the scenario runner).
    private void Layout()
    {
        Size = GetViewportRect().Size;
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
        if (Policies != null)
        {
            Policies.Position = new Vector2(0f, Hud.TopBarHeight);
            Policies.Size = new Vector2(Size.X, Size.Y - Hud.TopBarHeight);
        }
        if (Pedia != null)
        {
            Pedia.Position = new Vector2(0f, Hud.TopBarHeight);
            Pedia.Size = new Vector2(Size.X, Size.Y - Hud.TopBarHeight);
        }
        if (Espionage != null)
        {
            Espionage.Position = new Vector2(0f, Hud.TopBarHeight);
            Espionage.Size = new Vector2(Size.X, Size.Y - Hud.TopBarHeight);
        }
        if (statusPanel.Visible) LayoutStatus();
        LayoutSocial();
        Alerts.LayoutIn(Size);
        Proposals.LayoutIn(Size);
        VoteResult.LayoutIn(Size);
        GameOver.LayoutIn(Size);
        VictoryStatus.LayoutIn(Size);
        GameMenu.LayoutIn(Size);
        AutoPlayMenu.Size = AutoPlayMenu.GetCombinedMinimumSize();
        var autoPlayRect = Hud.AutoPlayButton?.GetGlobalRect() ?? default; // the dock's own layout places the button; the menu opens above it, right-aligned
        AutoPlayMenu.Position = new Vector2(autoPlayRect.End.X - AutoPlayMenu.Size.X, autoPlayRect.Position.Y - AutoPlayMenu.Size.Y - HudMargin / 2f);
        ConsolePopup.LayoutIn(Size);
        Picker.LayoutIn(Size);
        Tutorials.LayoutIn(Size);
        TaskCard.LayoutIn(Size, World.NavBottom + HudMargin);
        TurnBar.LayoutIn(Size.X, TurnBarTop());
        TurnOverlay.LayoutIn(Size);
        Panel.LayoutIn(Size);
        CombatPreview.LayoutIn(Size);
    }

    public override void _ExitTree()
    {
        GetViewport().SizeChanged -= Layout;
        turnCancellation?.Cancel(); // a turn still being processed stops after the civ it is playing; nothing waits for it
        if (session != null)
        {
            session.SnapshotChanged -= OnSnapshotChanged;
            session.EventsReceived -= OnEventsReceived;
            if (session is ITurnRoster roster) roster.RosterChanged -= RefreshTurnBar;
            BindSocial(null);
        }
    }

    /// <summary>Connects the screen to a game and draws it, centred on the human's first unit. Call after the screen entered the tree; calling it again switches to another game (C016: load).</summary>
    public void Start(IGameSession gameSession, AssetRegistry registry)
    {
        if (session != null)
        {
            session.SnapshotChanged -= OnSnapshotChanged;
            session.EventsReceived -= OnEventsReceived;
            if (session is ITurnRoster previous) previous.RosterChanged -= RefreshTurnBar;
            BindSocial(null);
            Presenter.Reset();
            turnCancellation?.Cancel();
            SelectedUnitId = null;
            mapLoaded = false;
            bannerTurn = -1;
            rivalsTurn = -1;
        }
        session = gameSession;
        assets = registry;
        queries = new GameQueries(session);
        ui?.Unbind();
        var theme = new UiTheme(registry);
        ui = theme;
        World.Init(theme);
        theme.LayoutChanged += World.OnLayoutChanged;
        theme.Bind(GetViewport());
        TurnBar.Init(theme);
        Social.Init(theme);
        Alerts.Init(theme);
        Proposals.Init(theme);
        Proposals.Close();
        VoteResult.Init(theme);
        VoteResult.Close();
        GameOver.Init(theme);
        Tutorials.Init(theme);
        Tutorials.Close();
        TaskCard.Init(theme);
        reportedTasks.Clear();
        tutorialQueue = new TutorialQueue(session.Ruleset);
        GameOver.Close();
        VictoryStatus.Init(theme);
        VictoryStatus.Close();
        GameMenu.Init(theme);
        GameMenu.Close();
        AutoPlayMenu.Init(theme);
        AutoPlayMenu.Close();
        autoPlayGeneration++; // a run of the previous game ends with it
        AutoPlay = null;
        Hud.SetAutoPlay(null);
        ConsolePopup.Init(theme);
        ConsolePopup.Close();
        ConsolePopup.Bind(session, queries, () => (Map.Selected, SelectedUnitId), Run);
        ConsolePopup.LayoutIn(Size);
        resigned = false;
        TurnOverlay.Init(theme);
        TurnOverlay.Close();
        watchingAfterDefeat = false;
        Picker.Init(theme);
        statusPanel.Theme = theme.Theme;
        deferredAlerts.Clear();
        toastedAlerts.Clear();
        deferredProposals.Clear();
        offerTracker.Reset();
        reviewingAlerts = false;
        City = new CityScreen(session, theme);
        AddChild(City);
        City.CommandAccepted += Refresh;
        City.CommandSent += command =>
        {
            if (TutorialTaskModel.FromCommand(command) is { } task) ReportTaskDone(task);
        };
        City.Closed += () => ShowCity(null);
        if (Diplomacy != null)
        {
            RemoveChild(Diplomacy);
            Diplomacy.QueueFree();
        }
        Diplomacy = new DiplomacyScreen(session, theme);
        AddChild(Diplomacy);
        Diplomacy.CommandAccepted += Refresh;
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
        Trade.Closed += () => Run(RefreshHud());
        Trade.Retracting += offerTracker.NoteRetracted;
        Tech = new TechScreen(session, theme);
        AddChild(Tech);
        Tech.CommandAccepted += Refresh;
        if (Policies != null)
        {
            RemoveChild(Policies);
            Policies.QueueFree();
        }
        Policies = new PolicyScreen(session, theme);
        AddChild(Policies);
        Policies.CommandAccepted += Refresh;
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
        if (boundSettings != null) Overview.BindSettings(boundSettings);
        if (Tooltips != null)
        {
            RemoveChild(Tooltips);
            Tooltips.QueueFree();
        }
        Tooltips = new TooltipHost(theme) { Name = "Tooltips", TopInset = () => World.BarBottom };
        HoveredTile = null;
        HoverTip = "";
        if (Pedia != null)
        {
            RemoveChild(Pedia);
            Pedia.QueueFree();
        }
        Pedia = new PediaScreen(session.Ruleset, theme, Tooltips);
        AddChild(Pedia);
        if (Espionage != null)
        {
            RemoveChild(Espionage);
            Espionage.QueueFree();
        }
        Espionage = new EspionageScreen(session, theme);
        AddChild(Espionage);
        Espionage.CommandAccepted += Refresh;
        Espionage.AssignRequested += () => Run(OpenSpyAssignment());
        Panel.Init(theme);
        CombatPreview.Init(theme);
        MoveChild(Picker, -1);
        MoveChild(Proposals, -1);
        MoveChild(Alerts, -1); // the popups sit above every screen opened above
        MoveChild(VoteResult, -1);
        MoveChild(GameOver, -1);
        MoveChild(VictoryStatus, -1);
        MoveChild(GameMenu, -1);
        MoveChild(AutoPlayMenu, -1);
        MoveChild(ConsolePopup, -1);
        MoveChild(Tutorials, -1);
        AddChild(Tooltips); // above everything, so a tip is never covered by a screen
        MoveChild(TurnOverlay, -1); // above every screen: while the turn is processed nothing underneath may be used
        ReachableOverlay.Setup(registry);
        AttackOverlay.Setup(registry);
        SiteOverlay.Setup(registry);
        session.EventsReceived += OnEventsReceived;
        Presenter.Bind(id => unitNodes.GetValueOrDefault(id), coord => UnitPosition(coord, 0));
        session.SnapshotChanged += OnSnapshotChanged; // commands made elsewhere (another screen, a scenario) change what the HUD shows
        if (session is ITurnRoster roster) roster.RosterChanged += RefreshTurnBar;
        BindSocial(session as IGameSocial);
        Refresh();
        RefreshTurnBar();
        var first = snapshot.Units.FirstOrDefault(it => it.Own != null);
        if (first != null) Map.FocusOn(MapLayout.TileTop(first.X, first.Y));
        SetStatus("Click one of your units.", quiet: true);
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
        SetStatus(DescribeSelected(), quiet: true);
        Run(RefreshSelection());
    }

    /// <summary>Opens (or, with null, closes) the city screen for a city of the snapshot.</summary>
    private void ShowCity(CityDto? city)
    {
        if (city != null && Diplomacy.Visible) Diplomacy.Close();
        if (city != null && Tech.Visible) Tech.Close();
        if (city != null && Policies.Visible) Policies.Close();
        if (city != null && Overview.Visible) Overview.Close();
        if (city != null && Espionage.Visible) Espionage.Close();
        if (city != null && Trade.Visible) Trade.Close();
        if (city != null) Deselect(); // C012: the unit panel gives way to the city screen
        OpenCity = city;
        if (city == null)
        {
            if (City.Visible) City.Close();
            return;
        }
        SetStatus("City: " + city.Name, quiet: true);
        ReportTaskDone(TutorialTaskModel.EnterCityScreen);
        Run(City.OpenAsync(city));
    }

    private void Deselect()
    {
        SelectedUnitId = null;
        ClearPreview();
        ApplySelectionScale();
        ClearSelectionUi();
        SetStatus("Click one of your units.", quiet: true);
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
            SetHint("(no movement left)");
            return;
        }
        var result = await queries.Path(unitNode.Unit.Id, tile.X, tile.Y);
        if (request != previewRequest) return; // a newer click or a deselect superseded this answer
        if (result is not { Reachable: true, FirstStep: not null })
        {
            SetHint("(cannot reach that tile)");
            return;
        }
        preview = result;
        PreviewTarget = tile;
        DrawDots(new HexCoord(unitNode.Unit.X, unitNode.Unit.Y), result);
        SetHint($"path {result.Turns.ToString(CultureInfo.InvariantCulture)} turn(s): click again to move");
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
        SiteOverlay.Clear();
        if (foundsCities)
        {
            foundsCities = false;
            if (HoveredTile is { } hovered) Run(LoadHoverTip(hovered)); // the tooltip goes back to the tile's own text
        }
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
        foundsCities = actions?.Actions.Any(it => it.Descriptor.ActionType == UnitActionType.FoundCity) == true;
        if (foundsCities && HoveredTile is { } under) Run(LoadHoverTip(under)); // the pointer already rests on a tile: show its site now
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
            SetHint("(no battle preview for that tile)");
            return;
        }
        pendingAttack = (result.Kind, tile);
        CombatPreview.Show(UnitOrdersModel.Combat(result));
        SetHint("confirm the attack or cancel", quiet: true); // the battle card says it
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
        ReportTaskDone(TutorialTaskModel.MoveUnitTask);
        Refresh();
        SetStatus(DescribeSelected(), quiet: true);
    }

    /// <summary>Asks the engine for the bar's numbers and for what is still pending, and shows them; announces a turn the first time it is seen.</summary>
    private async Task RefreshHud()
    {
        var bar = await queries.TopBar();
        var pending = await queries.Pending();
        if (bar == null) return; // a refused query leaves the old bar
        var action = HudModel.NextAction(pending, snapshot.TurnStatus, session.ViewerCivId);
        Hud.Update(bar, action, HudModel.CanEndTurn(pending));
        World.SetTechnologyAvailable(pending?.Decisions.Any(it => it.Kind == "Technology") == true);
        await RefreshRivals(bar.Turn, force: false);
        Layout();
        var rows = NotificationsModel.Rows(snapshot.Notifications);
        Notifications.Update(bar.Turn, rows);
        if (bar.Turn != bannerTurn)
        {
            bannerTurn = bar.Turn;
            Hud.ShowBanner(HudModel.Banner(bar.Turn, bar.Year, session.ViewerCivId));
        }
        ShowNextAlert(pending, reopen: false);
        ShowNextProposal(reopen: false);
        await CheckVoteResult();
        await CheckGameOver();
        CheckTutorials();
        await RefreshTaskCard();
    }

    /// <summary>
    /// C087: the rival stats ribbon's rows from the engine's overview (the known major civs) and the Rankings page (the figures the game shows the viewer) and, while the ribbon is open, from each rival's trade
    /// options (the luxuries it could trade). Collapsed, it asks once per turn (the header counts the rivals); open, on every refresh.
    /// </summary>
    private async Task RefreshRivals(int turn, bool force)
    {
        var ribbon = World.Rivals;
        if (!force && !ribbon.Expanded && rivalsTurn == turn) return;
        rivalsTurn = turn;
        int ticket = ++rivalsRequest;
        var overview = await queries.Overview();
        var rankings = await queries.Rankings();
        var trade = new Dictionary<string, TradeOptionsResult?>();
        if (ribbon.Expanded)
            foreach (var rival in RivalRibbonModel.Rivals(overview)) trade[rival.CivName] = await queries.TradeOptions(rival.CivName);
        if (ticket != rivalsRequest) return; // a newer refresh is on its way; never let an older answer overwrite it
        ribbon.Show(RivalRibbonModel.Rows(overview, rankings, trade));
    }

    private bool TaskCardActive => TaskCardEnabled && boundSettings != null && boundSettings.Current.ShowTutorials && !GameOver.Visible;

    /// <summary>
    /// C054: reports what the snapshot proves the player has done, then asks the engine for the current task and shows it (or hides the card).
    /// </summary>
    private async Task RefreshTaskCard()
    {
        if (!TaskCardActive)
        {
            TaskCard.Show(null);
            return;
        }
        foreach (var task in TutorialTaskModel.FromSnapshot(snapshot)) await ReportTask(task);
        TaskCard.Show(await queries.TutorialTask(showTutorials: true));
        Layout();
    }

    /// <summary>C054: tells the engine once per game screen that the player did <paramref name="task"/> (a refusal means the ruleset has no such task: harmless).</summary>
    private async Task ReportTask(string task)
    {
        if (!TaskCardActive || !reportedTasks.Add(task)) return;
        await session.SubmitAsync(new CompleteTutorialTask { Task = task });
    }

    private void ReportTaskDone(string task)
    {
        if (TaskCardActive && !reportedTasks.Contains(task)) Run(ReportTaskThenRefresh(task));
    }

    private async Task ReportTaskThenRefresh(string task)
    {
        await ReportTask(task);
        TaskCard.Show(await queries.TutorialTask(showTutorials: true));
        Layout();
    }

    private async Task ChooseTaskChoice(string eventName, int index)
    {
        await session.SubmitAsync(new ChooseTutorialTaskChoice { Event = eventName, Choice = index });
        await RefreshTaskCard();
    }

    /// <summary>
    /// C053: opens the World Congress result when the engine says it is due (Kotlin shows it before the victory screen and the alerts, when no popup is open), and
    /// closes it when it no longer is. The player's Continue sends <c>AckDiplomaticVoteResult</c>.
    /// </summary>
    private async Task CheckVoteResult()
    {
        if (resigned || Alerts.Visible || Picker.Visible) return;
        var due = VoteResultModel.Build(await queries.DiplomaticVoteResult());
        if (due == null)
        {
            VoteResult.Close();
            return;
        }
        if (!VoteResult.Visible) VoteResult.Open(due);
    }

    private async Task AcknowledgeVoteResult()
    {
        var result = await session.SubmitAsync(new Unciv.Commands.Empire.AckDiplomaticVoteResult());
        if (!result.Accepted) SetStatus("Continue refused: " + result.Message);
        VoteResult.Close();
        Refresh();
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
        if (pending != null)
        {
            // C067 (S2): an alert that only informs is not a popup: it shows once as a toast in the notification column and waits under "Review alerts"
            foreach (var entry in pending.Decisions)
                if (AlertPopupModel.Describe(entry, snapshot) is { Kind: AlertKind.Ambient } ambient && ambient.Type != AlertType.StartIntro && toastedAlerts.Add(ambient.Key))
                    Notifications.PushToast(ambient.Body.Length == 0 ? ambient.Title : ambient.Title + ": " + ambient.Body);
        }
        Notifications.SetWaiting(AlertPopupModel.WaitingCount(pending, snapshot, deferredAlerts) + TradeProposalModel.WaitingCount(snapshot, deferredProposals));
        if (next == null) Alerts.Close();
        else if (Alerts.Current?.Key != next.Key || !Alerts.Visible) Alerts.Open(next);
    }

    /// <summary>
    /// C039: opens the first incoming trade or peace proposal that was not put off, unless something else asks for the player first (an alert, a decision or a full
    /// screen: the proposal then waits and shows once they are closed); with <paramref name="reopen"/> the proposals that were put off come back. Also keeps the
    /// list of our own outstanding offers and what became of the latest ones.
    /// </summary>
    private void ShowNextProposal(bool reopen)
    {
        if (reopen) deferredProposals.Clear();
        Notifications.SetOffers(offerTracker.Update(snapshot));
        var next = TradeProposalModel.Next(snapshot, deferredProposals);
        bool covered = Alerts.Visible || Picker.Visible || City.Visible || Diplomacy.Visible || Tech.Visible || Policies.Visible || Trade.Visible || Overview.Visible || GameOver.Visible || VoteResult.Visible || Pedia.Visible;
        if (next == null || covered) Proposals.Close();
        else if (Proposals.Current?.Key != next.Key || !Proposals.Visible) Proposals.Open(next);
    }

    private bool answeringProposal;
    /// <summary>C076: the theme the screens share (the harness changes its scale to check that a screen rebuilds).</summary>
    public UiTheme? SharedTheme => ui;

    private UiTheme? ui;

    private async Task AnswerProposal(TradeProposal popup, AlertButton answer)
    {
        if (answeringProposal) return; // a second press while the engine answers would be refused and look like a withdrawal
        answeringProposal = true;
        try
        {
            await AnswerProposalOnce(popup, answer);
        }
        finally
        {
            answeringProposal = false;
        }
    }

    private async Task AnswerProposalOnce(TradeProposal popup, AlertButton answer)
    {
        offerTracker.NoteAnswered(popup.FromId);
        var request = snapshot.PendingTrades.FirstOrDefault(it => it.From == popup.FromId && it.To == snapshot.You);
        var result = await session.SubmitAsync(answer.Command);
        if (!result.Accepted)
        {
            offerTracker.NoteRefused(popup.FromId);
            SetStatus($"{popup.Title}: {answer.Label} refused: {result.Message}");
            return;
        }
        Proposals.Close();
        if (answer.Id == TradeProposalModel.CounterId)
        {
            Refresh();
            OpenTradeScreen(popup.FromId, request);
            return;
        }
        Refresh();
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
            case { Kind: NotificationTargetKind.Promotion } promotion:
                Run(PromoteFromNotification(promotion.Id));
                break;
            case { Kind: NotificationTargetKind.Spies }:
                Run(OpenDecision(DecisionKind.MoveSpies));
                break;
        }
    }

    // Events arrive before the screen redraws from the new snapshot, so the presenter can still take the nodes of units that just died.
    private void OnEventsReceived(EventBatch batch)
    {
        PresentEvents(batch.Events);
        PlayCues(batch.Events);
        if (session is not ITurnRoster roster) return;
        foreach (var gameEvent in batch.Events)
            if (TurnBarModel.Toast(gameEvent, roster.Roster, session.ViewerCivId) is { } toast) TurnBar.Toast(toast);
    }

    private void LayoutSocial() => Social.LayoutAtLeft(new Vector2(HudMargin, World.NavBottom + HudMargin));

    /// <summary>C067: what a nav button of the <see cref="WorldHud"/> opens.</summary>
    private void OnNav(NavTarget target)
    {
        switch (target)
        {
            case NavTarget.Diplomacy: OpenDiplomacyScreen(); break;
            case NavTarget.Technology: OpenTechScreen(); break;
            case NavTarget.Policies: OpenPolicyScreen(); break;
            case NavTarget.Trade: OpenTradeScreen(); break;
            case NavTarget.Espionage: OpenEspionageScreen(); break;
            case NavTarget.Civilopedia: OpenPediaScreen(); break;
            case NavTarget.Overview: OpenOverviewScreen(); break;
            case NavTarget.Menu: OpenGameMenu(); break;
            case NavTarget.Players:
                Social.Toggle(!Social.Visible);
                if (Social.Visible) LayoutSocial();
                break;
        }
    }

    /// <summary>C045: connects the panel to a hosted game's chat and player list (null disconnects and hides it).</summary>
    private void BindSocial(IGameSocial? next)
    {
        if (social != null)
        {
            social.ChatReceived -= Social.AddChat;
            social.PlayersChanged -= RefreshPlayers;
        }
        social = next;
        World.ShowPlayers(next != null);
        if (next == null)
        {
            Social.Toggle(false);
            return;
        }
        next.ChatReceived += Social.AddChat;
        next.PlayersChanged += RefreshPlayers;
        foreach (var line in next.Chat) Social.AddChat(line);
        RefreshPlayers();
    }

    private void RefreshPlayers()
    {
        if (social == null) return;
        Social.ShowPlayers(social.Players);
        LayoutSocial();
    }

    private async Task SendChat(string text)
    {
        if (social == null) return;
        try
        {
            await social.SendChatAsync(text);
        }
        catch (InvalidOperationException)
        {
            if (IsInstanceValid(this) && IsInsideTree()) Social.SetNotice("The host cannot be reached.".Tr());
        }
    }

    /// <summary>C045: Kick (<paramref name="newKey"/> null) or Rebind on the host's session; the refusal, if any, is shown in the panel's notice line.</summary>
    private async Task Admin(string civId, string? newKey)
    {
        if (social == null) return;
        string? refused;
        try
        {
            refused = newKey == null ? await social.KickAsync(civId) : await social.RebindAsync(civId, newKey);
        }
        catch (Exception e) when (e is InvalidOperationException or IOException)
        {
            refused = "host-error";
        }
        if (!IsInstanceValid(this) || !IsInsideTree()) return;
        Social.SetNotice(refused == null ? (newKey == null ? "Seat released." : "Seat rebound: the new device can join now.").Tr() : "Not done: ".Tr() + SocialModel.Describe(refused));
    }

    /// <summary>
    /// C036: redraws the turn bar from the host's TurnStatus and roster and raises the "resolving" overlay while the round is resolved (commands are refused then:
    /// <c>turn-resolving</c>). Hides the bar in a game that is not simultaneous.
    /// </summary>
    private void RefreshTurnBar()
    {
        if (session is not ITurnRoster roster || snapshot == null)
        {
            TurnBar.Show(null);
            return;
        }
        var bar = TurnBarModel.Of(snapshot.TurnStatus, roster.Roster, session.ViewerCivId, NowMs());
        TurnBar.Show(bar);
        shownCountdown = bar?.Countdown;
        TurnBar.LayoutIn(Size.X, TurnBarTop());
        if (bar is { Resolving: true }) TurnOverlay.Show(new NextTurnProgressViewData("Resolving the turn", "Waiting for the host", 0.0));
        else if (turnCancellation == null) TurnOverlay.Close();
    }

    public override void _Process(double delta)
    {
        if (statusFrames > 0 && --statusFrames == 0) statusPanel.Visible = false;
        // the countdown moves with the clock, not with snapshots
        if (session is not ITurnRoster || TurnBar.Data == null || snapshot?.TurnStatus.Deadline == null) return;
        if (TurnBarModel.Countdown(snapshot.TurnStatus.Deadline, NowMs()) != shownCountdown) RefreshTurnBar();
    }

    /// <summary>C057: where the engine's sound cues are played (the app's sound player); null in scenarios that do not care, which then play nothing.</summary>
    public Action<string>? PlaySound { get; set; }

    /// <summary>Plays the sounds the batch asks of this viewer; a missing sound device never throws into the event stream.</summary>
    public void PlayCues(IReadOnlyList<GameEvent> events)
    {
        if (PlaySound is not { } play) return;
        foreach (string sound in SoundCues.For(events, session.ViewerCivId)) play(sound);
    }

    /// <summary>Queues events for the presenter (C033). Call before <see cref="Refresh"/>, while the nodes of units the events destroy are still in the table.</summary>
    public bool PresentEvents(IReadOnlyList<GameEvent> events) => Presenter.Present(events, unitNodes);

    private void OnSnapshotChanged(ClientSnapshot changed)
    {
        snapshot = changed;
        RefreshTurnBar();
        Run(RefreshHud());
    }

    private void OnHudAction(HudAction action)
    {
        switch (action.Kind)
        {
            case HudActionKind.EndTurn:
                OnNextTurn();
                break;
            case HudActionKind.UndoEndTurn:
                Run(UndoEndTurn());
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
        string DescribeBelief(string name) => ruleset.Beliefs.TryGetValue(name, out var belief) ? DecisionPickerModel.DescribeUniques(belief) : "";
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

    /// <summary>C052: a "can be promoted" notification selects its unit (when it is still ours) and opens the promotion picker.</summary>
    private async Task PromoteFromNotification(int unitId)
    {
        if (!unitNodes.TryGetValue(unitId, out var node) || !node.IsOwn)
        {
            SetStatus("This unit cannot be promoted now.");
            return;
        }
        SelectUnit(node);
        await OpenPromotionPicker();
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
        if (Espionage.Visible) Run(Espionage.RefreshAsync());
        SetStatus($"{spec.Title}: done.");
    }

    private async Task DismissPicker(PickerSpec spec)
    {
        if (spec.Dismiss != null) await session.SubmitAsync(spec.Dismiss);
        Picker.Close();
        Refresh();
        if (Espionage.Visible) await Espionage.RefreshAsync();
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

    private async Task ReviewAlerts()
    {
        deferredProposals.Clear();
        ShowNextAlert(await queries.Pending(), reopen: true);
        ShowNextProposal(reopen: true);
    }

    private void OnNextTurn()
    {
        if (AutoPlay != null)
        {
            SetStatus("Stop AutoPlay first.");
            return;
        }
        Run(session is ITurnRoster ? EndTurnSimultaneous() : EndTurn());
    }

    /// <summary>
    /// C036: End turn in a simultaneous game only marks this player as done; the host resolves the round when everybody has (or the deadline passed). So nothing waits here
    /// and nothing is blocked: the turn bar shows whom the game waits for, and Undo end turn stays possible until the host starts resolving.
    /// </summary>
    private async Task EndTurnSimultaneous()
    {
        var result = await session.SubmitAsync(new EndTurn());
        if (!IsInstanceValid(this) || !IsInsideTree()) return;
        SetStatus(result.Accepted ? "Turn ended. Waiting for the other players." : "Cannot end the turn: " + Describe(result.Message));
        Refresh();
    }

    private async Task UndoEndTurn()
    {
        var result = await session.SubmitAsync(new UndoEndTurn());
        if (!IsInstanceValid(this) || !IsInsideTree()) return;
        SetStatus(result.Accepted ? "Your turn continues." : "Cannot undo: " + Describe(result.Message));
        Refresh();
    }

    private static string Describe(string code) => code == "turn-resolving" ? "the host is resolving the turn" : code;

    /// <summary>C047: the AutoPlay button stops a running run, otherwise opens its menu (or closes it when open).</summary>
    private void OnAutoPlayButton()
    {
        if (AutoPlay != null)
        {
            Run(StopAutoPlayAsync());
            return;
        }
        if (AutoPlayMenu.Visible)
        {
            AutoPlayMenu.Close();
            return;
        }
        var settings = (boundSettings?.Current ?? ClientSettings.Default).AutoPlay;
        AutoPlayMenu.Open(AutoPlayModel.Entries(settings, AutoPlayModel.CanStart(Hud.Action.Kind, GameOver.Visible || resigned)));
        Layout();
    }

    /// <summary>
    /// C047: starts the engine's auto-play with the stored options and then plays one turn per <see cref="AutoPlayFramesPerTurn"/> frames through the session
    /// until the run is out of turns, the engine refuses the next one (the game ended, the civ was defeated) or the player stops it. Stopping (the button, hiding the
    /// button in the options, leaving the game) sends the engine's StopAutoPlay so the log and a replay agree.
    /// </summary>
    public async Task StartAutoPlayAsync(AutoPlayKind kind)
    {
        AutoPlayMenu.Close();
        if (AutoPlay != null) return;
        var settings = (boundSettings?.Current ?? ClientSettings.Default).AutoPlay;
        var started = await session.SubmitAsync(AutoPlayModel.StartCommand(settings, kind));
        if (!started.Accepted)
        {
            SetStatus("Cannot start AutoPlay: " + started.Message);
            return;
        }
        int generation = ++autoPlayGeneration;
        AutoPlay = AutoPlayModel.StartRun(settings, kind);
        AutoPlayResult = null;
        Hud.SetAutoPlay(AutoPlay);
        string refusal = "";
        while (AutoPlay is { Active: true } && generation == autoPlayGeneration)
        {
            TurnOutcome outcome;
            try
            {
                outcome = await session.AutoPlayTurnAsync();
            }
            catch (Exception e)
            {
                // review C040: a failed background turn must end the run visibly instead of leaving AutoPlay "active" with nothing playing
                GD.PushError("AutoPlay turn failed: " + e);
                if (generation != autoPlayGeneration) return;
                refusal = "the turn could not be processed: " + e.Message;
                break;
            }
            if (generation != autoPlayGeneration) return; // stopped (or the game replaced) while the turn was played
            if (!outcome.Accepted)
            {
                refusal = outcome.Message;
                await session.SubmitAsync(new Unciv.Commands.Turn.StopAutoPlay()); // review C047: a refusal the engine did not cause itself must not leave its auto-play running (the next Start would be refused); refused harmlessly when it already stopped
                break;
            }
            AutoPlay = AutoPlay.AfterTurn();
            Hud.SetAutoPlay(AutoPlay);
            Refresh();
            for (int i = 0; i < AutoPlayFramesPerTurn && AutoPlay.Active; i++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (generation != autoPlayGeneration) return;
            }
        }
        FinishAutoPlay(stoppedByPlayer: false, refusal);
    }

    /// <summary>C047: the Stop control. Ends the run at once and tells the engine, so the next turn is the player's.</summary>
    public async Task StopAutoPlayAsync()
    {
        if (AutoPlay == null) return;
        autoPlayGeneration++;
        var result = await session.SubmitAsync(new Unciv.Commands.Turn.StopAutoPlay());
        FinishAutoPlay(stoppedByPlayer: true, result.Accepted ? "" : result.Message);
        Refresh();
    }

    private void FinishAutoPlay(bool stoppedByPlayer, string refusal)
    {
        if (AutoPlay == null) return;
        AutoPlayResult = AutoPlayModel.FinishedText(AutoPlay, stoppedByPlayer, refusal);
        AutoPlay = null;
        Hud.SetAutoPlay(null);
        SetStatus(AutoPlayResult);
        Run(RefreshHud());
    }

    private async Task EndTurn()
    {
        if (turnCancellation != null) return; // already processing: a second press (or hotkey) must not end the next turn too
        using var cancellation = new CancellationTokenSource();
        turnCancellation = cancellation;
        var turnSession = session;
        TurnOverlay.Show(NextTurnProgressModel.Begin());
        var progress = new Progress<TurnProgress>(report =>
        {
            // reports arrive posted to this thread, so one can land after the turn ended or the screen left the tree
            if (turnCancellation == cancellation && IsInsideTree()) TurnOverlay.Show(NextTurnProgressModel.Of(report, DisplayName));
        });
        TurnOutcome? outcome = null;
        try
        {
            outcome = await turnSession.EndTurnAsync(progress, cancellation.Token);
        }
        catch (Exception e)
        {
            GD.PushError("End turn failed: " + e);
            if (IsInsideTree()) SetStatus("The turn could not be processed: " + e.Message);
        }
        finally
        {
            if (turnCancellation == cancellation) turnCancellation = null;
            if (IsInstanceValid(this) && IsInsideTree()) TurnOverlay.Close();
        }
        if (!IsInstanceValid(this) || !IsInsideTree() || session != turnSession) return; // the player left the game meanwhile
        if (outcome is { Accepted: true, HandOverTo: { } next })
        {
            HandOverRequested?.Invoke(next); // before any redraw: the finished turn's view must never reach the screen again
            return;
        }
        Refresh();
        if (outcome == null) return;
        SetStatus(outcome.Accepted
            ? $"Turn {snapshot.Turn.ToString(CultureInfo.InvariantCulture)}: {outcome.AiTurnsRun.ToString(CultureInfo.InvariantCulture)} civ(s) played, {outcome.Events.Count.ToString(CultureInfo.InvariantCulture)} event(s)."
            : "Cannot end the turn: " + outcome.Message);
    }

    private string DisplayName(string civId) => civId;

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

    /// <summary>
    /// C068: the world screen's one-line message. <see cref="StatusText"/> always holds the latest text (scripts read it), but the line is shown only for a message the player did not
    /// cause by looking (a refusal, a result, a path hint) and fades after <see cref="StatusFrames"/>; <paramref name="quiet"/> texts (selection, opened screens) duplicate what is on
    /// screen already and are never shown. <paramref name="shown"/> is the part to display when the text starts with context the unit panel shows.
    /// </summary>
    private void SetStatus(string text, bool quiet = false, string? shown = null)
    {
        statusText = text.TrText();
        statusLabel.Text = (shown ?? text).TrText();
        statusFrames = quiet || statusText.Length == 0 ? 0 : StatusFrames;
        statusPanel.Visible = statusFrames > 0;
        if (statusFrames > 0) LayoutStatus();
    }

    private void SetHint(string hint, bool quiet = false) => SetStatus($"{DescribeSelected()}   {hint}", quiet, hint);

    private void LayoutStatus()
    {
        statusPanel.Size = Vector2.Zero;
        var size = statusPanel.GetCombinedMinimumSize();
        statusPanel.Position = new Vector2(Math.Max(HudMargin, (Size.X - size.X) / 2f), Size.Y - HudMargin - size.Y);
    }
}
