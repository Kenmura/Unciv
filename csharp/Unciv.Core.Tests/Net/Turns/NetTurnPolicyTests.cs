using Unciv.Commands;
using Unciv.Commands.Turn;
using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using Unciv.Net.Turns;
using RulesetClass = Unciv.Models.Ruleset.Ruleset;

namespace Unciv.Core.Tests.Net.Turns;

[Collection("RulesetCache")]
public sealed class NetTurnPolicyTests : IDisposable
{
    private readonly RulesetClass ruleset;

    public NetTurnPolicyTests()
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

    private GameInfo NewGame(long seed = 1L, params Player[] players)
    {
        if (players.Length == 0)
            players = [new Player("Babylon", PlayerType.Human), new Player("Greece", PlayerType.Human), new Player("Rome", PlayerType.AI)];

        var game = GameStarter.StartNewGame(
            new GameSetupInfo(
                new GameParameters
                {
                    BaseRuleset = ruleset.Name,
                    NumberOfCityStates = 0,
                    VictoryTypes = ruleset.Victories.Keys.ToList(),
                    Players = players.ToList(),
                },
                new MapParameters { Type = MapType.Pangaea, Shape = MapShape.Rectangular, Seed = 3, MapSize = MapSize.Tiny }),
            seed, 1000L);

        game.StartSimultaneousTurns();
        return game;
    }

    private static GameSession Session(GameInfo game, IClock clock) =>
        new(game) { Clock = () => clock.NowMillis };

    [Fact]
    public void AllEndedResolvesOnce()
    {
        var clock = new ManualClock(10_000L);
        var game = NewGame();
        var session = Session(game, clock);
        var timer = new TurnTimer(session, TurnPolicy.Default, clock, autoResolve: true);

        Assert.Equal(0, game.Turns);
        Assert.Equal(SimultaneousPhase.Playing, game.TurnState!.Phase);

        // Both humans submit EndTurn
        Assert.True(session.Submit("Babylon", new EndTurn()).Accepted);
        Assert.True(session.Submit("Greece", new EndTurn()).Accepted);

        // When all active humans ended, resolution is triggered
        timer.Tick();

        // Round has resolved once into the next turn
        Assert.Equal(1, game.Turns);
        Assert.Equal(SimultaneousPhase.Playing, game.TurnState.Phase);
        Assert.Empty(game.TurnState.Ended);
        Assert.True(game.IsTurnStarted(game.GetCivilization("Babylon")));
        Assert.True(game.IsTurnStarted(game.GetCivilization("Greece")));
    }

    [Fact]
    public void TimerExpiresIssuesForceEndTurnInCivIndexOrder()
    {
        var clock = new ManualClock(10_000L);
        var game = NewGame();
        var session = Session(game, clock);
        var policy = new TurnPolicy { TurnTimerSeconds = 60 };
        var timer = new TurnTimer(session, policy, clock, autoResolve: true);

        Assert.Equal(70_000L, timer.TurnDeadline);

        // Advance clock by 59 seconds: timer should not expire
        clock.AdvanceSeconds(59);
        timer.Tick();
        Assert.Equal(0, game.Turns);
        Assert.Empty(game.TurnState!.Ended);

        // Advance 2 more seconds (total 61s): timer expires
        clock.AdvanceSeconds(2);
        timer.Tick();

        // Both Babylon and Greece should have received ForceEndTurn in civ-index order
        var forcedEnvelopes = session.Log
            .Where(e => e.Type == "force-end-turn")
            .Select(e => (e.Payload.TryGetProperty("CivId", out var p) ? p : e.Payload.GetProperty("civId")).GetString())
            .ToList();

        Assert.Equal(["Babylon", "Greece"], forcedEnvelopes);

        // Round resolved into turn 1
        Assert.Equal(1, game.Turns);
        Assert.Equal(SimultaneousPhase.Playing, game.TurnState.Phase);
    }

    [Fact]
    public void DynamicTimerIsMinAtZeroLoadAndRisesWithWarsAndUnits()
    {
        var game = NewGame();
        var peace = DynamicTurnTimer.Load(game);
        var seconds = DynamicTurnTimer.Compute(game, 30, 300);
        Assert.InRange(seconds, 30, 300);

        var babylon = game.GetCivilization("Babylon");
        var greece = game.GetCivilization("Greece");
        babylon.DiplomacyFunctions.MakeCivilizationsMeet(greece);
        babylon.GetDiplomacyManager(greece)!.DeclareWar();
        Assert.Equal(peace + DynamicTurnTimer.PointsPerWar, DynamicTurnTimer.Load(game));
        Assert.True(DynamicTurnTimer.Compute(game, 30, 300) > seconds);
    }

    [Fact]
    public void DynamicTimerClampsToLobbyBounds()
    {
        var game = NewGame();
        Assert.Equal(100, DynamicTurnTimer.Compute(game, 100, 100));
        Assert.Equal(50, DynamicTurnTimer.Compute(game, 100, 50));
        Assert.Equal(0, DynamicTurnTimer.Compute(game, -5, 0));
        Assert.Equal(DynamicTurnTimer.Compute(game, 0, 300), DynamicTurnTimer.Compute(game, -20, 300));
        Assert.InRange(DynamicTurnTimer.Load(game), 0, DynamicTurnTimer.FullLoad);
    }

    [Fact]
    public void DynamicTimerSetsDeadlineAndStillEndsTurnsThroughForceEndTurn()
    {
        var clock = new ManualClock(10_000L);
        var game = NewGame();
        var session = Session(game, clock);
        var policy = new TurnPolicy { TurnTimerSeconds = 300, TurnTimerMinSeconds = 30, DynamicTurnTimer = true };
        var timer = new TurnTimer(session, policy, clock, autoResolve: true);

        var expected = DynamicTurnTimer.Compute(game, 30, 300);
        Assert.Equal(10_000L + expected * 1000L, timer.TurnDeadline);

        clock.AdvanceSeconds(expected + 1);
        timer.Tick();
        Assert.Equal(1, game.Turns);
        Assert.Equal(2, session.Log.Count(e => e.Type == "force-end-turn"));
    }

    [Fact]
    public void DynamicTimerOffWhenMaximumIsZero()
    {
        var clock = new ManualClock(10_000L);
        var session = Session(NewGame(), clock);
        var timer = new TurnTimer(session, new TurnPolicy { TurnTimerSeconds = 0, DynamicTurnTimer = true }, clock);
        Assert.Null(timer.TurnDeadline);
    }

    [Fact]
    public void LobbySettingsBuildTurnPolicy()
    {
        var policy = new Unciv.Net.Lobby.LobbySettings { TurnTimerSeconds = 240, TurnTimerMinSeconds = 45, DynamicTurnTimer = true }.ToTurnPolicy();
        Assert.Equal(240, policy.TurnTimerSeconds);
        Assert.Equal(45, policy.TurnTimerMinSeconds);
        Assert.True(policy.DynamicTurnTimer);
        Assert.False(new Unciv.Net.Lobby.LobbySettings().ToTurnPolicy().DynamicTurnTimer);
    }

    [Fact]
    public void DisconnectGraceIssuesForceEndTurnAndUndosOnReturn()
    {
        var clock = new ManualClock(10_000L);
        var game = NewGame();
        var session = Session(game, clock);
        var policy = new TurnPolicy { TurnTimerSeconds = 0, DropGraceSeconds = 120 };
        var timer = new TurnTimer(session, policy, clock, autoResolve: true);

        // Greece disconnects at T = 10_000
        timer.Disconnect("Greece");
        Assert.False(timer.IsConnected("Greece"));

        // Advance 60s (grace period not expired yet)
        clock.AdvanceSeconds(60);
        timer.Tick();
        Assert.Empty(game.TurnState!.Ended);

        // Advance 61 more seconds (total 121s): grace period expired for Greece
        clock.AdvanceSeconds(61);
        timer.Tick();

        // Greece was force-ended
        Assert.Contains("Greece", game.TurnState.Ended);
        Assert.True(timer.WasForceEndedByDisconnect("Greece"));
        // Babylon has not ended, so turn is still Playing
        Assert.Equal(SimultaneousPhase.Playing, game.TurnState.Phase);

        // Greece reconnects while still Playing: undo end turn is automatically submitted
        timer.Connect("Greece");
        Assert.True(timer.IsConnected("Greece"));
        Assert.DoesNotContain("Greece", game.TurnState.Ended);
        Assert.False(timer.WasForceEndedByDisconnect("Greece"));

        // Greece can now end its turn normally
        Assert.True(session.Submit("Greece", new EndTurn()).Accepted);
        Assert.Contains("Greece", game.TurnState.Ended);
    }

    [Fact]
    public void OnDisconnectAiTakeoverAfterKAbsentTurnsAndReclaim()
    {
        var clock = new ManualClock(10_000L);
        // 3 humans so taking over 1 still leaves other humans
        var game = NewGame(1L,
            new Player("Babylon", PlayerType.Human),
            new Player("Greece", PlayerType.Human),
            new Player("Rome", PlayerType.Human));
        var session = Session(game, clock);
        var policy = new TurnPolicy
        {
            TurnTimerSeconds = 60,
            DropGraceSeconds = 30,
            OnDisconnect = DisconnectAction.AiTakeover,
            AbsentTurnsBeforeAiTakeover = 2,
        };
        var timer = new TurnTimer(session, policy, clock, autoResolve: true);

        // Greece disconnects
        timer.Disconnect("Greece");

        // Turn 0: Greece is absent, Babylon and Rome end turns, turn timer expires for Greece
        session.Submit("Babylon", new EndTurn());
        session.Submit("Rome", new EndTurn());
        clock.AdvanceSeconds(35); // grace expired
        timer.Tick();

        Assert.Equal(1, game.Turns);
        Assert.Equal(1, timer.GetAbsentTurns("Greece"));
        Assert.Equal(PlayerType.Human, game.GetCivilization("Greece").PlayerType);

        // Turn 1: Greece is still absent, Babylon and Rome end turns
        session.Submit("Babylon", new EndTurn());
        session.Submit("Rome", new EndTurn());
        clock.AdvanceSeconds(35);
        timer.Tick();

        Assert.Equal(2, game.Turns);
        // Absent turns threshold (2) reached: Greece flipped to AI
        Assert.Equal(PlayerType.AI, game.GetCivilization("Greece").PlayerType);

        // Turn 2: Greece is now AI, so it is automated in resolution
        session.Submit("Babylon", new EndTurn());
        session.Submit("Rome", new EndTurn());
        timer.Tick();
        Assert.Equal(3, game.Turns);

        // Greece player returns and reclaims
        Assert.True(timer.Reclaim("Greece", "new-greece-key"));
        Assert.Equal(PlayerType.Human, game.GetCivilization("Greece").PlayerType);
        Assert.Equal("new-greece-key", game.GetCivilization("Greece").PlayerId);
        Assert.Equal(0, timer.GetAbsentTurns("Greece"));
    }

    [Fact]
    public void LastHumanPlayerCannotBeHandedToAi()
    {
        var clock = new ManualClock(10_000L);
        // Single human with AI players
        var game = NewGame(1L,
            new Player("Babylon", PlayerType.Human),
            new Player("Rome", PlayerType.AI));
        var session = Session(game, clock);
        var policy = new TurnPolicy
        {
            TurnTimerSeconds = 60,
            DropGraceSeconds = 10,
            OnDisconnect = DisconnectAction.AiTakeover,
            AbsentTurnsBeforeAiTakeover = 1,
        };
        var timer = new TurnTimer(session, policy, clock, autoResolve: true);

        timer.Disconnect("Babylon");
        clock.AdvanceSeconds(15);
        timer.Tick();

        Assert.Equal(1, game.Turns);
        // Babylon is the last human, so SetController to AI was refused or skipped
        Assert.Equal(PlayerType.Human, game.GetCivilization("Babylon").PlayerType);
    }

    [Fact]
    public void TurnStatusMessageReflectsDeadlinePhaseAndEnded()
    {
        var clock = new ManualClock(5_000L);
        var game = NewGame();
        var session = Session(game, clock);
        var policy = new TurnPolicy { TurnTimerSeconds = 30 };
        var timer = new TurnTimer(session, policy, clock);

        var status = timer.CreateTurnStatus();
        Assert.Equal(0, status.Turn);
        Assert.Equal(35_000L, status.Deadline);
        Assert.Equal("Playing", status.Phase);
        Assert.Empty(status.Ended);

        session.Submit("Babylon", new EndTurn());
        status = timer.CreateTurnStatus();
        Assert.Equal(["Babylon"], status.Ended);
    }

    [Fact]
    public void ResolvePumpStepAndPumpMethods()
    {
        var clock = new ManualClock(1_000L);
        var game = NewGame();
        var session = Session(game, clock);

        session.Submit("Babylon", new EndTurn());
        session.Submit("Greece", new EndTurn());

        Assert.Equal(SimultaneousPhase.Resolving, game.TurnState!.Phase);

        var pump = new ResolvePump(session);
        // Step once
        var first = pump.Step();
        Assert.NotNull(first);
        Assert.True(first.Accepted);

        // Pump the rest
        var remaining = pump.Pump();
        Assert.NotEmpty(remaining);

        // Turn completed
        Assert.Equal(1, game.Turns);
        Assert.Equal(SimultaneousPhase.Playing, game.TurnState.Phase);
    }

    [Fact]
    public void PropertiesAndEdgeCases()
    {
        var clock = new ManualClock(10_000L);
        var game = NewGame();
        var session = Session(game, clock);
        var policy = new TurnPolicy { TurnTimerSeconds = 30 };
        var timer = new TurnTimer(session, policy, clock);

        Assert.Same(session, timer.Session);
        Assert.Same(policy, timer.Policy);
        Assert.Same(clock, timer.Clock);

        Assert.Equal(0, timer.GetAbsentTurns("NonExistentCiv"));
        Assert.False(timer.WasForceEndedByDisconnect("NonExistentCiv"));
        Assert.False(timer.Reclaim("NonExistentCiv"));

        Assert.Throws<ArgumentOutOfRangeException>(() => clock.Advance(-5));
        Assert.True(SystemClock.Instance.NowMillis > 0);
    }

    [Fact]
    public void ReclaimDuringResolvingQueuesForNextTurn()
    {
        var clock = new ManualClock(10_000L);
        var game = NewGame(1L,
            new Player("Babylon", PlayerType.Human),
            new Player("Greece", PlayerType.Human),
            new Player("Rome", PlayerType.Human));
        var session = Session(game, clock);
        var timer = new TurnTimer(session, TurnPolicy.Default, clock, autoResolve: false);

        // Hand Greece to AI first
        session.SubmitAsServer(new SetController { CivId = "Greece", Controller = PlayerType.AI });
        Assert.Equal(PlayerType.AI, game.GetCivilization("Greece").PlayerType);

        // End Babylon and Rome to enter Resolving
        session.Submit("Babylon", new EndTurn());
        session.Submit("Rome", new EndTurn());
        Assert.Equal(SimultaneousPhase.Resolving, game.TurnState!.Phase);

        // Reclaim Greece while in Resolving
        Assert.True(timer.Reclaim("Greece", "greece-reclaimed-key"));

        // Greece is still AI while resolving
        Assert.Equal(PlayerType.AI, game.GetCivilization("Greece").PlayerType);

        // Pump resolution to next turn
        ResolvePump.Pump(session);
        timer.Tick();

        // On next turn start, Greece was reclaimed
        Assert.Equal(1, game.Turns);
        Assert.Equal(SimultaneousPhase.Playing, game.TurnState.Phase);
        Assert.Equal(PlayerType.Human, game.GetCivilization("Greece").PlayerType);
        Assert.Equal("greece-reclaimed-key", game.GetCivilization("Greece").PlayerId);
    }

    [Fact]
    public void ConnectTriggersAutoReclaimWhenCivIsAi()
    {
        var clock = new ManualClock(10_000L);
        var game = NewGame(1L,
            new Player("Babylon", PlayerType.Human),
            new Player("Greece", PlayerType.Human),
            new Player("Rome", PlayerType.Human));
        var session = Session(game, clock);
        var timer = new TurnTimer(session, TurnPolicy.Default, clock);

        // Hand Greece to AI
        session.SubmitAsServer(new SetController { CivId = "Greece", Controller = PlayerType.AI });
        Assert.Equal(PlayerType.AI, game.GetCivilization("Greece").PlayerType);

        // Greece player connects: automatically reclaims
        timer.Connect("Greece", "reconnected-key");
        Assert.Equal(PlayerType.Human, game.GetCivilization("Greece").PlayerType);
        Assert.Equal("reconnected-key", game.GetCivilization("Greece").PlayerId);
    }
}
