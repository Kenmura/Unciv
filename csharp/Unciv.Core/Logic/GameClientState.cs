using Unciv.Logic.City.Managers;
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
