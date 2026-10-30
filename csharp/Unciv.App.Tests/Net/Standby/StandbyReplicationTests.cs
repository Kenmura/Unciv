using Unciv.Client.Net.Session;
using Unciv.Client.Net.Transport;
using Unciv.Commands.Log;
using Unciv.Commands.Turn;
using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using Unciv.Net;
using Unciv.Net.Identity;
using Unciv.Net.Persistence;
using Unciv.Net.Standby;
using Unciv.Net.Turns;
using Unciv.Server;
using Unciv.Server.Transport;
using RulesetClass = Unciv.Models.Ruleset.Ruleset;

namespace Unciv.Core.Tests.Net.Standby;

/// <summary>A scripted successor: a real authenticated session of the designated seat that stores what the host replicates in a <see cref="StandbyStore"/> and acks like the client does.</summary>
internal sealed class TestSuccessor : IAsyncDisposable
{
    private readonly ITransport transport;
    private readonly List<Message> received = [];
    private readonly CancellationTokenSource stop = new();
    private readonly Task reader;
    private long lastAck = long.MinValue;

    private TestSuccessor(ITransport transport, string root, string gameId)
    {
        this.transport = transport;
        Root = root;
        GameId = gameId;
        reader = ReadAsync();
    }

    public string Root { get; }
    public string GameId { get; }
    public StandbyStore? Store { get; set; }
    public volatile bool HoldAcks;

    public static async Task<TestSuccessor> JoinAsync(InProcessHost host, ClientIdentity identity, string root, string gameId)
    {
        var tcp = await TcpTlsClientTransport.ConnectAsync("127.0.0.1", host.Port!.Value, expectedFingerprint: host.Fingerprint);
        await tcp.SendAsync(ProtocolCodec.Encode(new Hello { Protocol = ProtocolVersion.Current, ClientVersion = "test", RulesetHash = "", ModList = [] }));
        Welcome? welcome = null;
        while (welcome == null) welcome = ProtocolCodec.Decode((await tcp.ReceiveAsync())!) as Welcome;
        await tcp.SendAsync(ProtocolCodec.Encode(new Join { PlayerKey = identity.PlayerKey, DisplayName = "x" }));
        await tcp.SendAsync(ProtocolCodec.Encode(new Auth { Proof = identity.Sign(welcome.HostKeyFingerprint, welcome.SessionId, welcome.Challenge) }));
        var joined = false;
        while (!joined) joined = ProtocolCodec.Decode((await tcp.ReceiveAsync())!) is JoinOk;
        return new TestSuccessor(tcp, root, gameId);
    }

    public List<Message> All()
    {
        lock (received) return [.. received];
    }

    private string Frames() => string.Join(",", All().Select(it => it switch
    {
        StandbyEntry entry => "e" + entry.Seq,
        StandbyStatus status => "status:" + status.State + "/" + status.LagEntries,
        ViewDelta => "d",
        StandbySnapshot part => "s" + part.Seq + (part.Reset ? "r" : "a"),
        _ => it.GetType().Name,
    }));

    public List<T> Of<T>() where T : Message => All().OfType<T>().ToList();

    private readonly SemaphoreSlim sendGate = new(1, 1); // the TLS stream takes one write at a time; the reader acks while the test sends

    public async Task SendAsync(Message message)
    {
        await sendGate.WaitAsync();
        try
        {
            await transport.SendAsync(ProtocolCodec.Encode(message));
        }
        finally
        {
            sendGate.Release();
        }
    }

    public async Task SubscribeAsync()
    {
        Store?.Dispose();
        Store = StandbyStore.Open(Root, GameId);
        await SendAsync(new StandbySubscribe { GameId = GameId, HaveSeq = Store.HaveSeq, HaveLineSha256 = Store.HaveLineSha256 });
    }

    public async Task<T> WaitAsync<T>(Func<T, bool>? where = null) where T : Message
    {
        for (var attempt = 0; attempt < 750; attempt++)
        {
            var hit = Of<T>().FirstOrDefault(it => where == null || where(it));
            if (hit != null) return hit;
            await Task.Delay(20);
        }
        throw new TimeoutException($"No {typeof(T).Name}; got: {Frames()}; reader: {Failure}");
    }

    public async Task WaitForSeqAsync(long seq)
    {
        for (var attempt = 0; attempt < 750; attempt++)
        {
            if (Store != null && Store.HaveSeq >= seq) return;
            await Task.Delay(20);
        }
        throw new TimeoutException($"Standby store at {Store?.HaveSeq}, wanted {seq}; reader: {Failure}; got: {Frames()}");
    }

    private async Task ReadAsync()
    {
        try
        {
            while (true)
            {
                var frame = await transport.ReceiveAsync(stop.Token);
                if (frame == null) return;
                var message = ProtocolCodec.Decode(frame);
                lock (received) received.Add(message);
                if (Store == null) continue;
                switch (message)
                {
                    case StandbyMeta meta:
                        Assert.Equal(StandbyApply.Applied, Store.Apply(meta));
                        break;
                    case StandbySnapshot part:
                        var outcome = Store.Apply(part);
                        Assert.NotEqual(StandbyApply.Rejected, outcome);
                        if (part.Reset && outcome == StandbyApply.Applied) await AckAsync(true);
                        break;
                    case StandbyEntry entry:
                        Assert.Equal(StandbyApply.Applied, Store.Apply(entry));
                        await AckAsync(false);
                        break;
                }
            }
        }
        catch (Exception e) when (!stop.IsCancellationRequested)
        {
            Failure = e;
        }
        catch (Exception) { /* closed */ }
    }

    /// <summary>What ended the reader early (a rejected frame, a broken transport), shown by the wait timeouts.</summary>
    public Exception? Failure { get; private set; }

    private async Task AckAsync(bool force)
    {
        if (HoldAcks || (!force && lastAck == Store!.HaveSeq)) return;
        lastAck = Store!.HaveSeq;
        await SendAsync(new StandbyAck { Seq = lastAck });
    }

    /// <summary>Waits until no frame arrived for a while (the host stopped sending).</summary>
    public async Task WaitUntilQuietAsync()
    {
        var seen = -1;
        var quiet = 0;
        for (var attempt = 0; attempt < 750 && quiet < 15; attempt++)
        {
            await Task.Delay(20);
            var count = All().Count;
            quiet = count == seen ? quiet + 1 : 0;
            seen = count;
        }
    }

    /// <summary>Waits until the store holds a log line (its copy can then be reopened and continued).</summary>
    public async Task WaitDurableAsync()
    {
        for (var attempt = 0; attempt < 750; attempt++)
        {
            if (Store?.HaveLineSha256 != null) return;
            await Task.Delay(20);
        }
        throw new TimeoutException("The standby store holds no log line");
    }

    /// <summary>Acks what the store holds now (after <see cref="HoldAcks"/>).</summary>
    public Task AckNowAsync()
    {
        lastAck = Store!.HaveSeq;
        return SendAsync(new StandbyAck { Seq = lastAck });
    }

    private bool disposed;

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        await stop.CancelAsync();
        await transport.DisposeAsync();
        await reader;
        Store?.Dispose();
        stop.Dispose();
    }
}

/// <summary>T272 acceptance A3: the standby stream over a running game on TCP+TLS: replay equality at every checkpoint, resume, lag and resync, and the designated-only rules.</summary>
[Collection("RulesetCache")]
public sealed class StandbyReplicationTests : IDisposable
{
    private const long Seed = 11L;
    private const long StartClock = 1000L;
    private static readonly string Fp = HostKey.Fingerprint(new byte[] { 3, 1, 4 });
    private readonly RulesetClass ruleset;
    private readonly string root = Directory.CreateTempSubdirectory("unciv-replication-").FullName;
    private readonly ClientIdentity babylon = ClientIdentity.Generate();
    private readonly ClientIdentity greece = ClientIdentity.Generate();
    private readonly ClientIdentity rome = ClientIdentity.Generate();

    public StandbyReplicationTests()
    {
        var jsons = Path.Combine(RepoRoot(), "android", "assets", "jsons");
        RulesetCache.LoadRulesets(new DirectoryRulesetFileSource(jsons, Path.Combine(jsons, "no mods here")), noMods: true);
        ruleset = RulesetCache.GetVanillaRuleset();
    }

    public void Dispose()
    {
        RulesetCache.Clear();
        babylon.Dispose();
        greece.Dispose();
        rome.Dispose();
        foreach (var cert in certificates) cert.Dispose();
        try { Directory.Delete(root, recursive: true); } catch (IOException) { }
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "android", "assets", "jsons"))) return dir.FullName;
        throw new DirectoryNotFoundException("android/assets/jsons not found above " + AppContext.BaseDirectory);
    }

    private InProcessHost StartHost(string name, IClock? clock = null, StoreOptions? store = null, StandbyLimits? limits = null, bool humanRome = false)
    {
        var setup = new GameSetupInfo(
            new GameParameters
            {
                BaseRuleset = ruleset.Name,
                NumberOfCityStates = 0,
                VictoryTypes = ruleset.Victories.Keys.ToList(),
                Players = [new("Babylon", PlayerType.Human), new("Greece", PlayerType.Human), new("Rome", humanRome ? PlayerType.Human : PlayerType.AI)],
            },
            new MapParameters { Type = MapType.Pangaea, Shape = MapShape.Rectangular, Seed = 3, MapSize = MapSize.Tiny });
        var game = GameStarter.StartNewGame(setup, Seed, StartClock);
        game.StartSimultaneousTurns();
        var humans = new Dictionary<string, SlotBinding>
        {
            ["Babylon"] = new SlotBinding { PlayerKey = babylon.PlayerKey, DisplayName = "Host" },
            ["Greece"] = new SlotBinding { PlayerKey = greece.PlayerKey, DisplayName = "Friend" },
        };
        if (humanRome) humans["Rome"] = new SlotBinding { PlayerKey = rome.PlayerKey, DisplayName = "Third" };
        var host = InProcessHost.Start(game, setup, Seed, StartClock, Path.Combine(root, name), "Babylon", humans,
            new InProcessHostOptions { TickInterval = null, Clock = clock, Store = store ?? new StoreOptions { SnapshotEveryTurns = 2 } });
        host.Host.Standby.Prober = new FakeProber { Answer = (_, _) => Fp };
        host.Host.Standby.Limits = limits ?? new StandbyLimits { MinSubscribeMillis = 0 };
        return host;
    }

    private readonly List<System.Security.Cryptography.X509Certificates.X509Certificate2> certificates = [];

    private void Listen(InProcessHost host)
    {
        var cert = HostCertificate.Generate();
        certificates.Add(cert);
        host.Listen(cert, 0);
    }

    private static async Task PlayTurnsAsync(InProcessHost host, int turns)
    {
        for (var turn = 0; turn < turns; turn++)
        {
            await host.Host.EnqueueAsync(() =>
            {
                foreach (var civ in host.Host.Game.Civilizations.Where(it => it.IsHuman()))
                    Assert.True(host.Host.Session.Submit(civ.CivId, new EndTurn()).Accepted);
                host.Host.Tick();
                return Task.CompletedTask;
            });
        }
    }

    private async Task<TestSuccessor> DesignateAndAcceptAsync(InProcessHost host, string dataRoot)
    {
        if (host.Port == null) Listen(host);
        Assert.Null(await host.Host.DesignateBackupAsync("Greece", babylon.PlayerKey));
        var successor = await TestSuccessor.JoinAsync(host, greece, dataRoot, host.Host.Game.GameId);
        await successor.WaitAsync<StandbyOffer>();
        await successor.SendAsync(new StandbyAccept { Endpoints = ["friend.example"], Port = 47821, Fingerprint = Fp });
        var status = await successor.WaitAsync<StandbyStatus>(it => it.State == StandbyStates.Ready);
        Assert.Equal(host.Host.Game.GameId, status.GameId);
        return successor;
    }

    private static async Task WaitForHostStateAsync(InProcessHost host, string state)
    {
        for (var attempt = 0; attempt < 750 && host.Host.Standby.State != state; attempt++) await Task.Delay(20);
        Assert.Equal(state, host.Host.Standby.State);
    }

    private static async Task<long> HostSeqAsync(InProcessHost host)
    {
        long seq = 0;
        await host.Host.EnqueueAsync(() =>
        {
            seq = host.Host.Store!.LastSeq;
            return Task.CompletedTask;
        });
        return seq;
    }

    private static ulong HashOfDirectory(string directory, out RecoveryReport report)
    {
        using var opened = FileGameStore.Open(directory);
        report = opened.Report;
        return ReplayVerifier.FullHash(opened.Session.Game);
    }

    [Fact]
    public async Task Stream_ReplaysToTheHostsHash_AtEveryCheckpoint_AndTakesRefreshedSnapshots()
    {
        await using var host = StartHost("g1");
        var dataRoot = Path.Combine(root, "successor1");
        await using var successor = await DesignateAndAcceptAsync(host, dataRoot);
        await using var hostPlayer = new Wire(await host.ConnectHostPlayerAsync());
        await successor.SubscribeAsync();
        await PlayTurnsAsync(host, 5);

        var last = await HostSeqAsync(host);
        await successor.WaitForSeqAsync(last);
        await WaitForHostStateAsync(host, StandbyStates.Ready); // the last status the host sent is ready again (the first one only meant the probe passed)

        // Snapshot at assignment (reset), then one for each later host snapshot (cadence 2 turns), then the verbatim log.
        Assert.True(successor.Of<StandbySnapshot>().First().Reset);
        Assert.True(successor.Of<StandbySnapshot>().Count(it => it.Reset == false && it.Part == 0) >= 1);
        Assert.Equal(StandbyStates.Ready, host.Host.Standby.State);

        var hostDir = ((FileGameStore)host.Host.Store!).DirectoryPath;
        var standbyDir = StandbyStore.DirectoryFor(dataRoot, host.Host.Game.GameId)!;
        var hostLines = File.ReadAllText(Path.Combine(hostDir, "commands.log")).Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1).ToList();
        var standbyLines = File.ReadAllText(Path.Combine(standbyDir, "commands.log")).Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1).ToList();
        Assert.NotEmpty(standbyLines);
        Assert.Equal(standbyLines, hostLines.TakeLast(standbyLines.Count)); // verbatim lines

        // The successor's directory is a game directory: it replays, every checkpoint verifies, and it ends at the host's state.
        successor.Store!.Dispose();
        var hash = HashOfDirectory(standbyDir, out var report);
        Assert.Equal(RecoveryOutcome.Recovered, report.Outcome);
        Assert.True(report.CheckpointsVerified >= 2);
        Assert.Equal(last, report.LastSeq);
        Assert.Equal(ReplayVerifier.FullHash(host.Host.Game), hash);

        // Nobody but the designated session got a Standby frame (the host's own player never did).
        Assert.DoesNotContain(hostPlayer.All(), it => it is StandbyEntry or StandbySnapshot or StandbyMeta);
    }

    [Fact]
    public async Task Resume_AfterReconnect_ContinuesTheLogWithoutASnapshot_AndADivergedStoreRestarts()
    {
        await using var host = StartHost("g2");
        var dataRoot = Path.Combine(root, "successor2");
        var first = await DesignateAndAcceptAsync(host, dataRoot);
        await first.SubscribeAsync();
        await PlayTurnsAsync(host, 2);
        await first.WaitForSeqAsync(await HostSeqAsync(host));
        await first.WaitDurableAsync(); // a reset snapshot alone is not a copy that can be continued: its log line follows it
        await first.DisposeAsync();

        await PlayTurnsAsync(host, 2);
        var last = await HostSeqAsync(host);
        await using var second = await TestSuccessor.JoinAsync(host, greece, dataRoot, host.Host.Game.GameId);
        await second.SubscribeAsync();
        await second.WaitForSeqAsync(last);
        Assert.DoesNotContain(second.All(), it => it is StandbySnapshot { Reset: true });
        Assert.NotNull(second.Of<StandbyMeta>().Single());
        Assert.Null(second.Of<StandbyMeta>().Single().Header);
        Assert.Equal(ReplayVerifier.FullHash(host.Host.Game), HashAfterClose(second, host));

        // A store whose last line is not the host's line at that seq (a diverged history) is not continued: the host restarts it from a snapshot.
        await second.DisposeAsync();
        await using var third = await TestSuccessor.JoinAsync(host, greece, dataRoot, host.Host.Game.GameId);
        third.Store = null;
        await third.SendAsync(new StandbySubscribe { GameId = host.Host.Game.GameId, HaveSeq = last, HaveLineSha256 = new string('a', 64) });
        Assert.NotNull((await third.WaitAsync<StandbyMeta>()).Header);
        Assert.True((await third.WaitAsync<StandbySnapshot>()).Reset);
    }

    private ulong HashAfterClose(TestSuccessor successor, InProcessHost host)
    {
        successor.Store!.Dispose();
        return HashOfDirectory(StandbyStore.DirectoryFor(successor.Root, host.Host.Game.GameId)!, out _);
    }

    [Fact]
    public async Task OnlyTheDesignatedAcceptedSession_GetsOrCanDriveTheStream()
    {
        await using var host = StartHost("g3");
        Listen(host);
        var hostPlayer = new Wire(await host.ConnectHostPlayerAsync());
        await using var _ = hostPlayer;
        var gameId = host.Host.Game.GameId;

        // Nobody designated, then designated but not yet consented: a subscribe is refused with the one generic code.
        await hostPlayer.SendAsync(new StandbySubscribe { GameId = gameId, HaveSeq = -1 });
        await hostPlayer.SendAsync(new StandbyAck { Seq = 0 });
        Assert.Equal(2, (await WaitErrorsAsync(hostPlayer, 2)).Count(it => it.Code == ErrorCodes.NotDesignated));

        Assert.Null(await host.Host.DesignateBackupAsync("Greece", babylon.PlayerKey));
        await using var friend = await TestSuccessor.JoinAsync(host, greece, Path.Combine(root, "successor3"), gameId);
        await friend.WaitAsync<StandbyOffer>();
        await friend.SendAsync(new StandbySubscribe { GameId = gameId, HaveSeq = -1 });
        await friend.WaitAsync<Error>(it => it.Code == ErrorCodes.NotDesignated);
        await Task.Delay(100);
        Assert.Empty(friend.Of<StandbyMeta>());

        // Consent, subscribe: now the stream flows, to this session only. The host's own session still gets nothing and cannot subscribe.
        await friend.SendAsync(new StandbyAccept { Endpoints = ["friend.example"], Port = 47821, Fingerprint = Fp });
        await friend.WaitAsync<StandbyStatus>(it => it.State == StandbyStates.Ready);
        await friend.SubscribeAsync();
        await friend.WaitAsync<StandbySnapshot>();
        await hostPlayer.SendAsync(new StandbySubscribe { GameId = gameId, HaveSeq = -1 });
        await WaitErrorsAsync(hostPlayer, 3);
        Assert.DoesNotContain(hostPlayer.All(), it => it is StandbyEntry or StandbySnapshot or StandbyMeta);

        // A wrong game id is refused without starting anything new.
        await friend.SendAsync(new StandbySubscribe { GameId = "another-game", HaveSeq = -1 });
        await friend.WaitAsync<Error>(it => it.Code == ErrorCodes.InvalidMessage);

        // Revoke: the stream ends, and later commands reach nobody.
        await friend.WaitForSeqAsync(await HostSeqAsync(host));
        await host.Host.RevokeBackupAsync();
        await friend.WaitAsync<StandbyRevoke>();
        var before = friend.All().Count(it => it is StandbyEntry or StandbySnapshot or StandbyMeta);
        await PlayTurnsAsync(host, 2);
        await Task.Delay(300);
        Assert.Equal(before, friend.All().Count(it => it is StandbyEntry or StandbySnapshot or StandbyMeta));
        Assert.DoesNotContain(hostPlayer.All(), it => it is StandbyEntry or StandbySnapshot or StandbyMeta);
    }

    private static async Task<List<Error>> WaitErrorsAsync(Wire wire, int count)
    {
        for (var attempt = 0; attempt < 750; attempt++)
        {
            var errors = wire.Of<Error>();
            if (errors.Count >= count) return errors;
            await Task.Delay(20);
        }
        throw new TimeoutException("Errors: " + wire.Of<Error>().Count);
    }

    [Fact]
    public async Task AckGoingBackwardsOrBeyondTheLog_ClosesTheSubscription()
    {
        await using var host = StartHost("g4");
        var dataRoot = Path.Combine(root, "successor4");
        await using var successor = await DesignateAndAcceptAsync(host, dataRoot);
        await successor.SubscribeAsync();
        await PlayTurnsAsync(host, 1);
        var last = await HostSeqAsync(host);
        await successor.WaitForSeqAsync(last);

        await successor.SendAsync(new StandbyAck { Seq = last + 1000 });
        Assert.Equal(ErrorCodes.StandbyViolation, (await successor.WaitAsync<Error>(it => it.Code == ErrorCodes.StandbyViolation)).Code);
        var frames = successor.All().Count(it => it is StandbyEntry or StandbySnapshot);
        await PlayTurnsAsync(host, 1);
        await Task.Delay(300);
        Assert.Equal(frames, successor.All().Count(it => it is StandbyEntry or StandbySnapshot));

        // Resubscribing continues from the store; an ack lower than an earlier one then closes it again.
        await successor.SubscribeAsync();
        await successor.WaitForSeqAsync(await HostSeqAsync(host));
        await successor.SendAsync(new StandbyAck { Seq = successor.Store!.HaveSeq });
        await successor.SendAsync(new StandbyAck { Seq = 1 });
        await successor.WaitAsync<Error>(it => it.Code == ErrorCodes.StandbyViolation && successor.Of<Error>().Count(x => x.Code == ErrorCodes.StandbyViolation) >= 2);
    }

    [Fact]
    public async Task AStreamThatEndsOnAViolation_IsReportedAsStopped_UntilTheSuccessorSubscribesAgain()
    {
        await using var host = StartHost("g4b");
        await using var successor = await DesignateAndAcceptAsync(host, Path.Combine(root, "successor4b"));
        await successor.SubscribeAsync();
        await PlayTurnsAsync(host, 1);
        await successor.WaitForSeqAsync(await HostSeqAsync(host));
        await successor.WaitAsync<StandbyStatus>(it => it.State == StandbyStates.Ready);

        await successor.SendAsync(new StandbyAck { Seq = long.MaxValue / 2 });
        Assert.Equal("ack", (await successor.WaitAsync<Error>(it => it.Code == ErrorCodes.StandbyViolation)).Detail);
        await successor.WaitAsync<StandbyStatus>(it => it.State == StandbyStates.Stopped);
        Assert.Equal(StandbyStates.Stopped, host.Host.Standby.State); // not the probe's "ready": nothing is replicated any more
        Assert.Equal(StandbyStates.Stopped, host.Host.Standby.Announcement(0, []).Status);

        await successor.SubscribeAsync();
        await successor.WaitAsync<StandbyStatus>(it => successor.Of<StandbyStatus>().Last().State is StandbyStates.Syncing or StandbyStates.Ready);
        Assert.NotEqual(StandbyStates.Stopped, host.Host.Standby.State);
    }

    [Fact]
    public async Task SlowSuccessor_IsMarkedLagging_ThenResyncsFromASnapshotOnceItCatchesUp()
    {
        var clock = new ManualClock(StartClock);
        await using var host = StartHost("g5", clock, limits: new StandbyLimits { MaxLagEntries = 12, MinSubscribeMillis = 0 });
        var dataRoot = Path.Combine(root, "successor5");
        await using var successor = await DesignateAndAcceptAsync(host, dataRoot);
        successor.HoldAcks = true;
        await successor.SubscribeAsync();
        await successor.WaitAsync<StandbySnapshot>();
        await successor.WaitForSeqAsync(0); // installed (a client acks after applying)
        await successor.AckNowAsync(); // the snapshot's ack (the store holds seq 0 and no entries yet)
        await PlayTurnsAsync(host, 1); // a turn is about 9 entries: sent, not acked, still within the limit
        await successor.WaitForSeqAsync(await HostSeqAsync(host));
        await PlayTurnsAsync(host, 1); // and now more than the limit is unacked

        await successor.WaitAsync<StandbyStatus>(it => it.State == StandbyStates.Lagging);
        Assert.Equal(StandbyStates.Lagging, host.Host.Standby.State);
        Assert.True(host.Host.Standby.LagEntries > 12);
        var announced = host.Host.Standby.Announcement(0, []);
        Assert.Equal(StandbyStates.Lagging, announced.Status);
        Assert.NotNull(announced.Successor); // still reachable: only the replication is behind

        // It catches up with what was sent: the host drops the rest and restarts it from a snapshot (a second reset, with the header again).
        var resets = successor.Of<StandbySnapshot>().Count(it => it.Reset && it.Part == 0);
        await successor.WaitUntilQuietAsync(); // everything the host sent before it stopped has arrived
        await successor.AckNowAsync();
        var resync = await successor.WaitAsync<StandbySnapshot>(it => it.Reset && it.Part == 0 && successor.Of<StandbySnapshot>().Count(x => x.Reset && x.Part == 0) > resets);
        successor.HoldAcks = false;
        await successor.SendAsync(new StandbyAck { Seq = resync.Seq }); // the successor's ack of the snapshot it installed
        await successor.WaitForSeqAsync(await HostSeqAsync(host));
        await successor.AckNowAsync();
        await PlayTurnsAsync(host, 1);
        var last = await HostSeqAsync(host);
        await successor.WaitForSeqAsync(last);
        await successor.WaitAsync<StandbyStatus>(it => it.State == StandbyStates.Ready && successor.Of<StandbyStatus>().Last().State == StandbyStates.Ready);

        successor.Store!.Dispose();
        Assert.Equal(ReplayVerifier.FullHash(host.Host.Game), HashOfDirectory(StandbyStore.DirectoryFor(dataRoot, host.Host.Game.GameId)!, out var report));
        Assert.Equal(RecoveryOutcome.Recovered, report.Outcome);
    }

    [Fact]
    public async Task ASubscribeLoop_IsRefusedWithoutRestartingTheStream()
    {
        var clock = new ManualClock(StartClock);
        await using var host = StartHost("g8", clock, limits: new StandbyLimits { MinSubscribeMillis = 60_000 });
        await using var successor = await DesignateAndAcceptAsync(host, Path.Combine(root, "successor8"));
        await successor.SubscribeAsync();
        await successor.WaitAsync<StandbySnapshot>();
        for (var again = 0; again < 4; again++) await successor.SendAsync(new StandbySubscribe { GameId = host.Host.Game.GameId, HaveSeq = -1 }); // the burst a restarting client may need
        await successor.WaitAsync<StandbyMeta>(it => successor.Of<StandbyMeta>().Count >= 5);
        var metas = successor.Of<StandbyMeta>().Count;
        await successor.SendAsync(new StandbySubscribe { GameId = host.Host.Game.GameId, HaveSeq = -1 });
        await successor.WaitAsync<Error>(it => it.Code == ErrorCodes.StandbyViolation && it.Detail == "subscribe-rate");
        Assert.Equal(metas, successor.Of<StandbyMeta>().Count);
        clock.AdvanceSeconds(61);
        await successor.SendAsync(new StandbySubscribe { GameId = host.Host.Game.GameId, HaveSeq = -1 });
        await successor.WaitAsync<StandbyMeta>(it => successor.Of<StandbyMeta>().Count > metas);
    }

    [Fact]
    public async Task NoAckForThirtySeconds_MarksTheStandbyLagging()
    {
        var clock = new ManualClock(StartClock);
        await using var host = StartHost("g6", clock);
        await using var successor = await DesignateAndAcceptAsync(host, Path.Combine(root, "successor6"));
        successor.HoldAcks = true;
        await successor.SubscribeAsync();
        await successor.WaitAsync<StandbySnapshot>();
        await successor.WaitForSeqAsync(0);
        await successor.AckNowAsync();
        await PlayTurnsAsync(host, 1);
        await successor.WaitForSeqAsync(await HostSeqAsync(host));
        Assert.NotEqual(StandbyStates.Lagging, host.Host.Standby.State);

        clock.Advance(31_000);
        await host.Host.EnqueueAsync(() =>
        {
            host.Host.Tick();
            return Task.CompletedTask;
        });
        await successor.WaitAsync<StandbyStatus>(it => it.State == StandbyStates.Lagging);
    }

    [Fact]
    public async Task ClientSession_AsSuccessor_StoresTheCopy_AndTheMirrorStaysTheFilteredOne()
    {
        await using var host = StartHost("g7");
        Listen(host);
        var dataRoot = Path.Combine(root, "successor7");
        var options = new ClientSessionOptions
        {
            Identity = greece,
            DisplayName = "Friend",
            RulesetHash = "",
            ExpectedFingerprint = host.Fingerprint,
            StandbyRoot = dataRoot,
            ReconnectDelay = (_, _) => Task.CompletedTask,
        };
        await using var session = new ClientSession(token => TcpTlsClientTransport.ConnectAsync("127.0.0.1", host.Port!.Value, expectedFingerprint: host.Fingerprint, cancellationToken: token), options);
        Assert.Null(await host.Host.DesignateBackupAsync("Greece", babylon.PlayerKey));
        Assert.True((await session.JoinAsync()).Joined);
        await WaitEventAsync<StandbyOffered>(session);
        await session.AcceptStandbyAsync(["friend.example"], 47821, Fp);
        await PlayTurnsAsync(host, 3);

        var last = await HostSeqAsync(host);
        for (var attempt = 0; attempt < 750 && session.StandbyHaveSeq < last; attempt++) await Task.Delay(20);
        Assert.Equal(last, session.StandbyHaveSeq);
        var standbyDir = StandbyStore.DirectoryFor(dataRoot, host.Host.Game.GameId)!;
        Assert.True(File.Exists(Path.Combine(standbyDir, "game.json")));
        Assert.Equal(StandbyStates.Ready, host.Host.Standby.State);

        // The mirror this player renders from is the visibility-filtered snapshot (its own civ), never the replicated full state.
        var mirror = session.Game.Snapshot!;
        Assert.Equal("Greece", mirror.You);
        Assert.True(mirror.Tiles.Count < host.Host.Game.TileMap.Values.Count());
    }

    private static async Task<T> WaitEventAsync<T>(ClientSession session) where T : ClientEvent
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await foreach (var clientEvent in session.Events.ReadAllAsync(cts.Token))
            if (clientEvent is T hit) return hit;
        throw new TimeoutException(typeof(T).Name);
    }
}
