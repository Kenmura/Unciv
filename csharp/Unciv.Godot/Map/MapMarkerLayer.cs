using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Assets;
using Unciv.Logic.Map;

namespace Unciv.GodotClient.Map;

/// <summary>
/// C089: the player's private tacks drawn over the map: a small pin (a needle and a coloured head, with the icon's glyph on the head) and the label under a billboard.
/// Given the markers to show, never reading the engine or the settings itself; rebuilt in full by <see cref="Show"/>.
/// </summary>
public partial class MapMarkerLayer : Node3D
{
    private const float HeadHeight = 0.5f;
    private const float HeadRadius = 0.18f;
    private const float NeedleRadius = 0.025f;
    private const float PixelSize = 0.007f;

    private readonly Dictionary<HexCoord, MapMarker> shown = [];

    /// <summary>The markers drawn now, by tile.</summary>
    public IReadOnlyDictionary<HexCoord, MapMarker> Shown => shown;

    public Node3D? PinAt(HexCoord coord) => GetNodeOrNull<Node3D>(NodeName(coord));

    /// <summary>The label text drawn for a tile's marker ("" when it has none or the tile has no marker).</summary>
    public string LabelAt(HexCoord coord) => PinAt(coord)?.GetNodeOrNull<Label3D>("Label")?.Text ?? "";

    public void Show(IReadOnlyList<MapMarker> markers, Func<HexCoord, Vector3> top)
    {
        Clear();
        foreach (var marker in markers)
        {
            var coord = new HexCoord(marker.X, marker.Y);
            shown[coord] = marker;
            var colour = ProceduralAssets.ToColor(MapMarkerModel.ColourOf(marker.Icon));
            var material = new StandardMaterial3D { AlbedoColor = colour, Roughness = 0.6f, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
            var pin = new Node3D { Name = NodeName(coord), Position = top(coord) };
            pin.AddChild(new MeshInstance3D
            {
                Name = "Needle",
                Mesh = new CylinderMesh { TopRadius = NeedleRadius, BottomRadius = 0f, Height = HeadHeight, RadialSegments = 8 },
                MaterialOverride = material,
                Position = new Vector3(0f, HeadHeight / 2f, 0f),
            });
            pin.AddChild(new MeshInstance3D
            {
                Name = "Head",
                Mesh = new SphereMesh { Radius = HeadRadius, Height = HeadRadius * 2f, RadialSegments = 12, Rings = 6 },
                MaterialOverride = material,
                Position = new Vector3(0f, HeadHeight + HeadRadius * 0.6f, 0f),
            });
            string glyph = MapMarkerModel.GlyphOf(marker.Icon);
            if (glyph.Length > 0) pin.AddChild(Text("Glyph", glyph, new Vector3(0f, HeadHeight + HeadRadius * 0.6f, HeadRadius + 0.01f), 48, 8, colour.Darkened(0.6f)));
            if (marker.Label.Length > 0) pin.AddChild(Text("Label", marker.Label, new Vector3(0f, HeadHeight + HeadRadius * 2f + 0.14f, 0f), 40, 10, colour.Darkened(0.6f)));
            AddChild(pin);
        }
    }

    public void Clear()
    {
        shown.Clear();
        foreach (var child in GetChildren())
        {
            RemoveChild(child); // at once, so the count is right in the same frame
            child.QueueFree();
        }
    }

    private static string NodeName(HexCoord coord) => $"Marker_{coord.X}_{coord.Y}";

    private static Label3D Text(string name, string text, Vector3 at, int size, int outline, Color outlineColour) => new()
    {
        Name = name,
        Text = text,
        PixelSize = PixelSize,
        FontSize = size,
        OutlineSize = outline,
        OutlineModulate = outlineColour,
        Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
        NoDepthTest = true,
        Shaded = false,
        Position = at,
    };
}
