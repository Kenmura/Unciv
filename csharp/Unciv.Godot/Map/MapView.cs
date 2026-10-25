using Godot;
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
    public const float MinZoom = 4f;
    public const float MaxZoom = 40f;
    public const float DefaultZoom = 14f;
    private const float CameraDistance = 60f;
    private const float WheelStep = 1.12f;
    private const float KeyPanPerSecond = 1.2f;
    private const float DragThresholdPixels = 4f;
    private const int CandidateRadius = 2;
    private const float HillElevation = 3f;
    private const float MountainElevation = 6f;

    private readonly Dictionary<HexCoord, TileNode> tiles = [];
    private Node3D tileRoot = null!; // JUSTIFY: C009 assigned in _Ready before any use
    private TileHighlight highlight = null!; // JUSTIFY: C009 assigned in _Ready before any use
    private Vector3 focus;
    private float zoom = DefaultZoom;
    private Vector2? pressAt;
    private bool dragging;

    public Camera3D Camera { get; private set; } = null!; // JUSTIFY: C009 assigned in _Ready before any use

    public HexCoord? Selected { get; private set; }

    /// <summary>Raised on a click (press and release without dragging); null when the click hit no drawn tile.</summary>
    public event Action<HexCoord?>? TileClicked;

    /// <summary>Raised on a right click (C010: the quick "move there" order); the tile is picked but not selected.</summary>
    public event Action<HexCoord?>? TileRightClicked;

    public float Zoom => zoom;

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
        Camera = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Far = 200f, Current = true, RotationDegrees = new Vector3(-TiltDegrees, 0f, 0f) };
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

        var hex = assets.Get<Mesh>(RulesetAssetKeys.HexMesh);
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
            var elevation = ElevationOf(tile);
            var coord = new HexCoord(tile.X, tile.Y);
            var node = new TileNode
            {
                Name = $"Tile_{tile.X}_{tile.Y}",
                Coord = coord,
                Fog = tile.Visible ? TileFog.Visible : TileFog.Explored,
                Elevation = elevation,
                Mesh = hex,
                MaterialOverride = material,
                Position = MapLayout.TileTop(tile.X, tile.Y, 0f),
                Scale = new Vector3(1f, elevation, 1f),
            };
            tileRoot.AddChild(node);
            tiles[coord] = node;
        }
        if (tiles.Count > 0 && !keepCamera) FocusOn(Centre());
    }

    /// <summary>The feature or natural wonder drawn over the base terrain; hills are shown by height instead.</summary>
    private static string? OverlayOf(TileDto tile) =>
        tile.NaturalWonder ?? tile.Features.FirstOrDefault(it => it != HillFeature);

    private const string HillFeature = "Hill";
    private const string MountainTerrain = "Mountain";

    private static float ElevationOf(TileDto tile) =>
        tile.BaseTerrain == MountainTerrain ? MountainElevation : tile.Features.Contains(HillFeature) ? HillElevation : 1f;

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

    private void UpdateCamera()
    {
        if (Camera == null) return;
        Camera.Size = zoom;
        Camera.Position = focus + Camera.Basis.Z * CameraDistance;
    }

    /// <summary>
    /// The drawn tile under a screen position, or null if the ray misses the ground or no drawn tile is hit. Raised tiles (hills, mountains) are hit on
    /// their own top face: every tile near the flat-plane hit is tested at its real elevation and the one the ray reaches first (highest hit) wins.
    /// </summary>
    public HexCoord? PickTile(Vector2 screenPosition)
    {
        var flat = PickGround(screenPosition, ProceduralAssets.HexHeight);
        if (flat == null) return null;
        HexCoord? best = null;
        var bestHeight = float.MinValue;
        foreach (var candidate in HexMath.GetHexCoordsInDistance(MapLayout.TileAt(flat.Value), CandidateRadius, false))
        {
            if (!tiles.TryGetValue(candidate, out var node)) continue;
            var top = ProceduralAssets.HexHeight * node.Elevation;
            var hit = PickGround(screenPosition, top);
            if (hit == null || MapLayout.TileAt(hit.Value) != candidate) continue;
            if (top > bestHeight)
            {
                best = candidate;
                bestHeight = top;
            }
        }
        return best;
    }

    private Vector3? PickGround(Vector2 screenPosition, float planeY)
    {
        var origin = Camera.ProjectRayOrigin(screenPosition);
        var direction = Camera.ProjectRayNormal(screenPosition);
        return new Plane(Vector3.Up, planeY).IntersectsRay(origin, direction);
    }

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

    public override void _Process(double delta)
    {
        var direction = Vector3.Zero;
        if (Input.IsPhysicalKeyPressed(Key.A) || Input.IsPhysicalKeyPressed(Key.Left)) direction.X -= 1f;
        if (Input.IsPhysicalKeyPressed(Key.D) || Input.IsPhysicalKeyPressed(Key.Right)) direction.X += 1f;
        if (Input.IsPhysicalKeyPressed(Key.W) || Input.IsPhysicalKeyPressed(Key.Up)) direction.Z -= 1f;
        if (Input.IsPhysicalKeyPressed(Key.S) || Input.IsPhysicalKeyPressed(Key.Down)) direction.Z += 1f;
        if (direction != Vector3.Zero) PanBy(direction.Normalized() * (float)(KeyPanPerSecond * zoom * delta));
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
