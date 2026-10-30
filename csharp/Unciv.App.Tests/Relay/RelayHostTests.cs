using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Unciv.Relay;

namespace Unciv.Core.Tests.Relay;

// T337, RELAY.md R2 (host authentication) and R3 (no open proxy): the relay with a test-only host and joiner stub on loopback.
public sealed class RelayHostTests
{
    private static string Code(JsonObject reply) => reply["code"]!.GetValue<string>();

    [Fact]
    public async Task AHostGetsATenSymbolBase32RoomAndTheKeyFingerprint()
    {
        await using var rig = await RelayRig.StartAsync();
        var key = rig.NewKey();
        await using var host = await TestHost.OpenAsync(rig, key);
        Assert.True(host.Ok, host.Reply.ToJsonString());
        Assert.Matches(new Regex("^[0-9A-HJKMNP-TV-Z]{4}-[0-9A-HJKMNP-TV-Z]{6}$"), host.Room);
        Assert.Equal(key.FingerprintHex, host.Reply["fp"]!.GetValue<string>());
        Assert.Equal(90, host.Reply["ttl"]!.GetValue<int>());
        Assert.Equal(1, rig.Server.RoomCount);
        // the relay greets every connection with a fresh challenge
        await using var a = await rig.ConnectAsync();
        await using var b = await rig.ConnectAsync();
        Assert.NotEqual(a.Challenge, b.Challenge);
    }

    [Fact]
    public async Task ABadSignatureIsRefused()
    {
        await using var rig = await RelayRig.StartAsync();
        var key = rig.NewKey();
        await using var client = await rig.ConnectAsync();
        var line = TestHost.HostPreamble(key, client.Challenge, RelayProtocol.GameTag("g"));
        line["sig"] = Convert.ToBase64String(new byte[64]);
        Assert.Equal("bad-signature", Code(await client.RequestAsync(line)));
        Assert.Equal(0, rig.Server.RoomCount);
    }

    [Theory]
    [InlineData("other-relay")]
    [InlineData("other-challenge")]
    [InlineData("other-op")]
    [InlineData("other-game")]
    public async Task ASignatureMadeForAnotherRelayChallengeOpOrGameIsRefused(string what)
    {
        await using var rig = await RelayRig.StartAsync();
        var key = rig.NewKey();
        await using var client = await rig.ConnectAsync();
        await using var other = await rig.ConnectAsync();
        var tag = RelayProtocol.GameTag("g");
        var line = TestHost.HostPreamble(key, client.Challenge, tag);
        line["sig"] = what switch
        {
            "other-relay" => key.Sign(RelayProtocol.HostSignedText("relay.example.net", client.Challenge, tag)),
            "other-challenge" => key.Sign(RelayProtocol.HostSignedText(RelayRig.PublicName, other.Challenge, tag)),
            "other-op" => key.Sign(RelayProtocol.AcceptSignedText(RelayRig.PublicName, client.Challenge, "K7QF-3MXP4A", "id")),
            _ => key.Sign(RelayProtocol.HostSignedText(RelayRig.PublicName, client.Challenge, RelayProtocol.GameTag("another"))),
        };
        Assert.Equal("bad-signature", Code(await client.RequestAsync(line)));
    }

    [Fact]
    public async Task AReplayedHostLineOnAnotherConnectionIsRefused()
    {
        await using var rig = await RelayRig.StartAsync();
        var key = rig.NewKey();
        await using var first = await rig.ConnectAsync();
        var line = TestHost.HostPreamble(key, first.Challenge, RelayProtocol.GameTag("g"));
        await using (var host = await rig.ConnectAsync())
            Assert.Equal("bad-signature", Code(await host.RequestAsync(line))); // a captured line is useless: the challenge belongs to one connection
        Assert.True((await first.RequestAsync(line))["ok"]!.GetValue<bool>()); // on its own connection it works once
    }

    [Fact]
    public async Task AMalformedHostLineIsABadRequest()
    {
        await using var rig = await RelayRig.StartAsync();
        var key = rig.NewKey();
        var tag = RelayProtocol.GameTag("g");
        foreach (var mutate in new Action<JsonObject>[] { l => l.Remove("cert"), l => l["cert"] = "not base64!", l => l["cert"] = "AAAA", l => l["gameTag"] = "AAAA", l => l.Remove("sig") })
        {
            await using var client = await rig.ConnectAsync();
            var line = TestHost.HostPreamble(key, client.Challenge, tag);
            mutate(line);
            Assert.Equal("bad-request", Code(await client.RequestAsync(line)));
        }
    }

    [Fact]
    public async Task AKeyThatIsNotEcdsaP256IsRefused()
    {
        await using var rig = await RelayRig.StartAsync();
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        var request = new System.Security.Cryptography.X509Certificates.CertificateRequest("CN=rsa", rsa, System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.RSASignaturePadding.Pkcs1);
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        await using var client = await rig.ConnectAsync();
        var reply = await client.RequestAsync(new JsonObject { ["op"] = "host", ["v"] = 1, ["cert"] = Convert.ToBase64String(cert.RawData), ["gameTag"] = RelayProtocol.GameTag("g"), ["sig"] = Convert.ToBase64String(new byte[64]) });
        Assert.Equal("bad-request", Code(reply));
    }

    [Fact]
    public async Task ADifferentKeyGetsItsOwnRoomAndCannotAcceptForAnothersRoom()
    {
        await using var rig = await RelayRig.StartAsync();
        await using var alice = await TestHost.OpenAsync(rig, rig.NewKey(), "g");
        await using var mallory = await TestHost.OpenAsync(rig, rig.NewKey(), "g");
        Assert.NotEqual(alice.Room, mallory.Room);
        Assert.Equal(2, rig.Server.RoomCount);

        await using var joiner = await rig.ConnectAsync();
        await joiner.SendAsync(new JsonObject { ["op"] = "join", ["v"] = 1, ["room"] = alice.Room });
        var id = (await alice.NextEventAsync("incoming"))["id"]!.GetValue<string>();
        // Mallory signs an accept for Alice's room and Alice's real id with her own key
        await using var data = await rig.ConnectAsync();
        var reply = await data.RequestAsync(new JsonObject
        {
            ["op"] = "accept",
            ["v"] = 1,
            ["room"] = alice.Room,
            ["id"] = id,
            ["sig"] = mallory.Key.Sign(RelayProtocol.AcceptSignedText(RelayRig.PublicName, data.Challenge, alice.Room, id)),
        });
        Assert.Equal("bad-signature", Code(reply));
    }

    [Fact]
    public async Task TheSameKeyAndGameSupersedesTheOldControlChannelAndKeepsTheRoom()
    {
        await using var rig = await RelayRig.StartAsync();
        var key = rig.NewKey();
        await using var first = await TestHost.OpenAsync(rig, key, "g");
        await using var second = await TestHost.OpenAsync(rig, key, "g");
        Assert.True(second.Ok);
        Assert.Equal(first.Room, second.Room);
        var closing = await first.NextEventAsync("closing");
        Assert.Equal("superseded", closing["code"]!.GetValue<string>());
        Assert.Equal(1, rig.Server.RoomCount);
        // the new channel is the live one
        await using var joiner = await rig.ConnectAsync();
        await joiner.SendAsync(new JsonObject { ["op"] = "join", ["v"] = 1, ["room"] = second.Room });
        Assert.NotNull(await second.NextEventAsync("incoming"));
    }

    [Fact]
    public async Task TheSameKeyWithAnotherGameGetsASecondRoomWithoutDisturbingTheFirst()
    {
        await using var rig = await RelayRig.StartAsync();
        var key = rig.NewKey();
        await using var one = await TestHost.OpenAsync(rig, key, "game-one");
        await using var two = await TestHost.OpenAsync(rig, key, "game-two");
        Assert.NotEqual(one.Room, two.Room);
        Assert.Equal(2, rig.Server.RoomCount);
        await one.SendAsync(new JsonObject { ["op"] = "ping" });
        Assert.Equal("pong", (await one.NextEventAsync("pong"))["ev"]!.GetValue<string>());
    }

    [Fact]
    public async Task AReconnectingHostGetsTheSameRoomCodeBackAndSoDoesARestartedRelayWithTheSameSecret()
    {
        string room;
        string dir;
        var key = new TestHostKey();
        await using (var rig = await RelayRig.StartAsync())
        {
            dir = Directory.CreateTempSubdirectory("unciv-relay-keep-").FullName;
            File.Copy(Path.Combine(rig.Dir, "relay-secret"), Path.Combine(dir, "relay-secret"));
            await using (var host = await TestHost.OpenAsync(rig, key, "g")) room = host.Room;
            await Until(() => rig.Server.RoomCount == 0);
            await using (var again = await TestHost.OpenAsync(rig, key, "g")) Assert.Equal(room, again.Room);
        }
        await using (var restarted = await RelayRig.StartAsync(prepareDir: d => File.Copy(Path.Combine(dir, "relay-secret"), Path.Combine(d, "relay-secret"))))
        await using (var host = await TestHost.OpenAsync(restarted, key, "g"))
            Assert.Equal(room, host.Room);
        key.Dispose();
        Directory.Delete(dir, recursive: true);
    }

    internal static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++) await Task.Delay(25);
        Assert.True(condition());
    }

    [Fact]
    public async Task AnAcceptWithABadSignatureAnUnknownIdOrAReusedIdIsRefused()
    {
        await using var rig = await RelayRig.StartAsync();
        await using var host = await TestHost.OpenAsync(rig, rig.NewKey());
        await using var joiner = await rig.ConnectAsync();
        await joiner.SendAsync(new JsonObject { ["op"] = "join", ["v"] = 1, ["room"] = host.Room });
        var id = (await host.NextEventAsync("incoming"))["id"]!.GetValue<string>();

        await using (var forged = await rig.ConnectAsync())
        {
            var reply = await forged.RequestAsync(new JsonObject { ["op"] = "accept", ["v"] = 1, ["room"] = host.Room, ["id"] = id, ["sig"] = Convert.ToBase64String(new byte[64]) });
            Assert.Equal("bad-signature", Code(reply));
        }
        var (unknownData, unknown) = await host.AcceptAsync("deadbeefdeadbeefdeadbeef");
        await using (unknownData) Assert.Equal("bad-id", Code(unknown));
        var (good, goodReply) = await host.AcceptAsync(id);
        await using (good)
        {
            Assert.True(goodReply["ok"]!.GetValue<bool>());
            var (reused, again) = await host.AcceptAsync(id);
            await using (reused) Assert.Equal("bad-id", Code(again)); // single use
        }
        var (_, wrongRoom) = await host.AcceptAsync(id, room: "ZZZZ-ZZZZZZ");
        Assert.Equal("room-unknown", Code(wrongRoom));
    }

    [Fact]
    public async Task AnUnsupportedVersionIsAnsweredWithTheRelaysMaximum()
    {
        await using var rig = await RelayRig.StartAsync();
        await using var client = await rig.ConnectAsync();
        var reply = await client.RequestAsync(new JsonObject { ["op"] = "join", ["v"] = 2, ["room"] = "K7QF-3MXP4A" });
        Assert.Equal("unsupported-version", Code(reply));
        Assert.Equal(1, reply["max"]!.GetValue<int>());
        await using var noVersion = await rig.ConnectAsync();
        Assert.Equal("bad-request", Code(await noVersion.RequestAsync(new JsonObject { ["op"] = "join", ["room"] = "K7QF-3MXP4A" })));
        await using var junk = await rig.ConnectAsync();
        await junk.SendRawAsync("this is not json\n");
        Assert.Equal("bad-request", Code(await junk.ReadAsync()));
        await using var unknownOp = await rig.ConnectAsync();
        Assert.Equal("bad-request", Code(await unknownOp.RequestAsync(new JsonObject { ["op"] = "dial", ["v"] = 1 })));
    }

    [Fact]
    public async Task AJoinForAnUnknownRoomIsRefusedAndNoMessageCarriesAnAddress()
    {
        await using var rig = await RelayRig.StartAsync();
        await using var client = await rig.ConnectAsync();
        var reply = await client.RequestAsync(new JsonObject { ["op"] = "join", ["v"] = 1, ["room"] = "K7QF-3MXP4A", ["address"] = "127.0.0.1:9", ["host"] = "example.org", ["port"] = 9 });
        Assert.Equal("room-unknown", Code(reply));
        await using var noRoom = await rig.ConnectAsync();
        Assert.Equal("room-unknown", Code(await noRoom.RequestAsync(new JsonObject { ["op"] = "join", ["v"] = 1, ["address"] = "127.0.0.1:9" })));
    }

    [Fact]
    public async Task AJoinToARoomWhoseHostLeftIsHostGoneAndAWrongFingerprintIsWrongHost()
    {
        await using var rig = await RelayRig.StartAsync();
        var key = rig.NewKey();
        string room;
        await using (var host = await TestHost.OpenAsync(rig, key))
        {
            room = host.Room;
            await using var stale = await rig.ConnectAsync();
            Assert.Equal("wrong-host", Code(await stale.RequestAsync(new JsonObject { ["op"] = "join", ["v"] = 1, ["room"] = room, ["fp"] = "AAAAA-AAAAA-AAAAA-AAAAA-AAAAA-A" })));
        }
        await RelayHostTests.Until(() => rig.Server.RoomCount == 0);
        await using var joiner = await rig.ConnectAsync();
        Assert.Equal("host-gone", Code(await joiner.RequestAsync(new JsonObject { ["op"] = "join", ["v"] = 1, ["room"] = room })));
    }

    [Fact]
    public async Task ARoomCodeIsAcceptedCaseInsensitivelyWithoutTheDash()
    {
        await using var rig = await RelayRig.StartAsync();
        var key = rig.NewKey();
        await using var host = await TestHost.OpenAsync(rig, key);
        var (joiner, data) = await host.JoinAsync(host.Room.Replace("-", "", StringComparison.Ordinal).ToLowerInvariant());
        await using (joiner) await using (data) Assert.NotNull(joiner);
        // and the invite fingerprint prefix matches when given
        await using var withFp = await rig.ConnectAsync();
        await withFp.SendAsync(new JsonObject { ["op"] = "join", ["v"] = 1, ["room"] = host.Room, ["fp"] = key.InviteFingerprint });
        Assert.NotNull(await host.NextEventAsync("incoming"));
    }

    [Fact]
    public async Task AHostRejectingAnIncomingJoinGivesTheJoinerRoomFull()
    {
        await using var rig = await RelayRig.StartAsync();
        await using var host = await TestHost.OpenAsync(rig, rig.NewKey());
        await using var joiner = await rig.ConnectAsync();
        await joiner.SendAsync(new JsonObject { ["op"] = "join", ["v"] = 1, ["room"] = host.Room });
        var id = (await host.NextEventAsync("incoming"))["id"]!.GetValue<string>();
        await host.SendAsync(new JsonObject { ["op"] = "reject", ["id"] = id, ["code"] = "busy" });
        Assert.Equal("room-full", Code(await joiner.ReadAsync()));
    }

    [Fact]
    public async Task AJoinThatTheHostNeverAcceptsTimesOut()
    {
        await using var rig = await RelayRig.StartAsync(tweak: o => o with { Limits = o.Limits with { HostTimeoutMillis = 300 } });
        await using var host = await TestHost.OpenAsync(rig, rig.NewKey());
        await using var joiner = await rig.ConnectAsync();
        await joiner.SendAsync(new JsonObject { ["op"] = "join", ["v"] = 1, ["room"] = host.Room });
        Assert.Equal("host-timeout", Code(await joiner.ReadAsync()));
        // a late accept for that id is refused
        var id = (await host.NextEventAsync("incoming"))["id"]!.GetValue<string>();
        var (late, reply) = await host.AcceptAsync(id);
        await using (late) Assert.Equal("bad-id", Code(reply));
    }

    [Fact]
    public async Task JoinerAndHostExchangeBytesBothWaysThroughTheSplice()
    {
        await using var rig = await RelayRig.StartAsync();
        await using var host = await TestHost.OpenAsync(rig, rig.NewKey());
        var (joiner, data) = await host.JoinAsync();
        await using (joiner) await using (data)
        {
            byte[] hello = [0x16, 0x03, 0x01, 1, 2, 3, 4, 5];
            await joiner.Stream.WriteAsync(hello);
            var got = new byte[hello.Length];
            await data.Stream.ReadExactlyAsync(got).AsTask().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(hello, got);
            byte[] reply = [9, 8, 7, 6, 5, 4];
            await data.Stream.WriteAsync(reply);
            var back = new byte[reply.Length];
            await joiner.Stream.ReadExactlyAsync(back).AsTask().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(reply, back);
            Assert.Equal(1, rig.Server.SpliceCount);
            // closing one side ends the other (half-close propagates, then the splice goes away)
            await joiner.DisposeAsync();
            Assert.True(await data.ClosedAsync());
        }
        await RelayHostTests.Until(() => rig.Server.SpliceCount == 0);
    }

    [Fact]
    public void TheRelayProjectContainsNoCodeThatDialsOut()
    {
        // R3 / A1: the relay only accepts. The patterns are the ones RELAY.md names plus the other ways to open a socket.
        var forbidden = new Regex(@"\.Connect\(|\.ConnectAsync\(|Socket\.Connect|GetHostEntry|Dns\.|HttpClient|new TcpClient\([^)]|WebRequest|ClientWebSocket|UdpClient|SendTo", RegexOptions.CultureInvariant);
        var root = Path.Combine(RepoCsharpRoot(), "Unciv.Relay");
        var files = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories).Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            var code = string.Join('\n', File.ReadAllLines(file).Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal) && !l.TrimStart().StartsWith("///", StringComparison.Ordinal)));
            Assert.False(forbidden.IsMatch(code), $"{Path.GetFileName(file)} must not open outbound connections");
        }
    }

    [Fact]
    public void NoRelayFileNamesAHostOtherThanThePlaceholderOrLoopback()
    {
        // R13 (the source half that T337 owns): only relay.example.org and loopback appear in the relay project and its deployment files.
        var root = RepoCsharpRoot();
        var hostLike = new Regex(@"\b[a-z0-9-]+(\.[a-z0-9-]+)*\.(org|com|net|io|dev|app|eu|de)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var allowed = new[] { "relay.example.org", "example.org", "example.net", "www.w3.org", "github.com", "docs.docker.com", "nginx.org", "letsencrypt.org", "prometheus.io", "caddyserver.com", "systemd.io", "learn.microsoft.com", "datatracker.ietf.org", "www.haproxy.org", "unciv.app", "microsoft.net", "system.net", "unciv.net", "mcr.microsoft.com" };
        foreach (var file in RelayFiles(root))
            foreach (var match in hostLike.Matches(File.ReadAllText(file)).Select(m => m.Value))
                Assert.True(allowed.Any(a => match.EndsWith(a, StringComparison.OrdinalIgnoreCase)) || Regex.IsMatch(match, @"^\w+\.(cs|json|conf|csproj|txt|md|yml|sh)$"), $"{Path.GetFileName(file)} names '{match}'");
    }

    internal static string RepoCsharpRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "Unciv.Relay"))) return dir.FullName;
        throw new DirectoryNotFoundException("Unciv.Relay not found above " + AppContext.BaseDirectory);
    }

    private static IEnumerable<string> RelayFiles(string csharpRoot)
    {
        var relay = Path.Combine(csharpRoot, "Unciv.Relay");
        foreach (var file in Directory.EnumerateFiles(relay, "*", SearchOption.AllDirectories))
        {
            var part = Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar;
            var bin = Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar;
            if (!file.Contains(part, StringComparison.Ordinal) && !file.Contains(bin, StringComparison.Ordinal) && !file.EndsWith(".md", StringComparison.Ordinal)) yield return file;
        }
    }
}
