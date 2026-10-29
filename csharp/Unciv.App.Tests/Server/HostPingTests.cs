using Unciv.Commands;
using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using Unciv.Logic.Map.Tiles;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using Unciv.Net;
using Unciv.Net.Identity;
using Unciv.Net.Persistence;
using Unciv.Net.Turns;
using Unciv.Server;

namespace Unciv.Core.Tests.Server;

/// <summary>C090: shared map pings against the real <see cref="GameHost"/>: who receives them, what is dropped, the rate limit, and that nothing reaches the game state.</summary>
[Collection("RulesetCache")]
public sealed class HostPingTests : IDisposable
{
    private const long Seed = 11L;
    private readonly Ruleset ruleset;

    public HostPingTests()
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
        var setup = new GameSetupInfo(
            new GameParameters
            {
                BaseRuleset = ruleset.Name,
                NumberOfCityStates = 0,
                VictoryTypes = ruleset.Victories.Keys.ToList(),
                Players = [new("Babylon", PlayerType.Human), new("Greece", PlayerType.Human), new("Rome", PlayerType.Human)],
            },
            new MapParameters { Type = MapType.Pangaea, Shape = MapShape.Rectangular, Seed = 3, MapSize = MapSize.Tiny });
        var game = GameStarter.StartNewGame(setup, Seed, 1000L);
        game.StartSimultaneousTurns();
        return game;
    }

    private static int SessionCounter;

    private static async Task<ITransport> AttachAsync(GameHost host, string civ)
    {
        var (client, hostEnd) = InMemoryTransport.CreatePair();
        await host.AttachSessionAsync("p-" + civ + "-" + Interlocked.Increment(ref SessionCounter), civ, hostEnd, ClientIdentity.Generate().PlayerKey);
        return client;
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

    /// <summary>True when a <see cref="Pong"/> comes back before any <see cref="MapPingNotice"/>: the host handles frames in order, so nothing was relayed.</summary>
    private static async Task<bool> NothingRelayedAsync(ITransport transport)
    {
        await transport.SendAsync(ProtocolCodec.Encode(new Ping()));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (true)
        {
            var frame = await transport.ReceiveAsync(cts.Token);
            Assert.NotNull(frame);
            var message = ProtocolCodec.Decode(frame);
            if (message is MapPingNotice) return false;
            if (message is Pong) return true;
        }
    }

    private static Task PingAsync(ITransport transport, int x, int y, string kind = MapPingKinds.Danger) =>
        transport.SendAsync(ProtocolCodec.Encode(new MapPing { X = x, Y = y, Kind = kind })).AsTask();

    private static void Ally(GameInfo game, string a, string b)
    {
        var first = game.GetCivilization(a);
        var second = game.GetCivilization(b);
        first.DiplomacyFunctions.MakeCivilizationsMeet(second);
        first.GetDiplomacyManager(second)!.SignDefensivePact(10);
    }

    private static Tile ExploredBy(GameInfo game, params string[] civs)
    {
        var tile = game.TileMap.TileList.OrderBy(it => it.Position.x).ThenBy(it => it.Position.y).First();
        foreach (var civ in civs) tile.SetExplored(game.GetCivilization(civ), true);
        return tile;
    }

    [Fact]
    public async Task Ping_ReachesTheSenderAndTheAllyButNotAFreeCiv_WithTheBoundName()
    {
        var game = NewGame();
        Ally(game, "Babylon", "Greece");
        var tile = ExploredBy(game, "Babylon", "Greece", "Rome");
        await using var host = new GameHost(new GameSession(game), clock: new ManualClock(1_000_000));
        var mine = await AttachAsync(host, "Babylon");
        var ally = await AttachAsync(host, "Greece");
        var stranger = await AttachAsync(host, "Rome");

        await PingAsync(mine, tile.Position.x, tile.Position.y);

        var atAlly = await ReadUntilAsync<MapPingNotice>(ally);
        Assert.Equal((tile.Position.x, tile.Position.y, MapPingKinds.Danger, "Babylon"), (atAlly.X, atAlly.Y, atAlly.Kind, atAlly.Sender));
        Assert.Equal(atAlly, await ReadUntilAsync<MapPingNotice>(mine)); // echoed to the sender
        Assert.True(await NothingRelayedAsync(stranger));
    }

    [Fact]
    public async Task Ping_IsNotRelayedToAnAllyWhoHasNotExploredTheTile()
    {
        var game = NewGame();
        Ally(game, "Babylon", "Greece");
        var tile = ExploredBy(game, "Babylon");
        Assert.False(tile.IsExplored(game.GetCivilization("Greece")));
        await using var host = new GameHost(new GameSession(game), clock: new ManualClock(1_000_000));
        var mine = await AttachAsync(host, "Babylon");
        var ally = await AttachAsync(host, "Greece");

        await PingAsync(mine, tile.Position.x, tile.Position.y);

        await ReadUntilAsync<MapPingNotice>(mine);
        Assert.True(await NothingRelayedAsync(ally));
    }

    [Fact]
    public async Task Ping_OnATileTheSenderDoesNotKnow_OffTheMap_OrOfAnUnknownKind_IsDropped()
    {
        var game = NewGame();
        Ally(game, "Babylon", "Greece");
        var babylon = game.GetCivilization("Babylon");
        var unknown = game.TileMap.TileList.OrderBy(it => it.Position.x).ThenBy(it => it.Position.y).First(it => !it.IsExplored(babylon));
        unknown.SetExplored(game.GetCivilization("Greece"), true);
        var known = game.TileMap.TileList.OrderBy(it => it.Position.x).ThenBy(it => it.Position.y).First(it => it != unknown && it.IsExplored(babylon));
        known.SetExplored(game.GetCivilization("Greece"), true);
        await using var host = new GameHost(new GameSession(game), clock: new ManualClock(1_000_000));
        var mine = await AttachAsync(host, "Babylon");
        var ally = await AttachAsync(host, "Greece");

        await PingAsync(mine, unknown.Position.x, unknown.Position.y);
        await PingAsync(mine, 9999, 9999);
        await PingAsync(mine, known.Position.x, known.Position.y, "Spam");

        Assert.True(await NothingRelayedAsync(mine));
        Assert.True(await NothingRelayedAsync(ally));
    }

    [Fact]
    public async Task Ping_RateLimitDropsTheExcessUntilTheWindowPasses()
    {
        var game = NewGame();
        var tile = ExploredBy(game, "Babylon");
        var clock = new ManualClock(1_000_000);
        await using var host = new GameHost(new GameSession(game), clock: clock, limits: new HostLimits { MaxPingPerWindow = 2, PingWindowMillis = 5_000 });
        var mine = await AttachAsync(host, "Babylon");

        for (int i = 0; i < 3; i++) await PingAsync(mine, tile.Position.x, tile.Position.y, i == 0 ? MapPingKinds.Look : i == 1 ? MapPingKinds.Danger : MapPingKinds.Help);
        Assert.Equal(MapPingKinds.Look, (await ReadUntilAsync<MapPingNotice>(mine)).Kind);
        Assert.Equal(MapPingKinds.Danger, (await ReadUntilAsync<MapPingNotice>(mine)).Kind);
        Assert.True(await NothingRelayedAsync(mine)); // the third was dropped

        clock.Advance(5_001);
        await PingAsync(mine, tile.Position.x, tile.Position.y, MapPingKinds.Help);
        Assert.Equal(MapPingKinds.Help, (await ReadUntilAsync<MapPingNotice>(mine)).Kind);
    }

    [Fact]
    public async Task Ping_IsNotACommand_TheLogAndTheStateHashDoNotMove()
    {
        var game = NewGame();
        var tile = ExploredBy(game, "Babylon");
        var gameSession = new GameSession(game);
        await using var host = new GameHost(gameSession, clock: new ManualClock(1_000_000));
        var mine = await AttachAsync(host, "Babylon");
        long seq = gameSession.LastSeq;
        int logged = gameSession.Log.Count;

        await PingAsync(mine, tile.Position.x, tile.Position.y);
        await ReadUntilAsync<MapPingNotice>(mine);

        Assert.Equal(seq, gameSession.LastSeq);
        Assert.Equal(logged, gameSession.Log.Count);
    }

    [Fact]
    public async Task Ping_SenderNameIsCleanedAndCut()
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
        var game = GameStarter.StartNewGame(setup, Seed, 1000L);
        game.StartSimultaneousTurns();
        var tile = ExploredBy(game, "Babylon");
        using var babylon = ClientIdentity.Generate();
        using var greece = ClientIdentity.Generate();
        var root = Directory.CreateTempSubdirectory("unciv-ping-").FullName;
        try
        {
            var bindings = new Dictionary<string, SlotBinding>
            {
                ["Babylon"] = new SlotBinding { PlayerKey = babylon.PlayerKey, DisplayName = "  Evil\nName\t" + new string('x', 80) },
                ["Greece"] = new SlotBinding { PlayerKey = greece.PlayerKey, DisplayName = "Friend" },
            };
            await using var host = InProcessHost.Start(game, setup, Seed, 1000L, Path.Combine(root, "g"), "Babylon", bindings, new InProcessHostOptions { TickInterval = null });
            var mine = await host.ConnectHostPlayerAsync();

            await PingAsync(mine, tile.Position.x, tile.Position.y);

            var notice = await ReadUntilAsync<MapPingNotice>(mine);
            Assert.Equal(new HostLimits().MaxPingNameChars, notice.Sender.Length);
            Assert.StartsWith("EvilName", notice.Sender);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Ping_FromASessionBoundToNoCiv_GetsNoEchoAndReachesNobody()
    {
        var game = NewGame();
        Ally(game, "Babylon", "Greece");
        var tile = ExploredBy(game, "Babylon", "Greece");
        await using var host = new GameHost(new GameSession(game), clock: new ManualClock(1_000_000));
        var ally = await AttachAsync(host, "Greece");
        var unbound = await AttachAsync(host, "Nobody");

        await PingAsync(unbound, tile.Position.x, tile.Position.y);

        Assert.True(await NothingRelayedAsync(unbound));
        Assert.True(await NothingRelayedAsync(ally));
    }
}
