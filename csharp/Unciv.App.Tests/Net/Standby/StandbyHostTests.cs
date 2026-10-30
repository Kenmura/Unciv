using Unciv.Client.Net.Transport;
using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using Unciv.Net;
using Unciv.Net.Identity;
using Unciv.Net.Lobby;
using Unciv.Net.Persistence;
using Unciv.Net.Standby;
using Unciv.Server;
using Unciv.Server.Transport;
using RulesetClass = Unciv.Models.Ruleset.Ruleset;

namespace Unciv.Core.Tests.Net.Standby;

/// <summary>A connection that records every frame it receives, so a test can ask "did this ever arrive" and "wait for that".</summary>
internal sealed class Wire : IAsyncDisposable
{
    private readonly ITransport transport;
    private readonly List<Message> received = [];
    private readonly CancellationTokenSource stop = new();
    private readonly Task reader;

    public Wire(ITransport transport)
    {
        this.transport = transport;
        reader = ReadAsync();
    }

    private async Task ReadAsync()
    {
        try
        {
            while (true)
            {
                var frame = await transport.ReceiveAsync(stop.Token);
                if (frame == null) return;
                lock (received) received.Add(ProtocolCodec.Decode(frame));
            }
        }
        catch (Exception) { /* closed */ }
    }

    public Task SendAsync(Message message) => transport.SendAsync(ProtocolCodec.Encode(message)).AsTask();

    public List<Message> All()
    {
        lock (received) return [.. received];
    }

    public List<T> Of<T>() where T : Message => All().OfType<T>().ToList();

    /// <summary>The next <typeparamref name="T"/> matching <paramref name="where"/> (looking at everything received so far too).</summary>
    public async Task<T> WaitAsync<T>(Func<T, bool>? where = null) where T : Message
    {
        for (var attempt = 0; attempt < 750; attempt++) // 15 s
        {
            var hit = Of<T>().FirstOrDefault(it => where == null || where(it));
            if (hit != null) return hit;
            await Task.Delay(20);
        }
        throw new TimeoutException($"No {typeof(T).Name} arrived; got: {string.Join(", ", All().Select(it => it.GetType().Name))}");
    }

    public async ValueTask DisposeAsync()
    {
        await stop.CancelAsync();
        await transport.DisposeAsync();
        await reader;
        stop.Dispose();
    }
}

/// <summary>T281 in the pre-game lobby: designation by slot, the trust notice, consent, and the designated-only rule over real <see cref="LobbyHost"/> sessions.</summary>
[Collection("RulesetCache")]
public sealed class LobbyStandbyTests : IDisposable
{
    private static readonly string Fp = HostKey.Fingerprint(new byte[] { 4, 2 });

    public LobbyStandbyTests()
    {
        var jsons = Path.Combine(RepoRoot(), "android", "assets", "jsons");
        RulesetCache.LoadRulesets(new DirectoryRulesetFileSource(jsons, Path.Combine(jsons, "no mods here")), noMods: true);
    }

    public void Dispose() => RulesetCache.Clear();

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "android", "assets", "jsons"))) return dir.FullName;
        throw new DirectoryNotFoundException("android/assets/jsons not found above " + AppContext.BaseDirectory);
    }

    private static async Task<(Wire Wire, Welcome Welcome)> JoinAsync(LobbyHost lobby, ClientIdentity identity, string name)
    {
        var (client, server) = InMemoryTransport.CreatePair();
        _ = lobby.AttachConnectionAsync(server);
        var wire = new Wire(client);
        await wire.SendAsync(new Hello { Protocol = ProtocolVersion.Current, ClientVersion = "1", RulesetHash = lobby.Settings.ComputeRulesetHash(), ModList = [] });
        var welcome = await wire.WaitAsync<Welcome>();
        await wire.SendAsync(new Join { PlayerKey = identity.PlayerKey, DisplayName = name });
        await wire.SendAsync(new Auth { Proof = identity.Sign(lobby.HostFingerprint, welcome.SessionId, welcome.Challenge) });
        await wire.WaitAsync<JoinOk>();
        return (wire, welcome);
    }

    private static LobbyHost NewLobby(int? backup, FakeProber prober)
    {
        var lobby = new LobbyHost(new LobbySettings { BackupHost = backup });
        lobby.Standby.Prober = prober;
        return lobby;
    }

    [Fact]
    public async Task Welcome_CarriesTheNameFreeTrustNoticeOnlyWhileABackupHostIsIntended()
    {
        await using var none = new LobbyHost();
        using var id = ClientIdentity.Generate();
        var (plain, plainWelcome) = await JoinAsync(none, id, "Ann");
        await using (plain) Assert.Null(plainWelcome.BackupHostNotice);

        await using var lobby = NewLobby(1, new FakeProber());
        using var other = ClientIdentity.Generate();
        var (wire, welcome) = await JoinAsync(lobby, other, "Ann");
        await using (wire) Assert.Equal(StandbyNotice.Generic, welcome.BackupHostNotice);
    }

    [Fact]
    public async Task LobbyState_CarriesTheNoticeNamingTheBackupHost_ToEveryPlayer()
    {
        var prober = new FakeProber { Answer = (_, _) => Fp };
        await using var lobby = NewLobby(1, prober);
        using var ann = ClientIdentity.Generate();
        using var bob = ClientIdentity.Generate();
        var (annWire, _) = await JoinAsync(lobby, ann, "Ann");
        await using var _1 = annWire;
        var first = await annWire.WaitAsync<LobbyState>();
        Assert.Null(first.BackupHost); // slot 1 is still empty: nobody to name yet
        Assert.Null(first.BackupHostNotice);

        var (bobWire, _) = await JoinAsync(lobby, bob, "Bob");
        await using var _2 = bobWire;
        var state = await annWire.WaitAsync<LobbyState>(it => it.BackupHost == 1);
        Assert.Equal(StandbyStates.Offered, state.BackupHostState);
        Assert.Equal(StandbyNotice.For("Bob"), state.BackupHostNotice);
        Assert.Contains("Bob holds a complete copy of this game, including what you cannot see.", state.BackupHostNotice, StringComparison.Ordinal);
        var bobsOwn = await bobWire.WaitAsync<LobbyState>(it => it.BackupHost == 1);
        Assert.Equal(state.BackupHostNotice, bobsOwn.BackupHostNotice);
    }

    [Fact]
    public async Task Offer_ConsentAndAnnouncement_ForTheDesignatedPlayerOnly()
    {
        var prober = new FakeProber { Answer = (_, _) => Fp };
        await using var lobby = NewLobby(1, prober);
        using var ann = ClientIdentity.Generate();
        using var bob = ClientIdentity.Generate();
        var (annWire, _) = await JoinAsync(lobby, ann, "Ann");
        await using var _1 = annWire;
        var (bobWire, _) = await JoinAsync(lobby, bob, "Bob");
        await using var _2 = bobWire;

        var offer = await bobWire.WaitAsync<StandbyOffer>();
        Assert.Equal(bob.PlayerKey, offer.KeyId);
        Assert.Empty(annWire.Of<StandbyOffer>()); // the other player never sees an offer

        // Ann is not designated: refused with the one generic code, nothing changes, nothing is dialled.
        await annWire.SendAsync(new StandbyAccept { Endpoints = ["friend.example"], Port = 47821, Fingerprint = Fp });
        Assert.Equal(ErrorCodes.NotDesignated, (await annWire.WaitAsync<Error>()).Code);
        Assert.Equal(StandbyStates.Offered, lobby.Standby.State);
        Assert.Empty(prober.Dialled);
        await annWire.SendAsync(new StandbyDecline());
        await annWire.WaitAsync<Error>(it => it.Code == ErrorCodes.NotDesignated);
        Assert.Equal(1, lobby.Settings.BackupHost);

        // Bob accepts: probed, ready, everyone is told, the lobby state shows it.
        await bobWire.SendAsync(new StandbyAccept { Endpoints = ["friend.example"], Port = 47821, Fingerprint = Fp });
        Assert.Equal(StandbyStates.Ready, (await bobWire.WaitAsync<StandbyStatus>(it => it.State == StandbyStates.Ready)).State);
        var announced = await annWire.WaitAsync<HostSuccessor>(it => it.Status == StandbyStates.Ready);
        Assert.Equal(["friend.example:47821"], announced.Successor!.Endpoints);
        Assert.Equal(bob.PlayerKey, announced.Successor.KeyId);
        Assert.Contains(announced.Roster, it => it.KeyId == bob.PlayerKey && it.Connected);
        Assert.Equal(StandbyStates.Ready, (await annWire.WaitAsync<LobbyState>(it => it.BackupHostState == StandbyStates.Ready)).BackupHostState);
        Assert.Empty(annWire.Of<StandbyStatus>()); // status is the candidate's, the announcement is everyone's

        // A player who joins later learns it in JoinOk.
        using var cat = ClientIdentity.Generate();
        var (catWire, _) = await JoinAsync(lobby, cat, "Cat");
        await using var _3 = catWire;
        Assert.Equal(StandbyStates.Ready, (await catWire.WaitAsync<JoinOk>()).Successor!.Status);
    }

    [Fact]
    public async Task ReprobeStandby_InTheLobby_TellsTheCandidateAndAnnouncesTheChange()
    {
        var prober = new FakeProber { Answer = (_, _) => Fp };
        await using var lobby = NewLobby(1, prober);
        lobby.Standby.Limits = new StandbyLimits { ProbeMinIntervalMillis = 0, ReprobeIntervalMillis = 0 };
        using var ann = ClientIdentity.Generate();
        using var bob = ClientIdentity.Generate();
        var (annWire, _) = await JoinAsync(lobby, ann, "Ann");
        await using var _1 = annWire;
        var (bobWire, _) = await JoinAsync(lobby, bob, "Bob");
        await using var _2 = bobWire;
        await lobby.ReprobeStandbyAsync(); // not accepted yet: nothing to probe
        Assert.Empty(prober.Dialled);
        await bobWire.SendAsync(new StandbyAccept { Endpoints = ["friend.example"], Port = 47821, Fingerprint = Fp });
        await bobWire.WaitAsync<StandbyStatus>(it => it.State == StandbyStates.Ready);

        await lobby.ReprobeStandbyAsync(); // probed again, still ready: nothing to say
        Assert.Equal(2, prober.Dialled.Count);
        Assert.Single(bobWire.Of<StandbyStatus>());

        prober.Answer = (_, _) => null;
        await lobby.ReprobeStandbyAsync();
        await bobWire.WaitAsync<StandbyStatus>(it => it.State == StandbyStates.Unreachable);
        await annWire.WaitAsync<HostSuccessor>(it => it.Status == StandbyStates.Unreachable && it.Successor == null);
        Assert.Empty(annWire.Of<StandbyStatus>());
    }

    [Fact]
    public async Task Decline_ClearsTheBackupHost_AndKickEndsTheDesignation()
    {
        var prober = new FakeProber { Answer = (_, _) => Fp };
        await using var lobby = NewLobby(1, prober);
        using var ann = ClientIdentity.Generate();
        using var bob = ClientIdentity.Generate();
        var (annWire, _) = await JoinAsync(lobby, ann, "Ann");
        await using var _1 = annWire;
        var (bobWire, _) = await JoinAsync(lobby, bob, "Bob");
        await using var _2 = bobWire;
        await bobWire.WaitAsync<StandbyOffer>();

        await bobWire.SendAsync(new StandbyDecline());
        Assert.Equal(StandbyStates.None, (await bobWire.WaitAsync<StandbyStatus>()).State);
        var cleared = await annWire.WaitAsync<LobbyState>(it => it.BackupHost == null && it.Slots.Count > 1 && it.Slots[1].PlayerName == "Bob" && annWire.Of<LobbyState>().Any(x => x.BackupHost == 1));
        Assert.Null(cleared.BackupHostNotice);
        Assert.Null(lobby.Settings.BackupHost);

        // The host picks Bob again (offered again), moves the role to Ann (Bob is told it ended), then removes Ann's seat (the designation ends with it).
        await lobby.UpdateSettingsAsync(new LobbySettings { BackupHost = 1 });
        await bobWire.WaitAsync<StandbyOffer>(_ => bobWire.Of<StandbyOffer>().Count >= 2);
        await lobby.UpdateSettingsAsync(new LobbySettings { BackupHost = 0 });
        await bobWire.WaitAsync<StandbyRevoke>();
        Assert.Equal(ann.PlayerKey, (await annWire.WaitAsync<StandbyOffer>()).KeyId);
        await lobby.KickAsync(0);
        await annWire.WaitAsync<JoinRefused>(it => it.Code == JoinRefusedCodes.Kicked);
        Assert.Null(lobby.Settings.BackupHost);
        Assert.Equal(StandbyStates.None, lobby.Standby.State);
    }

    [Fact]
    public async Task ReconnectOfTheCandidate_IsOfferedTheRoleAgain()
    {
        await using var lobby = NewLobby(1, new FakeProber());
        using var ann = ClientIdentity.Generate();
        using var bob = ClientIdentity.Generate();
        var (annWire, _) = await JoinAsync(lobby, ann, "Ann");
        await using var _1 = annWire;
        var (first, _) = await JoinAsync(lobby, bob, "Bob");
        await first.WaitAsync<StandbyOffer>();
        await first.DisposeAsync();

        var (second, _) = await JoinAsync(lobby, bob, "Bob");
        await using var _2 = second;
        Assert.Equal(bob.PlayerKey, (await second.WaitAsync<StandbyOffer>()).KeyId);
    }

    [Fact]
    public async Task UnauthenticatedConnection_CannotAcceptOrDecline()
    {
        await using var lobby = NewLobby(1, new FakeProber { Answer = (_, _) => Fp });
        var (client, server) = InMemoryTransport.CreatePair();
        _ = lobby.AttachConnectionAsync(server);
        await using var wire = new Wire(client);
        await wire.SendAsync(new StandbyAccept { Endpoints = ["friend.example"], Port = 47821, Fingerprint = Fp });
        await wire.SendAsync(new StandbyDecline());
        await wire.WaitAsync<Error>(it => it.Code == ErrorCodes.NotDesignated);
        Assert.Equal(StandbyStates.None, lobby.Standby.State);
        Assert.Equal(1, lobby.Settings.BackupHost);
    }
}

/// <summary>T281 in a running game over TCP+TLS: the host designates a seat, the candidate consents, game.json remembers, and nobody else gets standby frames.</summary>
[Collection("RulesetCache")]
public sealed class GameStandbyTests : IDisposable
{
    private const long Seed = 11L;
    private const long StartClock = 1000L;
    private static readonly string Fp = HostKey.Fingerprint(new byte[] { 3, 1, 4 });
    private readonly RulesetClass ruleset;
    private readonly string root = Directory.CreateTempSubdirectory("unciv-standby-").FullName;
    private readonly ClientIdentity babylon = ClientIdentity.Generate();
    private readonly ClientIdentity greece = ClientIdentity.Generate();

    public GameStandbyTests()
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
        try { Directory.Delete(root, recursive: true); } catch (IOException) { }
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "android", "assets", "jsons"))) return dir.FullName;
        throw new DirectoryNotFoundException("android/assets/jsons not found above " + AppContext.BaseDirectory);
    }

    private InProcessHost StartHost(string name, FakeProber prober)
    {
        var setup = new GameSetupInfo(
            new GameParameters
            {
                BaseRuleset = ruleset.Name,
                NumberOfCityStates = 0,
                VictoryTypes = ruleset.Victories.Keys.ToList(),
                Players = [new("Babylon", PlayerType.Human), new("Greece", PlayerType.Human), new("Rome", PlayerType.AI)],
            },
            new MapParameters { Type = MapType.Pangaea, Shape = MapShape.Rectangular, Seed = 3, MapSize = MapSize.Tiny });
        var game = GameStarter.StartNewGame(setup, Seed, StartClock);
        game.StartSimultaneousTurns();
        var humans = new Dictionary<string, SlotBinding>
        {
            ["Babylon"] = new SlotBinding { PlayerKey = babylon.PlayerKey, DisplayName = "Host" },
            ["Greece"] = new SlotBinding { PlayerKey = greece.PlayerKey, DisplayName = "Friend" },
        };
        var host = InProcessHost.Start(game, setup, Seed, StartClock, Path.Combine(root, name), "Babylon", humans, new InProcessHostOptions { TickInterval = null });
        host.Host.Standby.Prober = prober;
        return host;
    }

    private static async Task<(Wire Wire, JoinOk Ok)> JoinAsync(InProcessHost host, ClientIdentity identity)
    {
        var transport = await TcpTlsClientTransport.ConnectAsync("127.0.0.1", host.Port!.Value, expectedFingerprint: host.Fingerprint);
        var wire = new Wire(transport);
        await wire.SendAsync(new Hello { Protocol = ProtocolVersion.Current, ClientVersion = "test", RulesetHash = "", ModList = [] });
        var welcome = await wire.WaitAsync<Welcome>();
        await wire.SendAsync(new Join { PlayerKey = identity.PlayerKey, DisplayName = "x" });
        await wire.SendAsync(new Auth { Proof = identity.Sign(welcome.HostKeyFingerprint, welcome.SessionId, welcome.Challenge) });
        return (wire, await wire.WaitAsync<JoinOk>());
    }

    [Fact]
    public async Task Designate_OfferConsentProbeAndPersist_AndOthersNeverGetStandbyFrames()
    {
        var prober = new FakeProber { Answer = (_, _) => Fp };
        await using var host = StartHost("g1", prober);
        using var cert = HostCertificate.Generate();
        host.Listen(cert, 0);
        var loopback = new Wire(await host.ConnectHostPlayerAsync());
        await using var _1 = loopback;

        Assert.Equal("own-key", await host.Host.DesignateBackupAsync("Babylon", babylon.PlayerKey));
        Assert.Equal("unknown-civ", await host.Host.DesignateBackupAsync("Atlantis"));
        Assert.Equal("not-bound", await host.Host.DesignateBackupAsync("Rome"));
        Assert.Equal(StandbyStates.None, host.Host.Standby.State);

        Assert.Null(await host.Host.DesignateBackupAsync("Greece", babylon.PlayerKey));
        var (friend, ok) = await JoinAsync(host, greece);
        await using var _2 = friend;
        Assert.Equal(StandbyStates.Offered, ok.Successor!.Status);
        Assert.Equal(greece.PlayerKey, (await friend.WaitAsync<StandbyOffer>()).KeyId);

        // The host's own session is not designated: refused, and it never receives an offer or a status.
        await loopback.SendAsync(new StandbyAccept { Endpoints = ["host.example"], Port = 47821, Fingerprint = Fp });
        Assert.Equal(ErrorCodes.NotDesignated, (await loopback.WaitAsync<Error>()).Code);
        Assert.Empty(prober.Dialled);

        await friend.SendAsync(new StandbyAccept { Endpoints = ["friend.example"], Port = 47821, Fingerprint = Fp });
        Assert.Equal(StandbyStates.Ready, (await friend.WaitAsync<StandbyStatus>()).State);
        var announced = await loopback.WaitAsync<HostSuccessor>(it => it.Status == StandbyStates.Ready);
        Assert.Equal(greece.PlayerKey, announced.Successor!.KeyId);
        Assert.Equal(0, announced.Epoch);
        Assert.Empty(loopback.Of<StandbyStatus>());
        Assert.Empty(loopback.Of<StandbyOffer>());

        var stored = ((FileGameStore)host.Host.Store!).ReadMeta().Standby!;
        Assert.Equal(greece.PlayerKey, stored.KeyId);
        Assert.True(stored.Accepted);
        Assert.Equal(["friend.example:47821"], stored.Endpoints);

        await host.StopAsync();
        await using var resumed = InProcessHost.ResumeFromDirectory(host.Directory, babylon.PlayerKey, new InProcessHostOptions { TickInterval = null });
        Assert.Equal(greece.PlayerKey, resumed.Host.Standby.KeyId);
        Assert.Equal(StandbyStates.Unreachable, resumed.Host.Standby.State); // not ready again until it is probed
    }

    [Fact]
    public async Task TheTick_ReprobesTheStandby_TellsTheCandidateAndAnnouncesBothDirections()
    {
        var prober = new FakeProber { Answer = (_, _) => Fp };
        await using var host = StartHost("g3", prober);
        host.Host.Standby.Limits = new StandbyLimits { ProbeMinIntervalMillis = 0, ReprobeIntervalMillis = 0 };
        using var cert = HostCertificate.Generate();
        host.Listen(cert, 0);
        var loopback = new Wire(await host.ConnectHostPlayerAsync());
        await using var _1 = loopback;
        var (friend, _) = await JoinAsync(host, greece);
        await using var _2 = friend;
        await host.Host.DesignateBackupAsync("Greece", babylon.PlayerKey);
        await friend.SendAsync(new StandbyAccept { Endpoints = ["friend.example"], Port = 47821, Fingerprint = Fp });
        await loopback.WaitAsync<HostSuccessor>(it => it.Status == StandbyStates.Ready);

        async Task TickAsync() => await host.Host.EnqueueAsync(() => { host.Host.Tick(); return Task.CompletedTask; });
        async Task<int> DialsAfterAsync()
        {
            for (var i = 0; i < 100 && prober.Dialled.Count < 2; i++) await Task.Delay(20);
            return prober.Dialled.Count;
        }

        prober.Answer = (_, _) => null; // the listener is gone
        await TickAsync();
        await loopback.WaitAsync<HostSuccessor>(it => it.Status == StandbyStates.Unreachable && it.Successor == null);
        Assert.Equal(StandbyStates.Unreachable, (await friend.WaitAsync<StandbyStatus>(it => it.State == StandbyStates.Unreachable)).State);
        Assert.True(await DialsAfterAsync() >= 2);
        Assert.Empty(loopback.Of<StandbyStatus>()); // the status goes to the candidate only

        prober.Answer = (_, _) => Fp; // it is back
        var before = loopback.Of<HostSuccessor>().Count;
        for (var i = 0; i < 150 && !loopback.Of<HostSuccessor>().Skip(before).Any(it => it.Status == StandbyStates.Ready && it.Successor != null); i++)
        {
            await TickAsync();
            await Task.Delay(20);
        }
        Assert.Contains(loopback.Of<HostSuccessor>().Skip(before), it => it.Status == StandbyStates.Ready && it.Successor != null);
        Assert.Equal(StandbyStates.Ready, host.Host.Standby.State);
    }

    [Fact]
    public async Task ARepobeStillRunningWhenTheHostCloses_IsDroppedQuietly()
    {
        var gate = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource();
        var prober = new FakeProber { Answer = (_, _) => Fp };
        await using var host = StartHost("g4", prober);
        host.Host.Standby.Limits = new StandbyLimits { ProbeMinIntervalMillis = 0, ReprobeIntervalMillis = 0 };
        using var cert = HostCertificate.Generate();
        host.Listen(cert, 0);
        var (friend, _) = await JoinAsync(host, greece);
        await using var _1 = friend;
        await host.Host.DesignateBackupAsync("Greece", babylon.PlayerKey);
        await friend.SendAsync(new StandbyAccept { Endpoints = ["friend.example"], Port = 47821, Fingerprint = Fp });
        await friend.WaitAsync<StandbyStatus>(it => it.State == StandbyStates.Ready);

        host.Host.Standby.Prober = new BlockingProber(started, gate.Task);
        await host.Host.EnqueueAsync(() => { host.Host.Tick(); return Task.CompletedTask; });
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await host.StopAsync();
        gate.SetResult(null); // the dial ends after the host is gone: a change nobody can announce
        await Task.Delay(100);
    }

    private sealed class BlockingProber(TaskCompletionSource started, Task<string?> gate) : IEndpointProber
    {
        public Task<string?> PresentedFingerprintAsync(string host, int port, TimeSpan timeout, CancellationToken cancellationToken)
        {
            started.TrySetResult();
            return gate;
        }
    }

    [Fact]
    public async Task ServerLobby_ReprobesTheStandbyOnItsOwnTimer()
    {
        var prober = new FakeProber { Answer = (_, _) => Fp };
        await using var lobby = new ServerLobby(new LobbySettings { BackupHost = 1 }, reprobeCheck: TimeSpan.FromMilliseconds(30));
        lobby.Lobby.Standby.Prober = prober;
        lobby.Lobby.Standby.Limits = new StandbyLimits { ProbeMinIntervalMillis = 0, ReprobeIntervalMillis = 0 };
        using var ann = ClientIdentity.Generate();
        using var bob = ClientIdentity.Generate();
        var (_, serverA) = InMemoryTransport.CreatePair();
        _ = serverA;
        var (clientA, serverEndA) = InMemoryTransport.CreatePair();
        _ = lobby.AttachConnectionAsync(serverEndA);
        await using var annWire = new Wire(clientA);
        var (clientB, serverEndB) = InMemoryTransport.CreatePair();
        _ = lobby.AttachConnectionAsync(serverEndB);
        await using var bobWire = new Wire(clientB);
        foreach (var (wire, identity, name) in new[] { (annWire, ann, "Ann"), (bobWire, bob, "Bob") })
        {
            await wire.SendAsync(new Hello { Protocol = ProtocolVersion.Current, ClientVersion = "1", RulesetHash = lobby.Lobby.Settings.ComputeRulesetHash(), ModList = [] });
            var welcome = await wire.WaitAsync<Welcome>();
            await wire.SendAsync(new Join { PlayerKey = identity.PlayerKey, DisplayName = name });
            await wire.SendAsync(new Auth { Proof = identity.Sign(lobby.Lobby.HostFingerprint, welcome.SessionId, welcome.Challenge) });
            await wire.WaitAsync<JoinOk>();
        }
        await bobWire.SendAsync(new StandbyAccept { Endpoints = ["friend.example"], Port = 47821, Fingerprint = Fp });
        await bobWire.WaitAsync<StandbyStatus>(it => it.State == StandbyStates.Ready);

        prober.Answer = (_, _) => null;
        await bobWire.WaitAsync<StandbyStatus>(it => it.State == StandbyStates.Unreachable); // found by the timer, nobody asked
    }

    [Fact]
    public async Task DeclineAndRevoke_ClearTheDesignation_AndAKickedSeatEndsIt()
    {
        var prober = new FakeProber { Answer = (_, _) => Fp };
        await using var host = StartHost("g2", prober);
        using var cert = HostCertificate.Generate();
        host.Listen(cert, 0);
        var (friend, _) = await JoinAsync(host, greece);
        await using var _1 = friend;

        await host.Host.DesignateBackupAsync("Greece");
        await friend.WaitAsync<StandbyOffer>();
        await friend.SendAsync(new StandbyDecline());
        Assert.Equal(StandbyStates.None, (await friend.WaitAsync<StandbyStatus>()).State);
        Assert.Null(((FileGameStore)host.Host.Store!).ReadMeta().Standby);

        await host.Host.DesignateBackupAsync("Greece");
        await host.Host.RevokeBackupAsync();
        await friend.WaitAsync<StandbyRevoke>();
        Assert.Equal(StandbyStates.None, host.Host.Standby.State);
        await host.Host.RevokeBackupAsync(); // nothing to revoke is not an error

        await host.Host.DesignateBackupAsync("Greece");
        Assert.Null(await host.KickAsync("Greece"));
        Assert.Equal(StandbyStates.None, host.Host.Standby.State);
        Assert.Null(((FileGameStore)host.Host.Store!).ReadMeta().Standby);
    }
}
