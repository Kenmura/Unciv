using Godot;
using Unciv.Assets;
using Unciv.Commands;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Map;
using Unciv.Logic.Map;
using Unciv.Net.Visibility;

namespace Unciv.GodotHarness;

/// <summary>
/// C002: displaced 3D terrain. Asserts first (elevation order water &lt; land &lt; hills &lt; mountains in the nodes, one MultiMesh of tufts whose instance count equals the
/// ruleset-independent placement rule, triangle budget, picking on the real surface at three camera angles for every raised and water tile), then captures the map
/// at three camera angles (default, low oblique zoomed close to a mountain, high turned view).
/// </summary>
public sealed class Terrain3DScenario : Scenario
{
    /// <summary>Generous: a tile mesh is 96 top + 48 wall triangles; llvmpipe is slow but triangle counts do not depend on it.</summary>
    private const int TrianglesPerTileBudget = 160;

    public override string Name => "terrain-3d";

    public override bool Uses3D => true;

    public override IReadOnlyList<string> Checklist =>
    [
        "Do hills and mountains read as raised ground at all three angles, and can you tell a mountain from a hill?",
        "Is deep ocean visibly darker than coast, and does the coast read as a shore against land?",
        "Do the grass tufts look like vegetation on shaded terrain rather than flat sprites?",
    ];

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session)
    {
        var tiles = Gallery();

        var view = new MapView();
        host.AddChild(view);
        view.Load(AssetRegistry.ForRuleset(session.Game.Ruleset), tiles);

        HexCoord Coord(TileDto t) => new(t.X, t.Y);
        float ElevationOf(TileDto t) => (view.TileNodeAt(Coord(t))?.Elevation ?? 0f);
        bool IsRaised(TileDto t) => t.BaseTerrain == "Mountain" || t.Features.Contains("Hill");
        bool IsWater(TileDto t) => t.BaseTerrain is "Ocean" or "Coast" or "Lakes";
        var mountains = tiles.Where(it => it.BaseTerrain == "Mountain").ToList();
        var hills = tiles.Where(it => it.BaseTerrain != "Mountain" && it.Features.Contains("Hill")).ToList();
        var water = tiles.Where(IsWater).ToList();
        var land = tiles.Where(it => !IsRaised(it) && !IsWater(it)).ToList();
        var expectedTufts = tiles.Where(it => !IsRaised(it) && !IsWater(it) && it.NaturalWonder == null && !it.Features.Any())
            .Sum(it => VegetationGeometry.TuftsFor(it.BaseTerrain));
        var focusTile = mountains[0];

        ScenarioStep PickAll(string angle) => new Act("pick every raised and water tile at " + angle, () =>
        {
            var picksOk = true;
            foreach (var tile in tiles.Where(it => IsRaised(it) || IsWater(it)))
            {
                var picked = view.PickTile(view.ScreenPositionOf(Coord(tile)));
                if (picked != Coord(tile))
                {
                    GD.PrintErr($"{angle}: {tile.BaseTerrain} tile {Coord(tile)} picked {picked}");
                    picksOk = false;
                }
            }
            lastPicks = picksOk;
        });

        return
        [
            new WaitFrames(3),
            new AssertQuery("the map has mountains, hills, water and plain land to compare", () => mountains.Count > 0 && hills.Count > 0 && water.Count > 0 && land.Count > 0),
            new AssertQuery("every tile is drawn", () => view.TileCount == tiles.Count),
            new AssertQuery("elevation: mountains above hills above flat land above water",
                () => mountains.Min(ElevationOf) > hills.Max(ElevationOf) && hills.Min(ElevationOf) > land.Max(ElevationOf) && land.Min(ElevationOf) > water.Max(ElevationOf)),
            new AssertQuery("deep ocean sits lower than coast", () => !tiles.Any(it => it.BaseTerrain == "Ocean") || !tiles.Any(it => it.BaseTerrain == "Coast")
                || tiles.Where(it => it.BaseTerrain == "Ocean").Max(ElevationOf) < tiles.Where(it => it.BaseTerrain == "Coast").Min(ElevationOf)),
            new AssertQuery("tufts are one MultiMesh whose instance count follows the placement rule", () => expectedTufts > 0 && view.VegetationCount == expectedTufts),
            new AssertQuery("triangle count stays inside the budget", () => view.TriangleCount > 0 && view.TriangleCount <= tiles.Count * TrianglesPerTileBudget + view.VegetationCount * 3),
            new Act("frame the mountain at the default angle", () => view.FocusOn(MapLayout.TileTop(focusTile.X, focusTile.Y, 0f))),
            new WaitFrames(2),
            PickAll("the default angle"),
            new AssertQuery("picking hits every raised and water tile at the default angle", () => lastPicks),
            new Capture("angle-default", "Default tilt (55 degrees): relief, water depth and grass tufts"),

            new Act("low oblique close view", () =>
            {
                view.SetView(32f);
                view.SetZoom(7f);
            }),
            new WaitFrames(2),
            PickAll("the low oblique angle"),
            new AssertQuery("picking hits every raised and water tile at the low oblique angle", () => lastPicks),
            new Capture("angle-low", "Low oblique (32 degrees), zoomed in: silhouettes of hills and mountains"),

            new Act("high turned wide view", () =>
            {
                view.SetView(70f, 35f);
                view.SetZoom(18f);
            }),
            new WaitFrames(2),
            PickAll("the high turned angle"),
            new AssertQuery("picking hits every raised and water tile at the high turned angle", () => lastPicks),
            new Capture("angle-high", "High tilt (70 degrees) turned 35 degrees, zoomed out"),
        ];
    }

    private bool lastPicks;

    /// <summary>
    /// A fixed gallery map (the seeded game's small explored area has no mountains or water): a mountain in the middle, hills, plains, grassland, a forest and
    /// a desert around it, then a ring of coast and a ring of deep ocean. The view is data-driven, so this exercises the same path as a real snapshot.
    /// </summary>
    internal static List<TileDto> Gallery()
    {
        (string Base, string[] Features)[] inner =
        [
            ("Grassland", ["Hill"]), ("Grassland", []), ("Plains", []), ("Plains", ["Hill"]), ("Grassland", []), ("Desert", []),
        ];
        (string Base, string[] Features)[] outer =
        [
            ("Grassland", []), ("Mountain", []), ("Plains", []), ("Grassland", ["Forest"]), ("Desert", ["Hill"]), ("Grassland", []),
            ("Plains", []), ("Mountain", []), ("Grassland", ["Hill"]), ("Plains", []), ("Desert", []), ("Grassland", []),
        ];
        var result = new List<TileDto>();
        for (var x = -6; x <= 6; x++)
        {
            for (var y = -6; y <= 6; y++)
            {
                var distance = HexMath.GetDistance(x, y, 0, 0);
                if (distance > 4) continue;
                var index = Math.Abs(x * 7 + y * 3);
                var (terrain, features) = distance switch
                {
                    0 => ("Mountain", []),
                    1 => inner[index % inner.Length],
                    2 => outer[index % outer.Length],
                    3 => ("Coast", []),
                    _ => ("Ocean", []),
                };
                result.Add(new TileDto { X = x, Y = y, Visible = true, BaseTerrain = terrain, Features = [.. features], RiverBottom = false, RiverBottomLeft = false, RiverBottomRight = false });
            }
        }
        return result;
    }
}
