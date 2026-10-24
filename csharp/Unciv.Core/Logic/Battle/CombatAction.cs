namespace Unciv.Logic.Battle;

/// <summary>
/// Ported from the bottom of <c>core/src/com/unciv/logic/battle/BattleDamage.kt</c> (ordinals are Kotlin declaration order).
/// Needed by <c>GameContext</c> (T027); the ticket that ports BattleDamage keeps this file.
/// </summary>
public enum CombatAction
{
    Attack = 0,
    Defend = 1,
    Intercept = 2,
}
