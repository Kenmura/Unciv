using System.Reflection;
using Unciv.Commands;
using Unciv.Commands.Diplomacy;
using Unciv.Commands.Events;
using Unciv.Core.Tests.Commands.Diplomacy;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using Unciv.Logic.Trade;
using Unciv.Net.Visibility;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Core.Tests.Net.Visibility;

// T181: private channels (trade, espionage) on a 3-civ fixture: A and B negotiate, C is the third party. Secrets carry distinctive markers.
[Collection("RulesetCache")]
public sealed class NetPrivacyTests
{
    private const int SecretGold = 31337;
    private const string SecretSpyName = "Agent-Marker-Zq";

    private static readonly TurnStatusDto Status = new() { Ended = [], Deadline = null, Phase = "playing" };

    private readonly DiplomacyWorld w = new();

    // Sees and knows everything: the most permissive recipient. A private event must still not reach a third party.
    private sealed class OmniscientVisibility : IViewerVisibility
    {
        public bool CanSeeTile(HexCoord position) => true;

        public bool CanSeeUnit(string civId, int unitId, HexCoord position) => true;

        public bool KnowsCiv(string civId) => true;
    }

    private string Json(CivilizationObject civ) => SnapshotBuilder.ToCanonicalJson(SnapshotBuilder.Build(w.Game.GameInfo, civ, 1, Status));

    private ClientSnapshot Snap(CivilizationObject civ) => SnapshotBuilder.Build(w.Game.GameInfo, civ, 1, Status);

    private IReadOnlyList<EngineEvent> Deliver(IEnumerable<GameEvent> events, CivilizationObject recipient) =>
        events.OfType<EngineEvent>().SelectMany(it => EventFilter.Classify(it, recipient.CivId, new OmniscientVisibility())).ToList();

    private CommandResult Propose(int gold)
    {
        w.A.AddGold(gold + 100 - w.A.Gold);
        return w.Ok(new ProposeTrade { CivId = w.B.CivId, OurOffers = [new TradeOfferSpec(Constants.FlatGold, nameof(TradeOfferType.Gold), gold)] });
    }

    [Fact]
    public void ProposedTrade_IsDeliveredToTheRecipientOnly_EvenToAnOmniscientThirdParty()
    {
        var result = Propose(SecretGold);
        var requested = Assert.Single(result.Events.OfType<TradeRequested>());
        Assert.Equal(SecretGold, Assert.Single(requested.Offered).Amount);

        Assert.Same(requested, Assert.Single(Deliver(result.Events, w.B)));
        Assert.Empty(Deliver(result.Events, w.C));
        Assert.Empty(Deliver(result.Events, w.S));
        Assert.Empty(Deliver(result.Events, w.A)); // the proposer already knows its own offer
    }

    [Fact]
    public void ProposedTrade_IsOnlyInTheSnapshotsOfTheTwoParties()
    {
        Propose(SecretGold);
        Assert.Single(Snap(w.A).PendingTrades);
        Assert.Single(Snap(w.B).PendingTrades);
        foreach (var third in new[] { w.C, w.S })
        {
            Assert.Empty(Snap(third).PendingTrades);
            Assert.DoesNotContain(SecretGold.ToString(System.Globalization.CultureInfo.InvariantCulture), Json(third));
        }
    }

    [Fact]
    public void RetractedTrade_IsDeletedFromTheRecipientAndNeverTold_ToAThirdParty()
    {
        Propose(SecretGold);
        var result = w.Ok(new RetractTrade { CivId = w.B.CivId });
        var withdrawn = Assert.Single(result.Events.OfType<TradeWithdrawn>());
        Assert.Same(withdrawn, Assert.Single(Deliver(result.Events, w.B)));
        Assert.Empty(Deliver(result.Events, w.C));
        Assert.Empty(Snap(w.B).PendingTrades);
    }

    [Fact]
    public void AcceptedTrade_IsNeverDeliveredToAThirdParty()
    {
        var accepted = new TradeAccepted(w.A.CivId, w.B.CivId, [new TradeOfferFact("Gold", TradeOfferType.Gold, SecretGold, 0)], []);
        Assert.Empty(Deliver([accepted], w.C));
        Assert.Single(Deliver([accepted], w.A));
        Assert.Single(Deliver([accepted], w.B));
    }

    [Fact]
    public void Spies_AreInTheOwnersSnapshotOnly()
    {
        var spy = w.A.EspionageManager.AddSpy();
        typeof(Unciv.Models.Spy).GetField("name", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(spy, SecretSpyName);

        var own = Assert.Single(Assert.IsType<OwnCivDto>(Snap(w.A).Civs.Single(it => it.CivId == w.A.CivId).Own).Spies);
        Assert.Equal(SecretSpyName, own.Name);
        Assert.Null(own.Location); // hideout

        foreach (var other in new[] { w.B, w.C, w.S })
        {
            Assert.DoesNotContain(SecretSpyName, Json(other));
            Assert.All(Snap(other).Civs, it => Assert.True(it.Own == null || it.CivId == other.CivId));
            Assert.Empty(Assert.IsType<OwnCivDto>(Snap(other).Civs.Single(it => it.CivId == other.CivId).Own).Spies);
        }
        Assert.DoesNotContain(SecretSpyName, Json(w.B));
    }

    [Fact]
    public void SpyStationedInAForeignCity_IsNotRevealedToThatCityOwner()
    {
        var spy = w.A.EspionageManager.AddSpy();
        var cityOfC = w.C.Cities.First();
        spy.MoveTo(cityOfC);
        var ownDto = Assert.Single(Assert.IsType<OwnCivDto>(Snap(w.A).Civs.Single(it => it.CivId == w.A.CivId).Own).Spies);
        Assert.Equal(cityOfC.Name, ownDto.Location);

        var forC = Snap(w.C);
        Assert.Empty(Assert.IsType<OwnCivDto>(forC.Civs.Single(it => it.CivId == w.C.CivId).Own).Spies);
        Assert.DoesNotContain(spy.Name, Json(w.C));
    }

    [Fact]
    public void StealResultNotification_IsOnlyForTheOwner()
    {
        var steal = new NotificationAdded(w.A.CivId, "Your spy stole Pottery", Notification.NotificationCategory.Espionage, [], []);
        Assert.Single(Deliver([steal], w.A));
        Assert.Empty(Deliver([steal], w.B));
        Assert.Empty(Deliver([steal], w.C));
    }

    [Fact]
    public void EveryPrivateEventTypeIsParticipantOnly_ForEveryOtherRecipient()
    {
        foreach (var gameEvent in new EngineEvent[]
        {
            new TradeRequested("A", "B", [], []), new TradeWithdrawn("A", "B"), new TradeAccepted("A", "B", [], []),
            new NotificationAdded("A", "x", Notification.NotificationCategory.Espionage, [], []), new AlertAdded("A", AlertType.WarDeclaration, "x"), new SoundRequested("A", "click")
        })
        {
            Assert.Empty(EventFilter.Classify(gameEvent, "C", new OmniscientVisibility()));
        }
    }

    [Fact]
    public void AiWillingnessAndValuationNumbers_AreNeverPartOfAnyWireType()
    {
        var forbidden = new[] { "Willing", "Evaluat", "Valuation", "Worth", "Desirab", "Acceptab" };
        var wireTypes = typeof(ClientSnapshot).Assembly.GetTypes()
            .Where(it => it.Namespace == "Unciv.Net.Visibility" || typeof(EngineEvent).IsAssignableFrom(it) || it == typeof(TradeOfferFact))
            .ToList();
        Assert.NotEmpty(wireTypes);
        foreach (var type in wireTypes)
        {
            foreach (var member in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                Assert.DoesNotContain(forbidden, word => member.Name.Contains(word, StringComparison.OrdinalIgnoreCase));
                Assert.DoesNotContain(forbidden, word => member.PropertyType.Name.Contains(word, StringComparison.OrdinalIgnoreCase));
            }
        }
    }

    [Fact]
    public void TradeEvents_SerializeOnlyOfferFacts()
    {
        var result = Propose(SecretGold);
        var envelope = GameEventRegistry.ToEnvelope(result.Events.OfType<TradeRequested>().Single());
        var names = envelope.Data.EnumerateObject().Select(it => it.Name).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(["FromCivId", "Offered", "Requested", "ToCivId"], names);
    }
}
