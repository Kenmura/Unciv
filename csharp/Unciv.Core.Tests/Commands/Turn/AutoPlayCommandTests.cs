using System.Globalization;
using Unciv.Commands;
using Unciv.Commands.Log;
using Unciv.Commands.Turn;
using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using RulesetClass = Unciv.Models.Ruleset.Ruleset;

namespace Unciv.Core.Tests.Commands.Turn;

// T228: StartAutoPlay / StopAutoPlay / AutoPlayTurn drive GameClientState.AutoPlay (Kotlin AutoPlay.kt), also through a command log replay.
[Collection("RulesetCache")]
public sealed class AutoPlayCommandTests : IDisposable
{
    private readonly RulesetClass ruleset;

    public AutoPlayCommandTests()
    {
        RulesetCache.Clear();
        var jsons = JsonsPath();
        RulesetCache.LoadRulesets(new DirectoryRulesetFileSource(jsons, Path.Combine(jsons, "no mods here")), noMods: true);
        ruleset = RulesetCache.GetVanillaRuleset();
    }

    public void Dispose() => RulesetCache.Clear();

    private static string JsonsPath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "android", "assets", "jsons");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException("android/assets/jsons not found above " + AppContext.BaseDirectory);
    }

    private GameSetupInfo Setup() => new(
        new GameParameters
        {
            BaseRuleset = ruleset.Name,
            NumberOfCityStates = 0,
            VictoryTypes = ruleset.Victories.Keys.ToList(),
            Players = [new Player("Babylon", PlayerType.Human), new Player("Greece", PlayerType.AI), new Player("Rome", PlayerType.AI)],
        },
        new MapParameters { Type = MapType.Pangaea, Shape = MapShape.Rectangular, Seed = 3, MapSize = MapSize.Tiny });

    private static void Ok(CommandResult result) => Assert.True(result.Accepted, result.Validation.Message);

    private static void Rejected(CommandResult result, string code)
    {
        Assert.False(result.Accepted);
        Assert.Equal(code, result.Validation.Code);
    }

    private static void RunOthers(GameSession session, CommandLogWriter? writer = null)
    {
        while (true)
        {
            switch (session.Game.TurnPhase)
            {
                case TurnPhase.AutoPlayers auto:
                    Ok(session.SubmitAsServer(new RunAiTurn { CivId = auto.NextCiv.CivId }));
                    break;
                case TurnPhase.AwaitingInput waiting when !session.Game.IsTurnStarted(waiting.Civ):
                    Ok(session.SubmitAsServer(new BeginTurn { CivId = waiting.Civ.CivId }));
                    writer?.Sync(session);
                    writer?.WriteCheckpoint(session.LastSeq, session.Game.Turns, ReplayVerifier.FullHash(session.Game));
                    break;
                default:
                    writer?.Sync(session);
                    return;
            }
        }
    }

    private sealed class MinimalAutoPlay : IAutoPlay
    {
        public bool IsAutoPlaying() => true;
        public void StopAutoPlay() { }
    }

    [Fact]
    public void AClientsOwnAutoPlayIsNeverFullAiNorACivsAutoPlayByDefault()
    {
        IAutoPlay autoPlay = new MinimalAutoPlay();
        Assert.False(autoPlay.IsAutoPlayingAndFullAutoPlayAI());
        Assert.False(autoPlay.IsAutoPlayingFor("Babylon"));
    }

    [Fact]
    public void CommandsAreRegistered()
    {
        var names = GameCommandRegistry.Scan().ToDictionary(it => it.Name, it => it.CommandClass);
        Assert.Equal(typeof(StartAutoPlay), names["start-auto-play"]);
        Assert.Equal(typeof(StopAutoPlay), names["stop-auto-play"]);
        Assert.Equal(typeof(AutoPlayTurn), names["auto-play-turn"]);
    }

    [Fact]
    public void AutoPlaysTheRequestedNumberOfTurnsAndThenStops()
    {
        var game = GameStarter.StartNewGame(Setup(), 1L, 1000L);
        var session = new GameSession(game) { Clock = ScriptedHuman.DeterministicClock(2000L) };
        var babylon = game.GetCivilization("Babylon");
        Assert.False(babylon.IsAIOrAutoPlaying());
        Rejected(session.Submit("Babylon", new AutoPlayTurn()), CommandErrors.NotAllowed); // not started

        Ok(session.Submit("Babylon", new StartAutoPlay { Turns = 2, FullAutoPlayAI = true }));
        Assert.True(babylon.IsAIOrAutoPlaying());
        Assert.False(game.GetCivilization("Greece").IsHuman());
        Assert.True(game.ClientState.IsFullAutoPlayAI);
        Rejected(session.Submit("Babylon", new StartAutoPlay()), CommandErrors.NotAllowed); // already running

        var turn = game.Turns;
        for (var i = 0; i < 2; i++)
        {
            Ok(session.Submit("Babylon", new AutoPlayTurn()));
            RunOthers(session);
        }
        Assert.Equal(turn + 2, game.Turns);
        Assert.False(babylon.IsAIOrAutoPlaying());
        Assert.False(game.ClientState.IsFullAutoPlayAI);
        Rejected(session.Submit("Babylon", new AutoPlayTurn()), CommandErrors.NotAllowed);
    }

    [Fact]
    public void StopAndUntilEndAndNotFullAi()
    {
        var game = GameStarter.StartNewGame(Setup(), 1L, 1000L);
        var session = new GameSession(game) { Clock = ScriptedHuman.DeterministicClock(2000L) };
        Rejected(session.Submit("Babylon", new StopAutoPlay()), CommandErrors.NotAllowed);
        Rejected(session.Submit("Greece", new StartAutoPlay()), CommandErrors.NotHuman);
        Rejected(session.Submit("Babylon", new StartAutoPlay { Turns = 0 }), CommandErrors.InvalidTarget);

        Ok(session.Submit("Babylon", new StartAutoPlay { Turns = 1, FullAutoPlayAI = false, UntilEnd = true }));
        Assert.False(game.ClientState.IsFullAutoPlayAI);
        Ok(session.Submit("Babylon", new AutoPlayTurn()));
        RunOthers(session);
        Assert.True(game.GetCivilization("Babylon").IsAIOrAutoPlaying()); // until the end: the turn counter is not used up
        Ok(session.Submit("Babylon", new StopAutoPlay()));
        Assert.False(game.GetCivilization("Babylon").IsAIOrAutoPlaying());
        Rejected(session.Submit("Babylon", new StopAutoPlay()), CommandErrors.NotAllowed);
    }

    [Fact]
    public void AutoPlayStopsWhenItsOwnTurnWinsTheGame()
    {
        var game = GameStarter.StartNewGame(Setup(), 1L, 1000L);
        var session = new GameSession(game) { Clock = ScriptedHuman.DeterministicClock(2000L) };
        Ok(session.Submit("Babylon", new StartAutoPlay { Turns = 5 }));
        foreach (var rival in game.Civilizations.Where(it => it.CivId is "Greece" or "Rome"))
            foreach (var unit in rival.Units.GetCivUnits().ToList()) unit.Destroy();
        Ok(session.Submit("Babylon", new AutoPlayTurn()));
        Assert.NotNull(game.VictoryData);
        Assert.False(game.ClientState.AutoPlay!.IsAutoPlaying());
    }

    [Fact]
    public void AutoPlayStopsWhenTheGameIsWon()
    {
        var game = GameStarter.StartNewGame(Setup(), 1L, 1000L);
        var session = new GameSession(game) { Clock = ScriptedHuman.DeterministicClock(2000L) };
        Ok(session.Submit("Babylon", new StartAutoPlay { Turns = 5 }));
        game.VictoryData = new VictoryData { WinningCiv = "Greece", VictoryType = "Scientific", VictoryTurn = 1 };
        Rejected(session.Submit("Babylon", new AutoPlayTurn()), CommandErrors.GameOver);
        Ok(session.Submit("Babylon", new StopAutoPlay()));
    }

    [Fact]
    public void SimultaneousGamesNeedTheHostsPermission()
    {
        var game = GameStarter.StartNewGame(Setup(), 1L, 1000L);
        var session = new GameSession(game);
        game.StartSimultaneousTurns();
        Rejected(session.Submit("Babylon", new StartAutoPlay()), CommandErrors.NotAllowed);
        game.TurnState!.AutoPlayAllowed = true;
        Ok(session.Submit("Babylon", new StartAutoPlay()));
        // T202: an auto-played turn in a simultaneous round only marks the civ as ended
        Ok(session.Submit("Babylon", new AutoPlayTurn { Now = 5000 }));
        Assert.True(game.HasEndedTurn(game.GetCivilization("Babylon")));
        Rejected(session.Submit("Babylon", new AutoPlayTurn { Now = 5000 }), CommandErrors.TurnResolving); // Babylon was the only human, so its end started the resolution
        Assert.True(game.TurnState!.Clone().AutoPlayAllowed);
    }

    [Fact]
    public void ReplayOfALogWithAutoPlayGivesTheLiveHashes()
    {
        var setup = Setup();
        var header = CommandLogHeader.Create(setup, 1L, 1000L);
        var game = GameStarter.StartNewGame(setup, 1L, 1000L);
        var session = new GameSession(game) { Clock = ScriptedHuman.DeterministicClock(2000L) };
        var text = new StringWriter(CultureInfo.InvariantCulture);
        var writer = new CommandLogWriter(text, header);
        writer.WriteCheckpoint(session.LastSeq, game.Turns, ReplayVerifier.FullHash(game));

        Ok(session.Submit("Babylon", new StartAutoPlay { Turns = 3 }));
        for (var i = 0; i < 3; i++)
        {
            Ok(session.Submit("Babylon", new AutoPlayTurn()));
            RunOthers(session, writer);
        }
        Ok(session.Submit("Babylon", new StartAutoPlay { Turns = 1 }));
        Ok(session.Submit("Babylon", new StopAutoPlay()));
        writer.Sync(session);

        var log = CommandLogReader.Read(text.ToString());
        Assert.Contains(log.Lines, it => it is CommandLogLine.Command { Envelope.Type: "auto-play-turn" });
        var result = ReplayVerifier.Verify(log);
        Assert.True(result.Matches, $"diverged {result.DivergedAtSeq}");
        Assert.Equal(ReplayVerifier.FullHash(game), result.FinalHash);
    }
}
