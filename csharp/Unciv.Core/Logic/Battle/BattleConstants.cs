namespace Unciv.Logic.Battle;

// Ported from core/src/com/unciv/logic/battle/BattleConstants.kt.
public static class BattleConstants
{
    //https://www.carlsguides.com/strategy/civilization5/war/combatbonuses.php
    public const int LandingMalus = -50;
    public const int BoardingMalus = -50;
    public const int AttackingAcrossRiverMalus = -20;
    public const float BaseFlankingBonus = 10f;
    public const int MissingResourcesMalus = -25;
    public const int EmbarkedDefenceBonus = 100;
    public const int FortificationBonus = 20;
    public const float DamageReductionWoundedUnitRatioPercentage = 300f;
    public const int DamageToCivilianUnit = 40;
    /// <summary>B023: extra damage (percent of the damage just dealt) when a Heavy Charge cannot push the defender back.</summary>
    public const int HeavyChargeBlockedDamagePercent = 50;
}
