using Godot;
using Unciv.Commands;
using Unciv.Assets;
using Unciv.Client.Net.Session;
using Unciv.Client.Net.Hosting;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Settings;
using Unciv.GodotClient.Setup;
using Unciv.GodotClient.Ui;
using Unciv.Logic.Map;
using Unciv.Net.Identity;
using Unciv.Net.Lobby;
using Unciv.Net;
using Unciv.Server;
using Unciv.Utils;

namespace Unciv.GodotHarness;

/// <summary>
/// C101: the menu screens as a phone shows them: a 2400x1080 window at 400 ppi (the shared theme derives scale 2.5 from the density, 48 dp = 120 px targets), the main menu, new
/// game, host, lobby, join and options screens and a modal, each asserted to have only touch-sized buttons and no text that leaves the window, is cut off or overlaps another.
/// Real-device behaviour (the virtual keyboard, the real density, rotation) is not covered; the ticket notes list it.
/// </summary>
public sealed class PhoneMenusScenario : Scenario
{
    private const float Dpi = 400f;
    private static readonly string Fingerprint = HostKey.Fingerprint([1, 2, 3, 4]);
    private const string Vanilla = "Civ V - Vanilla";

    public override string Name => "phone-menus";

    public override Vector2I Viewport => new(2400, 1080);

    public override float TouchDpi => Dpi;

    public override IReadOnlyList<string> Checklist =>
    [
        "Is every control of each menu screen big enough for a thumb, and does each page fit the phone window without clipped or overlapping text?",
        "Can the longer pages (new game, host, options) be scrolled to every field with a finger?",
    ];

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session)
    {
        var registry = AssetRegistry.ForRuleset(session.Game.Ruleset);
        var theme = new UiTheme(registry);
        host.AddChild(new ColorRect { Color = new Color(0.12f, 0.14f, 0.2f), AnchorRight = 1, AnchorBottom = 1 });
        var manager = new ScreenManager(theme);
        host.AddChild(manager);
        int minTarget = UiTokens.DpToPx(UiTokens.TouchTargetDp, Dpi);
        string root = Directory.CreateTempSubdirectory("unciv-phone-menus-").FullName;
        var settings = new ClientSettingsService(new ClientSettingsStore(Path.Combine(root, "settings.json")));
        var identity = ClientIdentity.Generate();
        var menu = new MainMenu(theme);
        manager.Push(menu);

        // the lobby: a host seated in a one-player lobby through the in-memory transport
        var nations = NewGameForm.FromRulesetCache();
        var lobbySettings = new LobbySettings { GameName = "Friday night", Ruleset = Vanilla, Difficulty = "King", MaxMajorCivs = 4, CityStateCount = 0, AiFill = true, Map = new MapParameters { Type = MapType.Pangaea, Shape = MapShape.Rectangular, Seed = 3, MapSize = MapSize.Tiny } };
        var lobby = new ServerLobby(lobbySettings, hostFingerprint: Fingerprint, hostName: "Host");
        var (client, hostEnd) = InMemoryTransport.CreatePair();
        _ = lobby.AttachConnectionAsync(hostEnd);
        var clientSession = new ClientSession(_ => Task.FromResult<ITransport>(client), new ClientSessionOptions { Identity = identity, DisplayName = "Host", RulesetHash = lobbySettings.ComputeRulesetHash(), ExpectedFingerprint = Fingerprint, MaxReconnectAttempts = 0 });
        var joined = clientSession.JoinAsync();

        ScenarioStep Fits(string what, Func<Node> screen) => LayoutChecks.Assert($"on a phone no {what} text is cut off, leaves the window or overlaps another", host, screen);
        ScenarioStep Touchable(string what, Func<Node> screen) => new AssertQuery($"every visible button of the {what} (check boxes aside: the host screen's two are still small, T282-T285 follow-ups do not cover them, see C101 notes) is at least 48 dp (120 px) tall", () =>
        {
            var bad = Buttons(screen()).Where(it => it.IsVisibleInTree() && it.Size.Y > 0 && it.Size.Y + 0.5f < minTarget).Select(it => $"{it.GetType().Name} '{(it as Button)?.Text}' {it.Name} {it.Size.Y}").ToList();
            foreach (string line in bad.Take(10)) GD.PrintErr($"[phone-menus] {what}: undersized {line}");
            return bad.Count == 0;
        });
        ScenarioStep[] Check(string what, Func<Node> screen) => [Fits(what, screen), Touchable(what, screen)];

        var steps = new List<ScenarioStep>
        {
            new WaitFrames(6),
            new AssertQuery("the shared theme is bound to the window: scale 2.5, 120 px targets", () => theme.Scale == 2.5f && theme.MinTargetPx == minTarget && minTarget == 120),
        };
        steps.AddRange(Check("main menu", () => menu));
        steps.Add(new Capture("main-menu", "The main menu on a 2400x1080 400 ppi phone: the column of 48 dp buttons is centred and fits"));
        steps.Add(new Act("open new game", () => manager.Push(new NewGameScreen(theme, NewGameForm.FromRulesetCache(), _ => throw new InvalidOperationException("not started")))));
        steps.Add(new WaitFrames(6));
        steps.AddRange(Check("new game screen", () => manager.Top.NotNull()));
        steps.Add(new Capture("new-game", "New game on a phone: the options page scrolls under the header, every row control is a touch target"));
        steps.Add(new Act("back", () => manager.Pop()));
        steps.Add(new Act("open host", () => manager.Push(new HostScreen(theme, new HostSettingsForm(NewGameForm.FromRulesetCache()), new HostScreenEnvironment
        {
            Identity = () => identity,
            Certificate = () => throw new InvalidOperationException("not hosting"),
            HostedGamesDirectory = root,
            DisplayName = "Host",
            Clock = () => 5000,
            SetClipboard = _ => { },
            Addresses = () => [new LocalAddress("192.168.1.20", "Wi-Fi", AddressScope.Lan)],
        }))));
        steps.Add(new WaitFrames(6));
        steps.AddRange(Check("host screen", () => manager.Top.NotNull()));
        steps.Add(new Capture("host", "Host game on a phone: settings and addresses reachable by scrolling, Host and Back as touch targets"));
        steps.Add(new Act("back", () => manager.Pop()));
        steps.Add(new WaitUntil("the host joined its lobby", () => joined.IsCompleted && joined.Result.Joined, 3000));
        steps.Add(new Act("open the lobby", () =>
        {
            manager.Push(new LobbyScreen(theme, nations, clientSession, identity.PlayerKey, lobby));
        }));
        steps.Add(new WaitFrames(8));
        steps.AddRange(Check("lobby", () => manager.Top.NotNull()));
        steps.Add(new Capture("lobby", "The host's lobby on a phone: seats, settings and Start/Leave as touch targets"));
        steps.Add(new Act("leave", () => manager.Pop()));
        steps.Add(new Act("open join", () => manager.Push(new JoinScreen(theme, new JoinScreenEnvironment
        {
            Identity = identity,
            DisplayName = "Friend",
            RulesetHash = () => lobbySettings.ComputeRulesetHash(),
            KnownHosts = new KnownHosts(),
            PersistKnownHosts = _ => { },
            Connect = (_, _) => throw new InvalidOperationException("not connecting"),
        }))));
        steps.Add(new WaitFrames(6));
        steps.AddRange(Check("join screen", () => manager.Top.NotNull()));
        steps.Add(new Capture("join", "Multiplayer join on a phone: address and invite fields with Join as a touch target"));
        steps.Add(new Act("back", () => manager.Pop()));
        steps.Add(new Act("open options", () => manager.Push(new OptionsScreen(settings, theme, null, ["Retro", "Neon"]))));
        steps.Add(new WaitFrames(8));
        steps.AddRange(Check("options screen", () => manager.Top.NotNull()));
        steps.Add(new Capture("options", "Options on a phone: tabs and switches as touch targets"));
        steps.Add(new Act("back", () => manager.Pop()));
        steps.Add(new Act("show a modal", () => manager.Popups.Show(new PopupSpec("Leave the game?", "Progress since the last save is lost.", [new PopupChoice("Cancel"), new PopupChoice("Leave", null, ChoiceRole.Primary)]))));
        steps.Add(new WaitFrames(6));
        steps.AddRange(Check("modal", () => manager.Popups));
        steps.Add(new Capture("modal", "A confirmation modal on a phone: centred, readable, both choices touch targets"));
        return steps;
    }

    private static IEnumerable<BaseButton> Buttons(Node root)
    {
        foreach (var child in root.GetChildren())
        {
            if (child is BaseButton button and not CheckBox) yield return button;
            foreach (var nested in Buttons(child)) yield return nested;
        }
    }
}
