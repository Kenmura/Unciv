using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Logic.Files;
using Unciv.Logic.Map;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using RulesetClass = Unciv.Models.Ruleset.Ruleset;

namespace Unciv.Core.Tests.Logic;

// T184: the phased turn API. The state-hash equivalence with NextTurn is in EndToEndDeterminismTests.
[Collection("RulesetCache")]
public sealed class TurnPhaseTests : IDisposable
{
    private readonly RulesetClass ruleset;

    public TurnPhaseTests()
    {
        RulesetCache.Clear();
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

    private GameInfo NewGame(bool online = false, params (string Nation, PlayerType Type)[] players)
    {
        if (players.Length == 0) players = [("Babylon", PlayerType.Human), ("Greece", PlayerType.AI), ("Rome", PlayerType.AI)];
        return GameStarter.StartNewGame(
            new GameSetupInfo(
                new GameParameters
                {
                    BaseRuleset = ruleset.Name,
                    NumberOfCityStates = 0,
                    IsOnlineMultiplayer = online,
                    VictoryTypes = ruleset.Victories.Keys.ToList(),
                    Players = players.Select(it => new Player(it.Nation, it.Type)).ToList(),
                },
                new MapParameters { Type = MapType.Pangaea, Shape = MapShape.Rectangular, Seed = 3, MapSize = MapSize.Tiny }),
            1L, 1000L);
    }

    private static Civilization Barbarians(GameInfo game) => game.Civilizations.Single(it => it.IsBarbarian);

    private static void Defeat(Civilization civ)
    {
        civ.HasEverOwnedOriginalCapital = true;
        civ.Cities = [];
    }

    [Fact]
    public void NewGameAwaitsInputFromTheFirstHumanWhoseTurnHasStarted()
    {
        var game = NewGame();
        var babylon = game.GetCivilization("Babylon");
        Assert.Equal(new TurnPhase.AwaitingInput(babylon), game.TurnPhase);
        Assert.True(game.IsTurnStarted(babylon));
        Assert.False(game.IsTurnStarted(game.GetCivilization("Greece")));
    }

    [Fact]
    public void PhasesWalkThroughTheAutoPlayersOneCivAtATime()
    {
        var game = NewGame();
        var (babylon, greece, rome) = (game.GetCivilization("Babylon"), game.GetCivilization("Greece"), game.GetCivilization("Rome"));
        var turns = game.Turns;

        game.EndPlayerTurn(2000L);
        Assert.False(game.IsTurnStarted(babylon));
        Assert.Equal(new TurnPhase.AutoPlayers(greece), game.TurnPhase);
        Assert.Same(babylon, game.CurrentPlayerCiv); // unchanged until BeginPlayerTurn

        Assert.Equal(new TurnPhase.AutoPlayers(rome), game.RunAutoPlayers());
        Assert.Equal(new TurnPhase.AutoPlayers(Barbarians(game)), game.RunAutoPlayers());
        Assert.Equal(new TurnPhase.AwaitingInput(babylon), game.RunAutoPlayers());
        Assert.Equal(turns + 1, game.Turns); // the index wrapped to the human
        Assert.False(game.IsTurnStarted(babylon)); // waiting for BeginPlayerTurn

        game.BeginPlayerTurn(3000L);
        Assert.True(game.IsTurnStarted(babylon));
        Assert.Equal(new TurnPhase.AwaitingInput(babylon), game.TurnPhase);
        Assert.Equal(3000L, game.CurrentTurnStartTime);
    }

    [Fact]
    public void RunAutoPlayersWithALargerLimitProcessesSeveralCivs()
    {
        var game = NewGame();
        game.EndPlayerTurn(2000L);
        Assert.IsType<TurnPhase.AwaitingInput>(game.RunAutoPlayers(max: 5));
    }

    [Fact]
    public void RunAutoPlayersWithZeroDoesNothing()
    {
        var game = NewGame();
        game.EndPlayerTurn(2000L);
        Assert.IsType<TurnPhase.AutoPlayers>(game.RunAutoPlayers(max: 0));
        Assert.Equal(game.GetCivilization("Greece"), ((TurnPhase.AutoPlayers)game.TurnPhase).NextCiv);
    }

    [Fact]
    public void MisorderedCallsAreRefused()
    {
        var game = NewGame();
        Assert.Throws<InvalidOperationException>(() => game.RunAutoPlayers());
        Assert.Throws<InvalidOperationException>(() => game.BeginPlayerTurn(2000L)); // already begun
    }

    [Fact]
    public void SavedGameRederivesThePhase()
    {
        var game = NewGame();
        game.NextTurn(2000L);
        var loaded = UncivFiles.GameInfoFromString(UncivFiles.GameInfoToString(game, forceZip: false));
        Assert.True(loaded.IsTurnStarted(loaded.CurrentPlayerCiv));
        Assert.Equal("Babylon", ((TurnPhase.AwaitingInput)loaded.TurnPhase).Civ.CivId);
        Assert.Equal(game.Turns, loaded.Turns);
    }

    [Fact]
    public void CloneMidPhaseKeepsThePhase()
    {
        var game = NewGame();
        game.EndPlayerTurn(2000L);
        var clone = game.Clone();
        clone.SetTransients();
        Assert.Equal("Greece", ((TurnPhase.AutoPlayers)clone.TurnPhase).NextCiv.CivId);
        clone.RunAutoPlayers(3);
        clone.BeginPlayerTurn(3000L);
        Assert.True(clone.IsTurnStarted(clone.GetCivilization("Babylon")));
        Assert.False(game.IsTurnStarted(game.GetCivilization("Babylon")));
    }

    [Fact]
    public void AnAiCivEndingItsTurnIsNotAdvancedPast()
    {
        // Resigning a multiplayer game ends the turn of a civ that is no longer human: it is processed like the other auto civs, not skipped.
        var game = NewGame();
        var babylon = game.GetCivilization("Babylon");
        babylon.PlayerType = PlayerType.AI;
        game.EndPlayerTurn(2000L);
        Assert.Equal(new TurnPhase.AutoPlayers(babylon), game.TurnPhase);
    }

    [Fact]
    public void EndingAnExplicitCivStartsFromThatCivsSuccessor()
    {
        var game = NewGame();
        var greece = game.GetCivilization("Greece");
        game.EndPlayerTurn(2000L, greece); // not human: not ended, will be processed itself
        Assert.Equal(new TurnPhase.AutoPlayers(greece), game.TurnPhase);
    }

    [Fact]
    public void DefeatedSecondHotseatHumanIsSkipped()
    {
        var game = NewGame(false, ("Babylon", PlayerType.Human), ("Greece", PlayerType.Human), ("Rome", PlayerType.AI));
        var greece = game.GetCivilization("Greece");
        Defeat(greece);
        game.EndPlayerTurn(2000L);
        Assert.Equal(new TurnPhase.AutoPlayers(greece), game.TurnPhase);
        Assert.Equal(new TurnPhase.AutoPlayers(game.GetCivilization("Rome")), game.RunAutoPlayers());
        Assert.Equal(new TurnPhase.AutoPlayers(Barbarians(game)), game.RunAutoPlayers());
        Assert.Equal(new TurnPhase.AwaitingInput(game.GetCivilization("Babylon")), game.RunAutoPlayers());
    }

    [Fact]
    public void LivingSecondHotseatHumanGetsTheTurn()
    {
        var game = NewGame(false, ("Babylon", PlayerType.Human), ("Greece", PlayerType.Human), ("Rome", PlayerType.AI));
        game.EndPlayerTurn(2000L);
        Assert.Equal(new TurnPhase.AwaitingInput(game.GetCivilization("Greece")), game.TurnPhase);
        Assert.IsType<TurnPhase.AwaitingInput>(game.RunAutoPlayers(5));
        game.BeginPlayerTurn(3000L);
        Assert.Equal("Greece", game.CurrentPlayer);
    }

    [Fact]
    public void AllDefeatedHotseatHumansLetTheFirstOneControlTheTurn()
    {
        var game = NewGame(false, ("Babylon", PlayerType.Human), ("Greece", PlayerType.Human), ("Rome", PlayerType.AI));
        Defeat(game.GetCivilization("Babylon"));
        Defeat(game.GetCivilization("Greece"));
        game.EndPlayerTurn(2000L);
        Assert.Equal(new TurnPhase.AutoPlayers(game.GetCivilization("Greece")), game.TurnPhase);
        game.RunAutoPlayers(5);
        Assert.Equal("Babylon", ((TurnPhase.AwaitingInput)game.TurnPhase).Civ.CivId);
    }

    [Fact]
    public void OnlineSpectatorIsProcessedAutomaticallyAndLosesItsPopups()
    {
        var game = NewGame(true, (Constants.Spectator, PlayerType.Human), ("Babylon", PlayerType.Human), ("Greece", PlayerType.AI));
        var spectator = game.Civilizations.Single(it => it.IsSpectator());
        var babylon = game.GetCivilization("Babylon");
        Assert.Same(babylon, game.CurrentPlayerCiv);
        game.EndPlayerTurn(2000L);
        Assert.Equal(new TurnPhase.AutoPlayers(game.GetCivilization("Greece")), game.TurnPhase);
        game.RunAutoPlayers(1);
        Assert.Equal(new TurnPhase.AutoPlayers(Barbarians(game)), game.TurnPhase);
        game.RunAutoPlayers(1);
        Assert.Equal(new TurnPhase.AutoPlayers(spectator), game.TurnPhase);
        game.RunAutoPlayers(1);
        Assert.Equal(new TurnPhase.AwaitingInput(babylon), game.TurnPhase);
    }

    [Fact]
    public void SpectatorWhoseTurnBeginsHasNoPopups()
    {
        var game = NewGame(false, (Constants.Spectator, PlayerType.Human), ("Babylon", PlayerType.Human), ("Greece", PlayerType.AI));
        var spectator = game.Civilizations.Single(it => it.IsSpectator());
        Assert.Same(spectator, game.CurrentPlayerCiv); // offline, a spectator does get a turn
        spectator.PopupAlerts.Add(new PopupAlert(AlertType.GoldenAge, ""));
        game.EndPlayerTurn(2000L);
        game.BeginPlayerTurn(3000L); // Babylon
        game.EndPlayerTurn(4000L);
        game.RunAutoPlayers(5);
        Assert.Same(spectator, ((TurnPhase.AwaitingInput)game.TurnPhase).Civ);
        spectator.PopupAlerts.Add(new PopupAlert(AlertType.GoldenAge, ""));
        game.BeginPlayerTurn(5000L);
        Assert.Empty(spectator.PopupAlerts);
    }

    [Fact]
    public void GameOverIsReportedBetweenTurnsUnlessOneMoreTurnIsOn()
    {
        var game = NewGame();
        game.VictoryData = new VictoryData("Babylon", "Domination", 1);
        Assert.IsType<TurnPhase.GameOver>(game.TurnPhase);
        game.OneMoreTurnMode = true;
        Assert.IsType<TurnPhase.AwaitingInput>(game.TurnPhase);
    }

    [Fact]
    public void SimulationBreakStopsOnTheCivThatWonAndReportsGameOver()
    {
        var game = NewGame();
        game.SimulateUntilWin = true;
        game.Turns = 1;
        game.SimulateMaxTurns = 1; // already reached: the first auto civ breaks the loop
        game.EndPlayerTurn(2000L);
        var phase = game.RunAutoPlayers();
        Assert.Equal(new TurnPhase.AwaitingInput(game.GetCivilization("Greece")), phase);
        Assert.False(game.SimulateUntilWin);
        game.BeginPlayerTurn(3000L);
        Assert.Equal("Greece", game.CurrentPlayer);
    }
}
