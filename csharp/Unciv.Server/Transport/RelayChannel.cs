using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Unciv.Net.Identity;
using Unciv.Net.Relay;

namespace Unciv.Server.Transport;

/// <summary>
/// T338: a relay refused or could not be reached. <see cref="Code"/> is a stable word (<see cref="RelayCodes"/>) the join screen turns into a sentence; <see cref="RelayCode"/> is the relay's own
/// word for a refusal (<c>room-unknown</c>, <c>host-gone</c> ...), null when the relay never answered.
/// </summary>
public sealed class RelayException(string code, string message, string? relayCode = null, Exception? inner = null) : Exception(message, inner)
{
    public string Code { get; } = code;

    public string? RelayCode { get; } = relayCode;
}

/// <summary>The words a failed relay attempt can end with (client side: <c>ClientSession</c> reports them as the end reason).</summary>
public static class RelayCodes
{
    public const string Unreachable = "relay-unreachable";
    public const string CertificateRejected = "relay-certificate";
    public const string RoomUnknown = "relay-room-unknown";
    public const string HostGone = "relay-host-gone";
    public const string RoomFull = "relay-room-full";
    public const string RateLimited = "relay-rate-limited";
    public const string Banned = "relay-banned";
    public const string RelayFull = "relay-full";
    public const string TooOld = "relay-too-old";
    public const string WrongHost = "relay-wrong-host";
    public const string HostTimeout = "relay-host-timeout";
    public const string Failed = "relay-failed";

    /// <summary>T339: the relay runs with <c>--listing off</c>: it has no directory.</summary>
    public const string ListingOff = "relay-listing-off";

    /// <summary>T339: the relay accepted the connection but did not answer a directory request in time.</summary>
    public const string NoAnswer = "relay-no-answer";

    /// <summary>Maps the relay's wire code to ours.</summary>
    public static string FromWire(string? wire) => wire switch
    {
        "room-unknown" => RoomUnknown,
        "host-gone" => HostGone,
        "room-full" => RoomFull,
        "rate-limited" => RateLimited,
        "banned" => Banned,
        "relay-full" => RelayFull,
        "unsupported-version" => TooOld,
        "wrong-host" => WrongHost,
        "host-timeout" => HostTimeout,
        _ => Failed,
    };
}

/// <summary>How to reach a relay. <see cref="UseTls"/> is off only for a relay started with <c>--tls none</c> on loopback in tests.</summary>
public sealed record RelayDialOptions
{
    public bool UseTls { get; init; } = true;

    /// <summary>The player accepted this relay in a prompt although its certificate is neither valid for its name nor pinned: the inner TLS, not this one, authenticates the host (design/RELAY.md section 8).</summary>
    public bool TrustAnyCertificate { get; init; }

    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);
}

/// <summary>
/// T338: one connection to a relay's tunnel port after the outer TLS and the relay's challenge line (design/RELAY.md section 3). It sends and reads the JSON preamble lines; after a successful
/// <c>join</c> or <c>accept</c> reply, <see cref="Stream"/> carries opaque bytes (the inner TLS) and <see cref="Tcp"/> is the connection that owns them. This type only ever dials the relay URL the
/// player or operator typed, never an address a peer supplied.
/// </summary>
public sealed class RelayChannel : IAsyncDisposable
{
    private const int MaxReplyBytes = 64 * 1024;

    private RelayChannel(TcpClient tcp, Stream stream, string challenge)
    {
        Tcp = tcp;
        Stream = stream;
        Challenge = challenge;
    }

    public TcpClient Tcp { get; }

    public Stream Stream { get; }

    /// <summary>The fresh challenge the relay sent on this connection (signed by <c>host</c> and <c>accept</c>).</summary>
    public string Challenge { get; }

    /// <exception cref="RelayException">the relay cannot be reached, its certificate is refused, or it does not speak the protocol.</exception>
    public static async Task<RelayChannel> ConnectAsync(RelayEndpoint endpoint, RelayDialOptions options, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.ConnectTimeout);
        var tcp = new TcpClient { NoDelay = true };
        try
        {
            try
            {
                await tcp.ConnectAsync(endpoint.Host, endpoint.Port, timeout.Token).ConfigureAwait(false);
                tcp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                Stream stream = tcp.GetStream();
                if (options.UseTls)
                {
                    var ssl = new SslStream(stream, leaveInnerStreamOpen: false, (_, certificate, _, errors) => AcceptRelayCertificate(endpoint, options, certificate, errors));
                    await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                    {
                        TargetHost = endpoint.Host,
                        EnabledSslProtocols = SslProtocols.Tls13 | SslProtocols.Tls12,
                        ApplicationProtocols = [new SslApplicationProtocol(RelayProtocol.Alpn)],
                    }, timeout.Token).ConfigureAwait(false);
                    stream = ssl;
                }
                var line = await ReadLineAsync(stream, timeout.Token).ConfigureAwait(false)
                    ?? throw new RelayException(RelayCodes.Failed, "The relay closed the connection before it said hello.");
                var challenge = line["challenge"]?.GetValue<string>() ?? throw new RelayException(RelayCodes.Failed, "The relay did not send a challenge: it may not be an Unciv relay.");
                return new RelayChannel(tcp, stream, challenge);
            }
            catch (RelayException)
            {
                throw;
            }
            catch (AuthenticationException e)
            {
                throw new RelayException(RelayCodes.CertificateRejected, "The relay's certificate was not accepted: " + e.Message, inner: e);
            }
            catch (Exception e) when (e is SocketException or IOException or OperationCanceledException or JsonException or InvalidOperationException or FormatException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new RelayException(RelayCodes.Unreachable, "The relay could not be reached: " + e.Message, inner: e);
            }
        }
        catch
        {
            tcp.Dispose();
            throw;
        }
    }

    private static bool AcceptRelayCertificate(RelayEndpoint endpoint, RelayDialOptions options, System.Security.Cryptography.X509Certificates.X509Certificate? certificate, SslPolicyErrors errors)
    {
        if (certificate == null) return false;
        if (endpoint.Fingerprint != null)
        {
            using var cert2 = new System.Security.Cryptography.X509Certificates.X509Certificate2(certificate);
            return HostKey.Matches(endpoint.Fingerprint, HostKey.Fingerprint(cert2));
        }
        return options.TrustAnyCertificate || errors == SslPolicyErrors.None;
    }

    /// <summary>One preamble or reply line, read byte by byte so nothing past it is consumed (what follows may be the inner TLS). Null when the stream ended first.</summary>
    public static async Task<JsonObject?> ReadLineAsync(Stream stream, CancellationToken cancellationToken)
    {
        var bytes = new List<byte>(128);
        var one = new byte[1];
        while (true)
        {
            if (await stream.ReadAsync(one, cancellationToken).ConfigureAwait(false) == 0) return null;
            if (one[0] == (byte)'\n') break;
            bytes.Add(one[0]);
            if (bytes.Count > MaxReplyBytes) throw new RelayException(RelayCodes.Failed, "The relay sent an over-long line.");
        }
        try { return JsonNode.Parse(Encoding.UTF8.GetString([.. bytes])) as JsonObject ?? throw new RelayException(RelayCodes.Failed, "The relay sent something that is not a JSON object."); }
        catch (JsonException e) { throw new RelayException(RelayCodes.Failed, "The relay sent something that is not JSON.", inner: e); }
    }

    public Task<JsonObject?> ReadAsync(CancellationToken cancellationToken) => ReadLineAsync(Stream, cancellationToken);

    /// <summary>
    /// T339: the one reply line of a directory request (op <c>get</c>), up to <paramref name="maxBytes"/> (512 KiB by design/RELAY.md section 3). The relay closes the connection after it, so reading in chunks is fine.
    /// Null when the stream ended first.
    /// </summary>
    public async Task<JsonObject?> ReadLargeAsync(int maxBytes, CancellationToken cancellationToken)
    {
        var bytes = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await Stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) return null;
            var end = Array.IndexOf(buffer, (byte)'\n', 0, read);
            bytes.Write(buffer, 0, end < 0 ? read : end);
            if (bytes.Length > maxBytes) throw new RelayException(RelayCodes.Failed, "The relay sent an over-long reply.");
            if (end >= 0) break;
        }
        try { return JsonNode.Parse(bytes.GetBuffer().AsSpan(0, (int)bytes.Length)) as JsonObject ?? throw new RelayException(RelayCodes.Failed, "The relay sent something that is not a JSON object."); }
        catch (JsonException e) { throw new RelayException(RelayCodes.Failed, "The relay sent something that is not JSON.", inner: e); }
    }

    public async Task SendAsync(JsonObject message, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(message.ToJsonString() + "\n");
        await Stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await Stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The refusal of an <c>{"ok":false,"code":..}</c> reply as an exception; returns normally for <c>ok</c>.</summary>
    public static void ThrowIfRefused(JsonObject? reply, string doing)
    {
        if (reply == null) throw new RelayException(RelayCodes.Failed, $"The relay closed the connection while {doing}.");
        if (reply["ok"]?.GetValue<bool>() == true) return;
        var wire = reply["code"]?.GetValue<string>();
        throw new RelayException(RelayCodes.FromWire(wire), $"The relay refused ({wire ?? "no reason"}) while {doing}.", wire);
    }

    public async ValueTask DisposeAsync()
    {
        try { await Stream.DisposeAsync().ConfigureAwait(false); }
        catch (Exception e) when (e is IOException or ObjectDisposedException) { }
        Tcp.Dispose();
    }
}
