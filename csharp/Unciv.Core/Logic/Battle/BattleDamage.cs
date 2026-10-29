using Unciv.Logic.Civilization.Managers;
using Unciv.Logic.Civilization.Transients;
using Unciv.Models;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Translations;
using Unciv.UI.Components.Extensions;
using Unciv.Utils;
using MapUnitClass = Unciv.Logic.Map.MapUnits.MapUnit;
using TileClass = Unciv.Logic.Map.Tiles.Tile;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.Logic.Battle;

// Ported from core/src/com/unciv/logic/battle/BattleDamage.kt (T081). CombatAction stays in CombatAction.cs (T027).
//
// Deviations: purity annotations dropped. The Kotlin default arguments of calculateDamageToAttacker / calculateDamageToDefender
// (`tileToAttackFrom = defender.getTile()`, `randomnessFactor = getRandomness(...)`) are nullable parameters evaluated at call time.
// Battle.getExtraRangedAttackFakeUnits is Battle.GetExtraRangedAttackFakeUnits (T083).
public static class BattleDamage
{
    private static string GetModifierStringFromUnique(UniqueObject unique)
    {
        var source = unique.SourceObjectType switch
        {
            UniqueTarget.Unit => "Unit ability",
            UniqueTarget.Nation => "National ability",
            UniqueTarget.Global => GlobalUniques.GetUniqueSourceDescription(unique),
            _ => $"[{unique.SourceObjectName}] ([{unique.GetSourceNameForUser()}])",
        };
        source = source.Tr();
        if (unique.Modifiers.Count == 0) return source;

        var conditionalsText = string.Join(", ", unique.Modifiers.Select(it => it.Text.Tr()));
        return $"{source} - {conditionalsText}";
    }

    private static Counter<string> GetGeneralModifiers(ICombatant combatant, ICombatant enemy, CombatAction combatAction, TileClass tileToAttackFrom)
    {
        var modifiers = new Counter<string>();

        var conditionalState = GetGameContext(combatAction, combatant, enemy);
        var civInfo = combatant.GetCivInfo();

        if (combatant is MapUnitCombatant mapUnitCombatant)
        {
            var unitUniqueModifiers = GetUnitUniqueModifiers(mapUnitCombatant, enemy, conditionalState, tileToAttackFrom);
            modifiers.Add(unitUniqueModifiers);

            int unhappiness = BnwHappinessRules.PenaltyPercent(civInfo); // BNW (B019): graded penalty, 0 in G&K
            if (unhappiness != 0) modifiers["Unhappiness"] = unhappiness;

            var civResources = civInfo.GetCivResourcesByName();
            foreach (var resource in mapUnitCombatant.Unit.GetResourceRequirementsPerTurn().Keys)
                if (civResources[resource] < 0 && !civInfo.IsBarbarian)
                    modifiers["Missing resource"] = BattleConstants.MissingResourcesMalus;

            var (greatGeneralName, greatGeneralBonus) = GreatGeneralImplementation.GetGreatGeneralBonus(mapUnitCombatant, enemy, combatAction);
            if (greatGeneralBonus != 0)
                modifiers[greatGeneralName] = greatGeneralBonus;
        }
        else if (combatant is CityCombatant cityCombatant)
        {
            cityCombatant.City.ForEachMatchingUnique(UniqueType.StrengthForCities, conditionalState, unique =>
            {
                modifiers.Add(GetModifierStringFromUnique(unique), unique.Params[0].ToInt());
            });
        }

        if (enemy.GetCivInfo().IsBarbarian)
        {
            modifiers["Difficulty"] = KotlinMath.FloatToInt(civInfo.GameInfo.GetDifficulty().BarbarianBonus * 100);
        }

        return modifiers;
    }

    private static GameContext GetGameContext(CombatAction combatAction, ICombatant combatant, ICombatant enemy)
    {
        var attackedTile = combatAction == CombatAction.Attack ? enemy.GetTile() : combatant.GetTile();

        return new GameContext(
            combatant.GetCivInfo(),
            city: (combatant as CityCombatant)?.City,
            ourCombatant: combatant,
            theirCombatant: enemy,
            attackedTile: attackedTile,
            combatAction: combatAction);
    }

    private static Counter<string> GetUnitUniqueModifiers(MapUnitCombatant combatant, ICombatant enemy, GameContext conditionalState,
        TileClass tileToAttackFrom)
    {
        var civInfo = combatant.GetCivInfo();
        var modifiers = new Counter<string>();

        combatant.Unit.ForEachMatchingUnique(UniqueType.Strength, conditionalState, true, unique =>
        {
            modifiers.Add(GetModifierStringFromUnique(unique), unique.Params[0].ToInt());
        });

        // e.g., Mehal Sefari https://civilization.fandom.com/wiki/Mehal_Sefari_(Civ5)
        foreach (var unique in combatant.GetMatchingUniques(UniqueType.StrengthNearCapital, conditionalState, true))
        {
            if (civInfo.Cities.Count == 0 || civInfo.GetCapital() == null) break;
            int distance = combatant.GetTile().AerialDistanceTo(civInfo.GetCapital().NotNull().GetCenterTile());
            // https://steamcommunity.com/sharedfiles/filedetails/?id=326411722#464287
            int effect = unchecked(unique.Params[0].ToInt() - 3 * distance);
            if (effect > 0)
                modifiers.Add(GetModifierStringFromUnique(unique), effect);
        }

        //https://www.carlsguides.com/strategy/civilization5/war/combatbonuses.php
        IEnumerable<MapUnitClass> adjacentUnits = combatant.GetTile().Neighbors.SelectMany(it => it.GetUnits()).ToList();
        if (!combatant.GetTile().Neighbors.Contains(enemy.GetTile()) && combatant.GetTile().Neighbors.Contains(tileToAttackFrom)
            && enemy is MapUnitCombatant enemyUnit)
            adjacentUnits = adjacentUnits.Concat([enemyUnit.Unit]).ToList();

        // e.g., Maori Warrior - https://civilization.fandom.com/wiki/Maori_Warrior_(Civ5)
        // maxByOrNull on the (String!) first parameter: ordinal string order, the first of equal maxima wins
        UniqueObject? strengthMalus = null;
        foreach (var unique in adjacentUnits.Where(it => it.Civ.IsAtWarWith(combatant.GetCivInfo()))
                     .SelectMany(it => it.GetMatchingUniques(UniqueType.StrengthForAdjacentEnemies))
                     .Where(it => combatant.MatchesFilter(it.Params[1]) && combatant.GetTile().MatchesFilter(it.Params[2])))
            if (strengthMalus == null || string.CompareOrdinal(strengthMalus.Params[0], unique.Params[0]) < 0)
                strengthMalus = unique;
        if (strengthMalus != null)
        {
            modifiers.Add("Adjacent enemy units", strengthMalus.Params[0].ToInt());
        }
        return modifiers;
    }

    public static Counter<string> GetAttackModifiers(ICombatant attacker, ICombatant defender, TileClass tileToAttackFrom)
    {
        var modifiers = GetGeneralModifiers(attacker, defender, CombatAction.Attack, tileToAttackFrom);

        if (attacker is MapUnitCombatant mapUnitAttacker)
        {
            var terrainAttackModifiers = GetTerrainAttackModifiers(mapUnitAttacker, defender, tileToAttackFrom);
            modifiers.Add(terrainAttackModifiers);

            // Air unit attacking with Air Sweep
            if (mapUnitAttacker.Unit.IsPreparingAirSweep())
                modifiers.Add(GetAirSweepAttackModifiers(mapUnitAttacker));

            if (defender.IsCity())
            {
                int sapperBonus = GetSapperBonus(mapUnitAttacker, tileToAttackFrom);
                if (sapperBonus != 0) modifiers["Sapper"] = sapperBonus;
            }

            if (mapUnitAttacker.IsMelee())
            {
                int numberOfOtherAttackersSurroundingDefender = defender.GetTile().Neighbors.Count(it =>
                    it.MilitaryUnit != null && it.MilitaryUnit != mapUnitAttacker.Unit
                    && it.MilitaryUnit.Civ == attacker.GetCivInfo()
                    && new MapUnitCombatant(it.MilitaryUnit).IsMelee());
                if (numberOfOtherAttackersSurroundingDefender > 0)
                {
                    float flankingBonus = BattleConstants.BaseFlankingBonus;

                    // e.g., Discipline policy - https://civilization.fandom.com/wiki/Discipline_(Civ5)
                    mapUnitAttacker.Unit.ForEachMatchingUnique(UniqueType.FlankAttackBonus,
                        GetGameContext(CombatAction.Attack, mapUnitAttacker, defender), true, unique =>
                        {
                            flankingBonus *= unique.Params[0].ToPercent();
                        });
                    modifiers["Flanking"] = KotlinMath.FloatToInt(flankingBonus * numberOfOtherAttackersSurroundingDefender);
                }
            }
        }

        return modifiers;
    }

    /// <summary>
    /// BNW Sapper (B023, bnw-spec 4.12): friendly units within <c>SapperBonusRange</c> tiles (1 when the ruleset states none) of the attacking
    /// tile that carry <see cref="UniqueType.SapperBonus"/> give the attacker the best matching bonus when it attacks a city. Bonuses do not stack.
    /// </summary>
    private static int GetSapperBonus(MapUnitCombatant attacker, TileClass tileToAttackFrom)
    {
        int range = Math.Max(1, attacker.GetCivInfo().GameInfo.Ruleset.ModOptions.Constants.SapperBonusRange);
        int best = 0;
        foreach (var tile in tileToAttackFrom.GetTilesInDistance(range).Append(tileToAttackFrom))
        {
            var sapper = tile.MilitaryUnit;
            if (sapper == null || sapper.Civ != attacker.GetCivInfo()) continue;
            foreach (var unique in sapper.GetMatchingUniques(UniqueType.SapperBonus))
                if (attacker.Unit.MatchesFilter(unique.Params[1], true)) best = Math.Max(best, unique.Params[0].ToInt());
        }
        return best;
    }

    private static Counter<string> GetTerrainAttackModifiers(MapUnitCombatant attacker, ICombatant defender, TileClass tileToAttackFrom)
    {
        var modifiers = new Counter<string>();
        if (attacker.Unit.IsEmbarked() && defender.GetTile().IsLand
            && !attacker.Unit.HasUnique(UniqueType.AttackAcrossCoast))
            modifiers["Landing"] = BattleConstants.LandingMalus;

        // Land Melee Unit attacking to Water
        if (attacker.Unit.Type.IsLandUnit() && !attacker.GetTile().IsWater && attacker.IsMelee() && defender.GetTile().IsWater
            && !attacker.Unit.HasUnique(UniqueType.AttackAcrossCoast))
            modifiers["Boarding"] = BattleConstants.BoardingMalus;

        // Melee Unit on water attacking to Land (not City) unit
        if (!attacker.Unit.Type.IsAirUnit() && attacker.IsMelee() && attacker.GetTile().IsWater && !defender.GetTile().IsWater
            && !attacker.Unit.HasUnique(UniqueType.AttackAcrossCoast) && !defender.IsCity())
            modifiers["Landing"] = BattleConstants.LandingMalus;

        if (IsMeleeAttackingAcrossRiverWithNoBridge(attacker, tileToAttackFrom, defender))
            modifiers["Across river"] = BattleConstants.AttackingAcrossRiverMalus;
        return modifiers;
    }

    private static bool IsMeleeAttackingAcrossRiverWithNoBridge(MapUnitCombatant attacker, TileClass tileToAttackFrom, ICombatant defender) =>
        attacker.IsMelee()
        && tileToAttackFrom.AerialDistanceTo(defender.GetTile()) == 1
        && tileToAttackFrom.IsConnectedByRiver(defender.GetTile())
        && !attacker.Unit.HasUnique(UniqueType.AttackAcrossRiver)
        && (!tileToAttackFrom.HasConnection(attacker.GetCivInfo()) // meaning, the tiles are not road-connected for this civ
            || !defender.GetTile().HasConnection(attacker.GetCivInfo())
            || !attacker.GetCivInfo().Tech.RoadsConnectAcrossRivers);

    public static Counter<string> GetAirSweepAttackModifiers(ICombatant attacker)
    {
        var modifiers = new Counter<string>();

        if (attacker is MapUnitCombatant mapUnitAttacker)
        {
            mapUnitAttacker.Unit.ForEachMatchingUnique(UniqueType.StrengthWhenAirsweep, unique =>
            {
                modifiers.Add(GetModifierStringFromUnique(unique), unique.Params[0].ToInt());
            });
        }

        return modifiers;
    }

    public static Counter<string> GetDefenceModifiers(ICombatant attacker, ICombatant defender, TileClass tileToAttackFrom)
    {
        var modifiers = GetGeneralModifiers(defender, attacker, CombatAction.Defend, tileToAttackFrom);
        var tile = defender.GetTile();

        if (defender is MapUnitCombatant mapUnitDefender && !mapUnitDefender.Unit.IsEmbarked()) // Embarked units get no terrain defensive bonuses
        {
            float tileDefenceBonus = tile.GetDefensiveBonus(unit: mapUnitDefender.Unit);
            if ((!mapUnitDefender.Unit.HasUnique(UniqueType.NoDefensiveTerrainBonus, null, true) && tileDefenceBonus > 0)
                || (!mapUnitDefender.Unit.HasUnique(UniqueType.NoDefensiveTerrainPenalty, null, true) && tileDefenceBonus < 0))
                modifiers["Tile"] = KotlinMath.FloatToInt(tileDefenceBonus * 100);

            if (mapUnitDefender.Unit.IsFortified() || mapUnitDefender.Unit.IsGuarding())
                modifiers["Fortification"] = unchecked(BattleConstants.FortificationBonus * mapUnitDefender.Unit.GetFortificationTurns());
        }

        return modifiers;
    }

    private static float ModifiersToFinalBonus(Counter<string> modifiers)
    {
        float finalModifier = 1f;
        foreach (int modifierValue in modifiers.Values) finalModifier += modifierValue / 100f;
        return finalModifier;
    }

    private static float GetHealthDependantDamageRatio(ICombatant combatant)
    {
        return combatant is not MapUnitCombatant mapUnitCombatant
            || mapUnitCombatant.Unit.HasUnique(UniqueType.NoDamagePenaltyWoundedUnits, null, true)
            ? 1f
            // Each 3 points of health reduces damage dealt by 1%
            : 1 - (100 - combatant.GetHealth()) / BattleConstants.DamageReductionWoundedUnitRatioPercentage;
    }

    /// <summary>Includes attack modifiers</summary>
    public static float GetAttackingStrength(ICombatant attacker, ICombatant defender, TileClass tileToAttackFrom)
    {
        float attackModifier = ModifiersToFinalBonus(GetAttackModifiers(attacker, defender, tileToAttackFrom));
        return Math.Max(1f, attacker.GetAttackingStrength(defender) * attackModifier);
    }

    /// <summary>Includes defence modifiers</summary>
    public static float GetDefendingStrength(ICombatant attacker, ICombatant defender, TileClass tileToAttackFrom)
    {
        float defenceModifier = ModifiersToFinalBonus(GetDefenceModifiers(attacker, defender, tileToAttackFrom));
        return Math.Max(1f, defender.GetDefendingStrength(attacker) * defenceModifier);
    }

    public static float GetRandomness(ICombatant combatant) =>
        combatant.GetCivInfo().GameInfo.SeededRandom(unchecked((long)combatant.GetCivInfo().GameInfo.Turns * combatant.GetTile().Position.ToVector2().GetHashCode())).NextFloat();

    /// <summary>randomnessFactor: between 0 and 1, defaults to <see cref="GetRandomness"/> of the attacker.</summary>
    public static int CalculateDamageToAttacker(ICombatant attacker, ICombatant defender, TileClass? tileToAttackFrom = null, float? randomnessFactor = null)
    {
        tileToAttackFrom ??= defender.GetTile();
        if (attacker.IsRanged() && !attacker.IsAirUnit()) return 0;
        if (defender.IsCivilian()) return 0;
        float ratio = GetAttackingStrength(attacker, defender, tileToAttackFrom) / GetDefendingStrength(attacker, defender, tileToAttackFrom);
        return KotlinMath.RoundToInt(DamageModifier(ratio, true, randomnessFactor ?? GetRandomness(attacker)) * GetHealthDependantDamageRatio(defender));
    }

    /// <summary>randomnessFactor: between 0 and 1, defaults to the turn and location-based random of the defender (avoids save scumming).</summary>
    public static int CalculateDamageToDefender(ICombatant attacker, ICombatant defender, TileClass? tileToAttackFrom = null, float? randomnessFactor = null)
    {
        tileToAttackFrom ??= defender.GetTile();
        if (defender.IsCivilian()) return BattleConstants.DamageToCivilianUnit;
        float ratio = GetAttackingStrength(attacker, defender, tileToAttackFrom) / GetDefendingStrength(attacker, defender, tileToAttackFrom);
        return KotlinMath.RoundToInt(DamageModifier(ratio, false, randomnessFactor ?? GetRandomness(defender)) * GetHealthDependantDamageRatio(attacker));
    }

    /// <summary>(max, min) bonus damage dealt to <paramref name="defender"/> from an additional <see cref="UniqueType.ExtraRangedAttack"/> - <c>(0, 0)</c> if not applicable.</summary>
    public static (int Max, int Min) GetExtraRangedAttackBonusDamage(MapUnitCombatant attacker, ICombatant defender, TileClass tileToAttackFrom)
    {
        int maxExtra = 0;
        int minExtra = 0;
        foreach (var fakeAttacker in Battle.GetExtraRangedAttackFakeUnits(attacker))
        {
            maxExtra = unchecked(maxExtra + CalculateDamageToDefender(fakeAttacker, defender, tileToAttackFrom, 1f));
            minExtra = unchecked(minExtra + CalculateDamageToDefender(fakeAttacker, defender, tileToAttackFrom, 0f));
        }
        return (maxExtra, minExtra);
    }

    // randomnessFactor is between 0 and 1.
    private static float DamageModifier(float attackerToDefenderRatio, bool damageToAttacker, float randomnessFactor)
    {
        // https://forums.civfanatics.com/threads/getting-the-combat-damage-math.646582/#post-15468029
        float strongerToWeakerRatio = KotlinMath.Pow(attackerToDefenderRatio, attackerToDefenderRatio < 1 ? -1 : 1);
        float ratioModifier = (KotlinMath.Pow((strongerToWeakerRatio + 3) / 4, 4) + 1) / 2;
        if ((damageToAttacker && attackerToDefenderRatio > 1) || (!damageToAttacker && attackerToDefenderRatio < 1)) // damage ratio from the weaker party is inverted
            ratioModifier = KotlinMath.Pow(ratioModifier, -1);
        float randomCenteredAround30 = 24 + 12 * randomnessFactor;
        return randomCenteredAround30 * ratioModifier;
    }
}
