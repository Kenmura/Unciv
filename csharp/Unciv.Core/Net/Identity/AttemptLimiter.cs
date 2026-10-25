namespace Unciv.Net.Identity;

/// <summary>
/// Failed-join back-off per remote key (an IP address). After <see cref="MaxFailures"/> failures the key is locked
/// for <see cref="BaseLockout"/>, doubling with every further failure up to <see cref="MaxLockout"/>. A success clears the key.
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

    /// <summary>False while the key is locked out; callers must refuse without evaluating the credential.</summary>
    public bool IsAllowed(string key)
    {
        lock (gate)
            return !states.TryGetValue(key, out var s) || clock.GetUtcNow() >= s.LockedUntil;
    }

    public void RecordFailure(string key)
    {
        lock (gate)
        {
            var now = clock.GetUtcNow();
            if (!states.TryGetValue(key, out var s))
            {
                if (states.Count >= MaxTrackedKeys) Prune(now);
                if (states.Count >= MaxTrackedKeys) return; // table saturated by distinct sources: stay bounded, the global session cap applies
                states[key] = s = new State();
            }
            s.Failures++;
            s.LastFailure = now;
            if (s.Failures >= MaxFailures)
            {
                var exponent = Math.Min(s.Failures - MaxFailures, 16);
                var lockout = TimeSpan.FromTicks(Math.Min(BaseLockout.Ticks << exponent, MaxLockout.Ticks));
                s.LockedUntil = now + lockout;
            }
        }
    }

    public void RecordSuccess(string key)
    {
        lock (gate) states.Remove(key);
    }

    private void Prune(DateTimeOffset now)
    {
        foreach (var stale in states.Where(p => now >= p.Value.LockedUntil && now - p.Value.LastFailure > IdleForget).Select(p => p.Key).ToList())
            states.Remove(stale);
    }
}
