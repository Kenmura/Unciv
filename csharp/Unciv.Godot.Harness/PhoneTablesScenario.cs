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
        ScenarioStep Touchable(string what, Func<Node> node) => new AssertQuery($"every visible button of the {what} is at least 48 dp (120 px) tall", () =>
        {
            var bad = Buttons(node()).Where(it => it.IsVisibleInTree() && it.Size.Y > 0 && it.Size.Y + 0.5f < minTarget).Select(it => $"{it.GetType().Name} '{(it as Button)?.Text}' {it.Name} {it.Size.Y}").ToList();
            foreach (string line in bad.Take(12)) GD.PrintErr($"[phone-tables] {what}: undersized {line}");
            return bad.Count == 0;
        });
        ScenarioStep Reachable(string what, Func<Node> node) => new AssertQuery($"nothing of the {what} is cut off by a container that cannot scroll", () =>
        {
            var bad = Unscrollable(node()).ToList();
            foreach (string line in bad.Take(12)) GD.PrintErr($"[phone-tables] {what}: {line}");
            return bad.Count == 0;
        });
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
        Tab(OverviewModel.Cities, "Overview cities table on a phone: the row and its Go-to arrow are 48 dp targets, bulk actions below");
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
        steps.Add(new Capture("trade", "Trade on a phone: both offer columns, the summary and Propose trade as a touch target"));
        steps.Add(new Act("open espionage", () => screen.OpenEspionageScreen()));
        steps.Add(new WaitFrames(5));
        steps.AddRange(Check("espionage screen", () => screen.Espionage));
        steps.Add(new Capture("espionage", "Espionage on a phone: the spy table with 48 dp Move and Coup buttons, Assign spies below"));
        return steps;
    }

    /// <summary>Scroll containers whose content is larger than they are on an axis they do not scroll: what lies beyond is unreachable for a finger.</summary>
    private static IEnumerable<string> Unscrollable(Node root)
    {
        foreach (var child in root.GetChildren())
        {
            if (child is ScrollContainer { Visible: true } scroll && scroll.IsVisibleInTree() && scroll.GetChildCount() > 0 && scroll.GetChild(0) is Control content)
            {
                var need = content.GetCombinedMinimumSize();
                if (scroll.VerticalScrollMode == ScrollContainer.ScrollMode.Disabled && need.Y > scroll.Size.Y + 1.5f) yield return $"{scroll.Name} needs {need.Y} high, has {scroll.Size.Y}";
                if (scroll.HorizontalScrollMode == ScrollContainer.ScrollMode.Disabled && need.X > scroll.Size.X + 1.5f) yield return $"{scroll.Name} needs {need.X} wide, has {scroll.Size.X}";
            }
            foreach (string nested in Unscrollable(child)) yield return nested;
        }
    }

    private static IEnumerable<BaseButton> Buttons(Node root)
    {
        foreach (var child in root.GetChildren())
        {
            if (child is BaseButton button) yield return button;
            foreach (var nested in Buttons(child)) yield return nested;
        }
    }
}
