using Unciv.Logic.Civilization;
using Unciv.Models.Ruleset.Unique;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.Logic.Map.MapUnit.Actions;

// core/src/com/unciv/logic/map/mapunit/actions/UnitActionModifiers.kt
// Not ported yet: T079 ports UnitActionModifiers. This file only carries the one function ReligionManager (T057) calls, so that the
// call site is final; the unit is typed by the IMapUnitState seam that MapUnit (T065) implements; T079 retypes the parameter.
public static class UnitActionModifiers
{
    // TODO(T079): `unit.getMatchingUniques(actionUniqueType).filter { !it.hasModifier(UnitActionExtraLimitedTimes) }.filter { canUse(unit, it) }`.
    // Until then no unique is usable, so ReligionManager.MaySpreadReligionAtAll is false.
    public static IEnumerable<UniqueObject> GetUsableUnitActionUniques(IMapUnitState unit, UniqueType actionUniqueType)
    {
        _ = unit;
        _ = actionUniqueType;
        return [];
    }
}
