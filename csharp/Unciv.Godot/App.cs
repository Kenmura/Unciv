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
        if (UserPaths.ProfileError is { } profileError)
        {
            GD.PrintErr(profileError);
            GetTree().Quit(2);
            return;
        }
        GetWindow().Title = UserProfile.WindowTitle(UserPaths.Profile);
        vanilla = RulesetLocator.LoadVanilla();
        theme = new UiTheme(AssetRegistry.ForRuleset(vanilla));
        settings = new ClientSettingsService(new ClientSettingsStore(UserPaths.Settings));
        if (GraphicsRuntime.StartupCheck(settings)) return; // C097: restarting into the renderer the graphics setting wants; nothing else is built in this process
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
        screens.BindLocalization(localization);
        screens.CanQuitOnBack = () => activeSession == null && !menuSaves.IsDialogOpen; // C099: in a game back is Escape (the game menu); only the main menu leaves the app
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
        activeSaves = null;
        activeSession = null;
        menu = new MainMenu(theme, UserPaths.Profile);
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
            DisplayName = UserPaths.Profile ?? (System.Environment.UserName is { Length: > 0 } user ? user : "Host"),
            Clock = WallClockMillis,
            SetClipboard = DisplayServer.ClipboardSet,
            Relay = () => RelaySettings.Load(UserPaths.Relay, UserPaths.RelayDefault).HostThroughRelay ? RelaySettings.Load(UserPaths.Relay, UserPaths.RelayDefault).Endpoint : null,
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
        var known = JoinFiles.LoadKnownHosts(UserPaths.KnownHosts);
        var environment = new JoinScreenEnvironment
        {
            Identity = identity,
            DisplayName = UserPaths.Profile ?? (System.Environment.UserName is { Length: > 0 } user ? user : "Player"),
            RulesetHash = () => new LobbySettings { Ruleset = form.BaseRuleset, Mods = [.. form.Mods] }.ComputeRulesetHash(),
            KnownHosts = known,
            PersistKnownHosts = known => JoinFiles.SaveKnownHosts(UserPaths.KnownHosts, known),
            LastAddress = JoinFiles.LoadLastAddress(UserPaths.LastJoin),
            RememberAddress = address => JoinFiles.SaveLastAddress(UserPaths.LastJoin, address),
            Relay = () => RelaySettings.Load(UserPaths.Relay, UserPaths.RelayDefault).Endpoint,
            Connect = (target, token) => ConnectAsync(target, known, token),
            ModFiles = RulesetLocator.FileSource(),
            ModsDirectory = UserPaths.Mods,
        };
        var screen = new JoinScreen(theme, environment);
        screen.BackRequested += () => screens.Pop();
        screen.Joined += session => OpenJoinedLobby(session, identity, ConfigureForSync(form, screen));
        screens.Push(screen);
    }

    // T338: a relay target dials the relay named by the invite (the configured one, or one the player agreed to before); the game's own TLS then runs through it and pins the host key.
    // Asking "use this relay?" for an unknown relay is a UI step that is not built yet (T371); until then an invite's relay is used for this attempt only (nothing is remembered), which exposes only that this player's address met that relay.
    private static async Task<Unciv.Net.ITransport> ConnectAsync(JoinTarget target, KnownHosts known, CancellationToken token)
    {
        if (!target.IsRelay || target.Room is not { } room) return await TcpTlsClientTransport.ConnectAsync(target.Host, target.Port, cancellationToken: token);
        var settings = RelaySettings.Load(UserPaths.Relay, UserPaths.RelayDefault);
        var choice = settings.Choose(target.Host, target.Port);
        return await TcpTlsClientTransport.ConnectViaRelayAsync(choice.Endpoint, room, target.Fingerprint, known, choice.Dial, cancellationToken: token);
    }

    // T247: after a mod download the host's mods are in the ruleset cache but not in the form made before; make the form the host's rules (the nations list and the game's ruleset come from it).
    private static NewGameForm ConfigureForSync(NewGameForm form, JoinScreen screen)
    {
        if (screen.SyncedRules is not { } rules) return form;
        var synced = NewGameForm.FromRulesetCache();
        synced.SetBaseRuleset(rules.BaseRuleset);
        foreach (var mod in rules.Mods) synced.SetMod(mod, true);
        return synced;
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
        var options = new OptionsScreen(settings, theme, localization, null, vanilla);
        options.CloseRequested += () => screens.Pop();
        screens.Push(options);
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
        hotseat.TookOver += next =>
        {
            saves.Attach(next);
            activeSession = next;
        };
        activeSaves = saves;
        activeSession = session;
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

    /// <summary>
    /// C099: Android freezes a backgrounded app (and its connection), and may kill it. Going to the background writes the autosave; coming back asks the game session for the
    /// current state (a networked session reconnects by itself and its overlay shows that; the resync catches up what was missed).
    /// </summary>
    public override void _Notification(int what)
    {
        if (what == NotificationApplicationPaused) _ = activeSaves?.AutosaveNowAsync();
        else if (what == NotificationApplicationResumed && activeSession != null) _ = activeSession.ResyncAsync();
    }

    private SaveController? activeSaves;
    private IGameSession? activeSession;

    private static long WallClockMillis() => (long)(Time.GetUnixTimeFromSystem() * 1000.0);
}
