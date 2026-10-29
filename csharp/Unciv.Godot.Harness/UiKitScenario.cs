using Godot;
using Unciv.Assets;
using Unciv.Commands;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Ui;
using TabBar = Unciv.GodotClient.Ui.TabBar;

namespace Unciv.GodotHarness;

/// <summary>
/// The UI kit (C005): one screen with a tab bar, a scrolling list, icon labels and a tooltip target; popups queue and close in order;
/// Escape closes a popup first and pops a screen second; the UI scale changes the font size. State assertions come before every capture.
/// </summary>
public sealed class UiKitScenario : Scenario
{
    public override string Name => "ui-kit";

    public override IReadOnlyList<string> Checklist =>
    [
        "Are panel, button, tab and list text legible against their backgrounds (contrast)?",
        "Is the selected tab visibly different from the others, and the selected list row from the rest?",
        "Do the popup and its dimmed backdrop clearly sit above the screen, and is the tooltip readable next to the pointer?",
        "Are the font sizes comfortable at 640x480, and does the 150% UI scale stay usable?",
    ];

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session)
    {
        var theme = new UiTheme(AssetRegistry.ForRuleset(session.Game.Ruleset));
        var manager = new ScreenManager(theme);
        host.AddChild(manager);
        var viewport = host.GetViewport();

        var tabs = new TabBar();
        var list = new ListPanel(theme, "Units", new Vector2(220, 300));
        foreach (string unit in new[] { "Warrior", "Settler", "Worker", "Archer" }) list.AddItem(unit, AssetKey.Of(RulesetAssetKeys.UnitIcon, unit));
        var stats = new VBoxContainer();
        var gold = new IconLabel(theme, AssetKey.Of(RulesetAssetKeys.StatIcon, "Gold"), "+12");
        stats.AddChild(gold);
        stats.AddChild(new IconLabel(theme, AssetKey.Of(RulesetAssetKeys.StatIcon, "Science"), "+5"));
        stats.AddChild(new IconLabel(theme, AssetKey.Of(RulesetAssetKeys.StatIcon, "Culture"), "+3"));
        // C063: nation emblems from the bundled pack on their colour badges, and one key no pack has (generated fallback)
        var badges = new HBoxContainer();
        var sampleBadges = new[] { ("Rome", new Color(0.55f, 0.1f, 0.12f)), ("Egypt", new Color(0.8f, 0.65f, 0.15f)), ("Greece", new Color(0.2f, 0.4f, 0.7f)), ("Atlantis", new Color(0.3f, 0.3f, 0.35f)) };
        foreach (var (nation, colour) in sampleBadges) badges.AddChild(new IconBadge(theme, AssetKey.Of(RulesetAssetKeys.NationIcon, nation), colour, Colors.White));
        stats.AddChild(badges);
        manager.Tooltips.Attach(gold, () => "[b]Gold[/b]\nIncome per turn: [color=yellow]+12[/color]");
        var tabsPage1 = new HBoxContainer();
        tabsPage1.AddChild(list);
        tabsPage1.AddChild(stats);
        tabs.AddTab("Overview", tabsPage1);
        tabs.AddTab("Cities", new Label { Text = "No cities yet" });
        tabs.Position = new Vector2(20, 20);
        var rootScreen = new Control();
        rootScreen.AddChild(tabs);
        manager.Push(rootScreen);

        var log = new List<string>();
        int selectedUnit = -1;
        ScreenFrame? frame = null;
        list.Selected += index => selectedUnit = index;
        PopupSpec Popup(string title) => new(title, "Body of " + title, [new PopupChoice("OK " + title, () => log.Add("ok " + title), ChoiceRole.Primary), new PopupChoice("Cancel " + title, () => log.Add("cancel " + title))]);

        // C065: the shared components in every state, built once per page (a control has one parent, so the wide capture builds them again)
        var gallery = new UiKitGallery(theme, log);

        return
        [
            new WaitFrames(2),
            new AssertQuery("one screen on the stack, first tab selected", () => manager.Depth == 1 && manager.Top == rootScreen && tabs.SelectedIndex == 0 && tabs.SelectedPage == tabsPage1),
            new AssertQuery("stat icons and badges come from the pack, an unknown nation falls back", () =>
                new[] { "Gold", "Science", "Culture" }.All(n => theme.Registry.Resolve(AssetKey.Of(RulesetAssetKeys.StatIcon, n)).Source == AssetSourceKind.Pack)
                && theme.Registry.Resolve(AssetKey.Of(RulesetAssetKeys.NationIcon, "Rome")).Source == AssetSourceKind.Pack
                && theme.Registry.Resolve(AssetKey.Of(RulesetAssetKeys.NationIcon, "Atlantis")).Source == AssetSourceKind.Procedural
                && badges.GetChildCount() == 4),
            new Capture("screen", "Tab bar, scrolling list with icons, icon labels"),
            new Press("Cities"),
            new AssertQuery("second tab selected, first page hidden", () => tabs.SelectedIndex == 1 && !tabsPage1.Visible),
            new Press("Overview"),
            new AssertQuery("back on the first tab", () => tabs.SelectedIndex == 0 && tabsPage1.Visible),
            new Press("Archer"),
            new AssertQuery("list selection reports the item index", () => selectedUnit == 3 && list.SelectedIndex == 3),
            new Act("pointer onto the Gold label", () => InputSimulator.SimulateMouseMove(viewport, gold.GetGlobalRect().GetCenter())),
            new WaitFrames(2),
            new AssertQuery("tooltip waits for its delay", () => manager.Tooltips.Hovered == gold && !manager.Tooltips.IsShowing),
            new Act("let the delay elapse", () => manager.Tooltips.Advance(ScenarioRunner.UnreachableDelay)),
            new WaitFrames(2),
            new AssertQuery("tooltip shows the rich text next to the pointer", () => manager.Tooltips.IsShowing && manager.Tooltips.ShownText.Contains("Gold", StringComparison.Ordinal)),
            new Capture("tooltip", "Tooltip beside the pointer after the delay: raised panel with the shadow, widest line decides the width"),
            new Act("a top bar 120 px tall and a long tip", () =>
            {
                manager.Tooltips.TopInset = () => 120f;
                manager.Tooltips.Point(() => string.Join(' ', Enumerable.Repeat("A very long line of tooltip text that has to wrap.", 8)));
                manager.Tooltips.Advance(ScenarioRunner.UnreachableDelay);
            }),
            new WaitFrames(3),
            new AssertQuery("a long tip wraps at the maximum width and is never placed above the top inset", () =>
                manager.Tooltips.IsShowing && manager.Tooltips.PanelRect.Size.X <= theme.Px(TooltipHost.MaxWidth) + 1 && manager.Tooltips.PanelRect.Size.X > theme.Px(TooltipHost.MaxWidth) / 2f
                && manager.Tooltips.PanelRect.Position.Y >= 120f),
            new Act("back to no inset", () =>
            {
                manager.Tooltips.Unpoint();
                manager.Tooltips.TopInset = null;
            }),
            new Act("pointer leaves", () => InputSimulator.SimulateMouseMove(viewport, new Vector2(600, 440))),
            new WaitFrames(2),
            new AssertQuery("tooltip hidden on leave", () => !manager.Tooltips.IsShowing && manager.Tooltips.Hovered == null),
            new Act("three popups at once", () =>
            {
                manager.Popups.Show(Popup("A"));
                manager.Popups.Show(Popup("B"));
                manager.Popups.Show(Popup("C"));
            }),
            new WaitFrames(2),
            new AssertQuery("first popup shown, the others queued in order", () => manager.Popups.Current?.Title == "A" && manager.Popups.PendingTitles.SequenceEqual(["B", "C"])),
            new AssertQuery("the popup is the shared modal frame and lays its choices out secondary left, primary right", () =>
                manager.Popups.View is { } view && ModalChecks.Framed(view) && !view.Stacked && view.Actions.Select(it => it.Text).SequenceEqual(["Cancel A", "OK A"])),
            new Capture("popup", "Popup A over the scrim with the brass hairline on its top edge: Cancel (secondary) left of OK (primary); B and C queued"),
            new Press("OK A"),
            new WaitFrames(2),
            new AssertQuery("B follows A", () => manager.Popups.Current?.Title == "B" && manager.Popups.PendingTitles.SequenceEqual(["C"]) && log.SequenceEqual(["ok A"])),
            new Press("Cancel B"),
            new WaitFrames(2),
            new AssertQuery("C follows B", () => manager.Popups.Current?.Title == "C" && log.SequenceEqual(["ok A", "cancel B"])),
            new PressKey(Key.Escape),
            new AssertQuery("Escape cancels the open popup and does not pop a screen", () => manager.Popups.Current == null && log.SequenceEqual(["ok A", "cancel B", "cancel C"]) && manager.Depth == 1),
            new Act("a destructive popup and a four-choice popup", () =>
            {
                log.Clear();
                manager.Popups.Show(new PopupSpec("Declare war on Rome?", "Your units will be at war with Rome and its allies, and your trade deals with them end.",
                    [new PopupChoice("Cancel", () => log.Add("cancel war")), new PopupChoice("Declare war", () => log.Add("war"), ChoiceRole.Danger)], CancelIndex: 0));
                manager.Popups.Show(new PopupSpec("Four ways", "More than three choices stack in a column.",
                    [new PopupChoice("One", null, ChoiceRole.Primary), new PopupChoice("Two"), new PopupChoice("Three"), new PopupChoice("Four", null, ChoiceRole.Ghost)]));
            }),
            new WaitFrames(2),
            new AssertQuery("the danger popup lays Cancel left of the red Declare war", () =>
                manager.Popups.View is { } view && ModalChecks.Framed(view) && view.Actions.Select(it => it.Text).SequenceEqual(["Cancel", "Declare war"])
                && view.Actions[1].ThemeTypeVariation == UiVariants.ButtonDanger && view.Actions[0].ThemeTypeVariation == UiVariants.ButtonSecondary),
            new Capture("popup-danger", "A destructive confirmation: the consequence line, Cancel (secondary) on the left and Declare war (danger red) on the right"),
            new PressKey(Key.Escape),
            new WaitFrames(2),
            new AssertQuery("Escape chose the declared cancel choice, and the four-choice popup is next, stacked in the given order", () =>
                log.SequenceEqual(["cancel war"]) && manager.Popups.View is { } view && ModalChecks.Framed(view) && view.Stacked && view.Actions.Select(it => it.Text).SequenceEqual(["One", "Two", "Three", "Four"])),
            new PressKey(Key.Escape),
            new WaitFrames(2),
            new AssertQuery("its last choice was the cancel by default", () => manager.Popups.Current == null && manager.Depth == 1),
            new Act("push a second screen in a ScreenFrame", () =>
            {
                frame = new ScreenFrame(theme, "Second screen");
                frame.SetContent(new Label { Text = "Second screen" });
                frame.SetFooter([(new Button { Text = "Apply" }, ChoiceRole.Primary), (new Button { Text = "Reset" }, ChoiceRole.Ghost)]);
                frame.CloseRequested += () => log.Add("frame closed");
                manager.Push(frame);
            }),
            new WaitFrames(2),
            new AssertQuery("second screen on top, first hidden", () => manager.Depth == 2 && !rootScreen.Visible),
            new AssertQuery("the frame has its hairline on the header, the close button at the right end and the footer actions right-aligned with the primary last", () =>
                frame is { } f && f.TitleText == "Second screen" && f.TitleLabel.Size.X > 80 && f.TitleLabel.Size.Y > 10 && f.Header.LineRect.Size.X > 0 && f.Header.LineColour == Godot.Color.FromHtml("#D6AA3C")
                && f.CloseButton.GetGlobalRect().End.X >= f.Header.GetGlobalRect().End.X - 24 && f.CloseButton.Label == "Close" && f.CloseButton.TooltipText == "Close (Esc)"
                && f.FooterActions.Select(it => it.Text).SequenceEqual(["Reset", "Apply"]) && f.Footer.Visible && f.FooterActions[1].GetGlobalRect().End.X > f.FooterActions[0].GetGlobalRect().End.X
                && f.ContentRect.Position.Y >= f.Header.GetGlobalRect().End.Y && f.ContentRect.End.Y <= f.Footer.GetGlobalRect().Position.Y + 0.5f),
            new Capture("pushed", "A second full-screen screen in a ScreenFrame: header bar with the brass hairline, the title and a close button, content below, and a footer with Reset (ghost) and Apply (primary, right-most)"),
            new Press("Close"),
            new AssertQuery("the close button raises CloseRequested and the screen decides what it means", () => log.SequenceEqual(["cancel war", "frame closed"]) && manager.Depth == 2),
            new PressKey(Key.Escape),
            new WaitFrames(2),
            new AssertQuery("Escape popped the screen", () => manager.Depth == 1 && rootScreen.Visible),
            new PressKey(Key.Escape),
            new AssertQuery("the bottom screen never pops", () => manager.Depth == 1),
            new Act("UI scale 150%", () => theme.SetScale(1.5f)),
            new WaitFrames(2),
            new AssertQuery("font size follows the scale", () => theme.Theme.DefaultFontSize == 23 && theme.Px(10) == 15),
            new Capture("scaled", "The same screen at 150% UI scale"),
            .. gallery.Steps(manager, host),
        ];
    }
}
