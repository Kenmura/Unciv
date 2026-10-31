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
/// T295/T309: the recognisability sheet. Every unit of the gallery side by side on plain grass, through the real <see cref="UnitNode"/>, in pages of at most <see cref="MaxColumns"/> x <see cref="MaxRows"/> units
/// (the units split evenly over the pages, the camera fits each page, the grid has exactly the tiles of one page), each with its name under it. The owner's top priority is that every unit is easily recognised
/// (a Settler must never be taken for an Archer). One figure per unit by default (the far-zoom lone figure: a pair or trio of figures is how the game draws a strong unit, not what tells units apart), the
/// formations on request. Not part of CI and never compared to goldens (a real GPU draws it): <c>tools/unit-sheet.sh</c> runs it on the host and writes <c>unit-sheet-N.png</c>. Environment: <c>UNCIV_SHEET_OUT</c>
/// (folder, required), <c>UNCIV_SHEET_RES</c> (WxH, default 1920x1080), <c>UNCIV_SHEET_ZOOM</c> (camera size in world units, default fitted to the page; <see cref="MapView.DefaultZoom"/> is the game's),
/// <c>UNCIV_SHEET_ONLY</c> (comma separated unit names: only those), <c>UNCIV_SHEET_TERRAIN</c> (T373, comma separated base terrains cycled over the tiles instead of plain grass, so units can be judged on real ground; a ship needs "Coast")
/// <c>UNCIV_SHEET_FEATURES</c> (T383, comma separated terrain features such as Forest or Jungle cycled over the tiles in step with the terrains; "-" is none), and <c>UNCIV_SHEET_NATIONS</c> (T373, comma separated nations whose real outer colours are cycled over the units instead of the two placeholder tints), <c>UNCIV_SHEET_YAW</c> (degrees the units face, default 0), <c>UNCIV_SHEET_FORMATIONS</c> (1: draw the formations of the game).
/// </summary>
public sealed class UnitSheetScenario : Scenario
{
    private const int MaxColumns = 12;
    private const int MaxRows = 6;

    public override string Name => "unit-sheet";

    public override bool Uses3D => true;

    public override Vector2I Viewport => ParseRes(GpuBenchScenario.Bench("UNCIV_SHEET_RES"));

    public override IReadOnlyList<string> Checklist => ["Open unit-sheet-N.png: is every unit recognisable at a glance, and does no civilian look like a soldier?"];

    private static Vector2I ParseRes(string? text) => text?.Split('x') is [var w, var h] && int.TryParse(w, out int x) && int.TryParse(h, out int y) ? new Vector2I(x, y) : new Vector2I(1920, 1080);

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session)
    {
        string outDir = GpuBenchScenario.Bench("UNCIV_SHEET_OUT") ?? throw new InvalidOperationException("UNCIV_SHEET_OUT is not set");
        Directory.CreateDirectory(outDir);
        float yaw = float.TryParse(GpuBenchScenario.Bench("UNCIV_SHEET_YAW"), NumberStyles.Float, CultureInfo.InvariantCulture, out float yw) ? yw : 0f;
        var ruleset = session.Game.Ruleset;
        var registry = AssetRegistry.ForRuleset(ruleset);
        var gods = RulesetCache.Get("Civ V - Gods & Kings") ?? ruleset;
        RulesetClass Of(string unit) => ruleset.Units.ContainsKey(unit) ? ruleset : gods;
        var colours = new[] { PlaceholderPalette.AttackTint, PlaceholderPalette.ReachableTint };
        var nations = (GpuBenchScenario.Bench("UNCIV_SHEET_NATIONS") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(n => ruleset.Nations.ContainsKey(n)).ToArray();
        var terrains = (GpuBenchScenario.Bench("UNCIV_SHEET_TERRAIN") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        // T383: terrain features (Forest, Jungle, ...) cycled over the tiles like the terrains (same index; "-" is no feature), so dark units can be judged on wooded ground
        var features = (GpuBenchScenario.Bench("UNCIV_SHEET_FEATURES") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var units = UnitVisualsScenario.SheetUnits;
        if (GpuBenchScenario.Bench("UNCIV_SHEET_ONLY") is { Length: > 0 } only) // T369: a focused sheet of the named units (comma separated), for judging one group at the real zoom
            units = [.. only.Split(',').Select(n => units.First(u => u.Unit == n.Trim()))];
        bool formations = GpuBenchScenario.Bench("UNCIV_SHEET_FORMATIONS") == "1";
        int pages = (units.Count + MaxColumns * MaxRows - 1) / (MaxColumns * MaxRows);
        int perPage = (units.Count + pages - 1) / pages;
        int rowCount = (perPage + MaxColumns - 1) / MaxColumns;
        int columns = (perPage + rowCount - 1) / rowCount; // as many columns as the rows need: the last row is as full as it can be

        // a straight staggered grid: column c of row r; every page uses the same tiles (slot = index within its page), all of them plain grass (a ship on grass is fine for telling shapes apart)
        // reading order is left to right, top to bottom on the screen (the engine's x grows to the left and y towards the camera)
        HexCoord Coord(int slot)
        {
            int c = columns - 1 - slot % columns, r = rowCount - 1 - slot / columns;
            return new(c / 2 + c % 2 + r, -(c / 2) + r);
        }
        var tiles = Enumerable.Range(0, perPage).Select(s => new TileDto { X = Coord(s).X, Y = Coord(s).Y, Visible = true, BaseTerrain = terrains.Length > 0 ? terrains[s % terrains.Length] : "Grassland", Features = features.Length > 0 && features[s % features.Length] is { Length: > 0 } f && f != "-" ? [f] : [], Resource = null, RiverBottom = false, RiverBottomLeft = false, RiverBottomRight = false }).ToList();
        var view = new MapView();
        host.AddChild(view);
        view.Load(registry, tiles);

        var steps = new List<ScenarioStep> { new WaitFrames(3) };
        var pageNodes = new List<(Node3D Node, int Page)>();
        for (int i = 0; i < units.Count; i++)
        {
            var (name, _) = units[i];
            int page = i / perPage;
            var c = Coord(i % perPage);
            var dto = new UnitDto { Id = i + 1, Name = name, Owner = i % 2 == 0 ? "Rome" : "Egypt", X = c.X, Y = c.Y, HealthBucket = 4, IsEmbarked = false, IsFortified = false, Own = null };
            var node = new UnitNode { Name = "Unit_" + dto.Id, Unit = dto };
            var top = MapLayout.TileTop(c.X, c.Y, view.TileNodeAt(c)?.Elevation ?? 1f);
            node.Place(top);
            var col = colours[i % 2];
            var civ = nations.Length > 0 ? ruleset.Nations[nations[i % nations.Length]].GetOuterColor() : null;
            node.Build(registry, Of(name), Of(name).Units[name], civ != null ? (civ.R, civ.G, civ.B) : (col.R / 255f, col.G / 255f, col.B / 255f));
            node.Face(yaw * MathF.PI / 180f);
            if (!formations) node.ApplyLod(true);
            view.AddChild(node);
            pageNodes.Add((node, page));
            var label = new Label3D
            {
                Text = name, FontSize = 36, PixelSize = 0.0042f, Width = 380f, AutowrapMode = TextServer.AutowrapMode.WordSmart, VerticalAlignment = VerticalAlignment.Top, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                NoDepthTest = true, Modulate = Colors.White, OutlineSize = 10, Position = top + new Vector3(0f, 0.02f, 0.62f),
            };
            view.AddChild(label);
            pageNodes.Add((label, page));
        }
        var tops = Enumerable.Range(0, perPage).Select(s => MapLayout.TileTop(Coord(s).X, Coord(s).Y, 0f)).ToList();
        var (min, max) = (new Vector3(tops.Min(t => t.X), 0f, tops.Min(t => t.Z)), new Vector3(tops.Max(t => t.X), 0f, tops.Max(t => t.Z)));
        var size = Viewport;
        // the camera's vertical size shows the ground foreshortened by the tilt; the margins hold the figures above the first row and the labels under the last
        float fit = MathF.Max((max.Z - min.Z) * MathF.Sin(MapView.TiltDegrees * MathF.PI / 180f) + 3.0f, (max.X - min.X + 1.8f) * size.Y / size.X);
        if (!float.TryParse(GpuBenchScenario.Bench("UNCIV_SHEET_ZOOM"), NumberStyles.Float, CultureInfo.InvariantCulture, out float zoom)) zoom = fit;
        for (int p = 0; p < pages; p++)
        {
            int page = p;
            steps.Add(new Act($"frame page {page + 1}", () =>
            {
                foreach (var (node, nodePage) in pageNodes) node.Visible = nodePage == page;
                view.FocusOn(new Vector3((min.X + max.X) / 2f, 0f, (min.Z + max.Z) / 2f + 0.35f));
                view.SetZoom(zoom);
            }));
            steps.Add(new WaitFrames(4));
            steps.Add(new Act($"save page {page + 1}", () => ViewportCapture.CaptureViewportPng(host.GetViewport(), Path.Combine(outDir, $"unit-sheet-{page + 1}.png"))));
        }
        steps.Add(new Act("done", () => GD.Print($"[unit-sheet] {units.Count} units on {pages} pages in {outDir}")));
        return steps;
    }
}
