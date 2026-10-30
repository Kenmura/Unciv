using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Play;
using Unciv.GodotClient.Settings;
using Unciv.Harness;
using Unciv.Commands;
using Unciv.Models;

namespace Unciv.GodotHarness;

/// <summary>
/// T299: tutorial pop-ups and the floating task card can be removed for good, on a 400 dpi touch build. The Introduction popup and the task card both carry a "Never show
/// tutorials" button (and the card an X that only hides it for the session); pressing it saves Show tutorials = off in the settings file, closes whatever is showing, empties
/// the waiting queue, and no popup or card returns (not on a refresh, not for a new trigger, not for a restarted client). The settings file lives in the harness temp directory.
/// </summary>
public sealed class TutorialDismissScenario : Scenario
{
    private const float Dpi = 400f;

    public override string Name => "tutorial-dismiss";

    public override bool Uses3D => true;

    public override bool Solo => true;

    public override bool TutorialTasks => true;

    public override float TouchDpi => Dpi;

    public override IReadOnlyList<string> Checklist =>
    [
        "Is it clear that 'Never show tutorials' removes every tutorial hint for good, and is it easy to hit with a thumb?",
        "Is the X on the task card distinct from the permanent button?",
    ];

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session)
    {
        var game = session.Game;
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "unciv-tutorial-dismiss-scenario-" + System.Environment.ProcessId, "settings.json");
        if (File.Exists(path)) File.Delete(path);
        var service = new ClientSettingsService(new ClientSettingsStore(path));
        var localHost = new LocalHost(session, ScenarioGame.HumanA, () => 1000L);
        var screen = new GameScreen();
        host.AddChild(screen);
        screen.TaskCardEnabled = true;
        screen.Start(localHost, AssetRegistry.ForRuleset(game.Ruleset));
        screen.BindSettings(service);
        var card = screen.TaskCard;
        var popup = screen.Tutorials;
        int seenBefore = -1;
        int minTarget = UiTokens.DpToPx(UiTokens.TouchTargetDp, Dpi);

        bool Big(Button button) => button.Visible && button.GetGlobalRect().Size.Y + 0.5f >= minTarget;
        bool OffInFile() => !new ClientSettingsStore(path).Load().ShowTutorials;

        return
        [
            new WaitFrames(4),
            new Act("dismiss the start banner", screen.Hud.HideBanner),
            new AssertQuery("the Introduction popup and the task card are up", () => popup.Visible && card.Visible && card.EventName.Length > 0),
            new AssertQuery("every dismissal control is at least 48 dp tall", () => Big(popup.NeverButton) && Big(popup.OkButton) && Big(card.CloseButton) && Big(card.NeverButton)),
            new Capture("popup", "The Introduction tutorial with its 'Never show tutorials' button beside OK"),
            new Press("OK"),
            new WaitFrames(2),
            new AssertQuery("OK only closed this popup: tutorials stay on", () => !popup.Visible && service.Current.ShowTutorials && card.Visible),
            new Capture("card", "The floating task card: X in its corner and a 'Never show tutorials' button under the task"),
            new Click(() => card.CloseButton.GetGlobalRect().GetCenter()),
            new WaitFrames(2),
            new AssertQuery("X hid the card but did not change the setting", () => !card.Visible && card.Closed && service.Current.ShowTutorials),
            new Act("refresh the HUD", screen.Refresh),
            new WaitFrames(3),
            new AssertQuery("the card stays away for this game screen", () => !card.Visible),
            new Act("bring the card back and queue the Workers tutorial", () =>
            {
                card.ResetClosed();
                screen.Refresh();
                screen.ShowTutorial(TutorialTrigger.Workers);
            }),
            new WaitFrames(3),
            new AssertQuery("both are showing again", () => card.Visible && popup.Visible && popup.Current?.Trigger == TutorialTrigger.Workers),
            new Click(() => popup.NeverButton.GetGlobalRect().GetCenter()),
            new WaitFrames(3),
            new AssertQuery("Never show tutorials closed the popup and the card, and is saved in the settings and the file", () =>
                !popup.Visible && !card.Visible && !service.Current.ShowTutorials && OffInFile()),
            new Act("trigger more tutorials and refresh", () =>
            {
                seenBefore = service.Current.TutorialsSeen.Count;
                screen.ShowTutorial(TutorialTrigger.CultureAndPolicies);
                screen.OpenPolicyScreen();
                screen.Policies.Close();
                screen.Refresh();
            }),
            new WaitFrames(4),
            new AssertQuery("nothing comes back: no popup, no card, nothing marked seen", () => !popup.Visible && !card.Visible && service.Current.TutorialsSeen.Count == seenBefore),
            new AssertQuery("a restarted client reads Show tutorials off", () => !new ClientSettingsService(new ClientSettingsStore(path)).Current.ShowTutorials),
            new Capture("gone", "After Never show tutorials: the map without any tutorial popup or card"),
            new Act("switch tutorials back on in Settings and refresh", () =>
            {
                service.Update(s => s with { ShowTutorials = true });
                card.ResetClosed();
                screen.Refresh();
            }),
            new WaitFrames(3),
            new AssertQuery("the card is back", () => card.Visible),
            new Click(() => card.NeverButton.GetGlobalRect().GetCenter()),
            new WaitFrames(3),
            new AssertQuery("the card's own Never button turns the setting off and saves it", () =>
                !card.Visible && !service.Current.ShowTutorials && OffInFile()),
        ];
    }
}
