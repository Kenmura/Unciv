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

    /// <summary>C040: how many civs the auto players will process from here before a civ that needs input (at most one full round); 0 when none is pending. Read-only: for a progress display.</summary>
    public int PendingAutoPlayerCount()
    {
        if (pendingPlayerIndex is not { } start) return 0;
        var count = 0;
        while (count < Civilizations.Count && ShouldAutoProcess(Civilizations[(start + count) % Civilizations.Count])) count++;
        return count;
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
        RefreshTransientCaches(); // T218: every civ's turn ends canonical, exactly like a resolve step of a simultaneous round
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
            RefreshTransientCaches(); // T218

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

        RefreshTransientCaches();
    }

    /// <summary>
    /// T208: the canonical point at which every lazily maintained cache equals what SetTransients would rebuild, so the running game and a
    /// reload of its own save continue identically (lockstep replay, join-in-progress). Upstream refreshes these lazily (open borders only at
    /// turn start; civ-wide vision only when a unit's view leaves the own borders), which a reload then "corrects".
    /// </summary>
    internal void RefreshTransientCaches()
    {
        // T219: the game-wide maps, the flag maps and the religion belief sets (all JVM-style hash tables) too.
        Religions.Canonicalize();
        foreach (var religion in Religions.Values) religion.Canonicalize();
        DiplomaticVictoryVotesCast.Canonicalize();
        foreach (var civInfo in Civilizations)
        {
            civInfo.Diplomacy.Canonicalize();
            civInfo.Proximity.Canonicalize();
            civInfo.FlagsCountdown.Canonicalize();
            civInfo.DisabledCityConstructions.Canonicalize();
            civInfo.PassableImpassables.Canonicalize();
            civInfo.Tech.TechsResearched.Canonicalize();
            civInfo.Tech.TechsInProgress.Canonicalize();
            civInfo.GreatPeople.LongCountGPPool.Canonicalize();
            civInfo.QuestManager.Canonicalize();
            foreach (var diplomacyManager in civInfo.Diplomacy.Values)
            {
                diplomacyManager.FlagsCountdown.Canonicalize();
                diplomacyManager.DiplomaticModifiers.Canonicalize();
            }
            // Hash sets that saw removals or resizes iterate differently from their own reload (which re-adds in saved order): rebuild them so.
            civInfo.Policies.AdoptedPolicies.Canonicalize();
            foreach (var city in civInfo.Cities)
            {
                city.CityConstructions.BuiltBuildings.Canonicalize();
                city.CityConstructions.SetTransients(); // the cached building objects (and unique map) are in research order while running, set order after a load
                foreach (var provided in city.CityConstructions.FreeBuildingsProvidedFromThisCity.Values) provided.Canonicalize();
                city.CityConstructions.FreeBuildingsProvidedFromThisCity.Canonicalize();
                city.DisabledConstructions.Canonicalize();
                city.Tiles.Canonicalize();
                city.WorkedTiles.Canonicalize();
                city.LockedTiles.Canonicalize();
                city.FlagsCountdown.Canonicalize();
                city.CityConstructions.InProgressConstructions.Canonicalize();
                city.UnitShouldUseSavedPromotion.Canonicalize();
                city.UnitToPromotions.Canonicalize();
                foreach (var promotions in city.UnitToPromotions.Values) promotions.Promotions.Canonicalize();
                city.Religion.Canonicalize();
            }
            foreach (var unit in civInfo.Units.GetCivUnits())
            {
                unit.Promotions.Promotions.Canonicalize();
                unit.AbilityToTimesUsed.Canonicalize();
                unit.StatusMap.Canonicalize();
            }
        }
        foreach (var civInfo in Civilizations) civInfo.Tech.RebuildTransients();
        // Military might is cached until the next turn start; a reload recomputes it on first use (T227: the Civ 5 AI reads force mid-turn).
        foreach (var civInfo in Civilizations) civInfo.ResetMilitaryMightCache();
        foreach (var civInfo in Civilizations)
            foreach (var diplomacyManager in civInfo.Diplomacy.Values.ToList()) // T218: a refresh between steps may find a civ whose manager is still missing
                diplomacyManager.UpdateHasOpenBorders();
        foreach (var civInfo in Civilizations) civInfo.Units.SortById(); // T231: creation order, as SetTransients does
        foreach (var civInfo in Civilizations)
            foreach (var city in civInfo.Cities)
                foreach (var tile in city.GetWorkedTiles().ToList())
                    if (!ReferenceEquals(tile.GetOwner(), civInfo))
                        city.StopWorkingTile(tile);
        // The tail of SetTransients: vision, civ-wide caches, then city and civ stats (Civ.Stats is not serialised, a load recomputes it).
        UpdateCivilizationState();
        // A civ without cities is never reached by the city stat updates above: its Stats keep the value of its last update while a load starts
        // them at zero. Recompute them so both agree.
        foreach (var civInfo in Civilizations.Where(it => it.Cities.Count == 0))
            civInfo.UpdateStatsForNextTurn();
    }

    /// <summary>We rotate Players in cycle: 1,2...N,1,2...</summary>
    private int AdvancePlayerIndex(int playerIndex)
    {
        playerIndex = (playerIndex + 1) % Civilizations.Count;
        if (playerIndex == 0)
        {
            RefreshTransientCaches(); // T218: the stats the ranking history reads are canonical (a mid-round reload recomputes them)
            RecordRankingStats();
            ProcessLeagueTurn();
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
