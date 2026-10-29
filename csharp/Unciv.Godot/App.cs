using Godot;
using Unciv.Assets.Localization;
using Unciv.Client.Net.Hosting;
using Unciv.Client.Net.Session;
using Unciv.Client.Net.Transport;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Localization;
using Unciv.GodotClient.Play;
using Unciv.GodotClient.Saves;
using Unciv.GodotClient.Settings;
using Unciv.GodotClient.Setup;
using Unciv.GodotClient.Ui;
using Unciv.Models;
using Unciv.Net.Identity;
using Unciv.Net.Lobby;
using Unciv.Utils;
using Unciv.Models.Translations;

namespace Unciv.GodotClient;

/// <summary>
/// C015: the composition root of the real client. Main menu, then New game (<see cref="NewGameScreen"/> over a <see cref="NewGameForm"/>, started through
/// <see cref="LocalHost.Start"/>), Host game (C034 <see cref="HostScreen"/>), Resume last / Load game (C016 <see cref="SaveController"/> over <see cref="SaveStore"/>) or Options (the C043
/// settings panel, which saves through <see cref="ClientSettingsService"/>). A started or loaded game replaces the whole screen stack; every game gets its own
/// <see cref="SaveController"/> for saving. The game-over screen (C024) returns to a fresh menu; C044: the in-game menu (Escape or the Menu button) saves, loads, starts a new game, resigns, shows the victory status, opens the options and leaves for the main menu.
/// </summary>
public partial class App : Node
{
    private ScreenManager screens = null!; // JUSTIFY: C015 assigned in _Ready before any use
    private UiTheme theme = null!; // JUSTIFY: C015 assigned in _Ready before any use
    private ClientSettingsService settings = null!; // JUSTIFY: C015 assigned in _Ready before any use
    private ClientLocalization localization = null!; // JUSTIFY: C025 assigned in _Ready before any use
    private SoundPlayer sounds = null!; // JUSTIFY: C030 assigned in _Ready before any use
    private SaveStore store = null!; // JUSTIFY: C015 assigned in _Ready before any use
    private SaveController menuSaves = null!; // JUSTIFY: C015 assigned in _Ready before any use
    private MainMenu menu = null!; // JUSTIFY: C015 assigned in _Ready before any use

    private Unciv.Models.Ruleset.Ruleset vanilla = null!; // JUSTIFY: C026 assigned in _Ready before any use

    public override void _Ready()
    {
        vanilla = RulesetLocator.LoadVanilla();
        theme = new UiTheme(AssetRegistry.ForRuleset(vanilla));
        settings = new ClientSettingsService(new ClientSettingsStore(UserPaths.Settings));
        localization = Loc.Init(settings, GetWindow());
        ClientSettingsApplier.Apply(settings.Current, GetWindow());
        settings.Changed += changed => ClientSettingsApplier.Apply(changed, GetWindow());
        sounds = new SoundPlayer(AssetRegistry.ForRuleset(vanilla, OptionsModel.RegistryPack(settings.Current.AssetPack)));
        AddChild(sounds);
        GetTree().NodeAdded += node =>
        {
            if (node is BaseButton button) button.Pressed += () => sounds.Play(UncivSound.Click);
        };
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
        menu.JoinRequested += OpenJoin;
        menu.HostRequested += OpenHost;
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

    // C034: the host screen; it stops hosting itself when it leaves the stack.
    private void OpenHost()
    {
        var environment = new HostScreenEnvironment
        {
            Identity = () => HostFiles.LoadIdentity(UserPaths.Identity),
            Certificate = () => HostFiles.LoadCertificate(UserPaths.HostKey),
            HostedGamesDirectory = UserPaths.HostedGames,
            DisplayName = System.Environment.UserName is { Length: > 0 } user ? user : "Host",
            Clock = WallClockMillis,
            SetClipboard = DisplayServer.ClipboardSet,
        };
        var screen = new HostScreen(theme, new HostSettingsForm(NewGameForm.FromRulesetCache()), environment);
        screen.BackRequested += () => screens.Pop();
        screen.HostingStarted += hosting => OpenLobby(screen, hosting, environment);
        screens.Push(screen);
    }

    // C035: a new lobby opens the lobby screen over the host screen (which keeps the address and invite and stops hosting); Leave returns to it.
    private void OpenLobby(HostScreen hostScreen, HostingSession hosting, HostScreenEnvironment environment)
    {
        if (hosting.Lobby == null || hosting.HostPlayer == null) return;
        var nations = hostScreen.Form.Game;
        var lobby = new LobbyScreen(theme, nations, hosting.HostPlayer, environment.Identity().PlayerKey, hosting.Lobby);
        lobby.LeaveRequested += () => screens.Pop();
        screens.Push(lobby);
    }

    // C006: the join screen; a joined session gets the lobby screen (the same one the host sees, without host controls) and, once the host starts, the game.
    private void OpenJoin()
    {
        var identity = HostFiles.LoadIdentity(UserPaths.Identity);
        var form = NewGameForm.FromRulesetCache();
        var environment = new JoinScreenEnvironment
        {
            Identity = identity,
            DisplayName = System.Environment.UserName is { Length: > 0 } user ? user : "Player",
            RulesetHash = () => new LobbySettings { Ruleset = form.BaseRuleset, Mods = [.. form.Mods] }.ComputeRulesetHash(),
            KnownHosts = JoinFiles.LoadKnownHosts(UserPaths.KnownHosts),
            PersistKnownHosts = known => JoinFiles.SaveKnownHosts(UserPaths.KnownHosts, known),
            LastAddress = JoinFiles.LoadLastAddress(UserPaths.LastJoin),
            RememberAddress = address => JoinFiles.SaveLastAddress(UserPaths.LastJoin, address),
            Connect = async (target, token) => await TcpTlsClientTransport.ConnectAsync(target.Host, target.Port, cancellationToken: token),
        };
        var screen = new JoinScreen(theme, environment);
        screen.BackRequested += () => screens.Pop();
        screen.Joined += session => OpenJoinedLobby(session, identity, form);
        screens.Push(screen);
    }

    private void OpenJoinedLobby(ClientSession session, ClientIdentity identity, NewGameForm form)
    {
        var lobby = new LobbyScreen(theme, form, session, identity.PlayerKey);
        lobby.LeaveRequested += () =>
        {
            _ = session.LeaveAsync();
            screens.Pop();
        };
        lobby.GameStarting += () => _ = StartJoinedGameAsync(session, lobby, form);
        screens.Push(lobby);
    }

    // The host announced the start: the snapshot follows within moments. The lobby stops reading the event channel first (it has one reader), then the network session takes over.
    private async Task StartJoinedGameAsync(ClientSession session, LobbyScreen lobby, NewGameForm form)
    {
        lobby.SetProcess(false);
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            while (session.Game.Snapshot == null) await Task.Delay(50, limit.Token);
        }
        catch (OperationCanceledException)
        {
            GD.PushWarning("The host started the game but sent no snapshot");
            await session.DisposeAsync();
            ShowMenu(first: false);
            return;
        }
        var ruleset = Unciv.Models.Ruleset.RulesetCache.GetComplexRuleset(new LinkedHashSet<string>(form.Mods), form.BaseRuleset);
        var net = new NetGameSession(session, ruleset);
        ShowGame(net, async () =>
        {
            await net.DisposeAsync();
            await session.LeaveAsync();
            await session.DisposeAsync();
        });
    }

    private void OpenOptions()
    {
        var page = new VBoxContainer();
        var options = new OptionsScreen(settings, localization, null, vanilla) { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        page.AddChild(options);
        var back = new Button { Text = "Back".Tr() };
        back.Pressed += () => screens.Pop();
        page.AddChild(back);
        screens.Push(page);
    }

    // A new or loaded game takes over the screen; its own save controller keeps autosaving it.
    private void ShowGame(IGameSession session) => ShowGame(session, null);

    private void ShowGame(IGameSession session, Func<Task>? leaveNetwork)
    {
        var registry = AssetRegistry.ForRuleset(session.Ruleset, OptionsModel.RegistryPack(settings.Current.AssetPack));
        var root = new Control { Name = "Play", MouseFilter = Control.MouseFilterEnum.Ignore };
        var screen = new GameScreen { Name = "GameScreen", PlaySound = sounds.PlaySound };
        screen.Presenter.Enabled = true;
        screen.TaskCardEnabled = true;
        root.AddChild(screen);
        // C029: hotseat. The cover sits above the game screen and everything it owns (the save dialog included), and the controller swaps the viewer.
        var cover = new HandOverView();
        cover.Init(new UiTheme(registry));
        HotseatController hotseat = null!; // JUSTIFY: C029 assigned before any callback can run
        var saves = new SaveController(store, settings, new UiTheme(registry),
            text => LocalHost.FromSave(text, WallClockMillis), loaded => hotseat.Present(loaded));
        root.AddChild(saves);
        root.AddChild(cover);
        // C006: a networked game gets the reconnect cover above everything; Leave there ends the network session and returns to the menu.
        if (session is NetGameSession netSession)
        {
            var reconnect = new ReconnectOverlay(theme);
            reconnect.LeaveRequested += () =>
            {
                _ = leaveNetwork?.Invoke();
                ShowMenu(first: false);
            };
            root.AddChild(reconnect);
            reconnect.Attach(netSession);
        }
        hotseat = new HotseatController(screen, cover, loaded => AssetRegistry.ForRuleset(loaded.Ruleset, OptionsModel.RegistryPack(settings.Current.AssetPack)), id => id);
        root.AddChild(hotseat);
        hotseat.TookOver += next => saves.Attach(next);
        screens.Reset(root);
        screen.MainMenuRequested += () => ShowMenu(first: false);
        screen.SaveRequested += saves.OpenDialog;
        screen.LoadRequested += saves.OpenDialog;
        screen.NewGameRequested += OpenNewGame;
        screen.OptionsRequested += OpenOptions;
        screen.HasUnsavedProgress = () => saves.HasUnsavedProgress;
        hotseat.Present(session);
        screen.BindSettings(settings);
        saves.Attach(session);
    }

    private static long WallClockMillis() => (long)(Time.GetUnixTimeFromSystem() * 1000.0);
}
