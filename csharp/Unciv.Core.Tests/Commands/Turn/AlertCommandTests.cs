using Unciv.Commands;
using Unciv.Commands.Turn;
using Unciv.Core.Tests.Commands.Diplomacy;
using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Unique;
using CityObject = Unciv.Logic.City.City;

namespace Unciv.Core.Tests.Commands.Turn;

// T187: AckAlert and RespondToAlert (Kotlin AlertPopup) on the T193 diplomacy world: A and B humans, C an AI, S a city-state.
[Collection("RulesetCache")]
public sealed class AlertCommandTests
{
    private readonly DiplomacyWorld w = new();

    private int nextX = -4;

    private CityObject ConqueredCity(string? foundedBy = null)
    {
        var city = w.Game.AddCity(w.A, w.Game.GetTile(nextX, 4));
        nextX += 2;
        if (foundedBy != null) city.FoundingCivObject = w.Game.GameInfo.GetCivilization(foundedBy);
        w.A.PopupAlerts.Add(new PopupAlert(AlertType.CityConquered, city.Id));
        return city;
    }

    // The position of the alert unless given: other alerts (first contact) may sit before it.
    private GameCommand Respond(AlertType type, string value, string choice, int? index = null) =>
        new RespondToAlert
        {
            Index = index ?? System.Math.Max(0, w.A.PopupAlerts.FindIndex(it => it.Type == type && it.Value == value)),
            AlertType = type,
            Value = value,
            Choice = choice,
        };

    [Fact]
    public void AckRemovesAnInformationalAlertAndRejectsStaleReferences()
    {
        w.A.PopupAlerts.Add(new PopupAlert(AlertType.WonderBuilt, "Pyramids"));
        w.A.PopupAlerts.Add(new PopupAlert(AlertType.WarDeclaration, w.C.CivId));
        w.Rejected(new AckAlert { Index = 5, AlertType = AlertType.WonderBuilt, Value = "Pyramids" }, CommandErrors.StaleReference);
        w.Rejected(new AckAlert { Index = -1, AlertType = AlertType.WonderBuilt, Value = "Pyramids" }, CommandErrors.StaleReference);
        w.Rejected(new AckAlert { Index = 0, AlertType = AlertType.TechResearched, Value = "Pyramids" }, CommandErrors.StaleReference);
        w.Rejected(new AckAlert { Index = 0, AlertType = AlertType.WonderBuilt, Value = "Other" }, CommandErrors.StaleReference);
        w.Rejected(new AckAlert { Index = 0, AlertType = AlertType.WonderBuilt, Value = "Pyramids" }, CommandErrors.NotHuman, w.C.CivId);

        w.Ok(new AckAlert { Index = 1, AlertType = AlertType.WarDeclaration, Value = w.C.CivId }); // the buttons only close it
        Assert.Equal([AlertType.WonderBuilt], w.A.PopupAlerts.Select(it => it.Type));
        w.Ok(new AckAlert { Index = 0, AlertType = AlertType.WonderBuilt, Value = "Pyramids" });
        Assert.Empty(w.A.PopupAlerts);
    }

    [Fact]
    public void AckWorksAfterVictoryAndOutOfTurn()
    {
        w.A.PopupAlerts.Add(new PopupAlert(AlertType.GameHasBeenWon, ""));
        w.B.PopupAlerts.Add(new PopupAlert(AlertType.Defeated, w.C.CivId));
        w.Game.GameInfo.VictoryData = new VictoryData { WinningCiv = w.A.CivId };
        w.Ok(new AckAlert { Index = 0, AlertType = AlertType.GameHasBeenWon, Value = "" });
        w.Ok(new AckAlert { Index = 0, AlertType = AlertType.Defeated, Value = w.C.CivId }, w.B.CivId);
    }

    [Fact]
    public void AckRefusesAlertsThatNeedAnAnswerExceptWhenKotlinWouldNotShowThem()
    {
        w.A.PopupAlerts.Add(new PopupAlert(AlertType.DiplomaticMarriage, "city"));
        var refused = w.Session.Submit(w.A.CivId, new AckAlert { Index = 0, AlertType = AlertType.DiplomaticMarriage, Value = "city" });
        Assert.Equal(CommandErrors.NotAllowed, refused.Validation.Code);
        Assert.Contains("respond-to-alert", refused.Validation.Message);
        w.A.PopupAlerts.Clear();

        // an event nobody can render any more, and a bully that is gone
        w.A.PopupAlerts.Add(new PopupAlert(AlertType.Event, "No Such Event"));
        w.Ok(new AckAlert { Index = 0, AlertType = AlertType.Event, Value = "No Such Event" });
        w.C.Cities = [];
        w.C.HasEverOwnedOriginalCapital = true;
        Assert.True(w.C.IsDefeated());
        var value = w.C.CivId + "@" + w.S.CivId;
        w.A.PopupAlerts.Add(new PopupAlert(AlertType.AttackedProtectedMinor, value));
        w.Ok(new AckAlert { Index = 0, AlertType = AlertType.AttackedProtectedMinor, Value = value });
        w.A.PopupAlerts.Add(new PopupAlert(AlertType.BulliedProtectedMinor, "nobody@" + w.S.CivId));
        w.Ok(new AckAlert { Index = 0, AlertType = AlertType.BulliedProtectedMinor, Value = "nobody@" + w.S.CivId });
    }

    [Fact]
    public void CityConqueredChoices()
    {
        var city = ConqueredCity();
        w.Rejected(Respond(AlertType.CityConquered, city.Id, "liberate"), CommandErrors.InvalidTarget); // no other founder
        w.Rejected(Respond(AlertType.CityConquered, city.Id, "destroy"), CommandErrors.InvalidTarget); // not a one city challenge
        w.Rejected(Respond(AlertType.CityConquered, city.Id, "nonsense"), CommandErrors.InvalidTarget);
        w.Rejected(Respond(AlertType.CityConquered, city.Id, "annex"), CommandErrors.NotHuman, w.C.CivId);
        w.Rejected(Respond(AlertType.CityConquered, "nowhere", "annex"), CommandErrors.StaleReference);

        w.Ok(Respond(AlertType.CityConquered, city.Id, "puppet"));
        Assert.True(city.IsPuppet);
        Assert.DoesNotContain(w.A.PopupAlerts, it => it.Type == AlertType.CityConquered);
    }

    [Fact]
    public void ConqueringACityOfTheSameCivDoesNotMakeItMeetItself()
    {
        var city = ConqueredCity();
        var alertsBefore = w.A.PopupAlerts.Count;
        var knownBefore = w.A.Diplomacy.Count;
        var modifiersBefore = w.Diplo(w.B, w.A).DiplomaticModifiers.Count;

        w.Ok(Respond(AlertType.CityConquered, city.Id, "puppet"));

        Assert.True(city.IsPuppet);
        Assert.Equal(w.A, city.Civ);
        Assert.False(w.A.Diplomacy.ContainsKey(w.A.CivId));
        Assert.Equal(knownBefore, w.A.Diplomacy.Count);
        Assert.DoesNotContain(w.A.PopupAlerts, it => it.Type == AlertType.FirstContact);
        Assert.Equal(alertsBefore - 1, w.A.PopupAlerts.Count);
        Assert.Equal(modifiersBefore, w.Diplo(w.B, w.A).DiplomaticModifiers.Count); // no WarMongerer for third parties
    }

    [Fact]
    public void CityConqueredAnnexRazeAndLiberate()
    {
        var annexed = ConqueredCity();
        w.Ok(Respond(AlertType.CityConquered, annexed.Id, "annex"));
        Assert.False(annexed.IsPuppet);

        var razed = ConqueredCity();
        w.Ok(Respond(AlertType.CityConquered, razed.Id, "raze"));
        Assert.True(razed.IsBeingRazed);

        var liberated = ConqueredCity(w.C.CivId);
        w.Ok(Respond(AlertType.CityConquered, liberated.Id, "liberate"));
        Assert.Equal(w.C, liberated.Civ);
    }

    [Fact]
    public void CityConqueredWithoutTheRightToAnnexAndRazingACapital()
    {
        w.A.Tech.AddTechnology("Agriculture"); // anything: the unique below does the work
        var withoutRights = new DiplomacyWorld(aUniques: ["May not annex cities"]);
        var city = withoutRights.Game.AddCity(withoutRights.A, withoutRights.Game.GetTile(-4, 4));
        withoutRights.A.PopupAlerts.Add(new PopupAlert(AlertType.CityConquered, city.Id));
        Assert.True(withoutRights.A.HasUnique(UniqueType.MayNotAnnexCities));
        withoutRights.Rejected(Respond(AlertType.CityConquered, city.Id, "annex"), CommandErrors.InvalidTarget);
        withoutRights.Ok(Respond(AlertType.CityConquered, city.Id, "raze"));
        Assert.True(city.IsBeingRazed);
        Assert.True(city.IsPuppet);

        var capital = w.CityA;
        w.A.PopupAlerts.Add(new PopupAlert(AlertType.CityConquered, capital.Id));
        Assert.False(capital.CanBeDestroyed(justCaptured: true));
        w.Rejected(Respond(AlertType.CityConquered, capital.Id, "raze"), CommandErrors.InvalidTarget);
    }

    [Fact]
    public void CityConqueredOneCityChallengeOnlyAllowsDestroying()
    {
        w.Game.GameInfo.GameParameters.OneCityChallenge = true;
        var city = ConqueredCity();
        foreach (var choice in new[] { "annex", "puppet", "raze" })
            w.Rejected(Respond(AlertType.CityConquered, city.Id, choice), CommandErrors.InvalidTarget);
        w.Ok(Respond(AlertType.CityConquered, city.Id, "destroy"));
        Assert.DoesNotContain(city, w.A.Cities);
    }

    [Fact]
    public void CityTradedCanBeLiberatedOrKept()
    {
        var city = w.Game.AddCity(w.A, w.Game.GetTile(-4, 4));
        city.FoundingCivObject = w.C;
        w.A.PopupAlerts.Add(new PopupAlert(AlertType.CityTraded, city.Id));
        w.MakeWar(w.A, w.C);
        w.Rejected(Respond(AlertType.CityTraded, city.Id, "liberate"), CommandErrors.InvalidTarget);
        w.Diplo(w.A, w.C).DiplomaticStatus = DiplomaticStatus.Peace;
        w.Diplo(w.C, w.A).DiplomaticStatus = DiplomaticStatus.Peace;
        w.Ok(Respond(AlertType.CityTraded, city.Id, "liberate"));
        Assert.Equal(w.C, city.Civ);

        var other = w.Game.AddCity(w.A, w.Game.GetTile(-6, 4));
        w.A.PopupAlerts.Add(new PopupAlert(AlertType.CityTraded, other.Id));
        w.Ok(new AckAlert { Index = 0, AlertType = AlertType.CityTraded, Value = other.Id }); // "Keep it"
        Assert.Equal(w.A, other.Civ);
    }

    [Fact]
    public void DiplomaticMarriageChoices()
    {
        var city = w.Game.AddCity(w.A, w.Game.GetTile(-4, 4));
        w.A.PopupAlerts.Add(new PopupAlert(AlertType.DiplomaticMarriage, city.Id));
        w.Rejected(Respond(AlertType.DiplomaticMarriage, city.Id, "raze"), CommandErrors.InvalidTarget);
        w.Rejected(Respond(AlertType.DiplomaticMarriage, city.Id, "destroy"), CommandErrors.InvalidTarget);
        w.Ok(Respond(AlertType.DiplomaticMarriage, city.Id, "puppet"));
        Assert.True(city.IsPuppet);

        var second = w.Game.AddCity(w.A, w.Game.GetTile(-6, 4));
        w.A.PopupAlerts.Add(new PopupAlert(AlertType.DiplomaticMarriage, second.Id));
        second.IsPuppet = true;
        w.Ok(Respond(AlertType.DiplomaticMarriage, second.Id, "annex"));
        Assert.False(second.IsPuppet);

        w.Game.GameInfo.GameParameters.OneCityChallenge = true;
        var third = w.Game.AddCity(w.A, w.Game.GetTile(-2, 4));
        w.A.PopupAlerts.Add(new PopupAlert(AlertType.DiplomaticMarriage, third.Id));
        w.Rejected(Respond(AlertType.DiplomaticMarriage, third.Id, "annex"), CommandErrors.InvalidTarget);
        w.Ok(Respond(AlertType.DiplomaticMarriage, third.Id, "destroy"));
        Assert.DoesNotContain(third, w.A.Cities);
    }

    private (Unciv.Logic.Map.Tiles.Tile Tile, string Value) CapturedWorker()
    {
        var tile = w.Game.GetTile(-5, 2);
        var unit = w.Game.AddUnit("Worker", w.A, tile);
        unit.OriginalOwner = w.C.CivId;
        w.A.PopupAlerts.Add(new PopupAlert(AlertType.RecapturedCivilian, tile.Position.ToPrettyString()));
        return (tile, tile.Position.ToPrettyString());
    }

    [Fact]
    public void RecapturedCivilianCanBeReturnedOrKept()
    {
        var (tile, value) = CapturedWorker();
        w.Rejected(Respond(AlertType.RecapturedCivilian, value, "sell"), CommandErrors.InvalidTarget);
        var before = w.C.Units.GetCivUnitsSize();
        w.Ok(Respond(AlertType.RecapturedCivilian, value, "return"));
        Assert.Null(tile.CivilianUnit);
        Assert.Equal(before + 1, w.C.Units.GetCivUnitsSize());
        Assert.True(w.Diplo(w.C, w.A).DiplomaticModifiers.ContainsKey(DiplomaticModifiers.ReturnedCapturedUnits.ToString()));
        Assert.Empty(w.A.PopupAlerts);

        var (keptTile, keptValue) = CapturedWorker();
        w.Ok(Respond(AlertType.RecapturedCivilian, keptValue, "keep"));
        Assert.Equal(w.A, keptTile.CivilianUnit?.Civ);
    }

    [Fact]
    public void RecapturedCivilianFromACityStateGivesInfluenceAndAGoneUnitNeedsOnlyAnAck()
    {
        var tile = w.Game.GetTile(-5, 2);
        var unit = w.Game.AddUnit("Worker", w.A, tile);
        unit.OriginalOwner = w.S.CivId;
        var value = tile.Position.ToPrettyString();
        w.A.PopupAlerts.Add(new PopupAlert(AlertType.RecapturedCivilian, value));
        var influence = w.Diplo(w.S, w.A).GetInfluence();
        w.Ok(Respond(AlertType.RecapturedCivilian, value, "return"));
        Assert.Equal(influence + 45f, w.Diplo(w.S, w.A).GetInfluence());

        w.A.PopupAlerts.Add(new PopupAlert(AlertType.RecapturedCivilian, value)); // the unit is gone
        w.Rejected(Respond(AlertType.RecapturedCivilian, value, "return"), CommandErrors.NotAllowed);
        w.Ok(new AckAlert { Index = 0, AlertType = AlertType.RecapturedCivilian, Value = value });
    }

    [Fact]
    public void ProtectedCityStateAttackChoices()
    {
        var value = w.C.CivId + "@" + w.S.CivId;
        w.A.PopupAlerts.Add(new PopupAlert(AlertType.AttackedProtectedMinor, value));
        w.Rejected(Respond(AlertType.AttackedProtectedMinor, value, "dance"), CommandErrors.InvalidTarget);
        w.Rejected(Respond(AlertType.AttackedProtectedMinor, "x", "war"), CommandErrors.StaleReference);
        w.A.PopupAlerts.Add(new PopupAlert(AlertType.AttackedProtectedMinor, "x@y@z"));
        w.Rejected(Respond(AlertType.AttackedProtectedMinor, "x@y@z", "war", 1), CommandErrors.StaleReference);
        w.A.PopupAlerts.RemoveAt(1);
        w.A.PopupAlerts.Add(new PopupAlert(AlertType.AttackedProtectedMinor, "nobody@" + w.S.CivId));
        w.Rejected(Respond(AlertType.AttackedProtectedMinor, "nobody@" + w.S.CivId, "war", 1), CommandErrors.UnknownCiv);
        w.A.PopupAlerts.RemoveAt(1);
        w.A.PopupAlerts.Add(new PopupAlert(AlertType.AttackedProtectedMinor, w.C.CivId + "@nobody"));
        w.Rejected(Respond(AlertType.AttackedProtectedMinor, w.C.CivId + "@nobody", "war", 1), CommandErrors.UnknownCiv);
        w.A.PopupAlerts.RemoveAt(1);

        var influence = w.Diplo(w.S, w.A).GetInfluence();
        w.Ok(Respond(AlertType.AttackedProtectedMinor, value, "war"));
        Assert.True(w.A.IsAtWarWith(w.C));
        Assert.True(w.Diplo(w.S, w.A).GetInfluence() >= influence + 20f); // "You went to war for us!!"

        w.A.PopupAlerts.Add(new PopupAlert(AlertType.AttackedAllyMinor, value));
        w.Rejected(Respond(AlertType.AttackedAllyMinor, value, "war"), CommandErrors.NotAllowed); // already at war
        w.Ok(Respond(AlertType.AttackedAllyMinor, value, "side"));
    }

    [Fact]
    public void ProtectedCityStateAbandoningThePledge()
    {
        var value = w.C.CivId + "@" + w.S.CivId;
        ((CityStateFunctions)w.S.CityStateFunctions).AddProtectorCiv(w.A);
        Assert.Contains(w.A.CivId, w.S.CityStateFunctions.GetProtectorCivs().Select(it => it.CivId));
        w.A.PopupAlerts.Add(new PopupAlert(AlertType.BulliedProtectedMinor, value));
        w.Ok(Respond(AlertType.BulliedProtectedMinor, value, "abandon"));
        Assert.DoesNotContain(w.A.CivId, w.S.CityStateFunctions.GetProtectorCivs().Select(it => it.CivId));
        Assert.Contains(w.A.Notifications, it => it.Text.Contains("broken your Pledge"));
    }

    [Fact]
    public void DenouncedCanBeAnsweredWithWar()
    {
        w.A.PopupAlerts.Add(new PopupAlert(AlertType.Denounced, w.C.CivId));
        w.Rejected(Respond(AlertType.Denounced, w.C.CivId, "nope"), CommandErrors.InvalidTarget);
        w.A.PopupAlerts.Add(new PopupAlert(AlertType.Denounced, "nobody"));
        w.Rejected(Respond(AlertType.Denounced, "nobody", "war", 1), CommandErrors.UnknownCiv);
        w.A.PopupAlerts.RemoveAt(1);
        w.Ok(Respond(AlertType.Denounced, w.C.CivId, "war"));
        Assert.True(w.A.IsAtWarWith(w.C));

        w.A.PopupAlerts.Add(new PopupAlert(AlertType.Denounced, w.C.CivId));
        w.Rejected(Respond(AlertType.Denounced, w.C.CivId, "war"), CommandErrors.NotAllowed); // already at war
        w.Ok(new AckAlert { Index = 0, AlertType = AlertType.Denounced, Value = w.C.CivId }); // "Very well."
    }

    // AlertPopup.addDenouncement / addAcceptingDemand / addRejectingDemand / addDemandViolationNoticed: the only buttons ("Very well.",
    // "You'll pay for this!") just close the popup, so these are dismissed with ack-alert; only denouncement's "THIS MEANS WAR!" has an effect.
    [Theory]
    [InlineData(AlertType.Denounced)]
    [InlineData(AlertType.AcceptingDemand)]
    [InlineData(AlertType.RejectingDemand)]
    [InlineData(AlertType.CitySettledNearOtherCivDespiteOurPromise)]
    [InlineData(AlertType.ReligionSpreadDespiteOurPromise)]
    [InlineData(AlertType.AttackedUsDespitePromise)]
    public void DiplomacyNoticesAreDismissedWithAck(AlertType type)
    {
        Assert.Equal("ack-alert", PendingDecisions.ResolverOf(type));
        w.A.PopupAlerts.Add(new PopupAlert(type, w.C.CivId));
        w.Rejected(Respond(type, w.C.CivId, "ok"), type == AlertType.Denounced ? CommandErrors.InvalidTarget : CommandErrors.NotAllowed);
        w.Rejected(new AckAlert { Index = 0, AlertType = type, Value = "someone else" }, CommandErrors.StaleReference);
        w.Ok(new AckAlert { Index = 0, AlertType = type, Value = w.C.CivId });
        Assert.Empty(w.A.PopupAlerts);
    }

    [Fact]
    public void DenouncementFromADefeatedCivCanOnlyBeAcknowledged()
    {
        w.A.PopupAlerts.Add(new PopupAlert(AlertType.Denounced, w.C.CivId));
        w.C.Cities = [];
        w.C.HasEverOwnedOriginalCapital = true;
        Assert.True(w.C.IsDefeated());
        w.Rejected(Respond(AlertType.Denounced, w.C.CivId, "war"), CommandErrors.NotAllowed);
        w.Ok(new AckAlert { Index = 0, AlertType = AlertType.Denounced, Value = w.C.CivId });
    }

    [Fact]
    public void EventChoiceIsPickedByIndex()
    {
        var eventObj = new Event { Name = "TestEvent", Presentation = Event.PresentationType.Alert };
        eventObj.Choices.Add(new EventChoice { Name = "Choice", Text = "go", Uniques = { "Free Technology" } });
        eventObj.Choices.Add(new EventChoice { Name = "Second", Text = "nope" });
        w.Game.Ruleset.Events[eventObj.Name] = eventObj;
        w.A.PopupAlerts.Add(new PopupAlert(AlertType.Event, eventObj.Name));

        w.Rejected(Respond(AlertType.Event, eventObj.Name, "2"), CommandErrors.InvalidTarget);
        w.Rejected(Respond(AlertType.Event, eventObj.Name, "-1"), CommandErrors.InvalidTarget);
        w.Rejected(Respond(AlertType.Event, eventObj.Name, "first"), CommandErrors.InvalidTarget);
        w.Rejected(new AckAlert { Index = 0, AlertType = AlertType.Event, Value = eventObj.Name }, CommandErrors.NotAllowed);
        w.Ok(Respond(AlertType.Event, eventObj.Name, "0"));
        Assert.Equal(1, w.A.Tech.FreeTechs);
        Assert.Empty(w.A.PopupAlerts);

        w.A.PopupAlerts.Add(new PopupAlert(AlertType.Event, "Gone"));
        w.Rejected(Respond(AlertType.Event, "Gone", "0"), CommandErrors.NotAllowed);
    }

    [Fact]
    public void DemandsAndFriendshipHaveTheirOwnCommands()
    {
        w.A.PopupAlerts.Add(new PopupAlert(AlertType.DemandToNotAttackUs, w.C.CivId));
        w.A.PopupAlerts.Add(new PopupAlert(AlertType.DeclarationOfFriendship, w.C.CivId));
        var demand = w.Session.Submit(w.A.CivId, Respond(AlertType.DemandToNotAttackUs, w.C.CivId, "accept"));
        Assert.Contains("respond-demand", demand.Validation.Message);
        var friendship = w.Session.Submit(w.A.CivId, Respond(AlertType.DeclarationOfFriendship, w.C.CivId, "accept", 1));
        Assert.Contains("respond-friendship", friendship.Validation.Message);
        w.Rejected(new AckAlert { Index = 1, AlertType = AlertType.DeclarationOfFriendship, Value = w.C.CivId }, CommandErrors.NotAllowed);
        w.A.PopupAlerts.Add(new PopupAlert(AlertType.WonderBuilt, "x"));
        w.Rejected(Respond(AlertType.WonderBuilt, "x", "ok", 2), CommandErrors.NotAllowed);
    }

    [Fact]
    public void DismissMoveSpiesSetsTheFlagWhenSpiesAreWaiting()
    {
        w.Rejected(new DismissMoveSpies(), CommandErrors.StaleReference);
        w.Game.GameInfo.GameParameters.EspionageEnabled = true;
        w.A.EspionageManager.AddSpy();
        w.CityS.GetCenterTile().SetExplored(w.A, true);
        Assert.True(w.A.EspionageManager.ShouldShowMoveSpies());
        w.Ok(new DismissMoveSpies());
        Assert.True(w.A.EspionageManager.DismissedShouldMoveSpies);
        w.Rejected(new DismissMoveSpies(), CommandErrors.StaleReference);
    }

    [Fact]
    public void AlertsAreClassifiedByTheirResolver()
    {
        Assert.Equal("respond-demand", PendingDecisions.ResolverOf(AlertType.DemandToStopSpyingOnUs));
        Assert.Equal("respond-friendship", PendingDecisions.ResolverOf(AlertType.DeclarationOfFriendship));
        Assert.Equal("respond-to-alert", PendingDecisions.ResolverOf(AlertType.CityConquered));
        Assert.Equal("ack-alert", PendingDecisions.ResolverOf(AlertType.BorderConflict));
    }
}
