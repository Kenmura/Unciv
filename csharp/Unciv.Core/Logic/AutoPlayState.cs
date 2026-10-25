namespace Unciv.Logic;

// T228: the engine's IAutoPlay, Kotlin AutoPlay.kt (ui/screens/worldscreen/unit) without the UI parts: no worker thread (autoPlayJob,
// runAutoPlayJobInNewThread), no Timers, no WorldScreen. The commands StartAutoPlay / StopAutoPlay / AutoPlayTurn drive it, so it replays.
// Deviation: Kotlin reads GameSettings.autoPlay (autoPlayMaxTurns, autoPlayUntilEnd, fullAutoPlayAI) when it starts; here the command carries them.

/// <summary>The auto-play of one human civ: a number of turns (or until the end) played by the AI automation.</summary>
public sealed class AutoPlayState : IAutoPlay
{
    /// <summary>The civ being auto-played (Kotlin: the world screen's viewing civ).</summary>
    public string CivId { get; private set; } = "";

    /// <summary>How many turns we should multiturn AutoPlay for; not decremented when <see cref="UntilEnd"/>.</summary>
    public int TurnsToAutoPlay { get; private set; }

    public bool UntilEnd { get; private set; }
    public bool FullAutoPlayAI { get; private set; }

    /// <summary>Whether the auto-played turn is being processed right now.</summary>
    public bool AutoPlayTurnInProgress { get; set; }

    /// <summary><c>startMultiturnAutoPlay</c> with the settings given.</summary>
    public void StartMultiturnAutoPlay(string civId, int turns, bool fullAutoPlayAI, bool untilEnd)
    {
        CivId = civId;
        AutoPlayTurnInProgress = false;
        TurnsToAutoPlay = turns;
        FullAutoPlayAI = fullAutoPlayAI;
        UntilEnd = untilEnd;
    }

    /// <summary><c>endTurnMultiturnAutoPlay</c></summary>
    public void EndTurnMultiturnAutoPlay()
    {
        if (!UntilEnd && TurnsToAutoPlay > 0)
            TurnsToAutoPlay--;
    }

    /// <summary><c>stopAutoPlay</c></summary>
    public void StopAutoPlay()
    {
        TurnsToAutoPlay = 0;
        AutoPlayTurnInProgress = false;
    }

    public bool IsAutoPlaying() => TurnsToAutoPlay > 0 || AutoPlayTurnInProgress;

    public bool IsAutoPlayingAndFullAutoPlayAI() => IsAutoPlaying() && FullAutoPlayAI;

    public bool IsAutoPlayingFor(string civId) => civId == CivId && IsAutoPlaying();

    /// <summary><c>shouldContinueAutoPlaying</c>: at least 1 more turn and none being processed.</summary>
    public bool ShouldContinueAutoPlaying() => !AutoPlayTurnInProgress && TurnsToAutoPlay > 0;
}
