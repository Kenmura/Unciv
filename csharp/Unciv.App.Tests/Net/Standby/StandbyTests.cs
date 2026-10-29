using System.Net;
using System.Net.Sockets;
using Unciv.Client.Net.Hosting;
using Unciv.Client.Net.Session;
using Unciv.Core.Tests.Net.ClientNet;
using Unciv.Net;
using Unciv.Net.Identity;
using Unciv.Net.Standby;
using Unciv.Net.Turns;
using Unciv.Server.Transport;

namespace Unciv.Core.Tests.Net.Standby;

/// <summary>A scripted dialler: what each endpoint "presents", and how often it was asked.</summary>
internal sealed class FakeProber : IEndpointProber
{
    public Func<string, int, string?> Answer { get; set; } = (_, _) => null;
    public List<string> Dialled { get; } = [];

    public Task<string?> PresentedFingerprintAsync(string host, int port, TimeSpan timeout, CancellationToken cancellationToken)
    {
        lock (Dialled) Dialled.Add($"{host}:{port}");
        return Task.FromResult(Answer(host, port));
    }
}

/// <summary>T281 acceptance A2 at the coordinator level: consent, designated-only, one generic code, and the endpoint probe's limits.</summary>
public sealed class StandbyCoordinatorTests
{
    private static readonly string Fp = HostKey.Fingerprint(new byte[] { 9, 9, 9 });
    private static readonly string OtherFp = HostKey.Fingerprint(new byte[] { 8, 8, 8 });

    private static StandbyAccept Accept(string fingerprint, int port = 47821, params string[] endpoints) =>
        new() { Endpoints = endpoints.Length == 0 ? ["friend.example"] : [.. endpoints], Port = port, Fingerprint = fingerprint };

    private static (StandbyCoordinator Coordinator, FakeProber Prober, ManualClock Clock) Make()
    {
        var clock = new ManualClock(1_000_000);
        var prober = new FakeProber { Answer = (_, _) => Fp };
        return (new StandbyCoordinator(clock, prober), prober, clock);
    }

    [Fact]
    public async Task Accept_FromAnyoneButTheDesignatedKey_GetsTheOneGenericCodeAndChangesNothing()
    {
        var (c, prober, _) = Make();
        Assert.Equal(ErrorCodes.NotDesignated, await c.AcceptAsync("stranger", Accept(Fp)));
        c.Designate("candidate", out _);
        Assert.Equal(ErrorCodes.NotDesignated, await c.AcceptAsync("stranger", Accept(Fp)));
        Assert.Equal(ErrorCodes.NotDesignated, await c.AcceptAsync(null, Accept(Fp)));
        Assert.Equal(ErrorCodes.NotDesignated, c.Decline("stranger"));
        Assert.Equal(StandbyStates.Offered, c.State);
        Assert.Empty(prober.Dialled); // nobody but the designated key can make the host dial anything
        Assert.Null(c.Announcement(0, []).Successor);
    }

    [Fact]
    public async Task Accept_WithAMatchingListener_MakesTheStandbyReadyAndAnnouncesIt()
    {
        var (c, prober, _) = Make();
        c.Designate("candidate", out _);
        Assert.Null(await c.AcceptAsync("candidate", Accept(Fp, 47821, "friend.example", "10.0.0.2")));
        Assert.Equal(StandbyStates.Ready, c.State);
        Assert.Equal(["friend.example:47821"], prober.Dialled); // one endpoint per probe
        var announcement = c.Announcement(3, [new SuccessorSeat { KeyId = "candidate", Connected = true }]);
        Assert.Equal(3, announcement.Epoch);
        Assert.Equal("ready", announcement.Status);
        Assert.Equal(["friend.example:47821", "10.0.0.2:47821"], announcement.Successor!.Endpoints);
        Assert.Equal("candidate", announcement.Successor.KeyId);
        Assert.Equal(Fp, announcement.Successor.Fingerprint);
    }

    [Fact]
    public async Task Probe_RejectsAWrongFingerprintAndAClosedPort()
    {
        var (c, prober, clock) = Make();
        c.Designate("candidate", out _);
        prober.Answer = (_, _) => OtherFp;
        Assert.Equal(ErrorCodes.ProbeFingerprint, await c.AcceptAsync("candidate", Accept(Fp)));
        Assert.Equal(StandbyStates.Unreachable, c.State);
        Assert.Null(c.Announcement(0, []).Successor);

        clock.AdvanceSeconds(61);
        prober.Answer = (_, _) => null; // closed port, timeout, no TLS
        Assert.Equal(ErrorCodes.ProbeFailed, await c.AcceptAsync("candidate", Accept(Fp)));
        Assert.Equal(StandbyStates.Unreachable, c.State);

        clock.AdvanceSeconds(61);
        prober.Answer = (_, _) => throw new SocketException();
        Assert.Equal(ErrorCodes.ProbeFailed, await c.AcceptAsync("candidate", Accept(Fp)));

        clock.AdvanceSeconds(61);
        prober.Answer = (_, _) => Fp;
        Assert.Null(await c.AcceptAsync("candidate", Accept(Fp)));
        Assert.Equal(StandbyStates.Ready, c.State);
    }

    [Fact]
    public async Task Probe_IsRateLimitedToOnePerMinuteAndTriesTheNextEndpointEachTime()
    {
        var (c, prober, clock) = Make();
        c.Designate("candidate", out _);
        prober.Answer = (_, _) => null;
        Assert.Equal(ErrorCodes.ProbeFailed, await c.AcceptAsync("candidate", Accept(Fp, 47821, "a.example", "b.example")));
        clock.AdvanceSeconds(59);
        Assert.Equal(ErrorCodes.ProbeRateLimited, await c.ReprobeAsync());
        Assert.Equal(ErrorCodes.ProbeRateLimited, await c.AcceptAsync("candidate", Accept(Fp, 47821, "a.example", "b.example")));
        Assert.Single(prober.Dialled);
        Assert.Equal(StandbyStates.Unreachable, c.State); // the consent stays recorded

        clock.AdvanceSeconds(2);
        prober.Answer = (_, _) => Fp;
        Assert.Null(await c.ReprobeAsync());
        Assert.Equal(["a.example:47821", "b.example:47821"], prober.Dialled); // the repeated identical accept did not restart the rotation
    }

    [Fact]
    public async Task RepeatedAcceptAndRateLimitedReprobe_NeverUndoAReadyStandby()
    {
        var (c, prober, clock) = Make();
        c.Designate("candidate", out _);
        Assert.Null(await c.AcceptAsync("candidate", Accept(Fp)));
        Assert.Null(await c.AcceptAsync("candidate", Accept(Fp))); // a client retry
        Assert.Equal(ErrorCodes.ProbeRateLimited, await c.ReprobeAsync());
        Assert.Equal(StandbyStates.Ready, c.State);
        Assert.Single(prober.Dialled);
        clock.AdvanceSeconds(61);
        prober.Answer = (_, _) => null;
        Assert.Equal(ErrorCodes.ProbeFailed, await c.ReprobeAsync());
        Assert.Equal(StandbyStates.Unreachable, c.State);
    }

    [Fact]
    public void Restore_DropsAnAcceptedRecordWithAnUndialableEndpoint()
    {
        var c = new StandbyCoordinator(new ManualClock(0), new FakeProber());
        c.Restore(new StandbyRecord { KeyId = "k", Accepted = true, Endpoints = ["nocolon"], Fingerprint = Fp });
        Assert.Equal(StandbyStates.Offered, c.State);
        c.Restore(new StandbyRecord { KeyId = "k", Accepted = true, Endpoints = ["a.example:80"], Fingerprint = Fp });
        Assert.Equal(StandbyStates.Offered, c.State);
    }

    [Fact]
    public async Task Probe_RotatesThroughTheAnnouncedEndpointsOnRetries()
    {
        var (c, prober, clock) = Make();
        c.Designate("candidate", out _);
        prober.Answer = (_, _) => null;
        await c.AcceptAsync("candidate", Accept(Fp, 47821, "a.example", "b.example"));
        clock.AdvanceSeconds(61);
        await c.ReprobeAsync();
        Assert.Equal(["a.example:47821", "b.example:47821"], prober.Dialled);
    }

    [Theory]
    [InlineData(80)]
    [InlineData(1023)]
    [InlineData(65536)]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Accept_RefusesPortsBelow1024AndAbove65535_WithoutDialling(int port)
    {
        var (c, prober, _) = Make();
        c.Designate("candidate", out _);
        Assert.Equal(ErrorCodes.InvalidEndpoint, await c.AcceptAsync("candidate", Accept(Fp, port)));
        Assert.Empty(prober.Dialled);
        Assert.Equal(StandbyStates.Offered, c.State);
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad host")]
    [InlineData("http://x.example")]
    [InlineData("x.example/path")]
    public async Task Accept_RefusesAnythingButPlainHostNames(string endpoint)
    {
        var (c, prober, _) = Make();
        c.Designate("candidate", out _);
        Assert.Equal(ErrorCodes.InvalidEndpoint, await c.AcceptAsync("candidate", Accept(Fp, 47821, endpoint)));
        Assert.Empty(prober.Dialled);
    }

    [Fact]
    public async Task Accept_RefusesTooManyEndpointsEmptyListAndBadFingerprint()
    {
        var (c, prober, _) = Make();
        c.Designate("candidate", out _);
        Assert.Equal(ErrorCodes.InvalidEndpoint, await c.AcceptAsync("candidate", new StandbyAccept { Endpoints = [], Port = 47821, Fingerprint = Fp }));
        Assert.Equal(ErrorCodes.InvalidEndpoint, await c.AcceptAsync("candidate", Accept(Fp, 47821, "a", "b", "c", "d", "e")));
        Assert.Equal(ErrorCodes.InvalidEndpoint, await c.AcceptAsync("candidate", Accept("not-a-fingerprint")));
        Assert.Equal(ErrorCodes.InvalidEndpoint, await c.AcceptAsync("candidate", Accept(Fp, 47821, new string('a', 300))));
        Assert.Empty(prober.Dialled);
    }

    [Fact]
    public async Task DeclineRevokeAndRedesignation_EndTheOldDesignation()
    {
        var (c, _, clock) = Make();
        c.Designate("candidate", out _);
        await c.AcceptAsync("candidate", Accept(Fp));
        Assert.Null(c.Decline("candidate"));
        Assert.Equal(StandbyStates.None, c.State);
        Assert.Null(c.ToRecord());

        c.Designate("one", out _);
        Assert.True(c.Designate("two", out var previous));
        Assert.Equal("one", previous);
        Assert.False(c.Designate("two", out _));
        clock.AdvanceSeconds(61);
        Assert.Equal(ErrorCodes.NotDesignated, await c.AcceptAsync("one", Accept(Fp)));
        Assert.Equal("two", c.Revoke());
        Assert.Null(c.Revoke());
    }

    [Fact]
    public async Task ARevokeDuringTheProbe_LeavesNothingDesignated()
    {
        var clock = new ManualClock(0);
        var release = new TaskCompletionSource<string?>();
        var c = new StandbyCoordinator(clock, new GatedProber(release.Task));
        c.Designate("candidate", out _);
        var pending = c.AcceptAsync("candidate", Accept(Fp));
        c.Revoke();
        release.SetResult(Fp);
        Assert.Equal(ErrorCodes.NotDesignated, await pending);
        Assert.Equal(StandbyStates.None, c.State);
    }

    [Fact]
    public async Task Restore_NeverComesBackReady_AndRoundTripsTheRecord()
    {
        var (c, _, _) = Make();
        c.Designate("candidate", out _);
        await c.AcceptAsync("candidate", Accept(Fp));
        var record = c.ToRecord();
        Assert.NotNull(record);

        var restored = new StandbyCoordinator(new ManualClock(5), new FakeProber());
        restored.Restore(record);
        Assert.Equal(StandbyStates.Unreachable, restored.State);
        Assert.Equal("candidate", restored.KeyId);

        restored.Restore(new StandbyRecord { KeyId = "candidate", Accepted = false });
        Assert.Equal(StandbyStates.Offered, restored.State);
        restored.Restore(new StandbyRecord { KeyId = "candidate", Accepted = true, Endpoints = [], Fingerprint = "garbage" });
        Assert.Equal(StandbyStates.Offered, restored.State); // an accepted record without usable endpoints is just an offer
        restored.Restore(null);
        Assert.Equal(StandbyStates.None, restored.State);
    }

    [Fact]
    public void AnnouncementForJoin_IsAbsentWhileNobodyIsDesignated()
    {
        var (c, _, _) = Make();
        Assert.Null(c.AnnouncementForJoin(0, []));
        c.Designate("candidate", out _);
        Assert.Equal("offered", c.AnnouncementForJoin(0, [])!.Status);
    }

    private sealed class GatedProber(Task<string?> gate) : IEndpointProber
    {
        public Task<string?> PresentedFingerprintAsync(string host, int port, TimeSpan timeout, CancellationToken cancellationToken) => gate;
    }
}

/// <summary>The real dialler: one TLS handshake, nothing else.</summary>
public sealed class TlsEndpointProberTests
{
    [Fact]
    public async Task ReportsTheFingerprintOfWhatListens_AndNullForAClosedPortOrAPlainSocket()
    {
        using var cert = HostCertificate.Generate();
        await using var listener = new TcpTlsListener(cert, 0);
        listener.Start();
        using var stop = new CancellationTokenSource();
        var accepted = Task.Run(async () =>
        {
            try { while (true) await (await listener.AcceptTransportAsync(stop.Token)).DisposeAsync(); }
            catch (Exception) { }
        });

        var prober = new TlsEndpointProber();
        var presented = await prober.PresentedFingerprintAsync("127.0.0.1", listener.Port, TimeSpan.FromSeconds(3), CancellationToken.None);
        Assert.True(HostKey.Matches(presented, HostCertificate.GetFingerprint(cert)));
        stop.Cancel();
        listener.Stop();

        var free = new TcpListener(IPAddress.Loopback, 0);
        free.Start();
        var closedPort = ((IPEndPoint)free.LocalEndpoint).Port;
        free.Stop();
        Assert.Null(await prober.PresentedFingerprintAsync("127.0.0.1", closedPort, TimeSpan.FromSeconds(3), CancellationToken.None));

        // A listener that accepts the connection and never speaks TLS runs into the timeout.
        var silent = new TcpListener(IPAddress.Loopback, 0);
        silent.Start();
        try
        {
            var acceptSilent = silent.AcceptTcpClientAsync();
            Assert.Null(await prober.PresentedFingerprintAsync("127.0.0.1", ((IPEndPoint)silent.LocalEndpoint).Port, TimeSpan.FromMilliseconds(300), CancellationToken.None));
            (await acceptSilent).Dispose();
        }
        finally { silent.Stop(); }
        await accepted;
    }
}

/// <summary>The client's half: the offer is for its own key only, the announcement is vouched into known-hosts, consent goes out as a frame.</summary>
public sealed class StandbyClientTests
{
    private static readonly string HostFp = ClientNetRig.Fingerprint;
    private static readonly string SuccessorFp = HostKey.Fingerprint(new byte[] { 7, 7, 7 });

    private static HostSuccessor Ready(int epoch, string keyId = "k") => new()
    {
        Epoch = epoch,
        Status = StandbyStates.Ready,
        Successor = new SuccessorInfo { KeyId = keyId, Endpoints = ["friend.example:47821", "10.0.0.2:47821"], Fingerprint = SuccessorFp },
        Roster = [new SuccessorSeat { KeyId = keyId, Connected = true }],
    };

    private static ClientNetRig Rig(KnownHosts known, List<KnownHosts> persisted, int knownEpoch = 0) =>
        new(identity => new ClientSessionOptions
        {
            Identity = identity,
            DisplayName = "Alice",
            RulesetHash = "hash",
            ExpectedFingerprint = ClientNetRig.Fingerprint,
            ReconnectDelay = (_, _) => Task.CompletedTask,
            HostAddress = "host.example:47821",
            KnownHosts = known,
            PersistKnownHosts = persisted.Add,
            KnownEpoch = knownEpoch,
            GameId = knownEpoch > 0 ? "g" : null,
        });

    [Fact]
    public async Task OfferForOwnKey_RaisesAnEvent_AndAcceptDeclineSendFrames()
    {
        await using var rig = Rig(new KnownHosts(), []);
        var host = rig.NewConnection();
        var join = rig.Session.JoinAsync();
        await rig.HandshakeAsync(host);
        Assert.True((await join).Joined);

        await host.SendAsync(new StandbyOffer { KeyId = "someone-else" });
        await host.SendAsync(new StandbyOffer { KeyId = rig.PlayerKey });
        var offered = await rig.NextAsync<StandbyOffered>();
        Assert.Equal(rig.PlayerKey, offered.KeyId); // the first offer (for another key) raised nothing

        await rig.Session.AcceptStandbyAsync(["friend.example"], 47821, SuccessorFp);
        var accept = await host.ReceiveAsync<StandbyAccept>();
        Assert.Equal(["friend.example"], accept.Endpoints);
        Assert.Equal(47821, accept.Port);
        Assert.Equal(SuccessorFp, accept.Fingerprint);

        await rig.Session.DeclineStandbyAsync();
        await host.ReceiveAsync<StandbyDecline>();

        await host.SendAsync(new StandbyStatus { State = StandbyStates.Ready, LagEntries = 0 });
        Assert.Equal("ready", (await rig.NextAsync<StandbyStatusChanged>()).Status.State);
        await host.SendAsync(new StandbyRevoke());
        await rig.NextAsync<StandbyRevoked>();
    }

    [Fact]
    public async Task ReadySuccessor_IsPinnedAndVouchedForByTheHostTheClientTrusts_AndPersisted()
    {
        var known = new KnownHosts();
        var persisted = new List<KnownHosts>();
        await using var rig = Rig(known, persisted);
        var host = rig.NewConnection();
        var join = rig.Session.JoinAsync();
        await rig.HandshakeAsync(host);
        await join;
        persisted.Clear();

        await host.SendAsync(Ready(0));
        await rig.NextAsync<SuccessorChanged>();
        Assert.Equal(HostTrust.Trusted, known.Check("friend.example:47821", SuccessorFp));
        Assert.Equal(HostTrust.Trusted, known.Check("10.0.0.2:47821", SuccessorFp));
        var vouch = known.Vouches["friend.example:47821"];
        Assert.Equal(HostKey.Normalize(HostFp), vouch.VouchedBy);
        Assert.Equal(0, vouch.Epoch);
        Assert.Single(persisted);
        Assert.Equal("ready", rig.Session.Successor!.Status);

        // A later mismatch for that endpoint is the usual hard stop.
        Assert.Equal(HostTrust.Mismatch, known.Check("friend.example:47821", HostKey.Fingerprint(new byte[] { 1 })));
    }

    [Fact]
    public async Task Announcement_NeverReplacesAnExistingPin_AndAnOldEpochIsIgnored()
    {
        var known = new KnownHosts();
        var other = HostKey.Fingerprint(new byte[] { 5, 5 });
        Assert.True(known.Pin("friend.example:47821", other));
        await using var rig = Rig(known, [], knownEpoch: 3);
        var host = rig.NewConnection();
        var join = rig.Session.JoinAsync();
        await host.ReceiveAsync<Hello>();
        await host.SendAsync(new Welcome { Protocol = ProtocolVersion.Current, SessionId = "s1", Challenge = "c1", HostKeyFingerprint = HostFp, RequiresPassword = false, Epoch = 3 });
        await host.ReceiveAsync<Join>();
        await host.ReceiveAsync<Auth>();
        await host.SendAsync(new JoinOk());
        await join;

        await host.SendAsync(Ready(2)); // below the epoch this client knows: ignored entirely
        await host.SendAsync(Ready(3));
        await rig.NextAsync<SuccessorChanged>();
        Assert.Equal(HostTrust.Mismatch, known.Check("friend.example:47821", SuccessorFp)); // the old pin stays
        Assert.False(known.Vouches.ContainsKey("friend.example:47821"));
        Assert.Equal(HostTrust.Trusted, known.Check("10.0.0.2:47821", SuccessorFp)); // the other endpoint was free
        Assert.Equal(3, rig.Session.Successor!.Epoch);
    }

    [Fact]
    public async Task JoinOkCarriesTheAnnouncement()
    {
        await using var rig = Rig(new KnownHosts(), []);
        var host = rig.NewConnection();
        var join = rig.Session.JoinAsync();
        await host.ReceiveAsync<Hello>();
        await host.SendAsync(new Welcome { Protocol = ProtocolVersion.Current, SessionId = "s1", Challenge = "c1", HostKeyFingerprint = HostFp, RequiresPassword = false });
        await host.ReceiveAsync<Join>();
        await host.ReceiveAsync<Auth>();
        await host.SendAsync(new JoinOk { Successor = new HostSuccessor { Epoch = 0, Status = StandbyStates.Offered, Roster = [] } });
        await join;
        Assert.Equal("offered", rig.Session.Successor!.Status);
    }

    [Fact]
    public void KnownHostsFile_KeepsVouchesAcrossSaveAndLoad_AndStillReadsTheOldFormat()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("unciv-known-").FullName, "known-hosts.json");
        var known = new KnownHosts();
        known.Pin("old.example:1", HostFp);
        Assert.True(known.PinVouched("new.example:2", SuccessorFp, HostFp, 4));
        Assert.True(known.PinVouched("old.example:1", HostFp, HostFp, 9)); // already trusted on first use: no vouch recorded
        Assert.False(known.Vouches.ContainsKey("old.example:1"));
        Assert.False(known.PinVouched("bad.example:3", SuccessorFp, "not-a-fingerprint", 4));
        JoinFiles.SaveKnownHosts(path, known);

        var loaded = JoinFiles.LoadKnownHosts(path);
        Assert.Equal(HostTrust.Trusted, loaded.Check("old.example:1", HostFp));
        Assert.Equal(HostTrust.Trusted, loaded.Check("new.example:2", SuccessorFp));
        Assert.Equal(new Vouch(HostKey.Normalize(HostFp)!, 4), loaded.Vouches["new.example:2"]);
        Assert.False(loaded.Vouches.ContainsKey("old.example:1"));
        Assert.DoesNotContain("$vouches", loaded.Pins.Keys);

        File.WriteAllText(path, $$"""{"a.example:1":"{{HostFp}}"}""");
        Assert.Equal(HostTrust.Trusted, JoinFiles.LoadKnownHosts(path).Check("a.example:1", HostFp));
    }

    [Fact]
    public void Forget_DropsThePinAndItsVouch()
    {
        var known = new KnownHosts();
        known.PinVouched("x:1", SuccessorFp, HostFp, 1);
        known.Forget("x:1");
        Assert.Empty(known.Pins);
        Assert.Empty(known.Vouches);
    }
}
