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
        list.Selected += index => selectedUnit = index;
        PopupSpec Popup(string title) => new(title, "Body of " + title, [new PopupChoice("OK " + title, () => log.Add("ok " + title)), new PopupChoice("Cancel " + title, () => log.Add("cancel " + title))]);

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
            new Capture("tooltip", "Tooltip beside the pointer after the delay"),
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
            new Capture("popup", "Popup A over the dimmed screen; B and C queued"),
            new Press("OK A"),
            new AssertQuery("B follows A", () => manager.Popups.Current?.Title == "B" && manager.Popups.PendingTitles.SequenceEqual(["C"]) && log.SequenceEqual(["ok A"])),
            new Press("Cancel B"),
            new AssertQuery("C follows B", () => manager.Popups.Current?.Title == "C" && log.SequenceEqual(["ok A", "cancel B"])),
            new PressKey(Key.Escape),
            new AssertQuery("Escape cancels the open popup and does not pop a screen", () => manager.Popups.Current == null && log.SequenceEqual(["ok A", "cancel B", "cancel C"]) && manager.Depth == 1),
            new Act("push a second screen", () => manager.Push(new Label { Text = "Second screen" })),
            new WaitFrames(2),
            new AssertQuery("second screen on top, first hidden", () => manager.Depth == 2 && !rootScreen.Visible),
            new Capture("pushed", "A second full-screen screen replaced the first"),
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
