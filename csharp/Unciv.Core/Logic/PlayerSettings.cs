using System.Text.Json.Serialization;
using Unciv.Utils;

namespace Unciv.Logic;

// T183: the Kotlin GameSettings (UncivGame.Current.settings) the game rules read, as per-civilization game state. Kotlin reads them from whoever
// runs the process, which makes the result of a turn depend on the machine; here every civ has its own values, stored in the GameInfo (saved,
// hashed, changed only through a command), so a snapshot plus a command log replays identically and several human civs can be active at once.
// Replaces the UserId / IsUsersTurn gating: a civ's own settings apply whenever that civ acts. Kotlin Defaults are those of GameSettings.
// UI-only settings (AlternateUnitCycleOrder, Collator, tutorials) stay in GameClientState.

/// <summary>The state-affecting settings of one civilization. Immutable: change a civ's settings with <see cref="GameInfo.SetPlayerSettings"/>.</summary>
public sealed record PlayerSettings : IStateHashable
{
    /// <summary>The settings of a civ that has none stored: Kotlin's <c>GameSettings</c> defaults.</summary>
    public static PlayerSettings Default { get; } = new();

    /// <summary><c>settings.autoAssignCityProduction</c></summary>
    [JsonPropertyName("autoAssignCityProduction")]
    public bool AutoAssignCityProduction { get; init; }

    /// <summary><c>settings.autoAssignSpecialistsInNewCities</c></summary>
    [JsonPropertyName("autoAssignSpecialistsInNewCities")]
    public bool AutoAssignSpecialistsInNewCities { get; init; } = true;

    /// <summary><c>settings.automatedUnitsMoveOnTurnStart</c></summary>
    [JsonPropertyName("automatedUnitsMoveOnTurnStart")]
    public bool AutomatedUnitsMoveOnTurnStart { get; init; }

    /// <summary><c>settings.citiesAutoBombardAtEndOfTurn</c></summary>
    [JsonPropertyName("citiesAutoBombardAtEndOfTurn")]
    public bool CitiesAutoBombardAtEndOfTurn { get; init; }

    /// <summary><c>settings.notificationsLogMaxTurns</c></summary>
    [JsonPropertyName("notificationsLogMaxTurns")]
    public int NotificationsLogMaxTurns { get; init; } = 5;

    /// <summary><c>settings.automatedUnitsChoosePromotions</c></summary>
    [JsonPropertyName("automatedUnitsChoosePromotions")]
    public bool AutomatedUnitsChoosePromotions { get; init; }

    /// <summary><c>settings.autoBuildingRoads</c></summary>
    [JsonPropertyName("autoBuildingRoads")]
    public bool AutoBuildingRoads { get; init; } = true;

    /// <summary><c>settings.automatedWorkersReplaceImprovements</c></summary>
    [JsonPropertyName("automatedWorkersReplaceImprovements")]
    public bool AutomatedWorkersReplaceImprovements { get; init; } = true;

    /// <summary><c>settings.stopAutomatedWorkersRemoveVegetation</c></summary>
    [JsonPropertyName("stopAutomatedWorkersRemoveVegetation")]
    public bool StopAutomatedWorkersRemoveVegetation { get; init; }

    /// <summary>
    /// Kotlin's "a world screen with autoPlay exists" (<c>worldScreen?.autoPlay != null</c>): the human is asked (conquest alert, worker and road
    /// automation obey the human's settings) instead of the engine deciding for them. Explicit and per civ, so that a headless server can give a
    /// human civ the alert. Default false, which is Kotlin's behaviour without a world screen (the conquest is automated).
    /// </summary>
    [JsonPropertyName("humanConquestPrompt")]
    public bool HumanConquestPrompt { get; init; }

    public void HashState(StateHasher hasher)
    {
        hasher.Add("PlayerSettings");
        hasher.Add(AutoAssignCityProduction);
        hasher.Add(AutoAssignSpecialistsInNewCities);
        hasher.Add(AutomatedUnitsMoveOnTurnStart);
        hasher.Add(CitiesAutoBombardAtEndOfTurn);
        hasher.Add(NotificationsLogMaxTurns);
        hasher.Add(AutomatedUnitsChoosePromotions);
        hasher.Add(AutoBuildingRoads);
        hasher.Add(AutomatedWorkersReplaceImprovements);
        hasher.Add(StopAutomatedWorkersRemoveVegetation);
        hasher.Add(HumanConquestPrompt);
    }
}
