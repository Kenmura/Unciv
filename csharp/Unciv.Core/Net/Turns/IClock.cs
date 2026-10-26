namespace Unciv.Net.Turns;

/// <summary>
/// Time provider for the server layer and turn coordination (SERVER.md 4).
/// Injected so tests and deterministic simulations run without reading wall-clock time.
/// </summary>
public interface IClock
{
    /// <summary>Current time in Unix epoch milliseconds.</summary>
    long NowMillis { get; }
}

/// <summary>Default system clock using system UTC time.</summary>
public sealed class SystemClock : IClock
{
    public static readonly SystemClock Instance = new();

    // JUSTIFY: T177 system clock implementation for the server layer
#pragma warning disable RS0030
    public long NowMillis => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
#pragma warning restore RS0030
}

/// <summary>A fake clock whose time can be advanced deterministically in tests.</summary>
public sealed class ManualClock : IClock
{
    public ManualClock(long initialMillis = 0)
    {
        NowMillis = initialMillis;
    }

    public long NowMillis { get; set; }

    public void Advance(long millis)
    {
        if (millis < 0) throw new ArgumentOutOfRangeException(nameof(millis), "Cannot advance backwards");
        NowMillis += millis;
    }

    public void AdvanceSeconds(int seconds) => Advance(seconds * 1000L);
}
