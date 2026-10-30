using Godot;
using Unciv.Assets;
using Unciv.Commands;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Play;
using Unciv.Harness;

namespace Unciv.GodotHarness;

/// <summary>
/// T285: the Civilopedia as a phone shows it: a 2400x1080 window at 400 ppi (scale 2.5, 48 dp = 120 px targets). A category page, an entry page (header card, headings, links),
/// and the search results are each asserted to have only touch-sized buttons, no label or button text that leaves the window, is cut off or overlaps another (the first screenful: what is scrolled out of view is not judged), and no content behind a
/// container that cannot scroll. Real-device behaviour is not covered.
/// </summary>
public sealed class PhonePediaScenario : Scenario
{
    private const float Dpi = 400f;

    public override string Name => "phone-pedia";

    public override bool Uses3D => true;

    public override Vector2I Viewport => new(2400, 1080);

    public override float TouchDpi => Dpi;

    public override IReadOnlyList<string> Checklist =>
    [
        "On the phone, can the Civilopedia's categories, entry list, search results and page text be read and scrolled with a finger, and is every button big enough for a thumb?",
    ];

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session)
    {
        var game = session.Game;
        var screen = new GameScreen();
        host.AddChild(screen);
        screen.Start(new LocalHost(session, ScenarioGame.HumanA, () => 1000L), AssetRegistry.ForRuleset(game.Ruleset));
        int minTarget = UiTokens.DpToPx(UiTokens.TouchTargetDp, Dpi);

        ScenarioStep[] Check(string what, Func<Node> node) =>
        [
            LayoutChecks.Assert($"on a phone no {what} text is cut off, leaves the window or overlaps another", host, node),
            LayoutChecks.Touchable("phone-pedia", what, minTarget, node),
            LayoutChecks.Reachable("phone-pedia", what, node),
        ];

        var steps = new List<ScenarioStep>
        {
            new WaitFrames(6),
            new Act("dismiss the start banner", () => screen.Hud.HideBanner()),
            new AssertQuery("the theme derived scale 2.5 and 120 px targets", () => screen.SharedTheme is { } theme && theme.Scale == 2.5f && theme.MinTargetPx == minTarget && minTarget == 120),
            new Act("open the Civilopedia on its first category", () => screen.OpenPediaScreen()),
            new WaitFrames(5),
        };
        steps.AddRange(Check("Civilopedia category page", () => screen.Pedia));
        steps.Add(new Capture("category", "Civilopedia on a 2400x1080 400 ppi phone: category icons (each a 48 dp target), entry list and the first entry's page side by side; the page scrolls"));
        steps.Add(new Act("open the Archer unit page", () => screen.OpenPediaScreen("Unit/Archer")));
        steps.Add(new WaitFrames(5));
        steps.AddRange(Check("Civilopedia unit page", () => screen.Pedia));
        steps.Add(new Capture("unit", "Civilopedia unit page on a phone: header card with cost chips, section headings and linked text"));
        steps.Add(new Act("search for 'ar'", () => screen.Pedia.SearchFor("ar")));
        steps.Add(new WaitFrames(5));
        steps.AddRange(Check("Civilopedia search results", () => screen.Pedia));
        steps.Add(new Capture("search", "Civilopedia search on a phone: results list with 48 dp rows over the whole pedia, and a 48 dp Clear button beside the box"));
        return steps;
    }
}
