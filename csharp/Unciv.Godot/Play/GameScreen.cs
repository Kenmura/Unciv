using System.Globalization;
using Godot;
using Unciv.Assets;
using Unciv.Commands;
using Unciv.Commands.Units;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Map;
using Unciv.GodotClient.Ui;
using Unciv.Logic.Map;
using Unciv.Net.Visibility;
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

    public int? SelectedUnitId { get; private set; }

    /// <summary>The city whose screen (<see cref="City"/>) is open: the hook that tells which city.</summary>
    public CityDto? OpenCity { get; private set; }

    /// <summary>The city screen (C017); created by <see cref="Start"/>.</summary>
    public CityScreen City { get; private set; } = null!; // JUSTIFY: C017 assigned in Start before any use

    public bool IsCityPanelVisible => City.Visible;

    /// <summary>C021: the diplomacy screen, opened by the Diplomacy button.</summary>
    public DiplomacyScreen Diplomacy { get; private set; } = null!; // JUSTIFY: C021 assigned in Start before any use

    public bool IsDiplomacyVisible => Diplomacy.Visible;

    public Button DiplomacyButton { get; } = new() { Name = "DiplomacyButton", Text = "Diplomacy" };

    /// <summary>Opens the diplomacy screen on the civ with this id (the first met civ when null), closing the city screen.</summary>
    public void OpenDiplomacyScreen(string? civId = null)
    {
        ShowCity(null);
        Deselect();
        SetStatus("Diplomacy");
        Run(Diplomacy.OpenAsync(civId));
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
        Hud.ActionRequested += OnHudAction;
        Hud.EndTurnRequested += OnNextTurn;
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
        DiplomacyButton.Position = new Vector2(HudMargin, HudMargin + Hud.TopBarHeight);
        statusLabel.Position = new Vector2(HudMargin, Size.Y - HudMargin - 24f);
        Minimap.Position = new Vector2(Size.X - HudMargin - Minimap.Size.X, HudMargin + Hud.TopBarHeight);
        Panel.LayoutIn(Size);
        CombatPreview.LayoutIn(Size);
    }

    public override void _ExitTree()
    {
        GetViewport().SizeChanged -= Layout;
        if (session != null) session.SnapshotChanged -= OnSnapshotChanged;
    }

    /// <summary>Connects the screen to a game and draws it, centred on the human's first unit. Call after the screen entered the tree; calling it again switches to another game (C016: load).</summary>
    public void Start(IGameSession gameSession, AssetRegistry registry)
    {
        if (session != null)
        {
            session.SnapshotChanged -= OnSnapshotChanged;
            SelectedUnitId = null;
            mapLoaded = false;
            bannerTurn = -1;
        }
        session = gameSession;
        assets = registry;
        queries = new GameQueries(session);
        var theme = new UiTheme(registry);
        Hud.Init(theme);
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
        Panel.Init(theme);
        CombatPreview.Init(theme);
        ReachableOverlay.Setup(registry);
        AttackOverlay.Setup(registry);
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
        Overlays.Rebuild(Map, snapshot.Tiles, snapshot.Cities, OwnerColour);
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
            var node = new UnitNode { Name = $"Unit_{unit.Id}", Unit = unit, Position = UnitPosition(coord, index) };
            node.Build(assets, session.Ruleset, session.Ruleset.Units.GetValueOrDefault(unit.Name), colour == null ? null : (colour.R, colour.G, colour.B));
            unitLayer.AddChild(node);
            unitNodes[unit.Id] = node;
        }
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
        if (bar.Turn != bannerTurn)
        {
            bannerTurn = bar.Turn;
            Hud.ShowBanner(HudModel.Banner(bar.Turn, bar.Year, session.ViewerCivId));
        }
    }

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
            default:
                SetStatus($"{action.Label}: this screen is not built yet.");
                break;
        }
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

    private void SetStatus(string text) => statusLabel.Text = text;
}
