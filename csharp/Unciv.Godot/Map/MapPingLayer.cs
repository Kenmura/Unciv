using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Assets;
using Unciv.Logic.Map;
using Unciv.Models.Translations;
using Unciv.Utils;

namespace Unciv.GodotClient.Map;

/// <summary>
/// C090: the allies' map pings drawn over the map: a ring on the tile that grows and fades over the ping's life, a column above it and a billboard naming the sender and the kind.
/// Given the pings to show, never reading the engine or the network itself; <see cref="Sync"/> is called every frame so the ring follows the ping's remaining time.
/// </summary>
public partial class MapPingLayer : Node3D
{
    private const float RingStart = 0.35f;
    private const float RingEnd = 1.1f;
    private const float PixelSize = 0.008f;

    private readonly Dictionary<HexCoord, ActivePing> shown = [];

    /// <summary>The pings drawn now, by tile.</summary>
    public IReadOnlyDictionary<HexCoord, ActivePing> Shown => shown;

    public Node3D? PingAt(HexCoord coord) => GetNodeOrNull<Node3D>(NodeName(coord));

    /// <summary>The billboard text of a tile's ping ("" when there is none).</summary>
    public string LabelAt(HexCoord coord) => PingAt(coord)?.GetNodeOrNull<Label3D>("Label")?.Text ?? "";

    /// <summary>Draws exactly <paramref name="pings"/>: a ping's node is made once and then only animated, one that is gone is removed.</summary>
    public void Sync(IReadOnlyList<ActivePing> pings, Func<HexCoord, Vector3> top)
    {
        var wanted = pings.ToDictionary(it => new HexCoord(it.X, it.Y));
        foreach (var gone in shown.Keys.Where(it => !wanted.ContainsKey(it) || wanted[it].Sender != shown[it].Sender || wanted[it].Kind != shown[it].Kind).ToList())
        {
            shown.Remove(gone);
            if (PingAt(gone) is { } node)
            {
                RemoveChild(node); // at once, so the count is right in the same frame
                node.QueueFree();
            }
        }
        foreach (var (coord, ping) in wanted)
        {
            if (!shown.ContainsKey(coord)) Build(coord, ping, top(coord));
            shown[coord] = ping;
            Animate(PingAt(coord).NotNull(), ping);
        }
    }

    public void Clear() => Sync([], _ => Vector3.Zero);

    private void Build(HexCoord coord, ActivePing ping, Vector3 at)
    {
        var colour = ProceduralAssets.ToColor(MapPingModel.ColourOf(ping.Kind));
        Material Make() => new StandardMaterial3D { AlbedoColor = colour, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, NoDepthTest = true };
        var node = new Node3D { Name = NodeName(coord), Position = at };
        node.AddChild(new MeshInstance3D
        {
            Name = "Ring",
            Mesh = new TorusMesh { InnerRadius = 0.9f, OuterRadius = 1f, Rings = 24, RingSegments = 6 },
            MaterialOverride = Make(),
            Position = new Vector3(0f, 0.05f, 0f),
        });
        node.AddChild(new MeshInstance3D
        {
            Name = "Column",
            Mesh = new CylinderMesh { TopRadius = 0.03f, BottomRadius = 0.03f, Height = 1.2f, RadialSegments = 8 },
            MaterialOverride = Make(),
            Position = new Vector3(0f, 0.6f, 0f),
        });
        string who = string.IsNullOrEmpty(ping.Sender) ? "" : ping.Sender + ": ";
        node.AddChild(new Label3D
        {
            Name = "Label",
            Text = who + MapPingModel.TextOf(ping.Kind).Tr(),
            PixelSize = PixelSize,
            FontSize = 44,
            OutlineSize = 10,
            OutlineModulate = colour.Darkened(0.6f),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            Shaded = false,
            Position = new Vector3(0f, 1.4f, 0f),
        });
        AddChild(node);
    }

    private static void Animate(Node3D node, ActivePing ping)
    {
        float progress = MapPingModel.Progress(ping);
        float size = Mathf.Lerp(RingStart, RingEnd, progress);
        var ring = node.GetNode<MeshInstance3D>("Ring");
        ring.Scale = new Vector3(size, 1f, size);
        float alpha = 1f - progress * progress;
        foreach (var name in new[] { "Ring", "Column" })
        {
            var material = (StandardMaterial3D)node.GetNode<MeshInstance3D>(name).MaterialOverride;
            var faded = material.AlbedoColor;
            faded.A = alpha;
            material.AlbedoColor = faded;
        }
        var label = node.GetNode<Label3D>("Label");
        var shade = label.Modulate;
        shade.A = Mathf.Clamp(alpha * 1.5f, 0f, 1f);
        label.Modulate = shade;
    }

    private static string NodeName(HexCoord coord) => $"Ping_{coord.X}_{coord.Y}";
}
