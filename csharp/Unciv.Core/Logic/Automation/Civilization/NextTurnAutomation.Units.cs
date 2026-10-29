using Unciv.AI;
using Unciv.Logic.Automation.Unit;
using Unciv.Logic.Battle;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Ruleset.Unit;
using Unciv.Utils;
using Unciv.View;
using BattleClass = Unciv.Logic.Battle.Battle;
using CityObject = Unciv.Logic.City.City;
using MapUnitClass = Unciv.Logic.Map.MapUnits.MapUnit;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Logic.Automation.Civilization;

// NextTurnAutomation.kt (T094), unit-related members.
public static partial class NextTurnAutomation
{
    private static void AutomateUnits(CivView civView, AutomationCache cache)
    {
        var civInfo = civView.GetCiv();
        var isAtWar = civView.IsAtWar();
        // Instances present when the pass starts. addUnit/removeUnit replace UnitManager's list,
        // so units acquired later must not extend this pass. The instances stay mutable:
        // capturedBy changes owner on the same object, and an earlier action can do that to a
        // later one, so ownership and destruction are checked at each dispatch, not before the loop.
        var unitsAtStart = civView.GetUnits();
        List<MapUnitView> UnitsInPriorityOrder() => unitsAtStart.OrderBy(unit => GetUnitPriority(unit, isAtWar, cache)).ToList();

        var citiesRequiringManualPlacement = civInfo.GetKnownCivs().Where(it => it.IsAtWarWith(civInfo))
            .SelectMany(it => it.Cities)
            .Where(it =>
            {
                var count = 0;
                it.GetCenterTile().ForEachTileInDistance(4, tile =>
                {
                    if (tile.MilitaryUnit?.Civ == civInfo) count++;
                });
                return count > 4;
            })
            .ToList();

        foreach (var unit in UnitsInPriorityOrder())
        {
            if (!IsStillOurUnit(unit, civView)) continue;
            ApplyPromotions(unit);
        }
        if (Civ5AiDriver.IsActive(civInfo))
        {
            AutomateUnitsWithCiv5Ai(civView, cache, UnitsInPriorityOrder);
            return;
        }
        foreach (var unit in UnitsInPriorityOrder())
        {
            // settlers need to move before automateSettlerEscorting(),
            // move spaceship parts before that to make sure we're not blocking them
            if (!unit.HasUnique(UniqueType.SpaceshipPart) && !unit.HasUnique(UniqueType.FoundCity)) continue;
            if (!IsStillOurUnit(unit, civView)) continue;
            civInfo.GameInfo.ClientState.BeforeUnitMoves?.Invoke(unit);
            UnitAutomation.AutomateUnitMoves(unit, cache);
        }

        if (civView.Cities().Count != 0) AutomateSettlerEscorting(civView);

        foreach (var city in citiesRequiringManualPlacement) AutomateCityConquer(civView, city, cache);

        foreach (var unit in UnitsInPriorityOrder())
        {
            // spaceship parts and settlers have already moved
            if (unit.HasUnique(UniqueType.SpaceshipPart) || unit.HasUnique(UniqueType.FoundCity)) continue;
            if (!IsStillOurUnit(unit, civView)) continue;
            civInfo.GameInfo.ClientState.BeforeUnitMoves?.Invoke(unit);
            UnitAutomation.AutomateUnitMoves(unit, cache);
        }
    }

    /// <summary>
    /// T227: the unit pass of an AI civ under <c>GameParameters.UseCiv5Ai</c>. The tactical queue (attacks and city capture, ranked by the grand strategy's
    /// flavors) replaces <see cref="AutomateCityConquer"/>; the homeland queue sets the order in which settlers, spaceship parts, workers, garrisons and
    /// hurt units act; every unit then acts through <see cref="UnitAutomation.AutomateUnitMoves"/> as before, those not claimed by a homeland move last.
    /// </summary>
    private static void AutomateUnitsWithCiv5Ai(CivView civView, AutomationCache cache, Func<List<MapUnitView>> unitsInPriorityOrder)
    {
        var civInfo = civView.GetCiv();
        HashSet<int> marching;
        using (TurnProbe.Measure("AI.Operations")) marching = Civ5AiDriver.RunOperations(civInfo);   // T235: armies on their way are moved by their operation only
        using (TurnProbe.Measure("AI.Tactical")) Civ5AiDriver.RunTactical(civInfo);

        void Automate(MapUnitView unit)
        {
            if (!IsStillOurUnit(unit, civView)) return;
            civInfo.GameInfo.ClientState.BeforeUnitMoves?.Invoke(unit);
            using (TurnProbe.Measure("AI.AutomateUnitMoves")) UnitAutomation.AutomateUnitMoves(unit, cache);
        }

        var ordered = unitsInPriorityOrder().Where(it => !marching.Contains(it.Unwrap().Id)).ToList();
        var byUnit = ordered.ToDictionary(it => it.Unwrap());   // lookup only, never iterated
        HashSet<MapUnitClass> handled;
        using (TurnProbe.Measure("AI.Homeland")) handled = Civ5AiDriver.RunHomeland(civInfo, ordered.Select(it => it.Unwrap()).ToList(), unit => Automate(byUnit[unit]));
        if (civView.Cities().Count != 0) AutomateSettlerEscorting(civView);
        foreach (var unit in ordered)
        {
            if (!handled.Contains(unit.Unwrap())) Automate(unit);
        }
    }

    /// <summary>False once an earlier action in this pass has captured or destroyed <paramref name="unit"/>.</summary>
    private static bool IsStillOurUnit(MapUnitView unit, CivView civView) =>
        !unit.IsDestroyed() && unit.Civ().Equals(civView);

    internal static void ApplyPromotions(MapUnitView unitView)
    {
        var unit = unitView.Unwrap();
        // Restrict Human automated units from promotions via setting
        if (!unit.Civ.IsAI() && !unit.Civ.GameInfo.GetPlayerSettings(unit.Civ.CivId).AutomatedUnitsChoosePromotions) return;
        var rng = unit.Cache.State.StateBasedRandom("NextTurnAutomation.automateUnits");
        var stateForConditionals = unit.Cache.State;
        while (unitView.CanBePromoted())
        {
            var promotions = unit.Promotions.GetAvailablePromotions().ToList();
            List<Promotion> availablePromotions;
            if (unitView.UnitHealth <= 60
                && promotions.Any(it => ((IHasUniques)it).HasUnique(UniqueType.OneTimeUnitHeal))
                && !(unitView.IsAirUnit() || unitView.HasUnique(UniqueType.CanMoveAfterAttacking)))
                availablePromotions = promotions.Where(it => ((IHasUniques)it).HasUnique(UniqueType.OneTimeUnitHeal)).ToList();
            else availablePromotions = promotions.Where(it => !((IHasUniques)it).HasUnique(UniqueType.SkipPromotion)).ToList();

            if (availablePromotions.Count == 0) break;
            var freePromotions = availablePromotions.Where(it => ((IHasUniques)it).HasUnique(UniqueType.FreePromotion)).ToList();
            var chosenPromotion = freePromotions.Count != 0
                ? freePromotions.RandomWeighted(rng, it => ((IHasUniques)it).GetWeightForAiDecision(stateForConditionals))
                : availablePromotions.RandomWeighted(rng, it => ((IHasUniques)it).GetWeightForAiDecision(stateForConditionals));
            unitView.TryAddPromotion(chosenPromotion.Name);
        }
    }

    /// <summary>All units will continue after this to the regular automation, so units not moved in this function will still move</summary>
    private static void AutomateCityConquer(CivView civView, CityObject city, AutomationCache cache)
    {
        var civInfo = civView.GetCiv();
        List<MapUnitClass> OurUnitsInRange(int range) => city.GetCenterTile().GetTilesInDistance(range)
            .Select(it => it.MilitaryUnit).OfType<MapUnitClass>()
            .Where(it => it.Civ == civInfo && (!it.BaseUnit.IsMelee() || it.Health > 30)).ToList();

        void AttackIfPossible(MapUnitClass unit, TileClass tile)
        {
            var attackableTile = TargetHelper.GetAttackableEnemies(unit, unit.Movement.GetDistanceToTiles(), [tile]).FirstOrDefault();
            if (attackableTile != null)
                BattleClass.MoveAndAttack(new MapUnitCombatant(unit), attackableTile);
        }

        // Air units should do their thing before any of this
        foreach (var unit in OurUnitsInRange(7).Where(it => it.BaseUnit.IsAirUnit()))
            UnitAutomation.AutomateUnitMoves(ViewOf(civInfo).GetMapUnitView(unit), cache);

        // First off, any siege unit that can attack the city, should - as long as it's not defeated
        if (city.Health != 1)
        {
            var seigeUnits = OurUnitsInRange(4).Where(it => it.BaseUnit.IsProbablySiegeUnit()).ToList();
            foreach (var unit in seigeUnits)
            {
                if (!unit.HasUnique(UniqueType.MustSetUp) || unit.IsSetUpForSiege())
                    AttackIfPossible(unit, city.GetCenterTile());
            }
        }

        // Melee units should focus on getting rid of enemy units that threaten the siege units
        // If there are no units, this means attacking the city
        var meleeUnits = OurUnitsInRange(5).Where(it => it.BaseUnit.IsMelee()).ToList();
        foreach (var unit in meleeUnits.OrderByDescending(it => it.BaseUnit.GetForceEvaluation()))
        {
            // We're so close, full speed ahead!
            if (city.Health < city.GetMaxHealth() / 5) AttackIfPossible(unit, city.GetCenterTile());

            var tilesToTarget = city.GetCenterTile().GetTilesInDistance(4).ToList();

            var attackableEnemies = TargetHelper.GetAttackableEnemies(unit, unit.Movement.GetDistanceToTiles(), tilesToTarget);
            if (attackableEnemies.Count == 0) continue;
            // maxBy: the first maximum
            AttackableTile? mostSurroundedEnemy = null;
            var bestCount = 0;
            foreach (var enemy in attackableEnemies)
            {
                var count = 0;
                enemy.TileToAttack.ForEachTileAtDistance(1, tile =>
                {
                    if (tile.MilitaryUnit?.Civ == unit.Civ) count++;
                });
                if (mostSurroundedEnemy == null || count > bestCount)
                {
                    mostSurroundedEnemy = enemy;
                    bestCount = count;
                }
            } // aims to maximize flanking bonus and number of hits in order to get kills

            BattleClass.MoveAndAttack(new MapUnitCombatant(unit), mostSurroundedEnemy.NotNull());
        }
    }

    // Kotlin @VisibleForTesting
    public static void AutomateSettlerEscorting(CivView civView)
    {
        var civInfo = civView.GetCiv();
        var capitalTile = civInfo.GetCapital().NotNull().GetCenterTile();
        MapUnitClass? BestUnitInRange(TileClass tile, int range)
        {
            // sortedBy is stable, maxByOrNull takes the first maximum
            MapUnitClass? best = null;
            foreach (var it in tile.GetTilesInDistance(range)
                         .Select(t => t.MilitaryUnit).OfType<MapUnitClass>()
                         .Where(it => it.Civ == civInfo
                             && it.Health >= 100
                             // only draft a unit from the core of the empire, or it'll interfere with other anti-barb activities
                             && it.CurrentTile.AerialDistanceTo(capitalTile) < tile.AerialDistanceTo(capitalTile)
                             && it.Movement.CanReach(tile))
                         .OrderBy(it => it.CurrentTile.AerialDistanceTo(tile)))
            {
                if (best == null || it.BaseUnit.Strength > best.BaseUnit.Strength) best = it; // could be more sophisticated based on promotions, movement speed etc.
            }
            return best;
        }

        var settlersToAccompany = civInfo.Units.GetCivUnits()
            .Where(it => it.IsCivilian() && it.HasUnique(UniqueType.FoundCity)).ToList();

        foreach (var settler in settlersToAccompany)
        {
            var escortUnit = BestUnitInRange(settler.CurrentTile, 3);
            if (escortUnit == null) continue;
            escortUnit.Movement.HeadTowards(settler.CurrentTile);
            if (escortUnit.Movement.CanUnitSwapTo(settler.CurrentTile))
            {
                // check if we can swap to replace the current inferior (wounded) escort
                escortUnit.Movement.SwapMoveToTile(settler.CurrentTile);
            }
        }
    }

    /// <summary>Returns the priority of the unit, a lower value is higher priority</summary>
    public static int GetUnitPriority(MapUnitView unit, bool isAtWar, AutomationCache cache)
    {
        var mapUnit = unit.Unwrap();
        if (unit.HasUnique(UniqueType.SpaceshipPart)) return 0; // Move before everything else so we can swap
        if (unit.IsCivilian() && unit.HasUnique(UniqueType.FoundCity)) return 1; // Settlers before workers
        if (unit.IsCivilian() && !mapUnit.IsGreatPersonOfType("War")) return 2; // Civilian
        if (unit.IsAirUnit())
        {
            if (mapUnit.CanIntercept()) return 2; // Fighers first
            if (unit.IsNuclearWeapon()) return 3; // Then Nukes (area damage)
            if (!unit.HasUnique(UniqueType.SelfDestructs)) return 4; // Then Bombers (reusable)
            return 5; // Missiles
        }
        var distance = !isAtWar ? 0 : ThreatManager.GetDistanceToClosestEnemyUnit(unit.Civ(), cache, unit.GetTile(), 6);
        // Lower health units should move earlier to swap with higher health units
        int bonus;
        if (unit.GetBaseUnit().IsRanged()) bonus = 10;
        else if (unit.GetBaseUnit().IsMelee()) bonus = 30;
        else if (mapUnit.IsGreatPersonOfType("War")) bonus = 100; // Generals move after military units
        else bonus = 1;
        return unchecked(distance + unit.UnitHealth / 10 - mapUnit.Promotions.NumberOfPromotions + bonus);
    }
}
