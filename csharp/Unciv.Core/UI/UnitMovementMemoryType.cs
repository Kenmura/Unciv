namespace Unciv.UI.Components;

// core/src/com/unciv/ui/components/MapArrowType.kt
// Only UnitMovementMemoryType (part of GameInfo serialization) is engine data. The MapArrowType marker interface,
// MiscArrowTypes and TintedMapArrow are map-overlay drawing concerns and stay with the client.

/// <summary>Enum constants describing how/why a unit changed position.</summary>
public enum UnitMovementMemoryType
{
    UnitMoved,
    UnitAttacked, // For when attacked, killed, and moved into tile.
    UnitWithdrew, // Caravel, destroyer, etc.
    UnitTeleported, // Paradrop, open borders end, air rebase, etc.
}
