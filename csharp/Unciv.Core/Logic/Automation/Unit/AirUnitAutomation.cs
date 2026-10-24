using Unciv.Logic.Automation.Civilization;
using Unciv.Logic.Battle;
using Unciv.Logic.Civilization.Transients;
using Unciv.Models.Ruleset.Unique;
using Unciv.Utils;
using Unciv.View;
using BattleClass = Unciv.Logic.Battle.Battle;
using CityObject = Unciv.Logic.City.City;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Logic.Automation.Unit;

// Ported from core/src/com/unciv/logic/automation/unit/AirUnitAutomation.kt (T155). Purity annotations dropped (T027 policy).
// - Kotlin HashMap<TileView, ...> (tilesReached, pathsToCities) iterates in identity-hash order, which is not reproducible; they are
//   LinkedHashMaps here (insertion order), which is what makes the minBy/maxBy tie-breaks deterministic.
// - Kotlin maxByOrNull/minByOrNull return the first extreme element; LINQ MaxBy/MinBy and the loops below do the same.
// - Kotlin Int arithmetic wraps: getNukeLocationValue sums in an unchecked context.
public static class AirUnitAutomation
{
    public static void AutomateFighter(MapUnitView unitView, AutomationCache cache)
    {
        if (unitView.UnitHealth < 75) return; // Wait and heal

        var tilesWithEnemyUnitsInRange = ThreatManager.GetTilesWithEnemyUnitsInDistance(unitView.Civ(), cache, unitView.GetTile(), unitView.GetRange());
        // todo: Optimize [friendlyAirUnitsInRange] by creating an alternate [ThreatManager.getTilesWithEnemyUnitsInDistance] that handles only friendly units
        var friendlyAirUnitsInRange = unitView.GetTile().GetExploredTilesInDistance(unitView.GetRange())
            .SelectMany(it => it.GetVisibleUnits()).Where(it => it.IsAirUnit() && it.Civ().Equals(unitView.Civ())).ToList();
        // Find all visible enemy air units
        var enemyAirUnitsInRange = tilesWithEnemyUnitsInRange
            .SelectMany(it => it.GetVisibleUnits()).Where(it => it.IsAirUnit() && it.Civ().IsAtWarWith(unitView.Civ())).ToList();
        int enemyFighters = enemyAirUnitsInRange.Count / 2; // Assume half the planes are fighters
        int friendlyUnusedFighterCount = friendlyAirUnitsInRange.Count(it => it.CanAttack());
        int friendlyUsedFighterCount = friendlyAirUnitsInRange.Count(it => !it.CanAttack());

        // We need to be on standby in case they attack
        if (friendlyUnusedFighterCount < enemyFighters) return;

        if (friendlyUsedFighterCount <= enemyFighters)
        {
            int AirSweepDamagePercentBonus() =>
                unitView.GetMatchingUniques(UniqueType.StrengthWhenAirsweep).Aggregate(0, (sum, it) => unchecked(sum + it.Params[0].ToInt()));

            // If we are outnumbered, don't heal after attacking and don't have an Air Sweep bonus
            // Then we shouldn't speed the air battle by killing our fighters, instead, focus on defending
            if (friendlyUsedFighterCount + friendlyUnusedFighterCount < enemyFighters
                && !unitView.HasUnique(UniqueType.HealsEvenAfterAction)
                && AirSweepDamagePercentBonus() <= 0)
            {
                return;
            }
            else
            {
                if (TryAirSweep(unitView, tilesWithEnemyUnitsInRange)) return;
            }
        }

        if (unitView.UnitHealth < 80)
        {
            return; // Wait and heal up, no point in moving closer to battle if we aren't healed
        }

        if (BattleHelper.TryAttackNearbyEnemy(unitView)) return;

        if (unitView.CannotMove()) return; // from here on it's all "try to move somewhere else"

        if (TryRelocateToCitiesWithEnemyNearBy(unitView)) return;

        var pathsToCities = GetAerialPathsToCities(unitView);
        if (pathsToCities.Count == 0) return; // can't actually move anywhere else

        var citiesByNearbyAirUnits = pathsToCities.Keys
            .GroupBy(key =>
                key.GetExploredTilesInDistance(unitView.GetAirUnitMovementRange().NotNull()).Count(tile =>
                {
                    var firstAirUnit = tile.GetVisibleUnits().FirstOrDefault(it => it.IsAirUnit());
                    return firstAirUnit != null && firstAirUnit.Civ().IsAtWarWith(unitView.Civ());
                }))
            .ToList();

        if (citiesByNearbyAirUnits.Any(it => it.Key != 0))
        {
            var citiesWithMostNeedOfAirUnits = citiesByNearbyAirUnits.MaxBy(it => it.Key).NotNull().ToList();
            //todo: maybe group by size and choose highest priority within the same size turns
            var chosenCity = citiesWithMostNeedOfAirUnits.MinBy(it => pathsToCities[it].Count).NotNull(); // city with min path = least turns to get there
            var firstStepInPath = pathsToCities[chosenCity].First();
            unitView.TryMoveToTile(firstStepInPath);
            return;
        }

        // no city needs fighters to defend, so let's attack stuff from the closest possible location
        TryMoveToCitiesToAerialAttackFrom(pathsToCities, unitView);
    }

    private static bool TryAirSweep(MapUnitView unitView, List<TileView> tilesWithEnemyUnitsInRange)
    {
        var unitCiv = unitView.Civ();
        var targetTile = tilesWithEnemyUnitsInRange.Where(tile =>
                tile.GetVisibleUnits().Any(it => it.Civ().IsAtWarWith(unitCiv))
                || tile.GetCity()?.OwningCiv().IsAtWarWith(unitCiv) == true)
            .MinBy(it => it.AerialDistanceTo(unitView.GetTile()));
        if (targetTile == null) return false;
        unitView.TryAirSweep(targetTile);
        return !unitView.HasMovement();
    }

    public static void AutomateBomber(MapUnitView unitView)
    {
        if (unitView.UnitHealth < 75) return; // Wait and heal

        if (BattleHelper.TryAttackNearbyEnemy(unitView)) return;

        if (unitView.UnitHealth <= 90 || (unitView.UnitHealth < 100 && !unitView.Civ().IsAtWar()))
        {
            return; // Wait and heal
        }

        if (unitView.CannotMove()) return; // from here on it's all "try to move somewhere else"

        if (TryRelocateToCitiesWithEnemyNearBy(unitView)) return;

        var pathsToCities = GetAerialPathsToCities(unitView);
        if (pathsToCities.Count == 0) return; // can't actually move anywhere else
        TryMoveToCitiesToAerialAttackFrom(pathsToCities, unitView);
    }

    private static void TryMoveToCitiesToAerialAttackFrom(LinkedHashMap<TileView, List<TileView>> pathsToCities, MapUnitView airUnitView)
    {
        var citiesThatCanAttackFrom = pathsToCities.Keys
            .Where(destinationCity =>
                !destinationCity.Equals(airUnitView.GetTile())
                    && destinationCity.GetExploredTilesInDistance(airUnitView.GetRange())
                    .Any(airUnitView.ContainsAttackableEnemy))
            .ToList();
        if (citiesThatCanAttackFrom.Count == 0) return;

        //todo: this logic looks similar to some parts of automateFighter, maybe pull out common code
        //todo: maybe group by size and choose highest priority within the same size turns
        var closestCityThatCanAttackFrom =
            citiesThatCanAttackFrom.MinBy(it => pathsToCities[it].Count).NotNull();
        var firstStepInPath = pathsToCities[closestCityThatCanAttackFrom].First();
        airUnitView.TryMoveToTile(firstStepInPath);
    }

    public static void AutomateNukes(MapUnitView unitView)
    {
        var unit = unitView.Unwrap();
        if (!unitView.Civ().IsAtWar()) return;
        // We should *Almost* never want to nuke our own city, so don't consider it
        if (unit.Type.IsAirUnit())
        {
            var tilesInRange = unit.CurrentTile.GetTilesInDistanceRange(new IntRange(2, unit.GetRange()));
            (TileClass Tile, int Value)? highestTileNukeValue = null;
            foreach (var tile in tilesInRange)
            {
                int value = GetNukeLocationValue(unitView, tile);
                if (highestTileNukeValue == null || value > highestTileNukeValue.Value.Value) highestTileNukeValue = (tile, value);
            }
            if (highestTileNukeValue != null && highestTileNukeValue.Value.Value > 0)
                Nuke.NUKE(new MapUnitCombatant(unit), highestTileNukeValue.Value.Tile);

            TryRelocateMissileToNearbyAttackableCities(unitView);
        }
        else
        {
            var attackableTiles = TargetHelper.GetAttackableEnemies(unit, unit.Movement.GetDistanceToTiles());
            (AttackableTile Tile, int Value)? highestTileNukeValue = null;
            foreach (var attackable in attackableTiles)
            {
                int value = GetNukeLocationValue(unitView, attackable.TileToAttack);
                if (highestTileNukeValue == null || value > highestTileNukeValue.Value.Value) highestTileNukeValue = (attackable, value);
            }
            if (highestTileNukeValue != null && highestTileNukeValue.Value.Value > 0)
                BattleClass.MoveAndAttack(new MapUnitCombatant(unit), highestTileNukeValue.Value.Tile);
            HeadTowardsEnemyCityAutomation.TryHeadTowardsEnemyCity(unitView);
        }
    }

    /// <summary>
    /// Ranks the tile to nuke based off of all tiles in it's blast radius
    /// By default the value is -500 to prevent inefficient nuking.
    /// </summary>
    private static int GetNukeLocationValue(MapUnitView nukeView, TileClass tile)
    {
        var nuke = nukeView.Unwrap();
        var civ = nuke.Civ;
        if (!Nuke.MayUseNuke(new MapUnitCombatant(nuke), tile)) return int.MinValue;
        int blastRadius = nuke.GetNukeBlastRadius();
        var tilesInBlastRadius = tile.GetTilesInDistance(blastRadius).ToList();
        var civsInBlastRadius = tilesInBlastRadius.Select(it => (CivilizationObject?)it.GetOwner()).OfType<CivilizationObject>()
            .Concat(tilesInBlastRadius.Select(it => it.GetFirstUnit()?.Civ).OfType<CivilizationObject>()).ToList();

        // Don't nuke if it means we will be declaring war on someone!
        if (civsInBlastRadius.Any(it => it != civ && !it.IsAtWarWith(civ))) return -100000;
        // If there are no enemies to hit, don't nuke
        if (!civsInBlastRadius.Any(it => it.IsAtWarWith(civ))) return -100000;

        // Launching a Nuke uses resources, therefore don't launch it by default
        int explosionValue = -500;

        // Returns either ourValue or thierValue depending on if the input Civ matches the Nuke's Civ
        int EvaluateCivValue(CivilizationObject targetCiv, int ourValue, int theirValue)
        {
            if (targetCiv == civ) // We are nuking something that we own!
                return ourValue;
            return theirValue; // We are nuking an enemy!
        }

        unchecked
        {
            foreach (var targetTile in tilesInBlastRadius)
            {
                // We can only account for visible units
                if (targetTile.IsVisible(civ))
                {
                    foreach (var targetUnit in targetTile.GetUnits())
                    {
                        if (!targetUnit.IsVisibleTo(civ)) continue;
                        // If we are nuking a unit at ground zero, it is more likely to be destroyed
                        int tileExplosionValue = targetTile == tile ? 80 : 50;

                        if (targetUnit.IsMilitary())
                        {
                            explosionValue += targetTile == tile ? EvaluateCivValue(targetUnit.Civ, -200, tileExplosionValue)
                                : EvaluateCivValue(targetUnit.Civ, -150, 50);
                        }
                        else if (targetUnit.IsCivilian())
                        {
                            explosionValue += EvaluateCivValue(targetUnit.Civ, -100, tileExplosionValue / 2);
                        }
                    }
                }
                // Never nuke our own Civ, don't nuke single enemy civs as well
                if (targetTile.IsCityCenter()
                    && !(((CityObject)targetTile.GetCity().NotNull()).Health <= 50f
                        && targetTile.Neighbors.Any(it => it.MilitaryUnit?.Civ == civ))) // Prefer not to nuke cities that we are about to take
                {
                    explosionValue += EvaluateCivValue(((CityObject)targetTile.GetCity().NotNull()).Civ, -100000, 250);
                }
                else if (targetTile.OwningCity != null)
                {
                    var owningCiv = ((CityObject)targetTile.OwningCity).Civ;
                    // If there is a tile to add fallout to there is a 50% chance it will get fallout
                    if (!(targetTile.IsWater || targetTile.IsImpassible() || targetTile.HasFalloutEquivalent()))
                        explosionValue += EvaluateCivValue(owningCiv, -40, 10);
                    // If there is an improvment to pillage
                    if (targetTile.Improvement != null && !targetTile.ImprovementIsPillaged)
                        explosionValue += EvaluateCivValue(owningCiv, -40, 20);
                }
                // If the value is too low end the search early
                if (explosionValue < -1000) return explosionValue;
            }
        }
        return explosionValue;
    }

    // This really needs to be changed, to have better targeting for missiles
    public static void AutomateMissile(MapUnitView unitView)
    {
        if (BattleHelper.TryAttackNearbyEnemy(unitView)) return;
        TryRelocateMissileToNearbyAttackableCities(unitView);
    }

    private static void TryRelocateMissileToNearbyAttackableCities(MapUnitView unitView)
    {
        var tilesInRange = unitView.GetTile().GetExploredTilesInDistance(unitView.GetRange());
        var immediatelyReachableCities = tilesInRange
            .Where(unitView.CanMoveTo);

        foreach (var city in immediatelyReachableCities)
        {
            if (city.GetExploredTilesInDistance(unitView.GetRange())
                    .Any(it => it.IsCityCenter() && it.GetOwner()?.IsAtWarWith(unitView.Civ()) == true))
            {
                unitView.TryMoveToTile(city);
                return;
            }
        }

        var pathsToCities = GetAerialPathsToCities(unitView);
        if (pathsToCities.Count == 0) return; // can't actually move anywhere else
        TryMoveToCitiesToAerialAttackFrom(pathsToCities, unitView);
    }

    private static bool TryRelocateToCitiesWithEnemyNearBy(MapUnitView unitView)
    {
        var movementRange = unitView.GetAirUnitMovementRange();
        if (movementRange == null) return false;
        var immediatelyReachableCitiesAndCarriers = unitView.GetTile()
            .GetExploredTilesInDistance(movementRange.Value).Where(unitView.CanMoveTo);

        foreach (var city in immediatelyReachableCitiesAndCarriers)
        {
            if (city.GetExploredTilesInDistance(unitView.GetRange())
                    .Any(it => it.IsVisible() && unitView.ContainsAttackableEnemy(it)))
            {
                unitView.TryMoveToTile(city);
                return true;
            }
        }
        return false;
    }

    /// <summary>Aerial equivalent of UnitMovement.getMovementToTilesAtPosition: for each tile the air unit can reach by hopping, the path of tiles to it</summary>
    private static LinkedHashMap<TileView, List<TileView>> GetAerialPathsToCities(MapUnitView unitView)
    {
        var tilesToCheck = new List<TileView>();
        /* each tile reached points to its parent tile, where we got to it from */
        var tilesReached = new LinkedHashMap<TileView, TileView>();

        var startingTile = unitView.GetTile();
        tilesToCheck.Add(startingTile);
        tilesReached.Put(startingTile, startingTile);

        while (tilesToCheck.Count != 0)
        {
            var newTilesToCheck = new List<TileView>();
            foreach (var currentTileToCheck in tilesToCheck)
            {
                foreach (var reachableTile in currentTileToCheck.GetExploredTilesInDistance(unitView.GetRange()).Where(unitView.CanMoveTo).ToList())
                {
                    if (!tilesReached.ContainsKey(reachableTile))
                    {
                        tilesReached.Put(reachableTile, currentTileToCheck);
                        newTilesToCheck.Add(reachableTile);
                    }
                }
            }
            tilesToCheck = newTilesToCheck;
        }

        var pathsToCities = new LinkedHashMap<TileView, List<TileView>>();

        foreach (var city in tilesReached.Keys)
        {
            var path = new List<TileView>();
            var currentCity = city;
            while (!currentCity.Equals(startingTile)) // we don't add the "starting tile" to the arraylist
            {
                path.Add(currentCity);
                currentCity = tilesReached[currentCity]; // go to parent
            }
            path.Reverse();
            pathsToCities.Put(city, path);
        }

        pathsToCities.Remove(startingTile);
        return pathsToCities;
    }
}
