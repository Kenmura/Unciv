using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Unciv.Net.Turns;
using Unciv.Relay;

namespace Unciv.Core.Tests.Relay;

public enum RelayMode
{
    /// <summary>--tls none on loopback, no proxy: the fastest way to test the protocol itself.</summary>
    Plain,

    /// <summary>--cert/--key (PEM files, public name taken from the certificate).</summary>
    Certificate,

    /// <summary>--self-signed, clients pin the printed fingerprint.</summary>
    SelfSigned,

    /// <summary>--tls none --proxy-protocol behind a test proxy that injects a PROXY header.</summary>
    Proxy,
}

/// <summary>A host key as a real host has it: a self-signed ECDSA P-256 certificate.</summary>
internal sealed class TestHostKey : IDisposable
{
    private readonly ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly X509Certificate2 certificate;

    public TestHostKey()
    {
        var request = new CertificateRequest("CN=TestHost", key, HashAlgorithmName.SHA256);
        certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
    }

    public string CertBase64 => Convert.ToBase64String(certificate.RawData);

    public string FingerprintHex => RelayProtocol.FingerprintHex(certificate.PublicKey.ExportSubjectPublicKeyInfo());

    public string InviteFingerprint => RelayProtocol.InviteFingerprint(certificate.PublicKey.ExportSubjectPublicKeyInfo());

    public string Sign(string text) => Convert.ToBase64String(key.SignData(Encoding.UTF8.GetBytes(text), HashAlgorithmName.SHA256));

    public void Dispose()
    {
        certificate.Dispose();
        key.Dispose();
    }
}

/// <summary>Forwards TCP to the relay and puts a PROXY protocol header first, like nginx <c>proxy_protocol on</c>.</summary>
internal sealed class TestProxy : IDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly int relayPort;
    private readonly CancellationTokenSource stop = new();

    public TestProxy(int relayPort)
    {
        this.relayPort = relayPort;
        listener.Start();
        _ = AcceptLoopAsync();
    }

    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

    /// <summary>The client address the next connections claim; "v2:" prefix sends a binary v2 header.</summary>
    public string ClientAddress { get; set; } = "198.51.100.7";

    private async Task AcceptLoopAsync()
    {
        while (!stop.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(stop.Token); }
            catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException or SocketException or InvalidOperationException) { return; }
            _ = ForwardAsync(client, ClientAddress);
        }
    }

    private async Task ForwardAsync(TcpClient client, string address)
    {
        using (client)
        using (var upstream = new TcpClient())
        {
            try
            {
                await upstream.ConnectAsync(IPAddress.Loopback, relayPort, stop.Token);
                var up = upstream.GetStream();
                var header = address.StartsWith("v2:", StringComparison.Ordinal) ? V2Header(IPAddress.Parse(address[3..])) : Encoding.ASCII.GetBytes($"PROXY {(address.Contains(':') ? "TCP6" : "TCP4")} {address} 127.0.0.1 40000 {relayPort}\r\n");
                await up.WriteAsync(header, stop.Token);
                var down = client.GetStream();
                await Task.WhenAny(down.CopyToAsync(up, stop.Token), up.CopyToAsync(down, stop.Token));
            }
            catch (Exception e) when (e is IOException or OperationCanceledException or SocketException or ObjectDisposedException) { }
        }
    }

    private static byte[] V2Header(IPAddress source)
    {
        var v4 = source.AddressFamily == AddressFamily.InterNetwork;
        var payload = new List<byte>(source.GetAddressBytes());
        payload.AddRange(v4 ? new byte[4] : new byte[16]);
        payload.AddRange(new byte[4]);
        var header = new List<byte> { 0x0D, 0x0A, 0x0D, 0x0A, 0x00, 0x0D, 0x0A, 0x51, 0x55, 0x49, 0x54, 0x0A, 0x21, (byte)(v4 ? 0x11 : 0x21), (byte)(payload.Count >> 8), (byte)payload.Count };
        header.AddRange(payload);
        return [.. header];
    }

    public void Dispose()
    {
        stop.Cancel();
        listener.Stop();
    }
}

/// <summary>A relay on random loopback ports for one test, in one of the TLS modes of RELAY.md section 8, with the generous limits tests want (a test lowers the one it examines).</summary>
internal sealed class RelayRig : IAsyncDisposable
{
    public const string PublicName = "relay.example.org";
    private readonly X509Certificate2? pemCertificate;

    private RelayRig(RelayMode mode, string dir, IClock clock, RelayServer server, TestProxy? proxy, X509Certificate2? pemCertificate)
    {
        Mode = mode;
        Dir = dir;
        Clock = clock;
        Server = server;
        Proxy = proxy;
        this.pemCertificate = pemCertificate;
    }

    private readonly List<TestHostKey> keys = [];

    public RelayMode Mode { get; }

    public string Dir { get; }

    public IClock Clock { get; }

    public RelayServer Server { get; }

    public TestProxy? Proxy { get; }

    public StringWriter Output { get; } = new();

    public int Port => Proxy?.Port ?? Server.TunnelPort;

    public bool UsesTls => Mode is RelayMode.Certificate or RelayMode.SelfSigned;

    /// <summary>The fingerprint a client would pin (--self-signed prints it, --cert has it in the certificate).</summary>
    public string? PinnedFingerprint => Server.CertificateFingerprint;

    public static async Task<RelayRig> StartAsync(RelayMode mode = RelayMode.Plain, Func<RelayOptions, RelayOptions>? tweak = null, IClock? clock = null, Action<string>? prepareDir = null)
    {
        var dir = Directory.CreateTempSubdirectory("unciv-relay-").FullName;
        prepareDir?.Invoke(dir);
        clock ??= new ManualClock(1_000_000);
        X509Certificate2? pem = null;
        var options = new RelayOptions
        {
            DataDir = dir,
            TunnelPort = 0,
            HttpPort = 0,
            NoLogFiles = true,
            LogLevel = "debug",
            SweepIntervalMillis = 3_600_000, // tests drive the sweep with the fake clock
            DrainSeconds = 1,
            Limits = new RelayLimits
            {
                HandshakesPerMinute = 100_000,
                MaxConnectionsPerNetwork = 1000,
                MaxRoomsPerNetwork = 100,
                MaxControlPerNetwork = 100,
                JoinsPerMinute = 100_000,
                MaxJoinersPerNetwork = 100,
                LookupFailuresPerMinute = 100_000,
                ListingsPerMinute = 100_000,
                HttpRequestsPerMinute = 100_000,
                PreambleTimeoutMillis = 5000,
                HostTimeoutMillis = 5000,
            },
        };
        switch (mode)
        {
            case RelayMode.Plain:
                options = options with { TlsMode = RelayTlsMode.None, PublicNames = [PublicName] };
                break;
            case RelayMode.Proxy:
                options = options with { TlsMode = RelayTlsMode.None, PublicNames = [PublicName], ProxyProtocol = true };
                break;
            case RelayMode.SelfSigned:
                options = options with { TlsMode = RelayTlsMode.SelfSigned, PublicNames = [PublicName] };
                break;
            case RelayMode.Certificate:
                pem = WritePem(dir, out var certFile, out var keyFile);
                options = options with { TlsMode = RelayTlsMode.Certificate, CertFile = certFile, KeyFile = keyFile };
                break;
        }
        if (tweak != null) options = tweak(options);
        var server = new RelayServer(options, clock, output: null);
        server.Start();
        var proxy = mode == RelayMode.Proxy ? new TestProxy(server.TunnelPort) : null;
        return new RelayRig(mode, dir, clock, server, proxy, pem);
    }

    /// <summary>A PEM certificate and key for relay.example.org (DNS SAN) in <paramref name="dir"/>.</summary>
    public static X509Certificate2 WritePem(string dir, out string certFile, out string keyFile, string fileStem = "relay")
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=" + PublicName, key, HashAlgorithmName.SHA256);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName(PublicName);
        request.CertificateExtensions.Add(names.Build());
        var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        certFile = Path.Combine(dir, fileStem + ".crt");
        keyFile = Path.Combine(dir, fileStem + ".key");
        File.WriteAllText(certFile, cert.ExportCertificatePem());
        File.WriteAllText(keyFile, key.ExportPkcs8PrivateKeyPem());
        return cert;
    }

    /// <summary>A fresh host key, disposed with the rig.</summary>
    public TestHostKey NewKey()
    {
        var key = new TestHostKey();
        keys.Add(key);
        return key;
    }

    public async Task<RelayClient> ConnectAsync() => await RelayClient.ConnectAsync(Port, UsesTls);

    public async Task<(int Status, string Headers, string Body)> HttpAsync(string request)
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, Server.HttpPort);
        var stream = tcp.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes(request));
        using var reader = new MemoryStream();
        await stream.CopyToAsync(reader).WaitAsync(TimeSpan.FromSeconds(10));
        var text = Encoding.UTF8.GetString(reader.ToArray());
        var split = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        var head = text[..split];
        return (int.Parse(head.Split(' ')[1]), head, text[(split + 4)..]);
    }

    public Task<(int Status, string Headers, string Body)> GetAsync(string path, string extraHeaders = "") =>
        HttpAsync($"GET {path} HTTP/1.1\r\nHost: x\r\n{extraHeaders}\r\n");

    /// <summary>The directory as a client sees it over the tunnel: op get.</summary>
    public async Task<JsonObject> GetOverTunnelAsync(string path)
    {
        await using var client = await ConnectAsync();
        return await client.RequestAsync(new JsonObject { ["op"] = "get", ["v"] = 1, ["path"] = path });
    }

    public async ValueTask DisposeAsync()
    {
        Proxy?.Dispose();
        await Server.DisposeAsync();
        pemCertificate?.Dispose();
        foreach (var key in keys) key.Dispose();
        try { Directory.Delete(Dir, recursive: true); }
        catch (IOException) { }
    }
}

/// <summary>A scripted client of the tunnel: connects, reads the challenge line, then sends one preamble line and talks the line protocol (or raw bytes after a join/accept).</summary>
internal sealed class RelayClient : IAsyncDisposable
{
    private readonly TcpClient tcp;

    private RelayClient(TcpClient tcp, Stream stream, string challenge)
    {
        this.tcp = tcp;
        Stream = stream;
        Challenge = challenge;
    }

    public Stream Stream { get; }

    public string Challenge { get; }

    public X509Certificate2? ServerCertificate { get; private set; }

    public static async Task<RelayClient> ConnectAsync(int port, bool tls, bool readChallenge = true)
    {
        var tcp = new TcpClient { NoDelay = true };
        await tcp.ConnectAsync(IPAddress.Loopback, port);
        Stream stream = tcp.GetStream();
        X509Certificate2? seen = null;
        if (tls)
        {
            var ssl = new SslStream(stream, false, (_, certificate, _, _) =>
            {
                seen = certificate == null ? null : new X509Certificate2(certificate);
                return true;
            });
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = RelayRig.PublicName,
                ApplicationProtocols = [new SslApplicationProtocol(RelayProtocol.Alpn)],
            }).WaitAsync(TimeSpan.FromSeconds(10));
            stream = ssl;
        }
        var client = new RelayClient(tcp, stream, "") { ServerCertificate = seen };
        if (!readChallenge) return client;
        var line = JsonNode.Parse(await client.ReadLineAsync())!.AsObject();
        return new RelayClient(tcp, stream, line["challenge"]!.GetValue<string>()) { ServerCertificate = seen };
    }

    public async Task<string> ReadLineAsync(int timeoutSeconds = 10)
    {
        var bytes = new List<byte>();
        var one = new byte[1];
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        while (true)
        {
            var n = await Stream.ReadAsync(one, cts.Token);
            if (n == 0) throw new EndOfStreamException("closed" + (bytes.Count > 0 ? " after " + Encoding.UTF8.GetString([.. bytes]) : ""));
            if (one[0] == '\n') return Encoding.UTF8.GetString([.. bytes]);
            bytes.Add(one[0]);
        }
    }

    public async Task<JsonObject> ReadAsync(int timeoutSeconds = 10) => JsonNode.Parse(await ReadLineAsync(timeoutSeconds))!.AsObject();

    public async Task SendRawAsync(string text)
    {
        await Stream.WriteAsync(Encoding.UTF8.GetBytes(text));
        await Stream.FlushAsync();
    }

    public Task SendAsync(JsonObject message) => SendRawAsync(message.ToJsonString() + "\n");

    public async Task<JsonObject> RequestAsync(JsonObject message)
    {
        await SendAsync(message);
        return await ReadAsync();
    }

    /// <summary>True when the relay closed the connection (end of stream or reset) within the time.</summary>
    public async Task<bool> ClosedAsync(int timeoutSeconds = 10)
    {
        var buffer = new byte[256];
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            while (await Stream.ReadAsync(buffer, cts.Token) > 0) { }
            return true;
        }
        catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException) { return true; }
        catch (OperationCanceledException) { return false; }
    }

    public async ValueTask DisposeAsync()
    {
        await Stream.DisposeAsync();
        tcp.Dispose();
    }

    /// <summary>The first line of a reply, or a closed marker when the relay hung up without one.</summary>
    public async Task<JsonObject> ReadOrClosedAsync()
    {
        try { return await ReadAsync(); }
        catch (Exception e) when (e is EndOfStreamException or IOException) { return new JsonObject { ["closed"] = true }; }
    }
}

/// <summary>A host as a game would run it: holds the control channel of one room and signs what the relay asks for.</summary>
internal sealed class TestHost : IAsyncDisposable
{
    private readonly RelayRig rig;
    private readonly Channel<JsonObject> events = Channel.CreateUnbounded<JsonObject>();
    private readonly CancellationTokenSource stop = new();

    private TestHost(RelayRig rig, TestHostKey key, string gameTag, RelayClient control, JsonObject reply)
    {
        this.rig = rig;
        Key = key;
        GameTag = gameTag;
        Control = control;
        Reply = reply;
        _ = ReadLoopAsync();
    }

    public TestHostKey Key { get; }

    public string GameTag { get; }

    public RelayClient Control { get; }

    public JsonObject Reply { get; }

    public bool Ok => Reply["ok"]?.GetValue<bool>() == true;

    public string Room => Reply["room"]!.GetValue<string>();

    public static JsonObject HostPreamble(TestHostKey key, string challenge, string gameTag, string relayHost = RelayRig.PublicName) => new()
    {
        ["op"] = "host",
        ["v"] = 1,
        ["cert"] = key.CertBase64,
        ["gameTag"] = gameTag,
        ["sig"] = key.Sign(RelayProtocol.HostSignedText(relayHost, challenge, gameTag)),
    };

    public static async Task<TestHost> OpenAsync(RelayRig rig, TestHostKey key, string gameId = "game-1")
    {
        var tag = RelayProtocol.GameTag(gameId);
        var control = await rig.ConnectAsync();
        var reply = await control.RequestAsync(HostPreamble(key, control.Challenge, tag));
        return new TestHost(rig, key, tag, control, reply);
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            while (!stop.IsCancellationRequested) events.Writer.TryWrite(await Control.ReadAsync(timeoutSeconds: 3600));
        }
        catch (Exception e) when (e is EndOfStreamException or IOException or OperationCanceledException or ObjectDisposedException or System.Text.Json.JsonException) { }
        events.Writer.TryComplete();
    }

    /// <summary>The next line from the relay on the control channel (an event or the answer to an op); null at the end of the channel.</summary>
    public async Task<JsonObject?> NextAsync(int timeoutSeconds = 10)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        try { return await events.Reader.ReadAsync(cts.Token); }
        catch (ChannelClosedException) { return null; }
    }

    public async Task<JsonObject> NextEventAsync(string ev)
    {
        while (true)
        {
            var next = await NextAsync() ?? throw new EndOfStreamException("control channel ended before " + ev);
            if (next["ev"]?.GetValue<string>() == ev) return next;
        }
    }

    public Task SendAsync(JsonObject message) => Control.SendAsync(message);

    /// <summary>Opens the data channel for a joiner id, returns it after the relay's ok (or the refusal).</summary>
    public async Task<(RelayClient Data, JsonObject Reply)> AcceptAsync(string id, string? room = null)
    {
        var data = await rig.ConnectAsync();
        var shown = room ?? Room;
        var reply = await data.RequestAsync(new JsonObject
        {
            ["op"] = "accept",
            ["v"] = 1,
            ["room"] = shown,
            ["id"] = id,
            ["sig"] = Key.Sign(RelayProtocol.AcceptSignedText(RelayRig.PublicName, data.Challenge, shown, id)),
        });
        return (data, reply);
    }

    /// <summary>A joiner connects, the host accepts the announced id: returns both ends, ready to carry bytes.</summary>
    public async Task<(RelayClient Joiner, RelayClient Data)> JoinAsync(string? room = null)
    {
        var joiner = await rig.ConnectAsync();
        await joiner.SendAsync(new JsonObject { ["op"] = "join", ["v"] = 1, ["room"] = room ?? Room });
        var incoming = await NextEventAsync("incoming");
        var (data, accepted) = await AcceptAsync(incoming["id"]!.GetValue<string>());
        Assert.True(accepted["ok"]?.GetValue<bool>(), accepted.ToJsonString());
        var joinReply = await joiner.ReadAsync();
        Assert.True(joinReply["ok"]?.GetValue<bool>(), joinReply.ToJsonString());
        return (joiner, data);
    }

    public static string ListingBody(long seq, string name = "Friendly game", string hostName = "Alice", int players = 1, int slots = 4, string ruleset = "Civ V - Vanilla", bool password = false, bool started = false, string version = "1.0", string[]? mods = null)
    {
        var body = new JsonObject
        {
            ["v"] = 1,
            ["seq"] = seq,
            ["name"] = name,
            ["hostName"] = hostName,
            ["players"] = players,
            ["slots"] = slots,
            ["ai"] = 2,
            ["ruleset"] = ruleset,
            ["rulesetHash"] = "abc123",
            ["mods"] = new JsonArray((mods ?? []).Select(m => (JsonNode?)JsonValue.Create(m)).ToArray()),
            ["speed"] = "Quick",
            ["map"] = "Small Continents",
            ["passwordRequired"] = password,
            ["turnMode"] = "simultaneous",
            ["started"] = started,
            ["version"] = version,
            ["proto"] = 1,
        };
        return body.ToJsonString();
    }

    public JsonObject ListMessage(string op, string body) => new() { ["op"] = op, ["body"] = body, ["sig"] = Key.Sign(RelayProtocol.ListingSignedText(body)) };

    public async Task<JsonObject> ListAsync(long seq, string op = "list", string? name = null, string? body = null)
    {
        await SendAsync(ListMessage(op, body ?? (name == null ? ListingBody(seq) : ListingBody(seq, name))));
        while (true)
        {
            var reply = await NextAsync() ?? throw new EndOfStreamException("control ended");
            if (reply["ok"] != null) return reply;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await stop.CancelAsync();
        await Control.DisposeAsync();
    }
}
