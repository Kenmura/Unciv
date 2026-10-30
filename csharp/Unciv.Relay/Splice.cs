using System.Net.Security;
using System.Net.Sockets;
using Unciv.Net.Turns;

namespace Unciv.Relay;

/// <summary>
/// One joiner connection spliced to the host's data connection: two pumps with 16 KiB buffers and back-pressure (a slow side blocks only its own pump).
/// Caps: bytes per second per splice (token bucket), per room per day and globally, idle seconds, a write stuck for the idle cap, and a first-bytes check
/// that the joiner starts with a TLS record (RELAY.md section 6, A2).
/// </summary>
internal sealed class Splice(RelayConn joiner, RelayConn host, Room room, string joinerNetwork, TokenBucket bucket, TokenBucket? egress, ByteBudget? monthly, IClock clock, long startedAt)
{
    private const int BufferSize = 16 * 1024;
    private readonly CancellationTokenSource cancel = new();
    private long lastActivity = startedAt;
    private long stuckSince = -1;
    private int tlsChecked;
    private long bytes;

    public Room Room { get; } = room;

    public string JoinerNetwork { get; } = joinerNetwork;

    public long StartedAt { get; } = startedAt;

    public long Bytes => Interlocked.Read(ref bytes);

    public string? CloseReason { get; private set; }

    /// <summary>True once the first two joiner bytes were seen to be a TLS record header.</summary>
    public bool TlsSeen => Volatile.Read(ref tlsChecked) == 2;

    public long LastActivity => Interlocked.Read(ref lastActivity);

    /// <summary>When a write began that has not finished, else -1.</summary>
    public long StuckSince => Interlocked.Read(ref stuckSince);

    public async Task RunAsync()
    {
        var up = PumpAsync(joiner, host, fromJoiner: true);
        var down = PumpAsync(host, joiner, fromJoiner: false);
        var first = await Task.WhenAny(up, down).ConfigureAwait(false);
        // a clean end of one direction is a half-close: the other may still drain until it ends or the idle cap closes it; a failure ends both
        if (first.IsFaulted || first.IsCanceled || CloseReason != null) Close(CloseReason ?? "error");
        try { await Task.WhenAll(up, down).ConfigureAwait(false); }
        catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException or SocketException or InvalidOperationException) { }
        Close(CloseReason ?? "ended");
    }

    public void Close(string reason)
    {
        CloseReason ??= reason;
        try { cancel.Cancel(); }
        catch (ObjectDisposedException) { }
        joiner.Dispose();
        host.Dispose();
    }

    private async Task PumpAsync(RelayConn from, RelayConn to, bool fromJoiner)
    {
        var buffer = new byte[BufferSize];
        try
        {
            while (true)
            {
                var read = await from.Stream.ReadAsync(buffer, cancel.Token).ConfigureAwait(false);
                if (read == 0) break;
                Interlocked.Exchange(ref lastActivity, clock.NowMillis);
                if (fromJoiner && !TlsSeen && !LooksLikeTls(buffer, read))
                {
                    Close("not-tls");
                    return;
                }
                var wait = Math.Max(bucket.Reserve(read), egress?.Reserve(read) ?? 0);
                if (wait > 0) await Task.Delay(TimeSpan.FromMilliseconds(wait), cancel.Token).ConfigureAwait(false);
                Interlocked.Exchange(ref stuckSince, clock.NowMillis);
                await to.Stream.WriteAsync(buffer.AsMemory(0, read), cancel.Token).ConfigureAwait(false);
                await to.Stream.FlushAsync(cancel.Token).ConfigureAwait(false);
                Interlocked.Exchange(ref stuckSince, -1);
                Interlocked.Exchange(ref lastActivity, clock.NowMillis);
                Interlocked.Add(ref bytes, read);
                Room.Daily.Add(read);
                monthly?.Add(read);
            }
            await HalfCloseAsync(to).ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException or SocketException or InvalidOperationException)
        {
            Close(CloseReason ?? "error");
        }
    }

    /// <summary>Passes the end of one direction on: the outer TLS close_notify (or a TCP FIN on a plain connection), leaving the other direction open.</summary>
    private async Task HalfCloseAsync(RelayConn to)
    {
        try
        {
            if (to.Stream is SslStream ssl) await ssl.ShutdownAsync().ConfigureAwait(false);
            else to.Tcp.Client.Shutdown(SocketShutdown.Send);
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or SocketException or InvalidOperationException) { }
    }

    /// <summary>Tracks the TLS record header (0x16 0x03) across reads that may carry a single byte.</summary>
    private bool LooksLikeTls(byte[] data, int count)
    {
        for (var i = 0; i < count && tlsChecked < 2; i++)
        {
            var expected = tlsChecked == 0 ? (byte)0x16 : (byte)0x03;
            if (data[i] != expected) return false;
            tlsChecked++;
        }
        return true;
    }
}
