namespace Unciv.Net.Identity;

/// <summary>
/// Failed-join back-off per remote key (an IP address; callers normalise IPv6 to its /64). An attempt is reserved
/// atomically before the credential is checked (<see cref="TryBeginAttempt"/>) and refunded by <see cref="RecordSuccess"/>,
/// so concurrent guesses are counted. After <see cref="MaxFailures"/> attempts the key is locked for
/// <see cref="BaseLockout"/>, doubling with every further attempt up to <see cref="MaxLockout"/>.
/// </summary>
public sealed class AttemptLimiter
{
    public const int MaxFailures = 5;
    public const int MaxTrackedKeys = 4096;
    public static readonly TimeSpan BaseLockout = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan MaxLockout = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan IdleForget = TimeSpan.FromMinutes(30);

    private readonly TimeProvider clock;
    private readonly Lock gate = new();
    private readonly Dictionary<string, State> states = new(StringComparer.Ordinal);

    public AttemptLimiter(TimeProvider clock) => this.clock = clock;

    private sealed class State
    {
        public int Failures;
        public DateTimeOffset LockedUntil;
        public DateTimeOffset LastFailure;
    }

    /// <summary>Read-only check (no reservation).</summary>
    public bool IsAllowed(string key)
    {
        lock (gate)
            return !states.TryGetValue(key, out var s) || clock.GetUtcNow() >= s.LockedUntil;
    }

    /// <summary>
    /// False while the key is locked out: refuse without evaluating the credential. Otherwise counts the attempt as a
    /// failure right now; <see cref="RecordSuccess"/> clears it. When the table is full the oldest entry is evicted, so a
    /// flood of distinct sources cannot switch limiting off for a new one.
    /// </summary>
    public bool TryBeginAttempt(string key)
    {
        lock (gate)
        {
            var now = clock.GetUtcNow();
            if (!states.TryGetValue(key, out var s) || (now >= s.LockedUntil && now - s.LastFailure > IdleForget))
            {
                if (s == null && states.Count >= MaxTrackedKeys) EvictOne(now);
                states[key] = s = new State();
            }
            else if (now < s.LockedUntil) return false;

            s.Failures++;
            s.LastFailure = now;
            if (s.Failures >= MaxFailures)
            {
                var exponent = Math.Min(s.Failures - MaxFailures, 16);
                s.LockedUntil = now + TimeSpan.FromTicks(Math.Min(BaseLockout.Ticks << exponent, MaxLockout.Ticks));
            }
            return true;
        }
    }

    public void RecordSuccess(string key)
    {
        lock (gate) states.Remove(key);
    }

    private void EvictOne(DateTimeOffset now)
    {
        string? victim = null;
        var oldest = DateTimeOffset.MaxValue;
        foreach (var (k, v) in states)
        {
            // prefer unlocked entries; among them the least recently failed
            var rank = now >= v.LockedUntil ? v.LastFailure : v.LockedUntil.AddYears(100);
            if (rank < oldest)
            {
                oldest = rank;
                victim = k;
            }
        }
        if (victim != null) states.Remove(victim);
    }
}
