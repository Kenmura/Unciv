using Unciv.Logic.Battle;
using Unciv.Models.Ruleset.Unique;
using Unciv.Utils;
using Unciv.View;
using BattleClass = Unciv.Logic.Battle.Battle;
using CityObject = Unciv.Logic.City.City;
using MapUnitClass = Unciv.Logic.Map.MapUnits.MapUnit;

namespace Unciv.Logic.Automation.Unit;

// Ported from core/src/com/unciv/logic/automation/unit/BattleHelper.kt (T090). Purity annotations dropped (T027 policy).
// - Kotlin Int arithmetic wraps: the attack values are computed in an unchecked context.
// - getCityAttackValue's `1.0 + 1.0 / friendlyUnitsAroundCity` is a Double division by an Int that can be 0 (Infinity), as in Kotlin.
public static class BattleHelper
{
    /// <summary>Returns true if the unit cannot further move this turn - NOT if an attack was successful!</summary>
    public static bool TryAttackNearbyEnemy(MapUnitView unitView, bool stayOnTile = false)
    {
        var unit = unitView.Unwrap();
        if (unitView.HasUnique(UniqueType.CannotAttack)) return false;
        var distanceToTiles = unit.Movement.GetDistanceToTiles();
        var attackableEnemies = TargetHelper.GetAttackableEnemies(unit, distanceToTiles, stayOnTile: stayOnTile)
            // Only take enemies we can fight without dying or are made to die
            .Where(it =>
            {
                var defender = BattleClass.GetMapCombatantOfTile(it.TileToAttack);
                return unitView.HasUnique(UniqueType.SelfDestructs) || (defender != null
                    && BattleDamage.CalculateDamageToAttacker(new MapUnitCombatant(unit), defender) < unitView.UnitHealth
                    && unit.GetDamageFromTerrain(it.TileToAttackFrom) <= 0);
                // For mounted units it is fine to attack from these tiles, but with current AI movement logic it is not easy to determine if our unit can meaningfully move away after attacking
                // Also, AI doesn't build tactical roads
            })
            .ToList();

        var enemyTileToAttack = ChooseAttackTarget(unit, attackableEnemies);

        if (enemyTileToAttack != null)
        {
            if (enemyTileToAttack.TileToAttack.MilitaryUnit == null && unitView.GetBaseUnit().IsRanged()
                && unit.Movement.CanMoveTo(enemyTileToAttack.TileToAttack)
                && distanceToTiles.ContainsKey(enemyTileToAttack.TileToAttack)) // Since the 'getAttackableEnemies' could return a tile we attack at range but cannot reach
            {
                // Ranged units should move to caputre a civilian unit instead of attacking it
                unit.Movement.MoveToTile(enemyTileToAttack.TileToAttack);
            }
            else
            {
                BattleClass.MoveAndAttack(new MapUnitCombatant(unit), enemyTileToAttack);
            }
        }
        return !unitView.HasMovement();
    }

    public static bool TryDisembarkUnitToAttackPosition(MapUnitClass unit)
    {
        if (!unit.BaseUnit.IsMelee() || !unit.BaseUnit.IsLandUnit || !unit.IsEmbarked()) return false;
        var unitDistanceToTiles = unit.Movement.GetDistanceToTiles();

        var attackableEnemiesNextTurn = TargetHelper.GetAttackableEnemies(unit, unitDistanceToTiles)
            // Only take enemies we can fight without dying
            .Where(it => BattleDamage.CalculateDamageToAttacker(
                new MapUnitCombatant(unit),
                BattleClass.GetMapCombatantOfTile(it.TileToAttack).NotNull()) < unit.Health)
            .Where(it => it.TileToAttackFrom.IsLand)
            .ToList();

        var enemyTileToAttackNextTurn = ChooseAttackTarget(unit, attackableEnemiesNextTurn);

        if (enemyTileToAttackNextTurn != null)
        {
            unit.Movement.MoveToTile(enemyTileToAttackNextTurn.TileToAttackFrom);
            return true;
        }
        return false;
    }

    /// <summary>Choses the best target in attackableEnemies, this could be a city or a unit.</summary>
    private static AttackableTile? ChooseAttackTarget(MapUnitClass unit, List<AttackableTile> attackableEnemies)
    {
        // Get the highest valued attackableEnemy
        var highestAttackValue = 0;
        AttackableTile? attackTile = null;
        // We always have to calculate the attack value even if there is only one attackableEnemy
        foreach (var attackableEnemy in attackableEnemies)
        {
            var tempAttackValue = attackableEnemy.TileToAttack.IsCityCenter()
                ? GetCityAttackValue(unit, (CityObject)attackableEnemy.TileToAttack.GetCity().NotNull())
                : GetUnitAttackValue(unit, attackableEnemy);
            if (tempAttackValue > highestAttackValue)
            {
                highestAttackValue = tempAttackValue;
                attackTile = attackableEnemy;
            }
        }
        // todo For air units, prefer to attack tiles with lower intercept chance
        // Only return that tile if it is actually a good tile to attack
        return highestAttackValue > 30 ? attackTile : null;
    }

    /// <summary>
    /// Returns a value which represents the attacker's motivation to attack a city.
    /// Siege units will almost always attack cities.
    /// Base value is 100(Mele) 110(Ranged) standard deviation is around 80 to 130
    /// </summary>
    private static int GetCityAttackValue(MapUnitClass attacker, CityObject city)
    {
        var attackerUnit = new MapUnitCombatant(attacker);
        var cityUnit = new CityCombatant(city);

        var canCaptureCity = attacker.BaseUnit.IsMelee() && !attacker.HasUnique(UniqueType.CannotCaptureCities);
        if (city.Health == 1)
            return canCaptureCity ? 10000 // Capture the city immediately!
                : 0; // No reason to attack, we won't make any difference

        if (canCaptureCity && city.Health <= Math.Max(BattleDamage.CalculateDamageToDefender(attackerUnit, cityUnit), 1))
            return 10000;

        if (attacker.BaseUnit.IsMelee())
        {
            var battleDamage = BattleDamage.CalculateDamageToAttacker(attackerUnit, cityUnit);
            if (unchecked(attacker.Health - battleDamage * 2) <= 0 && !attacker.HasUnique(UniqueType.SelfDestructs))
            {
                // The more fiendly units around the city, the more willing we should be to just attack the city
                var friendlyUnitsAroundCity = 0;
                city.GetCenterTile().ForEachTileInDistance(3, it =>
                {
                    if (it.MilitaryUnit?.Civ == attacker.Civ) friendlyUnitsAroundCity++;
                });
                // If we have more than 4 other units around the city, go for it
                if (friendlyUnitsAroundCity < 5)
                {
                    var attackerHealthModifier = 1.0 + 1.0 / friendlyUnitsAroundCity;
                    if (attacker.Health - battleDamage * attackerHealthModifier <= 0)
                        return 0; // We'll probably die next turn if we attack the city
                }
            }
        }

        return unchecked(CityAttackValueFromSurroundings(attacker, city));
    }

    private static int CityAttackValueFromSurroundings(MapUnitClass attacker, CityObject city)
    {
        var attackValue = 100;
        // Siege units should really only attack the city
        if (attacker.BaseUnit.IsProbablySiegeUnit()) attackValue += 100;
        // Ranged units don't take damage from the city
        else if (attacker.BaseUnit.IsRanged()) attackValue += 10;
        // Lower health cities have a higher priority to attack ranges from -20 to 30
        attackValue -= (city.Health - 60) / 2;

        // Add value based on number of units around the city
        var defendingCityCiv = city.Civ;
        city.GetCenterTile().ForEachTileInDistance(2, it =>
        {
            if (it.MilitaryUnit != null)
            {
                if (it.MilitaryUnit.Civ.IsAtWarWith(attacker.Civ))
                    attackValue -= 5;
                if (it.MilitaryUnit.Civ.IsAtWarWith(defendingCityCiv))
                    attackValue += 15;
            }
        });

        return attackValue;
    }

    /// <summary>
    /// Returns a value which represents the attacker's motivation to attack a unit.
    /// Base value is 100 and standard deviation is around 80 to 130
    /// </summary>
    private static int GetUnitAttackValue(MapUnitClass attacker, AttackableTile attackTile)
    {
        // Base attack value, there is nothing there...
        var attackValue = int.MinValue;
        // Prioritize attacking military
        var militaryUnit = attackTile.TileToAttack.MilitaryUnit;
        var civilianUnit = attackTile.TileToAttack.CivilianUnit;
        if (militaryUnit != null)
        {
            attackValue = unchecked(200 - militaryUnit.Health + // continuously prioritise lower-health units
                BattleDamage.CalculateDamageToDefender(new MapUnitCombatant(attacker), new MapUnitCombatant(militaryUnit)));
        }
        else if (civilianUnit != null)
        {
            attackValue = 50;
            // Only melee units should really attack/capture civilian units, ranged units may be able to capture by moving
            if (attacker.BaseUnit.IsMelee() || attacker.Movement.CanReachInCurrentTurn(attackTile.TileToAttack))
            {
                if (civilianUnit.IsGreatPerson())
                {
                    attackValue += 150;
                }
                if (civilianUnit.HasUnique(UniqueType.FoundCity, GameContext.IgnoreConditionalsState)) attackValue += 60;
            }
            else if (attacker.BaseUnit.IsRanged() && !civilianUnit.HasUnique(UniqueType.Uncapturable))
            {
                return 10; // Don't shoot civilians that we can capture!
            }
        }
        // Prioritise closer units as they are generally more threatening to this unit
        // Moving around less means we are straying less into enemy territory
        // Average should be around 2.5-5 early game and up to 35 for tanks in late game
        return unchecked(attackValue + KotlinMath.FloatToInt(attackTile.MovementLeftAfterMovingToAttackTile * 5));
    }
}
