using Godot;
using Unciv.Commands;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Play;
using Unciv.GodotClient.Saves;
using Unciv.GodotClient.Settings;
using Unciv.GodotClient.Ui;
using Unciv.Harness;
using Unciv.Utils;
using Unciv.Logic.Civilization;

namespace Unciv.GodotHarness;

/// <summary>
/// C044: the in-game menu over the real game screen. Escape and the Menu button open it and Resume (or Escape) closes it; Save game opens the save dialog and the save is written
/// through <c>IGameSession.ExportSaveAsync</c>; Victory status lists the victories' milestones from the engine; Options and New game ask the owner; Main menu leaves at once while the
/// game is saved and asks first when it has progress since (Cancel does nothing, the confirmation leaves). Resign asks every time; cancelling sends nothing, confirming sends the
/// engine's <c>Resign</c> command (logged for the viewer), the civ becomes the AI's and the resigned screen offers only the main menu. The save folder is a temporary directory.
/// </summary>
public sealed class MenuScenario : Scenario
{
    public override string Name => "menu";

    public override bool Uses3D => true;

    public override bool Solo => true;

    public override IReadOnlyList<string> Checklist =>
    [
        "Is the Menu button findable and clear of the other buttons and the minimap?",
        "Does the menu list the actions in a sensible order, with Resign set apart from harmless ones by its confirmation?",
        "Do the confirmations say what is lost and offer a clear way back?",
    ];

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session)
    {
        string root = Path.Combine(Path.GetTempPath(), "unciv-menu-scenario");
        if (Directory.Exists(root)) Directory.Delete(root, true);
        var store = new SaveStore(Path.Combine(root, "saves"));
        var settings = new ClientSettingsService(new ClientSettingsStore(Path.Combine(root, "settings.json")));
        long now = 1000;
        var localHost = new LocalHost(session, ScenarioGame.HumanA, () => now += 1000);
        var screen = new GameScreen();
        host.AddChild(screen);
        var registry = AssetRegistry.ForRuleset(session.Game.Ruleset);
        screen.Start(localHost, registry);
        var controller = new SaveController(store, settings, new UiTheme(registry), text => LocalHost.FromSave(text, () => now += 1000), loaded => screen.Start(loaded, registry));
        host.AddChild(controller);
        controller.ButtonVisible = false;
        controller.Attach(localHost);
        screen.HasUnsavedProgress = () => controller.HasUnsavedProgress;
        int mainMenu = 0, options = 0, newGame = 0;
        screen.MainMenuRequested += () => mainMenu++;
        screen.OptionsRequested += () => options++;
        screen.NewGameRequested += () => newGame++;
        screen.SaveRequested += controller.OpenDialog;
        int logged = 0;

        return
        [
            new WaitFrames(4),
            new AssertQuery("a fresh game has no unsaved progress", () => !controller.HasUnsavedProgress),
            new Capture("hud", "The game screen with the Menu button after Trade, clear of the minimap"),
            new Press("Menu"),
            new WaitFrames(2),
            new AssertQuery("the menu lists every entry, none refused", () => screen.GameMenu.Visible && screen.GameMenu.Entries.Count == 8 && screen.GameMenu.Entries.All(it => it.Enabled)),
            new Capture("menu", "The in-game menu: resume, save, load, new game, victory status, options, resign and main menu"),
            new Press("Resume game"),
            new AssertQuery("Resume closed the menu", () => !screen.GameMenu.Visible),
            new PressKey(Key.Escape),
            new AssertQuery("Escape opens the menu", () => screen.GameMenu.Visible),
            new PressKey(Key.Escape),
            new AssertQuery("Escape closes it again", () => !screen.GameMenu.Visible),

            new Press("Menu"),
            new Press("Victory status"),
            new WaitFrames(2),
            new AssertQuery("the victory status page lists the victories from the engine", () => screen.VictoryStatus.Visible && !screen.GameMenu.Visible && screen.VictoryStatus.LineTexts.Count > 0),
            new Capture("victory-status", "Victory status: each victory with the milestones of your own path and every civ's progress"),
            new Press("Close"),
            new AssertQuery("Close hides the page", () => !screen.VictoryStatus.Visible),

            new Press("Menu"),
            new Press("Options"),
            new AssertQuery("Options asked the owner and closed the menu", () => options == 1 && !screen.GameMenu.Visible),

            new Press("Menu"),
            new Press("Save game"),
            new AssertQuery("Save game opened the save dialog", () => controller.IsDialogOpen && !screen.GameMenu.Visible),
            new Act("type a name", () => controller.NameField.NotNull().Text = "FromMenu"),
            new Press("Save"),
            new AssertQuery("the save was written through the session and the game counts as saved", () => store.List().Any(it => it.Name == "FromMenu") && !controller.HasUnsavedProgress),
            new Press("Close"),

            new Press("Menu"),
            new Press("Main menu"),
            new AssertQuery("with everything saved Main menu leaves without asking", () => mainMenu == 1 && screen.GameMenu.Entries.Count == 0),

            new Press("Next turn"),
            new WaitFrames(3),
            new AssertQuery("ending the turn is unsaved progress", () => controller.HasUnsavedProgress),
            new Press("Menu"),
            new Press("Start new game"),
            new AssertQuery("New game asks first because of the unsaved progress", () => newGame == 0 && screen.GameMenu.Asking != null),
            new Capture("unsaved", "Start new game with unsaved progress: the menu asks before the game is left"),
            new Press("Cancel"),
            new AssertQuery("Cancel went back to the list and did nothing", () => newGame == 0 && screen.GameMenu.Asking == null && screen.GameMenu.Visible),
            new Press("Main menu"),
            new Press("Cancel"),
            new AssertQuery("Main menu asked and Cancel left nothing done", () => mainMenu == 1),
            new Press("Main menu"),
            new Press("Leave for the main menu"),
            new AssertQuery("confirming leaves for the main menu", () => mainMenu == 2),

            new Act("remember the command log", () => logged = session.Log.Count),
            new Press("Menu"),
            new Press("Resign"),
            new AssertQuery("Resign asks first and sent nothing", () => screen.GameMenu.Asking?.Title == "Resign" && session.Log.Count == logged),
            new Capture("resign-confirm", "Resign asks for confirmation and says the AI takes over"),
            new Press("Cancel"),
            new AssertQuery("Cancel sent nothing and the civ is still human", () => session.Log.Count == logged && session.Game.GetCivilization(ScenarioGame.HumanA).PlayerType == PlayerType.Human),
            new Press("Resign"),
            new Press("Resign"),
            new WaitFrames(4),
            new AssertQuery("the engine's Resign was logged for the viewer", () => session.Log.Count == logged + 1 && session.Log[^1].Type == "resign" && session.Log[^1].ActorId == ScenarioGame.HumanA),
            new AssertQuery("the civ is the AI's now", () => session.Game.GetCivilization(ScenarioGame.HumanA).PlayerType == PlayerType.AI),
            new AssertQuery("the resigned screen offers only the main menu", () => screen.GameOver.Visible && screen.GameOver.TitleText == "Resigned" && screen.GameOver.ButtonIds.SequenceEqual(["MainMenu"])),
            new Capture("resigned", "After resigning: the screen says so and offers only the main menu"),
            new Act("refresh the world", () => screen.Refresh()),
            new WaitFrames(4),
            new AssertQuery("the resigned screen stays after the world refreshes", () => screen.GameOver.Visible),
        ];
    }
}
