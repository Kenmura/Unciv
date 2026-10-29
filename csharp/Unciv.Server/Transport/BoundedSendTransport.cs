using System.Text;
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
    private readonly CancellationTokenSource graceCancel = new();
    private readonly Task pump;
    private readonly Lock gate = new();
    private int queuedFrames;
    private long queuedBytes;
    private readonly TaskCompletionSource closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile bool closing;
    private volatile bool slowReader;
    private int closeStarted;

    public BoundedSendTransport(ITransport inner, int maxFrames = 256, long maxBytes = 64L * 1024 * 1024, TimeSpan? drainGrace = null)
    {
        this.inner = inner;
        this.maxFrames = maxFrames;
        this.maxBytes = maxBytes;
        this.drainGrace = drainGrace ?? DefaultDrainGrace;
        pump = PumpAsync();
    }

    /// <summary>Frames accepted by <see cref="SendAsync"/> that the inner transport has not finished writing (T272: standby replication paces itself on this).</summary>
    public int QueuedFrames
    {
        get
        {
            lock (gate) return queuedFrames;
        }
    }

    /// <summary>True once the peer was disconnected for not reading fast enough.</summary>
    public bool DisconnectedAsSlowReader => slowReader;

    public bool IsOpen => !closing && inner.IsOpen;

    public ValueTask SendAsync(string frame, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var size = Encoding.UTF8.GetByteCount(frame);
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

    /// <summary>Stops accepting sends, lets queued frames drain for up to the grace period, then closes the inner transport. Idempotent; concurrent callers wait for the close to finish.</summary>
    public async ValueTask CloseAsync()
    {
        if (Interlocked.Exchange(ref closeStarted, 1) != 0)
        {
            await closed.Task.ConfigureAwait(false);
            return;
        }
        try
        {
            lock (gate)
            {
                closing = true;
                queue.Writer.TryComplete();
            }
            // A slow reader or a dead pump is not drained; otherwise wait (bounded) for what is already queued.
            if (!slowReader && !pump.IsCompleted) await Task.WhenAny(pump, Task.Delay(drainGrace, graceCancel.Token)).ConfigureAwait(false);
            await graceCancel.CancelAsync().ConfigureAwait(false);
            await pumpStop.CancelAsync().ConfigureAwait(false);
            // The pump normally ends at once (its write was cancelled); an inner transport that ignores the token must not hang the caller.
            try { await pump.WaitAsync(drainGrace).ConfigureAwait(false); } catch (Exception) { /* the pump's own failure was already handled; closing is all that is left */ }
            await inner.CloseAsync().ConfigureAwait(false);
        }
        finally
        {
            closed.TrySetResult();
        }
    }

    public ValueTask DisposeAsync() => CloseAsync();

    private async ValueTask DisconnectSlowReaderAsync()
    {
        slowReader = true;
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
                        queuedBytes -= Encoding.UTF8.GetByteCount(frame);
                    }
                }
            }
        }
        catch (Exception)
        {
            // The peer's socket died (reset, half-open) or we were told to stop: nothing more will be sent. Close the inner transport so
            // its reader unblocks and the session is detached instead of leaking the socket.
            lock (gate) { closing = true; }
            queue.Writer.TryComplete();
            try { await inner.CloseAsync().ConfigureAwait(false); } catch (Exception) { /* already dead */ }
        }
    }
}
