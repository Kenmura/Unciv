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
using Unciv.Net.Lobby;
using Unciv.Net.Persistence;
using Unciv.Net.Turns;
using Unciv.Server;
using Unciv.Server.Transport;
using RulesetClass = Unciv.Models.Ruleset.Ruleset;

namespace Unciv.Core.Tests.Server;

[Collection("RulesetCache")]
public sealed class InProcessHostTests : IDisposable
{
    private const long Seed = 11L;
    private const long StartClock = 1000L;
    private readonly RulesetClass ruleset;
    private readonly string root = Directory.CreateTempSubdirectory("unciv-inproc-").FullName;
    private readonly ClientIdentity babylon = ClientIdentity.Generate();
    private readonly ClientIdentity greece = ClientIdentity.Generate();

    public InProcessHostTests()
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

    private (GameInfo Game, GameSetupInfo Setup) NewGame()
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
        return (game, setup);
    }

    private Dictionary<string, SlotBinding> Bindings() => new()
    {
        ["Babylon"] = new SlotBinding { PlayerKey = babylon.PlayerKey, DisplayName = "Host" },
        ["Greece"] = new SlotBinding { PlayerKey = greece.PlayerKey, DisplayName = "Friend" },
    };

    private InProcessHost StartHost(string name = "game", InProcessHostOptions? options = null)
    {
        var (game, setup) = NewGame();
        return InProcessHost.Start(game, setup, Seed, StartClock, Path.Combine(root, name), "Babylon", Bindings(), options ?? new InProcessHostOptions { TickInterval = null });
    }

    private static async Task<Message> ReadAsync(ITransport transport)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var frame = await transport.ReceiveAsync(cts.Token);
        Assert.NotNull(frame);
        return ProtocolCodec.Decode(frame);
    }

    private async Task<(ITransport Transport, Message Last)> JoinOverTcpAsync(InProcessHost host, ClientIdentity identity)
    {
        var transport = await TcpTlsClientTransport.ConnectAsync("127.0.0.1", host.Port!.Value, expectedFingerprint: host.Fingerprint);
        await transport.SendAsync(ProtocolCodec.Encode(new Hello { Protocol = ProtocolVersion.Current, ClientVersion = "test", RulesetHash = "", ModList = [] }));
        var welcome = Assert.IsType<Welcome>(await ReadAsync(transport));
        await transport.SendAsync(ProtocolCodec.Encode(new Join { PlayerKey = identity.PlayerKey, DisplayName = "x" }));
        await transport.SendAsync(ProtocolCodec.Encode(new Auth { Proof = identity.Sign(welcome.HostKeyFingerprint, welcome.SessionId, welcome.Challenge) }));
        return (transport, await ReadAsync(transport));
    }

    private async Task<(ITransport Transport, Welcome Welcome, Message Last)> HandshakeAsync(InProcessHost host, ClientIdentity identity, Hello hello)
    {
        var transport = await TcpTlsClientTransport.ConnectAsync("127.0.0.1", host.Port!.Value, expectedFingerprint: host.Fingerprint);
        await transport.SendAsync(ProtocolCodec.Encode(hello));
        var first = await ReadAsync(transport);
        if (first is not Welcome welcome) return (transport, null!, first);
        await transport.SendAsync(ProtocolCodec.Encode(new Join { PlayerKey = identity.PlayerKey, DisplayName = "x" }));
        await transport.SendAsync(ProtocolCodec.Encode(new Auth { Proof = identity.Sign(welcome.HostKeyFingerprint, welcome.SessionId, welcome.Challenge) }));
        return (transport, welcome, await ReadAsync(transport));
    }

    private static Hello HelloWith(string? gameId, int? knownEpoch, PromotionProof? proof = null) =>
        new() { Protocol = ProtocolVersion.Current, ClientVersion = "test", RulesetHash = "", ModList = [], GameId = gameId, KnownEpoch = knownEpoch, HostMigration = true, EpochProof = proof };

    private static PromotionProof ProofBy(ClientIdentity signer, string gameId, int epoch, string fingerprint = "ab") =>
        new() { GameId = gameId, Epoch = epoch, KeyId = signer.PlayerKey, Fingerprint = fingerprint, Signature = signer.SignPromotion(gameId, epoch, fingerprint) };

    [Fact]
    public async Task HostEpoch_NewGame_IsZeroAndLeavesTheFilesUnchanged()
    {
        await using var host = StartHost("epoch0");
        using var cert = HostCertificate.Generate();
        host.Listen(cert, 0);
        var (transport, welcome, last) = await HandshakeAsync(host, greece, HelloWith(null, null));
        await using (transport)
        {
            Assert.Equal(0, welcome.Epoch);
            Assert.IsType<JoinOk>(last);
        }
        Assert.DoesNotContain("hostEpoch", File.ReadAllText(Path.Combine(host.Directory, "game.json")), StringComparison.Ordinal);
        Assert.DoesNotContain("seenEpoch", File.ReadAllText(Path.Combine(host.Directory, "game.json")), StringComparison.Ordinal);
        Assert.DoesNotContain("hostEpoch", File.ReadLines(Path.Combine(host.Directory, "commands.log")).First(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task HostEpoch_PromotedHost_AnnouncesItsEpochAndKeepsItAcrossMetaWrites()
    {
        await using var host = StartHost("epoch2");
        var store = (FileGameStore)host.Host.Store!;
        store.RaiseEpochs(2, 0);
        store.WriteMeta(store.ReadMeta() with { HostFingerprint = "x" }); // the lobby layer writes meta from its own copy
        Assert.Equal(2, store.ReadMeta().HostEpoch);
        using var cert = HostCertificate.Generate();
        host.Listen(cert, 0);
        var (transport, welcome, last) = await HandshakeAsync(host, greece, HelloWith(host.GameId, 1));
        await using (transport)
        {
            Assert.Equal(2, welcome.Epoch);
            Assert.IsType<JoinOk>(last);
        }
        Assert.Equal(0, store.ReadMeta().SeenEpoch);
    }

    [Fact]
    public async Task HostEpoch_ClientHoldingTheDesignatedSuccessorsProof_FencesThisHostForGood()
    {
        var dir = Path.Combine(root, "fenced");
        await using (var host = StartHost("fenced"))
        {
            using var cert = HostCertificate.Generate();
            host.Listen(cert, 0);
            Assert.Null(await host.Host.DesignateBackupAsync("Greece", babylon.PlayerKey));

            // Designated but not accepted (no hand-off can come from it): its proof refuses that connection and fences nothing.
            var (early, earlyWelcome, earlyLast) = await HandshakeAsync(host, babylon, HelloWith(host.GameId, 1, ProofBy(greece, host.GameId, 1)));
            await using (early)
            {
                Assert.NotNull(earlyWelcome);
                Assert.Equal(JoinRefusedCodes.StaleEpoch, Assert.IsType<JoinRefused>(earlyLast).Code);
            }
            var (fine, fineWelcome, _) = await HandshakeAsync(host, babylon, HelloWith(null, null));
            await using (fine) Assert.NotNull(fineWelcome);

            var successorFingerprint = HostKey.Fingerprint(new byte[] { 9, 9, 9 });
            host.Host.Standby.Prober = new Unciv.Core.Tests.Net.Standby.FakeProber { Answer = (_, _) => successorFingerprint };
            Assert.Null(await host.Host.Standby.AcceptAsync(greece.PlayerKey, new StandbyAccept { Endpoints = ["succ.example"], Port = 47900, Fingerprint = successorFingerprint }));
            var (transport, welcome, last) = await HandshakeAsync(host, babylon, HelloWith(host.GameId, 1, ProofBy(greece, host.GameId, 1)));
            await using (transport)
            {
                Assert.Equal(0, welcome.Epoch);
                Assert.Equal(JoinRefusedCodes.StaleEpoch, Assert.IsType<JoinRefused>(last).Code);
            }

            // Superseded: even a client that knows nothing is turned away before the handshake goes on.
            var (again, none, refusal) = await HandshakeAsync(host, babylon, HelloWith(null, null));
            await using (again)
            {
                Assert.Null(none);
                Assert.Equal(JoinRefusedCodes.StaleEpoch, Assert.IsType<JoinRefused>(refusal).Code);
            }
            await host.StopAsync();
        }

        using var opened = FileGameStore.Open(dir);
        Assert.Equal(1, opened.Store.ReadMeta().SeenEpoch);
        Assert.Equal(0, opened.Store.ReadMeta().HostEpoch);
    }

    [Fact]
    public async Task HostEpoch_ForgedClaim_RefusesThatConnectionOnly_NeverFencesTheHost()
    {
        await using var host = StartHost("forged");
        using var cert = HostCertificate.Generate();
        host.Listen(cert, 0);
        Assert.Null(await host.Host.DesignateBackupAsync("Greece", babylon.PlayerKey));
        var store = (FileGameStore)host.Host.Store!;
        using var outsider = ClientIdentity.Generate();
        var claims = new[]
        {
            HelloWith(host.GameId, 7), // a bare claim of a seat holder (the T269 residual risk)
            HelloWith(host.GameId, 7, ProofBy(babylon, host.GameId, 7)), // a seat key that is not the designated successor
            HelloWith(host.GameId, 7, ProofBy(outsider, host.GameId, 7)), // a key that holds no seat
            HelloWith(host.GameId, 7, ProofBy(greece, host.GameId, 6)), // the successor's proof of another epoch
            HelloWith(host.GameId, 7, ProofBy(greece, "another-game", 7)), // of another game
            HelloWith(host.GameId, 7, ProofBy(greece, host.GameId, 7) with { Signature = ProofBy(babylon, host.GameId, 7).Signature }), // a signature that is not the signer's
        };
        foreach (var claim in claims)
        {
            var (transport, _, last) = await HandshakeAsync(host, babylon, claim);
            await using (transport) Assert.Equal(JoinRefusedCodes.StaleEpoch, Assert.IsType<JoinRefused>(last).Code);
            Assert.Equal(0, store.ReadMeta().SeenEpoch);
        }
        var (honest, _, joined) = await HandshakeAsync(host, babylon, HelloWith(null, null)); // the host still serves everyone else
        await using (honest) Assert.IsType<JoinOk>(joined);
    }

    [Fact]
    public async Task HostEpoch_UnauthenticatedOrForeignClaims_DoNotFenceTheHost()
    {
        await using var host = StartHost("claims");
        using var cert = HostCertificate.Generate();
        host.Listen(cert, 0);
        using var stranger = ClientIdentity.Generate();
        var (t1, _, forged) = await HandshakeAsync(host, stranger, HelloWith(host.GameId, 9)); // no seat: refused for that, not fenced
        await using (t1) Assert.Equal(JoinRefusedCodes.GameAlreadyStarted, Assert.IsType<JoinRefused>(forged).Code);
        Assert.Equal(0, host.Host.Store is FileGameStore f ? f.ReadMeta().SeenEpoch : -1);

        var (t2, _, other) = await HandshakeAsync(host, greece, HelloWith("some-other-game", 9)); // another game's epoch means nothing here
        await using (t2) Assert.IsType<JoinOk>(other);
    }

    [Fact]
    public async Task InProcessHost_BackgroundTickLoop_RunsAndStopsCleanly_AndExposesItsParts()
    {
        var host = StartHost("ticking", new InProcessHostOptions { TickInterval = TimeSpan.FromMilliseconds(10) });
        Assert.Equal(Path.Combine(root, "ticking"), host.Directory);
        Assert.NotNull(host.Host.Gate);
        Assert.NotNull(host.Host.RemoteQueries);
        Assert.NotNull(host.Host.Clock);
        await host.Host.EnqueueAsync(() => Task.CompletedTask); // the actor answers while the loop ticks beside it
        await host.StopAsync();
    }

    [Fact]
    public async Task InProcessHost_TurnPolicyOption_ReachesTheTurnTimer()
    {
        var policy = new LobbySettings { TurnTimerSeconds = 240, TurnTimerMinSeconds = 45, DynamicTurnTimer = true }.ToTurnPolicy();
        await using var host = StartHost("policy", new InProcessHostOptions { TickInterval = null, TurnPolicy = policy });
        Assert.Equal(policy, host.Host.TurnTimer.Policy);
        await using var plain = StartHost("plain");
        Assert.Equal(TurnPolicy.Default, plain.Host.TurnTimer.Policy);
    }

    [Fact]
    public async Task InProcessHost_Resume_KeepsTheStoredTurnPolicy()
    {
        var policy = new TurnPolicy { TurnTimerSeconds = 240, TurnTimerMinSeconds = 45, DynamicTurnTimer = true, OnDisconnect = DisconnectAction.AiTakeover, DropGraceSeconds = 7, AbsentTurnsBeforeAiTakeover = 5 };
        var dir = Path.Combine(root, "keep");
        await using (var host = StartHost("keep", new InProcessHostOptions { TickInterval = null, TurnPolicy = policy })) await host.StopAsync();
        using (var opened = FileGameStore.Open(dir)) Assert.Equal(policy, opened.Store.ReadMeta().TimerPolicy);

        await using (var resumed = InProcessHost.ResumeFromDirectory(dir, babylon.PlayerKey, new InProcessHostOptions { TickInterval = null }))
        {
            Assert.Equal(policy, resumed.Host.TurnTimer.Policy);
            await resumed.StopAsync();
        }

        var other = new TurnPolicy { TurnTimerSeconds = 99 };
        await using var overridden = InProcessHost.ResumeFromDirectory(dir, babylon.PlayerKey, new InProcessHostOptions { TickInterval = null, TurnPolicy = other });
        Assert.Equal(other, overridden.Host.TurnTimer.Policy);
    }

    [Fact]
    public async Task InProcessHost_Resume_WithoutStoredPolicy_UsesTheDefault()
    {
        var dir = Path.Combine(root, "nopolicy");
        await using (var host = StartHost("nopolicy")) await host.StopAsync();
        await using var resumed = InProcessHost.ResumeFromDirectory(dir, babylon.PlayerKey, new InProcessHostOptions { TickInterval = null });
        Assert.Equal(TurnPolicy.Default, resumed.Host.TurnTimer.Policy);
    }

    [Fact]
    public async Task ServerLobby_StartGame_RunsTheGameWithTheLobbyTimerSettings()
    {
        var settings = new LobbySettings { TurnTimerSeconds = 240, TurnTimerMinSeconds = 45, DynamicTurnTimer = true };
        await using var lobby = new ServerLobby(settings);
        var (client, server) = InMemoryTransport.CreatePair();
        _ = lobby.AttachConnectionAsync(server);
        await client.SendAsync(ProtocolCodec.Encode(new Hello { Protocol = ProtocolVersion.Current, ClientVersion = "test", RulesetHash = settings.ComputeRulesetHash(), ModList = [] }));
        var welcome = Assert.IsType<Welcome>(await ReadAsync(client));
        await client.SendAsync(ProtocolCodec.Encode(new Join { PlayerKey = babylon.PlayerKey, DisplayName = "Alice" }));
        await client.SendAsync(ProtocolCodec.Encode(new Auth { Proof = babylon.Sign(lobby.Lobby.HostFingerprint, welcome.SessionId, welcome.Challenge) }));
        while (await ReadAsync(client) is not LobbyState) { }
        await client.SendAsync(ProtocolCodec.Encode(new LobbySet { Slot = 0, Nation = "Rome", Ready = true }));
        while (await ReadAsync(client) is not LobbyState) { }
        var gameHost = await lobby.StartGameAsync(force: true, seed: 42L, clockMillis: 5000L);
        Assert.Equal(settings.ToTurnPolicy(), gameHost.TurnTimer.Policy);
    }

    [Fact]
    public async Task InProcessHost_HostPlayerLoopback_ReceivesSnapshotForOwnCiv()
    {
        await using var host = StartHost();
        var client = await host.ConnectHostPlayerAsync();
        var snapshot = Assert.IsType<Snapshot>(await ReadAsync(client));
        Assert.Equal("Babylon", snapshot.You);
        Assert.Equal(host.GameId, host.Host.Game.GameId);
        Assert.Null(host.Report);
        Assert.Null(host.Port);
    }

    [Fact]
    public async Task InProcessHost_TcpListener_BoundFriendJoinsAndGetsSnapshot()
    {
        await using var host = StartHost();
        using var cert = HostCertificate.Generate();
        host.Listen(cert, 0);
        Assert.True(host.Port > 0);
        Assert.Equal(HostCertificate.GetFingerprint(cert), host.Fingerprint);
        Assert.Equal(host.Fingerprint, host.Host.Store!.ReadMeta().HostFingerprint);

        var (transport, joinOk) = await JoinOverTcpAsync(host, greece);
        await using (transport)
        {
            Assert.IsType<JoinOk>(joinOk);
            var snapshot = Assert.IsType<Snapshot>(await ReadAsync(transport));
            Assert.Equal("Greece", snapshot.You);
        }
    }

    [Fact]
    public async Task InProcessHost_TcpListener_UnboundKeyAndBadProofAreRefused()
    {
        await using var host = StartHost();
        using var cert = HostCertificate.Generate();
        host.Listen(cert, 0);
        Assert.Throws<InvalidOperationException>(() => host.Listen(cert, 0));

        using var stranger = ClientIdentity.Generate();
        var (strangerTransport, refused) = await JoinOverTcpAsync(host, stranger);
        await using (strangerTransport)
        {
            Assert.Equal(JoinRefusedCodes.GameAlreadyStarted, Assert.IsType<JoinRefused>(refused).Code);
        }

        // A bound key with somebody else's signature must not get the seat.
        var forged = await TcpTlsClientTransport.ConnectAsync("127.0.0.1", host.Port!.Value, expectedFingerprint: host.Fingerprint);
        await using (forged)
        {
            await forged.SendAsync(ProtocolCodec.Encode(new Hello { Protocol = ProtocolVersion.Current, ClientVersion = "t", RulesetHash = "", ModList = [] }));
            var welcome = Assert.IsType<Welcome>(await ReadAsync(forged));
            await forged.SendAsync(ProtocolCodec.Encode(new Join { PlayerKey = greece.PlayerKey, DisplayName = "x" }));
            await forged.SendAsync(ProtocolCodec.Encode(new Auth { Proof = stranger.Sign(welcome.HostKeyFingerprint, welcome.SessionId, welcome.Challenge) }));
            Assert.Equal(JoinRefusedCodes.AuthFailed, Assert.IsType<JoinRefused>(await ReadAsync(forged)).Code);
        }
        Assert.Empty(host.Host.Sessions);
    }

    [Fact]
    public async Task InProcessHost_TcpListener_WrongProtocolIsRefused()
    {
        await using var host = StartHost();
        using var cert = HostCertificate.Generate();
        host.Listen(cert, 0);
        var transport = await TcpTlsClientTransport.ConnectAsync("127.0.0.1", host.Port!.Value, expectedFingerprint: host.Fingerprint);
        await using (transport)
        {
            await transport.SendAsync(ProtocolCodec.Encode(new Hello { Protocol = 999, ClientVersion = "t", RulesetHash = "", ModList = [] }));
            Assert.Equal(JoinRefusedCodes.ProtocolMismatch, Assert.IsType<JoinRefused>(await ReadAsync(transport)).Code);
        }
    }

    [Fact]
    public async Task InProcessHost_Reconnect_ReplacesOlderSessionOfSameCiv()
    {
        await using var host = StartHost();
        using var cert = HostCertificate.Generate();
        host.Listen(cert, 0);
        var (first, _) = await JoinOverTcpAsync(host, greece);
        await using (first)
        {
            await ReadAsync(first);
            var (second, ok) = await JoinOverTcpAsync(host, greece);
            await using (second)
            {
                Assert.IsType<JoinOk>(ok);
                Assert.IsType<Snapshot>(await ReadAsync(second));
                Assert.Single(host.Host.Sessions);
                Assert.True(host.Host.TurnTimer.IsConnected("Greece"));
            }
        }
    }

    [Fact]
    public async Task InProcessHost_Stop_SnapshotsWithEndedFlagMarksCleanAndResumes()
    {
        var dir = Path.Combine(root, "stop");
        ulong hashBefore;
        await using (var host = StartHost("stop"))
        {
            var client = await host.ConnectHostPlayerAsync();
            await ReadAsync(client);
            Assert.True(host.Host.Session.Submit("Babylon", new EndTurn()).Accepted);
            host.Host.Store!.Sync(host.Host.Session);
            Assert.Contains("Babylon", host.Host.Game.TurnState!.Ended);
            hashBefore = StateHash(host.Host.Game);

            await host.StopAsync();
            Assert.IsType<HostClosing>(await ReadAsync(client));
        }

        Assert.True(File.Exists(Path.Combine(dir, "clean")));
        Assert.NotEmpty(Directory.GetFiles(dir, "snapshot-*.json.gz"));

        await using var resumed = InProcessHost.ResumeFromDirectory(dir, babylon.PlayerKey, new InProcessHostOptions { TickInterval = null });
        Assert.True(resumed.Report!.WasCleanShutdown);
        Assert.Equal(hashBefore, StateHash(resumed.Host.Game));
        Assert.Contains("Babylon", resumed.Host.Game.TurnState!.Ended);
        Assert.Equal("Greece", resumed.Host.Store!.ReadMeta().Slots.First(x => x.Value.PlayerKey == greece.PlayerKey).Key);

        // Clients reconnect with Resume and are served from the resumed state.
        var client2 = await resumed.ConnectHostPlayerAsync();
        var snapshot = Assert.IsType<Snapshot>(await ReadAsync(client2));
        Assert.Equal("Babylon", snapshot.You);
        await client2.SendAsync(ProtocolCodec.Encode(new Resume { GameId = resumed.GameId, LastSeq = 0, LastViewHash = snapshot.ViewHash }));
        Assert.IsType<Snapshot>(await ReadAsync(client2));
    }

    [Fact]
    public async Task InProcessHost_Stop_IsIdempotentAndDisposeAfterStopIsSafe()
    {
        var host = StartHost("idem");
        await host.StopAsync();
        await host.StopAsync();
        await host.DisposeAsync();
    }

    [Fact]
    public void InProcessHost_Start_RequiresBindingForHostCiv()
    {
        var (game, setup) = NewGame();
        Assert.Throws<ArgumentException>(() => InProcessHost.Start(game, setup, Seed, StartClock, Path.Combine(root, "nobind"), "Rome", Bindings()));
    }

    [Fact]
    public async Task InProcessHost_Resume_UnboundHostKeyThrows()
    {
        var dir = Path.Combine(root, "unbound");
        await StartHost("unbound").StopAsync();
        using var other = ClientIdentity.Generate();
        Assert.Throws<GameStoreException>(() => InProcessHost.ResumeFromDirectory(dir, other.PlayerKey));
    }

    [Fact]
    public async Task InProcessHost_CrashMidResolve_ResumeContinuesThroughNextResolveStep()
    {
        var dir = Path.Combine(root, "crash");
        var crashed = Path.Combine(root, "crash-image");
        int turnBefore;
        await using (var host = StartHost("crash"))
        {
            var game = host.Host.Game;
            turnBefore = game.Turns;
            Assert.True(host.Host.Session.Submit("Babylon", new EndTurn()).Accepted);
            Assert.True(host.Host.Session.Submit("Greece", new EndTurn()).Accepted);
            Assert.Equal(SimultaneousPhase.Resolving, game.TurnState!.Phase);
            host.Host.ApplyServerCommand(ResolvePump.StepCommand(game.NextResolveStep()!));
            Assert.Equal(SimultaneousPhase.Resolving, game.TurnState.Phase);

            // The process dies here: the directory as it is now (no clean marker, mid-resolve) is the crash image.
            CopyDirectory(dir, crashed);
            Assert.False(File.Exists(Path.Combine(crashed, "clean")));
        }

        await using var resumed = InProcessHost.ResumeFromDirectory(crashed, babylon.PlayerKey, new InProcessHostOptions { TickInterval = null });
        Assert.False(resumed.Report!.WasCleanShutdown);
        Assert.Equal(SimultaneousPhase.Playing, resumed.Host.Game.TurnState!.Phase);
        Assert.Equal(turnBefore + 1, resumed.Host.Game.Turns);
        Assert.Null(resumed.Host.Game.NextResolveStep());
    }

    [Fact]
    public async Task InProcessHost_TickLoop_RunsAndStopsCleanly()
    {
        await using var host = StartHost("tick", new InProcessHostOptions { TickInterval = TimeSpan.FromMilliseconds(10) });
        await Task.Delay(100);
        await host.StopAsync();
    }

    private static ulong StateHash(GameInfo game) => ReplayVerifier.FullHash(game);

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from))
        {
            using var src = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var dst = File.Create(Path.Combine(to, Path.GetFileName(file)));
            src.CopyTo(dst);
        }
        foreach (var sub in Directory.GetDirectories(from)) CopyDirectory(sub, Path.Combine(to, Path.GetFileName(sub)));
    }
}
