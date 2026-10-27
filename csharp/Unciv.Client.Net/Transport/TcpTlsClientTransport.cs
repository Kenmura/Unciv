using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using Unciv.Net;
using Unciv.Net.Identity;
using Unciv.Net.Transport;

namespace Unciv.Client.Net.Transport;

/// <summary>
/// Client-side direct TCP + TLS 1.3 transport connecting to a host (SERVER.md 6.3, 6.4).
/// Verifies host key fingerprint against an expected pin (from invite) or handles TOFU via <see cref="KnownHosts"/>.
/// </summary>
public sealed class TcpTlsClientTransport : ITransport
{
    private readonly TcpClient tcpClient;
    private readonly Stream stream;
    private bool disposed;

    private TcpTlsClientTransport(TcpClient tcpClient, Stream stream)
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

    /// <summary>
    /// Connects to a host with TLS 1.3, validating the presented host key fingerprint.
    /// </summary>
    public static async Task<ITransport> ConnectAsync(
        string host,
        int port,
        string? expectedFingerprint = null,
        KnownHosts? knownHosts = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var connectTimeout = timeout ?? TimeSpan.FromSeconds(10);
        using var timeoutCts = new CancellationTokenSource(connectTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        var client = new TcpClient();
        client.NoDelay = true;

        try
        {
            await client.ConnectAsync(host, port, linkedCts.Token).ConfigureAwait(false);

            var sslStream = new SslStream(
                client.GetStream(),
                leaveInnerStreamOpen: false,
                (sender, certificate, chain, errors) =>
                {
                    if (certificate == null) return false;
                    var cert2 = certificate as System.Security.Cryptography.X509Certificates.X509Certificate2
                        ?? new System.Security.Cryptography.X509Certificates.X509Certificate2(certificate);
                    var presentedFingerprint = HostKey.Fingerprint(cert2);

                    // Direct invite verification: zero first-use window, MITM-proof
                    if (!string.IsNullOrEmpty(expectedFingerprint))
                    {
                        return HostKey.Matches(expectedFingerprint, presentedFingerprint);
                    }

                    // TOFU: trust-on-first-use with known-hosts pinning
                    if (knownHosts != null)
                    {
                        var trust = knownHosts.Check(host, presentedFingerprint);
                        if (trust == HostTrust.Mismatch) return false; // Hard stop
                        if (trust == HostTrust.Unknown)
                        {
                            knownHosts.Pin(host, presentedFingerprint);
                        }
                        return true;
                    }

                    return true;
                });

            var clientAuthOptions = new SslClientAuthenticationOptions
            {
                TargetHost = host,
                EnabledSslProtocols = SslProtocols.Tls13 | SslProtocols.Tls12
            };

            await sslStream.AuthenticateAsClientAsync(clientAuthOptions, linkedCts.Token).ConfigureAwait(false);
            return new TcpTlsClientTransport(client, sslStream);
        }
        catch
        {
            client.Close();
            throw;
        }
    }

    /// <summary>Connects to a host using a parsed <see cref="InviteCode"/>.</summary>
    public static Task<ITransport> ConnectAsync(
        InviteCode invite,
        KnownHosts? knownHosts = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        return ConnectAsync(
            invite.Host,
            invite.Port,
            expectedFingerprint: invite.Fingerprint,
            knownHosts: knownHosts,
            timeout: timeout,
            cancellationToken: cancellationToken);
    }
}
