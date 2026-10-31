using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using Unciv.Client.Net.Hosting;
using Unciv.Client.Net.Session;
using Unciv.Client.Net.Transport;
using Unciv.Logic.Map;
using Unciv.Net;
using Unciv.Net.Identity;
using Unciv.Net.Lobby;
using Unciv.Net.Relay;
using Unciv.Relay;
using Unciv.Server;
using Unciv.Server.Transport;

namespace Unciv.Core.Tests.Relay;

// T338: the client side of the relay (design/RELAY.md R1, R9 host side, R10 wire, R11, R12, R13): the host dials out, a joiner reaches it by room code, the game's own TLS runs through the splice.
[Collection("RulesetCache")]
public sealed class RelayClientTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(60);

    private static Task<RelayRig> RigAsync(RelayMode mode = RelayMode.Plain, Func<RelayOptions, RelayOptions>? tweak = null) =>
        RelayRig.StartAsync(mode, o => (tweak ?? (x => x))(o with { PublicNames = [RelayRig.PublicName, "127.0.0.1"] }));

    private static RelayEndpoint EndpointOf(RelayRig rig) => new("127.0.0.1", rig.Port, rig.PinnedFingerprint);

    private static RelayDialOptions DialOf(RelayRig rig) => new() { UseTls = rig.UsesTls };

    private static RelayListenerOptions Fast(RelayRig rig) => new()
    {
        Dial = DialOf(rig),
        MinBackoff = TimeSpan.FromMilliseconds(50),
        MaxBackoff = TimeSpan.FromMilliseconds(200),
        ListingMinInterval = TimeSpan.FromMilliseconds(50),
    };

    private static async Task<(ITransport Host, ITransport Joiner)> PairAsync(RelayListener listener, RelayRig rig, string? fingerprint)
    {
        var join = TcpTlsClientTransport.ConnectViaRelayAsync(EndpointOf(rig), listener.Room!, fingerprint, dial: DialOf(rig));
        var host = await listener.AcceptTransportAsync().WaitAsync(Bound);
        return (host, await join.WaitAsync(Bound));
    }

    // ------------------------------------------------------------------ pieces that need no relay

    [Theory]
    [InlineData("unciv-relay://relay.example.org", "relay.example.org", 47830, null)]
    [InlineData("  UNCIV-RELAY://Relay.Example.ORG:9000/ ", "Relay.Example.ORG", 9000, null)]
    [InlineData("unciv-relay://[::1]:5000", "::1", 5000, null)]
    [InlineData("unciv-relay://[::1]", "::1", 47830, null)]
    public void Endpoint_Parses(string text, string host, int port, string? fingerprint)
    {
        Assert.True(RelayEndpoint.TryParse(text, out var endpoint));
        Assert.Equal((host, port, fingerprint), (endpoint!.Host, endpoint.Port, endpoint.Fingerprint));
        Assert.True(RelayEndpoint.TryParse(endpoint.ToString(), out var again));
        Assert.Equal(endpoint, again);
    }

    [Fact]
    public void Endpoint_ParsesAPinnedFingerprint()
    {
        var fp = HostKey.Fingerprint([1, 2, 3]);
        Assert.True(RelayEndpoint.TryParse($"unciv-relay://203.0.113.10:47830?fp={fp}", out var endpoint));
        Assert.True(HostKey.Matches(fp, endpoint!.Fingerprint));
        Assert.True(RelayEndpoint.TryParse(endpoint.ToString(), out var again));
        Assert.Equal(endpoint, again);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("relay.example.org")]
    [InlineData("unciv://relay.example.org")]
    [InlineData("unciv-relay://")]
    [InlineData("unciv-relay://:5")]
    [InlineData("unciv-relay://host:0")]
    [InlineData("unciv-relay://host:65536")]
    [InlineData("unciv-relay://host:x")]
    [InlineData("unciv-relay://host/path")]
    [InlineData("unciv-relay://host?fp=nope")]
    [InlineData("unciv-relay://host?other=1")]
    [InlineData("unciv-relay://two words")]
    [InlineData("unciv-relay://[::1")]
    public void Endpoint_RejectsWhatIsNotARelayUrl(string? text) => Assert.False(RelayEndpoint.TryParse(text, out _));

    [Fact]
    public void Endpoint_SignedHostIsLowerCaseAndCanonical()
    {
        Assert.Equal("relay.example.org", new RelayEndpoint("Relay.Example.ORG", 1).SignedHost);
        Assert.Equal("::1", new RelayEndpoint("0:0:0:0:0:0:0:1", 1).SignedHost);
        Assert.Equal("[::1]:7", new RelayEndpoint("::1", 7).Key);
    }

    [Fact]
    public void RelayInvite_RoundTrips_AndDirectInvitesAreUnchanged()
    {
        var fp = HostKey.Fingerprint([1, 2, 3]);
        var text = InviteCode.FormatRelay("relay.example.org", 47830, "k7qf3mxp4a", fp, "tok");
        Assert.StartsWith("unciv://relay.example.org:47830/r/K7QF-3MXP4A/", text, StringComparison.Ordinal);
        Assert.True(InviteCode.TryParse(text, out var invite));
        Assert.Equal(("relay.example.org", 47830, "K7QF-3MXP4A", "tok"), (invite!.Host, invite.Port, invite.Room, invite.Token));
        Assert.True(invite.IsRelay);
        Assert.True(HostKey.Matches(fp, invite.Fingerprint));
        Assert.Equal(text, invite.ToString());

        Assert.True(InviteCode.TryParse($"unciv://relay.example.org/r/K7QF-3MXP4A/{fp}", out var defaulted));
        Assert.Equal(47830, defaulted!.Port);
        Assert.Null(defaulted.Token);

        Assert.True(InviteCode.TryParse(InviteCode.Format("host.example", 47900, fp), out var direct));
        Assert.False(direct!.IsRelay);
        Assert.Equal(47900, direct.Port);
        Assert.True(InviteCode.TryParse($"unciv://host.example/{fp}", out var directDefault));
        Assert.Equal(InviteCode.DefaultPort, directDefault!.Port);
    }

    [Theory]
    [InlineData("unciv://relay.example.org/r/K7QF-3MXP4A")]
    [InlineData("unciv://relay.example.org/r/K7QF-3MXP4A/not-a-fingerprint")]
    [InlineData("unciv://relay.example.org/r/short/AAAAA-AAAAA-AAAAA-AAAAA-AAAAA-A")]
    [InlineData("unciv://relay.example.org/r/K7QF-3MXP4A/AAAAA-AAAAA-AAAAA-AAAAA-AAAAA-A/tok/extra")]
    [InlineData("unciv://relay.example.org/r")]
    public void RelayInvite_RejectsMalformed(string text) => Assert.False(InviteCode.TryParse(text, out _));

    [Fact]
    public void KnownHosts_AHostThatMovedKeepsItsPin_AndRelayAddressesNeverShareTheRelaysKey()
    {
        var known = new KnownHosts();
        var fp = HostKey.Fingerprint([9, 9]);
        Assert.True(known.Pin("192.0.2.1:47821", fp));
        Assert.True(known.IsFingerprintKnown(fp));
        Assert.False(known.IsFingerprintKnown(HostKey.Fingerprint([8])));
        var a = KnownHosts.RelayAddress("Relay.Example.org:47830", "k7qf3mxp4a");
        var b = KnownHosts.RelayAddress("relay.example.org:47830", "ZZZZ-ZZZZZZ");
        Assert.Equal("relay.example.org:47830/K7QF-3MXP4A", a);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void RelayCodes_MapTheRelaysWords()
    {
        Assert.Equal(RelayCodes.RoomUnknown, RelayCodes.FromWire("room-unknown"));
        Assert.Equal(RelayCodes.HostGone, RelayCodes.FromWire("host-gone"));
        Assert.Equal(RelayCodes.RoomFull, RelayCodes.FromWire("room-full"));
        Assert.Equal(RelayCodes.TooOld, RelayCodes.FromWire("unsupported-version"));
        Assert.Equal(RelayCodes.WrongHost, RelayCodes.FromWire("wrong-host"));
        Assert.Equal(RelayCodes.Failed, RelayCodes.FromWire("something-new"));
        Assert.Equal(RelayCodes.Failed, RelayCodes.FromWire(null));
    }

    [Fact]
    public void ServerOptions_RelayFlags()
    {
        Assert.Null(ServerOptions.Parse([]).Relay);
        var o = ServerOptions.Parse(["--relay", "unciv-relay://relay.example.org:9", "--relay-fingerprint", "ABCDEFGHIJKLMNOPQRSTUVWXYZ", "--list", "--list-name", "Friday game", "--print-invite"]);
        Assert.Equal(("relay.example.org", 9), (o.Relay!.Host, o.Relay.Port));
        Assert.Equal(HostKey.Normalize("ABCDEFGHIJKLMNOPQRSTUVWXYZ"), o.Relay.Fingerprint);
        Assert.True(o.List);
        Assert.Equal("Friday game", o.ListName);
        Assert.Contains("--relay URL", ServerOptions.HelpText, StringComparison.Ordinal);
        Assert.Throws<ServerUsageException>(() => ServerOptions.Parse(["--relay", "relay.example.org"]));
        Assert.Throws<ServerUsageException>(() => ServerOptions.Parse(["--list"]));
        Assert.Throws<ServerUsageException>(() => ServerOptions.Parse(["--relay-fingerprint", "ABCDEFGHIJKLMNOPQRSTUVWXYZ"]));
        Assert.Throws<ServerUsageException>(() => ServerOptions.Parse(["--relay", "unciv-relay://r.example.org", "--list-name", "x"]));
        Assert.Throws<ServerUsageException>(() => ServerOptions.Parse(["--relay", "unciv-relay://r.example.org", "--relay-fingerprint", "bad"]));
    }

    [Fact]
    public void RelaySettings_SettingsFields_ComposeSplitAndValidate()
    {
        var fp = HostKey.Fingerprint([1, 2, 3, 4]);
        var url = RelaySettings.Compose("  unciv-relay://relay.example.org:47831 ", fp);
        Assert.Equal("unciv-relay://relay.example.org:47831?fp=" + fp, url);
        Assert.Equal(fp, RelayEndpoint.TryParse(url, out var parsed) ? parsed!.Fingerprint : null);
        Assert.Equal(("unciv-relay://relay.example.org:47831", fp), RelaySettings.Split(url));
        Assert.Equal("unciv-relay://r.example.org?fp=" + fp, RelaySettings.Compose("unciv-relay://r.example.org?fp=" + HostKey.Fingerprint([9, 9]), fp)); // a given fingerprint replaces the pasted one
        Assert.Equal("unciv-relay://r.example.org?fp=" + fp, RelaySettings.Compose("unciv-relay://r.example.org?fp=" + fp, "")); // none given: the pasted one stays
        Assert.Equal("unciv-relay://r.example.org", RelaySettings.Compose("unciv-relay://r.example.org", "  "));
        Assert.Equal("", RelaySettings.Compose(null, fp));
        Assert.Equal(("not a relay?fp=x", ""), RelaySettings.Split("not a relay?fp=x"));
        Assert.Equal(("", ""), RelaySettings.Split(null));
        Assert.Equal(("unciv-relay://r.example.org", ""), RelaySettings.Split("unciv-relay://r.example.org"));
        Assert.Null(RelaySettings.FingerprintProblem(""));
        Assert.Null(RelaySettings.FingerprintProblem(fp));
        Assert.NotNull(RelaySettings.FingerprintProblem("abc"));
        Assert.Null(RelaySettings.Problem(url));
    }

    [Fact]
    public void RelaySettings_ForgetOneRelay_AsksAgainNextTime()
    {
        var settings = new RelaySettings();
        var friend = new RelayEndpoint("Friend.example.net", 47830);
        Assert.True(settings.Choose(friend.Host, friend.Port).NeedsPrompt);
        settings.Remember(friend);
        Assert.False(settings.Choose("friend.example.net", 47830).NeedsPrompt);
        Assert.False(settings.Forget("other.example.net:1"));
        Assert.True(settings.Forget("FRIEND.example.net:47830"));
        Assert.True(settings.Choose("friend.example.net", 47830).NeedsPrompt);
    }

    [Fact]
    public void RelaySettings_FreshInstallHasNoRelay_AndTheOwnersDefaultFileOnlyFillsAMissingOne()
    {
        var dir = Directory.CreateTempSubdirectory("unciv-relay-settings-").FullName;
        try
        {
            var path = Path.Combine(dir, "relay.json");
            var defaults = Path.Combine(dir, "relay-default.json");
            var fresh = RelaySettings.Load(path, defaults);
            Assert.Null(fresh.Endpoint);
            Assert.False(fresh.HostThroughRelay);
            Assert.True(fresh.ReachMeThroughRelay); // on by default once a relay is set

            File.WriteAllText(defaults, """{"url":"unciv-relay://relay.example.org"}""");
            var filled = RelaySettings.Load(path, defaults);
            Assert.Equal("relay.example.org", filled.Endpoint!.Host);
            Assert.True(filled.HostThroughRelay);

            filled.Url = "unciv-relay://other.example.org:1";
            filled.ReachMeThroughRelay = false;
            filled.Remember(new RelayEndpoint("friend.example.net", 47830));
            filled.Remember(new RelayEndpoint("FRIEND.example.net", 47830));
            filled.Save(path);
            var back = RelaySettings.Load(path, defaults);
            Assert.Equal("other.example.org", back.Endpoint!.Host);
            Assert.False(back.HostThroughRelay);
            Assert.Single(back.AcceptedRelays);

            File.WriteAllText(path, "{ damaged");
            Assert.Null(RelaySettings.Load(path, defaults).Endpoint); // a damaged file is never papered over with the default
            Assert.Null(RelaySettings.Problem(""));
            Assert.NotNull(RelaySettings.Problem("relay.example.org"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void RelaySettings_ChooseAsksOnceForARelayTheInviteNamed_AndNeverAddsItToTheSetting()
    {
        var settings = new RelaySettings { Url = "unciv-relay://mine.example.org:47830?fp=" + HostKey.Fingerprint([4]) };
        var mine = settings.Choose("MINE.example.org", 47830);
        Assert.False(mine.NeedsPrompt);
        Assert.NotNull(mine.Endpoint.Fingerprint); // the configured pin is kept
        Assert.False(mine.Dial.TrustAnyCertificate);

        var friends = settings.Choose("friend.example.net", 47830);
        Assert.True(friends.NeedsPrompt);
        Assert.True(friends.Dial.TrustAnyCertificate);
        settings.Remember(friends.Endpoint);
        Assert.False(settings.Choose("friend.example.net", 47830).NeedsPrompt);
        Assert.True(settings.Choose("friend.example.net", 47831).NeedsPrompt); // another port is another relay
        Assert.Equal("mine.example.org", settings.Endpoint!.Host);
    }

    // T380: the fail-closed dial decision of App.ConnectAsync, now in RelaySettings.ConnectInviteAsync so a test can break it.
    [Fact]
    public async Task RelaySettings_ConnectInvite_AnUnagreedRelayThrowsBeforeAnyDial_ConfiguredAndAgreedOnesDial()
    {
        var settings = new RelaySettings { Url = "unciv-relay://mine.example.org:47830?fp=" + HostKey.Fingerprint([4]) };
        settings.Remember(new RelayEndpoint("friend.example.net", 47830));
        var dialled = new List<(string Key, bool TrustAny, string? Fingerprint)>();
        Task<ITransport> Dialer(RelayEndpoint relay, string room, string? fingerprint, KnownHosts known, RelayDialOptions dial, CancellationToken token)
        {
            dialled.Add((relay.Key, dial.TrustAnyCertificate, fingerprint));
            throw new TimeoutException("reached the dial");
        }

        var stranger = await Assert.ThrowsAsync<RelayException>(() => settings.ConnectInviteAsync("stranger.example.net", 47830, "K7M2-9XQ4AB", null, new KnownHosts(), CancellationToken.None, Dialer));
        Assert.Equal(RelayCodes.Declined, stranger.Code);
        await Assert.ThrowsAsync<RelayException>(() => settings.ConnectInviteAsync("friend.example.net", 47831, "K7M2-9XQ4AB", null, new KnownHosts(), CancellationToken.None, Dialer)); // another port is another relay
        Assert.Empty(dialled);

        var hostFp = HostKey.Fingerprint([7]);
        await Assert.ThrowsAsync<TimeoutException>(() => settings.ConnectInviteAsync("MINE.example.org", 47830, "K7M2-9XQ4AB", hostFp, new KnownHosts(), CancellationToken.None, Dialer));
        await Assert.ThrowsAsync<TimeoutException>(() => settings.ConnectInviteAsync("friend.example.net", 47830, "K7M2-9XQ4AB", null, new KnownHosts(), CancellationToken.None, Dialer));
        Assert.Equal([("mine.example.org:47830", false, (string?)hostFp), ("friend.example.net:47830", true, null)], dialled);
    }

    [Fact]
    public void RelaySettings_NullAcceptedRelaysInTheFile_MeansNone()
    {
        var dir = Directory.CreateTempSubdirectory("unciv-relay-null-").FullName;
        try
        {
            var path = Path.Combine(dir, "relay.json");
            File.WriteAllText(path, """{"url":"unciv-relay://relay.example.org","acceptedRelays":null}""");
            var loaded = RelaySettings.Load(path);
            Assert.Empty(loaded.AcceptedRelays);
            Assert.True(loaded.Choose("friend.example.net", 47830).NeedsPrompt);
            loaded.Remember(new RelayEndpoint("friend.example.net", 47830));
            Assert.False(loaded.Forget("nobody:1"));
            Assert.True(loaded.Save(path));
            Assert.Single(RelaySettings.Load(path).AcceptedRelays);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void RelaySettings_SaveReportsAFailedWrite()
    {
        var dir = Directory.CreateTempSubdirectory("unciv-relay-save-").FullName;
        try
        {
            var blocker = Path.Combine(dir, "not-a-folder");
            File.WriteAllText(blocker, "x");
            Assert.False(new RelaySettings().Save(Path.Combine(blocker, "relay.json"))); // the parent is a file
            Assert.True(new RelaySettings().Save(Path.Combine(dir, "ok", "relay.json")));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // R13: no source file, fixture or config names a relay other than the placeholder or loopback.
    [Fact]
    public void NoRelayHostnameOtherThanThePlaceholderIsInTheSource()
    {
        var root = AppContext.BaseDirectory;
        while (root != null && !File.Exists(Path.Combine(root, "Unciv.sln"))) root = Path.GetDirectoryName(root);
        Assert.NotNull(root);
        var pattern = new System.Text.RegularExpressions.Regex(@"unciv-relay://([A-Za-z0-9][A-Za-z0-9.-]*)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        static bool Placeholder(string host) => !host.Contains('.', StringComparison.Ordinal) || host.EndsWith(".example.org", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".example.net", StringComparison.OrdinalIgnoreCase)
            || host.StartsWith("203.0.113.", StringComparison.Ordinal) || host == "127.0.0.1";
        var offenders = new List<string>();
        var count = 0;
        foreach (var file in Directory.EnumerateFiles(root!, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root!, file);
            if (relative.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) || relative.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || relative.StartsWith("bin", StringComparison.Ordinal) || relative.StartsWith("obj", StringComparison.Ordinal) || relative.Contains(".worktrees", StringComparison.Ordinal)) continue;
            if (Path.GetExtension(file) is not (".cs" or ".json" or ".md" or ".yml" or ".yaml" or ".conf" or ".example" or ".service" or ".txt" or ".cfg" or ".tscn" or ".csproj")) continue;
            foreach (System.Text.RegularExpressions.Match match in pattern.Matches(File.ReadAllText(file)))
            {
                count++;
                if (!Placeholder(match.Groups[1].Value.TrimEnd('.', ','))) offenders.Add($"{relative}: {match.Value}");
            }
        }
        Assert.True(count > 0, "the scan found no relay URLs at all: it is looking in the wrong place");
        Assert.Empty(offenders);
    }

    // ------------------------------------------------------------------ the tunnel (R12: the same connectivity in every TLS mode)

    [Theory]
    [InlineData(RelayMode.Plain)]
    [InlineData(RelayMode.Certificate)]
    [InlineData(RelayMode.SelfSigned)]
    [InlineData(RelayMode.Proxy)]
    public async Task HostAndJoiner_ReachEachOtherThroughTheRelay_InEveryTlsMode(RelayMode mode)
    {
        await using var rig = await RigAsync(mode);
        using var cert = HostCertificate.Generate();
        await using var listener = new RelayListener(EndpointOf(rig), cert, "game-1", Fast(rig));
        listener.Start();
        await listener.WaitForRoomAsync(CancellationToken.None).WaitAsync(Bound);
        Assert.Matches("^[0-9A-Z]{4}-[0-9A-Z]{6}$", listener.Room);

        Assert.True(InviteCode.TryParse(listener.Invite(), out var invite));
        Assert.Equal(listener.Room, invite!.Room);
        Assert.True(HostKey.Matches(HostCertificate.GetFingerprint(cert), invite.Fingerprint));

        var (host, joiner) = await PairAsync(listener, rig, invite.Fingerprint);
        await using (host)
        await using (joiner)
        {
            await joiner.SendAsync("ping from the joiner");
            Assert.Equal("ping from the joiner", await host.ReceiveAsync().AsTask().WaitAsync(Bound));
            await host.SendAsync("pong from the host");
            Assert.Equal("pong from the host", await joiner.ReceiveAsync().AsTask().WaitAsync(Bound));
            Assert.False(string.IsNullOrEmpty(((IPeerTransport)host).PeerId));
        }
    }

    [Fact]
    public async Task TheHostSeesAnOpaquePeerId_StablePerJoinerNetwork_AndNeverTheAddress()
    {
        await using var rig = await RigAsync(RelayMode.Proxy);
        using var cert = HostCertificate.Generate();
        await using var listener = new RelayListener(EndpointOf(rig), cert, "game-1", Fast(rig));
        listener.Start();
        await listener.WaitForRoomAsync(CancellationToken.None).WaitAsync(Bound);

        async Task<string?> PeerFromAsync(string address)
        {
            rig.Proxy!.ClientAddress = address;
            var (host, joiner) = await PairAsync(listener, rig, fingerprint: null);
            await using (host)
            await using (joiner) return ((IPeerTransport)host).PeerId;
        }

        var a1 = await PeerFromAsync("198.51.100.20");
        var a2 = await PeerFromAsync("198.51.100.20");
        var b = await PeerFromAsync("198.51.100.21");
        Assert.NotNull(a1);
        Assert.Equal(a1, a2);
        Assert.NotEqual(a1, b);
        Assert.DoesNotContain("198", a1, StringComparison.Ordinal);
        Assert.Matches("^[0-9a-f]{16}$", a1);
    }

    [Fact]
    public async Task ARelayInviteForAnotherHostsKey_IsRefusedBeforeAnyGameByte()
    {
        await using var rig = await RigAsync();
        using var cert = HostCertificate.Generate();
        await using var listener = new RelayListener(EndpointOf(rig), cert, "game-1", Fast(rig));
        listener.Start();
        await listener.WaitForRoomAsync(CancellationToken.None).WaitAsync(Bound);

        var stale = await Assert.ThrowsAsync<RelayException>(() =>
            TcpTlsClientTransport.ConnectViaRelayAsync(EndpointOf(rig), listener.Room!, HostKey.Fingerprint([1, 2, 3]), dial: DialOf(rig)));
        Assert.Equal(RelayCodes.WrongHost, stale.Code);
        using var shortWait = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => listener.AcceptTransportAsync(shortWait.Token)); // the host was never even knocked
    }

    [Fact]
    public async Task ARelayThatSubstitutesItsOwnCertificate_IsRefusedWithHostKeyMismatch_AndNoFrameIsDelivered()
    {
        using var attacker = HostCertificate.Generate();
        using var real = HostCertificate.Generate();
        var pretend = new TcpListener(IPAddress.Loopback, 0);
        pretend.Start();
        var received = new List<string>();
        var attackerRun = Task.Run(async () =>
        {
            using var tcp = await pretend.AcceptTcpClientAsync();
            var stream = tcp.GetStream();
            await stream.WriteAsync(Encoding.UTF8.GetBytes("{\"challenge\":\"AAAA\",\"v\":1}\n"));
            await RelayChannel.ReadLineAsync(stream, CancellationToken.None); // the join line
            await stream.WriteAsync(Encoding.UTF8.GetBytes("{\"ok\":true}\n"));
            try
            {
                var transport = await TcpTlsListener.HandshakeOverAsync(tcp, stream, attacker, TimeSpan.FromSeconds(5));
                while (await transport.ReceiveAsync() is { } frame) received.Add(frame);
            }
            catch (Exception e) when (e is IOException or System.Security.Authentication.AuthenticationException or OperationCanceledException) { }
        });
        try
        {
            var endpoint = new RelayEndpoint("127.0.0.1", ((IPEndPoint)pretend.LocalEndpoint).Port);
            var failure = await Assert.ThrowsAsync<RelayException>(() =>
                TcpTlsClientTransport.ConnectViaRelayAsync(endpoint, "K7QF-3MXP4A", HostCertificate.GetFingerprint(real), dial: new RelayDialOptions { UseTls = false }));
            Assert.Equal("host-key-mismatch", failure.Code);
            await attackerRun.WaitAsync(Bound);
            Assert.Empty(received);
        }
        finally { pretend.Stop(); }
    }

    [Fact]
    public async Task Failures_AreNamed_RelayDown_UnknownRoom_HostGone_RoomFull()
    {
        var free = new TcpListener(IPAddress.Loopback, 0);
        free.Start();
        var deadPort = ((IPEndPoint)free.LocalEndpoint).Port;
        free.Stop();
        var down = await Assert.ThrowsAsync<RelayException>(() =>
            TcpTlsClientTransport.ConnectViaRelayAsync(new RelayEndpoint("127.0.0.1", deadPort), "K7QF-3MXP4A", dial: new RelayDialOptions { UseTls = false }));
        Assert.Equal(RelayCodes.Unreachable, down.Code);

        await using var rig = await RigAsync();
        var unknown = await Assert.ThrowsAsync<RelayException>(() => TcpTlsClientTransport.ConnectViaRelayAsync(EndpointOf(rig), "K7QF-3MXP4A", dial: DialOf(rig)));
        Assert.Equal(RelayCodes.RoomUnknown, unknown.Code);
        var notACode = await Assert.ThrowsAsync<RelayException>(() => TcpTlsClientTransport.ConnectViaRelayAsync(EndpointOf(rig), "nope", dial: DialOf(rig)));
        Assert.Equal(RelayCodes.RoomUnknown, notACode.Code);

        using var cert = HostCertificate.Generate();
        string room;
        await using (var busy = new RelayListener(EndpointOf(rig), cert, "game-1", Fast(rig) with { MaxPendingJoins = 0 }))
        {
            busy.Start();
            await busy.WaitForRoomAsync(CancellationToken.None).WaitAsync(Bound);
            room = busy.Room!;
            var full = await Assert.ThrowsAsync<RelayException>(() => TcpTlsClientTransport.ConnectViaRelayAsync(EndpointOf(rig), room, dial: DialOf(rig)));
            Assert.Equal(RelayCodes.RoomFull, full.Code);
        }
        // the host closed its game: the relay still knows the room for a while but nobody answers it
        using var waitForClose = new CancellationTokenSource(Bound);
        RelayException? gone = null;
        while (gone == null && !waitForClose.IsCancellationRequested)
        {
            try { await TcpTlsClientTransport.ConnectViaRelayAsync(EndpointOf(rig), room, dial: DialOf(rig)); }
            catch (RelayException e) when (e.Code == RelayCodes.HostGone) { gone = e; }
            catch (RelayException) { await Task.Delay(50); }
        }
        Assert.NotNull(gone);
    }

    [Fact]
    public async Task ARelayWithAnUntrustedCertificate_IsNamed_AndAcceptedOnlyWhenPinnedOrAgreedTo()
    {
        await using var rig = await RigAsync(RelayMode.SelfSigned);
        var unpinned = new RelayEndpoint("127.0.0.1", rig.Port);
        var refused = await Assert.ThrowsAsync<RelayException>(() => TcpTlsClientTransport.ConnectViaRelayAsync(unpinned, "K7QF-3MXP4A"));
        Assert.Equal(RelayCodes.CertificateRejected, refused.Code);

        var wrongPin = unpinned with { Fingerprint = HostKey.Fingerprint([5, 5, 5]) };
        var mismatch = await Assert.ThrowsAsync<RelayException>(() => TcpTlsClientTransport.ConnectViaRelayAsync(wrongPin, "K7QF-3MXP4A"));
        Assert.Equal(RelayCodes.CertificateRejected, mismatch.Code);

        var agreed = await Assert.ThrowsAsync<RelayException>(() => TcpTlsClientTransport.ConnectViaRelayAsync(unpinned, "K7QF-3MXP4A", dial: new RelayDialOptions { TrustAnyCertificate = true }));
        Assert.Equal(RelayCodes.RoomUnknown, agreed.Code); // it got as far as asking the relay
    }

    [Fact]
    public async Task ThePreambleCarriesNoGameData_AndASpliceCapturedAtTheRelayHoldsNoPlaintext()
    {
        await using var rig = await RigAsync();
        using var cert = HostCertificate.Generate();
        await using var listener = new RelayListener(EndpointOf(rig), cert, "game-1", Fast(rig));
        listener.Start();
        await listener.WaitForRoomAsync(CancellationToken.None).WaitAsync(Bound);
        using var capture = new CapturingProxy(rig.Port);
        var viaCapture = new RelayEndpoint("127.0.0.1", capture.Port);

        var join = TcpTlsClientTransport.ConnectViaRelayAsync(viaCapture, listener.Room!, HostCertificate.GetFingerprint(cert), dial: new RelayDialOptions { UseTls = false });
        var host = await listener.AcceptTransportAsync().WaitAsync(Bound);
        await using var joiner = await join.WaitAsync(Bound);
        await using (host)
        {
            await joiner.SendAsync("{\"type\":\"Join\",\"displayName\":\"Mallory-the-Display-Name\",\"password\":\"hunter2-secret\",\"ruleset\":\"Civ V - Vanilla\"}");
            Assert.Contains("Mallory-the-Display-Name", await host.ReceiveAsync().AsTask().WaitAsync(Bound), StringComparison.Ordinal);
            await host.SendAsync("{\"type\":\"Welcome\",\"ruleset\":\"Civ V - Vanilla\"}");
            await joiner.ReceiveAsync().AsTask().WaitAsync(Bound);
        }
        var seen = capture.Captured();
        Assert.True(seen.Length > 500, "the capture should hold the whole inner handshake");
        foreach (var secret in new[] { "Mallory-the-Display-Name", "hunter2-secret", "Civ V - Vanilla", "Welcome" })
            Assert.False(Contains(seen, Encoding.UTF8.GetBytes(secret)), $"'{secret}' is readable on the wire at the relay");
        Assert.True(Contains(seen, Encoding.UTF8.GetBytes("\"join\"")), "sanity: the capture does hold the plain preamble");
    }

    private static bool Contains(byte[] haystack, byte[] needle) => haystack.AsSpan().IndexOf(needle) >= 0;

    /// <summary>Forwards TCP to the relay and records both directions, like a hostile wire between the player and the relay.</summary>
    private sealed class CapturingProxy : IDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly int relayPort;
        private readonly MemoryStream recorded = new();
        private readonly CancellationTokenSource stop = new();

        public CapturingProxy(int relayPort)
        {
            this.relayPort = relayPort;
            listener.Start();
            _ = AcceptLoopAsync();
        }

        public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

        public byte[] Captured()
        {
            lock (recorded) return recorded.ToArray();
        }

        private async Task AcceptLoopAsync()
        {
            while (!stop.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await listener.AcceptTcpClientAsync(stop.Token); }
                catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException or SocketException or InvalidOperationException) { return; }
                _ = ForwardAsync(client);
            }
        }

        private async Task ForwardAsync(TcpClient client)
        {
            using (client)
            using (var upstream = new TcpClient())
            {
                try
                {
                    await upstream.ConnectAsync(IPAddress.Loopback, relayPort, stop.Token);
                    var down = client.GetStream();
                    var up = upstream.GetStream();
                    await Task.WhenAny(Pump(down, up), Pump(up, down));
                }
                catch (Exception e) when (e is IOException or OperationCanceledException or SocketException or ObjectDisposedException) { }
            }
        }

        private async Task Pump(Stream from, Stream to)
        {
            var buffer = new byte[8192];
            int n;
            while ((n = await from.ReadAsync(buffer, stop.Token)) > 0)
            {
                lock (recorded) recorded.Write(buffer, 0, n);
                await to.WriteAsync(buffer.AsMemory(0, n), stop.Token);
            }
        }

        public void Dispose()
        {
            stop.Cancel();
            listener.Stop();
        }
    }

    // ------------------------------------------------------------------ reconnect (R11) and the listing publisher

    [Fact]
    public async Task TheHostReconnectsWithBackoffAndReclaimsTheSameRoom_AfterARelayRestart()
    {
        var free = new TcpListener(IPAddress.Loopback, 0);
        free.Start();
        var port = ((IPEndPoint)free.LocalEndpoint).Port;
        free.Stop();
        RelayOptions? used = null;
        await using var rig = await RigAsync(RelayMode.Plain, o => used = o with { TunnelPort = port });
        using var cert = HostCertificate.Generate();
        var changes = new List<bool>();
        await using var listener = new RelayListener(EndpointOf(rig), cert, "game-1", Fast(rig));
        listener.ConnectionChanged += up => { lock (changes) changes.Add(up); };
        listener.Start();
        await listener.WaitForRoomAsync(CancellationToken.None).WaitAsync(Bound);
        var room = listener.Room;

        await rig.Server.DisposeAsync(); // the relay goes away: every connection ends
        await WaitUntilAsync(() => !listener.Connected);

        await using var second = new RelayServer(used!, rig.Clock, output: null);
        second.Start();
        Assert.Equal(port, second.TunnelPort);
        await WaitUntilAsync(() => listener.Connected);
        Assert.Equal(room, listener.Room); // a stable relay secret in the data dir keeps the code
        lock (changes) Assert.Equal([true, false, true], changes);

        var (host, joiner) = await PairAsync(listener, rig, fingerprint: HostCertificate.GetFingerprint(cert));
        await using (host)
        await using (joiner)
        {
            await joiner.SendAsync("after the restart");
            Assert.Equal("after the restart", await host.ReceiveAsync().AsTask().WaitAsync(Bound));
        }
    }

    [Fact]
    public async Task TheListingIsPublished_UpdatedCoalesced_AndRemovedWhenTheGameCloses()
    {
        await using var rig = await RigAsync();
        using var cert = HostCertificate.Generate();
        await using var listener = new RelayListener(EndpointOf(rig), cert, "game-1", Fast(rig));
        listener.SetListing(Listing(players: 1)); // set before the relay is even reached: published on connect
        listener.Start();
        await listener.WaitForRoomAsync(CancellationToken.None).WaitAsync(Bound);

        var first = await WaitForListingAsync(rig, g => g?["body"] != null);
        var body = JsonNode.Parse(first["body"]!.GetValue<string>())!;
        Assert.Equal(("Friday night", 1, true), (body["name"]!.GetValue<string>(), body["players"]!.GetValue<int>(), body["passwordRequired"]!.GetValue<bool>()));
        Assert.Equal(listener.Room!.Replace("-", "", StringComparison.Ordinal), first["room"]!.GetValue<string>().Replace("-", "", StringComparison.Ordinal));
        Assert.DoesNotContain("password", first["body"]!.GetValue<string>().Replace("passwordRequired", "", StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase);

        listener.SetListing(Listing(players: 2));
        listener.SetListing(Listing(players: 3)); // coalesced with the one before
        var updated = await WaitForListingAsync(rig, g => g != null && JsonNode.Parse(g["body"]!.GetValue<string>())!["players"]!.GetValue<int>() == 3);
        Assert.NotNull(updated);

        listener.SetListing(null);
        await WaitForListingAsync(rig, g => g == null);
        listener.SetListing(Listing(players: 1));
        await WaitForListingAsync(rig, g => g != null);
        await listener.DisposeAsync(); // closing the game closes the control channel: the listing goes with it
        await WaitForListingAsync(rig, g => g == null);
    }

    private static RelayListing Listing(int players) => new() { Name = "Friday night", HostName = "Alice", Players = players, Slots = 4, PasswordRequired = true, Mods = ["Some Mod"] };

    private static async Task<JsonObject> WaitForListingAsync(RelayRig rig, Func<JsonObject?, bool> predicate)
    {
        using var cts = new CancellationTokenSource(Bound);
        while (true)
        {
            var reply = await rig.GetOverTunnelAsync("/v1/games");
            var game = reply["data"]?["games"]?.AsArray().FirstOrDefault()?.AsObject();
            if (predicate(game)) return game ?? reply;
            await Task.Delay(50, cts.Token);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(condition))] string what = "", [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
    {
        using var cts = new CancellationTokenSource(Bound);
        while (!condition())
        {
            try { await Task.Delay(20, cts.Token); }
            catch (OperationCanceledException) { throw new TimeoutException($"line {line}: never became true: {what}"); }
        }
    }

    // ------------------------------------------------------------------ the whole game (R1, R9, R11)

    private static LobbySettings GameSettings(string? password) => new()
    {
        GameName = "Through the relay",
        MaxMajorCivs = 3,
        CityStateCount = 0,
        TurnTimerSeconds = 0,
        Password = password,
        Map = new MapParameters { Type = MapType.Pangaea, Shape = MapShape.Rectangular, Seed = 3, MapSize = MapSize.Tiny },
    };

    private static ClientSession Friend(RelayRig rig, InviteCode invite, ClientIdentity identity, string name, LobbySettings settings, string? password, int reconnects = 0, RelayEndpoint? endpoint = null, RelayDialOptions? dial = null)
    {
        var relay = endpoint ?? EndpointOf(rig);
        return new ClientSession(token => TcpTlsClientTransport.ConnectViaRelayAsync(relay, invite.Room!, invite.Fingerprint, dial: dial ?? DialOf(rig), cancellationToken: token), new ClientSessionOptions
        {
            Identity = identity,
            DisplayName = name,
            RulesetHash = settings.ComputeRulesetHash(),
            ExpectedFingerprint = invite.Fingerprint,
            HostAddress = KnownHosts.RelayAddress(relay.Key, invite.Room!),
            Password = password,
            MaxReconnectAttempts = reconnects,
            ReconnectDelay = (_, token) => Task.Delay(100, token),
            KeepAliveInterval = Timeout.InfiniteTimeSpan,
            HostSilenceTimeout = Timeout.InfiniteTimeSpan,
        });
    }

    [Fact]
    public async Task ASimultaneousGame_IsPlayedThroughTheRelay_AndSurvivesTheRelayRestarting()
    {
        Unciv.Harness.ScenarioGame.LoadVanilla();
        var free = new TcpListener(IPAddress.Loopback, 0);
        free.Start();
        var port = ((IPEndPoint)free.LocalEndpoint).Port;
        free.Stop();
        RelayOptions? used = null;
        await using var rig = await RigAsync(RelayMode.SelfSigned, o => used = o with { TunnelPort = port });
        var settings = GameSettings("through-the-relay");
        using var hostId = ClientIdentity.Generate();
        using var aliceId = ClientIdentity.Generate();
        using var bobId = ClientIdentity.Generate();
        using var cert = HostCertificate.Generate();

        await using var hosting = await HostingSession.StartLobbyAsync(settings, hostId, cert, "Host", 0, EndpointOf(rig), Fast(rig));
        await hosting.Relay!.WaitForRoomAsync(CancellationToken.None).WaitAsync(Bound);
        Assert.True(InviteCode.TryParse(hosting.RelayInvite(), out var invite));
        Assert.True(invite!.IsRelay);
        Assert.True(InviteCode.TryParse(hosting.Invite("127.0.0.1"), out var direct)); // the direct address keeps working beside the relay
        Assert.False(direct!.IsRelay);

        await using var alice = Friend(rig, invite, aliceId, "Alice", settings, "through-the-relay", reconnects: 60);
        await using var bob = Friend(rig, invite, bobId, "Bob", settings, "through-the-relay", reconnects: 60);
        Assert.True((await alice.JoinAsync().WaitAsync(Bound)).Joined);
        Assert.True((await bob.JoinAsync().WaitAsync(Bound)).Joined);
        await alice.SetLobbySlotAsync(1, null, null, true);
        await bob.SetLobbySlotAsync(2, null, null, true);

        var game = await hosting.Lobby!.StartGameAsync(force: true, seed: 42, clockMillis: 1000).WaitAsync(Bound);
        await WaitUntilAsync(() => alice.State == ClientSessionState.InGame && bob.State == ClientSessionState.InGame && alice.Game.Snapshot != null && bob.Game.Snapshot != null);
        var aliceView = alice.Game.ViewHash;
        var bobView = bob.Game.ViewHash;
        var hostHash = Unciv.Commands.Log.ReplayVerifier.FullHash(game.Game);

        var firstRound = await Task.WhenAll(hosting.HostPlayer!.SubmitAsync(new Unciv.Commands.Turn.EndTurn()).WaitAsync(Bound), alice.SubmitAsync(new Unciv.Commands.Turn.EndTurn()).WaitAsync(Bound), bob.SubmitAsync(new Unciv.Commands.Turn.EndTurn()).WaitAsync(Bound));
        Assert.All(firstRound, o => Assert.True(o.Accepted, o.Code));
        await WaitUntilAsync(() => alice.Game.Snapshot is { Turn: >= 1 } && bob.Game.Snapshot is { Turn: >= 1 });
        // Let the round's trailing events land before taking the hashes to compare after the restart.
        for (var settled = 0; settled < 10;)
        {
            await Task.Delay(100);
            var (a, b) = (alice.Game.ViewHash, bob.Game.ViewHash);
            settled = (a, b) == (aliceView, bobView) ? settled + 1 : 0;
            (aliceView, bobView) = (a, b);
        }
        hostHash = Unciv.Commands.Log.ReplayVerifier.FullHash(game.Game);

        // The relay dies: both friends' connections are reset. They resume by themselves once the relay is back and the host reclaimed its room.
        var room = hosting.Relay.Room;
        await rig.Server.DisposeAsync();
        await WaitUntilAsync(() => alice.State == ClientSessionState.Reconnecting && bob.State == ClientSessionState.Reconnecting);
        await using var second = new RelayServer(used!, rig.Clock, output: null);
        second.Start();
        await WaitUntilAsync(() => alice.State == ClientSessionState.InGame && bob.State == ClientSessionState.InGame);
        Assert.Equal(room, hosting.Relay.Room);
        Assert.Equal(aliceView, alice.Game.ViewHash);
        Assert.Equal(bobView, bob.Game.ViewHash);
        Assert.Equal(hostHash, Unciv.Commands.Log.ReplayVerifier.FullHash(game.Game));

        // ... and the game goes on through the new relay.
        var secondRound = await Task.WhenAll(hosting.HostPlayer!.SubmitAsync(new Unciv.Commands.Turn.EndTurn()).WaitAsync(Bound), alice.SubmitAsync(new Unciv.Commands.Turn.EndTurn()).WaitAsync(Bound), bob.SubmitAsync(new Unciv.Commands.Turn.EndTurn()).WaitAsync(Bound));
        Assert.All(secondRound, o => Assert.True(o.Accepted, o.Code));
        await WaitUntilAsync(() => alice.Game.Snapshot is { Turn: >= 2 } && bob.Game.Snapshot is { Turn: >= 2 });
    }

    [Fact]
    public async Task FailedPasswordBackOff_IsPerJoinerNetworkThroughTheRelay_NotPerKey()
    {
        Unciv.Harness.ScenarioGame.LoadVanilla();
        await using var rig = await RigAsync(RelayMode.Proxy);
        var settings = GameSettings("the-password");
        using var hostId = ClientIdentity.Generate();
        using var cert = HostCertificate.Generate();
        await using var hosting = await HostingSession.StartLobbyAsync(settings, hostId, cert, "Host", 0, EndpointOf(rig), Fast(rig));
        await hosting.Relay!.WaitForRoomAsync(CancellationToken.None).WaitAsync(Bound);
        Assert.True(InviteCode.TryParse(hosting.RelayInvite(), out var invite));

        async Task<JoinResult> TryAsync(string address, string password)
        {
            rig.Proxy!.ClientAddress = address;
            using var identity = ClientIdentity.Generate(); // a fresh key every time: a limiter keyed on the key would never fire
            await using var session = Friend(rig, invite!, identity, "Guesser", settings, password);
            return await session.JoinAsync().WaitAsync(Bound);
        }

        for (var i = 0; i < AttemptLimiter.MaxFailures; i++)
            Assert.Equal(JoinRefusedCodes.BadCredentials, (await TryAsync("198.51.100.30", "wrong-" + i)).Code);
        Assert.Equal(JoinRefusedCodes.RateLimited, (await TryAsync("198.51.100.30", "the-password")).Code); // even the right password: this network is locked out
        Assert.True((await TryAsync("198.51.100.31", "the-password")).Joined); // another joiner is not
    }

    [Fact]
    public async Task AJoinerWhoSetsNoRelayCanStillBeToldWhatIsMissing()
    {
        var parsed = Unciv.GodotClient.Setup.JoinModel.Parse("K7QF-3MXP4A");
        Assert.Null(parsed.Target);
        Assert.Contains("relay", parsed.Problem, StringComparison.OrdinalIgnoreCase);
        var through = Unciv.GodotClient.Setup.JoinModel.Parse("k7qf-3mxp4a", new RelayEndpoint("relay.example.org", 47830));
        Assert.Equal(("relay.example.org", 47830, "K7QF-3MXP4A"), (through.Target!.Host, through.Target.Port, through.Target.Room));
        Assert.True(through.Target.IsRelay);
        Assert.Equal("relay.example.org:47830/K7QF-3MXP4A", through.Target.Address);
        // A plain host name of ten letters is a host name, not a room.
        Assert.False(Unciv.GodotClient.Setup.JoinModel.Parse("mygamehost", new RelayEndpoint("relay.example.org", 47830)).Target!.IsRelay);
        foreach (var code in new[] { RelayCodes.Unreachable, RelayCodes.CertificateRejected, RelayCodes.RoomUnknown, RelayCodes.HostGone, RelayCodes.RoomFull, RelayCodes.RateLimited, RelayCodes.Banned, RelayCodes.RelayFull, RelayCodes.TooOld, RelayCodes.WrongHost, RelayCodes.HostTimeout, RelayCodes.Failed })
            Assert.DoesNotContain("could not reach the host", Unciv.GodotClient.Setup.JoinModel.RefusalText(code), StringComparison.OrdinalIgnoreCase);
        await Task.CompletedTask;
    }


    private sealed class LineSink : TextWriter
    {
        private readonly StringBuilder all = new();
        private readonly Lock gate = new();

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value)
        {
            lock (gate) all.Append(value);
        }

        public string Text
        {
            get { lock (gate) return all.ToString(); }
        }

        public string? LineWith(string fragment) => Text.Split('\n').FirstOrDefault(l => l.Contains(fragment, StringComparison.Ordinal))?.Trim();
    }

    [Fact]
    public async Task TheDedicatedServer_RegistersWithTheRelay_PrintsBothInvites_AndListsItself()
    {
        Unciv.Harness.ScenarioGame.LoadVanilla();
        await using var rig = await RigAsync(RelayMode.SelfSigned);
        var root = Directory.CreateTempSubdirectory("unciv-relay-ded-").FullName;
        using var stop = new CancellationTokenSource();
        var sink = new LineSink();
        try
        {
            var relayUrl = $"unciv-relay://127.0.0.1:{rig.Port}?fp={rig.PinnedFingerprint}";
            var args = new List<string> { "--port", "0", "--data-dir", root, "--relay", relayUrl, "--list", "--list-name", "Dedicated test", "--password", "letmein", "--print-invite", "--no-ai-fill" };
            var server = ServerRunner.RunAsync([.. args], sink, new StringWriter(), stop.Token);
            await WaitUntilAsync(() => sink.LineWith("INVITE-RELAY") != null || server.IsCompleted);
            Assert.False(server.IsCompleted, sink.Text);
            Assert.NotNull(sink.LineWith("RELAY-ROOM"));
            Assert.True(InviteCode.TryParse(sink.LineWith("INVITE-RELAY")!.Split(' ', StringSplitOptions.RemoveEmptyEntries)[^1], out var invite));
            Assert.True(invite!.IsRelay);
            Assert.NotNull(invite.Token); // a password-protected server prints an invite that carries a fresh one-time token

            var listed = await WaitForListingAsync(rig, g => g != null);
            Assert.Equal("Dedicated test", JsonNode.Parse(listed["body"]!.GetValue<string>())!["name"]!.GetValue<string>());

            using var identity = ClientIdentity.Generate();
            await using var friend = new ClientSession(token => TcpTlsClientTransport.ConnectViaRelayAsync(EndpointOf(rig), invite.Room!, invite.Fingerprint, dial: DialOf(rig), cancellationToken: token), new ClientSessionOptions
            {
                Identity = identity,
                DisplayName = "Friend",
                RulesetHash = "",
                ExpectedFingerprint = invite.Fingerprint,
                Password = "wrong-password",
                MaxReconnectAttempts = 0,
            });
            var result = await friend.JoinAsync().WaitAsync(Bound); // the server answers through the relay: whatever it says, the lobby saw the connection
            Assert.False(result.Joined);
            Assert.NotEqual(RelayCodes.Failed, result.Code);
            await stop.CancelAsync();
            await server.WaitAsync(Bound);
            await WaitForListingAsync(rig, g => g == null); // the dedicated server's exit unlists it
        }
        finally
        {
            await stop.CancelAsync();
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
        }
    }

    [Fact]
    public async Task ASessionToldByTheRelayThatTheRoomIsGone_EndsWithThatReason()
    {
        await using var rig = await RigAsync();
        using var identity = ClientIdentity.Generate();
        await using var session = new ClientSession(token => TcpTlsClientTransport.ConnectViaRelayAsync(EndpointOf(rig), "K7QF-3MXP4A", dial: DialOf(rig), cancellationToken: token), new ClientSessionOptions
        {
            Identity = identity,
            DisplayName = "Lost",
            RulesetHash = "",
            MaxReconnectAttempts = 0,
        });
        Assert.Equal(RelayCodes.RoomUnknown, (await session.JoinAsync().WaitAsync(Bound)).Code);
    }

    [Fact]
    public async Task AHostThatMovedToAnotherRoom_KeepsItsPin_AndTheNewAddressIsPinnedToo()
    {
        Unciv.Harness.ScenarioGame.LoadVanilla();
        await using var rig = await RigAsync();
        var settings = GameSettings(null);
        using var hostId = ClientIdentity.Generate();
        using var friendId = ClientIdentity.Generate();
        using var cert = HostCertificate.Generate();
        await using var hosting = await HostingSession.StartLobbyAsync(settings, hostId, cert, "Host", 0, EndpointOf(rig), Fast(rig));
        await hosting.Relay!.WaitForRoomAsync(CancellationToken.None).WaitAsync(Bound);
        Assert.True(InviteCode.TryParse(hosting.RelayInvite(), out var invite));

        var known = new KnownHosts();
        Assert.True(known.Pin("192.0.2.77:47821", HostCertificate.GetFingerprint(cert))); // pinned earlier, when the host was reached directly
        var address = KnownHosts.RelayAddress(EndpointOf(rig).Key, invite!.Room!);
        var persisted = 0;
        await using var friend = new ClientSession(token => TcpTlsClientTransport.ConnectViaRelayAsync(EndpointOf(rig), invite.Room!, null, null, DialOf(rig), cancellationToken: token), new ClientSessionOptions
        {
            Identity = friendId,
            DisplayName = "Friend",
            RulesetHash = settings.ComputeRulesetHash(),
            HostAddress = address,
            KnownHosts = known,
            PersistKnownHosts = _ => persisted++,
            MaxReconnectAttempts = 0,
        });
        Assert.True((await friend.JoinAsync().WaitAsync(Bound)).Joined);
        Assert.Equal(1, persisted);
        Assert.Equal(HostTrust.Trusted, known.Check(address, HostCertificate.GetFingerprint(cert)));
    }

    // Review finding: a typed room code carries no fingerprint, so the player must be asked before the host key is pinned; the transport itself pins nothing.
    [Fact]
    public async Task ATypedRoomCode_AsksTheTrustPromptBeforeAnythingIsPinned()
    {
        Unciv.Harness.ScenarioGame.LoadVanilla();
        await using var rig = await RigAsync();
        var settings = GameSettings(null);
        using var hostId = ClientIdentity.Generate();
        using var friendId = ClientIdentity.Generate();
        using var cert = HostCertificate.Generate();
        await using var hosting = await HostingSession.StartLobbyAsync(settings, hostId, cert, "Host", 0, EndpointOf(rig), Fast(rig));
        await hosting.Relay!.WaitForRoomAsync(CancellationToken.None).WaitAsync(Bound);
        var room = hosting.Relay.Room!;
        var address = KnownHosts.RelayAddress(EndpointOf(rig).Key, room);

        async Task<(bool Joined, string? Code, KnownHosts Known, List<string> Asked)> JoinAsync(bool trust)
        {
            var known = new KnownHosts();
            var asked = new List<string>();
            await using var friend = new ClientSession(token => TcpTlsClientTransport.ConnectViaRelayAsync(EndpointOf(rig), room, null, known, DialOf(rig), cancellationToken: token), new ClientSessionOptions
            {
                Identity = friendId,
                DisplayName = "Friend",
                RulesetHash = settings.ComputeRulesetHash(),
                HostAddress = address,
                KnownHosts = known,
                TrustPrompt = (addr, fp) => { asked.Add(addr + " " + fp); Assert.Empty(known.Pins); return Task.FromResult(trust); },
                MaxReconnectAttempts = 0,
            });
            var result = await friend.JoinAsync().WaitAsync(Bound);
            return (result.Joined, result.Code, known, asked);
        }

        var refused = await JoinAsync(trust: false);
        Assert.False(refused.Joined);
        Assert.Single(refused.Asked);
        Assert.Empty(refused.Known.Pins); // declining pins nothing
        var accepted = await JoinAsync(trust: true);
        Assert.True(accepted.Joined);
        Assert.Single(accepted.Asked);
        Assert.Equal(HostTrust.Trusted, accepted.Known.Check(address, HostCertificate.GetFingerprint(cert)));
    }

    // Review finding: after a refresh tick the publisher must still wake for the next change.
    [Fact]
    public async Task ListingUpdates_StillPublishAfterTheRefreshIntervalHasPassed()
    {
        await using var rig = await RigAsync();
        using var cert = HostCertificate.Generate();
        await using var listener = new RelayListener(EndpointOf(rig), cert, "game-1", Fast(rig) with { ListingRefresh = TimeSpan.FromMilliseconds(150) });
        listener.SetListing(Listing(players: 1));
        listener.Start();
        await listener.WaitForRoomAsync(CancellationToken.None).WaitAsync(Bound);
        await WaitForListingAsync(rig, g => g != null);
        await Task.Delay(500); // several refresh ticks
        listener.SetListing(Listing(players: 4));
        await WaitForListingAsync(rig, g => g != null && JsonNode.Parse(g["body"]!.GetValue<string>())!["players"]!.GetValue<int>() == 4);
    }

    // Review finding: a relay that answers with nonsense must make the host retry, not silently stop.
    [Fact]
    public async Task AMalformedRelayReply_MakesTheHostReconnectInsteadOfDying()
    {
        var fake = new TcpListener(IPAddress.Loopback, 0);
        fake.Start();
        var connections = 0;
        var serve = Task.Run(async () =>
        {
            while (true)
            {
                using var tcp = await fake.AcceptTcpClientAsync();
                Interlocked.Increment(ref connections);
                var stream = tcp.GetStream();
                await stream.WriteAsync(Encoding.UTF8.GetBytes("{\"challenge\":\"AAAA\",\"v\":1}\n"));
                await RelayChannel.ReadLineAsync(stream, CancellationToken.None);
                await stream.WriteAsync(Encoding.UTF8.GetBytes("{\"ok\":true,\"room\":12345}\n")); // a number where the room code belongs
            }
        });
        using var cert = HostCertificate.Generate();
        var logged = new List<string>();
        await using (var listener = new RelayListener(new RelayEndpoint("127.0.0.1", ((IPEndPoint)fake.LocalEndpoint).Port), cert, "game-1",
            new RelayListenerOptions { Dial = new RelayDialOptions { UseTls = false }, MinBackoff = TimeSpan.FromMilliseconds(20), MaxBackoff = TimeSpan.FromMilliseconds(50) }, line => { lock (logged) logged.Add(line); }))
        {
            listener.Start();
            await WaitUntilAsync(() => Volatile.Read(ref connections) >= 3);
            lock (logged) Assert.Contains(logged, l => l.Contains("connection lost", StringComparison.Ordinal));
        }
        fake.Stop();
        try { await serve; }
        catch (Exception e) when (e is SocketException or ObjectDisposedException or IOException or InvalidOperationException) { }
    }
}
