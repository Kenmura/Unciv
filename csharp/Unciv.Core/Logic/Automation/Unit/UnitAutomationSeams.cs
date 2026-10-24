using Unciv.View;
using MapUnitClass = Unciv.Logic.Map.MapUnits.MapUnit;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Logic.Automation.Unit;

/// <summary>
/// What CivilianUnitAutomation and SpecificUnitAutomation (T091) call on the not-yet-ported civ's <c>WorkerAutomation</c> (owner: T092).
/// Set on <see cref="GameClientState.UnitAutomationSeams"/> by their ports; while it is null each call does nothing (booleans are false).
/// </summary>
public interface IUnitAutomationSeams
{
    /// <summary><c>civ.getWorkerAutomation().roadToAutomation.automateConnectRoad(unit, tilesWhereWeWillBeCaptured)</c>. TODO(T092)</summary>
    void AutomateConnectRoad(MapUnitClass unit, IReadOnlySet<TileView> tilesWhereWeWillBeCaptured);

    /// <summary><c>civ.getWorkerAutomation().automateWorkerAction(unitView, dangerousTiles)</c>. TODO(T092)</summary>
    void AutomateWorkerAction(MapUnitView unitView, HashSet<TileView> dangerousTiles);

    /// <summary><c>civ.getWorkerAutomation().automateWorkBoats(unitView)</c>. TODO(T092)</summary>
    bool AutomateWorkBoats(MapUnitView unitView);

    /// <summary><c>owner.getWorkerAutomation().getBasePriority(tile, unitView)</c>. TODO(T092)</summary>
    float GetBasePriority(TileClass tile, MapUnitView unitView);
}
