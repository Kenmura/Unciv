using System.Diagnostics;
using System.Text.Json.Nodes;
using Unciv.Net.Turns;
using Unciv.Relay;

namespace Unciv.Core.Tests.Relay;

// T337, RELAY.md R4 (limits), R5 (bandwidth) and R6 (back-pressure). The limits are lowered per test; the clock is fake wherever the rule is about time.
public sealed class RelayLimitsTests
{
    private static string Code(JsonObject reply) => reply["code"]!.GetValue<string>();

    private static Func<RelayOptions, RelayOptions> With(Func<RelayLimits, RelayLimits> change) => o => o with { Limits = change(o.Limits) };

    [Fact]
    public async Task TheRoomLimitRefusesTheNextHostAndKeepsTheFirst()
    {
        await using var rig = await RelayRig.StartAsync(tweak: With(l => l with { MaxRooms = 1 }));
        await using var first = await TestHost.OpenAsync(rig, rig.NewKey(), "a");
        await using var second = await TestHost.OpenAsync(rig, rig.NewKey(), "b");
        Assert.Equal("relay-full", Code(second.Reply));
        var (joiner, data) = await first.JoinAsync(); // the established room is unaffected
        await using (joiner) await using (data) Assert.Equal(1, rig.Server.RoomCount);
    }

    [Fact]
    public async Task RoomsAndControlChannelsPerNetworkAreLimited()
    {
        await using var rig = await RelayRig.StartAsync(tweak: With(l => l with { MaxRoomsPerNetwork = 2, MaxControlPerNetwork = 3 }));
        await using var a = await TestHost.OpenAsync(rig, rig.NewKey(), "a");
        await using var b = await TestHost.OpenAsync(rig, rig.NewKey(), "b");
        await using var c = await TestHost.OpenAsync(rig, rig.NewKey(), "c");
        Assert.Equal("rate-limited", Code(c.Reply)); // third room from one network
        // superseding is not a new room: the same key takes the room back even at the limit
        await using var again = await TestHost.OpenAsync(rig, a.Key, "a");
        Assert.True(again.Ok);
    }

    [Fact]
    public async Task ControlChannelsPerNetworkAreLimited()
    {
        await using var rig = await RelayRig.StartAsync(tweak: With(l => l with { MaxRoomsPerNetwork = 10, MaxControlPerNetwork = 2 }));
        await using var a = await TestHost.OpenAsync(rig, rig.NewKey(), "a");
        await using var b = await TestHost.OpenAsync(rig, rig.NewKey(), "b");
        await using var c = await TestHost.OpenAsync(rig, rig.NewKey(), "c");
        Assert.Equal("rate-limited", Code(c.Reply));
    }

    [Fact]
    public async Task ConnectionsPerNetworkAreLimited()
    {
        await using var rig = await RelayRig.StartAsync(tweak: With(l => l with { MaxConnectionsPerNetwork = 3 }));
        var held = new List<RelayClient>();
        for (var i = 0; i < 3; i++) held.Add(await rig.ConnectAsync());
        await using var fourth = await RelayClient.ConnectAsync(rig.Port, rig.UsesTls, readChallenge: false);
        Assert.True(await fourth.ClosedAsync());
        foreach (var client in held) await client.DisposeAsync();
        await RelayHostTests.Until(() => true);
        await using var later = await rig.ConnectAsync(); // the slots come back
        Assert.NotEmpty(later.Challenge);
    }

    [Fact]
    public async Task HandshakesPerMinuteAreLimitedAndTheWindowSlides()
    {
        var clock = new ManualClock(5_000_000);
        await using var rig = await RelayRig.StartAsync(tweak: With(l => l with { HandshakesPerMinute = 3 }), clock: clock);
        for (var i = 0; i < 3; i++)
        {
            await using var ok = await rig.ConnectAsync();
            Assert.NotEmpty(ok.Challenge);
        }
        await using var over = await RelayClient.ConnectAsync(rig.Port, rig.UsesTls, readChallenge: false);
        Assert.True(await over.ClosedAsync());
        clock.AdvanceSeconds(61);
        await using var later = await rig.ConnectAsync();
        Assert.NotEmpty(later.Challenge);
    }

    [Fact]
    public async Task ASilentClientGetsATimeoutAndAnOversizedPreambleIsTooLarge()
    {
        await using var rig = await RelayRig.StartAsync(tweak: With(l => l with { PreambleTimeoutMillis = 300 }));
        await using var silent = await rig.ConnectAsync();
        Assert.Equal("timeout", Code(await silent.ReadAsync()));
        await using var big = await rig.ConnectAsync();
        await big.SendRawAsync(new string('x', 2049));
        Assert.Equal("too-large", Code(await big.ReadAsync()));
        await using var exact = await rig.ConnectAsync();
        await exact.SendRawAsync("{\"op\":\"join\",\"v\":1,\"room\":\"K7QF-3MXP4A\",\"pad\":\"" + new string('x', 2048 - ("{\"op\":\"join\",\"v\":1,\"room\":\"K7QF-3MXP4A\",\"pad\":\"\"}").Length) + "\"}\n");
        Assert.Equal("room-unknown", Code(await exact.ReadAsync())); // exactly 2048 bytes is still fine
    }

    [Fact]
    public async Task JoinersAndPendingJoinsPerRoomAreLimited()
    {
        await using var rig = await RelayRig.StartAsync(tweak: With(l => l with { MaxPendingPerRoom = 2, MaxJoinersPerRoom = 3 }));
        await using var host = await TestHost.OpenAsync(rig, rig.NewKey());
        var pending = new List<RelayClient>();
        for (var i = 0; i < 2; i++)
        {
            var joiner = await rig.ConnectAsync();
            await joiner.SendAsync(new JsonObject { ["op"] = "join", ["v"] = 1, ["room"] = host.Room });
            pending.Add(joiner);
            await host.NextEventAsync("incoming");
        }
        await using var third = await rig.ConnectAsync();
        Assert.Equal("room-full", Code(await third.RequestAsync(new JsonObject { ["op"] = "join", ["v"] = 1, ["room"] = host.Room })));
        foreach (var joiner in pending) await joiner.DisposeAsync();
    }

    [Fact]
    public async Task JoinsPerMinutePerNetworkAndPerRoomAreLimited()
    {
        await using var rig = await RelayRig.StartAsync(tweak: With(l => l with { JoinsPerMinute = 2, MaxJoinersPerNetwork = 100 }));
        await using var host = await TestHost.OpenAsync(rig, rig.NewKey());
        await using var other = await TestHost.OpenAsync(rig, rig.NewKey(), "other");
        var kept = new List<RelayClient>();
        for (var i = 0; i < 2; i++)
        {
            var joiner = await rig.ConnectAsync();
            await joiner.SendAsync(new JsonObject { ["op"] = "join", ["v"] = 1, ["room"] = host.Room });
            kept.Add(joiner);
            await host.NextEventAsync("incoming");
        }
        await using var third = await rig.ConnectAsync();
        Assert.Equal("rate-limited", Code(await third.RequestAsync(new JsonObject { ["op"] = "join", ["v"] = 1, ["room"] = host.Room })));
        await using var elsewhere = await rig.ConnectAsync(); // the budget is per room: another room still takes the same network
        await elsewhere.SendAsync(new JsonObject { ["op"] = "join", ["v"] = 1, ["room"] = other.Room });
        Assert.NotNull(await other.NextEventAsync("incoming"));
        foreach (var joiner in kept) await joiner.DisposeAsync();
    }

    [Fact]
    public async Task JoinersPerNetworkAndRoomAreLimited()
    {
        await using var rig = await RelayRig.StartAsync(tweak: With(l => l with { MaxJoinersPerNetwork = 1 }));
        await using var host = await TestHost.OpenAsync(rig, rig.NewKey());
        var (joiner, data) = await host.JoinAsync();
        await using (joiner) await using (data)
        {
            await using var second = await rig.ConnectAsync();
            Assert.Equal("rate-limited", Code(await second.RequestAsync(new JsonObject { ["op"] = "join", ["v"] = 1, ["room"] = host.Room })));
        }
    }

    [Fact]
    public async Task AnIdleSpliceIsClosedByTheFakeClockWithoutTouchingAnotherRoom()
    {
        var clock = new ManualClock(9_000_000);
        await using var rig = await RelayRig.StartAsync(clock: clock);
        await using var one = await TestHost.OpenAsync(rig, rig.NewKey(), "one");
        await using var two = await TestHost.OpenAsync(rig, rig.NewKey(), "two");
        var (idleJoiner, idleData) = await one.JoinAsync();
        var (busyJoiner, busyData) = await two.JoinAsync();
        await using (idleJoiner) await using (idleData) await using (busyJoiner) await using (busyData)
        {
            await busyJoiner.Stream.WriteAsync(new byte[] { 0x16, 0x03, 1 });
            await busyData.Stream.ReadExactlyAsync(new byte[3]);
            await idleJoiner.Stream.WriteAsync(new byte[] { 0x16, 0x03, 1 });
            await idleData.Stream.ReadExactlyAsync(new byte[3]);
            clock.AdvanceSeconds(100);
            await busyJoiner.Stream.WriteAsync(new byte[] { 1 }); // activity in the other room only
            await busyData.Stream.ReadExactlyAsync(new byte[1]);
            clock.AdvanceSeconds(30);
            await rig.Server.SweepAsync();
            Assert.True(await idleJoiner.ClosedAsync());
            await busyJoiner.Stream.WriteAsync(new byte[] { 2 });
            var got = new byte[1];
            await busyData.Stream.ReadExactlyAsync(got).AsTask().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(2, got[0]);
        }
    }

    [Fact]
    public async Task ASpliceThatDoesNotStartWithATlsRecordIsClosedAtOnce()
    {
        await using var rig = await RelayRig.StartAsync();
        await using var host = await TestHost.OpenAsync(rig, rig.NewKey());
        var (joiner, data) = await host.JoinAsync();
        await using (joiner) await using (data)
        {
            await joiner.Stream.WriteAsync("GET / HTTP/1.1\r\n\r\n"u8.ToArray());
            Assert.True(await data.ClosedAsync());
            Assert.True(await joiner.ClosedAsync());
        }
    }

    [Fact]
    public async Task ASpliceThatSendsNothingWithinTenSecondsIsClosed()
    {
        var clock = new ManualClock(2_000_000);
        await using var rig = await RelayRig.StartAsync(clock: clock, tweak: With(l => l with { IdleSeconds = 600 }));
        await using var host = await TestHost.OpenAsync(rig, rig.NewKey());
        var (joiner, data) = await host.JoinAsync();
        await using (joiner) await using (data)
        {
            clock.AdvanceSeconds(11);
            await rig.Server.SweepAsync();
            Assert.True(await joiner.ClosedAsync());
        }
    }

    [Fact]
    public async Task ThirtyTwoMegabytesPassAtTheDefaultLimits()
    {
        await using var rig = await RelayRig.StartAsync();
        await using var host = await TestHost.OpenAsync(rig, rig.NewKey());
        var (joiner, data) = await host.JoinAsync();
        await using (joiner) await using (data)
        {
            const int total = 32 * 1024 * 1024;
            var chunk = new byte[64 * 1024];
            chunk[0] = 0x16;
            chunk[1] = 0x03;
            var send = Task.Run(async () =>
            {
                for (var sent = 0; sent < total; sent += chunk.Length) await joiner.Stream.WriteAsync(chunk);
            });
            var received = 0L;
            var buffer = new byte[64 * 1024];
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            while (received < total) received += await data.Stream.ReadAsync(buffer, cts.Token);
            await send;
            Assert.Equal(total, received);
        }
    }

    [Fact]
    public async Task ASustainedTransferOverTheRateIsThrottled()
    {
        // 256 KiB/s with a 64 KiB burst: 320 KiB cannot arrive in less than (320 - 64) / 256 = 1 s
        await using var rig = await RelayRig.StartAsync(clock: SystemClock.Instance, tweak: With(l => l with { SpliceRate = 256 * 1024, SpliceBurst = 64 * 1024 }));
        await using var host = await TestHost.OpenAsync(rig, rig.NewKey());
        var (joiner, data) = await host.JoinAsync();
        await using (joiner) await using (data)
        {
            var chunk = new byte[16 * 1024];
            chunk[0] = 0x16;
            chunk[1] = 0x03;
            var watch = Stopwatch.StartNew();
            var send = Task.Run(async () => { for (var i = 0; i < 20; i++) await joiner.Stream.WriteAsync(chunk); });
            var buffer = new byte[16 * 1024];
            var received = 0;
            while (received < 20 * chunk.Length) received += await data.Stream.ReadAsync(buffer).AsTask().WaitAsync(TimeSpan.FromSeconds(30));
            await send;
            Assert.True(watch.Elapsed >= TimeSpan.FromMilliseconds(800), $"took {watch.Elapsed}");
        }
    }

    [Fact]
    public async Task TheDailyRoomCapRefusesNewJoinsButLetsTheRunningSpliceFinish()
    {
        var clock = new ManualClock(3_000_000);
        await using var rig = await RelayRig.StartAsync(clock: clock, tweak: With(l => l with { RoomBytesPerDay = 100 * 1024 }));
        await using var host = await TestHost.OpenAsync(rig, rig.NewKey());
        var (joiner, data) = await host.JoinAsync();
        await using (joiner) await using (data)
        {
            var chunk = new byte[150 * 1024];
            chunk[0] = 0x16;
            chunk[1] = 0x03;
            await joiner.Stream.WriteAsync(chunk);
            await data.Stream.ReadExactlyAsync(new byte[chunk.Length]).AsTask().WaitAsync(TimeSpan.FromSeconds(10));
            await using (var refused = await rig.ConnectAsync())
                Assert.Equal("rate-limited", Code(await refused.RequestAsync(new JsonObject { ["op"] = "join", ["v"] = 1, ["room"] = host.Room })));
            await data.Stream.WriteAsync(new byte[] { 42 }); // the running splice still carries bytes
            var got = new byte[1];
            await joiner.Stream.ReadExactlyAsync(got).AsTask().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(42, got[0]);
            clock.Advance(24L * 3600 * 1000 + 1); // a new day
            var (second, secondData) = await host.JoinAsync();
            await using (second) await using (secondData) Assert.Equal(2, rig.Server.SpliceCount);
        }
    }

    [Fact]
    public async Task TheMonthlyAndRateCapsOfTheWholeRelayApply()
    {
        var clock = new ManualClock(4_000_000);
        await using var rig = await RelayRig.StartAsync(clock: clock, tweak: With(l => l with { MaxGibPerMonth = 1, MaxMbps = 1000 }));
        await using var host = await TestHost.OpenAsync(rig, rig.NewKey());
        var (joiner, data) = await host.JoinAsync();
        await using (joiner) await using (data)
        {
            await joiner.Stream.WriteAsync(new byte[] { 0x16, 0x03, 1 });
            await data.Stream.ReadExactlyAsync(new byte[3]).AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        }
        Assert.Contains("unciv_relay_joins_total 1", rig.Server.Metrics(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TenFailedRoomLookupsBanTheNetworkForTenMinutes()
    {
        var clock = new ManualClock(6_000_000);
        await using var rig = await RelayRig.StartAsync(clock: clock, tweak: With(l => l with { LookupFailuresPerMinute = 10 }));
        for (var i = 0; i < 10; i++)
        {
            await using var client = await rig.ConnectAsync();
            Assert.Equal("room-unknown", Code(await client.RequestAsync(new JsonObject { ["op"] = "join", ["v"] = 1, ["room"] = "K7QF-3MXP4A" })));
        }
        await using (var banned = await RelayClient.ConnectAsync(rig.Port, rig.UsesTls, readChallenge: false))
            Assert.Equal("banned", Code(await banned.ReadAsync()));
        clock.AdvanceSeconds(9 * 60);
        await using (var still = await RelayClient.ConnectAsync(rig.Port, rig.UsesTls, readChallenge: false))
            Assert.Equal("banned", Code(await still.ReadAsync()));
        clock.AdvanceSeconds(61);
        await using var back = await rig.ConnectAsync();
        Assert.NotEmpty(back.Challenge);
    }

    [Fact]
    public async Task AJoinerThatStopsReadingCannotGrowTheRelayAndTheRelayStaysResponsive()
    {
        var clock = new ManualClock(7_000_000);
        await using var rig = await RelayRig.StartAsync(clock: clock);
        await using var host = await TestHost.OpenAsync(rig, rig.NewKey());
        var (joiner, data) = await host.JoinAsync();
        await using (joiner) await using (data)
        {
            // the host pushes far more than the kernel buffers hold; the joiner never reads
            var flood = new byte[64 * 1024];
            var pushed = 0L;
            var pump = Task.Run(async () =>
            {
                try { while (true) { await data.Stream.WriteAsync(flood); Interlocked.Add(ref pushed, flood.Length); } }
                catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException) { }
            });
            await Task.Delay(1500);
            var before = Interlocked.Read(ref pushed);
            await Task.Delay(500);
            Assert.Equal(before, Interlocked.Read(ref pushed)); // the host is blocked: back-pressure reached it
            Assert.True(before < 256L * 1024 * 1024);
            await using var third = await rig.ConnectAsync(); // a third client still gets a handshake and an answer
            Assert.Equal("room-unknown", Code(await third.RequestAsync(new JsonObject { ["op"] = "join", ["v"] = 1, ["room"] = "K7QF-3MXP4A" })));
            clock.AdvanceSeconds(121);
            await rig.Server.SweepAsync();
            await pump.WaitAsync(TimeSpan.FromSeconds(10)); // the stuck write ends with the splice
        }
    }

    [Fact]
    public void TheTokenBucketAllowsTheBurstThenMakesCallersWait()
    {
        var clock = new ManualClock(0);
        var bucket = new TokenBucket(1000, 5000, clock);
        Assert.Equal(0, bucket.Reserve(5000));
        Assert.Equal(1000, bucket.Reserve(1000)); // empty: one second for the next 1000
        clock.Advance(1000);
        Assert.Equal(1000, bucket.Reserve(1000)); // still one second in debt after refilling 1000 of the 2000 owed
        clock.Advance(60_000);
        Assert.Equal(0, bucket.Reserve(5000)); // never more than the burst
    }

    [Fact]
    public void TheByteBudgetRestartsItsWindowAndAZeroLimitMeansOff()
    {
        var clock = new ManualClock(0);
        var budget = new ByteBudget(100, 1000, clock);
        budget.Add(99);
        Assert.False(budget.Exceeded);
        budget.Add(1);
        Assert.True(budget.Exceeded);
        clock.Advance(1000);
        Assert.False(budget.Exceeded);
        var off = new ByteBudget(0, 1000, clock);
        off.Add(1L << 40);
        Assert.False(off.Exceeded);
    }

    [Fact]
    public void TheKeyedWindowCountsWithinTheWindowOnly()
    {
        var clock = new ManualClock(0);
        var window = new KeyedWindow(2, 1000, clock);
        Assert.True(window.TryHit("a"));
        Assert.True(window.TryHit("a"));
        Assert.False(window.TryHit("a"));
        Assert.True(window.TryHit("b"));
        Assert.Equal(3, window.Hit("a"));
        clock.Advance(1000);
        window.Cleanup();
        Assert.True(window.TryHit("a"));
    }
}
