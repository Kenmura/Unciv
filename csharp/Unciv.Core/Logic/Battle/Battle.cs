using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Transients;
using Unciv.Logic.Map;
using Unciv.Logic.Map.MapUnit.Actions;
using Unciv.Logic.Map.MapUnits;
using Unciv.Models;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Ruleset.Unit;
using Unciv.Models.StatTypes;
using Unciv.Utils;
using CityObject = Unciv.Logic.City.City;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;
using NotificationCategory = Unciv.Logic.Civilization.Notification.NotificationCategory;
using TileClass = Unciv.Logic.Map.Tiles.Tile;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.Logic.Battle;

// Ported from core/src/com/unciv/logic/battle/Battle.kt (T083). Purity annotations and the debug() log line dropped.
// Damage calculations according to civ v wiki and https://steamcommunity.com/sharedfiles/filedetails/?id=170194443
//
// Deviations:
// - `Battle.DamageDealt` stays the top-level DamageDealt record (T082 declared it for AirInterception).
// - Kotlin's `object Battle.FakeUnitForExtraRangedAttack` is a nested class that delegates every ICombatant member (including the default
//   interface methods, which C# does not forward automatically).
// - automateCityConquer's NextTurnAutomation.valueCityStateAlliance(GameView(...).civView, foundingCiv) is the INextTurnAutomation hook
//   (T094); with no hook installed the alliance is valued 0, i.e. never liberate.
// - The UncivGame.Current.worldScreen/settings lines (autoplay check, tutorial task) go through GameClientState.
public static class Battle
{
    /// <summary>
    /// Moves <paramref name="attacker"/> to <paramref name="attackableTile"/>, handles siege setup then attacks if still possible
    /// (by calling <see cref="Attack"/> or <see cref="Nuke.NUKE"/>). Does _not_ play the attack sound!
    ///
    /// Currently not used by UI, only by automation via BattleHelper.tryAttackNearbyEnemy
    /// </summary>
    public static void MoveAndAttack(MapUnitCombatant attacker, AttackableTile attackableTile)
    {
        if (!MovePreparingAttack(attacker, attackableTile, true)) return;
        AttackOrNuke(attacker, attackableTile);
    }

    /// <summary>
    /// Moves <paramref name="attacker"/> to <paramref name="attackableTile"/>, handles siege setup and returns <c>true</c> if an attack is still possible.
    ///
    /// This is a logic function, not UI, so e.g. sound needs to be handled after calling this.
    /// Only relevant for <see cref="MapUnitCombatant"/> - cities can't move, so callers with a <see cref="CityCombatant"/> attacker
    /// should skip calling this and treat the attack as always still possible.
    /// </summary>
    public static bool MovePreparingAttack(MapUnitCombatant attacker, AttackableTile attackableTile, bool tryHealPillage = false)
    {
        var tilesMovedThrough = attacker.Unit.Movement.GetDistanceToTiles().GetPathToTile(attackableTile.TileToAttackFrom);
        attacker.Unit.Movement.MoveToTile(attackableTile.TileToAttackFrom);
        // When calculating movement distance, we assume that a hidden tile is 1 movement point,
        // which can lead to EXCEEDINGLY RARE edge cases where you think
        // that you can attack a tile by passing through a HIDDEN TILE,
        // but the hidden tile is actually IMPASSIBLE, so you stop halfway!
        if (attacker.GetTile() != attackableTile.TileToAttackFrom) return false;
        // Rarely, a melee unit will target a civilian then move through the civilian to get
        // to attackableTile.tileToAttackFrom, meaning that they take the civilian.
        // This can lead to:
        // A. the melee unit from trying to capture their own unit (see #7282)
        // B. The civilian unit disappearing entirely (e.g. Great Person) and trying to capture a non-existent unit (see #8563)
        var combatant = GetMapCombatantOfTile(attackableTile.TileToAttack);
        if (combatant == null || combatant.GetCivInfo() == attacker.GetCivInfo()) return false;
        // Alternatively, maybe we DID reach that tile, but it turned out to be a hill or something,
        // so we expended all of our movement points!
        if (attacker.HasUnique(UniqueType.MustSetUp)
            && !attacker.Unit.IsSetUpForSiege()
            && attacker.Unit.HasMovement())
        {
            attacker.Unit.Action = UnitActionType.SetUp.Value();
            attacker.Unit.UseMovementPoints(1f);
        }

        if (tryHealPillage)
        {
            // Now lets retroactively see if we can pillage any improvement on the path improvement to heal
            // while still being able to attack
            foreach (var tileToPillage in tilesMovedThrough)
            {
                if (attacker.Unit.CurrentMovement <= 1f || attacker.Unit.Health > 90) break; // We are done pillaging

                if (UnitActionsPillage.CanPillage(attacker.Unit, tileToPillage)
                    && tileToPillage.CanPillageTileImprovement())
                {
                    UnitActionsPillage.GetPillageAction(attacker.Unit, tileToPillage)?.Action?.Invoke();
                }
            }
        }
        return attacker.Unit.HasMovement();
    }

    /// <summary>This is meant to be called only after all prerequisite checks have been done.</summary>
    public static DamageDealt AttackOrNuke(ICombatant attacker, AttackableTile attackableTile)
    {
        if (attacker is MapUnitCombatant mapUnitAttacker && mapUnitAttacker.Unit.IsNuclearWeapon())
        {
            Nuke.NUKE(mapUnitAttacker, attackableTile.TileToAttack);
            return DamageDealt.None;
        }
        return Attack(attacker, GetMapCombatantOfTile(attackableTile.TileToAttack).NotNull());
    }

    public static DamageDealt Attack(ICombatant attacker, ICombatant defender)
    {
        var attackedTile = defender.GetTile();
        if (attacker is MapUnitCombatant mapUnitAttacker)
        {
            mapUnitAttacker.Unit.AttacksSinceTurnStart.Add(attackedTile.Position);
        }
        else
        {
            attacker.GetCivInfo().AttacksSinceTurnStart.Add(new CivilizationObject.HistoricalAttackMemory(
                null,
                attacker.GetTile().Position,
                attackedTile.Position));
        }

        DamageDealt interceptDamage;
        if (attacker is MapUnitCombatant airAttacker && airAttacker.Unit.BaseUnit.IsAirUnit())
        {
            interceptDamage = AirInterception.TryInterceptAirAttack(airAttacker, attackedTile, defender.GetCivInfo(), defender);
            if (attacker.IsDefeated()) return interceptDamage;
        }
        else interceptDamage = DamageDealt.None;

        if (HasWithdrawnFromMelee(attacker, defender, attackedTile)) return DamageDealt.None;

        bool isAlreadyDefeatedCity = defender is CityCombatant && defender.IsDefeated();

        var extraRangedAttackDamage = TryExtraRangedAttack(attacker, defender);

        TriggerCombatUniques(attacker, defender, attackedTile);

        var attackSnapshot = GameEventHooks.BeginAttack(attacker, defender);
        var damageDealt = TakeDamage(attacker, defender) + extraRangedAttackDamage;
        damageDealt = ApplyHeavyCharge(attacker, defender, attackedTile, damageDealt);
        GameEventHooks.UnitAttacked(attackSnapshot, attacker, defender, damageDealt);

        // check if unit is captured by the attacker (prize ships unique)
        // As ravignir clarified in issue #4374, this only works for aggressor
        bool captureMilitaryUnitSuccess = BattleUnitCapture.TryCaptureMilitaryUnit(attacker, defender, attackedTile);

        if (!captureMilitaryUnitSuccess) // capture creates a new unit, but `defender` still is the original, so this function would still show a kill message
            PostBattleNotifications(attacker, defender, attackedTile, attacker.GetTile(), damageDealt);

        if (defender.GetCivInfo().IsBarbarian && attackedTile.IsBarbarianEncampment())
            defender.GetCivInfo().GameInfo.Barbarians.CampAttacked(attackedTile.Position);

        // This needs to come BEFORE the move-to-tile, because if we haven't conquered it we can't move there =)
        HandleCityDefeated(defender, attacker);

        // Add culture when defeating a barbarian when Honor policy is adopted, gold from enemy killed when honor is complete
        // or any enemy military unit with Sacrificial captives unique (can be either attacker or defender!)
        TriggerPostKillingUniques(defender, attacker, attackedTile);

        // B010 (bnw-spec 4.4.1): a unit that dies in a fight leaves a historical record on its tile
        if (defender.IsDefeated() && defender is MapUnitCombatant)
            Archaeology.ArchaeologyRules.RecordCombatDeath(attackedTile, attacker.IsRanged(), attacker.GetCivInfo(), defender.GetCivInfo());
        else if (attacker.IsDefeated() && attacker is MapUnitCombatant)
            Archaeology.ArchaeologyRules.RecordCombatDeath(attacker.GetTile(), attacker.IsRanged(), defender.GetCivInfo(), attacker.GetCivInfo());

        if (attacker is MapUnitCombatant mapAttacker && defender is MapUnitCombatant mapDefender)
        {
            TriggerDamageUniquesForUnit(mapAttacker, mapDefender, attackedTile, CombatAction.Attack);
            if (!attacker.IsRanged()) TriggerDamageUniquesForUnit(mapDefender, mapAttacker, attackedTile, CombatAction.Defend);
        }

        // Exploring units surviving an attack should "wake up"
        if (!defender.IsDefeated() && defender is MapUnitCombatant exploringDefender && exploringDefender.Unit.IsExploring())
            exploringDefender.Unit.Action = null;

        if (attacker is MapUnitCombatant unitAttacker)
        {
            if (unitAttacker.Unit.HasUnique(UniqueType.SelfDestructs))
                unitAttacker.Unit.Destroy();
            else if (unitAttacker.Unit.IsMoving())
                unitAttacker.Unit.Action = null;
            DoDestroyImprovementsAbility(unitAttacker, attackedTile, defender);
        }

        // Should be called after tryCaptureUnit(), as that might spawn a unit on the tile we go to
        if (!captureMilitaryUnitSuccess)
            PostBattleMoveToAttackedTile(attacker, defender, attackedTile);

        ReduceAttackerMovementPointsAndAttacks(attacker, defender, attackedTile);

        if (!isAlreadyDefeatedCity) PostBattleAddXp(attacker, defender);

        if (!isAlreadyDefeatedCity)
        {
            GivePostCombatPromotion(attacker, defender, attackedTile, CombatAction.Attack);
            GivePostCombatPromotion(defender, attacker, attackedTile, CombatAction.Defend);
        }

        if (attacker is CityCombatant)
        {
            string bombardText = $"Your city [{attacker.GetName()}] can bombard the enemy!";
            var notifications = attacker.GetCivInfo().Notifications;
            var cityCanBombardNotification = notifications.FirstOrDefault(it => it.Text == bombardText);
            // Kotlin: MutableList.remove(null) of a missing element is a no-op
            if (cityCanBombardNotification != null) notifications.Remove(cityCanBombardNotification);
        }

        return damageDealt + interceptDamage;
    }

    private static void TriggerPostKillingUniques(ICombatant defender, ICombatant attacker, TileClass attackedTile)
    {
        if (defender.IsDefeated() && defender is MapUnitCombatant defendingUnit && !defendingUnit.Unit.IsCivilian())
        {
            TryEarnFromKilling(attacker, defendingUnit);
            TryHealAfterKilling(attacker);

            if (attacker is MapUnitCombatant attackingUnit) TriggerVictoryUniques(attackingUnit, defendingUnit, attackedTile);
            TriggerDefeatUniques(defendingUnit, attacker, attackedTile);
        }
        else if (attacker.IsDefeated() && attacker is MapUnitCombatant defeatedAttacker && !defeatedAttacker.Unit.IsCivilian())
        {
            TryEarnFromKilling(defender, defeatedAttacker);
            TryHealAfterKilling(defender);

            if (defender is MapUnitCombatant survivingDefender) TriggerVictoryUniques(survivingDefender, defeatedAttacker, attackedTile);
            TriggerDefeatUniques(defeatedAttacker, defender, attackedTile);
        }
    }

    private static void HandleCityDefeated(ICombatant defender, ICombatant attacker)
    {
        if (!defender.IsDefeated() || defender is not CityCombatant defendingCity || attacker is not MapUnitCombatant attackingUnit
            || (!attackingUnit.IsMelee() && !attackingUnit.Unit.HasUnique(UniqueType.CanDestroyCities, checkCivInfoUniques: true))
            || attackingUnit.Unit.HasUnique(UniqueType.CannotCaptureCities, checkCivInfoUniques: true))
            return;

        // Must come before normal conquest logic so units that cannot capture cities can still destroy them
        // Melee units can capture capitals; any unit with CanDestroyCities can destroy non-capital cities
        if (defendingCity.IsDefeated() && defendingCity.City.CanBeDestroyed())
        {
            var destroyFilters = attackingUnit.Unit.GetMatchingUniques(UniqueType.CanDestroyCities).Select(it => it.Params[0]);
            if (destroyFilters.Any(filter => defendingCity.City.MatchesFilter(filter.Trim(), attackingUnit.GetCivInfo())))
            {
                string cityName = defendingCity.City.Name;
                var defendingCiv = defendingCity.GetCivInfo();

                //Notification to attacker
                attackingUnit.GetCivInfo().AddNotification(
                    $"We have destroyed the city of [{cityName}]!",
                    defendingCity.GetTile().Position,
                    NotificationCategory.War,
                    NotificationIcon.War);

                defendingCity.City.DestroyCity();

                if (defendingCiv.Cities.Count == 0)
                {
                    DestroyIfDefeated(defendingCiv, attackingUnit.GetCivInfo(), defendingCity.GetTile().Position);
                }

                return; // stop further logic because city was destroyed
            }
        }

        // Barbarians can't capture cities, instead raiding them for gold
        if (attackingUnit.Unit.Civ.IsBarbarian)
        {
            defendingCity.TakeDamage(-1); // Back to 2 HP
            int ransom = Math.Min(200, defendingCity.City.Civ.Gold);
            defendingCity.City.Civ.AddGold(-ransom);
            defendingCity.City.Civ.AddNotification(
                $"Barbarians raided [{defendingCity.City.Name}] and stole [{ransom}] Gold from your treasury!",
                defendingCity.City.Location,
                NotificationCategory.War,
                NotificationIcon.War);
            attackingUnit.Unit.Destroy(); // Remove the barbarian
        }
        else
            ConquerCity(defendingCity.City, attackingUnit);
    }

    private static void TriggerCombatUniques(ICombatant attacker, ICombatant defender, TileClass attackedTile)
    {
        var attackerContext = new GameContext(attacker.GetCivInfo(),
            ourCombatant: attacker, theirCombatant: defender, tile: attackedTile, combatAction: CombatAction.Attack);
        if (attacker is MapUnitCombatant attackingUnit)
        {
            foreach (var unique in attackingUnit.Unit.GetTriggeredUniques(UniqueType.TriggerUponCombat, attackerContext))
            {
                var unit = unique.Params[0] == Constants.TargetUnit && defender is MapUnitCombatant targetDefender
                    ? targetDefender.Unit
                    : attackingUnit.Unit;
                UniqueTriggerActivation.TriggerUnique(unique, unit);
            }
        }
        var defenderContext = new GameContext(defender.GetCivInfo(),
            ourCombatant: defender, theirCombatant: attacker, tile: attackedTile, combatAction: CombatAction.Defend);
        if (defender is MapUnitCombatant defendingUnit)
        {
            foreach (var unique in defendingUnit.Unit.GetTriggeredUniques(UniqueType.TriggerUponCombat, defenderContext))
            {
                var unit = unique.Params[0] == Constants.TargetUnit && attacker is MapUnitCombatant targetAttacker
                    ? targetAttacker.Unit
                    : defendingUnit.Unit;
                UniqueTriggerActivation.TriggerUnique(unique, unit);
            }
        }
    }

    private static void TriggerVictoryUniques(MapUnitCombatant ourUnit, MapUnitCombatant enemy, TileClass attackedTile)
    {
        var gameContext = new GameContext(civInfo: ourUnit.GetCivInfo(),
            ourCombatant: ourUnit, theirCombatant: enemy, tile: attackedTile);
        foreach (var unique in ourUnit.Unit.GetTriggeredUniques(UniqueType.TriggerUponDefeatingUnit, gameContext,
                     it => enemy.Unit.MatchesFilter(it.Params[0])))
            UniqueTriggerActivation.TriggerUnique(unique, ourUnit.Unit,
                triggerNotificationText: $"due to {ourUnit.GetNotificationDisplay("our ")} defeating a [{enemy.GetName()}]");
    }

    private static void TriggerDamageUniquesForUnit(MapUnitCombatant triggeringUnit, MapUnitCombatant enemy, TileClass attackedTile, CombatAction combatAction)
    {
        var gameContext = new GameContext(civInfo: triggeringUnit.GetCivInfo(),
            ourCombatant: triggeringUnit, theirCombatant: enemy, tile: attackedTile, combatAction: combatAction);

        foreach (var unique in triggeringUnit.Unit.GetTriggeredUniques(UniqueType.TriggerUponDamagingUnit, gameContext,
                     it => enemy.MatchesFilter(it.Params[0])))
        {
            if (unique.Params[0] == Constants.TargetUnit)
            {
                UniqueTriggerActivation.TriggerUnique(unique, enemy.Unit,
                    triggerNotificationText: $"due to {enemy.GetNotificationDisplay("our ")} being damaged by a [{triggeringUnit.GetName()}]");
            }
            else
            {
                UniqueTriggerActivation.TriggerUnique(unique, triggeringUnit.Unit,
                    triggerNotificationText: $"due to {triggeringUnit.GetNotificationDisplay("our ")} damaging a [{enemy.GetName()}]");
            }
        }
    }

    internal static void TriggerDefeatUniques(MapUnitCombatant ourUnit, ICombatant enemy, TileClass attackedTile)
    {
        var gameContext = new GameContext(civInfo: ourUnit.GetCivInfo(),
            ourCombatant: ourUnit, theirCombatant: enemy, tile: attackedTile);
        foreach (var unique in ourUnit.Unit.GetTriggeredUniques(UniqueType.TriggerUponDefeat, gameContext))
            UniqueTriggerActivation.TriggerUnique(unique, ourUnit.Unit,
                triggerNotificationText: $"due to {ourUnit.GetNotificationDisplay("our ")} being defeated by a [{enemy.GetName()}]");
    }

    private static void TryEarnFromKilling(ICombatant civUnit, MapUnitCombatant defeatedUnit)
    {
        int unitStr = Math.Max(defeatedUnit.Unit.BaseUnit.Strength, defeatedUnit.Unit.BaseUnit.RangedStrength);
        int unitCost = defeatedUnit.Unit.BaseUnit.Cost;

        var bonusUniques = GetKillUnitPlunderUniques(civUnit, defeatedUnit);

        foreach (var unique in bonusUniques)
        {
            if (!defeatedUnit.MatchesFilter(unique.Params[1])) continue;

            float yieldPercent = unique.Params[0].ToFloat() / 100;
            string defeatedUnitYieldSourceType = unique.Params[2];
            int yieldTypeSourceAmount =
                defeatedUnitYieldSourceType == "Cost" ? unitCost : unitStr;
            int yieldAmount = KotlinMath.FloatToInt(yieldTypeSourceAmount * yieldPercent);

            var resource = civUnit.GetCivInfo().GameInfo.Ruleset.GetGameResource(unique.Params[3]);
            if (resource == null) continue;
            civUnit.GetCivInfo().AddGameResource(resource, yieldAmount);
        }

        // CS friendship from killing barbarians
        if (defeatedUnit.GetCivInfo().IsBarbarian && !defeatedUnit.IsCivilian() && civUnit.GetCivInfo().IsMajorCiv())
        {
            foreach (var cityState in defeatedUnit.GetCivInfo().GameInfo.GetAliveCityStates())
            {
                if (civUnit.GetCivInfo().Knows(cityState) && defeatedUnit.Unit.ThreatensCiv(cityState))
                {
                    cityState.CityStateFunctions.ThreateningBarbarianKilledBy(civUnit.GetCivInfo());
                }
            }
        }

        // CS war with major pseudo-quest
        foreach (var cityState in defeatedUnit.GetCivInfo().GameInfo.GetAliveCityStates())
        {
            cityState.QuestManager.MilitaryUnitKilledBy(civUnit.GetCivInfo(), defeatedUnit.GetCivInfo());
        }
    }

    /// <summary>See <see cref="UniqueType.KillUnitPlunder"/> for params</summary>
    private static List<UniqueObject> GetKillUnitPlunderUniques(ICombatant civUnit, MapUnitCombatant defeatedUnit)
    {
        var bonusUniques = new List<UniqueObject>();

        var gameContext = new GameContext(civInfo: civUnit.GetCivInfo(), ourCombatant: civUnit, theirCombatant: defeatedUnit);
        if (civUnit is MapUnitCombatant mapUnitCombatant)
        {
            bonusUniques.AddRange(mapUnitCombatant.GetMatchingUniques(UniqueType.KillUnitPlunder, gameContext, true));
        }
        else
        {
            civUnit.GetCivInfo().ForEachMatchingUnique(UniqueType.KillUnitPlunder, gameContext, bonusUniques.Add);
        }

        var cityWithReligion =
            civUnit.GetTile().GetTilesInDistance(4).FirstOrDefault(
                it => it.IsCityCenter() && ((CityObject)it.GetCity().NotNull()).GetMatchingUniques(UniqueType.KillUnitPlunderNearCity, gameContext).Any())
                ?.GetCity() as CityObject;
        if (cityWithReligion != null)
        {
            cityWithReligion.ForEachMatchingUnique(UniqueType.KillUnitPlunderNearCity, gameContext, bonusUniques.Add);
        }
        return bonusUniques;
    }

    internal static DamageDealt TakeDamage(ICombatant attacker, ICombatant defender)
    {
        var attackerContext = GameContext.ForCombatants(attacker, defender, defender.GetTile(), CombatAction.Attack);
        var defenderContext = GameContext.ForCombatants(defender, attacker, defender.GetTile(), CombatAction.Defend);
        int potentialDamageToDefender = BattleDamage.CalculateDamageToDefender(attacker, defender);
        int potentialDamageToAttacker = BattleDamage.CalculateDamageToAttacker(attacker, defender);
        var rng = attackerContext.StateBasedRandom("Battle.takeDamage");

        int attackerHealthBefore = attacker.GetHealth();
        int defenderHealthBefore = defender.GetHealth();

        if (defender is MapUnitCombatant { Unit: var defendingUnit } mapDefender && defendingUnit.IsCivilian() && attacker.IsMelee())
        {
            BattleUnitCapture.CaptureCivilianUnit(attacker, mapDefender);
        }
        else if (attacker.IsRanged() && !attacker.IsAirUnit()) // Air Units are Ranged, but take damage as well
        {
            defender.TakeDamage(potentialDamageToDefender); // straight up
        }
        else
        {
            //melee attack is complicated, because either side may defeat the other midway
            //so...for each round, we randomize who gets the attack in. Seems to be a good way to work for now.

            while (unchecked(potentialDamageToDefender + potentialDamageToAttacker) > 0)
            {
                if (rng.NextInt(unchecked(potentialDamageToDefender + potentialDamageToAttacker)) < potentialDamageToDefender)
                {
                    potentialDamageToDefender--;
                    defender.TakeDamage(1);
                    if (defender.IsDefeated()) break;
                }
                else
                {
                    potentialDamageToAttacker--;
                    attacker.TakeDamage(1);
                    if (attacker.IsDefeated()) break;
                }
            }
        }

        int defenderDamageDealt = unchecked(attackerHealthBefore - attacker.GetHealth());
        int attackerDamageDealt = unchecked(defenderHealthBefore - defender.GetHealth());

        TriggerLosingHPUniques(attacker, attackerContext, attackerDamageDealt, defender, defenderContext, defenderDamageDealt);

        PlunderFromDamage(attacker, defender, attackerDamageDealt);
        return new DamageDealt(attackerDamageDealt, defenderDamageDealt);
    }

    private static void TriggerLosingHPUniques(
        ICombatant attacker,
        GameContext attackerContext,
        int attackerDamageDealt,
        ICombatant defender,
        GameContext defenderContext,
        int defenderDamageDealt)
    {
        void TriggerUnique(ICombatant combatant, UniqueObject unique, CombatAction action)
        {
            string triggerText =
                action == CombatAction.Attack ? $"due to losing [{defenderDamageDealt}] HP"
                : $"due to losing [{attackerDamageDealt}] HP";
            switch (combatant)
            {
                case MapUnitCombatant mapUnitCombatant:
                    UniqueTriggerActivation.TriggerUnique(unique, mapUnitCombatant.Unit, triggerNotificationText: triggerText);
                    break;
                case CityCombatant cityCombatant:
                    UniqueTriggerActivation.TriggerUnique(unique, cityCombatant.City, triggerNotificationText: triggerText);
                    break;
            }
        }

        foreach (var unique in attacker.GetTriggeredUniques(UniqueType.TriggerUponLosingHealth, attackerContext,
                     it => it.Params[0].ToInt() <= defenderDamageDealt))
        {
            var combatant = unique.Params[0] == Constants.TargetUnit ? defender : attacker;
            TriggerUnique(combatant, unique, CombatAction.Attack);
        }

        foreach (var unique in defender.GetTriggeredUniques(UniqueType.TriggerUponLosingHealth, defenderContext,
                     it => it.Params[0].ToInt() <= attackerDamageDealt))
        {
            var combatant = unique.Params[0] == Constants.TargetUnit ? attacker : defender;
            TriggerUnique(combatant, unique, CombatAction.Defend);
        }
    }

    private static void PlunderFromDamage(ICombatant plunderingUnit, ICombatant plunderedUnit, int damageDealt)
    {
        // implementation based on the description of the original civilopedia, see issue #4374
        if (plunderingUnit is not MapUnitCombatant plunderer) return;
        var civ = plunderer.GetCivInfo();
        var plunderedGoods = new Models.StatTypes.Stats();

        foreach (var unique in plunderer.Unit.GetMatchingUniques(UniqueType.DamageUnitsPlunder, checkCivInfoUniques: true))
        {
            if (!plunderedUnit.MatchesFilter(unique.Params[1])) continue;

            float percentage = unique.Params[0].ToFloat();
            float amount = percentage / 100f * damageDealt;
            string resourceName = unique.Params[2];
            var resource = plunderedUnit.GetCivInfo().GameInfo.Ruleset.GetGameResource(resourceName);
            if (resource == null) continue;

            if (resource is Stat stat)
            {
                plunderedGoods.Add(stat, amount);
                continue; // Notification and adding to the civ happens all at once for stats further down
            }

            int plunderedAmount = KotlinMath.RoundToInt(amount);
            civ.AddGameResource(resource, plunderedAmount);
            string icon = resource is SubStat subStat ? subStat.Icon() : $"ResourceIcons/{resourceName}";
            civ.AddNotification(
                $"{plunderer.GetNotificationDisplay("Your ")} plundered [{plunderedAmount}] [{resourceName}] from [{plunderedUnit.GetName()}]",
                plunderedUnit.GetTile().Position,
                NotificationCategory.War,
                plunderer.GetName(), NotificationIcon.War, icon,
                plunderedUnit is CityCombatant ? NotificationIcon.City : plunderedUnit.GetName());
        }

        foreach (var (key, value) in plunderedGoods)
        {
            int plunderedAmount = KotlinMath.FloatToInt(value);
            if (plunderedAmount == 0) continue;
            civ.AddStat(key, plunderedAmount);
            civ.AddNotification(
                $"{plunderer.GetNotificationDisplay("your ")} plundered [{plunderedAmount}] [{key}] from [{plunderedUnit.GetName()}]",
                plunderedUnit.GetTile().Position,
                NotificationCategory.War,
                plunderer.GetName(), NotificationIcon.War, $"StatIcons/{key}",
                plunderedUnit is CityCombatant ? NotificationIcon.City : plunderedUnit.GetName());
        }
    }

    internal static void PostBattleNotifications(
        ICombatant attacker,
        ICombatant defender,
        TileClass attackedTile,
        TileClass? attackerTile = null,
        DamageDealt? damageDealt = null)
    {
        if (attacker.GetCivInfo() == defender.GetCivInfo()) return;

        // If what happened was that a civilian unit was captured, that's dealt with in the captureCivilianUnit function
        string battleActionIcon;
        string battleActionString;
        if (attacker is not CityCombatant && attacker.IsDefeated())
        {
            battleActionIcon = NotificationIcon.War;
            battleActionString = "was destroyed while attacking";
        }
        else if (!defender.IsDefeated())
        {
            battleActionIcon = NotificationIcon.War;
            battleActionString = "has attacked";
        }
        else if (defender.IsCity() && attacker.IsMelee() && attacker.GetCivInfo().IsBarbarian)
        {
            battleActionIcon = NotificationIcon.War;
            battleActionString = "has raided";
        }
        else if (defender.IsCity() && attacker.IsMelee())
        {
            battleActionIcon = NotificationIcon.War;
            battleActionString = "has captured";
        }
        else
        {
            battleActionIcon = NotificationIcon.Death;
            battleActionString = "has destroyed";
        }
        string attackerString =
            attacker.IsCity() ? "Enemy city [" + attacker.GetName() + "]"
            : "An enemy [" + attacker.GetName() + "]";

        string defenderString =
            defender.IsCity()
                ? (defender.IsDefeated() && attacker.IsRanged() ? "the defence of [" + defender.GetName() + "]"
                    : "[" + defender.GetName() + "]")
                : defender.GetNotificationDisplay("our ");

        string attackerHurtString = damageDealt != null && damageDealt.Value.DefenderDealt != 0 ? $" ([-{damageDealt.Value.DefenderDealt}] HP)" : "";
        string defenderHurtString = damageDealt != null ? $" ([-{damageDealt.Value.AttackerDealt}] HP)" : "";
        string notificationString = $"{attackerString}{attackerHurtString} {battleActionString} {defenderString}{defenderHurtString}";
        string attackerIcon = attacker is CityCombatant ? NotificationIcon.City : attacker.GetName();
        string defenderIcon = defender is CityCombatant ? NotificationIcon.City : defender.GetName();

        var locations = LocationAction.Of(attackedTile.Position, attackerTile?.Position);
        defender.GetCivInfo().AddNotification(notificationString, locations, NotificationCategory.War, attackerIcon, battleActionIcon, defenderIcon);
    }

    private static void TryHealAfterKilling(ICombatant attacker)
    {
        if (attacker is not MapUnitCombatant mapUnitCombatant) return;

        mapUnitCombatant.Unit.ForEachMatchingUnique(UniqueType.HealsAfterKilling, null, true, unique =>
        {
            int amountToHeal = unique.Params[0].ToInt();
            mapUnitCombatant.Unit.HealBy(amountToHeal);
        });
    }

    private static void PostBattleMoveToAttackedTile(ICombatant attacker, ICombatant defender, TileClass attackedTile)
    {
        if (!attacker.IsMelee()) return;
        if (!defender.IsDefeated() && defender.GetCivInfo() != attacker.GetCivInfo()) return;
        if (attacker is not MapUnitCombatant mapUnitCombatant) return;
        if (mapUnitCombatant.Unit.Cache.CannotMove) return;
        // Example: If the unit we attacked made us lose movement points
        if (!mapUnitCombatant.Unit.Movement.CanReachInCurrentTurn(attackedTile)) return;
        // This is so that if we attack e.g. a barbarian in enemy territory that we can't enter, we won't enter it
        if (!mapUnitCombatant.Unit.Movement.CanMoveTo(attackedTile)) return;

        // Units that can move after attacking are not affected by zone of control if the
        // movement is caused by killing a unit. Effectively, this means that attack movements
        // are exempt from zone of control, since units that cannot move after attacking already
        // lose all remaining movement points anyway.
        mapUnitCombatant.Unit.Movement.MoveToTile(attackedTile, considerZoneOfControl: false);
        mapUnitCombatant.Unit.MostRecentMoveType = UnitMovementMemoryType.UnitAttacked;
    }

    private static void PostBattleAddXp(ICombatant attacker, ICombatant defender)
    {
        void AddXpToBoth(int attackerXp, int defenderXp)
        {
            AddXp(attacker, attackerXp, defender);
            AddXp(defender, defenderXp, attacker);
        }

        if (attacker.IsAirUnit()) AddXpToBoth(4, 2);
        else if (attacker.IsRanged()) // ranged attack
        {
            if (defender.IsCity()) AddXpToBoth(3, 2);
            else AddXpToBoth(2, 2);
        }
        else if (!defender.IsCivilian()) // Captured units don't grant XP
            AddXpToBoth(5, 4);
    }

    // XP!
    internal static void AddXp(ICombatant thisCombatant, int amount, ICombatant otherCombatant)
    {
        if (thisCombatant is not MapUnitCombatant unitCombatant) return;
        var civ = unitCombatant.GetCivInfo();
        bool otherIsBarbarian = otherCombatant.GetCivInfo().IsBarbarian;
        var promotions = unitCombatant.Unit.Promotions;
        var modConstants = civ.GameInfo.Ruleset.ModOptions.Constants;

        if (otherIsBarbarian && promotions.TotalXpProduced() >= modConstants.MaxXPfromBarbarians)
            return;
        bool unitCouldAlreadyPromote = promotions.CanBePromoted();

        var gameContext = new GameContext(civInfo: civ, ourCombatant: unitCombatant, theirCombatant: otherCombatant);

        int baseXP = unchecked(amount + KotlinMath.SumOf(
            unitCombatant.GetMatchingUniques(UniqueType.FlatXPGain, gameContext, true), it => it.Params[0].ToInt()));

        double xpBonus = 0.0;
        foreach (var unique in unitCombatant.GetMatchingUniques(UniqueType.PercentageXPGain, gameContext, true))
            xpBonus += unique.Params[0].ToDouble();
        if (civ.IsAI() && civ.IsMajorCiv()) xpBonus += civ.GameInfo.GetDifficulty().AiFreeXpPercent; // B023: AI handicap (HandicapInfos.AIFreeXPPercent)
        double xpModifier = 1.0 + xpBonus / 100;

        int xpGained = KotlinMath.DoubleToInt(baseXP * xpModifier);
        promotions.XP = unchecked(promotions.XP + xpGained);

        if (!otherIsBarbarian && civ.IsMajorCiv()) // Can't get great generals from Barbarians
        {
            var ruleset = civ.GameInfo.Ruleset;
            IEnumerable<BaseUnit> greatGeneralUnits = ruleset.GreatGeneralUnits
                .Where(it => ((IHasUniques)it).HasUnique(UniqueType.GreatPersonFromCombat, gameContext) &&
                        // Check if the unit is allowed for the Civ, ignoring build constrants
                        it.GetRejectionReasons(civ).All(reason =>
                            reason.IsConstructionRejection() ||
                            // Allow Generals even if not allowed via tech
                            reason.TechPolicyEraWonderRequirements()));
            // For compatibility with older rulesets
            if (ruleset.GreatGeneralUnits.Count == 0 && ruleset.Units.TryGetValue("Great General", out var defaultGeneral))
                greatGeneralUnits = greatGeneralUnits.Append(defaultGeneral);

            foreach (var unit in greatGeneralUnits)
            {
                double greatGeneralPointsBonus = 0.0;
                foreach (var unique in unitCombatant.GetMatchingUniques(UniqueType.GreatPersonEarnedFaster, gameContext, true)
                             .Where(it => unit.MatchesFilter(it.Params[0], gameContext)))
                    greatGeneralPointsBonus += unique.Params[1].ToDouble();
                double greatGeneralPointsModifier = 1.0 + greatGeneralPointsBonus / 100;

                int greatGeneralPointsGained = KotlinMath.DoubleToInt(xpGained * greatGeneralPointsModifier);
                civ.GreatPeople.GreatGeneralPointsCounter[unit.Name] = unchecked(civ.GreatPeople.GreatGeneralPointsCounter[unit.Name] + greatGeneralPointsGained);
            }
        }

        if (!unitCombatant.IsDefeated() && !unitCouldAlreadyPromote && promotions.CanBePromoted())
        {
            civ.AddNotification($"[{unitCombatant.Unit.DisplayName()}] can be promoted!",
                new NotificationAction[]
                {
                    new MapUnitAction(unitCombatant.Unit.CurrentTile.Position, unitCombatant.Unit.Id),
                    new PromoteUnitAction(unitCombatant.Unit.Name, unitCombatant.Unit.CurrentTile.Position, unitCombatant.Unit.Id),
                },
                NotificationCategory.Units, unitCombatant.Unit.Name);
        }
    }

    private static void ReduceAttackerMovementPointsAndAttacks(ICombatant attacker, ICombatant defender, TileClass attackedTile)
    {
        if (attacker is not MapUnitCombatant mapUnitCombatant)
        {
            if (attacker is CityCombatant cityCombatant) cityCombatant.City.AttackedThisTurn = true;
            return;
        }

        var unit = mapUnitCombatant.Unit;
        // If captured this civilian, doesn't count as attack
        // And we've used a movement already
        if (defender.IsCivilian() && attacker.GetTile() == defender.GetTile()) return;

        var gameContext = GameContext.ForCombatants(attacker, defender, attackedTile, CombatAction.Attack);
        unit.AttacksThisTurn += 1;
        if (unit.HasUnique(UniqueType.CanMoveAfterAttacking, gameContext)
            || unit.MaxAttacksPerTurn() > unit.AttacksThisTurn)
        {
            // if it was a melee attack, and we won, then the unit ALREADY got movement points deducted,
            // for the movement to the enemy's tile!
            // and if it's an air unit, it only has 1 movement anyway, so...
            if (!unit.BaseUnit.IsAirUnit() && !(attacker.IsMelee() && defender.IsDefeated()))
                unit.UseMovementPoints(1f);
        }
        else unit.CurrentMovement = 0f;

        if (unit.IsFortified() || unit.IsSleeping() || unit.IsGuarding())
            unit.Action = null; // but not, for instance, if it's Set Up - then it should definitely keep the action!
    }

    private static void ConquerCity(CityObject city, MapUnitCombatant attacker)
    {
        var attackerCiv = attacker.GetCivInfo();

        attackerCiv.AddNotification($"We have conquered the city of [{city.Name}]!", city.Location, NotificationCategory.War, NotificationIcon.War);

        city.HasJustBeenConquered = true;
        var centerTile = city.GetCenterTile();
        if (centerTile.MilitaryUnit != null) centerTile.MilitaryUnit.Destroy();
        if (centerTile.CivilianUnit != null) BattleUnitCapture.CaptureCivilianUnit(attacker, new MapUnitCombatant(centerTile.CivilianUnit), checkDefeat: false);
        foreach (var airUnit in centerTile.AirUnits.ToList()) airUnit.Destroy();

        var gameContext = new GameContext(civInfo: attackerCiv, city: city, unit: attacker.Unit, ourCombatant: attacker, attackedTile: city.GetCenterTile());
        foreach (var unique in attacker.GetMatchingUniques(UniqueType.CaptureCityPlunder, gameContext, true))
        {
            var resource = attacker.GetCivInfo().GameInfo.Ruleset.GetGameResource(unique.Params[2]);
            if (resource == null) continue;
            attackerCiv.AddGameResource(
                resource,
                unchecked(unique.Params[0].ToInt() * KotlinMath.FloatToInt(city.CityStats.CurrentCityStats[Enum.Parse<Stat>(unique.Params[1])])));
        }

        if (attackerCiv.IsBarbarian || attackerCiv.IsOneCityChallenger())
        {
            city.DestroyCity(true);
            return;
        }

        var clientState = attackerCiv.GameInfo.ClientState;
        if (city.IsOriginalCapital && city.FoundingCivObject == attackerCiv)
        {
            // retaking old capital
            city.PuppetCity(attackerCiv);
            //Although in Civ5 Venice is unable to re-annex their capital, that seems a bit silly. No check for May not annex cities here.
            city.AnnexCity();
        }
        else if (attackerCiv.IsHuman() && attackerCiv.GameInfo.GetPlayerSettings(attackerCiv.CivId).HumanConquestPrompt && !clientState.IsFullAutoPlayAI)
        {
            // we're not taking our former capital
            attackerCiv.AddPopupAlert(new PopupAlert(AlertType.CityConquered, city.Id));
        }
        else AutomateCityConquer(attackerCiv, city);

        if (attackerCiv.IsActive())
            clientState.AddCompletedTutorialTask(attackerCiv.CivId, "Conquer a city");

        foreach (var unique in attackerCiv.GetTriggeredUniques(UniqueType.TriggerUponConqueringCity, gameContext)
                     .Concat(attacker.Unit.GetTriggeredUniques(UniqueType.TriggerUponConqueringCity, gameContext))) // lazy, like the Kotlin sequences
            UniqueTriggerActivation.TriggerUnique(unique, attacker.Unit);
    }

    /// <summary>Handle decision-making after city conquest, namely whether the AI should liberate, puppet, or raze a city</summary>
    private static void AutomateCityConquer(CivilizationObject civInfo, CityObject city)
    {
        if (!city.HasDiplomaticMarriage())
        {
            var foundingCiv = city.FoundingCivObject.NotNull();
            int valueAlliance = civInfo.GameInfo.ClientState.NextTurnAutomation?.ValueCityStateAlliance(civInfo, foundingCiv) ?? 0;
            if (civInfo.GetHappiness() < 0)
                valueAlliance = unchecked(valueAlliance - civInfo.GetHappiness()); // put extra weight on liberating if unhappy
            if (foundingCiv.IsCityState && city.Civ != civInfo && foundingCiv != civInfo
                && !civInfo.IsAtWarWith(foundingCiv)
                && valueAlliance > 0)
            {
                city.LiberateCity(civInfo);
                return;
            }
        }

        city.PuppetCity(civInfo);
        if ((city.Population.Population < 4 || civInfo.IsCityState)
            && city.FoundingCivObject != civInfo && city.CanBeDestroyed(justCaptured: true))
        {
            // raze if attacker is a city state
            if (!civInfo.HasUnique(UniqueType.MayNotAnnexCities)) city.AnnexCity();
            city.IsBeingRazed = true;
        }
    }

    public static ICombatant? GetMapCombatantOfTile(TileClass tile)
    {
        if (tile.IsCityCenter()) return new CityCombatant((CityObject)tile.GetCity().NotNull());
        if (tile.MilitaryUnit != null) return new MapUnitCombatant(tile.MilitaryUnit);
        if (tile.CivilianUnit != null) return new MapUnitCombatant(tile.CivilianUnit);
        return null;
    }

    public static void DestroyIfDefeated(CivilizationObject attackedCiv, CivilizationObject attacker, HexCoord? notificationLocation = null)
    {
        if (attackedCiv.IsDefeated())
        {
            if (attackedCiv.IsCityState)
                attackedCiv.CityStateFunctions.CityStateDestroyed(attacker);
            attackedCiv.Destroy(notificationLocation);
            attacker.AddPopupAlert(new PopupAlert(AlertType.Defeated, attackedCiv.CivId));
        }
    }

    /// <summary>The tile a melee-attacked defender retreats to: first a tile not adjacent to the attacker, else one that is; null when it has none.</summary>
    private static TileClass? SelectRetreatTile(MapUnitCombatant attacker, MapUnitCombatant defender, GameRandom rng)
    {
        var fromTile = defender.GetTile();
        var attackerTile = attacker.GetTile();

        bool CanNotWithdrawTo(TileClass tile) // if the tile is what the defender can't withdraw to, this fun will return true
        {
            return !defender.Unit.Movement.CanMoveTo(tile)
                   || (((ICombatant)defender).IsLandUnit() && !tile.IsLand) // forbid retreat from land to sea - embarked already excluded
                   || (tile.IsCityCenter() && tile.GetOwner() != defender.GetCivInfo()); // forbid retreat into the city which doesn't belong to the defender
        }

        var firstCandidateTiles = fromTile.Neighbors.Where(it => it != attackerTile && !attackerTile.Neighbors.Contains(it))
            .Where(it => !CanNotWithdrawTo(it));
        var secondCandidateTiles = fromTile.Neighbors.Where(it => attackerTile.Neighbors.Contains(it))
            .Where(it => !CanNotWithdrawTo(it));
        if (firstCandidateTiles.Any()) return firstCandidateTiles.ToList().Random(rng);
        if (secondCandidateTiles.Any()) return secondCandidateTiles.ToList().Random(rng);
        return null;
    }

    /// <summary>
    /// BNW Heavy Charge (B023, bnw-spec 4.12; the exact Civ V rule is [K] and undecoded, deliberate deviation): a melee attacker with
    /// <see cref="UniqueType.HeavyCharge"/> that wounds a surviving military defender pushes it one tile back for free; a defender that
    /// cannot retreat takes <see cref="BattleConstants.HeavyChargeBlockedDamagePercent"/>% of the damage just dealt as extra damage.
    /// </summary>
    private static DamageDealt ApplyHeavyCharge(ICombatant attacker, ICombatant defender, TileClass attackedTile, DamageDealt dealt)
    {
        if (attacker is not MapUnitCombatant mapAttacker || defender is not MapUnitCombatant mapDefender) return dealt;
        if (!attacker.IsMelee() || attacker.IsDefeated() || defender.IsDefeated() || dealt.AttackerDealt <= 0) return dealt;
        if (mapDefender.Unit.IsCivilian()) return dealt;
        if (!mapAttacker.Unit.HasUnique(UniqueType.HeavyCharge, GameContext.ForCombatants(attacker, defender, attackedTile, CombatAction.Attack))) return dealt;

        var rng = GameContext.ForCombatants(attacker, defender, attackedTile, CombatAction.Attack).StateBasedRandom("Battle.heavyCharge");
        var toTile = mapDefender.Unit.IsEmbarked() || mapDefender.Unit.Cache.CannotMove || mapDefender.Unit.IsEscorting() || mapDefender.Unit.IsGuarding() ? null : SelectRetreatTile(mapAttacker, mapDefender, rng);
        if (toTile != null)
        {
            var origin = mapDefender.Unit.GetTile();
            mapDefender.Unit.RemoveFromTile();
            mapDefender.Unit.PutInTile(toTile);
            GameEventHooks.UnitMoved(mapDefender.Unit, origin, toTile);
            mapDefender.Unit.MostRecentMoveType = UnitMovementMemoryType.UnitWithdrew;
            string text = $"[{mapDefender.GetName()}] was pushed back by a [{mapAttacker.GetName()}]";
            var locations = LocationAction.Of(toTile.Position, mapAttacker.GetTile().Position).ToList();
            defender.GetCivInfo().AddNotification(text, locations, NotificationCategory.War, mapDefender.GetName(), NotificationIcon.War, mapAttacker.GetName());
            attacker.GetCivInfo().AddNotification(text, locations, NotificationCategory.War, mapDefender.GetName(), NotificationIcon.War, mapAttacker.GetName());
            return dealt;
        }

        int extra = dealt.AttackerDealt * BattleConstants.HeavyChargeBlockedDamagePercent / 100;
        if (extra <= 0) return dealt;
        int before = defender.GetHealth();
        defender.TakeDamage(extra);
        return dealt + new DamageDealt(before - defender.GetHealth(), 0);
    }

    /// <summary>
    /// BNW post-combat promotions (B023, bnw-spec 4.12, Kris Swordsman's Mystic Blade): a surviving unit with
    /// <see cref="UniqueType.PostCombatPromotion"/> uniques gains one of the listed promotions it lacks, drawn from the state-based RNG.
    /// With <see cref="UniqueType.PostCombatPromotionsExclusive"/> it gets nothing once it already holds one of them.
    /// </summary>
    private static void GivePostCombatPromotion(ICombatant ourCombatant, ICombatant enemy, TileClass attackedTile, CombatAction action)
    {
        if (ourCombatant is not MapUnitCombatant mapUnit || mapUnit.IsDefeated() || mapUnit.Unit.IsDestroyed) return;
        var unit = mapUnit.Unit;
        var context = GameContext.ForCombatants(ourCombatant, enemy, attackedTile, action);
        var candidates = unit.GetMatchingUniques(UniqueType.PostCombatPromotion, context).Select(it => it.Params[0]).Distinct().ToList();
        if (candidates.Count == 0) return;
        if (unit.HasUnique(UniqueType.PostCombatPromotionsExclusive, context) && candidates.Any(it => unit.Promotions.Promotions.Contains(it))) return;
        var available = candidates.Where(it => !unit.Promotions.Promotions.Contains(it)
            && unit.Civ.GameInfo.Ruleset.UnitPromotions.ContainsKey(it)).ToList();
        if (available.Count == 0) return;
        unit.Promotions.AddPromotion(available.Random(context.StateBasedRandom("Battle.postCombatPromotion")), isFree: true);
    }

    private static bool HasWithdrawnFromMelee(ICombatant attacker, ICombatant defender, TileClass attackedTile)
    {
        return attacker is MapUnitCombatant mapAttacker && attacker.IsMelee() && defender is MapUnitCombatant mapDefender
               && mapDefender.Unit.HasUnique(UniqueType.WithdrawsBeforeMeleeCombat, new GameContext(
                   civInfo: mapDefender.GetCivInfo(),
                   ourCombatant: mapDefender,
                   theirCombatant: mapAttacker,
                   tile: attackedTile))
               && DoWithdrawFromMeleeAbility(mapAttacker, mapDefender);
    }

    private static bool DoWithdrawFromMeleeAbility(MapUnitCombatant attacker, MapUnitCombatant defender)
    {
        if (defender.Unit.IsEmbarked()) return false;
        if (defender.Unit.Cache.CannotMove) return false;
        if (defender.Unit.IsEscorting()) return false; // running away and leaving the escorted unit defeats the purpose of escorting
        if (defender.Unit.IsGuarding()) return false; // guarding this post and will fight to the death!

        // Promotions have no effect as per what I could find in available documentation
        var fromTile = defender.GetTile();
        var attackContext = GameContext.ForCombatants(attacker, defender, fromTile, CombatAction.Defend);
        var rng = attackContext.StateBasedRandom("Battle.doWithdrawFromMeleeAbility");

        var toTile = SelectRetreatTile(attacker, defender, rng);
        if (toTile == null) return false;
        // Withdraw success: Do it - move defender to toTile for no cost
        // NOT defender.unit.movement.moveToTile(toTile) - we want a free teleport
        var withdrawOrigin = defender.Unit.GetTile();
        defender.Unit.RemoveFromTile();
        defender.Unit.PutInTile(toTile);
        GameEventHooks.UnitMoved(defender.Unit, withdrawOrigin, toTile);
        defender.Unit.MostRecentMoveType = UnitMovementMemoryType.UnitWithdrew;
        // and count 1 attack for attacker but leave it in place
        ReduceAttackerMovementPointsAndAttacks(attacker, defender, fromTile);

        string attackerName = attacker.GetName();
        string defenderName = defender.GetName();
        string notificationString = $"[{defenderName}] withdrew from a [{attackerName}]";
        var locations = LocationAction.Of(toTile.Position, attacker.GetTile().Position).ToList();
        defender.GetCivInfo().AddNotification(notificationString, locations, NotificationCategory.War, defenderName, NotificationIcon.War, attackerName);
        attacker.GetCivInfo().AddNotification(notificationString, locations, NotificationCategory.War, defenderName, NotificationIcon.War, attackerName);
        return true;
    }

    private static void DoDestroyImprovementsAbility(MapUnitCombatant attacker, TileClass attackedTile, ICombatant defender)
    {
        var currentTileImprovement = attackedTile.TileImprovement;
        if (currentTileImprovement == null) return;

        var conditionalState = new GameContext(attacker.GetCivInfo(), ourCombatant: attacker, theirCombatant: defender, combatAction: CombatAction.Attack, attackedTile: attackedTile);
        if (!((IHasUniques)currentTileImprovement).HasUnique(UniqueType.Unpillagable) && attacker.HasUnique(UniqueType.DestroysImprovementUponAttack, conditionalState))
        {
            attackedTile.RemoveImprovement();
            defender.GetCivInfo().AddNotification(
                $"An enemy [{attacker.Unit.BaseUnit.Name}] has destroyed our tile improvement [{currentTileImprovement.Name}]",
                LocationAction.Of(attackedTile.Position, attacker.GetTile().Position),
                NotificationCategory.War, attacker.Unit.BaseUnit.Name,
                NotificationIcon.War);
        }
    }

    private static DamageDealt TryExtraRangedAttack(ICombatant attacker, ICombatant defender)
    {
        if (attacker is not MapUnitCombatant mapAttacker) return DamageDealt.None;
        if (defender is not MapUnitCombatant) return DamageDealt.None;
        if (defender.IsCivilian()) return DamageDealt.None;

        var damageDealt = DamageDealt.None;
        foreach (var fakeAttacker in GetExtraRangedAttackFakeUnits(mapAttacker))
        {
            damageDealt += TakeDamage(fakeAttacker, defender);
        }
        return damageDealt;
    }

    /// <summary>
    /// One <see cref="FakeUnitForExtraRangedAttack"/> per <see cref="UniqueType.ExtraRangedAttack"/> unique on <paramref name="attacker"/> - shared between
    /// actually performing the extra attack (<c>tryExtraRangedAttack</c>) and previewing its damage (<see cref="BattleDamage.GetExtraRangedAttackBonusDamage"/>).
    /// </summary>
    internal static List<ICombatant> GetExtraRangedAttackFakeUnits(MapUnitCombatant attacker) =>
        attacker.Unit.GetMatchingUniques(UniqueType.ExtraRangedAttack).Select(unique =>
        {
            int baseRangedStrengthForExtraAttack = KotlinMath.FloatToInt(attacker.Unit.BaseUnit.Strength *
                unique.Params[0].ToFloat() / 100);
            return (ICombatant)new FakeUnitForExtraRangedAttack(attacker, baseRangedStrengthForExtraAttack);
        }).ToList();

    /// <summary>
    /// This has all the properties and methods of <see cref="MapUnitCombatant"/> (Kotlin: via delegation)
    /// **except** for <see cref="IsRanged"/> and <see cref="GetAttackingStrength"/>
    /// </summary>
    internal sealed class FakeUnitForExtraRangedAttack(MapUnitCombatant mapUnitCombatant, int baseRangedStrength) : ICombatant
    {
        public MapUnitCombatant MapUnitCombatant { get; } = mapUnitCombatant;
        public int BaseRangedStrength { get; } = baseRangedStrength;

        public int GetAttackingStrength(ICombatant? defender = null)
        {
            var state = GameContext.ForCombatants(this, defender, GetTile(), CombatAction.Attack);
            int extraStrength = 0;
            MapUnitCombatant.Unit.ForEachMatchingUnique(UniqueType.StrengthAmount, state, it => extraStrength = unchecked(extraStrength + it.Params[0].ToInt()));
            return unchecked(BaseRangedStrength + extraStrength); // Is always ranged
        }

        public bool IsRanged() => true;

        // Kotlin `by mapUnitCombatant` forwards the interface's default methods too (C# does not), so they are spelled out.
        public bool IsMelee() => MapUnitCombatant.IsMelee();
        public bool IsAirUnit() => ((ICombatant)MapUnitCombatant).IsAirUnit();
        public bool IsWaterUnit() => ((ICombatant)MapUnitCombatant).IsWaterUnit();
        public bool IsLandUnit() => ((ICombatant)MapUnitCombatant).IsLandUnit();
        public bool IsCity() => ((ICombatant)MapUnitCombatant).IsCity();
        public bool IsCivilian() => MapUnitCombatant.IsCivilian();
        public string GetNotificationDisplay(string leadingText = "") => MapUnitCombatant.GetNotificationDisplay(leadingText);

        public string GetName() => MapUnitCombatant.GetName();
        public int GetHealth() => MapUnitCombatant.GetHealth();
        public int GetMaxHealth() => MapUnitCombatant.GetMaxHealth();
        public UnitType GetUnitType() => MapUnitCombatant.GetUnitType();
        public int GetDefendingStrength(ICombatant? attacker = null) => MapUnitCombatant.GetDefendingStrength(attacker);
        public void TakeDamage(int damage) => MapUnitCombatant.TakeDamage(damage);
        public bool IsDefeated() => MapUnitCombatant.IsDefeated();
        public CivilizationObject GetCivInfo() => MapUnitCombatant.GetCivInfo();
        public TileClass GetTile() => MapUnitCombatant.GetTile();
        public bool IsVisibleTo(CivilizationObject to) => MapUnitCombatant.IsVisibleTo(to);
        public bool CanAttack() => MapUnitCombatant.CanAttack();
        public bool MatchesFilter(string filter, bool multiFilter = true) => MapUnitCombatant.MatchesFilter(filter, multiFilter);
        public UncivSound GetAttackSound() => MapUnitCombatant.GetAttackSound();
        public IEnumerable<UniqueObject> GetTriggeredUniques(UniqueType trigger, GameContext gameContext, Func<UniqueObject, bool>? triggerFilter = null) =>
            MapUnitCombatant.GetTriggeredUniques(trigger, gameContext, triggerFilter);

        public override string ToString() => "Extra ranged attack of " + MapUnitCombatant;
    }
}
