using System.Text.Json;
using Unciv.Commands;
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
public sealed class CommandGateTests : IDisposable
{
    private readonly RulesetClass ruleset;

    public CommandGateTests()
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
    public void ActorInjection_AlwaysUsesBoundCivId()
    {
        var game = NewGame();
        var gate = new CommandGate();

        var cmd = new Command
        {
            ClientCmdId = "c1",
            CommandType = "end-turn",
            Payload = JsonSerializer.SerializeToElement(new { ActorId = "Rome", Other = "Hacked" })
        };

        var result = gate.Filter("s1", "Babylon", cmd, game);
        Assert.Equal(CommandGateResultKind.Accepted, result.Kind);
        Assert.NotNull(result.Envelope);
        Assert.Equal("Babylon", result.Envelope.ActorId);
    }

    [Fact]
    public void SanitizeValidation_CollapsesExistenceOracles()
    {
        var notYourUnit = CommandValidation.Fail(CommandErrors.NotYourUnit, "Unit 123 does not belong to Babylon");
        var unknownUnit = CommandValidation.Fail(CommandErrors.UnknownUnit, "There is no unit 123");

        var s1 = CommandGate.SanitizeValidation(notYourUnit);
        var s2 = CommandGate.SanitizeValidation(unknownUnit);

        Assert.Equal(CommandErrors.NotAllowed, s1.Code);
        Assert.Equal("Action not allowed", s1.Message);
        Assert.Equal(s1.Code, s2.Code);
        Assert.Equal(s1.Message, s2.Message);

        var notYourCity = CommandValidation.Fail(CommandErrors.NotYourCity, "City Athens does not belong to Babylon");
        var unknownCity = CommandValidation.Fail(CommandErrors.UnknownCity, "There is no city Athens");

        var c1 = CommandGate.SanitizeValidation(notYourCity);
        var c2 = CommandGate.SanitizeValidation(unknownCity);

        Assert.Equal(CommandErrors.NotAllowed, c1.Code);
        Assert.Equal("Action not allowed", c1.Message);
        Assert.Equal(c1.Code, c2.Code);
        Assert.Equal(c1.Message, c2.Message);

        var unknownTile = CommandValidation.Fail(CommandErrors.UnknownTile, "There is no tile at 99,99");
        var t1 = CommandGate.SanitizeValidation(unknownTile);
        Assert.Equal(CommandErrors.NotAllowed, t1.Code);
        Assert.Equal("Action not allowed", t1.Message);

        // Non-oracle errors stay untouched
        var alreadyEnded = CommandValidation.Fail(CommandErrors.AlreadyEnded, "Already ended turn");
        var a1 = CommandGate.SanitizeValidation(alreadyEnded);
        Assert.Equal(CommandErrors.AlreadyEnded, a1.Code);
        Assert.Equal("Already ended turn", a1.Message);
    }

    [Fact]
    public void RequireActive_AllowsAllowedCommandsWhenEnded()
    {
        var game = NewGame();
        var gate = new CommandGate();

        game.EndHumanTurn(game.GetCivilization("Babylon"));

        // Regular command rejected when ended
        var regularCmd = new Command
        {
            ClientCmdId = "c1",
            CommandType = "set-research-queue",
            Payload = JsonSerializer.SerializeToElement(new { Techs = new[] { "Pottery" } })
        };
        var r1 = gate.Filter("s1", "Babylon", regularCmd, game);
        Assert.Equal(CommandGateResultKind.Rejected, r1.Kind);
        Assert.Equal(CommandErrors.AlreadyEnded, r1.Code);

        // undo-end-turn allowed when ended
        var undoCmd = new Command
        {
            ClientCmdId = "c2",
            CommandType = "undo-end-turn",
            Payload = JsonSerializer.SerializeToElement(new { })
        };
        var r2 = gate.Filter("s1", "Babylon", undoCmd, game);
        Assert.Equal(CommandGateResultKind.Accepted, r2.Kind);

        // respond-to-trade-request allowed when ended
        var tradeCmd = new Command
        {
            ClientCmdId = "c3",
            CommandType = "respond-to-trade-request",
            Payload = JsonSerializer.SerializeToElement(new { RequestId = "req-1", Accepted = false })
        };
        var r3 = gate.Filter("s1", "Babylon", tradeCmd, game);
        Assert.Equal(CommandGateResultKind.Accepted, r3.Kind);
    }

    [Fact]
    public void RequireActive_RejectsWhenResolving()
    {
        var game = NewGame();
        var gate = new CommandGate();

        game.EndHumanTurn(game.GetCivilization("Babylon"));
        game.EndHumanTurn(game.GetCivilization("Greece"));
        game.BeginResolvingIfAllEnded();

        var cmd = new Command
        {
            ClientCmdId = "c1",
            CommandType = "set-research-queue",
            Payload = JsonSerializer.SerializeToElement(new { Techs = new[] { "Pottery" } })
        };
        var result = gate.Filter("s1", "Babylon", cmd, game);
        Assert.Equal(CommandGateResultKind.Rejected, result.Kind);
        Assert.Equal(CommandErrors.TurnResolving, result.Code);
    }

    [Fact]
    public void RateLimit_ReplenishesOverTime()
    {
        var game = NewGame();
        var clock = new ManualClock(1000L);
        var limits = new CommandGateLimits(Capacity: 1, RefillPerSecond: 10);
        var gate = new CommandGate(null, limits, clock);

        var cmd = new Command
        {
            ClientCmdId = "c1",
            CommandType = "end-turn",
            Payload = JsonSerializer.SerializeToElement(new { })
        };

        // 1st passes
        var r1 = gate.Filter("s1", "Babylon", cmd, game);
        Assert.Equal(CommandGateResultKind.Accepted, r1.Kind);

        // 2nd immediately rejected
        cmd = cmd with { ClientCmdId = "c2" };
        var r2 = gate.Filter("s1", "Babylon", cmd, game);
        Assert.Equal(CommandGateResultKind.Rejected, r2.Kind);
        Assert.Equal(QueryWireCodes.RateLimited, r2.Code);

        // Advance clock by 200ms (refills 2 tokens)
        clock.Advance(200);
        cmd = cmd with { ClientCmdId = "c3" };
        var r3 = gate.Filter("s1", "Babylon", cmd, game);
        Assert.Equal(CommandGateResultKind.Accepted, r3.Kind);
    }
}
