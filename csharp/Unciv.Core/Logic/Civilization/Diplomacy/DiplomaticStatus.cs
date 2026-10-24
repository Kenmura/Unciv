namespace Unciv.Logic.Civilization.Diplomacy;

// core/src/com/unciv/logic/civilization/diplomacy/DiplomaticStatus.kt

public enum DiplomaticStatus
{
    Peace,
    /// <summary>City state's diplomacy for major civ can be marked as Protector, not vice versa.</summary>
    Protector,
    War,
    DefensivePact,
}
