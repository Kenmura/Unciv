namespace Unciv.Logic.Battle;

// Kotlin Battle.DamageDealt (Battle.kt, T083). Declared here because AirInterception (T082) returns it; T083 may move it into Battle.

/// <param name="AttackerDealt">Damage done by attacker to defender</param>
/// <param name="DefenderDealt">Damage done by defender to attacker</param>
public readonly record struct DamageDealt(int AttackerDealt, int DefenderDealt)
{
    public static DamageDealt None { get; } = new(0, 0);

    public static DamageDealt operator +(DamageDealt a, DamageDealt other) =>
        new(unchecked(a.AttackerDealt + other.AttackerDealt), unchecked(a.DefenderDealt + other.DefenderDealt));
}
