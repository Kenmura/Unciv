using Unciv.Assets;
using Unciv.Commands.Diplomacy;
using Unciv.Net.Visibility;
using Unciv.Queries.Empire;

namespace Unciv.Core.Tests.Assets;

public class TradeScreenModelTests
{
    private static TradeOfferEntry Offer(string name, string type, int amount = 1, int duration = -1, string? display = null) => new(name, type, amount, duration, amount > 0, display);

    private static TradeOptionsResult Options(IReadOnlyList<TradeOfferEntry>? ours = null, IReadOnlyList<TradeOfferEntry>? theirs = null) =>
        new("Rome", ours ?? [], theirs ?? [], false);

    private static TradeBuilder Builder(IReadOnlyList<TradeOfferEntry>? ours = null, IReadOnlyList<TradeOfferEntry>? theirs = null) => new("Rome", Options(ours, theirs));

    private static CivDto Civ(string id, bool cityState = false, string? name = null) =>
        new() { CivId = id, Name = name ?? id, IsCityState = cityState, IsMajor = !cityState, IsBarbarian = false, IsDefeated = false };

    private static ClientSnapshot Snapshot(IEnumerable<PendingTradeDto>? trades = null) => new()
    {
        Seq = 1,
        Turn = 1,
        Phase = "Playing",
        TurnStatus = new TurnStatusDto { Ended = [], Phase = "Playing" },
        You = "Me",
        Civs = [Civ("Me"), Civ("rome", name: "Rome"), Civ("Aksum", cityState: true), Civ("Egypt")],
        Tiles = [],
        Cities = [],
        Units = [],
        Diplomacy =
        [
            new DiplomacyDto { A = "Me", B = "rome", AtWar = false, Status = "Peace" },
            new DiplomacyDto { A = "Me", B = "Aksum", AtWar = false, Status = "Peace" },
        ],
        PendingTrades = (trades ?? []).ToList(),
        Notifications = [],
    };

    private static TradeOfferDto Dto(string name, string type, int amount = 1, int duration = -1) => new() { Name = name, Type = type, Amount = amount, Duration = duration };

    [Fact]
    public void PartnersAreMetMajorCivsOnly()
    {
        Assert.Equal(["rome"], TradeScreenModel.Partners(Snapshot()).Select(it => it.CivId));
    }

    [Fact]
    public void GroupsFollowTheFixedOrderAndKeepEngineOrderInside()
    {
        var builder = Builder(ours:
        [
            Offer("Open Borders", "Agreement", duration: 30), Offer("Silk", "Luxury_Resource", 2), Offer("Gold per turn", "Gold_Per_Turn", 5), Offer("Gold", "Gold", 100),
            Offer("Dyes", "Luxury_Resource", 1), Offer("Iron", "Strategic_Resource", 3), Offer("Accept Embassy", "Embassy"), Offer("Oil", "Stockpiled_Resource", 4),
        ]);
        var groups = builder.Groups(TradeSide.Give);
        Assert.Equal(["Gold", "Gold per turn", "Luxury resources", "Strategic resources", "Treaties and agreements"], groups.Select(it => it.Title));
        Assert.Equal(["Silk", "Dyes"], groups[2].Choices.Select(it => it.Entry.Name));
        Assert.Equal(["Iron", "Oil"], groups[3].Choices.Select(it => it.Entry.Name));
        Assert.Equal(["Open Borders", "Accept Embassy"], groups[4].Choices.Select(it => it.Entry.Name));
        Assert.Empty(builder.Groups(TradeSide.Ask));
    }

    [Fact]
    public void EveryGroupHeadingCoversItsOwnTypesAndNoTypeTwice()
    {
        var all = TradeScreenModel.GroupOrder.SelectMany(it => it.Types).ToList();
        Assert.Equal(all.Count, all.Distinct().Count());
        foreach (var type in Enum.GetNames<Unciv.Logic.Trade.TradeOfferType>()) Assert.Contains(type, all);
    }

    [Fact]
    public void ToggleStagesQuantityAtOneStepOrLessAndUnstagesOnSecondPress()
    {
        var builder = Builder(ours: [Offer("Gold", "Gold", 100), Offer("Gold per turn", "Gold_Per_Turn", 5), Offer("Pearls", "Luxury_Resource", 2), Offer("Tiny", "Gold", 4)]);
        Assert.True(builder.Toggle(TradeSide.Give, "Gold", "Gold"));
        Assert.True(builder.Toggle(TradeSide.Give, "Gold_Per_Turn", "Gold per turn"));
        Assert.True(builder.Toggle(TradeSide.Give, "Luxury_Resource", "Pearls"));
        Assert.True(builder.Toggle(TradeSide.Give, "Gold", "Tiny"));
        Assert.Equal([10, 1, 1, 4], builder.StagedChoices(TradeSide.Give).Select(it => it.StagedAmount));
        Assert.True(builder.Toggle(TradeSide.Give, "Gold", "Gold"));
        Assert.Equal(["Gold per turn", "Pearls", "Tiny"], builder.StagedChoices(TradeSide.Give).Select(it => it.Entry.Name));
        Assert.False(builder.IsEmpty);
    }

    [Fact]
    public void StockpiledResourceIsAQuantityLikeAnyOtherResource()
    {
        var builder = Builder(ours: [Offer("Oil", "Stockpiled_Resource", 5)]);
        Assert.True(TradeScreenModel.IsQuantity("Stockpiled_Resource"));
        builder.Toggle(TradeSide.Give, "Stockpiled_Resource", "Oil");
        Assert.Equal(1, builder.StagedChoices(TradeSide.Give).Single().StagedAmount);
        Assert.True(builder.Adjust(TradeSide.Give, "Stockpiled_Resource", "Oil", 2));
        Assert.Equal(3, builder.StagedChoices(TradeSide.Give).Single().StagedAmount);
    }

    [Fact]
    public void ToggleRefusesUnlistedOrUntradableOffers()
    {
        var builder = Builder(ours: [Offer("Gold", "Gold", 100), Offer("Peace with X", "PeaceProposal", 0)]);
        Assert.False(builder.Toggle(TradeSide.Give, "Gold", "Nope"));
        Assert.False(builder.Toggle(TradeSide.Ask, "Gold", "Gold")); // only on our column
        Assert.False(builder.Toggle(TradeSide.Give, "PeaceProposal", "Peace with X")); // Amount 0: shown, not stageable
        Assert.False(builder.Toggle(TradeSide.Give, "Luxury_Resource", "Gold"));
        Assert.True(builder.IsEmpty);
    }

    [Fact]
    public void NonQuantityOfferIsStagedAsAvailable()
    {
        var builder = Builder(ours: [Offer("Open Borders", "Agreement", 1, 30)]);
        builder.Toggle(TradeSide.Give, "Agreement", "Open Borders");
        Assert.Equal(1, builder.StagedChoices(TradeSide.Give).Single().StagedAmount);
        Assert.False(builder.Adjust(TradeSide.Give, "Agreement", "Open Borders", 1));
    }

    [Fact]
    public void AdjustMovesByStepAndClampsToOneAndAvailable()
    {
        var builder = Builder(ours: [Offer("Gold", "Gold", 35), Offer("Iron", "Strategic_Resource", 3)]);
        builder.Toggle(TradeSide.Give, "Gold", "Gold");
        builder.Toggle(TradeSide.Give, "Strategic_Resource", "Iron");
        Assert.True(builder.Adjust(TradeSide.Give, "Gold", "Gold", 2));
        Assert.Equal(30, builder.StagedChoices(TradeSide.Give)[0].StagedAmount);
        builder.Adjust(TradeSide.Give, "Gold", "Gold", 1);
        Assert.Equal(35, builder.StagedChoices(TradeSide.Give)[0].StagedAmount); // clamped to available
        builder.Adjust(TradeSide.Give, "Gold", "Gold", -100);
        Assert.Equal(1, builder.StagedChoices(TradeSide.Give)[0].StagedAmount); // clamped to 1
        builder.Adjust(TradeSide.Give, "Strategic_Resource", "Iron", 1);
        builder.Adjust(TradeSide.Give, "Strategic_Resource", "Iron", 1);
        builder.Adjust(TradeSide.Give, "Strategic_Resource", "Iron", 1);
        Assert.Equal(3, builder.StagedChoices(TradeSide.Give)[1].StagedAmount);
        builder.Adjust(TradeSide.Give, "Gold", "Gold", int.MaxValue); // no overflow
        Assert.Equal(35, builder.StagedChoices(TradeSide.Give)[0].StagedAmount);
    }

    [Fact]
    public void AdjustRefusesWhatIsNotStaged()
    {
        var builder = Builder(ours: [Offer("Gold", "Gold", 100)]);
        Assert.False(builder.Adjust(TradeSide.Give, "Gold", "Gold", 1));
        Assert.False(builder.Adjust(TradeSide.Give, "Gold", "Missing", 1));
    }

    [Fact]
    public void TreatyIsStagedOnOneColumnAndUnstagedByPressingItAgain()
    {
        var treaty = Offer("Research Agreement", "Treaty", 250, 30);
        var builder = Builder(ours: [treaty], theirs: [treaty]);
        builder.Toggle(TradeSide.Give, "Treaty", "Research Agreement");
        var command = builder.Command()!;
        Assert.Equal("Research Agreement", Assert.Single(command.OurOffers).Name);
        Assert.Empty(command.TheirOffers);
        builder.Toggle(TradeSide.Give, "Treaty", "Research Agreement");
        Assert.True(builder.IsEmpty);
    }

    [Fact]
    public void TreatyOnlyOnTheirColumnIsSentOnTheirList()
    {
        var builder = Builder(theirs: [Offer("Peace Treaty", "Treaty", 1, 10)]);
        builder.Toggle(TradeSide.Ask, "Treaty", "Peace Treaty");
        var command = builder.Command()!;
        Assert.Empty(command.OurOffers);
        Assert.Equal("Peace Treaty", Assert.Single(command.TheirOffers).Name);
    }

    [Fact]
    public void TreatyStagedOnOneColumnReplacesTheOtherColumnsCopy()
    {
        var treaty = Offer("Peace Treaty", "Treaty", 1, 10);
        var builder = Builder(ours: [treaty], theirs: [treaty]);
        builder.Toggle(TradeSide.Ask, "Treaty", "Peace Treaty");
        builder.Toggle(TradeSide.Give, "Treaty", "Peace Treaty");
        Assert.Empty(builder.StagedChoices(TradeSide.Ask));
        Assert.Single(builder.StagedChoices(TradeSide.Give));
    }

    [Fact]
    public void CommandCarriesPartnerSidesAndAmountsAndIsNullWhenEmpty()
    {
        var builder = Builder(ours: [Offer("Gold", "Gold", 100)], theirs: [Offer("Silk", "Luxury_Resource", 2)]);
        Assert.Null(builder.Command());
        builder.Toggle(TradeSide.Give, "Gold", "Gold");
        builder.Adjust(TradeSide.Give, "Gold", "Gold", 3);
        builder.Toggle(TradeSide.Ask, "Luxury_Resource", "Silk");
        var command = builder.Command()!;
        Assert.Equal("Rome", command.CivId);
        Assert.Equal(new TradeOfferSpec("Gold", "Gold", 40), command.OurOffers.Single());
        Assert.Equal(new TradeOfferSpec("Silk", "Luxury_Resource", 1), command.TheirOffers.Single());
        builder.Clear();
        Assert.Null(builder.Command());
    }

    [Fact]
    public void HintRequestsAreTheStagedOffersOfOneColumn()
    {
        var builder = Builder(ours: [Offer("Gold", "Gold", 100)], theirs: [Offer("Silk", "Luxury_Resource", 2)]);
        builder.Toggle(TradeSide.Give, "Gold", "Gold");
        builder.Toggle(TradeSide.Ask, "Luxury_Resource", "Silk");
        Assert.Equal([new TradeOfferRequest("Gold", "Gold", 10)], builder.HintRequests(TradeSide.Give));
        Assert.Equal([new TradeOfferRequest("Silk", "Luxury_Resource", 1)], builder.HintRequests(TradeSide.Ask));
    }

    [Fact]
    public void SummaryWordsBothColumns()
    {
        var builder = Builder(ours: [Offer("Gold", "Gold", 100)], theirs: [Offer("Silk", "Luxury_Resource", 2)]);
        Assert.Equal(["You give: nothing", "You ask: nothing"], builder.Summary());
        builder.Toggle(TradeSide.Give, "Gold", "Gold");
        builder.Toggle(TradeSide.Ask, "Luxury_Resource", "Silk");
        Assert.Equal(["You give: 10 gold", "You ask: Silk x1"], builder.Summary());
    }

    [Fact]
    public void StageRequestPutsWhatTheyAskOnOurColumnAndWhatTheyOfferOnTheirs()
    {
        var builder = Builder(ours: [Offer("Gold", "Gold", 100), Offer("Open Borders", "Agreement", 1, 30)], theirs: [Offer("Gold", "Gold", 50), Offer("Silk", "Luxury_Resource", 2)]);
        builder.Toggle(TradeSide.Give, "Gold", "Gold"); // replaced by the request
        var request = new PendingTradeDto
        {
            From = "rome",
            To = "Me",
            Offered = [Dto("Gold", "Gold", 500), Dto("Silk", "Luxury_Resource", 1), Dto("Gone", "Luxury_Resource", 1)],
            Requested = [Dto("Gold", "Gold", 70), Dto("Open Borders", "Agreement", 1, 30)],
        };
        int skipped = builder.StageRequest(request);
        Assert.Equal(1, skipped);
        Assert.Equal([70, 1], builder.StagedChoices(TradeSide.Give).Select(it => it.StagedAmount));
        Assert.Equal([50, 1], builder.StagedChoices(TradeSide.Ask).Select(it => it.StagedAmount)); // 500 clamped to the 50 available
    }

    [Fact]
    public void StageRequestSkipsUntradableAndStagesATreatyOnce()
    {
        var treaty = Offer("Peace Treaty", "Treaty", 1, 10);
        var builder = Builder(ours: [treaty, Offer("Peace with X", "PeaceProposal", 0)], theirs: [treaty]);
        var request = new PendingTradeDto
        {
            From = "rome",
            To = "Me",
            Offered = [Dto("Peace Treaty", "Treaty", 1, 10)],
            Requested = [Dto("Peace Treaty", "Treaty", 1, 10), Dto("Peace with X", "PeaceProposal", 1), Dto("Peace Treaty", "Treaty", 1, 10)],
        };
        Assert.Equal(1, builder.StageRequest(request));
        Assert.Single(builder.StagedChoices(TradeSide.Give));
        Assert.Empty(builder.StagedChoices(TradeSide.Ask));
    }

    [Fact]
    public void ChoiceLabelsShowAvailabilityStagedMarkAndDuration()
    {
        var builder = Builder(ours: [Offer("Gold", "Gold", 100), Offer("Open Borders", "Agreement", 1, 30), Offer("Peace with X", "PeaceProposal", 0), Offer("c1", "City", 1, display: "Antium")]);
        var give = builder.Groups(TradeSide.Give).SelectMany(it => it.Choices).ToDictionary(it => it.Entry.Name);
        Assert.Equal("100 gold", give["Gold"].Label);
        Assert.Equal("Open Borders (30 turns)", give["Open Borders"].Label);
        Assert.Equal("Peace with Peace with X (not possible)", give["Peace with X"].Label);
        Assert.Equal("City: Antium", give["c1"].Label);
        builder.Toggle(TradeSide.Give, "Gold", "Gold");
        Assert.Equal("[x] 10 gold", builder.Groups(TradeSide.Give)[0].Choices[0].Label);
        Assert.Equal("Give_Gold_Gold", builder.Groups(TradeSide.Give)[0].Choices[0].Id);
        Assert.True(builder.Groups(TradeSide.Give)[0].Choices[0].Quantity);
        Assert.Equal("Ask_Agreement_Open_Borders", TradeScreenModel.KeyOf(TradeSide.Ask, "Agreement", "Open Borders"));
    }

    [Fact]
    public void OfferTextCoversEveryKind()
    {
        Assert.Equal("5 gold per turn (30 turns)", TradeScreenModel.OfferText("Gold per turn", "Gold_Per_Turn", 5, 30));
        Assert.Equal("Iron x3", TradeScreenModel.OfferText("Iron", "Strategic_Resource", 3, -1));
        Assert.Equal("Introduction to Egypt", TradeScreenModel.OfferText("Egypt", "Introduction", 1, -1));
        Assert.Equal("War on Egypt (30 turns)", TradeScreenModel.OfferText("Egypt", "WarDeclaration", 1, 30));
        Assert.Equal("Peace with Egypt", TradeScreenModel.OfferText("Egypt", "PeaceProposal", 1, 0));
        Assert.Equal("Technology: Writing", TradeScreenModel.OfferText("Writing", "Technology", 1, -1));
        Assert.Equal("Accept Embassy", TradeScreenModel.OfferText("Accept Embassy", "Embassy", 1, -1));
        Assert.Equal("50 gold", TradeScreenModel.OfferText("Gold", "Gold", 50, 5)); // gold has no duration even if the engine reports one
        Assert.Equal("Gold", TradeScreenModel.NameOf(Offer("Gold", "Gold")));
        Assert.Equal("Antium", TradeScreenModel.NameOf(Offer("c1", "City", display: "Antium")));
    }

    [Fact]
    public void IncomingListsOnlyTradesAddressedToTheViewerWithWords()
    {
        var snapshot = Snapshot(
        [
            new PendingTradeDto { From = "rome", To = "Me", Offered = [Dto("Gold", "Gold", 100), Dto("Egypt", "Introduction")], Requested = [Dto("Open Borders", "Agreement", 1, 30), Dto("rome", "PeaceProposal")] },
            new PendingTradeDto { From = "Me", To = "rome", Offered = [Dto("Gold", "Gold", 5)], Requested = [] },
            new PendingTradeDto { From = "Egypt", To = "rome", Offered = [], Requested = [] },
        ]);
        var incoming = Assert.Single(TradeScreenModel.Incoming(snapshot));
        Assert.Equal("rome", incoming.FromId);
        Assert.Equal("Rome proposes a trade", incoming.Title);
        Assert.Equal(["100 gold", "Introduction to Egypt"], incoming.Gives);
        Assert.Equal(["Open Borders (30 turns)", "Peace with Rome"], incoming.Asks);
        Assert.Equal(("rome", true), (incoming.Accept.CivId, incoming.Accept.Accept));
        Assert.Equal(("rome", false), (incoming.Decline.CivId, incoming.Decline.Accept));
        Assert.Equal("rome", incoming.Counter.CivId);
    }

    [Fact]
    public void IncomingUsesTheNameLookupWhenItKnowsAnId()
    {
        var snapshot = Snapshot([new PendingTradeDto { From = "rome", To = "Me", Offered = [Dto("c-1", "City")], Requested = [] }]);
        var withLookup = Assert.Single(TradeScreenModel.Incoming(snapshot, (type, name) => type == "City" && name == "c-1" ? "Antium" : null));
        Assert.Equal(["City: Antium"], withLookup.Gives);
        Assert.Equal(["City: c-1"], Assert.Single(TradeScreenModel.Incoming(snapshot)).Gives);
    }

    [Fact]
    public void OutgoingIsTheProposalWeSentToThatCivFromOurSide()
    {
        var snapshot = Snapshot(
        [
            new PendingTradeDto { From = "Egypt", To = "rome", Offered = [Dto("Gold", "Gold", 1)], Requested = [] }, // someone else's proposal to the same civ
            new PendingTradeDto { From = "Me", To = "rome", Offered = [Dto("Gold", "Gold", 100)], Requested = [Dto("Silk", "Luxury_Resource", 1)] },
            new PendingTradeDto { From = "rome", To = "Me", Offered = [], Requested = [] },
        ]);
        var outgoing = TradeScreenModel.Outgoing(snapshot, "rome")!;
        Assert.Equal(["100 gold"], outgoing.Gives);
        Assert.Equal(["Silk x1"], outgoing.Asks);
        Assert.Null(TradeScreenModel.Outgoing(snapshot, "Egypt"));
        Assert.Null(TradeScreenModel.Outgoing(Snapshot(), "rome"));
    }

    private static PendingDecisionEntry Alert(string type, string value, bool ackOnly = false) =>
        new("Alert", type, "respond-demand", false, 0, new AlertDetail(type, value, ackOnly, null, []));

    [Fact]
    public void DemandsAreTheAnswerableDemandAlertsOnly()
    {
        var pending = new PendingDecisionsResult(
        [
            Alert("DemandToStopSpreadingReligion", "rome"),
            Alert("DemandToStopSpyingOnUs", "Egypt", ackOnly: true),
            Alert("WonderBuilt", "x"),
            Alert("NotAnAlertType", "x"),
            new PendingDecisionEntry("TradeRequest", "rome", "respond-trade-request", false, 0, null),
            Alert("DemandToStopSettlingCitiesNear", "Unknown"),
            Alert("DemandToNotAttackUs", "rome"),
        ]);
        var demands = TradeScreenModel.Demands(pending, Snapshot());
        Assert.Equal(["DoNotSpreadReligion", "DoNotSettleNearUs", "DoNotAttackUs"], demands.Select(it => it.Demand));
        Assert.Equal("Rome demands: stop spreading your religion to us", demands[0].Title);
        Assert.Equal("Unknown", demands[1].FromName);
        Assert.Equal("stop settling cities near us", demands[1].Text);
        Assert.Equal("stop attacking us", demands[2].Text);
        Assert.Equal(("rome", "DoNotSpreadReligion", true), (demands[0].Accept.CivId, demands[0].Accept.Demand, demands[0].Accept.Accept));
        Assert.False(demands[0].Refuse.Accept);
        Assert.Equal("stop spying on us", TradeScreenModel.Demands(new PendingDecisionsResult([Alert("DemandToStopSpyingOnUs", "rome")]), Snapshot())[0].Text);
    }

    [Fact]
    public void HintTextWordsEachBucketAndIsEmptyWithoutAHint()
    {
        Assert.Equal("They would likely accept.", TradeScreenModel.HintText(new TradeHintResult("Rome", "accept")));
        Assert.Equal("They want more; adding gold from your treasury could do.", TradeScreenModel.HintText(new TradeHintResult("Rome", "counter-needed")));
        Assert.Equal("They would refuse.", TradeScreenModel.HintText(new TradeHintResult("Rome", "refuse")));
        Assert.Equal("", TradeScreenModel.HintText(new TradeHintResult("Rome", "???")));
        Assert.Equal("", TradeScreenModel.HintText(null));
    }
}
