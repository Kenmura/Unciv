using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Unciv.Client.Net.Hosting;
using Unciv.Client.Net.Session;
using Unciv.Client.Net.Transport;
using Unciv.Commands.Log;
using Unciv.Commands.Turn;
using Unciv.Core.Tests.Net.ClientNet;
using Unciv.Json;
using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using Unciv.Net;
using Unciv.Net.Identity;
using Unciv.Net.Persistence;
using Unciv.Net.Standby;
using Unciv.Server;
using Unciv.Server.Transport;
using RulesetClass = Unciv.Models.Ruleset.Ruleset;

namespace Unciv.Core.Tests.Net.Standby;

/// <summary>Collects every event of a session in the background so a test can wait for any of them, in any order.</summary>
internal sealed class EventTap : IAsyncDisposable
{
    private readonly ConcurrentQueue<ClientEvent> seen = new();
    private readonly CancellationTokenSource stop = new();
    private readonly Task reader;

    public EventTap(ClientSession session) => reader = ReadAsync(session);

    private async Task ReadAsync(ClientSession session)
    {
        try { await foreach (var item in session.Events.ReadAllAsync(stop.Token)) seen.Enqueue(item); }
        catch (OperationCanceledException) { /* stopped */ }
    }

    public List<ClientEvent> All() => [.. seen];

    public async Task<T> WaitAsync<T>(Func<T, bool>? where = null, int seconds = 20) where T : ClientEvent
    {
        for (var attempt = 0; attempt < seconds * 50; attempt++)
        {
            var hit = seen.OfType<T>().FirstOrDefault(it => where == null || where(it));
            if (hit != null) return hit;
            await Task.Delay(20);
        }
        throw new TimeoutException($"No {typeof(T).Name}; saw: {string.Join(", ", seen.Select(it => it.GetType().Name))}");
    }

    public async ValueTask DisposeAsync()
    {
        await stop.CancelAsync();
        await reader;
        stop.Dispose();
    }
}

/// <summary>T273 acceptance A4 and A5: the graceful hand-off to the designated successor over TCP+TLS, the promotion rules, and the rolled-back resume.</summary>
[Collection("RulesetCache")]
public sealed class HostHandoffTests : IDisposable
{
    private const long Seed = 11L;
    private const long StartClock = 1000L;
    private readonly RulesetClass ruleset;
    private readonly string root = Directory.CreateTempSubdirectory("unciv-handoff-").FullName;
    private readonly ClientIdentity babylon = ClientIdentity.Generate();
    private readonly ClientIdentity greece = ClientIdentity.Generate();
    private readonly ClientIdentity rome = ClientIdentity.Generate();
    private readonly List<X509Certificate2> certificates = [];
    private readonly List<IAsyncDisposable> toDispose = [];

    public HostHandoffTests()
    {
        var jsons = Path.Combine(RepoRoot(), "android", "assets", "jsons");
        RulesetCache.LoadRulesets(new DirectoryRulesetFileSource(jsons, Path.Combine(jsons, "no mods here")), noMods: true);
        ruleset = RulesetCache.GetVanillaRuleset();
    }

    public void Dispose()
    {
        foreach (var item in Enumerable.Reverse(toDispose)) item.DisposeAsync().AsTask().GetAwaiter().GetResult();
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

    private T Track<T>(T item) where T : IAsyncDisposable
    {
        toDispose.Add(item);
        return item;
    }

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private X509Certificate2 NewCertificate()
    {
        var cert = HostCertificate.Generate();
        certificates.Add(cert);
        return cert;
    }

    private InProcessHost StartHost(string name, string? successorFingerprint = null)
    {
        var setup = new GameSetupInfo(
            new GameParameters
            {
                BaseRuleset = ruleset.Name,
                NumberOfCityStates = 0,
                VictoryTypes = ruleset.Victories.Keys.ToList(),
                Players = [new("Babylon", PlayerType.Human), new("Greece", PlayerType.Human), new("Rome", PlayerType.Human)],
            },
            new MapParameters { Type = MapType.Pangaea, Shape = MapShape.Rectangular, Seed = 3, MapSize = MapSize.Tiny });
        var game = GameStarter.StartNewGame(setup, Seed, StartClock);
        game.StartSimultaneousTurns();
        var humans = new Dictionary<string, SlotBinding>
        {
            ["Babylon"] = new SlotBinding { PlayerKey = babylon.PlayerKey, DisplayName = "Host" },
            ["Greece"] = new SlotBinding { PlayerKey = greece.PlayerKey, DisplayName = "Friend" },
            ["Rome"] = new SlotBinding { PlayerKey = rome.PlayerKey, DisplayName = "Third" },
        };
        var host = Track(InProcessHost.Start(game, setup, Seed, StartClock, Path.Combine(root, name), "Babylon", humans,
            new InProcessHostOptions { TickInterval = null, Store = new StoreOptions { SnapshotEveryTurns = 2 } }));
        host.Host.Standby.Prober = new FakeProber { Answer = (_, _) => successorFingerprint };
        host.Host.Standby.Limits = new StandbyLimits { MinSubscribeMillis = 0 };
        return host;
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

    private static async Task<(long Seq, ulong Hash)> StateAsync(InProcessHost host)
    {
        (long, ulong) state = default;
        await host.Host.EnqueueAsync(() =>
        {
            state = (host.Host.Store!.LastSeq, ReplayVerifier.FullHash(host.Host.Game));
            return Task.CompletedTask;
        });
        return state;
    }

    private static string Hex(ulong hash) => hash.ToString("x16", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>A real client session of one seat over TCP+TLS, a known-hosts list that must never prompt, and (for the successor) the promotion wiring.</summary>
    private sealed class Seat(ClientSession session, EventTap tap, KnownHosts known, List<int> persistedEpochs, List<PromotionProof> persistedProofs, StandbyPromotion? promotion)
    {
        public ClientSession Session { get; } = session;
        public EventTap Tap { get; } = tap;
        public KnownHosts Known { get; } = known;
        public List<int> PersistedEpochs { get; } = persistedEpochs;
        public List<PromotionProof> PersistedProofs { get; } = persistedProofs;
        public StandbyPromotion? Promotion { get; } = promotion;
        public int Prompts { get; set; }
    }

    private async Task<Seat> JoinAsync(InProcessHost host, ClientIdentity identity, string name, string? dataRoot = null, X509Certificate2? ownCertificate = null, bool advertise = true)
    {
        var known = new KnownHosts();
        List<int> epochs = [];
        List<PromotionProof> proofs = [];
        var promotion = ownCertificate != null && dataRoot != null ? new StandbyPromotion(dataRoot, identity, ownCertificate, new InProcessHostOptions { TickInterval = null, Store = new StoreOptions { SnapshotEveryTurns = 2 } }) : null;
        Seat? seat = null;
        var session = new ClientSession(token => TcpTlsClientTransport.ConnectAsync("127.0.0.1", host.Port!.Value, cancellationToken: token), new ClientSessionOptions
        {
            Identity = identity,
            DisplayName = name,
            RulesetHash = "",
            ExpectedFingerprint = host.Fingerprint,
            HostAddress = $"127.0.0.1:{host.Port}",
            KnownHosts = known,
            TrustPrompt = (_, _) =>
            {
                seat!.Prompts++;
                return Task.FromResult(false);
            },
            GameId = host.GameId,
            StandbyRoot = dataRoot,
            AdvertiseHostMigration = advertise,
            ReconnectDelay = (_, _) => Task.Delay(50),
            KeepAliveInterval = Timeout.InfiniteTimeSpan,
            PersistKnownEpoch = epochs.Add,
            PersistEpochProof = proofs.Add,
            OnHandoff = promotion == null ? null : (handoff, token) => promotion.Handler(handoff, token),
            ConnectEndpoint = (endpoint, token) =>
            {
                var separator = endpoint.LastIndexOf(':');
                return TcpTlsClientTransport.ConnectAsync(endpoint[..separator], int.Parse(endpoint[(separator + 1)..], System.Globalization.CultureInfo.InvariantCulture), cancellationToken: token);
            },
        });
        seat = new Seat(session, new EventTap(session), known, epochs, proofs, promotion);
        Track(seat.Tap);
        Track(session);
        var joined = await session.JoinAsync();
        Assert.True(joined.Joined, joined.Code);
        return seat;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string what)
    {
        for (var attempt = 0; attempt < 750 && !condition(); attempt++) await Task.Delay(20);
        Assert.True(condition(), what);
    }

    /// <summary>The host with Babylon, a designated and ready successor (Greece, own listener) and a third player (Rome).</summary>
    private async Task<(InProcessHost Host, Seat Successor, Seat Third, int SuccessorPort)> ReadyAsync(string name, bool romeAdvertises = true)
    {
        var successorCert = NewCertificate();
        var successorPort = FreePort();
        var host = StartHost(name, HostCertificate.GetFingerprint(successorCert));
        host.Listen(NewCertificate(), 0);
        var dataRoot = Path.Combine(root, name + "-successor");
        var successor = await JoinAsync(host, greece, "Friend", dataRoot, successorCert);
        var third = await JoinAsync(host, rome, "Third", advertise: romeAdvertises);
        Assert.Null(await host.Host.DesignateBackupAsync("Greece", babylon.PlayerKey));
        await successor.Tap.WaitAsync<StandbyOffered>();
        await successor.Session.AcceptStandbyAsync(["127.0.0.1"], successorPort, HostCertificate.GetFingerprint(successorCert));
        await WaitUntilAsync(() => host.Host.Standby.State == StandbyStates.Ready, "the standby never became ready");
        await third.Tap.WaitAsync<SuccessorChanged>(it => it.Successor.Successor != null);
        return (host, successor, third, successorPort);
    }

    [Fact]
    public async Task GracefulHandoff_PromotesTheSuccessor_AndEveryClientFollowsWithTheSameKeys()
    {
        var (host, successor, third, successorPort) = await ReadyAsync("a4");
        await PlayTurnsAsync(host, 2);
        var (oldSeq, oldHash) = await StateAsync(host);
        await WaitUntilAsync(() => successor.Session.StandbyHaveSeq >= oldSeq, "the standby copy never caught up");

        Assert.Null(await host.HandOffAndStopAsync(babylon.PlayerKey, TimeSpan.FromSeconds(15)));

        var handed = await successor.Tap.WaitAsync<HostHandedOff>();
        Assert.Equal(1, handed.Handoff.Epoch);
        Assert.Equal(oldSeq, handed.Handoff.FinalSeq);
        Assert.Equal(Hex(oldHash), handed.Handoff.FinalHash);
        Assert.Equal(babylon.PlayerKey, handed.Handoff.DepartedKeyId);
        Assert.Equal(greece.PlayerKey, handed.Handoff.Successor.KeyId);
        await successor.Tap.WaitAsync<HostChanged>(it => it.Epoch == 1);
        await third.Tap.WaitAsync<HostChanged>(it => it.Epoch == 1);
        Assert.DoesNotContain(third.Tap.All(), it => it is HostClosed);

        // Exactly one successor promoted: its host holds the old host's state at the hand-off, one epoch up.
        var promoted = successor.Promotion!.Host!;
        Assert.Null(third.Promotion);
        Assert.Equal(1, promoted.Host.Store is FileGameStore store ? store.ReadMeta().HostEpoch : -1);
        Assert.Equal(successorPort, promoted.Port);
        var (newSeq, newHash) = await StateAsync(promoted);
        Assert.Equal(oldSeq, newSeq);
        Assert.Equal(oldHash, newHash);
        Assert.Contains("\"hostEpoch\":1", File.ReadLines(Path.Combine(promoted.Directory, "commands.log")).First(), StringComparison.Ordinal);

        // The departed seat follows the T267 rule at the new host, nothing else changed.
        Assert.True(promoted.Host.TurnTimer.HasPendingTakeover("Babylon"));

        // Same keys, same seats, no prompt: both clients are seated on the new host and see what it would show them.
        Assert.Equal(0, successor.Prompts + third.Prompts);
        foreach (var (seat, civ) in new[] { (successor, "Greece"), (third, "Rome") })
        {
            await WaitUntilAsync(() => promoted.Host.Sessions.Values.Any(it => it.BoundCivId == civ && it.PreviousSnapshot != null && seat.Session.Game.ViewHash == it.PreviousSnapshot.ViewHash && seat.Session.Game.Seq == oldSeq), $"{civ} did not resume on the new host");
            Assert.Contains(promoted.Host.Sessions.Values, it => it.BoundCivId == civ && it.PlayerKeyId == (civ == "Greece" ? greece.PlayerKey : rome.PlayerKey));
            Assert.Contains(1, seat.PersistedEpochs);
        }
        Assert.Contains(third.PersistedProofs, it => it.Epoch == 1 && it.KeyId == greece.PlayerKey);
        Assert.True(third.Known.Pins.ContainsKey($"127.0.0.1:{successorPort}")); // pinned from the announcement (vouched), never prompted

        // The UI wraps the promoted host like a resumed hosted game.
        await using var adopted = HostingSession.Adopt(promoted, promoted.Directory);
        Assert.Equal(promoted.Fingerprint, adopted.Fingerprint);
        Assert.Equal(successorPort, adopted.Port);
        Assert.Same(promoted, adopted.Resumed);

        // The log continues with no gap.
        await PlayTurnsAsync(promoted, 1);
        var (afterSeq, _) = await StateAsync(promoted);
        Assert.True(afterSeq > oldSeq);
        await WaitUntilAsync(() => third.Session.Game.Seq == afterSeq, "the follower did not get the new host's deltas");
        await adopted.DisposeAsync(); // the UI's quit: stops the promoted host cleanly
        using var reopened = FileGameStore.Open(promoted.Directory);
        Assert.Equal(RecoveryOutcome.Recovered, reopened.Report.Outcome);
        Assert.Equal(0, reopened.Report.DroppedLogLines);
        Assert.Equal(afterSeq, reopened.Store.LastSeq);
    }

    [Fact]
    public async Task Handoff_ReachesOnlyClientsThatAdvertisedHostMigration_TheRestGetThePlainClosing()
    {
        var (host, successor, third, _) = await ReadyAsync("legacy", romeAdvertises: false);
        await PlayTurnsAsync(host, 1);
        var (seq, _) = await StateAsync(host);
        await WaitUntilAsync(() => successor.Session.StandbyHaveSeq >= seq, "the standby copy never caught up");

        Assert.Null(await host.HandOffAndStopAsync(babylon.PlayerKey, TimeSpan.FromSeconds(15)));

        await third.Tap.WaitAsync<HostClosed>();
        Assert.DoesNotContain(third.Tap.All(), it => it is HostHandedOff);
        await successor.Tap.WaitAsync<HostChanged>();
        Assert.NotNull(successor.Promotion!.Host);
    }

    [Fact]
    public async Task Handoff_WithoutAReadySuccessor_IsRefused_AndTheHostKeepsRunning()
    {
        var host = StartHost("nosuccessor");
        host.Listen(NewCertificate(), 0);
        var friend = await JoinAsync(host, greece, "Friend");
        Assert.Equal(GameHost.HandoffRefusals.NoSuccessor, await host.HandOffAndStopAsync(babylon.PlayerKey, TimeSpan.FromSeconds(1)));
        // HandOffAndStop falls back to the plain stop: the client is told the ordinary way.
        await friend.Tap.WaitAsync<HostClosed>();
        Assert.DoesNotContain(friend.Tap.All(), it => it is HostHandedOff);

        var kept = StartHost("nosuccessor2");
        Assert.Equal(GameHost.HandoffRefusals.NoSuccessor, await kept.Host.HandOffAsync(TimeSpan.FromMilliseconds(100)));
        await PlayTurnsAsync(kept, 1); // untouched: it still runs
    }

    [Fact]
    public async Task Handoff_ASuccessorThatDoesNotCatchUp_IsRefusedAfterTheTimeout_ThenWorksOnceItAcks()
    {
        var cert = NewCertificate();
        var fingerprint = HostCertificate.GetFingerprint(cert);
        var host = StartHost("lagging", fingerprint);
        host.Listen(NewCertificate(), 0);
        Assert.Null(await host.Host.DesignateBackupAsync("Greece", babylon.PlayerKey));
        var dataRoot = Path.Combine(root, "lagging-successor");
        var successor = Track(await TestSuccessor.JoinAsync(host, greece, dataRoot, host.Host.Game.GameId));
        await successor.WaitAsync<StandbyOffer>();
        await successor.SendAsync(new StandbyAccept { Endpoints = ["127.0.0.1"], Port = FreePort(), Fingerprint = fingerprint });
        await successor.WaitAsync<StandbyStatus>(it => it.State == StandbyStates.Ready);
        await successor.SubscribeAsync();
        await successor.WaitAsync<StandbyStatus>(it => it.State == StandbyStates.Ready && it.LagEntries == 0 && successor.Store!.HaveSeq >= 0);

        successor.HoldAcks = true;
        await PlayTurnsAsync(host, 1);
        var (seq, hash) = await StateAsync(host);
        Assert.Equal(GameHost.HandoffRefusals.NotFlushed, await host.Host.HandOffAsync(TimeSpan.FromMilliseconds(400), babylon.PlayerKey));
        Assert.DoesNotContain(successor.All(), it => it is HostHandoff); // nothing was told, nothing closed

        successor.HoldAcks = false;
        await successor.WaitForSeqAsync(seq);
        await successor.AckNowAsync();
        Assert.Null(await host.Host.HandOffAsync(TimeSpan.FromSeconds(15), babylon.PlayerKey));
        var handoff = await successor.WaitAsync<HostHandoff>();
        Assert.Equal(seq, handoff.FinalSeq);
        Assert.Equal(Hex(hash), handoff.FinalHash);
        Assert.Equal(1, handoff.Epoch);
        Assert.Equal(new[] { "127.0.0.1" }, handoff.Successor.Endpoints.Select(it => it[..it.LastIndexOf(':')]));

        // The old directory is superseded for good: restarted by mistake it refuses everybody instead of forking the game.
        await host.StopAsync();
        using var opened = FileGameStore.Open(host.Directory);
        Assert.Equal(1, opened.Store.ReadMeta().SeenEpoch);
        Assert.Equal(0, opened.Store.ReadMeta().HostEpoch);
    }

    // ------------------------------------------------------------------ promotion rules

    private string StandbyCopyOf(InProcessHost host, string name)
    {
        var target = Path.Combine(root, name);
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(host.Directory)) File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
        return target;
    }

    private HostPromotion PromotionFor(string gameId, long seq, ulong hash, string? departedKey = null, int epoch = 1, string fingerprint = "00")
    {
        var Fingerprint = fingerprint;
        return new HostPromotion
        {
            Epoch = epoch,
            FinalSeq = seq,
            FinalHash = Hex(hash),
            DepartedKeyId = departedKey,
            Proof = new PromotionProof { GameId = gameId, Epoch = epoch, KeyId = greece.PlayerKey, Fingerprint = Fingerprint, Signature = greece.SignPromotion(gameId, epoch, Fingerprint) },
        };
    }

    [Fact]
    public async Task Promotion_RefusesACopyThatDoesNotReproduceTheHandoff_AndPromotesNothing()
    {
        var host = StartHost("rules");
        await PlayTurnsAsync(host, 2);
        var (seq, hash) = await StateAsync(host);
        await host.StopAsync();
        var gameId = host.GameId;

        foreach (var (promotion, expected) in new[]
        {
            (PromotionFor(gameId, seq, hash ^ 1UL), "standby-diverged"),
            (PromotionFor(gameId, seq + 1, hash), "standby-incomplete"),
            (PromotionFor(gameId, seq - 1, hash), "standby-incomplete"),
        })
        {
            var copy = StandbyCopyOf(host, "rules-" + expected + promotion.FinalSeq);
            var error = Assert.Throws<GameStoreException>(() => InProcessHost.ResumeFromDirectory(copy, greece.PlayerKey, new InProcessHostOptions { TickInterval = null, Promotion = promotion }));
            Assert.StartsWith(expected, error.Message, StringComparison.Ordinal);
            using var untouched = FileGameStore.Open(copy);
            Assert.Equal(0, untouched.Store.ReadMeta().HostEpoch); // nothing was promoted: still a plain standby copy
            Assert.Null(untouched.Store.ReadMeta().Promotion);
        }

        var ok = StandbyCopyOf(host, "rules-ok");
        var same = PromotionFor(gameId, seq, hash, babylon.PlayerKey);
        await using var promoted = InProcessHost.ResumeFromDirectory(ok, greece.PlayerKey, new InProcessHostOptions { TickInterval = null, Promotion = same });
        Assert.Equal(1, ((FileGameStore)promoted.Host.Store!).ReadMeta().HostEpoch);
        Assert.Equal(hash, (await StateAsync(promoted)).Hash);

        // A retry of the very same promotion (a failed Listen left the copy promoted) resumes it instead of being refused.
        await promoted.StopAsync();
        var retried = InProcessHost.ResumeFromDirectory(ok, greece.PlayerKey, new InProcessHostOptions { TickInterval = null, Promotion = same });
        await using (retried) Assert.Equal(hash, (await StateAsync(retried)).Hash);

        // An epoch that does not go up is refused, so a promotion cannot be replayed onto a promoted copy.
        await promoted.StopAsync();
        var again = Assert.Throws<GameStoreException>(() => InProcessHost.ResumeFromDirectory(ok, greece.PlayerKey, new InProcessHostOptions { TickInterval = null, Promotion = PromotionFor(gameId, seq, hash, fingerprint: "11") }));
        Assert.StartsWith("standby-epoch", again.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Promotion_AHalfResolvedRound_ContinuesThroughNextResolveStep()
    {
        var host = StartHost("resolving");
        await PlayTurnsAsync(host, 1);
        ulong hash = 0;
        long seq = 0;
        string phase = "";
        await host.Host.EnqueueAsync(() =>
        {
            // Everybody ended the turn: the round is Resolving and its first steps are logged, then the process "dies".
            foreach (var civ in host.Host.Game.Civilizations.Where(it => it.IsHuman())) Assert.True(host.Host.Session.Submit(civ.CivId, new EndTurn()).Accepted);
            host.Host.Store!.Sync(host.Host.Session);
            phase = host.Host.Game.TurnState!.Phase.ToString();
            seq = host.Host.Store.LastSeq;
            hash = ReplayVerifier.FullHash(host.Host.Game);
            return Task.CompletedTask;
        });
        Assert.Equal("Resolving", phase);
        var copy = StandbyCopyOf(host, "resolving-copy");

        await using var promoted = InProcessHost.ResumeFromDirectory(copy, greece.PlayerKey, new InProcessHostOptions { TickInterval = null, Promotion = PromotionFor(host.GameId, seq, hash) });
        Assert.Equal(SimultaneousPhase.Playing, promoted.Host.Game.TurnState!.Phase); // the promoted host finished the round itself
        Assert.True(promoted.Host.Game.Turns >= 2);
    }

    // ------------------------------------------------------------------ rolled-back resume

    [Fact]
    public async Task Resume_ClientAheadOfTheHost_GetsARolledBackSnapshot_ThroughTheNormalFilter()
    {
        var host = StartHost("rolledback");
        host.Listen(NewCertificate(), 0);
        await PlayTurnsAsync(host, 1);
        var (seq, _) = await StateAsync(host);
        var transport = await TcpTlsClientTransport.ConnectAsync("127.0.0.1", host.Port!.Value, expectedFingerprint: host.Fingerprint);
        await using var _ = transport;
        await transport.SendAsync(ProtocolCodec.Encode(new Hello { Protocol = ProtocolVersion.Current, ClientVersion = "t", RulesetHash = "", ModList = [], HostMigration = true }));
        var welcome = Assert.IsType<Welcome>(ProtocolCodec.Decode((await transport.ReceiveAsync())!));
        await transport.SendAsync(ProtocolCodec.Encode(new Join { PlayerKey = greece.PlayerKey, DisplayName = "x" }));
        await transport.SendAsync(ProtocolCodec.Encode(new Auth { Proof = greece.Sign(welcome.HostKeyFingerprint, welcome.SessionId, welcome.Challenge) }));
        Assert.IsType<JoinOk>(ProtocolCodec.Decode((await transport.ReceiveAsync())!));
        Assert.IsType<Snapshot>(ProtocolCodec.Decode((await transport.ReceiveAsync())!)); // the attach snapshot

        await transport.SendAsync(ProtocolCodec.Encode(new Resume { GameId = "another-game", LastSeq = seq + 3, LastViewHash = "x" }));
        Assert.Equal(ErrorCodes.InvalidMessage, Assert.IsType<Error>(ProtocolCodec.Decode((await transport.ReceiveAsync())!)).Code); // never a snapshot of another game

        await transport.SendAsync(ProtocolCodec.Encode(new Resume { GameId = host.GameId, LastSeq = seq + 3, LastViewHash = "x" }));
        var rolled = Assert.IsType<Snapshot>(ProtocolCodec.Decode((await transport.ReceiveAsync())!));
        Assert.True(rolled.RolledBack);
        Assert.Equal(3, rolled.LostCommands);
        Assert.Equal(seq, rolled.Seq);
        Assert.Equal("Greece", rolled.You); // the viewer's own filtered state

        await transport.SendAsync(ProtocolCodec.Encode(new Resume { GameId = host.GameId, LastSeq = seq, LastViewHash = rolled.ViewHash }));
        var current = Assert.IsType<Snapshot>(ProtocolCodec.Decode((await transport.ReceiveAsync())!));
        Assert.Null(current.RolledBack);
        Assert.Null(current.LostCommands);
    }

    // ------------------------------------------------------------------ the store and the proof

    [Fact]
    public async Task PromoteTo_WritesTheEpochIntoGameJsonAndTheLogHeader_AndNothingElseChanges()
    {
        var host = StartHost("store");
        await PlayTurnsAsync(host, 1);
        await host.StopAsync();
        var headerBefore = UncivJson.Deserialize<CommandLogHeader>(File.ReadLines(Path.Combine(host.Directory, "commands.log")).First())!;
        var linesBefore = File.ReadLines(Path.Combine(host.Directory, "commands.log")).Skip(1).ToList();

        using (var opened = FileGameStore.Open(host.Directory))
        {
            var proof = PromotionFor(host.GameId, opened.Store.LastSeq, 0).Proof;
            Assert.Throws<GameStoreException>(() => opened.Store.PromoteTo(0, proof with { Epoch = 0 })); // not above the held epoch
            Assert.Throws<GameStoreException>(() => opened.Store.PromoteTo(1, proof with { Epoch = 2 })); // proof for another epoch
            Assert.Throws<GameStoreException>(() => opened.Store.PromoteTo(1, proof with { GameId = "other" })); // proof for another game
            opened.Store.PromoteTo(1, proof);
            opened.Store.PromoteTo(1, proof with { Signature = greece.SignPromotion(host.GameId, 1, proof.Fingerprint) }); // a retry of the same promotion (fresh signature) finishes it
            Assert.Throws<GameStoreException>(() => opened.Store.PromoteTo(1, proof with { KeyId = rome.PlayerKey })); // another key cannot take the same epoch
            Assert.Equal(1, opened.Store.ReadMeta().HostEpoch);
            Assert.Equal(proof, opened.Store.ReadMeta().Promotion); // the first proof stays
            Assert.Equal(1L, opened.Store.LogGeneration);
        }
        var headerAfter = UncivJson.Deserialize<CommandLogHeader>(File.ReadLines(Path.Combine(host.Directory, "commands.log")).First())!;
        Assert.Equal(1, headerAfter.HostEpoch);
        Assert.Equal(UncivJson.Serialize(headerBefore with { HostEpoch = 1 }), UncivJson.Serialize(headerAfter));
        Assert.Equal(linesBefore, File.ReadLines(Path.Combine(host.Directory, "commands.log")).Skip(1).ToList());
        using var again = FileGameStore.Open(host.Directory);
        Assert.Equal(RecoveryOutcome.Recovered, again.Report.Outcome);

        // A later write of game.json by the lobby layer keeps the proof.
        again.Store.WriteMeta(again.Store.ReadMeta() with { Promotion = null, HostFingerprint = "x" });
        Assert.NotNull(again.Store.ReadMeta().Promotion);
    }

    [Fact]
    public void PromotionProof_IsBoundToGameEpochFingerprintAndSigner_AndNeverDoublesAsAnAuthProof()
    {
        var proof = new PromotionProof { GameId = "g", Epoch = 2, KeyId = greece.PlayerKey, Fingerprint = "ff", Signature = greece.SignPromotion("g", 2, "ff") };
        Assert.True(PlayerKeys.VerifyPromotion(proof));
        Assert.False(PlayerKeys.VerifyPromotion(null));
        Assert.False(PlayerKeys.VerifyPromotion(proof with { GameId = "h" }));
        Assert.False(PlayerKeys.VerifyPromotion(proof with { Epoch = 3 }));
        Assert.False(PlayerKeys.VerifyPromotion(proof with { Fingerprint = "fe" }));
        Assert.False(PlayerKeys.VerifyPromotion(proof with { KeyId = rome.PlayerKey }));
        Assert.False(PlayerKeys.VerifyPromotion(proof with { Signature = "not base64 !" }));
        Assert.False(PlayerKeys.VerifyPromotion(proof with { KeyId = "junk" }));

        // A malicious host that chooses the strings of an auth challenge cannot get a promotion signature out of the client.
        var auth = greece.Sign("ff", "g", "2");
        Assert.False(PlayerKeys.VerifyPromotion(proof with { Signature = auth }));
    }

    // ------------------------------------------------------------------ the client's own checks

    private static SuccessorInfo Info(string keyId, string fingerprint) => new() { KeyId = keyId, Endpoints = ["succ.example:47900"], Fingerprint = fingerprint };

    private static readonly string SuccessorFp = HostKey.Fingerprint(new byte[] { 7, 7, 7 });

    /// <summary>A client in a game on a scripted host that announced <c>succ.example:47900</c>; the scripted successor answers redirects.</summary>
    private async Task<(ClientNetRig Rig, FakeHostConnection Old, EventTap Tap, ClientIdentity Successor, Queue<FakeHostConnection> Redirects)> ClientInGameAsync(bool failingPromotion = false)
    {
        var successor = ClientIdentity.Generate();
        toDispose.Add(new Disposer(successor.Dispose));
        Queue<FakeHostConnection> redirects = new();
        var rig = Track(new ClientNetRig(id =>
        {
            var built = new ClientSessionOptions
            {
                Identity = id,
                DisplayName = "Alice",
                RulesetHash = "hash",
                ExpectedFingerprint = ClientNetRig.Fingerprint,
                HostAddress = "old.example:47821",
                GameId = "game-1",
                ReconnectDelay = (_, _) => Task.CompletedTask,
                KeepAliveInterval = Timeout.InfiniteTimeSpan,
                OnHandoff = failingPromotion ? (_, _) => throw new HostingException("no copy") : null,
                ConnectEndpoint = (_, _) =>
                {
                    var (client, host) = InMemoryTransport.CreatePair();
                    redirects.Enqueue(new FakeHostConnection(host));
                    return Task.FromResult<ITransport>(client);
                },
            };
            return built;
        }));
        var old = rig.NewConnection();
        var tap = Track(new EventTap(rig.Session));
        var join = rig.Session.JoinAsync();
        await rig.HandshakeAsync(old);
        Assert.True((await join).Joined);
        await old.SendAsync(ClientNetRig.SnapshotMessage(ClientNetRig.Snap(5, 1)));
        await tap.WaitAsync<SnapshotReplaced>();
        await old.SendAsync(new HostSuccessor { Epoch = 0, Status = StandbyStates.Ready, Roster = [], Successor = Info(successor.PlayerKey, SuccessorFp) });
        await tap.WaitAsync<SuccessorChanged>();
        return (rig, old, tap, successor, redirects);
    }

    private sealed class Disposer(Action action) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            action();
            return ValueTask.CompletedTask;
        }
    }

    private static HostHandoff HandoffTo(SuccessorInfo successor, int epoch = 1, string gameId = "game-1") =>
        new() { GameId = gameId, Epoch = epoch, Successor = successor, FinalSeq = 5, FinalHash = "00" };

    [Fact]
    public async Task Client_FollowsAHandoffToTheAnnouncedSuccessor_ProvesItsPromotion_AndTakesTheRolledBackSnapshot()
    {
        var (rig, old, tap, successor, redirects) = await ClientInGameAsync();
        var announced = Info(successor.PlayerKey, SuccessorFp);
        await old.SendAsync(HandoffTo(announced));
        await old.CloseAsync();
        await tap.WaitAsync<HostHandedOff>();

        var next = await WaitRedirectAsync(redirects);
        await next.ReceiveAsync<Hello>();
        var proof = new PromotionProof { GameId = "game-1", Epoch = 1, KeyId = successor.PlayerKey, Fingerprint = SuccessorFp, Signature = successor.SignPromotion("game-1", 1, SuccessorFp) };
        await next.SendAsync(new Welcome { Protocol = ProtocolVersion.Current, SessionId = "s2", Challenge = "c2", HostKeyFingerprint = SuccessorFp, RequiresPassword = false, Epoch = 1, Promotion = proof });
        await next.ReceiveAsync<Join>();
        var auth = await next.ReceiveAsync<Auth>();
        Assert.True(PlayerKeys.VerifyProof(rig.PlayerKey, SuccessorFp, "s2", "c2", auth.Proof)); // the same key, now proven to the new host
        await next.SendAsync(new JoinOk());
        var resume = await next.ReceiveAsync<Resume>();
        Assert.Equal(("game-1", 5L), (resume.GameId, resume.LastSeq));

        await next.SendAsync(ClientNetRig.SnapshotMessage(ClientNetRig.Snap(3, 1)) with { RolledBack = true, LostCommands = 2 });
        Assert.Equal(1, (await tap.WaitAsync<HostChanged>()).Epoch);
        Assert.Equal(2, (await tap.WaitAsync<HostRolledBack>()).LostCommands);
        Assert.Equal(3, rig.Session.Game.Seq);
    }

    [Fact]
    public async Task Client_RefusesAHandoffThatIsNotTheAnnouncedSuccessorOrTheNextEpoch()
    {
        var (rig, old, tap, successor, redirects) = await ClientInGameAsync();
        _ = rig;
        var announced = Info(successor.PlayerKey, SuccessorFp);
        using var other = ClientIdentity.Generate();
        var bad = new[]
        {
            HandoffTo(Info(other.PlayerKey, SuccessorFp)), // another key than announced
            HandoffTo(Info(successor.PlayerKey, HostKey.Fingerprint(new byte[] { 9 }))), // another certificate
            HandoffTo(announced with { Endpoints = ["evil.example:47900"] }), // an endpoint nobody announced
            HandoffTo(announced, epoch: 2), // skips an epoch
            HandoffTo(announced, epoch: 0),
            HandoffTo(announced, gameId: "another-game"),
        };
        foreach (var handoff in bad)
        {
            await old.SendAsync(handoff);
            Assert.Equal(ErrorCodes.InvalidMessage, (await tap.WaitAsync<ProtocolError>(it => it.Code == ErrorCodes.InvalidMessage)).Code);
        }
        await Task.Delay(100);
        Assert.DoesNotContain(tap.All(), it => it is HostHandedOff);
        Assert.Empty(redirects);
    }

    [Fact]
    public async Task Client_RefusesARedirectTargetThatDoesNotProveItIsThePromotedSuccessor()
    {
        using var impostor = ClientIdentity.Generate();
        foreach (var variant in new[] { "none", "wrong-signer", "wrong-epoch", "wrong-fingerprint", "wrong-welcome-epoch" })
        {
            var (rig, old, tap, successor, redirects) = await ClientInGameAsync();
            await using var _ = rig;
            await old.SendAsync(HandoffTo(Info(successor.PlayerKey, SuccessorFp)));
            await old.CloseAsync();
            var next = await WaitRedirectAsync(redirects);
            await next.ReceiveAsync<Hello>();
            var signer = variant == "wrong-signer" ? impostor : successor;
            var epoch = variant == "wrong-epoch" ? 2 : 1;
            var fingerprint = variant == "wrong-fingerprint" ? HostKey.Fingerprint(new byte[] { 5 }) : SuccessorFp;
            var proof = variant == "none" ? null : new PromotionProof { GameId = "game-1", Epoch = epoch, KeyId = signer.PlayerKey, Fingerprint = fingerprint, Signature = signer.SignPromotion("game-1", epoch, fingerprint) };
            await next.SendAsync(new Welcome { Protocol = ProtocolVersion.Current, SessionId = "s2", Challenge = "c2", HostKeyFingerprint = SuccessorFp, RequiresPassword = false, Epoch = variant == "wrong-welcome-epoch" ? 2 : 1, Promotion = proof });
            Assert.Equal(ClientCodes.BadPromotion, (await tap.WaitAsync<SessionEnded>()).Reason);
            Assert.DoesNotContain(tap.All(), it => it is HostChanged);
        }
    }

    [Fact]
    public async Task Client_WhoseSuccessorCannotPromote_EndsInsteadOfConnecting()
    {
        var (rig, old, tap, successor, redirects) = await ClientInGameAsync(failingPromotion: true);
        await using var _ = rig;
        await old.SendAsync(HandoffTo(Info(successor.PlayerKey, SuccessorFp)));
        await old.CloseAsync();
        await tap.WaitAsync<ProtocolError>(it => it.Code == ClientCodes.BadPromotion);
        Assert.Empty(redirects); // never connected to anything claiming to be the new host
    }

    private static async Task<FakeHostConnection> WaitRedirectAsync(Queue<FakeHostConnection> redirects)
    {
        for (var attempt = 0; attempt < 500 && redirects.Count == 0; attempt++) await Task.Delay(20);
        Assert.NotEmpty(redirects);
        return redirects.Dequeue();
    }
}
