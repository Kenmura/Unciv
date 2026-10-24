
namespace Unciv.Logic.Map.MapUnits;

// T065 (MapUnit): the seams of MapUnit, which is ported BEFORE its sub-objects (seam rule, T053 notes). Each interface holds exactly the
// members MapUnit.kt / MapUnitCache.kt call on that type; the ticket that ports the type implements it, retypes the property in MapUnit to
// the concrete class and replaces the lateinit-style property by Kotlin's initializer. (UnitMovement, T067, is done.)
// Member names mirror the Kotlin members.

/// <summary>The <c>UnitActionType.value</c> strings MapUnit compares its <c>action</c> against. TODO(T079): replaced by UnitActionType.</summary>
internal static class UnitActionNames
{
    public const string Automate = "Automate";
    public const string Sleep = "Sleep";
    public const string Fortify = "Fortify";
    public const string Guard = "Guard";
    public const string Explore = "Explore";
    public const string Paradrop = "Paradrop";
    public const string AirSweep = "Air Sweep";
    public const string SetUp = "Set up";
}
