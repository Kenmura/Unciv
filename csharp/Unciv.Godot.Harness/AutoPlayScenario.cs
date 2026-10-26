using Godot;
using Unciv.Commands;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Play;
using Unciv.GodotClient.Settings;
using Unciv.GodotClient.Ui;
using Unciv.Harness;
using Unciv.Utils;

namespace Unciv.GodotHarness;

/// <summary>
/// C047: the AutoPlay button over the real game screen. The option "Show AutoPlay button" shows it; its menu offers "Start AutoPlay" (the stored number of turns) and
/// "AutoPlay End Turn". Starting five turns advances the game five turns by the engine's auto-play (one command in the log, then one per turn), the Next turn button is greyed
/// meanwhile, and afterwards control is the player's again. A longer run is stopped with the button, which sends the engine's stop command and leaves the turn to the player.
/// The options screen shows the four AutoPlay settings and they persist in the settings file. The pace is slowed to twelve frames per turn so captures catch a run midway.
/// </summary>
public sealed class AutoPlayScenario : Scenario
{
    public override string Name => "autoplay";

    public override bool Uses3D => true;

    public override bool Solo => true;

    public override IReadOnlyList<string> Checklist =>
    [
        "Is the AutoPlay button findable, and does it clearly turn into a Stop control with the turns left while a run goes on?",
        "Does the menu say how long each entry plays?",
        "Is it obvious that Next turn is unavailable during a run, and that stopping gives the turn back?",
    ];

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session)
    {
        var game = session.Game;
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "unciv-autoplay-scenario-" + System.Environment.ProcessId, "settings.json");
        if (File.Exists(path)) File.Delete(path);
        var service = new ClientSettingsService(new ClientSettingsStore(path));
        var registry = AssetRegistry.ForRuleset(game.Ruleset);
        var screen = new GameScreen();
        host.AddChild(screen);
        screen.Start(new LocalHost(session, ScenarioGame.HumanA, () => 1000L), registry);
        screen.TutorialsEnabled = false;
        screen.AutoPlayFramesPerTurn = 12;
        screen.BindSettings(service);

        var backdrop = new PanelContainer { Name = "OptionsBackdrop", Theme = new UiTheme(registry).Theme, Visible = false };
        backdrop.Position = Vector2.Zero;
        backdrop.Size = new Vector2(640, 480);
        var options = new OptionsScreen(service);
        backdrop.AddChild(options);
        host.AddChild(backdrop);

        int startTurn = 0;
        int logged = 0;
        int Count(string type) => session.Log.Skip(logged).Count(it => it.Type == type);
        Vector2 Centre(Control control) => control.GetGlobalRect().GetCenter();

        return
        [
            new WaitFrames(3),
            new Act("dismiss the banner", () => screen.Hud.HideBanner()),
            new AssertQuery("the AutoPlay button is hidden by default (Kotlin: showAutoPlayButton is false)", () => !screen.Hud.AutoPlayButton.Visible),
            new Act("turn the option on: five turns per run", () =>
            {
                service.Update(s => s with { AutoPlay = s.AutoPlay with { ShowButton = true, MaxTurns = 5 } });
                startTurn = game.Turns;
                logged = session.Log.Count;
            }),
            new WaitFrames(3),
            new AssertQuery("the button shows, idle", () => screen.Hud.AutoPlayButton.Visible && screen.Hud.AutoPlayButton.Text == "AutoPlay" && screen.AutoPlay == null),
            new Capture("button", "The game screen with the AutoPlay option on: an AutoPlay button above Next turn"),

            new Press("AutoPlay"),
            new WaitFrames(2),
            new AssertQuery("the menu lists both entries, enabled, with the stored length", () =>
                screen.AutoPlayMenu.Visible && screen.AutoPlayMenu.Entries.Count == 2 && screen.AutoPlayMenu.Entries.All(it => it.Enabled) && screen.AutoPlayMenu.Entries[0].Detail == "5 turns"),
            new Capture("menu", "The AutoPlay menu above the button: Start AutoPlay (5 turns) and AutoPlay End Turn (1 turn)"),

            new Press("Start AutoPlay"),
            new WaitFrames(4),
            new AssertQuery("the run started: one turn played, four left, the button is Stop", () =>
                !screen.AutoPlayMenu.Visible && screen.AutoPlay is { TurnsPlayed: 1, TurnsLeft: 4 } && screen.Hud.AutoPlayButton.Text == "Stop AutoPlay (4 left)" && game.Turns == startTurn + 1),
            new AssertQuery("Next turn is greyed while it runs", () => screen.Hud.AutoPlayRunning && screen.Hud.ActionDisabled && !screen.Hud.EndTurnShown),
            new Capture("running", "AutoPlay running: the button reads Stop AutoPlay (4 left), Next turn is greyed and the turn counter has moved on"),

            new WaitFrames(60),
            new AssertQuery("five turns were played and the run is over", () => screen.AutoPlay == null && game.Turns == startTurn + 5 && screen.AutoPlayResult == "AutoPlay finished after 5 turns."),
            new AssertQuery("the log holds one start and five auto-played turns, no stop", () => Count("start-auto-play") == 1 && Count("auto-play-turn") == 5 && Count("stop-auto-play") == 0),
            new AssertQuery("control is back: the button is idle and Next turn is enabled", () => screen.Hud.AutoPlayButton.Text == "AutoPlay" && !screen.Hud.AutoPlayRunning && !screen.Hud.ActionDisabled),
            new Capture("finished", "After the five turns: the status line says AutoPlay finished after 5 turns, the button is idle again"),

            new Act("a long run: 50 turns", () =>
            {
                service.Update(s => s with { AutoPlay = s.AutoPlay with { MaxTurns = 50 } });
                logged = session.Log.Count;
                startTurn = game.Turns;
            }),
            new Press("AutoPlay"),
            new Press("Start AutoPlay"),
            new WaitFrames(30),
            new AssertQuery("it is running", () => screen.AutoPlay != null && game.Turns > startTurn),
            new Click(() => Centre(screen.Hud.AutoPlayButton)),
            new WaitFrames(3),
            new AssertQuery("Stop gave control back at once: not running, fewer than 50 turns, the engine was told", () =>
                screen.AutoPlay == null && game.Turns > startTurn && game.Turns < startTurn + 50 && Count("stop-auto-play") == 1 && screen.AutoPlayResult.NotNull().StartsWith("AutoPlay stopped after", StringComparison.Ordinal)),
            new Act("remember the turn", () => startTurn = game.Turns),
            new WaitFrames(40),
            new AssertQuery("no further turn is played after Stop", () => game.Turns == startTurn && !game.ClientState.AutoPlay.NotNull().IsAutoPlaying()),
            new Capture("stopped", "After pressing Stop: the status line says AutoPlay stopped, the button is idle and the turn is the player's"),
            new Press("Next turn"),
            new WaitFrames(8),
            new AssertQuery("Next turn works again (one turn)", () => game.Turns == startTurn + 1),

            new Act("remember the log", () => logged = session.Log.Count),
            new Press("AutoPlay"),
            new Press("AutoPlay End Turn"),
            new WaitFrames(20),
            new AssertQuery("AutoPlay End Turn plays exactly one turn", () => screen.AutoPlay == null && game.Turns == startTurn + 2 && Count("start-auto-play") == 1 && Count("auto-play-turn") == 1),

            new Act("open the options", () => backdrop.Visible = true),
            new WaitFrames(2),
            new Act("scroll to the AutoPlay settings", () => options.EnsureControlVisible(options.AutoPlayTurnsSlider)),
            new Act("a little further so the whole section shows", () => options.ScrollVertical += 90),
            new WaitFrames(3),
            new AssertQuery("the options show the slider at 50", () => options.AutoPlayTurnsSlider.Value == 50),
            new Capture("options", "Options, AutoPlay section: show the button, play until victory, number of turns and full AutoPlay AI"),
            new Act("close the options", () => backdrop.Visible = false),

            new Act("a new client reads the file", () => restartedSettings = new ClientSettingsService(new ClientSettingsStore(path))),
            new AssertQuery("the AutoPlay options persisted", () => restartedSettings.NotNull().Current.AutoPlay is { ShowButton: true, MaxTurns: 50, FullAutoPlayAI: true, UntilEnd: false }),
            new Act("hide the button again", () => service.Update(s => s with { AutoPlay = s.AutoPlay with { ShowButton = false } })),
            new WaitFrames(2),
            new AssertQuery("the button is gone", () => !screen.Hud.AutoPlayButton.Visible),
        ];
    }

    private ClientSettingsService? restartedSettings;
}
