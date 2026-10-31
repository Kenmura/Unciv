using Godot;
using Unciv.Assets;
using Unciv.Commands;
using Unciv.Harness;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Map;
using Unciv.Logic.Map;
using Unciv.Net.Visibility;
using Unciv.Utils;

namespace Unciv.GodotHarness;

/// <summary>
/// T298: territory borders. Japan's Osaka stretches north into fog the player explored earlier (the tiles are not in sight, their owner is the remembered one) and east to a shared edge with
/// Greece; the north rim is unexplored (absent from the snapshot). Asserts that no border runs along the line where sight ends, that both civs' ribbons exist on a shared edge, and that no
/// border sits on an unexplored tile; then captures the map from above-ish so the two ribbons of the shared edge can be told apart.
/// </summary>
public sealed class BordersScenario : Scenario
{
    public override string Name => "borders";

    public override bool Uses3D => true;

    public override IReadOnlyList<string> Checklist =>
    [
        "Does Japan's border continue unbroken across the line between the lit and the dimmed tiles (no line where only sight changes)?",
        "On the edge Japan and Greece share, are both colours visible side by side, each on its own side?",
        "Is there no border on the unexplored (missing) tiles at the top?",
    ];

    private static readonly HexCoord Osaka = new(0, 0);
    private static readonly HexCoord Athens = new(3, 3);

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session)
    {
        var assets = AssetRegistry.ForRuleset(session.Game.Ruleset);
        // What the player saw while every tile was in sight, then the same map with the northern half in the fog and the far north never explored
        var seen = Tiles(fogged: false);
        var memory = new TileOwnerMemory();
        memory.Remember(seen);
        var tiles = Tiles(fogged: true);

        var view = new MapView();
        host.AddChild(view);
        view.Load(assets, tiles);
        var layer = new MapOverlayLayer();
        host.AddChild(layer);
        Rgb? Colour(string owner) => owner == "Japan" ? new Rgb(230, 230, 235) : owner == "Greece" ? new Rgb(60, 110, 220) : null;
        layer.Rebuild(assets, session.Game.Ruleset, view, tiles, [], Colour, memory);

        var edges = MapOverlays.BorderEdges(tiles, memory);
        // An edge facing a tile whose sight differs and whose remembered owner is the same civ: the false border of the bug report
        var fogLine = edges.Where(it => tiles.FirstOrDefault(t => t.X == it.X + it.Dx && t.Y == it.Y + it.Dy) is { } other
            && other.Visible != Visible(tiles, it.X, it.Y) && memory.TryOwner(other, out var owner) && owner == it.Owner).ToList();
        var withoutMemory = MapOverlays.BorderEdges(tiles);

        var chains = BorderChains.Build(edges);
        var closed = chains.Where(it => it.Closed).ToList();
        RgbaImage? with = null;
        RgbaImage? without = null;
        var steps = new List<ScenarioStep>
        {
            new WaitFrames(3),
            new AssertQuery("the overlay draws exactly the border edges BorderEdges answers, in one ribbon per civ", () =>
                layer.BorderEdgeCount == edges.Count && layer.Borders?.GetNodeOrNull<MeshInstance3D>("Border_Japan") != null && layer.Borders.GetNodeOrNull<MeshInstance3D>("Border_Greece") != null),
            new AssertQuery("no border runs along the line where sight ends inside Japan's territory", () => fogLine.Count == 0),
            new AssertQuery("without the remembered owners the fogged territory would have no border of its own (what the memory adds)", () =>
                withoutMemory.Count < edges.Count && !withoutMemory.Any(it => !Visible(tiles, it.X, it.Y))),
            new AssertQuery("fogged Japanese tiles keep their remembered border", () => edges.Any(it => it.Owner == "Japan" && !Visible(tiles, it.X, it.Y))),
            new AssertQuery("Japan and Greece each draw their side of the shared edge", () =>
                edges.Any(it => it.Owner == "Japan" && it.X == 1 && it.Y == 1 && it is { Dx: 1, Dy: 1 }) && edges.Any(it => it.Owner == "Greece" && it.X == 2 && it.Y == 2 && it is { Dx: -1, Dy: -1 })),
            new AssertQuery("no border sits on a tile that was never explored", () => edges.All(it => tiles.Any(t => t.X == it.X && t.Y == it.Y))),
            new Act("frame the map", () =>
            {
                view.FocusOn(MapLayout.TileTop(1, 1, 0f));
                view.SetZoom(8f);
            }),
            new WaitFrames(2),
            new Capture("shared-and-fogged", "Japan (white) runs on into the dimmed, remembered tiles without a border where sight ends; at the shared edge with Greece (blue) both ribbons show side by side"),
            new Act("close on the shared edge", () =>
            {
                view.FocusOn(MapLayout.TileTop(2, 2, 0f));
                view.SetZoom(4.5f);
            }),
            new WaitFrames(2),
            new Capture("shared-edge-close", "The edge Japan and Greece share, close up: each border ribbon lies on its own side"),
        };
        // T377: measured on screen. Per camera: render with and without the strips and require paint at every side and every corner of every closed outline, and a width that does not depend on direction.
        foreach (var (tilt, yaw, zoom, scale, tag, caption) in new[] { (25f, 0f, 8f, 1f, "low-tilt", "Low tilt: the outlines stay closed and of one thickness"),
            (55f, 0f, 14f, 1.5f, "default-view", "The default view at 150% UI scale: thin two-tone outlines joined at every corner"),
            (80f, 30f, 10f, 1f, "steep-yawed", "Steep tilt and a turned map: the band keeps its width on slanted sides"),
            (55f, 17f, 20f, 2.5f, "far-zoom-large-ui", "Zoomed far out at 250% UI scale: the outline is still a few pixels wide and closed"),
            (40f, 60f, 6f, 1f, "close-yawed", "Zoomed in on a turned map: the outline is capped at four scaled pixels, corners mitred") })
        {
            var label = $"tilt {tilt} yaw {yaw} zoom {zoom} ui {scale}";
            steps.Add(new Act("view " + label, () =>
            {
                layer.UiScale = scale;
                view.SetView(tilt, yaw);
                view.FocusOn(MapLayout.TileTop(1, 1, 0f));
                view.SetZoom(zoom);
                layer.Borders.NotNull().Visible = true;
            }));
            steps.Add(new WaitFrames(3));
            steps.Add(new Capture(tag, caption));
            steps.Add(new Act("capture with " + label, () => with = ViewportCapture.Capture(host.GetViewport())));
            steps.Add(new Act("hide strips " + label, () => layer.Borders.NotNull().Visible = false));
            steps.Add(new WaitFrames(3));
            steps.Add(new Act("capture without " + label, () =>
            {
                without = ViewportCapture.Capture(host.GetViewport());
                layer.Borders.NotNull().Visible = true;
            }));
            steps.Add(new AssertQuery($"every side and corner of the closed outlines is painted ({label})", () =>
            {
                var result = BorderProbe.Run(view.Camera, with.NotNull(), without.NotNull(), closed, _ => true, layer.Borders.NotNull().BandPixels);
                GD.Print($"borders probe {label}: {result.Painted}/{result.Samples} painted, corners {result.CornersPainted}/{result.Corners}, band {layer.Borders.BandPixels}px {string.Join("; ", result.Misses)}");
                return result.Samples >= 20 && result.Corners >= 6 && result.Painted == result.Samples;
            }));
            steps.Add(new AssertQuery($"the band is as wide on a slanted side as on a horizontal one, within the measuring error of the probe ({label})", () =>
            {
                var widths = new List<float>();
                foreach (var chain in closed)
                {
                    for (var i = 0; i < chain.Sides.Count; i++)
                    {
                        // A side the neighbouring civ also borders carries two bands: measure the sides with one
                        if (tiles.Any(t => t.X == chain.Sides[i].X + chain.Sides[i].Dx && t.Y == chain.Sides[i].Y + chain.Sides[i].Dy && t.Owner != null && t.Owner != chain.Owner)) continue;
                        var w = BorderProbe.WidthAcross(view.Camera, with.NotNull(), without.NotNull(), chain.Points[i], chain.Points[(i + 1) % chain.Sides.Count]);
                        if (w > 0f) widths.Add(w);
                    }
                }
                GD.Print($"borders width {label}: min {widths.Min()} max {widths.Max()} band {layer.Borders.NotNull().BandPixels}");
                return widths.Count >= 12 && widths.Max() - widths.Min() <= 3f && Math.Abs(widths.Order().ElementAt(widths.Count / 2) - layer.Borders.BandPixels) <= 1f;
            }));
        }
        return steps;
    }

    private static bool Visible(IReadOnlyList<TileDto> tiles, int x, int y) => tiles.First(it => it.X == x && it.Y == y).Visible;

    private static string? OwnerOf(HexCoord c)
    {
        if (Dist(c, Athens) <= 1) return "Greece";
        return Dist(c, Osaka) <= 2 ? "Japan" : null;
    }

    /// <summary>The radius-3 map. Fogged: everything north of the row y = 0 is out of sight (no owner in the snapshot). Unexplored: the northern rim (y &lt;= -3) is left out entirely.</summary>
    private static List<TileDto> Tiles(bool fogged)
    {
        var tiles = new List<TileDto>();
        for (var x = -3; x <= 3; x++)
        {
            for (var y = -3; y <= 3; y++)
            {
                if (Math.Abs(x - y) > 3) continue;
                if (fogged && y <= -3) continue;
                var visible = !fogged || y >= 0;
                var coord = new HexCoord(x, y);
                tiles.Add(new TileDto
                {
                    X = x,
                    Y = y,
                    Visible = visible,
                    BaseTerrain = "Grassland",
                    Features = [],
                    Improvement = coord == Osaka || coord == Athens ? "City center" : null,
                    RiverBottom = false,
                    RiverBottomLeft = false,
                    RiverBottomRight = false,
                    Owner = visible ? OwnerOf(coord) : null,
                    Road = visible ? "None" : null,
                    RoadPillaged = visible ? false : null,
                });
            }
        }
        return tiles;
    }

    private static int Dist(HexCoord a, HexCoord b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Max(Math.Abs(dx), Math.Max(Math.Abs(dy), Math.Abs(dx - dy)));
    }
}
