using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Unciv.Client.Net.Hosting;
using Unciv.Client.Net.Session;
using Unciv.Client.Net.Transport;
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

    /// <summary>T354: what a relay room "presents"; null (the default) is an unreachable room.</summary>
    public Func<RelayRoute, string?> RelayAnswer { get; set; } = _ => null;

    public Task<string?> PresentedFingerprintViaRelayAsync(RelayRoute route, string expectedFingerprint, TimeSpan timeout, CancellationToken cancellationToken)
    {
        lock (Dialled) Dialled.Add($"relay:{route.Relay}/{route.Room}");
        return Task.FromResult(RelayAnswer(route));
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
    public async Task Reprobe_RunsOnlyWhenDue_TurnsReadyUnreachableOnLoss_AndBackWhenTheListenerReturns()
    {
        var (c, prober, clock) = Make();
        Assert.False(c.ReprobeDue); // nobody designated
        c.Designate("candidate", out _);
        Assert.False(c.ReprobeDue); // not accepted yet
        Assert.False(await c.ReprobeIfDueAsync());
        Assert.Null(await c.AcceptAsync("candidate", Accept(Fp)));
        Assert.Single(prober.Dialled);

        clock.AdvanceSeconds(119);
        Assert.False(c.ReprobeDue);
        Assert.False(await c.ReprobeIfDueAsync());
        Assert.Single(prober.Dialled);

        clock.AdvanceSeconds(2);
        Assert.True(c.ReprobeDue);
        Assert.False(await c.ReprobeIfDueAsync()); // still ready: probed, nothing changed
        Assert.Equal(2, prober.Dialled.Count);
        Assert.False(c.ReprobeDue);

        prober.Answer = (_, _) => null;
        clock.AdvanceSeconds(121);
        Assert.True(await c.ReprobeIfDueAsync()); // ready -> unreachable
        Assert.Equal(StandbyStates.Unreachable, c.State);
        Assert.Null(c.Announcement(0, []).Successor);

        clock.AdvanceSeconds(121);
        Assert.False(await c.ReprobeIfDueAsync()); // still unreachable: no change to announce
        prober.Answer = (_, _) => Fp;
        clock.AdvanceSeconds(121);
        Assert.True(await c.ReprobeIfDueAsync()); // unreachable -> ready
        Assert.Equal(StandbyStates.Ready, c.State);
        Assert.Equal("candidate", c.Announcement(0, []).Successor!.KeyId);
    }

    [Fact]
    public async Task Reprobe_WithSeveralEndpoints_TriesTheOthersBeforeDeclaringTheStandbyLost()
    {
        var (c, prober, clock) = Make();
        c.Designate("candidate", out _);
        prober.Answer = (host, _) => host == "lan.example" ? null : Fp; // the LAN address is dead, the public one answers
        Assert.Equal(ErrorCodes.ProbeFailed, await c.AcceptAsync("candidate", Accept(Fp, 47821, "lan.example", "wan.example")));
        clock.AdvanceSeconds(61);
        Assert.Null(await c.AcceptAsync("candidate", Accept(Fp, 47821, "lan.example", "wan.example"))); // the repeat tries the next one in rotation
        Assert.Equal(StandbyStates.Ready, c.State);

        var observed = new List<string>();
        prober.Answer = (host, _) =>
        {
            observed.Add(c.State); // what a client joining mid-round would see
            return host == "lan.example" ? null : Fp;
        };
        for (var round = 0; round < 4; round++)
        {
            clock.AdvanceSeconds(121);
            Assert.False(await c.ReprobeIfDueAsync()); // whichever endpoint comes first, the state never flaps
            Assert.Equal(StandbyStates.Ready, c.State);
        }

        Assert.All(observed, state => Assert.Equal(StandbyStates.Ready, state)); // a failed first endpoint is tentative while the others are tried
        prober.Answer = (_, _) => null; // now everything is dead: one re-probe tries every endpoint, then reports the loss
        var dialled = prober.Dialled.Count;
        clock.AdvanceSeconds(121);
        Assert.True(await c.ReprobeIfDueAsync());
        Assert.Equal(2, prober.Dialled.Count - dialled);
        Assert.Equal(StandbyStates.Unreachable, c.State);
    }

    [Fact]
    public async Task Reprobe_AfterARestart_ProbesOnceAndKeepsTheDesignationOnAFingerprintChange()
    {
        var (c, prober, clock) = Make();
        c.Designate("candidate", out _);
        Assert.Null(await c.AcceptAsync("candidate", Accept(Fp)));
        var record = c.ToRecord();

        var (restored, prober2, clock2) = Make();
        restored.Restore(record);
        Assert.Equal(StandbyStates.Unreachable, restored.State);
        Assert.True(restored.ReprobeDue); // never probed by this process
        Assert.True(await restored.ReprobeIfDueAsync());
        Assert.Equal(StandbyStates.Ready, restored.State);
        Assert.Single(prober2.Dialled);

        prober2.Answer = (_, _) => OtherFp; // somebody else answers on the port
        clock2.AdvanceSeconds(121);
        Assert.True(await restored.ReprobeIfDueAsync());
        Assert.Equal(StandbyStates.Unreachable, restored.State);
        Assert.Equal("candidate", restored.KeyId);
        _ = (prober, clock);
    }

    [Fact]
    public async Task Reprobe_AMinimumIntervalBelowTheRateLimitStillNeverProbesMoreThanOncePerMinute()
    {
        var clock = new ManualClock(1_000_000);
        var prober = new FakeProber { Answer = (_, _) => Fp };
        var c = new StandbyCoordinator(clock, prober, new StandbyLimits { ReprobeIntervalMillis = 1000 });
        c.Designate("candidate", out _);
        Assert.Null(await c.AcceptAsync("candidate", Accept(Fp)));
        clock.AdvanceSeconds(30);
        Assert.False(c.ReprobeDue);
        Assert.False(await c.ReprobeIfDueAsync());
        Assert.Single(prober.Dialled);
        clock.AdvanceSeconds(31);
        Assert.True(c.ReprobeDue);
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

/// <summary>The real dialler (T282): a TLS handshake plus one Hello; only a standby listener's Welcome counts.</summary>
public sealed class TlsEndpointProberTests
{
    private static async Task<string?> ProbeAsync(int port, int millis = 3000) =>
        await new TlsEndpointProber().PresentedFingerprintAsync("127.0.0.1", port, TimeSpan.FromMilliseconds(millis), CancellationToken.None);

    private static int FreePort()
    {
        var free = new TcpListener(IPAddress.Loopback, 0);
        free.Start();
        var port = ((IPEndPoint)free.LocalEndpoint).Port;
        free.Stop();
        return port;
    }

    [Fact]
    public async Task ReportsTheFingerprintOfAStandbyListener()
    {
        using var cert = HostCertificate.Generate();
        await using var listener = new StandbyListener(cert, 0);
        listener.Start();
        Assert.True(HostKey.Matches(await ProbeAsync(listener.Port), HostCertificate.GetFingerprint(cert)));
    }

    [Fact]
    public async Task IsNullForAClosedPort_ASilentSocket_AndATlsServiceThatIsNotAStandbyListener()
    {
        Assert.Null(await ProbeAsync(FreePort()));

        // A listener that accepts the connection and never speaks TLS runs into the timeout.
        var silent = new TcpListener(IPAddress.Loopback, 0);
        silent.Start();
        try
        {
            var acceptSilent = silent.AcceptTcpClientAsync();
            Assert.Null(await ProbeAsync(((IPEndPoint)silent.LocalEndpoint).Port, 300));
            (await acceptSilent).Dispose();
        }
        finally { silent.Stop(); }

        // The handshake alone is no longer enough: a TLS service that does not answer Hello as a standby is "nothing usable".
        using var cert = HostCertificate.Generate();
        await using var plain = new TcpTlsListener(cert, 0);
        plain.Start();
        using var stop = new CancellationTokenSource();
        var accepted = Task.Run(async () =>
        {
            try { while (true) await (await plain.AcceptTransportAsync(stop.Token)).DisposeAsync(); }
            catch (Exception) { }
        });
        Assert.Null(await ProbeAsync(plain.Port, 1000));

        // A real host (Welcome without standby) is not a standby either.
        await using var host = new TcpTlsListener(cert, 0);
        host.Start();
        var served = Task.Run(async () =>
        {
            try
            {
                await using var transport = await host.AcceptTransportAsync(stop.Token);
                await transport.ReceiveAsync(stop.Token);
                await transport.SendAsync(ProtocolCodec.Encode(new Welcome { Protocol = ProtocolVersion.Current, SessionId = "s", Challenge = "c", HostKeyFingerprint = HostCertificate.GetFingerprint(cert), RequiresPassword = false }), stop.Token);
                await Task.Delay(200, stop.Token);
            }
            catch (Exception) { }
        });
        Assert.Null(await ProbeAsync(host.Port, 2000));

        // A hostile endpoint that answers with a frame over the probe's cap is "nothing usable", not an exception.
        await using var hostile = new TcpTlsListener(cert, 0);
        hostile.Start();
        var flooded = Task.Run(async () =>
        {
            try
            {
                await using var transport = await hostile.AcceptTransportAsync(stop.Token);
                await transport.ReceiveAsync(stop.Token);
                await transport.SendAsync(new string('x', StandbyListener.MaxFrameBytes * 2), stop.Token);
                await Task.Delay(200, stop.Token);
            }
            catch (Exception) { }
        });
        Assert.Null(await ProbeAsync(hostile.Port, 2000));
        stop.Cancel();
        hostile.Stop();
        await flooded;
        plain.Stop();
        host.Stop();
        await accepted;
        await served;
    }
}

/// <summary>T282: the successor's standby listener answers Hello with Welcome{standby: true} and refuses the rest with not-host-yet.</summary>
public sealed class StandbyListenerTests
{
    private static async Task<(ITransport Transport, X509Certificate2 Cert)> ConnectAsync(StandbyListener listener, X509Certificate2 cert) =>
        (await TcpTlsClientTransport.ConnectAsync("127.0.0.1", listener.Port, expectedFingerprint: HostCertificate.GetFingerprint(cert)), cert);

    private static Hello Hello() => new() { Protocol = ProtocolVersion.Current, ClientVersion = "t", RulesetHash = "", ModList = [] };

    private static async Task<Message?> ReadAsync(ITransport transport)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var frame = await transport.ReceiveAsync(timeout.Token);
        return frame == null ? null : ProtocolCodec.Decode(frame);
    }

    [Fact]
    public async Task Hello_GetsAStandbyWelcome_AndAnythingElseIsNotHostYet()
    {
        using var cert = HostCertificate.Generate();
        await using var listener = new StandbyListener(cert, 0);
        listener.Start();

        var (first, _) = await ConnectAsync(listener, cert);
        await using (first)
        {
            await first.SendAsync(ProtocolCodec.Encode(Hello()));
            var welcome = Assert.IsType<Welcome>(await ReadAsync(first));
            Assert.True(welcome.Standby);
            Assert.Equal(ProtocolVersion.Current, welcome.Protocol);
            Assert.True(HostKey.Matches(welcome.HostKeyFingerprint, HostCertificate.GetFingerprint(cert)));
            await first.SendAsync(ProtocolCodec.Encode(new Join { PlayerKey = "k", DisplayName = "x" }));
            Assert.Equal(ErrorCodes.NotHostYet, Assert.IsType<Error>(await ReadAsync(first)).Code);
            Assert.Null(await ReadAsync(first)); // then the connection is closed
        }

        var (second, _) = await ConnectAsync(listener, cert);
        await using (second)
        {
            await second.SendAsync(ProtocolCodec.Encode(new StandbySubscribe { GameId = "g", HaveSeq = 0 }));
            Assert.Equal(ErrorCodes.NotHostYet, Assert.IsType<Error>(await ReadAsync(second)).Code); // no Hello needed to be refused
        }

        var (third, _) = await ConnectAsync(listener, cert);
        await using (third)
        {
            await third.SendAsync("{not json");
            Assert.Equal(ErrorCodes.NotHostYet, Assert.IsType<Error>(await ReadAsync(third)).Code);
        }
    }

    [Fact]
    public async Task AnOversizedFrame_IsNeverBuffered_TheConnectionJustCloses()
    {
        using var cert = HostCertificate.Generate();
        await using var listener = new StandbyListener(cert, 0);
        listener.Start();
        var (transport, _) = await ConnectAsync(listener, cert);
        await using (transport)
        {
            try { await transport.SendAsync(new string('x', StandbyListener.MaxFrameBytes * 2)); }
            catch (IOException) { /* the listener closed on the length prefix, before the rest was written: that is the point */ }
            Assert.Null(await ReadAsync(transport));
        }
    }

    [Fact]
    public async Task ASecondHello_IsRefused_AndAnIdleConnectionIsClosedAfterItsLifetime()
    {
        using var cert = HostCertificate.Generate();
        await using var listener = new StandbyListener(cert, 0) { ConnectionLifetime = TimeSpan.FromMilliseconds(300) };
        listener.Start();
        var (transport, _) = await ConnectAsync(listener, cert);
        await using (transport)
        {
            await transport.SendAsync(ProtocolCodec.Encode(Hello()));
            Assert.IsType<Welcome>(await ReadAsync(transport));
            await transport.SendAsync(ProtocolCodec.Encode(Hello()));
            Assert.IsType<Error>(await ReadAsync(transport));
        }

        var (idle, _) = await ConnectAsync(listener, cert);
        await using (idle) Assert.Null(await ReadAsync(idle));
    }

    [Fact]
    public async Task MoreThanTheConnectionLimit_IsClosedAtOnce_AndAStoppedListenerDropsItsClients()
    {
        using var cert = HostCertificate.Generate();
        await using var listener = new StandbyListener(cert, 0, TimeSpan.FromSeconds(2));
        listener.Start();
        var held = new List<TcpClient>();
        try
        {
            for (var i = 0; i < StandbyListener.MaxConnections + 4; i++)
            {
                var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, listener.Port);
                held.Add(client); // connected, never speaks TLS: holds a slot until the handshake times out
            }
            var last = held[^1].GetStream();
            var buffer = new byte[1];
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            Assert.Equal(0, await last.ReadAsync(buffer, timeout.Token)); // closed by the listener without a handshake
        }
        finally
        {
            foreach (var client in held) client.Dispose();
        }
    }

    [Fact]
    public async Task Stop_ClosesThePortAtOnce_SoTheRealHostCanBindIt()
    {
        using var cert = HostCertificate.Generate();
        var listener = new StandbyListener(cert, 0);
        listener.Start();
        var port = listener.Port;
        listener.Stop();
        await using var next = new TcpTlsListener(cert, port);
        next.Start();
        Assert.Equal(port, next.Port);
        await listener.DisposeAsync();
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
