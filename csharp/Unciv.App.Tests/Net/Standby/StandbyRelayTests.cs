using System.Text.Json;
using Unciv.Client.Net.Hosting;
using Unciv.Client.Net.Transport;
using Unciv.Core.Tests.Relay;
using Unciv.Net;
using Unciv.Net.Identity;
using Unciv.Net.Relay;
using Unciv.Net.Standby;
using Unciv.Net.Turns;
using Unciv.Server.Transport;

namespace Unciv.Core.Tests.Net.Standby;

/// <summary>T354 (design/RELAY.md R14): the relay endpoint kind of a successor on the wire, in the coordinator and in the standby probe.</summary>
public sealed class StandbyRelayWireTests
{
    private static readonly string Fp = HostKey.Fingerprint(new byte[] { 9, 9, 9 });
    private static readonly RelayRoute Route = new() { Relay = "relay.example.org:47830", Room = "K7QF-3MXP4A" };

    [Fact]
    public void RelayRoute_RoundTripsOnTheWire_AndADirectOnlySuccessorIsByteIdenticalToBefore()
    {
        var direct = new HostSuccessor { Epoch = 1, Status = "ready", Roster = [], Successor = new SuccessorInfo { KeyId = "k", Endpoints = ["a.example:47900"], Fingerprint = Fp } };
        var encoded = ProtocolCodec.Encode(direct);
        Assert.DoesNotContain("relay", encoded, StringComparison.OrdinalIgnoreCase);
        Assert.Null(((HostSuccessor)ProtocolCodec.Decode(encoded)).Successor!.Relay);

        var viaRelay = direct with { Successor = direct.Successor with { Endpoints = [], Relay = Route } };
        var decoded = (HostSuccessor)ProtocolCodec.Decode(ProtocolCodec.Encode(viaRelay));
        Assert.Equal(Route, decoded.Successor!.Relay);
        Assert.Empty(decoded.Successor.Endpoints);

        var accept = (StandbyAccept)ProtocolCodec.Decode(ProtocolCodec.Encode(new StandbyAccept { Endpoints = [], Port = 0, Fingerprint = Fp, Relay = Route }));
        Assert.Equal(Route, accept.Relay);
        Assert.Null(((StandbyAccept)ProtocolCodec.Decode(ProtocolCodec.Encode(new StandbyAccept { Endpoints = ["a"], Port = 5, Fingerprint = Fp }))).Relay);
    }

    [Theory]
    [InlineData("relay.example.org:47830", "k7qf3mxp4a", "relay.example.org:47830", "K7QF-3MXP4A")]
    [InlineData("Relay.Example.ORG", "K7QF-3MXP4A", "relay.example.org:47830", "K7QF-3MXP4A")]
    [InlineData("[::1]:9000", "K7QF3MXP4A", "[::1]:9000", "K7QF-3MXP4A")]
    public void RelayRoutes_NormaliseToTheCanonicalForm(string relay, string room, string expectedRelay, string expectedRoom) =>
        Assert.Equal(new RelayRoute { Relay = expectedRelay, Room = expectedRoom }, RelayRoutes.Normalize(new RelayRoute { Relay = relay, Room = room }));

    [Theory]
    [InlineData("", "K7QF-3MXP4A")]
    [InlineData("unciv-relay://relay.example.org", "K7QF-3MXP4A")]
    [InlineData("relay.example.org/path", "K7QF-3MXP4A")]
    [InlineData("relay.example.org?fp=AAAAA", "K7QF-3MXP4A")]
    [InlineData("relay.example.org:99999", "K7QF-3MXP4A")]
    [InlineData("two words", "K7QF-3MXP4A")]
    [InlineData("relay.example.org", "short")]
    [InlineData("relay.example.org", "K7QF-3MXP4U")]
    [InlineData("relay.example.org", "")]
    public void RelayRoutes_RefuseWhatIsNotARoute(string relay, string room) => Assert.Null(RelayRoutes.Normalize(new RelayRoute { Relay = relay, Room = room }));

    [Fact]
    public void RelayRoutes_Same_IgnoresSpellingOnly()
    {
        Assert.True(RelayRoutes.Same(Route, new RelayRoute { Relay = "RELAY.example.org", Room = "k7qf3mxp4a" }));
        Assert.False(RelayRoutes.Same(Route, Route with { Room = "K7QF-3MXP4B" }));
        Assert.False(RelayRoutes.Same(Route, Route with { Relay = "other.example.org:47830" }));
        Assert.False(RelayRoutes.Same(Route, null));
        Assert.True(RelayRoutes.Same(null, null));
        Assert.Equal(Route, RelayRoutes.Of(new RelayEndpoint("relay.example.org", 47830, Fp), "k7qf3mxp4a")); // a certificate pin is never part of a route
    }

    private static (StandbyCoordinator Coordinator, FakeProber Prober, ManualClock Clock) Make(string? directAnswer = null, string? relayAnswer = null)
    {
        var clock = new ManualClock(1_000_000);
        var prober = new FakeProber { Answer = (_, _) => directAnswer, RelayAnswer = _ => relayAnswer };
        var coordinator = new StandbyCoordinator(clock, prober);
        coordinator.Designate("candidate", out _);
        return (coordinator, prober, clock);
    }

    private static StandbyAccept RelayOnly(string fingerprint, RelayRoute? route = null) => new() { Endpoints = [], Port = 0, Fingerprint = fingerprint, Relay = route ?? Route };

    [Fact]
    public async Task Accept_OfARelayRoomOnly_IsProbedThroughTheRelay_AndAnnouncedWithTheRoute()
    {
        var (c, prober, _) = Make(relayAnswer: Fp);
        Assert.Null(await c.AcceptAsync("candidate", RelayOnly(Fp)));
        Assert.Equal(StandbyStates.Ready, c.State);
        Assert.Equal(["relay:relay.example.org:47830/K7QF-3MXP4A"], prober.Dialled);
        var info = c.Announcement(2, []).Successor!;
        Assert.Empty(info.Endpoints);
        Assert.Equal(Route, info.Relay);
        Assert.Equal(Fp, info.Fingerprint);
    }

    [Fact]
    public async Task Accept_ThroughTheRelay_RefusesAWrongFingerprint_AnUnreachableRoom_AndMalformedRoutes()
    {
        var (wrong, _, _) = Make(relayAnswer: HostKey.Fingerprint(new byte[] { 1 }));
        Assert.Equal(ErrorCodes.ProbeFingerprint, await wrong.AcceptAsync("candidate", RelayOnly(Fp)));
        Assert.Null(wrong.Announcement(0, []).Successor);

        var (dead, _, _) = Make(relayAnswer: null);
        Assert.Equal(ErrorCodes.ProbeFailed, await dead.AcceptAsync("candidate", RelayOnly(Fp)));
        Assert.Equal(StandbyStates.Unreachable, dead.State);

        var (bad, prober, _) = Make(relayAnswer: Fp);
        foreach (var route in new[] { new RelayRoute { Relay = "unciv-relay://evil.example.org", Room = "K7QF-3MXP4A" }, new RelayRoute { Relay = "relay.example.org", Room = "nope" } })
            Assert.Equal(ErrorCodes.InvalidEndpoint, await bad.AcceptAsync("candidate", RelayOnly(Fp, route)));
        Assert.Equal(ErrorCodes.InvalidEndpoint, await bad.AcceptAsync("candidate", new StandbyAccept { Endpoints = [], Port = 0, Fingerprint = Fp })); // neither an address nor a room
        Assert.Empty(prober.Dialled);
        Assert.Equal(ErrorCodes.NotDesignated, await bad.AcceptAsync("stranger", RelayOnly(Fp))); // only the designated key can make the host dial a relay
    }

    [Fact]
    public async Task Accept_WithAddressesAndARoom_ProbesThemInTurn_AndAReadyStandbyWithOneDeadTargetStaysReady()
    {
        var (c, prober, clock) = Make();
        prober.Answer = (_, _) => null;
        prober.RelayAnswer = _ => Fp;
        var accept = new StandbyAccept { Endpoints = ["friend.example"], Port = 47900, Fingerprint = Fp, Relay = Route };
        Assert.Equal(ErrorCodes.ProbeFailed, await c.AcceptAsync("candidate", accept)); // the first target is the address
        clock.Advance(61_000);
        Assert.Null(await c.ReprobeAsync()); // then the room
        Assert.Equal(["friend.example:47900", "relay:relay.example.org:47830/K7QF-3MXP4A"], prober.Dialled);
        Assert.Equal(StandbyStates.Ready, c.State);
        var info = c.Announcement(0, []).Successor!;
        Assert.Equal(["friend.example:47900"], info.Endpoints);
        Assert.Equal(Route, info.Relay);

        // The periodic re-probe tries the other target before it gives up on a standby that was ready.
        prober.Dialled.Clear();
        clock.Advance(200_000);
        Assert.False(await c.ReprobeIfDueAsync());
        Assert.Equal(StandbyStates.Ready, c.State);
        Assert.Equal(2, prober.Dialled.Count);
    }

    [Fact]
    public async Task ARepeatedAccept_OfTheSameRoom_ChangesNothing_AndANewRoomStartsOver()
    {
        var (c, prober, clock) = Make(relayAnswer: Fp);
        Assert.Null(await c.AcceptAsync("candidate", RelayOnly(Fp)));
        Assert.Null(await c.AcceptAsync("candidate", RelayOnly(Fp, new RelayRoute { Relay = "RELAY.example.org:47830", Room = "k7qf3mxp4a" })));
        Assert.Single(prober.Dialled);
        clock.Advance(61_000);
        Assert.Null(await c.AcceptAsync("candidate", RelayOnly(Fp, Route with { Room = "K7QF-3MXP4B" })));
        Assert.Equal(2, prober.Dialled.Count);
        Assert.Equal("K7QF-3MXP4B", c.Announcement(0, []).Successor!.Relay!.Room);
    }

    [Fact]
    public async Task TheDesignation_KeepsItsRoute_AcrossARestart_AndAHandEditedRouteIsNotTrusted()
    {
        var (c, _, _) = Make(relayAnswer: Fp);
        await c.AcceptAsync("candidate", RelayOnly(Fp));
        var record = c.ToRecord()!;
        Assert.Equal(Route, record.Relay);
        var json = JsonSerializer.Serialize(record);
        var restored = new StandbyCoordinator(new ManualClock(5), new FakeProber());
        restored.Restore(JsonSerializer.Deserialize<StandbyRecord>(json));
        Assert.Equal(StandbyStates.Unreachable, restored.State); // accepted, but never ready before it is probed again
        Assert.Equal(Route, restored.ToRecord()!.Relay);

        var edited = new StandbyCoordinator(new ManualClock(5), new FakeProber());
        edited.Restore(record with { Relay = new RelayRoute { Relay = "https://evil.example/x", Room = "K7QF-3MXP4A" } });
        Assert.Equal(StandbyStates.Offered, edited.State);
        Assert.Null(edited.ToRecord()!.Relay);
    }
}

/// <summary>T354 (R14): the standby probe through a local relay, the standby room, and the rule that a host dials only a relay it agreed to.</summary>
[Collection("RulesetCache")]
public sealed class StandbyRelayProbeTests : IAsyncDisposable
{
    private readonly List<IAsyncDisposable> toDispose = [];
    private readonly List<System.Security.Cryptography.X509Certificates.X509Certificate2> certificates = [];

    public async ValueTask DisposeAsync()
    {
        foreach (var item in Enumerable.Reverse(toDispose)) await item.DisposeAsync();
        foreach (var cert in certificates) cert.Dispose();
    }

    private async Task<(RelayRig Rig, RelayEndpoint Endpoint, RelayListenerOptions Options)> RigAsync()
    {
        var rig = await RelayRig.StartAsync(RelayMode.SelfSigned, o => o with { PublicNames = [RelayRig.PublicName, "127.0.0.1"] });
        toDispose.Add(rig);
        return (rig, new RelayEndpoint("127.0.0.1", rig.Port, rig.PinnedFingerprint), new RelayListenerOptions { MinBackoff = TimeSpan.FromMilliseconds(50), MaxBackoff = TimeSpan.FromMilliseconds(200) });
    }

    private async Task<(StandbyListener Listener, string Fingerprint, RelayRoute Route)> StandbyAsync(RelayEndpoint endpoint, RelayListenerOptions options, string gameId = "game-1")
    {
        var cert = HostCertificate.Generate();
        certificates.Add(cert);
        var relay = new RelayListener(endpoint, cert, gameId, options);
        var listener = new StandbyListener(cert, null, relay: relay);
        toDispose.Add(listener);
        listener.Start();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await relay.WaitForRoomAsync(timeout.Token);
        return (listener, HostCertificate.GetFingerprint(cert), RelayRoutes.Of(endpoint, relay.Room!));
    }

    private static Func<RelayEndpoint, RelayDialPlan?> Agrees(RelayEndpoint endpoint) =>
        named => string.Equals(named.Key, endpoint.Key, StringComparison.OrdinalIgnoreCase) ? new RelayDialPlan(endpoint, new RelayDialOptions()) : null;

    [Fact]
    public async Task TheProbe_ReachesAStandbyRoomThroughTheRelay_AndReportsItsFingerprint()
    {
        var (_, endpoint, options) = await RigAsync();
        var (_, fingerprint, route) = await StandbyAsync(endpoint, options);
        var presented = await new TlsEndpointProber(Agrees(endpoint)).PresentedFingerprintViaRelayAsync(route, fingerprint, TimeSpan.FromSeconds(10), CancellationToken.None);
        Assert.True(HostKey.Matches(presented, fingerprint));
        // Twice in a row: the room keeps serving.
        Assert.True(HostKey.Matches(await new TlsEndpointProber(Agrees(endpoint)).PresentedFingerprintViaRelayAsync(route, fingerprint, TimeSpan.FromSeconds(10), CancellationToken.None), fingerprint));
    }

    [Fact]
    public async Task TheProbe_NeverDialsARelayTheHostDidNotAgreeTo()
    {
        var (_, endpoint, options) = await RigAsync();
        var (_, fingerprint, route) = await StandbyAsync(endpoint, options);
        var asked = new List<string>();
        var refusing = new TlsEndpointProber(named => { asked.Add(named.Key); return null; });
        Assert.Null(await refusing.PresentedFingerprintViaRelayAsync(route, fingerprint, TimeSpan.FromSeconds(10), CancellationToken.None));
        Assert.Equal([endpoint.Key], asked);
        Assert.Null(await new TlsEndpointProber().PresentedFingerprintViaRelayAsync(route, fingerprint, TimeSpan.FromSeconds(10), CancellationToken.None)); // no resolver, no relay
        Assert.Null(await new TlsEndpointProber(Agrees(endpoint)).PresentedFingerprintViaRelayAsync(route with { Relay = "other.example.org:1" }, fingerprint, TimeSpan.FromSeconds(10), CancellationToken.None));
        Assert.Null(await new TlsEndpointProber(Agrees(endpoint)).PresentedFingerprintViaRelayAsync(route with { Relay = "unciv-relay://127.0.0.1" }, fingerprint, TimeSpan.FromSeconds(10), CancellationToken.None));
    }

    [Fact]
    public async Task TheProbe_FindsNothingUsable_ForAnUnknownRoom_AWrongKey_AndAnUnreachableRelay()
    {
        var (rig, endpoint, options) = await RigAsync();
        var (_, fingerprint, route) = await StandbyAsync(endpoint, options);
        var prober = new TlsEndpointProber(Agrees(endpoint));
        Assert.Null(await prober.PresentedFingerprintViaRelayAsync(route with { Room = "K7QF-3MXP4A" == route.Room ? "K7QF-3MXP4B" : "K7QF-3MXP4A" }, fingerprint, TimeSpan.FromSeconds(10), CancellationToken.None));
        Assert.Null(await prober.PresentedFingerprintViaRelayAsync(route, HostKey.Fingerprint(new byte[] { 4, 4 }), TimeSpan.FromSeconds(10), CancellationToken.None)); // the relay's own wrong-host early refusal
        var dead = new RelayEndpoint("127.0.0.1", 1, rig.PinnedFingerprint);
        Assert.Null(await new TlsEndpointProber(_ => new RelayDialPlan(dead, new RelayDialOptions())).PresentedFingerprintViaRelayAsync(route with { Relay = dead.Key }, fingerprint, TimeSpan.FromSeconds(3), CancellationToken.None));
    }

    [Fact]
    public async Task ARelayRoomIsNotAGame_AnythingButHelloIsNotHostYet_AndTheRoomIsUnlisted()
    {
        var (rig, endpoint, options) = await RigAsync();
        var (_, fingerprint, route) = await StandbyAsync(endpoint, options);
        await using var transport = await TcpTlsClientTransport.ConnectViaRelayAsync(endpoint, route.Room, fingerprint, dial: new RelayDialOptions());
        await transport.SendAsync(ProtocolCodec.Encode(new Ping()));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Assert.Equal(ErrorCodes.NotHostYet, Assert.IsType<Error>(ProtocolCodec.Decode((await transport.ReceiveAsync(timeout.Token))!)).Code);
        var directory = await rig.GetOverTunnelAsync("/v1/games");
        Assert.Empty(directory["data"]!["games"]!.AsArray()); // the standby room is never in the lobby directory
    }

    [Fact]
    public async Task AnImpostorBehindTheRoom_IsRefusedByThePinnedFingerprint_BeforeAnyGameByte()
    {
        var (_, endpoint, options) = await RigAsync();
        var (_, _, route) = await StandbyAsync(endpoint, options);
        var settings = new RelaySettings { Url = endpoint.ToString() };
        var announced = HostKey.Fingerprint(new byte[] { 5, 5, 5 }); // not the key the room's host holds
        var failure = await Assert.ThrowsAsync<RelayException>(() => settings.ConnectAsync(route, announced, CancellationToken.None));
        // The relay's early wrong-host refusal fires first against an honest relay; a dishonest one that splices anyway is stopped by the inner pin ("host-key-mismatch", proved with a MITM relay in RelayClientTests).
        Assert.Contains(failure.Code, new[] { "host-key-mismatch", RelayCodes.WrongHost });
    }

    [Fact]
    public async Task DetachRelay_HandsOverTheRoomStillRegistered_AndTheStandbyStopsAnswering()
    {
        var (_, endpoint, options) = await RigAsync();
        var (listener, fingerprint, route) = await StandbyAsync(endpoint, options);
        var relay = listener.DetachRelay()!;
        Assert.Null(listener.Relay);
        Assert.Null(listener.DetachRelay());
        Assert.Equal(route.Room, RelayProtocol.FormatRoom(RelayProtocol.NormalizeRoom(relay.Room)!));
        Assert.True(relay.Connected);

        // The adopter, not the standby, now receives the joiners of the same room.
        var join = TcpTlsClientTransport.ConnectViaRelayAsync(endpoint, route.Room, fingerprint, dial: new RelayDialOptions());
        await using var accepted = await relay.AcceptTransportAsync().WaitAsync(TimeSpan.FromSeconds(30));
        await using var joined = await join.WaitAsync(TimeSpan.FromSeconds(30));
        await accepted.SendAsync("hello");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Assert.Equal("hello", await joined.ReceiveAsync(timeout.Token));
        await relay.DisposeAsync();
    }

    [Fact]
    public async Task TheStandbyRoom_ClosesAConnectionThatSaysNothing_AndRefusesConnectionsBeyondItsLimit()
    {
        var (_, endpoint, options) = await RigAsync();
        var cert = HostCertificate.Generate();
        certificates.Add(cert);
        var relay = new RelayListener(endpoint, cert, "game-quiet", options with { MaxPendingPerPeer = 100, MaxPendingJoins = 100 });
        var listener = new StandbyListener(cert, null, relay: relay) { ConnectionLifetime = TimeSpan.FromMilliseconds(600), ConnectionLimit = 2 };
        toDispose.Add(listener);
        listener.Start();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await relay.WaitForRoomAsync(timeout.Token);
        var fingerprint = HostCertificate.GetFingerprint(cert);

        var quiet = new List<ITransport>();
        for (var i = 0; i < 5; i++)
        {
            try { quiet.Add(await TcpTlsClientTransport.ConnectViaRelayAsync(endpoint, relay.Room!, fingerprint, dial: new RelayDialOptions(), timeout: TimeSpan.FromSeconds(5))); }
            catch (RelayException) { /* the relay's own per-room limit may refuse the last ones first */ }
        }
        Assert.NotEmpty(quiet);
        foreach (var transport in quiet)
        {
            using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            Assert.Null(await transport.ReceiveAsync(wait.Token)); // closed by the lifetime limit or by the connection cap, never answered
            await transport.DisposeAsync();
        }
        var presented = await new TlsEndpointProber(Agrees(endpoint)).PresentedFingerprintViaRelayAsync(RelayRoutes.Of(endpoint, relay.Room!), fingerprint, TimeSpan.FromSeconds(10), CancellationToken.None);
        Assert.True(HostKey.Matches(presented, fingerprint)); // still serving afterwards
    }

    [Fact]
    public async Task TheNullProber_KnowsNoEndpointAndNoRelay()
    {
        IEndpointProber prober = new NullEndpointProber();
        Assert.Null(await prober.PresentedFingerprintAsync("friend.example.org", 47900, TimeSpan.FromSeconds(1), CancellationToken.None));
        Assert.Null(await prober.PresentedFingerprintViaRelayAsync(new RelayRoute { Relay = "relay.example.org:47830", Room = "K7QF-3MXP4A" }, "", TimeSpan.FromSeconds(1), CancellationToken.None));
    }

    [Fact]
    public void ARelayOnlyListener_NeedsARoom() => Assert.Throws<ArgumentException>(() => new StandbyListener(HostCertificate.Generate(), null));

    [Fact]
    public void RelaySettings_ResolvesOnlyRelaysThePlayerAgreedTo()
    {
        var configured = new RelayEndpoint("relay.example.org", 47830, HostKey.Fingerprint(new byte[] { 1 }));
        var settings = new RelaySettings { Url = configured.ToString() };
        var plan = settings.Agreed(new RelayEndpoint("RELAY.example.org", 47830))!;
        Assert.True(HostKey.Matches(configured.Fingerprint, plan.Endpoint.Fingerprint)); // the configured relay keeps its certificate pin
        Assert.Null(settings.Agreed(new RelayEndpoint("friend-relay.example.org", 47830)));
        settings.Remember(new RelayEndpoint("friend-relay.example.org", 47830));
        Assert.True(settings.Agreed(new RelayEndpoint("friend-relay.example.org", 47830))!.Dial.TrustAnyCertificate);
        Assert.Null(new RelaySettings().Agreed(configured)); // no relay configured, none agreed
    }

    [Fact]
    public async Task RelaySettings_ConfirmAsync_AsksOnceForAnUnknownRelay_AndRemembersAYes()
    {
        var route = new RelayRoute { Relay = "friend-relay.example.org:47830", Room = "K7QF-3MXP4A" };
        var settings = new RelaySettings();
        var asked = 0;
        var saved = 0;
        Assert.False(await settings.ConfirmAsync(route, _ => { asked++; return Task.FromResult(false); }, _ => saved++));
        Assert.Empty(settings.AcceptedRelays);
        Assert.True(await settings.ConfirmAsync(route, _ => { asked++; return Task.FromResult(true); }, _ => saved++));
        Assert.True(await settings.ConfirmAsync(route, _ => { asked++; return Task.FromResult(false); }, _ => saved++)); // remembered: not asked again
        Assert.Equal((2, 1), (asked, saved));
        Assert.Equal(["friend-relay.example.org:47830"], settings.AcceptedRelays);
        Assert.Null(settings.Url); // never added to the player's own relay
        Assert.False(await settings.ConfirmAsync(new RelayRoute { Relay = "not a relay", Room = "K7QF-3MXP4A" }, _ => Task.FromResult(true)));
    }
}
