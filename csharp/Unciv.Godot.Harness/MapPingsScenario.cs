using Godot;
using Unciv.Assets;
using Unciv.Commands;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Play;
using Unciv.Harness;
using Unciv.Logic.Map;
using Unciv.Models.Ruleset;
using Unciv.Net;
using Unciv.Net.Visibility;
using Unciv.Queries;

namespace Unciv.GodotHarness;

/// <summary>
/// C090: shared map pings in a hosted game, seen by Alice (Babylon) with an ally, Bob. The host's relay is scripted by this scenario (its rules are tested against the real <c>GameHost</c>
/// in <c>HostPingTests</c>): it echoes Alice's ping back with her name, as the host does. Right-click on a tile offers three ping entries (a game that is not hosted does not offer them);
/// the ring, column and name stand on the tile and fade; Bob's ping arrives and shows beside it; Ctrl+P pings the tile under the pointer; after the lifetime they are gone. Nothing is
/// sent to the game: the command log keeps its length.
/// </summary>
public sealed class MapPingsScenario : Scenario
{
    public override string Name => "map-pings";

    public override bool Uses3D => true;

    public override bool Solo => true;

    public override IReadOnlyList<string> Checklist =>
    [
        "Does a ping stand out on the map (ring, column, who and what) without hiding the tile?",
        "Is it clear which ping is fresh and which is about to go?",
    ];

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session)
    {
        var script = new ScriptedPingSession(new LocalHost(session, ScenarioGame.HumanA, () => 1_000_000));
        var screen = new GameScreen();
        host.AddChild(screen);
        screen.Start(script, AssetRegistry.ForRuleset(session.Game.Ruleset));
        screen.TutorialsEnabled = false;

        var human = session.Game.GetCivilization(ScenarioGame.HumanA);
        var lands = human.Units.GetCivUnits().OrderBy(it => it.Id).First().CurrentTile.Neighbors.Where(it => it.IsLand).ToList();
        var tileA = lands[0].Position;
        var tileB = lands[^1].Position;
        var tileC = lands[lands.Count / 2].Position;
        int logged = 0;

        float Left(HexCoord tile) => screen.Pings.Single(it => it.X == tile.x && it.Y == tile.y).Remaining;
        Vector2 Pointer(HexCoord tile) => screen.Map.ScreenPositionOf(tile);

        return
        [
            new WaitFrames(3),
            new Act("dismiss the banner", () =>
            {
                screen.Hud.HideBanner();
                logged = session.Log.Count;
            }),
            new AssertQuery("no ping yet", () => screen.Pings.Count == 0 && screen.PingLayer.Shown.Count == 0),
            new Act("right-click tile A", () => InputSimulator.SimulateMouseClick(screen.GetViewport(), Pointer(tileA), MouseButton.Right)),
            new AssertQuery("the menu offers the three pings", () => screen.MarkerMenu.Visible && screen.MarkerMenu.PingLookButton.Visible && screen.MarkerMenu.PingDangerButton.Visible && screen.MarkerMenu.PingHelpButton.Visible),
            new Capture("menu", "Right-click on a tile in a hosted game: below the marker entries, three ping entries (Look here, Danger, Help) tell the allies"),
            new Press("Ping allies: Danger"),
            new WaitFrames(2),
            new AssertQuery("the ping went to the host as asked, the host's echo is on tile A with Alice's name, and the menu closed", () =>
                script.Sent.SequenceEqual([(tileA.x, tileA.y, MapPingKinds.Danger)]) && !screen.MarkerMenu.Visible
                && screen.PingLayer.Shown.ContainsKey(tileA) && screen.PingLayer.LabelAt(tileA) == "Alice: Danger"),
            new Capture("sent", "A fresh Danger ping on the tile: a red ring, a column and 'Alice: Danger' above it"),
            new Act("five seconds pass, then Bob pings another tile", () =>
            {
                screen.AdvancePings(5f);
                script.Receive("Bob", tileB, MapPingKinds.Look);
            }),
            new WaitFrames(2),
            new AssertQuery("both pings show; Alice's has faded more than Bob's", () =>
                screen.Pings.Count == 2 && screen.PingLayer.LabelAt(tileB) == "Bob: Look here" && Left(tileA) < Left(tileB)),
            new Capture("ally", "An ally's blue 'Bob: Look here' ping on a second tile, bright; Alice's earlier red ping beside it is already fading"),
            new Act("pointer onto tile C", () => InputSimulator.SimulateMouseMove(screen.GetViewport(), Pointer(tileC))),
            new WaitFrames(2),
            new PressKey(Key.P, Ctrl: true),
            new WaitFrames(2),
            new AssertQuery("Ctrl+P pinged tile C with Look here", () => script.Sent.Last() == (tileC.x, tileC.y, MapPingKinds.Look) && screen.PingLayer.Shown.ContainsKey(tileC)),
            new Act("a ping on a tile off the map is ignored", () => script.Receive("Bob", new HexCoord(900, 900), MapPingKinds.Help)),
            new Act("four more seconds pass", () => screen.AdvancePings(4f)),
            new WaitFrames(2),
            new AssertQuery("Alice's first ping ran out, Bob's and the newest remain, nothing off the map", () =>
                screen.Pings.Count == 2 && screen.PingLayer.Shown.ContainsKey(tileB) && screen.PingLayer.Shown.ContainsKey(tileC) && !screen.PingLayer.Shown.ContainsKey(tileA)),
            new Act("the rest pass", () => screen.AdvancePings(MapPingModel.Lifetime)),
            new AssertQuery("all pings are gone", () => screen.Pings.Count == 0 && screen.PingLayer.Shown.Count == 0),
            new AssertQuery("no ping sent a command to the game", () => session.Log.Count == logged),
        ];
    }

    /// <summary>A hosted game as the scenario scripts it: the relay echoes what Alice sends (with her name) and shows what Bob sends.</summary>
    private sealed class ScriptedPingSession : IGameSession, IGameSocial
    {
        private readonly LocalHost inner;

        public ScriptedPingSession(LocalHost inner) => this.inner = inner;

        public List<(int X, int Y, string Kind)> Sent { get; } = [];

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

        public IReadOnlyList<ChatLine> Chat => [];

        public event Action<ChatLine>? ChatReceived
        {
            add { }
            remove { }
        }

        public Task SendChatAsync(string text) => Task.CompletedTask;

        public event Action<MapPingNotice>? PingReceived;

        public Task SendPingAsync(int x, int y, string kind)
        {
            Sent.Add((x, y, kind));
            Receive("Alice", new HexCoord(x, y), kind);
            return Task.CompletedTask;
        }

        public void Receive(string sender, HexCoord tile, string kind) => PingReceived?.Invoke(new MapPingNotice { X = tile.x, Y = tile.y, Kind = kind, Sender = sender });

        public IReadOnlyList<PlayerRow> Players => [];

        public event Action? PlayersChanged
        {
            add { }
            remove { }
        }

        public bool IsHost => false;

        public Task<string?> KickAsync(string civId) => Task.FromResult<string?>("not-allowed");

        public Task<string?> RebindAsync(string civId, string newPlayerKey) => Task.FromResult<string?>("not-allowed");

        public Task ResyncAsync() => inner.ResyncAsync();

        public Task<TResult?> QueryAsync<TResult>(IQuery<TResult> query) where TResult : class => inner.QueryAsync(query);

        public Task<SubmitResult> SubmitAsync(GameCommand command) => inner.SubmitAsync(command);

        public Task<TurnOutcome> EndTurnAsync(IProgress<TurnProgress>? progress = null, CancellationToken cancellation = default) => inner.EndTurnAsync(progress, cancellation);

        public Task<TurnOutcome> AutoPlayTurnAsync(IProgress<TurnProgress>? progress = null, CancellationToken cancellation = default) => inner.AutoPlayTurnAsync(progress, cancellation);

        public Task<string?> ExportSaveAsync() => inner.ExportSaveAsync();
    }
}
