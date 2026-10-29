using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Godot;
using Unciv.Client.Net.Hosting;
using Unciv.Client.Net.Session;
using Unciv.Client.Net.Transport;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Setup;
using Unciv.GodotClient.Ui;
using Unciv.Commands;
using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using Unciv.Models.Metadata;
using Unciv.Net.Identity;
using Unciv.Net.Lobby;
using Unciv.Net.Persistence;
using Unciv.Server;
using Unciv.Server.Transport;
using Unciv.Utils;

namespace Unciv.GodotHarness;

/// <summary>
/// C034: the host screen, by clicking. The settings (name, difficulty, speed, civs, ruleset, map size, seed, timer, password, port, address) reach the lobby settings; a port that is taken
/// is refused with a message and hosts nothing; Start hosting opens the lobby with the host in slot 1 and the other slots open; the invite parses back to the chosen address, the port and the host
/// key fingerprint; Copy invite puts it on the clipboard; a friend who uses that invite over real TLS/TCP takes slot 2; Stop hosting frees the port; a saved hosted game resumes and
/// listens again. Addresses, key, certificate and clipboard are fixed or captured, so the run does not depend on this machine's network cards.
/// </summary>
public sealed class HostScreenScenario : Scenario
{
    private const string Lan = "192.168.1.20";
    private const string Wan = "203.0.113.7";

    // The same on every run (it is in the golden images); a fixed number in the dynamic range that nothing else on the machine is likely to hold.
    private const int Port = 47891;

    public override string Name => "host-screen";

    public override bool Solo => true;

    public override IReadOnlyList<string> Checklist =>
    [
        "Is the port-forward hint clear about what a friend on the internet needs and what a friend on the same network needs?",
        "While hosting, can you tell which slot is you, which are open, and which address the invite uses?",
        "Is the refusal of a port that is already in use understandable?",
    ];

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session) => new Flow(host, session).Steps();

    private sealed class Flow
    {
        private readonly string root = Directory.CreateTempSubdirectory("unciv-host-scenario-").FullName;
        private readonly ClientIdentity identity = ClientIdentity.Generate();
        private readonly ClientIdentity friendIdentity = ClientIdentity.Generate();
        private readonly X509Certificate2 certificate = FixedCertificate();
        private readonly string fingerprint;
        private readonly HostScreen screen;
        private string clipboard = "";
        private TcpListener? blocker;
        private ClientSession? friend;
        private Task<JoinResult>? friendJoin;
        private bool wentBack;
        private Task? saving;

        public Flow(Control host, GameSession session)
        {
            var theme = new UiTheme(AssetRegistry.ForRuleset(session.Game.Ruleset));
            host.AddChild(new ColorRect { Color = new Color(0.12f, 0.14f, 0.2f), AnchorRight = 1, AnchorBottom = 1 });
            var manager = new ScreenManager(theme);
            host.AddChild(manager);
            fingerprint = HostCertificate.GetFingerprint(certificate);
            var environment = new HostScreenEnvironment
            {
                Identity = () => identity,
                Certificate = () => certificate,
                HostedGamesDirectory = root,
                DisplayName = "Host",
                Clock = () => 5000,
                SetClipboard = text => clipboard = text,
                Addresses = () => [new LocalAddress(Lan, "Wi-Fi", AddressScope.Lan), new LocalAddress(Wan, "eth0", AddressScope.Public)],
            };
            screen = new HostScreen(theme, new HostSettingsForm(NewGameForm.FromRulesetCache()), environment);
            screen.BackRequested += () => wentBack = true;
            manager.Push(screen);
        }

        private static Vector2 Centre(Func<Control?> find) => find()?.GetGlobalRect().GetCenter() ?? Vector2.Zero;

        private Click ClickChoice(string field, string option) => new(() => Centre(() => screen.ChoiceButton(field, option)));

        private static void Type(LineEdit field, string text)
        {
            field.Text = text;
            field.EmitSignal(LineEdit.SignalName.TextChanged, text);
        }

        private InviteCode Parsed()
        {
            InviteCode.TryParse(screen.Invite, out var invite);
            return invite.NotNull();
        }

        private bool InvitePointsAt(string address) => InviteCode.TryParse(screen.Invite, out var i) && i != null && (i.Host, i.Port, i.Fingerprint) == (address, Port, fingerprint) && screen.InviteField.Text == screen.Invite;

        private LobbySettings Lobby() => screen.Session.NotNull().Lobby.NotNull().Lobby.Settings;

        public IReadOnlyList<ScenarioStep> Steps() =>
            Settings().Concat(Hosting()).Concat(Resuming()).SelectMany(step => step is Press or Click or Act ? new[] { step, new WaitFrames(2) } : [step]).ToList();


        private ScenarioStep[] Settings() =>
        [
                new Act("save a hosted game the way the in-process host leaves one", () => saving = SaveHostedGameAsync(root, identity, friendIdentity)),
                new WaitUntil("the hosted game is saved", () => saving is { IsCompleted: true }),
                new Act("show it", () =>
                {
                    saving.NotNull().GetAwaiter().GetResult();
                    screen.Refresh();
                }),
                new WaitFrames(3),
                new AssertQuery("the screen opens on the Game tab with the defaults and nothing wrong", () =>
                    screen.Tabs.SelectedIndex == 0 && screen.Form is { GameName: "Multiplayer Game", AiFill: true, TurnTimerSeconds: 120, Port: 47821 } && screen.Message == "" && !screen.StartButton.Disabled && !screen.IsHosting),
                new Capture("defaults", "Host screen, Game tab, with the defaults: game name, civ and city-state counts, AI fill, difficulty, speed, era and victory types"),
    
                new Act("type the game name", () => Type(screen.NameField, "Friday night")),
                ClickChoice("Difficulty", "King"),
                ClickChoice("Speed", "Epic"),
                new Press("Fewer civs"),
                new Press("Rules"),
                ClickChoice("Base ruleset", "Civ V - Vanilla"),
                new Press("Map"),
                ClickChoice("Size", "Tiny"),
                new Act("type the seed", () => Type(screen.SeedField, "7")),
                new Press("Network"),
                ClickChoice("Timer", "5 min"),
                new Act("type the password and the port", () =>
                {
                    Type(screen.PasswordField, "hunter2");
                    Type(screen.PortField, Port.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }),
                new Click(() => Centre(() => screen.AddressButton(Wan))),
                new AssertQuery("the settings reached the form and build the lobby settings", () =>
                {
                    var s = screen.Form.Build(5000).Settings;
                    return s is { GameName: "Friday night", Difficulty: "King", Speed: "Epic", MaxMajorCivs: 3, Ruleset: "Civ V - Vanilla", TurnTimerSeconds: 300, Password: "hunter2", AiFill: true }
                        && s.Map.MapSize.Name == "Tiny" && s.Map.Seed == 7 && screen.Form.Port == Port && screen.InviteAddress == Wan;
                }),
                new AssertQuery("the network tab explains the port forward and names the chosen address", () =>
                    screen.FindChild("AddressHint", true, false) is Label hint && hint.Text.Contains("forward TCP port " + Port, StringComparison.Ordinal) && hint.Text.Contains(Wan + ": friends on the internet", StringComparison.Ordinal)),
                new Capture("network", "Network tab: turn timer, password, port, the addresses of this computer (the internet address chosen) and the port-forward hint"),
    
                new Press("Saved games"),
                new AssertQuery("the saved hosted game is listed and can be resumed by its host", () => screen.ResumeButton(0) is { Disabled: false } && screen.ResumeButton(1) == null),
                new Capture("saved", "Saved games tab: the hosted game with Host and Friend, last played at a fixed time, with a Resume button"),
    
        ];

        private ScenarioStep[] Hosting() =>
        [
                new Act("another program takes the port", () =>
                {
                    blocker = new TcpListener(IPAddress.IPv6Any, Port);
                    blocker.Server.DualMode = true;
                    blocker.Start();
                }),
                new Press("Start hosting"),
                new WaitUntil("the refusal is shown", () => screen.Message.Contains("cannot be used", StringComparison.Ordinal)),
                new AssertQuery("nothing is hosted and Start works again", () => !screen.IsHosting && !screen.StartButton.Disabled && screen.Message.Contains($"Port {Port}", StringComparison.Ordinal)),
                new Capture("port-in-use", "A port another program uses is refused with the port number and what to do; nothing is hosted"),
                new Act("the port is free again", () => blocker.NotNull().Stop()),
    
                new Press("Start hosting"),
                new WaitUntil("the lobby is open with the host seated", () => screen.IsHosting && screen.Session.NotNull().Slots.Count > 0 && screen.SlotLines.Count > 0),
                new AssertQuery("the host is in slot 1 as a human with their own key, the other two slots are open", () =>
                {
                    var slots = screen.Session.NotNull().Slots;
                    return slots.Count == 3 && slots[0] is { PlayerName: "Host", Kind: "Human" } && slots[0].PlayerKeyId == identity.PlayerKey && slots.Skip(1).All(it => it.Kind == "Open");
                }),
                new AssertQuery("the slot table says which one is you", () => screen.SlotLines.Count == 3 && screen.SlotLines[0] == "1. Host (you, the host)" && screen.SlotLines[1] == "2. Open"),
                new AssertQuery("the lobby runs with the settings from the form", () =>
                    Lobby() is { GameName: "Friday night", Difficulty: "King", Speed: "Epic", MaxMajorCivs: 3, TurnTimerSeconds: 300, Ruleset: "Civ V - Vanilla" }
                    && screen.Session.NotNull().Lobby.NotNull().Lobby.Admission.RequiresPassword),
                new AssertQuery("the invite parses back to the chosen address, the port and the host key", () => InvitePointsAt(Wan) && Parsed().Token == null),
                new Act("scroll to Copy invite", () => screen.Reveal(screen.CopyInviteButton)),
                new Press("Copy invite"),
                new AssertQuery("Copy invite put exactly the invite on the clipboard", () => clipboard == screen.Invite && clipboard.StartsWith("unciv://" + Wan + ":", StringComparison.Ordinal) && screen.Message.StartsWith("Invite copied", StringComparison.Ordinal)),
                new Capture("hosting", "Hosting: the host in slot 1, open slots, the address the invite uses, the invite and Copy invite, and the port-forward hint"),
    
                new Click(() => Centre(() => screen.AddressButton(Lan))),
                new AssertQuery("choosing another address changes the invite", () => InvitePointsAt(Lan)),
    
                new Act("a friend uses the invite over real TLS/TCP", () =>
                {
                    var invite = Parsed();
                    friend = new ClientSession(token => TcpTlsClientTransport.ConnectAsync("127.0.0.1", invite.Port, expectedFingerprint: invite.Fingerprint, cancellationToken: token), new ClientSessionOptions
                    {
                        Identity = friendIdentity,
                        DisplayName = "Friend",
                        RulesetHash = Lobby().ComputeRulesetHash(),
                        ExpectedFingerprint = invite.Fingerprint,
                        Password = "hunter2",
                        MaxReconnectAttempts = 0,
                    });
                    friendJoin = friend.JoinAsync();
                }),
                new WaitUntil("the friend is seated", () => friendJoin is { IsCompleted: true } && screen.SlotLines.Count == 3 && screen.SlotLines[1] == "2. Friend"),
                new AssertQuery("the friend was admitted into slot 2 and the host sees it", () =>
                    friendJoin.NotNull().Result.Joined && screen.Session.NotNull().Slots[1].PlayerKeyId == friendIdentity.PlayerKey && screen.SlotLines[0] == "1. Host (you, the host)" && screen.SlotLines[2] == "3. Open"),
                new Capture("friend", "A friend joined with the invite over the network and sits in slot 2; the host's table updated"),
    
                new Act("the friend leaves", () => _ = friend.NotNull().DisposeAsync().AsTask()),
                new Press("Stop hosting"),
                new WaitUntil("hosting stopped and the settings are back", () => !screen.IsHosting && screen.Tabs.Visible && !screen.StartButton.Disabled),
                new AssertQuery("the port is free again", () =>
                {
                    var probe = new TcpListener(IPAddress.IPv6Any, Port) { };
                    probe.Server.DualMode = true;
                    probe.Start();
                    probe.Stop();
                    return true;
                }),
    
        ];

        private ScenarioStep[] Resuming() =>
        [
                new Press("Saved games"),
                new Click(() => Centre(() => screen.ResumeButton(0))),
                new WaitUntil("the saved game is hosted again", () => screen.IsHosting && screen.SlotLines.Count > 0),
                new AssertQuery("it listens on the chosen port with the same key and shows who was in it", () =>
                    screen.Session.NotNull().Resumed != null && screen.Session.NotNull().Port == Port && screen.Session.NotNull().Fingerprint == fingerprint
                    && screen.SlotLines[0] == "Saved game with Host, Friend" && InvitePointsAt(Lan)),
                new Capture("resumed", "A saved hosted game resumed: it listens again, with an invite and the players who were in it"),
                new Press("Stop hosting"),
                new WaitUntil("stopped again", () => !screen.IsHosting && !screen.StartButton.Disabled),
                new Press("Back"),
                new AssertQuery("Back leaves the screen", () => wentBack),
                new Act("clean up", () =>
                {
                    certificate.Dispose();
                    try { Directory.Delete(root, recursive: true); } catch (IOException) { }
                }),
        ];
    }

    // A throwaway key written out here (it protects nothing), so the host key fingerprint in the invite, and so the golden images, are the same on every run.
    private const string FixedKey = "MIGHAgEAMBMGByqGSM49AgEGCCqGSM49AwEHBG0wawIBAQQgnffrsuxgfNWnbOIzonRQBbmh1UNS93KQF79FGYLvX5GhRANCAAQahTEjbElk4WjhH6+volsSACDhHP542Xsl3Oun7bSEb9AohA6+bKbDKd83jFwg9V1tg9Du/OBmuDu6prmBA/M2";

    private static X509Certificate2 FixedCertificate()
    {
        using var key = ECDsa.Create();
        key.ImportPkcs8PrivateKey(Convert.FromBase64String(FixedKey), out _);
        var request = new CertificateRequest("CN=UncivScenario", key, HashAlgorithmName.SHA256);
        return request.CreateSelfSigned(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2036, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }

    // A game directory as the in-process host (T205) leaves it after a clean stop: Host (Babylon) and Friend (Greece) bound, written at a fixed time.
    private static async Task SaveHostedGameAsync(string root, ClientIdentity host, ClientIdentity friend)
    {
        var setup = new GameSetupInfo(
            new GameParameters
            {
                BaseRuleset = "Civ V - Vanilla",
                NumberOfCityStates = 0,
                VictoryTypes = ["Domination"],
                Players = [new("Babylon", PlayerType.Human), new("Greece", PlayerType.Human), new("Rome", PlayerType.AI)],
            },
            new MapParameters { Type = MapType.Pangaea, Shape = MapShape.Rectangular, Seed = 3, MapSize = MapSize.Tiny });
        var game = GameStarter.StartNewGame(setup, 11, 1000);
        game.StartSimultaneousTurns();
        var directory = Path.Combine(root, "friday");
        var bindings = new Dictionary<string, SlotBinding>
        {
            ["Babylon"] = new() { PlayerKey = host.PlayerKey, DisplayName = "Host" },
            ["Greece"] = new() { PlayerKey = friend.PlayerKey, DisplayName = "Friend" },
        };
        var started = InProcessHost.Start(game, setup, 11, 1000, directory, "Babylon", bindings, new InProcessHostOptions { TickInterval = null });
        await started.StopAsync();
        File.SetLastWriteTimeUtc(Path.Combine(directory, "game.json"), new DateTime(2026, 10, 24, 19, 30, 0, DateTimeKind.Utc));
    }
}
