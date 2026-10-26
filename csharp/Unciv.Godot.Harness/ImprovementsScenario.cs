using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Map;
using Unciv.Commands;
using Unciv.Logic.Map;
using Unciv.Models.StatTypes;
using Unciv.Net.Visibility;

namespace Unciv.GodotHarness;

/// <summary>
/// C032: improvement props, roads and railroads, city models and flags, civ-colour borders and in-world yield icons on a fixed gallery map (the view and the overlay layer built directly from
/// viewer-style tile rows, as the game screen does). Asserts first (one prop per improvement, crowded tiles shrink the prop, pillaged ones lean and darken, remembered ones dim, city size tiers,
/// road and rail links, yield chips and the yields toggle), then captures the gallery from two angles and with the yields off.
/// </summary>
public sealed class ImprovementsScenario : Scenario
{
    public override string Name => "improvements";

    public override bool Uses3D => true;

    public override IReadOnlyList<string> Checklist =>
    [
        "Can you tell a farm, mine, pasture, camp, quarry and lumber mill apart at the default camera?",
        "Do the road (brown) and railroad (dark with light rails) read as links between tile centres, and do the city flags and borders show whose land it is?",
        "Are the yield icons legible without hiding the props, and does the town look bigger than the village?",
    ];

    private static readonly (string Improvement, string Terrain, string[] Features, string? Resource)[] Sites =
    [
        ("Farm", "Plains", [], null),
        ("Mine", "Plains", ["Hill"], null),
        ("Pasture", "Grassland", [], "Cattle"),
        ("Camp", "Plains", [], "Deer"),
        ("Plantation", "Grassland", [], "Bananas"),
        ("Quarry", "Plains", ["Hill"], "Stone"),
        ("Lumber mill", "Grassland", ["Forest"], null),
        ("Trading post", "Desert", [], null),
        ("Fort", "Plains", [], null),
        ("Citadel", "Grassland", [], null),
        ("Fishing Boats", "Coast", [], "Fish"),
        ("Oil well", "Desert", [], "Oil"),
        ("Offshore Platform", "Ocean", [], "Oil"),
        ("Academy", "Plains", [], null),
        ("Customs house", "Grassland", [], null),
        ("Manufactory", "Plains", [], null),
        ("Holy site", "Grassland", [], null),
        ("Landmark", "Desert", [], null),
        ("Terrace farm", "Plains", ["Hill"], null),
        ("Moai", "Coast", [], null),
        ("Ancient ruins", "Grassland", [], null),
        ("Barbarian encampment", "Plains", [], null),
    ];

    private static readonly HexCoord Village = new(0, 0);
    private static readonly HexCoord Town = new(-3, -3);
    private static readonly HexCoord PillagedAt = new(0, 3);
    private static readonly HexCoord FoggedAt = new(0, -3);

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session)
    {
        var ruleset = session.Game.Ruleset;
        var assets = AssetRegistry.ForRuleset(ruleset);
        var tiles = Gallery(out var improved);
        var cities = new List<CityDto> { City("Ur", Village, 3, "Babylon"), City("Athens", Town, 12, "Greece") };

        var view = new MapView();
        host.AddChild(view);
        view.Load(assets, tiles);
        var layer = new MapOverlayLayer();
        host.AddChild(layer);
        Rgb? Colour(string owner) => owner == "Babylon" ? new Rgb(60, 110, 220) : owner == "Greece" ? new Rgb(230, 190, 40) : null;
        layer.Rebuild(assets, ruleset, view, tiles, cities, Colour);

        int Parts(Node3D? n) => n?.GetChildren().OfType<MeshInstance3D>().Count() ?? 0;
        float Brightness(Node3D? n) => n == null ? 0f : n.GetChildren().OfType<MeshInstance3D>().Select(it => ((StandardMaterial3D)it.MaterialOverride).AlbedoColor).Average(c => c.R + c.G + c.B);
        Node3D Imp(HexCoord c) => layer.ImprovementAt(c) ?? new Node3D();
        HexCoord AtImprovement(string name) => improved[name];
        var plainFarm = AtImprovement("Farm");
        var cattle = AtImprovement("Pasture");

        return
        [
            new WaitFrames(3),
            new AssertQuery("one prop per improvement on the map (city centres are cities, not improvements)", () =>
                layer.ImprovementCount == Sites.Length + 2 && improved.All(it => layer.ImprovementAt(it.Value) != null)),
            new AssertQuery("improvements are built from several parts, farms differ from mines", () =>
                Parts(layer.ImprovementAt(plainFarm)) > 3 && Parts(layer.ImprovementAt(AtImprovement("Mine"))) > 3 && Parts(layer.ImprovementAt(plainFarm)) != Parts(layer.ImprovementAt(AtImprovement("Mine")))),
            new AssertQuery("on a tile with a resource the improvement prop shrinks and moves aside", () =>
                Imp(cattle).Scale.X < 1f && Imp(plainFarm).Scale.X == 1f
                && Imp(cattle).Position.DistanceTo(MapLayout.TileTop(cattle.X, cattle.Y, 0f) + new Vector3(0f, Imp(cattle).Position.Y, 0f)) > 0.3f),
            new AssertQuery("a pillaged improvement leans over and is darker than an intact one of its kind", () =>
                Imp(PillagedAt).RotationDegrees.Z > 5f && Brightness(layer.ImprovementAt(PillagedAt)) < Brightness(layer.ImprovementAt(AtImprovement("Mine"))) * 0.8f),
            new AssertQuery("a remembered improvement on a fogged tile is dimmer than a visible one", () =>
                Brightness(layer.ImprovementAt(FoggedAt)) < Brightness(layer.ImprovementAt(plainFarm)) * 0.9f),
            new AssertQuery("two cities with civ-coloured flags; the larger city is built from more parts", () =>
                layer.Cities.Count == 2 && Parts(layer.Cities[Town].GetNode<Node3D>("Model")) > Parts(layer.Cities[Village].GetNode<Node3D>("Model"))
                && layer.Cities[Village].GetNode<MeshInstance3D>("Flag").MaterialOverride is StandardMaterial3D flag && flag.AlbedoColor.B > flag.AlbedoColor.R
                && layer.Cities[Town].BannerText.Contains("Athens", StringComparison.Ordinal)),
            new AssertQuery("roads link the village to its neighbours and a railroad line stands apart", () => layer.RoadLinkCount >= 6 && layer.GetNodeOrNull<MeshInstance3D>("Rails")?.Mesh != null),
            new AssertQuery("both territories have borders", () => layer.BorderEdgeCount >= 24),
            new AssertQuery("yield chips: a bare grassland tile shows exactly 2 food", () =>
                layer.YieldsAt(new HexCoord(2, -1)).SequenceEqual([new YieldChip(Stat.Food, 2)])),
            new AssertQuery("a farmed tile shows more food than the bare grassland; city tiles show none", () =>
                layer.YieldsAt(plainFarm).Count > 0 && layer.YieldsAt(plainFarm).First(it => it.Stat == Stat.Food).Amount >= 2 && layer.YieldsAt(Village).Count == 0),
            new AssertQuery("the yield icons are shown", () => layer.YieldChipCount > 20 && layer.VisibleYieldChipCount == layer.YieldChipCount),
            new Act("frame the gallery", () =>
            {
                view.FocusOn(MapLayout.TileTop(-1, 0, 0f));
                view.SetZoom(10f);
            }),
            new WaitFrames(2),
            new Capture("gallery-default", "Improvement gallery, default camera: props, roads and rails, two cities with flags, borders, yield icons"),
            new Act("close view of the village", () =>
            {
                view.FocusOn(MapLayout.TileTop(0, 0, 0f));
                view.SetView(40f);
                view.SetZoom(5.5f);
            }),
            new WaitFrames(2),
            new Capture("village-close", "Low close view of the village, its flag, road links and neighbouring improvements"),
            new Act("yields off, as the settings toggle does", () =>
            {
                layer.ShowYields = false;
                view.SetView(MapView.TiltDegrees);
                view.FocusOn(MapLayout.TileTop(-1, 0, 0f));
                view.SetZoom(10f);
            }),
            new WaitFrames(2),
            new AssertQuery("with the setting off no yield icon is visible", () => layer.VisibleYieldChipCount == 0 && !layer.GetNode<Node3D>("Yields").Visible),
            new Capture("yields-off", "The same gallery with tile yields switched off"),
            new Act("yields back on", () => layer.ShowYields = true),
            new AssertQuery("switching the setting back shows them again", () => layer.VisibleYieldChipCount == layer.YieldChipCount && layer.GetNode<Node3D>("Yields").Visible),
        ];
    }

    private static CityDto City(string name, HexCoord at, int population, string owner) => new()
    {
        Name = name,
        Owner = owner,
        X = at.X,
        Y = at.Y,
        Population = population,
        IsCapital = name == "Ur",
        IsPuppet = false,
        IsBeingRazed = false,
        Health = 20,
        MaxHealth = 20,
        VisibleBuildings = [],
    };

    /// <summary>All tiles within three steps of the origin. Sites are dealt out in a fixed order to the tiles that are neither a city, a road nor reserved.</summary>
    private static List<TileDto> Gallery(out Dictionary<string, HexCoord> improved)
    {
        improved = [];
        var coords = new List<HexCoord>();
        for (var x = -3; x <= 3; x++)
        {
            for (var y = -3; y <= 3; y++)
            {
                if (Math.Abs(x - y) <= 3) coords.Add(new HexCoord(x, y));
            }
        }
        var roads = new Dictionary<HexCoord, string>
        {
            [new HexCoord(1, 0)] = "Road", [new HexCoord(1, 1)] = "Road", [new HexCoord(0, 1)] = "Road", [new HexCoord(2, 1)] = "Road",
            [new HexCoord(-1, 0)] = "Road", [new HexCoord(-2, 0)] = "Railroad", [new HexCoord(-3, 0)] = "Railroad",
        };
        var reserved = new HashSet<HexCoord>([Village, Town, PillagedAt, FoggedAt, .. roads.Keys]);
        var free = new Queue<HexCoord>(coords.Where(it => !reserved.Contains(it) && !(it.X == 2 && it.Y == -1)));
        var improvementOf = new Dictionary<HexCoord, (string Name, string Terrain, string[] Features, string? Resource)>();
        foreach (var site in Sites)
        {
            var at = free.Dequeue();
            improvementOf[at] = site;
            improved[site.Improvement] = at;
        }
        improvementOf[PillagedAt] = ("Mine", "Plains", ["Hill"], null);
        improvementOf[FoggedAt] = ("Farm", "Plains", [], null);
        improved["Mine (pillaged)"] = PillagedAt;
        improved["Farm (remembered)"] = FoggedAt;

        string? OwnerOf(HexCoord c)
        {
            if (Dist(c, Village) <= 1) return "Babylon";
            return Dist(c, Town) <= 1 ? "Greece" : null;
        }

        return [.. coords.Select(c =>
        {
            improvementOf.TryGetValue(c, out var site);
            var visible = c != FoggedAt;
            var isCity = c == Village || c == Town;
            return new TileDto
            {
                X = c.X,
                Y = c.Y,
                Visible = visible,
                BaseTerrain = isCity ? "Grassland" : site.Terrain ?? "Grassland",
                Features = [.. site.Features ?? []],
                Resource = site.Resource,
                Improvement = isCity ? "City center" : site.Name,
                ImprovementPillaged = c == PillagedAt ? true : visible && site.Name != null ? false : null,
                RiverBottom = false,
                RiverBottomLeft = false,
                RiverBottomRight = false,
                Owner = visible ? OwnerOf(c) : null,
                Road = visible ? roads.GetValueOrDefault(c, "None") : null,
                RoadPillaged = visible ? false : null,
            };
        })];
    }

    private static int Dist(HexCoord a, HexCoord b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Max(Math.Abs(dx), Math.Max(Math.Abs(dy), Math.Abs(dx - dy)));
    }
}
