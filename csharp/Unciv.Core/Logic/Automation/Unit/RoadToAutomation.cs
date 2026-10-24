using Unciv.Logic.Civilization;
using Unciv.Logic.Map.Tiles;
using Unciv.View;
using CivilizationClass = Unciv.Logic.Civilization.Civilization;
using MapUnitClass = Unciv.Logic.Map.MapUnits.MapUnit;
using NotificationCategory = Unciv.Logic.Civilization.Notification.NotificationCategory;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Logic.Automation.Unit;

// Ported from core/src/com/unciv/logic/automation/unit/RoadToAutomation.kt (T092). Purity annotations and Log.debug/debug calls dropped (T027 policy).

/// <summary>Responsible for automating the "build road to" action.</summary>
public class RoadToAutomation(CivilizationClass civInfo)
{
    private readonly RoadStatus actualBestRoadAvailable = civInfo.Tech.GetBestRoadAvailable();

    /// <summary>
    /// Automate the process of connecting a road between two points.
    /// Will be a special case of MapUnit.automated property: progress sequentially towards the end path; cancel upon being blocked or moved off the path;
    /// end automation upon finish. <paramref name="tilesWhereWeWillBeCaptured"/> is unused, as in Kotlin.
    /// </summary>
    public void AutomateConnectRoad(MapUnitClass unit, IReadOnlySet<TileView> tilesWhereWeWillBeCaptured)
    {
        _ = tilesWhereWeWillBeCaptured;
        if (actualBestRoadAvailable == RoadStatus.None) return;

        var currentTile = unit.GetTile();

        if (unit.AutomatedRoadConnectionDestination == null)
        {
            StopAndCleanAutomation(unit);
            return;
        }

        void Notify(string msg) =>
            unit.Civ.AddNotification(msg, new MapUnitAction(unit.GetTile().Position, unit.Id), NotificationCategory.Units, NotificationIcon.Construction);

        var destinationTile = unit.Civ.GameInfo.TileMap[unit.AutomatedRoadConnectionDestination.Value];

        var pathToDest = unit.AutomatedRoadConnectionPath;

        // The path does not exist, create it
        if (pathToDest == null)
        {
            var foundPath = unit.Movement.GetRoadPath(destinationTile);
            if (foundPath == null)
            {
                StopAndCleanAutomation(unit);
                Notify("Connect road failed!");
                return;
            }

            pathToDest = foundPath.Select(it => it.Position).ToList(); // Convert to a list of positions for serialization
            unit.AutomatedRoadConnectionPath = pathToDest;
        }

        var currTileIndex = pathToDest.IndexOf(currentTile.Position);

        // The worker was somehow moved off its path, cancel the action
        if (currTileIndex == -1)
        {
            StopAndCleanAutomation(unit);
            Notify("Connect road cancelled!");
            return;
        }

        /* Cannot build a road on this tile, try to move on.
         * The worker should search for the next furthest tile in the path that:
         * - It can move to
         * - Can be improved/upgraded */
        if (unit.HasMovement() && !ShouldBuildRoadOnTile(currentTile))
        {
            if (currTileIndex == pathToDest.Count - 1) // The last tile in the path is unbuildable or has a road.
            {
                StopAndCleanAutomation(unit);
                Notify("Connect road completed");
                return;
            }

            if (currTileIndex < pathToDest.Count - 1) // Try to move to the next tile in the path
            {
                var tileMap = unit.Civ.GameInfo.TileMap;
                var nextTile = currentTile;

                // The tiles with an index greater than currTileIndex
                var futureTiles = pathToDest.Skip(currTileIndex + 1).Select(it => tileMap[it]);

                foreach (var futureTile in futureTiles) // Find the furthest tile we can reach in this turn, move to, and does not have a road
                {
                    if (unit.Movement.CanReachInCurrentTurn(futureTile) && unit.Movement.CanMoveTo(futureTile)) // We can at least move to this tile
                    {
                        nextTile = futureTile;
                        if (ShouldBuildRoadOnTile(futureTile))
                            break; // Stop on this tile
                    }
                }

                if (nextTile.Position != currentTile.Position)
                {
                    unit.Movement.MoveToTile(nextTile);
                    currentTile = unit.GetTile();
                }
            }
        }

        // We need to check current movement again after we've (potentially) moved
        if (!unit.HasMovement()) return;
        // Repair pillaged roads first - try to build a new one if a mod removed repair
        var repairImprovement = currentTile.RoadStatus == RoadStatus.None || !currentTile.RoadIsPillaged
            ? null
            : civInfo.GameInfo.Ruleset.TileImprovements.GetValueOrDefault(Constants.Repair);
        var buildImprovement = !ShouldBuildRoadOnTile(currentTile) || currentTile.ImprovementInProgress == actualBestRoadAvailable.ToString()
            ? null
            : actualBestRoadAvailable.Improvement(civInfo.GameInfo.Ruleset);
        var improvement = repairImprovement ?? buildImprovement;
        if (improvement != null) currentTile.StartWorkingOnImprovement(improvement, civInfo, unit);
    }

    /// <summary>Reset side effects from automation, return worker to non-automated state.</summary>
    public void StopAndCleanAutomation(MapUnitClass unit)
    {
        unit.Automated = false;
        unit.Action = null;
        unit.AutomatedRoadConnectionDestination = null;
        unit.AutomatedRoadConnectionPath = null;
        if (unit.CurrentTile.GetTileImprovementInProgress()?.IsRoad() == true)
            unit.CurrentTile.StopWorkingOnImprovement();
    }

    /// <summary>Conditions for whether it is acceptable to build a road on this tile.</summary>
    public bool ShouldBuildRoadOnTile(TileClass tile)
    {
        if (tile.IsMarkedForCreatesOneImprovement()) return false;
        if (tile.RoadIsPillaged) return true;
        return !tile.IsCityCenter() // Can't build road on city tiles
                                    // Special case for civs that treat forest/jungles as roads (inside their territory). We shouldn't build if railroads aren't unlocked.
            && !(tile.HasConnection(civInfo) && actualBestRoadAvailable == RoadStatus.Road)
            && tile.RoadStatus != actualBestRoadAvailable; // Build (upgrade) if possible
    }
}
