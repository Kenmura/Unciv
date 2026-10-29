using Godot;
using Unciv.Commands;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Play;
using Unciv.GodotClient.Saves;
using Unciv.GodotClient.Settings;
using Unciv.GodotClient.Ui;
using Unciv.Harness;
using Unciv.Utils;

namespace Unciv.GodotHarness;

/// <summary>
/// C016: save by name through the dialog, end the turn (an autosave appears), load the save and find the state hash equal to the saved one; quick save and quick load by key,
/// delete with confirmation, a damaged and a truncated file that are listed, refuse to load and leave the running game alone, and Resume last. The save folder is a temporary
/// directory, not the player's user:// folder; file dates are pinned so the list (and its golden) does not depend on the clock.
/// </summary>
public sealed class SaveLoadScenario : Scenario
{
    public override string Name => "saveload";

    public override bool Uses3D => true;

    public override bool Solo => true;

    public override IReadOnlyList<string> Checklist =>
    [
        "Is the save table readable: name, turn, civ, ruleset and date on each row, and is the selected row and its details card obvious?",
        "Is it clear which footer buttons act on the selected save, and that Delete asks again?",
        "Do the damaged files read as damaged rather than as saves?",
    ];

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session)
    {
        string root = Path.Combine(Path.GetTempPath(), "unciv-saveload-scenario-" + System.Environment.ProcessId);
        if (Directory.Exists(root)) Directory.Delete(root, true);
        string folder = Path.Combine(root, "saves");
        var store = new SaveStore(folder);
        var settings = new ClientSettingsService(new ClientSettingsStore(Path.Combine(root, "settings.json")));
        long now = 1000;
        var localHost = new LocalHost(session, ScenarioGame.HumanA, () => now += 1000);
        var screen = new GameScreen();
        host.AddChild(screen);
        var registry = AssetRegistry.ForRuleset(session.Game.Ruleset);
        screen.Start(localHost, registry);
        LocalHost? loaded = null;
        var controller = new SaveController(store, settings, new UiTheme(registry),
            text => loaded = LocalHost.FromSave(text, () => now += 1000), session2 => screen.Start(session2, registry));
        host.AddChild(controller);
        controller.Attach(localHost);

        int startTurn = session.Game.Turns;
        string savedHash = "";
        int savedTurn = -1;
        string brokenText = "";

        void PinDates()
        {
            // The list shows file dates: fix them so the capture is reproducible. Older save first, autosaves newest.
            var when = new DateTime(2020, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            foreach (string path in Directory.GetFiles(folder).Order(StringComparer.Ordinal))
            {
                string name = Path.GetFileName(path);
                File.SetLastWriteTimeUtc(path, SaveStore.IsAutosaveName(name) ? when.AddDays(1) : name == "Broken" ? when.AddDays(2) : when);
            }
            controller.RefreshList();
        }

        ScenarioStep[] Row(string name) =>
        [
            new Act("scroll the " + name + " row into view", () =>
            {
                var row = FindRow(host, name);
                for (Node? node = row; node != null; node = node.GetParent())
                {
                    if (node is ScrollContainer scroller)
                    {
                        scroller.EnsureControlVisible(row.NotNull());
                        break;
                    }
                }
            }),
            new WaitFrames(2),
            new Click(() => RowCentre(name)),
            new WaitFrames(2),
        ];

        Vector2 RowCentre(string prefix) => FindRow(host, prefix)?.GetGlobalRect().GetCenter() ?? Vector2.Zero;

        return
        [
            new WaitFrames(3),
            new AssertQuery("no saves yet", () => store.List().Count == 0 && !Directory.Exists(folder)),
            new Press("Saves"),
            new AssertQuery("the dialog is open with an empty list", () => controller.IsDialogOpen && controller.ListedNames.Count == 0),
            new Act("type a name", () => controller.NameField.NotNull().Text = "Alpha"),
            new Press("Save"),
            new AssertQuery("the save was written and listed with its turn", () => store.List() is [{ Name: "Alpha", Info.Turn: var t }] && t == startTurn && controller.Message.StartsWith("Saved", StringComparison.Ordinal)),
            new Act("remember the saved state", () => { savedHash = ScenarioGame.StateHash(session); savedTurn = session.Game.Turns; }),
            new Press("Close"),
            new AssertQuery("the dialog closed", () => !controller.IsDialogOpen),
            new Press("Next turn"),
            new AssertQuery("the turn advanced and the state changed", () => session.Game.Turns == startTurn + 1 && ScenarioGame.StateHash(session) != savedHash),
            new AssertQuery("an autosave and a numbered autosave appeared", () => store.List().Select(it => it.Name).Order(StringComparer.Ordinal).SequenceEqual(["Alpha", "Autosave", $"Autosave-{startTurn + 1}"])),
            new Act("pin file dates and open the list", PinDates),
            new Press("Saves"),
            new Capture("list", "The save list: Alpha, then the autosave and the numbered autosave, each with turn, civ and ruleset"),
            .. Row("Alpha"),
            new AssertQuery("selecting a save copies its name into the name field", () => controller.NameField.NotNull().Text == "Alpha"),
            new Press("Load"),
            new AssertQuery("the loaded game's state hash equals the saved one", () => loaded != null && ScenarioGame.StateHash(loaded.Session) == savedHash),
            new AssertQuery("the screen shows the loaded turn and the dialog closed", () => screen.Turn == savedTurn && !controller.IsDialogOpen && controller.Message.StartsWith("Loaded", StringComparison.Ordinal)),
            new Capture("loaded", "After loading Alpha: the screen shows the saved turn again"),

            new PressKey(Key.F5),
            new AssertQuery("F5 wrote the quick save", () => store.List().Any(it => it.Name == "QuickSave" && it.Info?.Turn == savedTurn)),
            new Press("Next turn"),
            new AssertQuery("the loaded game plays on", () => screen.Turn == savedTurn + 1),
            new PressKey(Key.F9),
            new AssertQuery("F9 went back to the quick save", () => screen.Turn == savedTurn && ScenarioGame.StateHash(loaded.NotNull().Session) == savedHash),

            new Act("write a damaged and a truncated save", () =>
            {
                brokenText = File.ReadAllText(Path.Combine(folder, "Alpha"));
                File.WriteAllText(Path.Combine(folder, "Broken"), "this is not a save");
                File.WriteAllText(Path.Combine(folder, "Truncated"), brokenText[..(brokenText.Length / 2)]);
                PinDates();
            }),
            new Press("Saves"),
            new AssertQuery("both damaged files are listed but not readable", () => store.List().Count(it => it.Info == null) == 2),
            new Capture("damaged", "The damaged files are listed as unreadable"),
            .. Row("Broken"),
            new Press("Load"),
            new AssertQuery("loading the damaged file was refused with a message and changed nothing", () => controller.Message.StartsWith("Cannot load", StringComparison.Ordinal) && screen.Turn == savedTurn && controller.IsDialogOpen),
            .. Row("Truncated"),
            new Press("Load"),
            new AssertQuery("the truncated file was refused too", () => controller.Message.StartsWith("Cannot load \"Truncated\"", StringComparison.Ordinal) && screen.Turn == savedTurn),

            .. Row("Alpha"),
            new Press("Delete"),
            new AssertQuery("Delete only asks: the save is still there", () => store.List().Any(it => it.Name == "Alpha") && controller.Message.Contains("Confirm delete", StringComparison.Ordinal)),
            new Press("Confirm delete"),
            new AssertQuery("the save is gone after the confirmation", () => store.List().All(it => it.Name != "Alpha")),
            new Capture("deleted", "After deleting Alpha: gone from the list"),

            new PressKey(Key.Escape), new WaitFrames(2),
            new AssertQuery("Escape closed the dialog and did not open the game menu behind it", () => !controller.IsDialogOpen && !screen.GameMenu.Visible),
            new Press("Saves"),
            new Press("Resume last"),
            new AssertQuery("Resume last loaded the newest readable save (an autosave)", () => controller.Message.StartsWith("Loaded \"Autosave", StringComparison.Ordinal) && !controller.IsDialogOpen),
        ];
    }

    /// <summary>The name cell of a row of the open dialog's table.</summary>
    private static Label? FindRow(Node host, string name) =>
        host.FindChild("SaveTable", true, false)?.FindChildren("*", "Label", true, false).OfType<Label>().FirstOrDefault(it => it.Text == name && it.IsVisibleInTree());
}
