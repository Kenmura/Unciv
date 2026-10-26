using Godot;
using Unciv.Assets.Localization;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Localization;
using Unciv.GodotClient.Play;
using Unciv.GodotClient.Saves;
using Unciv.GodotClient.Settings;
using Unciv.GodotClient.Setup;
using Unciv.GodotClient.Ui;
using Unciv.Models.Translations;

namespace Unciv.GodotClient;

/// <summary>
/// C015: the composition root of the real client. Main menu, then New game (<see cref="NewGameScreen"/> over a <see cref="NewGameForm"/>, started through
/// <see cref="LocalHost.Start"/>), Resume last / Load game (C016 <see cref="SaveController"/> over <see cref="SaveStore"/>) or Options (the C043
/// settings panel, which saves through <see cref="ClientSettingsService"/>). A started or loaded game replaces the whole screen stack; every game gets its own
/// <see cref="SaveController"/> for saving. The game-over screen (C024) returns to a fresh menu; a pause menu is a later ticket.
/// </summary>
public partial class App : Node
{
    private ScreenManager screens = null!; // JUSTIFY: C015 assigned in _Ready before any use
    private UiTheme theme = null!; // JUSTIFY: C015 assigned in _Ready before any use
    private ClientSettingsService settings = null!; // JUSTIFY: C015 assigned in _Ready before any use
    private ClientLocalization localization = null!; // JUSTIFY: C025 assigned in _Ready before any use
    private SaveStore store = null!; // JUSTIFY: C015 assigned in _Ready before any use
    private SaveController menuSaves = null!; // JUSTIFY: C015 assigned in _Ready before any use
    private MainMenu menu = null!; // JUSTIFY: C015 assigned in _Ready before any use

    public override void _Ready()
    {
        var vanilla = RulesetLocator.LoadVanilla();
        theme = new UiTheme(AssetRegistry.ForRuleset(vanilla));
        settings = new ClientSettingsService(new ClientSettingsStore(UserPaths.Settings));
        localization = Loc.Init(settings, GetWindow());
        store = new SaveStore(UserPaths.Saves);
        screens = new ScreenManager(theme);
        AddChild(screens);

        menuSaves = new SaveController(store, settings, theme, text => LocalHost.FromSave(text, WallClockMillis), ShowGame) { ButtonVisible = false };
        screens.AddChild(menuSaves);
        ShowMenu(first: true);

        // C042: the packaged-build smoke test (tools/smoke-export.sh) waits for this line and asks the app to exit once the menu is up.
        GD.Print(MenuReadyMarker);
        if (OS.GetCmdlineUserArgs().Contains(ExitOnMenuArg)) GetTree().Quit();
    }

    // The menu is rebuilt each time (leaving a game frees the screen stack, menu included); the same save controller serves every menu.
    private void ShowMenu(bool first)
    {
        menu = new MainMenu();
        menu.NewGameRequested += OpenNewGame;
        menu.ResumeRequested += () => _ = menuSaves.ResumeLastAsync();
        menu.LoadRequested += menuSaves.OpenDialog;
        menu.OptionsRequested += OpenOptions;
        menu.QuitRequested += () => GetTree().Quit();
        menu.SetCanResume(store.Latest() != null);
        if (first) screens.Push(menu);
        else screens.Reset(menu);
    }

    public const string MenuReadyMarker = "UNCIV_MENU_READY";
    public const string ExitOnMenuArg = "--exit-on-menu";

    private void OpenNewGame()
    {
        var screen = new NewGameScreen(theme, NewGameForm.FromRulesetCache(), form => LocalHost.Start(form, WallClockMillis));
        screen.Started += ShowGame;
        screen.BackRequested += () => screens.Pop();
        screens.Push(screen);
    }

    private void OpenOptions()
    {
        var page = new VBoxContainer();
        page.AddChild(new SettingsPanel(settings, localization));
        var back = new Button { Text = "Back".Tr() };
        back.Pressed += () => screens.Pop();
        page.AddChild(back);
        screens.Push(page);
    }

    // A new or loaded game takes over the screen; its own save controller keeps autosaving it.
    private void ShowGame(IGameSession session)
    {
        var registry = AssetRegistry.ForRuleset(session.Ruleset);
        var root = new Control { Name = "Play" };
        var screen = new GameScreen { Name = "GameScreen" };
        root.AddChild(screen);
        var saves = new SaveController(store, settings, new UiTheme(registry),
            text => LocalHost.FromSave(text, WallClockMillis), loaded => screen.Start(loaded, AssetRegistry.ForRuleset(loaded.Ruleset)));
        root.AddChild(saves);
        screens.Reset(root);
        screen.MainMenuRequested += () => ShowMenu(first: false);
        screen.Start(session, registry);
        saves.Attach(session);
    }

    private static long WallClockMillis() => (long)(Time.GetUnixTimeFromSystem() * 1000.0);
}
