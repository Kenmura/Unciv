using System.Text.Json;
using Godot;
using Unciv.Assets;
using Unciv.Commands;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Play;
using Unciv.Harness;
using Unciv.Models.Ruleset;
using Unciv.Net;
using Unciv.Net.Identity;
using Unciv.Net.Visibility;
using Unciv.Queries;

namespace Unciv.GodotHarness;

/// <summary>
/// C045: the players-and-chat panel of a hosted game, seen by the host (Alice, Babylon) with a friend (Bob, Greece). The host's answers are scripted by this scenario (the rules of kick,
/// rebind and chat are tested against the real <c>GameHost</c> in <c>HostAdminTests</c>); the screen is the real <see cref="GameScreen"/> over a real <see cref="LocalHost"/> for the map.
/// Alice opens the panel, Bob chats and Alice answers through the chat line, a rebind with a malformed key is refused and shown, a rebind with a good key leaves the seat waiting for the new
/// device, the new device connects, and Alice kicks Bob, whose seat shows as released.
/// </summary>
public sealed class MpSocialScenario : Scenario
{
    /// <summary>A well-formed key for the new device; it is never drawn (the field is cleared when applied), so its random value cannot reach a golden image.</summary>
    private static readonly string NewKey = ClientIdentity.Generate().PlayerKey;

    public override string Name => "mp-social";

    public override bool Uses3D => true;

    public override IReadOnlyList<string> Checklist =>
    [
        "Is it clear who is in the game, who is online, and that only the host has Kick and Rebind?",
        "Can you tell a chat line's sender from its text, and does a refused admin action say why?",
        "Does the player list show the difference between 'waiting for a new device' and 'released'?",
    ];

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session)
    {
        var script = new ScriptedSocialSession(new LocalHost(session, ScenarioGame.HumanA, () => 1_000_000));
        var screen = new GameScreen();
        host.AddChild(screen);
        screen.Start(script, AssetRegistry.ForRuleset(session.Game.Ruleset));
        screen.Hud.HoldBanner = true;

        PlayerRow Bob() => screen.Social.Rows.Single(it => it.CivId == ScenarioGame.HumanB);

        return
        [
            new WaitFrames(4),
            new AssertQuery("the Players button is there and the panel is closed", () => screen.PlayersButton.Visible && !screen.Social.Visible),
            new Press("Players"),
            new WaitFrames(3),
            new AssertQuery("the host sees both players, Bob online, with Kick and Rebind on Bob's row only", () =>
                screen.Social.Visible && screen.Social.Rows.Count == 2 && Bob().Status == SeatStatus.Online && Bob().CanKick && Bob().CanRebind
                && screen.Social.Rows.Single(it => it.IsYou) is { CanKick: false, CanRebind: false, Name: "Alice" }),
            new Capture("players", "The Players panel, top right: Alice (you) and Bob with green dots and 'Online'; only Bob's row has Kick and Rebind; the chat below is empty"),
            new Act("Bob says hello", () => script.Say("Bob", "Hello Alice!")),
            new Act("Alice types an answer", () => screen.Social.SetTyped("Hi Bob, ready when you are")),
            new Press("Send"),
            new WaitUntil("the host echoed Alice's line", () => screen.Social.ChatTexts.Count == 2, 300),
            new WaitFrames(3),
            new AssertQuery("both lines are in the log with the host-stamped sender, and the typed line was cleared", () =>
                screen.Social.ChatTexts.SequenceEqual(["Bob: Hello Alice!", "Alice: Hi Bob, ready when you are"]) && script.Sent.SequenceEqual(["Hi Bob, ready when you are"])),
            new Capture("chat", "Two chat lines, 'Bob: Hello Alice!' and 'Alice: Hi Bob, ready when you are'; the sender is the name the host stamped"),
            new Press("Rebind"),
            new Act("Alice pastes a malformed key", () => screen.Social.SetKeyTyped("not a key")),
            new Press("Rebind seat"),
            new WaitUntil("the host refused", () => screen.Social.NoticeText.StartsWith("Not done"), 300),
            new AssertQuery("the refusal is shown in words and nothing changed", () =>
                screen.Social.NoticeText == "Not done: that is not a player key" && Bob().Status == SeatStatus.Online),
            new Capture("refused", "The rebind was refused: the notice under the chat reads 'Not done: that is not a player key', Bob is still Online"),
            new Press("Rebind"),
            new Act("Alice pastes Bob's new device key", () => screen.Social.SetKeyTyped(NewKey)),
            new Press("Rebind seat"),
            new WaitUntil("the seat waits for the new device", () => Bob().Status == SeatStatus.AwaitingDevice, 300),
            new WaitFrames(3),
            new AssertQuery("the seat is bound to the new key and Bob's old session is gone", () =>
                script.ReboundTo == NewKey && !Bob().CanKick && Bob().CanRebind && screen.Social.NoticeText.StartsWith("Seat rebound")),
            new Capture("rebound", "Bob's row is amber 'Waiting for new device' (no Kick any more, Rebind still possible); the notice says the seat is rebound"),
            new Act("Bob's new device connects", () => script.NewDeviceConnects()),
            new WaitFrames(3),
            new AssertQuery("Bob is online again", () => Bob().Status == SeatStatus.Online && Bob().CanKick),
            new Press("Kick"),
            new WaitUntil("the seat is released", () => Bob().Status == SeatStatus.Released, 300),
            new WaitFrames(3),
            new AssertQuery("Bob's seat is released and the host's notice says so", () =>
                script.Kicked.SequenceEqual([ScenarioGame.HumanB]) && screen.Social.NoticeText == "Seat released." && !Bob().CanKick),
            new Capture("kicked", "Bob's row is amber 'Released (AI)' with only Rebind left; the notice reads 'Seat released.'"),
        ];
    }

    /// <summary>The host as the scenario scripts it, around the real session of the viewer.</summary>
    private sealed class ScriptedSocialSession : IGameSession, IGameSocial
    {
        private readonly LocalHost inner;
        private readonly List<LobbySlot> slots =
        [
            new() { Slot = 0, PlayerName = "Alice", Nation = ScenarioGame.HumanA, Kind = "Human", PlayerKeyId = "alice-key", Connected = true, Ready = false },
            new() { Slot = 1, PlayerName = "Bob", Nation = ScenarioGame.HumanB, Kind = "Human", PlayerKeyId = "bob-key", Connected = true, Ready = false },
        ];
        private IReadOnlyList<ChatLine> chat = [];

        public ScriptedSocialSession(LocalHost inner) => this.inner = inner;

        public List<string> Sent { get; } = [];

        public List<string> Kicked { get; } = [];

        public string? ReboundTo { get; private set; }

        public string ViewerCivId => inner.ViewerCivId;

        public Ruleset Ruleset => inner.Ruleset;

        public ClientSnapshot Snapshot => inner.Snapshot;

        public event Action<ClientSnapshot>? SnapshotChanged
        {
            add { inner.SnapshotChanged += value; }
            remove { inner.SnapshotChanged -= value; }
        }

        public event Action<EventBatch>? EventsReceived
        {
            add { inner.EventsReceived += value; }
            remove { inner.EventsReceived -= value; }
        }

        public event Action<string>? Desynchronized
        {
            add { }
            remove { }
        }

        public IReadOnlyList<ChatLine> Chat => chat;

        public event Action<ChatLine>? ChatReceived;

        public IReadOnlyList<PlayerRow> Players => SocialModel.Players(new LobbyState { Settings = JsonDocument.Parse("{}").RootElement.Clone(), Slots = [.. slots], HostName = "Alice" }, ViewerCivId, IsHost);

        public event Action<MapPingNotice>? PingReceived
        {
            add { }
            remove { }
        }

        public Task SendPingAsync(int x, int y, string kind) => Task.CompletedTask;

        public event Action? PlayersChanged;

        public bool IsHost => true;

        public void Say(string sender, string text)
        {
            var line = new ChatLine(sender, text);
            chat = SocialModel.Append(chat, line);
            ChatReceived?.Invoke(line);
        }

        public Task SendChatAsync(string text)
        {
            Sent.Add(text);
            Say("Alice", text);
            return Task.CompletedTask;
        }

        private void Set(int index, Func<LobbySlot, LobbySlot> change)
        {
            slots[index] = change(slots[index]);
            PlayersChanged?.Invoke();
        }

        public Task<string?> KickAsync(string civId)
        {
            Kicked.Add(civId);
            Set(1, it => it with { Kind = "AI", PlayerKeyId = null, Connected = false });
            return Task.FromResult<string?>(null);
        }

        public Task<string?> RebindAsync(string civId, string newPlayerKey)
        {
            if (!PlayerKeys.IsValid(newPlayerKey)) return Task.FromResult<string?>("bad-key");
            ReboundTo = newPlayerKey;
            Set(1, it => it with { Kind = "AI", PlayerName = null, PlayerKeyId = newPlayerKey, Connected = false });
            return Task.FromResult<string?>(null);
        }

        public void NewDeviceConnects() => Set(1, it => it with { Kind = "Human", PlayerName = "Bob", Connected = true });

        public Task ResyncAsync() => inner.ResyncAsync();

        public Task<TResult?> QueryAsync<TResult>(IQuery<TResult> query) where TResult : class => inner.QueryAsync(query);

        public Task<SubmitResult> SubmitAsync(GameCommand command) => inner.SubmitAsync(command);

        public Task<TurnOutcome> EndTurnAsync(IProgress<TurnProgress>? progress = null, CancellationToken cancellation = default) => inner.EndTurnAsync(progress, cancellation);

        public Task<TurnOutcome> AutoPlayTurnAsync(IProgress<TurnProgress>? progress = null, CancellationToken cancellation = default) => inner.AutoPlayTurnAsync(progress, cancellation);

        public Task<string?> ExportSaveAsync() => inner.ExportSaveAsync();
    }
}
