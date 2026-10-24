namespace Unciv.Logic.Civilization.Managers;

// Ported from the enum at the end of core/src/com/unciv/logic/civilization/managers/ReligionManager.kt.
// Declaration order matters: Conditionals compares states with < and >=.
// TODO(T052): the rest of ReligionManager is ported with the Civilization tickets; this enum moves with it.
public enum ReligionState
{
    None,
    Pantheon,
    FoundingReligion, // Great prophet used, but religion has not yet been founded
    Religion,
    EnhancingReligion, // Great prophet used, but religion has not yet been enhanced
    EnhancedReligion,
}
