using Unciv.Assets;
using Unciv.Commands.Diplomacy;
using Unciv.Net.Visibility;

namespace Unciv.Core.Tests.Assets;

public class TradeProposalModelTests
{
    private static CivDto Civ(string id, string? name = null) =>
        new() { CivId = id, Name = name ?? id, IsCityState = false, IsMajor = true, IsBarbarian = false, IsDefeated = false };

    private static TradeOfferDto Dto(string name, string type, int amount = 1, int duration = -1) => new() { Name = name, Type = type, Amount = amount, Duration = duration };

    private static ClientSnapshot Snapshot(IEnumerable<PendingTradeDto> trades, int turn = 1, IEnumerable<string>? notifications = null) => new()
    {
        Seq = 1,
        Turn = turn,
        Phase = "Playing",
        TurnStatus = new TurnStatusDto { Ended = [], Phase = "Playing" },
        You = "Me",
        Civs = [Civ("Me"), Civ("rome", "Rome"), Civ("greece", "Greece")],
        Tiles = [],
        Cities = [],
        Units = [],
        Diplomacy = [],
        PendingTrades = trades.ToList(),
        Notifications = (notifications ?? []).Select(it => new NotificationDto { Category = "Trade", Text = it, Icons = [], Actions = [] }).ToList(),
    };

    private static PendingTradeDto Gold(string from, string to, int gives, int asks) =>
        new() { From = from, To = to, Offered = [Dto("Gold", "Gold", gives)], Requested = [Dto("Gold", "Gold", asks)] };

    private static PendingTradeDto Peace(string from, string to) =>
        new() { From = from, To = to, Offered = [Dto("Peace Treaty", "Treaty", duration: 10)], Requested = [Dto("Peace Treaty", "Treaty", duration: 10)] };

    private static readonly IReadOnlySet<string> None = new HashSet<string>();

    [Fact]
    public void ATradeProposalNamesTheProposerWhatTheyGiveAndWhatTheyAsk()
    {
        var popup = TradeProposalModel.Next(Snapshot([Gold("rome", "Me", 100, 30)]), None)!;
        Assert.Equal("Rome proposes a trade", popup.Title);
        Assert.Equal("They give: 100 gold\nThey ask: 30 gold", popup.Body);
        Assert.Equal("rome", popup.FromId);
        Assert.False(popup.Trade.IsPeace);
    }

    [Fact]
    public void ThePeaceTreatyAndThePeaceProposalMakeItAPeaceProposal()
    {
        var treaty = TradeProposalModel.Next(Snapshot([Peace("rome", "Me")]), None)!;
        Assert.Equal("Rome proposes peace", treaty.Title);
        Assert.True(treaty.Trade.IsPeace);
        var other = new PendingTradeDto { From = "rome", To = "Me", Offered = [Dto("greece", "PeaceProposal")], Requested = [] };
        var proposal = TradeProposalModel.Next(Snapshot([other]), None)!;
        Assert.Equal("Rome proposes peace", proposal.Title);
        Assert.Equal("They give: Peace with Greece\nThey ask: nothing", proposal.Body);
        var notPeace = new PendingTradeDto { From = "rome", To = "Me", Offered = [Dto("Open Borders", "Treaty")], Requested = [Dto("Peace Treaty", "Agreement")] };
        Assert.False(TradeProposalModel.Next(Snapshot([notPeace]), None)!.Trade.IsPeace);
    }

    [Fact]
    public void TheAnswersAreAcceptDeclineAndCounterCommandsNamingTheProposer()
    {
        var popup = TradeProposalModel.Next(Snapshot([Gold("rome", "Me", 1, 2)]), None)!;
        Assert.Equal(["Accept", "Decline", "Counter"], popup.Buttons.Select(it => it.Id));
        Assert.Equal(["Sounds good!", "Not this time.", "How about something else..."], popup.Buttons.Select(it => it.Label));
        Assert.True(Assert.IsType<RespondToTradeRequest>(popup.Buttons[0].Command) is { CivId: "rome", Accept: true });
        Assert.True(Assert.IsType<RespondToTradeRequest>(popup.Buttons[1].Command) is { CivId: "rome", Accept: false });
        Assert.Equal("rome", Assert.IsType<DismissTradeRequest>(popup.Buttons[2].Command).CivId);
    }

    [Fact]
    public void OnlyProposalsToTheViewerCountAndTheFirstOneInSnapshotOrderComesFirst()
    {
        Assert.Null(TradeProposalModel.Next(Snapshot([]), None));
        Assert.Null(TradeProposalModel.Next(Snapshot([Gold("Me", "rome", 5, 5)]), None));
        var snapshot = Snapshot([Gold("Me", "rome", 5, 5), Peace("greece", "Me"), Gold("rome", "Me", 1, 1)]);
        Assert.Equal("greece", TradeProposalModel.Next(snapshot, None)!.FromId);
    }

    [Fact]
    public void ADeferredProposalIsSkippedAndCountedAsWaitingUntilItChanges()
    {
        var snapshot = Snapshot([Gold("rome", "Me", 100, 30), Peace("greece", "Me")]);
        var first = TradeProposalModel.Next(snapshot, None)!;
        var deferred = new HashSet<string> { first.Key };
        Assert.Equal("greece", TradeProposalModel.Next(snapshot, deferred)!.FromId);
        Assert.Equal(1, TradeProposalModel.WaitingCount(snapshot, deferred));
        Assert.Equal(0, TradeProposalModel.WaitingCount(snapshot, None));
        var changed = Snapshot([Gold("rome", "Me", 100, 40)]);
        Assert.Equal(0, TradeProposalModel.WaitingCount(changed, deferred));
        Assert.Equal("rome", TradeProposalModel.Next(changed, deferred)!.FromId);
        Assert.Null(TradeProposalModel.Next(snapshot, new HashSet<string>(snapshot.PendingTrades.Select(it => TradeProposalModel.Describe(TradeScreenModel.Incoming(snapshot).First(i => i.FromId == it.From)).Key))));
    }

    [Fact]
    public void TheNameLookupIsUsedForOffersTheSnapshotOnlyHasIdsFor()
    {
        var city = new PendingTradeDto { From = "rome", To = "Me", Offered = [Dto("c-1", "City")], Requested = [] };
        var popup = TradeProposalModel.Next(Snapshot([city]), None, (type, name) => type == "City" ? "Antium" : null)!;
        Assert.Equal("They give: City: Antium\nThey ask: nothing", popup.Body);
    }

    [Fact]
    public void AnOfferAnsweredAcceptedIsReportedAsAcceptedAndADeniedOneAsDeclined()
    {
        var tracker = new OfferTracker();
        Assert.Equal(["Waiting for Rome: you give 20 gold; you ask nothing"], tracker.Update(Snapshot([new PendingTradeDto { From = "Me", To = "rome", Offered = [Dto("Gold", "Gold", 20)], Requested = [] }])));
        var accepted = tracker.Update(Snapshot([], 2, ["[Rome] has accepted your trade request"]));
        Assert.Equal(["Rome accepted your offer."], accepted);

        tracker.Reset();
        tracker.Update(Snapshot([new PendingTradeDto { From = "Me", To = "greece", Offered = [], Requested = [Dto("Gold", "Gold", 5)] }]));
        Assert.Equal(["Greece declined your offer."], tracker.Update(Snapshot([], 1, ["[Greece] has denied your trade request"])));
    }

    [Fact]
    public void AnOfferThatVanishedWithoutAnswerIsNoLongerPendingUnlessWeTookItBack()
    {
        var offer = new PendingTradeDto { From = "Me", To = "rome", Offered = [Dto("Gold", "Gold", 20)], Requested = [] };
        var tracker = new OfferTracker();
        tracker.Update(Snapshot([offer]));
        Assert.Equal(["Your offer to Rome is no longer pending."], tracker.Update(Snapshot([])));

        tracker.Reset();
        tracker.Update(Snapshot([offer]));
        tracker.NoteRetracted("rome");
        Assert.Equal(["You took back your offer to Rome."], tracker.Update(Snapshot([])));
        Assert.Equal(["You took back your offer to Rome."], tracker.Update(Snapshot([])));
    }

    [Fact]
    public void ARetractionNotedAfterTheSnapshotCorrectsTheGuess()
    {
        var offer = new PendingTradeDto { From = "Me", To = "rome", Offered = [Dto("Gold", "Gold", 20)], Requested = [] };
        var tracker = new OfferTracker();
        tracker.Update(Snapshot([offer]));
        tracker.Update(Snapshot([]));
        tracker.NoteRetracted("rome");
        Assert.Equal(["You took back your offer to Rome."], tracker.Update(Snapshot([])));
        // the retraction is used up: a later vanished offer to Rome is a guess again
        tracker.Update(Snapshot([offer]));
        Assert.Equal(["You took back your offer to Rome.", "Your offer to Rome is no longer pending."], tracker.Update(Snapshot([])));
        tracker.NoteRetracted("greece"); // nothing to correct, and it is remembered only for an offer that is still pending
        Assert.Equal(2, tracker.Update(Snapshot([])).Count);
    }

    [Fact]
    public void AProposalThatVanishesWithoutOurAnswerWasWithdrawnButAnAnsweredOneWasNot()
    {
        var tracker = new OfferTracker();
        tracker.Update(Snapshot([Gold("rome", "Me", 1, 1), Gold("greece", "Me", 1, 1)]));
        tracker.NoteAnswered("greece");
        Assert.Equal(["Rome withdrew their offer."], tracker.Update(Snapshot([])));
    }

    [Fact]
    public void ARefusedAnswerIsForgottenSoALaterWithdrawalIsReported()
    {
        var tracker = new OfferTracker();
        tracker.Update(Snapshot([Gold("rome", "Me", 1, 1)]));
        tracker.NoteAnswered("rome");
        tracker.NoteRefused("rome");
        Assert.Equal(["Rome withdrew their offer."], tracker.Update(Snapshot([])));
    }

    [Fact]
    public void AnAnswerIsRememberedOnlyWhileTheProposalIsStillThere()
    {
        var tracker = new OfferTracker();
        tracker.Update(Snapshot([Gold("rome", "Me", 1, 1)]));
        tracker.NoteAnswered("rome");
        Assert.Empty(tracker.Update(Snapshot([])));
        tracker.Update(Snapshot([Gold("rome", "Me", 2, 2)]));
        Assert.Equal(["Rome withdrew their offer."], tracker.Update(Snapshot([])));
    }

    [Fact]
    public void OutcomesFadeAfterTurnsAndOnlyTheLatestThreeAreKept()
    {
        var tracker = new OfferTracker();
        tracker.Update(Snapshot([Gold("rome", "Me", 1, 1)], turn: 1));
        Assert.Equal(["Rome withdrew their offer."], tracker.Update(Snapshot([], turn: 1)));
        Assert.Equal(["Rome withdrew their offer."], tracker.Update(Snapshot([], turn: 2)));
        Assert.Empty(tracker.Update(Snapshot([], turn: 3)));

        foreach (var civ in new[] { "a", "b", "c", "d" })
        {
            tracker.Update(Snapshot([Gold(civ, "Me", 1, 1)], turn: 5));
            tracker.Update(Snapshot([], turn: 5));
        }
        Assert.Equal(3, tracker.Update(Snapshot([], turn: 5)).Count);
        Assert.DoesNotContain("a withdrew their offer.", tracker.Update(Snapshot([], turn: 5)));
    }

    [Fact]
    public void WaitingOffersComeBeforeOutcomesAndResetForgetsEverything()
    {
        var tracker = new OfferTracker();
        var offer = new PendingTradeDto { From = "Me", To = "greece", Offered = [], Requested = [Dto("Gold", "Gold", 5)] };
        tracker.Update(Snapshot([Gold("rome", "Me", 1, 1), offer]));
        var lines = tracker.Update(Snapshot([offer]));
        Assert.Equal(["Waiting for Greece: you give nothing; you ask 5 gold", "Rome withdrew their offer."], lines);
        tracker.Reset();
        Assert.Empty(tracker.Update(Snapshot([])));
    }
}
