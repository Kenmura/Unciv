using Godot;
using Unciv.Commands;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Map;
using Unciv.GodotClient.Play;
using Unciv.Harness;
using Unciv.Utils;
using Unciv.Logic.Map;

namespace Unciv.GodotHarness;

/// <summary>
/// C014: the minimap on the real game screen. Asserts the picture is generated from the visible map (one cell per explored tile, own borders in the owner's
/// colour), that the camera rectangle is inside the picture, and that a click on a far minimap pixel moves the camera to the tile that pixel stands for
/// (and a click over unexplored space moves nothing). No command is submitted, so the log stays empty. Plays the solo game.
/// </summary>
public sealed class MinimapScenario : Scenario
{
    public override string Name => "minimap";

    public override bool Uses3D => true;

    public override bool Solo => true;

    public override IReadOnlyList<string> Checklist =>
    [
        "Can you tell land from sea and your own borders from the rest on the minimap?",
        "Is the white camera rectangle easy to see, and does it follow after a click on the minimap?",
    ];

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session)
    {
        var game = session.Game;
        var localHost = new LocalHost(session, ScenarioGame.HumanA, () => 1000);
        var screen = new GameScreen();
        host.AddChild(screen);
        screen.Start(localHost, AssetRegistry.ForRuleset(game.Ruleset));
        var viewport = host.GetViewport();
        var minimap = screen.Minimap;
        var map = screen.Map;
        HexCoord target = default;
        Vector2 clickAt = default;
        Vector3 focusBefore = default;

        return
        [
            new WaitFrames(3),
            new AssertQuery("the minimap has one cell per drawn map tile", () => minimap.Raster != null && minimap.Raster.TileCount == map.TileCount && map.TileCount > 0),
            new AssertQuery("the camera rectangle lies inside the picture", () => minimap.ViewportRect() is { } r && r.Size.X > 0f && r.Size.Y > 0f && new Rect2(Vector2.Zero, minimap.Size).Encloses(r)),
            new Capture("start", "Minimap top right: explored terrain, own border, white camera rectangle"),
            new Act("pick the explored tile farthest from the camera", () =>
            {
                focusBefore = map.Focus;
                var raster = minimap.Raster.NotNull();
                var tiles = Enumerable.Range(0, raster.Image.Width).SelectMany(x => Enumerable.Range(0, raster.Image.Height).Select(y => (x, y)))
                    .Select(p => (p, tile: raster.TileAt(p.x, p.y)))
                    .Where(it => it.tile != null)
                    .ToList();
                var far = tiles.OrderByDescending(it => MapLayout.TileTop(it.tile.NotNull().x, it.tile.NotNull().y).DistanceTo(focusBefore)).First();
                target = far.tile.NotNull();
                var (cx, cy) = raster.CentreOf(target);
                clickAt = minimap.GlobalPosition + new Vector2(cx, cy);
            }),
            new Click(() => clickAt),
            new WaitFrames(2),
            new AssertQuery("the camera moved", () => map.Focus.DistanceTo(focusBefore) > 1f),
            new AssertQuery("the camera now centres on the clicked minimap pixel's tile", () =>
            {
                var expected = MapLayout.TileTop(target.x, target.y, map.TileNodeAt(target)?.Elevation ?? 1f);
                return Math.Abs(map.Focus.X - expected.X) < 0.01f && Math.Abs(map.Focus.Z - expected.Z) < 0.01f;
            }),
            new AssertQuery("the camera's own tile at the screen centre is that tile", () => map.PickTile(viewport.GetVisibleRect().Size / 2f) is { } picked && Distance(picked, target) <= 1),
            new AssertQuery("jumping sent no command", () => session.LastSeq == 0),
            new Capture("jumped", "After clicking the far corner of the minimap: the camera rectangle and the map moved there"),
            new Act("remember the camera", () => focusBefore = map.Focus),
            new Click(() => minimap.GlobalPosition + new Vector2(0.5f, 0.5f)),
            new WaitFrames(1),
            new AssertQuery("a click over unexplored space moves nothing", () => map.Focus == focusBefore),
        ];
    }

    private static int Distance(HexCoord a, HexCoord b) => HexMath.GetDistance(a, b);
}
