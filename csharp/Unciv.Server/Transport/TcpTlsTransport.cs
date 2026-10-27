using System.Net.Sockets;
using Unciv.Net;
using Unciv.Net.Transport;

namespace Unciv.Server.Transport;

/// <summary>
/// Direct TCP + TLS 1.3 frame transport implementing <see cref="ITransport"/> (SERVER.md 6.3).
/// Frames are length-prefixed JSON UTF-8 payloads.
/// </summary>
public sealed class TcpTlsTransport : ITransport
{
    private readonly TcpClient tcpClient;
    private readonly Stream stream;
    private bool disposed;

    public TcpTlsTransport(TcpClient tcpClient, Stream stream)
    {
        this.tcpClient = tcpClient;
        this.stream = stream;
    }

    public bool IsOpen => tcpClient.Connected && !disposed;

    public ValueTask SendAsync(string frame, CancellationToken cancellationToken = default)
    {
        if (!IsOpen) throw new InvalidOperationException("Transport is closed");
        return LengthPrefixedFraming.WriteFrameAsync(stream, frame, cancellationToken);
    }

    public async ValueTask<string?> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        if (!IsOpen) return null;
        try
        {
            return await LengthPrefixedFraming.ReadFrameAsync(stream, cancellationToken).ConfigureAwait(false);
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
