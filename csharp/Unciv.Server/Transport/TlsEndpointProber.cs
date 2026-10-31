using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Nodes;
using Unciv.Net;
using Unciv.Net.Identity;
using Unciv.Net.Relay;
using Unciv.Net.Standby;
using Unciv.Net.Transport;

namespace Unciv.Server.Transport;

/// <summary>T354: how to dial a relay the host agreed to: the relay as the player's settings know it (with its certificate pin, if any) and how its certificate is checked.</summary>
public sealed record RelayDialPlan(RelayEndpoint Endpoint, RelayDialOptions Dial);

/// <summary>
/// T281: the standby probe's dialler. One TCP connect and one TLS handshake, then (T282) one fixed, small <c>Hello</c> and a size-capped read of the answer: only <c>Welcome{standby: true}</c> counts, so a closed
/// port, an unrelated TLS service and a real host all read as "nothing usable". The answer is never relayed, so a host cannot be used to push traffic at a third party beyond a handshake and one small frame.
/// The certificate is never validated against a chain (hosts use self-signed keys); the caller compares the returned fingerprint with the announced one.
/// </summary>
public sealed class TlsEndpointProber(Func<RelayEndpoint, RelayDialPlan?>? relayResolver = null) : IEndpointProber
{
    public async Task<string?> PresentedFingerprintAsync(string host, int port, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);
        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync(host, port, limit.Token).ConfigureAwait(false);
            return await ProbeStreamAsync(client.GetStream(), host, limit.Token).ConfigureAwait(false);
        }
        catch (Exception e) when (e is SocketException or IOException or AuthenticationException or OperationCanceledException or ObjectDisposedException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// T354: the probe through the candidate's relay room. Only a relay the caller's resolver agrees to is dialled (the route is the candidate's choice, so an unknown relay is never contacted: that would
    /// hand the host's address to a stranger's server on a peer's say-so); the splice then carries exactly the direct probe. The relay never sees more than the room it was asked for.
    /// </summary>
    public async Task<string?> PresentedFingerprintViaRelayAsync(RelayRoute route, string expectedFingerprint, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (relayResolver == null || RelayRoutes.Normalize(route) is not { } normalized || RelayRoutes.EndpointOf(normalized) is not { } named || relayResolver(named) is not { } plan) return null;
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);
        try
        {
            await using var channel = await RelayChannel.ConnectAsync(plan.Endpoint, plan.Dial, limit.Token).ConfigureAwait(false);
            var join = new JsonObject { ["op"] = "join", ["v"] = RelayProtocol.Version, ["room"] = normalized.Room };
            if (HostKey.Normalize(expectedFingerprint) is { } fingerprint) join["fp"] = fingerprint;
            await channel.SendAsync(join, limit.Token).ConfigureAwait(false);
            RelayChannel.ThrowIfRefused(await channel.ReadAsync(limit.Token).ConfigureAwait(false), "probing " + normalized.Room);
            return await ProbeStreamAsync(channel.Stream, plan.Endpoint.Host, limit.Token).ConfigureAwait(false);
        }
        catch (Exception e) when (e is RelayException or SocketException or IOException or AuthenticationException or OperationCanceledException or ObjectDisposedException or InvalidOperationException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static async Task<string?> ProbeStreamAsync(Stream stream, string targetHost, CancellationToken cancellationToken)
    {
        string? presented = null;
        await using var ssl = new SslStream(stream, leaveInnerStreamOpen: false, (_, certificate, _, _) =>
        {
            if (certificate == null) return false;
            presented = HostKey.Fingerprint(certificate as X509Certificate2 ?? new X509Certificate2(certificate));
            return true;
        });
        await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = targetHost, EnabledSslProtocols = SslProtocols.Tls13 | SslProtocols.Tls12 }, cancellationToken).ConfigureAwait(false);
        await LengthPrefixedFraming.WriteFrameAsync(ssl, ProtocolCodec.Encode(new Hello { Protocol = ProtocolVersion.Current, ClientVersion = "standby-probe", RulesetHash = "", ModList = [] }), cancellationToken).ConfigureAwait(false);
        var answer = await LengthPrefixedFraming.ReadFrameAsync(ssl, cancellationToken, StandbyListener.MaxFrameBytes).ConfigureAwait(false);
        return answer != null && ProtocolCodec.Decode(answer, StandbyListener.MaxFrameBytes) is Welcome { Standby: true } ? presented : null;
    }
}
