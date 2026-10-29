using Godot;
using Unciv.Assets;
using Unciv.Commands;
using Unciv.Commands.City;
using Unciv.Commands.Empire;
using Unciv.Commands.Events;
using Unciv.Commands.Turn;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Play;
using Unciv.Harness;
using Unciv.Models.Ruleset;
using Unciv.Net.Visibility;
using Unciv.Queries;
using Unciv.Utils;

namespace Unciv.GodotHarness;

/// <summary>
/// C036: the multiplayer turn bar with two scripted humans (Babylon, the viewer, and Greece). The turn controller is played by this scenario: it owns the TurnStatus the host would send
/// (who ended, deadline, phase) and the roster (who is connected), over a real <see cref="LocalHost"/> for the map and the queries. The viewer ends the turn through the HUD button, the ended mark
/// and the headline follow the status, Undo end turn takes it back, Greece drops offline and ends, a toast names the visible action, and while the round is Resolving the overlay is up and every
/// command is refused with <c>turn-resolving</c> and shown as such. Time is fixed, so the countdown and the golden images do not depend on the machine.
/// </summary>
public sealed class MpTurnScenario : Scenario
{
    private const long Now = 1_000_000;
    private const long Deadline = Now + 65_000;

    public override string Name => "mp-turn";

    public override bool Uses3D => true;

    public override IReadOnlyList<string> Checklist =>
    [
        "Is it clear whom the game is waiting for, and which of them is offline?",
        "Is it clear that you have ended your turn, that you can still take it back, and when the turn is forced to end?",
        "While the host resolves the turn, is it obvious that nothing can be done?",
    ];

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session)
    {
        var game = session.Game;
        var script = new ScriptedTurnSession(new LocalHost(session, ScenarioGame.HumanA, () => Now), Deadline);
        var screen = new GameScreen { NowMs = () => Now };
        host.AddChild(screen);
        screen.Start(script, AssetRegistry.ForRuleset(game.Ruleset));
        screen.Hud.HoldBanner = true;

        TurnBarRow Row(string civ) => screen.TurnBar.Data.NotNull().Rows.Single(it => it.CivId == civ);
        bool Playing() => screen.TurnBar.Data is { Resolving: false } && script.Snapshot.TurnStatus.Phase == "Playing";

        void AnswerDecisions()
        {
            var city = game.GetCivilization(ScenarioGame.HumanA).Cities[0];
            foreach (var command in new GameCommand[] { new QueueConstruction { CityId = city.Id, Name = "Warrior" }, new SetResearchQueue { Techs = ["Pottery"] } })
                script.SubmitAsync(command).GetAwaiter().GetResult();
        }

        return
        [
            new WaitFrames(4),
            new Act("Alice has answered the pending decisions, so the only open item is the units waiting for orders", AnswerDecisions),
            new WaitFrames(4),
            new AssertQuery("the bar shows both humans, nobody ended, and the countdown from the host's deadline", () =>
                screen.TurnBar.Visible && screen.TurnBar.Data.NotNull().Rows.Count == 2 && screen.TurnBar.Data.Rows.All(it => !it.Ended && it.Connected)
                && screen.TurnBar.HeadlineText == "Waiting for Alice, Bob" && screen.TurnBar.CountdownText == "1:05" && screen.Hud.EndTurnShown),
            new Capture("waiting", "Turn bar under the top bar: 'Waiting for Alice, Bob', countdown 1:05, both players green and Playing; the Next turn button is bottom right beside 'Next unit'"),
            new Press("Next turn"),
            new WaitUntil("the host marks Alice as ended", () => Row("Babylon").Ended, 300),
            new WaitFrames(2),
            new AssertQuery("only Alice ended: the headline waits for Bob, and the button became Undo end turn", () =>
                screen.TurnBar.HeadlineText == "Waiting for Bob" && !Row("Greece").Ended && screen.Hud.ActionText == "Undo end turn" && !screen.Hud.ActionDisabled),
            new Act("Greece drops offline", () => script.SetConnected(ScenarioGame.HumanB, false)),
            new WaitFrames(2),
            new AssertQuery("the bar marks Bob offline in red and the headline says so", () =>
                !Row("Greece").Connected && screen.TurnBar.HeadlineText == "Waiting for Bob (offline)"),
            new Capture("ended-offline", "Alice (you) has ended her turn and the button reads Undo end turn; the bar says 'Waiting for Bob (offline)' with Bob's dot red"),
            new Press("Undo end turn"),
            new WaitUntil("the host takes Alice's end back", () => !Row("Babylon").Ended, 300),
            new WaitFrames(2),
            new AssertQuery("undo restored the turn: Alice is playing again and the Next turn button is back", () =>
                screen.Hud.EndTurnShown && screen.TurnBar.HeadlineText == "Waiting for Alice, Bob (offline)" && screen.StatusText == "Your turn continues."),
            new Act("Greece comes back and ends the turn", () =>
            {
                script.SetConnected(ScenarioGame.HumanB, true);
                script.OtherHumanEnds(ScenarioGame.HumanB);
            }),
            new WaitFrames(2),
            new AssertQuery("the ended mark follows the status, and a toast names Bob's action", () =>
                Row("Greece").Ended && Row("Greece").Connected && screen.TurnBar.HeadlineText == "Waiting for Alice" && screen.TurnBar.ToastTexts.SequenceEqual(["Bob ended their turn"])),
            new Capture("other-ended", "Bob has ended his turn (green 'Ended'); the bar says 'Waiting for Alice' and a toast under it reads 'Bob ended their turn'"),
            new Press("Next turn"),
            new WaitUntil("both ended", () => script.Snapshot.TurnStatus.Ended.Count == 2, 300),
            new Act("the host starts resolving the round", () => script.SetPhase("Resolving")),
            new WaitFrames(2),
            new AssertQuery("the resolving overlay is up and the button says so", () =>
                screen.TurnOverlay.Visible && screen.TurnOverlay.HeadingText == "Resolving the turn" && screen.TurnBar.HeadlineText == TurnBarModel.ResolvingHeadline
                && screen.Hud.ActionText == "Resolving turn..." && screen.Hud.ActionDisabled && screen.IsResolving),
            new Act("a command during Resolving: End turn by hotkey", () => screen.TryHotkey("Space")),
            new WaitUntil("the host refused with turn-resolving", () => script.Refusals.Count == 1, 300),
            new WaitFrames(2),
            new AssertQuery("the refusal is shown as turn-resolving", () =>
                script.Refusals[0] == "turn-resolving" && screen.StatusText == "Cannot end the turn: the host is resolving the turn"),
            new Capture("resolving", "The host is resolving the round: a dimmed overlay 'Resolving the turn / Waiting for the host' covers the game, the bar says 'Resolving the turn...', and the status line shows the refusal"),
            new Act("the host finishes: turn 2 starts with nobody ended", () => script.StartNextTurn()),
            new WaitFrames(2),
            new AssertQuery("the overlay is gone and the marks are cleared for the new turn", () =>
                !screen.TurnOverlay.Visible && Playing() && screen.TurnBar.Data.NotNull().Rows.All(it => !it.Ended) && screen.TurnBar.HeadlineText == "Waiting for Alice, Bob"),
        ];
    }

    /// <summary>The host's turn controller as the scenario scripts it, around the real session of the viewer.</summary>
    private sealed class ScriptedTurnSession : IGameSession, ITurnRoster
    {
        private readonly LocalHost inner;
        private readonly long deadline;
        private readonly List<TurnRosterEntry> roster = [new("Babylon", "Alice", true), new("Greece", "Bob", true)];
        private readonly List<string> ended = [];
        private string phase = "Playing";
        private int turnOffset;
        private long seq = 1000;

        public ScriptedTurnSession(LocalHost inner, long deadline)
        {
            this.inner = inner;
            this.deadline = deadline;
            Snapshot = Wrap(inner.Snapshot);
        }

        /// <summary>The refusal codes the scripted host sent, in order.</summary>
        public List<string> Refusals { get; } = [];

        public string ViewerCivId => inner.ViewerCivId;

        public Ruleset Ruleset => inner.Ruleset;

        public ClientSnapshot Snapshot { get; private set; }

        public IReadOnlyList<TurnRosterEntry> Roster => roster;

        public event Action<ClientSnapshot>? SnapshotChanged;

        public event Action<EventBatch>? EventsReceived;

        public event Action<string>? Desynchronized
        {
            add { }
            remove { }
        }

        public event Action? RosterChanged;

        private ClientSnapshot Wrap(ClientSnapshot basis) => basis with
        {
            Seq = seq,
            Turn = basis.Turn + turnOffset,
            TurnStatus = new TurnStatusDto { Ended = [.. ended], Phase = phase, Deadline = deadline },
        };

        private void Publish(params GameEvent[] events)
        {
            seq++;
            Snapshot = Wrap(inner.Snapshot);
            if (events.Length > 0) EventsReceived?.Invoke(new EventBatch(seq, events));
            SnapshotChanged?.Invoke(Snapshot);
        }

        public void SetConnected(string civ, bool connected)
        {
            int index = roster.FindIndex(it => it.CivId == civ);
            roster[index] = roster[index] with { Connected = connected };
            RosterChanged?.Invoke();
        }

        public void OtherHumanEnds(string civ)
        {
            ended.Add(civ);
            Publish(new PlayerEndedTurn(civ, Snapshot.Turn));
        }

        public void SetPhase(string value)
        {
            phase = value;
            Publish(value == "Resolving" ? [new TurnResolving(Snapshot.Turn)] : []);
        }

        public void StartNextTurn()
        {
            ended.Clear();
            phase = "Playing";
            turnOffset++;
            Publish();
        }

        public Task ResyncAsync() => Task.CompletedTask;

        public Task<TResult?> QueryAsync<TResult>(IQuery<TResult> query) where TResult : class => inner.QueryAsync(query);

        public async Task<SubmitResult> SubmitAsync(GameCommand command)
        {
            if (phase == "Resolving")
            {
                Refusals.Add("turn-resolving");
                return new SubmitResult(false, "turn-resolving", []);
            }
            switch (command)
            {
                case EndTurn when !ended.Contains(ViewerCivId):
                    ended.Add(ViewerCivId);
                    Publish(new PlayerEndedTurn(ViewerCivId, Snapshot.Turn));
                    return new SubmitResult(true, "", []);
                case EndTurn:
                    return new SubmitResult(false, "already-ended", []);
                case UndoEndTurn when ended.Remove(ViewerCivId):
                    Publish(new PlayerUndidEndTurn(ViewerCivId, Snapshot.Turn));
                    return new SubmitResult(true, "", []);
                case UndoEndTurn:
                    return new SubmitResult(false, "not-allowed", []);
            }
            var result = await inner.SubmitAsync(command);
            Publish(result.Events.ToArray());
            return result;
        }

        public Task<TurnOutcome> EndTurnAsync(IProgress<TurnProgress>? progress = null, CancellationToken cancellation = default) =>
            throw new NotSupportedException("A simultaneous game ends the turn by submitting EndTurn");

        public Task<TurnOutcome> AutoPlayTurnAsync(IProgress<TurnProgress>? progress = null, CancellationToken cancellation = default) =>
            throw new NotSupportedException("The scripted session has no auto-play");

        public Task<string?> ExportSaveAsync() => Task.FromResult<string?>(null);
    }
}
