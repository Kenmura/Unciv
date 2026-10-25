using Unciv.Logic.Map;
using Unciv.Logic.Map.MapUnits;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Ruleset.Unit;
using Unciv.Utils;
using CityObject = Unciv.Logic.City.City;
using NotificationCategory = Unciv.Logic.Civilization.Notification.NotificationCategory;

namespace Unciv.Logic.Civilization.Managers;

// Ported from core/src/com/unciv/logic/civilization/managers/UnitManager.kt (T066). Stripped: the purity annotations.
// GUI.getSettings().alternateUnitCycleOrder is IGameInfoState.AlternateUnitCycleOrder (the client setting, carried by GameClientState).

public class UnitManager(Civilization civInfo)
{
    public Civilization CivInfo { get; } = civInfo;

    /// <summary>
    /// All units of <see cref="CivInfo"/>, _ordered_.
    /// Collection order and <see cref="nextPotentiallyDueAt"/> determine activation order when using "Next unit".
    ///
    /// When loading a save, this is entirely rebuilt from Tile.*Unit.
    /// GameInfo.setTransients -> TileMap.setTransients -> Tile.setUnitTransients -> MapUnit.assignOwner -> <see cref="AddUnit(MapUnit, bool)"/>
    ///
    /// We never add or remove from here directly, could cause comodification problems.
    /// Instead, we create a copy list with the change, and replace this list.
    /// The other solution, casting toList() every "get", has a performance cost
    /// </summary>
    private IReadOnlyList<MapUnit> unitList = [];

    /// <summary>Index in <see cref="unitList"/> of the unit that is potentially due and is next up for button "Next unit".</summary>
    private int nextPotentiallyDueAt;

    /// <summary>Creates a new <see cref="MapUnit"/> and places it on the map.</summary>
    /// <param name="unitName">The <see cref="BaseUnit"/> name to create a MapUnit instance of - auto-mapped to a nation equivalent if one exists</param>
    /// <param name="city">The City to place the new unit in or near</param>
    /// <returns>The new unit or <c>null</c> if unsuccessful (invalid unitName, no tile found where it could be placed, or civ has no cities)</returns>
    public virtual MapUnit? AddUnit(string unitName, CityObject? city = null)
    {
        if (!CivInfo.GameInfo.Ruleset.Units.TryGetValue(unitName, out var unit)) return null;
        return AddUnit(unit, city);
    }

    /// <summary>Creates a new <see cref="MapUnit"/> and places it on the map.</summary>
    /// <param name="baseUnit">The <see cref="BaseUnit"/> to create a MapUnit instance of - auto-mapped to a nation equivalent if one exists</param>
    /// <param name="city">The City to place the new unit in or near</param>
    /// <returns>The new unit or <c>null</c> if unsuccessful (no tile found where it could be placed, or civ has no cities)</returns>
    public virtual MapUnit? AddUnit(BaseUnit baseUnit, CityObject? city = null)
    {
        if (CivInfo.Cities.Count == 0) return null;
        var rng = (city?.State ?? CivInfo.State).StateBasedRandom("UnitManager.addUnit");

        var unit = CivInfo.GetEquivalentUnit(baseUnit);
        var citiesNotInResistance = CivInfo.Cities.Where(it => !it.IsInResistance()).ToList();

        // To allow cities on water tiles to be able to build water units.
        var canSpawnUnitOnWater = city?.IsNaval() == true;
        CityObject? cityToAddTo;
        if (unit.IsWaterUnit && canSpawnUnitOnWater) cityToAddTo = city;
        else if (unit.IsWaterUnit)
            cityToAddTo = citiesNotInResistance.Where(it => it.IsNaval()).ToList().RandomOrNull(rng)
                ?? CivInfo.Cities.Where(it => it.IsNaval()).ToList().RandomOrNull(rng);
        else if (city != null) cityToAddTo = city;
        else cityToAddTo = citiesNotInResistance.RandomOrNull(rng) ?? CivInfo.Cities.Random(rng);
        if (cityToAddTo == null) return null; // If we got a free water unit with no coastal city to place it in

        var placedUnit = PlaceUnitNearTile(cityToAddTo.Location, unit.Name);
        // silently bail if no tile to place the unit is found
        if (placedUnit == null) return null;
        if (unit.IsGreatPerson)
        {
            CivInfo.AddNotification($"A [{unit.Name}] has been born in [{cityToAddTo.Name}]!",
                new MapUnitAction(placedUnit.CurrentTile.Position, placedUnit.Id), NotificationCategory.General, unit.Name);
            CivInfo.QuestManager.HandleIndividualQuests(); // Maybe there's a "aquire great person" quest active
        }

        if (placedUnit.HasUnique(UniqueType.ReligiousUnit) && CivInfo.GameInfo.IsReligionEnabled())
        {
            if (!placedUnit.HasUnique(UniqueType.TakeReligionOverBirthCity)
                || CivInfo.ReligionManager.Religion?.IsMajorReligion() == false)
            {
                placedUnit.Religion = cityToAddTo.Religion.GetMajorityReligionName();
            }
        }

        return placedUnit;
    }

    /// <summary>Tries to place the a <paramref name="unitName"/> unit into the Tile closest to the given the <paramref name="location"/></summary>
    /// <param name="location">where to try to place the unit</param>
    /// <param name="unitName">name of the <see cref="BaseUnit"/> to create and place</param>
    /// <returns>created <see cref="MapUnit"/> or null if no suitable location was found</returns>
    public virtual MapUnit? PlaceUnitNearTile(HexCoord location, string unitName)
    {
        var unit = CivInfo.GameInfo.Ruleset.Units[unitName];
        return PlaceUnitNearTile(location, unit);
    }

    /// <summary>Tries to place the a <paramref name="baseUnit"/> unit into the Tile closest to the given the <paramref name="location"/></summary>
    /// <param name="location">where to try to place the unit</param>
    /// <param name="baseUnit"><see cref="BaseUnit"/> to create and place</param>
    /// <param name="unitId">the id the new unit gets, a fresh one when <c>null</c></param>
    /// <param name="copiedFrom">unit to copy the statistics from</param>
    /// <returns>created <see cref="MapUnit"/> or null if no suitable location was found</returns>
    public virtual MapUnit? PlaceUnitNearTile(HexCoord location, BaseUnit baseUnit, int? unitId = null, MapUnit? copiedFrom = null)
    {
        var unit = (MapUnit?)CivInfo.GameInfo.TileMap.PlaceUnitNearTile(location, baseUnit, CivInfo, unitId);

        if (unit == null) return unit;

        // Must happen before the triggers or else things like "[This Unit] loses the [promotionName] promotion"
        //    will trigger *before* the unit actually has the promotion, and then will get the promotion anyway.
        copiedFrom?.CopyStatisticsTo(unit);

        var triggerNotificationText = $"due to gaining a [{unit.Name}]";
        foreach (var unique in unit.GetUniques().ToList())
        {
            // Promotion triggerables are ALREADY handled in placeUnitNearTile -> addPromotion, where they were added
            if (unique.HasTriggerConditional() ||
                unique.SourceObjectType == UniqueTarget.Promotion ||
                !unique.ConditionalsApply(unit.Cache.State)
            ) continue;
            var times = unique.GetUniqueMultiplier(unit.Cache.State);
            for (var i = 0; i < times; i++)
                UniqueTriggerActivation.TriggerUnique(unique, unit, triggerNotificationText: triggerNotificationText);
        }

        CivInfo.ForEachTriggeredUnique(UniqueType.TriggerUponGainingUnit, unit.Cache.State,
            it => unit.MatchesFilter(it.Params[0]), unique =>
            {
                UniqueTriggerActivation.TriggerUnique(unique, unit, triggerNotificationText: triggerNotificationText);
            });

        if (unit.GetResourceRequirementsPerTurn().Count != 0)
            CivInfo.Cache.UpdateCivResources();

        CivInfo.ForEachMatchingUnique(UniqueType.LandUnitsCrossTerrainAfterUnitGained, unit.Cache.State, unique =>
        {
            if (unit.MatchesFilter(unique.Params[1]))
            {
                CivInfo.PassThroughImpassableUnlocked = true; // Update the cached Boolean
                CivInfo.PassableImpassables.Add(unique.Params[0]); // Add to list of passable impassables
            }
        });

        if (unit.HasUnique(UniqueType.ReligiousUnit) && CivInfo.GameInfo.IsReligionEnabled())
        {
            unit.Religion = CivInfo.ReligionManager.Religion?.Name;
        }
        return unit;
    }

    public virtual int GetCivUnitsSize() => unitList.Count;
    public virtual IEnumerable<MapUnit> GetCivUnits() => unitList;
    public virtual IEnumerable<MapUnit> GetCivGreatPeople() => GetCivUnits().Where(mapUnit => mapUnit.IsGreatPerson());

    // Similar to getCivUnits(), but the returned list is rotated so that the
    // 'nextPotentiallyDueAt' unit is first here.
    private IEnumerable<MapUnit> GetCivUnitsStartingAtNextDue() =>
        unitList.Skip(nextPotentiallyDueAt).Concat(unitList.Take(nextPotentiallyDueAt));

    /// <summary>
    /// Assigns an existing <paramref name="mapUnit"/> to this manager.
    ///
    /// Used during load game via setTransients to regenerate a Civilization's list from the serialized Tile fields.
    /// </summary>
    /// <param name="mapUnit">the unit</param>
    /// <param name="updateCivInfo">When <c>true</c>, calls <c>updateStatsForNextTurn</c> and possibly <c>updateCivResources</c></param>
    public virtual void AddUnit(MapUnit mapUnit, bool updateCivInfo = true)
    {
        // Since we create a new list anyway (otherwise some concurrent modification
        // exception will happen), also rearrange existing units so that
        // 'nextPotentiallyDueAt' becomes 0.  This way new units are always last to be due
        // (can be changed as wanted, just have a predictable place).
        var newList = GetCivUnitsStartingAtNextDue().ToList();
        newList.Add(mapUnit);
        unitList = newList;
        nextPotentiallyDueAt = 0;

        if (updateCivInfo)
        {
            // Not relevant when updating Tile transients, since some info of the civ itself isn't yet available,
            // and in any case it'll be updated once civ info transients are
            CivInfo.UpdateStatsForNextTurn(); // unit upkeep
            if (mapUnit.GetResourceRequirementsPerTurn().Count != 0)
                CivInfo.Cache.UpdateCivResources();
        }
    }

    /// <summary>
    /// T231 (deliberate deviation from Kotlin): puts the unit list into creation order (ascending <see cref="MapUnit.Id"/>, ids are monotonic
    /// from GameInfo.lastUnitId) and resets the "next unit" cursor. Called by both the load path (GameInfo.SetTransients, where Kotlin would
    /// keep tile-by-tile order) and GameInfo.RefreshTransientCaches (T208), so a running game and its reload iterate and automate their
    /// units identically, and that order is the one a live Kotlin game keeps.
    /// </summary>
    public void SortById()
    {
        // T236: units that share an id (legacy saves) are ordered by where a load would meet them (tile order, then the tile's own unit order),
        // never by their current list position, so a running game and its reload agree on ties too. Only computed when a tie exists.
        var ties = unitList.GroupBy(it => it.Id).Any(it => it.Count() > 1);
        if (!ties)
            unitList = unitList.OrderBy(it => it.Id).ToList();
        else
        {
            var rank = new Dictionary<MapUnit, int>(ReferenceEqualityComparer.Instance);
            foreach (var unit in CivInfo.GameInfo.TileMap.Values.SelectMany(it => it.GetUnits()))
                rank[unit] = rank.Count;
            unitList = unitList.OrderBy(it => it.Id).ThenBy(it => rank.GetValueOrDefault(it, int.MaxValue)).ToList();
        }
        nextPotentiallyDueAt = 0;
    }

    public virtual void RemoveUnit(MapUnit mapUnit, bool updateCivInfo = true)
    {
        // See comment in addUnit().
        var newList = GetCivUnitsStartingAtNextDue().ToList();
        // Kotlin MutableList.remove(element): the first element equal (reference identity, MapUnit has no equals)
        var index = newList.FindIndex(it => ReferenceEquals(it, mapUnit));
        if (index >= 0) newList.RemoveAt(index);
        unitList = newList;
        nextPotentiallyDueAt = 0;
        if (!updateCivInfo) return;

        CivInfo.UpdateStatsForNextTurn(); // unit upkeep
        if (mapUnit.GetResourceRequirementsPerTurn().Count != 0)
            CivInfo.Cache.UpdateCivResources();

        CivInfo.ForEachTriggeredUnique(UniqueType.TriggerUponLosingUnit, mapUnit.Cache.State,
            it => mapUnit.MatchesFilter(it.Params[0]), unique =>
            {
                UniqueTriggerActivation.TriggerUnique(unique, mapUnit);
            });
    }

    public IEnumerable<MapUnit> GetIdleUnits() => GetCivUnits().Where(it => it.IsIdle());

    public IEnumerable<MapUnit> GetDueUnits() => GetCivUnitsStartingAtNextDue().Where(it => it.Due && it.IsIdle());

    public MapUnit? GetUnitById(int id) => GetCivUnits().FirstOrDefault(it => it.Id == id);

    /// <summary>Return the next due unit, but preferably not <paramref name="unitToSkip"/>: this is returned only if it is the only remaining due unit.</summary>
    public MapUnit? CycleThroughDueUnits(MapUnit? unitToSkip = null)
    {
        if (unitList.Count == 0) return null;

        if (CivInfo.GameInfo.AlternateUnitCycleOrder)
            return AlternateCycleThroughDueUnits(unitToSkip);
        var returnAt = nextPotentiallyDueAt;
        var fallbackAt = -1;

        do
        {
            if (unitList[returnAt].Due && unitList[returnAt].IsIdle())
            {
                if (!ReferenceEquals(unitList[returnAt], unitToSkip))
                {
                    nextPotentiallyDueAt = (returnAt + 1) % unitList.Count;
                    return unitList[returnAt];
                }
                fallbackAt = returnAt;
            }

            returnAt = (returnAt + 1) % unitList.Count;
        } while (returnAt != nextPotentiallyDueAt);

        if (fallbackAt >= 0)
        {
            nextPotentiallyDueAt = (fallbackAt + 1) % unitList.Count;
            return unitList[fallbackAt];
        }
        return null;
    }

    private MapUnit? AlternateCycleThroughDueUnits(MapUnit? unitToSkip = null)
    {
        var center = unitToSkip?.CurrentTile
            ?? CivInfo.GetCapital(true)?.GetCenterTile()
            ?? CivInfo.GameInfo.TileMap[0, 0];
        // Kotlin minByOrNull: the first of the minimal elements
        (int Index, MapUnit Unit)? best = null;
        var bestDistance = 0;
        for (var index = 0; index < unitList.Count; index++)
        {
            var unit = unitList[index];
            if (!unit.Due || ReferenceEquals(unit, unitToSkip) || !unit.IsIdle()) continue;
            var distance = unit.CurrentTile.AerialDistanceTo(center);
            if (best == null || distance < bestDistance)
            {
                best = (index, unit);
                bestDistance = distance;
            }
        }
        if (best == null) return null;
        nextPotentiallyDueAt = (best.Value.Index + 1) % unitList.Count;
        return best.Value.Unit;
    }
}
