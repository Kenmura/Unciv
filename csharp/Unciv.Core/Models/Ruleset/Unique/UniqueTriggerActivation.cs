using Unciv.Logic.Civilization;
using CityObject = Unciv.Logic.City.City;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Models.Ruleset.Unique;

// core/src/com/unciv/models/ruleset/unique/UniqueTriggerActivation.kt
// Not ported yet: T052 ports UniqueTriggerActivation (1.5k LOC). This file only carries the three triggerUnique overloads that
// ReligionManager and RuinsManager (T057) call, so that those call sites are final.
public static class UniqueTriggerActivation
{
    // TODO(T052): getTriggerFunction(...)?.invoke() ?: false. Until then a trigger has no effect and reports failure.
    public static bool TriggerUnique(Unique unique, Civilization civInfo, CityObject? city = null, IMapUnitState? unit = null, TileClass? tile = null,
        string? notification = null, string? triggerNotificationText = null)
    {
        _ = unique;
        _ = civInfo;
        _ = city;
        _ = unit;
        _ = tile;
        _ = notification;
        _ = triggerNotificationText;
        return false;
    }

    // TODO(T052): TriggerUnique(unique, city.civ, city, tile = city.getCenterTile(), ...)
    public static bool TriggerUnique(Unique unique, CityObject city, string? notification = null, string? triggerNotificationText = null) =>
        TriggerUnique(unique, city.Civ, city, null, city.GetCenterTile(), notification, triggerNotificationText);

    // TODO(T052): TriggerUnique(unique, unit.civ, unit = unit, tile = unit.currentTile, ...)
    public static bool TriggerUnique(Unique unique, IMapUnitState unit, string? notification = null, string? triggerNotificationText = null) =>
        TriggerUnique(unique, unit.Civ, null, unit, unit.GetTile(), notification, triggerNotificationText);
}
