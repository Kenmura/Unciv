using System.Text.Json;
using Unciv.Queries;

namespace Unciv.Net.Remote;

/// <summary>The outcome of one remote query. On failure <see cref="Code"/> is a <see cref="QueryWireCodes"/> value and <see cref="Value"/> is default.</summary>
public sealed record RemoteQueryResult<T>(bool Ok, string Code, T? Value)
{
    public static RemoteQueryResult<T> Fail(string code) => new(false, code, default);
}

/// <summary>
/// Client side of remote queries (T223): the async API a session exposes, with no thread or transport of its own. The session passes a send delegate and
/// feeds every incoming <see cref="QueryResult"/> to <see cref="Complete"/>; on disconnect it calls <see cref="FailAll"/>.
/// Not thread-safe by itself: call it from one context (the session's receive loop and its callers share a synchronization context), or lock outside.
/// </summary>
public sealed class RemoteQueryClient(QueryRegistry registry, JsonSerializerOptions options, Func<Message, CancellationToken, ValueTask> send, int maxPending = 32)
{
    private readonly Dictionary<long, TaskCompletionSource<QueryResult>> pending = [];
    private long nextId = 1;

    public int PendingCount
    {
        get { lock (pending) return pending.Count; }
    }

    public async Task<RemoteQueryResult<TResult>> QueryAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default) where TResult : notnull
    {
        var envelope = registry.ToEnvelope("", query); // validates registration; the viewer is the host's business
        TaskCompletionSource<QueryResult> source = new(TaskCreationOptions.RunContinuationsAsynchronously);
        long id;
        lock (pending)
        {
            if (pending.Count >= maxPending) return RemoteQueryResult<TResult>.Fail(QueryWireCodes.TooManyPending);
            id = nextId++;
            pending[id] = source;
        }
        using var registration = cancellationToken.Register(() =>
        {
            bool removed;
            lock (pending) removed = pending.Remove(id);
            if (removed) source.TrySetCanceled(cancellationToken);
        });
        try
        {
            await send(new Query { QueryId = id, QueryType = envelope.Type, Payload = envelope.Payload }, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            lock (pending) pending.Remove(id);
            return RemoteQueryResult<TResult>.Fail(QueryWireCodes.SessionClosed);
        }
        catch
        {
            lock (pending) pending.Remove(id);
            throw;
        }

        var answer = await source.Task.ConfigureAwait(false);
        if (!answer.Ok) return RemoteQueryResult<TResult>.Fail(answer.Code ?? QueryWireCodes.Rejected);
        try
        {
            var value = answer.Payload is { } payload ? payload.Deserialize<TResult>(options) : default;
            return value == null ? RemoteQueryResult<TResult>.Fail(QueryWireCodes.MalformedResult) : new RemoteQueryResult<TResult>(true, "", value);
        }
        catch (Exception e) when (e is JsonException or NotSupportedException)
        {
            return RemoteQueryResult<TResult>.Fail(QueryWireCodes.MalformedResult);
        }
    }

    /// <summary>Delivers a host answer. False when no query is waiting for that id (a duplicate or an answer to a cancelled query): ignore it.</summary>
    public bool Complete(QueryResult result)
    {
        TaskCompletionSource<QueryResult>? source;
        lock (pending)
        {
            if (!pending.Remove(result.QueryId, out source)) return false;
        }
        return source.TrySetResult(result);
    }

    /// <summary>Answers every waiting query with a failure (connection lost, session closed).</summary>
    public void FailAll(string code = QueryWireCodes.SessionClosed)
    {
        List<KeyValuePair<long, TaskCompletionSource<QueryResult>>> all;
        lock (pending)
        {
            all = [.. pending];
            pending.Clear();
        }
        foreach (var (id, source) in all.OrderBy(it => it.Key))
            source.TrySetResult(new QueryResult { QueryId = id, Ok = false, Code = code });
    }
}
