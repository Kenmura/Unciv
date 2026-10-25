using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Assets;
using Unciv.Logic.Map;
using Unciv.Net.Visibility;

namespace Unciv.GodotClient.Map;

/// <summary>One city on the map: a block, and a banner above it; remembers the snapshot row it was drawn from so the screen and tests ask the view, not the engine.</summary>
public partial class CityNode : Node3D
{
    public CityDto City { get; set; } = null!; // JUSTIFY: C041 set by MapOverlayLayer right after construction

    /// <summary>The banner text as drawn.</summary>
    public string BannerText => GetNodeOrNull<Label3D>("Banner")?.Text ?? "";
}

/// <summary>
/// C041: cities, territory borders, roads and improvements on the flat map, drawn from the viewer's snapshot only. What to draw comes from <see cref="MapOverlays"/>
/// (Unciv.Assets, tested); this class only makes meshes of it, all from generated placeholders. Rebuilt in full after every snapshot change. C032 replaces it for the 3D chain.
/// </summary>
public partial class MapOverlayLayer : Node3D
{
    private const float BorderWidth = 0.1f;
    private const float BorderLift = 0.03f;
    private const float RoadWidth = 0.22f;
    private const float RoadLift = 0.02f;
    private const float MarkerSize = 0.3f;
    private const float CityDepthOffset = -0.3f;
    private const float BannerPixelSize = 0.005f;

    private readonly Dictionary<HexCoord, CityNode> cities = [];

    public IReadOnlyDictionary<HexCoord, CityNode> Cities => cities;

    public int BorderEdgeCount { get; private set; }

    public int RoadLinkCount { get; private set; }

    public int ImprovementCount { get; private set; }

    public int PillagedImprovementCount { get; private set; }

    /// <summary>Replaces everything drawn. <paramref name="colourOf"/> gives a civ's own colour (borders, city blocks, banner outlines), null for an unknown civ.</summary>
    public void Rebuild(MapView map, IReadOnlyList<TileDto> tiles, IReadOnlyList<CityDto> cityRows, Func<string, Rgb?> colourOf)
    {
        foreach (var child in GetChildren()) child.QueueFree();
        cities.Clear();
        float TopOf(int x, int y) => TerrainRelief.TopHeight(map.TileNodeAt(new HexCoord(x, y))?.Relief ?? ReliefKind.Flat);
        float RimOf(int x, int y) => TerrainRelief.RimHeight(map.TileNodeAt(new HexCoord(x, y))?.Relief ?? ReliefKind.Flat);

        var borders = MapOverlays.BorderEdges(tiles);
        BorderEdgeCount = borders.Count;
        foreach (var group in borders.GroupBy(it => it.Owner))
        {
            var ribbon = new Ribbon();
            foreach (var edge in group)
            {
                var (a, b) = MapOverlays.EdgeEnds(edge.X, edge.Y, edge.Dx, edge.Dy);
                float y = RimOf(edge.X, edge.Y) + BorderLift;
                ribbon.Add(new Vector3(a.X, y, a.Z), new Vector3(b.X, y, b.Z), BorderWidth);
            }
            AddChild(ribbon.ToNode("Border_" + group.Key, Unlit(colourOf(group.Key) is { } c ? ProceduralAssets.ToColor(c) : ProceduralAssets.ToColor(PlaceholderPalette.Light))));
        }

        var links = MapOverlays.RoadLinks(tiles, cityRows);
        RoadLinkCount = links.Count;
        foreach (var rail in new[] { false, true })
        {
            var ribbon = new Ribbon();
            foreach (var link in links.Where(it => it.Railroad == rail))
            {
                var from = MapLayout.TileTop(link.X, link.Y, 0f) + new Vector3(0f, TopOf(link.X, link.Y) + RoadLift, 0f);
                var to = link.Dx == 0 && link.Dy == 0
                    ? from + new Vector3(0.001f, 0f, 0.001f) // a stub: a dot the width of the road
                    : MapLayout.TileTop(link.X + link.Dx, link.Y + link.Dy, 0f) + new Vector3(0f, TopOf(link.X + link.Dx, link.Y + link.Dy) + RoadLift, 0f);
                ribbon.Add(from, to, RoadWidth);
            }
            AddChild(ribbon.ToNode(rail ? "Railroads" : "Roads", Unlit(ProceduralAssets.ToColor(rail ? PlaceholderPalette.RailGrey : PlaceholderPalette.RoadBrown))));
        }

        var improvements = MapOverlays.Improvements(tiles);
        ImprovementCount = improvements.Count;
        PillagedImprovementCount = improvements.Count(it => it.Pillaged);
        foreach (var marker in improvements) AddChild(ImprovementNode(marker, TopOf(marker.X, marker.Y)));

        foreach (var city in cityRows)
        {
            var node = CityNodeFor(city, TopOf(city.X, city.Y), colourOf(city.Owner));
            AddChild(node);
            cities[new HexCoord(city.X, city.Y)] = node;
        }
    }

    private static StandardMaterial3D Unlit(Color colour) => new()
    {
        AlbedoColor = colour,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };

    private static MeshInstance3D Block(string name, Vector3 size, Vector3 at, Color colour) => new()
    {
        Name = name,
        Mesh = new BoxMesh { Size = size },
        Position = at + new Vector3(0f, size.Y / 2f, 0f),
        MaterialOverride = new StandardMaterial3D { AlbedoColor = colour, Roughness = 1f },
    };

    private static Node3D ImprovementNode(ImprovementMarker marker, float top)
    {
        var colour = ProceduralAssets.ToColor(marker.Pillaged ? PlaceholderPalette.FromHash(marker.Name, 170, 220).Scale(35) : PlaceholderPalette.FromHash(marker.Name, 170, 220));
        var root = new Node3D { Name = $"Improvement_{marker.X}_{marker.Y}", Position = MapLayout.TileTop(marker.X, marker.Y, 0f) + new Vector3(0.45f, top, 0.4f) };
        root.AddChild(Block("Marker", new Vector3(MarkerSize, MarkerSize * 0.6f, MarkerSize), Vector3.Zero, colour));
        root.AddChild(new Label3D
        {
            Name = "Initial",
            Text = marker.Name[..1] + (marker.Pillaged ? "!" : ""),
            PixelSize = 0.006f,
            FontSize = 48,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            Shaded = false,
            Position = new Vector3(0f, MarkerSize * 0.6f + 0.15f, 0f),
        });
        return root;
    }

    private static CityNode CityNodeFor(CityDto city, float top, Rgb? ownerColour)
    {
        var civ = ProceduralAssets.ToColor(ownerColour ?? PlaceholderPalette.Light);
        var height = MapOverlays.CityBlockHeight(city.Population);
        var node = new CityNode { Name = $"City_{city.X}_{city.Y}", City = city, Position = MapLayout.TileTop(city.X, city.Y, 0f) + new Vector3(0f, top, 0f) };
        var at = new Vector3(0f, 0f, CityDepthOffset);
        node.AddChild(Block("Walls", new Vector3(0.9f, height, 0.55f), at, ProceduralAssets.ToColor(PlaceholderPalette.CityWall)));
        node.AddChild(Block("Roof", new Vector3(1.0f, 0.12f, 0.65f), at + new Vector3(0f, height, 0f), civ));
        var banner = new Label3D
        {
            Name = "Banner",
            Text = string.Join("\n", [MapOverlays.Banner(city).Title, .. MapOverlays.Banner(city).Details]),
            PixelSize = BannerPixelSize,
            FontSize = 40,
            OutlineSize = 10,
            OutlineModulate = civ.Darkened(0.55f),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            Shaded = false,
            Position = new Vector3(0f, height + 0.9f, CityDepthOffset),
        };
        node.AddChild(banner);
        return node;
    }

    /// <summary>Flat strips on the ground plane, many per mesh; double sided so the winding does not matter.</summary>
    private sealed class Ribbon
    {
        private readonly List<Vector3> vertices = [];
        private readonly List<int> indices = [];

        public void Add(Vector3 from, Vector3 to, float width)
        {
            var along = to - from;
            var side = new Vector3(-along.Z, 0f, along.X);
            side = side.LengthSquared() < 1e-12f ? new Vector3(0f, 0f, width / 2f) : side.Normalized() * (width / 2f);
            int at = vertices.Count;
            vertices.Add(from - side);
            vertices.Add(from + side);
            vertices.Add(to + side);
            vertices.Add(to - side);
            indices.AddRange([at, at + 1, at + 2, at, at + 2, at + 3]);
        }

        public MeshInstance3D ToNode(string name, Material material)
        {
            var node = new MeshInstance3D { Name = name, MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            if (vertices.Count == 0) return node;
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
            arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
            var mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            node.Mesh = mesh;
            return node;
        }
    }
}
