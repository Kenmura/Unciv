using Godot;
using Unciv.Assets;
using Unciv.Commands;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Play;
using Unciv.Harness;
using Unciv.Logic.Map;

namespace Unciv.GodotHarness;

/// <summary>
/// C096: the world screen as a phone shows it, driven by synthetic touch: a 2400x1080 window at 400 ppi (density scale 2.5, 48 dp = 120 px targets), then a
/// two-finger pinch (zoom about the midpoint), a two-finger pan, a tap (select), a tap that wobbles within 8 dp (still a tap), a drag past it (pans, selects
/// nothing), a second finger landing during a press (cancels it), and a long press (raises the right-click event, selects nothing). No command is submitted.
/// What this cannot show (real finger events, Android's emulation order, multitouch hardware) is listed in the ticket notes.
/// </summary>
public sealed class PhoneWorldScenario : Scenario
{
    private const float Dpi = 400f;

    public override string Name => "phone-world";

    public override bool Uses3D => true;

    public override bool Skirmish => true;

    public override Vector2I Viewport => new(2400, 1080);

    public override float TouchDpi => Dpi;

    public override IReadOnlyList<string> Checklist =>
    [
        "Is every button and tab big enough for a thumb (about 9 mm) (per-screen phone layout is C101: the top-right icon row overlapping Review alerts here is known)?",
    ];

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session)
    {
        var game = session.Game;
        var localHost = new LocalHost(session, ScenarioGame.HumanA, () => 1000L);
        var screen = new GameScreen();
        host.AddChild(screen);
        screen.Start(localHost, AssetRegistry.ForRuleset(game.Ruleset));
        screen.TutorialsEnabled = false;
        var map = screen.Map;
        var viewport = host.GetViewport();
        int clicked = 0, rightClicked = 0;
        map.TileClicked += _ => clicked++;
        map.TileRightClicked += _ => rightClicked++;
        int minTarget = UiTokens.DpToPx(UiTokens.TouchTargetDp, Dpi);
        int threshold = UiTokens.DpToPx(UiTokens.TouchDragThresholdDp, Dpi);
        Vector2 centre = new(1200f, 560f);
        HexCoord? tapped = null;
        float zoomBefore = 0f;
        Vector3 focusBefore = default;
        Vector2? groundBefore = null;
        var pinchMid = new Vector2(1250f, 520f);

        var undersized = () => AllButtons(screen).Where(it => it.IsVisibleInTree() && it.Size.Y > 0 && it.Size.Y + 0.5f < minTarget).Select(it => $"{it.GetType().Name} '{(it as Button)?.Text}' {it.Name} {it.Size.Y}").ToList();

        return
        [
            new WaitFrames(6),
            new AssertQuery("the theme derived a scale of 2.5 and a 120 px (48 dp) target from 400 ppi", () => screen.SharedTheme is { } theme && theme.Scale == 2.5f && theme.MinTargetPx == minTarget && minTarget == 120),
            new AssertQuery("every visible button of the world screen is at least 48 dp tall", () =>
            {
                var bad = undersized();
                foreach (string line in bad.Take(10)) GD.PrintErr("[phone-world] undersized " + line);
                return bad.Count == 0;
            }),
            new Capture("phone-hud", "The world screen on a 2400x1080 400 ppi phone: UI scale 2.5 (a 960x432 logical window) with every button at least 48 dp (120 px) tall"),
            new Act("pinch apart about the map centre", () =>
            {
                zoomBefore = map.Zoom;
                groundBefore = map.WorldAt(pinchMid);
                InputSimulator.SimulatePinch(viewport, pinchMid + new Vector2(-100, 0), pinchMid + new Vector2(100, 0), pinchMid + new Vector2(-250, 0), pinchMid + new Vector2(250, 0));
            }),
            new WaitFrames(2),
            new AssertQuery("spreading two fingers zoomed in, the ground under the midpoint stayed under it, and nothing was selected or sent", () =>
                map.Zoom < zoomBefore * 0.6f && map.WorldAt(pinchMid) is { } now && groundBefore is { } was && now.DistanceTo(was) < 0.05f && clicked == 0 && map.Selected == null),
            new Act("pinch together", () =>
            {
                zoomBefore = map.Zoom;
                InputSimulator.SimulatePinch(viewport, pinchMid + new Vector2(-300, 0), pinchMid + new Vector2(300, 0), pinchMid + new Vector2(-150, 0), pinchMid + new Vector2(150, 0));
            }),
            new WaitFrames(2),
            new AssertQuery("closing two fingers zoomed out", () => map.Zoom > zoomBefore * 1.3f),
            new Act("two-finger pan to the left", () =>
            {
                zoomBefore = map.Zoom;
                focusBefore = map.Focus;
                InputSimulator.SimulatePinch(viewport, centre + new Vector2(-100, 0), centre + new Vector2(100, 0), centre + new Vector2(-300, 0), centre + new Vector2(-100, 0));
            }),
            new WaitFrames(2),
            new AssertQuery("moving both fingers panned the camera and kept the zoom", () => Math.Abs(map.Zoom - zoomBefore) < 0.01f && map.Focus.DistanceTo(focusBefore) > 0.5f && clicked == 0),
            new Act("pick a tile under the centre", () => tapped = map.PickTile(centre)),
            new AssertQuery("there is a tile to tap", () => tapped != null),
            new Act("tap it", () => InputSimulator.SimulateTap(viewport, centre)),
            new WaitFrames(2),
            new AssertQuery("a tap selected the tile through the emulated mouse", () => map.Selected == tapped && clicked == 1 && rightClicked == 0),
            new Act("wobble within 8 dp of the tile next to it", () =>
            {
                var other = map.ScreenPositionOf(new HexCoord((tapped ?? throw new InvalidOperationException("no tile")).x + 1, tapped.Value.y));
                focusBefore = map.Focus;
                var from = other;
                InputSimulator.SimulateTouchDown(viewport, 0, from);
                var to = from + new Vector2(threshold * 0.6f, 0);
                InputSimulator.SimulateTouchMove(viewport, 0, from, to);
                InputSimulator.SimulateTouchUp(viewport, 0, to);
            }),
            new WaitFrames(2),
            new AssertQuery("a wobble inside the finger threshold is still a tap: it selected a tile and did not pan", () => clicked == 2 && map.Focus.DistanceTo(focusBefore) < 0.001f),
            new Act("drag a finger well past the threshold", () =>
            {
                focusBefore = map.Focus;
                InputSimulator.SimulateTouchDown(viewport, 0, centre);
                var to = centre + new Vector2(threshold * 3f, 0);
                InputSimulator.SimulateTouchMove(viewport, 0, centre, to);
                InputSimulator.SimulateTouchUp(viewport, 0, to);
            }),
            new WaitFrames(2),
            new AssertQuery("a drag past the threshold panned the map and selected nothing new", () => clicked == 2 && map.Focus.DistanceTo(focusBefore) > 0.2f),
            new Act("land a second finger during a press, then lift both without moving", () =>
            {
                InputSimulator.SimulateTouchDown(viewport, 0, centre);
                InputSimulator.SimulateTouchDown(viewport, 1, centre + new Vector2(200, 0));
                InputSimulator.SimulateTouchUp(viewport, 1, centre + new Vector2(200, 0));
                InputSimulator.SimulateTouchUp(viewport, 0, centre);
            }),
            new WaitFrames(2),
            new AssertQuery("the second finger cancelled the press: no tap", () => clicked == 2 && rightClicked == 0),
            new Act("a finger resting only briefly is not a long press", () =>
            {
                InputSimulator.SimulateTouchDown(viewport, 0, centre);
                InputSimulator.SimulateTouchUp(viewport, 0, centre);
            }),
            new WaitFrames(2),
            new AssertQuery("a brief touch is a tap", () => clicked == 3 && rightClicked == 0),
            new Act("press and hold", () => InputSimulator.SimulateTouchDown(viewport, 0, centre)),
            new WaitUntil("the held finger raised the right-click event", () => rightClicked == 1, 600),
            new Act("lift", () => InputSimulator.SimulateTouchUp(viewport, 0, centre)),
            new WaitFrames(2),
            new AssertQuery("the long press selected nothing and sent no command", () => clicked == 3 && rightClicked == 1 && session.Log.Count == 0),
            new Capture("phone-zoomed", "After a pinch, a two-finger pan, taps and a long press: the map zoomed about the fingers, the tapped tile is selected, and the long press acted as a right click (the selected worker's move order was refused: it is already there)"),
        ];
    }

    private static IEnumerable<BaseButton> AllButtons(Node root)
    {
        foreach (var child in root.GetChildren())
        {
            if (child is BaseButton button and not CheckBox) yield return button;
            foreach (var nested in AllButtons(child)) yield return nested;
        }
    }
}
