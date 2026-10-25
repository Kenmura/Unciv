using Unciv.Models.Ruleset.Unit;

namespace Unciv.Models.Ruleset.Unique;
// T039 (TileMap): TileMap talks to GameInfo, Civilization and MapUnit only through the members below, which extend the GameContext
// interfaces (GameContext.cs); GameInfo, Civilization and MapUnit implement them. Member names mirror the Kotlin members.

public partial interface IGameContextGameInfo
{
    /// <summary><c>gameInfo.ruleset.units[unitName]!!</c></summary>
    BaseUnit GetBaseUnit(string unitName);
}

public partial interface IGameContextCivilization
{
    /// <summary><c>baseUnit.newMapUnit(civInfo, unitId)</c></summary>
    IGameContextUnit NewMapUnit(BaseUnit baseUnit, int? unitId);
    /// <summary><c>civ.units.removeUnit(unit, updateCivInfo)</c></summary>
    void RemoveUnit(IGameContextUnit unit, bool updateCivInfo);
    /// <summary><c>civ.forEachMatchingUnique(uniqueType, action)</c></summary>
    void ForEachMatchingUnique(UniqueType uniqueType, Action<Unique> action);
    void UpdateStatsForNextTurn();
    /// <summary><c>civ.cache.updateCivResources()</c></summary>
    void UpdateCivResources();
}

public partial interface IGameContextUnit
{
    /// <summary><c>unit.baseUnit</c></summary>
    BaseUnit BaseUnit { get; }
    /// <summary><c>unit.cache.canMoveOnWater</c></summary>
    bool CanMoveOnWater { get; }
    /// <summary><c>unit.movement.canPassThrough(tile)</c></summary>
    bool CanPassThrough(Unciv.Logic.Map.Tiles.Tile tile);
    /// <summary><c>unit.movement.canMoveTo(tile)</c></summary>
    bool CanMoveTo(Unciv.Logic.Map.Tiles.Tile tile);
    /// <summary>The <c>originalOwner</c> setter.</summary>
    void SetOriginalOwner(string civId);
    /// <summary>The <c>cache.state</c> setter.</summary>
    void SetCacheState(GameContext state);
    void PutInTile(Unciv.Logic.Map.Tiles.Tile tile);
    void RemoveFromTile();
    int GetMaxMovement();
    /// <summary>The <c>currentMovement</c> setter.</summary>
    void SetCurrentMovement(float movement);
    void AddMovementMemory();
    /// <summary><c>unit.promotions.addPromotion(promotionName, isFree)</c></summary>
    void AddPromotion(string promotionName, bool isFree);
    /// <summary><c>unit.getResourceRequirementsPerTurn().isNotEmpty()</c></summary>
    bool HasResourceRequirementsPerTurn();
}
