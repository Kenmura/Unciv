using Unciv.Logic.Automation.Unit;
using Unciv.View;

namespace Unciv.Logic.Map.MapUnits;

public partial class MapUnit
{
    private void AutomateUnitMoves() =>
        UnitAutomation.AutomateUnitMoves(new GameView((GameInfo)Civ.GameInfo, Civ).GetMapUnitView(this));

    private void AutomatedExplore() =>
        UnitAutomation.AutomatedExplore(new GameView((GameInfo)Civ.GameInfo, Civ).GetMapUnitView(this));
}
