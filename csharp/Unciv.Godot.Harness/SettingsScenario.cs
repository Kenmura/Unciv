using Godot;
using Unciv.Commands;
using Unciv.GodotClient.Settings;
using Unciv.Harness;
using Unciv.Utils;

namespace Unciv.GodotHarness;

/// <summary>
/// C043: change a setting in the panel, "restart" the client (a new store and service read the same file) and find the value again; the engine
/// option reaches the game as a SetPlayerSettings command (state asserted from the game, command in the log). The file lives in the harness
/// output directory, not in the player's user:// folder.
/// </summary>
public sealed class SettingsScenario : Scenario
{
    public override string Name => "settings";

    public override IReadOnlyList<string> Checklist =>
    [
        "Is it clear which options are on?",
        "Does the UI scale line change when Bigger UI is pressed?",
    ];

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session)
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "unciv-settings-scenario-" + System.Environment.ProcessId, "settings.json");
        if (File.Exists(path)) File.Delete(path);
        var store = new ClientSettingsStore(path);
        var service = new ClientSettingsService(store, command => session.Submit(ScenarioGame.HumanA, command).Accepted);
        host.AddChild(new SettingsPanel(service) { Position = new Vector2(40, 40) });
        ClientSettingsService? restarted = null;

        return
        [
            new WaitFrames(2),
            new AssertQuery("no file yet and the game has the defaults", () => !File.Exists(path) && !session.Game.GetPlayerSettings(ScenarioGame.HumanA).AutomatedUnitsMoveOnTurnStart),
            new Capture("start", "Defaults: all three options off, scale 1.00"),
            new Press("Automated units move on turn start"),
            new Press("Bigger UI"),
            new AssertQuery("the option reached the game as a command", () => session.Game.GetPlayerSettings(ScenarioGame.HumanA).AutomatedUnitsMoveOnTurnStart && session.LastSeq == 1),
            new AssertQuery("the file was written", () => File.Exists(path)),
            new Act("restart the client: load the file again", () => restarted = new ClientSettingsService(new ClientSettingsStore(path))),
            new AssertQuery("the option and the scale persisted", () => restarted.NotNull().Current.Player.AutomatedUnitsMoveOnTurnStart && restarted.NotNull().Current.UiScale == 1.25f),
            new Capture("changed", "After the clicks: move-on-turn-start is on, scale 1.25"),
        ];
    }
}
