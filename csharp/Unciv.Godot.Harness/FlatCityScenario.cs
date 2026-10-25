using Godot;
using Unciv.Commands;
using Unciv.Commands.City;
using Unciv.Commands.Empire;
using Unciv.Commands.Units;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Map;
using Unciv.GodotClient.Play;
using Unciv.Harness;
using Unciv.Logic.Map;
using Unciv.Logic.Map.MapUnits;
using Unciv.Logic.Map.Tiles;
using Unciv.Models;

namespace Unciv.GodotHarness;

/// <summary>
/// C041: cities, borders, roads and improvements on the flat map, through the real screen and the real engine. The solo game starts without a city: the settler founds one
/// (the city block, its banner and its territory border appear), a worker bought with the God mode switch builds a road next to it and a trading post on it (a road link and an
/// improvement marker appear), and a click on the city opens the placeholder city panel. Engine state is asserted before every capture.
/// </summary>
public sealed class FlatCityScenario : Scenario
{
    public override string Name => "flat-city";

    public override bool Uses3D => true;

    public override bool Solo => true;

    public override bool GodMode => true;

    public override IReadOnlyList<string> Checklist =>
    [
        "Can you find the city at a glance, and does the banner read (name, size, what it builds)?",
        "Is the territory border clearly your colour without hiding the terrain?",
        "Do the road and the improvement marker read as such, and do they stay out of the way of the unit blocks?",
    ];

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session)
    {
        var game = session.Game;
        var human = game.GetCivilization(ScenarioGame.HumanA);
        long now = 1000;
        var localHost = new LocalHost(session, ScenarioGame.HumanA, () => now += 1000);
        var screen = new GameScreen();
        host.AddChild(screen);
        screen.Start(localHost, AssetRegistry.ForRuleset(game.Ruleset));
        var settler = human.Units.GetCivUnits().First(it => it.Name == "Settler");
        var cityTile = new HexCoord(settler.CurrentTile.Position.x, settler.CurrentTile.Position.y);
        var roadTile = new HexCoord(cityTile.x + 1, cityTile.y);
        var refusals = new List<string>();

        void Do(GameCommand command)
        {
            var result = localHost.SubmitAsync(command).GetAwaiter().GetResult();
            if (!result.Accepted) refusals.Add($"{command.GetType().Name}: {result.Message}");
        }

        MapUnit Worker() => human.Units.GetCivUnits().First(it => it.Name == "Worker");

        return
        [
            new WaitFrames(3),
            new Act("zoom in on the settler", () =>
            {
                screen.Map.FocusOn(MapLayout.TileTop(cityTile.x, cityTile.y));
                screen.Map.SetZoom(6f);
            }),
            new AssertQuery("before founding there is no city, border, road or improvement", () =>
                screen.Overlays.Cities.Count == 0 && screen.Overlays.BorderEdgeCount == 0 && screen.Overlays.RoadLinkCount == 0 && screen.Overlays.ImprovementCount == 0),
            new Act("dismiss the start banner", screen.Hud.HideBanner),
            new Capture("before", "The settler and warrior on open ground: no city, no border"),
            new Act("found the city, research, and buy a worker (God mode)", () =>
            {
                Do(new ExecuteUnitAction { UnitId = settler.Id, Descriptor = UnitActionDescriptor.Describe(settler).First(it => it.Descriptor.ActionType == UnitActionType.FoundCity).Descriptor });
                var city = human.Cities[0];
                Do(new DebugAddTechnology { Tech = "The Wheel" });
                Do(new DebugAddTechnology { Tech = "Trapping" });
                Do(new SetResearchQueue { Techs = ["Pottery"] });
                Do(new QueueConstruction { CityId = city.Id, Name = "Worker" });
                Do(new PurchaseConstruction { CityId = city.Id, Name = "Worker" });
                Do(new MoveUnit { UnitId = Worker().Id, To = roadTile });
                screen.Refresh();
            }),
            new WaitFrames(2),
            new AssertQuery("every setup command was accepted", () => refusals.Count == 0),
            new AssertQuery("the founded city is drawn at its tile with a banner naming it and its size", () =>
                screen.Overlays.Cities.Count == 1 && screen.Overlays.Cities.TryGetValue(cityTile, out var city) && city.BannerText.Contains(human.Cities[0].Name, StringComparison.Ordinal) && city.BannerText.Contains("(1)", StringComparison.Ordinal)),
            new AssertQuery("the city's territory has a border in the civ's colour: at least the outline of the work radius", () => screen.Overlays.BorderEdgeCount >= 18),
            new AssertQuery("no road yet", () => screen.Overlays.RoadLinkCount == 0),
            new Act("dismiss the banner", screen.Hud.HideBanner),
            new Capture("city", "The founded city: block, banner (name, size) and the territory border"),
            new Press("Next turn"),
            new WaitFrames(1),
            new Act("dismiss the new-turn banner", screen.Hud.HideBanner),
            new Act("order the worker to build a road", () =>
            {
                Do(new SetWorkerOrder { UnitId = Worker().Id, Order = WorkerOrderKind.Build, Improvement = "Road" });
            }),
            new Press("Next turn"),
            new WaitFrames(1),
            new Act("dismiss the new-turn banner", screen.Hud.HideBanner),
            new AssertQuery("the finished road is drawn as a link from the city to the worker's tile", () =>
                refusals.Count == 0 && game.TileMap[roadTile].RoadStatus != RoadStatus.None && screen.Overlays.RoadLinkCount == 1),
            new Capture("road", "The road next to the city, drawn as a strip from the city to the next tile"),
            new Act("order the worker to build a trading post", () =>
            {
                Do(new SetWorkerOrder { UnitId = Worker().Id, Order = WorkerOrderKind.Build, Improvement = "Trading post" });
            }),
            new Press("Next turn"),
            new WaitFrames(1),
            new Act("dismiss the new-turn banner", screen.Hud.HideBanner),
            new AssertQuery("the finished improvement is marked on its tile", () =>
                refusals.Count == 0 && game.TileMap[roadTile].Improvement == "Trading post" && screen.Overlays.ImprovementCount == 1 && screen.Overlays.PillagedImprovementCount == 0),
            new Capture("improvement", "The trading post marker on the road tile"),

            new Click(() => screen.Map.ScreenPositionOf(cityTile)),
            new AssertQuery("the first click on the city tile selects the warrior standing there and opens nothing", () =>
                screen.SelectedUnitId != null && screen.OpenCity == null && !screen.IsCityPanelVisible),
            new Click(() => screen.Map.ScreenPositionOf(cityTile)),
            new AssertQuery("the next click on the city opens its panel with the right city", () =>
                screen.OpenCity?.Name == human.Cities[0].Name && screen.IsCityPanelVisible),
            new Capture("city-open", "A click on the city opened the city screen"),
            new Press("Close city"),
            new AssertQuery("the close button closes the panel", () => screen.OpenCity == null && !screen.IsCityPanelVisible),
            new AssertQuery("the whole scenario stayed on the command path: nothing was refused", () => refusals.Count == 0),
        ];
    }
}
