using Godot;
using Unciv.Assets;
using Unciv.Commands;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Map;
using Unciv.GodotClient.Play;
using Unciv.Logic.Map;
using Unciv.Net.Visibility;

namespace Unciv.GodotHarness;

/// <summary>
/// C004: unit visuals. A gallery of 15 units on a staggered grid (civilians, ancient to modern land units, a siege pair, ships, a plane, one foreign unit), built through the real
/// <see cref="UnitNode"/>. Asserts first (figures per unit equal the strength tier rule and the count of figure nodes, one initials billboard per group, civ colour on every figure,
/// health bar iff hurt and its fill, status marks per unit, owner-only marks absent on the foreign unit), then captures the gallery and a close view of the formations.
/// </summary>
public sealed class UnitVisualsScenario : Scenario
{
    private const string Rome = "Rome";
    private const string Egypt = "Egypt";

    public override string Name => "unit-visuals";

    public override bool Uses3D => true;

    public override IReadOnlyList<string> Checklist =>
    [
        "Do you see more figures for stronger units (1 for Warrior, 2 for Longswordsman, 3 for Infantry, 4 for Tank), and one for civilians, ships and the plane?",
        "Can you tell the unit types apart by silhouette (infantry with shield, archer with bow, rider, siege barrel, ship with sail, plane with wings)?",
        "Are the health bar, the status marks (fortified, sleeping, automated, promotion) and the civ colour legible at game camera distance?",
    ];

    private sealed record Slot(string Unit, string Owner, int Health, string? Action = null, bool Fortified = false, bool Automated = false, bool Promotable = false, bool Own = true, string Terrain = "Grassland");

    private static readonly Slot[] Slots =
    [
        new("Settler", Rome, 100), new("Warrior", Rome, 100), new("Horseman", Rome, 100), new("Archer", Rome, 60), new("Catapult", Rome, 100),
        new("Longswordsman", Rome, 100, Action: "Fortify", Fortified: true), new("Musketman", Rome, 100, Action: "Sleep"), new("Infantry", Rome, 100),
        new("Tank", Egypt, 20, Promotable: true), new("Cannon", Egypt, 100, Automated: true),
        new("Trireme", Rome, 100, Terrain: "Coast"), new("Fighter", Egypt, 100), new("Worker", Rome, 100, Automated: true, Promotable: true),
        new("Destroyer", Egypt, 100, Terrain: "Coast"), new("Warrior", Egypt, 50, Fortified: true, Own: false),
    ];

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session)
    {
        var ruleset = session.Game.Ruleset;
        var registry = AssetRegistry.ForRuleset(ruleset);
        var colours = new Dictionary<string, Rgb> { [Rome] = PlaceholderPalette.AttackTint, [Egypt] = PlaceholderPalette.ReachableTint };

        // Staggered grid, 5 columns by 3 rows: two columns are one step (1,-1) apart, an odd column sits one step (1,0) from the even one before it
        HexCoord Coord(int index)
        {
            int c = index % 5, r = index / 5;
            return new HexCoord(c / 2 + c % 2 + r, -(c / 2) + r);
        }

        var tiles = Slots.Select((slot, i) => new TileDto
        {
            X = Coord(i).X, Y = Coord(i).Y, Visible = true, BaseTerrain = slot.Terrain, Features = [], Resource = null,
            RiverBottom = false, RiverBottomLeft = false, RiverBottomRight = false,
        }).ToList();
        var view = new MapView();
        host.AddChild(view);
        view.Load(registry, tiles);

        var nodes = new List<UnitNode>();
        for (var i = 0; i < Slots.Length; i++)
        {
            var slot = Slots[i];
            var dto = new UnitDto
            {
                Id = i + 1, Name = slot.Unit, Owner = slot.Owner, X = Coord(i).X, Y = Coord(i).Y,
                HealthBucket = Math.Clamp((slot.Health + 24) / 25, 1, 4), IsEmbarked = false, IsFortified = slot.Fortified,
                Own = slot.Own ? new OwnUnitDto { Health = slot.Health, CurrentMovement = 2, Xp = 0, Action = slot.Action, Promotions = [], Automated = slot.Automated, CanBePromoted = slot.Promotable } : null,
            };
            var node = new UnitNode { Name = "Unit_" + dto.Id, Unit = dto };
            var elevation = view.TileNodeAt(Coord(i))?.Elevation ?? 1f;
            node.Position = MapLayout.TileTop(dto.X, dto.Y, elevation);
            var c = colours[slot.Owner];
            node.Build(registry, ruleset, ruleset.Units[slot.Unit], (c.R / 255f, c.G / 255f, c.B / 255f));
            view.AddChild(node);
            nodes.Add(node);
        }

        UnitNode Node(string unit, string? owner = null) => nodes.First(it => it.Unit.Name == unit && (owner == null || it.Unit.Owner == owner));
        int Figures(UnitNode n) => n.GetChildren().Count(it => it.Name.ToString().StartsWith("Figure", StringComparison.Ordinal));
        int Glyphs(UnitNode n) => n.GetChildren().OfType<Node3D>().Sum(f => f.GetChildren().OfType<Node3D>().Count(m => m.GetNodeOrNull("Glyphs") != null));
        bool Marks(UnitNode n, params UnitStatusIcon[] icons) => n.StatusIcons.SequenceEqual(icons)
            && icons.All(i => n.GetNodeOrNull<Sprite3D>("Status_" + i) != null)
            && n.GetChildren().OfType<Sprite3D>().Count(s => s.Name.ToString().StartsWith("Status_", StringComparison.Ordinal)) == icons.Length;
        bool CivColoured(UnitNode n) => n.GetChildren().OfType<Node3D>().Where(f => f.Name.ToString().StartsWith("Figure", StringComparison.Ordinal))
            .All(f => f.GetChild<Node3D>(0).GetNodeOrNull<MeshInstance3D>("Banner") is { MaterialOverride: StandardMaterial3D m }
                && ProceduralAssets.ToColor(colours[n.Unit.Owner]).IsEqualApprox(m.AlbedoColor));

        return
        [
            new WaitFrames(3),
            new AssertQuery("every unit stands on its own tile of the gallery", () => nodes.Count == Slots.Length && nodes.Select(it => (it.Unit.X, it.Unit.Y)).Distinct().Count() == Slots.Length),
            new AssertQuery("figures: civilians, ships and the plane are one; Warrior 1, Longswordsman 2, Infantry 3, Tank 4", () =>
                new[] { "Settler", "Worker", "Trireme", "Destroyer", "Fighter", "Warrior", "Horseman", "Archer" }.All(n => Node(n).FigureCount == 1)
                && Node("Longswordsman").FigureCount == 2 && Node("Musketman").FigureCount == 2 && Node("Catapult").FigureCount == 2
                && Node("Infantry").FigureCount == 3 && Node("Cannon").FigureCount == 3 && Node("Tank").FigureCount == 4),
            new AssertQuery("the figure nodes equal the figure count, from the tier rule, for every unit", () =>
                nodes.All(n => Figures(n) == n.FigureCount && n.FigureCount == UnitVisuals.FigureCount(ruleset.Units[n.Unit.Name], ruleset))),
            new AssertQuery("a group has one initials billboard whatever its size", () => nodes.All(n => Glyphs(n) == 1)),
            new AssertQuery("every figure of a unit carries its civ's colour", () => nodes.All(CivColoured)),
            new AssertQuery("figures of a group stand at distinct spots, smaller than a lone figure", () =>
                Node("Tank").GetChildren().OfType<Node3D>().Where(f => f.Name.ToString().StartsWith("Figure", StringComparison.Ordinal)).Select(f => (f.Position.X, f.Position.Z)).Distinct().Count() == 4
                && Node("Tank").GetNode<Node3D>("Figure0").Scale.X < Node("Warrior").GetNode<Node3D>("Figure0").Scale.X),
            new AssertQuery("only hurt units have a health bar (Archer 60, Tank 20, foreign Warrior bucket 2)", () =>
                nodes.Where(n => n.HealthBar != null).Select(n => n.Unit.Name).Order().SequenceEqual(["Archer", "Tank", "Warrior"])
                && nodes.All(n => (n.GetNodeOrNull("HealthBar") != null) == (n.HealthBar != null))),
            new AssertQuery("the bar colours go green, amber, red as the unit gets worse", () =>
                Node("Archer").HealthBar is { Fraction: 0.6f } archer && archer.Fill == PlaceholderPalette.HealthWarn
                && Node("Tank").HealthBar is { Fraction: 0.2f } tank && tank.Fill == PlaceholderPalette.HealthBad
                && Node("Warrior", Egypt).HealthBar is { Fraction: 0.5f }),
            new AssertQuery("status marks: fortified, sleeping, automated, promotion available", () =>
                Marks(Node("Longswordsman"), UnitStatusIcon.Fortified) && Marks(Node("Musketman"), UnitStatusIcon.Sleeping)
                && Marks(Node("Cannon"), UnitStatusIcon.Automated) && Marks(Node("Tank"), UnitStatusIcon.PromotionAvailable)
                && Marks(Node("Worker"), UnitStatusIcon.Automated, UnitStatusIcon.PromotionAvailable) && Marks(Node("Warrior", Rome))),
            new AssertQuery("a foreign unit shows only what everybody can see (fortified)", () => Marks(Node("Warrior", Egypt), UnitStatusIcon.Fortified)),
            new Act("frame the gallery", () =>
            {
                view.FocusOn(MapLayout.TileTop(Coord(7).X, Coord(7).Y, 0f));
                view.SetZoom(10f);
            }),
            new WaitFrames(2),
            new Capture("gallery", "Unit gallery: civilian, land units by era (1 to 4 figures), ships and plane; red and blue civs; health bars and status marks"),
            new Act("close view of the formations", () =>
            {
                view.FocusOn(MapLayout.TileTop(Coord(8).X, Coord(8).Y, 0f) + new Vector3(-1.5f, 0f, 0f));
                view.SetView(40f);
                view.SetZoom(5f);
            }),
            new WaitFrames(2),
            new Capture("formations", "Close view: Infantry (3 figures), Tank (4, hurt, promotion mark) and Cannon (3, automated mark)"),
        ];
    }
}
