namespace Unciv.Net;

/// <summary>
/// A bidirectional, ordered, reliable stream of text frames (one JSON message each). The TCP+TLS implementation
/// is a later ticket; this interface is all the protocol layer depends on.
/// </summary>
public interface ITransport : IAsyncDisposable
{
    bool IsOpen { get; }

    /// <summary>Queues one frame for the peer. Throws <see cref="InvalidOperationException"/> when the transport is closed.</summary>
    ValueTask SendAsync(string frame, CancellationToken cancellationToken = default);

    /// <summary>The next frame from the peer, or null once the peer closed and everything sent was delivered.</summary>
    ValueTask<string?> ReceiveAsync(CancellationToken cancellationToken = default);

    /// <summary>Closes this end; further sends fail and the peer sees end-of-stream after draining. Idempotent.</summary>
    ValueTask CloseAsync();
}
