using Unciv.Commands;
using Unciv.Commands.Turn;
using Unciv.Logic;
using Unciv.Logic.Civilization;

namespace Unciv.Net.Turns;

/// <summary>
/// Manages turn deadlines, disconnect grace periods, and absent player handling for simultaneous turns (SERVER.md 4).
/// </summary>
public sealed class TurnTimer
{
    private readonly GameSession session;
    private readonly TurnPolicy policy;
    private readonly IClock clock;
    private readonly Dictionary<string, PlayerTurnState> playerStates = new(StringComparer.Ordinal);
    private readonly HashSet<string> pendingReclaims = new(StringComparer.Ordinal);
    private int currentTurn;

    public TurnTimer(GameSession session, TurnPolicy? policy = null, IClock? clock = null, bool autoResolve = true)
    {
        this.session = session;
        this.policy = policy ?? TurnPolicy.Default;
        this.clock = clock ?? SystemClock.Instance;
        AutoResolve = autoResolve;

        currentTurn = session.Game.Turns;
        InitializeLivingHumans();
        ResetTurnDeadline();
    }

    public GameSession Session => session;

    public TurnPolicy Policy => policy;

    public IClock Clock => clock;

    public bool AutoResolve { get; set; }

    /// <summary>Optional interceptor for server commands (e.g. GameHost broadcasting view deltas).</summary>
    public Func<GameCommand, Unciv.Commands.CommandResult>? ServerCommandSubmitter { get; set; }

    private Unciv.Commands.CommandResult SubmitServer(GameCommand command) =>
        ServerCommandSubmitter != null ? ServerCommandSubmitter(command) : session.SubmitAsServer(command);

    /// <summary>Absolute deadline (epoch milliseconds) for the current turn, or null when the turn timer is off.</summary>
    public long? TurnDeadline { get; private set; }

    /// <summary>Whether <paramref name="civId"/> is marked as currently connected.</summary>
    public bool IsConnected(string civId) =>
        playerStates.TryGetValue(civId, out var state) && state.IsConnected;

    /// <summary>Returns the number of consecutive absent turns for <paramref name="civId"/>.</summary>
    public int GetAbsentTurns(string civId) =>
        playerStates.TryGetValue(civId, out var state) ? state.AbsentTurns : 0;

    /// <summary>Whether <paramref name="civId"/> had its turn force-ended due to disconnect grace expiry this turn.</summary>
    public bool WasForceEndedByDisconnect(string civId) =>
        playerStates.TryGetValue(civId, out var state) && state.ForceEndedByDisconnect;

    /// <summary>Marks a player as connected. If their turn was force-ended due to disconnect grace expiry, undos it.</summary>
    public void Connect(string civId, string? playerKeyId = null)
    {
        var state = GetOrCreateState(civId);
        state.IsConnected = true;
        state.DisconnectedAt = null;
        if (!string.IsNullOrEmpty(playerKeyId))
            state.PlayerKeyId = playerKeyId;

        // Reset absent count since player is actively connected
        state.AbsentTurns = 0;

        // If the player was force-ended due to disconnect grace while still in Playing phase, undo the end turn
        if (state.ForceEndedByDisconnect && session.Game.TurnState is { Phase: SimultaneousPhase.Playing })
        {
            var civ = session.Game.GetCivilizationOrNull(civId);
            if (civ != null && civ.IsHuman() && session.Game.HasEndedTurn(civ))
            {
                session.Submit(civId, new UndoEndTurn());
            }
            state.ForceEndedByDisconnect = false;
        }

        // If civ was taken over by AI, reclaim it
        var currentCiv = session.Game.GetCivilizationOrNull(civId);
        if (currentCiv != null && currentCiv.PlayerType == PlayerType.AI)
        {
            Reclaim(civId, state.PlayerKeyId);
        }
    }

    /// <summary>Marks a player as disconnected and starts their disconnect grace timer.</summary>
    public void Disconnect(string civId)
    {
        var state = GetOrCreateState(civId);
        state.IsConnected = false;
        state.DisconnectedAt = clock.NowMillis;
    }

    /// <summary>C045: the seat of <paramref name="civId"/> changed hands (kick or rebind): drops a queued reclaim and the remembered key of the old player.</summary>
    public void ForgetPlayer(string civId)
    {
        pendingReclaims.Remove(civId);
        if (playerStates.TryGetValue(civId, out var state)) state.PlayerKeyId = null;
    }

    /// <summary>Reclaims an AI-controlled civ back to human control.</summary>
    public bool Reclaim(string civId, string? playerKeyId = null)
    {
        var civ = session.Game.GetCivilizationOrNull(civId);
        if (civ == null || !civ.IsMajorCiv() || civ.IsSpectator() || civ.IsDefeated())
            return false;

        var state = GetOrCreateState(civId);
        if (!string.IsNullOrEmpty(playerKeyId))
            state.PlayerKeyId = playerKeyId;

        if (session.Game.TurnState is { Phase: SimultaneousPhase.Playing })
        {
            if (civ.PlayerType == PlayerType.AI)
            {
                var result = session.SubmitAsServer(new SetController
                {
                    CivId = civId,
                    Controller = PlayerType.Human,
                    PlayerId = state.PlayerKeyId ?? "",
                });
                if (result.Accepted)
                {
                    state.AbsentTurns = 0;
                    pendingReclaims.Remove(civId);
                    return true;
                }
                return false;
            }
            state.AbsentTurns = 0;
            return true;
        }

        // In resolving: mark for reclaim as soon as Playing phase starts
        pendingReclaims.Add(civId);
        return true;
    }

    /// <summary>
    /// Checks disconnect grace periods, turn timer expiry, and absent player tracking.
    /// Drives server commands and resolution pump as needed.
    /// </summary>
    public void Tick()
    {
        // 1. If currently resolving and auto-resolve is on, pump until playing
        if (AutoResolve && session.Game.TurnState is { Phase: SimultaneousPhase.Resolving })
        {
            ResolvePump.Pump(session);
        }

        // 2. Check for turn advance
        if (session.Game.Turns != currentTurn)
        {
            OnTurnAdvanced();
        }

        if (session.Game.TurnState is not { Phase: SimultaneousPhase.Playing })
            return;

        var game = session.Game;
        var now = clock.NowMillis;

        // 3. Check disconnect grace expiry
        foreach (var civ in game.Civilizations.Where(c => c.IsHuman() && !c.IsDefeated()))
        {
            if (playerStates.TryGetValue(civ.CivId, out var state) && !state.IsConnected && !game.HasEndedTurn(civ))
            {
                if (state.DisconnectedAt.HasValue && now - state.DisconnectedAt.Value >= policy.DropGraceSeconds * 1000L)
                {
                    var result = SubmitServer(new ForceEndTurn { CivId = civ.CivId });
                    if (result.Accepted)
                    {
                        state.ForceEndedByDisconnect = true;
                    }
                }
            }
        }

        // If disconnect grace expiry caused all players to end and entered Resolving
        if (AutoResolve && session.Game.TurnState is { Phase: SimultaneousPhase.Resolving })
        {
            ResolvePump.Pump(session);
            if (session.Game.Turns != currentTurn)
            {
                OnTurnAdvanced();
            }
            return;
        }

        // 4. Check turn timer expiry
        if (policy.TurnTimerSeconds > 0 && TurnDeadline.HasValue && now >= TurnDeadline.Value)
        {
            // ForceEndTurn in civ-index order for all living human civs that have not ended
            var unended = game.Civilizations
                .Where(c => c.IsHuman() && !c.IsDefeated() && !game.HasEndedTurn(c))
                .OrderBy(c => game.Civilizations.IndexOf(c))
                .ToList();

            foreach (var civ in unended)
            {
                SubmitServer(new ForceEndTurn { CivId = civ.CivId });
            }

            if (AutoResolve && session.Game.TurnState is { Phase: SimultaneousPhase.Resolving })
            {
                ResolvePump.Pump(session);
                if (session.Game.Turns != currentTurn)
                {
                    OnTurnAdvanced();
                }
            }
        }
    }

    /// <summary>
    /// A round has just finished resolving (the turn counter moved and Playing began): restarts the deadline now, so the delta of the round's last step carries the new turn's
    /// deadline. Only the deadline: the rest of the turn-advance bookkeeping stays in <see cref="Tick"/>, because it can submit server commands and must not run inside one.
    /// </summary>
    public void RefreshDeadline()
    {
        if (session.Game.Turns != currentTurn && session.Game.TurnState is { Phase: SimultaneousPhase.Playing })
            ResetTurnDeadline();
    }

    /// <summary>Constructs a <see cref="TurnStatus"/> protocol message for clients (SERVER.md 6.1).</summary>
    public TurnStatus CreateTurnStatus()
    {
        var game = session.Game;
        return new TurnStatus
        {
            Turn = game.Turns,
            Ended = game.TurnState?.Ended.ToList() ?? [],
            Deadline = TurnDeadline,
            Phase = (game.TurnState?.Phase ?? SimultaneousPhase.Playing).ToString(),
        };
    }

    private void OnTurnAdvanced()
    {
        // 1. Process absent turns for the completed turn
        foreach (var civ in session.Game.Civilizations.Where(c => c.IsMajorCiv() && !c.IsDefeated()))
        {
            var state = GetOrCreateState(civ.CivId);
            if (civ.IsHuman())
            {
                if (!state.IsConnected)
                {
                    state.AbsentTurns++;
                }
                else
                {
                    state.AbsentTurns = 0;
                }
            }
            state.ForceEndedByDisconnect = false;
        }

        // 2. Process AI takeover for civs that exceeded absent turns threshold
        if (policy.OnDisconnect == DisconnectAction.AiTakeover)
        {
            var eligibleForTakeover = session.Game.Civilizations
                .Where(c => c.IsHuman() && !c.IsDefeated() && GetAbsentTurns(c.CivId) >= policy.AbsentTurnsBeforeAiTakeover)
                .OrderBy(c => session.Game.Civilizations.IndexOf(c))
                .ToList();

            foreach (var civ in eligibleForTakeover)
            {
                // SetController refuses if this is the last human player
                if (HasOtherHumans(civ))
                {
                    SubmitServer(new SetController
                    {
                        CivId = civ.CivId,
                        Controller = PlayerType.AI,
                    });
                }
            }
        }

        // 3. Process pending reclaims
        if (pendingReclaims.Count > 0)
        {
            var toReclaim = pendingReclaims.ToList();
            pendingReclaims.Clear();
            foreach (var civId in toReclaim)
            {
                var state = GetOrCreateState(civId);
                var civ = session.Game.GetCivilizationOrNull(civId);
                if (civ != null && civ.PlayerType == PlayerType.AI)
                {
                    session.SubmitAsServer(new SetController
                    {
                        CivId = civId,
                        Controller = PlayerType.Human,
                        PlayerId = state.PlayerKeyId ?? "",
                    });
                }
                state.AbsentTurns = 0;
            }
        }

        currentTurn = session.Game.Turns;
        ResetTurnDeadline();
    }

    private bool HasOtherHumans(Civilization civ) =>
        session.Game.Civilizations.Any(c => c != civ && c.IsHuman() && !c.IsSpectator() && !c.IsDefeated());

    private void ResetTurnDeadline()
    {
        if (policy.TurnTimerSeconds > 0 && session.Game.TurnState is { Phase: SimultaneousPhase.Playing })
        {
            TurnDeadline = clock.NowMillis + policy.TurnTimerSeconds * 1000L;
        }
        else
        {
            TurnDeadline = null;
        }
    }

    private void InitializeLivingHumans()
    {
        foreach (var civ in session.Game.Civilizations.Where(c => c.IsHuman() && !c.IsDefeated()))
        {
            var state = GetOrCreateState(civ.CivId);
            state.IsConnected = true;
            state.PlayerKeyId = civ.PlayerId;
        }
    }

    private PlayerTurnState GetOrCreateState(string civId)
    {
        if (!playerStates.TryGetValue(civId, out var state))
        {
            state = new PlayerTurnState();
            playerStates[civId] = state;
        }
        return state;
    }

    private sealed class PlayerTurnState
    {
        public bool IsConnected { get; set; } = true;
        public long? DisconnectedAt { get; set; }
        public bool ForceEndedByDisconnect { get; set; }
        public int AbsentTurns { get; set; }
        public string? PlayerKeyId { get; set; }
    }
}
