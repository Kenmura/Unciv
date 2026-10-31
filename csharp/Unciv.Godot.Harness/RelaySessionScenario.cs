using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Godot;
using Unciv.Client.Net.Hosting;
using Unciv.Client.Net.Session;
using Unciv.Client.Net.Transport;
using Unciv.Commands;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Play;
using Unciv.GodotClient.Setup;
using Unciv.GodotClient.Ui;
using Unciv.Models.Ruleset;
using Unciv.Net.Identity;
using Unciv.Net.Lobby;
using Unciv.Net.Relay;
using Unciv.Relay;
using Unciv.Server.Transport;
using Unciv.Utils;

namespace Unciv.GodotHarness;

/// <summary>
/// T372: a host and a joiner through a local Unciv.Relay, by clicking. The real relay server (the same code as the Unciv.Relay program, self-signed TLS, an ephemeral loopback port) runs inside this process;
/// the host screen is given the relay as its configured relay, so Start hosting dials out to it and shows a relay invite (room code and the host key fingerprint, not an address of this computer). The joiner
/// pastes that invite on the join screen, whose connection is the one the app makes (<see cref="RelaySettings.Choose"/>, then the game's own TLS through the splice), takes slot 2 of the host's lobby, both
/// press Ready, the host presses Start game and the joiner's screen opens the game from the snapshot that came through the relay. Nothing is written to the real user folder.
/// </summary>
public sealed class RelaySessionScenario : Scenario
{
    private const string Vanilla = "Civ V - Vanilla";

    public override string Name => "relay-session";

    public override bool Solo => true;

    public override bool Uses3D => true;

    public override IReadOnlyList<string> Checklist =>
    [
        "Does the host screen make clear which invite works through the relay and which one needs a port forward?",
        "Is the joiner's lobby indistinguishable from a direct one (nothing asks the player to know about the relay once it was agreed to)?",
    ];

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session) => new Flow(host, session).Steps();

    private sealed class Flow
    {
        private readonly string dir = Directory.CreateTempSubdirectory("unciv-relay-session-").FullName;
        private readonly UiTheme theme;
        private readonly Control root;
        private readonly ClientIdentity hostIdentity = ClientIdentity.Generate();
        private readonly ClientIdentity friendIdentity = ClientIdentity.Generate();
        private readonly X509Certificate2 certificate = HostCertificate.Generate();
        private readonly KnownHosts known = new();
        private readonly NewGameForm nations = NewGameForm.FromRulesetCache();
        private readonly HostScreen hostScreen;
        private readonly JoinScreen joinScreen;
        private readonly int hostPort = FreePort();
        private RelayServer? relay;
        private RelayEndpoint? endpoint;
        private ClientSession? friend;
        private LobbyScreen? hostLobby;
        private LobbyScreen? friendLobby;
        private GameScreen? game;
        private NetGameSession? net;
        private bool friendSawStart;
        private string clipboard = "";

        public Flow(Control host, GameSession session)
        {
            theme = new UiTheme(AssetRegistry.ForRuleset(session.Game.Ruleset));
            host.AddChild(new ColorRect { Color = new Color(0.12f, 0.14f, 0.2f), AnchorRight = 1, AnchorBottom = 1 });
            root = new Control { AnchorRight = 1, AnchorBottom = 1 };
            host.AddChild(root);
            if (nations.SetBaseRuleset(Vanilla) is { } problem) throw new InvalidOperationException(problem);
            StartRelay();
            hostScreen = new HostScreen(theme, new HostSettingsForm(NewGameForm.FromRulesetCache()), new HostScreenEnvironment
            {
                Identity = () => hostIdentity,
                Certificate = () => certificate,
                HostedGamesDirectory = dir,
                DisplayName = "Host",
                Clock = () => 5000,
                SetClipboard = text => clipboard = text,
                Addresses = () => [new LocalAddress("127.0.0.1", "lo", AddressScope.Lan)],
                Relay = () => endpoint,
                RelaySet = () => endpoint != null,
            });
            hostScreen.HostingStarted += OpenHostLobby;
            Fill(hostScreen);
            joinScreen = new JoinScreen(theme, new JoinScreenEnvironment
            {
                Identity = friendIdentity,
                DisplayName = "Friend",
                RulesetHash = () => new LobbySettings { Ruleset = Vanilla }.ComputeRulesetHash(),
                KnownHosts = known,
                PersistKnownHosts = _ => { },
                Relay = () => endpoint,
                Connect = ConnectAsync,
                RelayNeedsPrompt = named => ChoiceOf(named).NeedsPrompt,
            }) { Visible = false };
            joinScreen.Joined += OpenFriendLobby;
            Fill(joinScreen);
        }

        private void Fill(Control screen)
        {
            screen.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            root.AddChild(screen);
        }

        private static int FreePort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        private void StartRelay()
        {
            relay = new RelayServer(new RelayOptions
            {
                DataDir = Path.Combine(dir, "relay"),
                TunnelPort = 0,
                HttpPort = 0,
                NoLogFiles = true,
                TlsMode = RelayTlsMode.SelfSigned,
                PublicNames = ["127.0.0.1"],
            }, null, output: null);
            relay.Start();
            endpoint = new RelayEndpoint("127.0.0.1", relay.TunnelPort, relay.CertificateFingerprint);
        }

        private RelayChoice ChoiceOf(RelayEndpoint named) => new RelaySettings { Url = endpoint.NotNull().ToString() }.Choose(named.Host, named.Port);

        // what App.ConnectAsync does for a relay invite, with the relay settings held here instead of in the user's folder
        private async Task<Unciv.Net.ITransport> ConnectAsync(JoinTarget target, CancellationToken token)
        {
            if (!target.IsRelay || target.Room is not { } room) throw new InvalidOperationException("this scenario joins through the relay only");
            var choice = ChoiceOf(new RelayEndpoint(target.Host, target.Port));
            if (choice.NeedsPrompt) throw new InvalidOperationException("The relay " + choice.Endpoint.Key + " was not agreed to");
            return await TcpTlsClientTransport.ConnectViaRelayAsync(choice.Endpoint, room, target.Fingerprint, known, choice.Dial, cancellationToken: token);
        }

        private void OpenHostLobby(HostingSession hosting)
        {
            if (hosting.Lobby == null || hosting.HostPlayer == null) return;
            hostLobby = new LobbyScreen(theme, nations, hosting.HostPlayer, hostIdentity.PlayerKey, hosting.Lobby) { Visible = false };
            Fill(hostLobby);
        }

        private void OpenFriendLobby(ClientSession joined)
        {
            friend = joined;
            friendLobby = new LobbyScreen(theme, nations, joined, friendIdentity.PlayerKey) { Visible = false };
            friendLobby.GameStarting += () => friendSawStart = true;
            Fill(friendLobby);
        }

        private static Vector2 Centre(Func<Control?> find) => find()?.GetGlobalRect().GetCenter() ?? Vector2.Zero;

        private static void Type(LineEdit field, string text)
        {
            field.Text = text;
            field.EmitSignal(LineEdit.SignalName.TextChanged, text);
        }

        private LobbyScreen Host => hostLobby.NotNull();

        private LobbyScreen Friend => friendLobby.NotNull();

        private InviteCode RelayInvite()
        {
            InviteCode.TryParse(hostScreen.RelayInvite, out var invite);
            return invite.NotNull();
        }

        public IReadOnlyList<ScenarioStep> Steps() =>
            Hosting().Concat(Joining()).Concat(Playing()).SelectMany(step => step is Press or Click or Act ? new[] { step, new WaitFrames(2) } : [step]).ToList();

        private ScenarioStep[] Hosting() =>
        [
            new AssertQuery("the relay is listening on loopback and the host screen was given it", () => relay is { TunnelPort: > 0 } && endpoint is { Host: "127.0.0.1", Fingerprint: not null } && hostScreen.Visible),
            new Act("a small game and a free port", () =>
            {
                hostScreen.Form.Game.SetBaseRuleset(Vanilla);
                Type(hostScreen.NameField, "Through the relay");
                Type(hostScreen.SeedField, "3");
                Type(hostScreen.PortField, hostPort.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }),
            new Press("Map"),
            new Click(() => Centre(() => hostScreen.ChoiceButton("Size", "Tiny"))),
            new Press("Start hosting"),
            new WaitUntil("the lobby is open and the relay gave the room", () => hostScreen.IsHosting && hostScreen.RelayInvite.Length > 0, 1200),
            new AssertQuery("the relay invite names the relay, a room code and the host key, and no address of this computer", () =>
            {
                var invite = RelayInvite();
                return invite is { IsRelay: true, Host: "127.0.0.1", Room: not null } && invite.Port == relay.NotNull().TunnelPort
                    && invite.Fingerprint == hostScreen.Session.NotNull().Fingerprint && hostScreen.RelayInviteField.Text == hostScreen.RelayInvite && hostScreen.RelayInviteBox.Visible;
            }),
            new Act("scroll to the relay invite", () => hostScreen.Reveal(hostScreen.CopyRelayInviteButton)),
            new WaitFrames(3),
            new Click(() => Centre(() => hostScreen.CopyRelayInviteButton)),
            new AssertQuery("Copy relay invite put exactly that invite on the clipboard", () => clipboard.Length > 0 && clipboard == hostScreen.RelayInvite),
        ];

        private ScenarioStep[] Joining() =>
        [
            new Act("the friend opens the join screen and pastes the invite", () =>
            {
                hostScreen.Visible = false;
                joinScreen.Visible = true;
                Type(joinScreen.AddressField, clipboard);
            }),
            new Click(() => Centre(() => joinScreen.JoinButton)),
            new WaitUntil("the friend was seated by the host, through the relay", () => friendLobby != null && Host.SlotLines.Count > 1 && Host.SlotLines[1] == "2. Friend", 1200),
            new AssertQuery("no relay prompt was needed, the host key is pinned and the friend holds slot 2 with their own key", () =>
                joinScreen.RelayPrompt == "" && known.Pins.Count == 1 && hostScreen.Session.NotNull().Slots[1].PlayerKeyId == friendIdentity.PlayerKey && hostScreen.Session.Slots[0].PlayerKeyId == hostIdentity.PlayerKey),
            new Act("show the host's lobby", () =>
            {
                joinScreen.Visible = false;
                Host.Visible = true;
            }),
            new WaitFrames(4),
            new Capture("host-lobby", "The host's lobby after a friend joined through the relay: the friend sits in slot 2, the settings are the ones typed on the host screen; Start waits for both to be ready."),
            new Click(() => Centre(() => Host.ReadyButton)),
            new WaitUntil("the host is ready", () => Host.State is { } s && s.Slots[0].Ready),
            new Act("show the friend's lobby", () =>
            {
                Host.Visible = false;
                Friend.Visible = true;
            }),
            new WaitFrames(4),
            new AssertQuery("the joiner sees the same table, its own seat marked, and no host controls", () =>
                Friend.SlotLines.Count > 1 && Friend.SlotLines[0].StartsWith("1. Host", StringComparison.Ordinal) && Friend.SlotLines[1] == "2. Friend (you)" && !Friend.StartButton.Visible),
            new Capture("joiner-lobby", "The friend's lobby, reached through the relay with the pasted invite: own seat marked (you), settings read-only, only Ready and chat."),
            new Click(() => Centre(() => Friend.ReadyButton)),
            new WaitUntil("both are ready", () => Host.State is { } s && s.Slots.Take(2).All(it => it.Ready) && !Host.StartButton.Disabled, 1200),
        ];

        private async Task CleanUpAsync()
        {
            await net.NotNull().DisposeAsync();
            await friend.NotNull().DisposeAsync();
            await hostScreen.Session.NotNull().DisposeAsync();
            await relay.NotNull().DisposeAsync();
            certificate.Dispose();
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }

        private ScenarioStep[] Playing() =>
        [
            new Act("back to the host", () =>
            {
                Friend.Visible = false;
                Host.Visible = true;
            }),
            new WaitFrames(2),
            new Click(() => Centre(() => Host.StartButton)),
            new WaitUntil("the game started and the joiner was told", () => hostScreen.Session.NotNull().Lobby.NotNull().ActiveGameHost != null && friendSawStart && friend.NotNull().Game.Snapshot != null, 3000),
            new Act("the joiner's game opens from the snapshot that came through the relay", () =>
            {
                Host.Visible = false;
                Friend.Visible = false;
                var ruleset = RulesetCache.GetComplexRuleset(new LinkedHashSet<string>(nations.Mods), nations.BaseRuleset);
                net = new NetGameSession(friend.NotNull(), ruleset);
                game = new GameScreen { NowMs = () => 5000 };
                root.AddChild(game);
                game.Start(net, AssetRegistry.ForRuleset(ruleset));
            }),
            new WaitFrames(8),
            new Act("hide the turn bar, whose countdown runs on the wall clock", () => game.NotNull().TurnBar.Visible = false),
            new AssertQuery("the joiner plays a civ of its own, other than the host's", () =>
            {
                var snapshot = friend.NotNull().Game.Snapshot.NotNull();
                var hostCiv = hostScreen.Session.NotNull().HostPlayer.NotNull().Game.Snapshot.NotNull().You;
                return net.NotNull().ViewerCivId == snapshot.You && snapshot.You != hostCiv && game.NotNull().Visible;
            }),
            new Capture("game", "The joiner's game screen, opened from the first snapshot that reached it through the relay."),
            new Act("clean up", () =>
            {
                game.NotNull().QueueFree();
                _ = CleanUpAsync();
            }),
        ];
    }
}
