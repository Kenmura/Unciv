using Unciv.Logic.Civilization;
using Unciv.Models.Ruleset.Unique;
using Unciv.Utils;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;
using MapUnitClass = Unciv.Logic.Map.MapUnits.MapUnit;
using NotificationCategory = Unciv.Logic.Civilization.Notification.NotificationCategory;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Logic.Battle;

// Ported from core/src/com/unciv/logic/battle/AirInterception.kt (T082).
// Battle.takeDamage / Battle.addXp are Battle.TakeDamage / Battle.AddXp (T083); Battle.DamageDealt is the DamageDealt record.
//
// Deviation: Kotlin shuffles the potential interceptors in airSweep with the unseeded kotlin.random.Random.Default. Here the shuffle uses
// the state-based seeded RNG of the attack context (determinism rule); only the order of equal-chance interceptors differs from Kotlin.
public static class AirInterception
{
    // Should draw an Interception if available on the tile from any Civ
    // Land Units deal 0 damage, and no XP for either party
    // Air Interceptors do Air Combat as if Melee (mutual damage) but using Ranged Strength. 5XP to both
    // But does not use the Interception mechanic bonuses/promotions
    // Counts as an Attack for both units
    // Will always draw out an Interceptor's attack (they cannot miss)
    // This means the combat against Air Units will execute and always deal damage
    // Random Civ at War will Intercept, prioritizing Air Units,
    // sorted by highest Intercept chance (same as regular Intercept)
    public static void AirSweep(MapUnitCombatant attacker, TileClass attackedTile)
    {
        // Air Sweep counts as an attack, even if nothing else happens
        attacker.Unit.AttacksThisTurn++;
        // copied and modified from reduceAttackerMovementPointsAndAttacks()
        // use up movement
        if (attacker.Unit.HasUnique(UniqueType.CanMoveAfterAttacking) || attacker.Unit.MaxAttacksPerTurn() > attacker.Unit.AttacksThisTurn)
        {
            // if it was a melee attack and we won, then the unit ALREADY got movement points deducted,
            // for the movement to the enemy's tile!
            // and if it's an air unit, it only has 1 movement anyway, so...
            if (!attacker.Unit.BaseUnit.IsAirUnit())
                attacker.Unit.UseMovementPoints(1f);
        }
        else attacker.Unit.CurrentMovement = 0f;
        string attackerName = attacker.GetName();

        // Make giant sequence of all potential Interceptors from all Civs isAtWarWith()
        var potentialInterceptors = new List<MapUnitClass>();
        foreach (var interceptingCiv in attacker.GetCivInfo().GameInfo.Civilizations
                     .Where(it => attacker.GetCivInfo().IsAtWarWith(it)))
        {
            potentialInterceptors.AddRange(interceptingCiv.Units.GetCivUnits()
                .Where(it => it.CanIntercept(attackedTile)));
        }

        // first priority, only Air Units
        if (potentialInterceptors.Any(it => it.BaseUnit.IsAirUnit()))
            potentialInterceptors = potentialInterceptors.Where(it => it.BaseUnit.IsAirUnit()).ToList();

        var rng = GameContext.ForCombatants(attacker, null, attackedTile, CombatAction.Intercept).StateBasedRandom("AirInterception.airSweep");
        // Pick highest chance interceptor
        // (sortedByDescending is stable, like OrderByDescending)
        foreach (var interceptor in potentialInterceptors
                     .Shuffled(rng) // randomize Civ
                     .OrderByDescending(it => it.InterceptChance()))
        {
            // No chance of Interceptor to miss (unlike regular Interception). Always want to deal damage
            // pairs of LocationAction for Notification
            var locations = LocationAction.Of(
                interceptor.CurrentTile.Position,
                attacker.Unit.CurrentTile.Position);
            interceptor.AttacksThisTurn++; // even if you miss, you took the shot
            if (!interceptor.BaseUnit.IsAirUnit())
            {
                string interceptorName = interceptor.Name;
                // Deal no damage (moddable in future?) and no XP
                string attackerText =
                    $"Our [{attackerName}] ([-0] HP) was attacked by an intercepting [{interceptorName}] ([-0] HP)";
                string interceptorText =
                    $"Our [{interceptorName}] ([-0] HP) intercepted and attacked an enemy [{attackerName}] ([-0] HP)";
                attacker.GetCivInfo().AddNotification(
                    attackerText, locations, NotificationCategory.War,
                    attackerName, NotificationIcon.War, interceptorName);
                interceptor.Civ.AddNotification(
                    interceptorText, locations, NotificationCategory.War,
                    interceptorName, NotificationIcon.War, attackerName);
                attacker.Unit.Action = null;
                return;
            }

            // Damage if Air v Air should work similar to Melee
            var damageDealt = Battle.TakeDamage(attacker, new MapUnitCombatant(interceptor));

            // 5 XP to both
            Battle.AddXp(new MapUnitCombatant(interceptor), 5, attacker);
            Battle.AddXp(attacker, 5, new MapUnitCombatant(interceptor));

            var locationsInterceptorUnknown =
                LocationAction.Of(attackedTile.Position, attacker.Unit.CurrentTile.Position);

            AddAirSweepInterceptionNotifications(
                attacker,
                interceptor,
                damageDealt,
                locationsInterceptorUnknown,
                locations);
            attacker.Unit.Action = null;
            return;
        }

        // No Interceptions available
        string noInterceptText = $"Nothing tried to intercept our [{attackerName}]";
        attacker.GetCivInfo().AddNotification(noInterceptText, NotificationCategory.War, attackerName);
        attacker.Unit.Action = null;
    }

    // (Kotlin note: check overlap with addInterceptionNotifications, and unify what we can)
    private static void AddAirSweepInterceptionNotifications(
        MapUnitCombatant attacker,
        MapUnitClass interceptor,
        DamageDealt damageDealt,
        IEnumerable<LocationAction> locationsInterceptorUnknown,
        IEnumerable<LocationAction> locations)
    {
        string attackerName = attacker.GetName();
        string interceptorName = interceptor.Name;

        string attackerText;
        if (attacker.IsDefeated())
        {
            attackerText = attacker.GetCivInfo().ViewableTiles.Contains(interceptor.GetTile())
                ? $"Our [{attackerName}] ([-{damageDealt.DefenderDealt}] HP) was destroyed by an intercepting [{interceptorName}] ([-{damageDealt.AttackerDealt}] HP)"
                : $"Our [{attackerName}] ([-{damageDealt.DefenderDealt}] HP) was destroyed by an unknown interceptor";
        }
        else if (new MapUnitCombatant(interceptor).IsDefeated())
            attackerText = $"Our [{attackerName}] ([-{damageDealt.DefenderDealt}] HP) destroyed an intercepting [{interceptorName}] ([-{damageDealt.AttackerDealt}] HP)";
        else
            attackerText = $"Our [{attackerName}] ([-{damageDealt.DefenderDealt}] HP) was attacked by an intercepting [{interceptorName}] ([-{damageDealt.AttackerDealt}] HP)";

        attacker.GetCivInfo().AddNotification(
            attackerText, locationsInterceptorUnknown, NotificationCategory.War,
            attackerName, NotificationIcon.War, NotificationIcon.Question);

        string interceptorText;
        if (attacker.IsDefeated())
            interceptorText = $"Our [{interceptorName}] ([-{damageDealt.AttackerDealt}] HP) intercepted and destroyed an enemy [{attackerName}] ([-{damageDealt.DefenderDealt}] HP)";
        else if (new MapUnitCombatant(interceptor).IsDefeated())
        {
            interceptorText = interceptor.Civ.ViewableTiles.Contains(attacker.GetTile())
                ? $"Our [{interceptorName}] ([-{damageDealt.AttackerDealt}] HP) intercepted and was destroyed by an enemy [{attackerName}] ([-{damageDealt.DefenderDealt}] HP)"
                : $"Our [{interceptorName}] ([-{damageDealt.AttackerDealt}] HP) intercepted and was destroyed by an unknown enemy";
        }
        else
            interceptorText = $"Our [{interceptorName}] ([-{damageDealt.AttackerDealt}] HP) intercepted and attacked an enemy [{attackerName}] ([-{damageDealt.DefenderDealt}] HP)";

        interceptor.Civ.AddNotification(
            interceptorText, locations, NotificationCategory.War,
            interceptorName, NotificationIcon.War, attackerName);
    }

    internal static DamageDealt TryInterceptAirAttack(
        MapUnitCombatant attacker,
        TileClass attackedTile,
        CivilizationObject interceptingCiv,
        ICombatant? defender)
    {
        var attackContext = GameContext.ForCombatants(attacker, defender, attackedTile, CombatAction.Intercept);
        if (attacker.Unit.HasUnique(UniqueType.CannotBeIntercepted, attackContext))
            return DamageDealt.None;
        var rng = attackContext.StateBasedRandom("AirInterception.tryInterceptAirAttack");

        // Pick highest chance interceptor
        var interceptor = interceptingCiv.Units.GetCivUnits()
            .Where(it => it.CanIntercept(attackedTile))
            .OrderByDescending(it => it.InterceptChance())
            .FirstOrDefault(unit =>
            {
                // Can't intercept if we have a unique preventing it
                var conditionalState = new GameContext(interceptingCiv, ourCombatant: new MapUnitCombatant(unit), theirCombatant: attacker, combatAction: CombatAction.Intercept, attackedTile: attackedTile);
                return !unit.GetMatchingUniques(UniqueType.CannotInterceptUnits, conditionalState)
                           .Any(it => attacker.MatchesFilter(it.Params[0]))
                       // Defender can't intercept either
                       && unit != (defender as MapUnitCombatant)?.Unit;
            });
        if (interceptor == null) return DamageDealt.None;

        interceptor.AttacksThisTurn++; // even if you miss, you took the shot
        // Does Intercept happen? If not, exit
        if (rng.NextFloat() > interceptor.InterceptChance() / 100f)
            return DamageDealt.None;

        int damage = BattleDamage.CalculateDamageToDefender(
            new MapUnitCombatant(interceptor),
            attacker);

        float damageFactor = 1f + (float)interceptor.InterceptDamagePercentBonus() / 100f;
        damageFactor *= attacker.Unit.ReceivedInterceptDamageFactor();

        damage = Math.Min(KotlinMath.FloatToInt((float)damage * damageFactor), attacker.Unit.Health);

        attacker.TakeDamage(damage);
        if (damage > 0)
            Battle.AddXp(new MapUnitCombatant(interceptor), 2, attacker);

        AddInterceptionNotifications(attacker, interceptor, damage);

        return new DamageDealt(0, damage);
    }

    private static void AddInterceptionNotifications(
        MapUnitCombatant attacker,
        MapUnitClass interceptor,
        int damage)
    {
        string attackerName = attacker.GetName();
        string interceptorName = interceptor.Name;

        var locations = LocationAction.Of(interceptor.CurrentTile.Position, attacker.Unit.CurrentTile.Position);
        string attackerText;
        if (!attacker.IsDefeated())
            attackerText = $"Our [{attackerName}] ([-{damage}] HP) was attacked by an intercepting [{interceptorName}] ([-0] HP)";
        else if (attacker.GetCivInfo().ViewableTiles.Contains(interceptor.GetTile()))
            attackerText = $"Our [{attackerName}] ([-{damage}] HP) was destroyed by an intercepting [{interceptorName}] ([-0] HP)";
        else attackerText = $"Our [{attackerName}] ([-{damage}] HP) was destroyed by an unknown interceptor";

        attacker.GetCivInfo().AddNotification(
            attackerText, interceptor.CurrentTile.Position, NotificationCategory.War,
            attackerName, NotificationIcon.War, interceptorName);

        string interceptorText = attacker.IsDefeated()
            ? $"Our [{interceptorName}] ([-0] HP) intercepted and destroyed an enemy [{attackerName}] ([-{damage}] HP)"
            : $"Our [{interceptorName}] ([-0] HP) intercepted and attacked an enemy [{attackerName}] ([-{damage}] HP)";
        interceptor.Civ.AddNotification(
            interceptorText, locations, NotificationCategory.War,
            interceptorName, NotificationIcon.War, attackerName);
    }
}
