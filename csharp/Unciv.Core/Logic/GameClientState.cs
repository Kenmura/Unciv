using Unciv.Logic.City.Managers;
using Unciv.Logic.Civilization.Managers;
using Unciv.Utils;

namespace Unciv.Logic;

// T046: the client-side state that Kotlin GameInfo and its callers read from UncivGame.Current (settings, world screen). The engine has no
// UncivGame singleton, so the values the game rules read are an instance owned by the GameInfo (not serialized, not part of the game state):
// the client (Godot) sets them; a headless game keeps the defaults of Kotlin's GameSettings.

/// <summary>Client settings and UI state that the game rules read. Not saved, not synchronised between peers.</summary>
public sealed class GameClientState
{
    /// <summary><c>settings.autoAssignCityProduction</c></summary>
    public bool AutoAssignCityProduction { get; set; }

    /// <summary><c>settings.autoAssignSpecialistsInNewCities</c></summary>
    public bool AutoAssignSpecialistsInNewCities { get; set; } = true;

    /// <summary><c>settings.alternateUnitCycleOrder</c></summary>
    public bool AlternateUnitCycleOrder { get; set; }

    /// <summary><c>worldScreen?.autoPlay?.isAutoPlayingAndFullAutoPlayAI() == true</c></summary>
    public bool IsFullAutoPlayAI { get; set; }

    /// <summary><c>settings.automatedUnitsMoveOnTurnStart</c></summary>
    public bool AutomatedUnitsMoveOnTurnStart { get; set; }

    /// <summary><c>settings.citiesAutoBombardAtEndOfTurn</c></summary>
    public bool CitiesAutoBombardAtEndOfTurn { get; set; }

    /// <summary><c>settings.notificationsLogMaxTurns</c></summary>
    public int NotificationsLogMaxTurns { get; set; } = 5;

    /// <summary>The unported <c>NextTurnAutomation</c> (TODO(T094): set by its port). Null means "not ported": the calls are skipped.</summary>
    public INextTurnAutomation? NextTurnAutomation { get; set; }

    /// <summary>The unported <c>TradeEvaluation</c> (TODO(T089): set by its port). TurnManager throws when it must validate a trade and this is null.</summary>
    public ITradeValidity? TradeEvaluation { get; set; }

    /// <summary><c>worldScreen?.autoPlay</c>: null when there is no client (headless simulation).</summary>
    public IAutoPlay? AutoPlay { get; set; }

    /// <summary><c>settings.multiplayer.getUserId()</c>; the GameSettings default is the empty string.</summary>
    public string UserId { get; set; } = "";

    /// <summary><c>settings.tutorialTasksCompleted</c></summary>
    public LinkedHashSet<string> TutorialTasksCompleted { get; } = [];

    /// <summary><c>settings.getCollatorFromLocale()</c>: the culture-sensitive name ordering. Defaults to the invariant culture.</summary>
    public IComparer<string> Collator { get; set; } = StringComparer.InvariantCulture;

    /// <summary>Test-harness hook for the unported Automation (T087), see <c>CityPopulationManager.Automation</c>. Null (the default) means "not ported".</summary>
    internal ICityWorkAutomation? CityWorkAutomation { get; set; }

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
}
