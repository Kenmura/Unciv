using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Unciv.Net.Identity;
using Unciv.Net.Turns;

namespace Unciv.Relay;

/// <summary>
/// The relay (T337, design/RELAY.md): accepts hosts' control channels, joiners and data channels on one TCP port, splices joiner to host, and serves a read-only directory of signed listings.
/// It only accepts: no code in this project dials out (RELAY.md R3, enforced by a source-policy test). Startable in a test on random loopback ports with an injected clock.
/// </summary>
public sealed partial class RelayServer : IAsyncDisposable
{
    private readonly RelayOptions options;
    private readonly RelayLimits limits;
    private readonly IClock clock;
    private readonly ISecureRandom random;
    private readonly RelayPolicyFiles policy;
    private readonly Lock gate = new();
    private readonly Dictionary<string, Room> rooms = new(StringComparer.Ordinal);
    private readonly List<ControlChannel> controls = [];
    private readonly List<Splice> splices = [];
    private readonly HashSet<RelayConn> liveConnections = [];
    private readonly Dictionary<string, int> connectionsByNetwork = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> temporaryBans = new(StringComparer.Ordinal);
    private readonly KeyedWindow handshakes;
    private readonly KeyedWindow joinRate;
    private readonly KeyedWindow lookupFailures;
    private readonly KeyedWindow listingRate;
    private readonly KeyedWindow httpRate;
    private readonly KeyedWindow controlRate;
    private readonly ByteBudget? monthly;
    private readonly TokenBucket? egress;
    private readonly CancellationTokenSource stopping = new();
    private readonly List<Task> background = [];
    private readonly List<string> publicNames = [];
    private byte[] secret = [];
    private X509Certificate2? certificate;
    private string certificateStamp = "";
    private TcpListener? tunnel;
    private TcpListener? http;
    private long joinsTotal;
    private long bytesTotal;
    private int disposed;

    public RelayServer(RelayOptions options, IClock? clock = null, TextWriter? output = null, ISecureRandom? random = null)
    {
        this.options = options;
        limits = options.Limits;
        this.clock = clock ?? SystemClock.Instance;
        this.random = random ?? SystemSecureRandom.Instance;
        policy = new RelayPolicyFiles(options.DataDir);
        Log = new RelayLog(output, options.NoLogFiles ? null : Path.Combine(options.DataDir, "logs"), options.LogRetentionDays, options.LogLevel, this.clock);
        handshakes = new KeyedWindow(limits.HandshakesPerMinute, 60_000, this.clock);
        joinRate = new KeyedWindow(limits.JoinsPerMinute, 60_000, this.clock);
        lookupFailures = new KeyedWindow(limits.LookupFailuresPerMinute, 60_000, this.clock);
        listingRate = new KeyedWindow(limits.ListingsPerMinute, 60_000, this.clock);
        httpRate = new KeyedWindow(limits.HttpRequestsPerMinute, 60_000, this.clock);
        controlRate = new KeyedWindow(120, 60_000, this.clock);
        if (limits.MaxGibPerMonth > 0) monthly = new ByteBudget(limits.MaxGibPerMonth * 1024L * 1024 * 1024, 30L * 24 * 3600 * 1000, this.clock);
        if (limits.MaxMbps > 0) egress = new TokenBucket(limits.MaxMbps * 125_000L, limits.MaxMbps * 125_000L, this.clock);
    }

    public RelayLog Log { get; }

    /// <summary>The bound tunnel port after <see cref="Start"/>.</summary>
    public int TunnelPort { get; private set; }

    /// <summary>The bound HTTP port after <see cref="Start"/>, or 0 when the HTTP listener is off.</summary>
    public int HttpPort { get; private set; }

    /// <summary>The fingerprint clients pin for <c>--self-signed</c> or <c>--cert</c> (null with <c>--tls none</c>).</summary>
    public string? CertificateFingerprint => certificate == null ? null : RelayCertificates.Fingerprint(certificate);

    public int RoomCount
    {
        get { lock (gate) return rooms.Values.Count(r => r.Live); }
    }

    public int SpliceCount
    {
        get { lock (gate) return splices.Count; }
    }

    public int ListedCount
    {
        get { lock (gate) return rooms.Values.Count(r => r.Listing != null); }
    }

    /// <summary>The relay's own name for the host part signatures bind (lower case).</summary>
    public IReadOnlyList<string> PublicNames => publicNames;

    /// <summary>Loads the secret, policy files and certificate, binds the listeners and starts accepting. Throws <see cref="RelayUsageException"/> for a configuration that cannot work.</summary>
    public void Start()
    {
        Directory.CreateDirectory(options.DataDir);
        secret = LoadSecret();
        policy.Reload(force: true);
        foreach (var warning in policy.Warnings) Log.Warn("policy-warning", reason: warning);
        var mode = options.TlsMode ?? throw new RelayUsageException("choose a TLS mode: --cert/--key, --self-signed or --tls none");
        publicNames.AddRange(options.PublicNames);
        if (mode == RelayTlsMode.Certificate)
        {
            certificate = RelayCertificates.LoadPem(options.CertFile!, options.KeyFile!);
            certificateStamp = CertificateStamp();
            if (publicNames.Count == 0) publicNames.AddRange(RelayCertificates.DnsNames(certificate));
            if (publicNames.Count == 0) throw new RelayUsageException("the certificate has no DNS names: give --public-name");
        }
        else if (mode == RelayTlsMode.SelfSigned)
        {
            certificate = RelayCertificates.LoadOrCreateSelfSigned(options.DataDir, publicNames);
        }

        var bind = options.Bind != null ? IPAddress.Parse(options.Bind) : mode == RelayTlsMode.None ? IPAddress.Loopback : IPAddress.IPv6Any;
        tunnel = Listen(bind, options.TunnelPort);
        TunnelPort = ((IPEndPoint)tunnel.LocalEndpoint).Port;
        background.Add(AcceptLoopAsync(tunnel, HandleTunnelAsync));
        if (!options.NoHttp)
        {
            http = Listen(IPAddress.Parse(options.HttpBind), options.HttpPort);
            HttpPort = ((IPEndPoint)http.LocalEndpoint).Port;
            background.Add(AcceptLoopAsync(http, HandleHttpAsync));
        }
        background.Add(SweepLoopAsync());
        Log.Info("started", reason: mode.ToString());
    }

    private static TcpListener Listen(IPAddress address, int port)
    {
        var listener = new TcpListener(address, port);
        if (address.Equals(IPAddress.IPv6Any)) listener.Server.DualMode = true;
        try { listener.Start(); }
        catch (SocketException e) { throw new RelayUsageException($"cannot listen on {address}:{port}: {e.Message}"); }
        return listener;
    }

    private byte[] LoadSecret()
    {
        var path = Path.Combine(options.DataDir, "relay-secret");
        if (File.Exists(path))
        {
            var existing = Convert.FromHexString(File.ReadAllText(path).Trim());
            if (existing.Length >= 16) return existing;
        }
        var fresh = new byte[32];
        random.Fill(fresh);
        if (OperatingSystem.IsWindows()) File.WriteAllText(path, Convert.ToHexString(fresh));
        else
        {
            // created 0600 from the start: no window in which the secret is readable by others
            using var stream = new FileStream(path, new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write, UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite });
            stream.Write(Encoding.ASCII.GetBytes(Convert.ToHexString(fresh)));
        }
        return fresh;
    }

    private string CertificateStamp()
    {
        var parts = new[] { options.CertFile, options.KeyFile }.Select(f => f == null ? "-" : new FileInfo(f) is { Exists: true } info ? info.LastWriteTimeUtc.Ticks + ":" + info.Length : "-");
        return string.Join('|', parts);
    }

    private async Task AcceptLoopAsync(TcpListener listener, Func<TcpClient, Task> handler)
    {
        while (!stopping.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(stopping.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (InvalidOperationException) { return; }
            catch (SocketException) { continue; }
            _ = RunHandlerAsync(client, handler);
        }
    }

    private async Task RunHandlerAsync(TcpClient client, Func<TcpClient, Task> handler)
    {
        try
        {
            await Task.Yield();
            client.NoDelay = true;
            client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            await handler(client).ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException or SocketException or AuthenticationException or InvalidDataException or InvalidOperationException) { }
        catch (Exception e)
        {
            Log.Error("handler-failed", e.GetType().Name); // a bug must not take the relay down, nor leak a payload: the type name only
        }
        finally { client.Dispose(); }
    }

    private bool IsTrustedProxy(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        return options.TrustedProxies.Any(n => n.Contains(address));
    }

    private bool IsBanned(RelayConn conn)
    {
        if (policy.IsAddressBanned(conn.Address)) return true;
        lock (gate)
        {
            if (!temporaryBans.TryGetValue(conn.Network, out var until)) return false;
            if (clock.NowMillis < until) return true;
            temporaryBans.Remove(conn.Network);
            return false;
        }
    }

    private bool EnterConnection(string network)
    {
        lock (gate)
        {
            connectionsByNetwork.TryGetValue(network, out var count);
            if (count >= limits.MaxConnectionsPerNetwork) return false;
            connectionsByNetwork[network] = count + 1;
            return true;
        }
    }

    private void LeaveConnection(string network)
    {
        lock (gate)
        {
            if (!connectionsByNetwork.TryGetValue(network, out var count)) return;
            if (count <= 1) connectionsByNetwork.Remove(network);
            else connectionsByNetwork[network] = count - 1;
        }
    }

    private async Task HandleTunnelAsync(TcpClient tcp)
    {
        var remote = (IPEndPoint)tcp.Client.RemoteEndPoint!;
        using var conn = new RelayConn(tcp, remote.Address);
        lock (gate) liveConnections.Add(conn);
        string? counted = null;
        try
        {
            if (options.ProxyProtocol)
            {
                if (!IsTrustedProxy(remote.Address))
                {
                    Log.Warn("untrusted-proxy", reason: "proxy-header-refused", ip: conn.Ip);
                    return;
                }
                using var headerTimeout = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token);
                headerTimeout.CancelAfter(limits.PreambleTimeoutMillis);
                try
                {
                    var client = await ProxyProtocol.ReadAsync(conn.Stream, headerTimeout.Token).ConfigureAwait(false);
                    if (client != null) conn.Address = client;
                }
                catch (Exception e) when (e is InvalidDataException or OperationCanceledException or IOException)
                {
                    Log.Warn("bad-proxy-header", ip: conn.Ip);
                    return;
                }
            }
            var network = conn.Network;
            if (!EnterConnection(network))
            {
                Log.Warn("refused", reason: "connections-per-network", ip: conn.Ip);
                return;
            }
            counted = network;
            if (!handshakes.TryHit(network))
            {
                Log.Warn("refused", reason: "handshake-rate", ip: conn.Ip);
                return;
            }
            if (certificate != null && !await HandshakeTlsAsync(conn).ConfigureAwait(false)) return;
            if (IsBanned(conn))
            {
                await conn.TrySendAsync(Error("banned")).ConfigureAwait(false);
                Log.Info("refused", reason: "banned", ip: conn.Ip);
                return;
            }
            await HandlePreambleAsync(conn).ConfigureAwait(false);
        }
        finally
        {
            if (counted != null) LeaveConnection(counted);
            lock (gate) liveConnections.Remove(conn);
        }
    }

    private async Task<bool> HandshakeTlsAsync(RelayConn conn)
    {
        var ssl = new SslStream(conn.Stream, leaveInnerStreamOpen: false);
        conn.Stream = ssl;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token);
        timeout.CancelAfter(limits.PreambleTimeoutMillis);
        try
        {
            await ssl.AuthenticateAsServerAsync(
                new SslServerAuthenticationOptions
                {
                    ServerCertificate = certificate,
                    EnabledSslProtocols = SslProtocols.Tls13 | SslProtocols.Tls12,
                    ClientCertificateRequired = false,
                    ApplicationProtocols = [new SslApplicationProtocol(RelayProtocol.Alpn)],
                }, timeout.Token).ConfigureAwait(false);
            return true;
        }
        catch (Exception e) when (e is AuthenticationException or IOException or OperationCanceledException)
        {
            Log.Debug("tls-failed", reason: e.GetType().Name, ip: conn.Ip);
            return false;
        }
    }

    internal static JsonObject Error(string code, params (string Key, JsonNode? Value)[] extra)
    {
        var message = new JsonObject { ["ok"] = false, ["code"] = code };
        foreach (var (key, value) in extra) message[key] = value;
        return message;
    }

    private static string? Text(JsonObject json, string name) => json[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private async Task HandlePreambleAsync(RelayConn conn)
    {
        var challengeBytes = new byte[16];
        random.Fill(challengeBytes);
        var challenge = Convert.ToBase64String(challengeBytes);
        await conn.SendAsync(new JsonObject { ["challenge"] = challenge, ["v"] = RelayProtocol.Version }, stopping.Token).ConfigureAwait(false);

        string? line;
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token))
        {
            timeout.CancelAfter(limits.PreambleTimeoutMillis);
            try { line = await conn.ReadLineAsync(RelayProtocol.PreambleMaxBytes, timeout.Token).ConfigureAwait(false); }
            catch (LineTooLongException)
            {
                await conn.TrySendAsync(Error("too-large")).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) when (!stopping.IsCancellationRequested)
            {
                await conn.TrySendAsync(Error("timeout")).ConfigureAwait(false);
                Log.Debug("preamble-timeout", ip: conn.Ip);
                return;
            }
        }
        if (line == null) return;

        JsonObject? json;
        try { json = JsonNode.Parse(line) as JsonObject; }
        catch (JsonException) { json = null; }
        if (json == null)
        {
            await conn.TrySendAsync(Error("bad-request")).ConfigureAwait(false);
            return;
        }
        if (json["v"] is not JsonValue versionNode || !versionNode.TryGetValue<int>(out var version) || version < 1)
        {
            await conn.TrySendAsync(Error("bad-request")).ConfigureAwait(false);
            return;
        }
        if (version > RelayProtocol.Version)
        {
            await conn.TrySendAsync(Error("unsupported-version", ("max", RelayProtocol.Version))).ConfigureAwait(false);
            return;
        }
        switch (Text(json, "op"))
        {
            case "host": await HandleHostAsync(conn, json, challenge).ConfigureAwait(false); break;
            case "accept": await HandleAcceptAsync(conn, json, challenge).ConfigureAwait(false); break;
            case "join": await HandleJoinAsync(conn, json).ConfigureAwait(false); break;
            case "get": await HandleGetAsync(conn, json).ConfigureAwait(false); break;
            default: await conn.TrySendAsync(Error("bad-request")).ConfigureAwait(false); break;
        }
    }

    /// <summary>Verifies a signature made by <paramref name="key"/> over the text for any of the relay's public names (hosts sign the name they dialled).</summary>
    private bool VerifyForAnyName(ECDsa key, string? signature, Func<string, string> text)
    {
        if (signature == null) return false;
        byte[] raw;
        try { raw = Convert.FromBase64String(signature); }
        catch (FormatException) { return false; }
        if (raw.Length != 64) return false;
        foreach (var name in publicNames)
        {
            try { if (key.VerifyData(Encoding.UTF8.GetBytes(text(name)), raw, HashAlgorithmName.SHA256)) return true; }
            catch (CryptographicException) { return false; }
        }
        return false;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        await StopAsync().ConfigureAwait(false);
        stopping.Dispose();
        certificate?.Dispose();
        lock (gate) foreach (var room in rooms.Values) room.Dispose();
    }

    /// <summary>
    /// Graceful stop (RELAY.md section 8): stop accepting, tell every host <c>closing: restart</c> so it reconnects, give running splices <c>--drain-seconds</c> to end, then drop everything.
    /// </summary>
    public async Task StopAsync()
    {
        if (stopping.IsCancellationRequested) return;
        try { tunnel?.Stop(); } catch (SocketException) { }
        try { http?.Stop(); } catch (SocketException) { }
        List<ControlChannel> hosts;
        lock (gate) hosts = [.. controls];
        await Task.WhenAll(hosts.Select(h => h.Conn.TrySendAsync(new JsonObject { ["ev"] = "closing", ["code"] = "restart" }))).ConfigureAwait(false);
        Log.Info("stopping", reason: "restart");
        var deadline = options.DrainSeconds * 1000L;
        for (var waited = 0L; waited < deadline && SpliceCount > 0; waited += 50) await Task.Delay(50).ConfigureAwait(false);
        await stopping.CancelAsync().ConfigureAwait(false);
        List<RelayConn> all;
        lock (gate)
        {
            all = [.. liveConnections];
            foreach (var splice in splices.ToList()) splice.Close("shutdown");
        }
        foreach (var conn in all) conn.Dispose();
        try { await Task.WhenAll(background).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
        catch (Exception e) when (e is TimeoutException or OperationCanceledException) { }
        Log.Info("stopped");
    }
}
