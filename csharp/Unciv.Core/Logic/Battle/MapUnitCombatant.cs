using Unciv.Logic.Civilization.Transients;
using Unciv.Models;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Ruleset.Unit;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;
using MapUnitClass = Unciv.Logic.Map.MapUnits.MapUnit;
using TileClass = Unciv.Logic.Map.Tiles.Tile;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.Logic.Battle;

// Ported from core/src/com/unciv/logic/battle/MapUnitCombatant.kt (T081). Purity annotations dropped.
public sealed class MapUnitCombatant(MapUnitClass unit) : ICombatant
{
    public MapUnitClass Unit { get; } = unit;

    public int GetHealth() => Unit.Health;
    // Kotlin inherits these from the ICombatant interface; C# default interface methods are not members of the class. These public members
    // implement the interface ones (so they must not call them) with the interface's MapUnitCombatant branch.
    public bool IsMelee() => !IsRanged();
    public bool IsRanged() => Unit.BaseUnit.IsRanged();
    public bool IsCivilian() => Unit.IsCivilian();

    public int GetMaxHealth() => 100;
    public CivilizationObject GetCivInfo() => Unit.Civ;
    public TileClass GetTile() => Unit.GetTile();
    public string GetName() => Unit.Name;
    public bool IsDefeated() => Unit.Health <= 0;
    public bool CanAttack() => Unit.CanAttack();
    public bool MatchesFilter(string filter, bool multiFilter = true) => Unit.MatchesFilter(filter, multiFilter);
    public UncivSound GetAttackSound() => Unit.BaseUnit.AttackSound is { } sound ? new UncivSound(sound) : UncivSound.Click;
    public bool IsVisibleTo(CivilizationObject to) => Unit.IsVisibleTo(to);

    public string GetNotificationDisplay(string leadingText = "")
    {
        bool isUnitUnnamed = string.IsNullOrEmpty(Unit.InstanceName);
        return isUnitUnnamed
            ? leadingText + "[" + Unit.Name + "]"
            : "[" + Unit.InstanceName + "]";
    }

    public void TakeDamage(int damage) => Unit.TakeDamage(damage);

    public int GetAttackingStrength(ICombatant? defender = null)
    {
        var state = GameContext.ForCombatants(this, defender, GetTile(), CombatAction.Attack);
        int extraStrength = 0;
        Unit.ForEachMatchingUnique(UniqueType.StrengthAmount, state, it => extraStrength = unchecked(extraStrength + it.Params[0].ToInt()));
        return IsRanged()
            ? unchecked(Unit.BaseUnit.RangedStrength + extraStrength)
            : unchecked(Unit.BaseUnit.Strength + extraStrength);
    }

    public int GetDefendingStrength(ICombatant? attacker = null)
    {
        bool attackedByRanged = attacker?.IsRanged() == true;
        var state = GameContext.ForCombatants(this, attacker, GetTile(), CombatAction.Defend);
        int extraStrength = 0;
        Unit.ForEachMatchingUnique(UniqueType.StrengthAmount, state, it => extraStrength = unchecked(extraStrength + it.Params[0].ToInt()));
        if (Unit.IsEmbarked() && !IsCivilian())
            return Unit.Civ.GetEra().EmbarkDefense;
        if (IsRanged() && attackedByRanged)
            return unchecked(Unit.BaseUnit.RangedStrength + extraStrength);
        return unchecked(Unit.BaseUnit.Strength + extraStrength);
    }

    public UnitType GetUnitType() => Unit.Type;

    public override string ToString() => Unit.Name + " of " + Unit.Civ.CivId;

    public IEnumerable<UniqueObject> GetMatchingUniques(UniqueType uniqueType, GameContext gameContext, bool checkCivUniques) =>
        Unit.GetMatchingUniques(uniqueType, gameContext, checkCivUniques);

    public IEnumerable<UniqueObject> GetTriggeredUniques(UniqueType trigger, GameContext gameContext, Func<UniqueObject, bool>? triggerFilter = null) =>
        Unit.GetTriggeredUniques(trigger, gameContext, triggerFilter);

    public bool HasUnique(UniqueType uniqueType, GameContext? conditionalState = null) =>
        conditionalState == null ? Unit.HasUnique(uniqueType) : Unit.HasUnique(uniqueType, conditionalState);

    public override int GetHashCode() => Unit.GetHashCode();
    public override bool Equals(object? obj) => obj is MapUnitCombatant other && other.Unit == Unit;
}
