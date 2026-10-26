using Godot;
using Unciv.Assets;
using Unciv.Commands;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Map;
using Unciv.Logic.Map;
using Unciv.Net.Visibility;

namespace Unciv.GodotHarness;

/// <summary>
/// C031: terrain blending, fog edge and chunked meshes. On the gallery map it asserts the baked colour at an edge between two terrains lies toward the
/// neighbour (and that a fogged tile's edge fades), that tiles are grouped as <see cref="TerrainChunks"/> plans, that picking still hits every raised and water
/// tile at three camera angles, then captures the three angles. It then loads a Standard-size map (1000+ tiles), asserts the triangle budget, that zooming in
/// culls chunks and measures the mean frame time against a deliberately generous budget (software GL).
/// </summary>
public sealed class TerrainBlendScenario : Scenario
{
    private const int TrianglesPerTileBudget = 160;

    /// <summary>Generous: software GL (llvmpipe) renders the full 1000+ tile map; this only catches a regression by an order of magnitude.</summary>
    private const double FrameBudgetMs = 500.0;

    private const int MeasuredFrames = 20;

    public override string Name => "terrain-blend";

    public override bool Uses3D => true;

    public override IReadOnlyList<string> Checklist =>
    [
        "Do neighbouring terrains melt into each other (grass into coast, desert into plains) instead of meeting at hard hexagon seams?",
        "Does the outer edge of the explored land fade out instead of ending in a sharp cut against the black?",
        "Do cliffs (hills and mountains beside lower tiles) still read as cliffs?",
    ];

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session)
    {
        var assets = AssetRegistry.ForRuleset(session.Game.Ruleset);
        var gallery = Terrain3DScenario.Gallery();
        var view = new MapView();
        host.AddChild(view);
        view.Load(assets, gallery);

        HexCoord Coord(TileDto t) => new(t.X, t.Y);
        bool IsRaised(TileDto t) => t.BaseTerrain == "Mountain" || t.Features.Contains("Hill");
        bool IsWater(TileDto t) => t.BaseTerrain is "Ocean" or "Coast" or "Lakes";
        var focusTile = gallery.First(it => it.BaseTerrain == "Mountain");
        var picksOk = false;

        ScenarioStep PickAll(string angle) => new Act("pick every raised and water tile at " + angle, () =>
        {
            picksOk = true;
            foreach (var tile in gallery.Where(it => IsRaised(it) || IsWater(it)))
            {
                var picked = view.PickTile(view.ScreenPositionOf(Coord(tile)));
                if (picked != Coord(tile))
                {
                    GD.PrintErr($"{angle}: {tile.BaseTerrain} tile {Coord(tile)} picked {picked}");
                    picksOk = false;
                }
            }
        });

        // A flat land tile and its coast neighbour: the colour at their shared edge must lean toward the water.
        var land = gallery.First(it => it.BaseTerrain == "Grassland" && it.Features.Count == 0 && HexMath.GetDistance(it.X, it.Y, 0, 0) == 2
            && gallery.Any(c => c.BaseTerrain == "Coast" && HexMath.GetDistance(it.X, it.Y, c.X, c.Y) == 1));
        var coast = gallery.First(it => it.BaseTerrain == "Coast" && HexMath.GetDistance(it.X, it.Y, land.X, land.Y) == 1);
        var landTop = MapLayout.TileTop(land.X, land.Y, 0f);
        var coastTop = MapLayout.TileTop(coast.X, coast.Y, 0f);
        var towardCoast = (coastTop - landTop).Normalized();
        Color? landCentre = null, landRim = null, coastCentre = null;


        return
        [
            new WaitFrames(3),
            new AssertQuery("every gallery tile is drawn and tiles are merged into several chunks", () => view.TileCount == gallery.Count && view.ChunkCount >= 2 && view.ChunkCount < view.TileCount),
            new Act("sample the baked colours across a grass/coast edge", () =>
            {
                landCentre = view.SurfaceColourNear(landTop);
                landRim = view.SurfaceColourNear(landTop + towardCoast * 0.8f);
                coastCentre = view.SurfaceColourNear(coastTop);
                GD.Print($"blend samples: land centre {landCentre}, land rim {landRim}, coast centre {coastCentre}");
            }),
            new AssertQuery("the land tile's rim next to the coast has moved toward the coast colour (blue up, red down)", () =>
                landCentre is { } a && landRim is { } r && coastCentre is { } c && r.B > a.B && c.B > a.B && r.R < a.R),
            new AssertQuery("the triangle count stays inside the budget", () => view.TriangleCount > 0 && view.TriangleCount <= gallery.Count * TrianglesPerTileBudget + view.VegetationCount * 3),
            new Act("frame the mountain at the default angle", () => view.FocusOn(MapLayout.TileTop(focusTile.X, focusTile.Y, 0f))),
            new WaitFrames(2),
            new AssertQuery("the whole gallery is inside the footprint, so no chunk is culled", () => view.VisibleChunkCount == view.ChunkCount),
            PickAll("the default angle"),
            new AssertQuery("picking hits every raised and water tile at the default angle", () => picksOk),
            new Capture("blend-default", "Default tilt: grass, plains and desert melt into each other and into the coast; the outer ocean fades toward the black"),

            new Act("low oblique close view", () =>
            {
                view.SetView(32f);
                view.SetZoom(7f);
            }),
            new WaitFrames(2),
            PickAll("the low oblique angle"),
            new AssertQuery("picking hits every raised and water tile at the low oblique angle", () => picksOk),
            new Capture("blend-low", "Low oblique, zoomed in: blended terrain edges and cliff walls of hills and mountains"),

            new Act("high turned wide view", () =>
            {
                view.SetView(70f, 35f);
                view.SetZoom(18f);
            }),
            new WaitFrames(2),
            PickAll("the high turned angle"),
            new AssertQuery("picking hits every raised and water tile at the high turned angle", () => picksOk),
            new Capture("blend-high", "High tilt turned 35 degrees: the fading ocean rim of the explored area"),

            .. StandardMapSteps(view, assets),
        ];
    }

    /// <summary>Loads a Standard-size map and checks the chunk plan, the triangle budget, the frame time and the culling.</summary>
    private static ScenarioStep[] StandardMapSteps(MapView view, AssetRegistry assets)
    {
        var big = StandardMap();
        var bigTiles = big.Count;
        var frameStart = 0UL;
        var meanMs = 0.0;
        var batchMeans = new List<double>();

        ScenarioStep[] MeasureBatch() =>
        [
            new Act("start measuring frames", () => frameStart = Time.GetTicksMsec()),
            new WaitFrames(MeasuredFrames),
            new Act("stop measuring frames", () => batchMeans.Add((Time.GetTicksMsec() - frameStart) / (double)MeasuredFrames)),
        ];

        return
        [
            // Standard-size map: budgets, culling, frame time.
            new Act("load a Standard-size map", () =>
            {
                view.SetView(MapView.TiltDegrees);
                view.Load(assets, big);
                view.SetZoom(MapView.MaxZoom);
            }),
            new WaitFrames(3),
            new AssertQuery("the Standard-size map has 1000+ tiles, all drawn", () => bigTiles >= 1000 && view.TileCount == bigTiles),
            new AssertQuery("the chunk count equals the chunk plan",
                () => view.ChunkCount == TerrainChunks.Group([.. big.Select(it => MapLayout.TileTop(it.X, it.Y, 0f)).Select(it => (it.X, it.Z))]).Count),
            new AssertQuery("the triangle count stays inside the budget", () => view.TriangleCount <= bigTiles * TrianglesPerTileBudget + view.VegetationCount * 3),
            // Best of five batches (C058): a load spike on a shared machine slows one batch, a real regression slows all of them.
            .. MeasureBatch(),
            .. MeasureBatch(),
            .. MeasureBatch(),
            .. MeasureBatch(),
            .. MeasureBatch(),
            new Act("report the frame time", () =>
            {
                meanMs = batchMeans.Min();
                GD.Print($"terrain-blend: {bigTiles} tiles, {view.ChunkCount} chunks ({view.VisibleChunkCount} visible), {view.TriangleCount} triangles, mean frame time per batch {string.Join(", ", batchMeans.Select(it => it.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)))} ms, best {meanMs:F1} ms (budget {FrameBudgetMs} ms)");
            }),
            new AssertQuery("the best batch mean frame time is inside the budget", () => FrameBudget.WithinBudget([meanMs], FrameBudgetMs)),
            new Act("zoom in on the middle of the map", () =>
            {
                view.FocusOn(view.Focus);
                view.SetZoom(MapView.MinZoom);
            }),
            new WaitFrames(2),
            new AssertQuery("zoomed in, chunks outside the view are culled but the one under the camera focus is drawn",
                () => view.VisibleChunkCount < view.ChunkCount && view.VisibleChunkCount >= 1 && view.IsChunkVisibleAt(view.Focus)),
            new Act("zoom out again", () => view.SetZoom(MapView.MaxZoom)),
            new WaitFrames(2),
            new AssertQuery("zoomed out again, the culled chunks are back", () => view.VisibleChunkCount > 0 && view.IsChunkVisibleAt(view.Focus)),
        ];
    }

    /// <summary>40 by 28 tiles (1120): a deterministic mix of every terrain family with a ring of deep ocean around the outside.</summary>
    private static List<TileDto> StandardMap()
    {
        string[] bases = ["Grassland", "Plains", "Desert", "Grassland", "Mountain", "Coast", "Plains", "Grassland"];
        string[][] features = [[], [], [], ["Hill"], [], [], ["Forest"], []];
        var result = new List<TileDto>();
        for (var x = -20; x < 20; x++)
        {
            for (var y = -14; y < 14; y++)
            {
                var edge = Math.Abs(x) >= 19 || Math.Abs(y) >= 13;
                var index = (int)(StableHash.Mix(7u, x, y) % (uint)bases.Length);
                var terrain = edge ? "Ocean" : bases[index];
                var tileFeatures = edge ? [] : features[index];
                result.Add(new TileDto { X = x, Y = y, Visible = (x + y) % 5 != 0, BaseTerrain = terrain, Features = [.. tileFeatures], RiverBottom = false, RiverBottomLeft = false, RiverBottomRight = false });
            }
        }
        return result;
    }
}
