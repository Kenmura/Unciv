using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Assets;
using Unciv.Logic.Map;

namespace Unciv.GodotClient.Map;

/// <summary>C012: a set of translucent hexes laid on tile faces in one colour (the reachable-tile and the attack-range overlay). Tiles are given, never computed here.</summary>
public partial class TileOverlay : Node3D
{
    private const float Lift = 0.008f;
    private const float FaceFill = 0.9f;

    private Mesh? hex;
    private StandardMaterial3D? material;
    private readonly Rgb tint;

    public TileOverlay(Rgb tint)
    {
        this.tint = tint;
    }

    public IReadOnlyList<HexCoord> Tiles { get; private set; } = [];

    public void Setup(AssetRegistry registry)
    {
        hex = registry.Get<Mesh>(RulesetAssetKeys.HexMesh);
        material = MapMaterials.Overlay(tint);
    }

    /// <summary>Replaces the overlay with a hex on each of <paramref name="coords"/>; <paramref name="top"/> gives the face height point of a tile.</summary>
    public void Show(IReadOnlyList<HexCoord> coords, Func<HexCoord, Vector3> top)
    {
        Clear();
        if (hex == null) return;
        Tiles = coords;
        foreach (var coord in coords)
        {
            var point = top(coord);
            AddChild(new MeshInstance3D
            {
                Mesh = hex,
                MaterialOverride = material,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Scale = new Vector3(FaceFill, 0.04f, FaceFill),
                Position = new Vector3(point.X, point.Y + Lift, point.Z),
            });
        }
    }

    public void Clear()
    {
        Tiles = [];
        foreach (var child in GetChildren())
        {
            RemoveChild(child); // at once, so the count is right in the same frame
            child.QueueFree();
        }
    }
}
