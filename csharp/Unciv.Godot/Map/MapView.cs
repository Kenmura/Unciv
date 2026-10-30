using Godot;
using Unciv.GodotClient.Settings;
using Unciv.Assets;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Ui;
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

/// <summary>One drawn tile (its surface is part of a chunk mesh, C031); remembers what it was drawn from so tests and later layers can ask the view instead of the engine.</summary>
public partial class TileNode : Node3D
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
    private TerrainChunkLayer terrain = null!; // JUSTIFY: C031 assigned in _Ready before any use
    private FarPropLayer farProps = null!; // JUSTIFY: T287 assigned in _Ready before any use
    private Vector2 lastViewportSize;
    private Vector2? pressAt;
    private bool dragging;
    private readonly PinchTracker pinch = new();
    private bool touchPress;
    private bool longPressed;
    private double pressHeld;

    public Camera3D Camera { get; private set; } = null!; // JUSTIFY: C009 assigned in _Ready before any use

    public HexCoord? Selected { get; private set; }

    /// <summary>Raised on a click (press and release without dragging); null when the click hit no drawn tile.</summary>
    public event Action<HexCoord?>? TileClicked;

    /// <summary>C091: whether Shift was held for the click that last fired <see cref="TileClicked"/> (a shift-click adds a unit to the group).</summary>
    public bool LastClickShift { get; private set; }

    /// <summary>C100: where the last click or tap was released (a touch tile tip is shown there).</summary>
    public Vector2 ClickAt { get; private set; }

    /// <summary>Raised on a right click (C010: the quick "move there" order); the tile is picked but not selected.</summary>
    public event Action<HexCoord?>? TileRightClicked;

    /// <summary>C089: where the pointer was when <see cref="TileRightClicked"/> last fired (the marker menu opens there).</summary>
    public Vector2 RightClickAt { get; private set; }

    public float Zoom => zoom;

    public float Tilt => tilt;

    public float Yaw => yaw;

    /// <summary>Tufts instanced by the vegetation MultiMesh (C002).</summary>
    public int VegetationCount => vegetation?.Multimesh?.InstanceCount ?? 0;

    /// <summary>Triangles of every drawn tile mesh plus every vegetation instance; the budget check of the terrain scenario.</summary>
    public int TriangleCount { get; private set; }

    public Vector3 Focus => focus;

    /// <summary>C031: merged terrain chunks drawn.</summary>
    public int ChunkCount => terrain.ChunkCount;

    /// <summary>C031: chunks inside the camera's ground footprint (the others are hidden).</summary>
    public int VisibleChunkCount => terrain.VisibleChunkCount;

    /// <summary>C031: the baked, blended vertex colour nearest to a world point on the terrain surface.</summary>
    public Color? SurfaceColourNear(Vector3 ground) => terrain.ColourNear(ground);

    /// <summary>C031: whether the terrain chunk under a world point is currently drawn.</summary>
    public bool IsChunkVisibleAt(Vector3 ground) => terrain.IsChunkVisibleAt(ground);

    /// <summary>C098: the material the terrain chunks wear (a ShaderMaterial at Ultra, else the plain chunk material); null before the first Load.</summary>
    public Material? TerrainMaterial => terrain?.ChunkMaterial;

    public int TileCount => tiles.Count;

    public int Count(TileFog fog) => tiles.Values.Count(it => it.Fog == fog);

    public TileNode? TileNodeAt(HexCoord coord) => tiles.GetValueOrDefault(coord);

    public bool IsHighlightVisible => highlight.Visible;

    public Vector3 HighlightPosition => highlight.Position;

    private Godot.Environment environment = null!; // JUSTIFY: C097 assigned in _Ready before any use
    private DirectionalLight3D sun = null!; // JUSTIFY: C097 assigned in _Ready before any use

    /// <summary>C097: the preset in effect (Low until <see cref="ApplyGraphics"/> is given another).</summary>
    public Unciv.GodotClient.Settings.GraphicsPreset Graphics { get; private set; } = Unciv.GodotClient.Settings.GraphicsModel.Preset(Unciv.GodotClient.Settings.GraphicsQuality.Low);

    /// <summary>
    /// C097: puts a graphics preset into effect on the map's environment, sun and viewport. Low is exactly the look before the setting existed (flat ambient, linear tone map,
    /// no shadows, no glow, no anti-aliasing), so applying it changes nothing. SSAO and SSR are Forward+ features: the renderer ignores them elsewhere, and the preset
    /// asks for them only at Ultra, which is only drawn on Forward+.
    /// </summary>
    public void ApplyGraphics(Unciv.GodotClient.Settings.GraphicsPreset preset)
    {
        Graphics = preset;
        environment.TonemapMode = preset.Tonemap ? Godot.Environment.ToneMapper.Filmic : Godot.Environment.ToneMapper.Linear;
        environment.TonemapExposure = preset.Exposure;
        environment.AmbientLightEnergy = preset.AmbientEnergy;
        environment.AdjustmentEnabled = preset.Saturation != 1f;
        environment.AdjustmentSaturation = preset.Saturation;
        environment.GlowEnabled = preset.Glow;
        if (preset.Glow)
        {
            environment.GlowIntensity = 0.6f;
            environment.GlowBloom = 0.1f;
        }
        environment.SsaoEnabled = preset.Ssao;
        environment.SsrEnabled = preset.Ssr;
        sun.ShadowEnabled = preset.Shadows;
        if (terrain != null) terrain.ShaderTier = preset.ShaderTier; // C098: _Ready applies Low before the layer exists, which is tier 0 anyway
        var viewport = GetViewport();
        if (viewport != null)
        {
            viewport.Msaa3D = preset.Msaa switch { >= 4 => Viewport.Msaa.Msaa4X, >= 2 => Viewport.Msaa.Msaa2X, _ => Viewport.Msaa.Disabled };
        }
    }

    public override void _Ready()
    {
        environment = MapMaterials.Backdrop();
        sun = new DirectionalLight3D { LightEnergy = 0.8f, RotationDegrees = new Vector3(-60f, 30f, 0f), DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits };
        AddChild(new WorldEnvironment { Environment = environment });
        AddChild(sun);
        ApplyGraphics(Unciv.GodotClient.Settings.GraphicsModel.Preset(Unciv.GodotClient.Settings.GraphicsQuality.Low));
        terrain = new TerrainChunkLayer { Name = "Terrain" };
        AddChild(terrain);
        tileRoot = new Node3D { Name = "Tiles" };
        AddChild(tileRoot);
        farProps = new FarPropLayer { Name = "FarProps" };
        AddChild(farProps);
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
        var chunkTiles = new List<ChunkTile>();
        var colourCache = new Dictionary<(string Base, string? Feature, bool Visible), Tint>();
        foreach (var tile in snapshotTiles)
        {
            var feature = OverlayOf(tile);
            var key = (tile.BaseTerrain, feature, tile.Visible);
            if (!colourCache.TryGetValue(key, out var colour))
            {
                var baseMaterial = assets.Get<Material>(AssetKey.Of(RulesetAssetKeys.Terrain, tile.BaseTerrain));
                var overlay = feature == null ? null : assets.Get<Material>(AssetKey.Of(RulesetAssetKeys.Terrain, feature));
                colourCache[key] = colour = MapMaterials.TileColour(baseMaterial, overlay, tile.Visible);
            }
            var reliefName = ReliefTerrainOf(tile);
            var kind = TerrainRelief.KindOf(reliefName);
            var mesh = assets.Get<Mesh>(TerrainRelief.MeshKey(reliefName));
            var coord = new HexCoord(tile.X, tile.Y);
            chunkTiles.Add(new ChunkTile(coord, mesh, colour));
            var node = new TileNode
            {
                Name = $"Tile_{tile.X}_{tile.Y}",
                Coord = coord,
                Fog = tile.Visible ? TileFog.Visible : TileFog.Explored,
                Elevation = TerrainRelief.Elevation(kind),
                Relief = kind,
                Position = MapLayout.TileTop(tile.X, tile.Y, 0f),
            };
            tileRoot.AddChild(node);
            tiles[coord] = node;
            if (feature == null && kind == ReliefKind.Flat) AddTufts(tuftTransforms, tile, node.Position);
            AddProps(assets, node, tile, kind);
        }
        terrain.Build(chunkTiles);
        TriangleCount += terrain.TriangleCount;
        BuildVegetation(assets, tuftTransforms);
        farProps.Build(tiles.Values.SelectMany(tile => tile.GetChildren().OfType<Node3D>().Select(prop => (Tile: (Node3D)tile, Prop: prop, Resource: prop.Name == ResourcePropName))));
        ApplyResourceVisibility();
        if (tiles.Count > 0 && !keepCamera) FocusOn(Centre());
        UpdateCulling();
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
        UpdateCulling();
    }

    /// <summary>C031: hides the terrain chunks outside the camera's ground footprint (the four screen corners projected onto the ground).</summary>
    private void UpdateCulling()
    {
        if (Camera == null || !Camera.IsInsideTree()) return;
        var size = Camera.GetViewport().GetVisibleRect().Size;
        lastViewportSize = size;
        var corners = new[] { new Vector2(0f, 0f), new Vector2(size.X, 0f), new Vector2(size.X, size.Y), new Vector2(0f, size.Y) }.Select(it => PickGround(it, 0f)).ToList();
        if (propsMerged != MergedPropsDrawn)
        {
            propsMerged = MergedPropsDrawn;
            ApplyResourceVisibility();
        }
        VisibleGround = corners.Any(it => it == null) ? null : GroundRect.Around([.. corners.Select(it => (it!.Value.X, it.Value.Z))]);
        terrain?.Cull(VisibleGround);
        ViewChanged?.Invoke();
    }

    /// <summary>C085: the camera's ground footprint as of the last camera or window change (null before the camera is up or when it sees the sky).</summary>
    public GroundRect? VisibleGround { get; private set; }

    /// <summary>C085: raised after every camera or window change, once <see cref="VisibleGround"/> is current; the units' level of detail follows it.</summary>
    public event Action? ViewChanged;

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

    /// <summary>C058: whether <see cref="_Process"/> moves the camera from held keys and the mouse at the window edge, scaled by the real frame time. On in the game; the scenario harness turns it off (it moves the camera with explicit <see cref="TickEdgePan"/>/<see cref="PanBy"/> calls instead).</summary>
    public bool RealTimePanning { get; set; } = true;

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
        var merged = propsMerged = MergedPropsDrawn;
        foreach (var node in tiles.Values)
        {
            foreach (var prop in node.GetChildren().OfType<Node3D>())
            {
                var resource = prop.Name == ResourcePropName;
                var shown = !resource || showResources;
                prop.Visible = shown && !(merged && farProps.Merged.Contains(prop)); // T287: a merged prop is drawn by the far layer instead
            }
        }
        farProps.Apply(merged, showResources);
    }

    /// <summary>T287: whether the camera is far enough out that the merged prop meshes are drawn instead of the prop nodes (<see cref="FarZoomLod.MergeProps"/>).</summary>
    public bool MergedPropsDrawn => FarZoomLod.MergeProps(zoom);

    private bool propsMerged;

    /// <summary>T287: merged prop meshes made (one per terrain chunk and kind).</summary>
    public int MergedPropMeshCount => farProps.MeshCount;

    /// <summary>T287: props whose model is drawn by the merged meshes when far.</summary>
    public int MergedPropCount => farProps.Merged.Count;

    private const string ResourcePropName = "Resource";

    /// <summary>Resource props that are drawn right now (all of them, or none when the resource toggle is off).</summary>
    public int VisibleResourcePropCount => showResources ? tiles.Values.Count(it => it.GetNodeOrNull<Node3D>(ResourcePropName) != null) : 0;

    public override void _Process(double delta)
    {
        if (Camera != null && Camera.IsInsideTree() && Camera.GetViewport().GetVisibleRect().Size != lastViewportSize) UpdateCulling();
        TickLongPress(delta);
        if (!RealTimePanning) return;
        var direction = Vector3.Zero;
        // Typing into a text field must not pan the map behind it.
        bool typing = GetViewport()?.GuiGetFocusOwner() is LineEdit or TextEdit;
        bool Held(HotkeyAction action, Key arrow) => !typing && (Input.IsPhysicalKeyPressed(arrow) || HotkeyInput.IsHeld(Hotkeys.KeyFor(KeyBindings, action)));
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
            case InputEventScreenTouch touch:
                // C096: a second finger turns the press into a pinch: nothing is selected or dragged by the first one any more
                if (!touch.Pressed) pinch.Up(touch.Index);
                else if (pinch.Down(touch.Index, new System.Numerics.Vector2(touch.Position.X, touch.Position.Y))) CancelPress();
                break;
            case InputEventScreenDrag drag:
                if (pinch.Move(drag.Index, new System.Numerics.Vector2(drag.Position.X, drag.Position.Y)) is { } move)
                    ApplyPinch(move.ZoomFactor, new Vector2(move.Midpoint.X, move.Midpoint.Y), new Vector2(move.MidpointDelta.X, move.MidpointDelta.Y));
                break;
            case InputEventMagnifyGesture magnify when pinch.Count < 2: // a touch pinch is already zooming through the finger events
                SetZoom(zoom / Math.Max(0.1f, magnify.Factor));
                break;
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
                RightClickAt = right.Position;
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
        touchPress = false;
        longPressed = false;
        pressHeld = 0;
    }

    public override void _Notification(int what)
    {
        // a release that never arrives (focus lost, app paused, view removed) must not leave a ghost finger or a pending long press
        if (what is (int)NotificationWMWindowFocusOut or (int)NotificationApplicationFocusOut or (int)NotificationExitTree)
        {
            pinch.Reset();
            CancelPress();
        }
    }

    /// <summary>C096: zooms by <paramref name="factor"/> about the midpoint of the fingers and pans with it: the ground point that was under the midpoint stays under it.</summary>
    private void ApplyPinch(float factor, Vector2 midpoint, Vector2 midpointDelta)
    {
        var before = PickGround(midpoint - midpointDelta, 0f);
        SetZoom(zoom / factor);
        var after = PickGround(midpoint, 0f);
        if (before != null && after != null) PanBy(before.Value - after.Value);
    }

    /// <summary>C096: the smallest finger movement that is a drag, in this viewport's pixels (8 dp; the plain mouse threshold when the density is unknown).</summary>
    private float TouchDragThreshold()
    {
        var profile = DisplayProfile.Current;
        float dpi = profile.UsesDensity ? profile.Dpi : 0f;
        if (!(dpi > 0f)) return DragThresholdPixels;
        var window = GetWindow();
        float content = window != null && window.ContentScaleMode != Window.ContentScaleModeEnum.Disabled && window.ContentScaleFactor > 0f ? window.ContentScaleFactor : 1f;
        return Math.Max(DragThresholdPixels, UiTokens.DpToPx(UiTokens.TouchDragThresholdDp, dpi / content));
    }

    /// <summary>C096: a finger resting on the map for <see cref="UiTokens.LongPressSeconds"/> without dragging raises <see cref="TileRightClicked"/>, the same event as a right click (the move/attack order, the marker menu); the release then selects nothing.</summary>
    private void TickLongPress(double delta)
    {
        if (!touchPress || pressAt == null || dragging || longPressed || pinch.Active) return;
        pressHeld += delta;
        if (pressHeld < UiTokens.LongPressSeconds) return;
        longPressed = true;
        RightClickAt = pressAt.Value;
        TileRightClicked?.Invoke(PickTile(pressAt.Value));
    }

    private void OnMouseButton(InputEventMouseButton button)
    {
        if (button.Pressed)
        {
            pressAt = button.Position;
            dragging = button.ButtonIndex == MouseButton.Middle;
            touchPress = DisplayProfile.Current.Touch && button.Device == InputEvent.DeviceIdEmulation && button.ButtonIndex == MouseButton.Left; // C096: a finger on a touch build, as the emulated left button
            longPressed = false;
            pressHeld = 0;
            return;
        }
        var wasClick = pressAt != null && !dragging && !longPressed && button.ButtonIndex == MouseButton.Left;
        CancelPress();
        if (!wasClick) return;
        var picked = PickTile(button.Position);
        LastClickShift = button.ShiftPressed;
        ClickAt = button.Position;
        Select(picked);
        TileClicked?.Invoke(picked);
    }

    private void OnDrag(InputEventMouseMotion motion)
    {
        if (!dragging && pressAt is { } start && start.DistanceTo(motion.Position) > (touchPress ? TouchDragThreshold() : DragThresholdPixels)) dragging = true;
        if (!dragging || longPressed) return; // after a long press the finger only holds the menu or order it opened
        // Keep the ground point that was under the cursor under it: move the camera by the difference of the two picks.
        var before = PickGround(motion.Position - motion.Relative, 0f);
        var after = PickGround(motion.Position, 0f);
        if (before != null && after != null) PanBy(before.Value - after.Value);
    }
}
