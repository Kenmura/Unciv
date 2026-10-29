using System.Threading.Channels;

using Unciv.Net;

namespace Unciv.Server.Transport;

/// <summary>
/// Decorator giving a transport a bounded outbound queue. <see cref="SendAsync"/> never waits on the peer: frames go to a queue that
/// a background pump writes to the inner transport. A peer that cannot keep up (queue over <c>maxFrames</c> or <c>maxBytes</c>) is a slow reader:
/// the transport is closed instead of letting it stall the sender or grow memory without bound. Sends are thread-safe and keep their order.
/// </summary>
public sealed class BoundedSendTransport : ITransport
{
    /// <summary>How long <see cref="CloseAsync"/> lets the pump drain already queued frames before the inner transport is closed anyway.</summary>
    public static readonly TimeSpan DefaultDrainGrace = TimeSpan.FromSeconds(1);

    private readonly ITransport inner;
    private readonly int maxFrames;
    private readonly long maxBytes;
    private readonly TimeSpan drainGrace;
    private readonly Channel<string> queue = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource pumpStop = new();
    private readonly Task pump;
    private readonly Lock gate = new();
    private int queuedFrames;
    private long queuedBytes;
    private volatile bool closing;

    public BoundedSendTransport(ITransport inner, int maxFrames = 256, long maxBytes = 64L * 1024 * 1024, TimeSpan? drainGrace = null)
    {
        this.inner = inner;
        this.maxFrames = maxFrames;
        this.maxBytes = maxBytes;
        this.drainGrace = drainGrace ?? DefaultDrainGrace;
        pump = PumpAsync();
    }

    /// <summary>True once the peer was disconnected for not reading fast enough.</summary>
    public bool DisconnectedAsSlowReader { get; private set; }

    public bool IsOpen => !closing && inner.IsOpen;

    public ValueTask SendAsync(string frame, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var size = frame.Length; // chars, a cheap lower bound of the UTF-8 size that avoids encoding twice
        bool overflow;
        lock (gate)
        {
            if (closing || !inner.IsOpen) throw new InvalidOperationException("Transport is closed");
            overflow = queuedFrames + 1 > maxFrames || queuedBytes + size > maxBytes;
            if (!overflow)
            {
                queuedFrames++;
                queuedBytes += size;
                queue.Writer.TryWrite(frame);
            }
        }
        if (overflow) return DisconnectSlowReaderAsync();
        return ValueTask.CompletedTask;
    }

    public ValueTask<string?> ReceiveAsync(CancellationToken cancellationToken = default) => inner.ReceiveAsync(cancellationToken);

    public async ValueTask CloseAsync()
    {
        lock (gate)
        {
            if (closing) return;
            closing = true;
            queue.Writer.TryComplete();
        }
        if (!DisconnectedAsSlowReader)
        {
            var drained = await Task.WhenAny(pump, Task.Delay(drainGrace)).ConfigureAwait(false);
            if (drained != pump) await pumpStop.CancelAsync().ConfigureAwait(false);
        }
        else
        {
            await pumpStop.CancelAsync().ConfigureAwait(false);
        }
        try { await pump.ConfigureAwait(false); } catch (OperationCanceledException) { }
        await inner.CloseAsync().ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => CloseAsync();

    private async ValueTask DisconnectSlowReaderAsync()
    {
        DisconnectedAsSlowReader = true;
        await CloseAsync().ConfigureAwait(false);
        throw new InvalidOperationException("Peer disconnected: outbound queue full");
    }

    private async Task PumpAsync()
    {
        try
        {
            while (await queue.Reader.WaitToReadAsync(pumpStop.Token).ConfigureAwait(false))
            {
                while (queue.Reader.TryRead(out var frame))
                {
                    await inner.SendAsync(frame, pumpStop.Token).ConfigureAwait(false);
                    lock (gate)
                    {
                        queuedFrames--;
                        queuedBytes -= frame.Length;
                    }
                }
            }
        }
        catch (Exception e) when (e is OperationCanceledException or InvalidOperationException or IOException or ObjectDisposedException)
        {
            // The inner transport died or we were told to stop: nothing more will be sent.
            lock (gate) { closing = true; }
            queue.Writer.TryComplete();
        }
    }
}
