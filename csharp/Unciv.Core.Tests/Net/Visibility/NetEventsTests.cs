using System.Text.Json;
using Unciv.Commands.Events;
using Unciv.Core.Tests.Support;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using Unciv.Net.Visibility;

namespace Unciv.Core.Tests.Net.Visibility;

// T175: per-recipient event classifiers and the TilesRevealed / StatsChanged deltas.
[Collection("RulesetCache")]
public sealed class NetEventsTests
{
    private sealed class FakeVisibility(IEnumerable<HexCoord> seen, IEnumerable<string>? known = null, IEnumerable<int>? hiddenUnits = null) : IViewerVisibility
    {
        private readonly HashSet<HexCoord> seenTiles = seen.ToHashSet();
        private readonly HashSet<string> knownCivs = (known ?? []).ToHashSet();
        private readonly HashSet<int> hidden = (hiddenUnits ?? []).ToHashSet();

        public bool CanSeeTile(HexCoord position) => seenTiles.Contains(position);

        public bool CanSeeUnit(string civId, int unitId, HexCoord position) => CanSeeTile(position) && !hidden.Contains(unitId);

        public bool KnowsCiv(string civId) => knownCivs.Contains(civId);
    }

    private static readonly HexCoord P0 = new(0, 0);
    private static readonly HexCoord P1 = new(1, 0);
    private static readonly HexCoord P2 = new(2, 0);
    private static readonly HexCoord Far = new(9, 9);

    [Fact]
    public void EveryEventTypeHasAClassifier_AndNoClassifierIsOrphaned()
    {
        foreach (var (name, eventClass) in GameEventRegistry.Scan())
            Assert.True(EventFilter.HasClassifier(eventClass), $"{eventClass.Name} ({name}) has no classifier in EventFilter");
        Assert.False(EventFilter.HasClassifier(typeof(string)));
    }

    [Fact]
    public void Everyone_AndParticipants()
    {
        var vis = new FakeVisibility([]);
        Assert.Single(EventFilter.Classify(new TurnResolving(3), "B", vis));
        Assert.Single(EventFilter.Classify(new PlayerEndedTurn("A", 3), "B", vis));
        var alert = new AlertAdded("A", AlertType.WarDeclaration, "x");
        Assert.Single(EventFilter.Classify(alert, "A", vis));
        Assert.Empty(EventFilter.Classify(alert, "B", vis));
        Assert.Empty(EventFilter.Classify(new ResearchCompleted("A", "Pottery"), "B", vis));
        Assert.Empty(EventFilter.Classify(new TradeAccepted("A", "F", [], []), "B", new FakeVisibility([], ["A", "F"])));
    }

    [Fact]
    public void War_IsPublicOnlyToCivsKnowingBothParties()
    {
        var war = new WarDeclared("A", "F");
        Assert.Single(EventFilter.Classify(war, "B", new FakeVisibility([], ["A", "F"])));
        Assert.Empty(EventFilter.Classify(war, "B", new FakeVisibility([], ["A"])));
        Assert.Single(EventFilter.Classify(war, "F", new FakeVisibility([])));
    }

    [Fact]
    public void UnitMoved_FullyVisible_ParticipantAndEmptyPath()
    {
        var moved = new UnitMoved("A", 5, "Warrior", [P0, P1, P2]);
        Assert.Same(moved, EventFilter.Classify(moved, "B", new FakeVisibility([P0, P1, P2])).Single());
        Assert.Same(moved, EventFilter.Classify(moved, "A", new FakeVisibility([])).Single());
        Assert.Empty(EventFilter.Classify(new UnitMoved("A", 5, "Warrior", []), "B", new FakeVisibility([P0])));
    }

    [Fact]
    public void UnitMoved_PartlyVisible_BecomesAppearOrDisappear_NeverAPath()
    {
        var moved = new UnitMoved("A", 5, "Warrior", [P0, P1, P2]);
        var appear = EventFilter.Classify(moved, "B", new FakeVisibility([P2])).Single();
        Assert.Equal(new UnitAppeared("A", 5, "Warrior", P2), appear);
        var disappear = EventFilter.Classify(moved, "B", new FakeVisibility([P0])).Single();
        Assert.Equal(new UnitDisappeared("A", 5, P0), disappear);
        var both = EventFilter.Classify(moved, "B", new FakeVisibility([P0, P2]));
        Assert.Equal<EngineEvent>([new UnitDisappeared("A", 5, P0), new UnitAppeared("A", 5, "Warrior", P2)], both);
        Assert.Empty(EventFilter.Classify(moved, "B", new FakeVisibility([P1])));
        Assert.Empty(EventFilter.Classify(moved, "B", new FakeVisibility([P0, P1, P2], hiddenUnits: [5])));
        // Standing still on a visible tile (start == end) must not report a disappearance.
        Assert.Equal(new UnitAppeared("A", 5, "Warrior", P0), EventFilter.Classify(new UnitMoved("A", 5, "Warrior", [P0, P1, P0]), "B", new FakeVisibility([P0])).Single());
    }

    [Fact]
    public void UnitMoved_VisibleEndsWithFoggedMiddle_IsDisappearThenAppear_AndHiddenUnitStaysSilent()
    {
        var moved = new UnitMoved("A", 5, "Warrior", [P0, P1, P2]);
        var vis = new FakeVisibility([P0, P2]);
        Assert.Equal<EngineEvent>([new UnitDisappeared("A", 5, P0), new UnitAppeared("A", 5, "Warrior", P2)], EventFilter.Classify(moved, "B", vis));
        Assert.Empty(EventFilter.Classify(moved, "B", new FakeVisibility([P0, P2], hiddenUnits: [5])));
    }

    [Fact]
    public void PeaceTerritoryAndCityEvents_NonParticipantNeedsSight()
    {
        Assert.Single(EventFilter.Classify(new PeaceMade("A", "F"), "B", new FakeVisibility([], ["A", "F"])));
        Assert.Single(EventFilter.Classify(new PeaceMade("A", "F"), "A", new FakeVisibility([])));
        Assert.Empty(EventFilter.Classify(new PeaceMade("A", "F"), "B", new FakeVisibility([], ["F"])));
        var owner = new TileOwnerChanged(P0, "", "A");
        Assert.Single(EventFilter.Classify(owner, "B", new FakeVisibility([P0])));
        Assert.Empty(EventFilter.Classify(owner, "B", new FakeVisibility([P1])));
        var captured = new CityCaptured("c", "Ur", P0, "A", "F", "A");
        Assert.Single(EventFilter.Classify(captured, "B", new FakeVisibility([P0])));
        Assert.Empty(EventFilter.Classify(captured, "B", new FakeVisibility([])));
        var grew = new CityGrew("A", "c", P0, 3);
        Assert.Single(EventFilter.Classify(grew, "B", new FakeVisibility([P0])));
        Assert.Empty(EventFilter.Classify(grew, "B", new FakeVisibility([])));
        var cities = new UnitAttacked("A", null, "cA", P0, "F", null, "cF", P1, 1, 1, false, false);
        Assert.Single(EventFilter.Classify(cities, "B", new FakeVisibility([P0, P1])));
        Assert.Empty(EventFilter.Classify(cities, "B", new FakeVisibility([P0])));
    }

    [Fact]
    public void LocatedEvents_RequireEveryPositionVisible()
    {
        var founded = new CityFounded("A", "c1", "Rome", P0);
        Assert.Single(EventFilter.Classify(founded, "B", new FakeVisibility([P0])));
        Assert.Empty(EventFilter.Classify(founded, "B", new FakeVisibility([P1])));
        var created = new UnitCreated("A", 1, "Scout", P0);
        Assert.Single(EventFilter.Classify(created, "B", new FakeVisibility([P0])));
        Assert.Empty(EventFilter.Classify(created, "B", new FakeVisibility([P0], hiddenUnits: [1])));
        Assert.Single(EventFilter.Classify(new UnitDestroyed("A", 1, "Scout", P0), "B", new FakeVisibility([P0])));
        Assert.Empty(EventFilter.Classify(new UnitDestroyed("A", 1, "Scout", P0), "B", new FakeVisibility([Far])));
    }

    [Fact]
    public void UnitAttacked_NeedsBothSides_CityDefenderUsesTile()
    {
        var attack = new UnitAttacked("A", 1, null, P0, "F", null, "cityF", P1, 5, 2, false, false);
        Assert.Single(EventFilter.Classify(attack, "B", new FakeVisibility([P0, P1])));
        Assert.Empty(EventFilter.Classify(attack, "B", new FakeVisibility([P0])));
        Assert.Empty(EventFilter.Classify(attack, "B", new FakeVisibility([P1])));
        Assert.Empty(EventFilter.Classify(attack, "B", new FakeVisibility([P0, P1], hiddenUnits: [1])));
        Assert.Single(EventFilter.Classify(attack, "F", new FakeVisibility([])));
        var unitDefender = new UnitAttacked("A", null, "cityA", P0, "F", 7, null, P1, 5, 2, false, false);
        Assert.Empty(EventFilter.Classify(unitDefender, "B", new FakeVisibility([P0, P1], hiddenUnits: [7])));
    }

    [Fact]
    public void Notification_OnlyToOwner_AndUnseenLocationsStripped()
    {
        var note = new NotificationAdded("A", "Enemy spotted", Notification.NotificationCategory.War, ["Warrior"], [P0, Far]);
        Assert.Empty(EventFilter.Classify(note, "B", new FakeVisibility([P0, Far])));
        var own = (NotificationAdded)EventFilter.Classify(note, "A", new FakeVisibility([P0])).Single();
        Assert.Equal<HexCoord>([P0], own.Locations);
        Assert.Equal("Enemy spotted", own.Text);
        Assert.Empty(((NotificationAdded)EventFilter.Classify(note, "A", new FakeVisibility([])).Single()).Locations);
    }

    [Fact]
    public void ServerBuiltEvents_AreNeverForwardedFromTheEngine()
    {
        var vis = new FakeVisibility([P0]);
        Assert.Empty(EventFilter.Classify(new UnitAppeared("A", 1, "Scout", P0), "A", vis));
        Assert.Empty(EventFilter.Classify(new UnitDisappeared("A", 1, P0), "A", vis));
        Assert.Empty(EventFilter.Classify(new TilesRevealed("A", [], []), "A", vis));
        Assert.Empty(EventFilter.Classify(new StatsChanged("A", 1, 1), "A", vis));
    }

    // Real-state tests: A sees the origin area, B is elsewhere.
    private readonly TestGame game = new();

    private (Civilization A, Civilization B) TwoCivs()
    {
        game.MakeHexagonalMap(7, Constants.Grassland);
        var a = game.AddCiv(isPlayer: true);
        var b = game.AddCiv(isPlayer: true);
        game.GameInfo.CurrentPlayerCiv = a;
        game.AddCity(a, game.GetTile(new HexCoord(0, 0)));
        game.AddCity(b, game.GetTile(new HexCoord(-6, 0)));
        foreach (var civ in new[] { a, b }) civ.Cache.UpdateState();
        foreach (var tile in game.TileMap.Values.Where(it => HexMath.GetDistance(HexCoord.Zero, it.Position) <= 1)) tile.SetExplored(a, true);
        a.ViewableTiles = game.TileMap.Values.Where(it => HexMath.GetDistance(HexCoord.Zero, it.Position) <= 1).ToHashSet();
        return (a, b);
    }

    [Fact]
    public void Delta_TilesRevealedAndStatsChanged_ComputedFromBeforeAfter()
    {
        var (a, _) = TwoCivs();
        var before = ViewDeltaBuilder.Capture(game.GameInfo, a);
        var newTile = game.GetTile(new HexCoord(3, 0));
        newTile.SetExplored(a, true);
        a.AddGold(50);
        var events = ViewDeltaBuilder.BuildEvents(game.GameInfo, a, before, [new TurnResolving(1)]);
        var revealed = Assert.IsType<TilesRevealed>(events[0]);
        Assert.Equal(newTile.Position, new HexCoord(revealed.Tiles.Single().X, revealed.Tiles.Single().Y));
        Assert.False(revealed.Tiles.Single().Visible);
        Assert.IsType<TurnResolving>(events[1]);
        var stats = Assert.IsType<StatsChanged>(events[2]);
        Assert.Equal(a.CivId, stats.CivId);
        Assert.Equal(a.Gold, stats.Gold);

        var again = ViewDeltaBuilder.BuildEvents(game.GameInfo, a, ViewDeltaBuilder.Capture(game.GameInfo, a), []);
        Assert.Empty(again);
    }

    [Fact]
    public void Delta_NewlyVisibleTileCarriesItsVisibleForeignUnits()
    {
        var (a, b) = TwoCivs();
        var before = ViewDeltaBuilder.Capture(game.GameInfo, a);
        var spot = game.GetTile(new HexCoord(3, 0));
        game.AddUnit("Warrior", b, spot);
        spot.SetExplored(a, true);
        a.ViewableTiles = a.ViewableTiles.Append(spot).ToHashSet();
        var revealed = Assert.IsType<TilesRevealed>(ViewDeltaBuilder.BuildEvents(game.GameInfo, a, before, []).Single());
        Assert.True(revealed.Tiles.Single().Visible);
        Assert.Equal(b.CivId, revealed.Units.Single().Owner);
        Assert.Null(revealed.Units.Single().Own);
    }

    [Fact]
    public void Delta_FoggedTileTurningVisible_IsRefreshedWithItsUnits()
    {
        var (a, b) = TwoCivs();
        var spot = game.GetTile(new HexCoord(3, 0));
        spot.SetExplored(a, true);
        var before = ViewDeltaBuilder.Capture(game.GameInfo, a);
        Assert.Empty(before.KnownCivs!);
        game.AddUnit("Warrior", b, spot);
        a.ViewableTiles = a.ViewableTiles.Append(spot).ToHashSet();
        var revealed = Assert.IsType<TilesRevealed>(ViewDeltaBuilder.BuildEvents(game.GameInfo, a, before, []).Single());
        Assert.True(revealed.Tiles.Single().Visible);
        Assert.Equal(b.CivId, revealed.Units.Single().Owner);
        Assert.True(revealed.Civs != null, "null civs"); Assert.Equal(b.CivId, revealed.Civs!.Single().CivId); // sighted an unmet civ's unit
        Assert.Null(revealed.Civs!.Single().Own);
        // Nothing changes on a second diff.
        Assert.Empty(ViewDeltaBuilder.BuildEvents(game.GameInfo, a, ViewDeltaBuilder.Capture(game.GameInfo, a), []));
    }

    [Fact]
    public void Delta_MeetingACiv_IntroducesItWithoutTiles()
    {
        var (a, b) = TwoCivs();
        var before = ViewDeltaBuilder.Capture(game.GameInfo, a);
        a.DiplomacyFunctions.MakeCivilizationsMeet(b);
        var revealed = Assert.IsType<TilesRevealed>(ViewDeltaBuilder.BuildEvents(game.GameInfo, a, before, []).Single());
        Assert.Empty(revealed.Tiles);
        Assert.Equal(b.CivId, revealed.Civs!.Single().CivId);
    }

    [Fact]
    public void Delta_PerTurnYieldChange_EmitsStatsChanged()
    {
        var (a, _) = TwoCivs();
        var before = ViewDeltaBuilder.Capture(game.GameInfo, a);
        Assert.Equal(before.Yields, ViewDeltaBuilder.Capture(game.GameInfo, a).Yields);
        var changed = before with { Yields = new ViewDeltaBuilder.YieldsPerTurn(-1, -1, -1, -1) };
        var stats = Assert.IsType<StatsChanged>(ViewDeltaBuilder.BuildEvents(game.GameInfo, a, changed, []).Single());
        Assert.Equal(a.Gold, stats.Gold);
    }

    [Fact]
    public void Delta_WireForm_RoundTripsAndFiltersPerRecipient()
    {
        var (a, b) = TwoCivs();
        var unit = game.AddUnit("Warrior", a, game.GetTile(new HexCoord(1, 0)));
        a.ViewableTiles = game.TileMap.Values.Where(it => HexMath.GetDistance(HexCoord.Zero, it.Position) <= 1).ToHashSet();
        b.ViewableTiles = new[] { game.GetTile(new HexCoord(-6, 0)) }.ToHashSet();
        var moved = new UnitMoved(a.CivId, unit.Id, "Warrior", [new HexCoord(1, 0), new HexCoord(0, 0)]);
        var note = new NotificationAdded(a.CivId, "secret-note", Notification.NotificationCategory.General, [], [new HexCoord(5, 0)]);
        var deltaA = ViewDeltaBuilder.Build(game.GameInfo, a, ViewDeltaBuilder.Capture(game.GameInfo, a), [moved, note], 4);
        var deltaB = ViewDeltaBuilder.Build(game.GameInfo, b, ViewDeltaBuilder.Capture(game.GameInfo, b), [moved, note], 4);
        Assert.Equal(4, deltaA.Seq);
        Assert.Equal(2, deltaA.Events.Count);
        Assert.Empty(deltaB.Events); // B sees neither the moved tiles nor owns the notification
        Assert.IsType<UnitMoved>(GameEventRegistry.FromEnvelope(deltaA.Events[0].Deserialize<GameEventEnvelope>(Unciv.Json.UncivJson.DefaultOptions)!));
        var envelope = deltaA.Events[1].Deserialize<GameEventEnvelope>(Unciv.Json.UncivJson.DefaultOptions)!;
        var back = Assert.IsType<NotificationAdded>(GameEventRegistry.FromEnvelope(envelope));
        Assert.Empty(back.Locations);
    }

    [Fact]
    public void GameViewVisibility_AnswersFromViews()
    {
        var (a, b) = TwoCivs();
        var vis = new GameViewVisibility(game.GameInfo, a);
        Assert.True(vis.CanSeeTile(HexCoord.Zero));
        Assert.False(vis.CanSeeTile(new HexCoord(5, 0)));
        Assert.False(vis.CanSeeTile(new HexCoord(99, 99)));
        Assert.True(vis.KnowsCiv(a.CivId));
        Assert.False(vis.KnowsCiv(b.CivId));
        var warrior = game.AddUnit("Warrior", b, game.GetTile(new HexCoord(3, 0)));
        a.ViewableTiles = game.TileMap.Values.Where(it => HexMath.GetDistance(HexCoord.Zero, it.Position) <= 3).ToHashSet();
        game.GetTile(new HexCoord(3, 0)).SetExplored(a, true);
        Assert.True(vis.CanSeeUnit(b.CivId, warrior.Id, new HexCoord(3, 0)));
        Assert.True(vis.CanSeeUnit(b.CivId, warrior.Id, new HexCoord(0, 1))); // tile seen, unit elsewhere, not invisible
        Assert.False(vis.CanSeeUnit(b.CivId, warrior.Id, new HexCoord(5, 0)));
        Assert.True(vis.CanSeeUnit(b.CivId, 99999, new HexCoord(0, 1))); // gone unit
        Assert.True(vis.CanSeeUnit("nobody", 1, new HexCoord(0, 1)));
        // Hidden before the command: its death or move stays unreported, whether the unit is gone or elsewhere.
        var hiddenVis = new GameViewVisibility(game.GameInfo, a, new HashSet<(string, int)> { (b.CivId, 99999), (b.CivId, warrior.Id) });
        Assert.False(hiddenVis.CanSeeUnit(b.CivId, 99999, new HexCoord(0, 1)));
        Assert.False(hiddenVis.CanSeeUnit(b.CivId, warrior.Id, new HexCoord(0, 1)));
    }
}
