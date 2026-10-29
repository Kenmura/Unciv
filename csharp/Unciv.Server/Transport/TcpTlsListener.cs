using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Unciv.Net;

namespace Unciv.Server.Transport;

/// <summary>
/// Dual-stack TCP listener wrapping accepted connections in TLS 1.3 (SERVER.md 6.3, 6.4).
/// </summary>
public sealed class TcpTlsListener : IAsyncDisposable
{
    private readonly TcpListener listener;
    private readonly X509Certificate2 serverCertificate;
    private readonly TimeSpan handshakeTimeout;
    private bool disposed;

    public TcpTlsListener(
        X509Certificate2 serverCertificate,
        int port = 47821,
        TimeSpan? handshakeTimeout = null)
    {
        this.serverCertificate = serverCertificate;
        this.handshakeTimeout = handshakeTimeout ?? TimeSpan.FromSeconds(5);

        // IPv6 dual-stack listener accepting both IPv4 and IPv6 connections
        listener = new TcpListener(IPAddress.IPv6Any, port);
        listener.Server.DualMode = true;
    }

    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

    public X509Certificate2 ServerCertificate => serverCertificate;

    public void Start()
    {
        listener.Start();
    }

    public void Stop()
    {
        disposed = true;
        try { listener.Stop(); } catch { }
    }

    /// <summary>
    /// Accepts a client connection, performs TLS handshake as server, and returns an <see cref="ITransport"/>.
    /// Enforces handshake timeout.
    /// </summary>
    public async Task<ITransport> AcceptTransportAsync(CancellationToken cancellationToken = default) =>
        await HandshakeAsync(await AcceptTcpAsync(cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);

    /// <summary>T265: accepts the raw connection only, so a caller can run each TLS handshake in its own task and a silent client cannot stall the others.</summary>
    public async Task<TcpClient> AcceptTcpAsync(CancellationToken cancellationToken = default)
    {
        if (disposed) throw new ObjectDisposedException(nameof(TcpTlsListener));
        var tcpClient = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
        tcpClient.NoDelay = true;
        return tcpClient;
    }

    /// <summary>The server-side TLS handshake on an accepted connection (closes it on failure).</summary>
    public async Task<ITransport> HandshakeAsync(TcpClient tcpClient, CancellationToken cancellationToken = default)
    {

        var sslStream = new SslStream(tcpClient.GetStream(), leaveInnerStreamOpen: false);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(handshakeTimeout);

        try
        {
            var authOptions = new SslServerAuthenticationOptions
            {
                ServerCertificate = serverCertificate,
                EnabledSslProtocols = SslProtocols.Tls13 | SslProtocols.Tls12,
                ClientCertificateRequired = false
            };

            await sslStream.AuthenticateAsServerAsync(authOptions, cts.Token).ConfigureAwait(false);
            return new TcpTlsTransport(tcpClient, sslStream);
        }
        catch
        {
            sslStream.Dispose();
            tcpClient.Close();
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        Stop();
        return ValueTask.CompletedTask;
    }
}
