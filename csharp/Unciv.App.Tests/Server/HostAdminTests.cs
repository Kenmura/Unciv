using Unciv.Client.Net.Transport;
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

/// <summary>C045: in-game chat, the roster, and the host's kick and rebind, against the real <see cref="GameHost"/> and <see cref="InProcessHost"/>.</summary>
[Collection("RulesetCache")]
public sealed class HostAdminTests : IDisposable
{
    private const long Seed = 11L;
    private const long StartClock = 1000L;
    private readonly RulesetClass ruleset;
    private readonly string root = Directory.CreateTempSubdirectory("unciv-admin-").FullName;
    private readonly ClientIdentity babylon = ClientIdentity.Generate();
    private readonly ClientIdentity greece = ClientIdentity.Generate();
    private readonly ClientIdentity newDevice = ClientIdentity.Generate();

    public HostAdminTests()
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
        newDevice.Dispose();
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

    private InProcessHost StartHost(string name, bool announce = false)
    {
        var (game, setup) = NewGame();
        var bindings = new Dictionary<string, SlotBinding>
        {
            ["Babylon"] = new SlotBinding { PlayerKey = babylon.PlayerKey, DisplayName = "Host" },
            ["Greece"] = new SlotBinding { PlayerKey = greece.PlayerKey, DisplayName = "Friend" },
        };
        return InProcessHost.Start(game, setup, Seed, StartClock, Path.Combine(root, name), "Babylon", bindings, new InProcessHostOptions { TickInterval = null, AnnounceRoster = announce });
    }

    private static async Task<T> ReadUntilAsync<T>(ITransport transport) where T : Message
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (true)
        {
            var frame = await transport.ReceiveAsync(cts.Token);
            Assert.NotNull(frame);
            if (ProtocolCodec.Decode(frame) is T wanted) return wanted;
        }
    }

    private static int SessionCounter;

    private static async Task<ITransport> AttachAsync(GameHost host, string civ, string key)
    {
        var (client, hostEnd) = InMemoryTransport.CreatePair();
        await host.AttachSessionAsync("s-" + civ + "-" + Interlocked.Increment(ref SessionCounter), civ, hostEnd, key);
        return client;
    }

    private static Task SayAsync(ITransport transport, string text, string? sender = null) =>
        transport.SendAsync(ProtocolCodec.Encode(new LobbyChat { Text = text, Sender = sender })).AsTask();

    // ------------------------------------------------------------ chat

    [Fact]
    public async Task Chat_ReachesEverySessionWithTheBoundNameNotTheClaimedOne()
    {
        await using var host = StartHost("chat");
        var hostEnd = await host.ConnectHostPlayerAsync();
        var friend = await AttachAsync(host.Host, "Greece", greece.PlayerKey);
        await ReadUntilAsync<Snapshot>(hostEnd);

        await SayAsync(friend, "  hello there  ", sender: "Host");

        var atHost = await ReadUntilAsync<LobbyChat>(hostEnd);
        var atFriend = await ReadUntilAsync<LobbyChat>(friend);
        Assert.Equal(("hello there", "Friend"), (atHost.Text, atHost.Sender));
        Assert.Equal(atHost, atFriend);
    }

    [Fact]
    public async Task Chat_BlankLinesAreDroppedAndLongOnesCut()
    {
        await using var host = StartHost("chat-cut");
        var friend = await AttachAsync(host.Host, "Greece", greece.PlayerKey);

        await SayAsync(friend, " \t ");
        await SayAsync(friend, new string('x', 700));

        var line = await ReadUntilAsync<LobbyChat>(friend);
        Assert.Equal(new string('x', new HostLimits().MaxChatChars), line.Text);
    }

    [Fact]
    public async Task Chat_RateLimitDropsTheExcessUntilTheWindowPasses()
    {
        var (game, _) = NewGame();
        var clock = new ManualClock(1_000_000);
        await using var host = new GameHost(new Unciv.Commands.GameSession(game), clock: clock, limits: new HostLimits { MaxChatPerWindow = 2, ChatWindowMillis = 10_000 });
        var friend = await AttachAsync(host, "Greece", greece.PlayerKey);

        foreach (var text in new[] { "a", "b", "c" }) await SayAsync(friend, text);
        Assert.Equal("a", (await ReadUntilAsync<LobbyChat>(friend)).Text);
        Assert.Equal("b", (await ReadUntilAsync<LobbyChat>(friend)).Text);
        // the reader handles frames in order and waits for each: once the Pong is back, "c" has been seen (and dropped) on the old clock
        await friend.SendAsync(ProtocolCodec.Encode(new Ping()));
        await ReadUntilAsync<Pong>(friend);

        clock.Advance(10_001);
        await SayAsync(friend, "d");
        var next = await ReadUntilAsync<LobbyChat>(friend);
        Assert.Equal("d", next.Text); // "c" was dropped, not queued
        Assert.Equal("Greece", next.Sender); // no store: the civ id stands in for the name
    }

    // ------------------------------------------------------------ roster

    [Fact]
    public async Task Roster_IsAnnouncedOnlyWhenAsked_AndFollowsConnections()
    {
        await using var quiet = StartHost("quiet");
        var q = await quiet.ConnectHostPlayerAsync();
        Assert.IsType<Snapshot>(await ReadUntilAsync<Snapshot>(q));
        await quiet.Host.EnqueueAsync(() => Task.CompletedTask);
        Assert.Null(await TryReadAsync(q)); // no LobbyState frame without the option

        await using var host = StartHost("roster", announce: true);
        var hostEnd = await host.ConnectHostPlayerAsync();
        var friend = await AttachAsync(host.Host, "Greece", greece.PlayerKey);
        var roster = await LastRosterAsync(hostEnd, r => r.Slots.Any(s => s.Nation == "Greece" && s.Connected == true));
        Assert.Equal(["Babylon", "Greece"], roster.Slots.Select(s => s.Nation));
        Assert.Equal(["Host", "Friend"], roster.Slots.Select(s => s.PlayerName));
        Assert.All(roster.Slots, s => Assert.Equal("Human", s.Kind));

        await friend.CloseAsync();
        roster = await LastRosterAsync(hostEnd, r => r.Slots.Any(s => s.Nation == "Greece" && s.Connected == false));
        Assert.True(roster.Slots[0].Connected);
    }

    private static async Task<string?> TryReadAsync(ITransport transport)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        try { return await transport.ReceiveAsync(cts.Token); }
        catch (OperationCanceledException) { return null; }
    }

    private static async Task<LobbyState> LastRosterAsync(ITransport transport, Func<LobbyState, bool> until)
    {
        while (true)
        {
            var roster = await ReadUntilAsync<LobbyState>(transport);
            if (until(roster)) return roster;
        }
    }

    // ------------------------------------------------------------ kick

    [Fact]
    public async Task Kick_DisconnectsReleasesTheSeatAndHandsTheCivToTheAi()
    {
        await using var host = StartHost("kick", announce: true);
        using var cert = HostCertificate.Generate();
        host.Listen(cert, 0);
        var hostEnd = await host.ConnectHostPlayerAsync();
        var friend = await AttachAsync(host.Host, "Greece", greece.PlayerKey);
        await ReadUntilAsync<Snapshot>(friend);

        Assert.Null(await host.KickAsync("Greece"));

        Assert.Equal(JoinRefusedCodes.Kicked, (await ReadUntilAsync<JoinRefused>(friend)).Code);
        // T267: nothing changes mid-turn; the AI takes the civ at the next boundary.
        Assert.Equal(PlayerType.Human, host.Host.Game.GetCivilization("Greece").PlayerType);
        Assert.True(host.Host.TurnTimer.HasPendingTakeover("Greece"));
        await host.Host.EnqueueAsync(() =>
        {
            host.Host.Tick();
            return Task.CompletedTask;
        });
        Assert.Equal(PlayerType.Human, host.Host.Game.GetCivilization("Greece").PlayerType);
        Assert.True(host.Host.Session.Submit("Babylon", new EndTurn()).Accepted);
        await host.Host.EnqueueAsync(() =>
        {
            host.Host.Tick();
            return Task.CompletedTask;
        });
        Assert.Equal(1, host.Host.Game.Turns);
        Assert.Equal(PlayerType.AI, host.Host.Game.GetCivilization("Greece").PlayerType); // no queued reclaim hands it back
        Assert.Equal("", host.Host.Store!.ReadMeta().Slots["Greece"].PlayerKey);
        Assert.DoesNotContain(host.Host.Sessions.Values, s => s.BoundCivId == "Greece");
        var roster = await LastRosterAsync(hostEnd, r => r.Slots.Any(s => s.Nation == "Greece" && s.Kind == "AI"));
        Assert.Null(roster.Slots.Single(s => s.Nation == "Greece").PlayerKeyId);

        // The old key cannot come back.
        var again = await TcpTlsClientTransport.ConnectAsync("127.0.0.1", host.Port!.Value, expectedFingerprint: host.Fingerprint);
        await using (again)
        {
            await again.SendAsync(ProtocolCodec.Encode(new Hello { Protocol = ProtocolVersion.Current, ClientVersion = "t", RulesetHash = "", ModList = [] }));
            var welcome = await ReadUntilAsync<Welcome>(again);
            await again.SendAsync(ProtocolCodec.Encode(new Join { PlayerKey = greece.PlayerKey, DisplayName = "x" }));
            await again.SendAsync(ProtocolCodec.Encode(new Auth { Proof = greece.Sign(welcome.HostKeyFingerprint, welcome.SessionId, welcome.Challenge) }));
            Assert.Equal(JoinRefusedCodes.GameAlreadyStarted, (await ReadUntilAsync<JoinRefused>(again)).Code);
        }
    }

    [Fact]
    public async Task Kick_RefusesTheHostsOwnSeatUnknownCivsAndUnboundSeats()
    {
        await using var host = StartHost("kick-refused");
        Assert.Equal("own-seat", await host.KickAsync("Babylon"));
        Assert.Equal("unknown-civ", await host.KickAsync("Atlantis"));
        Assert.Equal("not-bound", await host.KickAsync("Rome"));
        Assert.Equal(PlayerType.Human, host.Host.Game.GetCivilization("Babylon").PlayerType);
        Assert.Equal(greece.PlayerKey, host.Host.Store!.ReadMeta().Slots["Greece"].PlayerKey);
    }

    [Fact]
    public async Task Kick_OfTheLastLivingHumanIsRefused()
    {
        await using var host = StartHost("kick-last");
        Assert.True(host.Host.ApplyServerCommand(new SetController { CivId = "Babylon", Controller = PlayerType.AI }).Accepted);

        Assert.Equal("not-allowed", await host.KickAsync("Greece"));

        Assert.Equal(PlayerType.Human, host.Host.Game.GetCivilization("Greece").PlayerType);
        Assert.False(host.Host.TurnTimer.HasPendingTakeover("Greece"));
        Assert.Equal(greece.PlayerKey, host.Host.Store!.ReadMeta().Slots["Greece"].PlayerKey);
    }

    [Fact]
    public async Task Kick_WhileTheRoundResolvesChangesNothing()
    {
        await using var host = StartHost("kick-resolving");
        var friend = await AttachAsync(host.Host, "Greece", greece.PlayerKey);
        Assert.True(host.Host.Session.Submit("Babylon", new EndTurn()).Accepted);
        Assert.True(host.Host.Session.Submit("Greece", new EndTurn()).Accepted);
        Assert.Equal(SimultaneousPhase.Resolving, host.Host.Game.TurnState!.Phase);

        Assert.Equal("turn-resolving", await host.KickAsync("Greece"));

        Assert.Equal(greece.PlayerKey, host.Host.Store!.ReadMeta().Slots["Greece"].PlayerKey);
        Assert.Contains(host.Host.Sessions.Values, s => s.BoundCivId == "Greece");
        Assert.True(friend.IsOpen);
    }

    [Fact]
    public async Task Kick_IsNotReachableFromAClientFrame()
    {
        await using var host = StartHost("kick-remote");
        var friend = await AttachAsync(host.Host, "Greece", greece.PlayerKey);
        await ReadUntilAsync<Snapshot>(friend);
        var command = new Command
        {
            ClientCmdId = "c1",
            CommandType = "set-controller",
            Payload = System.Text.Json.JsonSerializer.SerializeToElement(new { civId = "Babylon", controller = "AI" }),
        };
        await friend.SendAsync(ProtocolCodec.Encode(command));
        var result = await ReadUntilAsync<Unciv.Net.CommandResult>(friend);
        Assert.False(result.Accepted);
        Assert.Equal(PlayerType.Human, host.Host.Game.GetCivilization("Babylon").PlayerType);
    }

    // ------------------------------------------------------------ rebind

    [Fact]
    public async Task Rebind_NewDeviceTakesTheSeatBackFromTheAi_AndTheOldKeyIsDead()
    {
        await using var host = StartHost("rebind");
        using var cert = HostCertificate.Generate();
        host.Listen(cert, 0);
        var oldDevice = await AttachAsync(host.Host, "Greece", greece.PlayerKey);
        await ReadUntilAsync<Snapshot>(oldDevice);

        Assert.Null(await host.RebindAsync("Greece", newDevice.PlayerKey));

        Assert.Equal(JoinRefusedCodes.Kicked, (await ReadUntilAsync<JoinRefused>(oldDevice)).Code);
        Assert.Equal(PlayerType.AI, host.Host.Game.GetCivilization("Greece").PlayerType);
        Assert.Equal(newDevice.PlayerKey, host.Host.Store!.ReadMeta().Slots["Greece"].PlayerKey);

        var (transport, joinOk) = await JoinAsync(host, newDevice);
        await using (transport)
        {
            Assert.IsType<JoinOk>(joinOk);
            Assert.Equal("Greece", (await ReadUntilAsync<Snapshot>(transport)).You);
            var civ = host.Host.Game.GetCivilization("Greece");
            Assert.Equal((PlayerType.Human, newDevice.PlayerKey), (civ.PlayerType, civ.PlayerId));
        }

        var (stale, refused) = await JoinAsync(host, greece);
        await using (stale) Assert.IsType<JoinRefused>(refused);
    }

    [Fact]
    public async Task Rebind_RefusalsLeaveTheSeatUntouched()
    {
        await using var host = StartHost("rebind-refused");
        Assert.Equal("own-seat", await host.RebindAsync("Babylon", newDevice.PlayerKey));
        Assert.Equal("unknown-civ", await host.RebindAsync("Atlantis", newDevice.PlayerKey));
        Assert.Equal("not-bound", await host.RebindAsync("Rome", newDevice.PlayerKey));
        Assert.Equal("bad-key", await host.RebindAsync("Greece", "not a key"));
        Assert.Equal("unchanged", await host.RebindAsync("Greece", greece.PlayerKey));
        Assert.Equal("key-in-use", await host.RebindAsync("Greece", babylon.PlayerKey));
        var civ = host.Host.Game.GetCivilization("Greece");
        Assert.Equal(PlayerType.Human, civ.PlayerType);
        Assert.Equal(greece.PlayerKey, host.Host.Store!.ReadMeta().Slots["Greece"].PlayerKey);
    }

    [Fact]
    public async Task Rebind_OfAKickedSeatGivesItToTheNewKey()
    {
        await using var host = StartHost("rebind-kicked");
        Assert.Null(await host.KickAsync("Greece"));
        Assert.Null(await host.RebindAsync("Greece", newDevice.PlayerKey));
        Assert.Equal(newDevice.PlayerKey, host.Host.Store!.ReadMeta().Slots["Greece"].PlayerKey);
        var transport = await AttachAsync(host.Host, "Greece", newDevice.PlayerKey);
        await ReadUntilAsync<Snapshot>(transport);
        Assert.Equal(PlayerType.Human, host.Host.Game.GetCivilization("Greece").PlayerType);
    }

    private static async Task<(ITransport Transport, Message Last)> JoinAsync(InProcessHost host, ClientIdentity identity)
    {
        var transport = await TcpTlsClientTransport.ConnectAsync("127.0.0.1", host.Port!.Value, expectedFingerprint: host.Fingerprint);
        await transport.SendAsync(ProtocolCodec.Encode(new Hello { Protocol = ProtocolVersion.Current, ClientVersion = "t", RulesetHash = "", ModList = [] }));
        var welcome = await ReadUntilAsync<Welcome>(transport);
        await transport.SendAsync(ProtocolCodec.Encode(new Join { PlayerKey = identity.PlayerKey, DisplayName = "x" }));
        await transport.SendAsync(ProtocolCodec.Encode(new Auth { Proof = identity.Sign(welcome.HostKeyFingerprint, welcome.SessionId, welcome.Challenge) }));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var frame = await transport.ReceiveAsync(cts.Token);
        Assert.NotNull(frame);
        return (transport, ProtocolCodec.Decode(frame));
    }
}
