using System.Reflection;
using Unciv.Core.Tests.Support;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using Unciv.Logic.Trade;
using Unciv.Net;
using Unciv.Net.Visibility;
using CityClass = Unciv.Logic.City.City;

namespace Unciv.Core.Tests.Net.Visibility;

// T174: the visibility filter on hidden-state fixtures. Two humans (A, B) plus a foreign AI civ F that A has met, and a civ H nobody has met.
// Everything secret carries a distinctive marker so a leak anywhere in the serialized text is caught.
[Collection("RulesetCache")]
public sealed class NetVisibilityTests
{
    private const string SecretSeedText = "987654321123";
    private const string SecretGameId = "secret-game-id-xyz";

    private readonly TestGame game = new();
    private readonly Civilization a;
    private readonly Civilization b;
    private readonly Civilization f;
    private readonly Civilization h;
    private readonly Civilization g;
    private readonly CityClass fogCity;
    private readonly CityClass hiddenCity;
    private readonly CityClass foreignCity;

    private static readonly TurnStatusDto Status = new() { Ended = ["B"], Deadline = 1700000000000, Phase = "playing" };

    public NetVisibilityTests()
    {
        game.MakeHexagonalMap(7, Constants.Grassland);
        a = game.AddCiv(isPlayer: true);
        b = game.AddCiv(isPlayer: true);
        f = game.AddCiv();
        h = game.AddCiv();
        g = game.AddCiv();
        game.GameInfo.CurrentPlayerCiv = a;
        game.GameInfo.TileMap.MapParameters.Seed = long.Parse(SecretSeedText, System.Globalization.CultureInfo.InvariantCulture);
        game.GameInfo.GameId = SecretGameId;

        game.AddCity(a, game.GetTile(new HexCoord(0, 0)), initialPopulation: 2);
        foreignCity = game.AddCity(f, game.GetTile(new HexCoord(3, 0)));
        fogCity = game.AddCity(f, game.GetTile(new HexCoord(0, 3)));
        hiddenCity = game.AddCity(h, game.GetTile(new HexCoord(6, 0)));
        game.AddCity(b, game.GetTile(new HexCoord(-6, 0)));
        game.AddCity(g, game.GetTile(new HexCoord(0, -6)));
        foreach (var civ in new[] { a, b, f, h, g }) civ.Cache.UpdateState();
        a.DiplomacyFunctions.MakeCivilizationsMeet(f);

        f.AddGold(4242);
        h.AddGold(7777);
        a.AddGold(123);
        foreignCity.CityConstructions.AddToQueue("Worker");

        var near = game.TileMap.Values.Where(it => HexMath.GetDistance(HexCoord.Zero, it.Position) <= 4).ToList();
        foreach (var tile in near) tile.SetExplored(a, true);
        foreach (var tile in game.TileMap.Values.Where(it => HexMath.GetDistance(new HexCoord(-6, 0), it.Position) <= 2)) tile.SetExplored(b, true);
        ApplyVision();
    }

    // Adding units recomputes viewable tiles; pin the fixture's fog after every change so a unit added mid-test cannot widen vision.
    private void ApplyVision()
    {
        a.ViewableTiles = game.TileMap.Values.Where(it => HexMath.GetDistance(HexCoord.Zero, it.Position) <= 3 && it.Position != new HexCoord(0, 3)).ToHashSet();
        b.ViewableTiles = game.TileMap.Values.Where(it => HexMath.GetDistance(new HexCoord(-6, 0), it.Position) <= 2).ToHashSet();
    }

    private ClientSnapshot SnapshotFor(Civilization civ)
    {
        ApplyVision();
        return SnapshotBuilder.Build(game.GameInfo, civ, 7, Status);
    }

    private static string Text(ClientSnapshot snapshot) => SnapshotBuilder.ToCanonicalJson(snapshot);

    [Fact]
    public void Tiles_OnlyExploredAreSerialized_AndFogHidesLiveState()
    {
        var snapshot = SnapshotFor(a);
        var expectedExplored = game.TileMap.Values.Count(it => it.IsExplored(a));
        Assert.Equal(expectedExplored, snapshot.Tiles.Count);
        Assert.DoesNotContain(snapshot.Tiles, it => HexMath.GetDistance(HexCoord.Zero, new HexCoord(it.X, it.Y)) > 4);
        var fogged = snapshot.Tiles.Single(it => it.X == 0 && it.Y == 3);
        Assert.False(fogged.Visible);
        Assert.Null(fogged.Owner);
        Assert.Null(fogged.Road);
        Assert.Null(fogged.RoadPillaged);
        var seen = snapshot.Tiles.Single(it => it.X == 3 && it.Y == 0);
        Assert.True(seen.Visible);
        Assert.Equal(f.CivId, seen.Owner);
    }

    [Fact]
    public void Cities_ForeignCityIsReducedAndUnexploredCitiesAreAbsent()
    {
        var snapshot = SnapshotFor(a);
        // Cities with an explored center are listed even under fog (the View rule, ForeignCivView.Cities); H's city is unexplored.
        Assert.Equal(3, snapshot.Cities.Count);
        Assert.Contains(snapshot.Cities, it => it.Name == fogCity.Name);
        Assert.DoesNotContain(snapshot.Cities, it => it.Name == hiddenCity.Name);
        var foreign = snapshot.Cities.Single(it => it.Name == foreignCity.Name);
        Assert.Null(foreign.Own);
        Assert.Equal(f.CivId, foreign.Owner);
        var own = snapshot.Cities.Single(it => it.Owner == a.CivId);
        Assert.NotNull(own.Own);
        Assert.Contains("Worker", foreignCity.CityConstructions.ConstructionQueue);
        Assert.DoesNotContain("Worker", Text(snapshot));
    }

    [Fact]
    public void Civs_ForeignGoldAndUnmetCivsNeverAppear()
    {
        var snapshot = SnapshotFor(a);
        var text = Text(snapshot);
        Assert.DoesNotContain("4242", text);
        Assert.DoesNotContain("7777", text);
        Assert.DoesNotContain(snapshot.Civs, it => it.CivId == h.CivId);
        Assert.DoesNotContain(snapshot.Civs, it => it.CivId == b.CivId);
        Assert.Null(snapshot.Civs.Single(it => it.CivId == f.CivId).Own);
        var self = snapshot.Civs.Single(it => it.CivId == a.CivId).Own;
        Assert.NotNull(self);
        Assert.Equal(123, self.Gold);
    }

    [Fact]
    public void Units_HiddenForeignUnitsAreNotSerialized()
    {
        var visible = game.AddUnit("Warrior", f, game.GetTile(new HexCoord(2, 0)));
        var fogged = game.AddUnit("Warrior", f, game.GetTile(new HexCoord(0, 3)));
        var unexplored = game.AddUnit("Warrior", h, game.GetTile(new HexCoord(6, 1)));
        var ghost = game.AddDefaultMeleeUnitWithUniques(f, game.GetTile(new HexCoord(-2, 0)), "Invisible to others");
        var own = game.AddUnit("Warrior", a, game.GetTile(new HexCoord(0, 1)));
        Assert.False(ghost.IsVisibleTo(a));

        var snapshot = SnapshotFor(a);
        var ids = snapshot.Units.Select(it => it.Id).ToList();
        Assert.Contains(visible.Id, ids);
        Assert.Contains(own.Id, ids);
        Assert.DoesNotContain(fogged.Id, ids);
        Assert.DoesNotContain(unexplored.Id, ids);
        Assert.DoesNotContain(ghost.Id, ids);
        Assert.Null(snapshot.Units.Single(it => it.Id == visible.Id).Own);
        Assert.NotNull(snapshot.Units.Single(it => it.Id == own.Id).Own);
    }

    [Fact]
    public void Units_HealthIsBucketedForForeignUnits()
    {
        var unit = game.AddUnit("Warrior", f, game.GetTile(new HexCoord(2, 0)));
        unit.Health = 30;
        var dto = SnapshotFor(a).Units.Single(it => it.Id == unit.Id);
        Assert.Equal(2, dto.HealthBucket);
        Assert.Null(dto.Own);
    }

    [Fact]
    public void Units_OwnerSeesAutomationAndPromotionReadinessAndOthersDoNot()
    {
        var unit = game.AddUnit("Warrior", a, game.GetTile(new HexCoord(0, 1)));
        var plain = SnapshotFor(a).Units.Single(it => it.Id == unit.Id).Own!;
        Assert.False(plain.Automated);
        Assert.False(plain.CanBePromoted);

        unit.Automated = true;
        unit.Promotions.XP = 1000;
        var busy = SnapshotFor(a).Units.Single(it => it.Id == unit.Id).Own!;
        Assert.True(busy.Automated);
        Assert.True(busy.CanBePromoted);
        Assert.Equal(unit.Promotions.CanBePromoted(), busy.CanBePromoted);
    }

    [Fact]
    public void Snapshot_ContainsNoSeedOrGameId()
    {
        var text = Text(SnapshotFor(a));
        Assert.DoesNotContain(SecretSeedText, text);
        Assert.DoesNotContain(SecretGameId, text);
        Assert.DoesNotContain("seed", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("gameId", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OwnPolicies_AndDiplomacyStatus_AreExposedToTheOwnerOnly()
    {
        a.Policies.AdoptedPolicies.Add("Secret Policy");
        var forA = SnapshotFor(a);
        Assert.Equal(["Secret Policy"], forA.Civs.Single(it => it.CivId == a.CivId).Own!.Policies);
        Assert.All(forA.Civs.Where(it => it.CivId != a.CivId), it => Assert.Null(it.Own));
        Assert.DoesNotContain("Secret Policy", Text(SnapshotFor(b)));
        Assert.All(forA.Diplomacy, it => Assert.NotNull(it.Status));
        Assert.All(forA.Cities, it => Assert.NotNull(it.VisibleBuildings));
    }

    // GetVisibleBuildings uses IsAnyWonder, so national wonders count as public too (matches CityStatsTable.kt); only ordinary buildings are private.
    [Fact]
    public void ForeignCity_VisibleBuildingsListOnlyWonders_OwnerStillSeesEverything()
    {
        var wonder = game.CreateWonder();
        var plain = game.CreateBuilding();
        foreignCity.CityConstructions.AddBuilding(plain.Name);
        foreignCity.CityConstructions.AddBuilding(wonder.Name);

        var forA = SnapshotFor(a);
        var dto = forA.Cities.Single(it => it.Name == foreignCity.Name);
        // The capital's Palace is a national wonder, hence listed too.
        Assert.Equal(["Palace", wonder.Name], dto.VisibleBuildings);
        Assert.Null(dto.Own);
        Assert.DoesNotContain(plain.Name, Text(forA));

        var ownDto = SnapshotFor(f).Cities.Single(it => it.Name == foreignCity.Name);
        Assert.Contains(plain.Name, ownDto.Own!.Buildings);
        Assert.Contains(wonder.Name, ownDto.Own.Buildings);
    }

    [Fact]
    public void CityId_IsOnlyInTheOwnersSnapshot()
    {
        var id = foreignCity.Id;
        Assert.False(string.IsNullOrEmpty(id));
        Assert.Equal(id, SnapshotFor(f).Cities.Single(it => it.Name == foreignCity.Name).Own!.Id);
        Assert.Null(SnapshotFor(a).Cities.Single(it => it.Name == foreignCity.Name).Own);
        foreach (var viewer in new[] { a, b, h, g }) Assert.DoesNotContain(id, Text(SnapshotFor(viewer)));
    }

    [Fact]
    public void Trades_ThirdPartyRequestToAKnownCivIsAbsent()
    {
        var other = new Trade();
        other.TheirOffers.Add(new TradeOffer("Gold", TradeOfferType.Gold, 90210));
        f.TradeRequests.Add(new TradeRequest(h.CivId, other));
        var snapshot = SnapshotFor(a);
        Assert.Empty(snapshot.PendingTrades);
        Assert.DoesNotContain("90210", Text(snapshot));
    }

    [Fact]
    public void Trades_OnlyTheTwoPartiesSeeAProposal()
    {
        var offer = new Trade();
        offer.TheirOffers.Add(new TradeOffer("Gold", TradeOfferType.Gold, 55));
        offer.OurOffers.Add(new TradeOffer("Gold", TradeOfferType.Gold, 66));
        f.TradeRequests.Add(new TradeRequest(a.CivId, offer));
        var secret = new Trade();
        secret.TheirOffers.Add(new TradeOffer("Gold", TradeOfferType.Gold, 31337));
        f.TradeRequests.Add(new TradeRequest(h.CivId, secret));
        a.TradeRequests.Add(new TradeRequest(f.CivId, secret));

        var snapshot = SnapshotFor(a);
        Assert.Equal(2, snapshot.PendingTrades.Count);
        Assert.Contains(snapshot.PendingTrades, it => it.From == a.CivId && it.To == f.CivId && it.Offered[0].Amount == 55 && it.Requested[0].Amount == 66);
        Assert.Contains(snapshot.PendingTrades, it => it.From == f.CivId && it.To == a.CivId);

        Assert.Contains("\"amount\":55", Text(snapshot)); // the needle format is right, so its absence below means something
        var forB = Text(SnapshotFor(b));
        Assert.Empty(SnapshotFor(b).PendingTrades);
        Assert.DoesNotContain("\"amount\":55", forB); // a JSON number; city ids (C017) are random text that may contain "55"
        Assert.DoesNotContain("\"amount\":31337", forB);
    }

    [Fact]
    public void Diplomacy_OnlyViewerRelationsAndPublicWars()
    {
        var snapshot = SnapshotFor(a);
        Assert.Single(snapshot.Diplomacy);
        Assert.Equal(a.CivId, snapshot.Diplomacy[0].A);
        Assert.Equal(f.CivId, snapshot.Diplomacy[0].B);
    }

    [Fact]
    public void Notifications_LocationActionsOnUnseenTilesAreStripped()
    {
        a.Notifications.Clear();
        a.AddNotification("seen", new HexCoord(1, 0), Notification.NotificationCategory.General);
        a.AddNotification("fog", new HexCoord(0, 3), Notification.NotificationCategory.General);
        a.AddNotification("unexplored", new HexCoord(6, 1), Notification.NotificationCategory.General);
        a.AddNotification("offmap", new HexCoord(900, 900), Notification.NotificationCategory.General);
        a.AddNotification("tech", new TechAction("Pottery"), Notification.NotificationCategory.General);
        a.AddNotification("link", new LinkAction("https://example.invalid"), Notification.NotificationCategory.General);
        a.AddNotification("promote", [new PromoteUnitAction("Warrior", new HexCoord(900, 900), 17)], Notification.NotificationCategory.Units);
        a.AddNotification("spies", new EspionageAction(), Notification.NotificationCategory.Espionage);

        var notifications = SnapshotFor(a).Notifications;
        Assert.Equal(8, notifications.Count);
        var promote = notifications.Single(it => it.Text == "promote").Actions.Single(); // the unit is the recipient's own, so its id is kept even though the tile is not visible
        Assert.Equal(("promote", 17), (promote.Kind, promote.Id));
        Assert.Equal("espionage", notifications.Single(it => it.Text == "spies").Actions.Single().Kind);
        Assert.Single(notifications.Single(it => it.Text == "seen").Actions);
        Assert.Empty(notifications.Single(it => it.Text == "fog").Actions);
        Assert.Empty(notifications.Single(it => it.Text == "unexplored").Actions);
        Assert.Empty(notifications.Single(it => it.Text == "offmap").Actions);
        Assert.Equal("tech", notifications.Single(it => it.Text == "tech").Actions.Single().Kind);
        Assert.Empty(notifications.Single(it => it.Text == "link").Actions);
    }

    [Fact]
    public void TwoHumans_BSnapshotNeverHoldsWhatOnlyASees()
    {
        var onlyA = game.AddUnit("Warrior", a, game.GetTile(new HexCoord(0, 1)));
        var onlyB = game.AddUnit("Warrior", b, game.GetTile(new HexCoord(-6, 1)));
        a.AddNotification("a-secret-note", Notification.NotificationCategory.General);

        var forA = SnapshotFor(a);
        var forB = SnapshotFor(b);
        Assert.Contains(forA.Units, it => it.Id == onlyA.Id);
        Assert.DoesNotContain(forA.Units, it => it.Id == onlyB.Id);
        Assert.Contains(forB.Units, it => it.Id == onlyB.Id);
        Assert.DoesNotContain(forB.Units, it => it.Id == onlyA.Id);
        Assert.DoesNotContain(forB.Cities, it => it.Owner == a.CivId);
        Assert.DoesNotContain(forB.Civs, it => it.CivId == a.CivId);
        Assert.DoesNotContain("a-secret-note", Text(forB));
        Assert.Equal(b.Gold, forB.Civs.Single(it => it.CivId == b.CivId).Own!.Gold);
        Assert.Equal(b.CivId, forB.You);
        Assert.Equal(["B"], forB.TurnStatus.Ended);
    }

    [Fact]
    public void Tiles_FoggedImprovementIsTheLastSeenOne()
    {
        var tile = game.GetTile(new HexCoord(0, 3));
        tile.Improvement = "Farm";
        a.SetLastSeenImprovement(tile.Position, "Mine");
        Assert.Equal("Mine", SnapshotFor(a).Tiles.Single(it => it.X == 0 && it.Y == 3).Improvement);
    }

    [Fact]
    public void OwnDetail_NeverGrantedForForeignCitiesOrUnitsEvenWithVisibleMap()
    {
        game.GameInfo.EngineOptions.VisibleMap = true;
        var unit = game.AddUnit("Warrior", f, game.GetTile(new HexCoord(2, 0)));
        var snapshot = SnapshotFor(a);
        Assert.All(snapshot.Cities.Where(it => it.Owner != a.CivId), it => Assert.Null(it.Own));
        Assert.Null(snapshot.Units.Single(it => it.Id == unit.Id).Own);
        Assert.DoesNotContain("Worker", Text(snapshot));
    }

    [Fact]
    public void Diplomacy_ThirdPartyWarsArePublicButThirdPartyPeaceIsNot()
    {
        a.DiplomacyFunctions.MakeCivilizationsMeet(g);
        f.DiplomacyFunctions.MakeCivilizationsMeet(g);
        f.GetDiplomacyManager(g)!.DeclareWar();
        Assert.True(f.IsAtWarWith(g), f.GetDiplomacyManager(g)!.DiplomaticStatus.ToString());
        var pairs = SnapshotFor(a).Diplomacy;
        Assert.Contains(pairs, it => it.A == f.CivId && it.B == g.CivId && it.AtWar);
        Assert.DoesNotContain(pairs, it => it.A == h.CivId || it.B == h.CivId);
        f.GetDiplomacyManager(g)!.MakePeace();
        Assert.DoesNotContain(SnapshotFor(a).Diplomacy, it => it.A == f.CivId && it.B == g.CivId);
    }

    [Fact]
    public void Civs_OwnerOfAVisibleTileIsListed()
    {
        var tile = game.GetTile(new HexCoord(1, 1));
        tile.SetOwningCity(hiddenCity);
        var snapshot = SnapshotFor(a);
        var dto = snapshot.Tiles.Single(it => it.X == 1 && it.Y == 1);
        Assert.Equal(h.CivId, dto.Owner);
        Assert.Contains(snapshot.Civs, it => it.CivId == h.CivId);
    }

    [Fact]
    public void ViewHash_IsDeterministicAndSensitiveToState()
    {
        var first = SnapshotFor(a);
        var second = SnapshotFor(a);
        Assert.Equal(first.ViewHash, second.ViewHash);
        Assert.Equal(first.ViewHash, SnapshotBuilder.ComputeViewHash(first));
        Assert.Equal(16, first.ViewHash!.Length);
        game.AddUnit("Warrior", f, game.GetTile(new HexCoord(2, 0)));
        Assert.NotEqual(first.ViewHash, SnapshotFor(a).ViewHash);
        // Hidden changes do not move the hash of a viewer who cannot see them.
        var before = SnapshotFor(a).ViewHash;
        h.AddGold(5);
        game.AddUnit("Warrior", h, game.GetTile(new HexCoord(6, 1)));
        Assert.Equal(before, SnapshotFor(a).ViewHash);
    }

    [Fact]
    public void ToMessage_RoundTripsThroughTheProtocolCodec()
    {
        var snapshot = SnapshotFor(a);
        var message = SnapshotBuilder.ToMessage(snapshot);
        var decoded = Assert.IsType<Snapshot>(ProtocolCodec.Decode(ProtocolCodec.Encode(message)));
        Assert.Equal(snapshot.ViewHash, decoded.ViewHash);
        Assert.Equal(snapshot.Tiles.Count, decoded.Tiles.Count);
        Assert.Equal(snapshot.You, decoded.You);
    }

    // Default-deny: the exact member list of every DTO is pinned. Adding a field forces an edit here, i.e. a conscious review of what leaks.
    private static readonly Dictionary<string, string> Whitelist = new()
    {
        [nameof(ClientSnapshot)] = "Seq Turn Phase TurnStatus You Civs Tiles Cities Units Diplomacy PendingTrades Notifications PopupAlerts PendingDecisions ViewHash",
        [nameof(TurnStatusDto)] = "Ended Deadline Phase",
        [nameof(CivDto)] = "CivId Name IsCityState IsMajor IsBarbarian IsDefeated Own",
        [nameof(OwnCivDto)] = "Gold Happiness CurrentTech Techs Policies Spies",
        [nameof(SpyDto)] = "Name Rank Location Action",
        [nameof(TileDto)] = "X Y Visible BaseTerrain Features NaturalWonder Resource Improvement RiverBottom RiverBottomLeft RiverBottomRight Owner Road RoadPillaged ImprovementPillaged",
        [nameof(CityDto)] = "Name Owner X Y Population IsCapital IsPuppet IsBeingRazed Health MaxHealth VisibleBuildings Own",
        [nameof(OwnCityDto)] = "Id FoodStored CultureStored Buildings ConstructionQueue",
        [nameof(UnitDto)] = "Id Name Owner X Y HealthBucket IsEmbarked IsFortified Own",
        [nameof(OwnUnitDto)] = "Health CurrentMovement Xp Action Promotions Automated CanBePromoted",
        [nameof(DiplomacyDto)] = "A B AtWar Status",
        [nameof(PendingTradeDto)] = "From To Offered Requested",
        [nameof(TradeOfferDto)] = "Name Type Amount Duration",
        [nameof(NotificationDto)] = "Category Text Icons Actions",
        [nameof(NotificationActionDto)] = "Kind X Y Name Id",
        [nameof(PopupAlertDto)] = "Type Value",
        [nameof(PendingDecisionDto)] = "Kind Subject ResolvedBy Blocking Index"
    };

    [Fact]
    public void Dtos_ContainOnlyWhitelistedMembers()
    {
        var dtoTypes = typeof(ClientSnapshot).Assembly.GetTypes()
            .Where(it => it.Namespace == "Unciv.Net.Visibility" && it.Name.EndsWith("Dto", StringComparison.Ordinal) || it == typeof(ClientSnapshot))
            .ToList();
        Assert.Equal(Whitelist.Keys.Order(StringComparer.Ordinal), dtoTypes.Select(it => it.Name).Order(StringComparer.Ordinal));
        foreach (var type in dtoTypes)
        {
            var members = type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(it => it.Name != "EqualityContract").Select(it => it.Name);
            Assert.Equal(Whitelist[type.Name].Split(' '), members);
            Assert.Empty(type.GetFields(BindingFlags.Public | BindingFlags.Instance));
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(it => it.Name != "EqualityContract"))
                Assert.True(IsPlainData(property.PropertyType, dtoTypes), $"{type.Name}.{property.Name} has non-DTO type {property.PropertyType}");
        }
    }

    private static bool IsPlainData(Type type, List<Type> dtoTypes)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type.IsPrimitive || type == typeof(string)) return true;
        if (dtoTypes.Contains(type)) return true;
        return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>) && IsPlainData(type.GetGenericArguments()[0], dtoTypes);
    }
}
