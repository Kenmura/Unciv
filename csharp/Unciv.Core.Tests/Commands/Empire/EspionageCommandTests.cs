using Unciv.Commands;
using Unciv.Commands.Empire;
using Unciv.Core.Tests.Commands.Diplomacy;
using Unciv.Models;

namespace Unciv.Core.Tests.Commands.Empire;

// T210: stage / call off a coup (EspionageScreen.CoupButton).
[Collection("RulesetCache")]
public sealed class EspionageCommandTests
{
    private readonly DiplomacyWorld w = new();

    private Spy SpyIn(bool cityState, SpyAction action = SpyAction.Surveillance)
    {
        w.Game.GameInfo.GameParameters.EspionageEnabled = true;
        var spy = w.A.EspionageManager.AddSpy();
        spy.MoveTo(cityState ? w.CityS : w.CityA);
        spy.SetAction(action);
        return spy;
    }

    [Fact]
    public void StagingACoupSetsTheCoupActionAndCallingItOffGoesBackToCounterIntelligence()
    {
        var spy = SpyIn(cityState: true);
        w.Ok(new SetSpyCoup { Spy = spy.Name, Stage = true });
        Assert.Equal(SpyAction.Coup, spy.Action);
        Assert.Equal(1, spy.TurnsRemainingForAction);
        w.Rejected(new SetSpyCoup { Spy = spy.Name, Stage = true }, CommandErrors.NotAllowed);

        w.Ok(new SetSpyCoup { Spy = spy.Name, Stage = false });
        Assert.Equal(SpyAction.CounterIntelligence, spy.Action);
        Assert.Equal(10, spy.TurnsRemainingForAction);
        w.Rejected(new SetSpyCoup { Spy = spy.Name, Stage = false }, CommandErrors.NotAllowed);
    }

    [Fact]
    public void ACoupNeedsEspionageAKnownSpyAndASpyThatIsSetUpInACityState()
    {
        var spy = SpyIn(cityState: true, SpyAction.EstablishNetwork); // not set up yet
        w.Rejected(new SetSpyCoup { Spy = spy.Name, Stage = true }, CommandErrors.NotAllowed);
        w.Rejected(new SetSpyCoup { Spy = "Nobody", Stage = true }, CommandErrors.InvalidTarget);
        w.Rejected(new SetSpyCoup { Spy = spy.Name, Stage = true }, CommandErrors.NotYourTurn, w.B.CivId);

        var home = SpyIn(cityState: false);
        w.Rejected(new SetSpyCoup { Spy = home.Name, Stage = true }, CommandErrors.NotAllowed); // own city, not a city-state

        w.Game.GameInfo.GameParameters.EspionageEnabled = false;
        w.Rejected(new SetSpyCoup { Spy = spy.Name, Stage = true }, CommandErrors.NotAllowed);
    }

    [Fact]
    public void AnAllyOfTheCityStateCannotStageACoup()
    {
        var spy = SpyIn(cityState: true);
        w.S.Diplomacy[w.A.CivId].SetInfluence(100f);
        w.S.CityStateFunctions.UpdateAllyCivForCityState();
        Assert.Same(w.A, w.S.AllyCiv);
        w.Rejected(new SetSpyCoup { Spy = spy.Name, Stage = true }, CommandErrors.NotAllowed);
    }
}
