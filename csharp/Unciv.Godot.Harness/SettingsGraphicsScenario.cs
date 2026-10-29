using Godot;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Settings;
using Unciv.GodotClient.Ui;
using Unciv.Commands;
using Unciv.Utils;

namespace Unciv.GodotHarness;

/// <summary>
/// C097: the graphics quality picker on the Display tab. High applies at once; Ultra on the compatibility renderer shows the restart note and button, the button
/// stores the restart marker and asks for a Forward+ restart (recorded here, never performed); a start that finds the renderer still not Forward+ stores the
/// failure flag instead of restarting again, and the picker then says Ultra shows High. The renderer is overridden to the compatibility renderer, so the pictures do not
/// depend on the machine, and the restart is a recording handler. The file lives in the harness temp directory.
/// </summary>
public sealed class SettingsGraphicsScenario : Scenario
{
    public override string Name => "settings-graphics";

    public override IReadOnlyList<string> Checklist =>
    [
        "Is it clear which quality level is chosen, and what Ultra needs?",
        "Is the restart note readable and the Restart to apply button easy to find?",
        "After a failed Ultra attempt, does the note say why High is shown?",
    ];

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session)
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "unciv-settings-graphics-scenario-" + System.Environment.ProcessId, "settings.json");
        if (File.Exists(path)) File.Delete(path);
        GraphicsRuntime.Pin(null);
        GraphicsRuntime.RendererOverride = GraphicsModel.Compatibility;
        GraphicsRuntime.AdapterOverride = "Harness test adapter";
        string? restartedInto = null;
        GraphicsRuntime.RestartHandler = method => restartedInto = method;

        var service = new ClientSettingsService(new ClientSettingsStore(path));
        var ui = new UiTheme(AssetRegistry.ForRuleset(session.Game.Ruleset));
        var panel = new SettingsPanel(service, ui) { Position = new Vector2(40, 40), Size = new Vector2(560, 400), Theme = ui.Theme };
        host.AddChild(panel);
        ClientSettingsService? restarted = null;
        SettingsPanel? second = null;
        bool startRestarts = true;

        void Pick(SettingsPanel from, GraphicsQuality level)
        {
            from.GraphicsPicker.Select((int)level);
            from.GraphicsPicker.EmitSignal(OptionButton.SignalName.ItemSelected, (long)level);
        }

        return
        [
            new WaitFrames(2),
            new AssertQuery("default: Low, no note, no restart button, nothing stored", () =>
                panel.GraphicsPicker.Selected == 0 && !panel.GraphicsNote.Visible && !panel.GraphicsRestartButton.Visible && !File.Exists(path)
                && GraphicsRuntime.EffectiveFor(service.Current) == GraphicsQuality.Low),
            new Capture("low", "Display tab with Graphics quality on Low (the default): no note and no restart button"),
            new Act("choose High", () => Pick(panel, GraphicsQuality.High)),
            new WaitFrames(2),
            new AssertQuery("High is stored, drawn at once on this renderer and needs no restart", () =>
                service.Current.GraphicsLevel == "High" && File.ReadAllText(path).Contains("\"graphicsQuality\": \"High\"", StringComparison.Ordinal)
                && GraphicsRuntime.EffectiveFor(service.Current) == GraphicsQuality.High && !panel.GraphicsNote.Visible && !panel.GraphicsRestartButton.Visible),
            new Capture("high", "Graphics quality on High: nothing more to say, High works on every renderer"),
            new Act("choose Ultra", () => Pick(panel, GraphicsQuality.Ultra)),
            new WaitFrames(2),
            new AssertQuery("Ultra on the compatibility renderer: the note and the restart button show, High is still drawn", () =>
                panel.GraphicsNote.Visible && panel.GraphicsNote.Text == "Ultra needs a restart to switch the renderer" && panel.GraphicsRestartButton.Visible
                && GraphicsRuntime.EffectiveFor(service.Current) == GraphicsQuality.High),
            new Capture("ultra-restart", "Graphics quality on Ultra while the compatibility renderer runs: a note says Ultra needs a restart and Restart to apply is offered"),
            new Press("Restart to apply"),
            new WaitFrames(2),
            new AssertQuery("the restart asked for Forward+ and the marker was stored", () =>
                restartedInto == GraphicsModel.ForwardPlus && service.Current.RendererPending == GraphicsModel.ForwardPlus
                && new ClientSettingsStore(path).Load().RendererPending == GraphicsModel.ForwardPlus),
            new Act("restart: the new process still has the compatibility renderer; its start check runs", () =>
            {
                restartedInto = null;
                restarted = new ClientSettingsService(new ClientSettingsStore(path));
                startRestarts = GraphicsRuntime.StartupCheck(restarted);
            }),
            new AssertQuery("the failed attempt is remembered, nothing restarts again (no loop), Ultra shows High", () =>
                !startRestarts && restartedInto == null && restarted.NotNull().Current.UltraFailed && restarted.NotNull().Current.RendererPending == ""
                && restarted.NotNull().Current.Quality == GraphicsQuality.Ultra && GraphicsRuntime.EffectiveFor(restarted.NotNull().Current) == GraphicsQuality.High
                && new ClientSettingsStore(path).Load().UltraFailed),
            new Act("the options of the restarted client", () =>
            {
                panel.Visible = false;
                second = new SettingsPanel(restarted.NotNull(), ui) { Position = new Vector2(40, 40), Size = new Vector2(560, 400), Theme = ui.Theme };
                host.AddChild(second);
            }),
            new WaitFrames(2),
            new AssertQuery("the picker still says Ultra, the note explains High, no restart button", () =>
                second.NotNull().GraphicsPicker.Selected == 2 && second.NotNull().GraphicsNote.Visible
                && second.NotNull().GraphicsNote.Text == "Ultra needs a Vulkan or Direct3D 12 GPU; showing High" && !second.NotNull().GraphicsRestartButton.Visible),
            new Capture("ultra-fallback", "After the failed attempt: Ultra stays chosen, the note says it needs a Vulkan or Direct3D 12 GPU and that High is shown"),
            new Act("restore the runtime", () =>
            {
                GraphicsRuntime.RendererOverride = null;
                GraphicsRuntime.AdapterOverride = null;
                GraphicsRuntime.RestartHandler = null;
                GraphicsRuntime.Pin(GraphicsQuality.Low);
            }),
        ];
    }
}
