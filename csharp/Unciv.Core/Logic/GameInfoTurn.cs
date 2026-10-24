using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Managers;
using Unciv.Utils;
using CityObject = Unciv.Logic.City.City;
using NotificationCategory = Unciv.Logic.Civilization.Notification.NotificationCategory;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Logic;

// Ported from core/src/com/unciv/logic/GameInfo.kt (T071): turn handling (nextTurn and the members it uses).
//
// Stripped: the timeThis profiling wrapper; the music controller call at the end of nextTurn (client audio, Won't port).
// Deviations:
// - The wall clock is an input: NextTurn takes nowMillis where Kotlin reads Clock.System.now() / System.currentTimeMillis().
// - NextTurnProgress (a UI progress bar) is an optional Action called where Kotlin calls progressBar?.increment().
// - UncivGame.Current.worldScreen?.autoPlay is GameClientState.AutoPlay (null when there is no client).
// - DebugUtils.SIMULATE_UNTIL_TURN is EngineOptions.SimulateUntilTurn.

public partial class GameInfo
{
    /// <summary>Records the ranking stats of all major civs</summary>
    private void RecordRankingStats()
    {
        foreach (var civ in Civilizations)
        {
            if (!civ.IsMajorCiv() || !civ.IsAlive()) continue;
            // Force uses a transient cache that is not invalidated on combat losses during other civs' turns
            civ.ResetMilitaryMightCache();
            civ.StatsHistory.RecordRankingStats(civ);
        }
    }

    /// <summary>
    /// Advance a turn, running automation for AI players, stopping for human players
    /// </summary>
    /// <param name="nowMillis">The current time, epoch milliseconds (Kotlin reads the wall clock).</param>
    /// <param name="progressBar">Optional progress callback, or <c>null</c> when simulating</param>
    /// <param name="shouldGainTime">on a multiplayer game, if true, makes the player whose turn is ended recover time to play before risking getting forced to resign, 'false' by default</param>
    public void NextTurn(long nowMillis, Action? progressBar = null, bool shouldGainTime = false)
    {
        var player = CurrentPlayerCiv;
        var playerIndex = Civilizations.IndexOf(player);

        if (player.IsHuman() && player.IsAlive())
        {
            var elapsedSeconds = (nowMillis - CurrentTurnStartTime) / 1000;
            player.TotalTurnTimeSeconds = unchecked(player.TotalTurnTimeSeconds + (int)elapsedSeconds);
            player.TurnsPlayedAsHuman++;
        }

        if (GameParameters.IsOnlineMultiplayer) UpdateMinutesBeforeForceResign(player, shouldGainTime, nowMillis);

        // We rotate Players in cycle: 1,2...N,1,2...
        void SetNextPlayer()
        {
            playerIndex = (playerIndex + 1) % Civilizations.Count;
            if (playerIndex == 0)
            {
                RecordRankingStats();
                Turns++;
                if (EngineOptions.SimulateUntilTurn != 0)
                    Log.Debug("Starting simulation of turn %s", Turns);
            }
            player = Civilizations[playerIndex];
        }

        // Ending current player's turn
        //  (Check is important or else switchTurn
        //  would skip a turn if an AI civ calls nextTurn
        //  this happens when resigning a multiplayer game)
        if (player.IsHuman())
        {
            new TurnManager(player).EndTurn(progressBar);
            SetNextPlayer();
        }

        var isOnline = GameParameters.IsOnlineMultiplayer;

        // Skip the player if we are playing hotseat
        // If all hotseat players are defeated then skip all but the first one
        bool ShouldAutoProcessHotseatPlayer() =>
            !isOnline &&
            player.IsDefeated() && (Civilizations.Any(it => it.IsHuman() && it.IsAlive())
                || Civilizations.First(it => it.IsHuman()) != player);

        // Skip all spectators and defeated players
        // If all players are defeated then let the first player control next turn
        bool ShouldAutoProcessOnlinePlayer() =>
            isOnline && (player.IsSpectator() || (player.IsDefeated() &&
                (Civilizations.Any(it => it.IsHuman() && it.IsAlive())
                    || Civilizations.First(it => it.IsHuman()) != player)));

        // We process player automatically if:
        while (IsSimulation()                       // simulation is active
               || player.IsAI()                     // or player is AI
               || ShouldAutoProcessHotseatPlayer()  // or a player is defeated in hotseat
               || ShouldAutoProcessOnlinePlayer())  // or player is online spectator
        {
            // Starting preparations
            new TurnManager(player).StartTurn(progressBar);

            // Automation done here
            new TurnManager(player).AutomateTurn();

            var autoPlay = ClientState.AutoPlay;
            // Do we need to break if player won?
            if (SimulateUntilWin && (player.VictoryManager.HasWon() || (SimulateMaxTurns >= 1 && SimulateMaxTurns <= Turns)
                    || GetAliveMajorCivs().Count == 0))
            {
                SimulateUntilWin = false;
                SimulateMaxTurns = 0;
                autoPlay?.StopAutoPlay();
                break;
            }

            // Do we need to stop AutoPlay?
            if (autoPlay != null && autoPlay.IsAutoPlaying() && player.VictoryManager.HasWon() && !OneMoreTurnMode)
                autoPlay.StopAutoPlay();

            // Clean up
            new TurnManager(player).EndTurn(progressBar);

            // To the next player
            SetNextPlayer();
        }

        if (Turns == EngineOptions.SimulateUntilTurn)
            EngineOptions.SimulateUntilTurn = 0;

        // We found a human player, so we are making them current
        CurrentTurnStartTime = nowMillis;
        CurrentPlayer = player.CivId;
        CurrentPlayerCiv = player;

        // Starting their turn
        new TurnManager(player).StartTurn(progressBar);

        // No popups for spectators
        if (CurrentPlayerCiv.IsSpectator())
            CurrentPlayerCiv.PopupAlerts.Clear();

        // Start our turn immediately before the player can make decisions - affects
        // whether our units can commit automated actions and then be attacked immediately etc.
        NotifyOfCloseEnemyUnits(player);

        // This would belong at the end of TurnManager.startTurn, but needs to come after notifyOfCloseEnemyUnits
        player.NotificationCountAtStartTurn = player.Notifications.Count;
    }

    private void UpdateMinutesBeforeForceResign(Civilization.Civilization player, bool shouldGainTime, long nowMillis)
    {
        // Update remaining time before the player who's turn is ending can be forced to resign
        var timeUsed = (int)((nowMillis - CurrentTurnStartTime) / 60_000);
        var timeRegained = shouldGainTime ? GameParameters.MinutesRecoveredPerTurn : 0;
        var rawNewTime = unchecked(player.PlayerMinutesBeforeForceResign - timeUsed + timeRegained);
        var maxNewTime = GameParameters.MinutesUntilForceResign;
        player.PlayerMinutesBeforeForceResign = Math.Clamp(rawNewTime, 0, maxNewTime);
    }

    private void NotifyOfCloseEnemyUnits(Civilization.Civilization thisPlayer)
    {
        var enemyUnitsCloseToTerritory = thisPlayer.ViewableTiles
            .Where(it => it.MilitaryUnit != null && it.MilitaryUnit.Civ != thisPlayer
                         && thisPlayer.IsAtWarWith(it.MilitaryUnit.Civ)
                         && (it.GetOwner() == thisPlayer || (it.Neighbors.Any(neighbor => neighbor.GetOwner() == thisPlayer)
                                                             && it.MilitaryUnit.IsVisibleTo(thisPlayer))))
            .ToList();

        // enemy units IN our territory
        AddEnemyUnitNotification(thisPlayer, enemyUnitsCloseToTerritory.Where(it => it.GetOwner() == thisPlayer).ToList(), "in");
        // enemy units NEAR our territory
        AddEnemyUnitNotification(thisPlayer, enemyUnitsCloseToTerritory.Where(it => it.GetOwner() != thisPlayer).ToList(), "near");

        AddBombardNotification(
            thisPlayer,
            thisPlayer.Cities.Where(city =>
                city.CanBombard() &&
                enemyUnitsCloseToTerritory.Any(tile => tile.AerialDistanceTo(city.GetCenterTile()) <= city.GetBombardRange())).ToList());
    }

    public void ProcessDiplomaticVictory()
    {
        if (DiplomaticVictoryVotesProcessed) return;
        foreach (var civInfo in Civilizations)
        {
            if (civInfo.VictoryManager.HasEnoughVotesForDiplomaticVictory())
                civInfo.VictoryManager.HasEverWonDiplomaticVote = true;
        }
        DiplomaticVictoryVotesProcessed = true;
    }

    /// <returns><c>true</c> if someone has won - checks existing <see cref="VictoryData"/> and each civ's <c>VictoryManager.GetVictoryTypeAchieved</c></returns>
    public bool CheckForVictory()
    {
        if (VictoryData != null) return true;
        foreach (var civ in Civilizations)
        {
            new TurnManager(civ).UpdateWinningCiv();
            if (VictoryData != null) return true;
        }
        return false;
    }

    private static void AddEnemyUnitNotification(Civilization.Civilization thisPlayer, List<TileClass> tiles, string inOrNear)
    {
        // don't flood the player with similar messages. instead cycle through units by clicking the message multiple times.
        if (tiles.Count < 3)
        {
            foreach (var tile in tiles)
            {
                var unitName = tile.MilitaryUnit.NotNull().Name;
                thisPlayer.AddNotification($"An enemy [{unitName}] was spotted {inOrNear} our territory", tile.Position, NotificationCategory.War, NotificationIcon.War, unitName);
            }
        }
        else
        {
            var positions = tiles.Select(it => it.Position);
            thisPlayer.AddNotification($"[{tiles.Count}] enemy units were spotted {inOrNear} our territory", LocationAction.Of(positions), NotificationCategory.War, NotificationIcon.War);
        }
    }

    private static void AddBombardNotification(Civilization.Civilization thisPlayer, List<CityObject> cities)
    {
        if (cities.Count < 3)
        {
            foreach (var city in cities)
                thisPlayer.AddNotification($"Your city [{city.Name}] can bombard the enemy!", new MapUnitAction(city.Location), NotificationCategory.War, NotificationIcon.City, NotificationIcon.Crosshair);
        }
        else
        {
            var notificationActions = cities.Select(it => new MapUnitAction(it.Location));
            thisPlayer.AddNotification($"[{cities.Count}] of your cities can bombard the enemy!", notificationActions, NotificationCategory.War, NotificationIcon.City, NotificationIcon.Crosshair);
        }
    }
}
