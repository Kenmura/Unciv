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

    private static readonly string[] EraRoster =
    [
        "Settler", "Maori Warrior", "Jaguar", "Brute", "Marauder", "Pictish Warrior", "Mohawk Warrior", "Spearman", "Hoplite", "Persian Immortal", "Legion",
        "Bowman", "Slinger", "Atlatlist", "Skirmisher", "Composite Bowman", "Horse Archer", "Chariot Archer", "War Chariot", "Companion Cavalry", "Cataphract", "War Elephant", "African Forest Elephant",
    ];

    // C093: the medieval and renaissance units that are not already in the gallery's first rows (Knight, Crossbowman, Longswordsman and Musketman are)
    private static readonly string[] LaterRoster =
    [
        "Pikeman", "Landsknecht", "Samurai", "Berserker", "Longbowman", "Chu-Ko-Nu", "Musketeer", "Janissary", "Minuteman", "Tercio",
        "Keshik", "Conquistador", "Mandekalu Cavalry", "Camel Archer", "Naresuan's Elephant", "Hakkapeliitta", "Lancer", "Sipahi", // the three mounts of the close view are adjacent (slots 56 to 58)
    ];

    // C094: the industrial and modern humanoid units (gun crews hold the crossbow placeholder), four mounted units and the great people that are not already in the gallery; ships, aircraft and armour stay generated blocks
    private static readonly string[] ModernRoster =
    [
        "Rifleman", "Norwegian Ski Infantry", "Carolean", "Mehal Sefari", "Great War Infantry", "Foreign Legion", "Marine", "Paratrooper", "Mechanized Infantry", "Machine Gun", "Gatling Gun", "Anti-Tank Gun", "Anti-Aircraft Gun", "Mobile SAM",
        "Cavalry", "Cossack", "Hussar", "Khan",
        "Great Artist", "Great Merchant", "Great Engineer", "Great Prophet", "Great General", "Missionary", "Inquisitor",
    ];

    private static readonly Slot[] Slots =
    [
        new("Settler", Rome, 100), new("Warrior", Rome, 100), new("Horseman", Rome, 100), new("Archer", Rome, 60), new("Catapult", Rome, 100),
        new("Longswordsman", Rome, 100, Action: "Fortify", Fortified: true), new("Musketman", Rome, 100, Action: "Sleep"), new("Infantry", Rome, 100),
        new("Tank", Egypt, 20, Promotable: true), new("Cannon", Egypt, 100, Automated: true),
        new("Trireme", Rome, 100, Terrain: "Coast"), new("Fighter", Egypt, 100), new("Worker", Rome, 100, Automated: true, Promotable: true),
        new("Destroyer", Egypt, 100, Terrain: "Coast"), new("Warrior", Egypt, 50, Fortified: true, Own: false),
        new("Swordsman", Rome, 100), new("Knight", Egypt, 100), new("Crossbowman", Rome, 100), new("Great Scientist", Rome, 100), new("Scout", Egypt, 100),
        // C084: the ancient and classical roster (rows 5 to 9 of the grid; the last has three), all model files; owners alternate so both civ colours show
        .. EraRoster.Select((unit, i) => new Slot(unit, i % 2 == 0 ? Rome : Egypt, 100)),
        // C093: the medieval and renaissance roster (rows 8 to 12)
        .. LaterRoster.Select((unit, i) => new Slot(unit, i % 2 == 0 ? Egypt : Rome, 100)),
        // C094: the industrial and modern roster (rows 11 to 16)
        .. ModernRoster.Select((unit, i) => new Slot(unit, i % 2 == 0 ? Rome : Egypt, 100)),
    ];

    // C080/C083: the units of the bundled units pack are model files (TeamColor material, animation player, humanoid bodies and riders on a generated horse); every other unit is a generated block with a Banner
    private static readonly string[] ModelUnits = [.. EraRoster, .. LaterRoster, .. ModernRoster, "Infantry", "Musketman", "Warrior", "Longswordsman", "Swordsman", "Archer", "Crossbowman", "Scout", "Worker", "Great Scientist", "Horseman", "Knight"];
    private static readonly string[] MountedUnits = ["Horseman", "Knight", "Horse Archer", "Chariot Archer", "War Chariot", "Companion Cavalry", "Cataphract", "War Elephant", "African Forest Elephant", "Camel Archer", "Keshik", "Conquistador", "Naresuan's Elephant", "Mandekalu Cavalry", "Lancer", "Sipahi", "Hakkapeliitta", "Cavalry", "Cossack", "Hussar", "Khan"];
    private static bool IsModel(UnitNode n) => ModelUnits.Contains(n.Unit.Name);
    private static IEnumerable<Node3D> FigureModels(UnitNode n) => n.GetChildren().OfType<Node3D>().Where(f => f.Name.ToString().StartsWith("Figure", StringComparison.Ordinal)).Select(f => f.GetChild<Node3D>(0));
    private static bool HasClips(UnitNode n) => n.Players.Count == n.FigureCount && n.Players.All(p => UnitModels.Clips(p).SequenceEqual(UnitModelContract.ClipNames));
    private static bool HoldsIdle(AnimationPlayer p) => p.AssignedAnimation == UnitModelContract.IdleClip && !p.IsPlaying() && p.CurrentAnimationPosition > 0f;
    private static bool Textured(UnitNode n) => FigureModels(n).SelectMany(m => m.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>())
        .Any(mi => Enumerable.Range(0, mi.Mesh?.GetSurfaceCount() ?? 0).Any(i => mi.GetActiveMaterial(i) is BaseMaterial3D { ResourceName: UnitModelContract.TeamColorMaterial, AlbedoTexture: not null }));

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session)
    {
        var ruleset = session.Game.Ruleset;
        var registry = AssetRegistry.ForRuleset(ruleset);
        // C084: the session is Vanilla; the roster's Gods & Kings units (Pictish Warrior, Cataphract...) are built from that ruleset
        var gods = RulesetCache.Get("Civ V - Gods & Kings") ?? ruleset;
        RulesetClass Of(string unit) => ruleset.Units.ContainsKey(unit) ? ruleset : gods;
        var colours = new Dictionary<string, Rgb> { [Rome] = PlaceholderPalette.AttackTint, [Egypt] = PlaceholderPalette.ReachableTint };

        // Staggered grid, 5 columns by 3 rows: two columns are one step (1,-1) apart, an odd column sits one step (1,0) from the even one before it
        HexCoord Coord(int index)
        {
            int c = index % 5, r = index / 5;
            return new HexCoord(c / 2 + c % 2 + r, -(c / 2) + r);
        }

        var tiles = Slots.Select((slot, i) => new TileDto
        {
            X = Coord(i).X,
            Y = Coord(i).Y,
            Visible = true,
            BaseTerrain = slot.Terrain,
            Features = [],
            Resource = null,
            RiverBottom = false,
            RiverBottomLeft = false,
            RiverBottomRight = false,
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
                Id = i + 1,
                Name = slot.Unit,
                Owner = slot.Owner,
                X = Coord(i).X,
                Y = Coord(i).Y,
                HealthBucket = Math.Clamp((slot.Health + 24) / 25, 1, 4),
                IsEmbarked = false,
                IsFortified = slot.Fortified,
                Own = slot.Own ? new OwnUnitDto { Health = slot.Health, CurrentMovement = 2, Xp = 0, Action = slot.Action, Promotions = [], Automated = slot.Automated, CanBePromoted = slot.Promotable } : null,
            };
            var node = new UnitNode { Name = "Unit_" + dto.Id, Unit = dto };
            var elevation = view.TileNodeAt(Coord(i))?.Elevation ?? 1f;
            node.Position = MapLayout.TileTop(dto.X, dto.Y, elevation);
            var c = colours[slot.Owner];
            node.Build(registry, Of(slot.Unit), Of(slot.Unit).Units[slot.Unit], (c.R / 255f, c.G / 255f, c.B / 255f));
            view.AddChild(node);
            nodes.Add(node);
        }

        UnitNode Node(string unit, string? owner = null) => nodes.First(it => it.Unit.Name == unit && (owner == null || it.Unit.Owner == owner));
        int Figures(UnitNode n) => n.GetChildren().Count(it => it.Name.ToString().StartsWith("Figure", StringComparison.Ordinal));
        int Glyphs(UnitNode n) => n.GetChildren().OfType<Node3D>().Sum(f => f.GetChildren().OfType<Node3D>().Count(m => m.GetNodeOrNull("Glyphs") != null));
        bool Marks(UnitNode n, params UnitStatusIcon[] icons) => n.StatusIcons.SequenceEqual(icons)
            && icons.All(i => n.GetNodeOrNull<Sprite3D>("Status_" + i) != null)
            && n.GetChildren().OfType<Sprite3D>().Count(s => s.Name.ToString().StartsWith("Status_", StringComparison.Ordinal)) == icons.Length;
        bool CivColoured(UnitNode n) => FigureModels(n).All(m => MapMaterials.TeamColourOf(m) is { } worn
            && MapMaterials.TeamTint(ProceduralAssets.ToColor(colours[n.Unit.Owner]), textured: IsModel(n)).IsEqualApprox(worn));

        return
        [
            new WaitFrames(3),
            new AssertQuery("every unit stands on its own tile of the gallery", () => nodes.Count == Slots.Length && nodes.Select(it => (it.Unit.X, it.Unit.Y)).Distinct().Count() == Slots.Length),
            new AssertQuery("figures: civilians, ships and the plane are one; Warrior 1, Longswordsman 2, Infantry 3, Tank 4", () =>
                new[] { "Settler", "Worker", "Trireme", "Destroyer", "Fighter", "Warrior", "Horseman", "Archer" }.All(n => Node(n).FigureCount == 1)
                && Node("Longswordsman").FigureCount == 2 && Node("Musketman").FigureCount == 2 && Node("Catapult").FigureCount == 2
                && Node("Infantry").FigureCount == 3 && Node("Cannon").FigureCount == 3 && Node("Tank").FigureCount == 4),
            new AssertQuery("the figure nodes equal the figure count, from the tier rule, for every unit", () =>
                nodes.All(n => Figures(n) == n.FigureCount && n.FigureCount == UnitVisuals.FigureCount(Of(n.Unit.Name).Units[n.Unit.Name], Of(n.Unit.Name)))),
            new AssertQuery("a generated group has one initials billboard whatever its size; a model file carries none (its art identifies it)", () => nodes.All(n => Glyphs(n) == (IsModel(n) ? 0 : 1))),
            new AssertQuery("every figure of a unit carries its civ's colour", () => nodes.All(CivColoured)),
            new AssertQuery("the units pack's units are CC0 model files: a textured TeamColor material and an animation player with the six contract clips; no other unit has a player", () =>
                nodes.Where(IsModel).All(n => Textured(n) && HasClips(n)) && nodes.Where(n => !IsModel(n)).All(n => n.Players.Count == 0 && !Textured(n))),
            new AssertQuery("model figures hold their idle pose (paused, not at the first frame), at a different phase for (nearly) every unit", () =>
                nodes.Where(IsModel).SelectMany(n => n.Players).All(HoldsIdle)
                && nodes.Where(IsModel).Select(n => n.Players[0].CurrentAnimationPosition).Distinct().Count() >= nodes.Count(IsModel) * 9 / 10), // 1000 phases: a few of the 33 model units may collide
            .. PipelineAsserts(nodes, Node),
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
                view.FocusOn(MapLayout.TileTop(Coord(10).X, Coord(10).Y, 0f));
                view.SetZoom(13f);
            }),
            new WaitFrames(2),
            new Capture("gallery", "Unit gallery: civilian, land units by era (1 to 4 figures), ships and plane; red and blue civs; health bars and status marks"),
            .. EraSteps(view, Coord, Node),
            .. LaterSteps(view, Coord, Node),
            .. ModernSteps(view, Coord, Node),
            .. RigidSteps(view, nodes, Node, Coord),
            CloseView("close view of the formations", view, Coord(8), new Vector3(-1.5f, 0f, 0f), 5f),
            new WaitFrames(2),
            new Capture("formations", "Close view: Infantry (3 figures), Tank (4, hurt, promotion mark) and Cannon (3, automated mark)"),
            CloseView("close view of the Warrior model", view, Coord(1), Vector3.Zero, 3f),
            new WaitFrames(2),
            new Capture("warrior", "Close view of the Warrior, now a CC0 animated model file in its idle pose with its TeamColor material tinted in the red civ's colour (C080)"),
            .. MountedSteps(view, Node, Coord),
        ];
    }

    private static float SaddleHeight(UnitNode n) =>
        FigureModels(n).First().FindChildren("*", nameof(Skeleton3D), true, false).OfType<Skeleton3D>().Where(sk => sk.FindBone("saddle") >= 0).Select(sk => sk.GetBoneGlobalRest(sk.FindBone("saddle")).Origin.Y).FirstOrDefault();

    private static Act CloseView(string label, MapView view, HexCoord at, Vector3 offset, float zoom) => new(label, () =>
    {
        view.FocusOn(MapLayout.TileTop(at.X, at.Y, 0f) + offset);
        view.SetView(40f);
        view.SetZoom(zoom);
    });

    private static IEnumerable<ScenarioStep> PipelineAsserts(List<UnitNode> nodes, Func<string, string?, UnitNode> node) =>
    [
            new AssertQuery("mounted units are a horse and a rider on two skeletons under one animation player; humanoids have one skeleton", () =>
                nodes.Where(n => MountedUnits.Contains(n.Unit.Name)).All(n => FigureModels(n).All(m => m.FindChildren("*", nameof(Skeleton3D), true, false).Count == 2))
                && nodes.Where(n => IsModel(n) && !MountedUnits.Contains(n.Unit.Name)).All(n => FigureModels(n).All(m => m.FindChildren("*", nameof(Skeleton3D), true, false).Count == 1))),
            new AssertQuery("rigid motion applies only to clipless ships, siege, armour and aircraft", () =>
                node("Catapult", null).Rigid == RigidKind.Siege && node("Tank", null).Rigid == RigidKind.Armor && node("Destroyer", null).Rigid == RigidKind.Ship && node("Fighter", null).Rigid == RigidKind.Air
                && nodes.Where(n => n.Rigid != RigidKind.None).Select(n => n.Unit.Name).Order().SequenceEqual(new[] { "Catapult", "Cannon", "Destroyer", "Fighter", "Tank", "Trireme" }.Order())
                && nodes.Where(IsModel).All(n => n.Rigid == RigidKind.None)),
            new AssertQuery("C094: the 25 industrial and modern units (and the Infantry) are all model files with the six clips, none a generated block; the ships, aircraft and armour stay generated", () =>
                ModernRoster.Append("Infantry").All(u => nodes.Where(n => n.Unit.Name == u).All(n => IsModel(n) && HasClips(n) && n.Rigid == RigidKind.None))
                && new[] { "Trireme", "Destroyer", "Fighter", "Tank" }.All(u => !IsModel(node(u, null)))),
            new AssertQuery("C093: the 18 medieval and renaissance units (and the Musketman) are all model files with the six clips, none a generated block; Naresuan's Elephant stands on the taller mount", () =>
                LaterRoster.Append("Musketman").All(u => nodes.Where(n => n.Unit.Name == u).All(n => IsModel(n) && HasClips(n) && n.Rigid == RigidKind.None))
                && SaddleHeight(node("Naresuan's Elephant", null)) > SaddleHeight(node("Knight", null)) * 1.08f),
            new AssertQuery("C084: the 23 ancient and classical units are all model files with the six clips, none a generated block; the elephants stand on a taller, wider mount than the horses", () =>
                EraRoster.All(u => nodes.Where(n => n.Unit.Name == u).All(n => IsModel(n) && HasClips(n) && n.Rigid == RigidKind.None))
                && SaddleHeight(node("War Elephant", null)) > SaddleHeight(node("Horseman", null)) * 1.08f && SaddleHeight(node("African Forest Elephant", null)) > SaddleHeight(node("Cataphract", null)) * 1.08f),
    ];

    private static IEnumerable<ScenarioStep> LaterSteps(MapView view, Func<int, HexCoord> coord, Func<string, string?, UnitNode> node) =>
    [
            new Act("frame the medieval and renaissance roster", () =>
            {
                view.FocusOn(MapLayout.TileTop(coord(50).X, coord(50).Y, 0f));
                view.SetZoom(10f);
            }),
            new WaitFrames(2),
            new Capture("medieval", "Medieval and renaissance roster (the bottom rows of the gallery): pikemen, landsknechts, samurai and berserkers, longbowmen and chu-ko-nu, musketeers, camel archers, keshiks, conquistadors, lancers and elephants; red and blue civs"),
            new Act("close view of the medieval mounts", () =>
            {
                foreach (var name in new[] { "Camel Archer", "Naresuan's Elephant", "Hakkapeliitta" }) node(name, null).Face(MathF.PI / 2f);
                view.FocusOn(MapLayout.TileTop(coord(57).X, coord(57).Y, 0f));
                view.SetView(40f);
                view.SetZoom(3.5f);
            }),
            new WaitFrames(2),
            new Capture("medieval-mounts", "Close view, side on: the Camel Archer, Naresuan's Elephant and Hakkapeliitta, riders on the generated mount; the camel's is sand-coloured, the elephant's larger and grey (placeholder shapes)"),
    ];

    private static IEnumerable<ScenarioStep> ModernSteps(MapView view, Func<int, HexCoord> coord, Func<string, string?, UnitNode> node) =>
    [
            new Act("frame the industrial and modern roster", () =>
            {
                view.FocusOn(MapLayout.TileTop(coord(76).X, coord(76).Y, 0f));
                view.SetZoom(13f);
            }),
            new WaitFrames(2),
            new Capture("modern", "Industrial and modern roster (the gallery's lower part, partly below the frame): riflemen, marines, paratroopers, gun crews, cavalry and the great people; red and blue civs"),
            new Act("close view of the modern mounts", () =>
            {
                foreach (var name in new[] { "Cavalry", "Cossack", "Hussar" }) node(name, null).Face(MathF.PI / 2f);
                view.FocusOn(MapLayout.TileTop(coord(71).X, coord(71).Y, 0f));
                view.SetView(40f);
                view.SetZoom(3.5f);
            }),
            new WaitFrames(2),
            new Capture("modern-mounts", "Close view, side on: the Cavalry, Cossack and Hussar, riders on the generated mount (placeholder shapes)"),
    ];

    private static IEnumerable<ScenarioStep> EraSteps(MapView view, Func<int, HexCoord> coord, Func<string, string?, UnitNode> node) =>
    [
            new Act("frame the ancient and classical roster", () =>
            {
                view.FocusOn(MapLayout.TileTop(coord(30).X, coord(30).Y, 0f));
                view.SetZoom(13f);
            }),
            new WaitFrames(2),
            new Capture("ancient", "Ancient and classical roster (the top five rows; the gallery's lower rows show at the bottom): settler, axe infantry, spearmen and legionaries, throwers and bowmen, chariots, cavalry and elephants; red and blue civs"),
            new Act("close view of the cavalry and elephants", () =>
            {
                foreach (var name in new[] { "Cataphract", "War Elephant", "African Forest Elephant" }) node(name, null).Face(MathF.PI / 2f); // side on: the mounts' length shows
                view.FocusOn(MapLayout.TileTop(coord(41).X, coord(41).Y, 0f));
                view.SetView(40f);
                view.SetZoom(3.5f);
            }),
            new WaitFrames(2),
            new Capture("elephants", "Close view of the Cataphract, War Elephant and African Forest Elephant: riders on the generated mount, the elephants' a larger grey one (a placeholder shape)"),
    ];

    private static IEnumerable<ScenarioStep> RigidSteps(MapView view, List<UnitNode> nodes, Func<string, string?, UnitNode> node, Func<int, HexCoord> coord) =>
    [
            new Act("pose the rigid units mid-cue", () =>
            {
                node("Catapult", null).ShowRigid(RigidMotion.Pose(RigidKind.Siege, CueKind.Shot, 0.5f, 0.5f));
                node("Tank", null).ShowRigid(RigidMotion.Pose(RigidKind.Armor, CueKind.Shot, 0.1f, 0.1f));
                node("Destroyer", null).ShowRigid(RigidMotion.Pose(RigidKind.Ship, CueKind.Shot, 0.5f, 0.5f));
                node("Fighter", null).ShowRigid(RigidMotion.Pose(RigidKind.Air, CueKind.Strike, 0.5f, 0.5f));
            }),
            new AssertQuery("posed rigid figures moved off their slots (siege recoiled, armour turret turned, ship rolled, aircraft dived) and clip units did not", () =>
                node("Catapult", null).RigidPose.Recoil > 0.1f
                && node("Tank", null).GetNode<Node3D>("Figure0").GetNode<Node3D>("Tank/Part2").Rotation.Y > 0.3f
                && node("Destroyer", null).GetNode<Node3D>("Figure0").Basis.GetEuler().Z != 0f
                && node("Fighter", null).GetNode<Node3D>("Figure0").Basis.GetEuler().X != 0f
                && node("Warrior", Rome).RigidPose == RigidPose.Rest),
            new Act("close view of the posed tank", () =>
            {
                view.FocusOn(MapLayout.TileTop(coord(8).X, coord(8).Y, 0f));
                view.SetView(40f);
                view.SetZoom(4f);
            }),
            new WaitFrames(2),
            new Capture("rigid", "Close view of the Tank mid-shot, posed by the code-driven rigid motion: the hull has kicked back and the turret has swung off the target line (catapult, destroyer and fighter pose the same way)"),
            new Act("rigid units back to rest", () =>
            {
                foreach (var n in nodes) n.RestRigid();
            }),
            new AssertQuery("resting restores every rigid figure to its slot and turret", () =>
                nodes.Where(n => n.Rigid != RigidKind.None).All(n => n.RigidPose == RigidPose.Rest)
                && node("Tank", null).GetNode<Node3D>("Figure0").GetNode<Node3D>("Tank/Part2").Rotation.Y == 0f),
    ];

    private static IEnumerable<ScenarioStep> MountedSteps(MapView view, Func<string, string?, UnitNode> node, Func<int, HexCoord> coord) =>
    [
            new Act("close view of the Horseman", () =>
            {
                node("Horseman", null).Face(MathF.PI / 2f); // side on: the horse's length shows
                view.FocusOn(MapLayout.TileTop(coord(2).X, coord(2).Y, 0f));
                view.SetView(40f);
                view.SetZoom(3f);
            }),
            new WaitFrames(2),
            new Capture("mounted", "Close view of the Horseman: a CC0 rider seated on the generated horse rig, in its idle pose, the rider's tunic tinted in the red civ's colour (C083)"),
    ];
}
