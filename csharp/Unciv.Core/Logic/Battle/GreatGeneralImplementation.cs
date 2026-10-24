using Unciv.Logic.Automation.Unit;
using Unciv.Models.Ruleset.Unique;
using Unciv.View;
using CityObject = Unciv.Logic.City.City;
using MapUnitClass = Unciv.Logic.Map.MapUnits.MapUnit;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Logic.Battle;

// Ported from core/src/com/unciv/logic/battle/GreatGeneralImplementation.kt: getGreatGeneralBonus (T081: BattleDamage.getGeneralModifiers
// calls it) and getBestAffectedTroopsTile (T082). getEnemyCitiesByPriority(GameView(gameInfo, civ).getMapUnitView(general)) is HeadTowardsEnemyCityAutomation (T090).
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

    /// <summary>
    /// Find a tile for accompanying a military unit where the total bonus for all affected units is maximized.
    /// Used by SpecificUnitAutomation.automateGreatGeneral.
    /// </summary>
    public static TileClass? GetBestAffectedTroopsTile(MapUnitClass general) =>
        GetBestAffectedTroopsTile(general, HeadTowardsEnemyCityAutomation.GetEnemyCitiesByPriority(
            new GameView((GameInfo)general.Civ.GameInfo, general.Civ).GetMapUnitView(general)));

    /// <param name="general"></param>
    /// <param name="enemyCitiesByPriority">Kotlin <c>getEnemyCitiesByPriority(GameView(..).getMapUnitView(general))</c>, injectable so the scoring is testable</param>
    internal static TileClass? GetBestAffectedTroopsTile(MapUnitClass general, IEnumerable<CityObject> enemyCitiesByPriority)
    {
        // Normally we have only one Unique here. But a mix is not forbidden,
        // (imagine several GreatGeneralAura uniques - +50% at radius 1, +25% at radius 2, +5% at radius 3 - possibly learnable from promotions via buildings or natural wonders?)
        // However, rang-1 generals are difficult to use fully without getting it killed, so it's probably best placed to support 2nd and 3rd row ranged units
        // a 3 or more range generals are less sensitive to positioning, and thus probably suffice with 2-range general logic

        var militaryUnitTilesInDistance = general.Movement.GetDistanceToTiles().Keys
            .Where(tile =>
            {
                var militaryUnit = tile.MilitaryUnit;
                return militaryUnit != null && militaryUnit.Civ == general.Civ
                    && (tile.CivilianUnit == null || tile.CivilianUnit == general);
            });

        CityObject? closestReachableEnemyCity = enemyCitiesByPriority
            .FirstOrDefault(it => general.Movement.CanReach(it.GetCenterTile()));
        // Send generals to the same place as our units, so they don't get stuck at the wrong side of our empire. Update this when changing global unit movement

        // maxByOrNull: the first of equal maxima wins
        TileClass? militaryUnitTile = null;
        int bestScore = 0;
        foreach (var unitTile in militaryUnitTilesInDistance)
        {
            int score = unchecked(
                2 * unitTile.GetTilesInDistance(2).Count(it => it.MilitaryUnit?.Civ == general.Civ)
                - unitTile.GetTilesInDistance(2).Count(it => it.MilitaryUnit?.Civ != general.Civ)
                - (closestReachableEnemyCity != null ? 3 * unitTile.AerialDistanceTo(closestReachableEnemyCity.GetCenterTile()) : 0));
            // Scoring here is found to help AI defeat former AI,
            // a more robust scoring may be necessary to avoid leaving generals en-prise,
            // and improve handling of multiple generals, e.g. use one to support bomber stacks and the other for frontline troops.
            if (militaryUnitTile == null || bestScore < score)
            {
                militaryUnitTile = unitTile;
                bestScore = score;
            }
        }
        return militaryUnitTile;
    }
}
