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

    // T184: NextTurn re-expressed on the phases of GameInfoTurnPhases.cs. Between EndPlayerTurn and BeginPlayerTurn this is the index of the
    // civ whose turn comes next (null while a human's turn is running). Not serialized: a saved game is always at "awaiting input".
    private int? pendingPlayerIndex;

    // The civs whose turn has begun (BeginPlayerTurn) and not yet ended. Not serialized: SetTransients derives it. Kotlin has no such state - the
    // first human turn of a new game is "started" by convention, since GameStarter never calls TurnManager. Membership only, never iterated.
    private readonly HashSet<string> turnStartedCivs = new(StringComparer.Ordinal);

    /// <summary>
    /// Advance a turn, running automation for AI players, stopping for human players.
    /// Exactly <see cref="EndPlayerTurn"/>, then <see cref="RunAutoPlayers"/> one civ at a time, then <see cref="BeginPlayerTurn"/>.
    /// </summary>
    /// <param name="nowMillis">The current time, epoch milliseconds (Kotlin reads the wall clock).</param>
    /// <param name="progressBar">Optional progress callback, or <c>null</c> when simulating</param>
    /// <param name="shouldGainTime">on a multiplayer game, if true, makes the player whose turn is ended recover time to play before risking getting forced to resign, 'false' by default</param>
    public void NextTurn(long nowMillis, Action? progressBar = null, bool shouldGainTime = false)
    {
        EndPlayerTurn(nowMillis, progressBar: progressBar, shouldGainTime: shouldGainTime);
        while (RunAutoPlayers(1, progressBar) is TurnPhase.AutoPlayers) { }
        BeginPlayerTurn(nowMillis, progressBar: progressBar);
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
