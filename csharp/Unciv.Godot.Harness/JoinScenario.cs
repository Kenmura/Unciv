using Godot;
using Unciv.Client.Net.Session;
using Unciv.Commands;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Play;
using Unciv.GodotClient.Setup;
using Unciv.GodotClient.Ui;
using Unciv.Logic.Map;
using Unciv.Net;
using Unciv.Net.Identity;
using Unciv.Net.Lobby;
using Unciv.Server;
using Unciv.Utils;

namespace Unciv.GodotHarness;

/// <summary>
/// C006: the join screen, by clicking, against in-memory hosts (no sockets). A bad port is refused with a sentence; a typed address that was never seen asks about the host's fingerprint
/// and sends nothing when the player says no; trusting it pins the key and a wrong password is then refused with its reason; the pin shows up in the known hosts list; a host that presents
/// another key under a pinned address is a hard stop with no continue button and no seat taken, and an invite carrying the wrong fingerprint is refused too; only Forget the old key lets the
/// address be asked about again; a pasted invite joins with no prompt and takes a seat. The reconnect overlay is shown in each of its states (the live drop and resume is tested over the
/// real session in NetGameSessionTests, the overlay only follows its state).
/// </summary>
public sealed class JoinScenario : Scenario
{
    private static readonly string Fingerprint = HostKey.Fingerprint([1, 2, 3, 4]);
    private static readonly string OtherFingerprint = HostKey.Fingerprint([9, 9, 9, 9]);
    private const string Vanilla = "Civ V - Vanilla";
    private const string Address = "192.168.1.20:47821";

    public override string Name => "join";

    public override bool Solo => true;

    public override IReadOnlyList<string> Checklist =>
    [
        "Does the fingerprint prompt tell a friend what to compare and when it is safe to continue?",
        "Is the host-key-changed screen alarming enough, and is it clear that nothing was sent?",
        "Are the refusal sentences (wrong password, unreachable host) wording you would understand without help?",
    ];

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session) => new Flow(host, session).Steps();

    private sealed class Flow
    {
        private readonly UiTheme theme;
        private readonly Control root;
        private readonly ClientIdentity identity = ClientIdentity.Generate();
        private readonly KnownHosts known = new();
        private readonly ServerLobby lobby;
        private readonly ServerLobby impostor;
        private readonly JoinScreen screen;
        private readonly ReconnectOverlay overlay;
        private bool useImpostor;
        private int connects;
        private int persisted;
        private string remembered = "";
        private ClientSession? joined;

        public Flow(Control host, GameSession session)
        {
            theme = new UiTheme(AssetRegistry.ForRuleset(session.Game.Ruleset));
            host.AddChild(new ColorRect { Color = new Color(0.12f, 0.14f, 0.2f), AnchorRight = 1, AnchorBottom = 1 });
            root = new Control { AnchorRight = 1, AnchorBottom = 1 };
            host.AddChild(root);
            var nations = NewGameForm.FromRulesetCache();
            if (nations.SetBaseRuleset(Vanilla) is { } problem) throw new InvalidOperationException(problem);
            var settings = new LobbySettings
            {
                GameName = "Friday night",
                Ruleset = Vanilla,
                MaxMajorCivs = 4,
                CityStateCount = 0,
                Password = "swordfish",
                Map = new MapParameters { Type = MapType.Pangaea, Shape = MapShape.Rectangular, Seed = 3, MapSize = MapSize.Tiny },
            };
            lobby = new ServerLobby(settings, hostFingerprint: Fingerprint, hostName: "Host");
            impostor = new ServerLobby(settings, hostFingerprint: OtherFingerprint, hostName: "Impostor");
            var environment = new JoinScreenEnvironment
            {
                Identity = identity,
                DisplayName = "Friend",
                RulesetHash = () => settings.ComputeRulesetHash(),
                KnownHosts = known,
                PersistKnownHosts = _ => persisted++,
                RememberAddress = address => remembered = address,
                Connect = (_, _) =>
                {
                    connects++;
                    var (client, hostEnd) = InMemoryTransport.CreatePair();
                    _ = (useImpostor ? impostor : lobby).AttachConnectionAsync(hostEnd);
                    return Task.FromResult<ITransport>(client);
                },
            };
            screen = new JoinScreen(theme, environment);
            screen.Joined += session => joined = session;
            root.AddChild(screen);
            overlay = new ReconnectOverlay(theme);
            root.AddChild(overlay);
        }

        private static Vector2 Centre(Func<Control?> find) => find()?.GetGlobalRect().GetCenter() ?? Vector2.Zero;

        private static void Type(LineEdit field, string text) => field.Text = text;

        private int Seated() => lobby.Lobby.Slots.Count(it => it.Kind == SlotKind.Human) + impostor.Lobby.Slots.Count(it => it.Kind == SlotKind.Human);

        public IReadOnlyList<ScenarioStep> Steps() =>
            Typing().Concat(Trusting()).Concat(Changed()).Concat(Inviting()).Concat(Overlay()).SelectMany(step => step is Press or Click or Act ? new[] { step, new WaitFrames(2) } : [step]).ToList();

        private ScenarioStep[] Typing() =>
        [
            new Capture("empty", "The join screen as it opens: one field for an address or an invite, a password field, Join and Back. Nothing is listed under known hosts yet."),
            new Act("a port that is not one", () => Type(screen.AddressField, "192.168.1.20:99999")),
            new Click(() => Centre(() => screen.JoinButton)),
            new AssertQuery("it is refused with a sentence and nothing was dialled", () => screen.Message.Contains("port", StringComparison.Ordinal) && connects == 0 && !screen.IsBusy),
            new Act("a good address and a wrong password", () =>
            {
                Type(screen.AddressField, Address);
                Type(screen.PasswordField, "guess");
            }),
            new Click(() => Centre(() => screen.JoinButton)),
            new WaitUntil("the fingerprint prompt opens", () => screen.TrustPrompt.Length > 0),
            new AssertQuery("it shows the address and the host's fingerprint, and nobody is seated", () =>
                screen.TrustPrompt.Contains(Address, StringComparison.Ordinal) && screen.TrustPrompt.Contains(HostKey.Normalize(Fingerprint).NotNull(), StringComparison.Ordinal) && Seated() == 0 && known.Pins.Count == 0),
            new Capture("trust", "First time at this address: the host's key fingerprint is shown with the advice to compare it with what the host told you; nothing has been sent to the host yet."),
            new Click(() => Centre(() => screen.DistrustButton)),
            new WaitUntil("the attempt ended", () => !screen.IsBusy && screen.Message.Length > 0),
            new AssertQuery("saying no sent nothing, pinned nothing and seated nobody", () =>
                screen.Message.Contains("did not trust", StringComparison.Ordinal) && known.Pins.Count == 0 && persisted == 0 && Seated() == 0),
        ];

        private ScenarioStep[] Trusting() =>
        [
            new Click(() => Centre(() => screen.JoinButton)),
            new WaitUntil("the prompt opens again", () => screen.TrustPrompt.Length > 0),
            new Click(() => Centre(() => screen.TrustButton)),
            new WaitUntil("the host answered", () => !screen.IsBusy && screen.Message.Length > 0),
            new AssertQuery("the wrong password is refused with its reason, the key is pinned and nobody is seated", () =>
                screen.Message == "Wrong password or invite." && known.Pins.Count == 1 && HostKey.Matches(known.Pins[Address], Fingerprint) && persisted == 1 && Seated() == 0 && joined == null),
            new Capture("wrong-password", "The host refused the wrong password: a plain sentence under the form. The key was pinned when the player trusted it, so the address now appears under known hosts with Forget."),
            new Click(() => Centre(() => screen.HostButton(Address))),
            new AssertQuery("picking a known host fills the address", () => screen.AddressField.Text == Address),
        ];

        private ScenarioStep[] Changed() =>
        [
            new Act("the host at that address now presents another key", () => useImpostor = true),
            new Click(() => Centre(() => screen.JoinButton)),
            new WaitUntil("the hard stop opens", () => screen.KeyChangedPrompt.Length > 0),
            new AssertQuery("there is no way to continue, nothing was sent, nobody is seated", () =>
                screen.KeyChangedPrompt.Contains("has changed", StringComparison.Ordinal) && !screen.TrustButton.IsVisibleInTree() && !screen.JoinButton.IsVisibleInTree() && Seated() == 0 && HostKey.Matches(known.Pins[Address], Fingerprint)),
            new Capture("key-changed", "The host's key differs from the one pinned for this address: a hard stop that says what it may mean and that nothing was sent. The only choices are Back and, deliberately, Forget the old key."),
            new Click(() => Centre(() => screen.StopBackButton)),
            new AssertQuery("back on the form with the pin untouched", () => screen.JoinButton.Visible && known.Pins.Count == 1),
            new Act("an invite with the wrong fingerprint is refused as well", () =>
            {
                useImpostor = false;
                Type(screen.AddressField, InviteCode.Format("192.168.1.20", 47821, OtherFingerprint));
            }),
            new Click(() => Centre(() => screen.JoinButton)),
            new WaitUntil("the invite was refused", () => !screen.IsBusy && screen.Message.Contains("pretending", StringComparison.Ordinal)),
            new AssertQuery("no prompt, no seat", () => screen.TrustPrompt.Length == 0 && Seated() == 0 && joined == null),
            new Act("the host really did move: switch back to the impostor's key and open the hard stop again", () =>
            {
                useImpostor = true;
                Type(screen.AddressField, Address);
            }),
            new Click(() => Centre(() => screen.JoinButton)),
            new WaitUntil("the hard stop opens again", () => screen.KeyChangedPrompt.Length > 0),
            new Click(() => Centre(() => screen.ForgetButton)),
            new AssertQuery("forgetting removed the pin and was saved", () => known.Pins.Count == 0 && persisted == 2 && screen.Message.Contains("Forgot", StringComparison.Ordinal)),
        ];

        private ScenarioStep[] Inviting() =>
        [
            new Act("the friend pastes the invite the real host gave and the right password", () =>
            {
                useImpostor = false;
                Type(screen.AddressField, InviteCode.Format("192.168.1.20", 47821, Fingerprint));
                Type(screen.PasswordField, "swordfish");
            }),
            new Click(() => Centre(() => screen.JoinButton)),
            new WaitUntil("the host seated the friend", () => joined != null),
            new AssertQuery("no prompt was needed, the key is pinned, the address is remembered and the friend sits in a slot", () =>
                screen.TrustPrompt.Length == 0 && known.Pins.ContainsKey(Address) && remembered == Address && lobby.Lobby.Slots.Count(it => it.Kind == SlotKind.Human) == 1 && lobby.Lobby.Slots.Any(it => it.DisplayName == "Friend")),
        ];

        private ScenarioStep[] Overlay() =>
        [
            new Act("the connection to the host drops during a game", () =>
            {
                screen.Visible = false;
                overlay.Apply(JoinModel.Connection(false, false, null));
            }),
            new WaitFrames(2),
            new AssertQuery("the overlay waits and offers no way out", () => overlay.Visible && overlay.Title == "Connection lost" && !overlay.IsFinal),
            new Capture("reconnecting", "Connection lost: the game is covered and says it is reconnecting with the same key; commands are kept. No buttons while it retries."),
            new Act("the host announces it is closing", () => overlay.Apply(JoinModel.Connection(false, true, null))),
            new WaitFrames(2),
            new AssertQuery("the host-went-away wording is shown", () => overlay.Title == "The host went away"),
            new Capture("host-away", "The host announced it is shutting down: 'The host went away', waiting for it to come back; the client rejoins by itself."),
            new Act("reconnecting gave up", () => overlay.Apply(JoinModel.Connection(false, false, "reconnect-failed"))),
            new WaitFrames(2),
            new AssertQuery("only Leave remains", () => overlay.IsFinal && overlay.LeaveButton.Visible && overlay.Title == "Disconnected"),
            new Capture("gave-up", "The session gave up: 'Disconnected' with only Leave game."),
            new Act("the connection is back", () => overlay.Apply(JoinModel.Connection(true, false, null))),
            new AssertQuery("the overlay is gone", () => !overlay.Visible),
            new Act("clean up", () =>
            {
                _ = joined?.DisposeAsync().AsTask();
                _ = lobby.DisposeAsync().AsTask();
                _ = impostor.DisposeAsync().AsTask();
            }),
        ];
    }
}
