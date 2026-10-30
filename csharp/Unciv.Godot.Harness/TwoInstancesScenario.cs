using Godot;
using Unciv.Client.Net.Hosting;
using Unciv.Client.Net.Session;
using Unciv.Client.Net.Transport;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Saves;
using Unciv.GodotClient.Ui;
using Unciv.GodotClient.Settings;
using Unciv.GodotClient.Setup;
using Unciv.Logic.Map;
using Unciv.Net.Identity;
using Unciv.Net.Lobby;
using Unciv.Utils;
using Unciv.Commands;

namespace Unciv.GodotHarness;

/// <summary>
/// T334: two client instances on one computer. The profile "alpha" hosts a lobby over real TCP on loopback and the profile "beta" joins it, each with the files of its own profile folder under one
/// user-data root (exactly the paths the game computes for <c>--profile=alpha</c> and <c>--profile=beta</c>). The two are distinct players (two PlayerKeys, the host's seat holds alpha's key, the
/// friend's seat beta's), each keeps its own settings and saves (one's player name and save do not show up in the other's folder), and the default profile's identity is a third one that stays where it
/// always was. The main menu is shown for each profile with its label (the window title carries the same name). Nothing is written to the real user folder.
/// </summary>
public sealed class TwoInstancesScenario : Scenario
{
    public override string Name => "two-instances";

    public override bool Solo => true;

    public override IReadOnlyList<string> Checklist =>
    [
        "Can you tell the two windows apart at a glance (menu label and window title)?",
        "Does the label stay out of the way for the default profile (no label at all)?",
    ];

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session) => new Flow(host, session).Steps();

    private sealed class Flow
    {
        private readonly Control root;
        private readonly UiTheme theme;
        private readonly string dataRoot = Directory.CreateTempSubdirectory("unciv-two-instances-").FullName;
        private readonly ProfileFiles alpha;
        private readonly ProfileFiles beta;
        private readonly ProfileFiles standard;
        private Task<(string HostKey, string FriendKey, string HostSeat, string FriendSeat)>? join;

        public Flow(Control host, GameSession session)
        {
            theme = new UiTheme(AssetRegistry.ForRuleset(session.Game.Ruleset));
            host.AddChild(new ColorRect { Color = new Color(0.12f, 0.14f, 0.2f), AnchorRight = 1, AnchorBottom = 1 });
            root = new Control { AnchorRight = 1, AnchorBottom = 1 };
            host.AddChild(root);
            alpha = new ProfileFiles(dataRoot, "alpha");
            beta = new ProfileFiles(dataRoot, "beta");
            standard = new ProfileFiles(dataRoot, null);
        }

        private void ShowMenu(string? profile)
        {
            foreach (var child in root.GetChildren()) child.QueueFree();
            root.AddChild(new MainMenu(theme, profile));
        }

        private static string Key(ProfileFiles files)
        {
            using var identity = HostFiles.LoadIdentity(files.Identity);
            return identity.PlayerKey;
        }

        private async Task<(string, string, string, string)> HostAndJoinAsync()
        {
            var settings = new LobbySettings
            {
                GameName = "Two windows",
                Ruleset = "Civ V - Vanilla",
                MaxMajorCivs = 3,
                CityStateCount = 0,
                Map = new MapParameters { Type = MapType.Pangaea, Shape = MapShape.Rectangular, Seed = 3, MapSize = MapSize.Tiny },
            };
            using var hostIdentity = HostFiles.LoadIdentity(alpha.Identity);
            using var friendIdentity = HostFiles.LoadIdentity(beta.Identity);
            using var certificate = HostFiles.LoadCertificate(alpha.HostKey);
            await using var hosting = await HostingSession.StartLobbyAsync(settings, hostIdentity, certificate, "alpha", 0);
            if (!InviteCode.TryParse(hosting.Invite("127.0.0.1"), out var parsed)) throw new InvalidOperationException("no invite");
            var invite = parsed.NotNull();
            await using var friend = new ClientSession(token => TcpTlsClientTransport.ConnectAsync(invite, cancellationToken: token), new ClientSessionOptions
            {
                Identity = friendIdentity,
                DisplayName = "beta",
                RulesetHash = settings.ComputeRulesetHash(),
                ExpectedFingerprint = invite.Fingerprint,
                MaxReconnectAttempts = 0,
            });
            if (!(await friend.JoinAsync()).Joined) throw new InvalidOperationException("beta could not join");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            while (hosting.Slots[1].PlayerName != "beta") await Task.Delay(20, timeout.Token);
            return (hostIdentity.PlayerKey, friendIdentity.PlayerKey, hosting.Slots[0].PlayerKeyId ?? "", hosting.Slots[1].PlayerKeyId ?? "");
        }

        public IReadOnlyList<ScenarioStep> Steps() =>
        [
            new Act("the menu of an instance started with --profile=alpha", () => ShowMenu("alpha")),
            new WaitFrames(3),
            new AssertQuery("the menu names the profile", () => root.FindChild("ProfileLabel", true, false) is Label { Text: "Profile: alpha" }),
            new Capture("menu-alpha", "Main menu of the instance started with --profile=alpha: under the Unciv logo a line 'Profile: alpha'. The window title of that instance is 'Unciv - alpha'."),
            new Act("the menu of an instance started with --profile=beta", () => ShowMenu("beta")),
            new WaitFrames(3),
            new Capture("menu-beta", "The same menu for --profile=beta: 'Profile: beta', so two windows side by side are told apart."),
            new Act("the menu of an instance without a profile", () => ShowMenu(null)),
            new WaitFrames(3),
            new AssertQuery("the default profile shows no label", () => root.FindChild("ProfileLabel", true, false) == null),
            new Capture("menu-default", "Main menu with no profile (today's behaviour): no profile line."),
            new AssertQuery("each profile has its own identity key, and the default one is a third", () =>
            {
                string a = Key(alpha), b = Key(beta), d = Key(standard);
                return a != b && a != d && b != d && Key(alpha) == a && Key(beta) == b && File.Exists(Path.Combine(dataRoot, "identity.key")) && File.Exists(Path.Combine(dataRoot, "profiles", "alpha", "identity.key"));
            }),
            new AssertQuery("settings are separate: alpha's player name does not reach beta or the default", () =>
            {
                var a = new ClientSettingsStore(alpha.Settings);
                a.Save(ClientSettings.Default with { PlayerName = "Alpha Player" });
                return new ClientSettingsStore(alpha.Settings).Load().PlayerName == "Alpha Player"
                    && new ClientSettingsStore(beta.Settings).Load().PlayerName == ClientSettings.Default.PlayerName
                    && !File.Exists(standard.Settings);
            }),
            new AssertQuery("saves are separate: a save of alpha is not in beta's or the default's list", () =>
            {
                bool wrote = new SaveStore(alpha.Saves).Write("AlphaSave", "{}").Ok;
                return wrote && new SaveStore(alpha.Saves).List().Any(it => it.Name == "AlphaSave") && new SaveStore(beta.Saves).List().Count == 0 && new SaveStore(standard.Saves).List().Count == 0;
            }),
            new Act("alpha hosts a lobby over real TCP and beta joins it", () => join = Task.Run(HostAndJoinAsync)),
            new WaitUntil("the join finished", () => join.NotNull().IsCompleted, 600),
            new AssertQuery("the host's seat holds alpha's key and the friend's seat beta's: two distinct players", () =>
            {
                var (hostKey, friendKey, hostSeat, friendSeat) = join.NotNull().GetAwaiter().GetResult();
                return hostKey != friendKey && hostSeat == hostKey && friendSeat == friendKey && hostKey == Key(alpha) && friendKey == Key(beta);
            }),
            new Act("clean up the temporary user folder", () => Directory.Delete(dataRoot, true)),
        ];
    }
}
