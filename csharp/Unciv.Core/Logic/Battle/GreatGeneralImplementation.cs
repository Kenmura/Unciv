using Unciv.Models.Ruleset.Unique;
using MapUnitClass = Unciv.Logic.Map.MapUnits.MapUnit;

namespace Unciv.Logic.Battle;

// Ported from core/src/com/unciv/logic/battle/GreatGeneralImplementation.kt, only getGreatGeneralBonus (T081: BattleDamage.getGeneralModifiers
// calls it, and it needs nothing unported). Deferred to T082 (which adds it to this class): getBestAffectedTroopsTile
// (HeadTowardsEnemyCityAutomation / GameView) and the TODO(T082) below.
public static class GreatGeneralImplementation
{
    private sealed record GeneralBonusData(MapUnitClass General, int Radius, string Filter, int Bonus)
    {
        public GeneralBonusData(MapUnitClass general, Unique unique)
            : this(general, UniqueParameterTypeExtensions.ToIntOrNull(unique.Params[2]) ?? 0, unique.Params[1], UniqueParameterTypeExtensions.ToIntOrNull(unique.Params[0]) ?? 0)
        {
        }
    }

    /// <summary>
    /// Determine the "Great General" bonus for <paramref name="ourUnitCombatant"/> by searching for units carrying the
    /// <see cref="UniqueType.StrengthBonusInRadius"/> in the vicinity.
    /// </summary>
    /// <returns>A pair of unit's name and bonus (percentage) as Int (typically 15), or 0 if no applicable Great General equivalents found</returns>
    public static (string Name, int Bonus) GetGreatGeneralBonus(MapUnitCombatant ourUnitCombatant, ICombatant enemy, CombatAction combatAction)
    {
        var unit = ourUnitCombatant.Unit;
        var civInfo = ourUnitCombatant.Unit.Civ;
        var allGenerals = civInfo.Units.GetCivUnits()
            .Where(it => it.Cache.HasStrengthBonusInRadiusUnique)
            .ToList();
        if (allGenerals.Count == 0) return ("", 0);

        var greatGeneral = allGenerals
            .SelectMany(general =>
                general.GetMatchingUniques(UniqueType.StrengthBonusInRadius,
                        new GameContext(unit.Civ, ourCombatant: ourUnitCombatant, theirCombatant: enemy, combatAction: combatAction))
                    .Select(it => new GeneralBonusData(general, it)))
            .Where(it =>
                // Support the border case when a mod unit has several
                // GreatGeneralAura uniques (e.g. +50% as radius 1, +25% at radius 2, +5% at radius 3)
                // The "Military" test is also supported deep down in unit.matchesFilter, a small
                // optimization for the most common case, as this function is only called for `MapUnitCombatant`s
                it.General.CurrentTile.AerialDistanceTo(unit.GetTile()) <= it.Radius
                && (it.Filter == "Military" || unit.MatchesFilter(it.Filter, true, it.General.Cache.State)));

        // maxByOrNull: the first of equal maxima wins
        GeneralBonusData? greatGeneralModifier = null;
        foreach (var data in greatGeneral)
            if (greatGeneralModifier == null || greatGeneralModifier.Bonus < data.Bonus)
                greatGeneralModifier = data;
        if (greatGeneralModifier == null) return ("", 0);

        if (unit.HasUnique(UniqueType.GreatGeneralProvidesDoubleCombatBonus, null, true)
            && greatGeneralModifier.General.IsGreatPersonOfType("War")) // apply only on "true" generals
            return (greatGeneralModifier.General.Name, unchecked(greatGeneralModifier.Bonus * 2));
        return (greatGeneralModifier.General.Name, greatGeneralModifier.Bonus);
    }
}
