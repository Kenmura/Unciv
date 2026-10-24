namespace Unciv.Models;

// core/src/com/unciv/models/TutorialTrigger.kt

/// <summary>Each instance represents some event that can display a Tutorial. See <see cref="TutorialTriggerExtensions"/> for the value and the Civilopedia flag.</summary>
// Upstream suggests implementing these as unique conditionals instead
public enum TutorialTrigger
{
    Introduction,
    NewGame,
    SlowStart,
    CultureAndPolicies,
    Happiness,
    Unhappiness,
    GoldenAge,
    RoadsAndRailroads,
    VictoryTypes,
    EnemyCity,
    LuxuryResource,
    StrategicResource,
    EnemyCityNeedsConqueringWithMeleeUnit,
    AfterConquering,
    BarbarianEncountered,
    OtherCivEncountered,
    InjuredUnits,
    Workers,
    Embarking,
    IdleUnits,
    ContactMe,
    Pillaging,
    Experience,
    Combat,
    ResearchAgreements,
    CityStates,
    NaturalWonders,
    CityExpansion,
    GreatPeople,
    RemovingTerrainFeatures,
    Keyboard,
    WorldScreen,
    Faith,
    Religion,
    Religion_inside_cities,
    SpreadingReligion,
    Inquisitors,
    MayanCalendar,
    WeLoveTheKingDay,
    CityTileBlockade,
    CityBlockade,
}

public static class TutorialTriggerExtensions
{
    /// <summary>The tutorial name, as used in the Tutorials.json keys.</summary>
    public static string Value(this TutorialTrigger trigger) => trigger switch
    {
        TutorialTrigger.Introduction => "Introduction",
        TutorialTrigger.NewGame => "New_Game",
        TutorialTrigger.SlowStart => "_Slow_Start",
        TutorialTrigger.CultureAndPolicies => "Culture_and_Policies",
        TutorialTrigger.Happiness => "Happiness",
        TutorialTrigger.Unhappiness => "Unhappiness",
        TutorialTrigger.GoldenAge => "Golden_Age",
        TutorialTrigger.RoadsAndRailroads => "Roads_and_Railroads",
        TutorialTrigger.VictoryTypes => "Victory_Types",
        TutorialTrigger.EnemyCity => "Enemy_City",
        TutorialTrigger.LuxuryResource => "Luxury_Resource",
        TutorialTrigger.StrategicResource => "Strategic_Resource",
        TutorialTrigger.EnemyCityNeedsConqueringWithMeleeUnit => "_EnemyCityNeedsConqueringWithMeleeUnit",
        TutorialTrigger.AfterConquering => "After_Conquering",
        TutorialTrigger.BarbarianEncountered => "_BarbarianEncountered",
        TutorialTrigger.OtherCivEncountered => "_OtherCivEncountered",
        TutorialTrigger.InjuredUnits => "Injured_Units",
        TutorialTrigger.Workers => "Workers",
        TutorialTrigger.Embarking => "Embarking",
        TutorialTrigger.IdleUnits => "Idle_Units",
        TutorialTrigger.ContactMe => "Contact_Me",
        TutorialTrigger.Pillaging => "Pillaging",
        TutorialTrigger.Experience => "Experience",
        TutorialTrigger.Combat => "Combat",
        TutorialTrigger.ResearchAgreements => "Research_Agreements",
        TutorialTrigger.CityStates => "City-States",
        TutorialTrigger.NaturalWonders => "Natural_Wonders",
        TutorialTrigger.CityExpansion => "City_Expansion",
        TutorialTrigger.GreatPeople => "Great_People",
        TutorialTrigger.RemovingTerrainFeatures => "Removing_Terrain_Features",
        TutorialTrigger.Keyboard => "Keyboard",
        TutorialTrigger.WorldScreen => "World_Screen",
        TutorialTrigger.Faith => "Faith",
        TutorialTrigger.Religion => "Religion",
        TutorialTrigger.Religion_inside_cities => "Religion_inside_cities",
        TutorialTrigger.SpreadingReligion => "Spreading_Religion",
        TutorialTrigger.Inquisitors => "Inquisitors",
        TutorialTrigger.MayanCalendar => "Maya_Long_Count_calendar_cycle",
        TutorialTrigger.WeLoveTheKingDay => "We_Love_The_King_Day",
        TutorialTrigger.CityTileBlockade => "City_Tile_Blockade",
        TutorialTrigger.CityBlockade => "City_Blockade",
        _ => throw new ArgumentOutOfRangeException(nameof(trigger), trigger, null),
    };

    /// <summary>Tutorials whose name does not start with an underscore are shown in the Civilopedia.</summary>
    public static bool IsCivilopedia(this TutorialTrigger trigger) => !trigger.Value().StartsWith('_');
}
