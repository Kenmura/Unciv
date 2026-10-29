using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using Unciv.Client.Net.Session;
using Unciv.Client.Net.Transport;
using Unciv.Commands;
using Unciv.Core.Tests.Net.ClientNet;
using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using Unciv.Net;
using Unciv.Net.Transport;
using Unciv.Net.Turns;
using Unciv.Server;
using Unciv.Server.Transport;
using RulesetClass = Unciv.Models.Ruleset.Ruleset;

namespace Unciv.Core.Tests.Net.Robustness;

/// <summary>T225: real TCP+TLS on loopback (port 0), chunked frames, bounded outbound queue, idle and ping policy, resync rate limit.</summary>
[Collection("RulesetCache")]
public sealed class NetRobustnessTests : IDisposable
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);
    private readonly RulesetClass ruleset;

    public NetRobustnessTests()
    {
        var jsons = Path.Combine(RepoRoot(), "android", "assets", "jsons");
        RulesetCache.LoadRulesets(new DirectoryRulesetFileSource(jsons, Path.Combine(jsons, "no mods here")), noMods: true);
        ruleset = RulesetCache.GetVanillaRuleset();
    }

    public void Dispose() => RulesetCache.Clear();

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "android", "assets", "jsons"))) return dir.FullName;
        throw new DirectoryNotFoundException("android/assets/jsons not found above " + AppContext.BaseDirectory);
    }

    private GameInfo NewGame()
    {
        var game = GameStarter.StartNewGame(
            new GameSetupInfo(
                new GameParameters
                {
                    BaseRuleset = ruleset.Name,
                    NumberOfCityStates = 0,
                    VictoryTypes = ruleset.Victories.Keys.ToList(),
                    Players = [new("Babylon", PlayerType.Human), new("Greece", PlayerType.Human), new("Rome", PlayerType.AI)],
                },
                new MapParameters { Type = MapType.Pangaea, Shape = MapShape.Rectangular, Seed = 3, MapSize = MapSize.Tiny }),
            1L, 1000L);
        game.StartSimultaneousTurns();
        return game;
    }

    private static async Task<(ITransport Client, ITransport Server)> ConnectAsync(TcpTlsListener listener)
    {
        var accept = listener.AcceptTransportAsync();
        var client = await TcpTlsClientTransport.ConnectAsync("127.0.0.1", listener.Port,
            expectedFingerprint: HostCertificate.GetFingerprint(listener.ServerCertificate));
        return (client, await accept.WaitAsync(Bound));
    }

    private static async Task<Message> ReceiveAsync(ITransport t)
    {
        var frame = await t.ReceiveAsync().AsTask().WaitAsync(Bound);
        Assert.NotNull(frame);
        return ProtocolCodec.Decode(frame);
    }

    // ------------------------------------------------------------------ chunked frames

    [Fact]
    public async Task LargeMessage_OverRealTls_IsChunkedAndReassembledByTheClient()
    {
        await using var listener = new TcpTlsListener(HostCertificate.Generate(), port: 0);
        listener.Start();
        var (client, server) = await ConnectAsync(listener);
        await using (client)
        await using (server)
        {
            Assert.True(((TcpTlsTransport)server).NegotiatedProtocol is SslProtocols.Tls13 or SslProtocols.Tls12);

            var text = new string('x', ProtocolVersion.MaxFrameBytes * 2 + 12345) + "é€𝄞"; // 3 chunks, multi-byte tail
            Assert.True(Encoding.UTF8.GetByteCount(text) > 2 * ProtocolVersion.MaxFrameBytes);
            var frame = ProtocolCodec.Encode(new LobbyChat { Text = text });

            // Snapshots are host to client: the client accepts the large message, the host does not (see HostRejectsChunkedMessageAboveItsInboundCap).
            var send = server.SendAsync(frame).AsTask(); // not awaited first: 8 MiB exceeds the socket buffers until the peer reads
            var received = Assert.IsType<LobbyChat>(await ReceiveAsync(client));
            await send.WaitAsync(Bound);
            Assert.Equal(text, received.Text);

            // Ordinary frames still work on the same connection afterwards.
            await client.SendAsync(ProtocolCodec.Encode(new Ping()));
            Assert.IsType<Ping>(await ReceiveAsync(server));
        }
    }

    [Fact]
    public async Task HostRejectsChunkedMessageAboveItsInboundCap()
    {
        await using var listener = new TcpTlsListener(HostCertificate.Generate(), port: 0);
        listener.Start();
        var (client, server) = await ConnectAsync(listener);
        await using (client)
        await using (server)
        {
            var frame = ProtocolCodec.Encode(new LobbyChat { Text = new string('x', ProtocolVersion.MaxFrameBytes + 1) });
            _ = client.SendAsync(frame).AsTask().ContinueWith(_ => { }, TaskScheduler.Default); // the host stops reading, the write may fail or stall
            await Assert.ThrowsAsync<InvalidOperationException>(() => server.ReceiveAsync().AsTask().WaitAsync(Bound));
        }
    }

    [Fact]
    public async Task Framing_SmallFrameKeepsLegacyWireFormat()
    {
        using var ms = new MemoryStream();
        await LengthPrefixedFraming.WriteFrameAsync(ms, "abc");
        Assert.Equal([0, 0, 0, 3, (byte)'a', (byte)'b', (byte)'c'], ms.ToArray());
    }

    [Fact]
    public async Task Framing_RejectsMessageOverMaxMessageBytes_AndOversizeChunk()
    {
        // A chunk header above MaxFrameBytes is refused before any allocation.
        var big = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(big, ProtocolVersion.MaxFrameBytes + 1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => LengthPrefixedFraming.ReadFrameAsync(new MemoryStream(big)).AsTask());

        // An endless continuation chain is cut off at MaxMessageBytes.
        using var chain = new MemoryStream();
        var chunk = new byte[ProtocolVersion.MaxFrameBytes];
        var header = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header, unchecked((int)0x80000000) | chunk.Length);
        for (var i = 0; i <= ProtocolVersion.MaxMessageBytes / chunk.Length; i++)
        {
            chain.Write(header);
            chain.Write(chunk);
        }
        chain.Position = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() => LengthPrefixedFraming.ReadFrameAsync(chain).AsTask());

        // A truncated continuation chain is an end-of-stream error, not a partial message.
        using var truncated = new MemoryStream();
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header, unchecked((int)0x80000000) | 2);
        truncated.Write(header);
        truncated.Write([1, 2]);
        truncated.Position = 0;
        await Assert.ThrowsAsync<EndOfStreamException>(() => LengthPrefixedFraming.ReadFrameAsync(truncated).AsTask());
    }

    [Fact]
    public void Codec_RoundTripsMessageAboveOneChunk_AndRefusesAboveMessageLimit()
    {
        var text = new string('a', ProtocolVersion.MaxFrameBytes + 1);
        Assert.Equal(text, Assert.IsType<LobbyChat>(ProtocolCodec.Decode(ProtocolCodec.Encode(new LobbyChat { Text = text }))).Text);
        Assert.Throws<InvalidOperationException>(() => ProtocolCodec.Encode(new LobbyChat { Text = new string('a', ProtocolVersion.MaxMessageBytes) }));
    }

    // ------------------------------------------------------------------ bounded outbound queue

    private sealed class StuckTransport : ITransport
    {
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<string> Sent { get; } = [];
        public bool Closed { get; private set; }
        public bool IsOpen => !Closed;
        public void Release() => release.TrySetResult();

        public async ValueTask SendAsync(string frame, CancellationToken cancellationToken = default)
        {
            await release.Task.WaitAsync(cancellationToken);
            lock (Sent) Sent.Add(frame);
        }

        public ValueTask<string?> ReceiveAsync(CancellationToken cancellationToken = default) => new((string?)null);

        public ValueTask CloseAsync()
        {
            Closed = true;
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync() => CloseAsync();
    }

    [Fact]
    public async Task BoundedSend_StuckPeer_NeverBlocksSender_AndIsDisconnectedWhenQueueFull()
    {
        var inner = new StuckTransport();
        var bounded = new BoundedSendTransport(inner, maxFrames: 4);
        for (var i = 0; i < 4; i++) await bounded.SendAsync("f" + i).AsTask().WaitAsync(Bound); // returns at once although the peer reads nothing
        await Assert.ThrowsAsync<InvalidOperationException>(() => bounded.SendAsync("overflow").AsTask().WaitAsync(Bound));
        Assert.True(bounded.DisconnectedAsSlowReader);
        Assert.True(inner.Closed);
        Assert.False(bounded.IsOpen);
        await Assert.ThrowsAsync<InvalidOperationException>(() => bounded.SendAsync("late").AsTask());
    }

    private sealed class DyingTransport : ITransport
    {
        public bool Closed { get; private set; }
        public bool IsOpen => !Closed;
        public ValueTask SendAsync(string frame, CancellationToken cancellationToken = default) => throw new IOException("connection reset");
        public ValueTask<string?> ReceiveAsync(CancellationToken cancellationToken = default) => new((string?)null);

        public ValueTask CloseAsync()
        {
            Closed = true;
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync() => CloseAsync();
    }

    [Fact]
    public async Task BoundedSend_WhenTheInnerWriteFails_ClosesTheInnerTransport()
    {
        var inner = new DyingTransport();
        var bounded = new BoundedSendTransport(inner);
        await bounded.SendAsync("x");
        await Task.Run(async () => { while (inner.IsOpen) await Task.Yield(); }).WaitAsync(Bound); // the pump closes it itself, no CloseAsync needed
        Assert.False(bounded.IsOpen);
        await bounded.CloseAsync().AsTask().WaitAsync(Bound);
        await bounded.CloseAsync().AsTask().WaitAsync(Bound); // idempotent
    }

    [Fact]
    public async Task BoundedSend_ByteLimit_CountsUtf8Bytes()
    {
        var inner = new StuckTransport();
        var bounded = new BoundedSendTransport(inner, maxFrames: 100, maxBytes: 100);
        await Assert.ThrowsAsync<InvalidOperationException>(() => bounded.SendAsync(new string('€', 40)).AsTask()); // 40 chars, 120 bytes
        Assert.True(bounded.DisconnectedAsSlowReader);
    }

    [Fact]
    public async Task BoundedSend_ByteLimit_DisconnectsSlowReader()
    {
        var inner = new StuckTransport();
        var bounded = new BoundedSendTransport(inner, maxFrames: 100, maxBytes: 100);
        await bounded.SendAsync(new string('a', 60));
        await Assert.ThrowsAsync<InvalidOperationException>(() => bounded.SendAsync(new string('b', 60)).AsTask());
        Assert.True(bounded.DisconnectedAsSlowReader);
    }

    [Fact]
    public async Task BoundedSend_DeliversInOrder_AndCloseDrainsQueuedFrames()
    {
        var inner = new StuckTransport();
        var bounded = new BoundedSendTransport(inner, maxFrames: 100, drainGrace: Bound);
        for (var i = 0; i < 50; i++) await bounded.SendAsync("f" + i);
        inner.Release();
        await bounded.CloseAsync().AsTask().WaitAsync(Bound);
        Assert.Equal(Enumerable.Range(0, 50).Select(i => "f" + i), inner.Sent);
        Assert.True(inner.Closed);
        Assert.False(bounded.DisconnectedAsSlowReader);
    }

    [Fact]
    public async Task BoundedSend_CloseWithStuckPeer_GivesUpAfterGrace()
    {
        var inner = new StuckTransport();
        var bounded = new BoundedSendTransport(inner, maxFrames: 10, drainGrace: TimeSpan.FromMilliseconds(50));
        await bounded.SendAsync("stuck");
        await bounded.CloseAsync().AsTask().WaitAsync(Bound);
        Assert.True(inner.Closed);
        Assert.Empty(inner.Sent);
    }

    // ------------------------------------------------------------------ host policy (in-memory, ManualClock)

    private static (GameHost Host, ManualClock Clock) NewHost(GameInfo game, HostLimits? limits = null)
    {
        var clock = new ManualClock(1000L);
        var session = new GameSession(game) { Clock = () => clock.NowMillis };
        return (new GameHost(session, clock: clock, limits: limits), clock);
    }

    [Fact]
    public async Task Ping_IsAnsweredWhileTheActorIsBusy()
    {
        var (host, _) = NewHost(NewGame());
        await using var _h = host;
        var (client, hostEnd) = InMemoryTransport.CreatePair();
        await host.AttachSessionAsync("s1", "Babylon", hostEnd);
        Assert.IsType<Snapshot>(await ReceiveAsync(client));

        var release = new TaskCompletionSource();
        var busy = host.EnqueueAsync(() => release.Task); // an AI turn occupying the single-threaded actor
        await client.SendAsync(ProtocolCodec.Encode(new Ping()));
        Assert.IsType<Pong>(await ReceiveAsync(client));
        Assert.False(busy.IsCompleted);
        release.SetResult();
        await busy.WaitAsync(Bound);
    }

    [Fact]
    public async Task ResyncRequests_AreRateLimitedAndCoalesced()
    {
        var (host, clock) = NewHost(NewGame(), new HostLimits { ResyncMinIntervalMillis = 1000 });
        await using var _h = host;
        var (client, hostEnd) = InMemoryTransport.CreatePair();
        await host.AttachSessionAsync("s1", "Babylon", hostEnd);
        Assert.IsType<Snapshot>(await ReceiveAsync(client));

        await client.SendAsync(ProtocolCodec.Encode(new ResyncRequest()));
        Assert.IsType<Snapshot>(await ReceiveAsync(client)); // first one is answered at once

        for (var i = 0; i < 20; i++) await client.SendAsync(ProtocolCodec.Encode(new ResyncRequest()));
        await client.SendAsync(ProtocolCodec.Encode(new Ping())); // everything before it was processed once its Pong arrives
        // The reader answers Pong immediately, so wait for the actor too.
        Assert.IsType<Pong>(await ReceiveAsync(client));
        await host.EnqueueAsync(() => Task.CompletedTask).WaitAsync(Bound);
        await host.EnqueueAsync(() => { host.Tick(); return Task.CompletedTask; }).WaitAsync(Bound);
        Assert.True(host.Sessions["s1"].ResyncPending); // owed, not sent: still inside the window

        clock.Advance(1000);
        await host.EnqueueAsync(() => { host.Tick(); return Task.CompletedTask; }).WaitAsync(Bound);
        Assert.IsType<Snapshot>(await ReceiveAsync(client)); // the 20 requests became exactly one snapshot
        Assert.False(host.Sessions["s1"].ResyncPending);

        await client.SendAsync(ProtocolCodec.Encode(new Ping()));
        Assert.IsType<Pong>(await ReceiveAsync(client)); // nothing else was queued before the Pong
    }

    [Fact]
    public async Task SilentSession_IsDroppedAfterIdleTimeout_ButAnyFrameKeepsItAlive()
    {
        var (host, clock) = NewHost(NewGame(), new HostLimits { IdleTimeoutMillis = 10_000 });
        await using var _h = host;
        var (client, hostEnd) = InMemoryTransport.CreatePair();
        var (silent, silentHostEnd) = InMemoryTransport.CreatePair();
        await host.AttachSessionAsync("live", "Babylon", hostEnd);
        await host.AttachSessionAsync("silent", "Greece", silentHostEnd);
        await ReceiveAsync(client);
        await ReceiveAsync(silent);

        clock.Advance(8_000);
        await client.SendAsync(ProtocolCodec.Encode(new Ping()));
        Assert.IsType<Pong>(await ReceiveAsync(client));
        clock.Advance(8_000); // 16 s since attach, 8 s since the live client's ping
        await host.EnqueueAsync(() => { host.Tick(); return Task.CompletedTask; }).WaitAsync(Bound);
        await host.EnqueueAsync(() => Task.CompletedTask).WaitAsync(Bound); // let the detach complete

        Assert.True(host.Sessions.ContainsKey("live"));
        Assert.False(host.Sessions.ContainsKey("silent"));
        Assert.Null(await silent.ReceiveAsync().AsTask().WaitAsync(Bound)); // the silent peer sees the close
    }

    // ------------------------------------------------------------------ real TCP+TLS, several clients

    /// <summary>Delays every send by a fixed small amount to put some latency on the link; ordering and content are untouched.</summary>
    private sealed class LatencyTransport(ITransport inner, TimeSpan delay) : ITransport
    {
        public bool IsOpen => inner.IsOpen;

        public async ValueTask SendAsync(string frame, CancellationToken cancellationToken = default)
        {
            await Task.Delay(delay, cancellationToken);
            await inner.SendAsync(frame, cancellationToken);
        }

        public ValueTask<string?> ReceiveAsync(CancellationToken cancellationToken = default) => inner.ReceiveAsync(cancellationToken);
        public ValueTask CloseAsync() => inner.CloseAsync();
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    [Fact]
    public async Task ThreeClientsOverRealTls_WithLatency_SlowAndHalfOpenPeersDoNotHurtTheOthers()
    {
        var (host, clock) = NewHost(NewGame(), new HostLimits { MaxQueuedFrames = 8, IdleTimeoutMillis = 60_000 });
        await using var _h = host;
        await using var listener = new TcpTlsListener(HostCertificate.Generate(), port: 0);
        listener.Start();

        var (clientA, serverA) = await ConnectAsync(listener);
        var (clientB, serverB) = await ConnectAsync(listener);
        var (clientC, serverC) = await ConnectAsync(listener);
        // A, B: well-behaved with latency. C: connects, then never reads (a half-open or stalled peer).
        var latencyA = new LatencyTransport(clientA, TimeSpan.FromMilliseconds(3));
        var latencyB = new LatencyTransport(clientB, TimeSpan.FromMilliseconds(3));
        await using (clientA) await using (clientB) await using (clientC)
        {
            await host.AttachSessionAsync("A", "Babylon", serverA);
            await host.AttachSessionAsync("B", "Greece", serverB);
            await host.AttachSessionAsync("C", "Rome", serverC);
            Assert.IsType<Snapshot>(await ReceiveAsync(latencyA));
            Assert.IsType<Snapshot>(await ReceiveAsync(latencyB));

            // Flood C with large frames until its queue overflows; the host must not block and must drop only C.
            var chunk = new string('z', 1024 * 1024);
            var c = host.Sessions["C"].Transport;
            await host.EnqueueAsync(async () =>
            {
                for (var i = 0; i < 200 && c.IsOpen; i++)
                {
                    try { await c.SendAsync(chunk); }
                    catch (InvalidOperationException) { break; }
                }
            }).WaitAsync(Bound);
            await host.EnqueueAsync(() => Task.CompletedTask).WaitAsync(Bound);
            await host.EnqueueAsync(() => Task.CompletedTask).WaitAsync(Bound); // the reader's detach of C was queued behind the first round trip
            Assert.False(c.IsOpen);
            Assert.True(((BoundedSendTransport)c).DisconnectedAsSlowReader);
            await Task.Run(async () =>
            {
                while (true)
                {
                    var present = true;
                    await host.EnqueueAsync(() => { present = host.Sessions.ContainsKey("C"); return Task.CompletedTask; });
                    if (!present) return;
                    await Task.Yield();
                }
            }).WaitAsync(Bound); // the reader notices the close and detaches C through the mailbox

            // A and B still talk to the host through the same actor.
            await latencyA.SendAsync(ProtocolCodec.Encode(new Ping()));
            await latencyB.SendAsync(ProtocolCodec.Encode(new Ping()));
            Assert.IsType<Pong>(await ReceiveAsync(latencyA));
            Assert.IsType<Pong>(await ReceiveAsync(latencyB));
            await latencyA.SendAsync(ProtocolCodec.Encode(new ResyncRequest()));
            Assert.IsType<Snapshot>(await ReceiveAsync(latencyA));

            // B goes silent (half-open from the host's view); A keeps pinging, so only B times out.
            clock.Advance(40_000);
            await latencyA.SendAsync(ProtocolCodec.Encode(new Ping()));
            Assert.IsType<Pong>(await ReceiveAsync(latencyA));
            clock.Advance(40_000);
            await host.EnqueueAsync(() => { host.Tick(); return Task.CompletedTask; }).WaitAsync(Bound);
            await host.EnqueueAsync(() => Task.CompletedTask).WaitAsync(Bound);
            Assert.True(host.Sessions.ContainsKey("A"));
            Assert.False(host.Sessions.ContainsKey("B"));
            Assert.Null(await clientB.ReceiveAsync().AsTask().WaitAsync(Bound)); // B's socket was closed by the host
        }
    }

    [Fact]
    public async Task Tls_ProtocolFloorIsTls12_AndSessionNegotiatesAtLeastThat()
    {
        // Platform matrix (SERVER.md 6.3): both ends enable TLS 1.3 with a TLS 1.2 fallback, so a platform whose SslStream lacks 1.3 still connects.
        await using var listener = new TcpTlsListener(HostCertificate.Generate(), port: 0);
        listener.Start();
        var (client, server) = await ConnectAsync(listener);
        await using (client)
        await using (server)
        {
            var negotiated = ((TcpTlsTransport)server).NegotiatedProtocol;
            Assert.NotNull(negotiated);
            Assert.NotEqual(SslProtocols.None, negotiated);
            Assert.True(negotiated is SslProtocols.Tls12 or SslProtocols.Tls13);
        }
    }

    [Fact]
    public async Task RawTcpPeerThatNeverHandshakes_DoesNotBlockTheListener()
    {
        await using var listener = new TcpTlsListener(HostCertificate.Generate(), port: 0, handshakeTimeout: TimeSpan.FromMilliseconds(200));
        listener.Start();
        using var raw = new TcpClient();
        await raw.ConnectAsync("127.0.0.1", listener.Port);
        await Assert.ThrowsAnyAsync<Exception>(() => listener.AcceptTransportAsync().WaitAsync(Bound));
        var (client, server) = await ConnectAsync(listener); // the next, honest peer is accepted
        await client.DisposeAsync();
        await server.DisposeAsync();
    }

    // ------------------------------------------------------------------ client keep-alive and host-silence policy

    private static ClientSessionOptions Options(Unciv.Net.Identity.ClientIdentity identity, TimeSpan keepAlive, TimeSpan silence) => new()
    {
        Identity = identity,
        DisplayName = "Alice",
        RulesetHash = "hash",
        ExpectedFingerprint = ClientNetRig.Fingerprint,
        ReconnectDelay = (_, _) => Task.CompletedTask,
        KeepAliveInterval = keepAlive,
        HostSilenceTimeout = silence
    };

    [Fact]
    public async Task Client_PingsTheHostPeriodicallyOnceJoined()
    {
        await using var rig = new ClientNetRig(id => Options(id, TimeSpan.FromMilliseconds(20), Timeout.InfiniteTimeSpan));
        var host = await rig.StartInGameAsync(ClientNetRig.Snap(1, 1));
        await host.ReceiveAsync<Ping>();
        await host.ReceiveAsync<Ping>(); // keeps pinging
        await host.SendAsync(new Pong());
    }

    [Fact]
    public async Task Client_DropsAndReconnects_WhenTheHostGoesSilent()
    {
        await using var rig = new ClientNetRig(id => Options(id, Timeout.InfiniteTimeSpan, TimeSpan.FromMilliseconds(100)));
        var first = await rig.StartInGameAsync(ClientNetRig.Snap(1, 1));
        var second = rig.NewConnection();
        await first.Transport.ReceiveAsync().AsTask().WaitAsync(Bound); // never answers: the client gives up on it (our end sees the close) ...
        await second.ReceiveAsync<Hello>(); // ... and dials the next host
        Assert.Equal(2, rig.ConnectAttempts);
    }
}
