using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;

namespace Unciv.Relay;

/// <summary>A preamble or control line longer than its cap.</summary>
internal sealed class LineTooLongException : Exception;

/// <summary>One accepted connection: the socket, the stream the line protocol runs on (TLS or plain) and the source network the limits key on.</summary>
internal sealed class RelayConn(TcpClient tcp, IPAddress address) : IDisposable
{
    private readonly SemaphoreSlim writeLock = new(1, 1);
    private int disposed;

    public TcpClient Tcp { get; } = tcp;

    public Stream Stream { get; set; } = tcp.GetStream();

    /// <summary>The client address after PROXY protocol handling.</summary>
    public IPAddress Address { get; set; } = address;

    public string Network => RelayProtocol.NetworkKey(Address);

    public string Ip => Address.ToString();

    /// <summary>Reads one line byte by byte (so no byte after it is consumed: what follows a join or accept is opaque). Null at end of stream before any byte.</summary>
    public async Task<string?> ReadLineAsync(int maxBytes, CancellationToken cancellationToken)
    {
        var bytes = new List<byte>(128);
        var one = new byte[1];
        while (true)
        {
            var read = await Stream.ReadAsync(one, cancellationToken).ConfigureAwait(false);
            if (read == 0) return bytes.Count == 0 ? null : Encoding.UTF8.GetString([.. bytes]);
            if (one[0] == (byte)'\n') return Encoding.UTF8.GetString([.. bytes]).TrimEnd('\r');
            if (bytes.Count >= maxBytes) throw new LineTooLongException();
            bytes.Add(one[0]);
        }
    }

    public async Task SendAsync(JsonObject message, CancellationToken cancellationToken = default)
    {
        var data = Encoding.UTF8.GetBytes(message.ToJsonString() + "\n");
        await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await Stream.WriteAsync(data, cancellationToken).ConfigureAwait(false);
            await Stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { writeLock.Release(); }
    }

    /// <summary>Sends the line and swallows a failed write: the peer is going away anyway.</summary>
    public async Task TrySendAsync(JsonObject message)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await SendAsync(message, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException or SocketException or InvalidOperationException) { }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        try { Stream.Dispose(); }
        catch (Exception e) when (e is IOException or ObjectDisposedException) { }
        Tcp.Dispose();
    }
}
