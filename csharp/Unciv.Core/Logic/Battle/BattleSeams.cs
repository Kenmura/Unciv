using Unciv.Utils;
using CityObject = Unciv.Logic.City.City;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;
using MapUnitClass = Unciv.Logic.Map.MapUnits.MapUnit;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Logic.Battle;

// T081 (Battle A) / T082 (Battle B): the members of Kotlin Battle and Nuke (T083) that the ported battle files call.
internal static class BattleSeams
{
    /// <summary>
    /// Kotlin <c>Battle.getExtraRangedAttackFakeUnits(attacker)</c>: one <c>FakeUnitForExtraRangedAttack</c> (an ICombatant delegating to
    /// <paramref name="attacker"/>, always ranged, with its own base ranged strength) per <c>ExtraRangedAttack</c> unique.
    /// </summary>
    // TODO(T083): Battle.getExtraRangedAttackFakeUnits
    internal static IReadOnlyList<ICombatant> GetExtraRangedAttackFakeUnits(MapUnitCombatant attacker) =>
        throw new NotImplementedException($"Battle.getExtraRangedAttackFakeUnits({attacker}) needs T083");

    /// <summary>
    /// Kotlin <c>Battle.getMapCombatantOfTile(tile)</c> (T082: TargetHelper needs it and it needs nothing unported; T083 moves it into Battle).
    /// </summary>
    internal static ICombatant? GetMapCombatantOfTile(TileClass tile)
    {
        if (tile.IsCityCenter()) return new CityCombatant((CityObject)tile.GetCity().NotNull());
        if (tile.MilitaryUnit != null) return new MapUnitCombatant(tile.MilitaryUnit);
        if (tile.CivilianUnit != null) return new MapUnitCombatant(tile.CivilianUnit);
        return null;
    }

    /// <summary>Kotlin <c>Nuke.mayUseNuke(nuke, targetTile)</c>.</summary>
    // TODO(T083): Nuke.mayUseNuke
    internal static bool MayUseNuke(MapUnitCombatant nuke, TileClass targetTile) =>
        throw new NotImplementedException($"Nuke.mayUseNuke({nuke}, {targetTile}) needs T083");

    /// <summary>Kotlin <c>Battle.takeDamage(attacker, defender)</c>.</summary>
    // TODO(T083): Battle.takeDamage
    internal static DamageDealt TakeDamage(ICombatant attacker, ICombatant defender) =>
        throw new NotImplementedException($"Battle.takeDamage({attacker}, {defender}) needs T083");

    /// <summary>Kotlin <c>Battle.addXp(thisCombatant, amount, otherCombatant)</c>.</summary>
    // TODO(T083): Battle.addXp
    internal static void AddXp(ICombatant thisCombatant, int amount, ICombatant otherCombatant) =>
        throw new NotImplementedException($"Battle.addXp({thisCombatant}, {amount}, {otherCombatant}) needs T083");

    /// <summary>Kotlin <c>Battle.triggerDefeatUniques(ourUnit, enemy, attackedTile)</c>.</summary>
    // TODO(T083): Battle.triggerDefeatUniques
    internal static void TriggerDefeatUniques(MapUnitCombatant ourUnit, ICombatant enemy, TileClass attackedTile) =>
        throw new NotImplementedException($"Battle.triggerDefeatUniques({ourUnit}, {enemy}, {attackedTile}) needs T083");

    /// <summary>Kotlin <c>Battle.destroyIfDefeated(attackedCiv, attacker)</c>.</summary>
    // TODO(T083): Battle.destroyIfDefeated (only the not-defeated no-op is implemented; the defeat handling needs T083)
    internal static void DestroyIfDefeated(CivilizationObject attackedCiv, CivilizationObject attacker)
    {
        if (!attackedCiv.IsDefeated()) return;
        // TODO(T083): the defeat handling
        throw new NotImplementedException($"Battle.destroyIfDefeated({attackedCiv}, {attacker}) needs T083");
    }

    /// <summary>Kotlin <c>HeadTowardsEnemyCityAutomation.getEnemyCitiesByPriority(GameView(...).getMapUnitView(unit))</c> (T090).</summary>
    // TODO(T090): HeadTowardsEnemyCityAutomation.getEnemyCitiesByPriority
    internal static IEnumerable<CityObject> GetEnemyCitiesByPriority(MapUnitClass unit) =>
        throw new NotImplementedException($"HeadTowardsEnemyCityAutomation.getEnemyCitiesByPriority({unit}) needs T090");
}
