using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Assets;
using Unciv.Logic.Map;
using Unciv.Models.Ruleset;
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
/// C041/C032: cities, territory borders, roads, improvements and in-world tile yields on the 3D map, drawn from the viewer's snapshot only. What to draw comes from <see cref="MapOverlays"/>,
/// <see cref="ImprovementSpecs"/>, <see cref="CitySpecs"/> and <see cref="TileYieldModel"/> (Unciv.Assets, tested); this class only makes nodes of it. Improvements and cities are registry props
/// (<c>improvement/&lt;name&gt;</c>, replaceable by key), borders and roads are ribbons in the owner's colour, yields are billboard icons with an amount. Rebuilt in full after every snapshot change.
/// </summary>
public partial class MapOverlayLayer : Node3D
{
    private const float BorderLift = 0.03f;
    private const float RoadWidth = 0.22f;
    private const float RoadLift = 0.02f;
    private const float RoadLineWidth = 0.04f;
    private const float RailWidth = 0.03f;
    private const float RailGauge = 0.07f;
    private const float CityScale = 0.85f;
    private const float CrowdedScale = 0.5f;
    private const float CrowdedOffsetX = 0.5f;
    private const float CrowdedOffsetZ = 0.4f;
    private const float PillagedLeanDegrees = 14f;
    private const float YieldLift = 0.02f;
    private const float YieldIconPixelSize = 0.0036f;
    private const float YieldTextPixelSize = 0.0035f;
    private const float BannerPixelSize = 0.005f;

    private readonly Dictionary<HexCoord, CityNode> cities = [];

    public IReadOnlyDictionary<HexCoord, CityNode> Cities => cities;

    public int BorderEdgeCount { get; private set; }

    private BorderStrips? borderStrips;
    private MapView? borderMap;
    private float uiScale = 1f;

    /// <summary>The border strips of the last <see cref="Rebuild"/>, or null before it.</summary>
    public BorderStrips? Borders => borderStrips;

    /// <summary>T377: the effective UI scale (<c>UiTheme.Scale</c>) the border band width honours; set it again when the scale changes.</summary>
    public float UiScale
    {
        get => uiScale;
        set
        {
            uiScale = value;
            ApplyBorderView();
        }
    }

    private void ApplyBorderView()
    {
        if (borderStrips == null || !IsInstanceValid(borderStrips) || borderMap == null || !borderMap.IsInsideTree() || borderMap.Camera == null) return;
        borderStrips.ApplyView(borderMap.Camera, borderMap.Camera.GetViewport().GetVisibleRect().Size, uiScale);
    }

    public int RoadLinkCount { get; private set; }

    public int ImprovementCount { get; private set; }

    public int PillagedImprovementCount { get; private set; }

    /// <summary>Yield icons drawn (one per stat per tile), whether or not they are shown right now.</summary>
    public int YieldChipCount { get; private set; }

    /// <summary>Tiles that have at least one yield icon.</summary>
    public int YieldTileCount { get; private set; }

    private Node3D? yieldRoot;
    private bool showYields = true;
    private bool yieldsFar;

    /// <summary>C032: whether the yield icons are shown (the "Show tile yields" setting).</summary>
    public bool ShowYields
    {
        get => showYields;
        set
        {
            showYields = value;
            ApplyYieldVisibility();
        }
    }

    /// <summary>T287: whether the camera is too far out for the yield icons (see <see cref="FarZoomLod"/>); they are hidden then, which saves two draw calls per icon.</summary>
    public bool YieldsFar
    {
        get => yieldsFar;
        set
        {
            yieldsFar = value;
            ApplyYieldVisibility();
        }
    }

    private void ApplyYieldVisibility()
    {
        if (yieldRoot != null) yieldRoot.Visible = showYields && !yieldsFar;
    }

    /// <summary>Yield icons visible right now (zero when the setting is off or the camera is too far out).</summary>
    public int VisibleYieldChipCount => showYields && !yieldsFar ? YieldChipCount : 0;

    /// <summary>The improvement prop standing on a tile, or null.</summary>
    public Node3D? ImprovementAt(HexCoord coord) => GetNodeOrNull<Node3D>($"Improvement_{coord.X}_{coord.Y}");

    /// <summary>The yield chips drawn on a tile (stat and amount), empty when none.</summary>
    public IReadOnlyList<YieldChip> YieldsAt(HexCoord coord) => yieldChips.GetValueOrDefault(coord) ?? [];

    private readonly Dictionary<HexCoord, IReadOnlyList<YieldChip>> yieldChips = [];

    /// <summary>Replaces everything drawn. <paramref name="colourOf"/> gives a civ's own colour (borders, city blocks, banner outlines), null for an unknown civ.</summary>
    public void Rebuild(AssetRegistry assets, Ruleset ruleset, MapView map, IReadOnlyList<TileDto> tiles, IReadOnlyList<CityDto> cityRows, Func<string, Rgb?> colourOf, TileOwnerMemory? owners = null, Func<string, Rgb?>? innerColourOf = null)
    {
        foreach (var child in GetChildren())
        {
            RemoveChild(child); // out of the tree now, so the new nodes keep their names (lookups by name) while the old ones wait to be freed
            child.QueueFree();
        }
        cities.Clear();
        yieldChips.Clear();
        yieldRoot = null;
        float TopOf(int x, int y) => TerrainRelief.TopHeight(map.TileNodeAt(new HexCoord(x, y))?.Relief ?? ReliefKind.Flat);
        float RimOf(int x, int y) => TerrainRelief.RimHeight(map.TileNodeAt(new HexCoord(x, y))?.Relief ?? ReliefKind.Flat);

        var borders = MapOverlays.BorderEdges(tiles, owners);
        BorderEdgeCount = borders.Count;
        var visibleAt = tiles.ToDictionary(it => new HexCoord(it.X, it.Y), it => it.Visible);
        float SideHeight(BorderEdge edge) => Math.Max(RimOf(edge.X, edge.Y), RimOf(edge.X + edge.Dx, edge.Y + edge.Dy)) + BorderLift; // both civs of a shared side lie level
        float SideOpacity(BorderEdge edge) => visibleAt.GetValueOrDefault(new HexCoord(edge.X, edge.Y), true) ? 1f : BorderStyle.RememberedOpacity;
        var strips = new BorderStrips { Name = "Borders" };
        foreach (var group in BorderChains.Build(borders).GroupBy(it => it.Owner))
        {
            var outer = colourOf(group.Key) ?? PlaceholderPalette.Light;
            var casing = BorderStyle.CasingFor(outer, innerColourOf?.Invoke(group.Key) ?? new Rgb(255, 255, 255));
            strips.Add(group.Key, BorderMesh.Build([.. group], SideHeight, SideOpacity), ProceduralAssets.ToColor(outer), ProceduralAssets.ToColor(casing));
        }
        AddChild(strips);
        borderStrips = strips;
        borderMap = map;
        map.ViewChanged -= ApplyBorderView;
        map.ViewChanged += ApplyBorderView;
        ApplyBorderView();

        var links = MapOverlays.RoadLinks(tiles, cityRows);
        RoadLinkCount = links.Count;
        // A road is a brown strip with a light dashed-looking centre line; a railroad a dark bed with two light rails
        var road = new Ribbon();
        var roadLine = new Ribbon();
        var railBed = new Ribbon();
        var rails = new Ribbon();
        foreach (var link in links)
        {
            var from = MapLayout.TileTop(link.X, link.Y, 0f) + new Vector3(0f, TopOf(link.X, link.Y) + RoadLift, 0f);
            var to = link.Dx == 0 && link.Dy == 0
                ? from + new Vector3(0.001f, 0f, 0.001f) // a stub: a dot the width of the road
                : MapLayout.TileTop(link.X + link.Dx, link.Y + link.Dy, 0f) + new Vector3(0f, TopOf(link.X + link.Dx, link.Y + link.Dy) + RoadLift, 0f);
            if (link.Railroad)
            {
                railBed.Add(from, to, RoadWidth);
                rails.Add(from + new Vector3(0f, RoadLift / 2f, 0f), to + new Vector3(0f, RoadLift / 2f, 0f), RailWidth, RailGauge);
                rails.Add(from + new Vector3(0f, RoadLift / 2f, 0f), to + new Vector3(0f, RoadLift / 2f, 0f), RailWidth, -RailGauge);
            }
            else
            {
                road.Add(from, to, RoadWidth);
                roadLine.Add(from + new Vector3(0f, RoadLift / 2f, 0f), to + new Vector3(0f, RoadLift / 2f, 0f), RoadLineWidth);
            }
        }
        AddChild(road.ToNode("Roads", Unlit(ProceduralAssets.ToColor(PlaceholderPalette.RoadBrown))));
        AddChild(roadLine.ToNode("RoadLines", Unlit(ProceduralAssets.ToColor(PlaceholderPalette.RoadBrown.Scale(125)))));
        AddChild(railBed.ToNode("Railroads", Unlit(ProceduralAssets.ToColor(PlaceholderPalette.RailGrey))));
        AddChild(rails.ToNode("Rails", Unlit(ProceduralAssets.ToColor(PlaceholderPalette.RailGrey.Scale(190)))));

        var improvements = MapOverlays.Improvements(tiles);
        ImprovementCount = improvements.Count;
        PillagedImprovementCount = improvements.Count(it => it.Pillaged);
        var byCoord = tiles.ToDictionary(it => new HexCoord(it.X, it.Y));
        foreach (var marker in improvements)
            AddChild(ImprovementNode(assets, marker, byCoord[new HexCoord(marker.X, marker.Y)], TopOf(marker.X, marker.Y)));

        foreach (var city in cityRows)
        {
            var visible = byCoord.TryGetValue(new HexCoord(city.X, city.Y), out var cityTile) && cityTile.Visible;
            var node = CityNodeFor(assets, city, TopOf(city.X, city.Y), colourOf(city.Owner), visible);
            AddChild(node);
            cities[new HexCoord(city.X, city.Y)] = node;
        }

        BuildYields(assets, ruleset, tiles, cityRows, TopOf);
    }

    private void BuildYields(AssetRegistry assets, Ruleset ruleset, IReadOnlyList<TileDto> tiles, IReadOnlyList<CityDto> cityRows, Func<int, int, float> topOf)
    {
        yieldRoot = new Node3D { Name = "Yields", Visible = showYields && !yieldsFar };
        AddChild(yieldRoot);
        YieldChipCount = 0;
        YieldTileCount = 0;
        var cityTiles = cityRows.Select(it => new HexCoord(it.X, it.Y)).ToHashSet();
        var fog = ProceduralAssets.ToColor(PlaceholderPalette.FogDim);
        foreach (var tile in tiles)
        {
            var coord = new HexCoord(tile.X, tile.Y);
            if (cityTiles.Contains(coord)) continue; // the city model and banner stand there
            var chips = TileYieldModel.Chips(ruleset, tile);
            if (chips.Count == 0) continue;
            yieldChips[coord] = chips;
            YieldTileCount++;
            var origin = MapLayout.TileTop(tile.X, tile.Y, 0f) + new Vector3(0f, topOf(tile.X, tile.Y) + YieldLift, 0f);
            var row = new Node3D { Name = $"Yield_{tile.X}_{tile.Y}", Position = origin };
            for (var i = 0; i < chips.Count; i++)
            {
                var (dx, dz) = TileYieldModel.ChipOffset(i, chips.Count);
                var chip = new Sprite3D
                {
                    Name = "Chip" + i,
                    Texture = assets.Get<Texture2D>(AssetKey.Of(RulesetAssetKeys.StatIcon, chips[i].Stat.ToString())),
                    Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                    PixelSize = YieldIconPixelSize,
                    TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
                    NoDepthTest = true,
                    Shaded = false,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, // T287: a billboard icon's shadow is nothing to see and doubles its draw calls
                    Position = new Vector3(dx, 0f, dz),
                };
                chip.AddChild(new Label3D
                {
                    Name = "Amount",
                    Text = chips[i].Amount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    PixelSize = YieldTextPixelSize,
                    FontSize = 40,
                    OutlineSize = 12,
                    Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                    NoDepthTest = true,
                    Shaded = false,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                    Position = new Vector3(0f, -0.17f, 0.001f),
                });
                if (!tile.Visible)
                {
                    chip.Modulate = fog;
                    chip.GetNode<Label3D>("Amount").Modulate = fog;
                }
                row.AddChild(chip);
                YieldChipCount++;
            }
            yieldRoot.AddChild(row);
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

    /// <summary>
    /// The improvement's registry prop (<c>improvement/&lt;name&gt;</c>) on the tile centre; beside a resource prop or a feature ring it is shrunk and pushed to the side so both read.
    /// A pillaged one leans over and is darkened, a remembered one on a fogged tile dims like its terrain.
    /// </summary>
    private static Node3D ImprovementNode(AssetRegistry assets, ImprovementMarker marker, TileDto tile, float top)
    {
        var model = assets.Get<Node3D>(AssetKey.Of(RulesetAssetKeys.Improvement, marker.Name));
        model.Name = $"Improvement_{marker.X}_{marker.Y}";
        var crowded = tile.Resource != null;
        var scale = crowded ? CrowdedScale : 1f;
        model.Scale = new Vector3(scale, scale, scale);
        model.Position = MapLayout.TileTop(marker.X, marker.Y, 0f) + new Vector3(crowded ? CrowdedOffsetX : 0f, top, crowded ? CrowdedOffsetZ : 0f);
        if (!tile.Visible) MapMaterials.Dim(model);
        if (marker.Pillaged)
        {
            MapMaterials.Dim(model);
            MapMaterials.Dim(model);
            model.RotationDegrees = new Vector3(0f, 0f, PillagedLeanDegrees);
        }
        return model;
    }

    private static CityNode CityNodeFor(AssetRegistry assets, CityDto city, float top, Rgb? ownerColour, bool visible)
    {
        var civ = ProceduralAssets.ToColor(ownerColour ?? PlaceholderPalette.Light);
        var tier = CitySpecs.TierForPopulation(city.Population);
        var node = new CityNode { Name = $"City_{city.X}_{city.Y}", City = city, Position = MapLayout.TileTop(city.X, city.Y, 0f) + new Vector3(0f, top, 0f) };
        var model = assets.Get<Node3D>(CitySpecs.KeyForTier(tier));
        model.Name = "Model";
        model.Scale = new Vector3(CityScale, CityScale, CityScale);
        if (!visible) MapMaterials.Dim(model);
        node.AddChild(model);
        var flagHeight = CitySpecs.FlagHeight(tier);
        node.AddChild(new MeshInstance3D
        {
            Name = "Pole",
            Mesh = new BoxMesh { Size = new Vector3(0.025f, flagHeight, 0.025f) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = ProceduralAssets.ToColor(PlaceholderPalette.CityWall.Scale(60)), Roughness = 1f },
            Position = new Vector3(0f, flagHeight / 2f, 0f),
        });
        node.AddChild(new MeshInstance3D
        {
            Name = "Flag",
            Mesh = new BoxMesh { Size = new Vector3(0.3f, 0.18f, 0.02f) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = civ, Roughness = 1f },
            Position = new Vector3(0.16f, flagHeight - 0.1f, 0f),
        });
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
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Position = new Vector3(0f, flagHeight + 0.3f, 0f),
        };
        node.AddChild(banner);
        return node;
    }

    /// <summary>Flat strips on the ground plane, many per mesh; double sided so the winding does not matter.</summary>
    private sealed class Ribbon
    {
        private readonly List<Vector3> vertices = [];
        private readonly List<int> indices = [];

        public void Add(Vector3 from, Vector3 to, float width, float lateral = 0f)
        {
            var along = to - from;
            var side = new Vector3(-along.Z, 0f, along.X);
            side = side.LengthSquared() < 1e-12f ? new Vector3(0f, 0f, width / 2f) : side.Normalized() * (width / 2f);
            if (lateral != 0f)
            {
                var shift = new Vector3(-along.Z, 0f, along.X);
                shift = shift.LengthSquared() < 1e-12f ? new Vector3(0f, 0f, lateral) : shift.Normalized() * lateral;
                from += shift;
                to += shift;
            }
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
