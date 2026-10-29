using Godot;
using Unciv.Assets;
using Unciv.Commands;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Map;
using Unciv.GodotClient.Play;
using Unciv.Harness;
using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Utils;

namespace Unciv.GodotHarness;

/// <summary>
/// C013: notifications and popup alerts through the real game screen and the real engine, in the alerts game (the diplomacy game plus a founded capital, four injected
/// notifications and pending alerts). The alerts arrive one popup at a time and each button sends the command the engine's pending-decision list names: first contact
/// is acknowledged, Rome's demand is accepted (respond-demand), the welcome alert is reopened through the "Review alerts" button after the friendship offer was put off
/// with "Decide later" (nothing sent, still pending), the friendship offer is accepted (respond-friendship), the just-conquered city has no way to defer and its
/// choices are the engine's (respond-to-alert; End turn stays hidden while it is open), the wonder is acknowledged. Then the notification list: it shows every
/// notification the engine holds for the viewer, newest first; a tile the viewer cannot see has no jump target (nothing leaked), a city row centres the camera on the
/// city, the tech row opens the tech screen.
/// </summary>
public sealed class AlertsScenario : Scenario
{
    public override string Name => "alerts";

    public override bool Uses3D => true;

    public override bool Alerts => true;

    public override IReadOnlyList<string> Checklist =>
    [
        "Does a popup get your attention without hiding what you need to decide it?",
        "Is it clear which buttons answer the alert and which only close it?",
        "Can you tell the notifications apart at a glance, and see which ones take you somewhere when clicked?",
        "After a click on a notification, is it obvious where the camera went?",
    ];

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session)
    {
        var game = session.Game;
        var human = game.GetCivilization(ScenarioGame.HumanA);
        var rome = game.GetCivilization(ScenarioGame.Ai);
        var recording = new CityAScenario.RecordingSession(new LocalHost(session, ScenarioGame.HumanA, () => 1000L));
        var screen = new GameScreen();
        host.AddChild(screen);
        screen.Start(recording, AssetRegistry.ForRuleset(game.Ruleset));
        int logged = 0;

        Vector2 Centre(Button? button) => button?.GetGlobalRect().GetCenter() ?? Vector2.Zero;
        Vector2 Answer(string id) => Centre(screen.Alerts.ButtonById(id));
        bool Showing(string title) => screen.Alerts.Visible && screen.Alerts.TitleText == title;
        bool Pending(AlertType type) => human.PopupAlerts.Any(it => it.Type == type);
        var city = human.Cities[0];
        var cityTile = city.GetCenterTile();
        int IndexOf(string start) => screen.Notifications.Rows.First(it => it.Text.StartsWith(start, StringComparison.Ordinal)).Index;
        Vector2 Row(string start) => Centre(screen.Notifications.RowButton(IndexOf(start)));
        void Show(string start) => screen.Notifications.EnsureVisible(IndexOf(start));

        return
        [
            new WaitFrames(3),
            new Act("dismiss the start banner", () => screen.Hud.HideBanner()),
            new AssertQuery("only the alerts that ask for a decision interrupt: Rome's demand is on screen, the welcome and first contacts wait under Review alerts", () =>
                Showing("A demand from Rome") && Pending(AlertType.StartIntro) && screen.Notifications.ReviewButton.Visible && screen.Notifications.ReviewButton.Text == "Review alerts (3)"),
            new AssertQuery("the demand offers accept, refuse and decide later", () =>
                screen.Alerts.BodyText == "Please don't spread your religion to us." && screen.Alerts.ButtonIds.SequenceEqual(["Accept", "Refuse", "DecideLater"])),
            new AssertQuery("the popup is the shared modal frame: centred, accent hairline, titled, buttons inside with the primary right-most", () => ModalChecks.Framed(screen.Alerts)),
            new Capture("demand", "Rome's demand as a popup: accept, refuse or decide later; the informational alerts wait under Review alerts"),
            new Act("remember the command log", () => logged = session.Log.Count),
            new Click(() => Answer("Accept")),
            new WaitFrames(2),
            new AssertQuery("accepting sent respond-demand with the demand's name and the engine removed the alert", () =>
                session.Log.Count == logged + 1 && session.Log[^1].Type == "respond-demand" && session.Log[^1].Payload.ToString().Contains("DoNotSpreadReligion", StringComparison.Ordinal)
                && !Pending(AlertType.DemandToStopSpreadingReligion)),

            new AssertQuery("the friendship offer is shown", () => Showing("Declaration of Friendship") && screen.Alerts.ButtonIds.SequenceEqual(["Accept", "Decline", "DecideLater"])),
            new Act("remember the command log", () => logged = session.Log.Count),
            new PressKey(Key.Escape),
            new WaitFrames(2),
            new AssertQuery("Escape decides later: it sent nothing, the offer is still pending, and the next alert (the conquered city) took its place", () =>
                session.Log.Count == logged && Pending(AlertType.DeclarationOfFriendship) && Showing("City conquered")),
            new AssertQuery("the conquered city's alert cannot be put off and offers exactly the engine's choices; End turn is not offered", () =>
                screen.Alerts.ButtonIds.SequenceEqual(["Choice_annex", "Choice_puppet"]) && !screen.Hud.EndTurnShown),
            new AssertQuery("the popup is the shared modal frame: centred, accent hairline, titled, buttons inside with the primary right-most", () => ModalChecks.Framed(screen.Alerts)),
            new Capture("city-conquered", "A just-conquered city: only the engine's choices, no way to put it off"),
            new PressKey(Key.Escape),
            new AssertQuery("Escape does nothing to an alert that must be answered, and the game menu does not open behind it", () => Showing("City conquered") && !screen.GameMenu.Visible),
            new Click(() => Answer("Choice_puppet")),
            new WaitFrames(2),
            new AssertQuery("the choice sent respond-to-alert and the engine made the city a puppet", () =>
                session.Log[^1].Type == "respond-to-alert" && session.Log[^1].Payload.ToString().Contains("puppet", StringComparison.Ordinal) && city.IsPuppet && !Pending(AlertType.CityConquered)),
            new AssertQuery("no popup is left; the put-off offer and the informational alerts wait", () =>
                !screen.Alerts.Visible && Pending(AlertType.DeclarationOfFriendship) && screen.Notifications.ReviewButton.Visible),

            new Click(() => Centre(screen.Notifications.ReviewButton)),
            new WaitFrames(2),
            new AssertQuery("Review alerts opens every alert in the engine's order, starting with the welcome", () => Showing("Welcome") && screen.Alerts.ButtonIds.SequenceEqual(["Ok"])),
            new Capture("welcome", "Review alerts: the welcome alert, informational, with only an OK"),
            new Act("answer the alerts in order until the put-off friendship offer", () =>
            {
                while (screen.Alerts.Visible && screen.Alerts.Current.NotNull().Type != AlertType.DeclarationOfFriendship) screen.Alerts.ButtonById("Ok").NotNull().EmitSignal(BaseButton.SignalName.Pressed);
            }),
            new AssertQuery("the review reached the put-off friendship offer, which every earlier OK sent as ack-alert", () =>
                Showing("Declaration of Friendship") && session.Log.Skip(logged).Count(it => it.Type == "ack-alert") >= 2),
            new Capture("friendship", "The friendship offer, back in the review"),
            new Click(() => Answer("Accept")),
            new WaitFrames(2),
            new AssertQuery("accepting sent respond-friendship and the engine signed the declaration", () =>
                session.Log[^1].Type == "respond-friendship" && human.GetDiplomacyManager(rome).NotNull().HasFlag(DiplomacyFlags.DeclarationOfFriendship)),
            new Act("acknowledge the rest", () =>
            {
                while (screen.Alerts.Visible) screen.Alerts.ButtonById("Ok").NotNull().EmitSignal(BaseButton.SignalName.Pressed);
            }),
            new AssertQuery("every alert is answered: no popup, nothing pending, End turn offered, no review button", () =>
                !screen.Alerts.Visible && human.PopupAlerts.Count == 0 && !screen.Notifications.ReviewButton.Visible && screen.Hud.ActionText != "Review alert"),

            new AssertQuery("the list holds exactly the engine's notifications for the viewer, newest first, and shows no count of other civs' news", () =>
                screen.Notifications.Visible && screen.Notifications.Rows.Count == human.Notifications.Count && human.Notifications.Count >= 4
                && screen.Notifications.Rows.Select(it => it.Index).SequenceEqual(Enumerable.Range(0, human.Notifications.Count).Reverse())
                && screen.Notifications.ToggleText == $"Notifications ({human.Notifications.Count})"),
            new Click(() => Centre(screen.Notifications.ToggleButton)),
            new WaitFrames(2),
            new AssertQuery("opened: the summary counts the categories and every row has a button", () =>
                screen.Notifications.IsOpen && screen.Notifications.SummaryText.StartsWith("Turn 0: ", StringComparison.Ordinal)
                && screen.Notifications.SummaryText.Contains("1 War", StringComparison.Ordinal) && screen.Notifications.Rows.All(it => screen.Notifications.RowButton(it.Index) != null)),
            new AssertQuery("the scout seen on a tile the viewer cannot see has no jump target; the city and tech rows do", () =>
                screen.Notifications.Rows.Single(it => it.Text.StartsWith("An enemy scout", StringComparison.Ordinal)).Target == null
                && screen.Notifications.Rows.Single(it => it.Text.StartsWith("Your city", StringComparison.Ordinal)).Target is { Kind: NotificationTargetKind.Tile } tile
                && tile.X == cityTile.Position.x && tile.Y == cityTile.Position.y
                && screen.Notifications.Rows.Single(it => it.Text.StartsWith("You have discovered", StringComparison.Ordinal)).Target is { Kind: NotificationTargetKind.Tech, Name: "Agriculture" }),
            new Act("move the camera away", () => screen.Map.FocusOn(MapLayout.TileTop(cityTile.Position.x + 6, cityTile.Position.y + 6))),
            new Act("remember the command log", () => logged = session.Log.Count),
            new Act("scroll the row into view", () => Show("An enemy scout")),
            new WaitFrames(2),
            new Click(() => Row("An enemy scout")),
            new WaitFrames(2),
            new AssertQuery("the row without a target moved nothing", () => MathF.Abs(screen.Map.Focus.X - MapLayout.TileTop(cityTile.Position.x + 6, cityTile.Position.y + 6).X) < 0.01f),
            new Act("scroll the row into view", () => Show("Your city")),
            new WaitFrames(2),
            new Click(() => Row("Your city")),
            new WaitFrames(2),
            new AssertQuery("the city row centred the camera on the city's tile", () =>
                MathF.Abs(screen.Map.Focus.X - MapLayout.TileTop(cityTile.Position.x, cityTile.Position.y).X) < 0.01f
                && MathF.Abs(screen.Map.Focus.Z - MapLayout.TileTop(cityTile.Position.x, cityTile.Position.y).Z) < 0.01f),
            new Capture("list", "The notification list open; clicking the city row centred the camera on the city"),
            new Act("scroll the row into view", () => Show("You have discovered")),
            new WaitFrames(2),
            new Click(() => Row("You have discovered")),
            new WaitFrames(3),
            new AssertQuery("the tech row opened the tech screen, and clicking notifications sent no command", () => screen.IsTechVisible && session.Log.Count == logged),
            new Press("Close technology"),
            new AssertQuery("closed again", () => !screen.IsTechVisible),
        ];
    }
}
