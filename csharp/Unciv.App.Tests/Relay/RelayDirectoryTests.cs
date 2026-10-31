using System.Text.Json.Nodes;
using Unciv.Net.Relay;
using Unciv.Net.Turns;
using Unciv.Relay;

namespace Unciv.Core.Tests.Relay;

// T337, RELAY.md R7: the lobby directory, served over the tunnel op get and the optional HTTP listener.
public sealed class RelayDirectoryTests
{
    private static string Code(JsonObject reply) => reply["code"]!.GetValue<string>();

    private static JsonArray Games(JsonObject reply) => reply["data"]!["games"]!.AsArray();

    [Fact]
    public async Task ASignedListingAppearsOverTheTunnelAndTheHttpListenerAndUnlistRemovesIt()
    {
        await using var rig = await RelayRig.StartAsync();
        await using var host = await TestHost.OpenAsync(rig, rig.NewKey());
        Assert.Empty(Games(await rig.GetOverTunnelAsync("/v1/games")));
        Assert.True((await host.ListAsync(1))["ok"]!.GetValue<bool>());

        var games = Games(await rig.GetOverTunnelAsync("/v1/games"));
        var entry = Assert.Single(games)!;
        Assert.Equal(host.Room, entry["room"]!.GetValue<string>());
        Assert.Equal(host.Key.FingerprintHex, entry["fp"]!.GetValue<string>());
        Assert.Equal(host.Key.CertBase64, entry["cert"]!.GetValue<string>());
        var body = entry["body"]!.GetValue<string>();
        Assert.Equal(TestHost.ListingBody(1), body); // forwarded verbatim, so the signature still verifies
        Assert.Equal(host.Key.Sign(RelayProtocol.ListingSignedText(body)).Length, entry["sig"]!.GetValue<string>().Length);

        var http = await rig.GetAsync("/v1/games");
        Assert.Equal(200, http.Status);
        Assert.Contains("max-age=5", http.Headers, StringComparison.Ordinal);
        Assert.Contains(host.Room, http.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("127.0.0.1", http.Body, StringComparison.Ordinal); // a listing never carries an address

        var single = await rig.GetOverTunnelAsync("/v1/games/" + host.Room);
        Assert.Equal(host.Room, single["data"]!["room"]!.GetValue<string>());

        await host.SendAsync(new JsonObject { ["op"] = "unlist" });
        await host.NextAsync();
        Assert.Empty(Games(await rig.GetOverTunnelAsync("/v1/games")));
        Assert.Equal("not-found", Code(await rig.GetOverTunnelAsync("/v1/games/" + host.Room)));
    }

    [Fact]
    public async Task AListingDisappearsWhenTheControlChannelEnds()
    {
        await using var rig = await RelayRig.StartAsync();
        await using (var host = await TestHost.OpenAsync(rig, rig.NewKey()))
        {
            await host.ListAsync(1);
            Assert.Single(Games(await rig.GetOverTunnelAsync("/v1/games")));
        }
        await RelayHostTests.Until(() => rig.Server.ListedCount == 0);
    }

    [Fact]
    public async Task ASilentHostLosesRoomAndListingAfterNinetySecondsAndAnUnupdatedListingAfterThirtyMinutes()
    {
        var clock = new ManualClock(10_000_000);
        await using var rig = await RelayRig.StartAsync(clock: clock);
        await using var silent = await TestHost.OpenAsync(rig, rig.NewKey(), "silent");
        await using var quiet = await TestHost.OpenAsync(rig, rig.NewKey(), "quiet");
        await silent.ListAsync(1);
        await quiet.ListAsync(1);
        clock.AdvanceSeconds(89);
        await rig.Server.SweepAsync();
        Assert.Equal(2, rig.Server.ListedCount);
        await quiet.SendAsync(new JsonObject { ["op"] = "ping" }); // the quiet one keeps pinging every 20 s, but never updates
        await quiet.NextEventAsync("pong");
        clock.AdvanceSeconds(2);
        await rig.Server.SweepAsync();
        await RelayHostTests.Until(() => rig.Server.RoomCount == 1);
        Assert.Equal(1, rig.Server.ListedCount);
        for (var minute = 0; minute < 31; minute++)
        {
            clock.AdvanceSeconds(60);
            await quiet.SendAsync(new JsonObject { ["op"] = "ping" });
            await quiet.NextEventAsync("pong");
            await rig.Server.SweepAsync();
        }
        Assert.Equal(1, rig.Server.RoomCount);
        Assert.Equal(0, rig.Server.ListedCount); // 30 minutes without an update
        Assert.True((await quiet.ListAsync(2))["ok"]!.GetValue<bool>()); // and it can list again
    }

    [Fact]
    public async Task AnOverlongControlLineIsRefused()
    {
        await using var rig = await RelayRig.StartAsync();
        await using var host = await TestHost.OpenAsync(rig, rig.NewKey());
        await host.Control.SendRawAsync(new string('x', 4100) + "\n");
        var closing = await host.NextEventAsync("closing");
        Assert.Equal("too-large", closing["code"]!.GetValue<string>());
        Assert.Null(await host.NextAsync());
    }

    [Fact]
    public async Task ARejectedListingKeepsTheControlChannelOpen()
    {
        await using var rig = await RelayRig.StartAsync();
        await using var host = await TestHost.OpenAsync(rig, rig.NewKey());
        var valid = TestHost.ListingBody(5);
        var cases = new (string Why, JsonObject Message)[]
        {
            ("not signed by the host", new JsonObject { ["op"] = "list", ["body"] = valid, ["sig"] = Convert.ToBase64String(new byte[64]) }),
            ("signature text", new JsonObject { ["op"] = "list", ["body"] = valid, ["sig"] = "***" }),
            ("no body", new JsonObject { ["op"] = "list", ["sig"] = "AAAA" }),
            ("not json", host.ListMessage("list", "{not json")),
            ("not an object", host.ListMessage("list", "[1]")),
            ("missing field", host.ListMessage("list", ((JsonObject)JsonNode.Parse(valid)!).Also(o => o.Remove("map")).ToJsonString())),
            ("wrong type", host.ListMessage("list", ((JsonObject)JsonNode.Parse(valid)!).Also(o => o["players"] = "one").ToJsonString())),
            ("too many mods", host.ListMessage("list", TestHost.ListingBody(6, mods: Enumerable.Range(0, 17).Select(i => "m" + i).ToArray()))),
            ("body over 1 KiB", host.ListMessage("list", TestHost.ListingBody(7, name: new string('a', 40), mods: Enumerable.Range(0, 16).Select(i => new string('m', 100)).ToArray()))),
            ("name too short", host.ListMessage("list", TestHost.ListingBody(8, name: "ab"))),
            ("name too long", host.ListMessage("list", TestHost.ListingBody(9, name: new string('a', 41)))),
            ("bidi override", host.ListMessage("list", TestHost.ListingBody(10, name: "evil\u202Egame"))),
            ("zero width", host.ListMessage("list", TestHost.ListingBody(11, hostName: "Al\u200Bice"))),
            ("control char", host.ListMessage("list", TestHost.ListingBody(12, name: "line\nbreak"))),
            ("update before list", host.ListMessage("update", TestHost.ListingBody(14))),
        };
        foreach (var (why, message) in cases)
        {
            await host.SendAsync(message);
            var reply = await host.NextAsync() ?? throw new EndOfStreamException(why);
            Assert.Equal("listing-rejected", Code(reply)); // " + why
        }
        Assert.Equal(0, rig.Server.ListedCount);
        Assert.True((await host.ListAsync(20))["ok"]!.GetValue<bool>());
        Assert.Equal("listing-rejected", Code(await host.ListAsync(20))); // not strictly increasing
        Assert.Equal("listing-rejected", Code(await host.ListAsync(19, "update")));
        Assert.True((await host.ListAsync(21, "update"))["ok"]!.GetValue<bool>());
    }

    [Fact]
    public async Task OneKeyListsOneRoomAtATime()
    {
        await using var rig = await RelayRig.StartAsync();
        var key = rig.NewKey();
        await using var one = await TestHost.OpenAsync(rig, key, "one");
        await using var two = await TestHost.OpenAsync(rig, key, "two");
        Assert.True((await one.ListAsync(1))["ok"]!.GetValue<bool>());
        Assert.Equal("listing-rejected", Code(await two.ListAsync(1)));
    }

    [Fact]
    public async Task TheListingRateAndTheListingCountAreLimited()
    {
        await using var rig = await RelayRig.StartAsync(tweak: o => o with { Limits = o.Limits with { ListingsPerMinute = 1, MaxRooms = 2 } });
        await using var a = await TestHost.OpenAsync(rig, rig.NewKey(), "a");
        await using var b = await TestHost.OpenAsync(rig, rig.NewKey(), "b");
        Assert.True((await a.ListAsync(1))["ok"]!.GetValue<bool>());
        Assert.Equal("rate-limited", Code(await b.ListAsync(1)));
    }

    [Fact]
    public async Task DenyNamesAndTheListingPolicyAreEnforced()
    {
        await using (var rig = await RelayRig.StartAsync(prepareDir: d => File.WriteAllText(Path.Combine(d, "deny-names.txt"), "# comment\n^official\\b\nscam\n")))
        {
            await using var host = await TestHost.OpenAsync(rig, rig.NewKey());
            Assert.Equal("listing-rejected", Code(await host.ListAsync(1, name: "Official Unciv Server")));
            Assert.Equal("listing-rejected", Code(await host.ListAsync(2, name: "total SCAM here")));
            Assert.True((await host.ListAsync(3, name: "A fine game"))["ok"]!.GetValue<bool>());
        }
        await using (var off = await RelayRig.StartAsync(tweak: o => o with { Listing = ListingPolicy.Off }))
        {
            await using var host = await TestHost.OpenAsync(off, off.NewKey());
            Assert.Equal("listing-disabled", Code(await host.ListAsync(1)));
            Assert.Equal("not-found", Code(await off.GetOverTunnelAsync("/v1/games")));
            Assert.Equal("off", (await off.GetOverTunnelAsync("/v1/info"))["data"]!["listing"]!.GetValue<string>());
        }
        var allowed = new TestHostKey();
        var stranger = new TestHostKey();
        try
        {
            await using var list = await RelayRig.StartAsync(tweak: o => o with { Listing = ListingPolicy.Allowlist }, prepareDir: d => File.WriteAllText(Path.Combine(d, "allow-keys.txt"), allowed.FingerprintHex + "\n"));
            await using var yes = await TestHost.OpenAsync(list, allowed);
            await using var no = await TestHost.OpenAsync(list, stranger, "x");
            Assert.True((await yes.ListAsync(1))["ok"]!.GetValue<bool>());
            Assert.Equal("listing-rejected", Code(await no.ListAsync(1)));
        }
        finally
        {
            allowed.Dispose();
            stranger.Dispose();
        }
    }

    [Fact]
    public async Task FiltersAndPagingReturnTheExpectedRows()
    {
        var clock = new ManualClock(20_000_000);
        await using var rig = await RelayRig.StartAsync(clock: clock);
        var hosts = new List<TestHost>();
        string[] names = ["Alpha", "Bravo", "Charlie", "Delta"];
        for (var i = 0; i < names.Length; i++)
        {
            var host = await TestHost.OpenAsync(rig, rig.NewKey(), "g" + i);
            hosts.Add(host);
            var body = TestHost.ListingBody(1, name: names[i] + " game", hostName: "Host" + i, players: i == 3 ? 4 : 1, slots: 4, ruleset: i == 1 ? "Civ V - Gods & Kings" : "Civ V - Vanilla",
                password: i == 2, started: i == 3, version: i == 0 ? "0.9" : "1.0", mods: i == 2 ? ["Terrain Mod"] : null);
            Assert.True((await host.ListAsync(1, body: body))["ok"]!.GetValue<bool>());
            clock.AdvanceSeconds(10); // distinct firstSeen so the order is defined
        }
        string[] Rooms(JsonObject reply) => Games(reply).Select(g => JsonNode.Parse(g!["body"]!.GetValue<string>())!["name"]!.GetValue<string>()).ToArray();

        Assert.Equal(["Delta game", "Charlie game", "Bravo game", "Alpha game"], Rooms(await rig.GetOverTunnelAsync("/v1/games"))); // newest first
        Assert.Equal(["Bravo game"], Rooms(await rig.GetOverTunnelAsync("/v1/games?q=brav")));
        Assert.Equal(["Bravo game"], Rooms(await rig.GetOverTunnelAsync("/v1/games?ruleset=Civ%20V%20-%20Gods%20%26%20Kings")));
        Assert.Equal(["Charlie game"], Rooms(await rig.GetOverTunnelAsync("/v1/games?mod=Terrain%20Mod")));
        Assert.Equal(["Charlie game"], Rooms(await rig.GetOverTunnelAsync("/v1/games?passwordRequired=1")));
        Assert.Equal(["Delta game"], Rooms(await rig.GetOverTunnelAsync("/v1/games?started=1")));
        Assert.Equal(["Alpha game"], Rooms(await rig.GetOverTunnelAsync("/v1/games?version=0.9")));
        Assert.Equal(["Charlie game", "Bravo game", "Alpha game"], Rooms(await rig.GetOverTunnelAsync("/v1/games?hasSlots=1")));
        Assert.Empty(Rooms(await rig.GetOverTunnelAsync("/v1/games?q=nothing")));

        var first = await rig.GetOverTunnelAsync("/v1/games?limit=3");
        Assert.Equal(["Delta game", "Charlie game", "Bravo game"], Rooms(first));
        var next = first["data"]!["next"]!.GetValue<string>();
        var second = await rig.GetOverTunnelAsync("/v1/games?limit=3&cursor=" + Uri.EscapeDataString(next));
        Assert.Equal(["Alpha game"], Rooms(second));
        Assert.Null(second["data"]!["next"]);
        Assert.Equal(4, Games(await rig.GetOverTunnelAsync("/v1/games?limit=500")).Count);

        foreach (var host in hosts) await host.DisposeAsync();
    }

    [Fact]
    public async Task InfoAndHealthAndMetricsRespond()
    {
        await using var rig = await RelayRig.StartAsync(tweak: o => o with { Metrics = true, Name = "Test relay", Contact = "abuse@relay.example.org", Motd = "hello" });
        await using var host = await TestHost.OpenAsync(rig, rig.NewKey());
        await host.ListAsync(1);
        var info = (await rig.GetOverTunnelAsync("/v1/info"))["data"]!;
        Assert.Equal("Test relay", info["name"]!.GetValue<string>());
        Assert.Equal("abuse@relay.example.org", info["contact"]!.GetValue<string>());
        Assert.Equal(1, info["rooms"]!.GetValue<int>());
        Assert.Equal(1, info["listed"]!.GetValue<int>());
        Assert.Equal(200, (await rig.GetAsync("/healthz")).Status);
        Assert.Contains("rooms=1", (await rig.GetAsync("/healthz")).Body, StringComparison.Ordinal);
        var metrics = await rig.GetAsync("/metrics");
        Assert.Contains("unciv_relay_rooms 1", metrics.Body, StringComparison.Ordinal);
        Assert.Equal(200, (await rig.GetAsync("/v1/info")).Status);
    }

    [Fact]
    public async Task MetricsAreOffUnlessAskedFor()
    {
        await using var rig = await RelayRig.StartAsync();
        Assert.Equal(404, (await rig.GetAsync("/metrics")).Status);
    }

    [Fact]
    public async Task TheHttpListenerIsReadOnlyAndRateLimitedAndHonoursETags()
    {
        await using var rig = await RelayRig.StartAsync(tweak: o => o with { Limits = o.Limits with { HttpRequestsPerMinute = 6 } });
        var first = await rig.GetAsync("/v1/info");
        var etag = first.Headers.Split("\r\n").First(h => h.StartsWith("ETag:", StringComparison.OrdinalIgnoreCase))[5..].Trim();
        Assert.Equal(304, (await rig.GetAsync("/v1/info", $"If-None-Match: {etag}\r\n")).Status);
        Assert.Equal(405, (await rig.HttpAsync("POST /v1/games HTTP/1.1\r\nHost: x\r\n\r\n")).Status);
        Assert.Equal(400, (await rig.HttpAsync("GET /v1/games HTTP/1.1\r\nHost: x\r\nContent-Length: 5\r\n\r\nhello")).Status);
        Assert.Equal(404, (await rig.GetAsync("/nothing")).Status);
        Assert.Equal(400, (await rig.HttpAsync("GARBAGE\r\n\r\n")).Status);
        Assert.Equal(429, (await rig.GetAsync("/v1/info")).Status);
    }

    [Fact]
    public async Task AnOversizedHttpRequestHeadIsRefused()
    {
        await using var rig = await RelayRig.StartAsync();
        var response = await rig.HttpAsync("GET /v1/info HTTP/1.1\r\nHost: x\r\nX-Pad: " + new string('a', 9000) + "\r\n\r\n");
        Assert.Equal(431, response.Status);
    }

    [Fact]
    public async Task TheHttpSideTrustsXForwardedForOnlyFromATrustedProxy()
    {
        await using var rig = await RelayRig.StartAsync(tweak: o => o with { Limits = o.Limits with { HttpRequestsPerMinute = 1 } });
        // loopback is trusted by default, so two different forwarded clients have separate budgets
        Assert.Equal(200, (await rig.GetAsync("/healthz", "X-Forwarded-For: 203.0.113.1\r\n")).Status);
        Assert.Equal(200, (await rig.GetAsync("/healthz", "X-Forwarded-For: 203.0.113.2\r\n")).Status);
        Assert.Equal(429, (await rig.GetAsync("/healthz", "X-Forwarded-For: 203.0.113.1\r\n")).Status);
        await using var untrusting = await RelayRig.StartAsync(tweak: o => o with { TrustedProxies = [System.Net.IPNetwork.Parse("10.0.0.0/8")], Limits = o.Limits with { HttpRequestsPerMinute = 1 } });
        Assert.Equal(200, (await untrusting.GetAsync("/healthz", "X-Forwarded-For: 203.0.113.1\r\n")).Status);
        Assert.Equal(429, (await untrusting.GetAsync("/healthz", "X-Forwarded-For: 203.0.113.2\r\n")).Status); // the header was ignored: same socket address
    }

    [Fact]
    public async Task AnOversizedGetReplyIsRefusedAndABadPathIsABadRequest()
    {
        await using var rig = await RelayRig.StartAsync();
        Assert.Equal("bad-request", Code(await rig.GetOverTunnelAsync("v1/games")));
        Assert.Equal("not-found", Code(await rig.GetOverTunnelAsync("/etc/passwd")));
        Assert.Equal("not-found", Code(await rig.GetOverTunnelAsync("/v1/games/!!!")));
        await using var client = await rig.ConnectAsync();
        Assert.Equal("bad-request", Code(await client.RequestAsync(new JsonObject { ["op"] = "get", ["v"] = 1 })));
    }

    [Fact]
    public void ListingTextRulesRejectTheDangerousCharactersAndAcceptOrdinaryText()
    {
        Assert.True(ListingValidator.TextOk("Zażółć gęślą jaźń", 1, 40));
        Assert.True(ListingValidator.TextOk("日本語のゲーム", 1, 40));
        Assert.True(ListingValidator.TextOk("😀 party", 1, 40));
        Assert.False(ListingValidator.TextOk(null, 0, 10));
        Assert.False(ListingValidator.TextOk("", 1, 10));
        Assert.False(ListingValidator.TextOk("x\u2028y", 1, 10)); // line separator
        Assert.False(ListingValidator.TextOk("a\uE000b", 1, 10)); // private use
        Assert.False(ListingValidator.TextOk("a\uD800b", 1, 10)); // lone surrogate
        Assert.False(ListingValidator.TextOk("a\uFEFFb", 1, 10)); // BOM / zero-width no-break space
        Assert.False(ListingValidator.TextOk(new string('a', 11), 1, 10));
    }
}

internal static class JsonObjectExtensions
{
    public static JsonObject Also(this JsonObject json, Action<JsonObject> change)
    {
        change(json);
        return json;
    }
}
