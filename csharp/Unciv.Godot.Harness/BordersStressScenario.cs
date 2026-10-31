using Godot;
using Unciv.Assets;
using Unciv.Commands;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Map;
using Unciv.Net.Visibility;
using Unciv.Utils;

namespace Unciv.GodotHarness;

/// <summary>
/// T377: borders on a big, crowded map: 60 x 40 tiles split between twelve civs (every civ shares edges with several others), a stripe of the map in the fog. Asserts that each civ's outline is built
/// from closed loops (no open chain: every corner of the territory is joined), that the mesh stays at four vertices per side and that one strip pair per civ is all the overlay adds. Captures the whole map from far out.
/// </summary>
public sealed class BordersStressScenario : Scenario
{
    public override string Name => "borders-stress";

    public override bool Uses3D => true;

    public override IReadOnlyList<string> Checklist => ["Do the twelve territories read as separate closed outlines, with the shared edges showing both civs' colours, and nothing drawn on the water?"];

    private const int Width = 60;
    private const int Height = 40;
    private static readonly (int X, int Y)[] Seeds = [(-21, -11), (-8, -13), (6, -12), (20, -8), (-22, 3), (-9, 0), (4, 2), (18, 5), (-17, 13), (-3, 13), (10, 11), (22, 13)];

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session)
    {
        var assets = AssetRegistry.ForRuleset(session.Game.Ruleset);
        var tiles = new List<TileDto>();
        for (var x = -Width / 2; x < Width / 2; x++)
        {
            for (var y = -Height / 2; y < Height / 2; y++)
            {
                var owner = OwnerOf(x, y);
                tiles.Add(new TileDto
                {
                    X = x, Y = y, Visible = y < 6 || y > 10, BaseTerrain = owner == null ? "Ocean" : "Grassland", Features = [], Improvement = null,
                    RiverBottom = false, RiverBottomLeft = false, RiverBottomRight = false, Owner = owner, Road = "None", RoadPillaged = false,
                });
            }
        }
        var memory = new TileOwnerMemory();
        memory.Remember(tiles.Select(it => it with { Visible = true }).ToList());
        var view = new MapView();
        host.AddChild(view);
        view.Load(assets, tiles);
        var layer = new MapOverlayLayer { Name = "Overlay" };
        host.AddChild(layer);
        var colours = Enumerable.Range(0, Seeds.Length).ToDictionary(i => "Civ" + i, i => new Rgb((byte)(40 + i * 17 % 200), (byte)(220 - i * 29 % 200), (byte)(60 + i * 53 % 190)));
        var edges = MapOverlays.BorderEdges(tiles, memory);
        var chains = BorderChains.Build(edges);
        var mesh = BorderMesh.Build(chains, _ => 0.23f, _ => 1f);
        return
        [
            new Act("build the overlay", () =>
            {
                layer.Rebuild(assets, session.Game.Ruleset, view, tiles, [], owner => colours.TryGetValue(owner, out var c) ? c : null, memory, _ => new Rgb(255, 255, 255));
                view.FocusOn(MapLayout.TileTop(0, 0, 0f));
                view.SetZoom(40f);
            }),
            new WaitFrames(3),
            new AssertQuery("every side of the map belongs to a closed loop: no open chain, so every corner is joined", () => chains.Count >= Seeds.Length && chains.All(it => it.Closed) && chains.Sum(it => it.Sides.Count) == edges.Count),
            new AssertQuery("the mesh holds four vertices and two triangles per side, and the overlay adds one solid and one ghost strip per civ", () =>
                mesh.Vertices.Count == edges.Count * 4 && mesh.Indices.Count == edges.Count * 6 && layer.Borders.NotNull().StripCount == Seeds.Length * 2 && layer.BorderEdgeCount == edges.Count),
            new Capture("whole-map", "Twelve crowded territories from far out: closed outlines, shared edges in both colours, none on the ocean"),
        ];
    }

    private static string? OwnerOf(int x, int y)
    {
        var best = -1;
        var bestDistance = int.MaxValue;
        for (var i = 0; i < Seeds.Length; i++)
        {
            var dx = x - Seeds[i].X;
            var dy = y - Seeds[i].Y;
            var d = Math.Max(Math.Abs(dx), Math.Max(Math.Abs(dy), Math.Abs(dx - dy)));
            if (d < bestDistance) { bestDistance = d; best = i; }
        }
        return bestDistance <= 7 ? "Civ" + best : null;
    }
}
