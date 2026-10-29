using Godot;
using Unciv.GodotClient.Ui;

namespace Unciv.GodotHarness;

/// <summary>C066: the ui-kit steps of the shared frames: the tooltip's top inset and width, the destructive and stacked popups, and a screen in a ScreenFrame.</summary>
internal sealed class UiKitFrames(UiTheme theme, List<string> log)
{
    private ScreenFrame? frame;

    public IEnumerable<ScenarioStep> TooltipSteps(ScreenManager manager) =>
    [
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
    ];

    public IEnumerable<ScenarioStep> PopupSteps(ScreenManager manager) =>
    [
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
    ];

    public IEnumerable<ScenarioStep> FrameSteps(ScreenManager manager, Control rootScreen) =>
    [
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
    ];
}
