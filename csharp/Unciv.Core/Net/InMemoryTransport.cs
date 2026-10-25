using System.Threading.Channels;

namespace Unciv.Net;

/// <summary>Loopback <see cref="ITransport"/> pair for tests and the in-process host.</summary>
public sealed class InMemoryTransport : ITransport
{
    private readonly Channel<string> outgoing;
    private readonly Channel<string> incoming;
    private bool closed;

    private InMemoryTransport(Channel<string> outgoing, Channel<string> incoming)
    {
        this.outgoing = outgoing;
        this.incoming = incoming;
    }

    public static (InMemoryTransport A, InMemoryTransport B) CreatePair()
    {
        var ab = Channel.CreateUnbounded<string>();
        var ba = Channel.CreateUnbounded<string>();
        return (new InMemoryTransport(ab, ba), new InMemoryTransport(ba, ab));
    }

    public bool IsOpen => !closed && !outgoing.Reader.Completion.IsCompleted;

    public ValueTask SendAsync(string frame, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsOpen || !outgoing.Writer.TryWrite(frame))
            throw new InvalidOperationException("Transport is closed");
        return ValueTask.CompletedTask;
    }

    public async ValueTask<string?> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        while (await incoming.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (incoming.Reader.TryRead(out var frame)) return frame;
        }
        return null;
    }

    public ValueTask CloseAsync()
    {
        closed = true;
        outgoing.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => CloseAsync();
}
