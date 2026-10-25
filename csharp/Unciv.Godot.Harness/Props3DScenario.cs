using Godot;
using Unciv.Assets;
using Unciv.Commands;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Map;
using Unciv.Logic.Map;
using Unciv.Net.Visibility;

namespace Unciv.GodotHarness;

/// <summary>
/// C003: feature and resource props. Asserts first (herds of 3-5 posed animals, forest/jungle/marsh rings, feature and resource sharing a tile, props following hill
/// height, fogged props dimmed, prop count), then proves the swap: a second view built with the <c>swap-test</c> pack (which replaces only <c>resource/Horses</c>)
/// shows the replacement on the horse tile and leaves every other prop untouched. Captures the gallery at two camera angles.
/// </summary>
public sealed class Props3DScenario : Scenario
{
    public override string Name => "props-3d";

    public override bool Uses3D => true;

    public override IReadOnlyList<string> Checklist =>
    [
        "Is the horse herd (centre tile) readable as three to five horses facing different ways?",
        "Can you tell forest, jungle and marsh apart, and do they still leave room for a resource on the same tile?",
        "Can you tell livestock, crops, ore and sea resources apart, and is the dimmed (fogged) tile's forest darker than its visible twin?",
    ];

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session)
    {
        var tiles = Gallery(out var fogged);
        var ruleset = session.Game.Ruleset;

        var view = new MapView();
        host.AddChild(view);
        view.Load(AssetRegistry.ForRuleset(ruleset), tiles);

        HexCoord At(int x, int y) => new(x, y);
        // A missing prop is an empty node, so every assertion below simply fails instead of throwing
        Node3D Prop(MapView v, int x, int y, string name) => v.PropsAt(At(x, y)).FirstOrDefault(it => it.Name == name) ?? new Node3D();
        int Parts(Node3D n) => n.GetChildren().OfType<MeshInstance3D>().Count();
        float Brightness(Node3D n) => n.GetChildren().OfType<MeshInstance3D>().Select(it => ((StandardMaterial3D)it.MaterialOverride).AlbedoColor).Average(c => c.R + c.G + c.B);
        var expectedProps = tiles.Sum(t => t.Features.Count(PropSpecs.HasFeatureProp) + (t.Resource != null ? 1 : 0));
        var swapOk = false;
        var swapUntouched = false;

        return
        [
            new WaitFrames(3),
            new AssertQuery("every prop of the gallery is on the map", () => view.PropCount == expectedProps && expectedProps > 8),
            new AssertQuery("horses are a herd of 3-5 animals of 3 blocks each", () => Parts(Prop(view, 0, 0, "Resource")) is >= 9 and <= 15 && Parts(Prop(view, 0, 0, "Resource")) % 3 == 0),
            new AssertQuery("a herd's animals face different ways", () => Prop(view, 0, 0, "Resource").GetChildren().OfType<MeshInstance3D>().Select(it => (int)it.RotationDegrees.Y).Distinct().Count() > 2),
            new AssertQuery("forest, jungle and marsh each have a feature prop", () => Parts(Prop(view, 1, 0, "Feature_Forest")) == 14 && Parts(Prop(view, 1, -1, "Feature_Jungle")) == 16 && Parts(Prop(view, 0, 1, "Feature_Marsh")) == 11),
            new AssertQuery("a tile can carry a feature prop and a resource prop", () => view.PropsAt(At(-1, 1)).Count == 2),
            new AssertQuery("props on a hill stand higher than on flat land", () => Prop(view, -1, 0, "Resource").Position.Y > Prop(view, 0, 0, "Resource").Position.Y),
            new AssertQuery("a sea resource stands lower than a land one", () => Prop(view, 2, 0, "Resource").Position.Y < Prop(view, 0, 0, "Resource").Position.Y),
            new AssertQuery("hills do not float forests: forest on a hill sits at ring height, below the hill top", () => Prop(view, -2, 0, "Feature_Forest").Position.Y < TerrainRelief.TopHeight(ReliefKind.Hill)),
            new AssertQuery("a fogged tile's forest is darker than its visible twin", () => Brightness(Prop(view, fogged.X, fogged.Y, "Feature_Forest")) < Brightness(Prop(view, 1, 0, "Feature_Forest")) * 0.9f),
            new Act("swap test: view built with the swap-test pack", () =>
            {
                var swapped = new MapView();
                host.AddChild(swapped);
                var registry = AssetRegistry.ForRuleset(ruleset, "swap-test");
                swapped.Load(registry, tiles);
                var marker = Prop(swapped, 0, 0, "Resource");
                swapOk = registry.Resolve(new AssetKey("resource", "Horses")).Source == AssetSourceKind.Pack
                    && Parts(marker) == 1 && marker.GetChildren().OfType<MeshInstance3D>().First().Name == "Marker";
                swapUntouched = Parts(Prop(swapped, 1, 0, "Feature_Forest")) == 14
                    && Parts(Prop(swapped, 3, 0, "Resource")) == Parts(Prop(view, 3, 0, "Resource"))
                    && swapped.PropCount == view.PropCount;
                swapped.QueueFree();
                view.Camera.MakeCurrent();
            }),
            new WaitFrames(2),
            new AssertQuery("overriding only resource/Horses replaces the herd by the pack's model", () => swapOk),
            new AssertQuery("every other prop is unchanged by that override", () => swapUntouched),
            new Act("frame the gallery", () =>
            {
                view.FocusOn(MapLayout.TileTop(0, 0, 0f));
                view.SetZoom(8f);
            }),
            new WaitFrames(2),
            new Capture("gallery-default", "Props gallery, default tilt: horse herd centre, forests, jungle, marsh, crops, ore, sea resources"),
            new Act("close low view of the herd", () =>
            {
                view.SetView(35f);
                view.SetZoom(5f);
            }),
            new WaitFrames(2),
            new Capture("herd-close", "Low close view of the horse herd and its neighbours"),
        ];
    }

    /// <summary>
    /// Fixed gallery: centre Plains with Horses; around it Cattle, Sheep, Wheat, hill+Iron, forest, jungle, marsh, forest+Deer (both props), forest+Horses on a fogged tile,
    /// then Coast with Fish and Ocean with Whales further out.
    /// </summary>
    private static List<TileDto> Gallery(out TileDto fogged)
    {
        TileDto Tile(int x, int y, string terrain, string[]? features = null, string? resource = null, bool visible = true) => new()
        {
            X = x,
            Y = y,
            Visible = visible,
            BaseTerrain = terrain,
            Features = [.. features ?? []],
            Resource = resource,
            RiverBottom = false,
            RiverBottomLeft = false,
            RiverBottomRight = false,
        };
        var list = new List<TileDto>
        {
            Tile(0, 0, "Plains", resource: "Horses"),
            Tile(1, 0, "Grassland", ["Forest"]),
            Tile(1, -1, "Plains", ["Jungle"]),
            Tile(0, 1, "Grassland", ["Marsh"]),
            Tile(-1, 0, "Plains", ["Hill"], "Iron"),
            Tile(-1, 1, "Grassland", ["Forest"], "Deer"),
            Tile(0, -1, "Grassland", resource: "Cattle"),
            Tile(1, 1, "Plains", resource: "Wheat"),
            Tile(-1, -1, "Grassland", resource: "Sheep"),
            Tile(-2, 0, "Grassland", ["Hill", "Forest"]),
            Tile(2, -1, "Plains", ["Forest"], "Horses", visible: false),
            Tile(2, 0, "Coast", resource: "Fish"),
            Tile(3, 0, "Ocean", resource: "Whales"),
            Tile(3, -1, "Grassland", resource: "Cattle"),
            Tile(-2, 1, "Plains", resource: "Coal"),
            Tile(-2, 2, "Grassland", resource: "Bananas"),
        };
        fogged = list[10];
        return list;
    }
}
