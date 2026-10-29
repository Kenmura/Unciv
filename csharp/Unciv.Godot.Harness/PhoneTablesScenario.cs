using Godot;
using Unciv.Assets;
using Unciv.Commands;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Play;
using Unciv.Harness;

namespace Unciv.GodotHarness;

/// <summary>
/// T284: the diplomacy, trade, espionage and overview table screens as a phone shows them: a 2400x1080 window at 400 ppi (scale 2.5, 48 dp = 120 px targets), in the espionage game.
/// Each is asserted to have only touch-sized buttons and no text that leaves the window, is cut off or overlaps another. Real-device behaviour is not covered.
/// </summary>
public sealed class PhoneTablesScenario : Scenario
{
    private const float Dpi = 400f;

    public override string Name => "phone-tables";

    public override bool Uses3D => true;

    public override bool Espionage => true;

    public override Vector2I Viewport => new(2400, 1080);

    public override float TouchDpi => Dpi;

    public override IReadOnlyList<string> Checklist =>
    [
        "Can every table of the overview, diplomacy, trade and espionage screens be read and scrolled with a finger, and is every button big enough for a thumb?",
    ];

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session)
    {
        var game = session.Game;
        var screen = new GameScreen();
        host.AddChild(screen);
        screen.Start(new LocalHost(session, ScenarioGame.HumanA, () => 1000L), AssetRegistry.ForRuleset(game.Ruleset));
        int minTarget = UiTokens.DpToPx(UiTokens.TouchTargetDp, Dpi);

        ScenarioStep Fits(string what, Func<Node> node) => LayoutChecks.Assert($"on a phone no {what} text is cut off, leaves the window or overlaps another", host, node);
        ScenarioStep Touchable(string what, Func<Node> node) => LayoutChecks.Touchable("phone-tables", what, minTarget, node);
        ScenarioStep Reachable(string what, Func<Node> node) => LayoutChecks.Reachable("phone-tables", what, node);
        ScenarioStep[] Check(string what, Func<Node> node) => [Fits(what, node), Touchable(what, node), Reachable(what, node)];

        var steps = new List<ScenarioStep>
        {
            new WaitFrames(6),
            new Act("dismiss the start banner", () => screen.Hud.HideBanner()),
            new AssertQuery("the theme derived scale 2.5 and 120 px targets", () => screen.SharedTheme is { } theme && theme.Scale == 2.5f && theme.MinTargetPx == minTarget && minTarget == 120),
        };
        void Tab(string tab, string caption)
        {
            steps.Add(new Act($"open the overview on {tab}", () => screen.OpenOverviewScreen(tab)));
            steps.Add(new WaitFrames(5));
            steps.AddRange(Check($"overview {tab} tab", () => screen.Overview));
            steps.Add(new Capture("overview-" + tab.ToLowerInvariant(), caption));
        }
        Tab(OverviewModel.Stats, "Overview stats on a 2400x1080 400 ppi phone: two columns of cards, the page scrolls under the tab row");
        Tab(OverviewModel.Cities, "Overview cities table on a phone: the row's Go-to arrow is a 48 dp target, bulk actions below");
        Tab(OverviewModel.Units, "Overview units table on a phone: each row has a 48 dp (120 px) Go-to arrow");
        Tab(OverviewModel.Resources, "Overview resources on a phone");
        Tab(OverviewModel.Ranking, "Overview ranking on a phone");
        Tab(OverviewModel.Religion, "Overview religion on a phone");
        Tab(OverviewModel.Espionage, "Overview espionage tab on a phone");
        Tab(OverviewModel.Politics, "Overview politics table on a phone: all seven columns fit, vote line below");
        steps.Add(new Act("open diplomacy", () => screen.OpenDiplomacyScreen()));
        steps.Add(new WaitFrames(5));
        steps.AddRange(Check("diplomacy screen", () => screen.Diplomacy));
        steps.Add(new Capture("diplomacy", "Diplomacy on a phone: civ list and Rome's panel, Declare war and Denounce are touch targets"));
        steps.Add(new Act("open trade", () => screen.OpenTradeScreen()));
        steps.Add(new WaitFrames(5));
        steps.AddRange(Check("trade screen", () => screen.Trade));
        steps.Add(new Capture("trade", "Trade on a phone: both offer columns shown in full, the page scrolls to the summary, Clear and Propose trade in the footer"));
        steps.Add(new Act("open espionage", () => screen.OpenEspionageScreen()));
        steps.Add(new WaitFrames(5));
        steps.AddRange(Check("espionage screen", () => screen.Espionage));
        steps.Add(new Capture("espionage", "Espionage on a phone: the spy table with 48 dp Move and Coup buttons, Assign spies below"));
        return steps;
    }
}
