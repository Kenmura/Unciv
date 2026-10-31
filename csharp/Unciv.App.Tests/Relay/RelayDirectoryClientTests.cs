using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using Unciv.Client.Net.Hosting;
using Unciv.Net.Relay;
using Unciv.Relay;
using Unciv.Server.Transport;

namespace Unciv.Core.Tests.Relay;

// T339, RELAY.md R7/R10/R16: the browser reads the directory over the tunnel op get and trusts none of it.
public sealed class RelayDirectoryClientTests
{
    private static Task<RelayRig> RigAsync(Func<RelayOptions, RelayOptions>? tweak = null) =>
        RelayRig.StartAsync(RelayMode.Plain, o => (tweak ?? (x => x))(o with { PublicNames = [RelayRig.PublicName, "127.0.0.1"] }));

    private static RelayEndpoint EndpointOf(RelayRig rig) => new("127.0.0.1", rig.Port, rig.PinnedFingerprint);

    private static RelayDialOptions DialOf(RelayRig rig) => new() { UseTls = rig.UsesTls };

    [Fact]
    public async Task ListedGamesComeBackVerifiedWithTheRelaysNameAndFilters()
    {
        await using var rig = await RigAsync();
        using var key1 = rig.NewKey();
        using var key2 = rig.NewKey();
        await using var first = await TestHost.OpenAsync(rig, key1);
        await using var second = await TestHost.OpenAsync(rig, key2);
        Assert.True((await first.ListAsync(1, name: "Alpha game"))["ok"]!.GetValue<bool>());
        Assert.True((await second.ListAsync(1, body: TestHost.ListingBody(1, name: "Beta game", password: true, players: 4, slots: 4)))["ok"]!.GetValue<bool>());

        var all = await RelayDirectoryClient.FetchAsync(EndpointOf(rig), new DirectoryQuery(), DialOf(rig));
        Assert.Equal(2, all.Games.Count);
        Assert.Equal(0, all.Dropped);
        Assert.False(all.More);
        Assert.Contains(all.Games, g => g.Info.Name == "Beta game" && g.Info.PasswordRequired && g.Fingerprint == key2.InviteFingerprint);

        var search = await RelayDirectoryClient.FetchAsync(EndpointOf(rig), new DirectoryQuery { Search = "alpha" }, DialOf(rig));
        Assert.Equal("Alpha game", Assert.Single(search.Games).Info.Name);
        var withSlots = await RelayDirectoryClient.FetchAsync(EndpointOf(rig), new DirectoryQuery { OnlyWithSlots = true }, DialOf(rig));
        Assert.Equal("Alpha game", Assert.Single(withSlots.Games).Info.Name);
        var open = await RelayDirectoryClient.FetchAsync(EndpointOf(rig), new DirectoryQuery { PasswordRequired = false }, DialOf(rig));
        Assert.Equal("Alpha game", Assert.Single(open.Games).Info.Name);
    }

    [Fact]
    public async Task AnEmptyDirectoryIsEmptyNotAnError()
    {
        await using var rig = await RigAsync();
        var result = await RelayDirectoryClient.FetchAsync(EndpointOf(rig), new DirectoryQuery(), DialOf(rig));
        Assert.Empty(result.Games);
        Assert.False(string.IsNullOrEmpty(result.Info.Name));
    }

    [Fact]
    public async Task ARelayWithListingOffSaysSo()
    {
        await using var rig = await RigAsync(o => o with { Listing = ListingPolicy.Off });
        var e = await Assert.ThrowsAsync<RelayException>(() => RelayDirectoryClient.FetchAsync(EndpointOf(rig), new DirectoryQuery(), DialOf(rig)));
        Assert.Equal(RelayCodes.ListingOff, e.Code);
    }

    [Fact]
    public async Task ANoRelayAtTheAddressIsUnreachable()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var e = await Assert.ThrowsAsync<RelayException>(() => RelayDirectoryClient.FetchAsync(new RelayEndpoint("127.0.0.1", port), new DirectoryQuery(), new RelayDialOptions { UseTls = false }));
        Assert.Equal(RelayCodes.Unreachable, e.Code);
    }

    // A scripted relay: greets, reads each get, and answers with the next canned reply (or nothing).
    private sealed class FakeRelay : IDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly Queue<Func<string?>> replies = new();

        public FakeRelay(params Func<string?>[] script)
        {
            foreach (var reply in script) replies.Enqueue(reply);
            listener.Start();
            _ = Task.Run(AcceptAsync);
        }

        public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

        public RelayEndpoint Endpoint => new("127.0.0.1", Port);

        private async Task AcceptAsync()
        {
            try
            {
                while (true)
                {
                    var client = await listener.AcceptTcpClientAsync();
                    _ = Task.Run(() => ServeAsync(client));
                }
            }
            catch (Exception e) when (e is ObjectDisposedException or SocketException) { }
        }

        private async Task ServeAsync(TcpClient client)
        {
            using var owned = client;
            try
            {
                var stream = client.GetStream();
                await stream.WriteAsync(Encoding.UTF8.GetBytes("{\"challenge\":\"abc\"}\n"));
                var buffer = new byte[1024];
                _ = await stream.ReadAsync(buffer);
                Func<string?>? next;
                lock (replies) replies.TryDequeue(out next);
                var reply = next?.Invoke();
                if (reply == null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5));
                    return;
                }
                await stream.WriteAsync(Encoding.UTF8.GetBytes(reply));
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException or SocketException) { }
        }

        public void Dispose() => listener.Stop();
    }

    private static string Info(string name = "Test relay", string listing = "open") =>
        new JsonObject { ["ok"] = true, ["status"] = 200, ["data"] = new JsonObject { ["name"] = name, ["contact"] = "me@example.org", ["listing"] = listing, ["rooms"] = 2, ["listed"] = 1 } }.ToJsonString() + "\n";

    private static string Games(JsonArray games, string? next = null)
    {
        var data = new JsonObject { ["games"] = games };
        if (next != null) data["next"] = next;
        return new JsonObject { ["ok"] = true, ["status"] = 200, ["data"] = data }.ToJsonString() + "\n";
    }

    private static JsonObject Entry(TestHostKey key, string body, string room) => new()
    {
        ["room"] = room,
        ["fp"] = key.FingerprintHex,
        ["body"] = body,
        ["sig"] = key.Sign(RelayProtocol.ListingSignedText(body)),
        ["cert"] = key.CertBase64,
        ["ageSeconds"] = 3,
    };

    private static readonly RelayDialOptions Plain = new() { UseTls = false };

    [Fact]
    public async Task ForgedDuplicateAndHostileEntriesAreDroppedAndCounted()
    {
        using var key = new TestHostKey();
        using var evil = new TestHostKey();
        var good = Entry(key, TestHost.ListingBody(1), "aaaa-aaaaaa");
        var duplicate = Entry(key, TestHost.ListingBody(2, name: "Same room"), "aaaa-aaaaaa");
        var forged = Entry(key, TestHost.ListingBody(1), "bbbb-bbbbbb");
        forged["body"] = TestHost.ListingBody(1, name: "Forged");
        var bidi = Entry(evil, TestHost.ListingBody(1, name: "Evil‮game"), "cccc-cccccc");
        using var fake = new FakeRelay(() => Info(), () => Games([good, duplicate, forged, bidi, JsonValue.Create(5)]));
        var result = await RelayDirectoryClient.FetchAsync(fake.Endpoint, new DirectoryQuery(), Plain);
        Assert.Equal("Friendly game", Assert.Single(result.Games).Info.Name);
        Assert.Equal(4, result.Dropped);
    }

    [Fact]
    public async Task EntriesThatDoNotMatchTheQueryAreDroppedEvenIfTheRelayIgnoredIt()
    {
        using var key = new TestHostKey();
        var entry = Entry(key, TestHost.ListingBody(1, name: "Zebra game"), "aaaa-aaaaaa");
        using var fake = new FakeRelay(() => Info(), () => Games([entry]));
        var result = await RelayDirectoryClient.FetchAsync(fake.Endpoint, new DirectoryQuery { Search = "apple" }, Plain);
        Assert.Empty(result.Games);
        Assert.Equal(1, result.Dropped);
    }

    [Fact]
    public async Task MoreIsReportedAndAHostileRelayNameFallsBackToItsAddress()
    {
        using var fake = new FakeRelay(() => Info(name: "Bad‮name"), () => Games([], next: "cursor"));
        var result = await RelayDirectoryClient.FetchAsync(fake.Endpoint, new DirectoryQuery(), Plain);
        Assert.True(result.More);
        Assert.DoesNotContain('‮', result.Info.Name);
        Assert.Equal(fake.Endpoint.Key, result.Info.Name);
    }

    [Fact]
    public async Task AListingPolicyOfOffInTheInfoStopsBeforeTheListIsAsked()
    {
        using var fake = new FakeRelay(() => Info(listing: "off"));
        var e = await Assert.ThrowsAsync<RelayException>(() => RelayDirectoryClient.FetchAsync(fake.Endpoint, new DirectoryQuery(), Plain));
        Assert.Equal(RelayCodes.ListingOff, e.Code);
    }

    [Fact]
    public async Task NotFoundOnTheGamesPathMeansNoDirectory()
    {
        using var fake = new FakeRelay(() => Info(), () => "{\"ok\":false,\"code\":\"not-found\",\"status\":404}\n");
        var e = await Assert.ThrowsAsync<RelayException>(() => RelayDirectoryClient.FetchAsync(fake.Endpoint, new DirectoryQuery(), Plain));
        Assert.Equal(RelayCodes.ListingOff, e.Code);
    }

    [Fact]
    public async Task AnUnsupportedVersionMeansTheRelayIsTooOld()
    {
        using var fake = new FakeRelay(() => "{\"ok\":false,\"code\":\"unsupported-version\",\"message\":\"no\"}\n");
        var e = await Assert.ThrowsAsync<RelayException>(() => RelayDirectoryClient.FetchAsync(fake.Endpoint, new DirectoryQuery(), Plain));
        Assert.Equal(RelayCodes.TooOld, e.Code);
    }

    [Fact]
    public async Task NonJsonAndNonListRepliesAreFailuresNotCrashes()
    {
        using var junk = new FakeRelay(() => "this is not json\n");
        await Assert.ThrowsAsync<RelayException>(() => RelayDirectoryClient.FetchAsync(junk.Endpoint, new DirectoryQuery(), Plain));
        using var notList = new FakeRelay(() => Info(), () => new JsonObject { ["ok"] = true, ["data"] = new JsonObject { ["games"] = "nope" } }.ToJsonString() + "\n");
        await Assert.ThrowsAsync<RelayException>(() => RelayDirectoryClient.FetchAsync(notList.Endpoint, new DirectoryQuery(), Plain));
    }

    [Fact]
    public async Task ASilentRelayTimesOutAsNoAnswer()
    {
        using var fake = new FakeRelay(() => null);
        var e = await Assert.ThrowsAsync<RelayException>(() => RelayDirectoryClient.FetchAsync(fake.Endpoint, new DirectoryQuery(), Plain, replyTimeout: TimeSpan.FromMilliseconds(300)));
        Assert.Equal(RelayCodes.NoAnswer, e.Code);
    }

    [Fact]
    public async Task AnOversizeReplyIsRefused()
    {
        using var fake = new FakeRelay(() => "{\"ok\":true,\"data\":\"" + new string('x', RelayProtocol.GetReplyMaxBytes + 10) + "\"}\n");
        var e = await Assert.ThrowsAsync<RelayException>(() => RelayDirectoryClient.FetchAsync(fake.Endpoint, new DirectoryQuery(), Plain));
        Assert.Equal(RelayCodes.Failed, e.Code);
    }

    [Fact]
    public void TheQueryPathEscapesTextAndAcceptsMatchesTheRelaysTests()
    {
        var query = new DirectoryQuery { Search = "a b&c", Ruleset = "Civ V - Vanilla", OnlyWithSlots = true, PasswordRequired = true, HideStarted = true };
        var path = query.ToPath();
        Assert.StartsWith("/v1/games?", path, StringComparison.Ordinal);
        Assert.Contains("q=a%20b%26c", path, StringComparison.Ordinal);
        Assert.Contains("hasSlots=1", path, StringComparison.Ordinal);
        Assert.Contains("passwordRequired=1", path, StringComparison.Ordinal);
        Assert.Contains("started=0", path, StringComparison.Ordinal);
    }
}
