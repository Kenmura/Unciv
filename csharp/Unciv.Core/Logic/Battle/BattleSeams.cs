using CityObject = Unciv.Logic.City.City;
using MapUnitClass = Unciv.Logic.Map.MapUnits.MapUnit;

namespace Unciv.Logic.Battle;

// T090 (was T081/T082/T083): the one member of a later ticket's class that the ported battle files still call.
internal static class BattleSeams
{
    /// <summary>Kotlin <c>HeadTowardsEnemyCityAutomation.getEnemyCitiesByPriority(GameView(...).getMapUnitView(unit))</c> (T090).</summary>
    // TODO(T090): HeadTowardsEnemyCityAutomation.getEnemyCitiesByPriority
    internal static IEnumerable<CityObject> GetEnemyCitiesByPriority(MapUnitClass unit) =>
        throw new NotImplementedException($"HeadTowardsEnemyCityAutomation.getEnemyCitiesByPriority({unit}) needs T090");
}
