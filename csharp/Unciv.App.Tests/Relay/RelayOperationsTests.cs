using System.Text.Json.Nodes;
using Unciv.Net.Relay;
using Unciv.Net.Turns;
using Unciv.Relay;

namespace Unciv.Core.Tests.Relay;

// T337, RELAY.md R8 (bans), R9 (peer id), R12 (the connectivity suite in all four TLS modes) and R15 (operations: drain, reload, logs).
public sealed class RelayOperationsTests
{
    private static string Code(JsonObject reply) => reply["code"]!.GetValue<string>();

    [Fact]
    public async Task ABannedAddressIsRefusedAtConnect()
    {
        await using var rig = await RelayRig.StartAsync(prepareDir: d => File.WriteAllText(Path.Combine(d, "bans.txt"), "# abuse\n127.0.0.0/8\n"));
        await using var client = await RelayClient.ConnectAsync(rig.Port, rig.UsesTls, readChallenge: false);
        Assert.Equal("banned", Code(await client.ReadAsync()));
    }

    [Fact]
    public async Task ABannedKeyIsRefusedAtHostAndAnExistingRoomIsClosedAfterTheReload()
    {
        await using var rig = await RelayRig.StartAsync();
        var key = rig.NewKey();
        var other = rig.NewKey();
        await using var host = await TestHost.OpenAsync(rig, key);
        await using var innocent = await TestHost.OpenAsync(rig, other, "x");
        Assert.Equal(2, rig.Server.RoomCount);

        File.WriteAllText(Path.Combine(rig.Dir, "bans.txt"), "key " + key.InviteFingerprint + "\n");
        await rig.Server.ReloadAsync();
        var closing = await host.NextEventAsync("closing");
        Assert.Equal("banned", closing["code"]!.GetValue<string>());
        await RelayHostTests.Until(() => rig.Server.RoomCount == 1);
        await using var again = await TestHost.OpenAsync(rig, key);
        Assert.Equal("banned", Code(again.Reply));
        await innocent.SendAsync(new JsonObject { ["op"] = "ping" });
        Assert.NotNull(await innocent.NextEventAsync("pong"));
    }

    [Fact]
    public async Task ABanByFullFingerprintAndByNamePatternWorks()
    {
        await using var rig = await RelayRig.StartAsync();
        var key = rig.NewKey();
        File.WriteAllText(Path.Combine(rig.Dir, "bans.txt"), "key " + key.FingerprintHex + "\nname ^spam\n");
        await rig.Server.ReloadAsync();
        await using var banned = await TestHost.OpenAsync(rig, key);
        Assert.Equal("banned", Code(banned.Reply));
        await using var host = await TestHost.OpenAsync(rig, rig.NewKey());
        Assert.Equal("listing-rejected", Code(await host.ListAsync(1, name: "Spam central")));
        Assert.True((await host.ListAsync(2, name: "Fine game"))["ok"]!.GetValue<bool>());
        // a name ban added later removes the live room
        File.WriteAllText(Path.Combine(rig.Dir, "bans.txt"), "name ^fine\n");
        await rig.Server.ReloadAsync();
        Assert.Equal("banned", (await host.NextEventAsync("closing"))["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task AChangedBanFileIsPickedUpBySweepWithoutASignal()
    {
        await using var rig = await RelayRig.StartAsync();
        await using var host = await TestHost.OpenAsync(rig, rig.NewKey());
        await Task.Delay(50);
        File.WriteAllText(Path.Combine(rig.Dir, "bans.txt"), "127.0.0.1\n");
        await rig.Server.SweepAsync();
        Assert.Equal("banned", (await host.NextEventAsync("closing"))["code"]!.GetValue<string>());
        await using var client = await RelayClient.ConnectAsync(rig.Port, rig.UsesTls, readChallenge: false);
        Assert.Equal("banned", Code(await client.ReadAsync()));
    }

    [Fact]
    public async Task TheHostSeesAStableOpaquePeerPerJoinerNetworkAndRoomButNeverTheAddress()
    {
        await using var rig = await RelayRig.StartAsync(RelayMode.Proxy);
        await using var one = await TestHost.OpenAsync(rig, rig.NewKey(), "one");
        await using var two = await TestHost.OpenAsync(rig, rig.NewKey(), "two");
        async Task<string> PeerAsync(TestHost host, string address)
        {
            rig.Proxy!.ClientAddress = address;
            await using var joiner = await rig.ConnectAsync();
            await joiner.SendAsync(new JsonObject { ["op"] = "join", ["v"] = 1, ["room"] = host.Room });
            var incoming = await host.NextEventAsync("incoming");
            var peer = incoming["peer"]!.GetValue<string>();
            Assert.Matches("^[0-9a-f]{16}$", peer);
            Assert.DoesNotContain(address.Replace("v2:", "", StringComparison.Ordinal), incoming.ToJsonString(), StringComparison.Ordinal);
            await host.SendAsync(new JsonObject { ["op"] = "reject", ["id"] = incoming["id"]!.GetValue<string>() });
            return peer;
        }
        var a1 = await PeerAsync(one, "203.0.113.5");
        var a2 = await PeerAsync(one, "203.0.113.5");
        var b = await PeerAsync(one, "203.0.113.6");
        var aElsewhere = await PeerAsync(two, "203.0.113.5");
        var v6a = await PeerAsync(one, "2001:db8:1:2::1");
        var v6b = await PeerAsync(one, "2001:db8:1:2:ffff::9"); // the same /64
        var v6c = await PeerAsync(one, "2001:db8:1:3::1");
        Assert.Equal(a1, a2);
        Assert.NotEqual(a1, b);
        Assert.NotEqual(a1, aElsewhere);
        Assert.Equal(v6a, v6b);
        Assert.NotEqual(v6a, v6c);
    }

    [Theory]
    [InlineData(RelayMode.Plain)]
    [InlineData(RelayMode.Certificate)]
    [InlineData(RelayMode.SelfSigned)]
    [InlineData(RelayMode.Proxy)]
    public async Task TheConnectivitySuiteRunsInEveryTlsMode(RelayMode mode)
    {
        await using var rig = await RelayRig.StartAsync(mode);
        var key = rig.NewKey();
        await using var host = await TestHost.OpenAsync(rig, key);
        Assert.True(host.Ok, host.Reply.ToJsonString());

        if (rig.UsesTls)
        {
            // the client can pin the relay: the certificate the handshake showed is the one whose fingerprint the relay printed
            using var shown = host.Control.ServerCertificate!;
            Assert.Equal(rig.PinnedFingerprint, Unciv.Net.Identity.HostKey.Fingerprint(shown));
        }

        var (joiner, data) = await host.JoinAsync();
        await using (joiner) await using (data)
        {
            byte[] hello = [0x16, 0x03, 0x03, 7, 7, 7];
            await joiner.Stream.WriteAsync(hello);
            var got = new byte[hello.Length];
            await data.Stream.ReadExactlyAsync(got).AsTask().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(hello, got);
            await data.Stream.WriteAsync(new byte[] { 1, 2, 3 });
            await joiner.Stream.ReadExactlyAsync(new byte[3]).AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        }
        Assert.True((await host.ListAsync(1))["ok"]!.GetValue<bool>());
        Assert.Single(GamesOf(await rig.GetOverTunnelAsync("/v1/games")));
    }

    private static JsonArray GamesOf(JsonObject reply) => reply["data"]!["games"]!.AsArray();

    [Fact]
    public async Task BehindAProxyTheInjectedAddressDecidesBansAndAUntrustedSourceIsRefused()
    {
        await using var rig = await RelayRig.StartAsync(RelayMode.Proxy, prepareDir: d => File.WriteAllText(Path.Combine(d, "bans.txt"), "203.0.113.9\n"));
        rig.Proxy!.ClientAddress = "203.0.113.9";
        await using (var banned = await RelayClient.ConnectAsync(rig.Port, false, readChallenge: false))
            Assert.Equal("banned", Code(await banned.ReadAsync()));
        rig.Proxy.ClientAddress = "v2:203.0.113.10"; // a binary v2 header works the same
        await using (var fine = await rig.ConnectAsync()) Assert.NotEmpty(fine.Challenge);
        rig.Proxy.ClientAddress = "v2:2001:db8::1";
        await using (var v6 = await rig.ConnectAsync()) Assert.NotEmpty(v6.Challenge);

        // a source outside --trusted-proxy may not send a header: the connection is closed without an answer
        await using var strict = await RelayRig.StartAsync(RelayMode.Proxy, tweak: o => o with { TrustedProxies = [System.Net.IPNetwork.Parse("10.0.0.0/8")] });
        await using var refused = await RelayClient.ConnectAsync(strict.Port, false, readChallenge: false);
        Assert.True(await refused.ClosedAsync());
    }

    [Fact]
    public async Task ABadProxyHeaderClosesTheConnection()
    {
        await using var rig = await RelayRig.StartAsync(RelayMode.Proxy);
        await using var direct = await RelayClient.ConnectAsync(rig.Server.TunnelPort, false, readChallenge: false);
        await direct.SendRawAsync("GET / HTTP/1.1\r\n\r\n");
        Assert.True(await direct.ClosedAsync());
    }

    [Fact]
    public async Task ACertificateReloadOnSighupServesTheNewCertificate()
    {
        await using var rig = await RelayRig.StartAsync(RelayMode.Certificate);
        string before;
        await using (var client = await rig.ConnectAsync()) before = Unciv.Net.Identity.HostKey.Fingerprint(client.ServerCertificate!);
        using var fresh = RelayRig.WritePem(rig.Dir, out _, out _);
        await rig.Server.ReloadAsync();
        await using var after = await rig.ConnectAsync();
        var now = Unciv.Net.Identity.HostKey.Fingerprint(after.ServerCertificate!);
        Assert.NotEqual(before, now);
        Assert.Equal(Unciv.Net.Identity.HostKey.Fingerprint(fresh), now);

        File.WriteAllText(Path.Combine(rig.Dir, "relay.key"), "garbage"); // a broken renewal keeps the old certificate serving
        await rig.Server.ReloadAsync();
        await using var still = await rig.ConnectAsync();
        Assert.Equal(now, Unciv.Net.Identity.HostKey.Fingerprint(still.ServerCertificate!));
    }

    [Fact]
    public async Task AGracefulStopTellsHostsToReconnectDrainsAndClosesTheListeners()
    {
        await using var rig = await RelayRig.StartAsync(tweak: o => o with { DrainSeconds = 1 });
        await using var host = await TestHost.OpenAsync(rig, rig.NewKey());
        var (joiner, data) = await host.JoinAsync();
        await using (joiner) await using (data)
        {
            var stop = rig.Server.StopAsync();
            var closing = await host.NextEventAsync("closing");
            Assert.Equal("restart", closing["code"]!.GetValue<string>());
            await stop.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.True(await joiner.ClosedAsync()); // still spliced after the drain second: closed by the shutdown
        }
        await Assert.ThrowsAnyAsync<System.Net.Sockets.SocketException>(() => RelayClient.ConnectAsync(rig.Port, rig.UsesTls, readChallenge: false)); // the listener is closed
    }

    [Fact]
    public async Task ARelayWithNoRunningSplicesStopsAtOnce()
    {
        await using var rig = await RelayRig.StartAsync(tweak: o => o with { DrainSeconds = 30 });
        await using var host = await TestHost.OpenAsync(rig, rig.NewKey());
        await rig.Server.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("restart", (await host.NextEventAsync("closing"))["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task TheLogsHoldNoPayloadNamesKeysOrSignatures()
    {
        var output = new StringWriter();
        var dir = Directory2.Create();
        var key = new TestHostKey();
        try
        {
            var options = new RelayOptions
            {
                DataDir = dir,
                TunnelPort = 0,
                HttpPort = 0,
                TlsMode = RelayTlsMode.None,
                PublicNames = [RelayRig.PublicName],
                LogLevel = "debug",
                SweepIntervalMillis = 3_600_000,
                Limits = new RelayLimits { HandshakesPerMinute = 10000 },
            };
            await using (var server = new RelayServer(options, SystemClock.Instance, output))
            {
                server.Start();
                var rig = server.TunnelPort;
                await using var control = await RelayClient.ConnectAsync(rig, false);
                var tag = RelayProtocol.GameTag("secret-game-id");
                var reply = await control.RequestAsync(TestHost.HostPreamble(key, control.Challenge, tag));
                var room = reply["room"]!.GetValue<string>();
                var body = TestHost.ListingBody(1, name: "SECRETNAME", hostName: "SECRETHOST");
                await control.SendAsync(new JsonObject { ["op"] = "list", ["body"] = body, ["sig"] = key.Sign(RelayProtocol.ListingSignedText(body)) });
                Assert.True((await control.ReadAsync())["ok"]!.GetValue<bool>());
                await using var joiner = await RelayClient.ConnectAsync(rig, false);
                await joiner.SendAsync(new JsonObject { ["op"] = "join", ["v"] = 1, ["room"] = room });
                var id = (await control.ReadAsync())["id"]!.GetValue<string>();
                await using var data = await RelayClient.ConnectAsync(rig, false);
                await data.RequestAsync(new JsonObject { ["op"] = "accept", ["v"] = 1, ["room"] = room, ["id"] = id, ["sig"] = key.Sign(RelayProtocol.AcceptSignedText(RelayRig.PublicName, data.Challenge, room, id)) });
                await joiner.ReadAsync();
                await joiner.Stream.WriteAsync(new byte[] { 0x16, 0x03 }.Concat("PAYLOADMARKER"u8.ToArray()).ToArray());
                await data.Stream.ReadExactlyAsync(new byte[15]).AsTask().WaitAsync(TimeSpan.FromSeconds(10));
                await joiner.DisposeAsync();
            }
            var text = output.ToString();
            Assert.Contains("splice", text, StringComparison.Ordinal);
            foreach (var secret in new[] { "PAYLOADMARKER", "SECRETNAME", "SECRETHOST", "secret-game-id", key.CertBase64[..40], key.Sign("x")[..10] })
                Assert.DoesNotContain(secret, text, StringComparison.Ordinal);
            Assert.Contains("ip=127.0.0.1", text, StringComparison.Ordinal); // connection logs keep the source for abuse handling
        }
        finally
        {
            key.Dispose();
            System.IO.Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task LogFilesRotateAwayAfterTheRetention()
    {
        var clock = new ManualClock(1_900_000_000_000); // 2030-03-17
        var dir = Directory2.Create();
        try
        {
            var log = new RelayLog(null, Path.Combine(dir, "logs"), 7, "info", clock);
            log.Info("old");
            clock.Advance(8L * 24 * 3600 * 1000);
            log.Info("new");
            Assert.Equal(2, System.IO.Directory.GetFiles(Path.Combine(dir, "logs")).Length);
            Assert.Equal(1, log.Rotate());
            var left = Assert.Single(System.IO.Directory.GetFiles(Path.Combine(dir, "logs")));
            Assert.Contains("new", File.ReadAllText(left), StringComparison.Ordinal);
            File.WriteAllText(Path.Combine(dir, "logs", "relay-notadate.log"), "x");
            Assert.Equal(0, log.Rotate());
        }
        finally { System.IO.Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task EachLogLevelIsFilteredAndLinesAreSanitised()
    {
        var output = new StringWriter();
        var log = new RelayLog(output, null, 7, "warn", new ManualClock(0));
        log.Debug("d");
        log.Info("i");
        log.Warn("w", room: "AB CD", fp: "0123456789abcdef", reason: "a=b\nc", bytes: 5, ip: "1.2.3.4", peer: "p");
        log.Error("e", "boom");
        var lines = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.Contains("room=AB_CD fp=01234567 peer=p reason=a-b_c bytes=5 ip=1.2.3.4", lines[0], StringComparison.Ordinal);
        Assert.StartsWith("1970-01-01T00:00:00.000Z ERROR e", lines[1], StringComparison.Ordinal);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task RoomsAreForgottenAfterTheirTtlAndAnOfflineRoomCanBeReclaimed()
    {
        var clock = new ManualClock(50_000_000);
        await using var rig = await RelayRig.StartAsync(clock: clock);
        var key = rig.NewKey();
        string room;
        await using (var host = await TestHost.OpenAsync(rig, key)) room = host.Room;
        await RelayHostTests.Until(() => rig.Server.RoomCount == 0);
        await using (var joiner = await rig.ConnectAsync())
            Assert.Equal("host-gone", Code(await joiner.RequestAsync(new JsonObject { ["op"] = "join", ["v"] = 1, ["room"] = room })));
        clock.Advance(25L * 3600 * 1000);
        await rig.Server.SweepAsync();
        await using (var joiner = await rig.ConnectAsync())
            Assert.Equal("room-unknown", Code(await joiner.RequestAsync(new JsonObject { ["op"] = "join", ["v"] = 1, ["room"] = room })));
    }

    [Fact]
    public void TheNginxExampleIsSyntaxCheckedWhenNginxIsInstalled()
    {
        var example = Path.Combine(RelayHostTests.RepoCsharpRoot(), "Unciv.Relay", "deploy", "nginx", "relay.conf.example");
        Assert.True(File.Exists(example));
        var text = File.ReadAllText(example);
        Assert.Contains("proxy_protocol on", text, StringComparison.Ordinal);
        Assert.Contains("relay.example.org", text, StringComparison.Ordinal);
        var nginx = new[] { "/usr/sbin/nginx", "/usr/bin/nginx", "/usr/local/bin/nginx", "/usr/local/sbin/nginx" }.FirstOrDefault(File.Exists);
        if (nginx == null)
        {
            Console.WriteLine("nginx is not installed: relay.conf.example was not syntax-checked with nginx -t (UNVERIFIED here).");
            return;
        }
        var dir = Directory2.Create();
        try
        {
            RelayRig.WritePem(dir, out var cert, out var key, "fake").Dispose();
            var conf = text.Replace("/etc/letsencrypt/live/relay.example.org/fullchain.pem", cert, StringComparison.Ordinal).Replace("/etc/letsencrypt/live/relay.example.org/privkey.pem", key, StringComparison.Ordinal);
            var wrapper = Path.Combine(dir, "nginx.conf");
            File.WriteAllText(Path.Combine(dir, "relay.conf"), conf);
            File.WriteAllText(wrapper, $"pid {dir}/nginx.pid;\nerror_log {dir}/error.log;\nevents {{}}\ninclude {dir}/relay.conf;\n");
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(nginx, ["-t", "-c", wrapper, "-p", dir]) { RedirectStandardError = true, RedirectStandardOutput = true })!;
            process.WaitForExit(30_000);
            Assert.True(process.ExitCode == 0, process.StandardError.ReadToEnd());
        }
        finally { System.IO.Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void TheShippedDeploymentFilesExistAndKeepTheirSafeDefaults()
    {
        var root = Path.Combine(RelayHostTests.RepoCsharpRoot(), "Unciv.Relay");
        var docker = File.ReadAllText(Path.Combine(root, "deploy", "Dockerfile"));
        Assert.Contains("USER", docker, StringComparison.Ordinal);
        var compose = File.ReadAllText(Path.Combine(root, "deploy", "compose.yml"));
        Assert.Contains("read_only: true", compose, StringComparison.Ordinal);
        Assert.Contains("127.0.0.1:", compose, StringComparison.Ordinal);
        var unit = File.ReadAllText(Path.Combine(root, "deploy", "unciv-relay.service"));
        foreach (var line in new[] { "DynamicUser=yes", "ProtectSystem=strict", "NoNewPrivileges=yes", "Restart=on-failure" }) Assert.Contains(line, unit, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(root, "README.md")));
        Assert.Contains("ssl_preread", File.ReadAllText(Path.Combine(root, "README.md")), StringComparison.Ordinal);
    }
}

internal static class Directory2
{
    public static string Create() => System.IO.Directory.CreateTempSubdirectory("unciv-relay-t-").FullName;
}
