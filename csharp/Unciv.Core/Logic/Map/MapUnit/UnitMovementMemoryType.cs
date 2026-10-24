namespace Unciv.Logic.Map.MapUnits;

// core/src/com/unciv/ui/components/MapArrowType.kt: only the serialized enum a MapUnit stores; the arrow types themselves are UI.
public enum UnitMovementMemoryType
{
    UnitMoved,
    /// <summary>For when attacked, killed, and moved into tile.</summary>
    UnitAttacked,
    /// <summary>Caravel, destroyer, etc.</summary>
    UnitWithdrew,
    /// <summary>Paradrop, open borders end, air rebase, etc.</summary>
    UnitTeleported,
}
