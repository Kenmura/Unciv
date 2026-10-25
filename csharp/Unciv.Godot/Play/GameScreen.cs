using System.Globalization;
using Godot;
using Unciv.Assets;
using Unciv.Commands.Units;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Map;
using Unciv.Logic.Map;
using Unciv.Net.Visibility;
using Unciv.Queries.Units;

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
    private Label turnLabel = null!; // JUSTIFY: C010 assigned in _Ready before any use
    private Label statusLabel = null!; // JUSTIFY: C010 assigned in _Ready before any use
    private Button nextTurnButton = null!; // JUSTIFY: C010 assigned in _Ready before any use
    private ClientSnapshot snapshot = null!; // JUSTIFY: C010 assigned by Refresh in Start
    private int previewRequest;
    private PathPreviewResult? preview;
    private bool mapLoaded;

    public MapView Map { get; private set; } = null!; // JUSTIFY: C010 assigned in _Ready before any use

    public MinimapView Minimap { get; private set; } = null!; // JUSTIFY: C014 assigned in _Ready before any use

    public int? SelectedUnitId { get; private set; }

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
        unitLayer = new Node3D { Name = "Units" };
        AddChild(unitLayer);
        dotLayer = new Node3D { Name = "PathDots" };
        AddChild(dotLayer);

        turnLabel = new Label { Name = "TurnLabel", Position = new Vector2(12f, 8f) };
        AddChild(turnLabel);
        statusLabel = new Label { Name = "StatusLabel" };
        AddChild(statusLabel);
        nextTurnButton = new Button { Name = "NextTurn", Text = "Next turn", CustomMinimumSize = new Vector2(140f, 40f) };
        nextTurnButton.Pressed += OnNextTurn;
        AddChild(nextTurnButton);
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
        nextTurnButton.Position = Size - nextTurnButton.CustomMinimumSize - new Vector2(HudMargin, HudMargin);
        statusLabel.Position = new Vector2(HudMargin, Size.Y - HudMargin - 24f);
        Minimap.Position = new Vector2(Size.X - HudMargin - Minimap.Size.X, HudMargin);
    }

    public override void _ExitTree() => GetViewport().SizeChanged -= Layout;

    /// <summary>Connects the screen to a game and draws it, centred on the human's first unit. Call once, after the screen entered the tree.</summary>
    public void Start(IGameSession gameSession, AssetRegistry registry)
    {
        session = gameSession;
        assets = registry;
        queries = new GameQueries(session);
        Refresh();
        var first = snapshot.Units.FirstOrDefault(it => it.Own != null);
        if (first != null) Map.FocusOn(MapLayout.TileTop(first.X, first.Y));
        SetStatus("Click one of your units.");
    }

    private void Refresh()
    {
        snapshot = session.Snapshot;
        Map.Load(assets, snapshot.Tiles, keepCamera: mapLoaded);
        mapLoaded = true;
        var nations = session.Ruleset.Nations;
        Minimap.Update(snapshot.Tiles, owner => nations.TryGetValue(owner, out var nation) ? ToRgb(nation.GetOuterColor()) : null);
        Layout();
        RebuildUnits();
        ClearPreview();
        turnLabel.Text = $"Turn {snapshot.Turn.ToString(CultureInfo.InvariantCulture)}   {session.ViewerCivId}";
        if (SelectedUnitId is { } id && unitNodes.TryGetValue(id, out var selected) && selected.IsOwn)
            Map.Select(new HexCoord(selected.Unit.X, selected.Unit.Y));
        else SelectedUnitId = null;
        ApplySelectionScale();
    }

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
            var model = assets.Get<Node3D>(AssetKey.Of(RulesetAssetKeys.Unit, unit.Name));
            var colour = session.Ruleset.Nations.TryGetValue(unit.Owner, out var nation) ? nation.GetOuterColor() : null;
            if (colour != null) MapMaterials.ColourBanner(model, colour.R, colour.G, colour.B);
            var node = new UnitNode { Name = $"Unit_{unit.Id}", Unit = unit, Position = UnitPosition(coord, index) };
            node.AddChild(model);
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
        if (own.Count > 0)
        {
            int at = SelectedUnitId is { } current ? own.FindIndex(it => it.Unit.Id == current) : -1;
            SelectUnit(own[(at + 1) % own.Count]);
            return;
        }
        if (SelectedUnitId == null) return;
        if (PreviewTarget == tile) Run(MoveSelectedTo());
        else Run(ShowPreview(tile));
    }

    private void OnTileRightClicked(HexCoord? coord)
    {
        if (coord is not { } tile || SelectedUnitId == null) return;
        Run(RightClickMove(tile));
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
    }

    private void Deselect()
    {
        SelectedUnitId = null;
        ClearPreview();
        ApplySelectionScale();
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
