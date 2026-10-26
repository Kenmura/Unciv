using Unciv.Logic.Civilization.Managers;
using Unciv.Utils;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Logic;

// T046: the client-side state that Kotlin GameInfo and its callers read from UncivGame.Current (settings, world screen). The engine has no
// UncivGame singleton, so the values the game rules read are an instance owned by the GameInfo (not serialized, not part of the game state):
// the client (Godot) sets them; a headless game keeps the defaults of Kotlin's GameSettings.

/// <summary>Client settings and UI state that the game rules read. Not saved, not synchronised between peers.</summary>
public sealed class GameClientState
{
    /// <summary><c>settings.alternateUnitCycleOrder</c></summary>
    public bool AlternateUnitCycleOrder { get; set; }

    /// <summary><c>worldScreen?.autoPlay?.isAutoPlayingAndFullAutoPlayAI() == true</c></summary>
    public bool IsFullAutoPlayAI
    {
        get => isFullAutoPlayAI || AutoPlay?.IsAutoPlayingAndFullAutoPlayAI() == true;
        set => isFullAutoPlayAI = value;
    }

    private bool isFullAutoPlayAI;

    /// <summary>Test seam standing in for the Mockito spy of NextTurnAutomationTest, which runs an action the first time automation reaches a unit
    /// (Kotlin spies <c>getDamageFromTerrain</c>, the first thing <c>automateUnitMoves</c> asks of a unit). Always null in the game.</summary>
    internal Action<View.MapUnitView>? BeforeUnitMoves { get; set; }

    /// <summary><c>NextTurnAutomation</c>: the real automation by default. Tests (and clients) may replace it; null skips the calls.</summary>
    public INextTurnAutomation? NextTurnAutomation { get; set; } = Automation.Civilization.NextTurnAutomationHook.Instance;

    /// <summary>Replaces the valuation <c>TradeEvaluation()</c> used by TurnManager and the diplomacy automation (tests install stand-ins). Null means <see cref="Trade.TradeEvaluation.Default"/>.</summary>
    public ITradeValidity? TradeEvaluation { get; set; }

    /// <summary><c>settings.multiplayer.getUserId()</c>: only <c>GameInfo.GetPlayerToViewAs</c> (a client view choice) reads it; no game rule may (T183).</summary>
    public string UserId { get; set; } = "";

    /// <summary><c>worldScreen?.autoPlay</c>: null when there is no client (headless simulation).</summary>
    public IAutoPlay? AutoPlay { get; set; }

    /// <summary><c>settings.tutorialTasksCompleted</c></summary>
    public LinkedHashSet<string> TutorialTasksCompleted { get; } = [];

    /// <summary><c>settings.getCollatorFromLocale()</c>: the culture-sensitive name ordering. Defaults to the invariant culture.</summary>
    public IComparer<string> Collator { get; set; } = StringComparer.InvariantCulture;

    /// <summary>
    /// What the PlaySound/ChooseMusic triggers call (Kotlin: the client's <c>SoundPlayer</c> and <c>musicController</c>). C057 deliberate deviation: the default is
    /// <see cref="SoundCueAudio"/>, which turns a sound into a <see cref="Commands.Events.SoundRequested"/> event the client plays from its event stream, so the engine never
    /// touches a sound device and the trigger behaves the same headless, hosted and replayed. Null means "no audio" (the triggers do not fire).
    /// </summary>
    public IClientAudio? Audio { get; set; } = SoundCueAudio.Instance;

    /// <summary><c>settings.addCompletedTutorialTask(task)</c> without the settings file save and the UI flag.</summary>
    public bool AddCompletedTutorialTask(string tutorialTask) => TutorialTasksCompleted.Add(tutorialTask);
}

/// <summary>What <c>nextTurn</c> calls on <c>UncivGame.Current.worldScreen.autoPlay</c> (client state, set by the client).</summary>
public interface IAutoPlay
{
    /// <summary><c>autoPlay.isAutoPlaying()</c></summary>
    bool IsAutoPlaying();

    /// <summary><c>autoPlay.stopAutoPlay()</c></summary>
    void StopAutoPlay();

    /// <summary><c>autoPlay.isAutoPlayingAndFullAutoPlayAI()</c></summary>
    bool IsAutoPlayingAndFullAutoPlayAI() => false;

    /// <summary>The <c>selectedGameView.civView.getCiv() == this &amp;&amp; autoPlay.isAutoPlaying()</c> of <c>Civilization.isAIOrAutoPlaying</c>: the viewing civ is the one auto-played.</summary>
    bool IsAutoPlayingFor(string civId) => false;
}

/// <summary>What <c>UniqueTriggerActivation</c> calls on the client's <c>SoundPlayer</c> and <c>musicController</c>.</summary>
public interface IClientAudio
{
    /// <summary><c>SoundPlayer.get(UncivSound(soundName)) != null</c></summary>
    bool HasSound(string soundName);
    /// <summary><c>SoundPlayer.play(UncivSound(soundName))</c>, asked on behalf of <paramref name="civ"/> (the civ the trigger fired for).</summary>
    void PlaySound(CivilizationObject civ, string soundName);
    /// <summary><c>musicController.chooseTrack(prefix, suffixes, flags)</c>; flags are the matched <c>MusicTrackChooserFlags</c> names.</summary>
    void ChooseMusic(string prefix, IReadOnlyList<string> suffixes, IReadOnlyList<string> flags);
}

/// <summary>
/// C057: the engine's <see cref="IClientAudio"/>. A sound becomes a <see cref="Commands.Events.SoundRequested"/> event for the civ it was triggered for (nothing when no
/// event sink listens), which the viewer filter delivers to that civ only. Music is not supported yet (C030: music none), so ChooseMusic does nothing.
/// </summary>
public sealed class SoundCueAudio : IClientAudio
{
    public static SoundCueAudio Instance { get; } = new();

    public bool HasSound(string soundName) => soundName.Length > 0;

    public void PlaySound(CivilizationObject civ, string soundName) => GameEventHooks.SoundRequested(civ, soundName);

    public void ChooseMusic(string prefix, IReadOnlyList<string> suffixes, IReadOnlyList<string> flags)
    {
        // Music none (C030).
    }
}
