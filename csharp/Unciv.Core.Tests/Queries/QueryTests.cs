using System.Text.Json;
using Unciv.Core.Tests.Support;
using Unciv.Json;
using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Logic.Files;
using Unciv.Logic.Map;
using Unciv.Queries;
using Unciv.Utils;
using Unciv.View;

namespace Unciv.Core.Tests.Queries;

// T185: query infrastructure. The toy queries below stand in for the real ones (T195-T199).
[Collection("RulesetCache")]
public class QueryTests
{
    public sealed record TerrainResult(bool Known, string? BaseTerrain);

    // Reads one tile through the viewer's TileMapView, which returns null for unexplored tiles.
    [QueryType("test.Terrain")]
    public sealed record TerrainQuery(int X, int Y) : IQuery<TerrainResult>
    {
        public string Type => "test.Terrain";

        public TerrainResult Run(GameView view)
        {
            var tile = view.TileMapView.GetTile(new HexCoord(X, Y));
            return tile == null ? new TerrainResult(false, null) : new TerrainResult(true, tile.BaseTerrain);
        }
    }

    public sealed record ViewerResult(string Civ, bool Spectator, int Turn);

    [QueryType("test.Viewer")]
    public sealed record ViewerQuery : IQuery<ViewerResult>
    {
        public string Type => "test.Viewer";

        public ViewerResult Run(GameView view) => new(view.CivView.CivName, view.SpectatorMode, view.GetTurn());
    }

    [QueryType("test.Reject")]
    public sealed record RejectQuery : IQuery<ViewerResult>
    {
        public string Type => "test.Reject";

        public ViewerResult Run(GameView view) => throw new QueryException("no-such-thing", "nothing here");
    }

    [QueryType("test.Throws")]
    public sealed record ThrowsQuery(int X) : IQuery<ViewerResult>
    {
        public string Type => "test.Throws";

        public ViewerResult Run(GameView view) => X == 0 ? throw new InvalidOperationException("boom") : null!;
    }

    [QueryType("test.WrongName")]
    public sealed record MismatchQuery : IQuery<ViewerResult>
    {
        public string Type => "test.Other";

        public ViewerResult Run(GameView view) => new("", false, 0);
    }

    public abstract record AbstractQuery;

    private readonly TestGame testGame = new();
    private readonly Civilization player;
    private readonly Civilization enemy;
    private readonly QueryRegistry registry = NewRegistry();

    public QueryTests()
    {
        testGame.MakeHexagonalMap(2);
        player = testGame.AddCiv(testGame.Ruleset.Nations["Germany"], isPlayer: true);
        enemy = testGame.AddCiv(testGame.Ruleset.Nations["Egypt"]);
    }

    private static QueryRegistry NewRegistry()
    {
        var registry = new QueryRegistry(UncivJson.DefaultOptions);
        foreach (var type in typeof(QueryTests).Assembly.GetTypes().Where(it => it.IsDefined(typeof(QueryTypeAttribute), false)))
            if (type != typeof(MismatchQuery))
                registry.Register(type, type.GetCustomAttributes(typeof(QueryTypeAttribute), false).Cast<QueryTypeAttribute>().Single().Name);
        registry.RegisterFromAssembly(typeof(QueryRegistry).Assembly); // the purity test runs the real queries too
        return registry;
    }

    private QueryResponse Run(Civilization viewer, IQuery query)
    {
        var envelope = registry.ToEnvelope(viewer.CivName, query);
        // Through the wire form, as the host would see it.
        var wire = JsonSerializer.Deserialize<QueryEnvelope>(JsonSerializer.Serialize(envelope, UncivJson.DefaultOptions), UncivJson.DefaultOptions)!;
        return registry.Execute(wire, new QueryContext(testGame.GameInfo));
    }

    private static ulong Hash(GameInfo game) => StateHasher.ComputeHash(h => h.Add(UncivFiles.GameInfoToString(game, forceZip: false)));

    [Fact]
    public void EnvelopeRoundTripsAndReturnsTheResult()
    {
        var tile = testGame.GetTile(0, 0);
        tile.SetExplored(player, true);
        var query = new TerrainQuery(0, 0);

        var envelope = registry.ToEnvelope(player.CivName, query);
        Assert.Equal("test.Terrain", envelope.Type);
        Assert.Equal(player.CivName, envelope.ViewerId);
        Assert.True(registry.TryFromEnvelope(envelope, out var decoded, out _));
        Assert.Equal(query, decoded);

        var response = Run(player, query);
        Assert.True(response.Ok);
        var result = response.Result!.Value.Deserialize<TerrainResult>(UncivJson.DefaultOptions)!;
        Assert.True(result.Known);
        Assert.Equal(tile.BaseTerrain, result.BaseTerrain);
    }

    [Fact]
    public void SameQueryTwiceGivesIdenticalJson()
    {
        testGame.GetTile(0, 0).SetExplored(player, true);
        Assert.Equal(Run(player, new TerrainQuery(0, 0)).Result!.Value.GetRawText(), Run(player, new TerrainQuery(0, 0)).Result!.Value.GetRawText());
    }

    [Fact]
    public void UnknownTypeIsRejected()
    {
        var response = registry.Execute(new QueryEnvelope("nope", player.CivName, JsonSerializer.SerializeToElement(new { })), new QueryContext(testGame.GameInfo));
        Assert.False(response.Ok);
        Assert.Equal(QueryCodes.UnknownQuery, response.Code);
    }

    [Fact]
    public void MalformedPayloadsAreRejectedNotThrown()
    {
        foreach (var json in new[] { "[]", "null", "{\"X\":\"abc\"}", "{\"X\":{}}" })
        {
            var envelope = new QueryEnvelope("test.Terrain", player.CivName, JsonDocument.Parse(json).RootElement.Clone());
            var response = registry.Execute(envelope, new QueryContext(testGame.GameInfo));
            Assert.False(response.Ok);
            Assert.Equal(QueryCodes.MalformedQuery, response.Code);
        }
    }

    [Fact]
    public void MissingEnvelopeFieldsAreRejectedNotThrown()
    {
        var context = new QueryContext(testGame.GameInfo);
        var empty = JsonSerializer.Deserialize<QueryEnvelope>("{}", UncivJson.DefaultOptions)!;
        Assert.Equal(QueryCodes.UnknownQuery, registry.Execute(empty, context).Code);
        var noViewer = JsonSerializer.Deserialize<QueryEnvelope>("{\"Type\":\"test.Viewer\",\"Payload\":{}}", UncivJson.DefaultOptions)!;
        Assert.Equal(QueryCodes.UnknownViewer, registry.Execute(noViewer, context).Code);
        var noPayload = JsonSerializer.Deserialize<QueryEnvelope>("{\"Type\":\"test.Viewer\",\"ViewerId\":\"Germany\"}", UncivJson.DefaultOptions)!;
        Assert.Equal(QueryCodes.MalformedQuery, registry.Execute(noPayload, context).Code);
        var wrongCase = new QueryEnvelope("test.Viewer", player.CivName.ToUpperInvariant(), JsonSerializer.SerializeToElement(new { }));
        Assert.Equal(QueryCodes.UnknownViewer, registry.Execute(wrongCase, context).Code);
    }

    [Fact]
    public void HostilePayloadsDoNotEscape()
    {
        var context = new QueryContext(testGame.GameInfo);
        var deep = new string('[', 5000) + new string(']', 5000);
        foreach (var json in new[] { "{\"X\":99999999999}", "{\"X\":1,\"Extra\":[1,2]}", "{\"X\":1e999}", "{\"X\":" + deep + "}" })
        {
            JsonElement payload;
            try
            {
                payload = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 10000 }).RootElement.Clone();
            }
            catch (JsonException)
            {
                continue;
            }
            var response = registry.Execute(new QueryEnvelope("test.Terrain", player.CivName, payload), context);
            if (json.Contains("Extra", StringComparison.Ordinal))
            {
                Assert.True(response.Ok, json); // unknown properties are ignored, as for commands
                continue;
            }
            Assert.False(response.Ok, json);
            Assert.Equal(QueryCodes.MalformedQuery, response.Code);
        }
    }

    [Fact]
    public void QueryBugsBecomeInternalErrors()
    {
        Assert.Equal(QueryCodes.InternalError, Run(player, new ThrowsQuery(0)).Code);
        Assert.Equal(QueryCodes.InternalError, Run(player, new ThrowsQuery(1)).Code);
    }

    [Fact]
    public void RegistrationGuardsCatchWrongClasses()
    {
        var fresh = new QueryRegistry(UncivJson.DefaultOptions);
        fresh.RegisterFromAssembly(typeof(QueryTests).Assembly);
        Assert.Contains("test.Terrain", fresh.RegisteredTypes);
        var count = fresh.RegisteredTypes.Count;
        Assert.Throws<InvalidOperationException>(() => fresh.RegisterFromAssembly(typeof(QueryTests).Assembly));
        Assert.Equal(count, fresh.RegisteredTypes.Count);
        Assert.Throws<InvalidOperationException>(() => fresh.Register(typeof(AbstractQuery), "test.Abstract"));
        fresh = new QueryRegistry(UncivJson.DefaultOptions);
        Assert.Throws<InvalidOperationException>(() => fresh.Register(typeof(string), "test.NotAQuery"));
        Assert.Throws<InvalidOperationException>(() => fresh.Register(typeof(ViewerQuery), ""));

        fresh.Register<MismatchQuery>("test.WrongName");
        var envelope = new QueryEnvelope("test.WrongName", player.CivName, JsonSerializer.SerializeToElement(new { }));
        Assert.Equal(QueryCodes.MalformedQuery, fresh.Execute(envelope, new QueryContext(testGame.GameInfo)).Code);
        Assert.Throws<InvalidOperationException>(() => fresh.ToEnvelope("x", new MismatchQuery()));
    }

    [Fact]
    public void UnknownViewerIsRejected()
    {
        var envelope = registry.ToEnvelope("Atlantis", new ViewerQuery());
        var response = registry.Execute(envelope, new QueryContext(testGame.GameInfo));
        Assert.Equal(QueryCodes.UnknownViewer, response.Code);
    }

    [Fact]
    public void QueryExceptionBecomesAFailedResponse()
    {
        var response = Run(player, new RejectQuery());
        Assert.False(response.Ok);
        Assert.Equal("no-such-thing", response.Code);
    }

    [Fact]
    public void DuplicateAndUnregisteredTypesAreRefused()
    {
        Assert.Throws<InvalidOperationException>(() => registry.Register<TerrainQuery>("test.Other"));
        var fresh = new QueryRegistry(UncivJson.DefaultOptions);
        Assert.Throws<InvalidOperationException>(() => fresh.ToEnvelope("x", new ViewerQuery()));
        fresh.Register<ViewerQuery>("test.Viewer");
        Assert.Throws<InvalidOperationException>(() => fresh.Register<RejectQuery>("test.Viewer"));
    }

    [Fact]
    public void ViewerSeesOnlyWhatItHasExplored()
    {
        var tile = testGame.GetTile(1, 0);
        tile.SetExplored(enemy, true);

        var seenByEnemy = Run(enemy, new TerrainQuery(1, 0)).Result!.Value.Deserialize<TerrainResult>(UncivJson.DefaultOptions)!;
        var seenByPlayer = Run(player, new TerrainQuery(1, 0)).Result!.Value.Deserialize<TerrainResult>(UncivJson.DefaultOptions)!;
        Assert.True(seenByEnemy.Known);
        Assert.Equal(tile.BaseTerrain, seenByEnemy.BaseTerrain);
        Assert.Equal(new TerrainResult(false, null), seenByPlayer);
        Assert.DoesNotContain(tile.BaseTerrain, Run(player, new TerrainQuery(1, 0)).Result!.Value.GetRawText());
    }

    [Fact]
    public void TheViewerIsExplicitNotTheCurrentPlayer()
    {
        testGame.GameInfo.CurrentPlayer = enemy.CivName;
        var asPlayer = Run(player, new ViewerQuery()).Result!.Value.Deserialize<ViewerResult>(UncivJson.DefaultOptions)!;
        var asEnemy = Run(enemy, new ViewerQuery()).Result!.Value.Deserialize<ViewerResult>(UncivJson.DefaultOptions)!;
        Assert.Equal(player.CivName, asPlayer.Civ);
        Assert.Equal(enemy.CivName, asEnemy.Civ);
    }

    // Sample requests for every query that Unciv.Core registers: the purity test below fails for a registered
    // query without an entry, so query tickets (T195-T199) must add theirs here.
    // The state hash is the serialized GameInfo, which includes the RNG state, so a
    // query that draws randomness is caught here too.
    private static readonly Dictionary<string, Func<IQuery>> CoreSamples = new()
    {
        ["units.ReachableTiles"] = () => new Unciv.Queries.Units.ReachableTilesQuery(1),
        ["units.PathPreview"] = () => new Unciv.Queries.Units.PathPreviewQuery(1, 0, 0),
        ["units.AttackableTargets"] = () => new Unciv.Queries.Units.AttackableTargetsQuery(UnitId: 1),
        ["units.UnitActions"] = () => new Unciv.Queries.Units.UnitActionsQuery(1),
        ["empire.TechTree"] = () => new Unciv.Queries.Empire.TechTreeQuery(),
        ["empire.PolicyTree"] = () => new Unciv.Queries.Empire.PolicyTreeQuery(),
        ["empire.Religion"] = () => new Unciv.Queries.Empire.ReligionQuery(),
        ["empire.GreatPeople"] = () => new Unciv.Queries.Empire.GreatPeopleQuery(),
        ["empire.Notifications"] = () => new Unciv.Queries.Empire.NotificationsQuery(),
        ["empire.TopBar"] = () => new Unciv.Queries.Empire.EmpireTopBarQuery(),
        ["empire.Overview"] = () => new Unciv.Queries.Empire.EmpireOverviewQuery(),
        ["empire.DiplomacyPanel"] = () => new Unciv.Queries.Empire.DiplomacyPanelQuery("Egypt"),
        ["empire.DiplomacyActions"] = () => new Unciv.Queries.Empire.DiplomacyActionsQuery("Egypt"),
        ["empire.TradeOptions"] = () => new Unciv.Queries.Empire.TradeOptionsQuery("Egypt"),
        ["empire.TradeHint"] = () => new Unciv.Queries.Empire.TradeHintQuery("Egypt", [], []),
        ["empire.PendingDecisions"] = () => new Unciv.Queries.Empire.PendingDecisionsQuery(),
        ["empire.SpyDestinations"] = () => new Unciv.Queries.Empire.SpyDestinationsQuery(),
        ["empire.TradeRoutes"] = () => new Unciv.Queries.Empire.TradeRoutesQuery(),
        ["empire.TradeRouteOptions"] = () => new Unciv.Queries.Empire.TradeRouteOptionsQuery(1),
        ["empire.CityStateQuests"] = () => new Unciv.Queries.Empire.CityStateQuestsQuery("Egypt"),
        ["empire.DiplomaticVote"] = () => new Unciv.Queries.Empire.DiplomaticVoteQuery(),
        ["empire.DiplomaticVoteResult"] = () => new Unciv.Queries.Empire.DiplomaticVoteResultQuery(),
        ["units.PromotionChoices"] = () => new Unciv.Queries.Units.PromotionChoicesQuery(1),
        ["units.PromotableUnits"] = () => new Unciv.Queries.Units.PromotableUnitsQuery(),
        ["console.Plan"] = () => new Unciv.Queries.DevConsole.ConsolePlanQuery("civ addstat gold 5"),
        ["console.Autocomplete"] = () => new Unciv.Queries.DevConsole.ConsoleAutocompleteQuery("civ add"),
        ["units.AttackPreview"] = () => new Unciv.Queries.Units.AttackPreviewQuery(0, 0, UnitId: 1),
        ["cities.CityPanel"] = () => new Unciv.Queries.Cities.CityPanelQuery("c"),
        ["cities.ConstructionList"] = () => new Unciv.Queries.Cities.ConstructionListQuery("c"),
        ["cities.CityTiles"] = () => new Unciv.Queries.Cities.CityTilesQuery("c"),
        ["cities.BuyableTiles"] = () => new Unciv.Queries.Cities.BuyableTilesQuery("c"),
        ["tiles.TileInfo"] = () => new Unciv.Queries.Tiles.TileInfoQuery(0, 0),
        ["victory.Progress"] = () => new Unciv.Queries.Victory.VictoryProgressQuery(),
        ["victory.Scores"] = () => new Unciv.Queries.Victory.ScoresQuery(),
        ["victory.Rankings"] = () => new Unciv.Queries.Victory.RankingsQuery(),
        ["victory.Demographics"] = () => new Unciv.Queries.Victory.DemographicsQuery(),
        ["victory.History"] = () => new Unciv.Queries.Victory.HistoryQuery(),
        ["tutorial.Task"] = () => new Unciv.Queries.Tutorial.TutorialTaskQuery(),
        ["victory.GameOver"] = () => new Unciv.Queries.Victory.GameOverQuery(),
    };

    [Fact]
    public void EveryRegisteredQueryIsPureAndHasASample()
    {
        var core = new QueryRegistry(UncivJson.DefaultOptions);
        core.RegisterFromAssembly(typeof(QueryRegistry).Assembly);
        foreach (var type in core.RegisteredTypes)
            Assert.True(CoreSamples.ContainsKey(type), $"Registered query \"{type}\" has no sample in QueryTests.CoreSamples");

        testGame.GetTile(0, 0).SetExplored(player, true);
        var samples = CoreSamples.Values.Select(it => it())
            .Concat([new TerrainQuery(0, 0), new TerrainQuery(1, 0), new ViewerQuery(), new RejectQuery()]);
        foreach (var civ in new[] { player, enemy })
            foreach (var sample in samples)
            {
                var before = Hash(testGame.GameInfo);
                Run(civ, sample);
                Assert.Equal(before, Hash(testGame.GameInfo));
            }
    }

    private sealed record MutatingQuery : IQuery<ViewerResult>
    {
        public string Type => "test.Mutate";

        public ViewerResult Run(GameView view)
        {
            var civ = view.CivView.Unwrap();
            civ.GameInfo.TileMap[new HexCoord(0, 0)].SetExplored(civ, true);
            return new ViewerResult("", false, 0);
        }
    }

    [Fact]
    public void PurityCheckDetectsAMutatingQuery()
    {
        var local = new QueryRegistry(UncivJson.DefaultOptions);
        local.Register<MutatingQuery>("test.Mutate");
        var before = Hash(testGame.GameInfo);
        local.Execute(local.ToEnvelope(player.CivName, new MutatingQuery()), new QueryContext(testGame.GameInfo));
        Assert.NotEqual(before, Hash(testGame.GameInfo));
    }
}
