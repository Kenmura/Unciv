using Unciv.Assets;
using Unciv.Commands.Diplomacy;
using Unciv.Commands.Turn;
using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Net.Visibility;
using Unciv.Queries.Empire;

namespace Unciv.Core.Tests.Assets;

public class AlertPopupModelTests
{
    private static CivDto Civ(string id, string name) =>
        new() { CivId = id, Name = name, IsCityState = false, IsMajor = true, IsBarbarian = false, IsDefeated = false };

    private static CityDto City(string id, string name) => new()
    {
        Name = name,
        Owner = "Me",
        X = 1,
        Y = 1,
        Population = 1,
        IsCapital = true,
        IsPuppet = false,
        IsBeingRazed = false,
        Health = 1,
        MaxHealth = 1,
        VisibleBuildings = [],
        Own = new OwnCityDto { Id = id, FoodStored = 0, CultureStored = 0, Buildings = [], ConstructionQueue = [] },
    };

    private static ClientSnapshot Snapshot() => new()
    {
        Seq = 1,
        Turn = 1,
        Phase = "Playing",
        TurnStatus = new TurnStatusDto { Ended = [], Phase = "Playing" },
        You = "Me",
        Civs = [Civ("Me", "Me"), Civ("rome-id", "Rome"), Civ("ship-id", "Egypt")],
        Tiles = [],
        Cities = [City("c1", "Ur")],
        Units = [],
        Diplomacy = [],
        PendingTrades = [],
        Notifications = [],
    };

    private static PendingDecisionEntry Entry(AlertType type, string value, int index = 0, bool blocking = false, bool ackOnly = false, string? eventText = null, params (string Choice, string Label)[] choices) =>
        new(DecisionKind.Alert.ToString(), type.ToString(), PendingDecisions.ResolverOf(type), blocking, index,
            new AlertDetail(type.ToString(), value, ackOnly, eventText, choices.Select(it => new AlertChoice(it.Choice, it.Label)).ToList()));

    private static PendingDecisionsResult Pending(params PendingDecisionEntry[] entries) => new(entries);

    [Fact]
    public void EveryAlertTypeHasWordingAndAnAnsweringCommandKind()
    {
        Assert.Equal(Enum.GetValues<AlertType>().Length, AlertPopupModel.Table.Count);
        foreach (var type in Enum.GetValues<AlertType>())
        {
            Assert.True(AlertPopupModel.Table.TryGetValue(type, out var words), type.ToString());
            Assert.False(string.IsNullOrWhiteSpace(words.Title));
            string value = type is AlertType.BulliedProtectedMinor or AlertType.AttackedProtectedMinor or AlertType.AttackedAllyMinor ? "rome-id@ship-id" : "rome-id";
            var choices = type == AlertType.CityConquered ? new[] { ("annex", "annex") } : [];
            var popup = AlertPopupModel.Describe(Entry(type, value, choices: choices), Snapshot());
            Assert.NotNull(popup);
            if (type != AlertType.Event) Assert.False(string.IsNullOrWhiteSpace(popup.Body), type.ToString()); // an event's text comes from the engine
            Assert.NotEmpty(popup.Buttons);
            Assert.DoesNotContain('{', popup.Title + popup.Body);
            // the command the first button sends is the kind PendingDecisions says resolves this type
            string resolver = PendingDecisions.ResolverOf(type);
            var command = popup.Buttons[0].Command;
            Assert.True(resolver switch
            {
                "respond-demand" => command is RespondToDemand,
                "respond-friendship" => command is RespondToFriendship,
                "respond-to-alert" => command is RespondToAlert or AckAlert, // AckAlert only when the engine offered no choice
                _ => command is AckAlert,
            }, type.ToString());
        }
    }

    [Fact]
    public void AckAlertsGetOneOkButtonThatCannotBeDeferred()
    {
        var popup = AlertPopupModel.Describe(Entry(AlertType.WonderBuilt, "Pyramids", 3), Snapshot())!;
        Assert.Equal("Pyramids has been built in a faraway land.", popup.Body);
        var button = Assert.Single(popup.Buttons);
        var ack = Assert.IsType<AckAlert>(button.Command);
        Assert.Equal((3, AlertType.WonderBuilt, "Pyramids"), (ack.Index, ack.AlertType, ack.Value));
        Assert.False(popup.CanDefer);
    }

    [Fact]
    public void ADemandNamesTheCivAndOffersAcceptAndRefuseToTheDemandCommand()
    {
        var popup = AlertPopupModel.Describe(Entry(AlertType.DemandToStopSpreadingReligion, "rome-id", 2), Snapshot())!;
        Assert.Equal("A demand from Rome", popup.Title);
        Assert.Equal("Please don't spread your religion to us.", popup.Body);
        Assert.Equal(["Accept", "Refuse"], popup.Buttons.Select(it => it.Id));
        var accept = Assert.IsType<RespondToDemand>(popup.Buttons[0].Command);
        Assert.Equal(("rome-id", "DoNotSpreadReligion", true), (accept.CivId, accept.Demand, accept.Accept));
        Assert.False(((RespondToDemand)popup.Buttons[1].Command).Accept);
        Assert.True(popup.CanDefer);
    }

    [Fact]
    public void ABrokenPromiseShowsTheEnginesAccusation()
    {
        var popup = AlertPopupModel.Describe(Entry(AlertType.AttackedUsDespitePromise, "rome-id"), Snapshot())!;
        Assert.Equal("Broken promise by Rome", popup.Title);
        Assert.Equal("You have exposed yourself as a treacherous warmonger.", popup.Body);
        Assert.IsType<AckAlert>(popup.Buttons[0].Command);
    }

    [Fact]
    public void FriendshipOffersAreAcceptedOrDeclined()
    {
        var popup = AlertPopupModel.Describe(Entry(AlertType.DeclarationOfFriendship, "rome-id"), Snapshot())!;
        Assert.Equal("Rome offers you a Declaration of Friendship.", popup.Body);
        Assert.Equal(["Accept", "Decline"], popup.Buttons.Select(it => it.Id));
        Assert.True(((RespondToFriendship)popup.Buttons[0].Command).Accept);
        Assert.False(((RespondToFriendship)popup.Buttons[1].Command).Accept);
    }

    [Fact]
    public void ChoicesAreExactlyTheOnesTheEngineOfferedWithReadableLabels()
    {
        var popup = AlertPopupModel.Describe(Entry(AlertType.CityConquered, "c1", 1, blocking: true, choices: [("annex", "annex"), ("raze", "raze")]), Snapshot())!;
        Assert.Equal("Your units have taken Ur. What shall become of it?", popup.Body);
        Assert.Equal(["Annex", "Raze"], popup.Buttons.Select(it => it.Label));
        var command = Assert.IsType<RespondToAlert>(popup.Buttons[1].Command);
        Assert.Equal((1, AlertType.CityConquered, "c1", "raze"), (command.Index, command.AlertType, command.Value, command.Choice));
        Assert.True(popup.Blocking);
        Assert.False(popup.CanDefer);
    }

    [Fact]
    public void AnAlertWithNoAcceptableChoiceCanOnlyBeClosed()
    {
        var popup = AlertPopupModel.Describe(Entry(AlertType.Denounced, "rome-id"), Snapshot())!;
        Assert.IsType<AckAlert>(Assert.Single(popup.Buttons).Command);
        var gone = AlertPopupModel.Describe(Entry(AlertType.RecapturedCivilian, "1,2", ackOnly: true, choices: [("return", "return")]), Snapshot())!;
        Assert.IsType<AckAlert>(Assert.Single(gone.Buttons).Command); // ack-only wins over any choice
    }

    [Fact]
    public void MinorCivAlertsNameBothCivsAndMayBeDeferred()
    {
        var popup = AlertPopupModel.Describe(Entry(AlertType.BulliedProtectedMinor, "rome-id@ship-id", choices: [("war", "war"), ("side", "side"), ("abandon", "abandon")]), Snapshot())!;
        Assert.Equal("Rome demanded tribute from Egypt, a city-state you protect. How do you respond?", popup.Body);
        Assert.Equal(["Declare war", "Side with the city-state", "Abandon the city-state"], popup.Buttons.Select(it => it.Label));
        Assert.True(popup.CanDefer);
    }

    [Fact]
    public void EventsShowTheirTextAndTheirOwnChoiceLabels()
    {
        var popup = AlertPopupModel.Describe(Entry(AlertType.Event, "ev", eventText: "A [storm] hits!", choices: [("0", "Pray [hard]"), ("1", "Ignore")]), Snapshot())!;
        Assert.Equal("A storm hits!", popup.Body);
        Assert.Equal(["Pray hard", "Ignore"], popup.Buttons.Select(it => it.Label));
        Assert.Equal("1", ((RespondToAlert)popup.Buttons[1].Command).Choice);
        var empty = AlertPopupModel.Describe(Entry(AlertType.Event, "ev"), Snapshot())!;
        Assert.Equal("", empty.Body);
        Assert.IsType<AckAlert>(empty.Buttons[0].Command);
    }

    [Fact]
    public void UnknownNamesFallBackToTheRawValue()
    {
        var popup = AlertPopupModel.Describe(Entry(AlertType.FirstContact, "nobody"), Snapshot())!;
        Assert.Equal("You have met nobody.", popup.Body);
        Assert.Equal("Your units have taken a city. What shall become of it?", AlertPopupModel.Describe(Entry(AlertType.CityConquered, "zzz", choices: ("annex", "annex")), Snapshot())!.Body);
    }

    [Fact]
    public void NotAnAlertOrUnknownTypeDescribesNothing()
    {
        Assert.Null(AlertPopupModel.Describe(new PendingDecisionEntry("Technology", "", "x", true, -1, null), Snapshot()));
        Assert.Null(AlertPopupModel.Describe(new PendingDecisionEntry("Alert", "x", "x", false, 0, new AlertDetail("Nonsense", "", false, null, [])), Snapshot()));
    }

    [Fact]
    public void OnlyAlertsThatAskForADecisionOpenOnTheirOwnAndPutOffOnesAreSkipped()
    {
        var pending = Pending(Entry(AlertType.StartIntro, "", 0), Entry(AlertType.GoldenAge, "", 1), Entry(AlertType.DeclarationOfFriendship, "rome-id", 2),
            Entry(AlertType.DemandToNotAttackUs, "rome-id", 3), Entry(AlertType.WonderBuilt, "Pyramids", 4));
        var snapshot = Snapshot();
        var first = AlertPopupModel.Next(pending, snapshot, new HashSet<string>())!;
        Assert.Equal(AlertType.DeclarationOfFriendship, first.Type);
        var second = AlertPopupModel.Next(pending, snapshot, new HashSet<string> { first.Key })!;
        Assert.Equal(AlertType.DemandToNotAttackUs, second.Type);
        Assert.Null(AlertPopupModel.Next(pending, snapshot, new HashSet<string> { first.Key, second.Key }));
        Assert.Null(AlertPopupModel.Next(null, snapshot, new HashSet<string>()));
        Assert.Null(AlertPopupModel.Next(Pending(), snapshot, new HashSet<string>()));
        Assert.Null(AlertPopupModel.Next(Pending(Entry(AlertType.GoldenAge, "")), snapshot, new HashSet<string>()));
        Assert.Null(AlertPopupModel.Next(Pending(new PendingDecisionEntry("Technology", "", "x", true, -1, null)), snapshot, new HashSet<string>(), reviewing: true));
    }

    [Fact]
    public void ReviewingOpensEverythingInTheEnginesOrder()
    {
        var pending = Pending(Entry(AlertType.StartIntro, "", 0), Entry(AlertType.DeclarationOfFriendship, "rome-id", 1), Entry(AlertType.WonderBuilt, "Pyramids", 2));
        var snapshot = Snapshot();
        Assert.Equal(AlertType.StartIntro, AlertPopupModel.Next(pending, snapshot, new HashSet<string>(), reviewing: true)!.Type);
        var intro = AlertPopupModel.Next(pending, snapshot, new HashSet<string>(), reviewing: true)!;
        Assert.Equal(AlertType.DeclarationOfFriendship, AlertPopupModel.Next(pending, snapshot, new HashSet<string> { intro.Key }, reviewing: true)!.Type);
    }

    [Fact]
    public void WaitingCountsInformationalAndPutOffAlerts()
    {
        var pending = Pending(Entry(AlertType.StartIntro, "", 0), Entry(AlertType.DeclarationOfFriendship, "rome-id", 1), Entry(AlertType.CityConquered, "c1", 2, blocking: true, choices: ("annex", "annex")));
        var snapshot = Snapshot();
        Assert.Equal(0, AlertPopupModel.WaitingCount(pending, snapshot, new HashSet<string>())); // the welcome alert is not counted
        Assert.Equal(1, AlertPopupModel.WaitingCount(Pending(Entry(AlertType.GoldenAge, "")), snapshot, new HashSet<string>()));
        var friendship = AlertPopupModel.Describe(pending.Decisions[1], snapshot)!;
        Assert.Equal(1, AlertPopupModel.WaitingCount(pending, snapshot, new HashSet<string> { friendship.Key }));
        Assert.Equal(0, AlertPopupModel.WaitingCount(null, snapshot, new HashSet<string>()));
        Assert.Equal("Review alerts (3)", AlertPopupModel.ReviewLabel(3));
    }

    [Fact]
    public void KeyDistinguishesAlertsThatShareATypeOrPosition()
    {
        var a = AlertPopupModel.Describe(Entry(AlertType.WonderBuilt, "Pyramids", 1), Snapshot())!;
        var b = AlertPopupModel.Describe(Entry(AlertType.WonderBuilt, "Colossus", 1), Snapshot())!;
        var c = AlertPopupModel.Describe(Entry(AlertType.WonderBuilt, "Pyramids", 2), Snapshot())!;
        Assert.Equal(3, new[] { a.Key, b.Key, c.Key }.Distinct().Count());
    }

    [Fact]
    public void InformationalAlertsAreAmbientAndDecisionsAreBlocking()
    {
        var snapshot = Snapshot();
        foreach (var type in new[] { AlertType.WonderBuilt, AlertType.TechResearched, AlertType.FirstContact, AlertType.GoldenAge, AlertType.StartIntro })
            Assert.Equal(AlertKind.Ambient, AlertPopupModel.Describe(Entry(type, "rome-id"), snapshot)!.Kind);
        Assert.Equal(AlertKind.Blocking, AlertPopupModel.Describe(Entry(AlertType.DeclarationOfFriendship, "rome-id"), snapshot)!.Kind);
        Assert.Equal(AlertKind.Blocking, AlertPopupModel.Describe(Entry(AlertType.DemandToNotAttackUs, "rome-id"), snapshot)!.Kind);
        Assert.Equal(AlertKind.Blocking, AlertPopupModel.Describe(Entry(AlertType.CityConquered, "c1", choices: ("annex", "annex")), snapshot)!.Kind);
    }

    [Fact]
    public void AnAcknowledgeOnlyAlertThatBlocksTheTurnIsBlocking()
    {
        var snapshot = Snapshot();
        var popup = AlertPopupModel.Describe(Entry(AlertType.WonderBuilt, "Pyramids", blocking: true), snapshot)!;
        Assert.True(AlertPopupModel.IsInformational(popup));
        Assert.Equal(AlertKind.Blocking, popup.Kind);
        // so it opens as a popup straight away instead of waiting under "Review alerts"
        Assert.Equal(AlertType.WonderBuilt, AlertPopupModel.Next(Pending(Entry(AlertType.WonderBuilt, "Pyramids", blocking: true)), snapshot, new HashSet<string>())!.Type);
        Assert.Null(AlertPopupModel.Next(Pending(Entry(AlertType.WonderBuilt, "Pyramids")), snapshot, new HashSet<string>()));
    }
}
