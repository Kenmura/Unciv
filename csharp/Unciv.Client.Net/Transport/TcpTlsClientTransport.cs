using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text.Json.Nodes;
using Unciv.Net;
using Unciv.Net.Identity;
using Unciv.Net.Relay;
using Unciv.Net.Transport;
using Unciv.Server.Transport;

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
            return await HandshakeAsync(client, client.GetStream(), host, pinKey: host, expectedFingerprint, knownHosts, linkedCts.Token).ConfigureAwait(false);
        }
        catch
        {
            client.Close();
            throw;
        }
    }

    /// <summary>The end-to-end TLS client handshake over <paramref name="inner"/> (the connection's own stream, or the relay's outer session), pinned by invite or by <paramref name="knownHosts"/>.</summary>
    private static async Task<ITransport> HandshakeAsync(TcpClient owner, Stream inner, string targetHost, string pinKey, string? expectedFingerprint, KnownHosts? knownHosts, CancellationToken token, bool pinUnknown = true, System.Runtime.CompilerServices.StrongBox<bool>? rejected = null)
    {
        var sslStream = new SslStream(
            inner,
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
                    var matches = HostKey.Matches(expectedFingerprint, presentedFingerprint);
                    if (!matches && rejected != null) rejected.Value = true;
                    return matches;
                }

                // TOFU: trust-on-first-use with known-hosts pinning
                if (knownHosts != null)
                {
                    var trust = knownHosts.Check(pinKey, presentedFingerprint);
                    if (trust == HostTrust.Mismatch)
                    {
                        if (rejected != null) rejected.Value = true;
                        return false; // Hard stop
                    }
                    if (trust == HostTrust.Unknown && pinUnknown)
                    {
                        knownHosts.Pin(pinKey, presentedFingerprint);
                    }
                    return true;
                }

                return true;
            });

        await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
        {
            TargetHost = targetHost,
            EnabledSslProtocols = SslProtocols.Tls13 | SslProtocols.Tls12
        }, token).ConfigureAwait(false);
        return new TcpTlsClientTransport(owner, sslStream);
    }

    /// <summary>
    /// T338: connects to the host of <paramref name="room"/> through <paramref name="relay"/> (design/RELAY.md): dials out to the relay, asks for a splice to the room's host, then runs the same
    /// end-to-end TLS handshake as a direct connection over it, pinned to <paramref name="expectedFingerprint"/> (a relay invite) or trusting on first use through <paramref name="knownHosts"/>
    /// under <c>relayhost:port/room</c>. Nothing of the game is sent before the pin is checked. The relay never sees game data.
    /// </summary>
    /// <exception cref="RelayException">the relay is unreachable or refuses (unknown room, host gone, room full ...), or the host's key is not the pinned one (<c>host-key-mismatch</c>).</exception>
    public static async Task<ITransport> ConnectViaRelayAsync(
        RelayEndpoint relay,
        string room,
        string? expectedFingerprint = null,
        KnownHosts? knownHosts = null,
        RelayDialOptions? dial = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var canonical = RelayProtocol.NormalizeRoom(room) ?? throw new RelayException(RelayCodes.RoomUnknown, "That is not a room code.");
        var shownRoom = RelayProtocol.FormatRoom(canonical);
        using var timeoutCts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(30));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        var channel = await RelayChannel.ConnectAsync(relay, dial ?? new RelayDialOptions(), linked.Token).ConfigureAwait(false);
        var rejected = new System.Runtime.CompilerServices.StrongBox<bool>();
        try
        {
            try
            {
                var join = new JsonObject { ["op"] = "join", ["v"] = RelayProtocol.Version, ["room"] = shownRoom };
                if (HostKey.Normalize(expectedFingerprint) is { } fingerprint) join["fp"] = fingerprint;
                await channel.SendAsync(join, linked.Token).ConfigureAwait(false);
                RelayChannel.ThrowIfRefused(await channel.ReadAsync(linked.Token).ConfigureAwait(false), "joining " + shownRoom);
                var pinKey = KnownHosts.RelayAddress(relay.Key, shownRoom);
                return await HandshakeAsync(channel.Tcp, channel.Stream, relay.Host, pinKey, expectedFingerprint, knownHosts, linked.Token, pinUnknown: false, rejected: rejected).ConfigureAwait(false); // the session asks the player before anything is pinned (TOFU)
            }
            catch (System.Security.Authentication.AuthenticationException e) when (rejected.Value)
            {
                throw new RelayException("host-key-mismatch", "The host's key is not the one this invite or earlier visit pinned.", inner: e);
            }
            catch (Exception e) when (e is IOException or System.Net.Sockets.SocketException or System.Text.Json.JsonException)
            {
                throw new RelayException(RelayCodes.Failed, "The connection through the relay broke: " + e.Message, inner: e);
            }
        }
        catch
        {
            await channel.DisposeAsync().ConfigureAwait(false);
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
