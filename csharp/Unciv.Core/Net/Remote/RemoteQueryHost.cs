using System.Text;
using Unciv.Queries;

namespace Unciv.Net.Remote;

/// <summary>Per-session token bucket (T223). <see cref="Capacity"/> tokens, refilled at <see cref="RefillPerSecond"/>; an expensive query costs several.</summary>
public sealed record RemoteQueryLimits(int Capacity = 40, int RefillPerSecond = 20, int MaxCacheEntries = 256);

/// <summary>
/// Host-side dispatch of <see cref="Query"/> messages (T223). It is called on the game actor, so a query sees one consistent state and never runs
/// beside a command. It owns no clock and no thread: the caller passes the current time, the view sequence number and whether the host is resolving a turn.
/// <list type="bullet">
/// <item>Order of checks: resolving (<c>turn-resolving</c>) -> exposure policy (denied looks like an unknown query) -> rate limit -> cache -> run.</item>
/// <item>Every message costs one token; a query that has to run costs its policy cost. A cache hit costs only the one token.</item>
/// <item>The cache holds results for one view sequence number (any command, also a server command, invalidates it); queries are pure, so equal asks give equal answers.</item>
/// <item>Failures are collapsed to <see cref="QueryWireCodes"/> with no text: a query's own code or message (for example "no such unit" against "not yours") would be an existence oracle for hidden state.</item>
/// </list>
/// </summary>
public sealed class RemoteQueryHost(QueryRegistry registry, RemoteQueryPolicy policy, RemoteQueryLimits? limits = null)
{
    private const long MilliTokens = 1000;
    private readonly RemoteQueryLimits limits = limits ?? new RemoteQueryLimits();
    private readonly Dictionary<string, Bucket> buckets = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Viewer, string Type, string Payload), QueryResult> cache = [];
    private long cacheSeq = long.MinValue;

    public int CachedResults => cache.Count;

    /// <summary>Forget a session's rate-limit state (call when it disconnects).</summary>
    public void EndSession(string sessionId) => buckets.Remove(sessionId);

    /// <param name="sessionId">The authenticated session, for the rate limit.</param>
    /// <param name="viewerCivName">The civ the session is bound to; never taken from the message.</param>
    /// <param name="viewSeq">Sequence number of the live state; results are cached per value.</param>
    /// <param name="resolving">True while the host resolves a turn (state is changing under the engine).</param>
    /// <param name="query">The decoded message.</param>
    /// <param name="context">The live game.</param>
    /// <param name="nowMs">The host's clock, for the rate limit.</param>
    public QueryResult Handle(string sessionId, string viewerCivName, Query query, QueryContext context, long viewSeq, bool resolving, long nowMs)
    {
        long id = query.QueryId;
        var bucket = BucketFor(sessionId, nowMs);
        if (!bucket.TryTake(MilliTokens)) return Fail(id, QueryWireCodes.RateLimited);
        if (resolving) return Fail(id, QueryWireCodes.TurnResolving);

        string type = query.QueryType;
        var exposure = policy.Get(type);
        if (!exposure.Allowed || !registry.RegisteredTypes.Contains(type)) return Fail(id, QueryWireCodes.UnknownQuery);

        if (viewSeq != cacheSeq)
        {
            cache.Clear();
            cacheSeq = viewSeq;
        }
        var key = (viewerCivName, type, query.Payload.ValueKind == System.Text.Json.JsonValueKind.Undefined ? "" : query.Payload.GetRawText());
        if (cache.TryGetValue(key, out var cached)) return cached with { QueryId = id };

        if (!bucket.TryTake((exposure.Cost - 1) * MilliTokens)) return Fail(id, QueryWireCodes.RateLimited);

        var response = registry.Execute(new QueryEnvelope(type, viewerCivName, query.Payload), context);
        var result = ToWire(id, response);
        if (result.Code != QueryWireCodes.InternalError)
        {
            if (cache.Count >= limits.MaxCacheEntries) cache.Clear();
            cache[key] = result;
        }
        return result;
    }

    private static QueryResult ToWire(long id, QueryResponse response)
    {
        if (response.Ok && response.Result is { } payload)
        {
            if (Encoding.UTF8.GetByteCount(payload.GetRawText()) > ProtocolVersion.MaxFrameBytes - 1024) return Fail(id, QueryWireCodes.ResultTooLarge);
            return new QueryResult { QueryId = id, Ok = true, Payload = payload };
        }
        string code = response.Code switch
        {
            QueryCodes.UnknownQuery => QueryWireCodes.UnknownQuery,
            QueryCodes.MalformedQuery => QueryWireCodes.MalformedQuery,
            QueryCodes.InternalError => QueryWireCodes.InternalError,
            _ => QueryWireCodes.Rejected // the query's own codes, and unknown-viewer
        };
        return Fail(id, code);
    }

    private static QueryResult Fail(long id, string code) => new() { QueryId = id, Ok = false, Code = code };

    private Bucket BucketFor(string sessionId, long nowMs)
    {
        if (!buckets.TryGetValue(sessionId, out var bucket))
        {
            bucket = new Bucket(limits.Capacity * MilliTokens, limits.RefillPerSecond, nowMs);
            buckets[sessionId] = bucket;
        }
        bucket.Refill(nowMs);
        return bucket;
    }

    private sealed class Bucket
    {
        private readonly long capacity;
        private readonly long refillMilliTokensPerMs;
        private long tokens;
        private long lastMs;

        public Bucket(long capacity, long refillMilliTokensPerMs, long startMs)
        {
            this.capacity = capacity;
            this.refillMilliTokensPerMs = refillMilliTokensPerMs;
            tokens = capacity;
            lastMs = startMs;
        }

        public void Refill(long nowMs)
        {
            long elapsed = Math.Max(0, nowMs - lastMs);
            if (nowMs > lastMs) lastMs = nowMs;
            tokens = Math.Min(capacity, tokens + elapsed * refillMilliTokensPerMs);
        }

        public bool TryTake(long amount)
        {
            if (tokens < amount) return false;
            tokens -= amount;
            return true;
        }
    }
}
