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
        "Is each tab comfortable to scroll through on a small window, and are the switches clearly on or off?",
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

        var ui = new UiTheme(registry);
        var backdrop = new Control { Name = "OptionsBackdrop", Theme = ui.Theme };
        backdrop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var screenOptions = new OptionsScreen(service, ui, null, ["Retro", "Neon"]);
        screenOptions.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        backdrop.AddChild(screenOptions);
        host.AddChild(backdrop);
        var options = screenOptions.Panel;
        int startTurn = game.Turns;
        float focusBefore = 0f;
        float slowStep = 0f;
        int resourcesBefore = 0;
        ClientSettingsService? restarted = null;
        OptionsScreen? german = null;

        Vector2 Centre(Control control) => control.GetGlobalRect().GetCenter();
        ScenarioStep[] Reveal(HotkeyAction action) =>
        [
            new Act("open the Keys tab and scroll the " + action + " row into view", () => options.Reveal(options.KeyButton(action))),
            new WaitFrames(2),
        ];

        return
        [
            new WaitFrames(3),
            new Act("dismiss the banner", () => screen.Hud.HideBanner()),
            new AssertQuery("defaults (C078, the Kotlin keys): End turn is N, Next unit is Period, Technology is F6, nothing stored; the nav tooltips and the end-turn button show them", () =>
                Hotkeys.KeyFor(service.Current.KeyBindings, HotkeyAction.EndTurn) == "N" && options.KeyButton(HotkeyAction.NextUnit).Text == "Period" && service.Current.KeyBindings.Count == 0 && !File.Exists(path)
                && screen.World.Technology.TooltipText == "Technology (F6)" && screen.Hud.EndTurnButton is IconButton { Hotkey: "N" } && screen.World.Overview.Hotkey == "E"),
            new Capture("top", "Options, Display tab: the vertical tabs on the left, the UI scale stepper and the window mode"),
            LayoutChecks.Assert("at 640x480 no options text is cut off, leaves the window or overlaps another", host, () => backdrop),
            new Act("widen the window to 1280x720", () => host.GetWindow().Size = new Vector2I(1280, 720)),
            new WaitFrames(6),
            LayoutChecks.Assert("at 1280x720 no options text is cut off, leaves the window or overlaps another", host, () => backdrop),
            new Capture("wide", "1280x720: the Display tab with the tab strip, the stepper and the window mode in the wider panel"),
            new Act("widen the window to 1920x1080", () => host.GetWindow().Size = new Vector2I(1920, 1080)),
            new WaitFrames(6),
            LayoutChecks.Assert("at 1920x1080 no options text is cut off, leaves the window or overlaps another", host, () => backdrop),
            new Capture("fhd", "1920x1080: the same tab at full HD"),
            new Act("back to 640x480", () => host.GetWindow().Size = new Vector2I(640, 480)),
            new WaitFrames(8),
            LayoutChecks.Assert("back at 640x480 no options text is cut off, leaves the window or overlaps another", host, () => backdrop),

            .. Reveal(HotkeyAction.EndTurn),
            new Click(() => Centre(options.KeyButton(HotkeyAction.EndTurn))),
            new WaitFrames(2),
            new AssertQuery("the End turn row waits for a key", () => options.Listening == HotkeyAction.EndTurn && options.KeyButton(HotkeyAction.EndTurn).Text == "..."),
            new Capture("listening", "The Keys tab while rebinding: the End turn key chip shows ... and the other actions keep their keys"),
            new PressKey(Key.Escape),
            new WaitFrames(2),
            new AssertQuery("Escape cancelled: still N and nothing saved", () => options.Listening == null && options.KeyButton(HotkeyAction.EndTurn).Text == "N" && !File.Exists(path)),
            .. Reveal(HotkeyAction.EndTurn), // C046: one more key row below pushes End turn out of view after the scroll to the bottom
            new Click(() => Centre(options.KeyButton(HotkeyAction.EndTurn))),
            new WaitFrames(2),
            new PressKey(Key.Q),
            new WaitFrames(2),
            new AssertQuery("End turn is Q on the button, in the settings, in the file and in the end-turn tooltip", () =>
                options.Listening == null && options.KeyButton(HotkeyAction.EndTurn).Text == "Q"
                && service.Current.KeyBindings.Count == 1 && service.Current.KeyBindings["EndTurn"] == "Q" && File.ReadAllText(path).Contains("\"EndTurn\": \"Q\"", StringComparison.Ordinal)
                && screen.Hud.EndTurnButton is IconButton { Hotkey: "Q" }),
            new Act("restart the client: a new service reads the file", () => restarted = new ClientSettingsService(new ClientSettingsStore(path))),
            new AssertQuery("the binding persisted", () => Hotkeys.KeyFor(restarted.NotNull().Current.KeyBindings, HotkeyAction.EndTurn) == "Q"),
            .. Reveal(HotkeyAction.EndTurn),
            new Capture("rebound", "The Keys tab after rebinding: End turn now shows Q, the other actions keep their default keys"),

            new Act("close the options", () => backdrop.Visible = false),
            new WaitFrames(2),
            new PressKey(Key.N),
            new WaitFrames(4),
            new AssertQuery("the old key does nothing", () => game.Turns == startTurn),
            new PressKey(Key.Q),
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
                foreach (var box in options.FindChildren("*", "CheckButton", true, false).OfType<CheckButton>())
                {
                    if (box.Text == "Show resources on the map") box.ButtonPressed = false;
                }
            }),
            new WaitFrames(2),
            new AssertQuery("volume and the toggle reached the settings, the file and the map", () =>
                Math.Abs(service.Current.SoundVolume - 0.25f) < 0.001f && !service.Current.ShowResources && new ClientSettingsStore(path).Load().ShowResources == false
                && resourcesBefore > 0 && screen.Map.VisibleResourcePropCount == 0),
            new Act("open the Map tab", () => options.SelectTab(options.TabNames.ToList().IndexOf("Map"))),
            new WaitFrames(2),
            new Capture("settings", "The Map tab after the game-screen checks: scroll speed 2.00, edge-scroll on, resources off"),

            // C079: the options again in German on a fresh options screen (text is built with the screen), Keys tab (the longest labels)
            LocaleSteps.Use("German"),
            new Act("rebuild the options screen in German", () =>
            {
                backdrop.Visible = false;
                german = new OptionsScreen(service, ui, null, ["Retro", "Neon"]);
                german.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
                var layer = new Control { Name = "GermanBackdrop", Theme = ui.Theme };
                layer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
                layer.AddChild(german);
                host.AddChild(layer);
            }),
            new WaitFrames(3),
            LayoutChecks.Assert("German: no options text is cut off, leaves the window or overlaps another (Display tab)", host, () => german.NotNull()),
            new Capture("de", "German at 640x480: the Display tab's labels, the tab strip and the buttons fit without clipping or overlap"),
        ];
    }
}
