using System.Globalization;
using Godot;
using Unciv.Assets;
using Unciv.Commands;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Map;
using Unciv.GodotClient.Play;
using Unciv.Logic.Map;
using Unciv.Models.Ruleset;
using Unciv.Net.Visibility;
using RulesetClass = Unciv.Models.Ruleset.Ruleset;

namespace Unciv.GodotHarness;

/// <summary>
/// T295: the recognisability sheet. Every unit of the gallery side by side on plain grass, through the real <see cref="UnitNode"/>, at the game's DEFAULT zoom (<see cref="MapView.DefaultZoom"/>), in pages of
/// <see cref="PerPage"/> units, each with its name under it; the owner's top priority is that every unit is easily recognised at the zoom the game is played at (a Settler must never be taken for an Archer).
/// Not part of CI and never compared to goldens (a real GPU draws it): <c>tools/unit-sheet.sh</c> runs it on the host and writes <c>unit-sheet-N.png</c>. Environment: <c>UNCIV_SHEET_OUT</c> (folder, required),
/// <c>UNCIV_SHEET_RES</c> (WxH, default 1920x1080), <c>UNCIV_SHEET_ZOOM</c> (default the game's), <c>UNCIV_SHEET_YAW</c> (degrees the units face, default 0).
/// </summary>
public sealed class UnitSheetScenario : Scenario
{
    private const int Columns = 10;
    private const int PerPage = 40;

    public override string Name => "unit-sheet";

    public override bool Uses3D => true;

    public override Vector2I Viewport => ParseRes(GpuBenchScenario.Bench("UNCIV_SHEET_RES"));

    public override IReadOnlyList<string> Checklist => ["Open unit-sheet-N.png: is every unit recognisable at a glance, and does no civilian look like a soldier?"];

    private static Vector2I ParseRes(string? text) => text?.Split('x') is [var w, var h] && int.TryParse(w, out int x) && int.TryParse(h, out int y) ? new Vector2I(x, y) : new Vector2I(1920, 1080);

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session)
    {
        string outDir = GpuBenchScenario.Bench("UNCIV_SHEET_OUT") ?? throw new InvalidOperationException("UNCIV_SHEET_OUT is not set");
        Directory.CreateDirectory(outDir);
        float zoom = float.TryParse(GpuBenchScenario.Bench("UNCIV_SHEET_ZOOM"), NumberStyles.Float, CultureInfo.InvariantCulture, out float z) ? z : MapView.DefaultZoom;
        float yaw = float.TryParse(GpuBenchScenario.Bench("UNCIV_SHEET_YAW"), NumberStyles.Float, CultureInfo.InvariantCulture, out float yw) ? yw : 0f;
        var ruleset = session.Game.Ruleset;
        var registry = AssetRegistry.ForRuleset(ruleset);
        var gods = RulesetCache.Get("Civ V - Gods & Kings") ?? ruleset;
        RulesetClass Of(string unit) => ruleset.Units.ContainsKey(unit) ? ruleset : gods;
        var colours = new[] { PlaceholderPalette.AttackTint, PlaceholderPalette.ReachableTint };
        var units = UnitVisualsScenario.SheetUnits;
        int pages = (units.Count + PerPage - 1) / PerPage;

        // a straight staggered grid: column c of row r
        HexCoord Coord(int c, int r) => new(c / 2 + c % 2 + r + 0, -(c / 2) + r);
        var tiles = units.Select((u, i) => new TileDto { X = Coord(i % Columns, i / Columns).X, Y = Coord(i % Columns, i / Columns).Y, Visible = true, BaseTerrain = u.Terrain ?? "Grassland", Features = [], Resource = null, RiverBottom = false, RiverBottomLeft = false, RiverBottomRight = false }).ToList();
        var view = new MapView();
        host.AddChild(view);
        view.Load(registry, tiles);

        var steps = new List<ScenarioStep> { new WaitFrames(3) };
        var pageNodes = new List<Node3D>();
        for (int i = 0; i < units.Count; i++)
        {
            var (name, _) = units[i];
            var c = Coord(i % Columns, i / Columns);
            var dto = new UnitDto { Id = i + 1, Name = name, Owner = i % 2 == 0 ? "Rome" : "Egypt", X = c.X, Y = c.Y, HealthBucket = 4, IsEmbarked = false, IsFortified = false, Own = null };
            var node = new UnitNode { Name = "Unit_" + dto.Id, Unit = dto };
            var top = MapLayout.TileTop(c.X, c.Y, view.TileNodeAt(c)?.Elevation ?? 1f);
            node.Place(top);
            var col = colours[i % 2];
            node.Build(registry, Of(name), Of(name).Units[name], (col.R / 255f, col.G / 255f, col.B / 255f));
            node.Face(yaw * MathF.PI / 180f);
            view.AddChild(node);
            pageNodes.Add(node);
            var label = new Label3D { Text = name, PixelSize = 0.006f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true, Modulate = Colors.White, OutlineSize = 12, Position = top + new Vector3(0f, 0.02f, 0.75f) };
            view.AddChild(label);
            pageNodes.Add(label);
        }
        for (int p = 0; p < pages; p++)
        {
            int page = p;
            int first = page * PerPage, last = Math.Min(units.Count, first + PerPage) - 1;
            steps.Add(new Act($"frame page {page + 1}", () =>
            {
                for (int k = 0; k < pageNodes.Count; k++) pageNodes[k].Visible = k / 2 >= first && k / 2 <= last;
                var a = MapLayout.TileTop(Coord(0, first / Columns).X, Coord(0, first / Columns).Y, 0f);
                var b = MapLayout.TileTop(Coord(Columns - 1, last / Columns).X, Coord(Columns - 1, last / Columns).Y, 0f);
                view.FocusOn((a + b) / 2f);
                view.SetZoom(zoom);
            }));
            steps.Add(new WaitFrames(4));
            steps.Add(new Act($"save page {page + 1}", () => ViewportCapture.CaptureViewportPng(host.GetViewport(), Path.Combine(outDir, $"unit-sheet-{page + 1}.png"))));
        }
        steps.Add(new Act("done", () => GD.Print($"[unit-sheet] {units.Count} units on {pages} pages in {outDir}")));
        return steps;
    }
}
