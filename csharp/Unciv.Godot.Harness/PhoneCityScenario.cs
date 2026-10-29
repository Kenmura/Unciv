using Godot;
using Unciv.Assets;
using Unciv.Commands;
using Unciv.Commands.City;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Play;
using Unciv.Commands.Empire;
using Unciv.Harness;

namespace Unciv.GodotHarness;

/// <summary>
/// T286: the city screen and its production half as a phone shows them: a 2400x1080 window at 400 ppi (scale 2.5, 48 dp = 120 px targets).
/// Each view is asserted to have only touch-sized buttons (the tile hexagons are judged separately), no text that leaves the window, is cut off or overlaps another,
/// and no content behind a container that cannot scroll. Real-device behaviour is not covered.
/// </summary>
public sealed class PhoneCityScenario : Scenario
{
    private const float Dpi = 400f;

    public override string Name => "phone-city";

    public override bool Uses3D => true;

    public override bool GodMode => true;

    public override Vector2I Viewport => new(2400, 1080);

    public override float TouchDpi => Dpi;

    public override IReadOnlyList<string> Checklist =>
    [
        "On the phone, can every control of the city screen be hit with a thumb, and can the production queue, buildable list and buildings be scrolled to?",
    ];

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session)
    {
        var game = session.Game;
        var screen = new GameScreen();
        host.AddChild(screen);
        var local = new LocalHost(session, ScenarioGame.HumanA, () => 1000L);
        screen.Start(local, AssetRegistry.ForRuleset(game.Ruleset));
        var city = ScenarioGame.FirstCity(game, ScenarioGame.HumanA);
        int minTarget = UiTokens.DpToPx(UiTokens.TouchTargetDp, Dpi);
        var refusals = new List<string>();

        void Do(GameCommand command)
        {
            var result = local.SubmitAsync(command).GetAwaiter().GetResult();
            if (!result.Accepted) refusals.Add($"{command.GetType().Name}: {result.Message}");
        }

        ScenarioStep[] Check(string what, Func<Node> node) =>
        [
            LayoutChecks.Assert($"on a phone no {what} text is cut off, leaves the window or overlaps another", host, node),
            LayoutChecks.Touchable("phone-city", what, minTarget, node, it => it is CityTileButton),
            LayoutChecks.Reachable("phone-city", what, node),
        ];

        return
        [
            new WaitFrames(6),
            new Act("dismiss the start banner, give the capital buildings and a queue, and open its city screen", () =>
            {
                screen.Hud.HideBanner();
                foreach (string tech in new[] { "Bronze Working", "Currency", "Pottery", "Masonry" }) Do(new DebugAddTechnology { Tech = tech });
                foreach (string building in new[] { "Market", "Granary", "Monument" })
                {
                    Do(new QueueConstruction { CityId = city.Id, Name = building });
                    Do(new PurchaseConstruction { CityId = city.Id, Name = building });
                }
                foreach (string item in new[] { "Warrior", "Worker", "Scout" }) Do(new QueueConstruction { CityId = city.Id, Name = item });
                screen.Refresh();
                screen.OpenCityScreen(local.Snapshot.Cities.First(it => it.Own?.Id == city.Id));
            }),
            new WaitFrames(6),
            new AssertQuery("the setup commands were accepted and the city screen is open on the capital", () =>
            {
                foreach (string refusal in refusals) GD.PrintErr("[phone-city] refused " + refusal);
                return refusals.Count == 0 && screen.IsCityPanelVisible && screen.City.Panel != null;
            }),
            new AssertQuery("the theme derived scale 2.5 and 120 px targets", () => screen.SharedTheme is { } theme && theme.Scale == 2.5f && theme.MinTargetPx == minTarget && minTarget == 120),
            .. Check("citizens view", () => screen.City),
            new AssertQuery("the tile hexagons (exempt from 48 dp: seven rows of them have to fit one screen) are at least 72 px tall (about 29 dp: seven rows share the height)", () => screen.City.Cells.All(it => screen.City.TileButton(it.X, it.Y) is { Size.Y: >= 72f })),
            new Capture("citizens", "City screen on a 2400x1080 400 ppi phone: meters, stat chips and specialists in the side column (it scrolls), the tile grid beside it"),
            new Act("show the production tab", () => screen.City.SetProduction(true)),
            new WaitFrames(6),
            .. Check("production view", () => screen.City),
            new Capture("production", "City production on a phone: queue, buildable list and buildings"),
        ];
    }
}
