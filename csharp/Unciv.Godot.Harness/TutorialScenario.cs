using Godot;
using Unciv.Commands;
using Unciv.Commands.Turn;
using Unciv.Commands.Units;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Play;
using Unciv.GodotClient.Settings;
using Unciv.Harness;
using Unciv.Logic.Civilization;
using Unciv.Models;
using Unciv.Utils;

namespace Unciv.GodotHarness;

/// <summary>
/// C026: tutorial popups on the real game screen. The Introduction tutorial pops up when the game starts; OK closes it and the "seen" flag is saved in the client settings
/// file (and read back by a restarted client), not in the game. Opening the policy screen queues the three-step Culture and Policies tutorial: Space moves through the
/// steps and only the last one marks it seen. Seen tutorials do not return, ending the turn queues Slow Start, and with tutorials switched off nothing pops up. The
/// settings file lives in the harness temp directory, not in the player's user:// folder.
/// </summary>
public sealed class TutorialScenario : Scenario
{
    public override string Name => "tutorial";

    public override bool Uses3D => true;

    public override bool Solo => true;

    public override IReadOnlyList<string> Checklist =>
    [
        "Is the tutorial wording clear and short enough to read at a glance?",
        "Is it obvious how to continue (OK, Space) and that more steps follow?",
    ];

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session)
    {
        var game = session.Game;
        var human = game.GetCivilization(ScenarioGame.HumanA);
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "unciv-tutorial-scenario", "settings.json");
        if (File.Exists(path)) File.Delete(path);
        var service = new ClientSettingsService(new ClientSettingsStore(path));
        var localHost = new LocalHost(session, ScenarioGame.HumanA, () => 1000L);
        var screen = new GameScreen();
        host.AddChild(screen);
        screen.Start(localHost, AssetRegistry.ForRuleset(game.Ruleset));
        screen.BindSettings(service);
        int startTurn = game.Turns;
        var tutorials = screen.Tutorials;
        ClientSettingsService? restarted = null;

        bool SeenInFile(string key) => new ClientSettingsStore(path).Load().TutorialsSeen.Contains(key);

        return
        [
            new WaitFrames(4),
            new AssertQuery("the Introduction tutorial pops up at game start", () => tutorials.Visible && tutorials.Current?.Trigger == TutorialTrigger.Introduction && tutorials.TitleText == "Introduction"),
            new AssertQuery("it shows the ruleset's text and nothing is marked seen yet", () => tutorials.BodyText.StartsWith("Welcome to Unciv!", StringComparison.Ordinal) && service.Current.TutorialsSeen.Count == 0 && tutorials.CounterText == ""),
            new Capture("introduction", "Game start: the Introduction tutorial over the map with its OK button"),
            new Press("OK"),
            new WaitFrames(2),
            new AssertQuery("OK closed it and saved the seen flag in the settings and the file", () => !tutorials.Visible && service.Current.TutorialsSeen.SequenceEqual(["Introduction"]) && SeenInFile("Introduction")),
            new Act("restart the client: a new service reads the file", () => restarted = new ClientSettingsService(new ClientSettingsStore(path))),
            new AssertQuery("the flag persisted and the game itself is untouched", () => restarted.NotNull().Current.TutorialsSeen.Contains("Introduction") && game.Turns == startTurn),

            new Act("open the policy screen", () => screen.OpenPolicyScreen()),
            new WaitFrames(3),
            new AssertQuery("Culture and Policies is shown at step 1 of 3", () => tutorials.Visible && tutorials.Current?.Trigger == TutorialTrigger.CultureAndPolicies && tutorials.Current.Step == 0 && tutorials.CounterText == "1 / 3"),
            new PressKey(Key.Space),
            new WaitFrames(2),
            new AssertQuery("Space went to step 2 and nothing is marked seen yet", () => tutorials.Visible && tutorials.Current?.Step == 1 && tutorials.CounterText == "2 / 3" && !service.Current.TutorialsSeen.Contains("CultureAndPolicies")),
            new Capture("second-step", "Second of three steps of Culture and Policies: step counter 2 / 3 above OK"),
            new PressKey(Key.Space),
            new WaitFrames(2),
            new PressKey(Key.Space),
            new WaitFrames(2),
            new AssertQuery("after the last step it is gone and seen", () => !tutorials.Visible && SeenInFile("CultureAndPolicies")),
            new Act("close the policy screen and open it again", () =>
            {
                screen.Policies.Close();
                screen.OpenPolicyScreen();
            }),
            new WaitFrames(3),
            new AssertQuery("a seen tutorial does not come back", () => !tutorials.Visible),
            new Act("close the policy screen", () => screen.Policies.Close()),

            new Act("order every unit, acknowledge the start alert and end the turn", () =>
            {
                foreach (var unit in human.Units.GetCivUnits().OrderBy(it => it.Id))
                    localHost.SubmitAsync(new SetUnitDue { UnitId = unit.Id, Due = false }).GetAwaiter().GetResult();
                localHost.SubmitAsync(new AckAlert { Index = 0, AlertType = AlertType.StartIntro }).GetAwaiter().GetResult();
            }),
            new WaitFrames(1),
            new Press("Next turn"),
            new WaitFrames(4),
            new AssertQuery("the new turn queued Slow Start", () => game.Turns == startTurn + 1 && tutorials.Visible && tutorials.Current?.Trigger == TutorialTrigger.SlowStart),
            new Press("OK"),
            new WaitFrames(2),
            new AssertQuery("Slow Start is seen too", () => !tutorials.Visible && SeenInFile("SlowStart")),

            new Act("switch tutorials off and trigger one", () =>
            {
                service.Update(s => s with { ShowTutorials = false });
                screen.ShowTutorial(TutorialTrigger.Workers);
            }),
            new WaitFrames(2),
            new AssertQuery("with tutorials off nothing pops up and nothing is marked", () => !tutorials.Visible && !service.Current.TutorialsSeen.Contains("Workers")),
            new Act("switch them on, forget what was seen and trigger Workers", () =>
            {
                service.Update(s => s with { ShowTutorials = true, TutorialsSeen = [] });
                screen.ShowTutorial(TutorialTrigger.Workers);
            }),
            new WaitFrames(2),
            new AssertQuery("it shows again", () => tutorials.Visible && tutorials.Current?.Trigger == TutorialTrigger.Workers),
            new Capture("workers", "Workers tutorial shown after tutorials were switched back on and the seen list was reset"),
        ];
    }
}
