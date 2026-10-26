using Godot;
using Unciv.GodotClient.Settings;
using Unciv.Assets;
using Unciv.GodotClient.Assets;
using Unciv.Logic.Map;
using Unciv.Net.Visibility;

namespace Unciv.GodotClient.Map;

/// <summary>How one drawn tile is tinted.</summary>
public enum TileFog
{
    /// <summary>Explored and currently seen: full colour.</summary>
    Visible,

    /// <summary>Explored but not currently seen: dimmed.</summary>
    Explored,
}

/// <summary>One drawn tile; remembers what it was drawn from so tests and later layers can ask the view instead of the engine.</summary>
public partial class TileNode : MeshInstance3D
{
    public HexCoord Coord { get; set; }
    public TileFog Fog { get; set; }
    public float Elevation { get; set; } = 1f;

    /// <summary>C002: the silhouette family the tile's mesh was built from (picking intersects the real surface profile).</summary>
    public ReliefKind Relief { get; set; } = ReliefKind.Flat;
}

/// <summary>
/// C009: the flat hex map. Built only from the viewer's tile list of the client snapshot (explored tiles; the query side already withholds everything
/// else), one prism per tile obtained through <see cref="AssetRegistry"/> (<c>terrain/&lt;name&gt;</c>), a feature tinting its base, hills and mountains
/// stretched upward, fogged tiles dimmed. Unexplored tiles are simply not drawn: the black backdrop is their tint. Pan (left drag, middle drag, WASD/arrows),
/// zoom (wheel) and picking (<see cref="PickTile"/>) work on an orthographic camera tilted over the ground plane.
/// </summary>
public partial class MapView : Node3D
{
    public const float TiltDegrees = 55f;
    public const float MinTilt = 25f;
    public const float MaxTilt = 80f;
    public const float MinZoom = 4f;
    public const float MaxZoom = 40f;
    public const float DefaultZoom = 14f;
    private const float CameraDistance = 60f;
    private const float WheelStep = 1.12f;
    private const float DragThresholdPixels = 4f;
    private const int CandidateRadius = 2;
    private const int PickSteps = 24;

    private readonly Dictionary<HexCoord, TileNode> tiles = [];
    private Node3D tileRoot = null!; // JUSTIFY: C009 assigned in _Ready before any use
    private TileHighlight highlight = null!; // JUSTIFY: C009 assigned in _Ready before any use
    private Vector3 focus;
    private float zoom = DefaultZoom;
    private float tilt = TiltDegrees;
    private float yaw;
    private MultiMeshInstance3D? vegetation;
    private Vector2? pressAt;
    private bool dragging;

    public Camera3D Camera { get; private set; } = null!; // JUSTIFY: C009 assigned in _Ready before any use

    public HexCoord? Selected { get; private set; }

    /// <summary>Raised on a click (press and release without dragging); null when the click hit no drawn tile.</summary>
    public event Action<HexCoord?>? TileClicked;

    /// <summary>Raised on a right click (C010: the quick "move there" order); the tile is picked but not selected.</summary>
    public event Action<HexCoord?>? TileRightClicked;

    public float Zoom => zoom;

    public float Tilt => tilt;

    public float Yaw => yaw;

    /// <summary>Tufts instanced by the vegetation MultiMesh (C002).</summary>
    public int VegetationCount => vegetation?.Multimesh?.InstanceCount ?? 0;

    /// <summary>Triangles of every drawn tile mesh plus every vegetation instance; the budget check of the terrain scenario.</summary>
    public int TriangleCount { get; private set; }

    public Vector3 Focus => focus;

    public int TileCount => tiles.Count;

    public int Count(TileFog fog) => tiles.Values.Count(it => it.Fog == fog);

    public TileNode? TileNodeAt(HexCoord coord) => tiles.GetValueOrDefault(coord);

    public bool IsHighlightVisible => highlight.Visible;

    public Vector3 HighlightPosition => highlight.Position;

    public override void _Ready()
    {
        AddChild(new WorldEnvironment { Environment = MapMaterials.Backdrop() });
        AddChild(new DirectionalLight3D { LightEnergy = 0.8f, RotationDegrees = new Vector3(-60f, 30f, 0f) });
        tileRoot = new Node3D { Name = "Tiles" };
        AddChild(tileRoot);
        highlight = new TileHighlight { Name = "Highlight" };
        AddChild(highlight);
        Camera = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Far = 200f, Current = true };
        AddChild(Camera);
        UpdateCamera();
    }

    /// <summary>
    /// Replaces the drawn map with <paramref name="snapshotTiles"/> and centres the camera on it (unless <paramref name="keepCamera"/>: a refresh after a
    /// command). Clears the selection.
    /// </summary>
    public void Load(AssetRegistry assets, IReadOnlyList<TileDto> snapshotTiles, bool keepCamera = false)
    {
        highlight.Setup(assets);
        foreach (var node in tiles.Values) node.QueueFree();
        tiles.Clear();
        ClearSelection();

        vegetation?.QueueFree();
        vegetation = null;
        TriangleCount = 0;
        var tuftTransforms = new List<(Transform3D Transform, Color Colour)>();
        var tintCache = new Dictionary<(string Base, string? Feature, bool Visible), Material>();
        foreach (var tile in snapshotTiles)
        {
            var feature = OverlayOf(tile);
            var key = (tile.BaseTerrain, feature, tile.Visible);
            if (!tintCache.TryGetValue(key, out var material))
            {
                var baseMaterial = assets.Get<Material>(AssetKey.Of(RulesetAssetKeys.Terrain, tile.BaseTerrain));
                var overlay = feature == null ? null : assets.Get<Material>(AssetKey.Of(RulesetAssetKeys.Terrain, feature));
                tintCache[key] = material = MapMaterials.Tinted(baseMaterial, overlay, tile.Visible);
            }
            var reliefName = ReliefTerrainOf(tile);
            var kind = TerrainRelief.KindOf(reliefName);
            var mesh = assets.Get<Mesh>(TerrainRelief.MeshKey(reliefName));
            TriangleCount += (mesh.GetFaces().Length / 3);
            var coord = new HexCoord(tile.X, tile.Y);
            var node = new TileNode
            {
                Name = $"Tile_{tile.X}_{tile.Y}",
                Coord = coord,
                Fog = tile.Visible ? TileFog.Visible : TileFog.Explored,
                Elevation = TerrainRelief.Elevation(kind),
                Relief = kind,
                Mesh = mesh,
                MaterialOverride = material,
                Position = MapLayout.TileTop(tile.X, tile.Y, 0f),
            };
            tileRoot.AddChild(node);
            tiles[coord] = node;
            if (feature == null && kind == ReliefKind.Flat) AddTufts(tuftTransforms, tile, node.Position);
            AddProps(assets, node, tile, kind);
        }
        BuildVegetation(assets, tuftTransforms);
        ApplyResourceVisibility();
        if (tiles.Count > 0 && !keepCamera) FocusOn(Centre());
    }

    /// <summary>The feature or natural wonder drawn over the base terrain; hills are shown by height instead.</summary>
    private static string? OverlayOf(TileDto tile) =>
        tile.NaturalWonder ?? tile.Features.FirstOrDefault(it => it != HillFeature);

    private const string HillFeature = "Hill";
    private const string MountainTerrain = "Mountain";

    /// <summary>The terrain whose silhouette the tile uses: mountains and hills stand out, everything else keeps its base terrain's (water sits lower).</summary>
    private static string ReliefTerrainOf(TileDto tile) =>
        tile.BaseTerrain == MountainTerrain ? TerrainRelief.MountainName : tile.Features.Contains(HillFeature) ? TerrainRelief.HillName : tile.BaseTerrain;

    /// <summary>Number of feature and resource prop models currently on the map (C003).</summary>
    public int PropCount => tiles.Values.Sum(it => it.GetChildCount());

    /// <summary>The prop models standing on a tile: feature props (<c>feature/&lt;name&gt;</c>) then the resource prop (<c>resource/&lt;name&gt;</c>), each a child of the tile node.</summary>
    public IReadOnlyList<Node3D> PropsAt(HexCoord coord) => tiles.TryGetValue(coord, out var node) ? [.. node.GetChildren().OfType<Node3D>()] : [];

    /// <summary>
    /// Props of a tile (C003): a ring-shaped feature prop (forest, jungle, marsh) and a centred resource prop, both fetched through the registry so a
    /// file override or pack entry for the key replaces them. Fogged tiles dim them like their terrain.
    /// </summary>
    private static void AddProps(AssetRegistry assets, TileNode node, TileDto tile, ReliefKind kind)
    {
        void Place(string name, AssetKey key, float height)
        {
            var model = assets.Get<Node3D>(key);
            model.Name = name;
            model.Position = new Vector3(0f, height, 0f);
            if (!tile.Visible) MapMaterials.Dim(model);
            node.AddChild(model);
        }
        foreach (var feature in tile.Features.Where(PropSpecs.HasFeatureProp))
            Place("Feature_" + feature, AssetKey.Of(RulesetAssetKeys.Feature, feature), TerrainRelief.SurfaceHeight(kind, FeatureRingFraction));
        if (tile.Resource != null) Place(ResourcePropName, AssetKey.Of(RulesetAssetKeys.Resource, tile.Resource), TerrainRelief.TopHeight(kind));
    }

    /// <summary>Where on a raised tile the feature ring stands (rim fraction of the ring radius), so forests on hills follow the slope instead of floating or sinking.</summary>
    private const float FeatureRingFraction = 0.55f;

    private static void AddTufts(List<(Transform3D, Color)> into, TileDto tile, Vector3 origin)
    {
        var count = VegetationGeometry.TuftsFor(tile.BaseTerrain);
        if (count == 0) return;
        var colour = MapMaterials.TuftColour(tile.BaseTerrain, tile.Visible);
        for (var i = 0; i < count; i++)
        {
            var (x, z, yaw, scale) = VegetationGeometry.Placement(tile.X, tile.Y, i);
            var basis = new Basis(Vector3.Up, yaw).Scaled(new Vector3(scale, scale, scale));
            into.Add((new Transform3D(basis, origin + new Vector3(x, TerrainRelief.LandHeight, z)), colour));
        }
    }

    private void BuildVegetation(AssetRegistry assets, List<(Transform3D Transform, Color Colour)> tufts)
    {
        if (tufts.Count == 0) return;
        var multi = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = assets.Get<Mesh>(VegetationGeometry.TuftMeshKey),
            InstanceCount = tufts.Count,
        };
        for (var i = 0; i < tufts.Count; i++)
        {
            multi.SetInstanceTransform(i, tufts[i].Transform);
            multi.SetInstanceColor(i, tufts[i].Colour);
        }
        TriangleCount += tufts.Count * (multi.Mesh.GetFaces().Length / 3);
        vegetation = new MultiMeshInstance3D { Name = "Vegetation", Multimesh = multi, MaterialOverride = MapMaterials.Vegetation(), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(vegetation);
    }

    private Vector3 Centre()
    {
        var min = new Vector3(float.MaxValue, 0f, float.MaxValue);
        var max = new Vector3(float.MinValue, 0f, float.MinValue);
        foreach (var coord in tiles.Keys)
        {
            var top = MapLayout.TileTop(coord.X, coord.Y);
            min = new Vector3(Math.Min(min.X, top.X), 0f, Math.Min(min.Z, top.Z));
            max = new Vector3(Math.Max(max.X, top.X), 0f, Math.Max(max.Z, top.Z));
        }
        return (min + max) / 2f;
    }

    public void FocusOn(Vector3 ground)
    {
        focus = new Vector3(ground.X, 0f, ground.Z);
        UpdateCamera();
    }

    public void SetZoom(float size)
    {
        zoom = Math.Clamp(size, MinZoom, MaxZoom);
        UpdateCamera();
    }

    /// <summary>Moves the camera over the ground by a vector in world units.</summary>
    public void PanBy(Vector3 delta)
    {
        focus += new Vector3(delta.X, 0f, delta.Z);
        UpdateCamera();
    }

    /// <summary>Tilts the camera (degrees below the horizon, clamped) and turns it around the vertical axis; the focus stays put.</summary>
    public void SetView(float tiltDegrees, float yawDegrees = 0f)
    {
        tilt = Math.Clamp(tiltDegrees, MinTilt, MaxTilt);
        yaw = yawDegrees;
        UpdateCamera();
    }

    private void UpdateCamera()
    {
        if (Camera == null) return;
        Camera.RotationDegrees = new Vector3(-tilt, yaw, 0f);
        Camera.Size = zoom;
        Camera.Position = focus + Camera.Basis.Z * CameraDistance;
    }

    /// <summary>
    /// The drawn tile under a screen position, or null if the ray misses every drawn tile. The ray is marched through each nearby tile's real surface profile
    /// (<see cref="TerrainRelief.SurfaceHeight"/>, so hill flanks and mountain slopes count, not just their apex plane); the tile the ray reaches first wins.
    /// </summary>
    public HexCoord? PickTile(Vector2 screenPosition)
    {
        var flat = PickGround(screenPosition, ProceduralAssets.HexHeight);
        if (flat == null) return null;
        var origin = Camera.ProjectRayOrigin(screenPosition);
        var direction = Camera.ProjectRayNormal(screenPosition);
        HexCoord? best = null;
        var bestDistance = float.MaxValue;
        foreach (var candidate in HexMath.GetHexCoordsInDistance(MapLayout.TileAt(flat.Value), CandidateRadius, false))
        {
            if (!tiles.TryGetValue(candidate, out var node)) continue;
            var distance = RayDistanceToSurface(origin, direction, candidate, node);
            if (distance < bestDistance)
            {
                best = candidate;
                bestDistance = distance;
            }
        }
        return best;
    }

    /// <summary>Distance along the ray at which it first goes below the tile's surface inside the tile, or infinity.</summary>
    private static float RayDistanceToSurface(Vector3 origin, Vector3 direction, HexCoord coord, TileNode node)
    {
        if (Math.Abs(direction.Y) < 1e-6f) return float.PositiveInfinity;
        var top = ProceduralAssets.HexHeight * node.Elevation;
        var rim = TerrainRelief.RimHeight(node.Relief);
        var centre = MapLayout.TileTop(coord.X, coord.Y, 0f);
        var upper = Math.Max(top, rim);
        var lower = Math.Min(top, rim);
        var tStart = (upper - origin.Y) / direction.Y;
        var tEnd = (lower - origin.Y) / direction.Y;
        for (var step = 0; step <= PickSteps; step++)
        {
            var t = tStart + (tEnd - tStart) * step / PickSteps;
            var p = origin + direction * t;
            if (MapLayout.TileAt(p) != coord) continue;
            var surface = TerrainRelief.SurfaceHeight(node.Relief, TerrainRelief.RimFraction(p.X - centre.X, p.Z - centre.Z));
            if (p.Y <= surface + 1e-3f) return t;
        }
        return float.PositiveInfinity;
    }

    private Vector3? PickGround(Vector2 screenPosition, float planeY)
    {
        var origin = Camera.ProjectRayOrigin(screenPosition);
        var direction = Camera.ProjectRayNormal(screenPosition);
        return new Plane(Vector3.Up, planeY).IntersectsRay(origin, direction);
    }

    /// <summary>The engine world position (x, y; north up) of the ground under a screen position, or null when the ray misses the ground (C014 minimap viewport rectangle).</summary>
    public Vector2? WorldAt(Vector2 screenPosition) => PickGround(screenPosition, 0f) is { } ground ? new Vector2(ground.X, -ground.Z) : null;

    /// <summary>Screen position of a tile's top-face centre (for tests and for later overlays).</summary>
    public Vector2 ScreenPositionOf(HexCoord coord)
    {
        var elevation = tiles.TryGetValue(coord, out var node) ? node.Elevation : 1f;
        return Camera.UnprojectPosition(MapLayout.TileTop(coord.X, coord.Y, elevation));
    }

    public void Select(HexCoord? coord)
    {
        Selected = coord;
        if (coord is { } c && tiles.TryGetValue(c, out var node)) highlight.ShowOn(MapLayout.TileTop(c.X, c.Y, node.Elevation));
        else highlight.Clear();
    }

    public void ClearSelection() => Select(null);

    /// <summary>C028: the key bindings the pan keys are read from (the arrow keys always pan as well).</summary>
    public IReadOnlyDictionary<string, string> KeyBindings { get; set; } = new Dictionary<string, string>();

    /// <summary>C028: scroll speed multiplier for key and edge panning.</summary>
    public float ScrollSpeed { get; set; } = 1f;

    /// <summary>C028: pan when the mouse rests at the window edge.</summary>
    public bool EdgePan { get; set; }

    private bool showResources = true;

    /// <summary>C028: whether resource props are drawn (the settings' resource icon toggle).</summary>
    public bool ShowResources
    {
        get => showResources;
        set
        {
            showResources = value;
            ApplyResourceVisibility();
        }
    }

    private void ApplyResourceVisibility()
    {
        foreach (var node in tiles.Values)
        {
            if (node.GetNodeOrNull<Node3D>(ResourcePropName) is { } prop) prop.Visible = showResources;
        }
    }

    private const string ResourcePropName = "Resource";

    /// <summary>Resource props that are drawn right now (all of them, or none when the resource toggle is off).</summary>
    public int VisibleResourcePropCount => tiles.Values.Count(it => it.GetNodeOrNull<Node3D>(ResourcePropName) is { Visible: true });

    public override void _Process(double delta)
    {
        var direction = Vector3.Zero;
        bool Held(HotkeyAction action, Key arrow) => Input.IsPhysicalKeyPressed(arrow) || HotkeyInput.IsHeld(Hotkeys.KeyFor(KeyBindings, action));
        if (Held(HotkeyAction.PanLeft, Key.Left)) direction.X -= 1f;
        if (Held(HotkeyAction.PanRight, Key.Right)) direction.X += 1f;
        if (Held(HotkeyAction.PanUp, Key.Up)) direction.Z -= 1f;
        if (Held(HotkeyAction.PanDown, Key.Down)) direction.Z += 1f;
        if (direction != Vector3.Zero) PanBy(direction.Normalized() * OptionsModel.PanDistance(ScrollSpeed, zoom, delta));
        var viewport = GetViewport();
        if (EdgePan && viewport != null && GetWindow().HasFocus()) TickEdgePan(viewport.GetMousePosition(), viewport.GetVisibleRect().Size, delta);
    }

    /// <summary>C028: one frame of edge-panning for a mouse position inside a window of the given size (public so tests can drive it without a real mouse).</summary>
    public void TickEdgePan(Vector2 mouse, Vector2 windowSize, double delta)
    {
        var (x, y) = OptionsModel.EdgePanDirection(EdgePan, mouse.X, mouse.Y, windowSize.X, windowSize.Y);
        if (x == 0f && y == 0f) return;
        PanBy(new Vector3(x, 0f, y) * OptionsModel.PanDistance(ScrollSpeed, zoom, delta));
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp }:
                SetZoom(zoom / WheelStep);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown }:
                SetZoom(zoom * WheelStep);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left or MouseButton.Middle } button:
                OnMouseButton(button);
                break;
            case InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Right } right:
                TileRightClicked?.Invoke(PickTile(right.Position));
                break;
            case InputEventMouseMotion motion when pressAt != null:
                if ((motion.ButtonMask & (MouseButtonMask.Left | MouseButtonMask.Middle)) == 0) CancelPress(); // the release was lost (left the window, eaten by a Control)
                else OnDrag(motion);
                break;
        }
    }

    private void CancelPress()
    {
        pressAt = null;
        dragging = false;
    }

    private void OnMouseButton(InputEventMouseButton button)
    {
        if (button.Pressed)
        {
            pressAt = button.Position;
            dragging = button.ButtonIndex == MouseButton.Middle;
            return;
        }
        var wasClick = pressAt != null && !dragging && button.ButtonIndex == MouseButton.Left;
        pressAt = null;
        dragging = false;
        if (!wasClick) return;
        var picked = PickTile(button.Position);
        Select(picked);
        TileClicked?.Invoke(picked);
    }

    private void OnDrag(InputEventMouseMotion motion)
    {
        if (!dragging && pressAt is { } start && start.DistanceTo(motion.Position) > DragThresholdPixels) dragging = true;
        if (!dragging) return;
        // Keep the ground point that was under the cursor under it: move the camera by the difference of the two picks.
        var before = PickGround(motion.Position - motion.Relative, 0f);
        var after = PickGround(motion.Position, 0f);
        if (before != null && after != null) PanBy(before.Value - after.Value);
    }
}
