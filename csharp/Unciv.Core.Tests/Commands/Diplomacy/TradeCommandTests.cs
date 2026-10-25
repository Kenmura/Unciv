using Unciv.Commands;
using Unciv.Commands.Diplomacy;
using Unciv.Commands.Events;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Logic.Civilization.Managers;
using Unciv.Logic.Trade;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Core.Tests.Commands.Diplomacy;

// T193: trade proposals and their answers.
[Collection("RulesetCache")]
public sealed class TradeCommandTests
{
    private readonly DiplomacyWorld w = new();

    private static TradeOfferSpec Gold(int amount) => new(Constants.FlatGold, nameof(TradeOfferType.Gold), amount);

    private void SwitchTo(CivilizationObject civ)
    {
        w.Game.GameInfo.CurrentPlayerCiv = civ;
        w.Game.GameInfo.CurrentPlayer = civ.CivId;
    }

    private void GiveGold(CivilizationObject civ, int gold) => civ.AddGold(gold - civ.Gold);

    [Fact]
    public void ProposeTradeOnlyQueuesARequestWithTheOffersFromTheReceiversSide()
    {
        GiveGold(w.A, 300);
        GiveGold(w.B, 50);
        w.Ok(new ProposeTrade { CivId = w.B.CivId, OurOffers = [Gold(100)], TheirOffers = [Gold(20)] });

        var request = Assert.Single(w.B.TradeRequests);
        Assert.Equal(w.A.CivId, request.RequestingCiv);
        Assert.Equal(100, Assert.Single(request.Trade.TheirOffers).Amount); // what A offers is what B receives
        Assert.Equal(20, Assert.Single(request.Trade.OurOffers).Amount);
        Assert.Equal(300, w.A.Gold);
        Assert.Equal(50, w.B.Gold);
    }

    [Fact]
    public void AnOfferAlreadyPendingMustBeRetractedFirst()
    {
        GiveGold(w.A, 300);
        w.Ok(new ProposeTrade { CivId = w.B.CivId, OurOffers = [Gold(100)] });
        w.Rejected(new ProposeTrade { CivId = w.B.CivId, OurOffers = [Gold(50)] }, CommandErrors.NotAllowed);

        w.Ok(new RetractTrade { CivId = w.B.CivId });
        Assert.Empty(w.B.TradeRequests);
        w.Rejected(new RetractTrade { CivId = w.B.CivId }, CommandErrors.StaleReference);
        w.Ok(new ProposeTrade { CivId = w.B.CivId, OurOffers = [Gold(50)] });
    }

    [Fact]
    public void ProposeTradeRejectsOffersThatAreNotAvailable()
    {
        GiveGold(w.A, 300);
        GiveGold(w.B, 0);
        var request = (Func<TradeOfferSpec[], TradeOfferSpec[], ProposeTrade>)((ours, theirs) =>
            new ProposeTrade { CivId = w.B.CivId, OurOffers = ours, TheirOffers = theirs });

        w.Rejected(request([Gold(301)], []), CommandErrors.InvalidTarget);
        w.Rejected(request([Gold(0)], []), CommandErrors.InvalidTarget);
        w.Rejected(request([Gold(-5)], []), CommandErrors.InvalidTarget);
        w.Rejected(request([], [Gold(10)]), CommandErrors.InvalidTarget); // B has no gold
        w.Rejected(request([Gold(10), Gold(20)], []), CommandErrors.InvalidTarget); // twice
        w.Rejected(request([new TradeOfferSpec("Iron", nameof(TradeOfferType.Strategic_Resource))], []), CommandErrors.InvalidTarget);
        w.Rejected(request([new TradeOfferSpec(Constants.FlatGold, "Nonsense")], []), CommandErrors.InvalidTarget);
        w.Rejected(request([], []), CommandErrors.NotAllowed);
        w.Rejected(new ProposeTrade { CivId = w.S.CivId, OurOffers = [Gold(10)] }, CommandErrors.InvalidTarget);
        w.Rejected(new ProposeTrade { CivId = w.B.CivId, OurOffers = [null!] }, CommandErrors.InvalidTarget);
    }

    [Fact]
    public void AcceptingMovesTheGoldTellsTheProposerAndDropsTheRequest()
    {
        GiveGold(w.A, 300);
        GiveGold(w.B, 50);
        w.Ok(new ProposeTrade { CivId = w.B.CivId, OurOffers = [Gold(100)], TheirOffers = [Gold(20)] });
        SwitchTo(w.B);

        var result = w.Ok(new RespondToTradeRequest { CivId = w.A.CivId, Accept = true }, w.B.CivId);

        Assert.Empty(w.B.TradeRequests);
        Assert.Equal(220, w.A.Gold);
        Assert.Equal(130, w.B.Gold);
        Assert.Contains(w.A.Notifications, it => it.Text == $"[{w.B.CivName}] has accepted your trade request");
        Assert.Contains(result.Events, it => it is TradeAccepted);
    }

    [Fact]
    public void DecliningTellsTheProposerAndLeavesTheGoldAlone()
    {
        GiveGold(w.A, 300);
        w.Ok(new ProposeTrade { CivId = w.B.CivId, OurOffers = [Gold(100)] });
        SwitchTo(w.B);
        w.Ok(new RespondToTradeRequest { CivId = w.A.CivId, Accept = false }, w.B.CivId);
        Assert.Empty(w.B.TradeRequests);
        Assert.Equal(300, w.A.Gold);
        Assert.Contains(w.A.Notifications, it => it.Text == $"[{w.B.CivName}] has denied your trade request");
        w.Rejected(new RespondToTradeRequest { CivId = w.A.CivId, Accept = false }, CommandErrors.StaleReference, w.B.CivId);
    }

    [Fact]
    public void ARequestThatCanNoLongerBeHonouredCannotBeAcceptedButCanBeDeclined()
    {
        GiveGold(w.A, 300);
        w.Ok(new ProposeTrade { CivId = w.B.CivId, OurOffers = [Gold(100)] });
        w.A.AddGold(-w.A.Gold); // spent elsewhere
        SwitchTo(w.B);
        var accept = new RespondToTradeRequest { CivId = w.A.CivId, Accept = true };
        w.Rejected(accept, CommandErrors.StaleReference, w.B.CivId);
        w.Ok(new RespondToTradeRequest { CivId = w.A.CivId, Accept = false }, w.B.CivId);
    }

    [Fact]
    public void ARecipientMayAnswerAfterItsTurnPassedToSomeoneElseOnlyIfActive()
    {
        GiveGold(w.A, 300);
        w.Ok(new ProposeTrade { CivId = w.B.CivId, OurOffers = [Gold(100)] });
        w.Rejected(new RespondToTradeRequest { CivId = w.A.CivId, Accept = true }, CommandErrors.NotYourTurn, w.B.CivId);
        w.Rejected(new RespondToTradeRequest { CivId = "Atlantis", Accept = true }, CommandErrors.UnknownCiv, w.A.CivId);
    }

    [Fact]
    public void DismissingARequestDropsItQuietly()
    {
        GiveGold(w.A, 300);
        w.Ok(new ProposeTrade { CivId = w.B.CivId, OurOffers = [Gold(100)] });
        SwitchTo(w.B);
        int notifications = w.A.Notifications.Count;
        w.Ok(new DismissTradeRequest { CivId = w.A.CivId }, w.B.CivId);
        Assert.Empty(w.B.TradeRequests);
        Assert.Equal(notifications, w.A.Notifications.Count);
        Assert.False(w.Diplo(w.A, w.B).HasFlag(DiplomacyFlags.DeclinedPeace));
        w.Rejected(new DismissTradeRequest { CivId = w.A.CivId }, CommandErrors.StaleReference, w.B.CivId);
        w.Rejected(new DismissTradeRequest { CivId = "Atlantis" }, CommandErrors.UnknownCiv, w.B.CivId);
    }

    [Fact]
    public void PeaceIsATreatyOnBothSidesAndWaitsForTheDeclaredWarCooldown()
    {
        w.MakeWar(w.A, w.B);
        var peace = new TradeOfferSpec(Constants.PeaceTreaty, nameof(TradeOfferType.Treaty));
        w.Diplo(w.B, w.A).SetFlag(DiplomacyFlags.DeclaredWar, 10);
        w.Rejected(new ProposeTrade { CivId = w.B.CivId, OurOffers = [peace] }, CommandErrors.NotAllowed);
        w.Diplo(w.B, w.A).RemoveFlag(DiplomacyFlags.DeclaredWar);

        w.Ok(new ProposeTrade { CivId = w.B.CivId, OurOffers = [peace] });
        var request = Assert.Single(w.B.TradeRequests);
        Assert.Equal(Constants.PeaceTreaty, Assert.Single(request.Trade.OurOffers).Name);
        Assert.Equal(Constants.PeaceTreaty, Assert.Single(request.Trade.TheirOffers).Name);

        SwitchTo(w.B);
        var result = w.Ok(new RespondToTradeRequest { CivId = w.A.CivId, Accept = true }, w.B.CivId);
        Assert.False(w.A.IsAtWarWith(w.B));
        Assert.Contains(result.Events, it => it is PeaceMade);
    }

    [Fact]
    public void PeaceCannotBeProposedWhenAtPeaceOrWhenTheRulesetFreezesRelationships()
    {
        var peace = new TradeOfferSpec(Constants.PeaceTreaty, nameof(TradeOfferType.Treaty));
        w.Rejected(new ProposeTrade { CivId = w.B.CivId, OurOffers = [peace] }, CommandErrors.InvalidTarget); // not at war: not available
        var frozen = new DiplomacyWorld(modUniques: ["Diplomatic relationships cannot change"]);
        frozen.MakeWar(frozen.A, frozen.B);
        frozen.Rejected(new ProposeTrade { CivId = frozen.B.CivId, OurOffers = [peace] }, CommandErrors.NotAllowed);
    }

    [Fact]
    public void AResearchAgreementTopsUpTheGoldTheShortSideNeeds()
    {
        var world = new DiplomacyWorld(["Enables Research agreements"], bUniques: ["Enables Research agreements"]);
        world.Diplo(world.A, world.B).SetFlag(DiplomacyFlags.DeclarationOfFriendship, 30);
        world.Diplo(world.B, world.A).SetFlag(DiplomacyFlags.DeclarationOfFriendship, 30);
        world.Diplo(world.A, world.B).SetModifier(DiplomaticModifiers.SharedEmbassies, 0f);
        world.Diplo(world.B, world.A).SetModifier(DiplomaticModifiers.SharedEmbassies, 0f);
        int cost = world.A.DiplomacyFunctions.GetResearchAgreementCost(world.B);
        world.A.AddGold(-world.A.Gold);
        world.B.AddGold(cost + 500 - world.B.Gold);

        world.Ok(new ProposeTrade
        {
            CivId = world.B.CivId,
            OurOffers = [new TradeOfferSpec(Constants.ResearchAgreement, nameof(TradeOfferType.Treaty))],
        });

        var request = Assert.Single(world.B.TradeRequests);
        Assert.Contains(request.Trade.OurOffers, it => it.Name == Constants.ResearchAgreement);
        Assert.Contains(request.Trade.TheirOffers, it => it.Name == Constants.ResearchAgreement);
        // A cannot pay, so B is asked for the gold: in B's request it is one of B's own (our) offers
        Assert.Equal(cost, Assert.Single(request.Trade.OurOffers, it => it.Type == TradeOfferType.Gold).Amount);
    }

    [Fact]
    public void AResearchAgreementTopsUpOurOwnGoldToo()
    {
        var world = new DiplomacyWorld(["Enables Research agreements"], bUniques: ["Enables Research agreements"]);
        world.Diplo(world.A, world.B).SetFlag(DiplomacyFlags.DeclarationOfFriendship, 30);
        world.Diplo(world.B, world.A).SetFlag(DiplomacyFlags.DeclarationOfFriendship, 30);
        world.Diplo(world.A, world.B).SetModifier(DiplomaticModifiers.SharedEmbassies, 0f);
        world.Diplo(world.B, world.A).SetModifier(DiplomaticModifiers.SharedEmbassies, 0f);
        int cost = world.A.DiplomacyFunctions.GetResearchAgreementCost(world.B);
        world.B.AddGold(-world.B.Gold);
        world.A.AddGold(cost + 500 - world.A.Gold);

        world.Ok(new ProposeTrade
        {
            CivId = world.B.CivId,
            OurOffers = [new TradeOfferSpec(Constants.ResearchAgreement, nameof(TradeOfferType.Treaty))],
        });

        var request = Assert.Single(world.B.TradeRequests);
        Assert.Equal(cost, Assert.Single(request.Trade.TheirOffers, it => it.Type == TradeOfferType.Gold).Amount); // B receives A's gold
    }

    [Fact]
    public void AGiftToAnAiIsAnsweredWhenTheAiTakesItsTurn()
    {
        GiveGold(w.A, 300);
        GiveGold(w.C, 0);
        w.Ok(new ProposeTrade { CivId = w.C.CivId, OurOffers = [Gold(100)] });
        Assert.Single(w.C.TradeRequests);
        Assert.Equal(300, w.A.Gold); // no verdict yet

        new TurnManager(w.C).AutomateTurn();

        Assert.Empty(w.C.TradeRequests);
        Assert.Contains(w.A.Notifications, it => it.Text == $"[{w.C.CivName}] has accepted your trade request");
        Assert.Equal(200, w.A.Gold);
        Assert.True(w.C.Gold >= 100);
    }

    [Fact]
    public void AnAiAnswersADemandingTradeWithADenialOrACounteroffer()
    {
        GiveGold(w.A, 0);
        GiveGold(w.C, 400);
        w.Ok(new ProposeTrade { CivId = w.C.CivId, TheirOffers = [Gold(300)] });

        new TurnManager(w.C).AutomateTurn();

        Assert.DoesNotContain(w.C.TradeRequests, it => it.RequestingCiv == w.A.CivId);
        Assert.Contains(w.A.Notifications, it => it.Text == $"[{w.C.CivName}] has denied your trade request"
            || it.Text == $"[{w.C.CivName}] has made a counteroffer to your trade request");
    }
}
