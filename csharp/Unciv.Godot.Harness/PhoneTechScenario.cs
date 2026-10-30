using Godot;
using Unciv.Assets;
using Unciv.Commands;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Play;
using Unciv.Harness;
using Unciv.Utils;

namespace Unciv.GodotHarness;

/// <summary>
/// T283: the tech tree and the policy screen as a phone shows them: a 2400x1080 window at 400 ppi (scale 2.5, 48 dp = 120 px targets), in the policy game (culture stored, a pick offered).
/// Each screen is asserted to have only touch-sized buttons (the tech node cards and policy nodes are the tree's own cells and are judged by a lower bound instead), no text that leaves the window,
/// is cut off or overlaps another, and no content behind a container that cannot scroll. Real-device behaviour (finger scrolling, pinch, rotation) is not covered.
/// </summary>
public sealed class PhoneTechScenario : Scenario
{
    private const float Dpi = 400f;

    public override string Name => "phone-tech";

    public override bool Uses3D => true;

    public override bool Policies => true;

    public override Vector2I Viewport => new(2400, 1080);

    public override float TouchDpi => Dpi;

    public override IReadOnlyList<string> Checklist =>
    [
        "On the phone, can the tech tree and the policy branches be moved around with a finger, and are the node cards and the buttons big enough to hit?",
    ];

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session)
    {
        var game = session.Game;
        var screen = new GameScreen();
        host.AddChild(screen);
        screen.Start(new LocalHost(session, ScenarioGame.HumanA, () => 1000L), AssetRegistry.ForRuleset(game.Ruleset));
        bool dragged = false, selectionKept = false;
        int minTarget = UiTokens.DpToPx(UiTokens.TouchTargetDp, Dpi);

        ScenarioStep[] Check(string what, Func<Node> node, Func<BaseButton, bool> exempt) =>
        [
            LayoutChecks.Assert($"on a phone no {what} text is cut off, leaves the window or overlaps another", host, node),
            LayoutChecks.Touchable("phone-tech", what, minTarget, node, exempt),
            LayoutChecks.Reachable("phone-tech", what, node),
        ];

        return
        [
            new WaitFrames(6),
            new Act("dismiss the start banner and open the tech screen", () =>
            {
                screen.Hud.HideBanner();
                screen.OpenTechScreen();
            }),
            new WaitFrames(8),
            new AssertQuery("the theme derived scale 2.5 and 120 px targets, and the tree is open", () => screen.SharedTheme is { } theme && theme.Scale == 2.5f && theme.MinTargetPx == minTarget && minTarget == 120 && screen.IsTechVisible && screen.Tech.Tree != null),
            .. Check("tech screen", () => screen.Tech, it => it is TechNodeCard),
            new AssertQuery("every tech node card is at least 48 dp (120 px) tall", () => screen.Tech.Layout.Nodes.All(it => screen.Tech.NodeButton(it.Name) is { Size.Y: >= 120f })),
            new Capture("tech", "Tech tree on a 2400x1080 400 ppi phone: 48 dp node cards, the era band on top, the detail panel and 48 dp Research / Add to queue buttons"),
            new Act("drag a tech card: it pans and the release does not select it", () =>
            {
                var card = screen.Tech.NodeButton("Mining").NotNull();
                string? before = screen.Tech.SelectedTech;
                card._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
                card._GuiInput(new InputEventMouseMotion { ButtonMask = MouseButtonMask.Left, Relative = new Vector2(60f, 0f) });
                dragged = card.Dragged;
                card.EmitSignal(BaseButton.SignalName.Pressed);
                selectionKept = screen.Tech.SelectedTech == before && !card.Dragged;
            }),
            new AssertQuery("the drag was a pan (not a selection) and the flag was cleared", () => dragged && selectionKept),
            new Act("choose Pottery, a researchable tech, so the footer actions are enabled", () => screen.Tech.Select("Pottery")),
            new WaitFrames(4),
            new AssertQuery("the footer offers an enabled Research button", () => screen.Tech.ActionButton("Research") is { Disabled: false }),
            .. Check("tech screen with a researchable tech chosen", () => screen.Tech, it => it is TechNodeCard),
            new Capture("tech-chosen", "Tech tree on a phone with Pottery chosen: Research now and Add to queue are enabled 48 dp buttons in the footer"),
            new Act("open the policy screen", () => screen.OpenPolicyScreen()),
            new WaitFrames(8),
            new AssertQuery("the policy screen is open", () => screen.IsPolicyScreenVisible && screen.Policies.Tree != null),
            .. Check("policy screen", () => screen.Policies, it => it is PolicyNodeCard),
            new AssertQuery("every policy node is at least 48 dp (120 px) tall", () => screen.Policies.Branches.SelectMany(it => it.Cells).All(it => screen.Policies.PolicyButton(it.Name) is not PolicyNodeCard card || card.Size.Y >= 120f)),
            new Capture("policy", "Policy screen on a phone: culture bar, branch tabs, the Tradition card with its nodes, an empty detail panel"),
            new Act("scroll the Tradition card's Complete strip into view", () => screen.Policies.EnsureVisible("Tradition Complete")),
            new WaitFrames(3),
            new AssertQuery("the Complete strip is inside the visible part of its scroller and 120 px tall", () => screen.Policies.PolicyButton("Tradition Complete") is { } strip && strip.Size.Y >= 120f && ScrollerRect(strip).Encloses(strip.GetGlobalRect())),
            new Capture("policy-complete", "Policy screen on a phone scrolled to the Tradition Complete strip: a full 48 dp tall bar under the nodes"),
            .. Check("policy screen scrolled to the Complete strip", () => screen.Policies, it => it is PolicyNodeCard),
            new Act("choose the Tradition opener and scroll back", () => { screen.Policies.Select("Tradition"); screen.Policies.EnsureVisible("Tradition"); }),
            new WaitFrames(4),
            new AssertQuery("Adopt is offered", () => screen.Policies.AdoptButton is { Visible: true, Disabled: false }),
            .. Check("policy screen with a policy chosen", () => screen.Policies, it => it is PolicyNodeCard),
            new Capture("policy-chosen", "Policy screen on a phone with Tradition chosen: effects and the Adopt button in the detail panel"),
        ];
    }

    private static Rect2 ScrollerRect(Control control)
    {
        for (var parent = control.GetParent(); parent != null; parent = parent.GetParent())
            if (parent is ScrollContainer scroll) return scroll.GetGlobalRect();
        return new Rect2();
    }
}
