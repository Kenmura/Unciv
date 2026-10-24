using Unciv.Models;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Ruleset.Unit;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;
using TileClass = Unciv.Logic.Map.Tiles.Tile;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.Logic.Battle;

// Ported from core/src/com/unciv/logic/battle/ICombatant.kt (T081).
//
// Deviations: the purity annotations are dropped. The interface also is the IGameContextCombatant that GameContext reads (T027): the
// IGameContextCombatant members are default explicit implementations here, so a combatant only writes the Kotlin members.
// Kotlin's `this is CityCombatant` / `as MapUnitCombatant` checks are kept as type tests in the default methods.

public interface ICombatant : IGameContextCombatant
{
    // GetName() and GetHealth() are inherited from IGameContextCombatant (same signatures).
    int GetMaxHealth();
    UnitType GetUnitType();
    int GetAttackingStrength(ICombatant? defender = null);
    int GetDefendingStrength(ICombatant? attacker = null);
    void TakeDamage(int damage);
    bool IsDefeated();
    new CivilizationObject GetCivInfo();
    new TileClass GetTile();
    bool IsVisibleTo(CivilizationObject to);
    bool CanAttack();
    /// <summary>Implements <c>UniqueParameterType.CombatantFilter</c>.</summary>
    new bool MatchesFilter(string filter, bool multiFilter = true);
    UncivSound GetAttackSound();

    bool IsMelee() => !IsRanged();

    bool IsRanged()
    {
        if (this is CityCombatant) return true;
        return ((MapUnitCombatant)this).Unit.BaseUnit.IsRanged();
    }

    bool IsAirUnit()
    {
        if (this is CityCombatant) return false;
        return ((MapUnitCombatant)this).Unit.BaseUnit.IsAirUnit();
    }

    bool IsWaterUnit()
    {
        if (this is CityCombatant) return false;
        return ((MapUnitCombatant)this).Unit.BaseUnit.IsWaterUnit;
    }

    bool IsLandUnit()
    {
        if (this is CityCombatant) return false;
        return ((MapUnitCombatant)this).Unit.BaseUnit.IsLandUnit;
    }

    bool IsCity() => this is CityCombatant;

    bool IsCivilian() => this is MapUnitCombatant mapUnitCombatant && mapUnitCombatant.Unit.IsCivilian();

    IEnumerable<UniqueObject> GetTriggeredUniques(UniqueType trigger, GameContext gameContext, Func<UniqueObject, bool>? triggerFilter = null);

    string GetNotificationDisplay(string leadingText = "") => "";

    // IGameContextCombatant (what GameContext reads)
    IGameContextCivilization IGameContextCombatant.GetCivInfo() => GetCivInfo();
    IGameContextTile IGameContextCombatant.GetTile() => GetTile();
    IGameContextUnit? IGameContextCombatant.Unit => (this as MapUnitCombatant)?.Unit;
    IGameContextCity? IGameContextCombatant.City => (this as CityCombatant)?.City;
}
