using System.Text.Json.Serialization;
using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Managers;
using Unciv.Utils;
using CivilizationClass = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Logic;

// T200: simultaneous human turns (design/SIMULTANEOUS-TURNS.md section 3), composed from the civ-parameterised phases of GameInfoTurnPhases.cs.
// A round: all active humans play (any order, commands are another ticket), then the host resolves it in a fixed civ-index order:
//   1. ProcessEndTurn(h) for each human   2. RunAiTurn(c) for each auto civ   3. StartNextTurn   4. BeginTurn(h) for each human.
// With exactly one human at civ index 0 this is NextTurn's order, so the state hashes are the same (EndToEndDeterminismTests).
//
// Deviations / decisions:
// - Active humans are the human, non-spectator, non-defeated civs. Defeated humans and spectators are auto civs (step 2). If every human is
//   defeated, the first human that is not a spectator stays active (Kotlin: "if all players are defeated then let the first player control next turn").
// - RunAiTurn has no simulation win-check break (that quirk of RunAutoPlayers only exists to stop a simulation inside the sequential loop).
// - CurrentPlayer names the lowest-index active human as a nominal cursor; TurnState is authoritative.
// - Every step validates itself against NextResolveStep, which scans the state only, so a half-resolved round resumes from a loaded save.

public partial class GameInfo
{
    /// <summary>The simultaneous-turn state; <c>null</c> for a Sequential game (and then not written to the save).</summary>
    [JsonPropertyName("turnState")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TurnState? TurnState { get; set; }

    [JsonIgnore]
    public TurnMode TurnMode => TurnState?.Mode ?? TurnMode.Sequential;

    bool Unciv.Logic.Civilization.IGameInfoState.IsSimultaneousTurns => IsSimultaneous;

    bool Unciv.Logic.Civilization.IGameInfoState.IsCivActive(CivilizationClass civ) => IsActive(civ);

    private bool IsSimultaneous => TurnState is { Mode: TurnMode.Simultaneous };

    /// <summary>Whether <paramref name="civ"/> is one of the humans the round waits for (human, not a spectator, not defeated unless every human is).</summary>
    internal bool IsActiveHuman(CivilizationClass civ) => ActiveHumans().Contains(civ);

    /// <summary>T202: whether <paramref name="civ"/> has ended its turn in the round being played.</summary>
    public bool HasEndedTurn(CivilizationClass civ) => TurnState is { Mode: TurnMode.Simultaneous } state && state.Ended.Contains(civ.CivId);

    /// <summary>The active humans in civ-index order.</summary>
    private List<CivilizationClass> ActiveHumans()
    {
        var humans = Civilizations.Where(it => it.IsHuman() && !it.IsSpectator()).ToList();
        var alive = humans.Where(it => !it.IsDefeated()).ToList();
        return alive.Count > 0 ? alive : humans.Take(1).ToList();
    }

    /// <summary>
    /// Switches a Sequential game that waits for input to simultaneous turns: every active human has its turn begun
    /// (a new game never ran <c>StartTurn</c> for them, see the file header of GameInfoTurnPhases).
    /// </summary>
    public void StartSimultaneousTurns()
    {
        if (IsSimultaneous) throw new InvalidOperationException("The game already has simultaneous turns");
        if (pendingPlayerIndex != null) throw new InvalidOperationException("A sequential turn is in progress");
        TurnState = new TurnState { Mode = TurnMode.Simultaneous };
        turnStartedCivs.Clear();
        foreach (var human in ActiveHumans()) turnStartedCivs.Add(human.CivId);
        SyncSimultaneousState();
    }

    /// <summary>Whether <paramref name="civ"/> can act now: its turn has begun, it has not ended it, and the round is not being resolved.</summary>
    public bool IsActive(CivilizationClass civ) =>
        TurnState is { Mode: TurnMode.Simultaneous } state
            ? state.Phase == SimultaneousPhase.Playing && IsTurnStarted(civ) && IsActiveHuman(civ) && !state.Ended.Contains(civ.CivId)
            : IsTurnStarted(civ) && ReferenceEquals(civ, CurrentPlayerCiv);

    /// <summary>The human <paramref name="civ"/> ends its turn (Playing). It stays started until its <see cref="ProcessEndTurn"/>.</summary>
    public void EndHumanTurn(CivilizationClass civ)
    {
        var state = RequireSimultaneous();
        if (state.Phase != SimultaneousPhase.Playing) throw new InvalidOperationException("The round is being resolved");
        if (!IsActive(civ)) throw new InvalidOperationException($"{civ.CivId} is not active or has already ended its turn");
        state.Ended.Add(civ.CivId);
    }

    /// <summary>Takes back <see cref="EndHumanTurn"/> while the round is still Playing.</summary>
    public void UndoEndHumanTurn(CivilizationClass civ)
    {
        var state = RequireSimultaneous();
        if (state.Phase != SimultaneousPhase.Playing || !state.Ended.Remove(civ.CivId))
            throw new InvalidOperationException($"{civ.CivId} has not ended its turn, or the round is being resolved");
    }

    /// <summary>Whether every active human has ended its turn (the host may then, or on a timer after ending the rest, call <see cref="BeginResolving"/>).</summary>
    public bool AllHumansEnded() => TurnState is { Mode: TurnMode.Simultaneous, Phase: SimultaneousPhase.Playing } state
        && ActiveHumans().All(it => state.Ended.Contains(it.CivId));

    /// <summary>
    /// T202: <see cref="BeginResolving"/> when the round is Playing, has an active human and every one of them ended. Returns whether it did, so the
    /// command that completed the set (EndTurn, ForceEndTurn, a controller change, Resign) logs the transition by itself and replay needs no extra entry.
    /// </summary>
    public bool BeginResolvingIfAllEnded()
    {
        if (!AllHumansEnded() || ActiveHumans().Count == 0) return false;
        BeginResolving();
        return true;
    }

    /// <summary>
    /// T202: hands <paramref name="civ"/> to a human (<paramref name="human"/>, with the lobby key <paramref name="playerId"/>) or to the AI, Playing only.
    /// To the AI: the civ leaves <see cref="TurnState.Ended"/> and is played in step 2 of the round (its begun turn is not started again). To a human:
    /// its turn begins now (<c>StartTurn</c>, as <see cref="BeginTurn"/> does for the others) unless it already has, because an AI civ only runs its
    /// <c>StartTurn</c> inside step 2, so a joiner would otherwise never be able to act or end the round.
    /// </summary>
    public void SetHumanController(CivilizationClass civ, bool human, string playerId)
    {
        var state = RequireSimultaneous();
        if (state.Phase != SimultaneousPhase.Playing) throw new InvalidOperationException("The round is being resolved");
        state.Ended.Remove(civ.CivId);
        if (human)
        {
            civ.PlayerType = PlayerType.Human;
            civ.PlayerId = playerId;
            if (!IsTurnStarted(civ)) StartCivTurn(civ, null);
        }
        else
        {
            civ.PlayerType = PlayerType.AI;
            civ.PlayerId = "";
            if (ClientState.AutoPlay is { } autoPlay && autoPlay.IsAutoPlayingFor(civ.CivId)) autoPlay.StopAutoPlay();
        }
        // When the last living human leaves, the first (defeated) human takes over as the one the round waits for (see ActiveHumans); that civ has been
        // played as an auto civ so far and its turn has not begun, so begin it now or nobody could ever end the round (found in the T202 review).
        foreach (var active in ActiveHumans())
            if (!IsTurnStarted(active)) StartCivTurn(active, null);
        UpdateSimultaneousCursor();
        SyncSimultaneousState();
    }

    /// <summary>Playing to Resolving. Every active human must have ended its turn.</summary>
    public void BeginResolving()
    {
        var state = RequireSimultaneous();
        if (!AllHumansEnded()) throw new InvalidOperationException("Not every active human has ended its turn");
        state.Phase = SimultaneousPhase.Resolving;
        state.Stage = ResolveStage.EndTurns;
        state.Next = 0;
    }

    /// <summary>
    /// The next step of the round being resolved, <c>null</c> when none is being resolved. A pure function of the state: calling it again, or on
    /// a loaded save, gives the same answer. Victory does not stop it: the host decides what to do with <see cref="TurnPhase.GameOver"/>.
    /// </summary>
    public ResolveStep? NextResolveStep()
    {
        if (TurnState is not { Mode: TurnMode.Simultaneous, Phase: SimultaneousPhase.Resolving } state) return null;
        var active = ActiveHumans();
        for (var stage = state.Stage; stage <= ResolveStage.BeginTurns; stage++)
        {
            var from = stage == state.Stage ? state.Next : 0;
            switch (stage)
            {
                case ResolveStage.EndTurns:
                    for (var i = from; i < Civilizations.Count; i++)
                        if (active.Contains(Civilizations[i]) && IsTurnStarted(Civilizations[i])) return new ResolveStep.ProcessEndTurn(Civilizations[i].CivId);
                    break;
                case ResolveStage.AiRound:
                    for (var i = from; i < Civilizations.Count; i++)
                        if (!active.Contains(Civilizations[i])) return new ResolveStep.RunAiTurn(Civilizations[i].CivId);
                    break;
                case ResolveStage.NextTurn:
                    return new ResolveStep.StartNextTurn();
                case ResolveStage.BeginTurns:
                    for (var i = from; i < Civilizations.Count; i++)
                        if (active.Contains(Civilizations[i]) && !IsTurnStarted(Civilizations[i])) return new ResolveStep.BeginTurn(Civilizations[i].CivId);
                    break;
            }
        }
        return null;
    }

    /// <summary>Step 1: <c>TurnManager.EndTurn</c> of a human, the same work as <see cref="EndPlayerTurn"/> without moving on to the next civ.</summary>
    public void ProcessEndTurn(CivilizationClass civ, long nowMillis, Action? progressBar = null, bool shouldGainTime = false)
    {
        var state = Expect(new ResolveStep.ProcessEndTurn(civ.CivId));
        EndCivTurn(civ, nowMillis, progressBar, shouldGainTime);
        Advance(state, ResolveStage.EndTurns, civ);
    }

    /// <summary>Step 2: StartTurn, AutomateTurn, EndTurn of a civ that plays automatically (the body of <see cref="RunAutoPlayers"/>).</summary>
    public void RunAiTurn(CivilizationClass civ, Action? progressBar = null)
    {
        var state = Expect(new ResolveStep.RunAiTurn(civ.CivId));
        // T202: a civ whose turn already began (a human handed to the AI, or defeated, during Playing) must not run StartTurn a second time.
        var alreadyStarted = turnStartedCivs.Remove(civ.CivId);
        if (!alreadyStarted) new TurnManager(civ).StartTurn(progressBar);
        new TurnManager(civ).AutomateTurn();
        var autoPlay = ClientState.AutoPlay;
        if (autoPlay != null && autoPlay.IsAutoPlaying() && civ.VictoryManager.HasWon() && !OneMoreTurnMode)
            autoPlay.StopAutoPlay();
        new TurnManager(civ).EndTurn(progressBar);
        Advance(state, ResolveStage.AiRound, civ);
    }

    /// <summary>Step 3: the round is over for everybody, <c>Turns++</c> (what <see cref="EndPlayerTurn"/> does when the civ cycle wraps).</summary>
    public void StartNextTurn()
    {
        var state = Expect(new ResolveStep.StartNextTurn());
        RefreshTransientCaches(); // T218: as AdvancePlayerIndex does at the wrap of the sequential cycle
        RecordRankingStats();
        Turns++;
        if (EngineOptions.SimulateUntilTurn != 0)
            Log.Debug("Starting simulation of turn %s", Turns);
        state.Stage = ResolveStage.BeginTurns;
        state.Next = 0;
        CompleteRoundIfDone(state);
    }

    /// <summary>Step 4: the next turn of a human begins (<see cref="BeginPlayerTurn"/> for one civ). The last one returns the game to Playing.</summary>
    public void BeginTurn(CivilizationClass civ, long nowMillis, Action? progressBar = null)
    {
        var state = Expect(new ResolveStep.BeginTurn(civ.CivId));
        if (Turns == EngineOptions.SimulateUntilTurn)
            EngineOptions.SimulateUntilTurn = 0;
        CurrentTurnStartTime = nowMillis;
        StartCivTurn(civ, progressBar);
        Advance(state, ResolveStage.BeginTurns, civ);
    }

    private TurnState RequireSimultaneous() =>
        TurnState is { Mode: TurnMode.Simultaneous } state ? state : throw new InvalidOperationException("The game does not have simultaneous turns");

    private TurnState Expect(ResolveStep step)
    {
        var state = RequireSimultaneous();
        var actual = NextResolveStep();
        if (actual != step) throw new InvalidOperationException($"out-of-sequence: expected {actual?.ToString() ?? "no step"}, got {step}");
        return state;
    }

    private void Advance(TurnState state, ResolveStage stage, CivilizationClass civ)
    {
        state.Stage = stage;
        state.Next = Civilizations.IndexOf(civ) + 1;
        RefreshTransientCaches(); // T218: every resolve step ends canonical, so a host snapshot between steps reloads to the live state
        CompleteRoundIfDone(state);
    }

    // After a step: when none is left the round is resolved and the humans play again. Also keeps the saved started list in sync.
    private void CompleteRoundIfDone(TurnState state)
    {
        if (NextResolveStep() == null)
        {
            state.Phase = SimultaneousPhase.Playing;
            state.Ended.Clear();
            state.Stage = ResolveStage.EndTurns;
            state.Next = 0;
            UpdateSimultaneousCursor();
        }
        SyncSimultaneousState();
    }

    // CurrentPlayer is the nominal cursor of a simultaneous game: the lowest-index active human.
    private void UpdateSimultaneousCursor()
    {
        var first = ActiveHumans().FirstOrDefault();
        if (first == null) return;
        CurrentPlayer = first.CivId;
        CurrentPlayerCiv = first;
    }

    // turnStartedCivs is the runtime set; the saved list in civ-index order is what SetTransients restores it from.
    private void SyncSimultaneousState()
    {
        if (TurnState is not { Mode: TurnMode.Simultaneous } state) return;
        state.Started = Civilizations.Where(it => turnStartedCivs.Contains(it.CivId)).Select(it => it.CivId).ToList();
    }
}
