using Unciv.Commands;
using Unciv.Commands.City;
using Unciv.Commands.Empire;
using Unciv.Harness;
using Unciv.Logic;
using Unciv.Net.Visibility;

namespace Unciv.Core.Tests.Net.Visibility;

// C029: hotseat. After one human ends the turn and the next human's turn is up, the flow stops and names that human; it begins their turn only when asked,
// as the new viewer. The pass-the-device screen and LocalHost.TakeOverAsync are built on exactly these three answers.
[Collection("RulesetCache")]
public sealed class ViewerSessionHandOverTests
{
    private static (GameSession Session, ViewerSessionFlow A, ViewerSessionFlow B, List<GameEvent> PublishedToB) TwoHumans()
    {
        var session = ScenarioGame.Create();
        long clock = ScenarioGame.FixedClockMillis;
        var publishedToB = new List<GameEvent>();
        var a = new ViewerSessionFlow(session, ScenarioGame.HumanA, () => clock++, (_, _) => { });
        var b = new ViewerSessionFlow(session, ScenarioGame.HumanB, () => clock++, (_, events) => publishedToB.AddRange(events));
        return (session, a, b, publishedToB);
    }

    // The engine refuses End turn while a city has nothing to build or nothing is being researched; answer both as the player would.
    private static void AnswerDecisions(GameSession session, string civId)
    {
        var city = session.Game.GetCivilization(civId).Cities[0];
        var queued = session.Submit(civId, new QueueConstruction { CityId = city.Id, Name = "Warrior" });
        Assert.True(queued.Accepted, queued.Validation.Message);
        var researching = session.Submit(civId, new SetResearchQueue { Techs = ["Pottery"] });
        Assert.True(researching.Accepted, researching.Validation.Message);
    }

    [Fact]
    public void EndTurn_ByTheFirstHuman_StopsAndNamesTheSecond()
    {
        var (session, a, _, _) = TwoHumans();
        AnswerDecisions(session, ScenarioGame.HumanA);
        var outcome = a.EndTurn();

        Assert.True(outcome.Accepted, outcome.Message);
        Assert.Equal(ScenarioGame.HumanB, outcome.HandOverTo);
        Assert.False(outcome.NextTurnBegun);
        Assert.Equal(0, outcome.AiTurnsRun); // the second human follows directly (Rome plays after Greece)
        var phase = Assert.IsType<TurnPhase.AwaitingInput>(session.Game.TurnPhase);
        Assert.Equal(ScenarioGame.HumanB, phase.Civ.CivId);
        Assert.False(session.Game.IsTurnStarted(phase.Civ)); // nothing of the second human's turn ran while the first was still at the device
        Assert.False(a.ViewerTurnPending);
        Assert.Equal(ScenarioGame.HumanB, a.HandOverTarget);
    }

    [Fact]
    public void BeginPendingTurn_BeginsTheViewersTurn_OnlyForTheHumanWhoseTurnItIs()
    {
        var (session, a, b, publishedToB) = TwoHumans();
        AnswerDecisions(session, ScenarioGame.HumanA);
        a.EndTurn();

        Assert.True(b.ViewerTurnPending);
        Assert.Null(b.HandOverTarget); // it is B's own turn: nothing to hand over to
        var refusedForA = a.BeginPendingTurn();
        Assert.False(refusedForA.Accepted);
        Assert.Equal(ViewerSessionFlow.NoTurnToBeginMessage, refusedForA.Message);
        Assert.False(session.Game.IsTurnStarted(session.Game.GetCivilization(ScenarioGame.HumanB)));

        var began = b.BeginPendingTurn();
        Assert.True(began.Accepted, began.Message);
        Assert.True(session.Game.IsTurnStarted(session.Game.GetCivilization(ScenarioGame.HumanB)));
        Assert.False(b.ViewerTurnPending);
        Assert.NotEmpty(publishedToB); // B's own turn start reaches B

        var again = b.BeginPendingTurn();
        Assert.False(again.Accepted);
        Assert.Equal(ViewerSessionFlow.NoTurnToBeginMessage, again.Message);
    }

    [Fact]
    public void EndTurn_BySecondHuman_HandsBackToTheFirst_AndNeverLeaksTheFirstsEvents()
    {
        var (session, a, b, publishedToB) = TwoHumans();
        AnswerDecisions(session, ScenarioGame.HumanA);
        a.EndTurn();
        b.BeginPendingTurn();
        AnswerDecisions(session, ScenarioGame.HumanB);
        publishedToB.Clear();

        var outcome = b.EndTurn();
        Assert.True(outcome.Accepted, outcome.Message);
        Assert.Equal(ScenarioGame.HumanA, outcome.HandOverTo);
        Assert.True(outcome.AiTurnsRun >= 1); // Rome (and the barbarians) played before the device goes back
        Assert.False(outcome.NextTurnBegun);
        Assert.False(session.Game.IsTurnStarted(session.Game.GetCivilization(ScenarioGame.HumanA)));

        // A's turn begins only when A takes over; its events go to A's flow, not to B's published stream.
        var before = publishedToB.Count;
        var began = a.BeginPendingTurn();
        Assert.True(began.Accepted, began.Message);
        Assert.Equal(before, publishedToB.Count);
        Assert.DoesNotContain(began.Events, e => e is Unciv.Commands.Events.AlertAdded { CivId: ScenarioGame.HumanB });
    }

    [Fact]
    public void EndTurn_InASingleHumanGame_BeginsTheNextTurn_AndNamesNobody()
    {
        var session = ScenarioGame.Create(solo: true);
        long clock = ScenarioGame.FixedClockMillis;
        var flow = new ViewerSessionFlow(session, ScenarioGame.HumanA, () => clock++, (_, _) => { });
        var outcome = flow.EndTurn();

        Assert.True(outcome.Accepted, outcome.Message);
        Assert.Null(outcome.HandOverTo);
        Assert.True(outcome.NextTurnBegun);
        Assert.Null(flow.HandOverTarget);
        Assert.False(flow.ViewerTurnPending);
    }

    [Fact]
    public void HandOverTarget_IsNull_WhileTheCurrentHumansTurnIsRunning()
    {
        var (session, a, b, _) = TwoHumans();
        Assert.True(session.Game.IsTurnStarted(session.Game.GetCivilization(ScenarioGame.HumanA)));
        Assert.Null(b.HandOverTarget); // A is playing and B is not waiting for the device yet
        Assert.Null(a.HandOverTarget); // A is the viewer itself
        Assert.False(b.ViewerTurnPending);
        Assert.False(a.ViewerTurnPending);
    }
}
