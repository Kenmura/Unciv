using System.Globalization;
using Godot;
using Unciv.Assets;
using Unciv.Commands;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Play;
using Unciv.GodotClient.Settings;
using Unciv.GodotClient.Setup;
using Unciv.GodotClient.Ui;
using Unciv.Harness;
using Unciv.Client.Net.Hosting;
using Unciv.Client.Net.Session;
using Unciv.Logic.Map;
using Unciv.Net;
using Unciv.Net.Identity;
using Unciv.Net.Lobby;
using Unciv.Server;
using Unciv.Utils;

namespace Unciv.GodotHarness;

/// <summary>
/// T312: the multi-resolution layout matrix. Every main screen (HUD, game menu, city, tech, policies, diplomacy, trade, overview, civilopedia, espionage, and the menu screens: main menu, new game, host, join, options, a
/// modal) is opened in an off-screen viewport of each desktop size (1366x768, 1920x1080, 2560x1440, 3440x1440 ultrawide, 3840x2160) and checked from the real controls, not from pixels: no visible text leaves the window, none
/// is cut off or overlaps another (<see cref="LayoutChecks.Problems"/>), no visible button is under the minimum target height or over the maximum (<see cref="LayoutChecks.TargetProblems"/>). All problems of the run
/// are printed and the run fails at the end when there is any. When <c>UNCIV_MATRIX_OUT</c> names a folder each screen is also written there as <c>layout-WxH-screen.png</c> (a real GPU draws it; never compared to goldens).
/// <c>UNCIV_MATRIX_RES</c> (comma separated WxH or WxH@S) replaces the size list. <c>tools/layout-matrix.sh</c> runs it on the host GPU.
/// </summary>
public sealed class LayoutMatrixScenario : Scenario
{
    private static readonly string Fingerprint = HostKey.Fingerprint([1, 2, 3, 4]);
    private const string DefaultSizes = "1366x768,1920x1080,2560x1440,3440x1440,3840x2160";

    public override string Name => "layout-matrix";

    public override bool Uses3D => true;

    public override bool Solo => true;

    public override bool Trade => true;

    public override bool Espionage => true;

    public override IReadOnlyList<string> Checklist => ["Open layout-*.png: does every screen use the window well at every size (no clipping, no tiny or huge widgets, no empty ultrawide margins, HUD in its corners)?"];

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session)
    {
        var run = new Run(host, session, GpuBenchScenario.Bench("UNCIV_MATRIX_OUT"));
        if (run.OutDir != null) Directory.CreateDirectory(run.OutDir);
        var realProfile = DisplayProfile.Current;
        var steps = new List<ScenarioStep>();
        foreach (var (width, height, os) in ScaleSheetScenario.Sizes("UNCIV_MATRIX_RES", DefaultSizes))
        {
            var at = new At(run, width, height, os);
            steps.AddRange(GameSteps(at));
            steps.AddRange(MenuSteps(at, (a, n, r) => Check(a, n, r)));
        }

        // T316: the same menu screens, opened once and then resized (and rescaled) live
        steps.AddRange(MenuSteps(new At(run, 1366, 768, 1f), ResizeCycle));

        steps.Add(new Act("report", () =>
        {
            DisplayProfile.Current = realProfile;
            foreach (string it in run.Problems) GD.PrintErr($"[layout-matrix] {it}");
            GD.Print($"[layout-matrix] {run.Problems.Count} problem(s)");
        }));
        steps.Add(new AssertQuery("no screen overflows, clips, overlaps or has an undersized or oversized button at any size", () => run.Problems.Count == 0));
        return steps;
    }

    /// <summary>One size: wait for the layout, run the checks, write the picture. The checks only collect: the run reports every size and screen at the end.</summary>
    private static ScenarioStep[] Check(At at, string name, Func<Node> root, Func<BaseButton, bool>? exempt = null) =>
    [
        new WaitFrames(8),
        new Act($"{at.Size} {name}: the scenario's pending alert is not part of the screen", () => at.Screen?.Alerts.Close()),
        new WaitFrames(2),
        new Act($"{at.Size} {name}", () =>
        {
            var t = at.Theme.NotNull();
            var view = at.Sub.NotNull();
            var node = root();
            foreach (string it in LayoutChecks.Problems(node, new Rect2(Vector2.Zero, view.Size))) at.Run.Problems.Add($"{at.Size} {name}: {it.Replace("\n", "\\n")}");
            foreach (string it in LayoutChecks.TargetProblems(node, t.Px(UiTokens.CompactControlMinHeight), t.Px(UiTokens.ControlMinHeight) * 4, exempt == null ? Exempt : it => Exempt(it) || exempt(it))) at.Run.Problems.Add($"{at.Size} {name}: {it}");
            if (at.Run.OutDir != null) Save(view, Path.Combine(at.Run.OutDir, $"layout-{at.Size}-{name}.png"));
        }),
    ];

    private static List<ScenarioStep> GameSteps(At at)
    {
        var steps = new List<ScenarioStep>
        {
            new Act($"{at.Size}: build the game screen", () =>
            {
                DisplayProfile.Current = new DisplayProfile(false, 0f, at.Os);
                var sub = at.Sub = new SubViewport { Size = new Vector2I(at.Width, at.Height), RenderTargetUpdateMode = SubViewport.UpdateMode.Always, TransparentBg = false, World3D = at.Run.Host.GetViewport().FindWorld3D() };
                at.Run.Host.AddChild(sub);
                at.Local = new LocalHost(at.Run.Session, ScenarioGame.HumanA, () => at.Now += 1000);
                var screen = at.Screen = new GameScreen();
                sub.AddChild(screen);
                screen.Start(at.Local, at.Run.Registry);
                at.Theme = screen.SharedTheme;
            }),
            new WaitFrames(6),
            new Act($"{at.Size}: dismiss the banner and the pending alert", () =>
            {
                at.Screen.NotNull().Hud.HideBanner();
                at.Screen.NotNull().Alerts.Close();
            }),
        };
        GameScreen S() => at.Screen.NotNull();
        steps.AddRange(Check(at, "hud", S));
        steps.Add(new Act("open the menu", () => S().OpenGameMenu()));
        steps.AddRange(Check(at, "menu", () => S().GameMenu));
        steps.Add(new Act("close the menu", () => S().GameMenu.Close()));
        steps.Add(new Act("open the city", () => S().OpenCityScreen(at.Local.NotNull().Snapshot.Cities.First(it => it.Own != null))));
        steps.AddRange(Check(at, "city", () => S().City));
        steps.Add(new Act("close the city", () => S().OpenCityScreen(null)));
        steps.Add(new Act("open tech", () => S().OpenTechScreen()));
        steps.AddRange(Check(at, "tech", () => S().Tech, it => it.GetType().Name.Contains("Card", StringComparison.Ordinal)));
        steps.Add(new Act("close tech", () => S().Tech.Close()));
        steps.Add(new Act("open policies", () => S().OpenPolicyScreen()));
        steps.AddRange(Check(at, "policies", () => S().Policies));
        steps.Add(new Act("close policies", () => S().Policies.Close()));
        steps.Add(new Act("open diplomacy", () => S().OpenDiplomacyScreen()));
        steps.AddRange(Check(at, "diplomacy", () => S().Diplomacy));
        steps.Add(new Act("close diplomacy", () => S().Diplomacy.Close()));
        steps.Add(new Act("open trade", () => S().OpenTradeScreen()));
        steps.AddRange(Check(at, "trade", () => S().Trade));
        steps.Add(new Act("close trade", () => S().Trade.Close()));
        foreach (string tab in new[] { OverviewModel.Stats, OverviewModel.Cities, OverviewModel.Units })
        {
            steps.Add(new Act("open overview " + tab, () => S().OpenOverviewScreen(tab)));
            steps.AddRange(Check(at, "overview-" + tab.ToLowerInvariant(), () => S().Overview));
        }
        steps.Add(new Act("close overview", () => S().Overview.Close()));
        steps.Add(new Act("open espionage", () => S().OpenEspionageScreen()));
        steps.AddRange(Check(at, "espionage", () => S().Espionage));
        steps.Add(new Act("close espionage", () => S().Espionage.Close()));
        steps.Add(new Act("open the pedia", () => S().OpenPediaScreen("Unit/Warrior")));
        steps.AddRange(Check(at, "pedia", () => S().Pedia));
        steps.Add(new Act("close the pedia", () => S().Pedia.Close()));
        steps.Add(new Act($"{at.Size}: free the game screen", () =>
        {
            at.Sub.NotNull().QueueFree();
            at.Screen = null;
        }));
        steps.Add(new WaitFrames(2));
        return steps;
    }

    /// <summary>The menu screens, through a screen manager over the same size.</summary>
    private static List<ScenarioStep> MenuSteps(At at, Func<At, string, Func<Node>, ScenarioStep[]> check)
    {
        var run = at.Run;
        var steps = new List<ScenarioStep>
        {
            new Act($"{at.Size}: build the menus", () =>
            {
                var sub = at.Sub = new SubViewport { Size = new Vector2I(at.Width, at.Height), RenderTargetUpdateMode = SubViewport.UpdateMode.Always, TransparentBg = false };
                run.Host.AddChild(sub);
                sub.AddChild(new ColorRect { Color = new Color(0.12f, 0.14f, 0.2f), AnchorRight = 1, AnchorBottom = 1 });
                var theme = at.Theme = new UiTheme(run.Registry);
                at.Manager = new ScreenManager(theme);
                sub.AddChild(at.Manager);
                at.Manager.Push(new MainMenu(theme));
            }),
        };
        ScreenManager M() => at.Manager.NotNull();
        UiTheme T() => at.Theme.NotNull();
        steps.AddRange(check(at, "main-menu", () => M().Top.NotNull()));
        steps.Add(new Act("open new game", () => M().Push(new NewGameScreen(T(), NewGameForm.FromRulesetCache(), _ => throw new InvalidOperationException("not started")))));
        steps.AddRange(check(at, "new-game", () => M().Top.NotNull()));
        steps.Add(new Act("back", () => M().Pop()));
        steps.Add(new Act("open host", () => M().Push(new HostScreen(T(), new HostSettingsForm(NewGameForm.FromRulesetCache()), new HostScreenEnvironment
        {
            Identity = () => run.Identity,
            Certificate = () => throw new InvalidOperationException("not hosting"),
            HostedGamesDirectory = run.Temp,
            DisplayName = "Host",
            Clock = () => 5000,
            SetClipboard = _ => { },
            Addresses = () => [new LocalAddress("192.168.1.20", "Wi-Fi", AddressScope.Lan)],
        }))));
        steps.AddRange(check(at, "host", () => M().Top.NotNull()));
        steps.Add(new Act("back", () => M().Pop()));
        steps.Add(new Act("open the lobby", () =>
        {
            var lobbySettings = new LobbySettings { GameName = "Friday night", Ruleset = "Civ V - Vanilla", Difficulty = "King", MaxMajorCivs = 4, CityStateCount = 0, AiFill = true, Map = new MapParameters { Type = MapType.Pangaea, Shape = MapShape.Rectangular, Seed = 3, MapSize = MapSize.Tiny } };
            var lobby = at.Lobby = new ServerLobby(lobbySettings, hostFingerprint: Fingerprint, hostName: "Host");
            var (client, hostEnd) = InMemoryTransport.CreatePair();
            _ = lobby.AttachConnectionAsync(hostEnd);
            var clientSession = at.LobbySession = new ClientSession(_ => Task.FromResult<ITransport>(client), new ClientSessionOptions { Identity = run.Identity, DisplayName = "Host", RulesetHash = lobbySettings.ComputeRulesetHash(), ExpectedFingerprint = Fingerprint, MaxReconnectAttempts = 0 });
            at.Joined = clientSession.JoinAsync();
        }));
        steps.Add(new WaitUntil("the host joined its lobby", () => at.Joined.NotNull().IsCompleted && at.Joined.NotNull().Result.Joined, 3000));
        steps.Add(new Act("show the lobby", () => M().Push(new LobbyScreen(T(), NewGameForm.FromRulesetCache(), at.LobbySession.NotNull(), run.Identity.PlayerKey, at.Lobby.NotNull()))));
        steps.AddRange(check(at, "lobby", () => M().Top.NotNull()));
        steps.Add(new Act("back", () => M().Pop()));
        steps.Add(new Act("open join", () => M().Push(new JoinScreen(T(), new JoinScreenEnvironment
        {
            Identity = run.Identity,
            DisplayName = "Friend",
            RulesetHash = () => "0",
            KnownHosts = new KnownHosts(),
            PersistKnownHosts = _ => { },
            Connect = (_, _) => throw new InvalidOperationException("not connecting"),
        }))));
        steps.AddRange(check(at, "join", () => M().Top.NotNull()));
        steps.Add(new Act("back", () => M().Pop()));
        steps.Add(new Act("open options", () => M().Push(new OptionsScreen(run.Settings, T(), null, ["Retro", "Neon"]))));
        steps.AddRange(check(at, "options", () => M().Top.NotNull()));
        steps.Add(new Act("back", () => M().Pop()));
        steps.Add(new Act("show the shared widgets", () => M().Push(at.Gallery = new WidgetGallery(T()))));
        steps.AddRange(check(at, "widgets", () => M().Top.NotNull()));
        steps.Add(new Act("back", () => M().Pop()));
        steps.Add(new Act("show a modal", () => M().Popups.Show(new PopupSpec("Leave the game?", "Progress since the last save is lost.", [new PopupChoice("Cancel"), new PopupChoice("Leave", null, ChoiceRole.Primary)]))));
        steps.AddRange(check(at, "modal", () => M().Popups));
        steps.Add(new Act($"{at.Size}: free the menus", () => at.Sub.NotNull().QueueFree()));
        steps.Add(new WaitFrames(2));
        return steps;
    }

    /// <summary>
    /// T316: a screen already open is checked at its size, then the viewport is resized to 4K and back to 1366x768 and the UI scale is changed and restored, checking after each: the screen has to follow the live
    /// window (a width taken from the theme at construction would stay what it was and overflow or waste the window).
    /// </summary>
    private static ScenarioStep[] ResizeCycle(At at, string name, Func<Node> root)
    {
        var steps = new List<ScenarioStep>();
        void Phase(string label, Action change)
        {
            steps.Add(new Act($"{name}: {label}", () =>
            {
                at.Size = "resize-" + label;
                change();
            }));
            steps.AddRange(Check(at, name, root));
            steps.Add(new AssertQuery($"{name} {label}: the main menu column follows the live scale", () => name != "main-menu" || MenuColumnWidth(root()) == at.Theme.NotNull().Px(260)));
            // T323: the join column and the shared widgets (list row, list panel, icon label, stat chip, stat bar, tooltip) and the modal are at the live scale, not the one they were built at
            steps.Add(new AssertQuery($"{name} {label}: the join column follows the live scale", () => name != "join" || ColumnWidth(root(), "JoinColumn") == at.Theme.NotNull().Px(420)));
            steps.Add(new AssertQuery($"{name} {label}: the shared widgets follow the live scale", () =>
            {
                if (name != "widgets") return true;
                var wrong = at.Gallery.NotNull().Wrong(at.Theme.NotNull());
                foreach (string it in wrong) GD.PrintErr($"[layout-matrix] {at.Size} widgets: {it}");
                return wrong.Count == 0;
            }));
            steps.Add(new AssertQuery($"{name} {label}: the modal panel is at the live width", () =>
            {
                if (name != "modal") return true;
                var ui = at.Theme.NotNull();
                var modal = root().FindChildren("*", "", true, false).OfType<Modal>().FirstOrDefault();
                return modal != null && Math.Abs(modal.Frame.CustomMinimumSize.X - Math.Min(ui.Px(Modal.MinWidth), at.Sub.NotNull().Size.X * Modal.ViewportShare)) < 0.5f;
            }));
        }
        Phase("1366x768", () => { });
        Phase("3840x2160", () => at.Sub.NotNull().Size = new Vector2I(3840, 2160));
        Phase("1366x768-again", () => at.Sub.NotNull().Size = new Vector2I(1366, 768));
        Phase("scale-1.5", () => at.Theme.NotNull().SetScale(1.5f));
        Phase("scale-1", () => at.Theme.NotNull().SetScale(1f));
        return [.. steps];
    }

    private static float ColumnWidth(Node screen, string name) => screen.FindChild(name, true, false) is Control column ? column.CustomMinimumSize.X : -1f;

    private static float MenuColumnWidth(Node menu) => menu.FindChild("MenuColumn", true, false) is Control column ? column.CustomMinimumSize.X : -1f;

    /// <summary>What the whole run shares: the game, the host control, where pictures go and the problems found.</summary>
    private sealed class Run(Control host, GameSession session, string? outDir)
    {
        public Control Host { get; } = host;

        public GameSession Session { get; } = session;

        public string? OutDir { get; } = outDir;

        public List<string> Problems { get; } = [];

        public AssetRegistry Registry { get; } = AssetRegistry.ForRuleset(session.Game.Ruleset);

        public string Temp { get; } = Directory.CreateTempSubdirectory("unciv-layout-matrix-").FullName;

        public ClientSettingsService Settings => settings ??= new ClientSettingsService(new ClientSettingsStore(Path.Combine(Temp, "settings.json")));

        public ClientIdentity Identity { get; } = ClientIdentity.Generate();

        private ClientSettingsService? settings;
    }

    /// <summary>The state of one window size: its viewport and the screens built in it.</summary>
    private sealed class At(Run run, int width, int height, float os)
    {
        public Run Run { get; } = run;

        public int Width { get; } = width;

        public int Height { get; } = height;

        public float Os { get; } = os;

        public string Size { get; set; } = $"{width}x{height}" + (os > 1f ? "@" + os.ToString(CultureInfo.InvariantCulture) : "");

        public SubViewport? Sub { get; set; }

        public GameScreen? Screen { get; set; }

        public UiTheme? Theme { get; set; }

        public ScreenManager? Manager { get; set; }

        public LocalHost? Local { get; set; }

        public long Now { get; set; } = 1000;

        public Task<JoinResult>? Joined { get; set; }

        public ClientSession? LobbySession { get; set; }

        public ServerLobby? Lobby { get; set; }

        public WidgetGallery? Gallery { get; set; }
    }

    /// <summary>Not judged by the target-height check: the city grid's tile buttons (map cells, not controls to hit) and check boxes (a label row whose height is its text line).</summary>
    private static bool Exempt(BaseButton button) => button is CityTileButton or CheckBox;

    private static void Save(SubViewport viewport, string path)
    {
        using var image = viewport.GetTexture().GetImage() ?? throw new IOException($"no image for {path}");
        var error = image.SavePng(path);
        if (error != Godot.Error.Ok) throw new IOException($"Could not write {path}: {error}");
    }
}
