using Unciv.Logic.Automation.Civilization;
using Unciv.Logic.Battle;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Logic.Map.MapUnits;
using Unciv.Models;
using Unciv.Models.Ruleset.Unique;
using Unciv.Utils;
using Unciv.View;
using BattleClass = Unciv.Logic.Battle.Battle;
using CityObject = Unciv.Logic.City.City;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Logic.Automation.Unit;

// Ported from core/src/com/unciv/logic/automation/unit/UnitAutomation.kt (T090/T156).
// - Calls to CivilianUnitAutomation (T091) and NextTurnAutomation (T094) are TODO(T091)/TODO(T094) seams naming their owner.
// - timeThis profiling dropped (T027 policy).
public static partial class UnitAutomation
{
    private const int CloseEnemyTilesAwayLimit = 5;
    private const float CloseEnemyTurnsAwayLimit = 3f;

    public static void AutomateUnitMoves(MapUnitView unitView, AutomationCache? cache = null)
    {
        cache ??= new AutomationCache();
        var unit = unitView.Unwrap();
        if (unit.Civ.IsBarbarian)
            throw new InvalidOperationException("Barbarians is not allowed here.");

        // Might die next turn - move!
        if (unit.GetDamageFromTerrain() > 0 && TryHealUnit(unitView, cache)) return;

        if (unitView.IsCivilian())
        {
            CivilianUnitAutomationSeams.AutomateCivilianUnit(unitView, GetDangerousTiles(unitView), cache);
            return;
        }

        var isHumanAndNotAutoplaying =
            unit.Civ.IsHuman() && unit.Civ.GameInfo.ClientState.AutoPlay?.IsAutoPlaying() != true;
        // AI promotes units via NextTurnAutomation.automateUnits
        if (isHumanAndNotAutoplaying) NextTurnAutomationSeams.ApplyPromotions(unitView);
        // AI upgrades units via UseGoldAutomation in NextTurnAutomation
        if (isHumanAndNotAutoplaying && TryUpgradeUnit(unitView)) return;

        // This allows for military units with certain civilian abilities to behave as civilians in peace and soldiers in war
        if ((unitView.HasUnique(UniqueType.BuildImprovements) || unitView.HasUnique(UniqueType.FoundCity) ||
                    unitView.HasUnique(UniqueType.FoundPuppetCity) ||
                    unitView.HasUnique(UniqueType.ReligiousUnit) || unitView.HasUnique(UniqueType.CreateWaterImprovements))
            && !unit.Civ.IsAtWar())
        {
            CivilianUnitAutomationSeams.AutomateCivilianUnit(unitView, GetDangerousTiles(unitView), cache);
            return;
        }

        // Note that not all nukes have to be air units
        if (unitView.IsNuclearWeapon())
        {
            AirUnitAutomation.AutomateNukes(unitView);
            return;
        }

        if (unitView.IsAirUnit())
        {
            if (unit.CanIntercept())
            {
                AirUnitAutomation.AutomateFighter(unitView, cache);
                return;
            }

            if (unitView.HasUnique(UniqueType.SelfDestructs))
            {
                AirUnitAutomation.AutomateMissile(unitView);
                return;
            }

            AirUnitAutomation.AutomateBomber(unitView);
            return;
        }

        // Accompany settlers
        if (TryAccompanySettlerOrGreatPerson(unitView)) return;

        if (TryGoToRuin(unitView) && !unitView.HasMovement()) return;

        if (unitView.UnitHealth < 50 && (TryRetreat(unitView, cache) || TryHealUnit(unitView, cache))) return; // do nothing but heal

        // If there are no enemies nearby and we can heal here, wait until we are at full health
        if (unitView.UnitHealth < 100 && CanUnitHealInTurnsOnCurrentTile(unitView, cache, 2, 3)) return;

        if (TryHeadTowardsOurSiegedCity(unitView)) return;

        // if a embarked melee unit can land and attack next turn, do not attack from water.
        if (BattleHelper.TryDisembarkUnitToAttackPosition(unit)) return;

        // if there is an attackable unit in the vicinity, attack!
        if (TryAttacking(unitView, cache)) return;

        if (TryTakeBackCapturedCity(unitView)) return;

        // Focus all units without a specific target on the enemy city closest to one of our cities
        if (HeadTowardsEnemyCityAutomation.TryHeadTowardsEnemyCity(unitView)) return;

        if (TryHeadTowardsEncampment(unitView)) return;

        if (TryGarrisoningLandUnit(unitView)) return;

        if (unitView.UnitHealth < 80 && TryHealUnit(unitView, cache)) return;

        // move towards the closest reasonably attackable enemy unit within 3 turns of movement (and 5 tiles range)
        if (TryAdvanceTowardsCloseEnemy(unitView)) return;

        if (unitView.UnitHealth < 100 && TryHealUnit(unitView, cache)) return;

        if (TryPrepare(unitView)) return;

        // else, try to go to unreached tiles
        if (TryExplore(unitView, cache)) return;

        if (TryFogBust(unitView)) return;

        // Idle CS units should wander so they don't obstruct players so much
        if (unitView.Civ().IsCityState)
            Wander(unitView, stayInTerritory: true);
    }

    internal static bool TryUpgradeUnit(MapUnitView unitView)
    {
        var civ = unitView.Civ();

        var upgrade = unitView.GetPossibleUpgrades()
            .Where(it => it.Availability == UpgradeAvailability.Available // for resource reasons, usually
                && !(civ.IsBarbarian() && ((IHasUniques)it.UpgradedUnit).HasUnique(UniqueType.CannotBeBarbarian)))
            .MinBy(it => it.UpgradedUnit.Cost);
        if (upgrade == null) return false;

        // The upgrade requires new resource types, so check if we are willing to invest them
        if (upgrade.NewResourceRequirements.Any(it => it.Value > 0)
            && !Automation.AllowSpendingResource(civ, upgrade.UpgradedUnit)) return false;

        return unitView.TryUpgradeTo(upgrade.UpgradedUnit);
    }

    private static bool TryAttacking(MapUnitView unitView, AutomationCache cache)
    {
        int attackCount = unitView.MaxAttacksPerTurn() - unitView.AttacksThisTurn;
        for (int i = 0; i < attackCount; i++)
        {
            if (BattleHelper.TryAttackNearbyEnemy(unitView)) return true;
            // Cavalry style tactic, attack and then retreat
            if (unitView.UnitHealth < 50 && TryRetreat(unitView, cache)) return true;
        }
        return false;
    }

    private static bool TryRetreat(MapUnitView unitView, AutomationCache cache)
    {
        // Precondition: This must be a military unit
        if (unitView.IsCivilian()) return false;
        if (unitView.IsAirUnit()) return false;
        var civ = unitView.Civ();
        var unitTile = unitView.GetTile();
        // Better to do a more healing oriented move then
        if (ThreatManager.GetDistanceToClosestEnemyUnit(civ, cache, unitTile, 4, takeLargerValues: true) > 3) return false;

        var unitDistanceToTiles = unitView.GetDistanceToTiles();
        var closestCity = civ.Cities().MinBy(it => it.GetCenterTile().AerialDistanceTo(unitTile));
        if (closestCity != null && closestCity.GetCenterTile().AerialDistanceTo(unitTile) >= 20)
            closestCity = null;

        // Finding the distance to the closest enemy is expensive, so lets sort the tiles using a cheaper function
        var sortedTilesToRetreatTo = closestCity != null
            ? unitDistanceToTiles.Keys.OrderBy(it => it.AerialDistanceTo(closestCity.GetCenterTile()))
            : (IEnumerable<TileView>)unitDistanceToTiles.Keys.OrderByDescending(it => ThreatManager.GetDistanceToClosestEnemyUnit(civ, cache, it, 3, takeLargerValues: false));

        int ourDistanceToClosestEnemy = ThreatManager.GetDistanceToClosestEnemyUnit(civ, cache, unitTile, 4, takeLargerValues: false);
        // Lets check all tiles and swap with the first one
        foreach (var retreatTile in sortedTilesToRetreatTo)
        {
            int tileDistanceToClosestEnemy = ThreatManager.GetDistanceToClosestEnemyUnit(civ, cache, retreatTile, 4, takeLargerValues: false);
            if (ourDistanceToClosestEnemy >= tileDistanceToClosestEnemy) continue;

            var otherUnit = retreatTile.MilitaryUnit;
            if (otherUnit == null)
            {
                // See if we can retreat to the tile
                if (!unitView.CanMoveTo(retreatTile)) continue;
                unitView.TryMoveToTile(retreatTile);
                return true;
            }
            else if (otherUnit.Civ().Equals(civ))
            {
                // The tile is taken, lets see if we want to swap retreat to it
                if (otherUnit.UnitHealth <= 80) continue;
                if (otherUnit.GetBaseUnit().IsRanged())
                {
                    // Don't swap ranged units closer than they have to be
                    int range = otherUnit.GetBaseUnit().Range;
                    if (ourDistanceToClosestEnemy < range)
                        continue;
                }
                if (unitView.CanSwapTo(retreatTile))
                {
                    unitView.TryHeadTowards(retreatTile); // we need to move through the intermediate tiles
                    // if nothing changed
                    if (unitView.GetTile().GetVisibleNeighbors().Contains(otherUnit.GetTile()) && unitView.CanSwapTo(retreatTile))
                    {
                        unitView.TrySwapMoveToTile(retreatTile);
                    }
                    return true;
                }
            }
        }
        return false;
    }

    private static bool TryHealUnit(MapUnitView unitView, AutomationCache cache)
    {
        if (unitView.GetBaseUnit().IsRanged() && unitView.HasUnique(UniqueType.HealsEvenAfterAction))
            return false; // will heal anyway, and attacks don't hurt

        // Try pillage improvements until healed
        while (TryPillageImprovement(unitView, onlyPillageToHeal: true))
        {
            // If we are fully healed and can still do things, lets keep on going by returning false
            if (!unitView.HasMovement() || unitView.UnitHealth == 100) return !unitView.HasMovement();
        }

        var unitDistanceToTiles = unitView.GetDistanceToTiles();
        if (unitDistanceToTiles.Count == 0) return true; // can't move, so...

        var dangerousTiles = ThreatManager.GetDangerousTiles(cache, unitView, 3);

        // If the unit can heal on this tile in two turns, just heal here
        if (!dangerousTiles.Contains(unitView.GetTile())
            && CanUnitHealInTurnsOnCurrentTile(unitView, cache, 3)) return true;

        var currentUnitTile = unitView.GetTile();

        var viableTilesForHealing = unitDistanceToTiles.Keys
            .Where(it => !dangerousTiles.Contains(it) && unitView.CanMoveTo(it))
            .ToList();
        var tilesByHealingRate = viableTilesForHealing
            .GroupBy(it => unitView.HealingAmountForTile(it))
            .ToList();

        if (tilesByHealingRate.Count == 0 || tilesByHealingRate.All(it => it.Key == 0)) // We can't heal here at all! We're probably embarked
        {
            if (!unitView.IsAirUnit())
            {
                var reachableCityTile = unitView.Civ().Cities()
                    .Select(it => it.GetCenterTile())
                    .OrderBy(it => it.AerialDistanceTo(currentUnitTile))
                    .FirstOrDefault(it => unitView.CanReach(it));
                if (reachableCityTile != null) unitView.TryHeadTowards(reachableCityTile);
                else Wander(unitView);
                return true;
            }
            // Try to get closer to an empty city
            var emptyCities = unitView.Civ().Cities()
                .Select(it => it.GetCenterTile())
                .Where(it => unitView.CanMoveTo(it))
                .ToList();
            if (emptyCities.Count == 0) return false; // Nowhere to move to heal

            var nextTileToMove = unitDistanceToTiles.Keys
                .Where(it => unitView.CanMoveTo(it))
                .MinBy(tile => emptyCities.Min(city => city.AerialDistanceTo(tile)));
            if (nextTileToMove == null) return false;

            return unitView.TryMoveToTile(nextTileToMove);
        }

        var bestTilesForHealing = tilesByHealingRate.MaxBy(it => it.Key);
        if (bestTilesForHealing != null)
        {
            var bestTileForHealing = bestTilesForHealing.MaxBy(it => unitView.GetDefensiveBonusOn(it));
            if (bestTileForHealing != null)
            {
                int bestTileForHealingRank = unitView.HealingAmountForTile(bestTileForHealing);
                if (!currentUnitTile.Equals(bestTileForHealing)
                    && bestTileForHealingRank > unitView.HealingAmountForTile(currentUnitTile) - unitView.GetDamageFromTerrain(currentUnitTile))
                {
                    unitView.TryMoveToTile(bestTileForHealing);
                }
            }
        }

        unitView.TryFortifyIfCan();
        return true;
    }

    private static bool CanUnitHealInTurnsOnCurrentTile(MapUnitView unitView, AutomationCache cache, int turns, int noEnemyDistance = 3)
    {
        if (unitView.HasUnique(UniqueType.HealsEvenAfterAction)) return false; // We can keep on moving
        var unitTile = unitView.GetTile();
        // Check if we are not in a safe city and there is an enemy nearby this isn't a good tile to heal on
        if (!(unitTile.IsCityCenter() && unitTile.GetCity() is { } city && city.GetHealth() > 50)
            && ThreatManager.GetDistanceToClosestEnemyUnit(unitView.Civ(), cache, unitView.GetTile(), noEnemyDistance) <= noEnemyDistance) return false;

        // Round up, otherwise e.g. 99 health over 2 turns would give 0 required healing per turn
        int healthRequiredPerTurn = KotlinMath.FloatToInt(MathF.Ceiling((100 - unitView.UnitHealth) / (float)turns));
        return healthRequiredPerTurn <= unitView.HealingAmountForTile(unitTile);
    }

    private static HashSet<TileView> GetDangerousTiles(MapUnitView unitView)
    {
        var civ = unitView.Civ();
        var nearbyEnemyUnits = unitView.GetTile().GetExploredTilesInDistance(3)
            .SelectMany(tile => tile.GetVisibleUnits().Where(it => civ.IsAtWarWith(it.Civ())));

        var tilesInRangeOfAttack = nearbyEnemyUnits
            .SelectMany(it => it.GetTile().GetExploredTilesInDistance((it.GetMaxMovement() - 1) + it.GetRange()));

        var tilesWithinBombardmentRange = unitView.GetTile().GetExploredTilesInDistance(3)
            .Where(it => it.IsCityCenter())
            .Select(it => it.GetCity() is { } c && c.OwningCiv().IsAtWarWith(civ)
                ? it.GetExploredTilesInDistance(c.GetBombardRange())
                : Enumerable.Empty<TileView>())
            .SelectMany(it => it);

        var tilesWithTerrainDamage = unitView.GetTile().GetExploredTilesInDistance(3)
            .Where(it => unitView.GetDamageFromTerrain(it) > 0);

        return tilesInRangeOfAttack.Concat(tilesWithinBombardmentRange).Concat(tilesWithTerrainDamage).ToHashSet();
    }

    public static bool TryPillageImprovement(MapUnitView unitView, bool onlyPillageToHeal = false)
    {
        if (unitView.IsCivilian()) return false;
        var civ = unitView.Civ();
        var tilesThatCanWalkToAndThenPillage = unitView.GetDistanceToTiles()
            .Where(it => it.Value < unitView.CurrentMovement)
            .Select(it => it.Key)
            .Where(it => unitView.CanMoveTo(it) && unitView.CanPillage(it)
                    && (it.CanPillageTileImprovement()
                    || (!onlyPillageToHeal && it.CanPillageRoad() && it.GetRoadOwner() is { } roadOwner && civ.IsAtWarWith(roadOwner))))
            .ToList();

        if (tilesThatCanWalkToAndThenPillage.Count == 0) return false;
        var tileToPillage = tilesThatCanWalkToAndThenPillage.MaxBy(it => unitView.GetDefensiveBonusOn(it, includeImprovementBonus: false));
        if (tileToPillage == null) return false;

        if (!unitView.GetTile().Equals(tileToPillage))
            unitView.TryMoveToTile(tileToPillage);

        if (!unitView.GetTile().Equals(tileToPillage)) return false;

        return unitView.TryInvokeUnitAction(UnitActionType.Pillage);
    }

    private static bool TryAdvanceTowardsCloseEnemy(MapUnitView unitView)
    {
        var unit = unitView.Unwrap();
        var unitDistanceToTiles = unit.Movement.GetMovementToTilesAtPosition(
            unit.GetTile().Position,
            unit.GetMaxMovement() * CloseEnemyTurnsAwayLimit
        );
        var closeEnemy = TargetHelper.GetAttackableEnemies(
                unit,
                unitDistanceToTiles,
                tilesToCheck: unit.GetTile().GetTilesInDistance(CloseEnemyTilesAwayLimit).ToList())
            .Where(it => !(unitView.GetBaseUnit().IsRanged() && it.TileToAttack.IsCityCenter() && it.TileToAttack.GetCity()?.Health == 1))
            .Where(it => unit.GetDamageFromTerrain(it.TileToAttackFrom) <= 0)
            .Where(it =>
            {
                var defender = BattleClass.GetMapCombatantOfTile(it.TileToAttack);
                return defender != null
                    && BattleDamage.CalculateDamageToAttacker(new MapUnitCombatant(unit), defender) < unitView.UnitHealth;
            })
            .MinBy(it => it.TileToAttack.AerialDistanceTo(unit.GetTile()));

        if (closeEnemy != null)
        {
            unit.Movement.HeadTowards(closeEnemy.TileToAttackFrom);
            return true;
        }
        return false;
    }

    private static bool TryPrepare(MapUnitView unitView)
    {
        var unit = unitView.Unwrap();
        var civInfo = unit.Civ;

        bool HasPreparationFlag(CivilizationObject targetCiv)
        {
            var diploManager = civInfo.GetDiplomacyManager(targetCiv);
            if (diploManager == null) return false;
            if (diploManager.HasFlag(DiplomacyFlags.Denunciation)
                    || diploManager.OtherCivDiplomacy().HasFlag(DiplomacyFlags.Denunciation)) return true;
            if (diploManager.HasFlag(DiplomacyFlags.WaryOf) && diploManager.GetFlag(DiplomacyFlags.WaryOf) < 0) return true;
            return false;
        }

        var hostileCivs = civInfo.GetKnownCivs().Where(it => it.IsAtWarWith(civInfo) || HasPreparationFlag(it)).ToHashSet();
        var closeCities = ThreatManager.GetNeighboringCitiesOfOtherCivs(unitView.Civ())
            .Where(it => hostileCivs.Contains(it.Item2.OwningCiv().GetCiv()))
            .ToList();
        if (closeCities.Count == 0) return false;

        int closestDistance = closeCities.Min(it => it.Item1.GetCenterTile().AerialDistanceTo(it.Item2.GetCenterTile()));
        var citiesToDefend = closeCities
            .Where(it => it.Item1.GetCenterTile().AerialDistanceTo(it.Item2.GetCenterTile()) <= closestDistance + 2)
            .Select(it => it.Item1)
            .Distinct()
            .OrderBy(it => unitView.GetTile().AerialDistanceTo(it.GetCenterTile()));

        // Move to the closest city with a tile we can enter nearby
        foreach (var city in citiesToDefend)
        {
            if (unitView.GetTile().AerialDistanceTo(city.GetCenterTile()) <= 2) return true;
            var tileToMoveTo = city.GetCenterTile().GetExploredTilesInDistance(2)
                .FirstOrDefault(it => unitView.CanMoveTo(it) && unitView.CanReach(it));
            if (tileToMoveTo == null) continue;
            unitView.TryHeadTowards(tileToMoveTo);
            return true;
        }
        return false;
    }

    private static bool TryAccompanySettlerOrGreatPerson(MapUnitView unitView)
    {
        var distanceToTiles = unitView.GetDistanceToTiles();
        var settlerOrGreatPersonToAccompany = unitView.Civ().GetUnits()
            .FirstOrDefault(it =>
            {
                var tile = it.GetTile();
                return it.IsCivilian()
                    && (it.HasUnique(UniqueType.FoundCity) || it.GetMatchingUniques(UniqueType.GreatPerson).Any())
                    && !it.HasUnique(UniqueType.StrengthBonusInRadius)
                    && (tile.Equals(unitView.GetTile()) || (tile.MilitaryUnit == null && unitView.CanMoveTo(tile)))
                    && distanceToTiles.ContainsKey(tile);
            });
        if (settlerOrGreatPersonToAccompany == null) return false;

        unitView.TryHeadTowards(settlerOrGreatPersonToAccompany.GetTile());
        return true;
    }

    private static bool TryHeadTowardsOurSiegedCity(MapUnitView unitView)
    {
        var siegedCities = unitView.Civ().Cities()
            .Where(it => it.GetHealth() < it.GetMaxHealth())
            .ToList();

        if (siegedCities.Any(it => it.GetCenterTile().AerialDistanceTo(unitView.GetTile()) <= 2))
            return false;

        var reachableTileNearSiegedCity = siegedCities
            .SelectMany(it => it.GetCenterTile().GetExploredTilesAtDistance(2))
            .OrderBy(it => it.AerialDistanceTo(unitView.GetTile()))
            .FirstOrDefault(it => unitView.CanMoveTo(it) && unitView.CanReach(it)
                && unitView.GetDamageFromTerrain(it) <= 0);

        if (reachableTileNearSiegedCity != null)
        {
            unitView.TryHeadTowards(reachableTileNearSiegedCity);
        }
        return !unitView.HasMovement();
    }

    public static bool TryEnterOwnClosestCity(MapUnitView unitView)
    {
        var closestCity = unitView.Civ().Cities()
            .OrderBy(it => it.GetCenterTile().AerialDistanceTo(unitView.GetTile()))
            .FirstOrDefault(it => unitView.CanReach(it.GetCenterTile()));
        if (closestCity == null) return false;

        unitView.TryHeadTowards(closestCity.GetCenterTile());
        return true;
    }

    public static bool TryBombardEnemy(CityObject city)
    {
        if (!city.CanBombard()) return false;
        var enemy = ChooseBombardTarget(city);
        if (enemy == null) return false;
        BattleClass.Attack(new CityCombatant(city), enemy);
        return true;
    }

    private static ICombatant? ChooseBombardTarget(CityObject city)
    {
        var targets = TargetHelper.GetBombardableTiles(city)
            .Select(it => BattleClass.GetMapCombatantOfTile(it))
            .OfType<ICombatant>()
            .Where(it => !it.IsCivilian())
            .ToList();
        if (targets.Count == 0) return null;
        var cityCombatant = new CityCombatant(city);
        return targets.MaxBy(it => BattleDamage.CalculateDamageToDefender(cityCombatant, it));
    }

    private static bool TryTakeBackCapturedCity(MapUnitView unitView)
    {
        var unit = unitView.Unwrap();
        var capturedCities = unit.Civ.GetKnownCivs()
            .SelectMany(it => it.Cities)
            .Where(it =>
                unit.Civ.IsAtWarWith(it.Civ)
                && ReferenceEquals(unit.Civ, it.FoundingCivObject)
                && it.IsInResistance()
                && it.Health < it.GetMaxHealth());

        if (unitView.GetBaseUnit().IsRanged())
            capturedCities = capturedCities.Where(it => it.Health != 1);

        var closestReachableCapturedCity = capturedCities
            .Select(it => it.GetCenterTile())
            .OrderBy(it => it.AerialDistanceTo(unit.CurrentTile))
            .FirstOrDefault(it => unit.Movement.CanReach(it));

        if (closestReachableCapturedCity != null)
        {
            return HeadTowardsEnemyCityAutomation.HeadTowardsEnemyCity(
                unitView,
                closestReachableCapturedCity,
                unit.Movement.GetShortestPath(closestReachableCapturedCity)
            );
        }
        return false;
    }

    private static bool TryGarrisoningLandUnit(MapUnitView unitView)
    {
        if (unitView.GetBaseUnit().IsWaterUnit) return false;
        var citiesWithoutGarrison = unitView.Civ().Cities().Where(it =>
        {
            var centerTile = it.GetCenterTile();
            return centerTile.MilitaryUnit == null && unitView.CanMoveTo(centerTile);
        }).ToList();

        var unitTile = unitView.GetTile();
        IEnumerable<CityView> citiesToTry;
        if (!unitView.Civ().IsAtWar())
        {
            if (unitTile.IsCityCenter()) return true;
            citiesToTry = citiesWithoutGarrison;
        }
        else
        {
            if (unitTile.IsCityCenter() && unitTile.GetCity() is { } currentCity && IsCityThatNeedsDefendingInWartime(currentCity))
                return true;
            var citiesWithoutGarrisonThatNeedDefending = citiesWithoutGarrison
                .Where(IsCityThatNeedsDefendingInWartime)
                .ToList();
            citiesToTry = citiesWithoutGarrisonThatNeedDefending.Count != 0
                ? citiesWithoutGarrisonThatNeedDefending
                : citiesWithoutGarrison;
        }

        var closestReachableCityNeedsDefending = citiesToTry
            .OrderBy(it => it.GetCenterTile().AerialDistanceTo(unitTile))
            .FirstOrDefault(it => unitView.CanReach(it.GetCenterTile()));
        if (closestReachableCityNeedsDefending == null) return false;

        unitView.TryHeadTowards(closestReachableCityNeedsDefending.GetCenterTile());
        return true;
    }

    private static bool IsCityThatNeedsDefendingInWartime(ForeignCityView city)
    {
        if (city.GetHealth() < city.GetMaxHealth()) return true;
        foreach (var enemyCiv in city.OwningCiv().GetCivsAtWarWith())
        {
            foreach (var enemyCivCity in enemyCiv.Cities())
            {
                if (city.GetCenterTile().AerialDistanceTo(enemyCivCity.GetCenterTile()) <= 5) return true;
            }
        }
        return false;
    }
}
