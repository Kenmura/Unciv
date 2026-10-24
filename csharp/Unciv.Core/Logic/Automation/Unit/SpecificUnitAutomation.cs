using Unciv.Logic.Automation.Civilization;
using Unciv.Logic.Battle;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Logic.Map.MapUnit.Actions;
using Unciv.Models;
using Unciv.Models.Ruleset.Unique;
using Unciv.Utils;
using Unciv.View;
using CityObject = Unciv.Logic.City.City;
using CivilizationClass = Unciv.Logic.Civilization.Civilization;
using MapUnitClass = Unciv.Logic.Map.MapUnits.MapUnit;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Logic.Automation.Unit;

// Ported from core/src/com/unciv/logic/automation/unit/SpecificUnitAutomation.kt (T091). Purity annotations dropped (T027 policy).
// - Kotlin Sequences stay lazy (IEnumerable); sortedBy/sortedByDescending are stable (OrderBy/OrderByDescending); minBy/maxBy return the first extremum.
// - Kotlin `roundToInt` of getBasePriority is half-up (KotlinMath.RoundToInt).
public static partial class SpecificUnitAutomation
{
    public static bool AutomateGreatGeneral(MapUnitClass unit)
    {
        //try to follow nearby units. Do not garrison in city if possible
        var maxAffectedTroopsTile = GreatGeneralImplementation.GetBestAffectedTroopsTile(unit);
        if (maxAffectedTroopsTile == null) return false;

        unit.Movement.HeadTowards(maxAffectedTroopsTile);
        return true;
    }

    public static bool AutomateCitadelPlacer(MapUnitClass unit)
    {
        // Keep at least 2 generals alive
        if (unit.HasUnique(UniqueType.StrengthBonusInRadius)
            && unit.Civ.Units.GetCivUnits().Count(it => it.HasUnique(UniqueType.StrengthBonusInRadius)) < 3)
            return false;
        // try to revenge and capture their tiles
        var enemyCities = unit.Civ.GetKnownCivs()
            .Where(it => unit.Civ.GetDiplomacyManager(it).NotNull().HasModifier(DiplomaticModifiers.StealingTerritory))
            .SelectMany(it => it.Cities);
        // find the suitable tiles (or their neighbours)
        var tileToSteal = enemyCities.SelectMany(it => it.GetTiles()) // City tiles
            .Where(it => it.Neighbors.Any(tile => tile.GetOwner() != unit.Civ)) // Edge city tiles
            .SelectMany(it => it.Neighbors) // Neighbors of edge city tiles
            .Where(it =>
                unit.Civ.ViewableTiles.Contains(it) // we can see them
                && it.Neighbors.Any(tile => tile.GetOwner() == unit.Civ)) // they are close to our borders
            .OrderBy(it =>
            {
                // get closest tiles
                var distance = it.AerialDistanceTo(unit.CurrentTile);
                // ...also get priorities to steal the most valuable for them
                var owner = it.GetOwner();
                if (owner != null)
                    return distance - KotlinMath.RoundToInt(((CivilizationClass)owner).GetWorkerAutomation().GetBasePriority(it, new GameView((GameInfo)unit.Civ.GameInfo, unit.Civ).GetMapUnitView(unit)));
                return distance;
            })
            .FirstOrDefault(it => unit.Movement.CanReach(it)); // canReach is performance-heavy and always a last resort
        // if there is a good tile to steal - go there
        if (tileToSteal != null)
        {
            unit.Movement.HeadTowards(tileToSteal);
            if (unit.HasMovement() && unit.CurrentTile == tileToSteal)
                UnitActionsFromUniques.GetImprovementConstructionActionsFromGeneralUnique(unit, unit.CurrentTile).FirstOrDefault()?.Action?.Invoke();
            return true;
        }

        // keep in mind that citadels can be stolen by other citadels.
        // you need 1 more general than your opponent, or be able to place it on a totally unreachable tile,
        // for it to make sense to make the first move as the defender.
        // so, let's focus only on the stealing part for now (the old defensive citadel placement logic turned out negative in simulation result)

        return false;
    }

    public static void AutomateSettlerActions(MapUnitClass unit, HashSet<TileView> dangerousTiles, AutomationCache cache)
    {
        var gameView = new GameView((GameInfo)unit.Civ.GameInfo, unit.Civ);

        // City-state starts are predetermined by map gen / editor — trust that tile by default.
        // Mods can opt out via CityStatesSearchForFirstCitySite.
        if (unit.Civ.IsCityState && unit.Civ.Cities.Count == 0
            && !((IHasUniques)unit.Civ.GameInfo.Ruleset.ModOptions).HasUnique(UniqueType.CityStatesSearchForFirstCitySite))
        {
            var foundHere = UnitActionsFromUniques.GetFoundCityAction(unit, unit.GetTile());
            if (foundHere?.Action != null && unit.HasMovement())
            {
                foundHere.Action.Invoke();
                return;
            }
            // Cannot found here (blocked / invalid) — fall through to normal search.
        }

        // If we don't have any cities, we are probably at the start of the game with only one settler
        // If we are at the start of the game lets spend a maximum of 3 turns to settle our first city
        // As our turns progress lets shrink the area that we look at to make sure that we stay on target
        // If we have gone more than 3 turns without founding a city lets search a wider area
        // Kotlin todo: Figure out a way to not use turns as it might not be the best metric, what if we are a civ starting in the middle of a game?
        int? rangeToSearch = unit.Civ.Cities.Count == 0 && unit.Civ.GameInfo.Turns < 4 ? Math.Max(3 - unit.Civ.GameInfo.Turns, 1) : null;

        // It's possible that we'll see a tile "over the sea" that's better than the tiles close by, but that's not a reason to abandon the close tiles!
        // Also this lead to some routing problems, see https://github.com/yairm210/Unciv/issues/3653
        var minimumTileValue = unit.Civ.GameInfo.Ruleset.ModOptions.Constants.MinimumCityLocationTileValue; // To prevent AI from settling snow regions, could use a better implementation
        var bestTilesInfo = CityLocationTileRanker.GetBestTilesToFoundCity(unit, rangeToSearch, minimumTileValue);
        TileClass? bestCityLocation = null;

        if (unit.Civ.GameInfo.Turns == 0 && unit.Civ.Cities.Count == 0 && bestTilesInfo.TileRankMap.ContainsKey(unit.GetTile())) // Special case, we want AI to settle in place on turn 1.
        {
            var foundCityAction = UnitActionsFromUniques.GetFoundCityAction(unit, unit.GetTile());
            // Depending on era and difficulty we might start with more than one settler. In that case settle the one with the best location
            var allUnsettledSettlers = unit.Civ.Units.GetCivUnits().Where(it => it.HasMovement() && it.BaseUnit == unit.BaseUnit);

            // Don't settle immediately if we only have one settler, look for a better location
            var bestSettlerInRange = allUnsettledSettlers.MaxBy(it =>
                bestTilesInfo.TileRankMap.TryGetValue(it.GetTile(), out var rank) ? rank : -1f);
            if (bestSettlerInRange == unit && foundCityAction?.Action != null)
            {
                foundCityAction.Action.Invoke();
                return;
            }
            // Since this settler is not in the best location, lets assume the best settler will found their city where they are
            if (bestSettlerInRange != null)
            {
                var filtered = new LinkedHashMap<TileClass, float>();
                foreach (var (key, value) in bestTilesInfo.TileRankMap)
                    if (key.AerialDistanceTo(bestSettlerInRange.GetTile()) > 4)
                        filtered[key] = value;
                bestTilesInfo.TileRankMap = filtered;
            }
        }

        // If the tile we are currently on is close to the best tile, then lets just settle here instead
        if (bestTilesInfo.TileRankMap.ContainsKey(unit.GetTile())
            && (bestTilesInfo.BestTile == null || bestTilesInfo.TileRankMap[unit.GetTile()] >= bestTilesInfo.BestTileRank - 2))
            bestCityLocation = unit.GetTile();

        //Shortcut, if the best tile is nearby than lets just take it
        if (bestCityLocation == null && bestTilesInfo.BestTile != null && unit.Movement.GetShortestPath(bestTilesInfo.BestTile).Count is >= 1 and <= 3)
            bestCityLocation = bestTilesInfo.BestTile;

        if (bestCityLocation == null)
        {
            // Find the best tile that is within
            bool IsTileRankOK(KeyValuePair<TileClass, float> it)
            {
                if (gameView.GetMapUnitView(unit).GetTile().GetTileMap().GetTile(it.Key) is var tileView && dangerousTiles.Contains(tileView) && it.Key != unit.GetTile()) return false;
                var pathSize = unit.Movement.GetShortestPath(it.Key).Count;
                return pathSize is >= 1 and <= 3;
            }

            bestCityLocation = bestTilesInfo.TileRankMap
                .Where(it => bestTilesInfo.BestTile == null || it.Value >= bestTilesInfo.BestTileRank - 5)
                .OrderByDescending(it => it.Value)
                .Where(IsTileRankOK)
                .Select(it => (TileClass?)it.Key)
                .FirstOrDefault();
        }

        // We still haven't found a best city tile within 3 turns so lets just head to the best tile
        // Note that we must check that the shortest path exists or else an error will be thrown
        if (bestCityLocation == null && bestTilesInfo.BestTile != null && unit.Movement.GetShortestPath(bestTilesInfo.BestTile).Count is >= 1 and <= 8)
            bestCityLocation = bestTilesInfo.BestTile;

        if (bestCityLocation == null)
        { // We got a badass over here, all tiles within 5 are taken?
            // Try to move towards the frontier

            var frontierCity = unit.Civ.Cities.MaxBy(it => GetFrontierScore(it, unit.Civ));
            if (frontierCity != null && GetFrontierScore(frontierCity, unit.Civ) > 0 && unit.Movement.CanReach(frontierCity.GetCenterTile()))
                unit.Movement.HeadTowards(frontierCity.GetCenterTile());
            var unitView = gameView.GetMapUnitView(unit);
            if (UnitAutomation.TryExplore(unitView, cache)) return; // try to find new areas
            UnitAutomation.Wander(unitView, tilesToAvoid: dangerousTiles); // go around aimlessly
            return;
        }

        var foundCityAction2 = UnitActionsFromUniques.GetFoundCityAction(unit, bestCityLocation);
        if (foundCityAction2?.Action == null)
        { // this means either currentMove == 0 or city within 3 tiles
            if (unit.HasMovement() && !unit.Civ.IsOneCityChallenger()) // therefore, city within 3 tiles
                throw new Exception("City within distance");
            return;
        }

        var shouldSettle = unit.GetTile() == bestCityLocation && unit.HasMovement();
        if (shouldSettle) { foundCityAction2.Action.Invoke(); return; }
        //Settle if we're already on the best tile, before looking if we should retreat from barbarians
        if (CivilianUnitAutomation.TryRunAwayIfNeccessary(gameView.GetMapUnitView(unit), cache)) return;
        unit.Movement.HeadTowards(bestCityLocation);
        var shouldSettleNow = unit.GetTile() == bestCityLocation && unit.HasMovement();
        if (shouldSettleNow) foundCityAction2.Action.Invoke();
    }

    // the number of tiles 4 (un-modded) out from this city that could hold a city, ie how lonely this city is
    private static int GetFrontierScore(CityObject city, CivilizationClass civ)
    {
        var frontierScore = 0;
        city.GetCenterTile().ForEachTileAtDistance(city.Civ.GameInfo.Ruleset.ModOptions.Constants.MinimalCityDistance + 1, it =>
        {
            if (it.CanBeSettled(civ)) frontierScore++;
        });
        return frontierScore;
    }

    public static void AutomateAddInCapital(MapUnitView unit)
    {
        var capitalTile = unit.Civ().Cities().FirstOrDefault(it => it.IsCapital())?.GetCenterTile(); // safeguard
        if (capitalTile == null) return;
        if (unit.CanReach(capitalTile))
            unit.TryHeadTowards(capitalTile);
        if (unit.CanSwapTo(capitalTile))
            unit.TrySwapMoveToTile(capitalTile);
        if (unit.GetTile() == capitalTile)
            unit.TryInvokeUnitAction(UnitActionType.AddInCapital);
    }
}
