namespace Unciv.Logic.Battle;

// T081 (Battle A): the one member of Kotlin Battle (T083) that BattleDamage calls.
internal static class BattleSeams
{
    /// <summary>
    /// Kotlin <c>Battle.getExtraRangedAttackFakeUnits(attacker)</c>: one <c>FakeUnitForExtraRangedAttack</c> (an ICombatant delegating to
    /// <paramref name="attacker"/>, always ranged, with its own base ranged strength) per <c>ExtraRangedAttack</c> unique.
    /// </summary>
    // TODO(T083): Battle.getExtraRangedAttackFakeUnits
    internal static IReadOnlyList<ICombatant> GetExtraRangedAttackFakeUnits(MapUnitCombatant attacker) =>
        throw new NotImplementedException($"Battle.getExtraRangedAttackFakeUnits({attacker}) needs T083");
}
