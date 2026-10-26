using System.Text.Json;
using Unciv.Core.Tests.Support;
using Unciv.Json;
using Unciv.Logic.Civilization;
using Unciv.Net;
using Unciv.Net.Remote;
using Unciv.Queries;
using Unciv.View;

namespace Unciv.Core.Tests.Net.Remote;

// T223: host dispatch (policy, resolving, rate limit, cache, collapsed errors) and the async client.
[Collection("RulesetCache")]
public class RemoteQueryTests
{
    public sealed record EchoResult(string Viewer, int Value);

    private static int Runs;

    [QueryType("t223.Echo")]
    public sealed record EchoQuery(int Value = 0) : IQuery<EchoResult>
    {
        public string Type => "t223.Echo";

        public EchoResult Run(GameView view)
        {
            Interlocked.Increment(ref Runs);
            return new EchoResult(view.CivView.CivName, Value);
        }
    }

    [QueryType("t223.Heavy")]
    public sealed record HeavyQuery : IQuery<EchoResult>
    {
        public string Type => "t223.Heavy";

        public EchoResult Run(GameView view) => new(view.CivView.CivName, 0);
    }

    [QueryType("t223.Reject")]
    public sealed record RejectQuery(string Why = "") : IQuery<EchoResult>
    {
        public string Type => "t223.Reject";

        public EchoResult Run(GameView view) => throw new QueryException("no-such-unit-" + Why, "Unit 7 of Egypt does not exist");
    }

    [QueryType("t223.Boom")]
    public sealed record BoomQuery : IQuery<EchoResult>
    {
        public string Type => "t223.Boom";

        public EchoResult Run(GameView view)
        {
            Interlocked.Increment(ref Runs);
            throw new InvalidOperationException("secret detail");
        }
    }

    [QueryType("t223.Big")]
    public sealed record BigQuery : IQuery<string>
    {
        public string Type => "t223.Big";

        public string Run(GameView view) => new('x', ProtocolVersion.MaxFrameBytes);
    }

    [QueryType("t223.Denied")]
    public sealed record DeniedQuery : IQuery<EchoResult>
    {
        public string Type => "t223.Denied";

        public EchoResult Run(GameView view) => throw new InvalidOperationException("must not run");
    }

    [QueryType("t223.Undecided")]
    public sealed record UndecidedQuery : IQuery<EchoResult>
    {
        public string Type => "t223.Undecided";

        public EchoResult Run(GameView view) => throw new InvalidOperationException("must not run");
    }

    private static readonly RemoteQueryPolicy Policy = new(new Dictionary<string, QueryExposure>
    {
        ["t223.Echo"] = QueryExposure.Allow("test"),
        ["t223.Heavy"] = QueryExposure.Allow("test", QueryExposure.Heavy),
        ["t223.Reject"] = QueryExposure.Allow("test"),
        ["t223.Boom"] = QueryExposure.Allow("test"),
        ["t223.Big"] = QueryExposure.Allow("test"),
        ["t223.Denied"] = QueryExposure.Deny("test"),
    });

    private readonly TestGame testGame = new();
    private readonly Civilization player;
    private readonly Civilization enemy;
    private readonly QueryRegistry registry = new(UncivJson.DefaultOptions);
    private readonly QueryContext context;

    public RemoteQueryTests()
    {
        testGame.MakeHexagonalMap(2);
        player = testGame.AddCiv(testGame.Ruleset.Nations["Germany"], isPlayer: true);
        enemy = testGame.AddCiv(testGame.Ruleset.Nations["Egypt"]);
        context = new QueryContext(testGame.GameInfo);
        foreach (var type in new[] { typeof(EchoQuery), typeof(HeavyQuery), typeof(RejectQuery), typeof(BoomQuery), typeof(BigQuery), typeof(DeniedQuery), typeof(UndecidedQuery) })
            registry.Register(type, ((QueryTypeAttribute)type.GetCustomAttributes(typeof(QueryTypeAttribute), false).Single()).Name);
        Runs = 0;
    }

    private RemoteQueryHost NewHost(RemoteQueryLimits? limits = null) => new(registry, Policy, limits);

    private Query Ask(IQuery query, long id = 1)
    {
        var envelope = registry.ToEnvelope("", query);
        return new Query { QueryId = id, QueryType = envelope.Type, Payload = envelope.Payload };
    }

    private QueryResult Handle(RemoteQueryHost host, IQuery query, string session = "s1", Civilization? viewer = null, long seq = 1, bool resolving = false, long now = 0, long id = 1) =>
        host.Handle(session, (viewer ?? player).CivName, Ask(query, id), context, seq, resolving, now);

    [Fact]
    public void AllowedQueryRunsAsTheSessionsCivAndEchoesTheId()
    {
        var result = Handle(NewHost(), new EchoQuery(7), id: 42);
        Assert.True(result.Ok);
        Assert.Equal(42, result.QueryId);
        Assert.Null(result.Code);
        var echo = result.Payload!.Value.Deserialize<EchoResult>(UncivJson.DefaultOptions)!;
        Assert.Equal(new EchoResult(player.CivName, 7), echo);
    }

    [Fact]
    public void TheViewerIsTheSessionsCivNotAnythingInThePayload()
    {
        var host = NewHost();
        var forged = new Query { QueryId = 1, QueryType = "t223.Echo", Payload = JsonDocument.Parse($$"""{"Value":1,"ViewerId":"{{enemy.CivName}}","Viewer":"{{enemy.CivName}}"}""").RootElement.Clone() };
        var result = host.Handle("s1", player.CivName, forged, context, 1, false, 0);
        Assert.Equal(player.CivName, result.Payload!.Value.Deserialize<EchoResult>(UncivJson.DefaultOptions)!.Viewer);
        Assert.Equal(enemy.CivName, Handle(host, new EchoQuery(1), session: "s2", viewer: enemy).Payload!.Value.Deserialize<EchoResult>(UncivJson.DefaultOptions)!.Viewer);
    }

    [Fact]
    public void DeniedUndecidedAndUnknownQueriesAreIndistinguishable()
    {
        var host = NewHost();
        var denied = Handle(host, new DeniedQuery());
        var undecided = Handle(host, new UndecidedQuery());
        var unknown = host.Handle("s1", player.CivName, new Query { QueryId = 1, QueryType = "nope.Nothing", Payload = JsonDocument.Parse("{}").RootElement }, context, 1, false, 0);
        foreach (var result in new[] { denied, undecided, unknown })
        {
            Assert.False(result.Ok);
            Assert.Equal(QueryWireCodes.UnknownQuery, result.Code);
            Assert.Null(result.Payload);
        }
        Assert.Equal(ProtocolCodec.Encode(denied), ProtocolCodec.Encode(unknown));
    }

    [Fact]
    public void AllowedButUnregisteredTypeIsUnknown()
    {
        var host = new RemoteQueryHost(new QueryRegistry(UncivJson.DefaultOptions), Policy);
        Assert.Equal(QueryWireCodes.UnknownQuery, Handle(host, new EchoQuery()).Code);
    }

    [Fact]
    public void ResolvingRefusesEveryQuery()
    {
        var host = NewHost();
        Assert.True(Handle(host, new EchoQuery(), seq: 1).Ok);
        var result = Handle(host, new EchoQuery(), seq: 1, resolving: true); // even one that is cached
        Assert.Equal(QueryWireCodes.TurnResolving, result.Code);
        Assert.False(result.Ok);
        Assert.True(Handle(host, new EchoQuery(), seq: 2, resolving: false).Ok);
    }

    [Fact]
    public void QueryErrorsCollapseToOneCodeWithNoText()
    {
        var host = NewHost();
        var a = Handle(host, new RejectQuery("a"));
        var b = Handle(host, new RejectQuery("b"));
        Assert.Equal(QueryWireCodes.Rejected, a.Code);
        Assert.Equal(ProtocolCodec.Encode(a), ProtocolCodec.Encode(b));
        Assert.DoesNotContain("Egypt", ProtocolCodec.Encode(a));
        // A viewer that is not in the game is also just "rejected".
        var ghost = host.Handle("s9", "No Such Civ", Ask(new EchoQuery()), context, 1, false, 0);
        Assert.Equal(QueryWireCodes.Rejected, ghost.Code);
    }

    [Fact]
    public void MalformedPayloadAndInternalErrorsAreCoarse()
    {
        var host = NewHost();
        var malformed = host.Handle("s1", player.CivName, new Query { QueryId = 1, QueryType = "t223.Echo", Payload = JsonDocument.Parse("""{"Value":"abc"}""").RootElement }, context, 1, false, 0);
        Assert.Equal(QueryWireCodes.MalformedQuery, malformed.Code);
        var notObject = host.Handle("s1", player.CivName, new Query { QueryId = 1, QueryType = "t223.Echo", Payload = JsonDocument.Parse("[1]").RootElement }, context, 1, false, 0);
        Assert.Equal(QueryWireCodes.MalformedQuery, notObject.Code);
        var boom = Handle(host, new BoomQuery());
        Assert.Equal(QueryWireCodes.InternalError, boom.Code);
        Assert.DoesNotContain("secret", ProtocolCodec.Encode(boom));
    }

    [Fact]
    public void InternalErrorsAreNotCached()
    {
        var host = NewHost();
        Handle(host, new BoomQuery());
        Handle(host, new BoomQuery());
        Assert.Equal(2, Runs);
        Assert.Equal(0, host.CachedResults);
    }

    [Fact]
    public void TooLargeResultIsRefusedNotSent()
    {
        var result = Handle(NewHost(), new BigQuery());
        Assert.Equal(QueryWireCodes.ResultTooLarge, result.Code);
        Assert.Null(result.Payload);
        Assert.True(ProtocolCodec.Encode(result).Length < 200);
    }

    [Fact]
    public void ResultsAreCachedPerViewerPayloadAndViewSeq()
    {
        var host = NewHost();
        var first = Handle(host, new EchoQuery(1), id: 1);
        var again = Handle(host, new EchoQuery(1), id: 2);
        Assert.Equal(1, Runs);
        Assert.Equal(2, again.QueryId); // cached answer still carries the asker's id
        Assert.Equal(first.Payload!.Value.GetRawText(), again.Payload!.Value.GetRawText());
        Handle(host, new EchoQuery(2)); // other payload
        Assert.Equal(2, Runs);
        Handle(host, new EchoQuery(1), viewer: enemy, session: "s2"); // other viewer never sees the first viewer's answer
        Assert.Equal(3, Runs);
        Handle(host, new EchoQuery(1), viewer: enemy, session: "s2", seq: 2); // state moved on
        Assert.Equal(4, Runs);
        Assert.Equal(1, host.CachedResults);
        Handle(host, new EchoQuery(1), viewer: enemy, session: "s2", seq: 2);
        Assert.Equal(4, Runs);
    }

    [Fact]
    public void CacheIsBounded()
    {
        var host = NewHost(new RemoteQueryLimits(Capacity: 1000, RefillPerSecond: 1000, MaxCacheEntries: 3));
        for (int i = 0; i < 10; i++) Handle(host, new EchoQuery(i));
        Assert.InRange(host.CachedResults, 1, 3);
    }

    [Fact]
    public void RateLimitRefusesThenRefills()
    {
        var host = NewHost(new RemoteQueryLimits(Capacity: 3, RefillPerSecond: 2));
        Assert.True(Handle(host, new EchoQuery(1), now: 0).Ok);
        Assert.True(Handle(host, new EchoQuery(1), now: 0).Ok); // cached hit still costs a token
        Assert.True(Handle(host, new EchoQuery(1), now: 0).Ok);
        var limited = Handle(host, new EchoQuery(1), now: 0);
        Assert.Equal(QueryWireCodes.RateLimited, limited.Code);
        Assert.False(Handle(host, new EchoQuery(1), now: 400).Ok); // 0.8 token
        Assert.True(Handle(host, new EchoQuery(1), now: 500).Ok); // 1 token
        Assert.False(Handle(host, new EchoQuery(1), now: 500).Ok);
        Assert.True(Handle(host, new EchoQuery(1), now: 60_000).Ok); // refill is capped at the capacity
        Assert.True(Handle(host, new EchoQuery(1), now: 60_000).Ok);
        Assert.True(Handle(host, new EchoQuery(1), now: 60_000).Ok);
        Assert.False(Handle(host, new EchoQuery(1), now: 60_000).Ok);
    }

    [Fact]
    public void ExpensiveQueriesCostMoreAndARefusedAskStillCostsOne()
    {
        var host = NewHost(new RemoteQueryLimits(Capacity: 7, RefillPerSecond: 1));
        Assert.True(Handle(host, new HeavyQuery(), now: 0).Ok); // 5 tokens, 2 left
        Assert.Equal(QueryWireCodes.RateLimited, Handle(host, new HeavyQuery(), seq: 2, now: 0).Code); // needs 5, has 2; the refused ask still costs its 1
        Assert.True(Handle(host, new EchoQuery(), now: 0).Ok); // the last token
    }

    [Fact]
    public void RateLimitIsPerSessionAndResetsWhenTheSessionEnds()
    {
        var host = NewHost(new RemoteQueryLimits(Capacity: 1, RefillPerSecond: 1));
        Assert.True(Handle(host, new EchoQuery(), session: "a").Ok);
        Assert.False(Handle(host, new EchoQuery(), session: "a").Ok);
        Assert.True(Handle(host, new EchoQuery(), session: "b").Ok);
        host.EndSession("a");
        Assert.True(Handle(host, new EchoQuery(), session: "a").Ok);
    }

    [Fact]
    public void ClockGoingBackwardsDoesNotRefill()
    {
        var host = NewHost(new RemoteQueryLimits(Capacity: 1, RefillPerSecond: 1));
        Assert.True(Handle(host, new EchoQuery(), now: 10_000).Ok);
        Assert.False(Handle(host, new EchoQuery(), now: 0).Ok);
        Assert.False(Handle(host, new EchoQuery(), now: 10_500).Ok);
        Assert.True(Handle(host, new EchoQuery(), now: 11_000).Ok); // a backwards jump must not leave a penalty behind
    }

    [Fact]
    public void RealCoreQueryThroughTheHost()
    {
        var core = new QueryRegistry(UncivJson.DefaultOptions);
        core.RegisterFromAssembly(typeof(QueryRegistry).Assembly);
        var host = new RemoteQueryHost(core, RemoteQueryPolicy.Default);
        var ok = host.Handle("s", player.CivName, new Query { QueryId = 5, QueryType = "empire.TopBar", Payload = JsonDocument.Parse("{}").RootElement }, context, 1, false, 0);
        Assert.True(ok.Ok);
        var denied = host.Handle("s", player.CivName, new Query { QueryId = 6, QueryType = "victory.Progress", Payload = JsonDocument.Parse("{}").RootElement }, context, 1, false, 0);
        Assert.Equal(QueryWireCodes.UnknownQuery, denied.Code);
    }

    // ---------------------------------------------------------------- client

    private sealed class Wire
    {
        public readonly List<Query> Sent = [];
        public bool Closed;

        public ValueTask Send(Message message, CancellationToken token)
        {
            if (Closed) throw new InvalidOperationException("closed");
            Sent.Add((Query)message);
            return ValueTask.CompletedTask;
        }
    }

    private RemoteQueryClient NewClient(Wire wire, int maxPending = 32) => new(registry, UncivJson.DefaultOptions, wire.Send, maxPending);

    [Fact]
    public async Task ClientQueryCompletesWhenTheAnswerArrivesInAnyOrder()
    {
        var wire = new Wire();
        var client = NewClient(wire);
        var first = client.QueryAsync(new EchoQuery(1));
        var second = client.QueryAsync(new EchoQuery(2));
        Assert.Equal(2, client.PendingCount);
        Assert.Equal(["t223.Echo", "t223.Echo"], wire.Sent.Select(it => it.QueryType));
        Assert.NotEqual(wire.Sent[0].QueryId, wire.Sent[1].QueryId);

        Assert.True(client.Complete(Answer(wire.Sent[1].QueryId, new EchoResult("B", 2))));
        Assert.Equal(new EchoResult("B", 2), (await second).Value);
        Assert.False(first.IsCompleted);
        Assert.True(client.Complete(Answer(wire.Sent[0].QueryId, new EchoResult("A", 1))));
        Assert.Equal(new EchoResult("A", 1), (await first).Value);
        Assert.Equal(0, client.PendingCount);
        Assert.False(client.Complete(Answer(wire.Sent[0].QueryId, new EchoResult("dup", 0)))); // duplicate or stray answers are ignored
    }

    private static QueryResult Answer(long id, object value) => new() { QueryId = id, Ok = true, Payload = JsonSerializer.SerializeToElement(value, UncivJson.DefaultOptions) };

    [Fact]
    public async Task ClientSurfacesHostFailureCodes()
    {
        var wire = new Wire();
        var client = NewClient(wire);
        var task = client.QueryAsync(new EchoQuery());
        client.Complete(new QueryResult { QueryId = wire.Sent[0].QueryId, Ok = false, Code = QueryWireCodes.RateLimited });
        var result = await task;
        Assert.False(result.Ok);
        Assert.Equal(QueryWireCodes.RateLimited, result.Code);
        Assert.Null(result.Value);

        var noCode = client.QueryAsync(new EchoQuery());
        client.Complete(new QueryResult { QueryId = wire.Sent[1].QueryId, Ok = false });
        Assert.Equal(QueryWireCodes.Rejected, (await noCode).Code);
    }

    [Fact]
    public async Task ClientFailsOnMalformedResult()
    {
        var wire = new Wire();
        var client = NewClient(wire);
        var wrong = client.QueryAsync(new EchoQuery());
        client.Complete(new QueryResult { QueryId = wire.Sent[0].QueryId, Ok = true, Payload = JsonDocument.Parse("[1]").RootElement.Clone() });
        Assert.Equal(QueryWireCodes.MalformedResult, (await wrong).Code);
        var missing = client.QueryAsync(new EchoQuery());
        client.Complete(new QueryResult { QueryId = wire.Sent[1].QueryId, Ok = true });
        Assert.Equal(QueryWireCodes.MalformedResult, (await missing).Code);
    }

    [Fact]
    public async Task ClientLimitsPendingQueries()
    {
        var wire = new Wire();
        var client = NewClient(wire, maxPending: 2);
        var a = client.QueryAsync(new EchoQuery(1));
        var b = client.QueryAsync(new EchoQuery(2));
        var c = await client.QueryAsync(new EchoQuery(3));
        Assert.Equal(QueryWireCodes.TooManyPending, c.Code);
        Assert.Equal(2, wire.Sent.Count);
        client.Complete(Answer(wire.Sent[0].QueryId, new EchoResult("A", 1)));
        await a;
        var d = client.QueryAsync(new EchoQuery(4)); // room again
        Assert.Equal(3, wire.Sent.Count);
        client.FailAll();
        await b;
        await d;
    }

    [Fact]
    public async Task FailAllAnswersEverythingAndASendOnAClosedSessionFailsAtOnce()
    {
        var wire = new Wire();
        var client = NewClient(wire);
        var a = client.QueryAsync(new EchoQuery(1));
        var b = client.QueryAsync(new EchoQuery(2));
        client.FailAll();
        Assert.Equal(QueryWireCodes.SessionClosed, (await a).Code);
        Assert.Equal(QueryWireCodes.SessionClosed, (await b).Code);
        Assert.Equal(0, client.PendingCount);

        wire.Closed = true;
        var late = await client.QueryAsync(new EchoQuery(3));
        Assert.Equal(QueryWireCodes.SessionClosed, late.Code);
        Assert.Equal(0, client.PendingCount);
    }

    [Fact]
    public async Task ClientCancellationReleasesTheSlotAndIgnoresALateAnswer()
    {
        var wire = new Wire();
        var client = NewClient(wire);
        using var source = new CancellationTokenSource();
        var task = client.QueryAsync(new EchoQuery(), source.Token);
        await source.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Equal(0, client.PendingCount);
        Assert.False(client.Complete(Answer(wire.Sent[0].QueryId, new EchoResult("late", 0))));
    }

    [Fact]
    public async Task ClientSendFailureOtherThanClosedPropagatesAndFreesTheSlot()
    {
        var client = new RemoteQueryClient(registry, UncivJson.DefaultOptions, (_, _) => throw new IOException("boom"));
        await Assert.ThrowsAsync<IOException>(() => client.QueryAsync(new EchoQuery()));
        Assert.Equal(0, client.PendingCount);
    }

    [Fact]
    public async Task ClientRefusesAnUnregisteredQuery()
    {
        var client = NewClient(new Wire());
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.QueryAsync(new MismatchedQuery()));
    }

    public sealed record MismatchedQuery : IQuery<EchoResult>
    {
        public string Type => "t223.Nope";

        public EchoResult Run(GameView view) => new("", 0);
    }

    [Fact]
    public async Task EndToEndOverTheWireWithTheHostOnTheOtherEnd()
    {
        var (clientSide, hostSide) = InMemoryTransport.CreatePair();
        var host = NewHost();
        var client = new RemoteQueryClient(registry, UncivJson.DefaultOptions, (m, t) => clientSide.SendAsync(ProtocolCodec.Encode(m), t));
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var hostLoop = Task.Run(async () =>
        {
            while (await hostSide.ReceiveAsync(stop.Token) is { } frame)
            {
                var query = (Query)ProtocolCodec.Decode(frame);
                await hostSide.SendAsync(ProtocolCodec.Encode(host.Handle("s", player.CivName, query, context, 1, false, 0)));
            }
        });
        var clientLoop = Task.Run(async () =>
        {
            while (await clientSide.ReceiveAsync(stop.Token) is { } frame)
                client.Complete((QueryResult)ProtocolCodec.Decode(frame));
        });

        var ok = await client.QueryAsync(new EchoQuery(9));
        Assert.Equal(new EchoResult(player.CivName, 9), ok.Value);
        var refused = await client.QueryAsync(new RejectQuery("x"));
        Assert.Equal(QueryWireCodes.Rejected, refused.Code);
        var denied = await client.QueryAsync(new DeniedQuery());
        Assert.Equal(QueryWireCodes.UnknownQuery, denied.Code);

        await clientSide.CloseAsync();
        await hostLoop;
        await hostSide.CloseAsync();
        await clientLoop;
    }
}
