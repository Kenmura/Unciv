using System.Globalization;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Unciv.Client.Net.Session;
using Unciv.Net;
using Unciv.Net.Identity;
using Unciv.Net.Persistence;
using Unciv.Net.Standby;
using Unciv.Server;
using Unciv.Server.Transport;

namespace Unciv.Client.Net.Hosting;

/// <summary>
/// T273: the designated successor's side of a graceful hand-off (design/HOST-MIGRATION.md section 2). Wire <see cref="Handler"/> as <see cref="ClientSessionOptions.OnHandoff"/> of the successor's own session:
/// when the old host sends <see cref="HostHandoff"/>, the standby copy under <c>standby/&lt;gameId&gt;/</c> is promoted with <see cref="InProcessHost.ResumeFromDirectory"/> at the new epoch (after proving it
/// reproduces the old host's final state hash), a listener is opened on the announced endpoint with this player's own certificate, and the player's session continues through an in-memory loopback with the
/// same key and seat. Everything it needs to refuse is checked first: the hand-off must name this player's key, the certificate must be the announced fingerprint, and the standby directory must exist.
/// </summary>
public sealed class StandbyPromotion(string standbyRoot, ClientIdentity identity, X509Certificate2 certificate, InProcessHostOptions? hostOptions = null)
{
    /// <summary>The promoted host once <see cref="Handler"/> succeeded (null before, or when the hand-off was not for this player).</summary>
    public InProcessHost? Host { get; private set; }

    /// <summary>The game directory of the promoted host (the standby directory).</summary>
    public string? Directory { get; private set; }

    /// <summary>For <see cref="ClientSessionOptions.OnHandoff"/>. Returns the loopback connector of the new host, or null when this player is not the successor (it then uses <see cref="ClientSessionOptions.ConnectEndpoint"/>).</summary>
    public Task<Func<CancellationToken, Task<ITransport>>?> Handler(HostHandoff handoff, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(handoff.Successor.KeyId, identity.PlayerKey, StringComparison.Ordinal)) return Task.FromResult<Func<CancellationToken, Task<ITransport>>?>(null);
        var host = Promote(handoff);
        return Task.FromResult<Func<CancellationToken, Task<ITransport>>?>(_ => Task.FromResult(host.ConnectLoopback()));
    }

    private InProcessHost Promote(HostHandoff handoff)
    {
        if (Host != null) throw new HostingException("This player already promoted a standby copy");
        var fingerprint = HostCertificate.GetFingerprint(certificate);
        if (!HostKey.Matches(fingerprint, handoff.Successor.Fingerprint)) throw new HostingException("This player's certificate is not the announced successor fingerprint");
        var directory = StandbyStore.DirectoryFor(standbyRoot, handoff.GameId);
        if (directory == null || !System.IO.Directory.Exists(directory)) throw new HostingException("There is no standby copy of this game");
        var port = PortOf(handoff.Successor.Endpoints);

        var proof = new PromotionProof
        {
            GameId = handoff.GameId,
            Epoch = handoff.Epoch,
            KeyId = identity.PlayerKey,
            Fingerprint = fingerprint,
            Signature = identity.SignPromotion(handoff.GameId, handoff.Epoch, fingerprint),
        };
        InProcessHost host;
        try
        {
            host = InProcessHost.ResumeFromDirectory(directory, identity.PlayerKey, (hostOptions ?? new InProcessHostOptions()) with
            {
                Promotion = new HostPromotion { Epoch = handoff.Epoch, Proof = proof, FinalSeq = handoff.FinalSeq, FinalHash = handoff.FinalHash, DepartedKeyId = handoff.DepartedKeyId },
            });
        }
        catch (Exception e) when (e is GameStoreException or IOException or System.Text.Json.JsonException)
        {
            throw new HostingException("The standby copy cannot be promoted: " + e.Message, e);
        }
        try
        {
            host.Listen(certificate, port);
        }
        catch (Exception e) when (e is SocketException or IOException or InvalidOperationException or System.Security.Cryptography.CryptographicException)
        {
            host.StopAsync().GetAwaiter().GetResult(); // the copy stays promoted: a retry with the same hand-off resumes it (the promotion is idempotent)
            throw new HostingException($"Port {port} cannot be used ({e.Message}).", e);
        }
        Host = host;
        Directory = directory;
        return host;
    }

    // The successor announced one port (every endpoint shares it).
    private static int PortOf(IReadOnlyList<string> endpoints)
    {
        var endpoint = endpoints[0];
        var separator = endpoint.LastIndexOf(':');
        if (separator <= 0 || !int.TryParse(endpoint.AsSpan(separator + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port is < 1024 or > 65535)
            throw new HostingException("The announced endpoint is not usable");
        return port;
    }
}
