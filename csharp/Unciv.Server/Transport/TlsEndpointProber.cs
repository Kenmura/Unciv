using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Unciv.Net;
using Unciv.Net.Identity;
using Unciv.Net.Standby;
using Unciv.Net.Transport;

namespace Unciv.Server.Transport;

/// <summary>
/// T281: the standby probe's dialler. One TCP connect and one TLS handshake, then (T282) one fixed, small <c>Hello</c> and a size-capped read of the answer: only <c>Welcome{standby: true}</c> counts, so a closed
/// port, an unrelated TLS service and a real host all read as "nothing usable". The answer is never relayed, so a host cannot be used to push traffic at a third party beyond a handshake and one small frame.
/// The certificate is never validated against a chain (hosts use self-signed keys); the caller compares the returned fingerprint with the announced one.
/// </summary>
public sealed class TlsEndpointProber : IEndpointProber
{
    public async Task<string?> PresentedFingerprintAsync(string host, int port, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);
        using var client = new TcpClient();
        string? presented = null;
        try
        {
            await client.ConnectAsync(host, port, limit.Token).ConfigureAwait(false);
            await using var ssl = new SslStream(client.GetStream(), leaveInnerStreamOpen: false, (_, certificate, _, _) =>
            {
                if (certificate == null) return false;
                presented = HostKey.Fingerprint(certificate as X509Certificate2 ?? new X509Certificate2(certificate));
                return true;
            });
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = host, EnabledSslProtocols = SslProtocols.Tls13 | SslProtocols.Tls12 }, limit.Token).ConfigureAwait(false);
            await LengthPrefixedFraming.WriteFrameAsync(ssl, ProtocolCodec.Encode(new Hello { Protocol = ProtocolVersion.Current, ClientVersion = "standby-probe", RulesetHash = "", ModList = [] }), limit.Token).ConfigureAwait(false);
            var answer = await LengthPrefixedFraming.ReadFrameAsync(ssl, limit.Token, StandbyListener.MaxFrameBytes).ConfigureAwait(false);
            return answer != null && ProtocolCodec.Decode(answer, StandbyListener.MaxFrameBytes) is Welcome { Standby: true } ? presented : null;
        }
        catch (Exception e) when (e is SocketException or IOException or AuthenticationException or OperationCanceledException or ObjectDisposedException or InvalidOperationException)
        {
            return null;
        }
    }
}
