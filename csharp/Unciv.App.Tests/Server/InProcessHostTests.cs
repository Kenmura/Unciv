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
