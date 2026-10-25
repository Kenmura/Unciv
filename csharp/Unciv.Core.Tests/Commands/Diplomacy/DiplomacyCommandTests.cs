using System.Text.Json;
using Unciv.Commands;
using Unciv.Commands.Diplomacy;
using Unciv.Commands.Events;
using Unciv.Json;
using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Logic.Civilization.Managers;
using Unciv.Logic.Trade;
using DeclareWarCommand = Unciv.Commands.Diplomacy.DeclareWar;

namespace Unciv.Core.Tests.Commands.Diplomacy;

// T193: war, denunciation, friendship and demands (the popup-alert half of diplomacy).
[Collection("RulesetCache")]
public sealed class DiplomacyCommandTests
{
    private readonly DiplomacyWorld w = new();

    [Fact]
    public void CommandsAreRegisteredAndRoundTripThroughJson()
    {
        var names = GameCommandRegistry.Scan().ToDictionary(it => it.Name, it => it.CommandClass);
        Assert.Equal(typeof(DeclareWarCommand), names["declare-war"]);
        Assert.Equal(typeof(Denounce), names["denounce"]);
        Assert.Equal(typeof(OfferFriendship), names["offer-friendship"]);
        Assert.Equal(typeof(RespondToFriendship), names["respond-friendship"]);
        Assert.Equal(typeof(MakeDemand), names["make-demand"]);
        Assert.Equal(typeof(RespondToDemand), names["respond-demand"]);
        Assert.Equal(typeof(ProposeTrade), names["propose-trade"]);
        Assert.Equal(typeof(RetractTrade), names["retract-trade"]);
        Assert.Equal(typeof(RespondToTradeRequest), names["respond-trade-request"]);
        Assert.Equal(typeof(DismissTradeRequest), names["dismiss-trade-request"]);
        Assert.Equal(typeof(GiftGold), names["gift-gold"]);
        Assert.Equal(typeof(GiftImprovement), names["gift-improvement"]);
        Assert.Equal(typeof(TributeGold), names["tribute-gold"]);
        Assert.Equal(typeof(TributeWorker), names["tribute-worker"]);
        Assert.Equal(typeof(PledgeProtection), names["pledge-protection"]);
        Assert.Equal(typeof(WithdrawProtection), names["withdraw-protection"]);
        Assert.Equal(typeof(DiplomaticMarriage), names["diplomatic-marriage"]);
        Assert.Equal(typeof(NegotiateCityStatePeace), names["negotiate-city-state-peace"]);

        var trade = JsonSerializer.Deserialize<ProposeTrade>(
            """{"civId":"x","ourOffers":[{"name":"Gold","kind":"Gold","amount":5}],"theirOffers":[]}""", UncivJson.DefaultOptions)!;
        Assert.Equal("x", trade.CivId);
        Assert.Equal(new TradeOfferSpec("Gold", "Gold", 5), Assert.Single(trade.OurOffers));
        var respond = JsonSerializer.Deserialize<RespondToDemand>("""{"civId":"x","demand":"DoNotSettleNearUs","accept":true}""", UncivJson.DefaultOptions)!;
        Assert.Equal(("x", "DoNotSettleNearUs", true), (respond.CivId, respond.Demand, respond.Accept));
    }

    [Fact]
    public void EveryCommandNeedsAnActiveHumanActor()
    {
        foreach (var command in new GameCommand[]
        {
            new DeclareWarCommand { CivId = w.B.CivId }, new Denounce { CivId = w.B.CivId }, new OfferFriendship { CivId = w.B.CivId },
            new GiftGold { CivId = w.S.CivId, Amount = 250 },
        })
        {
            w.Rejected(command, CommandErrors.NotYourTurn, w.B.CivId);
            w.Rejected(command, CommandErrors.NotHuman, w.C.CivId);
        }
    }

    // ---- war

    [Fact]
    public void DeclareWarDeclaresAWarAndReportsIt()
    {
        w.A.GetDiplomacyManager(w.B)!.DiplomaticStatus = DiplomaticStatus.Peace;
        var result = w.Ok(new DeclareWarCommand { CivId = w.B.CivId });
        Assert.True(w.A.IsAtWarWith(w.B));
        Assert.Contains(result.Events, it => it is WarDeclared { DeclarerCivId: var d, TargetCivId: var t } && d == w.A.CivId && t == w.B.CivId);
    }

    [Fact]
    public void DeclareWarOnACityStateWorksToo()
    {
        w.Diplo(w.A, w.S).DiplomaticStatus = DiplomaticStatus.Peace;
        w.Ok(new DeclareWarCommand { CivId = w.S.CivId });
        Assert.True(w.A.IsAtWarWith(w.S));
    }

    [Fact]
    public void DeclareWarDropsTheTradeRequestsBetweenThePair()
    {
        w.Diplo(w.A, w.B).DiplomaticStatus = DiplomaticStatus.Peace;
        w.B.TradeRequests.Add(new TradeRequest(w.A.CivId, new Trade()));
        w.Ok(new DeclareWarCommand { CivId = w.B.CivId });
        Assert.Empty(w.B.TradeRequests);
    }

    [Fact]
    public void DeclareWarIsRejectedWhereTheScreenDisablesTheButton()
    {
        var war = new DeclareWarCommand { CivId = w.B.CivId };
        w.Rejected(new DeclareWarCommand { CivId = "Atlantis" }, CommandErrors.UnknownCiv);
        w.Rejected(new DeclareWarCommand { CivId = w.A.CivId }, CommandErrors.InvalidTarget);
        w.Rejected(new DeclareWarCommand { CivId = Constants.Barbarians }, CommandErrors.UnknownCiv);

        w.Diplo(w.A, w.B).DiplomaticStatus = DiplomaticStatus.Peace;
        var peace = new Trade();
        peace.OurOffers.Add(new TradeOffer(Constants.PeaceTreaty, TradeOfferType.Treaty, 1, 10));
        w.Diplo(w.A, w.B).Trades.Add(peace);
        w.Rejected(war, CommandErrors.NotAllowed); // a peace treaty is in force

        w.Diplo(w.A, w.B).Trades.Clear();
        w.MakeWar(w.A, w.B);
        w.Rejected(war, CommandErrors.NotAllowed); // already at war
    }

    [Fact]
    public void DeclareWarOnACivWeHaveNotMetOrWhoIsDefeatedIsRejected()
    {
        var stranger = w.Game.AddCiv();
        w.Game.AddCity(stranger, w.Game.GetTile(8, 8));
        w.A.Diplomacy.Remove(stranger.CivId);
        stranger.Diplomacy.Remove(w.A.CivId);
        w.Rejected(new DeclareWarCommand { CivId = stranger.CivId }, CommandErrors.InvalidTarget);
        w.A.DiplomacyFunctions.MakeCivilizationsMeet(stranger);
        stranger.Cities[0].DestroyCity(true);
        Assert.True(stranger.IsDefeated());
        w.Rejected(new DeclareWarCommand { CivId = stranger.CivId }, CommandErrors.InvalidTarget);
    }

    [Fact]
    public void DeclareWarIsRejectedWhenTheRulesetForbidsRelationshipChanges()
    {
        var fixedWorld = new DiplomacyWorld(modUniques: ["Diplomatic relationships cannot change"]);
        fixedWorld.Rejected(new DeclareWarCommand { CivId = fixedWorld.B.CivId }, CommandErrors.NotAllowed);
    }

    // ---- denounce

    [Fact]
    public void DenounceSetsTheFlagAndIsOnlyOfferedOnce()
    {
        w.Diplo(w.A, w.B).DiplomaticStatus = DiplomaticStatus.Peace;
        w.Ok(new Denounce { CivId = w.B.CivId });
        Assert.True(w.Diplo(w.A, w.B).HasFlag(DiplomacyFlags.Denunciation));
        w.Rejected(new Denounce { CivId = w.B.CivId }, CommandErrors.NotAllowed);
    }

    [Fact]
    public void DenounceIsRejectedForCityStatesWarsAndFriends()
    {
        w.Rejected(new Denounce { CivId = w.S.CivId }, CommandErrors.InvalidTarget);
        w.MakeWar(w.A, w.B);
        w.Rejected(new Denounce { CivId = w.B.CivId }, CommandErrors.NotAllowed);
        w.Diplo(w.A, w.C).DiplomaticStatus = DiplomaticStatus.Peace;
        w.Diplo(w.A, w.C).SetFlag(DiplomacyFlags.DeclarationOfFriendship, 30);
        w.Rejected(new Denounce { CivId = w.C.CivId }, CommandErrors.NotAllowed);
    }

    // ---- friendship

    [Fact]
    public void FriendshipOfferIsAnAlertThatTheOtherHumanAccepts()
    {
        w.Diplo(w.A, w.B).DiplomaticStatus = DiplomaticStatus.Peace;
        w.Diplo(w.B, w.A).DiplomaticStatus = DiplomaticStatus.Peace;
        var offer = w.Ok(new OfferFriendship { CivId = w.B.CivId });
        Assert.Contains(offer.Events, it => it is AlertAdded { CivId: var c, AlertType: AlertType.DeclarationOfFriendship } && c == w.B.CivId);
        var alert = Assert.Single(w.B.PopupAlerts);
        Assert.Equal((AlertType.DeclarationOfFriendship, w.A.CivId), (alert.Type, alert.Value));
        w.Rejected(new OfferFriendship { CivId = w.B.CivId }, CommandErrors.NotAllowed); // not answered yet

        w.Game.GameInfo.CurrentPlayerCiv = w.B;
        w.Game.GameInfo.CurrentPlayer = w.B.CivId;
        w.Ok(new RespondToFriendship { CivId = w.A.CivId, Accept = true }, w.B.CivId);
        Assert.Empty(w.B.PopupAlerts);
        Assert.True(w.Diplo(w.A, w.B).HasFlag(DiplomacyFlags.DeclarationOfFriendship));
        Assert.True(w.Diplo(w.B, w.A).HasFlag(DiplomacyFlags.DeclarationOfFriendship));
    }

    [Fact]
    public void DecliningFriendshipSetsTheCooldownOnTheOfferer()
    {
        w.Diplo(w.A, w.B).DiplomaticStatus = DiplomaticStatus.Peace;
        w.Diplo(w.B, w.A).DiplomaticStatus = DiplomaticStatus.Peace;
        w.Ok(new OfferFriendship { CivId = w.B.CivId });
        w.Game.GameInfo.CurrentPlayerCiv = w.B;
        w.Game.GameInfo.CurrentPlayer = w.B.CivId;
        w.Ok(new RespondToFriendship { CivId = w.A.CivId, Accept = false }, w.B.CivId);
        Assert.Equal(20, w.Diplo(w.A, w.B).GetFlag(DiplomacyFlags.DeclinedDeclarationOfFriendship));
        Assert.False(w.Diplo(w.A, w.B).HasFlag(DiplomacyFlags.DeclarationOfFriendship));
        w.Rejected(new RespondToFriendship { CivId = w.A.CivId, Accept = true }, CommandErrors.StaleReference, w.B.CivId);
    }

    [Fact]
    public void AStaleFriendshipOfferIsJustDropped()
    {
        w.Diplo(w.A, w.B).DiplomaticStatus = DiplomaticStatus.Peace;
        w.Diplo(w.B, w.A).DiplomaticStatus = DiplomaticStatus.Peace;
        w.Ok(new OfferFriendship { CivId = w.B.CivId });
        w.MakeWar(w.A, w.B);
        w.Game.GameInfo.CurrentPlayerCiv = w.B;
        w.Game.GameInfo.CurrentPlayer = w.B.CivId;
        w.Ok(new RespondToFriendship { CivId = w.A.CivId, Accept = true }, w.B.CivId);
        Assert.Empty(w.B.PopupAlerts);
        Assert.False(w.Diplo(w.A, w.B).HasFlag(DiplomacyFlags.DeclarationOfFriendship));
    }

    [Fact]
    public void FriendshipOfferIsRejectedAtWarAndWhileInForce()
    {
        w.MakeWar(w.A, w.B);
        w.Rejected(new OfferFriendship { CivId = w.B.CivId }, CommandErrors.NotAllowed);
        w.Rejected(new OfferFriendship { CivId = w.S.CivId }, CommandErrors.InvalidTarget);
        w.Diplo(w.A, w.B).DiplomaticStatus = DiplomaticStatus.Peace;
        w.Diplo(w.A, w.B).SetFlag(DiplomacyFlags.DeclarationOfFriendship, 30);
        w.Rejected(new OfferFriendship { CivId = w.B.CivId }, CommandErrors.NotAllowed);
    }

    [Fact]
    public void AnAiAnswersAFriendshipOfferInItsOwnTurn()
    {
        w.Diplo(w.A, w.C).DiplomaticStatus = DiplomaticStatus.Peace;
        w.Diplo(w.C, w.A).DiplomaticStatus = DiplomaticStatus.Peace;
        w.Ok(new OfferFriendship { CivId = w.C.CivId });
        Assert.Single(w.C.PopupAlerts);

        new TurnManager(w.C).AutomateTurn();

        Assert.Empty(w.C.PopupAlerts);
        bool signed = w.Diplo(w.A, w.C).HasFlag(DiplomacyFlags.DeclarationOfFriendship);
        Assert.Contains(w.A.Notifications, it => it.Text == (signed
            ? $"We have signed a Declaration of Friendship with [{w.C.CivName}]!"
            : $"[{w.C.CivName}] has denied our Declaration of Friendship!"));
    }

    // ---- demands

    [Fact]
    public void ADemandIsAnAlertThatTheOtherHumanAnswers()
    {
        w.Diplo(w.A, w.B).DiplomaticStatus = DiplomaticStatus.Peace;
        w.Ok(new MakeDemand { CivId = w.B.CivId, Demand = "DoNotSettleNearUs" });
        Assert.Equal(AlertType.DemandToStopSettlingCitiesNear, Assert.Single(w.B.PopupAlerts).Type);
        w.Rejected(new MakeDemand { CivId = w.B.CivId, Demand = "DoNotSettleNearUs" }, CommandErrors.NotAllowed);

        w.Game.GameInfo.CurrentPlayerCiv = w.B;
        w.Game.GameInfo.CurrentPlayer = w.B.CivId;
        w.Ok(new RespondToDemand { CivId = w.A.CivId, Demand = "DoNotSettleNearUs", Accept = true }, w.B.CivId);
        Assert.Empty(w.B.PopupAlerts);
        Assert.True(w.Diplo(w.A, w.B).HasFlag(DiplomacyFlags.AgreedToNotSettleNearUs));
    }

    [Fact]
    public void RefusingADemandCostsRelationshipAndRemovesTheAlert()
    {
        w.Diplo(w.A, w.B).DiplomaticStatus = DiplomaticStatus.Peace;
        w.Ok(new MakeDemand { CivId = w.B.CivId, Demand = "DoNotSettleNearUs" });
        w.Game.GameInfo.CurrentPlayerCiv = w.B;
        w.Game.GameInfo.CurrentPlayer = w.B.CivId;
        w.Ok(new RespondToDemand { CivId = w.A.CivId, Demand = "DoNotSettleNearUs", Accept = false }, w.B.CivId);
        Assert.Empty(w.B.PopupAlerts);
        Assert.True(w.Diplo(w.A, w.B).HasFlag(DiplomacyFlags.IgnoreThemSettlingNearUs));
        Assert.True(w.Diplo(w.B, w.A).HasModifier(DiplomaticModifiers.UnacceptableDemands));
    }

    [Fact]
    public void DemandsAreRejectedWhereTheScreenHidesOrDisablesThem()
    {
        w.Rejected(new MakeDemand { CivId = w.B.CivId, Demand = "Nonsense" }, CommandErrors.InvalidTarget);
        w.Rejected(new MakeDemand { CivId = w.B.CivId, Demand = "DoNotAttackUs" }, CommandErrors.InvalidTarget); // never offered to a human
        w.Rejected(new MakeDemand { CivId = w.B.CivId, Demand = "DontSpyOnUs" }, CommandErrors.InvalidTarget); // espionage is off
        w.Rejected(new MakeDemand { CivId = w.S.CivId, Demand = "DoNotSettleNearUs" }, CommandErrors.InvalidTarget);
        w.Game.GameInfo.GameParameters.EspionageEnabled = true;
        w.Ok(new MakeDemand { CivId = w.B.CivId, Demand = "DontSpyOnUs" });
        w.Diplo(w.A, w.B).SetFlag(DiplomacyFlags.AgreedToNotSettleNearUs, 50);
        w.Rejected(new MakeDemand { CivId = w.B.CivId, Demand = "DoNotSettleNearUs" }, CommandErrors.NotAllowed);
    }

    [Fact]
    public void ResponsesNeedAPendingAlert()
    {
        w.Rejected(new RespondToDemand { CivId = w.B.CivId, Demand = "DoNotSettleNearUs", Accept = true }, CommandErrors.StaleReference);
        w.Rejected(new RespondToDemand { CivId = w.B.CivId, Demand = "Nonsense", Accept = true }, CommandErrors.InvalidTarget);
        w.Rejected(new RespondToFriendship { CivId = w.B.CivId, Accept = true }, CommandErrors.StaleReference);
    }

    [Fact]
    public void ADemandFromADefeatedCivIsDroppedAndTheNoAttackUltimatumDeclaresWarOnRefusal()
    {
        w.Diplo(w.A, w.B).DiplomaticStatus = DiplomaticStatus.Peace;
        w.Diplo(w.B, w.A).DiplomaticStatus = DiplomaticStatus.Peace;
        w.A.AddPopupAlert(new PopupAlert(AlertType.DemandToNotAttackUs, w.B.CivId));
        w.Ok(new RespondToDemand { CivId = w.B.CivId, Demand = "DoNotAttackUs", Accept = false });
        Assert.True(w.A.IsAtWarWith(w.B));
        Assert.Empty(w.A.PopupAlerts);

        w.A.AddPopupAlert(new PopupAlert(AlertType.DemandToStopSettlingCitiesNear, w.C.CivId));
        w.C.Cities[0].DestroyCity(true);
        Assert.True(w.C.IsDefeated());
        int before = w.Diplo(w.C, w.A).GetFlag(DiplomacyFlags.IgnoreThemSettlingNearUs);
        w.Ok(new RespondToDemand { CivId = w.C.CivId, Demand = "DoNotSettleNearUs", Accept = true });
        Assert.Empty(w.A.PopupAlerts);
        Assert.Equal(before, w.Diplo(w.C, w.A).GetFlag(DiplomacyFlags.IgnoreThemSettlingNearUs));
    }

    [Fact]
    public void AnAiAnswersADemandInItsOwnTurn()
    {
        w.Diplo(w.A, w.C).DiplomaticStatus = DiplomaticStatus.Peace;
        w.Diplo(w.C, w.A).DiplomaticStatus = DiplomaticStatus.Peace;
        w.Ok(new MakeDemand { CivId = w.C.CivId, Demand = "DoNotSettleNearUs" });
        new TurnManager(w.C).AutomateTurn();
        Assert.Empty(w.C.PopupAlerts);
        var diplo = w.Diplo(w.A, w.C);
        Assert.True(diplo.HasFlag(DiplomacyFlags.AgreedToNotSettleNearUs) || w.Diplo(w.C, w.A).HasFlag(DiplomacyFlags.IgnoreThemSettlingNearUs)
            || diplo.HasFlag(DiplomacyFlags.IgnoreThemSettlingNearUs));
    }
}
