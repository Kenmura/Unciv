using System.Net.Sockets;
using Unciv.Net;
using Unciv.Net.Transport;

namespace Unciv.Server.Transport;

/// <summary>
/// Direct TCP + TLS 1.3 frame transport implementing <see cref="ITransport"/> (SERVER.md 6.3).
/// Frames are length-prefixed JSON UTF-8 payloads.
/// </summary>
public sealed class TcpTlsTransport : ITransport, IPeerTransport
{
    private readonly TcpClient tcpClient;
    private readonly Stream stream;
    private bool disposed;

    public TcpTlsTransport(TcpClient tcpClient, Stream stream, string? peerId = null)
    {
        this.tcpClient = tcpClient;
        this.stream = stream;
        PeerId = peerId;
    }

    /// <summary>T338: the relay's opaque id of the joiner for a connection that came through a relay; null for a direct one.</summary>
    public string? PeerId { get; }

    /// <summary>The TLS version negotiated for this connection (null when the stream is not TLS).</summary>
    public System.Security.Authentication.SslProtocols? NegotiatedProtocol => (stream as System.Net.Security.SslStream)?.SslProtocol;

    public bool IsOpen => tcpClient.Connected && !disposed;

    public ValueTask SendAsync(string frame, CancellationToken cancellationToken = default)
    {
        if (!IsOpen) throw new InvalidOperationException("Transport is closed");
        return LengthPrefixedFraming.WriteFrameAsync(stream, frame, cancellationToken);
    }

    public ValueTask<string?> ReceiveAsync(CancellationToken cancellationToken = default) => ReceiveAsync(ProtocolVersion.MaxFrameBytes, cancellationToken);

    /// <summary>T282: like <see cref="ReceiveAsync(CancellationToken)"/> with a smaller cap, so an unauthenticated peer cannot make this end buffer more than <paramref name="maxMessageBytes"/> (an oversized frame throws <see cref="InvalidOperationException"/>).</summary>
    public async ValueTask<string?> ReceiveAsync(int maxMessageBytes, CancellationToken cancellationToken = default)
    {
        if (!IsOpen) return null;
        try
        {
            return await LengthPrefixedFraming.ReadFrameAsync(stream, cancellationToken, maxMessageBytes).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is EndOfStreamException or IOException)
        {
            return null;
        }
    }

    public ValueTask CloseAsync()
    {
        if (disposed) return ValueTask.CompletedTask;
        disposed = true;
        try
        {
            stream.Dispose();
            tcpClient.Close();
        }
        catch { }
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => CloseAsync();
}
