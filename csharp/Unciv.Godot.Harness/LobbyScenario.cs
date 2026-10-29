using Godot;
using Unciv.Client.Net.Session;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Setup;
using Unciv.GodotClient.Ui;
using Unciv.Commands;
using Unciv.Logic.Map;
using Unciv.Net.Identity;
using Unciv.Net.Lobby;
using Unciv.Server;
using Unciv.Net;
using Unciv.Utils;

namespace Unciv.GodotHarness;

/// <summary>
/// C035: the lobby screen, by clicking, with three scripted clients over the in-memory transport (no sockets, so nothing depends on this machine). The host's screen shows the slot
/// table the lobby sent (compared with the lobby's own state), Start stays off until everybody is ready, the host picks a nation and a colour and presses Ready, the friends do the same through
/// their sessions and talk in the chat, a friend's screen shows the same table and settings read-only with no host controls, a friend whose ruleset hash differs is refused with the reason,
/// the host kicks one friend (who is told) and closes the freed slot, and Start fills the last slot with AI and starts the game.
/// </summary>
public sealed class LobbyScenario : Scenario
{
    private static readonly string Fingerprint = HostKey.Fingerprint([1, 2, 3, 4]);
    private const string Vanilla = "Civ V - Vanilla";

    public override string Name => "lobby";

    public override bool Solo => true;

    public override IReadOnlyList<string> Checklist =>
    [
        "Can you tell at a glance which seat is yours, who is ready and who is not?",
        "Does the host's disabled Start say what it is waiting for?",
        "Is the refusal for different rules understandable to someone who has never seen a ruleset hash?",
    ];

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session) => new Flow(host, session).Steps();

    private sealed class Flow
    {
        private readonly UiTheme theme;
        private readonly Control root;
        private readonly NewGameForm nations = NewGameForm.FromRulesetCache();
        private readonly LobbySettings settings;
        private readonly ServerLobby lobby;
        private readonly ClientIdentity[] identities = [ClientIdentity.Generate(), ClientIdentity.Generate(), ClientIdentity.Generate()];
        private readonly ClientSession?[] sessions = new ClientSession?[3];
        private readonly Task<JoinResult>?[] joins = new Task<JoinResult>?[3];
        private LobbyScreen? hostScreen;
        private LobbyScreen? friendScreen;
        private LobbyScreen? refusedScreen;
        private bool friendSawStart;
        private bool bSawKick;
        private JoinResult? mismatch;

        public Flow(Control host, GameSession session)
        {
            theme = new UiTheme(AssetRegistry.ForRuleset(session.Game.Ruleset));
            host.AddChild(new ColorRect { Color = new Color(0.12f, 0.14f, 0.2f), AnchorRight = 1, AnchorBottom = 1 });
            root = new Control { AnchorRight = 1, AnchorBottom = 1 };
            host.AddChild(root);
            if (nations.SetBaseRuleset(Vanilla) is { } problem) throw new InvalidOperationException(problem);
            settings = new LobbySettings
            {
                GameName = "Friday night",
                Ruleset = Vanilla,
                Difficulty = "King",
                Speed = "Epic",
                MaxMajorCivs = 4,
                CityStateCount = 0,
                AiFill = true,
                TurnTimerSeconds = 300,
                Map = new MapParameters { Type = MapType.Pangaea, Shape = MapShape.Rectangular, Seed = 3, MapSize = MapSize.Tiny },
            };
            lobby = new ServerLobby(settings, hostFingerprint: Fingerprint, hostName: "Host");
        }

        private static Vector2 Centre(Func<Control?> find) => find()?.GetGlobalRect().GetCenter() ?? Vector2.Zero;

        private ClientSession Connect(int who, string name, string? rulesetHash = null)
        {
            var (client, hostEnd) = InMemoryTransport.CreatePair();
            _ = lobby.AttachConnectionAsync(hostEnd);
            var created = new ClientSession(_ => Task.FromResult<ITransport>(client), new ClientSessionOptions
            {
                Identity = identities[who],
                DisplayName = name,
                RulesetHash = rulesetHash ?? settings.ComputeRulesetHash(),
                ExpectedFingerprint = Fingerprint,
                MaxReconnectAttempts = 0,
            });
            sessions[who] = created;
            joins[who] = created.JoinAsync();
            return created;
        }

        private LobbyScreen Host() => hostScreen.NotNull();

        private bool Joined(int who) => joins[who] is { IsCompleted: true, Result.Joined: true };

        private IReadOnlyList<string> Expected() =>
            lobby.Lobby.CreateLobbyState().Slots.Select(it => LobbyModel.SlotText(it, identities[0].PlayerKey, "Host")).ToList();

        public IReadOnlyList<ScenarioStep> Steps() =>
            Seating().Concat(Picking()).Concat(Joiner()).Concat(Hosting()).SelectMany(step => step is Press or Click or Act ? new[] { step, new WaitFrames(2) } : [step]).ToList();

        private ScenarioStep[] Seating() =>
        [
            new Act("the host takes slot 1 and the lobby screen opens", () =>
            {
                var host = Connect(0, "Host");
                hostScreen = new LobbyScreen(theme, nations, host, identities[0].PlayerKey, lobby);
                root.AddChild(hostScreen);
            }),
            new WaitUntil("the host is seated", () => Joined(0) && Host().State is { Slots.Count: 4 }),
            new Act("two friends join", () =>
            {
                Connect(1, "Friend A");
            }),
            new WaitUntil("friend A is seated", () => Joined(1) && Host().SlotLines.Count == 4 && Host().SlotLines[1] == "2. Friend A"),
            new Act("friend B joins", () => Connect(2, "Friend B")),
            new WaitUntil("friend B is seated", () => Joined(2) && Host().SlotLines[2] == "3. Friend B"),
            new AssertQuery("the slot table equals the lobby's state", () =>
                Host().SlotLines.SequenceEqual(Expected()) && Host().SlotLines[0] == "1. Host (you, the host)" && Host().SlotLines[3] == "4. Open"),
            new AssertQuery("the settings are shown read-only", () =>
                Host().SettingsLines.Contains("Ruleset: Civ V - Vanilla") && Host().SettingsLines.Contains("Difficulty: King") && Host().SettingsLines.Contains("Turns: Simultaneous, timer: 5 min")),
            new AssertQuery("Start waits for everybody and says so", () =>
                Host().StartButton.Disabled && Host().StartReason == "Waiting for Host, Friend A, Friend B to be ready."),
            new Capture("waiting", "Host's lobby: three people seated, one open slot, nobody ready; Start is off and says who it waits for. Settings are read-only on the right."),
        ];

        private ScenarioStep[] Picking() =>
        [
            new Press("Pick nation"),
            new WaitFrames(2),
            new Click(() => Centre(() => Host().NationButton("Babylon"))),
            new Act("scroll to the colours", () => Host().Reveal(Host().ColourButton("Blue").NotNull())),
            new WaitFrames(2),
            new Click(() => Centre(() => Host().ColourButton("Blue"))),
            new WaitUntil("the host's nation and colour reached the lobby", () => Host().State is { } s && s.Slots[0] is { Nation: "Babylon", Color: "Blue" }),
            new AssertQuery("Babylon is taken for everybody else", () => Host().NationButton("Babylon") is { Disabled: false } && lobby.Lobby.Slots[0].Nation == "Babylon"),
            new Act("scroll back up", () => Host().Reveal(Host().NationButton("Random").NotNull())),
            new WaitFrames(2),
            new Capture("picking", "Host picks a nation and a colour: the picker lists Random and every playable nation in its colours, with the chosen one pressed."),
            new Press("Pick nation"),
            new Press("Ready"),
            new Act("the friends pick, get ready and talk", () =>
            {
                _ = sessions[1].NotNull().SetLobbySlotAsync(1, "Greece", "Green", true);
                _ = sessions[2].NotNull().SetLobbySlotAsync(2, "Random", "Red", true);
                _ = sessions[1].NotNull().SendChatAsync("Ready when you are");
            }),
            new WaitUntil("the chat arrived", () => Host().ChatLines.Contains("Friend A: Ready when you are")),
            new Act("the host answers in the chat", () => Host().ChatField.Text = "Starting soon"),
            new Press("Send"),
            new WaitUntil("everybody is ready and the answer is in the chat", () =>
                Host().State is { } s && s.Slots.Take(3).All(it => it.Ready) && Host().ChatLines.Contains("Host: Starting soon") && !Host().StartButton.Disabled),
            new AssertQuery("the lobby agrees on nations and colours", () =>
                lobby.Lobby.Slots[0].Nation == "Babylon" && lobby.Lobby.Slots[1].Nation == "Greece" && lobby.Lobby.Slots[2].Color == "Red" && Host().StartReason == "Everybody is ready."),
            new Capture("ready", "Everybody is ready: all three show Ready, the chat has both lines, Start is on."),
        ];

        private ScenarioStep[] Joiner() =>
        [
            new Act("friend A's screen opens next to the host's", () =>
            {
                Host().Visible = false;
                friendScreen = new LobbyScreen(theme, nations, sessions[1].NotNull(), identities[1].PlayerKey);
                friendScreen.GameStarting += () => friendSawStart = true;
                root.AddChild(friendScreen);
            }),
            new WaitFrames(4),
            new AssertQuery("the joiner sees the same table and settings, with no host controls", () =>
            {
                var f = friendScreen.NotNull();
                return f.SlotLines.Count == 4 && f.SlotLines[0] == "1. Host (host)" && f.SlotLines[1] == "2. Friend A (you)" && f.SlotLines[3] == "4. Open"
                    && f.SettingsLines.SequenceEqual(Host().SettingsLines) && !f.StartButton.Visible && f.KickButton(0) == null && f.KickButton(2) == null && f.CloseButton(3) == null
                    && f.ReadyButton.Visible && f.ChatLines.Contains("Host: Starting soon");
            }),
            new Capture("joiner", "Friend A's lobby: own seat marked (you), the same settings read-only, no Kick/Close/Start, only Ready, nation and chat."),
            new Act("a friend with other rules tries to join", () =>
            {
                var (client, hostEnd) = InMemoryTransport.CreatePair();
                _ = lobby.AttachConnectionAsync(hostEnd);
                var odd = new ClientSession(_ => Task.FromResult<ITransport>(client), new ClientSessionOptions
                {
                    Identity = ClientIdentity.Generate(),
                    DisplayName = "Mod friend",
                    RulesetHash = "not-the-hosts",
                    ExpectedFingerprint = Fingerprint,
                    MaxReconnectAttempts = 0,
                });
                var join = odd.JoinAsync();
                join.ContinueWith(task => mismatch = task.Result, TaskScheduler.Default);
            }),
            new WaitUntil("the host refused", () => mismatch != null),
            new Act("the refused friend sees why", () =>
            {
                friendScreen.NotNull().Visible = false;
                refusedScreen = LobbyScreen.Refused(theme, nations, mismatch.NotNull().Code.NotNull());
                root.AddChild(refusedScreen);
            }),
            new WaitFrames(3),
            new AssertQuery("the reason is the ruleset one and nothing is seated", () =>
                mismatch is { Joined: false, Code: JoinRefusedCodes.RulesetMismatch } && refusedScreen.NotNull().Message.Contains("different rules", StringComparison.Ordinal)
                && lobby.Lobby.Slots.Count(it => it.Kind == SlotKind.Human) == 3),
            new Capture("refused", "A joiner whose rules differ from the host's: the lobby refuses with a plain sentence about base ruleset and mods; no seat is taken."),
            new Act("back to the host", () =>
            {
                refusedScreen.NotNull().Visible = false;
                Host().Visible = true;
            }),
        ];

        private ScenarioStep[] Hosting() =>
        [
            new Click(() => Centre(() => Host().KickButton(2))),
            new WaitUntil("friend B was removed", () => Host().State is { } s && s.Slots[2].Kind == "Open"),
            new Act("friend B's session saw why", () =>
            {
                while (sessions[2].NotNull().Events.TryRead(out var e))
                    if (e is JoinRefusedEvent { Code: JoinRefusedCodes.Kicked }) bSawKick = true;
            }),
            new AssertQuery("friend B was told, the seat is free and unbound", () => bSawKick && lobby.Lobby.Slots[2].PlayerKeyId == null && Host().SlotLines[2] == "3. Open"),
            new Click(() => Centre(() => Host().CloseButton(2))),
            new WaitUntil("the slot is closed", () => Host().State is { } s && s.Slots[2].Kind == "Closed"),
            new AssertQuery("the slot table shows it closed and Open can undo it", () => Host().SlotLines.ElementAtOrDefault(2) == "3. Closed" && Host().OpenButton(2) != null && Host().StartButton is { Disabled: false }),
            new Capture("kicked", "After the host kicked Friend B and closed the slot: slot 3 is Closed (with Open to undo), slot 4 is still Open and will become AI; Start is on."),
            new Press("Start game"),
            new WaitUntil("the game started", () => lobby.ActiveGameHost != null && friendSawStart, 6000),
            new AssertQuery("it has the two humans and an AI in the open slot", () =>
                lobby.Lobby.Slots[3].Kind == SlotKind.AI && lobby.Lobby.Slots[0].Kind == SlotKind.Human && lobby.Lobby.Slots[1].Kind == SlotKind.Human),
            new Act("clean up", () =>
            {
                foreach (var s in sessions) _ = s?.DisposeAsync().AsTask();
                _ = lobby.DisposeAsync().AsTask();
            }),
        ];
    }
}
