using Unciv.Core.Tests.Support;
using Unciv.Logic;
using Unciv.Logic.Civilization;
using CityObject = Unciv.Logic.City.City;

namespace Unciv.Core.Tests.Logic;

// T071: GameInfo.kt turn handling (nextTurn and the members it uses) on TestGame games.
// The ranking history, the victory checks and the notification texts are asserted directly; the wall clock is the nowMillis input.
[Collection("RulesetCache")]
public sealed class GameInfoTurnTests
{
    private sealed class FakeAutoPlay : IAutoPlay
    {
        public bool Playing { get; set; } = true;
        public int Stops { get; private set; }
        public bool IsAutoPlaying() => Playing;
        public void StopAutoPlay() => Stops++;
    }

    private readonly TestGame game = new();
    private GameInfo Info => game.GameInfo;

    /// <summary>A human civ and an AI civ, each with a capital, the human being the current player.</summary>
    private (Civilization Human, Civilization Ai) HumanAndAi()
    {
        game.MakeHexagonalMap(6);
        var human = game.AddCiv(isPlayer: true);
        var ai = game.AddCiv();
        game.AddCity(human, game.GetTile(0, 0));
        game.AddCity(ai, game.GetTile(4, 4));
        Info.CurrentPlayerCiv = human;
        Info.CurrentPlayer = human.CivId;
        return (human, ai);
    }

    [Fact]
    public void NextTurn_RunsTheAiAndReturnsToTheHumanInTheNextTurn()
    {
        var (human, ai) = HumanAndAi();
        Info.CurrentTurnStartTime = 1_000;

        Info.NextTurn(1_000 + 125_999);

        Assert.Equal(1, Info.Turns);
        Assert.Same(human, Info.CurrentPlayerCiv);
        Assert.Equal(human.CivId, Info.CurrentPlayer);
        Assert.Equal(1_000 + 125_999, Info.CurrentTurnStartTime);
        Assert.Equal(125, human.TotalTurnTimeSeconds);
        Assert.Equal(1, human.TurnsPlayedAsHuman);
        Assert.Equal(0, ai.TurnsPlayedAsHuman);
        Assert.Equal(human.Notifications.Count, human.NotificationCountAtStartTurn);
    }

    [Fact]
    public void NextTurn_RecordsRankingStatsOfLivingMajorCivsBeforeTheTurnCounterAdvances()
    {
        var (human, ai) = HumanAndAi();
        var cityState = game.AddCiv(cityStateType: "Cultured");
        game.AddCity(cityState, game.GetTile(-4, -4));

        Info.NextTurn(0);

        Assert.True(human.StatsHistory.ContainsKey(0));
        Assert.True(ai.StatsHistory.ContainsKey(0));
        Assert.Equal(Enum.GetValues<Unciv.UI.Screens.VictoryScreen.RankingType>().Length, human.StatsHistory[0].Count);
        Assert.Equal(0, cityState.StatsHistory.Count);
    }

    [Fact]
    public void NextTurn_OnlineMultiplayerChargesTheTimeUsedAndRegainsIfAsked()
    {
        var (human, _) = HumanAndAi();
        Info.GameParameters.IsOnlineMultiplayer = true;
        Info.CurrentTurnStartTime = 0;
        human.PlayerMinutesBeforeForceResign = 100;

        Info.NextTurn(10 * 60_000 + 59_999);
        Assert.Equal(90, human.PlayerMinutesBeforeForceResign);

        human.PlayerMinutesBeforeForceResign = 100;
        Info.NextTurn(Info.CurrentTurnStartTime + 60_000, shouldGainTime: true);
        Assert.Equal(100 - 1 + Info.GameParameters.MinutesRecoveredPerTurn, human.PlayerMinutesBeforeForceResign);

        human.PlayerMinutesBeforeForceResign = Info.GameParameters.MinutesUntilForceResign - 1;
        Info.NextTurn(Info.CurrentTurnStartTime, shouldGainTime: true);
        Assert.Equal(Info.GameParameters.MinutesUntilForceResign, human.PlayerMinutesBeforeForceResign); // clamped to the maximum
    }

    [Fact]
    public void NextTurn_OnlineMultiplayerClampsAtZero()
    {
        var (human, _) = HumanAndAi();
        Info.GameParameters.IsOnlineMultiplayer = true;
        Info.CurrentTurnStartTime = 0;
        human.PlayerMinutesBeforeForceResign = 5;

        Info.NextTurn(30 * 60_000);

        Assert.Equal(0, human.PlayerMinutesBeforeForceResign);
    }

    [Fact]
    public void NextTurn_SimulatesUntilTheRequestedTurnAndResetsTheOption()
    {
        var (human, ai) = HumanAndAi();
        Info.EngineOptions.SimulateUntilTurn = 3;

        Info.NextTurn(0);

        Assert.Equal(3, Info.Turns);
        Assert.Equal(0, Info.EngineOptions.SimulateUntilTurn);
        Assert.Same(human, Info.CurrentPlayerCiv);
        Assert.True(human.StatsHistory.ContainsKey(2));
        Assert.True(ai.StatsHistory.ContainsKey(1));
    }

    [Fact]
    public void NextTurn_SimulateUntilWinStopsAtTheTurnLimitAndStopsAutoPlay()
    {
        var (_, ai) = HumanAndAi();
        var autoPlay = new FakeAutoPlay();
        Info.ClientState.AutoPlay = autoPlay;
        Info.CurrentPlayerCiv = ai;
        Info.CurrentPlayer = ai.CivId;
        Info.Turns = 1;
        Info.SimulateUntilWin = true;
        Info.SimulateMaxTurns = 1;

        Info.NextTurn(0);

        Assert.False(Info.SimulateUntilWin);
        Assert.Equal(0, Info.SimulateMaxTurns);
        Assert.Equal(1, autoPlay.Stops);
        Assert.Equal(1, Info.Turns); // broke out before advancing
        Assert.Same(ai, Info.CurrentPlayerCiv);
    }

    [Fact]
    public void NextTurn_SimulateUntilWinKeepsGoingBelowTheLimit()
    {
        var (human, _) = HumanAndAi();
        Info.SimulateUntilWin = true;
        Info.SimulateMaxTurns = 3;

        Info.NextTurn(0);

        Assert.Equal(3, Info.Turns);
        Assert.True(Info.SimulateUntilWin);
        Assert.Same(human, Info.CurrentPlayerCiv);
    }

    [Fact]
    public void NextTurn_SkipsDefeatedHotseatPlayersButNotWhenOnline()
    {
        var (human, _) = HumanAndAi();
        var defeated = game.AddCiv(isPlayer: true); // no city, no unit: defeated
        var last = game.AddCiv(isPlayer: true);
        game.AddCity(last, game.GetTile(-4, -4));
        Info.Civilizations.Remove(defeated);
        Info.Civilizations.Insert(1, defeated);
        Assert.True(defeated.IsDefeated());

        Info.NextTurn(0);

        Assert.Same(last, Info.CurrentPlayerCiv); // the defeated one was processed automatically
        Assert.Equal(0, Info.Turns);

        Info.NextTurn(0);
        Assert.Same(human, Info.CurrentPlayerCiv);
        Assert.Equal(1, Info.Turns);
    }

    [Fact]
    public void NextTurn_OnlineSkipsSpectatorsAndDefeatedPlayers()
    {
        var (human, _) = HumanAndAi();
        Info.GameParameters.IsOnlineMultiplayer = true;
        var spectator = game.AddCiv(game.Ruleset.Nations.Values.First(it => it.IsSpectator), isPlayer: true);
        var defeated = game.AddCiv(isPlayer: true);
        var last = game.AddCiv(isPlayer: true);
        game.AddCity(last, game.GetTile(-4, -4));
        Assert.True(spectator.IsSpectator());
        Assert.True(defeated.IsDefeated());
        spectator.PopupAlerts.Add(new PopupAlert { Type = AlertType.Defeated });
        var popups = spectator.PopupAlerts.Count;

        Info.NextTurn(0);

        Assert.Same(last, Info.CurrentPlayerCiv);
        Assert.Equal(popups, spectator.PopupAlerts.Count); // only a spectator that becomes the current player has its popups cleared
        Assert.NotSame(human, Info.CurrentPlayerCiv);
    }

    [Fact]
    public void NextTurn_ClearsPopupsOfASpectatorWhoBecomesCurrent()
    {
        var (human, _) = HumanAndAi();
        var spectator = game.AddCiv(game.Ruleset.Nations.Values.First(it => it.IsSpectator), isPlayer: true);
        spectator.PopupAlerts.Add(new PopupAlert { Type = AlertType.Defeated });
        Info.CurrentPlayerCiv = human;
        Info.Civilizations.Remove(spectator);
        Info.Civilizations.Insert(1, spectator);
        // hotseat: the spectator is human and alive (never defeated), so it gets the turn
        Info.NextTurn(0);

        Assert.Same(spectator, Info.CurrentPlayerCiv);
        Assert.Empty(spectator.PopupAlerts);
    }

    private (Civilization Human, Civilization Ai, CityObject City) AtWar()
    {
        var (human, ai) = HumanAndAi();
        human.DiplomacyFunctions.MakeCivilizationsMeet(ai);
        human.GetDiplomacyManager(ai)!.DeclareWar();
        Assert.True(human.IsAtWarWith(ai));
        return (human, ai, human.Cities[0]);
    }

    [Fact]
    public void NextTurn_NotifiesAboutEnemyUnitsInTheTerritoryAndBombardableCities()
    {
        var (human, ai, city) = AtWar();
        var tile = city.GetCenterTile().Neighbors.First(it => it.GetOwner() == human && it.MilitaryUnit == null && it.IsLand);
        game.AddUnit("Warrior", ai, tile);

        Info.NextTurn(0);

        Assert.Contains(human.Notifications, it => it.Text == "An enemy [Warrior] was spotted in our territory");
        Assert.Contains(human.Notifications, it => it.Text == $"Your city [{city.Name}] can bombard the enemy!");
    }

    [Fact]
    public void NextTurn_SummarizesThreeOrMoreEnemyUnitsAndCities()
    {
        var (human, ai, city) = AtWar();
        var tiles = city.GetCenterTile().Neighbors.Where(it => it.GetOwner() == human && it.IsLand).Take(3).ToList();
        Assert.Equal(3, tiles.Count);
        foreach (var tile in tiles) game.AddUnit("Warrior", ai, tile);

        Info.NextTurn(0);

        Assert.Contains(human.Notifications, it => it.Text == "[3] enemy units were spotted in our territory");
        Assert.DoesNotContain(human.Notifications, it => it.Text.StartsWith("An enemy [", StringComparison.Ordinal));
    }

    [Fact]
    public void NextTurn_SummarizesThreeOrMoreBombardingCities()
    {
        var (human, ai, city) = AtWar();
        var unitTile = city.GetCenterTile().Neighbors.First(it => it.GetOwner() == human && it.IsLand);
        foreach (var tile in unitTile.GetTilesAtDistance(2).Where(it => it != city.GetCenterTile() && it.GetOwner() == null && it.IsLand).Take(2).ToList())
            game.AddCity(human, tile);
        Assert.Equal(3, human.Cities.Count);
        game.AddUnit("Warrior", ai, unitTile);

        Info.NextTurn(0);

        Assert.Contains(human.Notifications, it => it.Text == "[3] of your cities can bombard the enemy!");
    }

    [Fact]
    public void NextTurn_DoesNotNotifyAboutUnitsOfPeacefulCivs()
    {
        var (human, ai) = HumanAndAi();
        var tile = human.Cities[0].GetCenterTile().GetTilesAtDistance(2).First(it => it.GetOwner() == null && it.IsLand);
        game.AddUnit("Warrior", ai, tile);

        Info.NextTurn(0);

        Assert.DoesNotContain(human.Notifications, it => it.Text.Contains("spotted", StringComparison.Ordinal));
    }

    [Fact]
    public void ProcessDiplomaticVictory_RunsOnce()
    {
        var (human, _) = HumanAndAi();
        Assert.False(Info.DiplomaticVictoryVotesProcessed);

        Info.ProcessDiplomaticVictory();

        Assert.True(Info.DiplomaticVictoryVotesProcessed);
        Assert.False(human.VictoryManager.HasEverWonDiplomaticVote);
        Info.ProcessDiplomaticVictory(); // second call returns early
        Assert.True(Info.DiplomaticVictoryVotesProcessed);
    }

    [Fact]
    public void CheckForVictory_ReflectsExistingVictoryData()
    {
        HumanAndAi();
        Assert.False(Info.CheckForVictory());
        Info.VictoryData = new VictoryData("x", "Scientific", 3);
        Assert.True(Info.CheckForVictory());
    }

    [Fact]
    public void RecordRankingStats_StoresEveryRankingUnderTheCurrentTurn()
    {
        var (human, _) = HumanAndAi();
        Info.Turns = 7;
        human.StatsHistory.RecordRankingStats(human);
        Assert.True(human.StatsHistory.ContainsKey(7));
        Assert.Equal(human.GetStatForRanking(Unciv.UI.Screens.VictoryScreen.RankingType.Population), human.StatsHistory[7][Unciv.UI.Screens.VictoryScreen.RankingType.Population]);
    }
}
