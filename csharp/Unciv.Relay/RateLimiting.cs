using Unciv.Net.Turns;

namespace Unciv.Relay;

/// <summary>A token bucket on the injected clock: <see cref="Reserve"/> takes bytes at once and says how long the caller must wait before it may send them.</summary>
public sealed class TokenBucket(long ratePerSecond, long burst, IClock clock)
{
    private readonly Lock gate = new();
    private double tokens = burst;
    private long last = clock.NowMillis;

    /// <summary>Takes <paramref name="count"/> tokens (the bucket may go negative) and returns the milliseconds to wait until it is back at zero; 0 when the burst covered it.</summary>
    public long Reserve(long count)
    {
        lock (gate)
        {
            var now = clock.NowMillis;
            if (now > last) tokens = Math.Min(burst, tokens + (now - last) * (double)ratePerSecond / 1000.0);
            last = now;
            tokens -= count;
            return tokens >= 0 ? 0 : (long)Math.Ceiling(-tokens * 1000.0 / ratePerSecond);
        }
    }
}

/// <summary>A byte allowance over a fixed window (per room per day, global per month); the window restarts at the first use after it ended.</summary>
public sealed class ByteBudget(long limit, long windowMillis, IClock clock)
{
    private readonly Lock gate = new();
    private long used;
    private long windowStart = -1;

    public void Add(long bytes)
    {
        lock (gate)
        {
            Roll();
            used += bytes;
        }
    }

    public bool Exceeded
    {
        get
        {
            lock (gate)
            {
                Roll();
                return limit > 0 && used >= limit;
            }
        }
    }

    private void Roll()
    {
        var now = clock.NowMillis;
        if (windowStart >= 0 && now - windowStart < windowMillis) return;
        windowStart = now;
        used = 0;
    }
}

/// <summary>Counts events per key within a sliding window on the injected clock.</summary>
public sealed class KeyedWindow(int limit, long windowMillis, IClock clock)
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, Queue<long>> hits = new(StringComparer.Ordinal);

    /// <summary>Records an event; false (and nothing recorded) when the key already used its <c>limit</c> within the window.</summary>
    public bool TryHit(string key)
    {
        lock (gate)
        {
            var queue = Trim(key);
            if (queue.Count >= limit) return false;
            queue.Enqueue(clock.NowMillis);
            return true;
        }
    }

    /// <summary>Records an event unconditionally and returns how many the key has in the window.</summary>
    public int Hit(string key)
    {
        lock (gate)
        {
            var queue = Trim(key);
            queue.Enqueue(clock.NowMillis);
            return queue.Count;
        }
    }

    public void Cleanup()
    {
        lock (gate)
        {
            var now = clock.NowMillis;
            foreach (var key in hits.Keys.ToList())
            {
                var queue = hits[key];
                while (queue.Count > 0 && now - queue.Peek() >= windowMillis) queue.Dequeue();
                if (queue.Count == 0) hits.Remove(key);
            }
        }
    }

    private Queue<long> Trim(string key)
    {
        if (!hits.TryGetValue(key, out var queue)) hits[key] = queue = new Queue<long>();
        var now = clock.NowMillis;
        while (queue.Count > 0 && now - queue.Peek() >= windowMillis) queue.Dequeue();
        return queue;
    }
}
