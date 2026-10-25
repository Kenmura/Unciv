using Godot;
using Unciv.Commands;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Map;
using Unciv.Harness;
using Unciv.Logic.Map;
using Unciv.Net.Visibility;

namespace Unciv.GodotHarness;

/// <summary>
/// C009: the flat hex map over the seeded game. Asserts first (drawn tile counts equal the snapshot's explored and visible counts, unexplored tiles are
/// not drawn, clicks on known pixels pick the expected hexes, the highlight follows, wheel and drag move the camera), then captures default and zoomed views.
/// The scenario game has no fog after the first turn, so the tiles beyond two hexes of the capital are marked not visible in the snapshot the view
/// is given (what the query returns once the founder has left); the view is data-driven, so that exercises the same tint path.
/// </summary>
public sealed class MapV1Scenario : Scenario
{
    private const int VisibleRadius = 2;

    public override string Name => "map-v1";

    public override bool Uses3D => true;

    public override IReadOnlyList<string> Checklist =>
    [
        "Can you tell water, grassland, plains, desert, forest and hills apart at the default camera?",
        "Is the dimmed (explored, not visible) ring clearly different from visible tiles, and is the black outside clearly unexplored?",
        "Is the highlight visible on the selected tile? Does the zoomed view feel like a sensible closest zoom?",
    ];

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session)
    {
        var game = session.Game;
        var civ = game.GetCivilization(ScenarioGame.HumanA);
        var full = SnapshotBuilder.Build(game, civ, session.LastSeq, new TurnStatusDto { Ended = [], Phase = "Playing" });
        var capital = full.Cities.First(it => it.Owner == civ.CivId);
        var tiles = full.Tiles
            .Select(it => it with { Visible = HexMath.GetDistance(it.X, it.Y, capital.X, capital.Y) <= VisibleRadius })
            .ToList();
        int visibleCount = tiles.Count(it => it.Visible);
        int exploredCount = tiles.Count - visibleCount;

        var view = new MapView();
        host.AddChild(view);
        view.Load(AssetRegistry.ForRuleset(game.Ruleset), tiles);

        var centre = new HexCoord(capital.X, capital.Y);
        var fogged = tiles.First(it => !it.Visible);
        var foggedCoord = new HexCoord(fogged.X, fogged.Y);
        var elsewhere = tiles.Where(it => it.Visible && (it.X != capital.X || it.Y != capital.Y)).Last();
        var elsewhereCoord = new HexCoord(elsewhere.X, elsewhere.Y);
        var raised = tiles.Where(it => it.BaseTerrain == "Mountain" || it.Features.Contains("Hill")).ToList();
        var viewport = host.GetViewport();
        bool raisedPicksOk = true;
        float zoomBefore = 0f;
        Vector3 focusBefore = default;
        HexCoord? underCursor = null;
        var dragFrom = new Vector2(320f, 240f);

        return
        [
            new WaitFrames(3),
            new AssertQuery("the snapshot hides unexplored tiles (fewer tiles than the map has)", () => tiles.Count < game.TileMap.TileList.Count),
            new AssertQuery("every explored tile is drawn", () => view.TileCount == tiles.Count),
            new AssertQuery("visible tiles drawn equal the query's visible count", () => view.Count(TileFog.Visible) == visibleCount && visibleCount > 0),
            new AssertQuery("explored-only tiles drawn equal the query's explored count", () => view.Count(TileFog.Explored) == exploredCount && exploredCount > 0),
            new AssertQuery("tile layout round-trips through the engine's HexMath", () => tiles.All(it => MapLayout.TileAt(MapLayout.TileTop(it.X, it.Y)) == new HexCoord(it.X, it.Y))),
            new Capture("default", "Default camera: visible tiles in full colour, explored ring dimmed, unexplored black"),

            new Click(() => view.ScreenPositionOf(centre)),
            new AssertQuery("click on the capital's pixel picks the capital's hex", () => view.Selected == centre),
            new AssertQuery("the highlight sits on the picked tile", () => view.IsHighlightVisible && view.HighlightPosition.DistanceTo(MapLayout.TileTop(centre.X, centre.Y)) < 0.05f),
            new Capture("selected", "Capital tile clicked: highlight on the selected hex"),
            new Click(() => view.ScreenPositionOf(foggedCoord)),
            new AssertQuery("click on a dimmed tile picks that tile", () => view.Selected == foggedCoord),
            new Click(() => view.ScreenPositionOf(elsewhereCoord)),
            new AssertQuery("click on a third tile picks that tile", () => view.Selected == elsewhereCoord),
            new AssertQuery("the map has raised tiles to test picking on", () => raised.Count > 0),
            new Act("click the top face of every raised tile", () =>
            {
                foreach (var tile in raised)
                {
                    var coord = new HexCoord(tile.X, tile.Y);
                    var picked = view.PickTile(view.ScreenPositionOf(coord));
                    if (picked != coord) GD.PrintErr($"raised tile {coord} picked {picked}");
                    raisedPicksOk &= picked == coord;
                }
            }),
            new AssertQuery("every hill and mountain is picked on its own raised top face", () => raisedPicksOk),
            new Click(new Vector2(2f, 2f)),
            new AssertQuery("click on the unexplored backdrop picks nothing and clears the highlight", () => view.Selected == null && !view.IsHighlightVisible),

            new Act("wheel in at the centre", () =>
            {
                zoomBefore = view.Zoom;
                for (int i = 0; i < 4; i++) InputSimulator.SimulateWheel(viewport, dragFrom, up: true);
            }),
            new AssertQuery("wheel up zooms in", () => view.Zoom < zoomBefore),
            new Click(() => view.ScreenPositionOf(centre)),
            new AssertQuery("picking still hits the capital after zooming", () => view.Selected == centre),
            new Capture("zoomed", "After four wheel steps in: closer camera, capital selected"),

            new Act("drag the map", () =>
            {
                focusBefore = view.Focus;
                underCursor = view.PickTile(dragFrom);
                InputSimulator.SimulateDrag(viewport, dragFrom, dragFrom + new Vector2(60f, 30f));
            }),
            new AssertQuery("dragging moved the camera", () => view.Focus.DistanceTo(focusBefore) > 0.1f),
            new AssertQuery("a drag is not a click (selection unchanged)", () => view.Selected == centre),
            new AssertQuery("the tile grabbed by the drag follows the cursor", () => underCursor != null && view.PickTile(dragFrom + new Vector2(60f, 30f)) == underCursor),
            new Act("wheel back out", () =>
            {
                for (int i = 0; i < 8; i++) InputSimulator.SimulateWheel(viewport, dragFrom, up: false);
            }),
            new AssertQuery("wheel down zooms out again, within the limits", () => view.Zoom > zoomBefore && view.Zoom <= MapView.MaxZoom),
        ];
    }
}
