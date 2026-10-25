using System.Text.Json.Serialization;
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

    private bool IsSimultaneous => TurnState is { Mode: TurnMode.Simultaneous };

    private bool IsActiveHuman(CivilizationClass civ) => ActiveHumans().Contains(civ);

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
        turnStartedCivs.Remove(civ.CivId); // a human defeated during the round has no turn left to end
        new TurnManager(civ).StartTurn(progressBar);
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
        // T218: Civ.Stats (Happiness, StatsForNextTurn) is not serialised and a load recomputes it, while a running game holds the value of its
        // last update; recompute it here so the ranking history recorded below is the same live and after a mid-round reload.
        foreach (var civ in Civilizations)
            civ.UpdateStatsForNextTurn();
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
            var first = ActiveHumans().FirstOrDefault();
            if (first != null)
            {
                CurrentPlayer = first.CivId;
                CurrentPlayerCiv = first;
            }
        }
        SyncSimultaneousState();
    }

    // turnStartedCivs is the runtime set; the saved list in civ-index order is what SetTransients restores it from.
    private void SyncSimultaneousState()
    {
        if (TurnState is not { Mode: TurnMode.Simultaneous } state) return;
        state.Started = Civilizations.Where(it => turnStartedCivs.Contains(it.CivId)).Select(it => it.CivId).ToList();
    }
}
