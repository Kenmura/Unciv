using System.Text.Json;
using Unciv.Commands;
using Unciv.Commands.Empire;
using Unciv.Commands.Turn;
using Unciv.Commands.Units;
using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using Unciv.Net;
using Unciv.Net.Remote;
using Unciv.Net.Turns;
using Unciv.Server;
using RulesetClass = Unciv.Models.Ruleset.Ruleset;

namespace Unciv.Core.Tests.Server;

[Collection("RulesetCache")]
public sealed class GameHostTests : IDisposable
{
    private readonly RulesetClass ruleset;

    public GameHostTests()
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

    private GameInfo NewGame(long seed = 1L)
    {
        var players = new List<Player>
        {
            new("Babylon", PlayerType.Human),
            new("Greece", PlayerType.Human),
            new("Rome", PlayerType.AI)
        };

        var game = GameStarter.StartNewGame(
            new GameSetupInfo(
                new GameParameters
                {
                    BaseRuleset = ruleset.Name,
                    NumberOfCityStates = 0,
                    VictoryTypes = ruleset.Victories.Keys.ToList(),
                    Players = players,
                },
                new MapParameters { Type = MapType.Pangaea, Shape = MapShape.Rectangular, Seed = 3, MapSize = MapSize.Tiny }),
            seed, 1000L);

        game.StartSimultaneousTurns();
        return game;
    }

    [Fact]
    public async Task AttachSession_SendsInitialSnapshot_AndSetsPreviousSnapshot()
    {
        var game = NewGame();
        var clock = new ManualClock(1000L);
        var session = new GameSession(game) { Clock = () => clock.NowMillis };
        await using var host = new GameHost(session, clock: clock);

        var (clientTransport, hostTransport) = InMemoryTransport.CreatePair();
        await host.AttachSessionAsync("sess-1", "Babylon", hostTransport);

        string? frame = await clientTransport.ReceiveAsync();
        Assert.NotNull(frame);
        var message = ProtocolCodec.Decode(frame);
        var snapshot = Assert.IsType<Snapshot>(message);

        Assert.Equal("Babylon", snapshot.You);
        Assert.Equal(game.Turns, snapshot.Turn);
        Assert.Equal("Playing", snapshot.Phase);
        Assert.NotEmpty(snapshot.Civs);
        Assert.NotEmpty(snapshot.Tiles);
        Assert.NotNull(snapshot.ViewHash);

        Assert.True(host.Sessions.TryGetValue("sess-1", out var hostSession));
        Assert.NotNull(hostSession.PreviousSnapshot);
        Assert.Equal(snapshot.ViewHash, hostSession.PreviousSnapshot.ViewHash);
    }

    [Fact]
    public async Task CommandSubmission_ValidCommand_BroadcastsViewDeltaWithDiffAndHash()
    {
        var game = NewGame();
        var clock = new ManualClock(1000L);
        var session = new GameSession(game) { Clock = () => clock.NowMillis };
        await using var host = new GameHost(session, clock: clock);

        var (clientA, hostA) = InMemoryTransport.CreatePair();
        var (clientB, hostB) = InMemoryTransport.CreatePair();
        await host.AttachSessionAsync("sess-A", "Babylon", hostA);
        await host.AttachSessionAsync("sess-B", "Greece", hostB);

        // Consume initial snapshots
        await clientA.ReceiveAsync();
        await clientB.ReceiveAsync();

        // Babylon chooses a research tech
        var tech = ruleset.Technologies.Keys.First(k => !game.GetCivilization("Babylon").Tech.IsResearched(k));
        var commandMsg = new Command
        {
            ClientCmdId = "cmd-1",
            CommandType = "set-research-queue",
            Payload = JsonSerializer.SerializeToElement(new SetResearchQueue { Techs = [tech] })
        };

        await clientA.SendAsync(ProtocolCodec.Encode(commandMsg));

        // Client A gets CommandResult
        string? resultFrame = await clientA.ReceiveAsync();
        Assert.NotNull(resultFrame);
        var cmdResult = Assert.IsType<Unciv.Net.CommandResult>(ProtocolCodec.Decode(resultFrame));
        Assert.Equal("cmd-1", cmdResult.ClientCmdId);
        Assert.True(cmdResult.Accepted);
        Assert.NotNull(cmdResult.Seq);

        // Both clients get ViewDelta with non-null Diff and ViewHash
        string? deltaAFrame = await clientA.ReceiveAsync();
        Assert.NotNull(deltaAFrame);
        var deltaA = Assert.IsType<ViewDelta>(ProtocolCodec.Decode(deltaAFrame));
        Assert.NotNull(deltaA.Diff);
        Assert.NotNull(deltaA.ViewHash);

        string? deltaBFrame = await clientB.ReceiveAsync();
        Assert.NotNull(deltaBFrame);
        var deltaB = Assert.IsType<ViewDelta>(ProtocolCodec.Decode(deltaBFrame));
        Assert.NotNull(deltaB.Diff);
        Assert.NotNull(deltaB.ViewHash);
    }

    [Fact]
    public async Task CommandGate_Dedup_ReturnsCachedOutcome_AndDoesNotReapply()
    {
        var game = NewGame();
        var clock = new ManualClock(1000L);
        var session = new GameSession(game) { Clock = () => clock.NowMillis };
        await using var host = new GameHost(session, clock: clock);

        var (client, hostTransport) = InMemoryTransport.CreatePair();
        await host.AttachSessionAsync("sess-1", "Babylon", hostTransport);
        await client.ReceiveAsync(); // snapshot

        var tech = ruleset.Technologies.Keys.First(k => !game.GetCivilization("Babylon").Tech.IsResearched(k));
        var commandMsg = new Command
        {
            ClientCmdId = "dedup-cmd",
            CommandType = "set-research-queue",
            Payload = JsonSerializer.SerializeToElement(new SetResearchQueue { Techs = [tech] })
        };

        // First submit
        await client.SendAsync(ProtocolCodec.Encode(commandMsg));
        var res1 = Assert.IsType<Unciv.Net.CommandResult>(ProtocolCodec.Decode((await client.ReceiveAsync())!));
        Assert.True(res1.Accepted);
        long originalSeq = res1.Seq!.Value;
        await client.ReceiveAsync(); // delta

        // Second submit with same ClientCmdId
        await client.SendAsync(ProtocolCodec.Encode(commandMsg));
        var res2 = Assert.IsType<Unciv.Net.CommandResult>(ProtocolCodec.Decode((await client.ReceiveAsync())!));
        Assert.True(res2.Accepted);
        Assert.Equal(originalSeq, res2.Seq);

        // Seq has not increased
        Assert.Equal(originalSeq, host.Session.LastSeq);
    }

    [Fact]
    public async Task CommandGate_RequireActive_RejectsWhenAlreadyEnded()
    {
        var game = NewGame();
        var clock = new ManualClock(1000L);
        var session = new GameSession(game) { Clock = () => clock.NowMillis };
        await using var host = new GameHost(session, clock: clock);

        var (client, hostTransport) = InMemoryTransport.CreatePair();
        await host.AttachSessionAsync("sess-1", "Babylon", hostTransport);
        await client.ReceiveAsync(); // snapshot

        // End Babylon's turn
        var endTurnMsg = new Command
        {
            ClientCmdId = "end-1",
            CommandType = "end-turn",
            Payload = JsonSerializer.SerializeToElement(new EndTurn())
        };
        await client.SendAsync(ProtocolCodec.Encode(endTurnMsg));
        var endRes = Assert.IsType<Unciv.Net.CommandResult>(ProtocolCodec.Decode((await client.ReceiveAsync())!));
        Assert.True(endRes.Accepted);
        await client.ReceiveAsync(); // delta

        // Try submitting research command after turn ended
        var tech = ruleset.Technologies.Keys.First(k => !game.GetCivilization("Babylon").Tech.IsResearched(k));
        var invalidCmd = new Command
        {
            ClientCmdId = "cmd-after-end",
            CommandType = "set-research-queue",
            Payload = JsonSerializer.SerializeToElement(new SetResearchQueue { Techs = [tech] })
        };
        await client.SendAsync(ProtocolCodec.Encode(invalidCmd));
        var rejRes = Assert.IsType<Unciv.Net.CommandResult>(ProtocolCodec.Decode((await client.ReceiveAsync())!));
        Assert.False(rejRes.Accepted);
        Assert.Equal(CommandErrors.AlreadyEnded, rejRes.Code);
    }

    [Fact]
    public async Task CommandGate_ExistenceOracle_CollapsesNotYourUnitAndUnknownUnit()
    {
        var game = NewGame();
        var clock = new ManualClock(1000L);
        var session = new GameSession(game) { Clock = () => clock.NowMillis };
        await using var host = new GameHost(session, clock: clock);

        var (client, hostTransport) = InMemoryTransport.CreatePair();
        await host.AttachSessionAsync("sess-1", "Babylon", hostTransport);
        await client.ReceiveAsync(); // snapshot

        // 1. Reference an unknown unit ID
        var unknownUnitCmd = new Command
        {
            ClientCmdId = "unknown-unit-cmd",
            CommandType = "disband-unit",
            Payload = JsonSerializer.SerializeToElement(new DisbandUnit { UnitId = 999999 })
        };
        await client.SendAsync(ProtocolCodec.Encode(unknownUnitCmd));
        var rej1 = Assert.IsType<Unciv.Net.CommandResult>(ProtocolCodec.Decode((await client.ReceiveAsync())!));
        Assert.False(rej1.Accepted);

        // 2. Reference an unowned unit ID belonging to Rome
        var romeUnit = game.GetCivilization("Rome").Units.GetCivUnits().First();
        var notYourUnitCmd = new Command
        {
            ClientCmdId = "not-your-unit-cmd",
            CommandType = "disband-unit",
            Payload = JsonSerializer.SerializeToElement(new DisbandUnit { UnitId = romeUnit.Id })
        };
        await client.SendAsync(ProtocolCodec.Encode(notYourUnitCmd));
        var rej2 = Assert.IsType<Unciv.Net.CommandResult>(ProtocolCodec.Decode((await client.ReceiveAsync())!));
        Assert.False(rej2.Accepted);

        // Both must return the EXACT same sanitized code
        Assert.Equal(CommandErrors.NotAllowed, rej1.Code);
        Assert.Equal(CommandErrors.NotAllowed, rej2.Code);
        Assert.Equal(rej1.Code, rej2.Code);
    }

    [Fact]
    public async Task CommandGate_RateAndSizeLimits_RejectAnomalies()
    {
        var game = NewGame();
        var clock = new ManualClock(1000L);
        var session = new GameSession(game) { Clock = () => clock.NowMillis };

        var gateLimits = new CommandGateLimits(MaxPayloadBytes: 100, Capacity: 2, RefillPerSecond: 1);
        var gate = new CommandGate(null, gateLimits, clock);
        await using var host = new GameHost(session, gate: gate, clock: clock);

        var (client, hostTransport) = InMemoryTransport.CreatePair();
        await host.AttachSessionAsync("sess-1", "Babylon", hostTransport);
        await client.ReceiveAsync(); // snapshot

        // 1. Oversized payload
        var hugePayload = new string('x', 200);
        var oversizedCmd = new Command
        {
            ClientCmdId = "huge-cmd",
            CommandType = "set-research-queue",
            Payload = JsonSerializer.SerializeToElement(new { Data = hugePayload })
        };
        await client.SendAsync(ProtocolCodec.Encode(oversizedCmd));
        var sizeRej = Assert.IsType<Unciv.Net.CommandResult>(ProtocolCodec.Decode((await client.ReceiveAsync())!));
        Assert.False(sizeRej.Accepted);
        Assert.Equal(ErrorCodes.FrameTooLarge, sizeRej.Code);

        // 2. Rate limiting
        var tech = ruleset.Technologies.Keys.First(k => !game.GetCivilization("Babylon").Tech.IsResearched(k));
        for (int i = 0; i < 2; i++)
        {
            var cmd = new Command
            {
                ClientCmdId = $"rate-{i}",
                CommandType = "set-research-queue",
                Payload = JsonSerializer.SerializeToElement(new SetResearchQueue { Techs = [tech] })
            };
            await client.SendAsync(ProtocolCodec.Encode(cmd));
            await client.ReceiveAsync(); // result
            await client.ReceiveAsync(); // delta
        }

        // 3rd command exceeds capacity
        var excessCmd = new Command
        {
            ClientCmdId = "rate-overflow",
            CommandType = "set-research-queue",
            Payload = JsonSerializer.SerializeToElement(new SetResearchQueue { Techs = [tech] })
        };
        await client.SendAsync(ProtocolCodec.Encode(excessCmd));
        var rateRej = Assert.IsType<Unciv.Net.CommandResult>(ProtocolCodec.Decode((await client.ReceiveAsync())!));
        Assert.False(rateRej.Accepted);
        Assert.Equal(QueryWireCodes.RateLimited, rateRej.Code);
    }

    [Fact]
    public async Task RemoteQuery_HandledOnGameActor_ReturnsQueryResult()
    {
        var game = NewGame();
        var clock = new ManualClock(1000L);
        var session = new GameSession(game) { Clock = () => clock.NowMillis };
        await using var host = new GameHost(session, clock: clock);

        var (client, hostTransport) = InMemoryTransport.CreatePair();
        await host.AttachSessionAsync("sess-1", "Babylon", hostTransport);
        await client.ReceiveAsync(); // snapshot

        var queryMsg = new Query
        {
            QueryId = 42,
            QueryType = "empire.TopBar",
            Payload = JsonSerializer.SerializeToElement(new { })
        };

        await client.SendAsync(ProtocolCodec.Encode(queryMsg));
        string? responseFrame = await client.ReceiveAsync();
        Assert.NotNull(responseFrame);

        var queryResult = Assert.IsType<QueryResult>(ProtocolCodec.Decode(responseFrame));
        Assert.Equal(42, queryResult.QueryId);
        Assert.True(queryResult.Ok);
        Assert.NotNull(queryResult.Payload);
    }

    [Fact]
    public async Task TurnResolution_WhenAllHumansEnd_PumpsResolveStepsAndAdvancesTurn()
    {
        var game = NewGame();
        var clock = new ManualClock(1000L);
        var session = new GameSession(game) { Clock = () => clock.NowMillis };
        await using var host = new GameHost(session, clock: clock);

        var (clientA, hostA) = InMemoryTransport.CreatePair();
        var (clientB, hostB) = InMemoryTransport.CreatePair();
        await host.AttachSessionAsync("sess-A", "Babylon", hostA);
        await host.AttachSessionAsync("sess-B", "Greece", hostB);

        await clientA.ReceiveAsync(); // snapshot A
        await clientB.ReceiveAsync(); // snapshot B

        int initialTurn = game.Turns;

        // Human 1 ends turn
        await clientA.SendAsync(ProtocolCodec.Encode(new Command
        {
            ClientCmdId = "end-A",
            CommandType = "end-turn",
            Payload = JsonSerializer.SerializeToElement(new EndTurn())
        }));

        await clientA.ReceiveAsync(); // cmd result A
        await clientA.ReceiveAsync(); // delta A
        await clientB.ReceiveAsync(); // delta B

        Assert.Equal(initialTurn, game.Turns);

        // Human 2 ends turn -> triggers resolution!
        await clientB.SendAsync(ProtocolCodec.Encode(new Command
        {
            ClientCmdId = "end-B",
            CommandType = "end-turn",
            Payload = JsonSerializer.SerializeToElement(new EndTurn())
        }));

        await clientB.ReceiveAsync(); // cmd result B

        // Drain messages until turn resolution completes and phase returns to Playing
        while (game.TurnState?.Phase == SimultaneousPhase.Resolving || game.Turns == initialTurn)
        {
            await Task.Delay(10);
        }

        Assert.True(game.Turns > initialTurn);
        Assert.Equal(SimultaneousPhase.Playing, game.TurnState?.Phase);
    }

    [Fact]
    public async Task Disconnect_UpdatesTurnTimerAndEndsQueries()
    {
        var game = NewGame();
        var clock = new ManualClock(1000L);
        var session = new GameSession(game) { Clock = () => clock.NowMillis };
        await using var host = new GameHost(session, clock: clock);

        var (client, hostTransport) = InMemoryTransport.CreatePair();
        await host.AttachSessionAsync("sess-1", "Babylon", hostTransport);
        await client.ReceiveAsync(); // snapshot

        Assert.True(host.TurnTimer.IsConnected("Babylon"));

        // Babylon leaves
        await client.SendAsync(ProtocolCodec.Encode(new Leave()));

        // Allow actor loop to process detachment
        while (host.Sessions.ContainsKey("sess-1"))
        {
            await Task.Delay(10);
        }

        Assert.False(host.TurnTimer.IsConnected("Babylon"));
        Assert.False(host.Sessions.ContainsKey("sess-1"));
    }

    [Fact]
    public async Task ResumeAndResync_ReturnsFreshSnapshot()
    {
        var game = NewGame();
        var clock = new ManualClock(1000L);
        var session = new GameSession(game) { Clock = () => clock.NowMillis };
        await using var host = new GameHost(session, clock: clock);

        var (client, hostTransport) = InMemoryTransport.CreatePair();
        await host.AttachSessionAsync("sess-1", "Babylon", hostTransport);
        await client.ReceiveAsync(); // initial snapshot

        // 1. ResyncRequest
        await client.SendAsync(ProtocolCodec.Encode(new ResyncRequest()));
        var resyncSnap = Assert.IsType<Snapshot>(ProtocolCodec.Decode((await client.ReceiveAsync())!));
        Assert.Equal("Babylon", resyncSnap.You);

        // 2. Resume with matching gameId
        await client.SendAsync(ProtocolCodec.Encode(new Resume
        {
            GameId = game.GameId,
            LastSeq = 0,
            LastViewHash = "hash"
        }));
        var resumeSnap = Assert.IsType<Snapshot>(ProtocolCodec.Decode((await client.ReceiveAsync())!));
        Assert.Equal("Babylon", resumeSnap.You);
    }

    [Fact]
    public void Program_Main_ReturnsZeroForHelpAndDefault()
    {
        Assert.Equal(0, Program.Main(["--help"]));
        Assert.Equal(0, Program.Main(["-h"]));
        Assert.Equal(0, Program.Main([]));
    }
}
