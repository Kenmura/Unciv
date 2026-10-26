using Godot;
using Unciv.Commands;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Map;
using Unciv.GodotClient.Play;
using Unciv.GodotClient.Settings;
using Unciv.GodotClient.Ui;
using Unciv.Harness;
using Unciv.Utils;

namespace Unciv.GodotHarness;

/// <summary>
/// C028: the options screen over the real game screen. Rebinding End turn from the screen (click its key button, press a key) is saved in the settings file, survives a
/// "restart" (a new service reads the same file), and the new key ends the turn on the game screen while the old one does nothing; Escape cancels a rebind. Volume, edge-pan
/// and scroll speed reach the settings file and the map (edge-pan moves the camera by the Godot-free maths, twice as far at twice the speed), and switching resources off hides
/// every resource prop on the map. The file lives in the harness temp directory, not in the player's user:// folder.
/// </summary>
public sealed class OptionsScenario : Scenario
{
    public override string Name => "options";

    public override bool Uses3D => true;

    public override bool Solo => true;

    public override IReadOnlyList<string> Checklist =>
    [
        "Is it clear what each option does, and which key belongs to which action?",
        "Is the waiting-for-a-key state obvious, and how do you get out of it?",
        "Is the screen comfortable to scroll through on a small window?",
    ];

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session)
    {
        var game = session.Game;
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "unciv-options-scenario-" + System.Environment.ProcessId, "settings.json");
        if (File.Exists(path)) File.Delete(path);
        var service = new ClientSettingsService(new ClientSettingsStore(path));
        var registry = AssetRegistry.ForRuleset(game.Ruleset);
        var screen = new GameScreen();
        host.AddChild(screen);
        screen.Start(new LocalHost(session, ScenarioGame.HumanA, () => 1000L), registry);
        screen.TutorialsEnabled = false;
        screen.BindSettings(service);

        var backdrop = new PanelContainer { Name = "OptionsBackdrop", Theme = new UiTheme(registry).Theme };
        backdrop.Position = Vector2.Zero;
        backdrop.Size = new Vector2(640, 480);
        var options = new OptionsScreen(service, null, ["Retro", "Neon"]);
        backdrop.AddChild(options);
        host.AddChild(backdrop);
        int startTurn = game.Turns;
        float focusBefore = 0f;
        float slowStep = 0f;
        int resourcesBefore = 0;
        ClientSettingsService? restarted = null;

        Vector2 Centre(Control control) => control.GetGlobalRect().GetCenter();
        ScenarioStep[] Reveal(HotkeyAction action) =>
        [
            new Act("scroll the " + action + " row into view", () => options.EnsureControlVisible(options.KeyButton(action))),
            new WaitFrames(2),
        ];

        return
        [
            new WaitFrames(3),
            new Act("dismiss the banner", () => screen.Hud.HideBanner()),
            new AssertQuery("defaults: End turn is Space, Next unit is N, nothing stored", () =>
                Hotkeys.KeyFor(service.Current.KeyBindings, HotkeyAction.EndTurn) == "Space" && options.KeyButton(HotkeyAction.NextUnit).Text == "N" && service.Current.KeyBindings.Count == 0 && !File.Exists(path)),
            new Capture("top", "Options, top: engine options, UI scale buttons, then sound volume, window mode, scroll speed and the edge-scroll and map toggles"),

            .. Reveal(HotkeyAction.EndTurn),
            new Click(() => Centre(options.KeyButton(HotkeyAction.EndTurn))),
            new WaitFrames(2),
            new AssertQuery("the End turn row waits for a key", () => options.Listening == HotkeyAction.EndTurn && options.KeyButton(HotkeyAction.EndTurn).Text == "..."),
            new Act("scroll to the bottom", () => options.ScrollVertical = 100000),
            new WaitFrames(2),
            new Capture("listening", "Bottom of the options while rebinding: the End turn key button shows ... and the message under the key list says to press a key or Escape"),
            new PressKey(Key.Escape),
            new WaitFrames(2),
            new AssertQuery("Escape cancelled: still Space and nothing saved", () => options.Listening == null && options.KeyButton(HotkeyAction.EndTurn).Text == "Space" && !File.Exists(path)),
            .. Reveal(HotkeyAction.EndTurn), // C046: one more key row below pushes End turn out of view after the scroll to the bottom
            new Click(() => Centre(options.KeyButton(HotkeyAction.EndTurn))),
            new WaitFrames(2),
            new PressKey(Key.E),
            new WaitFrames(2),
            new AssertQuery("End turn is E on the button, in the settings and in the file", () =>
                options.Listening == null && options.KeyButton(HotkeyAction.EndTurn).Text == "E"
                && service.Current.KeyBindings.Count == 1 && service.Current.KeyBindings["EndTurn"] == "E" && File.ReadAllText(path).Contains("\"EndTurn\": \"E\"", StringComparison.Ordinal)),
            new Act("restart the client: a new service reads the file", () => restarted = new ClientSettingsService(new ClientSettingsStore(path))),
            new AssertQuery("the binding persisted", () => Hotkeys.KeyFor(restarted.NotNull().Current.KeyBindings, HotkeyAction.EndTurn) == "E"),
            new Act("scroll to the bottom", () => options.ScrollVertical = 100000),
            new WaitFrames(2),
            new Capture("rebound", "Bottom of the options after rebinding: End turn now shows E, the other actions keep their default keys"),

            new Act("close the options", () => backdrop.Visible = false),
            new WaitFrames(2),
            new PressKey(Key.Space),
            new WaitFrames(4),
            new AssertQuery("the old key does nothing", () => game.Turns == startTurn),
            new PressKey(Key.E),
            new WaitFrames(8),
            new AssertQuery("the new key ended the turn", () => game.Turns == startTurn + 1 && screen.Turn == startTurn + 1),

            new Act("count the resource props and set a slow scroll with edge-pan on", () =>
            {
                resourcesBefore = screen.Map.VisibleResourcePropCount;
                service.Update(s => s with { EdgePan = true, ScrollSpeed = 1f });
                focusBefore = screen.Map.Focus.X;
                screen.Map.TickEdgePan(new Vector2(0, 240), new Vector2(640, 480), 0.1);
                slowStep = focusBefore - screen.Map.Focus.X;
            }),
            new AssertQuery("edge-pan moved the camera left by the model's distance", () =>
                screen.Map.EdgePan && Math.Abs(slowStep - OptionsModel.PanDistance(1f, MapView.DefaultZoom, 0.1)) < 0.001f && slowStep > 0f),
            new Act("double the scroll speed and pan again", () =>
            {
                service.Update(s => s with { ScrollSpeed = 2f });
                float before = screen.Map.Focus.X;
                screen.Map.TickEdgePan(new Vector2(0, 240), new Vector2(640, 480), 0.1);
                focusBefore = before - screen.Map.Focus.X;
            }),
            new AssertQuery("twice the speed, twice the distance", () => Math.Abs(focusBefore - 2f * slowStep) < 0.001f),
            new Act("resting in the middle does not pan", () =>
            {
                float before = screen.Map.Focus.X;
                screen.Map.TickEdgePan(new Vector2(320, 240), new Vector2(640, 480), 0.1);
                slowStep = before - screen.Map.Focus.X;
            }),
            new AssertQuery("no movement away from the edge", () => slowStep == 0f),

            new Act("open the options again", () => backdrop.Visible = true),
            new WaitFrames(2),
            new Act("drag the volume slider and switch resources off", () => options.VolumeSlider.Value = 0.25),
            new Act("resources off through the checkbox", () =>
            {
                foreach (var box in options.FindChildren("*", "CheckBox", true, false).OfType<CheckBox>())
                {
                    if (box.Text == "Show resources on the map") box.ButtonPressed = false;
                }
            }),
            new WaitFrames(2),
            new AssertQuery("volume and the toggle reached the settings, the file and the map", () =>
                Math.Abs(service.Current.SoundVolume - 0.25f) < 0.001f && !service.Current.ShowResources && new ClientSettingsStore(path).Load().ShowResources == false
                && resourcesBefore > 0 && screen.Map.VisibleResourcePropCount == 0),
            new Act("scroll to the bottom", () => options.ScrollVertical = 100000),
            new WaitFrames(2),
            new Capture("settings", "Bottom of the options after the game-screen checks: volume slider moved to 25%, edge-scroll on, resources off, scroll speed 2, End turn still E"),
        ];
    }
}
