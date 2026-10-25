using System.Text.Json.Serialization;
using Unciv.Logic.Civilization.Managers;
using Unciv.Utils;
using CivilizationClass = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Logic;

// T184: the phases of GameInfo.NextTurn (design/COMMANDS-IMPLEMENTATION.md section 5.2), each doing exactly what its slice of Kotlin GameInfo.nextTurn does.
// Order and side effects are unchanged, so the end-to-end state hashes (T102) are the regression for this split.
//
// Deviations: RunAutoPlayers takes no clock (nothing in the auto loop reads it); the civ is an explicit optional parameter (default: the current player) so simultaneous turns (T200) can compose the same phases.
// Quirk kept: when the win-check breaks the auto loop, the civ it stopped on has had StartTurn and AutomateTurn run, and BeginPlayerTurn runs StartTurn again.

public partial class GameInfo
{
    /// <summary>
    /// Where the turn flow is now, derived from the state. <see cref="TurnPhase.GameOver"/> is only reported between turns, never in the middle of the auto players.
    /// After <see cref="RunAutoPlayers"/> stopped on a simulation win-check break, use its return value: the simulation flags it reset may let this report <c>AutoPlayers</c> for that civ.
    /// </summary>
    [JsonIgnore]
    public TurnPhase TurnPhase
    {
        get
        {
            if (IsSimultaneous)
            {
                if (VictoryData != null && !OneMoreTurnMode) return new TurnPhase.GameOver(VictoryData);
                return NextResolveStep() is ResolveStep.RunAiTurn ai
                    ? new TurnPhase.AutoPlayers(GetCivilization(ai.CivId))
                    : new TurnPhase.AwaitingInput(CurrentPlayerCiv);
            }
            if (pendingPlayerIndex is { } index)
            {
                var next = Civilizations[index];
                return ShouldAutoProcess(next) ? new TurnPhase.AutoPlayers(next) : new TurnPhase.AwaitingInput(next);
            }
            if (VictoryData != null && !OneMoreTurnMode) return new TurnPhase.GameOver(VictoryData);
            return new TurnPhase.AwaitingInput(CurrentPlayerCiv);
        }
    }

    /// <summary>Whether the turn of <paramref name="civ"/> has begun (<see cref="BeginPlayerTurn"/>) and not ended yet.</summary>
    public bool IsTurnStarted(CivilizationClass civ) => turnStartedCivs.Contains(civ.CivId);

    /// <summary>
    /// Ends the turn of <paramref name="civ"/> (default: the current player): time accounting, <c>TurnManager.EndTurn</c> if it is human, and moves on to the next civ in the cycle.
    /// A non-human civ is not ended and not skipped (this happens when resigning a multiplayer game): it is processed by <see cref="RunAutoPlayers"/>.
    /// </summary>
    public void EndPlayerTurn(long nowMillis, CivilizationClass? civ = null, Action? progressBar = null, bool shouldGainTime = false)
    {
        var player = civ ?? CurrentPlayerCiv;
        var playerIndex = Civilizations.IndexOf(player);

        EndCivTurn(player, nowMillis, progressBar, shouldGainTime);
        if (player.IsHuman()) playerIndex = AdvancePlayerIndex(playerIndex);
        pendingPlayerIndex = playerIndex;
    }

    // The civ-level part of EndPlayerTurn, shared with the simultaneous ProcessEndTurn (T200).
    private void EndCivTurn(CivilizationClass player, long nowMillis, Action? progressBar, bool shouldGainTime)
    {
        if (player.IsHuman() && player.IsAlive())
        {
            var elapsedSeconds = (nowMillis - CurrentTurnStartTime) / 1000;
            player.TotalTurnTimeSeconds = unchecked(player.TotalTurnTimeSeconds + (int)elapsedSeconds);
            player.TurnsPlayedAsHuman++;
        }

        if (GameParameters.IsOnlineMultiplayer) UpdateMinutesBeforeForceResign(player, shouldGainTime, nowMillis);

        // Ending current player's turn
        //  (Check is important or else switchTurn
        //  would skip a turn if an AI civ calls nextTurn
        //  this happens when resigning a multiplayer game)
        turnStartedCivs.Remove(player.CivId);
        if (player.IsHuman()) new TurnManager(player).EndTurn(progressBar);
    }

    /// <summary>
    /// Processes up to <paramref name="max"/> civs that play automatically (StartTurn, AutomateTurn, EndTurn) and returns the phase afterwards, so a host can log and checkpoint per civ.
    /// </summary>
    public TurnPhase RunAutoPlayers(int max = 1, Action? progressBar = null)
    {
        var index = pendingPlayerIndex ?? throw new InvalidOperationException("RunAutoPlayers needs EndPlayerTurn first");
        for (var done = 0; done < max && ShouldAutoProcess(Civilizations[index]); done++)
        {
            var player = Civilizations[index];

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
                // The loop stops on this civ; it becomes the next player
                return VictoryData != null ? new TurnPhase.GameOver(VictoryData) : new TurnPhase.AwaitingInput(player);
            }

            // Do we need to stop AutoPlay?
            if (autoPlay != null && autoPlay.IsAutoPlaying() && player.VictoryManager.HasWon() && !OneMoreTurnMode)
                autoPlay.StopAutoPlay();

            // Clean up
            new TurnManager(player).EndTurn(progressBar);

            // To the next player
            index = AdvancePlayerIndex(index);
            pendingPlayerIndex = index;
        }
        return TurnPhase;
    }

    /// <summary>Makes the next civ (default: the one <see cref="RunAutoPlayers"/> stopped on) the current player and starts its turn.</summary>
    public void BeginPlayerTurn(long nowMillis, CivilizationClass? civ = null, Action? progressBar = null)
    {
        var player = civ ?? (pendingPlayerIndex is { } index ? Civilizations[index] : CurrentPlayerCiv);
        if (IsTurnStarted(player)) throw new InvalidOperationException($"The turn of {player.CivId} has already begun");

        if (Turns == EngineOptions.SimulateUntilTurn)
            EngineOptions.SimulateUntilTurn = 0;

        // We found a human player, so we are making them current
        CurrentTurnStartTime = nowMillis;
        CurrentPlayer = player.CivId;
        CurrentPlayerCiv = player;
        pendingPlayerIndex = null;

        StartCivTurn(player, progressBar);
    }

    // The civ-level part of BeginPlayerTurn, shared with the simultaneous BeginTurn (T200).
    private void StartCivTurn(CivilizationClass player, Action? progressBar)
    {
        // Starting their turn
        new TurnManager(player).StartTurn(progressBar);
        turnStartedCivs.Add(player.CivId);

        // No popups for spectators
        if (player.IsSpectator())
            player.PopupAlerts.Clear();

        // Start our turn immediately before the player can make decisions - affects
        // whether our units can commit automated actions and then be attacked immediately etc.
        NotifyOfCloseEnemyUnits(player);

        // This would belong at the end of TurnManager.startTurn, but needs to come after notifyOfCloseEnemyUnits
        player.NotificationCountAtStartTurn = player.Notifications.Count;
    }

    /// <summary>We rotate Players in cycle: 1,2...N,1,2...</summary>
    private int AdvancePlayerIndex(int playerIndex)
    {
        playerIndex = (playerIndex + 1) % Civilizations.Count;
        if (playerIndex == 0)
        {
            RecordRankingStats();
            Turns++;
            if (EngineOptions.SimulateUntilTurn != 0)
                Log.Debug("Starting simulation of turn %s", Turns);
        }
        return playerIndex;
    }

    /// <summary>We process a player automatically if simulation is active, it is AI, it is a defeated hotseat human, or an online spectator/defeated player.</summary>
    private bool ShouldAutoProcess(CivilizationClass player)
    {
        if (IsSimulation() || player.IsAI()) return true;
        var isOnline = GameParameters.IsOnlineMultiplayer;

        // Skip the player if we are playing hotseat
        // If all hotseat players are defeated then skip all but the first one
        if (!isOnline && player.IsDefeated()
            && (Civilizations.Any(it => it.IsHuman() && it.IsAlive()) || Civilizations.First(it => it.IsHuman()) != player))
            return true;

        // Skip all spectators and defeated players
        // If all players are defeated then let the first player control next turn
        return isOnline && (player.IsSpectator() || (player.IsDefeated() &&
            (Civilizations.Any(it => it.IsHuman() && it.IsAlive())
                || Civilizations.First(it => it.IsHuman()) != player)));
    }
}
