using System.Globalization;
using Godot;
using Unciv.Assets;
using Unciv.Commands;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Map;
using Unciv.Net.Visibility;
using Unciv.Utils;

namespace Unciv.GodotHarness;

/// <summary>
/// T377: the real-GPU border review sheet. A mixed map (grass, plains, desert, snow, hills, mountains, forest, coast, ocean) with five territories, a fogged band and a never-seen band, rendered
/// off-screen at several window sizes and camera zooms, one PNG each (<c>borders-WxH-zoomZ[-tTILT].png</c>). Not part of CI and never compared to goldens: <c>tools/borders-sheet.sh</c> runs it on the host.
/// Environment: <c>UNCIV_BORDERS_OUT</c> (folder, required), <c>UNCIV_BORDERS_RES</c> (sizes, as the scale sheet), <c>UNCIV_BORDERS_ZOOMS</c> (comma separated camera sizes).
/// </summary>
public sealed class BordersSheetScenario : Scenario
{
    public override string Name => "borders-sheet";

    public override bool Uses3D => true;

    public override IReadOnlyList<string> Checklist => ["Open borders-*.png: does every territory read as one closed outline of constant thickness, joined at the hex corners, at every zoom and size?"];

    internal static readonly (string Name, Rgb Colour, Rgb Inner)[] Civs =
    [
        ("Black", new Rgb(15, 15, 15), new Rgb(255, 255, 255)), ("Red", new Rgb(220, 30, 60), new Rgb(255, 230, 230)), ("Green", new Rgb(30, 150, 50), new Rgb(255, 255, 255)), ("White", new Rgb(235, 235, 240), new Rgb(0, 0, 0)), ("Blue", new Rgb(40, 90, 230), new Rgb(255, 255, 255)),
    ];

    private static readonly (int X, int Y, string Civ)[] Capitals = [(0, 0, "Black"), (4, 2, "Red"), (-3, 3, "Green"), (3, -3, "White"), (-4, -2, "Blue")];

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session)
    {
        string outDir = GpuBenchScenario.Bench("UNCIV_BORDERS_OUT") ?? throw new InvalidOperationException("UNCIV_BORDERS_OUT is not set");
        Directory.CreateDirectory(outDir);
        var zooms = (GpuBenchScenario.Bench("UNCIV_BORDERS_ZOOMS") ?? "6,10,14,24,40").Split(',').Select(it => float.Parse(it, CultureInfo.InvariantCulture)).ToList();
        var tiles = Map(9);
        var memory = new TileOwnerMemory();
        if (GpuBenchScenario.Bench("UNCIV_BORDERS_MEMORY") != "empty") memory.Remember(tiles.Select(it => it with { Visible = true, Owner = OwnerOf(it.X, it.Y) }).ToList());
        var assets = AssetRegistry.ForRuleset(session.Game.Ruleset);
        var steps = new List<ScenarioStep>();
        foreach (var (width, height, os) in ScaleSheetScenario.Sizes("UNCIV_BORDERS_RES", "1366x768,1920x1080,3840x2160"))
        {
            var tag = $"{width}x{height}";
            SubViewport? sub = null;
            MapView? view = null;
            steps.Add(new Act("build " + tag, () =>
            {
                sub = new SubViewport { Size = new Vector2I(width, height), RenderTargetUpdateMode = SubViewport.UpdateMode.Always, TransparentBg = false, World3D = host.GetViewport().FindWorld3D() };
                host.AddChild(sub);
                view = new MapView();
                sub.AddChild(view);
                view.Load(assets, tiles);
                var layer = new MapOverlayLayer { Name = "Overlay", UiScale = UiTokens.AutoScale(width, height, os) };
                sub.AddChild(layer);
                layer.Rebuild(assets, session.Game.Ruleset, view, tiles, [], owner => Civs.Where(it => it.Name == owner).Select(it => (Rgb?)it.Colour).FirstOrDefault(), memory, owner => Civs.Where(it => it.Name == owner).Select(it => (Rgb?)it.Inner).FirstOrDefault());
                view.Camera.MakeCurrent();
            }));
            foreach (var zoom in zooms)
            {
                steps.Add(new Act($"zoom {zoom}", () =>
                {
                    view.NotNull().FocusOn(MapLayout.TileTop(0, 0, 0f));
                    view.SetZoom(zoom);
                    if (GpuBenchScenario.Bench("UNCIV_BORDERS_TILT") is { } tilt) view.SetView(float.Parse(tilt, CultureInfo.InvariantCulture));
                }));
                steps.Add(new WaitFrames(4));
                steps.Add(new Act($"capture {tag} {zoom}", () =>
                {
                    File.AppendAllText(Path.Combine(outDir, "index.txt"), $"{tag} zoom {zoom}: ui scale {sub.NotNull().GetNode<MapOverlayLayer>("Overlay").UiScale} band {sub.GetNode<MapOverlayLayer>("Overlay").Borders?.BandPixels} px\n");
                    using var image = sub.NotNull().GetTexture().GetImage();
                    image.SavePng(Path.Combine(outDir, $"borders-{tag}-zoom{zoom.ToString(CultureInfo.InvariantCulture)}.png"));
                }));
            }
            steps.Add(new Act("free " + tag, () => sub.NotNull().QueueFree()));
            steps.Add(new WaitFrames(2));
        }
        return steps;
    }

    private static string? OwnerOf(int x, int y)
    {
        foreach (var (cx, cy, civ) in Capitals)
            if (Dist(x - cx, y - cy) <= 2) return civ;
        return null;
    }

    private static int Dist(int dx, int dy) => Math.Max(Math.Abs(dx), Math.Max(Math.Abs(dy), Math.Abs(dx - dy)));

    private static List<TileDto> Map(int radius)
    {
        var tiles = new List<TileDto>();
        for (var x = -radius; x <= radius; x++)
        {
            for (var y = -radius; y <= radius; y++)
            {
                if (Dist(x, y) > radius) continue;
                var h = (uint)(((x * 73856093) ^ (y * 19349663)) & 0x7fffffff) % 100;
                var terrain = Dist(x, y) >= radius - 1 ? "Ocean" : h switch { < 35 => "Grassland", < 55 => "Plains", < 65 => "Desert", < 72 => "Snow", < 80 => "Mountain", < 90 => "Coast", _ => "Grassland" };
                var features = new List<string>();
                if (terrain is "Grassland" or "Plains" && h % 7 == 0) features.Add("Hill");
                else if (terrain == "Grassland" && h % 5 == 0) features.Add("Forest");
                var fog = x >= 1 && y >= 1 && Dist(x, y) <= 4;
                if (y <= -7) continue; // never explored
                tiles.Add(new TileDto
                {
                    X = x, Y = y, Visible = !fog, RiverBottom = false, RiverBottomLeft = false, RiverBottomRight = false, BaseTerrain = terrain, Features = features,
                    Improvement = Capitals.Any(it => it.X == x && it.Y == y) ? "City center" : null,
                    Owner = fog ? null : OwnerOf(x, y), Road = fog ? null : "None", RoadPillaged = fog ? null : false,
                });
            }
        }
        return tiles;
    }
}
