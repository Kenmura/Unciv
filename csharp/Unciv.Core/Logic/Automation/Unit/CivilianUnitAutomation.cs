using Unciv.Logic.Automation.Civilization;
using Unciv.Logic.Civilization.Managers;
using Unciv.Logic.Map.MapUnit.Actions;
using Unciv.Models;
using Unciv.Models.Ruleset.Unique;
using Unciv.View;
using MapUnitClass = Unciv.Logic.Map.MapUnits.MapUnit;

namespace Unciv.Logic.Automation.Unit;

// Ported from core/src/com/unciv/logic/automation/unit/CivilianUnitAutomation.kt (T091). Purity annotations and timeThis dropped (T027 policy).
// - Kotlin Sequences stay lazy (IEnumerable); minByOrNull returns the first minimum (MinBy).
public static class CivilianUnitAutomation
{
    public static bool ShouldClearTileForAddInCapitalUnits(MapUnitView unit, TileView tile) =>
        tile.IsCityCenter() && tile.GetCity()?.IsCapital() == true
        && !unit.HasUnique(UniqueType.AddInCapital)
        && unit.Civ().GetUnits().Any(it => it.HasUnique(UniqueType.AddInCapital));

    public static void AutomateCivilianUnit(MapUnitView unit, HashSet<TileView> dangerousTiles, AutomationCache cache)
    {
        // Temporary - logic-layer automation below still works on MapUnit
        var mapUnit = unit.Unwrap();

        // To allow "found city" actions that can only trigger a limited number of times

        // Slightly modified getUsableUnitActionUniques() to allow for settlers with *conditional* settling uniques
        bool HasSettlerAction(UniqueType uniqueType) =>
            mapUnit.GetMatchingUniques(uniqueType, GameContext.IgnoreConditionalsState)
                .Where(unique => !unique.HasModifier(UniqueType.UnitActionExtraLimitedTimes))
                .Any(unique => UnitActionModifiers.CanUse(mapUnit, unique));

        var hasSettlerUnique = HasSettlerAction(UniqueType.FoundCity) || HasSettlerAction(UniqueType.FoundPuppetCity);

        if (hasSettlerUnique && !(unit.Civ().IsCityState && unit.IsMilitary()))
        {
            SpecificUnitAutomation.AutomateSettlerActions(mapUnit, dangerousTiles, cache);
            return;
        }

        if (TryRunAwayIfNeccessary(unit, cache)) return;

        if (ShouldClearTileForAddInCapitalUnits(unit, unit.GetTile()))
        {
            // First off get out of the way, then decide if you actually want to do something else
            var tilesCanMoveTo = unit.GetDistanceToTiles()
                .Where(it => unit.CanMoveTo(it.Key))
                .ToList();
            if (tilesCanMoveTo.Count != 0)
                unit.TryMoveToTile(tilesCanMoveTo.MinBy(it => it.Value).Key);
        }

        if (unit.IsAutomatingRoadConnection())
        {
            mapUnit.Civ.GetWorkerAutomation().RoadToAutomation.AutomateConnectRoad(mapUnit, dangerousTiles);
            return;
        }

        if (unit.HasUniqueToBuildImprovements())
        {
            mapUnit.Civ.GetWorkerAutomation().AutomateWorkerAction(unit, dangerousTiles);
            return;
        }

        if (unit.HasUniqueToCreateWaterImprovements())
        {
            if (!mapUnit.Civ.GetWorkerAutomation().AutomateWorkBoats(unit))
                UnitAutomation.TryExplore(unit, cache);
            return;
        }

        if (TryAutomateFoundingAndEnhancingReligion(unit, mapUnit)) return;

        // We try to add any unit in the capital we can, though that might not always be desirable
        // For now its a simple option to allow AI to win a science victory again
        if (unit.HasUnique(UniqueType.AddInCapital))
        {
            SpecificUnitAutomation.AutomateAddInCapital(unit);
            return;
        }

        //todo this now supports "Great General"-like mod units not combining 'aura' and citadel
        // abilities, but not additional capabilities if automation finds no use for those two
        if (unit.HasStrengthBonusInRadiusUnique()
            && SpecificUnitAutomation.AutomateGreatGeneral(mapUnit))
            return;
        if (unit.HasCitadelPlacementUnique() && SpecificUnitAutomation.AutomateCitadelPlacer(mapUnit))
            return;

        if (mapUnit.Civ.ReligionManager.MaySpreadReligionAtAll(mapUnit))
        {
            ReligiousUnitAutomation.AutomateMissionary(unit);
            return;
        }

        if (unit.HasUnique(UniqueType.PreventSpreadingReligion) || unit.HasUnique(UniqueType.CanRemoveHeresy))
        {
            ReligiousUnitAutomation.AutomateInquisitor(unit);
            return;
        }

        if (TryAutomateLateGameAndWonderActions(unit, mapUnit)) return;

        if (unit.HasUnique(UniqueType.GainFreeBuildings))
        {
            AutomateGainFreeBuildings(unit, mapUnit);
            return;
        }

        // Kotlin todo: The AI tends to have a lot of great generals. Maybe there should be a cutoff
        //  (depending on number of cities) and after that they should just be used to start golden
        //  ages?

        if (SpecificUnitAutomation.AutomateImprovementPlacer(mapUnit)) return;

        var goldenAgeAction = UnitActions.GetUnitActions(mapUnit, UnitActionType.TriggerUnique)
            .Where(it => it.Action != null && it.AssociatedUnique?.Type is UniqueType.OneTimeEnterGoldenAge or UniqueType.OneTimeEnterGoldenAgeTurns)
            .FirstOrDefault();
        if (goldenAgeAction != null)
        {
            goldenAgeAction.Action?.Invoke();
            return;
        }

        // The AI doesn't know how to handle unknown civilian units
    }

    private static bool TryAutomateFoundingAndEnhancingReligion(MapUnitView unit, MapUnitClass mapUnit)
    {
        if (unit.HasUnique(UniqueType.MayFoundReligion)
            && mapUnit.Civ.ReligionManager.ReligionState < ReligionState.Religion
            && mapUnit.Civ.ReligionManager.MayFoundReligionAtAll())
        {
            ReligiousUnitAutomation.FoundReligion(unit);
            return true;
        }

        if (unit.HasUnique(UniqueType.MayFoundReligion) && unit.Civ().IsCityState)
        {
            // We have literally nothing to do with this unit, at least stop costing money
            unit.TryDisband();
            return true;
        }

        if (unit.HasUnique(UniqueType.MayEnhanceReligion)
            && mapUnit.Civ.ReligionManager.ReligionState < ReligionState.EnhancedReligion
            && mapUnit.Civ.ReligionManager.MayEnhanceReligionAtAll())
        {
            ReligiousUnitAutomation.EnhanceReligion(unit);
            return true;
        }

        return false;
    }

    private static bool TryAutomateLateGameAndWonderActions(MapUnitView unit, MapUnitClass mapUnit)
    {
        var isLateGame = IsLateGame(unit.Civ());
        // Great scientist -> Hurry research if late game
        // Great writer -> Hurry policy  if late game
        if (isLateGame)
        {
            var hurriedResearch = unit.TryInvokeUnitAction(UnitActionType.HurryResearch);
            if (hurriedResearch) return true;

            var hurriedPolicy = unit.TryInvokeUnitAction(UnitActionType.HurryPolicy);
            if (hurriedPolicy) return true;
            //Kotlin todo: save up great scientists/writers for late game (8 turns after research labs/broadcast towers resp.)
        }

        // Great merchant -> Conduct trade mission if late game and if not at war.
        // Kotlin todo: This could be more complex to walk to the city state that is most beneficial to
        //  also have more influence.
        if (unit.HasUnique(UniqueType.CanTradeWithCityStateForGoldAndInfluence)
            // There's a risk our merchant gets intercepted and killed by the enemy during war.
            // If such happens, it is a failure of our military unit movement to protect our merchant.
            // Barbs might also be a problem, but hopefully by the time we have a great merchant, they're under control.
            && isLateGame)
        {
            var tradeMissionCanBeConductedEventually = SpecificUnitAutomation.ConductTradeMission(mapUnit);
            if (tradeMissionCanBeConductedEventually)
                return true;
        }

        // Great engineer -> Try to speed up wonder construction
        if (unit.HasUnique(UniqueType.CanSpeedupConstruction)
            || unit.HasUnique(UniqueType.CanSpeedupWonderConstruction))
        {
            var wonderCanBeSpedUpEventually = SpecificUnitAutomation.SpeedupWonderConstruction(mapUnit);
            if (wonderCanBeSpedUpEventually)
                return true;
        }

        return false;
    }

    private static void AutomateGainFreeBuildings(MapUnitView unit, MapUnitClass mapUnit)
    {
        var unique = mapUnit.GetMatchingUniques(UniqueType.GainFreeBuildings).First();
        var buildingName = unique.Params[0];
        // Choose the city that is closest in distance and does not have the building constructed.
        var cityToGainBuilding = unit.Civ().Cities().Where(it =>
                !it.Unwrap().CityConstructions.ContainsBuildingOrEquivalent(buildingName)
                && (unit.CanMoveTo(it.GetCenterTile()) || unit.GetTile() == it.GetCenterTile()))
            .Select(it =>
            {
                var path = unit.GetShortestPath(it.GetCenterTile());
                // We want to calc path once, but still filter out unreachable cities
                return (City: it, Size: path.Count);
            })
            .Where(it => it.Size > 0)
            .OrderBy(it => it.Size) // stable: the first minimum, like minByOrNull
            .Select(it => it.City)
            .FirstOrDefault();

        if (cityToGainBuilding != null)
        {
            if (unit.GetTile() == cityToGainBuilding.GetCenterTile())
            {
                UniqueTriggerActivation.TriggerUnique(unique, mapUnit.Civ, unit: mapUnit, tile: mapUnit.CurrentTile);
                UnitActionModifiers.ActivateSideEffects(mapUnit, unique);
                return;
            }
            unit.TryHeadTowards(cityToGainBuilding.GetCenterTile());
        }
    }

    public static bool IsLateGame(CivView civ)
    {
        var technologies = civ.Ruleset.Technologies.Keys;
        var researchCompletePercent = technologies.Count(it => civ.IsResearched(it)) * 1.0f / technologies.Count();
        return researchCompletePercent >= 0.55f;
    }

    /// <returns>whether the civilian spends its turn hiding and not moving</returns>
    public static bool TryRunAwayIfNeccessary(MapUnitView unit, AutomationCache cache)
    {
        // This is a little 'Bugblatter Beast of Traal': Run if we can attack an enemy
        // Cheaper than determining which enemies could attack us next turn
        var enemyUnitsInWalkingDistance = unit.GetDistanceToTiles().Keys
            .Where(it => ThreatManager.DoesTileHaveMilitaryEnemy(unit.Civ(), it))
            .ToList();

        if (enemyUnitsInWalkingDistance.Count != 0 && !unit.GetBaseUnit().IsMilitary
            && unit.GetTile().MilitaryUnit == null && !unit.GetTile().IsCityCenter())
        {
            RunAway(unit, cache);
            return true;
        }

        return false;
    }

    private static void RunAway(MapUnitView unit, AutomationCache cache)
    {
        var mapUnit = unit.Unwrap();
        var reachableTiles = mapUnit.Movement.GetDistanceToTiles();
        var enterableCity = reachableTiles.Keys
            .FirstOrDefault(it => it.IsCityCenter() && mapUnit.Movement.CanMoveTo(it));
        if (enterableCity != null)
        {
            mapUnit.Movement.MoveToTile(enterableCity);
            return;
        }
        var defensiveUnit = reachableTiles.Keys
            .FirstOrDefault(it => it.MilitaryUnit != null && it.MilitaryUnit.Civ == mapUnit.Civ && it.CivilianUnit == null);
        if (defensiveUnit != null)
        {
            mapUnit.Movement.MoveToTile(defensiveUnit);
            return;
        }

        var dangerousTiles = ThreatManager.GetDangerousTiles(cache, unit);
        var unitTileView = unit.GetTile();
        var tileClosestToDanger = dangerousTiles
            // Priotirize capture threat over ranged attack
            .OrderByDescending(it => ThreatManager.GetEnemyUnitsOnTiles(unit.Civ(), [it]).Count != 0)
            .MinBy(it => it.AerialDistanceTo(unitTileView)) ?? unitTileView;
        var tileFurthestFromDanger = reachableTiles.Keys
            .Where(it =>
                mapUnit.Movement.CanMoveTo(it)
                && mapUnit.GetDamageFromTerrain(it) < unit.UnitHealth
                && !dangerousTiles.Contains(unitTileView.GetTileMap().GetTile(it)))
            .OrderByDescending(it => unitTileView.GetTileMap().GetTile(it).AerialDistanceTo(tileClosestToDanger)) // As far away from threat
            .ThenByDescending(it => it.IsFriendlyTerritory(mapUnit.Civ)) // Priotirize friendly territory
            .FirstOrDefault();
        if (tileFurthestFromDanger == null) return; // can't move anywhere!

        mapUnit.Movement.MoveToTile(tileFurthestFromDanger);
    }
}
