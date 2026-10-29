using System.Text.Json;
using Unciv.Commands;
using Unciv.Commands.Events;
using Unciv.Core.Tests.Support;
using Unciv.Json;
using Unciv.Logic;
using Unciv.Logic.Battle;
using Unciv.Logic.Civilization;
using Unciv.Logic.Files;
using Unciv.Logic.Map;
using Unciv.Logic.Trade;
using Unciv.Models;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Unique;
using Unciv.Net.Visibility;
using Unciv.Utils;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;
using NotificationCategory = Unciv.Logic.Civilization.Notification.NotificationCategory;
using RulesetClass = Unciv.Models.Ruleset.Ruleset;

namespace Unciv.Core.Tests.Commands;

// T186: event registry and JSON, the sink in the engine, the common events and their call sites.
[Collection("RulesetCache")]
public sealed class GameEventsTests
{
    private static readonly HexCoord P1 = new(1, 2);
    private static readonly HexCoord P2 = new(2, 2);

    // One sample per event class: every [EventType] class must appear here (a new domain ticket adds its own line).
    private static EngineEvent Sample(string type) => type switch
    {
        "turn-started" => new TurnStarted("Babylon", 3),
        "turn-ended" => new TurnEnded("Babylon", 3),
        "auto-player-turn-processed" => new AutoPlayerTurnProcessed("Rome", 3),
        "player-ended-turn" => new PlayerEndedTurn("Babylon", 3),
        "player-undid-end-turn" => new PlayerUndidEndTurn("Babylon", 3),
        "turn-resolving" => new TurnResolving(3),
        "controller-changed" => new ControllerChanged("Greece", true),
        "decision-auto-resolved" => new DecisionAutoResolved("Babylon", "Technology", "", "research"),
        "events-truncated" => new EventsTruncated(7),
        "dig-resolved" => new DigResolved("Babylon", 3, 4, "FindAncient", true),
        "great-work-created" => new GreatWorkCreated("Babylon", "ArtAncient", "Art", "city-1", "Museum", 0),
        "great-work-moved" => new GreatWorkMoved("Babylon", "ArtAncient", "city-1", "Museum", 0, "city-2", "Museum", 1),
        "great-work-swapped" => new GreatWorkSwapped("Babylon", "Greece", "ArtAncient", "ArtClassical"),
        "unit-moved" => new UnitMoved("Babylon", 12, "Warrior", [P1, P2]),
        "unit-attacked" => new UnitAttacked("Babylon", 12, null, P1, "Rome", null, "city-1", P2, 31, 10, false, true),
        "unit-created" => new UnitCreated("Babylon", 12, "Warrior", P1),
        "unit-destroyed" => new UnitDestroyed("Babylon", 12, "Warrior", P1),
        "city-founded" => new CityFounded("Babylon", "city-1", "Babylon", P1),
        "city-captured" => new CityCaptured("city-1", "Ur", P1, "Rome", "Babylon", "Rome"),
        "unit-captured" => new UnitCaptured(12, "Worker", P1, "Rome", "Babylon"),
        "city-destroyed" => new CityDestroyed("city-1", "Ur", P1, "Babylon"),
        "city-traded" => new CityTraded("city-1", "Ur", P1, "Babylon", "Rome"),
        "city-grew" => new CityGrew("Babylon", "city-1", P1, 4),
        "tile-owner-changed" => new TileOwnerChanged(P1, "", "Babylon"),
        "notification-added" => new NotificationAdded("Babylon", "Hello [x]", NotificationCategory.War, ["War"], [P1]),
        "alert-added" => new AlertAdded("Babylon", AlertType.WonderBuilt, "Pyramids"),
        "sound-requested" => new SoundRequested("Babylon", "click"),
        "research-completed" => new ResearchCompleted("Babylon", "Agriculture"),
        "policy-adopted" => new PolicyAdopted("Babylon", "Tradition"),
        "war-declared" => new WarDeclared("Babylon", "Rome"),
        "peace-made" => new PeaceMade("Babylon", "Rome"),
        "trade-accepted" => new TradeAccepted("Babylon", "Rome",
            [new TradeOfferFact("Gold", TradeOfferType.Gold, 50, -1)], [new TradeOfferFact("Agriculture", TradeOfferType.Technology, 1, -1)]),
        "trade-requested" => new TradeRequested("Babylon", "Rome", [new TradeOfferFact("Gold", TradeOfferType.Gold, 50, -1)], []),
        "trade-withdrawn" => new TradeWithdrawn("Babylon", "Rome"),
        "unit-appeared" => new UnitAppeared("Babylon", 12, "Warrior", P1),
        "unit-disappeared" => new UnitDisappeared("Babylon", 12, P1),
        "stats-changed" => new StatsChanged("Babylon", 100, 5),
        "tiles-revealed" => new TilesRevealed("Babylon",
            [new TileDto { X = 1, Y = 2, Visible = false, BaseTerrain = "Grassland", Features = [], RiverBottom = false, RiverBottomLeft = false, RiverBottomRight = false }], []),
        "trade-route-established" => new TradeRouteEstablished(1, "Babylon", "Rome", "city-1", "city-2", TradeDomain.Sea, TradeRouteKind.International),
        "trade-route-completed" => new TradeRouteCompleted(1, "Babylon", "Rome", "city-1", "city-2"),
        "trade-route-plundered" => new TradeRoutePlundered(1, "Babylon", "Rome", "Greece", P1, 120),
        "trade-route-broken" => new TradeRouteBroken(1, "Babylon", "Babylon", "city-1", "city-2"),
        _ => throw new InvalidOperationException($"No sample for event type \"{type}\"; add one to GameEventsTests.Sample"),
    };

    private static string Json(params GameEvent[] events) =>
        string.Join("\n", events.Select(it => Json(GameEventRegistry.ToEnvelope((EngineEvent)it))));

    private static string Json(GameEventEnvelope envelope) => JsonSerializer.Serialize(envelope, UncivJson.DefaultOptions);

    [Fact]
    public void EveryScannedEventHasAUniqueKebabCaseNameAnAudienceAndRoundTripsThroughJson()
    {
        var scanned = GameEventRegistry.Scan();
        Assert.Equal(scanned.Count, scanned.Select(it => it.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(scanned.Select(it => it.Name).OrderBy(it => it, StringComparer.Ordinal), scanned.Select(it => it.Name));
        Assert.True(scanned.Count >= 20);

        foreach (var (name, eventClass) in scanned)
        {
            var sample = Sample(name);
            Assert.Equal(eventClass, sample.GetType());
            Assert.Equal(name, sample.Type);

            var envelope = GameEventRegistry.ToEnvelope(sample);
            string wire = Json(envelope);
            var back = JsonSerializer.Deserialize<GameEventEnvelope>(wire, UncivJson.DefaultOptions)!;
            var decoded = GameEventRegistry.FromEnvelope(back);

            Assert.Equal(name, back.Type);
            Assert.Equal(eventClass, decoded.GetType());
            Assert.Equal(wire, Json(GameEventRegistry.ToEnvelope(decoded)));

            // The data the classifier needs is present for every audience class.
            var audience = EventTypeAttribute.Of(eventClass).Audience;
            if (audience != EventAudience.Everyone) Assert.Contains(decoded.Participants(), it => it != "");
            if (audience == EventAudience.Located) Assert.NotEmpty(decoded.Positions());
            if (audience == EventAudience.Everyone)
            {
                Assert.Empty(decoded.Participants());
                Assert.Empty(decoded.Positions());
            }
        }
    }

    [Fact]
    public void JsonUsesDeclarationOrderAndInvariantFormatting()
    {
        var wire = Json(GameEventRegistry.ToEnvelope(new UnitMoved("Babylon", 12, "Warrior", [P1, P2])));
        Assert.Equal("{\"Type\":\"unit-moved\",\"Data\":{\"CivId\":\"Babylon\",\"UnitId\":12,\"UnitName\":\"Warrior\",\"Path\":[{\"x\":1,\"y\":2},{\"x\":2,\"y\":2}]}}", wire);
    }

    [Fact]
    public void UntrustedEnvelopesAreReportedNotThrown()
    {
        using var doc = JsonDocument.Parse("""{"CivId": 5, "Turn": "x"}""");
        using var array = JsonDocument.Parse("[]");
        using var empty = JsonDocument.Parse("null");

        Assert.False(GameEventRegistry.TryFromEnvelope(new GameEventEnvelope("no-such-event", doc.RootElement), out _, out string error));
        Assert.Contains("Unknown event type", error);
        Assert.False(GameEventRegistry.TryFromEnvelope(new GameEventEnvelope("turn-started", array.RootElement), out _, out error));
        Assert.Contains("json object", error);
        Assert.False(GameEventRegistry.TryFromEnvelope(new GameEventEnvelope("turn-started", doc.RootElement), out _, out error));
        Assert.Contains("invalid", error);
        Assert.False(GameEventRegistry.TryFromEnvelope(new GameEventEnvelope("turn-started", empty.RootElement), out _, out error));
        Assert.Throws<InvalidOperationException>(() => GameEventRegistry.FromEnvelope(new GameEventEnvelope("no-such-event", doc.RootElement)));
    }

    private sealed record Unregistered : EngineEvent;

    [EventType("Not Kebab", EventAudience.Everyone)]
    private sealed record BadName : EngineEvent;

    [Fact]
    public void ScanRejectsMissingAttributesAndBadNames()
    {
        Assert.Throws<InvalidOperationException>(() => EventTypeAttribute.NameOf(typeof(Unregistered)));
        Assert.Throws<InvalidOperationException>(() => GameEventRegistry.ToEnvelope(new Unregistered()));
        Assert.Throws<InvalidOperationException>(() => GameEventRegistry.Scan(typeof(GameEventsTests).Assembly));
    }

    [Fact]
    public void SinkKeepsCallOrderIsBoundedAndDrains()
    {
        var sink = new GameInfoEventSink(2);
        sink.Emit(new TurnEnded("A", 1));
        sink.Emit(new TurnStarted("B", 1));
        sink.Emit(new TurnStarted("C", 1));
        sink.Emit(new TurnStarted("D", 1));
        Assert.Equal(2, sink.Count);
        Assert.Equal(2, sink.Dropped);

        var events = sink.Drain();
        Assert.Equal([new TurnEnded("A", 1), new TurnStarted("B", 1), new EventsTruncated(2)], events);
        Assert.Equal(0, sink.Count);
        Assert.Empty(sink.Drain());
        Assert.Equal(GameInfoEventSink.DefaultMaxEvents, new GameInfoEventSink().GetType().GetField("DefaultMaxEvents")!.GetRawConstantValue());
    }

    [Fact]
    public void FullSinkLetsEssentialEventsDisplaceTheNewestNonEssentialOnes()
    {
        var sink = new GameInfoEventSink(3);
        sink.Emit(new AlertAdded("A", AlertType.WonderBuilt, "1"));
        sink.Emit(new AlertAdded("A", AlertType.WonderBuilt, "2"));
        sink.Emit(new TurnStarted("A", 1));
        sink.Emit(new AlertAdded("A", AlertType.WonderBuilt, "3")); // dropped
        sink.Emit(new TurnEnded("A", 1)); // displaces alert 2
        sink.Emit(new TurnEnded("B", 1)); // displaces alert 1
        sink.Emit(new TurnEnded("C", 1)); // nothing left to displace
        Assert.Equal([new TurnStarted("A", 1), new TurnEnded("A", 1), new TurnEnded("B", 1), new EventsTruncated(4)], sink.Drain());
    }

    // --- engine call sites, on the small TestGame ---

    private readonly TestGame testGame = new();

    private (GameInfoEventSink Sink, CivilizationObject A, CivilizationObject B) Arena()
    {
        testGame.MakeHexagonalMap(4);
        var a = testGame.AddCiv(isPlayer: true);
        var b = testGame.AddCiv(isPlayer: true);
        a.DiplomacyFunctions.MakeCivilizationsMeet(b);
        testGame.AddUnit("Warrior", a, testGame.GetTile(-3, 0));
        testGame.AddUnit("Warrior", b, testGame.GetTile(3, 0));
        var sink = new GameInfoEventSink();
        testGame.GameInfo.EventSink = sink;
        return (sink, a, b);
    }

    private static List<T> Of<T>(GameInfoEventSink sink) where T : EngineEvent => sink.Drain().OfType<T>().ToList();

    [Fact]
    public void NoSinkMeansNoEventsAndTheDefaultIsNull()
    {
        Assert.Null(new GameInfo().EventSink);
        testGame.MakeHexagonalMap(2);
        var civ = testGame.AddCiv();
        civ.AddNotification("text", NotificationCategory.General);
        civ.AddPopupAlert(new PopupAlert(AlertType.StartIntro, ""));
        Assert.Contains(civ.PopupAlerts, it => it.Type == AlertType.StartIntro);
        Assert.Null(testGame.GameInfo.EventSink);
    }

    [Fact]
    public void NotificationsAndAlertsAreReportedPerCivAndAiCivsAreIgnored()
    {
        var (sink, a, _) = Arena();
        var ai = testGame.AddCiv();
        ai.PlayerType = PlayerType.AI;
        sink.Drain();

        a.AddNotification("Hello", P1, NotificationCategory.War, "War", "Gold");
        ai.AddNotification("Nobody reads this", NotificationCategory.General);
        a.AddPopupAlert(new PopupAlert(AlertType.WonderBuilt, "Pyramids"));
        ai.AddPopupAlert(new PopupAlert(AlertType.WonderBuilt, "Pyramids"));

        var events = sink.Drain();
        var notification = Assert.IsType<NotificationAdded>(events[0]);
        Assert.Equal((a.CivId, "Hello", NotificationCategory.War), (notification.CivId, notification.Text, notification.Category));
        Assert.Equal(["War", "Gold"], notification.Icons);
        Assert.Equal([P1], notification.Locations);
        Assert.Equal<GameEvent>(
            [new AlertAdded(a.CivId, AlertType.WonderBuilt, "Pyramids"), new AlertAdded(ai.CivId, AlertType.WonderBuilt, "Pyramids")],
            events.Skip(1).ToList());
        Assert.Equal(2, events.Count - 1);
        Assert.Contains(a.Notifications, it => it.Text == "Hello");
        Assert.DoesNotContain(ai.Notifications, it => it.Text == "Nobody reads this");
        Assert.Empty(ai.Notifications);
    }

    [Fact]
    public void NotificationLocationsComeInActionOrder()
    {
        var (sink, a, _) = Arena();
        sink.Drain();
        a.AddNotification("Many", [new LocationAction(P2), new CityAction(P1), new MapUnitAction(P2, 5), new PromoteUnitAction("W", P1, 5), new TechAction("x")], NotificationCategory.General);
        var added = Assert.Single(Of<NotificationAdded>(sink));
        Assert.Equal([P2, P1, P2, P1], added.Locations);
    }

    [Fact]
    public void CityFoundingReportsTheCityAndItsBorders()
    {
        var (sink, a, _) = Arena();
        sink.Drain();
        var city = testGame.AddCity(a, testGame.GetTile(0, 0));

        var events = sink.Drain();
        var founded = Assert.Single(events.OfType<CityFounded>());
        Assert.Equal(new CityFounded(a.CivId, city.Id, city.Name, HexCoord.Zero), founded);
        Assert.Contains(events.OfType<TileOwnerChanged>(), it => it.NewOwnerCivId == a.CivId && it.OldOwnerCivId == "");
    }

    [Fact]
    public void UnitsReportCreationMovementAndDestruction()
    {
        var (sink, a, _) = Arena();
        var start = testGame.GetTile(-3, 0);
        sink.Drain();

        var created = a.Units.PlaceUnitNearTile(start.Position, "Warrior")!;
        var createdEvent = Assert.Single(Of<UnitCreated>(sink));
        Assert.Equal(new UnitCreated(a.CivId, created.Id, "Warrior", created.CurrentTile.Position), createdEvent);

        created.CurrentMovement = 2f;
        var from = created.CurrentTile;
        var target = testGame.GetTile(from.Position.x + 2, from.Position.y);
        created.Movement.MoveToTile(target);
        var moved = Assert.Single(Of<UnitMoved>(sink));
        Assert.Equal(a.CivId, moved.CivId);
        Assert.Equal(from.Position, moved.Path[0]);
        Assert.Equal(created.CurrentTile.Position, moved.Path[^1]);
        Assert.True(moved.Path.Count >= 2);

        created.Movement.MoveToTile(created.CurrentTile); // no movement, no event
        Assert.Empty(Of<UnitMoved>(sink));

        var position = created.CurrentTile.Position;
        created.Destroy();
        created.Destroy(); // already destroyed
        Assert.Equal(new UnitDestroyed(a.CivId, created.Id, "Warrior", position), Assert.Single(Of<UnitDestroyed>(sink)));
    }

    [Fact]
    public void AirAndParadropMovesReportATwoStepPath()
    {
        var (sink, a, _) = Arena();
        testGame.AddCity(a, testGame.GetTile(-3, 1));
        testGame.AddCity(a, testGame.GetTile(-2, 1));
        var fighter = testGame.AddUnit("Fighter", a, testGame.GetTile(-3, 1));
        fighter.CurrentMovement = 4f;
        sink.Drain();
        fighter.Movement.MoveToTile(testGame.GetTile(-2, 1));
        var moved = Assert.Single(Of<UnitMoved>(sink));
        Assert.Equal([new HexCoord(-3, 1), new HexCoord(-2, 1)], moved.Path);

        var paratrooper = testGame.AddUnit("Paratrooper", a, testGame.GetTile(-2, 0));
        paratrooper.CurrentMovement = 2f;
        paratrooper.Action = UnitActionType.Paradrop.Value();
        sink.Drain();
        paratrooper.Movement.MoveToTile(testGame.GetTile(-2, 2));
        moved = Assert.Single(Of<UnitMoved>(sink));
        Assert.Equal([new HexCoord(-2, 0), new HexCoord(-2, 2)], moved.Path);
    }

    [Fact]
    public void CombatReportsBothSidesDamageAndKills()
    {
        var (sink, a, b) = Arena();
        a.DiplomacyFunctions.MakeCivilizationsMeet(b);
        a.GetDiplomacyManager(b)!.DeclareWar();
        var attacker = testGame.AddUnit("Warrior", a, testGame.GetTile(1, 0));
        var defender = testGame.AddUnit("Warrior", b, testGame.GetTile(HexCoord.Zero));
        attacker.CurrentMovement = 2f;
        defender.CurrentMovement = 2f;
        sink.Drain();

        var damage = Battle.Attack(new MapUnitCombatant(attacker), new MapUnitCombatant(defender));

        var attacked = Assert.Single(Of<UnitAttacked>(sink));
        Assert.Equal(new UnitAttacked(a.CivId, attacker.Id, null, new HexCoord(1, 0), b.CivId, defender.Id, null, HexCoord.Zero,
            damage.AttackerDealt, damage.DefenderDealt, false, false), attacked);
        Assert.Equal([a.CivId, b.CivId], attacked.Participants());
    }

    [Fact]
    public void CapturingACivilianKeepsTheVictimAsDefender()
    {
        var (sink, a, b) = Arena();
        a.GetDiplomacyManager(b)!.DeclareWar();
        var attacker = testGame.AddUnit("Warrior", a, testGame.GetTile(1, 0));
        var worker = testGame.AddUnit("Worker", b, testGame.GetTile(HexCoord.Zero));
        attacker.CurrentMovement = 2f;
        sink.Drain();

        Battle.Attack(new MapUnitCombatant(attacker), new MapUnitCombatant(worker));

        var attacked = Assert.Single(Of<UnitAttacked>(sink));
        Assert.Equal(b.CivId, attacked.DefenderCivId);
        Assert.Equal(worker.Id, attacked.DefenderUnitId);
        Assert.Contains(b.CivId, attacked.Participants());
    }

    [Fact]
    public void UnitsPlacedThroughTheTileMapAreReportedAsCreated()
    {
        var (sink, a, _) = Arena();
        sink.Drain();
        var unit = (Unciv.Logic.Map.MapUnits.MapUnit)testGame.TileMap.PlaceUnitNearTile(new HexCoord(-3, 0), "Warrior", a)!;
        Assert.Equal(new UnitCreated(a.CivId, unit.Id, "Warrior", unit.CurrentTile.Position), Assert.Single(Of<UnitCreated>(sink)));
    }

    [Fact]
    public void SpectatorsGetNoTurnEndedWithoutTurnStarted()
    {
        var (sink, _, _) = Arena();
        var spectator = testGame.AddCiv(new Unciv.Models.Ruleset.Nation.Nation { Name = Constants.Spectator }, isPlayer: true);
        Assert.True(spectator.IsSpectator());
        sink.Drain();
        new Unciv.Logic.Civilization.Managers.TurnManager(spectator).StartTurn();
        new Unciv.Logic.Civilization.Managers.TurnManager(spectator).EndTurn();
        Assert.Empty(sink.Drain().OfType<TurnEnded>());
    }

    [Fact]
    public void CityTransferReportsEveryTileOwnerInPositionOrder()
    {
        var (sink, a, b) = Arena();
        testGame.AddUnit("Warrior", b, testGame.GetTile(2, 2));
        testGame.GameInfo.CurrentPlayerCiv = a;
        testGame.GameInfo.CurrentPlayer = a.CivId;
        var city = testGame.AddCity(b, testGame.GetTile(HexCoord.Zero), initialPopulation: 2);
        testGame.AddCity(b, testGame.GetTile(2, 1));
        sink.Drain();

        city.MoveToCiv(a);

        var owners = Of<TileOwnerChanged>(sink).Where(it => it.OldOwnerCivId == b.CivId && it.NewOwnerCivId == a.CivId).Select(it => it.Position).ToList();
        Assert.NotEmpty(owners);
        Assert.Equal(owners.OrderBy(it => it.x).ThenBy(it => it.y), owners);
    }

    [Fact]
    public void CapturedUnitsDestroyedCitiesAndTradedCitiesAreReported()
    {
        var (sink, a, b) = Arena();
        var worker = testGame.AddUnit("Worker", b, testGame.GetTile(HexCoord.Zero));
        sink.Drain();
        worker.CapturedBy(a);
        Assert.Equal(new UnitCaptured(worker.Id, "Worker", HexCoord.Zero, a.CivId, b.CivId), Assert.Single(Of<UnitCaptured>(sink)));

        testGame.GameInfo.CurrentPlayerCiv = a;
        testGame.GameInfo.CurrentPlayer = a.CivId;
        testGame.AddCity(b, testGame.GetTile(2, 1));
        var doomed = testGame.AddCity(b, testGame.GetTile(HexCoord.Zero), initialPopulation: 2);
        sink.Drain();
        doomed.DestroyCity(overrideSafeties: true);
        Assert.Equal(new CityDestroyed(doomed.Id, doomed.Name, HexCoord.Zero, b.CivId), Assert.Single(Of<CityDestroyed>(sink)));

        var sold = testGame.AddCity(b, testGame.GetTile(-1, 2), initialPopulation: 2);
        var trade = new TradeLogic(b, a);
        trade.CurrentTrade.OurOffers.Add(new TradeOffer(sold.Id, TradeOfferType.City));
        sink.Drain();
        trade.AcceptTrade();
        var traded = Assert.Single(Of<CityTraded>(sink));
        Assert.Equal(new CityTraded(sold.Id, sold.Name, new HexCoord(-1, 2), b.CivId, a.CivId), traded);
    }

    [Fact]
    public void CargoTeleportedWithItsCarrierReportsItsOwnMove()
    {
        var (sink, a, _) = Arena();
        var fighterBase = testGame.CreateBaseUnit("Fighter", "MyFighters", "MyAircraft");
        var carrierBase = testGame.CreateBaseUnit("Aircraft Carrier", "Can carry [1] [MyFighters] units");
        carrierBase.Strength = 10;
        var origin = testGame.GetTile(-1, 1);
        var destination = testGame.GetTile(-1, 2);
        foreach (var water in new[] { origin, destination })
        {
            water.BaseTerrain = Constants.Coast;
            water.SetTerrainTransients();
        }
        var carrier = testGame.AddUnit(carrierBase.Name, a, origin);
        var fighter = testGame.AddUnit(fighterBase.Name, a, origin);
        Assert.True(fighter.IsTransported);
        sink.Drain();

        carrier.RemoveFromTile();
        carrier.PutInTile(destination);
        carrier.Movement.TeleportTransportedUnitsTo(origin, destination);

        var moved = Assert.Single(Of<UnitMoved>(sink));
        Assert.Equal(fighter.Id, moved.UnitId);
        Assert.Equal([origin.Position, destination.Position], moved.Path);
    }

    [Fact]
    public void CargoMovesWithItsCarrierAlongThePath()
    {
        var (sink, a, _) = Arena();
        var fighterBase = testGame.CreateBaseUnit("Fighter", "MyFighters", "MyAircraft");
        var carrierBase = testGame.CreateBaseUnit("Aircraft Carrier", "Can carry [1] [MyFighters] units");
        carrierBase.Strength = 10;
        carrierBase.Movement = 3;
        var origin = testGame.GetTile(-1, 1);
        var destination = testGame.GetTile(-1, 3);
        foreach (var water in new[] { origin, testGame.GetTile(-1, 2), destination })
        {
            water.BaseTerrain = Constants.Coast;
            water.SetTerrainTransients();
        }
        var carrier = testGame.AddUnit(carrierBase.Name, a, origin);
        var fighter = testGame.AddUnit(fighterBase.Name, a, origin);
        carrier.CurrentMovement = 3f;
        sink.Drain();

        carrier.Movement.MoveToTile(destination);

        var moves = Of<UnitMoved>(sink);
        Assert.Equal([carrier.Id, fighter.Id], moves.Select(it => it.UnitId));
        Assert.All(moves, it => Assert.Equal([origin.Position, carrier.CurrentTile.Position], [it.Path[0], it.Path[^1]]));
    }

    [Fact]
    public void OnlyTurnCityAndDiplomacyFactsAreEssential()
    {
        var essential = GameEventRegistry.Scan().Where(it => Sample(it.Name).IsEssential()).Select(it => it.Name).ToList();
        Assert.Equal(
        [
            "city-captured", "city-destroyed", "city-founded", "city-traded", "peace-made", "player-ended-turn", "player-undid-end-turn",
            "policy-adopted", "research-completed", "trade-accepted", "turn-ended", "turn-resolving", "turn-started", "war-declared",
        ], essential);
    }

    [Fact]
    public void DisplacedUnitsReportATeleportMove()
    {
        var (sink, a, b) = Arena();
        var unit = testGame.AddUnit("Warrior", b, testGame.GetTile(1, 0));
        var city = testGame.AddCity(a, testGame.GetTile(HexCoord.Zero), initialPopulation: 2);
        unit.GetTile().SetOwningCity(city);
        Assert.Equal(a, unit.GetTile().GetOwner());
        var start = unit.GetTile().Position;
        sink.Drain();
        unit.Movement.TeleportToClosestMoveableTile();
        var moved = Assert.Single(Of<UnitMoved>(sink));
        Assert.Equal(b.CivId, moved.CivId);
        Assert.Equal([start, unit.CurrentTile.Position], moved.Path);
    }

    [Fact]
    public void MeleeWithdrawalReportsTheDefendersMove()
    {
        var (sink, a, b) = Arena();
        var attacker = testGame.AddUnit("Warrior", a, testGame.GetTile(1, 0));
        attacker.CurrentMovement = 2f;
        var defender = testGame.AddDefaultMeleeUnitWithUniques(b, testGame.GetTile(0, 1), UniqueType.WithdrawsBeforeMeleeCombat.Text());
        defender.CurrentMovement = 2f;
        var start = defender.GetTile().Position;
        sink.Drain();
        Battle.Attack(new MapUnitCombatant(attacker), new MapUnitCombatant(defender));
        var moved = Assert.Single(Of<UnitMoved>(sink));
        Assert.Equal(b.CivId, moved.CivId);
        Assert.Equal(defender.Id, moved.UnitId);
        Assert.Equal([start, defender.CurrentTile.Position], moved.Path);
        Assert.NotEqual(start, defender.CurrentTile.Position);
    }

    [Fact]
    public void CityAttacksReportTheCityIds()
    {
        var (sink, a, b) = Arena();
        a.GetDiplomacyManager(b)!.DeclareWar();
        var city = testGame.AddCity(b, testGame.GetTile(HexCoord.Zero));
        var attacker = testGame.AddUnit("Archer", a, testGame.GetTile(1, 0));
        attacker.CurrentMovement = 2f;
        sink.Drain();

        Battle.Attack(new MapUnitCombatant(attacker), new CityCombatant(city));
        var attacked = Assert.Single(Of<UnitAttacked>(sink));
        Assert.Null(attacked.AttackerCityId);
        Assert.Equal(city.Id, attacked.DefenderCityId);
        Assert.Null(attacked.DefenderUnitId);
    }

    [Fact]
    public void CityGrowthCaptureAndTileLossAreReported()
    {
        var (sink, a, b) = Arena();
        testGame.AddUnit("Warrior", b, testGame.GetTile(2, 2));
        testGame.GameInfo.CurrentPlayerCiv = a;
        testGame.GameInfo.CurrentPlayer = a.CivId;
        var city = testGame.AddCity(b, testGame.GetTile(HexCoord.Zero), initialPopulation: 2);
        testGame.AddCity(b, testGame.GetTile(2, 1));
        sink.Drain();

        city.Population.FoodStored = 10_000;
        city.Population.NextTurn(0);
        var grew = Assert.Single(Of<CityGrew>(sink));
        Assert.Equal(new CityGrew(b.CivId, city.Id, HexCoord.Zero, city.Population.Population), grew);

        city.PuppetCity(a);
        var events = sink.Drain();
        var captured = Assert.Single(events.OfType<CityCaptured>());
        Assert.Equal(new CityCaptured(city.Id, city.Name, HexCoord.Zero, a.CivId, b.CivId, a.CivId), captured);
        Assert.Equal([a.CivId, b.CivId, a.CivId], captured.Participants());
    }

    [Fact]
    public void ResearchPolicyWarPeaceAndTradeAreReported()
    {
        var (sink, a, b) = Arena();
        sink.Drain();

        string tech = testGame.Ruleset.Technologies.Keys.First(it => !a.Tech.IsResearched(it));
        a.Tech.AddTechnology(tech);
        a.Tech.AddTechnology(tech); // not new: no second event
        Assert.Equal(new ResearchCompleted(a.CivId, tech), Assert.Single(Of<ResearchCompleted>(sink)));

        var policy = testGame.CreatePolicyBranch();
        a.Policies.FreePolicies = 1;
        a.Policies.Adopt(policy);
        Assert.Equal(new PolicyAdopted(a.CivId, policy.Name), Of<PolicyAdopted>(sink)[0]); // the empty branch also completes

        a.GetDiplomacyManager(b)!.DeclareWar();
        var declared = Of<WarDeclared>(sink);
        Assert.Equal(new WarDeclared(a.CivId, b.CivId), Assert.Single(declared));

        a.GetDiplomacyManager(b)!.MakePeace();
        Assert.Equal(new PeaceMade(a.CivId, b.CivId), Assert.Single(Of<PeaceMade>(sink)));

        a.AddGold(500);
        var trade = new TradeLogic(a, b);
        trade.CurrentTrade.OurOffers.Add(new TradeOffer("Gold", TradeOfferType.Gold, 50, -1));
        trade.CurrentTrade.TheirOffers.Add(new TradeOffer(Constants.PeaceTreaty, TradeOfferType.Treaty, 1, 10));
        sink.Drain();
        trade.AcceptTrade();
        var accepted = Assert.Single(Of<TradeAccepted>(sink));
        Assert.Equal(a.CivId, accepted.CivId);
        Assert.Equal(b.CivId, accepted.OtherCivId);
        Assert.Equal(new TradeOfferFact("Gold", TradeOfferType.Gold, 50, -1), Assert.Single(accepted.OffersFromFirst));
        Assert.Equal(TradeOfferType.Treaty, Assert.Single(accepted.OffersFromSecond).Type);
    }

    [Fact]
    public void TurnManagerReportsTurnFlowPerCivWithoutGlobalCurrentPlayer()
    {
        var (sink, a, b) = Arena();
        b.PlayerType = PlayerType.AI;
        sink.Drain();

        new Unciv.Logic.Civilization.Managers.TurnManager(b).StartTurn();
        new Unciv.Logic.Civilization.Managers.TurnManager(b).AutomateTurn();
        new Unciv.Logic.Civilization.Managers.TurnManager(b).EndTurn();
        new Unciv.Logic.Civilization.Managers.TurnManager(a).EndTurn();

        var flow = sink.Drain().OfType<EngineEvent>().Where(it => it is TurnStarted or TurnEnded or AutoPlayerTurnProcessed).ToList();
        int turn = testGame.GameInfo.Turns;
        Assert.Equal<EngineEvent>(
            [new TurnStarted(b.CivId, turn), new AutoPlayerTurnProcessed(b.CivId, turn), new TurnEnded(b.CivId, turn), new TurnEnded(a.CivId, turn)],
            flow);
    }

    // --- sessions and purity, on a real started game ---

    private static RulesetClass LoadVanilla()
    {
        RulesetCache.Clear();
        string jsons = JsonsPath();
        RulesetCache.LoadRulesets(new DirectoryRulesetFileSource(jsons, Path.Combine(jsons, "no mods here")), noMods: true);
        return RulesetCache.GetVanillaRuleset();
    }

    private static string JsonsPath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "android", "assets", "jsons");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException("android/assets/jsons not found above " + AppContext.BaseDirectory);
    }

    private static GameInfo NewGame(RulesetClass ruleset) =>
        GameStarter.StartNewGame(
            new GameSetupInfo(
                new GameParameters
                {
                    BaseRuleset = ruleset.Name,
                    NumberOfCityStates = 1,
                    VictoryTypes = ruleset.Victories.Keys.ToList(),
                    Players = [new Player("Babylon", PlayerType.Human), new Player("Greece", PlayerType.Human), new Player("Rome")],
                },
                new MapParameters { Type = MapType.Pangaea, Shape = MapShape.Rectangular, Seed = 3, MapSize = MapSize.Tiny }),
            7L, 1000L);

    private static ulong Hash(GameInfo game) => StateHasher.ComputeHash(h => h.Add(UncivFiles.GameInfoToString(game, forceZip: false)));

    [Fact]
    public void ACommandSessionInstallsTheSinkForOneCommandAndReturnsEventsInCallOrder()
    {
        var ruleset = LoadVanilla();
        try
        {
            var game = NewGame(ruleset);
            var registry = GameCommandRegistry.Create();
            registry.Register(typeof(NotifyOnApplyForTest), "notify-on-apply");
            var session = new GameSession(game, registry);

            var result = session.Submit("Babylon", new NotifyOnApplyForTest { Text = "hi" });

            Assert.True(result.Accepted);
            Assert.Equal(
                Json(new NotificationAdded("Babylon", "hi", NotificationCategory.General, [], []), new ResearchCompleted("Babylon", "returned-by-command")),
                Json(result.Events.ToArray()));
            Assert.Null(game.EventSink);
        }
        finally
        {
            RulesetCache.Clear();
        }
    }

    [CommandType("notify-on-apply")]
    private sealed class NotifyOnApplyForTest : GameCommand
    {
        public string Text { get; init; } = "";

        protected override CommandValidation ValidateCommand(CommandContext context) => CommandValidation.Ok;

        protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
        {
            context.Actor.AddNotification(Text, NotificationCategory.General);
            return [new ResearchCompleted(context.ActorId, "returned-by-command")];
        }
    }

    [Fact]
    public void TheSinkNeverChangesTheGameStateAndEventsAreDeterministic()
    {
        var ruleset = LoadVanilla();
        try
        {
            var plain = NewGame(ruleset);
            var observed = NewGame(ruleset);
            var again = NewGame(ruleset);
            var sink = new GameInfoEventSink(1_000_000);
            var sinkAgain = new GameInfoEventSink(1_000_000);
            observed.EventSink = sink;
            again.EventSink = sinkAgain;
            Assert.Equal(Hash(plain), Hash(observed));

            for (int turn = 0; turn < 6; turn++)
            {
                foreach (var game in new[] { plain, observed, again })
                {
                    game.NextTurn(2000L + turn);
                }
                Assert.Equal(Hash(plain), Hash(observed));
                Assert.Equal(Hash(observed), Hash(again));
            }

            var events = sink.Drain();
            Assert.Contains(events, it => it is TurnStarted);
            Assert.Contains(events, it => it is TurnEnded);
            Assert.Contains(events, it => it is AutoPlayerTurnProcessed);
            Assert.Contains(events, it => it is UnitMoved or UnitCreated or NotificationAdded or TileOwnerChanged);
            Assert.Equal(
                events.Select(it => Json(GameEventRegistry.ToEnvelope((EngineEvent)it))),
                sinkAgain.Drain().Select(it => Json(GameEventRegistry.ToEnvelope((EngineEvent)it))));
        }
        finally
        {
            RulesetCache.Clear();
        }
    }

    [Fact]
    public void ClonesDoNotInheritTheSink()
    {
        var ruleset = LoadVanilla();
        try
        {
            var game = NewGame(ruleset);
            game.EventSink = new GameInfoEventSink();
            Assert.Null(game.Clone().EventSink);
        }
        finally
        {
            RulesetCache.Clear();
        }
    }
}
