using System.Text.Json;
using Unciv.Commands.Diplomacy;
using Unciv.Core.Tests.Commands.Diplomacy;
using Unciv.Json;
using Unciv.Queries;
using Unciv.Queries.Empire;

namespace Unciv.Core.Tests.Queries;

// C021: the diplomacy buttons' legal set is the verdict of the commands they submit; the query is viewer-only, refuses civs the viewer has not met, and changes nothing.
[Collection("RulesetCache")]
public sealed class DiplomacyActionsQueryTests
{
    private readonly DiplomacyWorld w = new();
    private readonly QueryRegistry registry = new(UncivJson.DefaultOptions);

    public DiplomacyActionsQueryTests() => registry.RegisterFromAssembly(typeof(QueryRegistry).Assembly);

    private QueryResponse Execute(string viewer, string civ)
    {
        var envelope = registry.ToEnvelope(viewer, new DiplomacyActionsQuery(civ));
        var wire = JsonSerializer.Deserialize<QueryEnvelope>(JsonSerializer.Serialize(envelope, UncivJson.DefaultOptions), UncivJson.DefaultOptions)!;
        return registry.Execute(wire, new QueryContext(w.Game.GameInfo));
    }

    private DiplomacyActionsResult Ask(string civ, string? viewer = null)
    {
        ulong before = w.Hash();
        var response = Execute(viewer ?? w.A.CivId, civ);
        Assert.True(response.Ok, $"{response.Code}: {response.Message}");
        Assert.Equal(before, w.Hash());
        return response.Result!.Value.Deserialize<DiplomacyActionsResult>(UncivJson.DefaultOptions)!;
    }

    [Fact]
    public void MajorCivOffersWarDenounceAndNothingForCityStates()
    {
        var result = Ask(w.B.CivId);
        Assert.Equal(w.B.CivName, result.CivName);
        Assert.True(result.DeclareWar);
        Assert.True(result.Denounce);
        Assert.False(result.OfferPeace); // not at war
        Assert.False(result.NegotiateCityStatePeace);
        Assert.False(result.PledgeProtection);
        Assert.False(result.WithdrawProtection);
        Assert.False(result.TributeGold);
        Assert.False(result.TributeWorker);
        Assert.Empty(result.Gifts);
    }

    [Fact]
    public void AtWarWithAMajorCivWarAndDenounceCloseAndPeaceOpens()
    {
        w.MakeWar(w.A, w.B);
        var result = Ask(w.B.CivId);
        Assert.False(result.DeclareWar);
        Assert.False(result.Denounce);
        Assert.True(result.OfferPeace);
    }

    [Fact]
    public void ADeclarationOfWarOnTheSameTurnBlocksPeaceLikeTheCommand()
    {
        w.Ok(new Unciv.Commands.Diplomacy.DeclareWar { CivId = w.B.CivId });
        var result = Ask(w.B.CivId);
        var propose = new ProposeTrade { CivId = w.B.CivId, OurOffers = [new TradeOfferSpec(Constants.PeaceTreaty, "Treaty")] };
        Assert.Equal(propose.Validate(w.Game.GameInfo, w.A.CivId).IsValid, result.OfferPeace);
        Assert.False(result.DeclareWar);
    }

    [Fact]
    public void CityStateGiftsFollowTheGoldInTheTreasury()
    {
        w.A.AddGold(300 - w.A.Gold);
        Assert.Equal([250], Ask(w.S.CivId).Gifts);
        w.A.AddGold(1000 - w.A.Gold);
        Assert.Equal([250, 500, 1000], Ask(w.S.CivId).Gifts);
        w.A.AddGold(100 - w.A.Gold);
        Assert.Empty(Ask(w.S.CivId).Gifts);
        var cityState = Ask(w.S.CivId);
        Assert.False(cityState.Denounce);
        Assert.False(cityState.OfferPeace);
        Assert.True(cityState.DeclareWar);
    }

    [Fact]
    public void CityStateActionsAreTheCommandsVerdicts()
    {
        Assert.True(Ask(w.S.CivId).PledgeProtection);
        Assert.False(Ask(w.S.CivId).WithdrawProtection);
        Assert.False(Ask(w.S.CivId).TributeGold); // unwilling
        Assert.False(Ask(w.S.CivId).TributeWorker);
        Assert.False(Ask(w.S.CivId).NegotiateCityStatePeace); // not at war

        w.Game.Ruleset.ModOptions.Constants.TributeGlobalModifier = 1000;
        var willing = Ask(w.S.CivId);
        Assert.True(willing.TributeGold);
        Assert.True(willing.TributeWorker);

        w.Ok(new PledgeProtection { CivId = w.S.CivId });
        w.Diplo(w.S, w.A).RemoveFlag(Unciv.Logic.Civilization.Diplomacy.DiplomacyFlags.RecentlyPledgedProtection);
        var pledged = Ask(w.S.CivId);
        Assert.False(pledged.PledgeProtection);
        Assert.True(pledged.WithdrawProtection);

        w.MakeWar(w.A, w.S);
        var war = Ask(w.S.CivId);
        Assert.False(war.TributeGold);
        Assert.False(war.TributeWorker);
        Assert.Empty(war.Gifts);
        Assert.False(war.DeclareWar);
        Assert.True(war.NegotiateCityStatePeace);
    }

    [Fact]
    public void UnmetSelfBarbarianAndUnknownCivsAreRefusedAlike()
    {
        Assert.Equal(EmpireQueryCodes.UnknownCiv, Execute(w.A.CivId, w.A.CivId).Code);
        Assert.Equal(EmpireQueryCodes.UnknownCiv, Execute(w.A.CivId, "Nobody").Code);
        Assert.Equal(EmpireQueryCodes.UnknownCiv, Execute(w.A.CivId, "").Code);
        // C021 review: the barbarian refusal was behind an `if` that never ran; the barbarian is made to meet the viewer so only the barbarian guard can refuse it
        var barbarian = w.Game.AddBarbarianCiv();
        w.A.DiplomacyFunctions.MakeCivilizationsMeet(barbarian);
        Assert.Equal(EmpireQueryCodes.UnknownCiv, Execute(w.A.CivId, barbarian.CivId).Code);
        Assert.Equal(EmpireQueryCodes.UnknownCiv, Execute(w.A.CivId, Constants.Barbarians).Code);
    }

    [Fact]
    public void ACivTheViewerHasNotMetAnswersLikeANonexistentOne()
    {
        var stranger = w.Game.AddCiv();
        Assert.False(w.A.Knows(stranger));
        var unmet = Execute(w.A.CivId, stranger.CivId);
        var missing = Execute(w.A.CivId, "Nobody");
        Assert.False(unmet.Ok);
        Assert.Equal(missing.Code, unmet.Code);
        Assert.Equal(EmpireQueryCodes.UnknownCiv, unmet.Code);
        Assert.Equal(missing.Message.Replace("Nobody", "X", StringComparison.Ordinal), unmet.Message.Replace(stranger.CivId, "X", StringComparison.Ordinal));
        w.A.DiplomacyFunctions.MakeCivilizationsMeet(stranger);
        Assert.True(Execute(w.A.CivId, stranger.CivId).Ok);
    }

    [Fact]
    public void TheAnswerIsTheViewersOwnNotTheCurrentPlayers()
    {
        // B is a human but not the current player: its buttons are all closed, while A's (the current player) are open.
        Assert.True(Ask(w.C.CivId).DeclareWar);
        Assert.False(Ask(w.C.CivId, w.B.CivId).DeclareWar);
        w.MakeWar(w.B, w.C);
        Assert.False(Ask(w.C.CivId, w.B.CivId).OfferPeace); // at war, but not B's turn
        Assert.False(Ask(w.C.CivId).OfferPeace); // A is not at war with C
    }

    [Fact]
    public void BullyingGoldAndWorkerAreJudgedEachByItsOwnCommand()
    {
        bool sawGoldOnly = false;
        foreach (int modifier in Enumerable.Range(-20, 100).Select(it => it * 5).Append(1000))
        {
            w.Game.Ruleset.ModOptions.Constants.TributeGlobalModifier = modifier;
            var result = Ask(w.S.CivId);
            bool gold = new TributeGold { CivId = w.S.CivId }.Validate(w.Game.GameInfo, w.A.CivId).IsValid;
            bool worker = new TributeWorker { CivId = w.S.CivId }.Validate(w.Game.GameInfo, w.A.CivId).IsValid;
            Assert.Equal(gold, result.TributeGold);
            Assert.Equal(worker, result.TributeWorker);
            sawGoldOnly |= gold != worker;
        }
        Assert.True(sawGoldOnly, "no state separated the two flags: the test cannot tell them apart");
    }

    [Fact]
    public void QueryIsRegisteredAndRepeatable()
    {
        Assert.Contains("empire.DiplomacyActions", registry.RegisteredTypes);
        Assert.Equal(JsonSerializer.Serialize(Ask(w.S.CivId)), JsonSerializer.Serialize(Ask(w.S.CivId)));
    }

    [Fact]
    public void ProtectingACityStateShowsAsProtectorInThePanelAndTheSnapshotInEitherCivOrder()
    {
        string Panel(string viewer) => registryAsk<DiplomacyPanelResult>(viewer, new DiplomacyPanelQuery(w.S.CivName)).Status;
        Assert.Equal("Peace", Panel(w.A.CivId));
        w.Ok(new PledgeProtection { CivId = w.S.CivId });
        Assert.Equal("Protector", Panel(w.A.CivId));
        Assert.Equal("Peace", Panel(w.B.CivId)); // B did not pledge
        string StatusIn(string viewer) => Unciv.Net.Visibility.SnapshotBuilder.Build(w.Game.GameInfo, w.Game.GameInfo.GetCivilization(viewer), 0,
            new Unciv.Net.Visibility.TurnStatusDto { Ended = [], Phase = "Playing" }).Diplomacy
            .Single(it => (it.A == viewer && it.B == w.S.CivId) || (it.B == viewer && it.A == w.S.CivId)).Status!;
        Assert.Equal("Protector", StatusIn(w.A.CivId));
        Assert.Equal("Peace", StatusIn(w.B.CivId));
    }

    private T registryAsk<T>(string viewer, IQuery<T> query) where T : notnull
    {
        var envelope = registry.ToEnvelope(viewer, query);
        var response = registry.Execute(envelope, new QueryContext(w.Game.GameInfo));
        Assert.True(response.Ok, response.Message);
        return response.Result!.Value.Deserialize<T>(UncivJson.DefaultOptions)!;
    }
}
