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
}
