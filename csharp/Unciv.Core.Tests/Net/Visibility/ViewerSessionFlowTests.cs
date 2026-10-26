using Unciv.Commands;
using Unciv.Commands.Events;
using Unciv.Commands.Turn;
using Unciv.Harness;
using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Net.Visibility;

namespace Unciv.Core.Tests.Net.Visibility;

// C037: guards the wiring the LocalHost relies on. Every outbound event path (Published callback, SubmitResult.Events, TurnOutcome.Events) must go
// through ViewerEventFilter; a flow that forwards the engine's raw events fails here.
[Collection("RulesetCache")]
public sealed class ViewerSessionFlowTests
{
    private const int Turns = 30;

    private static bool IsAiOnly(GameEvent e) =>
        e is ResearchCompleted { CivId: ScenarioGame.Ai } or PolicyAdopted { CivId: ScenarioGame.Ai } or AlertAdded { CivId: ScenarioGame.Ai }
            or UnitMoved { CivId: ScenarioGame.Ai };

    // What the engine itself produced for the same commands, unfiltered: proves the AI really emits events the viewer must not get.
    private static List<GameEvent> RawEventsOfTurns()
    {
        var session = ScenarioGame.Create(solo: true);
        var raw = new List<GameEvent>();
        for (int i = 0; i < Turns; i++)
        {
            raw.AddRange(session.Submit(ScenarioGame.HumanA, new EndTurn { Now = ScenarioGame.FixedClockMillis + i }).Events);
            while (session.Game.TurnPhase is TurnPhase.AutoPlayers(var next))
                raw.AddRange(session.SubmitAsServer(new RunAiTurn { CivId = next.CivId }).Events);
            raw.AddRange(session.SubmitAsServer(new BeginTurn { CivId = ScenarioGame.HumanA, Now = ScenarioGame.FixedClockMillis + i }).Events);
        }
        return raw;
    }

    [Fact]
    public void EndTurn_NeverLeaksAiOnlyEvents_ThroughPublishedOrOutcome()
    {
        var raw = RawEventsOfTurns();
        Assert.Contains(raw, IsAiOnly); // the test is only meaningful if the engine does emit them

        var published = new List<GameEvent>();
        var session = ScenarioGame.Create(solo: true);
        long clock = ScenarioGame.FixedClockMillis;
        var flow = new ViewerSessionFlow(session, ScenarioGame.HumanA, () => clock++, (_, events) => published.AddRange(events));
        var outcomeEvents = new List<GameEvent>();
        var submitEvents = new List<GameEvent>();
        for (int i = 0; i < Turns; i++)
        {
            var outcome = flow.EndTurn();
            Assert.True(outcome.Accepted, outcome.Message);
            outcomeEvents.AddRange(outcome.Events);
        }

        Assert.NotEmpty(published); // the viewer still gets its own events
        Assert.DoesNotContain(published, IsAiOnly);
        Assert.DoesNotContain(outcomeEvents, IsAiOnly);
        Assert.Equal(published.Count, outcomeEvents.Count);

        // A directly submitted command is filtered too.
        var result = flow.Submit(new EndTurn { Now = clock++ });
        submitEvents.AddRange(result.Events);
        Assert.DoesNotContain(submitEvents, IsAiOnly);
    }

    [Fact]
    public void Submit_StampsAnUnstampedTimedCommandWithTheClock_AndKeepsAnExplicitNow()
    {
        var session = ScenarioGame.Create(solo: true);
        long clock = ScenarioGame.FixedClockMillis;
        var flow = new ViewerSessionFlow(session, ScenarioGame.HumanA, () => clock++, (_, _) => { });
        var resign = new Resign();
        var result = flow.Submit(resign);
        Assert.True(result.Accepted, result.Message);
        Assert.Equal(ScenarioGame.FixedClockMillis, resign.Now);
        Assert.Equal(ScenarioGame.FixedClockMillis + 1, clock);
        Assert.Equal(PlayerType.AI, session.Game.GetCivilization(ScenarioGame.HumanA).PlayerType);

        var other = new Resign { Now = 12345 };
        flow.Submit(other); // refused (no longer a human), but the explicit value must stay untouched
        Assert.Equal(12345, other.Now);
    }

    [Fact]
    public void AutoPlayTurn_WithoutARunIsRefused_AndChangesNothing()
    {
        var session = ScenarioGame.Create(solo: true);
        long clock = ScenarioGame.FixedClockMillis;
        var flow = new ViewerSessionFlow(session, ScenarioGame.HumanA, () => clock++, (_, _) => { });
        int turn = session.Game.Turns;
        int logged = session.Log.Count;
        var outcome = flow.AutoPlayTurn();
        Assert.False(outcome.Accepted);
        Assert.Contains("Auto-play is not running", outcome.Message);
        Assert.Equal(0, outcome.AiTurnsRun);
        Assert.False(outcome.NextTurnBegun);
        Assert.Equal(turn, session.Game.Turns);
        Assert.Equal(logged, session.Log.Count);
    }

    [Fact]
    public void AutoPlayTurn_PlaysTheRequestedTurnsThenHandsControlBack_AndTheFilterStillApplies()
    {
        var published = new List<GameEvent>();
        var session = ScenarioGame.Create(solo: true);
        long clock = ScenarioGame.FixedClockMillis;
        var flow = new ViewerSessionFlow(session, ScenarioGame.HumanA, () => clock++, (_, events) => published.AddRange(events));
        int startTurn = session.Game.Turns;
        Assert.True(flow.Submit(new StartAutoPlay { Turns = 3 }).Accepted);

        for (int i = 0; i < 3; i++)
        {
            var outcome = flow.AutoPlayTurn();
            Assert.True(outcome.Accepted, outcome.Message);
            Assert.True(outcome.NextTurnBegun);
            Assert.True(outcome.AiTurnsRun >= 1);
            Assert.DoesNotContain(outcome.Events, IsAiOnly);
        }

        Assert.DoesNotContain(published, IsAiOnly);
        Assert.Equal(startTurn + 3, session.Game.Turns);
        Assert.False(session.Game.ClientState.AutoPlay!.IsAutoPlaying());
        Assert.False(flow.AutoPlayTurn().Accepted); // out of turns: refused, control is the player's
        Assert.True(flow.EndTurn().Accepted);
        Assert.Equal(startTurn + 4, session.Game.Turns);
    }

    [Fact]
    public void AutoPlayTurn_StopsAtOnceAfterStopAutoPlay()
    {
        var session = ScenarioGame.Create(solo: true);
        long clock = ScenarioGame.FixedClockMillis;
        var flow = new ViewerSessionFlow(session, ScenarioGame.HumanA, () => clock++, (_, _) => { });
        Assert.True(flow.Submit(new StartAutoPlay { Turns = 50 }).Accepted);
        Assert.True(flow.AutoPlayTurn().Accepted);
        Assert.True(flow.Submit(new StopAutoPlay()).Accepted);
        Assert.False(flow.AutoPlayTurn().Accepted);
        Assert.False(flow.Submit(new StopAutoPlay()).Accepted);
    }

    private sealed class Recorder : IProgress<TurnProgress>
    {
        public List<TurnProgress> Reports { get; } = [];
        public Action<TurnProgress>? OnReport { get; init; }

        public void Report(TurnProgress value)
        {
            Reports.Add(value);
            OnReport?.Invoke(value);
        }
    }

    private static (GameSession Session, ViewerSessionFlow Flow) NewFlow()
    {
        var session = ScenarioGame.Create(solo: true);
        long clock = ScenarioGame.FixedClockMillis;
        return (session, new ViewerSessionFlow(session, ScenarioGame.HumanA, () => clock++, (_, _) => { }));
    }

    [Fact]
    public void EndTurn_ReportsEachAutoCivBeforeItPlays_WithAConsistentTotal()
    {
        var (_, flow) = NewFlow();
        var recorder = new Recorder();
        var outcome = flow.EndTurn(recorder);
        Assert.True(outcome.Accepted, outcome.Message);
        Assert.Equal(outcome.AiTurnsRun, recorder.Reports.Count);
        Assert.NotEmpty(recorder.Reports);
        for (int i = 0; i < recorder.Reports.Count; i++)
        {
            Assert.Equal(i, recorder.Reports[i].Played);
            Assert.True(recorder.Reports[i].Total > i);
            Assert.False(string.IsNullOrEmpty(recorder.Reports[i].CivId));
        }
        Assert.Equal(recorder.Reports[0].Total, recorder.Reports.Count);
    }

    [Fact]
    public void EndTurn_CancelledBetweenCivs_KeepsWhatWasPlayed_AndContinuingEndsInTheSameStateAsAnUninterruptedTurn()
    {
        var (whole, wholeFlow) = NewFlow();
        for (int i = 0; i < 3; i++) Assert.True(wholeFlow.EndTurn().Accepted);

        var (split, splitFlow) = NewFlow();
        for (int i = 0; i < 3; i++)
        {
            using var cts = new CancellationTokenSource();
            var cancelAtFirst = new Recorder { OnReport = r => { if (r.Played == 0) cts.Cancel(); } };
            var first = splitFlow.EndTurn(cancelAtFirst, cts.Token);
            if (first.Accepted) continue; // a round without an automatic civ cannot be interrupted
            Assert.Equal(ViewerSessionFlow.CancelledMessage, first.Message);
            Assert.False(first.NextTurnBegun);
            Assert.IsType<TurnPhase.AutoPlayers>(split.Game.TurnPhase); // stopped at a command boundary, resumable
            var rest = splitFlow.EndTurn();
            Assert.True(rest.Accepted, rest.Message);
            Assert.True(rest.NextTurnBegun);
        }
        Assert.Equal(ScenarioGame.StateHash(whole), ScenarioGame.StateHash(split));
    }

    [Fact]
    public void EndTurn_AlreadyCancelled_StillEndsTheHumanTurnThenStopsBeforeTheFirstAutoCiv()
    {
        var (session, flow) = NewFlow();
        var outcome = flow.EndTurn(null, new CancellationToken(true));
        Assert.False(outcome.Accepted);
        Assert.Equal(0, outcome.AiTurnsRun);
        Assert.IsType<TurnPhase.AutoPlayers>(session.Game.TurnPhase);
        Assert.True(session.Game.PendingAutoPlayerCount() > 0);
    }

    [Fact]
    public void PendingAutoPlayerCount_IsZeroBetweenRounds()
    {
        var (session, flow) = NewFlow();
        Assert.Equal(0, session.Game.PendingAutoPlayerCount());
        Assert.True(flow.EndTurn().Accepted);
        Assert.Equal(0, session.Game.PendingAutoPlayerCount());
    }

    [Fact]
    public void PendingAutoPlayerCount_IsAtMostOneFullRound()
    {
        // review C040: when every civ is processed automatically (a simulation) the count stops at one round instead of looping forever
        var (session, flow) = NewFlow();
        flow.EndTurn(null, new CancellationToken(true));
        session.Game.EngineOptions.SimulateUntilTurn = session.Game.Turns + 5;
        Assert.Equal(session.Game.Civilizations.Count, session.Game.PendingAutoPlayerCount());
    }
}
